using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.FindReferencesTool
{
    internal sealed class FindReferencesSceneSearchService
    {
        private readonly FindReferencesReferenceWalker _referenceWalker;

        internal FindReferencesSceneSearchService(FindReferencesReferenceWalker referenceWalker)
        {
            _referenceWalker = referenceWalker;
        }

        internal List<FindReferencesEntry> Search(Object target, FindReferencesSearchOptions options, Func<float, string, bool> reportProgress)
        {
            List<FindReferencesEntry> results = new List<FindReferencesEntry>();
            if (target == null || options == null)
            {
                reportProgress?.Invoke(1f, string.Empty);
                return results;
            }

            string targetAssetPath = AssetDatabase.GetAssetPath(target);
            Object targetMainAsset = string.IsNullOrEmpty(targetAssetPath) ? null : AssetDatabase.LoadMainAssetAtPath(targetAssetPath);
            bool isTargetPrefab = targetMainAsset != null && PrefabUtility.GetPrefabAssetType(targetMainAsset) != PrefabAssetType.NotAPrefab;

            List<GameObject> sceneRoots = new List<GameObject>();
            if (options.SceneLimitTransform != null)
            {
                sceneRoots.Add(options.SceneLimitTransform.gameObject);
            }
            else
            {
                for (int filterIndex = 0; filterIndex < options.SceneFilters.Count; filterIndex++)
                {
                    FindReferencesSceneFilterInfo filter = options.SceneFilters[filterIndex];
                    if (!filter.IsIncluded || !filter.Scene.IsValid() || !filter.Scene.isLoaded)
                    {
                        continue;
                    }

                    sceneRoots.AddRange(filter.Scene.GetRootGameObjects());
                }
            }

            List<GameObject> allGameObjects = new List<GameObject>();
            for (int rootIndex = 0; rootIndex < sceneRoots.Count; rootIndex++)
            {
                GameObject root = sceneRoots[rootIndex];
                if (root == null)
                {
                    continue;
                }

                allGameObjects.AddRange(GetHierarchy(root.transform));
            }

            for (int gameObjectIndex = 0; gameObjectIndex < allGameObjects.Count; gameObjectIndex++)
            {
                GameObject gameObject = allGameObjects[gameObjectIndex];
                if (gameObject == null)
                {
                    continue;
                }

                string sceneName = string.IsNullOrEmpty(gameObject.scene.name) ? "Untitled" : gameObject.scene.name;
                string hierarchyPath = GetHierarchyPath(gameObject);
                if (reportProgress != null && reportProgress(gameObjectIndex / (float)Math.Max(1, allGameObjects.Count), $"{sceneName}/{hierarchyPath}"))
                {
                    return results;
                }

                if (isTargetPrefab && IsScenePrefabInstanceOfTarget(gameObject, targetAssetPath))
                {
                    FindReferencesEntry prefabEntry = GetOrCreateEntry(results, gameObject);
                    prefabEntry.Locations.Add(new FindReferencesLocation
                    {
                        SourceObject = gameObject,
                        DirectReferenceObject = gameObject,
                        MatchedContainer = gameObject,
                        PropertyPath = "Prefab实例",
                        SceneName = sceneName,
                        SceneHierarchy = hierarchyPath,
                        ContainerPath = hierarchyPath
                    });
                }

                Component[] components = gameObject.GetComponents<Component>();
                for (int componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    Component component = components[componentIndex];
                    if (component == null)
                    {
                        continue;
                    }

                    if (!_referenceWalker.FindObjectReferences(component, target, gameObject, out List<FindReferencesLocation> locations, true))
                    {
                        continue;
                    }

                    FindReferencesEntry entry = GetOrCreateEntry(results, gameObject);
                    for (int locationIndex = 0; locationIndex < locations.Count; locationIndex++)
                    {
                        FindReferencesLocation location = locations[locationIndex];
                        Object directReference = location.DirectReferenceObject ?? location.SourceObject ?? location.MatchedContainer;
                        GameObject directReferenceContainer = GetSceneContainerObject(directReference);
                        if (directReferenceContainer != null && directReferenceContainer != gameObject)
                        {
                            continue;
                        }

                        location.SceneName = sceneName;
                        location.SceneHierarchy = hierarchyPath;
                        location.ContainerPath = hierarchyPath;
                        if (location.SourceObject == null)
                        {
                            location.SourceObject = component;
                        }

                        if (location.DirectReferenceObject == null)
                        {
                            location.DirectReferenceObject = component;
                        }

                        entry.Locations.Add(location);
                    }
                }
            }

            reportProgress?.Invoke(1f, string.Empty);
            return results;
        }

        private static GameObject GetSceneContainerObject(Object obj)
        {
            if (obj is GameObject gameObject)
            {
                return gameObject;
            }

            if (obj is Component component)
            {
                return component.gameObject;
            }

            return null;
        }

        private static FindReferencesEntry GetOrCreateEntry(List<FindReferencesEntry> results, Object containerObject)
        {
            for (int index = 0; index < results.Count; index++)
            {
                FindReferencesEntry existing = results[index];
                if (existing.ContainerObject == containerObject)
                {
                    return existing;
                }
            }

            FindReferencesEntry entry = new FindReferencesEntry
            {
                ContainerObject = containerObject
            };
            results.Add(entry);
            return entry;
        }

        private static bool IsScenePrefabInstanceOfTarget(GameObject sceneObject, string targetPrefabPath)
        {
            if (sceneObject == null || string.IsNullOrEmpty(targetPrefabPath))
            {
                return false;
            }

            GameObject instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(sceneObject);
            if (instanceRoot == null || instanceRoot != sceneObject)
            {
                return false;
            }

            string nearestPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);
            if (!string.IsNullOrEmpty(nearestPath) && string.Equals(nearestPath, targetPrefabPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            Object source = PrefabUtility.GetCorrespondingObjectFromSource(instanceRoot);
            if (source == null)
            {
                return false;
            }

            string sourcePath = AssetDatabase.GetAssetPath(source);
            return !string.IsNullOrEmpty(sourcePath) && string.Equals(sourcePath, targetPrefabPath, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetHierarchyPath(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return string.Empty;
            }

            string path = gameObject.name;
            Transform parent = gameObject.transform.parent;
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
            {
                yield break;
            }

            yield return root.gameObject;
            foreach (Transform child in root)
            {
                foreach (GameObject subObject in GetHierarchy(child))
                {
                    yield return subObject;
                }
            }
        }
    }
}
