using MVA.Toolbox.SwitchGenerator.Utils;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace MVA.Toolbox.SwitchGenerator.Entry
{
    internal static class SwitchGeneratorMenuPathOptions
    {
        public static string[] Build(VRCExpressionsMenu rootMenu)
        {
            if (rootMenu == null)
            {
                return new[] { "/" };
            }

            var displayPaths = AvatarAssetResolver.BuildMenuDisplayPaths(rootMenu);
            if (displayPaths == null || displayPaths.Length == 0)
            {
                return new[] { "/" };
            }

            return displayPaths;
        }

        public static int IndexOf(string[] options, string currentPath)
        {
            if (options == null || options.Length == 0)
            {
                return -1;
            }

            string normalized = AvatarAssetResolver.NormalizeMenuPath(currentPath);
            for (int i = 0; i < options.Length; i++)
            {
                if (AvatarAssetResolver.NormalizeMenuPath(options[i]) == normalized)
                {
                    return i;
                }
            }

            return -1;
        }

        public static string Resolve(string[] options, int index, string fallback)
        {
            if (options != null && index >= 0 && index < options.Length)
            {
                return AvatarAssetResolver.NormalizeMenuPath(options[index]);
            }

            return AvatarAssetResolver.NormalizeMenuPath(fallback);
        }
    }
}
