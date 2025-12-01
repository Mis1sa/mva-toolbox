using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using System.Linq;
using MVA.Toolbox.AnimPathRedirect.Services;
using MVA.Toolbox.Public;

namespace MVA.Toolbox.AnimPathRedirect.UI
{
    // Anim Path Redirect 主窗口（IMGUI），核心逻辑由 AnimPathRedirectService 提供（见 AnimPathRedirectService.cs）
    public sealed class AnimPathRedirectWindow : EditorWindow
    {
        // APR 主服务实例
        AnimPathRedirectService _service;
        // 主滚动视图（使用公共工具 ToolboxUtils.ScrollView，定义于 MVA.Toolbox.Public.ToolboxUtils）
        Vector2 _scroll;

        GUIStyle _missingLabelStyle;
        GUIStyle _removedLabelStyle;
        GUIStyle _changedLabelStyle;
        GUIStyle _pendingStatusStyle;
        GUIStyle _fixedStatusStyle;
        GUIStyle _wrapLabelStyle;
        GUIStyle _wrapMiniLabelStyle;

        [MenuItem("Tools/MVA Toolbox/Anim Path Redirect", false, 9)]
        public static void Open()
        {
            var w = GetWindow<AnimPathRedirectWindow>("Anim Path Redirect");
            w.minSize = new Vector2(560f, 600f);
        }

        void OnEnable()
        {
            if (_service == null)
            {
                _service = new AnimPathRedirectService();
            }
        }

        void OnHierarchyChange()
        {
            _service?.OnHierarchyChanged();
            Repaint();
        }

        // 懒加载 GUIStyle，避免在 OnGUI 中频繁分配样式对象
        void EnsureStyles()
        {
            if (_missingLabelStyle == null)
            {
                _missingLabelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    normal = { textColor = Color.red }
                };
            }

            if (_removedLabelStyle == null)
            {
                _removedLabelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    normal = { textColor = new Color(1f, 0.6f, 0f) }
                };
            }

