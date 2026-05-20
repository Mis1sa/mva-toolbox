using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace MVA.Toolbox.SwitchGenerator.Emit.Animator
{
    internal static class AnimatorParameterEmitter
    {
        public static void Upsert(AnimatorController controller, Compile.SwitchLayerPlan layer)
        {
            if (controller == null || layer == null || string.IsNullOrWhiteSpace(layer.parameterName))
            {
                return;
            }

            var type = ConvertType(layer.parameterType);
            var parameters = controller.parameters ?? Array.Empty<AnimatorControllerParameter>();
            int index = -1;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i] != null && parameters[i].name == layer.parameterName)
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
            {
                controller.RemoveParameter(index);
            }

            controller.AddParameter(new AnimatorControllerParameter
            {
                name = layer.parameterName,
                type = type,
                defaultBool = layer.defaultBoolValue == 1,
                defaultInt = layer.defaultIntValue,
                defaultFloat = layer.defaultFloatValue
            });

            EditorUtility.SetDirty(controller);
        }

        private static AnimatorControllerParameterType ConvertType(VRCExpressionParameters.ValueType type)
        {
            switch (type)
            {
                case VRCExpressionParameters.ValueType.Int:
                    return AnimatorControllerParameterType.Int;
                case VRCExpressionParameters.ValueType.Float:
                    return AnimatorControllerParameterType.Float;
                default:
                    return AnimatorControllerParameterType.Bool;
            }
        }
    }
}
