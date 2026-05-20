using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.AnimatorParameterTool
{
    internal static class AnimatorParameterAdjustService
    {
        internal static bool RemoveParameter(AnimatorController controller, string parameterName)
        {
            if (controller == null || string.IsNullOrEmpty(parameterName))
            {
                return false;
            }

            AnimatorControllerParameter[] parameters = controller.parameters;
            if (parameters == null || parameters.Length == 0)
            {
                return false;
            }

            bool removedDefinition = false;
            var newParameters = new List<AnimatorControllerParameter>(parameters.Length);
            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter p = parameters[i];
                if (p != null && p.name == parameterName)
                {
                    removedDefinition = true;
                    continue;
                }

                newParameters.Add(p);
            }

            if (!removedDefinition)
            {
                return false;
            }

            controller.parameters = newParameters.ToArray();
            TraverseStateMachines(
                controller,
                state => RemoveInState(state, parameterName),
                transition => RemoveInTransition(transition, parameterName),
                behaviour => RemoveInBehaviour(behaviour, parameterName));

            EditorUtility.SetDirty(controller);
            return true;
        }

        internal static bool RenameParameter(AnimatorController controller, string oldName, string newName)
        {
            if (controller == null || string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName))
            {
                return false;
            }

            if (!TryRenameDefinition(controller, oldName, newName))
            {
                return false;
            }

            RenameReferences(controller, oldName, newName);
            EditorUtility.SetDirty(controller);
            return true;
        }

        internal static bool ChangeParameterType(
            AnimatorController controller,
            string parameterName,
            AnimatorControllerParameterType targetType)
        {
            if (controller == null || string.IsNullOrEmpty(parameterName))
            {
                return false;
            }

            AnimatorControllerParameter[] parameters = controller.parameters;
            bool updatedDefinition = false;
            AnimatorControllerParameterType originalType = AnimatorControllerParameterType.Float;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name != parameterName)
                {
                    continue;
                }

                originalType = parameters[i].type;
                if (originalType == targetType)
                {
                    return false;
                }

                if (!AnimatorParameterTypeConversionUtility.IsSupportedType(originalType) ||
                    !AnimatorParameterTypeConversionUtility.IsSupportedType(targetType))
                {
                    return false;
                }

                parameters[i] = AnimatorParameterTypeConversionUtility.ApplyTypeConversion(parameters[i], targetType);
                updatedDefinition = true;
                break;
            }

            if (!updatedDefinition)
            {
                return false;
            }

            controller.parameters = parameters;

            TraverseStateMachines(controller, state => { }, transition =>
            {
                AnimatorCondition[] conditions = transition.conditions;
                bool changed = false;
                for (int i = 0; i < conditions.Length; i++)
                {
                    if (conditions[i].parameter != parameterName)
                    {
                        continue;
                    }

                    AnimatorCondition converted = AnimatorParameterTypeConversionUtility.ConvertCondition(
                        conditions[i],
                        originalType,
                        targetType);
                    conditions[i] = converted;
                    changed = true;
                }

                if (changed)
                {
                    transition.conditions = conditions;
                    EditorUtility.SetDirty(transition);
                }
            }, behaviour => { });

            EditorUtility.SetDirty(controller);
            return true;
        }

        internal static bool SwapParameters(AnimatorController controller, string paramA, string paramB)
        {
            if (controller == null || string.IsNullOrEmpty(paramA) || string.IsNullOrEmpty(paramB) || paramA == paramB)
            {
                return false;
            }

            AnimatorControllerParameter[] parameters = controller.parameters;
            AnimatorControllerParameter parameterA = null;
            AnimatorControllerParameter parameterB = null;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == paramA)
                {
                    parameterA = parameters[i];
                }

                if (parameters[i].name == paramB)
                {
                    parameterB = parameters[i];
                }
            }

            if (parameterA == null || parameterB == null || parameterA.type != parameterB.type)
            {
                return false;
            }

            string tempName = $"__TEMP__{Guid.NewGuid():N}";
            RenameParameter(controller, paramB, tempName);
            RenameParameter(controller, paramA, paramB);
            RenameParameter(controller, tempName, paramA);
            return true;
        }

        private static bool TryRenameDefinition(AnimatorController controller, string oldName, string newName)
        {
            AnimatorControllerParameter[] parameters = controller.parameters;
            bool hasOld = false;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == newName)
                {
                    return false;
                }

                if (parameters[i].name == oldName)
                {
                    hasOld = true;
                }
            }

            if (!hasOld)
            {
                return false;
            }

            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == oldName)
                {
                    parameters[i].name = newName;
                }
            }

            controller.parameters = parameters;
            return true;
        }

        private static void RenameReferences(AnimatorController controller, string oldName, string newName)
        {
            TraverseStateMachines(
                controller,
                state => RenameInState(state, oldName, newName),
                transition => RenameInTransition(transition, oldName, newName),
                behaviour => RenameInBehaviour(behaviour, oldName, newName));
        }

        private static void RemoveInState(AnimatorState state, string parameterName)
        {
            if (state == null)
            {
                return;
            }

            bool changed = false;
            if (!string.IsNullOrEmpty(state.speedParameter) && state.speedParameter == parameterName)
            {
                state.speedParameter = string.Empty;
                changed = true;
            }

            if (!string.IsNullOrEmpty(state.mirrorParameter) && state.mirrorParameter == parameterName)
            {
                state.mirrorParameter = string.Empty;
                changed = true;
            }

            if (!string.IsNullOrEmpty(state.cycleOffsetParameter) && state.cycleOffsetParameter == parameterName)
            {
                state.cycleOffsetParameter = string.Empty;
                changed = true;
            }

            if (!string.IsNullOrEmpty(state.timeParameter) && state.timeParameter == parameterName)
            {
                state.timeParameter = string.Empty;
                changed = true;
            }

            if (state.motion is BlendTree blendTree)
            {
                changed |= RemoveInBlendTree(blendTree, parameterName);
            }

            if (changed)
            {
                EditorUtility.SetDirty(state);
            }
        }

        private static bool RemoveInBlendTree(BlendTree blendTree, string parameterName)
        {
            if (blendTree == null)
            {
                return false;
            }

            bool changed = false;
            if (!string.IsNullOrEmpty(blendTree.blendParameter) && blendTree.blendParameter == parameterName)
            {
                blendTree.blendParameter = string.Empty;
                changed = true;
            }

            if (!string.IsNullOrEmpty(blendTree.blendParameterY) && blendTree.blendParameterY == parameterName)
            {
                blendTree.blendParameterY = string.Empty;
                changed = true;
            }

            ChildMotion[] children = blendTree.children;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].motion is BlendTree childBlendTree)
                {
                    changed |= RemoveInBlendTree(childBlendTree, parameterName);
                }

                if (!string.IsNullOrEmpty(children[i].directBlendParameter) && children[i].directBlendParameter == parameterName)
                {
                    children[i].directBlendParameter = string.Empty;
                    children[i].threshold = children[i].threshold;
                    changed = true;
                }
            }

            blendTree.children = children;
            if (changed)
            {
                EditorUtility.SetDirty(blendTree);
            }

            return changed;
        }

        private static void RemoveInTransition(AnimatorTransitionBase transition, string parameterName)
        {
            if (transition == null)
            {
                return;
            }

            AnimatorCondition[] conditions = transition.conditions;
            if (conditions == null || conditions.Length == 0)
            {
                return;
            }

            bool changed = false;
            var newConditions = new List<AnimatorCondition>(conditions.Length);
            for (int i = 0; i < conditions.Length; i++)
            {
                if (conditions[i].parameter == parameterName)
                {
                    changed = true;
                    continue;
                }

                newConditions.Add(conditions[i]);
            }

            if (changed)
            {
                transition.conditions = newConditions.ToArray();
                EditorUtility.SetDirty(transition);
            }
        }

        private static void RemoveInBehaviour(StateMachineBehaviour behaviour, string parameterName)
        {
            if (behaviour == null || !IsAvatarParameterDriverBehaviour(behaviour))
            {
                return;
            }

            var so = new SerializedObject(behaviour);
            SerializedProperty parametersProp = so.FindProperty("parameters");
            if (parametersProp == null || !parametersProp.isArray)
            {
                return;
            }

            bool changed = false;
            for (int i = parametersProp.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty element = parametersProp.GetArrayElementAtIndex(i);
                SerializedProperty nameProp = element.FindPropertyRelative("name");
                SerializedProperty sourceProp = element.FindPropertyRelative("source");
                bool matchesName = nameProp != null && nameProp.stringValue == parameterName;
                bool matchesSource = sourceProp != null && sourceProp.stringValue == parameterName;
                if (!matchesName && !matchesSource)
                {
                    continue;
                }

                parametersProp.DeleteArrayElementAtIndex(i);
                changed = true;
            }

            if (changed)
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(behaviour);
            }
        }

        private static void RenameInState(AnimatorState state, string oldName, string newName)
        {
            bool changed = false;
            if (!string.IsNullOrEmpty(state.speedParameter) && state.speedParameter == oldName)
            {
                state.speedParameter = newName;
                changed = true;
            }

            if (!string.IsNullOrEmpty(state.mirrorParameter) && state.mirrorParameter == oldName)
            {
                state.mirrorParameter = newName;
                changed = true;
            }

            if (!string.IsNullOrEmpty(state.cycleOffsetParameter) && state.cycleOffsetParameter == oldName)
            {
                state.cycleOffsetParameter = newName;
                changed = true;
            }

            if (!string.IsNullOrEmpty(state.timeParameter) && state.timeParameter == oldName)
            {
                state.timeParameter = newName;
                changed = true;
            }

            if (state.motion is BlendTree blendTree)
            {
                RenameInBlendTree(blendTree, oldName, newName);
            }

            if (changed)
            {
                EditorUtility.SetDirty(state);
            }
        }

        private static void RenameInBlendTree(BlendTree blendTree, string oldName, string newName)
        {
            if (blendTree == null)
            {
                return;
            }

            bool changed = false;
            if (!string.IsNullOrEmpty(blendTree.blendParameter) && blendTree.blendParameter == oldName)
            {
                blendTree.blendParameter = newName;
                changed = true;
            }

            if (!string.IsNullOrEmpty(blendTree.blendParameterY) && blendTree.blendParameterY == oldName)
            {
                blendTree.blendParameterY = newName;
                changed = true;
            }

            ChildMotion[] children = blendTree.children;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].motion is BlendTree childBlendTree)
                {
                    RenameInBlendTree(childBlendTree, oldName, newName);
                }

                if (!string.IsNullOrEmpty(children[i].directBlendParameter) && children[i].directBlendParameter == oldName)
                {
                    children[i].directBlendParameter = newName;
                    children[i].threshold = children[i].threshold;
                    changed = true;
                }
            }

            blendTree.children = children;
            if (changed)
            {
                EditorUtility.SetDirty(blendTree);
            }
        }

        private static void RenameInTransition(AnimatorTransitionBase transition, string oldName, string newName)
        {
            AnimatorCondition[] conditions = transition.conditions;
            bool changed = false;
            for (int i = 0; i < conditions.Length; i++)
            {
                if (conditions[i].parameter != oldName)
                {
                    continue;
                }

                conditions[i].parameter = newName;
                changed = true;
            }

            if (changed)
            {
                transition.conditions = conditions;
                EditorUtility.SetDirty(transition);
            }
        }

        private static void RenameInBehaviour(StateMachineBehaviour behaviour, string oldName, string newName)
        {
            if (behaviour == null || !IsAvatarParameterDriverBehaviour(behaviour))
            {
                return;
            }

            var so = new SerializedObject(behaviour);
            SerializedProperty parametersProp = so.FindProperty("parameters");
            if (parametersProp == null || !parametersProp.isArray)
            {
                return;
            }

            bool changed = false;
            for (int i = 0; i < parametersProp.arraySize; i++)
            {
                SerializedProperty element = parametersProp.GetArrayElementAtIndex(i);
                SerializedProperty nameProp = element.FindPropertyRelative("name");
                if (nameProp != null && nameProp.stringValue == oldName)
                {
                    nameProp.stringValue = newName;
                    changed = true;
                }

                SerializedProperty sourceProp = element.FindPropertyRelative("source");
                if (sourceProp != null && sourceProp.stringValue == oldName)
                {
                    sourceProp.stringValue = newName;
                    changed = true;
                }
            }

            if (changed)
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(behaviour);
            }
        }

        private static void TraverseStateMachines(
            AnimatorController controller,
            Action<AnimatorState> stateAction,
            Action<AnimatorTransitionBase> transitionAction,
            Action<StateMachineBehaviour> behaviourAction)
        {
            if (controller == null || controller.layers == null)
            {
                return;
            }

            for (int i = 0; i < controller.layers.Length; i++)
            {
                AnimatorControllerLayer layer = controller.layers[i];
                if (layer?.stateMachine == null)
                {
                    continue;
                }

                TraverseStateMachine(layer.stateMachine, stateAction, transitionAction, behaviourAction);
            }
        }

        private static void TraverseStateMachine(
            AnimatorStateMachine stateMachine,
            Action<AnimatorState> stateAction,
            Action<AnimatorTransitionBase> transitionAction,
            Action<StateMachineBehaviour> behaviourAction)
        {
            if (stateMachine == null)
            {
                return;
            }

            StateMachineBehaviour[] stateMachineBehaviours = stateMachine.behaviours;
            if (stateMachineBehaviours != null)
            {
                for (int i = 0; i < stateMachineBehaviours.Length; i++)
                {
                    behaviourAction?.Invoke(stateMachineBehaviours[i]);
                }
            }

            ChildAnimatorState[] states = stateMachine.states;
            for (int i = 0; i < states.Length; i++)
            {
                AnimatorState state = states[i].state;
                if (state == null)
                {
                    continue;
                }

                stateAction?.Invoke(state);

                AnimatorStateTransition[] transitions = state.transitions;
                for (int j = 0; j < transitions.Length; j++)
                {
                    transitionAction?.Invoke(transitions[j]);
                }

                StateMachineBehaviour[] stateBehaviours = state.behaviours;
                if (stateBehaviours == null)
                {
                    continue;
                }

                for (int j = 0; j < stateBehaviours.Length; j++)
                {
                    behaviourAction?.Invoke(stateBehaviours[j]);
                }
            }

            AnimatorStateTransition[] anyStateTransitions = stateMachine.anyStateTransitions;
            for (int i = 0; i < anyStateTransitions.Length; i++)
            {
                transitionAction?.Invoke(anyStateTransitions[i]);
            }

            AnimatorTransition[] entryTransitions = stateMachine.entryTransitions;
            for (int i = 0; i < entryTransitions.Length; i++)
            {
                transitionAction?.Invoke(entryTransitions[i]);
            }

            ChildAnimatorStateMachine[] subMachines = stateMachine.stateMachines;
            for (int i = 0; i < subMachines.Length; i++)
            {
                AnimatorStateMachine subStateMachine = subMachines[i].stateMachine;
                if (subStateMachine != null)
                {
                    TraverseStateMachine(subStateMachine, stateAction, transitionAction, behaviourAction);
                }
            }
        }

        private static bool IsAvatarParameterDriverBehaviour(StateMachineBehaviour behaviour)
        {
            if (behaviour == null)
            {
                return false;
            }

            string typeName = behaviour.GetType().Name;
            return !string.IsNullOrEmpty(typeName) &&
                   typeName.IndexOf("AvatarParameterDriver", StringComparison.Ordinal) >= 0;
        }
    }
}
