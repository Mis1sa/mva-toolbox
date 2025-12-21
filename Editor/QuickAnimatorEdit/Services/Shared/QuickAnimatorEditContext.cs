using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using MVA.Toolbox.Public;

namespace MVA.Toolbox.QuickAnimatorEdit.Services.Shared
{
    /// <summary>
    /// 共享上下文：统一管理目标对象、控制器列表、选中的控制器与层
    /// 复用 ToolboxUtils 中的方法
    /// </summary>
    public sealed class QuickAnimatorEditContext
    {
        // 目标对象（Avatar / GameObject / AnimatorController 资产）
        private Object _targetObject;

        // 从目标解析出的组件（可空）
        private VRCAvatarDescriptor _avatarDescriptor;
        private Animator _animator;

        // 控制器列表与显示名
        private readonly List<AnimatorController> _controllers = new List<AnimatorController>();
        private readonly List<string> _controllerNames = new List<string>();

        // 选中索引
        private int _selectedControllerIndex;
        private int _selectedLayerIndex;

        #region 属性

        public Object TargetObject => _targetObject;
        public VRCAvatarDescriptor AvatarDescriptor => _avatarDescriptor;
        public Animator Animator => _animator;
        public IReadOnlyList<AnimatorController> Controllers => _controllers;
        public IReadOnlyList<string> ControllerNames => _controllerNames;

        public int SelectedControllerIndex
        {
            get => _selectedControllerIndex;
            set => _selectedControllerIndex = Mathf.Clamp(value, 0, Mathf.Max(0, _controllers.Count - 1));
        }

        public int SelectedLayerIndex
        {
            get => _selectedLayerIndex;
            set
            {
                var controller = SelectedController;
                int maxLayer = controller != null && controller.layers != null ? controller.layers.Length - 1 : 0;
                _selectedLayerIndex = Mathf.Clamp(value, 0, Mathf.Max(0, maxLayer));
            }
        }

        public AnimatorController SelectedController
        {
            get
            {
                if (_selectedControllerIndex < 0 || _selectedControllerIndex >= _controllers.Count)
                    return null;
                return _controllers[_selectedControllerIndex];
            }
        }

        public AnimatorControllerLayer SelectedLayer
        {
            get
            {
                var controller = SelectedController;
                if (controller == null || controller.layers == null || controller.layers.Length == 0)
                    return null;
                int index = Mathf.Clamp(_selectedLayerIndex, 0, controller.layers.Length - 1);
                return controller.layers[index];
            }
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 设置目标对象并刷新控制器列表
        /// </summary>
        public void SetTarget(Object target)
        {
            _targetObject = target;
            RefreshComponents();
            RefreshControllers();
        }

        /// <summary>
        /// 绘制目标选择 UI（供 Window 调用）
        /// </summary>
        public bool DrawTargetSelectionUI()
        {
            EditorGUI.BeginChangeCheck();
            var newTarget = EditorGUILayout.ObjectField(
                "Avatar / Animator / 控制器",
                _targetObject,
                typeof(Object),
                true);

            if (EditorGUI.EndChangeCheck())
            {
                SetTarget(newTarget);
                return true;
            }

            if (_targetObject == null)
            {
                EditorGUILayout.HelpBox("请拖入 VRChat Avatar、带 Animator 组件的物体，或直接拖入动画控制器。", MessageType.Info);
            }
            else if (_controllers.Count == 0)
            {
                EditorGUILayout.HelpBox("在当前目标中未找到任何 AnimatorController。", MessageType.Warning);
            }

            return false;
        }

        /// <summary>
        /// 绘制控制器与层级选择 UI（供 Window 调用）
        /// </summary>
        /// <param name="enableLayerSelection">是否启用层级选择</param>
        public bool DrawControllerAndLayerSelectionUI(bool enableLayerSelection = true)
        {
            if (_controllers.Count == 0)
                return false;

            bool changed = false;

            // 控制器选择
            string[] controllerDisplayNames = new string[_controllerNames.Count];
            for (int i = 0; i < _controllerNames.Count; i++)
            {
                controllerDisplayNames[i] = _controllerNames[i];
            }

            EditorGUI.BeginChangeCheck();
            int newControllerIndex = EditorGUILayout.Popup("控制器", _selectedControllerIndex, controllerDisplayNames);
            if (EditorGUI.EndChangeCheck())
            {
                SelectedControllerIndex = newControllerIndex;
                _selectedLayerIndex = 0;
                changed = true;
            }

            // 层级选择
            var controller = SelectedController;
            if (controller != null && controller.layers != null && controller.layers.Length > 0)
            {
                var layers = controller.layers;
                string[] layerNames = new string[layers.Length];
                for (int i = 0; i < layers.Length; i++)
                {
                    layerNames[i] = string.IsNullOrEmpty(layers[i].name) ? $"Layer {i}" : layers[i].name;
                }

                EditorGUI.BeginDisabledGroup(!enableLayerSelection);
                EditorGUI.BeginChangeCheck();
                int displayLayerIndex = enableLayerSelection ? _selectedLayerIndex : 0;
                int newLayerIndex = EditorGUILayout.Popup("层级", displayLayerIndex, layerNames);
                if (EditorGUI.EndChangeCheck() && enableLayerSelection)
                {
                    SelectedLayerIndex = newLayerIndex;
                    changed = true;
                }
                EditorGUI.EndDisabledGroup();
            }

            return changed;
        }

        #endregion

        #region 私有方法

        private void RefreshComponents()
        {
            _avatarDescriptor = null;
            _animator = null;

            if (_targetObject is GameObject go)
            {
                _avatarDescriptor = ToolboxUtils.GetAvatarDescriptor(go);
                _animator = go.GetComponent<Animator>();
            }
        }

        private void RefreshControllers()
        {
            _controllers.Clear();
            _controllerNames.Clear();
            _selectedControllerIndex = 0;
            _selectedLayerIndex = 0;

            if (_targetObject == null)
                return;

            // 如果目标是 AnimatorController 资产
            if (_targetObject is AnimatorController controllerAsset)
            {
                _controllers.Add(controllerAsset);
                _controllerNames.Add(controllerAsset.name);
                return;
            }

            // 如果目标是 GameObject，使用 ToolboxUtils 收集控制器
            if (_targetObject is GameObject root)
            {
                _controllers.AddRange(ToolboxUtils.CollectControllersFromRoot(root, includeSpecialLayers: true));
                if (_controllers.Count > 0)
                {
                    _controllerNames.AddRange(ToolboxUtils.BuildControllerDisplayNames(_avatarDescriptor, _animator, _controllers));

                    // Avatar 目标时优先选择 FX 控制器
                    if (_avatarDescriptor != null)
                    {
                        var fxController = ToolboxUtils.GetExistingFXController(_avatarDescriptor);
                        if (fxController != null)
                        {
                            int fxIndex = _controllers.FindIndex(c => c == fxController);
                            if (fxIndex >= 0)
                            {
                                _selectedControllerIndex = fxIndex;
                            }
                        }
                    }
                }
            }
        }

        #endregion
    }
}
