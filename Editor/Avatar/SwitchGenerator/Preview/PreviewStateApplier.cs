using MVA.Toolbox.SwitchGenerator.Spec;
using MVA.Toolbox.SwitchGenerator.Utils;
using UnityEngine;

namespace MVA.Toolbox.SwitchGenerator.Preview
{
    internal static class PreviewStateApplier
    {
        public static void Apply(SwitchLayerSpec layer, float value)
        {
            if (layer == null)
            {
                return;
            }

            switch (layer.switchType)
            {
                case SwitchGeneratorConfig.SwitchType.Bool:
                    ApplyBool(layer, value >= 0.5f);
                    break;
                case SwitchGeneratorConfig.SwitchType.Int:
                    ApplyInt(layer, Mathf.RoundToInt(value));
                    break;
                case SwitchGeneratorConfig.SwitchType.Float:
                    ApplyFloat(layer, Mathf.Clamp01(value));
                    break;
            }
        }

        private static void ApplyBool(SwitchLayerSpec layer, bool on)
        {
            for (int i = 0; i < layer.boolTargets.Count; i++)
            {
                var target = layer.boolTargets[i];
                if (target?.targetObject == null)
                {
                    continue;
                }

                if (target.controlType == SwitchGeneratorConfig.TargetControlType.GameObject)
                {
                    bool onState = target.boolObjectState == SwitchGeneratorConfig.BoolObjectState.Active;
                    target.targetObject.SetActive(on ? onState : !onState);
                }
                else
                {
                    float onValue = target.boolBlendShapeState == SwitchGeneratorConfig.BoolBlendShapeState.Full ? 100f : 0f;
                    float weight = on ? onValue : 100f - onValue;
                    SetBlendShape(target.targetObject, target.blendShapeName, weight);
                }
            }
        }

        private static void ApplyInt(SwitchLayerSpec layer, int index)
        {
            if (layer.intGroups == null || layer.intGroups.Count == 0)
            {
                return;
            }

            int groupIndex = Mathf.Clamp(index, 0, layer.intGroups.Count - 1);
            var group = layer.intGroups[groupIndex];
            if (group?.targets == null)
            {
                return;
            }

            for (int i = 0; i < group.targets.Count; i++)
            {
                var target = group.targets[i];
                if (target?.targetObject == null)
                {
                    continue;
                }

                if (target.controlType == SwitchGeneratorConfig.TargetControlType.GameObject)
                {
                    bool active = target.boolObjectState == SwitchGeneratorConfig.BoolObjectState.Active;
                    target.targetObject.SetActive(active);
                }
                else
                {
                    float weight = target.boolBlendShapeState == SwitchGeneratorConfig.BoolBlendShapeState.Full ? 100f : 0f;
                    SetBlendShape(target.targetObject, target.blendShapeName, weight);
                }
            }
        }

        private static void ApplyFloat(SwitchLayerSpec layer, float value)
        {
            for (int i = 0; i < layer.floatTargets.Count; i++)
            {
                var target = layer.floatTargets[i];
                if (target?.targetObject == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(target.blendShapeName))
                {
                    SetBlendShape(target.targetObject, target.blendShapeName, Evaluate(value, target.floatDirection));
                }

                if (target.splitBlendShape && !string.IsNullOrWhiteSpace(target.secondaryBlendShapeName))
                {
                    SetBlendShape(target.targetObject, target.secondaryBlendShapeName, Evaluate(value, target.secondaryFloatDirection));
                }
            }
        }

        private static float Evaluate(float value, SwitchGeneratorConfig.FloatDirection direction)
        {
            float t = Mathf.Clamp01(value);
            return direction == SwitchGeneratorConfig.FloatDirection.FullToZero
                ? (1f - t) * 100f
                : t * 100f;
        }

        private static void SetBlendShape(GameObject target, string blendShapeName, float weight)
        {
            if (target == null || string.IsNullOrWhiteSpace(blendShapeName))
            {
                return;
            }

            var mappedTargets = TargetObjectResolver.ResolveOriginalBlendShapeTargets(target, blendShapeName);
            bool applied = false;
            if (mappedTargets != null && mappedTargets.Count > 0)
            {
                for (int i = 0; i < mappedTargets.Count; i++)
                {
                    var mapped = mappedTargets[i];
                    var smr = mapped.renderer;
                    var originalName = mapped.originalName;
                    if (smr == null || smr.sharedMesh == null || string.IsNullOrEmpty(originalName))
                    {
                        continue;
                    }

                    int mappedIndex = smr.sharedMesh.GetBlendShapeIndex(originalName);
                    if (mappedIndex < 0)
                    {
                        continue;
                    }

                    smr.SetBlendShapeWeight(mappedIndex, weight);
                    applied = true;
                }
            }

            if (applied)
            {
                return;
            }

            var fallbackSmr = TargetObjectResolver.ResolveSkinnedMeshForBlendShape(target);
            if (fallbackSmr == null || fallbackSmr.sharedMesh == null)
            {
                return;
            }

            int index = fallbackSmr.sharedMesh.GetBlendShapeIndex(blendShapeName);
            if (index >= 0)
            {
                fallbackSmr.SetBlendShapeWeight(index, weight);
            }
        }
    }
}
