using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using MVA.Toolbox.Public;

namespace MVA.Toolbox.BakeDefaultAnim
{
    public sealed class BakeDefaultAnimWindow : EditorWindow
    {
        UnityEngine.Object _targetObject;
        VRCAvatarDescriptor _avatarDescriptor;
        Animator _animator;

        readonly List<AnimatorController> _controllers = new List<AnimatorController>();
        readonly List<string> _controllerNames = new List<string>();

        readonly List<int> _visibleLayerIndices = new List<int>();
        int _selectedControllerIndex;
        int _selectedLayerIndex;

        // 可选的 Clip 名称后缀
        string _suffixName = string.Empty;
        // 输出根目录
        string _saveFolderRelative = DefaultRootFolder;
        bool _onlyGenerateClips = false;

        Vector2 _scroll;

        const string DefaultRootFolder = "Assets/MVA Toolbox/BDA";

        [MenuItem("Tools/MVA Toolbox/Bake Default Anim", false, 8)]
        public static void Open()
        {
            var w = GetWindow<BakeDefaultAnimWindow>("Bake Default Anim");
            w.minSize = new Vector2(450f, 260f);
        }

        void OnGUI()
        {
            _scroll = ToolboxUtils.ScrollView(_scroll, () =>
            {
                DrawTargetSelection();

                GUILayout.Space(4f);

                if (_targetObject == null)
                {
                    EditorGUILayout.HelpBox("请拖入 VRChat Avatar 或带 Animator 组件的物体。", MessageType.Info);
                    return;
                }

                DrawControllerAndLayerSelection();

                GUILayout.Space(4f);

                DrawOptionsAndAction();
            });
        }

        void DrawTargetSelection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("目标对象", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var newTarget = EditorGUILayout.ObjectField("Avatar / 带 Animator 的物体", _targetObject, typeof(UnityEngine.Object), true);
            if (EditorGUI.EndChangeCheck())
            {
                // 仅允许 Avatar 根或带 Animator 组件的物体
                if (newTarget is GameObject go && (ToolboxUtils.IsAvatarRoot(go) || ToolboxUtils.HasAnimator(go)))
                {
                    _targetObject = go;
                    RefreshTargetComponents();
                    RefreshControllers();
                }
                else
                {
                    _targetObject = null;
                    _avatarDescriptor = null;
                    _animator = null;
                    _controllers.Clear();
                    _controllerNames.Clear();
                    _visibleLayerIndices.Clear();
                    _selectedControllerIndex = 0;
                    _selectedLayerIndex = 0;
                }
            }

            EditorGUILayout.EndVertical();
        }

        void RefreshTargetComponents()
        {
            _avatarDescriptor = null;
            _animator = null;

            if (_targetObject is GameObject go)
            {
                _avatarDescriptor = go.GetComponent<VRCAvatarDescriptor>();
                _animator = go.GetComponent<Animator>();
            }
        }

