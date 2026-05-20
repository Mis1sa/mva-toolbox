using System.Collections.Generic;
using MVA.Toolbox.SwitchGenerator.Spec;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace MVA.Toolbox.SwitchGenerator.Compile
{
    internal sealed class SwitchBuildPlan
    {
        public VRCAvatarDescriptor avatar;
        public List<SwitchLayerPlan> layers = new List<SwitchLayerPlan>();
    }

    internal sealed class SwitchLayerPlan
    {
        public string displayName;
        public string layerName;
        public SwitchGeneratorConfig.SwitchType switchType;
        public string parameterName;
        public bool overwriteLayer;
        public bool overwriteParameter;
        public string clipSaveRoot;
        public SwitchGeneratorConfig.WriteDefaultsMode writeDefaults;

        public bool generateMenuControl;
        public string menuPath;
        public string menuControlName;
        public List<string> intMenuItemNames = new List<string>();

        public bool savedParameter;
        public bool syncedParameter;
        public int defaultBoolValue;
        public int defaultIntValue;
        public float defaultFloatValue;
        public bool persistAssets = true;

        public VRCExpressionParameters.ValueType parameterType;

        public List<SwitchTargetSpec> boolTargets = new List<SwitchTargetSpec>();
        public List<SwitchIntGroupSpec> intGroups = new List<SwitchIntGroupSpec>();
        public List<SwitchTargetSpec> floatTargets = new List<SwitchTargetSpec>();
    }
}
