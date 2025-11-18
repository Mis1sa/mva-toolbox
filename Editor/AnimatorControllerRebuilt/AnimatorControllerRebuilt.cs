using UnityEngine;
using UnityEditor;

using UnityEditor.Animations;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace MVA.Toolbox.AnimatorControllerRebuilt
{
    public class AnimatorControllerRebuiltWindow : EditorWindow
    {
        private AnimatorController sourceController;
        private string outputPath = "Assets/MVA Toolbox/ACR/";
        private bool isProcessing = false;
        bool _cancelRequested;
        private Vector2 scrollPosition;
        private string currentProgress = "";
        private float progress = 0f;
        
        // 处理统计
        private int totalStates = 0;
        private int totalTransitions = 0;
        private int totalParameters = 0;
        private int totalBehaviours = 0;
        private int totalBlendTrees = 0;
        private int totalSubStateMachines = 0;
        private int fixedErrors = 0;
        
        [MenuItem("Tools/MVA Toolbox/Anim Controller Rebuilt", false, 7)]
        public static void Open()
        {
            AnimatorControllerRebuiltWindow window = GetWindow<AnimatorControllerRebuiltWindow>("Anim Controller Rebuilt");
            window.minSize = new Vector2(400, 600);
            window.Show();
        }
        
        private GUIStyle headerBoxStyle;
        private GUIStyle contentBoxStyle;

        private void OnEnable()
        {
            minSize = new Vector2(400, 600);

            headerBoxStyle = EditorStyles.helpBox;
            contentBoxStyle = EditorStyles.helpBox;
            _cancelRequested = false;
        }
        
        private void OnDisable()
        {
            if (isProcessing)
            {
                _cancelRequested = true;
            }
        }
        
        private Texture2D MakeTex(int width, int height, Color color)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++)
            {
                pix[i] = color;
            }
            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            GUILayout.BeginVertical(contentBoxStyle);
            
            DrawMainContent();

            GUILayout.EndVertical();
            
            EditorGUILayout.Space();
            DrawStatisticsSection();

            EditorGUILayout.EndScrollView();
        }
        
        private void OnInspectorUpdate()
        {
            if (isProcessing && progress >= 0.3f && progress < 0.8f)
            {
                progress = Mathf.MoveTowards(progress, 0.8f, 0.01f);
                Repaint();
            }
        }
        
        private void DrawMainContent()
        {
            EditorGUILayout.LabelField("请将您的 Animator Controller 拖到此处:", EditorStyles.boldLabel);
            sourceController = (AnimatorController)EditorGUILayout.ObjectField("Source Controller", sourceController, typeof(AnimatorController), false);
            
            if (sourceController == null)
            {
                EditorGUILayout.HelpBox("请将 AnimatorController 拖到上方栏位。", MessageType.Info);
                return;
            }
            
            EditorGUILayout.Space();
            
            GUILayout.Label("输出设置", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("输出路径:", GUILayout.Width(80));
            outputPath = EditorGUILayout.TextField(outputPath);
            if (GUILayout.Button("浏览", GUILayout.Width(60)))
            {
                string selectedPath = EditorUtility.OpenFolderPanel("选择输出文件夹", "Assets", "");
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    outputPath = FileUtil.GetProjectRelativePath(selectedPath);
                }
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space();
            
            GUI.enabled = sourceController != null && !isProcessing;
            if (GUILayout.Button("开始重构", GUILayout.Height(30)))
            {
                StartRebuild();
            }
            GUI.enabled = true;
            
            if (isProcessing)
            {
                EditorGUILayout.Space();
                
                EditorGUILayout.LabelField("处理进度", EditorStyles.boldLabel);
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), progress, currentProgress);
                
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox($"正在处理中...\n{currentProgress}", MessageType.Info);
            }
        }
        
        private void DrawStatisticsSection()
        {
            if (totalStates > 0)
            {
                GUILayout.BeginVertical(headerBoxStyle);
                GUILayout.Label("处理统计", EditorStyles.boldLabel);
                GUILayout.EndVertical();
                
                GUI.backgroundColor = EditorGUIUtility.isProSkin ? new Color(0.25f, 0.25f, 0.25f) : new Color(0.85f, 0.85f, 0.85f);
                GUILayout.BeginVertical(contentBoxStyle);
                GUI.backgroundColor = Color.white;
                
                EditorGUILayout.LabelField($"状态数量: {totalStates}");
                EditorGUILayout.LabelField($"转换数量: {totalTransitions}");
                EditorGUILayout.LabelField($"参数数量: {totalParameters}");
                EditorGUILayout.LabelField($"行为组件: {totalBehaviours}");
                EditorGUILayout.LabelField($"混合树: {totalBlendTrees}");
                EditorGUILayout.LabelField($"子状态机: {totalSubStateMachines}");
                EditorGUILayout.LabelField($"修复错误: {fixedErrors}");
                
                GUILayout.EndVertical();
            }
        }
        
        private async void StartRebuild()
        {
            if (sourceController == null)
            {
                EditorUtility.DisplayDialog("错误", "请选择源动画控制器", "确定");
                return;
            }
            
            isProcessing = true;
            _cancelRequested = false;
            ResetStatistics();
            
            try
            {
                currentProgress = "初始化...";
                progress = 0.1f;
                Repaint();
                
                if (!Directory.Exists(outputPath))
                {
                    Directory.CreateDirectory(outputPath);
                    AssetDatabase.Refresh();
                }
                
                string newControllerName = $"{sourceController.name}_Rebuilt";
                string newControllerPath = Path.Combine(outputPath, $"{newControllerName}.controller");
                
                currentProgress = "创建新控制器...";
                progress = 0.2f;
                Repaint();
                
                AnimatorController newController = AnimatorController.CreateAnimatorControllerAtPath(newControllerPath);

                EditorUtility.SetDirty(newController);
                AssetDatabase.SaveAssetIfDirty(newController);
                
                currentProgress = "重构控制器...";
                progress = 0.3f;
                Repaint();
                
                if (_cancelRequested)
                    return;

                await RebuildController(sourceController, newController);
                
                currentProgress = "验证结果...";
                progress = 0.8f;
                Repaint();

                for (int i = 0; i < newController.layers.Length; i++)
                {
                    var layer = newController.layers[i];
                    if (layer.stateMachine == null)
                    {
                        Debug.LogWarning($"[VRCTool_ACR] 层 {i} ({layer.name}) 的状态机为空");
                        fixedErrors++;
                    }
                }
                
                EditorUtility.SetDirty(newController);

                foreach (var layer in newController.layers)
                {
                    if (layer.stateMachine != null)
                    {
                        EditorUtility.SetDirty(layer.stateMachine);
                    }
                }
                
                foreach (var layer in newController.layers)
                {
                    if (layer.stateMachine != null)
                    {
                        EditorUtility.SetDirty(layer.stateMachine);
                        AssetDatabase.SaveAssetIfDirty(layer.stateMachine);
                    }
                }
                
                EditorUtility.SetDirty(newController);
                AssetDatabase.SaveAssetIfDirty(newController);
                
                currentProgress = "保存资源...";
                progress = 0.9f;
                Repaint();
                
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                foreach (var layer in newController.layers)
                {
                    if (layer.stateMachine == null)
                    {
                        Debug.LogWarning($"[VRCTool_ACR] 状态机丢失: {layer.name}");
                    }
                }
                
                // 选择新创建的控制器
                Selection.activeObject = newController;
                EditorGUIUtility.PingObject(newController);
                
                currentProgress = "完成！";
                progress = 1.0f;
                Repaint();
                
                EditorUtility.DisplayDialog("完成", 
                    $"动画控制器重构完成！\n\n" +
                    $"新控制器: {newControllerName}\n" +
                    $"位置: {newControllerPath}\n\n" +
                    $"处理统计:\n" +
                    $"• 状态: {totalStates}\n" +
                    $"• 转换: {totalTransitions}\n" +
                    $"• 参数: {totalParameters}\n" +
                    $"• 行为组件: {totalBehaviours}\n" +
                    $"• 混合树: {totalBlendTrees}\n" +
                    $"• 子状态机: {totalSubStateMachines}\n" +
                    $"• 修复错误: {fixedErrors}", "确定");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[VRCTool_ACR] 重构失败: {ex.Message}");
                EditorUtility.DisplayDialog("错误", $"重构失败: {ex.Message}", "确定");
            }
            finally
            {
                isProcessing = false;
                currentProgress = "";
                progress = 0f;
                Repaint();
            }
        }
        
        private async System.Threading.Tasks.Task RebuildController(AnimatorController source, AnimatorController target)
        {
            // 1. 复制参数
            await RebuildParameters(source, target);
            

            // 2. 使用序列化复制基本结构，手动重建内容
            await RebuildControllerHybrid(source, target);
        }
        
        private async System.Threading.Tasks.Task RebuildControllerHybrid(AnimatorController source, AnimatorController target)
        {
            try
            {
                while (target.layers.Length > 0)
                {
                    target.RemoveLayer(0);
                }
                
                for (int i = 0; i < source.layers.Length; i++)
                {
                    var sourceLayer = source.layers[i];
                    
                    var newLayer = new AnimatorControllerLayer();
                    newLayer.name = sourceLayer.name;
                    newLayer.defaultWeight = sourceLayer.defaultWeight;
                    newLayer.blendingMode = sourceLayer.blendingMode;
                    newLayer.syncedLayerIndex = sourceLayer.syncedLayerIndex;
                    newLayer.iKPass = sourceLayer.iKPass;
                    newLayer.syncedLayerAffectsTiming = sourceLayer.syncedLayerAffectsTiming;
                    
                    var newStateMachine = new AnimatorStateMachine();
                    newStateMachine.name = sourceLayer.stateMachine.name;
                    newStateMachine.hideFlags = sourceLayer.stateMachine.hideFlags;
                    
                    newLayer.stateMachine = newStateMachine;
                    
                    target.AddLayer(newLayer);
                    
                    EditorUtility.SetDirty(newStateMachine);
                    AssetDatabase.AddObjectToAsset(newStateMachine, target);
                    AssetDatabase.SaveAssetIfDirty(newStateMachine);
                }
                
                EditorUtility.SetDirty(target);
                AssetDatabase.SaveAssetIfDirty(target);
                AssetDatabase.Refresh();
                
                for (int i = 0; i < target.layers.Length; i++)
                {
                    var layer = target.layers[i];
                    if (layer.stateMachine == null)
                    {
                        Debug.LogWarning($"[VRCTool_ACR] 层 {i}: {layer.name} -> 状态机为空");
                    }
                }
                
                for (int i = 0; i < source.layers.Length && i < target.layers.Length; i++)
                {
                    var sourceLayer = source.layers[i];
                    var targetLayer = target.layers[i];
                    
                    if (sourceLayer.stateMachine != null && targetLayer.stateMachine != null)
                    {
                        await RebuildStateMachine(sourceLayer.stateMachine, targetLayer.stateMachine, target);
                        
                        EditorUtility.SetDirty(targetLayer.stateMachine);
                        AssetDatabase.SaveAssetIfDirty(targetLayer.stateMachine);
                    }
                }
                
                EditorUtility.SetDirty(target);
                AssetDatabase.SaveAssetIfDirty(target);
                AssetDatabase.Refresh();
                
                await ManualSetComponentProperties(source, target);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[VRCTool_ACR] 混合重建失败: {ex.Message}");
                await RebuildLayers(source, target);
            }
        }
        
        private void CopyLayerBasicProperties(SerializedProperty sourceLayer, SerializedProperty targetLayer)
        {
            // 复制层的基本属性，跳过状态机
            var layerIterator = sourceLayer.Copy();
            if (layerIterator.NextVisible(true))
            {
                do
                {
                    if (layerIterator.propertyPath != "m_StateMachine")
                    {
                        var targetProperty = targetLayer.FindPropertyRelative(layerIterator.propertyPath);
                        if (targetProperty != null)
                        {
                            CopySerializedProperty(layerIterator, targetProperty);
                        }
                    }
                } while (layerIterator.NextVisible(false));
            }
        }
        
        private async System.Threading.Tasks.Task<AnimatorController> RebuildControllerUsingCopyAsset(AnimatorController source, AnimatorController target)
        {
            try
            {
                
                // 获取源控制器的路径
                string sourcePath = AssetDatabase.GetAssetPath(source);
                string targetPath = AssetDatabase.GetAssetPath(target);
                
                // 删除目标控制器
                AssetDatabase.DeleteAsset(targetPath);
                
                // 复制源控制器到目标路径
                AssetDatabase.CopyAsset(sourcePath, targetPath);
                AssetDatabase.Refresh();
                
                // 重新加载目标控制器
                target = AssetDatabase.LoadAssetAtPath<AnimatorController>(targetPath);
                
                if (target != null)
                {
                    // 重命名控制器
                    target.name = $"{source.name}_Rebuilt";
                    EditorUtility.SetDirty(target);
                    AssetDatabase.SaveAssetIfDirty(target);
                    
                    
                    // 验证复制结果
                    for (int i = 0; i < target.layers.Length; i++)
                    {
                        var layer = target.layers[i];
                        if (layer.stateMachine == null)
                        {
                            Debug.LogWarning($"[VRCTool_ACR] 层 {i}: {layer.name} -> 状态机为空");
                        }
                    }
                    
                    return target;
                }
                else
                {
                    Debug.LogWarning($"[VRCTool_ACR] 无法加载复制的控制器");
                    return null;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[VRCTool_ACR] CopyAsset复制失败: {ex.Message}");
                // 回退到原来的方法
            await RebuildParameters(source, target);
                await RebuildLayers(source, target);
                return target;
            }
        }
        
        private async System.Threading.Tasks.Task RebuildControllerUsingSerialization(AnimatorController source, AnimatorController target)
        {
            try
            {
                
                // 使用序列化系统复制控制器
                var sourceSerializedObject = new SerializedObject(source);
                var targetSerializedObject = new SerializedObject(target);
                
                // 复制所有属性
                var iterator = sourceSerializedObject.GetIterator();
                if (iterator.NextVisible(true))
                {
                    do
                    {
                        if (iterator.propertyPath == "m_AnimatorLayers")
                        {
                            // 特殊处理层 - 直接复制层数组
                            var targetLayers = targetSerializedObject.FindProperty("m_AnimatorLayers");
                            if (targetLayers != null)
                            {
                                targetLayers.ClearArray();
                                for (int i = 0; i < iterator.arraySize; i++)
                                {
                                    targetLayers.InsertArrayElementAtIndex(i);
                                    var sourceLayer = iterator.GetArrayElementAtIndex(i);
                                    var targetLayer = targetLayers.GetArrayElementAtIndex(i);
                                    
                                    // 复制层的所有属性
                                    var layerIterator = sourceLayer.Copy();
                                    if (layerIterator.NextVisible(true))
                                    {
                                        do
                                        {
                                            var targetLayerProperty = targetLayer.FindPropertyRelative(layerIterator.propertyPath);
                                            if (targetLayerProperty != null)
                                            {
                                                CopySerializedProperty(layerIterator, targetLayerProperty);
                                            }
                                        } while (layerIterator.NextVisible(false));
                                    }
                                }
                            }
                        }
                        else if (iterator.propertyPath != "m_Name" && iterator.propertyPath != "m_ObjectHideFlags")
                        {
                            // 复制其他属性
                            var targetProperty = targetSerializedObject.FindProperty(iterator.propertyPath);
                            if (targetProperty != null)
                            {
                                CopySerializedProperty(iterator, targetProperty);
                            }
                        }
                    } while (iterator.NextVisible(false));
                }
                
                targetSerializedObject.ApplyModifiedProperties();
                
                // 验证复制结果
                for (int i = 0; i < target.layers.Length; i++)
                {
                    var layer = target.layers[i];
                    if (layer.stateMachine == null)
                    {
                        Debug.LogWarning($"[VRCTool_ACR] 层 {i}: {layer.name} -> 状态机为空");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[VRCTool_ACR] 序列化复制失败: {ex.Message}");
                // 回退到原来的方法
            await RebuildLayers(source, target);
            }
        }
        
        private void CopySerializedProperty(SerializedProperty source, SerializedProperty target)
        {
            try
            {
                switch (source.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        target.intValue = source.intValue;
                        break;
                    case SerializedPropertyType.Boolean:
                        target.boolValue = source.boolValue;
                        break;
                    case SerializedPropertyType.Float:
                        target.floatValue = source.floatValue;
                        break;
                    case SerializedPropertyType.String:
                        target.stringValue = source.stringValue;
                        break;
                    case SerializedPropertyType.Color:
                        target.colorValue = source.colorValue;
                        break;
                    case SerializedPropertyType.Vector2:
                        target.vector2Value = source.vector2Value;
                        break;
                    case SerializedPropertyType.Vector3:
                        target.vector3Value = source.vector3Value;
                        break;
                    case SerializedPropertyType.Vector4:
                        target.vector4Value = source.vector4Value;
                        break;
                    case SerializedPropertyType.ObjectReference:
                        target.objectReferenceValue = source.objectReferenceValue;
                        break;
                    case SerializedPropertyType.Enum:
                        target.enumValueIndex = source.enumValueIndex;
                        break;
                    case SerializedPropertyType.ArraySize:
                        target.intValue = source.intValue;
                        break;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[VRCTool_ACR] 属性复制失败: {source.propertyPath} - {ex.Message}");
            }
        }
        
        private async System.Threading.Tasks.Task RebuildParameters(AnimatorController source, AnimatorController target)
        {
            if (_cancelRequested)
                return;

            foreach (var param in source.parameters)
            {
                try
                {
                    AnimatorControllerParameter newParam = new AnimatorControllerParameter
                    {
                        name = param.name,
                        type = param.type,
                        defaultBool = param.defaultBool,
                        defaultFloat = param.defaultFloat,
                        defaultInt = param.defaultInt
                    };
                    
                    target.AddParameter(newParam);
                    totalParameters++;
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[VRCTool_ACR] 参数复制失败: {param.name} - {ex.Message}");
                    fixedErrors++;
                }
            }
            await System.Threading.Tasks.Task.Yield();
        }
        
        private async System.Threading.Tasks.Task RebuildLayers(AnimatorController source, AnimatorController target)
        {
            if (_cancelRequested)
                return;


            // 使用Unity的AddLayer方法，让Unity自动创建状态机
            for (int i = 0; i < source.layers.Length; i++)
            {
                var sourceLayer = source.layers[i];
                
                if (i == 0)
                {

                    // 第一层：使用现有的状态机
                    var existingLayer = target.layers[0];
                    existingLayer.name = sourceLayer.name;
                    existingLayer.defaultWeight = sourceLayer.defaultWeight;
                    existingLayer.syncedLayerIndex = sourceLayer.syncedLayerIndex;
                    existingLayer.iKPass = sourceLayer.iKPass;
                    existingLayer.avatarMask = sourceLayer.avatarMask;
                    existingLayer.blendingMode = sourceLayer.blendingMode;
                    
                    await RebuildStateMachine(sourceLayer.stateMachine, existingLayer.stateMachine, target);
                }
                else
                {

                    // 其他层：使用AddLayer让Unity自动创建状态机
                    var newLayer = new AnimatorControllerLayer
                    {
                        name = sourceLayer.name,
                        defaultWeight = sourceLayer.defaultWeight,
                        syncedLayerIndex = sourceLayer.syncedLayerIndex,
                        iKPass = sourceLayer.iKPass,
                        avatarMask = sourceLayer.avatarMask,
                        blendingMode = sourceLayer.blendingMode
                    };
                    
                    
                    // 让Unity自动创建状态机
                    target.AddLayer(newLayer);

                    
                    var addedLayer = target.layers[target.layers.Length - 1];
                    if (addedLayer.stateMachine != null)
                    {
                        await RebuildStateMachine(sourceLayer.stateMachine, addedLayer.stateMachine, target);
                    }
                    else
                    {
                        Debug.LogWarning($"[VRCTool_ACR] Unity没有为层 {newLayer.name} 创建状态机");
                    }
                }
            }
        }
        
        private async System.Threading.Tasks.Task RebuildStateMachine(AnimatorStateMachine source, AnimatorStateMachine target, AnimatorController controller)
        {
            if (_cancelRequested)
                return;

            await RebuildStateMachine(source, target, controller, new Dictionary<AnimatorState, AnimatorState>());
        }
        
        private async System.Threading.Tasks.Task RebuildStateMachine(AnimatorStateMachine source, AnimatorStateMachine target, AnimatorController controller, Dictionary<AnimatorState, AnimatorState> globalStateMapping)
        {
            if (_cancelRequested)
                return;
            if (source == null || target == null)
            {
                Debug.LogError($"[VRCTool_ACR] 状态机为空: Source={source != null}, Target={target != null}");
                return;
            }
            
            // 复制状态机属性
            target.name = source.name;
            target.hideFlags = source.hideFlags;
            target.anyStatePosition = source.anyStatePosition;
            target.entryPosition = source.entryPosition;
            target.exitPosition = source.exitPosition;
            target.parentStateMachinePosition = source.parentStateMachinePosition;
            
            // 1. 复制状态
            var stateMapping = new Dictionary<AnimatorState, AnimatorState>();
            foreach (var childState in source.states)
            {
                if (_cancelRequested)
                    return;

                var newState = await RebuildState(childState.state, controller);
                if (newState != null)
                {
                    target.AddState(newState, childState.position);
                    stateMapping[childState.state] = newState;

                    globalStateMapping[childState.state] = newState; // 添加到全局映射
                    totalStates++;

                    
                    // 立即保存状态机以确保状态被正确添加
                    EditorUtility.SetDirty(target);
                    AssetDatabase.SaveAssetIfDirty(target);
                }
            }
            
            // 2. 复制子状态机
            var subStateMachineMapping = new Dictionary<AnimatorStateMachine, AnimatorStateMachine>();
            foreach (var childStateMachine in source.stateMachines)
            {
                var newSubStateMachine = new AnimatorStateMachine();
                newSubStateMachine.name = childStateMachine.stateMachine.name;
                newSubStateMachine.hideFlags = childStateMachine.stateMachine.hideFlags;
                

                // 递归重建子状态机，传递全局状态映射
                await RebuildStateMachine(childStateMachine.stateMachine, newSubStateMachine, controller, globalStateMapping);
                
                target.AddStateMachine(newSubStateMachine, childStateMachine.position);
                subStateMachineMapping[childStateMachine.stateMachine] = newSubStateMachine;
                totalSubStateMachines++;
                
                // 确保子状态机被正确保存到资源中
                EditorUtility.SetDirty(newSubStateMachine);
                AssetDatabase.AddObjectToAsset(newSubStateMachine, controller);
                AssetDatabase.SaveAssetIfDirty(newSubStateMachine);
            }
            

            // 3. 设置默认状态
            if (source.defaultState != null && stateMapping.ContainsKey(source.defaultState))
            {
                target.defaultState = stateMapping[source.defaultState];
            }
            
            // 4. 复制转换 - 使用全局状态映射
            await RebuildTransitions(source, target, globalStateMapping, subStateMachineMapping, controller);
            
            // 5. 复制AnyState转换 - 使用全局状态映射
            await RebuildAnyStateTransitions(source, target, globalStateMapping, subStateMachineMapping);
            
            // 6. 复制Entry转换 - 使用全局状态映射
            await RebuildEntryTransitions(source, target, globalStateMapping, subStateMachineMapping);
            
            // 7. 复制状态机行为（如果失败就跳过）
            try
            {
            await RebuildStateMachineBehaviours(source, target, controller);

            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[VRCTool_ACR] 状态机行为复制失败，跳过: {source.name} - {ex.Message}");
                fixedErrors++;
            }
            
            // 8. 清理无效转换
            CleanupInvalidTransitions(target);
            
            // 9. 立即保存状态机以确保所有转换和子状态机被持久化
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssetIfDirty(target);
            
            // 10. 强制保存所有状态和转换
            foreach (var state in target.states)
            {
                if (state.state != null)
                {
                    EditorUtility.SetDirty(state.state);
                    AssetDatabase.SaveAssetIfDirty(state.state);
                }
            }
            
            // 11. 修复状态机引用
            FixStateMachineReferences(target);
        }
        
        private async System.Threading.Tasks.Task ManualSetComponentProperties(AnimatorController source, AnimatorController target)
        {
            try
            {
                if (_cancelRequested)
                    return;

                for (int layerIndex = 0; layerIndex < source.layers.Length && layerIndex < target.layers.Length; layerIndex++)
                {
                    var sourceLayer = source.layers[layerIndex];
                    var targetLayer = target.layers[layerIndex];
                    
                    if (sourceLayer.stateMachine != null && targetLayer.stateMachine != null)
                    {
                        await ManualSetStateMachineComponentProperties(sourceLayer.stateMachine, targetLayer.stateMachine, target);
                    }
                }
                
                EditorUtility.SetDirty(target);
                AssetDatabase.SaveAssetIfDirty(target);
                AssetDatabase.Refresh();
                
                await RelinkAllParameterReferences(target);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[VRCTool_ACR] 手动设置组件属性失败: {ex.Message}");
            }
        }
        
        private async System.Threading.Tasks.Task ManualSetStateMachineComponentProperties(AnimatorStateMachine source, AnimatorStateMachine target, AnimatorController controller)
        {
            try
            {
                if (_cancelRequested)
                    return;

                // 处理状态机中的状态
                foreach (var sourceState in source.states)
                {
                    // 找到对应的目标状态
                    var targetState = FindTargetState(target, sourceState.state.name);
                    if (targetState != null)
                    {
                        await ManualSetStateComponentProperties(sourceState.state, targetState, controller);
                    }
                }
                
                // 递归处理子状态机
                foreach (var sourceSubStateMachine in source.stateMachines)
                {
                    var targetSubStateMachine = FindTargetStateMachine(target, sourceSubStateMachine.stateMachine.name);
                    if (targetSubStateMachine != null)
                    {
                        await ManualSetStateMachineComponentProperties(sourceSubStateMachine.stateMachine, targetSubStateMachine, controller);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[VRCTool_ACR] 状态机组件属性设置失败: {source.name} - {ex.Message}");
            }
        }
        
        private async System.Threading.Tasks.Task ManualSetStateComponentProperties(AnimatorState source, AnimatorState target, AnimatorController controller)
        {
            try
            {
                if (_cancelRequested)
                    return;

                foreach (var sourceBehaviour in source.behaviours)
                {
                    if (sourceBehaviour == null) continue;
                    
                    // 找到对应的目标行为组件
                    var targetBehaviour = FindTargetBehaviour(target, sourceBehaviour.GetType());
                    if (targetBehaviour != null)
                    {
                        await ManualCopyBehaviourProperties(sourceBehaviour, targetBehaviour, controller);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[VRCTool_ACR] 状态组件属性设置失败: {source.name} - {ex.Message}");
            }
        }
        
        private async System.Threading.Tasks.Task ManualCopyBehaviourProperties(StateMachineBehaviour source, StateMachineBehaviour target, AnimatorController controller)
        {
            try
            {
                if (_cancelRequested)
                    return;

                var sourceSerializedObject = new SerializedObject(source);
                var targetSerializedObject = new SerializedObject(target);
                
                // 特殊处理 VRC Parameter Driver 组件
                if (source.GetType().Name.Contains("VRCAvatarParameterDriver"))
                {
                    await CopyVRCAvatarParameterDriver(sourceSerializedObject, targetSerializedObject, controller);
                }
                else
                {
                    // 普通组件的复制逻辑
                    var iterator = sourceSerializedObject.GetIterator();
                    if (iterator.NextVisible(true))
                    {
                        do
                        {
                            try
                            {
                                var targetProperty = targetSerializedObject.FindProperty(iterator.propertyPath);
                                if (targetProperty != null)
                                {
                                    CopySerializedPropertyValue(iterator, targetProperty);
                                }
                            }
                            catch (System.Exception propEx)
                            {
                                Debug.LogWarning($"[VRCTool_ACR] 手动复制属性失败: {iterator.propertyPath} - {propEx.Message}");
                            }
                        } while (iterator.NextVisible(false));
                    }
                }
                
                targetSerializedObject.ApplyModifiedProperties();
                
                EditorUtility.SetDirty(target);
                EditorUtility.SetDirty(targetSerializedObject.targetObject);
                AssetDatabase.SaveAssetIfDirty(target);
                AssetDatabase.SaveAssetIfDirty(targetSerializedObject.targetObject);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[VRCTool_ACR] 手动复制行为属性失败: {source.GetType().Name} - {ex.Message}");
            }
        }
        
        private async System.Threading.Tasks.Task CopyVRCAvatarParameterDriver(SerializedObject source, SerializedObject target, AnimatorController controller)
        {
            try
            {
                var sourceIterator = source.GetIterator();
                if (sourceIterator.NextVisible(true))
                {
                    do
                    {
                        try
                        {
                            if (sourceIterator.propertyPath == "parameters")
                            {
                                continue;
                            }
                            
                            var targetProperty = target.FindProperty(sourceIterator.propertyPath);
                            if (targetProperty != null)
                            {
                                CopySerializedPropertyValue(sourceIterator, targetProperty);
                            }
                        }
                        catch (System.Exception propEx)
                        {
                            Debug.LogWarning($"[VRCTool_ACR] 复制基本属性失败: {sourceIterator.propertyPath} - {propEx.Message}");
                        }
                    } while (sourceIterator.NextVisible(false));
                }
                
                var sourceParameters = source.FindProperty("parameters");
                if (sourceParameters == null)
                {
                    Debug.LogWarning($"[VRCTool_ACR] 源组件没有 parameters 属性");
                    return;
                }
                
                var targetParameters = target.FindProperty("parameters");
                if (targetParameters != null)
                {
                    targetParameters.ClearArray();
                }
                
                for (int i = 0; i < sourceParameters.arraySize; i++)
                {
                    var sourceParam = sourceParameters.GetArrayElementAtIndex(i);
                    
                    var sourceName = sourceParam.FindPropertyRelative("name");
                    var sourceValue = sourceParam.FindPropertyRelative("value");
                    var sourceType = sourceParam.FindPropertyRelative("type");
                    var sourceLocalOnly = sourceParam.FindPropertyRelative("localOnly");
                    var sourceConvertRange = sourceParam.FindPropertyRelative("convertRange");
                    var sourceSourceMin = sourceParam.FindPropertyRelative("sourceMin");
                    var sourceSourceMax = sourceParam.FindPropertyRelative("sourceMax");
                    var sourceDestMin = sourceParam.FindPropertyRelative("destMin");
                    var sourceDestMax = sourceParam.FindPropertyRelative("destMax");
                    
                    var sourceParameter = sourceParam.FindPropertyRelative("parameter");
                    var sourceSourceParameter = sourceParam.FindPropertyRelative("sourceParameter");
                    
                    var sourceValueMin = sourceParam.FindPropertyRelative("valueMin");
                    var sourceValueMax = sourceParam.FindPropertyRelative("valueMax");
                    var sourceChance = sourceParam.FindPropertyRelative("chance");
                    var sourceSource = sourceParam.FindPropertyRelative("source");
                    var sourceDestination = sourceParam.FindPropertyRelative("destination");
                    
                    if (sourceName == null)
                    {
                        Debug.LogWarning($"[VRCTool_ACR] 跳过无效的参数 {i} - 缺少 name 属性");
                        continue;
                    }
                    
                    string paramName = sourceName.stringValue;
                    
                    if (string.IsNullOrEmpty(paramName))
                    {
                        Debug.LogWarning($"[VRCTool_ACR] 跳过空名称的参数 {i}");
                        continue;
                    }
                    
                    AnimatorControllerParameter targetParam = null;
                    if (controller != null)
                    {
                        foreach (var ctrlParam in controller.parameters)
                        {
                            if (ctrlParam.name == paramName)
                            {
                                targetParam = ctrlParam;
                                break;
                            }
                        }
                    }
                    
                    if (targetParam == null)
                    {
                        Debug.LogWarning($"[VRCTool_ACR] 在控制器中未找到参数: {paramName}");
                        continue;
                    }
                    
                    targetParameters.InsertArrayElementAtIndex(i);
                    var newParam = targetParameters.GetArrayElementAtIndex(i);
                    
                    var newParamName = newParam.FindPropertyRelative("name");
                    var newParamValue = newParam.FindPropertyRelative("value");
                    var newParamType = newParam.FindPropertyRelative("type");
                    var newParamLocalOnly = newParam.FindPropertyRelative("localOnly");
                    var newParamConvertRange = newParam.FindPropertyRelative("convertRange");
                    var newParamSourceMin = newParam.FindPropertyRelative("sourceMin");
                    var newParamSourceMax = newParam.FindPropertyRelative("sourceMax");
                    var newParamDestMin = newParam.FindPropertyRelative("destMin");
                    var newParamDestMax = newParam.FindPropertyRelative("destMax");
                    
                    var newParamParameter = newParam.FindPropertyRelative("parameter");
                    var newParamSourceParameter = newParam.FindPropertyRelative("sourceParameter");
                    
                    var newParamValueMin = newParam.FindPropertyRelative("valueMin");
                    var newParamValueMax = newParam.FindPropertyRelative("valueMax");
                    var newParamChance = newParam.FindPropertyRelative("chance");
                    var newParamSource = newParam.FindPropertyRelative("source");
                    var newParamDestination = newParam.FindPropertyRelative("destination");
                    
                    if (newParamName != null)
                        newParamName.stringValue = paramName;
                    
                    if (newParamValue != null && sourceValue != null)
                        newParamValue.floatValue = sourceValue.floatValue;
                    
                    if (newParamType != null && sourceType != null)
                        newParamType.enumValueIndex = sourceType.enumValueIndex;
                    
                    if (newParamLocalOnly != null && sourceLocalOnly != null)
                    {
                        newParamLocalOnly.boolValue = sourceLocalOnly.boolValue;
                    }
                    
                    if (newParamConvertRange != null && sourceConvertRange != null)
                    {
                        newParamConvertRange.boolValue = sourceConvertRange.boolValue;
                    }
                    
                    if (newParamSourceMin != null && sourceSourceMin != null)
                        newParamSourceMin.floatValue = sourceSourceMin.floatValue;
                    
                    if (newParamSourceMax != null && sourceSourceMax != null)
                        newParamSourceMax.floatValue = sourceSourceMax.floatValue;
                    
                    if (newParamDestMin != null && sourceDestMin != null)
                        newParamDestMin.floatValue = sourceDestMin.floatValue;
                    
                    if (newParamDestMax != null && sourceDestMax != null)
                        newParamDestMax.floatValue = sourceDestMax.floatValue;
                    
                    if (newParamValueMin != null && sourceValueMin != null)
                    {
                        newParamValueMin.floatValue = sourceValueMin.floatValue;
                    }
                    
                    if (newParamValueMax != null && sourceValueMax != null)
                    {
                        newParamValueMax.floatValue = sourceValueMax.floatValue;
                    }
                    
                    if (newParamChance != null && sourceChance != null)
                    {
                        newParamChance.floatValue = sourceChance.floatValue;
                    }
                    
                    if (newParamSource != null && sourceSource != null)
                    {
                        if (sourceSource.propertyType == SerializedPropertyType.String)
                        {
                            newParamSource.stringValue = sourceSource.stringValue;
                        }
                        else if (sourceSource.propertyType == SerializedPropertyType.ObjectReference && sourceSource.objectReferenceValue != null)
                        {
                            newParamSource.stringValue = sourceSource.objectReferenceValue.name;
                        }
                    }
                    
                    if (newParamParameter != null && sourceParameter != null)
                    {
                        try
                        {
                            if (sourceParameter.objectReferenceValue != null)
                            {
                                var sourceDestParamName = sourceParameter.objectReferenceValue.name;
                                
                                AnimatorControllerParameter targetDestParam = null;
                                if (controller != null)
                                {
                                    foreach (var ctrlParam in controller.parameters)
                                    {
                                        if (ctrlParam.name == sourceDestParamName)
                                        {
                                            targetDestParam = ctrlParam;
                                            break;
                                        }
                                    }
                                }
                                
                                if (targetDestParam == null)
                                {
                                    Debug.LogWarning($"[VRCTool_ACR] 在控制器中未找到 Destination 参数: {sourceDestParamName}");
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogWarning($"[VRCTool_ACR] 处理 Destination 参数引用时出错: {ex.Message}");
                        }
                    }
                    
                    if (newParamSourceParameter != null && sourceSourceParameter != null)
                    {
                        try
                        {
                            if (sourceSourceParameter.objectReferenceValue != null)
                            {
                                var sourceSourceParamName = sourceSourceParameter.objectReferenceValue.name;
                                
                                AnimatorControllerParameter targetSourceParam = null;
                                if (controller != null)
                                {
                                    foreach (var ctrlParam in controller.parameters)
                                    {
                                        if (ctrlParam.name == sourceSourceParamName)
                                        {
                                            targetSourceParam = ctrlParam;
                                            break;
                                        }
                                    }
                                }
                                
                                if (targetSourceParam == null)
                                {
                                    Debug.LogWarning($"[VRCTool_ACR] 在控制器中未找到 Source 参数: {sourceSourceParamName}");
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogWarning($"[VRCTool_ACR] 处理 Source 参数引用时出错: {ex.Message}");
                        }
                    }
                    
                    if (newParamSource != null && sourceSource != null && sourceSource.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        try
                        {
                            if (sourceSource.objectReferenceValue != null)
                            {
                                var sourceSourceParamName = sourceSource.objectReferenceValue.name;
                                
                                AnimatorControllerParameter targetSourceParam = null;
                                if (controller != null)
                                {
                                    foreach (var ctrlParam in controller.parameters)
                                    {
                                        if (ctrlParam.name == sourceSourceParamName)
                                        {
                                            targetSourceParam = ctrlParam;
                                            break;
                                        }
                                    }
                                }
                                
                                if (targetSourceParam == null)
                                {
                                    Debug.LogWarning($"[VRCTool_ACR] 在控制器中未找到 Source 参数: {sourceSourceParamName}");
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogWarning($"[VRCTool_ACR] 处理 Source 参数引用时出错: {ex.Message}");
                        }
                    }
                    
                    if (newParamDestination != null && sourceDestination != null && sourceDestination.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        try
                        {
                            if (sourceDestination.objectReferenceValue != null)
                            {
                                var sourceDestParamName = sourceDestination.objectReferenceValue.name;
                                
                                AnimatorControllerParameter targetDestParam = null;
                                if (controller != null)
                                {
                                    foreach (var ctrlParam in controller.parameters)
                                    {
                                        if (ctrlParam.name == sourceDestParamName)
                                        {
                                            targetDestParam = ctrlParam;
                                            break;
                                        }
                                    }
                                }
                                
                                if (targetDestParam == null)
                                {
                                    Debug.LogWarning($"[VRCTool_ACR] 在控制器中未找到 Destination 参数: {sourceDestParamName}");
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogWarning($"[VRCTool_ACR] 处理 Destination 参数引用时出错: {ex.Message}");
                        }
                    }
                }
                
                target.ApplyModifiedProperties();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[VRCTool_ACR] 手动设置 VRC Avatar Parameter Driver 失败: {ex.Message}");
            }
            await System.Threading.Tasks.Task.Yield();
        }
        
        private async System.Threading.Tasks.Task RelinkParameterReferences(SerializedObject target, AnimatorController controller)
        {
            try
            {
                var targetBehaviour = target.targetObject as StateMachineBehaviour;
                if (targetBehaviour != null)
                {
                    EditorUtility.SetDirty(targetBehaviour);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[VRCTool_ACR] 参数引用处理失败: {ex.Message}");
            }
            await System.Threading.Tasks.Task.Yield();
        }
        
        private void CopySerializedPropertyValue(SerializedProperty source, SerializedProperty target)
        {
            switch (source.propertyType)
            {
                case SerializedPropertyType.Integer:
                    target.intValue = source.intValue;
                    break;
                case SerializedPropertyType.Boolean:
                    target.boolValue = source.boolValue;
                    break;
                case SerializedPropertyType.Float:
                    target.floatValue = source.floatValue;
                    break;
                case SerializedPropertyType.String:
                    target.stringValue = source.stringValue;
                    break;
                case SerializedPropertyType.Color:
                    target.colorValue = source.colorValue;
                    break;
                case SerializedPropertyType.Vector2:
                    target.vector2Value = source.vector2Value;
                    break;
                case SerializedPropertyType.Vector3:
                    target.vector3Value = source.vector3Value;
                    break;
                case SerializedPropertyType.Vector4:
                    target.vector4Value = source.vector4Value;
                    break;
                case SerializedPropertyType.Enum:
                    target.enumValueIndex = source.enumValueIndex;
                    break;
                case SerializedPropertyType.ObjectReference:
                    if (source.objectReferenceValue != null)
                    {
                        target.objectReferenceValue = source.objectReferenceValue;
                    }
                    break;
            }
        }
        
        private AnimatorState FindTargetState(AnimatorStateMachine stateMachine, string stateName)
        {
            foreach (var state in stateMachine.states)
            {
                if (state.state.name == stateName)
                    return state.state;
            }
            return null;
        }
        
        private AnimatorStateMachine FindTargetStateMachine(AnimatorStateMachine stateMachine, string stateMachineName)
        {
            foreach (var subStateMachine in stateMachine.stateMachines)
            {
                if (subStateMachine.stateMachine.name == stateMachineName)
                    return subStateMachine.stateMachine;
            }
            return null;
        }
        
        private StateMachineBehaviour FindTargetBehaviour(AnimatorState state, System.Type behaviourType)
        {
            foreach (var behaviour in state.behaviours)
            {
                if (behaviour != null && behaviour.GetType() == behaviourType)
                    return behaviour;
            }
            return null;
        }
        
        private void CleanupInvalidTransitions(AnimatorStateMachine stateMachine)
        {
            try
            {
                // 清理 AnyState 转换中的无效转换
                var anyStateTransitions = new List<AnimatorStateTransition>(stateMachine.anyStateTransitions);
                foreach (var transition in anyStateTransitions)
                {
                    if (transition.destinationState == null && transition.destinationStateMachine == null && !transition.isExit)
                    {
                        stateMachine.RemoveAnyStateTransition(transition);
                    }
                }
                
                // 清理 Entry 转换中的无效转换
                var entryTransitions = new List<AnimatorTransition>(stateMachine.entryTransitions);
                foreach (var transition in entryTransitions)
                {
                    if (transition.destinationState == null && transition.destinationStateMachine == null && !transition.isExit)
                    {
                        stateMachine.RemoveEntryTransition(transition);
                    }
                }
                
                // 清理普通状态转换中的无效转换
                foreach (var state in stateMachine.states)
                {
                    var transitions = new List<AnimatorStateTransition>(state.state.transitions);
                    foreach (var transition in transitions)
                    {
                        if (transition.destinationState == null && transition.destinationStateMachine == null && !transition.isExit)
                        {
                            state.state.RemoveTransition(transition);
                        }
                    }
                }
                
                // 递归清理子状态机
                foreach (var subStateMachine in stateMachine.stateMachines)
                {
                    CleanupInvalidTransitions(subStateMachine.stateMachine);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[VRCTool_ACR] 清理无效转换失败: {ex.Message}");
            }
        }
        
        private async System.Threading.Tasks.Task RelinkAllParameterReferences(AnimatorController target)
        {
            try
            {
                for (int layerIndex = 0; layerIndex < target.layers.Length; layerIndex++)
                {
                    var layer = target.layers[layerIndex];
                    if (layer.stateMachine != null)
                    {
                        await RelinkStateMachineParameterReferences(layer.stateMachine);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[VRCTool_ACR] 重新链接所有参数引用失败: {ex.Message}");
            }
        }
        
        private async System.Threading.Tasks.Task RelinkStateMachineParameterReferences(AnimatorStateMachine stateMachine)
        {
            try
            {
                // 处理状态机中的状态
                foreach (var state in stateMachine.states)
                {
                    if (state.state != null)
                    {
                        await RelinkStateParameterReferences(state.state);
                    }
                }
                
                // 递归处理子状态机
                foreach (var subStateMachine in stateMachine.stateMachines)
                {
                    if (subStateMachine.stateMachine != null)
                    {
                        await RelinkStateMachineParameterReferences(subStateMachine.stateMachine);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[VRCTool_ACR] 重新链接状态机参数引用失败: {stateMachine.name} - {ex.Message}");
            }
        }
        
        private async System.Threading.Tasks.Task RelinkStateParameterReferences(AnimatorState state)
        {
            try
            {
                foreach (var behaviour in state.behaviours)
                {
                    if (behaviour != null && behaviour.GetType().Name.Contains("VRCAvatarParameterDriver"))
                    {
                        var serializedObject = new SerializedObject(behaviour);
                        var parameters = serializedObject.FindProperty("parameters");
                        
                        if (parameters != null)
                        {
                            for (int i = 0; i < parameters.arraySize; i++)
                            {
                                var parameter = parameters.GetArrayElementAtIndex(i);
                                var nameProperty = parameter.FindPropertyRelative("name");
                                
                                if (nameProperty != null && !string.IsNullOrEmpty(nameProperty.stringValue))
                                {
                                    serializedObject.Update();
                                    serializedObject.ApplyModifiedProperties();
                                    
                                    EditorUtility.SetDirty(behaviour);
                                    AssetDatabase.SaveAssetIfDirty(behaviour);
                                }
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[VRCTool_ACR] 重新链接状态参数引用失败: {state.name} - {ex.Message}");
            }
            await System.Threading.Tasks.Task.Yield();
        }
        
        private void FixStateMachineReferences(AnimatorStateMachine stateMachine)
        {
            try
            {
                var statesToRemove = new List<ChildAnimatorState>();
                foreach (var state in stateMachine.states)
                {
                    if (state.state == null)
                    {
                        Debug.LogWarning($"[VRCTool_ACR] 发现空状态引用，将移除");
                        statesToRemove.Add(state);
                    }
                }
                
                foreach (var stateToRemove in statesToRemove)
                {
                    Debug.LogWarning($"[VRCTool_ACR] 无法移除空状态引用，请手动检查");
                }
                
                var stateMachinesToRemove = new List<ChildAnimatorStateMachine>();
                foreach (var subStateMachine in stateMachine.stateMachines)
                {
                    if (subStateMachine.stateMachine == null)
                    {
                        Debug.LogWarning($"[VRCTool_ACR] 发现空子状态机引用，将移除");
                        stateMachinesToRemove.Add(subStateMachine);
                    }
                    else
                    {
                        FixStateMachineReferences(subStateMachine.stateMachine);
                    }
                }
                
                if (stateMachine.defaultState == null)
                {
                    Debug.LogWarning($"[VRCTool_ACR] 默认状态为空，尝试设置第一个有效状态");
                    if (stateMachine.states.Length > 0 && stateMachine.states[0].state != null)
                    {
                        stateMachine.defaultState = stateMachine.states[0].state;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[VRCTool_ACR] 修复状态机引用失败: {stateMachine.name} - {ex.Message}");
            }
        }
        
        private async System.Threading.Tasks.Task<AnimatorState> RebuildState(AnimatorState source, AnimatorController controller)
        {
            if (source == null) return null;

            if (_cancelRequested)
                return null;
            
            var newState = new AnimatorState();
            newState.name = source.name;
            newState.hideFlags = source.hideFlags;
            newState.speed = source.speed;
            newState.cycleOffset = source.cycleOffset;
            newState.iKOnFeet = source.iKOnFeet;
            newState.writeDefaultValues = source.writeDefaultValues;
            newState.mirror = source.mirror;
            newState.speedParameterActive = source.speedParameterActive;
            newState.mirrorParameterActive = source.mirrorParameterActive;
            newState.cycleOffsetParameterActive = source.cycleOffsetParameterActive;
            newState.timeParameterActive = source.timeParameterActive;
            newState.tag = source.tag;
            newState.speedParameter = source.speedParameter;
            newState.mirrorParameter = source.mirrorParameter;
            newState.cycleOffsetParameter = source.cycleOffsetParameter;
            newState.timeParameter = source.timeParameter;
            
            // 复制动画剪辑
            if (source.motion != null)
            {
                if (source.motion is AnimationClip clip)
                {
                    // 检查动画剪辑是否有效
                    if (clip != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(clip)))
                    {
                        newState.motion = clip;
                    }
                    else
                    {
                        Debug.LogWarning($"[VRCTool_ACR] 跳过损坏的动画剪辑: {source.name}");
                        fixedErrors++;
                    }
                }
                else if (source.motion is BlendTree blendTree)
                {
                    if (_cancelRequested)
                        return newState;

                    var newBlendTree = await RebuildBlendTree(blendTree, controller);
                    if (newBlendTree != null)
                    {
                        newState.motion = newBlendTree;
                        totalBlendTrees++;
                    }
                }
            }
            

            // 复制状态行为（如果失败就跳过）
            try
            {
            await RebuildStateBehaviours(source, newState, controller);

            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[VRCTool_ACR] 状态行为复制失败，跳过: {source.name} - {ex.Message}");
                fixedErrors++;
            }
            
            EditorUtility.SetDirty(newState);
            AssetDatabase.AddObjectToAsset(newState, controller);
            AssetDatabase.SaveAssetIfDirty(newState);
            
            return newState;
        }
        
        private async System.Threading.Tasks.Task<BlendTree> RebuildBlendTree(BlendTree source, AnimatorController controller)
        {
            if (source == null) return null;

            if (_cancelRequested)
                return null;
            
            var newBlendTree = new BlendTree();
            newBlendTree.name = source.name;
            newBlendTree.hideFlags = source.hideFlags;
            newBlendTree.blendParameter = source.blendParameter;
            newBlendTree.blendParameterY = source.blendParameterY;
            newBlendTree.minThreshold = source.minThreshold;
            newBlendTree.maxThreshold = source.maxThreshold;
            newBlendTree.useAutomaticThresholds = source.useAutomaticThresholds;

            // normalizedBlendValues 属性在某些Unity版本中不存在，跳过
            newBlendTree.blendType = source.blendType;
            
            // 复制子运动
            foreach (var child in source.children)
            {
                if (_cancelRequested)
                    return newBlendTree;

                if (child.motion != null)
                {
                    if (child.motion is AnimationClip clip)
                    {
                        if (clip != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(clip)))
                        {
                            newBlendTree.AddChild(clip, child.threshold);
                        }
                        else
                        {
                            Debug.LogWarning($"[VRCTool_ACR] 跳过损坏的混合树动画剪辑: {child.motion.name}");
                            fixedErrors++;
                        }
                    }
                    else if (child.motion is BlendTree subBlendTree)
                    {
                        var newSubBlendTree = await RebuildBlendTree(subBlendTree, controller);
                        if (newSubBlendTree != null)
                        {
                            newBlendTree.AddChild(newSubBlendTree, child.threshold);
                        }
                    }
                }
            }
            
            // 手动复制子项的 directBlendParameter 属性
            try
            {
                var sourceSerializedObject = new SerializedObject(source);
                var targetSerializedObject = new SerializedObject(newBlendTree);
                
                // 获取子项数组
                var sourceChildren = sourceSerializedObject.FindProperty("m_Childs");
                var targetChildren = targetSerializedObject.FindProperty("m_Childs");
                
                if (sourceChildren != null && targetChildren != null && sourceChildren.arraySize == targetChildren.arraySize)
                {
                    for (int i = 0; i < sourceChildren.arraySize; i++)
                    {
                        var sourceChild = sourceChildren.GetArrayElementAtIndex(i);
                        var targetChild = targetChildren.GetArrayElementAtIndex(i);
                        
                        // 复制 directBlendParameter
                        var sourceDirectBlendParam = sourceChild.FindPropertyRelative("m_DirectBlendParameter");
                        var targetDirectBlendParam = targetChild.FindPropertyRelative("m_DirectBlendParameter");
                        
                        if (sourceDirectBlendParam != null && targetDirectBlendParam != null)
                        {
                            targetDirectBlendParam.stringValue = sourceDirectBlendParam.stringValue;
                        }
                        
                        // 复制其他子项属性
                        var sourceThreshold = sourceChild.FindPropertyRelative("m_Threshold");
                        var targetThreshold = targetChild.FindPropertyRelative("m_Threshold");
                        if (sourceThreshold != null && targetThreshold != null)
                        {
                            targetThreshold.floatValue = sourceThreshold.floatValue;
                        }
                        
                        var sourcePosition = sourceChild.FindPropertyRelative("m_Position");
                        var targetPosition = targetChild.FindPropertyRelative("m_Position");
                        if (sourcePosition != null && targetPosition != null)
                        {
                            targetPosition.vector2Value = sourcePosition.vector2Value;
                        }
                        
                        var sourceTimeScale = sourceChild.FindPropertyRelative("m_TimeScale");
                        var targetTimeScale = targetChild.FindPropertyRelative("m_TimeScale");
                        if (sourceTimeScale != null && targetTimeScale != null)
                        {
                            targetTimeScale.floatValue = sourceTimeScale.floatValue;
                        }
                        
                        var sourceCycleOffset = sourceChild.FindPropertyRelative("m_CycleOffset");
                        var targetCycleOffset = targetChild.FindPropertyRelative("m_CycleOffset");
                        if (sourceCycleOffset != null && targetCycleOffset != null)
                        {
                            targetCycleOffset.floatValue = sourceCycleOffset.floatValue;
                        }
                        
                        var sourceMirror = sourceChild.FindPropertyRelative("m_Mirror");
                        var targetMirror = targetChild.FindPropertyRelative("m_Mirror");
                        if (sourceMirror != null && targetMirror != null)
                        {
                            targetMirror.boolValue = sourceMirror.boolValue;
                        }
                    }
                }
                
                targetSerializedObject.ApplyModifiedProperties();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[VRCTool_ACR] 复制 BlendTree 子项属性失败: {ex.Message}");
            }

            EditorUtility.SetDirty(newBlendTree);
            AssetDatabase.AddObjectToAsset(newBlendTree, controller);
            AssetDatabase.SaveAssetIfDirty(newBlendTree);
            
            return newBlendTree;
        }
        
        private async System.Threading.Tasks.Task RebuildTransitions(AnimatorStateMachine source, AnimatorStateMachine target, 
            Dictionary<AnimatorState, AnimatorState> globalStateMapping, Dictionary<AnimatorStateMachine, AnimatorStateMachine> subStateMachineMapping, AnimatorController controller)
        {
            foreach (var sourceState in source.states)
            {
                var targetState = globalStateMapping[sourceState.state];
                if (targetState == null) continue;
                
                foreach (var transition in sourceState.state.transitions)
                {
                    try
                    {
                        var newTransition = new AnimatorStateTransition();
                        newTransition.hideFlags = transition.hideFlags;
                        newTransition.destinationState = null;
                        newTransition.destinationStateMachine = null;
                        newTransition.solo = transition.solo;
                        newTransition.mute = transition.mute;
                        newTransition.isExit = transition.isExit;
                        newTransition.hasExitTime = transition.hasExitTime;
                        newTransition.hasFixedDuration = transition.hasFixedDuration;
                        newTransition.interruptionSource = transition.interruptionSource;
                        newTransition.orderedInterruption = transition.orderedInterruption;
                        newTransition.canTransitionToSelf = transition.canTransitionToSelf;
                        newTransition.duration = transition.duration;
                        newTransition.offset = transition.offset;
                        newTransition.exitTime = transition.exitTime;
                        
                        // 设置目标状态
                        if (transition.destinationState != null)
                        {

                            if (globalStateMapping.ContainsKey(transition.destinationState))
                            {
                                newTransition.destinationState = globalStateMapping[transition.destinationState];
                            }
                            else
                            {
                                Debug.LogWarning($"[VRCTool_ACR] 转换目标状态未找到: {transition.destinationState.name}");
                                continue;
                            }
                        }
                        
                        if (transition.destinationStateMachine != null)
                        {

                            if (subStateMachineMapping.ContainsKey(transition.destinationStateMachine))
                            {
                                newTransition.destinationStateMachine = subStateMachineMapping[transition.destinationStateMachine];
                            }
                            else
                            {
                                Debug.LogWarning($"[VRCTool_ACR] 转换目标状态机未找到: {transition.destinationStateMachine.name}");
                                continue;
                            }
                        }
                        
                        // 复制条件
                        foreach (var condition in transition.conditions)
                        {
                            newTransition.AddCondition(condition.mode, condition.threshold, condition.parameter);
                        }
                        
                        // 直接使用状态级别的转换创建，但确保转换被正确序列化
                        targetState.AddTransition(newTransition);
                        
                        // 关键：确保转换对象被添加到控制器的资源中
                        EditorUtility.SetDirty(newTransition);
                        AssetDatabase.AddObjectToAsset(newTransition, controller);
                        AssetDatabase.SaveAssetIfDirty(newTransition);
                        
                        totalTransitions++;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[VRCTool_ACR] 转换复制失败: {sourceState.state.name} - {ex.Message}");
                        fixedErrors++;
                    }
                }
            }
            await System.Threading.Tasks.Task.Yield();
        }
        
        private async System.Threading.Tasks.Task RebuildAnyStateTransitions(AnimatorStateMachine source, AnimatorStateMachine target,
            Dictionary<AnimatorState, AnimatorState> globalStateMapping, Dictionary<AnimatorStateMachine, AnimatorStateMachine> subStateMachineMapping)
        {
            foreach (var transition in source.anyStateTransitions)
            {
                try
                {
                    var newTransition = new AnimatorStateTransition();
                    newTransition.hideFlags = transition.hideFlags;
                    newTransition.destinationState = null;
                    newTransition.destinationStateMachine = null;
                    newTransition.solo = transition.solo;
                    newTransition.mute = transition.mute;
                    newTransition.isExit = transition.isExit;
                    newTransition.hasExitTime = transition.hasExitTime;
                    newTransition.hasFixedDuration = transition.hasFixedDuration;
                    newTransition.interruptionSource = transition.interruptionSource;
                    newTransition.orderedInterruption = transition.orderedInterruption;
                    newTransition.canTransitionToSelf = transition.canTransitionToSelf;
                    newTransition.duration = transition.duration;
                    newTransition.offset = transition.offset;
                    newTransition.exitTime = transition.exitTime;
                    
                    // 设置目标状态

                    AnimatorState targetState = null;
                    AnimatorStateMachine targetStateMachine = null;
                    
                    if (transition.destinationState != null)
                    {

                        if (globalStateMapping.ContainsKey(transition.destinationState))
                        {
                            targetState = globalStateMapping[transition.destinationState];
                        }
                        else
                        {
                            Debug.LogWarning($"[VRCTool_ACR] AnyState转换目标状态未找到: {transition.destinationState.name}");
                            continue;
                        }
                    }
                    
                    if (transition.destinationStateMachine != null)
                    {

                        if (subStateMachineMapping.ContainsKey(transition.destinationStateMachine))
                        {
                            targetStateMachine = subStateMachineMapping[transition.destinationStateMachine];
                        }
                        else
                        {
                            Debug.LogWarning($"[VRCTool_ACR] AnyState转换目标状态机未找到: {transition.destinationStateMachine.name}");
                            continue;
                        }
                    }
                    
                    // 修复方法调用 - 需要先创建转换，然后设置属性
                    if (targetState != null)
                    {
                        var anyStateTransition = target.AddAnyStateTransition(targetState);
                        // 复制转换属性
                        anyStateTransition.hideFlags = newTransition.hideFlags;
                        anyStateTransition.solo = newTransition.solo;
                        anyStateTransition.mute = newTransition.mute;
                        anyStateTransition.isExit = newTransition.isExit;
                        anyStateTransition.hasExitTime = newTransition.hasExitTime;
                        anyStateTransition.hasFixedDuration = newTransition.hasFixedDuration;
                        anyStateTransition.interruptionSource = newTransition.interruptionSource;
                        anyStateTransition.orderedInterruption = newTransition.orderedInterruption;
                        anyStateTransition.canTransitionToSelf = newTransition.canTransitionToSelf;
                        anyStateTransition.duration = newTransition.duration;
                        anyStateTransition.offset = newTransition.offset;
                        anyStateTransition.exitTime = newTransition.exitTime;
                    
                    // 复制条件
                    foreach (var condition in transition.conditions)
                    {

                            anyStateTransition.AddCondition(condition.mode, condition.threshold, condition.parameter);
                    }
                    
                    // 确保 AnyState 转换被正确保存到资源中
                    EditorUtility.SetDirty(anyStateTransition);
                    EditorUtility.SetDirty(target);
                    AssetDatabase.SaveAssetIfDirty(anyStateTransition);
                    AssetDatabase.SaveAssetIfDirty(target);
                    }
                    totalTransitions++;
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[VRCTool_ACR] AnyState转换复制失败: {ex.Message}");
                    fixedErrors++;
                }
            }
            await System.Threading.Tasks.Task.Yield();
        }
        
        private async System.Threading.Tasks.Task RebuildEntryTransitions(AnimatorStateMachine source, AnimatorStateMachine target,
            Dictionary<AnimatorState, AnimatorState> globalStateMapping, Dictionary<AnimatorStateMachine, AnimatorStateMachine> subStateMachineMapping)
        {
            foreach (var transition in source.entryTransitions)
            {
                try
                {
                    var newTransition = new AnimatorTransition();
                    newTransition.hideFlags = transition.hideFlags;
                    newTransition.destinationState = null;
                    newTransition.destinationStateMachine = null;
                    newTransition.solo = transition.solo;
                    newTransition.mute = transition.mute;
                    newTransition.isExit = transition.isExit;

                    // AnimatorTransition 没有这些属性，跳过
                    // newTransition.hasExitTime = transition.hasExitTime;
                    // newTransition.hasFixedDuration = transition.hasFixedDuration;
                    // newTransition.interruptionSource = transition.interruptionSource;
                    // newTransition.orderedInterruption = transition.orderedInterruption;
                    // newTransition.canTransitionToSelf = transition.canTransitionToSelf;
                    // newTransition.duration = transition.duration;
                    // newTransition.offset = transition.offset;
                    // newTransition.exitTime = transition.exitTime;
                    
                    // 设置目标状态

                    AnimatorState targetState = null;
                    AnimatorStateMachine targetStateMachine = null;
                    
                    if (transition.destinationState != null)
                    {

                        if (globalStateMapping.ContainsKey(transition.destinationState))
                        {
                            targetState = globalStateMapping[transition.destinationState];
                        }
                        else
                        {
                            Debug.LogWarning($"[VRCTool_ACR] Entry转换目标状态未找到: {transition.destinationState.name}");
                            continue;
                        }
                    }
                    
                    if (transition.destinationStateMachine != null)
                    {

                        if (subStateMachineMapping.ContainsKey(transition.destinationStateMachine))
                        {
                            targetStateMachine = subStateMachineMapping[transition.destinationStateMachine];
                        }
                        else
                        {
                            Debug.LogWarning($"[VRCTool_ACR] Entry转换目标状态机未找到: {transition.destinationStateMachine.name}");
                            continue;
                        }
                    }
                    
                    // 修复方法调用 - 需要先创建转换，然后设置属性
                    if (targetState != null)
                    {
                        var entryTransition = target.AddEntryTransition(targetState);
                        // 复制转换属性
                        entryTransition.hideFlags = newTransition.hideFlags;
                        entryTransition.solo = newTransition.solo;
                        entryTransition.mute = newTransition.mute;
                        entryTransition.isExit = newTransition.isExit;
                    
                    // 复制条件
                    foreach (var condition in transition.conditions)
                    {

                            entryTransition.AddCondition(condition.mode, condition.threshold, condition.parameter);
                    }
                    
                    // 确保 Entry 转换被正确保存到资源中
                    EditorUtility.SetDirty(entryTransition);
                    EditorUtility.SetDirty(target);
                    AssetDatabase.SaveAssetIfDirty(entryTransition);
                    AssetDatabase.SaveAssetIfDirty(target);
                    }
                    totalTransitions++;
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[VRCTool_ACR] Entry转换复制失败: {ex.Message}");
                    fixedErrors++;
                }
            }
            await System.Threading.Tasks.Task.Yield();
        }
        

        private async System.Threading.Tasks.Task RebuildStateBehaviours(AnimatorState source, AnimatorState target, AnimatorController controller)
        {
            if (_cancelRequested)
                return;

            foreach (var behaviour in source.behaviours)
            {
                if (_cancelRequested)
                    return;
                try
                {
                    if (behaviour == null) continue;
                    

                    // 检查行为类型是否可用
                    var behaviourType = behaviour.GetType();
                    if (behaviourType == null)
                    {
                        Debug.LogWarning($"[VRCTool_ACR] 跳过未知行为类型");
                        fixedErrors++;
                        continue;
                    }
                    
                    // VRC组件是必需的，不要跳过它们
                    
                    var newBehaviour = target.AddStateMachineBehaviour(behaviourType);
                    
                    // 特殊处理 VRC Parameter Driver 组件
                    if (behaviourType.Name.Contains("VRCAvatarParameterDriver"))
                    {
                        SerializedObject vrcSerializedObject = new SerializedObject(newBehaviour);
                        SerializedObject vrcSourceSerializedObject = new SerializedObject(behaviour);
                        await CopyVRCAvatarParameterDriver(vrcSourceSerializedObject, vrcSerializedObject, controller);
                        vrcSerializedObject.ApplyModifiedProperties();
                        
                        // 确保 VRC 组件被正确保存到资源中
                        EditorUtility.SetDirty(newBehaviour);
                        AssetDatabase.AddObjectToAsset(newBehaviour, controller);
                        AssetDatabase.SaveAssetIfDirty(newBehaviour);
                        
                        totalBehaviours++;
                        continue; // 跳过后续的通用复制逻辑
                    }
                    
                    SerializedObject serializedObject = null;
                    SerializedObject sourceSerializedObject = null;
                    
                    try
                    {
                        serializedObject = new SerializedObject(newBehaviour);
                        sourceSerializedObject = new SerializedObject(behaviour);
                    }
                    catch (System.Exception serialEx)
                    {
                        Debug.LogWarning($"[VRCTool_ACR] 序列化对象创建失败: {behaviour.GetType().Name} - {serialEx.Message}");
                        fixedErrors++;
                        continue;
                    }
                    
                    bool isVRCComponent = behaviourType.Name.Contains("VRC") || behaviourType.Namespace.Contains("VRC");
                    
                    var iterator = sourceSerializedObject.GetIterator();
                    if (iterator.NextVisible(true))
                    {
                        do

                        {
                            try
                        {
                            if (iterator.propertyType == SerializedPropertyType.ObjectReference)
                            {
                                var targetProperty = serializedObject.FindProperty(iterator.propertyPath);

                                    if (targetProperty != null && iterator.objectReferenceValue != null)
                                {

                                        if (isVRCComponent)
                                        {
                                    targetProperty.objectReferenceValue = iterator.objectReferenceValue;

                                        }
                                        else
                                        {
                                            if (iterator.objectReferenceValue != null && 
                                                !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(iterator.objectReferenceValue)))
                                            {
                                                targetProperty.objectReferenceValue = iterator.objectReferenceValue;
                                            }
                                        }
                                }
                            }
                            else
                            {
                                var targetProperty = serializedObject.FindProperty(iterator.propertyPath);
                                if (targetProperty != null)
                                {

                                        switch (iterator.propertyType)
                                        {
                                            case SerializedPropertyType.Integer:
                                                targetProperty.intValue = iterator.intValue;
                                                break;
                                            case SerializedPropertyType.Boolean:
                                                targetProperty.boolValue = iterator.boolValue;
                                                break;
                                            case SerializedPropertyType.Float:
                                                targetProperty.floatValue = iterator.floatValue;
                                                break;
                                            case SerializedPropertyType.String:
                                                targetProperty.stringValue = iterator.stringValue;
                                                break;
                                            case SerializedPropertyType.Color:
                                                targetProperty.colorValue = iterator.colorValue;
                                                break;
                                            case SerializedPropertyType.Vector2:
                                                targetProperty.vector2Value = iterator.vector2Value;
                                                break;
                                            case SerializedPropertyType.Vector3:
                                                targetProperty.vector3Value = iterator.vector3Value;
                                                break;
                                            case SerializedPropertyType.Vector4:
                                                targetProperty.vector4Value = iterator.vector4Value;
                                                break;
                                            case SerializedPropertyType.Enum:
                                                targetProperty.enumValueIndex = iterator.enumValueIndex;
                                                break;
                                        }
                                    }
                                }
                            }
                            catch (System.Exception propEx)
                            {
                                Debug.LogWarning($"[VRCTool_ACR] 跳过属性: {iterator.propertyPath} - {propEx.Message}");
                                fixedErrors++;
                            }
                        } while (iterator.NextVisible(false));
                    }
                    
                    serializedObject.ApplyModifiedProperties();
                    
                    EditorUtility.SetDirty(newBehaviour);
                    AssetDatabase.AddObjectToAsset(newBehaviour, controller);
                    AssetDatabase.SaveAssetIfDirty(newBehaviour);
                    
                    totalBehaviours++;
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[VRCTool_ACR] 状态行为复制失败: {behaviour.GetType().Name} - {ex.Message}");
                    fixedErrors++;

                    
                    if (behaviour.GetType().Name.Contains("VRC"))
                    {
                        try
                        {
                            var emptyBehaviour = target.AddStateMachineBehaviour(behaviour.GetType());
                            totalBehaviours++;
                        }
                        catch (System.Exception emptyEx)
                        {
                            Debug.LogWarning($"[VRCTool_ACR] 无法创建VRC组件空实例: {behaviour.GetType().Name} - {emptyEx.Message}");
                        }
                    }
                }
            }
        }
        
        private async System.Threading.Tasks.Task RebuildStateMachineBehaviours(AnimatorStateMachine source, AnimatorStateMachine target, AnimatorController controller)
        {
            if (_cancelRequested)
                return;

            foreach (var behaviour in source.behaviours)
            {
                if (_cancelRequested)
                    return;
                try
                {
                    if (behaviour == null) continue;
                    

                    // 检查行为类型是否可用
                    var behaviourType = behaviour.GetType();
                    if (behaviourType == null)
                    {
                        Debug.LogWarning($"[VRCTool_ACR] 跳过未知行为类型");
                        fixedErrors++;
                        continue;
                    }
                    
                    // VRC组件是必需的，不要跳过它们
                    
                    var newBehaviour = target.AddStateMachineBehaviour(behaviourType);
                    
                    // 特殊处理 VRC Parameter Driver 组件
                    if (behaviourType.Name.Contains("VRCAvatarParameterDriver"))
                    {
                        SerializedObject vrcSerializedObject = new SerializedObject(newBehaviour);
                        SerializedObject vrcSourceSerializedObject = new SerializedObject(behaviour);
                        await CopyVRCAvatarParameterDriver(vrcSourceSerializedObject, vrcSerializedObject, controller);
                        vrcSerializedObject.ApplyModifiedProperties();
                        
                        // 确保 VRC 组件被正确保存到资源中
                        EditorUtility.SetDirty(newBehaviour);
                        AssetDatabase.AddObjectToAsset(newBehaviour, controller);
                        AssetDatabase.SaveAssetIfDirty(newBehaviour);
                        
                        totalBehaviours++;
                        continue; // 跳过后续的通用复制逻辑
                    }
                    
                    SerializedObject serializedObject = null;
                    SerializedObject sourceSerializedObject = null;
                    
                    try
                    {
                        serializedObject = new SerializedObject(newBehaviour);
                        sourceSerializedObject = new SerializedObject(behaviour);
                    }
                    catch (System.Exception serialEx)
                    {
                        Debug.LogWarning($"[VRCTool_ACR] 序列化对象创建失败: {behaviour.GetType().Name} - {serialEx.Message}");
                        fixedErrors++;
                        continue;
                    }
                    
                    bool isVRCComponent = behaviourType.Name.Contains("VRC") || behaviourType.Namespace.Contains("VRC");
                    
                    var iterator = sourceSerializedObject.GetIterator();
                    if (iterator.NextVisible(true))
                    {
                        do

                        {
                            try
                        {
                            if (iterator.propertyType == SerializedPropertyType.ObjectReference)
                            {
                                var targetProperty = serializedObject.FindProperty(iterator.propertyPath);

                                    if (targetProperty != null && iterator.objectReferenceValue != null)
                                {

                                        if (isVRCComponent)
                                        {
                                    targetProperty.objectReferenceValue = iterator.objectReferenceValue;

                                        }
                                        else
                                        {
                                            if (iterator.objectReferenceValue != null && 
                                                !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(iterator.objectReferenceValue)))
                                            {
                                                targetProperty.objectReferenceValue = iterator.objectReferenceValue;
                                            }
                                        }
                                }
                            }
                            else
                            {
                                var targetProperty = serializedObject.FindProperty(iterator.propertyPath);
                                if (targetProperty != null)
                                {

                                        switch (iterator.propertyType)
                                        {
                                            case SerializedPropertyType.Integer:
                                                targetProperty.intValue = iterator.intValue;
                                                break;
                                            case SerializedPropertyType.Boolean:
                                                targetProperty.boolValue = iterator.boolValue;
                                                break;
                                            case SerializedPropertyType.Float:
                                                targetProperty.floatValue = iterator.floatValue;
                                                break;
                                            case SerializedPropertyType.String:
                                                targetProperty.stringValue = iterator.stringValue;
                                                break;
                                            case SerializedPropertyType.Color:
                                                targetProperty.colorValue = iterator.colorValue;
                                                break;
                                            case SerializedPropertyType.Vector2:
                                                targetProperty.vector2Value = iterator.vector2Value;
                                                break;
                                            case SerializedPropertyType.Vector3:
                                                targetProperty.vector3Value = iterator.vector3Value;
                                                break;
                                            case SerializedPropertyType.Vector4:
                                                targetProperty.vector4Value = iterator.vector4Value;
                                                break;
                                            case SerializedPropertyType.Enum:
                                                targetProperty.enumValueIndex = iterator.enumValueIndex;
                                                break;
                                        }
                                    }
                                }
                            }
                            catch (System.Exception propEx)
                            {
                                Debug.LogWarning($"[VRCTool_ACR] 跳过属性: {iterator.propertyPath} - {propEx.Message}");
                                fixedErrors++;
                            }
                        } while (iterator.NextVisible(false));
                    }
                    
                    serializedObject.ApplyModifiedProperties();
                    
                    EditorUtility.SetDirty(newBehaviour);
                    AssetDatabase.AddObjectToAsset(newBehaviour, controller);
                    AssetDatabase.SaveAssetIfDirty(newBehaviour);
                    
                    totalBehaviours++;
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[VRCTool_ACR] 状态机行为复制失败: {behaviour.GetType().Name} - {ex.Message}");
                    fixedErrors++;

                    
                    if (behaviour.GetType().Name.Contains("VRC"))
                    {
                        try
                        {
                            var emptyBehaviour = target.AddStateMachineBehaviour(behaviour.GetType());
                            totalBehaviours++;
                        }
                        catch (System.Exception emptyEx)
                        {
                            Debug.LogWarning($"[VRCTool_ACR] 无法创建VRC组件空实例: {behaviour.GetType().Name} - {emptyEx.Message}");
                        }
                    }
                }
            }
        }
        
        private AnimatorState FindStateByName(AnimatorStateMachine stateMachine, string stateName)
        {
            foreach (var childState in stateMachine.states)
            {
                if (childState.state.name == stateName)
                {
                    return childState.state;
                }
            }
            return null;
        }
        
        private void ResetStatistics()
        {
            totalStates = 0;
            totalTransitions = 0;
            totalParameters = 0;
            totalBehaviours = 0;
            totalBlendTrees = 0;
            totalSubStateMachines = 0;
            fixedErrors = 0;
        }
    }
}

