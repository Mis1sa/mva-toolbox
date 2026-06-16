using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.FbxReplacement
{
    internal enum FbxReplacementStageFourWorkflowStep
    {
        ComponentReview,
        Completed
    }

    internal enum FbxReplacementComponentDecisionActionType
    {
        ConfirmWithRemap,
        KeepWithoutRemap,
        Remove
    }

    internal enum FbxReplacementComponentSelectionKind
    {
        None,
        GameObject,
        Transform,
        Component
    }

    internal sealed class FbxReplacementComponentSelectionHandle
    {
        internal static FbxReplacementComponentSelectionHandle None { get; } = new FbxReplacementComponentSelectionHandle(
            FbxReplacementComponentSelectionKind.None,
            string.Empty,
            string.Empty,
            -1);

        internal FbxReplacementComponentSelectionHandle(
            FbxReplacementComponentSelectionKind kind,
            string targetHierarchyKey,
            string componentTypeName,
            int componentSlotIndex)
        {
            Kind = kind;
            TargetHierarchyKey = targetHierarchyKey ?? string.Empty;
            ComponentTypeName = componentTypeName ?? string.Empty;
            ComponentSlotIndex = componentSlotIndex;
        }

        internal FbxReplacementComponentSelectionKind Kind { get; }
        internal string TargetHierarchyKey { get; }
        internal string ComponentTypeName { get; }
        internal int ComponentSlotIndex { get; }
    }

    internal sealed class FbxReplacementComponentReferenceSlot
    {
        internal FbxReplacementComponentReferenceSlot(
            string propertyPath,
            string displayName,
            Type referenceType,
            Object sourceReference,
            FbxReplacementComponentSelectionHandle recommendedSelection)
        {
            PropertyPath = propertyPath ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            ReferenceType = referenceType ?? typeof(Object);
            SourceReference = sourceReference;
            RecommendedSelection = recommendedSelection ?? FbxReplacementComponentSelectionHandle.None;
        }

        internal string PropertyPath { get; }
        internal string DisplayName { get; }
        internal Type ReferenceType { get; }
        internal Object SourceReference { get; }
        internal FbxReplacementComponentSelectionHandle RecommendedSelection { get; }
    }

    internal sealed class FbxReplacementTransformSnapshot
    {
        internal FbxReplacementTransformSnapshot(
            string hierarchyKey,
            Vector3 localPosition,
            Quaternion localRotation,
            Vector3 localScale)
        {
            HierarchyKey = hierarchyKey ?? string.Empty;
            LocalPosition = localPosition;
            LocalRotation = localRotation;
            LocalScale = localScale;
        }

        internal string HierarchyKey { get; }
        internal Vector3 LocalPosition { get; }
        internal Quaternion LocalRotation { get; }
        internal Vector3 LocalScale { get; }
    }

    internal sealed class FbxReplacementComponentReviewEntry
    {
        internal FbxReplacementComponentReviewEntry(
            string key,
            string referenceKey,
            string targetKey,
            string targetHierarchyKey,
            bool isSupplement,
            Component sourceComponent,
            Type componentType,
            string displayName,
            int componentIndex,
            int typeSlotIndex)
        {
            Key = key ?? string.Empty;
            ReferenceKey = referenceKey ?? string.Empty;
            TargetKey = targetKey ?? string.Empty;
            TargetHierarchyKey = targetHierarchyKey ?? string.Empty;
            IsSupplement = isSupplement;
            SourceComponent = sourceComponent;
            ComponentType = componentType;
            DisplayName = displayName ?? string.Empty;
            ComponentIndex = componentIndex;
            TypeSlotIndex = typeSlotIndex;
            DependencyKeys = new List<string>();
            ReferenceSlots = new List<FbxReplacementComponentReferenceSlot>();
            PriorityScore = 100;
        }

        internal string Key { get; }
        internal string ReferenceKey { get; }
        internal string TargetKey { get; }
        internal string TargetHierarchyKey { get; }
        internal bool IsSupplement { get; }
        internal Component SourceComponent { get; }
        internal Type ComponentType { get; }
        internal string DisplayName { get; }
        internal int ComponentIndex { get; }
        internal int TypeSlotIndex { get; }
        internal List<string> DependencyKeys { get; }
        internal List<FbxReplacementComponentReferenceSlot> ReferenceSlots { get; }
        internal int PriorityScore { get; set; }
    }

    internal sealed class FbxReplacementComponentObjectReviewEntry
    {
        internal FbxReplacementComponentObjectReviewEntry(
            string referenceKey,
            string targetKey,
            string targetHierarchyKey,
            bool isSupplement,
            List<FbxReplacementComponentReviewEntry> componentEntries)
        {
            ReferenceKey = referenceKey ?? string.Empty;
            TargetKey = targetKey ?? string.Empty;
            TargetHierarchyKey = targetHierarchyKey ?? string.Empty;
            IsSupplement = isSupplement;
            ComponentEntries = componentEntries ?? new List<FbxReplacementComponentReviewEntry>();
        }

        internal string ReferenceKey { get; }
        internal string TargetKey { get; }
        internal string TargetHierarchyKey { get; }
        internal bool IsSupplement { get; }
        internal List<FbxReplacementComponentReviewEntry> ComponentEntries { get; }
    }

    internal sealed class FbxReplacementComponentDecisionRecord
    {
        internal FbxReplacementComponentDecisionRecord(
            FbxReplacementComponentDecisionActionType actionType,
            string entryKey,
            IReadOnlyList<FbxReplacementComponentSelectionHandle> selections)
        {
            ActionType = actionType;
            EntryKey = entryKey ?? string.Empty;
            Selections = selections != null
                ? selections.Select(CloneSelection).ToList()
                : new List<FbxReplacementComponentSelectionHandle>();
        }

        internal FbxReplacementComponentDecisionActionType ActionType { get; }
        internal string EntryKey { get; }
        internal IReadOnlyList<FbxReplacementComponentSelectionHandle> Selections { get; }

        private static FbxReplacementComponentSelectionHandle CloneSelection(FbxReplacementComponentSelectionHandle selection)
        {
            if (selection == null)
            {
                return FbxReplacementComponentSelectionHandle.None;
            }

            return new FbxReplacementComponentSelectionHandle(
                selection.Kind,
                selection.TargetHierarchyKey,
                selection.ComponentTypeName,
                selection.ComponentSlotIndex);
        }
    }

    internal sealed class FbxReplacementStageFourState
    {
        internal FbxReplacementStageFourState(
            FbxReplacementStageThreeState stageThreeState,
            GameObject baselineTargetTemplate,
            List<FbxReplacementComponentObjectReviewEntry> objectEntries,
            Dictionary<string, FbxReplacementComponentReviewEntry> entriesByKey,
            Dictionary<string, FbxReplacementComponentObjectReviewEntry> objectEntriesByReferenceKey,
            Dictionary<int, string> referenceKeyByInstanceId,
            Dictionary<Component, string> entryKeyByReferenceComponent,
            List<FbxReplacementTransformSnapshot> transformSnapshots)
        {
            StageThreeState = stageThreeState ?? throw new ArgumentNullException(nameof(stageThreeState));
            BaselineTargetTemplate = baselineTargetTemplate;
            ObjectEntries = objectEntries ?? new List<FbxReplacementComponentObjectReviewEntry>();
            EntriesByKey = entriesByKey ?? new Dictionary<string, FbxReplacementComponentReviewEntry>(StringComparer.Ordinal);
            ObjectEntriesByReferenceKey = objectEntriesByReferenceKey ?? new Dictionary<string, FbxReplacementComponentObjectReviewEntry>(StringComparer.Ordinal);
            ReferenceKeyByInstanceId = referenceKeyByInstanceId ?? new Dictionary<int, string>();
            EntryKeyByReferenceComponent = entryKeyByReferenceComponent ?? new Dictionary<Component, string>();
            TransformSnapshots = transformSnapshots ?? new List<FbxReplacementTransformSnapshot>();
            History = new List<FbxReplacementComponentDecisionRecord>();
            ProcessedEntryKeys = new HashSet<string>(StringComparer.Ordinal);
            ConfirmedEntryKeys = new HashSet<string>(StringComparer.Ordinal);
            KeptEntryKeys = new HashSet<string>(StringComparer.Ordinal);
            RemovedEntryKeys = new HashSet<string>(StringComparer.Ordinal);
            CurrentSelections = Array.Empty<FbxReplacementComponentSelectionHandle>();
            CurrentEntryKey = string.Empty;
            CurrentStep = FbxReplacementStageFourWorkflowStep.ComponentReview;
        }

        internal FbxReplacementStageThreeState StageThreeState { get; }
        internal GameObject BaselineTargetTemplate { get; set; }
        internal List<FbxReplacementComponentObjectReviewEntry> ObjectEntries { get; }
        internal Dictionary<string, FbxReplacementComponentReviewEntry> EntriesByKey { get; }
        internal Dictionary<string, FbxReplacementComponentObjectReviewEntry> ObjectEntriesByReferenceKey { get; }
        internal Dictionary<int, string> ReferenceKeyByInstanceId { get; }
        internal Dictionary<Component, string> EntryKeyByReferenceComponent { get; }
        internal List<FbxReplacementTransformSnapshot> TransformSnapshots { get; }
        internal List<FbxReplacementComponentDecisionRecord> History { get; }
        internal HashSet<string> ProcessedEntryKeys { get; }
        internal HashSet<string> ConfirmedEntryKeys { get; }
        internal HashSet<string> KeptEntryKeys { get; }
        internal HashSet<string> RemovedEntryKeys { get; }
        internal string CurrentEntryKey { get; set; }
        internal FbxReplacementComponentSelectionHandle[] CurrentSelections { get; set; }
        internal FbxReplacementStageFourWorkflowStep CurrentStep { get; set; }
    }
}