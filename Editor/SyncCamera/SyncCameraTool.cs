#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.SyncCamera.Editor
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
            }
            else
            {
                StopRecording(clearCache: true);
            }
        }

        private static void HandlePlayModeChange(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                StopRecording(clearCache: false);
                ApplyCachedPoseImmediate();
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                RefreshRecordingState();
            }
        }

        private static void RefreshRecordingState()
        {
            if (!Application.isPlaying && SyncCameraTool.IsSyncEnabled())
            {
                StartRecording();
            }
            else
            {
                StopRecording(clearCache: false);
            }
        }

        private static void StartRecording()
        {
            if (_isRecording)
                return;

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

            if (clearCache)
            {
                _hasPose = false;
                _cachedPosition = default;
                _cachedRotation = Quaternion.identity;
            }
        }

        private static void RecordScenePose(SceneView sceneView)
        {
            if (sceneView == null || sceneView.camera == null)
                return;

            if (Application.isPlaying)
                return;

            if (!SyncCameraTool.IsSyncEnabled())
                return;

            var transform = sceneView.camera.transform;
            _cachedPosition = transform.position;
            _cachedRotation = transform.rotation;
            _hasPose = true;
        }

        private static void ApplyCachedPoseImmediate()
        {
            if (!_hasPose)
                return;

            var mainCam = Camera.main;
            if (mainCam == null)
                return;

            mainCam.transform.SetPositionAndRotation(_cachedPosition, _cachedRotation);
        }

        internal static void ApplyCachedPoseOnDemand()
        {
            if (!Application.isPlaying)
                return;

            ApplyCachedPoseImmediate();
        }
    }

    // 菜单：切换主摄像机在播放模式下是否跟随 Scene 视图相机
    public static class SyncCameraTool
    {
        private const string MenuPath = "Tools/MVA Toolbox/Sync Main Camera to Scene View";
        private const string EditorPrefsKey = "MVA_SyncCamera_Enabled";

        [MenuItem(MenuPath, false, 32)]
        private static void ToggleSyncCamera()
        {
            bool isEnabled = !IsSyncEnabled();
            EditorPrefs.SetBool(EditorPrefsKey, isEnabled);
            Menu.SetChecked(MenuPath, isEnabled);
            Debug.Log($"[SyncCamera] 主摄像机与场景视图的同步功能已{(isEnabled ? "启用" : "禁用")}");
            SyncCameraPoseCache.OnSyncToggleChanged(isEnabled);

            if (EditorApplication.isPlaying)
            {
                if (isEnabled)
                {
                    var mainCam = EnsureMainCameraExists();
                    EnsureSyncComponent(mainCam);
                    SyncCameraPoseCache.ApplyCachedPoseOnDemand();
                }
                else
                {
                    RemoveSyncComponents();
                }
            }
        }

        internal static Camera EnsureMainCameraExists()
        {
            var mainCam = Camera.main;
            if (mainCam != null)
            {
                return mainCam;
            }

            Debug.LogWarning("[SyncCamera] 场景中没有主摄像机，已自动创建一个 Main Camera。");
            var newCamera = new GameObject("Main Camera");
            newCamera.tag = "MainCamera";
            var cam = newCamera.AddComponent<Camera>();
            newCamera.transform.position = new Vector3(0f, 1f, -10f);
            newCamera.transform.rotation = Quaternion.identity;
            return cam;
        }

        internal static void EnsureSyncComponent(Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            if (camera.GetComponent<SyncCameraComponent>() == null)
            {
                camera.gameObject.AddComponent<SyncCameraComponent>();
            }
        }

        internal static void RemoveSyncComponents()
        {
            var comps = Object.FindObjectsOfType<SyncCameraComponent>(true);
            for (int i = 0; i < comps.Length; i++)
            {
                var comp = comps[i];
                if (comp != null)
                {
                    Object.DestroyImmediate(comp);
                }
            }
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateToggleSyncCamera()
        {
            Menu.SetChecked(MenuPath, IsSyncEnabled());
            return true;
        }

        public static bool IsSyncEnabled()
        {
            return EditorPrefs.GetBool(EditorPrefsKey, false);
        }
    }

    // 播放模式切换时，根据设置为主摄像机挂载/移除同步组件
    [InitializeOnLoad]
    public static class SyncCameraInitializer
    {
        static SyncCameraInitializer()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                if (!SyncCameraTool.IsSyncEnabled())
                    return;

                var mainCam = SyncCameraTool.EnsureMainCameraExists();
                SyncCameraTool.EnsureSyncComponent(mainCam);
                SyncCameraPoseCache.ApplyCachedPoseOnDemand();
            }
        }
    }

    // 运行时将主摄像机对齐到 Scene 视图相机（仅在 Scene 视图绘制时触发，避免无 Scene 窗口时重置）
    public sealed class SyncCameraComponent : MonoBehaviour
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
                return;

            if (sceneView == null || sceneView.camera == null)
                return;

            var mainCam = Camera.main;
            if (mainCam == null)
                return;

            var sceneTransform = sceneView.camera.transform;
            var camTransform = mainCam.transform;
            camTransform.position = sceneTransform.position;
            camTransform.rotation = sceneTransform.rotation;
        }
    }
}

#endif
