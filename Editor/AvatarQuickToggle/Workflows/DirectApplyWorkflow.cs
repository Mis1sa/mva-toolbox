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
                // 在创建层和应用菜单之前，根据当前资源对参数名进行一次统一解析：
                // 1. 若未勾选覆盖参数且 Animator 或 VRCParameter 中已存在同名参数，则自动生成唯一的新参数名 Param_1/Param_2/...，
                //    并写回到 _config.config.parameterName，这样 Animator 与 VRC 两侧都会使用同一个新名字，避免含义不一致；
                // 2. 若勾选了覆盖参数，则保留当前参数名，用于后续在 Animator 与 VRC 中进行覆盖。
                ResolveParameterName(_config.config);

                _animationService.CreateLayer(_fxController, _config, null);
                ApplyParametersAndMenu(_config.config);

                EditorUtility.SetDirty(_fxController);
                EditorUtility.SetDirty(_expressionParameters);
                EditorUtility.SetDirty(_expressionsMenu);
                AssetDatabase.SaveAssets();

                // Direct Apply 完成后，可通知编辑窗口刷新可用层级列表（例如覆盖层级下拉）。
                // 这里预留钩子，实际刷新逻辑由 QuickToggleWindow 实现（若需要可添加静态方法来调用）。
                MVA.Toolbox.AvatarQuickToggle.Editor.QuickToggleWindow.RefreshCachedAvatarDataIfOpen();

                Debug.Log($"DirectApplyWorkflow: layer '{_config.config.layerName}' generated successfully.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"DirectApplyWorkflow: {ex.Message}");
            }
        }

        /// <summary>
        /// 解析并规范当前层使用的参数名。
        /// 未勾选覆盖参数时，只要 Animator 或 VRCExpressionParameters 任意一侧存在同名参数，
        /// 则自动将当前参数名改为 Param_1/Param_2/...，保证新建参数名在两侧都不与既有参数冲突；
        /// 勾选覆盖参数时则保留原名，用于覆盖既有参数。
        /// </summary>
        private void ResolveParameterName(ToggleConfig.LayerConfig layer)
        {
            if (layer == null) return;
            if (string.IsNullOrWhiteSpace(layer.parameterName)) return;

            // 覆盖模式下保留原参数名，用于后续覆盖行为
            if (layer.overwriteParameter)
            {
                return;
            }

            // 收集 Animator 与 VRCExpressionParameters 中现有的所有参数名
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
                // 当前参数名在两侧都不存在，直接使用
                return;
            }

            // 生成一个在 Animator 与 VRCParameter 两侧都未被占用的新参数名：Param_1/Param_2/...
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
                    // Bool: 0 = OFF(0f), 1 = ON(1f)
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

            // 无论是否勾选覆盖参数，都需要为当前层使用的参数名在 VRCExpressionParameters 中创建/确保一条记录：
            // 1. 未勾选覆盖参数时，参数名已经在 ResolveParameterName 中避开了冲突，此处调用 AddParameter 只会添加新参数；
            // 2. 勾选覆盖参数时，AddParameter 会覆盖同名参数的类型和默认值，与 Animator 侧保持一致。
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
                        // Bool 菜单项名称：优先使用 boolMenuItemName，未填写时使用最终层级名（若也为空则退回参数名）
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
                            // 直接工作流下，Bool 菜单的“下一页”子菜单 .asset 路径与本层动画剪辑同目录
                            string menuFolder = ToolboxUtils.BuildAqtLayerFolder(layer.clipSavePath, layer.layerName);
                            _vrcAssetsService.AddBoolMenuControl(menuTarget, controlName, layer.parameterName, menuFolder);
                        }
                    }
                    break;
                case 1:
                    {
                        // Int 子菜单名称：优先使用 intSubMenuName，未填写时使用最终层级名（若也为空则退回参数名）
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
                                // 默认为 [最终层级名]_[参数值]，参数值此处以索引 i 表示
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
                            _vrcAssetsService.AddIntMenuControl(menuTarget, controlName, layer.parameterName, stateNames, submenuFolder);
                        }
                    }
                    break;
                case 2:
                    {
                        // Float 菜单项名称：优先使用 floatMenuItemName，未填写时使用最终层级名（若也为空则退回参数名）
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
                            // Float 菜单的“下一页”子菜单 .asset 路径与本层动画剪辑同目录
                            string menuFolder = ToolboxUtils.BuildAqtLayerFolder(layer.clipSavePath, layer.layerName);
                            _vrcAssetsService.AddFloatMenuControl(menuTarget, controlName, layer.parameterName, menuFolder);
                        }
                    }
                    break;
            }
        }

        private VRCExpressionsMenu ResolveMenu(string menuPath)
        {
            if (_expressionsMenu == null) return null;

            // 与 SSG 思路保持一致：优先使用菜单映射中的 key 直接查找，不主动创建额外子菜单
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
