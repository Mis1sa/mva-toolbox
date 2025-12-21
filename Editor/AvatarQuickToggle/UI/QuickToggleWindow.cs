using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using MVA.Toolbox.Public;
using MVA.Toolbox.AvatarQuickToggle;
using MVA.Toolbox.AvatarQuickToggle.Workflows;
using TargetItem = MVA.Toolbox.AvatarQuickToggle.ToggleConfig.TargetItem;
using IntStateGroup = MVA.Toolbox.AvatarQuickToggle.ToggleConfig.IntStateGroup;

namespace MVA.Toolbox.AvatarQuickToggle.Editor
{
    public class QuickToggleWindow : EditorWindow
    {
        // 顶部对象：当前操作的 Avatar 及其 QuickToggleConfig
        private VRCAvatarDescriptor targetAvatarDescriptor;
        private QuickToggleConfig loadedConfigComponent;
        private AnimatorController fxController;
        private VRCExpressionParameters expressionParameters;
        private VRCExpressionsMenu expressionsMenu;

        // “从配置脚本编辑”相关的选择与锁定状态
        private readonly List<QuickToggleConfig.LayerConfig> availableConfigEntries = new List<QuickToggleConfig.LayerConfig>();
        private string[] configEntryOptions = System.Array.Empty<string>();
        private int selectedConfigEntryIndex = -1;
        private bool isEditingConfigEntry = false;
        private int lockedSwitchType = -1; // 0=Bool,1=Int,2=Float
        private string lockedLayerName = null;

        // 核心 UI 配置状态
        private int selectedLayerType = 0;
        private string layerName = "";
        private string parameterName = "";
        private string menuControlName = "Toggle";
        private string boolMenuItemName = string.Empty;
        private string floatMenuItemName = string.Empty;
        private string intSubMenuName = string.Empty;
        private readonly List<string> intMenuItemNames = new List<string>();
        private bool overwriteLayer;
        private bool overwriteParameter;
        private int writeDefaultSetting = 0; // 0=Auto,1=On,2=Off
        private bool createMenuControl = true;
        private bool savedParameter = true;
        private bool syncedParameter = true;
        private bool useNDMFMode = true;
        private string clipSavePath = ToolboxUtils.GetAqtRootFolder();

        // 参数默认值
        private int defaultStateSelection = 0; // Bool: 0=ON,1=OFF
        private int defaultIntValue = 0;
        private float defaultFloatValue = 0f;

        // 各类型开关的目标数据
        private readonly List<TargetItem> boolTargets = new List<TargetItem>();
        private readonly List<IntStateGroup> intGroups = new List<IntStateGroup>();
        private readonly List<TargetItem> floatTargets = new List<TargetItem>();
        private bool editInWDOnMode = false;
        private int pendingIntWDConversion = 0; // -1: 转为 WD Off, 1: 转为 WD On
        private bool useConfigScript = false;
        private string currentConfigDisplayName = string.Empty;

        private Vector2 scroll;

        // 覆盖/菜单选择相关的缓存 UI 数据
        private string[] availableLayerNames = System.Array.Empty<string>();
        private int selectedLayerPopupIndex = -1;
        private string[] availableParameterNames = System.Array.Empty<string>();
        private int selectedParameterPopupIndex = -1;
        private string[] availableMenuPaths = System.Array.Empty<string>();
        private int selectedMenuPathIndex = 0; // 0 means root
        private string currentMenuPath = "/";
        private Dictionary<string, VRCExpressionsMenu> menuPathMap = new Dictionary<string, VRCExpressionsMenu>();
        private bool menuExpanded = true;
        private const float W_OBJECT = 100f;
        private const float W_MODE = 100f;
        private const float W_BLEND = 160f;
        private const float W_LABEL = 30f;
        private const float W_STATE = 80f;
        private const float W_SPLIT = 90f;
        private const float W_BTN = 24f;

        // 预览相关状态
        private bool isPreviewing = false;
        private int previewBoolValue = 0;
        private int previewIntValue = 0;
        private float previewFloatValue = 0f;
        private int targetsVersion = 0;
        private readonly PreviewStateManager previewState = new PreviewStateManager();
        private bool hasPreviewSnapshot = false;

        [MenuItem("Tools/MVA Toolbox/Avatar Quick Toggle", false, 1)]
        public static void Open()
        {
            GetWindow<QuickToggleWindow>("Avatar Quick Toggle");
        }

        public static void RefreshCachedAvatarDataIfOpen()
        {
            var window = GetWindow<QuickToggleWindow>(false, null, false);
            if (window == null) return;

            // 若窗口已选择 Avatar，则刷新其缓存数据（由 DirectApplyWorkflow 调用）
            if (window.targetAvatarDescriptor != null)
            {
                window.LoadAvatarData(window.targetAvatarDescriptor);
                window.Repaint();
            }
        }

        private void OnEnable() {
            minSize = new Vector2(550f, 600f);

            // 使用 Unity 内置样式，避免热重载后自定义纹理失效
        }
        private void OnDisable()
        {
            if (isPreviewing)
            {
                StopPreview();
            }
            else if (hasPreviewSnapshot)
            {
                previewState.RestorePreviewSnapshot();
                hasPreviewSnapshot = false;
            }
            previewState.Dispose();
        }
        private void OnGUI()
        {
            DrawTopObjectField();
            if (targetAvatarDescriptor == null)
            {
                // 初始状态：未拖入有效对象，仅显示顶部 ObjectField
                return;
            }

            // Avatar 已确定后，根据其根物体上的 QuickToggleConfig 决定是否可以使用“配置脚本”
            DrawConfigScriptSection();

            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
            {
                // 当勾选了“配置脚本”但尚未选中具体项目时，禁用下面所有选项，防止在未绑定具体配置前修改设置
                using (new EditorGUI.DisabledScope(useConfigScript && !isEditingConfigEntry))
                {
                    if (pendingIntWDConversion != 0)
                    {
                        int conversion = pendingIntWDConversion;
                        pendingIntWDConversion = 0;
                        if (conversion > 0)
                            ApplyWDConversion(true);
                        else if (conversion < 0)
                            ApplyWDConversion(false);
                        Repaint();
                    }
                    using (var sv = new EditorGUILayout.ScrollViewScope(scroll, GUILayout.ExpandHeight(true)))
                    {
                        scroll = sv.scrollPosition;
                        DrawCoreSettings();
                        DrawTargetObjects();
                    }
                    GUILayout.FlexibleSpace();

                    // 预览区域独立成块，固定显示预览开关与“预览值”
                    DrawPreviewBlock();

                    // NDMF 模式勾选与配置名称，在预览块下方、操作按钮上方
                    DrawModeAndConfigNameRow();

                    // 操作按钮
                    DrawActions();
                }
            }
        }

