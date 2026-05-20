using System.Collections.Generic;
using UnityEngine;

namespace MVA.Toolbox.MaterialTextureReplaceTool
{
    internal enum MaterialTextureReplaceMode
    {
        Material,
        Texture
    }

    internal sealed class MaterialTextureReplaceApplyOptions
    {
        internal MaterialTextureReplaceApplyOptions(bool createMaterialCopies, string saveFolderRelative, string materialSuffix)
        {
            CreateMaterialCopies = createMaterialCopies;
            SaveFolderRelative = saveFolderRelative ?? string.Empty;
            MaterialSuffix = materialSuffix ?? string.Empty;
        }

        internal bool CreateMaterialCopies { get; }

        internal string SaveFolderRelative { get; }

        internal string MaterialSuffix { get; }
    }

    internal sealed class MaterialTextureReplaceApplyResult
    {
        internal MaterialTextureReplaceApplyResult(bool applied, string errorMessage, string saveFolderRelative)
        {
            Applied = applied;
            ErrorMessage = errorMessage ?? string.Empty;
            SaveFolderRelative = saveFolderRelative ?? string.Empty;
        }

        internal bool Applied { get; }

        internal string ErrorMessage { get; }

        internal string SaveFolderRelative { get; }

        internal bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    }

    internal sealed class MaterialTexturePropertyUsage
    {
        internal MaterialTexturePropertyUsage(string propertyName, Texture texture)
        {
            PropertyName = propertyName;
            Texture = texture;
        }

        internal string PropertyName { get; }

        internal Texture Texture { get; }
    }

    internal sealed class MaterialTextureUsage
    {
        private readonly List<MaterialTexturePropertyUsage> _properties;
        private readonly Dictionary<string, Texture> _texturesByProperty;

        internal MaterialTextureUsage(Material material, List<MaterialTexturePropertyUsage> properties)
        {
            Material = material;
            _properties = properties ?? new List<MaterialTexturePropertyUsage>();
            _texturesByProperty = new Dictionary<string, Texture>();

            for (int i = 0; i < _properties.Count; i++)
            {
                MaterialTexturePropertyUsage propertyUsage = _properties[i];
                if (propertyUsage == null || string.IsNullOrEmpty(propertyUsage.PropertyName) || propertyUsage.Texture == null)
                {
                    continue;
                }

                _texturesByProperty[propertyUsage.PropertyName] = propertyUsage.Texture;
            }
        }

        internal Material Material { get; }

        internal IReadOnlyList<MaterialTexturePropertyUsage> Properties => _properties;

        internal IReadOnlyDictionary<string, Texture> TexturesByProperty => _texturesByProperty;

        internal bool TryGetTexture(string propertyName, out Texture texture)
        {
            if (string.IsNullOrEmpty(propertyName))
            {
                texture = null;
                return false;
            }

            return _texturesByProperty.TryGetValue(propertyName, out texture);
        }
    }

    internal sealed class MaterialTextureScanResult
    {
        private readonly Dictionary<Material, MaterialTextureUsage> _usageByMaterial;

        internal static MaterialTextureScanResult Empty { get; } = new MaterialTextureScanResult(
            new List<Renderer>(),
            new List<Material>(),
            new List<Texture>(),
            new List<MaterialTextureUsage>());

        internal MaterialTextureScanResult(
            List<Renderer> renderers,
            List<Material> materials,
            List<Texture> textures,
            List<MaterialTextureUsage> textureUsages)
        {
            Renderers = renderers ?? new List<Renderer>();
            Materials = materials ?? new List<Material>();
            Textures = textures ?? new List<Texture>();
            TextureUsages = textureUsages ?? new List<MaterialTextureUsage>();

            _usageByMaterial = new Dictionary<Material, MaterialTextureUsage>();
            for (int i = 0; i < TextureUsages.Count; i++)
            {
                MaterialTextureUsage usage = TextureUsages[i];
                if (usage?.Material == null)
                {
                    continue;
                }

                _usageByMaterial[usage.Material] = usage;
            }
        }

        internal IReadOnlyList<Renderer> Renderers { get; }

        internal IReadOnlyList<Material> Materials { get; }

        internal IReadOnlyList<Texture> Textures { get; }

        internal IReadOnlyList<MaterialTextureUsage> TextureUsages { get; }

        internal bool HasRenderers => Renderers.Count > 0;

        internal bool TryGetTextureUsage(Material material, out MaterialTextureUsage usage)
        {
            if (material == null)
            {
                usage = null;
                return false;
            }

            return _usageByMaterial.TryGetValue(material, out usage);
        }
    }
}
