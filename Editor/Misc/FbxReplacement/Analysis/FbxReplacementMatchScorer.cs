using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MVA.Toolbox.FbxReplacement
{
    internal static class FbxReplacementMatchScorer
    {
        internal static (float score, string reason) ScoreMeshAnchorPair(
            FbxReplacementNodeSnapshot referenceNode,
            FbxReplacementNodeSnapshot targetNode)
        {
            float score = 0f;
            var reasons = new List<string>();

            if (!referenceNode.Renderer.Exists || !targetNode.Renderer.Exists)
            {
                return (0f, string.Empty);
            }

            if (referenceNode.Renderer.RendererTypeName == targetNode.Renderer.RendererTypeName)
            {
                score += 25f;
                reasons.Add("同Renderer类型");
            }

            if (string.Equals(referenceNode.Name, targetNode.Name, StringComparison.Ordinal))
            {
                score += 25f;
                reasons.Add("同名");
            }
            else if (string.Equals(NormalizeName(referenceNode.Name), NormalizeName(targetNode.Name), StringComparison.Ordinal))
            {
                score += 15f;
                reasons.Add("近似同名");
            }

            if (!string.IsNullOrEmpty(referenceNode.Renderer.MeshName)
                && string.Equals(referenceNode.Renderer.MeshName, targetNode.Renderer.MeshName, StringComparison.Ordinal))
            {
                score += 35f;
                reasons.Add("同Mesh");
            }

            if (referenceNode.Renderer.MaterialSlotCount == targetNode.Renderer.MaterialSlotCount)
            {
                score += 8f;
                reasons.Add("材质槽一致");
            }

            if (referenceNode.Renderer.BoneCount > 0 && referenceNode.Renderer.BoneCount == targetNode.Renderer.BoneCount)
            {
                score += 8f;
                reasons.Add("骨骼数量一致");
            }

            float pathSimilarity = CalculatePathTailSimilarity(referenceNode.Path, targetNode.Path);
            if (pathSimilarity > 0f)
            {
                score += pathSimilarity * 12f;
                reasons.Add("路径尾部接近");
            }

            return (score, string.Join("、", reasons));
        }

        internal static float ScoreNodePair(
            FbxReplacementNodeSnapshot referenceNode,
            FbxReplacementNodeSnapshot targetNode,
            IReadOnlyDictionary<FbxReplacementNodeSnapshot, FbxReplacementNodeSnapshot> referenceToTarget,
            IReadOnlyDictionary<FbxReplacementNodeSnapshot, FbxReplacementNodeSnapshot> targetToReference,
            out string reason)
        {
            float score = 0f;
            var reasons = new List<string>();

            if (string.Equals(referenceNode.Name, targetNode.Name, StringComparison.Ordinal))
            {
                score += 35f;
                reasons.Add("同名");
            }
            else if (string.Equals(NormalizeName(referenceNode.Name), NormalizeName(targetNode.Name), StringComparison.Ordinal))
            {
                score += 20f;
                reasons.Add("近似同名");
            }

            float pathSimilarity = CalculatePathTailSimilarity(referenceNode.Path, targetNode.Path);
            if (pathSimilarity > 0f)
            {
                score += pathSimilarity * 18f;
                reasons.Add("路径尾部接近");
            }

            if (referenceNode.Depth == targetNode.Depth)
            {
                score += 5f;
                reasons.Add("深度一致");
            }

            if (referenceNode.Renderer.Exists || targetNode.Renderer.Exists)
            {
                var anchorResult = ScoreMeshAnchorPair(referenceNode, targetNode);
                if (anchorResult.score > 0f)
                {
                    score += anchorResult.score * 0.55f;
                    reasons.Add("Renderer特征接近");
                }
            }

            float componentOverlap = CalculateComponentOverlap(referenceNode, targetNode);
            if (componentOverlap > 0f)
            {
                score += componentOverlap * 15f;
                reasons.Add("组件集合接近");
            }

            float childNameOverlap = CalculateChildNameOverlap(referenceNode, targetNode);
            if (childNameOverlap > 0f)
            {
                score += childNameOverlap * 12f;
                reasons.Add("子级名称接近");
            }

            if (IsMatchedParentPair(referenceNode, targetNode, referenceToTarget, targetToReference))
            {
                score += 25f;
                reasons.Add("父级已匹配");
            }

            float positionDistance = Vector3.Distance(referenceNode.LocalPosition, targetNode.LocalPosition);
            if (positionDistance <= 0.001f)
            {
                score += 4f;
                reasons.Add("局部位置一致");
            }
            else if (positionDistance <= 0.02f)
            {
                score += 2f;
            }

            float scaleDistance = Vector3.Distance(referenceNode.LocalScale, targetNode.LocalScale);
            if (scaleDistance <= 0.001f)
            {
                score += 4f;
                reasons.Add("局部缩放一致");
            }
            else if (scaleDistance <= 0.02f)
            {
                score += 2f;
            }

            reason = string.Join("、", reasons.Distinct());
            return score;
        }

        internal static bool IsMeshLikeNode(FbxReplacementNodeSnapshot node)
        {
            if (node == null || !node.Renderer.Exists)
            {
                return false;
            }

            return string.Equals(node.Renderer.RendererTypeName, nameof(SkinnedMeshRenderer), StringComparison.Ordinal)
                || string.Equals(node.Renderer.RendererTypeName, nameof(MeshRenderer), StringComparison.Ordinal);
        }

        private static bool IsMatchedParentPair(
            FbxReplacementNodeSnapshot referenceNode,
            FbxReplacementNodeSnapshot targetNode,
            IReadOnlyDictionary<FbxReplacementNodeSnapshot, FbxReplacementNodeSnapshot> referenceToTarget,
            IReadOnlyDictionary<FbxReplacementNodeSnapshot, FbxReplacementNodeSnapshot> targetToReference)
        {
            if (string.IsNullOrEmpty(referenceNode.ParentPath) || string.IsNullOrEmpty(targetNode.ParentPath))
            {
                return false;
            }

            var matchedParent = referenceToTarget.Keys.FirstOrDefault(node => node.Path == referenceNode.ParentPath);
            if (matchedParent != null && referenceToTarget.TryGetValue(matchedParent, out var targetParent))
            {
                return targetParent.Path == targetNode.ParentPath;
            }

            var reverseMatchedParent = targetToReference.Keys.FirstOrDefault(node => node.Path == targetNode.ParentPath);
            if (reverseMatchedParent != null && targetToReference.TryGetValue(reverseMatchedParent, out var referenceParent))
            {
                return referenceParent.Path == referenceNode.ParentPath;
            }

            return false;
        }

        private static float CalculateComponentOverlap(FbxReplacementNodeSnapshot referenceNode, FbxReplacementNodeSnapshot targetNode)
        {
            var referenceTypes = new HashSet<string>(referenceNode.Components.Select(component => component.FullTypeName));
            var targetTypes = new HashSet<string>(targetNode.Components.Select(component => component.FullTypeName));
            if (referenceTypes.Count == 0 && targetTypes.Count == 0)
            {
                return 1f;
            }

            var union = new HashSet<string>(referenceTypes);
            union.UnionWith(targetTypes);
            if (union.Count == 0)
            {
                return 0f;
            }

            referenceTypes.IntersectWith(targetTypes);
            return referenceTypes.Count / (float)union.Count;
        }

        private static float CalculateChildNameOverlap(FbxReplacementNodeSnapshot referenceNode, FbxReplacementNodeSnapshot targetNode)
        {
            var referenceNames = new HashSet<string>(referenceNode.ChildNames.Select(NormalizeName).Where(name => !string.IsNullOrEmpty(name)));
            var targetNames = new HashSet<string>(targetNode.ChildNames.Select(NormalizeName).Where(name => !string.IsNullOrEmpty(name)));
            if (referenceNames.Count == 0 && targetNames.Count == 0)
            {
                return 1f;
            }

            var union = new HashSet<string>(referenceNames);
            union.UnionWith(targetNames);
            if (union.Count == 0)
            {
                return 0f;
            }

            referenceNames.IntersectWith(targetNames);
            return referenceNames.Count / (float)union.Count;
        }

        private static float CalculatePathTailSimilarity(string referencePath, string targetPath)
        {
            var referenceSegments = referencePath.Split('/');
            var targetSegments = targetPath.Split('/');
            int maxComparable = Math.Min(referenceSegments.Length, targetSegments.Length);
            if (maxComparable == 0)
            {
                return 0f;
            }

            int matched = 0;
            for (int i = 1; i <= maxComparable; i++)
            {
                string referenceSegment = NormalizeName(referenceSegments[referenceSegments.Length - i]);
                string targetSegment = NormalizeName(targetSegments[targetSegments.Length - i]);
                if (!string.Equals(referenceSegment, targetSegment, StringComparison.Ordinal))
                {
                    break;
                }

                matched++;
            }

            return matched / (float)maxComparable;
        }

        private static string NormalizeName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace(" ", string.Empty)
                .Replace("_", string.Empty)
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }
    }
}