using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using MVA.Toolbox.Public;
using TargetItem = MVA.Toolbox.AvatarQuickToggle.ToggleConfig.TargetItem;
using IntStateGroup = MVA.Toolbox.AvatarQuickToggle.ToggleConfig.IntStateGroup;

namespace MVA.Toolbox.AvatarQuickToggle.Services
{
    public class AnimationService
    {
        public void CreateLayer(AnimatorController controller, MVA.Toolbox.AvatarQuickToggle.ToggleConfig config, int? insertIndex = null)
        {
            if (controller == null || config?.config == null) return;
            var data = config.config;
            var root = config.avatar != null ? config.avatar.gameObject : null;
            if (root == null) return;

            // 解析 Write Defaults 设置：Auto(0) 时根据现有 FX 层推导为 On(1)/Off(2)
            int writeDefaults = data.writeDefaultSetting;
            if (writeDefaults == 0)
            {
                writeDefaults = ResolveAutoWriteDefaults(controller);
            }

            var layers = controller.layers.ToList();
            int existingIndex = layers.FindIndex(l => l.name == data.layerName);
            if (existingIndex >= 0)
            {
                if (data.overwriteLayer)
                {
                    // 覆盖层级：删除同名层，并优先在原位置插入新层
                    layers.RemoveAt(existingIndex);
                    if (!insertIndex.HasValue)
                    {
                        insertIndex = existingIndex;
                    }
                }
                else
                {
                    // 不覆盖层级：为当前层生成唯一名称，采用 [层级名]_[序号] 规则（Cloth_1, Cloth_2 ...）
                    var existingNames = new HashSet<string>(layers.Select(l => l.name));
                    string baseName = data.layerName;
                    if (existingNames.Contains(baseName))
                    {
                        int suffix = 1;
                        string candidate;
                        do
                        {
                            candidate = $"{baseName}_{suffix}";
                            suffix++;
                        } while (existingNames.Contains(candidate));

                        data.layerName = candidate;
                    }
                }
            }

            EnsureAnimatorParameter(controller, data);

            AnimatorControllerLayer layer = null;
            switch (data.layerType)
            {
                case 0:
                    layer = CreateBoolLayer(controller, data, root, writeDefaults);
                    break;
                case 1:
                    layer = CreateIntLayer(controller, data, root, writeDefaults);
                    break;
                case 2:
                    layer = CreateFloatLayer(controller, data, root, writeDefaults);
                    break;
            }

            if (layer == null) return;
            layer.defaultWeight = 1f;

            if (insertIndex.HasValue && insertIndex.Value >= 0 && insertIndex.Value <= layers.Count)
            {
                layers.Insert(insertIndex.Value, layer);
            }
            else
            {
                layers.Add(layer);
            }

            controller.layers = layers.ToArray();
        }

