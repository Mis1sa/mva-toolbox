using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.FbxReplacement
{
    internal static partial class FbxReplacementComponentWorkflow
    {
        internal static readonly Color CurrentTargetHighlightColor = new Color(0.25f, 0.8f, 0.3f, 0.32f);
        internal static readonly Color CurrentReferenceHighlightColor = new Color(1f, 0.35f, 0.35f, 0.36f);
        internal static readonly Color ProcessedHighlightColor = new Color(0.3f, 0.55f, 1f, 0.34f);

        internal static FbxReplacementStageFourState CreateState(FbxReplacementStageThreeState stageThreeState)
        {
            if (stageThreeState == null)
            {
                throw new ArgumentNullException(nameof(stageThreeState));
            }

            if (FbxReplacementMaterialWorkflow.GetCurrentStep(stageThreeState) != FbxReplacementStageThreeWorkflowStep.Completed)
            {
                throw new InvalidOperationException("阶段3尚未完成，无法进入阶段4。");
            }

            FbxReplacementStageTwoState stageTwoState = stageThreeState.StageTwoState;
            if (stageTwoState?.SessionState?.Workspace == null
                || stageTwoState.SessionState.Workspace.TargetWorkspaceRoot == null)
            {
                throw new InvalidOperationException("当前工作区无效，无法进入阶段4。");
            }

            GameObject baselineTargetTemplate = CreateHiddenTemplate(
                stageTwoState.SessionState.Workspace.TargetWorkspaceRoot,
                stageTwoState.SessionState.Workspace.Scene);
            BuildReviewEntries(
                stageTwoState,
                baselineTargetTemplate,
                out List<FbxReplacementComponentObjectReviewEntry> objectEntries,
                out Dictionary<string, FbxReplacementComponentReviewEntry> entriesByKey,
                out Dictionary<string, FbxReplacementComponentObjectReviewEntry> objectEntriesByReferenceKey,
                out Dictionary<int, string> referenceKeyByInstanceId,
                out Dictionary<Component, string> entryKeyByReferenceComponent);
            List<FbxReplacementTransformSnapshot> transformSnapshots = CaptureTransformSnapshots(stageTwoState.SessionState.Workspace.TargetWorkspaceRoot);

            var state = new FbxReplacementStageFourState(
                stageThreeState,
                baselineTargetTemplate,
                objectEntries,
                entriesByKey,
                objectEntriesByReferenceKey,
                referenceKeyByInstanceId,
                entryKeyByReferenceComponent,
                transformSnapshots);

            if (entriesByKey.Count == 0)
            {
                CompleteStage(state);
                return state;
            }

            PrepareNextEntry(state);
            return state;
        }

        internal static void DisposeState(FbxReplacementStageFourState state)
        {
            if (state == null)
            {
                return;
            }

            if (state.BaselineTargetTemplate != null)
            {
                Object.DestroyImmediate(state.BaselineTargetTemplate);
                state.BaselineTargetTemplate = null;
            }

            state.ObjectEntries.Clear();
            state.EntriesByKey.Clear();
            state.ObjectEntriesByReferenceKey.Clear();
            state.ReferenceKeyByInstanceId.Clear();
            state.EntryKeyByReferenceComponent.Clear();
            state.TransformSnapshots.Clear();
            state.History.Clear();
            state.ProcessedEntryKeys.Clear();
            state.ConfirmedEntryKeys.Clear();
            state.KeptEntryKeys.Clear();
            state.RemovedEntryKeys.Clear();
            state.CurrentSelections = Array.Empty<FbxReplacementComponentSelectionHandle>();
            state.CurrentEntryKey = string.Empty;
        }

        internal static void RevertAllToBaseline(FbxReplacementStageFourState state)
        {
            if (state?.BaselineTargetTemplate == null
                || state.StageThreeState?.StageTwoState?.SessionState?.Workspace?.TargetWorkspaceRoot == null)
            {
                return;
            }

            var restorePairs = new List<KeyValuePair<GameObject, GameObject>>();
            for (int i = 0; i < state.ObjectEntries.Count; i++)
            {
                FbxReplacementComponentObjectReviewEntry objectEntry = state.ObjectEntries[i];
                GameObject targetObject = ResolveTargetObject(state, objectEntry.TargetKey, objectEntry.IsSupplement);
                Transform templateTransform = ResolveTransformByKey(state.BaselineTargetTemplate.transform, objectEntry.TargetHierarchyKey);
                if (targetObject != null && templateTransform != null)
                {
                    restorePairs.Add(new KeyValuePair<GameObject, GameObject>(targetObject, templateTransform.gameObject));
                }
            }

            for (int i = 0; i < restorePairs.Count; i++)
            {
                StripMigratableComponents(restorePairs[i].Key);
            }

            RestoreTransforms(state);

            for (int i = 0; i < restorePairs.Count; i++)
            {
                EnsureMigratableComponentShells(restorePairs[i].Value, restorePairs[i].Key);
            }

            for (int i = 0; i < restorePairs.Count; i++)
            {
                CopyMigratableComponents(state, restorePairs[i].Value, restorePairs[i].Key);
            }
        }

        internal static bool StepBack(FbxReplacementStageFourState state)
        {
            if (state == null || state.History.Count == 0)
            {
                return false;
            }

            state.History.RemoveAt(state.History.Count - 1);
            PrepareNextEntry(state);
            return true;
        }

        internal static FbxReplacementStageFourWorkflowStep GetCurrentStep(FbxReplacementStageFourState state)
        {
            return state?.CurrentStep ?? FbxReplacementStageFourWorkflowStep.ComponentReview;
        }

        internal static GameObject GetCurrentTargetObject(FbxReplacementStageFourState state)
        {
            return ResolveTargetObject(state, GetCurrentEntry(state));
        }

        internal static GameObject GetCurrentReferenceObject(FbxReplacementStageFourState state)
        {
            return ResolveReferenceObject(state, GetCurrentEntry(state));
        }

        internal static void RevealCurrentObjects(FbxReplacementStageFourState state)
        {
            if (state == null)
            {
                return;
            }

            FbxReplacementHierarchyHighlighter.RevealHierarchyObjects(
                GetCurrentTargetObject(state),
                GetCurrentReferenceObject(state));
        }

        internal static string GetCurrentComponentName(FbxReplacementStageFourState state)
        {
            return GetCurrentEntry(state)?.DisplayName ?? string.Empty;
        }

        internal static int GetCurrentReferenceSlotCount(FbxReplacementStageFourState state)
        {
            return GetCurrentEntry(state)?.ReferenceSlots.Count ?? 0;
        }

        internal static string GetCurrentReferenceSlotLabel(FbxReplacementStageFourState state, int slotIndex)
        {
            FbxReplacementComponentReviewEntry entry = GetCurrentEntry(state);
            return entry != null && slotIndex >= 0 && slotIndex < entry.ReferenceSlots.Count
                ? entry.ReferenceSlots[slotIndex].DisplayName
                : string.Empty;
        }

        internal static Type GetCurrentReferenceSlotType(FbxReplacementStageFourState state, int slotIndex)
        {
            FbxReplacementComponentReviewEntry entry = GetCurrentEntry(state);
            return entry != null && slotIndex >= 0 && slotIndex < entry.ReferenceSlots.Count
                ? entry.ReferenceSlots[slotIndex].ReferenceType
                : typeof(Object);
        }

        internal static Object GetCurrentReferenceSourceObject(FbxReplacementStageFourState state, int slotIndex)
        {
            FbxReplacementComponentReviewEntry entry = GetCurrentEntry(state);
            return entry != null && slotIndex >= 0 && slotIndex < entry.ReferenceSlots.Count
                ? entry.ReferenceSlots[slotIndex].SourceReference
                : null;
        }

        internal static Object GetCurrentReferenceSelection(FbxReplacementStageFourState state, int slotIndex)
        {
            FbxReplacementComponentReviewEntry entry = GetCurrentEntry(state);
            if (state == null || entry == null || slotIndex < 0 || slotIndex >= entry.ReferenceSlots.Count)
            {
                return null;
            }

            FbxReplacementComponentSelectionHandle selection = state.CurrentSelections != null && slotIndex < state.CurrentSelections.Length
                ? state.CurrentSelections[slotIndex]
                : entry.ReferenceSlots[slotIndex].RecommendedSelection;
            return ResolveSelectionHandle(state, selection, entry.ReferenceSlots[slotIndex].ReferenceType);
        }

        internal static void SetCurrentReferenceSelection(FbxReplacementStageFourState state, int slotIndex, Object selection)
        {
            FbxReplacementComponentReviewEntry entry = GetCurrentEntry(state);
            if (state == null || entry == null || slotIndex < 0 || slotIndex >= entry.ReferenceSlots.Count)
            {
                return;
            }

            if (state.CurrentSelections == null || state.CurrentSelections.Length != entry.ReferenceSlots.Count)
            {
                state.CurrentSelections = entry.ReferenceSlots
                    .Select(slot => CloneSelection(slot.RecommendedSelection))
                    .ToArray();
            }

            state.CurrentSelections[slotIndex] = CreateSelectionHandle(state, entry.ReferenceSlots[slotIndex], selection);
            PreviewCurrentEntry(state);
        }

        internal static void ConfirmCurrentComponent(FbxReplacementStageFourState state)
        {
            ApplyAndAdvance(state, FbxReplacementComponentDecisionActionType.ConfirmWithRemap, state?.CurrentSelections);
        }

        internal static void KeepCurrentComponent(FbxReplacementStageFourState state)
        {
            ApplyAndAdvance(state, FbxReplacementComponentDecisionActionType.KeepWithoutRemap, state?.CurrentSelections);
        }

        internal static void RemoveCurrentComponent(FbxReplacementStageFourState state)
        {
            ApplyAndAdvance(state, FbxReplacementComponentDecisionActionType.Remove, Array.Empty<FbxReplacementComponentSelectionHandle>());
        }

        internal static int GetTotalComponentCount(FbxReplacementStageFourState state)
        {
            return state?.EntriesByKey.Count ?? 0;
        }

        internal static int GetProcessedComponentCount(FbxReplacementStageFourState state)
        {
            return state?.ProcessedEntryKeys.Count ?? 0;
        }

        internal static Dictionary<int, Color> BuildHighlightMap(FbxReplacementStageFourState state)
        {
            var result = new Dictionary<int, Color>();
            if (state == null)
            {
                return result;
            }

            for (int i = 0; i < state.ObjectEntries.Count; i++)
            {
                FbxReplacementComponentObjectReviewEntry objectEntry = state.ObjectEntries[i];
                if (!IsObjectProcessed(state, objectEntry))
                {
                    continue;
                }

                GameObject targetObject = ResolveTargetObject(state, objectEntry.TargetKey, objectEntry.IsSupplement);
                GameObject referenceObject = ResolveReferenceObject(state, objectEntry.ReferenceKey);
                if (targetObject != null)
                {
                    result[targetObject.GetInstanceID()] = ProcessedHighlightColor;
                }

                if (referenceObject != null)
                {
                    result[referenceObject.GetInstanceID()] = ProcessedHighlightColor;
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
                result[currentReference.GetInstanceID()] = CurrentReferenceHighlightColor;
            }

            return result;
        }

        private static void ApplyAndAdvance(
            FbxReplacementStageFourState state,
            FbxReplacementComponentDecisionActionType actionType,
            IReadOnlyList<FbxReplacementComponentSelectionHandle> selections)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            FbxReplacementComponentReviewEntry entry = GetCurrentEntry(state);
            if (entry == null)
            {
                throw new InvalidOperationException("当前没有待处理的组件。");
            }

            var record = new FbxReplacementComponentDecisionRecord(actionType, entry.Key, selections);
            state.History.Add(record);
            PrepareNextEntry(state);
        }

        private static void ApplyDecision(
            FbxReplacementStageFourState state,
            FbxReplacementComponentDecisionRecord decision)
        {
            if (state == null || decision == null)
            {
                return;
            }

            if (!state.EntriesByKey.TryGetValue(decision.EntryKey, out FbxReplacementComponentReviewEntry entry))
            {
                return;
            }

            switch (decision.ActionType)
            {
                case FbxReplacementComponentDecisionActionType.ConfirmWithRemap:
                    CopyComponentToTarget(state, entry, true, decision.Selections);
                    state.ConfirmedEntryKeys.Add(entry.Key);
                    break;

                case FbxReplacementComponentDecisionActionType.KeepWithoutRemap:
                    CopyComponentToTarget(state, entry, false, decision.Selections);
                    state.KeptEntryKeys.Add(entry.Key);
                    break;

                case FbxReplacementComponentDecisionActionType.Remove:
                    state.RemovedEntryKeys.Add(entry.Key);
                    break;
            }

            state.ProcessedEntryKeys.Add(entry.Key);
        }

        private static void PrepareNextEntry(FbxReplacementStageFourState state)
        {
            if (state == null)
            {
                return;
            }

            RebuildState(state);

            FbxReplacementComponentReviewEntry nextEntry = FindNextEntry(state);
            if (nextEntry == null)
            {
                CompleteStage(state);
                return;
            }

            state.CurrentEntryKey = nextEntry.Key;
            state.CurrentSelections = nextEntry.ReferenceSlots
                .Select(slot => CloneSelection(slot.RecommendedSelection))
                .ToArray();
            state.CurrentStep = FbxReplacementStageFourWorkflowStep.ComponentReview;
            PreviewCurrentEntry(state);
            RevealCurrentObjects(state);
        }

        private static FbxReplacementComponentReviewEntry FindNextEntry(FbxReplacementStageFourState state)
        {
            if (state == null)
            {
                return null;
            }

            for (int i = 0; i < state.ObjectEntries.Count; i++)
            {
                FbxReplacementComponentObjectReviewEntry objectEntry = state.ObjectEntries[i];
                List<FbxReplacementComponentReviewEntry> readyEntries = objectEntry.ComponentEntries
                    .Where(entry => !state.ProcessedEntryKeys.Contains(entry.Key) && CanProcessEntry(state, entry))
                    .OrderBy(entry => entry.PriorityScore)
                    .ThenBy(entry => entry.ComponentIndex)
                    .ToList();
                if (readyEntries.Count > 0)
                {
                    return readyEntries[0];
                }
            }

            for (int i = 0; i < state.ObjectEntries.Count; i++)
            {
                FbxReplacementComponentObjectReviewEntry objectEntry = state.ObjectEntries[i];
                FbxReplacementComponentReviewEntry fallback = objectEntry.ComponentEntries
                    .Where(entry => !state.ProcessedEntryKeys.Contains(entry.Key))
                    .OrderBy(entry => entry.PriorityScore)
                    .ThenBy(entry => entry.ComponentIndex)
                    .FirstOrDefault();
                if (fallback != null)
                {
                    return fallback;
                }
            }

            return null;
        }

        private static bool CanProcessEntry(FbxReplacementStageFourState state, FbxReplacementComponentReviewEntry entry)
        {
            if (state == null || entry == null)
            {
                return false;
            }

            for (int i = 0; i < entry.DependencyKeys.Count; i++)
            {
                if (!state.ProcessedEntryKeys.Contains(entry.DependencyKeys[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static void CompleteStage(FbxReplacementStageFourState state)
        {
            if (state == null)
            {
                return;
            }

            RebuildState(state);

            state.CurrentEntryKey = string.Empty;
            state.CurrentSelections = Array.Empty<FbxReplacementComponentSelectionHandle>();
            state.CurrentStep = FbxReplacementStageFourWorkflowStep.Completed;
        }

        private static void RebuildState(FbxReplacementStageFourState state)
        {
            if (state == null)
            {
                return;
            }

            List<FbxReplacementComponentDecisionRecord> history = state.History.ToList();
            state.ProcessedEntryKeys.Clear();
            state.ConfirmedEntryKeys.Clear();
            state.KeptEntryKeys.Clear();
            state.RemovedEntryKeys.Clear();
            RevertAllToBaseline(state);
            for (int i = 0; i < history.Count; i++)
            {
                ApplyDecision(state, history[i]);
            }
        }

        private static void PreviewCurrentEntry(FbxReplacementStageFourState state)
        {
            if (state == null || state.CurrentStep != FbxReplacementStageFourWorkflowStep.ComponentReview)
            {
                return;
            }

            FbxReplacementComponentReviewEntry entry = GetCurrentEntry(state);
            if (entry == null)
            {
                return;
            }

            RebuildState(state);
            CopyComponentToTarget(state, entry, true, state.CurrentSelections ?? Array.Empty<FbxReplacementComponentSelectionHandle>());
        }

        private static FbxReplacementComponentReviewEntry GetCurrentEntry(FbxReplacementStageFourState state)
        {
            if (state == null || string.IsNullOrEmpty(state.CurrentEntryKey))
            {
                return null;
            }

            return state.EntriesByKey.TryGetValue(state.CurrentEntryKey, out FbxReplacementComponentReviewEntry entry)
                ? entry
                : null;
        }

        private static bool IsObjectProcessed(FbxReplacementStageFourState state, FbxReplacementComponentObjectReviewEntry objectEntry)
        {
            if (state == null || objectEntry == null)
            {
                return false;
            }

            for (int i = 0; i < objectEntry.ComponentEntries.Count; i++)
            {
                if (!state.ProcessedEntryKeys.Contains(objectEntry.ComponentEntries[i].Key))
                {
                    return false;
                }
            }

            return objectEntry.ComponentEntries.Count > 0;
        }

        private static FbxReplacementComponentSelectionHandle CreateSelectionHandle(
            FbxReplacementStageFourState state,
            FbxReplacementComponentReferenceSlot slot,
            Object selection)
        {
            if (selection == null)
            {
                return FbxReplacementComponentSelectionHandle.None;
            }

            if (slot == null)
            {
                throw new InvalidOperationException("引用槽位无效。");
            }

            GameObject targetRoot = state?.StageThreeState?.StageTwoState?.SessionState?.Workspace?.TargetWorkspaceRoot;
            if (targetRoot == null)
            {
                throw new InvalidOperationException("当前目标工作区无效。");
            }

            GameObject targetGameObject = ResolveReferencedGameObject(selection);
            if (targetGameObject == null || (targetGameObject != targetRoot && !targetGameObject.transform.IsChildOf(targetRoot.transform)))
            {
                throw new InvalidOperationException("引用槽位只能选择当前目标工作区中的对象或组件。");
            }

            Type selectionType = selection.GetType();
            if (slot.ReferenceType != null && !slot.ReferenceType.IsAssignableFrom(selectionType))
            {
                throw new InvalidOperationException($"当前槽位不接受该类型：{selectionType.Name}");
            }

            string targetHierarchyKey = GetHierarchyIndexPath(targetRoot.transform, targetGameObject.transform);
            switch (selection)
            {
                case GameObject _:
                    return new FbxReplacementComponentSelectionHandle(
                        FbxReplacementComponentSelectionKind.GameObject,
                        targetHierarchyKey,
                        string.Empty,
                        -1);

                case Transform _:
                    return new FbxReplacementComponentSelectionHandle(
                        FbxReplacementComponentSelectionKind.Transform,
                        targetHierarchyKey,
                        string.Empty,
                        -1);

                case Component component:
                    return new FbxReplacementComponentSelectionHandle(
                        FbxReplacementComponentSelectionKind.Component,
                        targetHierarchyKey,
                        component.GetType().FullName ?? component.GetType().Name,
                        GetComponentSlotIndex(component));

                default:
                    throw new InvalidOperationException("当前槽位仅支持对象、Transform 或组件引用。");
            }
        }

        private static Object ResolveSelectionHandle(
            FbxReplacementStageFourState state,
            FbxReplacementComponentSelectionHandle selection,
            Type expectedType)
        {
            if (state == null || selection == null || selection.Kind == FbxReplacementComponentSelectionKind.None)
            {
                return null;
            }

            Transform targetTransform = ResolveTransformByKey(
                state.StageThreeState.StageTwoState.SessionState.Workspace.TargetWorkspaceRoot.transform,
                selection.TargetHierarchyKey);
            if (targetTransform == null)
            {
                return null;
            }

            Object resolvedObject;
            switch (selection.Kind)
            {
                case FbxReplacementComponentSelectionKind.GameObject:
                    resolvedObject = targetTransform.gameObject;
                    break;

                case FbxReplacementComponentSelectionKind.Transform:
                    resolvedObject = targetTransform;
                    break;

                case FbxReplacementComponentSelectionKind.Component:
                    Component[] components = targetTransform.GetComponents<Component>();
                    int matchedIndex = 0;
                    for (int i = 0; i < components.Length; i++)
                    {
                        Component component = components[i];
                        if (component == null || component is Transform)
                        {
                            continue;
                        }

                        string fullTypeName = component.GetType().FullName ?? component.GetType().Name;
                        if (!string.Equals(fullTypeName, selection.ComponentTypeName, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (matchedIndex == selection.ComponentSlotIndex)
                        {
                            resolvedObject = component;
                            return IsExpectedReferenceType(resolvedObject, expectedType) ? resolvedObject : null;
                        }

                        matchedIndex++;
                    }

                    return null;

                default:
                    return null;
            }

            return IsExpectedReferenceType(resolvedObject, expectedType) ? resolvedObject : null;
        }

        private static bool IsExpectedReferenceType(Object referencedObject, Type expectedType)
        {
            return referencedObject == null
                || expectedType == null
                || expectedType == typeof(Object)
                || expectedType.IsInstanceOfType(referencedObject);
        }

        private static GameObject ResolveTargetObject(
            FbxReplacementStageFourState state,
            FbxReplacementComponentReviewEntry entry)
        {
            return entry != null
                ? ResolveTargetObject(state, entry.TargetKey, entry.IsSupplement)
                : null;
        }

        private static GameObject ResolveTargetObject(
            FbxReplacementStageFourState state,
            string targetKey,
            bool isSupplement)
        {
            if (state?.StageThreeState?.StageTwoState == null || string.IsNullOrEmpty(targetKey))
            {
                return null;
            }

            Dictionary<string, GameObject> map = isSupplement
                ? state.StageThreeState.StageTwoState.CurrentSupplementObjectsByKey
                : state.StageThreeState.StageTwoState.CurrentTargetObjectsByKey;
            return map != null && map.TryGetValue(targetKey, out GameObject targetObject)
                ? targetObject
                : null;
        }

        private static GameObject ResolveReferenceObject(
            FbxReplacementStageFourState state,
            FbxReplacementComponentReviewEntry entry)
        {
            return entry != null
                ? ResolveReferenceObject(state, entry.ReferenceKey)
                : null;
        }

        private static GameObject ResolveReferenceObject(FbxReplacementStageFourState state, string referenceKey)
        {
            if (state?.StageThreeState?.StageTwoState?.CurrentReferenceObjectsByKey == null || string.IsNullOrEmpty(referenceKey))
            {
                return null;
            }

            return state.StageThreeState.StageTwoState.CurrentReferenceObjectsByKey.TryGetValue(referenceKey, out GameObject referenceObject)
                ? referenceObject
                : null;
        }

        private static bool IsSkippedComponent(Component component)
        {
            return component != null
                && (component is Transform
                    || component is MeshFilter
                    || component is MeshRenderer
                    || component is SkinnedMeshRenderer);
        }

        private static List<Type> GetRequiredComponentTypes(Type componentType)
        {
            var result = new List<Type>();
            if (componentType == null)
            {
                return result;
            }

            object[] attributes = componentType.GetCustomAttributes(typeof(RequireComponent), true);
            FieldInfo type0Field = typeof(RequireComponent).GetField("m_Type0", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo type1Field = typeof(RequireComponent).GetField("m_Type1", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo type2Field = typeof(RequireComponent).GetField("m_Type2", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < attributes.Length; i++)
            {
                if (!(attributes[i] is RequireComponent requireComponent))
                {
                    continue;
                }

                AddRequiredType(result, type0Field?.GetValue(requireComponent) as Type);
                AddRequiredType(result, type1Field?.GetValue(requireComponent) as Type);
                AddRequiredType(result, type2Field?.GetValue(requireComponent) as Type);
            }

            return result;
        }

        private static void AddRequiredType(List<Type> types, Type type)
        {
            if (type != null && !types.Contains(type))
            {
                types.Add(type);
            }
        }

        private static int GetComponentSlotIndex(Component component)
        {
            if (component == null)
            {
                return -1;
            }

            Component[] hostComponents = component.gameObject.GetComponents<Component>();
            int slotIndex = 0;
            string fullTypeName = component.GetType().FullName ?? component.GetType().Name;
            for (int i = 0; i < hostComponents.Length; i++)
            {
                Component hostComponent = hostComponents[i];
                if (hostComponent == null || hostComponent is Transform)
                {
                    continue;
                }

                if (!string.Equals(hostComponent.GetType().FullName ?? hostComponent.GetType().Name, fullTypeName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (hostComponent == component)
                {
                    return slotIndex;
                }

                slotIndex++;
            }

            return -1;
        }

        private static FbxReplacementComponentSelectionHandle[] CloneSelections(IReadOnlyList<FbxReplacementComponentSelectionHandle> selections)
        {
            if (selections == null)
            {
                return Array.Empty<FbxReplacementComponentSelectionHandle>();
            }

            var result = new FbxReplacementComponentSelectionHandle[selections.Count];
            for (int i = 0; i < selections.Count; i++)
            {
                result[i] = CloneSelection(selections[i]);
            }

            return result;
        }

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

        private static GameObject ResolveReferencedGameObject(Object referencedObject)
        {
            switch (referencedObject)
            {
                case GameObject gameObject:
                    return gameObject;
                case Component component:
                    return component.gameObject;
                default:
                    return null;
            }
        }

        private static string BuildEntryKey(string referenceKey, string typeName, int typeSlotIndex)
        {
            return $"{referenceKey}|{typeName}|{typeSlotIndex}";
        }
    }
}


