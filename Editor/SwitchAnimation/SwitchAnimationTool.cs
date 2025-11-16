using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEditor.Animations;

namespace MVA.Toolbox.SwitchAnimation.Editor
{
    // 右键菜单 / 快捷键：在 Animation 窗口中切换同一物体上的 AnimationClip
    [InitializeOnLoad]
    public static class SwitchAnimationTool
    {
        private const string LogPrefix = "[SwitchAnimation] ";
        private const string PreviousShortcut = "#&%Z";  // Ctrl + Alt + Shift + Z
        private const string NextShortcut = "#&%X";      // Ctrl + Alt + Shift + X
        private const int MenuPriority = 52;
        private const string MenuPath = "Tools/MVA Toolbox/Switch Anim/";

        private static GlobalInputCatcher s_InputCatcher;

        static SwitchAnimationTool()
        {
            EditorApplication.delayCall += EnableInputCatcher;
        }

        // 启用隐藏窗口，用于捕获全局快捷键
        private static void EnableInputCatcher()
        {
            if (s_InputCatcher == null)
            {
                s_InputCatcher = ScriptableObject.CreateInstance<GlobalInputCatcher>();
                s_InputCatcher.Initialize();
            }
        }

        private class GlobalInputCatcher : EditorWindow
        {
            public void Initialize()
            {
                EditorApplication.update += Repaint;
            }

            private void OnDestroy()
            {
                EditorApplication.update -= Repaint;
                if (s_InputCatcher == this)
                {
                    s_InputCatcher = null;
                }
            }

            private void OnGUI()
            {
                var currentEvent = Event.current;
                if (currentEvent.type != EventType.KeyDown)
                    return;

                bool isCombo = currentEvent.control && currentEvent.alt && currentEvent.shift;
                if (!isCombo)
                    return;

                if (currentEvent.keyCode == KeyCode.Z)
                {
                    CallSwitch(isNext: false);
                    currentEvent.Use();
                }
                else if (currentEvent.keyCode == KeyCode.X)
                {
                    CallSwitch(isNext: true);
                    currentEvent.Use();
                }
            }
        }

        [MenuItem(MenuPath + "上一个动画 " + PreviousShortcut, priority = MenuPriority)]
        private static void SwitchToPreviousClip()
        {
            CallSwitch(isNext: false);
        }

        [MenuItem(MenuPath + "下一个动画 " + NextShortcut, priority = MenuPriority)]
        private static void SwitchToNextClip()
        {
            CallSwitch(isNext: true);
        }

        [MenuItem(MenuPath + "上一个动画 " + PreviousShortcut, true)]
        [MenuItem(MenuPath + "下一个动画 " + NextShortcut, true)]
        private static bool ValidateClipMenu()
        {
            return GetAnimationWindow() != null && Selection.activeGameObject != null;
        }

        // 从当前 Animation 窗口和选中物体中，切换到上一/下一个 AnimationClip
        private static void CallSwitch(bool isNext)
        {
            var animationWindow = GetAnimationWindow();
            if (animationWindow == null || Selection.activeGameObject == null)
                return;

            var animEditor = GetAnimEditor(animationWindow);
            if (animEditor == null) return;

            var stateInstance = GetStateInstance(animEditor);
            if (stateInstance == null) return;

            var stateType = stateInstance.GetType();

            var clipsList = GetClipsListViaPublicUtility(stateInstance, stateType);
            if (clipsList == null || clipsList.Count == 0)
            {
                Debug.LogWarning(LogPrefix + "无法获取动画列表，请尝试重新选择对象或动画控制器。");
                return;
            }

            var currentClip = GetCurrentActiveClip(stateInstance, stateType);
            AnimationClip targetClip = null;

            int currentIndex = -1;
            if (currentClip != null)
            {
                currentIndex = clipsList.IndexOf(currentClip);
            }

            int targetIndex = isNext ? currentIndex + 1 : currentIndex - 1;

            if (currentIndex == -1)
            {
                targetIndex = isNext ? 0 : clipsList.Count - 1;
            }
            else if (targetIndex >= clipsList.Count)
            {
                targetIndex = 0;
            }
            else if (targetIndex < 0)
            {
                targetIndex = clipsList.Count - 1;
            }

            if (targetIndex >= 0 && targetIndex < clipsList.Count)
            {
                targetClip = clipsList[targetIndex];
            }

            if (targetClip == null)
                return;

            if (TrySwitchClip(stateInstance, stateType, targetClip))
            {
                CallAnimEditorMethod(animEditor, "OnSelectionUpdated");
                animationWindow.Repaint();
                animationWindow.Focus();
            }
        }

        // 使用 AnimationUtility.GetAnimationClips 收集并自然排序当前物体上的所有剪辑
        private static List<AnimationClip> GetClipsListViaPublicUtility(object stateInstance, Type stateType)
        {
            var selectionProperty = stateType.GetProperty("selection", BindingFlags.Instance | BindingFlags.NonPublic);
            var currentSelectionItem = selectionProperty?.GetValue(stateInstance);

            var selectionItemType = currentSelectionItem?.GetType()
                ?? Type.GetType("UnityEditorInternal.AnimationWindowSelectionItem, UnityEditor");

            GameObject rootGameObject = null;
            if (currentSelectionItem != null)
            {
                var gameObjectField = selectionItemType.GetField("m_GameObject", BindingFlags.Instance | BindingFlags.NonPublic);
                rootGameObject = gameObjectField?.GetValue(currentSelectionItem) as GameObject;
            }

            if (rootGameObject == null)
                rootGameObject = Selection.activeGameObject;

            if (rootGameObject == null)
                return null;

            var clipsArray = AnimationUtility.GetAnimationClips(rootGameObject);
            if (clipsArray == null)
                return null;

            var clipsList = clipsArray.ToList();
            clipsList.Sort((c1, c2) => new NaturalStringComparer().Compare(c1.name, c2.name));
            return clipsList;
        }