        private void EnsureAnimatorParameter(AnimatorController controller, MVA.Toolbox.AvatarQuickToggle.ToggleConfig.LayerConfig data)
        {
            if (controller == null || data == null || string.IsNullOrEmpty(data.parameterName)) return;

            var desiredType = AnimatorControllerParameterType.Bool;
            switch (data.layerType)
            {
                case 0:
                    desiredType = AnimatorControllerParameterType.Bool;
                    break;
                case 1:
                    desiredType = AnimatorControllerParameterType.Int;
                    break;
                case 2:
                    desiredType = AnimatorControllerParameterType.Float;
                    break;
            }

            // 查找 AnimatorController 中是否已存在同名参数
            var parameters = controller.parameters ?? System.Array.Empty<AnimatorControllerParameter>();
            int index = -1;
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                if (p != null && p.name == data.parameterName)
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
            {
                // 已存在同名参数：未勾选覆盖参数则保持原设置；勾选覆盖参数则同步类型和默认值
                if (!data.overwriteParameter)
                {
                    return;
                }

                var existing = parameters[index];
                existing.type = desiredType;
                switch (desiredType)
                {
                    case AnimatorControllerParameterType.Bool:
                        // Bool 默认值：0=OFF(false)，1=ON(true)
                        existing.defaultBool = data.defaultStateSelection == 1;
                        break;
                    case AnimatorControllerParameterType.Int:
                        existing.defaultInt = Mathf.Max(0, data.defaultIntValue);
                        break;
                    case AnimatorControllerParameterType.Float:
                        existing.defaultFloat = Mathf.Clamp01(data.defaultFloatValue);
                        break;
                }

                // 将修改写回控制器参数数组
                parameters[index] = existing;
                controller.parameters = parameters;
                return;
            }

            // 不存在同名参数时，创建新参数并设置默认值
            var parameter = new AnimatorControllerParameter
            {
                name = data.parameterName,
                type = desiredType
            };

            switch (desiredType)
            {
                case AnimatorControllerParameterType.Bool:
                    // Bool：0=OFF(false)，1=ON(true)
                    parameter.defaultBool = data.defaultStateSelection == 1;
                    break;
                case AnimatorControllerParameterType.Int:
                    parameter.defaultInt = Mathf.Max(0, data.defaultIntValue);
                    break;
                case AnimatorControllerParameterType.Float:
                    parameter.defaultFloat = Mathf.Clamp01(data.defaultFloatValue);
                    break;
            }

            controller.AddParameter(parameter);
        }

        private AnimatorControllerLayer CreateBoolLayer(AnimatorController controller, MVA.Toolbox.AvatarQuickToggle.ToggleConfig.LayerConfig data, GameObject root, int writeDefaults)
        {
            var stateMachine = new AnimatorStateMachine { name = data.layerName + "_SM" };

            var offState = stateMachine.AddState("Off");
            var onState = stateMachine.AddState("On");
            stateMachine.defaultState = offState;

            offState.motion = CreateBoolAnimationClip(data, root, false);
            onState.motion = CreateBoolAnimationClip(data, root, true);

            SetWriteDefaults(offState, writeDefaults);
            SetWriteDefaults(onState, writeDefaults);

            var toOn = offState.AddTransition(onState);
            toOn.hasExitTime = false;
            toOn.duration = 0f;
            toOn.AddCondition(AnimatorConditionMode.If, 0f, data.parameterName);

            var toOff = onState.AddTransition(offState);
            toOff.hasExitTime = false;
            toOff.duration = 0f;
            toOff.AddCondition(AnimatorConditionMode.IfNot, 0f, data.parameterName);

            PersistStateMachineHierarchy(stateMachine, controller);

            var layer = new AnimatorControllerLayer
            {
                name = data.layerName,
                stateMachine = stateMachine
            };

            return layer;
        }

        private AnimatorControllerLayer CreateIntLayer(AnimatorController controller, MVA.Toolbox.AvatarQuickToggle.ToggleConfig.LayerConfig data, GameObject root, int writeDefaults)
        {
            var stateMachine = new AnimatorStateMachine { name = data.layerName + "_SM" };

            var states = new List<AnimatorState>();
            for (int i = 0; i < data.intGroups.Count; i++)
            {
                var group = data.intGroups[i];
                var stateName = string.IsNullOrEmpty(group?.stateName) ? $"State {i}" : group.stateName;
                var state = stateMachine.AddState(stateName);

                // Int 动画剪辑名称：优先使用菜单项名称；为空时使用 [层级名或参数名]_[索引]
                string clipName = null;
                if (data.intMenuItemNames != null && i < data.intMenuItemNames.Count)
                {
                    clipName = data.intMenuItemNames[i];
                }
                if (string.IsNullOrWhiteSpace(clipName))
                {
                    string baseName = !string.IsNullOrWhiteSpace(data.layerName) ? data.layerName : data.parameterName;
                    clipName = $"{baseName}_{i}";
                }

                state.motion = CreateIntAnimationClip(data, group, root, clipName);
                SetWriteDefaults(state, writeDefaults);
                states.Add(state);
            }

            if (states.Count > 0)
            {
                stateMachine.defaultState = states[Mathf.Clamp(data.defaultIntValue, 0, states.Count - 1)];
            }

            for (int i = 0; i < states.Count; i++)
            {
                var transition = stateMachine.AddAnyStateTransition(states[i]);
                transition.hasExitTime = false;
                transition.duration = 0f;
                transition.AddCondition(AnimatorConditionMode.Equals, i, data.parameterName);
            }

            PersistStateMachineHierarchy(stateMachine, controller);

            return new AnimatorControllerLayer
            {
                name = data.layerName,
                stateMachine = stateMachine
            };
        }

