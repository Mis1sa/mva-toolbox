using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using MVA.Toolbox.QuickAnimatorEdit.Services.Shared;
using MVA.Toolbox.QuickAnimatorEdit.Services.BlendTree;

namespace MVA.Toolbox.QuickAnimatorEdit.Windows
{
    /// <summary>
    /// 混合树功能面板
    /// </summary>
    public sealed class QuickAnimatorEditBlendTreeWindow
    {
        private enum BlendTreeOperation
        {
            CreateParent,
            MoveBlendTree,
            ExportBlendTree
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
                onChange?.Invoke(newIndex);
            }
        }

        private sealed class BlendTreeNodeInfo
        {
            public string DisplayPath;
            public UnityEditor.Animations.BlendTree Tree;
            public AnimatorState RootState;
            public UnityEditor.Animations.BlendTree ParentTree;
            public BlendTreeNodeInfo ParentNode;
            public int ParentChildIndex = -1;
        }

        private const HideFlags ControllerTreeHideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;

        private QuickAnimatorEditContext _context;

        // Edit Mode
        private AnimatorController _lastEditController;
        private int _lastEditLayerIndex = -1;
        private readonly List<(string path, AnimatorState state)> _editStates = new List<(string, AnimatorState)>();
        private string[] _editStateDisplayNames = System.Array.Empty<string>();
        private int _selectedEditStateIndex;
        private readonly List<BlendTreeNodeInfo> _editBlendTrees = new List<BlendTreeNodeInfo>();
        private string[] _editBlendTreeDisplayNames = System.Array.Empty<string>();
        private int _selectedEditTreeIndex;

        private BlendTreeOperation _selectedOperation = BlendTreeOperation.CreateParent;
        private int _parentTypeIndex;
        private int _moveTargetIndex;
        private bool _moveAsCopy;
        private string _exportFolder = "Assets/MVA Toolbox/AQE/";
        private readonly List<BlendTreeNodeInfo> _moveTargetNodes = new List<BlendTreeNodeInfo>();
        private string[] _moveTargetDisplayNames = System.Array.Empty<string>();

        private static readonly string[] _operationLabels = { "新建父级", "移动 BlendTree", "导出 BlendTree" };
        private static readonly BlendTreeType[] _parentTypeValues =
        {
            BlendTreeType.Simple1D,
            BlendTreeType.SimpleDirectional2D,
            BlendTreeType.FreeformDirectional2D,
            BlendTreeType.FreeformCartesian2D,
            BlendTreeType.Direct
        };

        private static readonly string[] _parentTypeLabels =
        {
            "1D",
            "2D Simple Directional",
            "2D Freedom Directional",
            "2D Freedom Cartesian",
            "直接"
        };
        
        public QuickAnimatorEditBlendTreeWindow(QuickAnimatorEditContext context)
        {
            _context = context;
        }

