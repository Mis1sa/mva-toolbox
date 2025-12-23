using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MVA.Toolbox.Public;

namespace MVA.Toolbox.QuickRemoveBones
{
    internal sealed class QuickRemoveBonesWindow : EditorWindow
    {
        const string WindowTitle = "Quick Remove Bones";

        [MenuItem("Tools/MVA Toolbox/Quick Remove Bones", false, 5)]
        static void Open()
        {
            var window = GetWindow<QuickRemoveBonesWindow>(WindowTitle);
            window.minSize = new Vector2(500f, 600f);
        }

        readonly List<Renderer> _removeCandidates = new List<Renderer>();
        readonly Dictionary<Renderer, List<Transform>> _exclusiveBones = new Dictionary<Renderer, List<Transform>>();
        readonly Dictionary<int, bool> _boneFoldoutStates = new Dictionary<int, bool>();

        Vector2 _mainScroll;

        void OnGUI()
        {
            _mainScroll = ToolboxUtils.ScrollView(_mainScroll, () =>
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("添加需要移除的 Renderer", EditorStyles.boldLabel);
                DrawCandidateSection();
                EditorGUILayout.EndVertical();

                GUILayout.Space(6f);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawBoneSection();
                GUILayout.Space(4f);
                DrawExecuteSection();
                EditorGUILayout.EndVertical();
            });
        }

        void DrawCandidateSection()
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

