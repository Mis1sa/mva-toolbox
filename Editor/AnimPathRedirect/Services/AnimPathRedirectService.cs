using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using MVA.Toolbox.Public;

namespace MVA.Toolbox.AnimPathRedirect.Services
{
    // Anim Path Redirect 主服务：负责路径快照、缺失检测与应用修改（UI 见 AnimPathRedirectWindow.cs）
    internal sealed class AnimPathRedirectService
    {
        #region 内部数据结构

        // 缺失绑定的最小粒度单位（单条曲线）
        internal sealed class MissingCurveEntry
        {
            public AnimationClip Clip;
            public EditorCurveBinding Binding;
            public AnimationCurve Curve;
            public ObjectReferenceKeyframe[] ObjectRefKeyframes;

            public string GroupName;

            public bool IsBlendshape;
            public string NewBlendshapeName;
            public readonly List<string> AvailableBlendshapes = new List<string>();

            public bool IsMarkedForRemoval;
            public bool IsFixedByGroup { get; set; }

            public bool IsComponentChange;

            public bool IsObjectReference => ObjectRefKeyframes != null && ObjectRefKeyframes.Length > 0;
        }

        // 按物体路径聚合的缺失绑定组
        internal sealed class MissingObjectGroup
        {
            public string OldPath;
            public UnityEngine.Object FixTarget;
            public readonly Dictionary<Type, List<MissingCurveEntry>> CurvesByType = new Dictionary<Type, List<MissingCurveEntry>>();
            public bool IsExpanded = true;

            public bool OwnerExistedAtSnapshot;

            public string CurrentPath;

            public bool OwnerDeleted;

            public List<Type> RequiredTypes => CurvesByType
                .Where(kvp => kvp.Value.Any(c => !c.IsMarkedForRemoval))
                .Select(kvp => kvp.Key)
                .ToList();

            public bool IsFixed => FixTarget != null;
            public bool IsEmpty => CurvesByType.All(kvp => kvp.Value.All(c => c.IsMarkedForRemoval));

            public string TargetObjectName
            {
                get
                {
                    // 优先使用当前路径（当物体被重命名/移动时），否则回退到快照时记录的旧路径。
                    var path = string.IsNullOrEmpty(CurrentPath) ? OldPath : CurrentPath;

                    if (string.IsNullOrEmpty(path)) return "根物体 (Root)";
                    var parts = path.Split('/');
                    return parts.Length > 0 ? parts[parts.Length - 1] : path;
                }
            }
        }

        // 组件级变更（目前主要用于记录组件被移除的曲线）
        internal sealed class ComponentChangeGroup
        {
            public string Path;
            public Type ComponentType;
            public bool IsRemoved;
            public readonly List<EditorCurveBinding> Bindings = new List<EditorCurveBinding>();

            public string TargetObjectName
            {
                get
                {
                    if (string.IsNullOrEmpty(Path)) return "根物体 (Root)";
                    var parts = Path.Split('/');
                    return parts.Length > 0 ? parts[parts.Length - 1] : Path;
                }
            }

            public string ComponentName => ComponentType != null ? ComponentType.Name : "Component";
        }

        // 物体路径级变更（重命名/移动/删除）
        internal sealed class PathChangeGroup
        {
            public int InstanceID;
            public string OldPath;
            public string NewPath;
            public bool IsDeleted;
            public readonly List<EditorCurveBinding> Bindings = new List<EditorCurveBinding>();

            public bool HasPathChanged => OldPath != NewPath;

            public string TargetObjectName
            {
                get
                {
                    if (string.IsNullOrEmpty(OldPath)) return "根物体 (Root)";
                    var parts = OldPath.Split('/');
                    return parts.Length > 0 ? parts[parts.Length - 1] : OldPath;
                }
            }
        }

        #endregion

        GameObject _targetRoot;
        readonly List<AnimatorController> _controllers = new List<AnimatorController>();
        readonly List<string> _controllerNames = new List<string>();
        int _selectedControllerIndex;

        int _selectedLayerIndex;

        readonly List<PathChangeGroup> _pathChangeGroups = new List<PathChangeGroup>();
        readonly List<MissingObjectGroup> _missingGroups = new List<MissingObjectGroup>();

        readonly List<ComponentChangeGroup> _componentChangeGroups = new List<ComponentChangeGroup>();

        // 组件/约束曲线快照服务，定义于 AnimPathRedirectComponentService.cs
        readonly AnimPathRedirectComponentService _componentService = new AnimPathRedirectComponentService();

        // 是否在应用时整体忽略“未指定修复目标的缺失”
        bool _ignoreAllMissing;
        bool _hierarchyChanged;

