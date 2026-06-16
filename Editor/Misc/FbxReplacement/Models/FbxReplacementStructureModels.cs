using System;
using System.Collections.Generic;
using UnityEngine;

namespace MVA.Toolbox.FbxReplacement
{
    internal enum FbxReplacementStageTwoWorkflowStep
    {
        StructureAlignment,
        ReferenceSupplementReview,
        Completed
    }

    internal enum FbxReplacementStructureAlignmentActionType
    {
        Match,
        Keep,
        Remove,
        SupplementKeep,
        SupplementRemove
    }

    internal enum FbxReplacementStructureSupplementMode
    {
        Descendant,
        Ancestor
    }

    internal sealed class FbxReplacementStructureReferenceEntry
    {
        internal FbxReplacementStructureReferenceEntry(string key, string analysisPath, string name, int depth, int childCount)
        {
            Key = key;
            AnalysisPath = analysisPath;
            Name = name ?? string.Empty;
            Depth = depth;
            ChildCount = childCount;
        }

        internal string Key { get; }
        internal string AnalysisPath { get; }
        internal string Name { get; }
        internal int Depth { get; }
        internal int ChildCount { get; }
    }

    internal sealed class FbxReplacementStructureTargetEntry
    {
        internal FbxReplacementStructureTargetEntry(string key, string analysisPath, string name, int depth, int childCount)
        {
            Key = key;
            AnalysisPath = analysisPath;
            Name = name ?? string.Empty;
            Depth = depth;
            ChildCount = childCount;
        }

        internal string Key { get; }
        internal string AnalysisPath { get; }
        internal string Name { get; }
        internal int Depth { get; }
        internal int ChildCount { get; }
    }

    internal sealed class FbxReplacementStructureRecommendationCandidate
    {
        internal FbxReplacementStructureRecommendationCandidate(string targetKey, string referenceKey, float score)
        {
            TargetKey = targetKey ?? string.Empty;
            ReferenceKey = referenceKey ?? string.Empty;
            Score = score;
        }

        internal string TargetKey { get; }
        internal string ReferenceKey { get; }
        internal float Score { get; }
    }

    internal sealed class FbxReplacementStructureSupplementEntry
    {
        internal FbxReplacementStructureSupplementEntry(
            string key,
            string referenceKey,
            string parentReferenceKey,
            string anchorTargetKey,
            string anchorReferenceKey,
            FbxReplacementStructureSupplementMode mode)
        {
            Key = key ?? string.Empty;
            ReferenceKey = referenceKey ?? string.Empty;
            ParentReferenceKey = parentReferenceKey ?? string.Empty;
            AnchorTargetKey = anchorTargetKey ?? string.Empty;
            AnchorReferenceKey = anchorReferenceKey ?? string.Empty;
            Mode = mode;
        }

        internal string Key { get; }
        internal string ReferenceKey { get; }
        internal string ParentReferenceKey { get; }
        internal string AnchorTargetKey { get; }
        internal string AnchorReferenceKey { get; }
        internal FbxReplacementStructureSupplementMode Mode { get; }
    }

    internal sealed class FbxReplacementStructureDecisionRecord
    {
        internal FbxReplacementStructureDecisionRecord(
            FbxReplacementStructureAlignmentActionType actionType,
            string targetKey,
            string referenceKey,
            bool alignName,
            bool alignTransform,
            bool includeChildren,
            bool affectChildren)
        {
            ActionType = actionType;
            TargetKey = targetKey;
            ReferenceKey = referenceKey;
            AlignName = alignName;
            AlignTransform = alignTransform;
            IncludeChildren = includeChildren;
            AffectChildren = affectChildren;
        }

        internal FbxReplacementStructureAlignmentActionType ActionType { get; }
        internal string TargetKey { get; }
        internal string ReferenceKey { get; }
        internal bool AlignName { get; }
        internal bool AlignTransform { get; }
        internal bool IncludeChildren { get; }
        internal bool AffectChildren { get; }
    }

