using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using MVA.Toolbox.QuickAddParameter.Services;

namespace MVA.Toolbox.QuickAddParameter.UI
{
    /// <summary>
    /// Quick Add Parameter 主窗口：提供将参数注册到 AnimatorController 或 ExpressionParameters 的界面。
    /// 具体数据与操作逻辑由 QuickAddParameterService 实现。
    /// </summary>
    public sealed class QuickAddParameterWindow : EditorWindow
    {
        enum ToolMode
        {
            ToController,
            ToParameters
        }

        UnityEngine.Object _targetObject;
        ToolMode _mode = ToolMode.ToController;
        Vector2 _scroll;

        AnimatorController _selectedController;
        VRCExpressionParameters _selectedParameters;

        bool _selectAll;
        bool _overwriteExisting;
        bool _filterUnregistered;

        bool _targetIsControllerAsset;

        // "注册到 Parameters" 模式下的列表滚动位置
        Vector2 _parametersScroll;

        // 在“注册到 Parameters”模式下记录每个控制器参数的选择、类型/默认值与保存/同步状态
        System.Collections.Generic.Dictionary<string, bool> _paramSelectFlags = new System.Collections.Generic.Dictionary<string, bool>();
        System.Collections.Generic.Dictionary<string, AnimatorControllerParameterType> _paramTypeOverrides = new System.Collections.Generic.Dictionary<string, AnimatorControllerParameterType>();
        System.Collections.Generic.Dictionary<string, float> _paramDefaultOverrides = new System.Collections.Generic.Dictionary<string, float>();
        System.Collections.Generic.Dictionary<string, bool> _saveFlags = new System.Collections.Generic.Dictionary<string, bool>();
        System.Collections.Generic.Dictionary<string, bool> _syncFlags = new System.Collections.Generic.Dictionary<string, bool>();

        QuickAddParameterService _service;

        [MenuItem("Tools/MVA Toolbox/Quick Add Parameter", false, 6)]
        public static void Open()
        {
            var w = GetWindow<QuickAddParameterWindow>("Quick Add Parameter");
            w.minSize = new Vector2(550f, 600f);
        }

        void OnEnable()
        {
            if (_service == null)
            {
                _service = new QuickAddParameterService();
            }
        }

        void OnGUI()
        {
            if (_service == null)
            {
                _service = new QuickAddParameterService();
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawTargetSelection();

            GUILayout.Space(4f);

            if (_targetObject == null)
            {
                EditorGUILayout.HelpBox("请拖入 VRChat Avatar 或带Animator 组件的物体，或直接拖入 动画控制器", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawModeSelection();

            GUILayout.Space(4f);

            DrawControllerAndParametersSelection();

            GUILayout.Space(4f);
            if (_mode == ToolMode.ToController)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawCommonOptions();
                GUILayout.Space(4f);
                DrawParameterListForController();
                EditorGUILayout.EndVertical();

                GUILayout.Space(4f);
                DrawActionsForController();
            }
            else
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawCommonOptions();
                GUILayout.Space(4f);
                DrawParameterListForParameters();
                EditorGUILayout.EndVertical();

                GUILayout.Space(4f);
                DrawActionsForParameters();
            }

            EditorGUILayout.EndScrollView();
        }

        void DrawTargetSelection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("目标对象", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var newTarget = EditorGUILayout.ObjectField("Avatar / 带 Animator / 控制器", _targetObject, typeof(UnityEngine.Object), true);
            if (EditorGUI.EndChangeCheck())
            {
                _targetObject = newTarget;
                _selectedController = null;
                _selectedParameters = null;
                _targetIsControllerAsset = false;

                if (_targetObject is GameObject go)
                {
                    _service.SetTarget(go);

                    var avatar = go.GetComponent<VRCAvatarDescriptor>();
                    if (avatar != null)
                    {
                        _selectedParameters = avatar.expressionParameters;
                        _service.ExpressionParameters = _selectedParameters;
                    }
                }
                else if (_targetObject is AnimatorController controllerAsset)
                {
                    _targetIsControllerAsset = true;
                    _selectedController = controllerAsset;
                    _service.SetControllerAsset(controllerAsset);

                    _mode = ToolMode.ToParameters;
                    _service.Mode = QuickAddParameterService.ToolMode.ToParameters;
                }
                else if (_targetObject == null)
                {
                    _service.SetTarget(null);
                }
            }

            EditorGUILayout.EndVertical();
        }

        void DrawModeSelection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("模式", EditorStyles.boldLabel);
            var labels = new[] { "注册到 Controller", "注册到 Parameters" };

            if (_targetIsControllerAsset)
            {
                // 控制器资产模式下锁定为 Parameters 模式
                _mode = ToolMode.ToParameters;
                _service.Mode = QuickAddParameterService.ToolMode.ToParameters;
                EditorGUI.BeginDisabledGroup(true);
                GUILayout.Toolbar((int)_mode, labels);
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                var newMode = (ToolMode)GUILayout.Toolbar((int)_mode, labels);
                if (newMode != _mode)
                {
                    _mode = newMode;
                    _service.Mode = _mode == ToolMode.ToController
                        ? QuickAddParameterService.ToolMode.ToController
                        : QuickAddParameterService.ToolMode.ToParameters;
                }
            }

            EditorGUILayout.EndVertical();
        }

