using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.AnimationRedirectTool
{
    internal sealed partial class AnimationRedirectToolService
    {
        internal sealed class MissingCurveEntry
        {
            internal AnimationClip Clip;
            internal EditorCurveBinding Binding;
            internal AnimationCurve Curve;
            internal ObjectReferenceKeyframe[] ObjectRefKeyframes;
            internal string GroupName;
            internal bool IsBlendshape;
            internal string NewBlendshapeName;
            internal readonly List<string> AvailableBlendshapes = new List<string>();
            internal bool IsMarkedForRemoval;
            internal bool IsFixedByGroup;
            internal bool IsComponentChange;
            internal bool IsObjectReference => ObjectRefKeyframes != null && ObjectRefKeyframes.Length > 0;
        }

        internal sealed class MissingObjectGroup
        {
            internal string OldPath;
            internal Object FixTarget;
            internal readonly Dictionary<Type, List<MissingCurveEntry>> CurvesByType = new Dictionary<Type, List<MissingCurveEntry>>();
            internal bool IsExpanded;
            internal bool OwnerExistedAtSnapshot;
            internal string CurrentPath;
            internal bool OwnerDeleted;

            internal List<Type> RequiredTypes => CurvesByType
                .Where(kvp => kvp.Value.Any(curve => !curve.IsMarkedForRemoval))
                .Select(kvp => kvp.Key)
                .ToList();

            internal bool IsFixed => FixTarget != null;
            internal bool IsEmpty => CurvesByType.All(kvp => kvp.Value.All(curve => curve.IsMarkedForRemoval));

            internal string TargetObjectName
            {
                get
                {
                    string path = string.IsNullOrEmpty(CurrentPath) ? OldPath : CurrentPath;
                    if (string.IsNullOrEmpty(path))
                    {
                        return "根物体 (Root)";
                    }

                    string[] parts = path.Split('/');
                    return parts.Length > 0 ? parts[parts.Length - 1] : path;
                }
            }
        }

        internal sealed class ComponentChangeGroup
        {
            internal string Path;
            internal Type ComponentType;
            internal bool IsRemoved;
            internal readonly List<EditorCurveBinding> Bindings = new List<EditorCurveBinding>();

            internal string TargetObjectName
            {
                get
                {
                    if (string.IsNullOrEmpty(Path))
                    {
                        return "根物体 (Root)";
                    }

                    string[] parts = Path.Split('/');
                    return parts.Length > 0 ? parts[parts.Length - 1] : Path;
                }
            }

            internal string ComponentName => ComponentType != null ? ComponentType.Name : "Component";
        }

        internal sealed class PathChangeGroup
        {
            internal int InstanceID;
            internal string OldPath;
            internal string NewPath;
            internal bool IsDeleted;
            internal readonly List<EditorCurveBinding> Bindings = new List<EditorCurveBinding>();

            internal bool HasPathChanged => OldPath != NewPath;

            internal string TargetObjectName
            {
                get
                {
                    if (string.IsNullOrEmpty(OldPath))
                    {
                        return "根物体 (Root)";
                    }

                    string[] parts = OldPath.Split('/');
                    return parts.Length > 0 ? parts[parts.Length - 1] : OldPath;
                }
            }
        }

        private GameObject _targetRoot;
        private IReadOnlyList<AnimatorController> _controllers = Array.Empty<AnimatorController>();
        private Dictionary<AnimatorController, Transform> _controllerRootMap = new Dictionary<AnimatorController, Transform>();
        private int _selectedControllerIndex = -1;
        private int _selectedLayerIndex = -1;

        private readonly List<PathChangeGroup> _pathChangeGroups = new List<PathChangeGroup>();
        private readonly List<MissingObjectGroup> _missingGroups = new List<MissingObjectGroup>();
        private readonly List<ComponentChangeGroup> _componentChangeGroups = new List<ComponentChangeGroup>();
        private readonly AnimationRedirectToolComponentService _componentService = new AnimationRedirectToolComponentService();

        private bool _ignoreAllMissing;
        private bool _hierarchyChanged;
        private int _trackedControllerIndex = -1;
        private int _trackedLayerIndex = -1;
        private Transform _trackedControllerRoot;

        internal GameObject TargetRoot => _targetRoot;
        internal bool HasSnapshot => _pathChangeGroups.Count > 0 || _missingGroups.Count > 0;
        internal bool IgnoreAllMissing
        {
            get => _ignoreAllMissing;
            set => _ignoreAllMissing = value;
        }

        internal bool HierarchyChanged
        {
            get => _hierarchyChanged;
            set => _hierarchyChanged = value;
        }

        internal IReadOnlyList<PathChangeGroup> PathChangeGroups => _pathChangeGroups;
        internal IReadOnlyList<MissingObjectGroup> MissingGroups => _missingGroups;
        internal IReadOnlyList<ComponentChangeGroup> ComponentChangeGroups => _componentChangeGroups;

        internal AnimatorController SelectedController
        {
            get
            {
                if (_controllers == null || _controllers.Count == 0 || _selectedControllerIndex < 0 || _selectedControllerIndex >= _controllers.Count)
                {
                    return null;
                }

                return _controllers[_selectedControllerIndex];
            }
        }

        internal void SyncScope(
            GameObject targetRoot,
            IReadOnlyList<AnimatorController> controllers,
            Dictionary<AnimatorController, Transform> controllerRootMap,
            int selectedControllerIndex,
            int selectedLayerIndex)
        {
            bool targetChanged = _targetRoot != targetRoot;
            _targetRoot = targetRoot;
            _controllers = controllers ?? Array.Empty<AnimatorController>();
            _controllerRootMap = controllerRootMap ?? new Dictionary<AnimatorController, Transform>();

            if (targetChanged)
            {
                ClearTracking();
            }

            _selectedControllerIndex = ClampControllerIndex(selectedControllerIndex);
            _selectedLayerIndex = _controllers.Count == 0 ? -1 : selectedLayerIndex;
        }

        internal void OnHierarchyChanged()
        {
            if (HasSnapshot)
            {
                _hierarchyChanged = true;
            }
        }

        internal void ClearTracking()
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

        internal (int matchedCount, int skippedAmbiguous, int skippedInvalid) AutoMatchMissingFixTargets()
        {
            if (!HasSnapshot || _targetRoot == null)
            {
                return (0, 0, 0);
            }

            CalculateCurrentPaths();
            Transform animatorRoot = _trackedControllerRoot ?? _targetRoot.transform;

            Dictionary<string, List<GameObject>> nameToObjects = new Dictionary<string, List<GameObject>>(StringComparer.OrdinalIgnoreCase);
            foreach (Transform transform in _targetRoot.GetComponentsInChildren<Transform>(true))
            {
                if (transform == null)
                {
                    continue;
                }

                GameObject go = transform.gameObject;
                if (go == null)
                {
                    continue;
                }

                string key = go.name ?? string.Empty;
                if (!nameToObjects.TryGetValue(key, out List<GameObject> list))
                {
                    list = new List<GameObject>();
                    nameToObjects.Add(key, list);
                }

                list.Add(go);
            }

            int matched = 0;
            int ambiguous = 0;
            int invalid = 0;

            foreach (MissingObjectGroup group in _missingGroups)
            {
                if (group == null || group.OwnerDeleted || group.IsEmpty || (_ignoreAllMissing && group.FixTarget == null) || group.FixTarget != null)
                {
                    continue;
                }

                if (animatorRoot != null && !string.IsNullOrEmpty(group.CurrentPath))
                {
                    Transform currentTransform = ResolveTransformByPath(animatorRoot, group.CurrentPath);
                    GameObject currentObject = currentTransform != null ? currentTransform.gameObject : null;
                    if (CandidateSatisfiesRequiredTypes(currentObject, group.RequiredTypes))
                    {
                        group.FixTarget = currentObject;
                        UpdateFixTargetStatus(group);
                        matched++;
                        continue;
                    }
                }

                string expectedName = GetObjectNameFromPath(group.OldPath);
                if (string.IsNullOrEmpty(expectedName) || string.Equals(expectedName, "根物体 (Root)", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!nameToObjects.TryGetValue(expectedName, out List<GameObject> candidates) || candidates == null || candidates.Count == 0)
                {
                    continue;
                }

                if (candidates.Count != 1)
                {
                    List<GameObject> validCandidates = candidates
                        .Where(candidate => CandidateSatisfiesRequiredTypes(candidate, group.RequiredTypes))
                        .ToList();
                    if (validCandidates.Count == 1)
                    {
                        group.FixTarget = validCandidates[0];
                        UpdateFixTargetStatus(group);
                        matched++;
                        continue;
                    }

                    ambiguous++;
                    continue;
                }

                GameObject candidate = candidates[0];
                if (candidate == null)
                {
                    continue;
                }

                if (!CandidateSatisfiesRequiredTypes(candidate, group.RequiredTypes))
                {
                    invalid++;
                    continue;
                }

                group.FixTarget = candidate;
                UpdateFixTargetStatus(group);
                matched++;
            }

            return (matched, ambiguous, invalid);
        }

        private static bool CandidateSatisfiesRequiredTypes(GameObject candidate, List<Type> requiredTypes)
        {
            if (candidate == null)
            {
                return false;
            }

            if (requiredTypes == null || requiredTypes.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < requiredTypes.Count; i++)
            {
                Type type = requiredTypes[i];
                if (type == null || type == typeof(GameObject) || type == typeof(Transform))
                {
                    continue;
                }

                if (candidate.GetComponent(type) == null)
                {
                    return false;
                }
            }

            return true;
        }

        private int ClampControllerIndex(int value)
        {
            if (_controllers == null || _controllers.Count == 0)
            {
                return -1;
            }

            return Mathf.Clamp(value, 0, _controllers.Count - 1);
        }
    }
}
