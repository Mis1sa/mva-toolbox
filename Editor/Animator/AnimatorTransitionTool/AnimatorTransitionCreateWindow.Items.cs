using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.AnimatorTransitionTool
{
    internal sealed partial class AnimatorTransitionCreateWindow
    {
        private void DrawCreateItemsList()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("添加过渡", GUILayout.Width(160f), GUILayout.Height(30f)))
            {
                CreateTransitionItem item = new CreateTransitionItem
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
                CreateTransitionItem item = _createItems[i];
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
                DrawTransitionConditionsList(_owner.SelectedController, item.conditions);
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
                ConditionUI cond = conditions[i];
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
                    for (int globalIndex = 0; globalIndex < _globalConditions.Count; globalIndex++)
                    {
                        ConditionUI globalCond = _globalConditions[globalIndex];
                        if (globalCond != null && globalCond.globalGuid == cond.globalGuid && globalCond.isGlobalOverride)
                        {
                            isGloballyOverridden = true;
                            break;
                        }
                    }
                }

                EditorGUI.BeginDisabledGroup(isGlobalSynced);
                DrawConditionParameterPopup(cond, false);
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
                if (HasNonTriggerParameter(controller))
                {
                    conditions.Add(new ConditionUI
                    {
                        boolValue = true,
                        mode = AnimatorConditionMode.Greater
                    });
                }
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }
    }
}
