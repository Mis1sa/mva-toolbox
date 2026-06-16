using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MVA.Toolbox.FbxReplacement
{
    internal static class FbxReplacementWorkspaceCleanup
    {
        internal static void DeleteUnusedWorkspaceSceneArtifacts(string workspaceSceneDirectoryRelativePath, string workspaceSceneFilePrefix)
        {
            string workspaceDirectoryFullPath = GetWorkspaceSceneFullPath(workspaceSceneDirectoryRelativePath);
            if (!Directory.Exists(workspaceDirectoryFullPath))
            {
                return;
            }

            string[] sceneFiles = Directory.GetFiles(workspaceDirectoryFullPath, workspaceSceneFilePrefix + "*.unity", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < sceneFiles.Length; i++)
            {
                string relativePath = GetProjectRelativePath(sceneFiles[i]);
                if (string.IsNullOrEmpty(relativePath) || IsScenePathLoaded(relativePath))
                {
                    continue;
                }

                AssetDatabase.DeleteAsset(relativePath);
            }

            if (Directory.Exists(workspaceDirectoryFullPath)
                && Directory.GetFiles(workspaceDirectoryFullPath, "*", SearchOption.TopDirectoryOnly).Length == 0
                && Directory.GetDirectories(workspaceDirectoryFullPath, "*", SearchOption.TopDirectoryOnly).Length == 0)
            {
                AssetDatabase.DeleteAsset(workspaceSceneDirectoryRelativePath);
            }
        }

        private static bool IsScenePathLoaded(string scenePath)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && string.Equals(scene.path, scenePath, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetProjectRelativePath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
            {
                return string.Empty;
            }

            string projectRootPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string normalizedFullPath = Path.GetFullPath(fullPath);
            if (!normalizedFullPath.StartsWith(projectRootPath, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return normalizedFullPath.Substring(projectRootPath.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace("\\", "/");
        }

        private static string GetWorkspaceSceneFullPath(string projectRelativePath)
        {
            if (string.IsNullOrEmpty(projectRelativePath))
            {
                return string.Empty;
            }

            string projectRootPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRootPath, projectRelativePath.Replace("/", Path.DirectorySeparatorChar.ToString()));
        }
    }
}