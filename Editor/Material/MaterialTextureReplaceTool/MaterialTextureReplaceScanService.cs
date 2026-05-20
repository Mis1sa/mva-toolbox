using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.MaterialTextureReplaceTool
{
    internal sealed class MaterialTextureReplaceScanService
    {
        internal MaterialTextureScanResult Scan(GameObject targetRoot)
        {
            if (targetRoot == null)
            {
                return MaterialTextureScanResult.Empty;
            }

            Renderer[] rendererArray = targetRoot.GetComponentsInChildren<Renderer>(true);
            List<Renderer> renderers = new List<Renderer>(rendererArray.Length);
            List<Material> materials = new List<Material>();
            HashSet<Material> seenMaterials = new HashSet<Material>();

            for (int rendererIndex = 0; rendererIndex < rendererArray.Length; rendererIndex++)
            {
                Renderer renderer = rendererArray[rendererIndex];
                if (renderer == null)
                {
                    continue;
                }

                renderers.Add(renderer);
                Material[] sharedMaterials = renderer.sharedMaterials;
                for (int materialIndex = 0; materialIndex < sharedMaterials.Length; materialIndex++)
                {
                    Material material = sharedMaterials[materialIndex];
                    if (material == null || !seenMaterials.Add(material))
                    {
                        continue;
                    }

                    materials.Add(material);
                }
            }

            List<Texture> textures = new List<Texture>();
            HashSet<Texture> seenTextures = new HashSet<Texture>();
            List<MaterialTextureUsage> textureUsages = new List<MaterialTextureUsage>();

            for (int materialIndex = 0; materialIndex < materials.Count; materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material == null)
                {
                    continue;
                }

                List<MaterialTexturePropertyUsage> propertyUsages = CollectTextureProperties(material, textures, seenTextures);
                if (propertyUsages.Count == 0)
                {
                    continue;
                }

                textureUsages.Add(new MaterialTextureUsage(material, propertyUsages));
            }

            return new MaterialTextureScanResult(renderers, materials, textures, textureUsages);
        }

        private static List<MaterialTexturePropertyUsage> CollectTextureProperties(
            Material material,
            List<Texture> textures,
            HashSet<Texture> seenTextures)
        {
            List<MaterialTexturePropertyUsage> properties = new List<MaterialTexturePropertyUsage>();
            Shader shader = material.shader;
            if (shader == null)
            {
                return properties;
            }

            int propertyCount = ShaderUtil.GetPropertyCount(shader);
            for (int propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
            {
                if (ShaderUtil.GetPropertyType(shader, propertyIndex) != ShaderUtil.ShaderPropertyType.TexEnv)
                {
                    continue;
                }

                string propertyName = ShaderUtil.GetPropertyName(shader, propertyIndex);
                if (!TryGetTexture(material, propertyName, out Texture texture) || texture == null)
                {
                    continue;
                }

                properties.Add(new MaterialTexturePropertyUsage(propertyName, texture));
                if (seenTextures.Add(texture))
                {
                    textures.Add(texture);
                }
            }

            return properties;
        }

        private static bool TryGetTexture(Material material, string propertyName, out Texture texture)
        {
            texture = null;
            if (material == null || string.IsNullOrEmpty(propertyName) || !material.HasProperty(propertyName))
            {
                return false;
            }

            try
            {
                texture = material.GetTexture(propertyName);
                return texture != null;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
}
