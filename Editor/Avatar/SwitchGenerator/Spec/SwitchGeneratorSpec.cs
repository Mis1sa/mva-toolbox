using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace MVA.Toolbox.SwitchGenerator.Spec
{
    [Serializable]
    internal class SwitchGeneratorSpec
    {
        public VRCAvatarDescriptor avatar;
        public List<SwitchLayerSpec> layers = new List<SwitchLayerSpec>();
    }

    [Serializable]
    internal class SwitchLayerSpec
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
        public string boolMenuItemName;
        public string intSubMenuName;
        public string floatMenuItemName;
        public List<string> intMenuItemNames = new List<string>();

        public bool savedParameter;
        public bool syncedParameter;
        public int defaultBoolValue;
        public int defaultIntValue;
        public float defaultFloatValue;

        public List<SwitchTargetSpec> boolTargets = new List<SwitchTargetSpec>();
        public List<SwitchIntGroupSpec> intGroups = new List<SwitchIntGroupSpec>();
        public List<SwitchTargetSpec> floatTargets = new List<SwitchTargetSpec>();
    }

    [Serializable]
    internal class SwitchIntGroupSpec
    {
        public string stateName;
        public List<SwitchTargetSpec> targets = new List<SwitchTargetSpec>();
    }

    [Serializable]
    internal class SwitchTargetSpec
    {
        public GameObject targetObject;
        public SwitchGeneratorConfig.TargetControlType controlType;
        public string blendShapeName;

        public SwitchGeneratorConfig.BoolObjectState boolObjectState;
        public SwitchGeneratorConfig.BoolBlendShapeState boolBlendShapeState;

        public SwitchGeneratorConfig.FloatDirection floatDirection;
        public bool splitBlendShape;
        public string secondaryBlendShapeName;
        public SwitchGeneratorConfig.FloatDirection secondaryFloatDirection;
    }
}
