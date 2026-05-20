using UnityEditor.Animations;

namespace MVA.Toolbox.SwitchGenerator.Compile
{
    internal static class WriteDefaultsPolicyResolver
    {
        public static bool Resolve(SwitchGeneratorConfig.WriteDefaultsMode mode, AnimatorController controller)
        {
            if (mode == SwitchGeneratorConfig.WriteDefaultsMode.On)
            {
                return true;
            }

            if (mode == SwitchGeneratorConfig.WriteDefaultsMode.Off)
            {
                return false;
            }

            if (controller?.layers == null)
            {
                return false;
            }

            for (int i = 0; i < controller.layers.Length; i++)
            {
                var machine = controller.layers[i].stateMachine;
                if (machine?.states == null)
                {
                    continue;
                }

                for (int j = 0; j < machine.states.Length; j++)
                {
                    var state = machine.states[j].state;
                    if (state != null)
                    {
                        return state.writeDefaultValues;
                    }
                }
            }

            return false;
        }
    }
}
