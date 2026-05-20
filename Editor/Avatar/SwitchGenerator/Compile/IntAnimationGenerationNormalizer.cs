using System.Collections.Generic;
using MVA.Toolbox.SwitchGenerator.Spec;
using MVA.Toolbox.SwitchGenerator.Utils;
using UnityEngine;

namespace MVA.Toolbox.SwitchGenerator.Compile
{
    internal static class IntAnimationGenerationNormalizer
    {
        public static List<SwitchIntGroupSpec> Normalize(List<SwitchIntGroupSpec> groups)
        {
            var result = new List<SwitchIntGroupSpec>();
            if (groups == null || groups.Count == 0)
            {
                return result;
            }

            var orderedKeys = new List<string>();
            var templates = new Dictionary<string, SwitchTargetSpec>(System.StringComparer.Ordinal);
            CollectTemplateTargets(groups, orderedKeys, templates);

            for (int g = 0; g < groups.Count; g++)
            {
                var src = groups[g];
                var newGroup = new SwitchIntGroupSpec
                {
                    stateName = src?.stateName,
                    targets = new List<SwitchTargetSpec>()
                };

                var lookup = BuildTargetLookup(src?.targets);
                for (int i = 0; i < orderedKeys.Count; i++)
                {
                    string key = orderedKeys[i];
                    if (!templates.TryGetValue(key, out var template))
                    {
                        continue;
                    }

                    if (lookup.TryGetValue(key, out var existing))
                    {
                        newGroup.targets.Add(CloneTargetItem(existing));
                    }
                    else
                    {
                        newGroup.targets.Add(CreateDefaultTargetFromTemplate(template));
                    }
                }

                if (newGroup.targets.Count == 0)
                {
                    newGroup.targets.Add(new SwitchTargetSpec());
                }

                result.Add(newGroup);
            }

            return result;
        }

        private static void CollectTemplateTargets(
            List<SwitchIntGroupSpec> groups,
            List<string> orderedKeys,
            Dictionary<string, SwitchTargetSpec> templates)
        {
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

        private static Dictionary<string, SwitchTargetSpec> BuildTargetLookup(List<SwitchTargetSpec> targets)
        {
            var map = new Dictionary<string, SwitchTargetSpec>(System.StringComparer.Ordinal);
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

        private static string BuildTargetKey(SwitchTargetSpec item)
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

        private static SwitchTargetSpec CreateDefaultTargetFromTemplate(SwitchTargetSpec template)
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

        private static SwitchTargetSpec CloneTargetStructure(SwitchTargetSpec src)
        {
            if (src == null)
            {
                return new SwitchTargetSpec();
            }

            return new SwitchTargetSpec
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

        private static SwitchTargetSpec CloneTargetItem(SwitchTargetSpec src)
        {
            if (src == null)
            {
                return new SwitchTargetSpec();
            }

            return new SwitchTargetSpec
            {
                targetObject = src.targetObject,
                controlType = src.controlType,
                blendShapeName = src.blendShapeName,
                boolObjectState = src.boolObjectState,
                boolBlendShapeState = src.boolBlendShapeState,
                floatDirection = src.floatDirection,
                splitBlendShape = src.splitBlendShape,
                secondaryBlendShapeName = src.secondaryBlendShapeName,
                secondaryFloatDirection = src.secondaryFloatDirection
            };
        }

        private static bool TryGetBlendShapeWeight(GameObject targetObject, string blendShapeName, out float weight)
        {
            weight = 0f;
            if (targetObject == null || string.IsNullOrWhiteSpace(blendShapeName))
            {
                return false;
            }

            var smr = TargetObjectResolver.ResolveSkinnedMeshForBlendShape(targetObject);
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
