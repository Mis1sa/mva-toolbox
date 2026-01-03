using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MVA.Toolbox.AnimFixUtility.Services;

namespace MVA.Toolbox.AnimPathRedirect.Services
{
    internal sealed class AnimFixRedirectPresentationService
    {
        private readonly AnimPathRedirectService _service;

        public AnimFixRedirectPresentationService(AnimPathRedirectService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public void SyncSelectionWithContext(AnimFixUtilityContext context)
        {
            if (context == null)
            {
                return;
            }

            var controllers = _service.Controllers;
            if (controllers.Count == 0)
            {
                return;
            }

            int ctxIndex = Mathf.Clamp(context.SelectedControllerIndex, 0, controllers.Count - 1);
            if (!_service.HasSnapshot)
            {
                _service.SelectedControllerIndex = ctxIndex;
                int desiredLayer = context.SelectedLayerIndex < 0 ? 0 : context.SelectedLayerIndex + 1;
                _service.SelectedLayerIndex = Mathf.Max(0, desiredLayer);
            }
            else
            {
                context.SelectedControllerIndex = Mathf.Clamp(_service.SelectedControllerIndex, 0, controllers.Count - 1);
                int serviceLayer = Mathf.Max(0, _service.SelectedLayerIndex);
                context.SelectedLayerIndex = serviceLayer <= 0 ? -1 : serviceLayer - 1;
            }
        }

        public AnimFixRedirectSummary BuildSummary()
        {
            var missingGroups = _service.MissingGroups;
            int totalMissingCount = missingGroups
                .Sum(g => g.CurvesByType.Sum(kvp => kvp.Value.Count(c => !c.IsMarkedForRemoval)));
            int ignorablesCount = missingGroups
                .Count(g => g.FixTarget == null && !g.IsEmpty);

            int pathChangeCount = _service.PathChangeGroups.Count(d => d.IsDeleted || d.HasPathChanged);
            int componentChangeCount = _service.ComponentChangeGroups.Sum(g => g.Bindings.Count);

            int activeMissingCount = missingGroups.Sum(g =>
                (g.OwnerDeleted || (_service.IgnoreAllMissing && g.FixTarget == null))
                    ? 0
                    : g.CurvesByType.Sum(kvp => kvp.Value.Count(c => !c.IsMarkedForRemoval))
            );

            int removalOnlyCount = missingGroups
                .Sum(g => g.CurvesByType.Sum(kvp => kvp.Value.Count(c => c.IsMarkedForRemoval)));

            int totalChanges = pathChangeCount + componentChangeCount + activeMissingCount + removalOnlyCount;

            if (pathChangeCount > 0 && !_service.HierarchyChanged)
            {
                _service.HierarchyChanged = true;
            }

            bool canAutoMatch = missingGroups.Any(g =>
                g != null &&
                !g.OwnerDeleted &&
                !g.IsEmpty &&
                g.FixTarget == null &&
                !(_service.IgnoreAllMissing && g.FixTarget == null));

            bool needsComponentFix = missingGroups
                .Where(g => g.FixTarget != null && !g.IsEmpty)
                .Any(g => !ValidateFixTargetComponentsForDisplay(g));

            bool canApply = totalChanges > 0 && !needsComponentFix;

            return new AnimFixRedirectSummary(
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
                canApply,
                _service.HierarchyChanged);
        }

        public IReadOnlyList<AnimPathRedirectService.MissingObjectGroup> GetVisibleMissingGroups()
        {
            return _service.MissingGroups
                .Where(g => !g.OwnerDeleted && !g.IsEmpty && !(_service.IgnoreAllMissing && g.FixTarget == null))
                .OrderBy(g => g.IsFixed)
                .ToList();
        }

        public MissingGroupDisplayInfo GetDisplayInfo(AnimPathRedirectService.MissingObjectGroup group)
        {
            if (group == null)
            {
                return default;
            }

            var root = _service.TargetRoot;
            string displayName = group.TargetObjectName;
            string displayPath = string.IsNullOrEmpty(group.CurrentPath) ? group.OldPath : group.CurrentPath;

            if (group.FixTarget != null && root != null)
            {
                var fixGo = group.FixTarget as GameObject ?? (group.FixTarget as Component)?.gameObject;
                if (fixGo != null)
                {
                    displayName = fixGo.name;

                    if (fixGo == root)
                    {
                        displayPath = string.Empty;
                    }
                    else
                    {
                        var fixPath = AnimationUtility.CalculateTransformPath(fixGo.transform, root.transform);
                        if (fixPath != null)
                        {
                            displayPath = fixPath;
                        }
                    }
                }
            }

            return new MissingGroupDisplayInfo(displayName, displayPath);
        }

        public IReadOnlyList<MissingTypeSectionView> BuildTypeSections(AnimPathRedirectService.MissingObjectGroup group)
        {
            var sections = new List<MissingTypeSectionView>();
            if (group == null)
            {
                return sections;
            }

            foreach (var kvp in group.CurvesByType)
            {
                var type = kvp.Key;
                var curves = kvp.Value;
                var activeCurves = curves.Where(c => !c.IsMarkedForRemoval).ToList();
                if (activeCurves.Count == 0)
                {
                    continue;
                }

                var aggregated = activeCurves
                    .GroupBy(GetAggregationKeyForProperty)
                    .OrderBy(g => GetCategoryOrder(g.First()))
                    .ThenBy(g => GetDisplayNameForProperty(g.First()))
                    .ToList();

                int totalCount = aggregated.Sum(g => g.Count());

                var propertyGroups = new List<MissingPropertyGroupView>();
                foreach (var grouping in aggregated)
                {
                    var entries = grouping.ToList();
                    var rep = entries[0];

                    string labelText;
                    if (rep.IsBlendshape)
                    {
                        string name = GetDisplayNameForProperty(rep);
                        labelText = $"[BlendShape] {name}";
                    }
                    else
                    {
                        var propName = rep.Binding.propertyName ?? string.Empty;
                        string baseName = GetComponentBaseName(propName);
                        string shortBaseName = GetShortName(baseName);
                        labelText = $"[{shortBaseName}]";
                    }

                    bool hasActive = entries.Any(e => !e.IsMarkedForRemoval);
                    bool allFixed = hasActive && group.FixTarget != null && entries.All(e => e.IsFixedByGroup && !e.IsMarkedForRemoval);

                    var blendshapeOptions = rep.AvailableBlendshapes ?? new List<string>();
                    int currentBlendshapeIndex = blendshapeOptions.Count > 0
                        ? Math.Max(0, blendshapeOptions.IndexOf(rep.NewBlendshapeName))
                        : -1;

                    propertyGroups.Add(new MissingPropertyGroupView(
                        labelText,
                        hasActive,
                        allFixed,
                        rep.IsBlendshape,
                        entries,
                        blendshapeOptions,
                        currentBlendshapeIndex));
                }

                sections.Add(new MissingTypeSectionView(type, totalCount, curves, propertyGroups));
            }

            return sections;
        }

        public void MarkEntriesForRemoval(AnimPathRedirectService.MissingObjectGroup group, IEnumerable<AnimPathRedirectService.MissingCurveEntry> entries)
        {
            if (group == null || entries == null)
            {
                return;
            }

            foreach (var entry in entries)
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

        public void ApplyBlendshapeSelection(MissingPropertyGroupView propertyGroup, int optionIndex)
        {
            if (propertyGroup == null)
            {
                return;
            }

            var options = propertyGroup.BlendshapeOptions;
            if (options == null || options.Count == 0)
            {
                return;
            }

            int clamped = Mathf.Clamp(optionIndex, 0, options.Count - 1);
            string selected = options[clamped];
            foreach (var entry in propertyGroup.Entries)
            {
                entry.NewBlendshapeName = selected;
            }
        }

        public void UpdateFixTarget(AnimPathRedirectService.MissingObjectGroup group, UnityEngine.Object newFixTarget, GameObject targetRoot)
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
            else if (newFixTarget is Component comp)
            {
                isRoot = comp.gameObject == targetRoot;
            }

            group.FixTarget = isRoot ? null : newFixTarget;

            if (group.FixTarget == null)
            {
                foreach (var entry in group.CurvesByType.SelectMany(kvp => kvp.Value))
                {
                    if (!entry.IsMarkedForRemoval)
                    {
                        entry.IsFixedByGroup = false;
                    }
                }
            }

            _service.UpdateFixTargetStatus(group);
        }

        public bool ValidateFixTargetComponentsForDisplay(AnimPathRedirectService.MissingObjectGroup group)
        {
            var requiredTypes = group.RequiredTypes;
            if (group.FixTarget == null)
            {
                return requiredTypes.Count == 0;
            }

            foreach (var type in requiredTypes)
            {
                if (!HasRequiredComponent(group, type))
                {
                    return false;
                }
            }

            return true;
        }

        public bool HasRequiredComponent(AnimPathRedirectService.MissingObjectGroup group, Type requiredType)
        {
            if (requiredType == null)
            {
                return true;
            }

            if (requiredType == typeof(GameObject) || requiredType == typeof(Transform))
            {
                return true;
            }

            var go = group.FixTarget as GameObject ?? (group.FixTarget as Component)?.gameObject;
            if (go == null)
            {
                return false;
            }

            return go.GetComponent(requiredType) != null;
        }

        public IReadOnlyList<AnimPathRedirectService.PathChangeGroup> GetVisiblePathChangeGroups()
        {
            var hiddenOriginalPaths = BuildHiddenOriginalPaths();
            return _service.PathChangeGroups
                .Where(d => (d.IsDeleted || d.HasPathChanged) && (string.IsNullOrEmpty(d.OldPath) || !hiddenOriginalPaths.Contains(d.OldPath)))
                .OrderByDescending(d => d.IsDeleted)
                .ThenByDescending(d => d.HasPathChanged)
                .ToList();
        }

        public IReadOnlyList<AnimPathRedirectService.ComponentChangeGroup> GetVisibleComponentChangeGroups()
        {
            var hiddenOriginalPaths = BuildHiddenOriginalPaths();
            return _service.ComponentChangeGroups
                .Where(g => g.IsRemoved && g.Bindings.Count > 0 && (string.IsNullOrEmpty(g.Path) || !hiddenOriginalPaths.Contains(g.Path)))
                .OrderBy(g => g.Path)
                .ThenBy(g => g.ComponentName)
                .ToList();
        }

        private HashSet<string> BuildHiddenOriginalPaths()
        {
            return _service.MissingGroups
                .Where(g => g.FixTarget != null && !g.OwnerDeleted)
                .Select(g => g.OldPath)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .ToHashSet();
        }

        private static string GetDisplayNameForProperty(AnimPathRedirectService.MissingCurveEntry entry)
        {
            var name = entry.Binding.propertyName ?? string.Empty;
            int dot = name.LastIndexOf('.');
            return dot >= 0 && dot < name.Length - 1 ? name.Substring(dot + 1) : name;
        }

        private static string GetAggregationKeyForProperty(AnimPathRedirectService.MissingCurveEntry entry)
        {
            string prop = entry.Binding.propertyName ?? string.Empty;
            return GetComponentBaseName(prop);
        }

        private static int GetCategoryOrder(AnimPathRedirectService.MissingCurveEntry entry)
        {
            if (entry.IsBlendshape)
            {
                return 0;
            }

            var prop = entry.Binding.propertyName ?? string.Empty;
            if (IsComponentProperty(prop))
            {
                return 1;
            }

            return 2;
        }

        private static string GetComponentBaseName(string prop)
        {
            if (string.IsNullOrEmpty(prop)) return string.Empty;

            if (prop.EndsWith(".x") || prop.EndsWith(".y") || prop.EndsWith(".z") || prop.EndsWith(".w") ||
                prop.EndsWith(".r") || prop.EndsWith(".g") || prop.EndsWith(".b") || prop.EndsWith(".a"))
            {
                if (prop.Length > 2)
                {
                    return prop.Substring(0, prop.Length - 2);
                }
            }

            return prop;
        }

        private static bool IsComponentProperty(string prop)
        {
            if (string.IsNullOrEmpty(prop)) return false;
            return prop.EndsWith(".x") || prop.EndsWith(".y") || prop.EndsWith(".z") || prop.EndsWith(".w") ||
                   prop.EndsWith(".r") || prop.EndsWith(".g") || prop.EndsWith(".b") || prop.EndsWith(".a");
        }

        private static string GetShortName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            int dot = name.LastIndexOf('.');
            return dot >= 0 && dot < name.Length - 1 ? name.Substring(dot + 1) : name;
        }
    }

