using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using System.Reflection;
using UnityEngine;
using MVA.Toolbox.QuickRemoveBones;

namespace MVA.Toolbox.BoneSyncTools
{
    /// <summary>
    /// 在编辑态/录制态下同步网格与其骨骼的启用状态。
    /// </summary>
    [InitializeOnLoad]
    internal static class BoneActiveSyncTool
    {
        private const string MenuRoot = "Tools/MVA Toolbox/Bone Active Sync";
        private const string MenuEnable = MenuRoot + "/启用功能";
        private const string MenuExclusiveOnly = MenuRoot + "/仅独占骨骼";
        private const string MenuCheckChildren = MenuRoot + "/检查子级网格";
        private const string MenuWriteAnim = MenuRoot + "/写入动画属性";
        private const string MenuRemoveAnim = MenuRoot + "/移除动画属性";
        private const int MenuPriority = 31;

        private const string PrefEnabled = "MVA.BoneActiveSync.Enabled";
        private const string PrefExclusiveOnly = "MVA.BoneActiveSync.ExclusiveOnly";
        private const string PrefCheckChildren = "MVA.BoneActiveSync.CheckChildren";
        private const string PrefWriteAnim = "MVA.BoneActiveSync.WriteAnim";
        private const string PrefRemoveAnim = "MVA.BoneActiveSync.RemoveAnim";

        // 总开关要求：每次打开工程默认关闭
        private static bool _enabled;
        private static bool _exclusiveOnly;
        private static bool _checkChildren;
        private static bool _writeAnim;
        private static bool _removeAnim;

        private static Transform _lastRoot;
        private static Transform _lastRecordingRoot;
        private static int _externalSuppressionCount;

        static BoneActiveSyncTool()
        {
            // 从偏好加载，但总开关强制默认关
            _exclusiveOnly = EditorPrefs.GetBool(PrefExclusiveOnly, false);
            _checkChildren = EditorPrefs.GetBool(PrefCheckChildren, true);
            _writeAnim = EditorPrefs.GetBool(PrefWriteAnim, false);
            _removeAnim = EditorPrefs.GetBool(PrefRemoveAnim, false);
            _enabled = false;
            EditorPrefs.SetBool(PrefEnabled, false);
            Menu.SetChecked(MenuEnable, _enabled);

            Undo.postprocessModifications += OnPostprocessModifications;
            AnimationUtility.onCurveWasModified += OnCurveWasModified;
        }

        public static void PushExternalSuppression()
        {
            _externalSuppressionCount = Mathf.Max(0, _externalSuppressionCount) + 1;
        }

        public static void PopExternalSuppression()
        {
            if (_externalSuppressionCount <= 0)
                return;
            _externalSuppressionCount--;
        }

        private static bool IsFeatureActive()
        {
            return _enabled && _externalSuppressionCount == 0;
        }

        [MenuItem(MenuEnable, false, MenuPriority)]
        private static void ToggleEnabled()
        {
            _enabled = !_enabled;
            Menu.SetChecked(MenuEnable, _enabled);
            EditorPrefs.SetBool(PrefEnabled, _enabled);
            Debug.Log($"[BoneActiveSync] 功能已{(_enabled ? "启用" : "禁用")}（每次打开工程默认为禁用）。");
        }

        [MenuItem(MenuExclusiveOnly, false, MenuPriority + 1)]
        private static void ToggleExclusiveOnly()
        {
            _exclusiveOnly = !_exclusiveOnly;
            EditorPrefs.SetBool(PrefExclusiveOnly, _exclusiveOnly);
            Debug.Log($"[BoneActiveSync] 仅独占骨骼：{_exclusiveOnly}");
        }

        [MenuItem(MenuCheckChildren, false, MenuPriority + 2)]
        private static void ToggleCheckChildren()
        {
            _checkChildren = !_checkChildren;
            EditorPrefs.SetBool(PrefCheckChildren, _checkChildren);
            Debug.Log($"[BoneActiveSync] 检查子级网格：{_checkChildren}");
        }

