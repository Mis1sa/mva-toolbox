using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using MVA.Toolbox.Public;

namespace MVA.Toolbox.QuickAddParameter.Services
{
    /// <summary>
    /// Quick Add Parameter 服务：负责从 Avatar/目标物体扫描参数，并根据模式写入 AnimatorController 或 VRCExpressionParameters。
    /// 不包含任何 IMGUI 代码，由 QuickAddParameterWindow 驱动。
    /// </summary>
    internal sealed class QuickAddParameterService
    {
        internal enum ToolMode
        {
            ToController,
            ToParameters
        }

        internal sealed class ParameterInfo
        {
            public string Name;
            public AnimatorControllerParameterType Type;
            public bool IsSelected;
            public bool DefaultBool;
            public float DefaultFloat;
            public int DefaultInt;
            public string SourceComponent;
            public bool IsFromPhysBone;
            public string PhysBoneSuffix;
            public string PhysBoneBaseName;
        }

        internal sealed class PhysBoneParameterGroup
        {
            public string BaseName;
            public List<ParameterInfo> Parameters;
        }

        GameObject _targetRoot;
        ToolMode _mode = ToolMode.ToController;

        readonly List<AnimatorController> _controllers = new List<AnimatorController>();
        readonly List<string> _controllerNames = new List<string>();
        int _selectedControllerIndex; // 对应 controllers 列表索引，-1 表示未选

        VRCExpressionParameters _expressionParameters;

        readonly List<ParameterInfo> _allParameters = new List<ParameterInfo>();
        readonly List<ParameterInfo> _contactReceiverParams = new List<ParameterInfo>();
        readonly List<PhysBoneParameterGroup> _physBoneGroups = new List<PhysBoneParameterGroup>();

        bool _overwriteExisting;
        bool _filterUnregistered;

        public ToolMode Mode
        {
            get => _mode;
            set => _mode = value;
        }

        public GameObject TargetRoot => _targetRoot;
        public IReadOnlyList<AnimatorController> Controllers => _controllers;
        public IReadOnlyList<string> ControllerNames => _controllerNames;

        public int SelectedControllerIndex
        {
            get => _selectedControllerIndex;
            set => _selectedControllerIndex = value;
        }

        public AnimatorController SelectedController
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

        public VRCExpressionParameters ExpressionParameters
        {
            get => _expressionParameters;
            set => _expressionParameters = value;
        }

        public bool OverwriteExisting
        {
            get => _overwriteExisting;
            set => _overwriteExisting = value;
        }

        public bool FilterUnregistered
        {
            get => _filterUnregistered;
            set => _filterUnregistered = value;
        }

        public IReadOnlyList<ParameterInfo> AllParameters => _allParameters;
        public IReadOnlyList<ParameterInfo> ContactReceiverParameters => _contactReceiverParams;
        public IReadOnlyList<PhysBoneParameterGroup> PhysBoneGroups => _physBoneGroups;

        public void SetTarget(GameObject root)
        {
            _targetRoot = root;
            RefreshControllers();
            ScanParameters();
        }

        public void SetControllerAsset(AnimatorController controller)
        {
            _targetRoot = null;

            _controllers.Clear();
            _controllerNames.Clear();
            _selectedControllerIndex = -1;

            _allParameters.Clear();
            _contactReceiverParams.Clear();
            _physBoneGroups.Clear();

            if (controller == null)
            {
                return;
            }

            _controllers.Add(controller);
            _controllerNames.Add(controller.name);
            _selectedControllerIndex = 0;
            _mode = ToolMode.ToParameters;
        }

        void RefreshControllers()
        {
            _controllers.Clear();
            _controllerNames.Clear();
            _selectedControllerIndex = -1;

            if (_targetRoot == null)
            {
                return;
            }

            var descriptor = ToolboxUtils.GetAvatarDescriptor(_targetRoot);
            var animator = _targetRoot.GetComponent<Animator>();

            _controllers.AddRange(ToolboxUtils.CollectControllersFromRoot(_targetRoot, includeSpecialLayers: true));
            if (_controllers.Count > 0)
            {
                _controllerNames.AddRange(ToolboxUtils.BuildControllerDisplayNames(descriptor, animator, _controllers));

                // 如果存在 FX 层控制器，优先选择 FX
                if (descriptor != null)
                {
                    var fxController = ToolboxUtils.GetExistingFXController(descriptor);
                    if (fxController != null)
                    {
                        var index = _controllers.IndexOf(fxController);
                        if (index >= 0)
                        {
                            _selectedControllerIndex = index;
                            return;
                        }
                    }
                }

                // 否则默认选择第一个
                _selectedControllerIndex = 0;
            }
        }

