using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Avatars.Components;
using MVA.Toolbox.QuickAnimatorEdit.Services.Shared;
using MVA.Toolbox.QuickAnimatorEdit.Services.Parameter;

namespace MVA.Toolbox.QuickAnimatorEdit.Windows
{
    /// <summary>
    /// 参数功能面板
    /// 子功能：扫描参数 / 写入参数 / 参数检查
    /// </summary>
    public sealed class QuickAnimatorEditParameterWindow
    {
        private enum ParameterMode
        {
            Scan,
            Apply,
            Check,
            Adjust
        }

        private const float _adjustFieldLabelWidth = 120f;

        private void EnsureAutoBindExpressionParameters()
        {
            var descriptor = _context.AvatarDescriptor;
            if (descriptor == _lastAvatarDescriptor)
            {
                return;
            }

            _lastAvatarDescriptor = descriptor;
            if (descriptor != null && descriptor.expressionParameters != null)
            {
                _expressionParameters = descriptor.expressionParameters;
            }
        }

        private void DrawAdjustRenameSection(AnimatorControllerParameter selectedParameter, IReadOnlyList<AnimatorController> controllers)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("新名称", GUILayout.Width(_adjustFieldLabelWidth));
            _adjustRenameNewName = EditorGUILayout.TextField(_adjustRenameNewName);
            EditorGUILayout.EndHorizontal();

