using System.Collections.Generic;
using MVA.Toolbox.SwitchGenerator.Utils;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace MVA.Toolbox.SwitchGenerator.Emit.Vrc
{
    internal static class MenuPaginationService
    {
        private const string NextPage = "下一页";
        private const string MorePage = "more";

        public static VRCExpressionsMenu EnsureFreeSlot(VRCExpressionsMenu menu, string assetFolder = null, string baseName = null)
        {
            if (menu == null)
            {
                return null;
            }

            var current = menu;
            while (true)
            {
                current.controls ??= new List<VRCExpressionsMenu.Control>();
                if (current.controls.Count < 8)
                {
                    return current;
                }

                var existing = FindNextPageControl(current);
                if (existing != null && existing.subMenu != null)
                {
                    current = existing.subMenu;
                    continue;
                }

                var nextMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                nextMenu.name = string.IsNullOrWhiteSpace(current.name) ? "Menu_Next" : current.name + "_Next";
                nextMenu.controls = new List<VRCExpressionsMenu.Control>();

                if (!string.IsNullOrWhiteSpace(assetFolder))
                {
                    AvatarAssetResolver.EnsureFolder(assetFolder);
                    string fileBaseName = !string.IsNullOrWhiteSpace(baseName)
                        ? baseName
                        : (!string.IsNullOrWhiteSpace(current.name) ? current.name : "SwitchGen_Menu");
                    fileBaseName = AvatarAssetResolver.SanitizeFileName(fileBaseName, "SwitchGen_Menu");
                    string assetPath = AssetDatabase.GenerateUniqueAssetPath(assetFolder + "/" + fileBaseName + "_Next.asset");
                    AssetDatabase.CreateAsset(nextMenu, assetPath);
                }
                else
                {
                    string parentPath = AssetDatabase.GetAssetPath(current);
                    if (!string.IsNullOrWhiteSpace(parentPath))
                    {
                        AssetDatabase.AddObjectToAsset(nextMenu, current);
                    }
                }

                while (current.controls.Count > 7)
                {
                    var moved = current.controls[7];
                    current.controls.RemoveAt(7);
                    nextMenu.controls.Add(moved);
                }

                current.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = NextPage,
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = nextMenu
                });

                EditorUtility.SetDirty(current);
                EditorUtility.SetDirty(nextMenu);
                current = nextMenu;
            }
        }

        private static VRCExpressionsMenu.Control FindNextPageControl(VRCExpressionsMenu menu)
        {
            if (menu?.controls == null)
            {
                return null;
            }

            for (int i = 0; i < menu.controls.Count; i++)
            {
                var control = menu.controls[i];
                if (control == null)
                {
                    continue;
                }

                if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu &&
                    control.subMenu != null &&
                    (control.name == NextPage || control.name == MorePage))
                {
                    return control;
                }
            }

            return null;
        }
    }
}
