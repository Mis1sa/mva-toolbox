using System.Collections.Generic;
using UnityEngine;
using MVA.Toolbox.Public;
using TargetItem = MVA.Toolbox.AvatarQuickToggle.ToggleConfig.TargetItem;
using IntStateGroup = MVA.Toolbox.AvatarQuickToggle.ToggleConfig.IntStateGroup;

namespace MVA.Toolbox.AvatarQuickToggle.Editor
{
    // 管理 Avatar 预览时的默认状态与 BlendShape 快照
    internal class PreviewStateManager
    {
        private readonly Dictionary<GameObject, bool> defaultActiveStates = new Dictionary<GameObject, bool>();
        private readonly Dictionary<SkinnedMeshRenderer, Dictionary<string, float>> defaultBlendShapeValues = new Dictionary<SkinnedMeshRenderer, Dictionary<string, float>>();
        private readonly Dictionary<GameObject, bool> previewActiveSnapshot = new Dictionary<GameObject, bool>();
        private readonly Dictionary<SkinnedMeshRenderer, Dictionary<string, float>> previewBlendSnapshot = new Dictionary<SkinnedMeshRenderer, Dictionary<string, float>>();

        public enum BlendShapePreviewMode
        {
            Directional,
            DiscreteToggle,
            DiscreteOnOnly
        }

        public IReadOnlyDictionary<GameObject, bool> DefaultStates => defaultActiveStates;

        public void Dispose()
        {
            defaultActiveStates.Clear();
            defaultBlendShapeValues.Clear();
            previewActiveSnapshot.Clear();
            previewBlendSnapshot.Clear();
        }

        public void RestorePreviewSnapshot()
        {
            foreach (var kv in previewActiveSnapshot)
            {
                if (kv.Key != null)
                    kv.Key.SetActive(kv.Value);
            }

            foreach (var kv in previewBlendSnapshot)
            {
                var smr = kv.Key;
                if (smr == null || smr.sharedMesh == null) continue;
                foreach (var pair in kv.Value)
                {
                    int idx = smr.sharedMesh.GetBlendShapeIndex(pair.Key);
                    if (idx >= 0)
                        smr.SetBlendShapeWeight(idx, pair.Value);
                }
            }
        }

        // 遍历 Avatar 层级，记录激活状态和 BlendShape 权重，可选地作为“默认快照”存档
        public void CaptureAvatarSnapshot(GameObject root, bool storeAsDefault)
        {
            previewActiveSnapshot.Clear();
            previewBlendSnapshot.Clear();
            if (storeAsDefault)
            {
                defaultActiveStates.Clear();
                defaultBlendShapeValues.Clear();
            }

            if (root == null) return;

            var stack = new Stack<Transform>();
            stack.Push(root.transform);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                var go = current.gameObject;

                bool isActive = go.activeSelf;
                previewActiveSnapshot[go] = isActive;
                if (storeAsDefault)
                    defaultActiveStates[go] = isActive;

                foreach (Transform child in current)
                {
                    if (child != null)
                        stack.Push(child);
                }

                var renderers = go.GetComponents<SkinnedMeshRenderer>();
                foreach (var smr in renderers)
                {
                    if (smr == null || smr.sharedMesh == null) continue;

                    var mesh = smr.sharedMesh;
                    int blendCount = mesh.blendShapeCount;

                    var previewMap = new Dictionary<string, float>(blendCount);
                    previewBlendSnapshot[smr] = previewMap;

                    Dictionary<string, float> defaultMap = null;
                    if (storeAsDefault)
                    {
                        defaultMap = new Dictionary<string, float>(blendCount);
                        defaultBlendShapeValues[smr] = defaultMap;
                    }

                    for (int i = 0; i < blendCount; i++)
                    {
                        string name = mesh.GetBlendShapeName(i);
                        float weight = smr.GetBlendShapeWeight(i);
                        previewMap[name] = weight;
                        if (storeAsDefault)
                            defaultMap[name] = weight;
                    }
                }
            }
        }

