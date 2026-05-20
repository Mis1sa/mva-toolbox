using System;
using System.Collections.Generic;
using MVA.Toolbox.SwitchGenerator.Utils;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.SwitchGenerator.Entry
{
    internal sealed partial class SwitchGeneratorWindow
    {
        private void DrawTargetObjects()
        {
            var layer = GetSelectedLayer();
            if (layer == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("目标物体", EditorStyles.boldLabel);

            if (layer.switchType == SwitchGeneratorConfig.SwitchType.Int)
            {
                bool previousWDMode = layer.editInWriteDefaultsOnMode;
                bool toggledWDMode = EditorGUILayout.ToggleLeft("在 Write Defaults on 下编辑", previousWDMode);
                layer.editInWriteDefaultsOnMode = toggledWDMode;
                if (toggledWDMode != previousWDMode)
                {
                    _pendingIntWDConversion = toggledWDMode ? 1 : -1;
                }
            }

            SwitchGeneratorLayerConfigEditing.EnsureDefaultTargets(layer);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            switch (layer.switchType)
            {
                case SwitchGeneratorConfig.SwitchType.Bool:
                    DrawBoolTargets();
                    break;
                case SwitchGeneratorConfig.SwitchType.Int:
                    DrawIntTargets();
                    break;
                case SwitchGeneratorConfig.SwitchType.Float:
                    DrawFloatTargets();
                    break;
            }

            EditorGUILayout.EndVertical();

            if (_previewSession.IsPreviewing)
            {
                if (layer.switchType == SwitchGeneratorConfig.SwitchType.Bool)
                {
                    ApplyPreviewStateBool(_previewValue >= 0.5f);
                }
                else if (layer.switchType == SwitchGeneratorConfig.SwitchType.Int)
                {
                    ApplyPreviewStateInt(Mathf.RoundToInt(_previewValue));
                }
                else
                {
                    ApplyPreviewStateFloat(_previewValue);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawBoolTargets()
        {
            var layer = GetSelectedLayer();
            if (layer == null)
            {
                return;
            }

            float btnSize = EditorGUIUtility.singleLineHeight;
            int removeAt = -1;
            for (int i = 0; i < layer.boolTargets.Count; i++)
            {
                var item = layer.boolTargets[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                var newBoolTarget = (GameObject)EditorGUILayout.ObjectField(item.targetObject, typeof(GameObject), true, GUILayout.Width(W_OBJECT));
                item.targetObject = TargetObjectResolver.ResolveValidTarget(newBoolTarget, _avatar);
                if (item.targetObject != null)
                {
                    bool hasGOElsewhere = false;
                    bool hasGOInPrevious = false;
                    for (int k = 0; k < layer.boolTargets.Count; k++)
                    {
                        if (k == i) continue;
                        var other = layer.boolTargets[k];
                        if (other.targetObject == item.targetObject && other.controlType == SwitchGeneratorConfig.TargetControlType.GameObject)
                        {
                            hasGOElsewhere = true;
                            if (k < i) hasGOInPrevious = true;
                        }
                    }

                    var allNames = GetBlendShapeNames(item.targetObject);
                    bool noBlendShapes = (allNames.Length == 0) || (allNames.Length == 1 && allNames[0] == "(None)");
                    var used = new HashSet<string>();
                    for (int k = 0; k < layer.boolTargets.Count; k++)
                    {
                        if (k == i) continue;
                        var other = layer.boolTargets[k];
                        if (other.targetObject == item.targetObject && other.controlType == SwitchGeneratorConfig.TargetControlType.BlendShape && !string.IsNullOrEmpty(other.blendShapeName) && other.blendShapeName != "(None)")
                        {
                            used.Add(other.blendShapeName);
                        }
                    }

                    var availableList = new List<string>();
                    for (int n = 0; n < allNames.Length; n++)
                    {
                        string nm = allNames[n];
                        if (nm == "(None)") continue;
                        if (!used.Contains(nm) || nm == item.blendShapeName) availableList.Add(nm);
                    }

                    string[] available = availableList.ToArray();

                    if (item.controlType == SwitchGeneratorConfig.TargetControlType.BlendShape && (available.Length == 0 || (!string.IsNullOrEmpty(item.blendShapeName) && Array.IndexOf(available, item.blendShapeName) < 0)))
                    {
                        item.controlType = SwitchGeneratorConfig.TargetControlType.GameObject;
                        item.blendShapeName = null;
                    }

                    bool goAvailable = !hasGOElsewhere;
                    bool blendAvailable = available.Length > 0;
                    bool hasValidGO = item.controlType == SwitchGeneratorConfig.TargetControlType.GameObject && !hasGOInPrevious;
                    bool hasValidBlend = item.controlType == SwitchGeneratorConfig.TargetControlType.BlendShape && !string.IsNullOrEmpty(item.blendShapeName) && Array.IndexOf(available, item.blendShapeName) >= 0;
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
                        if (allowGOSelect && allowBlendSelect)
                        {
                            int modeIdx = item.controlType == SwitchGeneratorConfig.TargetControlType.GameObject ? 0 : 1;
                            modeIdx = EditorGUILayout.Popup(modeIdx, new[] { "GameObject", "BlendShape" }, GUILayout.Width(W_MODE));
                            item.controlType = modeIdx == 0 ? SwitchGeneratorConfig.TargetControlType.GameObject : SwitchGeneratorConfig.TargetControlType.BlendShape;
                        }
                        else if (allowGOSelect && !allowBlendSelect)
                        {
                            EditorGUILayout.Popup(0, new[] { "GameObject" }, GUILayout.Width(W_MODE));
                            item.controlType = SwitchGeneratorConfig.TargetControlType.GameObject;
                        }
                        else if (!allowGOSelect && allowBlendSelect)
                        {
                            EditorGUILayout.Popup(0, new[] { "BlendShape" }, GUILayout.Width(W_MODE));
                            item.controlType = SwitchGeneratorConfig.TargetControlType.BlendShape;
                        }
                        else
                        {
                            GUILayout.Space(W_MODE);
                        }

                        if (item.controlType == SwitchGeneratorConfig.TargetControlType.GameObject)
                        {
                            DrawInvisibleObjectFieldExpand();
                        }
                        else if (allowBlendSelect && item.controlType == SwitchGeneratorConfig.TargetControlType.BlendShape)
                        {
                            int idx = Array.IndexOf(available, item.blendShapeName);
                            if (idx < 0 && !string.IsNullOrEmpty(item.blendShapeName))
                            {
                                item.controlType = SwitchGeneratorConfig.TargetControlType.GameObject;
                                item.blendShapeName = null;
                                DrawInvisibleObjectFieldExpand();
                            }
                            else
                            {
                                if (idx < 0) idx = 0;
                                int newIdx = EditorGUILayout.Popup(idx, available, GUILayout.ExpandWidth(true));
                                if (available.Length > 0) item.blendShapeName = available[Mathf.Clamp(newIdx, 0, available.Length - 1)];
                            }
                        }
                        else
                        {
                            DrawInvisibleObjectFieldExpand();
                        }

                        GUILayout.Label("状态", GUILayout.Width(W_LABEL));
                        if (item.controlType == SwitchGeneratorConfig.TargetControlType.GameObject)
                        {
                            int stateIdx = item.boolObjectState == SwitchGeneratorConfig.BoolObjectState.Active ? 0 : 1;
                            stateIdx = EditorGUILayout.Popup(stateIdx, new[] { "激活", "关闭" }, GUILayout.Width(W_STATE));
                            item.boolObjectState = stateIdx == 0 ? SwitchGeneratorConfig.BoolObjectState.Active : SwitchGeneratorConfig.BoolObjectState.Inactive;
                        }
                        else
                        {
                            int stateIdx = item.boolBlendShapeState == SwitchGeneratorConfig.BoolBlendShapeState.Zero ? 0 : 1;
                            stateIdx = EditorGUILayout.Popup(stateIdx, new[] { "0", "100" }, GUILayout.Width(W_STATE));
                            item.boolBlendShapeState = stateIdx == 0 ? SwitchGeneratorConfig.BoolBlendShapeState.Zero : SwitchGeneratorConfig.BoolBlendShapeState.Full;
                        }

                        Rect plusRect = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.button, GUILayout.Width(btnSize), GUILayout.Height(btnSize));
                        if (item.controlType == SwitchGeneratorConfig.TargetControlType.BlendShape)
                        {
                            bool hasAnyUnused = item.targetObject != null && NextUnusedBlendShapeWithinList(item.targetObject, null, layer.boolTargets) != null;
                            using (new EditorGUI.DisabledScope(!hasAnyUnused))
                            {
                                if (GUI.Button(plusRect, "+"))
                                {
                                    TryAddAnotherBlendShapeForItem(layer.boolTargets, i);
                                }
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
                    GUILayout.Space(W_MODE);
                    GUILayout.FlexibleSpace();
                    GUILayout.Space(W_LABEL + W_STATE + btnSize);
                }

                if (GUILayout.Button("-", GUILayout.Width(btnSize), GUILayout.Height(btnSize))) removeAt = i;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }

            if (removeAt >= 0) layer.boolTargets.RemoveAt(removeAt);
            if (GUILayout.Button("新目标")) layer.boolTargets.Add(new SwitchGeneratorConfig.TargetItem());
        }

        private void DrawIntTargets()
        {
            var layer = GetSelectedLayer();
            if (layer == null)
            {
                return;
            }

            float btnSize = EditorGUIUtility.singleLineHeight;
            SwitchGeneratorLayerConfigEditing.EnsureIntMenuNameCapacity(layer);
            int removeGroupIndex = -1;
            for (int g = 0; g < layer.intGroups.Count; g++)
            {
                var group = layer.intGroups[g];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                bool removeCurrentGroup = false;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"组 {g}", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("-", GUILayout.Width(btnSize), GUILayout.Height(btnSize)))
                    {
                        if (layer.intGroups.Count > 1)
                        {
                            removeGroupIndex = g;
                            removeCurrentGroup = true;
                        }
                        else
                        {
                            group.stateName = string.Empty;
                            if (group.targets == null) group.targets = new List<SwitchGeneratorConfig.TargetItem>();
                            group.targets.Clear();
                            group.targets.Add(new SwitchGeneratorConfig.TargetItem());
                            if (layer.intMenuItemNames.Count == 0) layer.intMenuItemNames.Add(string.Empty);
                            if (g < layer.intMenuItemNames.Count) layer.intMenuItemNames[g] = string.Empty;
                        }
                    }
                }

                if (removeCurrentGroup)
                {
                    EditorGUILayout.EndVertical();
                    continue;
                }

                int removeAt = -1;
                for (int i = 0; i < group.targets.Count; i++)
                {
                    var item = group.targets[i];
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.BeginHorizontal();
                    bool canEditGroup = layer.editInWriteDefaultsOnMode || g == 0;
                    bool showOccupied = false;

                    using (new EditorGUI.DisabledScope(!canEditGroup))
                    {
                        var newIntTarget = (GameObject)EditorGUILayout.ObjectField(item.targetObject, typeof(GameObject), true, GUILayout.Width(W_OBJECT));
                        item.targetObject = TargetObjectResolver.ResolveValidTarget(newIntTarget, _avatar);
                    }

                    bool hasTarget = item.targetObject != null;

                    if (hasTarget)
                    {
                        bool hasGOElsewhere = false;
                        bool hasGOPrior = false;
                        for (int k = 0; k < group.targets.Count; k++)
                        {
                            if (k == i) continue;
                            var other = group.targets[k];
                            if (other.targetObject == item.targetObject && other.controlType == SwitchGeneratorConfig.TargetControlType.GameObject)
                            {
                                hasGOElsewhere = true;
                                if (k < i) hasGOPrior = true;
                            }
                        }

                        var allNames = GetBlendShapeNames(item.targetObject);
                        var used = new HashSet<string>();
                        for (int k = 0; k < group.targets.Count; k++)
                        {
                            if (k == i) continue;
                            var other = group.targets[k];
                            if (other.targetObject == item.targetObject && other.controlType == SwitchGeneratorConfig.TargetControlType.BlendShape && !string.IsNullOrEmpty(other.blendShapeName) && other.blendShapeName != "(None)")
                            {
                                used.Add(other.blendShapeName);
                            }
                        }

                        var availableList = new List<string>();
                        for (int n = 0; n < allNames.Length; n++)
                        {
                            string nm = allNames[n];
                            if (nm == "(None)") continue;
                            if (!used.Contains(nm) || nm == item.blendShapeName) availableList.Add(nm);
                        }

                        string[] available = availableList.ToArray();

                        if (item.controlType == SwitchGeneratorConfig.TargetControlType.BlendShape && (available.Length == 0 || (!string.IsNullOrEmpty(item.blendShapeName) && Array.IndexOf(available, item.blendShapeName) < 0)))
                        {
                            item.controlType = SwitchGeneratorConfig.TargetControlType.GameObject;
                            item.blendShapeName = null;
                        }

                        bool allowBlend = available.Length > 0;
                        bool allowGO = !hasGOElsewhere;
                        bool allowCurrentGO = (item.controlType == SwitchGeneratorConfig.TargetControlType.GameObject && !hasGOPrior);
                        bool allowGOThis = allowGO || allowCurrentGO;
                        bool allowBlendThis = allowBlend || (item.controlType == SwitchGeneratorConfig.TargetControlType.BlendShape && !string.IsNullOrEmpty(item.blendShapeName) && Array.IndexOf(available, item.blendShapeName) >= 0);

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
                                if (allowGOThis && allowBlendThis)
                                {
                                    int modeIdx = item.controlType == SwitchGeneratorConfig.TargetControlType.GameObject ? 0 : 1;
                                    modeIdx = EditorGUILayout.Popup(modeIdx, new[] { "GameObject", "BlendShape" }, GUILayout.Width(W_MODE));
                                    item.controlType = modeIdx == 0 ? SwitchGeneratorConfig.TargetControlType.GameObject : SwitchGeneratorConfig.TargetControlType.BlendShape;
                                }
                                else if (allowGOThis && !allowBlendThis)
                                {
                                    EditorGUILayout.Popup(0, new[] { "GameObject" }, GUILayout.Width(W_MODE));
                                    item.controlType = SwitchGeneratorConfig.TargetControlType.GameObject;
                                }
                                else if (!allowGOThis && allowBlendThis)
                                {
                                    EditorGUILayout.Popup(0, new[] { "BlendShape" }, GUILayout.Width(W_MODE));
                                    item.controlType = SwitchGeneratorConfig.TargetControlType.BlendShape;
                                }
                                else
                                {
                                    GUILayout.Space(W_MODE);
                                }

                                if (item.controlType == SwitchGeneratorConfig.TargetControlType.GameObject)
                                {
                                    DrawInvisibleObjectFieldExpand();
                                }
                                else if (allowBlendThis && item.controlType == SwitchGeneratorConfig.TargetControlType.BlendShape)
                                {
                                    int idx = Array.IndexOf(available, item.blendShapeName);
                                    if (idx < 0 && !string.IsNullOrEmpty(item.blendShapeName))
                                    {
                                        item.controlType = SwitchGeneratorConfig.TargetControlType.GameObject;
                                        item.blendShapeName = null;
                                        DrawInvisibleObjectFieldExpand();
                                    }
                                    else
                                    {
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

                    if (!showOccupied && item.targetObject != null)
                    {
                        GUILayout.Label("状态", GUILayout.Width(W_LABEL));
                        if (item.controlType == SwitchGeneratorConfig.TargetControlType.GameObject)
                        {
                            int stateIdx = item.boolObjectState == SwitchGeneratorConfig.BoolObjectState.Active ? 0 : 1;
                            stateIdx = EditorGUILayout.Popup(stateIdx, new[] { "激活", "关闭" }, GUILayout.Width(W_STATE));
                            item.boolObjectState = stateIdx == 0 ? SwitchGeneratorConfig.BoolObjectState.Active : SwitchGeneratorConfig.BoolObjectState.Inactive;
                        }
                        else
                        {
                            int stateIdx = item.boolBlendShapeState == SwitchGeneratorConfig.BoolBlendShapeState.Zero ? 0 : 1;
                            stateIdx = EditorGUILayout.Popup(stateIdx, new[] { "0", "100" }, GUILayout.Width(W_STATE));
                            item.boolBlendShapeState = stateIdx == 0 ? SwitchGeneratorConfig.BoolBlendShapeState.Zero : SwitchGeneratorConfig.BoolBlendShapeState.Full;
                        }
                    }
                    else if (item.targetObject == null)
                    {
                        GUILayout.Space(W_LABEL + W_STATE);
                    }

                    Rect plusRect = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.button, GUILayout.Width(btnSize), GUILayout.Height(btnSize));
                    bool showPlus = canEditGroup && item.controlType == SwitchGeneratorConfig.TargetControlType.BlendShape && item.targetObject != null && !showOccupied;
                    if (showPlus)
                    {
                        bool hasAnyUnused = NextUnusedBlendShapeWithinList(item.targetObject, null, group.targets) != null;
                        using (new EditorGUI.DisabledScope(!hasAnyUnused))
                        {
                            if (GUI.Button(plusRect, "+"))
                            {
                                TryAddAnotherBlendShapeForIntGroup(group.targets, i);
                            }
                        }
                    }
                    else
                    {
                        GUI.Label(plusRect, GUIContent.none);
                    }

                    using (new EditorGUI.DisabledScope(!(layer.editInWriteDefaultsOnMode || g == 0)))
                    {
                        if (GUILayout.Button("-", GUILayout.Width(btnSize), GUILayout.Height(btnSize))) removeAt = i;
                    }

                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                }

                if (removeAt >= 0 && removeAt < group.targets.Count) group.targets.RemoveAt(removeAt);
                using (new EditorGUI.DisabledScope(!(layer.editInWriteDefaultsOnMode || g == 0)))
                {
                    if (GUILayout.Button("新目标")) group.targets.Add(new SwitchGeneratorConfig.TargetItem());
                }

                EditorGUILayout.EndVertical();
            }

            if (removeGroupIndex >= 0 && removeGroupIndex < layer.intGroups.Count)
            {
                layer.intGroups.RemoveAt(removeGroupIndex);
                if (removeGroupIndex < layer.intMenuItemNames.Count)
                {
                    layer.intMenuItemNames.RemoveAt(removeGroupIndex);
                }
            }

            if (!layer.editInWriteDefaultsOnMode && Event.current.type == EventType.Repaint)
            {
                SwitchGeneratorIntWdModeConverter.SyncFromTemplate(layer);
            }

            if (GUILayout.Button("添加组"))
            {
                layer.intGroups.Add(new SwitchGeneratorConfig.IntGroup { targets = new List<SwitchGeneratorConfig.TargetItem> { new SwitchGeneratorConfig.TargetItem() } });
                SwitchGeneratorLayerConfigEditing.EnsureIntMenuNameCapacity(layer);
                if (_previewSession.IsPreviewing)
                {
                    _previewValue = Mathf.Clamp(_previewValue, 0, Mathf.Max(0, layer.intGroups.Count - 1));
                    ApplyPreviewStateInt(Mathf.RoundToInt(_previewValue));
                }
            }
        }

        private void DrawFloatTargets()
        {
            var layer = GetSelectedLayer();
            if (layer == null)
            {
                return;
            }

            float btnSize = EditorGUIUtility.singleLineHeight;
            int removeAt = -1;
            for (int i = 0; i < layer.floatTargets.Count; i++)
            {
                var item = layer.floatTargets[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                var newFloatTarget = (GameObject)EditorGUILayout.ObjectField(item.targetObject, typeof(GameObject), true, GUILayout.Width(W_OBJECT));

                item.targetObject = TargetObjectResolver.ResolveValidTarget(newFloatTarget, _avatar);

                if (item.targetObject != null)
                {
                    string[] namesAll = GetBlendShapeNames(item.targetObject);
                    var allShapes = new List<string>();
                    for (int n = 0; n < namesAll.Length; n++) { if (namesAll[n] != "(None)") allShapes.Add(namesAll[n]); }

                    var usedByOthers = new HashSet<string>();
                    for (int u = 0; u < layer.floatTargets.Count; u++)
                    {
                        if (u == i) continue;
                        var ot = layer.floatTargets[u];
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
                            primIdx = found >= 0 ? found : 0;
                            if (found < 0 && !item.splitBlendShape)
                            {
                                item.blendShapeName = null;
                            }
                        }

                        string oldPrimary = item.blendShapeName;
                        string oldSecondary = item.secondaryBlendShapeName;
                        var oldPrimaryDir = item.floatDirection;
                        var oldSecondaryDir = item.secondaryFloatDirection;

                        int newPrimIdx = EditorGUI.Popup(primRect, primIdx, primaryOptions.ToArray());
                        string newPrimary = (item.splitBlendShape && newPrimIdx == 0) ? null : primaryOptions[newPrimIdx];

                        if (_previewSession.IsPreviewing && newPrimary != oldPrimary) { _previewSession.Stop(); StartPreview(); }

                        if (item.splitBlendShape && newPrimary == oldSecondary)
                        {
                            item.blendShapeName = newPrimary;
                            item.secondaryBlendShapeName = oldPrimary;
                            item.floatDirection = oldSecondaryDir;
                            item.secondaryFloatDirection = oldPrimaryDir;
                        }
                        else
                        {
                            item.blendShapeName = newPrimary;
                        }

                        GUILayout.Label("方向", GUILayout.Width(W_LABEL));
                        int dirIdx = item.floatDirection == SwitchGeneratorConfig.FloatDirection.ZeroToFull ? 0 : 1;
                        dirIdx = EditorGUILayout.Popup(dirIdx, new[] { "0->100", "100->0" }, GUILayout.Width(W_STATE));
                        item.floatDirection = dirIdx == 0 ? SwitchGeneratorConfig.FloatDirection.ZeroToFull : SwitchGeneratorConfig.FloatDirection.FullToZero;

                        bool hasAnyUnused = item.targetObject != null && NextUnusedBlendShapeWithinList(item.targetObject, null, layer.floatTargets, true) != null;
                        using (new EditorGUI.DisabledScope(!hasAnyUnused))
                        {
                            if (GUILayout.Button("+", GUILayout.Width(btnSize), GUILayout.Height(btnSize)))
                            {
                                TryAddAnotherBlendShapeForItem(layer.floatTargets, i, true);
                            }
                        }
                    }
                }

                if (item.targetObject == null)
                {
                    GUILayout.Space(W_SPLIT);
                    GUILayout.FlexibleSpace();
                }

                if (GUILayout.Button("-", GUILayout.Width(btnSize), GUILayout.Height(btnSize))) removeAt = i;
                EditorGUILayout.EndHorizontal();

                if (item.targetObject != null && item.splitBlendShape)
                {
                    string[] namesAll2 = GetBlendShapeNames(item.targetObject);
                    var allShapes2 = new List<string>();
                    for (int n = 0; n < namesAll2.Length; n++) { if (namesAll2[n] != "(None)") allShapes2.Add(namesAll2[n]); }

                    var usedByOthers2 = new HashSet<string>();
                    for (int u = 0; u < layer.floatTargets.Count; u++)
                    {
                        if (u == i) continue;
                        var ot = layer.floatTargets[u];
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
                    var currentPrimaryDir = item.floatDirection;
                    var currentSecondaryDir = item.secondaryFloatDirection;

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

                    if (_previewSession.IsPreviewing && newSecondary != currentSecondary) { _previewSession.Stop(); StartPreview(); }

                    if (newSecondary == currentPrimary)
                    {
                        item.secondaryBlendShapeName = currentPrimary;
                        item.secondaryFloatDirection = currentPrimaryDir;
                        item.blendShapeName = currentSecondary;
                        item.floatDirection = currentSecondaryDir;
                    }
                    else
                    {
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
                    int secDirIdx = item.secondaryFloatDirection == SwitchGeneratorConfig.FloatDirection.ZeroToFull ? 0 : 1;
                    secDirIdx = EditorGUILayout.Popup(secDirIdx, new[] { "0->100", "100->0" }, GUILayout.Width(W_STATE));
                    item.secondaryFloatDirection = secDirIdx == 0 ? SwitchGeneratorConfig.FloatDirection.ZeroToFull : SwitchGeneratorConfig.FloatDirection.FullToZero;
                    Rect plusPlaceholder = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.button, GUILayout.Width(btnSize), GUILayout.Height(btnSize));
                    GUI.Label(plusPlaceholder, GUIContent.none);
                    Rect minusPlaceholder = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.button, GUILayout.Width(btnSize), GUILayout.Height(btnSize));
                    GUI.Label(minusPlaceholder, GUIContent.none);
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndVertical();
            }

            if (removeAt >= 0) layer.floatTargets.RemoveAt(removeAt);
            if (GUILayout.Button("新目标")) layer.floatTargets.Add(new SwitchGeneratorConfig.TargetItem { controlType = SwitchGeneratorConfig.TargetControlType.BlendShape });
        }

        private void ApplyWDConversion(bool toWDOn)
        {
            var layer = GetSelectedLayer();
            if (layer == null || layer.switchType != SwitchGeneratorConfig.SwitchType.Int || layer.intGroups.Count == 0)
            {
                return;
            }

            SwitchGeneratorIntWdModeConverter.Apply(layer, toWDOn);

            if (_previewSession.IsPreviewing)
            {
                _previewValue = Mathf.Clamp(_previewValue, 0, Mathf.Max(0, layer.intGroups.Count - 1));
                ApplyPreviewStateInt(Mathf.RoundToInt(_previewValue));
            }
        }

        private string[] GetBlendShapeNames(GameObject go)
        {
            if (go == null) return Array.Empty<string>();
            return TargetObjectResolver.GetAvailableBlendShapeNames(go);
        }

        private string NextUnusedBlendShapeWithinList(GameObject go, string current, List<SwitchGeneratorConfig.TargetItem> list, bool includeSecondary = false)
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

        private void TryAddAnotherBlendShapeForIntGroup(List<SwitchGeneratorConfig.TargetItem> list, int index)
        {
            if (list == null || index < 0 || index >= list.Count) return;
            var it = list[index];
            if (it == null || it.targetObject == null) return;
            var next = NextUnusedBlendShapeWithinList(it.targetObject, it.blendShapeName, list);
            if (string.IsNullOrEmpty(next)) return;

            var copy = SwitchGeneratorLayerConfigEditing.CloneTargetItem(it);
            copy.controlType = SwitchGeneratorConfig.TargetControlType.BlendShape;
            copy.blendShapeName = next;
            list.Insert(index + 1, copy);
            if (_previewSession.IsPreviewing)
            {
                var layer = GetSelectedLayer();
                ApplyPreviewStateInt(Mathf.Clamp(Mathf.RoundToInt(_previewValue), 0, Mathf.Max(0, layer.intGroups.Count - 1)));
            }
        }

        private void DrawInvisibleObjectFieldExpand()
        {
            var prevColor = GUI.color;
            using (new EditorGUI.DisabledScope(true))
            {
                GUI.color = new Color(prevColor.r, prevColor.g, prevColor.b, 0f);
                Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, EditorStyles.objectField, GUILayout.ExpandWidth(true));
                EditorGUI.ObjectField(rect, GUIContent.none, null, typeof(Object), true);
                GUI.color = prevColor;
            }
        }

        private void TryAddAnotherBlendShapeForItem(List<SwitchGeneratorConfig.TargetItem> list, int index, bool includeSecondary = false)
        {
            if (index < 0 || index >= list.Count) return;
            var it = list[index];
            if (it == null || it.targetObject == null) return;
            var next = NextUnusedBlendShapeWithinList(it.targetObject, it.blendShapeName, list, includeSecondary);
            if (string.IsNullOrEmpty(next)) return;
            var copy = new SwitchGeneratorConfig.TargetItem
            {
                targetObject = it.targetObject,
                controlType = SwitchGeneratorConfig.TargetControlType.BlendShape,
                blendShapeName = next,
                floatDirection = it.floatDirection,
            };
            list.Insert(index + 1, copy);
        }
    }
}
