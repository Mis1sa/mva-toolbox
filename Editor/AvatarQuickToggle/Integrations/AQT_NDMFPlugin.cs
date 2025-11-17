using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

[assembly: ExportsPlugin(typeof(MVA.Toolbox.AvatarQuickToggle.Integrations.AQT_NDMFPlugin))]

namespace MVA.Toolbox.AvatarQuickToggle.Integrations
{
    internal sealed class AQT_NDMFPlugin : Plugin<AQT_NDMFPlugin>
    {
        public override string QualifiedName => "mva.toolbox.avatar-quick-toggle";
        public override string DisplayName => "Avatar Quick Toggle";

        private const string ModularAvatarQualifiedName = "nadena.dev.modular-avatar";

        protected override void Configure()
        {
            var sequence = InPhase(BuildPhase.Generating)
                .BeforePlugin(ModularAvatarQualifiedName);

            sequence.Run("Apply Avatar Quick Toggle", context =>
            {
                if (context == null || context.AvatarRootObject == null)
                {
                    Debug.LogWarning("AQT_NDMFPlugin: BuildContext 或 AvatarRootObject 无效，跳过执行。");
                    return;
                }

                // 设计约束：QuickToggleConfig 只挂在 Avatar 根物体上，每个 Avatar 仅存在一个
                var configOnAvatar = context.AvatarRootObject.GetComponent<MVA.Toolbox.AvatarQuickToggle.QuickToggleConfig>();
                if (configOnAvatar == null)
                {
                    // 当前 Avatar 没有 AQT 配置，静默跳过即可
                    return;
                }

                if (configOnAvatar.layerConfigs == null || configOnAvatar.layerConfigs.Count == 0)
                {
                    Debug.Log($"AQT_NDMFPlugin: Avatar '{context.AvatarRootObject.name}' 的 QuickToggleConfig 没有任何层配置，跳过执行。");
                    return;
                }

                var avatarDescriptor = context.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
                if (avatarDescriptor == null)
                {
                    Debug.LogWarning("AQT_NDMFPlugin: AvatarRootObject 缺少 VRCAvatarDescriptor，跳过执行。");
                    return;
                }

                // 使用组件上的层配置列表，保持顺序即为执行顺序
                var aggregatedLayers = new List<MVA.Toolbox.AvatarQuickToggle.QuickToggleConfig.LayerConfig>();
                foreach (var layer in configOnAvatar.layerConfigs)
                {
                    if (layer == null) continue;
                    aggregatedLayers.Add(layer);
                }

                if (aggregatedLayers.Count == 0)
                {
                    Debug.Log($"AQT_NDMFPlugin: Avatar '{context.AvatarRootObject.name}' 的层配置全部为空引用，跳过执行。");
                    return;
                }

                // NDMF 模式生成逻辑由 NDMFApplyWorkflow 负责（定义于 NDMFApplyWorkflow.cs）
                var workflow = new MVA.Toolbox.AvatarQuickToggle.Workflows.NDMFApplyWorkflow(
                    avatarDescriptor,
                    aggregatedLayers);
                workflow.Execute(context);

                // 为避免 VRChat Avatar SDK 在运行时 Avatar 上看到 QuickToggleConfig 报“未知组件”并阻止上传，
                // 需要在 NDMF 构建出的 Avatar 对象上移除所有 QuickToggleConfig 组件。
                // 这里不再依赖名称是否以 "(Clone)" 结尾，而是直接在 BuildContext 提供的 AvatarRootObject 上
                // 递归查找并销毁 QuickToggleConfig；该对象是构建用克隆体，场景中的原始 Avatar 不会被修改。
                var runtimeConfigs = context.AvatarRootObject.GetComponentsInChildren<MVA.Toolbox.AvatarQuickToggle.QuickToggleConfig>(true);
                if (runtimeConfigs != null && runtimeConfigs.Length > 0)
                {
                    for (int i = 0; i < runtimeConfigs.Length; i++)
                    {
                        var cfg = runtimeConfigs[i];
                        if (cfg != null)
                        {
                            UnityEngine.Object.DestroyImmediate(cfg);
                        }
                    }
                }
            });
        }

        private static int CompareByHierarchyPath(MVA.Toolbox.AvatarQuickToggle.QuickToggleConfig a,
            MVA.Toolbox.AvatarQuickToggle.QuickToggleConfig b)
        {
            if (a == null || b == null) return 0;
            var pathA = GetHierarchyPath(a.transform);
            var pathB = GetHierarchyPath(b.transform);
            return string.Compare(pathA, pathB, StringComparison.Ordinal);
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null) return string.Empty;
            return transform.parent == null
                ? transform.name
                : GetHierarchyPath(transform.parent) + "/" + transform.name;
        }
    }
}
