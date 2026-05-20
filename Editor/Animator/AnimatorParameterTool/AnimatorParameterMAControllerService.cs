using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.AnimatorParameterTool
{
    internal static class AnimatorParameterMAControllerService
    {
        internal readonly struct ControllerBuildResult
        {
            internal ControllerBuildResult(AnimatorController controller, Dictionary<string, (bool saved, bool synced)> defaults)
            {
                Controller = controller;
                Defaults = defaults;
            }

            internal AnimatorController Controller { get; }

            internal Dictionary<string, (bool saved, bool synced)> Defaults { get; }
        }

        internal static List<ControllerBuildResult> BuildControllers(GameObject root)
        {
            List<ControllerBuildResult> results = new List<ControllerBuildResult>();
            if (root == null)
            {
                return results;
            }

            Type maParamsType = null;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length && maParamsType == null; i++)
            {
                Assembly asm = assemblies[i];
                if (asm == null)
                {
                    continue;
                }

                maParamsType = asm.GetType("nadena.dev.modular_avatar.core.ModularAvatarParameters");
            }

            if (maParamsType == null)
            {
                return results;
            }

            Component[] comps = root.GetComponentsInChildren<Component>(true);
            if (comps == null || comps.Length == 0)
            {
                return results;
            }

            List<Component> maComponents = new List<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                Component c = comps[i];
                if (c != null && c.GetType() == maParamsType)
                {
                    maComponents.Add(c);
                }
            }

            if (maComponents.Count == 0)
            {
                return results;
            }

            FieldInfo parametersField = maParamsType.GetField("parameters", BindingFlags.Public | BindingFlags.Instance);
            if (parametersField == null)
            {
                return results;
            }

            for (int i = 0; i < maComponents.Count; i++)
            {
                if (!TryBuildController(maComponents[i], parametersField, i + 1, out AnimatorController controller, out Dictionary<string, (bool saved, bool synced)> defaults))
                {
                    continue;
                }

                if (controller == null)
                {
                    continue;
                }

                results.Add(new ControllerBuildResult(controller, defaults));
            }

            return results;
        }

        private static bool TryBuildController(
            Component component,
            FieldInfo parametersField,
            int index,
            out AnimatorController controller,
            out Dictionary<string, (bool saved, bool synced)> defaults)
        {
            controller = null;
            defaults = null;

            if (component == null || parametersField == null)
            {
                return false;
            }

            object listObj;
            try
            {
                listObj = parametersField.GetValue(component);
            }
            catch
            {
                return false;
            }

            System.Collections.IEnumerable enumerable = listObj as System.Collections.IEnumerable;
            if (enumerable == null)
            {
                return false;
            }

            string objectName = component.gameObject != null ? component.gameObject.name : "(GameObject)";
            controller = new AnimatorController
            {
                name = $"[MA Parameters] {objectName}[{index}]"
            };
            defaults = new Dictionary<string, (bool saved, bool synced)>();

            foreach (object entry in enumerable)
            {
                if (entry == null)
                {
                    continue;
                }

                Type entryType = entry.GetType();
                string nameOrPrefix = GetFieldString(entry, entryType, "nameOrPrefix");
                if (string.IsNullOrEmpty(nameOrPrefix) || GetFieldBool(entry, entryType, "isPrefix") || GetFieldBool(entry, entryType, "internalParameter"))
                {
                    continue;
                }

                string remapTo = GetFieldString(entry, entryType, "remapTo");
                string finalName = !string.IsNullOrEmpty(remapTo) ? remapTo : nameOrPrefix;
                if (string.IsNullOrEmpty(finalName) || controller.parameters.Any(p => p != null && p.name == finalName))
                {
                    continue;
                }

                float defaultValue = GetFieldFloat(entry, entryType, "defaultValue");
                bool saved = GetFieldBool(entry, entryType, "saved");
                bool localOnly = GetFieldBool(entry, entryType, "localOnly");
                bool synced = !localOnly;
                int syncTypeValue = GetFieldInt(entry, entryType, "syncType");

                AnimatorControllerParameterType paramType = AnimatorControllerParameterType.Float;
                if (syncTypeValue == 3)
                {
                    paramType = AnimatorControllerParameterType.Bool;
                }
                else if (syncTypeValue == 1)
                {
                    paramType = AnimatorControllerParameterType.Int;
                }
                else if (syncTypeValue != 2)
                {
                    synced = false;
                }

                AnimatorControllerParameter parameter = new AnimatorControllerParameter
                {
                    name = finalName,
                    type = paramType
                };

                switch (paramType)
                {
                    case AnimatorControllerParameterType.Bool:
                        parameter.defaultBool = defaultValue >= 0.5f;
                        break;
                    case AnimatorControllerParameterType.Int:
                        parameter.defaultInt = Mathf.RoundToInt(defaultValue);
                        break;
                    default:
                        parameter.defaultFloat = defaultValue;
                        break;
                }

                controller.AddParameter(parameter);
                defaults[finalName] = (saved, synced);
            }

            return true;
        }

        private static string GetFieldString(object obj, Type type, string fieldName)
        {
            try
            {
                FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return field != null ? field.GetValue(obj) as string : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool GetFieldBool(object obj, Type type, string fieldName)
        {
            try
            {
                FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return field != null && field.GetValue(obj) is bool value && value;
            }
            catch
            {
                return false;
            }
        }

        private static float GetFieldFloat(object obj, Type type, string fieldName)
        {
            try
            {
                FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                object value = field != null ? field.GetValue(obj) : null;
                if (value is float f)
                {
                    return f;
                }

                if (value is int i)
                {
                    return i;
                }

                return 0f;
            }
            catch
            {
                return 0f;
            }
        }

        private static int GetFieldInt(object obj, Type type, string fieldName)
        {
            try
            {
                FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return field != null && field.GetValue(obj) is int value ? value : 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