        public void ScanParameters()
        {
            _allParameters.Clear();
            _contactReceiverParams.Clear();
            _physBoneGroups.Clear();

            if (_targetRoot == null)
            {
                return;
            }

            var allComponents = _targetRoot.GetComponentsInChildren<Component>(true);
            var paramDict = new Dictionary<string, ParameterInfo>();
            var physBoneParamDict = new Dictionary<string, List<ParameterInfo>>();

            for (int i = 0; i < allComponents.Length; i++)
            {
                var component = allComponents[i];
                if (component == null) continue;

                string componentTypeName = component.GetType().FullName;
                if (componentTypeName.Contains("VRCContactReceiver") || componentTypeName.Contains("ContactReceiver"))
                {
                    ScanContactReceiver(component, paramDict);
                }
                else if (componentTypeName.Contains("VRCPhysBone") || componentTypeName.Contains("PhysBone"))
                {
                    ScanPhysBone(component, paramDict);
                }
            }

            foreach (var param in paramDict.Values)
            {
                _allParameters.Add(param);
                if (param.IsFromPhysBone && !string.IsNullOrEmpty(param.PhysBoneBaseName))
                {
                    if (!physBoneParamDict.TryGetValue(param.PhysBoneBaseName, out var list))
                    {
                        list = new List<ParameterInfo>();
                        physBoneParamDict[param.PhysBoneBaseName] = list;
                    }

                    list.Add(param);
                }
                else
                {
                    _contactReceiverParams.Add(param);
                }
            }

            foreach (var kv in physBoneParamDict.OrderBy(x => x.Key))
            {
                _physBoneGroups.Add(new PhysBoneParameterGroup
                {
                    BaseName = kv.Key,
                    Parameters = kv.Value.OrderBy(p => p.Name).ToList()
                });
            }

            _contactReceiverParams.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        }

        void ScanContactReceiver(Component component, Dictionary<string, ParameterInfo> paramDict)
        {
            var so = new SerializedObject(component);
            var parameterProp = so.FindProperty("parameter");
            var receiverTypeProp = so.FindProperty("receiverType");
            if (parameterProp == null || receiverTypeProp == null) return;

            string paramName = parameterProp.stringValue;
            if (string.IsNullOrEmpty(paramName)) return;

            int receiverType = receiverTypeProp.intValue;
            var paramType = receiverType == 2
                ? AnimatorControllerParameterType.Float
                : AnimatorControllerParameterType.Bool;

            if (paramDict.TryGetValue(paramName, out var existing))
            {
                if (existing.Type == AnimatorControllerParameterType.Float && paramType == AnimatorControllerParameterType.Bool)
                {
                    existing.Type = AnimatorControllerParameterType.Bool;
                    existing.SourceComponent += $", {component.GetType().Name}";
                }

                return;
            }

            paramDict[paramName] = new ParameterInfo
            {
                Name = paramName,
                Type = paramType,
                IsSelected = false,
                DefaultBool = false,
                DefaultFloat = 0f,
                DefaultInt = 0,
                SourceComponent = component.GetType().Name,
                IsFromPhysBone = false,
                PhysBoneSuffix = string.Empty,
                PhysBoneBaseName = string.Empty
            };
        }

        void ScanPhysBone(Component component, Dictionary<string, ParameterInfo> paramDict)
        {
            var so = new SerializedObject(component);
            var parameterProp = so.FindProperty("parameter");
            if (parameterProp == null) return;

            string baseParamName = parameterProp.stringValue;
            if (string.IsNullOrEmpty(baseParamName)) return;

            string[] suffixes = { "_IsGrabbed", "_IsPosed", "_Angle", "_Stretch", "_Squish" };
            for (int i = 0; i < suffixes.Length; i++)
            {
                string suffix = suffixes[i];
                string paramName = baseParamName + suffix;
                if (paramDict.ContainsKey(paramName))
                {
                    continue;
                }

                paramDict[paramName] = new ParameterInfo
                {
                    Name = paramName,
                    Type = AnimatorControllerParameterType.Float,
                    IsSelected = false,
                    DefaultBool = false,
                    DefaultFloat = 0f,
                    DefaultInt = 0,
                    SourceComponent = component.GetType().Name,
                    IsFromPhysBone = true,
                    PhysBoneSuffix = suffix,
                    PhysBoneBaseName = baseParamName
                };
            }
        }

