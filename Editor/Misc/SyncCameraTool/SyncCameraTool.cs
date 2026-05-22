using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.SyncCamera
{
    [InitializeOnLoad]
    internal static class SyncCameraPoseCache
    {
        private static Vector3 _cachedPosition;
        private static Quaternion _cachedRotation = Quaternion.identity;
        private static bool _hasPose;
        private static bool _isRecording;

        static SyncCameraPoseCache()
        {
            EditorApplication.playModeStateChanged += HandlePlayModeChange;
            RefreshRecordingState();
        }

        internal static void OnSyncToggleChanged(bool enabled)
        {
            if (enabled)
            {
                RefreshRecordingState();
                return;
            }

            StopRecording(true);
        }

        internal static void ApplyCachedPoseOnDemand()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ApplyCachedPoseImmediate();
        }

        private static void HandlePlayModeChange(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                StopRecording(false);
                ApplyCachedPoseImmediate();
                return;
            }

            if (state == PlayModeStateChange.EnteredEditMode)
            {
                RefreshRecordingState();
            }
        }

        private static void RefreshRecordingState()
        {
            if (!Application.isPlaying && SyncCameraTool.IsEnabled)
            {
                StartRecording();
                return;
            }

            StopRecording(false);
        }

        private static void StartRecording()
        {
            if (_isRecording)
            {
                return;
            }

            SceneView.duringSceneGui += RecordScenePose;
            _isRecording = true;
        }

        private static void StopRecording(bool clearCache)
        {
            if (_isRecording)
            {
                SceneView.duringSceneGui -= RecordScenePose;
                _isRecording = false;
            }

            if (!clearCache)
            {
                return;
            }

            _hasPose = false;
            _cachedPosition = default;
            _cachedRotation = Quaternion.identity;
        }

        private static void RecordScenePose(SceneView sceneView)
        {
            if (sceneView == null || sceneView.camera == null)
            {
                return;
            }

            if (Application.isPlaying || !SyncCameraTool.IsEnabled)
            {
                return;
            }

            Transform sceneCameraTransform = sceneView.camera.transform;
            _cachedPosition = sceneCameraTransform.position;
            _cachedRotation = sceneCameraTransform.rotation;
            _hasPose = true;
        }

        private static void ApplyCachedPoseImmediate()
        {
            if (!_hasPose)
            {
                return;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                return;
            }

            mainCamera.transform.SetPositionAndRotation(_cachedPosition, _cachedRotation);
        }
    }

    internal static class SyncCameraTool
    {
        private const string EditorPrefsKey = "MVA_SyncCamera_Enabled";
        private const string DefaultStateMigrationKey = "MVA_SyncCamera_DefaultOffMigrated";
        private static bool _preferenceInitialized;

        internal static bool IsEnabled
        {
            get
            {
                EnsurePreferenceInitialized();
                return EditorPrefs.GetBool(EditorPrefsKey, false);
            }
        }

        private static void EnsurePreferenceInitialized()
        {
            if (_preferenceInitialized)
            {
                return;
            }

            _preferenceInitialized = true;
            if (EditorPrefs.GetBool(DefaultStateMigrationKey, false))
            {
                return;
            }

            EditorPrefs.SetBool(EditorPrefsKey, false);
            EditorPrefs.SetBool(DefaultStateMigrationKey, true);
        }

        internal static void Toggle()
        {
            bool isEnabled = !IsEnabled;
            EditorPrefs.SetBool(EditorPrefsKey, isEnabled);
            Debug.Log(isEnabled
                ? "<color=#4DA3FF>[MVA]</color>同步主摄像机到游戏窗口<color=#4CAF50>已启用</color>"
                : "<color=#4DA3FF>[MVA]</color>同步主摄像机到游戏窗口<color=#F44336>已关闭</color>");
            SyncCameraPoseCache.OnSyncToggleChanged(isEnabled);

            if (!EditorApplication.isPlaying)
            {
                return;
            }

            if (isEnabled)
            {
                Camera mainCamera = EnsureMainCameraExists();
                EnsureSyncComponent(mainCamera);
                SyncCameraPoseCache.ApplyCachedPoseOnDemand();
                return;
            }

            RemoveSyncComponents();
        }

        internal static bool ValidateMenu()
        {
            return true;
        }

        internal static Camera EnsureMainCameraExists()
        {
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                return mainCamera;
            }

            Debug.LogWarning("[MVA]场景中没有主摄像机，已自动创建 Main Camera。");
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.transform.position = new Vector3(0f, 1f, -10f);
            cameraObject.transform.rotation = Quaternion.identity;
            return camera;
        }

        internal static void EnsureSyncComponent(Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            if (camera.GetComponent<SyncCameraComponent>() != null)
            {
                return;
            }

            camera.gameObject.AddComponent<SyncCameraComponent>();
        }

        internal static void RemoveSyncComponents()
        {
            SyncCameraComponent[] components = Object.FindObjectsOfType<SyncCameraComponent>(true);
            for (int index = 0; index < components.Length; index++)
            {
                SyncCameraComponent component = components[index];
                if (component == null)
                {
                    continue;
                }

                Object.DestroyImmediate(component);
            }
        }
    }

    [InitializeOnLoad]
    internal static class SyncCameraInitializer
    {
        static SyncCameraInitializer()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SyncCameraTool.IsEnabled)
            {
                return;
            }

            Camera mainCamera = SyncCameraTool.EnsureMainCameraExists();
            SyncCameraTool.EnsureSyncComponent(mainCamera);
            SyncCameraPoseCache.ApplyCachedPoseOnDemand();
        }
    }

    internal sealed class SyncCameraComponent : MonoBehaviour
    {
        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (!SyncCameraTool.IsEnabled)
            {
                return;
            }

            if (sceneView == null || sceneView.camera == null)
            {
                return;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                return;
            }

            Transform sceneCameraTransform = sceneView.camera.transform;
            Transform mainCameraTransform = mainCamera.transform;
            mainCameraTransform.position = sceneCameraTransform.position;
            mainCameraTransform.rotation = sceneCameraTransform.rotation;
        }
    }
}
