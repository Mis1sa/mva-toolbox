using System;
using System.Collections.Generic;
using MVA.Toolbox.AnimFixUtility.Services;
using MVA.Toolbox.Public;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.AnimFixUtility.Windows
{
    /// <summary>
    /// AnimFix - 默认值烘焙窗口（复刻 Bake Default Anim）
    /// </summary>
    public class AnimFixBakeWindow
    {
        private readonly AnimFixUtilityContext _context;

        private string _suffixName = string.Empty;
        private string _saveFolderRelative = DefaultRootFolder;
        private bool _onlyGenerateClips;

        private Vector2 _scroll;

        private const string DefaultRootFolder = "Assets/MVA Toolbox/AFU";

        private readonly AnimFixBakeService _service;

        public AnimFixBakeWindow(AnimFixUtilityContext context)
        {
            _context = context;
            _service = new AnimFixBakeService(context);
        }

        public void OnGUI()
        {
            _scroll = ToolboxUtils.ScrollView(_scroll, () =>
            {
                if (!_service.TryResolveLayer(out var controller, out var layer, out var error))
                {
                    if (!string.IsNullOrEmpty(error))
                    {
                        EditorGUILayout.HelpBox(error, MessageType.Info);
                    }
                    return;
                }

                GUILayout.Space(4f);

                DrawOptionsAndAction(controller, layer);
            });
        }

        private void DrawOptionsAndAction(AnimatorController controller, AnimatorControllerLayer layer)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("烘焙设置", EditorStyles.boldLabel);

            _suffixName = EditorGUILayout.TextField("剪辑名称后缀 (可选)", _suffixName);

            EditorGUILayout.BeginHorizontal();
            _saveFolderRelative = EditorGUILayout.TextField("保存路径", _saveFolderRelative);
            if (GUILayout.Button("浏览", GUILayout.Width(60f), GUILayout.Height(EditorGUIUtility.singleLineHeight)))
            {
                string abs = EditorUtility.OpenFolderPanel("选择保存文件夹", Application.dataPath, string.Empty);
                if (!string.IsNullOrEmpty(abs))
                {
                    if (abs.StartsWith(Application.dataPath, StringComparison.OrdinalIgnoreCase))
                    {
                        string rel = "Assets" + abs.Substring(Application.dataPath.Length);
                        _saveFolderRelative = rel.Replace("\\", "/");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("路径错误", "请选择项目 Assets 目录下的文件夹。", "确定");
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("不替换动画剪辑", GUILayout.Width(EditorGUIUtility.labelWidth));
            _onlyGenerateClips = EditorGUILayout.Toggle(_onlyGenerateClips);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4f);

            if (GUILayout.Button("对选中层执行默认值烘焙", GUILayout.Height(28f)))
            {
                _service.ExecuteBake(controller, layer, _suffixName, _saveFolderRelative, _onlyGenerateClips, DefaultRootFolder);
            }

            EditorGUILayout.EndVertical();
        }
    }
}
