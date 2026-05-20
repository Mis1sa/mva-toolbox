using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.AnimatorParameterTool
{
    internal sealed class AnimatorParameterScanWindow
    {
        private readonly AnimatorParameterToolWindow _host;
        private GameObject _lastScanRoot;
        private AnimatorParameterScanService.ScanResult _scanResult;
        private Vector2 _scanScrollPosition;
        private bool _selectAll;
        private bool _overwriteExisting;
        private bool _filterUnregistered;

        internal AnimatorParameterScanWindow(AnimatorParameterToolWindow host)
        {
            _host = host;
        }

        internal void Reset()
        {
            _lastScanRoot = null;
            _scanResult = null;
            _scanScrollPosition = Vector2.zero;
            _selectAll = false;
            _overwriteExisting = false;
            _filterUnregistered = false;
        }

        internal void OnGUI()
        {
            GameObject sceneRoot = _host.SceneFeatureRoot;
            if (sceneRoot != _lastScanRoot)
            {
                _lastScanRoot = sceneRoot;
                _scanResult = sceneRoot != null ? AnimatorParameterScanService.Execute(sceneRoot) : null;
                _selectAll = false;
            }

            if (sceneRoot == null)
            {
                EditorGUILayout.HelpBox("当前目标不是场景物体，无法扫描参数。", MessageType.Info);
                return;
            }

            if (_scanResult == null || _scanResult.AllParameters.Count == 0)
            {
                EditorGUILayout.HelpBox("未找到任何参数。请确保目标对象包含 VRC Contact Receiver 或 VRC Phys Bone 组件。", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            bool newSelectAll = EditorGUILayout.ToggleLeft("全选", _selectAll, GUILayout.Width(80f));
            if (newSelectAll != _selectAll)
            {
                _selectAll = newSelectAll;
                HashSet<string> registered = null;
                AnimatorController controller = _host.SelectedController;
                if (_filterUnregistered && controller != null)
                {
                    registered = new HashSet<string>();
                    AnimatorControllerParameter[] ctrlParams = controller.parameters ?? Array.Empty<AnimatorControllerParameter>();
                    for (int i = 0; i < ctrlParams.Length; i++)
                    {
                        AnimatorControllerParameter cp = ctrlParams[i];
                        if (cp != null && !string.IsNullOrEmpty(cp.name))
                        {
                            registered.Add(cp.name);
                        }
                    }
                }

                for (int i = 0; i < _scanResult.AllParameters.Count; i++)
                {
                    AnimatorParameterScanService.ParameterInfo param = _scanResult.AllParameters[i];
                    if (registered != null && !string.IsNullOrEmpty(param.Name) && registered.Contains(param.Name))
                    {
                        continue;
                    }

                    param.IsSelected = _selectAll;
                }
            }

            GUILayout.Space(10f);
            _overwriteExisting = EditorGUILayout.ToggleLeft("覆盖参数", _overwriteExisting, GUILayout.Width(100f));
            GUILayout.Space(10f);
            _filterUnregistered = EditorGUILayout.ToggleLeft("筛选未注册的参数", _filterUnregistered);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("参数列表 (来自 Scene)", EditorStyles.boldLabel);

            AnimatorController selectedController = _host.SelectedController;
            HashSet<string> registeredSet = null;
            if (_filterUnregistered && selectedController != null)
            {
                registeredSet = new HashSet<string>();
                AnimatorControllerParameter[] ctrlParams = selectedController.parameters ?? Array.Empty<AnimatorControllerParameter>();
                for (int i = 0; i < ctrlParams.Length; i++)
                {
                    AnimatorControllerParameter cp = ctrlParams[i];
                    if (cp != null && !string.IsNullOrEmpty(cp.name))
                    {
                        registeredSet.Add(cp.name);
                    }
                }
            }

            _scanScrollPosition = EditorGUILayout.BeginScrollView(_scanScrollPosition, GUILayout.Height(320f));
            for (int i = 0; i < _scanResult.ContactReceiverParameters.Count; i++)
            {
                AnimatorParameterScanService.ParameterInfo param = _scanResult.ContactReceiverParameters[i];
                if (registeredSet != null && !string.IsNullOrEmpty(param.Name) && registeredSet.Contains(param.Name))
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawScanParameterRow(param, false);
                EditorGUILayout.EndVertical();
            }

            for (int i = 0; i < _scanResult.PhysBoneGroups.Count; i++)
            {
                AnimatorParameterScanService.PhysBoneParameterGroup group = _scanResult.PhysBoneGroups[i];
                bool anyVisible = true;
                if (registeredSet != null)
                {
                    anyVisible = false;
                    for (int j = 0; j < group.Parameters.Count; j++)
                    {
                        AnimatorParameterScanService.ParameterInfo param = group.Parameters[j];
                        if (string.IsNullOrEmpty(param.Name) || !registeredSet.Contains(param.Name))
                        {
                            anyVisible = true;
                            break;
                        }
                    }
                }

                if (!anyVisible)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("VRC Phys Bone: " + group.BaseName, EditorStyles.boldLabel);
                GUILayout.Space(3f);
                for (int j = 0; j < group.Parameters.Count; j++)
                {
                    AnimatorParameterScanService.ParameterInfo param = group.Parameters[j];
                    if (registeredSet != null && !string.IsNullOrEmpty(param.Name) && registeredSet.Contains(param.Name))
                    {
                        continue;
                    }

                    DrawScanParameterRow(param, true);
                }
                EditorGUILayout.EndVertical();
                GUILayout.Space(3f);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4f);
            int selectedCount = _scanResult.AllParameters.Count(p => p.IsSelected);
            bool hasController = _host.SelectedController != null;
            EditorGUI.BeginDisabledGroup(_host.IsControllerAssetTarget || !hasController || selectedCount == 0);
            if (GUILayout.Button("添加参数到控制器", GUILayout.Height(32f)))
            {
                AnimatorParameterApplyService.ApplyResult result = AnimatorParameterApplyService.ApplyToController(_host.SelectedController, _scanResult.AllParameters, _overwriteExisting);
                string message = $"添加完成！\n新增: {result.AddedCount}\n覆盖: {result.OverwrittenCount}\n跳过: {result.SkippedCount}";
                if (result.Errors.Count > 0)
                {
                    message += $"\n错误: {result.Errors.Count}";
                }

                EditorUtility.DisplayDialog("完成", message, "确定");
            }
            EditorGUI.EndDisabledGroup();

            if (_host.IsControllerAssetTarget)
            {
                EditorGUILayout.HelpBox("当前目标是动画控制器资产，请改用“添加到 Parameters”功能。", MessageType.Info);
            }
            else if (!hasController)
            {
                EditorGUILayout.HelpBox("请先选择动画控制器。", MessageType.Warning);
            }
        }

        private static void DrawScanParameterRow(AnimatorParameterScanService.ParameterInfo param, bool indent)
        {
            if (indent)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(20f);
                EditorGUILayout.BeginVertical();
            }

            EditorGUILayout.BeginHorizontal();
            param.IsSelected = EditorGUILayout.Toggle(param.IsSelected, GUILayout.Width(20f));
            EditorGUILayout.LabelField("参数名:", GUILayout.Width(60f));
            EditorGUILayout.LabelField(param.Name, EditorStyles.boldLabel, GUILayout.MinWidth(120f));
            EditorGUILayout.LabelField("类型:", GUILayout.Width(40f));
            string[] typeOptionLabels = { "Bool", "Float", "Int" };
            int currentTypeIndex = param.Type == AnimatorControllerParameterType.Bool ? 0 : param.Type == AnimatorControllerParameterType.Float ? 1 : 2;
            int newTypeIndex = EditorGUILayout.Popup(currentTypeIndex, typeOptionLabels, GUILayout.Width(80f));
            param.Type = newTypeIndex == 0 ? AnimatorControllerParameterType.Bool : newTypeIndex == 1 ? AnimatorControllerParameterType.Float : AnimatorControllerParameterType.Int;
            EditorGUILayout.LabelField("默认值:", GUILayout.Width(60f));
            switch (param.Type)
            {
                case AnimatorControllerParameterType.Bool:
                    param.DefaultBool = EditorGUILayout.Toggle(param.DefaultBool, GUILayout.Width(80f));
                    break;
                case AnimatorControllerParameterType.Float:
                    param.DefaultFloat = EditorGUILayout.FloatField(param.DefaultFloat, GUILayout.Width(80f));
                    break;
                case AnimatorControllerParameterType.Int:
                    param.DefaultInt = EditorGUILayout.IntField(param.DefaultInt, GUILayout.Width(80f));
                    break;
            }
            EditorGUILayout.EndHorizontal();

            if (indent)
            {
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
            }
        }
    }
}
