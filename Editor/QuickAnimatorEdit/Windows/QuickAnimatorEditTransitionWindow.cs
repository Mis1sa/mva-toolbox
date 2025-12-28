using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using MVA.Toolbox.QuickAnimatorEdit.Services.Shared;
using MVA.Toolbox.QuickAnimatorEdit.Services.Transition;

namespace MVA.Toolbox.QuickAnimatorEdit.Windows
{
    /// <summary>
    /// 过渡功能面板
    /// 子功能：创建过渡 / 批量修改过渡
    /// </summary>
    public sealed class QuickAnimatorEditTransitionWindow
    {
        private enum TransitionMode
        {
            Create,
            Modify
        }

        private class ConditionDeltaSettingUI
        {
            public enum ConditionOp
            {
                Append,
                AddUnique,
                Remove
            }

            public string parameterName;
            public AnimatorConditionMode mode;
            public float threshold;
            public bool removeAllForParameter;
            public ConditionOp operation = ConditionOp.Append;
            public bool ignoreCondition = true;
            public bool requestRemove;

            private int _selectedParameterIndex;

            private static readonly string[] OpLabels = { "追加", "增加(去重)", "移除" };
            private static readonly string[] BoolModes = { "True", "False" };
            private static readonly string[] BoolModesWithAll = { "True", "False", "全部" };
            private static readonly string[] FloatModes = { "Greater", "Less" };
            private static readonly string[] FloatModesWithAll = { "Greater", "Less", "全部" };
            private static readonly string[] IntModes = { "Greater", "Less", "Equals", "NotEquals" };
            private static readonly string[] IntModesWithAll = { "Greater", "Less", "Equals", "NotEquals", "全部" };

