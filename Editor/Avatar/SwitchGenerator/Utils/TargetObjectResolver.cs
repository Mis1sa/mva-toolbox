using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace MVA.Toolbox.SwitchGenerator.Utils
{
    internal static class TargetObjectResolver
    {
        internal readonly struct BlendShapeTarget
        {
            internal BlendShapeTarget(SkinnedMeshRenderer renderer, string originalName)
            {
                this.renderer = renderer;
                this.originalName = originalName;
            }

            internal SkinnedMeshRenderer renderer { get; }
            internal string originalName { get; }
        }

        internal static GameObject ResolveMergeNodeTarget(GameObject target)
        {
            if (target == null)
            {
                return null;
            }

            var selfComps = target.GetComponents<Component>();
            for (int i = 0; i < selfComps.Length; i++)
            {
                var component = selfComps[i];
                if (component == null)
                {
                    continue;
                }

                var type = component.GetType();
                if (type != null && type.FullName == "Anatawa12.AvatarOptimizer.MergeSkinnedMesh")
                {
                    return target;
                }
            }

            var smrOnTarget = target.GetComponent<SkinnedMeshRenderer>();
            if (smrOnTarget == null)
            {
                return target;
            }

            var allComponents = UnityEngine.Object.FindObjectsOfType<Component>(true);
            for (int i = 0; i < allComponents.Length; i++)
            {
                var component = allComponents[i];
                if (component == null)
                {
                    continue;
                }

                var type = component.GetType();
                if (type == null || type.FullName != "Anatawa12.AvatarOptimizer.MergeSkinnedMesh")
                {
                    continue;
                }

                try
                {
                    var field = type.GetField("renderersSet", BindingFlags.NonPublic | BindingFlags.Instance);
                    var setObject = field != null ? field.GetValue(component) : null;
                    if (setObject == null)
                    {
                        continue;
                    }

                    var setType = setObject.GetType();
                    var getAsSet = setType.GetMethod("GetAsSet", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    System.Collections.IEnumerable enumerable = null;
                    if (getAsSet != null)
                    {
                        enumerable = getAsSet.Invoke(setObject, null) as System.Collections.IEnumerable;
                    }
                    else if (setObject is System.Collections.IEnumerable asEnumerable)
                    {
                        enumerable = asEnumerable;
                    }

                    if (enumerable == null)
                    {
                        continue;
                    }

                    foreach (var item in enumerable)
                    {
                        var smr = item as SkinnedMeshRenderer;
                        if (smr == null)
                        {
                            continue;
                        }

                        if (smr == smrOnTarget)
                        {
                            return component.gameObject;
                        }
                    }
                }
                catch
                {
                }
            }

            return target;
        }

        internal static GameObject ResolveValidTarget(GameObject target, VRCAvatarDescriptor avatar)
        {
            var resolved = ResolveMergeNodeTarget(target);
            return IsValidTargetObject(resolved, avatar) ? resolved : null;
        }

        internal static bool IsValidTargetObject(GameObject target, VRCAvatarDescriptor avatar)
        {
            if (target == null || avatar == null)
            {
                return false;
            }

            var avatarRoot = avatar.transform;
            var targetTransform = target.transform;
            if (avatarRoot == null || targetTransform == null)
            {
                return false;
            }

            if (targetTransform == avatarRoot)
            {
                return false;
            }

            if (!targetTransform.IsChildOf(avatarRoot))
            {
                return false;
            }

            var current = targetTransform;
            while (current != null && current != avatarRoot)
            {
                if (current.GetComponent<Animator>() != null)
                {
                    return false;
                }

                current = current.parent;
            }

            return true;
        }

        internal static SkinnedMeshRenderer ResolveSkinnedMeshForBlendShape(GameObject target)
        {
            if (target == null)
            {
                return null;
            }

            var smr = target.GetComponent<SkinnedMeshRenderer>();
            if (smr != null && smr.sharedMesh != null && smr.sharedMesh.blendShapeCount > 0)
            {
                return smr;
            }

            Component merge = null;
            var parents = target.GetComponentsInParent<Component>(true);
            for (int i = 0; i < parents.Length; i++)
            {
                var c = parents[i];
                if (c == null) continue;
                var t = c.GetType();
                if (t != null && t.FullName == "Anatawa12.AvatarOptimizer.MergeSkinnedMesh")
                {
                    merge = c;
                    break;
                }
            }

            if (merge != null)
            {
                var mergedSmr = merge.GetComponent<SkinnedMeshRenderer>();
                if (mergedSmr != null && mergedSmr.sharedMesh != null && mergedSmr.sharedMesh.blendShapeCount > 0)
                {
                    return mergedSmr;
                }
            }

            smr = target.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr != null && smr.sharedMesh != null && smr.sharedMesh.blendShapeCount > 0)
            {
                return smr;
            }

            return null;
        }

        internal static string[] GetAvailableBlendShapeNames(GameObject target)
        {
            if (target == null)
            {
                return Array.Empty<string>();
            }

            var names = new List<string>();
            var resolved = ResolveSkinnedMeshForBlendShape(target);
            bool hasSelfSmr = target.GetComponent<SkinnedMeshRenderer>() != null;

            Component mergeOnTarget = null;
            var comps = target.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                var c = comps[i];
                if (c == null) continue;
                var t = c.GetType();
                if (t != null && t.FullName == "Anatawa12.AvatarOptimizer.MergeSkinnedMesh")
                {
                    mergeOnTarget = c;
                    break;
                }
            }

            if (!hasSelfSmr && mergeOnTarget == null)
            {
                return Array.Empty<string>();
            }

            SkinnedMeshRenderer aaoRenderer = mergeOnTarget != null
                ? mergeOnTarget.GetComponent<SkinnedMeshRenderer>()
                : null;
            if (aaoRenderer == null) aaoRenderer = resolved;

            bool usedAaoInterface = false;
            if (aaoRenderer != null && mergeOnTarget != null)
            {
                try
                {
                    var utilType = FindTypeByFullName("Anatawa12.AvatarOptimizer.EditSkinnedMeshComponentUtil");
                    if (utilType != null)
                    {
                        var resetMethod = utilType.GetMethod("ResetSharedSorter", BindingFlags.NonPublic | BindingFlags.Static);
                        resetMethod?.Invoke(null, null);

                        var method = utilType.GetMethod(
                            "GetBlendShapes",
                            BindingFlags.Public | BindingFlags.Static,
                            null,
                            new[] { typeof(SkinnedMeshRenderer) },
                            null);

                        if (method != null)
                        {
                            usedAaoInterface = true;
                            var result = method.Invoke(null, new object[] { aaoRenderer });
                            if (result is Array arr)
                            {
                                foreach (var entry in arr)
                                {
                                    if (entry == null) continue;
                                    var nameField = entry.GetType().GetField("Item1");
                                    if (nameField != null)
                                    {
                                        var nm = nameField.GetValue(entry) as string;
                                        if (!string.IsNullOrEmpty(nm)) names.Add(nm);
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            if (usedAaoInterface)
            {
                if (names.Count == 0) return new[] { "(None)" };
            }
            else if (names.Count == 0)
            {
                if (resolved != null && resolved.sharedMesh != null && resolved.sharedMesh.blendShapeCount > 0)
                {
                    var mesh = resolved.sharedMesh;
                    for (int i = 0; i < mesh.blendShapeCount; i++) names.Add(mesh.GetBlendShapeName(i));
                }
                else
                {
                    if (mergeOnTarget != null)
                    {
                        var smrs = mergeOnTarget.gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                        foreach (var smr in smrs)
                        {
                            if (smr == null || smr.sharedMesh == null || smr.sharedMesh.blendShapeCount <= 0) continue;
                            for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++) names.Add(smr.sharedMesh.GetBlendShapeName(i));
                        }
                    }

                    if (names.Count == 0)
                    {
                        var childSmrs = target.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                        foreach (var smr in childSmrs)
                        {
                            if (smr == null || smr.sharedMesh == null || smr.sharedMesh.blendShapeCount <= 0) continue;
                            for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++) names.Add(smr.sharedMesh.GetBlendShapeName(i));
                        }
                    }
                }
            }

            if (names.Count > 1)
            {
                var seen = new HashSet<string>();
                var unique = new List<string>(names.Count);
                for (int i = 0; i < names.Count; i++)
                {
                    var nm = names[i];
                    if (string.IsNullOrEmpty(nm)) continue;
                    if (seen.Add(nm)) unique.Add(nm);
                }
                names = unique;
            }

            return names.Count == 0 ? new[] { "(None)" } : names.ToArray();
        }

        internal static List<BlendShapeTarget> ResolveOriginalBlendShapeTargets(GameObject target, string finalName)
        {
            var result = new List<BlendShapeTarget>();
            if (target == null || string.IsNullOrEmpty(finalName))
            {
                return result;
            }

            var mergeType = FindTypeByFullName("Anatawa12.AvatarOptimizer.MergeSkinnedMesh");
            var renameType = FindTypeByFullName("Anatawa12.AvatarOptimizer.RenameBlendShape");

            Component mergeOnTarget = null;
            if (mergeType != null)
            {
                var comps = target.GetComponents<Component>();
                for (int i = 0; i < comps.Length; i++)
                {
                    var c = comps[i];
                    if (c == null)
                    {
                        continue;
                    }

                    var t = c.GetType();
                    if (t != null && t == mergeType)
                    {
                        mergeOnTarget = c;
                        break;
                    }
                }
            }

            if (mergeOnTarget != null)
            {
                try
                {
                    var mergeTypeLocal = mergeOnTarget.GetType();
                    var renderersField = mergeTypeLocal.GetField("renderersSet", BindingFlags.NonPublic | BindingFlags.Instance);
                    var blendShapeModeField = mergeTypeLocal.GetField("blendShapeMode", BindingFlags.NonPublic | BindingFlags.Instance);

                    var setObj = renderersField != null ? renderersField.GetValue(mergeOnTarget) : null;
                    var sources = new List<SkinnedMeshRenderer>();

                    if (setObj != null)
                    {
                        var setType = setObj.GetType();
                        var getAsSet = setType.GetMethod("GetAsSet", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        IEnumerable enumerable = null;
                        if (getAsSet != null)
                        {
                            enumerable = getAsSet.Invoke(setObj, null) as IEnumerable;
                        }
                        else if (setObj is IEnumerable e)
                        {
                            enumerable = e;
                        }

                        if (enumerable != null)
                        {
                            foreach (var obj in enumerable)
                            {
                                if (obj is SkinnedMeshRenderer smr && smr != null)
                                {
                                    sources.Add(smr);
                                }
                            }
                        }
                    }

                    bool renameMode = false;
                    if (blendShapeModeField != null)
                    {
                        var modeVal = blendShapeModeField.GetValue(mergeOnTarget);
                        if (modeVal != null)
                        {
                            var modeName = modeVal.ToString();
                            if (modeName == "RenameToAvoidConflict")
                            {
                                renameMode = true;
                            }
                        }
                    }

                    if (sources.Count == 0)
                    {
                        return result;
                    }

                    if (renameMode)
                    {
                        var usedPrefixes = new HashSet<string>();
                        var prefixMap = new Dictionary<SkinnedMeshRenderer, string>();

                        for (int i = 0; i < sources.Count; i++)
                        {
                            var smr = sources[i];
                            if (smr == null || smr.gameObject == null)
                            {
                                continue;
                            }

                            var name = smr.gameObject.name ?? string.Empty;
                            var basePrefix = string.Format("{0}_{1}__", name.Length, name);
                            var prefix = basePrefix;
                            int j = 1;
                            while (usedPrefixes.Contains(prefix))
                            {
                                prefix = string.Format("{0}_{1}_{2}__", name.Length, name, j);
                                j++;
                            }

                            usedPrefixes.Add(prefix);
                            prefixMap[smr] = prefix;
                        }

                        SkinnedMeshRenderer matchedSmr = null;
                        string rbName = null;

                        foreach (var kv in prefixMap)
                        {
                            var smr = kv.Key;
                            var prefix = kv.Value;
                            if (string.IsNullOrEmpty(prefix) || smr == null)
                            {
                                continue;
                            }

                            if (finalName.StartsWith(prefix, StringComparison.Ordinal))
                            {
                                matchedSmr = smr;
                                rbName = finalName.Substring(prefix.Length);
                                break;
                            }
                        }

                        if (matchedSmr == null || string.IsNullOrEmpty(rbName))
                        {
                            return result;
                        }

                        string originalName = ResolveOriginalNameByRenameBlendShape(matchedSmr.gameObject, renameType, rbName);
                        if (matchedSmr.sharedMesh != null)
                        {
                            int idx = matchedSmr.sharedMesh.GetBlendShapeIndex(originalName);
                            if (idx >= 0)
                            {
                                result.Add(new BlendShapeTarget(matchedSmr, originalName));
                            }
                        }

                        return result;
                    }

                    for (int i = 0; i < sources.Count; i++)
                    {
                        var smr = sources[i];
                        if (smr == null || smr.sharedMesh == null)
                        {
                            continue;
                        }

                        string originalName = ResolveOriginalNameByRenameBlendShape(smr.gameObject, renameType, finalName);
                        int idx = smr.sharedMesh.GetBlendShapeIndex(originalName);
                        if (idx >= 0)
                        {
                            result.Add(new BlendShapeTarget(smr, originalName));
                        }
                    }

                    return result;
                }
                catch
                {
                    return result;
                }
            }

            var selfSmr = target.GetComponent<SkinnedMeshRenderer>();
            if (selfSmr == null || selfSmr.sharedMesh == null)
            {
                return result;
            }

            string originalSelf = ResolveOriginalNameByRenameBlendShape(target, renameType, finalName);
            int selfIdx = selfSmr.sharedMesh.GetBlendShapeIndex(originalSelf);
            if (selfIdx >= 0)
            {
                result.Add(new BlendShapeTarget(selfSmr, originalSelf));
            }

            return result;
        }

        private static string ResolveOriginalNameByRenameBlendShape(GameObject go, Type renameType, string rbName)
        {
            if (go == null || string.IsNullOrEmpty(rbName) || renameType == null)
            {
                return rbName;
            }

            try
            {
                var rbComp = go.GetComponent(renameType);
                if (rbComp == null)
                {
                    return rbName;
                }

                var rbType = rbComp.GetType();
                var nameMapField = rbType.GetField("nameMap", BindingFlags.NonPublic | BindingFlags.Instance);
                var nameMapObj = nameMapField != null ? nameMapField.GetValue(rbComp) : null;
                if (nameMapObj == null)
                {
                    return rbName;
                }

                var mapType = nameMapObj.GetType();
                var getAsMap = mapType.GetMethod("GetAsMap", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var dictObj = getAsMap != null ? getAsMap.Invoke(nameMapObj, null) as IDictionary : null;
                if (dictObj == null)
                {
                    return rbName;
                }

                foreach (DictionaryEntry entry in dictObj)
                {
                    var original = entry.Key as string;
                    var newName = entry.Value as string;
                    if (!string.IsNullOrEmpty(original) && newName == rbName)
                    {
                        return original;
                    }
                }
            }
            catch
            {
            }

            return rbName;
        }

        private static Type FindTypeByFullName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                var t = assemblies[i]?.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }
    }
}
