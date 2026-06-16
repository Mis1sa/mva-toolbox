using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.FbxReplacement
{
    internal static class FbxReplacementExportService
    {
        internal static bool TryExportPrefab(
            FbxReplacementStageFourState stageFourState,
            string exportFolderInput,
            string prefabNameInput,
            out string exportFolder,
            out GameObject prefabAsset,
            out string targetPath,
            out string errorMessage)
        {
            exportFolder = string.Empty;
            prefabAsset = null;
            targetPath = string.Empty;
            errorMessage = string.Empty;

            GameObject targetRoot = stageFourState?.StageThreeState?.StageTwoState?.SessionState?.Workspace?.TargetWorkspaceRoot;
            if (targetRoot == null)
            {
                errorMessage = "无法找到可导出的目标物体";
                return false;
            }

            if (!FbxReplacementPathUtility.TryNormalizeExportFolder(exportFolderInput, out exportFolder, out errorMessage))
            {
                return false;
            }

            string timestampFolderRelative = exportFolder.TrimEnd('/') + "/" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string timestampFolderAbsolute = Application.dataPath.Substring(0, Application.dataPath.Length - 6) + timestampFolderRelative;
            if (!Directory.Exists(timestampFolderAbsolute))
            {
                Directory.CreateDirectory(timestampFolderAbsolute);
                AssetDatabase.Refresh();
            }

            string prefabName = string.IsNullOrEmpty(prefabNameInput) ? "MigratedObject" : prefabNameInput;
            targetPath = AssetDatabase.GenerateUniqueAssetPath(timestampFolderRelative + "/" + prefabName + ".prefab");
            prefabAsset = PrefabUtility.SaveAsPrefabAsset(targetRoot, targetPath);
            if (prefabAsset != null)
            {
                return true;
            }

            errorMessage = "保存 Prefab 失败: " + targetPath;
            return false;
        }
    }
}