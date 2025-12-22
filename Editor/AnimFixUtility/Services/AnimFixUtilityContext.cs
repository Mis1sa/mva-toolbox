using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using MVA.Toolbox.Public;

namespace MVA.Toolbox.AnimFixUtility.Services
{
    /// <summary>
    /// AnimFix Utility 共享上下文：负责管理 Avatar / Animator 目标与控制器列表。
    /// </summary>
    public class AnimFixUtilityContext
    {
        private GameObject _targetRoot;
        private VRCAvatarDescriptor _avatarDescriptor;
        private Animator _animator;

        private readonly List<AnimatorController> _controllers = new List<AnimatorController>();
        private readonly List<string> _controllerNames = new List<string>();
        private int _selectedControllerIndex;
        private int _selectedLayerIndex = -1;

        public GameObject TargetRoot => _targetRoot;
        public VRCAvatarDescriptor AvatarDescriptor => _avatarDescriptor;
        public Animator Animator => _animator;

        public IReadOnlyList<AnimatorController> Controllers => _controllers;
        public IReadOnlyList<string> ControllerNames => _controllerNames;

        public AnimatorController SelectedController
        {
            get
            {
                if (_controllers.Count == 0 || _selectedControllerIndex < 0) return null;
                int index = Mathf.Clamp(_selectedControllerIndex, 0, _controllers.Count - 1);
                return _controllers[index];
            }
        }

        public int SelectedControllerIndex
        {
            get
            {
                if (_controllers.Count == 0) return 0;
                return Mathf.Clamp(_selectedControllerIndex, -1, Mathf.Max(0, _controllers.Count - 1));
            }
            set
            {
                int min = _controllers.Count > 0 ? -1 : 0;
                int max = Mathf.Max(0, _controllers.Count - 1);
                int clamped = Mathf.Clamp(value, min, max);
                if (_controllers.Count == 0)
                {
                    clamped = 0;
                }
                if (clamped != _selectedControllerIndex)
                {
                    _selectedControllerIndex = clamped;
                    _selectedLayerIndex = -1;
                }
            }
        }

        public bool HasValidTarget => _targetRoot != null;

        public int SelectedLayerIndex
        {
            get => _selectedLayerIndex;
            set => _selectedLayerIndex = ClampLayerIndex(value);
        }

        public AnimatorControllerLayer SelectedLayer
        {
            get
            {
                var controller = SelectedController;
                if (controller == null || controller.layers == null || controller.layers.Length == 0)
                    return null;

                if (_selectedLayerIndex < 0)
                    return null;

                int index = Mathf.Clamp(_selectedLayerIndex, 0, controller.layers.Length - 1);
                return controller.layers[index];
            }
        }

        public bool TrySetTarget(GameObject newTarget)
        {
            if (newTarget == null)
            {
                ClearTarget();
                return true;
            }

            if (!ToolboxUtils.IsAvatarRoot(newTarget) && !ToolboxUtils.HasAnimator(newTarget))
            {
                return false;
            }

            _targetRoot = newTarget;
            RefreshTargetComponents();
            RefreshControllers();
            return true;
        }

        public void RefreshTargetComponents()
        {
            _avatarDescriptor = null;
            _animator = null;

            if (_targetRoot == null) return;

            _avatarDescriptor = _targetRoot.GetComponent<VRCAvatarDescriptor>();
            _animator = _targetRoot.GetComponent<Animator>();
        }

        public void RefreshControllers()
        {
            _controllers.Clear();
            _controllerNames.Clear();
            _selectedControllerIndex = 0;
            _selectedLayerIndex = -1;

            if (_targetRoot == null) return;

            _controllers.AddRange(ToolboxUtils.CollectControllersFromRoot(_targetRoot, includeSpecialLayers: true));
            if (_controllers.Count == 0) return;

            _controllerNames.AddRange(ToolboxUtils.BuildControllerDisplayNames(_avatarDescriptor, _animator, _controllers));

            // Avatar 默认优先选择 FX 控制器
            if (_avatarDescriptor != null)
            {
                var fxController = ToolboxUtils.GetExistingFXController(_avatarDescriptor);
                if (fxController != null)
                {
                    int index = _controllers.IndexOf(fxController);
                    if (index >= 0)
                    {
                        _selectedControllerIndex = index;
                        _selectedLayerIndex = -1;
                    }
                }
            }
        }

        public string[] BuildControllerNameArray()
        {
            if (_controllerNames.Count == _controllers.Count && _controllerNames.Count > 0)
            {
                return _controllerNames.ToArray();
            }

            var names = new string[_controllers.Count];
            for (int i = 0; i < _controllers.Count; i++)
            {
                names[i] = _controllers[i] != null ? _controllers[i].name : "(Controller)";
            }

            return names;
        }

        public void ClearTarget()
        {
            _targetRoot = null;
            _avatarDescriptor = null;
            _animator = null;
            _controllers.Clear();
            _controllerNames.Clear();
            _selectedControllerIndex = 0;
            _selectedLayerIndex = -1;
        }

        private int ClampLayerIndex(int value)
        {
            var controller = SelectedController;
            if (controller == null || controller.layers == null || controller.layers.Length == 0)
                return -1;

            if (value < -1)
                return -1;

            if (value >= controller.layers.Length)
                return controller.layers.Length - 1;

            return value;
        }
    }
}
