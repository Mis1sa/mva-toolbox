using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.FbxReplacement
{
    internal static partial class FbxReplacementStructureWorkflow
    {
        private const string RootHierarchyKey = "@root";
        internal static readonly Color CurrentTargetHighlightColor = new Color(0.25f, 0.8f, 0.3f, 0.32f);
        internal static readonly Color SelectedReferenceHighlightColor = new Color(1f, 0.85f, 0.2f, 0.34f);
        internal static readonly Color ConfirmedPairHighlightColor = new Color(0.3f, 0.55f, 1f, 0.34f);
        internal static readonly Color PreservedTargetHighlightColor = new Color(0.75f, 0.35f, 1f, 0.34f);
        internal static readonly Color PendingSupplementHighlightColor = new Color(1f, 0.85f, 0.2f, 0.34f);

        internal static FbxReplacementStageTwoState CreateState(FbxReplacementStageOneState stageOneState)
        {
            if (stageOneState == null)
            {
                throw new ArgumentNullException(nameof(stageOneState));
            }

            return CreateState(stageOneState.Session);
        }

        internal static FbxReplacementStageTwoState CreateState(FbxReplacementSessionState sessionState)
        {
            if (sessionState == null)
            {
                throw new ArgumentNullException(nameof(sessionState));
            }

            if (sessionState.Workspace == null || !sessionState.Workspace.Scene.IsValid())
            {
                throw new InvalidOperationException("当前阶段1工作区已失效，请重新开始。");
            }

            if (sessionState.Workspace.ReferenceInstanceRoot == null || sessionState.Workspace.TargetWorkspaceRoot == null)
            {
                throw new InvalidOperationException("当前阶段1工作区缺少结构对齐所需的物体副本。");
            }

            GameObject baselineReferenceTemplate = CreateHiddenTemplate(sessionState.Workspace.ReferenceInstanceRoot, sessionState.Workspace.Scene);
            GameObject baselineTargetTemplate = CreateHiddenTemplate(sessionState.Workspace.TargetWorkspaceRoot, sessionState.Workspace.Scene);
            string originalTargetRootKey = GetHierarchyIndexPath(sessionState.Workspace.TargetWorkspaceRoot.transform, sessionState.Workspace.TargetOriginalRoot != null ? sessionState.Workspace.TargetOriginalRoot.transform : null);
            var referenceEntries = CollectReferenceEntries(baselineReferenceTemplate.transform);
            var targetEntries = CollectTargetEntries(baselineTargetTemplate.transform, originalTargetRootKey);
            var state = new FbxReplacementStageTwoState(
                sessionState,
                baselineReferenceTemplate,
                baselineTargetTemplate,
                originalTargetRootKey,
                referenceEntries,
                targetEntries);

            state.CurrentReferenceObjectsByKey = BuildObjectMap(state.ReferenceEntries, sessionState.Workspace.ReferenceInstanceRoot);
            state.CurrentTargetObjectsByKey = BuildObjectMap(state.TargetEntries, sessionState.Workspace.TargetWorkspaceRoot);
            BuildInitialRecommendations(state);
            PrepareCurrentTarget(state, 0);
            return state;
        }

        internal static void DisposeState(FbxReplacementStageTwoState state)
        {
            if (state == null)
            {
                return;
            }

            if (state.BaselineReferenceTemplate != null)
            {
                Object.DestroyImmediate(state.BaselineReferenceTemplate);
                state.BaselineReferenceTemplate = null;
            }

            if (state.BaselineTargetTemplate != null)
            {
                Object.DestroyImmediate(state.BaselineTargetTemplate);
                state.BaselineTargetTemplate = null;
            }

            state.CurrentReferenceObjectsByKey.Clear();
            state.CurrentTargetObjectsByKey.Clear();
            state.History.Clear();
            state.MatchedReferenceKeys.Clear();
            state.MatchedTargetKeys.Clear();
            state.PreservedTargetKeys.Clear();
            state.RemovedTargetKeys.Clear();
            state.AcceptedSupplementReferenceKeys.Clear();
            state.KeptSupplementKeys.Clear();
            state.RemovedSupplementKeys.Clear();
            state.CandidateReferenceKeysByTargetKey.Clear();
            state.RecommendedReferenceKeyByTargetKey.Clear();
            state.CurrentSupplementObjectsByKey.Clear();
            state.MatchedTargetKeyByReferenceKey.Clear();
            state.MatchedReferenceKeyByTargetKey.Clear();
            state.SupplementEntries.Clear();
            state.SelectedReferenceKey = null;
            state.CurrentTargetIndex = -1;
            state.CurrentSupplementIndex = -1;
            state.IncludeChildren = false;
            state.AffectChildren = true;
            state.ReferenceSupplementsInitialized = false;
            state.CurrentStep = FbxReplacementStageTwoWorkflowStep.StructureAlignment;
        }

        internal static Dictionary<int, Color> BuildHighlightMap(FbxReplacementStageTwoState state)
        {
            var result = new Dictionary<int, Color>();
            if (state == null)
            {
                return result;
            }

            bool isSupplementReview = state.CurrentStep == FbxReplacementStageTwoWorkflowStep.ReferenceSupplementReview;
            if (!isSupplementReview)
            {
                foreach (string key in state.MatchedReferenceKeys)
                {
                    if (state.CurrentReferenceObjectsByKey.TryGetValue(key, out GameObject referenceObject) && referenceObject != null)
                    {
                        result[referenceObject.GetInstanceID()] = ConfirmedPairHighlightColor;
                    }
                }

                foreach (string key in state.AcceptedSupplementReferenceKeys)
                {
                    if (state.CurrentReferenceObjectsByKey.TryGetValue(key, out GameObject referenceObject) && referenceObject != null)
                    {
                        result[referenceObject.GetInstanceID()] = ConfirmedPairHighlightColor;
                    }
                }
            }

            foreach (string key in state.MatchedTargetKeys)
            {
                if (state.CurrentTargetObjectsByKey.TryGetValue(key, out GameObject targetObject) && targetObject != null)
                {
                    result[targetObject.GetInstanceID()] = ConfirmedPairHighlightColor;
                }
            }

            foreach (string key in state.PreservedTargetKeys)
            {
                if (state.CurrentTargetObjectsByKey.TryGetValue(key, out GameObject targetObject) && targetObject != null)
                {
                    result[targetObject.GetInstanceID()] = PreservedTargetHighlightColor;
                }
            }

            foreach (string key in state.KeptSupplementKeys)
            {
                if (state.CurrentSupplementObjectsByKey.TryGetValue(key, out GameObject supplementObject) && supplementObject != null)
                {
                    result[supplementObject.GetInstanceID()] = ConfirmedPairHighlightColor;
                }
            }

            for (int i = 0; i < state.SupplementEntries.Count; i++)
            {
                FbxReplacementStructureSupplementEntry supplementEntry = state.SupplementEntries[i];
                if (supplementEntry == null
                    || state.KeptSupplementKeys.Contains(supplementEntry.Key)
                    || state.RemovedSupplementKeys.Contains(supplementEntry.Key))
                {
                    continue;
                }

                if (state.CurrentSupplementObjectsByKey.TryGetValue(supplementEntry.Key, out GameObject supplementObject) && supplementObject != null)
                {
                    result[supplementObject.GetInstanceID()] = PendingSupplementHighlightColor;
                }
            }

            if (state.CurrentStep == FbxReplacementStageTwoWorkflowStep.StructureAlignment)
            {
                GameObject currentTarget = GetCurrentTargetObject(state);
                if (currentTarget != null)
                {
                    result[currentTarget.GetInstanceID()] = CurrentTargetHighlightColor;
                }

                GameObject selectedReference = GetSelectedReferenceObject(state);
                if (selectedReference != null)
                {
                    result[selectedReference.GetInstanceID()] = SelectedReferenceHighlightColor;
                }
            }
            else if (isSupplementReview)
            {
                GameObject currentTarget = GetCurrentTargetObject(state);
                if (currentTarget != null)
                {
                    result[currentTarget.GetInstanceID()] = CurrentTargetHighlightColor;
                }

                GameObject currentReference = GetCurrentSupplementReferenceObject(state);
                if (currentReference != null)
                {
                    result[currentReference.GetInstanceID()] = SelectedReferenceHighlightColor;
                }
            }

            return result;
        }

        internal static FbxReplacementStageTwoWorkflowStep GetCurrentStep(FbxReplacementStageTwoState state)
        {
            return state != null ? state.CurrentStep : FbxReplacementStageTwoWorkflowStep.StructureAlignment;
        }

        internal static GameObject GetCurrentTargetObject(FbxReplacementStageTwoState state)
        {
            if (state != null && state.CurrentStep == FbxReplacementStageTwoWorkflowStep.ReferenceSupplementReview)
            {
                FbxReplacementStructureSupplementEntry supplementEntry = GetCurrentSupplementEntry(state);
                if (supplementEntry == null)
                {
                    return null;
                }

                return state.CurrentSupplementObjectsByKey.TryGetValue(supplementEntry.Key, out GameObject supplementObject)
                    ? supplementObject
                    : null;
            }

            FbxReplacementStructureTargetEntry entry = GetCurrentTargetEntry(state);
            if (entry == null)
            {
                return null;
            }

            return state.CurrentTargetObjectsByKey.TryGetValue(entry.Key, out GameObject targetObject)
                ? targetObject
                : null;
        }

        internal static GameObject GetSelectedReferenceObject(FbxReplacementStageTwoState state)
        {
            if (state == null
                || state.CurrentStep != FbxReplacementStageTwoWorkflowStep.StructureAlignment
                || state.SelectedReferenceKey == null)
            {
                return null;
            }

            return state.CurrentReferenceObjectsByKey.TryGetValue(state.SelectedReferenceKey, out GameObject referenceObject)
                ? referenceObject
                : null;
        }

        internal static GameObject GetCurrentSupplementReferenceObject(FbxReplacementStageTwoState state)
        {
            if (state == null || state.CurrentStep != FbxReplacementStageTwoWorkflowStep.ReferenceSupplementReview)
            {
                return null;
            }

            FbxReplacementStructureSupplementEntry supplementEntry = GetCurrentSupplementEntry(state);
            if (supplementEntry == null)
            {
                return null;
            }

            return state.CurrentReferenceObjectsByKey.TryGetValue(supplementEntry.ReferenceKey, out GameObject referenceObject)
                ? referenceObject
                : null;
        }

        internal static bool CurrentSupplementIsAncestor(FbxReplacementStageTwoState state)
        {
            FbxReplacementStructureSupplementEntry supplementEntry = GetCurrentSupplementEntry(state);
            return supplementEntry != null && supplementEntry.Mode == FbxReplacementStructureSupplementMode.Ancestor;
        }

        internal static bool CurrentSupplementCanIncludeChildren(FbxReplacementStageTwoState state)
        {
            FbxReplacementStructureSupplementEntry supplementEntry = GetCurrentSupplementEntry(state);
            return supplementEntry != null && supplementEntry.Mode == FbxReplacementStructureSupplementMode.Descendant;
        }

        internal static void RevealCurrentObjects(FbxReplacementStageTwoState state)
        {
            if (state == null)
            {
                return;
            }

            GameObject currentTarget = GetCurrentTargetObject(state);
            GameObject referenceObject = state.CurrentStep == FbxReplacementStageTwoWorkflowStep.ReferenceSupplementReview
                ? GetCurrentSupplementReferenceObject(state)
                : GetSelectedReferenceObject(state);
            FbxReplacementHierarchyHighlighter.RevealHierarchyObjects(currentTarget, referenceObject);
        }

        internal static bool IsReferenceCandidateValid(FbxReplacementStageTwoState state, GameObject candidate)
        {
            if (state == null)
            {
                return false;
            }

            if (candidate == null)
            {
                return true;
            }

            string key = FindReferenceKey(state, candidate);
            return key != null && !state.MatchedReferenceKeys.Contains(key);
        }

        internal static void SetSelectedReferenceCandidate(FbxReplacementStageTwoState state, GameObject candidate)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (candidate == null)
            {
                state.SelectedReferenceKey = null;
                UpdateCurrentPreview(state);
                return;
            }

            string key = FindReferenceKey(state, candidate);
            if (key == null)
            {
                throw new InvalidOperationException("对齐物体只能选择旧物体副本中的物体。");
            }

            if (state.MatchedReferenceKeys.Contains(key))
            {
                throw new InvalidOperationException("该旧物体已在前面的步骤中完成匹配，不能重复选择。");
            }

            state.SelectedReferenceKey = key;
            UpdateCurrentPreview(state);
        }

        internal static void SetAlignmentOptions(FbxReplacementStageTwoState state, bool alignName, bool alignTransform)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            state.AlignName = state.CurrentStep == FbxReplacementStageTwoWorkflowStep.StructureAlignment && state.SelectedReferenceKey != null
                ? alignName
                : false;
            state.AlignTransform = alignTransform;
            UpdateCurrentPreview(state);
        }

        internal static void SetSupplementOptions(FbxReplacementStageTwoState state, bool alignTransform, bool includeChildren, bool affectChildren)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            state.AlignName = false;
            state.AlignTransform = alignTransform;
            state.IncludeChildren = includeChildren;
            state.AffectChildren = affectChildren;
            UpdateCurrentPreview(state);
        }

        internal static void RevertCurrentPreview(FbxReplacementStageTwoState state)
        {
            if (state == null)
            {
                return;
            }

            if (state.CurrentStep == FbxReplacementStageTwoWorkflowStep.ReferenceSupplementReview)
            {
                RestoreCurrentSupplementBaseline(state);
                return;
            }

            RestoreCurrentTargetBaseline(state);
        }

        internal static void ConfirmCurrentMatch(FbxReplacementStageTwoState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            FbxReplacementStructureTargetEntry targetEntry = GetCurrentTargetEntry(state);
            if (targetEntry == null)
            {
                throw new InvalidOperationException("当前已没有待处理的新物体。");
            }

            if (state.SelectedReferenceKey == null)
            {
                throw new InvalidOperationException("请先指定对齐物体后再确认。");
            }

            if (state.MatchedReferenceKeys.Contains(state.SelectedReferenceKey))
            {
                throw new InvalidOperationException("当前选择的旧物体已被占用，请重新选择。");
            }

            var decision = new FbxReplacementStructureDecisionRecord(
                FbxReplacementStructureAlignmentActionType.Match,
                targetEntry.Key,
                state.SelectedReferenceKey,
                state.AlignName,
                state.AlignTransform,
                false,
                true);
            ApplyDecision(state, decision);
            state.History.Add(decision);
            PrepareCurrentTarget(state, state.CurrentTargetIndex + 1);
        }

        internal static void KeepCurrentTarget(FbxReplacementStageTwoState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            FbxReplacementStructureTargetEntry targetEntry = GetCurrentTargetEntry(state);
            if (targetEntry == null)
            {
                throw new InvalidOperationException("当前已没有待处理的新物体。");
            }

            RestoreCurrentTargetBaseline(state);
            var decision = new FbxReplacementStructureDecisionRecord(
                FbxReplacementStructureAlignmentActionType.Keep,
                targetEntry.Key,
                string.Empty,
                false,
                false,
                false,
                true);
            ApplyDecision(state, decision);
            state.History.Add(decision);
            PrepareCurrentTarget(state, state.CurrentTargetIndex + 1);
        }

        internal static void RemoveCurrentTarget(FbxReplacementStageTwoState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            FbxReplacementStructureTargetEntry targetEntry = GetCurrentTargetEntry(state);
            if (targetEntry == null)
            {
                throw new InvalidOperationException("当前已没有待处理的新物体。");
            }

            RestoreCurrentTargetBaseline(state);
            var decision = new FbxReplacementStructureDecisionRecord(
                FbxReplacementStructureAlignmentActionType.Remove,
                targetEntry.Key,
                string.Empty,
                false,
                false,
                false,
                true);
            ApplyDecision(state, decision);
            state.History.Add(decision);
            PrepareCurrentTarget(state, state.CurrentTargetIndex + 1);
        }

        internal static void KeepCurrentSupplement(FbxReplacementStageTwoState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            FbxReplacementStructureSupplementEntry supplementEntry = GetCurrentSupplementEntry(state);
            if (supplementEntry == null)
            {
                throw new InvalidOperationException("当前已没有待处理的补全物体。");
            }

            bool includeChildren = state.IncludeChildren && CurrentSupplementCanIncludeChildren(state);
            bool affectChildren = state.AffectChildren && CurrentSupplementIsAncestor(state);

            var decision = new FbxReplacementStructureDecisionRecord(
                FbxReplacementStructureAlignmentActionType.SupplementKeep,
                supplementEntry.Key,
                supplementEntry.ReferenceKey,
                false,
                state.AlignTransform,
                includeChildren,
                affectChildren);
            ApplyDecision(state, decision);
            state.History.Add(decision);
            PrepareCurrentSupplement(state, state.CurrentSupplementIndex + 1);
        }

        internal static void RemoveCurrentSupplement(FbxReplacementStageTwoState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            FbxReplacementStructureSupplementEntry supplementEntry = GetCurrentSupplementEntry(state);
            if (supplementEntry == null)
            {
                throw new InvalidOperationException("当前已没有待处理的补全物体。");
            }

            bool includeChildren = state.IncludeChildren && CurrentSupplementCanIncludeChildren(state);
            bool affectChildren = state.AffectChildren && CurrentSupplementIsAncestor(state);

            var decision = new FbxReplacementStructureDecisionRecord(
                FbxReplacementStructureAlignmentActionType.SupplementRemove,
                supplementEntry.Key,
                supplementEntry.ReferenceKey,
                false,
                state.AlignTransform,
                includeChildren,
                affectChildren);
            ApplyDecision(state, decision);
            state.History.Add(decision);
            PrepareCurrentSupplement(state, state.CurrentSupplementIndex + 1);
        }

        internal static void StepBack(FbxReplacementStageTwoState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (state.History.Count == 0)
            {
                return;
            }

            if (state.CurrentStep == FbxReplacementStageTwoWorkflowStep.Completed)
            {
                FbxReplacementStructureDecisionRecord lastDecision = state.History[state.History.Count - 1];
                RestoreUndoneDecisionAsCurrent(state, lastDecision);
                state.History.RemoveAt(state.History.Count - 1);
                return;
            }

            FbxReplacementStructureDecisionRecord undoneDecision = state.History[state.History.Count - 1];
            state.History.RemoveAt(state.History.Count - 1);
            RebuildCurrentWorkspace(state, undoneDecision);
        }

        internal static int GetProcessedCount(FbxReplacementStageTwoState state)
        {
            return state != null ? state.History.Count : 0;
        }

        internal static int GetSupplementCandidateCount(FbxReplacementStageTwoState state)
        {
            return state != null ? state.SupplementEntries.Count : 0;
        }

        internal static int GetSupplementProcessedCount(FbxReplacementStageTwoState state)
        {
            return state != null ? state.KeptSupplementKeys.Count + state.RemovedSupplementKeys.Count : 0;
        }

        internal static int GetKeptSupplementCount(FbxReplacementStageTwoState state)
        {
            return state != null ? state.KeptSupplementKeys.Count : 0;
        }

        internal static int GetRemovedSupplementCount(FbxReplacementStageTwoState state)
        {
            return state != null ? state.RemovedSupplementKeys.Count : 0;
        }

        private static void RebuildCurrentWorkspace(
            FbxReplacementStageTwoState state,
            FbxReplacementStructureDecisionRecord undoneDecision = null)
        {
            string selectedReferenceRootRelativePath = GetCurrentSelectedReferenceRootRelativePath(state);
            DestroyVisibleWorkspaceCopies(state);

            GameObject newReferenceRoot = InstantiateVisibleClone(state.BaselineReferenceTemplate, state.SessionState.Workspace.Scene);
            GameObject newTargetRoot = InstantiateVisibleClone(state.BaselineTargetTemplate, state.SessionState.Workspace.Scene);
            if (newReferenceRoot == null || newTargetRoot == null)
            {
                throw new InvalidOperationException("无法重建阶段2工作区。");
            }

            newReferenceRoot.transform.SetSiblingIndex(0);
            if (state.SessionState.Workspace.SeparatorObject != null)
            {
                state.SessionState.Workspace.SeparatorObject.transform.SetSiblingIndex(1);
            }

            newTargetRoot.transform.SetSiblingIndex(2);
            state.SessionState.Workspace.ReferenceInstanceRoot = newReferenceRoot;
            state.SessionState.Workspace.TargetWorkspaceRoot = newTargetRoot;
            state.CurrentReferenceObjectsByKey = BuildObjectMap(state.ReferenceEntries, newReferenceRoot);
            state.CurrentTargetObjectsByKey = BuildObjectMap(state.TargetEntries, newTargetRoot);
            state.SessionState.Workspace.TargetOriginalRoot = ResolveObjectByKey(state.CurrentTargetObjectsByKey, state.OriginalTargetRootKey);
            state.SessionState.SelectedReferenceRootSource = ResolveReferenceRootAfterRebuild(state, selectedReferenceRootRelativePath, newReferenceRoot);
            state.SessionState.Workspace.CreatedRootPathObjects = CollectCreatedRootPathObjects(
                state.SessionState.Workspace.TargetWorkspaceRoot,
                state.SessionState.Workspace.TargetOriginalRoot);
            state.CurrentSupplementObjectsByKey.Clear();
            state.SupplementEntries.Clear();
            state.MatchedReferenceKeys.Clear();
            state.MatchedTargetKeys.Clear();
            state.PreservedTargetKeys.Clear();
            state.RemovedTargetKeys.Clear();
            state.AcceptedSupplementReferenceKeys.Clear();
            state.KeptSupplementKeys.Clear();
            state.RemovedSupplementKeys.Clear();
            state.MatchedTargetKeyByReferenceKey.Clear();
            state.MatchedReferenceKeyByTargetKey.Clear();
            state.IncludeChildren = false;
            state.AffectChildren = true;
            state.ReferenceSupplementsInitialized = false;

            var supplementHistory = new List<FbxReplacementStructureDecisionRecord>();
            for (int i = 0; i < state.History.Count; i++)
            {
                FbxReplacementStructureDecisionRecord decision = state.History[i];
                if (IsSupplementDecision(decision))
                {
                    supplementHistory.Add(decision);
                    continue;
                }

                ApplyDecision(state, decision);
            }

            if (FindNextPendingTargetIndex(state, 0) < 0)
            {
                EnsureReferenceSupplements(state);
            }

            for (int i = 0; i < supplementHistory.Count; i++)
            {
                ApplyDecision(state, supplementHistory[i]);
                EnsureReferenceSupplements(state);
            }

            if (RestoreUndoneDecisionAsCurrent(state, undoneDecision))
            {
                return;
            }

            PrepareCurrentTarget(state, 0);
        }

        private static bool RestoreUndoneDecisionAsCurrent(
            FbxReplacementStageTwoState state,
            FbxReplacementStructureDecisionRecord undoneDecision)
        {
            if (state == null || undoneDecision == null)
            {
                return false;
            }

            if (IsSupplementDecision(undoneDecision))
            {
                int supplementIndex = FindSupplementEntryIndex(state, undoneDecision.TargetKey);
                if (supplementIndex < 0)
                {
                    return false;
                }

                state.CurrentStep = FbxReplacementStageTwoWorkflowStep.ReferenceSupplementReview;
                state.CurrentTargetIndex = -1;
                state.CurrentSupplementIndex = supplementIndex;
                state.SelectedReferenceKey = null;
                state.AlignName = false;
                state.AlignTransform = undoneDecision.ActionType == FbxReplacementStructureAlignmentActionType.SupplementKeep
                    ? undoneDecision.AlignTransform
                    : true;
                state.IncludeChildren = undoneDecision.IncludeChildren;
                state.AffectChildren = undoneDecision.AffectChildren;
                UpdateCurrentPreview(state);
                RevealCurrentObjects(state);

                return true;
            }

            int targetIndex = FindTargetEntryIndex(state, undoneDecision.TargetKey);
            if (targetIndex < 0)
            {
                return false;
            }

            state.CurrentTargetIndex = targetIndex;
            state.CurrentStep = FbxReplacementStageTwoWorkflowStep.StructureAlignment;
            state.CurrentSupplementIndex = -1;
            state.IncludeChildren = false;
            state.AffectChildren = true;

            if (undoneDecision.ActionType == FbxReplacementStructureAlignmentActionType.Match
                && !string.IsNullOrEmpty(undoneDecision.ReferenceKey)
                && !state.MatchedReferenceKeys.Contains(undoneDecision.ReferenceKey))
            {
                state.SelectedReferenceKey = undoneDecision.ReferenceKey;
                state.AlignName = undoneDecision.AlignName;
                state.AlignTransform = undoneDecision.AlignTransform;
            }
            else
            {
                state.AlignName = false;
                state.AlignTransform = true;
                FbxReplacementStructureTargetEntry targetEntry = GetCurrentTargetEntry(state);
                state.SelectedReferenceKey = targetEntry != null ? FindRecommendedReferenceKey(state, targetEntry) : null;
            }

            UpdateCurrentPreview(state);
            RevealCurrentObjects(state);

            return true;
        }

        private static void ApplyDecision(FbxReplacementStageTwoState state, FbxReplacementStructureDecisionRecord decision)
        {
            if (state == null || decision == null)
            {
                return;
            }

            GameObject targetObject = ResolveObjectByKey(state.CurrentTargetObjectsByKey, decision.TargetKey);
            switch (decision.ActionType)
            {
                case FbxReplacementStructureAlignmentActionType.Match:
                    GameObject referenceObject = ResolveObjectByKey(state.CurrentReferenceObjectsByKey, decision.ReferenceKey);
                    if (targetObject == null || referenceObject == null)
                    {
                        throw new InvalidOperationException("阶段2中的匹配对象已失效，请返回上一步或重新开始。");
                    }

                    if (decision.AlignName)
                    {
                        targetObject.name = referenceObject.name;
                    }

                    if (decision.AlignTransform)
                    {
                        Transform targetTransform = targetObject.transform;
                        Transform referenceTransform = referenceObject.transform;
                        targetTransform.localPosition = referenceTransform.localPosition;
                        targetTransform.localRotation = referenceTransform.localRotation;
                        targetTransform.localScale = referenceTransform.localScale;
                    }

                    state.MatchedTargetKeys.Add(decision.TargetKey);
                    state.MatchedReferenceKeys.Add(decision.ReferenceKey);
                    state.MatchedTargetKeyByReferenceKey[decision.ReferenceKey] = decision.TargetKey;
                    state.MatchedReferenceKeyByTargetKey[decision.TargetKey] = decision.ReferenceKey;
                    break;

                case FbxReplacementStructureAlignmentActionType.Keep:
                    state.PreservedTargetKeys.Add(decision.TargetKey);
                    break;

                case FbxReplacementStructureAlignmentActionType.Remove:
                    state.RemovedTargetKeys.Add(decision.TargetKey);
                    if (targetObject == null)
                    {
                        break;
                    }

                    Transform parent = targetObject.transform.parent;
                    var children = new List<Transform>(targetObject.transform.childCount);
                    for (int i = 0; i < targetObject.transform.childCount; i++)
                    {
                        children.Add(targetObject.transform.GetChild(i));
                    }

                    for (int i = 0; i < children.Count; i++)
                    {
                        children[i].SetParent(parent, true);
                    }

                    if (state.SessionState.Workspace.TargetOriginalRoot == targetObject)
                    {
                        state.SessionState.Workspace.TargetOriginalRoot = null;
                    }

                    if (state.SessionState.Workspace.TargetWorkspaceRoot == targetObject)
                    {
                        state.SessionState.Workspace.TargetWorkspaceRoot = null;
                    }

                    state.CurrentTargetObjectsByKey[decision.TargetKey] = null;
                    Object.DestroyImmediate(targetObject);
                    break;

                case FbxReplacementStructureAlignmentActionType.SupplementKeep:
                    GameObject supplementObject = ResolveSupplementObjectByKey(state, decision.TargetKey);
                    if (supplementObject == null)
                    {
                        throw new InvalidOperationException("阶段2中的补全对象已失效，请返回上一步或重新开始。");
                    }

                    List<string> keptDecisionKeys = GetSupplementDecisionKeys(state, decision.TargetKey, decision.IncludeChildren);
                    for (int i = 0; i < keptDecisionKeys.Count; i++)
                    {
                        RestoreSupplementBaseline(ResolveSupplementObjectByKey(state, keptDecisionKeys[i]));
                    }

                    ApplySupplementTransform(state, decision.TargetKey, decision.IncludeChildren, decision.AlignTransform, decision.AffectChildren);

                    for (int i = 0; i < keptDecisionKeys.Count; i++)
                    {
                        state.KeptSupplementKeys.Add(keptDecisionKeys[i]);
                        FbxReplacementStructureSupplementEntry keptEntry = state.SupplementEntries.FirstOrDefault(entry =>
                            entry != null && string.Equals(entry.Key, keptDecisionKeys[i], StringComparison.Ordinal));
                        if (keptEntry != null)
                        {
                            state.AcceptedSupplementReferenceKeys.Add(keptEntry.ReferenceKey);
                        }
                    }

                    break;

                case FbxReplacementStructureAlignmentActionType.SupplementRemove:
                    GameObject removedSupplementObject = ResolveSupplementObjectByKey(state, decision.TargetKey);
                    if (removedSupplementObject == null)
                    {
                        break;
                    }

                    List<string> removedDecisionKeys = GetSupplementDecisionKeys(state, decision.TargetKey, decision.IncludeChildren);
                    for (int i = 0; i < removedDecisionKeys.Count; i++)
                    {
                        state.RemovedSupplementKeys.Add(removedDecisionKeys[i]);
                    }

                    if (decision.IncludeChildren)
                    {
                        for (int i = 0; i < removedDecisionKeys.Count; i++)
                        {
                            state.CurrentSupplementObjectsByKey[removedDecisionKeys[i]] = null;
                        }

                        Object.DestroyImmediate(removedSupplementObject);
                        break;
                    }

                    Transform supplementParent = removedSupplementObject.transform.parent;
                    var supplementChildren = new List<Transform>(removedSupplementObject.transform.childCount);
                    for (int i = 0; i < removedSupplementObject.transform.childCount; i++)
                    {
                        supplementChildren.Add(removedSupplementObject.transform.GetChild(i));
                    }

                    for (int i = 0; i < supplementChildren.Count; i++)
                    {
                        supplementChildren[i].SetParent(supplementParent, true);
                    }

                    FbxReplacementStructureSupplementEntry removedEntry = state.SupplementEntries.FirstOrDefault(entry =>
                        entry != null && string.Equals(entry.Key, decision.TargetKey, StringComparison.Ordinal));
                    Transform removedReferenceTransform = removedEntry != null && state.BaselineReferenceTemplate != null
                        ? ResolveTransformByKey(state.BaselineReferenceTemplate.transform, removedEntry.ReferenceKey)
                        : null;
                    ApplyReferenceSiblingOrder(state, supplementParent, removedReferenceTransform);

                    state.CurrentSupplementObjectsByKey[decision.TargetKey] = null;
                    Object.DestroyImmediate(removedSupplementObject);
                    break;
            }

            RefreshWorkspaceTargetRoot(state);
        }

        private static void PrepareCurrentTarget(FbxReplacementStageTwoState state, int startIndex)
        {
            state.AlignName = false;
            state.AlignTransform = true;
            state.IncludeChildren = false;
            state.AffectChildren = true;
            state.CurrentTargetIndex = FindNextPendingTargetIndex(state, startIndex);
            if (state.CurrentTargetIndex < 0)
            {
                state.SelectedReferenceKey = null;
                state.CurrentTargetIndex = -1;
                PrepareCurrentSupplement(state, 0);
                return;
            }

            state.CurrentStep = FbxReplacementStageTwoWorkflowStep.StructureAlignment;
            state.CurrentSupplementIndex = -1;
            FbxReplacementStructureTargetEntry targetEntry = GetCurrentTargetEntry(state);
            state.SelectedReferenceKey = targetEntry != null ? FindRecommendedReferenceKey(state, targetEntry) : null;
            UpdateCurrentPreview(state);
            RevealCurrentObjects(state);
        }

        private static void PrepareCurrentSupplement(FbxReplacementStageTwoState state, int startIndex)
        {
            state.AlignName = false;
            state.SelectedReferenceKey = null;
            state.AlignTransform = true;
            if (startIndex == 0)
            {
                state.IncludeChildren = false;
            }
            
            state.AffectChildren = true;

            if (!EnsureReferenceSupplements(state))
            {
                state.CurrentStep = FbxReplacementStageTwoWorkflowStep.Completed;
                state.CurrentSupplementIndex = -1;
                return;
            }

            state.CurrentSupplementIndex = FindNextPendingSupplementIndex(state, startIndex);
            if (state.CurrentSupplementIndex < 0)
            {
                state.CurrentStep = FbxReplacementStageTwoWorkflowStep.Completed;
                state.CurrentSupplementIndex = -1;
                return;
            }

            state.CurrentStep = FbxReplacementStageTwoWorkflowStep.ReferenceSupplementReview;
            state.CurrentTargetIndex = -1;
            UpdateCurrentPreview(state);
            RevealCurrentObjects(state);
        }

        private static int FindNextPendingTargetIndex(FbxReplacementStageTwoState state, int startIndex)
        {
            if (state == null)
            {
                return -1;
            }

            int safeStartIndex = Mathf.Max(0, startIndex);
            for (int i = safeStartIndex; i < state.TargetEntries.Count; i++)
            {
                string key = state.TargetEntries[i].Key;
                if (!state.MatchedTargetKeys.Contains(key) && !state.PreservedTargetKeys.Contains(key) && !state.RemovedTargetKeys.Contains(key))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindSupplementEntryIndex(FbxReplacementStageTwoState state, string supplementKey)
        {
            if (state == null || string.IsNullOrEmpty(supplementKey))
            {
                return -1;
            }

            for (int i = 0; i < state.SupplementEntries.Count; i++)
            {
                if (string.Equals(state.SupplementEntries[i].Key, supplementKey, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindNextPendingSupplementIndex(FbxReplacementStageTwoState state, int startIndex)
        {
            if (state == null)
            {
                return -1;
            }

            int safeStartIndex = Mathf.Max(0, startIndex);
            for (int i = safeStartIndex; i < state.SupplementEntries.Count; i++)
            {
                string key = state.SupplementEntries[i].Key;
                if (!state.KeptSupplementKeys.Contains(key)
                    && !state.RemovedSupplementKeys.Contains(key)
                    && ResolveSupplementObjectByKey(state, key) != null)
                {
                    return i;
                }
            }

            return -1;
        }

        private static FbxReplacementStructureTargetEntry GetCurrentTargetEntry(FbxReplacementStageTwoState state)
        {
            if (state == null || state.CurrentTargetIndex < 0 || state.CurrentTargetIndex >= state.TargetEntries.Count)
            {
                return null;
            }

            return state.TargetEntries[state.CurrentTargetIndex];
        }

        private static FbxReplacementStructureSupplementEntry GetCurrentSupplementEntry(FbxReplacementStageTwoState state)
        {
            if (state == null || state.CurrentSupplementIndex < 0 || state.CurrentSupplementIndex >= state.SupplementEntries.Count)
            {
                return null;
            }

            return state.SupplementEntries[state.CurrentSupplementIndex];
        }

        private static int FindTargetEntryIndex(FbxReplacementStageTwoState state, string targetKey)
        {
            if (state == null || targetKey == null)
            {
                return -1;
            }

            for (int i = 0; i < state.TargetEntries.Count; i++)
            {
                if (string.Equals(state.TargetEntries[i].Key, targetKey, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

    }
}



