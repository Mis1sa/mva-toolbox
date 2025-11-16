using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using MVA.Toolbox.AvatarQuickToggle.Services;
using MVA.Toolbox.Public;

namespace MVA.Toolbox.AvatarQuickToggle.Workflows
{
    public class NDMFApplyWorkflow
    {
        private readonly IReadOnlyList<QuickToggleConfig.LayerConfig> _layerConfigs;
        private readonly VRCAvatarDescriptor _authoringDescriptor;
        private readonly AnimationService _animationService = new AnimationService();
        private readonly VRCAssetsService _vrcAssetsService = new VRCAssetsService();

        // 作者侧菜单路径到克隆菜单的映射，用于在 NDMF 中保持菜单路径一致
        private System.Collections.Generic.Dictionary<string, VRCExpressionsMenu> _authoringMenuPathMap;
        private System.Collections.Generic.Dictionary<VRCExpressionsMenu, VRCExpressionsMenu> _menuCloneMap;

        public NDMFApplyWorkflow(
            VRCAvatarDescriptor avatarDescriptor,
            IReadOnlyList<QuickToggleConfig.LayerConfig> aggregatedLayers)
        {
            _authoringDescriptor = avatarDescriptor;
            _layerConfigs = aggregatedLayers ?? new List<QuickToggleConfig.LayerConfig>();
        }

        public void Execute(BuildContext context)
        {
            if (context == null || context.AvatarRootObject == null)
            {
                Debug.LogWarning("NDMFApplyWorkflow.Execute: invalid BuildContext or AvatarRootObject.");
                return;
            }

            var runtimeDescriptor = context.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            if (runtimeDescriptor == null)
            {
                Debug.LogWarning("NDMFApplyWorkflow.Execute: BuildContext avatar does not contain VRCAvatarDescriptor; skip.");
                return;
            }

            if (_authoringDescriptor != null && _authoringDescriptor != runtimeDescriptor)
            {
                Debug.LogWarning(
                    "NDMFApplyWorkflow.Execute: QuickToggleConfig target avatar differs from runtime avatar (expected '"
                    + _authoringDescriptor.gameObject.name + "', runtime '" + runtimeDescriptor.gameObject.name
                    + "'). Using runtime descriptor.");
            }

            // 使用 NDMF 的 AssetContainer 获取或创建构建期 FX/参数/菜单资源
            var fxController = GetOrCreateFXController(context, runtimeDescriptor);
            var expressionParameters = GetOrCreateExpressionParameters(context, runtimeDescriptor);
            var expressionsMenu = GetOrCreateExpressionsMenu(context, runtimeDescriptor);

            if (fxController == null || expressionParameters == null || expressionsMenu == null)
            {
                Debug.LogWarning("NDMFApplyWorkflow.Execute: failed to prepare NDMF avatar resources; skip.");
                return;
            }

            ApplyAggregatedLayers(context, runtimeDescriptor, fxController, expressionParameters, expressionsMenu);
        }

        private void ApplyAggregatedLayers(
            BuildContext context,
            VRCAvatarDescriptor runtimeDescriptor,
            AnimatorController fxController,
            VRCExpressionParameters expressionParameters,
            VRCExpressionsMenu expressionsMenu)
        {
            if (_layerConfigs == null || _layerConfigs.Count == 0)
            {
                Debug.Log($"NDMFApplyWorkflow: no layer configs found for avatar '{runtimeDescriptor.gameObject.name}'.");
                return;
            }

            for (int i = 0; i < _layerConfigs.Count; i++)
            {
                var layerData = _layerConfigs[i];
                if (layerData == null)
                {
                    Debug.LogWarning($"NDMFApplyWorkflow: layer index {i} is null, skipping.");
                    continue;
                }

                var toggleConfig = ConvertToToggleConfig(layerData, runtimeDescriptor);
                if (!ToggleConfigValidator.Validate(toggleConfig.config, out var error))
                {
                    Debug.LogWarning($"NDMFApplyWorkflow: layer '{layerData.displayName ?? layerData.layerName}' invalid: {error}");
                    continue;
                }

                ResolveParameterName(toggleConfig.config, fxController, expressionParameters);

                // FX 层创建逻辑复用 AnimationService（定义于 AnimationService.cs）
                _animationService.CreateLayer(fxController, toggleConfig, null);

                ApplyParametersAndMenu(toggleConfig.config, expressionParameters, expressionsMenu, context.AssetContainer);
            }
        }

