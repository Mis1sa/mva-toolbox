using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.FbxReplacement
{
    internal sealed partial class FbxReplacementWindow
    {
        private void DrawBottomActionBar()
        {
            if (_referenceObject == null || _targetObject == null || _sessionState == null)
            {
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            bool drewSecondaryActions = false;
            if (_stageFourState != null)
            {
                drewSecondaryActions = DrawStageFourSecondaryActions();
            }
            else if (_stageThreeState != null)
            {
                drewSecondaryActions = DrawStageThreeSecondaryActions();
            }
            else if (_stageTwoState != null)
            {
                drewSecondaryActions = DrawStageTwoSecondaryActions();
            }

            if (drewSecondaryActions)
            {
                GUILayout.Space(4f);
            }

            DrawPrimaryActionRow();
            EditorGUILayout.EndVertical();
        }

        private void DrawPrimaryActionRow()
        {
            if (_stageThreeState != null
                && FbxReplacementMaterialWorkflow.GetCurrentStep(_stageThreeState) == FbxReplacementStageThreeWorkflowStep.SupplementModeSelection)
            {
                DrawTripleActionRow(
                    "上一步",
                    CanStepBack(),
                    StepBackCurrentWorkflow,
                    "逐个调整",
                    true,
                    ChooseSupplementMaterialAdjust,
                    "直接复用",
                    true,
                    ChooseSupplementMaterialReuse);
                return;
            }

            DrawDualActionRow("上一步", CanStepBack(), StepBackCurrentWorkflow, GetPrimaryActionLabel(), GetPrimaryActionEnabled(), GetPrimaryActionHandler());
        }

        private string GetPrimaryActionLabel()
        {
            if (_stageFourState != null)
            {
                return GetStageFourPrimaryActionLabel();
            }

            if (_stageThreeState != null)
            {
                return GetStageThreePrimaryActionLabel();
            }

            if (_stageTwoState != null)
            {
                return GetStageTwoPrimaryActionLabel();
            }

            return string.Empty;
        }

        private bool GetPrimaryActionEnabled()
        {
            if (_stageFourState != null)
            {
                return GetStageFourPrimaryActionEnabled();
            }

            if (_stageThreeState != null)
            {
                return GetStageThreePrimaryActionEnabled();
            }

            if (_stageTwoState != null)
            {
                return GetStageTwoPrimaryActionEnabled();
            }

            return false;
        }

        private System.Action GetPrimaryActionHandler()
        {
            if (_stageFourState != null)
            {
                return GetStageFourPrimaryActionHandler();
            }

            if (_stageThreeState != null)
            {
                return GetStageThreePrimaryActionHandler();
            }

            if (_stageTwoState != null)
            {
                return GetStageTwoPrimaryActionHandler();
            }

            return null;
        }

        private string GetStageTwoPrimaryActionLabel()
        {
            FbxReplacementStageTwoWorkflowStep step = FbxReplacementStructureWorkflow.GetCurrentStep(_stageTwoState);
            switch (step)
            {
                case FbxReplacementStageTwoWorkflowStep.StructureAlignment:
                    return "确定并下一步";

                case FbxReplacementStageTwoWorkflowStep.ReferenceSupplementReview:
                    return "保留";

                case FbxReplacementStageTwoWorkflowStep.Completed:
                    return "进入阶段3";

                default:
                    return "已完成";
            }
        }

        private bool GetStageTwoPrimaryActionEnabled()
        {
            FbxReplacementStageTwoWorkflowStep step = FbxReplacementStructureWorkflow.GetCurrentStep(_stageTwoState);
            switch (step)
            {
                case FbxReplacementStageTwoWorkflowStep.StructureAlignment:
                    return FbxReplacementStructureWorkflow.GetSelectedReferenceObject(_stageTwoState) != null;

                case FbxReplacementStageTwoWorkflowStep.ReferenceSupplementReview:
                case FbxReplacementStageTwoWorkflowStep.Completed:
                    return true;

                default:
                    return false;
            }
        }

        private System.Action GetStageTwoPrimaryActionHandler()
        {
            FbxReplacementStageTwoWorkflowStep step = FbxReplacementStructureWorkflow.GetCurrentStep(_stageTwoState);
            switch (step)
            {
                case FbxReplacementStageTwoWorkflowStep.StructureAlignment:
                    return ConfirmStructureAlignmentMatch;

                case FbxReplacementStageTwoWorkflowStep.ReferenceSupplementReview:
                    return KeepStructureSupplement;

                case FbxReplacementStageTwoWorkflowStep.Completed:
                    return EnterStageThree;

                default:
                    return null;
            }
        }

        private string GetStageThreePrimaryActionLabel()
        {
            FbxReplacementStageThreeWorkflowStep step = FbxReplacementMaterialWorkflow.GetCurrentStep(_stageThreeState);
            switch (step)
            {
                case FbxReplacementStageThreeWorkflowStep.TargetMaterialReview:
                case FbxReplacementStageThreeWorkflowStep.SupplementMaterialReview:
                    return "确认并下一步";

                case FbxReplacementStageThreeWorkflowStep.Completed:
                    return "进入阶段4";

                default:
                    return "已完成";
            }
        }

        private bool GetStageThreePrimaryActionEnabled()
        {
            FbxReplacementStageThreeWorkflowStep step = FbxReplacementMaterialWorkflow.GetCurrentStep(_stageThreeState);
            switch (step)
            {
                case FbxReplacementStageThreeWorkflowStep.TargetMaterialReview:
                case FbxReplacementStageThreeWorkflowStep.SupplementMaterialReview:
                case FbxReplacementStageThreeWorkflowStep.Completed:
                    return true;

                default:
                    return false;
            }
        }

        private System.Action GetStageThreePrimaryActionHandler()
        {
            FbxReplacementStageThreeWorkflowStep step = FbxReplacementMaterialWorkflow.GetCurrentStep(_stageThreeState);
            switch (step)
            {
                case FbxReplacementStageThreeWorkflowStep.TargetMaterialReview:
                    return ConfirmTargetMaterials;

                case FbxReplacementStageThreeWorkflowStep.SupplementMaterialReview:
                    return ConfirmSupplementMaterials;

                case FbxReplacementStageThreeWorkflowStep.Completed:
                    return EnterStageFour;

                default:
                    return null;
            }
        }

        private string GetStageFourPrimaryActionLabel()
        {
            FbxReplacementStageFourWorkflowStep step = FbxReplacementComponentWorkflow.GetCurrentStep(_stageFourState);
            switch (step)
            {
                case FbxReplacementStageFourWorkflowStep.ComponentReview:
                    return "确定并下一步";

                default:
                    return "已完成 (请在上方导出)";
            }
        }

        private bool GetStageFourPrimaryActionEnabled()
        {
            FbxReplacementStageFourWorkflowStep step = FbxReplacementComponentWorkflow.GetCurrentStep(_stageFourState);
            switch (step)
            {
                case FbxReplacementStageFourWorkflowStep.ComponentReview:
                    return true;

                default:
                    return false;
            }
        }

        private System.Action GetStageFourPrimaryActionHandler()
        {
            FbxReplacementStageFourWorkflowStep step = FbxReplacementComponentWorkflow.GetCurrentStep(_stageFourState);
            switch (step)
            {
                case FbxReplacementStageFourWorkflowStep.ComponentReview:
                    return ConfirmStageFourComponent;

                default:
                    return null;
            }
        }

        private bool DrawStageTwoSecondaryActions()
        {
            if (_stageTwoState == null)
            {
                return false;
            }

            switch (FbxReplacementStructureWorkflow.GetCurrentStep(_stageTwoState))
            {
                case FbxReplacementStageTwoWorkflowStep.StructureAlignment:
                    DrawDualActionRow("移除", RemoveStructureTarget, "保留", KeepStructureTarget);
                    return true;

                case FbxReplacementStageTwoWorkflowStep.ReferenceSupplementReview:
                    DrawSingleActionRow("移除", RemoveStructureSupplement);
                    return true;

                default:
                    return false;
            }
        }

        private bool DrawStageThreeSecondaryActions()
        {
            if (_stageThreeState == null)
            {
                return false;
            }

            switch (FbxReplacementMaterialWorkflow.GetCurrentStep(_stageThreeState))
            {
                case FbxReplacementStageThreeWorkflowStep.TargetMaterialReview:
                    DrawSingleActionRow("不替换", SkipTargetMaterials);
                    return true;

                default:
                    return false;
            }
        }

        private bool DrawStageFourSecondaryActions()
        {
            if (_stageFourState == null)
            {
                return false;
            }

            switch (FbxReplacementComponentWorkflow.GetCurrentStep(_stageFourState))
            {
                case FbxReplacementStageFourWorkflowStep.ComponentReview:
                    DrawSingleActionRow("仅复制", KeepStageFourComponent);
                    return true;

                default:
                    return false;
            }
        }

        private void DrawSingleActionRow(string label, System.Action onClick)
        {
            Rect rowRect = EditorGUILayout.GetControlRect(false, BottomActionButtonHeight);
            DrawActionButton(rowRect, label, true, onClick);
        }

        private void DrawDualActionRow(string leftLabel, System.Action onLeftClick, string rightLabel, System.Action onRightClick)
        {
            DrawDualActionRow(leftLabel, true, onLeftClick, rightLabel, true, onRightClick);
        }

        private void DrawDualActionRow(string leftLabel, bool leftEnabled, System.Action onLeftClick, string rightLabel, bool rightEnabled, System.Action onRightClick)
        {
            Rect rowRect = EditorGUILayout.GetControlRect(false, BottomActionButtonHeight);
            float halfWidth = (rowRect.width - BottomActionButtonSpacing) * 0.5f;
            Rect leftRect = new Rect(rowRect.x, rowRect.y, halfWidth, rowRect.height);
            Rect rightRect = new Rect(rowRect.x + halfWidth + BottomActionButtonSpacing, rowRect.y, halfWidth, rowRect.height);
            DrawActionButton(leftRect, leftLabel, leftEnabled, onLeftClick);
            DrawActionButton(rightRect, rightLabel, rightEnabled, onRightClick);
        }

        private void DrawTripleActionRow(
            string firstLabel,
            bool firstEnabled,
            System.Action onFirstClick,
            string secondLabel,
            bool secondEnabled,
            System.Action onSecondClick,
            string thirdLabel,
            bool thirdEnabled,
            System.Action onThirdClick)
        {
            Rect rowRect = EditorGUILayout.GetControlRect(false, BottomActionButtonHeight);
            float thirdWidth = (rowRect.width - BottomActionButtonSpacing * 2f) / 3f;
            Rect firstRect = new Rect(rowRect.x, rowRect.y, thirdWidth, rowRect.height);
            Rect secondRect = new Rect(rowRect.x + thirdWidth + BottomActionButtonSpacing, rowRect.y, thirdWidth, rowRect.height);
            Rect thirdRect = new Rect(rowRect.x + (thirdWidth + BottomActionButtonSpacing) * 2f, rowRect.y, thirdWidth, rowRect.height);
            DrawActionButton(firstRect, firstLabel, firstEnabled, onFirstClick);
            DrawActionButton(secondRect, secondLabel, secondEnabled, onSecondClick);
            DrawActionButton(thirdRect, thirdLabel, thirdEnabled, onThirdClick);
        }

        private bool DrawActionButton(Rect rect, string label, bool enabled, System.Action onClick = null)
        {
            EditorGUI.BeginDisabledGroup(!enabled);
            bool clicked = GUI.Button(rect, label);
            EditorGUI.EndDisabledGroup();
            if (clicked && enabled && onClick != null)
            {
                onClick();
                return true;
            }

            return false;
        }

        private bool CanStepBack()
        {
            if (_stageFourState != null || _stageThreeState != null)
            {
                return true;
            }

            return _stageTwoState != null && _stageTwoState.History.Count > 0;
        }

        private void StepBackCurrentWorkflow()
        {
            if (_stageFourState != null)
            {
                StepBackStageFour();
                return;
            }

            if (_stageThreeState != null)
            {
                StepBackStageThree();
                return;
            }

            if (_stageTwoState != null)
            {
                StepBackStructureAlignment();
            }
        }
    }
}