        private void DrawTopObjectField()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("请拖入场景中的 Avatar", EditorStyles.boldLabel);
            UnityEngine.Object shown = targetAvatarDescriptor ? (UnityEngine.Object)targetAvatarDescriptor.gameObject : null;
            var newObj = EditorGUILayout.ObjectField("Avatar", shown, typeof(UnityEngine.Object), true);
            if (newObj == shown)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            var av = ToolboxUtils.GetAvatarDescriptor(newObj);
            if (av != null)
            {
                targetAvatarDescriptor = av;
                loadedConfigComponent = null;
                isEditingConfigEntry = false;
                lockedLayerName = null;
                lockedSwitchType = -1;
                LoadAvatarData(targetAvatarDescriptor);
            }
            else
            {
                targetAvatarDescriptor = null;
                loadedConfigComponent = null;
                availableConfigEntries.Clear();
                configEntryOptions = System.Array.Empty<string>();
                selectedConfigEntryIndex = -1;
                isEditingConfigEntry = false;
                lockedLayerName = null;
                lockedSwitchType = -1;
                fxController = null;
                expressionParameters = null;
                expressionsMenu = null;
                availableLayerNames = System.Array.Empty<string>();
                availableParameterNames = System.Array.Empty<string>();
                availableMenuPaths = System.Array.Empty<string>();
                EditorGUILayout.EndVertical();
                return;
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawConfigScriptSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GameObject avatarGO = targetAvatarDescriptor != null ? targetAvatarDescriptor.gameObject : null;
            var cfgOnAvatar = avatarGO != null ? avatarGO.GetComponent<QuickToggleConfig>() : null;

            // 当 Avatar 上的配置脚本发生变化时，刷新可用项目列表
            if (cfgOnAvatar != loadedConfigComponent)
            {
                loadedConfigComponent = cfgOnAvatar;
                availableConfigEntries.Clear();
                configEntryOptions = System.Array.Empty<string>();
                selectedConfigEntryIndex = -1;
                isEditingConfigEntry = false;
                lockedLayerName = null;
                lockedSwitchType = -1;

                if (loadedConfigComponent != null && loadedConfigComponent.layerConfigs != null && loadedConfigComponent.layerConfigs.Count > 0)
                {
                    availableConfigEntries.AddRange(loadedConfigComponent.layerConfigs);
                    configEntryOptions = availableConfigEntries
                        .Select(e =>
                        {
                            if (!string.IsNullOrEmpty(e.displayName)) return e.displayName;
                            if (!string.IsNullOrEmpty(e.layerName)) return e.layerName;
                            return "(未命名层)";
                        })
                        .ToArray();
                }
            }

            bool hasConfig = loadedConfigComponent != null;
            using (new EditorGUI.DisabledScope(!hasConfig))
            {
                bool prevUseConfig = useConfigScript;
                bool newUseConfig = EditorGUILayout.ToggleLeft("配置脚本", useConfigScript);
                if (hasConfig)
                {
                    useConfigScript = newUseConfig;
                }
                else
                {
                    useConfigScript = false;
                }

                // 当从勾选状态切换为未勾选时，自动退出编辑模式并清空当前选择
                if (hasConfig && prevUseConfig && !useConfigScript)
                {
                    selectedConfigEntryIndex = -1;
                    ExitEditFromConfig();
                }
            }

            if (!hasConfig)
            {
                EditorGUILayout.HelpBox("当前Avatar没有AQT配置", MessageType.Info);
            }
            else if (useConfigScript)
            {
                DrawConfigEntrySelector();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawConfigEntrySelector()
        {
            bool avatarMismatch = loadedConfigComponent != null && targetAvatarDescriptor != null && loadedConfigComponent.targetAvatar != targetAvatarDescriptor;
            if (availableConfigEntries.Count == 0 && !avatarMismatch)
            {
                EditorGUILayout.HelpBox("该 QuickToggleConfig 暂无项目。请在生成时创建。", MessageType.Info);
                return;
            }
            EditorGUILayout.Space();
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("NDMF 项目选择", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(avatarMismatch))
            {
                int newIndex = EditorGUILayout.Popup("选择项目", selectedConfigEntryIndex, configEntryOptions);
                if (newIndex != selectedConfigEntryIndex)
                {
                    selectedConfigEntryIndex = newIndex;
                    if (selectedConfigEntryIndex >= 0 && selectedConfigEntryIndex < availableConfigEntries.Count)
                    {
                        var entry = availableConfigEntries[selectedConfigEntryIndex];
                        if (string.IsNullOrEmpty(entry.layerName) || string.IsNullOrEmpty(entry.parameterName))
                        {
                            EditorGUILayout.HelpBox("所选项目未完整配置（缺少层级名称或参数名称），请先在生成界面补全后再编辑。", MessageType.Warning);
                        }
                        else
                        {
                            EnterEditFromConfig(entry);
                        }
                    }
                }
            }
            if (avatarMismatch)
            {
                EditorGUILayout.HelpBox("该 QuickToggleConfig 的 Avatar 与当前窗口不一致，请拖入对应 Avatar 或该 Avatar 的 QuickToggleConfig。", MessageType.Warning);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawCoreSettings()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            using (new EditorGUI.DisabledScope(isEditingConfigEntry))
            {
                int prevType = selectedLayerType;
                selectedLayerType = GUILayout.Toolbar(isEditingConfigEntry ? lockedSwitchType : selectedLayerType, new[] {"Bool Switch","Int Switch","Float Switch"});
                if (isEditingConfigEntry) selectedLayerType = lockedSwitchType;
                if (prevType != selectedLayerType)
                {
                    RefreshAvailableParameterNames();
                    EnsureDefaultTargets();
                    if (isPreviewing) StopPreview();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                float prevLabel = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = 80f;

                if (!overwriteLayer)
                {
                    layerName = EditorGUILayout.TextField("层级名称", layerName);
                }
                else
                {
                    using (new EditorGUI.DisabledScope(availableLayerNames.Length == 0))
                    {
                        selectedLayerPopupIndex = Mathf.Clamp(selectedLayerPopupIndex, -1, availableLayerNames.Length - 1);

                        int matchingIndex = -1;
                        if (!string.IsNullOrEmpty(layerName) && availableLayerNames != null && availableLayerNames.Length > 0)
                        {
                            matchingIndex = System.Array.IndexOf(availableLayerNames, layerName);
                        }

                        bool originalMissing = !string.IsNullOrEmpty(layerName) && matchingIndex < 0;
                        string[] layerOptions;
                        int displayIndex;

                        if (availableLayerNames.Length == 0)
                        {
                            // 无可用层级时，如果有原名称则显示“[原名称] 缺失”，否则显示占位说明
                            string label = originalMissing ? $"[{layerName}] 缺失" : "(无可用层级)";
                            layerOptions = new[] { label };
                            displayIndex = 0;
                            selectedLayerPopupIndex = -1;
                        }
                        else if (originalMissing)
                        {
                            // 原来配置的层级名不在当前列表中：在首项显示“[原名称] 缺失”，其余为实际层级
                            layerOptions = new string[availableLayerNames.Length + 1];
                            layerOptions[0] = $"[{layerName}] 缺失";
                            System.Array.Copy(availableLayerNames, 0, layerOptions, 1, availableLayerNames.Length);

                            // 在“缺失”状态下强制保持逻辑索引为 -1，对应 UI 的第 0 项
                            selectedLayerPopupIndex = -1;
                            displayIndex = 0;

                            int newIdx = EditorGUILayout.Popup("层级名称", displayIndex, layerOptions);
                            if (newIdx > 0)
                            {
                                selectedLayerPopupIndex = newIdx - 1;
                                if (selectedLayerPopupIndex >= 0 && selectedLayerPopupIndex < availableLayerNames.Length)
                                {
                                    layerName = availableLayerNames[selectedLayerPopupIndex];
                                }
                            }
                            else
                            {
                                // 保持“缺失”占位，不修改 layerName
                            }
                            goto AfterLayerPopup;
                        }
                        else
                        {
                            // 原层级仍存在：不添加额外占位项，只在索引无效时预选匹配项
                            layerOptions = availableLayerNames;
                            if (matchingIndex >= 0 && (selectedLayerPopupIndex < 0 || selectedLayerPopupIndex >= availableLayerNames.Length))
                            {
                                selectedLayerPopupIndex = matchingIndex;
                            }
                            displayIndex = Mathf.Clamp(selectedLayerPopupIndex, 0, availableLayerNames.Length - 1);
                        }

                        int changedIdx = EditorGUILayout.Popup("层级名称", displayIndex, layerOptions);
                        if (changedIdx != displayIndex)
                        {
                            selectedLayerPopupIndex = Mathf.Clamp(changedIdx, 0, availableLayerNames.Length - 1);
                            if (selectedLayerPopupIndex >= 0 && selectedLayerPopupIndex < availableLayerNames.Length)
                            {
                                layerName = availableLayerNames[selectedLayerPopupIndex];
                            }
                        }

AfterLayerPopup: ;
                    }
                }

                bool newOverwriteLayer = EditorGUILayout.ToggleLeft("覆盖层级", overwriteLayer, GUILayout.Width(100));
                if (newOverwriteLayer != overwriteLayer)
                {
                    overwriteLayer = newOverwriteLayer;
                    if (overwriteLayer)
                    {
                        // 当首次勾选覆盖层级时，确保从已有层级中选择一个有效名称并写回 layerName
                        EnsureLayerSelection();
                    }
                }
                EditorGUIUtility.labelWidth = prevLabel;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                float prevLabel = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = 80f;

                if (!overwriteParameter)
                {
                    parameterName = EditorGUILayout.TextField("参数名称", parameterName);
                }
                else
                {
                    using (new EditorGUI.DisabledScope(availableParameterNames.Length == 0))
                    {
                        selectedParameterPopupIndex = Mathf.Clamp(selectedParameterPopupIndex, -1, availableParameterNames.Length - 1);

                        int matchingIndex = -1;
                        if (!string.IsNullOrEmpty(parameterName) && availableParameterNames != null && availableParameterNames.Length > 0)
                        {
                            matchingIndex = System.Array.IndexOf(availableParameterNames, parameterName);
                        }

                        bool originalMissing = !string.IsNullOrEmpty(parameterName) && matchingIndex < 0;
                        string[] paramOptions;
                        int displayIndex;

                        if (availableParameterNames.Length == 0)
                        {
                            string label = originalMissing ? $"[{parameterName}] 缺失" : "(无可用参数)";
                            paramOptions = new[] { label };
                            displayIndex = 0;
                            selectedParameterPopupIndex = -1;
                        }
                        else if (originalMissing)
                        {
                            // 原参数名不在当前列表：首项显示 "[原名] 缺失"，其余为实际参数
                            paramOptions = new string[availableParameterNames.Length + 1];
                            paramOptions[0] = $"[{parameterName}] 缺失";
                            System.Array.Copy(availableParameterNames, 0, paramOptions, 1, availableParameterNames.Length);

                            selectedParameterPopupIndex = -1;
                            displayIndex = 0;

                            int newIdx = EditorGUILayout.Popup("参数名称", displayIndex, paramOptions);
                            if (newIdx > 0)
                            {
                                selectedParameterPopupIndex = newIdx - 1;
                                if (selectedParameterPopupIndex >= 0 && selectedParameterPopupIndex < availableParameterNames.Length)
                                {
                                    ApplySelectedParameterNameFromPopup();
                                }
                            }
                            else
                            {
                                // 仍处于“缺失”占位，不自动修改 parameterName
                            }
                            goto AfterParameterPopup;
                        }
                        else
                        {
                            // 原参数仍存在：不添加占位项，只在索引无效时预选匹配项
                            paramOptions = availableParameterNames;
                            if (matchingIndex >= 0 && (selectedParameterPopupIndex < 0 || selectedParameterPopupIndex >= availableParameterNames.Length))
                            {
                                selectedParameterPopupIndex = matchingIndex;
                            }
                            displayIndex = Mathf.Clamp(selectedParameterPopupIndex, 0, availableParameterNames.Length - 1);
                        }

                        int changedIdx = EditorGUILayout.Popup("参数名称", displayIndex, paramOptions);
                        if (changedIdx != displayIndex)
                        {
                            selectedParameterPopupIndex = Mathf.Clamp(changedIdx, 0, availableParameterNames.Length - 1);
                            if (selectedParameterPopupIndex >= 0 && selectedParameterPopupIndex < availableParameterNames.Length)
                            {
                                ApplySelectedParameterNameFromPopup();
                            }
                        }

AfterParameterPopup: ;
                    }
                }

                bool newOverwriteParameter = EditorGUILayout.ToggleLeft("覆盖参数", overwriteParameter, GUILayout.Width(100));
                if (newOverwriteParameter != overwriteParameter)
                {
                    overwriteParameter = newOverwriteParameter;
                    if (overwriteParameter)
                    {
                        EnsureParameterSelection();
                    }
                    else
                    {
                        selectedParameterPopupIndex = -1;
                    }
                }
                EditorGUIUtility.labelWidth = prevLabel;
            }

            writeDefaultSetting = EditorGUILayout.Popup("Write Defaults", writeDefaultSetting, new[] {"Auto","On","Off"});

            if (selectedLayerType == 0)
                defaultStateSelection = EditorGUILayout.IntSlider("参数默认值", defaultStateSelection, 0, 1);
            else if (selectedLayerType == 1)
                defaultIntValue = EditorGUILayout.IntSlider("参数默认值", defaultIntValue, 0, Mathf.Max(0, intGroups.Count - 1));
            else
                defaultFloatValue = EditorGUILayout.Slider("参数默认值", defaultFloatValue, 0f, 1f);

            using (new EditorGUILayout.HorizontalScope())
            {
                string newPath = EditorGUILayout.TextField("动画剪辑路径", clipSavePath);
                if (!string.Equals(newPath, clipSavePath, StringComparison.Ordinal))
                {
                    clipSavePath = NormalizeClipRootPath(newPath);
                }

                if (GUILayout.Button("浏览", GUILayout.Width(60f)))
                {
                    string startPath = Application.dataPath;
                    if (!string.IsNullOrWhiteSpace(clipSavePath) && clipSavePath.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
                    {
                        string relative = clipSavePath.Length > 6 && clipSavePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                            ? clipSavePath.Substring(7)
                            : string.Empty;
                        string candidate = string.IsNullOrEmpty(relative) ? Application.dataPath : Path.Combine(Application.dataPath, relative);
                        if (Directory.Exists(candidate))
                        {
                            startPath = candidate;
                        }
                    }

                    string selected = EditorUtility.OpenFolderPanel("选择动画保存文件夹", startPath, "");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        if (selected.StartsWith(Application.dataPath, StringComparison.OrdinalIgnoreCase))
                        {
                            string relative = selected.Substring(Application.dataPath.Length).Replace('\\', '/');
                            clipSavePath = NormalizeClipRootPath(string.IsNullOrEmpty(relative) ? "Assets" : $"Assets{relative}");
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("无效路径", "请选择位于 Assets 目录内的文件夹。", "确定");
                        }
                    }
                }
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
            DrawMenuSettings();
        }

        private void DrawMenuSettings()
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.BeginHorizontal();
            menuExpanded = EditorGUILayout.Toggle(GUIContent.none, menuExpanded, GUILayout.Width(13));
            GUILayout.Label("菜单设置", EditorStyles.boldLabel);
            GUILayout.EndHorizontal();

            createMenuControl = menuExpanded;

            if (menuExpanded)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                if (expressionsMenu != null && availableMenuPaths != null && availableMenuPaths.Length > 0)
                {
                    int newIdx = EditorGUILayout.Popup("父菜单", selectedMenuPathIndex, availableMenuPaths);
                    if (newIdx != selectedMenuPathIndex)
                    {
                        selectedMenuPathIndex = newIdx;
                        if (selectedMenuPathIndex >= 0 && selectedMenuPathIndex < availableMenuPaths.Length)
                            currentMenuPath = availableMenuPaths[selectedMenuPathIndex];
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("未找到表情菜单或子菜单。", MessageType.Warning);
                }
                EditorGUILayout.Space();
                if (selectedLayerType == 0)
                {
                    boolMenuItemName = EditorGUILayout.TextField("菜单项名称", boolMenuItemName);
                }
                else if (selectedLayerType == 1)
                {
                    intSubMenuName = EditorGUILayout.TextField("子菜单名称", intSubMenuName);
                    EnsureIntMenuNameCapacity();
                    EditorGUI.indentLevel++;
                    for (int g = 0; g < intGroups.Count; g++)
                    {
                        string label = $"菜单项名称 (组 {g})";
                        intMenuItemNames[g] = EditorGUILayout.TextField(label, intMenuItemNames[g]);
                    }
                    EditorGUI.indentLevel--;
                }
                else
                {
                    floatMenuItemName = EditorGUILayout.TextField("菜单项名称", floatMenuItemName);
                }
                EditorGUILayout.Space();
                GUILayout.Label("参数设置", EditorStyles.boldLabel);
                savedParameter = EditorGUILayout.Toggle("参数保存", savedParameter);
                syncedParameter = EditorGUILayout.Toggle("参数同步", syncedParameter);
                EditorGUILayout.EndVertical();
            }
            GUILayout.EndVertical();
        }

        private void DrawTargetObjects()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("目标物体", EditorStyles.boldLabel);
            if (selectedLayerType == 1)
            {
                bool previousWDMode = editInWDOnMode;
                bool toggledWDMode = EditorGUILayout.ToggleLeft("在 WD on 下编辑", previousWDMode);
                editInWDOnMode = toggledWDMode;
                if (toggledWDMode != previousWDMode)
                {
                    pendingIntWDConversion = toggledWDMode ? 1 : -1;
                }
                if (editInWDOnMode)
                {
                    EditorGUILayout.HelpBox("注意：生成的动画将仅能在 Write Defaults On 下正常播放。", MessageType.Warning);
                }
            }
            EnsureDefaultTargets();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            switch (selectedLayerType)
            {
                case 0: DrawBoolTargets(); break;
                case 1: DrawIntTargets(); break;
                case 2: DrawFloatTargets(); break;
            }
            EditorGUILayout.EndVertical();
            if (isPreviewing)
            {
                if (selectedLayerType == 0) ApplyPreviewStateBool(previewBoolValue);
                else if (selectedLayerType == 1) ApplyPreviewStateInt(previewIntValue);
                else ApplyPreviewStateFloat(previewFloatValue);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawBoolTargets()
        {
            float btnSize = EditorGUIUtility.singleLineHeight;
            int removeAt = -1;
            for (int i = 0; i < boolTargets.Count; i++)
            {
                var item = boolTargets[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                var newBoolTarget = (GameObject)EditorGUILayout.ObjectField(item.targetObject, typeof(GameObject), true, GUILayout.Width(W_OBJECT));
                item.targetObject = ToolboxUtils.ResolveMergeNodeTarget(newBoolTarget);
                if (item.targetObject != null)
                {
                    // 计算是否允许 GameObject 与可用 BlendShape 名称
                    bool hasGOElsewhere = false;
                    bool hasGOInPrevious = false;
                    for (int k = 0; k < boolTargets.Count; k++)
                    {
                        if (k == i) continue;
                        var other = boolTargets[k];
                        if (other.targetObject == item.targetObject && other.controlType == 0)
                        {
                            hasGOElsewhere = true;
                            if (k < i) hasGOInPrevious = true;
                        }
                    }
                    var allNames = GetBlendShapeNames(item.targetObject);
                    bool noBlendShapes = (allNames.Length == 0) || (allNames.Length == 1 && allNames[0] == "(None)");
                    var used = new System.Collections.Generic.HashSet<string>();
                    for (int k = 0; k < boolTargets.Count; k++)
                    {
                        if (k == i) continue;
                        var other = boolTargets[k];
                        if (other.targetObject == item.targetObject && other.controlType == 1 && !string.IsNullOrEmpty(other.blendShapeName) && other.blendShapeName != "(None)")
                            used.Add(other.blendShapeName);
                    }
                    var availableList = new List<string>();
                    for (int n = 0; n < allNames.Length; n++)
                    {
                        string nm = allNames[n];
                        if (nm == "(None)") continue;
                        if (!used.Contains(nm) || nm == item.blendShapeName) availableList.Add(nm);
                    }
                    string[] available = availableList.ToArray();

                    if (item.controlType == 1 && (available.Length == 0 || (!string.IsNullOrEmpty(item.blendShapeName) && System.Array.IndexOf(available, item.blendShapeName) < 0)))
                    {
                        item.controlType = 0;
                        item.blendShapeName = null;
                    }

                    bool goAvailable = !hasGOElsewhere;
                    bool blendAvailable = available.Length > 0;
                    bool hasValidGO = item.controlType == 0 && !hasGOInPrevious;
                    bool hasValidBlend = item.controlType == 1 && !string.IsNullOrEmpty(item.blendShapeName) && System.Array.IndexOf(available, item.blendShapeName) >= 0;
                    bool allowGOSelect = goAvailable || hasValidGO;
                    bool allowBlendSelect = blendAvailable || hasValidBlend;
                    bool showOccupied = !allowGOSelect && !allowBlendSelect;

                    if (showOccupied)
                    {
                        var occupiedRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
                        EditorGUI.HelpBox(occupiedRect, "该物体的所有属性已被占用", MessageType.Warning);
                    }
                    else
                    {
                        // 模式选择（限制可选项）
                        if (allowGOSelect && allowBlendSelect)
                            item.controlType = EditorGUILayout.Popup(item.controlType, new[] {"GameObject","BlendShape"}, GUILayout.Width(W_MODE));
                        else if (allowGOSelect && !allowBlendSelect)
                        {
                            EditorGUILayout.Popup(0, new[] {"GameObject"}, GUILayout.Width(W_MODE));
                            item.controlType = 0;
                        }
                        else if (!allowGOSelect && allowBlendSelect)
                        { EditorGUILayout.Popup(0, new[] {"BlendShape"}, GUILayout.Width(W_MODE)); item.controlType = 1; }
                        else
                        { GUILayout.Space(W_MODE); }

                        // Blend 列（GameObject模式占据剩余空间，BlendShape模式显示下拉）
                        if (item.controlType == 0)
                        {
                            DrawInvisibleObjectFieldExpand();
                        }
                        else if (allowBlendSelect && item.controlType == 1)
                        {
                            int idx = System.Array.IndexOf(available, item.blendShapeName);
                            if (idx < 0 && !string.IsNullOrEmpty(item.blendShapeName))
                            {
                                // 仅在已绑定名称失效时作为双保险退回 GameObject；名称为空时允许首次进入 BlendShape 模式
                                item.controlType = 0;
                                item.blendShapeName = null;
                                DrawInvisibleObjectFieldExpand();
                            }
                            else
                            {
                                // idx<0 且名称为空：视为“尚未选中”，允许用户从下拉中选择第一个可用形态键
                                if (idx < 0) idx = 0;
                                int newIdx = EditorGUILayout.Popup(idx, available, GUILayout.ExpandWidth(true));
                                if (available.Length > 0) item.blendShapeName = available[Mathf.Clamp(newIdx, 0, available.Length - 1)];
                            }
                        }
                        else
                        {
                            DrawInvisibleObjectFieldExpand();
                        }

                        // 状态列
                        GUILayout.Label("状态", GUILayout.Width(W_LABEL));
                        if (item.controlType == 0)
                            item.onStateActiveSelection = EditorGUILayout.Popup(item.onStateActiveSelection, new[] {"激活","关闭"}, GUILayout.Width(W_STATE));
                        else
                            item.onStateBlendShapeValue = EditorGUILayout.Popup(item.onStateBlendShapeValue, new[] {"0","100"}, GUILayout.Width(W_STATE));

                        // '+'
                        Rect plusRect = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.button, GUILayout.Width(btnSize), GUILayout.Height(btnSize));
                        if (item.controlType == 1)
                        {
                            bool hasAnyUnused = item.targetObject != null && NextUnusedBlendShapeWithinList(item.targetObject, null, boolTargets) != null;
                            using (new EditorGUI.DisabledScope(!hasAnyUnused))
                            {
                                if (GUI.Button(plusRect, "+"))
                                    TryAddAnotherBlendShapeForItem(boolTargets, i);
                            }
                        }
                        else
                        {
                            GUI.Label(plusRect, GUIContent.none);
                        }
                    }
                }
                else
                {
                    // 未指定对象时：隐藏模式特定控件，但用占位保持对齐
                    GUILayout.Space(W_MODE);
                    GUILayout.FlexibleSpace();
                    GUILayout.Space(W_LABEL + W_STATE + btnSize);
                }
                if (GUILayout.Button("-", GUILayout.Width(btnSize), GUILayout.Height(btnSize))) removeAt = i;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                boolTargets[i] = item;
            }
            if (removeAt >= 0) boolTargets.RemoveAt(removeAt);
            if (GUILayout.Button("新目标")) boolTargets.Add(new TargetItem());
        }

        private void DrawIntTargets()
        {
            float btnSize = EditorGUIUtility.singleLineHeight;
            EnsureIntMenuNameCapacity();
            int removeGroupIndex = -1;
            for (int g = 0; g < intGroups.Count; g++)
            {
                var group = intGroups[g];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                bool removeCurrentGroup = false;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"组 {g}", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("-", GUILayout.Width(btnSize), GUILayout.Height(btnSize)))
                    {
                        if (intGroups.Count > 1)
                        {
                            removeGroupIndex = g;
                            removeCurrentGroup = true;
                        }
                        else
                        {
                            group.stateName = string.Empty;
                            if (group.targetItems == null) group.targetItems = new List<TargetItem>();
                            group.targetItems.Clear();
                            group.targetItems.Add(new TargetItem());
                            if (intMenuItemNames.Count == 0) intMenuItemNames.Add(string.Empty);
                            if (g < intMenuItemNames.Count) intMenuItemNames[g] = string.Empty;
                        }
                    }
                }
                if (removeCurrentGroup)
                {
                    EditorGUILayout.EndVertical();
                    continue;
                }
                int removeAt = -1;
                for (int i = 0; i < group.targetItems.Count; i++)
                {
                    var item = group.targetItems[i];
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.BeginHorizontal();
                    bool canEditGroup = editInWDOnMode || g == 0;
                    bool showOccupied = false;

                    using (new EditorGUI.DisabledScope(!canEditGroup))
                    {
                        var newIntTarget = (GameObject)EditorGUILayout.ObjectField(item.targetObject, typeof(GameObject), true, GUILayout.Width(W_OBJECT));
                        item.targetObject = ToolboxUtils.ResolveMergeNodeTarget(newIntTarget);
                    }
                    bool hasTarget = item.targetObject != null;

                    if (hasTarget)
                    {
                        // 计算当前组内是否已存在该物体的 GameObject 模式，以及可用的 BlendShape 名称
                        bool hasGOElsewhere = false;
                        bool hasGOPrior = false;
                        for (int k = 0; k < group.targetItems.Count; k++)
                        {
                            if (k == i) continue;
                            var other = group.targetItems[k];
                            if (other.targetObject == item.targetObject && other.controlType == 0)
                            {
                                hasGOElsewhere = true;
                                if (k < i) hasGOPrior = true;
                            }
                        }

                        var allNames = GetBlendShapeNames(item.targetObject);
                        var used = new HashSet<string>();
                        for (int k = 0; k < group.targetItems.Count; k++)
                        {
                            if (k == i) continue;
                            var other = group.targetItems[k];
                            if (other.targetObject == item.targetObject && other.controlType == 1 && !string.IsNullOrEmpty(other.blendShapeName) && other.blendShapeName != "(None)")
                                used.Add(other.blendShapeName);
                        }
                        var availableList = new List<string>();
                        for (int n = 0; n < allNames.Length; n++)
                        {
                            string nm = allNames[n];
                            if (nm == "(None)") continue;
                            if (!used.Contains(nm) || nm == item.blendShapeName) availableList.Add(nm);
                        }
                        string[] available = availableList.ToArray();

                        if (item.controlType == 1 && (available.Length == 0 || (!string.IsNullOrEmpty(item.blendShapeName) && System.Array.IndexOf(available, item.blendShapeName) < 0)))
                        {
                            item.controlType = 0;
                            item.blendShapeName = null;
                        }

                        bool allowBlend = available.Length > 0;
                        bool allowGO = !hasGOElsewhere;
                        bool allowCurrentGO = (item.controlType == 0 && !hasGOPrior);
                        bool allowGOThis = allowGO || allowCurrentGO;
                        bool allowBlendThis = allowBlend || (item.controlType == 1 && !string.IsNullOrEmpty(item.blendShapeName) && System.Array.IndexOf(available, item.blendShapeName) >= 0);

                        showOccupied = !allowGOThis && !allowBlendThis;

                        using (new EditorGUI.DisabledScope(!canEditGroup))
                        {
                            if (showOccupied)
                            {
                                var occupiedRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
                                EditorGUI.HelpBox(occupiedRect, "该物体的所有属性已被占用", MessageType.Warning);
                            }
                            else
                            {
                                // 模式列
                                if (allowGOThis && allowBlendThis)
                                    item.controlType = EditorGUILayout.Popup(item.controlType, new[] {"GameObject","BlendShape"}, GUILayout.Width(W_MODE));
                                else if (allowGOThis && !allowBlendThis)
                                {
                                    EditorGUILayout.Popup(0, new[] {"GameObject"}, GUILayout.Width(W_MODE));
                                    item.controlType = 0;
                                }
                                else if (!allowGOThis && allowBlendThis)
                                { EditorGUILayout.Popup(0, new[] {"BlendShape"}, GUILayout.Width(W_MODE)); item.controlType = 1; }
                                else
                                { GUILayout.Space(W_MODE); }

                                // Blend 列
                                if (item.controlType == 0)
                                {
                                    DrawInvisibleObjectFieldExpand();
                                }
                                else if (allowBlendThis && item.controlType == 1)
                                {
                                    int idx = System.Array.IndexOf(available, item.blendShapeName);
                                    if (idx < 0 && !string.IsNullOrEmpty(item.blendShapeName))
                                    {
                                        // 仅在已绑定名称失效时作为双保险退回 GameObject；名称为空时允许首次进入 BlendShape 模式
                                        item.controlType = 0;
                                        item.blendShapeName = null;
                                        DrawInvisibleObjectFieldExpand();
                                    }
                                    else
                                    {
                                        // idx<0 且名称为空：视为“尚未选中”，允许用户从下拉中选择第一个可用形态键
                                        if (idx < 0) idx = 0;
                                        int newIdx = EditorGUILayout.Popup(idx, available, GUILayout.ExpandWidth(true));
                                        if (available.Length > 0) item.blendShapeName = available[Mathf.Clamp(newIdx, 0, available.Length - 1)];
                                    }
                                }
                                else
                                {
                                    DrawInvisibleObjectFieldExpand();
                                }
                            }
                        }
                    }
                    else
                    {
                        using (new EditorGUI.DisabledScope(!canEditGroup))
                        {
                            GUILayout.Space(W_MODE);
                            GUILayout.FlexibleSpace();
                        }
                    }

                    // 状态列
                    if (!showOccupied && item.targetObject != null)
                    {
                        GUILayout.Label("状态", GUILayout.Width(W_LABEL));
                        if (item.controlType == 0)
                            item.onStateActiveSelection = EditorGUILayout.Popup(item.onStateActiveSelection, new[] {"激活","关闭"}, GUILayout.Width(W_STATE));
                        else
                            item.onStateBlendShapeValue = EditorGUILayout.Popup(item.onStateBlendShapeValue, new[] {"0","100"}, GUILayout.Width(W_STATE));
                    }
                    else if (item.targetObject == null)
                    {
                        GUILayout.Space(W_LABEL + W_STATE);
                    }

                    // '+' 列（仅 BlendShape 模式显示按钮，否则用同宽占位）
                    Rect plusRect = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.button, GUILayout.Width(btnSize), GUILayout.Height(btnSize));
                    bool showPlus = canEditGroup && item.controlType == 1 && item.targetObject != null && !showOccupied;
                    if (showPlus)
                    {
                        bool hasAnyUnused = NextUnusedBlendShapeWithinList(item.targetObject, null, group.targetItems) != null;
                        using (new EditorGUI.DisabledScope(!hasAnyUnused))
                        {
                            if (GUI.Button(plusRect, "+"))
                                TryAddAnotherBlendShapeForIntGroup(group.targetItems, i);
                        }
                    }
                    else
                    {
                        GUI.Label(plusRect, GUIContent.none);
                    }

                    using (new EditorGUI.DisabledScope(!(editInWDOnMode || g == 0)))
                    {
                        if (GUILayout.Button("-", GUILayout.Width(btnSize), GUILayout.Height(btnSize))) removeAt = i;
                    }
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    group.targetItems[i] = item;
                }
                if (removeAt >= 0 && removeAt < group.targetItems.Count) group.targetItems.RemoveAt(removeAt);
                using (new EditorGUI.DisabledScope(!(editInWDOnMode || g == 0)))
                {
                    if (GUILayout.Button("新目标")) group.targetItems.Add(new TargetItem());
                }
                EditorGUILayout.EndVertical();
                intGroups[g] = group;
            }
            if (removeGroupIndex >= 0 && removeGroupIndex < intGroups.Count)
            {
                intGroups.RemoveAt(removeGroupIndex);
                if (removeGroupIndex < intMenuItemNames.Count)
                    intMenuItemNames.RemoveAt(removeGroupIndex);
            }
            if (!editInWDOnMode && Event.current.type == EventType.Repaint)
                IntGroupSnapshotConverter.SyncStructureFromTemplate(intGroups);
            if (GUILayout.Button("添加组"))
            {
                intGroups.Add(new IntStateGroup { targetItems = new List<TargetItem> { new TargetItem() } });
                EnsureIntMenuNameCapacity();
                OnTargetsModified();
                if (isPreviewing)
                {
                    previewIntValue = Mathf.Clamp(previewIntValue, 0, Mathf.Max(0, intGroups.Count - 1));
                    ApplyPreviewStateInt(previewIntValue);
                }
            }
        }

        private void DrawFloatTargets()
        {
            float btnSize = EditorGUIUtility.singleLineHeight;
            int removeAt = -1;
            for (int i = 0; i < floatTargets.Count; i++)
            {
                var item = floatTargets[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                var newFloatTarget = (GameObject)EditorGUILayout.ObjectField(item.targetObject, typeof(GameObject), true, GUILayout.Width(W_OBJECT));

                item.targetObject = ToolboxUtils.ResolveMergeNodeTarget(newFloatTarget);
                
                if (item.targetObject != null)
                {
                    string[] namesAll = GetBlendShapeNames(item.targetObject);
                    var allShapes = new List<string>();
                    for (int n = 0; n < namesAll.Length; n++) { if (namesAll[n] != "(None)") allShapes.Add(namesAll[n]); }
                    
                    // 统计其他条目已占用的形态键
                    var usedByOthers = new HashSet<string>();
                    for (int u = 0; u < floatTargets.Count; u++)
                    {
                        if (u == i) continue;
                        var ot = floatTargets[u];
                        if (ot == null || ot.targetObject != item.targetObject) continue;
                        if (!string.IsNullOrEmpty(ot.blendShapeName)) usedByOthers.Add(ot.blendShapeName);
                        if (ot.splitBlendShape && !string.IsNullOrEmpty(ot.secondaryBlendShapeName)) usedByOthers.Add(ot.secondaryBlendShapeName);
                    }
                    
                    bool noShapes = allShapes.Count == 0;
                    bool allOccupied = !noShapes && usedByOthers.Count >= allShapes.Count && string.IsNullOrEmpty(item.blendShapeName) && string.IsNullOrEmpty(item.secondaryBlendShapeName);
                    
                    bool showOccupied = allOccupied;
                    if (noShapes || showOccupied)
                    {
                        var helpRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
                        EditorGUI.HelpBox(helpRect, noShapes ? "该物体没有Blendshape" : "该物体的所有属性已被占用", MessageType.Warning);
                    }
                    else
                    {
                        item.splitBlendShape = EditorGUILayout.ToggleLeft("二分模式", item.splitBlendShape, GUILayout.Width(W_SPLIT));
                        var primaryOptions = new List<string>();
                        if (item.splitBlendShape) primaryOptions.Add("无");
                        foreach (var shape in allShapes)
                        {
                            if (!usedByOthers.Contains(shape)) primaryOptions.Add(shape);
                        }
                        var primRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
                        int primIdx = 0;
                        if (!string.IsNullOrEmpty(item.blendShapeName))
                        {
                            int found = primaryOptions.IndexOf(item.blendShapeName);
                            // 若当前主形态键不在列表中：
                            // - 非二分模式：视为“无”，字段置空，但 UI 无“无”选项，只是重置为未选中；
                            // - 二分模式：found<0 时 primIdx 保持 0，对应 primaryOptions[0]="无"。
                            primIdx = found >= 0 ? found : 0;
                            if (found < 0 && !item.splitBlendShape)
                            {
                                item.blendShapeName = null;
                            }
                        }

                        string oldPrimary = item.blendShapeName;
                        string oldSecondary = item.secondaryBlendShapeName;
                        int oldPrimaryDir = item.onStateBlendShapeValue;
                        int oldSecondaryDir = item.secondaryBlendShapeValue;

                        int newPrimIdx = EditorGUI.Popup(primRect, primIdx, primaryOptions.ToArray());
                        string newPrimary = (item.splitBlendShape && newPrimIdx == 0) ? null : primaryOptions[newPrimIdx];

                        if (isPreviewing && newPrimary != oldPrimary) { StopPreview(); StartPreview(); }

                        if (item.splitBlendShape && newPrimary == oldSecondary)
                        {
                            item.blendShapeName = newPrimary;
                            item.secondaryBlendShapeName = oldPrimary;
                            item.onStateBlendShapeValue = oldSecondaryDir;
                            item.secondaryBlendShapeValue = oldPrimaryDir;
                        }
                        else
                        {
                            item.blendShapeName = newPrimary;
                        }

                        GUILayout.Label("方向", GUILayout.Width(W_LABEL));
                        item.onStateBlendShapeValue = EditorGUILayout.Popup(item.onStateBlendShapeValue, new[] {"0->100","100->0"}, GUILayout.Width(W_STATE));

                        bool hasAnyUnused = item.targetObject != null && NextUnusedBlendShapeWithinList(item.targetObject, null, floatTargets, true) != null;
                        using (new EditorGUI.DisabledScope(!hasAnyUnused))
                        {
                            if (GUILayout.Button("+", GUILayout.Width(btnSize), GUILayout.Height(btnSize)))
                                TryAddAnotherBlendShapeForItem(floatTargets, i, true);
                        }
                    }
                }
                // 无目标对象时不显示任何控件，直接到移除按钮
                if (item.targetObject == null)
                {
                    GUILayout.Space(W_SPLIT);
                    GUILayout.FlexibleSpace();
                }

                if (GUILayout.Button("-", GUILayout.Width(btnSize), GUILayout.Height(btnSize))) removeAt = i;
                EditorGUILayout.EndHorizontal();

                // 二分模式第二行
                if (item.targetObject != null && item.splitBlendShape)
                {
                    string[] namesAll2 = GetBlendShapeNames(item.targetObject);
                    var allShapes2 = new List<string>();
                    for (int n = 0; n < namesAll2.Length; n++) { if (namesAll2[n] != "(None)") allShapes2.Add(namesAll2[n]); }
                    
                    var usedByOthers2 = new HashSet<string>();
                    for (int u = 0; u < floatTargets.Count; u++)
                    {
                        if (u == i) continue;
                        var ot = floatTargets[u];
                        if (ot == null || ot.targetObject != item.targetObject) continue;
                        if (!string.IsNullOrEmpty(ot.blendShapeName)) usedByOthers2.Add(ot.blendShapeName);
                        if (ot.splitBlendShape && !string.IsNullOrEmpty(ot.secondaryBlendShapeName)) usedByOthers2.Add(ot.secondaryBlendShapeName);
                    }
                    
                    EditorGUILayout.BeginHorizontal();
                    var prevColor = GUI.color;
                    using (new EditorGUI.DisabledScope(true))
                    {
                        GUI.color = new Color(prevColor.r, prevColor.g, prevColor.b, 0f);
                        EditorGUILayout.ObjectField(GUIContent.none, null, typeof(GameObject), true, GUILayout.Width(W_OBJECT));
                        EditorGUILayout.ToggleLeft("二分模式", false, GUILayout.Width(W_SPLIT));
                        GUI.color = prevColor;
                    }

                    string currentPrimary = item.blendShapeName;
                    string currentSecondary = item.secondaryBlendShapeName;
                    int currentPrimaryDir = item.onStateBlendShapeValue;
                    int currentSecondaryDir = item.secondaryBlendShapeValue;

                    // 副下拉："无" + 未被其他条目占用的形态键（保留本行已选）
                    var secondaryOptions = new List<string> { "无" };
                    foreach (var shape in allShapes2)
                    {
                        bool occupiedElsewhere = usedByOthers2.Contains(shape);
                        bool isCurrentPrimary = !string.IsNullOrEmpty(currentPrimary) && shape == currentPrimary;
                        bool isCurrentSecondary = !string.IsNullOrEmpty(currentSecondary) && shape == currentSecondary;
                        if (!occupiedElsewhere || isCurrentPrimary || isCurrentSecondary)
                        {
                            secondaryOptions.Add(shape);
                        }
                    }

                    var secRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
                    int secIdx = 0;
                    if (!string.IsNullOrEmpty(item.secondaryBlendShapeName))
                    {
                        int found = secondaryOptions.IndexOf(item.secondaryBlendShapeName);
                        secIdx = found >= 0 ? found : 0;
                    }
                    
                    int newSecIdx = EditorGUI.Popup(secRect, secIdx, secondaryOptions.ToArray());
                    string newSecondary = (newSecIdx == 0) ? null : secondaryOptions[newSecIdx];
                    
                    if (isPreviewing && newSecondary != currentSecondary) { StopPreview(); StartPreview(); }
                    
                    // 互换逻辑：副选择了当前主
                    if (newSecondary == currentPrimary)
                    {
                        item.secondaryBlendShapeName = currentPrimary;
                        item.secondaryBlendShapeValue = currentPrimaryDir;
                        item.blendShapeName = currentSecondary;
                        item.onStateBlendShapeValue = currentSecondaryDir;
                    }
                    else
                    {
                        // 占用校验
                        if (!string.IsNullOrEmpty(newSecondary) && usedByOthers2.Contains(newSecondary) && newSecondary != currentPrimary)
                        {
                            EditorUtility.DisplayDialog("已被占用", "该形态键已被列表中其他目标选择，无法重复选择。", "确定");
                        }
                        else
                        {
                            item.secondaryBlendShapeName = newSecondary;
                        }
                    }
                    
                    GUILayout.Label("方向", GUILayout.Width(W_LABEL));
                    item.secondaryBlendShapeValue = EditorGUILayout.Popup(item.secondaryBlendShapeValue, new[] {"0->100","100->0"}, GUILayout.Width(W_STATE));
                    Rect plusPlaceholder = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.button, GUILayout.Width(btnSize), GUILayout.Height(btnSize));
                    GUI.Label(plusPlaceholder, GUIContent.none);
                    Rect minusPlaceholder = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.button, GUILayout.Width(btnSize), GUILayout.Height(btnSize));
                    GUI.Label(minusPlaceholder, GUIContent.none);
                    EditorGUILayout.EndHorizontal();
                }
                
                EditorGUILayout.EndVertical();
                floatTargets[i] = item;
            }
            if (removeAt >= 0) floatTargets.RemoveAt(removeAt);
            if (GUILayout.Button("新目标")) floatTargets.Add(new TargetItem { controlType = 1 });
        }

        private void ApplyWDConversion(bool toWDOn)
        {
            if (selectedLayerType != 1 || intGroups.Count == 0)
            {
                pendingIntWDConversion = 0;
                return;
            }

            if (toWDOn)
            {
                IntGroupSnapshotConverter.StripDefaultEntriesForWDOn(intGroups, previewState);
                editInWDOnMode = true;
            }
            else
            {
                var rebuilt = IntGroupSnapshotConverter.RebuildWDOffGroups(intGroups, previewState);
                ReplaceIntGroups(rebuilt);
                editInWDOnMode = false;
            }

            // 应用转换后的目标，并在预览模式下立即刷新预览状态
            OnTargetsModified();
            if (isPreviewing)
            {
                previewIntValue = Mathf.Clamp(previewIntValue, 0, Mathf.Max(0, intGroups.Count - 1));
                ApplyPreviewStateInt(previewIntValue);
            }
        }

        private void ReplaceIntGroups(List<IntStateGroup> newGroups)
        {
            // 将 Int 目标组整体替换为转换后的结果
            intGroups.Clear();
            foreach (var grp in newGroups)
            {
                intGroups.Add(CloneIntGroup(grp));
            }
        }

        private IntStateGroup CloneIntGroup(IntStateGroup src)
        {
            // 克隆 Int 组结构，避免直接共享引用
            var cloned = new IntStateGroup
            {
                stateName = src.stateName,
                isFoldout = src.isFoldout,
                targetItems = new List<TargetItem>()
            };
            if (src.targetItems != null)
            {
                foreach (var it in src.targetItems)
                    cloned.targetItems.Add(CloneTargetItem(it));
            }
            return cloned;
        }

        private TargetItem CloneTargetItem(TargetItem src)
        {
            if (src == null) return new TargetItem();
            return new TargetItem
            {
                targetObject = src.targetObject,
                controlType = src.controlType,
                blendShapeName = src.blendShapeName,
                onStateActiveSelection = src.onStateActiveSelection,
                onStateBlendShapeValue = src.onStateBlendShapeValue,
                splitBlendShape = src.splitBlendShape,
                secondaryBlendShapeName = src.secondaryBlendShapeName,
                secondaryBlendShapeValue = src.secondaryBlendShapeValue
            };
        }

        private void OnTargetsModified()
        {
            targetsVersion++;
        }

        private SkinnedMeshRenderer GetSkinnedMeshRenderer(GameObject go)
        {
            if (go == null) return null;
            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr == null) smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
            return smr;
        }

