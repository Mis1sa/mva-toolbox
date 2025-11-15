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
        /// <summary>
        /// 可识别为“翻页/更多”用途的子菜单名称列表。
        /// 若需要调整识别的名称，只需修改此列表内容即可。
        /// </summary>
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

            // 同名同类型（Toggle）控件：覆盖参数引用，而不是跳过
            var existing = FindControl(menu, controlName, VRCExpressionsMenu.Control.ControlType.Toggle);
            if (existing != null)
            {
                existing.parameter = new VRCExpressionsMenu.Control.Parameter { name = paramName };
                EditorUtility.SetDirty(menu);
                return;
            }

            // 新建控件时：若当前菜单已满，则尝试使用/创建“下一页”风格的子菜单进行分页
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

            // 同名同类型（SubMenu）控件：覆盖其子菜单引用
            var existing = FindControl(menu, controlName, VRCExpressionsMenu.Control.ControlType.SubMenu);
            if (existing != null)
            {
                existing.subMenu = submenu;
                EditorUtility.SetDirty(menu);
                return;
            }

            // 新建控件时：若当前菜单已满，则尝试使用/创建“下一页”风格的子菜单进行分页
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

            // 同名同类型（RadialPuppet）控件：覆盖其 subParameters 绑定的参数名
            var existing = FindControl(menu, controlName, VRCExpressionsMenu.Control.ControlType.RadialPuppet);
            if (existing != null)
            {
                // 主 parameter 按原设计保留为空字符串，只更新 subParameters[0]
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
                ToolboxUtils.EnsureFolderExists(assetFolder);
                // Int 模式子菜单文件名：使用末级文件夹名（即最终层级名）作为资产名
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

            // 旧逻辑会在此处将 subMenu 作为父菜单 asset 的子资产挂载。
            // 现改为主要通过 CreateSubMenu 或 EnsureMenuHasFreeSlot 在指定目录创建独立 .asset，
            // 因此这里不再强制把已有子菜单挂到当前菜单 asset 下，避免与独立菜单文件冲突。
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

        /// <summary>
        /// 在给定菜单及其“下一页/More”子菜单链中查找第一个仍有空余控件槽位的菜单。
        /// 若当前菜单及整个链路都已满，则在链路末端自动创建/插入“下一页”子菜单，
        /// 并将多余控件移动到新子菜单中，保证返回的菜单至少有 1 个空位。
        /// </summary>
        private VRCExpressionsMenu EnsureMenuHasFreeSlot(VRCExpressionsMenu menu, string assetFolder = null, string baseName = null)
        {
            if (menu == null) return null;

            var current = menu;

            while (true)
            {
                // 当前位置仍有容量，直接使用
                if (HasMenuCapacity(current))
                    return current;

                // 尝试沿着已存在的“下一页/More”子菜单向下寻找可用菜单
                var overflowCtrl = FindExistingOverflowSubMenu(current);
                if (overflowCtrl != null && overflowCtrl.subMenu != null)
                {
                    current = overflowCtrl.subMenu;
                    continue;
                }

                // 当前菜单没有可用的“下一页/More”子菜单，且自身已满：
                // 在此菜单上执行一次分页，将第 8 项改为“下一页”子菜单，并把多余控件移入其中。
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
                    // 方案 B：在指定目录下创建独立的“下一页”菜单 .asset
                    // 为避免多个菜单复用同一个 .asset 造成菜单图出现共享节点（从而导致 TraverseMenu 递归栈溢出），
                    // 每次这里都为当前分页链创建一个全新的 asset，使用 GenerateUniqueAssetPath 保证路径唯一。
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
                    // 保留原来的子资产方式作为兼容路径（例如未提供 assetFolder 的旧调用）
                    childMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                    childMenu.controls = new List<VRCExpressionsMenu.Control>();

                    string parentPath = AssetDatabase.GetAssetPath(current);
                    if (!string.IsNullOrEmpty(parentPath) && !AssetDatabase.IsSubAsset(childMenu))
                    {
                        AssetDatabase.AddObjectToAsset(childMenu, parentPath);
                    }
                }

                // 将第 8 个及之后的控件全部移动到子菜单中，当前菜单保留前 7 个
                while (current.controls.Count > 7)
                {
                    var move = current.controls[7];
                    current.controls.RemoveAt(7);
                    childMenu.controls.Add(move);
                }

                // 创建“下一页”风格的子菜单控件
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

                // 新创建的子菜单此时控件数量可能仍然 >= 8，但上一轮 while 会继续处理，
                // 最终会在链末端得到至少一个拥有空位的菜单。
                current = childMenu;
            }
        }

        /// <summary>
        /// 在给定菜单中查找名称属于 OverflowSubMenuNames 且类型为 SubMenu 的控件。
        /// 若找到，则返回该控件；否则返回 null。
        /// </summary>
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
