using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.AnimatorTransitionTool
{
    internal static class AnimatorTransitionCreateService
    {
        internal struct TransitionItemSettings
        {
            public bool overrideHasExitTime;
            public bool hasExitTime;
            public bool overrideExitTime;
            public float exitTime;
            public bool overrideHasFixedDuration;
            public bool hasFixedDuration;
            public bool overrideDuration;
            public float duration;
            public bool overrideOffset;
            public float offset;
            public bool overrideCanTransitionToSelf;
            public bool canTransitionToSelf;
            public List<ConditionSettings> conditions;
        }

        internal struct ConditionSettings
        {
            public string parameterName;
            public AnimatorControllerParameterType parameterType;
            public float floatValue;
            public int intValue;
            public bool boolValue;
            public AnimatorConditionMode mode;
        }

        internal struct ExecuteResult
        {
            public bool Success;
            public int CreatedCount;
            public string ErrorMessage;
        }

        internal static ExecuteResult Execute(
            AnimatorController controller,
            AnimatorControllerLayer layer,
            bool useAnyStateAsSource,
            AnimatorState sourceState,
            bool toExit,
            AnimatorState destState,
            AnimatorStateMachine destStateMachine,
            bool defaultHasExitTime,
            float defaultExitTime,
            bool defaultHasFixedDuration,
            float defaultDuration,
            float defaultOffset,
            bool defaultCanTransitionToSelf,
            IReadOnlyList<TransitionItemSettings> transitionItems,
            IReadOnlyList<ConditionSettings> globalConditions)
        {
            if (controller == null || layer == null)
            {
                return new ExecuteResult { Success = false, ErrorMessage = "控制器或层级为空" };
            }

            if (transitionItems == null || transitionItems.Count == 0)
            {
                return new ExecuteResult { Success = false, ErrorMessage = "没有过渡条目" };
            }

            AnimatorStateMachine stateMachine = layer.stateMachine;
            if (stateMachine == null)
            {
                return new ExecuteResult { Success = false, ErrorMessage = "层级状态机为空" };
            }

            if (useAnyStateAsSource && toExit)
            {
                return new ExecuteResult { Success = false, ErrorMessage = "不支持从 Any State 到 Exit 的过渡" };
            }

            if (!useAnyStateAsSource && sourceState == null)
            {
                return new ExecuteResult { Success = false, ErrorMessage = "源状态为空" };
            }

            if (!toExit && destState == null && destStateMachine == null)
            {
                return new ExecuteResult { Success = false, ErrorMessage = "目标状态为空" };
            }

            bool destIsStateMachine = destStateMachine != null;
            Undo.RecordObject(controller, "Animator Transition - Create Transitions");

            int createdCount = 0;
            for (int i = 0; i < transitionItems.Count; i++)
            {
                TransitionItemSettings item = transitionItems[i];
                AnimatorStateTransition transition = null;

                if (toExit)
                {
                    transition = sourceState.AddExitTransition();
                }
                else if (useAnyStateAsSource)
                {
                    transition = destIsStateMachine
                        ? stateMachine.AddAnyStateTransition(destStateMachine)
                        : stateMachine.AddAnyStateTransition(destState);
                }
                else
                {
                    transition = destIsStateMachine
                        ? sourceState.AddTransition(destStateMachine)
                        : sourceState.AddTransition(destState);
                }

                if (transition == null)
                {
                    continue;
                }

                transition.hasExitTime = item.overrideHasExitTime ? item.hasExitTime : defaultHasExitTime;
                transition.exitTime = item.overrideExitTime ? item.exitTime : defaultExitTime;
                transition.hasFixedDuration = item.overrideHasFixedDuration ? item.hasFixedDuration : defaultHasFixedDuration;
                transition.duration = item.overrideDuration ? item.duration : defaultDuration;
                transition.offset = item.overrideOffset ? item.offset : defaultOffset;
                transition.canTransitionToSelf = item.overrideCanTransitionToSelf ? item.canTransitionToSelf : defaultCanTransitionToSelf;

                if (item.conditions != null)
                {
                    for (int conditionIndex = 0; conditionIndex < item.conditions.Count; conditionIndex++)
                    {
                        ConditionSettings cond = item.conditions[conditionIndex];
                        if (!string.IsNullOrEmpty(cond.parameterName))
                        {
                            AddConditionToTransition(transition, cond);
                        }
                    }
                }

                if (globalConditions != null)
                {
                    for (int globalIndex = 0; globalIndex < globalConditions.Count; globalIndex++)
                    {
                        ConditionSettings globalCond = globalConditions[globalIndex];
                        if (string.IsNullOrEmpty(globalCond.parameterName))
                        {
                            continue;
                        }

                        bool isOverridden = false;
                        if (item.conditions != null)
                        {
                            for (int itemIndex = 0; itemIndex < item.conditions.Count; itemIndex++)
                            {
                                if (item.conditions[itemIndex].parameterName == globalCond.parameterName)
                                {
                                    isOverridden = true;
                                    break;
                                }
                            }
                        }

                        if (!isOverridden)
                        {
                            AddConditionToTransition(transition, globalCond);
                        }
                    }
                }

                createdCount++;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return new ExecuteResult { Success = true, CreatedCount = createdCount };
        }

        private static void AddConditionToTransition(AnimatorStateTransition transition, ConditionSettings cond)
        {
            float threshold = 0f;
            AnimatorConditionMode mode = cond.mode;

            switch (cond.parameterType)
            {
                case AnimatorControllerParameterType.Bool:
                    mode = cond.mode == AnimatorConditionMode.If || cond.mode == AnimatorConditionMode.IfNot
                        ? cond.mode : AnimatorConditionMode.If;
                    break;
                case AnimatorControllerParameterType.Float:
                    threshold = cond.floatValue;
                    break;
                case AnimatorControllerParameterType.Int:
                    threshold = cond.intValue;
                    break;
                case AnimatorControllerParameterType.Trigger:
                    mode = AnimatorConditionMode.If;
                    break;
            }

            transition.AddCondition(mode, threshold, cond.parameterName);
        }
    }
}
