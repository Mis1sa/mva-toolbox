using System;
using System.Collections.Generic;
using System.Linq;
using MVA.Toolbox.Animation.Shared.Controllers;
using MVA.Toolbox.Animation.Shared.SelectableRange;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.AnimationRedirectTool
{
    internal sealed partial class AnimationRedirectToolService
    {
        internal void StartTrackingSnapshot()
        {
            _pathChangeGroups.Clear();
            _missingGroups.Clear();
            _componentChangeGroups.Clear();
            _hierarchyChanged = false;
            _ignoreAllMissing = false;

            AnimatorController controller = SelectedController;
            if (controller == null || _targetRoot == null)
            {
                EditorUtility.DisplayDialog("提示", "请先选择有效的 AnimatorController 和目标物体。", "确定");
                return;
            }

            ControllerWithRoot controllerScope = ResolveControllerScope(controller);
            Transform controllerRoot = controllerScope.RootTransform ?? _targetRoot.transform;
            List<AnimationClip> clipsToProcess = GetClipsToProcess(controller, _selectedLayerIndex);
            if (clipsToProcess.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "当前选择的控制器/层级中没有找到任何动画剪辑。", "确定");
                return;
            }

            Dictionary<string, PathChangeGroup> uniquePathChangeMap = new Dictionary<string, PathChangeGroup>();
            Dictionary<string, MissingObjectGroup> uniqueMissingGroupMap = new Dictionary<string, MissingObjectGroup>();

            _componentService.BuildSnapshot(controllerRoot != null ? controllerRoot.gameObject : _targetRoot, clipsToProcess);
            AddInitialMissingComponents(controllerRoot, uniqueMissingGroupMap);

            foreach (AnimationClip clip in clipsToProcess)
            {
                EditorCurveBinding[] curveBindings = AnimationUtility.GetCurveBindings(clip);
                EditorCurveBinding[] refBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                EditorCurveBinding[] allBindings = curveBindings.Concat(refBindings).ToArray();

                foreach (EditorCurveBinding binding in allBindings)
                {
                    string groupName = GetCanonicalGroupName(binding.propertyName, binding.type);
                    Transform targetTransform = ResolveTransformByPath(controllerRoot, binding.path);
                    if (targetTransform != null)
                    {
                        if (!uniquePathChangeMap.TryGetValue(binding.path, out PathChangeGroup existingGroup))
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

                        if (!existingGroup.Bindings.Any(entry => entry.path == binding.path && entry.type == binding.type && entry.propertyName == binding.propertyName))
                        {
                            existingGroup.Bindings.Add(binding);
                        }
                    }
                    else
                    {
                        AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                        ObjectReferenceKeyframe[] refKeys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                        if (curve == null && (refKeys == null || refKeys.Length == 0))
                        {
                            continue;
                        }

                        if (!uniqueMissingGroupMap.TryGetValue(binding.path, out MissingObjectGroup missingGroup))
                        {
                            missingGroup = new MissingObjectGroup
                            {
                                OldPath = binding.path,
                                OwnerExistedAtSnapshot = false
                            };
                            uniqueMissingGroupMap.Add(binding.path, missingGroup);
                            _missingGroups.Add(missingGroup);
                        }

                        if (!missingGroup.CurvesByType.TryGetValue(binding.type, out List<MissingCurveEntry> list))
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
                            ObjectRefKeyframes = refKeys != null && refKeys.Length > 0 ? refKeys.ToArray() : null,
                            GroupName = groupName,
                            IsBlendshape = isBlendshape,
                            NewBlendshapeName = isBlendshape ? binding.propertyName : null
                        });
                    }
                }
            }

            _trackedControllerIndex = _selectedControllerIndex;
            _trackedLayerIndex = _selectedLayerIndex;
            _trackedControllerRoot = controllerRoot;
            _trackedControllerIgnoresNestedAnimators = controllerScope.IgnoresNestedAnimators;
        }

        internal void CalculateCurrentPaths()
        {
            Transform animatorRoot = _trackedControllerRoot ?? _targetRoot?.transform;
            if (animatorRoot == null)
            {
                return;
            }

            _componentChangeGroups.Clear();

            foreach (PathChangeGroup group in _pathChangeGroups)
            {
                GameObject currentObject = EditorUtility.InstanceIDToObject(group.InstanceID) as GameObject;
                if (currentObject != null)
                {
                    group.IsDeleted = false;
                    group.NewPath = GetRelativePath(currentObject.transform, animatorRoot);
                }
                else
                {
                    group.IsDeleted = true;
                    group.NewPath = null;
                }
            }

            foreach (MissingObjectGroup missing in _missingGroups)
            {
                missing.CurrentPath = null;
                missing.OwnerDeleted = false;
                if (missing.OldPath == null)
                {
                    continue;
                }

                PathChangeGroup pathGroup = _pathChangeGroups.FirstOrDefault(group => group.OldPath == missing.OldPath);
                if (missing.OwnerExistedAtSnapshot)
                {
                    if (pathGroup != null)
                    {
                        missing.OwnerDeleted = pathGroup.IsDeleted;
                        if (!pathGroup.IsDeleted)
                        {
                            missing.CurrentPath = pathGroup.NewPath;
                        }
                    }
                    else if (ResolveTransformByPath(animatorRoot, missing.OldPath) == null)
                    {
                        missing.OwnerDeleted = true;
                    }
                }
                else if (pathGroup != null)
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

            for (int i = _missingGroups.Count - 1; i >= 0; i--)
            {
                MissingObjectGroup group = _missingGroups[i];
                Transform currentTransform = ResolveTransformByPath(animatorRoot, group.OldPath);
                if (currentTransform == null)
                {
                    continue;
                }

                PathChangeGroup existingPathGroup = _pathChangeGroups.FirstOrDefault(entry => entry.OldPath == group.OldPath);
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
                foreach (Type type in group.CurvesByType.Keys)
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

                if (!allComponentsExist)
                {
                    continue;
                }

                GameObject go = currentTransform.gameObject;
                if (go != null)
                {
                    foreach (AnimationRedirectToolComponentService.ConstraintBindingInfo info in _componentService.ConstraintBindings)
                    {
                        if (info == null || info.Path != group.OldPath)
                        {
                            continue;
                        }

                        if (go.GetComponent(info.ComponentType) != null)
                        {
                            info.ComponentPresentAtSnapshot = true;
                        }
                    }
                }

                foreach (KeyValuePair<Type, List<MissingCurveEntry>> kvp in group.CurvesByType)
                {
                    foreach (MissingCurveEntry entry in kvp.Value)
                    {
                        EditorCurveBinding binding = entry.Binding;
                        if (!existingPathGroup.Bindings.Any(current => current.path == binding.path && current.type == binding.type && current.propertyName == binding.propertyName))
                        {
                            existingPathGroup.Bindings.Add(binding);
                        }
                    }
                }

                _missingGroups.RemoveAt(i);
            }

            CleanupResolvedComponentMissing(animatorRoot);
            UpdateMissingConstraintComponents(animatorRoot);
        }

        internal (int modifiedCount, int fixedCount, int removedCount) ApplyRedirects()
        {
            AnimatorController controller = SelectedController;
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

                List<AnimationClip> clipsForPathChange = GetClipsToProcess(controller, _trackedLayerIndex >= 0 ? _trackedLayerIndex : _selectedLayerIndex)
                    .Distinct()
                    .ToList();

                foreach (PathChangeGroup group in _pathChangeGroups)
                {
                    if (!group.IsDeleted && !group.HasPathChanged)
                    {
                        continue;
                    }

                    foreach (EditorCurveBinding binding in group.Bindings)
                    {
                        foreach (AnimationClip sourceClip in clipsForPathChange)
                        {
                            AnimationCurve curve = AnimationUtility.GetEditorCurve(sourceClip, binding);
                            ObjectReferenceKeyframe[] refKeys = AnimationUtility.GetObjectReferenceCurve(sourceClip, binding);
                            if (curve == null && (refKeys == null || refKeys.Length == 0))
                            {
                                continue;
                            }

                            if (refKeys != null && refKeys.Length > 0)
                            {
                                AnimationUtility.SetObjectReferenceCurve(sourceClip, binding, null);
                            }
                            else
                            {
                                AnimationUtility.SetEditorCurve(sourceClip, binding, null);
                            }

                            if (!group.IsDeleted && group.NewPath != null)
                            {
                                EditorCurveBinding newBinding = new EditorCurveBinding
                                {
                                    path = group.NewPath,
                                    type = binding.type,
                                    propertyName = binding.propertyName
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

                foreach (MissingObjectGroup group in _missingGroups)
                {
                    if ((_ignoreAllMissing && group.FixTarget == null) && !group.IsEmpty)
                    {
                        continue;
                    }

                    string newPath = null;
                    List<Type> typesToFix = new List<Type>();
                    if (group.IsFixed)
                    {
                        typesToFix = group.RequiredTypes;
                        if (!ValidateFixTargetComponents(group.FixTarget, typesToFix))
                        {
                            typesToFix.Clear();
                        }
                        else
                        {
                            GameObject go = GetGameObject(group.FixTarget);
                            if (go != null)
                            {
                                Transform relRoot = _trackedControllerRoot ?? _targetRoot.transform;
                                newPath = GetRelativePath(go.transform, relRoot);
                            }
                        }
                    }

                    foreach (KeyValuePair<Type, List<MissingCurveEntry>> kvp in group.CurvesByType)
                    {
                        Type oldType = kvp.Key;
                        List<MissingCurveEntry> entries = kvp.Value;
                        foreach (MissingCurveEntry curveEntry in entries)
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
                                string newPropertyName = curveEntry.IsBlendshape && !string.IsNullOrEmpty(curveEntry.NewBlendshapeName)
                                    ? curveEntry.NewBlendshapeName
                                    : curveEntry.Binding.propertyName;

                                EditorCurveBinding newBinding = new EditorCurveBinding
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

        private Transform ResolveControllerRoot(AnimatorController controller)
        {
            return ResolveControllerScope(controller).RootTransform;
        }

        private List<AnimationClip> GetClipsToProcess(AnimatorController controller, int layerIndex)
        {
            List<AnimationClip> clips = new List<AnimationClip>();
            if (controller == null)
            {
                return clips;
            }

            if (layerIndex < 0)
            {
                clips.AddRange(controller.animationClips);
            }
            else
            {
                AnimatorControllerLayer[] layers = controller.layers ?? Array.Empty<AnimatorControllerLayer>();
                if (layerIndex >= 0 && layerIndex < layers.Length)
                {
                    clips.AddRange(GetClipsFromStateMachine(layers[layerIndex].stateMachine));
                }
            }

            return clips.Distinct().ToList();
        }

        private List<AnimationClip> GetClipsFromStateMachine(AnimatorStateMachine stateMachine)
        {
            List<AnimationClip> clips = new List<AnimationClip>();
            if (stateMachine == null)
            {
                return clips;
            }

            foreach (ChildAnimatorState stateInfo in stateMachine.states)
            {
                clips.AddRange(GetClipsFromMotion(stateInfo.state.motion));
            }

            foreach (ChildAnimatorStateMachine subMachine in stateMachine.stateMachines)
            {
                clips.AddRange(GetClipsFromStateMachine(subMachine.stateMachine));
            }

            return clips.Distinct().ToList();
        }

        private List<AnimationClip> GetClipsFromMotion(Motion motion)
        {
            List<AnimationClip> clips = new List<AnimationClip>();
            if (motion == null)
            {
                return clips;
            }

            if (motion is AnimationClip clip)
            {
                clips.Add(clip);
            }
            else if (motion is BlendTree blendTree)
            {
                foreach (ChildMotion child in blendTree.children)
                {
                    clips.AddRange(GetClipsFromMotion(child.motion));
                }
            }

            return clips;
        }

        private void UpdateMissingConstraintComponents(Transform animatorRoot)
        {
            if (animatorRoot == null || _targetRoot == null)
            {
                return;
            }

            IReadOnlyList<AnimationRedirectToolComponentService.ConstraintBindingInfo> bindings = _componentService.ConstraintBindings;
            if (bindings == null || bindings.Count == 0)
            {
                return;
            }

            foreach (AnimationRedirectToolComponentService.ConstraintBindingInfo info in bindings)
            {
                if (info == null || info.Binding.type == null || !info.ComponentPresentAtSnapshot)
                {
                    continue;
                }

                Transform currentTransform = null;
                PathChangeGroup pathGroup = _pathChangeGroups.FirstOrDefault(group => group.OldPath == info.Path);
                if (pathGroup != null)
                {
                    if (pathGroup.IsDeleted)
                    {
                        continue;
                    }

                    GameObject currentObject = EditorUtility.InstanceIDToObject(pathGroup.InstanceID) as GameObject;
                    currentTransform = currentObject != null ? currentObject.transform : ResolveTransformByPath(animatorRoot, pathGroup.NewPath);
                }
                else
                {
                    currentTransform = ResolveTransformByPath(animatorRoot, info.Path);
                }

                if (currentTransform == null || info.ComponentType == typeof(GameObject) || info.ComponentType == typeof(Transform))
                {
                    continue;
                }

                GameObject go = currentTransform.gameObject;
                if (go == null || go.GetComponent(info.ComponentType) != null)
                {
                    continue;
                }

                string path = info.Path ?? string.Empty;
                ComponentChangeGroup changeGroup = _componentChangeGroups.FirstOrDefault(group => group.Path == path && group.ComponentType == info.ComponentType);
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

                bool exists = changeGroup.Bindings.Any(entry =>
                    entry.path == info.Binding.path &&
                    entry.type == info.Binding.type &&
                    entry.propertyName == info.Binding.propertyName);
                if (!exists)
                {
                    changeGroup.Bindings.Add(info.Binding);
                }
            }
        }

        private void AddInitialMissingComponents(Transform animatorRoot, Dictionary<string, MissingObjectGroup> uniqueMissingGroupMap)
        {
            if (animatorRoot == null || _targetRoot == null || uniqueMissingGroupMap == null)
            {
                return;
            }

            IReadOnlyList<AnimationRedirectToolComponentService.ConstraintBindingInfo> bindings = _componentService.ConstraintBindings;
            if (bindings == null || bindings.Count == 0)
            {
                return;
            }

            foreach (AnimationRedirectToolComponentService.ConstraintBindingInfo info in bindings)
            {
                if (info == null || info.Binding.type == null || info.ComponentPresentAtSnapshot)
                {
                    continue;
                }

                Transform currentTransform = ResolveTransformByPath(animatorRoot, info.Path);
                if (currentTransform == null)
                {
                    continue;
                }

                string path = info.Path ?? string.Empty;
                if (!uniqueMissingGroupMap.TryGetValue(path, out MissingObjectGroup missingGroup))
                {
                    missingGroup = new MissingObjectGroup
                    {
                        OldPath = path,
                        OwnerExistedAtSnapshot = true
                    };
                    uniqueMissingGroupMap.Add(path, missingGroup);
                    _missingGroups.Add(missingGroup);
                }

                if (!missingGroup.CurvesByType.TryGetValue(info.ComponentType, out List<MissingCurveEntry> list))
                {
                    list = new List<MissingCurveEntry>();
                    missingGroup.CurvesByType.Add(info.ComponentType, list);
                }

                bool exists = list.Any(entry =>
                    entry.Clip == info.Clip &&
                    entry.Binding.path == info.Binding.path &&
                    entry.Binding.type == info.Binding.type &&
                    entry.Binding.propertyName == info.Binding.propertyName);
                if (exists)
                {
                    continue;
                }

                AnimationCurve curve = AnimationUtility.GetEditorCurve(info.Clip, info.Binding);
                ObjectReferenceKeyframe[] refKeys = AnimationUtility.GetObjectReferenceCurve(info.Clip, info.Binding);
                if (curve == null && (refKeys == null || refKeys.Length == 0))
                {
                    continue;
                }

                bool isBlendshape = info.ComponentType == typeof(SkinnedMeshRenderer) && info.Binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal);
                list.Add(new MissingCurveEntry
                {
                    Clip = info.Clip,
                    Binding = info.Binding,
                    Curve = curve,
                    ObjectRefKeyframes = refKeys != null && refKeys.Length > 0 ? refKeys.ToArray() : null,
                    GroupName = GetCanonicalGroupName(info.Binding.propertyName, info.ComponentType),
                    IsBlendshape = isBlendshape,
                    NewBlendshapeName = isBlendshape ? info.Binding.propertyName : null
                });
            }
        }

        private void CleanupResolvedComponentMissing(Transform animatorRoot)
        {
            if (animatorRoot == null || _targetRoot == null)
            {
                return;
            }

            foreach (MissingObjectGroup group in _missingGroups.ToList())
            {
                if (group.OldPath == null)
                {
                    continue;
                }

                Transform currentTransform = ResolveTransformByPath(animatorRoot, group.OldPath);
                if (currentTransform == null)
                {
                    continue;
                }

                GameObject go = currentTransform.gameObject;
                if (go == null)
                {
                    continue;
                }

                List<Type> types = group.CurvesByType.Keys.ToList();
                foreach (Type type in types)
                {
                    if (type == typeof(GameObject) || type == typeof(Transform) || go.GetComponent(type) == null)
                    {
                        continue;
                    }

                    if (group.CurvesByType.TryGetValue(type, out List<MissingCurveEntry> curves) && curves != null)
                    {
                        curves.Clear();
                    }

                    foreach (AnimationRedirectToolComponentService.ConstraintBindingInfo info in _componentService.ConstraintBindings)
                    {
                        if (info != null && info.Path == group.OldPath && info.ComponentType == type)
                        {
                            info.ComponentPresentAtSnapshot = true;
                        }
                    }
                }

                List<Type> emptyTypes = group.CurvesByType
                    .Where(kvp => kvp.Value == null || kvp.Value.Count == 0)
                    .Select(kvp => kvp.Key)
                    .ToList();
                foreach (Type type in emptyTypes)
                {
                    group.CurvesByType.Remove(type);
                }
            }
        }

        private string GetRelativePath(Transform target, Transform root)
        {
            if (target == null || root == null)
            {
                return null;
            }

            if (target == root)
            {
                return string.Empty;
            }

            string path = target.name;
            Transform current = target.parent;
            while (current != null && current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return current == root ? path : null;
        }

        private static Transform ResolveTransformByPath(Transform root, string path)
        {
            if (root == null || path == null)
            {
                return null;
            }

            return path.Length == 0 ? root : root.Find(path);
        }

        private bool ValidateFixTargetComponents(Object fixTarget, List<Type> requiredTypes)
        {
            if (fixTarget == null)
            {
                return false;
            }

            for (int i = 0; i < requiredTypes.Count; i++)
            {
                if (!HasRequiredComponent(fixTarget, requiredTypes[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private bool HasRequiredComponent(Object fixTarget, Type requiredType)
        {
            if (requiredType == typeof(GameObject) || requiredType == typeof(Transform))
            {
                return true;
            }

            GameObject go = fixTarget as GameObject ?? (fixTarget as Component)?.gameObject;
            return go != null && go.GetComponent(requiredType) != null;
        }

        private GameObject GetGameObject(Object obj)
        {
            if (obj is GameObject go)
            {
                return go;
            }

            if (obj is Component component)
            {
                return component.gameObject;
            }

            return null;
        }

        internal void UpdateFixTargetStatus(MissingObjectGroup group)
        {
            if (group == null)
            {
                return;
            }

            if (group.FixTarget != null)
            {
                group.FixTarget = ValidateFixTargetObject(group.FixTarget, group.OldPath);
            }

            foreach (KeyValuePair<Type, List<MissingCurveEntry>> kvp in group.CurvesByType)
            {
                Type type = kvp.Key;
                List<MissingCurveEntry> curves = kvp.Value;
                bool componentSatisfied = group.FixTarget != null && HasRequiredComponent(group.FixTarget, type);

                SkinnedMeshRenderer smr = null;
                if (componentSatisfied && type == typeof(SkinnedMeshRenderer))
                {
                    GameObject go = GetGameObject(group.FixTarget);
                    smr = go != null ? go.GetComponent<SkinnedMeshRenderer>() : group.FixTarget as SkinnedMeshRenderer;
                }

                foreach (MissingCurveEntry curve in curves)
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
                foreach (MissingCurveEntry curve in group.CurvesByType.SelectMany(kvp => kvp.Value).Where(curve => !curve.IsMarkedForRemoval))
                {
                    curve.IsFixedByGroup = false;
                }
            }

            if (group.IsEmpty)
            {
                foreach (MissingCurveEntry curve in group.CurvesByType.SelectMany(kvp => kvp.Value))
                {
                    curve.IsFixedByGroup = false;
                }
            }
        }

        private Object ValidateFixTargetObject(Object fixTarget, string oldPath)
        {
            if (fixTarget == null || _targetRoot == null)
            {
                return null;
            }

            GameObject go = fixTarget as GameObject ?? (fixTarget as Component)?.gameObject;
            if (go == null)
            {
                return null;
            }

            ControllerWithRoot selectionScope = CurrentSelectionScope;
            Transform selectionRoot = selectionScope.RootTransform ?? _targetRoot.transform;
            if (go.transform == selectionRoot)
            {
                string scopeName = selectionRoot == _targetRoot.transform ? _targetRoot.name : selectionRoot.name;
                EditorUtility.DisplayDialog("错误", $"无法修复路径 '{oldPath}'：物体 '{go.name}' 是当前控制器作用域 '{scopeName}' 的根物体，不能作为可选目标。", "确定");
                return null;
            }

            if (!AnimationSelectableRangeUtility.IsTransformInControllerScope(go.transform, selectionRoot, selectionScope.IgnoresNestedAnimators))
            {
                string scopeName = selectionRoot == _targetRoot.transform ? _targetRoot.name : selectionRoot.name;
                EditorUtility.DisplayDialog("错误", $"无法修复路径 '{oldPath}'：物体 '{go.name}' 不在当前控制器作用域 '{scopeName}' 内。", "确定");
                return null;
            }

            return fixTarget;
        }

        private void UpdateBlendshapeListAndSelection(MissingCurveEntry curve, SkinnedMeshRenderer smr)
        {
            curve.AvailableBlendshapes.Clear();
            curve.NewBlendshapeName = null;

            if (smr?.sharedMesh == null)
            {
                return;
            }

            Mesh mesh = smr.sharedMesh;
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

        private string GetCanonicalGroupName(string propertyName, Type type)
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

        private static string GetObjectNameFromPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "根物体 (Root)";
            }

            string[] parts = path.Split('/');
            return parts.Length > 0 ? parts[parts.Length - 1] : path;
        }
    }
}
