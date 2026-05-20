using nadena.dev.ndmf;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

[assembly: ExportsPlugin(typeof(MVA.Toolbox.SwitchGenerator.Integration.SwitchGeneratorNdmfPlugin))]

namespace MVA.Toolbox.SwitchGenerator.Integration
{
    internal sealed class SwitchGeneratorNdmfPlugin : Plugin<SwitchGeneratorNdmfPlugin>
    {
        private const string ModularAvatarQualifiedName = "nadena.dev.modular-avatar";

        public override string QualifiedName => "mva.toolbox.switch-generator";
        public override string DisplayName => "Switch Generator";

        protected override void Configure()
        {
            var sequence = InPhase(BuildPhase.Generating)
                .BeforePlugin(ModularAvatarQualifiedName);

            sequence.Run("Apply Switch Generator", context =>
                {
                    if (context?.AvatarRootObject == null)
                    {
                        return;
                    }

                    var descriptor = context.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
                    if (descriptor == null)
                    {
                        return;
                    }

                    var config = context.AvatarRootObject.GetComponent<SwitchGeneratorConfig>();
                    if (config == null || config.layers == null || config.layers.Count == 0)
                    {
                        return;
                    }

                    Workflows.ApplyToNdmfWorkflow.Execute(context, config);

                    var configs = context.AvatarRootObject.GetComponentsInChildren<SwitchGeneratorConfig>(true);
                    for (int i = 0; i < configs.Length; i++)
                    {
                        if (configs[i] != null)
                        {
                            Object.DestroyImmediate(configs[i]);
                        }
                    }
                });
        }
    }
}
