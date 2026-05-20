using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.AnimatorStateTool
{
    internal sealed partial class AnimatorStateSplitWindow
    {
        private readonly AnimatorStateToolWindow _owner;

        private string _selectedStatePath = string.Empty;
        private int _selectedStateIndex;
        private AnimatorState _resolvedState;
        private string _headName = string.Empty;
        private string _tailName = string.Empty;
        private bool _keepOriginalAsHead;
        private bool _keepOriginalAsTail;
        private bool _manualAdjust;
        private bool _isDefaultState;
        private AnimatorStateSplitService.DefaultStateDesignation _defaultDesignation = AnimatorStateSplitService.DefaultStateDesignation.Head;
        private readonly List<AnimatorStateSplitService.TransitionAdjustment> _incomingAdjustments = new List<AnimatorStateSplitService.TransitionAdjustment>();
        private readonly List<AnimatorStateSplitService.TransitionAdjustment> _anyAdjustments = new List<AnimatorStateSplitService.TransitionAdjustment>();
        private readonly List<AnimatorStateSplitService.TransitionAdjustment> _outgoingAdjustments = new List<AnimatorStateSplitService.TransitionAdjustment>();
        private Vector2 _scrollIncoming;
        private Vector2 _scrollOutgoing;

        internal AnimatorStateSplitWindow(AnimatorStateToolWindow owner)
        {
            _owner = owner;
        }

        internal void Reset()
        {
            _selectedStatePath = string.Empty;
            _selectedStateIndex = 0;
            _resolvedState = null;
            _headName = string.Empty;
            _tailName = string.Empty;
            _keepOriginalAsHead = false;
            _keepOriginalAsTail = false;
            _manualAdjust = false;
            _isDefaultState = false;
            _incomingAdjustments.Clear();
            _anyAdjustments.Clear();
            _outgoingAdjustments.Clear();
        }

        internal void OnGUI()
        {
            var layer = _owner.SelectedLayer;
            if (layer == null || layer.stateMachine == null)
            {
                return;
            }

            EnsureResolvedStateValid();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("分割状态", EditorStyles.boldLabel);
            DrawStateSelection();
            GUILayout.Space(4f);

            if (_resolvedState != null)
            {
                DrawNameAndDefaultOptions(layer);
                GUILayout.Space(4f);
                DrawAdjustments(layer);
                GUILayout.Space(4f);
                DrawApplyButton();
            }

            EditorGUILayout.EndVertical();
        }
    }
}
