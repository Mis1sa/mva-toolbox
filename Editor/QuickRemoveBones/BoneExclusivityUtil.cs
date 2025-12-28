using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MVA.Toolbox.QuickRemoveBones
{
    /// <summary>
    /// 提供独占骨骼判定与骨骼使用统计的纯工具方法，供 QuickRemoveBones 及其他脚本共用。
    /// </summary>
    internal static class BoneExclusivityUtil
    {
        /// <summary>
        /// 针对同一 Avatar 根下的一组 SkinnedMeshRenderer，构建骨骼 -> 使用该骨骼的 Renderer 集合。
        /// 注意：只统计实际被 mesh.boneWeights 使用到的骨骼索引。
        /// </summary>
        public static Dictionary<Transform, HashSet<Renderer>> BuildBoneUsage(IEnumerable<Renderer> candidates)
        {
            var map = new Dictionary<Transform, HashSet<Renderer>>();
            if (candidates == null) return map;

            var visitedRoots = new HashSet<Transform>();
            foreach (var renderer in candidates)
            {
                if (renderer == null) continue;

                var root = renderer.transform != null ? renderer.transform.root : null;
                if (root == null || !visitedRoots.Add(root))
                {
                    continue;
                }

                var skinnedMeshes = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var smr in skinnedMeshes)
                {
                    if (smr == null || smr.bones == null || smr.sharedMesh == null) continue;

                    var usedIndices = GetUsedBoneIndices(smr.sharedMesh);
                    if (usedIndices.Count == 0) continue;

                    foreach (var boneIndex in usedIndices)
                    {
                        if (boneIndex < 0 || boneIndex >= smr.bones.Length) continue;

                        var bone = smr.bones[boneIndex];
                        if (bone == null) continue;

                        if (!map.TryGetValue(bone, out var set))
                        {
                            set = new HashSet<Renderer>();
                            map[bone] = set;
                        }

                        set.Add(smr);
                    }
                }
            }

            return map;
        }

        /// <summary>
        /// 获取指定 SMR 的“独占骨骼”列表：该 SMR 使用的骨骼，且使用者集合全部在 candidateRenderers 内。
        /// </summary>
        public static List<Transform> CollectExclusiveBones(
            Renderer renderer,
            Dictionary<Transform, HashSet<Renderer>> boneUsage,
            IEnumerable<Renderer> candidateRenderers)
        {
            var bones = new List<Transform>();
            if (renderer is not SkinnedMeshRenderer smr || smr.sharedMesh == null || smr.bones == null)
            {
                return bones;
            }

            var candidateSet = new HashSet<Renderer>(candidateRenderers ?? Array.Empty<Renderer>());
            var usedIndices = GetUsedBoneIndices(smr.sharedMesh);
            foreach (var boneIndex in usedIndices)
            {
                if (boneIndex < 0 || boneIndex >= smr.bones.Length) continue;

                var bone = smr.bones[boneIndex];
                if (bone == null) continue;

                if (!boneUsage.TryGetValue(bone, out var users) || users.Count == 0) continue;

                if (users.All(candidateSet.Contains))
                {
                    bones.Add(bone);
                }
            }

            return bones
                .Where(b => b != null)
                .OrderBy(b => b.name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// 给定骨骼使用映射，返回“受保护骨骼”：被非候选 Renderer 使用的骨骼。
        /// </summary>
        public static HashSet<Transform> CollectProtectedBones(
            Dictionary<Transform, HashSet<Renderer>> boneUsage,
            IEnumerable<Renderer> candidateRenderers)
        {
            var protectedBones = new HashSet<Transform>();
            if (boneUsage == null || boneUsage.Count == 0) return protectedBones;

            var candidateSet = new HashSet<Renderer>(candidateRenderers ?? Array.Empty<Renderer>());
            foreach (var pair in boneUsage)
            {
                if (pair.Value.Any(renderer => !candidateSet.Contains(renderer)))
                {
                    protectedBones.Add(pair.Key);
                }
            }

            return protectedBones;
        }

        /// <summary>
        /// 获取 mesh 中被骨骼权重使用到的骨骼索引集合（权重>0）。
        /// </summary>
        public static HashSet<int> GetUsedBoneIndices(Mesh mesh)
        {
            var indices = new HashSet<int>();
            if (mesh == null) return indices;

            var weights = mesh.boneWeights;
            if (weights == null || weights.Length == 0) return indices;

            for (int i = 0; i < weights.Length; i++)
            {
                var bw = weights[i];
                if (bw.weight0 > 0f) indices.Add(bw.boneIndex0);
                if (bw.weight1 > 0f) indices.Add(bw.boneIndex1);
                if (bw.weight2 > 0f) indices.Add(bw.boneIndex2);
                if (bw.weight3 > 0f) indices.Add(bw.boneIndex3);
            }

            return indices;
        }
    }
}
