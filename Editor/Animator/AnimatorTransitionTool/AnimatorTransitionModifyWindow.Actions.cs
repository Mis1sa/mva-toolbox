using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace MVA.Toolbox.AnimatorTransitionTool
{
    internal sealed partial class AnimatorTransitionModifyWindow
    {
        private void ApplyModify()
        {
            List<string> options = BuildModifyTargetStateOptions(_modifyMode);
            if (_modifyStateIndex < 0 || _modifyStateIndex >= options.Count)
            {
                return;
            }

            string selectedDisplayPath = options[_modifyStateIndex];
            string targetStatePath = ResolveTargetStatePath(selectedDisplayPath);

            if (_modifyActionMode == ModifyActionMode.ModifyTransitions)
            {
                List<AnimatorTransitionModifyService.ConditionSetting> serviceSettings = _modifyConditionSettings.Select(s => new AnimatorTransitionModifyService.ConditionSetting
                {
                    parameterName = s.parameterName,
                    mode = s.mode,
                    threshold = s.threshold,
                    enableIntAutoIncrement = s.enableIntAutoIncrement,
                    incrementDirection = s.incrementDirection == ModifyConditionSettingUI.IntIncrementDirection.Decrement
                        ? AnimatorTransitionModifyService.IncrementDirection.Decrement
                        : AnimatorTransitionModifyService.IncrementDirection.Increment,
                    sortMode = s.sortMode == ModifyConditionSettingUI.LocalSortMode.NameNumberOrder
                        ? AnimatorTransitionModifyService.SortMode.NameNumberOrder
                        : AnimatorTransitionModifyService.SortMode.ArrangementOrder,
                    incrementStep = s.incrementStep,
                    floatIncrementStep = s.floatIncrementStep
                }).ToList();

                AnimatorTransitionModifyService.Execute(
                    _owner.SelectedController,
                    _owner.SelectedLayer,
                    _modifyMode,
                    targetStatePath,
                    _modifyHasExitTimeValue,
                    _modifyExitTimeValue,
                    _modifyHasFixedDurationValue,
                    _modifyDurationValue,
                    _modifyOffsetValue,
                    _modifyConditions,
                    serviceSettings);
            }
            else
            {
                List<AnimatorTransitionModifyService.ConditionDeltaSetting> deltaSettings = _conditionDeltaSettings.Select(s => new AnimatorTransitionModifyService.ConditionDeltaSetting
                {
                    parameterName = s.parameterName,
                    mode = s.mode,
                    threshold = s.threshold,
                    operation = s.operation == ConditionDeltaSettingUI.ConditionOp.AddUnique
                        ? AnimatorTransitionModifyService.ConditionDeltaOperation.AddUnique
                        : s.operation == ConditionDeltaSettingUI.ConditionOp.Remove
                            ? AnimatorTransitionModifyService.ConditionDeltaOperation.Remove
                            : AnimatorTransitionModifyService.ConditionDeltaOperation.Append,
                    removeAllForParameter = s.operation == ConditionDeltaSettingUI.ConditionOp.Remove && s.removeAllForParameter,
                    ignoreCondition = s.operation == ConditionDeltaSettingUI.ConditionOp.AddUnique && s.ignoreCondition
                }).ToList();

                AnimatorTransitionModifyService.ExecuteConditionDelta(
                    _owner.SelectedController,
                    _owner.SelectedLayer,
                    _modifyMode,
                    targetStatePath,
                    deltaSettings);
            }

            EditorUtility.DisplayDialog("完成", "过渡修改完成！", "确定");
        }
    }
}
