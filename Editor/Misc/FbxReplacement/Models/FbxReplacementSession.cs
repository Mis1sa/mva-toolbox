using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MVA.Toolbox.FbxReplacement
{
    internal sealed class FbxReplacementWorkspaceContext
    {
        internal FbxReplacementWorkspaceContext(
            Scene scene,
            string sceneFilePath,
            GameObject referenceInstanceRoot,
            GameObject separatorObject,
            GameObject targetOriginalRoot,
            GameObject targetWorkspaceRoot,
            List<GameObject> createdRootPathObjects)
        {
            Scene = scene;
            SceneFilePath = sceneFilePath;
            ReferenceInstanceRoot = referenceInstanceRoot;
            SeparatorObject = separatorObject;
            TargetOriginalRoot = targetOriginalRoot;
            TargetWorkspaceRoot = targetWorkspaceRoot;
            CreatedRootPathObjects = createdRootPathObjects ?? new List<GameObject>();
        }

        internal Scene Scene { get; }
        internal string SceneFilePath { get; }
        internal GameObject ReferenceInstanceRoot { get; set; }
        internal GameObject SeparatorObject { get; }
        internal GameObject TargetOriginalRoot { get; set; }
        internal GameObject TargetWorkspaceRoot { get; set; }
        internal List<GameObject> CreatedRootPathObjects { get; set; }

    }

    internal sealed class FbxReplacementSessionState
    {
        internal FbxReplacementSessionState(FbxReplacementAnalysisResult analysisResult, GameObject referenceSource, GameObject targetSource)
        {
            AnalysisResult = analysisResult ?? throw new ArgumentNullException(nameof(analysisResult));
            LockedReferenceSource = referenceSource ?? throw new ArgumentNullException(nameof(referenceSource));
            LockedTargetSource = targetSource ?? throw new ArgumentNullException(nameof(targetSource));
            SelectedReferenceRootSource = referenceSource;
        }

        internal FbxReplacementAnalysisResult AnalysisResult { get; }
        internal GameObject LockedReferenceSource { get; }
        internal GameObject LockedTargetSource { get; }
        internal GameObject SelectedReferenceRootSource { get; set; }
        internal FbxReplacementWorkspaceContext Workspace { get; set; }
    }
}
