using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace MVA.Toolbox.AvatarQuickToggle
{
    [Serializable]
    public class ToggleConfig
    {
        public VRCAvatarDescriptor avatar;
        public LayerConfig config = new LayerConfig();

        [Serializable]
        public class LayerConfig
        {
            public string layerName;
            // 开关类型：0=Bool,1=Int,2=Float
            public int layerType;
            public string parameterName;
            public bool overwriteLayer;
            public bool overwriteParameter;
            public string clipSavePath;
            // Write Defaults 模式：0=Auto,1=On,2=Off
            public int writeDefaultSetting;
            public bool createMenuControl;
            public string menuControlName;
            public string boolMenuItemName;
            public string floatMenuItemName;
            public string intSubMenuName;
            public List<string> intMenuItemNames = new List<string>();
            public string menuPath;
            public bool savedParameter;
            public bool syncedParameter;
            public int defaultStateSelection;
            public int defaultIntValue;
            public float defaultFloatValue;
            public bool editInWDOnMode;
            public List<TargetItem> boolTargets = new List<TargetItem>();
            public List<IntStateGroup> intGroups = new List<IntStateGroup>();
            public List<TargetItem> floatTargets = new List<TargetItem>();
        }

        [Serializable]
        public class TargetItem
        {
            public GameObject targetObject;
            // 目标控制类型：0=GameObject,1=BlendShape
            public int controlType;
            public string blendShapeName;
            // Bool ON 时的 BlendShape 值：0=100,1=0
            public int onStateBlendShapeValue;
            // Bool ON 时的 GameObject 状态：0=Active,1=Inactive
            public int onStateActiveSelection;
            public bool splitBlendShape;
            public string secondaryBlendShapeName;
            // 第二个 BlendShape 的 ON 值：0=100,1=0
            public int secondaryBlendShapeValue;
        }

        [Serializable]
        public class IntStateGroup
        {
            public string stateName;
            public List<TargetItem> targetItems = new List<TargetItem>();
            public bool isFoldout;
        }
    }

    internal static class ToggleConfigValidator
    {
        public static bool Validate(ToggleConfig.LayerConfig config, out string error)
        {
            error = null;
            if (config == null)
            {
                error = "configuration is null";
                return false;
            }
            if (string.IsNullOrWhiteSpace(config.layerName))
            {
                error = "layer name is required";
                return false;
            }
            if (string.IsNullOrWhiteSpace(config.parameterName))
            {
                error = "parameter name is required";
                return false;
            }
            bool isRootAssets = string.Equals(config.clipSavePath, "Assets", StringComparison.Ordinal);
            if (string.IsNullOrEmpty(config.clipSavePath) || (!isRootAssets && !config.clipSavePath.StartsWith("Assets/")))
            {
                error = "clip save path must start with Assets/";
                return false;
            }

            switch (config.layerType)
            {
                case 0:
                    if (config.boolTargets == null || config.boolTargets.Count == 0)
                    {
                        error = "Bool layer requires at least one target";
                        return false;
                    }
                    // Bool 层至少需要一个 targetObject 非空的目标
                    bool hasValidBoolTarget = false;
                    for (int i = 0; i < config.boolTargets.Count; i++)
                    {
                        var t = config.boolTargets[i];
                        if (t != null && t.targetObject != null)
                        {
                            hasValidBoolTarget = true;
                            break;
                        }
                    }
                    if (!hasValidBoolTarget)
                    {
                        error = "Bool layer requires at least one target with a non-null targetObject";
                        return false;
                    }
                    if (config.defaultStateSelection < 0 || config.defaultStateSelection > 1)
                    {
                        error = "Bool default value must be 0 or 1";
                        return false;
                    }
                    break;
                case 1:
                    if (config.intGroups == null || config.intGroups.Count < 2)
                    {
                        error = "Int layer requires at least two groups";
                        return false;
                    }
                    bool anyGroupHasTargets = false;
                    foreach (var group in config.intGroups)
                    {
                        if (group?.targetItems == null) continue;
                        foreach (var item in group.targetItems)
                        {
                            if (item != null && item.targetObject != null)
                            {
                                anyGroupHasTargets = true;
                                break;
                            }
                        }
                        if (anyGroupHasTargets) break;
                    }
                    if (!anyGroupHasTargets)
                    {
                        error = "Int layer requires at least one group containing targets";
                        return false;
                    }
                    if (config.defaultIntValue < 0 || config.defaultIntValue >= config.intGroups.Count)
                    {
                        error = "Int default value out of range";
                        return false;
                    }
                    break;
                case 2:
                    if (config.floatTargets == null || config.floatTargets.Count == 0)
                    {
                        error = "Float layer requires at least one target";
                        return false;
                    }
                    // Float 层同样至少需要一个 targetObject 非空的目标
                    bool hasValidFloatTarget = false;
                    for (int i = 0; i < config.floatTargets.Count; i++)
                    {
                        var t = config.floatTargets[i];
                        if (t != null && t.targetObject != null)
                        {
                            hasValidFloatTarget = true;
                            break;
                        }
                    }
                    if (!hasValidFloatTarget)
                    {
                        error = "Float layer requires at least one target with a non-null targetObject";
                        return false;
                    }
                    if (config.defaultFloatValue < 0f || config.defaultFloatValue > 1f)
                    {
                        error = "Float default value must be between 0 and 1";
                        return false;
                    }
                    break;
                default:
                    error = "unknown layer type";
                    return false;
            }

            return true;
        }
    }
}
