using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.AnimatorParameterTool
{
    internal sealed class AnimatorParameterAdjustWindow
    {
        private readonly AnimatorParameterToolWindow _host;
        private const float AdjustFieldLabelWidth = 120f;
        private static readonly AnimatorControllerParameterType[] TypeOptionValues =
        {
            AnimatorControllerParameterType.Bool,
            AnimatorControllerParameterType.Float,
            AnimatorControllerParameterType.Int
        };
        private static readonly string[] TypeOptionLabels = { "Bool", "Float", "Int" };
        private static readonly string[] AdjustOperationLabels = { "名称", "类型", "替换", "移除" };

        private enum AdjustOperation
        {
            Rename,
            ChangeType,
            Swap,
            Remove
        }

        private int _adjustParameterIndex;
        private int _adjustSecondParameterIndex;
        private string _adjustRenameNewName = string.Empty;
        private bool _adjustRenameApplyAll;
        private AnimatorControllerParameterType _adjustTypeTargetType = AnimatorControllerParameterType.Float;
        private bool _adjustTypeApplyAll;
        private bool _adjustSwapApplyAll;
        private bool _adjustRemoveApplyAll;
        private AnimatorController _adjustLastController;
        private string _adjustLastParameterName;
        private AdjustOperation _adjustOperation = AdjustOperation.Rename;

        internal AnimatorParameterAdjustWindow(AnimatorParameterToolWindow host)
        {
            _host = host;
        }

        internal void Reset()
        {
            _adjustParameterIndex = 0;
            _adjustSecondParameterIndex = 0;
            _adjustRenameNewName = string.Empty;
            _adjustRenameApplyAll = false;
            _adjustTypeTargetType = AnimatorControllerParameterType.Float;
            _adjustTypeApplyAll = false;
            _adjustSwapApplyAll = false;
            _adjustRemoveApplyAll = false;
            _adjustLastController = null;
            _adjustLastParameterName = null;
            _adjustOperation = AdjustOperation.Rename;
        }

        internal void OnGUI()
        {
            AnimatorController controller = _host.SelectedController;
            if (controller == null)
            {
                EditorGUILayout.HelpBox("请先选择动画控制器。", MessageType.Warning);
                return;
            }

            if (_host.Controllers.Count == 0)
            {
                EditorGUILayout.HelpBox("当前目标中没有可用的动画控制器。", MessageType.Warning);
                return;
            }

            AnimatorControllerParameter[] parameters = controller.parameters ?? Array.Empty<AnimatorControllerParameter>();
            if (parameters.Length == 0)
            {
                EditorGUILayout.HelpBox("当前动画控制器中没有参数。", MessageType.Info);
                return;
            }

            EnsureAdjustSelection(controller, parameters);
            string[] parameterNames = parameters.Select(p => p.name).ToArray();
            _adjustParameterIndex = Mathf.Clamp(_adjustParameterIndex, 0, parameters.Length - 1);
            AnimatorControllerParameter selectedParameter = parameters[_adjustParameterIndex];

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("参数", GUILayout.Width(AdjustFieldLabelWidth));
            int newParameterIndex = EditorGUILayout.Popup(_adjustParameterIndex, parameterNames);
            EditorGUILayout.EndHorizontal();
            if (newParameterIndex != _adjustParameterIndex)
            {
                _adjustParameterIndex = newParameterIndex;
                selectedParameter = parameters[_adjustParameterIndex];
                _adjustTypeTargetType = selectedParameter.type;
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("操作", GUILayout.Width(AdjustFieldLabelWidth));
            int newOperationIndex = EditorGUILayout.Popup((int)_adjustOperation, AdjustOperationLabels);
            EditorGUILayout.EndHorizontal();
            if (newOperationIndex != (int)_adjustOperation)
            {
                _adjustOperation = (AdjustOperation)newOperationIndex;
            }

            EditorGUILayout.Space(6f);
            switch (_adjustOperation)
            {
                case AdjustOperation.Rename:
                    DrawAdjustRenameSection(selectedParameter);
                    break;
                case AdjustOperation.ChangeType:
                    DrawAdjustTypeSection(selectedParameter);
                    break;
                case AdjustOperation.Swap:
                    DrawAdjustSwapSection(selectedParameter, parameters);
                    break;
                case AdjustOperation.Remove:
                    DrawAdjustRemoveSection(selectedParameter);
                    break;
            }
        }

        private void EnsureAdjustSelection(AnimatorController controller, AnimatorControllerParameter[] parameters)
        {
            if (_adjustLastController != controller)
            {
                _adjustLastController = controller;
                _adjustParameterIndex = 0;
                _adjustSecondParameterIndex = 0;
                _adjustRenameNewName = string.Empty;
                _adjustTypeTargetType = parameters.Length > 0 ? parameters[0].type : AnimatorControllerParameterType.Float;
            }

            _adjustParameterIndex = Mathf.Clamp(_adjustParameterIndex, 0, Mathf.Max(0, parameters.Length - 1));
            if (parameters.Length > 0)
            {
                AnimatorControllerParameter selected = parameters[_adjustParameterIndex];
                if (_adjustLastParameterName != selected.name)
                {
                    _adjustLastParameterName = selected.name;
                    _adjustTypeTargetType = selected.type;
                }
            }
        }

        private void DrawAdjustRenameSection(AnimatorControllerParameter selectedParameter)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("新名称", GUILayout.Width(AdjustFieldLabelWidth));
            _adjustRenameNewName = EditorGUILayout.TextField(_adjustRenameNewName);
            EditorGUILayout.EndHorizontal();
            _adjustRenameApplyAll = EditorGUILayout.ToggleLeft("覆盖到全部控制器", _adjustRenameApplyAll);

            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_adjustRenameNewName) || _adjustRenameNewName == selectedParameter.name);
            if (GUILayout.Button("应用改名", GUILayout.Height(28f)))
            {
                ApplyAdjustAction("改名", controller => AnimatorParameterAdjustService.RenameParameter(controller, selectedParameter.name, _adjustRenameNewName), _adjustRenameApplyAll);
                _adjustRenameNewName = string.Empty;
            }
            EditorGUI.EndDisabledGroup();
        }

        private void DrawAdjustTypeSection(AnimatorControllerParameter selectedParameter)
        {
            int currentTypeIndex = Array.IndexOf(TypeOptionValues, _adjustTypeTargetType);
            if (currentTypeIndex < 0)
            {
                currentTypeIndex = 0;
            }
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("目标类型", GUILayout.Width(AdjustFieldLabelWidth));
            currentTypeIndex = EditorGUILayout.Popup(currentTypeIndex, TypeOptionLabels);
            EditorGUILayout.EndHorizontal();
            _adjustTypeTargetType = TypeOptionValues[currentTypeIndex];
            _adjustTypeApplyAll = EditorGUILayout.ToggleLeft("覆盖到全部控制器", _adjustTypeApplyAll);

            EditorGUI.BeginDisabledGroup(_adjustTypeTargetType == selectedParameter.type);
            if (GUILayout.Button("应用类型调整", GUILayout.Height(28f)))
            {
                ApplyAdjustAction("改类型", controller => AnimatorParameterAdjustService.ChangeParameterType(controller, selectedParameter.name, _adjustTypeTargetType), _adjustTypeApplyAll);
            }
            EditorGUI.EndDisabledGroup();
        }

        private void DrawAdjustSwapSection(AnimatorControllerParameter selectedParameter, AnimatorControllerParameter[] parameters)
        {
            AnimatorControllerParameter[] sameTypeParams = parameters.Where(p => p != null && p.type == selectedParameter.type && p.name != selectedParameter.name).ToArray();
            string[] sameTypeNames = sameTypeParams.Select(p => p.name).ToArray();
            if (sameTypeParams.Length == 0)
            {
                EditorGUILayout.HelpBox("没有同类型的其他参数可供替换。", MessageType.Info);
                return;
            }

            _adjustSecondParameterIndex = Mathf.Clamp(_adjustSecondParameterIndex, 0, sameTypeParams.Length - 1);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("替换对象", GUILayout.Width(AdjustFieldLabelWidth));
            _adjustSecondParameterIndex = EditorGUILayout.Popup(_adjustSecondParameterIndex, sameTypeNames);
            EditorGUILayout.EndHorizontal();
            string targetParameterName = sameTypeParams[_adjustSecondParameterIndex].name;
            _adjustSwapApplyAll = EditorGUILayout.ToggleLeft("覆盖到全部控制器", _adjustSwapApplyAll);

            if (GUILayout.Button("执行参数替换", GUILayout.Height(28f)))
            {
                ApplyAdjustAction("参数替换", controller => AnimatorParameterAdjustService.SwapParameters(controller, selectedParameter.name, targetParameterName), _adjustSwapApplyAll);
            }
        }

        private void DrawAdjustRemoveSection(AnimatorControllerParameter selectedParameter)
        {
            _adjustRemoveApplyAll = EditorGUILayout.ToggleLeft("覆盖到全部控制器", _adjustRemoveApplyAll);
            if (GUILayout.Button("应用移除", GUILayout.Height(28f)))
            {
                bool confirmed = EditorUtility.DisplayDialog(
                    "移除参数",
                    $"将从动画控制器中移除参数：{selectedParameter.name}\n\n同时会清理所有相关引用（Transition Conditions / BlendTree / Behaviour 等）。\n\n此操作可通过 Undo 撤销。",
                    "确定移除",
                    "取消");
                if (!confirmed)
                {
                    return;
                }

                ApplyAdjustAction("移除", controller => AnimatorParameterAdjustService.RemoveParameter(controller, selectedParameter.name), _adjustRemoveApplyAll);
            }
        }

        private void ApplyAdjustAction(string actionName, Func<AnimatorController, bool> action, bool applyAll)
        {
            int successCount = 0;
            if (applyAll)
            {
                for (int i = 0; i < _host.Controllers.Count; i++)
                {
                    AnimatorController ctrl = _host.Controllers[i];
                    if (ctrl != null && action(ctrl))
                    {
                        successCount++;
                    }
                }
            }
            else
            {
                AnimatorController controller = _host.SelectedController;
                if (controller != null && action(controller))
                {
                    successCount = 1;
                }
            }

            string message = successCount > 0
                ? $"已完成 {actionName} 操作，作用于 {successCount} 个控制器。"
                : "没有控制器发生变化，请检查条件是否满足。";
            EditorUtility.DisplayDialog("参数调整", message, "确定");
        }
    }
}
