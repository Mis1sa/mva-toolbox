using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using MVA.Toolbox.AvatarQuickToggle;
using MVA.Toolbox.Public;

#if AAO_VRCSDK3_AVATARS && MVA_AAO_SUPPORT
using Anatawa12.AvatarOptimizer.API;
#endif

namespace MVA.Toolbox.AvatarQuickToggle.Editor
{
    [CustomEditor(typeof(QuickToggleConfig))]
    public class QuickToggleConfigEditor : UnityEditor.Editor
    {
        private SerializedProperty _layerConfigsProp;
        private ReorderableList _configList;

        private void OnEnable()
        {
            _layerConfigsProp = serializedObject.FindProperty("layerConfigs");
            SetupReorderableList();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // 顶部提示：引导用户使用菜单窗口进行主要编辑
            EditorGUILayout.HelpBox("推荐使用菜单 Tools/MVA Toolbox/Avatar Quick Toggle 来编辑和设置配置。此组件主要用于查看和微调现有配置。", MessageType.Info);

            DrawAvatarInfo();

            EditorGUILayout.Space();
            DrawConfigList();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawAvatarInfo()
        {
            var cfg = (QuickToggleConfig)target;
            var avatar = cfg != null ? (cfg.targetAvatar != null ? cfg.targetAvatar : cfg.GetComponent<VRCAvatarDescriptor>()) : null;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Avatar", avatar != null ? avatar.gameObject.name : "(未绑定)");
            EditorGUILayout.EndVertical();
        }

        private void SetupReorderableList()
        {
            _configList = new ReorderableList(serializedObject, _layerConfigsProp, true, true, true, true)
            {
                elementHeight = EditorGUIUtility.singleLineHeight + 6f
            };

            _configList.drawHeaderCallback = rect =>
            {
                EditorGUI.LabelField(rect, "配置列表（按顺序应用）");
            };

            _configList.drawElementCallback = (rect, index, active, focused) =>
            {
                var element = _layerConfigsProp.GetArrayElementAtIndex(index);
                var displayNameProp = element.FindPropertyRelative("displayName");
                string title = string.IsNullOrEmpty(displayNameProp.stringValue)
                    ? $"配置 {index + 1}"
                    : displayNameProp.stringValue;

                rect.y += 2f;
                EditorGUI.LabelField(rect, title, EditorStyles.label);
            };

            _configList.onAddCallback = list =>
            {
                int index = _layerConfigsProp.arraySize;
                _layerConfigsProp.arraySize++;
                var element = _layerConfigsProp.GetArrayElementAtIndex(index);
                element.FindPropertyRelative("displayName").stringValue = string.Empty;
                element.FindPropertyRelative("layerName").stringValue = string.Empty;
                element.FindPropertyRelative("parameterName").stringValue = string.Empty;
                element.FindPropertyRelative("layerType").intValue = 0;
                EnsureArrayInitialized(element.FindPropertyRelative("boolTargets"));
                EnsureArrayInitialized(element.FindPropertyRelative("floatTargets"));
                EnsureArrayInitialized(element.FindPropertyRelative("intGroups"));
                _configList.index = index;
            };

            _configList.onRemoveCallback = list =>
            {
                if (list.index < 0 || list.index >= _layerConfigsProp.arraySize) return;
                _layerConfigsProp.DeleteArrayElementAtIndex(list.index);
                if (list.index >= _layerConfigsProp.arraySize)
                    list.index = _layerConfigsProp.arraySize - 1;
            };
        }

        private void EnsureArrayInitialized(SerializedProperty arrayProp)
        {
            if (arrayProp == null) return;
            if (arrayProp.arraySize == 0)
                arrayProp.arraySize = 1;
        }

        private void DrawConfigList()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _configList.DoLayoutList();
            EditorGUILayout.EndVertical();

            if (_configList.index >= 0 && _configList.index < _layerConfigsProp.arraySize)
            {
                var element = _layerConfigsProp.GetArrayElementAtIndex(_configList.index);
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("配置详情", EditorStyles.boldLabel);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawLayerConfigDetail(element);
                EditorGUILayout.EndVertical();
            }
            else
            {
                EditorGUILayout.HelpBox("请选择一个配置以查看详细内容。", MessageType.Info);
            }
        }

