using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.PhysBoneCollisionOverlay
{
    internal static class PhysBoneCollisionOverlaySerializedAccess
    {
        internal static Type FindType(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                return null;
            }

            Type type = Type.GetType(fullName);
            if (type != null)
            {
                return type;
            }

            System.Reflection.Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int assemblyIndex = 0; assemblyIndex < assemblies.Length; assemblyIndex++)
            {
                type = assemblies[assemblyIndex].GetType(fullName);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        internal static T ReadObjectReference<T>(Component component, params string[] names) where T : UnityEngine.Object
        {
            SerializedObject serializedObject = new SerializedObject(component);
            SerializedProperty property = FindFirstProperty(serializedObject, names);
            return property != null ? property.objectReferenceValue as T : null;
        }

        internal static float ReadFloat(Component component, float fallback, params string[] names)
        {
            SerializedObject serializedObject = new SerializedObject(component);
            SerializedProperty property = FindFirstProperty(serializedObject, names);
            if (property == null)
            {
                return fallback;
            }

            return property.propertyType == SerializedPropertyType.Float ? property.floatValue : fallback;
        }

        internal static int ReadInt(Component component, int fallback, params string[] names)
        {
            SerializedObject serializedObject = new SerializedObject(component);
            SerializedProperty property = FindFirstProperty(serializedObject, names);
            if (property == null)
            {
                return fallback;
            }

            if (property.propertyType == SerializedPropertyType.Integer || property.propertyType == SerializedPropertyType.Enum)
            {
                return property.intValue;
            }

            return fallback;
        }

        internal static bool ReadBool(Component component, bool fallback, params string[] names)
        {
            SerializedObject serializedObject = new SerializedObject(component);
            SerializedProperty property = FindFirstProperty(serializedObject, names);
            if (property == null)
            {
                return fallback;
            }

            return property.propertyType == SerializedPropertyType.Boolean ? property.boolValue : fallback;
        }

        internal static Quaternion ReadQuaternion(Component component, Quaternion fallback, params string[] names)
        {
            SerializedObject serializedObject = new SerializedObject(component);
            SerializedProperty property = FindFirstProperty(serializedObject, names);
            if (property == null)
            {
                return fallback;
            }

            return property.propertyType == SerializedPropertyType.Quaternion ? property.quaternionValue : fallback;
        }

        internal static Vector3 ReadVector3(Component component, Vector3 fallback, params string[] names)
        {
            SerializedObject serializedObject = new SerializedObject(component);
            SerializedProperty property = FindFirstProperty(serializedObject, names);
            if (property == null)
            {
                return fallback;
            }

            return property.propertyType == SerializedPropertyType.Vector3 ? property.vector3Value : fallback;
        }

        internal static AnimationCurve ReadCurve(Component component, params string[] names)
        {
            SerializedObject serializedObject = new SerializedObject(component);
            SerializedProperty property = FindFirstProperty(serializedObject, names);
            if (property == null)
            {
                return null;
            }

            return property.propertyType == SerializedPropertyType.AnimationCurve ? property.animationCurveValue : null;
        }

        internal static HashSet<Transform> GetIgnoredTransforms(Component physBone)
        {
            HashSet<Transform> result = new HashSet<Transform>();
            if (physBone == null)
            {
                return result;
            }

            SerializedObject serializedObject = new SerializedObject(physBone);
            SerializedProperty property = FindFirstProperty(serializedObject, "ignoreTransforms");
            if (property == null || !property.isArray)
            {
                return result;
            }

            for (int index = 0; index < property.arraySize; index++)
            {
                SerializedProperty element = property.GetArrayElementAtIndex(index);
                Transform transform = element != null ? element.objectReferenceValue as Transform : null;
                if (transform != null)
                {
                    result.Add(transform);
                }
            }

            return result;
        }

        internal static List<Component> GetReferencedColliders(Component physBone, Func<Component, bool> isPhysBoneCollider)
        {
            List<Component> colliders = new List<Component>();
            if (physBone == null)
            {
                return colliders;
            }

            SerializedObject serializedObject = new SerializedObject(physBone);
            SerializedProperty property = FindFirstProperty(serializedObject, "colliders");
            if (property == null || !property.isArray)
            {
                return colliders;
            }

            for (int index = 0; index < property.arraySize; index++)
            {
                SerializedProperty element = property.GetArrayElementAtIndex(index);
                Component component = element != null ? element.objectReferenceValue as Component : null;
                if (component == null || isPhysBoneCollider == null || !isPhysBoneCollider(component))
                {
                    continue;
                }

                if (!colliders.Contains(component))
                {
                    colliders.Add(component);
                }
            }

            return colliders;
        }

        private static SerializedProperty FindFirstProperty(SerializedObject serializedObject, params string[] names)
        {
            if (serializedObject == null || names == null)
            {
                return null;
            }

            for (int index = 0; index < names.Length; index++)
            {
                string name = names[index];
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                SerializedProperty property = serializedObject.FindProperty(name);
                if (property != null)
                {
                    return property;
                }
            }

            return null;
        }
    }
}
