using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
using MVA.Toolbox.Public;

namespace MVA.Toolbox.AvatarQuickToggle.Services
{
    public class VRCAssetsService
    {
        // 用于识别“下一页/More”用途的子菜单名称列表
        private static readonly string[] OverflowSubMenuNames =
        {
            "下一页",
            "more"
        };

        public void AddParameter(VRCExpressionParameters parameters, string paramName, VRCExpressionParameters.ValueType type, float defaultValue, bool saved, bool synced)
        {
            if (parameters == null || string.IsNullOrWhiteSpace(paramName)) return;
            var list = parameters.parameters?.ToList() ?? new List<VRCExpressionParameters.Parameter>();
            int index = list.FindIndex(p => p != null && p.name == paramName);
            var newParam = new VRCExpressionParameters.Parameter
            {
                name = paramName,
                valueType = type,
                defaultValue = defaultValue,
                saved = saved,
                networkSynced = synced
            };

            if (index >= 0)
            {
                var existing = list[index];
                int used = GetUsedMemory(parameters) - GetParameterCost(existing.valueType);
                if (used + GetParameterCost(type) > VRCExpressionParameters.MAX_PARAMETER_COST)
                    throw new InvalidOperationException("Not enough parameter memory to overwrite " + paramName);
                list[index] = newParam;
            }
            else
            {
                if (!HasCapacityFor(parameters, type))
                    throw new InvalidOperationException("Not enough parameter memory to add " + paramName);
                list.Add(newParam);
            }

            parameters.parameters = list.ToArray();
            EditorUtility.SetDirty(parameters);
        }

        public void AddBoolMenuControl(VRCExpressionsMenu menu, string controlName, string paramName, string assetFolder = null)
        {
            if (menu == null || string.IsNullOrWhiteSpace(controlName) || string.IsNullOrWhiteSpace(paramName)) return;

            // 已存在同名 Toggle 控件时仅覆盖参数引用
            var existing = FindControl(menu, controlName, VRCExpressionsMenu.Control.ControlType.Toggle);
            if (existing != null)
            {
                existing.parameter = new VRCExpressionsMenu.Control.Parameter { name = paramName };
                EditorUtility.SetDirty(menu);
                return;
            }

            // 新建控件时：若当前菜单已满，则通过 EnsureMenuHasFreeSlot 进行分页
            var targetMenu = EnsureMenuHasFreeSlot(menu, assetFolder, controlName);

            var control = new VRCExpressionsMenu.Control
            {
                name = controlName,
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = paramName }
            };

            AddControlToMenu(targetMenu, control);
        }

        public void AddIntMenuControl(VRCExpressionsMenu menu, string controlName, string paramName, List<string> stateNames, string assetFolder = null)
        {
            if (menu == null || string.IsNullOrWhiteSpace(controlName) || string.IsNullOrWhiteSpace(paramName)) return;

            var submenu = CreateSubMenu(controlName, paramName, stateNames ?? new List<string>(), assetFolder);

            // 已存在同名 SubMenu 控件时仅覆盖其子菜单引用
            var existing = FindControl(menu, controlName, VRCExpressionsMenu.Control.ControlType.SubMenu);
            if (existing != null)
            {
                existing.subMenu = submenu;
                EditorUtility.SetDirty(menu);
                return;
            }

            // 新建控件时：若当前菜单已满，则通过 EnsureMenuHasFreeSlot 进行分页
            var targetMenu = EnsureMenuHasFreeSlot(menu);

            var control = new VRCExpressionsMenu.Control
            {
                name = controlName,
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = submenu
            };

            AddControlToMenu(targetMenu, control);
        }

        public void AddFloatMenuControl(VRCExpressionsMenu menu, string controlName, string paramName, string assetFolder = null)
        {
            if (menu == null || string.IsNullOrWhiteSpace(controlName) || string.IsNullOrWhiteSpace(paramName)) return;

            // 已存在同名 RadialPuppet 控件时覆盖其 subParameters[0] 绑定的参数名
            var existing = FindControl(menu, controlName, VRCExpressionsMenu.Control.ControlType.RadialPuppet);
            if (existing != null)
            {
                // 主 parameter 按原设计保持为空，仅更新 subParameters[0]
                if (existing.subParameters == null || existing.subParameters.Length == 0)
                {
                    existing.subParameters = new[] { new VRCExpressionsMenu.Control.Parameter { name = paramName } };
                }
                else
                {
                    existing.subParameters[0].name = paramName;
                }

                EditorUtility.SetDirty(menu);
                return;
            }

            // 新建控件时：若当前菜单已满，则尝试使用/创建“下一页”风格的子菜单进行分页
            var targetMenu = EnsureMenuHasFreeSlot(menu);

            var control = new VRCExpressionsMenu.Control
            {
                name = controlName,
                type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = string.Empty },
                subParameters = new[] { new VRCExpressionsMenu.Control.Parameter { name = paramName } }
            };

