using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.AnimationBakeTool
{
    internal sealed class AnimationBakeDefaultValueResolver
    {
        private readonly GameObject _targetRoot;
        private readonly Transform _controllerRoot;

        internal AnimationBakeDefaultValueResolver(GameObject targetRoot, Transform controllerRoot)
        {
            _targetRoot = targetRoot;
            _controllerRoot = controllerRoot;
        }

        internal object GetDefaultValue(EditorCurveBinding binding)
        {
            if (binding.path == null)
            {
                return null;
            }

            GameObject target = ResolveTargetGameObject(binding.path);
            if (target == null)
            {
                return null;
            }

            string propertyName = binding.propertyName;

            if (binding.type == typeof(GameObject) && propertyName == "m_IsActive")
            {
                return target.activeSelf;
            }

            if (propertyName.StartsWith("material.", StringComparison.Ordinal))
            {
                Renderer renderer = target.GetComponent<Renderer>();
                if (renderer != null)
                {
                    string strippedPropertyName = propertyName.Substring("material.".Length);
                    if (TryGetMaterialPropertyValue(renderer, strippedPropertyName, out object materialValue))
                    {
                        return materialValue;
                    }
                }

                return null;
            }

            Component component = target.GetComponent(binding.type);
            if (component == null)
            {
                return null;
            }

            if (propertyName.StartsWith("blendShape.", StringComparison.Ordinal) && component is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            {
                string blendShapeName = propertyName.Substring("blendShape.".Length);
                int blendShapeIndex = smr.sharedMesh.GetBlendShapeIndex(blendShapeName);
                if (blendShapeIndex != -1)
                {
                    return smr.GetBlendShapeWeight(blendShapeIndex);
                }
            }

            if (component is Transform transform)
            {
                return propertyName switch
                {
                    "m_LocalPosition.x" => transform.localPosition.x,
                    "m_LocalPosition.y" => transform.localPosition.y,
                    "m_LocalPosition.z" => transform.localPosition.z,
                    "m_LocalRotation.x" => transform.localRotation.x,
                    "m_LocalRotation.y" => transform.localRotation.y,
                    "m_LocalRotation.z" => transform.localRotation.z,
                    "m_LocalRotation.w" => transform.localRotation.w,
                    "m_LocalScale.x" => transform.localScale.x,
                    "m_LocalScale.y" => transform.localScale.y,
                    "m_LocalScale.z" => transform.localScale.z,
                    _ => GetSerializedValue(component, propertyName)
                };
            }

            return GetSerializedValue(component, propertyName);
        }

        private GameObject ResolveTargetGameObject(string relativePath)
        {
            if (relativePath == null)
            {
                return null;
            }

            if (relativePath.Length == 0)
            {
                Transform root = _controllerRoot ?? (_targetRoot != null ? _targetRoot.transform : null);
                return root != null ? root.gameObject : null;
            }

            HashSet<Transform> triedRoots = new HashSet<Transform>();

            GameObject TryFind(Transform root)
            {
                if (root == null || !triedRoots.Add(root))
                {
                    return null;
                }

                Transform node = root.Find(relativePath);
                return node != null ? node.gameObject : null;
            }

            GameObject target = TryFind(_controllerRoot);
            if (target != null)
            {
                return target;
            }

            Transform avatarRoot = _targetRoot != null ? _targetRoot.transform : null;
            target = TryFind(avatarRoot);
            if (target != null)
            {
                return target;
            }

            if (_controllerRoot != null && avatarRoot != null && _controllerRoot != avatarRoot && _controllerRoot.IsChildOf(avatarRoot))
            {
                string prefix = BuildRelativePath(avatarRoot, _controllerRoot);
                if (!string.IsNullOrEmpty(prefix))
                {
                    string combined = string.IsNullOrEmpty(relativePath) ? prefix : $"{prefix}/{relativePath}";
                    Transform node = avatarRoot.Find(combined);
                    if (node != null)
                    {
                        return node.gameObject;
                    }
                }
            }

            return null;
        }

        private static bool TryGetMaterialPropertyValue(Renderer renderer, string propertyName, out object value)
        {
            value = null;
            if (renderer == null)
            {
                return false;
            }

            Material material = renderer.sharedMaterial;
            if (material == null || string.IsNullOrEmpty(propertyName))
            {
                return false;
            }

            string basePropertyName = propertyName;
            bool hasComponent = false;
            char componentChar = '\0';

            int lastDot = propertyName.LastIndexOf('.');
            if (lastDot >= 0 && lastDot < propertyName.Length - 1)
            {
                hasComponent = true;
                componentChar = propertyName[lastDot + 1];
                basePropertyName = propertyName.Substring(0, lastDot);
            }

            if (!material.HasProperty(basePropertyName))
            {
                return false;
            }

            ShaderUtil.ShaderPropertyType? propType = null;
            Shader shader = material.shader;
            if (shader != null)
            {
                int count = ShaderUtil.GetPropertyCount(shader);
                for (int i = 0; i < count; i++)
                {
                    if (ShaderUtil.GetPropertyName(shader, i) == basePropertyName)
                    {
                        propType = ShaderUtil.GetPropertyType(shader, i);
                        break;
                    }
                }
            }

            ShaderUtil.ShaderPropertyType resolvedType = propType ?? ShaderUtil.ShaderPropertyType.Float;
            switch (resolvedType)
            {
                case ShaderUtil.ShaderPropertyType.Color:
                    if (!hasComponent)
                    {
                        return false;
                    }

                    Color color = material.GetColor(basePropertyName);
                    if (TryExtractComponent(color, componentChar, out float colorValue))
                    {
                        value = colorValue;
                        return true;
                    }

                    return false;
                case ShaderUtil.ShaderPropertyType.Vector:
                    if (!hasComponent)
                    {
                        return false;
                    }

                    Vector4 vector = material.GetVector(basePropertyName);
                    if (TryExtractComponent(vector, componentChar, out float vectorValue))
                    {
                        value = vectorValue;
                        return true;
                    }

                    return false;
                case ShaderUtil.ShaderPropertyType.Float:
                case ShaderUtil.ShaderPropertyType.Range:
                    value = material.GetFloat(basePropertyName);
                    return true;
                case ShaderUtil.ShaderPropertyType.TexEnv:
                    value = material.GetTexture(basePropertyName);
                    return true;
            }

            if (hasComponent)
            {
                Vector4 vec = material.GetVector(basePropertyName);
                if (TryExtractComponent(vec, componentChar, out float componentValue))
                {
                    value = componentValue;
                    return true;
                }
            }

            return false;
        }

        private static bool TryExtractComponent(Color color, char component, out float value)
        {
            value = 0f;
            switch (char.ToLowerInvariant(component))
            {
                case 'r':
                    value = color.r;
                    return true;
                case 'g':
                    value = color.g;
                    return true;
                case 'b':
                    value = color.b;
                    return true;
                case 'a':
                    value = color.a;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryExtractComponent(Vector4 vector, char component, out float value)
        {
            value = 0f;
            switch (char.ToLowerInvariant(component))
            {
                case 'x':
                case 'r':
                    value = vector.x;
                    return true;
                case 'y':
                case 'g':
                    value = vector.y;
                    return true;
                case 'z':
                case 'b':
                    value = vector.z;
                    return true;
                case 'w':
                case 'a':
                    value = vector.w;
                    return true;
                default:
                    return false;
            }
        }

        private static string BuildRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null)
            {
                return null;
            }

            if (root == target)
            {
                return string.Empty;
            }

            if (!target.IsChildOf(root))
            {
                return null;
            }

            Stack<string> stack = new Stack<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                stack.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", stack);
        }

        private static object GetSerializedValue(Component component, string propertyName)
        {
            using SerializedObject serializedObject = new SerializedObject(component);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                return null;
            }

            return property.propertyType switch
            {
                SerializedPropertyType.Float => property.floatValue,
                SerializedPropertyType.Boolean => property.boolValue,
                SerializedPropertyType.ObjectReference => property.objectReferenceValue,
                _ => null
            };
        }
    }
}
