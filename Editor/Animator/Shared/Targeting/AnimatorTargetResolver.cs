using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.AnimatorShared.Targeting
{
    internal static class AnimatorTargetResolver
    {
        internal static VRCAvatarDescriptor ResolveAvatarDescriptor(Object target)
        {
            if (target is VRCAvatarDescriptor descriptor)
            {
                return descriptor;
            }

            if (target is GameObject go)
            {
                return go.GetComponent<VRCAvatarDescriptor>() ?? go.GetComponentInParent<VRCAvatarDescriptor>(true);
            }

            if (target is Component component)
            {
                return component.GetComponent<VRCAvatarDescriptor>() ?? component.GetComponentInParent<VRCAvatarDescriptor>(true);
            }

            return null;
        }

        internal static VRCAvatarDescriptor ResolveDirectAvatarDescriptor(Object target)
        {
            if (target is VRCAvatarDescriptor descriptor)
            {
                return descriptor;
            }

            if (target is GameObject go)
            {
                return go.GetComponent<VRCAvatarDescriptor>();
            }

            if (target is Component component)
            {
                return component.GetComponent<VRCAvatarDescriptor>();
            }

            return null;
        }

        internal static Animator ResolveAnimator(Object target)
        {
            if (target is Animator animator)
            {
                return animator;
            }

            if (target is GameObject go)
            {
                return go.GetComponent<Animator>() ?? go.GetComponentInParent<Animator>(true);
            }

            if (target is Component component)
            {
                return component.GetComponent<Animator>() ?? component.GetComponentInParent<Animator>(true);
            }

            return null;
        }

        internal static GameObject ResolveTargetGameObject(Object target)
        {
            if (target is GameObject go)
            {
                return go;
            }

            if (target is Component component)
            {
                return component.gameObject;
            }

            return null;
        }

        internal static GameObject ResolveControllerScanRoot(Object target, VRCAvatarDescriptor avatarDescriptor, Animator animator)
        {
            if (avatarDescriptor != null && avatarDescriptor.gameObject != null)
            {
                return avatarDescriptor.gameObject;
            }

            if (animator != null && animator.gameObject != null)
            {
                return animator.gameObject;
            }

            return ResolveTargetGameObject(target);
        }
    }
}
