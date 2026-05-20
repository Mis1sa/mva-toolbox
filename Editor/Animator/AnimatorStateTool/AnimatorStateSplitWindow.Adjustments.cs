using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.AnimatorStateTool
{
    internal sealed partial class AnimatorStateSplitWindow
    {
        private void DrawAdjustments(AnimatorControllerLayer layer)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _manualAdjust = EditorGUILayout.ToggleLeft("手动调整 Transition", _manualAdjust);
            if (!_manualAdjust)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            if (_resolvedState == null)
            {
                EditorGUILayout.HelpBox("请先选择一个有效的状态。", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            List<AnimatorState> allStates = AnimatorStateSplitService.GetAllStatesInLayer(_owner.SelectedController, _owner.SelectedLayerIndex);
            Dictionary<AnimatorStateTransition, bool> prevIncoming = new Dictionary<AnimatorStateTransition, bool>();
            foreach (AnimatorStateSplitService.TransitionAdjustment adj in _incomingAdjustments)
            {
                if (adj.Transition != null && !prevIncoming.ContainsKey(adj.Transition))
                {
                    prevIncoming.Add(adj.Transition, adj.ShouldMove);
                }
            }

            Dictionary<AnimatorStateTransition, bool> prevAny = new Dictionary<AnimatorStateTransition, bool>();
            foreach (AnimatorStateSplitService.TransitionAdjustment adj in _anyAdjustments)
            {
                if (adj.Transition != null && !prevAny.ContainsKey(adj.Transition))
                {
                    prevAny.Add(adj.Transition, adj.ShouldMove);
                }
            }

            Dictionary<AnimatorStateTransition, bool> prevOutgoing = new Dictionary<AnimatorStateTransition, bool>();
            foreach (AnimatorStateSplitService.TransitionAdjustment adj in _outgoingAdjustments)
            {
                if (adj.Transition != null && !prevOutgoing.ContainsKey(adj.Transition))
                {
                    prevOutgoing.Add(adj.Transition, adj.ShouldMove);
                }
            }

            _incomingAdjustments.Clear();
            _anyAdjustments.Clear();
            _outgoingAdjustments.Clear();

            if (allStates != null && _resolvedState != null)
            {
                foreach (AnimatorState state in allStates)
                {
                    if (state == null || state.transitions == null)
                    {
                        continue;
                    }

                    foreach (AnimatorStateTransition transition in state.transitions)
                    {
                        if (transition != null && transition.destinationState == _resolvedState)
                        {
                            bool shouldMove = prevIncoming.TryGetValue(transition, out bool value) && value;
                            _incomingAdjustments.Add(new AnimatorStateSplitService.TransitionAdjustment
                            {
                                Transition = transition,
                                ShouldMove = shouldMove,
                                DisplayName = _owner.FormatStateNameWithPath(state) + " -> Head"
                            });
                        }
                    }
                }

                if (layer.stateMachine.anyStateTransitions != null)
                {
                    foreach (AnimatorStateTransition transition in layer.stateMachine.anyStateTransitions)
                    {
                        if (transition != null && transition.destinationState == _resolvedState)
                        {
                            bool shouldMove = prevAny.TryGetValue(transition, out bool value) && value;
                            _anyAdjustments.Add(new AnimatorStateSplitService.TransitionAdjustment
                            {
                                Transition = transition,
                                ShouldMove = shouldMove,
                                DisplayName = "Any State -> Head"
                            });
                        }
                    }
                }

                if (_resolvedState.transitions != null)
                {
                    foreach (AnimatorStateTransition transition in _resolvedState.transitions)
                    {
                        if (transition == null)
                        {
                            continue;
                        }

                        bool shouldMove = prevOutgoing.TryGetValue(transition, out bool value) && value;
                        string destName;
                        if (transition.destinationState != null)
                        {
                            destName = _owner.FormatStateNameWithPath(transition.destinationState);
                        }
                        else if (transition.destinationStateMachine != null)
                        {
                            destName = transition.destinationStateMachine.name;
                        }
                        else
                        {
                            destName = "Exit";
                        }

                        _outgoingAdjustments.Add(new AnimatorStateSplitService.TransitionAdjustment
                        {
                            Transition = transition,
                            ShouldMove = shouldMove,
                            DisplayName = "Tail -> " + destName
                        });
                    }
                }
            }

            GUILayout.Space(4f);
            float columnWidth = Mathf.Max(0f, (EditorGUIUtility.currentViewWidth - 40f) * 0.5f);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(4f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(columnWidth), GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField("头部 Transition", EditorStyles.boldLabel);
            _scrollIncoming = EditorGUILayout.BeginScrollView(_scrollIncoming, GUILayout.ExpandHeight(true));
            for (int i = 0; i < _incomingAdjustments.Count; i++)
            {
                AnimatorStateSplitService.TransitionAdjustment adj = _incomingAdjustments[i];
                if (adj.ShouldMove)
                {
                    continue;
                }

                EditorGUILayout.BeginHorizontal();
                bool moveToTail = EditorGUILayout.Toggle(false, GUILayout.Width(20f));
                EditorGUILayout.LabelField(adj.DisplayName);
                if (moveToTail)
                {
                    adj.ShouldMove = true;
                    _incomingAdjustments[i] = adj;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (_anyAdjustments.Count > 0)
            {
                EditorGUILayout.LabelField("Any State", EditorStyles.boldLabel);
                for (int i = 0; i < _anyAdjustments.Count; i++)
                {
                    AnimatorStateSplitService.TransitionAdjustment adj = _anyAdjustments[i];
                    if (adj.ShouldMove)
                    {
                        continue;
                    }

                    EditorGUILayout.BeginHorizontal();
                    bool moveToTail = EditorGUILayout.Toggle(false, GUILayout.Width(20f));
                    EditorGUILayout.LabelField(adj.DisplayName);
                    if (moveToTail)
                    {
                        adj.ShouldMove = true;
                        _anyAdjustments[i] = adj;
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            if (_outgoingAdjustments.Count > 0)
            {
                for (int i = 0; i < _outgoingAdjustments.Count; i++)
                {
                    AnimatorStateSplitService.TransitionAdjustment adj = _outgoingAdjustments[i];
                    if (!adj.ShouldMove)
                    {
                        continue;
                    }

                    EditorGUILayout.BeginHorizontal();
                    bool keepInHead = EditorGUILayout.Toggle(true, GUILayout.Width(20f));
                    EditorGUILayout.LabelField(adj.DisplayName);
                    if (!keepInHead)
                    {
                        adj.ShouldMove = false;
                        _outgoingAdjustments[i] = adj;
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            GUILayout.Space(4f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(columnWidth), GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField("尾部 Transition", EditorStyles.boldLabel);
            _scrollOutgoing = EditorGUILayout.BeginScrollView(_scrollOutgoing, GUILayout.ExpandHeight(true));
            for (int i = 0; i < _incomingAdjustments.Count; i++)
            {
                AnimatorStateSplitService.TransitionAdjustment adj = _incomingAdjustments[i];
                if (!adj.ShouldMove)
                {
                    continue;
                }

                EditorGUILayout.BeginHorizontal();
                bool keepInTail = EditorGUILayout.Toggle(true, GUILayout.Width(20f));
                EditorGUILayout.LabelField(adj.DisplayName);
                if (!keepInTail)
                {
                    adj.ShouldMove = false;
                    _incomingAdjustments[i] = adj;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (_anyAdjustments.Count > 0)
            {
                EditorGUILayout.LabelField("Any State", EditorStyles.boldLabel);
                for (int i = 0; i < _anyAdjustments.Count; i++)
                {
                    AnimatorStateSplitService.TransitionAdjustment adj = _anyAdjustments[i];
                    if (!adj.ShouldMove)
                    {
                        continue;
                    }

                    EditorGUILayout.BeginHorizontal();
                    bool keepInTail = EditorGUILayout.Toggle(true, GUILayout.Width(20f));
                    EditorGUILayout.LabelField(adj.DisplayName);
                    if (!keepInTail)
                    {
                        adj.ShouldMove = false;
                        _anyAdjustments[i] = adj;
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            if (_outgoingAdjustments.Count > 0)
            {
                for (int i = 0; i < _outgoingAdjustments.Count; i++)
                {
                    AnimatorStateSplitService.TransitionAdjustment adj = _outgoingAdjustments[i];
                    if (adj.ShouldMove)
                    {
                        continue;
                    }

                    EditorGUILayout.BeginHorizontal();
                    bool moveToHead = EditorGUILayout.Toggle(false, GUILayout.Width(20f));
                    EditorGUILayout.LabelField(adj.DisplayName);
                    if (moveToHead)
                    {
                        adj.ShouldMove = true;
                        _outgoingAdjustments[i] = adj;
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            GUILayout.Space(4f);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }
    }
}
