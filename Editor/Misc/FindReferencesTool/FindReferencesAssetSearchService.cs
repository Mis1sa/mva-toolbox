using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.FindReferencesTool
{
    internal sealed class FindReferencesAssetSearchService
    {
        private readonly FindReferencesReferenceWalker _referenceWalker;

        internal FindReferencesAssetSearchService(FindReferencesReferenceWalker referenceWalker)
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

            List<string> roots = new List<string>();
            if (options.AssetPathAssets)
            {
                roots.Add("Assets");
            }

            if (options.AssetPathPackages)
            {
                roots.Add("Packages");
            }

            List<string> assetPaths = new List<string>();
            HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int rootIndex = 0; rootIndex < roots.Count; rootIndex++)
            {
                string root = roots[rootIndex];
                string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { root });
                for (int guidIndex = 0; guidIndex < guids.Length; guidIndex++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[guidIndex]);
                    if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path) || path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (seenPaths.Add(path))
                    {
                        assetPaths.Add(path);
                    }
                }
            }

            for (int pathIndex = 0; pathIndex < assetPaths.Count; pathIndex++)
            {
                string assetPath = assetPaths[pathIndex];
                if (reportProgress != null && reportProgress(pathIndex / (float)Math.Max(1, assetPaths.Count), assetPath))
                {
                    return results;
                }

                Object mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (mainAsset == null || mainAsset == target)
                {
                    continue;
                }

                if (!_referenceWalker.FindObjectReferences(mainAsset, target, mainAsset, out List<FindReferencesLocation> locations, true))
                {
                    continue;
                }

                List<FindReferencesLocation> filteredLocations = FilterLocationsForAssetContainer(mainAsset, locations);
                if (filteredLocations.Count == 0)
                {
                    continue;
                }

                FindReferencesEntry entry = GetOrCreateEntry(results, mainAsset);
                AppendLocations(entry, filteredLocations, assetPath, mainAsset);
            }

            reportProgress?.Invoke(1f, string.Empty);
            return results;
        }

        private static List<FindReferencesLocation> FilterLocationsForAssetContainer(Object containerAsset, List<FindReferencesLocation> locations)
        {
            List<FindReferencesLocation> filtered = new List<FindReferencesLocation>();
            for (int index = 0; index < locations.Count; index++)
            {
                FindReferencesLocation location = locations[index];
                Object directReference = GetEffectiveDirectReference(location);
                if (IsIndependentAsset(directReference) && directReference != containerAsset)
                {
                    continue;
                }

                filtered.Add(location);
            }

            return filtered;
        }

        private static void AppendLocations(FindReferencesEntry entry, List<FindReferencesLocation> locations, string assetPath, Object fallbackSourceObject)
        {
            for (int locationIndex = 0; locationIndex < locations.Count; locationIndex++)
            {
                FindReferencesLocation location = locations[locationIndex];
                if (string.IsNullOrEmpty(location.ContainerPath))
                {
                    location.ContainerPath = assetPath;
                }

                if (location.SourceObject == null)
                {
                    location.SourceObject = fallbackSourceObject;
                }

                if (location.DirectReferenceObject == null)
                {
                    location.DirectReferenceObject = fallbackSourceObject;
                }

                entry.Locations.Add(location);
            }
        }

        private static Object GetEffectiveDirectReference(FindReferencesLocation location)
        {
            if (location == null)
            {
                return null;
            }

            return location.DirectReferenceObject ?? location.SourceObject ?? location.MatchedContainer;
        }

        private static bool IsIndependentAsset(Object obj)
        {
            if (obj == null || !EditorUtility.IsPersistent(obj))
            {
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            Object mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            return mainAsset == obj;
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
    }
}
