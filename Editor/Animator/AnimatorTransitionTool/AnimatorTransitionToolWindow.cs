using System;
using System.Collections.Generic;
using MVA.Toolbox.AnimatorShared.Paths;
using MVA.Toolbox.AnimatorShared.Targeting;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.AnimatorTransitionTool
{
    internal sealed class AnimatorTransitionToolWindow : EditorWindow
    {
        private enum TransitionMode
        {
            Create,
            Modify
        }

        private TransitionMode _mode = TransitionMode.Create;
        private Vector2 _contentScrollPosition;
        private Object _targetObject;
        private VRCAvatarDescriptor _avatarDescriptor;
        private Animator _animator;
        private readonly List<AnimatorController> _controllers = new List<AnimatorController>();
        private readonly List<string> _controllerNames = new List<string>();
        private int _selectedControllerIndex;
        private int _selectedLayerIndex;
        private AnimatorTransitionCreateWindow _createWindow;
        private AnimatorTransitionModifyWindow _modifyWindow;
        private AnimatorController _lastController;
        private int _lastLayerIndex = -1;
        private readonly List<string> _displayPaths = new List<string>();
        private readonly Dictionary<string, string> _displayToActualPath = new Dictionary<string, string>();
        private readonly Dictionary<string, AnimatorState> _stateByDisplayPath = new Dictionary<string, AnimatorState>();
        private readonly Dictionary<string, AnimatorStateMachine> _stateMachineByDisplayPath = new Dictionary<string, AnimatorStateMachine>();
        private readonly Dictionary<string, bool> _isStateMachineMap = new Dictionary<string, bool>();

        internal static void Open()
        {
            AnimatorTransitionToolWindow window = GetWindow<AnimatorTransitionToolWindow>("动画控制器 - 过渡");
            window.minSize = new Vector2(550f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            if (_createWindow == null)
            {
                _createWindow = new AnimatorTransitionCreateWindow(this);
            }

            if (_modifyWindow == null)
            {
                _modifyWindow = new AnimatorTransitionModifyWindow(this);
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

        internal int SelectedLayerIndex => _selectedLayerIndex;

        internal int DisplayPathCount => _displayPaths.Count;

        private void OnGUI()
        {
            if (_createWindow == null || _modifyWindow == null)
            {
                OnEnable();
            }

            DrawTargetSelectionSection();

            AnimatorController currentController = SelectedController;
            if (currentController == null)
            {
                return;
            }

            int currentLayerIndex = Mathf.Max(0, _selectedLayerIndex);
            if (currentController != _lastController || currentLayerIndex != _lastLayerIndex)
            {
                RefreshStateList(SelectedLayer);
                _lastController = currentController;
                _lastLayerIndex = currentLayerIndex;
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("模式", EditorStyles.boldLabel);
            TransitionMode newMode = (TransitionMode)GUILayout.Toolbar((int)_mode, new[] { "创建过渡", "修改过渡" });
            if (newMode != _mode)
            {
                _mode = newMode;
                _createWindow.Reset();
                _modifyWindow.Reset();
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4f);
            _contentScrollPosition = EditorGUILayout.BeginScrollView(_contentScrollPosition);
            if (_mode == TransitionMode.Modify && _displayPaths.Count == 0)
            {
                EditorGUILayout.HelpBox("当前层级中没有找到任何状态。", MessageType.Warning);
            }
            else if (_mode == TransitionMode.Create)
            {
                _createWindow.OnGUI();
            }
            else
            {
                _modifyWindow.OnGUI();
            }
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
                EditorGUI.EndChangeCheck();
            }
            else
            {
                EditorGUILayout.HelpBox("当前控制器没有可用层级。", MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        private void SetTarget(Object target)
        {
            _targetObject = target;
            RefreshTargetContext();
            RefreshControllers();
            _lastController = null;
            _lastLayerIndex = -1;
            RefreshStateList(SelectedLayer);
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

            AnimatorController controllerAsset = _targetObject as AnimatorController;
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

        private void RefreshStateList(AnimatorControllerLayer layer)
        {
            _displayPaths.Clear();
            _displayToActualPath.Clear();
            _stateByDisplayPath.Clear();
            _stateMachineByDisplayPath.Clear();
            _isStateMachineMap.Clear();

            if (layer != null && layer.stateMachine != null)
            {
                CollectHierarchy(layer.stateMachine, string.Empty, string.Empty);
            }

            if (_createWindow != null)
            {
                _createWindow.Reset();
            }

            if (_modifyWindow != null)
            {
                _modifyWindow.Reset();
            }
        }

        internal void RefreshStateListForCurrentSelection()
        {
            RefreshStateList(SelectedLayer);
        }

        internal string[] GetDisplayPathOptions()
        {
            return _displayPaths.ToArray();
        }

        internal bool ContainsDisplayPath(string displayPath)
        {
            return !string.IsNullOrEmpty(displayPath) && _displayPaths.Contains(displayPath);
        }

        internal int IndexOfDisplayPath(string displayPath)
        {
            return string.IsNullOrEmpty(displayPath) ? -1 : _displayPaths.IndexOf(displayPath);
        }

        internal string GetDisplayPathAt(int index)
        {
            return index >= 0 && index < _displayPaths.Count ? _displayPaths[index] : string.Empty;
        }

        internal AnimatorState ResolveState(string displayPath)
        {
            AnimatorState state;
            return _stateByDisplayPath.TryGetValue(displayPath, out state) ? state : null;
        }

        internal AnimatorStateMachine ResolveStateMachine(string displayPath)
        {
            AnimatorStateMachine stateMachine;
            return _stateMachineByDisplayPath.TryGetValue(displayPath, out stateMachine) ? stateMachine : null;
        }

        internal bool IsStateMachineDisplayPath(string displayPath)
        {
            bool isStateMachine;
            return _isStateMachineMap.TryGetValue(displayPath, out isStateMachine) && isStateMachine;
        }

        internal string ResolveActualPath(string displayPath)
        {
            if (string.IsNullOrEmpty(displayPath))
            {
                return string.Empty;
            }

            string actualPath;
            return _displayToActualPath.TryGetValue(displayPath, out actualPath) ? actualPath : displayPath;
        }

        private void CollectHierarchy(AnimatorStateMachine stateMachine, string parentDisplayPath, string parentActualPath)
        {
            if (stateMachine == null)
            {
                return;
            }

            foreach (ChildAnimatorState childState in stateMachine.states)
            {
                if (childState.state == null)
                {
                    continue;
                }

                string stateName = childState.state.name;
                string fullDisplayPath = string.IsNullOrEmpty(parentDisplayPath)
                    ? stateName
                    : parentDisplayPath + AnimatorStatePathUtility.DisplayPathSeparator + stateName;

                string uniqueDisplayPath = fullDisplayPath;
                int counter = 1;
                while (_stateByDisplayPath.ContainsKey(uniqueDisplayPath) || _stateMachineByDisplayPath.ContainsKey(uniqueDisplayPath))
                {
                    uniqueDisplayPath = $"{fullDisplayPath} ({counter++})";
                }

                string fullActualPath = AnimatorStatePathUtility.CombinePath(parentActualPath, stateName);
                _displayPaths.Add(uniqueDisplayPath);
                _displayToActualPath[uniqueDisplayPath] = fullActualPath;
                _stateByDisplayPath[uniqueDisplayPath] = childState.state;
                _isStateMachineMap[uniqueDisplayPath] = false;
            }

            foreach (ChildAnimatorStateMachine childMachine in stateMachine.stateMachines)
            {
                if (childMachine.stateMachine == null)
                {
                    continue;
                }

                string machineName = childMachine.stateMachine.name;
                string machineNode = machineName + AnimatorStatePathUtility.SubStateMachineSuffix;
                string selfDisplayPath = string.IsNullOrEmpty(parentDisplayPath)
                    ? machineNode
                    : parentDisplayPath + AnimatorStatePathUtility.DisplayPathSeparator + machineNode;
                string uniqueSelfPath = selfDisplayPath;
                int counter = 1;
                while (_stateByDisplayPath.ContainsKey(uniqueSelfPath) || _stateMachineByDisplayPath.ContainsKey(uniqueSelfPath))
                {
                    uniqueSelfPath = $"{selfDisplayPath} ({counter++})";
                }

                string fullActualPath = AnimatorStatePathUtility.CombinePath(parentActualPath, machineName);
                _displayPaths.Add(uniqueSelfPath);
                _displayToActualPath[uniqueSelfPath] = fullActualPath;
                _stateMachineByDisplayPath[uniqueSelfPath] = childMachine.stateMachine;
                _isStateMachineMap[uniqueSelfPath] = true;
                CollectHierarchy(childMachine.stateMachine, uniqueSelfPath, fullActualPath);
            }
        }
    }
}
