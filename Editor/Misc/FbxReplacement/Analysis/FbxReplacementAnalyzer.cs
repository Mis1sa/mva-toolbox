using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MVA.Toolbox.FbxReplacement
{
    internal static class FbxReplacementAnalyzer
    {
        internal static FbxReplacementAnalysisResult Analyze(GameObject referenceRoot, GameObject targetRoot)
        {
            if (referenceRoot == null)
            {
                throw new ArgumentNullException(nameof(referenceRoot));
            }

            if (targetRoot == null)
            {
                throw new ArgumentNullException(nameof(targetRoot));
            }

            var referenceSnapshot = BuildSnapshot(referenceRoot);
            var targetSnapshot = BuildSnapshot(targetRoot);
            var meshAnchors = BuildMeshAnchors(referenceSnapshot, targetSnapshot);
            var nodeMatches = BuildNodeMatches(referenceSnapshot, targetSnapshot, meshAnchors);
            return new FbxReplacementAnalysisResult(
                referenceSnapshot,
                targetSnapshot,
                nodeMatches);
        }

        private static FbxReplacementObjectSnapshot BuildSnapshot(GameObject rootObject)
        {
            var nodes = new List<FbxReplacementNodeSnapshot>();
            CollectNode(rootObject.transform, string.Empty, 0, nodes);
            return new FbxReplacementObjectSnapshot(rootObject, nodes);
        }

        private static void CollectNode(Transform current, string parentPath, int depth, List<FbxReplacementNodeSnapshot> nodes)
        {
            if (current == null)
            {
                return;
            }

            string path = string.IsNullOrEmpty(parentPath) ? current.name : parentPath + "/" + current.name;
            var childNames = new List<string>(current.childCount);
            for (int i = 0; i < current.childCount; i++)
            {
                var child = current.GetChild(i);
                childNames.Add(child != null ? child.name : string.Empty);
            }

            var components = current.GetComponents<Component>()
                .Where(component => component != null && !(component is Transform))
                .Select(component => new FbxReplacementComponentSnapshot(component, path))
                .ToList();
            var renderer = current.GetComponent<Renderer>();
            nodes.Add(new FbxReplacementNodeSnapshot(
                current.gameObject,
                path,
                parentPath,
                depth,
                childNames,
                components,
                FbxReplacementRendererSignature.Create(renderer)));

            for (int i = 0; i < current.childCount; i++)
            {
                CollectNode(current.GetChild(i), path, depth + 1, nodes);
            }
        }

        private static List<FbxReplacementNodeMatch> BuildMeshAnchors(
            FbxReplacementObjectSnapshot referenceSnapshot,
            FbxReplacementObjectSnapshot targetSnapshot)
        {
            var referenceNodes = referenceSnapshot.Nodes.Where(FbxReplacementMatchScorer.IsMeshLikeNode).ToList();
            var targetNodes = targetSnapshot.Nodes.Where(FbxReplacementMatchScorer.IsMeshLikeNode).ToList();
            return BuildOptimalMatches(referenceNodes, targetNodes, FbxReplacementMatchScorer.ScoreMeshAnchorPair, 65f, true);
        }

        private static List<FbxReplacementNodeMatch> BuildNodeMatches(
            FbxReplacementObjectSnapshot referenceSnapshot,
            FbxReplacementObjectSnapshot targetSnapshot,
            List<FbxReplacementNodeMatch> meshAnchors)
        {
            var matches = meshAnchors != null
                ? new List<FbxReplacementNodeMatch>(meshAnchors)
                : new List<FbxReplacementNodeMatch>();

            bool matchedInPass;
            do
            {
                matchedInPass = false;
                var matchedReferenceNodes = new HashSet<FbxReplacementNodeSnapshot>(matches.Select(match => match.ReferenceNode));
                var matchedTargetNodes = new HashSet<FbxReplacementNodeSnapshot>(matches.Select(match => match.TargetNode));
                var referenceToTarget = matches.ToDictionary(match => match.ReferenceNode, match => match.TargetNode);
                var targetToReference = matches.ToDictionary(match => match.TargetNode, match => match.ReferenceNode);

                var referenceNodes = referenceSnapshot.Nodes.Where(node => !matchedReferenceNodes.Contains(node)).ToList();
                var targetNodes = targetSnapshot.Nodes.Where(node => !matchedTargetNodes.Contains(node)).ToList();
                var passMatches = BuildOptimalMatches(
                    referenceNodes,
                    targetNodes,
                    (referenceNode, targetNode) =>
                    {
                        float score = FbxReplacementMatchScorer.ScoreNodePair(referenceNode, targetNode, referenceToTarget, targetToReference, out string reason);
                        return (score, reason);
                    },
                    48f,
                    false);

                for (int i = 0; i < passMatches.Count; i++)
                {
                    matches.Add(passMatches[i]);
                }

                matchedInPass = passMatches.Count > 0;
            }
            while (matchedInPass);

            matches.Sort((left, right) =>
            {
                int scoreCompare = right.Score.CompareTo(left.Score);
                return scoreCompare != 0
                    ? scoreCompare
                    : string.CompareOrdinal(left.ReferenceNode.Path, right.ReferenceNode.Path);
            });
            return matches;
        }

        private static List<FbxReplacementNodeMatch> BuildOptimalMatches(
            IEnumerable<FbxReplacementNodeSnapshot> referenceNodes,
            IEnumerable<FbxReplacementNodeSnapshot> targetNodes,
            Func<FbxReplacementNodeSnapshot, FbxReplacementNodeSnapshot, (float score, string reason)> scorer,
            float minScore,
            bool isMeshAnchor)
        {
            var candidates = new List<FbxReplacementNodeMatch>();
            var referenceList = referenceNodes.ToList();
            var targetList = targetNodes.ToList();

            for (int i = 0; i < referenceList.Count; i++)
            {
                var referenceNode = referenceList[i];
                for (int j = 0; j < targetList.Count; j++)
                {
                    var targetNode = targetList[j];
                    var result = scorer(referenceNode, targetNode);
                    if (result.score < minScore)
                    {
                        continue;
                    }

                    candidates.Add(new FbxReplacementNodeMatch(referenceNode, targetNode, result.score, isMeshAnchor, result.reason));
                }
            }

            if (referenceList.Count == 0 || targetList.Count == 0)
            {
                return new List<FbxReplacementNodeMatch>();
            }

            int referenceCount = referenceList.Count;
            int targetCount = targetList.Count;
            int matrixSize = referenceCount + targetCount;
            var weights = new double[matrixSize, matrixSize];
            var candidateMatrix = new FbxReplacementNodeMatch[referenceCount, targetCount];

            for (int i = 0; i < candidates.Count; i++)
            {
                FbxReplacementNodeMatch candidate = candidates[i];
                int referenceIndex = referenceList.IndexOf(candidate.ReferenceNode);
                int targetIndex = targetList.IndexOf(candidate.TargetNode);
                if (referenceIndex < 0 || targetIndex < 0)
                {
                    continue;
                }

                weights[referenceIndex, targetIndex] = candidate.Score;
                candidateMatrix[referenceIndex, targetIndex] = candidate;
            }

            int[] assignedColumnsByRow = SolveMaximumWeightAssignment(weights, matrixSize);
            var matches = new List<FbxReplacementNodeMatch>();
            for (int referenceIndex = 0; referenceIndex < referenceCount; referenceIndex++)
            {
                int assignedColumn = assignedColumnsByRow[referenceIndex];
                if (assignedColumn < 0 || assignedColumn >= targetCount)
                {
                    continue;
                }

                FbxReplacementNodeMatch candidate = candidateMatrix[referenceIndex, assignedColumn];
                if (candidate == null || candidate.Score < minScore)
                {
                    continue;
                }

                matches.Add(candidate);
            }

            matches.Sort((left, right) =>
            {
                int scoreCompare = right.Score.CompareTo(left.Score);
                if (scoreCompare != 0)
                {
                    return scoreCompare;
                }

                int referenceCompare = string.CompareOrdinal(left.ReferenceNode.Path, right.ReferenceNode.Path);
                return referenceCompare != 0
                    ? referenceCompare
                    : string.CompareOrdinal(left.TargetNode.Path, right.TargetNode.Path);
            });
            return matches;
        }

        private static int[] SolveMaximumWeightAssignment(double[,] weights, int size)
        {
            var costs = new double[size + 1, size + 1];
            double maxWeight = 0d;
            for (int row = 0; row < size; row++)
            {
                for (int column = 0; column < size; column++)
                {
                    if (weights[row, column] > maxWeight)
                    {
                        maxWeight = weights[row, column];
                    }
                }
            }

            for (int row = 1; row <= size; row++)
            {
                for (int column = 1; column <= size; column++)
                {
                    costs[row, column] = maxWeight - weights[row - 1, column - 1];
                }
            }

            var u = new double[size + 1];
            var v = new double[size + 1];
            var p = new int[size + 1];
            var way = new int[size + 1];
            for (int row = 1; row <= size; row++)
            {
                p[0] = row;
                int column0 = 0;
                var minv = new double[size + 1];
                var used = new bool[size + 1];
                for (int column = 0; column <= size; column++)
                {
                    minv[column] = double.PositiveInfinity;
                }

                do
                {
                    used[column0] = true;
                    int row0 = p[column0];
                    double delta = double.PositiveInfinity;
                    int column1 = 0;
                    for (int column = 1; column <= size; column++)
                    {
                        if (used[column])
                        {
                            continue;
                        }

                        double current = costs[row0, column] - u[row0] - v[column];
                        if (current < minv[column])
                        {
                            minv[column] = current;
                            way[column] = column0;
                        }

                        if (minv[column] < delta)
                        {
                            delta = minv[column];
                            column1 = column;
                        }
                    }

                    for (int column = 0; column <= size; column++)
                    {
                        if (used[column])
                        {
                            u[p[column]] += delta;
                            v[column] -= delta;
                        }
                        else
                        {
                            minv[column] -= delta;
                        }
                    }

                    column0 = column1;
                }
                while (p[column0] != 0);

                do
                {
                    int column1 = way[column0];
                    p[column0] = p[column1];
                    column0 = column1;
                }
                while (column0 != 0);
            }

            var assignedColumnsByRow = new int[size];
            for (int row = 0; row < size; row++)
            {
                assignedColumnsByRow[row] = -1;
            }

            for (int column = 1; column <= size; column++)
            {
                if (p[column] > 0)
                {
                    assignedColumnsByRow[p[column] - 1] = column - 1;
                }
            }

            return assignedColumnsByRow;
        }

    }
}
