using System;
using System.Collections.Generic;
using MVA.Toolbox.Animation.Shared.Controllers;
using MVA.Toolbox.AnimatorShared.Targeting;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.AnimatorParameterTool
{
    internal sealed class AnimatorParameterToolWindow : EditorWindow
    {
        private enum ParameterMode
        {
            Scan,
            Apply,
            Check,
            Adjust,
            Trace
        }

        private ParameterMode _mode = ParameterMode.Scan;
        private Vector2 _contentScrollPosition;

        private Object _targetObject;
        private VRCAvatarDescriptor _avatarDescriptor;
        private VRCAvatarDescriptor _directAvatarDescriptor;
        private Animator _animator;
        private readonly List<AnimatorController> _controllers = new List<AnimatorController>();
        private readonly List<string> _controllerNames = new List<string>();
        private readonly Dictionary<AnimatorController, Dictionary<string, (bool saved, bool synced)>> _maParameterDefaults =
            new Dictionary<AnimatorController, Dictionary<string, (bool saved, bool synced)>>();
        private int _selectedControllerIndex;

        private AnimatorParameterScanWindow _scanWindow;
        private AnimatorParameterApplyWindow _applyWindow;
        private AnimatorParameterCheckWindow _checkWindow;
        private AnimatorParameterTraceWindow _traceWindow;
        private AnimatorParameterAdjustWindow _adjustWindow;

        internal static void Open()
        {
            var window = GetWindow<AnimatorParameterToolWindow>("动画控制器 - 参数");
            window.minSize = new Vector2(760f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            if (_scanWindow == null)
            {
                _scanWindow = new AnimatorParameterScanWindow(this);
            }

            if (_applyWindow == null)
            {
                _applyWindow = new AnimatorParameterApplyWindow(this);
            }

            if (_checkWindow == null)
            {
                _checkWindow = new AnimatorParameterCheckWindow(this);
            }

            if (_traceWindow == null)
            {
                _traceWindow = new AnimatorParameterTraceWindow(this);
            }

            if (_adjustWindow == null)
            {
                _adjustWindow = new AnimatorParameterAdjustWindow(this);
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

        internal IReadOnlyList<AnimatorController> Controllers => _controllers;

        internal Object TargetObject => _targetObject;

        internal bool IsControllerAssetTarget => _targetObject is AnimatorController;

        internal GameObject SceneFeatureRoot => ResolveSceneFeatureRoot();

        internal VRCExpressionParameters AutoBoundExpressionParameters => _directAvatarDescriptor != null ? _directAvatarDescriptor.expressionParameters : null;

        internal bool TryGetMAParameterDefaults(AnimatorController controller, out Dictionary<string, (bool saved, bool synced)> defaults)
        {
            return _maParameterDefaults.TryGetValue(controller, out defaults);
        }

        private void OnGUI()
        {
            if (_scanWindow == null || _applyWindow == null || _checkWindow == null || _traceWindow == null || _adjustWindow == null)
            {
                OnEnable();
            }

            DrawTargetSelectionSection();

            if (_targetObject == null || _controllers.Count == 0)
            {
                return;
            }

            EditorGUILayout.Space(4f);
            DrawModeSection();

            EditorGUILayout.Space(4f);
            _contentScrollPosition = EditorGUILayout.BeginScrollView(_contentScrollPosition);
            switch (_mode)
            {
                case ParameterMode.Scan:
                    _scanWindow.OnGUI();
                    break;
                case ParameterMode.Apply:
                    _applyWindow.OnGUI();
                    break;
                case ParameterMode.Check:
                    _checkWindow.OnGUI();
                    break;
                case ParameterMode.Adjust:
                    _adjustWindow.OnGUI();
                    break;
                case ParameterMode.Trace:
                    _traceWindow.OnGUI();
                    break;
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
            _selectedControllerIndex = EditorGUILayout.Popup("控制器", Mathf.Clamp(_selectedControllerIndex, 0, _controllerNames.Count - 1), _controllerNames.ToArray());
            if (EditorGUI.EndChangeCheck())
            {
                if (_checkWindow != null)
                {
                    _checkWindow.Reset();
                }

                if (_traceWindow != null)
                {
                    _traceWindow.Reset();
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawModeSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("模式", EditorStyles.boldLabel);

            bool targetIsControllerAsset = _targetObject is AnimatorController;
            string[] modeLabels = targetIsControllerAsset
                ? new[] { "添加到 Parameters", "参数检查", "参数调整", "参数追踪" }
                : new[] { "添加参数到控制器", "添加到 Parameters", "参数检查", "参数调整", "参数追踪" };

            int selectedIndex = targetIsControllerAsset
                ? Mathf.Clamp((int)_mode - 1, 0, modeLabels.Length - 1)
                : (int)_mode;

            int newIndex = GUILayout.Toolbar(selectedIndex, modeLabels);
            ParameterMode mappedMode = targetIsControllerAsset ? (ParameterMode)(newIndex + 1) : (ParameterMode)newIndex;
            if (mappedMode != _mode)
            {
                ParameterMode oldMode = _mode;
                _mode = mappedMode;
                if (ShouldIncludeMAParameterControllers(oldMode) != ShouldIncludeMAParameterControllers(_mode))
                {
                    RefreshControllers();
                }
            }

            EditorGUILayout.EndVertical();
        }

        private bool ShouldIncludeMAParameterControllers(ParameterMode mode)
        {
            return mode == ParameterMode.Apply && !(_targetObject is AnimatorController);
        }

        private void SetTarget(Object target)
        {
            _targetObject = target;
            RefreshTargetContext();
            RefreshControllers();
            RefreshExpressionParametersBinding();

            if (_scanWindow != null)
            {
                _scanWindow.Reset();
            }

            if (_checkWindow != null)
            {
                _checkWindow.Reset();
            }

            if (_traceWindow != null)
            {
                _traceWindow.Reset();
            }

            if (_adjustWindow != null)
            {
                _adjustWindow.Reset();
            }
        }

        private void RefreshTargetContext()
        {
            _avatarDescriptor = AnimatorTargetResolver.ResolveAvatarDescriptor(_targetObject);
            _directAvatarDescriptor = AnimatorTargetResolver.ResolveDirectAvatarDescriptor(_targetObject);
            _animator = AnimatorTargetResolver.ResolveAnimator(_targetObject);
        }

        private void RefreshExpressionParametersBinding()
        {
            if (_applyWindow != null)
            {
                _applyWindow.OnTargetChanged(AutoBoundExpressionParameters);
            }
        }

        private void RefreshControllers()
        {
            AnimatorController previousSelection = SelectedController;

            _controllers.Clear();
            _controllerNames.Clear();
            _maParameterDefaults.Clear();
            _selectedControllerIndex = 0;

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

            GameObject root = AnimatorTargetResolver.ResolveControllerScanRoot(_targetObject, _avatarDescriptor, _animator);
            if (root == null)
            {
                return;
            }

            List<ControllerWithRoot> entries = AnimatorControllerCollection.CollectControllersWithRoot(root, includeSpecialLayers: true, allowAnimatorSubtree: true);
            for (int i = 0; i < entries.Count; i++)
            {
                ControllerWithRoot entry = entries[i];
                if (entry.Controller != null)
                {
                    _controllers.Add(entry.Controller);
                }
            }

            _controllerNames.AddRange(AnimatorControllerCollection.BuildControllerDisplayNames(_avatarDescriptor, _animator, _controllers));
            _selectedControllerIndex = 0;
            if (_avatarDescriptor != null)
            {
                AnimatorController fxController = AnimatorControllerCollection.GetExistingFXController(_avatarDescriptor);
                if (fxController != null)
                {
                    int fxIndex = _controllers.IndexOf(fxController);
                    if (fxIndex >= 0)
                    {
                        _selectedControllerIndex = fxIndex;
                    }
                }
            }

            if (ShouldIncludeMAParameterControllers(_mode))
            {
                AppendMAParametersControllers(root, new HashSet<AnimatorController>(_controllers));
            }

            if (previousSelection != null)
            {
                int previousIndex = _controllers.FindIndex(c => c == previousSelection);
                if (previousIndex >= 0)
                {
                    _selectedControllerIndex = previousIndex;
                    return;
                }
            }

        }

        private void AppendMAParametersControllers(GameObject root, HashSet<AnimatorController> seen)
        {
            if (root == null)
            {
                return;
            }

            List<AnimatorParameterMAControllerService.ControllerBuildResult> results = AnimatorParameterMAControllerService.BuildControllers(root);
            for (int i = 0; i < results.Count; i++)
            {
                AnimatorController controller = results[i].Controller;
                Dictionary<string, (bool saved, bool synced)> defaults = results[i].Defaults;
                if (controller == null || seen == null || !seen.Add(controller))
                {
                    continue;
                }

                _controllers.Add(controller);
                _controllerNames.Add(controller.name);
                if (defaults != null)
                {
                    _maParameterDefaults[controller] = defaults;
                }
            }
        }

        private GameObject ResolveSceneFeatureRoot()
        {
            return AnimatorTargetResolver.ResolveTargetGameObject(_targetObject);
        }

    }
}
