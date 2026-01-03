using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.AnimPathRedirect.Services
{
    // 组件/约束相关的绑定快照服务，仅被 AnimPathRedirectService 调用（见 AnimPathRedirectService.cs）
    internal sealed class AnimPathRedirectComponentService
    {
        // 单条曲线绑定的信息，用于后续判断组件是否被移除
        internal sealed class ConstraintBindingInfo
        {
            public AnimationClip Clip;
            public EditorCurveBinding Binding;
            public string Path;
            public Type ComponentType;
            public bool IsEnabledProperty;
            public bool IsSourceProperty;
            public int SourceIndex;
            // 记录快照时该路径下是否存在此组件类型
            public bool ComponentPresentAtSnapshot;
        }

        // 按路径 + 组件类型聚合后的快照信息（包含参与动画的源索引）
        internal sealed class ConstraintComponentSnapshot
        {
            public string Path;
            public Type ComponentType;
            public HashSet<int> SourceIndices = new HashSet<int>();
        }

        // 所有参与动画的组件曲线
        readonly List<ConstraintBindingInfo> _constraintBindings = new List<ConstraintBindingInfo>();
        // 聚合后的组件级快照
        readonly List<ConstraintComponentSnapshot> _constraintComponents = new List<ConstraintComponentSnapshot>();

        public IReadOnlyList<ConstraintBindingInfo> ConstraintBindings => _constraintBindings;
        public IReadOnlyList<ConstraintComponentSnapshot> ConstraintComponents => _constraintComponents;

        public void Clear()
        {
            _constraintBindings.Clear();
            _constraintComponents.Clear();
        }

        // 从一组 AnimationClip 中提取与组件/约束相关的绑定，构建快照
        public void BuildSnapshot(GameObject root, IEnumerable<AnimationClip> clips)
        {
            Clear();

            if (root == null)
            {
                return;
            }

            if (clips == null)
            {
                return;
            }

            var clipList = clips.Distinct().ToList();
            if (clipList.Count == 0)
            {
                return;
            }

            var rootTransform = root.transform;

            foreach (var clip in clipList)
            {
                if (clip == null)
                {
                    continue;
                }

                var curveBindings = AnimationUtility.GetCurveBindings(clip);
                var refBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                var allBindings = curveBindings.Concat(refBindings);

                foreach (var binding in allBindings)
                {
                    if (binding.type == null)
                    {
                        continue;
                    }

                    if (binding.type == typeof(GameObject) || binding.type == typeof(Transform))
                    {
                        continue;
                    }

                    var targetTransform = rootTransform.Find(binding.path);
                    GameObject go = targetTransform != null ? targetTransform.gameObject : null;
                    bool hasComponent = go != null && go.GetComponent(binding.type) != null;

                    bool isEnabledProperty = string.Equals(binding.propertyName, "m_Enabled", StringComparison.Ordinal);

                    int sourceIndex;
                    bool isSourceProperty = TryGetSourceIndex(binding.propertyName, out sourceIndex);

                    bool isBlendshapeProperty = binding.type == typeof(SkinnedMeshRenderer) &&
                                               binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal);

                    if (!isEnabledProperty && !isSourceProperty && !isBlendshapeProperty)
                    {
                        continue;
                    }

                    var info = new ConstraintBindingInfo
                    {
                        Clip = clip,
                        Binding = binding,
                        Path = binding.path,
                        ComponentType = binding.type,
                        IsEnabledProperty = isEnabledProperty,
                        IsSourceProperty = isSourceProperty,
                        SourceIndex = isSourceProperty ? sourceIndex : -1,
                        ComponentPresentAtSnapshot = hasComponent
                    };

                    _constraintBindings.Add(info);
                }
            }

            // 将单条绑定按 Path + ComponentType 聚合，便于后续组件级运算
            var grouped = _constraintBindings
                .GroupBy(x => new { x.Path, x.ComponentType })
                .ToList();

            foreach (var group in grouped)
            {
                var snapshot = new ConstraintComponentSnapshot
                {
                    Path = group.Key.Path,
                    ComponentType = group.Key.ComponentType
                };

                foreach (var entry in group)
                {
                    if (entry.IsSourceProperty && entry.SourceIndex >= 0)
                    {
                        snapshot.SourceIndices.Add(entry.SourceIndex);
                    }
                }

                _constraintComponents.Add(snapshot);
            }
        }

        // 从属性名中解析约束源的下标，支持 Unity 内置约束与部分 VRC 组件的命名
        bool TryGetSourceIndex(string propertyName, out int sourceIndex)
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
                    var number = propertyName.Substring(start, end - start);
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
                    var number = propertyName.Substring(start, end - start);
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
