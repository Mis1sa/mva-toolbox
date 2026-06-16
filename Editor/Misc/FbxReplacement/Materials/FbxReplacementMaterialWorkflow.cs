using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.FbxReplacement
{
    internal static class FbxReplacementMaterialWorkflow
    {
        internal static readonly Color CurrentTargetHighlightColor = new Color(0.25f, 0.8f, 0.3f, 0.32f);
        internal static readonly Color CurrentReferenceHighlightColor = new Color(1f, 0.35f, 0.35f, 0.36f);
        internal static readonly Color ProcessedHighlightColor = new Color(0.3f, 0.55f, 1f, 0.34f);
        internal static readonly Color PendingSupplementHighlightColor = new Color(1f, 0.85f, 0.2f, 0.34f);
        private static Material _defaultMaterial;

        internal static FbxReplacementStageThreeState CreateState(FbxReplacementStageTwoState stageTwoState)
        {
            if (stageTwoState == null)
            {
                throw new ArgumentNullException(nameof(stageTwoState));
            }

            if (FbxReplacementStructureWorkflow.GetCurrentStep(stageTwoState) != FbxReplacementStageTwoWorkflowStep.Completed)
            {
                throw new InvalidOperationException("阶段2尚未完成，无法进入阶段3。");
            }

            var targetEntries = BuildTargetMaterialEntries(stageTwoState);
            var supplementEntries = BuildSupplementMaterialEntries(stageTwoState);
            var state = new FbxReplacementStageThreeState(stageTwoState, targetEntries, supplementEntries);
            if (targetEntries.Count == 0 && supplementEntries.Count == 0)
            {
                CompleteStage(state);
                return state;
            }

            InitializeBaselines(state);
            PrepareNextStep(state);
            return state;
        }

        internal static void DisposeState(FbxReplacementStageThreeState state)
        {
            if (state == null)
            {
                return;
            }

            state.BaselineMaterialsByEntryKey.Clear();
            state.History.Clear();
            state.ProcessedTargetEntryKeys.Clear();
            state.ReplacedTargetEntryKeys.Clear();
            state.SkippedTargetEntryKeys.Clear();
            state.ProcessedSupplementEntryKeys.Clear();
            state.CurrentMaterialSelections = Array.Empty<Material>();
            state.CurrentTargetIndex = -1;
            state.CurrentSupplementIndex = -1;
            state.SupplementReuseChosen = false;
            state.SupplementAdjustChosen = false;
            state.CurrentStep = FbxReplacementStageThreeWorkflowStep.Completed;
        }

        internal static void RevertAllToBaseline(FbxReplacementStageThreeState state)
        {
            if (state == null)
            {
                return;
            }

            List<FbxReplacementMaterialReviewEntry> allEntries = GetAllEntries(state);
            for (int i = 0; i < allEntries.Count; i++)
            {
                ResetEntryToBaseline(state, allEntries[i]);
            }
        }

        internal static FbxReplacementStageThreeWorkflowStep GetCurrentStep(FbxReplacementStageThreeState state)
        {
            return state != null ? state.CurrentStep : FbxReplacementStageThreeWorkflowStep.Completed;
        }

        internal static int GetTargetCandidateCount(FbxReplacementStageThreeState state)
        {
            return state != null ? state.TargetMaterialEntries.Count : 0;
        }

        internal static int GetTargetProcessedCount(FbxReplacementStageThreeState state)
        {
            return state != null ? state.ProcessedTargetEntryKeys.Count : 0;
        }

        internal static int GetSupplementCandidateCount(FbxReplacementStageThreeState state)
        {
            return state != null ? state.SupplementMaterialEntries.Count : 0;
        }

        internal static int GetSupplementProcessedCount(FbxReplacementStageThreeState state)
        {
            if (state == null)
            {
                return 0;
            }

            return state.SupplementReuseChosen
                ? state.SupplementMaterialEntries.Count
                : state.ProcessedSupplementEntryKeys.Count;
        }

        internal static bool HasSupplementMeshEntries(FbxReplacementStageThreeState state)
        {
            return state != null && state.SupplementMaterialEntries.Count > 0;
        }

        internal static Material[] GetCurrentMaterialSelections(FbxReplacementStageThreeState state)
        {
            return state != null && state.CurrentMaterialSelections != null
                ? state.CurrentMaterialSelections
                : Array.Empty<Material>();
        }

        internal static Material[] GetCurrentOriginalMaterials(FbxReplacementStageThreeState state)
        {
            FbxReplacementMaterialReviewEntry entry = GetCurrentEntry(state);
            if (state == null || entry == null)
            {
                return Array.Empty<Material>();
            }

            return GetBaselineMaterials(state, entry);
        }

        internal static string GetCurrentMaterialSlotLabel(FbxReplacementStageThreeState state, int slotIndex)
        {
            return $"Element {slotIndex}";
        }

        internal static GameObject[] GetPendingSupplementTargetObjects(FbxReplacementStageThreeState state)
        {
            if (state == null || state.SupplementMaterialEntries == null)
            {
                return Array.Empty<GameObject>();
            }

            return state.SupplementMaterialEntries
                .Where(entry => !state.ProcessedSupplementEntryKeys.Contains(entry.Key))
                .Select(entry => ResolveTargetObject(state, entry))
                .Where(gameObject => gameObject != null)
                .Distinct()
                .ToArray();
        }

        internal static GameObject GetCurrentTargetObject(FbxReplacementStageThreeState state)
        {
            FbxReplacementMaterialReviewEntry entry = GetCurrentEntry(state);
            return ResolveTargetObject(state, entry);
        }

        internal static GameObject GetCurrentReferenceObject(FbxReplacementStageThreeState state)
        {
            FbxReplacementMaterialReviewEntry entry = GetCurrentEntry(state);
            return ResolveReferenceObject(state, entry);
        }

        internal static void RevealCurrentObjects(FbxReplacementStageThreeState state)
        {
            if (state == null)
            {
                return;
            }

            if (state.CurrentStep == FbxReplacementStageThreeWorkflowStep.SupplementModeSelection)
            {
                int pendingSupplementIndex = FindNextPendingSupplementIndex(state, 0);
                FbxReplacementMaterialReviewEntry pendingSupplementEntry = pendingSupplementIndex >= 0
                    ? state.SupplementMaterialEntries[pendingSupplementIndex]
                    : null;
                FbxReplacementHierarchyHighlighter.RevealHierarchyObjects(
                    ResolveTargetObject(state, pendingSupplementEntry),
                    ResolveReferenceObject(state, pendingSupplementEntry));
                return;
            }

            FbxReplacementHierarchyHighlighter.RevealHierarchyObjects(
                GetCurrentTargetObject(state),
                GetCurrentReferenceObject(state));
        }

        internal static Dictionary<int, Color> BuildHighlightMap(FbxReplacementStageThreeState state)
        {
            var result = new Dictionary<int, Color>();
            if (state == null)
            {
                return result;
            }

            for (int i = 0; i < state.TargetMaterialEntries.Count; i++)
            {
                FbxReplacementMaterialReviewEntry entry = state.TargetMaterialEntries[i];
                if (!state.ProcessedTargetEntryKeys.Contains(entry.Key))
                {
                    continue;
                }

                GameObject targetObject = ResolveTargetObject(state, entry);
                if (targetObject != null)
                {
                    result[targetObject.GetInstanceID()] = ProcessedHighlightColor;
                }
            }

            for (int i = 0; i < state.SupplementMaterialEntries.Count; i++)
            {
                FbxReplacementMaterialReviewEntry entry = state.SupplementMaterialEntries[i];
                bool isProcessed = state.SupplementReuseChosen || state.ProcessedSupplementEntryKeys.Contains(entry.Key);
                if (!isProcessed)
                {
                    continue;
                }

                GameObject targetObject = ResolveTargetObject(state, entry);
                if (targetObject != null)
                {
                    result[targetObject.GetInstanceID()] = ProcessedHighlightColor;
                }
            }

            if (state.CurrentStep == FbxReplacementStageThreeWorkflowStep.TargetMaterialReview)
            {
                GameObject currentTarget = GetCurrentTargetObject(state);
                if (currentTarget != null)
                {
                    result[currentTarget.GetInstanceID()] = CurrentTargetHighlightColor;
                }

                GameObject currentReference = GetCurrentReferenceObject(state);
                if (currentReference != null)
                {
                    result[currentReference.GetInstanceID()] = CurrentReferenceHighlightColor;
                }
            }
            else if (state.CurrentStep == FbxReplacementStageThreeWorkflowStep.SupplementModeSelection)
            {
                for (int i = 0; i < state.SupplementMaterialEntries.Count; i++)
                {
                    FbxReplacementMaterialReviewEntry entry = state.SupplementMaterialEntries[i];
                    GameObject targetObject = ResolveTargetObject(state, entry);
                    if (targetObject != null)
                    {
                        result[targetObject.GetInstanceID()] = PendingSupplementHighlightColor;
                    }
                }
            }
            else if (state.CurrentStep == FbxReplacementStageThreeWorkflowStep.SupplementMaterialReview)
            {
                for (int i = 0; i < state.SupplementMaterialEntries.Count; i++)
                {
                    FbxReplacementMaterialReviewEntry entry = state.SupplementMaterialEntries[i];
                    if (state.ProcessedSupplementEntryKeys.Contains(entry.Key))
                    {
                        continue;
                    }

                    GameObject pendingTarget = ResolveTargetObject(state, entry);
                    if (pendingTarget != null)
                    {
                        result[pendingTarget.GetInstanceID()] = PendingSupplementHighlightColor;
                    }
                }

                GameObject currentTarget = GetCurrentTargetObject(state);
                if (currentTarget != null)
                {
                    result[currentTarget.GetInstanceID()] = CurrentTargetHighlightColor;
                }

                GameObject currentReference = GetCurrentReferenceObject(state);
                if (currentReference != null)
                {
                    result[currentReference.GetInstanceID()] = PendingSupplementHighlightColor;
                }
            }

            return result;
        }

        internal static void SetCurrentMaterialSelection(FbxReplacementStageThreeState state, int slotIndex, Material material)
        {
            if (state == null || state.CurrentMaterialSelections == null || slotIndex < 0 || slotIndex >= state.CurrentMaterialSelections.Length)
            {
                return;
            }

            var selections = state.CurrentMaterialSelections.ToArray();
            selections[slotIndex] = material;
            state.CurrentMaterialSelections = selections;
            PreviewCurrentEntry(state);
        }

        internal static void ConfirmCurrentTargetMaterials(FbxReplacementStageThreeState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            FbxReplacementMaterialReviewEntry entry = GetCurrentTargetEntry(state);
            if (entry == null)
            {
                throw new InvalidOperationException("当前已没有待处理的材质替换对象。");
            }

            var decision = new FbxReplacementMaterialDecisionRecord(
                FbxReplacementMaterialDecisionActionType.ReplaceTargetMaterials,
                entry.Key,
                state.CurrentMaterialSelections);
            ApplyDecision(state, decision);
            state.History.Add(decision);
            PrepareNextStep(state);
        }

        internal static void SkipCurrentTargetMaterials(FbxReplacementStageThreeState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            FbxReplacementMaterialReviewEntry entry = GetCurrentTargetEntry(state);
            if (entry == null)
            {
                throw new InvalidOperationException("当前已没有待处理的材质替换对象。");
            }

            var decision = new FbxReplacementMaterialDecisionRecord(
                FbxReplacementMaterialDecisionActionType.SkipTargetMaterials,
                entry.Key,
                Array.Empty<Material>());
            ApplyDecision(state, decision);
            state.History.Add(decision);
            PrepareNextStep(state);
        }

        internal static void ChooseSupplementReuse(FbxReplacementStageThreeState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (state.SupplementMaterialEntries.Count == 0)
            {
                CompleteStage(state);
                return;
            }

            var decision = new FbxReplacementMaterialDecisionRecord(
                FbxReplacementMaterialDecisionActionType.ChooseSupplementReuse,
                string.Empty,
                Array.Empty<Material>());
            ApplyDecision(state, decision);
            state.History.Add(decision);
            PrepareNextStep(state);
        }

        internal static void ChooseSupplementAdjust(FbxReplacementStageThreeState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (state.SupplementMaterialEntries.Count == 0)
            {
                CompleteStage(state);
                return;
            }

            var decision = new FbxReplacementMaterialDecisionRecord(
                FbxReplacementMaterialDecisionActionType.ChooseSupplementAdjust,
                string.Empty,
                Array.Empty<Material>());
            ApplyDecision(state, decision);
            state.History.Add(decision);
            PrepareNextStep(state);
        }

        internal static void ConfirmCurrentSupplementMaterials(FbxReplacementStageThreeState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            FbxReplacementMaterialReviewEntry entry = GetCurrentSupplementEntry(state);
            if (entry == null)
            {
                throw new InvalidOperationException("当前已没有待处理的补充 Mesh 材质对象。");
            }

            var decision = new FbxReplacementMaterialDecisionRecord(
                FbxReplacementMaterialDecisionActionType.ConfirmSupplementMaterials,
                entry.Key,
                state.CurrentMaterialSelections);
            ApplyDecision(state, decision);
            state.History.Add(decision);
            PrepareNextStep(state);
        }

        internal static bool StepBack(FbxReplacementStageThreeState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (state.History.Count == 0)
            {
                return false;
            }

            FbxReplacementMaterialDecisionRecord undoneDecision = state.History[state.History.Count - 1];
            state.History.RemoveAt(state.History.Count - 1);
            RebuildState(state, undoneDecision);
            return true;
        }

        private static List<FbxReplacementMaterialReviewEntry> BuildTargetMaterialEntries(FbxReplacementStageTwoState stageTwoState)
        {
            var result = new List<FbxReplacementMaterialReviewEntry>();
            if (stageTwoState == null)
            {
                return result;
            }

            for (int i = 0; i < stageTwoState.TargetEntries.Count; i++)
            {
                FbxReplacementStructureTargetEntry targetEntry = stageTwoState.TargetEntries[i];
                if (targetEntry == null)
                {
                    continue;
                }

                GameObject targetObject = ResolveObjectByKey(stageTwoState.CurrentTargetObjectsByKey, targetEntry.Key);
                if (!TryGetMaterialRenderer(targetObject, out Renderer renderer, out int slotCount))
                {
                    continue;
                }

                string referenceKey = stageTwoState.MatchedReferenceKeyByTargetKey.TryGetValue(targetEntry.Key, out string matchedReferenceKey)
                    ? matchedReferenceKey
                    : string.Empty;
                result.Add(new FbxReplacementMaterialReviewEntry(
                    "target-material/" + targetEntry.Key,
                    targetEntry.Key,
                    referenceKey,
                    false,
                    slotCount));
            }

            return result;
        }

        private static List<FbxReplacementMaterialReviewEntry> BuildSupplementMaterialEntries(FbxReplacementStageTwoState stageTwoState)
        {
            var result = new List<FbxReplacementMaterialReviewEntry>();
            if (stageTwoState == null)
            {
                return result;
            }

            for (int i = 0; i < stageTwoState.SupplementEntries.Count; i++)
            {
                FbxReplacementStructureSupplementEntry supplementEntry = stageTwoState.SupplementEntries[i];
                if (supplementEntry == null || !stageTwoState.KeptSupplementKeys.Contains(supplementEntry.Key))
                {
                    continue;
                }

                GameObject supplementObject = ResolveObjectByKey(stageTwoState.CurrentSupplementObjectsByKey, supplementEntry.Key);
                if (!TryGetMaterialRenderer(supplementObject, out Renderer renderer, out int slotCount))
                {
                    continue;
                }

                result.Add(new FbxReplacementMaterialReviewEntry(
                    "supplement-material/" + supplementEntry.Key,
                    supplementEntry.Key,
                    supplementEntry.ReferenceKey,
                    true,
                    slotCount));
            }

            return result;
        }

        private static void InitializeBaselines(FbxReplacementStageThreeState state)
        {
            List<FbxReplacementMaterialReviewEntry> allEntries = GetAllEntries(state);
            for (int i = 0; i < allEntries.Count; i++)
            {
                FbxReplacementMaterialReviewEntry entry = allEntries[i];
                Renderer renderer = ResolveRendererForEntry(state, entry, out _);
                state.BaselineMaterialsByEntryKey[entry.Key] = CloneMaterials(renderer != null ? renderer.sharedMaterials : null, entry.MaterialSlotCount);
            }
        }

        private static void PrepareNextStep(FbxReplacementStageThreeState state)
        {
            if (state == null)
            {
                return;
            }

            int nextTargetIndex = FindNextPendingTargetIndex(state, 0);
            if (nextTargetIndex >= 0)
            {
                PrepareCurrentTargetReview(state, nextTargetIndex, null);
                return;
            }

            if (state.SupplementMaterialEntries.Count == 0)
            {
                CompleteStage(state);
                return;
            }

            if (!state.SupplementReuseChosen && !state.SupplementAdjustChosen)
            {
                if (GetPendingSupplementTargetObjects(state).Length == 0)
                {
                    ChooseSupplementReuse(state);
                    return;
                }

                PrepareSupplementModeSelection(state);
                return;
            }

            if (state.SupplementReuseChosen)
            {
                CompleteStage(state);
                return;
            }

            int nextSupplementIndex = FindNextPendingSupplementIndex(state, 0);
            if (nextSupplementIndex >= 0)
            {
                PrepareCurrentSupplementReview(state, nextSupplementIndex, null);
                return;
            }

            CompleteStage(state);
        }

        private static void PrepareCurrentTargetReview(
            FbxReplacementStageThreeState state,
            int targetIndex,
            Material[] materialSelections)
        {
            if (state == null || targetIndex < 0 || targetIndex >= state.TargetMaterialEntries.Count)
            {
                CompleteStage(state);
                return;
            }

            FbxReplacementMaterialReviewEntry entry = state.TargetMaterialEntries[targetIndex];
            state.CurrentStep = FbxReplacementStageThreeWorkflowStep.TargetMaterialReview;
            state.CurrentTargetIndex = targetIndex;
            state.CurrentSupplementIndex = -1;
            state.CurrentMaterialSelections = materialSelections != null
                ? CloneMaterials(materialSelections, entry.MaterialSlotCount)
                : BuildRecommendedMaterials(state, entry);
            PreviewCurrentEntry(state);
            RevealCurrentObjects(state);
        }

        private static void PrepareSupplementModeSelection(FbxReplacementStageThreeState state)
        {
            if (state == null)
            {
                return;
            }

            state.CurrentStep = FbxReplacementStageThreeWorkflowStep.SupplementModeSelection;
            state.CurrentTargetIndex = -1;
            state.CurrentSupplementIndex = -1;
            state.CurrentMaterialSelections = Array.Empty<Material>();
            RevealCurrentObjects(state);
        }

        private static void PrepareCurrentSupplementReview(
            FbxReplacementStageThreeState state,
            int supplementIndex,
            Material[] materialSelections)
        {
            if (state == null || supplementIndex < 0 || supplementIndex >= state.SupplementMaterialEntries.Count)
            {
                CompleteStage(state);
                return;
            }

            FbxReplacementMaterialReviewEntry entry = state.SupplementMaterialEntries[supplementIndex];
            state.CurrentStep = FbxReplacementStageThreeWorkflowStep.SupplementMaterialReview;
            state.CurrentTargetIndex = -1;
            state.CurrentSupplementIndex = supplementIndex;
            state.CurrentMaterialSelections = materialSelections != null
                ? CloneMaterials(materialSelections, entry.MaterialSlotCount)
                : GetBaselineMaterials(state, entry);
            PreviewCurrentEntry(state);
            RevealCurrentObjects(state);
        }

        private static void CompleteStage(FbxReplacementStageThreeState state)
        {
            if (state == null)
            {
                return;
            }

            state.CurrentStep = FbxReplacementStageThreeWorkflowStep.Completed;
            state.CurrentTargetIndex = -1;
            state.CurrentSupplementIndex = -1;
            state.CurrentMaterialSelections = Array.Empty<Material>();
        }

        private static void PreviewCurrentEntry(FbxReplacementStageThreeState state)
        {
            FbxReplacementMaterialReviewEntry entry = GetCurrentEntry(state);
            Renderer renderer = ResolveRendererForEntry(state, entry, out _);
            if (renderer == null)
            {
                return;
            }

            Material[] appliedMaterials = NormalizeMaterialsForApplication(state.CurrentMaterialSelections, entry.MaterialSlotCount);
            renderer.sharedMaterials = appliedMaterials;
        }

        private static void ApplyDecision(FbxReplacementStageThreeState state, FbxReplacementMaterialDecisionRecord decision)
        {
            if (state == null || decision == null)
            {
                return;
            }

            switch (decision.ActionType)
            {
                case FbxReplacementMaterialDecisionActionType.ReplaceTargetMaterials:
                    FbxReplacementMaterialReviewEntry targetReplaceEntry = FindEntry(state.TargetMaterialEntries, decision.EntryKey);
                    if (targetReplaceEntry == null)
                    {
                        return;
                    }

                    ApplyMaterialsToEntry(state, targetReplaceEntry, decision.Materials);
                    state.ProcessedTargetEntryKeys.Add(targetReplaceEntry.Key);
                    state.ReplacedTargetEntryKeys.Add(targetReplaceEntry.Key);
                    state.SkippedTargetEntryKeys.Remove(targetReplaceEntry.Key);
                    break;

                case FbxReplacementMaterialDecisionActionType.SkipTargetMaterials:
                    FbxReplacementMaterialReviewEntry targetSkipEntry = FindEntry(state.TargetMaterialEntries, decision.EntryKey);
                    if (targetSkipEntry == null)
                    {
                        return;
                    }

                    ResetEntryToBaseline(state, targetSkipEntry);
                    state.ProcessedTargetEntryKeys.Add(targetSkipEntry.Key);
                    state.ReplacedTargetEntryKeys.Remove(targetSkipEntry.Key);
                    state.SkippedTargetEntryKeys.Add(targetSkipEntry.Key);
                    break;

                case FbxReplacementMaterialDecisionActionType.ChooseSupplementReuse:
                    state.SupplementReuseChosen = true;
                    state.SupplementAdjustChosen = false;
                    break;

                case FbxReplacementMaterialDecisionActionType.ChooseSupplementAdjust:
                    state.SupplementReuseChosen = false;
                    state.SupplementAdjustChosen = true;
                    break;

                case FbxReplacementMaterialDecisionActionType.ConfirmSupplementMaterials:
                    FbxReplacementMaterialReviewEntry supplementEntry = FindEntry(state.SupplementMaterialEntries, decision.EntryKey);
                    if (supplementEntry == null)
                    {
                        return;
                    }

                    ApplyMaterialsToEntry(state, supplementEntry, decision.Materials);
                    state.ProcessedSupplementEntryKeys.Add(supplementEntry.Key);
                    break;
            }
        }

        private static void RebuildState(
            FbxReplacementStageThreeState state,
            FbxReplacementMaterialDecisionRecord undoneDecision)
        {
            RevertAllToBaseline(state);
            state.ProcessedTargetEntryKeys.Clear();
            state.ReplacedTargetEntryKeys.Clear();
            state.SkippedTargetEntryKeys.Clear();
            state.ProcessedSupplementEntryKeys.Clear();
            state.SupplementReuseChosen = false;
            state.SupplementAdjustChosen = false;
            state.CurrentTargetIndex = -1;
            state.CurrentSupplementIndex = -1;
            state.CurrentMaterialSelections = Array.Empty<Material>();

            for (int i = 0; i < state.History.Count; i++)
            {
                ApplyDecision(state, state.History[i]);
            }

            if (RestoreUndoneDecisionAsCurrent(state, undoneDecision))
            {
                return;
            }

            PrepareNextStep(state);
        }

        private static bool RestoreUndoneDecisionAsCurrent(
            FbxReplacementStageThreeState state,
            FbxReplacementMaterialDecisionRecord undoneDecision)
        {
            if (state == null || undoneDecision == null)
            {
                return false;
            }

            switch (undoneDecision.ActionType)
            {
                case FbxReplacementMaterialDecisionActionType.ReplaceTargetMaterials:
                    int targetReplaceIndex = FindEntryIndex(state.TargetMaterialEntries, undoneDecision.EntryKey);
                    if (targetReplaceIndex < 0)
                    {
                        return false;
                    }

                    PrepareCurrentTargetReview(state, targetReplaceIndex, undoneDecision.Materials.ToArray());
                    return true;

                case FbxReplacementMaterialDecisionActionType.SkipTargetMaterials:
                    int targetSkipIndex = FindEntryIndex(state.TargetMaterialEntries, undoneDecision.EntryKey);
                    if (targetSkipIndex < 0)
                    {
                        return false;
                    }

                    PrepareCurrentTargetReview(state, targetSkipIndex, null);
                    return true;

                case FbxReplacementMaterialDecisionActionType.ChooseSupplementReuse:
                case FbxReplacementMaterialDecisionActionType.ChooseSupplementAdjust:
                    PrepareSupplementModeSelection(state);
                    return true;

                case FbxReplacementMaterialDecisionActionType.ConfirmSupplementMaterials:
                    int supplementIndex = FindEntryIndex(state.SupplementMaterialEntries, undoneDecision.EntryKey);
                    if (supplementIndex < 0)
                    {
                        return false;
                    }

                    PrepareCurrentSupplementReview(state, supplementIndex, undoneDecision.Materials.ToArray());
                    return true;
            }

            return false;
        }

        private static Material[] BuildRecommendedMaterials(
            FbxReplacementStageThreeState state,
            FbxReplacementMaterialReviewEntry entry)
        {
            if (state == null || entry == null)
            {
                return Array.Empty<Material>();
            }

            Renderer targetRenderer = ResolveRendererForEntry(state, entry, out _);
            Renderer referenceRenderer = ResolveMaterialRenderer(ResolveReferenceObject(state, entry), out _);
            Material[] currentTargetMaterials = CloneMaterials(targetRenderer != null ? targetRenderer.sharedMaterials : null, entry.MaterialSlotCount);
            Material[] referenceMaterials = CloneMaterials(referenceRenderer != null ? referenceRenderer.sharedMaterials : null, entry.MaterialSlotCount);
            var result = new Material[entry.MaterialSlotCount];
            for (int i = 0; i < result.Length; i++)
            {
                Material recommended = null;
                if (referenceMaterials.Length > i && referenceMaterials[i] != null)
                {
                    recommended = referenceMaterials[i];
                }

                if (recommended == null && currentTargetMaterials.Length > i && currentTargetMaterials[i] != null)
                {
                    recommended = currentTargetMaterials[i];
                }

                if (recommended == null)
                {
                    recommended = RecommendMaterialFromWorkspace(state, entry, i, targetRenderer);
                }

                result[i] = recommended;
            }

            return result;
        }

        private static Material RecommendMaterialFromWorkspace(
            FbxReplacementStageThreeState state,
            FbxReplacementMaterialReviewEntry entry,
            int slotIndex,
            Renderer currentRenderer)
        {
            string meshName = GetMeshName(currentRenderer);
            string rendererTypeName = currentRenderer != null ? currentRenderer.GetType().FullName : string.Empty;
            Material bestMaterial = null;
            float bestScore = float.MinValue;
            List<Renderer> candidateRenderers = EnumerateCandidateRenderers(state);
            for (int i = 0; i < candidateRenderers.Count; i++)
            {
                Renderer candidate = candidateRenderers[i];
                if (candidate == null || candidate == currentRenderer)
                {
                    continue;
                }

                Material[] candidateMaterials = CloneMaterials(candidate.sharedMaterials, Math.Max(slotIndex + 1, ResolveMaterialSlotCount(candidate.gameObject, candidate)));
                if (slotIndex >= candidateMaterials.Length || candidateMaterials[slotIndex] == null)
                {
                    continue;
                }

                float score = 0f;
                if (!string.IsNullOrEmpty(meshName) && string.Equals(meshName, GetMeshName(candidate), StringComparison.Ordinal))
                {
                    score += 100f;
                }

                if (ResolveMaterialSlotCount(candidate.gameObject, candidate) == entry.MaterialSlotCount)
                {
                    score += 30f;
                }

                if (!string.IsNullOrEmpty(rendererTypeName)
                    && string.Equals(rendererTypeName, candidate.GetType().FullName, StringComparison.Ordinal))
                {
                    score += 15f;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMaterial = candidateMaterials[slotIndex];
                }
            }

            return bestMaterial != null ? bestMaterial : GetDefaultMaterial();
        }

        private static List<Renderer> EnumerateCandidateRenderers(FbxReplacementStageThreeState state)
        {
            var result = new List<Renderer>();
            if (state == null || state.StageTwoState == null)
            {
                return result;
            }

            var objects = state.StageTwoState.CurrentTargetObjectsByKey.Values
                .Concat(state.StageTwoState.CurrentReferenceObjectsByKey.Values)
                .Concat(state.StageTwoState.CurrentSupplementObjectsByKey.Values)
                .Where(gameObject => gameObject != null)
                .Distinct()
                .ToList();
            for (int i = 0; i < objects.Count; i++)
            {
                if (TryGetMaterialRenderer(objects[i], out Renderer renderer, out _))
                {
                    result.Add(renderer);
                }
            }

            return result;
        }

        private static int FindNextPendingTargetIndex(FbxReplacementStageThreeState state, int startIndex)
        {
            if (state == null)
            {
                return -1;
            }

            for (int i = Mathf.Max(0, startIndex); i < state.TargetMaterialEntries.Count; i++)
            {
                if (!state.ProcessedTargetEntryKeys.Contains(state.TargetMaterialEntries[i].Key))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindNextPendingSupplementIndex(FbxReplacementStageThreeState state, int startIndex)
        {
            if (state == null)
            {
                return -1;
            }

            for (int i = Mathf.Max(0, startIndex); i < state.SupplementMaterialEntries.Count; i++)
            {
                if (!state.ProcessedSupplementEntryKeys.Contains(state.SupplementMaterialEntries[i].Key))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindEntryIndex(List<FbxReplacementMaterialReviewEntry> entries, string entryKey)
        {
            if (entries == null || string.IsNullOrEmpty(entryKey))
            {
                return -1;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].Key, entryKey, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static FbxReplacementMaterialReviewEntry GetCurrentEntry(FbxReplacementStageThreeState state)
        {
            if (state == null)
            {
                return null;
            }

            return state.CurrentStep == FbxReplacementStageThreeWorkflowStep.SupplementMaterialReview
                ? GetCurrentSupplementEntry(state)
                : GetCurrentTargetEntry(state);
        }

        private static FbxReplacementMaterialReviewEntry GetCurrentTargetEntry(FbxReplacementStageThreeState state)
        {
            return state != null
                && state.CurrentTargetIndex >= 0
                && state.CurrentTargetIndex < state.TargetMaterialEntries.Count
                ? state.TargetMaterialEntries[state.CurrentTargetIndex]
                : null;
        }

        private static FbxReplacementMaterialReviewEntry GetCurrentSupplementEntry(FbxReplacementStageThreeState state)
        {
            return state != null
                && state.CurrentSupplementIndex >= 0
                && state.CurrentSupplementIndex < state.SupplementMaterialEntries.Count
                ? state.SupplementMaterialEntries[state.CurrentSupplementIndex]
                : null;
        }

        private static FbxReplacementMaterialReviewEntry FindEntry(
            List<FbxReplacementMaterialReviewEntry> entries,
            string entryKey)
        {
            int index = FindEntryIndex(entries, entryKey);
            return index >= 0 ? entries[index] : null;
        }

        private static List<FbxReplacementMaterialReviewEntry> GetAllEntries(FbxReplacementStageThreeState state)
        {
            var result = new List<FbxReplacementMaterialReviewEntry>();
            if (state == null)
            {
                return result;
            }

            result.AddRange(state.TargetMaterialEntries);
            result.AddRange(state.SupplementMaterialEntries);
            return result;
        }

        private static GameObject ResolveTargetObject(
            FbxReplacementStageThreeState state,
            FbxReplacementMaterialReviewEntry entry)
        {
            if (state == null || entry == null || state.StageTwoState == null)
            {
                return null;
            }

            return entry.IsSupplement
                ? ResolveObjectByKey(state.StageTwoState.CurrentSupplementObjectsByKey, entry.TargetKey)
                : ResolveObjectByKey(state.StageTwoState.CurrentTargetObjectsByKey, entry.TargetKey);
        }

        private static GameObject ResolveReferenceObject(
            FbxReplacementStageThreeState state,
            FbxReplacementMaterialReviewEntry entry)
        {
            if (state == null || entry == null || state.StageTwoState == null || string.IsNullOrEmpty(entry.ReferenceKey))
            {
                return null;
            }

            return ResolveObjectByKey(state.StageTwoState.CurrentReferenceObjectsByKey, entry.ReferenceKey);
        }

        private static Renderer ResolveRendererForEntry(
            FbxReplacementStageThreeState state,
            FbxReplacementMaterialReviewEntry entry,
            out int slotCount)
        {
            return TryGetMaterialRenderer(ResolveTargetObject(state, entry), out Renderer renderer, out slotCount)
                ? renderer
                : null;
        }

        private static Renderer ResolveMaterialRenderer(GameObject gameObject, out int slotCount)
        {
            return TryGetMaterialRenderer(gameObject, out Renderer renderer, out slotCount)
                ? renderer
                : null;
        }

        private static bool TryGetMaterialRenderer(GameObject gameObject, out Renderer renderer, out int slotCount)
        {
            renderer = null;
            slotCount = 0;
            if (gameObject == null)
            {
                return false;
            }

            SkinnedMeshRenderer skinnedMeshRenderer = gameObject.GetComponent<SkinnedMeshRenderer>();
            if (skinnedMeshRenderer != null && skinnedMeshRenderer.sharedMesh != null)
            {
                renderer = skinnedMeshRenderer;
                slotCount = ResolveMaterialSlotCount(gameObject, skinnedMeshRenderer);
                return true;
            }

            MeshRenderer meshRenderer = gameObject.GetComponent<MeshRenderer>();
            MeshFilter meshFilter = gameObject.GetComponent<MeshFilter>();
            if (meshRenderer != null && meshFilter != null && meshFilter.sharedMesh != null)
            {
                renderer = meshRenderer;
                slotCount = ResolveMaterialSlotCount(gameObject, meshRenderer);
                return true;
            }

            return false;
        }

        private static int ResolveMaterialSlotCount(GameObject gameObject, Renderer renderer)
        {
            int materialCount = renderer != null && renderer.sharedMaterials != null
                ? renderer.sharedMaterials.Length
                : 0;
            int subMeshCount = 0;
            if (renderer is SkinnedMeshRenderer skinnedMeshRenderer && skinnedMeshRenderer.sharedMesh != null)
            {
                subMeshCount = skinnedMeshRenderer.sharedMesh.subMeshCount;
            }
            else if (renderer is MeshRenderer && gameObject != null)
            {
                MeshFilter meshFilter = gameObject.GetComponent<MeshFilter>();
                if (meshFilter != null && meshFilter.sharedMesh != null)
                {
                    subMeshCount = meshFilter.sharedMesh.subMeshCount;
                }
            }

            return Math.Max(Math.Max(materialCount, subMeshCount), 1);
        }

        private static Material[] GetBaselineMaterials(
            FbxReplacementStageThreeState state,
            FbxReplacementMaterialReviewEntry entry)
        {
            if (state == null || entry == null)
            {
                return Array.Empty<Material>();
            }

            return state.BaselineMaterialsByEntryKey.TryGetValue(entry.Key, out Material[] materials)
                ? CloneMaterials(materials, entry.MaterialSlotCount)
                : new Material[entry.MaterialSlotCount];
        }

        private static void ResetEntryToBaseline(
            FbxReplacementStageThreeState state,
            FbxReplacementMaterialReviewEntry entry)
        {
            Renderer renderer = ResolveRendererForEntry(state, entry, out _);
            if (renderer == null)
            {
                return;
            }

            renderer.sharedMaterials = GetBaselineMaterials(state, entry);
        }

        private static void ApplyMaterialsToEntry(
            FbxReplacementStageThreeState state,
            FbxReplacementMaterialReviewEntry entry,
            IReadOnlyList<Material> materials)
        {
            Renderer renderer = ResolveRendererForEntry(state, entry, out _);
            if (renderer == null)
            {
                return;
            }

            renderer.sharedMaterials = NormalizeMaterialsForApplication(materials, entry.MaterialSlotCount);
        }

        private static Material[] NormalizeMaterialsForApplication(IReadOnlyList<Material> materials, int slotCount)
        {
            int count = Mathf.Max(1, slotCount);
            var result = new Material[count];
            Material defaultMaterial = GetDefaultMaterial();
            for (int i = 0; i < result.Length; i++)
            {
                Material material = materials != null && i < materials.Count
                    ? materials[i]
                    : null;
                result[i] = material != null ? material : defaultMaterial;
            }

            return result;
        }

        private static Material[] CloneMaterials(IReadOnlyList<Material> materials, int slotCount)
        {
            int count = Mathf.Max(1, slotCount);
            var result = new Material[count];
            if (materials == null)
            {
                return result;
            }

            for (int i = 0; i < result.Length && i < materials.Count; i++)
            {
                result[i] = materials[i];
            }

            return result;
        }

        private static Material GetDefaultMaterial()
        {
            if (_defaultMaterial != null)
            {
                return _defaultMaterial;
            }

            _defaultMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
            if (_defaultMaterial == null)
            {
                _defaultMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Diffuse.mat");
            }

            return _defaultMaterial;
        }

        private static string GetMeshName(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
            {
                return skinnedMeshRenderer.sharedMesh != null ? skinnedMeshRenderer.sharedMesh.name : string.Empty;
            }

            if (renderer is MeshRenderer && renderer != null)
            {
                MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
                return meshFilter != null && meshFilter.sharedMesh != null ? meshFilter.sharedMesh.name : string.Empty;
            }

            return string.Empty;
        }

        private static GameObject ResolveObjectByKey(Dictionary<string, GameObject> objectMap, string key)
        {
            return objectMap != null && !string.IsNullOrEmpty(key) && objectMap.TryGetValue(key, out GameObject gameObject)
                ? gameObject
                : null;
        }
    }
}


