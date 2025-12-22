using System;
using System.Collections.Generic;
using MVA.Toolbox.AnimFixUtility.Services;
using MVA.Toolbox.AnimFixUtility.Shared;
using MVA.Toolbox.Public;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.AnimFixUtility.Windows
{
    /// <summary>
    /// AnimFix - 查找动画面板（复刻原 Find Anim 功能）
    /// </summary>
    public class AnimFixFindWindow
    {
        private readonly AnimFixUtilityContext _context;

        private Vector2 _scroll;

        private readonly AnimFixFindService _service;

        public AnimFixFindWindow(AnimFixUtilityContext context)
        {
            _context = context;
            _service = new AnimFixFindService(context);
        }

        public void OnGUI()
        {
            _service.SyncContextChanges();

            _scroll = ToolboxUtils.ScrollView(_scroll, () =>
            {
                DrawAnimatedObjectSelector();

                GUILayout.Space(4f);

                DrawPropertyGroupSelector();

                GUILayout.Space(4f);

                DrawSearchResults();
            });
        }

        private void DrawAnimatedObjectSelector()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("被动画控制的物体", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var newObj = EditorGUILayout.ObjectField("目标物体", _service.SelectedAnimatedObject, typeof(Object), true);
            if (EditorGUI.EndChangeCheck())
            {
                _service.SetSelectedAnimatedObject(newObj);
            }

            if (!_service.HasAnimatedObject)
            {
                EditorGUILayout.HelpBox("请选择 Avatar 层级中的具体对象（GameObject 或组件）。", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawPropertyGroupSelector()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("属性", EditorStyles.boldLabel);

            if (!_service.HasAvailableGroups)
            {
                EditorGUILayout.HelpBox("请先在上方选择目标对象，窗口会自动扫描可用属性。", MessageType.Info);
            }
            else
            {
                var groups = _service.PropertyGroups;
                var displayNames = new string[groups.Count + 1];
                displayNames[0] = "全部属性";
                for (int i = 0; i < groups.Count; i++)
                {
                    displayNames[i + 1] = groups[i].GroupDisplayName;
                }

                int newIndex = EditorGUILayout.Popup("属性", _service.SelectedGroupIndex, displayNames);
                _service.ChangeGroupIndex(newIndex);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSearchResults()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("结果列表", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(!_service.CanRefresh());
            if (GUILayout.Button("刷新", GUILayout.Width(60f)))
            {
                _service.RefreshSearch();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (!_service.SearchCompleted)
            {
                EditorGUILayout.HelpBox("尚未执行搜索。", MessageType.Info);
            }
            else if (_service.FoundClips.Count == 0)
            {
                EditorGUILayout.HelpBox("未找到任何匹配该属性的动画剪辑。", MessageType.Info);
            }
            else
            {
                var clips = _service.FoundClips;
                for (int i = 0; i < clips.Count; i++)
                {
                    var info = clips[i];
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.ObjectField(info.Clip, typeof(AnimationClip), false);
                    EditorGUILayout.LabelField(info.Controller != null ? info.Controller.name : "(Controller)",
                        GUILayout.Width(150f));
                    if (GUILayout.Button("跳转", GUILayout.Width(60f)))
                    {
                        AnimJumpTool.TryJumpToClip(info.Clip);
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            DrawAqtConfigHints();

            EditorGUILayout.EndVertical();
        }

        private void DrawAqtConfigHints()
        {
            var hints = _service.BuildAqtConfigHints();
            if (hints.Count == 0)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Avatar Quick Toggle 配置", EditorStyles.boldLabel);
            for (int i = 0; i < hints.Count; i++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(hints[i]);
                EditorGUILayout.EndVertical();
            }
        }
    }
}
