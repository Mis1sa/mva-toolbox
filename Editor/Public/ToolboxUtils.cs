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

namespace MVA.Toolbox.Public
{
    public static class ToolboxUtils
    {
        private const string AqtRootFolder = "Assets/MVA Toolbox/AQT";
        private const string ControllerFolder = AqtRootFolder + "/Controllers";
        private const string ParametersFolder = AqtRootFolder + "/ExpressionParameters";
        private const string MenuFolder = AqtRootFolder + "/ExpressionMenus";

        public static string GetAqtRootFolder() => AqtRootFolder;

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

        public static string BuildAqtLayerFolder(string rootPath, string layerName)
        {
            string segment = SanitizePathSegment(layerName);
            if (string.IsNullOrEmpty(rootPath)) rootPath = AqtRootFolder;
            if (!rootPath.StartsWith("Assets/")) rootPath = AqtRootFolder;
            return $"{rootPath}/{segment}";
        }

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

        public static bool IsValidAvatar(GameObject obj)
        {
            if (obj == null) return false;
            return obj.GetComponent<VRCAvatarDescriptor>() != null;
        }

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

        public static VRCExpressionParameters GetExistingExpressionParameters(VRCAvatarDescriptor avatar)
        {
            if (avatar == null) return null;
            return avatar.expressionParameters;
        }

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

        public static VRCExpressionsMenu GetExistingExpressionsMenu(VRCAvatarDescriptor avatar)
        {
            if (avatar == null) return null;
            return avatar.expressionsMenu;
        }

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

        public static Dictionary<string, VRCExpressionsMenu> GetMenuMap(VRCExpressionsMenu rootMenu)
        {
            if (rootMenu == null) return new Dictionary<string, VRCExpressionsMenu>();
            // 使用本类已有的 TraverseMenuPaths 逻辑自行构建菜单路径到菜单对象的映射，
            // 避免依赖 VRChat SDK 内部的 VRCExpressionManager.GetAllMenusRecursive 辅助方法。
            var map = new Dictionary<string, VRCExpressionsMenu>();
            TraverseMenuPaths(rootMenu, "/", map);
            return map;
        }

        public static Dictionary<string, VRCExpressionsMenu> BuildMenuPathMap(VRCExpressionsMenu rootMenu)
        {
            var map = new Dictionary<string, VRCExpressionsMenu>();
            if (rootMenu == null) return map;

            TraverseMenuPaths(rootMenu, "/", map);
            return map;
        }

        private static void TraverseMenuPaths(VRCExpressionsMenu menu, string currentPath, Dictionary<string, VRCExpressionsMenu> map)
        {
            if (menu == null || map.ContainsKey(currentPath)) return;

            map[currentPath] = menu;

            if (menu.controls == null) return;

            foreach (var control in menu.controls)
            {
                if (control == null || control.type != VRCExpressionsMenu.Control.ControlType.SubMenu || control.subMenu == null)
                    continue;

                var name = string.IsNullOrEmpty(control.name) ? string.Empty : control.name.Trim();
                if (string.IsNullOrEmpty(name)) continue;

                string childPath = currentPath == "/" ? $"/{name}" : $"{currentPath}/{name}";
                TraverseMenuPaths(control.subMenu, childPath, map);
            }
        }

