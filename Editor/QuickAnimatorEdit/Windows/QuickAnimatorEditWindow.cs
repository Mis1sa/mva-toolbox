using UnityEditor;
using UnityEngine;
using MVA.Toolbox.QuickAnimatorEdit.Services.Shared;

namespace MVA.Toolbox.QuickAnimatorEdit.Windows
{
    /// <summary>
    /// Quick Animator Edit 总控窗口
    /// 提供 Tab 切换：状态 / 过渡 / 参数 / 混合树
    /// </summary>
    public sealed class QuickAnimatorEditWindow : EditorWindow
    {
        private enum Tab
        {
            State,
            Transition,
            Parameter,
            BlendTree
        }

        private Tab _currentTab = Tab.State;
        private Vector2 _scrollPosition;

        // 共享上下文（目标对象/控制器/层）
        private QuickAnimatorEditContext _context;

        // 各功能面板实例（延迟初始化）
        private QuickAnimatorEditStateWindow _statePanel;
        private QuickAnimatorEditTransitionWindow _transitionPanel;
        private QuickAnimatorEditParameterWindow _parameterPanel;
        private QuickAnimatorEditBlendTreeWindow _blendTreePanel;

        [MenuItem("Tools/MVA Toolbox/Quick Animator Edit", false, 2)]
        public static void Open()
        {
            var window = GetWindow<QuickAnimatorEditWindow>("Quick Animator Edit");
            window.minSize = new Vector2(550f, 500f);
        }

        private void OnEnable()
        {
            _context = new QuickAnimatorEditContext();
        }

        private void OnGUI()
        {
            DrawTargetSelection();

            EditorGUILayout.Space(4f);

            DrawTabBar();

            EditorGUILayout.Space(4f);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            DrawCurrentTabContent();
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// 绘制目标对象选择区域（共享）
        /// </summary>
        private void DrawTargetSelection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("目标对象", EditorStyles.boldLabel);

            _context.DrawTargetSelectionUI();

            if (_context.Controllers.Count > 0)
            {
                EditorGUILayout.Space(4f);
                // 参数功能不需要层级选择，将其置灰
                bool enableLayerSelection = _currentTab != Tab.Parameter;
                _context.DrawControllerAndLayerSelectionUI(enableLayerSelection);
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 绘制顶部 Tab 栏
        /// </summary>
        private void DrawTabBar()
        {
            var tabLabels = new[] { "状态", "过渡", "参数", "混合树" };
            _currentTab = (Tab)GUILayout.Toolbar((int)_currentTab, tabLabels);
        }

        /// <summary>
        /// 根据当前 Tab 绘制对应功能面板
        /// </summary>
        private void DrawCurrentTabContent()
        {
            // 延迟初始化各功能面板
            if (_context.Controllers.Count == 0)
            {
                EditorGUILayout.HelpBox("请先选择目标对象和控制器。", MessageType.Info);
                return;
            }

            switch (_currentTab)
            {
                case Tab.State:
                    if (_statePanel == null)
                        _statePanel = new QuickAnimatorEditStateWindow(_context);
                    _statePanel.OnGUI();
                    break;
                case Tab.Transition:
                    if (_transitionPanel == null)
                        _transitionPanel = new QuickAnimatorEditTransitionWindow(_context);
                    _transitionPanel.OnGUI();
                    break;
                case Tab.Parameter:
                    if (_parameterPanel == null)
                        _parameterPanel = new QuickAnimatorEditParameterWindow(_context);
                    _parameterPanel.OnGUI();
                    break;
                case Tab.BlendTree:
                    if (_blendTreePanel == null)
                        _blendTreePanel = new QuickAnimatorEditBlendTreeWindow(_context);
                    _blendTreePanel.OnGUI();
                    break;
            }
        }
    }
}
