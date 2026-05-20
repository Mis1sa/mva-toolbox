using System.Collections.Generic;
using MVA.Toolbox.SwitchGenerator.Utils;

namespace MVA.Toolbox.SwitchGenerator.Spec
{
    internal static class SwitchGeneratorSpecValidator
    {
        public static List<string> ValidateAll(SwitchGeneratorSpec spec)
        {
            var errors = new List<string>();
            if (spec == null)
            {
                errors.Add("配置为空。");
                return errors;
            }

            if (spec.avatar == null)
            {
                errors.Add("未指定 Avatar。");
            }

            if (spec.layers == null || spec.layers.Count == 0)
            {
                errors.Add("至少需要一个开关层。");
                return errors;
            }

            var layerNames = new HashSet<string>();
            for (int i = 0; i < spec.layers.Count; i++)
            {
                var layer = spec.layers[i];
                if (layer == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(layer.layerName))
                {
                    errors.Add($"第 {i + 1} 个层级未填写层级名称。");
                    continue;
                }

                if (!layerNames.Add(layer.layerName))
                {
                    errors.Add("层级名称重复：" + layer.layerName);
                }

                if (string.IsNullOrWhiteSpace(layer.parameterName))
                {
                    errors.Add($"层级 {layer.layerName} 未填写参数名。");
                }

                switch (layer.switchType)
                {
                    case SwitchGeneratorConfig.SwitchType.Bool:
                        if (HasInvalidTargets(layer.boolTargets, spec.avatar))
                        {
                            errors.Add($"层级 {layer.layerName} 包含非法目标对象。");
                        }

                        if (!HasValidTargets(layer.boolTargets, spec.avatar))
                        {
                            errors.Add($"层级 {layer.layerName} 的 Bool 目标无效。");
                        }
                        break;
                    case SwitchGeneratorConfig.SwitchType.Int:
                        if (layer.intGroups == null || layer.intGroups.Count < 2)
                        {
                            errors.Add($"层级 {layer.layerName} 的 Int 分组至少 2 个。");
                            break;
                        }

                        bool hasAny = false;
                        bool hasInvalidIntTargets = false;
                        for (int g = 0; g < layer.intGroups.Count; g++)
                        {
                            if (HasInvalidTargets(layer.intGroups[g]?.targets, spec.avatar))
                            {
                                errors.Add($"层级 {layer.layerName} 的 Int 分组包含非法目标对象。");
                                hasInvalidIntTargets = true;
                                break;
                            }

                            if (HasValidTargets(layer.intGroups[g]?.targets, spec.avatar))
                            {
                                hasAny = true;
                            }
                        }

                        if (!hasAny && !hasInvalidIntTargets)
                        {
                            errors.Add($"层级 {layer.layerName} 的 Int 分组没有有效目标。");
                        }

                        if (layer.defaultIntValue < 0 || layer.defaultIntValue >= layer.intGroups.Count)
                        {
                            errors.Add($"层级 {layer.layerName} 的 Int 默认值越界。");
                        }
                        break;
                    case SwitchGeneratorConfig.SwitchType.Float:
                        if (HasInvalidTargets(layer.floatTargets, spec.avatar))
                        {
                            errors.Add($"层级 {layer.layerName} 包含非法目标对象。");
                        }

                        if (!HasValidTargets(layer.floatTargets, spec.avatar))
                        {
                            errors.Add($"层级 {layer.layerName} 的 Float 目标无效。");
                        }

                        if (layer.defaultFloatValue < 0f || layer.defaultFloatValue > 1f)
                        {
                            errors.Add($"层级 {layer.layerName} 的 Float 默认值超出范围。");
                        }
                        break;
                }
            }

            return errors;
        }

        public static bool Validate(SwitchGeneratorSpec spec, out string error)
        {
            var errors = ValidateAll(spec);
            if (errors.Count > 0)
            {
                error = errors[0];
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool HasValidTargets(List<SwitchTargetSpec> targets, VRC.SDK3.Avatars.Components.VRCAvatarDescriptor avatar)
        {
            if (targets == null || targets.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (target != null && TargetObjectResolver.IsValidTargetObject(target.targetObject, avatar))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasInvalidTargets(List<SwitchTargetSpec> targets, VRC.SDK3.Avatars.Components.VRCAvatarDescriptor avatar)
        {
            if (targets == null || targets.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (target?.targetObject != null && !TargetObjectResolver.IsValidTargetObject(target.targetObject, avatar))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
