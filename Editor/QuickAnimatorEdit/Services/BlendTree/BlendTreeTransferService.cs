using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using MVA.Toolbox.QuickAnimatorEdit.Services.Shared;

namespace MVA.Toolbox.QuickAnimatorEdit.Services.BlendTree
{
    /// <summary>
    /// 混合树转移服务
    /// 在控制器之间或层之间转移 BlendTree
    /// </summary>
    public static class BlendTreeTransferService
    {
        /// <summary>
        /// 转移模式
        /// </summary>
        public enum TransferMode
        {
            Copy,   // 复制（保留原始）
            Move    // 移动（删除原始 - 暂未完全实现自动删除源引用，仅做复制行为）
        }

        /// <summary>
        /// 转移结果
        /// </summary>
        public struct TransferResult
        {
            public bool Success;
            public UnityEditor.Animations.BlendTree NewBlendTree;
            public string ErrorMessage;
        }

        /// <summary>
        /// 转移 BlendTree 到另一个状态
        /// </summary>
        /// <param name="sourceBlendTree">源混合树</param>
        /// <param name="targetController">目标控制器</param>
        /// <param name="targetState">目标状态</param>
        /// <param name="mode">转移模式</param>
        /// <returns>转移结果</returns>
        public static TransferResult TransferToState(
            UnityEditor.Animations.BlendTree sourceBlendTree,
            AnimatorController targetController,
            AnimatorState targetState,
            TransferMode mode)
        {
            if (sourceBlendTree == null || targetController == null || targetState == null)
            {
                return new TransferResult { Success = false, ErrorMessage = "参数无效：源混合树、目标控制器或目标状态为空。" };
            }

            // 克隆混合树
            var newTree = CloneBlendTree(sourceBlendTree, targetController);
            if (newTree == null)
            {
                return new TransferResult { Success = false, ErrorMessage = "克隆混合树失败。" };
            }

            Undo.RecordObject(targetState, "Transfer BlendTree");
            targetState.motion = newTree;

            EditorUtility.SetDirty(targetController);
            AssetDatabase.SaveAssets();

            return new TransferResult { Success = true, NewBlendTree = newTree };
        }

        /// <summary>
        /// 复制 BlendTree（深拷贝）
        /// </summary>
        /// <param name="source">源混合树</param>
        /// <param name="targetController">目标控制器（用于存储新资产）</param>
        /// <returns>复制的混合树</returns>
        public static UnityEditor.Animations.BlendTree CloneBlendTree(
            UnityEditor.Animations.BlendTree source,
            AnimatorController targetController)
        {
            if (source == null) return null;

            // 创建新实例
            var newTree = new UnityEditor.Animations.BlendTree();
            
            // 复制属性
            newTree.name = source.name;
            newTree.blendType = source.blendType;
            newTree.blendParameter = source.blendParameter;
            newTree.blendParameterY = source.blendParameterY;
            newTree.minThreshold = source.minThreshold;
            newTree.maxThreshold = source.maxThreshold;
            newTree.useAutomaticThresholds = source.useAutomaticThresholds;
            
            // 立即添加到目标控制器资产中，确保持久化
            if (targetController != null)
            {
                AssetDatabase.AddObjectToAsset(newTree, targetController);
            }

            // 递归克隆子节点
            var children = source.children;
            var newChildren = new ChildMotion[children.Length];
            
            for (int i = 0; i < children.Length; i++)
            {
                newChildren[i] = children[i]; // 复制结构体
                
                // 如果子节点的 Motion 是 BlendTree，递归克隆
                if (newChildren[i].motion is UnityEditor.Animations.BlendTree childBt)
                {
                    newChildren[i].motion = CloneBlendTree(childBt, targetController);
                }
                // 如果是 AnimationClip，保持引用不变
            }

            newTree.children = newChildren;
            return newTree;
        }

        /// <summary>
        /// 收集控制器中的所有 BlendTree
        /// </summary>
        /// <param name="controller">目标控制器</param>
        /// <returns>BlendTree 列表（含路径信息）</returns>
        public static List<(string path, UnityEditor.Animations.BlendTree blendTree)> CollectAllBlendTrees(
            AnimatorController controller)
        {
            var result = new List<(string, UnityEditor.Animations.BlendTree)>();
            if (controller == null) return result;

            for (int i = 0; i < controller.layers.Length; i++)
            {
                var layer = controller.layers[i];
                CollectFromStateMachine(layer.stateMachine, layer.name, result);
            }
            return result;
        }

        private static void CollectFromStateMachine(AnimatorStateMachine sm, string path, List<(string, UnityEditor.Animations.BlendTree)> result)
        {
            if (sm == null) return;

            foreach (var childState in sm.states)
            {
                if (childState.state.motion is UnityEditor.Animations.BlendTree bt)
                {
                    // Use AnimatorPathUtility to combine paths correctly handles slashes in names
                    string statePath = AnimatorPathUtility.Combine(path, childState.state.name);
                    CollectFromBlendTree(bt, statePath, result);
                }
            }

            foreach (var childSm in sm.stateMachines)
            {
                string smPath = AnimatorPathUtility.Combine(path, childSm.stateMachine.name);
                CollectFromStateMachine(childSm.stateMachine, smPath, result);
            }
        }

        private static void CollectFromBlendTree(UnityEditor.Animations.BlendTree bt, string path, List<(string, UnityEditor.Animations.BlendTree)> result)
        {
            result.Add((path, bt));
            
            foreach (var child in bt.children)
            {
                if (child.motion is UnityEditor.Animations.BlendTree childBt)
                {
                    string childPath = AnimatorPathUtility.Combine(path, childBt.name);
                    CollectFromBlendTree(childBt, childPath, result);
                }
            }
        }
    }
}
