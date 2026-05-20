using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.AnimatorBlendTreeTool
{
    internal sealed partial class AnimatorBlendTreeEditWindow
    {
        private void DrawOperationUI(BlendTreeNodeInfo selectedNode)
        {
            DrawLabeledPopup("选择操作", (int)_selectedOperation, OperationLabels, delegate(int newIndex)
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
            DrawLabeledPopup("父 BlendTree 类型", _parentTypeIndex, ParentTypeLabels, delegate(int index)
            {
                _parentTypeIndex = index;
            });

            EditorGUI.BeginDisabledGroup(selectedNode == null);
            if (GUILayout.Button("应用", GUILayout.Height(26f)))
            {
                if (CreateParentBlendTree(selectedNode, ParentTypeValues[_parentTypeIndex]))
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

            DrawLabeledPopup("选择目标 BlendTree", _moveTargetIndex, _moveTargetDisplayNames, delegate(int index)
            {
                _moveTargetIndex = index;
            }, _moveTargetNodes.Count > 0);

            _moveAsCopy = EditorGUILayout.ToggleLeft("复制（不移除原节点）", _moveAsCopy);

            EditorGUI.BeginDisabledGroup(_moveTargetNodes.Count == 0);
            if (GUILayout.Button("应用", GUILayout.Height(26f)))
            {
                BlendTreeNodeInfo targetNode = _moveTargetIndex >= 0 && _moveTargetIndex < _moveTargetNodes.Count
                    ? _moveTargetNodes[_moveTargetIndex]
                    : null;
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

        private bool CreateParentBlendTree(BlendTreeNodeInfo selectedNode, BlendTreeType parentType)
        {
            if (selectedNode == null || selectedNode.Tree == null)
            {
                return false;
            }

            AnimatorController controller = _owner.SelectedController;
            if (controller == null)
            {
                return false;
            }

            var newTree = AnimatorBlendTreeEditService.CreateBlendTree(controller, selectedNode.Tree.name + "_Parent");
            if (newTree == null)
            {
                return false;
            }

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
                if (selectedNode.RootState == null)
                {
                    return false;
                }

                Undo.RegisterCompleteObjectUndo(selectedNode.RootState, "创建父级 BlendTree");
                selectedNode.RootState.motion = newTree;
                EditorUtility.SetDirty(selectedNode.RootState);
            }
            else
            {
                UnityEditor.Animations.BlendTree parentTree = selectedNode.ParentTree;
                int childIndex = selectedNode.ParentChildIndex;
                if (childIndex < 0)
                {
                    return false;
                }

                Undo.RegisterCompleteObjectUndo(parentTree, "创建父级 BlendTree");
                ChildMotion[] children = parentTree.children;
                if (childIndex >= children.Length)
                {
                    return false;
                }

                ChildMotion childMotion = children[childIndex];
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
            if (sourceNode == null || sourceNode.Tree == null || targetNode == null || targetNode.Tree == null)
            {
                return false;
            }

            if (sourceNode.Tree == targetNode.Tree)
            {
                return false;
            }

            if (sourceNode.RootState != targetNode.RootState)
            {
                return false;
            }

            AnimatorController controller = _owner.SelectedController;
            if (controller == null)
            {
                return false;
            }

            UnityEditor.Animations.BlendTree treeToInsert;
            if (copy)
            {
                treeToInsert = AnimatorBlendTreeEditService.CloneBlendTree(sourceNode.Tree, controller);
                if (treeToInsert == null)
                {
                    return false;
                }

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
                UnityEditor.Animations.BlendTree parentTree = sourceNode.ParentTree;

                Undo.RegisterCompleteObjectUndo(parentTree, "移动 BlendTree");
                List<ChildMotion> children = parentTree.children.ToList();
                if (sourceNode.ParentChildIndex >= children.Count)
                {
                    return false;
                }

                children.RemoveAt(sourceNode.ParentChildIndex);
                parentTree.children = children.ToArray();
                EditorUtility.SetDirty(parentTree);
            }

            Undo.RegisterCompleteObjectUndo(targetNode.Tree, copy ? "复制 BlendTree" : "移动 BlendTree");
            List<ChildMotion> targetChildren = targetNode.Tree.children.ToList();
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
            string absolutePath = EditorUtility.OpenFolderPanel("选择导出路径", startPath, string.Empty);
            if (string.IsNullOrEmpty(absolutePath))
            {
                return;
            }

            if (!absolutePath.StartsWith(Application.dataPath, StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("路径错误", "请选择工程 Assets 目录下的文件夹。", "确定");
                return;
            }

            string relative = "Assets" + absolutePath.Substring(Application.dataPath.Length);
            relative = relative.Replace("\\", "/");
            if (!relative.EndsWith("/", StringComparison.Ordinal))
            {
                relative += "/";
            }

            _exportFolder = relative;
        }

        private void ExportBlendTree(BlendTreeNodeInfo selectedNode)
        {
            if (selectedNode == null || selectedNode.Tree == null)
            {
                return;
            }

            string folder = string.IsNullOrEmpty(_exportFolder) ? "Assets/MVA Toolbox/Animator BlendTree/" : _exportFolder;
            folder = folder.Replace("\\", "/");
            if (!folder.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("路径错误", "导出路径必须位于 Assets 目录下。", "确定");
                return;
            }

            string sanitizedFolder = folder.TrimEnd('/');
            EnsureAssetFolder(sanitizedFolder);

            var clone = AnimatorBlendTreeEditService.CloneBlendTree(selectedNode.Tree, null);
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

            string assetPath = sanitizedFolder + "/" + fileName + ".asset";
            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            AssetDatabase.CreateAsset(clone, assetPath);

            var subTrees = new List<UnityEditor.Animations.BlendTree>();
            CollectSubTrees(clone, subTrees);
            for (int i = 0; i < subTrees.Count; i++)
            {
                if (subTrees[i] == clone)
                {
                    continue;
                }

                AssetDatabase.AddObjectToAsset(subTrees[i], clone);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("导出成功", "BlendTree 已导出到:\n" + assetPath, "确定");
        }

        private void CollectSubTrees(UnityEditor.Animations.BlendTree tree, List<UnityEditor.Animations.BlendTree> list)
        {
            if (tree == null)
            {
                return;
            }

            if (!list.Contains(tree))
            {
                list.Add(tree);
            }

            ChildMotion[] children = tree.children;
            for (int i = 0; i < children.Length; i++)
            {
                var childTree = children[i].motion as UnityEditor.Animations.BlendTree;
                if (childTree != null)
                {
                    CollectSubTrees(childTree, list);
                }
            }
        }

        private void EnsureAssetFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folder);
            if (!string.IsNullOrEmpty(parent))
            {
                parent = parent.Replace("\\", "/");
            }

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

        private static string MakeSafeFilename(string name)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            char[] chars = (name ?? string.Empty).Select(c => invalidChars.Contains(c) ? '_' : c).ToArray();
            return new string(chars);
        }

        private static void ApplyHideFlagsRecursive(UnityEditor.Animations.BlendTree tree, HideFlags flags)
        {
            if (tree == null)
            {
                return;
            }

            tree.hideFlags = flags;
            ChildMotion[] children = tree.children;
            for (int i = 0; i < children.Length; i++)
            {
                var childTree = children[i].motion as UnityEditor.Animations.BlendTree;
                if (childTree != null)
                {
                    ApplyHideFlagsRecursive(childTree, flags);
                }
            }
        }
    }
}
