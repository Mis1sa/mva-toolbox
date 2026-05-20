using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.AnimatorStateTool
{
    internal sealed partial class AnimatorStateMergeWindow
    {
        private void DrawMergeActions()
        {
            GUILayout.Space(4f);
            int currentKeepIndex = _keepA ? 0 : 1;
            int newKeepIndex = EditorGUILayout.Popup("保留状态", currentKeepIndex, new[] { "状态 A", "状态 B" });
            if (newKeepIndex != currentKeepIndex)
            {
                _keepA = newKeepIndex == 0;
                _newStateName = string.Empty;
            }

            GUILayout.Space(4f);
            var keepState = _keepA ? _resolvedStateA : _resolvedStateB;
            string keepStateName = keepState != null ? keepState.name : string.Empty;
            if (keepState != null && (string.IsNullOrEmpty(_newStateName) || _newStateName == _lastKeepStateName))
            {
                _newStateName = keepStateName;
            }

            _newStateName = EditorGUILayout.TextField("新状态名称", _newStateName);
            _lastKeepStateName = keepStateName;
            GUILayout.Space(4f);

            bool invalid = string.IsNullOrEmpty(_newStateName);
            EditorGUI.BeginDisabledGroup(invalid);
            if (GUILayout.Button("执行状态合并", GUILayout.Height(28f)))
            {
                var stateToKeep = _keepA ? _resolvedStateA : _resolvedStateB;
                var stateToRemove = _keepA ? _resolvedStateB : _resolvedStateA;
                bool confirm = EditorUtility.DisplayDialog(
                    "执行状态合并",
                    $"将状态 '{stateToRemove.name}' 合并到 '{stateToKeep.name}' 中，\n\n新状态名称：{_newStateName}",
                    "合并",
                    "取消");
                if (confirm)
                {
                    AnimatorStateMergeService.Execute(
                        _owner.SelectedController,
                        _owner.SelectedLayerIndex,
                        stateToKeep,
                        stateToRemove,
                        _newStateName);

                    Reset();
                    _owner.RefreshStateListForCurrentSelection();
                }
            }
            EditorGUI.EndDisabledGroup();
        }
    }
}
