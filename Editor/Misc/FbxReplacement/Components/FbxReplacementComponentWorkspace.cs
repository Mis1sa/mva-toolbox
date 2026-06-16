using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.FbxReplacement
{
    internal static partial class FbxReplacementComponentWorkflow
    {
        private const string RootHierarchyKey = "@root";

        private static List<FbxReplacementTransformSnapshot> CaptureTransformSnapshots(GameObject root)
        {
            var result = new List<FbxReplacementTransformSnapshot>();
            if (root == null)
            {
                return result;
            }

            CaptureTransformSnapshotsRecursive(root.transform, root.transform, result);
            return result;
        }

        private static void CaptureTransformSnapshotsRecursive(
            Transform root,
            Transform current,
            List<FbxReplacementTransformSnapshot> result)
        {
            if (root == null || current == null || result == null)
            {
                return;
            }

            result.Add(new FbxReplacementTransformSnapshot(
                GetHierarchyIndexPath(root, current),
                current.localPosition,
                current.localRotation,
                current.localScale));

            for (int i = 0; i < current.childCount; i++)
            {
                CaptureTransformSnapshotsRecursive(root, current.GetChild(i), result);
            }
        }

        private static void RestoreTransforms(FbxReplacementStageFourState state)
        {
            if (state?.StageThreeState?.StageTwoState?.SessionState?.Workspace?.TargetWorkspaceRoot == null)
            {
                return;
            }

            Transform liveRoot = state.StageThreeState.StageTwoState.SessionState.Workspace.TargetWorkspaceRoot.transform;
            for (int i = 0; i < state.TransformSnapshots.Count; i++)
            {
                FbxReplacementTransformSnapshot snapshot = state.TransformSnapshots[i];
                if (snapshot == null)
                {
                    continue;
                }

                Transform transform = ResolveTransformByKey(liveRoot, snapshot.HierarchyKey);
                if (transform == null)
                {
                    continue;
                }

                transform.localPosition = snapshot.LocalPosition;
                transform.localRotation = snapshot.LocalRotation;
                transform.localScale = snapshot.LocalScale;
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

        private static void SetHideFlagsRecursively(Transform root, HideFlags flags)
        {
            if (root == null)
            {
                return;
            }

            root.gameObject.hideFlags = flags;
            Component[] components = root.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null && !(components[i] is Transform))
                {
                    components[i].hideFlags = flags;
                }
            }

            for (int i = 0; i < root.childCount; i++)
            {
                SetHideFlagsRecursively(root.GetChild(i), flags);
            }
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
    }
}