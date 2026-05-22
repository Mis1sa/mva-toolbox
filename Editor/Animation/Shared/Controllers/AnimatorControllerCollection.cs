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
        internal bool IgnoresNestedAnimators;
    }

    internal static class AnimatorControllerCollection
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

        internal static List<string> BuildControllerDisplayNames(
            VRCAvatarDescriptor descriptor,
            Animator animator,
            List<AnimatorController> controllers,
            Dictionary<AnimatorController, ControllerWithRoot> controllerScopeMap = null)
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
                bool prefersMergeAnimatorLabel = controllerScopeMap != null
                    && controllerScopeMap.TryGetValue(controller, out ControllerWithRoot controllerScope)
                    && controllerScope.IgnoresNestedAnimators;

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

                if (label == null && prefersMergeAnimatorLabel)
                {
                    label = "MA Merge Animator: " + controller.name;
                }

                if (label == null && animator != null && animator.runtimeAnimatorController == controller)
                {
                    label = "Animator: " + controller.name;
                }

                if (string.IsNullOrEmpty(label))
                {
                    label = "MA Merge Animator: " + controller.name;
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
            Dictionary<AnimatorController, int> controllerIndices = new Dictionary<AnimatorController, int>();
            Transform rootTransform = root.transform;
            Transform avatarRootTransform = FindAvatarRootTransform(rootTransform) ?? rootTransform;

            void AddController(AnimatorController controller, Transform controllerRoot, bool ignoresNestedAnimators = false)
            {
                if (controller == null || controllerRoot == null)
                {
                    return;
                }

                ControllerWithRoot candidate = new ControllerWithRoot
                {
                    Controller = controller,
                    RootTransform = controllerRoot,
                    IgnoresNestedAnimators = ignoresNestedAnimators
                };

                if (controllerIndices.TryGetValue(controller, out int existingIndex))
                {
                    ControllerWithRoot existing = result[existingIndex];
                    if (!ShouldReplaceControllerScope(existing, candidate, rootTransform, isAvatar))
                    {
                        return;
                    }

                    result[existingIndex] = candidate;
                    return;
                }

                controllerIndices[controller] = result.Count;
                result.Add(candidate);
            }

            if (descriptor != null)
            {
                VRCAvatarDescriptor.CustomAnimLayer[] baseLayers = descriptor.baseAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                for (int i = 0; i < baseLayers.Length; i++)
                {
                    if (baseLayers[i].animatorController is AnimatorController controller)
                    {
                        AddController(controller, rootTransform, false);
                    }
                }

                if (includeSpecialLayers)
                {
                    VRCAvatarDescriptor.CustomAnimLayer[] specialLayers = descriptor.specialAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                    for (int i = 0; i < specialLayers.Length; i++)
                    {
                        if (specialLayers[i].animatorController is AnimatorController controller)
                        {
                            AddController(controller, rootTransform, false);
                        }
                    }
                }
            }

            Animator animator = root.GetComponent<Animator>();
            bool preferMergeAnimatorForRootAnimator = !isAvatar && animator != null;
            if (preferMergeAnimatorForRootAnimator)
            {
                AnimationModularAvatarControllerBridge.CollectMergeAnimatorControllersWithRoots(rootTransform, avatarRootTransform, isAvatar, allowAnimatorSubtree, AddController);
            }

            if (animator != null && animator.runtimeAnimatorController is AnimatorController runtimeController)
            {
                AddController(runtimeController, rootTransform, false);
            }

            if (!preferMergeAnimatorForRootAnimator)
            {
                AnimationModularAvatarControllerBridge.CollectMergeAnimatorControllersWithRoots(rootTransform, avatarRootTransform, isAvatar, allowAnimatorSubtree, AddController);
            }

            return result;
        }

        private static bool ShouldReplaceControllerScope(ControllerWithRoot existing, ControllerWithRoot candidate, Transform traversalRoot, bool isAvatar)
        {
            bool existingIsRootNonMma = !existing.IgnoresNestedAnimators && existing.RootTransform == traversalRoot;
            bool candidateIsRootNonMma = !candidate.IgnoresNestedAnimators && candidate.RootTransform == traversalRoot;

            if (candidate.IgnoresNestedAnimators && existingIsRootNonMma)
            {
                return candidate.RootTransform != traversalRoot || !isAvatar;
            }

            if (candidateIsRootNonMma && existing.IgnoresNestedAnimators)
            {
                return existing.RootTransform == traversalRoot && isAvatar;
            }

            int existingDepth = GetRelativeDepth(traversalRoot, existing.RootTransform);
            int candidateDepth = GetRelativeDepth(traversalRoot, candidate.RootTransform);
            if (candidateDepth != existingDepth)
            {
                return candidateDepth > existingDepth;
            }

            if (candidate.IgnoresNestedAnimators != existing.IgnoresNestedAnimators)
            {
                return candidate.IgnoresNestedAnimators;
            }

            return false;
        }

        private static int GetRelativeDepth(Transform root, Transform target)
        {
            if (root == null || target == null)
            {
                return int.MinValue;
            }

            int depth = 0;
            Transform current = target;
            while (current != null)
            {
                if (current == root)
                {
                    return depth;
                }

                current = current.parent;
                depth++;
            }

            return -1;
        }

        private static Transform FindAvatarRootTransform(Transform current)
        {
            while (current != null)
            {
                if (current.GetComponent<VRCAvatarDescriptor>() != null)
                {
                    return current;
                }

                current = current.parent;
            }

            return null;
        }
    }
}
