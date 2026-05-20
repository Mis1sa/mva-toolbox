using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.AnimatorTransitionTool
{
    internal sealed partial class AnimatorTransitionModifyWindow
    {
        private void DrawModifyModeSelection()
        {
            string[] actionModeOptions = { "修改过渡", "增减Conditions" };
            int actionModeIndex = (int)_modifyActionMode;
            actionModeIndex = EditorGUILayout.Popup("修改模式", actionModeIndex, actionModeOptions);
            _modifyActionMode = (ModifyActionMode)actionModeIndex;

            string[] modifyModeOptions = { "由选择的状态出发", "到达选择的状态" };
            int modeIndex = (int)_modifyMode;
            modeIndex = EditorGUILayout.Popup("过渡选择", modeIndex, modifyModeOptions);
            _modifyMode = (AnimatorTransitionModifyService.ModifyMode)modeIndex;

            List<string> options = BuildModifyTargetStateOptions(_modifyMode);
            if (options.Count == 0)
            {
                return;
            }

            _modifyStateIndex = Mathf.Clamp(_modifyStateIndex, 0, options.Count - 1);
            _modifyStateIndex = EditorGUILayout.Popup("目标状态", _modifyStateIndex, options.ToArray());
        }

        private List<string> BuildModifyTargetStateOptions(AnimatorTransitionModifyService.ModifyMode modifyMode)
        {
            List<string> options = new List<string>();
            if (modifyMode == AnimatorTransitionModifyService.ModifyMode.FromStateTransitions)
            {
                options.Add("Any State");
            }

            options.AddRange(_owner.GetDisplayPathOptions());
            if (modifyMode == AnimatorTransitionModifyService.ModifyMode.ToStateTransitions)
            {
                options.Add("Exit");
            }

            return options;
        }

        private string ResolveTargetStatePath(string selectedDisplayPath)
        {
            if (selectedDisplayPath == "Any State" || selectedDisplayPath == "Exit")
            {
                return selectedDisplayPath;
            }

            return _owner.ResolveActualPath(selectedDisplayPath);
        }
    }
}