        private AnimatorControllerLayer CreateFloatLayer(AnimatorController controller, MVA.Toolbox.AvatarQuickToggle.ToggleConfig.LayerConfig data, GameObject root, int writeDefaults)
        {
            var stateMachine = new AnimatorStateMachine { name = data.layerName + "_SM" };

            // 先创建动画剪辑，再使用剪辑名称作为状态名
            // Float 动画剪辑名称：与层级名保持一致（不再附加 "_Float" 后缀）
            var clip = CreateFloatAnimationClip(data, root, data.layerName);
            var state = stateMachine.AddState(clip != null ? clip.name : "Blend");
            state.motion = clip;

            // 启用 Motion Time，并使用当前层参数名驱动 0-1 范围的归一化时间
#if UNITY_2019_1_OR_NEWER
            state.timeParameterActive = true;
            state.timeParameter = data.parameterName;
#endif
            SetWriteDefaults(state, data.writeDefaultSetting);
            stateMachine.defaultState = state;

            PersistStateMachineHierarchy(stateMachine, controller);

            return new AnimatorControllerLayer
            {
                name = data.layerName,
                stateMachine = stateMachine
            };
        }

        private AnimationClip CreateBoolAnimationClip(MVA.Toolbox.AvatarQuickToggle.ToggleConfig.LayerConfig data, GameObject root, bool isOnState)
        {
            var clip = CreateBoolAnimationClipInMemory(data, root, isOnState);
            if (clip == null) return null;
            // NDMF 工作流下，使用临时路径 "Assets/__AQT_NDMF__/..."：仅在内存中使用，不写入磁盘
            if (!string.IsNullOrEmpty(data.clipSavePath) && data.clipSavePath.StartsWith("Assets/__AQT_NDMF__/"))
            {
                return clip;
            }

            // 普通模式：将剪辑保存到指定路径（目录由 ToolboxUtils 构建，定义于 ToolboxUtils.cs）
            string folderPath = ToolboxUtils.BuildAqtLayerFolder(data.clipSavePath, data.layerName);
            return SaveAnimationClip(clip, folderPath);
        }

        private AnimationClip CreateBoolAnimationClipInMemory(MVA.Toolbox.AvatarQuickToggle.ToggleConfig.LayerConfig data, GameObject root, bool isOnState)
        {
            var clip = new AnimationClip
            {
                // Bool 动画剪辑名称：使用层级名 + "_ON" / "_OFF"
                name = $"{data.layerName}_{(isOnState ? "ON" : "OFF")}" 
            };

            foreach (var item in data.boolTargets)
            {
                if (item?.targetObject == null) continue;
                string path = ToolboxUtils.GetGameObjectPath(item.targetObject, root);
                if (item.controlType == 0)
                {
                    bool activeWhenOn = item.onStateActiveSelection == 0;
                    float value = (isOnState == activeWhenOn) ? 1f : 0f;
                    AddGameObjectCurve(clip, path, value > 0.5f);
                }
                else if (item.controlType == 1 && !string.IsNullOrEmpty(item.blendShapeName))
                {
                    // BlendShape 曲线直接绑定到目标物体路径，由 AAO 在构建阶段处理合并/重命名
                    bool isZeroWhenOn = item.onStateBlendShapeValue == 0;
                    float onValue = isZeroWhenOn ? 0f : 100f;
                    float targetValue = isOnState ? onValue : 100f - onValue;
                    AddBlendShapeCurve(clip, path, item.blendShapeName, targetValue);
                }
            }

            return clip;
        }

