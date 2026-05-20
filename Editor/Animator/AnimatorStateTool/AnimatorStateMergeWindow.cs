using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.AnimatorStateTool
{
    internal sealed partial class AnimatorStateMergeWindow
    {
        private readonly AnimatorStateToolWindow _owner;

        private string _stateAPath = string.Empty;
        private int _stateAIndex;
        private AnimatorState _resolvedStateA;
        private string _stateBPath = string.Empty;
        private int _stateBIndex;
        private AnimatorState _resolvedStateB;
        private string _newStateName = string.Empty;
        private string _lastKeepStateName = string.Empty;
        private bool _keepA = true;

        internal AnimatorStateMergeWindow(AnimatorStateToolWindow owner)
        {
            _owner = owner;
        }

        internal void Reset()
        {
            _stateAPath = string.Empty;
            _stateAIndex = 0;
            _resolvedStateA = null;
            _stateBPath = string.Empty;
            _stateBIndex = 0;
            _resolvedStateB = null;
            _newStateName = string.Empty;
            _lastKeepStateName = string.Empty;
            _keepA = true;
        }

        internal void OnGUI()
        {
            var layer = _owner.SelectedLayer;
            if (layer == null || layer.stateMachine == null)
            {
                return;
            }

            EnsureResolvedStatesValid();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("合并状态", EditorStyles.boldLabel);
            if (_owner.DisplayPathCount < 2)
            {
                EditorGUILayout.HelpBox("当前层中至少需要两个状态才能执行合并。", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            DrawStateSelection();

            if (_resolvedStateA == null || _resolvedStateB == null || _resolvedStateA == _resolvedStateB)
            {
                EditorGUILayout.HelpBox("请选择两个不同的状态。", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            DrawMergeActions();
            EditorGUILayout.EndVertical();
        }
    }
}
