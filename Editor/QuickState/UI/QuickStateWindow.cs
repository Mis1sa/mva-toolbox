using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using MVA.Toolbox.Public;
using MVA.Toolbox.QuickState.Services;

namespace MVA.Toolbox.QuickState.UI
{
    /// <summary>
    /// Quick State 主窗口：提供 Animator 状态的分割/合并工具。
    /// 仅负责编辑器 UI 与用户交互，具体状态与 Transition 修改逻辑由 QuickStateService 实现。
    /// </summary>
    public sealed class QuickStateWindow : EditorWindow
    {
        enum ToolMode
        {
            Split,
            Merge
        }

        // 目标对象：Avatar / 带 Animator 的物体 / AnimatorController 资产
        Object _targetObject;
        VRCAvatarDescriptor _avatarDescriptor;
        Animator _animator;

        // 当前工作用到的 AnimatorController 列表及显示名称
        readonly List<AnimatorController> _controllers = new List<AnimatorController>();
        readonly List<string> _controllerNames = new List<string>();
        int _selectedControllerIndex;
        int _selectedLayerIndex;

        ToolMode _mode = ToolMode.Split;

        Vector2 _mainScroll;

        // 分割模式：状态选择与缓存
        string _splitSelectedStatePath = string.Empty;
        readonly List<string> _splitAllStatePaths = new List<string>();
        readonly Dictionary<string, AnimatorState> _splitStateByPath = new Dictionary<string, AnimatorState>();
        int _splitSelectedStateIndex;
        AnimatorState _splitResolvedState;

        string _splitHeadName = string.Empty;
        string _splitTailName = string.Empty;
        bool _splitKeepOriginalAsHead;
        bool _splitKeepOriginalAsTail;
        bool _splitManualAdjust;
        bool _splitIsDefaultState;
        QuickStateService.DefaultStateDesignation _splitDefaultDesignation = QuickStateService.DefaultStateDesignation.Head;

        readonly List<QuickStateService.TransitionAdjustment> _splitIncomingAdjustments = new List<QuickStateService.TransitionAdjustment>();
        readonly List<QuickStateService.TransitionAdjustment> _splitAnyAdjustments = new List<QuickStateService.TransitionAdjustment>();
        readonly List<QuickStateService.TransitionAdjustment> _splitOutgoingAdjustments = new List<QuickStateService.TransitionAdjustment>();

        Vector2 _splitScrollIncoming;
        Vector2 _splitScrollOutgoing;

        // 合并模式：两端状态与保留状态名称
        string _mergeStateAPath = string.Empty;
        int _mergeStateAIndex;
        AnimatorState _mergeResolvedStateA;

        string _mergeStateBPath = string.Empty;
        int _mergeStateBIndex;
        AnimatorState _mergeResolvedStateB;

        string _mergeNewStateName = string.Empty;
        string _mergeLastKeepStateName = string.Empty;
        bool _mergeKeepA = true;

        [MenuItem("Tools/MVA Toolbox/Quick State", false, 4)]
        public static void Open()
        {
            var window = GetWindow<QuickStateWindow>("Quick State");
            window.minSize = new Vector2(500f, 550f);
        }

        void OnEnable()
        {
        }

        void OnDisable()
        {
        }

        void OnGUI()
        {
            EnsureResolvedStatesValid();

            _mainScroll = ToolboxUtils.ScrollView(_mainScroll, () =>
            {
                DrawTargetSelection();

                GUILayout.Space(4f);

                if (_controllers.Count > 0)
                {
                    DrawControllerAndLayerSelection();

                    GUILayout.Space(4f);

                    DrawModeSelection();

                    GUILayout.Space(6f);

                    if (_controllers.Count > 0)
                    {
                        switch (_mode)
                        {
                            case ToolMode.Split:
                                DrawSplitModeArea();
                                break;
                            case ToolMode.Merge:
                                DrawMergeModeArea();
                                break;
                        }
                    }
                }
            });
        }

