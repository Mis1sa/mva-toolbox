using System;
using System.Collections.Generic;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace MVA.Toolbox.SwitchGenerator.Compile
{
    internal static class ParameterNameAllocator
    {
        public static string Allocate(
            string preferred,
            bool overwrite,
            AnimatorController controller,
            VRCExpressionParameters expressionParameters)
        {
            if (string.IsNullOrWhiteSpace(preferred))
            {
                return preferred;
            }

            if (overwrite)
            {
                return preferred;
            }

            var used = new HashSet<string>(StringComparer.Ordinal);
            if (controller?.parameters != null)
            {
                for (int i = 0; i < controller.parameters.Length; i++)
                {
                    var p = controller.parameters[i];
                    if (p != null && !string.IsNullOrEmpty(p.name))
                    {
                        used.Add(p.name);
                    }
                }
            }

            if (expressionParameters?.parameters != null)
            {
                for (int i = 0; i < expressionParameters.parameters.Length; i++)
                {
                    var p = expressionParameters.parameters[i];
                    if (p != null && !string.IsNullOrEmpty(p.name))
                    {
                        used.Add(p.name);
                    }
                }
            }

            if (!used.Contains(preferred))
            {
                return preferred;
            }

            int suffix = 1;
            string candidate;
            do
            {
                candidate = preferred + "_" + suffix;
                suffix++;
            } while (used.Contains(candidate));

            return candidate;
        }
    }
}
