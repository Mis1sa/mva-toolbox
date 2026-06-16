using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.FbxReplacement
{
    internal sealed partial class FbxReplacementWindow
    {
        private void DrawStageThreeWorkflow()
        {
            if (_stageFourState == null
                && _stageThreeState != null
                && FbxReplacementMaterialWorkflow.GetCurrentStep(_stageThreeState) == FbxReplacementStageThreeWorkflowStep.Completed)
            {
                EnterStageFour();
                if (_stageFourState != null)
                {
                    DrawStageFourWorkflow();
                    return;
                }
            }

            SyncStageHighlights();
            switch (FbxReplacementMaterialWorkflow.GetCurrentStep(_stageThreeState))
            {
                case FbxReplacementStageThreeWorkflowStep.TargetMaterialReview:
                    DrawTargetMaterialReviewStep();
                    break;

                case FbxReplacementStageThreeWorkflowStep.SupplementModeSelection:
                    DrawSupplementMaterialModeSelectionStep();
                    break;

                case FbxReplacementStageThreeWorkflowStep.SupplementMaterialReview:
                    DrawSupplementMaterialReviewStep();
                    break;

                default:
                    EditorGUILayout.HelpBox("当前阶段3状态无效，请返回上一步或重新开始。", MessageType.Warning);
                    break;
            }
        }

        private void DrawTargetMaterialReviewStep()
        {
            GameObject currentTarget = FbxReplacementMaterialWorkflow.GetCurrentTargetObject(_stageThreeState);
            GameObject currentReference = FbxReplacementMaterialWorkflow.GetCurrentReferenceObject(_stageThreeState);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField("目标物体", currentTarget, typeof(GameObject), true);
            EditorGUILayout.ObjectField("对齐物体", currentReference, typeof(GameObject), true);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndVertical();

            GUILayout.Space(4f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawCurrentMaterialSlots();
            EditorGUILayout.EndVertical();
        }

        private void DrawSupplementMaterialModeSelectionStep()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GameObject[] pendingSupplementObjects = FbxReplacementMaterialWorkflow.GetPendingSupplementTargetObjects(_stageThreeState);

            EditorGUI.BeginDisabledGroup(true);
            for (int i = 0; i < pendingSupplementObjects.Length; i++)
            {
                EditorGUILayout.ObjectField(GUIContent.none, pendingSupplementObjects[i], typeof(GameObject), true);
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndVertical();
        }

        private void DrawSupplementMaterialReviewStep()
        {
            GameObject currentTarget = FbxReplacementMaterialWorkflow.GetCurrentTargetObject(_stageThreeState);
            GameObject currentReference = FbxReplacementMaterialWorkflow.GetCurrentReferenceObject(_stageThreeState);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField("目标物体", currentTarget, typeof(GameObject), true);
            EditorGUILayout.ObjectField("对齐物体", currentReference, typeof(GameObject), true);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndVertical();

            GUILayout.Space(4f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawCurrentMaterialSlots();
            EditorGUILayout.EndVertical();
        }

        private void DrawCurrentMaterialSlots()
        {
            Material[] originalMaterials = FbxReplacementMaterialWorkflow.GetCurrentOriginalMaterials(_stageThreeState);
            Material[] selections = FbxReplacementMaterialWorkflow.GetCurrentMaterialSelections(_stageThreeState);
            for (int i = 0; i < selections.Length; i++)
            {
                Material originalMaterial = i < originalMaterials.Length ? originalMaterials[i] : null;
                Material currentMaterial = selections[i];

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"元素[{i}]", GUILayout.Width(48f));
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField(
                    GUIContent.none,
                    originalMaterial,
                    typeof(Material),
                    false,
                    GUILayout.ExpandWidth(true));
                EditorGUI.EndDisabledGroup();

                Material updatedMaterial = EditorGUILayout.ObjectField(
                    GUIContent.none,
                    currentMaterial,
                    typeof(Material),
                    false,
                    GUILayout.ExpandWidth(true)) as Material;
                EditorGUILayout.EndHorizontal();

                if (updatedMaterial == currentMaterial)
                {
                    continue;
                }

                try
                {
                    FbxReplacementMaterialWorkflow.SetCurrentMaterialSelection(_stageThreeState, i, updatedMaterial);
                    _analysisError = string.Empty;
                    SyncStageHighlights();
                }
                catch (System.Exception exception)
                {
                    _analysisError = exception.Message;
                }
            }
        }
    }
}