        [MenuItem(MenuWriteAnim, false, MenuPriority + 3)]
        private static void ToggleWriteAnim()
        {
            _writeAnim = !_writeAnim;
            EditorPrefs.SetBool(PrefWriteAnim, _writeAnim);
            Debug.Log($"[BoneActiveSync] 写入动画属性：{_writeAnim}");
        }

        [MenuItem(MenuRemoveAnim, false, MenuPriority + 4)]
        private static void ToggleRemoveAnim()
        {
            _removeAnim = !_removeAnim;
            EditorPrefs.SetBool(PrefRemoveAnim, _removeAnim);
            Debug.Log($"[BoneActiveSync] 移除动画属性：{_removeAnim}");
        }

        [MenuItem(MenuEnable, true)]
        private static bool ValidateToggleEnabled()
        {
            Menu.SetChecked(MenuEnable, _enabled);
            return true;
        }

        [MenuItem(MenuExclusiveOnly, true)]
        private static bool ValidateToggleExclusiveOnly()
        {
            Menu.SetChecked(MenuExclusiveOnly, _exclusiveOnly);
            return true;
        }

        [MenuItem(MenuCheckChildren, true)]
        private static bool ValidateToggleCheckChildren()
        {
            Menu.SetChecked(MenuCheckChildren, _checkChildren);
            return true;
        }

        [MenuItem(MenuWriteAnim, true)]
        private static bool ValidateWriteAnim()
        {
            Menu.SetChecked(MenuWriteAnim, _writeAnim);
            return true;
        }

        [MenuItem(MenuRemoveAnim, true)]
        private static bool ValidateRemoveAnim()
        {
            Menu.SetChecked(MenuRemoveAnim, _removeAnim);
            return true;
        }

