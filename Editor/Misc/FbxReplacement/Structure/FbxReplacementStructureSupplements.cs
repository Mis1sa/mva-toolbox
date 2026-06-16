using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.FbxReplacement
{
    internal static partial class FbxReplacementStructureWorkflow
    {
        private static bool EnsureReferenceSupplements(FbxReplacementStageTwoState state)
        {
            if (state == null)
            {
                return false;
            }

            if (!state.ReferenceSupplementsInitialized)
            {
                RebuildReferenceSupplements(state);
                state.ReferenceSupplementsInitialized = true;
            }
            else
            {
                CreateUnlockedSupplementObjects(state);
            }

            return state.SupplementEntries.Count > 0;
        }

        private static void RebuildReferenceSupplements(FbxReplacementStageTwoState state)
        {
            if (state == null)
            {
                return;
            }

            state.SupplementEntries.Clear();
            state.CurrentSupplementObjectsByKey.Clear();
            List<FbxReplacementStructureSupplementEntry> supplementEntries = BuildSupplementEntries(state);
            for (int i = 0; i < supplementEntries.Count; i++)
            {
                FbxReplacementStructureSupplementEntry supplementEntry = supplementEntries[i];
                if (supplementEntry == null)
                {
                    continue;
                }

                state.SupplementEntries.Add(supplementEntry);
            }

            for (int i = 0; i < state.SupplementEntries.Count; i++)
            {
                FbxReplacementStructureSupplementEntry supplementEntry = state.SupplementEntries[i];
                if (supplementEntry == null)
                {
                    continue;
                }
            }

            CreateUnlockedSupplementObjects(state);
        }

        private static List<FbxReplacementStructureSupplementEntry> BuildSupplementEntries(FbxReplacementStageTwoState state)
        {
            var result = new List<FbxReplacementStructureSupplementEntry>();
            if (state == null)
            {
                return result;
            }

            var unresolvedEntriesByKey = new Dictionary<string, FbxReplacementStructureReferenceEntry>(StringComparer.Ordinal);
            var referenceOrderByKey = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < state.ReferenceEntries.Count; i++)
            {
                FbxReplacementStructureReferenceEntry referenceEntry = state.ReferenceEntries[i];
                if (referenceEntry == null
                    || state.MatchedReferenceKeys.Contains(referenceEntry.Key))
                {
                    continue;
                }

                unresolvedEntriesByKey[referenceEntry.Key] = referenceEntry;
                referenceOrderByKey[referenceEntry.Key] = i;
            }

            var resolvedReferenceKeys = new HashSet<string>(state.MatchedReferenceKeys, StringComparer.Ordinal);
            bool progressed;
            do
            {
                progressed = false;

                List<FbxReplacementStructureReferenceEntry> descendantBatch = state.ReferenceEntries
                    .Where(entry => entry != null
                        && unresolvedEntriesByKey.ContainsKey(entry.Key)
                        && resolvedReferenceKeys.Contains(GetParentKey(entry.Key)))
                    .OrderBy(entry => entry.Depth)
                    .ThenBy(entry => referenceOrderByKey[entry.Key])
                    .ToList();
                for (int i = 0; i < descendantBatch.Count; i++)
                {
                    FbxReplacementStructureReferenceEntry referenceEntry = descendantBatch[i];
                    string parentKey = GetParentKey(referenceEntry.Key);
                    result.Add(new FbxReplacementStructureSupplementEntry(
                        BuildSupplementKey(referenceEntry.Key),
                        referenceEntry.Key,
                        parentKey,
                        ResolveMatchedTargetKeyForReferenceKey(state, parentKey),
                        string.Empty,
                        FbxReplacementStructureSupplementMode.Descendant));
                    unresolvedEntriesByKey.Remove(referenceEntry.Key);
                    resolvedReferenceKeys.Add(referenceEntry.Key);
                    progressed = true;
                }

                List<FbxReplacementStructureReferenceEntry> ancestorBatch = state.ReferenceEntries
                    .Where(entry => entry != null
                        && unresolvedEntriesByKey.ContainsKey(entry.Key)
                        && ResolveDirectResolvedChildReferenceKey(state, resolvedReferenceKeys, entry.Key) != null)
                    .OrderByDescending(entry => entry.Depth)
                    .ThenBy(entry => referenceOrderByKey[entry.Key])
                    .ToList();
                for (int i = 0; i < ancestorBatch.Count; i++)
                {
                    FbxReplacementStructureReferenceEntry referenceEntry = ancestorBatch[i];
                    string parentKey = GetParentKey(referenceEntry.Key);
                    string anchorReferenceKey = ResolveDirectResolvedChildReferenceKey(state, resolvedReferenceKeys, referenceEntry.Key);
                    if (string.IsNullOrEmpty(anchorReferenceKey))
                    {
                        continue;
                    }

                    result.Add(new FbxReplacementStructureSupplementEntry(
                        BuildSupplementKey(referenceEntry.Key),
                        referenceEntry.Key,
                        parentKey,
                        BuildSupplementKey(anchorReferenceKey),
                        anchorReferenceKey,
                        FbxReplacementStructureSupplementMode.Ancestor));
                    unresolvedEntriesByKey.Remove(referenceEntry.Key);
                    resolvedReferenceKeys.Add(referenceEntry.Key);
                    progressed = true;
                }
            }
            while (progressed && unresolvedEntriesByKey.Count > 0);

            if (unresolvedEntriesByKey.Count == 0)
            {
                return result;
            }

            for (int i = 0; i < state.ReferenceEntries.Count; i++)
            {
                FbxReplacementStructureReferenceEntry referenceEntry = state.ReferenceEntries[i];
                if (referenceEntry == null || !unresolvedEntriesByKey.ContainsKey(referenceEntry.Key))
                {
                    continue;
                }

                string parentKey = GetParentKey(referenceEntry.Key);
                string anchorReferenceKey = ResolveAncestorSupplementAnchorReferenceKey(state, referenceEntry.Key);
                if (!string.IsNullOrEmpty(anchorReferenceKey)
                    && !string.Equals(anchorReferenceKey, referenceEntry.Key, StringComparison.Ordinal))
                {
                    result.Add(new FbxReplacementStructureSupplementEntry(
                        BuildSupplementKey(referenceEntry.Key),
                        referenceEntry.Key,
                        parentKey,
                        BuildSupplementKey(anchorReferenceKey),
                        anchorReferenceKey,
                        FbxReplacementStructureSupplementMode.Ancestor));
                    continue;
                }

                result.Add(new FbxReplacementStructureSupplementEntry(
                    BuildSupplementKey(referenceEntry.Key),
                    referenceEntry.Key,
                    parentKey,
                    ResolveSupplementAnchorTargetKey(state, referenceEntry.Key),
                    string.Empty,
                    FbxReplacementStructureSupplementMode.Descendant));
            }

            return result;
        }

        private static string ResolveMatchedTargetKeyForReferenceKey(FbxReplacementStageTwoState state, string referenceKey)
        {
            if (state == null || string.IsNullOrEmpty(referenceKey))
            {
                return null;
            }

            return state.MatchedTargetKeyByReferenceKey.TryGetValue(referenceKey, out string targetKey)
                ? targetKey
                : null;
        }

        private static string ResolveDirectResolvedChildReferenceKey(
            FbxReplacementStageTwoState state,
            HashSet<string> resolvedReferenceKeys,
            string referenceKey)
        {
            if (state == null || resolvedReferenceKeys == null || string.IsNullOrEmpty(referenceKey))
            {
                return null;
            }

            for (int i = 0; i < state.ReferenceEntries.Count; i++)
            {
                FbxReplacementStructureReferenceEntry entry = state.ReferenceEntries[i];
                if (entry == null
                    || !resolvedReferenceKeys.Contains(entry.Key)
                    || !string.Equals(GetParentKey(entry.Key), referenceKey, StringComparison.Ordinal))
                {
                    continue;
                }

                return entry.Key;
            }

            return null;
        }

        private static GameObject CreateSupplementObject(FbxReplacementStageTwoState state, FbxReplacementStructureSupplementEntry supplementEntry)
        {
            if (state == null || supplementEntry == null || state.BaselineReferenceTemplate == null)
            {
                return null;
            }

            Transform referenceTransform = ResolveTransformByKey(state.BaselineReferenceTemplate.transform, supplementEntry.ReferenceKey);
            if (referenceTransform == null)
            {
                return null;
            }

            GameObject clone = Object.Instantiate(referenceTransform.gameObject);
            SceneManager.MoveGameObjectToScene(clone, state.SessionState.Workspace.Scene);
            clone.name = referenceTransform.name;
            if (supplementEntry.Mode == FbxReplacementStructureSupplementMode.Ancestor)
            {
                while (clone.transform.childCount > 0)
                {
                    Object.DestroyImmediate(clone.transform.GetChild(0).gameObject);
                }
            }

            StripDisallowedSupplementComponentsRecursively(clone.transform);
            SetHideFlagsRecursively(clone.transform, HideFlags.None);
            Transform anchorTransform = ResolveSupplementAnchorTargetTransform(state, supplementEntry);
            if (anchorTransform == null)
            {
                Object.DestroyImmediate(clone);
                return null;
            }

            clone.transform.SetParent(
                supplementEntry.Mode == FbxReplacementStructureSupplementMode.Ancestor ? anchorTransform.parent : anchorTransform,
                false);
            state.CurrentSupplementObjectsByKey[supplementEntry.Key] = clone;
            ApplySupplementSiblingOrder(state, supplementEntry, clone.transform, referenceTransform);
            clone.SetActive(true);
            RestoreSupplementBaseline(clone);
            ApplySupplementTransform(
                state,
                supplementEntry.Key,
                supplementEntry.Mode == FbxReplacementStructureSupplementMode.Descendant,
                true,
                true);
            return clone;
        }

        private static void ApplySupplementSiblingOrder(
            FbxReplacementStageTwoState state,
            FbxReplacementStructureSupplementEntry supplementEntry,
            Transform supplementTransform,
            Transform referenceTransform)
        {
            if (state == null || supplementEntry == null || supplementTransform == null || referenceTransform == null)
            {
                return;
            }

            Transform targetParent = supplementTransform.parent;
            Transform referenceParent = referenceTransform.parent;
            if (targetParent == null || referenceParent == null)
            {
                return;
            }

            if (TryPlaceAfterPreviousReferenceSibling(state, supplementTransform, referenceTransform, targetParent, referenceParent))
            {
                ApplyReferenceSiblingOrder(state, targetParent, referenceParent);
                return;
            }

            if (TryPlaceBeforeNextReferenceSibling(state, supplementTransform, referenceTransform, targetParent, referenceParent))
            {
                ApplyReferenceSiblingOrder(state, targetParent, referenceParent);
                return;
            }

            int desiredIndex = Mathf.Clamp(referenceTransform.GetSiblingIndex(), 0, targetParent.childCount - 1);
            supplementTransform.SetSiblingIndex(desiredIndex);
            ApplyReferenceSiblingOrder(state, targetParent, referenceParent);
        }

        private static void ApplyReferenceSiblingOrder(FbxReplacementStageTwoState state, Transform targetParent, Transform referenceParent)
        {
            if (state == null || targetParent == null || referenceParent == null)
            {
                return;
            }

            var orderedTransforms = new List<Transform>();
            var seenTransforms = new HashSet<Transform>();
            for (int i = 0; i < referenceParent.childCount; i++)
            {
                GameObject workspaceObject = ResolveWorkspaceObjectForReferenceTransform(state, referenceParent.GetChild(i));
                if (workspaceObject == null || workspaceObject.transform.parent != targetParent || !seenTransforms.Add(workspaceObject.transform))
                {
                    continue;
                }

                orderedTransforms.Add(workspaceObject.transform);
            }

            if (orderedTransforms.Count <= 1)
            {
                return;
            }

            int insertIndex = orderedTransforms.Min(transform => transform.GetSiblingIndex());
            for (int i = 0; i < orderedTransforms.Count; i++)
            {
                orderedTransforms[i].SetSiblingIndex(insertIndex + i);
            }
        }

        private static bool TryPlaceAfterPreviousReferenceSibling(
            FbxReplacementStageTwoState state,
            Transform supplementTransform,
            Transform referenceTransform,
            Transform targetParent,
            Transform referenceParent)
        {
            for (int i = referenceTransform.GetSiblingIndex() - 1; i >= 0; i--)
            {
                GameObject siblingObject = ResolveWorkspaceObjectForReferenceTransform(state, referenceParent.GetChild(i));
                if (siblingObject == null || siblingObject.transform == supplementTransform || siblingObject.transform.parent != targetParent)
                {
                    continue;
                }

                int desiredIndex = Mathf.Min(siblingObject.transform.GetSiblingIndex() + 1, targetParent.childCount - 1);
                supplementTransform.SetSiblingIndex(desiredIndex);
                return true;
            }

            return false;
        }

        private static bool TryPlaceBeforeNextReferenceSibling(
            FbxReplacementStageTwoState state,
            Transform supplementTransform,
            Transform referenceTransform,
            Transform targetParent,
            Transform referenceParent)
        {
            for (int i = referenceTransform.GetSiblingIndex() + 1; i < referenceParent.childCount; i++)
            {
                GameObject siblingObject = ResolveWorkspaceObjectForReferenceTransform(state, referenceParent.GetChild(i));
                if (siblingObject == null || siblingObject.transform == supplementTransform || siblingObject.transform.parent != targetParent)
                {
                    continue;
                }

                int siblingIndex = siblingObject.transform.GetSiblingIndex();
                int desiredIndex = supplementTransform.GetSiblingIndex() < siblingIndex
                    ? Mathf.Max(0, siblingIndex - 1)
                    : siblingIndex;
                supplementTransform.SetSiblingIndex(desiredIndex);
                return true;
            }

            return false;
        }

        private static GameObject ResolveWorkspaceObjectForReferenceTransform(FbxReplacementStageTwoState state, Transform referenceTransform)
        {
            if (state == null || state.BaselineReferenceTemplate == null || referenceTransform == null)
            {
                return null;
            }

            string referenceKey = GetHierarchyIndexPath(state.BaselineReferenceTemplate.transform, referenceTransform);
            return ResolveWorkspaceObjectForReferenceKey(state, referenceKey);
        }

        private static void StripDisallowedSupplementComponentsRecursively(Transform root)
        {
            if (root == null)
            {
                return;
            }

            bool removedAny;
            do
            {
                removedAny = false;
                Component[] components = root.GetComponents<Component>();
                for (int i = components.Length - 1; i >= 0; i--)
                {
                    Component component = components[i];
                    if (component == null || IsAllowedSupplementComponent(component))
                    {
                        continue;
                    }

                    Object.DestroyImmediate(component);
                    removedAny = true;
                }
            }
            while (removedAny);

            for (int i = 0; i < root.childCount; i++)
            {
                StripDisallowedSupplementComponentsRecursively(root.GetChild(i));
            }
        }

        private static bool IsAllowedSupplementComponent(Component component)
        {
            return component is Transform
                || component is MeshFilter
                || component is MeshRenderer
                || component is SkinnedMeshRenderer;
        }

        private static string BuildSupplementKey(string referenceKey)
        {
            return referenceKey == null
                ? null
                : "supp/" + referenceKey;
        }

        private static string GetParentKey(string key)
        {
            if (key == null)
            {
                return null;
            }

            if (string.Equals(key, RootHierarchyKey, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            int lastSlashIndex = key.LastIndexOf('/');
            return lastSlashIndex >= 0
                ? key.Substring(0, lastSlashIndex)
                : RootHierarchyKey;
        }

        private static string ResolveSupplementAnchorTargetKey(FbxReplacementStageTwoState state, string referenceKey)
        {
            if (state == null || referenceKey == null)
            {
                return null;
            }

            string parentKey = GetParentKey(referenceKey);
            while (!string.IsNullOrEmpty(parentKey))
            {
                if (state.MatchedTargetKeyByReferenceKey.TryGetValue(parentKey, out string targetKey))
                {
                    return targetKey;
                }

                parentKey = GetParentKey(parentKey);
            }

            return null;
        }

        private static string ResolveAncestorSupplementAnchorReferenceKey(FbxReplacementStageTwoState state, string referenceKey)
        {
            if (state == null || referenceKey == null)
            {
                return null;
            }

            string bestKey = null;
            int referenceDepth = GetDepthFromKey(referenceKey);
            int bestDepth = int.MaxValue;
            for (int i = 0; i < state.ReferenceEntries.Count; i++)
            {
                FbxReplacementStructureReferenceEntry entry = state.ReferenceEntries[i];
                if (entry == null
                    || !IsDescendantOrSelfKey(entry.Key, referenceKey)
                    || ResolveWorkspaceObjectForReferenceKey(state, entry.Key) == null)
                {
                    continue;
                }

                int depth = GetDepthFromKey(entry.Key);
                if (depth <= referenceDepth)
                {
                    continue;
                }

                if (depth < bestDepth)
                {
                    bestDepth = depth;
                    bestKey = entry.Key;
                }
            }

            return bestKey;
        }

        private static Transform ResolveSupplementAnchorTargetTransform(FbxReplacementStageTwoState state, FbxReplacementStructureSupplementEntry supplementEntry)
        {
            if (state == null || state.SessionState == null || state.SessionState.Workspace == null)
            {
                return null;
            }

            if (supplementEntry == null)
            {
                return null;
            }

            string anchorReferenceKey = supplementEntry.Mode == FbxReplacementStructureSupplementMode.Ancestor
                ? supplementEntry.AnchorReferenceKey
                : supplementEntry.ParentReferenceKey;
            GameObject anchorTargetObject = ResolveWorkspaceObjectForReferenceKey(state, anchorReferenceKey);
            if (anchorTargetObject != null)
            {
                return anchorTargetObject.transform;
            }

            if (!string.IsNullOrEmpty(supplementEntry.AnchorTargetKey))
            {
                anchorTargetObject = ResolveObjectByKey(state.CurrentTargetObjectsByKey, supplementEntry.AnchorTargetKey);
                if (anchorTargetObject != null)
                {
                    return anchorTargetObject.transform;
                }
            }

            return state.SessionState.Workspace.TargetWorkspaceRoot != null
                ? state.SessionState.Workspace.TargetWorkspaceRoot.transform
                : null;
        }

        private static GameObject ResolveSupplementObjectByKey(FbxReplacementStageTwoState state, string supplementKey)
        {
            if (state == null || string.IsNullOrEmpty(supplementKey))
            {
                return null;
            }

            return state.CurrentSupplementObjectsByKey.TryGetValue(supplementKey, out GameObject supplementObject)
                ? supplementObject
                : null;
        }

        private static void ApplySupplementTransform(FbxReplacementStageTwoState state, string supplementKey, bool includeChildren, bool alignTransform, bool affectChildren)
        {
            if (state == null || string.IsNullOrEmpty(supplementKey) || state.BaselineReferenceTemplate == null)
            {
                return;
            }

            List<string> decisionKeys = GetSupplementDecisionKeys(state, supplementKey, includeChildren);
            for (int i = 0; i < decisionKeys.Count; i++)
            {
                FbxReplacementStructureSupplementEntry supplementEntry = state.SupplementEntries.FirstOrDefault(entry =>
                    entry != null && string.Equals(entry.Key, decisionKeys[i], StringComparison.Ordinal));
                if (supplementEntry == null)
                {
                    continue;
                }

                GameObject supplementObject = ResolveSupplementObjectByKey(state, decisionKeys[i]);
                Transform referenceTransform = ResolveTransformByKey(state.BaselineReferenceTemplate.transform, supplementEntry.ReferenceKey);
                if (supplementObject == null || referenceTransform == null)
                {
                    continue;
                }

                Transform supplementTransform = supplementObject.transform;
                bool isAncestorSupplement = supplementEntry.Mode == FbxReplacementStructureSupplementMode.Ancestor;
                List<GameObject> ancestorChildObjects = isAncestorSupplement
                    ? ResolveDirectWorkspaceChildrenForReference(state, referenceTransform)
                    : null;

                // 仅当为祖先补物体且不影响子级变换时，记录已匹配物体的世界变换
                List<WorldTransformSnapshot> matchedSnapshots = null;
                if (isAncestorSupplement && !affectChildren)
                {
                    matchedSnapshots = CaptureMatchedWorldSnapshots(state, supplementEntry.ReferenceKey);
                }

                if (isAncestorSupplement)
                {
                    Transform releaseParent = supplementTransform.parent;
                    for (int childIndex = 0; ancestorChildObjects != null && childIndex < ancestorChildObjects.Count; childIndex++)
                    {
                        GameObject childObject = ancestorChildObjects[childIndex];
                        if (childObject != null && childObject.transform.parent == supplementTransform)
                        {
                            childObject.transform.SetParent(releaseParent, true);
                        }
                    }
                }

                if (alignTransform)
                {
                    supplementTransform.localPosition = referenceTransform.localPosition;
                    supplementTransform.localRotation = referenceTransform.localRotation;
                    supplementTransform.localScale = referenceTransform.localScale;
                }

                if (isAncestorSupplement)
                {
                    for (int childIndex = 0; ancestorChildObjects != null && childIndex < ancestorChildObjects.Count; childIndex++)
                    {
                        GameObject childObject = ancestorChildObjects[childIndex];
                        if (childObject != null && childObject.transform != supplementTransform)
                        {
                            childObject.transform.SetParent(supplementTransform, !affectChildren);
                        }
                    }

                    ApplyReferenceSiblingOrder(state, supplementTransform, referenceTransform);
                    ApplyReferenceSiblingOrder(state, supplementTransform.parent, referenceTransform.parent);
                }

                // 若不影响子级变换，则恢复匹配物体的世界变换
                if (matchedSnapshots != null)
                {
                    RestoreWorldSnapshots(matchedSnapshots);
                }
            }
        }

        private static List<GameObject> ResolveDirectWorkspaceChildrenForReference(FbxReplacementStageTwoState state, Transform referenceTransform)
        {
            var result = new List<GameObject>();
            if (state == null || referenceTransform == null)
            {
                return result;
            }

            var seenObjects = new HashSet<GameObject>();
            for (int i = 0; i < referenceTransform.childCount; i++)
            {
                GameObject workspaceObject = ResolveWorkspaceObjectForReferenceTransform(state, referenceTransform.GetChild(i));
                if (workspaceObject == null || !seenObjects.Add(workspaceObject))
                {
                    continue;
                }

                result.Add(workspaceObject);
            }

            return result;
        }

        private struct WorldTransformSnapshot
        {
            internal Transform Transform;
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal Vector3 LossyScale;
        }

        private static List<WorldTransformSnapshot> CaptureMatchedWorldSnapshots(
            FbxReplacementStageTwoState state,
            string referenceAncestorKey)
        {
            var result = new List<WorldTransformSnapshot>();
            if (state == null || string.IsNullOrEmpty(referenceAncestorKey))
            {
                return result;
            }

            foreach (KeyValuePair<string, string> kvp in state.MatchedTargetKeyByReferenceKey)
            {
                string referenceKey = kvp.Key;
                if (!IsDescendantOrSelfKey(referenceKey, referenceAncestorKey))
                {
                    continue;
                }

                GameObject targetObject = ResolveObjectByKey(state.CurrentTargetObjectsByKey, kvp.Value);
                if (targetObject == null)
                {
                    continue;
                }

                Transform t = targetObject.transform;
                result.Add(new WorldTransformSnapshot
                {
                    Transform = t,
                    Position = t.position,
                    Rotation = t.rotation,
                    LossyScale = t.lossyScale
                });
            }

            return result;
        }

        private static void RestoreWorldSnapshots(List<WorldTransformSnapshot> snapshots)
        {
            if (snapshots == null)
            {
                return;
            }

            for (int i = 0; i < snapshots.Count; i++)
            {
                WorldTransformSnapshot snapshot = snapshots[i];
                Transform t = snapshot.Transform;
                if (t == null)
                {
                    continue;
                }

                t.position = snapshot.Position;
                t.rotation = snapshot.Rotation;

                Transform parent = t.parent;
                Vector3 desiredWorldScale = snapshot.LossyScale;
                if (parent == null)
                {
                    t.localScale = desiredWorldScale;
                }
                else
                {
                    Vector3 parentScale = parent.lossyScale;
                    t.localScale = new Vector3(
                        SafeDivide(desiredWorldScale.x, parentScale.x),
                        SafeDivide(desiredWorldScale.y, parentScale.y),
                        SafeDivide(desiredWorldScale.z, parentScale.z));
                }
            }
        }

        private static float SafeDivide(float numerator, float denominator)
        {
            const float Epsilon = 1e-5f;
            if (Mathf.Abs(denominator) < Epsilon)
            {
                return 0f;
            }

            return numerator / denominator;
        }

        private static void RestoreSupplementBaseline(GameObject supplementObject)
        {
            if (supplementObject == null)
            {
                return;
            }

            Transform supplementTransform = supplementObject.transform;
            supplementTransform.localPosition = Vector3.zero;
            supplementTransform.localRotation = Quaternion.identity;
            supplementTransform.localScale = Vector3.one;
        }

        private static void CreateUnlockedSupplementObjects(FbxReplacementStageTwoState state)
        {
            if (state == null)
            {
                return;
            }

            bool createdAny;
            do
            {
                createdAny = false;
                for (int i = 0; i < state.SupplementEntries.Count; i++)
                {
                    FbxReplacementStructureSupplementEntry supplementEntry = state.SupplementEntries[i];
                    if (supplementEntry == null
                        || state.KeptSupplementKeys.Contains(supplementEntry.Key)
                        || state.RemovedSupplementKeys.Contains(supplementEntry.Key)
                        || ResolveSupplementObjectByKey(state, supplementEntry.Key) != null
                        || HasUnmatchedSupplementParent(state, supplementEntry))
                    {
                        continue;
                    }

                    GameObject supplementRootObject = CreateSupplementObject(state, supplementEntry);
                    if (supplementRootObject == null)
                    {
                        continue;
                    }

                    if (supplementEntry.Mode == FbxReplacementStructureSupplementMode.Ancestor)
                    {
                        state.CurrentSupplementObjectsByKey[supplementEntry.Key] = supplementRootObject;
                        createdAny = true;
                        continue;
                    }

                    Transform referenceTransform = ResolveTransformByKey(state.BaselineReferenceTemplate.transform, supplementEntry.ReferenceKey);
                    if (referenceTransform == null)
                    {
                        Object.DestroyImmediate(supplementRootObject);
                        continue;
                    }

                    RegisterSupplementSubtreeObjects(state, referenceTransform, supplementRootObject.transform);
                    createdAny = true;
                }
            }
            while (createdAny);

            ApplyResolvedReferenceHierarchyOrder(state);
        }

        private static void ApplyResolvedReferenceHierarchyOrder(FbxReplacementStageTwoState state)
        {
            if (state == null || state.BaselineReferenceTemplate == null)
            {
                return;
            }

            for (int i = 0; i < state.ReferenceEntries.Count; i++)
            {
                FbxReplacementStructureReferenceEntry referenceEntry = state.ReferenceEntries[i];
                if (referenceEntry == null)
                {
                    continue;
                }

                Transform referenceTransform = ResolveTransformByKey(state.BaselineReferenceTemplate.transform, referenceEntry.Key);
                GameObject targetParentObject = ResolveWorkspaceObjectForReferenceKey(state, referenceEntry.Key);
                if (referenceTransform == null || targetParentObject == null)
                {
                    continue;
                }

                ApplyReferenceSiblingOrder(state, targetParentObject.transform, referenceTransform);
            }
        }

        private static bool IsSupplementDecision(FbxReplacementStructureDecisionRecord decision)
        {
            return decision != null
                && (decision.ActionType == FbxReplacementStructureAlignmentActionType.SupplementKeep
                    || decision.ActionType == FbxReplacementStructureAlignmentActionType.SupplementRemove);
        }

        private static List<string> GetSupplementDecisionKeys(
            FbxReplacementStageTwoState state,
            string supplementKey,
            bool includeChildren)
        {
            var result = new List<string>();
            if (state == null || string.IsNullOrEmpty(supplementKey))
            {
                return result;
            }

            FbxReplacementStructureSupplementEntry rootEntry = state.SupplementEntries.FirstOrDefault(entry =>
                entry != null && string.Equals(entry.Key, supplementKey, StringComparison.Ordinal));
            if (rootEntry == null)
            {
                return result;
            }

            string referenceKey = rootEntry.ReferenceKey;
            for (int i = 0; i < state.SupplementEntries.Count; i++)
            {
                FbxReplacementStructureSupplementEntry entry = state.SupplementEntries[i];
                if (entry == null)
                {
                    continue;
                }

                if (!string.Equals(entry.ReferenceKey, referenceKey, StringComparison.Ordinal)
                    && (!includeChildren || !IsDescendantKey(entry.ReferenceKey, referenceKey)))
                {
                    continue;
                }

                result.Add(entry.Key);
            }

            return result;
        }

        private static bool HasUnmatchedSupplementParent(
            FbxReplacementStageTwoState state,
            FbxReplacementStructureSupplementEntry supplementEntry)
        {
            return state != null
                && supplementEntry != null
                && supplementEntry.Mode == FbxReplacementStructureSupplementMode.Descendant
                && supplementEntry.ParentReferenceKey != null
                && supplementEntry.ParentReferenceKey.Length > 0
                && ResolveWorkspaceObjectForReferenceKey(state, supplementEntry.ParentReferenceKey) == null;
        }

        private static bool IsReferenceResolvedForSupplementHierarchy(FbxReplacementStageTwoState state, string referenceKey)
        {
            return state != null
                && !string.IsNullOrEmpty(referenceKey)
                && (state.MatchedReferenceKeys.Contains(referenceKey)
                    || state.AcceptedSupplementReferenceKeys.Contains(referenceKey));
        }

        private static GameObject ResolveWorkspaceObjectForReferenceKey(FbxReplacementStageTwoState state, string referenceKey)
        {
            if (state == null || referenceKey == null)
            {
                return null;
            }

            if (state.MatchedTargetKeyByReferenceKey.TryGetValue(referenceKey, out string targetKey))
            {
                GameObject matchedTargetObject = ResolveObjectByKey(state.CurrentTargetObjectsByKey, targetKey);
                if (matchedTargetObject != null)
                {
                    return matchedTargetObject;
                }
            }

            return ResolveSupplementObjectByKey(state, BuildSupplementKey(referenceKey));
        }
    }
}