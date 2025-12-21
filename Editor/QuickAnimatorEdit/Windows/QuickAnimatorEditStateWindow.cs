using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using MVA.Toolbox.QuickAnimatorEdit.Services.Shared;
using MVA.Toolbox.QuickAnimatorEdit.Services.State;

namespace MVA.Toolbox.QuickAnimatorEdit.Windows
{
    /// <summary>
    /// 状态功能面板
    /// 子功能：拆分状态 / 合并状态
    /// </summary>
    public sealed class QuickAnimatorEditStateWindow
    {
        private enum StateMode
        {
            Split,
            Merge
        }

        private StateMode _mode = StateMode.Split;
        private QuickAnimatorEditContext _context;

        // 状态列表缓存
        private AnimatorController _lastController;
        private int _lastLayerIndex = -1;
        private List<string> _displayPaths = new List<string>();  // 用于UI显示的路径（使用 " > " 分隔）
        private List<string> _actualPaths = new List<string>();    // 用于查找的实际路径（已转义）
        private Dictionary<string, AnimatorState> _stateByDisplayPath = new Dictionary<string, AnimatorState>();

        // 拆分功能状态
        private string _splitSelectedStatePath = string.Empty;
        private int _splitSelectedStateIndex;
        private AnimatorState _splitResolvedState;
        private string _splitHeadName = string.Empty;
        private string _splitTailName = string.Empty;
        private bool _splitKeepOriginalAsHead;
        private bool _splitKeepOriginalAsTail;
        private bool _splitManualAdjust;
        private bool _splitIsDefaultState;
        private StateSplitService.DefaultStateDesignation _splitDefaultDesignation = StateSplitService.DefaultStateDesignation.Head;
        
        private readonly List<StateSplitService.TransitionAdjustment> _splitIncomingAdjustments = new List<StateSplitService.TransitionAdjustment>();
        private readonly List<StateSplitService.TransitionAdjustment> _splitAnyAdjustments = new List<StateSplitService.TransitionAdjustment>();
        private readonly List<StateSplitService.TransitionAdjustment> _splitOutgoingAdjustments = new List<StateSplitService.TransitionAdjustment>();
        
        private Vector2 _splitScrollIncoming;
        private Vector2 _splitScrollOutgoing;

        // 合并功能状态
        private string _mergeStateAPath = string.Empty;
        private int _mergeStateAIndex;
        private AnimatorState _mergeResolvedStateA;
        private string _mergeStateBPath = string.Empty;
        private int _mergeStateBIndex;
        private AnimatorState _mergeResolvedStateB;
        private string _mergeNewStateName = string.Empty;
        private string _mergeLastKeepStateName = string.Empty;
        private bool _mergeKeepA = true;

        public QuickAnimatorEditStateWindow(QuickAnimatorEditContext context)
        {
            _context = context;
        }