        private static UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] mods)
        {
            if (!IsFeatureActive() || mods == null || mods.Length == 0)
            {
                return mods;
            }

            foreach (var m in mods)
            {
                var pm = m.currentValue;
                if (pm == null || pm.target == null)
                    continue;

                if (pm.propertyPath != "m_IsActive")
                    continue;

                if (pm.target is not GameObject go)
                    continue;

                bool isActive = go.activeSelf;
                HandleGameObjectActiveChanged(go, isActive);
            }

            return mods;
        }

        private static void HandleGameObjectActiveChanged(GameObject go, bool active)
        {
            if (go == null)
                return;

            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr == null || smr.sharedMesh == null || smr.bones == null)
                return;

            // 构建骨骼使用映射
            var root = smr.transform != null ? smr.transform.root : null;
            if (root == null)
                return;

            var smrsUnderRoot = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (smrsUnderRoot == null || smrsUnderRoot.Length == 0)
                return;

            var boneUsage = BoneExclusivityUtil.BuildBoneUsage(smrsUnderRoot);
            _lastRoot = root;

            // 独占骨骼：仅被当前 SMR 使用
            var exclusiveBones = BoneExclusivityUtil.CollectExclusiveBones(smr, boneUsage, new[] { smr });
            if (exclusiveBones == null || exclusiveBones.Count == 0)
                return;

            var candidateTargets = new HashSet<Transform>();
            if (_exclusiveOnly)
            {
                foreach (var b in exclusiveBones)
                {
                    if (b == null) continue;
                    var target = b;
                    if (!active && !IsSafeContainer(target, smr, boneUsage, _checkChildren))
                        continue;
                    candidateTargets.Add(target);
                }
            }
            else
            {
                foreach (var b in exclusiveBones)
                {
                    if (b == null) continue;
                    var t = PickMinimalTarget(b, smr, boneUsage);
                    if (t == null) continue;
                    if (!IsSafeContainer(t, smr, boneUsage, _checkChildren))
                        continue;
                    candidateTargets.Add(t);
                }
            }

            var targets = _exclusiveOnly ? candidateTargets : ReduceTargets(candidateTargets);

            foreach (var t in targets)
            {
                if (t == null) continue;
                t.gameObject.SetActive(active);
                if (_writeAnim && ShouldWriteKeyframe(t.gameObject, root))
                {
                    WriteActiveKeyframe(t.gameObject, active, root);
                }
            }
        }

        private static Transform PickMinimalTarget(Transform bone, SkinnedMeshRenderer smr, Dictionary<Transform, HashSet<Renderer>> boneUsage)
        {
            if (bone == null) return null;

            var parent = bone.parent;
            if (parent == null)
                return bone;

            if (!IsSafeContainer(parent, smr, boneUsage, _checkChildren))
            {
                return bone;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child == null || child == bone)
                    continue;

                if (!IsSafeContainer(child, smr, boneUsage, _checkChildren))
                {
                    return bone;
                }
            }

            return parent;
        }

        /// <summary>
        /// 将候选目标收敛为最小集
        /// </summary>
        private static HashSet<Transform> ReduceTargets(HashSet<Transform> candidates)
        {
            var result = new HashSet<Transform>();
            if (candidates == null || candidates.Count == 0) return result;

            // 按层级从浅到深排序，优先放入高层节点
            var ordered = candidates
                .Where(t => t != null)
                .OrderBy(t => GetDepth(t))
                .ToList();

            foreach (var t in ordered)
            {
                if (t == null) continue;
                // 如果已存在的目标是其祖先，则跳过
                if (result.Any(r => IsAncestorOf(r, t)))
                    continue;

                result.RemoveWhere(r => IsAncestorOf(t, r));
                result.Add(t);
            }

            return result;
        }

        private static int GetDepth(Transform t)
        {
            int d = 0;
            var cur = t;
            while (cur != null)
            {
                d++;
                cur = cur.parent;
            }
            return d;
        }

        private static bool IsAncestorOf(Transform ancestor, Transform node)
        {
            if (ancestor == null || node == null) return false;
            var cur = node.parent;
            while (cur != null)
            {
                if (cur == ancestor) return true;
                cur = cur.parent;
            }
            return false;
        }

        private static bool IsRecording()
        {
            return AnimationMode.InAnimationMode();
        }

        private static bool ShouldWriteKeyframe(GameObject go, Transform root)
        {
            if (go == null) return false;
            if (!IsRecording()) return false;

            var ctx = GetActiveAnimationContext();
            if (ctx.clip == null || ctx.root == null) return false;

            var path = AnimationUtility.CalculateTransformPath(go.transform, ctx.root);
            return true;
        }

        private static void WriteActiveKeyframe(GameObject go, bool active, Transform root)
        {
            if (go == null) return;
            var ctx = GetActiveAnimationContext();
            if (ctx.clip == null || ctx.root == null) return;
            _lastRecordingRoot = ctx.root;

            var binding = new EditorCurveBinding
            {
                type = typeof(GameObject),
                path = AnimationUtility.CalculateTransformPath(go.transform, ctx.root),
                propertyName = "m_IsActive"
            };

            var curve = AnimationUtility.GetEditorCurve(ctx.clip, binding) ?? new AnimationCurve();
            curve.AddKey(new Keyframe(ctx.time, active ? 1f : 0f));
            AnimationUtility.SetEditorCurve(ctx.clip, binding, curve);
        }

        private static (AnimationClip clip, Transform root, float time) GetActiveAnimationContext()
        {
            var window = Resources.FindObjectsOfTypeAll(typeof(EditorWindow))
                .FirstOrDefault(w => w.GetType().Name == "AnimationWindow");
            if (window == null) return (null, null, 0f);

            var animEditorField = window.GetType().GetField("m_AnimEditor", BindingFlags.NonPublic | BindingFlags.Instance);
            var animEditor = animEditorField?.GetValue(window);
            if (animEditor == null) return (null, null, 0f);

            var stateProp = animEditor.GetType().GetProperty("state", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var state = stateProp?.GetValue(animEditor);
            if (state == null) return (null, null, 0f);

            var clipProp = state.GetType().GetProperty("activeAnimationClip", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var clip = clipProp?.GetValue(state) as AnimationClip;
            if (clip == null) return (null, null, 0f);

            var rootProp = state.GetType().GetProperty("activeRootGameObject", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var rootObj = rootProp?.GetValue(state) as GameObject;
            if (rootObj == null) return (null, null, 0f);

            var timeProp = state.GetType().GetProperty("currentTime", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var timeObj = timeProp?.GetValue(state);
            float time = timeObj is float f ? f : 0f;

            return (clip, rootObj.transform, time);
        }

        private static void OnCurveWasModified(AnimationClip clip, EditorCurveBinding binding, AnimationUtility.CurveModifiedType type)
        {
            if (!IsFeatureActive() || !_removeAnim) return;
            if (type != AnimationUtility.CurveModifiedType.CurveDeleted) return;
            if (binding.type != typeof(GameObject) || binding.propertyName != "m_IsActive") return;
            if (clip == null) return;
            var rootForRemoval = _lastRecordingRoot != null ? _lastRecordingRoot : _lastRoot;
            if (rootForRemoval == null) return;

            var target = AnimationUtility.GetAnimatedObject(rootForRemoval.gameObject, binding) as GameObject;
            if (target == null) return;

            var smr = target.GetComponent<SkinnedMeshRenderer>();
            if (smr == null || smr.sharedMesh == null || smr.bones == null) return;

            var smrsUnderRoot = rootForRemoval.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var boneUsage = BoneExclusivityUtil.BuildBoneUsage(smrsUnderRoot);
            var exclusiveBones = BoneExclusivityUtil.CollectExclusiveBones(smr, boneUsage, new[] { smr });
            if (exclusiveBones == null || exclusiveBones.Count == 0) return;

            HashSet<Transform> candidateTargets = _exclusiveOnly
                ? new HashSet<Transform>(exclusiveBones.Where(b => b != null))
                : ReduceTargets(new HashSet<Transform>(exclusiveBones.Where(b => b != null).Select(b => PickMinimalTarget(b, smr, boneUsage))));

            foreach (var t in candidateTargets)
            {
                if (t == null) continue;
                var path = AnimationUtility.CalculateTransformPath(t, rootForRemoval);
                var boneBinding = new EditorCurveBinding
                {
                    type = typeof(GameObject),
                    path = path,
                    propertyName = "m_IsActive"
                };
                AnimationUtility.SetEditorCurve(clip, boneBinding, null);
            }
        }

        private static bool IsUsedBone(Transform t, Dictionary<Transform, HashSet<Renderer>> boneUsage)
        {
            return t != null && boneUsage != null && boneUsage.ContainsKey(t);
        }

        private static bool IsSafeContainer(Transform t, SkinnedMeshRenderer smr, Dictionary<Transform, HashSet<Renderer>> boneUsage, bool checkChildren)
        {
            if (t == null) return false;
            // 若 t 是骨骼，但被其他 Renderer 使用，则不安全
            if (boneUsage != null && boneUsage.TryGetValue(t, out var users) && users.Any(r => r != smr))
                return false;
            // 渲染器/子树检查
            if (HasRenderer(t, smr, checkChildren)) return false;
            if (checkChildren && ContainsBoneUsedByOthers(t, smr, boneUsage)) return false;
            return true;
        }

        private static bool HasRenderer(Transform t, SkinnedMeshRenderer allowRenderer, bool checkChildren)
        {
            if (t == null) return false;
            var r = t.GetComponent<Renderer>();
            if (r != null && r != allowRenderer) return true;
            if (!checkChildren) return false;

            var stack = new Stack<Transform>();
            stack.Push(t);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                if (cur == null) continue;
                if (cur != t)
                {
                    var cr = cur.GetComponent<Renderer>();
                    if (cr != null && cr != allowRenderer)
                        return true;
                }
                for (int i = 0; i < cur.childCount; i++)
                {
                    stack.Push(cur.GetChild(i));
                }
            }

            return false;
        }

        private static bool ContainsBoneUsedByOthers(Transform t, SkinnedMeshRenderer smr, Dictionary<Transform, HashSet<Renderer>> boneUsage)
        {
            if (t == null || boneUsage == null || boneUsage.Count == 0) return false;
            var stack = new Stack<Transform>();
            stack.Push(t);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                if (cur == null) continue;
                if (boneUsage.TryGetValue(cur, out var users) && users.Any(r => r != smr))
                    return true;
                for (int i = 0; i < cur.childCount; i++)
                {
                    stack.Push(cur.GetChild(i));
                }
            }
            return false;
        }
    }
}
