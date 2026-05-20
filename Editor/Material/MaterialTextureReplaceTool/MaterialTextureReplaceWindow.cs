using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.MaterialTextureReplaceTool
{
    internal sealed class MaterialTextureReplaceWindow : EditorWindow
    {
        private readonly GUIContent _targetLabel = new GUIContent(
            "目标物体",
            "将场景中的 GameObject 拖入到此处，工具会收集其及全部子对象中使用的材质 / 贴图。");

        private Vector2 _scrollPosition;
        private MaterialTextureReplaceController _controller;

        internal static void Open()
        {
            MaterialTextureReplaceWindow window = GetWindow<MaterialTextureReplaceWindow>(false, "材质纹理替换");
            window.minSize = new Vector2(500f, 300f);
            window.Show();
        }

        private void OnEnable()
        {
            _controller ??= new MaterialTextureReplaceController();
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            _controller?.OnWindowDisabled();
        }

        private void OnUndoRedoPerformed()
        {
            Repaint();
        }

        private void OnGUI()
        {
            _controller ??= new MaterialTextureReplaceController();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            DrawTargetSelection();

            GUILayout.Space(4f);

            if (_controller.TargetObject == null)
            {
                EditorGUILayout.HelpBox("请拖入场景中需要替换材质/纹理的物体。", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawModeSelection();

            GUILayout.Space(6f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if (_controller.Mode == MaterialTextureReplaceMode.Material)
            {
                DrawMaterialMode();
            }
            else
            {
                DrawTextureMode();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }

        private void DrawTargetSelection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUI.BeginChangeCheck();
            GameObject newTarget = (GameObject)EditorGUILayout.ObjectField(_targetLabel, _controller.TargetObject, typeof(GameObject), true, GUILayout.ExpandWidth(true));
            if (EditorGUI.EndChangeCheck())
            {
                if (newTarget != null && !newTarget.scene.IsValid())
                {
                    EditorUtility.DisplayDialog("目标物体", "仅支持拖入场景中的物体，不接受项目资源中的预制体或其他资产对象。", "确定");
                    newTarget = null;
                }

                _controller.SetTarget(newTarget);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawModeSelection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("模式", EditorStyles.boldLabel);
            string[] labels = { "材质 (Material)", "贴图 (Texture)" };
            MaterialTextureReplaceMode newMode = (MaterialTextureReplaceMode)GUILayout.Toolbar((int)_controller.Mode, labels);
            if (newMode != _controller.Mode)
            {
                _controller.SetMode(newMode);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawMaterialMode()
        {
            IReadOnlyList<Material> materials = _controller.FoundMaterials;
            if (materials.Count == 0)
            {
                EditorGUILayout.HelpBox("未检测到任何材质。", MessageType.Info);
            }

            EditorGUILayout.LabelField($"检测到材质: {materials.Count}", EditorStyles.boldLabel);
            for (int index = 0; index < materials.Count; index++)
            {
                Material sourceMaterial = materials[index];
                if (sourceMaterial == null)
                {
                    continue;
                }

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField(sourceMaterial, typeof(Material), false);

                _controller.MaterialReplacements.TryGetValue(sourceMaterial, out Material replacementMaterial);
                EditorGUI.BeginChangeCheck();
                replacementMaterial = (Material)EditorGUILayout.ObjectField(replacementMaterial, typeof(Material), false, GUILayout.Width(200f));
                if (EditorGUI.EndChangeCheck())
                {
                    _controller.UpdateMaterialReplacement(sourceMaterial, replacementMaterial);
                }

                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(8f);
            DrawActionButtons();
        }

        private void DrawTextureMode()
        {
            EditorGUILayout.BeginHorizontal();
            _controller.ExtraCreateMaterials = EditorGUILayout.ToggleLeft("额外创建材质 (复制并保存修改后的材质)", _controller.ExtraCreateMaterials);
            EditorGUILayout.EndHorizontal();

            if (_controller.ExtraCreateMaterials)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("保存路径", GUILayout.Width(60f));
                _controller.SaveFolderRelative = EditorGUILayout.TextField(_controller.SaveFolderRelative);
                if (GUILayout.Button("浏览", GUILayout.Width(60f)))
                {
                    string absolutePath = EditorUtility.OpenFolderPanel("选择保存文件夹 (请选 Assets 下文件夹或在 Assets 下新建)", Application.dataPath, string.Empty);
                    if (!string.IsNullOrEmpty(absolutePath))
                    {
                        if (absolutePath.StartsWith(Application.dataPath))
                        {
                            string relativePath = "Assets" + absolutePath.Substring(Application.dataPath.Length);
                            _controller.SaveFolderRelative = relativePath.Replace("\\", "/") + "/";
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("路径错误", "请选择项目中的 Assets 目录下的文件夹以便保存材质。", "确定");
                        }
                    }
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("名称后缀", GUILayout.Width(60f));
                _controller.MaterialSuffix = EditorGUILayout.TextField(_controller.MaterialSuffix ?? string.Empty);
                EditorGUILayout.EndHorizontal();
            }

            IReadOnlyList<Texture> textures = _controller.FoundTextures;
            if (textures.Count == 0)
            {
                EditorGUILayout.HelpBox("未检测到任何贴图。", MessageType.Info);
            }

            EditorGUILayout.LabelField($"检测到贴图: {textures.Count}", EditorStyles.boldLabel);
            for (int index = 0; index < textures.Count; index++)
            {
                Texture sourceTexture = textures[index];
                if (sourceTexture == null)
                {
                    continue;
                }

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField(sourceTexture, typeof(Texture), false);

                _controller.TextureReplacements.TryGetValue(sourceTexture, out Texture replacementTexture);
                EditorGUI.BeginChangeCheck();
                replacementTexture = (Texture)EditorGUILayout.ObjectField(replacementTexture, typeof(Texture), false, GUILayout.Width(200f));
                if (EditorGUI.EndChangeCheck())
                {
                    _controller.UpdateTextureReplacement(sourceTexture, replacementTexture);
                }

                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(8f);
            DrawActionButtons();
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = _controller.HasPreviewChanges;
            if (GUILayout.Button("应用 (Apply)", GUILayout.Height(26f)))
            {
                MaterialTextureReplaceApplyResult result = _controller.ApplyChanges();
                if (result != null && result.HasError)
                {
                    EditorUtility.DisplayDialog("路径错误", result.ErrorMessage, "确定");
                }
            }

            GUI.enabled = true;
            if (GUILayout.Button("切换显示 (Toggle Display)", GUILayout.Height(26f)))
            {
                if (!_controller.ToggleDisplay(out string errorMessage) && !string.IsNullOrEmpty(errorMessage))
                {
                    EditorUtility.DisplayDialog("切换显示", errorMessage, "确定");
                }
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}
