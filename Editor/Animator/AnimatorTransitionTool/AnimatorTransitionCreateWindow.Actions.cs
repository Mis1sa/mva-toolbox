using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;

namespace MVA.Toolbox.AnimatorTransitionTool
{
    internal sealed partial class AnimatorTransitionCreateWindow
    {
        private void CreateTransitions()
        {
            if (_owner.SelectedController == null || _owner.SelectedLayer == null)
            {
                EditorUtility.DisplayDialog("错误", "请先选择控制器和层级。", "确定");
                return;
            }

            bool useAnyStateAsSource = _createSourceState == "Any State";
            bool toExit = _createDestState == "Exit";
            AnimatorState sourceState = null;
            AnimatorState destState = null;
            AnimatorStateMachine destStateMachine = null;

            if (!useAnyStateAsSource)
            {
                if (_owner.IsStateMachineDisplayPath(_createSourceState))
                {
                    EditorUtility.DisplayDialog("错误", "不能从子状态机创建过渡，请选择具体状态或 Any State。", "确定");
                    return;
                }

                sourceState = _owner.ResolveState(_createSourceState);
                if (sourceState == null)
                {
                    EditorUtility.DisplayDialog("错误", $"未找到源状态: {_createSourceState}", "确定");
                    return;
                }
            }

            if (!toExit)
            {
                if (_owner.IsStateMachineDisplayPath(_createDestState))
                {
                    destStateMachine = _owner.ResolveStateMachine(_createDestState);
                    if (destStateMachine == null)
                    {
                        EditorUtility.DisplayDialog("错误", $"未找到目标子状态机: {_createDestState}", "确定");
                        return;
                    }
                }
                else
                {
                    destState = _owner.ResolveState(_createDestState);
                    if (destState == null)
                    {
                        EditorUtility.DisplayDialog("错误", $"未找到目标状态: {_createDestState}", "确定");
                        return;
                    }
                }
            }

            List<AnimatorTransitionCreateService.TransitionItemSettings> transitionItems = new List<AnimatorTransitionCreateService.TransitionItemSettings>();
            for (int itemIndex = 0; itemIndex < _createItems.Count; itemIndex++)
            {
                CreateTransitionItem item = _createItems[itemIndex];
                List<AnimatorTransitionCreateService.ConditionSettings> conditions = new List<AnimatorTransitionCreateService.ConditionSettings>();
                for (int conditionIndex = 0; conditionIndex < item.conditions.Count; conditionIndex++)
                {
                    ConditionUI cond = item.conditions[conditionIndex];
                    conditions.Add(new AnimatorTransitionCreateService.ConditionSettings
                    {
                        parameterName = cond.parameterName,
                        parameterType = cond.parameterType,
                        floatValue = cond.floatValue,
                        intValue = cond.intValue,
                        boolValue = cond.boolValue,
                        mode = cond.mode
                    });
                }

                transitionItems.Add(new AnimatorTransitionCreateService.TransitionItemSettings
                {
                    overrideHasExitTime = item.overrideHasExitTime,
                    hasExitTime = item.hasExitTime,
                    overrideExitTime = item.overrideExitTime,
                    exitTime = item.exitTime,
                    overrideHasFixedDuration = item.overrideHasFixedDuration,
                    hasFixedDuration = item.hasFixedDuration,
                    overrideDuration = item.overrideDuration,
                    duration = item.duration,
                    overrideOffset = item.overrideOffset,
                    offset = item.offset,
                    overrideCanTransitionToSelf = item.overrideCanTransitionToSelf,
                    canTransitionToSelf = item.canTransitionToSelf,
                    conditions = conditions
                });
            }

            List<AnimatorTransitionCreateService.ConditionSettings> globalConditions = new List<AnimatorTransitionCreateService.ConditionSettings>();
            for (int i = 0; i < _globalConditions.Count; i++)
            {
                ConditionUI cond = _globalConditions[i];
                globalConditions.Add(new AnimatorTransitionCreateService.ConditionSettings
                {
                    parameterName = cond.parameterName,
                    parameterType = cond.parameterType,
                    floatValue = cond.floatValue,
                    intValue = cond.intValue,
                    boolValue = cond.boolValue,
                    mode = cond.mode
                });
            }

            AnimatorTransitionCreateService.ExecuteResult result = AnimatorTransitionCreateService.Execute(
                _owner.SelectedController,
                _owner.SelectedLayer,
                useAnyStateAsSource,
                sourceState,
                toExit,
                destState,
                destStateMachine,
                _defaultHasExitTime,
                _defaultExitTime,
                _defaultHasFixedDuration,
                _defaultDuration,
                _defaultOffset,
                _defaultCanTransitionToSelf,
                transitionItems,
                globalConditions);

            if (result.Success)
            {
                EditorUtility.DisplayDialog("完成", $"成功创建 {result.CreatedCount} 个过渡！", "确定");
                Reset();
                _owner.RefreshStateListForCurrentSelection();
            }
            else
            {
                EditorUtility.DisplayDialog("错误", result.ErrorMessage, "确定");
            }
        }
    }
}