        void DrawControllerAndParametersSelection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var controllers = _service.Controllers;
            var names = _service.ControllerNames;
            int index = _service.SelectedControllerIndex;

            if (controllers.Count == 0)
            {
                EditorGUILayout.HelpBox("在目标对象中未找到任何 AnimatorController。", MessageType.Warning);
            }
            else
            {
                string[] display = new string[names.Count];
                for (int i = 0; i < names.Count; i++)
                {
                    display[i] = names[i];
                }

                EditorGUI.BeginChangeCheck();
                index = EditorGUILayout.Popup("选择动画控制器", index, display);
                if (EditorGUI.EndChangeCheck())
                {
                    _service.SelectedControllerIndex = index;
                    _selectedController = _service.SelectedController;
                }
            }

            if (_mode == ToolMode.ToParameters)
            {
                GUILayout.Space(4f);
                EditorGUI.BeginChangeCheck();
                var newParams = (VRCExpressionParameters)EditorGUILayout.ObjectField(
                    "ExpressionParameters",
                    _selectedParameters,
                    typeof(VRCExpressionParameters),
                    false);
                if (EditorGUI.EndChangeCheck())
                {
                    _selectedParameters = newParams;
                    _service.ExpressionParameters = _selectedParameters;
                }

                if (_selectedParameters == null)
                {
                    EditorGUILayout.HelpBox("需要先指定一个 VRCExpressionParameters 资源才能使用“注册到 Parameters”模式。", MessageType.Info);
                }
            }

            EditorGUILayout.EndVertical();
        }

        void DrawCommonOptions()
        {
            EditorGUILayout.BeginHorizontal();
            bool newSelectAll = EditorGUILayout.ToggleLeft("全选", _selectAll, GUILayout.Width(80f));
            if (newSelectAll != _selectAll)
            {
                _selectAll = newSelectAll;
                if (_mode == ToolMode.ToController)
                {
                    var controller = _service.SelectedController;
                    System.Collections.Generic.HashSet<string> registered = null;
                    if (_filterUnregistered && controller != null)
                    {
                        registered = new System.Collections.Generic.HashSet<string>();
                        var ctrlParams = controller.parameters ?? System.Array.Empty<AnimatorControllerParameter>();
                        for (int i = 0; i < ctrlParams.Length; i++)
                        {
                            var cp = ctrlParams[i];
                            if (cp != null && !string.IsNullOrEmpty(cp.name))
                            {
                                registered.Add(cp.name);
                            }
                        }
                    }

                    foreach (var p in _service.AllParameters)
                    {
                        if (registered != null && !string.IsNullOrEmpty(p.Name) && registered.Contains(p.Name))
                        {
                            // 筛选模式下跳过已注册的参数
                            continue;
                        }

                        p.IsSelected = _selectAll;
                    }
                }
                else
                {
                    foreach (var p in _service.AllParameters)
                    {
                        p.IsSelected = _selectAll;
                    }
                }
            }

            GUILayout.Space(10f);
            if (_mode == ToolMode.ToController)
            {
                bool newOverwrite = EditorGUILayout.ToggleLeft("覆盖参数", _overwriteExisting, GUILayout.Width(100f));
                if (newOverwrite != _overwriteExisting)
                {
                    _overwriteExisting = newOverwrite;
                    _service.OverwriteExisting = _overwriteExisting;
                }

                GUILayout.Space(10f);
            }

            bool newFilter = EditorGUILayout.ToggleLeft("筛选未注册的参数", _filterUnregistered);
            if (newFilter != _filterUnregistered)
            {
                _filterUnregistered = newFilter;
                _service.FilterUnregistered = _filterUnregistered;
            }

            EditorGUILayout.EndHorizontal();
        }

