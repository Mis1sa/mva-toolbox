using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
using UnityEngine.SceneManagement;
using MVA.Toolbox.Public;

namespace MVA.Toolbox.Utilities.FindReferences
{
    public sealed class FindReferencesWindow : EditorWindow
    {
        private Object _targetAsset;
        private Vector2 _scrollPosition;
        private bool _searchAssets = true;
        private bool _searchScenes = true;
        private bool _assetPathAssets = true;
        private bool _assetPathPackages = false;
        private List<SceneFilterInfo> _sceneFilters = new List<SceneFilterInfo>();
        private Transform _sceneLimitTransform;
        private bool _isSearching;
        private float _searchProgress;
        private bool _needsSceneFilterUpdate = true;

        private readonly List<ReferenceEntry> _assetResults = new List<ReferenceEntry>();
        private readonly List<ReferenceEntry> _sceneResults = new List<ReferenceEntry>();

        private struct SceneFilterInfo
        {
            public Scene Scene;
            public bool IsIncluded;
            public bool IsLoaded;
        }

        private enum ReferenceSourceType
        {
            Asset,
            Scene
        }

        private class ReferenceEntry
        {
            public ReferenceSourceType SourceType;
            public Object ContainerObject;
            public string ContainerLabel;
            public string ContainerPath;
            public bool Expanded;
            public List<ReferenceLocation> Locations = new List<ReferenceLocation>();
        }

        private class ReferenceLocation
        {
            public Object ReferencingObject;
            public string PropertyPath;
            public Object ActualContainer;
            public string DetailText;
            public string SceneName;
            public string SceneHierarchy;
            public string ContainerPath;  // 最后非引用容器的路径/层级
            public string ReferencePath;  // 引用链路径
        }

        private void OnEnable()
        {
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

        private void OnNewSceneCreated(Scene scene, NewSceneSetup setup, NewSceneMode mode)
        {
            _needsSceneFilterUpdate = true;
            Repaint();
        }

        private void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            _needsSceneFilterUpdate = true;
            Repaint();
        }

        private void OnSceneClosed(Scene scene)
        {
            _needsSceneFilterUpdate = true;
            Repaint();
        }

        public static void Open(Object targetAsset)
        {
            var window = GetWindow<FindReferencesWindow>("Find References");
            window.minSize = new Vector2(520f, 460f);
            window.Initialize(targetAsset);
        }

        private void Initialize(Object targetAsset)
        {
            _targetAsset = targetAsset;
            _sceneFilters.Clear();
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                var scene = SceneManager.GetSceneAt(i);
                _sceneFilters.Add(new SceneFilterInfo
                {
                    Scene = scene,
                    IsIncluded = scene.isLoaded,
                    IsLoaded = scene.isLoaded
                });
            }

            _sceneLimitTransform = null;
            _assetResults.Clear();
            _sceneResults.Clear();
            PerformSearch();
        }

        private void OnGUI()
        {
            if (_targetAsset == null)
            {
                EditorGUILayout.HelpBox("目标资产无效，请重新选择一个资产并重试。", MessageType.Warning);
                return;
            }

            // 场景有变化时更新列表
            if (_needsSceneFilterUpdate)
            {
                UpdateSceneFilters();
                _needsSceneFilterUpdate = false;
            }

            // 检查场景变化，如果场景失效则重置相关状态
            if (_sceneLimitTransform != null && !_sceneLimitTransform.gameObject.scene.isLoaded)
            {
                _sceneLimitTransform = null;
            }

            DrawHeader();
            GUILayout.Space(8f);
            DrawScopeControls();
            GUILayout.Space(8f);

            if (_isSearching)
            {
                DrawProgress();
            }
            else
            {
                DrawResultArea();
            }
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("引用查询", EditorStyles.boldLabel);
            EditorGUILayout.ObjectField("目标资产", _targetAsset, typeof(Object), false);
            EditorGUILayout.EndVertical();
        }

