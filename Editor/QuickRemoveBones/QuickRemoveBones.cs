using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using MVA.Toolbox.Public;

namespace MVA.Toolbox.QuickRemoveBones
{
    internal sealed class QuickRemoveBonesWindow : EditorWindow
    {
        const string WindowTitle = "Quick Remove Bones";

        [MenuItem("Tools/MVA Toolbox/Quick Remove Bones", false, 10)]
        static void Open()
        {
            var window = GetWindow<QuickRemoveBonesWindow>(WindowTitle);
            window.minSize = new Vector2(500f, 600f);
        }

        UnityEngine.Object _avatarObject;
        VRCAvatarDescriptor _lockedAvatar;
        bool _avatarLocked;

        readonly List<Renderer> _removeCandidates = new List<Renderer>();
        readonly Dictionary<Renderer, List<Transform>> _exclusiveBones = new Dictionary<Renderer, List<Transform>>();
        readonly Dictionary<int, bool> _boneFoldoutStates = new Dictionary<int, bool>();

        Vector2 _mainScroll;
        Vector2 _candidateScroll;
        Vector2 _boneScroll;

        void OnGUI()
        {
            _mainScroll = ToolboxUtils.ScrollView(_mainScroll, () =>
            {
                DrawAvatarSection();
                GUILayout.Space(6f);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawLockControls();
                GUILayout.Space(4f);
                if (_avatarLocked && _lockedAvatar != null)
                {
                    DrawCandidateSection();
                }
                EditorGUILayout.EndVertical();

                if (!_avatarLocked || _lockedAvatar == null)
                {
                    return;
                }

                GUILayout.Space(6f);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawBoneSection();
                GUILayout.Space(4f);
                DrawExecuteSection();
                EditorGUILayout.EndVertical();
            });
        }

        void DrawAvatarSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("目标对象", EditorStyles.boldLabel);
            EditorGUI.BeginDisabledGroup(_avatarLocked);
            EditorGUI.BeginChangeCheck();
            var picked = EditorGUILayout.ObjectField("Avatar", _avatarObject, typeof(UnityEngine.Object), true);
            if (EditorGUI.EndChangeCheck())
            {
                HandleAvatarSelection(picked);
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndVertical();
        }

        void DrawLockControls()
        {
            string buttonLabel = _avatarLocked ? "退出检测" : "开始检测";
            EditorGUI.BeginDisabledGroup(_lockedAvatar == null && !_avatarLocked);
            if (GUILayout.Button(buttonLabel, GUILayout.Height(28f)))
            {
                if (_avatarLocked)
                {
                    ExitDetectionMode();
                }
                else if (_lockedAvatar != null)
                {
                    EnterDetectionMode();
                }
            }
            EditorGUI.EndDisabledGroup();
        }

        void EnterDetectionMode()
        {
            _avatarLocked = true;
            _removeCandidates.Clear();
            _exclusiveBones.Clear();
            _candidateScroll = Vector2.zero;
            _boneScroll = Vector2.zero;
            _boneFoldoutStates.Clear();
        }

        void ExitDetectionMode()
        {
            _avatarLocked = false;
            _removeCandidates.Clear();
            _exclusiveBones.Clear();
            _candidateScroll = Vector2.zero;
            _boneScroll = Vector2.zero;
            _boneFoldoutStates.Clear();
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
            if (renderer == null || !_avatarLocked || _lockedAvatar == null)
            {
                return;
            }

            if (!IsRendererUnderAvatar(renderer.transform))
            {
                EditorUtility.DisplayDialog("无效对象", "仅能添加当前 Avatar 下的 Renderer。", "确定");
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

        bool IsRendererUnderAvatar(Transform rendererTransform)
        {
            if (rendererTransform == null || _lockedAvatar == null)
            {
                return false;
            }

            var root = _lockedAvatar.gameObject.transform;
            var current = rendererTransform;
            while (current != null)
            {
                if (current == root)
                {
                    return true;
                }
                current = current.parent;
            }

            return false;
        }

        void RefreshBoneAnalysis()
        {
            _exclusiveBones.Clear();
            if (_removeCandidates.Count == 0 || _lockedAvatar == null)
            {
                return;
            }

            var skinnedMeshes = _lockedAvatar.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToList();
            var boneUsage = BuildBoneUsage(skinnedMeshes);

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

        Dictionary<Transform, HashSet<Renderer>> BuildBoneUsage(List<SkinnedMeshRenderer> skinnedMeshes)
        {
            var map = new Dictionary<Transform, HashSet<Renderer>>();
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

        void HandleAvatarSelection(UnityEngine.Object picked)
        {
            if (picked == null)
            {
                if (_avatarLocked)
                {
                    ExitDetectionMode();
                }
                _avatarObject = null;
                _lockedAvatar = null;
                return;
            }

            var descriptor = ToolboxUtils.GetAvatarDescriptor(picked);
            if (descriptor == null)
            {
                EditorUtility.DisplayDialog("无效对象", "请拖入带 VRCAvatarDescriptor 的 Avatar。", "确定");
                return;
            }

            _lockedAvatar = descriptor;
            _avatarObject = descriptor;
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