            if (_changedLabelStyle == null)
            {
                _changedLabelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    normal = { textColor = new Color(1f, 0.9f, 0f) }
                };
            }

            if (_pendingStatusStyle == null)
            {
                _pendingStatusStyle = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(1f, 0.9f, 0f) }
                };
            }

            if (_fixedStatusStyle == null)
            {
                _fixedStatusStyle = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.green }
                };
            }

            if (_wrapLabelStyle == null)
            {
                _wrapLabelStyle = new GUIStyle(EditorStyles.label)
                {
                    wordWrap = true
                };
            }

            if (_wrapMiniLabelStyle == null)
            {
                _wrapMiniLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    wordWrap = true
                };
            }
        }

        void OnGUI()
        {
            if (_service == null)
            {
                _service = new AnimPathRedirectService();
            }

            EnsureStyles();

            _scroll = ToolboxUtils.ScrollView(_scroll, () =>
            {
                DrawTargetSelection();

                GUILayout.Space(4f);

                if (_service.TargetRoot == null)
                {
                    EditorGUILayout.HelpBox("请拖入一个 Avatar 或带 Animator 组件的物体。", MessageType.Info);
                    return;
                }

                DrawControllerAndLayerSelection();

                GUILayout.Space(4f);

                DrawTrackingAndResults();
            });
        }

        void DrawTargetSelection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("目标对象", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var newTarget = (GameObject)EditorGUILayout.ObjectField("Avatar / 带 Animator 的物体", _service.TargetRoot, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                if (newTarget != null && (ToolboxUtils.IsAvatarRoot(newTarget) || ToolboxUtils.HasAnimator(newTarget)))
                {
                    _service.SetTarget(newTarget);
                }
                else
                {
                    _service.SetTarget(null);
                }
            }

            EditorGUILayout.EndVertical();
        }

        void DrawControllerAndLayerSelection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            var controllers = _service.Controllers;
            var names = _service.ControllerNames;

            if (controllers.Count == 0)
            {
                EditorGUILayout.HelpBox("在目标对象中未找到任何 AnimatorController。", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUI.BeginDisabledGroup(_service.HasSnapshot);

            // 控制器选择
            int index = _service.SelectedControllerIndex;
            if (index < 0 || index >= names.Count)
            {
                index = 0;
            }

            string[] display = names.ToArray();
            index = EditorGUILayout.Popup("动画控制器", index, display);
            _service.SelectedControllerIndex = index;

            var controller = _service.SelectedController;
            string[] layerOptions;
            if (controller != null)
            {
                layerOptions = controller.layers
                    .Select(l => string.IsNullOrEmpty(l.name) ? "Layer" : l.name)
                    .Prepend("全部层级 (ALL)")
                    .ToArray();
            }
            else
            {
                layerOptions = new[] { "全部层级" };
            }

            int layerIndex = _service.SelectedLayerIndex;
            layerIndex = Mathf.Clamp(layerIndex, 0, Mathf.Max(0, layerOptions.Length - 1));
            layerIndex = EditorGUILayout.Popup("层级范围", layerIndex, layerOptions);
            _service.SelectedLayerIndex = layerIndex;

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndVertical();
        }

        // 追踪入口与结果汇总区域（调用 AnimPathRedirectService 的核心方法）
        void DrawTrackingAndResults()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();

            bool hasController = _service.SelectedController != null;
            GUI.enabled = hasController;

            string trackButtonLabel = _service.HasSnapshot
                ? "退出追踪"
                : "开始追踪";

            if (GUILayout.Button(trackButtonLabel, GUILayout.Height(30f)))
            {
                if (_service.HasSnapshot)
                {
                    _service.ClearTracking();
                }
                else
                {
                    _service.StartTrackingSnapshot();
                }
            }

            GUI.enabled = true;

            int totalMissingCount = _service.MissingGroups
                .Sum(g => g.CurvesByType.Sum(kvp => kvp.Value.Count(c => !c.IsMarkedForRemoval)));
            int ignorablesCount = _service.MissingGroups
                .Count(g => g.FixTarget == null && !g.IsEmpty);

            GUI.enabled = _service.MissingGroups.Count > 0;
            string ignoreButtonText = _service.IgnoreAllMissing
                ? $"已忽略 ({ignorablesCount} 项)"
                : "忽略所有未处理缺失";

            if (GUILayout.Button(ignoreButtonText, GUILayout.Height(30f)))
            {
                _service.IgnoreAllMissing = !_service.IgnoreAllMissing;
                Repaint();
            }

            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8f);

            if (!_service.HasSnapshot)
            {
                EditorGUILayout.HelpBox("点击“开始追踪”记录当前动画曲线的路径快照，然后在层级中修改物体结构，最后返回此处进行重定向/修复。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            _service.CalculateCurrentPaths();

            int pathChangeCount = _service.PathChangeGroups.Count(d => d.IsDeleted || d.HasPathChanged);

            int componentChangeCount = _service.ComponentChangeGroups
                .Sum(g => g.Bindings.Count);

            int activeMissingCount = _service.MissingGroups.Sum(g =>
                (g.OwnerDeleted || (_service.IgnoreAllMissing && g.FixTarget == null))
                    ? 0
                    : g.CurvesByType.Sum(kvp => kvp.Value.Count(c => !c.IsMarkedForRemoval))
            );

            int removalOnlyCount = _service.MissingGroups
                .Sum(g => g.CurvesByType.Sum(kvp => kvp.Value.Count(c => c.IsMarkedForRemoval)));

            int totalChanges = pathChangeCount + componentChangeCount + activeMissingCount + removalOnlyCount;

            if (pathChangeCount > 0 && !_service.HierarchyChanged)
            {
                _service.HierarchyChanged = true;
            }

            if (_service.HierarchyChanged)
            {
                EditorGUILayout.HelpBox($"层级结构已被修改。路径变动/删除：{pathChangeCount} 组，组件变更：{componentChangeCount} 条，缺失绑定（待处理）：{activeMissingCount} 条，标记移除：{removalOnlyCount} 条。", MessageType.Warning);
            }

            EditorGUILayout.LabelField($"追踪中... 总变动/缺失量: {totalChanges}", EditorStyles.boldLabel);

            DrawMissingBindingsContent();
            DrawPathChangesContent();

            GUILayout.FlexibleSpace();

            bool canApply = totalChanges > 0;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            bool needsComponentFix = _service.MissingGroups
                .Where(g => g.FixTarget != null && !g.IsEmpty)
                .Any(g => !ValidateFixTargetComponentsForDisplay(g));

            if (needsComponentFix)
            {
                EditorGUILayout.HelpBox("部分缺失绑定的修复目标缺少必要的组件，请移除曲线或替换修复目标。", MessageType.Error);
                canApply = false;
            }

            GUI.enabled = canApply;
            if (GUILayout.Button("应用修改", GUILayout.Height(40f)))
            {
                var (modified, fixedCount, removed) = _service.ApplyRedirects();
                GUI.enabled = true;
                EditorUtility.DisplayDialog("完成",
                    $"路径修正完成！\n重定向: {modified}\n修复: {fixedCount}\n移除: {removed}",
                    "确定");
                Repaint();
            }

            GUI.enabled = true;
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndVertical();
        }

        #region 缺失与路径变化展示

        void DrawPathChangesContent()
        {
            // 若某个缺失组已经指定了 FixTarget，则在 UI 上暂时隐藏其“原物体”的路径/组件变更，
            // 仅在 Service 内部继续追踪这些变更，直到 FixTarget 被清除或应用。
            var hiddenOriginalPaths = _service.MissingGroups
                .Where(g => g.FixTarget != null && !g.OwnerDeleted)
                .Select(g => g.OldPath)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .ToHashSet();

            // 物体级路径变更/移除
            var objectGroups = _service.PathChangeGroups
                .Where(d => d.IsDeleted || d.HasPathChanged)
                .OrderByDescending(d => d.IsDeleted)
                .ThenByDescending(d => d.HasPathChanged);

            foreach (var data in objectGroups)
            {
                // 当该路径上的缺失已挂到新物体(FixTarget)上时，隐藏原物体的变更展示
                if (!string.IsNullOrEmpty(data.OldPath) && hiddenOriginalPaths.Contains(data.OldPath))
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();

                if (data.IsDeleted)
                {
                    GUILayout.Label("移除", _removedLabelStyle, GUILayout.Width(50f));
                    GUILayout.Label($"物体: {data.TargetObjectName}", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                }
                else if (data.HasPathChanged)
                {
                    string oldName = System.IO.Path.GetFileName(data.OldPath);
                    string newName = System.IO.Path.GetFileName(data.NewPath);

                    if (oldName != newName)
                    {
                        GUILayout.Label("名称变更", _changedLabelStyle, GUILayout.Width(80f));
                        GUILayout.Label($"'{oldName}' -> '{newName}'", EditorStyles.boldLabel);
                    }
                    else
                    {
                        GUILayout.Label("路径变更", _changedLabelStyle, GUILayout.Width(80f));
                        GUILayout.Label($"物体: {data.TargetObjectName}", EditorStyles.boldLabel);
                    }
                }

                EditorGUILayout.EndHorizontal();

                if (data.HasPathChanged)
                {
                    GUILayout.Label($"旧路径: {data.OldPath}", _wrapLabelStyle);
                    GUILayout.Label($"新路径: {data.NewPath}", _wrapLabelStyle);
                }
                else if (data.IsDeleted)
                {
                    GUILayout.Label($"原路径: {data.OldPath}", _wrapLabelStyle);
                }

                int totalCurveCount = data.Bindings.Count;
                var componentTypes = data.Bindings.Select(b => b.type.Name).Distinct();
                GUILayout.Label($"涉及类型: {string.Join(", ", componentTypes)} (共 {totalCurveCount} 条曲线)", _wrapMiniLabelStyle);

                EditorGUILayout.EndVertical();
            }

            // 组件级变更（当前仅包含“组件移除”）
            var componentGroups = _service.ComponentChangeGroups
                .Where(g => g.IsRemoved && g.Bindings.Count > 0)
                .OrderBy(g => g.Path)
                .ThenBy(g => g.ComponentName);

            foreach (var group in componentGroups)
            {
                // 同样，当该路径上的缺失已指定 FixTarget 时，在 UI 上暂时隐藏原物体的组件移除记录
                if (!string.IsNullOrEmpty(group.Path) && hiddenOriginalPaths.Contains(group.Path))
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();

                GUILayout.Label("组件移除", _removedLabelStyle, GUILayout.Width(70f));
                GUILayout.Label($"物体: {group.TargetObjectName}", EditorStyles.boldLabel);
                GUILayout.Label($"组件: {group.ComponentName}", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                EditorGUILayout.EndHorizontal();

                GUILayout.Label($"路径: {group.Path}", _wrapLabelStyle);

                int totalCurveCount = group.Bindings.Count;
                var propSamples = group.Bindings
                    .Select(b => b.propertyName)
                    .Distinct()
                    .Take(3)
                    .ToArray();
                string props = propSamples.Length > 0 ? string.Join(", ", propSamples) : "(属性未知)";

                GUILayout.Label($"涉及属性: {props} (共 {totalCurveCount} 条曲线)", _wrapMiniLabelStyle);

                EditorGUILayout.EndVertical();
            }
        }

        void DrawMissingBindingsContent()
        {
            var groups = _service.MissingGroups
                // 排除所属物体已被删除的缺失组，仅在物体存在时展示其缺失组件
                .Where(g => !g.OwnerDeleted && !g.IsEmpty && !(_service.IgnoreAllMissing && g.FixTarget == null))
                .OrderBy(g => g.IsFixed)
                .ToList();

            if (groups.Count == 0)
            {
                return;
            }

            GUILayout.Label("--- 缺失绑定 ---", EditorStyles.centeredGreyMiniLabel);

            foreach (var group in groups)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                var root = _service.TargetRoot;
                string displayName = group.TargetObjectName;

                // 默认使用当前路径（例如物体被重命名/移动后），否则退回原始路径
                string displayPath = string.IsNullOrEmpty(group.CurrentPath) ? group.OldPath : group.CurrentPath;

                // 若指定了 FixTarget，则在显示上将缺失项“挂靠”到新物体：
                // 使用 FixTarget 对应物体的名称和相对路径进行展示，
                // 同时保持原物体的路径/组件变更在 Service 内部继续计算。
                if (group.FixTarget != null && root != null)
                {
                    var fixGo = group.FixTarget as GameObject ?? (group.FixTarget as Component)?.gameObject;
                    if (fixGo != null)
                    {
                        displayName = fixGo.name;

                        if (fixGo == root)
                        {
                            displayPath = string.Empty;
                        }
                        else
                        {
                            var fixPath = AnimationUtility.CalculateTransformPath(fixGo.transform, root.transform);
                            if (fixPath != null)
                            {
                                displayPath = fixPath;
                            }
                        }
                    }
                }

                GUILayout.Label($"缺失: {displayName}", _missingLabelStyle);
                GUILayout.Label($"路径: {displayPath}", _wrapLabelStyle);

                // 修复目标
                EditorGUI.BeginChangeCheck();
                var newFixTarget = EditorGUILayout.ObjectField("新物体/组件", group.FixTarget, typeof(UnityEngine.Object), true);
                if (EditorGUI.EndChangeCheck())
                {
                    var targetRoot = _service.TargetRoot;
                    bool isRoot = false;

                    if (newFixTarget is GameObject go)
                    {
                        isRoot = go == targetRoot;
                    }
                    else if (newFixTarget is Component comp)
                    {
                        isRoot = comp.gameObject == targetRoot;
                    }

                    group.FixTarget = isRoot ? null : newFixTarget;

                    // 若 FixTarget 被清空（包括被判定为根而置为 null），强制重置该组中
                    // 所有未被标记移除条目的 IsFixedByGroup，以便状态从“待修复”恢复为“未处理”。
                    if (group.FixTarget == null)
                    {
                        foreach (var entry in group.CurvesByType.SelectMany(kvp => kvp.Value))
                        {
                            if (!entry.IsMarkedForRemoval)
                            {
                                entry.IsFixedByGroup = false;
                            }
                        }
                    }

                    _service.UpdateFixTargetStatus(group);
                }

                // 按类型分组的缺失属性
                group.IsExpanded = EditorGUILayout.Foldout(group.IsExpanded, "缺失属性列表", true);
                if (group.IsExpanded)
                {
                    EditorGUILayout.BeginVertical("box");

                    foreach (var kvp in group.CurvesByType)
                    {
                        var type = kvp.Key;
                        var curves = kvp.Value;

                        var activeCurves = curves.Where(c => !c.IsMarkedForRemoval).ToList();
                        if (activeCurves.Count == 0)
                        {
                            continue;
                        }

                        // 聚合：同一组件类型 + 同一属性键（GroupName 或 propertyName）只显示一行
                        var aggregated = activeCurves
                            .GroupBy(GetAggregationKeyForProperty)
                            .ToList();

                        int totalCount = aggregated.Sum(g => g.Count());
                        EditorGUILayout.LabelField($"[{type.Name}] 属性组 (共 {totalCount} 条未处理)", EditorStyles.boldLabel);

                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                        if (totalCount >= 2)
                        {
                            if (GUILayout.Button("全部移除", GUILayout.Height(20f)))
                            {
                                foreach (var c in curves)
                                {
                                    c.IsMarkedForRemoval = true;
                                    c.IsFixedByGroup = false;
                                }

                                _service.UpdateFixTargetStatus(group);

                                EditorGUILayout.EndVertical();
                                continue;
                            }
                        }

                        // 为了避免不同类型属性交错显示，对聚合结果按“属性类别”排序
                        var orderedAggregated = aggregated
                            .OrderBy(g => GetCategoryOrder(g.First()))
                            .ThenBy(g => GetDisplayNameForProperty(g.First()))
                            .ToList();

                        foreach (var propGroup in orderedAggregated)
                        {
                            var rep = propGroup.First();

                            string labelText;

                            // BlendShape: 始终显示 [BlendShape] + 具体名称
                            if (rep.IsBlendshape)
                            {
                                string name = GetDisplayNameForProperty(rep);
                                labelText = $"[BlendShape] {name}";
                            }
                            else
                            {
                                // xyz/rgba 组件属性：m_testName.x/y/z -> [m_testName]
                                var propName = rep.Binding.propertyName ?? string.Empty;
                                string baseName = GetComponentBaseName(propName);
                                string shortBaseName = GetShortName(baseName);
                                labelText = $"[{shortBaseName}]";
                            }

                            EditorGUILayout.BeginHorizontal();
                            GUILayout.Label(labelText, EditorStyles.boldLabel);

                            var entries = propGroup.ToList();

                            // 左侧：可选的 BlendShape 下拉
                            if (rep.IsBlendshape && group.FixTarget != null && ValidateHasRequiredComponent(group, type))
                            {
                                DrawBlendShapeSelectionForGroup(entries);
                            }

                            // 中间拉伸空白，将状态与按钮推到最右侧
                            GUILayout.FlexibleSpace();

                            // 状态提示：未处理(黄) / 待修复(绿)
                            bool hasActive = entries.Any(e => !e.IsMarkedForRemoval);
                            // 只有在存在有效 FixTarget 且所有条目均标记为 IsFixedByGroup 时，才视为“待修复”
                            bool allFixed = hasActive
                                && group.FixTarget != null
                                && entries.All(e => e.IsFixedByGroup && !e.IsMarkedForRemoval);

                            if (allFixed)
                            {
                                GUILayout.Label("待修复", _fixedStatusStyle);
                            }
                            else
                            {
                                GUILayout.Label("未处理", _pendingStatusStyle);
                            }

                            if (GUILayout.Button("-", GUILayout.Width(EditorGUIUtility.singleLineHeight), GUILayout.Height(EditorGUIUtility.singleLineHeight)))
                            {
                                foreach (var c in propGroup)
                                {
                                    c.IsMarkedForRemoval = true;
                                    c.IsFixedByGroup = false;
                                }

                                _service.UpdateFixTargetStatus(group);
                            }

                            EditorGUILayout.EndHorizontal();
                        }

                        EditorGUILayout.EndVertical();
                    }

                    EditorGUILayout.EndVertical();
                }

                EditorGUILayout.EndVertical();
            }
        }

        string GetDisplayNameForProperty(AnimPathRedirectService.MissingCurveEntry entry)
        {
            var name = entry.Binding.propertyName ?? string.Empty;
            int dot = name.LastIndexOf('.');
            return dot >= 0 && dot < name.Length - 1 ? name.Substring(dot + 1) : name;
        }

        string GetAggregationKeyForProperty(AnimPathRedirectService.MissingCurveEntry entry)
        {
            string prop = entry.Binding.propertyName ?? string.Empty;
            return GetComponentBaseName(prop);
        }

        int GetCategoryOrder(AnimPathRedirectService.MissingCurveEntry entry)
        {
            // 排序优先级：BlendShape(0) -> 含 xyz/rgba 分量的属性(1) -> 其他(2)
            if (entry.IsBlendshape)
            {
                return 0;
            }

            var prop = entry.Binding.propertyName ?? string.Empty;
            if (IsComponentProperty(prop))
            {
                return 1;
            }

            return 2;
        }

        string GetComponentBaseName(string prop)
        {
            if (string.IsNullOrEmpty(prop)) return string.Empty;

            if (prop.EndsWith(".x") || prop.EndsWith(".y") || prop.EndsWith(".z") || prop.EndsWith(".w") ||
                prop.EndsWith(".r") || prop.EndsWith(".g") || prop.EndsWith(".b") || prop.EndsWith(".a"))
            {
                if (prop.Length > 2)
                {
                    return prop.Substring(0, prop.Length - 2);
                }
            }

            return prop;
        }

        bool IsComponentProperty(string prop)
        {
            if (string.IsNullOrEmpty(prop)) return false;
            return prop.EndsWith(".x") || prop.EndsWith(".y") || prop.EndsWith(".z") || prop.EndsWith(".w") ||
                   prop.EndsWith(".r") || prop.EndsWith(".g") || prop.EndsWith(".b") || prop.EndsWith(".a");
        }

        string GetShortName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            int dot = name.LastIndexOf('.');
            return dot >= 0 && dot < name.Length - 1 ? name.Substring(dot + 1) : name;
        }

        void DrawBlendShapeSelectionForGroup(System.Collections.Generic.List<AnimPathRedirectService.MissingCurveEntry> entries)
        {
            if (entries == null || entries.Count == 0) return;
            var first = entries[0];
            if (first.AvailableBlendshapes.Count == 0) return;

            string current = first.NewBlendshapeName;
            int index = first.AvailableBlendshapes.IndexOf(current);
            if (index < 0) index = 0;

            int newIndex = EditorGUILayout.Popup(index, first.AvailableBlendshapes.ToArray(), GUILayout.Width(150f));
            if (newIndex != index && newIndex >= 0 && newIndex < first.AvailableBlendshapes.Count)
            {
                string selected = first.AvailableBlendshapes[newIndex];
                foreach (var e in entries)
                {
                    e.NewBlendshapeName = selected;
                }
            }
        }

        bool ValidateFixTargetComponentsForDisplay(AnimPathRedirectService.MissingObjectGroup group)
        {
            var requiredTypes = group.RequiredTypes;
            if (group.FixTarget == null) return requiredTypes.Count == 0;

            foreach (var type in requiredTypes)
            {
                if (!ValidateHasRequiredComponent(group, type))
                {
                    return false;
                }
            }

            return true;
        }

        bool ValidateHasRequiredComponent(AnimPathRedirectService.MissingObjectGroup group, System.Type requiredType)
        {
            if (requiredType == typeof(GameObject) || requiredType == typeof(Transform)) return true;

            var go = group.FixTarget as GameObject ?? (group.FixTarget as Component)?.gameObject;
            if (go == null) return false;

            return go.GetComponent(requiredType) != null;
        }

        #endregion
    }
}
