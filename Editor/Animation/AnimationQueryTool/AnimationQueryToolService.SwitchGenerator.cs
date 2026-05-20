using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using MVA.Toolbox.SwitchGenerator;

namespace MVA.Toolbox.AnimationQueryTool
{
    internal sealed partial class AnimationQueryToolService
    {
        internal IReadOnlyList<string> BuildSwitchGeneratorConfigHints()
        {
            if (_avatarDescriptor == null || _selectedAnimatedObject == null)
            {
                return Array.Empty<string>();
            }

            GameObject avatarRoot = _avatarDescriptor.gameObject;
            if (avatarRoot == null)
            {
                return Array.Empty<string>();
            }

            SwitchGeneratorConfig config = avatarRoot.GetComponent<SwitchGeneratorConfig>();
            if (config == null || config.layers == null || config.layers.Count == 0)
            {
                return Array.Empty<string>();
            }

            GameObject targetGo = (_selectedAnimatedObject as GameObject) ?? (_selectedAnimatedObject as Component)?.gameObject;
            if (targetGo == null)
            {
                return Array.Empty<string>();
            }

            AnimatorController selectedController = SelectedController;
            if (selectedController == null)
            {
                return Array.Empty<string>();
            }

            bool allowScope = false;
            if (_selectedLayerIndex < 0)
            {
                allowScope = true;
            }
            else
            {
                VRCAvatarDescriptor.CustomAnimLayer[] baseLayers = _avatarDescriptor.baseAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                for (int i = 0; i < baseLayers.Length; i++)
                {
                    VRCAvatarDescriptor.CustomAnimLayer layer = baseLayers[i];
                    if (layer.type == VRCAvatarDescriptor.AnimLayerType.FX && layer.animatorController == selectedController)
                    {
                        allowScope = true;
                        break;
                    }
                }
            }

            if (!allowScope)
            {
                return Array.Empty<string>();
            }

            if (_selectedGroupIndex < 0 || _selectedGroupIndex > _availableGroups.Count)
            {
                return Array.Empty<string>();
            }

            bool isAllProperties = _selectedGroupIndex == 0;
            PropertyGroupData currentGroup = null;
            bool isBlendShapeGroup = false;
            bool isActiveGroup = false;

            if (!isAllProperties)
            {
                currentGroup = _availableGroups[_selectedGroupIndex - 1];
                isBlendShapeGroup =
                    currentGroup.ComponentType == typeof(SkinnedMeshRenderer) &&
                    currentGroup.CanonicalPropertyName == "blendShape";

                isActiveGroup =
                    currentGroup.ComponentType == typeof(GameObject) &&
                    (currentGroup.CanonicalPropertyName == "m_IsActive" ||
                     currentGroup.CanonicalPropertyName == "IsActive");

                if (!isBlendShapeGroup && !isActiveGroup)
                {
                    return Array.Empty<string>();
                }
            }

            List<string> hits = new List<string>();
            for (int i = 0; i < config.layers.Count; i++)
            {
                SwitchGeneratorConfig.LayerConfig layer = config.layers[i];
                if (layer == null)
                {
                    continue;
                }

                bool affectsTarget = false;
                switch (layer.switchType)
                {
                    case SwitchGeneratorConfig.SwitchType.Bool:
                        if (layer.boolTargets != null)
                        {
                            for (int t = 0; t < layer.boolTargets.Count; t++)
                            {
                                SwitchGeneratorConfig.TargetItem target = layer.boolTargets[t];
                                if (target == null || target.targetObject != targetGo)
                                {
                                    continue;
                                }

                                if (isAllProperties ||
                                    (isBlendShapeGroup && target.controlType == SwitchGeneratorConfig.TargetControlType.BlendShape) ||
                                    (isActiveGroup && target.controlType == SwitchGeneratorConfig.TargetControlType.GameObject))
                                {
                                    affectsTarget = true;
                                    break;
                                }
                            }
                        }
                        break;
                    case SwitchGeneratorConfig.SwitchType.Int:
                        if (layer.intGroups != null)
                        {
                            for (int g = 0; g < layer.intGroups.Count && !affectsTarget; g++)
                            {
                                SwitchGeneratorConfig.IntGroup group = layer.intGroups[g];
                                if (group?.targets == null)
                                {
                                    continue;
                                }

                                for (int t = 0; t < group.targets.Count; t++)
                                {
                                    SwitchGeneratorConfig.TargetItem target = group.targets[t];
                                    if (target == null || target.targetObject != targetGo)
                                    {
                                        continue;
                                    }

                                    if (isAllProperties ||
                                        (isBlendShapeGroup && target.controlType == SwitchGeneratorConfig.TargetControlType.BlendShape) ||
                                        (isActiveGroup && target.controlType == SwitchGeneratorConfig.TargetControlType.GameObject))
                                    {
                                        affectsTarget = true;
                                        break;
                                    }
                                }
                            }
                        }
                        break;
                    case SwitchGeneratorConfig.SwitchType.Float:
                        if (layer.floatTargets != null)
                        {
                            for (int t = 0; t < layer.floatTargets.Count; t++)
                            {
                                SwitchGeneratorConfig.TargetItem target = layer.floatTargets[t];
                                if (target == null || target.targetObject != targetGo)
                                {
                                    continue;
                                }

                                if (isAllProperties || isBlendShapeGroup)
                                {
                                    affectsTarget = true;
                                    break;
                                }
                            }
                        }
                        break;
                }

                if (!affectsTarget)
                {
                    continue;
                }

                string typeLabel = layer.switchType == SwitchGeneratorConfig.SwitchType.Bool
                    ? "Bool"
                    : layer.switchType == SwitchGeneratorConfig.SwitchType.Int
                        ? "Int"
                        : "Float";

                string name = !string.IsNullOrEmpty(layer.displayName)
                    ? layer.displayName
                    : (!string.IsNullOrEmpty(layer.layerName) ? layer.layerName : "(未命名配置)");
                hits.Add($"{name} ({typeLabel})");
            }

            return hits.Count > 0 ? hits : Array.Empty<string>();
        }

