using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.AnimatorTransitionTool
{
    internal sealed partial class AnimatorTransitionModifyWindow
    {
        private sealed class ConditionDeltaSettingUI
        {
            internal enum ConditionOp
            {
                Append,
                AddUnique,
                Remove
            }

            private static readonly string[] OpLabels = { "追加", "增加(去重)", "移除" };
            private static readonly string[] BoolModes = { "True", "False" };
            private static readonly string[] BoolModesWithAll = { "True", "False", "全部" };
            private static readonly string[] FloatModes = { "Greater", "Less" };
            private static readonly string[] FloatModesWithAll = { "Greater", "Less", "全部" };
            private static readonly string[] IntModes = { "Greater", "Less", "Equals", "NotEquals" };
            private static readonly string[] IntModesWithAll = { "Greater", "Less", "Equals", "NotEquals", "全部" };

            internal string parameterName;
            internal AnimatorConditionMode mode;
            internal float threshold;
            internal bool removeAllForParameter;
            internal ConditionOp operation = ConditionOp.Append;
            internal bool ignoreCondition = true;
            internal bool requestRemove;
            private int _selectedParameterIndex;

            internal void Draw(AnimatorController controller)
            {
                List<string> availableNames = AnimatorTransitionModifyWindow.GetNonTriggerParameterNames(controller);
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

                AnimatorControllerParameter selectedParam = AnimatorTransitionModifyWindow.FindParameter(controller, parameterName);
                int opIndex = operation == ConditionOp.AddUnique ? 1 : operation == ConditionOp.Remove ? 2 : 0;
                int newOpIndex = EditorGUILayout.Popup(opIndex, OpLabels, GUILayout.Width(100f));
                ConditionOp newOp = newOpIndex == 1 ? ConditionOp.AddUnique : newOpIndex == 2 ? ConditionOp.Remove : ConditionOp.Append;
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
                            string[] labels = includeAll ? BoolModesWithAll : BoolModes;
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

                            GUILayout.Label(string.Empty, GUILayout.Width(60f));
                            break;
                        }
                        case AnimatorControllerParameterType.Float:
                        {
                            bool includeAll = operation == ConditionOp.Remove;
                            string[] labels = includeAll ? FloatModesWithAll : FloatModes;
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
                            string[] labels = includeAll ? IntModesWithAll : IntModes;
                            int modeIndex;
                            if (includeAll && removeAllForParameter)
                            {
                                modeIndex = labels.Length - 1;
                            }
                            else
                            {
                                modeIndex = 0;
                                if (mode == AnimatorConditionMode.Less)
                                {
                                    modeIndex = 1;
                                }
                                else if (mode == AnimatorConditionMode.Equals)
                                {
                                    modeIndex = 2;
                                }
                                else if (mode == AnimatorConditionMode.NotEqual)
                                {
                                    modeIndex = 3;
                                }
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
                            if (newThreshold < 0)
                            {
                                newThreshold = 0;
                            }
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

        private sealed class ModifyConditionSettingUI
        {
            private static readonly string[] IntModes = { "Greater", "Less", "Equals", "NotEquals" };

            internal enum IntIncrementDirection
            {
                Increment,
                Decrement
            }

            internal enum LocalSortMode
            {
                ArrangementOrder,
                NameNumberOrder
            }

            internal string parameterName;
            internal AnimatorConditionMode mode;
            internal float threshold;
            internal bool enableIntAutoIncrement;
            internal IntIncrementDirection incrementDirection = IntIncrementDirection.Increment;
            internal LocalSortMode sortMode = LocalSortMode.ArrangementOrder;
            internal int incrementStep = 1;
            internal float floatIncrementStep = 0.01f;
            internal bool requestRemove;
            private int _selectedParameterIndex;

            internal void Draw(AnimatorController controller, AnimatorTransitionModifyService.ModifyMode modifyMode)
            {
                List<string> availableNames = AnimatorTransitionModifyWindow.GetNonTriggerParameterNames(controller);
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

                AnimatorControllerParameter selectedParam = AnimatorTransitionModifyWindow.FindParameter(controller, parameterName);
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
                            if (mode == AnimatorConditionMode.Less)
                            {
                                intModeIndex = 1;
                            }
                            else if (mode == AnimatorConditionMode.Equals)
                            {
                                intModeIndex = 2;
                            }
                            else if (mode == AnimatorConditionMode.NotEqual)
                            {
                                intModeIndex = 3;
                            }

                            intModeIndex = EditorGUILayout.Popup(intModeIndex, IntModes, GUILayout.Width(80f));
                            switch (intModeIndex)
                            {
                                case 0: mode = AnimatorConditionMode.Greater; break;
                                case 1: mode = AnimatorConditionMode.Less; break;
                                case 2: mode = AnimatorConditionMode.Equals; break;
                                case 3: mode = AnimatorConditionMode.NotEqual; break;
                            }

                            int newThreshold = EditorGUILayout.IntField((int)threshold, GUILayout.Width(60f));
                            if (newThreshold < 0)
                            {
                                newThreshold = 0;
                            }
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

                if (selectedParam != null && (selectedParam.type == AnimatorControllerParameterType.Int || selectedParam.type == AnimatorControllerParameterType.Float))
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
                            if (newStep < 1)
                            {
                                newStep = 1;
                            }
                            if (newStep > 255)
                            {
                                newStep = 255;
                            }
                            incrementStep = newStep;
                        }
                        else
                        {
                            float floatStep = EditorGUILayout.FloatField(floatIncrementStep, GUILayout.Width(60f));
                            floatIncrementStep = floatStep;
                        }

                        GUILayout.Label("排列方式", GUILayout.Width(70f));
                        if (modifyMode == AnimatorTransitionModifyService.ModifyMode.ToStateTransitions)
                        {
                            GUILayout.Label("按状态名称");
                            sortMode = LocalSortMode.NameNumberOrder;
                        }
                        else
                        {
                            string[] sortOptions = { "按连接顺序", "按状态名称" };
                            int sortIndex = sortMode == LocalSortMode.ArrangementOrder ? 0 : 1;
                            sortIndex = EditorGUILayout.Popup(sortIndex, sortOptions);
                            sortMode = sortIndex == 0 ? LocalSortMode.ArrangementOrder : LocalSortMode.NameNumberOrder;
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }
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
                ConditionDeltaSettingUI setting = _conditionDeltaSettings[i];
                if (setting == null)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                setting.Draw(_owner.SelectedController);
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5f);
                if (setting.requestRemove)
                {
                    _conditionDeltaSettings.RemoveAt(i);
                }
            }
        }

        private void DrawModifyConditions()
        {
            EditorGUILayout.BeginHorizontal();
            _modifyConditions = EditorGUILayout.Toggle(_modifyConditions, GUILayout.Width(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.LabelField("条件设置", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            if (!_modifyConditions)
            {
                return;
            }

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
                ModifyConditionSettingUI setting = _modifyConditionSettings[i];
                if (setting == null)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                setting.Draw(_owner.SelectedController, _modifyMode);
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5f);
                if (setting.requestRemove)
                {
                    _modifyConditionSettings.RemoveAt(i);
                }
            }
        }

        private static List<string> GetNonTriggerParameterNames(AnimatorController controller)
        {
            List<string> availableNames = new List<string>();
            if (controller == null || controller.parameters == null)
            {
                return availableNames;
            }

            for (int i = 0; i < controller.parameters.Length; i++)
            {
                AnimatorControllerParameter p = controller.parameters[i];
                if (p != null && p.type != AnimatorControllerParameterType.Trigger)
                {
                    availableNames.Add(p.name);
                }
            }

            return availableNames;
        }

        private static AnimatorControllerParameter FindParameter(AnimatorController controller, string parameterName)
        {
            if (controller == null || controller.parameters == null || string.IsNullOrEmpty(parameterName))
            {
                return null;
            }

            for (int i = 0; i < controller.parameters.Length; i++)
            {
                AnimatorControllerParameter p = controller.parameters[i];
                if (p != null && p.name == parameterName)
                {
                    return p;
                }
            }

            return null;
        }
    }
}
