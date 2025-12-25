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

        bool _removeChildNonBoneObjects = true;
        bool _excludeForeignChildObjects = true;

        HashSet<Transform> _protectedBones = new HashSet<Transform>();
        HashSet<Transform> _allBones = new HashSet<Transform>();

        Vector2 _mainScroll;

        void OnGUI()
        {
            HandleGlobalDragAndDrop();

            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            _mainScroll = ToolboxUtils.ScrollView(_mainScroll, () =>
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

            DrawChildRemovalOptions();

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

        void DrawChildRemovalOptions()
        {
            EditorGUILayout.BeginHorizontal();
            _removeChildNonBoneObjects = EditorGUILayout.ToggleLeft("移除子级中非骨骼物体", _removeChildNonBoneObjects);
            using (new EditorGUI.DisabledScope(!_removeChildNonBoneObjects))
            {
                _excludeForeignChildObjects = EditorGUILayout.ToggleLeft("排除其他骨骼下的非骨骼物体", _excludeForeignChildObjects);
            }
            EditorGUILayout.EndHorizontal();
        }

        void HandleGlobalDragAndDrop()
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

        void TryAddCandidates(IEnumerable<UnityEngine.Object> objects)
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

        bool TryAddCandidate(Renderer renderer, bool refreshImmediately = true)
        {
            if (renderer == null)
            {
                return false;
            }

            if (renderer is not SkinnedMeshRenderer)
            {
                EditorUtility.DisplayDialog("仅支持 SkinnedMeshRenderer", "请拖入需要移除的 SkinnedMeshRenderer。", "确定");
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
            _allBones = new HashSet<Transform>(boneUsage.Keys);
            UpdateProtectedBones(boneUsage);

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
            var bonesToRemove = new HashSet<Transform>();
            foreach (var kvp in _exclusiveBones)
            {
                foreach (var bone in kvp.Value)
                {
                    if (bone != null)
                    {
                        bonesToRemove.Add(bone);
                    }
                }
            }

            var context = new RemovalContext(bonesToRemove, _protectedBones, _allBones);
            var extraRemovals = new HashSet<Transform>();
            var preservedNodes = new HashSet<Transform>();

            foreach (var bone in bonesToRemove)
            {
                if (bone == null)
                {
                    continue;
                }

                CollectChildActions(bone, context, extraRemovals, preservedNodes);
            }

            var nodesToRemove = new HashSet<Transform>(bonesToRemove);
            nodesToRemove.UnionWith(extraRemovals);

            ReleasePreservedNodes(preservedNodes, nodesToRemove);

            foreach (var renderer in _removeCandidates)
            {
                if (renderer == null)
                {
                    continue;
                }

                Undo.DestroyObjectImmediate(renderer.gameObject);
            }

            foreach (var node in extraRemovals)
            {
                if (node == null)
                {
                    continue;
                }

                Undo.DestroyObjectImmediate(node.gameObject);
            }

            foreach (var bone in bonesToRemove)
            {
                if (bone == null || removedBones.Contains(bone))
                {
                    continue;
                }

                Undo.DestroyObjectImmediate(bone.gameObject);
                removedBones.Add(bone);
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

        void UpdateProtectedBones(Dictionary<Transform, HashSet<Renderer>> boneUsage)
        {
            _protectedBones.Clear();
            if (boneUsage == null || boneUsage.Count == 0)
            {
                return;
            }

            var candidateSet = new HashSet<Renderer>(_removeCandidates.Where(r => r != null));
            foreach (var pair in boneUsage)
            {
                if (pair.Value.Any(renderer => !candidateSet.Contains(renderer)))
                {
                    _protectedBones.Add(pair.Key);
                }
            }
        }

        void CollectChildActions(
            Transform bone,
            RemovalContext context,
            HashSet<Transform> extraRemovals,
            HashSet<Transform> preservedNodes)
        {
            if (bone == null)
            {
                return;
            }

            for (int i = 0; i < bone.childCount; i++)
            {
                var child = bone.GetChild(i);
                if (child == null || context.BonesToRemove.Contains(child))
                {
                    continue;
                }

                if (context.BonesToKeep.Contains(child) || HasRendererComponent(child))
                {
                    preservedNodes.Add(child);
                    continue;
                }

                if (ShouldRemoveChildObject(child, context))
                {
                    MarkSubtreeForRemoval(child, extraRemovals, context);
                    continue;
                }

                preservedNodes.Add(child);
                CollectChildActions(child, context, extraRemovals, preservedNodes);
            }
        }

        void ReleasePreservedNodes(IEnumerable<Transform> preservedNodes, HashSet<Transform> nodesToRemove)
        {
            foreach (var node in preservedNodes)
            {
                if (node == null)
                {
                    continue;
                }

                var parent = node.parent;
                if (parent == null || !nodesToRemove.Contains(parent))
                {
                    continue;
                }

                var safeParent = FindSafeParent(parent, nodesToRemove);
                Undo.SetTransformParent(node, safeParent, "释放骨骼子级");
            }
        }

        static Transform FindSafeParent(Transform start, HashSet<Transform> nodesToRemove)
        {
            var current = start;
            while (current != null && nodesToRemove.Contains(current))
            {
                current = current.parent;
            }

            return current;
        }

        bool ShouldRemoveChildObject(Transform node, RemovalContext context)
        {
            if (!_removeChildNonBoneObjects || node == null)
            {
                return false;
            }

            if (context.AllBones.Contains(node) || HasRendererComponent(node))
            {
                return false;
            }

            if (_excludeForeignChildObjects && ContainsProtectedElement(node, context))
            {
                return false;
            }

            return true;
        }

        void MarkSubtreeForRemoval(Transform node, HashSet<Transform> extraRemovals, RemovalContext context)
        {
            if (node == null || extraRemovals.Contains(node))
            {
                return;
            }

            if (context.BonesToKeep.Contains(node))
            {
                return;
            }

            extraRemovals.Add(node);
            for (int i = 0; i < node.childCount; i++)
            {
                var child = node.GetChild(i);
                MarkSubtreeForRemoval(child, extraRemovals, context);
            }
        }

        bool ContainsProtectedElement(Transform node, RemovalContext context)
        {
            var stack = new Stack<Transform>();
            stack.Push(node);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current == null)
                {
                    continue;
                }

                if (context.BonesToKeep.Contains(current) || HasRendererComponent(current))
                {
                    return true;
                }

                for (int i = 0; i < current.childCount; i++)
                {
                    stack.Push(current.GetChild(i));
                }
            }

            return false;
        }

        static bool HasRendererComponent(Transform node)
        {
            return node != null && node.GetComponent<Renderer>() != null;
        }

        readonly struct RemovalContext
        {
            public readonly HashSet<Transform> BonesToRemove;
            public readonly HashSet<Transform> BonesToKeep;
            public readonly HashSet<Transform> AllBones;

            public RemovalContext(HashSet<Transform> remove, HashSet<Transform> keep, HashSet<Transform> all)
            {
                BonesToRemove = remove ?? new HashSet<Transform>();
                BonesToKeep = keep ?? new HashSet<Transform>();
                AllBones = all ?? new HashSet<Transform>();
            }
        }
    }
}
