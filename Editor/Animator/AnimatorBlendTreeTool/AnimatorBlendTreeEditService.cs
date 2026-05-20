using UnityEditor;
using UnityEditor.Animations;

namespace MVA.Toolbox.AnimatorBlendTreeTool
{
    internal static class AnimatorBlendTreeEditService
    {
        internal static UnityEditor.Animations.BlendTree CreateBlendTree(AnimatorController controller, string name)
        {
            var newTree = new UnityEditor.Animations.BlendTree();
            newTree.name = name;

            if (controller != null)
            {
                AssetDatabase.AddObjectToAsset(newTree, controller);
            }

            return newTree;
        }

        internal static UnityEditor.Animations.BlendTree CloneBlendTree(
            UnityEditor.Animations.BlendTree source,
            AnimatorController targetController)
        {
            if (source == null)
            {
                return null;
            }

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

            ChildMotion[] children = source.children;
            var newChildren = new ChildMotion[children.Length];
            for (int i = 0; i < children.Length; i++)
            {
                newChildren[i] = children[i];
                var childTree = newChildren[i].motion as UnityEditor.Animations.BlendTree;
                if (childTree != null)
                {
                    newChildren[i].motion = CloneBlendTree(childTree, targetController);
                }
            }

            newTree.children = newChildren;
            return newTree;
        }
    }
}
