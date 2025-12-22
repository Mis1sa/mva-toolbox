using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using MVA.Toolbox.QuickAnimatorEdit.Services.Shared;

namespace MVA.Toolbox.QuickAnimatorEdit.Services.BlendTree
{
    /// <summary>
    /// 混合树编辑服务
    /// 增删中间节点、调整子节点位置/顺序
    /// </summary>
    public static class BlendTreeEditService
    {
        /// <summary>
        /// 在指定位置插入子节点
        /// </summary>
        public static bool InsertChild(
            UnityEditor.Animations.BlendTree blendTree,
            int index,
            Motion motion,
            float threshold = 0f,
            Vector2 position = default)
        {
            if (blendTree == null) return false;

            Undo.RecordObject(blendTree, "BlendTree Insert Child");

            var childrenList = blendTree.children.ToList();
            var newChild = new ChildMotion
            {
                motion = motion,
                threshold = threshold,
                position = position,
                timeScale = 1f,
                directBlendParameter = "Blend"
            };

            if (index < 0) index = 0;
            if (index > childrenList.Count) index = childrenList.Count;

            childrenList.Insert(index, newChild);
            blendTree.children = childrenList.ToArray();
            
            EditorUtility.SetDirty(blendTree);
            return true;
        }

        /// <summary>
        /// 移除指定位置的子节点
        /// </summary>
        public static bool RemoveChild(UnityEditor.Animations.BlendTree blendTree, int index)
        {
            if (blendTree == null) return false;
            var children = blendTree.children;
            if (index < 0 || index >= children.Length) return false;

            Undo.RecordObject(blendTree, "BlendTree Remove Child");

            var list = children.ToList();
            list.RemoveAt(index);
            blendTree.children = list.ToArray();

            EditorUtility.SetDirty(blendTree);
            return true;
        }

        /// <summary>
        /// 移动子节点位置
        /// </summary>
        public static bool MoveChild(UnityEditor.Animations.BlendTree blendTree, int fromIndex, int toIndex)
        {
            if (blendTree == null) return false;
            var children = blendTree.children;
            if (fromIndex < 0 || fromIndex >= children.Length) return false;
            if (toIndex < 0 || toIndex >= children.Length) return false;
            if (fromIndex == toIndex) return true;

            Undo.RecordObject(blendTree, "BlendTree Reorder Child");

            var list = children.ToList();
            var item = list[fromIndex];
            list.RemoveAt(fromIndex);
            list.Insert(toIndex, item);
            blendTree.children = list.ToArray();

            EditorUtility.SetDirty(blendTree);
            return true;
        }

        /// <summary>
        /// 修改子节点的阈值（1D BlendTree）
        /// </summary>
        public static bool SetChildThreshold(UnityEditor.Animations.BlendTree blendTree, int index, float newThreshold)
        {
            if (blendTree == null) return false;
            var children = blendTree.children;
            if (index < 0 || index >= children.Length) return false;

            Undo.RecordObject(blendTree, "BlendTree Set Threshold");

            var child = children[index];
            child.threshold = newThreshold;
            children[index] = child;
            blendTree.children = children;

            EditorUtility.SetDirty(blendTree);
            return true;
        }

        /// <summary>
        /// 修改子节点的位置（2D BlendTree）
        /// </summary>
        public static bool SetChildPosition(UnityEditor.Animations.BlendTree blendTree, int index, Vector2 newPosition)
        {
            if (blendTree == null) return false;
            var children = blendTree.children;
            if (index < 0 || index >= children.Length) return false;

            Undo.RecordObject(blendTree, "BlendTree Set Position");

            var child = children[index];
            child.position = newPosition;
            children[index] = child;
            blendTree.children = children;

            EditorUtility.SetDirty(blendTree);
            return true;
        }

        /// <summary>
        /// 获取混合树的所有子节点信息
        /// </summary>
        public static List<ChildMotion> GetChildren(UnityEditor.Animations.BlendTree blendTree)
        {
            if (blendTree == null) return new List<ChildMotion>();
            return blendTree.children.ToList();
        }
        
        /// <summary>
        /// 创建一个新的 BlendTree 资产
        /// </summary>
        public static UnityEditor.Animations.BlendTree CreateBlendTree(AnimatorController controller, string name)
        {
            var newTree = new UnityEditor.Animations.BlendTree();
            newTree.name = name;
            
            if (controller != null)
            {
                AssetDatabase.AddObjectToAsset(newTree, controller);
            }
            
            return newTree;
        }

        /// <summary>
        /// 转移模式
        /// </summary>
        public enum TransferMode
        {
            Copy,
            Move
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
        /// 将 BlendTree 转移到另一状态
        /// </summary>
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
        /// 深拷贝 BlendTree
        /// </summary>
        public static UnityEditor.Animations.BlendTree CloneBlendTree(
            UnityEditor.Animations.BlendTree source,
            AnimatorController targetController)
        {
            if (source == null) return null;

            var newTree = new UnityEditor.Animations.BlendTree
            {
                name = source.name,
                blendType = source.blendType,
                blendParameter = source.blendParameter,
                blendParameterY = source.blendParameterY,
                minThreshold = source.minThreshold,
                maxThreshold = source.maxThreshold,
                useAutomaticThresholds = source.useAutomaticThresholds
            };

            if (targetController != null)
            {
                AssetDatabase.AddObjectToAsset(newTree, targetController);
            }

            var children = source.children;
            var newChildren = new ChildMotion[children.Length];

            for (int i = 0; i < children.Length; i++)
            {
                newChildren[i] = children[i];
                if (newChildren[i].motion is UnityEditor.Animations.BlendTree childBt)
                {
                    newChildren[i].motion = CloneBlendTree(childBt, targetController);
                }
            }

            newTree.children = newChildren;
            return newTree;
        }

        /// <summary>
        /// 收集控制器内所有 BlendTree
        /// </summary>
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

        private static void CollectFromStateMachine(
            AnimatorStateMachine sm,
            string path,
            List<(string, UnityEditor.Animations.BlendTree)> result)
        {
            if (sm == null) return;

            foreach (var childState in sm.states)
            {
                if (childState.state.motion is UnityEditor.Animations.BlendTree bt)
                {
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

        private static void CollectFromBlendTree(
            UnityEditor.Animations.BlendTree bt,
            string path,
            List<(string, UnityEditor.Animations.BlendTree)> result)
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
