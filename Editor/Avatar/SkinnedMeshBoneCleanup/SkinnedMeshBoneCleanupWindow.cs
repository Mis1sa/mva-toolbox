using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.SkinnedMeshBoneCleanup
{
    internal sealed class SkinnedMeshBoneCleanupWindow : EditorWindow
    {
        private const string WindowTitle = "Skinned Mesh Bone Cleanup";

        private readonly List<Renderer> _removeCandidates = new List<Renderer>();
        private readonly Dictionary<Renderer, List<Transform>> _exclusiveBones = new Dictionary<Renderer, List<Transform>>();
        private readonly Dictionary<int, bool> _boneFoldoutStates = new Dictionary<int, bool>();

        private bool _removeChildNonBoneObjects = true;
        private bool _excludeForeignChildObjects = true;

        private HashSet<Transform> _protectedBones = new HashSet<Transform>();
        private HashSet<Transform> _allBones = new HashSet<Transform>();

        private Vector2 _mainScroll;

        public static void Open()
        {
            var window = GetWindow<SkinnedMeshBoneCleanupWindow>(WindowTitle);
            window.minSize = new Vector2(500f, 600f);
        }

        private void OnGUI()
        {
            HandleGlobalDragAndDrop();

            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            _mainScroll = ScrollView(_mainScroll, () =>
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("添加需要移除的 Renderer", EditorStyles.boldLabel);
                DrawCandidateSection();
                EditorGUILayout.EndVertical();

                GUILayout.Space(6f);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawBoneSection();
                EditorGUILayout.EndVertical();
            }, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndVertical();

            GUILayout.Space(6f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawExecuteSection();
            EditorGUILayout.EndVertical();
        }

        private static Vector2 ScrollView(Vector2 scroll, Action drawContent, params GUILayoutOption[] options)
        {
            scroll = EditorGUILayout.BeginScrollView(scroll, options);
            drawContent?.Invoke();
            EditorGUILayout.EndScrollView();
            return scroll;
        }

        private void DrawCandidateSection()
        {
            float lineHeight = EditorGUIUtility.singleLineHeight;
            EditorGUILayout.BeginHorizontal();
            var renderer = EditorGUILayout.ObjectField("添加 Renderer", null, typeof(Renderer), true) as Renderer;
            if (renderer != null)
            {
                TryAddCandidate(renderer);
            }

            if (GUILayout.Button("清空列表", GUILayout.Width(90f), GUILayout.Height(lineHeight)))
            {
                ClearCandidates();
            }
            EditorGUILayout.EndHorizontal();

            DrawChildRemovalOptions();

            bool refreshed = false;
            for (int i = _removeCandidates.Count - 1; i >= 0; i--)
            {
                var candidate = _removeCandidates[i];
                if (candidate == null)
                {
                    _removeCandidates.RemoveAt(i);
                    refreshed = true;
                    continue;
                }

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUILayout.ObjectField(candidate, typeof(Renderer), true);
                float square = EditorGUIUtility.singleLineHeight;
                if (GUILayout.Button("-", GUILayout.Width(square), GUILayout.Height(square)))
                {
                    RemoveFoldoutState(candidate);
                    _exclusiveBones.Remove(candidate);
                    _removeCandidates.RemoveAt(i);
                    refreshed = true;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (refreshed)
            {
                RefreshBoneAnalysis();
            }
        }

        private void DrawBoneSection()
        {
            if (_exclusiveBones.Count == 0)
            {
                EditorGUILayout.HelpBox("尚未检测到需要移除的骨骼。", MessageType.Info);
                return;
            }

            foreach (var pair in _exclusiveBones)
            {
                var renderer = pair.Key;
                if (renderer == null)
                {
                    continue;
                }

                bool state = GetFoldoutState(renderer);
                state = EditorGUILayout.Foldout(state, renderer.name, true);
                _boneFoldoutStates[renderer.GetInstanceID()] = state;

                if (!state)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                var bones = pair.Value;
                if (bones == null || bones.Count == 0)
                {
                    EditorGUILayout.HelpBox("没有独占骨骼。", MessageType.Warning);
                }
                else
                {
                    for (int i = 0; i < bones.Count; i++)
                    {
                        EditorGUILayout.ObjectField(GUIContent.none, bones[i], typeof(Transform), true);
                    }
                }
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawExecuteSection()
        {
            EditorGUI.BeginDisabledGroup(_removeCandidates.Count == 0);
            if (GUILayout.Button("执行清理", GUILayout.Height(32f)))
            {
                ExecuteRemoval();
            }
            EditorGUI.EndDisabledGroup();
        }

        private void DrawChildRemovalOptions()
        {
            EditorGUILayout.BeginHorizontal();
            _removeChildNonBoneObjects = EditorGUILayout.ToggleLeft("移除子级中非骨骼物体", _removeChildNonBoneObjects);
            using (new EditorGUI.DisabledScope(!_removeChildNonBoneObjects))
            {
                _excludeForeignChildObjects = EditorGUILayout.ToggleLeft("排除其他骨骼下的非骨骼物体", _excludeForeignChildObjects);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void HandleGlobalDragAndDrop()
        {
            var current = Event.current;
            if (current == null)
            {
                return;
            }

            var fullRect = new Rect(0f, 0f, position.width, position.height);
            if (!fullRect.Contains(current.mousePosition))
            {
                return;
            }

            if (current.type == EventType.DragUpdated || current.type == EventType.DragPerform)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (current.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    TryAddCandidates(DragAndDrop.objectReferences);
                }
                current.Use();
            }
        }

        private void TryAddCandidates(IEnumerable<UnityEngine.Object> objects)
        {
            if (objects == null)
            {
                return;
            }

            bool added = false;
            foreach (var obj in objects)
            {
                if (obj == null)
                {
                    continue;
                }

                if (obj is Renderer renderer)
                {
                    added |= TryAddCandidate(renderer, false);
                    continue;
                }

                if (obj is GameObject go)
                {
                    var renderers = go.GetComponentsInChildren<Renderer>(true);
                    foreach (var childRenderer in renderers)
                    {
                        added |= TryAddCandidate(childRenderer, false);
                    }
                }
            }

            if (added)
            {
                RefreshBoneAnalysis();
            }
        }

        private bool TryAddCandidate(Renderer renderer, bool refreshImmediately = true)
        {
            if (renderer == null)
            {
                return false;
            }

            if (renderer is not SkinnedMeshRenderer && renderer is not MeshRenderer)
            {
                return false;
            }

            if (_removeCandidates.Contains(renderer))
            {
                return false;
            }

            _removeCandidates.Add(renderer);
            if (refreshImmediately)
            {
                RefreshBoneAnalysis();
            }

            return true;
        }

        private void ClearCandidates()
        {
            if (_removeCandidates.Count == 0)
            {
                return;
            }

            _removeCandidates.Clear();
            RefreshBoneAnalysis();
        }

        private void RefreshBoneAnalysis()
        {
            _exclusiveBones.Clear();
            if (_removeCandidates.Count == 0)
            {
                _protectedBones.Clear();
                _allBones.Clear();
                _boneFoldoutStates.Clear();
                return;
            }

            var analysis = BoneExclusivityAnalyzer.Analyze(_removeCandidates);
            _protectedBones = analysis.protectedBones;
            _allBones = analysis.allBones;

            foreach (var pair in analysis.exclusiveBones)
            {
                _exclusiveBones[pair.Key] = pair.Value;
            }

            PruneFoldoutStates();
        }

        private void ExecuteRemoval()
        {
            if (_removeCandidates.Count == 0)
            {
                return;
            }

            RefreshBoneAnalysis();

            if (!SkinnedMeshBoneCleanupExecutor.Execute(
                    _removeCandidates,
                    _exclusiveBones,
                    _protectedBones,
                    _allBones,
                    _removeChildNonBoneObjects,
                    _excludeForeignChildObjects))
            {
                return;
            }

            _removeCandidates.Clear();
            _exclusiveBones.Clear();
            _boneFoldoutStates.Clear();
            _protectedBones.Clear();
            _allBones.Clear();
            EditorUtility.DisplayDialog("完成", "已移除 Renderer 及其关联骨骼，可通过 Undo 撤销。", "确定");
        }

        private bool GetFoldoutState(Renderer renderer)
        {
            if (renderer == null)
            {
                return false;
            }

            int id = renderer.GetInstanceID();
            if (_boneFoldoutStates.TryGetValue(id, out var state))
            {
                return state;
            }

            _boneFoldoutStates[id] = true;
            return true;
        }

        private void RemoveFoldoutState(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            _boneFoldoutStates.Remove(renderer.GetInstanceID());
        }

        private void PruneFoldoutStates()
        {
            if (_boneFoldoutStates.Count == 0)
            {
                return;
            }

            var validIds = new HashSet<int>();
            for (int i = 0; i < _removeCandidates.Count; i++)
            {
                var candidate = _removeCandidates[i];
                if (candidate == null)
                {
                    continue;
                }

                validIds.Add(candidate.GetInstanceID());
            }

            var staleIds = _boneFoldoutStates.Keys
                .Where(id => !validIds.Contains(id))
                .ToList();

            for (int i = 0; i < staleIds.Count; i++)
            {
                _boneFoldoutStates.Remove(staleIds[i]);
            }
        }
    }
}
