using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using MVA.Toolbox.Public;

namespace MVA.Toolbox.AnimFixUtility.Services
{
    /// <summary>
    /// AnimFix Utility 共享上下文：负责管理 Avatar / Animator 目标与控制器列表。
    /// </summary>
    public class AnimFixUtilityContext
    {
        private GameObject _targetRoot;
        private VRCAvatarDescriptor _avatarDescriptor;
        private Animator _animator;

        private readonly List<AnimatorController> _controllers = new List<AnimatorController>();
        private readonly List<string> _controllerNames = new List<string>();
        private readonly Dictionary<AnimatorController, Transform> _controllerRootMap = new Dictionary<AnimatorController, Transform>();
        private int _selectedControllerIndex;
        private int _selectedLayerIndex = -1;
        private bool _allowAnimatorSubtree = true;

        public GameObject TargetRoot => _targetRoot;
        public VRCAvatarDescriptor AvatarDescriptor => _avatarDescriptor;
        public Animator Animator => _animator;

        public IReadOnlyList<AnimatorController> Controllers => _controllers;
        public IReadOnlyList<string> ControllerNames => _controllerNames;
        public Transform SelectedControllerRoot => GetControllerRoot(SelectedController);

        public AnimatorController SelectedController
        {
            get
            {
                if (_controllers.Count == 0 || _selectedControllerIndex < 0) return null;
                int index = Mathf.Clamp(_selectedControllerIndex, 0, _controllers.Count - 1);
                return _controllers[index];
            }
        }

        public Transform GetControllerRoot(AnimatorController controller)
        {
            if (controller == null) return null;
            return _controllerRootMap.TryGetValue(controller, out var root) ? root : null;
        }

        public void SetAllowAnimatorSubtree(bool allow)
        {
            if (_allowAnimatorSubtree == allow) return;
            _allowAnimatorSubtree = allow;
            RefreshControllers();
        }

        public int SelectedControllerIndex
        {
            get
            {
                if (_controllers.Count == 0) return 0;
                return Mathf.Clamp(_selectedControllerIndex, -1, Mathf.Max(0, _controllers.Count - 1));
            }
            set
            {
                int min = _controllers.Count > 0 ? -1 : 0;
                int max = Mathf.Max(0, _controllers.Count - 1);
                int clamped = Mathf.Clamp(value, min, max);
                if (_controllers.Count == 0)
                {
                    clamped = 0;
                }
                if (clamped != _selectedControllerIndex)
                {
                    _selectedControllerIndex = clamped;
                    _selectedLayerIndex = -1;
                }
            }
        }

        public bool HasValidTarget => _targetRoot != null;

        public int SelectedLayerIndex
        {
            get => _selectedLayerIndex;
            set => _selectedLayerIndex = ClampLayerIndex(value);
        }

        public AnimatorControllerLayer SelectedLayer
        {
            get
            {
                var controller = SelectedController;
                if (controller == null || controller.layers == null || controller.layers.Length == 0)
                    return null;

                if (_selectedLayerIndex < 0)
                    return null;

                int index = Mathf.Clamp(_selectedLayerIndex, 0, controller.layers.Length - 1);
                return controller.layers[index];
            }
        }

        public bool TrySetTarget(GameObject newTarget)
        {
            if (newTarget == null)
            {
                ClearTarget();
                return true;
            }

            if (!ToolboxUtils.IsAvatarRoot(newTarget) && !ToolboxUtils.HasAnimator(newTarget))
            {
                return false;
            }

            _targetRoot = newTarget;
            RefreshTargetComponents();
            RefreshControllers();
            return true;
        }

        public void RefreshTargetComponents()
        {
            _avatarDescriptor = null;
            _animator = null;

            if (_targetRoot == null) return;

            _avatarDescriptor = _targetRoot.GetComponent<VRCAvatarDescriptor>();
            _animator = _targetRoot.GetComponent<Animator>();
        }

