using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MVA.Toolbox.FbxReplacement
{
    internal static partial class FbxReplacementStructureWorkflow
    {
        private static string FindRecommendedReferenceKey(FbxReplacementStageTwoState state, FbxReplacementStructureTargetEntry targetEntry)
        {
            if (state == null || targetEntry == null)
            {
                return null;
            }

            if (!state.CandidateReferenceKeysByTargetKey.TryGetValue(targetEntry.Key, out List<string> candidateKeys) || candidateKeys == null)
            {
                return null;
            }

            for (int i = 0; i < candidateKeys.Count; i++)
            {
                string candidateKey = candidateKeys[i];
                if (string.IsNullOrEmpty(candidateKey) || state.MatchedReferenceKeys.Contains(candidateKey))
                {
                    continue;
                }

                return candidateKey;
            }

            return null;
        }

        private static void BuildInitialRecommendations(FbxReplacementStageTwoState state)
        {
            if (state == null)
            {
                return;
            }

            state.CandidateReferenceKeysByTargetKey.Clear();
            state.RecommendedReferenceKeyByTargetKey.Clear();

            var allCandidates = new List<FbxReplacementStructureRecommendationCandidate>();
            for (int i = 0; i < state.TargetEntries.Count; i++)
            {
                FbxReplacementStructureTargetEntry targetEntry = state.TargetEntries[i];
                if (targetEntry == null)
                {
                    continue;
                }

                List<FbxReplacementStructureRecommendationCandidate> rankedCandidates = BuildRankedReferenceCandidates(state, targetEntry);
                state.CandidateReferenceKeysByTargetKey[targetEntry.Key] = rankedCandidates
                    .Select(candidate => candidate.ReferenceKey)
                    .Where(key => key != null)
                    .ToList();
                allCandidates.AddRange(rankedCandidates);
            }

            allCandidates.Sort((left, right) =>
            {
                int scoreCompare = right.Score.CompareTo(left.Score);
                if (scoreCompare != 0)
                {
                    return scoreCompare;
                }

                int targetCompare = string.CompareOrdinal(left.TargetKey, right.TargetKey);
                return targetCompare != 0
                    ? targetCompare
                    : string.CompareOrdinal(left.ReferenceKey, right.ReferenceKey);
            });

            var assignedTargetKeys = new HashSet<string>(StringComparer.Ordinal);
            var assignedReferenceKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < allCandidates.Count; i++)
            {
                FbxReplacementStructureRecommendationCandidate candidate = allCandidates[i];
                if (candidate == null
                    || candidate.TargetKey == null
                    || candidate.ReferenceKey == null
                    || assignedTargetKeys.Contains(candidate.TargetKey)
                    || assignedReferenceKeys.Contains(candidate.ReferenceKey))
                {
                    continue;
                }

                assignedTargetKeys.Add(candidate.TargetKey);
                assignedReferenceKeys.Add(candidate.ReferenceKey);
                state.RecommendedReferenceKeyByTargetKey[candidate.TargetKey] = candidate.ReferenceKey;
            }

            foreach (KeyValuePair<string, string> pair in state.RecommendedReferenceKeyByTargetKey)
            {
                if (!state.CandidateReferenceKeysByTargetKey.TryGetValue(pair.Key, out List<string> candidateKeys)
                    || candidateKeys == null
                    || candidateKeys.Count == 0)
                {
                    continue;
                }

                int currentIndex = candidateKeys.FindIndex(key => string.Equals(key, pair.Value, StringComparison.Ordinal));
                if (currentIndex <= 0)
                {
                    continue;
                }

                candidateKeys.RemoveAt(currentIndex);
                candidateKeys.Insert(0, pair.Value);
            }
        }

        private static List<FbxReplacementStructureRecommendationCandidate> BuildRankedReferenceCandidates(
            FbxReplacementStageTwoState state,
            FbxReplacementStructureTargetEntry targetEntry)
        {
            var result = new List<FbxReplacementStructureRecommendationCandidate>();
            if (state == null || targetEntry == null)
            {
                return result;
            }

            for (int i = 0; i < state.ReferenceEntries.Count; i++)
            {
                FbxReplacementStructureReferenceEntry referenceEntry = state.ReferenceEntries[i];
                if (referenceEntry == null)
                {
                    continue;
                }

                float score = ScoreCandidate(state, targetEntry, referenceEntry);
                result.Add(new FbxReplacementStructureRecommendationCandidate(targetEntry.Key, referenceEntry.Key, score));
            }

            result.Sort((left, right) =>
            {
                int scoreCompare = right.Score.CompareTo(left.Score);
                return scoreCompare != 0
                    ? scoreCompare
                    : string.CompareOrdinal(left.ReferenceKey, right.ReferenceKey);
            });
            return result;
        }

        private static float ScoreCandidate(
            FbxReplacementStageTwoState state,
            FbxReplacementStructureTargetEntry targetEntry,
            FbxReplacementStructureReferenceEntry referenceEntry)
        {
            float score = 0f;
            FbxReplacementNodeSnapshot targetNode = FindTargetSnapshotNode(state, targetEntry);
            FbxReplacementNodeSnapshot referenceNode = FindReferenceSnapshotNode(state, referenceEntry);
            GameObject currentTargetObject = ResolveObjectByKey(state.CurrentTargetObjectsByKey, targetEntry.Key);
            GameObject currentReferenceObject = ResolveObjectByKey(state.CurrentReferenceObjectsByKey, referenceEntry.Key);

            if (targetNode != null && referenceNode != null)
            {
                score += ScoreSnapshotPair(referenceNode, targetNode);
                if (IsAnalysisMatchedPair(state, referenceNode.Path, targetNode.Path))
                {
                    score += 140f;
                }
                if (IsAnalysisParentPair(state, referenceNode, targetNode))
                {
                    score += 25f;
                }
            }
            else
            {
                score += ScoreWorkspacePair(currentReferenceObject, currentTargetObject);
            }

            if (string.Equals(targetEntry.Name, referenceEntry.Name, StringComparison.Ordinal))
            {
                score += 20f;
            }
            else if (string.Equals(NormalizeName(targetEntry.Name), NormalizeName(referenceEntry.Name), StringComparison.Ordinal))
            {
                score += 12f;
            }

            int depthDelta = Mathf.Abs(targetEntry.Depth - referenceEntry.Depth);
            score += Mathf.Max(0, 10 - depthDelta * 2);

            int childCountDelta = Mathf.Abs(targetEntry.ChildCount - referenceEntry.ChildCount);
            score += Mathf.Max(0, 8 - childCountDelta * 2);

            float pathSimilarity = CalculatePathTailSimilarity(referenceEntry.AnalysisPath, targetEntry.AnalysisPath);
            if (pathSimilarity > 0f)
            {
                score += pathSimilarity * 12f;
            }

            if (string.IsNullOrEmpty(targetEntry.AnalysisPath))
            {
                score += 6f;
            }

            return score;
        }

        private static bool IsAnalysisMatchedPair(FbxReplacementStageTwoState state, string referencePath, string targetPath)
        {
            if (state == null || string.IsNullOrEmpty(referencePath) || string.IsNullOrEmpty(targetPath))
            {
                return false;
            }

            for (int i = 0; i < state.SessionState.AnalysisResult.NodeMatches.Count; i++)
            {
                FbxReplacementNodeMatch match = state.SessionState.AnalysisResult.NodeMatches[i];
                if (match == null || match.ReferenceNode == null || match.TargetNode == null)
                {
                    continue;
                }

                if (string.Equals(match.ReferenceNode.Path, referencePath, StringComparison.Ordinal)
                    && string.Equals(match.TargetNode.Path, targetPath, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAnalysisParentPair(
            FbxReplacementStageTwoState state,
            FbxReplacementNodeSnapshot referenceNode,
            FbxReplacementNodeSnapshot targetNode)
        {
            if (state == null || referenceNode == null || targetNode == null)
            {
                return false;
            }

            if (string.IsNullOrEmpty(referenceNode.ParentPath) || string.IsNullOrEmpty(targetNode.ParentPath))
            {
                return false;
            }

            return IsAnalysisMatchedPair(state, referenceNode.ParentPath, targetNode.ParentPath);
        }

        private static FbxReplacementNodeSnapshot FindReferenceSnapshotNode(
            FbxReplacementStageTwoState state,
            FbxReplacementStructureReferenceEntry referenceEntry)
        {
            if (state == null || referenceEntry == null || string.IsNullOrEmpty(referenceEntry.AnalysisPath))
            {
                return null;
            }

            return state.SessionState.AnalysisResult.ReferenceSnapshot.Nodes.FirstOrDefault(node =>
                string.Equals(node.Path, referenceEntry.AnalysisPath, StringComparison.Ordinal));
        }

        private static FbxReplacementNodeSnapshot FindTargetSnapshotNode(
            FbxReplacementStageTwoState state,
            FbxReplacementStructureTargetEntry targetEntry)
        {
            if (state == null || targetEntry == null || string.IsNullOrEmpty(targetEntry.AnalysisPath))
            {
                return null;
            }

            return state.SessionState.AnalysisResult.TargetSnapshot.Nodes.FirstOrDefault(node =>
                string.Equals(node.Path, targetEntry.AnalysisPath, StringComparison.Ordinal));
        }

        private static float ScoreSnapshotPair(
            FbxReplacementNodeSnapshot referenceNode,
            FbxReplacementNodeSnapshot targetNode)
        {
            if (referenceNode == null || targetNode == null)
            {
                return 0f;
            }

            float score = 0f;
            if (string.Equals(referenceNode.Name, targetNode.Name, StringComparison.Ordinal))
            {
                score += 35f;
            }
            else if (string.Equals(NormalizeName(referenceNode.Name), NormalizeName(targetNode.Name), StringComparison.Ordinal))
            {
                score += 20f;
            }

            float pathSimilarity = CalculatePathTailSimilarity(referenceNode.Path, targetNode.Path);
            if (pathSimilarity > 0f)
            {
                score += pathSimilarity * 18f;
            }

            if (referenceNode.Depth == targetNode.Depth)
            {
                score += 5f;
            }

            score += ScoreRendererSimilarity(referenceNode, targetNode) * 0.55f;
            score += CalculateSnapshotComponentOverlap(referenceNode, targetNode) * 15f;
            score += CalculateSnapshotChildNameOverlap(referenceNode, targetNode) * 12f;

            float positionDistance = Vector3.Distance(referenceNode.LocalPosition, targetNode.LocalPosition);
            if (positionDistance <= 0.001f)
            {
                score += 4f;
            }
            else if (positionDistance <= 0.02f)
            {
                score += 2f;
            }

            float scaleDistance = Vector3.Distance(referenceNode.LocalScale, targetNode.LocalScale);
            if (scaleDistance <= 0.001f)
            {
                score += 4f;
            }
            else if (scaleDistance <= 0.02f)
            {
                score += 2f;
            }

            return score;
        }

        private static float ScoreWorkspacePair(GameObject referenceObject, GameObject targetObject)
        {
            if (referenceObject == null || targetObject == null)
            {
                return 0f;
            }

            float score = 0f;
            if (string.Equals(referenceObject.name, targetObject.name, StringComparison.Ordinal))
            {
                score += 30f;
            }
            else if (string.Equals(NormalizeName(referenceObject.name), NormalizeName(targetObject.name), StringComparison.Ordinal))
            {
                score += 16f;
            }

            score += CalculateGameObjectComponentOverlap(referenceObject, targetObject) * 16f;
            score += CalculateGameObjectChildNameOverlap(referenceObject, targetObject) * 10f;

            float positionDistance = Vector3.Distance(referenceObject.transform.localPosition, targetObject.transform.localPosition);
            if (positionDistance <= 0.001f)
            {
                score += 4f;
            }

            float scaleDistance = Vector3.Distance(referenceObject.transform.localScale, targetObject.transform.localScale);
            if (scaleDistance <= 0.001f)
            {
                score += 4f;
            }

            return score;
        }

        private static float ScoreRendererSimilarity(
            FbxReplacementNodeSnapshot referenceNode,
            FbxReplacementNodeSnapshot targetNode)
        {
            if (referenceNode == null || targetNode == null)
            {
                return 0f;
            }

            float score = 0f;
            if (!referenceNode.Renderer.Exists || !targetNode.Renderer.Exists)
            {
                return score;
            }

            if (referenceNode.Renderer.RendererTypeName == targetNode.Renderer.RendererTypeName)
            {
                score += 25f;
            }

            if (!string.IsNullOrEmpty(referenceNode.Renderer.MeshName)
                && string.Equals(referenceNode.Renderer.MeshName, targetNode.Renderer.MeshName, StringComparison.Ordinal))
            {
                score += 35f;
            }

            if (referenceNode.Renderer.MaterialSlotCount == targetNode.Renderer.MaterialSlotCount)
            {
                score += 8f;
            }

            if (referenceNode.Renderer.BoneCount > 0 && referenceNode.Renderer.BoneCount == targetNode.Renderer.BoneCount)
            {
                score += 8f;
            }

            return score;
        }

        private static float CalculateSnapshotComponentOverlap(
            FbxReplacementNodeSnapshot referenceNode,
            FbxReplacementNodeSnapshot targetNode)
        {
            var referenceTypes = new HashSet<string>(referenceNode.Components.Select(component => component.FullTypeName));
            var targetTypes = new HashSet<string>(targetNode.Components.Select(component => component.FullTypeName));
            return CalculateSetOverlap(referenceTypes, targetTypes);
        }

        private static float CalculateSnapshotChildNameOverlap(
            FbxReplacementNodeSnapshot referenceNode,
            FbxReplacementNodeSnapshot targetNode)
        {
            var referenceNames = new HashSet<string>(referenceNode.ChildNames.Select(NormalizeName).Where(name => !string.IsNullOrEmpty(name)));
            var targetNames = new HashSet<string>(targetNode.ChildNames.Select(NormalizeName).Where(name => !string.IsNullOrEmpty(name)));
            return CalculateSetOverlap(referenceNames, targetNames);
        }

        private static float CalculateGameObjectComponentOverlap(GameObject referenceObject, GameObject targetObject)
        {
            var referenceTypes = new HashSet<string>(referenceObject.GetComponents<Component>()
                .Where(component => component != null && !(component is Transform))
                .Select(component => component.GetType().FullName));
            var targetTypes = new HashSet<string>(targetObject.GetComponents<Component>()
                .Where(component => component != null && !(component is Transform))
                .Select(component => component.GetType().FullName));
            return CalculateSetOverlap(referenceTypes, targetTypes);
        }

        private static float CalculateGameObjectChildNameOverlap(GameObject referenceObject, GameObject targetObject)
        {
            var referenceNames = new HashSet<string>();
            for (int i = 0; i < referenceObject.transform.childCount; i++)
            {
                referenceNames.Add(NormalizeName(referenceObject.transform.GetChild(i).name));
            }

            var targetNames = new HashSet<string>();
            for (int i = 0; i < targetObject.transform.childCount; i++)
            {
                targetNames.Add(NormalizeName(targetObject.transform.GetChild(i).name));
            }

            referenceNames.RemoveWhere(string.IsNullOrEmpty);
            targetNames.RemoveWhere(string.IsNullOrEmpty);
            return CalculateSetOverlap(referenceNames, targetNames);
        }

        private static float CalculateSetOverlap(HashSet<string> left, HashSet<string> right)
        {
            if ((left == null || left.Count == 0) && (right == null || right.Count == 0))
            {
                return 1f;
            }

            left ??= new HashSet<string>();
            right ??= new HashSet<string>();
            var union = new HashSet<string>(left);
            union.UnionWith(right);
            if (union.Count == 0)
            {
                return 0f;
            }

            var intersection = new HashSet<string>(left);
            intersection.IntersectWith(right);
            return intersection.Count / (float)union.Count;
        }

        private static float CalculatePathTailSimilarity(string referencePath, string targetPath)
        {
            if (string.IsNullOrEmpty(referencePath) || string.IsNullOrEmpty(targetPath))
            {
                return 0f;
            }

            string[] referenceSegments = referencePath.Split('/');
            string[] targetSegments = targetPath.Split('/');
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