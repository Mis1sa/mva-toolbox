using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.QuickAnimatorEdit.Services.Shared
{
    internal static class ParameterTypeConversionUtility
    {
        public static bool IsSupportedType(AnimatorControllerParameterType type)
        {
            return type == AnimatorControllerParameterType.Bool ||
                   type == AnimatorControllerParameterType.Float ||
                   type == AnimatorControllerParameterType.Int;
        }

        public static AnimatorControllerParameter ApplyTypeConversion(
            AnimatorControllerParameter parameter,
            AnimatorControllerParameterType targetType)
        {
            if (parameter == null)
            {
                return null;
            }

            var sourceType = parameter.type;
            if (sourceType == targetType ||
                !IsSupportedType(sourceType) ||
                !IsSupportedType(targetType))
            {
                return parameter;
            }

            bool boolValue = ResolveBoolValue(parameter);

            switch (targetType)
            {
                case AnimatorControllerParameterType.Bool:
                    parameter.defaultBool = boolValue;
                    parameter.defaultFloat = boolValue ? 1f : 0f;
                    parameter.defaultInt = boolValue ? 1 : 0;
                    break;
                case AnimatorControllerParameterType.Int:
                    parameter.defaultInt = boolValue ? 1 : 0;
                    parameter.defaultFloat = parameter.defaultInt;
                    parameter.defaultBool = boolValue;
                    break;
                case AnimatorControllerParameterType.Float:
                    parameter.defaultFloat = boolValue ? 1f : 0f;
                    parameter.defaultInt = boolValue ? 1 : 0;
                    parameter.defaultBool = boolValue;
                    break;
            }

            parameter.type = targetType;
            return parameter;
        }

        public static AnimatorCondition ConvertCondition(
            AnimatorCondition condition,
            AnimatorControllerParameterType fromType,
            AnimatorControllerParameterType toType)
        {
            if (fromType == toType ||
                !IsSupportedType(fromType) ||
                !IsSupportedType(toType))
            {
                return condition;
            }

            bool expectsTrue = DetermineExpectedTrue(condition, fromType);
            var converted = condition;

            switch (toType)
            {
                case AnimatorControllerParameterType.Bool:
                    converted.mode = expectsTrue ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot;
                    converted.threshold = 0f;
                    break;
                case AnimatorControllerParameterType.Int:
                    converted.mode = AnimatorConditionMode.Equals;
                    converted.threshold = expectsTrue ? 1f : 0f;
                    break;
                case AnimatorControllerParameterType.Float:
                    converted.mode = expectsTrue ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less;
                    converted.threshold = 0.5f;
                    break;
            }

            return converted;
        }

        private static bool DetermineExpectedTrue(AnimatorCondition condition, AnimatorControllerParameterType sourceType)
        {
            switch (sourceType)
            {
                case AnimatorControllerParameterType.Bool:
                    return condition.mode != AnimatorConditionMode.IfNot;

                case AnimatorControllerParameterType.Int:
                    switch (condition.mode)
                    {
                        case AnimatorConditionMode.Equals:
                            return condition.threshold >= 0.5f;
                        case AnimatorConditionMode.NotEqual:
                            return condition.threshold < 0.5f;
                        case AnimatorConditionMode.Greater:
                            return true;
                        case AnimatorConditionMode.Less:
                            return condition.threshold > 1f;
                        default:
                            return true;
                    }

                case AnimatorControllerParameterType.Float:
                    switch (condition.mode)
                    {
                        case AnimatorConditionMode.Greater:
                            return true;
                        case AnimatorConditionMode.Less:
                            return false;
                        case AnimatorConditionMode.Equals:
                            return condition.threshold >= 0.5f;
                        case AnimatorConditionMode.NotEqual:
                            return condition.threshold < 0.5f;
                        default:
                            return true;
                    }
            }

            return true;
        }

        private static bool ResolveBoolValue(AnimatorControllerParameter parameter)
        {
            return parameter.type switch
            {
                AnimatorControllerParameterType.Bool => parameter.defaultBool,
                AnimatorControllerParameterType.Float => parameter.defaultFloat >= 0.5f,
                AnimatorControllerParameterType.Int => parameter.defaultInt != 0,
                _ => false
            };
        }
    }
}
