using System.Collections.Generic;
using MVA.Toolbox.SwitchGenerator.Utils;

namespace MVA.Toolbox.SwitchGenerator.Entry
{
    internal static class SwitchGeneratorLayerConfigEditing
    {
        public static SwitchGeneratorConfig.LayerConfig CloneLayerConfig(SwitchGeneratorConfig.LayerConfig src)
        {
            if (src == null) return new SwitchGeneratorConfig.LayerConfig();
            var clone = new SwitchGeneratorConfig.LayerConfig
            {
                displayName = src.displayName,
                layerName = src.layerName,
                switchType = src.switchType,
                parameterName = src.parameterName,
                overwriteLayer = src.overwriteLayer,
                overwriteParameter = src.overwriteParameter,
                clipSaveRoot = src.clipSaveRoot,
                writeDefaults = src.writeDefaults,
                generateMenuControl = src.generateMenuControl,
                menuPath = src.menuPath,
                boolMenuItemName = src.boolMenuItemName,
                floatMenuItemName = src.floatMenuItemName,
                intSubMenuName = src.intSubMenuName,
                intMenuItemNames = new List<string>(src.intMenuItemNames ?? new List<string>()),
                savedParameter = src.savedParameter,
                syncedParameter = src.syncedParameter,
                defaultBoolValue = src.defaultBoolValue,
                defaultIntValue = src.defaultIntValue,
                defaultFloatValue = src.defaultFloatValue,
                editInWriteDefaultsOnMode = src.editInWriteDefaultsOnMode,
                boolTargets = new List<SwitchGeneratorConfig.TargetItem>(),
                intGroups = new List<SwitchGeneratorConfig.IntGroup>(),
                floatTargets = new List<SwitchGeneratorConfig.TargetItem>()
            };

            if (src.boolTargets != null)
            {
                for (int i = 0; i < src.boolTargets.Count; i++)
                {
                    clone.boolTargets.Add(CloneTargetItem(src.boolTargets[i]));
                }
            }

            if (src.intGroups != null)
            {
                for (int i = 0; i < src.intGroups.Count; i++)
                {
                    var group = src.intGroups[i];
                    var clonedGroup = new SwitchGeneratorConfig.IntGroup
                    {
                        stateName = group != null ? group.stateName : null,
                        targets = new List<SwitchGeneratorConfig.TargetItem>()
                    };
                    if (group?.targets != null)
                    {
                        for (int t = 0; t < group.targets.Count; t++)
                        {
                            clonedGroup.targets.Add(CloneTargetItem(group.targets[t]));
                        }
                    }
                    clone.intGroups.Add(clonedGroup);
                }
            }

            if (src.floatTargets != null)
            {
                for (int i = 0; i < src.floatTargets.Count; i++)
                {
                    clone.floatTargets.Add(CloneTargetItem(src.floatTargets[i]));
                }
            }

            clone.EnsureCollections();
            return clone;
        }

        public static SwitchGeneratorConfig.LayerConfig CreatePreparedLayerConfig()
        {
            var layer = new SwitchGeneratorConfig.LayerConfig();
            PrepareLayerConfig(layer);
            return layer;
        }

        public static SwitchGeneratorConfig.LayerConfig ClonePreparedLayerConfig(SwitchGeneratorConfig.LayerConfig src)
        {
            var layer = CloneLayerConfig(src);
            PrepareLayerConfig(layer);
            return layer;
        }

        public static void PrepareLayerConfig(SwitchGeneratorConfig.LayerConfig layer)
        {
            if (layer == null)
            {
                return;
            }

            layer.EnsureCollections();
            layer.menuPath = string.IsNullOrWhiteSpace(layer.menuPath)
                ? "/"
                : AvatarAssetResolver.NormalizeMenuPath(layer.menuPath);
        }

        public static SwitchGeneratorConfig.TargetItem CloneTargetItem(SwitchGeneratorConfig.TargetItem src)
        {
            if (src == null) return new SwitchGeneratorConfig.TargetItem();
            return new SwitchGeneratorConfig.TargetItem
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

        public static void EnsureDefaultTargets(SwitchGeneratorConfig.LayerConfig layer)
        {
            if (layer == null) return;
            layer.EnsureCollections();

            if (layer.switchType == SwitchGeneratorConfig.SwitchType.Bool)
            {
                if (layer.boolTargets.Count == 0)
                {
                    layer.boolTargets.Add(new SwitchGeneratorConfig.TargetItem());
                }
            }
            else if (layer.switchType == SwitchGeneratorConfig.SwitchType.Int)
            {
                if (layer.intGroups.Count == 0)
                {
                    layer.intGroups.Add(new SwitchGeneratorConfig.IntGroup
                    {
                        targets = new List<SwitchGeneratorConfig.TargetItem> { new SwitchGeneratorConfig.TargetItem() }
                    });
                }

                for (int g = 0; g < layer.intGroups.Count; g++)
                {
                    if (layer.intGroups[g].targets == null)
                    {
                        layer.intGroups[g].targets = new List<SwitchGeneratorConfig.TargetItem>();
                    }

                    if (layer.intGroups[g].targets.Count == 0)
                    {
                        layer.intGroups[g].targets.Add(new SwitchGeneratorConfig.TargetItem());
                    }
                }

                EnsureIntMenuNameCapacity(layer);
            }
            else if (layer.switchType == SwitchGeneratorConfig.SwitchType.Float)
            {
                if (layer.floatTargets.Count == 0)
                {
                    layer.floatTargets.Add(new SwitchGeneratorConfig.TargetItem
                    {
                        controlType = SwitchGeneratorConfig.TargetControlType.BlendShape
                    });
                }
            }
        }

        public static void EnsureIntMenuNameCapacity(SwitchGeneratorConfig.LayerConfig layer)
        {
            if (layer == null) return;
            layer.EnsureCollections();
            int targetCount = layer.intGroups.Count;
            while (layer.intMenuItemNames.Count < targetCount) layer.intMenuItemNames.Add(string.Empty);
            while (layer.intMenuItemNames.Count > targetCount && layer.intMenuItemNames.Count > 0) layer.intMenuItemNames.RemoveAt(layer.intMenuItemNames.Count - 1);
        }

        public static string BuildDefaultDisplayName(SwitchGeneratorConfig config, SwitchGeneratorConfig.LayerConfig layer)
        {
            int index = config != null && config.layers != null ? config.layers.Count + 1 : 1;
            string modeLabel = layer != null && layer.switchType == SwitchGeneratorConfig.SwitchType.Bool
                ? "Bool"
                : layer != null && layer.switchType == SwitchGeneratorConfig.SwitchType.Int
                    ? "Int"
                    : "Float";
            return $"{modeLabel}配置{index}";
        }
    }
}
