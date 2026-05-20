using System.Collections.Generic;
using MVA.Toolbox.SwitchGenerator.Spec;
using MVA.Toolbox.SwitchGenerator.Utils;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace MVA.Toolbox.SwitchGenerator.Compile
{
    internal static class SwitchPlanCompiler
    {
        public static SwitchBuildPlan Compile(
            SwitchGeneratorSpec spec,
            AnimatorController controller,
            VRCExpressionParameters expressionParameters,
            bool persistAssets)
        {
            var plan = new SwitchBuildPlan
            {
                avatar = spec.avatar
            };

            var reservedLayerNames = new HashSet<string>();
            for (int i = 0; i < spec.layers.Count; i++)
            {
                var layer = spec.layers[i];
                if (layer == null)
                {
                    continue;
                }

                var lp = new SwitchLayerPlan
                {
                    displayName = layer.displayName,
                    layerName = ResolveLayerName(layer, controller, reservedLayerNames),
                    switchType = layer.switchType,
                    parameterName = ParameterNameAllocator.Allocate(layer.parameterName, layer.overwriteParameter, controller, expressionParameters),
                    overwriteLayer = layer.overwriteLayer,
                    overwriteParameter = layer.overwriteParameter,
                    clipSaveRoot = layer.clipSaveRoot,
                    writeDefaults = layer.writeDefaults,
                    generateMenuControl = layer.generateMenuControl,
                    menuPath = AvatarAssetResolver.NormalizeMenuPath(layer.menuPath),
                    savedParameter = layer.savedParameter,
                    syncedParameter = layer.syncedParameter,
                    defaultBoolValue = layer.defaultBoolValue,
                    defaultIntValue = layer.defaultIntValue,
                    defaultFloatValue = layer.defaultFloatValue,
                    persistAssets = persistAssets,
                    parameterType = ResolveParameterType(layer.switchType),
                    menuControlName = ResolveMenuName(layer)
                };

                if (layer.intMenuItemNames != null)
                {
                    lp.intMenuItemNames.AddRange(layer.intMenuItemNames);
                }

                if (layer.boolTargets != null)
                {
                    lp.boolTargets.AddRange(layer.boolTargets);
                }

                if (layer.intGroups != null)
                {
                    lp.intGroups.AddRange(IntAnimationGenerationNormalizer.Normalize(layer.intGroups));
                }

                if (layer.floatTargets != null)
                {
                    lp.floatTargets.AddRange(layer.floatTargets);
                }

                plan.layers.Add(lp);
            }

            return plan;
        }

        private static string ResolveLayerName(SwitchLayerSpec layer, AnimatorController controller, HashSet<string> reserved)
        {
            if (layer.overwriteLayer)
            {
                reserved.Add(layer.layerName);
                return layer.layerName;
            }

            string baseName = layer.layerName;
            string candidate = baseName;
            int suffix = 1;
            while (reserved.Contains(candidate) || LayerExists(controller, candidate))
            {
                candidate = baseName + "_" + suffix;
                suffix++;
            }

            reserved.Add(candidate);
            return candidate;
        }

        private static bool LayerExists(AnimatorController controller, string layerName)
        {
            if (controller?.layers == null)
            {
                return false;
            }

            for (int i = 0; i < controller.layers.Length; i++)
            {
                if (controller.layers[i].name == layerName)
                {
                    return true;
                }
            }

            return false;
        }

        private static string ResolveMenuName(SwitchLayerSpec layer)
        {
            switch (layer.switchType)
            {
                case SwitchGeneratorConfig.SwitchType.Bool:
                    if (!string.IsNullOrWhiteSpace(layer.boolMenuItemName)) return layer.boolMenuItemName;
                    break;
                case SwitchGeneratorConfig.SwitchType.Int:
                    if (!string.IsNullOrWhiteSpace(layer.intSubMenuName)) return layer.intSubMenuName;
                    break;
                case SwitchGeneratorConfig.SwitchType.Float:
                    if (!string.IsNullOrWhiteSpace(layer.floatMenuItemName)) return layer.floatMenuItemName;
                    break;
            }

            return layer.layerName;
        }

        private static VRCExpressionParameters.ValueType ResolveParameterType(SwitchGeneratorConfig.SwitchType type)
        {
            switch (type)
            {
                case SwitchGeneratorConfig.SwitchType.Int:
                    return VRCExpressionParameters.ValueType.Int;
                case SwitchGeneratorConfig.SwitchType.Float:
                    return VRCExpressionParameters.ValueType.Float;
                default:
                    return VRCExpressionParameters.ValueType.Bool;
            }
        }
    }
}