        void RefreshControllers()
        {
            _controllers.Clear();
            _controllerNames.Clear();
            _visibleLayerIndices.Clear();
            _selectedControllerIndex = 0;
            _selectedLayerIndex = 0;

            if (_targetObject == null)
            {
                return;
            }

            if (_targetObject is GameObject root)
            {
                _controllers.AddRange(ToolboxUtils.CollectControllersFromRoot(root, includeSpecialLayers: true));
                if (_controllers.Count > 0)
                {
                    _controllerNames.AddRange(ToolboxUtils.BuildControllerDisplayNames(_avatarDescriptor, _animator, _controllers));

                    // 若目标是 Avatar 且存在 FX 控制器，则默认选中 FX 控制器
                    if (_avatarDescriptor != null)
                    {
                        var fxController = ToolboxUtils.GetExistingFXController(_avatarDescriptor);
                        if (fxController != null)
                        {
                            for (int i = 0; i < _controllers.Count; i++)
                            {
                                if (_controllers[i] == fxController)
                                {
                                    _selectedControllerIndex = i;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        void DrawControllerAndLayerSelection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (_controllers.Count == 0)
            {
                EditorGUILayout.HelpBox("在当前目标中未找到任何 AnimatorController，请确认 Avatar/物体已正确配置动画控制器。", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            // 控制器选择
            var controller = _controllers[Mathf.Clamp(_selectedControllerIndex, 0, _controllers.Count - 1)];
            if (controller == null)
            {
                EditorGUILayout.HelpBox("选中的 AnimatorController 无效。", MessageType.Error);
                EditorGUILayout.EndVertical();
                return;
            }

            if (_controllers.Count > 1)
            {
                var names = _controllerNames.Count == _controllers.Count ? _controllerNames.ToArray() : BuildControllerNamesFallback();
                _selectedControllerIndex = EditorGUILayout.Popup("控制器", _selectedControllerIndex, names);
                controller = _controllers[Mathf.Clamp(_selectedControllerIndex, 0, _controllers.Count - 1)];
            }

            // 层级选择（过滤掉包含 BlendTree 的层）
            var layers = controller.layers ?? Array.Empty<AnimatorControllerLayer>();
            if (layers.Length == 0)
            {
                EditorGUILayout.HelpBox("当前控制器中没有任何层级。", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            _visibleLayerIndices.Clear();
            var layerNameList = new List<string>();
            for (int i = 0; i < layers.Length; i++)
            {
                // 含有任意 BlendTree 的层级直接忽略，不加入可选列表
                if (LayerHasBlendTree(layers[i].stateMachine))
                {
                    continue;
                }

                _visibleLayerIndices.Add(i);
                layerNameList.Add(layers[i].name ?? $"Layer {i}");
            }

            if (_visibleLayerIndices.Count == 0)
            {
                EditorGUILayout.HelpBox("当前控制器中没有不包含 BlendTree 的层级。", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            var layerNames = layerNameList.ToArray();
            _selectedLayerIndex = Mathf.Clamp(_selectedLayerIndex, 0, _visibleLayerIndices.Count - 1);
            _selectedLayerIndex = EditorGUILayout.Popup("层级 (忽略Blendtree)", _selectedLayerIndex, layerNames);

            EditorGUILayout.EndVertical();
        }

        string[] BuildControllerNamesFallback()
        {
            var result = new string[_controllers.Count];
            for (int i = 0; i < _controllers.Count; i++)
            {
                result[i] = _controllers[i] != null ? _controllers[i].name : "(Controller)";
            }

            return result;
        }

        void DrawOptionsAndAction()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            _suffixName = EditorGUILayout.TextField("剪辑名称后缀 (可选)", _suffixName);

            EditorGUILayout.BeginHorizontal();
            _saveFolderRelative = EditorGUILayout.TextField("保存路径", _saveFolderRelative);
            if (GUILayout.Button("浏览", GUILayout.Width(60f), GUILayout.Height(EditorGUIUtility.singleLineHeight)))
            {
                string abs = EditorUtility.OpenFolderPanel("选择保存文件夹 (请选 Assets 下文件夹或在 Assets 下新建)", Application.dataPath, "");
                if (!string.IsNullOrEmpty(abs))
                {
                    if (abs.StartsWith(Application.dataPath))
                    {
                        string rel = "Assets" + abs.Substring(Application.dataPath.Length);
                        _saveFolderRelative = rel.Replace("\\", "/");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("路径错误", "请选择项目中的 Assets 目录下的文件夹以便保存动画剪辑。", "确定");
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            // 不替换动画剪辑：标签与上方输入项左对齐，勾选框位置与输入框左侧对齐
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("不替换动画剪辑", GUILayout.Width(EditorGUIUtility.labelWidth));
            _onlyGenerateClips = EditorGUILayout.Toggle(_onlyGenerateClips);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4f);

            // 只有在存在控制器且当前控制器中有可见层（已过滤 BlendTree）时才允许执行
            var hasController =
                _controllers.Count > 0 &&
                _selectedControllerIndex >= 0 && _selectedControllerIndex < _controllers.Count &&
                _visibleLayerIndices.Count > 0;
            GUI.enabled = hasController;
            if (GUILayout.Button("对选中层执行默认值烘焙", GUILayout.Height(28f)))
            {
                var controller = _controllers[Mathf.Clamp(_selectedControllerIndex, 0, _controllers.Count - 1)];
                var layers = controller.layers ?? Array.Empty<AnimatorControllerLayer>();
                if (_visibleLayerIndices.Count > 0)
                {
                    int visibleIndex = Mathf.Clamp(_selectedLayerIndex, 0, _visibleLayerIndices.Count - 1);
                    int layerIndex = _visibleLayerIndices[visibleIndex];
                    if (layerIndex < 0 || layerIndex >= layers.Length)
                    {
                        return;
                    }

                    var layer = layers[layerIndex];
                    var confirm = EditorUtility.DisplayDialog(
                        "执行默认值烘焙",
                        $"将对控制器 '{controller.name}' 的层 '{layer.name}' 进行动画剪辑复制与默认值补齐。",
                        "执行",
                        "取消");
                    if (confirm)
                    {
                        ProcessSelectedLayer(controller, layer);
                    }
                }
            }
            GUI.enabled = true;

            EditorGUILayout.EndVertical();
        }

        void ProcessSelectedLayer(AnimatorController controller, AnimatorControllerLayer layer)
        {
            if (controller == null || layer == null)
            {
                EditorUtility.DisplayDialog("错误", "请选择有效的 AnimatorController 和层级。", "确定");
                return;
            }

            Undo.RecordObject(controller, "Bake Default Anim");

            var clipMap = new Dictionary<AnimationClip, List<AnimatorState>>();
            var emptyStates = new List<AnimatorState>();
            var allBindings = new HashSet<EditorCurveBinding>();

            TraverseStateMachine(layer.stateMachine, clipMap, emptyStates, allBindings);

            if (clipMap.Count == 0 && emptyStates.Count == 0)
            {
                EditorUtility.DisplayDialog("信息", "该层中没有需要处理的状态。", "确定");
                return;
            }

            // 输出目录：[保存路径]/[LayerName]_[时间戳]/
            string timeStamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string layerSegment = string.IsNullOrEmpty(layer.name) ? "Layer" : ToolboxUtils.SanitizePathSegment(layer.name);

            // 使用用户指定的保存根路径，若为空或非法则回退到默认路径
            string rootFolder = _saveFolderRelative;
            if (!string.IsNullOrEmpty(rootFolder))
            {
                rootFolder = rootFolder.Trim();
                rootFolder = rootFolder.Replace("\\", "/");
            }
            if (string.IsNullOrEmpty(rootFolder) ||
                !(string.Equals(rootFolder, "Assets", StringComparison.Ordinal) ||
                  rootFolder.StartsWith("Assets/", StringComparison.Ordinal)))
            {
                rootFolder = DefaultRootFolder;
            }

            string folderRelative = $"{rootFolder}/{layerSegment}_{timeStamp}";
            ToolboxUtils.EnsureFolderExists(rootFolder);
            ToolboxUtils.EnsureFolderExists(folderRelative);

            string assetFolder = folderRelative.EndsWith("/") ? folderRelative : folderRelative + "/";

            AssetDatabase.StartAssetEditing();

            try
            {
                // 预计算每个 binding 的默认值，避免重复采样
                var defaultValueMap = new Dictionary<EditorCurveBinding, object>();
                foreach (var binding in allBindings)
                {
                    var v = GetDefaultValueFromObject(binding);
                    if (v != null)
                    {
                        defaultValueMap[binding] = v;
                    }
                }

                AnimationClip defaultClip = null;

                if (emptyStates.Count > 0)
                {
                    defaultClip = new AnimationClip();
                    string defaultClipName = $"{layer.name}_Default";
                    if (!string.IsNullOrEmpty(_suffixName))
                    {
                        defaultClipName += "_" + _suffixName;
                    }

                    defaultClip.name = defaultClipName;

                    string defaultClipPath = AssetDatabase.GenerateUniqueAssetPath($"{assetFolder}{defaultClipName}.anim");

                    foreach (var kv in defaultValueMap)
                    {
                        var binding = kv.Key;
                        var defaultValue = kv.Value;
                        ApplyDefaultValueToClip(defaultClip, binding, defaultValue);
                    }

                    AssetDatabase.CreateAsset(defaultClip, defaultClipPath);
                }

                if (defaultClip != null && !_onlyGenerateClips)
                {
                    foreach (var state in emptyStates)
                    {
                        state.motion = defaultClip;
                    }
                }

                foreach (var pair in clipMap)
                {
                    var originalClip = pair.Key;
                    var states = pair.Value;

                    string newClipName = originalClip != null ? originalClip.name : "Clip";
                    if (!string.IsNullOrEmpty(_suffixName))
                    {
                        newClipName += "_" + _suffixName;
                    }

                    string newClipPath = AssetDatabase.GenerateUniqueAssetPath($"{assetFolder}{newClipName}.anim");
                    var newClip = new AnimationClip();
                    if (originalClip != null)
                    {
                        EditorUtility.CopySerialized(originalClip, newClip);
                    }
                    newClip.name = newClipName;
                    AssetDatabase.CreateAsset(newClip, newClipPath);

                    if (!_onlyGenerateClips)
                    {
                        foreach (var state in states)
                        {
                            if (state != null)
                            {
                                state.motion = newClip;
                            }
                        }
                    }

                    foreach (var binding in allBindings)
                    {
                        bool hasCurve = AnimationUtility.GetEditorCurve(newClip, binding) != null;
                        bool hasRefCurve = AnimationUtility.GetObjectReferenceCurve(newClip, binding) != null;
                        if (hasCurve || hasRefCurve)
                        {
                            continue;
                        }

                        if (!defaultValueMap.TryGetValue(binding, out var defaultValue) || defaultValue == null)
                        {
                            continue;
                        }

                        ApplyDefaultValueToClip(newClip, binding, defaultValue);
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            EditorUtility.DisplayDialog("完成", $"层 '{layer.name}' 已处理完成。新剪辑保存在 '{assetFolder}'。", "确定");
        }

        void TraverseStateMachine(
            AnimatorStateMachine stateMachine,
            Dictionary<AnimationClip, List<AnimatorState>> clipMap,
            List<AnimatorState> emptyStates,
            HashSet<EditorCurveBinding> allBindings)
        {
            if (stateMachine == null) return;

            foreach (var state in stateMachine.states)
            {
                if (state.state == null) continue;

                if (state.state.motion is AnimationClip clip)
                {
                    if (!clipMap.TryGetValue(clip, out var list))
                    {
                        list = new List<AnimatorState>();
                        clipMap[clip] = list;
                    }
                    list.Add(state.state);

                    allBindings.UnionWith(AnimationUtility.GetCurveBindings(clip));
                    allBindings.UnionWith(AnimationUtility.GetObjectReferenceCurveBindings(clip));
                }
                else
                {
                    emptyStates.Add(state.state);
                }
            }

            foreach (var childStateMachine in stateMachine.stateMachines)
            {
                if (childStateMachine.stateMachine != null)
                {
                    TraverseStateMachine(childStateMachine.stateMachine, clipMap, emptyStates, allBindings);
                }
            }
        }

        // 判断某个层级的状态机中是否包含任意 BlendTree
        bool LayerHasBlendTree(AnimatorStateMachine stateMachine)
        {
            if (stateMachine == null) return false;

            foreach (var childState in stateMachine.states)
            {
                if (childState.state != null && childState.state.motion is BlendTree)
                {
                    return true;
                }
            }

            foreach (var childStateMachine in stateMachine.stateMachines)
            {
                if (childStateMachine.stateMachine != null && LayerHasBlendTree(childStateMachine.stateMachine))
                {
                    return true;
                }
            }

            return false;
        }

        void ApplyDefaultValueToClip(AnimationClip clip, EditorCurveBinding binding, object defaultValue)
        {
            if (clip == null || defaultValue == null) return;

            if (binding.propertyName.EndsWith(".r") || binding.propertyName.EndsWith(".g") ||
                binding.propertyName.EndsWith(".b") || binding.propertyName.EndsWith(".a"))
            {
                if (defaultValue is float fv)
                {
                    var curve = new AnimationCurve(new Keyframe(0f, fv));
                    AnimationUtility.SetEditorCurve(clip, binding, curve);
                }
                return;
            }

            if (defaultValue is float floatValue)
            {
                var curve = new AnimationCurve(new Keyframe(0f, floatValue));
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }
            else if (defaultValue is bool boolValue)
            {
                var curve = new AnimationCurve(new Keyframe(0f, boolValue ? 1f : 0f));
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }
            else if (defaultValue is UnityEngine.Object objectValue)
            {
                var keyframes = new ObjectReferenceKeyframe[1];
                keyframes[0] = new ObjectReferenceKeyframe { time = 0f, value = objectValue };
                AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);
            }
        }

        object GetDefaultValueFromObject(EditorCurveBinding binding)
        {
            if (!(_targetObject is GameObject root) || binding.path == null)
            {
                return null;
            }

            var target = root.transform.Find(binding.path)?.gameObject;
            if (target == null) return null;

            string propertyName = binding.propertyName;

            if (binding.type == typeof(GameObject) && propertyName == "m_IsActive")
            {
                return target.activeSelf;
            }

            if (propertyName.StartsWith("material.", StringComparison.Ordinal))
            {
                var renderer = target.GetComponent<Renderer>();
                if (renderer != null && renderer.sharedMaterial != null)
                {
                    string strippedPropertyName = propertyName.Substring("material.".Length);

                    if (strippedPropertyName.EndsWith(".r") || strippedPropertyName.EndsWith(".g") ||
                        strippedPropertyName.EndsWith(".b") || strippedPropertyName.EndsWith(".a"))
                    {
                        string basePropertyName = strippedPropertyName.Substring(0, strippedPropertyName.Length - 2);
                        if (renderer.sharedMaterial.HasProperty(basePropertyName))
                        {
                            Color color = renderer.sharedMaterial.GetColor(basePropertyName);
                            switch (strippedPropertyName[strippedPropertyName.Length - 1])
                            {
                                case 'r': return color.r;
                                case 'g': return color.g;
                                case 'b': return color.b;
                                case 'a': return color.a;
                            }
                        }
                    }
                    else if (renderer.sharedMaterial.HasProperty(strippedPropertyName))
                    {
                        try
                        {
                            return renderer.sharedMaterial.GetFloat(strippedPropertyName);
                        }
                        catch { }
                        try
                        {
                            return renderer.sharedMaterial.GetTexture(strippedPropertyName);
                        }
                        catch { }
                    }
                }
            }
            else
            {
                var component = target.GetComponent(binding.type);
                if (component == null) return null;

                if (propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                {
                    if (component is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                    {
                        string blendShapeName = propertyName.Substring("blendShape.".Length);
                        int blendShapeIndex = smr.sharedMesh.GetBlendShapeIndex(blendShapeName);
                        if (blendShapeIndex != -1)
                        {
                            return smr.GetBlendShapeWeight(blendShapeIndex);
                        }
                    }
                }

                if (component is Transform transform)
                {
                    switch (propertyName)
                    {
                        case "m_LocalPosition.x": return transform.localPosition.x;
                        case "m_LocalPosition.y": return transform.localPosition.y;
                        case "m_LocalPosition.z": return transform.localPosition.z;
                        case "m_LocalRotation.x": return transform.localRotation.x;
                        case "m_LocalRotation.y": return transform.localRotation.y;
                        case "m_LocalRotation.z": return transform.localRotation.z;
                        case "m_LocalRotation.w": return transform.localRotation.w;
                        case "m_LocalScale.x": return transform.localScale.x;
                        case "m_LocalScale.y": return transform.localScale.y;
                        case "m_LocalScale.z": return transform.localScale.z;
                    }
                }

                using (var so = new SerializedObject(component))
                {
                    var sp = so.FindProperty(propertyName);
                    if (sp != null)
                    {
                        switch (sp.propertyType)
                        {
                            case SerializedPropertyType.Float: return sp.floatValue;
                            case SerializedPropertyType.Boolean: return sp.boolValue;
                            case SerializedPropertyType.ObjectReference: return sp.objectReferenceValue;
                        }
                    }
                }
            }

            return null;
        }
    }
}
