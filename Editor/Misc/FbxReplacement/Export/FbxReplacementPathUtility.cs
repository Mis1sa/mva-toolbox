using System;

namespace MVA.Toolbox.FbxReplacement
{
    internal static class FbxReplacementPathUtility
    {
        internal static bool TryNormalizeExportFolder(string inputPath, out string exportFolder, out string errorMessage)
        {
            exportFolder = string.IsNullOrWhiteSpace(inputPath)
                ? "Assets"
                : inputPath.Trim().Replace("\\", "/").TrimEnd('/');
            errorMessage = string.Empty;

            if (exportFolder.Length == 0)
            {
                exportFolder = "Assets";
            }

            if (exportFolder.StartsWith("/", StringComparison.Ordinal)
                || exportFolder.Contains(":"))
            {
                errorMessage = "请填写 Assets 目录下的项目相对路径。";
                return false;
            }

            if (!exportFolder.Equals("Assets", StringComparison.Ordinal)
                && !exportFolder.StartsWith("Assets/", StringComparison.Ordinal))
            {
                exportFolder = "Assets/" + exportFolder.TrimStart('/');
            }

            return true;
        }
    }
}