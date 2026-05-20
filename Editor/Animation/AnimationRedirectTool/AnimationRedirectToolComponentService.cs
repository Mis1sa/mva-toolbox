using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.AnimationRedirectTool
{
    internal sealed class AnimationRedirectToolComponentService
    {
        internal sealed class ConstraintBindingInfo
        {
            internal AnimationClip Clip;
            internal EditorCurveBinding Binding;
            internal string Path;
            internal Type ComponentType;
            internal bool IsEnabledProperty;
            internal bool IsSourceProperty;
            internal int SourceIndex;
            internal bool ComponentPresentAtSnapshot;
        }

        internal sealed class ConstraintComponentSnapshot
        {
            internal string Path;
            internal Type ComponentType;
            internal readonly HashSet<int> SourceIndices = new HashSet<int>();
        }

        private readonly List<ConstraintBindingInfo> _constraintBindings = new List<ConstraintBindingInfo>();
        private readonly List<ConstraintComponentSnapshot> _constraintComponents = new List<ConstraintComponentSnapshot>();

        internal IReadOnlyList<ConstraintBindingInfo> ConstraintBindings => _constraintBindings;
        internal IReadOnlyList<ConstraintComponentSnapshot> ConstraintComponents => _constraintComponents;

        internal void Clear()
        {
            _constraintBindings.Clear();
            _constraintComponents.Clear();
        }

        internal void BuildSnapshot(GameObject root, IEnumerable<AnimationClip> clips)
        {
            Clear();

            if (root == null || clips == null)
            {
                return;
            }

            List<AnimationClip> clipList = clips.Distinct().ToList();
            if (clipList.Count == 0)
            {
                return;
            }

            Transform rootTransform = root.transform;
            foreach (AnimationClip clip in clipList)
            {
                if (clip == null)
                {
                    continue;
                }

                IEnumerable<EditorCurveBinding> allBindings = AnimationUtility.GetCurveBindings(clip)
                    .Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip));

                foreach (EditorCurveBinding binding in allBindings)
                {
                    if (binding.type == null)
                    {
                        continue;
                    }

                    if (binding.type == typeof(GameObject) || binding.type == typeof(Transform))
                    {
                        continue;
                    }

                    Transform targetTransform = ResolveTransformByPath(rootTransform, binding.path);
                    GameObject go = targetTransform != null ? targetTransform.gameObject : null;
                    bool hasComponent = go != null && go.GetComponent(binding.type) != null;

                    bool isEnabledProperty = string.Equals(binding.propertyName, "m_Enabled", StringComparison.Ordinal);
                    bool isSourceProperty = TryGetSourceIndex(binding.propertyName, out int sourceIndex);
                    bool isBlendshapeProperty = binding.type == typeof(SkinnedMeshRenderer) &&
                                                binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal);

                    if (!isEnabledProperty && !isSourceProperty && !isBlendshapeProperty)
                    {
                        continue;
                    }

                    _constraintBindings.Add(new ConstraintBindingInfo
                    {
                        Clip = clip,
                        Binding = binding,
                        Path = binding.path,
                        ComponentType = binding.type,
                        IsEnabledProperty = isEnabledProperty,
                        IsSourceProperty = isSourceProperty,
                        SourceIndex = isSourceProperty ? sourceIndex : -1,
                        ComponentPresentAtSnapshot = hasComponent
                    });
                }
            }

            List<IGrouping<(string Path, Type ComponentType), ConstraintBindingInfo>> grouped = _constraintBindings
                .GroupBy(entry => (entry.Path, entry.ComponentType))
                .ToList();

            foreach (IGrouping<(string Path, Type ComponentType), ConstraintBindingInfo> group in grouped)
            {
                ConstraintComponentSnapshot snapshot = new ConstraintComponentSnapshot
                {
                    Path = group.Key.Path,
                    ComponentType = group.Key.ComponentType
                };

                foreach (ConstraintBindingInfo entry in group)
                {
                    if (entry.IsSourceProperty && entry.SourceIndex >= 0)
                    {
                        snapshot.SourceIndices.Add(entry.SourceIndex);
                    }
                }

                _constraintComponents.Add(snapshot);
            }
        }

        private static Transform ResolveTransformByPath(Transform root, string path)
        {
            if (root == null || path == null)
            {
                return null;
            }

            if (path.Length == 0)
            {
                return root;
            }

            return root.Find(path);
        }

        private static bool TryGetSourceIndex(string propertyName, out int sourceIndex)
        {
            sourceIndex = -1;
            if (string.IsNullOrEmpty(propertyName))
            {
                return false;
            }

            const string builtinPrefix = "m_Sources.Array.data[";
            int index = propertyName.IndexOf(builtinPrefix, StringComparison.Ordinal);
            if (index >= 0)
            {
                int start = index + builtinPrefix.Length;
                int end = propertyName.IndexOf(']', start);
                if (end > start)
                {
                    string number = propertyName.Substring(start, end - start);
                    if (int.TryParse(number, out sourceIndex))
                    {
                        return true;
                    }
                }
            }

            const string vrcPrefix = "Sources.source";
            index = propertyName.IndexOf(vrcPrefix, StringComparison.Ordinal);
            if (index >= 0)
            {
                int start = index + vrcPrefix.Length;
                int end = start;
                while (end < propertyName.Length && char.IsDigit(propertyName[end]))
                {
                    end++;
                }

                if (end > start)
                {
                    string number = propertyName.Substring(start, end - start);
                    if (int.TryParse(number, out sourceIndex))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
