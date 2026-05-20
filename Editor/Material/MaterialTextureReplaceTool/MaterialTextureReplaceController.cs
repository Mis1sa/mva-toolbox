using System.Collections.Generic;
using UnityEngine;

namespace MVA.Toolbox.MaterialTextureReplaceTool
{
    internal sealed class MaterialTextureReplaceController
    {
        private const string DefaultSaveFolder = "Assets/MVA Toolbox/MaterialTextureReplace/";

        private readonly MaterialTextureReplaceScanService _scanService = new MaterialTextureReplaceScanService();
        private readonly MaterialTextureReplacePreviewSession _previewSession = new MaterialTextureReplacePreviewSession();
        private readonly MaterialTextureReplaceApplyService _applyService = new MaterialTextureReplaceApplyService();
        private readonly Dictionary<Material, Material> _materialReplacements = new Dictionary<Material, Material>();
        private readonly Dictionary<Texture, Texture> _textureReplacements = new Dictionary<Texture, Texture>();

        private GameObject _targetObject;
        private MaterialTextureReplaceMode _mode = MaterialTextureReplaceMode.Material;
        private MaterialTextureScanResult _scanResult = MaterialTextureScanResult.Empty;
        private bool _extraCreateMaterials = true;
        private string _saveFolderRelative = DefaultSaveFolder;
        private string _materialSuffix = string.Empty;

        internal GameObject TargetObject => _targetObject;

        internal MaterialTextureReplaceMode Mode => _mode;

        internal IReadOnlyList<Material> FoundMaterials => _scanResult.Materials;

        internal IReadOnlyList<Texture> FoundTextures => _scanResult.Textures;

        internal IReadOnlyDictionary<Material, Material> MaterialReplacements => _materialReplacements;

        internal IReadOnlyDictionary<Texture, Texture> TextureReplacements => _textureReplacements;

        internal bool ExtraCreateMaterials
        {
            get => _extraCreateMaterials;
            set => _extraCreateMaterials = value;
        }

        internal string SaveFolderRelative
        {
            get => _saveFolderRelative;
            set => _saveFolderRelative = value ?? string.Empty;
        }

        internal string MaterialSuffix
        {
            get => _materialSuffix;
            set => _materialSuffix = value ?? string.Empty;
        }

        internal bool HasPreviewChanges => _previewSession.HasPreviewChanges;

        internal void SetTarget(GameObject targetObject)
        {
            targetObject = NormalizeTarget(targetObject);

            if (targetObject == _targetObject)
            {
                return;
            }

            ClearSessionAndMappings();
            _targetObject = targetObject;
            Rescan();
        }

        internal void SetMode(MaterialTextureReplaceMode mode)
        {
            if (mode == _mode)
            {
                return;
            }

            ClearSessionAndMappings();
            _mode = mode;
            Rescan();
        }

        internal void UpdateMaterialReplacement(Material sourceMaterial, Material replacementMaterial)
        {
            if (sourceMaterial == null)
            {
                return;
            }

            if (replacementMaterial == sourceMaterial)
            {
                replacementMaterial = null;
            }

            if (replacementMaterial == null)
            {
                _materialReplacements.Remove(sourceMaterial);
            }
            else
            {
                _materialReplacements[sourceMaterial] = replacementMaterial;
            }

            RefreshPreview();
        }

        internal void UpdateTextureReplacement(Texture sourceTexture, Texture replacementTexture)
        {
            if (sourceTexture == null)
            {
                return;
            }

            if (replacementTexture == sourceTexture)
            {
                replacementTexture = null;
            }

            if (replacementTexture == null)
            {
                _textureReplacements.Remove(sourceTexture);
            }
            else
            {
                _textureReplacements[sourceTexture] = replacementTexture;
            }

            RefreshPreview();
        }

        internal bool ToggleDisplay(out string errorMessage)
        {
            errorMessage = string.Empty;
            if (!HasPreviewChanges && _materialReplacements.Count == 0 && _textureReplacements.Count == 0)
            {
                errorMessage = "当前没有预览修改可切换显示。请先设置替换项以预览。";
                return false;
            }

            if (_previewSession.IsShowingModified)
            {
                _previewSession.ShowOriginals();
                return true;
            }

            _previewSession.ShowModified(_mode, _scanResult, _materialReplacements, _textureReplacements);
            return true;
        }

        internal MaterialTextureReplaceApplyResult ApplyChanges()
        {
            MaterialTextureReplaceApplyResult result = _mode == MaterialTextureReplaceMode.Material
                ? _applyService.ApplyMaterialChanges(_scanResult, _previewSession, _materialReplacements)
                : _applyService.ApplyTextureChanges(
                    _scanResult,
                    _previewSession,
                    _textureReplacements,
                    new MaterialTextureReplaceApplyOptions(_extraCreateMaterials, _saveFolderRelative, _materialSuffix));

            if (result == null || !result.Applied)
            {
                return result ?? new MaterialTextureReplaceApplyResult(false, string.Empty, _saveFolderRelative);
            }

            if (!string.IsNullOrEmpty(result.SaveFolderRelative))
            {
                _saveFolderRelative = result.SaveFolderRelative;
            }

            _materialReplacements.Clear();
            _textureReplacements.Clear();
            Rescan();
            return result;
        }

        internal void OnWindowDisabled()
        {
            _previewSession.RestoreAndClear();
            _materialReplacements.Clear();
            _textureReplacements.Clear();
            _targetObject = null;
            _scanResult = MaterialTextureScanResult.Empty;
        }

        private void RefreshPreview()
        {
            _previewSession.ShowModified(_mode, _scanResult, _materialReplacements, _textureReplacements);
        }

        private void ClearSessionAndMappings()
        {
            _previewSession.RestoreAndClear();
            _materialReplacements.Clear();
            _textureReplacements.Clear();
        }

        private void Rescan()
        {
            _scanResult = _scanService.Scan(_targetObject);
        }

        private static GameObject NormalizeTarget(GameObject targetObject)
        {
            if (targetObject == null)
            {
                return null;
            }

            return targetObject.scene.IsValid() ? targetObject : null;
        }
    }
}
