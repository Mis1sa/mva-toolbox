using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using MVA.Toolbox.Public;
using MVA.Toolbox.AvatarQuickToggle;

namespace MVA.Toolbox.FindAnimation.Editor
{
    // Find Anim 主窗口，用于按对象与属性查找相关动画剪辑
    public class FindAnimWindow : EditorWindow
    {
        // 目标 Avatar/带 Animator 根物体
        private GameObject targetRoot;
        private VRCAvatarDescriptor descriptor;
        private Animator animator;

        // 控制器列表与显示名
        private readonly List<AnimatorController> controllers = new List<AnimatorController>();
        private readonly List<string> controllerNames = new List<string>();
        private int selectedControllerIndex = 0;

        // 被动画控制的对象与属性分组
        private Object selectedAnimatedObject;
        private string selectedPath;

        // 搜索范围：动画控制器与层级
        // controllerScopeIndex: 0 = 全部，1..N = controllers 中的具体控制器
        private int controllerScopeIndex = 0;
        // layerScopeIndex: 0 = 全部，1..M = 选中控制器的具体层
        private int layerScopeIndex = 0;

        private readonly List<PropertyGroupData> availableGroups = new List<PropertyGroupData>();
        // selectedGroupIndex: 0 = 未选择，1..Count = availableGroups 中的具体分组
        private int selectedGroupIndex = 0;
        private bool searchCompleted;

        // 查找到的动画剪辑结果
        private readonly List<FoundClipInfo> foundClips = new List<FoundClipInfo>();

        private Vector2 mainScroll;

        [MenuItem("Tools/MVA Toolbox/Find Anim", false, 6)]
        public static void Open()
        {
            var window = GetWindow<FindAnimWindow>("Find Anim");
            window.minSize = new Vector2(500f, 600f);
        }

        private void OnGUI()
        {
            mainScroll = ToolboxUtils.ScrollView(mainScroll, () =>
            {
                DrawTargetSelection();

                GUILayout.Space(4f);

                if (targetRoot != null && controllers.Count > 0)
                {
                    DrawSearchAreaPlaceholder();
                }
            });
        }

        private void DrawSearchScope()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("搜索范围", EditorStyles.boldLabel);

            int oldControllerScope = controllerScopeIndex;
            int oldLayerScope = layerScopeIndex;

            // 动画控制器：全部 + 每个控制器
            if (controllers.Count == 0)
            {
                EditorGUILayout.Popup("动画控制器", 0, new[] { "(无可用控制器)" });
                EditorGUILayout.Popup("层级", 0, new[] { "全部" });
                EditorGUILayout.EndVertical();
                return;
            }

            var controllerOptions = new string[controllers.Count + 1];
            controllerOptions[0] = "全部";
            for (int i = 0; i < controllers.Count; i++)
            {
                controllerOptions[i + 1] = controllerNames != null && i < controllerNames.Count
                    ? controllerNames[i]
                    : controllers[i]?.name ?? "(Controller)";
            }

            if (controllerScopeIndex < 0 || controllerScopeIndex > controllers.Count)
            {
                controllerScopeIndex = 0;
            }

            controllerScopeIndex = EditorGUILayout.Popup("动画控制器", controllerScopeIndex, controllerOptions);

            // 层级：当动画控制器为“全部”时禁用
            string[] layerOptions;
            bool layerEnabled = controllerScopeIndex > 0;
            if (!layerEnabled)
            {
                layerScopeIndex = 0;
                layerOptions = new[] { "全部" };
            }
            else
            {
                int ctrlIndex = controllerScopeIndex - 1;
                if (ctrlIndex < 0 || ctrlIndex >= controllers.Count)
                {
                    layerScopeIndex = 0;
                    layerOptions = new[] { "全部" };
                }
                else
                {
                    var ctrl = controllers[ctrlIndex];
                    var layers = ctrl != null ? ctrl.layers : null;
                    int count = layers != null ? layers.Length : 0;
                    layerOptions = new string[count + 1];
                    layerOptions[0] = "全部";
                    for (int i = 0; i < count; i++)
                    {
                        layerOptions[i + 1] = layers[i].name ?? $"Layer {i}";
                    }

                    if (layerScopeIndex < 0 || layerScopeIndex > count)
                    {
                        layerScopeIndex = 0;
                    }
                }
            }