        int _trackedControllerIndex = -1;
        int _trackedLayerIndex = -1;

        const string AllLayersName = "全部层级 (ALL)";

        #region 对外属性

        public GameObject TargetRoot => _targetRoot;

        public IReadOnlyList<AnimatorController> Controllers => _controllers;
        public IReadOnlyList<string> ControllerNames => _controllerNames;

        public int SelectedControllerIndex
        {
            get => _selectedControllerIndex;
            set => _selectedControllerIndex = Mathf.Clamp(value, 0, _controllers.Count > 0 ? _controllers.Count - 1 : 0);
        }

        public AnimatorController SelectedController
        {
            get
            {
                if (_selectedControllerIndex < 0 || _selectedControllerIndex >= _controllers.Count)
                {
                    return null;
                }

                return _controllers[_selectedControllerIndex];
            }
        }

        public int SelectedLayerIndex
        {
            get => _selectedLayerIndex;
            set => _selectedLayerIndex = Mathf.Max(0, value);
        }

        public bool HasSnapshot => _pathChangeGroups.Count > 0 || _missingGroups.Count > 0;

        public bool IgnoreAllMissing
        {
            get => _ignoreAllMissing;
            set => _ignoreAllMissing = value;
        }

        public bool HierarchyChanged
        {
            get => _hierarchyChanged;
            set => _hierarchyChanged = value;
        }

        public int TrackedControllerIndex => _trackedControllerIndex;
        public int TrackedLayerIndex => _trackedLayerIndex;

        public IReadOnlyList<PathChangeGroup> PathChangeGroups => _pathChangeGroups;
        public IReadOnlyList<MissingObjectGroup> MissingGroups => _missingGroups;
        public IReadOnlyList<ComponentChangeGroup> ComponentChangeGroups => _componentChangeGroups;

        #endregion

        #region 目标与控制器管理

        // 设置当前 Avatar / 根物体，同时刷新可用控制器（使用 ToolboxUtils，见 MVA.Toolbox.Public.ToolboxUtils）
        public void SetTarget(GameObject root)
        {
            if (root == _targetRoot)
            {
                return;
            }

            _targetRoot = root;
            ClearTracking();
            RefreshControllers();
        }

        public void OnHierarchyChanged()
        {
            if (HasSnapshot)
            {
                _hierarchyChanged = true;
            }
        }

        // 从 Avatar 根物体收集 AnimatorController 列表及显示名（依赖 ToolboxUtils）
        void RefreshControllers()
        {
            _controllers.Clear();
            _controllerNames.Clear();
            _selectedControllerIndex = -1;
            _selectedLayerIndex = 0;

            if (_targetRoot == null)
            {
                return;
            }

            var descriptor = ToolboxUtils.GetAvatarDescriptor(_targetRoot);
            var animator = _targetRoot.GetComponent<Animator>();

            _controllers.AddRange(ToolboxUtils.CollectControllersFromRoot(_targetRoot, includeSpecialLayers: true));
            if (_controllers.Count == 0)
            {
                return;
            }

            _controllerNames.AddRange(ToolboxUtils.BuildControllerDisplayNames(descriptor, animator, _controllers));

            // 若 Avatar 存在 FX 控制器，则默认选中 FX
            if (descriptor != null)
            {
                var fxController = ToolboxUtils.GetExistingFXController(descriptor);
                if (fxController != null)
                {
                    int fxIndex = _controllers.IndexOf(fxController);
                    if (fxIndex >= 0)
                    {
                        _selectedControllerIndex = fxIndex;
                        return;
                    }
                }
            }

            _selectedControllerIndex = 0;
        }

        public void ClearTracking()
        {
            _pathChangeGroups.Clear();
            _missingGroups.Clear();
            _componentChangeGroups.Clear();
            _componentService.Clear();
            _ignoreAllMissing = false;
            _hierarchyChanged = false;
            _trackedControllerIndex = -1;
            _trackedLayerIndex = -1;
        }

        #endregion

        #region 追踪与状态计算

        // 扫描选中控制器与层级中的所有动画剪辑，生成当前层级的路径与组件快照
        public void StartTrackingSnapshot()
        {
            _pathChangeGroups.Clear();
            _missingGroups.Clear();
            _hierarchyChanged = false;
            _ignoreAllMissing = false;

            var controller = SelectedController;
            if (controller == null || _targetRoot == null)
            {
                EditorUtility.DisplayDialog("提示", "请先选择有效的 AnimatorController 和目标物体。", "确定");
                return;
            }

            List<AnimationClip> clipsToProcess = GetClipsToProcess(controller, _selectedLayerIndex);
            if (clipsToProcess.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "当前选择的控制器/层级中没有找到任何动画剪辑。", "确定");
                return;
            }