        // 顶部目标对象区域：选择 Avatar/Animator/控制器，并刷新内部缓存
        void DrawTargetSelection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("目标对象", EditorStyles.boldLabel);

            var newTarget = EditorGUILayout.ObjectField("Avatar / Animator / 控制器", _targetObject, typeof(Object), true);
            if (newTarget != _targetObject)
            {
                _targetObject = newTarget;
                RefreshTargetComponents();
                RefreshControllers();
                ClearSplitSelection();
                ClearMergeSelection();
            }

            if (_targetObject == null)
            {
                EditorGUILayout.HelpBox("请拖入一个 VRChat Avatar、带 Animator 组件的物体，或直接拖入 动画控制器。", MessageType.Info);
            }
            else if (_controllers.Count == 0)
            {
                EditorGUILayout.HelpBox("在当前目标中未找到任何 AnimatorController，请确认 Avatar/物体已正确配置动画控制器。", MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        // 从当前目标对象提取 AvatarDescriptor / Animator（若存在）
        void RefreshTargetComponents()
        {
            _avatarDescriptor = null;
            _animator = null;

            if (_targetObject is GameObject go)
            {
                _avatarDescriptor = go.GetComponent<VRCAvatarDescriptor>();
                _animator = go.GetComponent<Animator>();
            }
        }

        // 基于当前目标对象收集可用 AnimatorController 列表
        void RefreshControllers()
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

            if (_targetObject is GameObject root)
            {
                _controllers.AddRange(ToolboxUtils.CollectControllersFromRoot(root, includeSpecialLayers: true));
                if (_controllers.Count > 0)
                {
                    _controllerNames.AddRange(ToolboxUtils.BuildControllerDisplayNames(_avatarDescriptor, _animator, _controllers));
                }
            }
        }

        // 控制器与层级的选择区域
        void DrawControllerAndLayerSelection()
        {
            var controller = _controllers[Mathf.Clamp(_selectedControllerIndex, 0, _controllers.Count - 1)];
            if (controller == null)
            {
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            int prevControllerIndex = _selectedControllerIndex;
            int prevLayerIndex = _selectedLayerIndex;

            if (_controllers.Count > 1)
            {
                EditorGUILayout.LabelField("动画控制器", EditorStyles.boldLabel);
                var names = _controllerNames.Count == _controllers.Count ? _controllerNames.ToArray() : BuildControllerNamesFallback();
                _selectedControllerIndex = EditorGUILayout.Popup("控制器", _selectedControllerIndex, names);
                controller = _controllers[Mathf.Clamp(_selectedControllerIndex, 0, _controllers.Count - 1)];
            }

            var layers = controller.layers ?? System.Array.Empty<AnimatorControllerLayer>();
            if (layers.Length == 0)
            {
                EditorGUILayout.HelpBox("当前控制器中没有任何层级。", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            var layerNames = new string[layers.Length];
            for (int i = 0; i < layers.Length; i++)
            {
                layerNames[i] = layers[i].name ?? $"Layer {i}";
            }

            _selectedLayerIndex = Mathf.Clamp(_selectedLayerIndex, 0, layers.Length - 1);
            _selectedLayerIndex = EditorGUILayout.Popup("层级", _selectedLayerIndex, layerNames);

            // 控制器或层级变动时，清理与之绑定的分割/合并状态缓存
            if (_selectedControllerIndex != prevControllerIndex || _selectedLayerIndex != prevLayerIndex)
            {
                ClearSplitSelection();
                ClearMergeSelection();
            }

            EditorGUILayout.EndVertical();
        }

        string[] BuildControllerNamesFallback()
        {
            var result = new string[_controllers.Count];
            for (int i = 0; i < _controllers.Count; i++)
            {
                result[i] = _controllers[i] != null ? _controllers[i].name : "(Controller)";
            }

            return result;
        }

        // 工具模式切换：分割状态 / 合并状态
        void DrawModeSelection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("模式", EditorStyles.boldLabel);

            var labels = new[] { "分割状态", "合并状态" };
            var newIndex = GUILayout.Toolbar((int)_mode, labels);
            if (newIndex != (int)_mode)
            {
                _mode = (ToolMode)newIndex;
                ClearSplitSelection();
                ClearMergeSelection();
            }

            EditorGUILayout.EndVertical();
        }

        // 分割模式主区域：状态选择 + 新状态名称 + 手动 Transition 调整
        void DrawSplitModeArea()
        {
            var controller = _controllers[Mathf.Clamp(_selectedControllerIndex, 0, _controllers.Count - 1)];
            var layers = controller.layers ?? System.Array.Empty<AnimatorControllerLayer>();
            if (layers.Length == 0)
            {
                return;
            }

            var layer = layers[Mathf.Clamp(_selectedLayerIndex, 0, layers.Length - 1)];

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("分割状态", EditorStyles.boldLabel);

            DrawSplitStateSelection(layer);

            GUILayout.Space(4f);

            if (_splitResolvedState != null)
            {
                DrawSplitNameAndDefaultOptions(layer);

                GUILayout.Space(4f);

                DrawSplitAdjustments(layer);

                GUILayout.Space(4f);

                DrawSplitApplyButton(controller, layer);
            }

            EditorGUILayout.EndVertical();
        }

        // 在当前层的状态机中构建状态路径下拉，用于选择待分割的原始状态
        void DrawSplitStateSelection(AnimatorControllerLayer layer)
        {
            var stateMachine = layer.stateMachine;
            _splitAllStatePaths.Clear();
            _splitStateByPath.Clear();

            CollectStatesWithPaths(stateMachine, string.Empty, _splitAllStatePaths, _splitStateByPath);

            if (_splitAllStatePaths.Count == 0)
            {
                EditorGUILayout.HelpBox("当前层中没有任何状态。", MessageType.Warning);
                return;
            }

            if (!_splitAllStatePaths.Contains(_splitSelectedStatePath))
            {
                _splitSelectedStatePath = _splitAllStatePaths[0];
            }

            _splitSelectedStateIndex = _splitAllStatePaths.IndexOf(_splitSelectedStatePath);
            if (_splitSelectedStateIndex < 0) _splitSelectedStateIndex = 0;

            int oldIndex = _splitSelectedStateIndex;
            _splitSelectedStateIndex = EditorGUILayout.Popup("目标状态", _splitSelectedStateIndex, _splitAllStatePaths.ToArray());
            _splitSelectedStatePath = _splitAllStatePaths[Mathf.Clamp(_splitSelectedStateIndex, 0, _splitAllStatePaths.Count - 1)];

            _splitResolvedState = _splitStateByPath.TryGetValue(_splitSelectedStatePath, out var s) ? s : null;

            // 当切换目标状态时，重置新状态名称和相关选项，避免沿用上一个状态的配置
            if (_splitSelectedStateIndex != oldIndex)
            {
                _splitHeadName = string.Empty;
                _splitTailName = string.Empty;
                _splitKeepOriginalAsHead = false;
                _splitKeepOriginalAsTail = false;
                _splitIsDefaultState = false;
                _splitDefaultDesignation = QuickStateService.DefaultStateDesignation.Head;

                _splitIncomingAdjustments.Clear();
                _splitAnyAdjustments.Clear();
                _splitOutgoingAdjustments.Clear();
            }
        }

        // 设置头/尾状态名称与默认状态选项
        void DrawSplitNameAndDefaultOptions(AnimatorControllerLayer layer)
        {
            var originalName = _splitResolvedState != null ? _splitResolvedState.name : string.Empty;

            if (string.IsNullOrEmpty(_splitHeadName))
            {
                _splitHeadName = string.IsNullOrEmpty(originalName) ? "State_Head" : originalName + "_Head";
            }

            if (string.IsNullOrEmpty(_splitTailName))
            {
                _splitTailName = string.IsNullOrEmpty(originalName) ? "State_Tail" : originalName + "_Tail";
            }

            EditorGUILayout.LabelField("新状态名称", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("头部状态", GUILayout.Width(80f));
            if (_splitKeepOriginalAsHead && !string.IsNullOrEmpty(originalName))
            {
                _splitHeadName = originalName;
            }
            EditorGUI.BeginDisabledGroup(_splitKeepOriginalAsHead);
            _splitHeadName = EditorGUILayout.TextField(_splitHeadName);
            EditorGUI.EndDisabledGroup();
            _splitKeepOriginalAsHead = EditorGUILayout.ToggleLeft("保留原状态", _splitKeepOriginalAsHead, GUILayout.Width(110f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("尾部状态", GUILayout.Width(80f));
            if (_splitKeepOriginalAsTail && !string.IsNullOrEmpty(originalName))
            {
                _splitTailName = originalName;
            }
            EditorGUI.BeginDisabledGroup(_splitKeepOriginalAsTail);
            _splitTailName = EditorGUILayout.TextField(_splitTailName);
            EditorGUI.EndDisabledGroup();
            _splitKeepOriginalAsTail = EditorGUILayout.ToggleLeft("保留原状态", _splitKeepOriginalAsTail, GUILayout.Width(110f));
            EditorGUILayout.EndHorizontal();

            if (_splitKeepOriginalAsHead && _splitKeepOriginalAsTail)
            {
                EditorGUILayout.HelpBox("原始状态不能同时作为头部和尾部。", MessageType.Error);
            }

            if (_splitHeadName == _splitTailName)
            {
                EditorGUILayout.HelpBox("头部和尾部状态名称不能相同。", MessageType.Error);
            }

            GUILayout.Space(4f);

            _splitIsDefaultState = layer.stateMachine.defaultState == _splitResolvedState;
            if (_splitIsDefaultState)
            {
                EditorGUILayout.HelpBox("当前状态是默认状态，分割后需要选择新的默认状态。", MessageType.Warning);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("新的默认状态", GUILayout.Width(80f));
                var options = new[] { "头部", "尾部" };
                var idx = _splitDefaultDesignation == QuickStateService.DefaultStateDesignation.Head ? 0 : 1;
                var newIdx = EditorGUILayout.Popup(idx, options);
                _splitDefaultDesignation = newIdx == 0
                    ? QuickStateService.DefaultStateDesignation.Head
                    : QuickStateService.DefaultStateDesignation.Tail;
                EditorGUILayout.EndHorizontal();
            }
        }

        // 手动调整 Transition：在“头部/尾部”两列间移动进站/Any/尾部出站 Transition
        void DrawSplitAdjustments(AnimatorControllerLayer layer)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            _splitManualAdjust = EditorGUILayout.ToggleLeft("手动调整 Transition", _splitManualAdjust);

            if (!_splitManualAdjust)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            var controller = _controllers[Mathf.Clamp(_selectedControllerIndex, 0, _controllers.Count - 1)];
            var serviceType = typeof(QuickStateService);
            var allStates = serviceType
                .GetMethod("GetAllStatesInLayer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?
                .Invoke(null, new object[] { controller, _selectedLayerIndex }) as List<AnimatorState>;

            // 重建 UI 列表前缓存各 Transition 的 ShouldMove，避免 IMGUI 重绘时勾选状态丢失
            var prevIncoming = new Dictionary<AnimatorStateTransition, bool>();
            foreach (var adj in _splitIncomingAdjustments)
            {
                if (adj.Transition != null && !prevIncoming.ContainsKey(adj.Transition))
                    prevIncoming.Add(adj.Transition, adj.ShouldMove);
            }

            var prevAny = new Dictionary<AnimatorStateTransition, bool>();
            foreach (var adj in _splitAnyAdjustments)
            {
                if (adj.Transition != null && !prevAny.ContainsKey(adj.Transition))
                    prevAny.Add(adj.Transition, adj.ShouldMove);
            }

            var prevOutgoing = new Dictionary<AnimatorStateTransition, bool>();
            foreach (var adj in _splitOutgoingAdjustments)
            {
                if (adj.Transition != null && !prevOutgoing.ContainsKey(adj.Transition))
                    prevOutgoing.Add(adj.Transition, adj.ShouldMove);
            }

            _splitIncomingAdjustments.Clear();
            _splitAnyAdjustments.Clear();
            _splitOutgoingAdjustments.Clear();

            if (allStates != null)
            {
                foreach (var state in allStates)
                {
                    foreach (var t in state.transitions)
                    {
                        if (t.destinationState == _splitResolvedState)
                        {
                            bool shouldMove = prevIncoming.TryGetValue(t, out var v) && v;
                            _splitIncomingAdjustments.Add(new QuickStateService.TransitionAdjustment
                            {
                                Transition = t,
                                ShouldMove = shouldMove,
                                DisplayName = state.name + " -> Head"
                            });
                        }
                    }
                }

                foreach (var t in layer.stateMachine.anyStateTransitions)
                {
                    if (t.destinationState == _splitResolvedState)
                    {
                        bool shouldMove = prevAny.TryGetValue(t, out var v) && v;
                        _splitAnyAdjustments.Add(new QuickStateService.TransitionAdjustment
                        {
                            Transition = t,
                            ShouldMove = shouldMove,
                            DisplayName = "Any State -> Head"
                        });
                    }
                }

                foreach (var t in _splitResolvedState.transitions)
                {
                    bool shouldMove = prevOutgoing.TryGetValue(t, out var v) && v;
                    _splitOutgoingAdjustments.Add(new QuickStateService.TransitionAdjustment
                    {
                        Transition = t,
                        ShouldMove = shouldMove,
                        DisplayName = "Tail -> " + (t.destinationState != null ? t.destinationState.name : t.destinationStateMachine != null ? t.destinationStateMachine.name : "Exit")
                    });
                }
            }

            GUILayout.Space(4f);

            float totalWidth = position.width;
            float columnWidth = Mathf.Max(0f, (totalWidth - 40f) * 0.5f);

            EditorGUILayout.BeginHorizontal();

            GUILayout.Space(4f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(columnWidth), GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField("头部 Transition", EditorStyles.boldLabel);
            _splitScrollIncoming = ToolboxUtils.ScrollView(_splitScrollIncoming, () =>
            {

                for (int i = 0; i < _splitIncomingAdjustments.Count; i++)
                {
                    var adj = _splitIncomingAdjustments[i];
                    bool inHead = !adj.ShouldMove;
                    if (!inHead)
                    {
                        continue;
                    }

                    EditorGUILayout.BeginHorizontal();
                    bool moveToTail = EditorGUILayout.Toggle(false, GUILayout.Width(20f));
                    EditorGUILayout.LabelField(adj.DisplayName);
                    if (moveToTail)
                    {
                        adj.ShouldMove = true; // 移到尾部
                        _splitIncomingAdjustments[i] = adj;
                    }
                    EditorGUILayout.EndHorizontal();
                }

                if (_splitAnyAdjustments.Count > 0)
                {
                    EditorGUILayout.LabelField("Any State", EditorStyles.boldLabel);
                    for (int i = 0; i < _splitAnyAdjustments.Count; i++)
                    {
                        var adj = _splitAnyAdjustments[i];
                        bool inHead = !adj.ShouldMove;
                        if (!inHead)
                        {
                            continue;
                        }

                        EditorGUILayout.BeginHorizontal();
                        bool moveToTail = EditorGUILayout.Toggle(false, GUILayout.Width(20f));
                        EditorGUILayout.LabelField(adj.DisplayName);
                        if (moveToTail)
                        {
                            adj.ShouldMove = true; // 移到尾部
                            _splitAnyAdjustments[i] = adj;
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }


                if (_splitOutgoingAdjustments.Count > 0)
                {
                    for (int i = 0; i < _splitOutgoingAdjustments.Count; i++)
                    {
                        var adj = _splitOutgoingAdjustments[i];
                        bool inHead = adj.ShouldMove;
                        if (!inHead)
                        {
                            continue;
                        }

                        EditorGUILayout.BeginHorizontal();

                        bool keepInHead = EditorGUILayout.Toggle(true, GUILayout.Width(20f));
                        EditorGUILayout.LabelField(adj.DisplayName);
                        if (!keepInHead)
                        {
                            adj.ShouldMove = false;
                            _splitOutgoingAdjustments[i] = adj;
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndVertical();

            GUILayout.Space(4f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(columnWidth), GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField("尾部 Transition", EditorStyles.boldLabel);
            _splitScrollOutgoing = ToolboxUtils.ScrollView(_splitScrollOutgoing, () =>
            {

                for (int i = 0; i < _splitIncomingAdjustments.Count; i++)
                {
                    var adj = _splitIncomingAdjustments[i];
                    bool inTail = adj.ShouldMove;
                    if (!inTail)
                    {
                        continue;
                    }

                    EditorGUILayout.BeginHorizontal();
                    bool keepInTail = EditorGUILayout.Toggle(true, GUILayout.Width(20f));
                    EditorGUILayout.LabelField(adj.DisplayName);
                    if (!keepInTail)
                    {
                        adj.ShouldMove = false; // 移回头部
                        _splitIncomingAdjustments[i] = adj;
                    }
                    EditorGUILayout.EndHorizontal();
                }


                if (_splitAnyAdjustments.Count > 0)
                {
                    EditorGUILayout.LabelField("Any State", EditorStyles.boldLabel);
                    for (int i = 0; i < _splitAnyAdjustments.Count; i++)
                    {
                        var adj = _splitAnyAdjustments[i];
                        bool inTail = adj.ShouldMove;
                        if (!inTail)
                        {
                            continue;
                        }

                        EditorGUILayout.BeginHorizontal();
                        bool keepInTail = EditorGUILayout.Toggle(true, GUILayout.Width(20f));
                        EditorGUILayout.LabelField(adj.DisplayName);
                        if (!keepInTail)
                        {
                            adj.ShouldMove = false; // 移回头部
                            _splitAnyAdjustments[i] = adj;
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }


                if (_splitOutgoingAdjustments.Count > 0)
                {
                    for (int i = 0; i < _splitOutgoingAdjustments.Count; i++)
                    {
                        var adj = _splitOutgoingAdjustments[i];
                        bool inTail = !adj.ShouldMove;
                        if (!inTail)
                        {
                            continue;
                        }

                        EditorGUILayout.BeginHorizontal();
                        bool moveToHead = EditorGUILayout.Toggle(false, GUILayout.Width(20f));
                        EditorGUILayout.LabelField(adj.DisplayName);
                        if (moveToHead)
                        {
                            adj.ShouldMove = true;
                            _splitOutgoingAdjustments[i] = adj;
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndVertical();

            GUILayout.Space(4f);

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        // 分割操作按钮：收集当前 UI 输入并调用 QuickStateService.SplitAnimatorState
        void DrawSplitApplyButton(AnimatorController controller, AnimatorControllerLayer layer)
        {
            bool invalid = _splitResolvedState == null || string.IsNullOrEmpty(_splitHeadName) || string.IsNullOrEmpty(_splitTailName) ||
                           _splitHeadName == _splitTailName || (_splitKeepOriginalAsHead && _splitKeepOriginalAsTail);

            EditorGUI.BeginDisabledGroup(invalid);
            if (GUILayout.Button("执行状态分割", GUILayout.Height(28f)))
            {
                var confirm = EditorUtility.DisplayDialog(
                    "执行状态分割",
                    $"将状态 '{_splitResolvedState.name}' 分割为:\n\n头部: {_splitHeadName}\n尾部: {_splitTailName}",
                    "分割",
                    "取消");
                if (confirm)
                {
                    var headAdjustments = new List<QuickStateService.TransitionAdjustment>(_splitIncomingAdjustments.Count + _splitAnyAdjustments.Count);
                    headAdjustments.AddRange(_splitIncomingAdjustments);
                    headAdjustments.AddRange(_splitAnyAdjustments);

                    QuickStateService.SplitAnimatorState(
                        controller,
                        _selectedLayerIndex,
                        _splitResolvedState,
                        _splitHeadName,
                        _splitTailName,
                        _splitKeepOriginalAsHead,
                        _splitKeepOriginalAsTail,
                        _splitIsDefaultState,
                        _splitDefaultDesignation,
                        headAdjustments,
                        _splitOutgoingAdjustments);

                    ClearSplitSelection();
                }
            }
            EditorGUI.EndDisabledGroup();
        }

        // 合并模式主区域：选择状态 A/B 与保留侧，并调用 QuickStateService.MergeAnimatorStates
        void DrawMergeModeArea()
        {
            var controller = _controllers[Mathf.Clamp(_selectedControllerIndex, 0, _controllers.Count - 1)];
            var layers = controller.layers ?? System.Array.Empty<AnimatorControllerLayer>();
            if (layers.Length == 0)
            {
                return;
            }

            var layer = layers[Mathf.Clamp(_selectedLayerIndex, 0, layers.Length - 1)];
            var stateMachine = layer.stateMachine;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("合并状态", EditorStyles.boldLabel);

            var allPaths = new List<string>();
            var stateByPath = new Dictionary<string, AnimatorState>();
            CollectStatesWithPaths(stateMachine, string.Empty, allPaths, stateByPath);

            if (allPaths.Count < 2)
            {
                EditorGUILayout.HelpBox("当前层中至少需要两个状态才能执行合并。", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            if (!allPaths.Contains(_mergeStateAPath))
            {
                _mergeStateAPath = allPaths[0];
            }

            if (!allPaths.Contains(_mergeStateBPath) || _mergeStateBPath == _mergeStateAPath)
            {
                _mergeStateBPath = allPaths.Find(p => p != _mergeStateAPath);
            }

            _mergeStateAIndex = allPaths.IndexOf(_mergeStateAPath);
            if (_mergeStateAIndex < 0) _mergeStateAIndex = 0;
            _mergeStateAIndex = EditorGUILayout.Popup("状态 A", _mergeStateAIndex, allPaths.ToArray());
            _mergeStateAPath = allPaths[Mathf.Clamp(_mergeStateAIndex, 0, allPaths.Count - 1)];
            _mergeResolvedStateA = stateByPath.TryGetValue(_mergeStateAPath, out var a) ? a : null;

            _mergeStateBIndex = allPaths.IndexOf(_mergeStateBPath);
            if (_mergeStateBIndex < 0) _mergeStateBIndex = 0;
            _mergeStateBIndex = EditorGUILayout.Popup("状态 B", _mergeStateBIndex, allPaths.ToArray());
            _mergeStateBPath = allPaths[Mathf.Clamp(_mergeStateBIndex, 0, allPaths.Count - 1)];
            _mergeResolvedStateB = stateByPath.TryGetValue(_mergeStateBPath, out var b) ? b : null;

            if (_mergeResolvedStateA == null || _mergeResolvedStateB == null || _mergeResolvedStateA == _mergeResolvedStateB)
            {
                EditorGUILayout.HelpBox("请选择两个不同的状态。", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            GUILayout.Space(4f);

            // 使用下拉列表选择保留的状态：状态 A / 状态 B
            int currentKeepIndex = _mergeKeepA ? 0 : 1;
            int newKeepIndex = EditorGUILayout.Popup("保留状态", currentKeepIndex, new[] { "状态 A", "状态 B" });
            if (newKeepIndex != currentKeepIndex)
            {
                _mergeKeepA = newKeepIndex == 0;
                _mergeNewStateName = string.Empty;
            }

            GUILayout.Space(4f);

            var keepState = _mergeKeepA ? _mergeResolvedStateA : _mergeResolvedStateB;
            var keepStateName = keepState != null ? keepState.name : string.Empty;

            // 当保留状态发生变化，且用户尚未自定义名称（名称为空或仍等于上一次保留状态名）时，自动使用当前保留状态名称
            if (keepState != null && (string.IsNullOrEmpty(_mergeNewStateName) || _mergeNewStateName == _mergeLastKeepStateName))
            {
                _mergeNewStateName = keepStateName;
            }

            _mergeNewStateName = EditorGUILayout.TextField("新状态名称", _mergeNewStateName);

            // 记录本帧保留状态的原始名称，用于下一帧判断是否需要自动刷新
            _mergeLastKeepStateName = keepStateName;

            GUILayout.Space(4f);

            bool invalid = string.IsNullOrEmpty(_mergeNewStateName);
            EditorGUI.BeginDisabledGroup(invalid);
            if (GUILayout.Button("执行状态合并", GUILayout.Height(28f)))
            {
                var stateToKeep = _mergeKeepA ? _mergeResolvedStateA : _mergeResolvedStateB;
                var stateToRemove = _mergeKeepA ? _mergeResolvedStateB : _mergeResolvedStateA;

                var confirm = EditorUtility.DisplayDialog(
                    "执行状态合并",
                    $"将状态 '{stateToRemove.name}' 合并到 '{stateToKeep.name}' 中，\n\n新状态名称：{_mergeNewStateName}",
                    "合并",
                    "取消");
                if (confirm)
                {
                    QuickStateService.MergeAnimatorStates(
                        controller,
                        _selectedLayerIndex,
                        stateToKeep,
                        stateToRemove,
                        _mergeNewStateName);

                    ClearMergeSelection();
                }
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndVertical();
        }

        void EnsureResolvedStatesValid()
        {
            if (_splitResolvedState == null && !string.IsNullOrEmpty(_splitSelectedStatePath))
            {
                _splitSelectedStatePath = string.Empty;
                _splitSelectedStateIndex = 0;
                _splitResolvedState = null;
            }

            if (_mergeResolvedStateA == null && !string.IsNullOrEmpty(_mergeStateAPath))
            {
                _mergeStateAPath = string.Empty;
                _mergeStateAIndex = 0;
                _mergeResolvedStateA = null;
            }

            if (_mergeResolvedStateB == null && !string.IsNullOrEmpty(_mergeStateBPath))
            {
                _mergeStateBPath = string.Empty;
                _mergeStateBIndex = 0;
                _mergeResolvedStateB = null;
            }
        }

        void ClearSplitSelection()
        {
            _splitSelectedStatePath = string.Empty;
            _splitSelectedStateIndex = 0;
            _splitResolvedState = null;
            _splitHeadName = string.Empty;
            _splitTailName = string.Empty;
            _splitKeepOriginalAsHead = false;
            _splitKeepOriginalAsTail = false;
            _splitManualAdjust = false;
            _splitIsDefaultState = false;
            _splitIncomingAdjustments.Clear();
            _splitAnyAdjustments.Clear();
            _splitOutgoingAdjustments.Clear();
        }

        void ClearMergeSelection()
        {
            _mergeStateAPath = string.Empty;
            _mergeStateAIndex = 0;
            _mergeResolvedStateA = null;
            _mergeStateBPath = string.Empty;
            _mergeStateBIndex = 0;
            _mergeResolvedStateB = null;
            _mergeNewStateName = string.Empty;
            _mergeKeepA = true;
        }

        static void CollectStatesWithPaths(AnimatorStateMachine stateMachine, string parentPath, List<string> paths, Dictionary<string, AnimatorState> map)
        {
            if (stateMachine == null)
            {
                return;
            }

            foreach (var child in stateMachine.states)
            {
                if (child.state == null) continue;

                string path = string.IsNullOrEmpty(parentPath)
                    ? child.state.name
                    : parentPath + "/" + child.state.name;

                if (!paths.Contains(path))
                {
                    paths.Add(path);
                }

                if (!map.ContainsKey(path))
                {
                    map.Add(path, child.state);
                }
            }

            foreach (var sub in stateMachine.stateMachines)
            {
                if (sub.stateMachine == null) continue;

                string subPath = string.IsNullOrEmpty(parentPath)
                    ? sub.stateMachine.name
                    : parentPath + "/" + sub.stateMachine.name;

                CollectStatesWithPaths(sub.stateMachine, subPath, paths, map);
            }
        }
    }
}