        public void RefreshControllers()
        {
            _controllers.Clear();
            _controllerNames.Clear();
            _controllerRootMap.Clear();
            _selectedControllerIndex = 0;
            _selectedLayerIndex = -1;

            if (_targetRoot == null) return;

            var controllerEntries = AnimFixControllerScanUtility.CollectControllersWithRoot(
                _targetRoot,
                includeSpecialLayers: true,
                allowAnimatorSubtree: _allowAnimatorSubtree);
            for (int i = 0; i < controllerEntries.Count; i++)
            {
                var entry = controllerEntries[i];
                if (entry.Controller == null) continue;
                _controllers.Add(entry.Controller);
                if (entry.RootTransform != null)
                {
                    _controllerRootMap[entry.Controller] = entry.RootTransform;
                }
            }
            if (_controllers.Count == 0) return;

            _controllerNames.AddRange(ToolboxUtils.BuildControllerDisplayNames(_avatarDescriptor, _animator, _controllers));

            // Avatar 默认优先选择 FX 控制器
            if (_avatarDescriptor != null)
            {
                var fxController = ToolboxUtils.GetExistingFXController(_avatarDescriptor);
                if (fxController != null)
                {
                    int index = _controllers.IndexOf(fxController);
                    if (index >= 0)
                    {
                        _selectedControllerIndex = index;
                        _selectedLayerIndex = -1;
                    }
                }
            }
        }

        public string[] BuildControllerNameArray()
        {
            if (_controllerNames.Count == _controllers.Count && _controllerNames.Count > 0)
            {
                return _controllerNames.ToArray();
            }

            var names = new string[_controllers.Count];
            for (int i = 0; i < _controllers.Count; i++)
            {
                names[i] = _controllers[i] != null ? _controllers[i].name : "(Controller)";
            }

            return names;
        }

        public void ClearTarget()
        {
            _targetRoot = null;
            _avatarDescriptor = null;
            _animator = null;
            _controllers.Clear();
            _controllerNames.Clear();
            _controllerRootMap.Clear();
            _selectedControllerIndex = 0;
            _selectedLayerIndex = -1;
        }

        private int ClampLayerIndex(int value)
        {
            var controller = SelectedController;
            if (controller == null || controller.layers == null || controller.layers.Length == 0)
                return -1;

            if (value < -1)
                return -1;

            if (value >= controller.layers.Length)
                return controller.layers.Length - 1;

            return value;
        }
    }

    internal static class AnimFixControllerScanUtility
    {
        internal struct ControllerWithRoot
        {
            public AnimatorController Controller;
            public Transform RootTransform;
        }

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

        public static List<ControllerWithRoot> CollectControllersWithRoot(
            GameObject root,
            bool includeSpecialLayers = true,
            bool allowAnimatorSubtree = true)
        {
            var result = new List<ControllerWithRoot>();
            if (root == null) return result;

            var descriptor = root.GetComponent<VRCAvatarDescriptor>();
            bool isAvatar = descriptor != null;
            bool traversalAvatarMode = isAvatar && allowAnimatorSubtree;
            var seen = new HashSet<AnimatorController>();
            var rootTransform = root.transform;

            void AddController(AnimatorController controller, Transform controllerRoot)
            {
                if (controller == null || controllerRoot == null) return;
                if (seen.Add(controller))
                {
                    result.Add(new ControllerWithRoot
                    {
                        Controller = controller,
                        RootTransform = controllerRoot
                    });
                }
            }

            if (descriptor != null)
            {
                var baseLayers = descriptor.baseAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                for (int i = 0; i < baseLayers.Length; i++)
                {
                    var layer = baseLayers[i];
                    if (layer.animatorController is AnimatorController ac)
                    {
                        AddController(ac, rootTransform);
                    }
                }

                if (includeSpecialLayers)
                {
                    var specialLayers = descriptor.specialAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                    for (int i = 0; i < specialLayers.Length; i++)
                    {
                        var layer = specialLayers[i];
                        if (layer.animatorController is AnimatorController ac)
                        {
                            AddController(ac, rootTransform);
                        }
                    }
                }
            }

            var animator = root.GetComponent<Animator>();
            if (animator != null && animator.runtimeAnimatorController is AnimatorController runtimeController)
            {
                AddController(runtimeController, rootTransform);
            }

            CollectMergeAnimatorControllersWithRoots(rootTransform, rootTransform, traversalAvatarMode, AddController);
            return result;
        }

        private static void CollectMergeAnimatorControllersWithRoots(
            Transform traversalRoot,
            Transform avatarRoot,
            bool isAvatar,
            Action<AnimatorController, Transform> addController)
        {
            if (traversalRoot == null || addController == null) return;
            if (!EnsureMergeAnimatorReflection()) return;

            var mergeType = _cachedMergeAnimatorType;
            var mergePresenceCache = isAvatar ? new Dictionary<Transform, bool>() : null;

            var stack = new Stack<Transform>();
            stack.Push(traversalRoot);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current == null) continue;

                bool isRootNode = current == traversalRoot;
                bool hasAnimator = current.GetComponent<Animator>() != null;