            bool refreshed = false;
            for (int i = _removeCandidates.Count - 1; i >= 0; i--)
            {
                var candidate = _removeCandidates[i];
                if (candidate == null)
                {
                    RemoveFoldoutState(candidate);
                    _removeCandidates.RemoveAt(i);
                    _exclusiveBones.Remove(candidate);
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

        void DrawBoneSection()
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

        void DrawExecuteSection()
        {
            EditorGUI.BeginDisabledGroup(_removeCandidates.Count == 0);
            if (GUILayout.Button("执行清理", GUILayout.Height(32f)))
            {
                ExecuteRemoval();
            }
            EditorGUI.EndDisabledGroup();
        }

        void TryAddCandidate(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            if (renderer is not SkinnedMeshRenderer)
            {
                EditorUtility.DisplayDialog("仅支持 SkinnedMeshRenderer", "请拖入需要移除的 SkinnedMeshRenderer。", "确定");
                return;
            }

            if (_removeCandidates.Contains(renderer))
            {
                return;
            }

            _removeCandidates.Add(renderer);
            RefreshBoneAnalysis();
        }

        void ClearCandidates()
        {
            if (_removeCandidates.Count == 0)
            {
                return;
            }

            _removeCandidates.Clear();
            RefreshBoneAnalysis();
            _boneFoldoutStates.Clear();
        }

        void RefreshBoneAnalysis()
        {
            _exclusiveBones.Clear();
            if (_removeCandidates.Count == 0)
            {
                return;
            }

            var boneUsage = BuildBoneUsage(_removeCandidates);

            foreach (var candidate in _removeCandidates)
            {
                if (candidate == null)
                {
                    continue;
                }

                EnsureFoldoutState(candidate);
                var bones = CollectExclusiveBones(candidate, boneUsage);
                _exclusiveBones[candidate] = bones;
            }
        }

        Dictionary<Transform, HashSet<Renderer>> BuildBoneUsage(IEnumerable<Renderer> candidates)
        {
            var map = new Dictionary<Transform, HashSet<Renderer>>();
            var visitedRoots = new HashSet<Transform>();

            foreach (var renderer in candidates)
            {
                if (renderer == null)
                {
                    continue;
                }

                var root = renderer.transform != null ? renderer.transform.root : null;
                if (root == null || !visitedRoots.Add(root))
                {
                    continue;
                }

                var skinnedMeshes = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var smr in skinnedMeshes)
                {
                    if (smr == null || smr.bones == null || smr.sharedMesh == null)
                    {
                        continue;
                    }

                    var usedIndices = GetUsedBoneIndices(smr.sharedMesh);
                    if (usedIndices.Count == 0)
                    {
                        continue;
                    }

                    foreach (var boneIndex in usedIndices)
                    {
                        if (boneIndex < 0 || boneIndex >= smr.bones.Length)
                        {
                            continue;
                        }

                        var bone = smr.bones[boneIndex];
                        if (bone == null)
                        {
                            continue;
                        }

                        if (!map.TryGetValue(bone, out var set))
                        {
                            set = new HashSet<Renderer>();
                            map[bone] = set;
                        }

                        set.Add(smr);
                    }
                }
            }

            return map;
        }

        List<Transform> CollectExclusiveBones(Renderer renderer, Dictionary<Transform, HashSet<Renderer>> boneUsage)
        {
            var bones = new List<Transform>();
            var smr = renderer as SkinnedMeshRenderer;
            if (smr == null)
            {
                return bones;
            }

            var allRemoveSet = new HashSet<Renderer>(_removeCandidates);
            var usedIndices = GetUsedBoneIndices(smr.sharedMesh);
            foreach (var boneIndex in usedIndices)
            {
                if (boneIndex < 0 || boneIndex >= smr.bones.Length)
                {
                    continue;
                }

                var bone = smr.bones[boneIndex];
                if (bone == null)
                {
                    continue;
                }

                if (!boneUsage.TryGetValue(bone, out var users) || users.Count == 0)
                {
                    continue;
                }

                if (users.All(allRemoveSet.Contains))
                {
                    bones.Add(bone);
                }
            }

            return bones
                .Where(b => b != null)
                .OrderBy(b => b.name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static HashSet<int> GetUsedBoneIndices(Mesh mesh)
        {
            var indices = new HashSet<int>();
            if (mesh == null)
            {
                return indices;
            }

            var weights = mesh.boneWeights;
            if (weights == null || weights.Length == 0)
            {
                return indices;
            }

            for (int i = 0; i < weights.Length; i++)
            {
                var bw = weights[i];
                if (bw.weight0 > 0f) indices.Add(bw.boneIndex0);
                if (bw.weight1 > 0f) indices.Add(bw.boneIndex1);
                if (bw.weight2 > 0f) indices.Add(bw.boneIndex2);
                if (bw.weight3 > 0f) indices.Add(bw.boneIndex3);
            }

            return indices;
        }

        void ExecuteRemoval()
        {
            if (_removeCandidates.Count == 0)
            {
                return;
            }

            RefreshBoneAnalysis();

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            var removedBones = new HashSet<Transform>();

            foreach (var renderer in _removeCandidates)
            {
                if (renderer == null)
                {
                    continue;
                }

                Undo.DestroyObjectImmediate(renderer.gameObject);

                if (_exclusiveBones.TryGetValue(renderer, out var bones))
                {
                    for (int i = 0; i < bones.Count; i++)
                    {
                        var bone = bones[i];
                        if (bone == null || removedBones.Contains(bone))
                        {
                            continue;
                        }

                        Undo.DestroyObjectImmediate(bone.gameObject);
                        removedBones.Add(bone);
                    }
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            _removeCandidates.Clear();
            _exclusiveBones.Clear();
            _boneFoldoutStates.Clear();
            EditorUtility.DisplayDialog("完成", "已移除 Renderer 及其关联骨骼，可通过 Undo 撤销。", "确定");
        }

        void EnsureFoldoutState(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            int id = renderer.GetInstanceID();
            if (!_boneFoldoutStates.ContainsKey(id))
            {
                _boneFoldoutStates[id] = true;
            }
        }

        bool GetFoldoutState(Renderer renderer)
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

        void RemoveFoldoutState(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            _boneFoldoutStates.Remove(renderer.GetInstanceID());
        }
    }
}