        private ToggleConfig ConvertToToggleConfig(QuickToggleConfig.LayerConfig source, VRCAvatarDescriptor avatar)
        {
            var toggle = new ToggleConfig
            {
                avatar = avatar,
                config = new ToggleConfig.LayerConfig
                {
                    layerName = string.IsNullOrWhiteSpace(source.layerName) ? "AQT_Layer" : source.layerName,
                    layerType = source.layerType,
                    parameterName = string.IsNullOrWhiteSpace(source.parameterName) ? source.layerName + "_Param" : source.parameterName,
                    overwriteLayer = source.overwriteLayer,
                    overwriteParameter = source.overwriteParameter,
                    clipSavePath = "Assets/__AQT_NDMF__/Temp",
                    writeDefaultSetting = source.writeDefaultSetting,
                    createMenuControl = source.createMenuControl,
                    menuControlName = source.menuControlName,
                    boolMenuItemName = source.boolMenuItemName,
                    floatMenuItemName = source.floatMenuItemName,
                    intSubMenuName = source.intSubMenuName,
                    intMenuItemNames = source.intMenuItemNames != null ? new List<string>(source.intMenuItemNames) : new List<string>(),
                    menuPath = string.IsNullOrWhiteSpace(source.menuPath) ? "/" : source.menuPath,
                    savedParameter = source.savedParameter,
                    syncedParameter = source.syncedParameter,
                    defaultStateSelection = Mathf.Clamp(source.defaultStateSelection, 0, 1),
                    defaultIntValue = Mathf.Max(0, source.defaultIntValue),
                    defaultFloatValue = Mathf.Clamp01(source.defaultFloatValue),
                    editInWDOnMode = source.editInWDOnMode
                }
            };

            if (source.boolTargets != null)
            {
                foreach (var target in source.boolTargets)
                {
                    toggle.config.boolTargets.Add(ConvertTarget(target));
                }
            }

            if (source.floatTargets != null)
            {
                foreach (var target in source.floatTargets)
                {
                    toggle.config.floatTargets.Add(ConvertTarget(target));
                }
            }

            if (source.intGroups != null)
            {
                foreach (var group in source.intGroups)
                {
                    var newGroup = new ToggleConfig.IntStateGroup
                    {
                        stateName = group?.stateName ?? string.Empty
                    };
                    if (group?.targetItems != null)
                    {
                        foreach (var target in group.targetItems)
                        {
                            newGroup.targetItems.Add(ConvertTarget(target));
                        }
                    }
                    toggle.config.intGroups.Add(newGroup);
                }
            }

            return toggle;
        }

        private ToggleConfig.TargetItem ConvertTarget(QuickToggleConfig.TargetItemData source)
        {
            if (source == null)
            {
                return new ToggleConfig.TargetItem();
            }

            return new ToggleConfig.TargetItem
            {
                targetObject = source.targetObject,
                controlType = source.controlType == QuickToggleConfig.TargetControlType.GameObject ? 0 : 1,
                blendShapeName = source.blendShapeName,
                onStateActiveSelection = source.goState == QuickToggleConfig.GameObjectState.Active ? 0 : 1,
                onStateBlendShapeValue = source.bsState == QuickToggleConfig.BlendShapeState.Zero ? 0 : 1,
                splitBlendShape = source.splitBlendShape,
                secondaryBlendShapeName = source.secondaryBlendShapeName,
                secondaryBlendShapeValue = source.secondaryDirection == QuickToggleConfig.FloatDirection.ZeroToFull ? 0 : 1
            };
        }

