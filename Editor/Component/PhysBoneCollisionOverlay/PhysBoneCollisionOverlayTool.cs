using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MVA.Toolbox.PhysBoneCollisionOverlay
{
    [InitializeOnLoad]
    internal static class PhysBoneCollisionOverlayTool
    {
        private static readonly Color NormalColor = new Color(1f, 1f, 1f, 0.3f);
        private static readonly Color HitColor = new Color(1f, 0.2f, 0.2f, 0.95f);

        private static readonly List<Component> TargetPhysBones = new List<Component>();
        private static readonly List<PhysBoneColliderShape> TempColliderShapes = new List<PhysBoneColliderShape>();
        private static readonly List<bool> TempColliderInsideFlags = new List<bool>();

        private static bool _enabled;
        private static Component _currentSelectionSource;
        private static bool _targetsDirty = true;

        private static readonly Type PhysBoneType;
        private static readonly Type PhysBoneColliderType;

        static PhysBoneCollisionOverlayTool()
        {
            PhysBoneType = PhysBoneCollisionOverlaySerializedAccess.FindType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone");
            PhysBoneColliderType = PhysBoneCollisionOverlaySerializedAccess.FindType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider");

            Selection.selectionChanged += OnSelectionChanged;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        internal static bool IsEnabled => _enabled;

        internal static void ToggleOverlay()
        {
            _enabled = !_enabled;
            _targetsDirty = true;
            Debug.Log(_enabled
                ? "<color=#4DA3FF>[MVA]</color>Phys Bone碰撞检查<color=#4CAF50>已开启</color>"
                : "<color=#4DA3FF>[MVA]</color>Phys Bone碰撞检查<color=#F44336>已关闭</color>");
            SceneView.RepaintAll();
        }

        internal static bool ValidateMenu()
        {
            return true;
        }

        private static void OnSelectionChanged()
        {
            _targetsDirty = true;
            SceneView.RepaintAll();
        }

        private static void OnUndoRedoPerformed()
        {
            _targetsDirty = true;
            SceneView.RepaintAll();
        }

        private static void OnHierarchyChanged()
        {
            _targetsDirty = true;
            SceneView.RepaintAll();
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (!_enabled)
            {
                return;
            }

            if (Event.current == null || Event.current.type != EventType.Repaint)
            {
                return;
            }

            if (_targetsDirty)
            {
                RebuildTargetsBySelection();
                _targetsDirty = false;
            }

            if (TargetPhysBones.Count == 0)
            {
                return;
            }

            Component selectedCollider = null;
            if (_currentSelectionSource != null && IsPhysBoneCollider(_currentSelectionSource))
            {
                selectedCollider = _currentSelectionSource;
            }

            for (int index = 0; index < TargetPhysBones.Count; index++)
            {
                Component physBone = TargetPhysBones[index];
                if (physBone == null)
                {
                    continue;
                }

                DrawPhysBoneOverlay(physBone, selectedCollider);
            }
        }

        private static void RebuildTargetsBySelection()
        {
            TargetPhysBones.Clear();
            _currentSelectionSource = ResolveSelectionSource();

            if (_currentSelectionSource == null)
            {
                return;
            }

            if (IsPhysBone(_currentSelectionSource))
            {
                List<Component> referencedColliders = GetReferencedColliders(_currentSelectionSource);
                if (referencedColliders.Count > 0)
                {
                    TargetPhysBones.Add(_currentSelectionSource);
                }

                return;
            }

            if (!IsPhysBoneCollider(_currentSelectionSource))
            {
                return;
            }

            List<Component> referencingBones = FindPhysBonesReferencingCollider(_currentSelectionSource);
            for (int index = 0; index < referencingBones.Count; index++)
            {
                TargetPhysBones.Add(referencingBones[index]);
            }
        }

        private static Component ResolveSelectionSource()
        {
            if (Selection.activeObject is Component selectedComponent)
            {
                if (IsPhysBoneCollider(selectedComponent) || IsPhysBone(selectedComponent))
                {
                    return selectedComponent;
                }
            }

            GameObject gameObject = Selection.activeGameObject;
            if (gameObject == null)
            {
                return null;
            }

            Component firstPhysBone = null;
            Component firstCollider = null;
            int physBoneCount = 0;
            int colliderCount = 0;

            Component[] components = gameObject.GetComponents<Component>();
            for (int index = 0; index < components.Length; index++)
            {
                Component component = components[index];
                if (component == null)
                {
                    continue;
                }

                if (IsPhysBoneCollider(component))
                {
                    colliderCount++;
                    if (firstCollider == null)
                    {
                        firstCollider = component;
                    }

                    continue;
                }

                if (!IsPhysBone(component))
                {
                    continue;
                }

                physBoneCount++;
                if (firstPhysBone == null)
                {
                    firstPhysBone = component;
                }
            }

            if (physBoneCount > 0 && colliderCount == 0)
            {
                return firstPhysBone;
            }

            if (colliderCount > 0 && physBoneCount == 0)
            {
                return firstCollider;
            }

            return firstPhysBone ?? firstCollider;
        }

        private static void DrawPhysBoneOverlay(Component physBone, Component selectedCollider)
        {
            List<Component> colliders = GetReferencedColliders(physBone);
            if (colliders.Count == 0)
            {
                return;
            }

            if (selectedCollider != null)
            {
                if (!colliders.Contains(selectedCollider))
                {
                    return;
                }

                colliders.Clear();
                colliders.Add(selectedCollider);
            }

            List<PhysBoneBoneSegment> segments = BuildBoneSegments(physBone);
            if (segments.Count == 0)
            {
                return;
            }

            float boneRadius = PhysBoneCollisionOverlaySerializedAccess.ReadFloat(physBone, 0f, "radius");
            if (boneRadius < 0f)
            {
                boneRadius = 0f;
            }

            AnimationCurve radiusCurve = PhysBoneCollisionOverlaySerializedAccess.ReadCurve(physBone, "radiusCurve");

            TempColliderShapes.Clear();
            TempColliderInsideFlags.Clear();
            for (int index = 0; index < colliders.Count; index++)
            {
                if (!PhysBoneCollisionOverlayGeometry.TryBuildColliderShape(colliders[index], out PhysBoneColliderShape shape))
                {
                    continue;
                }

                TempColliderShapes.Add(shape);
                bool insideBounds = PhysBoneCollisionOverlaySerializedAccess.ReadBool(colliders[index], false, "insideBounds");
                TempColliderInsideFlags.Add(insideBounds);
            }

            if (TempColliderShapes.Count == 0)
            {
                return;
            }

            for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
            {
                PhysBoneBoneSegment segment = segments[segmentIndex];
                float startRadius = EvaluateBoneRadius(boneRadius, radiusCurve, segment.StartNormalized);
                float endRadius = EvaluateBoneRadius(boneRadius, radiusCurve, segment.EndNormalized);

                bool drawAsRed = false;
                for (int colliderIndex = 0; colliderIndex < TempColliderShapes.Count; colliderIndex++)
                {
                    bool hit = PhysBoneCollisionOverlayGeometry.IsSegmentColliding(segment.Start, segment.End, startRadius, endRadius, TempColliderShapes[colliderIndex]);
                    bool insideBounds = TempColliderInsideFlags[colliderIndex];
                    bool redForThisCollider = insideBounds ? !hit : hit;
                    if (!redForThisCollider)
                    {
                        continue;
                    }

                    drawAsRed = true;
                    break;
                }

                DrawSegment(segment.Start, segment.End, startRadius, endRadius, drawAsRed ? HitColor : NormalColor);
            }
        }

        private static List<PhysBoneBoneSegment> BuildBoneSegments(Component physBone)
        {
            List<PhysBoneBoneSegment> segments = new List<PhysBoneBoneSegment>();
            List<PhysBoneRawBoneSegment> rawSegments = new List<PhysBoneRawBoneSegment>();

            if (physBone == null)
            {
                return segments;
            }

            Transform root = PhysBoneCollisionOverlaySerializedAccess.ReadObjectReference<Transform>(physBone, "rootTransform");
            if (root == null)
            {
                root = physBone.transform;
            }

            HashSet<Transform> ignoredTransforms = PhysBoneCollisionOverlaySerializedAccess.GetIgnoredTransforms(physBone);
            if (root == null)
            {
                return segments;
            }

            bool rootIgnored = ignoredTransforms.Contains(root);
            bool ignoreRootSelfOnly = rootIgnored && root == physBone.transform;
            if (rootIgnored && !ignoreRootSelfOnly)
            {
                return segments;
            }

            if (root.childCount > 0)
            {
                float maxDistance = 0f;
                Transform initialAnchor = ignoreRootSelfOnly ? null : root;
                CollectChildSegments(root, initialAnchor, 0f, rawSegments, ref maxDistance, ignoredTransforms);
                NormalizeRawSegments(rawSegments, maxDistance, segments);
                return segments;
            }

            if (ignoreRootSelfOnly)
            {
                return segments;
            }

            Vector3 endpointLocal = PhysBoneCollisionOverlaySerializedAccess.ReadVector3(physBone, Vector3.zero, "endpointPosition");
            if (endpointLocal.sqrMagnitude <= 1e-8f)
            {
                endpointLocal = Vector3.up * 0.05f;
            }

            Vector3 endWorld = root.TransformPoint(endpointLocal);
            float length = Vector3.Distance(root.position, endWorld);
            rawSegments.Add(new PhysBoneRawBoneSegment(root.position, endWorld, 0f, length));
            NormalizeRawSegments(rawSegments, length, segments);
            return segments;
        }

        private static void CollectChildSegments(
            Transform traversalNode,
            Transform anchor,
            float anchorDistance,
            List<PhysBoneRawBoneSegment> segments,
            ref float maxDistance,
            HashSet<Transform> ignoredTransforms)
        {
            if (traversalNode == null)
            {
                return;
            }

            for (int childIndex = 0; childIndex < traversalNode.childCount; childIndex++)
            {
                Transform child = traversalNode.GetChild(childIndex);
                if (child == null)
                {
                    continue;
                }

                bool isIgnored = ignoredTransforms != null && ignoredTransforms.Contains(child);
                if (isIgnored)
                {
                    continue;
                }

                if (anchor == null)
                {
                    CollectChildSegments(child, child, anchorDistance, segments, ref maxDistance, ignoredTransforms);
                    continue;
                }

                float currentDistance = anchorDistance + Vector3.Distance(anchor.position, child.position);
                segments.Add(new PhysBoneRawBoneSegment(anchor.position, child.position, anchorDistance, currentDistance));
                if (currentDistance > maxDistance)
                {
                    maxDistance = currentDistance;
                }

                CollectChildSegments(child, child, currentDistance, segments, ref maxDistance, ignoredTransforms);
            }
        }

        private static void NormalizeRawSegments(List<PhysBoneRawBoneSegment> rawSegments, float maxDistance, List<PhysBoneBoneSegment> output)
        {
            output.Clear();
            if (rawSegments == null || rawSegments.Count == 0)
            {
                return;
            }

            float safeMax = maxDistance;
            if (safeMax <= 1e-6f)
            {
                safeMax = 1f;
            }

            for (int index = 0; index < rawSegments.Count; index++)
            {
                PhysBoneRawBoneSegment raw = rawSegments[index];
                float startNormalized = Mathf.Clamp01(raw.StartDistance / safeMax);
                float endNormalized = Mathf.Clamp01(raw.EndDistance / safeMax);
                output.Add(new PhysBoneBoneSegment(raw.Start, raw.End, startNormalized, endNormalized));
            }
        }

        private static float EvaluateBoneRadius(float baseRadius, AnimationCurve curve, float normalized)
        {
            if (baseRadius <= 0f)
            {
                return 0f;
            }

            float multiplier = 1f;
            if (curve != null && curve.length > 0)
            {
                multiplier = curve.Evaluate(Mathf.Clamp01(normalized));
            }

            if (multiplier < 0f)
            {
                multiplier = 0f;
            }

            return baseRadius * multiplier;
        }

        private static void DrawSegment(Vector3 start, Vector3 end, float startRadius, float endRadius, Color color)
        {
            Handles.color = color;

            if ((end - start).sqrMagnitude <= 1e-12f)
            {
                return;
            }

            if (startRadius <= 1e-5f && endRadius <= 1e-5f)
            {
                Handles.DrawAAPolyLine(2f, start, end);
                return;
            }

            Vector3 axis = (end - start).normalized;
            Vector3 referenceNormal = Vector3.up;
            SceneView sceneView = SceneView.currentDrawingSceneView;
            if (sceneView != null && sceneView.camera != null)
            {
                referenceNormal = sceneView.camera.transform.forward;
            }

            Vector3 right = Vector3.Cross(axis, referenceNormal);
            if (right.sqrMagnitude <= 1e-10f)
            {
                right = Vector3.Cross(axis, Vector3.right);
                if (right.sqrMagnitude <= 1e-10f)
                {
                    right = Vector3.Cross(axis, Vector3.up);
                }
            }

            right.Normalize();
            Vector3 up = Vector3.Cross(axis, right).normalized;

            Handles.DrawAAPolyLine(2f, start, end);
            Handles.DrawAAPolyLine(2f, start + right * startRadius, end + right * endRadius);
            Handles.DrawAAPolyLine(2f, start - right * startRadius, end - right * endRadius);
            Handles.DrawAAPolyLine(2f, start + up * startRadius, end + up * endRadius);
            Handles.DrawAAPolyLine(2f, start - up * startRadius, end - up * endRadius);

            if (startRadius > 1e-5f)
            {
                Handles.DrawWireDisc(start, axis, startRadius);
            }

            if (endRadius > 1e-5f)
            {
                Handles.DrawWireDisc(end, axis, endRadius);
            }
        }

        private static List<Component> GetReferencedColliders(Component physBone)
        {
            return PhysBoneCollisionOverlaySerializedAccess.GetReferencedColliders(physBone, IsPhysBoneCollider);
        }

        private static List<Component> FindPhysBonesReferencingCollider(Component collider)
        {
            List<Component> result = new List<Component>();
            if (collider == null)
            {
                return result;
            }

            List<Component> physBones = FindAllPhysBonesInLoadedScenes();
            for (int physBoneIndex = 0; physBoneIndex < physBones.Count; physBoneIndex++)
            {
                Component physBone = physBones[physBoneIndex];
                if (physBone == null)
                {
                    continue;
                }

                List<Component> references = GetReferencedColliders(physBone);
                for (int referenceIndex = 0; referenceIndex < references.Count; referenceIndex++)
                {
                    if (references[referenceIndex] != collider)
                    {
                        continue;
                    }

                    result.Add(physBone);
                    break;
                }
            }

            return result;
        }

        private static List<Component> FindAllPhysBonesInLoadedScenes()
        {
            List<Component> result = new List<Component>();

            if (PhysBoneType != null)
            {
                UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll(PhysBoneType);
                for (int index = 0; index < all.Length; index++)
                {
                    Component component = all[index] as Component;
                    if (component == null || !IsSceneObject(component))
                    {
                        continue;
                    }

                    result.Add(component);
                }

                return result;
            }

            Component[] fallbackAll = Resources.FindObjectsOfTypeAll<Component>();
            for (int index = 0; index < fallbackAll.Length; index++)
            {
                Component component = fallbackAll[index];
                if (component == null || !IsPhysBone(component) || !IsSceneObject(component))
                {
                    continue;
                }

                result.Add(component);
            }

            return result;
        }

        private static bool IsSceneObject(Component component)
        {
            if (component == null || component.gameObject == null)
            {
                return false;
            }

            if (EditorUtility.IsPersistent(component))
            {
                return false;
            }

            UnityEngine.SceneManagement.Scene scene = component.gameObject.scene;
            return scene.IsValid() && scene.isLoaded;
        }

        private static bool IsPhysBone(Component component)
        {
            if (component == null)
            {
                return false;
            }

            Type type = component.GetType();
            if (PhysBoneType != null && PhysBoneType.IsAssignableFrom(type))
            {
                return true;
            }

            string name = type.Name;
            return name.IndexOf("PhysBone", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   name.IndexOf("Collider", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static bool IsPhysBoneCollider(Component component)
        {
            if (component == null)
            {
                return false;
            }

            Type type = component.GetType();
            if (PhysBoneColliderType != null && PhysBoneColliderType.IsAssignableFrom(type))
            {
                return true;
            }

            return type.Name.IndexOf("PhysBoneCollider", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
