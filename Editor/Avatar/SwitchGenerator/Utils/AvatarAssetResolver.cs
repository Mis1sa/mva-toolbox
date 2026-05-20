using System.Collections.Generic;
using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace MVA.Toolbox.SwitchGenerator.Utils
{
    internal static class AvatarAssetResolver
    {
        private const string RootFolder = "Assets/MVA Toolbox/SwitchGenerator";
        private const string ControllersFolder = RootFolder + "/Controllers";
        private const string ParametersFolder = RootFolder + "/ExpressionParameters";
        private const string MenusFolder = RootFolder + "/ExpressionMenus";

        public static AnimatorController GetOrCreateFxController(VRCAvatarDescriptor avatar)
        {
            if (avatar == null)
            {
                return null;
            }

            var layers = avatar.baseAnimationLayers ?? System.Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            int fxIndex = -1;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].type != VRCAvatarDescriptor.AnimLayerType.FX)
                {
                    continue;
                }

                fxIndex = i;
                if (layers[i].animatorController is AnimatorController existing)
                {
                    return existing;
                }

                break;
            }

            EnsureFolder(ControllersFolder);
            string path = AssetDatabase.GenerateUniqueAssetPath(ControllersFolder + "/" + avatar.gameObject.name + "_FX.controller");
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);

            if (fxIndex >= 0)
            {
                var fxLayer = layers[fxIndex];
                fxLayer.animatorController = controller;
                fxLayer.isDefault = false;
                layers[fxIndex] = fxLayer;
            }
            else
            {
                var newLayer = new VRCAvatarDescriptor.CustomAnimLayer
                {
                    type = VRCAvatarDescriptor.AnimLayerType.FX,
                    isDefault = false,
                    isEnabled = true,
                    animatorController = controller
                };
                var list = new List<VRCAvatarDescriptor.CustomAnimLayer>(layers) { newLayer };
                layers = list.ToArray();
            }

            avatar.baseAnimationLayers = layers;
            EditorUtility.SetDirty(avatar);
            return controller;
        }

        public static VRCExpressionParameters GetOrCreateExpressionParameters(VRCAvatarDescriptor avatar)
        {
            if (avatar == null)
            {
                return null;
            }

            if (avatar.expressionParameters != null)
            {
                return avatar.expressionParameters;
            }

            EnsureFolder(ParametersFolder);
            string path = AssetDatabase.GenerateUniqueAssetPath(ParametersFolder + "/" + avatar.gameObject.name + "_Parameters.asset");
            var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            parameters.parameters = new VRCExpressionParameters.Parameter[0];
            AssetDatabase.CreateAsset(parameters, path);
            avatar.expressionParameters = parameters;
            EditorUtility.SetDirty(avatar);
            return parameters;
        }

        public static VRCExpressionsMenu GetOrCreateExpressionsMenu(VRCAvatarDescriptor avatar)
        {
            if (avatar == null)
            {
                return null;
            }

            if (avatar.expressionsMenu != null)
            {
                return avatar.expressionsMenu;
            }

            EnsureFolder(MenusFolder);
            string path = AssetDatabase.GenerateUniqueAssetPath(MenusFolder + "/" + avatar.gameObject.name + "_Menu.asset");
            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.controls = new List<VRCExpressionsMenu.Control>();
            AssetDatabase.CreateAsset(menu, path);
            avatar.expressionsMenu = menu;
            EditorUtility.SetDirty(avatar);
            return menu;
        }

        public static string BuildLayerFolder(string root, string layerName)
        {
            string normalizedRoot = NormalizeRoot(root);
            string safeLayer = SanitizeFileName(layerName, "Layer");
            return normalizedRoot + "/" + safeLayer;
        }

        internal static string NormalizeAssetsRootPath(string root)
        {
            return NormalizeRoot(root);
        }

        public static void EnsureFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            string normalized = folder.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(normalized))
            {
                return;
            }

            string[] parts = normalized.Split('/');
            if (parts.Length == 0 || parts[0] != "Assets")
            {
                return;
            }

            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        public static string GetRelativePath(GameObject target, GameObject root)
        {
            if (target == null || root == null)
            {
                return string.Empty;
            }

            var stack = new Stack<string>();
            Transform current = target.transform;
            while (current != null && current != root.transform)
            {
                stack.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", stack.ToArray());
        }

        public static Dictionary<string, VRCExpressionsMenu> BuildMenuMap(VRCExpressionsMenu root)
        {
            var map = new Dictionary<string, VRCExpressionsMenu>();
            if (root == null)
            {
                return map;
            }

            var legacyMap = BuildLegacyMenuPathMap(root);
            string rootName = string.IsNullOrWhiteSpace(root.name) ? "Menu" : root.name.Trim();
            foreach (var kvp in legacyMap)
            {
                if (kvp.Value == null)
                {
                    continue;
                }

                string relativePath = ConvertLegacyMenuPathToRelative(kvp.Key, rootName);
                if (!map.ContainsKey(relativePath))
                {
                    map[relativePath] = kvp.Value;
                }
            }

            if (!map.ContainsKey("/"))
            {
                map["/"] = root;
            }

            return map;
        }

        public static string[] BuildMenuDisplayPaths(VRCExpressionsMenu root)
        {
            if (root == null)
            {
                return Array.Empty<string>();
            }

            string rootName = string.IsNullOrWhiteSpace(root.name) ? "Menu" : root.name.Trim();
            var paths = new List<string>();
            TraverseLegacyMenuDisplayPaths(root, rootName, paths, new HashSet<string>(StringComparer.Ordinal));
            if (paths.Count == 0)
            {
                return Array.Empty<string>();
            }

            return paths.ToArray();
        }

        private static Dictionary<string, VRCExpressionsMenu> BuildLegacyMenuPathMap(VRCExpressionsMenu root)
        {
            var map = new Dictionary<string, VRCExpressionsMenu>();
            if (root == null)
            {
                return map;
            }

            string rootName = string.IsNullOrWhiteSpace(root.name) ? "Menu" : root.name.Trim();
            TraverseLegacyMenuPaths(root, rootName, map);
            return map;
        }

        private static void TraverseLegacyMenuPaths(VRCExpressionsMenu menu, string currentPath, Dictionary<string, VRCExpressionsMenu> map)
        {
            if (menu == null || string.IsNullOrEmpty(currentPath) || map.ContainsKey(currentPath))
            {
                return;
            }

            map[currentPath] = menu;

            if (menu.controls == null)
            {
                return;
            }

            for (int i = 0; i < menu.controls.Count; i++)
            {
                var control = menu.controls[i];
                if (control == null || control.type != VRCExpressionsMenu.Control.ControlType.SubMenu || control.subMenu == null)
                {
                    continue;
                }

                string nodeName = string.IsNullOrWhiteSpace(control.name)
                    ? control.subMenu.name
                    : control.name;
                nodeName = string.IsNullOrWhiteSpace(nodeName) ? string.Empty : nodeName.Trim().Trim('/');
                if (string.IsNullOrWhiteSpace(nodeName))
                {
                    continue;
                }

                string childPath = string.IsNullOrEmpty(currentPath)
                    ? nodeName
                    : currentPath + "/" + nodeName;
                TraverseLegacyMenuPaths(control.subMenu, childPath, map);
            }
        }

        private static void TraverseLegacyMenuDisplayPaths(
            VRCExpressionsMenu menu,
            string currentPath,
            List<string> paths,
            HashSet<string> seenPaths)
        {
            if (menu == null || string.IsNullOrEmpty(currentPath) || !seenPaths.Add(currentPath))
            {
                return;
            }

            paths.Add(currentPath);

            if (menu.controls == null)
            {
                return;
            }

            for (int i = 0; i < menu.controls.Count; i++)
            {
                var control = menu.controls[i];
                if (control == null || control.type != VRCExpressionsMenu.Control.ControlType.SubMenu || control.subMenu == null)
                {
                    continue;
                }

                string nodeName = string.IsNullOrWhiteSpace(control.name)
                    ? control.subMenu.name
                    : control.name;
                nodeName = string.IsNullOrWhiteSpace(nodeName) ? string.Empty : nodeName.Trim().Trim('/');
                if (string.IsNullOrWhiteSpace(nodeName))
                {
                    continue;
                }

                string childPath = string.IsNullOrEmpty(currentPath)
                    ? nodeName
                    : currentPath + "/" + nodeName;
                TraverseLegacyMenuDisplayPaths(control.subMenu, childPath, paths, seenPaths);
            }
        }

        private static string ConvertLegacyMenuPathToRelative(string path, string rootName)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "/";
            }

            string normalized = path.Trim().Replace('\\', '/');
            while (normalized.IndexOf("//", StringComparison.Ordinal) >= 0)
            {
                normalized = normalized.Replace("//", "/");
            }

            string rootSegment = string.IsNullOrWhiteSpace(rootName) ? "Menu" : rootName.Trim().Trim('/');
            if (!string.IsNullOrEmpty(rootSegment))
            {
                if (string.Equals(normalized, rootSegment, StringComparison.Ordinal))
                {
                    return "/";
                }

                string rootPrefix = rootSegment + "/";
                if (normalized.StartsWith(rootPrefix, StringComparison.Ordinal))
                {
                    return NormalizeMenuPath("/" + normalized.Substring(rootPrefix.Length));
                }
            }

            return NormalizeMenuPath(normalized);
        }

        private static string NormalizeRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                return RootFolder;
            }

            string normalized = root.Trim().Replace('\\', '/');
            if (string.Equals(normalized, "Assets", System.StringComparison.OrdinalIgnoreCase))
            {
                return "Assets";
            }

            if (!normalized.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase))
            {
                return RootFolder;
            }

            while (normalized.Length > 6 && normalized.EndsWith("/", System.StringComparison.Ordinal))
            {
                normalized = normalized.Substring(0, normalized.Length - 1);
            }

            return normalized;
        }

        public static string SanitizeFileName(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            var chars = value.ToCharArray();
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            for (int i = 0; i < chars.Length; i++)
            {
                if (System.Array.IndexOf(invalid, chars[i]) >= 0)
                {
                    chars[i] = '_';
                }
            }

            string result = new string(chars).Trim();
            return string.IsNullOrEmpty(result) ? fallback : result;
        }

        public static string NormalizeMenuPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "/";
            }

            string normalized = path.Trim().Replace('\\', '/');
            while (normalized.IndexOf("//", StringComparison.Ordinal) >= 0)
            {
                normalized = normalized.Replace("//", "/");
            }

            if (!normalized.StartsWith("/", StringComparison.Ordinal))
            {
                int separatorIndex = normalized.IndexOf('/', StringComparison.Ordinal);
                if (separatorIndex < 0 || separatorIndex >= normalized.Length - 1)
                {
                    return "/";
                }

                normalized = "/" + normalized.Substring(separatorIndex + 1).TrimStart('/');
            }

            while (normalized.Length > 1 && normalized.EndsWith("/", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(0, normalized.Length - 1);
            }

            return string.IsNullOrWhiteSpace(normalized) ? "/" : normalized;
        }
    }
}