            EditorGUI.BeginDisabledGroup(!layerEnabled);
            layerScopeIndex = EditorGUILayout.Popup("层级", layerScopeIndex, layerOptions);
            EditorGUI.EndDisabledGroup();

            // 当搜索范围（控制器或层级）发生变化时，自动刷新属性分组和结果
            if (controllerScopeIndex != oldControllerScope || layerScopeIndex != oldLayerScope)
            {
                ScanAndGroupAnimatedProperties();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawTargetSelection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("目标对象", EditorStyles.boldLabel);

            var newTarget = (GameObject)EditorGUILayout.ObjectField("Avatar / 带 Animator 的物体", targetRoot, typeof(GameObject), true);
            if (newTarget != targetRoot)
            {
                targetRoot = newTarget;
                RefreshTargetComponents();
                RefreshControllers();
            }

            if (targetRoot == null)
            {
                EditorGUILayout.HelpBox("请拖入一个 Avatar 或带 Animator 组件的物体。", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void RefreshTargetComponents()
        {
            descriptor = null;
            animator = null;
            if (targetRoot == null) return;

            descriptor = targetRoot.GetComponent<VRCAvatarDescriptor>();
            animator = targetRoot.GetComponent<Animator>();
        }

        private void RefreshControllers()
        {
            controllers.Clear();
            controllerNames.Clear();
            selectedControllerIndex = 0;
            controllerScopeIndex = 0;
            layerScopeIndex = 0;

            if (targetRoot == null) return;

            // 使用公共方法从 Avatar/Animator 根收集控制器
            controllers.AddRange(ToolboxUtils.CollectControllersFromRoot(targetRoot, includeSpecialLayers: true));
            if (controllers.Count == 0) return;

            controllerNames.AddRange(ToolboxUtils.BuildControllerDisplayNames(descriptor, animator, controllers));
        }

        private void DrawSearchAreaPlaceholder()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("查找与结果", EditorStyles.boldLabel);

            DrawAnimatedObjectSelector();

            GUILayout.Space(4f);

            DrawSearchScope();

            GUILayout.Space(4f);

            DrawPropertyGroupSelector();
            GUILayout.Space(4f);

            DrawSearchResults();

            EditorGUILayout.EndVertical();
        }

        private void DrawAnimatedObjectSelector()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("被动画控制的物体", EditorStyles.boldLabel);

            var newObj = EditorGUILayout.ObjectField("目标物体", selectedAnimatedObject, typeof(Object), true);
            if (newObj != selectedAnimatedObject)
            {
                // 规范化目标对象：限制在当前 Avatar/Animator 根下，并处理 AAO MergeSkinnedMesh
                selectedAnimatedObject = NormalizeSelectedObjectForAvatarAndAao(newObj as Object);
                selectedPath = null;
                availableGroups.Clear();
                selectedGroupIndex = 0;
                searchCompleted = false;
                foundClips.Clear();

                // 拖入新对象后，若已有控制器则自动扫描一次属性，行为贴近原 FAC
                if (selectedAnimatedObject != null && controllers.Count > 0)
                {
                    ScanAndGroupAnimatedProperties();
                }
            }

            if (selectedAnimatedObject == null)
            {
                EditorGUILayout.HelpBox("请选择 Avatar 层级中的具体对象（GameObject 或组件）。", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawPropertyGroupSelector()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("属性", EditorStyles.boldLabel);

            if (availableGroups.Count == 0)
            {
                EditorGUILayout.HelpBox("请先在上方选择目标对象，窗口会自动扫描可用属性。", MessageType.Info);
            }
            else
            {
                if (selectedGroupIndex < 0 || selectedGroupIndex > availableGroups.Count)
                {
                    selectedGroupIndex = 0;
                }

                // 第 0 项为“全部属性”，后续为实际属性分组
                var displayNames = new string[availableGroups.Count + 1];
                displayNames[0] = "全部属性";
                for (int i = 0; i < availableGroups.Count; i++)
                {
                    displayNames[i + 1] = availableGroups[i].GroupDisplayName;
                }

                var newIndex = EditorGUILayout.Popup("属性", selectedGroupIndex, displayNames);

                // 当选择的属性发生变化时自动触发搜索（包括“全部属性”）
                if (newIndex != selectedGroupIndex)
                {
                    selectedGroupIndex = newIndex;
                    searchCompleted = false;
                    foundClips.Clear();

                    FindClipsForSelectedGroup();
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSearchResults()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("结果列表", EditorStyles.boldLabel);

            // 右上角刷新按钮：根据当前属性和范围重新搜索（包括“全部属性”）
            EditorGUI.BeginDisabledGroup(selectedAnimatedObject == null || controllers.Count == 0 || selectedGroupIndex < 0 || selectedGroupIndex > availableGroups.Count);
            if (GUILayout.Button("刷新", GUILayout.Width(60f)))
            {
                FindClipsForSelectedGroup();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (!searchCompleted)
            {
                EditorGUILayout.HelpBox("尚未执行搜索。", MessageType.Info);
            }
            else if (foundClips.Count == 0)
            {
                EditorGUILayout.HelpBox("未找到任何匹配该属性的动画剪辑。", MessageType.Info);
            }
            else
            {
                for (int i = 0; i < foundClips.Count; i++)
                {
                    var info = foundClips[i];
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.ObjectField(info.Clip, typeof(AnimationClip), false);
                    EditorGUILayout.LabelField(info.Controller != null ? info.Controller.name : "(Controller)", GUILayout.Width(150f));
                    if (GUILayout.Button("跳转", GUILayout.Width(60f)))
                    {
                        AnimJumpTool.TryJumpToClip(info.Clip);
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            // 在动画剪辑结果下方追加 AQT 配置提示（基于配置本身判断是否命中当前目标对象，不读取 NDMF 最终结果）
            DrawAqtConfigHints();

            EditorGUILayout.EndVertical();
        }

        private void ScanAndGroupAnimatedProperties()
        {
            availableGroups.Clear();
            selectedGroupIndex = 0;
            searchCompleted = false;
            foundClips.Clear();

            if (selectedAnimatedObject == null || targetRoot == null)
                return;

            Transform root = targetRoot.transform;
            Transform objectTransform = null;

            if (selectedAnimatedObject is Component component)
                objectTransform = component.transform;
            else if (selectedAnimatedObject is GameObject go)
                objectTransform = go.transform;

            if (objectTransform == null)
                return;

            selectedPath = GetRelativePath(objectTransform, root);

            // 若对象不在 Avatar 根节点层级内，则不继续扫描属性
            if (string.IsNullOrEmpty(selectedPath) && objectTransform != root)
                return;

            var uniqueBindings = new Dictionary<(string path, string propertyName, System.Type type), EditorCurveBinding>();

            ForEachLayerInScope((controller, layer) =>
            {
                var clips = GetClipsFromStateMachine(layer.stateMachine);
                for (int j = 0; j < clips.Count; j++)
                {
                    var clip = clips[j];
                    if (clip == null) continue;

                    var bindings = AnimationUtility.GetCurveBindings(clip);
                    for (int k = 0; k < bindings.Length; k++)
                    {
                        var binding = bindings[k];
                        if (binding.path != selectedPath)
                            continue;

                        var key = (binding.path, binding.propertyName, binding.type);
                        if (!uniqueBindings.ContainsKey(key))
                        {
                            uniqueBindings.Add(key, binding);
                        }
                    }
                }
            });

            var tempGroups = new Dictionary<(System.Type type, string canonicalName), PropertyGroupData>();

            foreach (var binding in uniqueBindings.Values)
            {
                string canonicalName;

                var basePropertyName = binding.propertyName;
                int dotIndex = basePropertyName.IndexOf('.');
                canonicalName = dotIndex > 0 ? basePropertyName.Substring(0, dotIndex) : basePropertyName;

                var groupKey = (binding.type, canonicalName);
                if (!tempGroups.TryGetValue(groupKey, out var group))
                {
                    group = new PropertyGroupData
                    {
                        ComponentType = binding.type,
                        CanonicalPropertyName = canonicalName,
                        GroupDisplayName = $"{binding.type.Name}: {canonicalName}"
                    };
                    tempGroups.Add(groupKey, group);
                    availableGroups.Add(group);
                }

                if (!group.BoundPropertyNames.Contains(binding.propertyName))
                {
                    group.BoundPropertyNames.Add(binding.propertyName);
                }
            }

            // 根据 AQT 配置补充当前目标对象的属性分组
            AugmentGroupsWithAqtConfigForTarget();

            // 按名称排序，方便查找
            availableGroups.Sort((a, b) => string.Compare(a.GroupDisplayName, b.GroupDisplayName, System.StringComparison.Ordinal));

            // 扫描完成后，根据当前属性索引自动执行一次搜索：
            // 默认 selectedGroupIndex=0 表示“全部属性”，可直接看到所有影响该物体的动画。
            if (availableGroups.Count > 0)
            {
                if (selectedGroupIndex < 0 || selectedGroupIndex > availableGroups.Count)
                {
                    selectedGroupIndex = 0;
                }
                FindClipsForSelectedGroup();
            }
        }

        private void FindClipsForSelectedGroup()
        {
            searchCompleted = false;
            foundClips.Clear();

            if (controllers.Count == 0 || selectedGroupIndex < 0 || selectedGroupIndex > availableGroups.Count)
                return;

            // selectedGroupIndex == 0 表示“全部属性”，>0 表示具体某个属性分组
            PropertyGroupData group = null;
            bool useAllGroups = selectedGroupIndex == 0;
            if (!useAllGroups)
            {
                group = availableGroups[selectedGroupIndex - 1];
            }
            var usedClips = new HashSet<AnimationClip>();
            ForEachLayerInScope((controller, layer) =>
            {
                var clips = GetClipsFromStateMachine(layer.stateMachine);
                for (int j = 0; j < clips.Count; j++)
                {
                    var clip = clips[j];
                    if (clip == null || usedClips.Contains(clip))
                        continue;

                    var bindings = AnimationUtility.GetCurveBindings(clip);
                    bool hasMatch = false;

                    for (int k = 0; k < bindings.Length; k++)
                    {
                        var binding = bindings[k];
                        if (binding.path != selectedPath)
                            continue;

                        if (useAllGroups)
                        {
                            // “全部属性”：只要该路径下存在任意绑定即可
                            hasMatch = true;
                            break;
                        }

                        if (binding.type == group.ComponentType && group.BoundPropertyNames.Contains(binding.propertyName))
                        {
                            hasMatch = true;
                            break;
                        }
                    }

                    if (hasMatch)
                    {
                        usedClips.Add(clip);
                        foundClips.Add(new FoundClipInfo
                        {
                            Clip = clip,
                            Controller = controller
                        });
                    }
                }
            });

            searchCompleted = true;
        }

        private void ForEachLayerInScope(System.Action<AnimatorController, AnimatorControllerLayer> action)
        {
            if (action == null || controllers.Count == 0)
                return;

            // 全部控制器
            if (controllerScopeIndex <= 0)
            {
                for (int ci = 0; ci < controllers.Count; ci++)
                {
                    var ctrl = controllers[ci];
                    if (ctrl == null) continue;
                    var layers = ctrl.layers;
                    if (layers == null) continue;

                    for (int li = 0; li < layers.Length; li++)
                    {
                        action(ctrl, layers[li]);
                    }
                }
                return;
            }

            // 单个控制器
            int controllerIndex = controllerScopeIndex - 1;
            if (controllerIndex < 0 || controllerIndex >= controllers.Count)
                return;

            var controller = controllers[controllerIndex];
            if (controller == null)
                return;

            var controllerLayers = controller.layers;
            if (controllerLayers == null || controllerLayers.Length == 0)
                return;

            // 层级为“全部”
            if (layerScopeIndex <= 0)
            {
                for (int li = 0; li < controllerLayers.Length; li++)
                {
                    action(controller, controllerLayers[li]);
                }
                return;
            }

            // 指定层
            int layerIndex = layerScopeIndex - 1;
            if (layerIndex < 0 || layerIndex >= controllerLayers.Length)
                return;

            action(controller, controllerLayers[layerIndex]);
        }

        private AnimatorController GetSelectedController()
        {
            if (controllers.Count == 0)
                return null;

            if (selectedControllerIndex < 0 || selectedControllerIndex >= controllers.Count)
                return controllers[0];

            return controllers[selectedControllerIndex];
        }

        private static string GetRelativePath(Transform target, Transform root)
        {
            if (target == null || root == null)
                return string.Empty;

            if (target == root)
                return string.Empty;

            var stack = new Stack<string>();
            var current = target;
            while (current != null && current != root)
            {
                stack.Push(current.name);
                current = current.parent;
            }

            if (current != root)
                return string.Empty;

            return string.Join("/", stack.ToArray());
        }

        private static List<AnimationClip> GetClipsFromStateMachine(AnimatorStateMachine stateMachine)
        {
            var clips = new List<AnimationClip>();
            if (stateMachine == null)
                return clips;

            var states = stateMachine.states;
            for (int i = 0; i < states.Length; i++)
            {
                var state = states[i].state;
                if (state == null) continue;

                var motion = state.motion;
                CollectClipsFromMotion(motion, clips);
            }

            var subMachines = stateMachine.stateMachines;
            for (int i = 0; i < subMachines.Length; i++)
            {
                var sub = subMachines[i].stateMachine;
                if (sub != null)
                {
                    clips.AddRange(GetClipsFromStateMachine(sub));
                }
            }

            return clips;
        }

        private static void CollectClipsFromMotion(Motion motion, List<AnimationClip> clips)
        {
            if (motion == null)
                return;

            var clip = motion as AnimationClip;
            if (clip != null)
            {
                if (!clips.Contains(clip))
                {
                    clips.Add(clip);
                }
                return;
            }

            var tree = motion as BlendTree;
            if (tree != null)
            {
                var children = tree.children;
                for (int i = 0; i < children.Length; i++)
                {
                    CollectClipsFromMotion(children[i].motion, clips);
                }
            }
        }

        private class PropertyGroupData
        {
            public System.Type ComponentType;
            public string CanonicalPropertyName;
            public string GroupDisplayName;
            public readonly List<string> BoundPropertyNames = new List<string>();
        }

        private class FoundClipInfo
        {
            public AnimationClip Clip;
            public AnimatorController Controller;
        }

        /// <summary>
        /// 规范化用户选择的目标对象：限定在当前 Avatar/Animator 根下，排除其他 Animator 控制的层级，
        /// 并将 AAO MergeSkinnedMesh 合并目标映射到 MSM 节点。
        /// </summary>
        private Object NormalizeSelectedObjectForAvatarAndAao(Object obj)
        {
            if (obj == null)
                return null;

            // 确定当前作为“根”的对象：优先使用 Avatar 根，其次使用 Animator 根
            GameObject rootGo = null;
            if (descriptor != null && descriptor.gameObject != null)
            {
                rootGo = descriptor.gameObject;
            }
            else if (animator != null && animator.gameObject != null)
            {
                rootGo = animator.gameObject;
            }

            if (rootGo == null)
            {
                // 未指定受控根时，保持原有行为，仅返回原对象
                return obj;
            }

            GameObject go = null;
            if (obj is GameObject goObj)
            {
                go = goObj;
            }
            else if (obj is Component comp)
            {
                go = comp.gameObject;
            }
            else
            {
                return null;
            }

            if (go == null)
                return null;

            // 不允许将 Avatar/Animator 根物体本身作为“被动画控制的物体”
            if (go == rootGo)
                return null;

            // 必须在当前根层级内
            var rootTransform = rootGo.transform;
            var t = go.transform;
            bool underRoot = false;
            while (t != null)
            {
                if (t == rootTransform)
                {
                    underRoot = true;
                    break;
                }
                t = t.parent;
            }

            if (!underRoot)
            {
                // 不在 Avatar/Animator 根的层级里，视为无效选择
                return null;
            }

            // 排除挂在其他 Animator 下的对象
            if (controllers != null && controllers.Count > 0)
            {
                t = go.transform.parent;
                while (t != null && t != rootTransform)
                {
                    var a = t.GetComponent<Animator>();
                    if (a != null && a.runtimeAnimatorController != null)
                    {
                        var ac = a.runtimeAnimatorController as AnimatorController;
                        if (ac != null && !controllers.Contains(ac))
                        {
                            return null;
                        }
                    }
                    t = t.parent;
                }
            }

            // AAO MergeSkinnedMesh：如该对象为 SMR，尝试映射到 MSM 节点
            var smrOnGo = go.GetComponent<SkinnedMeshRenderer>();
            if (smrOnGo != null)
            {
                var msmOwner = FindAaoMergeOwnerForRendererUnderRoot(rootGo, smrOnGo);
                if (msmOwner != null)
                {
                    go = msmOwner;
                }
            }

            return go;
        }

        /// <summary>
        /// 在当前根层级下查找引用指定 SMR 的 AAO MergeSkinnedMesh 组件，仅返回同一 Avatar/Animator 下的 MSM 节点。
        /// </summary>
        private GameObject FindAaoMergeOwnerForRendererUnderRoot(GameObject root, SkinnedMeshRenderer targetSmr)
        {
            if (root == null || targetSmr == null)
                return null;

            var rootTransform = root.transform;

            // 反射获取 AAO MergeSkinnedMesh 类型：遍历已加载程序集按 FullName 查找
            System.Type mergeType = null;
            var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length && mergeType == null; i++)
            {
                var asm = assemblies[i];
                if (asm == null) continue;
                mergeType = asm.GetType("Anatawa12.AvatarOptimizer.MergeSkinnedMesh");
            }
            if (mergeType == null)
            {
                // 如无法解析类型，直接视为未合并
                return null;
            }

            var comps = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < comps.Length; i++)
            {
                var c = comps[i];
                if (c == null) continue;
                var t = c.GetType();
                if (t == null || t != mergeType) continue;

                var msmGo = c.gameObject;
                if (msmGo == null) continue;

                // MSM 节点也必须在当前根层级内
                var msmTransform = msmGo.transform;
                bool msmUnderRoot = false;
                var temp = msmTransform;
                while (temp != null)
                {
                    if (temp == rootTransform)
                    {
                        msmUnderRoot = true;
                        break;
                    }
                    temp = temp.parent;
                }
                if (!msmUnderRoot)
                    continue;

                // 读取其 renderersSet 字段，并展开为 SMR 集合
                try
                {
                    var renderersField = t.GetField("renderersSet", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var setObj = renderersField != null ? renderersField.GetValue(c) : null;
                    if (setObj == null) continue;

                    var setType = setObj.GetType();
                    var getAsSet = setType.GetMethod("GetAsSet", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    System.Collections.IEnumerable enumerable = null;
                    if (getAsSet != null)
                    {
                        enumerable = getAsSet.Invoke(setObj, null) as System.Collections.IEnumerable;
                    }
                    else if (setObj is System.Collections.IEnumerable e)
                    {
                        enumerable = e;
                    }

                    if (enumerable == null) continue;

                    foreach (var obj in enumerable)
                    {
                        if (obj is SkinnedMeshRenderer smr && smr == targetSmr)
                        {
                            return msmGo;
                        }
                    }
                }
                catch
                {
                    // 任意反射失败视为未匹配，继续下一个 MSM
                }
            }

            return null;
        }

        // 若 Avatar 上存在 QuickToggleConfig，则根据配置为当前目标对象补充 IsActive / BlendShape 分组
        private void AugmentGroupsWithAqtConfigForTarget()
        {
            if (descriptor == null || selectedAnimatedObject == null)
                return;

            var avatarRoot = descriptor.gameObject;
            if (avatarRoot == null)
                return;

            var config = avatarRoot.GetComponent<QuickToggleConfig>();
            if (config == null || config.layerConfigs == null || config.layerConfigs.Count == 0)
                return;

            var targetGO = (selectedAnimatedObject as GameObject) ?? (selectedAnimatedObject as Component)?.gameObject;
            if (targetGO == null)
                return;

            bool needActiveGroup = false;
            bool needBlendShapeGroup = false;

            for (int i = 0; i < config.layerConfigs.Count; i++)
            {
                var layer = config.layerConfigs[i];
                if (layer == null)
                    continue;

                switch (layer.layerType)
                {
                    case 0: // Bool
                        if (layer.boolTargets != null)
                        {
                            for (int j = 0; j < layer.boolTargets.Count; j++)
                            {
                                var t = layer.boolTargets[j];
                                if (t == null || t.targetObject != targetGO)
                                    continue;

                                if (t.controlType == QuickToggleConfig.TargetControlType.GameObject)
                                    needActiveGroup = true;
                                else if (t.controlType == QuickToggleConfig.TargetControlType.BlendShape)
                                    needBlendShapeGroup = true;
                            }
                        }
                        break;
                    case 1: // Int
                        if (layer.intGroups != null)
                        {
                            for (int g = 0; g < layer.intGroups.Count; g++)
                            {
                                var grp = layer.intGroups[g];
                                if (grp?.targetItems == null) continue;
                                for (int j = 0; j < grp.targetItems.Count; j++)
                                {
                                    var t = grp.targetItems[j];
                                    if (t == null || t.targetObject != targetGO)
                                        continue;

                                    if (t.controlType == QuickToggleConfig.TargetControlType.GameObject)
                                        needActiveGroup = true;
                                    else if (t.controlType == QuickToggleConfig.TargetControlType.BlendShape)
                                        needBlendShapeGroup = true;
                                }
                            }
                        }
                        break;
                    case 2: // Float（仅 BlendShape）
                        if (layer.floatTargets != null)
                        {
                            for (int j = 0; j < layer.floatTargets.Count; j++)
                            {
                                var t = layer.floatTargets[j];
                                if (t == null || t.targetObject != targetGO)
                                    continue;

                                needBlendShapeGroup = true;
                            }
                        }
                        break;
                }
            }

            // 若没有任何 AQT 配置命中当前对象，则不补充分组
            if (!needActiveGroup && !needBlendShapeGroup)
                return;

            // 检查现有分组中是否已经包含对应类型
            bool hasActiveGroup = false;
            bool hasBlendShapeGroup = false;
            for (int i = 0; i < availableGroups.Count; i++)
            {
                var g = availableGroups[i];
                if (g == null) continue;

                if (g.ComponentType == typeof(GameObject) && g.CanonicalPropertyName == "m_IsActive")
                    hasActiveGroup = true;
                if (g.ComponentType == typeof(SkinnedMeshRenderer) && g.CanonicalPropertyName == "blendShape")
                    hasBlendShapeGroup = true;
            }

            if (needActiveGroup && !hasActiveGroup)
            {
                var group = new PropertyGroupData
                {
                    ComponentType = typeof(GameObject),
                    CanonicalPropertyName = "m_IsActive",
                    GroupDisplayName = "GameObject: IsActive"
                };
                group.BoundPropertyNames.Add("m_IsActive");
                availableGroups.Add(group);
            }

            if (needBlendShapeGroup && !hasBlendShapeGroup)
            {
                var group = new PropertyGroupData
                {
                    ComponentType = typeof(SkinnedMeshRenderer),
                    CanonicalPropertyName = "blendShape",
                    GroupDisplayName = "SkinnedMeshRenderer: blendShape"
                };
                // 对于只由 AQT 配置控制而没有实际曲线的情况，BoundPropertyNames 不用于匹配现有动画，可留空或添加占位
                availableGroups.Add(group);
            }
        }

        // 基于 AQT QuickToggleConfig 配置，提示当前目标对象会被哪些配置控制
        private void DrawAqtConfigHints()
        {
            if (descriptor == null || selectedAnimatedObject == null)
                return;

            var avatarRoot = descriptor.gameObject;
            if (avatarRoot == null)
                return;

            var config = avatarRoot.GetComponent<QuickToggleConfig>();
            if (config == null || config.layerConfigs == null || config.layerConfigs.Count == 0)
                return;

            var targetGO = (selectedAnimatedObject as GameObject) ?? (selectedAnimatedObject as Component)?.gameObject;
            if (targetGO == null)
                return;

            // 仅在以下两种范围下显示 AQT 提示：
            // 1) 动画控制器为“全部”；
            // 2) 控制器为 Avatar 的 FX 且层级为“全部”。
            bool allowScope = false;
            if (controllerScopeIndex <= 0)
            {
                allowScope = true;
            }
            else if (controllerScopeIndex - 1 >= 0 && controllerScopeIndex - 1 < controllers.Count)
            {
                var selectedController = controllers[controllerScopeIndex - 1];
                if (selectedController != null && layerScopeIndex <= 0)
                {
                    var baseLayers = descriptor.baseAnimationLayers ?? System.Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                    for (int i = 0; i < baseLayers.Length; i++)
                    {
                        var l = baseLayers[i];
                        if (l.type == VRCAvatarDescriptor.AnimLayerType.FX && l.animatorController == selectedController)
                        {
                            allowScope = true;
                            break;
                        }
                    }
                }
            }

            if (!allowScope)
                return;

            // 属性要求：
            // - 选中具体分组时仅在 BlendShape / GameObject IsActive 下显示；
            // - 选中“全部属性”时，只要配置目标包含该对象就显示。
            if (selectedGroupIndex < 0 || selectedGroupIndex > availableGroups.Count)
                return;

            bool isAllProperties = selectedGroupIndex == 0;

            PropertyGroupData currentGroup = null;
            bool isBlendShapeGroup = false;
            bool isActiveGroup = false;

            if (!isAllProperties)
            {
                currentGroup = availableGroups[selectedGroupIndex - 1];
                isBlendShapeGroup =
                    currentGroup.ComponentType == typeof(SkinnedMeshRenderer) &&
                    currentGroup.CanonicalPropertyName == "blendShape";

                isActiveGroup =
                    currentGroup.ComponentType == typeof(GameObject) &&
                    (currentGroup.CanonicalPropertyName == "m_IsActive" || currentGroup.CanonicalPropertyName == "IsActive");

                if (!isBlendShapeGroup && !isActiveGroup)
                    return;
            }

            var hits = new List<string>();

            for (int i = 0; i < config.layerConfigs.Count; i++)
            {
                var layer = config.layerConfigs[i];
                if (layer == null)
                    continue;

                bool affectsTarget = false;

                switch (layer.layerType)
                {
                    case 0:
                        if (layer.boolTargets != null)
                        {
                            for (int j = 0; j < layer.boolTargets.Count; j++)
                            {
                                var t = layer.boolTargets[j];
                                if (t == null || t.targetObject != targetGO)
                                    continue;

                                if (isAllProperties)
                                {
                                    // “全部属性”模式下：只要配置目标中包含当前物体即可认为受影响
                                    affectsTarget = true;
                                    break;
                                }

                                // Bool：GameObject/BlendShape 区分
                                if (isBlendShapeGroup && t.controlType == QuickToggleConfig.TargetControlType.BlendShape)
                                {
                                    affectsTarget = true;
                                    break;
                                }
                                if (isActiveGroup && t.controlType == QuickToggleConfig.TargetControlType.GameObject)
                                {
                                    affectsTarget = true;
                                    break;
                                }
                            }
                        }
                        break;
                    case 1:
                        if (layer.intGroups != null)
                        {
                            for (int g = 0; g < layer.intGroups.Count && !affectsTarget; g++)
                            {
                                var grp = layer.intGroups[g];
                                if (grp?.targetItems == null) continue;
                                for (int j = 0; j < grp.targetItems.Count; j++)
                                {
                                    var t = grp.targetItems[j];
                                    if (t == null || t.targetObject != targetGO)
                                        continue;

                                    if (isAllProperties)
                                    {
                                        affectsTarget = true;
                                        break;
                                    }

                                    // Int：GameObject/BlendShape 区分
                                    if (isBlendShapeGroup && t.controlType == QuickToggleConfig.TargetControlType.BlendShape)
                                    {
                                        affectsTarget = true;
                                        break;
                                    }
                                    if (isActiveGroup && t.controlType == QuickToggleConfig.TargetControlType.GameObject)
                                    {
                                        affectsTarget = true;
                                        break;
                                    }
                                }
                            }
                        }
                        break;
                    case 2:
                        if (layer.floatTargets != null)
                        {
                            for (int j = 0; j < layer.floatTargets.Count; j++)
                            {
                                var t = layer.floatTargets[j];
                                if (t == null || t.targetObject != targetGO)
                                    continue;

                                if (isAllProperties)
                                {
                                    affectsTarget = true;
                                    break;
                                }

                                // Float：仅在 BlendShape 组时认为命中
                                if (isBlendShapeGroup)
                                {
                                    affectsTarget = true;
                                    break;
                                }
                            }
                        }
                        break;
                }

                if (!affectsTarget)
                    continue;

                string typeLabel = layer.layerType == 0 ? "Bool" : layer.layerType == 1 ? "Int" : "Float";
                string name = !string.IsNullOrEmpty(layer.displayName) ? layer.displayName : (!string.IsNullOrEmpty(layer.layerName) ? layer.layerName : "(未命名配置)");
                hits.Add($"{name} ({typeLabel})");
            }

            if (hits.Count == 0)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Avatar Quick Toggle 配置", EditorStyles.boldLabel);
            for (int i = 0; i < hits.Count; i++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(hits[i]);
                EditorGUILayout.EndVertical();
            }
        }
    }
}