        // 获取物体在默认快照中的激活状态，若无记录则以当前状态为默认
        public bool TryGetDefaultActiveState(GameObject go, out bool isActive)
        {
            if (go == null)
            {
                isActive = false;
                return false;
            }

            if (previewActiveSnapshot.TryGetValue(go, out isActive))
                return true;

            if (defaultActiveStates.TryGetValue(go, out isActive))
                return true;

            isActive = go.activeSelf;
            defaultActiveStates[go] = isActive;
            return true;
        }

        // 获取 BlendShape 在默认快照中的权重，若无记录则从当前 SMR 读取并缓存
        public bool TryGetDefaultBlendShape(GameObject go, string shapeName, out float weight)
        {
            weight = 0f;
            if (go == null || string.IsNullOrEmpty(shapeName)) return false;

            if (TryGetFromPreviewSnapshot(go, shapeName, out weight))
                return true;

            var smr = ResolveRenderer(go);
            if (smr == null)
                return false;

            if (defaultBlendShapeValues.TryGetValue(smr, out var map) && map.TryGetValue(shapeName, out weight))
                return true;

            if (smr.sharedMesh == null) return false;
            int idx = smr.sharedMesh.GetBlendShapeIndex(shapeName);
            if (idx < 0) return false;

            weight = smr.GetBlendShapeWeight(idx);
            if (!defaultBlendShapeValues.TryGetValue(smr, out map))
            {
                map = new Dictionary<string, float>();
                defaultBlendShapeValues[smr] = map;
            }
            map[shapeName] = weight;
            return true;
        }

        // 将场景状态重置为捕获快照时的默认激活与 BlendShape 权重
        public void ApplyBaselineDefaults()
        {
            foreach (var kv in defaultActiveStates)
            {
                if (kv.Key != null)
                    kv.Key.SetActive(kv.Value);
            }

            foreach (var kv in defaultBlendShapeValues)
            {
                var smr = kv.Key;
                if (smr == null || smr.sharedMesh == null) continue;
                foreach (var pair in kv.Value)
                {
                    int idx = smr.sharedMesh.GetBlendShapeIndex(pair.Key);
                    if (idx >= 0)
                        smr.SetBlendShapeWeight(idx, pair.Value);
                }
            }
        }

        // 应用单个 Bool/BlendShape 目标的预览状态
        public void ApplyTargetState(TargetItem item, bool useOnState, BlendShapePreviewMode blendShapeMode = BlendShapePreviewMode.Directional)
        {
            if (item == null || item.targetObject == null) return;

            if (item.controlType == 0)
            {
                bool activeWhenOn = item.onStateActiveSelection == 0;
                bool desired = useOnState ? activeWhenOn : !activeWhenOn;
                item.targetObject.SetActive(desired);
            }
            else if (item.controlType == 1)
            {
                switch (blendShapeMode)
                {
                    case BlendShapePreviewMode.DiscreteOnOnly:
                        if (!useOnState) return;
                        ApplyDiscreteWeights(item, true);
                        break;
                    case BlendShapePreviewMode.DiscreteToggle:
                        ApplyDiscreteWeights(item, useOnState);
                        break;
                    default:
                        ApplyDirectionalWeights(item, useOnState ? 1f : 0f);
                        break;
                }
            }
        }

        // 离散模式：在 0/100 之间切换主/副 BlendShape 权重
        private void ApplyDiscreteWeights(TargetItem item, bool useOnState)
        {
            if (!item.splitBlendShape || string.IsNullOrEmpty(item.secondaryBlendShapeName))
            {
                float onWeight = MapDiscreteWeight(item.onStateBlendShapeValue);
                float weight = useOnState ? onWeight : 100f - onWeight;
                ApplyBlendShape(item.targetObject, item.blendShapeName, weight);
            }
            else
            {
                float primaryOn = MapDiscreteWeight(item.onStateBlendShapeValue);
                float secondaryOn = MapDiscreteWeight(item.secondaryBlendShapeValue);
                float primaryWeight = useOnState ? primaryOn : 100f - primaryOn;
                float secondaryWeight = useOnState ? secondaryOn : 100f - secondaryOn;
                ApplyBlendShape(item.targetObject, item.blendShapeName, primaryWeight);
                ApplyBlendShape(item.targetObject, item.secondaryBlendShapeName, secondaryWeight);
            }
        }

