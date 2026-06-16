using System.Collections.Generic;
using UnityEngine;

namespace MVA.Toolbox.FbxReplacement
{
    internal static partial class FbxReplacementStructureWorkflow
    {
        private static void UpdateCurrentPreview(FbxReplacementStageTwoState state)
        {
            if (state == null)
            {
                return;
            }

            if (state.CurrentStep == FbxReplacementStageTwoWorkflowStep.ReferenceSupplementReview)
            {
                RestoreCurrentSupplementBaseline(state);
                FbxReplacementStructureSupplementEntry supplementEntry = GetCurrentSupplementEntry(state);
                if (supplementEntry != null)
                {
                    ApplySupplementTransform(state, supplementEntry.Key, state.IncludeChildren, state.AlignTransform, state.AffectChildren);
                }

                return;
            }

            RestoreCurrentTargetBaseline(state);
            GameObject currentTarget = GetCurrentTargetObject(state);
            GameObject selectedReference = GetSelectedReferenceObject(state);
            if (currentTarget == null || selectedReference == null)
            {
                return;
            }

            if (state.AlignName)
            {
                currentTarget.name = selectedReference.name;
            }

            if (state.AlignTransform)
            {
                Transform currentTransform = currentTarget.transform;
                Transform referenceTransform = selectedReference.transform;
                currentTransform.localPosition = referenceTransform.localPosition;
                currentTransform.localRotation = referenceTransform.localRotation;
                currentTransform.localScale = referenceTransform.localScale;
            }
        }

        private static void RestoreCurrentTargetBaseline(FbxReplacementStageTwoState state)
        {
            if (state == null || state.BaselineTargetTemplate == null)
            {
                return;
            }

            FbxReplacementStructureTargetEntry targetEntry = GetCurrentTargetEntry(state);
            GameObject currentTarget = GetCurrentTargetObject(state);
            if (targetEntry == null || currentTarget == null)
            {
                return;
            }

            Transform baselineTarget = ResolveTransformByKey(state.BaselineTargetTemplate.transform, targetEntry.Key);
            if (baselineTarget == null)
            {
                return;
            }

            Transform currentTransform = currentTarget.transform;
            currentTarget.name = baselineTarget.name;
            currentTransform.localPosition = baselineTarget.localPosition;
            currentTransform.localRotation = baselineTarget.localRotation;
            currentTransform.localScale = baselineTarget.localScale;
        }

        private static void RestoreCurrentSupplementBaseline(FbxReplacementStageTwoState state)
        {
            if (state == null)
            {
                return;
            }

            FbxReplacementStructureSupplementEntry supplementEntry = GetCurrentSupplementEntry(state);
            if (supplementEntry == null)
            {
                return;
            }

            List<string> decisionKeys = GetSupplementDecisionKeys(state, supplementEntry.Key, true);
            for (int i = 0; i < decisionKeys.Count; i++)
            {
                RestoreSupplementBaseline(ResolveSupplementObjectByKey(state, decisionKeys[i]));
            }
        }
    }
}