            _adjustRenameApplyAll = EditorGUILayout.ToggleLeft("覆盖到全部控制器", _adjustRenameApplyAll);

            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_adjustRenameNewName) || _adjustRenameNewName == selectedParameter.name);
            if (GUILayout.Button("应用改名", GUILayout.Height(28f)))
            {
                ApplyAdjustAction("改名", controller =>
                {
                    return ParameterAdjustService.RenameParameter(controller, selectedParameter.name, _adjustRenameNewName);
                }, _adjustRenameApplyAll, controllers);
                _adjustRenameNewName = string.Empty;
            }
            EditorGUI.EndDisabledGroup();
        }

        private void DrawAdjustTypeSection(AnimatorControllerParameter selectedParameter, IReadOnlyList<AnimatorController> controllers)
        {
            int currentTypeIndex = System.Array.IndexOf(_typeOptionValues, _adjustTypeTargetType);
            if (currentTypeIndex < 0) currentTypeIndex = 0;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("目标类型", GUILayout.Width(_adjustFieldLabelWidth));
            currentTypeIndex = EditorGUILayout.Popup(currentTypeIndex, _typeOptionLabels);
            EditorGUILayout.EndHorizontal();
            _adjustTypeTargetType = _typeOptionValues[currentTypeIndex];

            _adjustTypeApplyAll = EditorGUILayout.ToggleLeft("覆盖到全部控制器", _adjustTypeApplyAll);

            EditorGUI.BeginDisabledGroup(_adjustTypeTargetType == selectedParameter.type);
            if (GUILayout.Button("应用类型调整", GUILayout.Height(28f)))
            {
                ApplyAdjustAction("改类型", controller =>
                {
                    return ParameterAdjustService.ChangeParameterType(controller, selectedParameter.name, _adjustTypeTargetType);
                }, _adjustTypeApplyAll, controllers);
            }
            EditorGUI.EndDisabledGroup();
        }

        private void DrawAdjustSwapSection(AnimatorControllerParameter selectedParameter, AnimatorControllerParameter[] parameters, IReadOnlyList<AnimatorController> controllers)
        {
            var sameTypeParams = parameters
                .Where(p => p != null && p.type == selectedParameter.type && p.name != selectedParameter.name)
                .ToArray();

            string[] sameTypeNames = sameTypeParams.Select(p => p.name).ToArray();
            if (sameTypeParams.Length == 0)
            {
                EditorGUILayout.HelpBox("没有同类型的其他参数可供替换。", MessageType.Info);
                return;
            }

            _adjustSecondParameterIndex = Mathf.Clamp(_adjustSecondParameterIndex, 0, sameTypeParams.Length - 1);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("替换对象", GUILayout.Width(_adjustFieldLabelWidth));
            _adjustSecondParameterIndex = EditorGUILayout.Popup(_adjustSecondParameterIndex, sameTypeNames);
            EditorGUILayout.EndHorizontal();
            var targetParameterName = sameTypeParams[_adjustSecondParameterIndex].name;

            _adjustSwapApplyAll = EditorGUILayout.ToggleLeft("覆盖到全部控制器", _adjustSwapApplyAll);

            if (GUILayout.Button("执行参数替换", GUILayout.Height(28f)))
            {
                ApplyAdjustAction("参数替换", controller =>
                {
                    return ParameterAdjustService.SwapParameters(controller, selectedParameter.name, targetParameterName);
                }, _adjustSwapApplyAll, controllers);
            }
        }

        private void EnsureAdjustSelection(AnimatorController controller, AnimatorControllerParameter[] parameters)
        {
            if (_adjustLastController != controller)
            {
                _adjustLastController = controller;
                _adjustParameterIndex = 0;
                _adjustSecondParameterIndex = 0;
                _adjustRenameNewName = string.Empty;
                _adjustTypeTargetType = parameters.Length > 0
                    ? parameters[0].type
                    : AnimatorControllerParameterType.Float;
            }

            _adjustParameterIndex = Mathf.Clamp(_adjustParameterIndex, 0, Mathf.Max(0, parameters.Length - 1));
            if (parameters.Length > 0)
            {
                var selected = parameters[_adjustParameterIndex];
                if (_adjustLastParameterName != selected.name)
                {
                    _adjustLastParameterName = selected.name;
                    _adjustTypeTargetType = selected.type;
                }
            }
        }

        private void ApplyAdjustAction(
            string actionName,
            System.Func<AnimatorController, bool> action,
            bool applyAll,
            IReadOnlyList<AnimatorController> controllers)
        {
            int successCount = 0;
            if (applyAll)
            {
                foreach (var ctrl in controllers)
                {
                    if (ctrl == null) continue;
                    if (action(ctrl))
                    {
                        successCount++;
                    }
                }
            }
            else
            {
                var controller = _context.SelectedController;
                if (controller != null && action(controller))
                {
                    successCount = 1;
                }
            }

            string message = successCount > 0
                ? $"已完成 {actionName} 操作，作用于 {successCount} 个控制器。"
                : $"没有控制器发生变化，请检查条件是否满足。";
            EditorUtility.DisplayDialog("参数调整", message, "确定");
        }

        private void DrawAdjustUI()
        {
            var controller = _context.SelectedController;
            if (controller == null)
            {
                EditorGUILayout.HelpBox("请先选择动画控制器。", MessageType.Warning);
                return;
            }

            var controllers = _context.Controllers;
            if (controllers == null || controllers.Count == 0)
            {
                EditorGUILayout.HelpBox("当前目标中没有可用的动画控制器。", MessageType.Warning);
                return;
            }

            var parameters = controller.parameters ?? System.Array.Empty<AnimatorControllerParameter>();
            if (parameters.Length == 0)
            {
                EditorGUILayout.HelpBox("当前动画控制器中没有参数。", MessageType.Info);
                return;
            }

            EnsureAdjustSelection(controller, parameters);

            var parameterNames = parameters.Select(p => p.name).ToArray();
            _adjustParameterIndex = Mathf.Clamp(_adjustParameterIndex, 0, parameters.Length - 1);
            var selectedParameter = parameters[_adjustParameterIndex];

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("参数", GUILayout.Width(_adjustFieldLabelWidth));
            int newParameterIndex = EditorGUILayout.Popup(_adjustParameterIndex, parameterNames);
            EditorGUILayout.EndHorizontal();
            if (newParameterIndex != _adjustParameterIndex)
            {
                _adjustParameterIndex = newParameterIndex;
                selectedParameter = parameters[_adjustParameterIndex];
                _adjustTypeTargetType = selectedParameter.type;
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("操作", GUILayout.Width(_adjustFieldLabelWidth));
            int newOperationIndex = EditorGUILayout.Popup((int)_adjustOperation, _adjustOperationLabels);
            EditorGUILayout.EndHorizontal();
            if (newOperationIndex != (int)_adjustOperation)
            {
                _adjustOperation = (AdjustOperation)newOperationIndex;
            }

            EditorGUILayout.Space(6f);
            switch (_adjustOperation)
            {
                case AdjustOperation.Rename:
                    DrawAdjustRenameSection(selectedParameter, controllers);
                    break;
                case AdjustOperation.ChangeType:
                    DrawAdjustTypeSection(selectedParameter, controllers);
                    break;
                case AdjustOperation.Swap:
                    DrawAdjustSwapSection(selectedParameter, parameters, controllers);
                    break;
            }
        }

        private ParameterMode _mode = ParameterMode.Scan;
        private QuickAnimatorEditContext _context;

        private Object _lastTargetObject;
        private VRCAvatarDescriptor _lastAvatarDescriptor;

        // 扫描结果
        private ParameterScanService.ScanResult _scanResult;
        private Vector2 _scanScrollPosition;
        private bool _selectAll;
        private bool _overwriteExisting;

        // ExpressionParameters 相关
        private VRCExpressionParameters _expressionParameters;
        private Dictionary<string, bool> _parameterSelectFlags = new Dictionary<string, bool>();
        private Dictionary<string, AnimatorControllerParameterType> _parameterTypeOverrides = new Dictionary<string, AnimatorControllerParameterType>();
        private Dictionary<string, float> _parameterDefaultOverrides = new Dictionary<string, float>();
        private Dictionary<string, bool> _parameterSaveFlags = new Dictionary<string, bool>();
        private Dictionary<string, bool> _parameterSyncFlags = new Dictionary<string, bool>();
        private bool _filterUnregistered;

        // Parameter Check
        private ParameterCheckService.CheckResult _checkResult;
        private Vector2 _checkScrollPosition;
        private List<ParameterIssueUI> _issueUIs = new List<ParameterIssueUI>();

        private class ParameterIssueUI
        {
            public ParameterCheckService.ParameterIssue Issue;
            public int SelectedOptionIndex; // 针对缺失引用：0 忽略 / 1 置空 / 2 替换
            public int SelectedExistingParamIndex = -1;
            public bool RemoveUnused;
            public bool ApplyTypeFix;
            public AnimatorControllerParameterType TypeFixTarget;
        }

        private enum AdjustOperation
        {
            Rename,
            ChangeType,
            Swap
        }

        private static readonly string[] _missingReferenceOptions = { "忽略", "置空引用", "替换为已有参数" };
        private static readonly AnimatorControllerParameterType[] _typeOptionValues = {
            AnimatorControllerParameterType.Bool,
            AnimatorControllerParameterType.Float,
            AnimatorControllerParameterType.Int
        };
        private static readonly string[] _typeOptionLabels = { "Bool", "Float", "Int" };
        private static readonly string[] _adjustOperationLabels = { "名称", "类型", "替换" };

        // Parameter Adjust
        private int _adjustParameterIndex;
        private int _adjustSecondParameterIndex;
        private string _adjustRenameNewName = string.Empty;
        private bool _adjustRenameApplyAll;
        private AnimatorControllerParameterType _adjustTypeTargetType = AnimatorControllerParameterType.Float;
        private bool _adjustTypeApplyAll;
        private bool _adjustSwapApplyAll;
        private AnimatorController _adjustLastController;
        private string _adjustLastParameterName;
        private AdjustOperation _adjustOperation = AdjustOperation.Rename;

        public QuickAnimatorEditParameterWindow(QuickAnimatorEditContext context)
        {
            _context = context;
        }

        /// <summary>
        /// 绘制参数功能面板
        /// </summary>
        public void OnGUI()
        {
            EnsureAutoBindExpressionParameters();

            // 模式选择区域
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("模式", EditorStyles.boldLabel);

            bool targetIsControllerAsset = _context.TargetObject is AnimatorController;
            var modeLabels = targetIsControllerAsset
                ? new[] { "添加到 Parameters", "参数检查", "参数调整" }
                : new[] { "添加参数到控制器", "添加到 Parameters", "参数检查", "参数调整" };

            int selectedIndex = targetIsControllerAsset
                ? Mathf.Clamp((int)_mode - 1, 0, modeLabels.Length - 1)
                : (int)_mode;

            int newIndex = GUILayout.Toolbar(selectedIndex, modeLabels);

            if (targetIsControllerAsset)
            {
                var mappedMode = (ParameterMode)(newIndex + 1);
                if (_mode != mappedMode)
                {
                    _mode = mappedMode;
                }
            }
            else
            {
                var mappedMode = (ParameterMode)newIndex;
                if (_mode != mappedMode)
                {
                    _mode = mappedMode;
                }
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4f);

            // 工作区域
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            switch (_mode)
            {
                case ParameterMode.Scan:
                    DrawScanUI();
                    break;
                case ParameterMode.Apply:
                    DrawApplyUI();
                    break;
                case ParameterMode.Check:
                    DrawCheckUI();
                    break;
                case ParameterMode.Adjust:
                    DrawAdjustUI();
                    break;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawScanUI()
        {
            if (_context.TargetObject == null)
            {
                EditorGUILayout.HelpBox("请先选择目标对象。", MessageType.Info);
                return;
            }

            // 自动扫描：与原脚本一致（目标变更时重新扫描）
            if (_context.TargetObject != _lastTargetObject)
            {
                _lastTargetObject = _context.TargetObject;
                if (_context.TargetObject is GameObject go)
                {
                    _scanResult = ParameterScanService.Execute(go);
                    _selectAll = false;
                }
                else
                {
                    _scanResult = null;
                }
            }

            if (_scanResult == null || _scanResult.AllParameters.Count == 0)
            {
                EditorGUILayout.HelpBox("未找到任何参数。请确保目标对象包含 VRC Contact Receiver 或 VRC Phys Bone 组件。", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(4f);

            // 通用选项（对齐原脚本）
            EditorGUILayout.BeginHorizontal();
            bool newSelectAll = EditorGUILayout.ToggleLeft("全选", _selectAll, GUILayout.Width(80f));
            if (newSelectAll != _selectAll)
            {
                _selectAll = newSelectAll;

                System.Collections.Generic.HashSet<string> registered = null;
                if (_filterUnregistered && _context.SelectedController != null)
                {
                    registered = new System.Collections.Generic.HashSet<string>();
                    var ctrlParams = _context.SelectedController.parameters ?? System.Array.Empty<AnimatorControllerParameter>();
                    for (int i = 0; i < ctrlParams.Length; i++)
                    {
                        var cp = ctrlParams[i];
                        if (cp != null && !string.IsNullOrEmpty(cp.name))
                        {
                            registered.Add(cp.name);
                        }
                    }
                }

                foreach (var param in _scanResult.AllParameters)
                {
                    if (registered != null && !string.IsNullOrEmpty(param.Name) && registered.Contains(param.Name))
                    {
                        // 筛选模式下跳过已注册的参数
                        continue;
                    }

                    param.IsSelected = _selectAll;
                }
            }

            GUILayout.Space(10f);
            _overwriteExisting = EditorGUILayout.ToggleLeft("覆盖参数", _overwriteExisting, GUILayout.Width(100f));

            GUILayout.Space(10f);
            _filterUnregistered = EditorGUILayout.ToggleLeft("筛选未注册的参数", _filterUnregistered);

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);

            // 参数列表（对齐原脚本：每条可编辑类型/默认值）
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("参数列表 (来自 Controller)", EditorStyles.boldLabel);

            var controller = _context.SelectedController;
            System.Collections.Generic.HashSet<string> registeredSet = null;
            if (_filterUnregistered && controller != null)
            {
                registeredSet = new System.Collections.Generic.HashSet<string>();
                var ctrlParams = controller.parameters ?? System.Array.Empty<AnimatorControllerParameter>();
                for (int i = 0; i < ctrlParams.Length; i++)
                {
                    var cp = ctrlParams[i];
                    if (cp != null && !string.IsNullOrEmpty(cp.name))
                    {
                        registeredSet.Add(cp.name);
                    }
                }
            }

            _scanScrollPosition = EditorGUILayout.BeginScrollView(_scanScrollPosition, GUILayout.Height(320f));

            // ContactReceiver 参数
            for (int i = 0; i < _scanResult.ContactReceiverParameters.Count; i++)
            {
                var param = _scanResult.ContactReceiverParameters[i];
                if (registeredSet != null && !string.IsNullOrEmpty(param.Name) && registeredSet.Contains(param.Name))
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawScanParameterRow(param, indent: false);
                EditorGUILayout.EndVertical();
            }

            // PhysBone 参数组
            for (int i = 0; i < _scanResult.PhysBoneGroups.Count; i++)
            {
                var group = _scanResult.PhysBoneGroups[i];
                bool anyVisible = true;
                if (registeredSet != null)
                {
                    anyVisible = false;
                    for (int j = 0; j < group.Parameters.Count; j++)
                    {
                        var p = group.Parameters[j];
                        if (string.IsNullOrEmpty(p.Name) || !registeredSet.Contains(p.Name))
                        {
                            anyVisible = true;
                            break;
                        }
                    }
                }

                if (!anyVisible)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("VRC Phys Bone: " + group.BaseName, EditorStyles.boldLabel);
                GUILayout.Space(3f);
                for (int j = 0; j < group.Parameters.Count; j++)
                {
                    var param = group.Parameters[j];
                    if (registeredSet != null && !string.IsNullOrEmpty(param.Name) && registeredSet.Contains(param.Name))
                    {
                        continue;
                    }

                    DrawScanParameterRow(param, indent: true);
                }
                EditorGUILayout.EndVertical();
                GUILayout.Space(3f);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            // 应用按钮
            EditorGUILayout.Space(4f);
            var selectedCount = _scanResult.AllParameters.Count(p => p.IsSelected);
            bool hasController = _context.SelectedController != null;

            bool targetIsControllerAsset = _context.TargetObject is AnimatorController;
            EditorGUI.BeginDisabledGroup(targetIsControllerAsset || !hasController || selectedCount == 0);
            if (GUILayout.Button("添加参数到控制器", GUILayout.Height(32f)))
            {
                var result = ParameterApplyService.ApplyToController(_context.SelectedController, _scanResult.AllParameters, _overwriteExisting);

                string message = $"添加完成！\n新增: {result.AddedCount}\n覆盖: {result.OverwrittenCount}\n跳过: {result.SkippedCount}";
                if (result.Errors.Count > 0)
                {
                    message += $"\n错误: {result.Errors.Count}";
                }

                EditorUtility.DisplayDialog("完成", message, "确定");
            }
            EditorGUI.EndDisabledGroup();

            if (targetIsControllerAsset)
            {
                EditorGUILayout.HelpBox("当前目标是动画控制器资产，请改用“添加到 Parameters”功能。", MessageType.Info);
            }
            else if (!hasController)
            {
                EditorGUILayout.HelpBox("请先选择动画控制器。", MessageType.Warning);
            }
        }

        private void DrawScanParameterRow(ParameterScanService.ParameterInfo param, bool indent)
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

        private void DrawApplyUI()
        {
            if (_context.SelectedController == null)
            {
                EditorGUILayout.HelpBox("请先选择动画控制器。", MessageType.Warning);
                return;
            }

            // ExpressionParameters 选择
            EditorGUILayout.LabelField("目标 ExpressionParameters", EditorStyles.boldLabel);
            _expressionParameters = (VRCExpressionParameters)EditorGUILayout.ObjectField(
                "ExpressionParameters", 
                _expressionParameters, 
                typeof(VRCExpressionParameters), 
                false);

            if (_expressionParameters == null)
            {
                EditorGUILayout.HelpBox("请选择一个 VRCExpressionParameters 资源。", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(4f);

            // 获取控制器参数
            var controllerParams = _context.SelectedController.parameters;
            if (controllerParams == null || controllerParams.Length == 0)
            {
                EditorGUILayout.HelpBox("当前控制器中没有任何参数。", MessageType.Info);
                return;
            }

            // 通用选项（对齐原脚本）
            EditorGUILayout.BeginHorizontal();
            bool newSelectAll = EditorGUILayout.ToggleLeft("全选", _selectAll, GUILayout.Width(80f));
            if (newSelectAll != _selectAll)
            {
                _selectAll = newSelectAll;

                var existingNames = new System.Collections.Generic.HashSet<string>();
                if (_filterUnregistered && _expressionParameters != null && _expressionParameters.parameters != null)
                {
                    for (int i = 0; i < _expressionParameters.parameters.Length; i++)
                    {
                        var p = _expressionParameters.parameters[i];
                        if (!string.IsNullOrEmpty(p.name))
                        {
                            existingNames.Add(p.name);
                        }
                    }
                }

                foreach (var param in controllerParams)
                {
                    if (param == null || string.IsNullOrEmpty(param.name))
                    {
                        continue;
                    }

                    if (_filterUnregistered && existingNames.Contains(param.name))
                    {
                        // 筛选模式下跳过已存在于 Parameters 的条目
                        continue;
                    }

                    _parameterSelectFlags[param.name] = _selectAll;
                }
            }

            GUILayout.Space(10f);
            _filterUnregistered = EditorGUILayout.ToggleLeft("筛选未注册的参数", _filterUnregistered);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("参数列表", EditorStyles.boldLabel);

            var existingInParameters = new System.Collections.Generic.HashSet<string>();
            if (_expressionParameters != null && _expressionParameters.parameters != null)
            {
                for (int i = 0; i < _expressionParameters.parameters.Length; i++)
                {
                    var p = _expressionParameters.parameters[i];
                    if (!string.IsNullOrEmpty(p.name))
                    {
                        existingInParameters.Add(p.name);
                    }
                }
            }

            _scanScrollPosition = EditorGUILayout.BeginScrollView(_scanScrollPosition, GUILayout.Height(320f));

            foreach (var param in controllerParams)
            {
                if (param == null || string.IsNullOrEmpty(param.name))
                {
                    continue;
                }

                if (_filterUnregistered && existingInParameters.Contains(param.name))
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawApplyParameterRow(param);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4f);

            // 应用按钮
            int selectedCount = 0;
            foreach (var kv in _parameterSelectFlags)
            {
                if (kv.Value) selectedCount++;
            }
            EditorGUI.BeginDisabledGroup(selectedCount == 0);
            if (GUILayout.Button("添加到 Parameters", GUILayout.Height(32f)))
            {
                ApplyToExpressionParameters();
            }
            EditorGUI.EndDisabledGroup();

            if (selectedCount == 0)
            {
                EditorGUILayout.HelpBox("请至少选择一个参数。", MessageType.Warning);
            }
        }

        private void ApplyToExpressionParameters()
        {
            try
            {
                ParameterApplyService.ApplyToExpressionParameters(
                    _context.SelectedController,
                    _expressionParameters,
                    _parameterSelectFlags,
                    _parameterSaveFlags,
                    _parameterSyncFlags,
                    _parameterTypeOverrides,
                    _parameterDefaultOverrides,
                    _filterUnregistered
                );

                var selectedCount = _parameterSelectFlags.Values.Count(v => v);
                EditorUtility.DisplayDialog("完成", $"已写入 {selectedCount} 个参数到 ExpressionParameters。", "确定");
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("错误", $"写入失败: {ex.Message}", "确定");
            }
        }

        private void DrawApplyParameterRow(AnimatorControllerParameter param)
        {
            EditorGUILayout.BeginHorizontal();

            // 选择复选框
            bool isSelected = _parameterSelectFlags.TryGetValue(param.name, out var selected) && selected;
            bool newSelected = EditorGUILayout.Toggle(isSelected, GUILayout.Width(20f));
            if (newSelected != isSelected)
            {
                _parameterSelectFlags[param.name] = newSelected;
            }

            EditorGUILayout.LabelField("参数名:", GUILayout.Width(60f));
            EditorGUILayout.LabelField(param.name, EditorStyles.boldLabel, GUILayout.MinWidth(120f));

            // 类型覆盖
            EditorGUILayout.LabelField("类型:", GUILayout.Width(40f));
            string[] typeOptions = { "Bool", "Float", "Int" };
            var overrideType = _parameterTypeOverrides.TryGetValue(param.name, out var t) ? t : param.type;
            int currentTypeIndex = overrideType == AnimatorControllerParameterType.Bool ? 0 :
                                   overrideType == AnimatorControllerParameterType.Float ? 1 : 2;
            int newTypeIndex = EditorGUILayout.Popup(currentTypeIndex, typeOptions, GUILayout.Width(80f));
            var newType = newTypeIndex == 0 ? AnimatorControllerParameterType.Bool :
                         newTypeIndex == 1 ? AnimatorControllerParameterType.Float :
                         AnimatorControllerParameterType.Int;
            _parameterTypeOverrides[param.name] = newType;

            // 默认值覆盖（ExpressionParameters 的默认值是 float，因此这里统一用 float 存储）
            EditorGUILayout.LabelField("默认值:", GUILayout.Width(60f));
            float defaultValue;
            if (!_parameterDefaultOverrides.TryGetValue(param.name, out defaultValue))
            {
                defaultValue = param.type == AnimatorControllerParameterType.Bool
                    ? (param.defaultBool ? 1f : 0f)
                    : param.type == AnimatorControllerParameterType.Float
                        ? param.defaultFloat
                        : param.defaultInt;
            }

            switch (newType)
            {
                case AnimatorControllerParameterType.Bool:
                {
                    bool b = defaultValue >= 0.5f;
                    b = EditorGUILayout.Toggle(b, GUILayout.Width(80f));
                    _parameterDefaultOverrides[param.name] = b ? 1f : 0f;
                    break;
                }
                case AnimatorControllerParameterType.Float:
                {
                    float v = EditorGUILayout.FloatField(defaultValue, GUILayout.Width(80f));
                    _parameterDefaultOverrides[param.name] = v;
                    break;
                }
                case AnimatorControllerParameterType.Int:
                {
                    int v = EditorGUILayout.IntField(Mathf.RoundToInt(defaultValue), GUILayout.Width(80f));
                    _parameterDefaultOverrides[param.name] = v;
                    break;
                }
            }

            // Saved 标记
            bool isSaved = _parameterSaveFlags.TryGetValue(param.name, out var saved) && saved;
            bool newSaved = EditorGUILayout.ToggleLeft("保存", isSaved, GUILayout.Width(60f));
            if (newSaved != isSaved)
            {
                _parameterSaveFlags[param.name] = newSaved;
            }

            // Synced 标记
            bool isSynced = _parameterSyncFlags.TryGetValue(param.name, out var synced) && synced;
            bool newSynced = EditorGUILayout.ToggleLeft("同步", isSynced, GUILayout.Width(60f));
            if (newSynced != isSynced)
            {
                _parameterSyncFlags[param.name] = newSynced;
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawCheckUI()
        {
            var controller = _context.SelectedController;
            if (controller == null)
            {
                EditorGUILayout.HelpBox("请先选择动画控制器。", MessageType.Warning);
                return;
            }

            if (GUILayout.Button("开始检查", GUILayout.Height(30f)))
            {
                _checkResult = ParameterCheckService.Execute(controller);
                BuildIssueUIs();
            }

            if (_checkResult == null)
            {
                EditorGUILayout.HelpBox("点击“开始检查”以分析当前控制器的参数使用情况。", MessageType.Info);
                return;
            }

            if (!_checkResult.HasIssues)
            {
                EditorGUILayout.HelpBox("未发现参数问题。", MessageType.Info);
                return;
            }

            var controllerParams = controller.parameters ?? System.Array.Empty<AnimatorControllerParameter>();
            var controllerParamNames = controllerParams
                .Where(p => p != null && !string.IsNullOrEmpty(p.name))
                .Select(p => p.name)
                .ToArray();

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField($"发现 {_issueUIs.Count} 个问题", EditorStyles.boldLabel);

            var missingIssues = _issueUIs.Where(u => u.Issue.Type == ParameterCheckService.IssueType.MissingReference).ToList();
            var unusedIssues = _issueUIs.Where(u => u.Issue.Type == ParameterCheckService.IssueType.UnusedParameter).ToList();
            var mismatchIssues = _issueUIs.Where(u => u.Issue.Type == ParameterCheckService.IssueType.TypeMismatch).ToList();

            _checkScrollPosition = EditorGUILayout.BeginScrollView(_checkScrollPosition);

            if (missingIssues.Count > 0)
            {
                EditorGUILayout.LabelField("缺失参数引用", EditorStyles.boldLabel);
                foreach (var ui in missingIssues)
                {
                    DrawMissingReferenceRow(ui, controllerParamNames);
                }
                EditorGUILayout.Space(8f);
            }

            if (unusedIssues.Count > 0)
            {
                EditorGUILayout.LabelField("无用参数", EditorStyles.boldLabel);
                foreach (var ui in unusedIssues)
                {
                    DrawUnusedParameterRow(ui);
                }
                EditorGUILayout.Space(8f);
            }

            if (mismatchIssues.Count > 0)
            {
                EditorGUILayout.LabelField("参数类型不匹配", EditorStyles.boldLabel);
                foreach (var ui in mismatchIssues)
                {
                    DrawTypeMismatchRow(ui);
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4f);
            if (GUILayout.Button("应用参数检查修复", GUILayout.Height(30f)))
            {
                ApplyCheckFixes(controllerParamNames);
            }
        }

        private void BuildIssueUIs()
        {
            _issueUIs.Clear();
            if (_checkResult == null || !_checkResult.HasIssues)
            {
                return;
            }

            foreach (var issue in _checkResult.Issues)
            {
                var ui = new ParameterIssueUI
                {
                    Issue = issue,
                    SelectedOptionIndex = 0,
                    SelectedExistingParamIndex = 0,
                    RemoveUnused = false,
                    ApplyTypeFix = false,
                    TypeFixTarget = issue.ExpectedType ?? issue.ActualType ?? AnimatorControllerParameterType.Float
                };
                _issueUIs.Add(ui);
            }
        }

        private void DrawMissingReferenceRow(ParameterIssueUI ui, string[] controllerParamNames)
        {
            var issue = ui.Issue;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"参数名: {issue.ParameterName}", EditorStyles.boldLabel);
            if (issue.ExpectedType.HasValue)
            {
                EditorGUILayout.LabelField($"期望类型: {issue.ExpectedType.Value}", EditorStyles.miniLabel);
            }

            int maxRefs = Mathf.Min(issue.References.Count, 3);
            for (int i = 0; i < maxRefs; i++)
            {
                EditorGUILayout.LabelField($"• {issue.References[i].Description}", EditorStyles.miniLabel);
            }
            if (issue.References.Count > 3)
            {
                EditorGUILayout.LabelField($"... 以及其他 {issue.References.Count - 3} 处", EditorStyles.miniLabel);
            }

            ui.SelectedOptionIndex = EditorGUILayout.Popup("修复方式", ui.SelectedOptionIndex, _missingReferenceOptions);

            if (ui.SelectedOptionIndex == 2)
            {
                if (controllerParamNames.Length == 0)
                {
                    EditorGUILayout.HelpBox("当前控制器中没有可供替换的已定义参数。", MessageType.Warning);
                }
                else
                {
                    ui.SelectedExistingParamIndex = Mathf.Clamp(ui.SelectedExistingParamIndex, 0, controllerParamNames.Length - 1);
                    ui.SelectedExistingParamIndex = EditorGUILayout.Popup("替换为", ui.SelectedExistingParamIndex, controllerParamNames);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawUnusedParameterRow(ParameterIssueUI ui)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"{ui.Issue.ParameterName} ({ui.Issue.ActualType})", GUILayout.ExpandWidth(true));
            ui.RemoveUnused = EditorGUILayout.ToggleLeft("移除", ui.RemoveUnused, GUILayout.Width(60f));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTypeMismatchRow(ParameterIssueUI ui)
        {
            var issue = ui.Issue;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"参数名: {issue.ParameterName}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"当前类型: {issue.ActualType}", EditorStyles.miniLabel);
            if (issue.ExpectedType.HasValue)
            {
                EditorGUILayout.LabelField($"引用期望: {issue.ExpectedType.Value}", EditorStyles.miniLabel);
            }

            int maxRefs = Mathf.Min(issue.References.Count, 3);
            for (int i = 0; i < maxRefs; i++)
            {
                EditorGUILayout.LabelField($"• {issue.References[i].Description}", EditorStyles.miniLabel);
            }
            if (issue.References.Count > 3)
            {
                EditorGUILayout.LabelField($"... 以及其他 {issue.References.Count - 3} 处", EditorStyles.miniLabel);
            }

            ui.ApplyTypeFix = EditorGUILayout.ToggleLeft("同步该参数类型并自动修复条件", ui.ApplyTypeFix);
            using (new EditorGUI.DisabledGroupScope(!ui.ApplyTypeFix))
            {
                int typeIndex = System.Array.IndexOf(_typeOptionValues, ui.TypeFixTarget);
                if (typeIndex < 0) typeIndex = 0;
                typeIndex = EditorGUILayout.Popup("目标类型", typeIndex, _typeOptionLabels);
                ui.TypeFixTarget = _typeOptionValues[Mathf.Clamp(typeIndex, 0, _typeOptionValues.Length - 1)];
            }

            EditorGUILayout.EndVertical();
        }

        private void ApplyCheckFixes(string[] controllerParamNames)
        {
            if (_context.SelectedController == null || _issueUIs.Count == 0)
            {
                return;
            }

            int fixCount = 0;

            // 缺失引用
            foreach (var ui in _issueUIs.Where(u => u.Issue.Type == ParameterCheckService.IssueType.MissingReference))
            {
                if (ui.SelectedOptionIndex == 0)
                    continue;

                string fixOption = ui.SelectedOptionIndex == 1 ? "Remove" : "UseExisting";
                string target = null;
                if (ui.SelectedOptionIndex == 2 && controllerParamNames.Length > 0)
                {
                    if (ui.SelectedExistingParamIndex >= 0 && ui.SelectedExistingParamIndex < controllerParamNames.Length)
                    {
                        target = controllerParamNames[ui.SelectedExistingParamIndex];
                    }
                }

                if (fixOption == "UseExisting" && string.IsNullOrEmpty(target))
                {
                    continue;
                }

                if (ParameterCheckService.FixMissingReference(_context.SelectedController, ui.Issue, fixOption, target))
                {
                    fixCount++;
                }
            }

            // 无用参数
            foreach (var ui in _issueUIs.Where(u => u.Issue.Type == ParameterCheckService.IssueType.UnusedParameter))
            {
                if (!ui.RemoveUnused)
                    continue;

                if (ParameterCheckService.RemoveUnusedParameter(_context.SelectedController, ui.Issue.ParameterName))
                {
                    fixCount++;
                }
            }

            // 类型不匹配
            foreach (var ui in _issueUIs.Where(u => u.Issue.Type == ParameterCheckService.IssueType.TypeMismatch))
            {
                if (!ui.ApplyTypeFix)
                    continue;

                if (ParameterCheckService.FixTypeMismatch(_context.SelectedController, ui.Issue, ui.TypeFixTarget))
                {
                    fixCount++;
                }
            }

            if (fixCount > 0)
            {
                EditorUtility.DisplayDialog("完成", $"已应用 {fixCount} 项修复。", "确定");
                _checkResult = ParameterCheckService.Execute(_context.SelectedController);
                BuildIssueUIs();
            }
            else
            {
                EditorUtility.DisplayDialog("提示", "未选择任何需要应用的修复。", "确定");
            }
        }
    }
}
