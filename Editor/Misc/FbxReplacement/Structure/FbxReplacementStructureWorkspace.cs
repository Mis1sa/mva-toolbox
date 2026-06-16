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
        private static bool IsDescendantKey(string key, string ancestorKey)
        {
            return key != null
                && ancestorKey != null
                && key != ancestorKey
                && (string.Equals(ancestorKey, RootHierarchyKey, StringComparison.Ordinal)
                    || key.StartsWith(ancestorKey + "/", StringComparison.Ordinal));
        }

        private static bool IsDescendantOrSelfKey(string key, string ancestorKey)
        {
            return key != null
                && ancestorKey != null
                && (string.Equals(key, ancestorKey, StringComparison.Ordinal) || IsDescendantKey(key, ancestorKey));
        }

        private static List<FbxReplacementStructureReferenceEntry> CollectReferenceEntries(Transform referenceRoot)
        {
            var result = new List<FbxReplacementStructureReferenceEntry>();
            if (referenceRoot == null)
            {
                return result;
            }

            CollectReferenceEntriesRecursive(referenceRoot, referenceRoot, result);
            return result;
        }

        private static void CollectReferenceEntriesRecursive(Transform root, Transform current, List<FbxReplacementStructureReferenceEntry> result)
        {
            string key = GetHierarchyIndexPath(root, current);
            string relativePath = GetRelativePathByName(root, current);
            string analysisPath = string.IsNullOrEmpty(relativePath)
                ? root.name
                : root.name + "/" + relativePath;
            result.Add(new FbxReplacementStructureReferenceEntry(key, analysisPath, current.name, GetDepthFromKey(key), current.childCount));
            for (int i = 0; i < current.childCount; i++)
            {
                CollectReferenceEntriesRecursive(root, current.GetChild(i), result);
            }
        }

        private static List<FbxReplacementStructureTargetEntry> CollectTargetEntries(Transform targetRoot, string originalTargetRootKey)
        {
            var result = new List<FbxReplacementStructureTargetEntry>();
            if (targetRoot == null)
            {
                return result;
            }

            Transform originalTargetRoot = ResolveTransformByKey(targetRoot, originalTargetRootKey);
            CollectTargetEntriesRecursive(targetRoot, targetRoot, originalTargetRoot, result);

            return result;
        }

        private static void CollectTargetEntriesRecursive(Transform root, Transform current, Transform originalTargetRoot, List<FbxReplacementStructureTargetEntry> result)
        {
            string key = GetHierarchyIndexPath(root, current);
            string analysisPath = string.Empty;
            if (originalTargetRoot != null && (current == originalTargetRoot || current.IsChildOf(originalTargetRoot)))
            {
                string relativePath = GetRelativePathByName(originalTargetRoot, current);
                analysisPath = string.IsNullOrEmpty(relativePath)
                    ? originalTargetRoot.name
                    : originalTargetRoot.name + "/" + relativePath;
            }

            result.Add(new FbxReplacementStructureTargetEntry(key, analysisPath, current.name, GetDepthFromKey(key), current.childCount));
            for (int i = 0; i < current.childCount; i++)
            {
                CollectTargetEntriesRecursive(root, current.GetChild(i), originalTargetRoot, result);
            }
        }

        private static Dictionary<string, GameObject> BuildObjectMap<TEntry>(IReadOnlyList<TEntry> entries, GameObject root)
        {
            var result = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            if (entries == null || root == null)
            {
                return result;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                string key = GetEntryKey(entries[i]);
                Transform resolved = ResolveTransformByKey(root.transform, key);
                result[key] = resolved != null ? resolved.gameObject : null;
            }

            return result;
        }

        private static string GetEntryKey<TEntry>(TEntry entry)
        {
            switch (entry)
            {
                case FbxReplacementStructureReferenceEntry referenceEntry:
                    return referenceEntry.Key;
                case FbxReplacementStructureTargetEntry targetEntry:
                    return targetEntry.Key;
                default:
                    return string.Empty;
            }
        }

        private static GameObject CreateHiddenTemplate(GameObject source, Scene scene)
        {
            if (source == null)
            {
                return null;
            }

            GameObject clone = Object.Instantiate(source);
            SceneManager.MoveGameObjectToScene(clone, scene);
            clone.name = source.name;
            SetHideFlagsRecursively(clone.transform, HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor);
            clone.SetActive(false);
            return clone;
        }

        private static GameObject InstantiateVisibleClone(GameObject template, Scene scene)
        {
            if (template == null)
            {
                return null;
            }

            GameObject clone = Object.Instantiate(template);
            SceneManager.MoveGameObjectToScene(clone, scene);
            clone.name = template.name;
            SetHideFlagsRecursively(clone.transform, HideFlags.None);
            clone.SetActive(true);
            return clone;
        }

        private static void SetHideFlagsRecursively(Transform root, HideFlags hideFlags)
        {
            if (root == null)
            {
                return;
            }

            root.gameObject.hideFlags = hideFlags;
            for (int i = 0; i < root.childCount; i++)
            {
                SetHideFlagsRecursively(root.GetChild(i), hideFlags);
            }
        }

        private static void DestroyVisibleWorkspaceCopies(FbxReplacementStageTwoState state)
        {
            if (state == null)
            {
                return;
            }

            var visibleObjects = state.CurrentReferenceObjectsByKey.Values
                .Concat(state.CurrentTargetObjectsByKey.Values)
                .Concat(state.CurrentSupplementObjectsByKey.Values)
                .Concat(new[]
                {
                    state.SessionState != null && state.SessionState.Workspace != null ? state.SessionState.Workspace.ReferenceInstanceRoot : null,
                    state.SessionState != null && state.SessionState.Workspace != null ? state.SessionState.Workspace.TargetWorkspaceRoot : null
                })
                .Where(gameObject => gameObject != null)
                .Distinct()
                .ToList();
            var visibleSet = new HashSet<GameObject>(visibleObjects);
            var rootsToDestroy = new List<GameObject>();
            for (int i = 0; i < visibleObjects.Count; i++)
            {
                GameObject gameObject = visibleObjects[i];
                Transform parent = gameObject.transform.parent;
                if (parent == null || !visibleSet.Contains(parent.gameObject))
                {
                    rootsToDestroy.Add(gameObject);
                }
            }

            for (int i = 0; i < rootsToDestroy.Count; i++)
            {
                Object.DestroyImmediate(rootsToDestroy[i]);
            }
        }

        private static void RefreshWorkspaceTargetRoot(FbxReplacementStageTwoState state)
        {
            if (state?.SessionState?.Workspace == null)
            {
                return;
            }

            GameObject anchorObject = state.SessionState.Workspace.TargetOriginalRoot;
            if (anchorObject == null)
            {
                anchorObject = state.CurrentTargetObjectsByKey.Values.FirstOrDefault(gameObject => gameObject != null)
                    ?? state.CurrentSupplementObjectsByKey.Values.FirstOrDefault(gameObject => gameObject != null);
            }

            if (anchorObject == null)
            {
                state.SessionState.Workspace.TargetWorkspaceRoot = null;
                return;
            }

            Transform current = anchorObject.transform;
            while (current.parent != null)
            {
                current = current.parent;
            }

            state.SessionState.Workspace.TargetWorkspaceRoot = current.gameObject;
        }

        private static void RegisterSupplementSubtreeObjects(
            FbxReplacementStageTwoState state,
            Transform referenceTransform,
            Transform supplementTransform)
        {
            if (state == null || referenceTransform == null || supplementTransform == null)
            {
                return;
            }

            string referenceKey = GetHierarchyIndexPath(state.BaselineReferenceTemplate.transform, referenceTransform);
            if (referenceKey != null
                && !state.MatchedReferenceKeys.Contains(referenceKey)
                && !state.AcceptedSupplementReferenceKeys.Contains(referenceKey))
            {
                state.CurrentSupplementObjectsByKey[BuildSupplementKey(referenceKey)] = supplementTransform.gameObject;
            }

            int childCount = Math.Min(referenceTransform.childCount, supplementTransform.childCount);
            for (int i = 0; i < childCount; i++)
            {
                RegisterSupplementSubtreeObjects(state, referenceTransform.GetChild(i), supplementTransform.GetChild(i));
            }
        }

        private static void AddSubtreeHighlight(Dictionary<int, Color> highlightMap, GameObject rootObject, Color color)
        {
            if (highlightMap == null || rootObject == null)
            {
                return;
            }

            highlightMap[rootObject.GetInstanceID()] = color;
            Transform rootTransform = rootObject.transform;
            for (int i = 0; i < rootTransform.childCount; i++)
            {
                AddSubtreeHighlight(highlightMap, rootTransform.GetChild(i).gameObject, color);
            }
        }

        private static string GetCurrentSelectedReferenceRootRelativePath(FbxReplacementStageTwoState state)
        {
            if (state == null
                || state.SessionState == null
                || state.SessionState.Workspace == null
                || state.SessionState.Workspace.ReferenceInstanceRoot == null
                || state.SessionState.SelectedReferenceRootSource == null)
            {
                return string.Empty;
            }

            return GetRelativePathByName(
                state.SessionState.Workspace.ReferenceInstanceRoot.transform,
                state.SessionState.SelectedReferenceRootSource.transform);
        }

        private static GameObject ResolveReferenceRootAfterRebuild(
            FbxReplacementStageTwoState state,
            string relativePath,
            GameObject newReferenceRoot)
        {
            if (state == null || newReferenceRoot == null)
            {
                return null;
            }

            if (string.IsNullOrEmpty(relativePath))
            {
                return newReferenceRoot;
            }

            Transform resolved = newReferenceRoot.transform.Find(relativePath);
            return resolved != null ? resolved.gameObject : newReferenceRoot;
        }

        private static List<GameObject> CollectCreatedRootPathObjects(GameObject targetWorkspaceRoot, GameObject targetOriginalRoot)
        {
            var result = new List<GameObject>();
            if (targetWorkspaceRoot == null || targetOriginalRoot == null || targetWorkspaceRoot == targetOriginalRoot)
            {
                return result;
            }

            Transform current = targetOriginalRoot.transform.parent;
            while (current != null)
            {
                result.Add(current.gameObject);
                if (current.gameObject == targetWorkspaceRoot)
                {
                    break;
                }

                current = current.parent;
            }

            return result;
        }

        private static string FindReferenceKey(FbxReplacementStageTwoState state, GameObject candidate)
        {
            if (state == null || candidate == null)
            {
                return null;
            }

            foreach (KeyValuePair<string, GameObject> pair in state.CurrentReferenceObjectsByKey)
            {
                if (pair.Value == candidate)
                {
                    return pair.Key;
                }
            }

            return null;
        }

        private static GameObject ResolveObjectByKey(Dictionary<string, GameObject> objectMap, string key)
        {
            if (objectMap == null)
            {
                return null;
            }

            if (string.Equals(key, RootHierarchyKey, StringComparison.Ordinal))
            {
                return objectMap.TryGetValue(RootHierarchyKey, out GameObject rootObject)
                    ? rootObject
                    : null;
            }

            return objectMap.TryGetValue(key, out GameObject resolvedObject)
                ? resolvedObject
                : null;
        }

        private static Transform ResolveTransformByKey(Transform root, string key)
        {
            if (root == null)
            {
                return null;
            }

            if (string.Equals(key, RootHierarchyKey, StringComparison.Ordinal))
            {
                return root;
            }

            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            string[] segments = key.Split('/');
            Transform current = root;
            for (int i = 0; i < segments.Length; i++)
            {
                if (!int.TryParse(segments[i], out int childIndex) || childIndex < 0 || childIndex >= current.childCount)
                {
                    return null;
                }

                current = current.GetChild(childIndex);
            }

            return current;
        }

        private static string GetHierarchyIndexPath(Transform root, Transform target)
        {
            if (root == null || target == null)
            {
                return null;
            }

            if (root == target)
            {
                return RootHierarchyKey;
            }

            var segments = new Stack<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                segments.Push(current.GetSiblingIndex().ToString());
                current = current.parent;
            }

            return current == root
                ? string.Join("/", segments.ToArray())
                : null;
        }

        private static string GetRelativePathByName(Transform root, Transform target)
        {
            if (root == null || target == null)
            {
                return string.Empty;
            }

            if (root == target)
            {
                return string.Empty;
            }

            var segments = new Stack<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                segments.Push(current.name);
                current = current.parent;
            }

            return current == root
                ? string.Join("/", segments.ToArray())
                : string.Empty;
        }

        private static int GetDepthFromKey(string key)
        {
            return string.IsNullOrEmpty(key) || string.Equals(key, RootHierarchyKey, StringComparison.Ordinal)
                ? 0
                : key.Split('/').Length;
        }
    }
}