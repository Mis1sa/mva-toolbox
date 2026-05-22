using System;
using System.Collections.Generic;
using MVA.Toolbox.Animation.Shared.Controllers;
using MVA.Toolbox.Animation.Shared.SelectableRange;
using AnimationJumpToolEntry = MVA.Toolbox.AnimationJumpTool.AnimationJumpTool;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.AnimationQueryTool
{
    public sealed class AnimationQueryToolWindow : EditorWindow
    {
        private readonly List<AnimatorController> _controllers = new List<AnimatorController>();
        private readonly List<string> _controllerNames = new List<string>();
        private readonly Dictionary<AnimatorController, ControllerWithRoot> _controllerScopeMap = new Dictionary<AnimatorController, ControllerWithRoot>();

        private GameObject _targetRoot;
        private VRCAvatarDescriptor _avatarDescriptor;
        private Animator _animator;
        private int _selectedControllerIndex = -1;
        private int _selectedLayerIndex = -1;
        private Vector2 _scroll;
        private AnimationQueryToolService _service;

        public static void Open()
        {
            AnimationQueryToolWindow window = GetWindow<AnimationQueryToolWindow>(false, "动画 - 动画查询");
            window.minSize = new Vector2(520f, 480f);
            window.Show();
        }

        private void OnEnable()
        {
            _service ??= new AnimationQueryToolService();
        }

        private void OnDisable()
        {
            AnimationSelectableRangeHighlighter.Deactivate(this);
        }

        private void OnGUI()
        {
            DrawTargetSelectionSection();

            if (_targetRoot == null || _controllers.Count == 0)
            {
                return;
            }

            _service.SyncScope(_targetRoot, _avatarDescriptor, _controllers, _controllerScopeMap, _selectedControllerIndex, _selectedLayerIndex);

            EditorGUILayout.Space(4f);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawAnimatedObjectSelector();
            if (!_service.HasAnimatedObject)
            {
                EditorGUILayout.EndScrollView();
                return;
            }

            GUILayout.Space(4f);
            DrawPropertyGroupSelector();
            GUILayout.Space(4f);
            DrawSearchResults();
            EditorGUILayout.EndScrollView();
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

        private void DrawTargetSelectionSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GameObject newTarget = (GameObject)EditorGUILayout.ObjectField(
                "目标物体",
                _targetRoot,
                typeof(GameObject),
                true);

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

            string[] controllerNames = BuildControllerOptions();
            int displayControllerIndex = _selectedControllerIndex < 0 ? 0 : Mathf.Clamp(_selectedControllerIndex + 1, 0, controllerNames.Length - 1);
            int newControllerIndex = EditorGUILayout.Popup("控制器", displayControllerIndex, controllerNames);
            if (newControllerIndex != displayControllerIndex)
            {
                _selectedControllerIndex = newControllerIndex == 0 ? -1 : newControllerIndex - 1;
                _selectedLayerIndex = -1;
            }

            AnimatorController controller = SelectedController;
            if (controller == null)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Popup("层级", 0, new[] { "全部层级" });
                }

                _selectedLayerIndex = -1;
                DrawSelectableRangeSection();
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
            int newLayerIndex = EditorGUILayout.Popup("层级", displayLayerIndex, layerOptions);
            if (newLayerIndex != displayLayerIndex)
            {
                _selectedLayerIndex = newLayerIndex - 1;
            }

            DrawSelectableRangeSection();
            EditorGUILayout.EndVertical();
        }

        private void DrawSelectableRangeSection()
        {
            AnimationSelectableRangeControls.Draw(this, BuildSelectableRangeInstanceIds);
        }

        private void DrawAnimatedObjectSelector()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.BeginChangeCheck();
            Object newObject = EditorGUILayout.ObjectField("查询物体", _service.SelectedAnimatedObject, typeof(Object), true);
            if (EditorGUI.EndChangeCheck())
            {
                _service.SetSelectedAnimatedObject(newObject);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawPropertyGroupSelector()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("属性", EditorStyles.boldLabel);

            if (!_service.HasAvailableGroups)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            IReadOnlyList<AnimationQueryToolService.PropertyGroupData> groups = _service.PropertyGroups;
            string[] displayNames = new string[groups.Count + 1];
            displayNames[0] = "全部属性";
            for (int i = 0; i < groups.Count; i++)
            {
                displayNames[i + 1] = groups[i].GroupDisplayName;
            }

            int newIndex = EditorGUILayout.Popup("属性", _service.SelectedGroupIndex, displayNames);
            _service.ChangeGroupIndex(newIndex);

            if (_service.SelectedGroupIsBlendshape && _service.CurrentBlendshapeOptions.Count > 0)
            {
                string[] blendshapeOptions = new string[_service.CurrentBlendshapeOptions.Count];
                for (int i = 0; i < blendshapeOptions.Length; i++)
                {
                    blendshapeOptions[i] = _service.CurrentBlendshapeOptions[i];
                }

                int blendshapeIndex = EditorGUILayout.Popup("Blendshape", _service.SelectedBlendshapeOptionIndex, blendshapeOptions);
                _service.ChangeBlendshapeOptionIndex(blendshapeIndex);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSearchResults()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("结果列表", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(!_service.CanRefresh()))
            {
                if (GUILayout.Button("刷新", GUILayout.Width(60f)))
                {
                    _service.RefreshSearch();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (_service.SearchCompleted && _service.FoundClips.Count == 0)
            {
                EditorGUILayout.HelpBox("未找到任何匹配该属性的动画剪辑。", MessageType.Info);
            }
            else if (_service.SearchCompleted)
            {
                IReadOnlyList<AnimationQueryToolService.FoundClipInfo> clips = _service.FoundClips;
                for (int i = 0; i < clips.Count; i++)
                {
                    AnimationQueryToolService.FoundClipInfo info = clips[i];
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.ObjectField(info.Clip, typeof(AnimationClip), false);
                    EditorGUILayout.LabelField(info.Controller != null ? info.Controller.name : "(Controller)", GUILayout.Width(150f));
                    if (GUILayout.Button("跳转", GUILayout.Width(60f)))
                    {
                        AnimationJumpToolEntry.TryJumpToClip(info.Clip);
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            DrawSwitchGeneratorConfigHints();
            EditorGUILayout.EndVertical();
        }

        private void DrawSwitchGeneratorConfigHints()
        {
            IReadOnlyList<string> hints = _service.BuildSwitchGeneratorConfigHints();
            if (hints.Count == 0)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("开关生成配置", EditorStyles.boldLabel);
            for (int i = 0; i < hints.Count; i++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(hints[i]);
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
            _controllerScopeMap.Clear();
            _selectedControllerIndex = -1;
            _selectedLayerIndex = -1;

            if (_targetRoot == null)
            {
                return;
            }

            List<ControllerWithRoot> entries = AnimatorControllerCollection.CollectControllersWithRoot(_targetRoot, includeSpecialLayers: true, allowAnimatorSubtree: true);
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
                    _controllerScopeMap[entry.Controller] = entry;
                }
            }

            if (_controllers.Count == 0)
            {
                AnimationSelectableRangeHighlighter.Deactivate(this);
                return;
            }

            _controllerNames.AddRange(AnimatorControllerCollection.BuildControllerDisplayNames(_avatarDescriptor, _animator, _controllers, _controllerScopeMap));
            if (previousController != null)
            {
                int previousIndex = _controllers.IndexOf(previousController);
                if (previousIndex >= 0)
                {
                    _selectedControllerIndex = previousIndex;
                    _selectedLayerIndex = previousLayerIndex;
                }
            }
        }

        private void ClearTarget()
        {
            AnimationSelectableRangeHighlighter.Deactivate(this);
            _targetRoot = null;
            _avatarDescriptor = null;
            _animator = null;
            _controllers.Clear();
            _controllerNames.Clear();
            _controllerScopeMap.Clear();
            _selectedControllerIndex = -1;
            _selectedLayerIndex = -1;
            _service?.SyncScope(null, null, _controllers, _controllerScopeMap, -1, -1);
        }

        private HashSet<int> BuildSelectableRangeInstanceIds()
        {
            return AnimationSelectableRangeUtility.CollectSelectableGameObjectInstanceIds(_targetRoot, EnumerateSelectableRangeScopes());
        }

        private IEnumerable<ControllerWithRoot> EnumerateSelectableRangeScopes()
        {
            if (_targetRoot == null || _controllers.Count == 0)
            {
                yield break;
            }

            if (_selectedControllerIndex < 0)
            {
                for (int i = 0; i < _controllers.Count; i++)
                {
                    AnimatorController controller = _controllers[i];
                    if (controller == null)
                    {
                        continue;
                    }

                    if (_controllerScopeMap.TryGetValue(controller, out ControllerWithRoot scope) && scope.RootTransform != null)
                    {
                        yield return scope;
                    }
                    else
                    {
                        yield return new ControllerWithRoot
                        {
                            Controller = controller,
                            RootTransform = _targetRoot.transform,
                            IgnoresNestedAnimators = false
                        };
                    }
                }

                yield break;
            }

            AnimatorController selectedController = SelectedController;
            if (selectedController == null)
            {
                yield break;
            }

            if (_controllerScopeMap.TryGetValue(selectedController, out ControllerWithRoot selectedScope) && selectedScope.RootTransform != null)
            {
                yield return selectedScope;
            }
            else
            {
                yield return new ControllerWithRoot
                {
                    Controller = selectedController,
                    RootTransform = _targetRoot.transform,
                    IgnoresNestedAnimators = false
                };
            }
        }

        private string[] BuildControllerOptions()
        {
            string[] options = new string[_controllers.Count + 1];
            options[0] = "全部控制器";
            for (int i = 0; i < _controllers.Count; i++)
            {
                if (i < _controllerNames.Count)
                {
                    options[i + 1] = _controllerNames[i];
                }
                else
                {
                    options[i + 1] = _controllers[i] != null ? _controllers[i].name : "(Controller)";
                }
            }

            return options;
        }
    }
}
