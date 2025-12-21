using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace MVA.Toolbox.QuickAnimatorEdit.Services.Parameter
{
    /// <summary>
    /// 参数检查服务
    /// 检查缺失参数引用、无用参数、参数类型不匹配等问题
    /// </summary>
    public static class ParameterCheckService
    {
        public enum IssueType
        {
            MissingReference,    // 缺失参数引用 (使用了不存在的参数)
            UnusedParameter,     // 无用参数 (定义了但未被使用)
            TypeMismatch         // 类型不匹配 (暂未实现完全检测)
        }

        public class ParameterReferenceContext
        {
            public string Description;
            public Action<string> FixAction; // 用于修复引用的回调（传入新的参数名）
            public AnimatorTransitionBase Transition;
            public int ConditionIndex = -1;
            public AnimatorConditionMode? ConditionMode;
            public float ConditionThreshold;
        }

        public class ParameterIssue
        {
            public IssueType Type;
            public string ParameterName;
            public AnimatorControllerParameterType? ExpectedType; // 根据上下文推断的期望类型
            public AnimatorControllerParameterType? ActualType;   // 实际定义的类型
            public List<ParameterReferenceContext> References = new List<ParameterReferenceContext>();
        }

        private delegate void ParameterReferenceRegister(
            string paramName,
            string description,
            AnimatorControllerParameterType? typeHint,
            Action<string> fixAction,
            AnimatorTransitionBase transition = null,
            int conditionIndex = -1,
            AnimatorConditionMode? conditionMode = null,
            float conditionThreshold = 0f);

        public class CheckResult
        {
            public List<ParameterIssue> Issues = new List<ParameterIssue>();
            public bool HasIssues => Issues.Count > 0;
        }

        /// <summary>
        /// 执行参数检查
        /// </summary>
        public static CheckResult Execute(AnimatorController controller)
        {
            var result = new CheckResult();
            if (controller == null) return result;

            // 1. 获取所有已定义的参数
            var definedParameters = controller.parameters.ToDictionary(p => p.name, p => p);
            var usedParameterNames = new HashSet<string>();
            var typeHintMap = new Dictionary<string, HashSet<AnimatorControllerParameterType>>();
            var referenceMap = new Dictionary<string, List<ParameterReferenceContext>>(StringComparer.Ordinal);

            // 辅助方法：注册引用
            void RegisterReference(
                string paramName,
                string description,
                AnimatorControllerParameterType? typeHint,
                Action<string> fixAction,
                AnimatorTransitionBase transition = null,
                int conditionIndex = -1,
                AnimatorConditionMode? conditionMode = null,
                float conditionThreshold = 0f)
            {
                if (string.IsNullOrEmpty(paramName)) return;

                usedParameterNames.Add(paramName);

                var context = new ParameterReferenceContext
                {
                    Description = description,
                    FixAction = fixAction,
                    Transition = transition,
                    ConditionIndex = conditionIndex,
                    ConditionMode = conditionMode,
                    ConditionThreshold = conditionThreshold
                };

                if (!referenceMap.TryGetValue(paramName, out var referenceList))
                {
                    referenceList = new List<ParameterReferenceContext>();
                    referenceMap[paramName] = referenceList;
                }
                referenceList.Add(context);

                if (typeHint.HasValue)
                {
                    if (!typeHintMap.TryGetValue(paramName, out var hints))
                    {
                        hints = new HashSet<AnimatorControllerParameterType>();
                        typeHintMap[paramName] = hints;
                    }
                    hints.Add(typeHint.Value);
                }

                // 检查是否缺失
                if (!definedParameters.ContainsKey(paramName))
                {
                    var issue = result.Issues.FirstOrDefault(i => i.Type == IssueType.MissingReference && i.ParameterName == paramName);
                    if (issue == null)
                    {
                        issue = new ParameterIssue
                        {
                            Type = IssueType.MissingReference,
                            ParameterName = paramName,
                            ExpectedType = typeHint
                        };
                        result.Issues.Add(issue);
                    }
                    
                    // 如果之前的推断类型为空，尝试补充
                    if (!issue.ExpectedType.HasValue && typeHint.HasValue)
                    {
                        issue.ExpectedType = typeHint;
                    }

                    issue.References.Add(context);
                }
            }

            // 2. 遍历控制器收集引用
            for (int i = 0; i < controller.layers.Length; i++)
            {
                var layer = controller.layers[i];
                AnalyzeStateMachine(layer.stateMachine, $"Layer {i}", RegisterReference);
            }

            // 3. 检查无用参数
            foreach (var param in controller.parameters)
            {
                if (!usedParameterNames.Contains(param.name))
                {
                    result.Issues.Add(new ParameterIssue
                    {
                        Type = IssueType.UnusedParameter,
                        ParameterName = param.name,
                        ActualType = param.type
                    });
                }
            }

            // 4. 检查类型不匹配
            foreach (var param in controller.parameters)
            {
                if (!typeHintMap.TryGetValue(param.name, out var hints) || hints.Count == 0)
                    continue;

                if (hints.Contains(param.type))
                    continue;

                var issue = new ParameterIssue
                {
                    Type = IssueType.TypeMismatch,
                    ParameterName = param.name,
                    ExpectedType = hints.First(),
                    ActualType = param.type
                };

                if (referenceMap.TryGetValue(param.name, out var references))
                {
                    issue.References.AddRange(references);
                }

                result.Issues.Add(issue);
            }

            // 排序：缺失引用在前，无用参数在后
            result.Issues = result.Issues
                .OrderBy(i => i.Type)
                .ThenBy(i => i.ParameterName)
                .ToList();

            return result;
        }

        private static void AnalyzeStateMachine(AnimatorStateMachine stateMachine, string path, ParameterReferenceRegister register)
        {
            if (stateMachine == null) return;

            // States
            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                if (state == null) continue;
                string statePath = $"{path}/{state.name}";

                AnalyzeState(state, statePath, register);
                
                // Transitions
                foreach (var t in state.transitions)
                {
                    AnalyzeTransition(t, $"{statePath} -> {(t.destinationState?.name ?? "Exit")}", register);
                }
            }

            // AnyState Transitions
            foreach (var t in stateMachine.anyStateTransitions)
            {
                AnalyzeTransition(t, $"{path}/AnyState -> {(t.destinationState?.name ?? "Exit")}", register);
            }

            // Entry Transitions
            foreach (var t in stateMachine.entryTransitions)
            {
                AnalyzeTransition(t, $"{path}/Entry -> {(t.destinationState?.name ?? "Exit")}", register);
            }

            // Sub State Machines
            foreach (var sub in stateMachine.stateMachines)
            {
                AnalyzeStateMachine(sub.stateMachine, $"{path}/{sub.stateMachine.name}", register);
            }
        }

        private static void AnalyzeState(AnimatorState state, string path, ParameterReferenceRegister register)
        {
            if (state.speedParameterActive)
            {
                register(state.speedParameter, $"{path} (Speed)", AnimatorControllerParameterType.Float, newName =>
                {
                    state.speedParameter = newName;
                    EditorUtility.SetDirty(state);
                });
            }
            if (state.mirrorParameterActive)
            {
                register(state.mirrorParameter, $"{path} (Mirror)", AnimatorControllerParameterType.Bool, newName =>
                {
                    state.mirrorParameter = newName;
                    EditorUtility.SetDirty(state);
                });
            }
            if (state.cycleOffsetParameterActive)
            {
                register(state.cycleOffsetParameter, $"{path} (Cycle Offset)", AnimatorControllerParameterType.Float, newName =>
                {
                    state.cycleOffsetParameter = newName;
                    EditorUtility.SetDirty(state);
                });
            }
            if (state.timeParameterActive)
            {
                register(state.timeParameter, $"{path} (Time)", AnimatorControllerParameterType.Float, newName =>
                {
                    state.timeParameter = newName;
                    EditorUtility.SetDirty(state);
                });
            }

            if (state.motion is UnityEditor.Animations.BlendTree bt)
            {
                AnalyzeBlendTree(bt, path, register);
            }

            foreach (var behaviour in state.behaviours)
            {
                AnalyzeBehaviour(behaviour, path, register);
            }
        }

        private static void AnalyzeBlendTree(UnityEditor.Animations.BlendTree bt, string path, ParameterReferenceRegister register)
        {
            if (bt == null) return;
            string btPath = $"{path}/{bt.name}";

            if (bt.blendType != BlendTreeType.Simple1D)
            {
                // 2D Blend Trees use X and Y
                if (bt.blendType != BlendTreeType.Direct)
                {
                    register(bt.blendParameter, $"{btPath} (Blend X)", AnimatorControllerParameterType.Float, newName =>
                    {
                        bt.blendParameter = newName;
                        EditorUtility.SetDirty(bt);
                    });
                    register(bt.blendParameterY, $"{btPath} (Blend Y)", AnimatorControllerParameterType.Float, newName =>
                    {
                        bt.blendParameterY = newName;
                        EditorUtility.SetDirty(bt);
                    });
                }
            }
            else
            {
                // 1D
                register(bt.blendParameter, $"{btPath} (Blend)", AnimatorControllerParameterType.Float, newName =>
                {
                    bt.blendParameter = newName;
                    EditorUtility.SetDirty(bt);
                });
            }

            // Recursive children
            foreach (var child in bt.children)
            {
                if (child.motion is UnityEditor.Animations.BlendTree childBt)
                {
                    AnalyzeBlendTree(childBt, btPath, register);
                }
                
                // Direct BlendTree children parameters
                if (bt.blendType == BlendTreeType.Direct)
                {
                    register(child.directBlendParameter, $"{btPath} (Direct Child)", AnimatorControllerParameterType.Float, newName =>
                    {
                        // Direct blend parameter needs to be updated in the ChildMotion struct array
                        // Note: Modifying child struct in array requires re-assigning the array or finding the index.
                        // Here we use a closure capturing the BlendTree and child index logic would be complex.
                        // Simplified approach: Re-fetch children, update, set back.
                        var children = bt.children;
                        for(int i=0; i<children.Length; i++)
                        {
                            if (children[i].directBlendParameter == child.directBlendParameter && children[i].motion == child.motion) // Weak identification
                            {
                                var c = children[i];
                                c.directBlendParameter = newName;
                                children[i] = c;
                                bt.children = children; // Re-assign
                                EditorUtility.SetDirty(bt);
                                break;
                            }
                        }
                    });
                }
            }
        }

        private static void AnalyzeTransition(AnimatorTransitionBase transition, string path, ParameterReferenceRegister register)
        {
            var conditions = transition.conditions;
            for (int i = 0; i < conditions.Length; i++)
            {
                var cond = conditions[i];
                int index = i; // Capture for closure
                AnimatorControllerParameterType? typeHint = cond.mode switch
                {
                    AnimatorConditionMode.If => AnimatorControllerParameterType.Bool,
                    AnimatorConditionMode.IfNot => AnimatorControllerParameterType.Bool,
                    AnimatorConditionMode.Equals => AnimatorControllerParameterType.Int,
                    AnimatorConditionMode.NotEqual => AnimatorControllerParameterType.Int,
                    _ => null
                };

                register(cond.parameter, $"{path} (Condition {index})", typeHint, newName =>
                {
                    // Need to re-fetch conditions array, modify, and set back
                    var currentConditions = transition.conditions;
                    if (index < currentConditions.Length)
                    {
                        currentConditions[index].parameter = newName;
                        transition.conditions = currentConditions;
                        EditorUtility.SetDirty(transition);
                    }
                },
                transition,
                index,
                cond.mode,
                cond.threshold);
            }
        }

        private static void AnalyzeBehaviour(StateMachineBehaviour behaviour, string path, ParameterReferenceRegister register)
        {
            if (behaviour == null) return;

            // Support VRCAvatarParameterDriver
            if (behaviour.GetType().Name.Contains("VRCAvatarParameterDriver"))
            {
                var so = new SerializedObject(behaviour);
                var paramsProp = so.FindProperty("parameters");
                if (paramsProp != null)
                {
                    for (int i = 0; i < paramsProp.arraySize; i++)
                    {
                        int index = i;
                        var elem = paramsProp.GetArrayElementAtIndex(i);
                        var nameProp = elem.FindPropertyRelative("name");
                        if (nameProp != null)
                        {
                            register(nameProp.stringValue, $"{path} (Driver {index})", null, newName =>
                            {
                                var freshSo = new SerializedObject(behaviour);
                                var freshParams = freshSo.FindProperty("parameters");
                                if (index < freshParams.arraySize)
                                {
                                    var freshElem = freshParams.GetArrayElementAtIndex(index);
                                    var freshName = freshElem.FindPropertyRelative("name");
                                    freshName.stringValue = newName;
                                    freshSo.ApplyModifiedProperties();
                                }
                            });
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 修复缺失参数引用
        /// </summary>
        public static bool FixMissingReference(
            AnimatorController controller,
            ParameterIssue issue,
            string fixOption,
            string targetParameterName = null)
        {
            if (issue == null || issue.Type != IssueType.MissingReference) return false;

            if (fixOption == "UseExisting")
            {
                if (string.IsNullOrEmpty(targetParameterName)) return false;
                foreach (var refer in issue.References)
                {
                    refer.FixAction?.Invoke(targetParameterName);
                }
                return true;
            }
            else if (fixOption == "CreateNew")
            {
                // Create parameter
                var type = issue.ExpectedType ?? AnimatorControllerParameterType.Float;
                var newParam = new AnimatorControllerParameter { name = issue.ParameterName, type = type };
                controller.AddParameter(newParam);
                // No need to update references since they already point to this name
                return true;
            }
            else if (fixOption == "Remove")
            {
                // Set to empty string usually removes/invalidates the reference
                foreach (var refer in issue.References)
                {
                    refer.FixAction?.Invoke(string.Empty);
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// 移除无用参数
        /// </summary>
        public static bool RemoveUnusedParameter(AnimatorController controller, string parameterName)
        {
            if (controller == null || string.IsNullOrEmpty(parameterName)) return false;

            for (int i = 0; i < controller.parameters.Length; i++)
            {
                if (controller.parameters[i].name == parameterName)
                {
                    controller.RemoveParameter(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 修复参数类型不匹配：同步参数类型并调整条件
        /// </summary>
        public static bool FixTypeMismatch(
            AnimatorController controller,
            ParameterIssue issue,
            AnimatorControllerParameterType targetType)
        {
            if (controller == null || issue == null || issue.Type != IssueType.TypeMismatch)
                return false;

            bool updatedParameter = UpdateParameterType(controller, issue.ParameterName, targetType);

            foreach (var reference in issue.References)
            {
                if (reference?.Transition == null || reference.ConditionIndex < 0)
                    continue;

                AdjustConditionForType(reference.Transition, reference.ConditionIndex, targetType);
            }

            return updatedParameter;
        }

        private static bool UpdateParameterType(AnimatorController controller, string parameterName, AnimatorControllerParameterType targetType)
        {
            var parameters = controller.parameters;
            bool changed = false;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == parameterName)
                {
                    if (parameters[i].type != targetType)
                    {
                        parameters[i].type = targetType;
                        changed = true;
                    }
                    break;
                }
            }

            if (changed)
            {
                controller.parameters = parameters;
                EditorUtility.SetDirty(controller);
            }

            return changed;
        }

        private static void AdjustConditionForType(AnimatorTransitionBase transition, int conditionIndex, AnimatorControllerParameterType targetType)
        {
            if (transition == null) return;
            var conditions = transition.conditions;
            if (conditionIndex < 0 || conditionIndex >= conditions.Length) return;

            var cond = conditions[conditionIndex];
            switch (targetType)
            {
                case AnimatorControllerParameterType.Bool:
                case AnimatorControllerParameterType.Trigger:
                    cond.mode = cond.mode switch
                    {
                        AnimatorConditionMode.IfNot => AnimatorConditionMode.IfNot,
                        AnimatorConditionMode.Less => AnimatorConditionMode.IfNot,
                        AnimatorConditionMode.NotEqual => AnimatorConditionMode.IfNot,
                        _ => AnimatorConditionMode.If
                    };
                    cond.threshold = 0f;
                    break;
                case AnimatorControllerParameterType.Float:
                    cond.mode = cond.mode switch
                    {
                        AnimatorConditionMode.Equals => AnimatorConditionMode.Greater,
                        AnimatorConditionMode.NotEqual => AnimatorConditionMode.Less,
                        AnimatorConditionMode.If => AnimatorConditionMode.Greater,
                        AnimatorConditionMode.IfNot => AnimatorConditionMode.Less,
                        _ => cond.mode
                    };
                    if (cond.mode == AnimatorConditionMode.Greater)
                    {
                        cond.threshold = Mathf.Max(cond.threshold, 0.5f);
                    }
                    else if (cond.mode == AnimatorConditionMode.Less)
                    {
                        cond.threshold = cond.threshold <= 0f ? 0f : Mathf.Min(cond.threshold, 0.5f);
                    }
                    break;
                case AnimatorControllerParameterType.Int:
                    cond.mode = cond.mode switch
                    {
                        AnimatorConditionMode.If => AnimatorConditionMode.Equals,
                        AnimatorConditionMode.IfNot => AnimatorConditionMode.NotEqual,
                        _ => cond.mode
                    };
                    cond.threshold = Mathf.Round(cond.threshold);
                    break;
            }

            conditions[conditionIndex] = cond;
            transition.conditions = conditions;
            EditorUtility.SetDirty(transition);
        }
    }
}
