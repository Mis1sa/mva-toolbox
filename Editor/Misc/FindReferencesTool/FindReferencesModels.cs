using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.FindReferencesTool
{
    internal enum FindReferencesSourceType
    {
        Asset,
        Scene
    }

    internal sealed class FindReferencesSceneFilterInfo
    {
        internal Scene Scene;
        internal bool IsIncluded;
        internal bool IsLoaded;
    }

    internal sealed class FindReferencesLocation
    {
        internal Object SourceObject;
        internal Object DirectReferenceObject;
        internal Object MatchedContainer;
        internal string PropertyPath = string.Empty;
        internal string SceneName = string.Empty;
        internal string SceneHierarchy = string.Empty;
        internal string ContainerPath = string.Empty;
    }

    internal sealed class FindReferencesEntry
    {
        internal Object ContainerObject;
        internal bool Expanded;
        internal List<FindReferencesLocation> Locations { get; } = new List<FindReferencesLocation>();
    }

    internal sealed class FindReferencesSearchOptions
    {
        internal bool SearchAssets = true;
        internal bool SearchScenes = true;
        internal bool AssetPathAssets = true;
        internal bool AssetPathPackages;
        internal Transform SceneLimitTransform;
        internal List<FindReferencesSceneFilterInfo> SceneFilters { get; } = new List<FindReferencesSceneFilterInfo>();

        internal FindReferencesSearchOptions Clone()
        {
            FindReferencesSearchOptions clone = new FindReferencesSearchOptions
            {
                SearchAssets = SearchAssets,
                SearchScenes = SearchScenes,
                AssetPathAssets = AssetPathAssets,
                AssetPathPackages = AssetPathPackages,
                SceneLimitTransform = SceneLimitTransform
            };

            for (int index = 0; index < SceneFilters.Count; index++)
            {
                FindReferencesSceneFilterInfo filter = SceneFilters[index];
                clone.SceneFilters.Add(new FindReferencesSceneFilterInfo
                {
                    Scene = filter.Scene,
                    IsIncluded = filter.IsIncluded,
                    IsLoaded = filter.IsLoaded
                });
            }

            return clone;
        }
    }
}
