using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.FbxReplacement
{
    internal sealed partial class FbxReplacementWindow
    {
        private void DrawInputSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            bool inputsLocked = _sessionState != null;
            if (inputsLocked)
            {
                DrawLockedObjectFields();
            }
            else
            {
                DrawTargetObjectField();
                DrawReferenceObjectField();
            }

            GUILayout.Space(4f);
            bool hasUnsavedOpenScenes = !inputsLocked && FbxReplacementWorkspaceService.HasUnsavedOpenScenes();
            if (!inputsLocked)
            {
                DrawPreAnalysisChecklist(hasUnsavedOpenScenes);
                GUILayout.Space(4f);
            }

            if (inputsLocked)
            {
                if (GUILayout.Button("取消并重置", GUILayout.Height(BottomActionButtonHeight)))
                {
                    if (EditorUtility.DisplayDialog("取消并重置", "将终止并清除所有操作", "确定", "返回"))
                    {
                        ResetWorkflowState();
                    }
                }
            }

            if (!inputsLocked)
            {
                EditorGUI.BeginDisabledGroup(
                    _referenceObject == null
                    || _targetObject == null
                    || hasUnsavedOpenScenes);
                if (GUILayout.Button("分析", GUILayout.Height(BottomActionButtonHeight)))
                {
                    RunAnalysisAndEnterStageOne();
                }

                EditorGUI.EndDisabledGroup();
            }

            if (ShouldShowOverallProgressBar())
            {
                GUILayout.Space(4f);
                DrawOverallProgressBar();
            }

            if (!string.IsNullOrEmpty(_analysisError))
            {
                GUILayout.Space(4f);
                EditorGUILayout.HelpBox(_analysisError, MessageType.Error);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawPreAnalysisChecklist(bool hasUnsavedOpenScenes)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("开始分析前请确认：", EditorStyles.boldLabel);
            DrawChecklistLine("层级中的场景均已保存。", hasUnsavedOpenScenes);
            DrawChecklistLine("替换 FBX 与原 FBX 的层级、命名、骨骼和 Renderer 分布尽量相似。", false);
            DrawChecklistLine("流程进行中不要手动移动、删除、重命名或修改临时场景中的物体。", false);
            DrawChecklistLine("自动匹配仅作为推荐，请逐步确认“目标物体”和“对齐物体”是否正确。", false);
            EditorGUILayout.EndVertical();
        }

        private void DrawChecklistLine(string text, bool isError)
        {
            string labelText = "- " + text;
            if (!isError)
            {
                EditorGUILayout.LabelField(labelText, EditorStyles.wordWrappedLabel);
                return;
            }

            Color previousColor = GUI.color;
            GUI.color = Color.red;
            EditorGUILayout.BeginHorizontal();
            GUIStyle style = EditorStyles.label;
            float labelWidth = style.CalcSize(new GUIContent(labelText)).x;
            GUILayout.Label(labelText, style, GUILayout.Width(labelWidth));
            GUIContent warningIcon = EditorGUIUtility.IconContent("console.warnicon.sml");
            GUILayout.Label(warningIcon, GUILayout.Width(18f), GUILayout.Height(EditorGUIUtility.singleLineHeight));

            EditorGUILayout.EndHorizontal();
            GUI.color = previousColor;
        }

        private void DrawLockedObjectFields()
        {
            GameObject lockedTarget = _sessionState != null && _sessionState.LockedTargetSource != null
                ? _sessionState.LockedTargetSource
                : _targetObject;
            GameObject lockedReference = _sessionState != null && _sessionState.LockedReferenceSource != null
                ? _sessionState.LockedReferenceSource
                : _referenceObject;

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField("目标物体", lockedTarget, typeof(GameObject), true);
            EditorGUILayout.ObjectField("参考物体", lockedReference, typeof(GameObject), true);
            EditorGUI.EndDisabledGroup();
        }

        private void DrawReferenceObjectField()
        {
            var candidate = EditorGUILayout.ObjectField("参考物体", _referenceObject, typeof(GameObject), true) as GameObject;
            if (candidate == _referenceObject)
            {
                return;
            }

            if (!IsAcceptedSelection(candidate, _targetObject))
            {
                return;
            }

            _referenceObject = candidate;
            ResetWorkflowState();
        }

        private void DrawTargetObjectField()
        {
            var candidate = EditorGUILayout.ObjectField("目标物体", _targetObject, typeof(GameObject), true) as GameObject;
            if (candidate == _targetObject)
            {
                return;
            }

            if (!IsAcceptedSelection(candidate, _referenceObject))
            {
                return;
            }

            _targetObject = candidate;
            ResetWorkflowState();
        }

        private static bool IsAcceptedSelection(GameObject candidate, GameObject other)
        {
            if (candidate == null)
            {
                return true;
            }

            if (other == null)
            {
                return true;
            }

            if (candidate == other)
            {
                return false;
            }

            var candidateTransform = candidate.transform;
            var otherTransform = other.transform;
            return !candidateTransform.IsChildOf(otherTransform) && !otherTransform.IsChildOf(candidateTransform);
        }
    }
}