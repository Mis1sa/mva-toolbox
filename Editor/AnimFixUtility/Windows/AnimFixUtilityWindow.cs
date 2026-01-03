using System;
using UnityEditor;
using UnityEngine;
using MVA.Toolbox.AnimFixUtility.Services;
using UnityEditor.Animations;

namespace MVA.Toolbox.AnimFixUtility.Windows
{
    /// <summary>
    /// AnimFix Utility 总控窗口：整合 Find / Bake / Redirect 三个动画维护功能
    /// </summary>
    public sealed class AnimFixUtilityWindow : EditorWindow
    {
        private enum Tab
        {
            Find,
            Bake,
            Redirect
        }

        private Tab _currentTab = Tab.Find;
        private Vector2 _scroll;

        private AnimFixUtilityContext _context;
        private AnimFixFindWindow _findWindow;
        private AnimFixBakeWindow _bakeWindow;
        private AnimFixRedirectWindow _redirectWindow;

        [MenuItem("Tools/MVA Toolbox/AnimFix Utility", false, 3)]
        public static void Open()
        {
            var window = GetWindow<AnimFixUtilityWindow>("AnimFix Utility");
            window.minSize = new Vector2(520f, 480f);
        }

        private void OnEnable()
        {
            _context = new AnimFixUtilityContext();
        }

        private void OnDisable()
        {
            _redirectWindow?.Dispose();
            _redirectWindow = null;
        }

        private void OnGUI()
        {
            DrawTargetSelection();

            EditorGUILayout.Space(4f);

            DrawTabBar();

            EditorGUILayout.Space(4f);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawCurrentTabContent();
            EditorGUILayout.EndScrollView();
        }

        private bool RedirectTrackingActive => RedirectWindow != null && RedirectWindow.IsTracking;

        private void DrawTargetSelection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("目标对象", EditorStyles.boldLabel);

            var newTarget = (GameObject)EditorGUILayout.ObjectField(
                "Avatar / 带 Animator 的物体",
                _context.TargetRoot,
                typeof(GameObject),
                true);

            if (newTarget != _context.TargetRoot)
            {
                if (!_context.TrySetTarget(newTarget))
                {
                    EditorUtility.DisplayDialog("无效对象", "请拖入 Avatar 根或带 Animator 组件的物体。", "确定");
                }
            }

            bool lockSelection = RedirectTrackingActive;

            if (!_context.HasValidTarget)
            {
                EditorGUILayout.HelpBox("请选择 Avatar 或带 Animator 的物体。", MessageType.Info);
            }
            else if (_context.Controllers.Count == 0)
            {
                EditorGUILayout.HelpBox("未在该目标下找到 AnimatorController。", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.Space(4f);

                var controllerNames = _context.BuildControllerNameArray();
                bool allowControllerAll = _currentTab == Tab.Find;
                if (!allowControllerAll && _context.SelectedControllerIndex < 0 && controllerNames.Length > 0)
                {
                    _context.SelectedControllerIndex = 0;
                }
                string[] controllerOptions;
                int displayControllerIndex;

                if (allowControllerAll)
                {
                    controllerOptions = new string[controllerNames.Length + 1];
                    controllerOptions[0] = "全部控制器";
                    for (int i = 0; i < controllerNames.Length; i++)
                    {
                        controllerOptions[i + 1] = controllerNames[i];
                    }

                    displayControllerIndex = _context.SelectedControllerIndex < 0
                        ? 0
                        : Mathf.Clamp(_context.SelectedControllerIndex + 1, 0, controllerOptions.Length - 1);
                }
                else
                {
                    controllerOptions = controllerNames;
                    displayControllerIndex = Mathf.Clamp(_context.SelectedControllerIndex, 0,
                        Mathf.Max(0, controllerOptions.Length - 1));
                }

                using (new EditorGUI.DisabledScope(lockSelection))
                {
                    int newControllerIndex = EditorGUILayout.Popup("控制器", displayControllerIndex, controllerOptions);
                    if (newControllerIndex != displayControllerIndex)
                    {
                        if (allowControllerAll)
                        {
                            _context.SelectedControllerIndex = newControllerIndex == 0 ? -1 : newControllerIndex - 1;
                        }
                        else
                        {
                            _context.SelectedControllerIndex = newControllerIndex;
                        }
                    }
                }

                var selectedController = _context.SelectedController;
                if (selectedController != null)
                {
                    bool allowAllLayers = _currentTab != Tab.Bake;
                    var layers = selectedController.layers ?? Array.Empty<AnimatorControllerLayer>();
                    int extra = allowAllLayers ? 1 : 0;
                    string[] layerOptions = new string[layers.Length + extra];
                    if (allowAllLayers)
                    {
                        layerOptions[0] = "全部层级";
                    }

                    for (int i = 0; i < layers.Length; i++)
                    {
                        string layerName = layers[i]?.name;
                        layerOptions[i + extra] = string.IsNullOrEmpty(layerName) ? $"Layer {i}" : layerName;
                    }

                    if (!allowAllLayers && _context.SelectedLayerIndex < 0 && layerOptions.Length > 0)
                    {
                        _context.SelectedLayerIndex = 0;
                    }

                    int displayLayerIndex = allowAllLayers
                        ? Mathf.Clamp(_context.SelectedLayerIndex + 1, 0, layerOptions.Length - 1)
                        : Mathf.Clamp(_context.SelectedLayerIndex, 0, Mathf.Max(0, layerOptions.Length - 1));

                    int newLayerIndex;
                    using (new EditorGUI.DisabledScope(lockSelection))
                    {
                        newLayerIndex = EditorGUILayout.Popup("层级", displayLayerIndex, layerOptions);
                    }
                    if (newLayerIndex != displayLayerIndex)
                    {
                        _context.SelectedLayerIndex = allowAllLayers ? newLayerIndex - 1 : newLayerIndex;
                    }
                }
                else
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.Popup("层级", 0, new[] { "全部层级" });
                    }
                    _context.SelectedLayerIndex = -1;
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawTabBar()
        {
            var labels = new[] { "查找动画", "默认值烘焙", "路径重定向" };
            var previousTab = _currentTab;
            using (new EditorGUI.DisabledScope(RedirectTrackingActive))
            {
                _currentTab = (Tab)GUILayout.Toolbar((int)_currentTab, labels);
            }

            if (previousTab != _currentTab)
            {
                OnTabChanged(_currentTab);
            }
        }

        private void DrawCurrentTabContent()
        {
            if (!_context.HasValidTarget || _context.Controllers.Count == 0)
            {
                EditorGUILayout.HelpBox("请先选择有效的目标和控制器。", MessageType.Info);
                return;
            }

            switch (_currentTab)
            {
                case Tab.Find:
                    _findWindow ??= new AnimFixFindWindow(_context);
                    _findWindow.OnGUI();
                    break;
                case Tab.Bake:
                    _bakeWindow ??= new AnimFixBakeWindow(_context);
                    _bakeWindow.OnGUI();
                    break;
                case Tab.Redirect:
                    RedirectWindow.OnGUI();
                    break;
            }
        }

        private void OnTabChanged(Tab tab)
        {
            bool allowAnimatorSubtree = tab != Tab.Redirect;
            _context.SetAllowAnimatorSubtree(allowAnimatorSubtree);
        }

        private AnimFixRedirectWindow RedirectWindow
        {
            get
            {
                _redirectWindow ??= new AnimFixRedirectWindow(_context, Repaint);
                return _redirectWindow;
            }
        }
    }
}
