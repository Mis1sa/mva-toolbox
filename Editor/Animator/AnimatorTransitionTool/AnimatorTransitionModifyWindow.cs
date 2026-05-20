using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.AnimatorTransitionTool
{
    internal sealed partial class AnimatorTransitionModifyWindow
    {
        private enum ModifyActionMode
        {
            ModifyTransitions,
            ConditionDelta
        }

        private readonly AnimatorTransitionToolWindow _owner;
        private AnimatorTransitionModifyService.ModifyMode _modifyMode = AnimatorTransitionModifyService.ModifyMode.FromStateTransitions;
        private int _modifyStateIndex;
        private ModifyActionMode _modifyActionMode = ModifyActionMode.ModifyTransitions;
        private bool _modifyHasExitTimeValue = true;
        private float _modifyExitTimeValue = 0.75f;
        private bool _modifyHasFixedDurationValue = true;
        private float _modifyDurationValue = 0.25f;
        private float _modifyOffsetValue;
        private bool _modifyConditions;
        private readonly List<ModifyConditionSettingUI> _modifyConditionSettings = new List<ModifyConditionSettingUI>();
        private readonly List<ConditionDeltaSettingUI> _conditionDeltaSettings = new List<ConditionDeltaSettingUI>();

        internal AnimatorTransitionModifyWindow(AnimatorTransitionToolWindow owner)
        {
            _owner = owner;
        }

        internal void Reset()
        {
            _modifyMode = AnimatorTransitionModifyService.ModifyMode.FromStateTransitions;
            _modifyStateIndex = 0;
            _modifyActionMode = ModifyActionMode.ModifyTransitions;
            _modifyHasExitTimeValue = true;
            _modifyExitTimeValue = 0.75f;
            _modifyHasFixedDurationValue = true;
            _modifyDurationValue = 0.25f;
            _modifyOffsetValue = 0f;
            _modifyConditions = false;
            _modifyConditionSettings.Clear();
            _conditionDeltaSettings.Clear();
        }

        internal void OnGUI()
        {
            if (_owner.SelectedController == null)
            {
                return;
            }

            DrawModifyModeSelection();
            EditorGUILayout.Space(4f);
            if (_modifyActionMode == ModifyActionMode.ModifyTransitions)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("过渡属性", EditorStyles.boldLabel);
                DrawModifyProperties();
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(4f);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawModifyConditions();
                EditorGUILayout.EndVertical();
            }
            else
            {
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
    }
}
