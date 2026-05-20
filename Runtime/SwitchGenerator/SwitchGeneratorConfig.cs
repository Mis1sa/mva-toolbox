using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace MVA.Toolbox.SwitchGenerator
{
    [DisallowMultipleComponent]
    [AddComponentMenu("MVA Toolbox/开关生成配置")]
    public class SwitchGeneratorConfig : MonoBehaviour, IEditorOnly
    {
        public VRCAvatarDescriptor targetAvatar;
        public List<LayerConfig> layers = new List<LayerConfig>();

        public void OnValidate()
        {
            var descriptor = GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this != null)
                    {
                        DestroyImmediate(this, true);
                    }
                };
#endif
                return;
            }

            targetAvatar = descriptor;
            layers ??= new List<LayerConfig>();

            var usedLayerNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (layer == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(layer.layerName))
                {
                    layer.layerName = $"SwitchLayer_{i + 1}";
                }

                layer.EnsureCollections();

                string baseName = layer.layerName.Trim();
                string candidate = baseName;
                int suffix = 1;
                while (usedLayerNames.Contains(candidate))
                {
                    candidate = baseName + "_" + suffix;
                    suffix++;
                }

                layer.layerName = candidate;
                usedLayerNames.Add(candidate);
            }
        }

        [Serializable]
        public class LayerConfig
        {
            public string displayName;
            public string layerName;
            public SwitchType switchType;
            public string parameterName;
            public bool overwriteLayer;
            public bool overwriteParameter;
            public string clipSaveRoot = "Assets/MVA Toolbox/SwitchGenerator";
            public WriteDefaultsMode writeDefaults = WriteDefaultsMode.Auto;

            public bool generateMenuControl = true;
            public string menuPath = "/";
            public string boolMenuItemName;
            public string intSubMenuName;
            public string floatMenuItemName;
            public List<string> intMenuItemNames = new List<string>();

            public bool savedParameter = true;
            public bool syncedParameter = true;
            public int defaultBoolValue;
            public int defaultIntValue;
            public float defaultFloatValue;

            public bool editInWriteDefaultsOnMode;

            public List<TargetItem> boolTargets = new List<TargetItem>();
            public List<IntGroup> intGroups = new List<IntGroup>();
            public List<TargetItem> floatTargets = new List<TargetItem>();

            public void EnsureCollections()
            {
                intMenuItemNames ??= new List<string>();
                boolTargets ??= new List<TargetItem>();
                intGroups ??= new List<IntGroup>();
                floatTargets ??= new List<TargetItem>();
            }
        }

        [Serializable]
        public class IntGroup
        {
            public string stateName;
            public List<TargetItem> targets = new List<TargetItem>();
        }

        [Serializable]
        public class TargetItem
        {
            public GameObject targetObject;
            public TargetControlType controlType;
            public string blendShapeName;

            public BoolObjectState boolObjectState = BoolObjectState.Active;
            public BoolBlendShapeState boolBlendShapeState = BoolBlendShapeState.Zero;

            public FloatDirection floatDirection = FloatDirection.ZeroToFull;
            public bool splitBlendShape;
            public string secondaryBlendShapeName;
            public FloatDirection secondaryFloatDirection = FloatDirection.ZeroToFull;
        }

        public enum SwitchType
        {
            Bool = 0,
            Int = 1,
            Float = 2
        }

        public enum WriteDefaultsMode
        {
            Auto = 0,
            On = 1,
            Off = 2
        }

        public enum TargetControlType
        {
            GameObject = 0,
            BlendShape = 1
        }

        public enum BoolObjectState
        {
            Active = 0,
            Inactive = 1
        }

        public enum BoolBlendShapeState
        {
            Zero = 0,
            Full = 1
        }

        public enum FloatDirection
        {
            ZeroToFull = 0,
            FullToZero = 1
        }
    }
}