        public void ApplyToController()
        {
            var controller = SelectedController;
            if (controller == null)
            {
                EditorUtility.DisplayDialog("错误", "请选择一个有效的 AnimatorController。", "确定");
                return;
            }

            var selectedParams = _allParameters.Where(p => p.IsSelected).ToList();
            if (selectedParams.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "请至少选择一个参数。", "确定");
                return;
            }

            int addedCount = 0;
            int skippedCount = 0;
            int overwrittenCount = 0;
            var errors = new List<string>();

            foreach (var param in selectedParams)
            {
                try
                {
                    var existingParam = controller.parameters.FirstOrDefault(p => p.name == param.Name);
                    if (existingParam != null)
                    {
                        if (!_overwriteExisting)
                        {
                            skippedCount++;
                            continue;
                        }

                        if (existingParam.type == param.Type)
                        {
                            UpdateParameterDefaultValue(controller, param);
                            overwrittenCount++;
                        }
                        else
                        {
                            RemoveParameter(controller, param.Name);
                            AddParameter(controller, param);
                            overwrittenCount++;
                        }
                    }
                    else
                    {
                        AddParameter(controller, param);
                        addedCount++;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{param.Name}: {ex.Message}");
                }
            }

            if (addedCount > 0 || overwrittenCount > 0)
            {
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
            }

            string message = "添加完成！\n";
            message += $"新增: {addedCount}\n";
            if (overwrittenCount > 0)
            {
                message += $"覆盖: {overwrittenCount}\n";
            }
            if (skippedCount > 0)
            {
                message += $"跳过: {skippedCount}\n";
            }
            if (errors.Count > 0)
            {
                message += $"\n错误: {errors.Count}\n";
                for (int i = 0; i < errors.Count; i++)
                {
                    message += "- " + errors[i] + "\n";
                }
            }

            EditorUtility.DisplayDialog("完成", message, "确定");
        }

        void AddParameter(AnimatorController controller, ParameterInfo param)
        {
            var newParam = new AnimatorControllerParameter
            {
                name = param.Name,
                type = param.Type
            };

            switch (param.Type)
            {
                case AnimatorControllerParameterType.Bool:
                    newParam.defaultBool = param.DefaultBool;
                    break;
                case AnimatorControllerParameterType.Float:
                    newParam.defaultFloat = param.DefaultFloat;
                    break;
                case AnimatorControllerParameterType.Int:
                    newParam.defaultInt = param.DefaultInt;
                    break;
            }

            controller.AddParameter(newParam);
        }

        void UpdateParameterDefaultValue(AnimatorController controller, ParameterInfo param)
        {
            var so = new SerializedObject(controller);
            var controllerProp = so.FindProperty("m_Controller");
            if (controllerProp == null) return;

            var parametersProp = controllerProp.FindPropertyRelative("m_AnimatorParameters");
            if (parametersProp == null) return;

            for (int i = 0; i < parametersProp.arraySize; i++)
            {
                var paramProp = parametersProp.GetArrayElementAtIndex(i);
                var nameProp = paramProp.FindPropertyRelative("m_Name");
                if (nameProp != null && nameProp.stringValue == param.Name)
                {
                    switch (param.Type)
                    {
                        case AnimatorControllerParameterType.Bool:
                            var defaultBoolProp = paramProp.FindPropertyRelative("m_DefaultBool");
                            if (defaultBoolProp != null) defaultBoolProp.boolValue = param.DefaultBool;
                            break;
                        case AnimatorControllerParameterType.Float:
                            var defaultFloatProp = paramProp.FindPropertyRelative("m_DefaultFloat");
                            if (defaultFloatProp != null) defaultFloatProp.floatValue = param.DefaultFloat;
                            break;
                        case AnimatorControllerParameterType.Int:
                            var defaultIntProp = paramProp.FindPropertyRelative("m_DefaultInt");
                            if (defaultIntProp != null) defaultIntProp.intValue = param.DefaultInt;
                            break;
                    }

                    so.ApplyModifiedProperties();
                    break;
                }
            }
        }

