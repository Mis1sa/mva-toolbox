using System;
using System.Collections.Generic;
using UnityEditor;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace MVA.Toolbox.SwitchGenerator.Emit.Vrc
{
    internal static class VrcParameterEmitter
    {
        public static void Upsert(
            VRCExpressionParameters parameters,
            string name,
            VRCExpressionParameters.ValueType type,
            float defaultValue,
            bool saved,
            bool synced)
        {
            if (parameters == null || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var list = parameters.parameters != null
                ? new List<VRCExpressionParameters.Parameter>(parameters.parameters)
                : new List<VRCExpressionParameters.Parameter>();

            int index = list.FindIndex(p => p != null && p.name == name);
            int used = GetUsedMemory(list);

            var item = new VRCExpressionParameters.Parameter
            {
                name = name,
                valueType = type,
                defaultValue = defaultValue,
                saved = saved,
                networkSynced = synced
            };

            if (index >= 0)
            {
                used -= GetCost(list[index].valueType);
                if (used + GetCost(type) > VRCExpressionParameters.MAX_PARAMETER_COST)
                {
                    throw new InvalidOperationException("参数内存不足，无法覆盖参数：" + name);
                }

                list[index] = item;
            }
            else
            {
                if (used + GetCost(type) > VRCExpressionParameters.MAX_PARAMETER_COST)
                {
                    throw new InvalidOperationException("参数内存不足，无法新增参数：" + name);
                }

                list.Add(item);
            }

            parameters.parameters = list.ToArray();
            EditorUtility.SetDirty(parameters);
        }

        private static int GetUsedMemory(List<VRCExpressionParameters.Parameter> list)
        {
            int result = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (p == null)
                {
                    continue;
                }

                result += GetCost(p.valueType);
            }

            return result;
        }

        private static int GetCost(VRCExpressionParameters.ValueType type)
        {
            switch (type)
            {
                case VRCExpressionParameters.ValueType.Bool:
                    return 1;
                case VRCExpressionParameters.ValueType.Int:
                case VRCExpressionParameters.ValueType.Float:
                    return 8;
                default:
                    return 0;
            }
        }
    }
}
