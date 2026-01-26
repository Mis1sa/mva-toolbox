using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor.Animations;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using MVA.Toolbox.AnimFixUtility.Services;

namespace MVA.Toolbox.Public
{
    // 提供 Avatar/资源管理、AAO 兼容处理和通用编辑器工具方法
    public static class ToolboxUtils
    {
        private const string AqtRootFolder = "Assets/MVA Toolbox/AQT";
        private const string ControllerFolder = AqtRootFolder + "/Controllers";
        private const string ParametersFolder = AqtRootFolder + "/ExpressionParameters";
        private const string MenuFolder = AqtRootFolder + "/ExpressionMenus";

        // 返回 AQT 相关资产的根目录
        public static string GetAqtRootFolder() => AqtRootFolder;

        // 清理路径片段中的非法字符，用于生成安全的文件夹/文件名
        public static string SanitizePathSegment(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment)) return "Layer";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = segment.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (invalid.Contains(chars[i]))
                {
                    chars[i] = '_';
                }
            }
            var result = new string(chars).Trim();
            return string.IsNullOrEmpty(result) ? "Layer" : result;
        }

        // 在指定根路径下生成 AQT 层级的子文件夹路径
        public static string BuildAqtLayerFolder(string rootPath, string layerName)
        {
            string segment = SanitizePathSegment(layerName);
            string normalizedRoot = NormalizeAssetsRoot(rootPath);
            return $"{normalizedRoot}/{segment}";
        }

        public static string SanitizeAssetFileName(string name, string fallback = "Asset")
        {
            if (string.IsNullOrWhiteSpace(name)) return fallback;

            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (invalid.Contains(chars[i]))
                {
                    chars[i] = '_';
                }
            }

            var result = new string(chars).Trim();
            return string.IsNullOrEmpty(result) ? fallback : result;
        }

        private static string NormalizeAssetsRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return AqtRootFolder;

            string trimmed = path.Trim().Replace('\\', '/');

            if (string.Equals(trimmed, "Assets", StringComparison.OrdinalIgnoreCase))
                return "Assets";

            if (!trimmed.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return AqtRootFolder;

            while (trimmed.Length > "Assets".Length && trimmed.EndsWith("/", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 1);
            }

            return trimmed;
        }

        public static Vector2 ScrollView(Vector2 scroll, System.Action drawContent, params GUILayoutOption[] options)
        {
            scroll = EditorGUILayout.BeginScrollView(scroll, options);
            drawContent?.Invoke();
            EditorGUILayout.EndScrollView();
            return scroll;
        }

        // 从传入对象解析并返回 VRCAvatarDescriptor
        public static VRCAvatarDescriptor GetAvatarDescriptor(UnityEngine.Object obj)
        {
            if (obj == null) return null;
            if (obj is VRCAvatarDescriptor vrc) return vrc;
            if (obj is GameObject go)
            {
                var v = go.GetComponent<VRCAvatarDescriptor>();
                if (v != null) return v;
                return go.GetComponentInParent<VRCAvatarDescriptor>(true);
            }
            return null;
        }

        // 判断给定物体是否是包含 VRCAvatarDescriptor 的 Avatar 根
        public static bool IsValidAvatar(GameObject obj)
        {
            if (obj == null) return false;
            return obj.GetComponent<VRCAvatarDescriptor>() != null;
        }

        // 计算 target 相对于 root 的层级路径（不包含 root 名称）
        public static string GetGameObjectPath(GameObject target, GameObject root)
        {
            if (target == null || root == null) return string.Empty;
            var stack = new Stack<string>();
            var t = target.transform;
            while (t != null && t != root.transform)
            {
                stack.Push(t.name);
                t = t.parent;
            }
            if (t == root.transform)
            {
                // do not include root name for relative path
            }
            return string.Join("/", stack.ToArray());
        }

        // 确保指定 Assets 下的文件夹路径存在，不存在时逐级创建
        public static void EnsureFolderExists(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return;
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            var parts = assetPath.Split('/');
            if (parts.Length == 0 || parts[0] != "Assets") return;
            var current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        // 获取 Avatar 已配置的 FX AnimatorController，如不存在则返回 null
        public static AnimatorController GetExistingFXController(VRCAvatarDescriptor avatar)
        {
            if (avatar == null) return null;

            var layers = avatar.baseAnimationLayers ?? System.Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            int fxIndex = -1;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].type == VRCAvatarDescriptor.AnimLayerType.FX)
                {
                    fxIndex = i;
                    if (layers[i].animatorController is AnimatorController existingController)
                    {
                        return existingController;
                    }
                    break;
                }
            }

            return null;
        }

        // 确保 Avatar 拥有 FX AnimatorController，不存在时自动创建并挂载
        public static AnimatorController EnsureFXController(VRCAvatarDescriptor avatar)
        {
            if (avatar == null) return null;

            var existing = GetExistingFXController(avatar);
            if (existing != null) return existing;

            var layers = avatar.baseAnimationLayers ?? System.Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            int fxIndex = -1;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].type == VRCAvatarDescriptor.AnimLayerType.FX)
                {
                    fxIndex = i;
                    break;
                }
            }

            EnsureFolderExists(ControllerFolder);
            string baseName = avatar.gameObject != null ? avatar.gameObject.name : "Avatar";
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{ControllerFolder}/{baseName}_FX.controller");
            var controller = AnimatorController.CreateAnimatorControllerAtPath(assetPath);

            if (fxIndex == -1)
            {
                var newLayer = new VRCAvatarDescriptor.CustomAnimLayer
                {
                    animatorController = controller,
                    isDefault = false,
                    type = VRCAvatarDescriptor.AnimLayerType.FX
                };
                var newLayers = new List<VRCAvatarDescriptor.CustomAnimLayer>(layers) { newLayer };
                avatar.baseAnimationLayers = newLayers.ToArray();
            }
            else
            {
                layers[fxIndex].animatorController = controller;
                layers[fxIndex].isDefault = false;
                avatar.baseAnimationLayers = layers;
            }

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(avatar);
            AssetDatabase.SaveAssets();
            return controller;
        }

        // 获取 Avatar 已配置的 VRCExpressionParameters，如不存在则返回 null
        public static VRCExpressionParameters GetExistingExpressionParameters(VRCAvatarDescriptor avatar)
        {
            if (avatar == null) return null;
            return avatar.expressionParameters;
        }

        // 确保 Avatar 拥有 VRCExpressionParameters，不存在时在固定目录下创建
        public static VRCExpressionParameters EnsureExpressionParameters(VRCAvatarDescriptor avatar)
        {
            if (avatar == null) return null;
            if (avatar.expressionParameters != null) return avatar.expressionParameters;

            EnsureFolderExists(ParametersFolder);
            string baseName = avatar.gameObject != null ? avatar.gameObject.name : "Avatar";
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{ParametersFolder}/{baseName}_Parameters.asset");

            var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            parameters.parameters = new VRCExpressionParameters.Parameter[0];
            AssetDatabase.CreateAsset(parameters, assetPath);
            AssetDatabase.SaveAssets();

            avatar.expressionParameters = parameters;
            EditorUtility.SetDirty(parameters);
            EditorUtility.SetDirty(avatar);
            AssetDatabase.SaveAssets();
            return parameters;
        }

        // 获取 Avatar 已配置的 VRCExpressionsMenu，如不存在则返回 null
        public static VRCExpressionsMenu GetExistingExpressionsMenu(VRCAvatarDescriptor avatar)
        {
            if (avatar == null) return null;
            return avatar.expressionsMenu;
        }

        // 确保 Avatar 拥有 VRCExpressionsMenu，不存在时在固定目录下创建
        public static VRCExpressionsMenu EnsureExpressionsMenu(VRCAvatarDescriptor avatar)
        {
            if (avatar == null) return null;
            if (avatar.expressionsMenu != null) return avatar.expressionsMenu;

            EnsureFolderExists(MenuFolder);
            string baseName = avatar.gameObject != null ? avatar.gameObject.name : "Avatar";
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{MenuFolder}/{baseName}_Expressions.asset");

            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            AssetDatabase.CreateAsset(menu, assetPath);
            AssetDatabase.SaveAssets();

            avatar.expressionsMenu = menu;
            EditorUtility.SetDirty(menu);
            EditorUtility.SetDirty(avatar);
            AssetDatabase.SaveAssets();
            return menu;
        }

        // 构建从菜单路径到菜单对象的映射：以根菜单名称为起点，路径形如 "Root/Sub/Sub2"
        public static Dictionary<string, VRCExpressionsMenu> GetMenuMap(VRCExpressionsMenu rootMenu)
        {
            if (rootMenu == null) return new Dictionary<string, VRCExpressionsMenu>();

            var map = new Dictionary<string, VRCExpressionsMenu>();
            var rootName = string.IsNullOrEmpty(rootMenu.name) ? "Menu" : rootMenu.name.Trim();
            TraverseMenuPaths(rootMenu, rootName, map);
            return map;
        }

        // 使用遍历逻辑创建菜单路径映射（与 GetMenuMap 行为保持一致）
        public static Dictionary<string, VRCExpressionsMenu> BuildMenuPathMap(VRCExpressionsMenu rootMenu)
        {
            var map = new Dictionary<string, VRCExpressionsMenu>();
            if (rootMenu == null) return map;

            var rootName = string.IsNullOrEmpty(rootMenu.name) ? "Menu" : rootMenu.name.Trim();
            TraverseMenuPaths(rootMenu, rootName, map);
            return map;
        }

        // 递归遍历菜单层级，将 SubMenu 映射到对应路径；路径格式保持 "Root/Sub/Sub2"，不再依赖以 "/" 为虚根
        private static void TraverseMenuPaths(VRCExpressionsMenu menu, string currentPath, Dictionary<string, VRCExpressionsMenu> map)
        {
            if (menu == null || string.IsNullOrEmpty(currentPath) || map.ContainsKey(currentPath)) return;

            map[currentPath] = menu;

            if (menu.controls == null) return;

            foreach (var control in menu.controls)
            {
                if (control == null || control.type != VRCExpressionsMenu.Control.ControlType.SubMenu || control.subMenu == null)
                    continue;

                var name = string.IsNullOrEmpty(control.name) ? string.Empty : control.name.Trim();
                if (string.IsNullOrEmpty(name)) continue;

                string childPath = string.IsNullOrEmpty(currentPath) ? name : $"{currentPath}/{name}";
                TraverseMenuPaths(control.subMenu, childPath, map);
            }
        }

        // 返回排序后的菜单路径列表，用于 UI 下拉显示，路径格式为 "Root/Sub/Sub2"
        public static string[] GetMenuDisplayPaths(VRCExpressionsMenu rootMenu)
        {
            if (rootMenu == null) return Array.Empty<string>();

            var map = BuildMenuPathMap(rootMenu);
            if (map == null || map.Count == 0) return Array.Empty<string>();

            return map.Keys
                .OrderBy(p => p.Length)
                .ThenBy(p => p, StringComparer.Ordinal)
                .ToArray();
        }

        public static GameObject ResolveMergeNodeTarget(GameObject target)
        {
            if (target == null) return null;

            // 若自身就是 MergeSkinnedMesh 节点，则直接返回自身
            var selfComps = target.GetComponents<Component>();
            for (int i = 0; i < selfComps.Length; i++)
            {
                var c = selfComps[i];
                if (c == null) continue;
                var t = c.GetType();
                if (t != null && t.FullName == "Anatawa12.AvatarOptimizer.MergeSkinnedMesh")
                {
                    return target;
                }
            }
            var smrOnTarget = target.GetComponent<SkinnedMeshRenderer>();
            if (smrOnTarget == null) return target;

            var allComponents = UnityEngine.Object.FindObjectsOfType<Component>(true);
            for (int i = 0; i < allComponents.Length; i++)
            {
                var comp = allComponents[i];
                if (comp == null) continue;
                var type = comp.GetType();
                if (type == null || type.FullName != "Anatawa12.AvatarOptimizer.MergeSkinnedMesh") continue;

                try
                {
                    var field = type.GetField("renderersSet", BindingFlags.NonPublic | BindingFlags.Instance);
                    var setObj = field != null ? field.GetValue(comp) : null;
                    if (setObj != null)
                    {
                        var setType = setObj.GetType();
                        var getAsSet = setType.GetMethod("GetAsSet", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        System.Collections.IEnumerable enumerable = null;
                        if (getAsSet != null)
                        {
                            var hs = getAsSet.Invoke(setObj, null) as System.Collections.IEnumerable;
                            enumerable = hs;
                        }
                        else if (setObj is System.Collections.IEnumerable e)
                        {
                            enumerable = e;
                        }

                        if (enumerable != null)
                        {
                            foreach (var obj in enumerable)
                            {
                                var smr = obj as SkinnedMeshRenderer;
                                if (smr == null) continue;
                                if (smr == smrOnTarget)
                                {
                                    return comp.gameObject;
                                }
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            return target;
        }

        private static Type FindTypeByFullName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                var asm = assemblies[i];
                if (asm == null) continue;
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }

        public static SkinnedMeshRenderer ResolveSkinnedMeshForBlendShape(GameObject target)
        {
            if (target == null) return null;

            // 优先：目标自身上的 SkinnedMeshRenderer
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

        public static string[] GetAvailableBlendShapeNames(GameObject target)
        {
            if (target == null) return System.Array.Empty<string>();

            var names = new List<string>();
            var resolved = ResolveSkinnedMeshForBlendShape(target);

            bool hasSelfSmr = target.GetComponent<SkinnedMeshRenderer>() != null;

            Component mergeOnTarget = null;
            if (target != null)
            {
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
            }

            if (!hasSelfSmr && mergeOnTarget == null)
            {
                return System.Array.Empty<string>();
            }

            SkinnedMeshRenderer aaoRenderer = null;
            if (mergeOnTarget != null)
            {
                aaoRenderer = mergeOnTarget.GetComponent<SkinnedMeshRenderer>();
            }

            if (aaoRenderer == null) aaoRenderer = resolved;

            bool usedAaoInterface = false;
            if (aaoRenderer != null && mergeOnTarget != null)
            {
                try
                {
                    var utilType = FindTypeByFullName("Anatawa12.AvatarOptimizer.EditSkinnedMeshComponentUtil");
                    if (utilType != null)
                    {
                        var resetMethod = utilType.GetMethod(
                            "ResetSharedSorter",
                            BindingFlags.NonPublic | BindingFlags.Static);
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
                            if (result is System.Array arr)
                            {
                                foreach (var entry in arr)
                                {
                                    if (entry == null) continue;
                                    // 条目类型为 (string name, float weight) 的 ValueTuple
                                    var entryType = entry.GetType();
                                    var nameField = entryType.GetField("Item1");
                                    if (nameField != null)
                                    {
                                        var nmObj = nameField.GetValue(entry) as string;
                                        if (!string.IsNullOrEmpty(nmObj))
                                        {
                                            names.Add(nmObj);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // 任何反射错误都静默忽略，仍允许回退到原有 Mesh 遍历逻辑
                }
            }

            if (usedAaoInterface)
            {
                if (names.Count == 0)
                {
                    return new[] { "(None)" };
                }
            }
            else if (names.Count == 0)
            {
                if (resolved != null && resolved.sharedMesh != null && resolved.sharedMesh.blendShapeCount > 0)
                {
                    var mesh = resolved.sharedMesh;
                    for (int i = 0; i < mesh.blendShapeCount; i++)
                    {
                        names.Add(mesh.GetBlendShapeName(i));
                    }
                }
                else
                {
                    // 2. 若当前物体本身挂有 AAO MergeSkinnedMesh，则在其节点及子节点 SMR 中收集 BlendShape
                    if (mergeOnTarget != null)
                    {
                        var mergeGo = mergeOnTarget.gameObject;
                        var smrs = mergeGo.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                        foreach (var smr in smrs)
                        {
                            if (smr == null || smr.sharedMesh == null) continue;
                            var mesh = smr.sharedMesh;
                            if (mesh.blendShapeCount <= 0) continue;
                            for (int i = 0; i < mesh.blendShapeCount; i++)
                            {
                                names.Add(mesh.GetBlendShapeName(i));
                            }
                        }
                    }

                    // 3. 若在 Merge 结构下仍未找到，则回退到 target 子层级 SMR
                    if (names.Count == 0)
                    {
                        var childSmrs = target.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                        foreach (var smr in childSmrs)
                        {
                            if (smr == null || smr.sharedMesh == null) continue;
                            var mesh = smr.sharedMesh;
                            if (mesh.blendShapeCount <= 0) continue;
                            for (int i = 0; i < mesh.blendShapeCount; i++)
                            {
                                names.Add(mesh.GetBlendShapeName(i));
                            }
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

            if (names.Count == 0)
            {
                return new[] { "(None)" };
            }

            return names.ToArray();
        }

        /// <summary>
        /// 混合形状目标结构体
        /// </summary>
        public struct BlendShapeTarget
        {
            public SkinnedMeshRenderer renderer;
            public string originalName;
        }

        /// <summary>
        /// 根据 AAO MSM + RenameBlendShape 规则，将最终 BlendShape 名称反向解析到原始 SMR 与原始名称
        /// </summary>
        public static List<BlendShapeTarget> ResolveOriginalBlendShapeTargets(GameObject target, string finalName)
        {
            var result = new List<BlendShapeTarget>();
            if (target == null || string.IsNullOrEmpty(finalName)) return result;

            // 反射获取 AAO 类型
            var mergeType = FindTypeByFullName("Anatawa12.AvatarOptimizer.MergeSkinnedMesh");
            var renameType = FindTypeByFullName("Anatawa12.AvatarOptimizer.RenameBlendShape");

            Component mergeOnTarget = null;
            if (mergeType != null)
            {
                var comps = target.GetComponents<Component>();
                for (int i = 0; i < comps.Length; i++)
                {
                    var c = comps[i];
                    if (c == null) continue;
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
                                renameMode = true;
                        }
                    }

                    if (sources.Count == 0)
                        return result;

                    if (renameMode)
                    {
                        var usedPrefixes = new HashSet<string>();
                        var prefixMap = new Dictionary<SkinnedMeshRenderer, string>();

                        for (int i = 0; i < sources.Count; i++)
                        {
                            var smr = sources[i];
                            if (smr == null || smr.gameObject == null) continue;
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
                            if (string.IsNullOrEmpty(prefix) || smr == null) continue;
                            if (finalName.StartsWith(prefix, StringComparison.Ordinal))
                            {
                                matchedSmr = smr;
                                rbName = finalName.Substring(prefix.Length);
                                break;
                            }
                        }

                        if (matchedSmr == null || string.IsNullOrEmpty(rbName))
                            return result;

                        string originalName = ResolveOriginalNameByRenameBlendShape(matchedSmr.gameObject, renameType, rbName);

                        if (matchedSmr.sharedMesh != null)
                        {
                            int idx = matchedSmr.sharedMesh.GetBlendShapeIndex(originalName);
                            if (idx >= 0)
                            {
                                result.Add(new BlendShapeTarget
                                {
                                    renderer = matchedSmr,
                                    originalName = originalName
                                });
                            }
                        }

                        return result;
                    }
                    else
                    {
                        // MSM 非重命名模式：finalName 直接对应各源 SMR 上的 rbName/原名
                        for (int i = 0; i < sources.Count; i++)
                        {
                            var smr = sources[i];
                            if (smr == null || smr.sharedMesh == null) continue;

                            string rbName = finalName;
                            string originalName = ResolveOriginalNameByRenameBlendShape(smr.gameObject, renameType, rbName);

                            int idx = smr.sharedMesh.GetBlendShapeIndex(originalName);
                            if (idx >= 0)
                            {
                                result.Add(new BlendShapeTarget
                                {
                                    renderer = smr,
                                    originalName = originalName
                                });
                            }
                        }

                        return result;
                    }
                }
                catch
                {
                    // 任意反射错误都视为无法解析，返回空列表，由调用方决定是否回退到旧逻辑
                    return result;
                }
            }
            var selfSmr = target.GetComponent<SkinnedMeshRenderer>();
            if (selfSmr == null || selfSmr.sharedMesh == null)
                return result;

            string finalAsRbName = finalName;
            string originalSelf = ResolveOriginalNameByRenameBlendShape(target, renameType, finalAsRbName);

            int selfIdx = selfSmr.sharedMesh.GetBlendShapeIndex(originalSelf);
            if (selfIdx >= 0)
            {
                result.Add(new BlendShapeTarget
                {
                    renderer = selfSmr,
                    originalName = originalSelf
                });
            }

            return result;
        }

        /// <summary>
        /// 辅助：根据 RenameBlendShape 的 nameMap，从 RB 改名后的名称反查原始名称。
        /// 若未找到映射或不存在 RB，则直接返回传入的名称。
        /// </summary>
        private static string ResolveOriginalNameByRenameBlendShape(GameObject go, Type renameType, string rbName)
        {
            if (go == null || string.IsNullOrEmpty(rbName) || renameType == null)
                return rbName;

            try
            {
                var rbComp = go.GetComponent(renameType);
                if (rbComp == null) return rbName;

                var rbType = rbComp.GetType();
                var nameMapField = rbType.GetField("nameMap", BindingFlags.NonPublic | BindingFlags.Instance);
                var nameMapObj = nameMapField != null ? nameMapField.GetValue(rbComp) : null;
                if (nameMapObj == null) return rbName;

                var mapType = nameMapObj.GetType();
                var getAsMap = mapType.GetMethod("GetAsMap", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var dictObj = getAsMap != null ? getAsMap.Invoke(nameMapObj, null) as IDictionary : null;
                if (dictObj == null) return rbName;

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
                // 任何反射错误都视为无法解析，回退到原名
            }

            return rbName;
        }

        // 判定物体是否为 Avatar 根（是否挂载 VRCAvatarDescriptor）
        public static bool IsAvatarRoot(GameObject obj)
        {
            if (obj == null) return false;
            return obj.GetComponent<VRCAvatarDescriptor>() != null;
        }

        // 判定物体是否包含有效 Animator（存在且 runtimeAnimatorController 非空）
        public static bool HasAnimator(GameObject obj)
        {
            if (obj == null) return false;
            var animator = obj.GetComponent<Animator>();
            return animator != null && animator.runtimeAnimatorController != null;
        }

        // 从 Avatar/Animator 根对象收集所有相关 AnimatorController（仅返回控制器列表）
        public static List<AnimatorController> CollectControllersFromRoot(
            GameObject root,
            bool includeSpecialLayers = true,
            bool allowAnimatorSubtree = true)
        {
            var entries = AnimFixControllerScanUtility.CollectControllersWithRoot(root, includeSpecialLayers, allowAnimatorSubtree);
            var result = new List<AnimatorController>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                var controller = entries[i].Controller;
                if (controller != null)
                {
                    result.Add(controller);
                }
            }
            return result;
        }

        // 根据 Avatar/Animator 来源构建控制器显示名称列表
        public static List<string> BuildControllerDisplayNames(VRCAvatarDescriptor descriptor, Animator animator, List<AnimatorController> controllers)
        {
            var names = new List<string>();
            if (controllers == null || controllers.Count == 0)
            {
                return names;
            }

            for (int i = 0; i < controllers.Count; i++)
            {
                var controller = controllers[i];
                if (controller == null)
                {
                    names.Add("(Missing Controller)");
                    continue;
                }

                if (!string.IsNullOrEmpty(controller.name) && controller.name.StartsWith("[MA Parameters]", StringComparison.Ordinal))
                {
                    names.Add(controller.name);
                    continue;
                }

                string label = null;

                if (descriptor != null)
                {
                    var baseLayers = descriptor.baseAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                    for (int j = 0; j < baseLayers.Length; j++)
                    {
                        var layer = baseLayers[j];
                        if (layer.animatorController == controller)
                        {
                            // 只显示层类型（如 FX、Base 之类），不再带 Base/Special 前缀
                            label = $"{layer.type}: {controller.name}";
                            break;
                        }
                    }

                    if (label == null)
                    {
                        var specialLayers = descriptor.specialAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                        for (int j = 0; j < specialLayers.Length; j++)
                        {
                            var layer = specialLayers[j];
                            if (layer.animatorController == controller)
                            {
                                // Special 层同样只显示层类型名称
                                label = $"{layer.type}: {controller.name}";
                                break;
                            }
                        }
                    }
                }

                if (label == null && animator != null && animator.runtimeAnimatorController == controller)
                {
                    label = "Animator: " + controller.name;
                }

                if (string.IsNullOrEmpty(label))
                {
                    label = "Animator Controller: " + controller.name;
                }

                names.Add(label);
            }

            return names;
        }
    }
}
