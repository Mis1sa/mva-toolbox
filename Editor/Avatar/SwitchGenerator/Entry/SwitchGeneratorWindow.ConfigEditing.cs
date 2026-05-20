using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.SwitchGenerator.Entry
{
    internal sealed partial class SwitchGeneratorWindow
    {
        private void DrawConfigScriptSection()
        {
            var cfgOnAvatar = _avatar != null ? _avatar.GetComponent<SwitchGeneratorConfig>() : null;
            if (cfgOnAvatar != _config)
            {
                _config = cfgOnAvatar;
                LoadFromConfig(_config);
            }

            EditorGUILayout.Space();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            bool hasConfig = _config != null;
            using (new EditorGUI.DisabledScope(!hasConfig))
            {
                bool prevUseConfig = _useConfigScript;
                bool newUseConfig = EditorGUILayout.ToggleLeft("配置脚本", _useConfigScript);
                if (hasConfig)
                {
                    _useConfigScript = newUseConfig;
                }
                else
                {
                    _useConfigScript = false;
                }

                if (hasConfig && prevUseConfig && !_useConfigScript)
                {
                    _selectedConfigIndex = -1;
                    ExitEditFromConfig();
                }
            }

            if (!hasConfig)
            {
                EditorGUILayout.HelpBox("当前Avatar没有SwitchGenerator配置", MessageType.Info);
            }
            else if (_useConfigScript)
            {
                DrawConfigEntrySelector();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawConfigEntrySelector()
        {
            _avatarMismatch = _config != null && _config.targetAvatar != null && _avatar != null && _config.targetAvatar != _avatar;
            if (_availableConfigEntries.Count == 0 && !_avatarMismatch)
            {
                EditorGUILayout.HelpBox("该 SwitchGeneratorConfig 暂无项目。请在生成时创建。", MessageType.Info);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("NDMF 项目选择", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(_avatarMismatch))
            {
                int newIndex = EditorGUILayout.Popup("选择项目", _selectedConfigIndex, _configEntryOptions);
                if (newIndex != _selectedConfigIndex)
                {
                    _selectedConfigIndex = newIndex;
                    if (_selectedConfigIndex >= 0 && _selectedConfigIndex < _availableConfigEntries.Count)
                    {
                        var entry = _availableConfigEntries[_selectedConfigIndex];
                        if (string.IsNullOrEmpty(entry.layerName) || string.IsNullOrEmpty(entry.parameterName))
                        {
                            ExitEditFromConfig();
                            EditorGUILayout.HelpBox("所选项目未完整配置（缺少层级名称或参数名称），请先在生成界面补全后再编辑。", MessageType.Warning);
                        }
                        else
                        {
                            EnterEditFromConfig(entry);
                        }
                    }
                }
            }

            if (_avatarMismatch)
            {
                EditorGUILayout.HelpBox("该 SwitchGeneratorConfig 的 Avatar 与当前窗口不一致，请拖入对应 Avatar 或该 Avatar 的 SwitchGeneratorConfig。", MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawModeAndConfigNameRow()
        {
            GUILayout.Space(2f);
            var layer = GetSelectedLayer();
            if (layer == null)
            {
                return;
            }

            if (!_isEditingConfigEntry)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                _useNDMFMode = EditorGUILayout.ToggleLeft("NDMF模式", _useNDMFMode);

                if (_useNDMFMode)
                {
                    layer.displayName = EditorGUILayout.TextField("配置名称", layer.displayName);
                }

                EditorGUILayout.EndVertical();
            }
            else
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                layer.displayName = EditorGUILayout.TextField("配置名称", layer.displayName);
                EditorGUILayout.EndVertical();
            }
        }

        private void LoadFromConfig(SwitchGeneratorConfig config)
        {
            _availableConfigEntries.Clear();
            _configEntryOptions = Array.Empty<string>();
            _selectedConfigIndex = -1;
            _isEditingConfigEntry = false;
            _editingConfigEntry = null;

            if (config == null) return;
            foreach (var e in config.layers)
            {
                if (e == null) continue;
                if (config.targetAvatar != null && _avatar != null && config.targetAvatar != _avatar)
                {
                    continue;
                }

                _availableConfigEntries.Add(e);
            }

            _availableConfigEntries.Sort((a, b) =>
            {
                int c1 = string.Compare(a.layerName, b.layerName, StringComparison.Ordinal);
                if (c1 != 0) return c1;
                return a.switchType.CompareTo(b.switchType);
            });

            var opts = new List<string>();
            foreach (var e in _availableConfigEntries)
            {
                string text;
                if (!string.IsNullOrEmpty(e.displayName)) text = e.displayName;
                else if (!string.IsNullOrEmpty(e.layerName)) text = e.layerName;
                else text = "(未命名层)";
                opts.Add(text);
            }

            _configEntryOptions = opts.ToArray();
        }

        private void EnterEditFromConfig(SwitchGeneratorConfig.LayerConfig entry)
        {
            if (entry == null) return;
            _isEditingConfigEntry = true;
            _editingConfigEntry = entry;

            _draftLayer = SwitchGeneratorLayerConfigEditing.ClonePreparedLayerConfig(entry);
            RefreshAvatarContext();

            _selectedParameterPopupIndex = SwitchGeneratorWindowOptions.EnsureParameterSelection(
                _draftLayer,
                _availableParameterNames,
                _selectedParameterPopupIndex);
        }

        private void ExitEditFromConfig()
        {
            _isEditingConfigEntry = false;
            _editingConfigEntry = null;
        }

        private void ResetDraftLayer()
        {
            _draftLayer = SwitchGeneratorLayerConfigEditing.CreatePreparedLayerConfig();
        }
    }
}