            AddControlToMenu(targetMenu, control);
        }

        public VRCExpressionsMenu CreateSubMenu(string name, string paramName, List<string> stateNames, string assetFolder = null)
        {
            var names = stateNames != null && stateNames.Count > 0 ? stateNames : new List<string> { "State 0" };
            int count = Mathf.Min(names.Count, 8);
            VRCExpressionsMenu submenu;
            string assetPath = null;

            if (!string.IsNullOrEmpty(assetFolder))
            {
                // 使用 ToolboxUtils 确保子菜单保存目录存在（方法定义于 ToolboxUtils.cs）
                ToolboxUtils.EnsureFolderExists(assetFolder);
                // Int 子菜单文件名：默认使用末级文件夹名（即层级名）作为资产名
                string folderName = assetFolder.Replace('\\', '/');
                int idx = folderName.LastIndexOf('/') + 1;
                if (idx >= 0 && idx < folderName.Length)
                    folderName = folderName.Substring(idx);
                if (string.IsNullOrWhiteSpace(folderName))
                    folderName = string.IsNullOrWhiteSpace(name) ? paramName : name;

                assetPath = $"{assetFolder}/{folderName}.asset";
                submenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(assetPath);
                if (submenu == null)
                {
                    submenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                    submenu.controls = new List<VRCExpressionsMenu.Control>(count);
                    AssetDatabase.CreateAsset(submenu, assetPath);
                }
                else
                {
                    if (submenu.controls == null)
                        submenu.controls = new List<VRCExpressionsMenu.Control>(count);
                    else
                        submenu.controls.Clear();
                }
            }
            else
            {
                submenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                submenu.controls = new List<VRCExpressionsMenu.Control>(count);
            }

            for (int i = 0; i < count; i++)
            {
                var control = new VRCExpressionsMenu.Control
                {
                    name = string.IsNullOrEmpty(names[i]) ? $"State {i}" : names[i],
                    type = VRCExpressionsMenu.Control.ControlType.Toggle,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = paramName },
                    value = i
                };
                submenu.controls.Add(control);
            }

            if (!string.IsNullOrEmpty(assetPath))
            {
                EditorUtility.SetDirty(submenu);
                AssetDatabase.SaveAssets();
            }

            return submenu;
        }

        public void AddControlToMenu(VRCExpressionsMenu menu, VRCExpressionsMenu.Control control)
        {
            if (menu == null || control == null) return;
            if (menu.controls == null)
                menu.controls = new List<VRCExpressionsMenu.Control>();
            if (!HasMenuCapacity(menu))
                throw new InvalidOperationException("Menu has no remaining slots.");

            menu.controls.Add(control);
            EditorUtility.SetDirty(menu);

            // subMenu 资产的挂载由 CreateSubMenu/EnsureMenuHasFreeSlot 负责，此处只维护菜单控件列表
        }

        public bool ParameterExists(VRCExpressionParameters parameters, string paramName)
        {
            if (parameters?.parameters == null) return false;
            return parameters.parameters.Any(p => p != null && p.name == paramName);
        }

        private VRCExpressionsMenu.Control FindControl(VRCExpressionsMenu menu, string controlName, VRCExpressionsMenu.Control.ControlType type)
        {
            if (menu?.controls == null) return null;
            for (int i = 0; i < menu.controls.Count; i++)
            {
                var c = menu.controls[i];
                if (c != null && c.name == controlName && c.type == type)
                {
                    return c;
                }
            }
            return null;
        }

        public int GetParameterCost(VRCExpressionParameters.ValueType type)
        {
            switch (type)
            {
                case VRCExpressionParameters.ValueType.Bool:
                    return 1;
                case VRCExpressionParameters.ValueType.Int:
                case VRCExpressionParameters.ValueType.Float:
                    return 8;
                default:
                    return 0;
            }
        }

        public int GetUsedMemory(VRCExpressionParameters parameters)
        {
            if (parameters?.parameters == null) return 0;
            return parameters.parameters.Where(p => p != null).Sum(p => GetParameterCost(p.valueType));
        }

        public bool HasCapacityFor(VRCExpressionParameters parameters, VRCExpressionParameters.ValueType type)
        {
            int used = GetUsedMemory(parameters);
            int cost = GetParameterCost(type);
            return used + cost <= VRCExpressionParameters.MAX_PARAMETER_COST;
        }

        public int GetRemainingMenuSlots(VRCExpressionsMenu menu)
        {
            int count = menu?.controls?.Count ?? 0;
            return Mathf.Clamp(8 - count, 0, 8);
        }

        public bool HasMenuCapacity(VRCExpressionsMenu menu, int needed = 1)
        {
            return GetRemainingMenuSlots(menu) >= needed;
        }

        private VRCExpressionsMenu EnsureMenuHasFreeSlot(VRCExpressionsMenu menu, string assetFolder = null, string baseName = null)
        {
            if (menu == null) return null;

            var current = menu;

            while (true)
            {
                // 当前菜单仍有容量时直接返回
                if (HasMenuCapacity(current))
                    return current;

                // 沿着已存在的“下一页/More”子菜单链向下寻找有空位的菜单
                var overflowCtrl = FindExistingOverflowSubMenu(current);
                if (overflowCtrl != null && overflowCtrl.subMenu != null)
                {
                    current = overflowCtrl.subMenu;
                    continue;
                }

                // 当前菜单没有可用“下一页/More”子菜单且已满：在此菜单上执行一次分页
                if (current.controls == null)
                {
                    current.controls = new List<VRCExpressionsMenu.Control>();
                }

                if (current.controls.Count < 8)
                {
                    // 理论上不会出现（前面已判断 HasMenuCapacity == false），作为容错保留
                    return current;
                }

                // 创建新的子菜单用于承载“多出”的控件
                VRCExpressionsMenu childMenu;

                if (!string.IsNullOrEmpty(assetFolder))
                {
                    // 在指定目录下创建独立的“下一页”菜单 .asset，每次生成唯一 asset，避免多个菜单复用导致菜单图共享节点
                    ToolboxUtils.EnsureFolderExists(assetFolder);

                    string fileBaseName = !string.IsNullOrWhiteSpace(baseName)
                        ? baseName
                        : (!string.IsNullOrWhiteSpace(current.name) ? current.name : "AQT_Menu");

                    // 简单规避非法文件名字符
                    fileBaseName = fileBaseName.Replace('/', '_').Replace('\\', '_');

                    string baseAssetPath = $"{assetFolder}/{fileBaseName}_Next.asset";
                    string uniquePath = AssetDatabase.GenerateUniqueAssetPath(baseAssetPath);

                    childMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                    childMenu.name = System.IO.Path.GetFileNameWithoutExtension(uniquePath);
                    childMenu.controls = new List<VRCExpressionsMenu.Control>();
                    AssetDatabase.CreateAsset(childMenu, uniquePath);
                }
                else
                {
                    // 未提供 assetFolder 时保留旧的子资产方式
                    childMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                    childMenu.controls = new List<VRCExpressionsMenu.Control>();

                    string parentPath = AssetDatabase.GetAssetPath(current);
                    if (!string.IsNullOrEmpty(parentPath) && !AssetDatabase.IsSubAsset(childMenu))
                    {
                        AssetDatabase.AddObjectToAsset(childMenu, parentPath);
                    }
                }

                // 将第 8 个及之后的控件移动到子菜单中，当前菜单只保留前 7 个
                while (current.controls.Count > 7)
                {
                    var move = current.controls[7];
                    current.controls.RemoveAt(7);
                    childMenu.controls.Add(move);
                }

                // 创建“下一页/More”风格的子菜单控件
                var overflowName = OverflowSubMenuNames != null && OverflowSubMenuNames.Length > 0
                    ? OverflowSubMenuNames[0]
                    : "Next";

                var overflowControl = new VRCExpressionsMenu.Control
                {
                    name = overflowName,
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = childMenu
                };

                current.controls.Add(overflowControl);

                EditorUtility.SetDirty(current);
                EditorUtility.SetDirty(childMenu);

                // 若新子菜单仍已满，下一轮 while 会继续处理，最终在链末端得到至少一个有空位的菜单
                current = childMenu;
            }
        }

        private VRCExpressionsMenu.Control FindExistingOverflowSubMenu(VRCExpressionsMenu menu)
        {
            if (menu?.controls == null || OverflowSubMenuNames == null || OverflowSubMenuNames.Length == 0)
                return null;

            for (int i = 0; i < menu.controls.Count; i++)
            {
                var c = menu.controls[i];
                if (c == null) continue;
                if (c.type != VRCExpressionsMenu.Control.ControlType.SubMenu) continue;

                for (int j = 0; j < OverflowSubMenuNames.Length; j++)
                {
                    var name = OverflowSubMenuNames[j];
                    if (!string.IsNullOrEmpty(name) && string.Equals(c.name, name, StringComparison.Ordinal))
                    {
                        return c;
                    }
                }
            }

            return null;
        }
    }
}
