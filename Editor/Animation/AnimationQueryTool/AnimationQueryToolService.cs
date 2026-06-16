using System;
using System.Collections.Generic;
using MVA.Toolbox.Animation.Shared.Controllers;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.AnimationQueryTool
{
    internal sealed partial class AnimationQueryToolService
    {
        internal sealed class PropertyGroupData
        {
            internal Type ComponentType;
            internal string CanonicalPropertyName;
            internal string GroupDisplayName;
            internal readonly List<string> BoundPropertyNames = new List<string>();
        }

        internal sealed class FoundClipInfo
        {
            internal AnimationClip Clip;
            internal AnimatorController Controller;
        }

        private GameObject _targetRoot;
        private VRCAvatarDescriptor _avatarDescriptor;
        private IReadOnlyList<AnimatorController> _controllers;
        private Dictionary<AnimatorController, ControllerWithRoot> _controllerScopeMap;
        private int _selectedControllerIndex = -1;
        private int _selectedLayerIndex = -1;

        private Object _selectedAnimatedObject;
        // private string _selectedPath;
        private Transform _selectedTransform;

        private readonly List<PropertyGroupData> _availableGroups = new List<PropertyGroupData>();
        private readonly List<FoundClipInfo> _foundClips = new List<FoundClipInfo>();
        private readonly List<string> _currentBlendshapeOptions = new List<string>();

        private bool _searchCompleted;
        private int _selectedGroupIndex;
        private int _selectedBlendshapeOptionIndex;

        internal Object SelectedAnimatedObject => _selectedAnimatedObject;
        internal IReadOnlyList<PropertyGroupData> PropertyGroups => _availableGroups;
        internal IReadOnlyList<FoundClipInfo> FoundClips => _foundClips;
        internal IReadOnlyList<string> CurrentBlendshapeOptions => _currentBlendshapeOptions;
        internal int SelectedGroupIndex => _selectedGroupIndex;
        internal int SelectedBlendshapeOptionIndex => _selectedBlendshapeOptionIndex;
        internal bool SearchCompleted => _searchCompleted;
        internal bool HasAvailableGroups => _availableGroups.Count > 0;
        internal bool HasAnimatedObject => _selectedAnimatedObject != null;
        internal bool SelectedGroupIsBlendshape =>
            _selectedGroupIndex > 0 &&
            _selectedGroupIndex <= _availableGroups.Count &&
            _availableGroups[_selectedGroupIndex - 1].ComponentType == typeof(SkinnedMeshRenderer) &&
            string.Equals(_availableGroups[_selectedGroupIndex - 1].CanonicalPropertyName, "blendShape", StringComparison.Ordinal);

        internal void SyncScope(
            GameObject targetRoot,
            VRCAvatarDescriptor avatarDescriptor,
            IReadOnlyList<AnimatorController> controllers,
            Dictionary<AnimatorController, ControllerWithRoot> controllerScopeMap,
            int selectedControllerIndex,
            int selectedLayerIndex)
        {
            bool targetChanged = _targetRoot != targetRoot;

            _targetRoot = targetRoot;
            _avatarDescriptor = avatarDescriptor;
            _controllers = controllers;
            _controllerScopeMap = controllerScopeMap;

            if (targetChanged)
            {
                _selectedControllerIndex = selectedControllerIndex;
                _selectedLayerIndex = selectedLayerIndex;
                ResetSelection();
                return;
            }

            if (_selectedControllerIndex != selectedControllerIndex || _selectedLayerIndex != selectedLayerIndex)
            {
                _selectedControllerIndex = selectedControllerIndex;
                _selectedLayerIndex = selectedLayerIndex;

                if (_selectedAnimatedObject != null && _controllers != null && _controllers.Count > 0)
                {
                    Object normalized = NormalizeSelectedObjectForScope(_selectedAnimatedObject);
                    if (!Equals(normalized, _selectedAnimatedObject))
                    {
                        SetSelectedAnimatedObject(normalized);
                        return;
                    }

                    ScanAndGroupAnimatedProperties();
                }
            }
        }

        internal bool SetSelectedAnimatedObject(Object newValue)
        {
            Object normalized = NormalizeSelectedObjectForScope(newValue);
            if (Equals(_selectedAnimatedObject, normalized))
            {
                return false;
            }

            _selectedAnimatedObject = normalized;
            _selectedTransform = (_selectedAnimatedObject as Component)?.transform ?? (_selectedAnimatedObject as GameObject)?.transform;
            // _selectedPath = null;
            _availableGroups.Clear();
            _selectedGroupIndex = 0;
            _selectedBlendshapeOptionIndex = 0;
            _searchCompleted = false;
            _foundClips.Clear();
            _currentBlendshapeOptions.Clear();

            if (_selectedAnimatedObject != null && _controllers != null && _controllers.Count > 0)
            {
                ScanAndGroupAnimatedProperties();
            }

            return true;
        }

        internal void ChangeGroupIndex(int newIndex)
        {
            newIndex = Mathf.Clamp(newIndex, 0, _availableGroups.Count);
            if (newIndex == _selectedGroupIndex)
            {
                return;
            }

            _selectedGroupIndex = newIndex;
            _selectedBlendshapeOptionIndex = 0;
            RebuildBlendshapeOptionsForSelectedGroup();
            _searchCompleted = false;
            _foundClips.Clear();
            FindClipsForSelectedGroup();
        }

        internal void ChangeBlendshapeOptionIndex(int newIndex)
        {
            int maxIndex = _currentBlendshapeOptions.Count > 0 ? _currentBlendshapeOptions.Count - 1 : 0;
            newIndex = Mathf.Clamp(newIndex, 0, maxIndex);
            if (newIndex == _selectedBlendshapeOptionIndex)
            {
                return;
            }

            _selectedBlendshapeOptionIndex = newIndex;
            _searchCompleted = false;
            _foundClips.Clear();
            FindClipsForSelectedGroup();
        }

        internal bool CanRefresh()
        {
            return _selectedAnimatedObject != null &&
                   _controllers != null &&
                   _controllers.Count > 0 &&
                   _selectedGroupIndex >= 0 &&
                   _selectedGroupIndex <= _availableGroups.Count;
        }

        internal void RefreshSearch()
        {
            FindClipsForSelectedGroup();
        }

        private AnimatorController SelectedController
        {
            get
            {
                if (_controllers == null || _controllers.Count == 0 || _selectedControllerIndex < 0)
                {
                    return null;
                }

                int index = Mathf.Clamp(_selectedControllerIndex, 0, _controllers.Count - 1);
                return _controllers[index];
            }
        }

        private ControllerWithRoot SelectedControllerScope => GetControllerScope(SelectedController);

        private Transform SelectedControllerRoot => SelectedControllerScope.RootTransform;

        private ControllerWithRoot GetControllerScope(AnimatorController controller)
        {
            if (controller == null)
            {
                return new ControllerWithRoot
                {
                    Controller = null,
                    RootTransform = _targetRoot != null ? _targetRoot.transform : null,
                    IgnoresNestedAnimators = false
                };
            }

            if (_controllerScopeMap != null && _controllerScopeMap.TryGetValue(controller, out ControllerWithRoot scope) && scope.RootTransform != null)
            {
                return scope;
            }

            return new ControllerWithRoot
            {
                Controller = controller,
                RootTransform = _targetRoot != null ? _targetRoot.transform : null,
                IgnoresNestedAnimators = false
            };
        }

        private void ResetSelection()
        {
            _selectedAnimatedObject = null;
            _selectedTransform = null;
            // _selectedPath = null;
            _availableGroups.Clear();
            _selectedGroupIndex = 0;
            _selectedBlendshapeOptionIndex = 0;
            _searchCompleted = false;
            _foundClips.Clear();
            _currentBlendshapeOptions.Clear();
        }
    }
}
