using System;
using System.Collections.Generic;
using MVA.Toolbox.Animation.Shared.Controllers;
using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.Animation.Shared.SelectableRange
{
    [InitializeOnLoad]
    internal static class AnimationSelectableRangeHighlighter
    {
        private const string HighlightColorPrefKey = "MVA_AnimationSelectableRangeHighlightColor";
        private static readonly Color DefaultHighlightColor = new Color(1f, 0.4f, 0f, 0.18f);

        private static EditorWindow _ownerWindow;
        private static HashSet<int> _highlightedInstanceIds = new HashSet<int>();
        private static bool _colorLoaded;
        private static Color _highlightColor;

        static AnimationSelectableRangeHighlighter()
        {
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyWindowItemOnGUI;
        }

        internal static bool IsActiveFor(EditorWindow owner)
        {
            CleanupInvalidOwner();
            return owner != null && _ownerWindow == owner;
        }

        internal static Color HighlightColor
        {
            get
            {
                EnsureColorLoaded();
                return _highlightColor;
            }
            set
            {
                EnsureColorLoaded();
                if (ApproximatelyEqual(_highlightColor, value))
                {
                    return;
                }

                _highlightColor = value;
                SaveColor(value);
                RepaintWindow(_ownerWindow);
                EditorApplication.RepaintHierarchyWindow();
            }
        }

        internal static void Activate(EditorWindow owner, HashSet<int> highlightedInstanceIds)
        {
            if (owner == null)
            {
                return;
            }

            CleanupInvalidOwner();
            EditorWindow previousOwner = _ownerWindow;
            _ownerWindow = owner;
            bool changed = ReplaceHighlightedIds(highlightedInstanceIds);
            RepaintWindow(previousOwner);
            if (previousOwner != owner || changed)
            {
                RepaintWindow(owner);
                EditorApplication.RepaintHierarchyWindow();
            }
        }

        internal static void Update(EditorWindow owner, HashSet<int> highlightedInstanceIds)
        {
            if (!IsActiveFor(owner))
            {
                return;
            }

            if (!ReplaceHighlightedIds(highlightedInstanceIds))
            {
                return;
            }

            RepaintWindow(owner);
            EditorApplication.RepaintHierarchyWindow();
        }

        internal static void Deactivate(EditorWindow owner)
        {
            if (!IsActiveFor(owner))
            {
                return;
            }

            EditorWindow previousOwner = _ownerWindow;
            _ownerWindow = null;
            _highlightedInstanceIds.Clear();
            RepaintWindow(previousOwner);
            EditorApplication.RepaintHierarchyWindow();
        }

        private static void OnHierarchyWindowItemOnGUI(int instanceId, Rect selectionRect)
        {
            CleanupInvalidOwner();
            if (_ownerWindow == null || !_highlightedInstanceIds.Contains(instanceId) || Array.IndexOf(Selection.instanceIDs, instanceId) >= 0)
            {
                return;
            }

            EditorGUI.DrawRect(selectionRect, HighlightColor);
        }

        private static void CleanupInvalidOwner()
        {
            if (_ownerWindow != null)
            {
                return;
            }

            if (_highlightedInstanceIds.Count == 0)
            {
                return;
            }

            _highlightedInstanceIds.Clear();
            EditorApplication.RepaintHierarchyWindow();
        }

        private static bool ReplaceHighlightedIds(HashSet<int> highlightedInstanceIds)
        {
            HashSet<int> newSet = highlightedInstanceIds != null
                ? new HashSet<int>(highlightedInstanceIds)
                : new HashSet<int>();

            if (_highlightedInstanceIds.SetEquals(newSet))
            {
                return false;
            }

            _highlightedInstanceIds = newSet;
            return true;
        }

        private static void EnsureColorLoaded()
        {
            if (_colorLoaded)
            {
                return;
            }

            _colorLoaded = true;
            string saved = EditorPrefs.GetString(HighlightColorPrefKey, string.Empty);
            if (!string.IsNullOrEmpty(saved) && ColorUtility.TryParseHtmlString(saved, out Color parsed))
            {
                _highlightColor = parsed;
                return;
            }

            _highlightColor = DefaultHighlightColor;
        }

        private static void SaveColor(Color color)
        {
            string html = "#" + ColorUtility.ToHtmlStringRGBA(color);
            EditorPrefs.SetString(HighlightColorPrefKey, html);
        }

        private static void RepaintWindow(EditorWindow window)
        {
            if (window != null)
            {
                window.Repaint();
            }
        }

        private static bool ApproximatelyEqual(Color a, Color b)
        {
            return Mathf.Approximately(a.r, b.r)
                   && Mathf.Approximately(a.g, b.g)
                   && Mathf.Approximately(a.b, b.b)
                   && Mathf.Approximately(a.a, b.a);
        }
    }

    internal static class AnimationSelectableRangeControls
    {
        internal static void Draw(EditorWindow owner, Func<HashSet<int>> buildHighlightedIds)
        {
            if (owner == null || buildHighlightedIds == null)
            {
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            bool isActive = AnimationSelectableRangeHighlighter.IsActiveFor(owner);
            if (!isActive)
            {
                if (GUILayout.Button("显示可选范围"))
                {
                    AnimationSelectableRangeHighlighter.Activate(owner, buildHighlightedIds());
                }
            }
            else
            {
                AnimationSelectableRangeHighlighter.Update(owner, buildHighlightedIds());
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("取消显示", GUILayout.Width(90f)))
                {
                    AnimationSelectableRangeHighlighter.Deactivate(owner);
                }

                Color currentColor = AnimationSelectableRangeHighlighter.HighlightColor;
                Color newColor = EditorGUILayout.ColorField("高亮颜色", currentColor);
                if (!ApproximatelyEqual(currentColor, newColor))
                {
                    AnimationSelectableRangeHighlighter.HighlightColor = newColor;
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private static bool ApproximatelyEqual(Color a, Color b)
        {
            return Mathf.Approximately(a.r, b.r)
                   && Mathf.Approximately(a.g, b.g)
                   && Mathf.Approximately(a.b, b.b)
                   && Mathf.Approximately(a.a, b.a);
        }
    }

    internal static class AnimationSelectableRangeUtility
    {
        internal static HashSet<int> CollectSelectableGameObjectInstanceIds(GameObject targetRoot, IEnumerable<Transform> controllerRoots)
        {
            HashSet<int> result = new HashSet<int>();
            if (targetRoot == null || controllerRoots == null)
            {
                return result;
            }
            HashSet<Transform> uniqueRoots = new HashSet<Transform>();
            foreach (Transform controllerRoot in controllerRoots)
            {
                if (controllerRoot == null || !uniqueRoots.Add(controllerRoot))
                {
                    continue;
                }

                Transform[] transforms = controllerRoot.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    Transform transform = transforms[i];
                    if (transform == null || transform == controllerRoot)
                    {
                        continue;
                    }

                    if (!IsTransformInControllerScope(transform, controllerRoot, false))
                    {
                        continue;
                    }

                    result.Add(transform.gameObject.GetInstanceID());
                }
            }

            return result;
        }

        internal static HashSet<int> CollectSelectableGameObjectInstanceIds(GameObject targetRoot, IEnumerable<ControllerWithRoot> controllerScopes)
        {
            HashSet<int> result = new HashSet<int>();
            if (targetRoot == null || controllerScopes == null)
            {
                return result;
            }
            HashSet<string> uniqueScopes = new HashSet<string>(StringComparer.Ordinal);
            foreach (ControllerWithRoot controllerScope in controllerScopes)
            {
                Transform controllerRoot = controllerScope.RootTransform;
                if (controllerRoot == null)
                {
                    continue;
                }

                string scopeKey = controllerRoot.GetInstanceID() + ":" + (controllerScope.IgnoresNestedAnimators ? "1" : "0");
                if (!uniqueScopes.Add(scopeKey))
                {
                    continue;
                }

                Transform[] transforms = controllerRoot.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    Transform transform = transforms[i];
                    if (transform == null || transform == controllerRoot)
                    {
                        continue;
                    }

                    if (!IsTransformInControllerScope(transform, controllerRoot, controllerScope.IgnoresNestedAnimators))
                    {
                        continue;
                    }

                    result.Add(transform.gameObject.GetInstanceID());
                }
            }

            return result;
        }

        internal static bool IsTransformInControllerScope(Transform target, Transform controllerRoot)
        {
            return IsTransformInControllerScope(target, controllerRoot, false);
        }

        internal static bool IsTransformInControllerScope(Transform target, Transform controllerRoot, bool ignoresNestedAnimators)
        {
            if (target == null || controllerRoot == null)
            {
                return false;
            }

            if (!ignoresNestedAnimators && target != controllerRoot && target.GetComponent<Animator>() != null)
            {
                return false;
            }

            Transform current = target;
            while (current != null)
            {
                if (current == controllerRoot)
                {
                    return true;
                }

                Transform parent = current.parent;
                if (!ignoresNestedAnimators && parent != null && parent != controllerRoot && parent.GetComponent<Animator>() != null)
                {
                    return false;
                }

                current = parent;
            }

            return false;
        }
    }
}
