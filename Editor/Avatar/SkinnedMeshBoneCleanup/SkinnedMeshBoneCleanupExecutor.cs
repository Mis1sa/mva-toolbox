using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.SkinnedMeshBoneCleanup
{
    internal static class SkinnedMeshBoneCleanupExecutor
    {
        public static bool Execute(
            IEnumerable<Renderer> removeCandidates,
            IReadOnlyDictionary<Renderer, List<Transform>> exclusiveBones,
            HashSet<Transform> protectedBones)
        {
            var candidateList = removeCandidates?
                .Where(renderer => renderer != null)
                .Distinct()
                .ToList() ?? new List<Renderer>();

            if (candidateList.Count == 0)
            {
                return false;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            var removedBones = new HashSet<Transform>();
            var bonesToRemove = new HashSet<Transform>();
            if (exclusiveBones != null)
            {
                foreach (var kvp in exclusiveBones)
                {
                    var bones = kvp.Value;
                    if (bones == null)
                    {
                        continue;
                    }

                    for (int i = 0; i < bones.Count; i++)
                    {
                        var bone = bones[i];
                        if (bone != null)
                        {
                            bonesToRemove.Add(bone);
                        }
                    }
                }
            }

            var context = new RemovalContext(bonesToRemove, protectedBones);
            var preservedNodes = new HashSet<Transform>();

            foreach (var bone in bonesToRemove)
            {
                if (bone == null)
                {
                    continue;
                }

                CollectChildActions(bone, context, preservedNodes);
            }

            ReleasePreservedNodes(preservedNodes, bonesToRemove);

            foreach (var renderer in candidateList)
            {
                if (renderer == null)
                {
                    continue;
                }

                Undo.DestroyObjectImmediate(renderer.gameObject);
            }

            foreach (var bone in bonesToRemove)
            {
                if (bone == null || removedBones.Contains(bone))
                {
                    continue;
                }

                Undo.DestroyObjectImmediate(bone.gameObject);
                removedBones.Add(bone);
            }

            Undo.CollapseUndoOperations(undoGroup);
            return true;
        }

        private static void CollectChildActions(
            Transform bone,
            RemovalContext context,
            HashSet<Transform> preservedNodes)
        {
            if (bone == null)
            {
                return;
            }

            for (int i = 0; i < bone.childCount; i++)
            {
                var child = bone.GetChild(i);
                if (child == null || context.bonesToRemove.Contains(child))
                {
                    continue;
                }

                preservedNodes.Add(child);
                if (context.bonesToKeep.Contains(child) || HasRendererComponent(child))
                {
                    continue;
                }

                CollectChildActions(child, context, preservedNodes);
            }
        }

        private static void ReleasePreservedNodes(IEnumerable<Transform> preservedNodes, HashSet<Transform> nodesToRemove)
        {
            foreach (var node in preservedNodes)
            {
                if (node == null)
                {
                    continue;
                }

                var parent = node.parent;
                if (parent == null || !nodesToRemove.Contains(parent))
                {
                    continue;
                }

                var safeParent = FindSafeParent(parent, nodesToRemove);
                Undo.SetTransformParent(node, safeParent, "释放骨骼子级");
            }
        }

        private static Transform FindSafeParent(Transform start, HashSet<Transform> nodesToRemove)
        {
            var current = start;
            while (current != null && nodesToRemove.Contains(current))
            {
                current = current.parent;
            }

            return current;
        }

        private static bool HasRendererComponent(Transform node)
        {
            return node != null && node.GetComponent<Renderer>() != null;
        }

        private readonly struct RemovalContext
        {
            internal RemovalContext(
                HashSet<Transform> bonesToRemove,
                HashSet<Transform> bonesToKeep)
            {
                this.bonesToRemove = bonesToRemove ?? new HashSet<Transform>();
                this.bonesToKeep = bonesToKeep ?? new HashSet<Transform>();
            }

            internal HashSet<Transform> bonesToRemove { get; }
            internal HashSet<Transform> bonesToKeep { get; }
        }
    }
}
