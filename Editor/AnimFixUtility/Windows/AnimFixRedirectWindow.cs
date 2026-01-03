using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MVA.Toolbox.AnimPathRedirect.Services;
using MVA.Toolbox.AnimFixUtility.Services;
using MVA.Toolbox.Public;

namespace MVA.Toolbox.AnimFixUtility.Windows
{
    // Anim Path Redirect 面板（IMGUI），由 AnimFix Utility 主窗口托管
    public sealed class AnimFixRedirectWindow : IDisposable
    {
        private readonly AnimFixUtilityContext _context;
        private readonly AnimPathRedirectService _service;
        private readonly AnimFixRedirectPresentationService _presentation;
        private readonly Action _repaint;

        // 主滚动视图
        private Vector2 _scroll;

        GUIStyle _missingLabelStyle;
        GUIStyle _removedLabelStyle;
        GUIStyle _changedLabelStyle;
        GUIStyle _pendingStatusStyle;
        GUIStyle _fixedStatusStyle;
        GUIStyle _wrapLabelStyle;
        GUIStyle _wrapMiniLabelStyle;

        public AnimFixRedirectWindow(AnimFixUtilityContext context, Action repaint)
        {
            _context = context;
            _repaint = repaint;
            _service = new AnimPathRedirectService();
            _presentation = new AnimFixRedirectPresentationService(_service);
            EditorApplication.hierarchyChanged += HandleHierarchyChanged;
        }

        public bool IsTracking => _service.HasSnapshot;

        public void Dispose()
        {
            EditorApplication.hierarchyChanged -= HandleHierarchyChanged;
        }

