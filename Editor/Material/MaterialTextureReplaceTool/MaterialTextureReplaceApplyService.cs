using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.MaterialTextureReplaceTool
{
    internal sealed class MaterialTextureReplaceApplyService
    {
        private const string DefaultSaveFolder = "Assets/MVA Toolbox/MaterialTextureReplace";

        internal MaterialTextureReplaceApplyResult ApplyMaterialChanges(
            MaterialTextureScanResult scanResult,
            MaterialTextureReplacePreviewSession previewSession,
            IReadOnlyDictionary<Material, Material> materialReplacements)
        {
            if (scanResult == null || previewSession == null || !previewSession.HasPreviewChanges)
            {
                return new MaterialTextureReplaceApplyResult(false, string.Empty, string.Empty);
            }

            IReadOnlyList<Renderer> renderers = scanResult.Renderers;
            for (int rendererIndex = 0; rendererIndex < renderers.Count; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                if (renderer == null)
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material originalMaterial = ResolveOriginalMaterial(previewSession, renderer, materialIndex, materials[materialIndex]);
                    if (originalMaterial == null)
                    {
                        continue;
                    }

                    if (!TryGetEffectiveMaterialReplacement(materialReplacements, originalMaterial, out Material replacementMaterial))
                    {
                        continue;
                    }

                    materials[materialIndex] = replacementMaterial;
                    changed = true;
                }

                if (!changed)
                {
                    continue;
                }

                Undo.RegisterCompleteObjectUndo(renderer, "Apply Material Replacement");
                renderer.sharedMaterials = materials;
            }

            previewSession.ClearAfterApply();
            EditorSceneManager.MarkAllScenesDirty();
            return new MaterialTextureReplaceApplyResult(true, string.Empty, string.Empty);
        }

        internal MaterialTextureReplaceApplyResult ApplyTextureChanges(
            MaterialTextureScanResult scanResult,
            MaterialTextureReplacePreviewSession previewSession,
            IReadOnlyDictionary<Texture, Texture> textureReplacements,
            MaterialTextureReplaceApplyOptions options)
        {
            if (scanResult == null || previewSession == null || !previewSession.HasPreviewChanges)
            {
                return new MaterialTextureReplaceApplyResult(false, string.Empty, options?.SaveFolderRelative ?? string.Empty);
            }

            if (options == null)
            {
                options = new MaterialTextureReplaceApplyOptions(true, DefaultSaveFolder, string.Empty);
            }

            return options.CreateMaterialCopies
                ? ApplyTextureChangesWithMaterialCopies(scanResult, previewSession, textureReplacements, options)
                : ApplyTextureChangesInPlace(scanResult, previewSession, textureReplacements, options);
        }

        private MaterialTextureReplaceApplyResult ApplyTextureChangesWithMaterialCopies(
            MaterialTextureScanResult scanResult,
            MaterialTextureReplacePreviewSession previewSession,
            IReadOnlyDictionary<Texture, Texture> textureReplacements,
            MaterialTextureReplaceApplyOptions options)
        {
            string normalizedFolder = NormalizeSaveFolder(options.SaveFolderRelative);
            string normalizedFolderWithSlash = normalizedFolder + "/";

            if (!EnsureAssetFolderExists(normalizedFolder))
            {
                return new MaterialTextureReplaceApplyResult(false, "材质保存路径无效，请选择 Assets 目录下的文件夹。", normalizedFolderWithSlash);
            }

            string timeFolderName = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string targetFolder = normalizedFolder + "/" + timeFolderName;
            if (!EnsureAssetFolderExists(targetFolder))
            {
                return new MaterialTextureReplaceApplyResult(false, "无法创建材质保存目录，请检查保存路径。", normalizedFolderWithSlash);
            }

            Dictionary<Material, Material> materialCopies = BuildMaterialCopies(scanResult, textureReplacements, targetFolder, options.MaterialSuffix);

            IReadOnlyList<Renderer> renderers = scanResult.Renderers;
            for (int rendererIndex = 0; rendererIndex < renderers.Count; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                if (renderer == null)
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material originalMaterial = ResolveOriginalMaterial(previewSession, renderer, materialIndex, materials[materialIndex]);
                    if (originalMaterial == null || !materialCopies.TryGetValue(originalMaterial, out Material copyMaterial) || copyMaterial == null)
                    {
                        continue;
                    }

                    materials[materialIndex] = copyMaterial;
                    changed = true;
                }

                if (!changed)
                {
                    continue;
                }

                Undo.RegisterCompleteObjectUndo(renderer, "Apply Texture Replacement");
                renderer.sharedMaterials = materials;
            }

            previewSession.ClearAfterApply();
            EditorSceneManager.MarkAllScenesDirty();
            AssetDatabase.SaveAssets();
            return new MaterialTextureReplaceApplyResult(true, string.Empty, normalizedFolderWithSlash);
        }

        private MaterialTextureReplaceApplyResult ApplyTextureChangesInPlace(
            MaterialTextureScanResult scanResult,
            MaterialTextureReplacePreviewSession previewSession,
            IReadOnlyDictionary<Texture, Texture> textureReplacements,
            MaterialTextureReplaceApplyOptions options)
        {
            IReadOnlyList<MaterialTextureUsage> usages = scanResult.TextureUsages;
            for (int usageIndex = 0; usageIndex < usages.Count; usageIndex++)
            {
                MaterialTextureUsage usage = usages[usageIndex];
                Material material = usage?.Material;
                if (material == null)
                {
                    continue;
                }

                bool registeredUndo = false;
                bool changed = false;
                IReadOnlyList<MaterialTexturePropertyUsage> properties = usage.Properties;
                for (int propertyIndex = 0; propertyIndex < properties.Count; propertyIndex++)
                {
                    MaterialTexturePropertyUsage propertyUsage = properties[propertyIndex];
                    if (propertyUsage == null || string.IsNullOrEmpty(propertyUsage.PropertyName) || propertyUsage.Texture == null)
                    {
                        continue;
                    }

                    if (!TryGetEffectiveTextureReplacement(textureReplacements, propertyUsage.Texture, out Texture replacementTexture))
                    {
                        continue;
                    }

                    if (!registeredUndo)
                    {
                        Undo.RegisterCompleteObjectUndo(material, "Apply Texture Replacement");
                        registeredUndo = true;
                    }

                    material.SetTexture(propertyUsage.PropertyName, replacementTexture);
                    changed = true;
                }

                if (changed)
                {
                    EditorUtility.SetDirty(material);
                }
            }

            if (previewSession.HasBackup)
            {
                previewSession.ShowOriginals();
            }

            previewSession.ClearAfterApply();
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkAllScenesDirty();
            return new MaterialTextureReplaceApplyResult(true, string.Empty, options.SaveFolderRelative);
        }

        private static Dictionary<Material, Material> BuildMaterialCopies(
            MaterialTextureScanResult scanResult,
            IReadOnlyDictionary<Texture, Texture> textureReplacements,
            string targetFolder,
            string materialSuffix)
        {
            Dictionary<Material, Material> materialCopies = new Dictionary<Material, Material>();
            IReadOnlyList<MaterialTextureUsage> usages = scanResult.TextureUsages;
            for (int usageIndex = 0; usageIndex < usages.Count; usageIndex++)
            {
                MaterialTextureUsage usage = usages[usageIndex];
                Material originalMaterial = usage?.Material;
                if (originalMaterial == null)
                {
                    continue;
                }

                Material newMaterial = new Material(originalMaterial);
                bool changed = false;
                IReadOnlyList<MaterialTexturePropertyUsage> properties = usage.Properties;
                for (int propertyIndex = 0; propertyIndex < properties.Count; propertyIndex++)
                {
                    MaterialTexturePropertyUsage propertyUsage = properties[propertyIndex];
                    if (propertyUsage == null || string.IsNullOrEmpty(propertyUsage.PropertyName) || propertyUsage.Texture == null)
                    {
                        continue;
                    }

                    if (!TryGetEffectiveTextureReplacement(textureReplacements, propertyUsage.Texture, out Texture replacementTexture))
                    {
                        continue;
                    }

                    newMaterial.SetTexture(propertyUsage.PropertyName, replacementTexture);
                    changed = true;
                }

                if (!changed)
                {
                    Object.DestroyImmediate(newMaterial);
                    continue;
                }

                string assetPath = BuildMaterialAssetPath(targetFolder, originalMaterial.name, materialSuffix);
                AssetDatabase.CreateAsset(newMaterial, assetPath);
                materialCopies[originalMaterial] = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            }

            return materialCopies;
        }

        private static string BuildMaterialAssetPath(string targetFolder, string materialName, string materialSuffix)
        {
            string safeName = MakeSafeFilename(materialName);
            if (!string.IsNullOrEmpty(materialSuffix))
            {
                string safeSuffix = MakeSafeFilename(materialSuffix);
                if (!string.IsNullOrEmpty(safeSuffix))
                {
                    safeName += "_" + safeSuffix;
                }
            }

            if (string.IsNullOrEmpty(safeName))
            {
                safeName = "Material";
            }

            string assetPath = targetFolder + "/" + safeName + ".mat";
            return AssetDatabase.GenerateUniqueAssetPath(assetPath);
        }

        private static Material ResolveOriginalMaterial(
            MaterialTextureReplacePreviewSession previewSession,
            Renderer renderer,
            int materialIndex,
            Material currentMaterial)
        {
            if (previewSession != null && previewSession.TryGetOriginalMaterial(renderer, materialIndex, out Material originalMaterial))
            {
                return originalMaterial;
            }

            return currentMaterial;
        }

        private static bool TryGetEffectiveMaterialReplacement(
            IReadOnlyDictionary<Material, Material> materialReplacements,
            Material sourceMaterial,
            out Material replacementMaterial)
        {
            replacementMaterial = null;
            if (materialReplacements == null || sourceMaterial == null)
            {
                return false;
            }

            if (!materialReplacements.TryGetValue(sourceMaterial, out replacementMaterial))
            {
                return false;
            }

            return replacementMaterial != null && replacementMaterial != sourceMaterial;
        }

        private static bool TryGetEffectiveTextureReplacement(
            IReadOnlyDictionary<Texture, Texture> textureReplacements,
            Texture sourceTexture,
            out Texture replacementTexture)
        {
            replacementTexture = null;
            if (textureReplacements == null || sourceTexture == null)
            {
                return false;
            }

            if (!textureReplacements.TryGetValue(sourceTexture, out replacementTexture))
            {
                return false;
            }

            return replacementTexture != null && replacementTexture != sourceTexture;
        }

        private static string NormalizeSaveFolder(string input)
        {
            string rootFolder = input;
            if (!string.IsNullOrEmpty(rootFolder))
            {
                rootFolder = rootFolder.Trim().Replace("\\", "/");
            }

            while (!string.IsNullOrEmpty(rootFolder) && rootFolder.EndsWith("/", StringComparison.Ordinal))
            {
                rootFolder = rootFolder.Substring(0, rootFolder.Length - 1);
            }

            if (string.IsNullOrEmpty(rootFolder) ||
                !(string.Equals(rootFolder, "Assets", StringComparison.OrdinalIgnoreCase) ||
                  rootFolder.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)))
            {
                rootFolder = DefaultSaveFolder;
            }

            return rootFolder;
        }

        private static bool EnsureAssetFolderExists(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return false;
            }

            relativePath = relativePath.Trim().Replace("\\", "/");
            while (relativePath.EndsWith("/", StringComparison.Ordinal))
            {
                relativePath = relativePath.Substring(0, relativePath.Length - 1);
            }

            if (AssetDatabase.IsValidFolder(relativePath))
            {
                return true;
            }

            if (!(string.Equals(relativePath, "Assets", StringComparison.OrdinalIgnoreCase) ||
                  relativePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            string[] parts = relativePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return false;
            }

            string current = parts[0];
            if (!string.Equals(current, "Assets", StringComparison.OrdinalIgnoreCase) || !AssetDatabase.IsValidFolder(current))
            {
                return false;
            }

            for (int partIndex = 1; partIndex < parts.Length; partIndex++)
            {
                string part = parts[partIndex];
                if (string.IsNullOrEmpty(part))
                {
                    continue;
                }

                string next = current + "/" + part;
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, part);
                }

                current = next;
            }

            return AssetDatabase.IsValidFolder(relativePath);
        }

        private static string MakeSafeFilename(string name)
        {
            string safeName = name ?? string.Empty;
            char[] invalidChars = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalidChars.Length; i++)
            {
                safeName = safeName.Replace(invalidChars[i], '_');
            }

            return safeName;
        }
    }
}