        private void DrawScopeControls()
        {
            ValidateSearchState();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("搜索范围", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(250f));
            _searchAssets = EditorGUILayout.ToggleLeft("资产", _searchAssets);
            using (new EditorGUI.DisabledScope(!_searchAssets))
            {
                EditorGUI.indentLevel++;
                _assetPathAssets = EditorGUILayout.ToggleLeft("Assets", _assetPathAssets);
                _assetPathPackages = EditorGUILayout.ToggleLeft("Packages", _assetPathPackages);
                if (!_assetPathAssets && !_assetPathPackages)
                {
                    _assetPathAssets = true;
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _searchScenes = EditorGUILayout.ToggleLeft("场景", _searchScenes);
            using (new EditorGUI.DisabledScope(!_searchScenes))
            {
                EditorGUI.indentLevel++;
                DrawSceneFilterList();
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            using (new EditorGUI.DisabledScope(!_searchScenes))
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("场景物体限制", EditorStyles.label);
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                _sceneLimitTransform = EditorGUILayout.ObjectField("物体", _sceneLimitTransform, typeof(Transform), true) as Transform;
                if (EditorGUI.EndChangeCheck())
                {
                    if (_sceneLimitTransform != null && !_sceneLimitTransform.gameObject.scene.isLoaded)
                    {
                        _sceneLimitTransform = null;
                        EditorUtility.DisplayDialog("无效物体", "请拖入当前已加载场景中的物体。", "确定");
                    }
                }

                using (new EditorGUI.DisabledScope(_sceneLimitTransform == null))
                {
                    if (GUILayout.Button("清除", GUILayout.Width(50f)))
                    {
                        _sceneLimitTransform = null;
                    }
                }
                EditorGUILayout.EndHorizontal();

                if (_sceneLimitTransform != null)
                {
                    EditorGUILayout.HelpBox("已限制搜索范围至该物体及其子物体，场景勾选项暂时失效。", MessageType.Info);
                }
            }

            EditorGUILayout.EndVertical();

            GUILayout.Space(4f);
            if (GUILayout.Button("重新搜索", GUILayout.Height(26f)))
            {
                PerformSearch();
            }
        }

        private void DrawSceneFilterList()
        {
            if (_sceneFilters.Count == 0)
            {
                EditorGUILayout.HelpBox("当前没有场景。", MessageType.Info);
                return;
            }

            for (int i = 0; i < _sceneFilters.Count; ++i)
            {
                var filter = _sceneFilters[i];
                if (!filter.Scene.IsValid())
                {
                    continue;
                }

                string sceneName = string.IsNullOrEmpty(filter.Scene.name) ? "Untitled" : filter.Scene.name;
                bool isEnabled = filter.IsIncluded;

                using (new EditorGUI.DisabledScope(!filter.IsLoaded || _sceneLimitTransform != null))
                {
                    isEnabled = EditorGUILayout.ToggleLeft(sceneName, isEnabled);
                }

                if (filter.IsLoaded)
                {
                    filter.IsIncluded = isEnabled;
                    _sceneFilters[i] = filter;
                }
            }

            if (_sceneLimitTransform == null && _sceneFilters.Where(x => x.IsLoaded).All(x => !x.IsIncluded) && _sceneFilters.Any(x => x.IsLoaded))
            {
                for (int i = 0; i < _sceneFilters.Count; ++i)
                {
                    if (_sceneFilters[i].IsLoaded)
                    {
                        var first = _sceneFilters[i];
                        first.IsIncluded = true;
                        _sceneFilters[i] = first;
                        break;
                    }
                }
            }
        }

        private void DrawProgress()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("搜索中...", EditorStyles.boldLabel);
            var rect = EditorGUILayout.GetControlRect(false, 18f);
            EditorGUI.ProgressBar(rect, _searchProgress, $"{_searchProgress * 100f:F0}%");
            EditorGUILayout.EndVertical();
        }

        private void DrawResultArea()
        {
            _scrollPosition = ToolboxUtils.ScrollView(_scrollPosition, () =>
            {
                DrawResultGroup("资产引用", _assetResults);
                GUILayout.Space(6f);
                DrawResultGroup("场景引用", _sceneResults);
            });
        }

        private void DrawResultGroup(string title, List<ReferenceEntry> entries)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"{title} ({entries.Count})", EditorStyles.boldLabel);