        public static string[] GetMenuDisplayPaths(VRCExpressionsMenu rootMenu)
        {
            if (rootMenu == null) return new[] { "/" };

            var map = BuildMenuPathMap(rootMenu);
            if (map == null || map.Count == 0) return new[] { "/" };

            return map.Keys
                .OrderBy(p => p.Length)
                .ThenBy(p => p, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// 若传入的目标物体本身不是 AAO MergeSkinnedMesh 节点，但其挂载的 SkinnedMeshRenderer
        /// 已经被某个 MergeSkinnedMesh.renderersSet 引用，则返回该 MergeSkinnedMesh 所在物体，
        /// 以避免同时对“源 SMR”和“合并节点”重复配置。否则返回原始 target。
        /// </summary>
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

            // 仅检查“当前物体自身”是否挂有 SkinnedMeshRenderer，且该 SMR 是否被某个 MSM 的 renderersSet 引用。
            // 不再向子节点查找 SMR，以严格遵循“物体自己在合并列表中才自动替换”的规则。
            var smrOnTarget = target.GetComponent<SkinnedMeshRenderer>();
            if (smrOnTarget == null) return target;

            // 在场景中查找所有 MergeSkinnedMesh 组件，检查其 renderersSet 是否包含该 SMR
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
                        // AAO 的 renderersSet 是 PrefabSafeSet<SkinnedMeshRenderer>，优先通过 GetAsSet() 拿到实际集合
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
                    // 反射失败时忽略该组件，尝试下一个
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

        /// <summary>
        /// 针对 BlendShape 目标解析实际使用的 SkinnedMeshRenderer。
        /// 优先使用目标自身的 SkinnedMeshRenderer；若不存在，则向父级查找 AAO 的 Merge 组件，
        /// 并使用其所在物体上的 SkinnedMeshRenderer；若仍未找到，再回退到子层级的 SkinnedMeshRenderer。
        /// </summary>
        public static SkinnedMeshRenderer ResolveSkinnedMeshForBlendShape(GameObject target)
        {
            if (target == null) return null;

            // 优先：目标自身上的 SkinnedMeshRenderer
            var smr = target.GetComponent<SkinnedMeshRenderer>();
            if (smr != null && smr.sharedMesh != null && smr.sharedMesh.blendShapeCount > 0)
            {
                return smr;
            }

            // 其次：向父级查找 AAO MergeSkinnedMesh 组件，并使用其所在物体上的 SkinnedMeshRenderer
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

            // 最后回退：搜索子层级 SkinnedMeshRenderer
            smr = target.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr != null && smr.sharedMesh != null && smr.sharedMesh.blendShapeCount > 0)
            {
                return smr;
            }

            return null;
        }

        /// <summary>
        /// 获取用于 UI 下拉显示的可用 BlendShape 名称列表。
        /// 优先使用 ResolveSkinnedMeshForBlendShape 返回的 SMR；若该 SMR 无 BlendShape，
        /// 则在挂有 AAO MergeSkinnedMesh 的节点及其子层级 SMR 中收集 BlendShape 名称；
        /// 若仍未找到，则回退到 target 子层级的 SkinnedMeshRenderer。
        /// </summary>
        public static string[] GetAvailableBlendShapeNames(GameObject target)
        {
            if (target == null) return System.Array.Empty<string>();

            var names = new List<string>();
            var resolved = ResolveSkinnedMeshForBlendShape(target);

            // 前置资格判断：只有当目标物体本身包含 SkinnedMeshRenderer，或自身挂有 AAO MergeSkinnedMesh 组件时，
            // 才认为有资格进入 BlendShape 模式。否则直接返回空列表，避免“纯集合物体”仅因子节点存在 SMR 就被当作可编辑目标。
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
                // 既不是带 SMR 的网格根，也不是 MSM 节点，直接视为不可进入 BlendShape 模式
                return System.Array.Empty<string>();
            }

            // 0. 优先：若存在 AAO 的 EditSkinnedMeshComponentUtil，则通过其 GetBlendShapes 接口获取最终 BlendShape 名称。
            // 为了让 AAO 的处理链生效，并避免对普通集合物体误触发 AAO 逻辑，
            // 仅当“当前拖入的物体本身”挂有 MergeSkinnedMesh 组件时，才使用该特殊路径。
            SkinnedMeshRenderer aaoRenderer = null;
            if (mergeOnTarget != null)
            {
                aaoRenderer = mergeOnTarget.GetComponent<SkinnedMeshRenderer>();
            }

            if (aaoRenderer == null)
            {
                aaoRenderer = resolved;
            }

            bool usedAaoInterface = false;
            if (aaoRenderer != null && mergeOnTarget != null)
            {
                try
                {
                    var utilType = FindTypeByFullName("Anatawa12.AvatarOptimizer.EditSkinnedMeshComponentUtil");
                    if (utilType != null)
                    {
                        // 在调用 GetBlendShapes 之前，先重置其内部共享排序器缓存，
                        // 以便在切换 AAO Merge 模式或相关组件配置后重新构建处理链。
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

            // 若 AAO MSM 存在且成功调用了 AAO 接口，则其返回结果视为最终真相：
            // 即便返回 0 个名称，也不再回退到基于 mesh 的遍历逻辑，直接视为“无 BlendShape”。
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

            // 去重但保持出现顺序
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
        /// 仅用于预览：根据 AAO MSM + RenameBlendShape 的规则，从“最终 BlendShape 名称”
        /// 反向解析出应当在场景中实际设置权重的 SkinnedMeshRenderer 及其原始 BlendShape 名称。
        /// 该方法完全基于当前组件状态即时计算，不做跨帧缓存，以便用户在任意时刻增删改
        /// MSM / RB 组件后，下一次预览都能使用最新配置。
        /// </summary>
        public struct BlendShapeTarget
        {
            public SkinnedMeshRenderer renderer;
            public string originalName;
        }

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

            // 情况一：当前物体本身就是 MSM 节点，按 MSM + RB 规则解析
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

                    // 读取 MSM 的 BlendShapeMode，使用名称判断是否为 RenameToAvoidConflict
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
                        // MSM 为重命名模式：finalName = prefix(rendererName) + rbName
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

            // 情况二：当前物体不是 MSM 节点，仅考虑自身 SMR + 其上的 RenameBlendShape
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
    }
}
