using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.AnimationQueryTool
{
    internal sealed partial class AnimationQueryToolService
    {
        private void ScanAndGroupAnimatedProperties()
        {
            _availableGroups.Clear();
            _selectedGroupIndex = 0;
            _selectedBlendshapeOptionIndex = 0;
            _searchCompleted = false;
            _foundClips.Clear();
            _currentBlendshapeOptions.Clear();

            if (_selectedAnimatedObject == null || _targetRoot == null || _selectedTransform == null)
            {
                return;
            }

            // _selectedPath = null;

            Dictionary<(string path, string propertyName, Type type), EditorCurveBinding> uniqueBindings =
                new Dictionary<(string path, string propertyName, Type type), EditorCurveBinding>();
            Dictionary<AnimatorController, string> controllerPathCache = new Dictionary<AnimatorController, string>();

            ForEachLayerInScope((controller, layer, controllerScope) =>
            {
                if (!TryGetSelectedPathForController(controller, controllerScope.RootTransform, controllerScope.IgnoresNestedAnimators, controllerPathCache, out string relativePath))
                {
                    return;
                }

                HashSet<AnimationClip> clips = new HashSet<AnimationClip>();
                CollectClipsFromStateMachine(layer.stateMachine, clips);
                foreach (AnimationClip clip in clips)
                {
                    if (clip == null)
                    {
                        continue;
                    }

                    List<EditorCurveBinding> bindings = new List<EditorCurveBinding>();
                    bindings.AddRange(AnimationUtility.GetCurveBindings(clip));
                    bindings.AddRange(AnimationUtility.GetObjectReferenceCurveBindings(clip));
                    for (int i = 0; i < bindings.Count; i++)
                    {
                        EditorCurveBinding binding = bindings[i];
                        if (binding.path != relativePath)
                        {
                            continue;
                        }

                        var key = (binding.path, binding.propertyName, binding.type);
                        if (!uniqueBindings.ContainsKey(key))
                        {
                            uniqueBindings.Add(key, binding);
                        }
                    }
                }
            });

            Dictionary<(Type type, string canonicalName), PropertyGroupData> tempGroups =
                new Dictionary<(Type type, string canonicalName), PropertyGroupData>();

            foreach (EditorCurveBinding binding in uniqueBindings.Values)
            {
                string basePropertyName = binding.propertyName;
                int dotIndex = basePropertyName.IndexOf('.');
                string canonicalName = dotIndex > 0 ? basePropertyName.Substring(0, dotIndex) : basePropertyName;

                var groupKey = (binding.type, canonicalName);
                if (!tempGroups.TryGetValue(groupKey, out PropertyGroupData group))
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

            AugmentGroupsWithSwitchGeneratorConfigForTarget();
            _availableGroups.Sort((a, b) => string.Compare(a.GroupDisplayName, b.GroupDisplayName, StringComparison.Ordinal));

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

            PropertyGroupData group = _availableGroups[_selectedGroupIndex - 1];
            HashSet<string> uniqueNames = new HashSet<string>(StringComparer.Ordinal);
            List<string> discoveredOrder = new List<string>();
            for (int i = 0; i < group.BoundPropertyNames.Count; i++)
            {
                string property = group.BoundPropertyNames[i];
                if (string.IsNullOrEmpty(property) || !property.StartsWith("blendShape.", StringComparison.Ordinal))
                {
                    continue;
                }

                string name = property.Substring("blendShape.".Length);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                if (uniqueNames.Add(name))
                {
                    discoveredOrder.Add(name);
                }
            }

            _currentBlendshapeOptions.Add("全部");
            if (uniqueNames.Count > 0)
            {
                _currentBlendshapeOptions.AddRange(BuildMeshOrderedBlendShapes(uniqueNames, discoveredOrder));
            }

            _selectedBlendshapeOptionIndex = 0;
        }

        private List<string> BuildMeshOrderedBlendShapes(HashSet<string> remainingNames, List<string> fallbackOrder)
        {
            List<string> ordered = new List<string>();
            SkinnedMeshRenderer smr = ResolveSelectedSkinnedMeshRenderer();
            if (smr?.sharedMesh != null)
            {
                Mesh mesh = smr.sharedMesh;
                int count = mesh.blendShapeCount;
                for (int i = 0; i < count; i++)
                {
                    string shapeName = mesh.GetBlendShapeName(i);
                    if (string.IsNullOrEmpty(shapeName))
                    {
                        continue;
                    }

                    if (remainingNames.Remove(shapeName))
                    {
                        ordered.Add(shapeName);
                    }
                }
            }

            if (remainingNames.Count > 0 && fallbackOrder != null)
            {
                for (int i = 0; i < fallbackOrder.Count; i++)
                {
                    string name = fallbackOrder[i];
                    if (remainingNames.Remove(name))
                    {
                        ordered.Add(name);
                    }
                }
            }

            if (remainingNames.Count > 0)
            {
                ordered.AddRange(remainingNames);
            }

            return ordered;
        }

        private SkinnedMeshRenderer ResolveSelectedSkinnedMeshRenderer()
        {
            if (_selectedAnimatedObject is SkinnedMeshRenderer smr)
            {
                return smr;
            }

            if (_selectedAnimatedObject is Component component)
            {
                return component.GetComponent<SkinnedMeshRenderer>();
            }

            if (_selectedAnimatedObject is GameObject go)
            {
                return go.GetComponent<SkinnedMeshRenderer>();
            }

            return null;
        }

        private void FindClipsForSelectedGroup()
        {
            _searchCompleted = false;
            _foundClips.Clear();

            if (_controllers == null ||
                _controllers.Count == 0 ||
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

            HashSet<AnimationClip> usedClips = new HashSet<AnimationClip>();
            HashSet<string> allowedProperties = null;
            bool filterBlendshape = false;
            if (group != null &&
                group.ComponentType == typeof(SkinnedMeshRenderer) &&
                string.Equals(group.CanonicalPropertyName, "blendShape", StringComparison.Ordinal))
            {
                if (_selectedBlendshapeOptionIndex > 0 && _selectedBlendshapeOptionIndex < _currentBlendshapeOptions.Count)
                {
                    string selected = _currentBlendshapeOptions[_selectedBlendshapeOptionIndex];
                    if (!string.IsNullOrEmpty(selected))
                    {
                        allowedProperties = new HashSet<string>(StringComparer.Ordinal) { "blendShape." + selected };
                        filterBlendshape = true;
                    }
                }
            }

            Dictionary<AnimatorController, string> controllerPathCache = new Dictionary<AnimatorController, string>();
            ForEachLayerInScope((controller, layer, controllerScope) =>
            {
                if (!TryGetSelectedPathForController(controller, controllerScope.RootTransform, controllerScope.IgnoresNestedAnimators, controllerPathCache, out string relativePath))
                {
                    return;
                }

                HashSet<AnimationClip> clips = new HashSet<AnimationClip>();
                CollectClipsFromStateMachine(layer.stateMachine, clips);
                foreach (AnimationClip clip in clips)
                {
                    if (clip == null || usedClips.Contains(clip))
                    {
                        continue;
                    }

                    List<EditorCurveBinding> bindings = new List<EditorCurveBinding>();
                    bindings.AddRange(AnimationUtility.GetCurveBindings(clip));
                    bindings.AddRange(AnimationUtility.GetObjectReferenceCurveBindings(clip));

                    bool hasMatch = false;
                    for (int i = 0; i < bindings.Count; i++)
                    {
                        EditorCurveBinding binding = bindings[i];
                        if (binding.path != relativePath)
                        {
                            continue;
                        }

                        if (useAllGroups)
                        {
                            hasMatch = true;
                            break;
                        }

                        if (binding.type != group.ComponentType)
                        {
                            continue;
                        }

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

                    if (!hasMatch)
                    {
                        continue;
                    }

                    usedClips.Add(clip);
                    _foundClips.Add(new FoundClipInfo
                    {
                        Clip = clip,
                        Controller = controller
                    });
                }
            });

            _searchCompleted = true;
        }
    }
}