        private void ResolveParameterName(
            ToggleConfig.LayerConfig layer,
            AnimatorController fxController,
            VRCExpressionParameters expressionParameters)
        {
            if (layer == null || string.IsNullOrWhiteSpace(layer.parameterName)) return;
            if (layer.overwriteParameter) return;

            var usedNames = new HashSet<string>(StringComparer.Ordinal);

            if (fxController?.parameters != null)
            {
                foreach (var param in fxController.parameters)
                {
                    if (param != null && !string.IsNullOrEmpty(param.name))
                    {
                        usedNames.Add(param.name);
                    }
                }
            }

            if (expressionParameters?.parameters != null)
            {
                foreach (var param in expressionParameters.parameters)
                {
                    if (param != null && !string.IsNullOrEmpty(param.name))
                    {
                        usedNames.Add(param.name);
                    }
                }
            }

            if (!usedNames.Contains(layer.parameterName)) return;

            string baseName = layer.parameterName;
            int suffix = 1;
            string candidate;
            do
            {
                candidate = $"{baseName}_{suffix++}";
            } while (usedNames.Contains(candidate));

            layer.parameterName = candidate;
        }

        private void ApplyParametersAndMenu(
            ToggleConfig.LayerConfig layer,
            VRCExpressionParameters expressionParameters,
            VRCExpressionsMenu expressionsMenu,
            UnityEngine.Object assetContainer)
        {
            if (layer == null || expressionParameters == null) return;

            var valueType = VRCExpressionParameters.ValueType.Bool;
            float defaultValue = 0f;

            switch (layer.layerType)
            {
                case 0:
                    defaultValue = layer.defaultStateSelection == 1 ? 1f : 0f;
                    break;
                case 1:
                    valueType = VRCExpressionParameters.ValueType.Int;
                    defaultValue = layer.defaultIntValue;
                    break;
                case 2:
                    valueType = VRCExpressionParameters.ValueType.Float;
                    defaultValue = layer.defaultFloatValue;
                    break;
            }

            try
            {
                // 使用 VRCAssetsService 确保 NDMF 克隆参数表中有对应参数（方法定义于 VRCAssetsService.cs）
                _vrcAssetsService.AddParameter(
                    expressionParameters,
                    layer.parameterName,
                    valueType,
                    defaultValue,
                    layer.savedParameter,
                    layer.syncedParameter);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"NDMFApplyWorkflow: failed to add parameter '{layer.parameterName}': {ex.Message}");
            }

            if (!layer.createMenuControl || expressionsMenu == null) return;

            var targetMenu = ResolveMenuTarget(expressionsMenu, layer.menuPath);
            if (targetMenu == null)
            {
                Debug.LogWarning($"NDMFApplyWorkflow: menu path '{layer.menuPath}' not found; skipping menu control.");
                return;
            }

            try
            {
                switch (layer.layerType)
                {
                    case 0:
                        {
                            string controlName = !string.IsNullOrWhiteSpace(layer.boolMenuItemName)
                                ? layer.boolMenuItemName
                                : (!string.IsNullOrWhiteSpace(layer.layerName) ? layer.layerName : layer.parameterName);
                            if (!string.IsNullOrWhiteSpace(controlName))
                            {
                                // NDMF 下 Bool 菜单及其“下一页”子菜单仅作为克隆菜单的子资产存在
                                _vrcAssetsService.AddBoolMenuControl(targetMenu, controlName, layer.parameterName, null);
                            }
                        }
                        break;
                    case 1:
                        {
                            string controlName = !string.IsNullOrWhiteSpace(layer.intSubMenuName)
                                ? layer.intSubMenuName
                                : (!string.IsNullOrWhiteSpace(layer.layerName) ? layer.layerName : layer.parameterName);
                            var stateNames = BuildIntStateNames(layer);
                            if (!string.IsNullOrWhiteSpace(controlName))
                            {
                                // NDMF 下 Int 子菜单及其“下一页”分页菜单同样仅作为克隆菜单的子资产存在
                                _vrcAssetsService.AddIntMenuControl(targetMenu, controlName, layer.parameterName, stateNames, null);
                            }
                        }
                        break;
                    case 2:
                        {
                            string controlName = !string.IsNullOrWhiteSpace(layer.floatMenuItemName)
                                ? layer.floatMenuItemName
                                : (!string.IsNullOrWhiteSpace(layer.layerName) ? layer.layerName : layer.parameterName);
                            if (!string.IsNullOrWhiteSpace(controlName))
                            {
                                // NDMF 下 Float 菜单及其分页“下一页”子菜单也只挂在构建期菜单资产中，不写入磁盘
                                _vrcAssetsService.AddFloatMenuControl(targetMenu, controlName, layer.parameterName, null);
                            }
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"NDMFApplyWorkflow: failed to add menu control for '{layer.layerName}': {ex.Message}");
            }
        }