        /// <summary>
        /// 绘制状态功能面板
        /// </summary>
        public void OnGUI()
        {
            EnsureResolvedStatesValid();
            
            // 检查并刷新状态列表
            var currentController = _context.SelectedController;
            var currentLayerIndex = _context.SelectedLayerIndex;
            if (currentController != _lastController || currentLayerIndex != _lastLayerIndex)
            {
                RefreshStateList(_context.SelectedLayer);
                _lastController = currentController;
                _lastLayerIndex = currentLayerIndex;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("模式", EditorStyles.boldLabel);

            var modeLabels = new[] { "分割状态", "合并状态" };
            var newMode = (StateMode)GUILayout.Toolbar((int)_mode, modeLabels);
            if (newMode != _mode)
            {
                _mode = newMode;
                ClearSplitSelection();
                ClearMergeSelection();
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4f);

            if (_displayPaths.Count == 0)
            {
                EditorGUILayout.HelpBox("当前层级中没有找到任何状态。", MessageType.Warning);
            }
            else
            {
                switch (_mode)
                {
                    case StateMode.Split:
                        DrawSplitUI();
                        break;
                    case StateMode.Merge:
                        DrawMergeUI();
                        break;
                }
            }
        }

        private void RefreshStateList(AnimatorControllerLayer layer)
        {
            _displayPaths.Clear();
            _actualPaths.Clear();
            _stateByDisplayPath.Clear();

            if (layer != null && layer.stateMachine != null)
            {
                // 使用共享方法收集状态，状态模式下不将子状态机作为可选项
                AnimatorPathUtility.CollectHierarchy(
                    layer.stateMachine,
                    string.Empty,
                    string.Empty,
                    _displayPaths,
                    _actualPaths,
                    null, // displayToActualPath
                    _stateByDisplayPath,
                    null, // stateMachineByDisplayPath
                    null, // isStateMachineMap
                    includeStates: true,
                    machinesAreSelectable: false
                );
            }
            
            ClearSplitSelection();
            ClearMergeSelection();
        }


        private void DrawSplitUI()
        {
            var layer = _context.SelectedLayer;
            if (layer == null || layer.stateMachine == null) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("分割状态", EditorStyles.boldLabel);

            // 状态选择
            DrawSplitStateSelection(layer);

            GUILayout.Space(4f);

            if (_splitResolvedState != null)
            {
                DrawSplitNameAndDefaultOptions(layer);
                GUILayout.Space(4f);
                DrawSplitAdjustments(layer);
                GUILayout.Space(4f);
                DrawSplitApplyButton(layer);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSplitStateSelection(AnimatorControllerLayer layer)
        {
            if (_displayPaths.Count == 0)
            {
                EditorGUILayout.HelpBox("当前层中没有可用的状态。", MessageType.Warning);
                return;
            }

            // 确保选中的路径有效
            if (string.IsNullOrEmpty(_splitSelectedStatePath) || !_displayPaths.Contains(_splitSelectedStatePath))
            {
                _splitSelectedStatePath = _displayPaths[0];
                _splitSelectedStateIndex = 0;
            }

            // 获取当前索引
            _splitSelectedStateIndex = _displayPaths.IndexOf(_splitSelectedStatePath);
            if (_splitSelectedStateIndex < 0) _splitSelectedStateIndex = 0;

            int oldIndex = _splitSelectedStateIndex;
            
            // 显示下拉框
            int newIndex = EditorGUILayout.Popup("目标状态", _splitSelectedStateIndex, _displayPaths.ToArray());
            
            // 检测索引是否变化
            if (newIndex != _splitSelectedStateIndex)
            {
                _splitSelectedStateIndex = newIndex;
                
                // 更新选中的路径
                if (_splitSelectedStateIndex >= 0 && _splitSelectedStateIndex < _displayPaths.Count)
                {
                    _splitSelectedStatePath = _displayPaths[_splitSelectedStateIndex];
                }
            }

            // 强制更新 resolved state - 直接从字典中获取
            _splitResolvedState = _stateByDisplayPath.TryGetValue(_splitSelectedStatePath, out var state) ? state : null;

            // 切换状态时重置设置
            if (_splitSelectedStateIndex != oldIndex)
            {
                _splitHeadName = string.Empty;
                _splitTailName = string.Empty;
                _splitKeepOriginalAsHead = false;
                _splitKeepOriginalAsTail = false;
                _splitIsDefaultState = false;
                _splitDefaultDesignation = StateSplitService.DefaultStateDesignation.Head;
                _splitIncomingAdjustments.Clear();
                _splitAnyAdjustments.Clear();
                _splitOutgoingAdjustments.Clear();
            }
        }

        private void DrawSplitNameAndDefaultOptions(AnimatorControllerLayer layer)
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

            // Head 状态
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

            // Tail 状态
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

            // 默认状态处理
            _splitIsDefaultState = layer.stateMachine.defaultState == _splitResolvedState;
            if (_splitIsDefaultState)
            {
                EditorGUILayout.HelpBox("当前状态是默认状态，分割后需要选择新的默认状态。", MessageType.Warning);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("新的默认状态", GUILayout.Width(80f));
                var options = new[] { "头部", "尾部" };
                var idx = _splitDefaultDesignation == StateSplitService.DefaultStateDesignation.Head ? 0 : 1;
                var newIdx = EditorGUILayout.Popup(idx, options);
                _splitDefaultDesignation = newIdx == 0
                    ? StateSplitService.DefaultStateDesignation.Head
                    : StateSplitService.DefaultStateDesignation.Tail;
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawSplitAdjustments(AnimatorControllerLayer layer)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _splitManualAdjust = EditorGUILayout.ToggleLeft("手动调整 Transition", _splitManualAdjust);

            if (!_splitManualAdjust)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            if (_splitResolvedState == null)
            {
                EditorGUILayout.HelpBox("请先选择一个有效的状态。", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            var allStates = StateSplitService.GetAllStatesInLayer(_context.SelectedController, _context.SelectedLayerIndex);

            // 缓存当前状态
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

            // 收集当前 Transition
            if (allStates != null && _splitResolvedState != null)
            {
                foreach (var state in allStates)
                {
                    if (state == null || state.transitions == null) continue;
                    
                    foreach (var t in state.transitions)
                    {
                        if (t != null && t.destinationState == _splitResolvedState)
                        {
                            bool shouldMove = prevIncoming.TryGetValue(t, out var v) && v;
                            _splitIncomingAdjustments.Add(new StateSplitService.TransitionAdjustment
                            {
                                Transition = t,
                                ShouldMove = shouldMove,
                                DisplayName = state.name + " -> Head"
                            });
                        }
                    }
                }

                if (layer.stateMachine.anyStateTransitions != null)
                {
                    foreach (var t in layer.stateMachine.anyStateTransitions)
                    {
                        if (t != null && t.destinationState == _splitResolvedState)
                        {
                            bool shouldMove = prevAny.TryGetValue(t, out var v) && v;
                            _splitAnyAdjustments.Add(new StateSplitService.TransitionAdjustment
                            {
                                Transition = t,
                                ShouldMove = shouldMove,
                                DisplayName = "Any State -> Head"
                            });
                        }
                    }
                }

                if (_splitResolvedState.transitions != null)
                {
                    foreach (var t in _splitResolvedState.transitions)
                    {
                        if (t == null) continue;
                        
                        bool shouldMove = prevOutgoing.TryGetValue(t, out var v) && v;
                        string destName = t.destinationState != null ? t.destinationState.name 
                                        : t.destinationStateMachine != null ? t.destinationStateMachine.name 
                                        : "Exit";
                        _splitOutgoingAdjustments.Add(new StateSplitService.TransitionAdjustment
                        {
                            Transition = t,
                            ShouldMove = shouldMove,
                            DisplayName = "Tail -> " + destName
                        });
                    }
                }
            }

            GUILayout.Space(4f);

            float columnWidth = Mathf.Max(0f, (EditorGUIUtility.currentViewWidth - 40f) * 0.5f);

            EditorGUILayout.BeginHorizontal();

            GUILayout.Space(4f);

            // 头部列
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(columnWidth), GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField("头部 Transition", EditorStyles.boldLabel);
            _splitScrollIncoming = EditorGUILayout.BeginScrollView(_splitScrollIncoming, GUILayout.ExpandHeight(true));

            // 入站 Transition（默认在头部，ShouldMove=false 表示留在头部）
            for (int i = 0; i < _splitIncomingAdjustments.Count; i++)
            {
                var adj = _splitIncomingAdjustments[i];
                bool inHead = !adj.ShouldMove;
                if (!inHead) continue;

                EditorGUILayout.BeginHorizontal();
                bool moveToTail = EditorGUILayout.Toggle(false, GUILayout.Width(20f));
                EditorGUILayout.LabelField(adj.DisplayName);
                if (moveToTail)
                {
                    adj.ShouldMove = true;
                    _splitIncomingAdjustments[i] = adj;
                }
                EditorGUILayout.EndHorizontal();
            }

            // Any State Transition（默认在头部）
            if (_splitAnyAdjustments.Count > 0)
            {
                EditorGUILayout.LabelField("Any State", EditorStyles.boldLabel);
                for (int i = 0; i < _splitAnyAdjustments.Count; i++)
                {
                    var adj = _splitAnyAdjustments[i];
                    bool inHead = !adj.ShouldMove;
                    if (!inHead) continue;

                    EditorGUILayout.BeginHorizontal();
                    bool moveToTail = EditorGUILayout.Toggle(false, GUILayout.Width(20f));
                    EditorGUILayout.LabelField(adj.DisplayName);
                    if (moveToTail)
                    {
                        adj.ShouldMove = true;
                        _splitAnyAdjustments[i] = adj;
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            // 出站 Transition（默认在尾部，ShouldMove=true 表示移到头部）
            if (_splitOutgoingAdjustments.Count > 0)
            {
                for (int i = 0; i < _splitOutgoingAdjustments.Count; i++)
                {
                    var adj = _splitOutgoingAdjustments[i];
                    bool inHead = adj.ShouldMove;
                    if (!inHead) continue;

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

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            GUILayout.Space(4f);

            // 尾部列
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(columnWidth), GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField("尾部 Transition", EditorStyles.boldLabel);
            _splitScrollOutgoing = EditorGUILayout.BeginScrollView(_splitScrollOutgoing, GUILayout.ExpandHeight(true));

            // 入站 Transition（已移到尾部的）
            for (int i = 0; i < _splitIncomingAdjustments.Count; i++)
            {
                var adj = _splitIncomingAdjustments[i];
                bool inTail = adj.ShouldMove;
                if (!inTail) continue;

                EditorGUILayout.BeginHorizontal();
                bool keepInTail = EditorGUILayout.Toggle(true, GUILayout.Width(20f));
                EditorGUILayout.LabelField(adj.DisplayName);
                if (!keepInTail)
                {
                    adj.ShouldMove = false;
                    _splitIncomingAdjustments[i] = adj;
                }
                EditorGUILayout.EndHorizontal();
            }

            // Any State Transition（已移到尾部的）
            if (_splitAnyAdjustments.Count > 0)
            {
                EditorGUILayout.LabelField("Any State", EditorStyles.boldLabel);
                for (int i = 0; i < _splitAnyAdjustments.Count; i++)
                {
                    var adj = _splitAnyAdjustments[i];
                    bool inTail = adj.ShouldMove;
                    if (!inTail) continue;

                    EditorGUILayout.BeginHorizontal();
                    bool keepInTail = EditorGUILayout.Toggle(true, GUILayout.Width(20f));
                    EditorGUILayout.LabelField(adj.DisplayName);
                    if (!keepInTail)
                    {
                        adj.ShouldMove = false;
                        _splitAnyAdjustments[i] = adj;
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            // 出站 Transition（默认在尾部的）
            if (_splitOutgoingAdjustments.Count > 0)
            {
                for (int i = 0; i < _splitOutgoingAdjustments.Count; i++)
                {
                    var adj = _splitOutgoingAdjustments[i];
                    bool inTail = !adj.ShouldMove;
                    if (!inTail) continue;

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

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            GUILayout.Space(4f);

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawSplitApplyButton(AnimatorControllerLayer layer)
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
                    var headAdjustments = new List<StateSplitService.TransitionAdjustment>(_splitIncomingAdjustments.Count + _splitAnyAdjustments.Count);
                    headAdjustments.AddRange(_splitIncomingAdjustments);
                    headAdjustments.AddRange(_splitAnyAdjustments);

                    StateSplitService.Execute(
                        _context.SelectedController,
                        _context.SelectedLayerIndex,
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
                    RefreshStateList(layer);
                }
            }
            EditorGUI.EndDisabledGroup();
        }

        private void DrawMergeUI()
        {
            var layer = _context.SelectedLayer;
            if (layer == null || layer.stateMachine == null) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("合并状态", EditorStyles.boldLabel);

            if (_displayPaths.Count < 2)
            {
                EditorGUILayout.HelpBox("当前层中至少需要两个状态才能执行合并。", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            // 确保路径有效
            if (!_displayPaths.Contains(_mergeStateAPath))
            {
                _mergeStateAPath = _displayPaths[0];
                _mergeStateAIndex = 0;
            }

            if (!_displayPaths.Contains(_mergeStateBPath) || _mergeStateBPath == _mergeStateAPath)
            {
                _mergeStateBPath = _displayPaths.Find(p => p != _mergeStateAPath);
                if (string.IsNullOrEmpty(_mergeStateBPath) && _displayPaths.Count > 1)
                {
                    _mergeStateBPath = _displayPaths[1];
                }
            }

            // 状态 A 选择
            _mergeStateAIndex = _displayPaths.IndexOf(_mergeStateAPath);
            if (_mergeStateAIndex < 0) _mergeStateAIndex = 0;
            
            int newIndexA = EditorGUILayout.Popup("状态 A", _mergeStateAIndex, _displayPaths.ToArray());
            
            if (newIndexA != _mergeStateAIndex)
            {
                _mergeStateAIndex = newIndexA;
                if (_mergeStateAIndex >= 0 && _mergeStateAIndex < _displayPaths.Count)
                {
                    _mergeStateAPath = _displayPaths[_mergeStateAIndex];
                }
            }
            
            // 直接从字典中获取状态
            _mergeResolvedStateA = _stateByDisplayPath.TryGetValue(_mergeStateAPath, out var stateA) ? stateA : null;

            // 状态 B 选择
            _mergeStateBIndex = _displayPaths.IndexOf(_mergeStateBPath);
            if (_mergeStateBIndex < 0) _mergeStateBIndex = 0;
            
            int newIndexB = EditorGUILayout.Popup("状态 B", _mergeStateBIndex, _displayPaths.ToArray());
            
            if (newIndexB != _mergeStateBIndex)
            {
                _mergeStateBIndex = newIndexB;
                if (_mergeStateBIndex >= 0 && _mergeStateBIndex < _displayPaths.Count)
                {
                    _mergeStateBPath = _displayPaths[_mergeStateBIndex];
                }
            }
            
            // 直接从字典中获取状态
            _mergeResolvedStateB = _stateByDisplayPath.TryGetValue(_mergeStateBPath, out var stateB) ? stateB : null;

            if (_mergeResolvedStateA == null || _mergeResolvedStateB == null || _mergeResolvedStateA == _mergeResolvedStateB)
            {
                EditorGUILayout.HelpBox("请选择两个不同的状态。", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            GUILayout.Space(4f);

            // 保留状态选择
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

            // 自动名称管理
            if (keepState != null && (string.IsNullOrEmpty(_mergeNewStateName) || _mergeNewStateName == _mergeLastKeepStateName))
            {
                _mergeNewStateName = keepStateName;
            }

            _mergeNewStateName = EditorGUILayout.TextField("新状态名称", _mergeNewStateName);
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
                    StateMergeService.Execute(
                        _context.SelectedController,
                        _context.SelectedLayerIndex,
                        stateToKeep,
                        stateToRemove,
                        _mergeNewStateName);

                    ClearMergeSelection();
                    RefreshStateList(layer);
                }
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndVertical();
        }

        private void EnsureResolvedStatesValid()
        {
            // 之前的逻辑：只要 resolvedState 为空就清空路径。
            // 这段代码原本用于避免“引用失效”的旧状态对象，但在 Unity 的 OnGUI 刷新节奏下，
            // 很容易导致每帧都把选择重置掉，从而表现为下拉框无法切换/界面不可操作。

            if (!string.IsNullOrEmpty(_splitSelectedStatePath))
            {
                if (_displayPaths.Contains(_splitSelectedStatePath))
                {
                    _splitSelectedStateIndex = _displayPaths.IndexOf(_splitSelectedStatePath);
                    _splitResolvedState = _stateByDisplayPath.TryGetValue(_splitSelectedStatePath, out var s) ? s : null;
                }
                else
                {
                    _splitSelectedStatePath = string.Empty;
                    _splitSelectedStateIndex = 0;
                    _splitResolvedState = null;
                }
            }

            if (!string.IsNullOrEmpty(_mergeStateAPath))
            {
                if (_displayPaths.Contains(_mergeStateAPath))
                {
                    _mergeStateAIndex = _displayPaths.IndexOf(_mergeStateAPath);
                    _mergeResolvedStateA = _stateByDisplayPath.TryGetValue(_mergeStateAPath, out var a) ? a : null;
                }
                else
                {
                    _mergeStateAPath = string.Empty;
                    _mergeStateAIndex = 0;
                    _mergeResolvedStateA = null;
                }
            }

            if (!string.IsNullOrEmpty(_mergeStateBPath))
            {
                if (_displayPaths.Contains(_mergeStateBPath))
                {
                    _mergeStateBIndex = _displayPaths.IndexOf(_mergeStateBPath);
                    _mergeResolvedStateB = _stateByDisplayPath.TryGetValue(_mergeStateBPath, out var b) ? b : null;
                }
                else
                {
                    _mergeStateBPath = string.Empty;
                    _mergeStateBIndex = 0;
                    _mergeResolvedStateB = null;
                }
            }
        }

        private void ClearSplitSelection()
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

        private void ClearMergeSelection()
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
    }
}
