using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MVA.Toolbox.AnimatorShared.Paths;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.AnimatorTransitionTool
{
    internal static class AnimatorTransitionModifyService
    {
        internal enum ModifyMode
        {
            FromStateTransitions,
            ToStateTransitions
        }

        internal enum SortMode
        {
            ArrangementOrder,
            NameNumberOrder
        }

        internal enum IncrementDirection
        {
            Increment,
            Decrement
        }

        internal struct ConditionSetting
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

        internal enum ConditionDeltaOperation
        {
            Append,
            AddUnique,
            Remove
        }

        internal struct ConditionDeltaSetting
        {
            public string parameterName;
            public AnimatorConditionMode mode;
            public float threshold;
            public ConditionDeltaOperation operation;
            public bool removeAllForParameter;
            public bool ignoreCondition;
        }

        private struct TransitionInfo
        {
            public AnimatorStateTransition transition;
            public AnimatorState sourceState;
        }

        internal static void Execute(
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
            if (controller == null || layer == null || string.IsNullOrEmpty(statePath))
            {
                return;
            }

            AnimatorStateMachine stateMachine = layer.stateMachine;
            if (stateMachine == null)
            {
                return;
            }

            List<TransitionInfo> transitionsToModifyInfo = CollectTransitionsToModify(stateMachine, modifyMode, statePath);
            if (transitionsToModifyInfo.Count == 0)
            {
                Debug.LogWarning("[AnimatorTransitionModifyService] 未找到符合条件的过渡可修改");
                return;
            }

            Undo.RecordObject(controller, "Animator Transition - Modify Transitions");

            if (!ValidateSortedIncrementNames(controller, transitionsToModifyInfo, modifyMode, statePath, conditionSettings))
            {
                return;
            }

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

            if (!ValidateDecrementThresholds(controller, transitionsToModifyInfo, conditionSettings))
            {
                return;
            }

            int[] transitionCounter = conditionSettings != null && conditionSettings.Count > 0
                ? new int[conditionSettings.Count]
                : System.Array.Empty<int>();

            for (int infoIndex = 0; infoIndex < transitionsToModifyInfo.Count; infoIndex++)
            {
                AnimatorStateTransition transition = transitionsToModifyInfo[infoIndex].transition;
                transition.hasExitTime = hasExitTimeValue;
                transition.exitTime = exitTimeValue;
                transition.hasFixedDuration = hasFixedDurationValue;
                transition.duration = durationValue;
                transition.offset = offsetValue;

                if (modifyConditions && conditionSettings != null)
                {
                    transition.conditions = new AnimatorCondition[0];

                    for (int conditionIndex = 0; conditionIndex < conditionSettings.Count; conditionIndex++)
                    {
                        ConditionSetting condSetting = conditionSettings[conditionIndex];
                        if (string.IsNullOrEmpty(condSetting.parameterName))
                        {
                            continue;
                        }

                        AnimatorControllerParameter parameter = controller.parameters.FirstOrDefault(p => p.name == condSetting.parameterName);
                        if (parameter == null)
                        {
                            continue;
                        }

                        float threshold = condSetting.threshold;
                        AnimatorConditionMode mode = condSetting.mode;

                        if (condSetting.enableIntAutoIncrement)
                        {
                            if (parameter.type == AnimatorControllerParameterType.Int)
                            {
                                threshold = condSetting.incrementDirection == IncrementDirection.Increment
                                    ? (int)condSetting.threshold + transitionCounter[conditionIndex] * condSetting.incrementStep
                                    : (int)condSetting.threshold - transitionCounter[conditionIndex] * condSetting.incrementStep;
                            }
                            else if (parameter.type == AnimatorControllerParameterType.Float)
                            {
                                threshold = condSetting.incrementDirection == IncrementDirection.Increment
                                    ? condSetting.threshold + transitionCounter[conditionIndex] * condSetting.floatIncrementStep
                                    : condSetting.threshold - transitionCounter[conditionIndex] * condSetting.floatIncrementStep;
                            }
                        }

                        transition.AddCondition(mode, threshold, condSetting.parameterName);
                    }

                    for (int conditionIndex = 0; conditionIndex < conditionSettings.Count; conditionIndex++)
                    {
                        if (conditionSettings[conditionIndex].enableIntAutoIncrement)
                        {
                            transitionCounter[conditionIndex]++;
                        }
                    }
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log($"[AnimatorTransitionModifyService] 成功修改了 {transitionsToModifyInfo.Count} 个过渡");
        }

        internal static void ExecuteConditionDelta(
            AnimatorController controller,
            AnimatorControllerLayer layer,
            ModifyMode modifyMode,
            string statePath,
            IReadOnlyList<ConditionDeltaSetting> conditionSettings)
        {
            if (controller == null || layer == null || string.IsNullOrEmpty(statePath))
            {
                return;
            }

            AnimatorStateMachine stateMachine = layer.stateMachine;
            if (stateMachine == null)
            {
                return;
            }

            List<TransitionInfo> transitionsToModifyInfo = CollectTransitionsToModify(stateMachine, modifyMode, statePath);
            if (transitionsToModifyInfo.Count == 0)
            {
                Debug.LogWarning("[AnimatorTransitionModifyService] 未找到符合条件的过渡可修改");
                return;
            }

            Undo.RecordObject(controller, "Animator Transition - Condition Delta");

            for (int infoIndex = 0; infoIndex < transitionsToModifyInfo.Count; infoIndex++)
            {
                AnimatorStateTransition transition = transitionsToModifyInfo[infoIndex].transition;
                if (transition == null || conditionSettings == null || conditionSettings.Count == 0)
                {
                    continue;
                }

                List<AnimatorCondition> conditions = transition.conditions != null
                    ? transition.conditions.ToList()
                    : new List<AnimatorCondition>();

                for (int settingIndex = 0; settingIndex < conditionSettings.Count; settingIndex++)
                {
                    ConditionDeltaSetting setting = conditionSettings[settingIndex];
                    if (string.IsNullOrEmpty(setting.parameterName))
                    {
                        continue;
                    }

                    AnimatorControllerParameter parameter = controller.parameters.FirstOrDefault(p => p != null && p.name == setting.parameterName);
                    if (parameter == null)
                    {
                        continue;
                    }

                    if (setting.operation == ConditionDeltaOperation.Remove)
                    {
                        if (setting.removeAllForParameter)
                        {
                            conditions.RemoveAll(c => c.parameter == setting.parameterName);
                        }
                        else
                        {
                            conditions.RemoveAll(c => c.parameter == setting.parameterName && c.mode == setting.mode && Mathf.Approximately(c.threshold, setting.threshold));
                        }
                    }
                    else
                    {
                        bool alreadyExists;
                        if (setting.operation == ConditionDeltaOperation.AddUnique && setting.ignoreCondition)
                        {
                            alreadyExists = conditions.Any(c => c.parameter == setting.parameterName);
                        }
                        else
                        {
                            alreadyExists = conditions.Any(c => c.parameter == setting.parameterName && c.mode == setting.mode && Mathf.Approximately(c.threshold, setting.threshold));
                        }

                        if (setting.operation == ConditionDeltaOperation.AddUnique && alreadyExists)
                        {
                            continue;
                        }

                        conditions.Add(new AnimatorCondition
                        {
                            mode = setting.mode,
                            parameter = setting.parameterName,
                            threshold = setting.threshold
                        });
                    }
                }

                transition.conditions = conditions.ToArray();
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log($"[AnimatorTransitionModifyService] 成功修改了 {transitionsToModifyInfo.Count} 个过渡");
        }

        private static List<TransitionInfo> CollectTransitionsToModify(AnimatorStateMachine stateMachine, ModifyMode modifyMode, string statePath)
        {
            List<TransitionInfo> transitionsToModifyInfo = new List<TransitionInfo>();

            if (modifyMode == ModifyMode.FromStateTransitions)
            {
                if (statePath == "Any State")
                {
                    foreach (AnimatorStateTransition transition in stateMachine.anyStateTransitions)
                    {
                        transitionsToModifyInfo.Add(new TransitionInfo
                        {
                            transition = transition,
                            sourceState = null
                        });
                    }
                }
                else
                {
                    AnimatorState state = FindStateByPath(stateMachine, statePath);
                    if (state == null)
                    {
                        return transitionsToModifyInfo;
                    }

                    foreach (AnimatorStateTransition transition in state.transitions)
                    {
                        transitionsToModifyInfo.Add(new TransitionInfo
                        {
                            transition = transition,
                            sourceState = state
                        });
                    }
                }
            }
            else
            {
                if (statePath == "Exit")
                {
                    FindTransitionsToExit(stateMachine, transitionsToModifyInfo);
                }
                else
                {
                    AnimatorState state = FindStateByPath(stateMachine, statePath);
                    if (state == null)
                    {
                        return transitionsToModifyInfo;
                    }

                    FindTransitionsToState(stateMachine, state, transitionsToModifyInfo);
                    foreach (AnimatorStateTransition transition in stateMachine.anyStateTransitions)
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

                if (statePath != "Exit")
                {
                    transitionsToModifyInfo.RemoveAll(info => info.transition.isExit);
                }
            }

            return transitionsToModifyInfo;
        }

        private static bool ValidateSortedIncrementNames(
            AnimatorController controller,
            List<TransitionInfo> transitionsToModifyInfo,
            ModifyMode modifyMode,
            string statePath,
            IReadOnlyList<ConditionSetting> conditionSettings)
        {
            if (conditionSettings == null)
            {
                return true;
            }

            for (int i = 0; i < conditionSettings.Count; i++)
            {
                ConditionSetting condSetting = conditionSettings[i];
                if (!condSetting.enableIntAutoIncrement || condSetting.sortMode != SortMode.NameNumberOrder)
                {
                    continue;
                }

                AnimatorControllerParameter param = controller.parameters.FirstOrDefault(p => p.name == condSetting.parameterName);
                if (param == null || (param.type != AnimatorControllerParameterType.Int && param.type != AnimatorControllerParameterType.Float))
                {
                    continue;
                }

                if (param.type == AnimatorControllerParameterType.Int && statePath != "Exit")
                {
                    for (int infoIndex = 0; infoIndex < transitionsToModifyInfo.Count; infoIndex++)
                    {
                        TransitionInfo info = transitionsToModifyInfo[infoIndex];
                        string nameToValidate = modifyMode == ModifyMode.FromStateTransitions
                            ? info.transition.destinationState != null ? info.transition.destinationState.name : string.Empty
                            : info.sourceState != null ? info.sourceState.name : string.Empty;

                        if (!IsValidStateName(nameToValidate))
                        {
                            EditorUtility.DisplayDialog(
                                "无效的状态名称",
                                $"状态 '{nameToValidate}' 不符合命名要求。\n\n请将状态命名为 '[任意名称] [序号]' 或 '[序号]'。",
                                "好的");
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private static bool ValidateDecrementThresholds(
            AnimatorController controller,
            List<TransitionInfo> transitionsToModifyInfo,
            IReadOnlyList<ConditionSetting> conditionSettings)
        {
            if (conditionSettings == null)
            {
                return true;
            }

            for (int i = 0; i < conditionSettings.Count; i++)
            {
                ConditionSetting condSetting = conditionSettings[i];
                if (!condSetting.enableIntAutoIncrement || condSetting.incrementDirection != IncrementDirection.Decrement)
                {
                    continue;
                }

                AnimatorControllerParameter param = controller.parameters.FirstOrDefault(p => p.name == condSetting.parameterName);
                if (param != null && param.type == AnimatorControllerParameterType.Int)
                {
                    int maxDecrement = transitionsToModifyInfo.Count - 1;
                    int totalDecrease = maxDecrement * condSetting.incrementStep;
                    int requiredMinThreshold = totalDecrease;
                    if ((int)condSetting.threshold < requiredMinThreshold)
                    {
                        EditorUtility.DisplayDialog(
                            "无效的起始值",
                            $"检测到有 {transitionsToModifyInfo.Count} 个有效的 Transition，幅度为 {condSetting.incrementStep}。\n在递减模式下，起始值至少应当为 {requiredMinThreshold}（起始值 - 总幅度不能低于 0）。",
                            "好的");
                        return false;
                    }
                }
            }

            return true;
        }

        private static void FindTransitionsToState(AnimatorStateMachine stateMachine, AnimatorState targetState, List<TransitionInfo> list)
        {
            foreach (ChildAnimatorState child in stateMachine.states)
            {
                foreach (AnimatorStateTransition transition in child.state.transitions)
                {
                    if (transition.destinationState == targetState)
                    {
                        list.Add(new TransitionInfo { transition = transition, sourceState = child.state });
                    }
                }
            }

            foreach (ChildAnimatorStateMachine sub in stateMachine.stateMachines)
            {
                FindTransitionsToState(sub.stateMachine, targetState, list);
            }
        }

        private static void FindTransitionsToExit(AnimatorStateMachine stateMachine, List<TransitionInfo> list)
        {
            foreach (ChildAnimatorState child in stateMachine.states)
            {
                foreach (AnimatorStateTransition transition in child.state.transitions)
                {
                    if (transition.isExit)
                    {
                        list.Add(new TransitionInfo { transition = transition, sourceState = child.state });
                    }
                }
            }

            foreach (ChildAnimatorStateMachine sub in stateMachine.stateMachines)
            {
                FindTransitionsToExit(sub.stateMachine, list);
            }
        }

        private static AnimatorState FindStateByPath(AnimatorStateMachine root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path))
            {
                return null;
            }

            string[] segments = AnimatorStatePathUtility.SplitPath(path);
            return FindStateRecursive(root, segments, 0);
        }

        private static AnimatorState FindStateRecursive(AnimatorStateMachine current, string[] segments, int index)
        {
            if (current == null || index >= segments.Length)
            {
                return null;
            }

            string segmentName = segments[index];
            bool isLast = index == segments.Length - 1;
            if (isLast)
            {
                foreach (ChildAnimatorState childState in current.states)
                {
                    if (childState.state != null && childState.state.name == segmentName)
                    {
                        return childState.state;
                    }
                }
            }
            else
            {
                foreach (ChildAnimatorStateMachine childMachine in current.stateMachines)
                {
                    if (childMachine.stateMachine != null && childMachine.stateMachine.name == segmentName)
                    {
                        return FindStateRecursive(childMachine.stateMachine, segments, index + 1);
                    }
                }
            }

            return null;
        }
        private static bool IsValidStateName(string stateName)
        {
            return Regex.IsMatch(stateName ?? string.Empty, @"^.*\s-?\d+$") || Regex.IsMatch(stateName ?? string.Empty, @"^-?\d+$");
        }

        private static int ParseStateNameNumber(string stateName)
        {
            Match match = Regex.Match(stateName ?? string.Empty, @"-?\d+$");
            if (match.Success)
            {
                return int.Parse(match.Value);
            }

            return 0;
        }
    }
}
