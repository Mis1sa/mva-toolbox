using UnityEditor;

namespace MVA.Toolbox.AnimatorStateTool
{
    internal sealed partial class AnimatorStateMergeWindow
    {
        private void EnsureResolvedStatesValid()
        {
            if (!string.IsNullOrEmpty(_stateAPath))
            {
                if (_owner.ContainsDisplayPath(_stateAPath))
                {
                    _stateAIndex = _owner.IndexOfDisplayPath(_stateAPath);
                    _resolvedStateA = _owner.ResolveState(_stateAPath);
                }
                else
                {
                    _stateAPath = string.Empty;
                    _stateAIndex = 0;
                    _resolvedStateA = null;
                }
            }

            if (!string.IsNullOrEmpty(_stateBPath))
            {
                if (_owner.ContainsDisplayPath(_stateBPath))
                {
                    _stateBIndex = _owner.IndexOfDisplayPath(_stateBPath);
                    _resolvedStateB = _owner.ResolveState(_stateBPath);
                }
                else
                {
                    _stateBPath = string.Empty;
                    _stateBIndex = 0;
                    _resolvedStateB = null;
                }
            }
        }

        private void DrawStateSelection()
        {
            if (!_owner.ContainsDisplayPath(_stateAPath))
            {
                _stateAPath = _owner.GetDisplayPathAt(0);
                _stateAIndex = 0;
            }

            if (!_owner.ContainsDisplayPath(_stateBPath) || _stateBPath == _stateAPath)
            {
                _stateBPath = string.Empty;
                for (int i = 0; i < _owner.DisplayPathCount; i++)
                {
                    string path = _owner.GetDisplayPathAt(i);
                    if (path != _stateAPath)
                    {
                        _stateBPath = path;
                        break;
                    }
                }
            }

            _stateAIndex = _owner.IndexOfDisplayPath(_stateAPath);
            if (_stateAIndex < 0)
            {
                _stateAIndex = 0;
            }

            int newIndexA = EditorGUILayout.Popup("状态 A", _stateAIndex, _owner.GetDisplayPathOptions());
            if (newIndexA != _stateAIndex)
            {
                _stateAIndex = newIndexA;
                if (_stateAIndex >= 0 && _stateAIndex < _owner.DisplayPathCount)
                {
                    _stateAPath = _owner.GetDisplayPathAt(_stateAIndex);
                }
            }
            _resolvedStateA = _owner.ResolveState(_stateAPath);

            _stateBIndex = _owner.IndexOfDisplayPath(_stateBPath);
            if (_stateBIndex < 0)
            {
                _stateBIndex = 0;
            }

            int newIndexB = EditorGUILayout.Popup("状态 B", _stateBIndex, _owner.GetDisplayPathOptions());
            if (newIndexB != _stateBIndex)
            {
                _stateBIndex = newIndexB;
                if (_stateBIndex >= 0 && _stateBIndex < _owner.DisplayPathCount)
                {
                    _stateBPath = _owner.GetDisplayPathAt(_stateBIndex);
                }
            }
            _resolvedStateB = _owner.ResolveState(_stateBPath);
        }
    }
}
