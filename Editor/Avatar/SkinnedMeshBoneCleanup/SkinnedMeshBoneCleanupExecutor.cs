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
            HashSet<Transform> protectedBones,
            HashSet<Transform> allBones,
            bool removeChildNonBoneObjects,
            bool excludeForeignChildObjects)
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

            var context = new RemovalContext(
                bonesToRemove,
                protectedBones,
                allBones,
                removeChildNonBoneObjects,
                excludeForeignChildObjects);
            var extraRemovals = new HashSet<Transform>();
            var preservedNodes = new HashSet<Transform>();

            foreach (var bone in bonesToRemove)
            {
                if (bone == null)
                {
                    continue;
                }

                CollectChildActions(bone, context, extraRemovals, preservedNodes);
            }

            var nodesToRemove = new HashSet<Transform>(bonesToRemove);
            nodesToRemove.UnionWith(extraRemovals);

            ReleasePreservedNodes(preservedNodes, nodesToRemove);

            foreach (var renderer in candidateList)
            {
                if (renderer == null)
                {
                    continue;
                }

                Undo.DestroyObjectImmediate(renderer.gameObject);
            }

            foreach (var node in extraRemovals)
            {
                if (node == null)
                {
                    continue;
                }

                Undo.DestroyObjectImmediate(node.gameObject);
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
            HashSet<Transform> extraRemovals,
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

                if (context.bonesToKeep.Contains(child) || HasRendererComponent(child))
                {
                    preservedNodes.Add(child);
                    continue;
                }

                if (ShouldRemoveChildObject(child, context))
                {
                    MarkSubtreeForRemoval(child, extraRemovals, context);
                    continue;
                }

                preservedNodes.Add(child);
                CollectChildActions(child, context, extraRemovals, preservedNodes);
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

        private static bool ShouldRemoveChildObject(Transform node, RemovalContext context)
        {
            if (!context.removeChildNonBoneObjects || node == null)
            {
                return false;
            }

            if (context.allBones.Contains(node) || HasRendererComponent(node))
            {
                return false;
            }

            if (context.excludeForeignChildObjects && ContainsProtectedElement(node, context))
            {
                return false;
            }

            return true;
        }

        private static void MarkSubtreeForRemoval(Transform node, HashSet<Transform> extraRemovals, RemovalContext context)
        {
            if (node == null || extraRemovals.Contains(node))
            {
                return;
            }

            if (context.bonesToKeep.Contains(node))
            {
                return;
            }

            extraRemovals.Add(node);
            for (int i = 0; i < node.childCount; i++)
            {
                var child = node.GetChild(i);
                MarkSubtreeForRemoval(child, extraRemovals, context);
            }
        }

        private static bool ContainsProtectedElement(Transform node, RemovalContext context)
        {
            var stack = new Stack<Transform>();
            stack.Push(node);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current == null)
                {
                    continue;
                }

                if (context.bonesToKeep.Contains(current) || HasRendererComponent(current))
                {
                    return true;
                }

                for (int i = 0; i < current.childCount; i++)
                {
                    stack.Push(current.GetChild(i));
                }
            }

            return false;
        }

        private static bool HasRendererComponent(Transform node)
        {
            return node != null && node.GetComponent<Renderer>() != null;
        }

        private readonly struct RemovalContext
        {
            internal RemovalContext(
                HashSet<Transform> bonesToRemove,
                HashSet<Transform> bonesToKeep,
                HashSet<Transform> allBones,
                bool removeChildNonBoneObjects,
                bool excludeForeignChildObjects)
            {
                this.bonesToRemove = bonesToRemove ?? new HashSet<Transform>();
                this.bonesToKeep = bonesToKeep ?? new HashSet<Transform>();
                this.allBones = allBones ?? new HashSet<Transform>();
                this.removeChildNonBoneObjects = removeChildNonBoneObjects;
                this.excludeForeignChildObjects = excludeForeignChildObjects;
            }

            internal HashSet<Transform> bonesToRemove { get; }
            internal HashSet<Transform> bonesToKeep { get; }
            internal HashSet<Transform> allBones { get; }
            internal bool removeChildNonBoneObjects { get; }
            internal bool excludeForeignChildObjects { get; }
        }
    }
}
