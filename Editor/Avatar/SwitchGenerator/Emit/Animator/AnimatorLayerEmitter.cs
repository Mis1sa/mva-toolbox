using System.Collections.Generic;
using MVA.Toolbox.SwitchGenerator.Utils;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.SwitchGenerator.Emit.Animator
{
    internal static class AnimatorLayerEmitter
    {
        private const HideFlags ControllerSubAssetHideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;

        public static void Upsert(AnimatorController controller, Compile.SwitchLayerPlan layer, GameObject avatarRoot)
        {
            if (controller == null || layer == null || avatarRoot == null)
            {
                return;
            }

            bool wd = Compile.WriteDefaultsPolicyResolver.Resolve(layer.writeDefaults, controller);
            string folder = AvatarAssetResolver.BuildLayerFolder(layer.clipSaveRoot, layer.layerName);

            switch (layer.switchType)
            {
                case SwitchGeneratorConfig.SwitchType.Bool:
                    BuildBool(controller, layer, avatarRoot, wd, folder);
                    break;
                case SwitchGeneratorConfig.SwitchType.Int:
                    BuildInt(controller, layer, avatarRoot, false, folder);
                    break;
                case SwitchGeneratorConfig.SwitchType.Float:
                    BuildFloat(controller, layer, avatarRoot, wd, folder);
                    break;
            }
        }

        private static void BuildBool(AnimatorController controller, Compile.SwitchLayerPlan layer, GameObject avatarRoot, bool wd, string folder)
        {
            var offClip = AnimatorClipEmitter.CreateBool(layer, avatarRoot, false);
            var onClip = AnimatorClipEmitter.CreateBool(layer, avatarRoot, true);

            Motion off = AnimatorClipEmitter.SaveIfNeeded(offClip, folder, layer.persistAssets);
            Motion on = AnimatorClipEmitter.SaveIfNeeded(onClip, folder, layer.persistAssets);

            var machine = new AnimatorStateMachine { name = layer.layerName + "_SM" };
            AssetDatabase.AddObjectToAsset(machine, controller);
            SetSubAssetHideFlags(machine);

            var offState = machine.AddState("Off", new Vector3(280f, 120f, 0f));
            offState.motion = off;
            offState.writeDefaultValues = wd;

            var onState = machine.AddState("On", new Vector3(520f, 120f, 0f));
            onState.motion = on;
            onState.writeDefaultValues = wd;

            machine.defaultState = layer.defaultBoolValue == 1 ? onState : offState;

            var toOn = offState.AddTransition(onState);
            toOn.hasExitTime = false;
            toOn.duration = 0f;
            toOn.AddCondition(AnimatorConditionMode.If, 0f, layer.parameterName);

            var toOff = onState.AddTransition(offState);
            toOff.hasExitTime = false;
            toOff.duration = 0f;
            toOff.AddCondition(AnimatorConditionMode.IfNot, 0f, layer.parameterName);

            ApplyHideFlagsRecursive(machine);
            ApplyLayer(controller, layer.layerName, machine, layer.overwriteLayer);
        }

        private static void BuildInt(AnimatorController controller, Compile.SwitchLayerPlan layer, GameObject avatarRoot, bool wd, string folder)
        {
            var machine = new AnimatorStateMachine { name = layer.layerName + "_SM" };
            AssetDatabase.AddObjectToAsset(machine, controller);
            SetSubAssetHideFlags(machine);

            var states = new List<AnimatorState>();
            for (int i = 0; i < layer.intGroups.Count; i++)
            {
                var group = layer.intGroups[i];
                string stateName = !string.IsNullOrWhiteSpace(group?.stateName) ? group.stateName : layer.layerName + "_" + i;
                var clip = AnimatorClipEmitter.CreateInt(layer, group, avatarRoot, stateName);
                Motion motion = AnimatorClipEmitter.SaveIfNeeded(clip, folder, layer.persistAssets);

                var state = machine.AddState(stateName, new Vector3(280f + i * 220f, 120f, 0f));
                state.motion = motion;
                state.writeDefaultValues = wd;
                states.Add(state);
            }

            if (states.Count > 0)
            {
                int index = Mathf.Clamp(layer.defaultIntValue, 0, states.Count - 1);
                machine.defaultState = states[index];
            }

            for (int i = 0; i < states.Count; i++)
            {
                var transition = machine.AddAnyStateTransition(states[i]);
                transition.hasExitTime = false;
                transition.duration = 0f;
                transition.AddCondition(AnimatorConditionMode.Equals, i, layer.parameterName);
            }

            ApplyHideFlagsRecursive(machine);
            ApplyLayer(controller, layer.layerName, machine, layer.overwriteLayer);
        }

        private static void BuildFloat(AnimatorController controller, Compile.SwitchLayerPlan layer, GameObject avatarRoot, bool wd, string folder)
        {
            var clip = AnimatorClipEmitter.CreateFloat(layer, avatarRoot);
            Motion motion = AnimatorClipEmitter.SaveIfNeeded(clip, folder, layer.persistAssets);

            var machine = new AnimatorStateMachine { name = layer.layerName + "_SM" };
            AssetDatabase.AddObjectToAsset(machine, controller);
            SetSubAssetHideFlags(machine);

            var state = machine.AddState("Blend", new Vector3(360f, 120f, 0f));
            state.motion = motion;
            state.timeParameter = layer.parameterName;
            state.timeParameterActive = true;
            state.writeDefaultValues = wd;
            machine.defaultState = state;

            ApplyHideFlagsRecursive(machine);
            ApplyLayer(controller, layer.layerName, machine, layer.overwriteLayer);
        }

        private static void ApplyLayer(AnimatorController controller, string layerName, AnimatorStateMachine machine, bool overwrite)
        {
            var layers = new List<AnimatorControllerLayer>(controller.layers);
            int index = layers.FindIndex(l => l.name == layerName);

            var newLayer = new AnimatorControllerLayer
            {
                name = layerName,
                stateMachine = machine,
                defaultWeight = 1f,
                blendingMode = AnimatorLayerBlendingMode.Override,
                iKPass = false,
                syncedLayerAffectsTiming = false,
                syncedLayerIndex = -1,
                avatarMask = null
            };

            if (index >= 0)
            {
                if (overwrite)
                {
                    layers[index] = newLayer;
                }
                else
                {
                    layers.Add(newLayer);
                }
            }
            else
            {
                layers.Add(newLayer);
            }

            controller.layers = layers.ToArray();
            EditorUtility.SetDirty(controller);
        }

        private static void ApplyHideFlagsRecursive(AnimatorStateMachine machine)
        {
            if (machine == null)
            {
                return;
            }

            SetSubAssetHideFlags(machine);

            if (machine.states != null)
            {
                for (int i = 0; i < machine.states.Length; i++)
                {
                    var state = machine.states[i].state;
                    if (state == null)
                    {
                        continue;
                    }

                    SetSubAssetHideFlags(state);
                    if (state.transitions != null)
                    {
                        for (int j = 0; j < state.transitions.Length; j++)
                        {
                            SetSubAssetHideFlags(state.transitions[j]);
                        }
                    }
                }
            }

            if (machine.anyStateTransitions != null)
            {
                for (int i = 0; i < machine.anyStateTransitions.Length; i++)
                {
                    SetSubAssetHideFlags(machine.anyStateTransitions[i]);
                }
            }

            if (machine.entryTransitions != null)
            {
                for (int i = 0; i < machine.entryTransitions.Length; i++)
                {
                    SetSubAssetHideFlags(machine.entryTransitions[i]);
                }
            }

            if (machine.stateMachines != null)
            {
                for (int i = 0; i < machine.stateMachines.Length; i++)
                {
                    ApplyHideFlagsRecursive(machine.stateMachines[i].stateMachine);
                }
            }
        }

        private static void SetSubAssetHideFlags(Object asset)
        {
            if (asset == null)
            {
                return;
            }

            asset.hideFlags = ControllerSubAssetHideFlags;
            EditorUtility.SetDirty(asset);
        }
    }
}
