using System;
using System.Collections.Generic;
using System.Linq;
using MVA.Toolbox.AnimFixUtility.Shared;
using MVA.Toolbox.AvatarQuickToggle;
using MVA.Toolbox.Public;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.AnimFixUtility.Services
{
    /// <summary>
    /// 提供 Find 模式所需的业务逻辑：目标管理、属性扫描、结果收集等。
    /// </summary>
    public sealed class AnimFixFindService
    {
        private readonly AnimFixUtilityContext _context;

        private Object _selectedAnimatedObject;
        private string _selectedPath;

        private readonly List<PropertyGroupData> _availableGroups = new();
        private readonly List<FoundClipInfo> _foundClips = new();

        private int _selectedGroupIndex;
        private int _selectedBlendshapeOptionIndex;
        private bool _searchCompleted;

        private GameObject _lastTargetRoot;
        private int _lastControllerIndex = -1;
        private int _lastLayerIndex = -2;

        private readonly List<string> _currentBlendshapeOptions = new();
        private bool _hasBlendshapeGroups;

        public AnimFixFindService(AnimFixUtilityContext context)
        {
            _context = context;
        }

        public Object SelectedAnimatedObject => _selectedAnimatedObject;
        public IReadOnlyList<PropertyGroupData> PropertyGroups => _availableGroups;
        public IReadOnlyList<FoundClipInfo> FoundClips => _foundClips;
        public int SelectedGroupIndex => _selectedGroupIndex;
        public int SelectedBlendshapeOptionIndex => _selectedBlendshapeOptionIndex;
        public bool SearchCompleted => _searchCompleted;
        public bool HasAvailableGroups => _availableGroups.Count > 0;
        public bool HasAnimatedObject => _selectedAnimatedObject != null;
        public bool SelectedGroupIsBlendshape =>
            _selectedGroupIndex > 0 &&
            _selectedGroupIndex <= _availableGroups.Count &&
            _availableGroups[_selectedGroupIndex - 1].ComponentType == typeof(SkinnedMeshRenderer) &&
            string.Equals(_availableGroups[_selectedGroupIndex - 1].CanonicalPropertyName, "blendShape", StringComparison.Ordinal);
        public bool HasBlendshapeGroups => _hasBlendshapeGroups;
        public IReadOnlyList<string> CurrentBlendshapeOptions => _currentBlendshapeOptions;

        public void SyncContextChanges()
        {
            bool targetChanged = _context.TargetRoot != _lastTargetRoot;
            if (targetChanged)
            {
                _lastTargetRoot = _context.TargetRoot;
                ResetSelection();
            }

            if (!targetChanged &&
                (_context.SelectedControllerIndex != _lastControllerIndex ||
                 _context.SelectedLayerIndex != _lastLayerIndex))
            {
                _lastControllerIndex = _context.SelectedControllerIndex;
                _lastLayerIndex = _context.SelectedLayerIndex;

                if (_selectedAnimatedObject != null && _context.Controllers.Count > 0)
                {
                    ScanAndGroupAnimatedProperties();
                }
            }
            else if (targetChanged)
            {
                _lastControllerIndex = _context.SelectedControllerIndex;
                _lastLayerIndex = _context.SelectedLayerIndex;
            }
        }

        public bool SetSelectedAnimatedObject(Object newValue)
        {
            var normalized = NormalizeSelectedObjectForAvatarAndAao(newValue);
            if (Equals(_selectedAnimatedObject, normalized))
                return false;

            _selectedAnimatedObject = normalized;
            _selectedPath = null;
            _availableGroups.Clear();
            _selectedGroupIndex = 0;
            _selectedBlendshapeOptionIndex = 0;
            _searchCompleted = false;
            _foundClips.Clear();

            if (_selectedAnimatedObject != null && _context.Controllers.Count > 0)
            {
                ScanAndGroupAnimatedProperties();
            }

            return true;
        }

        public void ChangeGroupIndex(int newIndex)
        {
            newIndex = Mathf.Clamp(newIndex, 0, _availableGroups.Count);
            if (newIndex == _selectedGroupIndex)
                return;

            _selectedGroupIndex = newIndex;
            _selectedBlendshapeOptionIndex = 0;
            RebuildBlendshapeOptionsForSelectedGroup();
            _searchCompleted = false;
            _foundClips.Clear();
            FindClipsForSelectedGroup();
        }

        public void ChangeBlendshapeOptionIndex(int newIndex)
        {
            newIndex = Mathf.Clamp(newIndex, 0, _currentBlendshapeOptions.Count > 0 ? _currentBlendshapeOptions.Count - 1 : 0);
            if (newIndex == _selectedBlendshapeOptionIndex)
                return;

            _selectedBlendshapeOptionIndex = newIndex;
            _searchCompleted = false;
            _foundClips.Clear();
            FindClipsForSelectedGroup();
        }

        public bool CanRefresh()
        {
            return _selectedAnimatedObject != null &&
                   _context.Controllers.Count > 0 &&
                   _selectedGroupIndex >= 0 &&
                   _selectedGroupIndex <= _availableGroups.Count;
        }

        public void RefreshSearch()
        {
            FindClipsForSelectedGroup();
        }

        public IReadOnlyList<string> BuildAqtConfigHints()
        {
            var descriptor = _context.AvatarDescriptor;
            if (descriptor == null || _selectedAnimatedObject == null)
                return Array.Empty<string>();

            var avatarRoot = descriptor.gameObject;
            if (avatarRoot == null)
                return Array.Empty<string>();

            var config = avatarRoot.GetComponent<QuickToggleConfig>();
            if (config == null || config.layerConfigs == null || config.layerConfigs.Count == 0)
                return Array.Empty<string>();

            var targetGO = (_selectedAnimatedObject as GameObject) ?? (_selectedAnimatedObject as Component)?.gameObject;
            if (targetGO == null)
                return Array.Empty<string>();

            bool allowScope = false;
            var selectedController = _context.SelectedController;
            if (selectedController != null)
            {
                if (_context.SelectedLayerIndex < 0)
                {
                    allowScope = true;
                }
                else
                {
                    var baseLayers = descriptor.baseAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                    for (int i = 0; i < baseLayers.Length; i++)
                    {
                        var layer = baseLayers[i];
                        if (layer.type == VRCAvatarDescriptor.AnimLayerType.FX &&
                            layer.animatorController == selectedController)
                        {
                            allowScope = true;
                            break;
                        }
                    }
                }
            }

            if (!allowScope)
                return Array.Empty<string>();

            if (_selectedGroupIndex < 0 || _selectedGroupIndex > _availableGroups.Count)
                return Array.Empty<string>();

            bool isAllProperties = _selectedGroupIndex == 0;
            PropertyGroupData currentGroup = null;
            bool isBlendShapeGroup = false;
            bool isActiveGroup = false;

            if (!isAllProperties)
            {
                currentGroup = _availableGroups[_selectedGroupIndex - 1];
                isBlendShapeGroup =
                    currentGroup.ComponentType == typeof(SkinnedMeshRenderer) &&
                    currentGroup.CanonicalPropertyName == "blendShape";

                isActiveGroup =
                    currentGroup.ComponentType == typeof(GameObject) &&
                    (currentGroup.CanonicalPropertyName == "m_IsActive" ||
                     currentGroup.CanonicalPropertyName == "IsActive");

                if (!isBlendShapeGroup && !isActiveGroup)
                    return Array.Empty<string>();
            }

            var hits = new List<string>();

            for (int i = 0; i < config.layerConfigs.Count; i++)
            {
                var layer = config.layerConfigs[i];
                if (layer == null)
                    continue;

                bool affectsTarget = false;

                switch (layer.layerType)
                {
                    case 0:
                        if (layer.boolTargets != null)
                        {
                            foreach (var t in layer.boolTargets)
                            {
                                if (t == null || t.targetObject != targetGO)
                                    continue;

                                if (isAllProperties ||
                                    (isBlendShapeGroup && t.controlType == QuickToggleConfig.TargetControlType.BlendShape) ||
                                    (isActiveGroup && t.controlType == QuickToggleConfig.TargetControlType.GameObject))
                                {
                                    affectsTarget = true;
                                    break;
                                }
                            }
                        }
                        break;
                    case 1:
                        if (layer.intGroups != null)
                        {
                            foreach (var grp in layer.intGroups)
                            {
                                if (grp?.targetItems == null) continue;
                                foreach (var t in grp.targetItems)
                                {
                                    if (t == null || t.targetObject != targetGO)
                                        continue;

                                    if (isAllProperties ||
                                        (isBlendShapeGroup && t.controlType == QuickToggleConfig.TargetControlType.BlendShape) ||
                                        (isActiveGroup && t.controlType == QuickToggleConfig.TargetControlType.GameObject))
                                    {
                                        affectsTarget = true;
                                        break;
                                    }
                                }

                                if (affectsTarget) break;
                            }
                        }
                        break;
                    case 2:
                        if (layer.floatTargets != null)
                        {
                            foreach (var t in layer.floatTargets)
                            {
                                if (t == null || t.targetObject != targetGO)
                                    continue;

                                if (isAllProperties || isBlendShapeGroup)
                                {
                                    affectsTarget = true;
                                    break;
                                }
                            }
                        }
                        break;
                }

                if (!affectsTarget)
                    continue;

                string typeLabel = layer.layerType == 0 ? "Bool" :
                    layer.layerType == 1 ? "Int" : "Float";
                string name = !string.IsNullOrEmpty(layer.displayName)
                    ? layer.displayName
                    : (!string.IsNullOrEmpty(layer.layerName) ? layer.layerName : "(未命名配置)");
                hits.Add($"{name} ({typeLabel})");
            }

            return hits.Count > 0 ? hits : Array.Empty<string>();
        }

        private void ResetSelection()
        {
            _selectedAnimatedObject = null;
            _selectedPath = null;
            _availableGroups.Clear();
            _selectedGroupIndex = 0;
            _searchCompleted = false;
            _foundClips.Clear();
        }

        private void ScanAndGroupAnimatedProperties()
        {
            _availableGroups.Clear();
            _selectedGroupIndex = 0;
            _selectedBlendshapeOptionIndex = 0;
            _searchCompleted = false;
            _foundClips.Clear();
            _hasBlendshapeGroups = false;

            if (_selectedAnimatedObject == null || _context.TargetRoot == null)
                return;

            Transform root = _context.TargetRoot.transform;
            Transform objectTransform = null;

            if (_selectedAnimatedObject is Component component)
                objectTransform = component.transform;
            else if (_selectedAnimatedObject is GameObject go)
                objectTransform = go.transform;

            if (objectTransform == null)
                return;

            _selectedPath = GetRelativePath(objectTransform, root);

            if (string.IsNullOrEmpty(_selectedPath) && objectTransform != root)
                return;

            var uniqueBindings = new Dictionary<(string path, string propertyName, Type type), EditorCurveBinding>();

            ForEachLayerInScope((controller, layer) =>
            {
                var clips = GetClipsFromStateMachine(layer.stateMachine);
                for (int j = 0; j < clips.Count; j++)
                {
                    var clip = clips[j];
                    if (clip == null) continue;

                    var bindings = AnimationUtility.GetCurveBindings(clip);
                    for (int k = 0; k < bindings.Length; k++)
                    {
                        var binding = bindings[k];
                        if (binding.path != _selectedPath)
                            continue;

                        var key = (binding.path, binding.propertyName, binding.type);
                        if (!uniqueBindings.ContainsKey(key))
                        {
                            uniqueBindings.Add(key, binding);
                        }
                    }
                }
            });

            var tempGroups = new Dictionary<(Type type, string canonicalName), PropertyGroupData>();

            foreach (var binding in uniqueBindings.Values)
            {
                string canonicalName;
                var basePropertyName = binding.propertyName;
                int dotIndex = basePropertyName.IndexOf('.');
                canonicalName = dotIndex > 0 ? basePropertyName[..dotIndex] : basePropertyName;

                var groupKey = (binding.type, canonicalName);
                if (!tempGroups.TryGetValue(groupKey, out var group))
                {
                    group = new PropertyGroupData
                    {
                        ComponentType = binding.type,
                        CanonicalPropertyName = canonicalName,
                        GroupDisplayName = $"{binding.type.Name}: {canonicalName}"
                    };
                    tempGroups.Add(groupKey, group);
                    _availableGroups.Add(group);
                }

                if (!group.BoundPropertyNames.Contains(binding.propertyName))
                {
                    group.BoundPropertyNames.Add(binding.propertyName);
                }
            }

            AugmentGroupsWithAqtConfigForTarget();

            _availableGroups.Sort((a, b) =>
                string.Compare(a.GroupDisplayName, b.GroupDisplayName, StringComparison.Ordinal));

            _hasBlendshapeGroups = _availableGroups.Any(g =>
                g.ComponentType == typeof(SkinnedMeshRenderer) &&
                string.Equals(g.CanonicalPropertyName, "blendShape", StringComparison.Ordinal));

            if (_availableGroups.Count > 0)
            {
                _selectedGroupIndex = 0;
                _selectedBlendshapeOptionIndex = 0;
                _currentBlendshapeOptions.Clear();
                FindClipsForSelectedGroup();
            }
        }

        private void RebuildBlendshapeOptionsForSelectedGroup()
        {
            _currentBlendshapeOptions.Clear();

            if (!SelectedGroupIsBlendshape)
            {
                _selectedBlendshapeOptionIndex = 0;
                return;
            }

            var group = _availableGroups[_selectedGroupIndex - 1];
            // 收集该分组下已绑定的 BlendShape 名称（去重，去前缀）
            var names = new HashSet<string>();
            foreach (var prop in group.BoundPropertyNames)
            {
                if (string.IsNullOrEmpty(prop)) continue;
                const string prefix = "blendShape.";
                if (!prop.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var name = prop.Substring(prefix.Length);
                if (!string.IsNullOrEmpty(name))
                {
                    names.Add(name);
                }
            }

            _currentBlendshapeOptions.Add("全部");
            if (names.Count > 0)
            {
                _currentBlendshapeOptions.AddRange(names.OrderBy(n => n));
            }
            _selectedBlendshapeOptionIndex = 0;
        }

        private void FindClipsForSelectedGroup()
        {
            _searchCompleted = false;
            _foundClips.Clear();

            if (_context.Controllers.Count == 0 ||
                _selectedAnimatedObject == null ||
                _selectedGroupIndex < 0 ||
                _selectedGroupIndex > _availableGroups.Count)
            {
                return;
            }

            PropertyGroupData group = null;
            bool useAllGroups = _selectedGroupIndex == 0;
            if (!useAllGroups)
            {
                group = _availableGroups[_selectedGroupIndex - 1];
            }

            var usedClips = new HashSet<AnimationClip>();
            HashSet<string> allowedProperties = null;
            bool filterBlendshape = false;
            if (group != null &&
                group.ComponentType == typeof(SkinnedMeshRenderer) &&
                string.Equals(group.CanonicalPropertyName, "blendShape", StringComparison.Ordinal))
            {
                if (_selectedBlendshapeOptionIndex > 0 &&
                    _selectedBlendshapeOptionIndex < _currentBlendshapeOptions.Count)
                {
                    var selected = _currentBlendshapeOptions[_selectedBlendshapeOptionIndex];
                    if (!string.IsNullOrEmpty(selected))
                    {
                        allowedProperties = new HashSet<string> { "blendShape." + selected };
                        filterBlendshape = true;
                    }
                }
            }

            ForEachLayerInScope((controller, layer) =>
            {
                var clips = GetClipsFromStateMachine(layer.stateMachine);
                for (int j = 0; j < clips.Count; j++)
                {
                    var clip = clips[j];
                    if (clip == null || usedClips.Contains(clip))
                        continue;

                    var bindings = AnimationUtility.GetCurveBindings(clip);
                    bool hasMatch = false;

                    for (int k = 0; k < bindings.Length; k++)
                    {
                        var binding = bindings[k];
                        if (binding.path != _selectedPath)
                            continue;

                        if (useAllGroups)
                        {
                            hasMatch = true;
                            break;
                        }

                        if (binding.type == group.ComponentType)
                        {
                            if (filterBlendshape)
                            {
                                if (allowedProperties != null && allowedProperties.Contains(binding.propertyName))
                                {
                                    hasMatch = true;
                                    break;
                                }
                            }
                            else if (group.BoundPropertyNames.Contains(binding.propertyName))
                            {
                                hasMatch = true;
                                break;
                            }
                        }
                    }

                    if (hasMatch)
                    {
                        usedClips.Add(clip);
                        _foundClips.Add(new FoundClipInfo
                        {
                            Clip = clip,
                            Controller = controller
                        });
                    }
                }
            });

            _searchCompleted = true;
        }

        private void ForEachLayerInScope(Action<AnimatorController, AnimatorControllerLayer> action)
        {
            if (action == null) return;

            var controllers = _context.Controllers;
            if (controllers.Count == 0) return;

            if (_context.SelectedControllerIndex < 0)
            {
                for (int i = 0; i < controllers.Count; i++)
                {
                    var controller = controllers[i];
                    if (controller == null) continue;
                    IterateControllerLayers(controller, _context.SelectedLayerIndex, action);
                }
                return;
            }

            var selectedController = _context.SelectedController;
            if (selectedController == null) return;

            IterateControllerLayers(selectedController, _context.SelectedLayerIndex, action);
        }

        private static void IterateControllerLayers(AnimatorController controller, int selectedLayerIndex, Action<AnimatorController, AnimatorControllerLayer> action)
        {
            var layers = controller.layers ?? Array.Empty<AnimatorControllerLayer>();
            if (layers.Length == 0) return;

            if (selectedLayerIndex < 0)
            {
                for (int i = 0; i < layers.Length; i++)
                {
                    action(controller, layers[i]);
                }
                return;
            }

            if (selectedLayerIndex >= 0 && selectedLayerIndex < layers.Length)
            {
                action(controller, layers[selectedLayerIndex]);
            }
        }

        private static List<AnimationClip> GetClipsFromStateMachine(AnimatorStateMachine stateMachine)
        {
            var clips = new List<AnimationClip>();
            if (stateMachine == null)
                return clips;

            foreach (var child in stateMachine.states)
            {
                if (child.state == null) continue;
                CollectClipsFromMotion(child.state.motion, clips);
            }

            foreach (var sub in stateMachine.stateMachines)
            {
                if (sub.stateMachine == null) continue;
                clips.AddRange(GetClipsFromStateMachine(sub.stateMachine));
            }

            return clips.Distinct().ToList();
        }

        private static void CollectClipsFromMotion(Motion motion, List<AnimationClip> clips)
        {
            if (motion == null)
                return;

            if (motion is AnimationClip clip)
            {
                if (!clips.Contains(clip))
                {
                    clips.Add(clip);
                }
                return;
            }

            if (motion is BlendTree tree)
            {
                foreach (var child in tree.children)
                {
                    CollectClipsFromMotion(child.motion, clips);
                }
            }
        }

        private Object NormalizeSelectedObjectForAvatarAndAao(Object obj)
        {
            if (obj == null) return null;

            GameObject rootGo = _context.TargetRoot;
            if (rootGo == null) return obj;

            GameObject go = obj switch
            {
                GameObject goObj => goObj,
                Component comp => comp.gameObject,
                _ => null
            };

            if (go == null || go == rootGo)
                return null;

            var rootTransform = rootGo.transform;
            var t = go.transform;
            bool underRoot = false;
            while (t != null)
            {
                if (t == rootTransform)
                {
                    underRoot = true;
                    break;
                }
                t = t.parent;
            }

            if (!underRoot)
                return null;

            var controllers = _context.Controllers;
            if (controllers != null && controllers.Count > 0)
            {
                t = go.transform.parent;
                while (t != null && t != rootTransform)
                {
                    var a = t.GetComponent<Animator>();
                    if (a != null && a.runtimeAnimatorController is AnimatorController ac && !controllers.Contains(ac))
                    {
                        return null;
                    }
                    t = t.parent;
                }
            }

            var smrOnGo = go.GetComponent<SkinnedMeshRenderer>();
            if (smrOnGo != null)
            {
                var msmOwner = FindAaoMergeOwnerForRendererUnderRoot(rootGo, smrOnGo);
                if (msmOwner != null)
                {
                    go = msmOwner;
                }
            }

            return go;
        }

        private static GameObject FindAaoMergeOwnerForRendererUnderRoot(GameObject root, SkinnedMeshRenderer targetSmr)
        {
            if (root == null || targetSmr == null)
                return null;

            var rootTransform = root.transform;
            Type mergeType = null;
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length && mergeType == null; i++)
            {
                var asm = assemblies[i];
                if (asm == null) continue;
                mergeType = asm.GetType("Anatawa12.AvatarOptimizer.MergeSkinnedMesh");
            }
            if (mergeType == null) return null;

            var comps = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < comps.Length; i++)
            {
                var c = comps[i];
                if (c == null) continue;
                var t = c.GetType();
                if (t == null || t != mergeType) continue;

                var msmGo = c.gameObject;
                if (msmGo == null) continue;

                var msmTransform = msmGo.transform;
                bool msmUnderRoot = false;
                var temp = msmTransform;
                while (temp != null)
                {
                    if (temp == rootTransform)
                    {
                        msmUnderRoot = true;
                        break;
                    }
                    temp = temp.parent;
                }
                if (!msmUnderRoot)
                    continue;

                try
                {
                    var renderersField = t.GetField("renderersSet", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var setObj = renderersField != null ? renderersField.GetValue(c) : null;
                    if (setObj == null) continue;

                    var setType = setObj.GetType();
                    var getAsSet = setType.GetMethod("GetAsSet", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    System.Collections.IEnumerable enumerable = null;
                    if (getAsSet != null)
                    {
                        enumerable = getAsSet.Invoke(setObj, null) as System.Collections.IEnumerable;
                    }
                    else if (setObj is System.Collections.IEnumerable e)
                    {
                        enumerable = e;
                    }

                    if (enumerable == null) continue;

                    foreach (var obj in enumerable)
                    {
                        if (obj is SkinnedMeshRenderer smr && smr == targetSmr)
                        {
                            return msmGo;
                        }
                    }
                }
                catch
                {
                    // 忽略反射异常
                }
            }

            return null;
        }

        private static string GetRelativePath(Transform target, Transform root)
        {
            if (target == null || root == null)
                return string.Empty;

            if (target == root)
                return string.Empty;

            var stack = new Stack<string>();
            var current = target;
            while (current != null && current != root)
            {
                stack.Push(current.name);
                current = current.parent;
            }

            if (current != root)
                return string.Empty;

            return string.Join("/", stack.ToArray());
        }

        private void AugmentGroupsWithAqtConfigForTarget()
        {
            var descriptor = _context.AvatarDescriptor;
            if (descriptor == null || _selectedAnimatedObject == null)
                return;

            var avatarRoot = descriptor.gameObject;
            if (avatarRoot == null)
                return;

            var config = avatarRoot.GetComponent<QuickToggleConfig>();
            if (config == null || config.layerConfigs == null || config.layerConfigs.Count == 0)
                return;

            var targetGO = (_selectedAnimatedObject as GameObject) ?? (_selectedAnimatedObject as Component)?.gameObject;
            if (targetGO == null)
                return;

            bool needActiveGroup = false;
            bool needBlendShapeGroup = false;

            for (int i = 0; i < config.layerConfigs.Count; i++)
            {
                var layer = config.layerConfigs[i];
                if (layer == null)
                    continue;

                switch (layer.layerType)
                {
                    case 0:
                        if (layer.boolTargets != null)
                        {
                            foreach (var t in layer.boolTargets)
                            {
                                if (t == null || t.targetObject != targetGO)
                                    continue;

                                if (t.controlType == QuickToggleConfig.TargetControlType.GameObject)
                                    needActiveGroup = true;
                                else if (t.controlType == QuickToggleConfig.TargetControlType.BlendShape)
                                    needBlendShapeGroup = true;
                            }
                        }
                        break;
                    case 1:
                        if (layer.intGroups != null)
                        {
                            foreach (var grp in layer.intGroups)
                            {
                                if (grp?.targetItems == null) continue;
                                foreach (var t in grp.targetItems)
                                {
                                    if (t == null || t.targetObject != targetGO)
                                        continue;

                                    if (t.controlType == QuickToggleConfig.TargetControlType.GameObject)
                                        needActiveGroup = true;
                                    else if (t.controlType == QuickToggleConfig.TargetControlType.BlendShape)
                                        needBlendShapeGroup = true;
                                }
                            }
                        }
                        break;
                    case 2:
                        if (layer.floatTargets != null)
                        {
                            foreach (var t in layer.floatTargets)
                            {
                                if (t == null || t.targetObject != targetGO)
                                    continue;

                                needBlendShapeGroup = true;
                            }
                        }
                        break;
                }
            }

            if (!needActiveGroup && !needBlendShapeGroup)
                return;

            bool hasActiveGroup = _availableGroups.Any(g =>
                g.ComponentType == typeof(GameObject) && g.CanonicalPropertyName == "m_IsActive");

            bool hasBlendShapeGroup = _availableGroups.Any(g =>
                g.ComponentType == typeof(SkinnedMeshRenderer) && g.CanonicalPropertyName == "blendShape");

            if (needActiveGroup && !hasActiveGroup)
            {
                var group = new PropertyGroupData
                {
                    ComponentType = typeof(GameObject),
                    CanonicalPropertyName = "m_IsActive",
                    GroupDisplayName = "GameObject: IsActive"
                };
                group.BoundPropertyNames.Add("m_IsActive");
                _availableGroups.Add(group);
            }

            if (needBlendShapeGroup && !hasBlendShapeGroup)
            {
                var group = new PropertyGroupData
                {
                    ComponentType = typeof(SkinnedMeshRenderer),
                    CanonicalPropertyName = "blendShape",
                    GroupDisplayName = "SkinnedMeshRenderer: blendShape"
                };
                _availableGroups.Add(group);
            }
        }

        public sealed class PropertyGroupData
        {
            public Type ComponentType;
            public string CanonicalPropertyName;
            public string GroupDisplayName;
            public readonly List<string> BoundPropertyNames = new();
        }

        public sealed class FoundClipInfo
        {
            public AnimationClip Clip;
            public AnimatorController Controller;
        }
    }
}