    internal sealed class FbxReplacementStageTwoState
    {
        internal FbxReplacementStageTwoState(
            FbxReplacementSessionState sessionState,
            GameObject baselineReferenceTemplate,
            GameObject baselineTargetTemplate,
            string originalTargetRootKey,
            List<FbxReplacementStructureReferenceEntry> referenceEntries,
            List<FbxReplacementStructureTargetEntry> targetEntries)
        {
            SessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
            BaselineReferenceTemplate = baselineReferenceTemplate;
            BaselineTargetTemplate = baselineTargetTemplate;
            OriginalTargetRootKey = originalTargetRootKey ?? string.Empty;
            ReferenceEntries = referenceEntries ?? new List<FbxReplacementStructureReferenceEntry>();
            TargetEntries = targetEntries ?? new List<FbxReplacementStructureTargetEntry>();
            History = new List<FbxReplacementStructureDecisionRecord>();
            MatchedReferenceKeys = new HashSet<string>(StringComparer.Ordinal);
            MatchedTargetKeys = new HashSet<string>(StringComparer.Ordinal);
            PreservedTargetKeys = new HashSet<string>(StringComparer.Ordinal);
            RemovedTargetKeys = new HashSet<string>(StringComparer.Ordinal);
            AcceptedSupplementReferenceKeys = new HashSet<string>(StringComparer.Ordinal);
            KeptSupplementKeys = new HashSet<string>(StringComparer.Ordinal);
            RemovedSupplementKeys = new HashSet<string>(StringComparer.Ordinal);
            CurrentReferenceObjectsByKey = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            CurrentTargetObjectsByKey = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            CurrentSupplementObjectsByKey = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            CandidateReferenceKeysByTargetKey = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            RecommendedReferenceKeyByTargetKey = new Dictionary<string, string>(StringComparer.Ordinal);
            MatchedTargetKeyByReferenceKey = new Dictionary<string, string>(StringComparer.Ordinal);
            MatchedReferenceKeyByTargetKey = new Dictionary<string, string>(StringComparer.Ordinal);
            SupplementEntries = new List<FbxReplacementStructureSupplementEntry>();
            CurrentTargetIndex = -1;
            CurrentSupplementIndex = -1;
            AlignName = false;
            AlignTransform = true;
            IncludeChildren = false;
            AffectChildren = true;
            CurrentStep = FbxReplacementStageTwoWorkflowStep.StructureAlignment;
        }

        internal FbxReplacementSessionState SessionState { get; }
        internal GameObject BaselineReferenceTemplate { get; set; }
        internal GameObject BaselineTargetTemplate { get; set; }
        internal string OriginalTargetRootKey { get; }
        internal IReadOnlyList<FbxReplacementStructureReferenceEntry> ReferenceEntries { get; }
        internal IReadOnlyList<FbxReplacementStructureTargetEntry> TargetEntries { get; }
        internal List<FbxReplacementStructureDecisionRecord> History { get; }
        internal HashSet<string> MatchedReferenceKeys { get; }
        internal HashSet<string> MatchedTargetKeys { get; }
        internal HashSet<string> PreservedTargetKeys { get; }
        internal HashSet<string> RemovedTargetKeys { get; }
        internal HashSet<string> AcceptedSupplementReferenceKeys { get; }
        internal HashSet<string> KeptSupplementKeys { get; }
        internal HashSet<string> RemovedSupplementKeys { get; }
        internal Dictionary<string, GameObject> CurrentReferenceObjectsByKey { get; set; }
        internal Dictionary<string, GameObject> CurrentTargetObjectsByKey { get; set; }
        internal Dictionary<string, GameObject> CurrentSupplementObjectsByKey { get; set; }
        internal Dictionary<string, List<string>> CandidateReferenceKeysByTargetKey { get; }
        internal Dictionary<string, string> RecommendedReferenceKeyByTargetKey { get; }
        internal Dictionary<string, string> MatchedTargetKeyByReferenceKey { get; }
        internal Dictionary<string, string> MatchedReferenceKeyByTargetKey { get; }
        internal List<FbxReplacementStructureSupplementEntry> SupplementEntries { get; }
        internal int CurrentTargetIndex { get; set; }
        internal int CurrentSupplementIndex { get; set; }
        internal string SelectedReferenceKey { get; set; }
        internal bool AlignName { get; set; }
        internal bool AlignTransform { get; set; }
        internal bool IncludeChildren { get; set; }
        internal bool AffectChildren { get; set; }
        internal bool ReferenceSupplementsInitialized { get; set; }
        internal FbxReplacementStageTwoWorkflowStep CurrentStep { get; set; }
    }
}