using System.Collections.Generic;
using UnityEngine;
using TargetItem = MVA.Toolbox.AvatarQuickToggle.ToggleConfig.TargetItem;
using IntStateGroup = MVA.Toolbox.AvatarQuickToggle.ToggleConfig.IntStateGroup;

namespace MVA.Toolbox.AvatarQuickToggle.Editor
{
    internal static class IntGroupSnapshotConverter
    {
        // 将基线快照筛减为 WD On 模式所需目标（PreviewStateManager 定义于 PreviewStateManager.cs）
        public static PreviewStateManager.BaselineSnapshot ToWDOn(PreviewStateManager manager, PreviewStateManager.BaselineSnapshot snapshot)
        {
            var result = new PreviewStateManager.BaselineSnapshot
            {
                groups = new List<IntStateGroup>()
            };

            for (int g = 0; g < snapshot.groups.Count; g++)
            {
                var group = snapshot.groups[g];
                if (group == null)
                {
                    result.groups.Add(new IntStateGroup { targetItems = new List<TargetItem>() });
                    continue;
                }

                var filteredGroup = new IntStateGroup
                {
                    stateName = group.stateName,
                    isFoldout = group.isFoldout,
                    targetItems = new List<TargetItem>()
                };

                foreach (var item in group.targetItems)
                {
                    if (item == null || item.targetObject == null) continue;

                    bool keep = ShouldKeepItem(manager, item);
                    if (!keep) continue;

                    filteredGroup.targetItems.Add(CopyItem(item));
                }

                result.groups.Add(filteredGroup);
            }

            return result;
        }

        // 根据现有快照重建 WD Off 模式目标，缺失条目使用首组模板
        public static PreviewStateManager.BaselineSnapshot ToWDOff(PreviewStateManager manager, PreviewStateManager.BaselineSnapshot snapshot)
        {
            var templates = new Dictionary<string, TargetItem>();
            var overrides = new Dictionary<(int groupIndex, string key), TargetItem>();

            for (int g = 0; g < snapshot.groups.Count; g++)
            {
                var group = snapshot.groups[g];
                if (group?.targetItems == null) continue;
                foreach (var item in group.targetItems)
                {
                    if (item == null || item.targetObject == null) continue;
                    string key = BuildKey(item);
                    if (!templates.ContainsKey(key))
                        templates[key] = CopyItem(item);
                    overrides[(g, key)] = item;
                }
            }

            var result = new PreviewStateManager.BaselineSnapshot
            {
                groups = new List<IntStateGroup>()
            };

            for (int g = 0; g < snapshot.groups.Count; g++)
            {
                var group = snapshot.groups[g];
                var rebuilt = new IntStateGroup
                {
                    stateName = group?.stateName,
                    isFoldout = group?.isFoldout ?? false,
                    targetItems = new List<TargetItem>()
                };

                foreach (var kv in templates)
                {
                    string key = kv.Key;
                    var template = kv.Value;

                    TargetItem copy;
                    if (overrides.TryGetValue((g, key), out var specific))
                    {
                        copy = CopyItem(specific);
                    }
                    else
                    {
                        copy = CopyItem(template);
                    }

                    if (copy.controlType == 0)
                    {
                        bool desired = ResolveActiveState(manager, template?.targetObject, g, key, overrides);
                        copy.onStateActiveSelection = desired ? 0 : 1;
                    }
                    else if (copy.controlType == 1)
                    {
                        float defaultWeight = ResolveBlendWeight(manager, template, g, key, overrides);
                        copy.onStateBlendShapeValue = defaultWeight >= 50f ? 1 : 0;
                    }

                    rebuilt.targetItems.Add(copy);
                }

                if (rebuilt.targetItems.Count == 0)
                    rebuilt.targetItems.Add(new TargetItem());

                result.groups.Add(rebuilt);
            }

            if (result.groups.Count == 0)
                result.groups.Add(new IntStateGroup { targetItems = new List<TargetItem> { new TargetItem() } });

            return result;
        }

