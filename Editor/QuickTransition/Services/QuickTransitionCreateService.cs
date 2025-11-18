using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.QuickTransition.Services
{
    /// <summary>
    /// 提供“创建过渡”模式下的实际 AnimatorController 操作逻辑。
    /// </summary>
    internal static class QuickTransitionCreateService
    {
        internal struct TransitionSettings
        {
            public bool hasExitTimeOverride;
            public bool hasExitTime;
            public bool exitTimeOverride;
            public float exitTime;
            public bool hasFixedDurationOverride;
            public bool hasFixedDuration;
            public bool durationOverride;
            public float duration;
            public bool offsetOverride;
            public float offset;
            public bool canTransitionToSelfOverride;
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

        /// <summary>
        /// 根据窗口提供的数据，在指定层上批量创建过渡。
        /// </summary>
        internal static void CreateTransitions(
            AnimatorController controller,
            AnimatorControllerLayer layer,
            string sourceStateName,
            string destinationStateName,
            bool defaultHasExitTime,
            float defaultExitTime,
            bool defaultHasFixedDuration,
            float defaultDuration,
            float defaultOffset,
            bool defaultCanTransitionToSelf,
            IReadOnlyList<TransitionSettings> transitions)
        {
            if (controller == null || layer == null || transitions == null || transitions.Count == 0)
            {
                return;
            }

            var stateMachine = layer.stateMachine;
            if (stateMachine == null)
            {
                return;
            }

            bool toExit = destinationStateName == "Exit";
            AnimatorState destinationState = null;

            // 查找目标状态（使用路径匹配，支持区分根/子状态机同名状态）。当目标为 Exit 时，不需要具体状态。
            if (!toExit)
            {
                destinationState = FindStateByPath(stateMachine, destinationStateName);
                if (destinationState == null)
                {
                    Debug.LogWarning($"[QuickTransition] 未找到目标状态: {destinationStateName}");
                    return;
                }
            }

            bool useAnyStateAsSource = sourceStateName == "Any State";
            AnimatorState sourceState = null;

            if (!useAnyStateAsSource)
            {
                sourceState = FindStateByPath(stateMachine, sourceStateName);
                if (sourceState == null)
                {
                    Debug.LogWarning($"[QuickTransition] 未找到源状态: {sourceStateName}");
                    return;
                }
            }

            Undo.RecordObject(controller, "Quick Transition - Create Transitions");

            int createdCount = 0;
            foreach (var settings in transitions)
            {
                AnimatorStateTransition transition = null;

                if (toExit)
                {
                    // 目标为 Exit：只能从具体状态到 Exit，不能从 Any State 到 Exit
                    if (useAnyStateAsSource)
                    {
                        Debug.LogWarning("[QuickTransition] 不支持从 Any State 直接创建到 Exit 的过渡。");
                        return;
                    }

                    if (sourceState != null)
                    {
                        // 从当前源状态创建到其所属状态机 Exit 的过渡
                        transition = sourceState.AddExitTransition();
                    }
                }
                else
                {
                    if (useAnyStateAsSource)
                    {
                        transition = stateMachine.AddAnyStateTransition(destinationState);
                    }
                    else
                    {
                        transition = sourceState.AddTransition(destinationState);
                    }
                }

                if (transition == null)
                {
                    continue;
                }

                // 组合默认值与覆盖值
                bool hasExitTime = settings.hasExitTimeOverride ? settings.hasExitTime : defaultHasExitTime;
                float exitTime = settings.exitTimeOverride ? settings.exitTime : defaultExitTime;
                bool hasFixedDuration = settings.hasFixedDurationOverride ? settings.hasFixedDuration : defaultHasFixedDuration;
                float duration = settings.durationOverride ? settings.duration : defaultDuration;
                float offset = settings.offsetOverride ? settings.offset : defaultOffset;
                bool canTransitionToSelf = settings.canTransitionToSelfOverride ? settings.canTransitionToSelf : defaultCanTransitionToSelf;

                transition.hasExitTime = hasExitTime;
                transition.exitTime = exitTime;
                transition.hasFixedDuration = hasFixedDuration;
                transition.duration = duration;
                transition.offset = offset;
                transition.canTransitionToSelf = canTransitionToSelf;

                // 条件
                if (settings.conditions != null && settings.conditions.Count > 0)
                {
                    var condList = new List<AnimatorCondition>();

                    foreach (var cond in settings.conditions)
                    {
                        if (string.IsNullOrEmpty(cond.parameterName))
                        {
                            continue;
                        }

                        float threshold = 0f;
                        AnimatorConditionMode mode = cond.mode;

                        switch (cond.parameterType)
                        {
                            case AnimatorControllerParameterType.Bool:
                                mode = cond.boolValue ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot;
                                threshold = 0f;
                                break;
                            case AnimatorControllerParameterType.Float:
                                threshold = cond.floatValue;
                                break;
                            case AnimatorControllerParameterType.Int:
                                threshold = cond.intValue;
                                break;
                            default:
                                continue;
                        }

                        condList.Add(new AnimatorCondition
                        {
                            parameter = cond.parameterName,
                            mode = mode,
                            threshold = threshold
                        });
                    }

                    transition.conditions = condList.ToArray();
                }

                createdCount++;
            }

            if (createdCount > 0)
            {
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
                Debug.Log($"[QuickTransition] 已创建 {createdCount} 个过渡");
            }
        }

        private static AnimatorState FindStateByPath(AnimatorStateMachine root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path))
            {
                return null;
            }

            var segments = path.Split('/');
            return FindStateByPathRecursive(root, segments, 0);
        }

        private static AnimatorState FindStateByPathRecursive(AnimatorStateMachine current, string[] segments, int index)
        {
            if (current == null || segments == null || index >= segments.Length)
            {
                return null;
            }

            if (index == segments.Length - 1)
            {
                string stateName = segments[index];
                foreach (var child in current.states)
                {
                    if (child.state != null && child.state.name == stateName)
                    {
                        return child.state;
                    }
                }
                return null;
            }
            else
            {
                string subName = segments[index];
                foreach (var sub in current.stateMachines)
                {
                    if (sub.stateMachine != null && sub.stateMachine.name == subName)
                    {
                        return FindStateByPathRecursive(sub.stateMachine, segments, index + 1);
                    }
                }
            }

            return null;
        }

    }
}
