using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.QuickAnimatorEdit.Services.State
{
    /// <summary>
    /// 状态拆分服务
    /// 将一个状态拆分为 Head 和 Tail 两个状态，并处理相关 Transition 重定向
    /// </summary>
    public static class StateSplitService
    {
        /// <summary>
        /// 默认状态指定方式
        /// </summary>
        public enum DefaultStateDesignation
        {
            Head,
            Tail
        }

        /// <summary>
        /// Transition 调整信息
        /// </summary>
        public struct TransitionAdjustment
        {
            public AnimatorStateTransition Transition;
            public bool ShouldMove;
            public string DisplayName;
        }

        /// <summary>
        /// 执行状态拆分
        /// </summary>
        public static bool Execute(
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
                Debug.LogError("[StateSplitService] Execute: 无效的输入参数。");
                return false;
            }

            if (isOriginalHead && isOriginalTail)
            {
                Debug.LogError("[StateSplitService] Execute: 原始状态不能同时作为头部和尾部。");
                return false;
            }

            var layer = controller.layers[layerIndex];
            var stateMachine = layer.stateMachine;

            Vector3 originalPosition = Vector3.zero;
            AnimatorStateMachine parentStateMachine = null;

            if (!FindStateAndParentMachine(stateMachine, originalState, ref originalPosition, ref parentStateMachine))
            {
                Debug.LogError($"[StateSplitService] Execute: 无法在 {layer.name} 层级中找到状态 {originalState.name}。");
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

            // 重定向所有入站过渡到 Head
            foreach (var incoming in incomingTransitions)
            {
                incoming.transition.destinationState = headState;
            }

            foreach (var transition in anyStateTransitions)
            {
                transition.destinationState = headState;
            }

            // 如果原始状态是 Head，需要将原来的出站过渡移动到 Tail（除非特殊调整）
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

            // 处理 Head 调整（将部分入站移动到 Tail）
            if (headAdjustments != null)
            {
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
            }

            // 处理 Tail 调整（将部分出站移动回 Head）
            if (tailAdjustments != null)
            {
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
            }

            if (isDefaultState)
            {
                var newState = defaultDesignation == DefaultStateDesignation.Head ? headState : tailState;
                parentStateMachine.defaultState = newState;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return true;
        }

        private static bool FindStateAndParentMachine(AnimatorStateMachine root, AnimatorState targetState, ref Vector3 position, ref AnimatorStateMachine parentMachine)
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

        public static List<AnimatorState> GetAllStatesInLayer(AnimatorController controller, int layerIndex)
        {
            var result = new List<AnimatorState>();
            if (controller == null || layerIndex < 0 || layerIndex >= controller.layers.Length)
                return result;

            var stateMachine = controller.layers[layerIndex].stateMachine;
            CollectStatesRecursive(stateMachine, result);
            return result;
        }

        private static void CollectStatesRecursive(AnimatorStateMachine stateMachine, List<AnimatorState> result)
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

        private static void SetStateWriteDefaults(AnimatorState state, bool writeDefault)
        {
            if (state == null) return;
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
            catch { }
        }
    }
}
