using System.Collections.Generic;
using System.Linq;
using MVA.Toolbox.AnimatorShared.Paths;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.AnimatorBlendTreeTool
{
    internal sealed partial class AnimatorBlendTreeEditWindow
    {
        private void RefreshEditStateList()
        {
            _editStates.Clear();
            _editStateDisplayNames = new string[0];
            _selectedEditStateIndex = 0;

            AnimatorControllerLayer layer = _owner.SelectedLayer;
            if (layer == null || layer.stateMachine == null)
            {
                _editBlendTrees.Clear();
                _editBlendTreeDisplayNames = new string[0];
                return;
            }

            var displayNames = new List<string>();
            CollectBlendTreeStates(layer.stateMachine, string.Empty, _editStates, displayNames);
            _editStateDisplayNames = displayNames.ToArray();

            if (_editStates.Count > 0)
            {
                RefreshEditBlendTreeListForState(0);
            }
            else
            {
                RefreshEditBlendTreeListForState(-1);
            }
        }

        private void RefreshEditBlendTreeListForState(int stateIndex)
        {
            _editBlendTrees.Clear();
            _editBlendTreeDisplayNames = new string[0];
            _selectedEditTreeIndex = 0;
            _moveTargetNodes.Clear();
            _moveTargetDisplayNames = new string[0];
            _moveTargetIndex = 0;

            if (stateIndex < 0 || stateIndex >= _editStates.Count)
            {
                return;
            }

            _selectedEditStateIndex = stateIndex;
            AnimatorState state = _editStates[stateIndex];
            var rootTree = state != null ? state.motion as UnityEditor.Animations.BlendTree : null;
            if (rootTree != null)
            {
                CollectBlendTreesFromState(rootTree, GetTreeDisplayName(rootTree), state, null, null, -1, _editBlendTrees);
                _editBlendTreeDisplayNames = _editBlendTrees.Select(x => x.DisplayPath).ToArray();
                _selectedEditTreeIndex = _editBlendTrees.Count > 0 ? 0 : -1;
            }

            RefreshMoveTargetList(GetSelectedNode());
        }

        private void CollectBlendTreeStates(AnimatorStateMachine stateMachine, string parentPath, List<AnimatorState> result, List<string> displayNames)
        {
            if (stateMachine == null)
            {
                return;
            }

            ChildAnimatorState[] childStates = stateMachine.states;
            for (int i = 0; i < childStates.Length; i++)
            {
                AnimatorState state = childStates[i].state;
                if (state == null || !(state.motion is UnityEditor.Animations.BlendTree))
                {
                    continue;
                }

                string stateName = state.name;
                string fullPath = string.IsNullOrEmpty(parentPath) ? stateName : parentPath + AnimatorStatePathUtility.DisplayPathSeparator + stateName;
                result.Add(state);
                displayNames.Add(fullPath);
            }

            ChildAnimatorStateMachine[] childStateMachines = stateMachine.stateMachines;
            for (int i = 0; i < childStateMachines.Length; i++)
            {
                AnimatorStateMachine childStateMachine = childStateMachines[i].stateMachine;
                if (childStateMachine == null)
                {
                    continue;
                }

                string childPath = string.IsNullOrEmpty(parentPath)
                    ? childStateMachine.name + AnimatorStatePathUtility.SubStateMachineSuffix
                    : parentPath + AnimatorStatePathUtility.DisplayPathSeparator + childStateMachine.name + AnimatorStatePathUtility.SubStateMachineSuffix;
                CollectBlendTreeStates(childStateMachine, childPath, result, displayNames);
            }
        }

        private static string GetTreeDisplayName(UnityEditor.Animations.BlendTree tree)
        {
            return string.IsNullOrEmpty(tree != null ? tree.name : null) ? "未命名 BlendTree" : tree.name;
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
            if (tree == null)
            {
                return;
            }

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

            ChildMotion[] children = tree.children;
            for (int i = 0; i < children.Length; i++)
            {
                var childTree = children[i].motion as UnityEditor.Animations.BlendTree;
                if (childTree != null)
                {
                    string childLabel = displayPath + "/" + GetTreeDisplayName(childTree);
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
            _moveTargetDisplayNames = new string[0];
            _moveTargetIndex = 0;

            if (selectedNode == null)
            {
                return;
            }

            for (int i = 0; i < _editBlendTrees.Count; i++)
            {
                BlendTreeNodeInfo node = _editBlendTrees[i];
                if (node == selectedNode)
                {
                    continue;
                }

                if (node.RootState != selectedNode.RootState)
                {
                    continue;
                }

                if (IsDescendant(node, selectedNode))
                {
                    continue;
                }

                _moveTargetNodes.Add(node);
            }

            _moveTargetDisplayNames = _moveTargetNodes.Select(n => n.DisplayPath).ToArray();
            if (_moveTargetNodes.Count > 0)
            {
                _moveTargetIndex = Mathf.Clamp(_moveTargetIndex, 0, _moveTargetNodes.Count - 1);
            }
        }

        private static bool IsDescendant(BlendTreeNodeInfo candidate, BlendTreeNodeInfo potentialAncestor)
        {
            BlendTreeNodeInfo current = candidate != null ? candidate.ParentNode : null;
            while (current != null)
            {
                if (current == potentialAncestor)
                {
                    return true;
                }

                current = current.ParentNode;
            }

            return false;
        }
    }
}
