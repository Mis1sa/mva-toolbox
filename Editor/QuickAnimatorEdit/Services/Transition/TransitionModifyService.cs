using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using MVA.Toolbox.QuickAnimatorEdit.Services.Shared;

namespace MVA.Toolbox.QuickAnimatorEdit.Services.Transition
{
    /// <summary>
    /// 过渡批量修改服务
    /// 批量修改指定状态的入站或出站过渡属性与条件
    /// </summary>
    public static class TransitionModifyService
    {
        /// <summary>
        /// 修改模式
        /// </summary>
        public enum ModifyMode
        {
            FromStateTransitions,  // 从指定状态出发的过渡
            ToStateTransitions     // 到达指定状态的过渡
        }

        /// <summary>
        /// 排序模式
        /// </summary>
        public enum SortMode
        {
            ArrangementOrder,  // 按连接顺序
            NameNumberOrder    // 按状态名称数字排序
        }

        /// <summary>
        /// 递增/递减方向
        /// </summary>
        public enum IncrementDirection
        {
            Increment,
            Decrement
        }

        /// <summary>
        /// 条件设置
        /// </summary>
        public struct ConditionSetting
        {
            public string parameterName;
            public AnimatorConditionMode mode;
            public float threshold;

            public bool enableIntAutoIncrement;
            public IncrementDirection incrementDirection;
            public SortMode sortMode;
            public int incrementStep;
            public float floatIncrementStep;
        }

        private struct TransitionInfo
        {
            public AnimatorStateTransition transition;
            public AnimatorState sourceState; // From 模式时为源状态，To 模式时为“来源状态”（用于按名称排序）
        }

