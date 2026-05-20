using System;
using System.Collections.Generic;
using MVA.Toolbox.SwitchGenerator.Utils;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace MVA.Toolbox.SwitchGenerator.Entry
{
    internal static class SwitchGeneratorWindowOptions
    {
        internal readonly struct SelectionState
        {
            public SelectionState(int layerIndex, int parameterIndex, int menuPathIndex)
            {
                this.layerIndex = layerIndex;
                this.parameterIndex = parameterIndex;
                this.menuPathIndex = menuPathIndex;
            }

            public int layerIndex { get; }
            public int parameterIndex { get; }
            public int menuPathIndex { get; }
        }

        internal sealed class AvatarContext
        {
            public string[] availableLayerNames = Array.Empty<string>();
            public string[] availableParameterNames = Array.Empty<string>();
            public string[] availableMenuPaths = Array.Empty<string>();
        }

        public static AvatarContext BuildAvatarContext(VRCAvatarDescriptor avatar, SwitchGeneratorConfig.LayerConfig layer)
        {
            var context = new AvatarContext();
            if (avatar == null)
            {
                return context;
            }

            var fxController = AvatarAssetResolver.GetOrCreateFxController(avatar);
            var expressionParameters = AvatarAssetResolver.GetOrCreateExpressionParameters(avatar);
            var expressionsMenu = AvatarAssetResolver.GetOrCreateExpressionsMenu(avatar);
            context.availableLayerNames = BuildLayerNames(fxController);
            context.availableParameterNames = BuildParameterNames(expressionParameters, fxController, layer != null ? layer.switchType : SwitchGeneratorConfig.SwitchType.Bool);
            context.availableMenuPaths = SwitchGeneratorMenuPathOptions.Build(expressionsMenu);
            return context;
        }

        private static string[] BuildLayerNames(AnimatorController fxController)
        {
            if (fxController == null)
            {
                return Array.Empty<string>();
            }

            var list = new List<string>();
            for (int i = 0; i < fxController.layers.Length; i++)
            {
                var layer = fxController.layers[i];
                if (!string.IsNullOrEmpty(layer.name))
                {
                    list.Add(layer.name);
                }
            }

            return list.ToArray();
        }

        private static string[] BuildParameterNames(
            VRCExpressionParameters expressionParameters,
            AnimatorController fxController,
            SwitchGeneratorConfig.SwitchType switchType)
        {
            var typeMap = BuildAvailableParameterTypeMap(expressionParameters, fxController);
            if (typeMap.Count == 0)
            {
                return Array.Empty<string>();
            }

            var list = new List<string>();
            foreach (var kvp in typeMap)
            {
                switch (switchType)
                {
                    case SwitchGeneratorConfig.SwitchType.Bool:
                        if (kvp.Value == VRCExpressionParameters.ValueType.Bool) list.Add(kvp.Key);
                        break;
                    case SwitchGeneratorConfig.SwitchType.Int:
                        if (kvp.Value == VRCExpressionParameters.ValueType.Int) list.Add(kvp.Key);
                        break;
                    case SwitchGeneratorConfig.SwitchType.Float:
                        if (kvp.Value == VRCExpressionParameters.ValueType.Float) list.Add(kvp.Key);
                        break;
                }
            }

            if (list.Count > 1)
            {
                list.Sort(string.CompareOrdinal);
            }

            return list.ToArray();
        }

        public static string NormalizeClipRootPath(string path)
        {
            return AvatarAssetResolver.NormalizeAssetsRootPath(path);
        }

        public static SelectionState BuildSelectionState(
            SwitchGeneratorConfig.LayerConfig layer,
            string[] availableLayerNames,
            string[] availableParameterNames,
            string[] availableMenuPaths)
        {
            return new SelectionState(
                ResolveLayerSelectionIndex(layer, availableLayerNames),
                ResolveParameterSelectionIndex(layer, availableParameterNames),
                ResolveMenuSelectionIndex(layer, availableMenuPaths));
        }

        public static int EnsureLayerSelection(
            SwitchGeneratorConfig.LayerConfig layer,
            string[] availableLayerNames,
            int selectedLayerPopupIndex)
        {
            if (layer == null || !layer.overwriteLayer || !string.IsNullOrEmpty(layer.layerName) || availableLayerNames == null || availableLayerNames.Length == 0)
            {
                return selectedLayerPopupIndex;
            }

            int resolvedIndex = selectedLayerPopupIndex;
            if (resolvedIndex < 0 || resolvedIndex >= availableLayerNames.Length)
            {
                resolvedIndex = 0;
            }

            layer.layerName = availableLayerNames[resolvedIndex];
            return resolvedIndex;
        }

        public static int EnsureParameterSelection(
            SwitchGeneratorConfig.LayerConfig layer,
            string[] availableParameterNames,
            int selectedParameterPopupIndex)
        {
            if (layer == null || !layer.overwriteParameter || availableParameterNames == null || availableParameterNames.Length == 0)
            {
                return selectedParameterPopupIndex;
            }

            if (!string.IsNullOrEmpty(layer.parameterName))
            {
                return Array.IndexOf(availableParameterNames, layer.parameterName);
            }

            int resolvedIndex = selectedParameterPopupIndex;
            if (resolvedIndex < 0 || resolvedIndex >= availableParameterNames.Length)
            {
                resolvedIndex = 0;
                layer.parameterName = availableParameterNames[0];
            }

            return resolvedIndex;
        }

        public static void ApplySelectedParameterName(
            SwitchGeneratorConfig.LayerConfig layer,
            string[] availableParameterNames,
            int selectedParameterPopupIndex)
        {
            if (layer == null || !layer.overwriteParameter || availableParameterNames == null)
            {
                return;
            }

            if (selectedParameterPopupIndex >= 0 && selectedParameterPopupIndex < availableParameterNames.Length)
            {
                layer.parameterName = availableParameterNames[selectedParameterPopupIndex];
            }
        }

        public static int ResolveLayerSelectionIndex(SwitchGeneratorConfig.LayerConfig layer, string[] availableLayerNames)
        {
            return layer == null || availableLayerNames == null || availableLayerNames.Length == 0
                ? -1
                : Array.IndexOf(availableLayerNames, layer.layerName ?? string.Empty);
        }

        public static int ResolveParameterSelectionIndex(SwitchGeneratorConfig.LayerConfig layer, string[] availableParameterNames)
        {
            return layer == null || availableParameterNames == null || availableParameterNames.Length == 0
                ? -1
                : Array.IndexOf(availableParameterNames, layer.parameterName ?? string.Empty);
        }

        public static int ResolveMenuSelectionIndex(SwitchGeneratorConfig.LayerConfig layer, string[] availableMenuPaths)
        {
            if (availableMenuPaths == null || availableMenuPaths.Length == 0)
            {
                return -1;
            }

            string currentPath = layer != null ? layer.menuPath : string.Empty;
            int selectedMenuPathIndex = SwitchGeneratorMenuPathOptions.IndexOf(availableMenuPaths, currentPath);
            if (selectedMenuPathIndex < 0 && availableMenuPaths.Length > 0)
            {
                selectedMenuPathIndex = 0;
                if (layer != null)
                {
                    layer.menuPath = SwitchGeneratorMenuPathOptions.Resolve(availableMenuPaths, selectedMenuPathIndex, currentPath);
                }
            }

            return selectedMenuPathIndex;
        }

        private static Dictionary<string, VRCExpressionParameters.ValueType> BuildAvailableParameterTypeMap(
            VRCExpressionParameters expressionParameters,
            AnimatorController fxController)
        {
            var map = new Dictionary<string, VRCExpressionParameters.ValueType>(StringComparer.Ordinal);

            if (expressionParameters?.parameters != null)
            {
                for (int i = 0; i < expressionParameters.parameters.Length; i++)
                {
                    var parameter = expressionParameters.parameters[i];
                    if (parameter == null || string.IsNullOrEmpty(parameter.name)) continue;
                    if (!map.ContainsKey(parameter.name))
                    {
                        map[parameter.name] = parameter.valueType;
                    }
                }
            }

            if (fxController?.parameters != null)
            {
                for (int i = 0; i < fxController.parameters.Length; i++)
                {
                    var animatorParam = fxController.parameters[i];
                    if (animatorParam == null || string.IsNullOrEmpty(animatorParam.name)) continue;
                    var valueType = ConvertAnimatorParameterType(animatorParam.type);
                    if (valueType == null) continue;
                    map[animatorParam.name] = valueType.Value;
                }
            }

            return map;
        }

        private static VRCExpressionParameters.ValueType? ConvertAnimatorParameterType(UnityEngine.AnimatorControllerParameterType type)
        {
            switch (type)
            {
                case UnityEngine.AnimatorControllerParameterType.Bool:
                    return VRCExpressionParameters.ValueType.Bool;
                case UnityEngine.AnimatorControllerParameterType.Int:
                    return VRCExpressionParameters.ValueType.Int;
                case UnityEngine.AnimatorControllerParameterType.Float:
                    return VRCExpressionParameters.ValueType.Float;
                default:
                    return null;
            }
        }
    }
}
