using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.FbxReplacement
{
    internal static partial class FbxReplacementComponentWorkflow
    {
        private static void CopyComponentToTarget(
            FbxReplacementStageFourState state,
            FbxReplacementComponentReviewEntry entry,
            bool applyRemap,
            IReadOnlyList<FbxReplacementComponentSelectionHandle> selections)
        {
            if (state == null || entry == null || entry.SourceComponent == null || entry.ComponentType == null)
            {
                return;
            }

            GameObject targetObject = ResolveTargetObject(state, entry);
            if (targetObject == null)
            {
                throw new InvalidOperationException("当前组件缺少可移植的目标物体。");
            }

            Component destinationComponent = EnsureDestinationComponent(targetObject, entry.ComponentType, entry.TypeSlotIndex);
            if (destinationComponent == null)
            {
                throw new InvalidOperationException($"无法在目标物体上创建组件：{entry.DisplayName}");
            }

            EditorUtility.CopySerialized(entry.SourceComponent, destinationComponent);
            if (!applyRemap || entry.ReferenceSlots.Count == 0)
            {
                return;
            }

            var serializedObject = new SerializedObject(destinationComponent);
            for (int i = 0; i < entry.ReferenceSlots.Count; i++)
            {
                FbxReplacementComponentReferenceSlot slot = entry.ReferenceSlots[i];
                SerializedProperty property = serializedObject.FindProperty(slot.PropertyPath);
                if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
                {
                    continue;
                }

                FbxReplacementComponentSelectionHandle selection = i < selections.Count
                    ? selections[i]
                    : slot.RecommendedSelection;
                property.objectReferenceValue = ResolveSelectionHandle(state, selection, slot.ReferenceType);
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Component EnsureDestinationComponent(GameObject targetObject, Type componentType, int typeSlotIndex)
        {
            if (targetObject == null || componentType == null)
            {
                return null;
            }

            Component[] existingComponents = targetObject.GetComponents(componentType);
            while (existingComponents.Length <= typeSlotIndex)
            {
                targetObject.AddComponent(componentType);
                existingComponents = targetObject.GetComponents(componentType);
            }

            return existingComponents[typeSlotIndex];
        }

        private static void StripMigratableComponents(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }

            bool removedAny;
            do
            {
                removedAny = false;
                Component[] components = gameObject.GetComponents<Component>();
                for (int i = components.Length - 1; i >= 0; i--)
                {
                    Component component = components[i];
                    if (component == null || IsSkippedComponent(component))
                    {
                        continue;
                    }

                    Object.DestroyImmediate(component);
                    removedAny = true;
                }
            }
            while (removedAny);
        }

        private static void EnsureMigratableComponentShells(GameObject source, GameObject destination)
        {
            if (source == null || destination == null)
            {
                return;
            }

            Component[] sourceComponents = source.GetComponents<Component>();
            for (int i = 0; i < sourceComponents.Length; i++)
            {
                Component sourceComponent = sourceComponents[i];
                if (sourceComponent == null || IsSkippedComponent(sourceComponent))
                {
                    continue;
                }

                destination.AddComponent(sourceComponent.GetType());
            }
        }

        private static void CopyMigratableComponents(FbxReplacementStageFourState state, GameObject source, GameObject destination)
        {
            if (state == null || source == null || destination == null)
            {
                return;
            }

            Component[] sourceComponents = source.GetComponents<Component>();
            Component[] destinationComponents = destination.GetComponents<Component>();
            int destinationIndex = 0;
            for (int i = 0; i < sourceComponents.Length; i++)
            {
                Component sourceComponent = sourceComponents[i];
                if (sourceComponent == null || IsSkippedComponent(sourceComponent))
                {
                    continue;
                }

                while (destinationIndex < destinationComponents.Length
                    && (destinationComponents[destinationIndex] == null || IsSkippedComponent(destinationComponents[destinationIndex])))
                {
                    destinationIndex++;
                }

                if (destinationIndex >= destinationComponents.Length)
                {
                    return;
                }

                Component destinationComponent = destinationComponents[destinationIndex];
                destinationIndex++;
                EditorUtility.CopySerialized(sourceComponent, destinationComponent);
                RemapTemplateReferencesToLiveTargets(state, sourceComponent, destinationComponent);
            }
        }

        private static void RemapTemplateReferencesToLiveTargets(
            FbxReplacementStageFourState state,
            Component sourceComponent,
            Component destinationComponent)
        {
            if (state == null || sourceComponent == null || destinationComponent == null || state.BaselineTargetTemplate == null)
            {
                return;
            }

            var sourceSerializedObject = new SerializedObject(sourceComponent);
            var destinationSerializedObject = new SerializedObject(destinationComponent);
            SerializedProperty iterator = sourceSerializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.Next(enterChildren))
            {
                enterChildren = true;
                if (iterator.propertyType != SerializedPropertyType.ObjectReference
                    || ShouldSkipObjectReferenceProperty(iterator))
                {
                    continue;
                }

                Object sourceReference = iterator.objectReferenceValue;
                if (sourceReference == null)
                {
                    continue;
                }

                Object remappedReference = RemapTemplateReferenceToLiveTarget(state, sourceReference);
                if (ReferenceEquals(remappedReference, sourceReference))
                {
                    continue;
                }

                SerializedProperty destinationProperty = destinationSerializedObject.FindProperty(iterator.propertyPath);
                if (destinationProperty == null || destinationProperty.propertyType != SerializedPropertyType.ObjectReference)
                {
                    continue;
                }

                destinationProperty.objectReferenceValue = remappedReference;
            }

            destinationSerializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Object RemapTemplateReferenceToLiveTarget(FbxReplacementStageFourState state, Object sourceReference)
        {
            if (state == null || sourceReference == null || state.BaselineTargetTemplate == null)
            {
                return sourceReference;
            }

            GameObject templateReferenceObject = ResolveReferencedGameObject(sourceReference);
            if (templateReferenceObject == null)
            {
                return sourceReference;
            }

            Transform baselineRoot = state.BaselineTargetTemplate.transform;
            if (templateReferenceObject.transform != baselineRoot && !templateReferenceObject.transform.IsChildOf(baselineRoot))
            {
                return sourceReference;
            }

            Transform liveTransform = ResolveTransformByKey(
                state.StageThreeState.StageTwoState.SessionState.Workspace.TargetWorkspaceRoot.transform,
                GetHierarchyIndexPath(baselineRoot, templateReferenceObject.transform));
            if (liveTransform == null)
            {
                return null;
            }

            switch (sourceReference)
            {
                case GameObject _:
                    return liveTransform.gameObject;

                case Transform _:
                    return liveTransform;

                case Component templateComponent:
                    if (templateComponent is Transform)
                    {
                        return liveTransform;
                    }

                    return ResolveMappedComponent(liveTransform, templateComponent.GetType(), GetComponentSlotIndex(templateComponent));

                default:
                    return sourceReference;
            }
        }

        private static Component ResolveMappedComponent(Transform targetTransform, Type componentType, int slotIndex)
        {
            if (targetTransform == null || componentType == null || slotIndex < 0)
            {
                return null;
            }

            Component[] components = targetTransform.GetComponents<Component>();
            int matchedIndex = 0;
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || component is Transform || component.GetType() != componentType)
                {
                    continue;
                }

                if (matchedIndex == slotIndex)
                {
                    return component;
                }

                matchedIndex++;
            }

            return null;
        }

    }
}