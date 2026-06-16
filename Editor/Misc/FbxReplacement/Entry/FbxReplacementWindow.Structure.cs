using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.FbxReplacement
{
    internal sealed partial class FbxReplacementWindow
    {
        private void DrawStageTwoWorkflow()
        {
            if (_stageThreeState == null
                && _stageTwoState != null
                && FbxReplacementStructureWorkflow.GetCurrentStep(_stageTwoState) == FbxReplacementStageTwoWorkflowStep.Completed)
            {
                EnterStageThree();
                if (_stageThreeState != null)
                {
                    DrawStageThreeWorkflow();
                    return;
                }
            }

            SyncStageHighlights();
            switch (FbxReplacementStructureWorkflow.GetCurrentStep(_stageTwoState))
            {
                case FbxReplacementStageTwoWorkflowStep.StructureAlignment:
                    DrawStructureAlignmentStep();
                    break;

                case FbxReplacementStageTwoWorkflowStep.ReferenceSupplementReview:
                case FbxReplacementStageTwoWorkflowStep.Completed:
                    DrawReferenceSupplementReviewStep();
                    break;

                default:
                    EditorGUILayout.HelpBox("当前阶段2状态无效，请返回上一步或重新开始。", MessageType.Warning);
                    break;
            }
        }

        private void DrawReferenceSupplementReviewStep()
        {
            GameObject currentTarget = FbxReplacementStructureWorkflow.GetCurrentTargetObject(_stageTwoState);
            GameObject currentReference = FbxReplacementStructureWorkflow.GetCurrentSupplementReferenceObject(_stageTwoState);
            bool isAncestorSupplement = FbxReplacementStructureWorkflow.CurrentSupplementIsAncestor(_stageTwoState);
            bool canIncludeChildren = FbxReplacementStructureWorkflow.CurrentSupplementCanIncludeChildren(_stageTwoState);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField("目标物体", currentTarget, typeof(GameObject), true);
            EditorGUILayout.ObjectField("对齐物体", currentReference, typeof(GameObject), true);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndVertical();

            GUILayout.Space(4f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            bool alignTransform = DrawRightAlignedToggle("对齐变换", _stageTwoState.AlignTransform);

            bool includeChildren = _stageTwoState.IncludeChildren;
            Color previousColor = GUI.color;
            if (!canIncludeChildren)
            {
                GUI.color = new Color(previousColor.r, previousColor.g, previousColor.b, previousColor.a * 0.6f);
            }
            includeChildren = DrawRightAlignedToggle("包含子级", includeChildren);
            GUI.color = previousColor;

            bool affectChildren = _stageTwoState.AffectChildren;
            previousColor = GUI.color;
            if (!isAncestorSupplement)
            {
                GUI.color = new Color(previousColor.r, previousColor.g, previousColor.b, previousColor.a * 0.6f);
            }
            affectChildren = DrawRightAlignedToggle("影响子级变换", affectChildren);
            GUI.color = previousColor;

            if (alignTransform != _stageTwoState.AlignTransform
                || includeChildren != _stageTwoState.IncludeChildren
                || affectChildren != _stageTwoState.AffectChildren)
            {
                try
                {
                    FbxReplacementStructureWorkflow.SetSupplementOptions(_stageTwoState, alignTransform, includeChildren, affectChildren);
                    _analysisError = string.Empty;
                    SyncStageHighlights();
                }
                catch (System.Exception exception)
                {
                    _analysisError = exception.Message;
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawStructureAlignmentStep()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GameObject currentTarget = FbxReplacementStructureWorkflow.GetCurrentTargetObject(_stageTwoState);
            GameObject selectedReference = FbxReplacementStructureWorkflow.GetSelectedReferenceObject(_stageTwoState);

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField("目标物体", currentTarget, typeof(GameObject), true);
            EditorGUI.EndDisabledGroup();

            GameObject candidate = EditorGUILayout.ObjectField("对齐物体", selectedReference, typeof(GameObject), true) as GameObject;
            if (candidate != selectedReference)
            {
                try
                {
                    FbxReplacementStructureWorkflow.SetSelectedReferenceCandidate(_stageTwoState, candidate);
                    FbxReplacementStructureWorkflow.RevealCurrentObjects(_stageTwoState);
                    _analysisError = string.Empty;
                    SyncStageHighlights();
                }
                catch (System.Exception exception)
                {
                    _analysisError = exception.Message;
                }
            }

            EditorGUILayout.EndVertical();

            GUILayout.Space(4f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.BeginDisabledGroup(selectedReference == null);
            bool alignName = DrawRightAlignedToggle("对齐名称", _stageTwoState.AlignName);
            bool alignTransform = DrawRightAlignedToggle("对齐变换", _stageTwoState.AlignTransform);
            EditorGUI.EndDisabledGroup();

            if (alignName != _stageTwoState.AlignName || alignTransform != _stageTwoState.AlignTransform)
            {
                try
                {
                    FbxReplacementStructureWorkflow.SetAlignmentOptions(_stageTwoState, alignName, alignTransform);
                    _analysisError = string.Empty;
                    SyncStageHighlights();
                }
                catch (System.Exception exception)
                {
                    _analysisError = exception.Message;
                }
            }
            EditorGUILayout.EndVertical();
        }

        private bool DrawRightAlignedToggle(string label, bool value)
        {
            Rect rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect fieldRect = EditorGUI.PrefixLabel(rowRect, new GUIContent(label));
            Rect toggleRect = new Rect(fieldRect.x, fieldRect.y, 18f, fieldRect.height);
            bool result = EditorGUI.Toggle(toggleRect, value);
            return result;
        }
    }
}