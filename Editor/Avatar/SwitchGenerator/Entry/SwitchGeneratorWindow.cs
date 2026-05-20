using System;
using System.Collections.Generic;
using MVA.Toolbox.SwitchGenerator.Preview;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.SwitchGenerator.Entry
{
    internal sealed partial class SwitchGeneratorWindow : EditorWindow
    {
        private const float W_OBJECT = 160f;
        private const float W_MODE = 100f;
        private const float W_SPLIT = 80f;
        private const float W_LABEL = 40f;
        private const float W_STATE = 60f;

        private VRCAvatarDescriptor _avatar;
        private SwitchGeneratorConfig _config;
        private SwitchGeneratorConfig.LayerConfig _draftLayer = new SwitchGeneratorConfig.LayerConfig();
        private bool _useConfigScript;
        private int _selectedConfigIndex = -1;
        private bool _useNDMFMode;

        private SwitchPreviewSession _previewSession = new SwitchPreviewSession();
        private float _previewValue;

        private string[] _availableLayerNames = Array.Empty<string>();
        private string[] _availableParameterNames = Array.Empty<string>();
        private string[] _availableMenuPaths = Array.Empty<string>();
        private int _selectedLayerPopupIndex = -1;
        private int _selectedParameterPopupIndex = -1;
        private int _selectedMenuPathIndex = -1;

        private List<SwitchGeneratorConfig.LayerConfig> _availableConfigEntries = new List<SwitchGeneratorConfig.LayerConfig>();
        private string[] _configEntryOptions = Array.Empty<string>();

        private bool _isEditingConfigEntry;
        private SwitchGeneratorConfig.LayerConfig _editingConfigEntry;
        private bool _avatarMismatch;

        private int _pendingIntWDConversion;

        private Vector2 _scrollPosition;

        public static void Open()
        {
            var window = GetWindow<SwitchGeneratorWindow>(false, "开关生成");
            window.minSize = new Vector2(520f, 480f);
            window.Show();
        }

        private void OnEnable()
        {
            _draftLayer ??= SwitchGeneratorLayerConfigEditing.CreatePreparedLayerConfig();
            SwitchGeneratorLayerConfigEditing.PrepareLayerConfig(_draftLayer);
            if (_avatar != null)
            {
                RefreshAvatarContext();
            }
        }

        private void OnDisable()
        {
            if (_previewSession.IsPreviewing)
            {
                _previewSession.Stop();
            }
        }

        private void OnGUI()
        {
            DrawAvatarField();
            if (_avatar == null)
            {
                return;
            }

            DrawConfigScriptSection();

            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
            {
                using (new EditorGUI.DisabledScope(_useConfigScript && !_isEditingConfigEntry))
                {
                    if (_pendingIntWDConversion != 0)
                    {
                        int conversion = _pendingIntWDConversion;
                        _pendingIntWDConversion = 0;
                        ApplyWDConversion(conversion > 0);
                        Repaint();
                    }

                    using (var scrollView = new EditorGUILayout.ScrollViewScope(_scrollPosition, GUILayout.ExpandHeight(true)))
                    {
                        _scrollPosition = scrollView.scrollPosition;
                        DrawCoreSettings();
                        DrawTargetObjects();
                    }

                    GUILayout.FlexibleSpace();
                    DrawPreviewBlock();
                    DrawModeAndConfigNameRow();
                    DrawActions();
                }
            }
        }

        private SwitchGeneratorConfig.LayerConfig GetSelectedLayer()
        {
            return _draftLayer;
        }

        private void DrawAvatarField()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("请拖入场景中的 Avatar", EditorStyles.boldLabel);

            Object shown = _avatar ? (Object)_avatar.gameObject : null;
            var newObj = EditorGUILayout.ObjectField("Avatar", shown, typeof(Object), true);
            if (newObj == shown)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            if (_previewSession.IsPreviewing)
            {
                _previewSession.Stop();
            }

            var resolvedAvatar = ResolveAvatarDescriptor(newObj);
            if (resolvedAvatar != null)
            {
                _avatar = resolvedAvatar;
                _config = _avatar.GetComponent<SwitchGeneratorConfig>();
                LoadFromConfig(_config);
                ResetDraftLayer();
                RefreshAvatarContext();
            }
            else
            {
                _avatar = null;
                _config = null;
                _useConfigScript = false;
                LoadFromConfig(null);
                ResetDraftLayer();
                _availableLayerNames = Array.Empty<string>();
                _availableParameterNames = Array.Empty<string>();
                _availableMenuPaths = Array.Empty<string>();
                _selectedLayerPopupIndex = -1;
                _selectedParameterPopupIndex = -1;
                _selectedMenuPathIndex = -1;
                _avatarMismatch = false;
            }

            EditorGUILayout.EndVertical();
        }

        private static VRCAvatarDescriptor ResolveAvatarDescriptor(Object obj)
        {
            if (obj == null)
            {
                return null;
            }

            if (obj is VRCAvatarDescriptor descriptor)
            {
                return descriptor;
            }

            if (obj is GameObject gameObject)
            {
                return gameObject.GetComponentInParent<VRCAvatarDescriptor>();
            }

            if (obj is Component component)
            {
                return component.GetComponentInParent<VRCAvatarDescriptor>();
            }

            return null;
        }

        private void DrawCoreSettings()
        {
            var layer = GetSelectedLayer();
            if (layer == null)
            {
                return;
            }

            layer.EnsureCollections();

            EditorGUILayout.Space();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            using (new EditorGUI.DisabledScope(_isEditingConfigEntry))
            {
                int prevType = (int)layer.switchType;
                int newType = GUILayout.Toolbar(prevType, new[] { "Bool Switch", "Int Switch", "Float Switch" });

                if (prevType != newType)
                {
                    layer.switchType = (SwitchGeneratorConfig.SwitchType)newType;
                    RefreshAvatarContext();
                    SwitchGeneratorLayerConfigEditing.EnsureDefaultTargets(layer);
                    if (_previewSession.IsPreviewing)
                    {
                        _previewSession.Stop();
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                float prevLabel = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 80f;

                if (!layer.overwriteLayer)
                {
                    layer.layerName = EditorGUILayout.TextField("层级名称", layer.layerName);
                }
                else
                {
                    using (new EditorGUI.DisabledScope(_availableLayerNames.Length == 0))
                    {
                        _selectedLayerPopupIndex = Mathf.Clamp(_selectedLayerPopupIndex, -1, _availableLayerNames.Length - 1);

                        int matchingIndex = -1;
                        if (!string.IsNullOrEmpty(layer.layerName) && _availableLayerNames != null && _availableLayerNames.Length > 0)
                        {
                            matchingIndex = Array.IndexOf(_availableLayerNames, layer.layerName);
                        }

                        bool originalMissing = !string.IsNullOrEmpty(layer.layerName) && matchingIndex < 0;
                        string[] layerOptions;
                        int displayIndex;

                        if (_availableLayerNames.Length == 0)
                        {
                            string label = originalMissing ? $"[{layer.layerName}] 缺失" : "(无可用层级)";
                            layerOptions = new[] { label };
                            displayIndex = 0;
                            _selectedLayerPopupIndex = -1;
                        }
                        else if (originalMissing)
                        {
                            layerOptions = new string[_availableLayerNames.Length + 1];
                            layerOptions[0] = $"[{layer.layerName}] 缺失";
                            Array.Copy(_availableLayerNames, 0, layerOptions, 1, _availableLayerNames.Length);

                            _selectedLayerPopupIndex = -1;
                            displayIndex = 0;

                            int newIdx = EditorGUILayout.Popup("层级名称", displayIndex, layerOptions);
                            if (newIdx > 0)
                            {
                                _selectedLayerPopupIndex = newIdx - 1;
                                if (_selectedLayerPopupIndex >= 0 && _selectedLayerPopupIndex < _availableLayerNames.Length)
                                {
                                    layer.layerName = _availableLayerNames[_selectedLayerPopupIndex];
                                }
                            }

                            goto AfterLayerPopup;
                        }
                        else
                        {
                            layerOptions = _availableLayerNames;
                            if (matchingIndex >= 0 && (_selectedLayerPopupIndex < 0 || _selectedLayerPopupIndex >= _availableLayerNames.Length))
                            {
                                _selectedLayerPopupIndex = matchingIndex;
                            }

                            displayIndex = Mathf.Clamp(_selectedLayerPopupIndex, 0, _availableLayerNames.Length - 1);
                        }

                        int changedIdx = EditorGUILayout.Popup("层级名称", displayIndex, layerOptions);
                        if (changedIdx != displayIndex)
                        {
                            _selectedLayerPopupIndex = Mathf.Clamp(changedIdx, 0, _availableLayerNames.Length - 1);
                            if (_selectedLayerPopupIndex >= 0 && _selectedLayerPopupIndex < _availableLayerNames.Length)
                            {
                                layer.layerName = _availableLayerNames[_selectedLayerPopupIndex];
                            }
                        }

                    AfterLayerPopup: ;
                    }
                }

                bool newOverwriteLayer = EditorGUILayout.ToggleLeft("覆盖层级", layer.overwriteLayer, GUILayout.Width(100));
                if (newOverwriteLayer != layer.overwriteLayer)
                {
                    layer.overwriteLayer = newOverwriteLayer;
                    if (layer.overwriteLayer)
                    {
                        _selectedLayerPopupIndex = SwitchGeneratorWindowOptions.EnsureLayerSelection(
                            layer,
                            _availableLayerNames,
                            _selectedLayerPopupIndex);
                    }
                }

                EditorGUIUtility.labelWidth = prevLabel;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                float prevLabel = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 80f;

                if (!layer.overwriteParameter)
                {
                    layer.parameterName = EditorGUILayout.TextField("参数名称", layer.parameterName);
                }
                else
                {
                    using (new EditorGUI.DisabledScope(_availableParameterNames.Length == 0))
                    {
                        _selectedParameterPopupIndex = Mathf.Clamp(_selectedParameterPopupIndex, -1, _availableParameterNames.Length - 1);

                        int matchingIndex = -1;
                        if (!string.IsNullOrEmpty(layer.parameterName) && _availableParameterNames != null && _availableParameterNames.Length > 0)
                        {
                            matchingIndex = Array.IndexOf(_availableParameterNames, layer.parameterName);
                        }

                        bool originalMissing = !string.IsNullOrEmpty(layer.parameterName) && matchingIndex < 0;
                        string[] paramOptions;
                        int displayIndex;

                        if (_availableParameterNames.Length == 0)
                        {
                            string label = originalMissing ? $"[{layer.parameterName}] 缺失" : "(无可用参数)";
                            paramOptions = new[] { label };
                            displayIndex = 0;
                            _selectedParameterPopupIndex = -1;
                        }
                        else if (originalMissing)
                        {
                            paramOptions = new string[_availableParameterNames.Length + 1];
                            paramOptions[0] = $"[{layer.parameterName}] 缺失";
                            Array.Copy(_availableParameterNames, 0, paramOptions, 1, _availableParameterNames.Length);

                            _selectedParameterPopupIndex = -1;
                            displayIndex = 0;

                            int newIdx = EditorGUILayout.Popup("参数名称", displayIndex, paramOptions);
                            if (newIdx > 0)
                            {
                                _selectedParameterPopupIndex = newIdx - 1;
                                if (_selectedParameterPopupIndex >= 0 && _selectedParameterPopupIndex < _availableParameterNames.Length)
                                {
                                    SwitchGeneratorWindowOptions.ApplySelectedParameterName(
                                        layer,
                                        _availableParameterNames,
                                        _selectedParameterPopupIndex);
                                }
                            }

                            goto AfterParameterPopup;
                        }
                        else
                        {
                            paramOptions = _availableParameterNames;
                            if (matchingIndex >= 0 && (_selectedParameterPopupIndex < 0 || _selectedParameterPopupIndex >= _availableParameterNames.Length))
                            {
                                _selectedParameterPopupIndex = matchingIndex;
                            }

                            displayIndex = Mathf.Clamp(_selectedParameterPopupIndex, 0, _availableParameterNames.Length - 1);
                        }

                        int changedIdx = EditorGUILayout.Popup("参数名称", displayIndex, paramOptions);
                        if (changedIdx != displayIndex)
                        {
                            _selectedParameterPopupIndex = Mathf.Clamp(changedIdx, 0, _availableParameterNames.Length - 1);
                            if (_selectedParameterPopupIndex >= 0 && _selectedParameterPopupIndex < _availableParameterNames.Length)
                            {
                                SwitchGeneratorWindowOptions.ApplySelectedParameterName(
                                    layer,
                                    _availableParameterNames,
                                    _selectedParameterPopupIndex);
                            }
                        }

                    AfterParameterPopup: ;
                    }
                }

                bool newOverwriteParameter = EditorGUILayout.ToggleLeft("覆盖参数", layer.overwriteParameter, GUILayout.Width(100));
                if (newOverwriteParameter != layer.overwriteParameter)
                {
                    layer.overwriteParameter = newOverwriteParameter;
                    if (layer.overwriteParameter)
                    {
                        _selectedParameterPopupIndex = SwitchGeneratorWindowOptions.EnsureParameterSelection(
                            layer,
                            _availableParameterNames,
                            _selectedParameterPopupIndex);
                    }
                    else
                    {
                        _selectedParameterPopupIndex = -1;
                    }
                }

                EditorGUIUtility.labelWidth = prevLabel;
            }

            layer.writeDefaults = (SwitchGeneratorConfig.WriteDefaultsMode)EditorGUILayout.Popup(
                "Write Defaults", (int)layer.writeDefaults, new[] { "Auto", "On", "Off" });

            if (layer.switchType == SwitchGeneratorConfig.SwitchType.Bool)
            {
                layer.defaultBoolValue = EditorGUILayout.IntSlider("参数默认值", layer.defaultBoolValue, 0, 1);
            }
            else if (layer.switchType == SwitchGeneratorConfig.SwitchType.Int)
            {
                int maxVal = layer.intGroups != null ? Mathf.Max(0, layer.intGroups.Count - 1) : 0;
                layer.defaultIntValue = EditorGUILayout.IntSlider("参数默认值", layer.defaultIntValue, 0, maxVal);
            }
            else
            {
                layer.defaultFloatValue = EditorGUILayout.Slider("参数默认值", layer.defaultFloatValue, 0f, 1f);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                string newPath = EditorGUILayout.TextField("动画剪辑路径", layer.clipSaveRoot);
                if (!string.Equals(newPath, layer.clipSaveRoot, StringComparison.Ordinal))
                {
                    layer.clipSaveRoot = SwitchGeneratorWindowOptions.NormalizeClipRootPath(newPath);
                }

                if (GUILayout.Button("浏览", GUILayout.Width(60f)))
                {
                    string startPath = Application.dataPath;
                    if (!string.IsNullOrWhiteSpace(layer.clipSaveRoot) && layer.clipSaveRoot.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
                    {
                        string relative = layer.clipSaveRoot.Length > 6 && layer.clipSaveRoot.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                            ? layer.clipSaveRoot.Substring(7)
                            : string.Empty;
                        string candidate = string.IsNullOrEmpty(relative) ? Application.dataPath : System.IO.Path.Combine(Application.dataPath, relative);
                        if (System.IO.Directory.Exists(candidate))
                        {
                            startPath = candidate;
                        }
                    }

                    string selected = EditorUtility.OpenFolderPanel("选择动画保存文件夹", startPath, "");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        if (selected.StartsWith(Application.dataPath, StringComparison.OrdinalIgnoreCase))
                        {
                            string relative = selected.Substring(Application.dataPath.Length).Replace('\\', '/');
                            layer.clipSaveRoot = SwitchGeneratorWindowOptions.NormalizeClipRootPath(string.IsNullOrEmpty(relative) ? "Assets" : $"Assets{relative}");
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("无效路径", "请选择位于 Assets 目录内的文件夹。", "确定");
                        }
                    }
                }
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
            DrawMenuSettings();
        }

        private void DrawMenuSettings()
        {
            var layer = GetSelectedLayer();
            if (layer == null)
            {
                return;
            }

            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.BeginHorizontal();
            layer.generateMenuControl = EditorGUILayout.Toggle(GUIContent.none, layer.generateMenuControl, GUILayout.Width(13));
            GUILayout.Label("菜单设置", EditorStyles.boldLabel);
            GUILayout.EndHorizontal();

            if (layer.generateMenuControl)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                if (_availableMenuPaths != null && _availableMenuPaths.Length > 0)
                {
                    int newIdx = EditorGUILayout.Popup("父菜单", _selectedMenuPathIndex, _availableMenuPaths);
                    if (newIdx != _selectedMenuPathIndex)
                    {
                        _selectedMenuPathIndex = newIdx;
                        layer.menuPath = SwitchGeneratorMenuPathOptions.Resolve(_availableMenuPaths, _selectedMenuPathIndex, layer.menuPath);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("未找到表情菜单或子菜单。", MessageType.Warning);
                }

                EditorGUILayout.Space();
                if (layer.switchType == SwitchGeneratorConfig.SwitchType.Bool)
                {
                    layer.boolMenuItemName = EditorGUILayout.TextField("菜单项名称", layer.boolMenuItemName);
                }
                else if (layer.switchType == SwitchGeneratorConfig.SwitchType.Int)
                {
                    layer.intSubMenuName = EditorGUILayout.TextField("子菜单名称", layer.intSubMenuName);
                    SwitchGeneratorLayerConfigEditing.EnsureIntMenuNameCapacity(layer);
                    EditorGUI.indentLevel++;
                    for (int g = 0; g < layer.intGroups.Count; g++)
                    {
                        string label = $"菜单项名称 (组 {g})";
                        if (g < layer.intMenuItemNames.Count)
                        {
                            layer.intMenuItemNames[g] = EditorGUILayout.TextField(label, layer.intMenuItemNames[g]);
                        }
                        else
                        {
                            layer.intMenuItemNames.Add(EditorGUILayout.TextField(label, string.Empty));
                        }
                    }

                    EditorGUI.indentLevel--;
                }
                else
                {
                    layer.floatMenuItemName = EditorGUILayout.TextField("菜单项名称", layer.floatMenuItemName);
                }

                EditorGUILayout.Space();
                GUILayout.Label("参数设置", EditorStyles.boldLabel);
                layer.savedParameter = EditorGUILayout.Toggle("参数保存", layer.savedParameter);
                layer.syncedParameter = EditorGUILayout.Toggle("参数同步", layer.syncedParameter);
                EditorGUILayout.EndVertical();
            }

            GUILayout.EndVertical();
        }


        private void RefreshAvatarContext()
        {
            var context = SwitchGeneratorWindowOptions.BuildAvatarContext(_avatar, GetSelectedLayer());
            _availableLayerNames = context.availableLayerNames ?? Array.Empty<string>();
            _availableParameterNames = context.availableParameterNames ?? Array.Empty<string>();
            _availableMenuPaths = context.availableMenuPaths ?? Array.Empty<string>();
            var selectionState = SwitchGeneratorWindowOptions.BuildSelectionState(
                GetSelectedLayer(),
                _availableLayerNames,
                _availableParameterNames,
                _availableMenuPaths);
            _selectedLayerPopupIndex = selectionState.layerIndex;
            _selectedParameterPopupIndex = selectionState.parameterIndex;
            _selectedMenuPathIndex = selectionState.menuPathIndex;
        }
    }
}
