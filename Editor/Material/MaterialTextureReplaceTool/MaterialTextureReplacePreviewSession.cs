using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.MaterialTextureReplaceTool
{
    internal sealed class MaterialTextureReplacePreviewSession
    {
        private readonly Dictionary<Renderer, Material[]> _originalRendererMaterials = new Dictionary<Renderer, Material[]>();
        private readonly Dictionary<Material, Material> _tempMaterialInstances = new Dictionary<Material, Material>();

        internal bool HasBackup => _originalRendererMaterials.Count > 0;

        internal bool HasPreviewChanges { get; private set; }

        internal bool IsShowingModified { get; private set; } = true;

        internal bool ShowModified(
            MaterialTextureReplaceMode mode,
            MaterialTextureScanResult scanResult,
            IReadOnlyDictionary<Material, Material> materialReplacements,
            IReadOnlyDictionary<Texture, Texture> textureReplacements)
        {
            IsShowingModified = true;

            if (scanResult == null || !scanResult.HasRenderers)
            {
                ClearModifiedPreview();
                return false;
            }

            return mode == MaterialTextureReplaceMode.Material
                ? ApplyMaterialPreview(scanResult, materialReplacements)
                : ApplyTexturePreview(scanResult, textureReplacements);
        }

        internal void ShowOriginals()
        {
            RestoreRendererMaterialsFromBackup(false);
            IsShowingModified = false;
        }

        internal void RestoreAndClear()
        {
            RestoreRendererMaterialsFromBackup(true);
            DestroyTempMaterialInstances();
            HasPreviewChanges = false;
            IsShowingModified = false;
        }

        internal void ClearAfterApply()
        {
            _originalRendererMaterials.Clear();
            DestroyTempMaterialInstances();
            HasPreviewChanges = false;
            IsShowingModified = true;
        }

        internal bool TryGetOriginalMaterial(Renderer renderer, int materialIndex, out Material material)
        {
            material = null;
            if (renderer == null || materialIndex < 0)
            {
                return false;
            }

            if (!_originalRendererMaterials.TryGetValue(renderer, out Material[] originalMaterials) || originalMaterials == null || materialIndex >= originalMaterials.Length)
            {
                return false;
            }

            material = originalMaterials[materialIndex];
            return material != null;
        }

        private bool ApplyMaterialPreview(
            MaterialTextureScanResult scanResult,
            IReadOnlyDictionary<Material, Material> materialReplacements)
        {
            if (!HasEffectiveMaterialReplacement(materialReplacements))
            {
                ClearModifiedPreview();
                return false;
            }

            EnsureBackup(scanResult.Renderers);
            DestroyTempMaterialInstances();

            for (int rendererIndex = 0; rendererIndex < scanResult.Renderers.Count; rendererIndex++)
            {
                Renderer renderer = scanResult.Renderers[rendererIndex];
                if (!TryGetOriginalMaterials(renderer, out Material[] originalMaterials))
                {
                    continue;
                }

                Material[] previewMaterials = CloneMaterials(originalMaterials);
                bool changed = false;
                for (int materialIndex = 0; materialIndex < previewMaterials.Length; materialIndex++)
                {
                    Material originalMaterial = originalMaterials[materialIndex];
                    if (originalMaterial == null)
                    {
                        continue;
                    }

                    if (!materialReplacements.TryGetValue(originalMaterial, out Material replacement) || replacement == null || replacement == originalMaterial)
                    {
                        continue;
                    }

                    previewMaterials[materialIndex] = replacement;
                    changed = true;
                }

                if (changed)
                {
                    TryAssignSharedMaterials(renderer, previewMaterials);
                }
                else
                {
                    TryAssignSharedMaterials(renderer, originalMaterials);
                }
            }

            HasPreviewChanges = true;
            return true;
        }

        private bool ApplyTexturePreview(
            MaterialTextureScanResult scanResult,
            IReadOnlyDictionary<Texture, Texture> textureReplacements)
        {
            if (!HasEffectiveTextureReplacement(scanResult, textureReplacements))
            {
                ClearModifiedPreview();
                return false;
            }

            EnsureBackup(scanResult.Renderers);
            DestroyTempMaterialInstances();

            for (int rendererIndex = 0; rendererIndex < scanResult.Renderers.Count; rendererIndex++)
            {
                Renderer renderer = scanResult.Renderers[rendererIndex];
                if (!TryGetOriginalMaterials(renderer, out Material[] originalMaterials))
                {
                    continue;
                }

                Material[] previewMaterials = CloneMaterials(originalMaterials);
                for (int materialIndex = 0; materialIndex < originalMaterials.Length; materialIndex++)
                {
                    Material originalMaterial = originalMaterials[materialIndex];
                    if (originalMaterial == null || !scanResult.TryGetTextureUsage(originalMaterial, out MaterialTextureUsage usage))
                    {
                        continue;
                    }

                    if (!MaterialWillChange(usage, textureReplacements))
                    {
                        continue;
                    }

                    Material previewMaterial = GetOrCreateTempMaterial(originalMaterial);
                    previewMaterial.CopyPropertiesFromMaterial(originalMaterial);

                    IReadOnlyList<MaterialTexturePropertyUsage> properties = usage.Properties;
                    for (int propertyIndex = 0; propertyIndex < properties.Count; propertyIndex++)
                    {
                        MaterialTexturePropertyUsage propertyUsage = properties[propertyIndex];
                        if (propertyUsage == null || string.IsNullOrEmpty(propertyUsage.PropertyName) || propertyUsage.Texture == null)
                        {
                            continue;
                        }

                        if (textureReplacements.TryGetValue(propertyUsage.Texture, out Texture replacementTexture) && replacementTexture != null && replacementTexture != propertyUsage.Texture)
                        {
                            previewMaterial.SetTexture(propertyUsage.PropertyName, replacementTexture);
                        }
                        else
                        {
                            previewMaterial.SetTexture(propertyUsage.PropertyName, propertyUsage.Texture);
                        }
                    }

                    previewMaterials[materialIndex] = previewMaterial;
                }

                TryAssignSharedMaterials(renderer, previewMaterials);
            }

            HasPreviewChanges = true;
            return true;
        }

        private void ClearModifiedPreview()
        {
            if (HasPreviewChanges)
            {
                RestoreRendererMaterialsFromBackup(false);
            }

            DestroyTempMaterialInstances();
            HasPreviewChanges = false;
        }

        private void EnsureBackup(IReadOnlyList<Renderer> renderers)
        {
            if (renderers == null)
            {
                return;
            }

            for (int rendererIndex = 0; rendererIndex < renderers.Count; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                if (renderer == null || _originalRendererMaterials.ContainsKey(renderer))
                {
                    continue;
                }

                Material[] sharedMaterials = renderer.sharedMaterials;
                _originalRendererMaterials[renderer] = CloneMaterials(sharedMaterials);
            }
        }

        private void RestoreRendererMaterialsFromBackup(bool clearBackupCache)
        {
            foreach (KeyValuePair<Renderer, Material[]> entry in _originalRendererMaterials)
            {
                Renderer renderer = entry.Key;
                if (renderer == null)
                {
                    continue;
                }

                TryAssignSharedMaterials(renderer, entry.Value);
            }

            if (clearBackupCache)
            {
                _originalRendererMaterials.Clear();
            }
        }

        private void DestroyTempMaterialInstances()
        {
            foreach (Material temporaryMaterial in _tempMaterialInstances.Values)
            {
                if (temporaryMaterial == null)
                {
                    continue;
                }

                try
                {
                    Object.DestroyImmediate(temporaryMaterial);
                }
                catch (InvalidOperationException)
                {
                }
                catch (ArgumentException)
                {
                }
            }

            _tempMaterialInstances.Clear();
        }

        private Material GetOrCreateTempMaterial(Material originalMaterial)
        {
            if (_tempMaterialInstances.TryGetValue(originalMaterial, out Material tempMaterial) && tempMaterial != null)
            {
                return tempMaterial;
            }

            tempMaterial = new Material(originalMaterial);
            _tempMaterialInstances[originalMaterial] = tempMaterial;
            return tempMaterial;
        }

        private bool TryGetOriginalMaterials(Renderer renderer, out Material[] originalMaterials)
        {
            originalMaterials = null;
            if (renderer == null)
            {
                return false;
            }

            if (_originalRendererMaterials.TryGetValue(renderer, out originalMaterials) && originalMaterials != null)
            {
                return true;
            }

            return false;
        }

        private static bool HasEffectiveMaterialReplacement(IReadOnlyDictionary<Material, Material> materialReplacements)
        {
            if (materialReplacements == null || materialReplacements.Count == 0)
            {
                return false;
            }

            foreach (KeyValuePair<Material, Material> entry in materialReplacements)
            {
                if (entry.Key != null && entry.Value != null && entry.Key != entry.Value)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasEffectiveTextureReplacement(
            MaterialTextureScanResult scanResult,
            IReadOnlyDictionary<Texture, Texture> textureReplacements)
        {
            if (scanResult == null || textureReplacements == null || textureReplacements.Count == 0)
            {
                return false;
            }

            IReadOnlyList<MaterialTextureUsage> usages = scanResult.TextureUsages;
            for (int usageIndex = 0; usageIndex < usages.Count; usageIndex++)
            {
                MaterialTextureUsage usage = usages[usageIndex];
                if (usage == null || !MaterialWillChange(usage, textureReplacements))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static bool MaterialWillChange(
            MaterialTextureUsage usage,
            IReadOnlyDictionary<Texture, Texture> textureReplacements)
        {
            if (usage == null || textureReplacements == null || textureReplacements.Count == 0)
            {
                return false;
            }

            IReadOnlyList<MaterialTexturePropertyUsage> properties = usage.Properties;
            for (int propertyIndex = 0; propertyIndex < properties.Count; propertyIndex++)
            {
                MaterialTexturePropertyUsage propertyUsage = properties[propertyIndex];
                if (propertyUsage?.Texture == null)
                {
                    continue;
                }

                if (textureReplacements.TryGetValue(propertyUsage.Texture, out Texture replacementTexture) && replacementTexture != null && replacementTexture != propertyUsage.Texture)
                {
                    return true;
                }
            }

            return false;
        }

        private static Material[] CloneMaterials(Material[] materials)
        {
            if (materials == null || materials.Length == 0)
            {
                return Array.Empty<Material>();
            }

            Material[] clone = new Material[materials.Length];
            Array.Copy(materials, clone, materials.Length);
            return clone;
        }

        private static void TryAssignSharedMaterials(Renderer renderer, Material[] materials)
        {
            if (renderer == null)
            {
                return;
            }

            try
            {
                renderer.sharedMaterials = materials ?? Array.Empty<Material>();
            }
            catch (InvalidOperationException)
            {
            }
            catch (ArgumentException)
            {
            }
            catch (NullReferenceException)
            {
            }
        }
    }
}
