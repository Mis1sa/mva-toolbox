using UnityEditor;
using UnityEngine;
using MVA.Toolbox.MaterialRefit.Services;

namespace MVA.Toolbox.MaterialRefit.UI
{
    /// <summary>
    /// Material Refit 主窗口：提供材质 / 贴图批量替换的界面，具体逻辑由 MaterialRefitService 实现。
    /// </summary>
    public sealed class MaterialRefitWindow : EditorWindow
    {
        enum ToolMode
        {
            Material,
            Texture
        }

        GameObject _targetObject;
        ToolMode _mode = ToolMode.Material;
        Vector2 _scrollPos;

        readonly GUIContent _targetLabel = new GUIContent("目标物体", "将场景或项目中的 GameObject 拖入到此处，工具会收集其及全部子对象中使用的材质 / 贴图。");

        MaterialRefitService _service;

        [MenuItem("Tools/MVA Toolbox/Material Refit", false, 2)]
        public static void Open()
        {
            var w = GetWindow<MaterialRefitWindow>("Material Refit");
            w.minSize = new Vector2(500, 300);
        }

        void OnEnable()
        {
            if (_service == null)
            {
                _service = new MaterialRefitService();
            }

            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;

            if (_service != null)
            {
                _service.OnWindowDisabled();
            }
        }

        void OnUndoRedoPerformed()
        {
            Repaint();
        }

        void OnGUI()
        {
            if (_service == null)
            {
                _service = new MaterialRefitService();
            }

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawTargetSelection();

            GUILayout.Space(4f);

            if (_targetObject == null)
            {
                EditorGUILayout.HelpBox("将场景或项目中的 GameObject 拖入到 '目标物体'，工具会收集其及全部子对象中使用的材质 / 贴图。关闭窗口且未点击 应用 前将撤销预览更改。", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawModeSelection();

            GUILayout.Space(6f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (_mode == ToolMode.Material)
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

        void DrawTargetSelection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("目标对象", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var newTarget = (GameObject)EditorGUILayout.ObjectField(_targetLabel, _targetObject, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                _targetObject = newTarget;
                _service.SetTarget(_targetObject);
            }

            EditorGUILayout.EndVertical();
        }

        void DrawModeSelection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("模式", EditorStyles.boldLabel);
            var labels = new[] { "材质 (Material)", "贴图 (Texture)" };
            var newMode = (ToolMode)GUILayout.Toolbar((int)_mode, labels);

            if (newMode != _mode)
            {
                _mode = newMode;
                _service.SetMode(_mode == ToolMode.Material ? MaterialRefitService.Mode.Material : MaterialRefitService.Mode.Texture);
            }

            EditorGUILayout.EndVertical();
        }

        void DrawMaterialMode()
        {
            var materials = _service.FoundMaterials;
            if (materials.Count == 0)
            {
                EditorGUILayout.HelpBox("未检测到任何材质。", MessageType.Info);
            }

            EditorGUILayout.LabelField($"检测到材质: {materials.Count}", EditorStyles.boldLabel);

            foreach (var src in materials)
            {
                if (src == null)
                {
                    continue;
                }

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField(src, typeof(Material), false);

                _service.MaterialReplacements.TryGetValue(src, out var newMat);
                EditorGUI.BeginChangeCheck();
                newMat = (Material)EditorGUILayout.ObjectField(newMat, typeof(Material), false, GUILayout.Width(200f));
                if (EditorGUI.EndChangeCheck())
                {
                    _service.UpdateMaterialReplacement(src, newMat);
                }

                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(8f);

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = _service.HasPreviewChanges;
            if (GUILayout.Button("应用 (Apply)", GUILayout.Height(26f)))
            {
                _service.ApplyChanges();
            }

            GUI.enabled = true;
            if (GUILayout.Button("切换显示 (Toggle Display)", GUILayout.Height(26f)))
            {
                _service.ToggleDisplay();
            }

            EditorGUILayout.EndHorizontal();
        }

        void DrawTextureMode()
        {
            EditorGUILayout.BeginHorizontal();
            _service.ExtraCreateMaterials = EditorGUILayout.ToggleLeft("额外创建材质 (复制并保存修改后的材质)", _service.ExtraCreateMaterials);
            EditorGUILayout.EndHorizontal();

            if (_service.ExtraCreateMaterials)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("保存路径", GUILayout.Width(60f));
                _service.SaveFolderRelative = EditorGUILayout.TextField(_service.SaveFolderRelative);
                if (GUILayout.Button("浏览", GUILayout.Width(60f)))
                {
                    string abs = EditorUtility.OpenFolderPanel("选择保存文件夹 (请选 Assets 下文件夹或在 Assets 下新建)", Application.dataPath, "");
                    if (!string.IsNullOrEmpty(abs))
                    {
                        if (abs.StartsWith(Application.dataPath))
                        {
                            string rel = "Assets" + abs.Substring(Application.dataPath.Length);
                            _service.SaveFolderRelative = rel.Replace("\\", "/") + "/";
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("路径错误", "请选择项目中的 Assets 目录下的文件夹以便保存材质。", "确定");
                        }
                    }
                }

                EditorGUILayout.EndHorizontal();
            }

            var textures = _service.FoundTextures;
            if (textures.Count == 0)
            {
                EditorGUILayout.HelpBox("未检测到任何贴图。", MessageType.Info);
            }

            EditorGUILayout.LabelField($"检测到贴图: {textures.Count}", EditorStyles.boldLabel);

            foreach (var src in textures)
            {
                if (src == null)
                {
                    continue;
                }

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField(src, typeof(Texture), false);

                _service.TextureReplacements.TryGetValue(src, out var newTex);
                EditorGUI.BeginChangeCheck();
                newTex = (Texture)EditorGUILayout.ObjectField(newTex, typeof(Texture), false, GUILayout.Width(200f));
                if (EditorGUI.EndChangeCheck())
                {
                    _service.UpdateTextureReplacement(src, newTex);
                }

                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(8f);

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = _service.HasPreviewChanges;
            if (GUILayout.Button("应用 (Apply)", GUILayout.Height(26f)))
            {
                _service.ApplyChanges();
            }

            GUI.enabled = true;
            if (GUILayout.Button("切换显示 (Toggle Display)", GUILayout.Height(26f)))
            {
                _service.ToggleDisplay();
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}