    internal readonly struct AnimFixRedirectSummary
    {
        public bool HasSnapshot { get; }
        public int TotalMissingCount { get; }
        public int IgnorablesCount { get; }
        public int PathChangeCount { get; }
        public int ComponentChangeCount { get; }
        public int ActiveMissingCount { get; }
        public int RemovalOnlyCount { get; }
        public int TotalChanges { get; }
        public bool CanAutoMatch { get; }
        public bool NeedsComponentFix { get; }
        public bool CanApply { get; }
        public bool ShowHierarchyWarning { get; }

        public AnimFixRedirectSummary(
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
        public string DisplayName { get; }
        public string DisplayPath { get; }

        public MissingGroupDisplayInfo(string displayName, string displayPath)
        {
            DisplayName = displayName;
            DisplayPath = displayPath;
        }
    }

    internal sealed class MissingTypeSectionView
    {
        public Type ComponentType { get; }
        public int TotalActiveCount { get; }
        public IReadOnlyList<AnimPathRedirectService.MissingCurveEntry> AllEntries { get; }
        public IReadOnlyList<MissingPropertyGroupView> PropertyGroups { get; }

        public MissingTypeSectionView(
            Type componentType,
            int totalActiveCount,
            IReadOnlyList<AnimPathRedirectService.MissingCurveEntry> allEntries,
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
        public string LabelText { get; }
        public bool HasActiveEntries { get; }
        public bool AllFixed { get; }
        public bool IsBlendshape { get; }
        public IReadOnlyList<AnimPathRedirectService.MissingCurveEntry> Entries { get; }
        public IReadOnlyList<string> BlendshapeOptions { get; }
        public int CurrentBlendshapeIndex { get; }

        public MissingPropertyGroupView(
            string labelText,
            bool hasActiveEntries,
            bool allFixed,
            bool isBlendshape,
            IReadOnlyList<AnimPathRedirectService.MissingCurveEntry> entries,
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
