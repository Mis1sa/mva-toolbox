using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.Animation.Shared.ModularAvatar
{
    internal static class AnimationModularAvatarControllerBridge
    {
        private static Type _cachedMergeAnimatorType;
        private static FieldInfo _cachedMergeAnimatorAnimatorField;
        private static FieldInfo _cachedMergeAnimatorPathModeField;
        private static FieldInfo _cachedMergeAnimatorRelativePathRootField;
        private static bool _mergeAnimatorLookupAttempted;
        private static Type _cachedAvatarObjectReferenceType;
        private static MethodInfo _cachedAvatarObjectReferenceGetMethod;
        private static FieldInfo _cachedAvatarObjectReferenceReferencePathField;
        private static FieldInfo _cachedAvatarObjectReferenceTargetObjectField;
        private static string _cachedAvatarObjectReferenceAvatarRootToken;

        internal static void CollectMergeAnimatorControllersWithRoots(
            Transform traversalRoot,
            Transform avatarRoot,
            bool isAvatar,
            Action<AnimatorController, Transform> addController)
        {
            if (traversalRoot == null || addController == null || !EnsureMergeAnimatorReflection())
            {
                return;
            }

            Type mergeType = _cachedMergeAnimatorType;
            Dictionary<Transform, bool> mergePresenceCache = isAvatar ? new Dictionary<Transform, bool>() : null;
            Stack<Transform> stack = new Stack<Transform>();
            stack.Push(traversalRoot);

            while (stack.Count > 0)
            {
                Transform current = stack.Pop();
                if (current == null)
                {
                    continue;
                }

                bool isRootNode = current == traversalRoot;
                bool hasAnimator = current.GetComponent<Animator>() != null;
                if (!isRootNode && hasAnimator)
                {
                    if (!isAvatar)
                    {
                        continue;
                    }

                    if (mergePresenceCache != null && !SubtreeContainsMergeAnimator(current, mergeType, mergePresenceCache))
                    {
                        continue;
                    }
                }

                CollectMergeAnimatorOnTransform(current, avatarRoot ?? traversalRoot, mergeType, addController);

                for (int i = current.childCount - 1; i >= 0; i--)
                {
                    stack.Push(current.GetChild(i));
                }
            }
        }

        private static bool SubtreeContainsMergeAnimator(Transform node, Type mergeType, Dictionary<Transform, bool> cache)
        {
            if (node == null || mergeType == null)
            {
                return false;
            }

            if (cache != null && cache.TryGetValue(node, out bool cached))
            {
                return cached;
            }

            bool result = false;
            Stack<Transform> stack = new Stack<Transform>();
            stack.Push(node);

            while (stack.Count > 0)
            {
                Transform current = stack.Pop();
                if (current == null)
                {
                    continue;
                }

                Component[] components = current.GetComponents<Component>();
                for (int i = 0; i < components.Length; i++)
                {
                    Component component = components[i];
                    if (component != null && component.GetType() == mergeType)
                    {
                        result = true;
                        stack.Clear();
                        break;
                    }
                }

                if (!result)
                {
                    for (int i = 0; i < current.childCount; i++)
                    {
                        stack.Push(current.GetChild(i));
                    }
                }
            }

            if (cache != null)
            {
                cache[node] = result;
            }

            return result;
        }

        private static void CollectMergeAnimatorOnTransform(Transform node, Transform avatarRoot, Type mergeType, Action<AnimatorController, Transform> addController)
        {
            if (node == null || mergeType == null)
            {
                return;
            }

            Component[] components = node.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || component.GetType() != mergeType)
                {
                    continue;
                }

                AnimatorController controller = ExtractAnimatorController(component);
                if (controller == null)
                {
                    continue;
                }

                Transform rootTransform = ResolveMergeAnimatorRoot(component, avatarRoot, controller) ?? node;
                addController(controller, rootTransform);
            }
        }

        private static AnimatorController ExtractAnimatorController(Component mergeComponent)
        {
            if (mergeComponent == null || !EnsureMergeAnimatorReflection())
            {
                return null;
            }

            RuntimeAnimatorController runtimeController = _cachedMergeAnimatorAnimatorField?.GetValue(mergeComponent) as RuntimeAnimatorController;
            return NormalizeAnimatorController(runtimeController);
        }

        private static AnimatorController NormalizeAnimatorController(RuntimeAnimatorController runtimeController)
        {
            if (runtimeController is AnimatorController controller)
            {
                return controller;
            }

            if (runtimeController is AnimatorOverrideController overrideController)
            {
                return overrideController.runtimeAnimatorController as AnimatorController;
            }

            return null;
        }

        private static Transform ResolveMergeAnimatorRoot(Component mergeComponent, Transform avatarRoot, AnimatorController controller)
        {
            if (mergeComponent == null)
            {
                return null;
            }

            if (!EnsureMergeAnimatorReflection())
            {
                return mergeComponent.transform;
            }

            Transform resolved = null;
            object pathModeValue = _cachedMergeAnimatorPathModeField?.GetValue(mergeComponent);
            string pathModeName = pathModeValue?.ToString();
            if (string.Equals(pathModeName, "Relative", StringComparison.Ordinal))
            {
                object relativeRef = _cachedMergeAnimatorRelativePathRootField?.GetValue(mergeComponent);
                if (relativeRef != null && EnsureAvatarObjectReferenceReflection())
                {
                    try
                    {
                        if (_cachedAvatarObjectReferenceGetMethod != null)
                        {
                            GameObject obj = _cachedAvatarObjectReferenceGetMethod.Invoke(relativeRef, new object[] { mergeComponent }) as GameObject;
                            if (obj != null)
                            {
                                resolved = obj.transform;
                            }
                        }
                    }
                    catch
                    {
                    }

                    if (resolved == null && avatarRoot != null)
                    {
                        string referencePath = _cachedAvatarObjectReferenceReferencePathField?.GetValue(relativeRef) as string;
                        if (!string.IsNullOrEmpty(referencePath))
                        {
                            if (!string.IsNullOrEmpty(_cachedAvatarObjectReferenceAvatarRootToken) && referencePath == _cachedAvatarObjectReferenceAvatarRootToken)
                            {
                                resolved = avatarRoot;
                            }
                            else
                            {
                                Transform candidate = avatarRoot.Find(referencePath);
                                if (candidate != null)
                                {
                                    resolved = candidate;
                                }
                            }
                        }

                        if (resolved == null)
                        {
                            GameObject targetObject = _cachedAvatarObjectReferenceTargetObjectField?.GetValue(relativeRef) as GameObject;
                            if (targetObject != null)
                            {
                                resolved = targetObject.transform;
                            }
                        }
                    }
                }
            }
            else
            {
                resolved = avatarRoot;
            }

            if (resolved == null)
            {
                resolved = InferRootFromController(controller, mergeComponent.transform, avatarRoot);
            }

            return resolved ?? mergeComponent.transform;
        }

        private static Transform InferRootFromController(AnimatorController controller, Transform mergeTransform, Transform avatarRoot)
        {
            if (controller == null)
            {
                return mergeTransform ?? avatarRoot;
            }

            AnimationClip[] clips = controller.animationClips;
            if (clips == null || clips.Length == 0)
            {
                return mergeTransform ?? avatarRoot;
            }

            List<EditorCurveBinding> bindings = new List<EditorCurveBinding>();
            for (int i = 0; i < clips.Length; i++)
            {
                AnimationClip clip = clips[i];
                if (clip == null)
                {
                    continue;
                }

                bindings.AddRange(AnimationUtility.GetCurveBindings(clip));
                bindings.AddRange(AnimationUtility.GetObjectReferenceCurveBindings(clip));
            }

            if (bindings.Count == 0)
            {
                return mergeTransform ?? avatarRoot;
            }

            List<Transform> candidates = BuildCandidateRoots(mergeTransform, avatarRoot);
            if (candidates.Count == 0)
            {
                return mergeTransform ?? avatarRoot;
            }

            Transform best = null;
            int bestScore = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                Transform candidate = candidates[i];
                if (candidate == null)
                {
                    continue;
                }

                int score = 0;
                for (int j = 0; j < bindings.Count; j++)
                {
                    EditorCurveBinding binding = bindings[j];
                    if (!string.IsNullOrEmpty(binding.path) && candidate.Find(binding.path) != null)
                    {
                        score++;
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best != null && bestScore > 0 ? best : mergeTransform ?? avatarRoot;
        }

        private static List<Transform> BuildCandidateRoots(Transform mergeTransform, Transform avatarRoot)
        {
            List<Transform> result = new List<Transform>();
            HashSet<Transform> visited = new HashSet<Transform>();

            void AddCandidate(Transform transform)
            {
                if (transform != null && visited.Add(transform))
                {
                    result.Add(transform);
                }
            }

            Transform current = mergeTransform;
            while (current != null)
            {
                AddCandidate(current);
                if (current == avatarRoot)
                {
                    break;
                }

                current = current.parent;
            }

            AddCandidate(avatarRoot);
            return result;
        }

        private static bool EnsureMergeAnimatorReflection()
        {
            if (_cachedMergeAnimatorType != null && _cachedMergeAnimatorAnimatorField != null && _cachedMergeAnimatorPathModeField != null && _cachedMergeAnimatorRelativePathRootField != null)
            {
                return true;
            }

            if (_mergeAnimatorLookupAttempted)
            {
                return _cachedMergeAnimatorType != null && _cachedMergeAnimatorAnimatorField != null && _cachedMergeAnimatorPathModeField != null && _cachedMergeAnimatorRelativePathRootField != null;
            }

            _mergeAnimatorLookupAttempted = true;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (assembly == null)
                {
                    continue;
                }

                Type type = assembly.GetType("nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator");
                if (type != null)
                {
                    _cachedMergeAnimatorType = type;
                    _cachedMergeAnimatorAnimatorField = type.GetField("animator", BindingFlags.Public | BindingFlags.Instance);
                    _cachedMergeAnimatorPathModeField = type.GetField("pathMode", BindingFlags.Public | BindingFlags.Instance);
                    _cachedMergeAnimatorRelativePathRootField = type.GetField("relativePathRoot", BindingFlags.Public | BindingFlags.Instance);
                    break;
                }
            }

            return _cachedMergeAnimatorType != null && _cachedMergeAnimatorAnimatorField != null && _cachedMergeAnimatorPathModeField != null && _cachedMergeAnimatorRelativePathRootField != null;
        }

        private static bool EnsureAvatarObjectReferenceReflection()
        {
            if (_cachedAvatarObjectReferenceType != null && _cachedAvatarObjectReferenceGetMethod != null && _cachedAvatarObjectReferenceReferencePathField != null && _cachedAvatarObjectReferenceTargetObjectField != null)
            {
                return true;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length && _cachedAvatarObjectReferenceType == null; i++)
            {
                Assembly assembly = assemblies[i];
                if (assembly == null)
                {
                    continue;
                }

                Type type = assembly.GetType("nadena.dev.modular_avatar.core.AvatarObjectReference");
                if (type != null)
                {
                    _cachedAvatarObjectReferenceType = type;
                    _cachedAvatarObjectReferenceGetMethod = type.GetMethod("Get", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Component) }, null);
                    _cachedAvatarObjectReferenceReferencePathField = type.GetField("referencePath", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    _cachedAvatarObjectReferenceTargetObjectField = type.GetField("targetObject", BindingFlags.NonPublic | BindingFlags.Instance);
                    FieldInfo avatarRootField = type.GetField("AVATAR_ROOT", BindingFlags.Public | BindingFlags.Static);
                    if (avatarRootField != null)
                    {
                        _cachedAvatarObjectReferenceAvatarRootToken = avatarRootField.GetValue(null) as string;
                    }
                }
            }

            return _cachedAvatarObjectReferenceType != null && _cachedAvatarObjectReferenceGetMethod != null && _cachedAvatarObjectReferenceReferencePathField != null && _cachedAvatarObjectReferenceTargetObjectField != null;
        }
    }
}