        // 连续模式：根据归一化 t 在 0~100 范围内插值权重
        private void ApplyDirectionalWeights(TargetItem item, float t)
        {
            if (!item.splitBlendShape || string.IsNullOrEmpty(item.secondaryBlendShapeName))
            {
                float weight = EvaluateWeight(item.onStateBlendShapeValue, t);
                ApplyBlendShape(item.targetObject, item.blendShapeName, weight);
            }
            else
            {
                float primaryWeight = EvaluateWeight(item.onStateBlendShapeValue, t);
                float secondaryWeight = EvaluateWeight(item.secondaryBlendShapeValue, t);
                ApplyBlendShape(item.targetObject, item.blendShapeName, primaryWeight);
                ApplyBlendShape(item.targetObject, item.secondaryBlendShapeName, secondaryWeight);
            }
        }

        // Float 模式：根据 normalized 在主/副 BlendShape 间分段平滑过渡
        public void ApplySplitFloatState(TargetItem item, float normalized)
        {
            if (item == null || item.targetObject == null) return;
            normalized = Mathf.Clamp01(normalized);

            if (!item.splitBlendShape)
            {
                float weight = EvaluateWeight(item.onStateBlendShapeValue, normalized);
                ApplyBlendShape(item.targetObject, item.blendShapeName, weight);
                return;
            }

            if (string.IsNullOrEmpty(item.secondaryBlendShapeName))
            {
                float primaryWeight;
                if (normalized <= 0.5f)
                {
                    float progress = normalized / 0.5f;
                    primaryWeight = EvaluateWeight(item.onStateBlendShapeValue, progress);
                }
                else
                {
                    primaryWeight = EvaluateWeight(item.onStateBlendShapeValue, 1f);
                }

                ApplyBlendShape(item.targetObject, item.blendShapeName, primaryWeight);
                return;
            }

            float primarySegment;
            float secondarySegment;

            if (normalized <= 0.5f)
            {
                float progress = normalized / 0.5f;
                primarySegment = EvaluateWeight(item.onStateBlendShapeValue, progress);
                secondarySegment = EvaluateWeight(item.secondaryBlendShapeValue, 0f);
            }
            else
            {
                float progress = (normalized - 0.5f) / 0.5f;
                primarySegment = EvaluateWeight(item.onStateBlendShapeValue, 1f);
                secondarySegment = EvaluateWeight(item.secondaryBlendShapeValue, progress);
            }

            ApplyBlendShape(item.targetObject, item.blendShapeName, primarySegment);
            ApplyBlendShape(item.targetObject, item.secondaryBlendShapeName, secondarySegment);
        }

        private Component FindAaoMergeComponent(GameObject target)
        {
            if (target == null) return null;

            var parents = target.GetComponentsInParent<Component>(true);
            for (int i = 0; i < parents.Length; i++)
            {
                var c = parents[i];
                if (c == null) continue;
                var t = c.GetType();
                if (t != null && t.FullName == "Anatawa12.AvatarOptimizer.MergeSkinnedMesh")
                {
                    return c;
                }
            }

            return null;
        }

        private SkinnedMeshRenderer ResolveRenderer(GameObject go)
        {
            if (go == null) return null;
            // 预览时复用动画生成阶段的 AAO 解析逻辑，保证能找到实际驱动的 SkinnedMeshRenderer
            return ToolboxUtils.ResolveSkinnedMeshForBlendShape(go);
        }

        private bool TryGetFromPreviewSnapshot(GameObject go, string shapeName, out float weight)
        {
            weight = 0f;
            var smr = ResolveRenderer(go);
            if (smr == null) return false;
            return previewBlendSnapshot.TryGetValue(smr, out var map) && map != null && map.TryGetValue(shapeName, out weight);
        }

