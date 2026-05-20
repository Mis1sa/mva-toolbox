using System.Collections.Generic;
using MVA.Toolbox.AnimatorShared.Targeting;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.AnimatorBlendTreeTool
{
    internal sealed class AnimatorBlendTreeToolWindow : EditorWindow
    {
        private Vector2 _contentScrollPosition;
        private Object _targetObject;
        private VRCAvatarDescriptor _avatarDescriptor;
        private Animator _animator;
        private readonly List<AnimatorController> _controllers = new List<AnimatorController>();
        private readonly List<string> _controllerNames = new List<string>();
        private int _selectedControllerIndex;
        private int _selectedLayerIndex;
        private AnimatorBlendTreeEditWindow _editWindow;

        internal static void Open()
        {
            var window = GetWindow<AnimatorBlendTreeToolWindow>("动画控制器 - 混合树");
            window.minSize = new Vector2(550f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            if (_editWindow == null)
            {
                _editWindow = new AnimatorBlendTreeEditWindow(this);
            }
        }

        internal AnimatorController SelectedController
        {
            get
            {
                if (_selectedControllerIndex < 0 || _selectedControllerIndex >= _controllers.Count)
                {
                    return null;
                }

                return _controllers[_selectedControllerIndex];
            }
        }

        internal AnimatorControllerLayer SelectedLayer
        {
            get
            {
                AnimatorController controller = SelectedController;
                if (controller == null || controller.layers == null || controller.layers.Length == 0)
                {
                    return null;
                }

                int index = Mathf.Clamp(_selectedLayerIndex, 0, controller.layers.Length - 1);
                return controller.layers[index];
            }
        }

        internal int SelectedLayerIndex
        {
            get
            {
                return _selectedLayerIndex;
            }
        }

        private void OnGUI()
        {
            if (_editWindow == null)
            {
                OnEnable();
            }

            DrawTargetSelectionSection();

            if (SelectedController == null)
            {
                return;
            }

            _contentScrollPosition = EditorGUILayout.BeginScrollView(_contentScrollPosition);
            _editWindow.OnGUI();
            EditorGUILayout.EndScrollView();
        }

        private void DrawTargetSelectionSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.BeginChangeCheck();
            Object newTarget = EditorGUILayout.ObjectField("目标对象", _targetObject, typeof(Object), true);
            if (EditorGUI.EndChangeCheck())
            {
                SetTarget(newTarget);
            }

            if (_targetObject == null)
            {
                EditorGUILayout.HelpBox("请拖入 Avatar、带 Animator 的物体，或 AnimatorController 资产。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            if (_controllers.Count == 0)
            {
                EditorGUILayout.HelpBox("当前目标中未找到可用的 AnimatorController。", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUI.BeginChangeCheck();
            _selectedControllerIndex = EditorGUILayout.Popup("控制器", _selectedControllerIndex, _controllerNames.ToArray());
            if (EditorGUI.EndChangeCheck())
            {
                _selectedLayerIndex = 0;
                if (_editWindow != null)
                {
                    _editWindow.Reset();
                }
            }

            AnimatorController controller = SelectedController;
            if (controller != null && controller.layers != null && controller.layers.Length > 0)
            {
                string[] layerNames = new string[controller.layers.Length];
                for (int i = 0; i < controller.layers.Length; i++)
                {
                    layerNames[i] = string.IsNullOrEmpty(controller.layers[i].name) ? $"Layer {i}" : controller.layers[i].name;
                }

                EditorGUI.BeginChangeCheck();
                _selectedLayerIndex = EditorGUILayout.Popup("层级", Mathf.Clamp(_selectedLayerIndex, 0, layerNames.Length - 1), layerNames);
                if (EditorGUI.EndChangeCheck() && _editWindow != null)
                {
                    _editWindow.Reset();
                }
            }
            else
            {
                EditorGUILayout.HelpBox("当前控制器没有可用层级。", MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4f);
        }

        private void SetTarget(Object target)
        {
            _targetObject = target;
            RefreshTargetContext();
            RefreshControllers();

            if (_editWindow != null)
            {
                _editWindow.Reset();
            }
        }

        private void RefreshTargetContext()
        {
            _avatarDescriptor = AnimatorTargetResolver.ResolveAvatarDescriptor(_targetObject);
            _animator = AnimatorTargetResolver.ResolveAnimator(_targetObject);
        }

        private void RefreshControllers()
        {
            _controllers.Clear();
            _controllerNames.Clear();
            _selectedControllerIndex = 0;
            _selectedLayerIndex = 0;

            if (_targetObject == null)
            {
                return;
            }

            var controllerAsset = _targetObject as AnimatorController;
            if (controllerAsset != null)
            {
                _controllers.Add(controllerAsset);
                _controllerNames.Add(controllerAsset.name);
                return;
            }

            GameObject root = AnimatorTargetResolver.ResolveControllerScanRoot(_targetObject, _avatarDescriptor, _animator);
            if (root == null)
            {
                return;
            }

            AnimatorControllerCollectionResult collection = AnimatorControllerCollector.CollectControllers(_targetObject, _avatarDescriptor, _animator, root);
            _controllers.AddRange(collection.Controllers);
            _controllerNames.AddRange(collection.ControllerNames);
            _selectedControllerIndex = collection.SuggestedSelectedIndex;
        }
    }
}
