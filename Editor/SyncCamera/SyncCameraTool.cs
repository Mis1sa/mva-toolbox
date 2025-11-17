#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.SyncCamera.Editor
{
    // 菜单：切换主摄像机在播放模式下是否跟随 Scene 视图相机
    public static class SyncCameraTool
    {
        private const string MenuPath = "Tools/MVA Toolbox/Sync Main Camera to Scene View";
        private const string EditorPrefsKey = "MVA_SyncCamera_Enabled";

        [MenuItem(MenuPath, false, 31)]
        private static void ToggleSyncCamera()
        {
            bool isEnabled = !IsSyncEnabled();
            EditorPrefs.SetBool(EditorPrefsKey, isEnabled);
            Menu.SetChecked(MenuPath, isEnabled);
            Debug.Log($"[SyncCamera] 主摄像机与场景视图的同步功能已{(isEnabled ? "启用" : "禁用")}");

            if (EditorApplication.isPlaying)
            {
                if (isEnabled)
                {
                    // 确保存在主摄像机
                    if (Camera.main == null)
                    {
                        Debug.LogWarning("[SyncCamera] 场景中没有主摄像机，已自动创建一个 Main Camera。");
                        var newCamera = new GameObject("Main Camera");
                        newCamera.tag = "MainCamera";
                        newCamera.AddComponent<Camera>();
                        newCamera.transform.position = new Vector3(0f, 1f, -10f);
                        newCamera.transform.rotation = Quaternion.identity;
                    }

                    var mainCam = Camera.main;
                    if (mainCam != null && mainCam.GetComponent<SyncCameraComponent>() == null)
                    {
                        mainCam.gameObject.AddComponent<SyncCameraComponent>();
                    }
                }
                else
                {
                    // 在播放模式下禁用时，移除主摄像机上的同步组件
                    var mainCam = Camera.main;
                    if (mainCam != null)
                    {
                        var comp = mainCam.GetComponent<SyncCameraComponent>();
                        if (comp != null)
                        {
                            Object.DestroyImmediate(comp);
                        }
                    }
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

                if (Camera.main == null)
                {
                    Debug.LogWarning("[SyncCamera] 场景中没有主摄像机，已自动创建一个 Main Camera。");
                    var newCamera = new GameObject("Main Camera");
                    newCamera.tag = "MainCamera";
                    newCamera.AddComponent<Camera>();
                    newCamera.transform.position = new Vector3(0f, 1f, -10f);
                    newCamera.transform.rotation = Quaternion.identity;
                }

                var mainCam = Camera.main;
                if (mainCam != null && mainCam.GetComponent<SyncCameraComponent>() == null)
                {
                    mainCam.gameObject.AddComponent<SyncCameraComponent>();
                }
            }
        }
    }

    // 运行时将主摄像机对齐到 Scene 视图相机
    public sealed class SyncCameraComponent : MonoBehaviour
    {
        private void Update()
        {
            if (!Application.isPlaying)
                return;

            var sceneView = SceneView.lastActiveSceneView;
            var mainCam = Camera.main;
            if (sceneView == null || mainCam == null)
                return;

            var sceneTransform = sceneView.camera.transform;
            var camTransform = mainCam.transform;
            camTransform.position = sceneTransform.position;
            camTransform.rotation = sceneTransform.rotation;
        }
    }
}

#endif
