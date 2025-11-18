using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using MVA.Toolbox.Public;
using MVA.Toolbox.QuickTransition.Services;

namespace MVA.Toolbox.QuickTransition.UI
{
    /// <summary>
    /// Quick Transition 主窗口：用于选择目标 Avatar/Animator/控制器，并进入创建/修改过渡的工作区。
    /// 仅负责编辑器界面与用户交互，具体过渡创建/修改逻辑由 Services 层承担。
    /// </summary>
    public sealed class QuickTransitionWindow : EditorWindow
    {
        private enum ToolMode
        {
            CreateTransitions,
            ModifyTransitions
        }

        // 目标对象：Avatar、带 Animator 的物体 或 AnimatorController 资产
        [SerializeField]
        private Object _targetObject;

        // 解析出的 Avatar / Animator（当目标是 GameObject 时）
        private VRCAvatarDescriptor _avatarDescriptor;
        private Animator _animator;

        // 可用的 AnimatorController 列表（来自 Avatar base/special 层和 Animator.runtime）
        private readonly List<AnimatorController> _controllers = new List<AnimatorController>();
        private readonly List<string> _controllerNames = new List<string>();
        private int _selectedControllerIndex;

        // 当前控制器的层级选择
        private int _selectedLayerIndex;

        // 源 / 目标状态名称（源状态支持 "Any State"）
        private string _selectedSourceStateName;
        private string _selectedDestinationStateName;

        // 默认过渡设置
        private bool _defaultHasExitTime = true;
        private float _defaultExitTime = 0.75f;
        private bool _defaultHasFixedDuration = true;
        private float _defaultDuration = 0.25f;
        private float _defaultOffset = 0f;
        private bool _defaultCanTransitionToSelf = true;

        // 默认设置的 override 标记（决定是否允许在单个过渡上单独设置）
        private bool _overrideHasExitTime;
        private bool _overrideExitTime;
        private bool _overrideHasFixedDuration;
        private bool _overrideDuration;
        private bool _overrideOffset;
        private bool _overrideCanTransitionToSelf;

        // 待创建过渡列表（每条过渡只保存“覆盖值”和条件，而不是完整复制默认值）
        [System.Serializable]
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

            public List<Condition> conditions = new List<Condition>();
        }

        // 条件结构：包含本地/全局标记与 GUID，以支持“全局条件同步 + 局部覆盖”行为
        [System.Serializable]
        private class Condition
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

        private readonly List<CreateTransitionItem> _createItems = new List<CreateTransitionItem>();
        private readonly List<Condition> _globalConditions = new List<Condition>();

        private enum ModifyMode
        {
            FromStateTransitions,
            ToStateTransitions
        }

        private ModifyMode _modifyMode = ModifyMode.FromStateTransitions;
        private string _modifyStateName = string.Empty;
        private int _modifySelectedStateIndex;

        // 批量修改的过渡属性
        private bool _modifyHasExitTimeValue = true;
        private float _modifyExitTimeValue = 0.75f;
        private bool _modifyHasFixedDurationValue = true;
        private float _modifyDurationValue = 0.25f;
        private float _modifyOffsetValue = 0f;

        // 条件设置
        private bool _modifyConditions;

        [System.Serializable]
        private class ModifyConditionSetting
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

            public void Draw(AnimatorController controller, ModifyMode modifyMode)
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

                // 初始化参数名
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

                // 与参数与模式在同一行末尾绘制删除按钮
                GUILayout.FlexibleSpace();
                float removeButtonSize = EditorGUIUtility.singleLineHeight;
                if (GUILayout.Button("-", GUILayout.Width(removeButtonSize), GUILayout.Height(removeButtonSize)))
                {
                    requestRemove = true;
                }

                EditorGUILayout.EndHorizontal();

                // 仅在 Int/Float 参数上显示“参数递增/递减”选项
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
                        if (modifyMode == ModifyMode.ToStateTransitions)
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

        private readonly List<ModifyConditionSetting> _modifyConditionSettings = new List<ModifyConditionSetting>();

        // 当前模式与滚动条位置
        private ToolMode _toolMode = ToolMode.CreateTransitions;
        private Vector2 _scrollPosition;

