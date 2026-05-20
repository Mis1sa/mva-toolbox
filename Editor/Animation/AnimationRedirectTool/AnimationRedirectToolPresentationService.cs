using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.AnimationRedirectTool
{
    internal sealed class AnimationRedirectToolPresentationService
    {
        private readonly AnimationRedirectToolService _service;

        internal AnimationRedirectToolPresentationService(AnimationRedirectToolService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        internal AnimationRedirectSummary BuildSummary()
        {
            IReadOnlyList<AnimationRedirectToolService.MissingObjectGroup> missingGroups = _service.MissingGroups;
            int totalMissingCount = missingGroups.Sum(group => group.CurvesByType.Sum(kvp => kvp.Value.Count(curve => !curve.IsMarkedForRemoval)));
            int ignorablesCount = missingGroups.Count(group => group.FixTarget == null && !group.IsEmpty);
            int pathChangeCount = _service.PathChangeGroups.Count(group => group.IsDeleted || group.HasPathChanged);
            int componentChangeCount = _service.ComponentChangeGroups.Sum(group => group.Bindings.Count);
            int activeMissingCount = missingGroups.Sum(group =>
                (group.OwnerDeleted || (_service.IgnoreAllMissing && group.FixTarget == null))
                    ? 0
                    : group.CurvesByType.Sum(kvp => kvp.Value.Count(curve => !curve.IsMarkedForRemoval)));
            int removalOnlyCount = missingGroups.Sum(group => group.CurvesByType.Sum(kvp => kvp.Value.Count(curve => curve.IsMarkedForRemoval)));
            int totalChanges = pathChangeCount + componentChangeCount + activeMissingCount + removalOnlyCount;

            if (pathChangeCount > 0 && !_service.HierarchyChanged)
            {
                _service.HierarchyChanged = true;
            }

            bool canAutoMatch = missingGroups.Any(group =>
                group != null &&
                !group.OwnerDeleted &&
                !group.IsEmpty &&
                group.FixTarget == null &&
                !(_service.IgnoreAllMissing && group.FixTarget == null));

            bool needsComponentFix = missingGroups
                .Where(group => group.FixTarget != null && !group.IsEmpty)
                .Any(group => !ValidateFixTargetComponentsForDisplay(group));

            return new AnimationRedirectSummary(
                _service.HasSnapshot,
                totalMissingCount,
                ignorablesCount,
                pathChangeCount,
                componentChangeCount,
                activeMissingCount,
                removalOnlyCount,
                totalChanges,
                canAutoMatch,
                needsComponentFix,
                totalChanges > 0 && !needsComponentFix,
                _service.HierarchyChanged);
        }

        internal IReadOnlyList<AnimationRedirectToolService.MissingObjectGroup> GetVisibleMissingGroups()
        {
            return _service.MissingGroups
                .Where(group => !group.OwnerDeleted && !group.IsEmpty && !(_service.IgnoreAllMissing && group.FixTarget == null))
                .OrderBy(group => group.IsFixed)
                .ToList();
        }

        internal MissingGroupDisplayInfo GetDisplayInfo(AnimationRedirectToolService.MissingObjectGroup group)
        {
            if (group == null)
            {
                return default;
            }

            GameObject root = _service.TargetRoot;
            string displayName = group.TargetObjectName;
            string displayPath = string.IsNullOrEmpty(group.CurrentPath) ? group.OldPath : group.CurrentPath;

            if (group.FixTarget != null && root != null)
            {
                GameObject fixGo = group.FixTarget as GameObject ?? (group.FixTarget as Component)?.gameObject;
                if (fixGo != null)
                {
                    displayName = fixGo.name;
                    displayPath = fixGo == root ? string.Empty : AnimationUtility.CalculateTransformPath(fixGo.transform, root.transform) ?? displayPath;
                }
            }

            return new MissingGroupDisplayInfo(displayName, displayPath);
        }

        internal IReadOnlyList<MissingTypeSectionView> BuildTypeSections(AnimationRedirectToolService.MissingObjectGroup group)
        {
            List<MissingTypeSectionView> sections = new List<MissingTypeSectionView>();
            if (group == null)
            {
                return sections;
            }

            foreach (KeyValuePair<Type, List<AnimationRedirectToolService.MissingCurveEntry>> kvp in group.CurvesByType)
            {
                Type type = kvp.Key;
                List<AnimationRedirectToolService.MissingCurveEntry> curves = kvp.Value;
                List<AnimationRedirectToolService.MissingCurveEntry> activeCurves = curves.Where(curve => !curve.IsMarkedForRemoval).ToList();
                if (activeCurves.Count == 0)
                {
                    continue;
                }

                List<IGrouping<string, AnimationRedirectToolService.MissingCurveEntry>> aggregated = activeCurves
                    .GroupBy(GetAggregationKeyForProperty)
                    .OrderBy(grouping => GetCategoryOrder(grouping.First()))
                    .ThenBy(grouping => GetDisplayNameForProperty(grouping.First()))
                    .ToList();

                List<MissingPropertyGroupView> propertyGroups = new List<MissingPropertyGroupView>();
                foreach (IGrouping<string, AnimationRedirectToolService.MissingCurveEntry> grouping in aggregated)
                {
                    List<AnimationRedirectToolService.MissingCurveEntry> entries = grouping.ToList();
                    AnimationRedirectToolService.MissingCurveEntry representative = entries[0];

                    string labelText;
                    if (representative.IsBlendshape)
                    {
                        labelText = $"[BlendShape] {GetDisplayNameForProperty(representative)}";
                    }
                    else
                    {
                        string baseName = GetComponentBaseName(representative.Binding.propertyName ?? string.Empty);
                        labelText = $"[{GetShortName(baseName)}]";
                    }

                    bool hasActiveEntries = entries.Any(entry => !entry.IsMarkedForRemoval);
                    bool allFixed = hasActiveEntries && group.FixTarget != null && entries.All(entry => entry.IsFixedByGroup && !entry.IsMarkedForRemoval);
                    IReadOnlyList<string> blendshapeOptions = representative.AvailableBlendshapes ?? new List<string>();
                    int currentBlendshapeIndex = -1;
                    if (blendshapeOptions.Count > 0)
                    {
                        for (int optionIndex = 0; optionIndex < blendshapeOptions.Count; optionIndex++)
                        {
                            if (string.Equals(blendshapeOptions[optionIndex], representative.NewBlendshapeName, StringComparison.Ordinal))
                            {
                                currentBlendshapeIndex = optionIndex;
                                break;
                            }
                        }

                        currentBlendshapeIndex = Math.Max(0, currentBlendshapeIndex);
                    }

                    propertyGroups.Add(new MissingPropertyGroupView(
                        labelText,
                        hasActiveEntries,
                        allFixed,
                        representative.IsBlendshape,
                        entries,
                        blendshapeOptions,
                        currentBlendshapeIndex));
                }

                sections.Add(new MissingTypeSectionView(type, aggregated.Sum(grouping => grouping.Count()), curves, propertyGroups));
            }

            return sections;
        }

        internal void MarkEntriesForRemoval(AnimationRedirectToolService.MissingObjectGroup group, IEnumerable<AnimationRedirectToolService.MissingCurveEntry> entries)
        {
            if (group == null || entries == null)
            {
                return;
            }

            foreach (AnimationRedirectToolService.MissingCurveEntry entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                entry.IsMarkedForRemoval = true;
                entry.IsFixedByGroup = false;
            }

            _service.UpdateFixTargetStatus(group);
        }

        internal void ApplyBlendshapeSelection(MissingPropertyGroupView propertyGroup, int optionIndex)
        {
            if (propertyGroup == null || propertyGroup.BlendshapeOptions == null || propertyGroup.BlendshapeOptions.Count == 0)
            {
                return;
            }

            int clamped = Mathf.Clamp(optionIndex, 0, propertyGroup.BlendshapeOptions.Count - 1);
            string selected = propertyGroup.BlendshapeOptions[clamped];
            foreach (AnimationRedirectToolService.MissingCurveEntry entry in propertyGroup.Entries)
            {
                entry.NewBlendshapeName = selected;
            }
        }

        internal void UpdateFixTarget(AnimationRedirectToolService.MissingObjectGroup group, Object newFixTarget, GameObject targetRoot)
        {
            if (group == null)
            {
                return;
            }

            bool isRoot = false;
            if (newFixTarget is GameObject go)
            {
                isRoot = go == targetRoot;
            }
            else if (newFixTarget is Component component)
            {
                isRoot = component.gameObject == targetRoot;
            }

            group.FixTarget = isRoot ? null : newFixTarget;
            if (group.FixTarget == null)
            {
                foreach (AnimationRedirectToolService.MissingCurveEntry entry in group.CurvesByType.SelectMany(kvp => kvp.Value))
                {
                    if (!entry.IsMarkedForRemoval)
                    {
                        entry.IsFixedByGroup = false;
                    }
                }
            }

            _service.UpdateFixTargetStatus(group);
        }

        internal bool ValidateFixTargetComponentsForDisplay(AnimationRedirectToolService.MissingObjectGroup group)
        {
            List<Type> requiredTypes = group.RequiredTypes;
            if (group.FixTarget == null)
            {
                return requiredTypes.Count == 0;
            }

            for (int i = 0; i < requiredTypes.Count; i++)
            {
                if (!HasRequiredComponent(group, requiredTypes[i]))
                {
                    return false;
                }
            }

            return true;
        }

        internal bool HasRequiredComponent(AnimationRedirectToolService.MissingObjectGroup group, Type requiredType)
        {
            if (requiredType == null || requiredType == typeof(GameObject) || requiredType == typeof(Transform))
            {
                return true;
            }

            GameObject go = group.FixTarget as GameObject ?? (group.FixTarget as Component)?.gameObject;
            return go != null && go.GetComponent(requiredType) != null;
        }

        internal IReadOnlyList<AnimationRedirectToolService.PathChangeGroup> GetVisiblePathChangeGroups()
        {
            HashSet<string> hiddenOriginalPaths = BuildHiddenOriginalPaths();
            return _service.PathChangeGroups
                .Where(group => (group.IsDeleted || group.HasPathChanged) && (string.IsNullOrEmpty(group.OldPath) || !hiddenOriginalPaths.Contains(group.OldPath)))
                .OrderByDescending(group => group.IsDeleted)
                .ThenByDescending(group => group.HasPathChanged)
                .ToList();
        }

        internal IReadOnlyList<AnimationRedirectToolService.ComponentChangeGroup> GetVisibleComponentChangeGroups()
        {
            HashSet<string> hiddenOriginalPaths = BuildHiddenOriginalPaths();
            return _service.ComponentChangeGroups
                .Where(group => group.IsRemoved && group.Bindings.Count > 0 && (string.IsNullOrEmpty(group.Path) || !hiddenOriginalPaths.Contains(group.Path)))
                .OrderBy(group => group.Path)
                .ThenBy(group => group.ComponentName)
                .ToList();
        }

        private HashSet<string> BuildHiddenOriginalPaths()
        {
            return _service.MissingGroups
                .Where(group => group.FixTarget != null && !group.OwnerDeleted)
                .Select(group => group.OldPath)
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct()
                .ToHashSet();
        }

        private static string GetDisplayNameForProperty(AnimationRedirectToolService.MissingCurveEntry entry)
        {
            string name = entry.Binding.propertyName ?? string.Empty;
            int dot = name.LastIndexOf('.');
            return dot >= 0 && dot < name.Length - 1 ? name.Substring(dot + 1) : name;
        }

        private static string GetAggregationKeyForProperty(AnimationRedirectToolService.MissingCurveEntry entry)
        {
            return GetComponentBaseName(entry.Binding.propertyName ?? string.Empty);
        }

        private static int GetCategoryOrder(AnimationRedirectToolService.MissingCurveEntry entry)
        {
            if (entry.IsBlendshape)
            {
                return 0;
            }

            string prop = entry.Binding.propertyName ?? string.Empty;
            return IsComponentProperty(prop) ? 1 : 2;
        }

        private static string GetComponentBaseName(string prop)
        {
            if (string.IsNullOrEmpty(prop))
            {
                return string.Empty;
            }

            if (prop.EndsWith(".x", StringComparison.Ordinal) || prop.EndsWith(".y", StringComparison.Ordinal) ||
                prop.EndsWith(".z", StringComparison.Ordinal) || prop.EndsWith(".w", StringComparison.Ordinal) ||
                prop.EndsWith(".r", StringComparison.Ordinal) || prop.EndsWith(".g", StringComparison.Ordinal) ||
                prop.EndsWith(".b", StringComparison.Ordinal) || prop.EndsWith(".a", StringComparison.Ordinal))
            {
                return prop.Length > 2 ? prop.Substring(0, prop.Length - 2) : prop;
            }

            return prop;
        }

        private static bool IsComponentProperty(string prop)
        {
            if (string.IsNullOrEmpty(prop))
            {
                return false;
            }

            return prop.EndsWith(".x", StringComparison.Ordinal) || prop.EndsWith(".y", StringComparison.Ordinal) ||
                   prop.EndsWith(".z", StringComparison.Ordinal) || prop.EndsWith(".w", StringComparison.Ordinal) ||
                   prop.EndsWith(".r", StringComparison.Ordinal) || prop.EndsWith(".g", StringComparison.Ordinal) ||
                   prop.EndsWith(".b", StringComparison.Ordinal) || prop.EndsWith(".a", StringComparison.Ordinal);
        }

        private static string GetShortName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            int dot = name.LastIndexOf('.');
            return dot >= 0 && dot < name.Length - 1 ? name.Substring(dot + 1) : name;
        }
    }

    internal readonly struct AnimationRedirectSummary
    {
        internal bool HasSnapshot { get; }
        internal int TotalMissingCount { get; }
        internal int IgnorablesCount { get; }
        internal int PathChangeCount { get; }
        internal int ComponentChangeCount { get; }
        internal int ActiveMissingCount { get; }
        internal int RemovalOnlyCount { get; }
        internal int TotalChanges { get; }
        internal bool CanAutoMatch { get; }
        internal bool NeedsComponentFix { get; }
        internal bool CanApply { get; }
        internal bool ShowHierarchyWarning { get; }

        internal AnimationRedirectSummary(
            bool hasSnapshot,
            int totalMissingCount,
            int ignorablesCount,
            int pathChangeCount,
            int componentChangeCount,
            int activeMissingCount,
            int removalOnlyCount,
            int totalChanges,
            bool canAutoMatch,
            bool needsComponentFix,
            bool canApply,
            bool showHierarchyWarning)
        {
            HasSnapshot = hasSnapshot;
            TotalMissingCount = totalMissingCount;
            IgnorablesCount = ignorablesCount;
            PathChangeCount = pathChangeCount;
            ComponentChangeCount = componentChangeCount;
            ActiveMissingCount = activeMissingCount;
            RemovalOnlyCount = removalOnlyCount;
            TotalChanges = totalChanges;
            CanAutoMatch = canAutoMatch;
            NeedsComponentFix = needsComponentFix;
            CanApply = canApply;
            ShowHierarchyWarning = showHierarchyWarning;
        }
    }

    internal readonly struct MissingGroupDisplayInfo
    {
        internal string DisplayName { get; }
        internal string DisplayPath { get; }

        internal MissingGroupDisplayInfo(string displayName, string displayPath)
        {
            DisplayName = displayName;
            DisplayPath = displayPath;
        }
    }

    internal sealed class MissingTypeSectionView
    {
        internal Type ComponentType { get; }
        internal int TotalActiveCount { get; }
        internal IReadOnlyList<AnimationRedirectToolService.MissingCurveEntry> AllEntries { get; }
        internal IReadOnlyList<MissingPropertyGroupView> PropertyGroups { get; }

        internal MissingTypeSectionView(
            Type componentType,
            int totalActiveCount,
            IReadOnlyList<AnimationRedirectToolService.MissingCurveEntry> allEntries,
            IReadOnlyList<MissingPropertyGroupView> propertyGroups)
        {
            ComponentType = componentType;
            TotalActiveCount = totalActiveCount;
            AllEntries = allEntries;
            PropertyGroups = propertyGroups;
        }
    }

    internal sealed class MissingPropertyGroupView
    {
        internal string LabelText { get; }
        internal bool HasActiveEntries { get; }
        internal bool AllFixed { get; }
        internal bool IsBlendshape { get; }
        internal IReadOnlyList<AnimationRedirectToolService.MissingCurveEntry> Entries { get; }
        internal IReadOnlyList<string> BlendshapeOptions { get; }
        internal int CurrentBlendshapeIndex { get; }

        internal MissingPropertyGroupView(
            string labelText,
            bool hasActiveEntries,
            bool allFixed,
            bool isBlendshape,
            IReadOnlyList<AnimationRedirectToolService.MissingCurveEntry> entries,
            IReadOnlyList<string> blendshapeOptions,
            int currentBlendshapeIndex)
        {
            LabelText = labelText;
            HasActiveEntries = hasActiveEntries;
            AllFixed = allFixed;
            IsBlendshape = isBlendshape;
            Entries = entries;
            BlendshapeOptions = blendshapeOptions;
            CurrentBlendshapeIndex = currentBlendshapeIndex;
        }
    }
}
