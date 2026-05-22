using System;
using System.Collections.Generic;
using MVA.Toolbox.Animation.Shared.Controllers;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace MVA.Toolbox.AnimationBakeTool
{
    /// <summary>
    /// AnimFix - 默认值烘焙窗口（复刻 Bake Default Anim）
    /// </summary>
    public sealed class AnimationBakeToolWindow : EditorWindow
    {
        private const string DefaultRootFolder = "Assets/MVA Toolbox/AnimationBake";

        private readonly List<AnimatorController> _controllers = new List<AnimatorController>();
        private readonly List<string> _controllerNames = new List<string>();
        private readonly Dictionary<AnimatorController, ControllerWithRoot> _controllerScopeMap = new Dictionary<AnimatorController, ControllerWithRoot>();

        private GameObject _targetRoot;
        private VRCAvatarDescriptor _avatarDescriptor;
        private Animator _animator;
        private int _selectedControllerIndex;
        private int _selectedLayerIndex = -1;
        private string _suffixName = string.Empty;
        private string _saveFolderRelative = DefaultRootFolder;
        private bool _onlyGenerateClips;
        private Vector2 _scroll;
        private AnimationBakeToolService _service;

        public static void Open()
        {
            AnimationBakeToolWindow window = GetWindow<AnimationBakeToolWindow>(false, "动画 - 默认值烘培");
            window.minSize = new Vector2(520f, 480f);
            window.Show();
        }

        private void OnEnable()
        {
            _service ??= new AnimationBakeToolService();
        }

        public void OnGUI()
        {
            DrawTargetSelectionSection();

            if (_targetRoot == null || _controllers.Count == 0)
            {
                return;
            }

            EditorGUILayout.Space(4f);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            AnimatorController controller = SelectedController;
            if (!_service.TryResolveLayer(_targetRoot, controller, _selectedLayerIndex, out AnimatorControllerLayer layer, out string error))
            {
                if (!string.IsNullOrEmpty(error))
                {
                    EditorGUILayout.HelpBox(error, MessageType.Info);
                }

                EditorGUILayout.EndScrollView();
                return;
            }

            GUILayout.Space(4f);

            DrawOptionsAndAction(controller, layer);
            EditorGUILayout.EndScrollView();
        }

        private AnimatorController SelectedController
        {
            get
            {
                if (_controllers.Count == 0)
                {
                    return null;
                }

                int index = Mathf.Clamp(_selectedControllerIndex, 0, _controllers.Count - 1);
                return _controllers[index];
            }
        }

        private Transform SelectedControllerRoot
        {
            get
            {
                AnimatorController controller = SelectedController;
                if (controller == null)
                {
                    return null;
                }

                return _controllerScopeMap.TryGetValue(controller, out ControllerWithRoot scope) ? scope.RootTransform : null;
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

            string[] controllerOptions = _controllerNames.Count == _controllers.Count
                ? _controllerNames.ToArray()
                : BuildFallbackControllerNames();
            int displayControllerIndex = Mathf.Clamp(_selectedControllerIndex, 0, Mathf.Max(0, controllerOptions.Length - 1));
            int newControllerIndex = EditorGUILayout.Popup("控制器", displayControllerIndex, controllerOptions);
            if (newControllerIndex != displayControllerIndex)
            {
                _selectedControllerIndex = newControllerIndex;
                EnsureLayerSelectionValid();
            }

            AnimatorController controller = SelectedController;
            if (controller != null)
            {
                AnimatorControllerLayer[] layers = controller.layers ?? Array.Empty<AnimatorControllerLayer>();
                if (layers.Length == 0)
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.Popup("层级", 0, new[] { "(无层级)" });
                    }

                    _selectedLayerIndex = -1;
                }
                else
                {
                    EnsureLayerSelectionValid();
                    string[] layerOptions = new string[layers.Length];
                    for (int i = 0; i < layers.Length; i++)
                    {
                        string layerName = layers[i]?.name;
                        layerOptions[i] = string.IsNullOrEmpty(layerName) ? $"Layer {i}" : layerName;
                    }

                    int displayLayerIndex = Mathf.Clamp(_selectedLayerIndex, 0, layerOptions.Length - 1);
                    int newLayerIndex = EditorGUILayout.Popup("层级", displayLayerIndex, layerOptions);
                    if (newLayerIndex != displayLayerIndex)
                    {
                        _selectedLayerIndex = newLayerIndex;
                    }
                }
            }

            EditorGUILayout.EndVertical();
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
            _selectedControllerIndex = 0;
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
                return;
            }

            _controllerNames.AddRange(AnimatorControllerCollection.BuildControllerDisplayNames(_avatarDescriptor, _animator, _controllers, _controllerScopeMap));

            int selectedIndex = previousController != null ? _controllers.IndexOf(previousController) : -1;
            if (selectedIndex < 0 && _avatarDescriptor != null)
            {
                AnimatorController fxController = AnimatorControllerCollection.GetExistingFXController(_avatarDescriptor);
                if (fxController != null)
                {
                    selectedIndex = _controllers.IndexOf(fxController);
                }
            }

            if (selectedIndex < 0)
            {
                selectedIndex = 0;
            }

            _selectedControllerIndex = selectedIndex;

            AnimatorController controller = SelectedController;
            if (controller != null && controller.layers != null && controller.layers.Length > 0)
            {
                _selectedLayerIndex = previousLayerIndex >= 0
                    ? Mathf.Clamp(previousLayerIndex, 0, controller.layers.Length - 1)
                    : 0;
            }
        }

        private void ClearTarget()
        {
            _targetRoot = null;
            _avatarDescriptor = null;
            _animator = null;
            _controllers.Clear();
            _controllerNames.Clear();
            _controllerScopeMap.Clear();
            _selectedControllerIndex = 0;
            _selectedLayerIndex = -1;
        }

        private void EnsureLayerSelectionValid()
        {
            AnimatorController controller = SelectedController;
            if (controller == null || controller.layers == null || controller.layers.Length == 0)
            {
                _selectedLayerIndex = -1;
                return;
            }

            if (_selectedLayerIndex < 0 || _selectedLayerIndex >= controller.layers.Length)
            {
                _selectedLayerIndex = 0;
            }
        }

        private string[] BuildFallbackControllerNames()
        {
            string[] result = new string[_controllers.Count];
            for (int i = 0; i < _controllers.Count; i++)
            {
                result[i] = _controllers[i] != null ? _controllers[i].name : "(Controller)";
            }

            return result;
        }

        private void DrawOptionsAndAction(AnimatorController controller, AnimatorControllerLayer layer)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("烘焙设置", EditorStyles.boldLabel);

            _suffixName = EditorGUILayout.TextField("剪辑名称后缀 (可选)", _suffixName);

            EditorGUILayout.BeginHorizontal();
            _saveFolderRelative = EditorGUILayout.TextField("保存路径", _saveFolderRelative);
            if (GUILayout.Button("浏览", GUILayout.Width(60f), GUILayout.Height(EditorGUIUtility.singleLineHeight)))
            {
                string abs = EditorUtility.OpenFolderPanel("选择保存文件夹", Application.dataPath, string.Empty);
                if (!string.IsNullOrEmpty(abs))
                {
                    if (abs.StartsWith(Application.dataPath, StringComparison.OrdinalIgnoreCase))
                    {
                        string rel = "Assets" + abs.Substring(Application.dataPath.Length);
                        _saveFolderRelative = rel.Replace("\\", "/");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("路径错误", "请选择项目 Assets 目录下的文件夹。", "确定");
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("不替换动画剪辑", GUILayout.Width(EditorGUIUtility.labelWidth));
            _onlyGenerateClips = EditorGUILayout.Toggle(_onlyGenerateClips);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4f);

            if (GUILayout.Button("对选中层执行默认值烘焙", GUILayout.Height(28f)))
            {
                _service.ExecuteBake(_targetRoot, SelectedControllerRoot, controller, layer, _suffixName, _saveFolderRelative, _onlyGenerateClips, DefaultRootFolder);
            }

            EditorGUILayout.EndVertical();
        }
    }
}