        void DrawParameterListForController()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("参数列表", EditorStyles.boldLabel);

            var contact = _service.ContactReceiverParameters;
            var groups = _service.PhysBoneGroups;
            var controller = _service.SelectedController;

            System.Collections.Generic.HashSet<string> registered = null;
            if (_service.FilterUnregistered && controller != null)
            {
                registered = new System.Collections.Generic.HashSet<string>();
                var ctrlParams = controller.parameters ?? System.Array.Empty<AnimatorControllerParameter>();
                for (int i = 0; i < ctrlParams.Length; i++)
                {
                    var cp = ctrlParams[i];
                    if (cp != null && !string.IsNullOrEmpty(cp.name))
                    {
                        registered.Add(cp.name);
                    }
                }
            }

            if (contact.Count == 0 && groups.Count == 0)
            {
                EditorGUILayout.HelpBox("未找到任何参数。请检查物体上是否有 VRC Contact Receiver 或 VRC Phys Bone 组件。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            var scroll = Vector2.zero;
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(320f));

            for (int i = 0; i < contact.Count; i++)
            {
                var param = contact[i];
                if (registered != null && !string.IsNullOrEmpty(param.Name) && registered.Contains(param.Name))
                {
                    // 已注册且启用筛选时跳过
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawSingleParameterRow(param, false);
                EditorGUILayout.EndVertical();
            }

            for (int i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                bool anyVisible = false;

                // 先检查该组中是否存在未注册的参数
                if (registered != null)
                {
                    for (int j = 0; j < group.Parameters.Count; j++)
                    {
                        var param = group.Parameters[j];
                        if (string.IsNullOrEmpty(param.Name) || !registered.Contains(param.Name))
                        {
                            anyVisible = true;
                            break;
                        }
                    }
                    if (!anyVisible)
                    {
                        continue;
                    }
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("VRC Phys Bone: " + group.BaseName, EditorStyles.boldLabel);
                GUILayout.Space(3f);
                for (int j = 0; j < group.Parameters.Count; j++)
                {
                    var param = group.Parameters[j];
                    if (registered != null && !string.IsNullOrEmpty(param.Name) && registered.Contains(param.Name))
                    {
                        continue;
                    }

                    DrawSingleParameterRow(param, true);
                }

                EditorGUILayout.EndVertical();
                GUILayout.Space(3f);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void DrawSingleParameterRow(QuickAddParameterService.ParameterInfo param, bool indent)
        {
            if (indent)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(20f);
                EditorGUILayout.BeginVertical();
            }

            EditorGUILayout.BeginHorizontal();

            param.IsSelected = EditorGUILayout.Toggle(param.IsSelected, GUILayout.Width(20f));

            EditorGUILayout.LabelField("参数名:", GUILayout.Width(60f));
            EditorGUILayout.LabelField(param.Name, EditorStyles.boldLabel, GUILayout.MinWidth(120f));

            EditorGUILayout.LabelField("类型:", GUILayout.Width(40f));
            string[] typeOptions = { "Bool", "Float", "Int" };
            int currentTypeIndex = param.Type == AnimatorControllerParameterType.Bool ? 0 :
                                   param.Type == AnimatorControllerParameterType.Float ? 1 : 2;
            int newTypeIndex = EditorGUILayout.Popup(currentTypeIndex, typeOptions, GUILayout.Width(80f));
            param.Type = newTypeIndex == 0 ? AnimatorControllerParameterType.Bool :
                         newTypeIndex == 1 ? AnimatorControllerParameterType.Float :
                         AnimatorControllerParameterType.Int;

            EditorGUILayout.LabelField("默认值:", GUILayout.Width(60f));
            switch (param.Type)
            {
                case AnimatorControllerParameterType.Bool:
                    param.DefaultBool = EditorGUILayout.Toggle(param.DefaultBool, GUILayout.Width(80f));
                    break;
                case AnimatorControllerParameterType.Float:
                    param.DefaultFloat = EditorGUILayout.FloatField(param.DefaultFloat, GUILayout.Width(80f));
                    break;
                case AnimatorControllerParameterType.Int:
                    param.DefaultInt = EditorGUILayout.IntField(param.DefaultInt, GUILayout.Width(80f));
                    break;
            }

            EditorGUILayout.EndHorizontal();

            if (indent)
            {
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
            }
        }

        void DrawActionsForController()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            bool hasSelection = false;
            foreach (var p in _service.AllParameters)
            {
                if (p.IsSelected)
                {
                    hasSelection = true;
                    break;
                }
            }

            bool hasController = _service.SelectedController != null;

            GUI.enabled = hasSelection && hasController;
            if (GUILayout.Button("添加参数到控制器", GUILayout.Height(32f)))
            {
                _service.ApplyToController();
            }

            GUI.enabled = true;

            EditorGUILayout.EndVertical();
        }

        void DrawParameterListForParameters()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("参数列表 (来自 Controller)", EditorStyles.boldLabel);

            if (_service.SelectedController == null)
            {
                EditorGUILayout.HelpBox("请先选择动画控制器。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            if (_selectedParameters == null)
            {
                EditorGUILayout.HelpBox("请先选择一个 VRCExpressionParameters 资源。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            var controller = _service.SelectedController;
            var controllerParams = controller.parameters ?? System.Array.Empty<AnimatorControllerParameter>();

            System.Collections.Generic.HashSet<string> existingNames = null;
            if (_filterUnregistered && _selectedParameters.parameters != null)
            {
                existingNames = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
                foreach (var ep in _selectedParameters.parameters)
                {
                    if (ep != null && !string.IsNullOrEmpty(ep.name))
                    {
                        existingNames.Add(ep.name);
                    }
                }
            }

            if (controllerParams.Length == 0)
            {
                EditorGUILayout.HelpBox("当前动画控制器中没有任何参数。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            _parametersScroll = EditorGUILayout.BeginScrollView(_parametersScroll, GUILayout.Height(320f));

            for (int i = 0; i < controllerParams.Length; i++)
            {
                var p = controllerParams[i];
                if (p == null) continue;

                if (existingNames != null && !string.IsNullOrEmpty(p.name) && existingNames.Contains(p.name))
                {
                    // 筛选模式下跳过已存在于 ExpressionParameters 的条目
                    continue;
                }

                string key = p.name ?? string.Empty;
                if (!_paramSelectFlags.ContainsKey(key)) _paramSelectFlags[key] = false;
                if (!_paramTypeOverrides.ContainsKey(key))
                {
                    _paramTypeOverrides[key] = p.type;
                }

                if (!_paramDefaultOverrides.ContainsKey(key))
                {
                    float def = 0f;
                    switch (p.type)
                    {
                        case AnimatorControllerParameterType.Bool:
                            def = p.defaultBool ? 1f : 0f;
                            break;
                        case AnimatorControllerParameterType.Float:
                            def = p.defaultFloat;
                            break;
                        case AnimatorControllerParameterType.Int:
                            def = p.defaultInt;
                            break;
                    }
                    _paramDefaultOverrides[key] = def;
                }

                if (!_saveFlags.ContainsKey(key)) _saveFlags[key] = false;
                if (!_syncFlags.ContainsKey(key)) _syncFlags[key] = false;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();

                _paramSelectFlags[key] = EditorGUILayout.Toggle(_paramSelectFlags[key], GUILayout.Width(20f));

                EditorGUILayout.LabelField("参数名:", GUILayout.Width(60f));
                EditorGUILayout.LabelField(string.IsNullOrEmpty(p.name) ? "(Unnamed)" : p.name, EditorStyles.boldLabel, GUILayout.MinWidth(120f));

                EditorGUILayout.LabelField("类型:", GUILayout.Width(40f));
                string[] typeOptions = { "Bool", "Float", "Int" };
                AnimatorControllerParameterType currentType = _paramTypeOverrides[key];
                int currentTypeIndex = currentType == AnimatorControllerParameterType.Bool ? 0 :
                                       currentType == AnimatorControllerParameterType.Float ? 1 : 2;
                int newTypeIndex = EditorGUILayout.Popup(currentTypeIndex, typeOptions, GUILayout.Width(80f));
                var displayType = newTypeIndex == 0 ? AnimatorControllerParameterType.Bool :
                                  newTypeIndex == 1 ? AnimatorControllerParameterType.Float :
                                  AnimatorControllerParameterType.Int;
                _paramTypeOverrides[key] = displayType;

                EditorGUILayout.LabelField("默认值:", GUILayout.Width(60f));
                float currentDefault = _paramDefaultOverrides[key];
                switch (displayType)
                {
                    case AnimatorControllerParameterType.Bool:
                        bool boolVal = currentDefault > 0.5f;
                        boolVal = EditorGUILayout.Toggle(boolVal, GUILayout.Width(60f));
                        currentDefault = boolVal ? 1f : 0f;
                        break;
                    case AnimatorControllerParameterType.Float:
                        float floatVal = currentDefault;
                        floatVal = EditorGUILayout.FloatField(floatVal, GUILayout.Width(60f));
                        currentDefault = floatVal;
                        break;
                    case AnimatorControllerParameterType.Int:
                        int intVal = Mathf.RoundToInt(currentDefault);
                        intVal = EditorGUILayout.IntField(intVal, GUILayout.Width(60f));
                        currentDefault = intVal;
                        break;
                }
                _paramDefaultOverrides[key] = currentDefault;

                GUILayout.Space(10f);
                _saveFlags[key] = EditorGUILayout.ToggleLeft("保存", _saveFlags[key], GUILayout.Width(60f));
                GUILayout.Space(4f);
                _syncFlags[key] = EditorGUILayout.ToggleLeft("同步", _syncFlags[key], GUILayout.Width(60f));

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        void DrawActionsForParameters()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            bool hasController = _service.SelectedController != null;
            bool hasParams = _selectedParameters != null;

            bool hasSelection = false;
            if (hasController && hasParams)
            {
                foreach (var kv in _paramSelectFlags)
                {
                    if (!kv.Value) continue;

                    var key = kv.Key;
                    bool saveFlag = _saveFlags.TryGetValue(key, out var s) && s;
                    bool syncFlag = _syncFlags.TryGetValue(key, out var y) && y;
                    if (saveFlag || syncFlag)
                    {
                        hasSelection = true;
                        break;
                    }
                }
            }

            GUI.enabled = hasController && hasParams && hasSelection;
            if (GUILayout.Button("添加到 Parameters", GUILayout.Height(32f)))
            {
                _service.ApplyToParameters(_paramSelectFlags, _saveFlags, _syncFlags, _paramTypeOverrides, _paramDefaultOverrides);
            }

            GUI.enabled = true;

            EditorGUILayout.EndVertical();
        }
    }
}
