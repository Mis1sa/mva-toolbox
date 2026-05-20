using UnityEditor;
using UnityEditor.Animations;

namespace MVA.Toolbox.AnimatorStateTool
{
    internal sealed partial class AnimatorStateSplitWindow
    {
        private void EnsureResolvedStateValid()
        {
            if (string.IsNullOrEmpty(_selectedStatePath))
            {
                return;
            }

            if (_owner.ContainsDisplayPath(_selectedStatePath))
            {
                _selectedStateIndex = _owner.IndexOfDisplayPath(_selectedStatePath);
                _resolvedState = _owner.ResolveState(_selectedStatePath);
            }
            else
            {
                _selectedStatePath = string.Empty;
                _selectedStateIndex = 0;
                _resolvedState = null;
            }
        }

        private void DrawStateSelection()
        {
            if (_owner.DisplayPathCount == 0)
            {
                EditorGUILayout.HelpBox("当前层中没有可用的状态。", MessageType.Warning);
                return;
            }

            if (string.IsNullOrEmpty(_selectedStatePath) || !_owner.ContainsDisplayPath(_selectedStatePath))
            {
                _selectedStatePath = _owner.GetDisplayPathAt(0);
                _selectedStateIndex = 0;
            }

            _selectedStateIndex = _owner.IndexOfDisplayPath(_selectedStatePath);
            if (_selectedStateIndex < 0)
            {
                _selectedStateIndex = 0;
            }

            int oldIndex = _selectedStateIndex;
            int newIndex = EditorGUILayout.Popup("目标状态", _selectedStateIndex, _owner.GetDisplayPathOptions());
            if (newIndex != _selectedStateIndex)
            {
                _selectedStateIndex = newIndex;
                if (_selectedStateIndex >= 0 && _selectedStateIndex < _owner.DisplayPathCount)
                {
                    _selectedStatePath = _owner.GetDisplayPathAt(_selectedStateIndex);
                }
            }

            _resolvedState = _owner.ResolveState(_selectedStatePath);
            if (_selectedStateIndex != oldIndex)
            {
                _headName = string.Empty;
                _tailName = string.Empty;
                _keepOriginalAsHead = false;
                _keepOriginalAsTail = false;
                _isDefaultState = false;
                _defaultDesignation = AnimatorStateSplitService.DefaultStateDesignation.Head;
                _incomingAdjustments.Clear();
                _anyAdjustments.Clear();
                _outgoingAdjustments.Clear();
            }
        }
    }
}
