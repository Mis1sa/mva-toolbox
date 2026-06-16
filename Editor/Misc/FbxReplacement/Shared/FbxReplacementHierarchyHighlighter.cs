using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.FbxReplacement
{
    [InitializeOnLoad]
    internal static class FbxReplacementHierarchyHighlighter
    {
        private static EditorWindow _ownerWindow;
        private static Dictionary<int, Color> _highlightedColors = new Dictionary<int, Color>();
        private static readonly System.Type SceneHierarchyWindowType = typeof(EditorWindow).Assembly.GetType("UnityEditor.SceneHierarchyWindow");
        private static readonly MethodInfo GetAllSceneHierarchyWindowsMethod = SceneHierarchyWindowType != null
            ? SceneHierarchyWindowType.GetMethod("GetAllSceneHierarchyWindows", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            : null;
        private static readonly PropertyInfo SceneHierarchyProperty = SceneHierarchyWindowType != null
            ? SceneHierarchyWindowType.GetProperty("sceneHierarchy", BindingFlags.NonPublic | BindingFlags.Instance)
            : null;
        private static readonly FieldInfo SceneHierarchyField = SceneHierarchyWindowType != null
            ? SceneHierarchyWindowType.GetField("m_SceneHierarchy", BindingFlags.NonPublic | BindingFlags.Instance)
            : null;

        static FbxReplacementHierarchyHighlighter()
        {
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyWindowItemOnGUI;
        }

        internal static void RevealHierarchyObjects(params GameObject[] gameObjects)
        {
            ClearSelection();

            System.Collections.IEnumerable windowsEnumerable = GetHierarchyWindows();
            if (windowsEnumerable == null)
            {
                return;
            }

            CollapseAllHierarchyRootsRecursive(windowsEnumerable);

            if (gameObjects == null || gameObjects.Length == 0)
            {
                EditorApplication.RepaintHierarchyWindow();
                return;
            }

            var uniqueObjects = new HashSet<GameObject>();
            GameObject lastObject = null;
            for (int i = 0; i < gameObjects.Length; i++)
            {
                GameObject gameObject = gameObjects[i];
                if (gameObject == null || !uniqueObjects.Add(gameObject))
                {
                    continue;
                }

                ExpandHierarchyChain(gameObject.transform, windowsEnumerable);
                lastObject = gameObject;
            }

            if (lastObject != null)
            {
                EditorGUIUtility.PingObject(lastObject);
            }

            EditorApplication.RepaintHierarchyWindow();
        }

        internal static void ClearSelection()
        {
            Selection.objects = System.Array.Empty<Object>();
        }

        internal static bool IsActiveFor(EditorWindow owner)
        {
            CleanupInvalidOwner();
            return owner != null && _ownerWindow == owner;
        }

        internal static void Activate(EditorWindow owner, IDictionary<int, Color> highlightedColors)
        {
            if (owner == null)
            {
                return;
            }

            CleanupInvalidOwner();
            EditorWindow previousOwner = _ownerWindow;
            _ownerWindow = owner;
            bool changed = ReplaceHighlightedColors(highlightedColors);
            RepaintWindow(previousOwner);
            if (previousOwner != owner || changed)
            {
                RepaintWindow(owner);
                EditorApplication.RepaintHierarchyWindow();
            }
        }

        internal static void Update(EditorWindow owner, IDictionary<int, Color> highlightedColors)
        {
            if (!IsActiveFor(owner))
            {
                return;
            }

            if (!ReplaceHighlightedColors(highlightedColors))
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
            _highlightedColors.Clear();
            RepaintWindow(previousOwner);
            EditorApplication.RepaintHierarchyWindow();
        }

        private static void OnHierarchyWindowItemOnGUI(int instanceId, Rect selectionRect)
        {
            CleanupInvalidOwner();
            if (_ownerWindow == null)
            {
                return;
            }

            if (!_highlightedColors.TryGetValue(instanceId, out Color color))
            {
                return;
            }

            if (System.Array.IndexOf(Selection.instanceIDs, instanceId) >= 0)
            {
                return;
            }

            EditorGUI.DrawRect(selectionRect, color);
        }

        private static void CleanupInvalidOwner()
        {
            if (_ownerWindow != null)
            {
                return;
            }

            if (_highlightedColors.Count == 0)
            {
                return;
            }

            _highlightedColors.Clear();
            EditorApplication.RepaintHierarchyWindow();
        }

        private static bool ReplaceHighlightedColors(IDictionary<int, Color> highlightedColors)
        {
            var newMap = highlightedColors != null
                ? new Dictionary<int, Color>(highlightedColors)
                : new Dictionary<int, Color>();
            if (DictionaryEquals(_highlightedColors, newMap))
            {
                return false;
            }

            _highlightedColors = newMap;
            return true;
        }

        private static bool DictionaryEquals(IReadOnlyDictionary<int, Color> left, IReadOnlyDictionary<int, Color> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            foreach (var pair in left)
            {
                if (!right.TryGetValue(pair.Key, out Color value))
                {
                    return false;
                }

                if (!ApproximatelyEqual(pair.Value, value))
                {
                    return false;
                }
            }

            return true;
        }

        private static void RepaintWindow(EditorWindow window)
        {
            if (window != null)
            {
                window.Repaint();
            }
        }

        private static void ExpandHierarchyChain(Transform transform)
        {
            ExpandHierarchyChain(transform, GetHierarchyWindows());
        }

        private static void ExpandHierarchyChain(Transform transform, System.Collections.IEnumerable windowsEnumerable)
        {
            if (transform == null || SceneHierarchyWindowType == null || windowsEnumerable == null)
            {
                return;
            }

            for (Transform current = transform.parent; current != null; current = current.parent)
            {
                foreach (object window in windowsEnumerable)
                {
                    if (window == null)
                    {
                        continue;
                    }

                    object sceneHierarchy = SceneHierarchyProperty != null
                        ? SceneHierarchyProperty.GetValue(window, null)
                        : null;
                    if (sceneHierarchy == null && SceneHierarchyField != null)
                    {
                        sceneHierarchy = SceneHierarchyField.GetValue(window);
                    }

                    ExpandHierarchyItem(window, current.gameObject.GetInstanceID());
                    ExpandHierarchyItem(sceneHierarchy, current.gameObject.GetInstanceID());
                }
            }
        }

        private static void CollapseAllHierarchyRootsRecursive(System.Collections.IEnumerable windowsEnumerable)
        {
            if (windowsEnumerable == null)
            {
                return;
            }

            int sceneCount = UnityEngine.SceneManagement.SceneManager.sceneCount;
            for (int sceneIndex = 0; sceneIndex < sceneCount; sceneIndex++)
            {
                UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(sceneIndex);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }

                GameObject[] rootObjects = scene.GetRootGameObjects();
                for (int rootIndex = 0; rootIndex < rootObjects.Length; rootIndex++)
                {
                    GameObject rootObject = rootObjects[rootIndex];
                    if (rootObject == null)
                    {
                        continue;
                    }

                    foreach (object window in windowsEnumerable)
                    {
                        if (window == null)
                        {
                            continue;
                        }

                        object sceneHierarchy = SceneHierarchyProperty != null
                            ? SceneHierarchyProperty.GetValue(window, null)
                            : null;
                        if (sceneHierarchy == null && SceneHierarchyField != null)
                        {
                            sceneHierarchy = SceneHierarchyField.GetValue(window);
                        }

                        CollapseHierarchyItemRecursive(window, rootObject.transform);
                        CollapseHierarchyItemRecursive(sceneHierarchy, rootObject.transform);
                    }
                }
            }
        }

        private static void CollapseHierarchyItemRecursive(object target, Transform root)
        {
            if (target == null || root == null)
            {
                return;
            }

            if (SetHierarchyExpanded(target, root.gameObject.GetInstanceID(), false, true, true))
            {
                return;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                CollapseHierarchyItemRecursive(target, root.GetChild(i));
            }

            SetHierarchyExpanded(target, root.gameObject.GetInstanceID(), false, false, false);
        }

        private static System.Collections.IEnumerable GetHierarchyWindows()
        {
            if (GetAllSceneHierarchyWindowsMethod != null)
            {
                object windowsObject = GetAllSceneHierarchyWindowsMethod.Invoke(null, null);
                if (windowsObject is System.Collections.IEnumerable windowsEnumerable)
                {
                    return windowsEnumerable;
                }
            }

            return Resources.FindObjectsOfTypeAll(SceneHierarchyWindowType);
        }

        private static void ExpandHierarchyItem(object target, int instanceId)
        {
            SetHierarchyExpanded(target, instanceId, true, false, false);
        }

        private static void SetHierarchyExpanded(object target, int instanceId, bool expanded)
        {
            SetHierarchyExpanded(target, instanceId, expanded, true, false);
        }

        private static bool SetHierarchyExpanded(object target, int instanceId, bool expanded, bool allowRecursive, bool preferRecursive)
        {
            if (target == null)
            {
                return false;
            }

            MethodInfo[] methods = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo method = preferRecursive && allowRecursive
                ? FindSetExpandedRecursiveMethod(methods)
                : FindSetExpandedMethod(methods);

            if (method == null)
            {
                method = allowRecursive && !preferRecursive
                    ? FindSetExpandedRecursiveMethod(methods)
                    : FindSetExpandedMethod(methods);
            }

            if (method == null)
            {
                return false;
            }

            method.Invoke(target, new object[] { instanceId, expanded });
            return true;
        }

        private static MethodInfo FindSetExpandedMethod(MethodInfo[] methods)
        {
            return methods.FirstOrDefault(candidate =>
            {
                if (string.Equals(candidate.Name, "SetExpanded", System.StringComparison.Ordinal)
                    || string.Equals(candidate.Name, "ExpandTreeViewItem", System.StringComparison.Ordinal))
                {
                    ParameterInfo[] parameters = candidate.GetParameters();
                    return parameters.Length == 2
                        && parameters[0].ParameterType == typeof(int)
                        && parameters[1].ParameterType == typeof(bool);
                }

                return false;
            });
        }

        private static MethodInfo FindSetExpandedRecursiveMethod(MethodInfo[] methods)
        {
            return methods.FirstOrDefault(candidate =>
            {
                if (!string.Equals(candidate.Name, "SetExpandedRecursive", System.StringComparison.Ordinal))
                {
                    return false;
                }

                ParameterInfo[] parameters = candidate.GetParameters();
                return parameters.Length == 2
                    && parameters[0].ParameterType == typeof(int)
                    && parameters[1].ParameterType == typeof(bool);
            });
        }

        private static bool ApproximatelyEqual(Color a, Color b)
        {
            return Mathf.Approximately(a.r, b.r)
                   && Mathf.Approximately(a.g, b.g)
                   && Mathf.Approximately(a.b, b.b)
                   && Mathf.Approximately(a.a, b.a);
        }
    }
}