        private void ApplyPreviewStateBool(int value)
        {
            previewState.ApplyBaselineDefaults();
            bool useOn = value == 1;
            foreach (var it in boolTargets)
                previewState.ApplyTargetState(it, useOn, PreviewStateManager.BlendShapePreviewMode.DiscreteToggle);
        }

        private void ApplyPreviewStateInt(int value)
        {
            if (intGroups.Count == 0) return;
            previewState.ApplyBaselineDefaults();
            int gi = Mathf.Clamp(value, 0, intGroups.Count - 1);
            var group = intGroups[gi];
            if (group?.targetItems == null) return;
            foreach (var it in group.targetItems)
                previewState.ApplyTargetState(it, true, PreviewStateManager.BlendShapePreviewMode.DiscreteOnOnly);
        }

        private void ApplyPreviewStateFloat(float t)
        {
            t = Mathf.Clamp01(t);
            previewState.ApplyBaselineDefaults();
            foreach (var it in floatTargets)
            {
                if (it == null) continue;
                previewState.ApplySplitFloatState(it, t);
            }
        }

        private string[] GetBlendShapeNames(GameObject go)
        {
            if (go == null) return System.Array.Empty<string>();

            return ToolboxUtils.GetAvailableBlendShapeNames(go);
        }

        private string NextUnusedBlendShape(GameObject go, string current)
        {
            var names = GetBlendShapeNames(go);
            foreach (var n in names)
            {
                if (string.IsNullOrEmpty(n) || n == current || n == "(None)") continue;
                bool used = false;
                foreach (var it in boolTargets)
                    if (it.targetObject == go && it.blendShapeName == n) { used = true; break; }
                foreach (var grp in intGroups)
                    foreach (var it in grp.targetItems)
                        if (it.targetObject == go && it.blendShapeName == n) { used = true; break; }
                foreach (var it in floatTargets)
                {
                    if (it.targetObject != go) continue;
                    if (it.blendShapeName == n) { used = true; break; }
                    if (it.splitBlendShape && it.secondaryBlendShapeName == n) { used = true; break; }
                }
                if (!used) return n;
            }
            return null;
        }

