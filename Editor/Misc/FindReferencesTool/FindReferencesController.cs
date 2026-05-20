using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.FindReferencesTool
{
    internal sealed class FindReferencesController
    {
        private readonly FindReferencesReferenceWalker _referenceWalker = new FindReferencesReferenceWalker();
        private readonly FindReferencesAssetSearchService _assetSearchService;
        private readonly FindReferencesSceneSearchService _sceneSearchService;
        private readonly FindReferencesSearchOptions _options = new FindReferencesSearchOptions();
        private readonly List<FindReferencesEntry> _assetResults = new List<FindReferencesEntry>();
        private readonly List<FindReferencesEntry> _sceneResults = new List<FindReferencesEntry>();

        private Object _targetAsset;
        private bool _isSearching;
        private bool _cancelRequested;
        private float _searchProgress;
        private string _progressLabel = string.Empty;
        private bool _needsSceneFilterUpdate = true;

        internal FindReferencesController()
        {
            _assetSearchService = new FindReferencesAssetSearchService(_referenceWalker);
            _sceneSearchService = new FindReferencesSceneSearchService(_referenceWalker);
        }

        internal Object TargetAsset => _targetAsset;

        internal bool IsSearching => _isSearching;

        internal float SearchProgress => _searchProgress;

        internal string ProgressLabel => _progressLabel;

        internal IReadOnlyList<FindReferencesEntry> AssetResults => _assetResults;

        internal IReadOnlyList<FindReferencesEntry> SceneResults => _sceneResults;

        internal bool SearchAssets
        {
            get => _options.SearchAssets;
            set => _options.SearchAssets = value;
        }

        internal bool SearchScenes
        {
            get => _options.SearchScenes;
            set => _options.SearchScenes = value;
        }

        internal bool AssetPathAssets
        {
            get => _options.AssetPathAssets;
            set => _options.AssetPathAssets = value;
        }

        internal bool AssetPathPackages
        {
            get => _options.AssetPathPackages;
            set => _options.AssetPathPackages = value;
        }

        internal Transform SceneLimitTransform
        {
            get => _options.SceneLimitTransform;
            set => _options.SceneLimitTransform = value;
        }

        internal List<FindReferencesSceneFilterInfo> SceneFilters => _options.SceneFilters;

        internal void Initialize(Object targetAsset)
        {
            _targetAsset = targetAsset;
            UpdateSceneFiltersIfNeeded();
            _assetResults.Clear();
            _sceneResults.Clear();

            if (_targetAsset != null)
            {
                PerformSearch();
            }
        }

        internal void MarkSceneFiltersDirty()
        {
            _needsSceneFilterUpdate = true;
        }

        internal void UpdateSceneFiltersIfNeeded()
        {
            if (!_needsSceneFilterUpdate)
            {
                return;
            }

            UpdateSceneFilters();
            _needsSceneFilterUpdate = false;
        }

        internal void ValidateSceneLimitTransform()
        {
            if (_options.SceneLimitTransform != null && !_options.SceneLimitTransform.gameObject.scene.isLoaded)
            {
                _options.SceneLimitTransform = null;
            }
        }

        internal void SetTarget(Object targetAsset)
        {
            _targetAsset = targetAsset;
        }

        internal void ClearTarget()
        {
            _targetAsset = null;
            _assetResults.Clear();
            _sceneResults.Clear();
            _isSearching = false;
            _cancelRequested = false;
            _searchProgress = 0f;
            _progressLabel = string.Empty;
            _referenceWalker.ResetSearchCache();
            EditorUtility.ClearProgressBar();
        }

        internal bool CanSearchWithCurrentTarget()
        {
            return _targetAsset != null;
        }

        internal bool CanUseAsSearchTarget(Object target)
        {
            if (target == null)
            {
                return false;
            }

            string path = AssetDatabase.GetAssetPath(target);
            return !string.IsNullOrEmpty(path) && !AssetDatabase.IsValidFolder(path);
        }

        internal void SearchUsingTarget(Object target)
        {
            if (!CanUseAsSearchTarget(target))
            {
                return;
            }

            _targetAsset = target;
            PerformSearch();
        }

        internal void ValidateSearchState()
        {
            if (!_options.SearchAssets && !_options.SearchScenes)
            {
                _options.SearchAssets = true;
            }

            if (_options.SearchAssets && !_options.AssetPathAssets && !_options.AssetPathPackages)
            {
                _options.AssetPathAssets = true;
            }

            if (_options.SceneLimitTransform == null && _options.SceneFilters.Count > 0 && !_options.SceneFilters.Any(filter => filter.IsIncluded))
            {
                _options.SceneFilters[0].IsIncluded = true;
            }
        }

        internal void PerformSearch()
        {
            if (_targetAsset == null)
            {
                return;
            }

            _assetResults.Clear();
            _sceneResults.Clear();
            _isSearching = true;
            _cancelRequested = false;
            _searchProgress = 0f;
            _progressLabel = string.Empty;
            _referenceWalker.ResetSearchCache();

            try
            {
                int taskCount = (_options.SearchAssets ? 1 : 0) + (_options.SearchScenes ? 1 : 0);
                int completedTaskCount = 0;

                if (_options.SearchAssets)
                {
                    List<FindReferencesEntry> assetEntries = _assetSearchService.Search(_targetAsset, _options.Clone(), (progress, label) =>
                    {
                        return UpdateProgress(taskCount, completedTaskCount, progress, label);
                    });
                    if (!_cancelRequested)
                    {
                        _assetResults.AddRange(assetEntries);
                    }

                    if (_cancelRequested)
                    {
                        return;
                    }

                    completedTaskCount++;
                    UpdateProgress(taskCount, completedTaskCount, 0f, string.Empty);
                }

                if (_options.SearchScenes)
                {
                    List<FindReferencesEntry> sceneEntries = _sceneSearchService.Search(_targetAsset, _options.Clone(), (progress, label) =>
                    {
                        return UpdateProgress(taskCount, completedTaskCount, progress, label);
                    });
                    if (!_cancelRequested)
                    {
                        _sceneResults.AddRange(sceneEntries);
                    }

                    if (_cancelRequested)
                    {
                        return;
                    }

                    completedTaskCount++;
                    UpdateProgress(taskCount, completedTaskCount, 0f, string.Empty);
                }
            }
            finally
            {
                _isSearching = false;
                _searchProgress = _cancelRequested ? 0f : 1f;
                _progressLabel = string.Empty;
                _cancelRequested = false;
                EditorUtility.ClearProgressBar();
            }
        }

        internal void CancelSearch()
        {
            if (!_isSearching)
            {
                return;
            }

            _cancelRequested = true;
        }

        private void UpdateSceneFilters()
        {
            Dictionary<int, Scene> currentScenes = new Dictionary<int, Scene>();
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (scene.IsValid())
                {
                    currentScenes[scene.handle] = scene;
                }
            }

            for (int filterIndex = 0; filterIndex < _options.SceneFilters.Count; filterIndex++)
            {
                FindReferencesSceneFilterInfo filter = _options.SceneFilters[filterIndex];
                if (!filter.Scene.IsValid())
                {
                    continue;
                }

                filter.IsLoaded = filter.Scene.isLoaded;
            }

            for (int filterIndex = _options.SceneFilters.Count - 1; filterIndex >= 0; filterIndex--)
            {
                FindReferencesSceneFilterInfo filter = _options.SceneFilters[filterIndex];
                if (!filter.Scene.IsValid() || !currentScenes.ContainsKey(filter.Scene.handle))
                {
                    _options.SceneFilters.RemoveAt(filterIndex);
                }
            }

            foreach (KeyValuePair<int, Scene> pair in currentScenes)
            {
                bool exists = _options.SceneFilters.Any(filter => filter.Scene.IsValid() && filter.Scene.handle == pair.Key);
                if (exists)
                {
                    continue;
                }

                _options.SceneFilters.Add(new FindReferencesSceneFilterInfo
                {
                    Scene = pair.Value,
                    IsIncluded = pair.Value.isLoaded,
                    IsLoaded = pair.Value.isLoaded
                });
            }
        }

        private bool UpdateProgress(int taskCount, int completedTaskCount, float subProgress, string label)
        {
            float safeTaskCount = Mathf.Max(1, taskCount);
            _searchProgress = Mathf.Clamp01((completedTaskCount + Mathf.Clamp01(subProgress)) / safeTaskCount);
            _progressLabel = label ?? string.Empty;
            if (!string.IsNullOrEmpty(_progressLabel))
            {
                if (EditorUtility.DisplayCancelableProgressBar("引用查询", _progressLabel, _searchProgress))
                {
                    _cancelRequested = true;
                }
            }

            return _cancelRequested;
        }
    }
}