        // 将 0/1 选项映射为 0/100 权重
        private float MapDiscreteWeight(int option)
        {
            return option == 0 ? 0f : 100f;
        }

        // 根据方向与归一化 t 计算权重（0~100）
        private float EvaluateWeight(int direction, float t)
        {
            t = Mathf.Clamp01(t);
            return (direction == 0 ? t : 1f - t) * 100f;
        }

        private float EvaluateSplitPrimary(int direction, float t)
        {
            return EvaluateWeight(direction, Mathf.Clamp01(t));
        }

        private float EvaluateSplitSecondary(int direction, float t)
        {
            return EvaluateWeight(direction, Mathf.Clamp01(t));
        }

        // 为指定目标应用 BlendShape 权重，优先使用 AAO 映射，其次回退到单一 SMR 与 MergeSkinnedMesh 子层级
        private void ApplyBlendShape(GameObject target, string blendShapeName, float weight)
        {
            if (target == null || string.IsNullOrEmpty(blendShapeName)) return;
            var mappedTargets = ToolboxUtils.ResolveOriginalBlendShapeTargets(target, blendShapeName);
            bool applied = false;
            if (mappedTargets != null && mappedTargets.Count > 0)
            {
                for (int i = 0; i < mappedTargets.Count; i++)
                {
                    var t = mappedTargets[i];
                    var smr = t.renderer;
                    var originalName = t.originalName;
                    if (smr == null || smr.sharedMesh == null || string.IsNullOrEmpty(originalName)) continue;
                    int idx = smr.sharedMesh.GetBlendShapeIndex(originalName);
                    if (idx < 0) continue;
                    smr.SetBlendShapeWeight(idx, weight);
                    applied = true;
                }
            }

            if (!applied)
            {
                var smr = ResolveRenderer(target);
                if (smr != null && smr.sharedMesh != null)
                {
                    int idx = smr.sharedMesh.GetBlendShapeIndex(blendShapeName);
                    if (idx >= 0)
                    {
                        smr.SetBlendShapeWeight(idx, weight);
                        applied = true;
                    }
                }

                if (!applied)
                {
                    var merge = FindAaoMergeComponent(target);
                    if (merge != null)
                    {
                        var childSmrs = merge.gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                        foreach (var child in childSmrs)
                        {
                            if (child == null || child.sharedMesh == null) continue;
                            int idx = child.sharedMesh.GetBlendShapeIndex(blendShapeName);
                            if (idx < 0) continue;
                            child.SetBlendShapeWeight(idx, weight);
                            applied = true;
                        }
                    }
                }
            }
        }

        private List<IntStateGroup> CloneGroups(List<IntStateGroup> groups)
        {
            var cloned = new List<IntStateGroup>();
            if (groups == null) return cloned;
            foreach (var g in groups)
            {
                if (g == null)
                {
                    cloned.Add(new IntStateGroup { targetItems = new List<TargetItem>() });
                    continue;
                }

                var cg = new IntStateGroup
                {
                    stateName = g.stateName,
                    isFoldout = g.isFoldout,
                    targetItems = new List<TargetItem>()
                };

                if (g.targetItems != null)
                {
                    foreach (var it in g.targetItems)
                    {
                        var copy = new TargetItem
                        {
                            targetObject = it.targetObject,
                            controlType = it.controlType,
                            blendShapeName = it.blendShapeName,
                            onStateActiveSelection = it.onStateActiveSelection,
                            onStateBlendShapeValue = it.onStateBlendShapeValue,
                            splitBlendShape = it.splitBlendShape,
                            secondaryBlendShapeName = it.secondaryBlendShapeName,
                            secondaryBlendShapeValue = it.secondaryBlendShapeValue
                        };
                        cg.targetItems.Add(copy);
                    }
                }

                cloned.Add(cg);
            }
            return cloned;
        }
    }
}