        // 判断目标在 WD On 模式下是否需要保留
        private static bool ShouldKeepItem(PreviewStateManager manager, TargetItem item)
        {
            if (item.controlType == 0)
            {
                bool defaultActive;
                if (!manager.TryGetDefaultActiveState(item.targetObject, out defaultActive))
                    defaultActive = item.targetObject != null && item.targetObject.activeSelf;

                bool desiredActive = item.onStateActiveSelection == 0;
                return desiredActive != defaultActive;
            }

            if (item.controlType == 1)
            {
                if (string.IsNullOrEmpty(item.blendShapeName)) return false;
                float defaultWeight;
                if (!manager.TryGetDefaultBlendShape(item.targetObject, item.blendShapeName, out defaultWeight))
                    defaultWeight = GetCurrentBlendShapeWeight(item.targetObject, item.blendShapeName);

                float desiredWeight = DirectionToWeight(item.onStateBlendShapeValue);
                return !Mathf.Approximately(defaultWeight, desiredWeight);
            }

            return false;
        }

        // 获取 GameObject 在 WD Off 模式下的默认激活状态
        private static bool ResolveActiveState(PreviewStateManager manager, GameObject target, int groupIndex, string key, Dictionary<(int groupIndex, string key), TargetItem> overrides)
        {
            if (groupIndex != 0 && overrides.TryGetValue((groupIndex, key), out var specific))
                return specific.onStateActiveSelection == 0;

            if (manager.TryGetDefaultActiveState(target, out bool defaultActive))
                return defaultActive;

            return target != null && target.activeSelf;
        }

        // 计算 BlendShape 在 WD Off 模式的默认权重
        private static float ResolveBlendWeight(PreviewStateManager manager, TargetItem template, int groupIndex, string key, Dictionary<(int groupIndex, string key), TargetItem> overrides)
        {
            if (groupIndex != 0 && overrides.TryGetValue((groupIndex, key), out var specific))
                return DirectionToWeight(specific.onStateBlendShapeValue);

            return GetDefaultBlendWeight(manager, template);
        }

        // 克隆 TargetItem，避免引用共享
        private static TargetItem CopyItem(TargetItem src)
        {
            return new TargetItem
            {
                targetObject = src.targetObject,
                controlType = src.controlType,
                blendShapeName = src.blendShapeName,
                onStateActiveSelection = src.onStateActiveSelection,
                onStateBlendShapeValue = src.onStateBlendShapeValue,
                splitBlendShape = src.splitBlendShape,
                secondaryBlendShapeName = src.secondaryBlendShapeName,
                secondaryBlendShapeValue = src.secondaryBlendShapeValue
            };
        }

        // 组合控制类型与对象 ID 生成唯一键
        private static string BuildKey(TargetItem item)
        {
            int id = item.targetObject.GetInstanceID();
            string blend = item.controlType == 1 ? item.blendShapeName ?? string.Empty : string.Empty;
            return string.Concat(item.controlType, ":", id, ":", blend);
        }

        // 将方向枚举转为权重值（0 或 100）
        private static float DirectionToWeight(int direction)
        {
            return direction == 0 ? 0f : 100f;
        }

        // 获取 BlendShape 的默认权重，优先读取 PreviewStateManager 快照
        private static float GetDefaultBlendWeight(PreviewStateManager manager, TargetItem template)
        {
            if (template?.targetObject == null || string.IsNullOrEmpty(template.blendShapeName))
                return 0f;

            if (manager.TryGetDefaultBlendShape(template.targetObject, template.blendShapeName, out float weight))
                return weight;

            return GetCurrentBlendShapeWeight(template.targetObject, template.blendShapeName);
        }

        private static float GetCurrentBlendShapeWeight(GameObject go, string blendShapeName)
        {
            var smr = ResolveRenderer(go);
            if (smr == null || smr.sharedMesh == null) return 0f;
            int idx = smr.sharedMesh.GetBlendShapeIndex(blendShapeName);
            if (idx < 0) return 0f;
            return smr.GetBlendShapeWeight(idx);
        }

        private static SkinnedMeshRenderer ResolveRenderer(GameObject go)
        {
            if (go == null) return null;
            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr == null)
                smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
            return smr;
        }
    }
}