        private VRCExpressionsMenu ResolveMenuTarget(VRCExpressionsMenu rootMenu, string menuPath)
        {
            if (rootMenu == null) return null;
            if (string.IsNullOrEmpty(menuPath)) return rootMenu;

            // 优先使用作者侧路径映射 + 克隆映射，将作者侧菜单路径定位到克隆菜单
            if (_authoringMenuPathMap != null && _menuCloneMap != null)
            {
                if (_authoringMenuPathMap.TryGetValue(menuPath, out var authoringMenu) && authoringMenu != null)
                {
                    if (_menuCloneMap.TryGetValue(authoringMenu, out var clonedMenu) && clonedMenu != null)
                    {
                        return clonedMenu;
                    }
                }
            }

            // 兜底：若映射缺失或路径未命中，仍尝试在当前克隆菜单树上使用菜单映射查找（GetMenuMap 定义于 ToolboxUtils.cs）
            var map = ToolboxUtils.GetMenuMap(rootMenu);
            if (map != null && map.TryGetValue(menuPath, out var target) && target != null)
            {
                return target;
            }

            // 最终找不到时退回根菜单，避免空引用
            return rootMenu;
        }

        private List<string> BuildIntStateNames(ToggleConfig.LayerConfig layer)
        {
            var result = new List<string>();
            int groupCount = layer.intGroups?.Count ?? 0;
            for (int i = 0; i < groupCount; i++)
            {
                string label = (layer.intMenuItemNames != null && i < layer.intMenuItemNames.Count)
                    ? layer.intMenuItemNames[i]
                    : null;
                if (string.IsNullOrWhiteSpace(label))
                {
                    string baseName = !string.IsNullOrWhiteSpace(layer.layerName) ? layer.layerName : layer.parameterName;
                    label = $"{baseName}_{i}";
                }
                result.Add(label);
            }

            if (result.Count == 0)
            {
                string baseName = !string.IsNullOrWhiteSpace(layer.layerName) ? layer.layerName : layer.parameterName;
                result.Add($"{baseName}_0");
            }

            return result;
        }


