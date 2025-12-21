using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using MVA.Toolbox.QuickAnimatorEdit.Services.Shared;

namespace MVA.Toolbox.QuickAnimatorEdit.Services.Parameter
{
    public static class ParameterAdjustService
    {
        public static bool RenameParameter(AnimatorController controller, string oldName, string newName)
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

        public static bool ChangeParameterType(
            AnimatorController controller,
            string parameterName,
            AnimatorControllerParameterType targetType)
        {
            if (controller == null || string.IsNullOrEmpty(parameterName))
            {
                return false;
            }

            var parameters = controller.parameters;
            bool updatedDefinition = false;
            AnimatorControllerParameterType originalType = AnimatorControllerParameterType.Float;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == parameterName)
                {
                    originalType = parameters[i].type;

                    if (originalType == targetType)
                    {
                        return false;
                    }

                    if (!ParameterTypeConversionUtility.IsSupportedType(originalType) ||
                        !ParameterTypeConversionUtility.IsSupportedType(targetType))
                    {
                        return false;
                    }

                    parameters[i] = ParameterTypeConversionUtility.ApplyTypeConversion(parameters[i], targetType);
                    updatedDefinition = true;
                    break;
                }
            }

            if (!updatedDefinition)
            {
                return false;
            }

            controller.parameters = parameters;

            TraverseStateMachines(controller, state => { }, transition =>
            {
                var conditions = transition.conditions;
                bool changed = false;
                for (int i = 0; i < conditions.Length; i++)
                {
                    if (conditions[i].parameter == parameterName)
                    {
                        var converted = ParameterTypeConversionUtility.ConvertCondition(
                            conditions[i],
                            originalType,
                            targetType);
                        conditions[i] = converted;
                        changed = true;
                    }
                }

                if (changed)
                {
                    transition.conditions = conditions;
                    EditorUtility.SetDirty(transition);
                }
            },
            behaviour => { });

            EditorUtility.SetDirty(controller);
            return true;
        }

        public static bool SwapParameters(AnimatorController controller, string paramA, string paramB)
        {
            if (controller == null || string.IsNullOrEmpty(paramA) || string.IsNullOrEmpty(paramB) || paramA == paramB)
            {
                return false;
            }

            var parameters = controller.parameters;
            AnimatorControllerParameter parameterA = null;
            AnimatorControllerParameter parameterB = null;
            foreach (var p in parameters)
            {
                if (p.name == paramA) parameterA = p;
                if (p.name == paramB) parameterB = p;
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
            var parameters = controller.parameters;
            bool hasOld = false;
            foreach (var p in parameters)
            {
                if (p.name == newName)
                {
                    return false;
                }
                if (p.name == oldName)
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
            TraverseStateMachines(controller,
                state => RenameInState(state, oldName, newName),
                transition => RenameInTransition(transition, oldName, newName),
                behaviour => RenameInBehaviour(behaviour, oldName, newName));
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

            if (state.motion is UnityEditor.Animations.BlendTree bt)
            {
                RenameInBlendTree(bt, oldName, newName);
            }

            if (changed)
            {
                EditorUtility.SetDirty(state);
            }
        }

        private static void RenameInBlendTree(UnityEditor.Animations.BlendTree bt, string oldName, string newName)
        {
            if (bt == null) return;

            bool changed = false;
            if (!string.IsNullOrEmpty(bt.blendParameter) && bt.blendParameter == oldName)
            {
                bt.blendParameter = newName;
                changed = true;
            }
            if (!string.IsNullOrEmpty(bt.blendParameterY) && bt.blendParameterY == oldName)
            {
                bt.blendParameterY = newName;
                changed = true;
            }

            var children = bt.children;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].motion is UnityEditor.Animations.BlendTree childBt)
                {
                    RenameInBlendTree(childBt, oldName, newName);
                }

                if (!string.IsNullOrEmpty(children[i].directBlendParameter) &&
                    children[i].directBlendParameter == oldName)
                {
                    children[i].directBlendParameter = newName;
                    children[i].threshold = children[i].threshold;
                    changed = true;
                }
            }
            bt.children = children;

            if (changed)
            {
                EditorUtility.SetDirty(bt);
            }
        }

        private static void RenameInTransition(AnimatorTransitionBase transition, string oldName, string newName)
        {
            var conditions = transition.conditions;
            bool changed = false;
            for (int i = 0; i < conditions.Length; i++)
            {
                if (conditions[i].parameter == oldName)
                {
                    conditions[i].parameter = newName;
                    changed = true;
                }
            }

            if (changed)
            {
                transition.conditions = conditions;
                EditorUtility.SetDirty(transition);
            }
        }

        private static void RenameInBehaviour(StateMachineBehaviour behaviour, string oldName, string newName)
        {
            if (behaviour == null) return;
            if (!behaviour.GetType().Name.Contains("VRCAvatarParameterDriver"))
            {
                return;
            }

            var so = new SerializedObject(behaviour);
            var parametersProp = so.FindProperty("parameters");
            if (parametersProp == null)
            {
                return;
            }

            bool changed = false;
            for (int i = 0; i < parametersProp.arraySize; i++)
            {
                var element = parametersProp.GetArrayElementAtIndex(i);
                var nameProp = element.FindPropertyRelative("name");
                if (nameProp != null && nameProp.stringValue == oldName)
                {
                    nameProp.stringValue = newName;
                    changed = true;
                }

                var sourceProp = element.FindPropertyRelative("source");
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
            if (controller == null || controller.layers == null) return;

            for (int i = 0; i < controller.layers.Length; i++)
            {
                var layer = controller.layers[i];
                if (layer?.stateMachine == null) continue;
                TraverseStateMachine(layer.stateMachine, stateAction, transitionAction, behaviourAction);
            }
        }

        private static void TraverseStateMachine(
            AnimatorStateMachine stateMachine,
            Action<AnimatorState> stateAction,
            Action<AnimatorTransitionBase> transitionAction,
            Action<StateMachineBehaviour> behaviourAction)
        {
            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                if (state == null) continue;

                stateAction?.Invoke(state);

                foreach (var transition in state.transitions)
                {
                    transitionAction?.Invoke(transition);
                }

                if (state.behaviours != null)
                {
                    foreach (var behaviour in state.behaviours)
                    {
                        behaviourAction?.Invoke(behaviour);
                    }
                }
            }

            foreach (var transition in stateMachine.anyStateTransitions)
            {
                transitionAction?.Invoke(transition);
            }

            foreach (var transition in stateMachine.entryTransitions)
            {
                transitionAction?.Invoke(transition);
            }

            foreach (var sub in stateMachine.stateMachines)
            {
                var subStateMachine = sub.stateMachine;
                if (subStateMachine != null)
                {
                    TraverseStateMachine(subStateMachine, stateAction, transitionAction, behaviourAction);
                }
            }
        }
    }
}