        public void ApplyToParameters(System.Collections.Generic.Dictionary<string, bool> selectFlags,
                                       System.Collections.Generic.Dictionary<string, bool> saveFlags,
                                       System.Collections.Generic.Dictionary<string, bool> syncFlags,
                                       System.Collections.Generic.Dictionary<string, AnimatorControllerParameterType> typeOverrides,
                                       System.Collections.Generic.Dictionary<string, float> defaultOverrides)
        {
            var controller = SelectedController;
            if (controller == null)
            {
                EditorUtility.DisplayDialog("错误", "请选择一个有效的 AnimatorController。", "确定");
                return;
            }

            if (_expressionParameters == null)
            {
                EditorUtility.DisplayDialog("错误", "请先指定一个 VRCExpressionParameters 资源。", "确定");
                return;
            }

            var controllerParams = controller.parameters ?? System.Array.Empty<AnimatorControllerParameter>();
            if (controllerParams.Length == 0)
            {
                EditorUtility.DisplayDialog("提示", "当前动画控制器中没有任何参数。", "确定");
                return;
            }

            var paramList = new List<VRCExpressionParameters.Parameter>();
            if (_expressionParameters.parameters != null)
            {
                paramList.AddRange(_expressionParameters.parameters);
            }

            var existingNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < paramList.Count; i++)
            {
                var p = paramList[i];
                if (!string.IsNullOrEmpty(p.name))
                {
                    existingNames.Add(p.name);
                }
            }

            for (int i = 0; i < controllerParams.Length; i++)
            {
                var cp = controllerParams[i];
                if (cp == null || string.IsNullOrEmpty(cp.name))
                {
                    continue;
                }

                bool isSelected = selectFlags != null && selectFlags.TryGetValue(cp.name, out var sel) && sel;
                if (!isSelected)
                {
                    continue;
                }

                if (_filterUnregistered && existingNames.Contains(cp.name))
                {
                    // 筛选模式下跳过已存在于 Parameters 的条目
                    continue;
                }

                bool saveFlag = saveFlags != null && saveFlags.TryGetValue(cp.name, out var s) && s;
                bool syncFlag = syncFlags != null && syncFlags.TryGetValue(cp.name, out var y) && y;
                if (!saveFlag && !syncFlag)
                {
                    continue;
                }

                int index = -1;
                for (int j = 0; j < paramList.Count; j++)
                {
                    if (paramList[j] != null && paramList[j].name == cp.name)
                    {
                        index = j;
                        break;
                    }
                }

                if (index < 0)
                {
                    var valueType = cp.type == AnimatorControllerParameterType.Bool
                        ? VRCExpressionParameters.ValueType.Bool
                        : cp.type == AnimatorControllerParameterType.Float
                            ? VRCExpressionParameters.ValueType.Float
                            : VRCExpressionParameters.ValueType.Int;

                    if (typeOverrides != null && typeOverrides.TryGetValue(cp.name, out var overriddenType))
                    {
                        valueType = overriddenType == AnimatorControllerParameterType.Bool
                            ? VRCExpressionParameters.ValueType.Bool
                            : overriddenType == AnimatorControllerParameterType.Float
                                ? VRCExpressionParameters.ValueType.Float
                                : VRCExpressionParameters.ValueType.Int;
                    }

                    float defaultVal;
                    if (defaultOverrides != null && defaultOverrides.TryGetValue(cp.name, out var overrideVal))
                    {
                        defaultVal = overrideVal;
                    }
                    else
                    {
                        defaultVal = cp.type == AnimatorControllerParameterType.Bool
                            ? (cp.defaultBool ? 1f : 0f)
                            : cp.type == AnimatorControllerParameterType.Float
                                ? cp.defaultFloat
                                : cp.defaultInt;
                    }

                    var p = new VRCExpressionParameters.Parameter
                    {
                        name = cp.name,
                        valueType = valueType,
                        defaultValue = defaultVal,
                        saved = saveFlag,
                        networkSynced = syncFlag
                    };

                    paramList.Add(p);
                    existingNames.Add(p.name);
                }
                else
                {
                    var p = paramList[index];
                    p.saved = saveFlag;
                    p.networkSynced = syncFlag;
                    paramList[index] = p;
                }
            }

            _expressionParameters.parameters = paramList.ToArray();
            EditorUtility.SetDirty(_expressionParameters);
            AssetDatabase.SaveAssets();
        }

        void RemoveParameter(AnimatorController controller, string paramName)
        {
            var so = new SerializedObject(controller);
            var controllerProp = so.FindProperty("m_Controller");
            if (controllerProp == null) return;

            var parametersProp = controllerProp.FindPropertyRelative("m_AnimatorParameters");
            if (parametersProp == null) return;

            for (int i = parametersProp.arraySize - 1; i >= 0; i--)
            {
                var paramProp = parametersProp.GetArrayElementAtIndex(i);
                var nameProp = paramProp.FindPropertyRelative("m_Name");
                if (nameProp != null && nameProp.stringValue == paramName)
                {
                    parametersProp.DeleteArrayElementAtIndex(i);
                    so.ApplyModifiedProperties();
                    break;
                }
            }
        }
    }
}
