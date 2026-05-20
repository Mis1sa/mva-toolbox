using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.FindReferencesTool
{
    internal enum FindReferencesContainerKind
    {
        None,
        Asset,
        SceneObject
    }

    internal sealed class FindReferencesReferenceWalker
    {
        private readonly FindReferencesAnimatorSupport _animatorSupport = new FindReferencesAnimatorSupport();
        private readonly Dictionary<CachedReferenceKey, CachedReferenceResult> _indirectReferenceCache = new Dictionary<CachedReferenceKey, CachedReferenceResult>();

        internal void ResetSearchCache()
        {
            _indirectReferenceCache.Clear();
        }

        internal bool FindObjectReferences(Object source, Object target, Object rootContainer, out List<FindReferencesLocation> locations, bool allowIndirect)
        {
            locations = new List<FindReferencesLocation>();
            if (source == null || target == null)
            {
                return false;
            }

            if (source is AnimationClip clip)
            {
                return _animatorSupport.SearchAnimationClipReferences(clip, target, locations);
            }

            if (source is AnimatorController controller)
            {
                return _animatorSupport.SearchAnimatorControllerReferences(controller, target, locations, ObjectReferencesTarget, rootContainer, null, allowIndirect);
            }

            List<SerializedReferenceProperty> references = EnumerateObjectReferences(source);
            for (int referenceIndex = 0; referenceIndex < references.Count; referenceIndex++)
            {
                SerializedReferenceProperty reference = references[referenceIndex];
                Object referencedObject = reference.ReferencedObject;
                if (referencedObject == null)
                {
                    continue;
                }

                if (referencedObject == target)
                {
                    locations.Add(new FindReferencesLocation
                    {
                        SourceObject = source,
                        DirectReferenceObject = source,
                        MatchedContainer = referencedObject,
                        PropertyPath = reference.PropertyPath
                    });
                    continue;
                }

                if (!allowIndirect)
                {
                    continue;
                }

                HashSet<int> pathVisited = new HashSet<int> { source.GetInstanceID() };
                if (!ObjectReferencesTarget(referencedObject, target, rootContainer, pathVisited, out Object actualContainer))
                {
                    continue;
                }

                locations.Add(new FindReferencesLocation
                {
                    SourceObject = source,
                    DirectReferenceObject = actualContainer != null ? actualContainer : referencedObject,
                    MatchedContainer = actualContainer,
                    PropertyPath = reference.PropertyPath
                });
            }

            return locations.Count > 0;
        }

        internal bool ObjectReferencesTarget(Object source, Object target, Object rootContainer, HashSet<int> visited, out Object actualContainer)
        {
            HashSet<int> pathVisited = visited ?? new HashSet<int>();
            return ObjectReferencesTargetCore(source, target, rootContainer, pathVisited, out actualContainer);
        }

        private bool ObjectReferencesTargetCore(Object source, Object target, Object rootContainer, HashSet<int> pathVisited, out Object actualContainer)
        {
            actualContainer = null;
            if (source == null || target == null || source == target)
            {
                return false;
            }

            Object rootBoundary = GetBoundaryObject(rootContainer, out FindReferencesContainerKind rootKind);
            Object sourceBoundary = GetBoundaryObject(source, out FindReferencesContainerKind sourceKind);
            if (rootKind != FindReferencesContainerKind.None && sourceKind == rootKind && !ReferenceEquals(sourceBoundary, rootBoundary))
            {
                return false;
            }

            int instanceId = source.GetInstanceID();
            int rootContainerId = rootBoundary != null ? rootBoundary.GetInstanceID() : 0;
            CachedReferenceKey cacheKey = new CachedReferenceKey(instanceId, rootContainerId);
            if (_indirectReferenceCache.TryGetValue(cacheKey, out CachedReferenceResult cachedResult))
            {
                actualContainer = cachedResult.ActualContainer;
                return cachedResult.Found;
            }

            if (!pathVisited.Add(instanceId))
            {
                return false;
            }

            bool found = false;
            Object resolvedContainer = null;

            try
            {
                if (source is AnimationClip clip)
                {
                    List<FindReferencesLocation> locations = new List<FindReferencesLocation>();
                    if (_animatorSupport.SearchAnimationClipReferences(clip, target, locations) && locations.Count > 0)
                    {
                        found = true;
                        resolvedContainer = source;
                    }
                }
                else if (source is AnimatorController controller)
                {
                    List<FindReferencesLocation> locations = new List<FindReferencesLocation>();
                    if (_animatorSupport.SearchAnimatorControllerReferences(controller, target, locations, ObjectReferencesTarget, rootContainer, pathVisited, true) && locations.Count > 0)
                    {
                        found = true;
                        resolvedContainer = source;
                    }
                }
                else if (TryResolveSerializedReferences(source, target, rootContainer, pathVisited, out Object serializedContainer))
                {
                    found = true;
                    resolvedContainer = serializedContainer;
                }
            }

            finally
            {
                pathVisited.Remove(instanceId);
            }

            _indirectReferenceCache[cacheKey] = new CachedReferenceResult(found, resolvedContainer);
            actualContainer = resolvedContainer;
            return found;
        }

        private bool TryResolveSerializedReferences(Object source, Object target, Object rootContainer, HashSet<int> pathVisited, out Object actualContainer)
        {
            actualContainer = null;
            List<SerializedReferenceProperty> references = EnumerateObjectReferences(source);
            for (int referenceIndex = 0; referenceIndex < references.Count; referenceIndex++)
            {
                Object referencedObject = references[referenceIndex].ReferencedObject;
                if (referencedObject == null)
                {
                    continue;
                }

                if (referencedObject == target)
                {
                    actualContainer = source;
                    return true;
                }

                if (ObjectReferencesTargetCore(referencedObject, target, rootContainer, pathVisited, out actualContainer))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<SerializedReferenceProperty> EnumerateObjectReferences(Object source)
        {
            List<SerializedReferenceProperty> references = new List<SerializedReferenceProperty>();
            if (source == null)
            {
                return references;
            }

            try
            {
                SerializedObject serializedObject = new SerializedObject(source);
                SerializedProperty iterator = serializedObject.GetIterator();
                bool enterChildren = true;
                while (iterator.Next(enterChildren))
                {
                    enterChildren = true;
                    if (iterator.propertyType != SerializedPropertyType.ObjectReference)
                    {
                        continue;
                    }

                    Object referencedObject = iterator.objectReferenceValue;
                    if (referencedObject == null)
                    {
                        continue;
                    }

                    references.Add(new SerializedReferenceProperty(iterator.propertyPath, referencedObject));
                }
            }
            catch (Exception)
            {
                return references;
            }

            return references;
        }

        private static Object GetBoundaryObject(Object obj, out FindReferencesContainerKind kind)
        {
            kind = FindReferencesContainerKind.None;
            if (obj == null)
            {
                return null;
            }

            if (obj is GameObject gameObject)
            {
                kind = FindReferencesContainerKind.SceneObject;
                return gameObject;
            }

            if (obj is Component component)
            {
                GameObject owner = component.gameObject;
                if (owner != null)
                {
                    kind = FindReferencesContainerKind.SceneObject;
                    return owner;
                }
            }

            if (!EditorUtility.IsPersistent(obj))
            {
                return null;
            }

            string assetPath = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            Object mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (mainAsset == null)
            {
                return null;
            }

            kind = FindReferencesContainerKind.Asset;
            return mainAsset;
        }

        private readonly struct SerializedReferenceProperty
        {
            internal readonly string PropertyPath;
            internal readonly Object ReferencedObject;

            internal SerializedReferenceProperty(string propertyPath, Object referencedObject)
            {
                PropertyPath = propertyPath ?? string.Empty;
                ReferencedObject = referencedObject;
            }
        }

        private readonly struct CachedReferenceResult
        {
            internal readonly bool Found;
            internal readonly Object ActualContainer;

            internal CachedReferenceResult(bool found, Object actualContainer)
            {
                Found = found;
                ActualContainer = actualContainer;
            }
        }

        private readonly struct CachedReferenceKey
        {
            internal readonly int SourceInstanceId;
            internal readonly int RootContainerInstanceId;

            internal CachedReferenceKey(int sourceInstanceId, int rootContainerInstanceId)
            {
                SourceInstanceId = sourceInstanceId;
                RootContainerInstanceId = rootContainerInstanceId;
            }
        }
    }
}
