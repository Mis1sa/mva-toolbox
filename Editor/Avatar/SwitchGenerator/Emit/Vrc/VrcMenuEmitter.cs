using System.Collections.Generic;
using MVA.Toolbox.SwitchGenerator.Utils;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace MVA.Toolbox.SwitchGenerator.Emit.Vrc
{
    internal static class VrcMenuEmitter
    {
        public static void Upsert(VRCExpressionsMenu rootMenu, Compile.SwitchLayerPlan layer)
        {
            if (rootMenu == null || layer == null || !layer.generateMenuControl)
            {
                return;
            }

            var targetMenu = ResolveMenu(rootMenu, layer.menuPath) ?? rootMenu;
            string menuAssetFolder = layer.persistAssets
                ? AvatarAssetResolver.BuildLayerFolder(layer.clipSaveRoot, layer.layerName)
                : null;
            switch (layer.switchType)
            {
                case SwitchGeneratorConfig.SwitchType.Bool:
                    UpsertBool(targetMenu, layer.menuControlName, layer.parameterName, menuAssetFolder);
                    break;
                case SwitchGeneratorConfig.SwitchType.Int:
                    UpsertInt(targetMenu, layer, menuAssetFolder);
                    break;
                case SwitchGeneratorConfig.SwitchType.Float:
                    UpsertFloat(targetMenu, layer.menuControlName, layer.parameterName, menuAssetFolder);
                    break;
            }

            EditorUtility.SetDirty(rootMenu);
        }

        private static VRCExpressionsMenu ResolveMenu(VRCExpressionsMenu root, string path)
        {
            if (root == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(path) || path == "/")
            {
                return root;
            }

            var map = AvatarAssetResolver.BuildMenuMap(root);
            if (map.TryGetValue(path, out var menu))
            {
                return menu;
            }

            return root;
        }

        private static void UpsertBool(VRCExpressionsMenu menu, string controlName, string parameterName, string assetFolder)
        {
            var existing = FindControl(menu, controlName, VRCExpressionsMenu.Control.ControlType.Toggle);
            if (existing != null)
            {
                existing.parameter = new VRCExpressionsMenu.Control.Parameter { name = parameterName };
                return;
            }

            var target = MenuPaginationService.EnsureFreeSlot(menu, assetFolder, controlName);
            target.controls.Add(new VRCExpressionsMenu.Control
            {
                name = controlName,
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = parameterName }
            });
        }

        private static void UpsertInt(VRCExpressionsMenu menu, Compile.SwitchLayerPlan layer, string assetFolder)
        {
            var subMenu = BuildIntSubMenu(layer, assetFolder);
            if (string.IsNullOrWhiteSpace(assetFolder))
            {
                string menuAssetPath = AssetDatabase.GetAssetPath(menu);
                if (!string.IsNullOrWhiteSpace(menuAssetPath))
                {
                    AssetDatabase.AddObjectToAsset(subMenu, menu);
                }
            }

            var existing = FindControl(menu, layer.menuControlName, VRCExpressionsMenu.Control.ControlType.SubMenu);
            if (existing != null)
            {
                existing.subMenu = subMenu;
                return;
            }

            var target = MenuPaginationService.EnsureFreeSlot(menu, assetFolder, layer.menuControlName);
            target.controls.Add(new VRCExpressionsMenu.Control
            {
                name = layer.menuControlName,
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = subMenu
            });
        }

        private static VRCExpressionsMenu BuildIntSubMenu(Compile.SwitchLayerPlan layer, string assetFolder)
        {
            VRCExpressionsMenu menu;
            if (!string.IsNullOrWhiteSpace(assetFolder))
            {
                AvatarAssetResolver.EnsureFolder(assetFolder);
                string folderName = assetFolder.Replace('\\', '/');
                int lastSlash = folderName.LastIndexOf('/') + 1;
                if (lastSlash > 0 && lastSlash < folderName.Length)
                {
                    folderName = folderName.Substring(lastSlash);
                }

                folderName = AvatarAssetResolver.SanitizeFileName(folderName, AvatarAssetResolver.SanitizeFileName(layer.layerName, "SwitchGen_Menu"));
                string assetPath = assetFolder + "/" + folderName + ".asset";
                menu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(assetPath);
                if (menu == null)
                {
                    menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                    menu.name = folderName;
                    menu.controls = new List<VRCExpressionsMenu.Control>();
                    AssetDatabase.CreateAsset(menu, assetPath);
                }
                else
                {
                    menu.controls ??= new List<VRCExpressionsMenu.Control>();
                    menu.controls.Clear();
                }
            }
            else
            {
                menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                menu.name = layer.menuControlName + "_Sub";
                menu.controls = new List<VRCExpressionsMenu.Control>();
            }

            int count = Mathf.Max(1, layer.intGroups.Count);
            for (int i = 0; i < count; i++)
            {
                string name = (i < layer.intMenuItemNames.Count && !string.IsNullOrWhiteSpace(layer.intMenuItemNames[i]))
                    ? layer.intMenuItemNames[i]
                    : layer.layerName + "_" + i;

                var target = MenuPaginationService.EnsureFreeSlot(menu, assetFolder, layer.menuControlName);
                target.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = name,
                    type = VRCExpressionsMenu.Control.ControlType.Toggle,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = layer.parameterName },
                    value = i
                });
            }

            return menu;
        }

        private static void UpsertFloat(VRCExpressionsMenu menu, string controlName, string parameterName, string assetFolder)
        {
            var existing = FindControl(menu, controlName, VRCExpressionsMenu.Control.ControlType.RadialPuppet);
            if (existing != null)
            {
                existing.subParameters = new[]
                {
                    new VRCExpressionsMenu.Control.Parameter { name = parameterName }
                };
                return;
            }

            var target = MenuPaginationService.EnsureFreeSlot(menu, assetFolder, controlName);
            target.controls.Add(new VRCExpressionsMenu.Control
            {
                name = controlName,
                type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = string.Empty },
                subParameters = new[]
                {
                    new VRCExpressionsMenu.Control.Parameter { name = parameterName }
                }
            });
        }

        private static VRCExpressionsMenu.Control FindControl(VRCExpressionsMenu menu, string name, VRCExpressionsMenu.Control.ControlType type)
        {
            if (menu?.controls == null)
            {
                return null;
            }

            for (int i = 0; i < menu.controls.Count; i++)
            {
                var control = menu.controls[i];
                if (control != null && control.type == type && control.name == name)
                {
                    return control;
                }
            }

            return null;
        }
    }
}
