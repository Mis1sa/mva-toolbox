using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace MVA.Toolbox.AvatarQuickToggle
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Avatar Quick Toggle")]
    public class QuickToggleConfig : MonoBehaviour
    {
        public VRCAvatarDescriptor targetAvatar;
        public List<LayerConfig> layerConfigs = new List<LayerConfig>();

        public void OnValidate()
        {
            // 约束 1：组件必须挂在包含 VRCAvatarDescriptor 的 Avatar 根物体上
            var descriptorOnObject = GetComponent<VRCAvatarDescriptor>();
            if (descriptorOnObject == null)
            {
#if UNITY_EDITOR
                UnityEngine.Debug.LogWarning("[AQT] QuickToggleConfig 只能挂在包含 VRCAvatarDescriptor 的 Avatar 根物体上，本组件将被移除。", this);
                // 在 Editor 下延迟销毁自身，避免在 OnValidate 期间直接修改对象集合导致错误
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this != null)
                    {
                        UnityEngine.Object.DestroyImmediate(this, true);
                    }
                };
#endif
                return;
            }

            // 保持 targetAvatar 与当前 Avatar 根对象一致
            targetAvatar = descriptorOnObject;

#if UNITY_EDITOR
            // 约束 2：同一个 Avatar 场景实例上最多只允许存在一个 QuickToggleConfig
            var allConfigs = UnityEngine.Object.FindObjectsOfType<QuickToggleConfig>(true);
            foreach (var cfg in allConfigs)
            {
                if (cfg == null || cfg == this) continue;
                if (cfg.targetAvatar != null && cfg.targetAvatar == targetAvatar)
                {
                    UnityEngine.Debug.LogWarning($"[AQT] Avatar '{targetAvatar.gameObject.name}' 上已存在一个 QuickToggleConfig，本组件将被移除。", this);
                    UnityEditor.EditorApplication.delayCall += () =>
                    {
                        if (this != null)
                        {
                            UnityEngine.Object.DestroyImmediate(this, true);
                        }
                    };
                    return;
                }
            }
#endif
            if (layerConfigs == null)
                layerConfigs = new List<LayerConfig>();
            // ensure unique layer names
            for (int i = 0; i < layerConfigs.Count; i++)
            {
                if (string.IsNullOrEmpty(layerConfigs[i].layerName)) continue;
                for (int j = i + 1; j < layerConfigs.Count; j++)
                {
                    if (layerConfigs[j] != null && layerConfigs[i].layerName == layerConfigs[j].layerName)
                    {
                        layerConfigs[j].layerName += "_1";
                    }

                }
            }
        }

        public LayerConfig GetConfiguration(string layerName)
        {
            return layerConfigs.FirstOrDefault(x => x != null && x.layerName == layerName);
        }

        public void RemoveConfiguration(string layerName)
        {
            layerConfigs.RemoveAll(x => x != null && x.layerName == layerName);
        }

        public void UpdateConfiguration(LayerConfig updated)
        {
            if (updated == null) return;
            var idx = layerConfigs.FindIndex(x => x != null && x.layerName == updated.layerName);
            if (idx >= 0) layerConfigs[idx] = updated; else layerConfigs.Add(updated);
        }

        [Serializable]
        public class LayerConfig
        {
            // 配置名称，用于在组件和“配置脚本”列表中显示，不影响 Animator 层级名称
            public string displayName;
            public string layerName;
            public int layerType; // 0=Bool,1=Int,2=Float
            public string parameterName;
            public bool overwriteLayer;
            public bool overwriteParameter;
            public string clipSavePath;
            public int writeDefaultSetting; // 0=Auto,1=On,2=Off
            public bool createMenuControl;
            public string menuControlName;
            public string boolMenuItemName;
            public string floatMenuItemName;
            public string intSubMenuName;
            public List<string> intMenuItemNames = new List<string>();
            public string menuPath;
            public bool savedParameter;
            public bool syncedParameter;
            public int defaultStateSelection;
            public int defaultIntValue;
            public float defaultFloatValue;
            public List<TargetItemData> boolTargets = new List<TargetItemData>();
            public List<IntGroupData> intGroups = new List<IntGroupData>();
            public List<TargetItemData> floatTargets = new List<TargetItemData>();
            public bool editInWDOnMode;
        }

        [Serializable]
        public class TargetItemData
        {
            public GameObject targetObject;
            public TargetControlType controlType;
            public string blendShapeName;
            public GameObjectState goState;
            public BlendShapeState bsState;
            public FloatDirection direction;
            public bool splitBlendShape;
            public string secondaryBlendShapeName;
            public FloatDirection secondaryDirection;
        }

        [Serializable]
        public class IntGroupData
        {
            public string stateName;
            public List<TargetItemData> targetItems = new List<TargetItemData>();
        }

        public enum TargetControlType { GameObject, BlendShape }
        public enum GameObjectState { Active, Inactive }
        public enum BlendShapeState { Zero, Full }
        public enum FloatDirection { ZeroToFull, FullToZero }
    }
}
