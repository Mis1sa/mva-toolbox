using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace MVA.Toolbox.AnimatorParameterTool
{
    internal static class AnimatorParameterApplyService
    {
        internal struct ApplyResult
        {
            public int AddedCount;
            public int SkippedCount;
            public int OverwrittenCount;
            public List<string> Errors;
        }

        internal static ApplyResult ApplyToController(
            AnimatorController controller,
            IReadOnlyList<AnimatorParameterScanService.ParameterInfo> parameters,
            bool overwriteExisting)
        {
            var result = new ApplyResult { Errors = new List<string>() };
            if (controller == null)
            {
                result.Errors.Add("控制器为空");
                return result;
            }

            List<AnimatorParameterScanService.ParameterInfo> selectedParams = parameters.Where(p => p.IsSelected).ToList();
            if (selectedParams.Count == 0)
            {
                result.Errors.Add("未选择任何参数");
                return result;
            }

            for (int i = 0; i < selectedParams.Count; i++)
            {
                AnimatorParameterScanService.ParameterInfo param = selectedParams[i];
                try
                {
                    AnimatorControllerParameter existingParam = controller.parameters.FirstOrDefault(p => p.name == param.Name);
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
                    result.Errors.Add(param.Name + ": " + ex.Message);
                }
            }

            if (result.AddedCount > 0 || result.OverwrittenCount > 0)
            {
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
            }

            return result;
        }

        private static void AddParameter(AnimatorController controller, AnimatorParameterScanService.ParameterInfo param)
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

        private static void UpdateParameterDefaultValue(AnimatorController controller, AnimatorParameterScanService.ParameterInfo param)
        {
            var so = new SerializedObject(controller);
            SerializedProperty controllerProp = so.FindProperty("m_Controller");
            if (controllerProp == null)
            {
                return;
            }

            SerializedProperty parametersProp = controllerProp.FindPropertyRelative("m_AnimatorParameters");
            if (parametersProp == null)
            {
                return;
            }

            for (int i = 0; i < parametersProp.arraySize; i++)
            {
                SerializedProperty paramProp = parametersProp.GetArrayElementAtIndex(i);
                SerializedProperty nameProp = paramProp.FindPropertyRelative("m_Name");
                if (nameProp == null || nameProp.stringValue != param.Name)
                {
                    continue;
                }

                switch (param.Type)
                {
                    case AnimatorControllerParameterType.Bool:
                    {
                        SerializedProperty defaultBoolProp = paramProp.FindPropertyRelative("m_DefaultBool");
                        if (defaultBoolProp != null)
                        {
                            defaultBoolProp.boolValue = param.DefaultBool;
                        }
                        break;
                    }
                    case AnimatorControllerParameterType.Float:
                    {
                        SerializedProperty defaultFloatProp = paramProp.FindPropertyRelative("m_DefaultFloat");
                        if (defaultFloatProp != null)
                        {
                            defaultFloatProp.floatValue = param.DefaultFloat;
                        }
                        break;
                    }
                    case AnimatorControllerParameterType.Int:
                    {
                        SerializedProperty defaultIntProp = paramProp.FindPropertyRelative("m_DefaultInt");
                        if (defaultIntProp != null)
                        {
                            defaultIntProp.intValue = param.DefaultInt;
                        }
                        break;
                    }
                }

                so.ApplyModifiedProperties();
                break;
            }
        }

        private static void RemoveParameter(AnimatorController controller, string parameterName)
        {
            AnimatorControllerParameter[] parameters = controller.parameters;
            var newParameters = new List<AnimatorControllerParameter>();
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name != parameterName)
                {
                    newParameters.Add(parameters[i]);
                }
            }

            controller.parameters = newParameters.ToArray();
        }

        internal static void ApplyToExpressionParameters(
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
            {
                return;
            }

            AnimatorControllerParameter[] controllerParams = controller.parameters ?? new AnimatorControllerParameter[0];
            if (controllerParams.Length == 0)
            {
                return;
            }

            var paramList = new List<VRCExpressionParameters.Parameter>();
            if (expressionParameters.parameters != null)
            {
                paramList.AddRange(expressionParameters.parameters);
            }

            var existingNames = new HashSet<string>();
            for (int i = 0; i < paramList.Count; i++)
            {
                VRCExpressionParameters.Parameter p = paramList[i];
                if (!string.IsNullOrEmpty(p.name))
                {
                    existingNames.Add(p.name);
                }
            }

            bool dirty = false;
            for (int i = 0; i < controllerParams.Length; i++)
            {
                AnimatorControllerParameter cp = controllerParams[i];
                if (cp == null || string.IsNullOrEmpty(cp.name))
                {
                    continue;
                }

                bool isSelected = selectFlags != null && selectFlags.TryGetValue(cp.name, out bool sel) && sel;
                if (!isSelected)
                {
                    continue;
                }

                if (filterUnregistered && existingNames.Contains(cp.name))
                {
                    continue;
                }

                bool saveFlag = saveFlags != null && saveFlags.TryGetValue(cp.name, out bool s) && s;
                bool syncFlag = syncFlags != null && syncFlags.TryGetValue(cp.name, out bool y) && y;
                int index = paramList.FindIndex(p => p.name == cp.name);

                if (index < 0)
                {
                    VRCExpressionParameters.ValueType valueType = VRCExpressionParameters.ValueType.Int;
                    AnimatorControllerParameterType targetType = cp.type;
                    if (typeOverrides != null && typeOverrides.TryGetValue(cp.name, out AnimatorControllerParameterType overrideType))
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

                    float defaultVal;
                    if (defaultOverrides != null && defaultOverrides.TryGetValue(cp.name, out float overrideVal))
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
                    VRCExpressionParameters.Parameter p = paramList[index];
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
