using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace MVA.Toolbox.QuickAnimatorEdit.Services.Parameter
{
    /// <summary>
    /// 参数写入服务
    /// 将参数写入到 AnimatorController 或 VRCExpressionParameters
    /// </summary>
    public static class ParameterApplyService
    {
        /// <summary>
        /// 写入结果
        /// </summary>
        public struct ApplyResult
        {
            public int AddedCount;
            public int SkippedCount;
            public int OverwrittenCount;
            public List<string> Errors;
        }

        /// <summary>
        /// 将参数写入到 AnimatorController
        /// </summary>
        /// <param name="controller">目标控制器</param>
        /// <param name="parameters">要写入的参数列表</param>
        /// <param name="overwriteExisting">是否覆盖已存在的参数</param>
        /// <returns>写入结果</returns>
        public static ApplyResult ApplyToController(
            AnimatorController controller,
            IReadOnlyList<ParameterScanService.ParameterInfo> parameters,
            bool overwriteExisting)
        {
            var result = new ApplyResult { Errors = new List<string>() };
            
            if (controller == null)
            {
                result.Errors.Add("控制器为空");
                return result;
            }

            var selectedParams = parameters.Where(p => p.IsSelected).ToList();
            if (selectedParams.Count == 0)
            {
                result.Errors.Add("未选择任何参数");
                return result;
            }

            foreach (var param in selectedParams)
            {
                try
                {
                    var existingParam = controller.parameters.FirstOrDefault(p => p.name == param.Name);
                    if (existingParam != null)
                    {
                        if (!overwriteExisting)
                        {
                            result.SkippedCount++;
                            continue;
                        }

                        if (existingParam.type == param.Type)
                        {
                            UpdateParameterDefaultValue(controller, param);
                            result.OverwrittenCount++;
                        }
                        else
                        {
                            RemoveParameter(controller, param.Name);
                            AddParameter(controller, param);
                            result.OverwrittenCount++;
                        }
                    }
                    else
                    {
                        AddParameter(controller, param);
                        result.AddedCount++;
                    }
                }
                catch (System.Exception ex)
                {
                    result.Errors.Add($"{param.Name}: {ex.Message}");
                }
            }

            if (result.AddedCount > 0 || result.OverwrittenCount > 0)
            {
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
            }

            return result;
        }

        /// <summary>
        /// 添加新参数到控制器
        /// </summary>
        private static void AddParameter(AnimatorController controller, ParameterScanService.ParameterInfo param)
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

        /// <summary>
        /// 更新参数默认值
        /// </summary>
        private static void UpdateParameterDefaultValue(AnimatorController controller, ParameterScanService.ParameterInfo param)
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

        /// <summary>
        /// 从控制器移除参数
        /// </summary>
        private static void RemoveParameter(AnimatorController controller, string paramName)
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

        /// <summary>
        /// 将参数写入到 VRCExpressionParameters
        /// </summary>
        /// <param name="controller">源控制器（读取参数）</param>
        /// <param name="expressionParameters">目标 ExpressionParameters</param>
        /// <param name="selectFlags">选择标记</param>
        /// <param name="saveFlags">保存标记</param>
        /// <param name="syncFlags">同步标记</param>
        /// <param name="typeOverrides">类型覆盖</param>
        /// <param name="defaultOverrides">默认值覆盖</param>
        /// <param name="filterUnregistered">是否筛选未注册的参数</param>
        public static void ApplyToExpressionParameters(
            AnimatorController controller,
            VRCExpressionParameters expressionParameters,
            Dictionary<string, bool> selectFlags,
            Dictionary<string, bool> saveFlags,
            Dictionary<string, bool> syncFlags,
            Dictionary<string, AnimatorControllerParameterType> typeOverrides,
            Dictionary<string, float> defaultOverrides,
            bool filterUnregistered)
        {
            if (controller == null || expressionParameters == null)
                return;

            var controllerParams = controller.parameters ?? new AnimatorControllerParameter[0];
            if (controllerParams.Length == 0)
                return;

            var paramList = new List<VRCExpressionParameters.Parameter>();
            if (expressionParameters.parameters != null)
            {
                paramList.AddRange(expressionParameters.parameters);
            }

            var existingNames = new HashSet<string>();
            foreach (var p in paramList)
            {
                if (!string.IsNullOrEmpty(p.name))
                {
                    existingNames.Add(p.name);
                }
            }

            bool dirty = false;

            for (int i = 0; i < controllerParams.Length; i++)
            {
                var cp = controllerParams[i];
                if (cp == null || string.IsNullOrEmpty(cp.name))
                    continue;

                // Check selection
                bool isSelected = selectFlags != null && selectFlags.TryGetValue(cp.name, out var sel) && sel;
                if (!isSelected)
                    continue;

                // Filter existing if requested
                if (filterUnregistered && existingNames.Contains(cp.name))
                    continue;

                bool saveFlag = saveFlags != null && saveFlags.TryGetValue(cp.name, out var s) && s;
                bool syncFlag = syncFlags != null && syncFlags.TryGetValue(cp.name, out var y) && y;

                int index = paramList.FindIndex(p => p.name == cp.name);

                if (index < 0)
                {
                    // Create new
                    var valueType = VRCExpressionParameters.ValueType.Int;
                    
                    // Determine type (check override or default)
                    AnimatorControllerParameterType targetType = cp.type;
                    if (typeOverrides != null && typeOverrides.TryGetValue(cp.name, out var overrideType))
                    {
                        targetType = overrideType;
                    }

                    switch (targetType)
                    {
                        case AnimatorControllerParameterType.Bool:
                            valueType = VRCExpressionParameters.ValueType.Bool;
                            break;
                        case AnimatorControllerParameterType.Float:
                            valueType = VRCExpressionParameters.ValueType.Float;
                            break;
                        case AnimatorControllerParameterType.Int:
                            valueType = VRCExpressionParameters.ValueType.Int;
                            break;
                    }

                    float defaultVal = 0f;
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
                    dirty = true;
                }
                else
                {
                    // Update existing
                    var p = paramList[index];
                    if (p.saved != saveFlag || p.networkSynced != syncFlag)
                    {
                        p.saved = saveFlag;
                        p.networkSynced = syncFlag;
                        paramList[index] = p;
                        dirty = true;
                    }
                }
            }

            if (dirty)
            {
                Undo.RecordObject(expressionParameters, "Apply Parameters to ExpressionParameters");
                expressionParameters.parameters = paramList.ToArray();
                EditorUtility.SetDirty(expressionParameters);
                AssetDatabase.SaveAssets();
            }
        }
    }
}
