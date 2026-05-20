using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.AnimatorParameterTool
{
    internal sealed class AnimatorParameterCheckWindow
    {
        private sealed class ParameterIssueUI
        {
            public AnimatorParameterCheckService.ParameterIssue Issue;
            public int SelectedOptionIndex;
            public int SelectedExistingParamIndex;
            public bool RemoveUnused;
            public bool ApplyTypeFix;
            public AnimatorControllerParameterType TypeFixTarget;
            public bool FixBrokenPPtr;
        }

        private static readonly string[] MissingReferenceOptions = { "忽略", "置空引用", "替换为已有参数" };
        private static readonly AnimatorControllerParameterType[] TypeOptionValues =
        {
            AnimatorControllerParameterType.Bool,
            AnimatorControllerParameterType.Float,
            AnimatorControllerParameterType.Int
        };
        private static readonly string[] TypeOptionLabels = { "Bool", "Float", "Int" };

        private readonly AnimatorParameterToolWindow _host;
        private AnimatorParameterCheckService.CheckResult _checkResult;
        private Vector2 _checkScrollPosition;
        private readonly List<ParameterIssueUI> _issueUIs = new List<ParameterIssueUI>();
        private bool _unusedSelectAll;

        internal AnimatorParameterCheckWindow(AnimatorParameterToolWindow host)
        {
            _host = host;
        }

        internal void Reset()
        {
            _checkResult = null;
            _checkScrollPosition = Vector2.zero;
            _issueUIs.Clear();
            _unusedSelectAll = false;
        }

        internal void OnGUI()
        {
            AnimatorController controller = _host.SelectedController;
            if (controller == null)
            {
                EditorGUILayout.HelpBox("请先选择动画控制器。", MessageType.Warning);
                return;
            }

            if (GUILayout.Button("开始检查", GUILayout.Height(30f)))
            {
                _checkResult = AnimatorParameterCheckService.Execute(controller);
                BuildIssueUIs();
            }

            if (_checkResult == null)
            {
                EditorGUILayout.HelpBox("点击“开始检查”以分析当前控制器的参数使用情况。", MessageType.Info);
                return;
            }

            if (!_checkResult.HasIssues)
            {
                EditorGUILayout.HelpBox("未发现参数问题。", MessageType.Info);
                return;
            }

            AnimatorControllerParameter[] controllerParams = controller.parameters ?? Array.Empty<AnimatorControllerParameter>();
            string[] controllerParamNames = controllerParams.Where(p => p != null && !string.IsNullOrEmpty(p.name)).Select(p => p.name).ToArray();

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField($"发现 {_issueUIs.Count} 个问题", EditorStyles.boldLabel);
            List<ParameterIssueUI> missingIssues = _issueUIs.Where(u => u.Issue.Type == AnimatorParameterCheckService.IssueType.MissingReference).ToList();
            List<ParameterIssueUI> unusedIssues = _issueUIs.Where(u => u.Issue.Type == AnimatorParameterCheckService.IssueType.UnusedParameter).ToList();
            List<ParameterIssueUI> mismatchIssues = _issueUIs.Where(u => u.Issue.Type == AnimatorParameterCheckService.IssueType.TypeMismatch).ToList();
            List<ParameterIssueUI> brokenPPtrIssues = _issueUIs.Where(u => u.Issue.Type == AnimatorParameterCheckService.IssueType.BrokenPPtr).ToList();

            _checkScrollPosition = EditorGUILayout.BeginScrollView(_checkScrollPosition);
            if (missingIssues.Count > 0)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("缺失参数引用", EditorStyles.boldLabel);
                for (int i = 0; i < missingIssues.Count; i++)
                {
                    DrawMissingReferenceRow(missingIssues[i], controllerParamNames);
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(8f);
            }

            if (unusedIssues.Count > 0)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("无用参数", EditorStyles.boldLabel);
                if (GUILayout.Button(_unusedSelectAll ? "全不选" : "全选", GUILayout.Width(60f)))
                {
                    _unusedSelectAll = !_unusedSelectAll;
                    for (int i = 0; i < unusedIssues.Count; i++)
                    {
                        unusedIssues[i].RemoveUnused = _unusedSelectAll;
                    }
                }
                EditorGUILayout.EndHorizontal();
                for (int i = 0; i < unusedIssues.Count; i++)
                {
                    DrawUnusedParameterRow(unusedIssues[i]);
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(8f);
            }

            if (mismatchIssues.Count > 0)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("参数类型不匹配", EditorStyles.boldLabel);
                for (int i = 0; i < mismatchIssues.Count; i++)
                {
                    DrawTypeMismatchRow(mismatchIssues[i]);
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(8f);
            }

            if (brokenPPtrIssues.Count > 0)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Broken PPtr", EditorStyles.boldLabel);
                for (int i = 0; i < brokenPPtrIssues.Count; i++)
                {
                    DrawBrokenPPtrRow(brokenPPtrIssues[i]);
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4f);
            if (GUILayout.Button("应用参数检查修复", GUILayout.Height(30f)))
            {
                ApplyCheckFixes(controllerParamNames);
            }
        }

        private void BuildIssueUIs()
        {
            _issueUIs.Clear();
            if (_checkResult == null || !_checkResult.HasIssues)
            {
                return;
            }

            _unusedSelectAll = false;
            for (int i = 0; i < _checkResult.Issues.Count; i++)
            {
                AnimatorParameterCheckService.ParameterIssue issue = _checkResult.Issues[i];
                _issueUIs.Add(new ParameterIssueUI
                {
                    Issue = issue,
                    SelectedOptionIndex = 0,
                    SelectedExistingParamIndex = 0,
                    RemoveUnused = false,
                    ApplyTypeFix = false,
                    TypeFixTarget = issue.ExpectedType ?? issue.ActualType ?? AnimatorControllerParameterType.Float,
                    FixBrokenPPtr = issue.Type == AnimatorParameterCheckService.IssueType.BrokenPPtr
                });
            }
        }

        private static void DrawMissingReferenceRow(ParameterIssueUI ui, string[] controllerParamNames)
        {
            AnimatorParameterCheckService.ParameterIssue issue = ui.Issue;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"参数名: {issue.ParameterName}", EditorStyles.boldLabel);
            if (issue.ExpectedType.HasValue)
            {
                EditorGUILayout.LabelField($"期望类型: {issue.ExpectedType.Value}", EditorStyles.miniLabel);
            }

            int maxRefs = Mathf.Min(issue.References.Count, 3);
            for (int i = 0; i < maxRefs; i++)
            {
                EditorGUILayout.LabelField($"• {issue.References[i].Description}", EditorStyles.miniLabel);
            }
            if (issue.References.Count > 3)
            {
                EditorGUILayout.LabelField($"... 以及其他 {issue.References.Count - 3} 处", EditorStyles.miniLabel);
            }

            ui.SelectedOptionIndex = EditorGUILayout.Popup("修复方式", ui.SelectedOptionIndex, MissingReferenceOptions);
            if (ui.SelectedOptionIndex == 2)
            {
                if (controllerParamNames.Length == 0)
                {
                    EditorGUILayout.HelpBox("当前控制器中没有可供替换的已定义参数。", MessageType.Warning);
                }
                else
                {
                    ui.SelectedExistingParamIndex = Mathf.Clamp(ui.SelectedExistingParamIndex, 0, controllerParamNames.Length - 1);
                    ui.SelectedExistingParamIndex = EditorGUILayout.Popup("替换为", ui.SelectedExistingParamIndex, controllerParamNames);
                }
            }
            EditorGUILayout.EndVertical();
        }

        private static void DrawUnusedParameterRow(ParameterIssueUI ui)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"{ui.Issue.ParameterName} ({ui.Issue.ActualType})", GUILayout.ExpandWidth(true));
            ui.RemoveUnused = EditorGUILayout.ToggleLeft("移除", ui.RemoveUnused, GUILayout.Width(60f));
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawTypeMismatchRow(ParameterIssueUI ui)
        {
            AnimatorParameterCheckService.ParameterIssue issue = ui.Issue;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"参数名: {issue.ParameterName}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"当前类型: {issue.ActualType}", EditorStyles.miniLabel);
            if (issue.ExpectedType.HasValue)
            {
                EditorGUILayout.LabelField($"引用期望: {issue.ExpectedType.Value}", EditorStyles.miniLabel);
            }

            int maxRefs = Mathf.Min(issue.References.Count, 3);
            for (int i = 0; i < maxRefs; i++)
            {
                EditorGUILayout.LabelField($"• {issue.References[i].Description}", EditorStyles.miniLabel);
            }
            if (issue.References.Count > 3)
            {
                EditorGUILayout.LabelField($"... 以及其他 {issue.References.Count - 3} 处", EditorStyles.miniLabel);
            }

            ui.ApplyTypeFix = EditorGUILayout.ToggleLeft("同步该参数类型并自动修复条件", ui.ApplyTypeFix);
            using (new EditorGUI.DisabledGroupScope(!ui.ApplyTypeFix))
            {
                int typeIndex = Array.IndexOf(TypeOptionValues, ui.TypeFixTarget);
                if (typeIndex < 0)
                {
                    typeIndex = 0;
                }
                typeIndex = EditorGUILayout.Popup("目标类型", typeIndex, TypeOptionLabels);
                ui.TypeFixTarget = TypeOptionValues[Mathf.Clamp(typeIndex, 0, TypeOptionValues.Length - 1)];
            }
            EditorGUILayout.EndVertical();
        }

        private static void DrawBrokenPPtrRow(ParameterIssueUI ui)
        {
            AnimatorParameterCheckService.ParameterIssue issue = ui.Issue;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"资源: {issue.ParameterName}", EditorStyles.boldLabel);
            int maxRefs = Mathf.Min(issue.References.Count, 5);
            for (int i = 0; i < maxRefs; i++)
            {
                EditorGUILayout.LabelField($"• {issue.References[i].Description}", EditorStyles.miniLabel);
            }
            if (issue.References.Count > 5)
            {
                EditorGUILayout.LabelField($"... 以及其他 {issue.References.Count - 5} 处", EditorStyles.miniLabel);
            }
            ui.FixBrokenPPtr = EditorGUILayout.ToggleLeft("修复（将损坏引用置为 {fileID: 0}）", ui.FixBrokenPPtr);
            EditorGUILayout.EndVertical();
        }

        private void ApplyCheckFixes(string[] controllerParamNames)
        {
            if (_host.SelectedController == null || _issueUIs.Count == 0)
            {
                return;
            }

            int fixCount = 0;
            List<ParameterIssueUI> missingReferenceUis = _issueUIs.Where(u => u.Issue.Type == AnimatorParameterCheckService.IssueType.MissingReference).ToList();
            for (int i = 0; i < missingReferenceUis.Count; i++)
            {
                ParameterIssueUI ui = missingReferenceUis[i];
                if (ui.SelectedOptionIndex == 0)
                {
                    continue;
                }

                string fixOption = ui.SelectedOptionIndex == 1 ? "Remove" : "UseExisting";
                string target = null;
                if (ui.SelectedOptionIndex == 2 && controllerParamNames.Length > 0 && ui.SelectedExistingParamIndex >= 0 && ui.SelectedExistingParamIndex < controllerParamNames.Length)
                {
                    target = controllerParamNames[ui.SelectedExistingParamIndex];
                }

                if (fixOption == "UseExisting" && string.IsNullOrEmpty(target))
                {
                    continue;
                }

                if (AnimatorParameterCheckService.FixMissingReference(_host.SelectedController, ui.Issue, fixOption, target))
                {
                    fixCount++;
                }
            }

            List<ParameterIssueUI> unusedUis = _issueUIs.Where(u => u.Issue.Type == AnimatorParameterCheckService.IssueType.UnusedParameter).ToList();
            for (int i = 0; i < unusedUis.Count; i++)
            {
                ParameterIssueUI ui = unusedUis[i];
                if (ui.RemoveUnused && AnimatorParameterCheckService.RemoveUnusedParameter(_host.SelectedController, ui.Issue.ParameterName))
                {
                    fixCount++;
                }
            }

            List<ParameterIssueUI> mismatchUis = _issueUIs.Where(u => u.Issue.Type == AnimatorParameterCheckService.IssueType.TypeMismatch).ToList();
            for (int i = 0; i < mismatchUis.Count; i++)
            {
                ParameterIssueUI ui = mismatchUis[i];
                if (ui.ApplyTypeFix && AnimatorParameterCheckService.FixTypeMismatch(_host.SelectedController, ui.Issue, ui.TypeFixTarget))
                {
                    fixCount++;
                }
            }

            bool shouldFixBrokenPPtr = _issueUIs.Any(u => u.Issue.Type == AnimatorParameterCheckService.IssueType.BrokenPPtr && u.FixBrokenPPtr);
            if (shouldFixBrokenPPtr && AnimatorParameterCheckService.FixBrokenPPtrs(_host.SelectedController))
            {
                fixCount++;
            }

            if (fixCount > 0)
            {
                EditorUtility.DisplayDialog("完成", $"已应用 {fixCount} 项修复。", "确定");
                _checkResult = AnimatorParameterCheckService.Execute(_host.SelectedController);
                BuildIssueUIs();
            }
            else
            {
                EditorUtility.DisplayDialog("提示", "未选择任何需要应用的修复。", "确定");
            }
        }
    }
}
