using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using MVA.Toolbox.AnimFixUtility.Services;
using MVA.Toolbox.Public;

namespace MVA.Toolbox.AnimPathRedirect.Services
{
    /// <summary>
    /// Anim Path Redirect 主服务：负责控制器收集、缺失/路径变更状态以及命令入口（追踪、应用等逻辑见同名 partial 类）。
    /// </summary>
    internal sealed partial class AnimPathRedirectService
    {
        #region 自动匹配

        public (int matchedCount, int skippedAmbiguous, int skippedInvalid) AutoMatchMissingFixTargets()
        {
            if (!HasSnapshot || _targetRoot == null)
            {
                return (0, 0, 0);
            }

            CalculateCurrentPaths();

            var nameToObjects = new Dictionary<string, List<GameObject>>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in _targetRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                var go = t.gameObject;
                if (go == null) continue;

                var key = go.name ?? string.Empty;
                if (!nameToObjects.TryGetValue(key, out var list))
                {
                    list = new List<GameObject>();
                    nameToObjects.Add(key, list);
                }
                list.Add(go);
            }

            int matched = 0;
            int ambiguous = 0;
            int invalid = 0;

            foreach (var group in _missingGroups)
            {
                
                if (group == null) continue;
                if (group.OwnerDeleted || group.IsEmpty) continue;
                if (_ignoreAllMissing && group.FixTarget == null) continue;
                if (group.FixTarget != null) continue;

                string expectedName = GetObjectNameFromPath(group.OldPath);
                if (string.IsNullOrEmpty(expectedName) ||
                    string.Equals(expectedName, "根物体 (Root)", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!nameToObjects.TryGetValue(expectedName, out var candidates) || candidates == null || candidates.Count == 0)
                {
                    continue;
                }

                if (candidates.Count != 1)
                {
                    ambiguous++;
                    continue;
                }

                var candidate = candidates[0];
                if (candidate == null)
                {
                    continue;
                }

                var requiredTypes = group.RequiredTypes;
                if (requiredTypes != null && requiredTypes.Count > 0)
                {
                    bool ok = true;
                    for (int i = 0; i < requiredTypes.Count; i++)
                    {
                        var type = requiredTypes[i];
                        if (type == null || type == typeof(GameObject) || type == typeof(Transform)) continue;

                        if (candidate.GetComponent(type) == null)
                        {
                            ok = false;
                            break;
                        }
                    }

                    if (!ok)
                    {
                        invalid++;
                        continue;
                    }
                }

                group.FixTarget = candidate;
                UpdateFixTargetStatus(group);
                matched++;
            }

            return (matched, ambiguous, invalid);
        }

        #endregion

        #region 内部数据结构

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

        internal sealed class MissingObjectGroup
        {
            public string OldPath;
            public UnityEngine.Object FixTarget;
            public readonly Dictionary<Type, List<MissingCurveEntry>> CurvesByType = new Dictionary<Type, List<MissingCurveEntry>>();
            public bool IsExpanded;
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
                    var path = string.IsNullOrEmpty(CurrentPath) ? OldPath : CurrentPath;
                    if (string.IsNullOrEmpty(path)) return "根物体 (Root)";
                    var parts = path.Split('/');
                    return parts.Length > 0 ? parts[parts.Length - 1] : path;
                }
            }
        }

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

        #region 字段

        GameObject _targetRoot;
        readonly List<AnimatorController> _controllers = new List<AnimatorController>();
        readonly Dictionary<AnimatorController, Transform> _controllerRootMap = new Dictionary<AnimatorController, Transform>();
        readonly List<string> _controllerNames = new List<string>();
        int _selectedControllerIndex;
        int _selectedLayerIndex;

        readonly List<PathChangeGroup> _pathChangeGroups = new List<PathChangeGroup>();
        readonly List<MissingObjectGroup> _missingGroups = new List<MissingObjectGroup>();
        readonly List<ComponentChangeGroup> _componentChangeGroups = new List<ComponentChangeGroup>();

        readonly AnimPathRedirectComponentService _componentService = new AnimPathRedirectComponentService();

        bool _ignoreAllMissing;
        bool _hierarchyChanged;

        int _trackedControllerIndex = -1;
        int _trackedLayerIndex = -1;
        Transform _trackedControllerRoot;

        const string AllLayersName = "全部层级 (ALL)";

        #endregion

        #region 公共属性

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
                if (_selectedControllerIndex < 0 || _selectedControllerIndex >= _controllers.Count) return null;
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
            _controllerRootMap.Clear();
            _selectedControllerIndex = -1;
            _selectedLayerIndex = 0;

            if (_targetRoot == null)
            {
                return;
            }

            var descriptor = ToolboxUtils.GetAvatarDescriptor(_targetRoot);
            var animator = _targetRoot.GetComponent<Animator>();

            var controllerEntries = AnimFixControllerScanUtility.CollectControllersWithRoot(
                _targetRoot,
                includeSpecialLayers: true,
                allowAnimatorSubtree: false);
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

            if (_controllers.Count == 0)
            {
                return;
            }

            _controllerNames.AddRange(ToolboxUtils.BuildControllerDisplayNames(descriptor, animator, _controllers));

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
            _trackedControllerRoot = null;
        }

        #endregion
    }
}
