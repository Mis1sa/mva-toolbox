using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.AnimatorStateTool
{
    internal static class AnimatorStateMergeService
    {
        internal static bool Execute(
            AnimatorController controller,
            int layerIndex,
            AnimatorState stateToKeep,
            AnimatorState stateToRemove,
            string newStateName)
        {
            if (controller == null || layerIndex < 0 || layerIndex >= controller.layers.Length || stateToKeep == null || stateToRemove == null || stateToKeep == stateToRemove)
            {
                Debug.LogError("[AnimatorStateMergeService] Execute: 无效的输入参数或状态相同。");
                return false;
            }

            var layer = controller.layers[layerIndex];
            var stateMachine = layer.stateMachine;

            AnimatorStateMachine parentMachineToKeep = FindParentStateMachine(stateMachine, stateToKeep);
            AnimatorStateMachine parentMachineToRemove = FindParentStateMachine(stateMachine, stateToRemove);

            if (parentMachineToKeep == null || parentMachineToRemove == null)
            {
                Debug.LogError("[AnimatorStateMergeService] Execute: 无法在当前层中找到指定状态的父状态机。");
                return false;
            }

            Undo.RecordObject(controller, "Animator State - Merge Animator States");
            Undo.RecordObject(parentMachineToKeep, "Animator State - Merge Animator States Parent");
            if (parentMachineToRemove != null && parentMachineToRemove != parentMachineToKeep)
            {
                Undo.RecordObject(parentMachineToRemove, "Animator State - Merge Animator States Parent Remove");
            }

            var allStatesInLayer = GetAllStatesInLayer(controller, layerIndex);
            foreach (var state in allStatesInLayer)
            {
                foreach (var transition in state.transitions)
                {
                    if (transition.destinationState == stateToRemove)
                    {
                        Undo.RecordObject(transition, "Animator State - Retarget Incoming Transition");
                        transition.destinationState = stateToKeep;
                    }
                }
            }

            var anyStateTransitions = stateMachine.anyStateTransitions
                .Where(t => t.destinationState == stateToRemove)
                .ToList();

            foreach (var transition in anyStateTransitions)
            {
                Undo.RecordObject(transition, "Animator State - Retarget AnyState Transition");
                transition.destinationState = stateToKeep;
            }

            var transitionsToMove = stateToRemove.transitions.ToList();
            foreach (var transition in transitionsToMove)
            {
                AnimatorStateTransition newTransition;
                if (transition.destinationState != null)
                {
                    newTransition = stateToKeep.AddTransition(transition.destinationState);
                }
                else if (transition.destinationStateMachine != null)
                {
                    newTransition = stateToKeep.AddTransition(transition.destinationStateMachine);
                }
                else
                {
                    newTransition = stateToKeep.AddExitTransition();
                }

                EditorUtility.CopySerialized(transition, newTransition);
                newTransition.destinationState = transition.destinationState;
                newTransition.destinationStateMachine = transition.destinationStateMachine;
            }

            var stateToKeepName = stateToKeep.name;
            var stateToRemoveName = stateToRemove.name;

            if (parentMachineToRemove.defaultState == stateToRemove)
            {
                parentMachineToRemove.defaultState = stateToKeep;
            }

            parentMachineToRemove.RemoveState(stateToRemove);

            if (!string.IsNullOrEmpty(newStateName))
            {
                Undo.RecordObject(stateToKeep, "Animator State - Rename Merged State");
                stateToKeep.name = newStateName;
                stateToKeepName = newStateName;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log($"[AnimatorStateTool] 已将状态 '{stateToRemoveName}' 合并到 '{stateToKeepName}' 中。");
            return true;
        }

        private static AnimatorStateMachine FindParentStateMachine(AnimatorStateMachine root, AnimatorState targetState)
        {
            if (root == null || targetState == null)
            {
                return null;
            }

            foreach (var child in root.states)
            {
                if (child.state == targetState)
                {
                    return root;
                }
            }

            foreach (var sub in root.stateMachines)
            {
                if (sub.stateMachine != null)
                {
                    var found = FindParentStateMachine(sub.stateMachine, targetState);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }

            return null;
        }

        private static List<AnimatorState> GetAllStatesInLayer(AnimatorController controller, int layerIndex)
        {
            var result = new List<AnimatorState>();
            if (controller == null || layerIndex < 0 || layerIndex >= controller.layers.Length)
            {
                return result;
            }

            var stateMachine = controller.layers[layerIndex].stateMachine;
            CollectStatesRecursive(stateMachine, result);
            return result;
        }

        private static void CollectStatesRecursive(AnimatorStateMachine stateMachine, List<AnimatorState> result)
        {
            if (stateMachine == null)
            {
                return;
            }

            foreach (var child in stateMachine.states)
            {
                if (child.state != null)
                {
                    result.Add(child.state);
                }
            }

            foreach (var sub in stateMachine.stateMachines)
            {
                if (sub.stateMachine != null)
                {
                    CollectStatesRecursive(sub.stateMachine, result);
                }
            }
        }
    }
}