        private string NextUnusedBlendShapeWithinList(GameObject go, string current, List<TargetItem> list, bool includeSecondary = false)
        {
            if (go == null || list == null) return null;
            var names = GetBlendShapeNames(go);
            foreach (var n in names)
            {
                if (string.IsNullOrEmpty(n) || n == "(None)") continue;
                if (!string.IsNullOrEmpty(current) && n == current) continue;
                bool used = false;
                for (int i = 0; i < list.Count; i++)
                {
                    var it = list[i];
                    if (it == null) continue;
                    if (it.targetObject == go && it.blendShapeName == n) { used = true; break; }
                    if (includeSecondary && it.targetObject == go && it.splitBlendShape && it.secondaryBlendShapeName == n) { used = true; break; }
                }
                if (!used) return n;
            }
            return null;
        }

        private void TryAddAnotherBlendShapeForIntGroup(List<TargetItem> list, int index)
        {
            if (list == null || index < 0 || index >= list.Count) return;
            var it = list[index];
            if (it == null || it.targetObject == null) return;
            var next = NextUnusedBlendShapeWithinList(it.targetObject, it.blendShapeName, list);
            if (string.IsNullOrEmpty(next)) return;

            var copy = CloneTargetItem(it);
            copy.controlType = 1;
            copy.blendShapeName = next;
            list.Insert(index + 1, copy);
            OnTargetsModified();
            if (isPreviewing)
            {
                ApplyPreviewStateInt(Mathf.Clamp(previewIntValue, 0, Mathf.Max(0, intGroups.Count - 1)));
            }
        }

