using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.FbxReplacement
{
    internal static partial class FbxReplacementComponentWorkflow
    {
        private static void BuildReviewEntries(
            FbxReplacementStageTwoState stageTwoState,
            GameObject baselineTargetTemplate,
            out List<FbxReplacementComponentObjectReviewEntry> objectEntries,
            out Dictionary<string, FbxReplacementComponentReviewEntry> entriesByKey,
            out Dictionary<string, FbxReplacementComponentObjectReviewEntry> objectEntriesByReferenceKey,
            out Dictionary<int, string> referenceKeyByInstanceId,
            out Dictionary<Component, string> entryKeyByReferenceComponent)
        {
            objectEntries = new List<FbxReplacementComponentObjectReviewEntry>();
            entriesByKey = new Dictionary<string, FbxReplacementComponentReviewEntry>(StringComparer.Ordinal);
            objectEntriesByReferenceKey = new Dictionary<string, FbxReplacementComponentObjectReviewEntry>(StringComparer.Ordinal);
            referenceKeyByInstanceId = new Dictionary<int, string>();
            entryKeyByReferenceComponent = new Dictionary<Component, string>();
            if (stageTwoState == null)
            {
                return;
            }

            foreach (KeyValuePair<string, GameObject> pair in stageTwoState.CurrentReferenceObjectsByKey)
            {
                if (pair.Value != null)
                {
                    referenceKeyByInstanceId[pair.Value.GetInstanceID()] = pair.Key;
                }
            }

            Dictionary<string, FbxReplacementStructureSupplementEntry> keptSupplementEntries = stageTwoState.SupplementEntries
                .Where(entry => entry != null && stageTwoState.KeptSupplementKeys.Contains(entry.Key))
                .GroupBy(entry => entry.ReferenceKey)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            GameObject targetRoot = stageTwoState.SessionState?.Workspace?.TargetWorkspaceRoot;
            if (targetRoot == null)
            {
                return;
            }

            for (int i = 0; i < stageTwoState.ReferenceEntries.Count; i++)
            {
                FbxReplacementStructureReferenceEntry referenceEntry = stageTwoState.ReferenceEntries[i];
                if (referenceEntry == null)
                {
                    continue;
                }

                if (!stageTwoState.CurrentReferenceObjectsByKey.TryGetValue(referenceEntry.Key, out GameObject referenceObject) || referenceObject == null)
                {
                    continue;
                }

                string targetKey;
                bool isSupplement;
                GameObject targetObject;
                if (!TryResolveTargetForReferenceKey(stageTwoState, keptSupplementEntries, referenceEntry.Key, out targetKey, out isSupplement, out targetObject)
                    || targetObject == null)
                {
                    continue;
                }

                string targetHierarchyKey = GetHierarchyIndexPath(targetRoot.transform, targetObject.transform);
                List<FbxReplacementComponentReviewEntry> componentEntries = BuildComponentEntries(
                    stageTwoState,
                    referenceEntry.Key,
                    targetKey,
                    targetHierarchyKey,
                    isSupplement,
                    referenceObject,
                    entriesByKey,
                    entryKeyByReferenceComponent,
                    referenceKeyByInstanceId);
                if (componentEntries.Count == 0)
                {
                    continue;
                }

                var objectEntry = new FbxReplacementComponentObjectReviewEntry(
                    referenceEntry.Key,
                    targetKey,
                    targetHierarchyKey,
                    isSupplement,
                    componentEntries);
                objectEntries.Add(objectEntry);
                objectEntriesByReferenceKey[referenceEntry.Key] = objectEntry;
            }

            ComputePriorityScores(objectEntries);
        }

        private static List<FbxReplacementComponentReviewEntry> BuildComponentEntries(
            FbxReplacementStageTwoState stageTwoState,
            string referenceKey,
            string targetKey,
            string targetHierarchyKey,
            bool isSupplement,
            GameObject referenceObject,
            Dictionary<string, FbxReplacementComponentReviewEntry> entriesByKey,
            Dictionary<Component, string> entryKeyByReferenceComponent,
            Dictionary<int, string> referenceKeyByInstanceId)
        {
            var result = new List<FbxReplacementComponentReviewEntry>();
            if (referenceObject == null)
            {
                return result;
            }

            Component[] components = referenceObject.GetComponents<Component>();
            var sameTypeOrdinal = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || IsSkippedComponent(component))
                {
                    continue;
                }

                string typeName = component.GetType().FullName ?? component.GetType().Name;
                int typeSlotIndex = sameTypeOrdinal.TryGetValue(typeName, out int ordinal) ? ordinal : 0;
                sameTypeOrdinal[typeName] = typeSlotIndex + 1;
                string entryKey = BuildEntryKey(referenceKey, typeName, typeSlotIndex);
                var entry = new FbxReplacementComponentReviewEntry(
                    entryKey,
                    referenceKey,
                    targetKey,
                    targetHierarchyKey,
                    isSupplement,
                    component,
                    component.GetType(),
                    component.GetType().Name,
                    i,
                    typeSlotIndex);
                result.Add(entry);
                entriesByKey[entryKey] = entry;
                entryKeyByReferenceComponent[component] = entryKey;
            }

            for (int i = 0; i < result.Count; i++)
            {
                CollectDependenciesAndSlots(stageTwoState, result[i], entriesByKey, entryKeyByReferenceComponent, referenceKeyByInstanceId);
            }

            return result;
        }

        private static void CollectDependenciesAndSlots(
            FbxReplacementStageTwoState stageTwoState,
            FbxReplacementComponentReviewEntry entry,
            Dictionary<string, FbxReplacementComponentReviewEntry> entriesByKey,
            Dictionary<Component, string> entryKeyByReferenceComponent,
            Dictionary<int, string> referenceKeyByInstanceId)
        {
            if (stageTwoState == null || entry?.SourceComponent == null)
            {
                return;
            }

            HashSet<string> dependencyKeys = new HashSet<string>(StringComparer.Ordinal);
            List<Type> requiredTypes = GetRequiredComponentTypes(entry.ComponentType);
            Component[] hostComponents = entry.SourceComponent.gameObject.GetComponents<Component>();
            for (int i = 0; i < requiredTypes.Count; i++)
            {
                Type requiredType = requiredTypes[i];
                if (requiredType == null)
                {
                    continue;
                }

                Component dependencyComponent = hostComponents.FirstOrDefault(component => component != null && requiredType.IsAssignableFrom(component.GetType()) && entryKeyByReferenceComponent.ContainsKey(component));
                if (dependencyComponent != null && entryKeyByReferenceComponent.TryGetValue(dependencyComponent, out string dependencyKey) && dependencyKey != entry.Key)
                {
                    dependencyKeys.Add(dependencyKey);
                }
            }

            var serializedObject = new SerializedObject(entry.SourceComponent);
            var iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.Next(enterChildren))
            {
                enterChildren = true;
                if (iterator.propertyType != SerializedPropertyType.ObjectReference
                    || ShouldSkipObjectReferenceProperty(iterator))
                {
                    continue;
                }

                Object referencedObject = iterator.objectReferenceValue;
                if (referencedObject == null)
                {
                    continue;
                }

                GameObject referencedGameObject = ResolveReferencedGameObject(referencedObject);
                if (referencedGameObject == null
                    || !referenceKeyByInstanceId.TryGetValue(referencedGameObject.GetInstanceID(), out string referencedReferenceKey))
                {
                    continue;
                }

                if (referencedObject is Component referencedComponent
                    && entryKeyByReferenceComponent.TryGetValue(referencedComponent, out string dependencyKey)
                    && dependencyKey != entry.Key)
                {
                    dependencyKeys.Add(dependencyKey);
                }

                FbxReplacementComponentSelectionHandle recommendedSelection = BuildAutoSelectionForReferenceObject(
                    stageTwoState,
                    entriesByKey,
                    entryKeyByReferenceComponent,
                    referenceKeyByInstanceId,
                    referencedReferenceKey,
                    referencedObject);
                entry.ReferenceSlots.Add(new FbxReplacementComponentReferenceSlot(
                    iterator.propertyPath,
                    iterator.displayName,
                    referencedObject.GetType(),
                    referencedObject,
                    recommendedSelection));
            }

            entry.DependencyKeys.AddRange(dependencyKeys.OrderBy(value => value, StringComparer.Ordinal));
        }

        private static bool ShouldSkipObjectReferenceProperty(SerializedProperty property)
        {
            return property != null
                && string.Equals(property.propertyPath, "m_GameObject", StringComparison.Ordinal);
        }

        private static void ComputePriorityScores(List<FbxReplacementComponentObjectReviewEntry> objectEntries)
        {
            var dependentCountByEntryKey = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < objectEntries.Count; i++)
            {
                for (int j = 0; j < objectEntries[i].ComponentEntries.Count; j++)
                {
                    FbxReplacementComponentReviewEntry entry = objectEntries[i].ComponentEntries[j];
                    if (!dependentCountByEntryKey.ContainsKey(entry.Key))
                    {
                        dependentCountByEntryKey[entry.Key] = 0;
                    }

                    for (int k = 0; k < entry.DependencyKeys.Count; k++)
                    {
                        string dependencyKey = entry.DependencyKeys[k];
                        dependentCountByEntryKey[dependencyKey] = dependentCountByEntryKey.TryGetValue(dependencyKey, out int current)
                            ? current + 1
                            : 1;
                    }
                }
            }

            for (int i = 0; i < objectEntries.Count; i++)
            {
                FbxReplacementComponentObjectReviewEntry objectEntry = objectEntries[i];
                objectEntry.ComponentEntries.Sort((left, right) =>
                {
                    int leftScore = ComputePriorityScore(left, dependentCountByEntryKey);
                    int rightScore = ComputePriorityScore(right, dependentCountByEntryKey);
                    int scoreCompare = leftScore.CompareTo(rightScore);
                    if (scoreCompare != 0)
                    {
                        return scoreCompare;
                    }

                    return left.ComponentIndex.CompareTo(right.ComponentIndex);
                });

                for (int j = 0; j < objectEntry.ComponentEntries.Count; j++)
                {
                    objectEntry.ComponentEntries[j].PriorityScore = ComputePriorityScore(objectEntry.ComponentEntries[j], dependentCountByEntryKey);
                }
            }
        }

        private static int ComputePriorityScore(
            FbxReplacementComponentReviewEntry entry,
            Dictionary<string, int> dependentCountByEntryKey)
        {
            if (entry == null || entry.ComponentType == null)
            {
                return int.MaxValue;
            }

            int dependentCount = dependentCountByEntryKey.TryGetValue(entry.Key, out int value) ? value : 0;
            bool isColliderLike = typeof(Collider).IsAssignableFrom(entry.ComponentType)
                || string.Equals(entry.ComponentType.Name, "VRCPhysBoneCollider", StringComparison.Ordinal)
                || entry.ComponentType.Name.EndsWith("Collider", StringComparison.Ordinal);
            if (isColliderLike)
            {
                return 0;
            }

            if (typeof(Rigidbody).IsAssignableFrom(entry.ComponentType))
            {
                return 10;
            }

            if (dependentCount > 0)
            {
                return 20;
            }

            if (entry.DependencyKeys.Count == 0)
            {
                return 30;
            }

            return 40;
        }

        private static bool TryResolveTargetForReferenceKey(
            FbxReplacementStageTwoState stageTwoState,
            Dictionary<string, FbxReplacementStructureSupplementEntry> keptSupplementEntries,
            string referenceKey,
            out string targetKey,
            out bool isSupplement,
            out GameObject targetObject)
        {
            targetKey = string.Empty;
            isSupplement = false;
            targetObject = null;
            if (stageTwoState == null || string.IsNullOrEmpty(referenceKey))
            {
                return false;
            }

            if (stageTwoState.MatchedTargetKeyByReferenceKey.TryGetValue(referenceKey, out targetKey)
                && stageTwoState.CurrentTargetObjectsByKey.TryGetValue(targetKey, out targetObject)
                && targetObject != null)
            {
                return true;
            }

            if (keptSupplementEntries != null
                && keptSupplementEntries.TryGetValue(referenceKey, out FbxReplacementStructureSupplementEntry supplementEntry)
                && supplementEntry != null
                && stageTwoState.CurrentSupplementObjectsByKey.TryGetValue(supplementEntry.Key, out targetObject)
                && targetObject != null)
            {
                targetKey = supplementEntry.Key;
                isSupplement = true;
                return true;
            }

            targetKey = string.Empty;
            isSupplement = false;
            targetObject = null;
            return false;
        }

        private static FbxReplacementComponentSelectionHandle BuildAutoSelectionForReferenceObject(
            FbxReplacementStageTwoState stageTwoState,
            Dictionary<string, FbxReplacementComponentReviewEntry> entriesByKey,
            Dictionary<Component, string> entryKeyByReferenceComponent,
            Dictionary<int, string> referenceKeyByInstanceId,
            string referencedReferenceKey,
            Object referencedObject)
        {
            if (stageTwoState == null || referencedObject == null)
            {
                return FbxReplacementComponentSelectionHandle.None;
            }

            if (!TryResolveTargetForReferenceKey(
                    stageTwoState,
                    stageTwoState.SupplementEntries
                        .Where(entry => entry != null && stageTwoState.KeptSupplementKeys.Contains(entry.Key))
                        .GroupBy(entry => entry.ReferenceKey)
                        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal),
                    referencedReferenceKey,
                    out _,
                    out _,
                    out GameObject mappedTargetObject)
                || mappedTargetObject == null)
            {
                return FbxReplacementComponentSelectionHandle.None;
            }

            string targetHierarchyKey = GetHierarchyIndexPath(stageTwoState.SessionState.Workspace.TargetWorkspaceRoot.transform, mappedTargetObject.transform);
            switch (referencedObject)
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

                case Component referencedComponent:
                    if (referencedComponent is Transform)
                    {
                        return new FbxReplacementComponentSelectionHandle(
                            FbxReplacementComponentSelectionKind.Transform,
                            targetHierarchyKey,
                            string.Empty,
                            -1);
                    }

                    if (entryKeyByReferenceComponent.TryGetValue(referencedComponent, out string referencedEntryKey)
                        && entriesByKey.TryGetValue(referencedEntryKey, out FbxReplacementComponentReviewEntry referencedEntry))
                    {
                        return new FbxReplacementComponentSelectionHandle(
                            FbxReplacementComponentSelectionKind.Component,
                            referencedEntry.TargetHierarchyKey,
                            referencedEntry.ComponentType.FullName ?? referencedEntry.ComponentType.Name,
                            referencedEntry.TypeSlotIndex);
                    }

                    return new FbxReplacementComponentSelectionHandle(
                        FbxReplacementComponentSelectionKind.Component,
                        targetHierarchyKey,
                        referencedComponent.GetType().FullName ?? referencedComponent.GetType().Name,
                        GetComponentSlotIndex(referencedComponent));

                default:
                    return FbxReplacementComponentSelectionHandle.None;
            }
        }

    }
}