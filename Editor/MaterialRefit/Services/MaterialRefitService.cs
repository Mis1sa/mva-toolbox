using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MVA.Toolbox.MaterialRefit.Services
{
    /// <summary>
    /// 提供材质 / 贴图替换的扫描、预览和应用逻辑，不包含任何 IMGUI 代码。
    /// 由 MaterialRefitWindow 持有一个实例并驱动。
    /// </summary>
    internal sealed class MaterialRefitService
    {
        internal enum Mode
        {
            Material,
            Texture
        }

        GameObject _targetRoot;
        Mode _mode = Mode.Material;

        readonly List<Material> _foundMaterials = new List<Material>();
        readonly List<Texture> _foundTextures = new List<Texture>();

        readonly Dictionary<Material, Material> _materialReplacements = new Dictionary<Material, Material>();
        readonly Dictionary<Texture, Texture> _textureReplacements = new Dictionary<Texture, Texture>();

        readonly Dictionary<Renderer, Material[]> _originalRendererMaterials = new Dictionary<Renderer, Material[]>();
        readonly Dictionary<Material, Material> _tempMaterialInstances = new Dictionary<Material, Material>();
        readonly Dictionary<Material, Material> _tempToOriginal = new Dictionary<Material, Material>();

        bool _extraCreateMaterials = true;
        string _saveFolderRelative = "Assets/MVA Toolbox/MR/";

        bool _showModified = true;
        bool _hasPreviewChanges;
        bool _applied;
        bool _hasBackup;

        public GameObject TargetRoot => _targetRoot;
        public Mode CurrentMode => _mode;

        public IReadOnlyList<Material> FoundMaterials => _foundMaterials;
        public IReadOnlyList<Texture> FoundTextures => _foundTextures;

        public IReadOnlyDictionary<Material, Material> MaterialReplacements => _materialReplacements;
        public IReadOnlyDictionary<Texture, Texture> TextureReplacements => _textureReplacements;

        public bool ExtraCreateMaterials
        {
            get => _extraCreateMaterials;
            set => _extraCreateMaterials = value;
        }

        public string SaveFolderRelative
        {
            get => _saveFolderRelative;
            set => _saveFolderRelative = value;
        }

        public bool HasPreviewChanges => _hasPreviewChanges;

        public void SetTarget(GameObject root)
        {
            if (root == _targetRoot)
            {
                return;
            }

            ClearPreviewState();
            _targetRoot = root;
            ScanForReferences();
        }

        public void SetMode(Mode newMode)
        {
            if (newMode == _mode)
            {
                return;
            }

            if (_hasBackup && !_applied)
            {
                RestoreOriginals();
            }

            _materialReplacements.Clear();
            _textureReplacements.Clear();
            _hasPreviewChanges = false;

            _mode = newMode;
            ScanForReferences();
        }

        public void UpdateMaterialReplacement(Material source, Material replacement)
        {
            if (source == null)
            {
                return;
            }

            if (replacement == source)
            {
                replacement = null;
            }

            if (replacement == null)
            {
                _materialReplacements.Remove(source);
            }
            else
            {
                _materialReplacements[source] = replacement;
            }

            ApplyMaterialPreview();
        }

        public void UpdateTextureReplacement(Texture source, Texture replacement)
        {
            if (source == null)
            {
                return;
            }

            if (replacement == source)
            {
                replacement = null;
            }

            if (replacement == null)
            {
                _textureReplacements.Remove(source);
            }
            else
            {
                _textureReplacements[source] = replacement;
            }

            ApplyTexturePreview();
        }

        public void ApplyChanges()
        {
            if (_mode == Mode.Material)
            {
                ApplyMaterialChanges();
            }
            else
            {
                ApplyTextureChanges();
            }
        }

        public void ToggleDisplay()
        {
            if (!_hasPreviewChanges && _materialReplacements.Count == 0 && _textureReplacements.Count == 0)
            {
                EditorUtility.DisplayDialog("切换显示", "当前没有预览修改可切换显示。请先设置替换项以预览。", "确定");
                return;
            }

            _showModified = !_showModified;
            if (_showModified)
            {
                ShowModified();
            }
            else
            {
                ShowOriginals();
            }
        }

        public void OnWindowDisabled()
        {
            if (!_applied)
            {
                RestoreOriginals();
            }

            ClearCaches();
        }

        void ScanForReferences()
        {
            ClearCaches();
            if (_targetRoot == null)
            {
                return;
            }

            var renderers = _targetRoot.GetComponentsInChildren<Renderer>(true);

            var mats = new HashSet<Material>();
            foreach (var r in renderers)
            {
                if (r == null)
                {
                    continue;
                }

                var shared = r.sharedMaterials;
                foreach (var m in shared)
                {
                    if (m != null)
                    {
                        mats.Add(m);
                    }
                }
            }

            _foundMaterials.Clear();
            _foundMaterials.AddRange(mats);

            var texs = new HashSet<Texture>();
            foreach (var m in _foundMaterials)
            {
                if (m == null)
                {
                    continue;
                }

                var shader = m.shader;
                int count = ShaderUtil.GetPropertyCount(shader);
                for (int i = 0; i < count; i++)
                {
                    if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                    {
                        string prop = ShaderUtil.GetPropertyName(shader, i);
                        try
                        {
                            var tex = m.GetTexture(prop);
                            if (tex != null)
                            {
                                texs.Add(tex);
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }

            _foundTextures.Clear();
            _foundTextures.AddRange(texs);
        }

        void ApplyMaterialPreview()
        {
            if (_targetRoot == null)
            {
                return;
            }

            var renderers = _targetRoot.GetComponentsInChildren<Renderer>(true);

            if (!_hasPreviewChanges)
            {
                _originalRendererMaterials.Clear();
                foreach (var r in renderers)
                {
                    if (r == null)
                    {
                        continue;
                    }

                    _originalRendererMaterials[r] = r.sharedMaterials.ToArray();
                }

                _hasBackup = true;
            }

            foreach (var r in renderers)
            {
                if (r == null)
                {
                    continue;
                }

                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (src != null && _materialReplacements.TryGetValue(src, out var repl) && repl != null)
                    {
                        mats[i] = repl;
                        changed = true;
                    }
                }

                if (changed)
                {
                    r.sharedMaterials = mats;
                }
            }

            _hasPreviewChanges = _materialReplacements.Count > 0;
            _applied = false;
        }

        void ApplyMaterialChanges()
        {
            if (!_hasPreviewChanges)
            {
                return;
            }

            var renderers = _targetRoot != null
                ? _targetRoot.GetComponentsInChildren<Renderer>(true)
                : Array.Empty<Renderer>();

            foreach (var r in renderers)
            {
                if (r == null)
                {
                    continue;
                }

                Undo.RegisterCompleteObjectUndo(r, "Apply Material Replacement");

                Material[] mats = r.sharedMaterials.ToArray();
                Material[] backup = null;
                if (_originalRendererMaterials.TryGetValue(r, out var arr))
                {
                    backup = arr;
                }

                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material origMat = null;
                    if (backup != null && i < backup.Length)
                    {
                        origMat = backup[i];
                    }
                    else
                    {
                        origMat = mats[i];
                    }

                    if (origMat != null && _materialReplacements.TryGetValue(origMat, out var repl) && repl != null)
                    {
                        mats[i] = repl;
                        changed = true;
                    }
                }

                if (changed)
                {
                    r.sharedMaterials = mats;
                }
            }

            foreach (var t in _tempMaterialInstances.Values)
            {
                if (t != null)
                {
                    UnityEngine.Object.DestroyImmediate(t);
                }
            }

            _tempMaterialInstances.Clear();
            _tempToOriginal.Clear();

            _originalRendererMaterials.Clear();
            _hasBackup = false;
            _hasPreviewChanges = false;
            _applied = true;

            ScanForReferences();
            _materialReplacements.Clear();
            _textureReplacements.Clear();
            EditorSceneManager.MarkAllScenesDirty();
        }

        void ApplyTexturePreview()
        {
            if (_targetRoot == null)
            {
                return;
            }

            var renderers = _targetRoot.GetComponentsInChildren<Renderer>(true);
            var usage = BuildTextureUsageMap();

            bool anyReplacement = false;
            foreach (var kv in usage)
            {
                foreach (var srcTex in kv.Value.Values)
                {
                    if (srcTex != null && _textureReplacements.TryGetValue(srcTex, out var cand) && cand != null && cand != srcTex)
                    {
                        anyReplacement = true;
                        break;
                    }
                }

                if (anyReplacement)
                {
                    break;
                }
            }

            if (!anyReplacement)
            {
                if (_hasPreviewChanges)
                {
                    RestoreOriginals();
                }

                _hasPreviewChanges = false;
                return;
            }

            if (!_hasPreviewChanges)
            {
                _originalRendererMaterials.Clear();
                foreach (var r in renderers)
                {
                    if (r == null)
                    {
                        continue;
                    }

                    _originalRendererMaterials[r] = r.sharedMaterials.ToArray();
                }

                _hasBackup = true;
            }

            foreach (var r in renderers)
            {
                if (r == null)
                {
                    continue;
                }

                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var slotMat = mats[i];
                    if (slotMat == null)
                    {
                        continue;
                    }

                    Material orig = null;
                    if (_tempToOriginal.TryGetValue(slotMat, out var foundOrig))
                    {
                        orig = foundOrig;
                    }
                    else if (_foundMaterials.Contains(slotMat))
                    {
                        orig = slotMat;
                    }
                    else
                    {
                        if (_originalRendererMaterials.TryGetValue(r, out var origArr) && i < origArr.Length)
                        {
                            orig = origArr[i];
                        }
                        else
                        {
                            orig = _foundMaterials.FirstOrDefault(x => x != null && x.name == slotMat.name);
                        }
                    }

                    if (orig == null)
                    {
                        continue;
                    }

                    if (!usage.TryGetValue(orig, out var props))
                    {
                        continue;
                    }

                    bool materialWillChange = false;
                    foreach (var p in props)
                    {
                        var srcTex = p.Value;
                        if (srcTex != null && _textureReplacements.TryGetValue(srcTex, out var cand) && cand != null && cand != srcTex)
                        {
                            materialWillChange = true;
                            break;
                        }
                    }

                    if (!materialWillChange)
                    {
                        continue;
                    }

                    if (!_tempMaterialInstances.TryGetValue(orig, out var temp))
                    {
                        temp = new Material(orig);
                        _tempMaterialInstances[orig] = temp;
                        _tempToOriginal[temp] = orig;
                    }

                    foreach (var p in props)
                    {
                        var srcTex = p.Value;
                        if (srcTex != null && _textureReplacements.TryGetValue(srcTex, out var cand) && cand != null && cand != srcTex)
                        {
                            temp.SetTexture(p.Key, cand);
                        }
                    }

                    mats[i] = temp;
                    changed = true;
                }

                if (changed)
                {
                    r.sharedMaterials = mats;
                }
            }

            _hasPreviewChanges = true;
            _applied = false;
        }

        Dictionary<Material, Dictionary<string, Texture>> BuildTextureUsageMap()
        {
            var map = new Dictionary<Material, Dictionary<string, Texture>>();
            foreach (var m in _foundMaterials)
            {
                if (m == null)
                {
                    continue;
                }

                var shader = m.shader;
                int count = ShaderUtil.GetPropertyCount(shader);
                for (int i = 0; i < count; i++)
                {
                    if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
                    {
                        continue;
                    }

                    string prop = ShaderUtil.GetPropertyName(shader, i);
                    try
                    {
                        var tex = m.GetTexture(prop);
                        if (tex != null)
                        {
                            if (!map.TryGetValue(m, out var inner))
                            {
                                inner = new Dictionary<string, Texture>();
                                map[m] = inner;
                            }

                            inner[prop] = tex;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return map;
        }

        void ApplyTextureChanges()
        {
            if (!_hasPreviewChanges)
            {
                return;
            }

            var usage = BuildTextureUsageMap();

            if (_extraCreateMaterials)
            {
                string baseFolder = _saveFolderRelative.TrimEnd('/');
                if (!AssetDatabase.IsValidFolder(baseFolder))
                {
                    CreateAssetFolderIfNeeded(baseFolder);
                }

                // 每次应用时在当前保存路径下创建一个以时间戳命名的子文件夹，避免不同批次资源覆盖
                string timeFolderName = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string targetFolder = baseFolder + "/" + timeFolderName;
                if (!AssetDatabase.IsValidFolder(targetFolder))
                {
                    CreateAssetFolderIfNeeded(targetFolder);
                }

                var materialCopies = new Dictionary<Material, Material>();

                foreach (var kv in usage)
                {
                    var origMat = kv.Key;
                    var props = kv.Value;
                    bool changed = false;
                    var newMat = new Material(origMat);
                    foreach (var p in props)
                    {
                        var srcTex = p.Value;
                        if (srcTex != null && _textureReplacements.TryGetValue(srcTex, out var repl) && repl != null && repl != srcTex)
                        {
                            newMat.SetTexture(p.Key, repl);
                            changed = true;
                        }
                    }

                    if (changed)
                    {
                        string safeName = MakeSafeFilename(origMat.name) + "_copy.mat";
                        string assetPath = targetFolder + "/" + safeName;
                        assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
                        AssetDatabase.CreateAsset(newMat, assetPath);
                        materialCopies[origMat] = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(newMat);
                    }
                }

                var renderers = _targetRoot != null
                    ? _targetRoot.GetComponentsInChildren<Renderer>(true)
                    : Array.Empty<Renderer>();

                foreach (var r in renderers)
                {
                    if (r == null)
                    {
                        continue;
                    }

                    var mats = r.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        Material origMat = null;
                        if (_originalRendererMaterials.TryGetValue(r, out var origArr) && i < origArr.Length)
                        {
                            origMat = origArr[i];
                        }
                        else
                        {
                            origMat = mats[i];
                        }

                        if (origMat != null && materialCopies.TryGetValue(origMat, out var cp))
                        {
                            mats[i] = cp;
                            changed = true;
                        }
                    }

                    if (changed)
                    {
                        Undo.RegisterCompleteObjectUndo(r, "Apply Texture Replacement");
                        r.sharedMaterials = mats;
                    }
                }
            }
            else
            {
                foreach (var kv in usage)
                {
                    var mat = kv.Key;
                    var props = kv.Value;
                    bool matChanged = false;
                    foreach (var p in props)
                    {
                        var src = p.Value;
                        if (src != null && _textureReplacements.TryGetValue(src, out var repl) && repl != null && repl != src)
                        {
                            Undo.RegisterCompleteObjectUndo(mat, "Apply Texture Replacement");
                            mat.SetTexture(p.Key, repl);
                            matChanged = true;
                        }
                    }

                    if (matChanged)
                    {
                        EditorUtility.SetDirty(mat);
                    }
                }

                AssetDatabase.SaveAssets();
            }

            _originalRendererMaterials.Clear();
            foreach (var t in _tempMaterialInstances.Values)
            {
                if (t != null)
                {
                    UnityEngine.Object.DestroyImmediate(t);
                }
            }

            _tempMaterialInstances.Clear();
            _tempToOriginal.Clear();
            _hasBackup = false;
            _hasPreviewChanges = false;
            _applied = true;

            ScanForReferences();
            _materialReplacements.Clear();
            _textureReplacements.Clear();
            EditorSceneManager.MarkAllScenesDirty();
        }

        void CreateAssetFolderIfNeeded(string relPath)
        {
            if (AssetDatabase.IsValidFolder(relPath))
            {
                return;
            }

            string[] parts = relPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(cur, parts[i]);
                }

                cur = next;
            }
        }

        string MakeSafeFilename(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name;
        }

        void RestoreOriginals()
        {
            foreach (var kv in _originalRendererMaterials)
            {
                if (kv.Key == null)
                {
                    continue;
                }

                try
                {
                    kv.Key.sharedMaterials = kv.Value;
                }
                catch
                {
                }
            }

            _originalRendererMaterials.Clear();

            foreach (var t in _tempMaterialInstances.Values)
            {
                if (t != null)
                {
                    UnityEngine.Object.DestroyImmediate(t);
                }
            }

            _tempMaterialInstances.Clear();
            _tempToOriginal.Clear();

            _hasPreviewChanges = false;
            _applied = false;
            _hasBackup = false;
        }

        void ClearPreviewState()
        {
            RestoreOriginals();
            _materialReplacements.Clear();
            _textureReplacements.Clear();
        }

        void ClearCaches()
        {
            _foundMaterials.Clear();
            _foundTextures.Clear();
            _materialReplacements.Clear();
            _textureReplacements.Clear();

            try
            {
                foreach (var t in _tempMaterialInstances.Values)
                {
                    if (t != null)
                    {
                        UnityEngine.Object.DestroyImmediate(t);
                    }
                }
            }
            catch
            {
            }

            _tempMaterialInstances.Clear();
            _tempToOriginal.Clear();
            _originalRendererMaterials.Clear();
            _hasPreviewChanges = false;
            _hasBackup = false;
        }

        void ShowOriginals()
        {
            foreach (var kv in _originalRendererMaterials)
            {
                if (kv.Key == null)
                {
                    continue;
                }

                try
                {
                    kv.Key.sharedMaterials = kv.Value;
                }
                catch
                {
                }
            }

            _showModified = false;
        }

        void ShowModified()
        {
            var renderers = _targetRoot != null
                ? _targetRoot.GetComponentsInChildren<Renderer>(true)
                : Array.Empty<Renderer>();

            if (_mode == Mode.Material)
            {
                foreach (var r in renderers)
                {
                    if (r == null)
                    {
                        continue;
                    }

                    Material[] mats = r.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        Material origMat = null;
                        if (_originalRendererMaterials.TryGetValue(r, out var origArr) && i < origArr.Length)
                        {
                            origMat = origArr[i];
                        }
                        else
                        {
                            origMat = mats[i];
                        }

                        if (origMat != null && _materialReplacements.TryGetValue(origMat, out var repl) && repl != null)
                        {
                            mats[i] = repl;
                            changed = true;
                        }
                    }

                    if (changed)
                    {
                        r.sharedMaterials = mats;
                    }
                }
            }
            else
            {
                ApplyTexturePreview();
            }

            _showModified = true;
        }
    }
}
