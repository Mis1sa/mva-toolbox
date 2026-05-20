using System;
using System.Collections.Generic;
using System.Linq;
using MVA.Toolbox.Animation.Shared.Controllers;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.AnimationRedirectTool
{
    public sealed class AnimationRedirectToolWindow : EditorWindow
    {
        private readonly List<AnimatorController> _controllers = new List<AnimatorController>();
        private readonly List<string> _controllerNames = new List<string>();
        private readonly Dictionary<AnimatorController, Transform> _controllerRootMap = new Dictionary<AnimatorController, Transform>();

        private GameObject _targetRoot;
        private VRCAvatarDescriptor _avatarDescriptor;
        private Animator _animator;
        private int _selectedControllerIndex = -1;
        private int _selectedLayerIndex = -1;
        private Vector2 _scroll;

        private AnimationRedirectToolService _service;
        private AnimationRedirectToolPresentationService _presentation;

        private GUIStyle _missingLabelStyle;
        private GUIStyle _removedLabelStyle;
        private GUIStyle _changedLabelStyle;
        private GUIStyle _pendingStatusStyle;
        private GUIStyle _fixedStatusStyle;
        private GUIStyle _wrapLabelStyle;
        private GUIStyle _wrapMiniLabelStyle;

        public static void Open()
        {
            AnimationRedirectToolWindow window = GetWindow<AnimationRedirectToolWindow>(false, "动画 - 重定向");
            window.minSize = new Vector2(520f, 480f);
            window.Show();
        }

        private AnimatorController SelectedController
        {
            get
            {
                if (_controllers.Count == 0 || _selectedControllerIndex < 0)
                {
                    return null;
                }

                int index = Mathf.Clamp(_selectedControllerIndex, 0, _controllers.Count - 1);
                return _controllers[index];
            }
        }

        private void OnEnable()
        {
            _service ??= new AnimationRedirectToolService();
            _presentation ??= new AnimationRedirectToolPresentationService(_service);
            EditorApplication.hierarchyChanged += HandleHierarchyChanged;
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= HandleHierarchyChanged;
        }

        private void HandleHierarchyChanged()
        {
            _service?.OnHierarchyChanged();
            Repaint();
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawTargetSelectionSection();

            if (_targetRoot == null || _controllers.Count == 0)
            {
                return;
            }

            _service.SyncScope(_targetRoot, _controllers, _controllerRootMap, _selectedControllerIndex, _selectedLayerIndex);

            EditorGUILayout.Space(4f);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawTrackingAndResults();
            EditorGUILayout.EndScrollView();
        }

        private void EnsureStyles()
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

        private void DrawTargetSelectionSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GameObject newTarget = (GameObject)EditorGUILayout.ObjectField("目标物体", _targetRoot, typeof(GameObject), true);
            if (newTarget != _targetRoot)
            {
                if (!TrySetTarget(newTarget))
                {
                    EditorUtility.DisplayDialog("无效对象", "请拖入 Avatar 根或带 Animator 组件的物体。", "确定");
                }
            }

            if (_targetRoot == null)
            {
                EditorGUILayout.HelpBox("请选择 Avatar 或带 Animator 的物体。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            if (_controllers.Count == 0)
            {
                EditorGUILayout.HelpBox("未在该目标下找到 AnimatorController。", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.Space(4f);
            bool selectionLocked = _service != null && _service.HasSnapshot;

            using (new EditorGUI.DisabledScope(selectionLocked))
            {
                int newControllerIndex = EditorGUILayout.Popup("控制器", Mathf.Clamp(_selectedControllerIndex, 0, _controllerNames.Count - 1), BuildControllerOptions());
                if (newControllerIndex != _selectedControllerIndex)
                {
                    _selectedControllerIndex = newControllerIndex;
                    _selectedLayerIndex = -1;
                }
            }

            AnimatorController controller = SelectedController;
            if (controller == null)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Popup("层级", 0, new[] { "全部层级" });
                }

                _selectedLayerIndex = -1;
                EditorGUILayout.EndVertical();
                return;
            }

            AnimatorControllerLayer[] layers = controller.layers ?? Array.Empty<AnimatorControllerLayer>();
            string[] layerOptions = new string[layers.Length + 1];
            layerOptions[0] = "全部层级";
            for (int i = 0; i < layers.Length; i++)
            {
                string layerName = layers[i]?.name;
                layerOptions[i + 1] = string.IsNullOrEmpty(layerName) ? $"Layer {i}" : layerName;
            }

            int displayLayerIndex = _selectedLayerIndex < 0 ? 0 : Mathf.Clamp(_selectedLayerIndex + 1, 0, layerOptions.Length - 1);
            using (new EditorGUI.DisabledScope(selectionLocked))
            {
                int newLayerIndex = EditorGUILayout.Popup("层级", displayLayerIndex, layerOptions);
                if (newLayerIndex != displayLayerIndex)
                {
                    _selectedLayerIndex = newLayerIndex - 1;
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawTrackingAndResults()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            bool hasController = SelectedController != null;
            using (new EditorGUI.DisabledScope(!hasController))
            {
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
            }

            AnimationRedirectSummary summary = _presentation.BuildSummary();
            using (new EditorGUI.DisabledScope(_service.MissingGroups.Count == 0))
            {
                string ignoreButtonText = _service.IgnoreAllMissing
                    ? $"已忽略 ({summary.IgnorablesCount} 项)"
                    : "忽略所有未处理缺失";
                if (GUILayout.Button(ignoreButtonText, GUILayout.Height(30f)))
                {
                    _service.IgnoreAllMissing = !_service.IgnoreAllMissing;
                    Repaint();
                }
            }
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
                EditorGUILayout.HelpBox($"层级结构已被修改。路径变动/删除：{summary.PathChangeCount} 组，组件变更：{summary.ComponentChangeCount} 条，缺失绑定（待处理）：{summary.ActiveMissingCount} 条，标记移除：{summary.RemovalOnlyCount} 条。", MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"追踪中... 总变动/缺失量: {summary.TotalChanges}", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(!summary.CanAutoMatch))
            {
                if (GUILayout.Button("自动匹配", GUILayout.Width(100f)))
                {
                    var (matched, ambiguous, invalid) = _service.AutoMatchMissingFixTargets();
                    EditorUtility.DisplayDialog("自动匹配完成", $"成功匹配：{matched}\n跳过（重名/不唯一）：{ambiguous}\n跳过（组件不满足）：{invalid}", "确定");
                    Repaint();
                }
            }
            EditorGUILayout.EndHorizontal();

            DrawMissingBindingsContent();
            DrawPathChangesContent();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if (summary.NeedsComponentFix)
            {
                EditorGUILayout.HelpBox("部分缺失绑定的修复目标缺少必要的组件，请移除曲线或替换修复目标。", MessageType.Error);
            }

            using (new EditorGUI.DisabledScope(!summary.CanApply))
            {
                if (GUILayout.Button("应用修改", GUILayout.Height(40f)))
                {
                    var (modified, fixedCount, removed) = _service.ApplyRedirects();
                    EditorUtility.DisplayDialog("完成", $"路径修正完成！\n重定向: {modified}\n修复: {fixedCount}\n移除: {removed}", "确定");
                    Repaint();
                }
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndVertical();
        }

        private void DrawPathChangesContent()
        {
            IReadOnlyList<AnimationRedirectToolService.PathChangeGroup> pathGroups = _presentation.GetVisiblePathChangeGroups();
            foreach (AnimationRedirectToolService.PathChangeGroup data in pathGroups)
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

                IEnumerable<string> componentTypes = data.Bindings.Select(binding => binding.type.Name).Distinct();
                GUILayout.Label($"涉及类型: {string.Join(", ", componentTypes)} (共 {data.Bindings.Count} 条曲线)", _wrapMiniLabelStyle);
                EditorGUILayout.EndVertical();
            }

            IReadOnlyList<AnimationRedirectToolService.ComponentChangeGroup> componentGroups = _presentation.GetVisibleComponentChangeGroups();
            foreach (AnimationRedirectToolService.ComponentChangeGroup group in componentGroups)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("组件移除", _removedLabelStyle, GUILayout.Width(70f));
                GUILayout.Label($"物体: {group.TargetObjectName}", EditorStyles.boldLabel);
                GUILayout.Label($"组件: {group.ComponentName}", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                GUILayout.Label($"路径: {group.Path}", _wrapLabelStyle);
                string[] propSamples = group.Bindings.Select(binding => binding.propertyName).Distinct().Take(3).ToArray();
                string props = propSamples.Length > 0 ? string.Join(", ", propSamples) : "(属性未知)";
                GUILayout.Label($"涉及属性: {props} (共 {group.Bindings.Count} 条曲线)", _wrapMiniLabelStyle);
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawMissingBindingsContent()
        {
            IReadOnlyList<AnimationRedirectToolService.MissingObjectGroup> groups = _presentation.GetVisibleMissingGroups();
            if (groups.Count == 0)
            {
                return;
            }

            GUILayout.Label("--- 缺失绑定 ---", EditorStyles.centeredGreyMiniLabel);
            foreach (AnimationRedirectToolService.MissingObjectGroup group in groups)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                MissingGroupDisplayInfo display = _presentation.GetDisplayInfo(group);
                GUILayout.Label($"缺失: {display.DisplayName}", _missingLabelStyle);
                GUILayout.Label($"路径: {display.DisplayPath}", _wrapLabelStyle);

                EditorGUI.BeginChangeCheck();
                Object newFixTarget = EditorGUILayout.ObjectField("新物体/组件", group.FixTarget, typeof(Object), true);
                if (EditorGUI.EndChangeCheck())
                {
                    _presentation.UpdateFixTarget(group, newFixTarget, _service.TargetRoot);
                }

                group.IsExpanded = EditorGUILayout.Foldout(group.IsExpanded, "缺失属性列表", true);
                if (group.IsExpanded)
                {
                    EditorGUILayout.BeginVertical("box");
                    IReadOnlyList<MissingTypeSectionView> sections = _presentation.BuildTypeSections(group);
                    foreach (MissingTypeSectionView section in sections)
                    {
                        EditorGUILayout.LabelField($"[{section.ComponentType.Name}] 属性组 (共 {section.TotalActiveCount} 条未处理)", EditorStyles.boldLabel);
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                        if (section.TotalActiveCount >= 2 && GUILayout.Button("全部移除", GUILayout.Height(20f)))
                        {
                            _presentation.MarkEntriesForRemoval(group, section.AllEntries);
                            _presentation.UpdateFixTarget(group, group.FixTarget, _service.TargetRoot);
                            EditorGUILayout.EndVertical();
                            continue;
                        }

                        foreach (MissingPropertyGroupView propertyGroup in section.PropertyGroups)
                        {
                            EditorGUILayout.BeginHorizontal();
                            GUILayout.Label(propertyGroup.LabelText, EditorStyles.boldLabel);

                            if (propertyGroup.IsBlendshape && group.FixTarget != null && _presentation.HasRequiredComponent(group, section.ComponentType))
                            {
                                int newIndex = EditorGUILayout.Popup(propertyGroup.CurrentBlendshapeIndex, propertyGroup.BlendshapeOptions.ToArray(), GUILayout.Width(150f));
                                if (newIndex != propertyGroup.CurrentBlendshapeIndex)
                                {
                                    _presentation.ApplyBlendshapeSelection(propertyGroup, newIndex);
                                }
                            }

                            GUILayout.FlexibleSpace();
                            GUILayout.Label(propertyGroup.AllFixed ? "已修复" : "未处理", propertyGroup.AllFixed ? _fixedStatusStyle : _pendingStatusStyle);

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

        private bool TrySetTarget(GameObject newTarget)
        {
            if (newTarget == null)
            {
                ClearTarget();
                return true;
            }

            VRCAvatarDescriptor avatarDescriptor = newTarget.GetComponent<VRCAvatarDescriptor>();
            Animator animator = newTarget.GetComponent<Animator>();
            if (avatarDescriptor == null && (animator == null || animator.runtimeAnimatorController == null))
            {
                return false;
            }

            _targetRoot = newTarget;
            RefreshTargetComponents();
            RefreshControllers();
            return true;
        }

        private void RefreshTargetComponents()
        {
            _avatarDescriptor = null;
            _animator = null;
            if (_targetRoot == null)
            {
                return;
            }

            _avatarDescriptor = _targetRoot.GetComponent<VRCAvatarDescriptor>();
            _animator = _targetRoot.GetComponent<Animator>();
        }

        private void RefreshControllers()
        {
            AnimatorController previousController = SelectedController;
            int previousLayerIndex = _selectedLayerIndex;

            _controllers.Clear();
            _controllerNames.Clear();
            _controllerRootMap.Clear();
            _selectedControllerIndex = -1;
            _selectedLayerIndex = -1;

            if (_targetRoot == null)
            {
                return;
            }

            List<ControllerWithRoot> entries = AnimationControllerCollection.CollectControllersWithRoot(_targetRoot, includeSpecialLayers: true, allowAnimatorSubtree: false);
            for (int i = 0; i < entries.Count; i++)
            {
                ControllerWithRoot entry = entries[i];
                if (entry.Controller == null)
                {
                    continue;
                }

                _controllers.Add(entry.Controller);
                if (entry.RootTransform != null)
                {
                    _controllerRootMap[entry.Controller] = entry.RootTransform;
                }
            }

            if (_controllers.Count == 0)
            {
                return;
            }

            _controllerNames.AddRange(AnimationControllerCollection.BuildControllerDisplayNames(_avatarDescriptor, _animator, _controllers));
            if (previousController != null)
            {
                int previousIndex = _controllers.IndexOf(previousController);
                if (previousIndex >= 0)
                {
                    _selectedControllerIndex = previousIndex;
                    _selectedLayerIndex = previousLayerIndex;
                    return;
                }
            }

            if (_avatarDescriptor != null)
            {
                AnimatorController fxController = AnimationControllerCollection.GetExistingFXController(_avatarDescriptor);
                if (fxController != null)
                {
                    int fxIndex = _controllers.IndexOf(fxController);
                    if (fxIndex >= 0)
                    {
                        _selectedControllerIndex = fxIndex;
                        return;
                    }
                }
            }

            _selectedControllerIndex = 0;
        }

        private void ClearTarget()
        {
            _targetRoot = null;
            _avatarDescriptor = null;
            _animator = null;
            _controllers.Clear();
            _controllerNames.Clear();
            _controllerRootMap.Clear();
            _selectedControllerIndex = -1;
            _selectedLayerIndex = -1;
            _service?.SyncScope(null, Array.Empty<AnimatorController>(), new Dictionary<AnimatorController, Transform>(), -1, -1);
        }

        private string[] BuildControllerOptions()
        {
            string[] options = new string[_controllers.Count];
            for (int i = 0; i < _controllers.Count; i++)
            {
                options[i] = i < _controllerNames.Count ? _controllerNames[i] : _controllers[i] != null ? _controllers[i].name : "(Controller)";
            }

            return options;
        }
    }
}
