using System.Collections.Generic;
using MVA.Toolbox.SwitchGenerator.Compile;
using MVA.Toolbox.SwitchGenerator.Emit.Animator;
using MVA.Toolbox.SwitchGenerator.Emit.Vrc;
using MVA.Toolbox.SwitchGenerator.Spec;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace MVA.Toolbox.SwitchGenerator.Workflows
{
    internal static class ApplyToNdmfWorkflow
    {
        public static void Execute(BuildContext context, SwitchGeneratorConfig config)
        {
            if (context?.AvatarRootObject == null || config == null)
            {
                return;
            }

            var descriptor = context.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                return;
            }

            var fx = CloneFxController(descriptor, context.AssetContainer);
            var expressionParameters = CloneExpressionParameters(descriptor.expressionParameters, descriptor, context.AssetContainer);
            var expressionsMenu = CloneMenu(descriptor.expressionsMenu, descriptor, context.AssetContainer, new Dictionary<VRCExpressionsMenu, VRCExpressionsMenu>());

            if (fx == null || expressionParameters == null || expressionsMenu == null)
            {
                return;
            }

            var spec = SwitchGeneratorSpecFactory.FromConfig(config);
            spec.avatar = descriptor;
            SwitchGeneratorSpecNormalizer.Normalize(spec);
            if (!SwitchGeneratorSpecValidator.Validate(spec, out _))
            {
                return;
            }

            var plan = SwitchPlanCompiler.Compile(spec, fx, expressionParameters, false);
            for (int i = 0; i < plan.layers.Count; i++)
            {
                var layer = plan.layers[i];
                AnimatorParameterEmitter.Upsert(fx, layer);
                AnimatorLayerEmitter.Upsert(fx, layer, descriptor.gameObject);

                float defaultValue = layer.switchType == SwitchGeneratorConfig.SwitchType.Bool
                    ? layer.defaultBoolValue
                    : layer.switchType == SwitchGeneratorConfig.SwitchType.Int
                        ? layer.defaultIntValue
                        : layer.defaultFloatValue;

                VrcParameterEmitter.Upsert(
                    expressionParameters,
                    layer.parameterName,
                    layer.parameterType,
                    defaultValue,
                    layer.savedParameter,
                    layer.syncedParameter);

                VrcMenuEmitter.Upsert(expressionsMenu, layer);
            }

            descriptor.expressionParameters = expressionParameters;
            descriptor.expressionsMenu = expressionsMenu;
            AssignFxController(descriptor, fx);
            EditorUtility.SetDirty(descriptor);
        }

        private static AnimatorController CloneFxController(VRCAvatarDescriptor descriptor, Object assetContainer)
        {
            var layers = descriptor.baseAnimationLayers ?? System.Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            int fxIndex = -1;
            AnimatorController src = null;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].type != VRCAvatarDescriptor.AnimLayerType.FX)
                {
                    continue;
                }

                fxIndex = i;
                src = layers[i].animatorController as AnimatorController;
                break;
            }

            var clone = new AnimatorController
            {
                name = src != null ? src.name + "_SwitchGen" : descriptor.gameObject.name + "_FX_SwitchGen"
            };

            if (src != null)
            {
                EditorUtility.CopySerialized(src, clone);
            }

            if (assetContainer != null)
            {
                AssetDatabase.AddObjectToAsset(clone, assetContainer);
            }

            if (fxIndex >= 0)
            {
                var layer = layers[fxIndex];
                layer.animatorController = clone;
                layer.isDefault = false;
                layers[fxIndex] = layer;
                descriptor.baseAnimationLayers = layers;
            }
            else
            {
                var list = new List<VRCAvatarDescriptor.CustomAnimLayer>(layers)
                {
                    new VRCAvatarDescriptor.CustomAnimLayer
                    {
                        type = VRCAvatarDescriptor.AnimLayerType.FX,
                        isDefault = false,
                        isEnabled = true,
                        animatorController = clone
                    }
                };
                descriptor.baseAnimationLayers = list.ToArray();
            }

            return clone;
        }

        private static void AssignFxController(VRCAvatarDescriptor descriptor, AnimatorController fx)
        {
            var layers = descriptor.baseAnimationLayers;
            if (layers == null)
            {
                return;
            }

            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].type != VRCAvatarDescriptor.AnimLayerType.FX)
                {
                    continue;
                }

                var layer = layers[i];
                layer.animatorController = fx;
                layer.isDefault = false;
                layers[i] = layer;
                descriptor.baseAnimationLayers = layers;
                return;
            }
        }

        private static VRCExpressionParameters CloneExpressionParameters(VRCExpressionParameters source, VRCAvatarDescriptor descriptor, Object assetContainer)
        {
            var clone = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            clone.name = descriptor.gameObject.name + "_Parameters_SwitchGen";
            clone.parameters = source?.parameters != null
                ? (VRCExpressionParameters.Parameter[])source.parameters.Clone()
                : new VRCExpressionParameters.Parameter[0];

            if (assetContainer != null)
            {
                AssetDatabase.AddObjectToAsset(clone, assetContainer);
            }

            return clone;
        }

        private static VRCExpressionsMenu CloneMenu(
            VRCExpressionsMenu source,
            VRCAvatarDescriptor descriptor,
            Object assetContainer,
            Dictionary<VRCExpressionsMenu, VRCExpressionsMenu> map)
        {
            if (source == null)
            {
                var created = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                created.name = descriptor.gameObject.name + "_Menu_SwitchGen";
                created.controls = new List<VRCExpressionsMenu.Control>();
                if (assetContainer != null)
                {
                    AssetDatabase.AddObjectToAsset(created, assetContainer);
                }

                return created;
            }

            if (map.TryGetValue(source, out var cached))
            {
                return cached;
            }

            var clone = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            clone.name = source.name;
            clone.controls = new List<VRCExpressionsMenu.Control>();
            map[source] = clone;

            if (assetContainer != null)
            {
                AssetDatabase.AddObjectToAsset(clone, assetContainer);
            }

            if (source.controls == null)
            {
                return clone;
            }

            for (int i = 0; i < source.controls.Count; i++)
            {
                var control = source.controls[i];
                if (control == null)
                {
                    continue;
                }

                var newControl = new VRCExpressionsMenu.Control
                {
                    name = control.name,
                    icon = control.icon,
                    type = control.type,
                    parameter = control.parameter,
                    value = control.value,
                    style = control.style
                };

                if (control.subParameters != null && control.subParameters.Length > 0)
                {
                    var sub = new VRCExpressionsMenu.Control.Parameter[control.subParameters.Length];
                    for (int j = 0; j < sub.Length; j++)
                    {
                        sub[j] = new VRCExpressionsMenu.Control.Parameter { name = control.subParameters[j].name };
                    }

                    newControl.subParameters = sub;
                }

                if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu && control.subMenu != null)
                {
                    newControl.subMenu = CloneMenu(control.subMenu, descriptor, assetContainer, map);
                }

                clone.controls.Add(newControl);
            }

            return clone;
        }
    }
}