        // 提供给 NDMF 工作流：仅在内存中创建 Bool 动画剪辑，不保存到磁盘
        public AnimationClip CreateBoolAnimationClipForNDMF(MVA.Toolbox.AvatarQuickToggle.ToggleConfig.LayerConfig data, GameObject root, bool isOnState)
        {
            return CreateBoolAnimationClipInMemory(data, root, isOnState);
        }

        private AnimationClip CreateIntAnimationClip(MVA.Toolbox.AvatarQuickToggle.ToggleConfig.LayerConfig data, IntStateGroup group, GameObject root, string clipName)
        {
            var clip = CreateIntAnimationClipInMemory(data, group, root, clipName);
            if (clip == null) return null;

            // NDMF 工作流下使用临时路径：不创建/保存磁盘资源
            if (!string.IsNullOrEmpty(data.clipSavePath) && data.clipSavePath.StartsWith("Assets/__AQT_NDMF__/"))
            {
                return clip;
            }

            // 普通模式：保存到指定路径（目录由 ToolboxUtils 构建，定义于 ToolboxUtils.cs）
            string folderPath = ToolboxUtils.BuildAqtLayerFolder(data.clipSavePath, data.layerName);
            return SaveAnimationClip(clip, folderPath);
        }

        private AnimationClip CreateIntAnimationClipInMemory(MVA.Toolbox.AvatarQuickToggle.ToggleConfig.LayerConfig data, IntStateGroup group, GameObject root, string clipName)
        {
            var clip = new AnimationClip
            {
                // Int 动画剪辑名称：使用传入 clipName（由菜单名或默认规则生成）
                name = clipName
            };

            if (group?.targetItems != null)
            {
                foreach (var item in group.targetItems)
                {
                    if (item?.targetObject == null) continue;
                    string path = ToolboxUtils.GetGameObjectPath(item.targetObject, root);
                    if (item.controlType == 0)
                    {
                        bool activeWhenOn = item.onStateActiveSelection == 0;
                        AddGameObjectCurve(clip, path, activeWhenOn);
                    }
                    else if (item.controlType == 1 && !string.IsNullOrEmpty(item.blendShapeName))
                    {
                        // Int 模式同样将曲线绑定到目标物体路径，由 AAO 在构建时处理合并/重命名
                        bool isZero = item.onStateBlendShapeValue == 0;
                        float value = isZero ? 0f : 100f;
                        AddBlendShapeCurve(clip, path, item.blendShapeName, value);
                    }
                }
            }

            return clip;
        }

        private AnimationClip CreateFloatAnimationClip(MVA.Toolbox.AvatarQuickToggle.ToggleConfig.LayerConfig data, GameObject root, string clipName)
        {
            var clip = CreateFloatAnimationClipInMemory(data, root, clipName);
            if (clip == null) return null;

            // NDMF 工作流下使用临时路径：不创建/保存磁盘资源
            if (!string.IsNullOrEmpty(data.clipSavePath) && data.clipSavePath.StartsWith("Assets/__AQT_NDMF__/"))
            {
                return clip;
            }

            // 普通模式：保存到指定路径
            string folderPath = ToolboxUtils.BuildAqtLayerFolder(data.clipSavePath, data.layerName);
            return SaveAnimationClip(clip, folderPath);
        }