            if (entries.Count == 0)
            {
                EditorGUILayout.HelpBox("未找到任何结果。", MessageType.Info);
            }
            else
            {
                foreach (var entry in entries)
                {
                    if (entry.ContainerObject != null)
                    {
                        DrawReferenceEntry(entry);
                    }
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawReferenceEntry(ReferenceEntry entry)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            // 收起展开标题
            EditorGUILayout.BeginHorizontal();
            var foldoutRect = GUILayoutUtility.GetRect(14f, EditorGUIUtility.singleLineHeight, GUILayout.Width(14f));
            entry.Expanded = EditorGUI.Foldout(foldoutRect, entry.Expanded, GUIContent.none, true);
            
            // 按伊槽位显示最后非引用容器
            EditorGUILayout.ObjectField(entry.ContainerObject, typeof(Object), false, GUILayout.ExpandWidth(true));
            if (CanUseAsSearchTarget(entry.ContainerObject))
            {
                if (GUILayout.Button("作为“目标资产”进行搜索", GUILayout.Width(160f)))
                {
                    SearchUsingTarget(entry.ContainerObject);
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndHorizontal();
            
            if (entry.Expanded && entry.Locations.Count > 0)
            {
                EditorGUI.indentLevel++;
                DrawReferenceLocations(entry.Locations, entry.SourceType);
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.EndVertical();
            GUILayout.Space(4f);
        }

        private void DrawReferenceLocations(List<ReferenceLocation> locations, ReferenceSourceType sourceType)
        {
            // 按ReferencingObject分组
            var groupedLocations = new Dictionary<Object, List<ReferenceLocation>>();
            foreach (var location in locations)
            {
                if (!groupedLocations.ContainsKey(location.ReferencingObject))
                {
                    groupedLocations[location.ReferencingObject] = new List<ReferenceLocation>();
                }
                groupedLocations[location.ReferencingObject].Add(location);
            }
            
            // 对每个分组显示一次直接引用对象，然后显示所有路径
            foreach (var kvp in groupedLocations)
            {
                var directReference = kvp.Key;
                var pathList = kvp.Value;
                
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                
                // 直接引用
                EditorGUILayout.LabelField("直接引用", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField(directReference, typeof(Object), false, GUILayout.ExpandWidth(true));
                if (CanUseAsSearchTarget(directReference))
                {
                    if (GUILayout.Button("作为“目标资产”进行搜索", GUILayout.Width(160f)))
                    {
                        SearchUsingTarget(directReference);
                        GUIUtility.ExitGUI();
                    }
                }
                EditorGUILayout.EndHorizontal();
                
                // 引用路径链
                EditorGUILayout.LabelField("引用路径", EditorStyles.boldLabel);
                
                foreach (var location in pathList)
                {
                    string pathDisplay = "";
                    
                    if (directReference is UnityEditor.Animations.AnimatorController)
                    {
                        // AnimatorController 显示格式: X.controller [Layer > SubMachine > State]
                        pathDisplay = $"{directReference.name}.controller [{location.PropertyPath}]";
                    }
                    else if (sourceType == ReferenceSourceType.Scene)
                    {
                        pathDisplay = $"{(string.IsNullOrEmpty(location.SceneName) ? "Untitled" : location.SceneName)} > {location.SceneHierarchy}" +
                            (string.IsNullOrEmpty(location.PropertyPath) ? "" : $" > {location.PropertyPath}");
                    }
                    else
                    {
                        pathDisplay = $"{location.ContainerPath}" +
                            (string.IsNullOrEmpty(location.PropertyPath) ? "" : $" > {location.PropertyPath}");
                    }
                    
                    EditorGUILayout.SelectableLabel(pathDisplay, EditorStyles.textField, GUILayout.Height(20f));
                }
                
                EditorGUILayout.EndVertical();
            }
        }

        private void SearchUsingTarget(Object target)
        {
            if (!CanUseAsSearchTarget(target))
                return;

            _targetAsset = target;
            PerformSearch();
        }

        private static bool CanUseAsSearchTarget(Object target)
        {
            if (target == null)
                return false;

            if (!EditorUtility.IsPersistent(target))
                return false;

            var path = AssetDatabase.GetAssetPath(target);
            return !string.IsNullOrEmpty(path);
        }

        private void ValidateSearchState()
        {
            if (!_searchAssets && !_searchScenes)
            {
                _searchAssets = true;
            }

            if (_searchAssets && !_assetPathAssets && !_assetPathPackages)
            {
                _assetPathAssets = true;
            }

            if (_sceneLimitTransform == null && _sceneFilters.Count > 0 && !_sceneFilters.Any(x => x.IsIncluded))
            {
                var first = _sceneFilters[0];
                first.IsIncluded = true;
                _sceneFilters[0] = first;
            }
        }

        private void PerformSearch()
        {
            if (_targetAsset == null)
            {
                return;
            }

            _assetResults.Clear();
            _sceneResults.Clear();
            _isSearching = true;
            _searchProgress = 0f;

            try
            {
                var tasks = _searchAssets ? 1 : 0;
                tasks += _searchScenes ? 1 : 0;
                var currentTask = 0;

                if (_searchAssets)
                {
                    SearchAssetReferences();
                    currentTask++;
                    _searchProgress = currentTask / (float)Math.Max(1, tasks);
                }

                if (_searchScenes)
                {
                    SearchSceneReferences();
                    currentTask++;
                    _searchProgress = currentTask / (float)Math.Max(1, tasks);
                }
            }
            finally
            {
                _isSearching = false;
                _searchProgress = 1f;
            }
        }

        private void SearchAssetReferences()
        {
            var roots = new List<string>();
            if (_assetPathAssets) roots.Add("Assets");
            if (_assetPathPackages) roots.Add("Packages");

            var assetPaths = new List<string>();
            foreach (var root in roots)
            {
                var guids = AssetDatabase.FindAssets("", new[] { root });
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path))
                        continue;

                    if (path == AssetDatabase.GetAssetPath(_targetAsset))
                        continue;

                    if (AssetDatabase.IsValidFolder(path))
                        continue;

                    if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                        continue;

                    assetPaths.Add(path);
                }
            }

            try
            {
                for (int i = 0; i < assetPaths.Count; ++i)
                {
                    var path = assetPaths[i];
                    _searchProgress = i / (float)Math.Max(1, assetPaths.Count);
                    EditorUtility.DisplayProgressBar("正在搜索资产", path, _searchProgress);

                    var asset = AssetDatabase.LoadMainAssetAtPath(path);
                    if (asset == null)
                        continue;

                    if (FindObjectReferences(asset, _targetAsset, out var locations, allowIndirect: true))
                    {
                        var entry = GetOrCreateEntry(_assetResults, ReferenceSourceType.Asset, asset, AssetDatabase.GetAssetPath(asset));
                        entry.ContainerLabel = PathLabel(asset);
                        foreach (var location in locations)
                        {
                            location.ContainerPath = path;
                        }
                        entry.Locations.AddRange(locations);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void SearchSceneReferences()
        {
            UpdateSceneFilters();
            var targetAssetPath = AssetDatabase.GetAssetPath(_targetAsset);
            var targetMainAsset = string.IsNullOrEmpty(targetAssetPath) ? null : AssetDatabase.LoadMainAssetAtPath(targetAssetPath);
            var isTargetPrefab = targetMainAsset != null && PrefabUtility.GetPrefabAssetType(targetMainAsset) != PrefabAssetType.NotAPrefab;

            var sceneRoots = new List<GameObject>();
            if (_sceneLimitTransform != null)
            {
                sceneRoots.Add(_sceneLimitTransform.gameObject);
            }
            else
            {
                foreach (var filter in _sceneFilters)
                {
                    if (!filter.IsIncluded || !filter.Scene.IsValid() || !filter.Scene.isLoaded)
                        continue;

                    var rootObjects = filter.Scene.GetRootGameObjects();
                    sceneRoots.AddRange(rootObjects);
                }
            }

            var allGameObjects = new List<GameObject>();
            foreach (var root in sceneRoots)
            {
                if (root != null)
                {
                    allGameObjects.AddRange(GetHierarchy(root.transform).ToList());
                }
            }

            try
            {
                for (int i = 0; i < allGameObjects.Count; ++i)
                {
                    var go = allGameObjects[i];
                    if (go == null)
                        continue;

                    var sceneName = string.IsNullOrEmpty(go.scene.name) ? "Untitled" : go.scene.name;
                    var hierarchyPath = GetHierarchyPath(go);

                    _searchProgress = i / (float)Math.Max(1, allGameObjects.Count);
                    EditorUtility.DisplayProgressBar("正在搜索场景", $"{sceneName}/{hierarchyPath}", _searchProgress);

                    if (isTargetPrefab && IsScenePrefabInstanceOfTarget(go, targetAssetPath))
                    {
                        var entry = GetOrCreateEntry(_sceneResults, ReferenceSourceType.Scene, go, hierarchyPath);
                        entry.Locations.Add(new ReferenceLocation
                        {
                            ReferencingObject = go,
                            PropertyPath = "Prefab实例",
                            ActualContainer = go,
                            DetailText = "Prefab实例引用",
                            SceneName = sceneName,
                            SceneHierarchy = hierarchyPath,
                            ContainerPath = hierarchyPath
                        });
                    }

                    var components = go.GetComponents<Component>();
                    foreach (var component in components)
                    {
                        if (component == null)
                            continue;

                        if (FindObjectReferences(component, _targetAsset, out var locations, allowIndirect: true))
                        {
                            var entry = GetOrCreateEntry(_sceneResults, ReferenceSourceType.Scene, go, hierarchyPath);
                            foreach (var location in locations)
                            {
                                location.SceneName = sceneName;
                                location.SceneHierarchy = hierarchyPath;
                                location.ContainerPath = hierarchyPath;
                                entry.Locations.Add(location);
                            }
                        }
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static bool IsScenePrefabInstanceOfTarget(GameObject sceneObject, string targetPrefabPath)
        {
            if (sceneObject == null || string.IsNullOrEmpty(targetPrefabPath))
                return false;

            var instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(sceneObject);
            if (instanceRoot == null || instanceRoot != sceneObject)
                return false;

            var nearestPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);
            if (!string.IsNullOrEmpty(nearestPath) && string.Equals(nearestPath, targetPrefabPath, StringComparison.OrdinalIgnoreCase))
                return true;

            var source = PrefabUtility.GetCorrespondingObjectFromSource(instanceRoot);
            if (source == null)
                return false;

            var sourcePath = AssetDatabase.GetAssetPath(source);
            return !string.IsNullOrEmpty(sourcePath) && string.Equals(sourcePath, targetPrefabPath, StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateSceneFilters()
        {
            var currentScenes = new Dictionary<string, Scene>();
            
            // 收集所有有效的场景
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid())
                {
                    currentScenes[scene.path] = scene;
                }
            }

            // 更新现有条目的IsLoaded状态
            for (int i = 0; i < _sceneFilters.Count; ++i)
            {
                var filter = _sceneFilters[i];
                if (filter.Scene.IsValid())
                {
                    filter.IsLoaded = filter.Scene.isLoaded;
                    _sceneFilters[i] = filter;
                }
            }

            // 移除无效的场景
            for (int i = _sceneFilters.Count - 1; i >= 0; --i)
            {
                if (!_sceneFilters[i].Scene.IsValid())
                {
                    _sceneFilters.RemoveAt(i);
                }
            }

            // 添加新的场景
            foreach (var kvp in currentScenes)
            {
                if (!_sceneFilters.Exists(x => x.Scene.IsValid() && x.Scene.path == kvp.Key))
                {
                    _sceneFilters.Add(new SceneFilterInfo { Scene = kvp.Value, IsIncluded = kvp.Value.isLoaded, IsLoaded = kvp.Value.isLoaded });
                }
            }
        }

        private bool FindObjectReferences(Object source, Object target, out List<ReferenceLocation> locations, bool allowIndirect = false)
        {
            locations = new List<ReferenceLocation>();
            if (source == null)
                return false;

            if (source is AnimationClip clip)
            {
                return SearchAnimationClipReferences(clip, target, locations);
            }

            if (source is AnimatorController controller)
            {
                return SearchAnimatorControllerReferences(controller, target, locations, allowIndirect);
            }

            var visited = new HashSet<int>();
            var so = new SerializedObject(source);
            var iterator = so.GetIterator();
            while (iterator.NextVisible(true))
            {
                if (iterator.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                var referenced = iterator.objectReferenceValue;
                if (referenced == null)
                    continue;

                if (referenced == target)
                {
                    locations.Add(new ReferenceLocation
                    {
                        ReferencingObject = source,
                        PropertyPath = iterator.propertyPath,
                        ActualContainer = referenced,
                        DetailText = $"直接引用：{iterator.propertyPath}"
                    });
                    continue;
                }

                if (allowIndirect && ObjectReferencesTarget(referenced, target, visited, out var actualContainer))
                {
                    // actualContainer 是真正的直接引用对象，即使它不在场景中也应该包括
                    locations.Add(new ReferenceLocation
                    {
                        ReferencingObject = actualContainer,
                        PropertyPath = iterator.propertyPath,
                        ActualContainer = actualContainer,
                        DetailText = $"间接引用：{iterator.propertyPath} -> {ObjectLabel(actualContainer)}"
                    });
                }
            }

            return locations.Count > 0;
        }

        private bool SearchAnimatorControllerReferences(AnimatorController controller, Object target, List<ReferenceLocation> locations, bool allowIndirect)
        {
            var visited = new HashSet<int>();
            bool found = false;

            for (int layerIndex = 0; layerIndex < controller.layers.Length; layerIndex++)
            {
                var layer = controller.layers[layerIndex];
                var stateMachine = layer.stateMachine;
                if (stateMachine == null)
                    continue;

                string layerName = layer.name;

                // 搜索状态
                foreach (var state in stateMachine.states)
                {
                    if (state.state == null)
                        continue;

                    // 检查状态的动作
                    if (state.state.motion == target)
                    {
                        locations.Add(new ReferenceLocation
                        {
                            ReferencingObject = controller,
                            PropertyPath = $"{layerName} > {state.state.name}",
                            ActualContainer = target,
                            DetailText = $"动画控制器状态：{state.state.name}"
                        });
                        found = true;
                        continue;
                    }

                    // 检查间接引用
                    if (allowIndirect && state.state.motion != null && ObjectReferencesTarget(state.state.motion, target, visited, out var actualContainer))
                    {
                        locations.Add(new ReferenceLocation
                        {
                            ReferencingObject = controller,
                            PropertyPath = $"{layerName} > {state.state.name}",
                            ActualContainer = actualContainer,
                            DetailText = $"动画控制器状态（间接）：{state.state.name}"
                        });
                        found = true;
                    }
                }

                // 搜索状态机的子状态机
                SearchAnimatorSubStateMachine(stateMachine, target, controller, layerName, "", locations, visited, allowIndirect, ref found);
            }

            return found;
        }

        private void SearchAnimatorSubStateMachine(AnimatorStateMachine stateMachine, Object target, AnimatorController controller, string layerName, string parentPath, List<ReferenceLocation> locations, HashSet<int> visited, bool allowIndirect, ref bool found)
        {
            foreach (var subMachine in stateMachine.stateMachines)
            {
                if (subMachine.stateMachine == null)
                    continue;

                string currentPath = string.IsNullOrEmpty(parentPath) 
                    ? $"{layerName} > {subMachine.stateMachine.name}" 
                    : $"{parentPath} > {subMachine.stateMachine.name}";

                foreach (var state in subMachine.stateMachine.states)
                {
                    if (state.state == null)
                        continue;

                    string statePath = $"{currentPath} > {state.state.name}";

                    if (state.state.motion == target)
                    {
                        locations.Add(new ReferenceLocation
                        {
                            ReferencingObject = controller,
                            PropertyPath = statePath,
                            ActualContainer = target,
                            DetailText = $"动画控制器子状态机：{state.state.name}"
                        });
                        found = true;
                        continue;
                    }

                    if (allowIndirect && state.state.motion != null && ObjectReferencesTarget(state.state.motion, target, visited, out var actualContainer))
                    {
                        locations.Add(new ReferenceLocation
                        {
                            ReferencingObject = controller,
                            PropertyPath = statePath,
                            ActualContainer = actualContainer,
                            DetailText = $"动画控制器子状态机（间接）：{state.state.name}"
                        });
                        found = true;
                    }
                }

                SearchAnimatorSubStateMachine(subMachine.stateMachine, target, controller, layerName, currentPath, locations, visited, allowIndirect, ref found);
            }
        }

        private bool SearchAnimationClipReferences(AnimationClip clip, Object target, List<ReferenceLocation> locations)
        {
            var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            foreach (var binding in bindings)
            {
                var keyframes = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                foreach (var keyframe in keyframes)
                {
                    if (keyframe.value == target)
                    {
                        locations.Add(new ReferenceLocation
                        {
                            ReferencingObject = clip,
                            PropertyPath = $"{binding.path}:{binding.propertyName}",
                            ActualContainer = target,
                            DetailText = $"动画引用：{binding.path}/{binding.propertyName}"
                        });
                    }
                }
            }

            return locations.Count > 0;
        }

        private bool ObjectReferencesTarget(Object source, Object target, HashSet<int> visited, out Object actualContainer)
        {
            actualContainer = null;
            if (source == null || source == target)
                return false;

            var id = source.GetInstanceID();
            if (visited.Contains(id))
                return false;

            visited.Add(id);

            if (source is AnimationClip clip)
            {
                var locations = new List<ReferenceLocation>();
                if (SearchAnimationClipReferences(clip, target, locations) && locations.Count > 0)
                {
                    actualContainer = source;
                    return true;
                }

                return false;
            }

            if (source is AnimatorController controller)
            {
                var locations = new List<ReferenceLocation>();
                if (SearchAnimatorControllerReferences(controller, target, locations, allowIndirect: true) && locations.Count > 0)
                {
                    actualContainer = source;
                    return true;
                }

                return false;
            }

            var so = new SerializedObject(source);
            var iterator = so.GetIterator();
            while (iterator.NextVisible(true))
            {
                if (iterator.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                var referenced = iterator.objectReferenceValue;
                if (referenced == null)
                    continue;

                if (referenced == target)
                {
                    actualContainer = source;
                    return true;
                }

                if (ObjectReferencesTarget(referenced, target, visited, out actualContainer))
                {
                    return true;
                }
            }

            return false;
        }

        private ReferenceEntry GetOrCreateEntry(List<ReferenceEntry> list, ReferenceSourceType sourceType, Object container, string containerPath)
        {
            var entry = list.FirstOrDefault(x => x.SourceType == sourceType && x.ContainerObject == container);
            if (entry != null)
            {
                return entry;
            }

            entry = new ReferenceEntry
            {
                SourceType = sourceType,
                ContainerObject = container,
                ContainerPath = containerPath,
                ContainerLabel = ObjectLabel(container),
                Expanded = false
            };
            list.Add(entry);
            return entry;
        }

        private static string ObjectLabel(Object obj)
        {
            if (obj == null)
                return "未知对象";

            var path = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(path))
            {
                return $"{obj.name} ({path})";
            }

            if (obj is GameObject go)
            {
                return $"{go.name}";
            }

            return obj.name;
        }

        private static string PathLabel(Object obj)
        {
            if (obj == null)
            {
                return "未知";
            }

            var path = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(path))
            {
                return path;
            }

            if (obj is GameObject go)
            {
                return GetHierarchyPath(go);
            }

            return obj.name;
        }

        private static string GetHierarchyPath(GameObject gameObject)
        {
            if (gameObject == null)
                return string.Empty;

            var path = gameObject.name;
            var parent = gameObject.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }

        private static IEnumerable<GameObject> GetHierarchy(Transform root)
        {
            if (root == null)
                yield break;

            yield return root.gameObject;
            foreach (Transform child in root)
            {
                foreach (var sub in GetHierarchy(child))
                {
                    yield return sub;
                }
            }
        }
    }

    public static class FindReferences
    {
        [MenuItem("Assets/MVA Toolbox/Find References", false, 102)]
        private static void FindReferencesMenu()
        {
            var targetAsset = Selection.activeObject;
            if (targetAsset == null)
            {
                return;
            }

            FindReferencesWindow.Open(targetAsset);
        }

        [MenuItem("Assets/MVA Toolbox/Find References", true)]
        private static bool FindReferencesMenuValidate()
        {
            if (Selection.objects.Length != 1)
            {
                return false;
            }

            var targetAsset = Selection.activeObject;
            if (targetAsset == null)
            {
                return false;
            }

            var path = AssetDatabase.GetAssetPath(targetAsset);
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            if (AssetDatabase.IsValidFolder(path))
            {
                return false;
            }

            return true;
        }
    }
}