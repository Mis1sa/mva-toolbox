using System.Collections.Generic;
using UnityEngine;
using IntStateGroup = MVA.Toolbox.AvatarQuickToggle.ToggleConfig.IntStateGroup;
using TargetItem = MVA.Toolbox.AvatarQuickToggle.ToggleConfig.TargetItem;

namespace MVA.Toolbox.AvatarQuickToggle.Editor
{
    internal static class IntGroupSnapshotConverter
    {
        public static void StripDefaultEntriesForWDOn(List<IntStateGroup> groups, PreviewStateManager preview)
        {
            if (groups == null || preview == null) return;
            foreach (var group in groups)
            {
                if (group?.targetItems == null) continue;
                for (int i = group.targetItems.Count - 1; i >= 0; i--)
                {
                    var item = group.targetItems[i];
                    if (item == null || item.targetObject == null || IsItemAtDefault(item, preview))
                        group.targetItems.RemoveAt(i);
                }
            }
        }

        public static List<IntStateGroup> RebuildWDOffGroups(List<IntStateGroup> groups, PreviewStateManager preview)
        {
            var templates = new Dictionary<string, TargetItem>();
            var orderedKeys = new List<string>();
            CollectTemplateKeys(groups, orderedKeys, templates);

            int targetGroupCount = Mathf.Max(1, groups?.Count ?? 0);
            var rebuilt = new List<IntStateGroup>(targetGroupCount);
            for (int g = 0; g < targetGroupCount; g++)
            {
                var src = (groups != null && g < groups.Count) ? groups[g] : null;
                var group = new IntStateGroup
                {
                    stateName = src?.stateName,
                    isFoldout = src?.isFoldout ?? false,
                    targetItems = new List<TargetItem>()
                };

                var lookup = BuildTargetLookup(src?.targetItems);
                foreach (var key in orderedKeys)
                {
                    if (!templates.TryGetValue(key, out var template)) continue;
                    if (lookup.TryGetValue(key, out var existing))
                        group.targetItems.Add(CloneTargetItem(existing));
                    else
                        group.targetItems.Add(CreateDefaultItemFromTemplate(template, preview));
                }

                if (group.targetItems.Count == 0)
                    group.targetItems.Add(new TargetItem());

                rebuilt.Add(group);
            }

            return rebuilt;
        }

        public static void SyncStructureFromTemplate(List<IntStateGroup> groups)
        {
            if (groups == null || groups.Count == 0) return;
            var templateGroup = groups[0];
            if (templateGroup?.targetItems == null)
                templateGroup.targetItems = new List<TargetItem>();
            int templateCount = templateGroup.targetItems.Count;

            for (int g = 1; g < groups.Count; g++)
            {
                var dstGroup = groups[g];
                if (dstGroup.targetItems == null) dstGroup.targetItems = new List<TargetItem>();

                while (dstGroup.targetItems.Count > templateCount)
                    dstGroup.targetItems.RemoveAt(dstGroup.targetItems.Count - 1);

                while (dstGroup.targetItems.Count < templateCount)
                    dstGroup.targetItems.Add(CloneStructureOnly(templateGroup.targetItems[dstGroup.targetItems.Count]));

                for (int j = 0; j < templateCount; j++)
                {
                    var srcItem = templateGroup.targetItems[j];
                    if (srcItem == null)
                    {
                        dstGroup.targetItems[j] = new TargetItem();
                        continue;
                    }

                    var dstItem = dstGroup.targetItems[j];
                    if (dstItem == null)
                    {
                        dstGroup.targetItems[j] = CloneStructureOnly(srcItem);
                        continue;
                    }

                    dstItem.targetObject = srcItem.targetObject;
                    dstItem.controlType = srcItem.controlType;
                    dstItem.blendShapeName = srcItem.blendShapeName;
                    dstItem.splitBlendShape = srcItem.splitBlendShape;
                    dstItem.secondaryBlendShapeName = srcItem.secondaryBlendShapeName;
                    dstItem.secondaryBlendShapeValue = srcItem.secondaryBlendShapeValue;
                }

                groups[g] = dstGroup;
            }
        }

        private static void CollectTemplateKeys(List<IntStateGroup> source, List<string> orderedKeys, Dictionary<string, TargetItem> templates)
        {
            if (source == null) return;
            foreach (var group in source)
            {
                if (group?.targetItems == null) continue;
                foreach (var item in group.targetItems)
                {
                    string key = BuildTargetKey(item);
                    if (string.IsNullOrEmpty(key) || templates.ContainsKey(key)) continue;
                    templates[key] = CloneStructureOnly(item);
                    orderedKeys.Add(key);
                }
            }
        }

        private static Dictionary<string, TargetItem> BuildTargetLookup(List<TargetItem> items)
        {
            var dict = new Dictionary<string, TargetItem>();
            if (items == null) return dict;
            foreach (var item in items)
            {
                string key = BuildTargetKey(item);
                if (string.IsNullOrEmpty(key)) continue;
                dict[key] = item;
            }
            return dict;
        }

        private static string BuildTargetKey(TargetItem item)
        {
            if (item == null || item.targetObject == null) return null;
            int id = item.targetObject.GetInstanceID();
            string blend = item.controlType == 1 ? (item.blendShapeName ?? string.Empty) : string.Empty;
            return $"{item.controlType}:{id}:{blend}";
        }

        private static TargetItem CloneStructureOnly(TargetItem src)
        {
            if (src == null) return new TargetItem();
            return new TargetItem
            {
                targetObject = src.targetObject,
                controlType = src.controlType,
                blendShapeName = src.blendShapeName,
                splitBlendShape = src.splitBlendShape,
                secondaryBlendShapeName = src.secondaryBlendShapeName,
                secondaryBlendShapeValue = src.secondaryBlendShapeValue
            };
        }

        private static TargetItem CreateDefaultItemFromTemplate(TargetItem template, PreviewStateManager preview)
        {
            var item = CloneStructureOnly(template);
            if (item.targetObject == null || preview == null) return item;

            if (item.controlType == 0)
            {
                if (!preview.TryGetDefaultActiveState(item.targetObject, out bool defaultActive))
                    defaultActive = item.targetObject.activeSelf;
                item.onStateActiveSelection = defaultActive ? 0 : 1;
            }
            else if (item.controlType == 1)
            {
                if (!preview.TryGetDefaultBlendShape(item.targetObject, item.blendShapeName, out float defaultWeight))
                    defaultWeight = 0f;
                item.onStateBlendShapeValue = defaultWeight >= 50f ? 1 : 0;
            }

            return item;
        }

        private static bool IsItemAtDefault(TargetItem item, PreviewStateManager preview)
        {
            if (item == null || item.targetObject == null) return true;
            if (preview == null) return false;

            if (item.controlType == 0)
            {
                if (!preview.TryGetDefaultActiveState(item.targetObject, out bool defaultActive))
                    defaultActive = item.targetObject.activeSelf;
                bool desired = item.onStateActiveSelection == 0;
                return desired == defaultActive;
            }

            if (item.controlType == 1)
            {
                if (string.IsNullOrEmpty(item.blendShapeName)) return true;
                if (!preview.TryGetDefaultBlendShape(item.targetObject, item.blendShapeName, out float defaultWeight))
                    defaultWeight = 0f;
                float desiredWeight = item.onStateBlendShapeValue == 0 ? 0f : 100f;
                return Mathf.Approximately(defaultWeight, desiredWeight);
            }

            return true;
        }

        private static TargetItem CloneTargetItem(TargetItem src)
        {
            if (src == null) return new TargetItem();
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
    }
}
