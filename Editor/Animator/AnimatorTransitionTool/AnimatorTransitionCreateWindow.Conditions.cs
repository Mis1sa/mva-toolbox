using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.AnimatorTransitionTool
{
    internal sealed partial class AnimatorTransitionCreateWindow
    {
        private void DrawDefaultSettings()
        {
            DrawBoolDefaultSetting("Has Exit Time", ref _defaultHasExitTime, ref _overrideHasExitTime, value =>
            {
                for (int i = 0; i < _createItems.Count; i++)
                {
                    _createItems[i].overrideHasExitTime = value;
                }
            });

            DrawFloatDefaultSetting("Exit Time", ref _defaultExitTime, ref _overrideExitTime, value =>
            {
                for (int i = 0; i < _createItems.Count; i++)
                {
                    _createItems[i].overrideExitTime = value;
                }
            });

            DrawBoolDefaultSetting("Fixed Duration", ref _defaultHasFixedDuration, ref _overrideHasFixedDuration, value =>
            {
                for (int i = 0; i < _createItems.Count; i++)
                {
                    _createItems[i].overrideHasFixedDuration = value;
                }
            });

            DrawFloatDefaultSetting("Transition Duration (s)", ref _defaultDuration, ref _overrideDuration, value =>
            {
                for (int i = 0; i < _createItems.Count; i++)
                {
                    _createItems[i].overrideDuration = value;
                }
            });

            DrawFloatDefaultSetting("Transition Offset", ref _defaultOffset, ref _overrideOffset, value =>
            {
                for (int i = 0; i < _createItems.Count; i++)
                {
                    _createItems[i].overrideOffset = value;
                }
            });

            if (_createSourceState == "Any State")
            {
                DrawBoolDefaultSetting("Can Transition To Self", ref _defaultCanTransitionToSelf, ref _overrideCanTransitionToSelf, value =>
                {
                    for (int i = 0; i < _createItems.Count; i++)
                    {
                        _createItems[i].overrideCanTransitionToSelf = value;
                    }
                });
            }
        }

        private static void DrawBoolDefaultSetting(string label, ref bool value, ref bool overrideFlag, Action<bool> onOverrideChanged)
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

        private static void DrawFloatDefaultSetting(string label, ref float value, ref bool overrideFlag, Action<bool> onOverrideChanged)
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
                ConditionUI cond = _globalConditions[i];
                if (cond == null)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginDisabledGroup(cond.isGlobalOverride);
                DrawConditionParameterPopup(cond, true);
                EditorGUI.EndDisabledGroup();
                DrawConditionValueFields(cond);
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
                for (int i = 0; i < _createItems.Count; i++)
                {
                    _createItems[i].conditions.RemoveAll(c => c.isGlobalOverride && c.globalGuid == guidToRemove);
                }
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("添加全局条件", GUILayout.Width(140f)))
            {
                if (HasNonTriggerParameter(_owner.SelectedController))
                {
                    _globalConditions.Add(new ConditionUI
                    {
                        globalGuid = Guid.NewGuid().ToString(),
                        boolValue = true,
                        mode = AnimatorConditionMode.Greater
                    });
                }
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void SyncGlobalConditionsToTransitions()
        {
            List<string> globalGuids = new List<string>();
            for (int i = 0; i < _globalConditions.Count; i++)
            {
                ConditionUI globalCondition = _globalConditions[i];
                if (globalCondition != null && !string.IsNullOrEmpty(globalCondition.globalGuid))
                {
                    globalGuids.Add(globalCondition.globalGuid);
                }
            }

            for (int itemIndex = 0; itemIndex < _createItems.Count; itemIndex++)
            {
                CreateTransitionItem transition = _createItems[itemIndex];
                transition.conditions.RemoveAll(c => c.isGlobalOverride && !globalGuids.Contains(c.globalGuid));

                for (int globalIndex = 0; globalIndex < _globalConditions.Count; globalIndex++)
                {
                    ConditionUI globalCond = _globalConditions[globalIndex];
                    if (globalCond == null || string.IsNullOrEmpty(globalCond.globalGuid))
                    {
                        continue;
                    }

                    ConditionUI existing = transition.conditions.Find(c => c.isGlobalOverride && c.globalGuid == globalCond.globalGuid);
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

        private void DrawConditionParameterPopup(ConditionUI cond, bool showParameterLabel)
        {
            AnimatorController controller = _owner.SelectedController;
            AnimatorControllerParameter[] validParameters = controller.parameters;
            List<AnimatorControllerParameter> nonTriggerParams = new List<AnimatorControllerParameter>();
            for (int i = 0; i < validParameters.Length; i++)
            {
                AnimatorControllerParameter p = validParameters[i];
                if (p != null && p.type != AnimatorControllerParameterType.Trigger)
                {
                    nonTriggerParams.Add(p);
                }
            }

            string[] parameterNames = new string[nonTriggerParams.Count];
            for (int i = 0; i < nonTriggerParams.Count; i++)
            {
                parameterNames[i] = nonTriggerParams[i].name;
            }

            int currentParamIndex = -1;
            if (!string.IsNullOrEmpty(cond.parameterName))
            {
                for (int i = 0; i < parameterNames.Length; i++)
                {
                    if (parameterNames[i] == cond.parameterName)
                    {
                        currentParamIndex = i;
                        break;
                    }
                }
            }

            EditorGUI.BeginChangeCheck();
            if (showParameterLabel)
            {
                EditorGUILayout.LabelField("参数", GUILayout.Width(40f));
            }
            int newParamIndex = EditorGUILayout.Popup(currentParamIndex, parameterNames, GUILayout.MinWidth(80f), GUILayout.MaxWidth(200f));
            if (EditorGUI.EndChangeCheck())
            {
                if (newParamIndex >= 0 && newParamIndex < nonTriggerParams.Count)
                {
                    AnimatorControllerParameter p = nonTriggerParams[newParamIndex];
                    cond.parameterName = p.name;
                    cond.parameterType = p.type;
                }
            }
        }

        private static void DrawConditionValueFields(ConditionUI condition)
        {
            if (string.IsNullOrEmpty(condition.parameterName))
            {
                const float modeWidth = 80f;
                const float valueWidth = 60f;
                float padding = EditorGUIUtility.standardVerticalSpacing;
                GUILayout.Label(string.Empty, GUILayout.Width(modeWidth + valueWidth + padding));
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

                    GUILayout.Label(string.Empty, GUILayout.Width(60f));
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
                    if (condition.mode != AnimatorConditionMode.Greater && condition.mode != AnimatorConditionMode.Less && condition.mode != AnimatorConditionMode.Equals && condition.mode != AnimatorConditionMode.NotEqual)
                    {
                        condition.mode = AnimatorConditionMode.Greater;
                    }

                    string[] intModes = { "Greater", "Less", "Equals", "NotEquals" };
                    int modeIndex = 0;
                    if (condition.mode == AnimatorConditionMode.Less)
                    {
                        modeIndex = 1;
                    }
                    else if (condition.mode == AnimatorConditionMode.Equals)
                    {
                        modeIndex = 2;
                    }
                    else if (condition.mode == AnimatorConditionMode.NotEqual)
                    {
                        modeIndex = 3;
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

        private static bool HasNonTriggerParameter(AnimatorController controller)
        {
            if (controller == null || controller.parameters == null)
            {
                return false;
            }

            for (int i = 0; i < controller.parameters.Length; i++)
            {
                AnimatorControllerParameter p = controller.parameters[i];
                if (p != null && p.type != AnimatorControllerParameterType.Trigger)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