        private void DrawLayerConfigDetail(SerializedProperty layerConfig)
        {
            if (layerConfig == null) return;

            var displayNameProp = layerConfig.FindPropertyRelative("displayName");
            var layerNameProp = layerConfig.FindPropertyRelative("layerName");
            var layerTypeProp = layerConfig.FindPropertyRelative("layerType");
            var parameterNameProp = layerConfig.FindPropertyRelative("parameterName");
            var overwriteLayerProp = layerConfig.FindPropertyRelative("overwriteLayer");
            var overwriteParameterProp = layerConfig.FindPropertyRelative("overwriteParameter");
            var writeDefaultProp = layerConfig.FindPropertyRelative("writeDefaultSetting");
            var createMenuProp = layerConfig.FindPropertyRelative("createMenuControl");
            var menuPathProp = layerConfig.FindPropertyRelative("menuPath");
            var boolMenuNameProp = layerConfig.FindPropertyRelative("boolMenuItemName");
            var floatMenuNameProp = layerConfig.FindPropertyRelative("floatMenuItemName");
            var intSubMenuNameProp = layerConfig.FindPropertyRelative("intSubMenuName");
            var intMenuItemNamesProp = layerConfig.FindPropertyRelative("intMenuItemNames");
            var defaultStateProp = layerConfig.FindPropertyRelative("defaultStateSelection");
            var defaultIntProp = layerConfig.FindPropertyRelative("defaultIntValue");
            var defaultFloatProp = layerConfig.FindPropertyRelative("defaultFloatValue");
            var boolTargetsProp = layerConfig.FindPropertyRelative("boolTargets");
            var intGroupsProp = layerConfig.FindPropertyRelative("intGroups");
            var floatTargetsProp = layerConfig.FindPropertyRelative("floatTargets");

            EditorGUILayout.PropertyField(displayNameProp, new GUIContent("配置名称"));
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("基础设置", EditorStyles.boldLabel);
            DrawNameWithOverwrite("层级名称", layerNameProp, overwriteLayerProp);
            DrawNameWithOverwrite("参数名称", parameterNameProp, overwriteParameterProp);

            string[] wdOptions = { "Auto", "On", "Off" };
            writeDefaultProp.intValue = EditorGUILayout.Popup("Write Defaults 设置", writeDefaultProp.intValue, wdOptions);

            EditorGUILayout.Space();
            string[] typeOptions = { "Bool Switch", "Int Switch", "Float Switch" };
            layerTypeProp.intValue = EditorGUILayout.Popup("开关类型", layerTypeProp.intValue, typeOptions);

            EditorGUILayout.Space();
            switch (layerTypeProp.intValue)
            {
                case 0:
                    DrawBoolSection(defaultStateProp, createMenuProp, menuPathProp, boolMenuNameProp, boolTargetsProp);
                    break;
                case 1:
                    DrawIntSection(defaultIntProp, createMenuProp, intSubMenuNameProp, intMenuItemNamesProp, intGroupsProp);
                    break;
                case 2:
                    DrawFloatSection(defaultFloatProp, createMenuProp, menuPathProp, floatMenuNameProp, floatTargetsProp);
                    break;
            }
        }

