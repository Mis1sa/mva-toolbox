using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using MVA.Toolbox.AvatarQuickToggle.Services;
using MVA.Toolbox.Public;
using MVA.Toolbox.AvatarQuickToggle;

namespace MVA.Toolbox.AvatarQuickToggle.Workflows
{
    public class DirectApplyWorkflow
    {
        private readonly MVA.Toolbox.AvatarQuickToggle.ToggleConfig _config;
        private readonly AnimationService _animationService = new AnimationService();
        private readonly VRCAssetsService _vrcAssetsService = new VRCAssetsService();

        private AnimatorController _fxController;
        private VRCExpressionParameters _expressionParameters;
        private VRCExpressionsMenu _expressionsMenu;

        public DirectApplyWorkflow(MVA.Toolbox.AvatarQuickToggle.ToggleConfig config)
        {
            _config = config;
        }

        public void Execute()
        {
            if (_config == null || _config.avatar == null)
            {
                Debug.LogError("DirectApplyWorkflow: invalid config or avatar.");
                return;
            }
            if (!ToggleConfigValidator.Validate(_config.config, out string error))
            {
                Debug.LogError($"DirectApplyWorkflow: {error}");
                return;
            }

            PrepareResources(_config.avatar);
            if (_fxController == null || _expressionParameters == null || _expressionsMenu == null)
            {
                Debug.LogError("DirectApplyWorkflow: failed to prepare avatar resources.");
                return;
            }

            try
            {
                // 在创建层和菜单前，先统一处理本层的参数名，避免与现有参数冲突
                ResolveParameterName(_config.config);

                // FX 层创建由 AnimationService 负责（定义于 AnimationService.cs）
                _animationService.CreateLayer(_fxController, _config, null);
                // 参数与菜单内容由当前工作流负责写入
                ApplyParametersAndMenu(_config.config);

                EditorUtility.SetDirty(_fxController);
                EditorUtility.SetDirty(_expressionParameters);
                EditorUtility.SetDirty(_expressionsMenu);
                AssetDatabase.SaveAssets();

                // Direct Apply 完成后，尝试通知 QuickToggleWindow 刷新缓存的 Avatar 数据（方法定义于 QuickToggleWindow.cs）
                MVA.Toolbox.AvatarQuickToggle.Editor.QuickToggleWindow.RefreshCachedAvatarDataIfOpen();

                Debug.Log($"DirectApplyWorkflow: layer '{_config.config.layerName}' generated successfully.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"DirectApplyWorkflow: {ex.Message}");
            }
        }

        private void ResolveParameterName(ToggleConfig.LayerConfig layer)
        {
            if (layer == null) return;
            if (string.IsNullOrWhiteSpace(layer.parameterName)) return;

            // 勾选覆盖参数时保留原参数名，用于覆盖已有参数
            if (layer.overwriteParameter)
            {
                return;
            }

            // 收集 AnimatorController 与 VRCExpressionParameters 中已有的全部参数名
            var existingNames = new HashSet<string>(StringComparer.Ordinal);
            if (_fxController != null)
            {
                var parameters = _fxController.parameters ?? Array.Empty<AnimatorControllerParameter>();
                for (int i = 0; i < parameters.Length; i++)
                {
                    var p = parameters[i];
                    if (p != null && !string.IsNullOrEmpty(p.name))
                    {
                        existingNames.Add(p.name);
                    }
                }
            }

            if (_expressionParameters != null && _expressionParameters.parameters != null)
            {
                var vrcParams = _expressionParameters.parameters;
                for (int i = 0; i < vrcParams.Length; i++)
                {
                    var p = vrcParams[i];
                    if (p != null && !string.IsNullOrEmpty(p.name))
                    {
                        existingNames.Add(p.name);
                    }
                }
            }

            string baseName = layer.parameterName;
            if (!existingNames.Contains(baseName))
            {
                // 当前参数名在 Animator 与 VRC 中都不存在，可直接沿用
                return;
            }

            // 生成一个在 Animator 与 VRC 参数表中都未被占用的新参数名：Param_1/Param_2/...
            int suffix = 1;
            string candidate;
            do
            {
                candidate = $"{baseName}_{suffix}";
                suffix++;
            } while (existingNames.Contains(candidate));

            layer.parameterName = candidate;
        }

        private void PrepareResources(VRCAvatarDescriptor avatar)
        {
            // FX 控制器、参数资产与菜单资产由 ToolboxUtils 提供获取/创建工具方法（定义于 ToolboxUtils.cs）
            _fxController = ToolboxUtils.GetExistingFXController(avatar) ?? ToolboxUtils.EnsureFXController(avatar);
            _expressionParameters = ToolboxUtils.GetExistingExpressionParameters(avatar) ?? ToolboxUtils.EnsureExpressionParameters(avatar);
            _expressionsMenu = ToolboxUtils.GetExistingExpressionsMenu(avatar) ?? ToolboxUtils.EnsureExpressionsMenu(avatar);
        }

        private void ApplyParametersAndMenu(ToggleConfig.LayerConfig layer)
        {
            if (layer == null) return;

            var type = VRCExpressionParameters.ValueType.Bool;
            float defaultValue = 0f;

            switch (layer.layerType)
            {
                case 0:
                    // Bool 层：默认值 0 表示 OFF，1 表示 ON
                    defaultValue = layer.defaultStateSelection == 1 ? 1f : 0f;
                    break;
                case 1:
                    type = VRCExpressionParameters.ValueType.Int;
                    defaultValue = layer.defaultIntValue;
                    break;
                case 2:
                    type = VRCExpressionParameters.ValueType.Float;
                    defaultValue = layer.defaultFloatValue;
                    break;
            }

            // 使用 VRCAssetsService 确保 VRCExpressionParameters 中存在对应参数（方法定义于 VRCAssetsService.cs）
            _vrcAssetsService.AddParameter(_expressionParameters, layer.parameterName, type, defaultValue, layer.savedParameter, layer.syncedParameter);

            if (!layer.createMenuControl) return;

            var menuTarget = ResolveMenu(layer.menuPath);
            if (menuTarget == null)
            {
                Debug.LogWarning($"DirectApplyWorkflow: 未能解析菜单路径 '{layer.menuPath}'，跳过菜单控件创建。");
                return;
            }

            switch (layer.layerType)
            {
                case 0:
                    {
                        // Bool 菜单项名称：优先使用 boolMenuItemName，未填写时使用层级名，层级名为空则退回参数名
                        string controlName = layer.boolMenuItemName;
                        if (string.IsNullOrWhiteSpace(controlName))
                        {
                            if (!string.IsNullOrWhiteSpace(layer.layerName))
                                controlName = layer.layerName;
                            else
                                controlName = layer.parameterName;
                        }
                        if (!string.IsNullOrWhiteSpace(controlName))
                        {
                            // 直接工作流下，“下一页”子菜单 .asset 与本层动画剪辑保存在同一目录
                            string menuFolder = ToolboxUtils.BuildAqtLayerFolder(layer.clipSavePath, layer.layerName);
                            // Bool 菜单控件与分页逻辑由 VRCAssetsService 负责创建（定义于 VRCAssetsService.cs）
                            _vrcAssetsService.AddBoolMenuControl(menuTarget, controlName, layer.parameterName, menuFolder);
                        }
                    }
                    break;
                case 1:
                    {
                        // Int 子菜单名称：优先使用 intSubMenuName，未填写时使用层级名，层级名为空则退回参数名
                        string controlName = layer.intSubMenuName;
                        if (string.IsNullOrWhiteSpace(controlName))
                        {
                            if (!string.IsNullOrWhiteSpace(layer.layerName))
                                controlName = layer.layerName;
                            else
                                controlName = layer.parameterName;
                        }

                        int groupCount = layer.intGroups?.Count ?? 0;
                        var stateNames = new List<string>(groupCount);
                        for (int i = 0; i < groupCount; i++)
                        {
                            string label = (layer.intMenuItemNames != null && i < layer.intMenuItemNames.Count)
                                ? layer.intMenuItemNames[i]
                                : null;
                            if (string.IsNullOrWhiteSpace(label))
                            {
                                // 默认标签格式为 [层级名或参数名]_[索引]，索引与 Int 值对应
                                string baseName = !string.IsNullOrWhiteSpace(layer.layerName) ? layer.layerName : layer.parameterName;
                                label = $"{baseName}_{i}";
                            }
                            stateNames.Add(label);
                        }

                        if (stateNames.Count == 0)
                        {
                            string baseName = !string.IsNullOrWhiteSpace(layer.layerName) ? layer.layerName : layer.parameterName;
                            stateNames.Add($"{baseName}_0");
                        }

                        if (!string.IsNullOrWhiteSpace(controlName))
                        {
                            // Int 子菜单及其“下一页”分页菜单 .asset 保存在本层动画剪辑目录下
                            string submenuFolder = ToolboxUtils.BuildAqtLayerFolder(layer.clipSavePath, layer.layerName);
                            // Int SubMenu 与分页逻辑由 VRCAssetsService 负责创建（定义于 VRCAssetsService.cs）
                            _vrcAssetsService.AddIntMenuControl(menuTarget, controlName, layer.parameterName, stateNames, submenuFolder);
                        }
                    }
                    break;
                case 2:
                    {
                        // Float 菜单项名称：优先使用 floatMenuItemName，未填写时使用层级名，层级名为空则退回参数名
                        string controlName = layer.floatMenuItemName;
                        if (string.IsNullOrWhiteSpace(controlName))
                        {
                            if (!string.IsNullOrWhiteSpace(layer.layerName))
                                controlName = layer.layerName;
                            else
                                controlName = layer.parameterName;
                        }
                        if (!string.IsNullOrWhiteSpace(controlName))
                        {
                            // Float 菜单及其“下一页”子菜单 .asset 与本层动画剪辑同目录
                            string menuFolder = ToolboxUtils.BuildAqtLayerFolder(layer.clipSavePath, layer.layerName);
                            // Float 菜单控件与分页逻辑由 VRCAssetsService 负责创建（定义于 VRCAssetsService.cs）
                            _vrcAssetsService.AddFloatMenuControl(menuTarget, controlName, layer.parameterName, menuFolder);
                        }
                    }
                    break;
            }
        }

        private VRCExpressionsMenu ResolveMenu(string menuPath)
        {
            if (_expressionsMenu == null) return null;

            // 通过 ToolboxUtils 构建的菜单映射查找目标菜单（GetMenuMap 定义于 ToolboxUtils.cs）
            var map = ToolboxUtils.GetMenuMap(_expressionsMenu);
            if (map == null || map.Count == 0)
            {
                return _expressionsMenu;
            }

            if (string.IsNullOrEmpty(menuPath))
            {
                return _expressionsMenu;
            }

            if (map.TryGetValue(menuPath, out var target) && target != null)
            {
                return target;
            }

            // 找不到对应 key 时退回根菜单，避免生成与总菜单同名的多余 SubMenu
            return _expressionsMenu;
        }
    }
}
