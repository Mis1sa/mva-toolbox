using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MVA.Toolbox.SkinnedMeshBoneCleanup
{
    internal sealed class BoneExclusivityAnalysisResult
    {
        internal BoneExclusivityAnalysisResult(
            Dictionary<Renderer, List<Transform>> exclusiveBones,
            HashSet<Transform> protectedBones,
            HashSet<Transform> allBones)
        {
            this.exclusiveBones = exclusiveBones ?? new Dictionary<Renderer, List<Transform>>();
            this.protectedBones = protectedBones ?? new HashSet<Transform>();
            this.allBones = allBones ?? new HashSet<Transform>();
        }

        internal Dictionary<Renderer, List<Transform>> exclusiveBones { get; }
        internal HashSet<Transform> protectedBones { get; }
        internal HashSet<Transform> allBones { get; }
    }

    internal static class BoneExclusivityAnalyzer
    {
        public static BoneExclusivityAnalysisResult Analyze(IEnumerable<Renderer> candidates)
        {
            var candidateList = candidates?
                .Where(renderer => renderer != null)
                .Distinct()
                .ToList() ?? new List<Renderer>();

            var exclusiveBones = new Dictionary<Renderer, List<Transform>>();
            if (candidateList.Count == 0)
            {
                return new BoneExclusivityAnalysisResult(exclusiveBones, new HashSet<Transform>(), new HashSet<Transform>());
            }

            var boneUsage = BuildBoneUsage(candidateList);
            var allBones = new HashSet<Transform>(boneUsage.Keys);
            var protectedBones = CollectProtectedBones(boneUsage, candidateList);
            for (int i = 0; i < candidateList.Count; i++)
            {
                var candidate = candidateList[i];
                exclusiveBones[candidate] = CollectExclusiveBones(candidate, boneUsage, candidateList);
            }

            return new BoneExclusivityAnalysisResult(exclusiveBones, protectedBones, allBones);
        }

        public static Dictionary<Transform, HashSet<Renderer>> BuildBoneUsage(IEnumerable<Renderer> candidates)
        {
            var map = new Dictionary<Transform, HashSet<Renderer>>();
            if (candidates == null)
            {
                return map;
            }

            var visitedRoots = new HashSet<Transform>();
            foreach (var renderer in candidates)
            {
                if (renderer == null)
                {
                    continue;
                }

                var root = renderer.transform != null ? renderer.transform.root : null;
                if (root == null || !visitedRoots.Add(root))
                {
                    continue;
                }

                var skinnedMeshes = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var smr in skinnedMeshes)
                {
                    if (smr == null || smr.bones == null || smr.sharedMesh == null)
                    {
                        continue;
                    }

                    var usedIndices = GetUsedBoneIndices(smr.sharedMesh);
                    if (usedIndices.Count == 0)
                    {
                        continue;
                    }

                    foreach (var boneIndex in usedIndices)
                    {
                        if (boneIndex < 0 || boneIndex >= smr.bones.Length)
                        {
                            continue;
                        }

                        var bone = smr.bones[boneIndex];
                        if (bone == null)
                        {
                            continue;
                        }

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
                if (boneIndex < 0 || boneIndex >= smr.bones.Length)
                {
                    continue;
                }

                var bone = smr.bones[boneIndex];
                if (bone == null)
                {
                    continue;
                }

                if (!boneUsage.TryGetValue(bone, out var users) || users.Count == 0)
                {
                    continue;
                }

                if (users.All(candidateSet.Contains))
                {
                    bones.Add(bone);
                }
            }

            return bones
                .Where(bone => bone != null)
                .OrderBy(bone => bone.name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static HashSet<Transform> CollectProtectedBones(
            Dictionary<Transform, HashSet<Renderer>> boneUsage,
            IEnumerable<Renderer> candidateRenderers)
        {
            var protectedBones = new HashSet<Transform>();
            if (boneUsage == null || boneUsage.Count == 0)
            {
                return protectedBones;
            }

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

        public static HashSet<int> GetUsedBoneIndices(Mesh mesh)
        {
            var indices = new HashSet<int>();
            if (mesh == null)
            {
                return indices;
            }

            var weights = mesh.boneWeights;
            if (weights == null || weights.Length == 0)
            {
                return indices;
            }

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
