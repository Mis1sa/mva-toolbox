using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.FbxReplacement
{
    internal sealed partial class FbxReplacementWindow
    {
        private void ExportAndCompleteStageFour()
        {
            if (!FbxReplacementExportService.TryExportPrefab(
                    _stageFourState,
                    _exportFolderRelative,
                    _exportPrefabName,
                    out string exportFolder,
                    out GameObject prefabAsset,
                    out string targetPath,
                    out string errorMessage))
            {
                EditorUtility.DisplayDialog("错误", errorMessage, "确定");
                return;
            }

            _exportFolderRelative = exportFolder;
            EditorGUIUtility.PingObject(prefabAsset);
            Debug.Log($"FBX替换已完成，导出 Prefab: {targetPath}", prefabAsset);

            ResetWorkflowState();
        }
    }
}