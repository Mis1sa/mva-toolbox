using System.Collections.Generic;
using MVA.Toolbox.SwitchGenerator.Spec;
using MVA.Toolbox.SwitchGenerator.Workflows;
using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.SwitchGenerator.Entry
{
    internal sealed partial class SwitchGeneratorWindow
    {
        private void ApplyPreviewStateBool(bool on)
        {
            var layer = GetSelectedLayer();
            if (layer == null) return;
            _previewSession.Apply(BuildSelectedLayerSpec(), on ? 1f : 0f);
        }

        private void ApplyPreviewStateInt(int value)
        {
            var layer = GetSelectedLayer();
            if (layer == null || layer.intGroups.Count == 0) return;
            _previewSession.Apply(BuildSelectedLayerSpec(), Mathf.Clamp(value, 0, layer.intGroups.Count - 1));
        }

        private void ApplyPreviewStateFloat(float t)
        {
            _previewSession.Apply(BuildSelectedLayerSpec(), Mathf.Clamp01(t));
        }

        private void DrawActions()
        {
            var layer = GetSelectedLayer();
            bool valid = layer != null && !string.IsNullOrEmpty(layer.layerName) && !string.IsNullOrEmpty(layer.parameterName);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (_isEditingConfigEntry)
                {
                    using (new EditorGUI.DisabledScope(!valid))
                    {
                        if (GUILayout.Button("保存修改", GUILayout.Height(30), GUILayout.ExpandWidth(true)))
                        {
                            OnModifyButtonClick();
                        }

                        if (GUILayout.Button("直接修改", GUILayout.Height(30), GUILayout.ExpandWidth(true)))
                        {
                            OnApplyButtonClick();
                        }
                    }
                }
                else
                {
                    using (new EditorGUI.DisabledScope(!valid))
                    {
                        string mainLabel = _useNDMFMode ? "生成脚本" : "应用修改";
                        if (GUILayout.Button(mainLabel, GUILayout.Height(30), GUILayout.ExpandWidth(true)))
                        {
                            if (_useNDMFMode)
                            {
                                OnGenerateButtonClick();
                            }
                            else
                            {
                                OnApplyButtonClick();
                            }
                        }
                    }
                }
            }
        }

        private void DrawPreviewBlock()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            Rect lineRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            const float buttonWidth = 80f;

            if (!_previewSession.IsPreviewing)
            {
                if (GUI.Button(lineRect, "开启预览"))
                {
                    StartPreview();
                }
            }
            else
            {
                Rect buttonRect = new Rect(lineRect.x, lineRect.y, buttonWidth, lineRect.height);
                Rect sliderRect = new Rect(lineRect.x + buttonWidth + 4f, lineRect.y,
                    Mathf.Max(0f, lineRect.width - buttonWidth - 4f), lineRect.height);

                if (GUI.Button(buttonRect, "退出预览"))
                {
                    StopPreview();
                }
                else
                {
                    var layer = GetSelectedLayer();
                    float prevLabelWidth = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = 50f;
                    if (layer.switchType == SwitchGeneratorConfig.SwitchType.Bool)
                    {
                        int newVal = EditorGUI.IntSlider(sliderRect, new GUIContent("预览值"), Mathf.RoundToInt(_previewValue), 0, 1);
                        if (newVal != Mathf.RoundToInt(_previewValue))
                        {
                            _previewValue = newVal;
                        }

                        ApplyPreviewStateBool(_previewValue >= 0.5f);
                    }
                    else if (layer.switchType == SwitchGeneratorConfig.SwitchType.Int)
                    {
                        int maxIndex = Mathf.Max(0, layer.intGroups.Count - 1);
                        int newVal = EditorGUI.IntSlider(sliderRect, new GUIContent("预览值"), Mathf.RoundToInt(_previewValue), 0, maxIndex);
                        if (newVal != Mathf.RoundToInt(_previewValue))
                        {
                            _previewValue = newVal;
                        }

                        ApplyPreviewStateInt(Mathf.RoundToInt(_previewValue));
                    }
                    else
                    {
                        float newVal = EditorGUI.Slider(sliderRect, new GUIContent("预览值"), _previewValue, 0f, 1f);
                        if (!Mathf.Approximately(newVal, _previewValue))
                        {
                            _previewValue = newVal;
                        }

                        ApplyPreviewStateFloat(_previewValue);
                    }

                    EditorGUIUtility.labelWidth = prevLabelWidth;
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void StartPreview()
        {
            if (_previewSession.IsPreviewing) return;
            var root = _avatar ? _avatar.gameObject : (_config ? _config.gameObject : null);
            if (root == null)
            {
                ShowNotification(new GUIContent("请先拖入 Avatar"));
                return;
            }

            _previewSession.Start(root);
            ApplyCurrentPreviewValues();
        }

        private void StopPreview()
        {
            if (!_previewSession.IsPreviewing) return;
            _previewSession.Stop();
        }

        private void ApplyCurrentPreviewValues()
        {
            if (!_previewSession.IsPreviewing) return;
            var layer = GetSelectedLayer();
            if (layer == null) return;
            if (layer.switchType == SwitchGeneratorConfig.SwitchType.Bool)
            {
                ApplyPreviewStateBool(_previewValue >= 0.5f);
            }
            else if (layer.switchType == SwitchGeneratorConfig.SwitchType.Int)
            {
                ApplyPreviewStateInt(Mathf.RoundToInt(_previewValue));
            }
            else
            {
                ApplyPreviewStateFloat(_previewValue);
            }
        }

        private SwitchLayerSpec BuildSelectedLayerSpec()
        {
            var layer = GetSelectedLayer();
            if (layer == null) return null;
            var spec = SwitchGeneratorSpecFactory.FromLayer(_avatar, layer);
            SwitchGeneratorSpecNormalizer.Normalize(spec);
            return spec.layers.Count > 0 ? spec.layers[0] : null;
        }

        private void OnApplyButtonClick()
        {
            var layer = GetSelectedLayer();
            if (layer == null) return;
            var layerSpec = BuildSelectedLayerSpec();
            if (layerSpec == null) return;

            var spec = new SwitchGeneratorSpec
            {
                avatar = _avatar,
                layers = new List<SwitchLayerSpec> { layerSpec }
            };

            bool applySucceeded = ApplyToAvatarWorkflow.Execute(spec, out string message);

            if (applySucceeded && _isEditingConfigEntry && _config != null && _editingConfigEntry != null)
            {
                _config.layers.Remove(_editingConfigEntry);
                ExitEditFromConfig();
                LoadFromConfig(_config);
                ShowNotification(new GUIContent("已应用并从配置脚本中移除"));
            }
            else if (!applySucceeded)
            {
                ShowNotification(new GUIContent("应用失败，请查看控制台日志"));
            }
        }

        private void OnGenerateButtonClick()
        {
            if (_avatar == null)
            {
                ShowNotification(new GUIContent("请先拖入 Avatar"));
                return;
            }

            if (_config == null)
            {
                _config = _avatar.GetComponent<SwitchGeneratorConfig>();
                if (_config == null)
                {
                    _config = _avatar.gameObject.AddComponent<SwitchGeneratorConfig>();
                }
            }

            var layer = GetSelectedLayer();
            if (layer == null) return;

            if (string.IsNullOrEmpty(layer.displayName))
            {
                layer.displayName = SwitchGeneratorLayerConfigEditing.BuildDefaultDisplayName(_config, layer);
            }

            SwitchGeneratorLayerConfigEditing.PrepareLayerConfig(layer);

            if (!_isEditingConfigEntry)
            {
                var newLayer = SwitchGeneratorLayerConfigEditing.CloneLayerConfig(layer);
                _config.layers.Add(newLayer);
            }

            LoadFromConfig(_config);
            ShowNotification(new GUIContent("已保存到 SwitchGeneratorConfig"));
        }

        private void OnModifyButtonClick()
        {
            if (!_isEditingConfigEntry || _config == null) return;
            var layer = GetSelectedLayer();
            if (layer == null) return;

            SwitchGeneratorLayerConfigEditing.PrepareLayerConfig(layer);

            if (_editingConfigEntry == null)
            {
                return;
            }

            int index = _config.layers.IndexOf(_editingConfigEntry);
            if (index < 0)
            {
                return;
            }

            var updatedEntry = SwitchGeneratorLayerConfigEditing.CloneLayerConfig(layer);
            _config.layers[index] = updatedEntry;
            LoadFromConfig(_config);
            _selectedConfigIndex = _availableConfigEntries.IndexOf(updatedEntry);
            EnterEditFromConfig(updatedEntry);
            ShowNotification(new GUIContent("已更新配置项"));
        }
    }
}
