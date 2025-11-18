using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.QuickState.Services
{
    internal static class QuickStateService
    {
        internal struct TransitionAdjustment
        {
            public AnimatorStateTransition Transition;
            public bool ShouldMove;
            public string DisplayName;
        }

        internal enum DefaultStateDesignation
        {
            Head,
            Tail
        }

        internal static bool SplitAnimatorState(
            AnimatorController controller,
            int layerIndex,
            AnimatorState originalState,
            string headStateName,
            string tailStateName,
            bool isOriginalHead,
            bool isOriginalTail,
            bool isDefaultState,
            DefaultStateDesignation defaultDesignation,
            List<TransitionAdjustment> headAdjustments,
            List<TransitionAdjustment> tailAdjustments)
        {
            if (controller == null || layerIndex < 0 || layerIndex >= controller.layers.Length || originalState == null)
            {
                Debug.LogError("[QuickState] SplitAnimatorState: 无效的输入参数。");
                return false;
            }

            if (isOriginalHead && isOriginalTail)
            {
                Debug.LogError("[QuickState] SplitAnimatorState: 原始状态不能同时作为头部和尾部。");
                return false;
            }

            var layer = controller.layers[layerIndex];
            var stateMachine = layer.stateMachine;

            Vector3 originalPosition = Vector3.zero;
            AnimatorStateMachine parentStateMachine = null;

            if (!FindStateAndParentMachine(stateMachine, originalState, ref originalPosition, ref parentStateMachine))
            {
                Debug.LogError($"[QuickState] SplitAnimatorState: 无法在 {layer.name} 层级中找到状态 {originalState.name}。");
                return false;
            }

            if (parentStateMachine == null)
            {
                parentStateMachine = stateMachine;
            }

            Undo.RecordObject(controller, "Quick State - Split Animator State");
            Undo.RecordObject(parentStateMachine, "Quick State - Split Animator State Machine");

            AnimatorState headState;
            AnimatorState tailState;

            bool isOriginalTheHead = isOriginalHead || (!isOriginalHead && !isOriginalTail);

            var originalStateSerialized = new SerializedObject(originalState);
            var originalWriteDefaultProp = originalStateSerialized.FindProperty("m_WriteDefaultValues");
            bool originalWriteDefault = originalWriteDefaultProp != null && originalWriteDefaultProp.boolValue;

            if (!isOriginalTheHead)
            {
                headState = parentStateMachine.AddState(headStateName, originalPosition + new Vector3(-300, 0, 0));
                SetStateWriteDefaults(headState, originalWriteDefault);

                tailState = originalState;
                Undo.RecordObject(tailState, "Quick State - Rename Original State to Tail");
                tailState.name = tailStateName;
            }
            else
            {
                headState = originalState;
                Undo.RecordObject(headState, "Quick State - Rename Original State to Head");
                headState.name = headStateName;

                tailState = parentStateMachine.AddState(tailStateName, originalPosition + new Vector3(300, 0, 0));
                SetStateWriteDefaults(tailState, originalWriteDefault);
            }

            var allStatesInLayer = GetAllStatesInLayer(controller, layerIndex);

            var incomingTransitions = new List<(AnimatorStateTransition transition, AnimatorState sourceState)>();
            foreach (var state in allStatesInLayer)
            {
                foreach (var transition in state.transitions)
                {
                    if (transition.destinationState == originalState)
                    {
                        incomingTransitions.Add((transition, state));
                        Undo.RecordObject(transition, "Quick State - Retarget Incoming Transition");
                    }
                }
            }

            var anyStateTransitions = stateMachine.anyStateTransitions
                .Where(t => t.destinationState == originalState)
                .ToList();

            foreach (var transition in anyStateTransitions)
            {
                Undo.RecordObject(transition, "Quick State - Retarget AnyState Transition");
            }

            foreach (var incoming in incomingTransitions)
            {
                incoming.transition.destinationState = headState;
            }

            foreach (var transition in anyStateTransitions)
            {
                transition.destinationState = headState;
            }

            if (headState == originalState)
            {
                var transitionsToMove = originalState.transitions.ToList();

                Undo.RecordObject(originalState, "Quick State - Clear Head State Outgoing");
                originalState.transitions = new AnimatorStateTransition[0];

                foreach (var transition in transitionsToMove)
                {
                    AnimatorStateTransition newTransition;
                    if (transition.destinationState != null)
                    {
                        newTransition = tailState.AddTransition(transition.destinationState);
                    }
                    else if (transition.destinationStateMachine != null)
                    {
                        newTransition = tailState.AddTransition(transition.destinationStateMachine);
                    }
                    else
                    {
                        newTransition = tailState.AddExitTransition();
                    }

                    EditorUtility.CopySerialized(transition, newTransition);

                    newTransition.destinationState = transition.destinationState;
                    newTransition.destinationStateMachine = transition.destinationStateMachine;
                }
            }

            foreach (var adj in headAdjustments.Where(a => a.ShouldMove))
            {
                var incoming = incomingTransitions.FirstOrDefault(t => t.transition == adj.Transition);
                if (incoming.transition != null)
                {
                    incoming.transition.destinationState = tailState;
                }
                else if (anyStateTransitions.Contains(adj.Transition))
                {
                    adj.Transition.destinationState = tailState;
                }
            }

            foreach (var adj in tailAdjustments.Where(a => a.ShouldMove))
            {
                var tailTransition = tailState.transitions.FirstOrDefault(t =>
                    t.destinationState == adj.Transition.destinationState &&
                    t.destinationStateMachine == adj.Transition.destinationStateMachine);

                if (tailTransition != null)
                {
                    AnimatorStateTransition newHeadTransition;
                    if (tailTransition.destinationState != null)
                    {
                        newHeadTransition = headState.AddTransition(tailTransition.destinationState);
                    }
                    else if (tailTransition.destinationStateMachine != null)
                    {
                        newHeadTransition = headState.AddTransition(tailTransition.destinationStateMachine);
                    }
                    else
                    {
                        newHeadTransition = headState.AddExitTransition();
                    }

                    EditorUtility.CopySerialized(tailTransition, newHeadTransition);
                    Undo.RecordObject(headState, "Quick State - Move Transition To Head");

                    newHeadTransition.destinationState = tailTransition.destinationState;
                    newHeadTransition.destinationStateMachine = tailTransition.destinationStateMachine;

                    Undo.RecordObject(tailState, "Quick State - Remove Transition From Tail");
                    tailState.RemoveTransition(tailTransition);
                }
            }

            if (isDefaultState)
            {
                var newState = defaultDesignation == DefaultStateDesignation.Head ? headState : tailState;
                parentStateMachine.defaultState = newState;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            Debug.Log($"[QuickState] 已将状态 '{originalState.name}' 分割为 '{headState.name}' 和 '{tailState.name}'。");
            return true;
        }

        internal static bool MergeAnimatorStates(
            AnimatorController controller,
            int layerIndex,
            AnimatorState stateToKeep,
            AnimatorState stateToRemove,
            string newStateName)
        {
            if (controller == null || layerIndex < 0 || layerIndex >= controller.layers.Length || stateToKeep == null || stateToRemove == null || stateToKeep == stateToRemove)
            {
                Debug.LogError("[QuickState] MergeAnimatorStates: 无效的输入参数或状态相同。");
                return false;
            }

            var layer = controller.layers[layerIndex];
            var stateMachine = layer.stateMachine;

            AnimatorStateMachine parentMachineToKeep = null;
            AnimatorStateMachine parentMachineToRemove = null;
            var dummyPos = Vector3.zero;

            if (!FindStateAndParentMachine(stateMachine, stateToKeep, ref dummyPos, ref parentMachineToKeep) ||
                !FindStateAndParentMachine(stateMachine, stateToRemove, ref dummyPos, ref parentMachineToRemove))
            {
                Debug.LogError("[QuickState] MergeAnimatorStates: 无法在当前层中找到指定状态。");
                return false;
            }

            Undo.RecordObject(controller, "Quick State - Merge Animator States");
            Undo.RecordObject(parentMachineToKeep, "Quick State - Merge Animator States Parent");
            if (parentMachineToRemove != null && parentMachineToRemove != parentMachineToKeep)
            {
                Undo.RecordObject(parentMachineToRemove, "Quick State - Merge Animator States Parent Remove");
            }

            var allStatesInLayer = GetAllStatesInLayer(controller, layerIndex);

            foreach (var state in allStatesInLayer)
            {
                foreach (var transition in state.transitions)
                {
                    if (transition.destinationState == stateToRemove)
                    {
                        Undo.RecordObject(transition, "Quick State - Retarget Incoming Transition");
                        transition.destinationState = stateToKeep;
                    }
                }
            }

            var anyStateTransitions = stateMachine.anyStateTransitions
                .Where(t => t.destinationState == stateToRemove)
                .ToList();

            foreach (var transition in anyStateTransitions)
            {
                Undo.RecordObject(transition, "Quick State - Retarget AnyState Transition");
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

            var stateToKeepName = stateToKeep != null ? stateToKeep.name : "(null)";
            var stateToRemoveName = stateToRemove != null ? stateToRemove.name : "(null)";

            if (parentMachineToRemove != null)
            {
                parentMachineToRemove.RemoveState(stateToRemove);
            }

            if (!string.IsNullOrEmpty(newStateName))
            {
                Undo.RecordObject(stateToKeep, "Quick State - Rename Merged State");
                stateToKeep.name = newStateName;
                stateToKeepName = newStateName;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            Debug.Log($"[QuickState] 已将状态 '{stateToRemoveName}' 合并到 '{stateToKeepName}' 中。");
            return true;
        }

        static List<AnimatorState> GetAllStatesInLayer(AnimatorController controller, int layerIndex)
        {
            var result = new List<AnimatorState>();
            if (controller == null || layerIndex < 0 || layerIndex >= controller.layers.Length)
                return result;

            var stateMachine = controller.layers[layerIndex].stateMachine;
            CollectStatesRecursive(stateMachine, result);
            return result;
        }

        static void CollectStatesRecursive(AnimatorStateMachine stateMachine, List<AnimatorState> result)
        {
            if (stateMachine == null) return;

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

        static bool FindStateAndParentMachine(AnimatorStateMachine root, AnimatorState targetState, ref Vector3 position, ref AnimatorStateMachine parentMachine)
        {
            if (root == null || targetState == null)
                return false;

            foreach (var child in root.states)
            {
                if (child.state == targetState)
                {
                    position = child.position;
                    parentMachine = root;
                    return true;
                }
            }

            foreach (var sub in root.stateMachines)
            {
                if (sub.stateMachine != null)
                {
                    if (FindStateAndParentMachine(sub.stateMachine, targetState, ref position, ref parentMachine))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        static void SetStateWriteDefaults(AnimatorState state, bool writeDefault)
        {
            if (state == null)
                return;

            try
            {
                var serializedState = new SerializedObject(state);
                var writeDefaultProp = serializedState.FindProperty("m_WriteDefaultValues");
                if (writeDefaultProp != null)
                {
                    writeDefaultProp.boolValue = writeDefault;
                    serializedState.ApplyModifiedProperties();
                }
            }
            catch
            {
            }
        }
    }
}
