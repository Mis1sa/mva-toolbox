using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace MVA.Toolbox.AvatarQuickToggle
{
    [DisallowMultipleComponent]
    [AddComponentMenu("MVA Toolbox/Avatar Quick Toggle")]
    public class QuickToggleConfig : MonoBehaviour, IEditorOnly
    {
        public VRCAvatarDescriptor targetAvatar;
        public List<LayerConfig> layerConfigs = new List<LayerConfig>();

        public void OnValidate()
        {
            // 校验：组件必须挂在包含 VRCAvatarDescriptor 的 Avatar 根物体上
            var descriptorOnObject = GetComponent<VRCAvatarDescriptor>();
            if (descriptorOnObject == null)
            {
#if UNITY_EDITOR
                UnityEngine.Debug.LogWarning("[AQT] QuickToggleConfig 只能挂在包含 VRCAvatarDescriptor 的 Avatar 根物体上，本组件将被移除。", this);
                // 在 Editor 中延迟销毁本组件，避免 OnValidate 期间直接修改对象集合
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

            // 同步 targetAvatar 与当前 Avatar 根对象
            targetAvatar = descriptorOnObject;

#if UNITY_EDITOR
            // 校验：同一个 Avatar 场景实例只允许存在一个 QuickToggleConfig
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
            // 确保每个 layerName 唯一
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
            // 配置显示名称，仅用于 Inspector 和窗口展示
            public string displayName;
            public string layerName;
            // 开关类型：0=Bool,1=Int,2=Float
            public int layerType;
            public string parameterName;
            public bool overwriteLayer;
            public bool overwriteParameter;
            public string clipSavePath;
            // Write Defaults 模式：0=Auto,1=On,2=Off
            public int writeDefaultSetting;
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
            // 目标控制类型
            public TargetControlType controlType;
            public string blendShapeName;
            // GameObject 在 ON 时的状态
            public GameObjectState goState;
            // BlendShape 在 ON 时的目标状态
            public BlendShapeState bsState;
            // Float 变化方向
            public FloatDirection direction;
            public bool splitBlendShape;
            public string secondaryBlendShapeName;
            // 第二个目标的 Float 变化方向
            public FloatDirection secondaryDirection;
        }

        [Serializable]
        public class IntGroupData
        {
            public string stateName;
            public List<TargetItemData> targetItems = new List<TargetItemData>();
        }

        // 控制目标类型
        public enum TargetControlType { GameObject, BlendShape }
        // GameObject 开关状态
        public enum GameObjectState { Active, Inactive }
        // BlendShape 端点状态
        public enum BlendShapeState { Zero, Full }
        // Float 从 Zero 到 Full 或反向变化
        public enum FloatDirection { ZeroToFull, FullToZero }
    }
}