        private AnimatorController GetOrCreateFXController(BuildContext context, VRCAvatarDescriptor avatarDescriptor)
        {
            if (context == null || avatarDescriptor == null) return null;

            var layers = avatarDescriptor.baseAnimationLayers ?? System.Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            int fxIndex = -1;
            AnimatorController existingController = null;

            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].type != VRCAvatarDescriptor.AnimLayerType.FX) continue;
                fxIndex = i;
                existingController = layers[i].animatorController as AnimatorController;
                if (existingController != null) break;
            }

            if (existingController != null)
            {
                return existingController;
            }

            var controller = new AnimatorController
            {
                name = avatarDescriptor.gameObject.name + "_FX_AQT"
            };
            if (context.AssetContainer != null)
            {
                AssetDatabase.AddObjectToAsset(controller, context.AssetContainer);
            }

            var newLayer = new VRCAvatarDescriptor.CustomAnimLayer
            {
                type = VRCAvatarDescriptor.AnimLayerType.FX,
                isEnabled = true,
                isDefault = false,
                animatorController = controller
            };

            if (fxIndex >= 0 && fxIndex < layers.Length)
            {
                layers[fxIndex] = newLayer;
            }
            else
            {
                var newLayers = new VRCAvatarDescriptor.CustomAnimLayer[layers.Length + 1];
                for (int i = 0; i < layers.Length; i++) newLayers[i] = layers[i];
                newLayers[layers.Length] = newLayer;
                layers = newLayers;
            }

            avatarDescriptor.baseAnimationLayers = layers;
            return controller;
        }

        private VRCExpressionParameters GetOrCreateExpressionParameters(BuildContext context, VRCAvatarDescriptor avatarDescriptor)
        {
            if (context == null || avatarDescriptor == null) return null;

            var existing = avatarDescriptor.expressionParameters;
            var newParameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            newParameters.name = avatarDescriptor.gameObject.name + "_Parameters_AQT";

            if (existing != null && existing.parameters != null)
            {
                // 复制一份参数数组，避免直接引用原数组（使用 Clone 创建浅拷贝）
                newParameters.parameters = (VRCExpressionParameters.Parameter[])existing.parameters.Clone();
            }

            if (context.AssetContainer != null)
            {
                AssetDatabase.AddObjectToAsset(newParameters, context.AssetContainer);
            }

            avatarDescriptor.expressionParameters = newParameters;
            return newParameters;
        }

        private VRCExpressionsMenu GetOrCreateExpressionsMenu(BuildContext context, VRCAvatarDescriptor avatarDescriptor)
        {
            if (context == null || avatarDescriptor == null) return null;

            var existing = avatarDescriptor.expressionsMenu;
            VRCExpressionsMenu newMenu;

            if (existing == null)
            {
                newMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                newMenu.name = avatarDescriptor.gameObject.name + "_Menu_AQT";

                // 作者侧没有菜单时，不构建路径映射与克隆映射
                _authoringMenuPathMap = null;
                _menuCloneMap = null;
            }
            else
            {
                // 为避免在 NDMF 中直接修改原始菜单资源，这里克隆一份菜单树
                // 记录作者侧菜单路径映射：key 与 QuickToggleWindow 中下拉使用的路径字符串一致
                _authoringMenuPathMap = ToolboxUtils.GetMenuMap(existing) 
                                         ?? new System.Collections.Generic.Dictionary<string, VRCExpressionsMenu>();

                // 构建作者侧菜单实例 -> 克隆菜单实例的映射，便于根据路径找到克隆菜单
                _menuCloneMap = new System.Collections.Generic.Dictionary<VRCExpressionsMenu, VRCExpressionsMenu>();
                newMenu = CloneExpressionsMenu(existing, context.AssetContainer, _menuCloneMap);
            }

            // 仅在“新建”菜单（existing == null）时在此处添加到 AssetContainer；克隆菜单由 CloneExpressionsMenu 负责注册
            if (existing == null && context.AssetContainer != null && !AssetDatabase.IsSubAsset(newMenu))
            {
                AssetDatabase.AddObjectToAsset(newMenu, context.AssetContainer);
            }

            avatarDescriptor.expressionsMenu = newMenu;
            return newMenu;
        }

        private VRCExpressionsMenu CloneExpressionsMenu(VRCExpressionsMenu source, UnityEngine.Object assetContainer, System.Collections.Generic.Dictionary<VRCExpressionsMenu, VRCExpressionsMenu> map)
        {
            if (source == null) return null;
            if (map.TryGetValue(source, out var cached)) return cached;

            var clone = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            clone.name = source.name + "_AQT";

            if (assetContainer != null)
            {
                AssetDatabase.AddObjectToAsset(clone, assetContainer);
            }

            map[source] = clone;

            if (source.controls != null)
            {
                foreach (var ctrl in source.controls)
                {
                    if (ctrl == null) continue;
                    var newCtrl = new VRCExpressionsMenu.Control
                    {
                        name = ctrl.name,
                        icon = ctrl.icon,
                        type = ctrl.type,
                        parameter = ctrl.parameter,
                        value = ctrl.value,
                        style = ctrl.style
                    };

                    if (ctrl.subParameters != null && ctrl.subParameters.Length > 0)
                    {
                        var subParams = new VRCExpressionsMenu.Control.Parameter[ctrl.subParameters.Length];
                        for (int i = 0; i < ctrl.subParameters.Length; i++)
                        {
                            subParams[i] = new VRCExpressionsMenu.Control.Parameter
                            {
                                name = ctrl.subParameters[i].name
                            };
                        }
                        newCtrl.subParameters = subParams;
                    }

                    if (ctrl.type == VRCExpressionsMenu.Control.ControlType.SubMenu && ctrl.subMenu != null)
                    {
                        newCtrl.subMenu = CloneExpressionsMenu(ctrl.subMenu, assetContainer, map);
                    }

                    clone.controls.Add(newCtrl);
                }
            }

            return clone;
        }
    }
}
