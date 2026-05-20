using System;
using System.Collections.Generic;
using MVA.Toolbox.SwitchGenerator.Utils;
using UnityEngine;

namespace MVA.Toolbox.SwitchGenerator.Spec
{
    internal static class SwitchGeneratorSpecNormalizer
    {
        public static void Normalize(SwitchGeneratorSpec spec)
        {
            if (spec == null)
            {
                return;
            }

            spec.layers ??= new List<SwitchLayerSpec>();
            for (int i = 0; i < spec.layers.Count; i++)
            {
                var layer = spec.layers[i];
                if (layer == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(layer.layerName))
                {
                    layer.layerName = $"SwitchLayer_{i + 1}";
                }
                else
                {
                    layer.layerName = layer.layerName.Trim();
                }

                if (string.IsNullOrWhiteSpace(layer.parameterName))
                {
                    layer.parameterName = layer.layerName + "_Param";
                }
                else
                {
                    layer.parameterName = layer.parameterName.Trim();
                }

                layer.clipSaveRoot = NormalizeAssetsRoot(layer.clipSaveRoot);
                layer.menuPath = AvatarAssetResolver.NormalizeMenuPath(layer.menuPath);

                layer.defaultBoolValue = Mathf.Clamp(layer.defaultBoolValue, 0, 1);
                layer.defaultIntValue = Mathf.Max(0, layer.defaultIntValue);
                layer.defaultFloatValue = Mathf.Clamp01(layer.defaultFloatValue);

                layer.intMenuItemNames ??= new List<string>();
                layer.boolTargets ??= new List<SwitchTargetSpec>();
                layer.intGroups ??= new List<SwitchIntGroupSpec>();
                layer.floatTargets ??= new List<SwitchTargetSpec>();

                NormalizeTargets(layer.boolTargets);
                NormalizeTargets(layer.floatTargets);
                NormalizeIntGroups(layer.intGroups);
            }
        }

        private static void NormalizeIntGroups(List<SwitchIntGroupSpec> groups)
        {
            if (groups == null)
            {
                return;
            }

            for (int i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                if (group == null)
                {
                    continue;
                }

                group.targets ??= new List<SwitchTargetSpec>();
                NormalizeTargets(group.targets);
            }
        }

        private static void NormalizeTargets(List<SwitchTargetSpec> targets)
        {
            if (targets == null)
            {
                return;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (target == null)
                {
                    continue;
                }

                target.blendShapeName = string.IsNullOrWhiteSpace(target.blendShapeName) ? string.Empty : target.blendShapeName.Trim();
                target.secondaryBlendShapeName = string.IsNullOrWhiteSpace(target.secondaryBlendShapeName) ? string.Empty : target.secondaryBlendShapeName.Trim();
            }
        }

        private static string NormalizeAssetsRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "Assets/MVA Toolbox/SwitchGenerator";
            }

            string normalized = path.Trim().Replace('\\', '/');
            if (string.Equals(normalized, "Assets", StringComparison.OrdinalIgnoreCase))
            {
                return "Assets";
            }

            if (!normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                return "Assets/MVA Toolbox/SwitchGenerator";
            }

            while (normalized.Length > 6 && normalized.EndsWith("/", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(0, normalized.Length - 1);
            }

            return normalized;
        }

    }
}