            var uniquePathChangeMap = new Dictionary<string, PathChangeGroup>();
            var uniqueMissingGroupMap = new Dictionary<string, MissingObjectGroup>();

            Transform rootTransform = _targetRoot.transform;

            _componentService.BuildSnapshot(_targetRoot, clipsToProcess);

            AddInitialMissingComponents(rootTransform, uniqueMissingGroupMap);

            foreach (AnimationClip clip in clipsToProcess)
            {
                var curveBindings = AnimationUtility.GetCurveBindings(clip);
                var refBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                var allBindings = curveBindings.Concat(refBindings).ToArray();

                foreach (var binding in allBindings)
                {
                    string groupName = GetCanonicalGroupName(binding.propertyName, binding.type);
                    Transform targetTransform = rootTransform.Find(binding.path);

                    if (targetTransform != null)
                    {
                        // 路径存在：记录为 PathChangeGroup
                        if (!uniquePathChangeMap.TryGetValue(binding.path, out var existingGroup))
                        {
                            existingGroup = new PathChangeGroup
                            {
                                OldPath = binding.path,
                                InstanceID = targetTransform.gameObject.GetInstanceID(),
                                NewPath = binding.path
                            };
                            uniquePathChangeMap.Add(binding.path, existingGroup);
                            _pathChangeGroups.Add(existingGroup);
                        }

                        // 记录所有绑定信息（避免重复）
                        if (!existingGroup.Bindings.Any(b => b.path == binding.path && b.type == binding.type && b.propertyName == binding.propertyName))
                        {
                            existingGroup.Bindings.Add(binding);
                        }
                    }
                    else
                    {
                        var curve = AnimationUtility.GetEditorCurve(clip, binding);
                        var refKeys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                        if (curve == null && (refKeys == null || refKeys.Length == 0))
                        {
                            continue;
                        }

                        ObjectReferenceKeyframe[] storedRefKeys = null;
                        if (refKeys != null && refKeys.Length > 0)
                        {
                            storedRefKeys = refKeys.ToArray();
                        }

                        if (!uniqueMissingGroupMap.TryGetValue(binding.path, out var missingGroup))
                        {
                            missingGroup = new MissingObjectGroup
                            {
                                OldPath = binding.path,
                                OwnerExistedAtSnapshot = false
                            };
                            uniqueMissingGroupMap.Add(binding.path, missingGroup);
                            _missingGroups.Add(missingGroup);
                        }

                        if (!missingGroup.CurvesByType.TryGetValue(binding.type, out var list))
                        {
                            list = new List<MissingCurveEntry>();
                            missingGroup.CurvesByType.Add(binding.type, list);
                        }

                        bool isBlendshape = binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal);

                        list.Add(new MissingCurveEntry
                        {
                            Clip = clip,
                            Binding = binding,
                            Curve = curve,
                            ObjectRefKeyframes = storedRefKeys,
                            GroupName = groupName,
                            IsBlendshape = isBlendshape,
                            NewBlendshapeName = isBlendshape ? binding.propertyName : null
                        });
                    }
                }
            }