        private void DrawNameWithOverwrite(string label, SerializedProperty valueProp, SerializedProperty overwriteProp)
        {
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(overwriteProp.boolValue))
            {
                valueProp.stringValue = EditorGUILayout.TextField(label, valueProp.stringValue);
            }
            overwriteProp.boolValue = EditorGUILayout.ToggleLeft("覆盖", overwriteProp.boolValue, GUILayout.Width(60f));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawBoolSection(SerializedProperty defaultState, SerializedProperty createMenu,
            SerializedProperty menuPath, SerializedProperty menuName, SerializedProperty targets)
        {
            EditorGUILayout.LabelField("Bool Switch", EditorStyles.boldLabel);
            defaultState.intValue = EditorGUILayout.IntSlider("默认值", defaultState.intValue, 0, 1);

            DrawMenuOptions(createMenu, menuPath, menuName);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("目标项", EditorStyles.boldLabel);
            DrawTargetList(targets, TargetMode.Bool);
        }

        private void DrawIntSection(SerializedProperty defaultInt, SerializedProperty createMenu,
            SerializedProperty subMenuName, SerializedProperty menuItemNames, SerializedProperty intGroups)
        {
            EditorGUILayout.LabelField("Int Switch", EditorStyles.boldLabel);
            defaultInt.intValue = EditorGUILayout.IntField("默认值", defaultInt.intValue);

            createMenu.boolValue = EditorGUILayout.ToggleLeft("生成菜单项", createMenu.boolValue);
            if (createMenu.boolValue)
            {
                subMenuName.stringValue = EditorGUILayout.TextField("子菜单名称", subMenuName.stringValue);
                DrawStringList(menuItemNames, "菜单项名称");
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("目标组", EditorStyles.boldLabel);
            if (intGroups.arraySize == 0) intGroups.arraySize = 1;
            for (int g = 0; g < intGroups.arraySize; g++)
            {
                var group = intGroups.GetArrayElementAtIndex(g);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"组 {g + 1}", EditorStyles.boldLabel);
                if (GUILayout.Button("-", GUILayout.Width(22)))
                {
                    intGroups.DeleteArrayElementAtIndex(g);
                    if (intGroups.arraySize == 0) intGroups.arraySize = 1;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();

                var stateName = group.FindPropertyRelative("stateName");
                var targets = group.FindPropertyRelative("targetItems");
                stateName.stringValue = EditorGUILayout.TextField("状态名称", stateName.stringValue);
                DrawTargetList(targets, TargetMode.Bool);
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button("添加目标组"))
            {
                intGroups.arraySize++;
            }
        }

        private void DrawFloatSection(SerializedProperty defaultFloat, SerializedProperty createMenu,
            SerializedProperty menuPath, SerializedProperty menuName, SerializedProperty targets)
        {
            EditorGUILayout.LabelField("Float Switch", EditorStyles.boldLabel);
            defaultFloat.floatValue = EditorGUILayout.Slider("默认值", defaultFloat.floatValue, 0f, 1f);

            DrawMenuOptions(createMenu, menuPath, menuName);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("目标项", EditorStyles.boldLabel);
            DrawTargetList(targets, TargetMode.Float);
        }

        private void DrawMenuOptions(SerializedProperty createMenu, SerializedProperty menuPath, SerializedProperty menuName)
        {
            createMenu.boolValue = EditorGUILayout.ToggleLeft("生成菜单项", createMenu.boolValue);
            if (createMenu.boolValue)
            {
                menuPath.stringValue = EditorGUILayout.TextField("菜单项位置", menuPath.stringValue);
                menuName.stringValue = EditorGUILayout.TextField("菜单项名称", menuName.stringValue);
            }
        }

        private enum TargetMode { Bool, Float }

        private void DrawTargetList(SerializedProperty listProp, TargetMode mode)
        {
            if (listProp.arraySize == 0)
                listProp.arraySize = 1;

            for (int i = 0; i < listProp.arraySize; i++)
            {
                var item = listProp.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"目标 {i + 1}", EditorStyles.boldLabel);
                if (GUILayout.Button("-", GUILayout.Width(22)))
                {
                    listProp.DeleteArrayElementAtIndex(i);
                    if (listProp.arraySize == 0) listProp.arraySize = 1;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();

                var targetObjProp = item.FindPropertyRelative("targetObject");
                var currentGo = targetObjProp.objectReferenceValue as GameObject;
                var newGo = (GameObject)EditorGUILayout.ObjectField("目标对象", currentGo, typeof(GameObject), true);
                targetObjProp.objectReferenceValue = ToolboxUtils.ResolveMergeNodeTarget(newGo);

                if (mode == TargetMode.Bool)
                {
                    var controlType = item.FindPropertyRelative("controlType");
                    var goState = item.FindPropertyRelative("goState");
                    var bsState = item.FindPropertyRelative("bsState");
                    var blendShapeName = item.FindPropertyRelative("blendShapeName");

                    string[] modes = { "GameObject", "BlendShape" };
                    controlType.intValue = EditorGUILayout.Popup("模式", controlType.intValue, modes);
                    if (controlType.intValue == 0)
                    {
                        goState.intValue = EditorGUILayout.Popup("状态", goState.intValue, new[] { "激活", "关闭" });
                    }
                    else
                    {
                        blendShapeName.stringValue = EditorGUILayout.TextField("BlendShape", blendShapeName.stringValue);
                        bsState.intValue = EditorGUILayout.Popup("状态", bsState.intValue, new[] { "0", "100" });
                    }
                }
                else
                {
                    var blendShapeName = item.FindPropertyRelative("blendShapeName");
                    var direction = item.FindPropertyRelative("direction");
                    var splitBlend = item.FindPropertyRelative("splitBlendShape");
                    var secondaryName = item.FindPropertyRelative("secondaryBlendShapeName");
                    var secondaryDir = item.FindPropertyRelative("secondaryDirection");

                    blendShapeName.stringValue = EditorGUILayout.TextField("BlendShape", blendShapeName.stringValue);
                    direction.intValue = EditorGUILayout.Popup("方向", direction.intValue, new[] { "0->100", "100->0" });
                    splitBlend.boolValue = EditorGUILayout.ToggleLeft("二分模式", splitBlend.boolValue);
                    if (splitBlend.boolValue)
                    {
                        secondaryName.stringValue = EditorGUILayout.TextField("BlendShape2", secondaryName.stringValue);
                        secondaryDir.intValue = EditorGUILayout.Popup("方向2", secondaryDir.intValue, new[] { "0->100", "100->0" });
                    }
                }

                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("添加目标"))
            {
                listProp.arraySize++;
            }
        }

        private void DrawStringList(SerializedProperty listProp, string label)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            if (listProp.arraySize == 0)
                listProp.arraySize = 1;
            for (int i = 0; i < listProp.arraySize; i++)
            {
                EditorGUILayout.BeginHorizontal();
                listProp.GetArrayElementAtIndex(i).stringValue = EditorGUILayout.TextField($"项 {i + 1}", listProp.GetArrayElementAtIndex(i).stringValue);
                if (GUILayout.Button("-", GUILayout.Width(22)))
                {
                    listProp.DeleteArrayElementAtIndex(i);
                    if (listProp.arraySize == 0) listProp.arraySize = 1;
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("添加菜单项"))
            {
                listProp.arraySize++;
            }
        }

    }

#if AAO_VRCSDK3_AVATARS && MVA_AAO_SUPPORT
    [ComponentInformation(typeof(QuickToggleConfig))]
    internal sealed class QuickToggleConfigComponentInformation : ComponentInformation<QuickToggleConfig>
    {
        protected override void CollectDependency(QuickToggleConfig component, ComponentDependencyCollector collector)
        {
            if (component == null) return;

            collector.MarkEntrypoint();

            var descriptor = component.targetAvatar != null
                ? component.targetAvatar
                : component.GetComponent<VRCAvatarDescriptor>();

            if (descriptor != null)
            {
                collector.AddDependency(descriptor).EvenIfDependantDisabled();
            }
        }
    }
#endif
}
