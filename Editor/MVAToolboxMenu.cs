using UnityEditor;
using MVA.Toolbox.AnimationBakeTool;
using MVA.Toolbox.AnimationQueryTool;
using MVA.Toolbox.AnimationRedirectTool;
using AnimationSwitchToolEntry = MVA.Toolbox.AnimationSwitchTool.AnimationSwitchTool;
using MVA.Toolbox.AnimatorParameterTool;
using MVA.Toolbox.AnimatorTransitionTool;
using MVA.Toolbox.AnimatorStateTool;
using MVA.Toolbox.SkinnedMeshBoneCleanup;
using MVA.Toolbox.SwitchGenerator.Entry;
using MVA.Toolbox.AnimatorBlendTreeTool;
using MVA.Toolbox.MaterialTextureReplaceTool;
using MVA.Toolbox.PhysBoneCollisionOverlay;
using MVA.Toolbox.FindReferencesTool;
using MVA.Toolbox.SyncCamera;

namespace MVA.Toolbox.Editor
{
    internal static class MVAToolboxMenu
    {
        [MenuItem("Tools/MVA Toolbox/Avatar/开关生成", false, 1)]
        private static void OpenSwitchGenerator()
        {
            SwitchGeneratorWindow.Open();
        }

        [MenuItem("Tools/MVA Toolbox/Avatar/移除网格权重骨骼", false, 2)]
        private static void OpenSkinnedMeshBoneCleanup()
        {
            SkinnedMeshBoneCleanupWindow.Open();
        }

        [MenuItem("Tools/MVA Toolbox/动画控制器/状态", false, 11)]
        private static void OpenAnimatorStateTool()
        {
            AnimatorStateToolWindow.Open();
        }

        [MenuItem("Tools/MVA Toolbox/动画控制器/过渡", false, 12)]
        private static void OpenAnimatorTransitionTool()
        {
            AnimatorTransitionToolWindow.Open();
        }

        [MenuItem("Tools/MVA Toolbox/动画控制器/参数", false, 13)]
        private static void OpenAnimatorParameterTool()
        {
            AnimatorParameterToolWindow.Open();
        }

        [MenuItem("Tools/MVA Toolbox/动画控制器/混合树", false, 14)]
        private static void OpenAnimatorBlendTreeTool()
        {
            AnimatorBlendTreeToolWindow.Open();
        }
        
        [MenuItem("Tools/MVA Toolbox/动画/默认值烘培", false, 21)]
        private static void OpenAnimationBakeTool()
        {
            AnimationBakeToolWindow.Open();
        }

        [MenuItem("Tools/MVA Toolbox/动画/动画查询", false, 22)]
        private static void OpenAnimationQueryTool()
        {
            AnimationQueryToolWindow.Open();
        }

        [MenuItem("Tools/MVA Toolbox/动画/重定向", false, 23)]
        private static void OpenAnimationRedirectTool()
        {
            AnimationRedirectToolWindow.Open();
        }

        [MenuItem("Tools/MVA Toolbox/动画/动画切换/上一个动画 #&%Z", false, 24)]
        private static void SwitchToPreviousAnimationClip()
        {
            AnimationSwitchToolEntry.SwitchToPreviousClip();
        }

        [MenuItem("Tools/MVA Toolbox/动画/动画切换/下一个动画 #&%X", false, 25)]
        private static void SwitchToNextAnimationClip()
        {
            AnimationSwitchToolEntry.SwitchToNextClip();
        }

        [MenuItem("Tools/MVA Toolbox/动画/动画切换/上一个动画 #&%Z", true)]
        [MenuItem("Tools/MVA Toolbox/动画/动画切换/下一个动画 #&%X", true)]
        private static bool ValidateAnimationSwitchTool()
        {
            return AnimationSwitchToolEntry.ValidateClipMenu();
        }

        [MenuItem("Tools/MVA Toolbox/材质/材质纹理替换", false, 31)]
        private static void OpenMaterialTextureReplaceTool()
        {
            MaterialTextureReplaceWindow.Open();
        }

        [MenuItem("Tools/MVA Toolbox/组件/Phys Bone碰撞检查", false, 41)]
        private static void TogglePhysBoneCollisionOverlayTool()
        {
            PhysBoneCollisionOverlayTool.ToggleOverlay();
            Menu.SetChecked("Tools/MVA Toolbox/组件/Phys Bone碰撞检查", PhysBoneCollisionOverlayTool.IsEnabled);
        }

        [MenuItem("Tools/MVA Toolbox/组件/Phys Bone碰撞检查", true)]
        private static bool ValidatePhysBoneCollisionOverlayTool()
        {
            Menu.SetChecked("Tools/MVA Toolbox/组件/Phys Bone碰撞检查", PhysBoneCollisionOverlayTool.IsEnabled);
            return PhysBoneCollisionOverlayTool.ValidateMenu();
        }

        [MenuItem("Tools/MVA Toolbox/杂项/引用查询", false, 51)]
        private static void OpenFindReferencesTool()
        {
            FindReferencesWindow.Open();
        }

        [MenuItem("Tools/MVA Toolbox/杂项/同步主摄像机到游戏窗口", false, 52)]
        private static void ToggleSyncCameraTool()
        {
            SyncCameraTool.Toggle();
            Menu.SetChecked("Tools/MVA Toolbox/杂项/同步主摄像机到游戏窗口", SyncCameraTool.IsEnabled);
        }

        [MenuItem("Tools/MVA Toolbox/杂项/同步主摄像机到游戏窗口", true)]
        private static bool ValidateSyncCameraTool()
        {
            Menu.SetChecked("Tools/MVA Toolbox/杂项/同步主摄像机到游戏窗口", SyncCameraTool.IsEnabled);
            return SyncCameraTool.ValidateMenu();
        }
    }
}
