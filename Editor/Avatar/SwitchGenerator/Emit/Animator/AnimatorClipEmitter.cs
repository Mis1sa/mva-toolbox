using MVA.Toolbox.SwitchGenerator.Spec;
using MVA.Toolbox.SwitchGenerator.Utils;
using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.SwitchGenerator.Emit.Animator
{
    internal static class AnimatorClipEmitter
    {
        public static AnimationClip CreateBool(Compile.SwitchLayerPlan layer, GameObject avatarRoot, bool onState)
        {
            var clip = new AnimationClip
            {
                name = layer.layerName + (onState ? "_On" : "_Off")
            };

            for (int i = 0; i < layer.boolTargets.Count; i++)
            {
                var target = layer.boolTargets[i];
                if (target?.targetObject == null)
                {
                    continue;
                }

                string path = AvatarAssetResolver.GetRelativePath(target.targetObject, avatarRoot);
                if (target.controlType == SwitchGeneratorConfig.TargetControlType.GameObject)
                {
                    bool activeWhenOn = target.boolObjectState == SwitchGeneratorConfig.BoolObjectState.Active;
                    bool active = onState ? activeWhenOn : !activeWhenOn;
                    clip.SetCurve(path, typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 0f, active ? 1f : 0f));
                }
                else if (!string.IsNullOrWhiteSpace(target.blendShapeName))
                {
                    float on = target.boolBlendShapeState == SwitchGeneratorConfig.BoolBlendShapeState.Full ? 100f : 0f;
                    float value = onState ? on : 100f - on;
                    clip.SetCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + target.blendShapeName, AnimationCurve.Constant(0f, 0f, value));
                }
            }

            return clip;
        }

        public static AnimationClip CreateInt(Compile.SwitchLayerPlan layer, SwitchIntGroupSpec group, GameObject avatarRoot, string stateName)
        {
            var clip = new AnimationClip
            {
                name = stateName
            };

            if (group?.targets == null)
            {
                return clip;
            }

            for (int i = 0; i < group.targets.Count; i++)
            {
                var target = group.targets[i];
                if (target?.targetObject == null)
                {
                    continue;
                }

                string path = AvatarAssetResolver.GetRelativePath(target.targetObject, avatarRoot);
                if (target.controlType == SwitchGeneratorConfig.TargetControlType.GameObject)
                {
                    bool active = target.boolObjectState == SwitchGeneratorConfig.BoolObjectState.Active;
                    clip.SetCurve(path, typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 0f, active ? 1f : 0f));
                }
                else if (!string.IsNullOrWhiteSpace(target.blendShapeName))
                {
                    float value = target.boolBlendShapeState == SwitchGeneratorConfig.BoolBlendShapeState.Full ? 100f : 0f;
                    clip.SetCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + target.blendShapeName, AnimationCurve.Constant(0f, 0f, value));
                }
            }

            return clip;
        }

        public static AnimationClip CreateFloat(Compile.SwitchLayerPlan layer, GameObject avatarRoot)
        {
            var clip = new AnimationClip
            {
                name = layer.layerName + "_Float",
                frameRate = 60f
            };

            for (int i = 0; i < layer.floatTargets.Count; i++)
            {
                var target = layer.floatTargets[i];
                if (target?.targetObject == null)
                {
                    continue;
                }

                string path = AvatarAssetResolver.GetRelativePath(target.targetObject, avatarRoot);
                if (!string.IsNullOrWhiteSpace(target.blendShapeName))
                {
                    clip.SetCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + target.blendShapeName, BuildFloatCurve(target.floatDirection));
                }

                if (target.splitBlendShape && !string.IsNullOrWhiteSpace(target.secondaryBlendShapeName))
                {
                    clip.SetCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + target.secondaryBlendShapeName, BuildFloatCurve(target.secondaryFloatDirection));
                }
            }

            return clip;
        }

        public static Motion SaveIfNeeded(AnimationClip clip, string folder, bool persistAssets)
        {
            if (clip == null)
            {
                return null;
            }

            if (!persistAssets)
            {
                return clip;
            }

            AvatarAssetResolver.EnsureFolder(folder);
            string safe = AvatarAssetResolver.SanitizeFileName(clip.name, "SwitchClip");
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + safe + ".anim");
            AssetDatabase.CreateAsset(clip, path);
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        }

        private static AnimationCurve BuildFloatCurve(SwitchGeneratorConfig.FloatDirection direction)
        {
            if (direction == SwitchGeneratorConfig.FloatDirection.FullToZero)
            {
                return new AnimationCurve(
                    new Keyframe(0f, 100f),
                    new Keyframe(1f, 0f));
            }

            return new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(1f, 100f));
        }
    }
}
