using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace MVA.Toolbox.FindAnimation.Editor
{
    // 右键菜单：从 AnimationClip 跳转到相关 Animator / Avatar
    public static class AnimJumpTool
    {
        private class FoundControllerInfo
        {
            public GameObject TargetGameObject;
            public AnimatorController FoundController;
            public bool IsFromVrcDescriptor;
        }

        [MenuItem("Assets/MVA Toolbox/Jump To Animator", false, 101)]
        private static void JumpToAnimatorMenu()
        {
            var clip = Selection.activeObject as AnimationClip;
            if (clip == null)
            {
                return;
            }

            TryJumpToClip(clip);
        }

        [MenuItem("Assets/MVA Toolbox/Jump To Animator", true)]
        private static bool JumpToAnimatorMenuValidate()
        {
            if (Selection.objects.Length != 1)
            {
                return false;
            }

            return Selection.activeObject is AnimationClip;
        }

        // 从 AnimationClip 跳转到场景中的 Avatar / Animator，并在 Animation 窗口中显示该剪辑
        public static bool TryJumpToClip(AnimationClip clip)
        {
            if (clip == null)
            {
                return false;
            }

            Selection.activeObject = clip;

            try
            {
                var info = FindFirstControllerInScene(clip);
                if (info != null)
                {
                    var target = info.TargetGameObject;
                    var controller = info.FoundController;

                    if (target == null || controller == null)
                    {
                        return false;
                    }

                    if (info.IsFromVrcDescriptor)
                    {
                        var animator = target.GetComponent<Animator>();
                        if (animator != null)
                        {
                            animator.runtimeAnimatorController = controller;
                        }
                    }

                    Selection.activeGameObject = target;

                    if (!ForceAnimationWindowToShowClip(clip, target))
                    {
                        EditorUtility.DisplayDialog(
                            "Jump To Animator",
                            $"已定位到对象 '{target.name}'，但无法自动在 Animation 窗口中显示该剪辑，请确认 Animation 窗口已打开并重试。",
                            "确定");
                    }

                    return true;
                }

                // 场景中未找到，提示在项目资产中搜索
                ShowAssetSearchPrompt(clip);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AnimJumpTool] 跳转过程中发生异常: {ex.Message}");
            }

            return false;
        }

        private static FoundControllerInfo FindFirstControllerInScene(AnimationClip clip)
        {
            if (clip == null)
                return null;

            var descriptors = UnityEngine.Object.FindObjectsOfType<VRCAvatarDescriptor>(true);
            var animators = UnityEngine.Object.FindObjectsOfType<Animator>(true);

            var candidates = new HashSet<GameObject>();
            for (int i = 0; i < descriptors.Length; i++)
            {
                var d = descriptors[i];
                if (d != null && d.gameObject != null)
                {
                    candidates.Add(d.gameObject);
                }
            }

            for (int i = 0; i < animators.Length; i++)
            {
                var a = animators[i];
                if (a != null && a.gameObject != null)
                {
                    candidates.Add(a.gameObject);
                }
            }

            foreach (var go in candidates)
            {
                if (go == null) continue;

                var descriptor = go.GetComponent<VRCAvatarDescriptor>();
                if (descriptor != null)
                {
                    var controllerFromDescriptor = GetControllerFromDescriptor(descriptor, clip);
                    if (controllerFromDescriptor != null)
                    {
                        return new FoundControllerInfo
                        {
                            TargetGameObject = go,
                            FoundController = controllerFromDescriptor,
                            IsFromVrcDescriptor = true
                        };
                    }
                }

                var animator = go.GetComponent<Animator>();
                if (animator != null && animator.runtimeAnimatorController is AnimatorController ac)
                {
                    if (IsClipReferencedByController(ac, clip))
                    {
                        return new FoundControllerInfo
                        {
                            TargetGameObject = go,
                            FoundController = ac,
                            IsFromVrcDescriptor = false
                        };
                    }
                }
            }

            return null;
        }

        private static AnimatorController GetControllerFromDescriptor(VRCAvatarDescriptor descriptor, AnimationClip clip)
        {
            if (descriptor == null || clip == null)
                return null;

            var baseLayers = descriptor.baseAnimationLayers;
            if (baseLayers != null)
            {
                for (int i = 0; i < baseLayers.Length; i++)
                {
                    var layer = baseLayers[i];
                    if (layer.animatorController is AnimatorController ac && IsClipReferencedByController(ac, clip))
                    {
                        return ac;
                    }
                }
            }

            var specialLayers = descriptor.specialAnimationLayers;
            if (specialLayers != null)
            {
                for (int i = 0; i < specialLayers.Length; i++)
                {
                    var layer = specialLayers[i];
                    if (layer.animatorController is AnimatorController ac && IsClipReferencedByController(ac, clip))
                    {
                        return ac;
                    }
                }
            }

            return null;
        }

        private static void ShowAssetSearchPrompt(AnimationClip clip)
        {
            bool shouldSearchAssets = EditorUtility.DisplayDialog(
                "在场景中未找到引用",
                $"场景中未找到引用动画剪辑 '{clip.name}' 的 Animator 或 VRCAvatarDescriptor。是否在项目资产中搜索引用？",
                "在资产中搜索",
                "取消");

            if (!shouldSearchAssets)
                return;

            FindAndSelectInAssets(clip);
        }

        private static void FindAndSelectInAssets(AnimationClip clip)
        {
            var foundControllers = new List<AnimatorController>();

            try
            {
                string[] guids = AssetDatabase.FindAssets("t:AnimatorController");
                int total = guids.Length;

                for (int i = 0; i < total; i++)
                {
                    string guid = guids[i];
                    string path = AssetDatabase.GUIDToAssetPath(guid);

                    if (total > 0)
                    {
                        float progress = (float)i / total;
                        EditorUtility.DisplayProgressBar("搜索 Animator Controller", path, progress);
                    }

                    var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                    if (controller != null && IsClipReferencedByController(controller, clip))
                    {
                        foundControllers.Add(controller);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (foundControllers.Count > 0)
            {
                AssetSelectionWindow.ShowWindow(clip, foundControllers);
            }
            else
            {
                EditorUtility.DisplayDialog(
                    "未找到引用",
                    $"在项目资产中也未找到引用动画剪辑 '{clip.name}' 的 Animator Controller。",
                    "确定");
            }
        }

        private static bool ForceAnimationWindowToShowClip(AnimationClip clip, GameObject targetGameObject)
        {
            if (clip == null || targetGameObject == null)
                return false;

            try
            {
                var editorAssembly = Assembly.GetAssembly(typeof(EditorWindow));
                var animationWindowType = editorAssembly.GetType("UnityEditor.AnimationWindow");
                var selectionItemType = editorAssembly.GetType("UnityEditorInternal.AnimationWindowSelectionItem");

                if (animationWindowType == null || selectionItemType == null)
                    return false;

                var animationWindow = EditorWindow.GetWindow(animationWindowType, false, "Animation");
                if (animationWindow == null)
                {
                    animationWindow = EditorWindow.GetWindow(animationWindowType, false, "Animation", true);
                    if (animationWindow == null) return false;
                }

                var animEditorField = animationWindowType.GetField("m_AnimEditor", BindingFlags.Instance | BindingFlags.NonPublic);
                var animEditor = animEditorField?.GetValue(animationWindow);
                if (animEditor == null) return false;

                var stateField = animEditor.GetType().GetField("m_State", BindingFlags.Instance | BindingFlags.NonPublic);
                var stateInstance = stateField?.GetValue(animEditor);
                if (stateInstance == null) return false;

                var stateType = stateInstance.GetType();
                var selectionProperty = stateType.GetProperty("selection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (selectionProperty == null || selectionProperty.PropertyType != selectionItemType) return false;

                var createInstanceMethod = typeof(ScriptableObject).GetMethod("CreateInstance", new Type[] { typeof(Type) });
                var newSelectionItem = createInstanceMethod?.Invoke(null, new object[] { selectionItemType });
                if (newSelectionItem == null) return false;

                var clipField = selectionItemType.GetField("m_AnimationClip", BindingFlags.Instance | BindingFlags.NonPublic);
                clipField?.SetValue(newSelectionItem, clip);

                var gameObjectField = selectionItemType.GetField("m_GameObject", BindingFlags.Instance | BindingFlags.NonPublic);
                gameObjectField?.SetValue(newSelectionItem, targetGameObject);

                EditorApplication.delayCall += () =>
                {
                    selectionProperty.SetValue(stateInstance, newSelectionItem);

                    var onSelectionUpdatedMethod = animEditor.GetType().GetMethod("OnSelectionUpdated", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    onSelectionUpdatedMethod?.Invoke(animEditor, null);

                    animationWindow.Focus();
                    animationWindow.Repaint();
                };

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AnimJumpTool] 打开 Animation 窗口失败: {ex.Message}");
                return false;
            }
        }

        private static bool IsClipReferencedByController(AnimatorController controller, AnimationClip clip)
        {
            if (controller == null || clip == null) return false;

            var layers = controller.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                if (CheckStateMachine(layer.stateMachine, clip))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CheckStateMachine(AnimatorStateMachine stateMachine, AnimationClip clip)
        {
            if (stateMachine == null) return false;

            var states = stateMachine.states;
            for (int i = 0; i < states.Length; i++)
            {
                var state = states[i];
                if (CheckMotion(state.state.motion, clip))
                {
                    return true;
                }
            }

            var subMachines = stateMachine.stateMachines;
            for (int i = 0; i < subMachines.Length; i++)
            {
                var sub = subMachines[i];
                if (CheckStateMachine(sub.stateMachine, clip))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CheckMotion(Motion motion, AnimationClip clip)
        {
            if (motion == null || clip == null) return false;

            if (motion == clip)
            {
                return true;
            }

            var tree = motion as BlendTree;
            if (tree != null)
            {
                var children = tree.children;
                for (int i = 0; i < children.Length; i++)
                {
                    if (CheckMotion(children[i].motion, clip))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    internal class AssetSelectionWindow : EditorWindow
    {
        private AnimationClip _targetClip;
        private List<AnimatorController> _foundControllers;
        private Vector2 _scrollPosition;

        public static void ShowWindow(AnimationClip clip, List<AnimatorController> controllers)
        {
            var window = GetWindow<AssetSelectionWindow>("选择动画控制器");
            window._targetClip = clip;
            window._foundControllers = controllers;
            window.minSize = new Vector2(400f, 300f);
            window.Show();
        }

        private void OnGUI()
        {
            if (_targetClip == null || _foundControllers == null || !_foundControllers.Any())
            {
                Close();
                return;
            }

            EditorGUILayout.LabelField($"动画剪辑: {_targetClip.name}", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("以下 Animator Controller 引用了该剪辑，请选择一个以定位:", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            for (int i = 0; i < _foundControllers.Count; i++)
            {
                var controller = _foundControllers[i];
                if (controller == null) continue;

                EditorGUILayout.BeginHorizontal(GUI.skin.box);
                EditorGUILayout.ObjectField(controller.name, controller, typeof(AnimatorController), false);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(10f);

            if (GUILayout.Button("取消"))
            {
                Close();
            }
        }
    }
}