        [MenuItem("Tools/MVA Toolbox/Quick Transition", false, 3)]
        private static void ShowWindow()
        {
            var window = GetWindow<QuickTransitionWindow>("Quick Transition");
            window.minSize = new Vector2(520f, 420f);
        }

        private void OnEnable()
        {
            RefreshTargetComponents();
            RefreshControllers();
        }

        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawTargetSelection();
            EditorGUILayout.Space();

            if (_targetObject != null && _controllers.Count > 0)
            {
                DrawControllerSelection();
                EditorGUILayout.Space();

                DrawModeSelection();

                EditorGUILayout.Space();
                DrawWorkAreaPlaceholder();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawTargetSelection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("目标对象", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var newTarget = EditorGUILayout.ObjectField(
                "Avatar / 带 Animator / 控制器",
                _targetObject,
                typeof(Object),
                true);

            if (EditorGUI.EndChangeCheck())
            {
                _targetObject = newTarget;
                RefreshTargetComponents();
                RefreshControllers();
            }

            if (_targetObject == null)
            {
                EditorGUILayout.HelpBox("请拖入一个VRChat Avatar、带 Animator 组件的物体，或直接拖入 动画控制器 资产。", MessageType.Info);
            }
            else if (_controllers.Count == 0)
            {
                EditorGUILayout.HelpBox("在当前目标中未找到任何 动画控制器，请确认 Avatar/物体已正确配置动画控制器。", MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawDefaultSettingsArea()
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

            // 仅当源状态为 Any State 时才显示
            if (_selectedSourceStateName == "Any State")
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

        private void RefreshTargetComponents()
        {
            _avatarDescriptor = null;
            _animator = null;

            if (_targetObject is GameObject go)
            {
                _avatarDescriptor = go.GetComponent<VRCAvatarDescriptor>();
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
            {
                return;
            }

            // 如果目标是 AnimatorController 资产，直接加入列表
            if (_targetObject is AnimatorController controllerAsset)
            {
                _controllers.Add(controllerAsset);
                _controllerNames.Add("Animator Controller: " + controllerAsset.name);
                return;
            }

            // 如果目标是 GameObject，则使用公共工具方法从 Avatar/Animator 根收集控制器
            if (_targetObject is GameObject root)
            {
                _controllers.AddRange(ToolboxUtils.CollectControllersFromRoot(root, includeSpecialLayers: true));
                if (_controllers.Count == 0)
                {
                    return;
                }

                _controllerNames.AddRange(ToolboxUtils.BuildControllerDisplayNames(_avatarDescriptor, _animator, _controllers));
            }
        }

        private void DrawControllerSelection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if (_controllers.Count == 0)
            {
                EditorGUILayout.LabelField("(无可用控制器)");
            }
            else
            {
                if (_selectedControllerIndex < 0 || _selectedControllerIndex >= _controllers.Count)
                {
                    _selectedControllerIndex = 0;
                }

                _selectedControllerIndex = EditorGUILayout.Popup("选择控制器", _selectedControllerIndex, _controllerNames.ToArray());

                // 在同一块区域内选择层级
                var controller = _controllers[_selectedControllerIndex];
                var layers = controller.layers ?? System.Array.Empty<AnimatorControllerLayer>();
                if (layers.Length == 0)
                {
                    EditorGUILayout.HelpBox("当前 AnimatorController 中没有任何层级。请先在 Animator 中添加层。", MessageType.Warning);
                }
                else
                {
                    if (_selectedLayerIndex < 0 || _selectedLayerIndex >= layers.Length)
                    {
                        _selectedLayerIndex = 0;
                    }

                    var layerNames = new string[layers.Length];
                    for (int i = 0; i < layers.Length; i++)
                    {
                        layerNames[i] = string.IsNullOrEmpty(layers[i].name) ? $"Layer {i}" : layers[i].name;
                    }

                    _selectedLayerIndex = EditorGUILayout.Popup("选择层级", _selectedLayerIndex, layerNames);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawModeSelection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("模式", EditorStyles.boldLabel);

            // 先选择模式，再在后续区域中绘制“目标状态”等内容
            var modes = new[] { "创建过渡", "修改过渡" };
            _toolMode = (ToolMode)GUILayout.Toolbar((int)_toolMode, modes);

            EditorGUILayout.EndVertical();
        }

        private void DrawWorkAreaPlaceholder()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (_controllers.Count == 0)
            {
                EditorGUILayout.LabelField("未找到可用 AnimatorController，无法配置过渡。", EditorStyles.wordWrappedLabel);
            }
            else
            {
                // 根据模式绘制对应工作区（层级选择已在控制器区域中完成）
                if (_toolMode == ToolMode.CreateTransitions)
                {
                    DrawCreateModeArea();
                }
                else
                {
                    DrawModifyModeArea();
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawCreateModeArea()
        {
            var controller = _controllers[_selectedControllerIndex];
            var layers = controller.layers ?? System.Array.Empty<AnimatorControllerLayer>();
            if (layers.Length == 0)
            {
                EditorGUILayout.HelpBox("当前 AnimatorController 中没有任何层级，无法创建过渡。", MessageType.Warning);
                return;
            }

            var layer = layers[Mathf.Clamp(_selectedLayerIndex, 0, layers.Length - 1)];

            DrawStateSelectionArea(layer);
            EditorGUILayout.Space();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("默认过渡设置", EditorStyles.boldLabel);
            DrawDefaultSettingsArea();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("全局条件", EditorStyles.boldLabel);
            DrawGlobalConditionsArea(controller);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            EditorGUILayout.LabelField("待创建过渡列表", EditorStyles.boldLabel);
            SyncGlobalConditionsToTransitions();
            DrawTransitionListArea(controller);

            EditorGUILayout.Space();

            // 创建过渡按钮：当没有条目或未选择状态时禁用
            bool canCreate =
                _createItems.Count > 0 &&
                !string.IsNullOrEmpty(_selectedSourceStateName) &&
                !string.IsNullOrEmpty(_selectedDestinationStateName);

            EditorGUI.BeginDisabledGroup(!canCreate);
            if (GUILayout.Button("创建过渡", GUILayout.Height(32f)))
            {
                CreateTransitionsWithService();
            }
            EditorGUI.EndDisabledGroup();
        }

        private void DrawStateSelectionArea(AnimatorControllerLayer layer)
        {
            var displayNames = new List<string>();
            var paths = new List<string>();
            CollectAllStatePaths(layer.stateMachine, displayNames, paths, string.Empty);

            // 源状态：Any State + 所有状态路径
            var sourceDisplay = new List<string> { "Any State" };
            sourceDisplay.AddRange(displayNames);

            var sourcePaths = new List<string> { "Any State" };
            sourcePaths.AddRange(paths);

            if (!sourcePaths.Contains(_selectedSourceStateName))
            {
                _selectedSourceStateName = "Any State";
            }

            int sourceIndex = sourcePaths.IndexOf(_selectedSourceStateName);
            sourceIndex = EditorGUILayout.Popup("源状态", sourceIndex, sourceDisplay.ToArray());
            _selectedSourceStateName = sourcePaths[Mathf.Clamp(sourceIndex, 0, sourcePaths.Count - 1)];

            // 目标状态：所有状态路径；当源状态不是 Any State 时再追加 Exit
            if (paths.Count == 0)
            {
                EditorGUILayout.HelpBox("当前层中没有任何状态，无法创建过渡。", MessageType.Warning);
                return;
            }

            var destDisplay = new List<string>(displayNames);
            var destValues = new List<string>(paths);

            // 只有当源状态是具体状态时才允许目标为 Exit
            if (_selectedSourceStateName != "Any State")
            {
                destDisplay.Add("Exit");
                destValues.Add("Exit");
            }

            if (!destValues.Contains(_selectedDestinationStateName))
            {
                _selectedDestinationStateName = destValues[0];
            }

            int destIndex = destValues.IndexOf(_selectedDestinationStateName);
            if (destIndex < 0) destIndex = 0;
            destIndex = EditorGUILayout.Popup("目标状态", destIndex, destDisplay.ToArray());
            _selectedDestinationStateName = destValues[Mathf.Clamp(destIndex, 0, destValues.Count - 1)];
        }

        private void CollectAllStatePaths(AnimatorStateMachine stateMachine, List<string> displayNames, List<string> paths, string parentPath)
        {
            foreach (var childState in stateMachine.states)
            {
                if (childState.state == null) continue;

                string path = string.IsNullOrEmpty(parentPath)
                    ? childState.state.name
                    : parentPath + "/" + childState.state.name;

                paths.Add(path);
                displayNames.Add(path);
            }

            foreach (var subStateMachine in stateMachine.stateMachines)
            {
                if (subStateMachine.stateMachine == null) continue;

                string subPath = string.IsNullOrEmpty(parentPath)
                    ? subStateMachine.stateMachine.name
                    : parentPath + "/" + subStateMachine.stateMachine.name;

                CollectAllStatePaths(subStateMachine.stateMachine, displayNames, paths, subPath);
            }
        }

        private void DrawGlobalConditionsArea(AnimatorController controller)
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

                var validParameters = controller.parameters;
                var nonTriggerParams = new List<AnimatorControllerParameter>();
                foreach (var p in validParameters)
                {
                    if (p != null && p.type != AnimatorControllerParameterType.Trigger)
                    {
                        nonTriggerParams.Add(p);
                    }
                }

                string[] parameterNames = new string[nonTriggerParams.Count];
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
                if (controller != null)
                {
                    var hasNonTrigger = false;
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
                        _globalConditions.Add(new Condition
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
                        transition.conditions.Add(new Condition
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

        private void DrawTransitionListArea(AnimatorController controller)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // 顶部“添加过渡”按钮
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
                // 当列表为空时不再显示额外提示文字，仅保持空列表区域
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
                DrawTransitionConditionsList(controller, item.conditions);

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }

            // 在循环结束后统一删除，避免 IMGUI 布局状态错误
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

            if (_selectedSourceStateName == "Any State" && settings.overrideCanTransitionToSelf)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Can Transition To Self", GUILayout.Width(150f));
                settings.canTransitionToSelf = EditorGUILayout.Toggle(settings.canTransitionToSelf, GUILayout.ExpandWidth(true));
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawTransitionConditionsList(AnimatorController controller, List<Condition> conditions)
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

                string[] parameterNames = new string[nonTriggerParams.Count];
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
                    // 空白占位宽度 = 全局提示宽度 - 按钮宽度 - 2 * IMGUI 默认间距，让两种情况下总占用宽度在视觉上尽量一致
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
                    var hasNonTrigger = false;
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
                        conditions.Add(new Condition
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

        // 根据参数类型绘制条件的模式和值（Bool/Float/Int）。
        private void DrawConditionValueFields(Condition condition)
        {
            // 当尚未选择参数时，不绘制实际控件，仅用占位空白保持行宽稳定
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

                    // 为 Bool 条件补一个与 Float/Int 数值输入框等宽的空白占位，避免没有输入框时行宽收缩
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

        /// <summary>
        /// 修改过渡模式的完整 UI：状态选择 + 修改模式 + 过渡属性 + 条件设置列表。
        /// 仅负责编辑器界面与数据，不直接修改 Animator。
        /// </summary>
        private void DrawModifyModeArea()
        {
            var controller = _controllers[_selectedControllerIndex];
            var layers = controller.layers ?? System.Array.Empty<AnimatorControllerLayer>();
            if (layers.Length == 0)
            {
                EditorGUILayout.HelpBox("当前 AnimatorController 中没有任何层级，无法修改过渡。", MessageType.Warning);
                return;
            }

            var layer = layers[Mathf.Clamp(_selectedLayerIndex, 0, layers.Length - 1)];

            // 修改模式（由选择的状态出发 / 到达选择的状态）
            var modifyModeOptions = new[] { "由选择的状态出发", "到达选择的状态" };
            int modeIndex = (int)_modifyMode;
            modeIndex = EditorGUILayout.Popup("修改模式", modeIndex, modifyModeOptions);
            _modifyMode = (ModifyMode)modeIndex;

            // 目标状态
            DrawModifyStateSelection(layer);

            EditorGUILayout.Space();

            // 过渡属性设置
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("过渡属性", EditorStyles.boldLabel);

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

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            // 条件设置
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            _modifyConditions = EditorGUILayout.Toggle(_modifyConditions, GUILayout.Width(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.LabelField("条件设置", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
            if (_modifyConditions)
            {
                EditorGUILayout.Space();

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("添加条件", GUILayout.Width(120f)))
                {
                    _modifyConditionSettings.Add(new ModifyConditionSetting());
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space();

                // 逆序遍历，方便安全删除
                for (int i = _modifyConditionSettings.Count - 1; i >= 0; i--)
                {
                    var setting = _modifyConditionSettings[i];
                    if (setting == null)
                    {
                        continue;
                    }

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    setting.Draw(controller, _modifyMode);

                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space(5f);

                    if (setting.requestRemove)
                    {
                        _modifyConditionSettings.RemoveAt(i);
                    }
                }
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            // 应用按钮：当未选择目标状态时禁用
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_modifyStateName));
            if (GUILayout.Button("应用更改", GUILayout.Height(32f)))
            {
                ApplyModifyWithService();
            }
            EditorGUI.EndDisabledGroup();
        }

        /// <summary>
        /// 调用 QuickTransitionCreateService，根据当前创建模式的数据实际创建 Animator 过渡。
        /// </summary>
        private void CreateTransitionsWithService()
        {
            if (_controllers == null || _controllers.Count == 0)
            {
                return;
            }

            var controller = _controllers[_selectedControllerIndex];
            if (controller == null)
            {
                return;
            }

            var layers = controller.layers ?? System.Array.Empty<AnimatorControllerLayer>();
            if (layers.Length == 0)
            {
                return;
            }

            var layer = layers[Mathf.Clamp(_selectedLayerIndex, 0, layers.Length - 1)];

            // 组装过渡设置
            var settingsList = new List<QuickTransitionCreateService.TransitionSettings>(_createItems.Count);
            for (int i = 0; i < _createItems.Count; i++)
            {
                var item = _createItems[i];
                if (item == null)
                {
                    continue;
                }

                var ts = new QuickTransitionCreateService.TransitionSettings
                {
                    hasExitTimeOverride = item.overrideHasExitTime,
                    hasExitTime = item.hasExitTime,
                    exitTimeOverride = item.overrideExitTime,
                    exitTime = item.exitTime,
                    hasFixedDurationOverride = item.overrideHasFixedDuration,
                    hasFixedDuration = item.hasFixedDuration,
                    durationOverride = item.overrideDuration,
                    duration = item.duration,
                    offsetOverride = item.overrideOffset,
                    offset = item.offset,
                    canTransitionToSelfOverride = item.overrideCanTransitionToSelf,
                    canTransitionToSelf = item.canTransitionToSelf,
                    conditions = new List<QuickTransitionCreateService.ConditionSettings>()
                };

                if (item.conditions != null)
                {
                    for (int ci = 0; ci < item.conditions.Count; ci++)
                    {
                        var cond = item.conditions[ci];
                        if (cond == null || string.IsNullOrEmpty(cond.parameterName))
                        {
                            continue;
                        }

                        ts.conditions.Add(new QuickTransitionCreateService.ConditionSettings
                        {
                            parameterName = cond.parameterName,
                            parameterType = cond.parameterType,
                            floatValue = cond.floatValue,
                            intValue = cond.intValue,
                            boolValue = cond.boolValue,
                            mode = cond.mode
                        });
                    }
                }

                settingsList.Add(ts);
            }

            if (settingsList.Count == 0)
            {
                return;
            }

            QuickTransitionCreateService.CreateTransitions(
                controller,
                layer,
                _selectedSourceStateName,
                _selectedDestinationStateName,
                _defaultHasExitTime,
                _defaultExitTime,
                _defaultHasFixedDuration,
                _defaultDuration,
                _defaultOffset,
                _defaultCanTransitionToSelf,
                settingsList);
        }

        /// <summary>
        /// 修改模式下的状态选择 UI：支持普通状态和 Exit（当模式为 ToStateTransitions 时）。
        /// </summary>
        private void DrawModifyStateSelection(AnimatorControllerLayer layer)
        {
            var displayNames = new List<string>();
            var paths = new List<string>();
            CollectAllStatePaths(layer.stateMachine, displayNames, paths, string.Empty);

            var optionDisplay = new List<string>();
            var optionValues = new List<string>();

            if (_modifyMode == ModifyMode.FromStateTransitions)
            {
                optionDisplay.Add("Any State");
                optionValues.Add("Any State");
            }

            optionDisplay.AddRange(displayNames);
            optionValues.AddRange(paths);

            if (_modifyMode == ModifyMode.ToStateTransitions)
            {
                optionDisplay.Add("Exit");
                optionValues.Add("Exit");
            }

            if (optionValues.Count == 0)
            {
                EditorGUILayout.HelpBox("当前层中没有任何状态，无法修改过渡。", MessageType.Warning);
                _modifyStateName = string.Empty;
                return;
            }

            int currentIndex = optionValues.IndexOf(_modifyStateName);
            if (currentIndex < 0)
            {
                currentIndex = 0;
                _modifyStateName = optionValues[0];
            }

            _modifySelectedStateIndex = currentIndex;

            int newIndex = EditorGUILayout.Popup("目标状态", _modifySelectedStateIndex, optionDisplay.ToArray());
            if (newIndex != _modifySelectedStateIndex)
            {
                _modifySelectedStateIndex = newIndex;
                _modifyStateName = optionValues[_modifySelectedStateIndex];
            }
        }

        /// <summary>
        /// 调用 QuickTransitionModifyService，根据当前修改模式的数据批量修改 Animator 过渡。
        /// </summary>
        private void ApplyModifyWithService()
        {
            if (_controllers == null || _controllers.Count == 0)
            {
                return;
            }

            var controller = _controllers[_selectedControllerIndex];
            if (controller == null)
            {
                return;
            }

            var layers = controller.layers ?? System.Array.Empty<AnimatorControllerLayer>();
            if (layers.Length == 0)
            {
                return;
            }

            var layer = layers[Mathf.Clamp(_selectedLayerIndex, 0, layers.Length - 1)];

            // 映射修改模式
            QuickTransitionModifyService.ModifyMode serviceMode =
                _modifyMode == ModifyMode.FromStateTransitions
                    ? QuickTransitionModifyService.ModifyMode.FromStateTransitions
                    : QuickTransitionModifyService.ModifyMode.ToStateTransitions;

            // 映射条件设置
            List<QuickTransitionModifyService.ConditionSetting> serviceConditions = null;
            if (_modifyConditions && _modifyConditionSettings.Count > 0)
            {
                serviceConditions = new List<QuickTransitionModifyService.ConditionSetting>(_modifyConditionSettings.Count);

                foreach (var setting in _modifyConditionSettings)
                {
                    if (setting == null)
                    {
                        continue;
                    }

                    var serviceCond = new QuickTransitionModifyService.ConditionSetting
                    {
                        parameterName = setting.parameterName,
                        mode = setting.mode,
                        threshold = setting.threshold,
                        enableIntAutoIncrement = setting.enableIntAutoIncrement,
                        incrementDirection = setting.incrementDirection == ModifyConditionSetting.IntIncrementDirection.Increment
                            ? QuickTransitionModifyService.IntIncrementDirection.Increment
                            : QuickTransitionModifyService.IntIncrementDirection.Decrement,
                        sortMode = setting.sortMode == ModifyConditionSetting.SortMode.ArrangementOrder
                            ? QuickTransitionModifyService.SortMode.ArrangementOrder
                            : QuickTransitionModifyService.SortMode.NameNumberOrder,
                        incrementStep = setting.incrementStep,
                        floatIncrementStep = setting.floatIncrementStep
                    };

                    serviceConditions.Add(serviceCond);
                }
            }

            QuickTransitionModifyService.ApplyChanges(
                controller,
                layer,
                serviceMode,
                _modifyStateName,
                _modifyHasExitTimeValue,
                _modifyExitTimeValue,
                _modifyHasFixedDurationValue,
                _modifyDurationValue,
                _modifyOffsetValue,
                _modifyConditions,
                serviceConditions);
        }
    }
}
