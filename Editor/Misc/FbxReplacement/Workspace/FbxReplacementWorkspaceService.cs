using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.FbxReplacement
{
    internal sealed class FbxReplacementStageOneState
    {
        internal FbxReplacementStageOneState(FbxReplacementAnalysisResult analysisResult, GameObject referenceSource, GameObject targetSource)
        {
            Session = new FbxReplacementSessionState(
                analysisResult ?? throw new ArgumentNullException(nameof(analysisResult)),
                referenceSource ?? throw new ArgumentNullException(nameof(referenceSource)),
                targetSource ?? throw new ArgumentNullException(nameof(targetSource)));
            SelectedReferenceRootSource = ResolveDefaultReferenceRoot(analysisResult, referenceSource);
        }

        internal FbxReplacementSessionState Session { get; }
        internal FbxReplacementAnalysisResult AnalysisResult => Session.AnalysisResult;
        internal GameObject LockedReferenceSource => Session.LockedReferenceSource;
        internal GameObject LockedTargetSource => Session.LockedTargetSource;
        internal GameObject SelectedReferenceRootSource
        {
            get => Session.SelectedReferenceRootSource;
            set => Session.SelectedReferenceRootSource = value;
        }
        internal FbxReplacementWorkspaceContext Workspace
        {
            get => Session.Workspace;
            set => Session.Workspace = value;
        }

        private static GameObject ResolveDefaultReferenceRoot(FbxReplacementAnalysisResult analysisResult, GameObject referenceSource)
        {
            return referenceSource;
        }
    }

    internal static class FbxReplacementWorkspaceService
    {
        private const string WorkspaceSceneDirectoryRelativePath = "Assets/__FbxReplacement_TempScenes";
        private const string WorkspaceSceneFilePrefix = "__FbxReplacement__";

        internal static FbxReplacementStageOneState CreateState(FbxReplacementAnalysisResult analysisResult, GameObject referenceSource, GameObject targetSource)
        {
            EnsureNoUnsavedOpenScenes();
            var state = new FbxReplacementStageOneState(analysisResult, referenceSource, targetSource);
            string defaultReferenceRootRelativePath = GetRelativePath(state.LockedReferenceSource.transform, state.SelectedReferenceRootSource.transform);
            state.Workspace = CreateWorkspace(state, defaultReferenceRootRelativePath, false);
            state.SelectedReferenceRootSource = ResolveWorkspaceReferenceObject(state.Workspace.ReferenceInstanceRoot, defaultReferenceRootRelativePath);
            return state;
        }

        internal static bool HasUnsavedOpenScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || EditorSceneManager.IsPreviewScene(scene))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(scene.path) || scene.isDirty)
                {
                    return true;
                }
            }

            return false;
        }

        internal static void DisposeState(FbxReplacementStageOneState state)
        {
            if (state == null)
            {
                return;
            }

            CloseWorkspace(state.Workspace);
            state.Workspace = null;
        }

        private static FbxReplacementWorkspaceContext CreateWorkspace(FbxReplacementStageOneState state, string selectedReferenceRootRelativePath, bool supplementTargetRootPath)
        {
            EnsureNoUnsavedOpenScenes();

            Scene workspaceScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            string sceneFilePath = CreateUniqueWorkspaceScenePath();
            if (!EditorSceneManager.SaveScene(workspaceScene, sceneFilePath, false))
            {
                throw new InvalidOperationException("无法创建阶段1临时场景。");
            }

            EditorSceneManager.SetActiveScene(workspaceScene);

            GameObject referenceInstanceRoot = CreateWorkingCopy(state.LockedReferenceSource, workspaceScene);
            GameObject targetOriginalRoot = CreateWorkingCopy(state.LockedTargetSource, workspaceScene);
            if (referenceInstanceRoot == null || targetOriginalRoot == null)
            {
                throw new InvalidOperationException("无法创建阶段1工作区副本。");
            }

            referenceInstanceRoot.transform.position = new Vector3(2f, 0f, 0f);
            referenceInstanceRoot.transform.rotation = Quaternion.identity;
            targetOriginalRoot.transform.position = Vector3.zero;
            targetOriginalRoot.transform.rotation = Quaternion.identity;

            GameObject separatorObject = new GameObject("====================");
            SceneManager.MoveGameObjectToScene(separatorObject, workspaceScene);
            separatorObject.transform.position = new Vector3(1f, 0f, 0f);
            separatorObject.transform.rotation = Quaternion.identity;

            GameObject selectedReferenceRoot = ResolveWorkspaceReferenceObject(referenceInstanceRoot, selectedReferenceRootRelativePath);
            List<GameObject> createdRootPathObjects = supplementTargetRootPath
                ? CreateMissingRootPathObjects(selectedReferenceRoot, referenceInstanceRoot, targetOriginalRoot, workspaceScene)
                : new List<GameObject>();
            GameObject targetWorkspaceRoot = createdRootPathObjects.Count > 0
                ? createdRootPathObjects[createdRootPathObjects.Count - 1]
                : targetOriginalRoot;
            referenceInstanceRoot.transform.SetSiblingIndex(0);
            separatorObject.transform.SetSiblingIndex(1);
            if (targetWorkspaceRoot != null)
            {
                targetWorkspaceRoot.transform.SetSiblingIndex(2);
            }

            return new FbxReplacementWorkspaceContext(
                workspaceScene,
                sceneFilePath,
                referenceInstanceRoot,
                separatorObject,
                targetOriginalRoot,
                targetWorkspaceRoot,
                createdRootPathObjects);
        }

        private static GameObject CreateWorkingCopy(GameObject source, Scene scene)
        {
            if (source == null)
            {
                return null;
            }

            GameObject instance = null;
            if (EditorUtility.IsPersistent(source) || PrefabUtility.IsPartOfPrefabAsset(source))
            {
                instance = PrefabUtility.InstantiatePrefab(source, scene) as GameObject;
            }
            else
            {
                instance = Object.Instantiate(source);
                SceneManager.MoveGameObjectToScene(instance, scene);
            }

            if (instance == null)
            {
                return null;
            }

            if (PrefabUtility.IsAnyPrefabInstanceRoot(instance))
            {
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }

            return instance;
        }

        private static List<GameObject> CreateMissingRootPathObjects(GameObject selectedReferenceRoot, GameObject referenceSourceRoot, GameObject targetOriginalRoot, Scene scene)
        {
            var createdObjects = new List<GameObject>();
            if (selectedReferenceRoot == null || referenceSourceRoot == null || targetOriginalRoot == null)
            {
                return createdObjects;
            }

            Transform currentReferenceAncestor = selectedReferenceRoot.transform.parent;
            Transform currentTargetChild = targetOriginalRoot.transform;
            while (currentReferenceAncestor != null)
            {
                var createdObject = new GameObject(currentReferenceAncestor.name);
                SceneManager.MoveGameObjectToScene(createdObject, scene);
                CopyAllowedVisualComponents(currentReferenceAncestor.gameObject, createdObject);
                createdObject.transform.SetParent(null, false);
                currentTargetChild.SetParent(createdObject.transform, false);
                createdObjects.Add(createdObject);
                currentTargetChild = createdObject.transform;
                if (currentReferenceAncestor == referenceSourceRoot.transform)
                {
                    break;
                }

                currentReferenceAncestor = currentReferenceAncestor.parent;
            }

            return createdObjects;
        }

        private static void CopyAllowedVisualComponents(GameObject source, GameObject destination)
        {
            if (source == null || destination == null)
            {
                return;
            }

            Component[] sourceComponents = source.GetComponents<Component>();
            for (int i = 0; i < sourceComponents.Length; i++)
            {
                Component sourceComponent = sourceComponents[i];
                if (!IsAllowedVisualComponent(sourceComponent))
                {
                    continue;
                }

                Component destinationComponent = destination.AddComponent(sourceComponent.GetType());
                EditorUtility.CopySerialized(sourceComponent, destinationComponent);
            }
        }

        private static bool IsAllowedVisualComponent(Component component)
        {
            return component != null
                && !(component is Transform)
                && (component is MeshFilter
                    || component is MeshRenderer
                    || component is SkinnedMeshRenderer);
        }

        private static GameObject ResolveWorkspaceReferenceObject(GameObject referenceInstanceRoot, string relativePath)
        {
            if (referenceInstanceRoot == null)
            {
                return null;
            }

            if (string.IsNullOrEmpty(relativePath))
            {
                return referenceInstanceRoot;
            }

            Transform resolved = referenceInstanceRoot.transform.Find(relativePath);
            return resolved != null ? resolved.gameObject : referenceInstanceRoot;
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null)
            {
                return string.Empty;
            }

            if (root == target)
            {
                return string.Empty;
            }

            var segments = new Stack<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                segments.Push(current.name);
                current = current.parent;
            }

            if (current != root)
            {
                return string.Empty;
            }

            return string.Join("/", segments.ToArray());
        }

        private static void CloseWorkspace(FbxReplacementWorkspaceContext workspace)
        {
            if (workspace == null || !workspace.Scene.IsValid())
            {
                return;
            }

            EditorSceneManager.CloseScene(workspace.Scene, true);
            if (!string.IsNullOrEmpty(workspace.SceneFilePath) && AssetDatabase.LoadAssetAtPath<SceneAsset>(workspace.SceneFilePath) != null)
            {
                AssetDatabase.DeleteAsset(workspace.SceneFilePath);
            }

            FbxReplacementWorkspaceCleanup.DeleteUnusedWorkspaceSceneArtifacts(WorkspaceSceneDirectoryRelativePath, WorkspaceSceneFilePrefix);
        }

        private static string CreateUniqueWorkspaceScenePath()
        {
            FbxReplacementWorkspaceCleanup.DeleteUnusedWorkspaceSceneArtifacts(WorkspaceSceneDirectoryRelativePath, WorkspaceSceneFilePrefix);

            string workspaceDirectoryFullPath = GetWorkspaceSceneFullPath(WorkspaceSceneDirectoryRelativePath);
            Directory.CreateDirectory(workspaceDirectoryFullPath);
            AssetDatabase.Refresh();

            string relativePath;
            do
            {
                string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
                string fileName = $"{WorkspaceSceneFilePrefix}{DateTime.Now:yyyyMMdd_HHmmss_fff}_{suffix}.unity";
                relativePath = Path.Combine(WorkspaceSceneDirectoryRelativePath, fileName).Replace("\\", "/");
            }
            while (File.Exists(GetWorkspaceSceneFullPath(relativePath)));

            return relativePath;
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

        private static void EnsureNoUnsavedOpenScenes()
        {
            if (!HasUnsavedOpenScenes())
            {
                return;
            }

            throw new InvalidOperationException("当前存在未保存的场景，请先保存后再进行分析。");
        }
    }
}
