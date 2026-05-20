using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.AnimatorParameterTool
{
    internal sealed class AnimatorParameterTraceWindow
    {
        private readonly AnimatorParameterToolWindow _host;
        private AnimatorParameterTraceService.TraceResult _traceResult;
        private Vector2 _traceScrollPosition;
        private int _traceParameterIndex;
        private AnimatorController _traceLastController;

        internal AnimatorParameterTraceWindow(AnimatorParameterToolWindow host)
        {
            _host = host;
        }

        internal void Reset()
        {
            _traceResult = null;
            _traceScrollPosition = Vector2.zero;
            _traceParameterIndex = 0;
            _traceLastController = null;
        }

        internal void OnGUI()
        {
            AnimatorController controller = _host.SelectedController;
            if (controller == null)
            {
                EditorGUILayout.HelpBox("请先选择动画控制器。", MessageType.Warning);
                return;
            }

            AnimatorControllerParameter[] parameters = controller.parameters ?? Array.Empty<AnimatorControllerParameter>();
            if (parameters.Length == 0)
            {
                EditorGUILayout.HelpBox("当前动画控制器中没有参数。", MessageType.Info);
                return;
            }

            if (_traceLastController != controller)
            {
                _traceLastController = controller;
                _traceParameterIndex = 0;
                _traceResult = null;
            }

            _traceParameterIndex = Mathf.Clamp(_traceParameterIndex, 0, parameters.Length - 1);
            string[] parameterNames = parameters.Select(x => x.name).ToArray();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("参数", GUILayout.Width(80f));
            _traceParameterIndex = EditorGUILayout.Popup(_traceParameterIndex, parameterNames);
            EditorGUILayout.EndHorizontal();

            GameObject sceneRoot = _host.SceneFeatureRoot;
            if (sceneRoot == null)
            {
                EditorGUILayout.HelpBox("当前目标是控制器资产，仅分析该控制器。", MessageType.Info);
            }

            if (GUILayout.Button("开始追踪", GUILayout.Height(30f)))
            {
                string parameterName = parameters[_traceParameterIndex].name;
                _traceResult = AnimatorParameterTraceService.Execute(controller, parameterName, sceneRoot);
            }

            if (_traceResult == null)
            {
                EditorGUILayout.HelpBox("选择参数后点击“开始追踪”。", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField($"参数: {_traceResult.ParameterName} | 引用: {_traceResult.References.Count} | 修改: {_traceResult.Modifications.Count}", EditorStyles.boldLabel);
            if (!_traceResult.HasAny)
            {
                EditorGUILayout.HelpBox("未找到该参数的引用或修改来源。", MessageType.Info);
                return;
            }

            _traceScrollPosition = EditorGUILayout.BeginScrollView(_traceScrollPosition, GUILayout.Height(360f));
            DrawTraceGroup("引用（读取）", _traceResult.References, true);
            EditorGUILayout.Space(6f);
            DrawTraceGroup("修改（写入）", _traceResult.Modifications, false);
            EditorGUILayout.EndScrollView();
        }

        private void DrawTraceGroup(string title, List<AnimatorParameterTraceService.TraceEntry> entries, bool isReference)
        {
            int count = entries != null ? entries.Count : 0;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"{title} ({count})", EditorStyles.boldLabel);
            if (entries == null || entries.Count == 0)
            {
                EditorGUILayout.HelpBox("无记录。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                AnimatorParameterTraceService.TraceEntry entry = entries[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                if (isReference)
                {
                    DrawTraceReferenceEntry(entry);
                }
                else
                {
                    DrawTraceModificationEntry(entry);
                }
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndVertical();
        }

        private static void DrawTraceReferenceEntry(AnimatorParameterTraceService.TraceEntry entry)
        {
            EditorGUILayout.LabelField(entry.Description, EditorStyles.boldLabel);
            DrawTraceLabel("层级", entry.LayerName);
            if (entry.SourceKind == AnimatorParameterTraceService.TraceSourceKind.TransitionCondition)
            {
                DrawTraceLabel("源状态", entry.SourceState);
                DrawTraceLabel("目标状态", entry.DestinationState);
            }
            else
            {
                DrawTraceLabel("源状态", entry.SourceState);
            }

            if (!string.IsNullOrEmpty(entry.ComponentName))
            {
                DrawTraceLabel("组件名", entry.ComponentName);
            }

            if (entry.SourceKind == AnimatorParameterTraceService.TraceSourceKind.BlendTreeParameter)
            {
                DrawTraceLabel("混合树", entry.BlendTreePath);
            }
        }

        private static void DrawTraceModificationEntry(AnimatorParameterTraceService.TraceEntry entry)
        {
            EditorGUILayout.LabelField(entry.Description, EditorStyles.boldLabel);
            if (entry.SourceKind == AnimatorParameterTraceService.TraceSourceKind.AnimationCurve)
            {
                if (entry.RelatedObject != null)
                {
                    EditorGUILayout.ObjectField("动画剪辑", entry.RelatedObject, typeof(AnimationClip), false);
                }
                return;
            }

            if (entry.SourceKind == AnimatorParameterTraceService.TraceSourceKind.AvatarParameterDriverTarget)
            {
                DrawTraceLabel("层级", entry.LayerName);
                DrawTraceLabel("状态", entry.SourceState);
                DrawTraceLabel("组件名", entry.ComponentName);
                return;
            }

            if (entry.SourceKind == AnimatorParameterTraceService.TraceSourceKind.SceneComponent)
            {
                DrawTraceLabel("层级（Hierarchy）", entry.HierarchyPath);
                DrawTraceLabel("组件名", entry.ComponentName);
                return;
            }

            DrawTraceLabel("层级", entry.LayerName);
            DrawTraceLabel("状态", entry.SourceState);
            if (!string.IsNullOrEmpty(entry.ComponentName))
            {
                DrawTraceLabel("组件名", entry.ComponentName);
            }
        }

        private static void DrawTraceLabel(string label, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                EditorGUILayout.LabelField(label + ": " + value, EditorStyles.miniLabel);
            }
        }
    }
}