                if (!isRootNode && hasAnimator)
                {
                    if (!isAvatar)
                    {
                        continue;
                    }

                    if (mergePresenceCache != null &&
                        !SubtreeContainsMergeAnimator(current, mergeType, mergePresenceCache))
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

        private static bool SubtreeContainsMergeAnimator(
            Transform node,
            Type mergeType,
            Dictionary<Transform, bool> cache)
        {
            if (node == null || mergeType == null) return false;
            if (cache != null && cache.TryGetValue(node, out var cachedResult))
            {
                return cachedResult;
            }

            bool result = false;
            var stack = new Stack<Transform>();
            stack.Push(node);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current == null) continue;

                var components = current.GetComponents<Component>();
                for (int i = 0; i < components.Length; i++)
                {
                    var comp = components[i];
                    if (comp != null && comp.GetType() == mergeType)
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

        private static void CollectMergeAnimatorOnTransform(
            Transform node,
            Transform avatarRoot,
            Type mergeType,
            Action<AnimatorController, Transform> addController)
        {
            if (node == null || mergeType == null) return;

            var components = node.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                var comp = components[i];
                if (comp == null || comp.GetType() != mergeType) continue;

                var controller = ExtractAnimatorController(comp);
                if (controller == null) continue;

                var rootTransform = ResolveMergeAnimatorRoot(comp, avatarRoot, controller) ?? node;
                addController(controller, rootTransform);
            }
        }

        private static AnimatorController ExtractAnimatorController(Component mergeComponent)
        {
            if (!EnsureMergeAnimatorReflection() || mergeComponent == null) return null;
            var runtimeController = _cachedMergeAnimatorAnimatorField?.GetValue(mergeComponent) as RuntimeAnimatorController;
            return NormalizeAnimatorController(runtimeController);
        }

        private static AnimatorController NormalizeAnimatorController(RuntimeAnimatorController runtimeController)
        {
            if (runtimeController is AnimatorController ac) return ac;
            if (runtimeController is AnimatorOverrideController aoc)
            {
                return aoc.runtimeAnimatorController as AnimatorController;
            }
            return null;
        }

        private static Transform ResolveMergeAnimatorRoot(Component mergeComponent, Transform avatarRoot, AnimatorController controller)
        {
            if (mergeComponent == null) return null;
            if (!EnsureMergeAnimatorReflection()) return mergeComponent.transform;

            Transform resolved = null;
            var pathModeValue = _cachedMergeAnimatorPathModeField?.GetValue(mergeComponent);
            string pathModeName = pathModeValue?.ToString();

            if (string.Equals(pathModeName, "Relative", StringComparison.Ordinal))
            {
                var relativeRef = _cachedMergeAnimatorRelativePathRootField?.GetValue(mergeComponent);
                if (relativeRef != null && EnsureAvatarObjectReferenceReflection())
                {
                    try
                    {
                        if (_cachedAvatarObjectReferenceGetMethod != null)
                        {
                            var obj = _cachedAvatarObjectReferenceGetMethod.Invoke(relativeRef, new object[] { mergeComponent }) as GameObject;
                            if (obj != null)
                            {
                                resolved = obj.transform;
                            }
                        }
                    }
                    catch
                    {
                        // ignore reflection errors, fallback below
                    }

                    if (resolved == null && avatarRoot != null)
                    {
                        var referencePath = _cachedAvatarObjectReferenceReferencePathField?.GetValue(relativeRef) as string;
                        if (!string.IsNullOrEmpty(referencePath))
                        {
                            if (!string.IsNullOrEmpty(_cachedAvatarObjectReferenceAvatarRootToken) &&
                                referencePath == _cachedAvatarObjectReferenceAvatarRootToken)
                            {
                                resolved = avatarRoot;
                            }
                            else
                            {
                                var candidate = avatarRoot.Find(referencePath);
                                if (candidate != null)
                                {
                                    resolved = candidate;
                                }
                            }
                        }

                        if (resolved == null)
                        {
                            var targetObject = _cachedAvatarObjectReferenceTargetObjectField?.GetValue(relativeRef) as GameObject;
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
                // Absolute 模式默认以 Avatar 根为基准
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

            var clips = controller.animationClips;
            if (clips == null || clips.Length == 0)
            {
                return mergeTransform ?? avatarRoot;
            }

            var bindings = new List<EditorCurveBinding>();
            for (int i = 0; i < clips.Length; i++)
            {
                var clip = clips[i];
                if (clip == null) continue;
                bindings.AddRange(AnimationUtility.GetCurveBindings(clip));
                bindings.AddRange(AnimationUtility.GetObjectReferenceCurveBindings(clip));
            }

            if (bindings.Count == 0)
            {
                return mergeTransform ?? avatarRoot;
            }

            var candidates = BuildCandidateRoots(mergeTransform, avatarRoot);
            if (candidates.Count == 0)
            {
                return mergeTransform ?? avatarRoot;
            }

            Transform best = null;
            int bestScore = -1;

            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (candidate == null) continue;

                int score = 0;
                for (int j = 0; j < bindings.Count; j++)
                {
                    var binding = bindings[j];
                    if (string.IsNullOrEmpty(binding.path)) continue;
                    var target = candidate.Find(binding.path);
                    if (target != null)
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

            if (best != null && bestScore > 0)
            {
                return best;
            }

            return mergeTransform ?? avatarRoot;
        }

        private static List<Transform> BuildCandidateRoots(Transform mergeTransform, Transform avatarRoot)
        {
            var result = new List<Transform>();
            var visited = new HashSet<Transform>();

            void AddCandidate(Transform t)
            {
                if (t != null && visited.Add(t))
                {
                    result.Add(t);
                }
            }

            var current = mergeTransform;
            while (current != null)
            {
                AddCandidate(current);
                if (current == avatarRoot) break;
                current = current.parent;
            }

            AddCandidate(avatarRoot);

            return result;
        }

        private static bool EnsureMergeAnimatorReflection()
        {
            if (_cachedMergeAnimatorType != null &&
                _cachedMergeAnimatorAnimatorField != null &&
                _cachedMergeAnimatorPathModeField != null &&
                _cachedMergeAnimatorRelativePathRootField != null)
            {
                return true;
            }

            if (_mergeAnimatorLookupAttempted)
            {
                return _cachedMergeAnimatorType != null &&
                       _cachedMergeAnimatorAnimatorField != null &&
                       _cachedMergeAnimatorPathModeField != null &&
                       _cachedMergeAnimatorRelativePathRootField != null;
            }

            _mergeAnimatorLookupAttempted = true;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                var asm = assemblies[i];
                if (asm == null) continue;
                var type = asm.GetType("nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator");
                if (type != null)
                {
                    _cachedMergeAnimatorType = type;
                    _cachedMergeAnimatorAnimatorField = type.GetField("animator", BindingFlags.Public | BindingFlags.Instance);
                    _cachedMergeAnimatorPathModeField = type.GetField("pathMode", BindingFlags.Public | BindingFlags.Instance);
                    _cachedMergeAnimatorRelativePathRootField = type.GetField("relativePathRoot", BindingFlags.Public | BindingFlags.Instance);
                    break;
                }
            }

            return _cachedMergeAnimatorType != null &&
                   _cachedMergeAnimatorAnimatorField != null &&
                   _cachedMergeAnimatorPathModeField != null &&
                   _cachedMergeAnimatorRelativePathRootField != null;
        }

        private static bool EnsureAvatarObjectReferenceReflection()
        {
            if (_cachedAvatarObjectReferenceType != null &&
                _cachedAvatarObjectReferenceGetMethod != null &&
                _cachedAvatarObjectReferenceReferencePathField != null &&
                _cachedAvatarObjectReferenceTargetObjectField != null)
            {
                return true;
            }

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length && _cachedAvatarObjectReferenceType == null; i++)
            {
                var asm = assemblies[i];
                if (asm == null) continue;
                var type = asm.GetType("nadena.dev.modular_avatar.core.AvatarObjectReference");
                if (type != null)
                {
                    _cachedAvatarObjectReferenceType = type;
                    _cachedAvatarObjectReferenceGetMethod = type.GetMethod("Get", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Component) }, null);
                    _cachedAvatarObjectReferenceReferencePathField = type.GetField("referencePath", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    _cachedAvatarObjectReferenceTargetObjectField = type.GetField("targetObject", BindingFlags.NonPublic | BindingFlags.Instance);
                    var avatarRootField = type.GetField("AVATAR_ROOT", BindingFlags.Public | BindingFlags.Static);
                    if (avatarRootField != null)
                    {
                        _cachedAvatarObjectReferenceAvatarRootToken = avatarRootField.GetValue(null) as string;
                    }
                }
            }

            return _cachedAvatarObjectReferenceType != null &&
                   _cachedAvatarObjectReferenceGetMethod != null &&
                   _cachedAvatarObjectReferenceReferencePathField != null &&
                   _cachedAvatarObjectReferenceTargetObjectField != null;
        }
    }
}
