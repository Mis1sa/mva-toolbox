using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.FindReferencesTool
{
    internal sealed class FindReferencesWindow : EditorWindow
    {
        private Vector2 _scrollPosition;
        private FindReferencesController _controller;

        internal static void Open(Object targetAsset)
        {
            FindReferencesWindow window = GetWindow<FindReferencesWindow>(false, "引用查询");
            window.minSize = new Vector2(520f, 460f);
            window.EnsureController();
            window._controller.Initialize(targetAsset);
            window.Show();
        }

        internal static void Open()
        {
            Open(null);
        }

        private void OnEnable()
        {
            EnsureController();
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneClosed += OnSceneClosed;
            EditorSceneManager.newSceneCreated += OnNewSceneCreated;
        }

        private void OnDisable()
        {
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneClosed -= OnSceneClosed;
            EditorSceneManager.newSceneCreated -= OnNewSceneCreated;
        }

        private void OnDestroy()
        {
            _controller?.ClearTarget();
        }

        private void OnNewSceneCreated(Scene scene, NewSceneSetup setup, NewSceneMode mode)
        {
            _controller?.MarkSceneFiltersDirty();
            Repaint();
        }

        private void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            _controller?.MarkSceneFiltersDirty();
            Repaint();
        }

        private void OnSceneClosed(Scene scene)
        {
            _controller?.MarkSceneFiltersDirty();
            Repaint();
        }

        private void OnGUI()
        {
            EnsureController();
            _controller.UpdateSceneFiltersIfNeeded();
            _controller.ValidateSceneLimitTransform();

            DrawHeader();
            GUILayout.Space(8f);
            DrawScopeControls();
            GUILayout.Space(8f);

            if (_controller.IsSearching)
            {
                DrawProgress();
                return;
            }

            DrawResultArea();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUI.BeginChangeCheck();
            Object newTarget = EditorGUILayout.ObjectField("目标资产", _controller.TargetAsset, typeof(Object), false);
            if (EditorGUI.EndChangeCheck())
            {
                _controller.SetTarget(newTarget);
            }

            if (!_controller.CanSearchWithCurrentTarget())
            {
                EditorGUILayout.HelpBox("请选择一个要查询引用的目标资产。", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawScopeControls()
        {
            _controller.ValidateSearchState();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("搜索范围", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(250f));
            _controller.SearchAssets = EditorGUILayout.ToggleLeft("资产", _controller.SearchAssets);
            using (new EditorGUI.DisabledScope(!_controller.SearchAssets))
            {
                EditorGUI.indentLevel++;
                _controller.AssetPathAssets = EditorGUILayout.ToggleLeft("Assets", _controller.AssetPathAssets);
                _controller.AssetPathPackages = EditorGUILayout.ToggleLeft("Packages", _controller.AssetPathPackages);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _controller.SearchScenes = EditorGUILayout.ToggleLeft("场景", _controller.SearchScenes);
            using (new EditorGUI.DisabledScope(!_controller.SearchScenes))
            {
                EditorGUI.indentLevel++;
                DrawSceneFilterList();
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            using (new EditorGUI.DisabledScope(!_controller.SearchScenes))
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("场景物体限制", EditorStyles.label);
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                Transform newLimit = EditorGUILayout.ObjectField("物体", _controller.SceneLimitTransform, typeof(Transform), true) as Transform;
                if (EditorGUI.EndChangeCheck())
                {
                    if (newLimit != null && !newLimit.gameObject.scene.isLoaded)
                    {
                        EditorUtility.DisplayDialog("无效物体", "请拖入当前已加载场景中的物体。", "确定");
                        newLimit = null;
                    }

                    _controller.SceneLimitTransform = newLimit;
                }

                using (new EditorGUI.DisabledScope(_controller.SceneLimitTransform == null))
                {
                    if (GUILayout.Button("清除", GUILayout.Width(50f)))
                    {
                        _controller.SceneLimitTransform = null;
                    }
                }

                EditorGUILayout.EndHorizontal();

                if (_controller.SceneLimitTransform != null)
                {
                    EditorGUILayout.HelpBox("已限制搜索范围至该物体及其子物体，场景勾选项暂时失效。", MessageType.Info);
                }
            }

            EditorGUILayout.EndVertical();

            GUILayout.Space(4f);
            using (new EditorGUI.DisabledScope(!_controller.CanSearchWithCurrentTarget()))
            {
                if (GUILayout.Button("搜索", GUILayout.Height(26f)))
                {
                    _controller.PerformSearch();
                }
            }
        }

        private void DrawSceneFilterList()
        {
            List<FindReferencesSceneFilterInfo> filters = _controller.SceneFilters;
            if (filters.Count == 0)
            {
                EditorGUILayout.HelpBox("当前没有场景。", MessageType.Info);
                return;
            }

            for (int index = 0; index < filters.Count; index++)
            {
                FindReferencesSceneFilterInfo filter = filters[index];
                if (!filter.Scene.IsValid())
                {
                    continue;
                }

                string sceneName = string.IsNullOrEmpty(filter.Scene.name) ? "Untitled" : filter.Scene.name;
                using (new EditorGUI.DisabledScope(!filter.IsLoaded || _controller.SceneLimitTransform != null))
                {
                    filter.IsIncluded = EditorGUILayout.ToggleLeft(sceneName, filter.IsIncluded);
                }
            }
        }

        private void DrawProgress()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("搜索中...", EditorStyles.boldLabel);
            Rect rect = EditorGUILayout.GetControlRect(false, 18f);
            EditorGUI.ProgressBar(rect, _controller.SearchProgress, $"{_controller.SearchProgress * 100f:F0}%");
            if (!string.IsNullOrEmpty(_controller.ProgressLabel))
            {
                GUILayout.Space(4f);
                EditorGUILayout.LabelField(_controller.ProgressLabel, EditorStyles.miniLabel);
            }

            GUILayout.Space(6f);
            using (new EditorGUI.DisabledScope(!_controller.IsSearching))
            {
                if (GUILayout.Button("中止搜索", GUILayout.Height(24f)))
                {
                    _controller.CancelSearch();
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawResultArea()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            DrawResultGroup("资产引用", _controller.AssetResults, FindReferencesSourceType.Asset);
            GUILayout.Space(6f);
            DrawResultGroup("场景引用", _controller.SceneResults, FindReferencesSourceType.Scene);
            EditorGUILayout.EndScrollView();
        }

        private void DrawResultGroup(string title, IReadOnlyList<FindReferencesEntry> entries, FindReferencesSourceType sourceType)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"{title} ({entries.Count})", EditorStyles.boldLabel);

            if (entries.Count == 0)
            {
                EditorGUILayout.HelpBox("未找到任何结果。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            for (int index = 0; index < entries.Count; index++)
            {
                FindReferencesEntry entry = entries[index];
                if (entry.ContainerObject == null)
                {
                    continue;
                }

                DrawReferenceEntry(entry, sourceType);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawReferenceEntry(FindReferencesEntry entry, FindReferencesSourceType sourceType)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            Rect foldoutRect = GUILayoutUtility.GetRect(14f, EditorGUIUtility.singleLineHeight, GUILayout.Width(14f));
            entry.Expanded = EditorGUI.Foldout(foldoutRect, entry.Expanded, GUIContent.none, true);
            EditorGUILayout.ObjectField(entry.ContainerObject, typeof(Object), false, GUILayout.ExpandWidth(true));
            if (_controller.CanUseAsSearchTarget(entry.ContainerObject))
            {
                if (GUILayout.Button("作为“目标资产”进行搜索", GUILayout.Width(160f)))
                {
                    _controller.SearchUsingTarget(entry.ContainerObject);
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (entry.Expanded && entry.Locations.Count > 0)
            {
                EditorGUI.indentLevel++;
                DrawReferenceLocations(entry.Locations, sourceType);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(4f);
        }

        private void DrawReferenceLocations(List<FindReferencesLocation> locations, FindReferencesSourceType sourceType)
        {
            Dictionary<Object, List<FindReferencesLocation>> groupedLocations = new Dictionary<Object, List<FindReferencesLocation>>();
            for (int index = 0; index < locations.Count; index++)
            {
                FindReferencesLocation location = locations[index];
                Object directReference = location.DirectReferenceObject != null ? location.DirectReferenceObject : location.SourceObject;
                if (directReference == null)
                {
                    directReference = location.MatchedContainer;
                }

                if (!groupedLocations.TryGetValue(directReference, out List<FindReferencesLocation> list))
                {
                    list = new List<FindReferencesLocation>();
                    groupedLocations.Add(directReference, list);
                }

                list.Add(location);
            }

            foreach (KeyValuePair<Object, List<FindReferencesLocation>> pair in groupedLocations)
            {
                Object directReference = pair.Key;
                List<FindReferencesLocation> pathList = pair.Value;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("直接引用", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField(directReference, typeof(Object), false, GUILayout.ExpandWidth(true));
                if (_controller.CanUseAsSearchTarget(directReference))
                {
                    if (GUILayout.Button("作为“目标资产”进行搜索", GUILayout.Width(160f)))
                    {
                        _controller.SearchUsingTarget(directReference);
                        GUIUtility.ExitGUI();
                    }
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField("引用路径", EditorStyles.boldLabel);
                for (int pathIndex = 0; pathIndex < pathList.Count; pathIndex++)
                {
                    FindReferencesLocation location = pathList[pathIndex];
                    string pathDisplay = BuildPathDisplay(location, directReference, sourceType);
                    float height = Mathf.Max(20f, EditorStyles.textField.CalcHeight(new GUIContent(pathDisplay), EditorGUIUtility.currentViewWidth - 80f));
                    EditorGUILayout.SelectableLabel(pathDisplay, EditorStyles.textField, GUILayout.Height(height));
                }

                EditorGUILayout.EndVertical();
            }
        }

        private static string BuildPathDisplay(FindReferencesLocation location, Object directReference, FindReferencesSourceType sourceType)
        {
            if (directReference is UnityEditor.Animations.AnimatorController)
            {
                return $"{directReference.name}.controller [{location.PropertyPath}]";
            }

            if (sourceType == FindReferencesSourceType.Scene)
            {
                if (location.PropertyPath == "Prefab实例")
                {
                    return $"{(string.IsNullOrEmpty(location.SceneName) ? "Untitled" : location.SceneName)} > {location.SceneHierarchy} > Prefab实例";
                }

                return $"{(string.IsNullOrEmpty(location.SceneName) ? "Untitled" : location.SceneName)} > {location.SceneHierarchy}" +
                       (string.IsNullOrEmpty(location.PropertyPath) ? string.Empty : $" > {location.PropertyPath}");
            }

            return $"{location.ContainerPath}" +
                   (string.IsNullOrEmpty(location.PropertyPath) ? string.Empty : $" > {location.PropertyPath}");
        }

        private void EnsureController()
        {
            _controller ??= new FindReferencesController();
        }
    }
}