        private AnimationClip CreateFloatAnimationClipInMemory(MVA.Toolbox.AvatarQuickToggle.ToggleConfig.LayerConfig data, GameObject root, string clipName)
        {
            // Float 动画剪辑：使用 clipName 作为名称，时间轴为 0~1 归一化时间，由 Motion Time 控制
            var clip = new AnimationClip
            {
                name = clipName
            };
            clip.frameRate = 60f;

            foreach (var item in data.floatTargets)
            {
                if (item == null || item.targetObject == null) continue;
                // Float 只支持 BlendShape 目标，主次名称都为空时跳过
                bool hasPrimary = !string.IsNullOrEmpty(item.blendShapeName);
                bool hasSecondary = !string.IsNullOrEmpty(item.secondaryBlendShapeName);
                if (!hasPrimary && !hasSecondary) continue;

                // 曲线直接绑定到目标物体路径，由 AAO 在构建阶段根据名称处理合并/重命名
                string path = ToolboxUtils.GetGameObjectPath(item.targetObject, root);

                // 未开启二分模式：始终使用单一形变曲线（优先使用主名称，其次使用副名称）
                if (!item.splitBlendShape)
                {
                    string shape = hasPrimary ? item.blendShapeName : item.secondaryBlendShapeName;
                    int dir = hasPrimary ? item.onStateBlendShapeValue : item.secondaryBlendShapeValue;
                    if (string.IsNullOrEmpty(shape)) continue;

                    var binding = new EditorCurveBinding
                    {
                        path = path,
                        type = typeof(SkinnedMeshRenderer),
                        propertyName = "blendShape." + shape
                    };

                    // 两点曲线：0 -> 1，对应 SSG 中 0 -> endTime 的 EvaluateFloatWeight(dir, t)
                    var curve = new AnimationCurve(
                        new Keyframe(0f, EvaluateFloatWeight(dir, 0f)),
                        new Keyframe(1f, EvaluateFloatWeight(dir, 1f))
                    );
                    SetLinearTangents(curve);
                    AnimationUtility.SetEditorCurve(clip, binding, curve);
                }
                else
                {
                    // 二分模式：按主/次三点曲线处理，即便次名称为空也仍视为二分模式
                    float halfTime = 0.5f; // 归一化时间 0.5 对应 SSG 的 halfTime

                    if (hasPrimary)
                    {
                        var primaryBinding = new EditorCurveBinding
                        {
                            path = path,
                            type = typeof(SkinnedMeshRenderer),
                            propertyName = "blendShape." + item.blendShapeName
                        };
                        var primaryCurve = new AnimationCurve(
                            new Keyframe(0f, EvaluateFloatWeight(item.onStateBlendShapeValue, 0f)),
                            new Keyframe(halfTime, EvaluateFloatWeight(item.onStateBlendShapeValue, 1f)),
                            new Keyframe(1f, EvaluateFloatWeight(item.onStateBlendShapeValue, 1f))
                        );
                        SetLinearTangents(primaryCurve);
                        AnimationUtility.SetEditorCurve(clip, primaryBinding, primaryCurve);
                    }

                    if (hasSecondary)
                    {
                        var secondaryBinding = new EditorCurveBinding
                        {
                            path = path,
                            type = typeof(SkinnedMeshRenderer),
                            propertyName = "blendShape." + item.secondaryBlendShapeName
                        };
                        var secondaryCurve = new AnimationCurve(
                            new Keyframe(0f, EvaluateFloatWeight(item.secondaryBlendShapeValue, 0f)),
                            new Keyframe(halfTime, EvaluateFloatWeight(item.secondaryBlendShapeValue, 0f)),
                            new Keyframe(1f, EvaluateFloatWeight(item.secondaryBlendShapeValue, 1f))
                        );
                        SetLinearTangents(secondaryCurve);
                        AnimationUtility.SetEditorCurve(clip, secondaryBinding, secondaryCurve);
                    }
                }
            }

            return clip;
        }

        // 评估 Float 模式下给定方向（0: 0->100, 1: 100->0）在归一化时间 t 对应的权重
        private float EvaluateFloatWeight(int direction, float t)
        {
            t = Mathf.Clamp01(t);
            return (direction == 0 ? t : 1f - t) * 100f;
        }

