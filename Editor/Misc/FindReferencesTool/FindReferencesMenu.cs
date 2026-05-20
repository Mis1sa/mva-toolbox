using UnityEditor;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.FindReferencesTool
{
    internal static class FindReferencesMenu
    {
        [MenuItem("Assets/MVA Toolbox/引用查询", false, 102)]
        private static void FindReferencesFromAssetMenu()
        {
            Object targetAsset = Selection.activeObject;
            if (targetAsset == null)
            {
                return;
            }

            FindReferencesWindow.Open(targetAsset);
        }

        [MenuItem("Assets/MVA Toolbox/引用查询", true)]
        private static bool ValidateFindReferencesFromAssetMenu()
        {
            if (Selection.objects.Length != 1)
            {
                return false;
            }

            Object targetAsset = Selection.activeObject;
            if (targetAsset == null)
            {
                return false;
            }

            string path = AssetDatabase.GetAssetPath(targetAsset);
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return !AssetDatabase.IsValidFolder(path);
        }
    }
}
