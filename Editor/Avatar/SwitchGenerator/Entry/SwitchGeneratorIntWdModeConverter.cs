using System.Collections.Generic;
using UnityEngine;

namespace MVA.Toolbox.SwitchGenerator.Entry
{
    internal static class SwitchGeneratorIntWdModeConverter
    {
        public static void Apply(SwitchGeneratorConfig.LayerConfig layer, bool toWdOn)
        {
            if (layer?.intGroups == null || layer.intGroups.Count == 0)
            {
                return;
            }

            if (toWdOn)
            {
                StripDefaultEntriesForWdOn(layer.intGroups);
            }
            else
            {
                RebuildWdOffGroups(layer.intGroups);
            }
        }

        public static void SyncFromTemplate(SwitchGeneratorConfig.LayerConfig layer)
        {
            if (layer == null || layer.switchType != SwitchGeneratorConfig.SwitchType.Int || layer.intGroups == null || layer.intGroups.Count == 0)
            {
                return;
            }

            var template = layer.intGroups[0];
            if (template?.targets == null)
            {
                return;
            }

            for (int g = 1; g < layer.intGroups.Count; g++)
            {
                var group = layer.intGroups[g];
                if (group?.targets == null)
                {
                    continue;
                }

                var lookup = BuildTargetLookup(group.targets);
                var newTargets = new List<SwitchGeneratorConfig.TargetItem>();
                for (int i = 0; i < template.targets.Count; i++)
                {
                    var templateItem = template.targets[i];
                    string key = BuildTargetKey(templateItem);
                    if (!string.IsNullOrEmpty(key) && lookup.TryGetValue(key, out var existing))
                    {
                        newTargets.Add(SwitchGeneratorLayerConfigEditing.CloneTargetItem(existing));
                    }
                    else
                    {
                        newTargets.Add(SwitchGeneratorLayerConfigEditing.CloneTargetItem(templateItem));
                    }
                }

                if (newTargets.Count == 0)
                {
                    newTargets.Add(new SwitchGeneratorConfig.TargetItem());
                }

                group.targets = newTargets;
            }
        }

        private static void StripDefaultEntriesForWdOn(List<SwitchGeneratorConfig.IntGroup> groups)
        {
            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                if (group?.targets == null)
                {
                    continue;
                }

                for (int i = group.targets.Count - 1; i >= 0; i--)
                {
                    var item = group.targets[i];
                    if (item == null || item.targetObject == null || IsTargetAtDefault(item))
                    {
                        group.targets.RemoveAt(i);
                    }
                }
            }
        }

        private static void RebuildWdOffGroups(List<SwitchGeneratorConfig.IntGroup> groups)
        {
            var orderedKeys = new List<string>();
            var templates = new Dictionary<string, SwitchGeneratorConfig.TargetItem>(System.StringComparer.Ordinal);
            CollectTemplateTargets(groups, orderedKeys, templates);

            int targetGroupCount = Mathf.Max(1, groups?.Count ?? 0);
            var rebuilt = new List<SwitchGeneratorConfig.IntGroup>(targetGroupCount);
            for (int g = 0; g < targetGroupCount; g++)
            {
                var src = g < groups.Count ? groups[g] : null;
                var newGroup = new SwitchGeneratorConfig.IntGroup
                {
                    stateName = src?.stateName,
                    targets = new List<SwitchGeneratorConfig.TargetItem>()
                };

                var lookup = BuildTargetLookup(src?.targets);
                for (int i = 0; i < orderedKeys.Count; i++)
                {
                    var key = orderedKeys[i];
                    if (!templates.TryGetValue(key, out var template))
                    {
                        continue;
                    }

                    if (lookup.TryGetValue(key, out var existing))
                    {
                        newGroup.targets.Add(SwitchGeneratorLayerConfigEditing.CloneTargetItem(existing));
                    }
                    else
                    {
                        newGroup.targets.Add(CreateDefaultTargetFromTemplate(template));
                    }
                }

                if (newGroup.targets.Count == 0)
                {
                    newGroup.targets.Add(new SwitchGeneratorConfig.TargetItem());
                }

                rebuilt.Add(newGroup);
            }

            groups.Clear();
            groups.AddRange(rebuilt);
        }

