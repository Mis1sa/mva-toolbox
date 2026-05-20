using System;
using System.Collections.Generic;
using MVA.Toolbox.Animation.Shared.ModularAvatar;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace MVA.Toolbox.Animation.Shared.Controllers
{
    internal struct ControllerWithRoot
    {
        internal AnimatorController Controller;
        internal Transform RootTransform;
    }

    internal static class AnimationControllerCollection
    {
        internal static AnimatorController GetExistingFXController(VRCAvatarDescriptor avatar)
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

        internal static List<string> BuildControllerDisplayNames(VRCAvatarDescriptor descriptor, Animator animator, List<AnimatorController> controllers)
        {
            List<string> names = new List<string>();
            if (controllers == null || controllers.Count == 0)
            {
                return names;
            }

            for (int i = 0; i < controllers.Count; i++)
            {
                AnimatorController controller = controllers[i];
                if (controller == null)
                {
                    names.Add("(Missing Controller)");
                    continue;
                }

                if (!string.IsNullOrEmpty(controller.name) && controller.name.StartsWith("[MA Parameters]", StringComparison.Ordinal))
                {
                    names.Add(controller.name);
                    continue;
                }

                string label = null;
                if (descriptor != null)
                {
                    VRCAvatarDescriptor.CustomAnimLayer[] baseLayers = descriptor.baseAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                    for (int j = 0; j < baseLayers.Length; j++)
                    {
                        VRCAvatarDescriptor.CustomAnimLayer layer = baseLayers[j];
                        if (layer.animatorController == controller)
                        {
                            label = $"{layer.type}: {controller.name}";
                            break;
                        }
                    }

                    if (label == null)
                    {
                        VRCAvatarDescriptor.CustomAnimLayer[] specialLayers = descriptor.specialAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                        for (int j = 0; j < specialLayers.Length; j++)
                        {
                            VRCAvatarDescriptor.CustomAnimLayer layer = specialLayers[j];
                            if (layer.animatorController == controller)
                            {
                                label = $"{layer.type}: {controller.name}";
                                break;
                            }
                        }
                    }
                }

                if (label == null && animator != null && animator.runtimeAnimatorController == controller)
                {
                    label = "Animator: " + controller.name;
                }

                if (string.IsNullOrEmpty(label))
                {
                    label = "Animator Controller: " + controller.name;
                }

                names.Add(label);
            }

            return names;
        }

        internal static List<ControllerWithRoot> CollectControllersWithRoot(GameObject root, bool includeSpecialLayers = true, bool allowAnimatorSubtree = true)
        {
            List<ControllerWithRoot> result = new List<ControllerWithRoot>();
            if (root == null)
            {
                return result;
            }

            VRCAvatarDescriptor descriptor = root.GetComponent<VRCAvatarDescriptor>();
            bool isAvatar = descriptor != null;
            bool traversalAvatarMode = isAvatar && allowAnimatorSubtree;
            HashSet<AnimatorController> seen = new HashSet<AnimatorController>();
            Transform rootTransform = root.transform;

            void AddController(AnimatorController controller, Transform controllerRoot)
            {
                if (controller == null || controllerRoot == null || !seen.Add(controller))
                {
                    return;
                }

                result.Add(new ControllerWithRoot
                {
                    Controller = controller,
                    RootTransform = controllerRoot
                });
            }

            if (descriptor != null)
            {
                VRCAvatarDescriptor.CustomAnimLayer[] baseLayers = descriptor.baseAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                for (int i = 0; i < baseLayers.Length; i++)
                {
                    if (baseLayers[i].animatorController is AnimatorController controller)
                    {
                        AddController(controller, rootTransform);
                    }
                }

                if (includeSpecialLayers)
                {
                    VRCAvatarDescriptor.CustomAnimLayer[] specialLayers = descriptor.specialAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                    for (int i = 0; i < specialLayers.Length; i++)
                    {
                        if (specialLayers[i].animatorController is AnimatorController controller)
                        {
                            AddController(controller, rootTransform);
                        }
                    }
                }
            }

            Animator animator = root.GetComponent<Animator>();
            if (animator != null && animator.runtimeAnimatorController is AnimatorController runtimeController)
            {
                AddController(runtimeController, rootTransform);
            }

            AnimationModularAvatarControllerBridge.CollectMergeAnimatorControllersWithRoots(rootTransform, rootTransform, traversalAvatarMode, AddController);
            return result;
        }
    }
}
