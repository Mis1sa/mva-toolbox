using MVA.Toolbox.SwitchGenerator.Compile;
using MVA.Toolbox.SwitchGenerator.Emit.Animator;
using MVA.Toolbox.SwitchGenerator.Emit.Vrc;
using MVA.Toolbox.SwitchGenerator.Spec;
using MVA.Toolbox.SwitchGenerator.Utils;
using UnityEditor;

namespace MVA.Toolbox.SwitchGenerator.Workflows
{
    internal static class ApplyToAvatarWorkflow
    {
        public static bool Execute(SwitchGeneratorSpec spec, out string message)
        {
            message = string.Empty;

            SwitchGeneratorSpecNormalizer.Normalize(spec);
            if (!SwitchGeneratorSpecValidator.Validate(spec, out message))
            {
                return false;
            }

            var avatar = spec.avatar;
            var fx = AvatarAssetResolver.GetOrCreateFxController(avatar);
            var expressionParameters = AvatarAssetResolver.GetOrCreateExpressionParameters(avatar);
            var expressionsMenu = AvatarAssetResolver.GetOrCreateExpressionsMenu(avatar);

            if (fx == null || expressionParameters == null || expressionsMenu == null)
            {
                message = "无法准备 Avatar 资源。";
                return false;
            }

            var plan = SwitchPlanCompiler.Compile(spec, fx, expressionParameters, true);
            using (var tx = new ApplyTransaction("Switch Generator Apply"))
            {
                try
                {
                    for (int i = 0; i < plan.layers.Count; i++)
                    {
                        var layer = plan.layers[i];
                        AnimatorParameterEmitter.Upsert(fx, layer);
                        AnimatorLayerEmitter.Upsert(fx, layer, avatar.gameObject);

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

                    EditorUtility.SetDirty(fx);
                    EditorUtility.SetDirty(expressionParameters);
                    EditorUtility.SetDirty(expressionsMenu);
                    AssetDatabase.SaveAssets();
                    tx.Complete();

                    message = "应用完成。";
                    return true;
                }
                catch (System.Exception ex)
                {
                    message = "应用失败：" + ex.Message;
                    return false;
                }
            }
        }
    }
}