        private void HandleHierarchyChanged()
        {
            _service?.OnHierarchyChanged();
            _repaint?.Invoke();
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

        public void OnGUI()
        {
            EnsureStyles();

            _service.SetTarget(_context.TargetRoot);

            if (_service.TargetRoot == null)
            {
                EditorGUILayout.HelpBox("请先在顶部选择 Avatar 或带 Animator 组件的物体。", MessageType.Info);
                return;
            }

            SyncControllerSelection();

            _scroll = ToolboxUtils.ScrollView(_scroll, DrawTrackingAndResults);
        }

        void SyncControllerSelection()
        {
            _presentation.SyncSelectionWithContext(_context);
        }

        // 追踪入口与结果汇总区域（调用 AnimPathRedirectService 的核心方法）
        void DrawTrackingAndResults()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();

            bool hasController = _service.SelectedController != null;
            GUI.enabled = hasController;

            string trackButtonLabel = _service.HasSnapshot ? "退出追踪" : "开始追踪";

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

            var summary = _presentation.BuildSummary();

            GUI.enabled = _service.MissingGroups.Count > 0;
            string ignoreButtonText = _service.IgnoreAllMissing
                ? $"已忽略 ({summary.IgnorablesCount} 项)"
                : "忽略所有未处理缺失";

            if (GUILayout.Button(ignoreButtonText, GUILayout.Height(30f)))
            {
                _service.IgnoreAllMissing = !_service.IgnoreAllMissing;
                _repaint?.Invoke();
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
            summary = _presentation.BuildSummary();

            if (summary.ShowHierarchyWarning)
            {
                EditorGUILayout.HelpBox(
                    $"层级结构已被修改。路径变动/删除：{summary.PathChangeCount} 组，组件变更：{summary.ComponentChangeCount} 条，缺失绑定（待处理）：{summary.ActiveMissingCount} 条，标记移除：{summary.RemovalOnlyCount} 条。",
                    MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"追踪中... 总变动/缺失量: {summary.TotalChanges}", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            GUI.enabled = summary.CanAutoMatch;
            if (GUILayout.Button("自动匹配", GUILayout.Width(100f)))
            {
                var (matched, ambiguous, invalid) = _service.AutoMatchMissingFixTargets();
                GUI.enabled = true;
                EditorUtility.DisplayDialog(
                    "自动匹配完成",
                    $"成功匹配：{matched}\n跳过（重名/不唯一）：{ambiguous}\n跳过（组件不满足）：{invalid}",
                    "确定");
                _repaint?.Invoke();
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            DrawMissingBindingsContent();
            DrawPathChangesContent();

            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (summary.NeedsComponentFix)
            {
                EditorGUILayout.HelpBox("部分缺失绑定的修复目标缺少必要的组件，请移除曲线或替换修复目标。", MessageType.Error);
            }

            GUI.enabled = summary.CanApply;
            if (GUILayout.Button("应用修改", GUILayout.Height(40f)))
            {
                var (modified, fixedCount, removed) = _service.ApplyRedirects();
                GUI.enabled = true;
                EditorUtility.DisplayDialog("完成",
                    $"路径修正完成！\n重定向: {modified}\n修复: {fixedCount}\n移除: {removed}",
                    "确定");
                _repaint?.Invoke();
            }

            GUI.enabled = true;
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndVertical();
        }

        #region 缺失与路径变化展示

        void DrawPathChangesContent()
        {
            var pathGroups = _presentation.GetVisiblePathChangeGroups();

            foreach (var data in pathGroups)
            {
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

            var componentGroups = _presentation.GetVisibleComponentChangeGroups();

            foreach (var group in componentGroups)
            {
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
            var groups = _presentation.GetVisibleMissingGroups();
            if (groups.Count == 0)
            {
                return;
            }

            GUILayout.Label("--- 缺失绑定 ---", EditorStyles.centeredGreyMiniLabel);

            foreach (var group in groups)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                var display = _presentation.GetDisplayInfo(group);
                GUILayout.Label($"缺失: {display.DisplayName}", _missingLabelStyle);
                GUILayout.Label($"路径: {display.DisplayPath}", _wrapLabelStyle);

                EditorGUI.BeginChangeCheck();
                var newFixTarget = EditorGUILayout.ObjectField("新物体/组件", group.FixTarget, typeof(UnityEngine.Object), true);
                if (EditorGUI.EndChangeCheck())
                {
                    _presentation.UpdateFixTarget(group, newFixTarget, _service.TargetRoot);
                }

                group.IsExpanded = EditorGUILayout.Foldout(group.IsExpanded, "缺失属性列表", true);
                if (group.IsExpanded)
                {
                    EditorGUILayout.BeginVertical("box");

                    var sections = _presentation.BuildTypeSections(group);
                    foreach (var section in sections)
                    {
                        EditorGUILayout.LabelField($"[{section.ComponentType.Name}] 属性组 (共 {section.TotalActiveCount} 条未处理)", EditorStyles.boldLabel);

                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                        if (section.TotalActiveCount >= 2)
                        {
                            if (GUILayout.Button("全部移除", GUILayout.Height(20f)))
                            {
                                _presentation.MarkEntriesForRemoval(group, section.AllEntries);
                                _presentation.UpdateFixTarget(group, group.FixTarget, _service.TargetRoot);
                                EditorGUILayout.EndVertical();
                                continue;
                            }
                        }

                        foreach (var propertyGroup in section.PropertyGroups)
                        {
                            EditorGUILayout.BeginHorizontal();
                            GUILayout.Label(propertyGroup.LabelText, EditorStyles.boldLabel);

                            if (propertyGroup.IsBlendshape &&
                                group.FixTarget != null &&
                                _presentation.HasRequiredComponent(group, section.ComponentType))
                            {
                                int newIndex = EditorGUILayout.Popup(
                                    propertyGroup.CurrentBlendshapeIndex,
                                    propertyGroup.BlendshapeOptions.ToArray(),
                                    GUILayout.Width(150f));
                                if (newIndex != propertyGroup.CurrentBlendshapeIndex)
                                {
                                    _presentation.ApplyBlendshapeSelection(propertyGroup, newIndex);
                                }
                            }

                            GUILayout.FlexibleSpace();

                            if (propertyGroup.AllFixed)
                            {
                                GUILayout.Label("待修复", _fixedStatusStyle);
                            }
                            else
                            {
                                GUILayout.Label("未处理", _pendingStatusStyle);
                            }

                            if (GUILayout.Button("-", GUILayout.Width(EditorGUIUtility.singleLineHeight), GUILayout.Height(EditorGUIUtility.singleLineHeight)))
                            {
                                _presentation.MarkEntriesForRemoval(group, propertyGroup.Entries);
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

        #endregion
    }
}
