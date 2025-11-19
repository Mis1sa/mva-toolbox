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
    /// <summary>
    /// Anim Path Redirect 服务：负责记录/计算动画曲线的路径快照，以及应用路径重定向和缺失修复逻辑。
    /// 不包含任何 IMGUI 代码，由 AnimPathRedirectWindow 驱动。
    /// </summary>
    internal sealed class AnimPathRedirectService
    {
        #region 内部数据结构

        internal sealed class MissingCurveEntry
        {
            public AnimationClip Clip;                 // 所属动画剪辑
            public EditorCurveBinding Binding;         // 原始绑定信息
            public AnimationCurve Curve;               // 数值曲线
            public ObjectReferenceKeyframe[] ObjectRefKeyframes; // 对象引用曲线

            public string GroupName;                   // 规范化的属性组名

            public bool IsBlendshape;
            public string NewBlendshapeName;           // 修复后新的形态键名
            public readonly List<string> AvailableBlendshapes = new List<string>();

            public bool IsMarkedForRemoval;            // 是否被标记为移除
            public bool IsFixedByGroup { get; set; }   // 是否已由所属组准备好修复

            public bool IsObjectReference => ObjectRefKeyframes != null && ObjectRefKeyframes.Length > 0;
        }

        internal sealed class MissingObjectGroup
        {
            public string OldPath;
            public UnityEngine.Object FixTarget;
            public readonly Dictionary<Type, List<MissingCurveEntry>> CurvesByType = new Dictionary<Type, List<MissingCurveEntry>>();
            public bool IsExpanded = true;

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
                    if (string.IsNullOrEmpty(OldPath)) return "根物体 (Root)";
                    var parts = OldPath.Split('/');
                    return parts.Length > 0 ? parts[parts.Length - 1] : OldPath;
                }
            }
        }

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

        GameObject _targetRoot;                    // Avatar / 根物体
        readonly List<AnimatorController> _controllers = new List<AnimatorController>();
        readonly List<string> _controllerNames = new List<string>();
        int _selectedControllerIndex;              // 0-based, -1 表示未选

        int _selectedLayerIndex;                   // 0 = 全部层级 (ALL)，>0 对应具体层

        readonly List<PathChangeGroup> _pathChangeGroups = new List<PathChangeGroup>();
        readonly List<MissingObjectGroup> _missingGroups = new List<MissingObjectGroup>();

        bool _ignoreAllMissing;
        bool _hierarchyChanged;

        // 追踪时锁定的控制器/层级索引（用于 UI 提示，可选）
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

        #endregion

        #region 目标与控制器管理

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
            _ignoreAllMissing = false;
            _hierarchyChanged = false;
            _trackedControllerIndex = -1;
            _trackedLayerIndex = -1;
        }

        #endregion

        #region 追踪与状态计算

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
                        // 路径不存在：记录为 MissingObjectGroup
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
                                OldPath = binding.path
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

            // 记录追踪时的控制器与层级索引，用于 UI 锁定提示
            _trackedControllerIndex = _selectedControllerIndex;
            _trackedLayerIndex = _selectedLayerIndex;
        }

        public void CalculateCurrentPaths()
        {
            if (_targetRoot == null) return;
            var animatorRoot = _targetRoot.transform;

            // 1. 计算 PathChangeGroup 状态
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

            // 2. 缺失项自愈：如果原路径重新出现且组件齐全，则将 MissingGroup 转换为 PathChangeGroup
            for (int i = _missingGroups.Count - 1; i >= 0; i--)
            {
                var group = _missingGroups[i];
                var currentTransform = animatorRoot.Find(group.OldPath);
                if (currentTransform == null)
                {
                    continue;
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
                    // 如果该路径已经存在 PathChangeGroup，则更新之；否则新建一个
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
        }

        #endregion

        #region 应用重定向与修复

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

                // 1. 处理路径变更和删除
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

                            // 先移除旧绑定
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

                // 2. 处理缺失绑定
                // 当 _ignoreAllMissing 为 true 时，只忽略“未处理缺失”（FixTarget == null 且组不为空）的组，
                // 仍然处理已经指定修复目标的组以及仅做移除(IsEmpty)的组。
                foreach (var group in _missingGroups)
                {
                    // 忽略所有未处理缺失：无 FixTarget 且组中仍有未被标记移除的条目
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
                            // 清除旧绑定
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