        // 将曲线关键帧切线设为线性，避免插值缓入/缓出
        private void SetLinearTangents(AnimationCurve curve)
        {
            if (curve == null) return;

            var keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }

            keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                if (i > 0)
                {
                    var prev = keys[i - 1];
                    float dt = key.time - prev.time;
                    key.inTangent = dt != 0f ? (key.value - prev.value) / dt : 0f;
                }
                if (i < keys.Length - 1)
                {
                    var next = keys[i + 1];
                    float dt = next.time - key.time;
                    key.outTangent = dt != 0f ? (next.value - key.value) / dt : 0f;
                }
                keys[i] = key;
            }
            curve.keys = keys;
        }

        private void AddGameObjectCurve(AnimationClip clip, string path, bool isActive)
        {
            var curve = AnimationCurve.Constant(0f, 0f, isActive ? 1f : 0f);
            var binding = new EditorCurveBinding
            {
                path = path,
                propertyName = "m_IsActive",
                type = typeof(GameObject)
            };
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        private void AddBlendShapeCurve(AnimationClip clip, string path, string blendShapeName, float value)
        {
            var curve = AnimationCurve.Constant(0f, 0f, value);
            var binding = new EditorCurveBinding
            {
                path = path,
                type = typeof(SkinnedMeshRenderer),
                propertyName = "blendShape." + blendShapeName
            };
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        private void AddFloatBlendShapeCurve(AnimationClip clip, string path, string blendShapeName, int direction, bool secondary, float startTime, float endTime, bool useWDOn)
        {
            float startValue = direction == 0 ? 0f : 100f;
            float endValue = direction == 0 ? 100f : 0f;
            if (secondary)
            {
                startValue = direction == 0 ? 0f : 100f;
                endValue = direction == 0 ? 100f : 0f;
            }

            var curve = new AnimationCurve
            {
                keys = new[]
                {
                    new Keyframe(startTime, startValue),
                    new Keyframe(endTime, endValue)
                }
            };

            var binding = new EditorCurveBinding
            {
                path = path,
                type = typeof(SkinnedMeshRenderer),
                propertyName = "blendShape." + blendShapeName
            };
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        private void SetWriteDefaults(AnimatorState state, int writeDefaultSetting)
        {
            if (state == null) return;
            if (writeDefaultSetting == 0) return;

            bool writeDefaults = (writeDefaultSetting == 1);

            // 使用序列化方式设置 m_WriteDefaultValues 字段
            var so = new SerializedObject(state);
            var prop = so.FindProperty("m_WriteDefaultValues");
            if (prop != null)
            {
                prop.boolValue = writeDefaults;
                so.ApplyModifiedProperties();
            }
        }

        private int ResolveAutoWriteDefaults(AnimatorController controller)
        {
            if (controller == null || controller.layers == null || controller.layers.Length == 0)
                return 2; // 无有效层时视为 OFF

            bool anyLayerConsidered = false;
            bool allLayersAllOn = true;

            foreach (var layer in controller.layers)
            {
                if (layer == null || layer.stateMachine == null) continue;

                var states = CollectStatesRecursive(layer.stateMachine);
                if (states.Count <= 1) continue; // 0 或 1 个状态的层忽略

                anyLayerConsidered = true;

                foreach (var s in states)
                {
                    if (s == null) continue;
                    var so = new SerializedObject(s);
                    var prop = so.FindProperty("m_WriteDefaultValues");
                    bool isOn = prop != null && prop.boolValue;
                    if (!isOn)
                    {
                        allLayersAllOn = false;
                        break;
                    }
                }

                if (!allLayersAllOn)
                {
                    break;
                }
            }

            if (!anyLayerConsidered) return 2;      // 没有“有意义”的层 -> OFF
            if (allLayersAllOn) return 1;           // 所有被考虑的层全部 WD=ON -> ON
            return 2;                               // 其它情况 -> OFF
        }

        private List<AnimatorState> CollectStatesRecursive(AnimatorStateMachine sm)
        {
            var list = new List<AnimatorState>();
            if (sm == null) return list;

            foreach (var child in sm.states)
            {
                if (child.state != null) list.Add(child.state);
            }

            foreach (var sub in sm.stateMachines)
            {
                if (sub.stateMachine != null)
                {
                    list.AddRange(CollectStatesRecursive(sub.stateMachine));
                }
            }

            return list;
        }

        private AnimationClip SaveAnimationClip(AnimationClip clip, string folderPath)
        {
            if (clip == null || string.IsNullOrEmpty(folderPath)) return clip;
            // 使用 ToolboxUtils 确保目标文件夹存在（方法定义于 ToolboxUtils.cs）
            ToolboxUtils.EnsureFolderExists(folderPath);
            string fileName = ToolboxUtils.SanitizeAssetFileName(clip.name);
            string assetPath = $"{folderPath}/{fileName}.anim";

            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(clip, existing);
                AssetDatabase.SaveAssets();
                return existing;
            }

            AssetDatabase.CreateAsset(clip, assetPath);
            AssetDatabase.SaveAssets();
            var savedClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            return savedClip != null ? savedClip : clip;
        }

        private void PersistStateMachineHierarchy(AnimatorStateMachine stateMachine, AnimatorController controller)
        {
            if (stateMachine == null || controller == null) return;

            string controllerPath = AssetDatabase.GetAssetPath(controller);
            if (string.IsNullOrEmpty(controllerPath)) return;

            if (!AssetDatabase.Contains(stateMachine))
            {
                AssetDatabase.AddObjectToAsset(stateMachine, controller);
                SetSubAssetHideFlags(stateMachine);
            }

            PersistChildStates(stateMachine);
            PersistStateMachineTransitions(stateMachine);

            foreach (var child in stateMachine.stateMachines)
            {
                var childStateMachine = child.stateMachine;
                if (childStateMachine == null) continue;
                PersistStateMachineHierarchy(childStateMachine, controller);
            }
        }

        private void PersistChildStates(AnimatorStateMachine stateMachine)
        {
            if (stateMachine.states == null) return;

            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                if (state == null) continue;

                if (!AssetDatabase.Contains(state))
                {
                    AssetDatabase.AddObjectToAsset(state, stateMachine);
                    SetSubAssetHideFlags(state);
                }

                PersistStateTransitions(state, stateMachine);
            }
        }

        private void PersistStateTransitions(AnimatorState state, AnimatorStateMachine owner)
        {
            if (state?.transitions == null) return;
            foreach (var transition in state.transitions)
            {
                PersistTransitionAsset(transition, owner);
            }
        }

        private void PersistStateMachineTransitions(AnimatorStateMachine stateMachine)
        {
            if (stateMachine == null) return;

            if (stateMachine.anyStateTransitions != null)
            {
                foreach (var transition in stateMachine.anyStateTransitions)
                {
                    PersistTransitionAsset(transition, stateMachine);
                }
            }

            if (stateMachine.entryTransitions != null)
            {
                foreach (var transition in stateMachine.entryTransitions)
                {
                    PersistTransitionAsset(transition, stateMachine);
                }
            }

        }

        private void PersistTransitionAsset(AnimatorTransitionBase transition, UnityEngine.Object parent)
        {
            if (transition == null || parent == null) return;
            if (AssetDatabase.Contains(transition)) return;

            AssetDatabase.AddObjectToAsset(transition, parent);
            SetSubAssetHideFlags(transition);
        }

        private void SetSubAssetHideFlags(UnityEngine.Object obj)
        {
            if (obj == null) return;
            obj.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
            EditorUtility.SetDirty(obj);
        }
    }
}
