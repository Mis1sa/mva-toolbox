using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace MVA.Toolbox.AnimatorParameterTool
{
    internal sealed class AnimatorParameterApplyWindow
    {
        private readonly AnimatorParameterToolWindow _host;
        private VRCExpressionParameters _expressionParameters;
        private readonly Dictionary<string, bool> _parameterSelectFlags = new Dictionary<string, bool>();
        private readonly Dictionary<string, AnimatorControllerParameterType> _parameterTypeOverrides = new Dictionary<string, AnimatorControllerParameterType>();
        private readonly Dictionary<string, float> _parameterDefaultOverrides = new Dictionary<string, float>();
        private readonly Dictionary<string, bool> _parameterSaveFlags = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> _parameterSyncFlags = new Dictionary<string, bool>();
        private AnimatorController _applyLastController;
        private Vector2 _scrollPosition;
        private bool _selectAll;
        private bool _filterUnregistered;

        internal AnimatorParameterApplyWindow(AnimatorParameterToolWindow host)
        {
            _host = host;
        }

        internal void OnTargetChanged(VRCExpressionParameters autoBoundExpressionParameters)
        {
            _expressionParameters = autoBoundExpressionParameters;
            _applyLastController = null;
            _scrollPosition = Vector2.zero;
            _selectAll = false;
            _filterUnregistered = false;
            _parameterSelectFlags.Clear();
            _parameterTypeOverrides.Clear();
            _parameterDefaultOverrides.Clear();
            _parameterSaveFlags.Clear();
            _parameterSyncFlags.Clear();
        }

        internal void OnGUI()
        {
            AnimatorController selectedController = _host.SelectedController;
            if (selectedController == null)
            {
                EditorGUILayout.HelpBox("请先选择动画控制器。", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("目标 ExpressionParameters", EditorStyles.boldLabel);
            _expressionParameters = (VRCExpressionParameters)EditorGUILayout.ObjectField("ExpressionParameters", _expressionParameters, typeof(VRCExpressionParameters), false);
            if (_expressionParameters == null)
            {
                EditorGUILayout.HelpBox("请选择一个 VRCExpressionParameters 资源。", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(4f);
            AnimatorControllerParameter[] controllerParams = selectedController.parameters;
            if (controllerParams == null || controllerParams.Length == 0)
            {
                EditorGUILayout.HelpBox("当前控制器中没有任何参数。", MessageType.Info);
                return;
            }

            if (_applyLastController != selectedController)
            {
                _applyLastController = selectedController;
                _selectAll = false;
                _parameterSelectFlags.Clear();
                _parameterTypeOverrides.Clear();
                _parameterDefaultOverrides.Clear();
                _parameterSaveFlags.Clear();
                _parameterSyncFlags.Clear();

                _host.TryGetMAParameterDefaults(selectedController, out Dictionary<string, (bool saved, bool synced)> maDefaults);
                for (int i = 0; i < controllerParams.Length; i++)
                {
                    AnimatorControllerParameter p = controllerParams[i];
                    if (p == null || string.IsNullOrEmpty(p.name))
                    {
                        continue;
                    }

                    _parameterSelectFlags[p.name] = false;
                    if (maDefaults != null && maDefaults.TryGetValue(p.name, out (bool saved, bool synced) d))
                    {
                        _parameterSaveFlags[p.name] = d.saved;
                        _parameterSyncFlags[p.name] = d.synced;
                    }
                    else
                    {
                        _parameterSaveFlags[p.name] = false;
                        _parameterSyncFlags[p.name] = false;
                    }
                }
            }

            EditorGUILayout.BeginHorizontal();
            bool newSelectAll = EditorGUILayout.ToggleLeft("全选", _selectAll, GUILayout.Width(80f));
            if (newSelectAll != _selectAll)
            {
                _selectAll = newSelectAll;
                var existingNames = new HashSet<string>();
                if (_filterUnregistered && _expressionParameters != null && _expressionParameters.parameters != null)
                {
                    for (int i = 0; i < _expressionParameters.parameters.Length; i++)
                    {
                        VRCExpressionParameters.Parameter p = _expressionParameters.parameters[i];
                        if (!string.IsNullOrEmpty(p.name))
                        {
                            existingNames.Add(p.name);
                        }
                    }
                }

                for (int i = 0; i < controllerParams.Length; i++)
                {
                    AnimatorControllerParameter param = controllerParams[i];
                    if (param == null || string.IsNullOrEmpty(param.name))
                    {
                        continue;
                    }

                    if (_filterUnregistered && existingNames.Contains(param.name))
                    {
                        continue;
                    }

                    _parameterSelectFlags[param.name] = _selectAll;
                }
            }

            GUILayout.Space(10f);
            _filterUnregistered = EditorGUILayout.ToggleLeft("筛选未注册的参数", _filterUnregistered);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("参数列表", EditorStyles.boldLabel);

            var existingInParameters = new HashSet<string>();
            if (_expressionParameters != null && _expressionParameters.parameters != null)
            {
                for (int i = 0; i < _expressionParameters.parameters.Length; i++)
                {
                    VRCExpressionParameters.Parameter p = _expressionParameters.parameters[i];
                    if (!string.IsNullOrEmpty(p.name))
                    {
                        existingInParameters.Add(p.name);
                    }
                }
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(320f));
            for (int i = 0; i < controllerParams.Length; i++)
            {
                AnimatorControllerParameter param = controllerParams[i];
                if (param == null || string.IsNullOrEmpty(param.name))
                {
                    continue;
                }

                if (_filterUnregistered && existingInParameters.Contains(param.name))
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawApplyParameterRow(param);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4f);
            int selectedCount = _parameterSelectFlags.Values.Count(v => v);
            EditorGUI.BeginDisabledGroup(selectedCount == 0);
            if (GUILayout.Button("添加到 Parameters", GUILayout.Height(32f)))
            {
                ApplyToExpressionParameters();
            }
            EditorGUI.EndDisabledGroup();

            if (selectedCount == 0)
            {
                EditorGUILayout.HelpBox("请至少选择一个参数。", MessageType.Warning);
            }
        }

        private void ApplyToExpressionParameters()
        {
            try
            {
                AnimatorParameterApplyService.ApplyToExpressionParameters(_host.SelectedController, _expressionParameters, _parameterSelectFlags, _parameterSaveFlags, _parameterSyncFlags, _parameterTypeOverrides, _parameterDefaultOverrides, _filterUnregistered);
                int selectedCount = _parameterSelectFlags.Values.Count(v => v);
                EditorUtility.DisplayDialog("完成", $"已写入 {selectedCount} 个参数到 ExpressionParameters。", "确定");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("错误", "写入失败: " + ex.Message, "确定");
            }
        }

        private void DrawApplyParameterRow(AnimatorControllerParameter param)
        {
            EditorGUILayout.BeginHorizontal();
            bool isSelected = _parameterSelectFlags.TryGetValue(param.name, out bool selected) && selected;
            bool newSelected = EditorGUILayout.Toggle(isSelected, GUILayout.Width(20f));
            if (newSelected != isSelected)
            {
                _parameterSelectFlags[param.name] = newSelected;
            }

            EditorGUILayout.LabelField("参数名:", GUILayout.Width(60f));
            EditorGUILayout.LabelField(param.name, EditorStyles.boldLabel, GUILayout.MinWidth(120f));
            EditorGUILayout.LabelField("类型:", GUILayout.Width(40f));
            string[] typeOptionLabels = { "Bool", "Float", "Int" };
            AnimatorControllerParameterType overrideType = _parameterTypeOverrides.TryGetValue(param.name, out AnimatorControllerParameterType t) ? t : param.type;
            int currentTypeIndex = overrideType == AnimatorControllerParameterType.Bool ? 0 : overrideType == AnimatorControllerParameterType.Float ? 1 : 2;
            int newTypeIndex = EditorGUILayout.Popup(currentTypeIndex, typeOptionLabels, GUILayout.Width(80f));
            AnimatorControllerParameterType newType = newTypeIndex == 0 ? AnimatorControllerParameterType.Bool : newTypeIndex == 1 ? AnimatorControllerParameterType.Float : AnimatorControllerParameterType.Int;
            _parameterTypeOverrides[param.name] = newType;

            EditorGUILayout.LabelField("默认值:", GUILayout.Width(60f));
            float defaultValue;
            if (!_parameterDefaultOverrides.TryGetValue(param.name, out defaultValue))
            {
                defaultValue = param.type == AnimatorControllerParameterType.Bool
                    ? (param.defaultBool ? 1f : 0f)
                    : param.type == AnimatorControllerParameterType.Float
                        ? param.defaultFloat
                        : param.defaultInt;
            }

            switch (newType)
            {
                case AnimatorControllerParameterType.Bool:
                {
                    bool b = defaultValue >= 0.5f;
                    b = EditorGUILayout.Toggle(b, GUILayout.Width(80f));
                    _parameterDefaultOverrides[param.name] = b ? 1f : 0f;
                    break;
                }
                case AnimatorControllerParameterType.Float:
                {
                    float v = EditorGUILayout.FloatField(defaultValue, GUILayout.Width(80f));
                    _parameterDefaultOverrides[param.name] = v;
                    break;
                }
                case AnimatorControllerParameterType.Int:
                {
                    int v = EditorGUILayout.IntField(Mathf.RoundToInt(defaultValue), GUILayout.Width(80f));
                    _parameterDefaultOverrides[param.name] = v;
                    break;
                }
            }

            bool isSaved = _parameterSaveFlags.TryGetValue(param.name, out bool saved) && saved;
            bool newSaved = EditorGUILayout.ToggleLeft("保存", isSaved, GUILayout.Width(60f));
            if (newSaved != isSaved)
            {
                _parameterSaveFlags[param.name] = newSaved;
            }

            bool isSynced = _parameterSyncFlags.TryGetValue(param.name, out bool synced) && synced;
            bool newSynced = EditorGUILayout.ToggleLeft("同步", isSynced, GUILayout.Width(60f));
            if (newSynced != isSynced)
            {
                _parameterSyncFlags[param.name] = newSynced;
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}