            _trackedControllerIndex = _selectedControllerIndex;
            _trackedLayerIndex = _selectedLayerIndex;
        }

        public void CalculateCurrentPaths()
        {
            if (_targetRoot == null) return;
            var animatorRoot = _targetRoot.transform;

            _componentChangeGroups.Clear();

            foreach (var data in _pathChangeGroups)
            {
                var currentObject = EditorUtility.InstanceIDToObject(data.InstanceID) as GameObject;
                if (currentObject != null)
                {
                    data.IsDeleted = false;
                    data.NewPath = GetRelativePath(currentObject.transform, animatorRoot);
                }
                else
                {
                    data.IsDeleted = true;
                    data.NewPath = null;
                }
            }

            foreach (var missing in _missingGroups)
            {
                missing.CurrentPath = null;
                missing.OwnerDeleted = false;

                if (string.IsNullOrEmpty(missing.OldPath))
                {
                    continue;
                }

                if (missing.OwnerExistedAtSnapshot)
                {
                    var pathGroup = _pathChangeGroups.FirstOrDefault(g => g.OldPath == missing.OldPath);
                    if (pathGroup != null)
                    {
                        missing.OwnerDeleted = pathGroup.IsDeleted;
                        if (!pathGroup.IsDeleted)
                        {
                            missing.CurrentPath = pathGroup.NewPath;
                        }
                    }
                    else
                    {
                        // 当不存在对应的 PathChangeGroup 时，根据当前层级是否还能找到 OldPath 来判断是否已被删除。
                        var currentTransform = animatorRoot.Find(missing.OldPath);
                        if (currentTransform == null)
                        {
                            missing.OwnerDeleted = true;
                        }
                    }
                }
                else
                {
                    var pathGroup = _pathChangeGroups.FirstOrDefault(g => g.OldPath == missing.OldPath);
                    if (pathGroup != null)
                    {
                        if (pathGroup.IsDeleted)
                        {
                            missing.OwnerDeleted = true;
                        }
                        else
                        {
                            missing.CurrentPath = pathGroup.NewPath;
                        }
                    }
                }
            }

            for (int i = _missingGroups.Count - 1; i >= 0; i--)
            {
                var group = _missingGroups[i];
                var currentTransform = animatorRoot.Find(group.OldPath);
                if (currentTransform == null)
                {
                    continue;
                }

                var existingPathGroup = _pathChangeGroups
                    .FirstOrDefault(g => g.OldPath == group.OldPath);

                if (existingPathGroup == null)
                {
                    existingPathGroup = new PathChangeGroup
                    {
                        OldPath = group.OldPath,
                        InstanceID = currentTransform.gameObject.GetInstanceID(),
                        NewPath = GetRelativePath(currentTransform, animatorRoot)
                    };
                    _pathChangeGroups.Add(existingPathGroup);
                }
                else
                {
                    existingPathGroup.InstanceID = currentTransform.gameObject.GetInstanceID();
                    existingPathGroup.NewPath = GetRelativePath(currentTransform, animatorRoot);
                }

                bool allComponentsExist = true;
                foreach (var type in group.CurvesByType.Keys)
                {
                    if (type == typeof(GameObject) || type == typeof(Transform))
                    {
                        continue;
                    }

                    if (currentTransform.GetComponent(type) == null)
                    {
                        allComponentsExist = false;
                        break;
                    }
                }

                if (allComponentsExist)
                {
                    var go = currentTransform.gameObject;
                    if (go != null)
                    {
                        foreach (var info in _componentService.ConstraintBindings)
                        {
                            if (info == null)
                            {
                                continue;
                            }

                            if (info.Path != group.OldPath)
                            {
                                continue;
                            }

                            if (go.GetComponent(info.ComponentType) != null)
                            {
                                info.ComponentPresentAtSnapshot = true;
                            }
                        }
                    }

                    // 将该缺失组中的所有绑定加入 PathChangeGroup 的 Bindings，避免重复
                    foreach (var kvp in group.CurvesByType)
                    {
                        foreach (var entry in kvp.Value)
                        {
                            var b = entry.Binding;
                            if (!existingPathGroup.Bindings.Any(x => x.path == b.path && x.type == b.type && x.propertyName == b.propertyName))
                            {
                                existingPathGroup.Bindings.Add(b);
                            }
                        }
                    }

                    _missingGroups.RemoveAt(i);
                }
            }

            // 组件级缺失自愈与最新组件缺失状态
            CleanupResolvedComponentMissing(animatorRoot);
            UpdateMissingConstraintComponents(animatorRoot);
        }

        #endregion

        #region 应用重定向与修复

        // 将当前路径变更与缺失修复写回 AnimationClip
        public (int modifiedCount, int fixedCount, int removedCount) ApplyRedirects()
        {
            var controller = SelectedController;
            if (!HasSnapshot || controller == null || _targetRoot == null)
            {
                return (0, 0, 0);
            }

            CalculateCurrentPaths();

            int modifiedCount = 0;
            int fixedCount = 0;
            int removedCount = 0;

            try
            {
                AssetDatabase.StartAssetEditing();

                var clipsForPathChange = GetClipsToProcess(controller, _trackedLayerIndex >= 0 ? _trackedLayerIndex : _selectedLayerIndex)
                    .Distinct()
                    .ToList();

                foreach (var group in _pathChangeGroups)
                {
                    if (!group.IsDeleted && !group.HasPathChanged)
                    {
                        continue;
                    }

                    foreach (var binding in group.Bindings)
                    {
                        var oldBinding = binding;

                        foreach (var sourceClip in clipsForPathChange)
                        {
                            var curve = AnimationUtility.GetEditorCurve(sourceClip, oldBinding);
                            var refKeys = AnimationUtility.GetObjectReferenceCurve(sourceClip, oldBinding);
                            if (curve == null && (refKeys == null || refKeys.Length == 0))
                            {
                                continue;
                            }

                            if (refKeys != null && refKeys.Length > 0)
                            {
                                AnimationUtility.SetObjectReferenceCurve(sourceClip, oldBinding, null);
                            }
                            else
                            {
                                AnimationUtility.SetEditorCurve(sourceClip, oldBinding, null);
                            }

                            if (!group.IsDeleted && !string.IsNullOrEmpty(group.NewPath))
                            {
                                var newBinding = new EditorCurveBinding
                                {
                                    path = group.NewPath,
                                    type = oldBinding.type,
                                    propertyName = oldBinding.propertyName
                                };

                                if (refKeys != null && refKeys.Length > 0)
                                {
                                    AnimationUtility.SetObjectReferenceCurve(sourceClip, newBinding, refKeys);
                                }
                                else if (curve != null)
                                {
                                    AnimationUtility.SetEditorCurve(sourceClip, newBinding, curve);
                                }

                                modifiedCount++;
                            }
                            else
                            {
                                removedCount++;
                            }

                            EditorUtility.SetDirty(sourceClip);
                        }
                    }
                }

                foreach (var group in _missingGroups)
                {
                    if (_ignoreAllMissing && group.FixTarget == null && !group.IsEmpty)
                    {
                        continue;
                    }

                    string newPath = null;
                    var typesToFix = new List<Type>();

                    if (group.IsFixed)
                    {
                        typesToFix = group.RequiredTypes;
                        if (!ValidateFixTargetComponents(group.FixTarget, typesToFix))
                        {
                            typesToFix.Clear();
                        }
                        else
                        {
                            var go = GetGameObject(group.FixTarget);
                            if (go != null)
                            {
                                newPath = GetRelativePath(go.transform, _targetRoot.transform);
                            }
                        }
                    }

                    foreach (var kvp in group.CurvesByType)
                    {
                        var oldType = kvp.Key;
                        var entries = kvp.Value;

                        foreach (var curveEntry in entries)
                        {
                            if (curveEntry.IsObjectReference)
                            {
                                AnimationUtility.SetObjectReferenceCurve(curveEntry.Clip, curveEntry.Binding, null);
                            }
                            else
                            {
                                AnimationUtility.SetEditorCurve(curveEntry.Clip, curveEntry.Binding, null);
                            }

                            if (curveEntry.IsMarkedForRemoval)
                            {
                                removedCount++;
                                EditorUtility.SetDirty(curveEntry.Clip);
                                continue;
                            }

                            if (newPath != null && typesToFix.Contains(oldType) && curveEntry.IsFixedByGroup)
                            {
                                string newPropertyName = curveEntry.Binding.propertyName;
                                if (curveEntry.IsBlendshape && !string.IsNullOrEmpty(curveEntry.NewBlendshapeName))
                                {
                                    newPropertyName = curveEntry.NewBlendshapeName;
                                }

                                var newBinding = new EditorCurveBinding
                                {
                                    path = newPath,
                                    type = oldType,
                                    propertyName = newPropertyName
                                };

                                if (curveEntry.IsObjectReference)
                                {
                                    AnimationUtility.SetObjectReferenceCurve(curveEntry.Clip, newBinding, curveEntry.ObjectRefKeyframes);
                                }
                                else if (curveEntry.Curve != null)
                                {
                                    AnimationUtility.SetEditorCurve(curveEntry.Clip, newBinding, curveEntry.Curve);
                                }

                                fixedCount++;
                            }
                            else
                            {
                                removedCount++;
                            }

                            EditorUtility.SetDirty(curveEntry.Clip);
                        }
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
            }

            ClearTracking();
            return (modifiedCount, fixedCount, removedCount);
        }

        #endregion

        #region 辅助方法

        string GetCanonicalGroupName(string propertyName, Type type)
        {
            if (type == typeof(Transform))
            {
                if (propertyName.StartsWith("m_LocalPosition.", StringComparison.Ordinal)) return "Position";
                if (propertyName.StartsWith("m_LocalRotation.", StringComparison.Ordinal)) return "Rotation";
                if (propertyName.StartsWith("m_LocalScale.", StringComparison.Ordinal)) return "Scale";
            }

            if (type == typeof(SkinnedMeshRenderer) && propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
            {
                return "BlendShape";
            }

            return propertyName;
        }

        List<AnimationClip> GetClipsToProcess(AnimatorController controller, int layerIndex)
        {
            var clips = new List<AnimationClip>();
            if (controller == null)
            {
                return clips;
            }

            if (layerIndex <= 0)
            {
                clips.AddRange(controller.animationClips);
            }
            else
            {
                int idx = layerIndex - 1;
                var layers = controller.layers ?? Array.Empty<AnimatorControllerLayer>();
                if (idx >= 0 && idx < layers.Length)
                {
                    var layer = layers[idx];
                    clips.AddRange(GetClipsFromStateMachine(layer.stateMachine));
                }
            }

            return clips.Distinct().ToList();
        }

        List<AnimationClip> GetClipsFromStateMachine(AnimatorStateMachine stateMachine)
        {
            var clips = new List<AnimationClip>();
            if (stateMachine == null) return clips;

            foreach (var stateInfo in stateMachine.states)
            {
                clips.AddRange(GetClipsFromMotion(stateInfo.state.motion));
            }

            foreach (var subMachine in stateMachine.stateMachines)
            {
                clips.AddRange(GetClipsFromStateMachine(subMachine.stateMachine));
            }

            return clips.Distinct().ToList();
        }

        List<AnimationClip> GetClipsFromMotion(Motion motion)
        {
            var clips = new List<AnimationClip>();
            if (motion == null) return clips;

            if (motion is AnimationClip clip)
            {
                clips.Add(clip);
            }
            else if (motion is BlendTree blendTree)
            {
                foreach (var child in blendTree.children)
                {
                    clips.AddRange(GetClipsFromMotion(child.motion));
                }
            }

            return clips;
        }

        void UpdateMissingConstraintComponents(Transform animatorRoot)
        {
            if (animatorRoot == null) return;
            if (_targetRoot == null) return;

            var bindings = _componentService.ConstraintBindings;
            if (bindings == null || bindings.Count == 0) return;

            foreach (var info in bindings)
            {
                if (info == null || info.Binding.type == null)
                {
                    continue;
                }

                // 起始快照时就不存在对应组件的条目，已在 StartTrackingSnapshot 中按“缺失”处理，这里仅处理
                // “快照时存在、之后该路径仍存在但组件类型已被移除”的情况。
                if (!info.ComponentPresentAtSnapshot)
                {
                    continue;
                }

                // 通过 PathChangeGroup 映射当前物体：
                // - 若存在 OldPath == info.Path 的 PathChangeGroup，则优先使用其 InstanceID / NewPath 获取当前物体；
                // - 若不存在 PathChangeGroup，则回退到使用快照时的路径查找。
                Transform currentTransform = null;

                var pathGroup = _pathChangeGroups.FirstOrDefault(g => g.OldPath == info.Path);
                if (pathGroup != null)
                {
                    if (pathGroup.IsDeleted)
                    {
                        // 该物体已被整体删除，物体级路径变更逻辑会处理
                        continue;
                    }

                    var currentObject = EditorUtility.InstanceIDToObject(pathGroup.InstanceID) as GameObject;
                    if (currentObject != null)
                    {
                        currentTransform = currentObject.transform;
                    }
                    else if (!string.IsNullOrEmpty(pathGroup.NewPath))
                    {
                        currentTransform = animatorRoot.Find(pathGroup.NewPath);
                    }
                }
                else
                {
                    currentTransform = animatorRoot.Find(info.Path);
                }

                if (currentTransform == null)
                {
                    // 当前找不到对应物体：视为物体级路径缺失，由 PathChangeGroup / MissingObjectGroup 逻辑处理
                    continue;
                }

                // 忽略 GameObject / Transform 自身，它们不属于组件级变更的范围
                if (info.ComponentType == typeof(GameObject) || info.ComponentType == typeof(Transform))
                {
                    continue;
                }

                var go = currentTransform.gameObject;
                if (go == null)
                {
                    continue;
                }

                // 若该类型组件仍然存在，则不视为组件变更
                if (go.GetComponent(info.ComponentType) != null)
                {
                    continue;
                }

                string path = info.Path ?? string.Empty;

                // 查找或创建对应路径 + 组件类型的 ComponentChangeGroup
                var changeGroup = _componentChangeGroups.FirstOrDefault(g =>
                    g.Path == path && g.ComponentType == info.ComponentType);

                if (changeGroup == null)
                {
                    changeGroup = new ComponentChangeGroup
                    {
                        Path = path,
                        ComponentType = info.ComponentType,
                        IsRemoved = true
                    };
                    _componentChangeGroups.Add(changeGroup);
                }

                // 避免重复添加同一条曲线
                bool exists = changeGroup.Bindings.Any(e =>
                    e.path == info.Binding.path &&
                    e.type == info.Binding.type &&
                    e.propertyName == info.Binding.propertyName);

                if (exists)
                {
                    continue;
                }

                changeGroup.Bindings.Add(info.Binding);
            }
        }

        void AddInitialMissingComponents(Transform animatorRoot, Dictionary<string, MissingObjectGroup> uniqueMissingGroupMap)
        {
            if (animatorRoot == null) return;
            if (_targetRoot == null) return;
            if (uniqueMissingGroupMap == null) return;

            var bindings = _componentService.ConstraintBindings;
            if (bindings == null || bindings.Count == 0) return;

            foreach (var info in bindings)
            {
                if (info == null || info.Binding.type == null)
                {
                    continue;
                }

                // 仅处理起始时就缺少对应组件的条目
                if (info.ComponentPresentAtSnapshot)
                {
                    continue;
                }

                var currentTransform = animatorRoot.Find(info.Path);
                if (currentTransform == null)
                {
                    // 该路径整体不存在时，由现有路径缺失逻辑处理
                    continue;
                }

                string path = info.Path ?? string.Empty;

                // 查找或创建对应路径的 MissingObjectGroup
                if (!uniqueMissingGroupMap.TryGetValue(path, out var missingGroup))
                {
                    missingGroup = new MissingObjectGroup
                    {
                        OldPath = path,
                        // 在这里创建的缺失组代表“物体存在但组件缺失”（组件级起始缺失）
                        OwnerExistedAtSnapshot = true
                    };
                    uniqueMissingGroupMap.Add(path, missingGroup);
                    _missingGroups.Add(missingGroup);
                }

                if (!missingGroup.CurvesByType.TryGetValue(info.ComponentType, out var list))
                {
                    list = new List<MissingCurveEntry>();
                    missingGroup.CurvesByType.Add(info.ComponentType, list);
                }

                // 避免重复添加同一条曲线
                bool exists = list.Any(e =>
                    e.Clip == info.Clip &&
                    e.Binding.path == info.Binding.path &&
                    e.Binding.type == info.Binding.type &&
                    e.Binding.propertyName == info.Binding.propertyName);

                if (exists)
                {
                    continue;
                }

                var curve = AnimationUtility.GetEditorCurve(info.Clip, info.Binding);
                var refKeys = AnimationUtility.GetObjectReferenceCurve(info.Clip, info.Binding);
                if (curve == null && (refKeys == null || refKeys.Length == 0))
                {
                    continue;
                }

                ObjectReferenceKeyframe[] storedRefKeys = null;
                if (refKeys != null && refKeys.Length > 0)
                {
                    storedRefKeys = refKeys.ToArray();
                }

                string groupName = GetCanonicalGroupName(info.Binding.propertyName, info.ComponentType);
                bool isBlendshape = info.ComponentType == typeof(SkinnedMeshRenderer) &&
                                    info.Binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal);

                list.Add(new MissingCurveEntry
                {
                    Clip = info.Clip,
                    Binding = info.Binding,
                    Curve = curve,
                    ObjectRefKeyframes = storedRefKeys,
                    GroupName = groupName,
                    IsBlendshape = isBlendshape,
                    NewBlendshapeName = isBlendshape ? info.Binding.propertyName : null
                });
            }
        }

        void CleanupResolvedComponentMissing(Transform animatorRoot)
        {
            if (animatorRoot == null) return;
            if (_targetRoot == null) return;

            foreach (var group in _missingGroups.ToList())
            {
                if (string.IsNullOrEmpty(group.OldPath))
                {
                    continue;
                }

                var currentTransform = animatorRoot.Find(group.OldPath);
                if (currentTransform == null)
                {
                    // 路径本身缺失的情况仍由现有路径级逻辑处理
                    continue;
                }

                var go = currentTransform.gameObject;
                if (go == null)
                {
                    continue;
                }

                // 针对组件级缺失：当该路径上重新出现某个组件类型时，仅移除该类型的缺失条目
                var types = group.CurvesByType.Keys.ToList();
                foreach (var type in types)
                {
                    if (type == typeof(GameObject) || type == typeof(Transform))
                    {
                        // GameObject / Transform 的缺失仍视为路径级问题
                        continue;
                    }

                    if (go.GetComponent(type) == null)
                    {
                        continue;
                    }

                    if (!group.CurvesByType.TryGetValue(type, out var curves) || curves == null)
                    {
                        continue;
                    }

                    // 移除该组件类型下的所有缺失条目（无论是起始缺失还是组件变更），
                    // 其余组件/BlendShape 的缺失仍保留在组内。
                    curves.Clear();

                    // 同时更新组件快照：当某路径上重新出现该组件类型时，后续再移除该组件
                    // 应被视为“组件变更”而非“起始缺失”，因此将对应的 ComponentPresentAtSnapshot 标记为 true。
                    foreach (var info in _componentService.ConstraintBindings)
                    {
                        if (info == null)
                        {
                            continue;
                        }

                        if (info.Path == group.OldPath && info.ComponentType == type)
                        {
                            info.ComponentPresentAtSnapshot = true;
                        }
                    }
                }

                // 清理空的类型条目
                var emptyTypes = group.CurvesByType
                    .Where(kvp => kvp.Value == null || kvp.Value.Count == 0)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var t in emptyTypes)
                {
                    group.CurvesByType.Remove(t);
                }
            }
        }

        string GetRelativePath(Transform target, Transform root)
        {
            if (target == null || root == null) return null;
            if (target == root) return string.Empty;

            string path = target.name;
            var current = target.parent;
            while (current != null && current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            if (current == root)
            {
                return path;
            }

            return null;
        }

        bool ValidateFixTargetComponents(UnityEngine.Object fixTarget, List<Type> requiredTypes)
        {
            if (fixTarget == null) return false;

            foreach (var type in requiredTypes)
            {
                if (!HasRequiredComponent(fixTarget, type))
                {
                    return false;
                }
            }

            return true;
        }

        bool HasRequiredComponent(UnityEngine.Object fixTarget, Type requiredType)
        {
            if (requiredType == typeof(GameObject) || requiredType == typeof(Transform))
            {
                return true;
            }

            var go = fixTarget as GameObject ?? (fixTarget as Component)?.gameObject;
            if (go == null) return false;

            return go.GetComponent(requiredType) != null;
        }

        GameObject GetGameObject(UnityEngine.Object obj)
        {
            if (obj is GameObject go) return go;
            if (obj is Component c) return c.gameObject;
            return null;
        }

        public void UpdateFixTargetStatus(MissingObjectGroup group)
        {
            if (group == null) return;

            if (group.FixTarget != null)
            {
                group.FixTarget = ValidateFixTargetObject(group.FixTarget, group.OldPath);
            }

            foreach (var kvp in group.CurvesByType)
            {
                var type = kvp.Key;
                var curves = kvp.Value;

                bool componentSatisfied = group.FixTarget != null && HasRequiredComponent(group.FixTarget, type);

                SkinnedMeshRenderer smr = null;
                if (componentSatisfied && type == typeof(SkinnedMeshRenderer))
                {
                    var go = GetGameObject(group.FixTarget);
                    smr = go != null ? go.GetComponent<SkinnedMeshRenderer>() : group.FixTarget as SkinnedMeshRenderer;
                }

                foreach (var curve in curves)
                {
                    if (curve.IsMarkedForRemoval)
                    {
                        curve.IsFixedByGroup = false;
                        continue;
                    }

                    if (!componentSatisfied)
                    {
                        curve.IsFixedByGroup = false;
                        continue;
                    }

                    if (curve.IsBlendshape)
                    {
                        UpdateBlendshapeListAndSelection(curve, smr);
                        curve.IsFixedByGroup = curve.AvailableBlendshapes.Count > 0;
                    }
                    else
                    {
                        curve.IsFixedByGroup = true;
                    }
                }
            }

            if (group.FixTarget == null && group.RequiredTypes.Count > 0)
            {
                foreach (var curve in group.CurvesByType.SelectMany(kvp => kvp.Value).Where(c => !c.IsMarkedForRemoval))
                {
                    curve.IsFixedByGroup = false;
                }
            }

            if (group.IsEmpty)
            {
                foreach (var curve in group.CurvesByType.SelectMany(kvp => kvp.Value))
                {
                    curve.IsFixedByGroup = false;
                }
            }
        }

        UnityEngine.Object ValidateFixTargetObject(UnityEngine.Object fixTarget, string oldPath)
        {
            if (fixTarget == null || _targetRoot == null) return null;

            var go = fixTarget as GameObject ?? (fixTarget as Component)?.gameObject;
            if (go == null) return null;

            string path = GetRelativePath(go.transform, _targetRoot.transform);
            if (path == null && go.transform != _targetRoot.transform)
            {
                EditorUtility.DisplayDialog("错误", $"无法修复路径 '{oldPath}'：物体 '{go.name}' 不在目标物体 '{_targetRoot.name}' 的层级结构之下。", "确定");
                return null;
            }

            return fixTarget;
        }

        void UpdateBlendshapeListAndSelection(MissingCurveEntry curve, SkinnedMeshRenderer smr)
        {
            curve.AvailableBlendshapes.Clear();
            curve.NewBlendshapeName = null;

            if (smr != null && smr.sharedMesh != null)
            {
                var mesh = smr.sharedMesh;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    curve.AvailableBlendshapes.Add("blendShape." + mesh.GetBlendShapeName(i));
                }

                if (curve.AvailableBlendshapes.Contains(curve.Binding.propertyName))
                {
                    curve.NewBlendshapeName = curve.Binding.propertyName;
                }
                else if (curve.AvailableBlendshapes.Count > 0)
                {
                    curve.NewBlendshapeName = curve.AvailableBlendshapes[0];
                }
            }
        }

        #endregion
    }
}