        private static AnimationClip GetCurrentActiveClip(object stateInstance, Type stateType)
        {
            var activeClipProperty = stateType.GetProperty("activeAnimationClip", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (activeClipProperty != null)
            {
                return activeClipProperty.GetValue(stateInstance) as AnimationClip;
            }

            var selectionProperty = stateType.GetProperty("selection", BindingFlags.Instance | BindingFlags.NonPublic);
            var currentSelectionItem = selectionProperty?.GetValue(stateInstance);

            if (currentSelectionItem == null)
                return null;

            var selectionItemType = currentSelectionItem.GetType();
            var clipField = selectionItemType.GetField("m_AnimationClip", BindingFlags.Instance | BindingFlags.NonPublic);
            return clipField?.GetValue(currentSelectionItem) as AnimationClip;
        }

        private static EditorWindow GetAnimationWindow()
        {
            var animationWindowType = Type.GetType("UnityEditor.AnimationWindow, UnityEditor");
            if (animationWindowType == null)
                return null;

            var getWindowMethod = typeof(EditorWindow).GetMethod(
                "GetWindowIfOpen",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            if (getWindowMethod != null)
            {
                return getWindowMethod.MakeGenericMethod(animationWindowType).Invoke(null, null) as EditorWindow;
            }

            return EditorWindow.GetWindow(animationWindowType, false, "Animation") as EditorWindow;
        }

        private static object GetAnimEditor(EditorWindow animationWindow)
        {
            var animationWindowType = animationWindow.GetType();
            var animEditorField = animationWindowType.GetField("m_AnimEditor", BindingFlags.Instance | BindingFlags.NonPublic);
            return animEditorField?.GetValue(animationWindow);
        }

        private static object GetStateInstance(object animEditor)
        {
            if (animEditor == null) return null;
            var animEditorType = animEditor.GetType();
            var stateField = animEditorType.GetField("m_State", BindingFlags.Instance | BindingFlags.NonPublic);
            return stateField?.GetValue(animEditor);
        }

        private static bool TrySwitchClip(object stateInstance, Type stateType, AnimationClip targetClip)
        {
            var activeClipProperty = stateType.GetProperty("activeAnimationClip", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (activeClipProperty != null && activeClipProperty.CanWrite)
            {
                activeClipProperty.SetValue(stateInstance, targetClip);
                return true;
            }

            var selectionItemType = Type.GetType("UnityEditorInternal.AnimationWindowSelectionItem, UnityEditor");
            if (selectionItemType == null)
                return false;

            var selectionProperty = stateType.GetProperty("selection", BindingFlags.Instance | BindingFlags.NonPublic);
            if (selectionProperty == null)
                return false;

            var currentSelectionItem = selectionProperty.GetValue(stateInstance);

            GameObject targetGameObject = null;
            if (currentSelectionItem != null)
            {
                var gameObjectField = selectionItemType.GetField("m_GameObject", BindingFlags.Instance | BindingFlags.NonPublic);
                targetGameObject = gameObjectField?.GetValue(currentSelectionItem) as GameObject;
            }

            if (targetGameObject == null)
                targetGameObject = Selection.activeGameObject;

            var newSelectionItem = Activator.CreateInstance(selectionItemType, true);

            var clipField = selectionItemType.GetField("m_AnimationClip", BindingFlags.Instance | BindingFlags.NonPublic);
            clipField?.SetValue(newSelectionItem, targetClip);

            var gameObjectFieldFinal = selectionItemType.GetField("m_GameObject", BindingFlags.Instance | BindingFlags.NonPublic);
            gameObjectFieldFinal?.SetValue(newSelectionItem, targetGameObject);

            selectionProperty.SetValue(stateInstance, newSelectionItem);

            return true;
        }

        private static void CallAnimEditorMethod(object animEditor, string methodName)
        {
            if (animEditor == null) return;
            var animEditorType = animEditor.GetType();
            var method = animEditorType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            method?.Invoke(animEditor, null);
        }

        // 字符串自然排序（数字部分按数值比较），用于对 Clip 名称排序
        private class NaturalStringComparer : IComparer<string>
        {
            private static readonly Regex _re = new Regex(@"(\d+)", RegexOptions.Compiled);

            public int Compare(string x, string y)
            {
                if (x == y) return 0;
                if (x == null) return -1;
                if (y == null) return 1;

                var xParts = _re.Split(x);
                var yParts = _re.Split(y);

                for (int i = 0; i < Math.Min(xParts.Length, yParts.Length); i++)
                {
                    var xPart = xParts[i];
                    var yPart = yParts[i];

                    if (i % 2 == 1)
                    {
                        if (int.TryParse(xPart, out var xInt) && int.TryParse(yPart, out var yInt))
                        {
                            var result = xInt.CompareTo(yInt);
                            if (result != 0) return result;
                        }
                        else
                        {
                            var result = string.Compare(xPart, yPart, StringComparison.OrdinalIgnoreCase);
                            if (result != 0) return result;
                        }
                    }
                    else
                    {
                        var result = string.Compare(xPart, yPart, StringComparison.OrdinalIgnoreCase);
                        if (result != 0) return result;
                    }
                }

                return xParts.Length.CompareTo(yParts.Length);
            }
        }
    }
}
