using System;
using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace MVA.Toolbox.AnimatorShared.Targeting
{
    internal sealed class AnimatorControllerCollectionResult
    {
        internal readonly List<AnimatorController> Controllers = new List<AnimatorController>();
        internal readonly List<string> ControllerNames = new List<string>();
        internal int SuggestedSelectedIndex;
    }

    internal static class AnimatorControllerCollector
    {
        internal static AnimatorControllerCollectionResult CollectControllers(
            UnityEngine.Object targetObject,
            VRCAvatarDescriptor avatarDescriptor,
            Animator primaryAnimator,
            GameObject root)
        {
            var result = new AnimatorControllerCollectionResult();
            if (targetObject == null)
            {
                return result;
            }

            if (targetObject is AnimatorController controllerAsset)
            {
                result.Controllers.Add(controllerAsset);
                result.ControllerNames.Add(controllerAsset.name);
                return result;
            }

            if (root == null)
            {
                return result;
            }

            var seen = new HashSet<AnimatorController>();
            if (avatarDescriptor != null)
            {
                AppendAvatarControllers(result, avatarDescriptor.baseAnimationLayers, seen);
                AppendAvatarControllers(result, avatarDescriptor.specialAnimationLayers, seen);
            }

            Animator[] animators = root.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                Animator animator = animators[i];
                if (animator == null)
                {
                    continue;
                }

                AnimatorController controller = animator.runtimeAnimatorController as AnimatorController;
                if (controller == null)
                {
                    continue;
                }

                string label;
                if (animator == primaryAnimator)
                {
                    label = "Animator: " + controller.name;
                }
                else
                {
                    string path = BuildTransformPath(animator.transform, root.transform);
                    label = string.IsNullOrEmpty(path)
                        ? "Animator: " + controller.name
                        : $"Animator({path}): {controller.name}";
                }

                TryAddController(result, controller, label, seen);
            }

            if (avatarDescriptor != null)
            {
                AnimatorController fxController = GetExistingFxController(avatarDescriptor);
                if (fxController != null)
                {
                    int fxIndex = result.Controllers.FindIndex(c => c == fxController);
                    if (fxIndex >= 0)
                    {
                        result.SuggestedSelectedIndex = fxIndex;
                    }
                }
            }

            return result;
        }

        internal static void AppendAvatarControllers(
            AnimatorControllerCollectionResult result,
            VRCAvatarDescriptor.CustomAnimLayer[] layers,
            HashSet<AnimatorController> seen)
        {
            if (layers == null)
            {
                return;
            }

            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].animatorController is AnimatorController controller)
                {
                    TryAddController(result, controller, $"{layers[i].type}: {controller.name}", seen);
                }
            }
        }

        internal static void TryAddController(
            AnimatorControllerCollectionResult result,
            AnimatorController controller,
            string label,
            HashSet<AnimatorController> seen)
        {
            if (result == null || controller == null || seen == null || !seen.Add(controller))
            {
                return;
            }

            result.Controllers.Add(controller);
            result.ControllerNames.Add(string.IsNullOrEmpty(label) ? controller.name : label);
        }

        internal static AnimatorController GetExistingFxController(VRCAvatarDescriptor avatar)
        {
            if (avatar == null)
            {
                return null;
            }

            VRCAvatarDescriptor.CustomAnimLayer[] layers = avatar.baseAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].type == VRCAvatarDescriptor.AnimLayerType.FX && layers[i].animatorController is AnimatorController controller)
                {
                    return controller;
                }
            }

            return null;
        }

        internal static string BuildTransformPath(Transform target, Transform root)
        {
            if (target == null || root == null || target == root)
            {
                return string.Empty;
            }

            var segments = new List<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                segments.Add(current.name);
                current = current.parent;
            }

            if (current != root)
            {
                return target.name;
            }

            segments.Reverse();
            return string.Join("/", segments);
        }
    }
}