        private static void CollectTemplateTargets(
            List<SwitchGeneratorConfig.IntGroup> groups,
            List<string> orderedKeys,
            Dictionary<string, SwitchGeneratorConfig.TargetItem> templates)
        {
            if (groups == null)
            {
                return;
            }

            for (int g = 0; g < groups.Count; g++)
            {
                var targets = groups[g]?.targets;
                if (targets == null)
                {
                    continue;
                }

                for (int i = 0; i < targets.Count; i++)
                {
                    var item = targets[i];
                    string key = BuildTargetKey(item);
                    if (string.IsNullOrEmpty(key) || templates.ContainsKey(key))
                    {
                        continue;
                    }

                    templates[key] = CloneTargetStructure(item);
                    orderedKeys.Add(key);
                }
            }
        }

        private static Dictionary<string, SwitchGeneratorConfig.TargetItem> BuildTargetLookup(List<SwitchGeneratorConfig.TargetItem> targets)
        {
            var map = new Dictionary<string, SwitchGeneratorConfig.TargetItem>(System.StringComparer.Ordinal);
            if (targets == null)
            {
                return map;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                var item = targets[i];
                string key = BuildTargetKey(item);
                if (!string.IsNullOrEmpty(key))
                {
                    map[key] = item;
                }
            }

            return map;
        }

        private static string BuildTargetKey(SwitchGeneratorConfig.TargetItem item)
        {
            if (item == null || item.targetObject == null)
            {
                return null;
            }

            int id = item.targetObject.GetInstanceID();
            string blendShape = item.controlType == SwitchGeneratorConfig.TargetControlType.BlendShape
                ? (item.blendShapeName ?? string.Empty)
                : string.Empty;
            return ((int)item.controlType) + ":" + id + ":" + blendShape;
        }

        private static bool IsTargetAtDefault(SwitchGeneratorConfig.TargetItem item)
        {
            if (item == null || item.targetObject == null)
            {
                return true;
            }

            if (item.controlType == SwitchGeneratorConfig.TargetControlType.GameObject)
            {
                bool desiredActive = item.boolObjectState == SwitchGeneratorConfig.BoolObjectState.Active;
                return desiredActive == item.targetObject.activeSelf;
            }

            if (string.IsNullOrWhiteSpace(item.blendShapeName))
            {
                return true;
            }

            if (!TryGetBlendShapeWeight(item.targetObject, item.blendShapeName, out float currentWeight))
            {
                currentWeight = 0f;
            }

            float desired = item.boolBlendShapeState == SwitchGeneratorConfig.BoolBlendShapeState.Full ? 100f : 0f;
            return Mathf.Approximately(desired, currentWeight);
        }

        private static SwitchGeneratorConfig.TargetItem CreateDefaultTargetFromTemplate(SwitchGeneratorConfig.TargetItem template)
        {
            var item = CloneTargetStructure(template);
            if (item.targetObject == null)
            {
                return item;
            }

            if (item.controlType == SwitchGeneratorConfig.TargetControlType.GameObject)
            {
                item.boolObjectState = item.targetObject.activeSelf
                    ? SwitchGeneratorConfig.BoolObjectState.Active
                    : SwitchGeneratorConfig.BoolObjectState.Inactive;
            }
            else
            {
                if (!TryGetBlendShapeWeight(item.targetObject, item.blendShapeName, out float currentWeight))
                {
                    currentWeight = 0f;
                }

                item.boolBlendShapeState = currentWeight >= 50f
                    ? SwitchGeneratorConfig.BoolBlendShapeState.Full
                    : SwitchGeneratorConfig.BoolBlendShapeState.Zero;
            }

            return item;
        }

        private static SwitchGeneratorConfig.TargetItem CloneTargetStructure(SwitchGeneratorConfig.TargetItem src)
        {
            if (src == null)
            {
                return new SwitchGeneratorConfig.TargetItem();
            }

            return new SwitchGeneratorConfig.TargetItem
            {
                targetObject = src.targetObject,
                controlType = src.controlType,
                blendShapeName = src.blendShapeName,
                splitBlendShape = src.splitBlendShape,
                secondaryBlendShapeName = src.secondaryBlendShapeName,
                secondaryFloatDirection = src.secondaryFloatDirection,
                floatDirection = src.floatDirection
            };
        }

        private static bool TryGetBlendShapeWeight(GameObject targetObject, string blendShapeName, out float weight)
        {
            weight = 0f;
            if (targetObject == null || string.IsNullOrWhiteSpace(blendShapeName))
            {
                return false;
            }

            var smr = targetObject.GetComponent<SkinnedMeshRenderer>() ?? targetObject.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr == null || smr.sharedMesh == null)
            {
                return false;
            }

            int index = smr.sharedMesh.GetBlendShapeIndex(blendShapeName);
            if (index < 0)
            {
                return false;
            }

            weight = smr.GetBlendShapeWeight(index);
            return true;
        }
    }
}
