using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.QuickAnimatorEdit.Services.Transition
{
    /// <summary>
    /// 过渡创建服务
    /// 批量创建从源状态到目标状态的过渡，支持子状态机作为目标
    /// </summary>
    public static class TransitionCreateService
    {
        /// <summary>
        /// 过渡条目设置
        /// </summary>
        public struct TransitionItemSettings
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

        /// <summary>
        /// 条件设置
        /// </summary>
        public struct ConditionSettings
        {
            public string parameterName;
            public AnimatorControllerParameterType parameterType;
            public float floatValue;
            public int intValue;
            public bool boolValue;
            public AnimatorConditionMode mode;
            public bool isGlobalOverride;
        }

        /// <summary>
        /// 执行结果
        /// </summary>
        public struct ExecuteResult
        {
            public bool Success;
            public int CreatedCount;
            public string ErrorMessage;
        }

        /// <summary>
        /// 执行过渡创建
        /// </summary>
        /// <param name="controller">目标控制器</param>
        /// <param name="layer">目标层</param>
        /// <param name="useAnyStateAsSource">是否使用 Any State 作为源</param>
        /// <param name="sourceState">源状态（当 useAnyStateAsSource=false 时使用）</param>
        /// <param name="toExit">是否目标为 Exit</param>
        /// <param name="destState">目标状态（当目标不是子状态机且不是 Exit 时使用）</param>
        /// <param name="destStateMachine">目标子状态机（当目标是子状态机时使用）</param>
        /// <param name="defaultHasExitTime">默认是否有退出时间</param>
        /// <param name="defaultExitTime">默认退出时间</param>
        /// <param name="defaultHasFixedDuration">默认是否固定时长</param>
        /// <param name="defaultDuration">默认时长</param>
        /// <param name="defaultOffset">默认偏移</param>
        /// <param name="defaultCanTransitionToSelf">默认是否可过渡到自身</param>
        /// <param name="transitionItems">过渡条目设置列表</param>
        /// <param name="globalConditions">全局条件列表</param>
        public static ExecuteResult Execute(
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

            var stateMachine = layer.stateMachine;
            if (stateMachine == null)
            {
                return new ExecuteResult { Success = false, ErrorMessage = "层级状态机为空" };
            }

            // 验证：不能从 Any State 到 Exit
            if (useAnyStateAsSource && toExit)
            {
                return new ExecuteResult { Success = false, ErrorMessage = "不支持从 Any State 到 Exit 的过渡" };
            }

            // 验证：非 Any State 源时必须有源状态
            if (!useAnyStateAsSource && sourceState == null)
            {
                return new ExecuteResult { Success = false, ErrorMessage = "源状态为空" };
            }

            // 验证：非 Exit 目标时必须有目标状态或子状态机
            if (!toExit && destState == null && destStateMachine == null)
            {
                return new ExecuteResult { Success = false, ErrorMessage = "目标状态为空" };
            }

            bool destIsStateMachine = destStateMachine != null;

            Undo.RecordObject(controller, "Quick Transition - Create Transitions");

            int createdCount = 0;
            foreach (var item in transitionItems)
            {
                AnimatorStateTransition transition = null;

                if (toExit)
                {
                    transition = sourceState.AddExitTransition();
                }
                else if (useAnyStateAsSource)
                {
                    if (destIsStateMachine)
                    {
                        transition = stateMachine.AddAnyStateTransition(destStateMachine);
                    }
                    else
                    {
                        transition = stateMachine.AddAnyStateTransition(destState);
                    }
                }
                else
                {
                    if (destIsStateMachine)
                    {
                        transition = sourceState.AddTransition(destStateMachine);
                    }
                    else
                    {
                        transition = sourceState.AddTransition(destState);
                    }
                }

                if (transition == null)
                {
                    continue;
                }

                // 应用设置
                transition.hasExitTime = item.overrideHasExitTime ? item.hasExitTime : defaultHasExitTime;
                transition.exitTime = item.overrideExitTime ? item.exitTime : defaultExitTime;
                transition.hasFixedDuration = item.overrideHasFixedDuration ? item.hasFixedDuration : defaultHasFixedDuration;
                transition.duration = item.overrideDuration ? item.duration : defaultDuration;
                transition.offset = item.overrideOffset ? item.offset : defaultOffset;
                transition.canTransitionToSelf = item.overrideCanTransitionToSelf ? item.canTransitionToSelf : defaultCanTransitionToSelf;

                // 添加条目条件
                if (item.conditions != null)
                {
                    foreach (var cond in item.conditions)
                    {
                        if (string.IsNullOrEmpty(cond.parameterName)) continue;
                        AddConditionToTransition(transition, cond);
                    }
                }

                // 添加全局条件
                if (globalConditions != null)
                {
                    foreach (var globalCond in globalConditions)
                    {
                        if (string.IsNullOrEmpty(globalCond.parameterName)) continue;
                        
                        // 检查是否被条目条件覆盖
                        bool isOverridden = false;
                        if (item.conditions != null)
                        {
                            foreach (var itemCond in item.conditions)
                            {
                                if (itemCond.parameterName == globalCond.parameterName)
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
