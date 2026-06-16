using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.FbxReplacement
{
    internal sealed partial class FbxReplacementWindow
    {
        private void DrawStageFourWorkflow()
        {
            SyncStageHighlights();
            switch (FbxReplacementComponentWorkflow.GetCurrentStep(_stageFourState))
            {
                case FbxReplacementStageFourWorkflowStep.ComponentReview:
                    DrawStageFourComponentReviewStep();
                    break;

                case FbxReplacementStageFourWorkflowStep.Completed:
                    DrawStageFourCompletedStep();
                    break;

                default:
                    EditorGUILayout.HelpBox("当前阶段4状态无效，请返回上一步或重新开始。", MessageType.Warning);
                    break;
            }
        }

        private void DrawStageFourComponentReviewStep()
        {
            GameObject currentTarget = FbxReplacementComponentWorkflow.GetCurrentTargetObject(_stageFourState);
            GameObject currentReference = FbxReplacementComponentWorkflow.GetCurrentReferenceObject(_stageFourState);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField("目标物体", currentTarget, typeof(GameObject), true);
            EditorGUILayout.ObjectField("对齐物体", currentReference, typeof(GameObject), true);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndVertical();

            GUILayout.Space(4f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("组件名称", FbxReplacementComponentWorkflow.GetCurrentComponentName(_stageFourState));
            DrawStageFourReferenceSlots();
            EditorGUILayout.EndVertical();
        }

        private void DrawStageFourReferenceSlots()
        {
            int slotCount = FbxReplacementComponentWorkflow.GetCurrentReferenceSlotCount(_stageFourState);
            if (slotCount <= 0)
            {
                return;
            }

            GUILayout.Space(4f);
            for (int i = 0; i < slotCount; i++)
            {
                string label = FbxReplacementComponentWorkflow.GetCurrentReferenceSlotLabel(_stageFourState, i);
                System.Type slotType = FbxReplacementComponentWorkflow.GetCurrentReferenceSlotType(_stageFourState, i);
                UnityEngine.Object sourceReference = FbxReplacementComponentWorkflow.GetCurrentReferenceSourceObject(_stageFourState, i);
                UnityEngine.Object selection = FbxReplacementComponentWorkflow.GetCurrentReferenceSelection(_stageFourState, i);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel(label);
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField(sourceReference, slotType, true);
                EditorGUI.EndDisabledGroup();
                UnityEngine.Object updatedSelection = EditorGUILayout.ObjectField(selection, slotType, true);
                EditorGUILayout.EndHorizontal();
                if (ReferenceEquals(updatedSelection, selection))
                {
                    continue;
                }

                try
                {
                    FbxReplacementComponentWorkflow.SetCurrentReferenceSelection(_stageFourState, i, updatedSelection);
                    _analysisError = string.Empty;
                }
                catch (System.Exception exception)
                {
                    _analysisError = exception.Message;
                }
            }
        }

        private void DrawStageFourCompletedStep()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("导出设置", EditorStyles.boldLabel);
            GUILayout.Space(4f);

            if (string.IsNullOrEmpty(_exportPrefabName)
                && _stageFourState != null
                && _stageFourState.StageThreeState?.StageTwoState?.SessionState?.Workspace?.TargetWorkspaceRoot != null)
            {
                _exportPrefabName = _stageFourState.StageThreeState.StageTwoState.SessionState.Workspace.TargetWorkspaceRoot.name;
            }

            EditorGUILayout.BeginHorizontal();
            _exportFolderRelative = EditorGUILayout.TextField("基础保存路径", _exportFolderRelative);
            if (GUILayout.Button("浏览", GUILayout.Width(60f)))
            {
                string absolutePath = EditorUtility.OpenFolderPanel("选择保存文件夹 (请选择 Assets 下文件夹)", Application.dataPath, string.Empty);
                if (!string.IsNullOrEmpty(absolutePath))
                {
                    if (absolutePath.StartsWith(Application.dataPath))
                    {
                        string relativePath = "Assets" + absolutePath.Substring(Application.dataPath.Length);
                        _exportFolderRelative = relativePath.Replace("\\", "/");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("路径错误", "请选择项目中的 Assets 目录下的文件夹以便保存。", "确定");
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            _exportPrefabName = EditorGUILayout.TextField("Prefab 名称", _exportPrefabName);

            GUILayout.Space(8f);
            EditorGUILayout.HelpBox("将会保存在设定路径下的时间戳文件夹内，以防覆盖。", MessageType.Info);

            if (GUILayout.Button("导出 Prefab 并完成", GUILayout.Height(36f)))
            {
                ExportAndCompleteStageFour();
            }

            EditorGUILayout.EndVertical();
        }
    }
}