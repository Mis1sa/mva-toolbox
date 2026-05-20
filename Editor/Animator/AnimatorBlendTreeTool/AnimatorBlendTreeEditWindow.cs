using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.AnimatorBlendTreeTool
{
    internal sealed partial class AnimatorBlendTreeEditWindow
    {
        private sealed class BlendTreeNodeInfo
        {
            public string DisplayPath;
            public UnityEditor.Animations.BlendTree Tree;
            public AnimatorState RootState;
            public UnityEditor.Animations.BlendTree ParentTree;
            public BlendTreeNodeInfo ParentNode;
            public int ParentChildIndex = -1;
        }

        private enum BlendTreeOperation
        {
            CreateParent,
            MoveBlendTree,
            ExportBlendTree
        }

        private const HideFlags ControllerTreeHideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;

        private static readonly string[] OperationLabels = { "新建父级", "移动 BlendTree", "导出 BlendTree" };
        private static readonly BlendTreeType[] ParentTypeValues =
        {
            BlendTreeType.Simple1D,
            BlendTreeType.SimpleDirectional2D,
            BlendTreeType.FreeformDirectional2D,
            BlendTreeType.FreeformCartesian2D,
            BlendTreeType.Direct
        };
        private static readonly string[] ParentTypeLabels =
        {
            "1D",
            "2D Simple Directional",
            "2D Freedom Directional",
            "2D Freedom Cartesian",
            "直接"
        };

        private readonly AnimatorBlendTreeToolWindow _owner;
        private AnimatorController _lastController;
        private int _lastLayerIndex = -1;
        private readonly List<AnimatorState> _editStates = new List<AnimatorState>();
        private string[] _editStateDisplayNames = new string[0];
        private int _selectedEditStateIndex;
        private readonly List<BlendTreeNodeInfo> _editBlendTrees = new List<BlendTreeNodeInfo>();
        private string[] _editBlendTreeDisplayNames = new string[0];
        private int _selectedEditTreeIndex;
        private BlendTreeOperation _selectedOperation = BlendTreeOperation.CreateParent;
        private int _parentTypeIndex;
        private int _moveTargetIndex;
        private bool _moveAsCopy;
        private string _exportFolder = "Assets/MVA Toolbox/Animator BlendTree/";
        private readonly List<BlendTreeNodeInfo> _moveTargetNodes = new List<BlendTreeNodeInfo>();
        private string[] _moveTargetDisplayNames = new string[0];

        internal AnimatorBlendTreeEditWindow(AnimatorBlendTreeToolWindow owner)
        {
            _owner = owner;
        }

        internal void Reset()
        {
            _lastController = null;
            _lastLayerIndex = -1;
            _editStates.Clear();
            _editStateDisplayNames = new string[0];
            _selectedEditStateIndex = 0;
            _editBlendTrees.Clear();
            _editBlendTreeDisplayNames = new string[0];
            _selectedEditTreeIndex = 0;
            _moveTargetNodes.Clear();
            _moveTargetDisplayNames = new string[0];
            _moveTargetIndex = 0;
        }

        internal void OnGUI()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("混合树工具", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);
            DrawEditUI();
            EditorGUILayout.EndVertical();
        }

        private void DrawEditUI()
        {
            AnimatorController controller = _owner.SelectedController;
            if (controller == null)
            {
                EditorGUILayout.HelpBox("请先选择动画控制器。", MessageType.Warning);
                return;
            }

            AnimatorControllerLayer layer = _owner.SelectedLayer;
            int layerIndex = _owner.SelectedLayerIndex;
            if (layer == null || layer.stateMachine == null)
            {
                EditorGUILayout.HelpBox("请在目标区域中选择有效的层级。", MessageType.Warning);
                return;
            }

            if (controller != _lastController || layerIndex != _lastLayerIndex)
            {
                RefreshEditStateList();
                _lastController = controller;
                _lastLayerIndex = layerIndex;
            }

            if (_editStates.Count == 0)
            {
                EditorGUILayout.HelpBox("当前层级中没有包含 BlendTree 的状态。", MessageType.Info);
                if (GUILayout.Button("刷新列表"))
                {
                    RefreshEditStateList();
                }
                return;
            }

            DrawLabeledPopup("选择状态", _selectedEditStateIndex, _editStateDisplayNames, delegate(int index)
            {
                _selectedEditStateIndex = index;
                RefreshEditBlendTreeListForState(_selectedEditStateIndex);
                GUI.FocusControl(null);
            });

            if (_editBlendTrees.Count == 0)
            {
                EditorGUILayout.HelpBox("所选状态中没有可编辑的 BlendTree。", MessageType.Info);
                return;
            }

            DrawLabeledPopup("选择 BlendTree", _selectedEditTreeIndex, _editBlendTreeDisplayNames, delegate(int index)
            {
                _selectedEditTreeIndex = index;
                GUI.FocusControl(null);
                RefreshMoveTargetList(GetSelectedNode());
            });

            BlendTreeNodeInfo selectedNode = GetSelectedNode();
            if (selectedNode == null || selectedNode.Tree == null)
            {
                return;
            }

            EditorGUILayout.Space(4f);
            DrawOperationUI(selectedNode);
        }

        private void DrawLabeledPopup(string label, int currentIndex, string[] options, Action<int> onChange, bool enabled = true)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(120f));
            EditorGUI.BeginDisabledGroup(!enabled);
            int newIndex = options != null && options.Length > 0
                ? EditorGUILayout.Popup(currentIndex, options)
                : currentIndex;
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (enabled && newIndex != currentIndex)
            {
                onChange(newIndex);
            }
        }
    }
}
