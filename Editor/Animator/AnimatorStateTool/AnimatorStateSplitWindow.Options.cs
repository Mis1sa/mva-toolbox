using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.AnimatorStateTool
{
    internal sealed partial class AnimatorStateSplitWindow
    {
        private void DrawNameAndDefaultOptions(AnimatorControllerLayer layer)
        {
            string originalName = _resolvedState != null ? _resolvedState.name : string.Empty;
            if (string.IsNullOrEmpty(_headName))
            {
                _headName = string.IsNullOrEmpty(originalName) ? "State_Head" : originalName + "_Head";
            }

            if (string.IsNullOrEmpty(_tailName))
            {
                _tailName = string.IsNullOrEmpty(originalName) ? "State_Tail" : originalName + "_Tail";
            }

            EditorGUILayout.LabelField("新状态名称", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("头部状态", GUILayout.Width(80f));
            if (_keepOriginalAsHead && !string.IsNullOrEmpty(originalName))
            {
                _headName = originalName;
            }
            EditorGUI.BeginDisabledGroup(_keepOriginalAsHead);
            _headName = EditorGUILayout.TextField(_headName);
            EditorGUI.EndDisabledGroup();
            _keepOriginalAsHead = EditorGUILayout.ToggleLeft("保留原状态", _keepOriginalAsHead, GUILayout.Width(110f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("尾部状态", GUILayout.Width(80f));
            if (_keepOriginalAsTail && !string.IsNullOrEmpty(originalName))
            {
                _tailName = originalName;
            }
            EditorGUI.BeginDisabledGroup(_keepOriginalAsTail);
            _tailName = EditorGUILayout.TextField(_tailName);
            EditorGUI.EndDisabledGroup();
            _keepOriginalAsTail = EditorGUILayout.ToggleLeft("保留原状态", _keepOriginalAsTail, GUILayout.Width(110f));
            EditorGUILayout.EndHorizontal();

            if (_keepOriginalAsHead && _keepOriginalAsTail)
            {
                EditorGUILayout.HelpBox("原始状态不能同时作为头部和尾部。", MessageType.Error);
            }

            if (_headName == _tailName)
            {
                EditorGUILayout.HelpBox("头部和尾部状态名称不能相同。", MessageType.Error);
            }

            GUILayout.Space(4f);
            _isDefaultState = layer.stateMachine.defaultState == _resolvedState;
            if (_isDefaultState)
            {
                EditorGUILayout.HelpBox("当前状态是默认状态，分割后需要选择新的默认状态。", MessageType.Warning);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("新的默认状态", GUILayout.Width(80f));
                int idx = _defaultDesignation == AnimatorStateSplitService.DefaultStateDesignation.Head ? 0 : 1;
                int newIdx = EditorGUILayout.Popup(idx, new[] { "头部", "尾部" });
                _defaultDesignation = newIdx == 0
                    ? AnimatorStateSplitService.DefaultStateDesignation.Head
                    : AnimatorStateSplitService.DefaultStateDesignation.Tail;
                EditorGUILayout.EndHorizontal();
            }
        }
    }
}
