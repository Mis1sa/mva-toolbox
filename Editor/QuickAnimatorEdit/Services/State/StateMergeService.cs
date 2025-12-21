using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using MVA.Toolbox.QuickAnimatorEdit.Services.Shared;

namespace MVA.Toolbox.QuickAnimatorEdit.Services.State
{
    /// <summary>
    /// 状态合并服务
    /// 将两个状态合并为一个，重定向入站 Transition，迁移出站 Transition，删除被合并状态
    /// </summary>
    public static class StateMergeService
    {
        /// <summary>
        /// 执行状态合并
        /// </summary>
        /// <param name="controller">目标控制器</param>
        /// <param name="layerIndex">层索引</param>
        /// <param name="stateToKeep">保留的状态</param>
        /// <param name="stateToRemove">要移除的状态</param>
        /// <param name="newStateName">合并后的状态名称（可空，为空则保留原名）</param>
        /// <returns>是否成功</returns>
        public static bool Execute(
            AnimatorController controller,
            int layerIndex,
            AnimatorState stateToKeep,
            AnimatorState stateToRemove,
            string newStateName)
        {
            if (controller == null || layerIndex < 0 || layerIndex >= controller.layers.Length || stateToKeep == null || stateToRemove == null || stateToKeep == stateToRemove)
            {
                Debug.LogError("[StateMergeService] Execute: 无效的输入参数或状态相同。");
                return false;
            }

            var layer = controller.layers[layerIndex];
            var stateMachine = layer.stateMachine;

            // 查找父状态机
            // 这里的查找不需要位置，所以可以直接尝试用 AnimatorPathUtility 或者手动查找
            // 由于输入已经是 AnimatorState 对象（从 UI 通过 Path 找到的），我们需要反查它们的父状态机以便删除操作
            
            AnimatorStateMachine parentMachineToKeep = FindParentStateMachine(stateMachine, stateToKeep);
            AnimatorStateMachine parentMachineToRemove = FindParentStateMachine(stateMachine, stateToRemove);

            if (parentMachineToKeep == null || parentMachineToRemove == null)
            {
                Debug.LogError("[StateMergeService] Execute: 无法在当前层中找到指定状态的父状态机。");
                return false;
            }

            Undo.RecordObject(controller, "Quick State - Merge Animator States");
            Undo.RecordObject(parentMachineToKeep, "Quick State - Merge Animator States Parent");
            if (parentMachineToRemove != null && parentMachineToRemove != parentMachineToKeep)
            {
                Undo.RecordObject(parentMachineToRemove, "Quick State - Merge Animator States Parent Remove");
            }

            // 1. 重定向入站 Transition (指向 stateToRemove 的都改为 stateToKeep)
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

            // 2. 迁移出站 Transition (从 stateToRemove 出发的都复制到 stateToKeep)
            var transitionsToMove = stateToRemove.transitions.ToList();

            foreach (var transition in transitionsToMove)
            {
                AnimatorStateTransition newTransition;
                if (transition.destinationState != null)
                {
                    // 避免自我循环的重复（如果 Keep 已经有去该目标的连线）- 但通常合并是保留所有路径
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

            // 3. 处理名称和删除
            var stateToKeepName = stateToKeep.name;
            var stateToRemoveName = stateToRemove.name;

            // 如果 stateToRemove 是默认状态，转移默认状态给 stateToKeep
            if (parentMachineToRemove.defaultState == stateToRemove)
            {
                parentMachineToRemove.defaultState = stateToKeep; // 注意：如果跨子状态机合并，这可能不合法，但在 Unity 中通常是在同一层级或有引用关系
                // 严谨起见，只有当 parentMachineToRemove == parentMachineToKeep 时才安全转移默认状态
                // 或者如果 stateToKeep 在另一个机器，那那个机器的 defaultState 不受影响，而 Remove 的机器需要一个新的 defaultState (通常 Unity 会置空)
            }

            parentMachineToRemove.RemoveState(stateToRemove);

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

        private static AnimatorStateMachine FindParentStateMachine(AnimatorStateMachine root, AnimatorState targetState)
        {
            if (root == null || targetState == null) return null;

            foreach (var child in root.states)
            {
                if (child.state == targetState) return root;
            }

            foreach (var sub in root.stateMachines)
            {
                if (sub.stateMachine != null)
                {
                    var found = FindParentStateMachine(sub.stateMachine, targetState);
                    if (found != null) return found;
                }
            }

            return null;
        }

        private static List<AnimatorState> GetAllStatesInLayer(AnimatorController controller, int layerIndex)
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
    }
}