            public void Draw(AnimatorController controller)
            {
                var parameters = controller.parameters;
                var availableNames = new List<string>();
                foreach (var p in parameters)
                {
                    if (p != null && p.type != AnimatorControllerParameterType.Trigger)
                    {
                        availableNames.Add(p.name);
                    }
                }

                if (availableNames.Count == 0)
                {
                    EditorGUILayout.HelpBox("未找到非 Trigger 类型的参数。", MessageType.Info);
                    return;
                }

                if (string.IsNullOrEmpty(parameterName))
                {
                    parameterName = availableNames[0];
                }

                int currentIndex = availableNames.IndexOf(parameterName);
                if (currentIndex >= 0)
                {
                    _selectedParameterIndex = currentIndex;
                }

                EditorGUILayout.BeginHorizontal();

                int newIndex = EditorGUILayout.Popup(_selectedParameterIndex, availableNames.ToArray(), GUILayout.Width(150f));
                if (newIndex != _selectedParameterIndex && newIndex >= 0 && newIndex < availableNames.Count)
                {
                    _selectedParameterIndex = newIndex;
                    parameterName = availableNames[_selectedParameterIndex];
                    removeAllForParameter = false;
                }

                AnimatorControllerParameter selectedParam = null;
                foreach (var p in parameters)
                {
                    if (p != null && p.name == parameterName)
                    {
                        selectedParam = p;
                        break;
                    }
                }

                int opIndex = operation == ConditionOp.AddUnique ? 1 : operation == ConditionOp.Remove ? 2 : 0;
                int newOpIndex = EditorGUILayout.Popup(opIndex, OpLabels, GUILayout.Width(100f));
                var newOp = newOpIndex == 1 ? ConditionOp.AddUnique : newOpIndex == 2 ? ConditionOp.Remove : ConditionOp.Append;
                if (newOp != operation)
                {
                    operation = newOp;
                    removeAllForParameter = false;
                }

                if (operation == ConditionOp.AddUnique)
                {
                    ignoreCondition = EditorGUILayout.ToggleLeft("忽略条件", ignoreCondition, GUILayout.Width(90f));
                }

                if (selectedParam != null)
                {
                    switch (selectedParam.type)
                    {
                        case AnimatorControllerParameterType.Bool:
                        {
                            bool includeAll = operation == ConditionOp.Remove;
                            var labels = includeAll ? BoolModesWithAll : BoolModes;
                            int modeIndex;

                            if (includeAll && removeAllForParameter)
                            {
                                modeIndex = labels.Length - 1;
                            }
                            else
                            {
                                modeIndex = mode == AnimatorConditionMode.If ? 0 : 1;
                            }

                            int newModeIndex = EditorGUILayout.Popup(modeIndex, labels, GUILayout.Width(80f));
                            if (includeAll && newModeIndex == labels.Length - 1)
                            {
                                removeAllForParameter = true;
                                mode = AnimatorConditionMode.If;
                            }
                            else
                            {
                                removeAllForParameter = false;
                                mode = newModeIndex == 0 ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot;
                            }

                            float valueFieldWidth = 60f;
                            GUILayout.Label(string.Empty, GUILayout.Width(valueFieldWidth));
                            break;
                        }
                        case AnimatorControllerParameterType.Float:
                        {
                            bool includeAll = operation == ConditionOp.Remove;
                            var labels = includeAll ? FloatModesWithAll : FloatModes;
                            int modeIndex;

                            if (includeAll && removeAllForParameter)
                            {
                                modeIndex = labels.Length - 1;
                            }
                            else
                            {
                                modeIndex = mode == AnimatorConditionMode.Less ? 1 : 0;
                            }

                            int newModeIndex = EditorGUILayout.Popup(modeIndex, labels, GUILayout.Width(80f));
                            if (includeAll && newModeIndex == labels.Length - 1)
                            {
                                removeAllForParameter = true;
                                mode = AnimatorConditionMode.Greater;
                            }
                            else
                            {
                                removeAllForParameter = false;
                                mode = newModeIndex == 0 ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less;
                            }

                            threshold = EditorGUILayout.FloatField(threshold, GUILayout.Width(60f));
                            break;
                        }
                        case AnimatorControllerParameterType.Int:
                        {
                            bool includeAll = operation == ConditionOp.Remove;
                            var labels = includeAll ? IntModesWithAll : IntModes;
                            int modeIndex;

                            if (includeAll && removeAllForParameter)
                            {
                                modeIndex = labels.Length - 1;
                            }
                            else
                            {
                                modeIndex = 0;
                                if (mode == AnimatorConditionMode.Less) modeIndex = 1;
                                else if (mode == AnimatorConditionMode.Equals) modeIndex = 2;
                                else if (mode == AnimatorConditionMode.NotEqual) modeIndex = 3;
                            }

                            int newModeIndex = EditorGUILayout.Popup(modeIndex, labels, GUILayout.Width(80f));
                            if (includeAll && newModeIndex == labels.Length - 1)
                            {
                                removeAllForParameter = true;
                                mode = AnimatorConditionMode.Greater;
                            }
                            else
                            {
                                removeAllForParameter = false;
                                switch (newModeIndex)
                                {
                                    case 0: mode = AnimatorConditionMode.Greater; break;
                                    case 1: mode = AnimatorConditionMode.Less; break;
                                    case 2: mode = AnimatorConditionMode.Equals; break;
                                    case 3: mode = AnimatorConditionMode.NotEqual; break;
                                }
                            }

                            int newThreshold = EditorGUILayout.IntField((int)threshold, GUILayout.Width(60f));
                            if (newThreshold < 0) newThreshold = 0;
                            threshold = newThreshold;
                            break;
                        }
                    }
                }

                GUILayout.FlexibleSpace();
                float removeButtonSize = EditorGUIUtility.singleLineHeight;
                if (GUILayout.Button("-", GUILayout.Width(removeButtonSize), GUILayout.Height(removeButtonSize)))
                {
                    requestRemove = true;
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private TransitionMode _mode = TransitionMode.Create;
        private QuickAnimatorEditContext _context;

        // Common
        private AnimatorController _lastController;
        private int _lastLayerIndex = -1;
        private List<string> _displayPaths = new List<string>();  // 用于UI显示的路径
        private List<string> _actualPaths = new List<string>();    // 用于查找的实际路径
        private Dictionary<string, string> _displayToActualPath = new Dictionary<string, string>();  // 显示路径到实际路径的映射
        private Dictionary<string, bool> _isStateMachineMap = new Dictionary<string, bool>();  // 记录是否为子状态机
        private Dictionary<string, AnimatorState> _stateByDisplayPath = new Dictionary<string, AnimatorState>();  // 显示路径到状态的映射
        private Dictionary<string, AnimatorStateMachine> _stateMachineByDisplayPath = new Dictionary<string, AnimatorStateMachine>();  // 显示路径到子状态机的映射

        // Create Mode
        private string _createSourceState = "Any State";
        private string _createDestState;
        
        private bool _defaultHasExitTime = true;
        private float _defaultExitTime = 0.75f;
        private bool _defaultHasFixedDuration = true;
        private float _defaultDuration = 0.25f;
        private float _defaultOffset = 0f;
        private bool _defaultCanTransitionToSelf = true;

        private bool _overrideHasExitTime;
        private bool _overrideExitTime;
        private bool _overrideHasFixedDuration;
        private bool _overrideDuration;
        private bool _overrideOffset;
        private bool _overrideCanTransitionToSelf;

        private List<CreateTransitionItem> _createItems = new List<CreateTransitionItem>();
        private List<ConditionUI> _globalConditions = new List<ConditionUI>();

        // Modify Mode
        private TransitionModifyService.ModifyMode _modifyMode = TransitionModifyService.ModifyMode.FromStateTransitions;
        private int _modifyStateIndex = 0;

        private enum ModifyActionMode
        {
            ModifyTransitions,
            ConditionDelta
        }

        private ModifyActionMode _modifyActionMode = ModifyActionMode.ModifyTransitions;
        
        private bool _modifyHasExitTimeValue = true;
        private float _modifyExitTimeValue = 0.75f;
        private bool _modifyHasFixedDurationValue = true;
        private float _modifyDurationValue = 0.25f;
        private float _modifyOffsetValue = 0f;
        
        private bool _modifyConditions = false;
        private List<ModifyConditionSettingUI> _modifyConditionSettings = new List<ModifyConditionSettingUI>();

        private List<ConditionDeltaSettingUI> _conditionDeltaSettings = new List<ConditionDeltaSettingUI>();

        public QuickAnimatorEditTransitionWindow(QuickAnimatorEditContext context)
        {
            _context = context;
        }

        public void OnGUI()
        {
            // Check controller/layer change
            var currentController = _context.SelectedController;
            var currentLayerIndex = _context.SelectedLayerIndex;
            if (currentController != _lastController || currentLayerIndex != _lastLayerIndex)
            {
                RefreshStateList(_context.SelectedLayer);
                _lastController = currentController;
                _lastLayerIndex = currentLayerIndex;
            }

            // 模式选择区域
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("模式", EditorStyles.boldLabel);

            var modeLabels = new[] { "创建过渡", "修改过渡" };
            _mode = (TransitionMode)GUILayout.Toolbar((int)_mode, modeLabels);

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4f);

            // 工作区域
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (_displayPaths.Count == 0 && _mode == TransitionMode.Modify)
            {
                if (_context.SelectedController == null)
                    EditorGUILayout.HelpBox("请先选择控制器。", MessageType.Warning);
                else
                    EditorGUILayout.HelpBox("当前层级中没有找到任何状态。", MessageType.Warning);
            }
            else
            {
                switch (_mode)
                {
                    case TransitionMode.Create:
                        DrawCreateUI();
                        break;
                    case TransitionMode.Modify:
                        DrawModifyUI();
                        break;
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void RefreshStateList(AnimatorControllerLayer layer)
        {
            _displayPaths.Clear();
            _actualPaths.Clear();
            _displayToActualPath.Clear();
            _isStateMachineMap.Clear();
            _stateByDisplayPath.Clear();
            _stateMachineByDisplayPath.Clear();

            if (layer != null && layer.stateMachine != null)
            {
                // 使用共享方法收集状态和子状态机
                AnimatorPathUtility.CollectHierarchy(
                    layer.stateMachine, 
                    string.Empty, 
                    string.Empty, 
                    _displayPaths, 
                    _actualPaths, 
                    _displayToActualPath, 
                    _stateByDisplayPath, 
                    _stateMachineByDisplayPath, 
                    _isStateMachineMap, 
                    includeStates: true, 
                    machinesAreSelectable: true
                );
            }

            // 刷新后保持/修正索引，避免下拉框每帧被重置
            _modifyStateIndex = Mathf.Clamp(_modifyStateIndex, 0, Mathf.Max(0, _displayPaths.Count - 1));
        }


        #region Create Mode

        private class CreateTransitionItem
        {
            public bool overrideHasExitTime;
            public bool hasExitTime;
            public bool overrideExitTime;
            public float exitTime;
            public bool overrideHasFixedDuration;
            public bool hasFixedDuration;
            public bool overrideDuration;
            public float duration;
            public bool overrideOffset;
            public float offset;
            public bool overrideCanTransitionToSelf;
            public bool canTransitionToSelf;

            public List<ConditionUI> conditions = new List<ConditionUI>();
        }

        private class ConditionUI
        {
            public string parameterName;
            public AnimatorControllerParameterType parameterType;
            public float floatValue;
            public int intValue;
            public bool boolValue;
            public AnimatorConditionMode mode;
            public bool isGlobalOverride;
            public string globalGuid;
        }

        private void DrawCreateUI()
        {
            if (_context.SelectedController == null) return;

            DrawCreateStateSelection();
            EditorGUILayout.Space(4f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("默认过渡设置", EditorStyles.boldLabel);
            DrawDefaultSettings();
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("全局条件", EditorStyles.boldLabel);
            DrawGlobalConditions();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4f);

            EditorGUILayout.LabelField("待创建过渡列表", EditorStyles.boldLabel);
            SyncGlobalConditionsToTransitions();
            DrawCreateItemsList();

            EditorGUILayout.Space(4f);

            bool canCreate =
                _createItems.Count > 0 &&
                !string.IsNullOrEmpty(_createSourceState) &&
                !string.IsNullOrEmpty(_createDestState);

            EditorGUI.BeginDisabledGroup(!canCreate);
            if (GUILayout.Button("创建过渡", GUILayout.Height(32f)))
            {
                CreateTransitions();
            }
            EditorGUI.EndDisabledGroup();
        }

        private void DrawCreateStateSelection()
        {
            // Source - 现在使用 displayPath 作为存储值
            var sourceOptions = new List<string> { "Any State" };
            sourceOptions.AddRange(_displayPaths);

            int sourceIndex = sourceOptions.IndexOf(_createSourceState);
            if (sourceIndex < 0) sourceIndex = 0;

            sourceIndex = EditorGUILayout.Popup("源状态", sourceIndex, sourceOptions.ToArray());
            _createSourceState = sourceOptions[Mathf.Clamp(sourceIndex, 0, sourceOptions.Count - 1)];

            // Dest - 现在使用 displayPath 作为存储值
            var destOptions = new List<string>(_displayPaths);
            
            if (_createSourceState != "Any State")
            {
                destOptions.Add("Exit");
            }

            if (destOptions.Count == 0) return;

            int destIndex = destOptions.IndexOf(_createDestState);
            if (destIndex < 0) destIndex = 0;

            destIndex = EditorGUILayout.Popup("目标状态", destIndex, destOptions.ToArray());
            _createDestState = destOptions[Mathf.Clamp(destIndex, 0, destOptions.Count - 1)];
        }

        private void DrawDefaultSettings()
        {
            DrawBoolDefaultSetting("Has Exit Time", ref _defaultHasExitTime, ref _overrideHasExitTime, value =>
            {
                foreach (var t in _createItems) t.overrideHasExitTime = value;
            });

            DrawFloatDefaultSetting("Exit Time", ref _defaultExitTime, ref _overrideExitTime, value =>
            {
                foreach (var t in _createItems) t.overrideExitTime = value;
            });

            DrawBoolDefaultSetting("Fixed Duration", ref _defaultHasFixedDuration, ref _overrideHasFixedDuration, value =>
            {
                foreach (var t in _createItems) t.overrideHasFixedDuration = value;
            });

            DrawFloatDefaultSetting("Transition Duration (s)", ref _defaultDuration, ref _overrideDuration, value =>
            {
                foreach (var t in _createItems) t.overrideDuration = value;
            });

            DrawFloatDefaultSetting("Transition Offset", ref _defaultOffset, ref _overrideOffset, value =>
            {
                foreach (var t in _createItems) t.overrideOffset = value;
            });

            if (_createSourceState == "Any State")
            {
                DrawBoolDefaultSetting("Can Transition To Self", ref _defaultCanTransitionToSelf, ref _overrideCanTransitionToSelf, value =>
                {
                    foreach (var t in _createItems) t.overrideCanTransitionToSelf = value;
                });
            }
        }

        private void DrawBoolDefaultSetting(string label, ref bool value, ref bool overrideFlag, System.Action<bool> onOverrideChanged)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(150f));

            EditorGUI.BeginDisabledGroup(overrideFlag);
            value = EditorGUILayout.Toggle(value, GUILayout.ExpandWidth(true));
            EditorGUI.EndDisabledGroup();

            GUILayout.Label("单独设置", GUILayout.Width(80f));

            EditorGUI.BeginChangeCheck();
            overrideFlag = EditorGUILayout.Toggle(overrideFlag, GUILayout.Width(20f));
            if (EditorGUI.EndChangeCheck())
            {
                onOverrideChanged?.Invoke(overrideFlag);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawFloatDefaultSetting(string label, ref float value, ref bool overrideFlag, System.Action<bool> onOverrideChanged)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(150f));

            EditorGUI.BeginDisabledGroup(overrideFlag);
            value = EditorGUILayout.FloatField(value, GUILayout.ExpandWidth(true));
            EditorGUI.EndDisabledGroup();

            GUILayout.Label("单独设置", GUILayout.Width(80f));

            EditorGUI.BeginChangeCheck();
            overrideFlag = EditorGUILayout.Toggle(overrideFlag, GUILayout.Width(20f));
            if (EditorGUI.EndChangeCheck())
            {
                onOverrideChanged?.Invoke(overrideFlag);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawGlobalConditions()
        {
            int removeIndex = -1;

            for (int i = 0; i < _globalConditions.Count; i++)
            {
                var cond = _globalConditions[i];
                if (cond == null)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();

                var validParameters = _context.SelectedController.parameters;
                var nonTriggerParams = new List<AnimatorControllerParameter>();
                foreach (var p in validParameters)
                {
                    if (p != null && p.type != AnimatorControllerParameterType.Trigger)
                    {
                        nonTriggerParams.Add(p);
                    }
                }

                var parameterNames = new string[nonTriggerParams.Count];
                for (int pi = 0; pi < nonTriggerParams.Count; pi++)
                {
                    parameterNames[pi] = nonTriggerParams[pi].name;
                }

                int currentParamIndex = -1;
                if (!string.IsNullOrEmpty(cond.parameterName))
                {
                    for (int pi = 0; pi < parameterNames.Length; pi++)
                    {
                        if (parameterNames[pi] == cond.parameterName)
                        {
                            currentParamIndex = pi;
                            break;
                        }
                    }
                }

                EditorGUI.BeginDisabledGroup(cond.isGlobalOverride);
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.LabelField("参数", GUILayout.Width(40f));
                int newParamIndex = EditorGUILayout.Popup(currentParamIndex, parameterNames, GUILayout.MinWidth(80f), GUILayout.MaxWidth(200f));
                if (EditorGUI.EndChangeCheck())
                {
                    if (newParamIndex >= 0 && newParamIndex < nonTriggerParams.Count)
                    {
                        var p = nonTriggerParams[newParamIndex];
                        cond.parameterName = p.name;
                        cond.parameterType = p.type;
                    }
                }

                DrawConditionValueFields(cond);
                EditorGUI.EndDisabledGroup();

                GUILayout.FlexibleSpace();
                GUILayout.Label("单独设置", GUILayout.Width(80f));
                cond.isGlobalOverride = EditorGUILayout.Toggle(cond.isGlobalOverride, GUILayout.Width(20f));

                float removeButtonSize = EditorGUIUtility.singleLineHeight;
                if (GUILayout.Button("-", GUILayout.Width(removeButtonSize), GUILayout.Height(removeButtonSize)))
                {
                    removeIndex = i;
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }

            if (removeIndex >= 0 && removeIndex < _globalConditions.Count)
            {
                string guidToRemove = _globalConditions[removeIndex].globalGuid;
                _globalConditions.RemoveAt(removeIndex);

                foreach (var transition in _createItems)
                {
                    transition.conditions.RemoveAll(c => c.isGlobalOverride && c.globalGuid == guidToRemove);
                }
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("添加全局条件", GUILayout.Width(140f)))
            {
                if (_context.SelectedController != null)
                {
                    bool hasNonTrigger = false;
                    foreach (var p in _context.SelectedController.parameters)
                    {
                        if (p != null && p.type != AnimatorControllerParameterType.Trigger)
                        {
                            hasNonTrigger = true;
                            break;
                        }
                    }

                    if (hasNonTrigger)
                    {
                        _globalConditions.Add(new ConditionUI
                        {
                            globalGuid = System.Guid.NewGuid().ToString(),
                            boolValue = true,
                            mode = AnimatorConditionMode.Greater
                        });
                    }
                }
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void SyncGlobalConditionsToTransitions()
        {
            var globalGuids = new List<string>();
            for (int i = 0; i < _globalConditions.Count; i++)
            {
                var gc = _globalConditions[i];
                if (gc != null && !string.IsNullOrEmpty(gc.globalGuid))
                {
                    globalGuids.Add(gc.globalGuid);
                }
            }

            foreach (var transition in _createItems)
            {
                transition.conditions.RemoveAll(c => c.isGlobalOverride && !globalGuids.Contains(c.globalGuid));

                foreach (var globalCond in _globalConditions)
                {
                    if (globalCond == null || string.IsNullOrEmpty(globalCond.globalGuid))
                    {
                        continue;
                    }

                    var existing = transition.conditions.Find(c => c.isGlobalOverride && c.globalGuid == globalCond.globalGuid);
                    if (existing != null)
                    {
                        if (!globalCond.isGlobalOverride)
                        {
                            existing.parameterName = globalCond.parameterName;
                            existing.parameterType = globalCond.parameterType;
                            existing.floatValue = globalCond.floatValue;
                            existing.intValue = globalCond.intValue;
                            existing.boolValue = globalCond.boolValue;
                            existing.mode = globalCond.mode;
                        }
                    }
                    else
                    {
                        transition.conditions.Add(new ConditionUI
                        {
                            parameterName = globalCond.parameterName,
                            parameterType = globalCond.parameterType,
                            floatValue = globalCond.floatValue,
                            intValue = globalCond.intValue,
                            boolValue = globalCond.boolValue,
                            mode = globalCond.mode,
                            isGlobalOverride = true,
                            globalGuid = globalCond.globalGuid
                        });
                    }
                }
            }
        }

        private void DrawCreateItemsList()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("添加过渡", GUILayout.Width(160f), GUILayout.Height(30f)))
            {
                var item = new CreateTransitionItem
                {
                    overrideHasExitTime = _overrideHasExitTime,
                    hasExitTime = _defaultHasExitTime,
                    overrideExitTime = _overrideExitTime,
                    exitTime = _defaultExitTime,
                    overrideHasFixedDuration = _overrideHasFixedDuration,
                    hasFixedDuration = _defaultHasFixedDuration,
                    overrideDuration = _overrideDuration,
                    duration = _defaultDuration,
                    overrideOffset = _overrideOffset,
                    offset = _defaultOffset,
                    overrideCanTransitionToSelf = _overrideCanTransitionToSelf,
                    canTransitionToSelf = _defaultCanTransitionToSelf
                };

                _createItems.Add(item);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            if (_createItems.Count == 0)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            int removeIndex = -1;

            for (int i = 0; i < _createItems.Count; i++)
            {
                var item = _createItems[i];
                if (item == null)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"过渡 {i + 1}", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                float removeButtonSize = EditorGUIUtility.singleLineHeight;
                if (GUILayout.Button("-", GUILayout.Width(removeButtonSize), GUILayout.Height(removeButtonSize)))
                {
                    removeIndex = i;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space();

                DrawTransitionOverrideSettings(item);

                EditorGUILayout.Space();

                EditorGUILayout.LabelField("条件", EditorStyles.miniBoldLabel);
                DrawTransitionConditionsList(_context.SelectedController, item.conditions);

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }

            if (removeIndex >= 0 && removeIndex < _createItems.Count)
            {
                _createItems.RemoveAt(removeIndex);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawTransitionOverrideSettings(CreateTransitionItem settings)
        {
            if (settings.overrideHasExitTime)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Has Exit Time", GUILayout.Width(150f));
                settings.hasExitTime = EditorGUILayout.Toggle(settings.hasExitTime, GUILayout.ExpandWidth(true));
                EditorGUILayout.EndHorizontal();
            }

            if (settings.overrideExitTime)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Exit Time", GUILayout.Width(150f));
                settings.exitTime = EditorGUILayout.FloatField(settings.exitTime, GUILayout.ExpandWidth(true));
                EditorGUILayout.EndHorizontal();
            }

            if (settings.overrideHasFixedDuration)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Fixed Duration", GUILayout.Width(150f));
                settings.hasFixedDuration = EditorGUILayout.Toggle(settings.hasFixedDuration, GUILayout.ExpandWidth(true));
                EditorGUILayout.EndHorizontal();
            }

            if (settings.overrideDuration)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Transition Duration (s)", GUILayout.Width(150f));
                settings.duration = EditorGUILayout.FloatField(settings.duration, GUILayout.ExpandWidth(true));
                EditorGUILayout.EndHorizontal();
            }

            if (settings.overrideOffset)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Transition Offset", GUILayout.Width(150f));
                settings.offset = EditorGUILayout.FloatField(settings.offset, GUILayout.ExpandWidth(true));
                EditorGUILayout.EndHorizontal();
            }

            if (_createSourceState == "Any State" && settings.overrideCanTransitionToSelf)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Can Transition To Self", GUILayout.Width(150f));
                settings.canTransitionToSelf = EditorGUILayout.Toggle(settings.canTransitionToSelf, GUILayout.ExpandWidth(true));
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawTransitionConditionsList(AnimatorController controller, List<ConditionUI> conditions)
        {
            for (int i = 0; i < conditions.Count; i++)
            {
                var cond = conditions[i];
                if (cond == null)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();

                bool isGlobalSynced = cond.isGlobalOverride;
                bool isGloballyOverridden = false;
                if (isGlobalSynced)
                {
                    for (int gi = 0; gi < _globalConditions.Count; gi++)
                    {
                        var gc = _globalConditions[gi];
                        if (gc != null && gc.globalGuid == cond.globalGuid && gc.isGlobalOverride)
                        {
                            isGloballyOverridden = true;
                            break;
                        }
                    }
                }

                EditorGUI.BeginDisabledGroup(isGlobalSynced);

                var validParameters = controller.parameters;
                var nonTriggerParams = new List<AnimatorControllerParameter>();
                foreach (var p in validParameters)
                {
                    if (p != null && p.type != AnimatorControllerParameterType.Trigger)
                    {
                        nonTriggerParams.Add(p);
                    }
                }

                var parameterNames = new string[nonTriggerParams.Count];
                for (int pi = 0; pi < nonTriggerParams.Count; pi++)
                {
                    parameterNames[pi] = nonTriggerParams[pi].name;
                }

                int currentParamIndex = -1;
                if (!string.IsNullOrEmpty(cond.parameterName))
                {
                    for (int pi = 0; pi < parameterNames.Length; pi++)
                    {
                        if (parameterNames[pi] == cond.parameterName)
                        {
                            currentParamIndex = pi;
                            break;
                        }
                    }
                }

                EditorGUI.BeginChangeCheck();
                EditorGUILayout.LabelField("参数", GUILayout.Width(40f));
                int newParamIndex = EditorGUILayout.Popup(currentParamIndex, parameterNames, GUILayout.MinWidth(80f), GUILayout.MaxWidth(200f));
                if (EditorGUI.EndChangeCheck())
                {
                    if (newParamIndex >= 0 && newParamIndex < nonTriggerParams.Count)
                    {
                        var p = nonTriggerParams[newParamIndex];
                        cond.parameterName = p.name;
                        cond.parameterType = p.type;
                    }
                }

                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(isGlobalSynced && !isGloballyOverridden);
                DrawConditionValueFields(cond);
                EditorGUI.EndDisabledGroup();

                GUILayout.FlexibleSpace();
                float globalLabelWidth = 120f;
                float removeButtonSize = EditorGUIUtility.singleLineHeight;
                if (isGlobalSynced)
                {
                    GUILayout.Label("(全局参数)", GUILayout.Width(globalLabelWidth));
                }
                else
                {
                    float padding = EditorGUIUtility.standardVerticalSpacing;
                    GUILayout.Label(string.Empty, GUILayout.Width(globalLabelWidth - removeButtonSize - 2f * padding));

                    if (GUILayout.Button("-", GUILayout.Width(removeButtonSize), GUILayout.Height(removeButtonSize)))
                    {
                        conditions.RemoveAt(i);
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.EndVertical();
                        break;
                    }
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("添加局部条件", GUILayout.Width(120f)))
            {
                if (controller != null)
                {
                    bool hasNonTrigger = false;
                    foreach (var p in controller.parameters)
                    {
                        if (p != null && p.type != AnimatorControllerParameterType.Trigger)
                        {
                            hasNonTrigger = true;
                            break;
                        }
                    }

                    if (hasNonTrigger)
                    {
                        conditions.Add(new ConditionUI
                        {
                            boolValue = true,
                            mode = AnimatorConditionMode.Greater
                        });
                    }
                }
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawConditionValueFields(ConditionUI condition)
        {
            if (string.IsNullOrEmpty(condition.parameterName))
            {
                const float modeWidth = 80f;
                const float valueWidth = 60f;
                float padding = EditorGUIUtility.standardVerticalSpacing;
                GUILayout.Label(string.Empty, GUILayout.Width(modeWidth + valueWidth + 1f * padding));
                return;
            }

            switch (condition.parameterType)
            {
                case AnimatorControllerParameterType.Bool:
                {
                    string[] boolModes = { "True", "False" };
                    int modeIndex = condition.boolValue ? 0 : 1;
                    int newIndex = EditorGUILayout.Popup(modeIndex, boolModes, GUILayout.Width(80f), GUILayout.ExpandWidth(false));
                    if (newIndex != modeIndex)
                    {
                        condition.boolValue = newIndex == 0;
                    }

                    float valueFieldWidth = 60f;
                    GUILayout.Label(string.Empty, GUILayout.Width(valueFieldWidth));
                    break;
                }
                case AnimatorControllerParameterType.Float:
                {
                    if (condition.mode != AnimatorConditionMode.Greater && condition.mode != AnimatorConditionMode.Less)
                    {
                        condition.mode = AnimatorConditionMode.Greater;
                    }

                    string[] floatModes = { "Greater", "Less" };
                    int modeIndex = condition.mode == AnimatorConditionMode.Less ? 1 : 0;
                    int newIndex = EditorGUILayout.Popup(modeIndex, floatModes, GUILayout.Width(80f), GUILayout.ExpandWidth(false));
                    if (newIndex != modeIndex)
                    {
                        condition.mode = newIndex == 0 ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less;
                    }

                    condition.floatValue = EditorGUILayout.FloatField(condition.floatValue, GUILayout.Width(60f), GUILayout.ExpandWidth(false));
                    break;
                }
                case AnimatorControllerParameterType.Int:
                {
                    if (condition.mode != AnimatorConditionMode.Greater &&
                        condition.mode != AnimatorConditionMode.Less &&
                        condition.mode != AnimatorConditionMode.Equals &&
                        condition.mode != AnimatorConditionMode.NotEqual)
                    {
                        condition.mode = AnimatorConditionMode.Greater;
                    }

                    string[] intModes = { "Greater", "Less", "Equals", "NotEquals" };
                    int modeIndex = 0;
                    switch (condition.mode)
                    {
                        case AnimatorConditionMode.Greater: modeIndex = 0; break;
                        case AnimatorConditionMode.Less: modeIndex = 1; break;
                        case AnimatorConditionMode.Equals: modeIndex = 2; break;
                        case AnimatorConditionMode.NotEqual: modeIndex = 3; break;
                    }

                    int newIndex = EditorGUILayout.Popup(modeIndex, intModes, GUILayout.Width(80f), GUILayout.ExpandWidth(false));
                    if (newIndex != modeIndex)
                    {
                        switch (newIndex)
                        {
                            case 0: condition.mode = AnimatorConditionMode.Greater; break;
                            case 1: condition.mode = AnimatorConditionMode.Less; break;
                            case 2: condition.mode = AnimatorConditionMode.Equals; break;
                            case 3: condition.mode = AnimatorConditionMode.NotEqual; break;
                        }
                    }

                    condition.intValue = EditorGUILayout.IntField(condition.intValue, GUILayout.Width(60f), GUILayout.ExpandWidth(false));
                    break;
                }
            }
        }

        private void CreateTransitions()
        {
            if (_context.SelectedController == null || _context.SelectedLayer == null)
            {
                EditorUtility.DisplayDialog("错误", "请先选择控制器和层级。", "确定");
                return;
            }

            // 解析源和目标
            bool useAnyStateAsSource = _createSourceState == "Any State";
            bool toExit = _createDestState == "Exit";
            
            AnimatorState sourceState = null;
            bool sourceIsStateMachine = false;
            
            AnimatorState destState = null;
            AnimatorStateMachine destStateMachine = null;
            bool destIsStateMachine = false;

            // 解析源
            if (!useAnyStateAsSource)
            {
                if (_isStateMachineMap.TryGetValue(_createSourceState, out sourceIsStateMachine))
                {
                    if (sourceIsStateMachine)
                    {
                        EditorUtility.DisplayDialog("错误", "不能从子状态机创建过渡，请选择具体状态或 Any State。", "确定");
                        return;
                    }
                    else
                    {
                        sourceState = _stateByDisplayPath.TryGetValue(_createSourceState, out var s) ? s : null;
                        if (sourceState == null)
                        {
                            EditorUtility.DisplayDialog("错误", $"未找到源状态: {_createSourceState}", "确定");
                            return;
                        }
                    }
                }
            }

            // 解析目标
            if (!toExit)
            {
                if (_isStateMachineMap.TryGetValue(_createDestState, out destIsStateMachine))
                {
                    if (destIsStateMachine)
                    {
                        destStateMachine = _stateMachineByDisplayPath.TryGetValue(_createDestState, out var sm) ? sm : null;
                        if (destStateMachine == null)
                        {
                            EditorUtility.DisplayDialog("错误", $"未找到目标子状态机: {_createDestState}", "确定");
                            return;
                        }
                    }
                    else
                    {
                        destState = _stateByDisplayPath.TryGetValue(_createDestState, out var s) ? s : null;
                        if (destState == null)
                        {
                            EditorUtility.DisplayDialog("错误", $"未找到目标状态: {_createDestState}", "确定");
                            return;
                        }
                    }
                }
            }

            // 转换 UI 数据为 Service 数据结构
            var transitionItems = new List<TransitionCreateService.TransitionItemSettings>();
            foreach (var item in _createItems)
            {
                var conditions = new List<TransitionCreateService.ConditionSettings>();
                foreach (var cond in item.conditions)
                {
                    conditions.Add(new TransitionCreateService.ConditionSettings
                    {
                        parameterName = cond.parameterName,
                        parameterType = cond.parameterType,
                        floatValue = cond.floatValue,
                        intValue = cond.intValue,
                        boolValue = cond.boolValue,
                        mode = cond.mode,
                        isGlobalOverride = cond.isGlobalOverride
                    });
                }

                transitionItems.Add(new TransitionCreateService.TransitionItemSettings
                {
                    overrideHasExitTime = item.overrideHasExitTime,
                    hasExitTime = item.hasExitTime,
                    overrideExitTime = item.overrideExitTime,
                    exitTime = item.exitTime,
                    overrideHasFixedDuration = item.overrideHasFixedDuration,
                    hasFixedDuration = item.hasFixedDuration,
                    overrideDuration = item.overrideDuration,
                    duration = item.duration,
                    overrideOffset = item.overrideOffset,
                    offset = item.offset,
                    overrideCanTransitionToSelf = item.overrideCanTransitionToSelf,
                    canTransitionToSelf = item.canTransitionToSelf,
                    conditions = conditions
                });
            }

            var globalConditions = new List<TransitionCreateService.ConditionSettings>();
            foreach (var cond in _globalConditions)
            {
                globalConditions.Add(new TransitionCreateService.ConditionSettings
                {
                    parameterName = cond.parameterName,
                    parameterType = cond.parameterType,
                    floatValue = cond.floatValue,
                    intValue = cond.intValue,
                    boolValue = cond.boolValue,
                    mode = cond.mode,
                    isGlobalOverride = cond.isGlobalOverride
                });
            }

            // 调用 Service 执行创建
            var result = TransitionCreateService.Execute(
                _context.SelectedController,
                _context.SelectedLayer,
                useAnyStateAsSource,
                sourceState,
                toExit,
                destState,
                destStateMachine,
                _defaultHasExitTime,
                _defaultExitTime,
                _defaultHasFixedDuration,
                _defaultDuration,
                _defaultOffset,
                _defaultCanTransitionToSelf,
                transitionItems,
                globalConditions
            );

            if (result.Success)
            {
                EditorUtility.DisplayDialog("完成", $"成功创建 {result.CreatedCount} 个过渡！", "确定");
            }
            else
            {
                EditorUtility.DisplayDialog("错误", result.ErrorMessage, "确定");
            }
        }

        #endregion

        #region Modify Mode

        private class ModifyConditionSettingUI
        {
            public string parameterName;
            public AnimatorConditionMode mode;
            public float threshold;

            private int _selectedParameterIndex;

            public bool enableIntAutoIncrement;

            public enum IntIncrementDirection
            {
                Increment,
                Decrement
            }

            public IntIncrementDirection incrementDirection = IntIncrementDirection.Increment;

            public enum SortMode
            {
                ArrangementOrder,
                NameNumberOrder
            }

            public SortMode sortMode = SortMode.ArrangementOrder;

            public int incrementStep = 1;
            public float floatIncrementStep = 0.01f;

            public bool requestRemove;

            private static readonly string[] IntModes = { "Greater", "Less", "Equals", "NotEquals" };

            public void Draw(AnimatorController controller, TransitionModifyService.ModifyMode modifyMode)
            {
                var parameters = controller.parameters;
                var availableNames = new List<string>();
                foreach (var p in parameters)
                {
                    if (p != null && p.type != AnimatorControllerParameterType.Trigger)
                    {
                        availableNames.Add(p.name);
                    }
                }

                if (availableNames.Count == 0)
                {
                    EditorGUILayout.HelpBox("未找到非 Trigger 类型的参数。", MessageType.Info);
                    return;
                }

                if (string.IsNullOrEmpty(parameterName))
                {
                    parameterName = availableNames[0];
                }

                int currentIndex = availableNames.IndexOf(parameterName);
                if (currentIndex >= 0)
                {
                    _selectedParameterIndex = currentIndex;
                }

                EditorGUILayout.BeginHorizontal();

                int newIndex = EditorGUILayout.Popup(_selectedParameterIndex, availableNames.ToArray(), GUILayout.Width(150f));
                if (newIndex != _selectedParameterIndex && newIndex >= 0 && newIndex < availableNames.Count)
                {
                    _selectedParameterIndex = newIndex;
                    parameterName = availableNames[_selectedParameterIndex];
                    enableIntAutoIncrement = false;
                }

                AnimatorControllerParameter selectedParam = null;
                foreach (var p in parameters)
                {
                    if (p != null && p.name == parameterName)
                    {
                        selectedParam = p;
                        break;
                    }
                }

                if (selectedParam != null)
                {
                    switch (selectedParam.type)
                    {
                        case AnimatorControllerParameterType.Bool:
                        {
                            string[] boolModes = { "True", "False" };
                            int boolModeIndex = mode == AnimatorConditionMode.If ? 0 : 1;
                            boolModeIndex = EditorGUILayout.Popup(boolModeIndex, boolModes, GUILayout.Width(80f));
                            mode = boolModeIndex == 0 ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot;
                            break;
                        }
                        case AnimatorControllerParameterType.Float:
                        {
                            string[] floatModes = { "Greater", "Less" };
                            int floatModeIndex = mode == AnimatorConditionMode.Greater ? 0 : 1;
                            floatModeIndex = EditorGUILayout.Popup(floatModeIndex, floatModes, GUILayout.Width(80f));
                            mode = floatModeIndex == 0 ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less;
                            threshold = EditorGUILayout.FloatField(threshold, GUILayout.Width(60f));
                            break;
                        }
                        case AnimatorControllerParameterType.Int:
                        {
                            int intModeIndex = 0;
                            if (mode == AnimatorConditionMode.Less) intModeIndex = 1;
                            else if (mode == AnimatorConditionMode.Equals) intModeIndex = 2;
                            else if (mode == AnimatorConditionMode.NotEqual) intModeIndex = 3;

                            intModeIndex = EditorGUILayout.Popup(intModeIndex, IntModes, GUILayout.Width(80f));

                            switch (intModeIndex)
                            {
                                case 0: mode = AnimatorConditionMode.Greater; break;
                                case 1: mode = AnimatorConditionMode.Less; break;
                                case 2: mode = AnimatorConditionMode.Equals; break;
                                case 3: mode = AnimatorConditionMode.NotEqual; break;
                            }

                            int newThreshold = EditorGUILayout.IntField((int)threshold, GUILayout.Width(60f));
                            if (newThreshold < 0) newThreshold = 0;
                            threshold = newThreshold;
                            break;
                        }
                    }
                }

                GUILayout.FlexibleSpace();
                float removeButtonSize = EditorGUIUtility.singleLineHeight;
                if (GUILayout.Button("-", GUILayout.Width(removeButtonSize), GUILayout.Height(removeButtonSize)))
                {
                    requestRemove = true;
                }

                EditorGUILayout.EndHorizontal();

                if (selectedParam != null &&
                    (selectedParam.type == AnimatorControllerParameterType.Int || selectedParam.type == AnimatorControllerParameterType.Float))
                {
                    EditorGUILayout.Space(5f);
                    enableIntAutoIncrement = EditorGUILayout.Toggle("启用 参数递增/递减", enableIntAutoIncrement);
                    if (enableIntAutoIncrement)
                    {
                        EditorGUILayout.BeginHorizontal();

                        GUILayout.Label("趋势", GUILayout.Width(60f));
                        string[] directionOptions = { "增加", "减少" };
                        int directionIndex = incrementDirection == IntIncrementDirection.Increment ? 0 : 1;
                        directionIndex = EditorGUILayout.Popup(directionIndex, directionOptions, GUILayout.Width(80f));
                        incrementDirection = directionIndex == 0 ? IntIncrementDirection.Increment : IntIncrementDirection.Decrement;

                        GUILayout.Label("幅度", GUILayout.Width(50f));
                        if (selectedParam.type == AnimatorControllerParameterType.Int)
                        {
                            int newStep = EditorGUILayout.IntField(incrementStep, GUILayout.Width(60f));
                            if (newStep < 1) newStep = 1;
                            if (newStep > 255) newStep = 255;
                            incrementStep = newStep;
                        }
                        else
                        {
                            float newStep = EditorGUILayout.FloatField(floatIncrementStep, GUILayout.Width(60f));
                            floatIncrementStep = newStep;
                        }

                        GUILayout.Label("排列方式", GUILayout.Width(70f));
                        if (modifyMode == TransitionModifyService.ModifyMode.ToStateTransitions)
                        {
                            GUILayout.Label("按状态名称");
                            sortMode = SortMode.NameNumberOrder;
                        }
                        else
                        {
                            string[] sortOptions = { "按连接顺序", "按状态名称" };
                            int sortIndex = sortMode == SortMode.ArrangementOrder ? 0 : 1;
                            sortIndex = EditorGUILayout.Popup(sortIndex, sortOptions);
                            sortMode = sortIndex == 0 ? SortMode.ArrangementOrder : SortMode.NameNumberOrder;
                        }

                        EditorGUILayout.EndHorizontal();
                    }
                }
            }
        }

        private void DrawModifyUI()
        {
            if (_context.SelectedController == null) return;

            DrawModifyModeSelection();
            EditorGUILayout.Space(4f);

            if (_modifyActionMode == ModifyActionMode.ModifyTransitions)
            {
                // 过渡属性区域
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("过渡属性", EditorStyles.boldLabel);
                DrawModifyProperties();
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space(4f);

                // 条件设置区域
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawModifyConditions();
                EditorGUILayout.EndVertical();
            }
            else
            {
                // 条件增减区域（无过渡属性）
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawConditionDeltaUI();
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(8f);
            if (GUILayout.Button("应用更改", GUILayout.Height(32f)))
            {
                ApplyModify();
            }
        }

        private void DrawModifyModeSelection()
        {
            // 修改模式
            var actionModeOptions = new[] { "修改过渡", "增减Conditions" };
            int actionModeIndex = (int)_modifyActionMode;
            actionModeIndex = EditorGUILayout.Popup("修改模式", actionModeIndex, actionModeOptions);
            _modifyActionMode = (ModifyActionMode)actionModeIndex;

            // 过渡选择（由选择的状态出发 / 到达选择的状态）
            var modifyModeOptions = new[] { "由选择的状态出发", "到达选择的状态" };
            int modeIndex = (int)_modifyMode;
            modeIndex = EditorGUILayout.Popup("过渡选择", modeIndex, modifyModeOptions);
            _modifyMode = (TransitionModifyService.ModifyMode)modeIndex;

            var options = BuildModifyTargetStateOptions(_modifyMode);
            if (options.Count == 0) return;

            _modifyStateIndex = Mathf.Clamp(_modifyStateIndex, 0, options.Count - 1);
            _modifyStateIndex = EditorGUILayout.Popup("目标状态", _modifyStateIndex, options.ToArray());
        }

        private List<string> BuildModifyTargetStateOptions(TransitionModifyService.ModifyMode modifyMode)
        {
            var options = new List<string>();

            if (modifyMode == TransitionModifyService.ModifyMode.FromStateTransitions)
            {
                options.Add("Any State");
            }

            options.AddRange(_displayPaths);

            if (modifyMode == TransitionModifyService.ModifyMode.ToStateTransitions)
            {
                options.Add("Exit");
            }

            return options;
        }

        private string ResolveTargetStatePath(string selectedDisplayPath)
        {
            string targetStatePath = selectedDisplayPath;
            if (selectedDisplayPath != "Any State" && selectedDisplayPath != "Exit")
            {
                targetStatePath = _displayToActualPath.TryGetValue(selectedDisplayPath, out var actualPath) ? actualPath : selectedDisplayPath;
            }

            return targetStatePath;
        }

        private void DrawConditionDeltaUI()
        {
            EditorGUILayout.LabelField("条件设置", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("添加条件", GUILayout.Width(120f)))
            {
                _conditionDeltaSettings.Add(new ConditionDeltaSettingUI());
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);

            for (int i = _conditionDeltaSettings.Count - 1; i >= 0; i--)
            {
                var setting = _conditionDeltaSettings[i];
                if (setting == null)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                setting.Draw(_context.SelectedController);
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5f);

                if (setting.requestRemove)
                {
                    _conditionDeltaSettings.RemoveAt(i);
                }
            }
        }

        private void DrawModifyProperties()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Has Exit Time", GUILayout.Width(150f));
            _modifyHasExitTimeValue = EditorGUILayout.Toggle(_modifyHasExitTimeValue, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Exit Time", GUILayout.Width(150f));
            _modifyExitTimeValue = EditorGUILayout.FloatField(_modifyExitTimeValue, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Fixed Duration", GUILayout.Width(150f));
            _modifyHasFixedDurationValue = EditorGUILayout.Toggle(_modifyHasFixedDurationValue, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Transition Duration (s)", GUILayout.Width(150f));
            _modifyDurationValue = EditorGUILayout.FloatField(_modifyDurationValue, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Transition Offset", GUILayout.Width(150f));
            _modifyOffsetValue = EditorGUILayout.FloatField(_modifyOffsetValue, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawModifyConditions()
        {
            EditorGUILayout.BeginHorizontal();
            _modifyConditions = EditorGUILayout.Toggle(_modifyConditions, GUILayout.Width(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.LabelField("条件设置", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            if (!_modifyConditions) return;

            EditorGUILayout.Space(4f);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("添加条件", GUILayout.Width(120f)))
            {
                _modifyConditionSettings.Add(new ModifyConditionSettingUI());
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);

            for (int i = _modifyConditionSettings.Count - 1; i >= 0; i--)
            {
                var setting = _modifyConditionSettings[i];
                if (setting == null)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                setting.Draw(_context.SelectedController, _modifyMode);
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5f);

                if (setting.requestRemove)
                {
                    _modifyConditionSettings.RemoveAt(i);
                }
            }
        }

        private void ApplyModify()
        {
            var options = BuildModifyTargetStateOptions(_modifyMode);

            if (_modifyStateIndex < 0 || _modifyStateIndex >= options.Count) return;
            string selectedDisplayPath = options[_modifyStateIndex];

            string targetStatePath = ResolveTargetStatePath(selectedDisplayPath);

            if (_modifyActionMode == ModifyActionMode.ModifyTransitions)
            {
                var serviceSettings = _modifyConditionSettings.Select(s => new TransitionModifyService.ConditionSetting
                {
                    parameterName = s.parameterName,
                    mode = s.mode,
                    threshold = s.threshold,
                    enableIntAutoIncrement = s.enableIntAutoIncrement,
                    incrementDirection = s.incrementDirection == ModifyConditionSettingUI.IntIncrementDirection.Decrement
                        ? TransitionModifyService.IncrementDirection.Decrement
                        : TransitionModifyService.IncrementDirection.Increment,
                    sortMode = s.sortMode == ModifyConditionSettingUI.SortMode.NameNumberOrder
                        ? TransitionModifyService.SortMode.NameNumberOrder
                        : TransitionModifyService.SortMode.ArrangementOrder,
                    incrementStep = s.incrementStep,
                    floatIncrementStep = s.floatIncrementStep
                }).ToList();

                TransitionModifyService.Execute(
                    _context.SelectedController,
                    _context.SelectedLayer,
                    _modifyMode,
                    targetStatePath,
                    _modifyHasExitTimeValue,
                    _modifyExitTimeValue,
                    _modifyHasFixedDurationValue,
                    _modifyDurationValue,
                    _modifyOffsetValue,
                    _modifyConditions,
                    serviceSettings
                );
            }
            else
            {
                var deltaSettings = _conditionDeltaSettings.Select(s => new TransitionModifyService.ConditionDeltaSetting
                {
                    parameterName = s.parameterName,
                    mode = s.mode,
                    threshold = s.threshold,
                    operation = s.operation == ConditionDeltaSettingUI.ConditionOp.AddUnique
                        ? TransitionModifyService.ConditionDeltaOperation.AddUnique
                        : s.operation == ConditionDeltaSettingUI.ConditionOp.Remove
                            ? TransitionModifyService.ConditionDeltaOperation.Remove
                            : TransitionModifyService.ConditionDeltaOperation.Append,
                    removeAllForParameter = s.operation == ConditionDeltaSettingUI.ConditionOp.Remove && s.removeAllForParameter,
                    ignoreCondition = s.operation == ConditionDeltaSettingUI.ConditionOp.AddUnique && s.ignoreCondition
                }).ToList();

                TransitionModifyService.ExecuteConditionDelta(
                    _context.SelectedController,
                    _context.SelectedLayer,
                    _modifyMode,
                    targetStatePath,
                    deltaSettings);
            }
            
            EditorUtility.DisplayDialog("完成", "过渡修改完成！", "确定");
        }

        #endregion
    }
}
