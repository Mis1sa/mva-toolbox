using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MVA.Toolbox.FbxReplacement
{
    internal enum FbxReplacementStageThreeWorkflowStep
    {
        TargetMaterialReview,
        SupplementModeSelection,
        SupplementMaterialReview,
        Completed
    }

    internal enum FbxReplacementMaterialDecisionActionType
    {
        ReplaceTargetMaterials,
        SkipTargetMaterials,
        ChooseSupplementReuse,
        ChooseSupplementAdjust,
        ConfirmSupplementMaterials
    }

    internal sealed class FbxReplacementMaterialReviewEntry
    {
        internal FbxReplacementMaterialReviewEntry(
            string key,
            string targetKey,
            string referenceKey,
            bool isSupplement,
            int materialSlotCount)
        {
            Key = key ?? string.Empty;
            TargetKey = targetKey ?? string.Empty;
            ReferenceKey = referenceKey ?? string.Empty;
            IsSupplement = isSupplement;
            MaterialSlotCount = Mathf.Max(1, materialSlotCount);
        }

        internal string Key { get; }
        internal string TargetKey { get; }
        internal string ReferenceKey { get; }
        internal bool IsSupplement { get; }
        internal int MaterialSlotCount { get; }
    }

    internal sealed class FbxReplacementMaterialDecisionRecord
    {
        internal FbxReplacementMaterialDecisionRecord(
            FbxReplacementMaterialDecisionActionType actionType,
            string entryKey,
            Material[] materials)
        {
            ActionType = actionType;
            EntryKey = entryKey ?? string.Empty;
            Materials = materials != null
                ? materials.ToArray()
                : Array.Empty<Material>();
        }

        internal FbxReplacementMaterialDecisionActionType ActionType { get; }
        internal string EntryKey { get; }
        internal IReadOnlyList<Material> Materials { get; }
    }

    internal sealed class FbxReplacementStageThreeState
    {
        internal FbxReplacementStageThreeState(
            FbxReplacementStageTwoState stageTwoState,
            List<FbxReplacementMaterialReviewEntry> targetMaterialEntries,
            List<FbxReplacementMaterialReviewEntry> supplementMaterialEntries)
        {
            StageTwoState = stageTwoState ?? throw new ArgumentNullException(nameof(stageTwoState));
            TargetMaterialEntries = targetMaterialEntries ?? new List<FbxReplacementMaterialReviewEntry>();
            SupplementMaterialEntries = supplementMaterialEntries ?? new List<FbxReplacementMaterialReviewEntry>();
            BaselineMaterialsByEntryKey = new Dictionary<string, Material[]>(StringComparer.Ordinal);
            History = new List<FbxReplacementMaterialDecisionRecord>();
            ProcessedTargetEntryKeys = new HashSet<string>(StringComparer.Ordinal);
            ReplacedTargetEntryKeys = new HashSet<string>(StringComparer.Ordinal);
            SkippedTargetEntryKeys = new HashSet<string>(StringComparer.Ordinal);
            ProcessedSupplementEntryKeys = new HashSet<string>(StringComparer.Ordinal);
            CurrentTargetIndex = -1;
            CurrentSupplementIndex = -1;
            CurrentMaterialSelections = Array.Empty<Material>();
            CurrentStep = FbxReplacementStageThreeWorkflowStep.TargetMaterialReview;
        }

        internal FbxReplacementStageTwoState StageTwoState { get; }
        internal List<FbxReplacementMaterialReviewEntry> TargetMaterialEntries { get; }
        internal List<FbxReplacementMaterialReviewEntry> SupplementMaterialEntries { get; }
        internal Dictionary<string, Material[]> BaselineMaterialsByEntryKey { get; }
        internal List<FbxReplacementMaterialDecisionRecord> History { get; }
        internal HashSet<string> ProcessedTargetEntryKeys { get; }
        internal HashSet<string> ReplacedTargetEntryKeys { get; }
        internal HashSet<string> SkippedTargetEntryKeys { get; }
        internal HashSet<string> ProcessedSupplementEntryKeys { get; }
        internal int CurrentTargetIndex { get; set; }
        internal int CurrentSupplementIndex { get; set; }
        internal Material[] CurrentMaterialSelections { get; set; }
        internal bool SupplementReuseChosen { get; set; }
        internal bool SupplementAdjustChosen { get; set; }
        internal FbxReplacementStageThreeWorkflowStep CurrentStep { get; set; }
    }
}