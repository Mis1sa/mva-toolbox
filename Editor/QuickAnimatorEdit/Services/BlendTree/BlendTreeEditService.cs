using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

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
    }
}