        private void DrawInvisibleObjectFieldExpand()
        {
            var prevColor = GUI.color;
            using (new EditorGUI.DisabledScope(true))
            {
                GUI.color = new Color(prevColor.r, prevColor.g, prevColor.b, 0f);
                Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, EditorStyles.objectField, GUILayout.ExpandWidth(true));
                EditorGUI.ObjectField(rect, GUIContent.none, null, typeof(UnityEngine.Object), true);
                GUI.color = prevColor;
            }
        }

        private void TryAddAnotherBlendShapeForItem(List<TargetItem> list, int index, bool includeSecondary = false)
        {
            if (index < 0 || index >= list.Count) return;
            var it = list[index];
            if (it == null || it.targetObject == null) return;
            var next = NextUnusedBlendShapeWithinList(it.targetObject, it.blendShapeName, list, includeSecondary);
            if (string.IsNullOrEmpty(next)) return;
            var copy = new TargetItem
            {
                targetObject = it.targetObject,
                controlType = 1,
                blendShapeName = next,
                onStateBlendShapeValue = it.onStateBlendShapeValue,
            };
            list.Insert(index + 1, copy);
        }

        private void DrawActions()
        {
            bool valid = !string.IsNullOrEmpty(layerName) && !string.IsNullOrEmpty(parameterName);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (isEditingConfigEntry)
                {
                    using (new EditorGUI.DisabledScope(!valid))
                    {
                        if (GUILayout.Button("保存修改", GUILayout.Height(30), GUILayout.ExpandWidth(true)))
                        {
                            OnModifyButtonClick();
                        }
                        if (GUILayout.Button("直接修改", GUILayout.Height(30), GUILayout.ExpandWidth(true)))
                        {
                            OnApplyButtonClick();
                        }
                    }
                }
                else
                {
                    using (new EditorGUI.DisabledScope(!valid))
                    {
                        // 非配置编辑模式下，根据 NDMF 模式切换单一主按钮：NDMF=生成脚本，否则=应用修改
                        string mainLabel = useNDMFMode ? "生成脚本" : "应用修改";
                        if (GUILayout.Button(mainLabel, GUILayout.Height(30), GUILayout.ExpandWidth(true)))
                        {
                            if (useNDMFMode)
                            {
                                OnGenerateButtonClick();
                            }
                            else
                            {
                                OnApplyButtonClick();
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 预览区域：固定为单行。未预览时按钮填满整行；预览中时左侧为按钮，右侧为“预览值”滑条，避免整体布局跳动。
        /// </summary>
        private void DrawPreviewBlock()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            Rect lineRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            const float buttonWidth = 80f;

            if (!isPreviewing)
            {
                // 未预览：按钮填满整行
                if (GUI.Button(lineRect, "开启预览"))
                {
                    StartPreview();
                }
            }
            else
            {
                // 预览中：左侧按钮，右侧为预览值滑条
                Rect buttonRect = new Rect(lineRect.x, lineRect.y, buttonWidth, lineRect.height);
                Rect sliderRect = new Rect(lineRect.x + buttonWidth + 4f, lineRect.y,
                    Mathf.Max(0f, lineRect.width - buttonWidth - 4f), lineRect.height);

                if (GUI.Button(buttonRect, "退出预览"))
                {
                    StopPreview();
                }
                else
                {
                    float prevLabelWidth = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = 50f;
                    if (selectedLayerType == 0)
                    {
                        int newVal = EditorGUI.IntSlider(sliderRect, new GUIContent("预览值"), previewBoolValue, 0, 1);
                        if (newVal != previewBoolValue)
                        {
                            previewBoolValue = newVal;
                        }
                        ApplyPreviewStateBool(previewBoolValue);
                    }
                    else if (selectedLayerType == 1)
                    {
                        int maxIndex = Mathf.Max(0, intGroups.Count - 1);
                        int newVal = EditorGUI.IntSlider(sliderRect, new GUIContent("预览值"), previewIntValue, 0, maxIndex);
                        if (newVal != previewIntValue)
                        {
                            previewIntValue = newVal;
                        }
                        ApplyPreviewStateInt(previewIntValue);
                    }
                    else
                    {
                        float newVal = EditorGUI.Slider(sliderRect, new GUIContent("预览值"), previewFloatValue, 0f, 1f);
                        if (!Mathf.Approximately(newVal, previewFloatValue))
                        {
                            previewFloatValue = newVal;
                        }
                        ApplyPreviewStateFloat(previewFloatValue);
                    }
                    EditorGUIUtility.labelWidth = prevLabelWidth;
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawModeAndConfigNameRow()
        {
            GUILayout.Space(2f);
            if (!isEditingConfigEntry)
            {
                // 非编辑模式：使用统一的框包裹 NDMF 勾选及配置名称区域
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                useNDMFMode = EditorGUILayout.ToggleLeft("NDMF模式", useNDMFMode);

                // 仅在勾选 NDMF 模式时才显示配置名称输入框
                if (useNDMFMode)
                {
                    currentConfigDisplayName = EditorGUILayout.TextField("配置名称", currentConfigDisplayName);
                }
                EditorGUILayout.EndVertical();
            }
            else
            {
                // 编辑模式：不显示 NDMF 勾选，但始终显示配置名称输入
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                currentConfigDisplayName = EditorGUILayout.TextField("配置名称", currentConfigDisplayName);
                EditorGUILayout.EndVertical();
            }
        }

        // 预览控制：捕获/恢复 Avatar 外观，并根据当前参数值应用预览
        private void StartPreview()
        {
            if (isPreviewing) return;
            var root = targetAvatarDescriptor ? targetAvatarDescriptor.gameObject : (loadedConfigComponent ? loadedConfigComponent.gameObject : null);
            if (root == null)
            {
                ShowNotification(new GUIContent("请先拖入 Avatar"));
                return;
            }
            previewState.CaptureAvatarSnapshot(root, true);
            hasPreviewSnapshot = true;
            isPreviewing = true;
            ApplyCurrentPreviewValues();
        }
        
        private void StopPreview()
        {
            if (!isPreviewing) return;
            previewState.RestorePreviewSnapshot();
            isPreviewing = false;
            hasPreviewSnapshot = false;
        }

        private void ApplyCurrentPreviewValues()
        {
            if (!isPreviewing) return;
            if (selectedLayerType == 0)
                ApplyPreviewStateBool(previewBoolValue);
            else if (selectedLayerType == 1)
                ApplyPreviewStateInt(previewIntValue);
            else
                ApplyPreviewStateFloat(previewFloatValue);
        }

        private IEnumerable<TargetItem> EnumerateAllTargets()
        {
            foreach (var it in boolTargets)
            {
                if (it == null) continue;
                yield return it;
            }

            foreach (var grp in intGroups)
            {
                if (grp?.targetItems == null) continue;
                foreach (var it in grp.targetItems)
                {
                    if (it == null) continue;
                    yield return it;
                }
            }

            foreach (var it in floatTargets)
            {
                if (it == null) continue;
                yield return it;
            }
        }

        private ToggleConfig GatherConfiguration()
        {
            var cfg = new ToggleConfig
            {
                avatar = targetAvatarDescriptor,
                config = new ToggleConfig.LayerConfig
                {
                    // 对于覆盖层级模式，如果 layerName 为空但已有有效下拉选项，优先从下拉中获取最终层级名
                    layerName = ResolveEffectiveLayerName(),
                    layerType = selectedLayerType,
                    parameterName = parameterName,
                    overwriteLayer = overwriteLayer,
                    overwriteParameter = overwriteParameter,
                    // 仅保存用户选择的根路径，最终层级名在生成阶段再附加
                    clipSavePath = NormalizeClipRootPath(clipSavePath),
                    writeDefaultSetting = writeDefaultSetting,
                    createMenuControl = createMenuControl,
                    menuControlName = GetCurrentMenuControlName(),
                    boolMenuItemName = boolMenuItemName,
                    floatMenuItemName = floatMenuItemName,
                    intSubMenuName = intSubMenuName,
                    intMenuItemNames = new List<string>(intMenuItemNames),
                    menuPath = (availableMenuPaths != null && availableMenuPaths.Length > 0 && selectedMenuPathIndex >= 0 && selectedMenuPathIndex < availableMenuPaths.Length) ? availableMenuPaths[selectedMenuPathIndex] : "/",
                    savedParameter = savedParameter,
                    syncedParameter = syncedParameter,
                    defaultStateSelection = defaultStateSelection,
                    defaultIntValue = defaultIntValue,
                    defaultFloatValue = defaultFloatValue,
                    editInWDOnMode = editInWDOnMode
                }
            };

            switch (selectedLayerType)
            {
                case 0:
                    cfg.config.boolTargets = MapBoolTargetsToToggleData();
                    break;
                case 1:
                    cfg.config.intGroups = MapIntTargetsToToggleData();
                    break;
                case 2:
                    cfg.config.floatTargets = MapFloatTargetsToToggleData();
                    break;
            }

            return cfg;
        }

        private void OnApplyButtonClick()
        {
            var config = GatherConfiguration();
            var workflow = new DirectApplyWorkflow(config);
            workflow.Execute();

            // 在编辑模式下，“直接修改”等同于“应用并移除”：应用后从配置脚本中移除当前项目
            if (isEditingConfigEntry && loadedConfigComponent != null && !string.IsNullOrEmpty(lockedLayerName))
            {
                loadedConfigComponent.RemoveConfiguration(lockedLayerName);
                ExitEditFromConfig();
                LoadFromConfig(loadedConfigComponent);
                ShowNotification(new GUIContent("已应用并从配置脚本中移除"));
            }
        }

        private void OnGenerateButtonClick()
        {
            if (targetAvatarDescriptor == null)
            {
                ShowNotification(new GUIContent("请先拖入 Avatar"));
                return;
            }
            if (loadedConfigComponent == null)
            {
                loadedConfigComponent = targetAvatarDescriptor.GetComponent<QuickToggleConfig>();
                if (loadedConfigComponent == null)
                    loadedConfigComponent = targetAvatarDescriptor.gameObject.AddComponent<QuickToggleConfig>();
                loadedConfigComponent.targetAvatar = targetAvatarDescriptor;
            }
            var entry = MapUIToEntry();
            // 若未填写配置名称，则根据模式与现有数量生成默认名称，例如“Bool配置1”
            if (string.IsNullOrEmpty(entry.displayName))
            {
                int index = loadedConfigComponent.layerConfigs != null ? loadedConfigComponent.layerConfigs.Count + 1 : 1;
                string modeLabel = entry.layerType == 0 ? "Bool" : entry.layerType == 1 ? "Int" : "Float";
                entry.displayName = $"{modeLabel}配置{index}";
            }
            loadedConfigComponent.UpdateConfiguration(entry);
            LoadFromConfig(loadedConfigComponent);
            ShowNotification(new GUIContent("已保存到 QuickToggleConfig"));
        }

        private void OnModifyButtonClick()
        {
            if (!isEditingConfigEntry || loadedConfigComponent == null) return;
            var entry = MapUIToEntry();
            loadedConfigComponent.UpdateConfiguration(entry);
            LoadFromConfig(loadedConfigComponent);
            ShowNotification(new GUIContent("已更新配置项"));
        }

        private void OnApplyAndRemoveButtonClick()
        {
            OnApplyButtonClick();
            if (!string.IsNullOrEmpty(lockedLayerName) && loadedConfigComponent != null)
            {
                loadedConfigComponent.RemoveConfiguration(lockedLayerName);
                ExitEditFromConfig();
                LoadFromConfig(loadedConfigComponent);
            }
        }

        private void LoadFromConfig(QuickToggleConfig config)
        {
            availableConfigEntries.Clear();
            configEntryOptions = System.Array.Empty<string>();
            selectedConfigEntryIndex = -1;
            isEditingConfigEntry = false;
            lockedLayerName = null;
            lockedSwitchType = -1;

            if (config == null) return;
            foreach (var e in config.layerConfigs)
            {
                if (e == null) continue;
                // filter by avatar
                if (config.targetAvatar != null && targetAvatarDescriptor != null && config.targetAvatar != targetAvatarDescriptor)
                    continue;
                availableConfigEntries.Add(e);
            }
            // sort: AvatarName asc, SwitchType, layerName
            availableConfigEntries.Sort((a,b)=>
            {
                string avA = config.targetAvatar ? config.targetAvatar.gameObject.name : "";
                string avB = config.targetAvatar ? config.targetAvatar.gameObject.name : "";
                int c1 = string.Compare(avA, avB, System.StringComparison.Ordinal);
                if (c1 != 0) return c1;
                int c2 = a.layerType.CompareTo(b.layerType);
                if (c2 != 0) return c2;
                return string.Compare(a.layerName, b.layerName, System.StringComparison.Ordinal);
            });
            var opts = new List<string>();
            foreach (var e in availableConfigEntries)
            {
                string text;
                if (!string.IsNullOrEmpty(e.displayName)) text = e.displayName;
                else if (!string.IsNullOrEmpty(e.layerName)) text = e.layerName;
                else text = "(未命名层)";
                opts.Add(text);
            }
            configEntryOptions = opts.ToArray();
        }

        private void EnterEditFromConfig(QuickToggleConfig.LayerConfig entry)
        {
            if (entry == null) return;
            isEditingConfigEntry = true;
            lockedLayerName = entry.layerName;
            lockedSwitchType = entry.layerType;

            selectedLayerType = entry.layerType;
            layerName = entry.layerName;
            parameterName = entry.parameterName;
            overwriteLayer = entry.overwriteLayer;
            overwriteParameter = entry.overwriteParameter;
            writeDefaultSetting = entry.writeDefaultSetting;
            createMenuControl = entry.createMenuControl;
            menuControlName = string.IsNullOrEmpty(entry.menuControlName) ? null : entry.menuControlName;
            boolMenuItemName = entry.boolMenuItemName;
            floatMenuItemName = entry.floatMenuItemName;
            intSubMenuName = entry.intSubMenuName;
            intMenuItemNames.Clear();
            if (entry.intMenuItemNames != null)
                intMenuItemNames.AddRange(entry.intMenuItemNames);
            savedParameter = entry.savedParameter;
            syncedParameter = entry.syncedParameter;
            defaultStateSelection = entry.defaultStateSelection;
            defaultIntValue = entry.defaultIntValue;
            defaultFloatValue = entry.defaultFloatValue;
            editInWDOnMode = entry.editInWDOnMode;
            SetClipSaveRootFromConfig(entry.clipSavePath);

            currentMenuPath = string.IsNullOrEmpty(entry.menuPath) ? "/" : entry.menuPath;
            RefreshMenuPaths();

            if (string.IsNullOrEmpty(menuControlName))
            {
                menuControlName = GetCurrentMenuControlName();
            }

            // 配置名称载入到临时字段，供 UI 显示与编辑
            currentConfigDisplayName = entry.displayName;

            RefreshAvailableParameterNames();
            EnsureParameterSelection();

            MapEntryTargetsToUI(entry);
        }

        private void ExitEditFromConfig()
        {
            isEditingConfigEntry = false;
            lockedLayerName = null;
            lockedSwitchType = -1;
        }

        private QuickToggleConfig.LayerConfig MapUIToEntry()
        {
            var result = new QuickToggleConfig.LayerConfig
            {
                displayName = currentConfigDisplayName,
                layerName = ResolveEffectiveLayerName(),
                layerType = selectedLayerType,
                parameterName = parameterName,
                overwriteLayer = overwriteLayer,
                overwriteParameter = overwriteParameter,
                // 配置中仅存根路径，真正保存动画时再拼接最终层级名
                clipSavePath = NormalizeClipRootPath(clipSavePath),
                writeDefaultSetting = writeDefaultSetting,
                createMenuControl = createMenuControl,
                menuControlName = GetCurrentMenuControlName(),
                menuPath = currentMenuPath,
                boolMenuItemName = boolMenuItemName,
                floatMenuItemName = floatMenuItemName,
                intSubMenuName = intSubMenuName,
                intMenuItemNames = new List<string>(),
                savedParameter = savedParameter,
                syncedParameter = syncedParameter,
                defaultStateSelection = defaultStateSelection,
                defaultIntValue = defaultIntValue,
                defaultFloatValue = defaultFloatValue,
                editInWDOnMode = editInWDOnMode
            };
            if (selectedLayerType == 1)
            {
                EnsureIntMenuNameCapacity();
                for (int g = 0; g < intGroups.Count; g++)
                {
                    string value = g < intMenuItemNames.Count ? intMenuItemNames[g] : string.Empty;
                    result.intMenuItemNames.Add(value);
                }
            }

            switch (selectedLayerType)
            {
                case 0:
                    result.boolTargets = MapBoolTargetsToQuickConfigData();
                    break;
                case 1:
                    result.intGroups = MapIntTargetsToQuickConfigData();
                    break;
                case 2:
                    result.floatTargets = MapFloatTargetsToQuickConfigData();
                    break;
            }

            return result;
        }

        private string ResolveEffectiveLayerName()
        {
            if (!overwriteLayer)
                return layerName;

            if (!string.IsNullOrWhiteSpace(layerName))
                return layerName;

            if (availableLayerNames != null && availableLayerNames.Length > 0)
            {
                int idx = selectedLayerPopupIndex;
                if (idx < 0 || idx >= availableLayerNames.Length)
                    idx = 0;
                return availableLayerNames[idx];
            }

            return layerName;
        }

        private string NormalizeClipRootPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return ToolboxUtils.GetAqtRootFolder();

            string trimmed = path.Trim().Replace('\\', '/');
            if (!trimmed.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
                return ToolboxUtils.GetAqtRootFolder();

            if (trimmed.Length == 6 && trimmed.Equals("Assets", StringComparison.OrdinalIgnoreCase))
                return "Assets";

            if (!trimmed.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                trimmed = "Assets/" + trimmed.Substring(6).TrimStart('/');

            while (trimmed.EndsWith("/", StringComparison.Ordinal))
                trimmed = trimmed.Substring(0, trimmed.Length - 1);

            return string.IsNullOrEmpty(trimmed) ? ToolboxUtils.GetAqtRootFolder() : trimmed;
        }

        private string BuildFinalClipFolder()
        {
            string root = NormalizeClipRootPath(clipSavePath);
            string segment = string.IsNullOrWhiteSpace(layerName) ? "Layer" : layerName;
            return ToolboxUtils.BuildAqtLayerFolder(root, segment);
        }

        private void SetClipSaveRootFromConfig(string storedPath)
        {
            if (string.IsNullOrWhiteSpace(storedPath))
            {
                clipSavePath = ToolboxUtils.GetAqtRootFolder();
                return;
            }
            // 现在配置中只存根路径，直接归一化后还原即可
            clipSavePath = NormalizeClipRootPath(storedPath);
        }

        private void EnsureLayerSelection()
        {
            if (!overwriteLayer)
                return;

            if (!string.IsNullOrEmpty(layerName))
                return;

            if (availableLayerNames == null || availableLayerNames.Length == 0)
                return;

            if (selectedLayerPopupIndex < 0 || selectedLayerPopupIndex >= availableLayerNames.Length)
                selectedLayerPopupIndex = 0;

            layerName = availableLayerNames[selectedLayerPopupIndex];
        }

        private void EnsureParameterSelection()
        {
            if (!overwriteParameter)
                return;

            if (availableParameterNames == null || availableParameterNames.Length == 0)
                return;

            if (!string.IsNullOrEmpty(parameterName))
            {
                // 已有配置：仅根据名称尝试对齐索引，若找不到则保持为 -1，交由下拉逻辑显示 "[原名] 缺失"
                selectedParameterPopupIndex = Array.IndexOf(availableParameterNames, parameterName);
                return;
            }

            // 无配置名称（新建时）：才在需要时自动选中第一个可用参数
            if (selectedParameterPopupIndex < 0 || selectedParameterPopupIndex >= availableParameterNames.Length)
            {
                selectedParameterPopupIndex = 0;
                parameterName = availableParameterNames[0];
            }
        }

        private void ApplySelectedParameterNameFromPopup()
        {
            if (!overwriteParameter)
                return;

            if (selectedParameterPopupIndex >= 0 && selectedParameterPopupIndex < availableParameterNames.Length)
            {
                parameterName = availableParameterNames[selectedParameterPopupIndex];
            }
        }

        private void MapEntryTargetsToUI(QuickToggleConfig.LayerConfig entry)
        {
            boolTargets.Clear();
            intGroups.Clear();
            floatTargets.Clear();
            if (entry.layerType == 0 && entry.boolTargets != null)
            {
                foreach (var d in entry.boolTargets)
                {
                    var ui = new TargetItem
                    {
                        targetObject = d.targetObject,
                        controlType = d.controlType == QuickToggleConfig.TargetControlType.GameObject ? 0 : 1,
                        blendShapeName = d.blendShapeName,
                        onStateActiveSelection = d.goState == QuickToggleConfig.GameObjectState.Active ? 0 : 1,
                        onStateBlendShapeValue = d.bsState == QuickToggleConfig.BlendShapeState.Zero ? 0 : 1
                    };
                    boolTargets.Add(ui);
                }
            }
            else if (entry.layerType == 1 && entry.intGroups != null)
            {
                for (int g = 0; g < entry.intGroups.Count; g++)
                {
                    var gd = entry.intGroups[g];
                    var gu = new IntStateGroup { stateName = gd.stateName, targetItems = new List<TargetItem>(), isFoldout = false };
                    if (gd.targetItems != null)
                    {
                        foreach (var d in gd.targetItems)
                        {
                            var ui = new TargetItem
                            {
                                targetObject = d.targetObject,
                                controlType = d.controlType == QuickToggleConfig.TargetControlType.GameObject ? 0 : 1,
                                blendShapeName = d.blendShapeName,
                                onStateActiveSelection = d.goState == QuickToggleConfig.GameObjectState.Active ? 0 : 1,
                                onStateBlendShapeValue = d.bsState == QuickToggleConfig.BlendShapeState.Zero ? 0 : 1
                            };
                            gu.targetItems.Add(ui);
                        }
                    }
                    intGroups.Add(gu);
                }
            }
            else if (entry.layerType == 2 && entry.floatTargets != null)
            {
                foreach (var d in entry.floatTargets)
                {
                    var ui = new TargetItem
                    {
                        targetObject = d.targetObject,
                        controlType = 1,
                        blendShapeName = d.blendShapeName,
                        onStateBlendShapeValue = d.direction == QuickToggleConfig.FloatDirection.ZeroToFull ? 0 : 1,
                        splitBlendShape = d.splitBlendShape,
                        secondaryBlendShapeName = d.secondaryBlendShapeName,
                        secondaryBlendShapeValue = d.secondaryDirection == QuickToggleConfig.FloatDirection.ZeroToFull ? 0 : 1
                    };
                    floatTargets.Add(ui);
                }
            }
        }

        private List<TargetItem> MapBoolTargetsToToggleData()
        {
            var list = new List<TargetItem>();
            foreach (var it in boolTargets)
            {
                if (it?.targetObject == null) continue;
                var data = new TargetItem
                {
                    targetObject = it.targetObject,
                    controlType = it.controlType,
                    blendShapeName = it.blendShapeName,
                    onStateActiveSelection = it.onStateActiveSelection,
                    onStateBlendShapeValue = it.onStateBlendShapeValue,
                    splitBlendShape = it.splitBlendShape,
                    secondaryBlendShapeName = it.secondaryBlendShapeName,
                    secondaryBlendShapeValue = it.secondaryBlendShapeValue
                };
                list.Add(data);
            }
            return list;
        }

        private List<QuickToggleConfig.TargetItemData> MapBoolTargetsToQuickConfigData()
        {
            var list = new List<QuickToggleConfig.TargetItemData>();
            foreach (var it in boolTargets)
            {
                if (it?.targetObject == null) continue;
                var data = new QuickToggleConfig.TargetItemData
                {
                    targetObject = it.targetObject,
                    controlType = it.controlType == 0 ? QuickToggleConfig.TargetControlType.GameObject : QuickToggleConfig.TargetControlType.BlendShape,
                    blendShapeName = it.blendShapeName,
                    goState = it.onStateActiveSelection == 0 ? QuickToggleConfig.GameObjectState.Active : QuickToggleConfig.GameObjectState.Inactive,
                    bsState = it.onStateBlendShapeValue == 0 ? QuickToggleConfig.BlendShapeState.Zero : QuickToggleConfig.BlendShapeState.Full
                };
                list.Add(data);
            }
            return list;
        }

        private List<QuickToggleConfig.IntGroupData> MapIntTargetsToQuickConfigData()
        {
            var groups = new List<QuickToggleConfig.IntGroupData>();
            for (int g = 0; g < intGroups.Count; g++)
            {
                var group = intGroups[g];
                var data = new QuickToggleConfig.IntGroupData { stateName = group.stateName, targetItems = new List<QuickToggleConfig.TargetItemData>() };
                foreach (var it in group.targetItems)
                {
                    if (it?.targetObject == null) continue;
                    var item = new QuickToggleConfig.TargetItemData
                    {
                        targetObject = it.targetObject,
                        controlType = it.controlType == 0 ? QuickToggleConfig.TargetControlType.GameObject : QuickToggleConfig.TargetControlType.BlendShape,
                        blendShapeName = it.blendShapeName,
                        goState = it.onStateActiveSelection == 0 ? QuickToggleConfig.GameObjectState.Active : QuickToggleConfig.GameObjectState.Inactive,
                        bsState = it.onStateBlendShapeValue == 0 ? QuickToggleConfig.BlendShapeState.Zero : QuickToggleConfig.BlendShapeState.Full
                    };
                    data.targetItems.Add(item);
                }
                groups.Add(data);
            }
            return groups;
        }

        private List<QuickToggleConfig.TargetItemData> MapFloatTargetsToQuickConfigData()
        {
            var list = new List<QuickToggleConfig.TargetItemData>();
            foreach (var it in floatTargets)
            {
                if (it?.targetObject == null) continue;
                var item = new QuickToggleConfig.TargetItemData
                {
                    targetObject = it.targetObject,
                    controlType = QuickToggleConfig.TargetControlType.BlendShape,
                    blendShapeName = it.blendShapeName,
                    direction = it.onStateBlendShapeValue == 0 ? QuickToggleConfig.FloatDirection.ZeroToFull : QuickToggleConfig.FloatDirection.FullToZero,
                    splitBlendShape = it.splitBlendShape,
                    secondaryBlendShapeName = it.secondaryBlendShapeName,
                    secondaryDirection = it.secondaryBlendShapeValue == 0 ? QuickToggleConfig.FloatDirection.ZeroToFull : QuickToggleConfig.FloatDirection.FullToZero
                };
                list.Add(item);
            }
            return list;
        }

        private List<IntStateGroup> MapIntTargetsToToggleData()
        {
            var list = new List<IntStateGroup>();
            for (int g = 0; g < intGroups.Count; g++)
            {
                var group = intGroups[g];
                var data = new IntStateGroup { stateName = group.stateName, targetItems = new List<TargetItem>() };
                foreach (var it in group.targetItems)
                {
                    if (it?.targetObject == null) continue;
                    var item = new TargetItem
                    {
                        targetObject = it.targetObject,
                        controlType = it.controlType,
                        blendShapeName = it.blendShapeName,
                        onStateActiveSelection = it.onStateActiveSelection,
                        onStateBlendShapeValue = it.onStateBlendShapeValue,
                        splitBlendShape = it.splitBlendShape,
                        secondaryBlendShapeName = it.secondaryBlendShapeName,
                        secondaryBlendShapeValue = it.secondaryBlendShapeValue
                    };
                    data.targetItems.Add(item);
                }
                list.Add(data);
            }
            return list;
        }

        private List<TargetItem> MapFloatTargetsToToggleData()
        {
            var list = new List<TargetItem>();
            foreach (var it in floatTargets)
            {
                if (it?.targetObject == null) continue;
                var item = new TargetItem
                {
                    targetObject = it.targetObject,
                    controlType = 1,
                    blendShapeName = it.blendShapeName,
                    onStateBlendShapeValue = it.onStateBlendShapeValue,
                    splitBlendShape = it.splitBlendShape,
                    secondaryBlendShapeName = it.secondaryBlendShapeName,
                    secondaryBlendShapeValue = it.secondaryBlendShapeValue
                };
                list.Add(item);
            }
            return list;
        }

        private void EnsureDefaultTargets()
        {
            if (selectedLayerType == 0)
            {
                if (boolTargets.Count == 0)
                    boolTargets.Add(new TargetItem());
            }

            else if (selectedLayerType == 1)
            {
                if (intGroups.Count == 0)
                    intGroups.Add(new IntStateGroup { targetItems = new List<TargetItem> { new TargetItem() } });
                for (int g = 0; g < intGroups.Count; g++)
                {
                    if (intGroups[g].targetItems == null)
                        intGroups[g].targetItems = new List<TargetItem>();
                    if (intGroups[g].targetItems.Count == 0)
                        intGroups[g].targetItems.Add(new TargetItem());
                }
                EnsureIntMenuNameCapacity();
            }

            else if (selectedLayerType == 2)
            {
                if (floatTargets.Count == 0)
                    floatTargets.Add(new TargetItem { controlType = 1 });
            }
        }

        private void EnsureIntMenuNameCapacity()
        {
            int targetCount = intGroups.Count;
            while (intMenuItemNames.Count < targetCount) intMenuItemNames.Add(string.Empty);
            while (intMenuItemNames.Count > targetCount && intMenuItemNames.Count > 0) intMenuItemNames.RemoveAt(intMenuItemNames.Count - 1);
        }

        private string GetCurrentMenuControlName()
        {
            switch (selectedLayerType)
            {
                case 0:
                    return string.IsNullOrEmpty(boolMenuItemName) ? layerName : boolMenuItemName;
                case 1:
                    return string.IsNullOrEmpty(intSubMenuName) ? layerName : intSubMenuName;
                case 2:
                    return string.IsNullOrEmpty(floatMenuItemName) ? layerName : floatMenuItemName;
                default:
                    return layerName;
            }
        }

        private void LoadAvatarData(VRCAvatarDescriptor avatar)
        {
            fxController = null;
            expressionParameters = null;
            expressionsMenu = null;
            availableLayerNames = System.Array.Empty<string>();
            availableParameterNames = System.Array.Empty<string>();
            availableMenuPaths = System.Array.Empty<string>();
            selectedLayerPopupIndex = -1;
            selectedParameterPopupIndex = -1;

            selectedMenuPathIndex = -1;
            menuPathMap = new Dictionary<string, VRCExpressionsMenu>();
            // 将当前菜单路径重置为空字符串，由 RefreshMenuPaths 在有可用菜单时填充实际根路径
            currentMenuPath = string.Empty;

            if (avatar == null) return;
            fxController = ToolboxUtils.GetExistingFXController(avatar) ?? ToolboxUtils.EnsureFXController(avatar);
            expressionParameters = ToolboxUtils.GetExistingExpressionParameters(avatar) ?? ToolboxUtils.EnsureExpressionParameters(avatar);
            expressionsMenu = ToolboxUtils.GetExistingExpressionsMenu(avatar) ?? ToolboxUtils.EnsureExpressionsMenu(avatar);
            RefreshAvailableLayerNames();
            RefreshAvailableParameterNames();
            RefreshMenuPaths();
        }

        private void RefreshAvailableLayerNames()
        {
            if (fxController == null)
            {
                availableLayerNames = System.Array.Empty<string>();
                return;
            }
            var list = new List<string>();
            foreach (var l in fxController.layers)
            {
                if (!string.IsNullOrEmpty(l.name)) list.Add(l.name);
            }
            availableLayerNames = list.ToArray();
            selectedLayerPopupIndex = Mathf.Max(0, System.Array.IndexOf(availableLayerNames, layerName));
        }

        private void RefreshAvailableParameterNames()
        {
            var typeMap = BuildAvailableParameterTypeMap();
            if (typeMap == null || typeMap.Count == 0)
            {
                availableParameterNames = System.Array.Empty<string>();
                selectedParameterPopupIndex = -1;
                return;
            }

            var list = new List<string>();
            foreach (var kvp in typeMap)
            {
                switch (selectedLayerType)
                {
                    case 0:
                        if (kvp.Value == VRCExpressionParameters.ValueType.Bool)
                            list.Add(kvp.Key);
                        break;
                    case 1:
                        if (kvp.Value == VRCExpressionParameters.ValueType.Int)
                            list.Add(kvp.Key);
                        break;
                    case 2:
                        if (kvp.Value == VRCExpressionParameters.ValueType.Float)
                            list.Add(kvp.Key);
                        break;
                }
            }

            if (list.Count > 1)
            {
                list.Sort(string.CompareOrdinal);
            }

            availableParameterNames = list.ToArray();
            selectedParameterPopupIndex = Mathf.Max(0, System.Array.IndexOf(availableParameterNames, parameterName));
            if (overwriteParameter)
            {
                EnsureParameterSelection();
            }
            else
            {
                selectedParameterPopupIndex = -1;
            }
        }

        private Dictionary<string, VRCExpressionParameters.ValueType> BuildAvailableParameterTypeMap()
        {
            var map = new Dictionary<string, VRCExpressionParameters.ValueType>(StringComparer.Ordinal);

            if (expressionParameters?.parameters != null)
            {
                foreach (var parameter in expressionParameters.parameters)
                {
                    if (parameter == null || string.IsNullOrEmpty(parameter.name)) continue;
                    if (!map.ContainsKey(parameter.name))
                    {
                        map[parameter.name] = parameter.valueType;
                    }
                }
            }

            if (fxController?.parameters != null)
            {
                foreach (var animatorParam in fxController.parameters)
                {
                    if (animatorParam == null || string.IsNullOrEmpty(animatorParam.name)) continue;
                    var valueType = ConvertAnimatorParameterType(animatorParam.type);
                    if (valueType == null) continue;
                    map[animatorParam.name] = valueType.Value;
                }
            }

            return map;
        }

        private VRCExpressionParameters.ValueType? ConvertAnimatorParameterType(AnimatorControllerParameterType type)
        {
            switch (type)
            {
                case AnimatorControllerParameterType.Bool:
                    return VRCExpressionParameters.ValueType.Bool;
                case AnimatorControllerParameterType.Int:
                    return VRCExpressionParameters.ValueType.Int;
                case AnimatorControllerParameterType.Float:
                    return VRCExpressionParameters.ValueType.Float;
                default:
                    return null;
            }
        }

        private void RefreshMenuPaths()
        {
            if (expressionsMenu == null)
            {
                menuPathMap = new Dictionary<string, VRCExpressionsMenu>();
                availableMenuPaths = System.Array.Empty<string>();
                selectedMenuPathIndex = -1;
                currentMenuPath = string.Empty;
                return;
            }

            menuPathMap = ToolboxUtils.GetMenuMap(expressionsMenu) ?? new Dictionary<string, VRCExpressionsMenu>();
            availableMenuPaths = menuPathMap.Keys.ToArray();

            if (availableMenuPaths.Length == 0)
            {
                selectedMenuPathIndex = -1;
                currentMenuPath = string.Empty;
                return;
            }

            // 若处于“从配置编辑”模式，优先使用配置中保存的 menuPath
            if (isEditingConfigEntry && !string.IsNullOrEmpty(currentMenuPath))
            {
                var normalized = NormalizeMenuPathForLookup(currentMenuPath);
                if (menuPathMap.ContainsKey(normalized))
                {
                    selectedMenuPathIndex = System.Array.IndexOf(availableMenuPaths, normalized);
                    if (selectedMenuPathIndex < 0)
                    {
                        selectedMenuPathIndex = 0;
                        currentMenuPath = availableMenuPaths[0];
                    }
                    else
                    {
                        currentMenuPath = availableMenuPaths[selectedMenuPathIndex];
                    }
                    return;
                }
            }

            selectedMenuPathIndex = Mathf.Clamp(selectedMenuPathIndex, 0, availableMenuPaths.Length - 1);
            currentMenuPath = availableMenuPaths[selectedMenuPathIndex];
        }

        private string NormalizeMenuPathForLookup(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;

            string trimmed = path.Trim();
            if (trimmed == "/")
            {
                return expressionsMenu != null && !string.IsNullOrEmpty(expressionsMenu.name)
                    ? expressionsMenu.name.Trim()
                    : string.Empty;
            }

            if (trimmed.StartsWith("/", StringComparison.Ordinal))
            {
                trimmed = trimmed.TrimStart('/');
            }
            return trimmed;
        }

    }
}