        /// <summary>
        /// 执行批量修改
        /// </summary>
        public static void Execute(
            AnimatorController controller,
            AnimatorControllerLayer layer,
            ModifyMode modifyMode,
            string statePath,
            bool hasExitTimeValue,
            float exitTimeValue,
            bool hasFixedDurationValue,
            float durationValue,
            float offsetValue,
            bool modifyConditions,
            IReadOnlyList<ConditionSetting> conditionSettings)
        {
            if (controller == null || layer == null) return;
            if (string.IsNullOrEmpty(statePath)) return;

            var stateMachine = layer.stateMachine;
            if (stateMachine == null) return;

            // 收集需要修改的过渡
            var transitionsToModifyInfo = new List<TransitionInfo>();

            if (modifyMode == ModifyMode.FromStateTransitions)
            {
                // 由选择的状态出发
                if (statePath == "Any State")
                {
                    foreach (var t in stateMachine.anyStateTransitions)
                    {
                        transitionsToModifyInfo.Add(new TransitionInfo
                        {
                            transition = t,
                            sourceState = null
                        });
                    }
                }
                else
                {
                    var state = AnimatorPathUtility.FindStateByPath(stateMachine, statePath);
                    if (state == null) return;

                    foreach (var transition in state.transitions)
                    {
                        transitionsToModifyInfo.Add(new TransitionInfo
                        {
                            transition = transition,
                            sourceState = state
                        });
                    }
                }
            }
            else // ToStateTransitions
            {
                if (statePath == "Exit")
                {
                    FindTransitionsToExit(stateMachine, transitionsToModifyInfo);
                }
                else
                {
                    var state = AnimatorPathUtility.FindStateByPath(stateMachine, statePath);
                    if (state == null) return;

                    // 从所有状态递归查找指向该状态的过渡
                    FindTransitionsToState(stateMachine, state, transitionsToModifyInfo);

                    // Any State 到该状态
                    foreach (var transition in stateMachine.anyStateTransitions)
                    {
                        if (transition.destinationState == state)
                        {
                            transitionsToModifyInfo.Add(new TransitionInfo
                            {
                                transition = transition,
                                sourceState = null
                            });
                        }
                    }
                }

                // 只有在不是处理 Exit 节点时才移除 isExit 的过渡
                if (statePath != "Exit")
                {
                    transitionsToModifyInfo.RemoveAll(info => info.transition.isExit);
                }
            }

            if (transitionsToModifyInfo.Count == 0)
            {
                Debug.LogWarning("[TransitionModifyService] 未找到符合条件的过渡可修改");
                return;
            }

            Undo.RecordObject(controller, "Quick Transition - Modify Transitions");

            // 预处理带递增的条件：需要按名称排序时先验证命名
            if (conditionSettings != null)
            {
                for (int i = 0; i < conditionSettings.Count; i++)
                {
                    var condSetting = conditionSettings[i];
                    if (!condSetting.enableIntAutoIncrement || condSetting.sortMode != SortMode.NameNumberOrder)
                    {
                        continue;
                    }

                    var param = controller.parameters.FirstOrDefault(p => p.name == condSetting.parameterName);
                    if (param == null || (param.type != AnimatorControllerParameterType.Int && param.type != AnimatorControllerParameterType.Float))
                    {
                        continue;
                    }

                    if (param.type == AnimatorControllerParameterType.Int)
                    {
                        if (statePath == "Exit")
                        {
                            // 改为连接顺序
                            // 注意：这里我们修改的是传入的结构体副本，不影响外部，但在本方法内有效
                            // 由于结构体是值类型，直接修改副本无效，需要替换列表中的元素或者在循环中使用 ref (但 IReadOnlyList 不支持)
                            // 这里我们假设调用者传入的数据是正确配置的，或者我们在下面排序时处理
                            // 实际上，如果 statePath == "Exit"，我们应该在排序逻辑中忽略 NameNumberOrder
                        }
                        else
                        {
                            foreach (var info in transitionsToModifyInfo)
                            {
                                string nameToValidate;
                                if (modifyMode == ModifyMode.FromStateTransitions)
                                {
                                    nameToValidate = info.transition.destinationState != null ? info.transition.destinationState.name : string.Empty;
                                }
                                else
                                {
                                    nameToValidate = info.sourceState != null ? info.sourceState.name : string.Empty;
                                }

                                if (!IsValidStateName(nameToValidate))
                                {
                                    EditorUtility.DisplayDialog(
                                        "无效的状态名称",
                                        $"状态 '{nameToValidate}' 不符合命名要求。\n\n请将状态命名为 '[任意名称] [序号]' 或 '[序号]'。",
                                        "好的");
                                    return;
                                }
                            }
                        }
                    }
                }
            }

            // 排序（仅当某个条件要求按名称排序，且当前目标不是 Exit 时才生效）
            if (conditionSettings != null && statePath != "Exit" && conditionSettings.Any(c => c.enableIntAutoIncrement && c.sortMode == SortMode.NameNumberOrder))
            {
                if (modifyMode == ModifyMode.FromStateTransitions)
                {
                    transitionsToModifyInfo = transitionsToModifyInfo
                        .OrderBy(info => info.transition.destinationState != null ? ParseStateNameNumber(info.transition.destinationState.name) : 0)
                        .ToList();
                }
                else
                {
                    transitionsToModifyInfo = transitionsToModifyInfo
                        .OrderBy(info => info.sourceState != null ? ParseStateNameNumber(info.sourceState.name) : 0)
                        .ToList();
                }
            }

            // 对于递减模式的校验
            if (conditionSettings != null)
            {
                for (int i = 0; i < conditionSettings.Count; i++)
                {
                    var condSetting = conditionSettings[i];
                    if (!condSetting.enableIntAutoIncrement || condSetting.incrementDirection != IncrementDirection.Decrement)
                    {
                        continue;
                    }

                    var param = controller.parameters.FirstOrDefault(p => p.name == condSetting.parameterName);
                    if (param != null && param.type == AnimatorControllerParameterType.Int)
                    {
                        int maxDecrement = transitionsToModifyInfo.Count - 1;
                        int totalDecrease = maxDecrement * condSetting.incrementStep;
                        int requiredMinThreshold = totalDecrease;

                        if ((int)condSetting.threshold < requiredMinThreshold)
                        {
                            EditorUtility.DisplayDialog(
                                "无效的起始值",
                                $"检测到有 {transitionsToModifyInfo.Count} 个有效的 Transition，幅度为 {condSetting.incrementStep}。\n" +
                                $"在递减模式下，起始值至少应当为 {requiredMinThreshold}（起始值 - 总幅度不能低于 0）。",
                                "好的");
                            return;
                        }
                    }
                }
            }

            // 应用属性和条件
            var transitionCounter = conditionSettings != null && conditionSettings.Count > 0
                ? new int[conditionSettings.Count]
                : System.Array.Empty<int>();

            foreach (var info in transitionsToModifyInfo)
            {
                var transition = info.transition;
                transition.hasExitTime = hasExitTimeValue;
                transition.exitTime = exitTimeValue;
                transition.hasFixedDuration = hasFixedDurationValue;
                transition.duration = durationValue;
                transition.offset = offsetValue;

                if (modifyConditions && conditionSettings != null)
                {
                    transition.conditions = new AnimatorCondition[0];

                    for (int i = 0; i < conditionSettings.Count; i++)
                    {
                        var condSetting = conditionSettings[i];
                        if (string.IsNullOrEmpty(condSetting.parameterName)) continue;

                        var parameter = controller.parameters.FirstOrDefault(p => p.name == condSetting.parameterName);
                        if (parameter == null) continue;

                        float threshold = condSetting.threshold;
                        var mode = condSetting.mode;

                        if (condSetting.enableIntAutoIncrement)
                        {
                            if (parameter.type == AnimatorControllerParameterType.Int)
                            {
                                if (condSetting.incrementDirection == IncrementDirection.Increment)
                                {
                                    threshold = (int)condSetting.threshold + transitionCounter[i] * condSetting.incrementStep;
                                }
                                else
                                {
                                    threshold = (int)condSetting.threshold - transitionCounter[i] * condSetting.incrementStep;
                                }
                            }
                            else if (parameter.type == AnimatorControllerParameterType.Float)
                            {
                                if (condSetting.incrementDirection == IncrementDirection.Increment)
                                {
                                    threshold = condSetting.threshold + transitionCounter[i] * condSetting.floatIncrementStep;
                                }
                                else
                                {
                                    threshold = condSetting.threshold - transitionCounter[i] * condSetting.floatIncrementStep;
                                }
                            }
                        }

                        transition.AddCondition(mode, threshold, condSetting.parameterName);
                    }

                    // 更新计数器
                    for (int i = 0; i < conditionSettings.Count; i++)
                    {
                        if (conditionSettings[i].enableIntAutoIncrement)
                        {
                            transitionCounter[i]++;
                        }
                    }
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log($"[TransitionModifyService] 成功修改了 {transitionsToModifyInfo.Count} 个过渡");
        }

        private static void FindTransitionsToState(AnimatorStateMachine stateMachine, AnimatorState targetState, List<TransitionInfo> list)
        {
            foreach (var child in stateMachine.states)
            {
                foreach (var transition in child.state.transitions)
                {
                    if (transition.destinationState == targetState)
                    {
                        list.Add(new TransitionInfo { transition = transition, sourceState = child.state });
                    }
                }
            }

            foreach (var sub in stateMachine.stateMachines)
            {
                FindTransitionsToState(sub.stateMachine, targetState, list);
            }
        }

        private static void FindTransitionsToExit(AnimatorStateMachine stateMachine, List<TransitionInfo> list)
        {
            foreach (var child in stateMachine.states)
            {
                foreach (var transition in child.state.transitions)
                {
                    if (transition.isExit)
                    {
                        list.Add(new TransitionInfo { transition = transition, sourceState = child.state });
                    }
                }
            }

            foreach (var sub in stateMachine.stateMachines)
            {
                FindTransitionsToExit(sub.stateMachine, list);
            }
        }

        private static bool IsValidStateName(string stateName)
        {
            return Regex.IsMatch(stateName ?? string.Empty, @"^.*\s-?\d+$") || Regex.IsMatch(stateName ?? string.Empty, @"^-?\d+$");
        }

        private static int ParseStateNameNumber(string stateName)
        {
            var match = Regex.Match(stateName ?? string.Empty, @"-?\d+$");
            if (match.Success)
            {
                return int.Parse(match.Value);
            }

            return 0;
        }
    }
}
