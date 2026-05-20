using System;
using System.Collections.Generic;
using System.Linq;
using MVA.Toolbox.AnimatorShared.Paths;
using MVA.Toolbox.AnimatorShared.Targeting;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.AnimatorStateTool
{
    internal sealed class AnimatorStateToolWindow : EditorWindow
    {
        private enum StateMode
        {
            Split,
            Merge
        }

        private StateMode _mode = StateMode.Split;
        private Vector2 _contentScrollPosition;

        private Object _targetObject;
        private VRCAvatarDescriptor _avatarDescriptor;
        private Animator _animator;
        private readonly List<AnimatorController> _controllers = new List<AnimatorController>();
        private readonly List<string> _controllerNames = new List<string>();
        private int _selectedControllerIndex;
        private int _selectedLayerIndex;
        private AnimatorStateSplitWindow _splitWindow;
        private AnimatorStateMergeWindow _mergeWindow;

        private AnimatorController _lastController;
        private int _lastLayerIndex = -1;
        private readonly List<string> _displayPaths = new List<string>();
        private readonly Dictionary<string, string> _displayToActualPath = new Dictionary<string, string>();
        private readonly Dictionary<string, AnimatorState> _stateByDisplayPath = new Dictionary<string, AnimatorState>();

        internal static void Open()
        {
            var window = GetWindow<AnimatorStateToolWindow>("动画控制器 - 状态");
            window.minSize = new Vector2(550f, 520f);
            window.Show();
        }

        private void OnEnable()
        {
            if (_splitWindow == null)
            {
                _splitWindow = new AnimatorStateSplitWindow(this);
            }

            if (_mergeWindow == null)
            {
                _mergeWindow = new AnimatorStateMergeWindow(this);
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
                var controller = SelectedController;
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
            if (_splitWindow == null || _mergeWindow == null)
            {
                OnEnable();
            }

            DrawTargetSelectionSection();

            var currentController = SelectedController;
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
            var newMode = (StateMode)GUILayout.Toolbar((int)_mode, new[] { "分割状态", "合并状态" });
            if (newMode != _mode)
            {
                _mode = newMode;
                _splitWindow.Reset();
                _mergeWindow.Reset();
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4f);
            _contentScrollPosition = EditorGUILayout.BeginScrollView(_contentScrollPosition);
            if (_displayPaths.Count == 0)
            {
                EditorGUILayout.HelpBox("当前层级中没有找到任何状态。", MessageType.Warning);
            }
            else if (_mode == StateMode.Split)
            {
                _splitWindow.OnGUI();
            }
            else
            {
                _mergeWindow.OnGUI();
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawTargetSelectionSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.BeginChangeCheck();
            var newTarget = EditorGUILayout.ObjectField("目标对象", _targetObject, typeof(Object), true);
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

            var controller = SelectedController;
            if (controller != null && controller.layers != null && controller.layers.Length > 0)
            {
                var layerNames = new string[controller.layers.Length];
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

            if (_targetObject is AnimatorController controllerAsset)
            {
                _controllers.Add(controllerAsset);
                _controllerNames.Add(controllerAsset.name);
                return;
            }

            var root = AnimatorTargetResolver.ResolveControllerScanRoot(_targetObject, _avatarDescriptor, _animator);
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

            if (layer != null && layer.stateMachine != null)
            {
                CollectStateHierarchy(layer.stateMachine, string.Empty, string.Empty);
            }

            if (_splitWindow != null)
            {
                _splitWindow.Reset();
            }

            if (_mergeWindow != null)
            {
                _mergeWindow.Reset();
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
            return _stateByDisplayPath.TryGetValue(displayPath, out var state) ? state : null;
        }

        private void CollectStateHierarchy(AnimatorStateMachine stateMachine, string parentDisplayPath, string parentActualPath)
        {
            if (stateMachine == null)
            {
                return;
            }

            foreach (var childState in stateMachine.states)
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
                while (_stateByDisplayPath.ContainsKey(uniqueDisplayPath))
                {
                    uniqueDisplayPath = $"{fullDisplayPath} ({counter++})";
                }

                string fullActualPath = AnimatorStatePathUtility.CombinePath(parentActualPath, stateName);
                _displayPaths.Add(uniqueDisplayPath);
                _displayToActualPath[uniqueDisplayPath] = fullActualPath;
                _stateByDisplayPath[uniqueDisplayPath] = childState.state;
            }

            foreach (var childMachine in stateMachine.stateMachines)
            {
                if (childMachine.stateMachine == null)
                {
                    continue;
                }

                string machineName = childMachine.stateMachine.name;
                string selfDisplayPath = string.IsNullOrEmpty(parentDisplayPath)
                    ? machineName + AnimatorStatePathUtility.SubStateMachineSuffix
                    : parentDisplayPath + AnimatorStatePathUtility.DisplayPathSeparator + machineName + AnimatorStatePathUtility.SubStateMachineSuffix;
                string fullActualPath = AnimatorStatePathUtility.CombinePath(parentActualPath, machineName);
                CollectStateHierarchy(childMachine.stateMachine, selfDisplayPath, fullActualPath);
            }
        }

        private string GetStateParentPath(AnimatorState targetState)
        {
            if (targetState == null)
            {
                return null;
            }

            foreach (var entry in _displayToActualPath)
            {
                if (_stateByDisplayPath.TryGetValue(entry.Key, out var state) && state == targetState)
                {
                    var segments = AnimatorStatePathUtility.SplitPath(entry.Value);
                    if (segments.Length > 1)
                    {
                        return string.Join("/", segments.Take(segments.Length - 1).ToArray());
                    }

                    break;
                }
            }

            return null;
        }

        internal string FormatStateNameWithPath(AnimatorState state)
        {
            if (state == null)
            {
                return string.Empty;
            }

            string parentPath = GetStateParentPath(state);
            return !string.IsNullOrEmpty(parentPath) ? $"{state.name} ({parentPath})" : state.name;
        }
    }
}