        /// <summary>
        /// 绘制混合树功能面板
        /// </summary>
        public void OnGUI()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("混合树工具", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);
            DrawEditUI();
            EditorGUILayout.EndVertical();
        }

        private void DrawEditUI()
        {
            var controller = _context.SelectedController;
            if (controller == null)
            {
                EditorGUILayout.HelpBox("请先选择动画控制器。", MessageType.Warning);
                return;
            }

            var layer = _context.SelectedLayer;
            int layerIndex = _context.SelectedLayerIndex;
            if (layer == null || layer.stateMachine == null)
            {
                EditorGUILayout.HelpBox("请在目标区域中选择有效的层级。", MessageType.Warning);
                return;
            }

            // Refresh list if controller or layer changed
            if (controller != _lastEditController || layerIndex != _lastEditLayerIndex)
            {
                RefreshEditStateList();
                _lastEditController = controller;
                _lastEditLayerIndex = layerIndex;
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

            // Select State
            DrawLabeledPopup("选择状态", _selectedEditStateIndex, _editStateDisplayNames, index =>
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

            // Select BlendTree
            DrawLabeledPopup("选择 BlendTree", _selectedEditTreeIndex, _editBlendTreeDisplayNames, index =>
            {
                _selectedEditTreeIndex = index;
                GUI.FocusControl(null);
                RefreshMoveTargetList(GetSelectedNode());
            });

            var selectedNode = _editBlendTrees[_selectedEditTreeIndex];
            if (selectedNode?.Tree == null) return;

            EditorGUILayout.Space(4f);
            DrawOperationUI(selectedNode);
        }

        private void DrawOperationUI(BlendTreeNodeInfo selectedNode)
        {
            DrawLabeledPopup("选择操作", (int)_selectedOperation, _operationLabels, newIndex =>
            {
                _selectedOperation = (BlendTreeOperation)newIndex;
                if (_selectedOperation == BlendTreeOperation.MoveBlendTree)
                {
                    RefreshMoveTargetList(selectedNode);
                }
            });

            EditorGUILayout.Space(6f);

            switch (_selectedOperation)
            {
                case BlendTreeOperation.CreateParent:
                    DrawCreateParentSection(selectedNode);
                    break;
                case BlendTreeOperation.MoveBlendTree:
                    DrawMoveBlendTreeSection(selectedNode);
                    break;
                case BlendTreeOperation.ExportBlendTree:
                    DrawExportBlendTreeSection(selectedNode);
                    break;
            }
        }

        private void DrawCreateParentSection(BlendTreeNodeInfo selectedNode)
        {
            DrawLabeledPopup("父 BlendTree 类型", _parentTypeIndex, _parentTypeLabels, index =>
            {
                _parentTypeIndex = index;
            });

            EditorGUI.BeginDisabledGroup(selectedNode == null);
            if (GUILayout.Button("应用", GUILayout.Height(26f)))
            {
                if (CreateParentBlendTree(selectedNode, _parentTypeValues[_parentTypeIndex]))
                {
                    RefreshEditBlendTreeListForState(_selectedEditStateIndex);
                    RefreshMoveTargetList(GetSelectedNode());
                }
            }
            EditorGUI.EndDisabledGroup();
        }

        private void DrawMoveBlendTreeSection(BlendTreeNodeInfo selectedNode)
        {
            if (selectedNode == null)
            {
                EditorGUILayout.HelpBox("请选择要操作的 BlendTree。", MessageType.Info);
                return;
            }

            if (selectedNode.ParentTree == null)
            {
                EditorGUILayout.HelpBox("根 BlendTree 无法直接移动，请先创建父级。", MessageType.Info);
                return;
            }

            if (_moveTargetNodes.Count == 0)
            {
                EditorGUILayout.HelpBox("当前没有可用的目标 BlendTree。", MessageType.Info);
                return;
            }

            DrawLabeledPopup("选择目标 BlendTree", _moveTargetIndex, _moveTargetDisplayNames, index =>
            {
                _moveTargetIndex = index;
            }, _moveTargetNodes.Count > 0);

            _moveAsCopy = EditorGUILayout.ToggleLeft("复制（不移除原节点）", _moveAsCopy);

            EditorGUI.BeginDisabledGroup(_moveTargetNodes.Count == 0);
            if (GUILayout.Button("应用", GUILayout.Height(26f)))
            {
                var targetNode = _moveTargetNodes.ElementAtOrDefault(_moveTargetIndex);
                if (targetNode == null)
                {
                    EditorUtility.DisplayDialog("提示", "请选择有效的目标 BlendTree。", "确定");
                }
                else if (MoveOrCopyBlendTree(selectedNode, targetNode, _moveAsCopy))
                {
                    RefreshEditBlendTreeListForState(_selectedEditStateIndex);
                    RefreshMoveTargetList(GetSelectedNode());
                }
            }
            EditorGUI.EndDisabledGroup();
        }

        private void DrawExportBlendTreeSection(BlendTreeNodeInfo selectedNode)
        {
            if (selectedNode == null)
            {
                EditorGUILayout.HelpBox("请选择要导出的 BlendTree。", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("导出路径", GUILayout.Width(120f));
            _exportFolder = EditorGUILayout.TextField(_exportFolder);
            if (GUILayout.Button("浏览", GUILayout.Width(60f)))
            {
                BrowseExportFolder();
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("导出 BlendTree", GUILayout.Height(26f)))
            {
                ExportBlendTree(selectedNode);
            }
        }

        private void RefreshEditStateList()
        {
            _editStates.Clear();
            _editStateDisplayNames = System.Array.Empty<string>();
            _selectedEditStateIndex = 0;

            var controller = _context.SelectedController;
            var layer = _context.SelectedLayer;
            if (controller == null || layer?.stateMachine == null)
            {
                _editBlendTrees.Clear();
                _editBlendTreeDisplayNames = System.Array.Empty<string>();
                return;
            }

            CollectBlendTreeStates(layer.stateMachine, layer.name, _editStates);
            _editStateDisplayNames = _editStates
                .Select(x => x.state != null ? x.state.name : string.Empty)
                .ToArray();

            RefreshEditBlendTreeListForState(_editStates.Count > 0 ? 0 : -1);
        }

        private void RefreshEditBlendTreeListForState(int stateIndex)
        {
            _editBlendTrees.Clear();
            _editBlendTreeDisplayNames = System.Array.Empty<string>();
            _selectedEditTreeIndex = 0;
            _moveTargetNodes.Clear();
            _moveTargetDisplayNames = System.Array.Empty<string>();

            if (stateIndex < 0 || stateIndex >= _editStates.Count)
            {
                return;
            }

            var (_, state) = _editStates[stateIndex];
            if (state?.motion is UnityEditor.Animations.BlendTree rootTree)
            {
                CollectBlendTreesFromState(
                    rootTree,
                    GetTreeDisplayName(rootTree),
                    state,
                    null,
                    null,
                    -1,
                    _editBlendTrees);
                _editBlendTreeDisplayNames = _editBlendTrees.Select(x => x.DisplayPath).ToArray();
            }

            RefreshMoveTargetList(GetSelectedNode());
        }

        private void CollectBlendTreeStates(AnimatorStateMachine stateMachine, string parentPath, List<(string path, AnimatorState state)> result)
        {
            if (stateMachine == null) return;

            foreach (var child in stateMachine.states)
            {
                var state = child.state;
                if (state?.motion is UnityEditor.Animations.BlendTree)
                {
                    string statePath = AnimatorPathUtility.Combine(parentPath, state.name);
                    result.Add((statePath, state));
                }
            }

            foreach (var sub in stateMachine.stateMachines)
            {
                string subPath = AnimatorPathUtility.Combine(parentPath, sub.stateMachine.name);
                CollectBlendTreeStates(sub.stateMachine, subPath, result);
            }
        }

        private string GetTreeDisplayName(UnityEditor.Animations.BlendTree tree)
        {
            return string.IsNullOrEmpty(tree?.name) ? "未命名 BlendTree" : tree.name;
        }

        private void CollectBlendTreesFromState(
            UnityEditor.Animations.BlendTree tree,
            string displayPath,
            AnimatorState rootState,
            UnityEditor.Animations.BlendTree parentTree,
            BlendTreeNodeInfo parentNode,
            int parentChildIndex,
            List<BlendTreeNodeInfo> result)
        {
            if (tree == null) return;

            var node = new BlendTreeNodeInfo
            {
                DisplayPath = displayPath,
                Tree = tree,
                RootState = rootState,
                ParentTree = parentTree,
                ParentNode = parentNode,
                ParentChildIndex = parentChildIndex
            };

            result.Add(node);

            var children = tree.children;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].motion is UnityEditor.Animations.BlendTree childTree)
                {
                    string childLabel = $"{displayPath}/{GetTreeDisplayName(childTree)}";
                    CollectBlendTreesFromState(childTree, childLabel, rootState, tree, node, i, result);
                }
            }
        }

        private BlendTreeNodeInfo GetSelectedNode()
        {
            if (_selectedEditTreeIndex < 0 || _selectedEditTreeIndex >= _editBlendTrees.Count)
            {
                return null;
            }
            return _editBlendTrees[_selectedEditTreeIndex];
        }

        private void RefreshMoveTargetList(BlendTreeNodeInfo selectedNode)
        {
            _moveTargetNodes.Clear();
            _moveTargetDisplayNames = Array.Empty<string>();
            _moveTargetIndex = 0;

            if (selectedNode == null)
            {
                return;
            }

            foreach (var node in _editBlendTrees)
            {
                if (node == selectedNode) continue;
                if (node.RootState != selectedNode.RootState) continue;
                if (IsDescendant(node, selectedNode)) continue;
                _moveTargetNodes.Add(node);
            }

            _moveTargetDisplayNames = _moveTargetNodes.Select(n => n.DisplayPath).ToArray();
            if (_moveTargetNodes.Count == 0)
            {
                _moveTargetIndex = 0;
            }
            else
            {
                _moveTargetIndex = Mathf.Clamp(_moveTargetIndex, 0, Mathf.Max(0, _moveTargetNodes.Count - 1));
            }
        }

        private bool IsDescendant(BlendTreeNodeInfo candidate, BlendTreeNodeInfo potentialAncestor)
        {
            var current = candidate.ParentNode;
            while (current != null)
            {
                if (current == potentialAncestor) return true;
                current = current.ParentNode;
            }
            return false;
        }

        private bool CreateParentBlendTree(BlendTreeNodeInfo selectedNode, BlendTreeType parentType)
        {
            if (selectedNode?.Tree == null) return false;
            var controller = _context.SelectedController;
            if (controller == null) return false;

            var newTree = BlendTreeEditService.CreateBlendTree(controller, $"{selectedNode.Tree.name}_Parent");
            if (newTree == null) return false;

            Undo.RegisterCreatedObjectUndo(newTree, "创建父级 BlendTree");

            newTree.blendType = parentType;
            newTree.hideFlags = ControllerTreeHideFlags;
            ApplyHideFlagsRecursive(newTree, ControllerTreeHideFlags);

            switch (parentType)
            {
                case BlendTreeType.Simple1D:
                    newTree.blendParameter = string.IsNullOrEmpty(selectedNode.Tree.blendParameter)
                        ? "Blend"
                        : selectedNode.Tree.blendParameter;
                    break;
                case BlendTreeType.Direct:
                    newTree.blendParameter = string.Empty;
                    newTree.blendParameterY = string.Empty;
                    break;
                default:
                    newTree.blendParameter = string.IsNullOrEmpty(selectedNode.Tree.blendParameter)
                        ? "BlendX"
                        : selectedNode.Tree.blendParameter;
                    newTree.blendParameterY = string.IsNullOrEmpty(selectedNode.Tree.blendParameterY)
                        ? "BlendY"
                        : selectedNode.Tree.blendParameterY;
                    break;
            }

            newTree.minThreshold = 0f;
            newTree.maxThreshold = 1f;
            newTree.useAutomaticThresholds = true;

            newTree.children = new[]
            {
                new ChildMotion
                {
                    motion = selectedNode.Tree,
                    threshold = 0f,
                    position = Vector2.zero,
                    timeScale = 1f,
                    directBlendParameter = selectedNode.Tree.blendParameter
                }
            };

            if (selectedNode.ParentTree == null)
            {
                if (selectedNode.RootState == null) return false;
                Undo.RegisterCompleteObjectUndo(selectedNode.RootState, "创建父级 BlendTree");
                selectedNode.RootState.motion = newTree;
                EditorUtility.SetDirty(selectedNode.RootState);
            }
            else
            {
                var parentTree = selectedNode.ParentTree;
                var childIndex = selectedNode.ParentChildIndex;
                if (childIndex < 0) return false;

                Undo.RegisterCompleteObjectUndo(parentTree, "创建父级 BlendTree");
                var children = parentTree.children;
                if (childIndex >= children.Length) return false;
                var childMotion = children[childIndex];
                childMotion.motion = newTree;
                children[childIndex] = childMotion;
                parentTree.children = children;
                EditorUtility.SetDirty(parentTree);
            }

            ApplyHideFlagsRecursive(selectedNode.Tree, ControllerTreeHideFlags);
            EditorUtility.SetDirty(newTree);
            EditorUtility.SetDirty(controller);
            return true;
        }

        private bool MoveOrCopyBlendTree(BlendTreeNodeInfo sourceNode, BlendTreeNodeInfo targetNode, bool copy)
        {
            if (sourceNode?.Tree == null || targetNode?.Tree == null) return false;
            if (sourceNode.Tree == targetNode.Tree) return false;
            if (sourceNode.RootState != targetNode.RootState) return false;

            var controller = _context.SelectedController;
            if (controller == null) return false;

            UnityEditor.Animations.BlendTree treeToInsert;

            if (copy)
            {
                treeToInsert = BlendTreeTransferService.CloneBlendTree(sourceNode.Tree, controller);
                if (treeToInsert == null) return false;
                Undo.RegisterCreatedObjectUndo(treeToInsert, "复制 BlendTree");
                ApplyHideFlagsRecursive(treeToInsert, ControllerTreeHideFlags);
            }
            else
            {
                if (sourceNode.ParentTree == null || sourceNode.ParentChildIndex < 0)
                {
                    EditorUtility.DisplayDialog("无法移动", "根 BlendTree 不能直接移动，请先创建父级。", "确定");
                    return false;
                }

                treeToInsert = sourceNode.Tree;
                var parentTree = sourceNode.ParentTree;

                Undo.RegisterCompleteObjectUndo(parentTree, "移动 BlendTree");
                var children = parentTree.children.ToList();
                if (sourceNode.ParentChildIndex >= children.Count)
                {
                    return false;
                }

                children.RemoveAt(sourceNode.ParentChildIndex);
                parentTree.children = children.ToArray();
                EditorUtility.SetDirty(parentTree);
            }

            Undo.RegisterCompleteObjectUndo(targetNode.Tree, copy ? "复制 BlendTree" : "移动 BlendTree");
            var targetChildren = targetNode.Tree.children.ToList();
            targetChildren.Add(new ChildMotion
            {
                motion = treeToInsert,
                threshold = 0f,
                position = Vector2.zero,
                timeScale = 1f,
                directBlendParameter = treeToInsert.blendParameter
            });
            targetNode.Tree.children = targetChildren.ToArray();

            ApplyHideFlagsRecursive(treeToInsert, ControllerTreeHideFlags);

            EditorUtility.SetDirty(targetNode.Tree);
            EditorUtility.SetDirty(controller);
            return true;
        }

        private void BrowseExportFolder()
        {
            string startPath = Application.dataPath;
            string abs = EditorUtility.OpenFolderPanel("选择导出路径", startPath, string.Empty);
            if (string.IsNullOrEmpty(abs)) return;

            if (!abs.StartsWith(Application.dataPath, StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("路径错误", "请选择工程 Assets 目录下的文件夹。", "确定");
                return;
            }

            string rel = "Assets" + abs.Substring(Application.dataPath.Length);
            rel = rel.Replace("\\", "/");
            if (!rel.EndsWith("/")) rel += "/";
            _exportFolder = rel;
        }

        private void ExportBlendTree(BlendTreeNodeInfo selectedNode)
        {
            if (selectedNode?.Tree == null) return;

            string folder = string.IsNullOrEmpty(_exportFolder) ? "Assets/MVA Toolbox/AQE/" : _exportFolder;
            folder = folder.Replace("\\", "/");
            if (!folder.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("路径错误", "导出路径必须位于 Assets 目录下。", "确定");
                return;
            }

            string sanitizedFolder = folder.TrimEnd('/');
            EnsureAssetFolder(sanitizedFolder);

            var clone = BlendTreeTransferService.CloneBlendTree(selectedNode.Tree, null);
            if (clone == null)
            {
                EditorUtility.DisplayDialog("导出失败", "克隆 BlendTree 失败。", "确定");
                return;
            }

            ApplyHideFlagsRecursive(clone, HideFlags.None);

            string fileName = MakeSafeFilename(selectedNode.Tree.name);
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = "BlendTree";
            }

            string assetPath = $"{sanitizedFolder}/{fileName}.asset";
            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

            AssetDatabase.CreateAsset(clone, assetPath);

            var subTrees = new List<UnityEditor.Animations.BlendTree>();
            CollectSubTrees(clone, subTrees);
            foreach (var child in subTrees)
            {
                if (child == clone) continue;
                AssetDatabase.AddObjectToAsset(child, clone);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("导出成功", $"BlendTree 已导出到:\n{assetPath}", "确定");
        }

        private void CollectSubTrees(UnityEditor.Animations.BlendTree tree, List<UnityEditor.Animations.BlendTree> list)
        {
            if (tree == null) return;
            if (!list.Contains(tree))
            {
                list.Add(tree);
            }

            foreach (var child in tree.children)
            {
                if (child.motion is UnityEditor.Animations.BlendTree childTree)
                {
                    CollectSubTrees(childTree, list);
                }
            }
        }

        private void EnsureAssetFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            string parent = Path.GetDirectoryName(folder)?.Replace("\\", "/");
            string name = Path.GetFileName(folder);

            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureAssetFolder(parent);
            }

            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(name))
            {
                AssetDatabase.CreateFolder(parent, name);
            }
        }

        private string MakeSafeFilename(string name)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var chars = name.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray();
            return new string(chars);
        }

        private void ApplyHideFlagsRecursive(UnityEditor.Animations.BlendTree tree, HideFlags flags)
        {
            if (tree == null) return;
            tree.hideFlags = flags;
            foreach (var child in tree.children)
            {
                if (child.motion is UnityEditor.Animations.BlendTree childTree)
                {
                    ApplyHideFlagsRecursive(childTree, flags);
                }
            }
        }

    }
}
