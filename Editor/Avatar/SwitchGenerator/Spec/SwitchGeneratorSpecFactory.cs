namespace MVA.Toolbox.SwitchGenerator.Spec
{
    internal static class SwitchGeneratorSpecFactory
    {
        public static SwitchGeneratorSpec FromLayer(VRC.SDK3.Avatars.Components.VRCAvatarDescriptor avatar, SwitchGeneratorConfig.LayerConfig layer)
        {
            var spec = new SwitchGeneratorSpec
            {
                avatar = avatar
            };

            var mappedLayer = CreateLayerSpec(layer);
            if (mappedLayer != null)
            {
                spec.layers.Add(mappedLayer);
            }

            return spec;
        }

        public static SwitchGeneratorSpec FromConfig(SwitchGeneratorConfig config)
        {
            var spec = new SwitchGeneratorSpec
            {
                avatar = config != null ? config.targetAvatar : null
            };

            if (config?.layers == null)
            {
                return spec;
            }

            for (int i = 0; i < config.layers.Count; i++)
            {
                var src = config.layers[i];
                if (src == null)
                {
                    continue;
                }

                var layer = CreateLayerSpec(src);

                if (layer != null)
                {
                    spec.layers.Add(layer);
                }
            }

            return spec;
        }

        public static SwitchLayerSpec CreateLayerSpec(SwitchGeneratorConfig.LayerConfig src)
        {
            if (src == null)
            {
                return null;
            }

            var layer = new SwitchLayerSpec
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
                intSubMenuName = src.intSubMenuName,
                floatMenuItemName = src.floatMenuItemName,
                savedParameter = src.savedParameter,
                syncedParameter = src.syncedParameter,
                defaultBoolValue = src.defaultBoolValue,
                defaultIntValue = src.defaultIntValue,
                defaultFloatValue = src.defaultFloatValue
            };

            if (src.intMenuItemNames != null)
            {
                layer.intMenuItemNames.AddRange(src.intMenuItemNames);
            }

            CopyTargets(src.boolTargets, layer.boolTargets);
            CopyTargets(src.floatTargets, layer.floatTargets);

            if (src.intGroups != null)
            {
                for (int g = 0; g < src.intGroups.Count; g++)
                {
                    var srcGroup = src.intGroups[g];
                    if (srcGroup == null)
                    {
                        continue;
                    }

                    var group = new SwitchIntGroupSpec
                    {
                        stateName = srcGroup.stateName
                    };

                    CopyTargets(srcGroup.targets, group.targets);
                    layer.intGroups.Add(group);
                }
            }

            return layer;
        }

        public static SwitchTargetSpec CreateTargetSpec(SwitchGeneratorConfig.TargetItem src)
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

        private static void CopyTargets(
            System.Collections.Generic.List<SwitchGeneratorConfig.TargetItem> source,
            System.Collections.Generic.List<SwitchTargetSpec> destination)
        {
            if (source == null)
            {
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                var src = source[i];
                if (src == null)
                {
                    continue;
                }

                destination.Add(CreateTargetSpec(src));
            }
        }
    }
}
