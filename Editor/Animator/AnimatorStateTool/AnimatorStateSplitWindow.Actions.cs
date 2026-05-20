using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.AnimatorStateTool
{
    internal sealed partial class AnimatorStateSplitWindow
    {
        private void DrawApplyButton()
        {
            bool invalid = _resolvedState == null || string.IsNullOrEmpty(_headName) || string.IsNullOrEmpty(_tailName) ||
                           _headName == _tailName || (_keepOriginalAsHead && _keepOriginalAsTail);

            EditorGUI.BeginDisabledGroup(invalid);
            if (GUILayout.Button("执行状态分割", GUILayout.Height(28f)))
            {
                bool confirm = EditorUtility.DisplayDialog(
                    "执行状态分割",
                    $"将状态 '{_resolvedState.name}' 分割为:\n\n头部: {_headName}\n尾部: {_tailName}",
                    "分割",
                    "取消");
                if (confirm)
                {
                    List<AnimatorStateSplitService.TransitionAdjustment> headAdjustments = new List<AnimatorStateSplitService.TransitionAdjustment>(_incomingAdjustments.Count + _anyAdjustments.Count);
                    headAdjustments.AddRange(_incomingAdjustments);
                    headAdjustments.AddRange(_anyAdjustments);

                    AnimatorStateSplitService.Execute(
                        _owner.SelectedController,
                        _owner.SelectedLayerIndex,
                        _resolvedState,
                        _headName,
                        _tailName,
                        _keepOriginalAsHead,
                        _keepOriginalAsTail,
                        _isDefaultState,
                        _defaultDesignation,
                        headAdjustments,
                        _outgoingAdjustments);

                    Reset();
                    _owner.RefreshStateListForCurrentSelection();
                }
            }
            EditorGUI.EndDisabledGroup();
        }
    }
}
