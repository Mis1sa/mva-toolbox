using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.AnimationBakeTool
{
    /// <summary>
    /// 默认值烘焙业务逻辑：负责校验上下文、遍历状态机与生成/替换动画剪辑。
    /// </summary>
    public sealed class AnimationBakeToolService
    {
        public bool TryResolveLayer(GameObject targetRoot, AnimatorController controller, int selectedLayerIndex, out AnimatorControllerLayer layer, out string errorMessage)
        {
            layer = null;
            errorMessage = null;

            if (targetRoot == null)
            {
                errorMessage = "请先选择 Avatar 或带 Animator 的物体。";
                return false;
            }

            if (controller == null)
            {
                errorMessage = "请在顶部选择目标控制器。";
                return false;
            }

            var layers = controller.layers ?? Array.Empty<AnimatorControllerLayer>();
            if (layers.Length == 0)
            {
                errorMessage = "当前控制器没有任何层级。";
                return false;
            }

            if (selectedLayerIndex < 0)
            {
                errorMessage = "默认值烘焙模式需要指定具体层级，请在顶部选择。";
                return false;
            }

            if (selectedLayerIndex >= layers.Length)
            {
                errorMessage = "所选层级索引已失效，请重新选择。";
                return false;
            }

            layer = layers[selectedLayerIndex];
            if (LayerHasBlendTree(layer.stateMachine))
            {
                errorMessage = "默认值烘焙不支持包含 BlendTree 的层，请在顶部改选其它层级。";
                layer = null;
                return false;
            }

            return true;
        }

        public void ExecuteBake(GameObject targetRoot,
            Transform controllerRoot,
            AnimatorController controller,
            AnimatorControllerLayer layer,
            string suffixName,
            string saveFolderRelative,
            bool onlyGenerateClips,
            string defaultRootFolder)
        {
            if (controller == null || layer == null)
                throw new ArgumentNullException(nameof(layer), "执行烘焙前需要有效控制器与层级。");

            bool confirm = EditorUtility.DisplayDialog(
                "执行默认值烘焙",
                $"将对控制器 '{controller.name}' 的层 '{layer.name}' 进行动画剪辑复制与默认值补齐。",
                "执行",
                "取消");
            if (!confirm) return;

            Transform resolvedControllerRoot = controllerRoot ?? targetRoot?.transform;
            AnimationBakeDefaultValueResolver defaultValueResolver = new AnimationBakeDefaultValueResolver(targetRoot, resolvedControllerRoot);
            ProcessSelectedLayer(controller, layer, defaultValueResolver, suffixName, saveFolderRelative, onlyGenerateClips, defaultRootFolder);
        }

        private void ProcessSelectedLayer(AnimatorController controller,
            AnimatorControllerLayer layer,
            AnimationBakeDefaultValueResolver defaultValueResolver,
            string suffixName,
            string saveFolderRelative,
            bool onlyGenerateClips,
            string defaultRootFolder)
        {
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

            string timeStamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string layerSegment = string.IsNullOrEmpty(layer.name) ? "Layer" : SanitizePathSegment(layer.name);

            string rootFolder = NormalizeSaveFolder(saveFolderRelative, defaultRootFolder);
            string folderRelative = $"{rootFolder}/{layerSegment}_{timeStamp}";
            EnsureFolderExists(rootFolder);
            EnsureFolderExists(folderRelative);

            string assetFolder = folderRelative.EndsWith("/") ? folderRelative : folderRelative + "/";

            AssetDatabase.StartAssetEditing();

            try
            {
                var defaultValueMap = new Dictionary<EditorCurveBinding, object>();
                foreach (var binding in allBindings)
                {
                    var v = defaultValueResolver.GetDefaultValue(binding);
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
                    if (!string.IsNullOrEmpty(suffixName))
                    {
                        defaultClipName += "_" + suffixName;
                    }

                    defaultClip.name = defaultClipName;
                    string defaultClipPath = AssetDatabase.GenerateUniqueAssetPath($"{assetFolder}{defaultClipName}.anim");

                    foreach (var kv in defaultValueMap)
                    {
                        ApplyDefaultValueToClip(defaultClip, kv.Key, kv.Value);
                    }

                    AssetDatabase.CreateAsset(defaultClip, defaultClipPath);
                }

                if (defaultClip != null && !onlyGenerateClips)
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
                    if (!string.IsNullOrEmpty(suffixName))
                    {
                        newClipName += "_" + suffixName;
                    }

                    string newClipPath = AssetDatabase.GenerateUniqueAssetPath($"{assetFolder}{newClipName}.anim");
                    var newClip = new AnimationClip();
                    if (originalClip != null)
                    {
                        EditorUtility.CopySerialized(originalClip, newClip);
                    }
                    newClip.name = newClipName;
                    AssetDatabase.CreateAsset(newClip, newClipPath);

                    if (!onlyGenerateClips)
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

        private static string NormalizeSaveFolder(string input, string defaultRootFolder)
        {
            string rootFolder = input;
            if (!string.IsNullOrEmpty(rootFolder))
            {
                rootFolder = rootFolder.Trim().Replace("\\", "/");
            }

            if (string.IsNullOrEmpty(rootFolder) ||
                !(string.Equals(rootFolder, "Assets", StringComparison.OrdinalIgnoreCase) ||
                  rootFolder.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)))
            {
                rootFolder = defaultRootFolder;
            }

            return rootFolder;
        }

        private static string SanitizePathSegment(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                return "Layer";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            char[] chars = segment.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (invalid.Contains(chars[i]))
                {
                    chars[i] = '_';
                }
            }

            string result = new string(chars).Trim();
            return string.IsNullOrEmpty(result) ? "Layer" : result;
        }

        private static void EnsureFolderExists(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            string[] parts = assetPath.Split('/');
            if (parts.Length == 0 || parts[0] != "Assets")
            {
                return;
            }

            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        private void TraverseStateMachine(
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

        private static bool LayerHasBlendTree(AnimatorStateMachine stateMachine)
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

        private static void ApplyDefaultValueToClip(AnimationClip clip, EditorCurveBinding binding, object defaultValue)
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
    }
}