        private void AugmentGroupsWithSwitchGeneratorConfigForTarget()
        {
            if (_avatarDescriptor == null || _selectedAnimatedObject == null)
            {
                return;
            }

            GameObject avatarRoot = _avatarDescriptor.gameObject;
            if (avatarRoot == null)
            {
                return;
            }

            SwitchGeneratorConfig config = avatarRoot.GetComponent<SwitchGeneratorConfig>();
            if (config == null || config.layers == null || config.layers.Count == 0)
            {
                return;
            }

            GameObject targetGo = (_selectedAnimatedObject as GameObject) ?? (_selectedAnimatedObject as Component)?.gameObject;
            if (targetGo == null)
            {
                return;
            }

            bool needActiveGroup = false;
            bool needBlendShapeGroup = false;
            for (int i = 0; i < config.layers.Count; i++)
            {
                SwitchGeneratorConfig.LayerConfig layer = config.layers[i];
                if (layer == null)
                {
                    continue;
                }

                switch (layer.switchType)
                {
                    case SwitchGeneratorConfig.SwitchType.Bool:
                        if (layer.boolTargets != null)
                        {
                            for (int t = 0; t < layer.boolTargets.Count; t++)
                            {
                                SwitchGeneratorConfig.TargetItem target = layer.boolTargets[t];
                                if (target == null || target.targetObject != targetGo)
                                {
                                    continue;
                                }

                                if (target.controlType == SwitchGeneratorConfig.TargetControlType.GameObject)
                                {
                                    needActiveGroup = true;
                                }
                                else if (target.controlType == SwitchGeneratorConfig.TargetControlType.BlendShape)
                                {
                                    needBlendShapeGroup = true;
                                }
                            }
                        }
                        break;
                    case SwitchGeneratorConfig.SwitchType.Int:
                        if (layer.intGroups != null)
                        {
                            for (int g = 0; g < layer.intGroups.Count; g++)
                            {
                                SwitchGeneratorConfig.IntGroup group = layer.intGroups[g];
                                if (group?.targets == null)
                                {
                                    continue;
                                }

                                for (int t = 0; t < group.targets.Count; t++)
                                {
                                    SwitchGeneratorConfig.TargetItem target = group.targets[t];
                                    if (target == null || target.targetObject != targetGo)
                                    {
                                        continue;
                                    }

                                    if (target.controlType == SwitchGeneratorConfig.TargetControlType.GameObject)
                                    {
                                        needActiveGroup = true;
                                    }
                                    else if (target.controlType == SwitchGeneratorConfig.TargetControlType.BlendShape)
                                    {
                                        needBlendShapeGroup = true;
                                    }
                                }
                            }
                        }
                        break;
                    case SwitchGeneratorConfig.SwitchType.Float:
                        if (layer.floatTargets != null)
                        {
                            for (int t = 0; t < layer.floatTargets.Count; t++)
                            {
                                SwitchGeneratorConfig.TargetItem target = layer.floatTargets[t];
                                if (target != null && target.targetObject == targetGo)
                                {
                                    needBlendShapeGroup = true;
                                }
                            }
                        }
                        break;
                }
            }

            if (!needActiveGroup && !needBlendShapeGroup)
            {
                return;
            }

            bool hasActiveGroup = _availableGroups.Any(group =>
                group.ComponentType == typeof(GameObject) && group.CanonicalPropertyName == "m_IsActive");

            bool hasBlendShapeGroup = _availableGroups.Any(group =>
                group.ComponentType == typeof(SkinnedMeshRenderer) && group.CanonicalPropertyName == "blendShape");

            if (needActiveGroup && !hasActiveGroup)
            {
                PropertyGroupData group = new PropertyGroupData
                {
                    ComponentType = typeof(GameObject),
                    CanonicalPropertyName = "m_IsActive",
                    GroupDisplayName = "GameObject: IsActive"
                };
                group.BoundPropertyNames.Add("m_IsActive");
                _availableGroups.Add(group);
            }

            if (needBlendShapeGroup && !hasBlendShapeGroup)
            {
                PropertyGroupData group = new PropertyGroupData
                {
                    ComponentType = typeof(SkinnedMeshRenderer),
                    CanonicalPropertyName = "blendShape",
                    GroupDisplayName = "SkinnedMeshRenderer: blendShape"
                };
                _availableGroups.Add(group);
            }
        }
    }
}
