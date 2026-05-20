using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.FindReferencesTool
{
    internal delegate bool FindReferencesIndirectMatcher(Object source, Object target, Object rootContainer, HashSet<int> visited, out Object actualContainer);

    internal sealed class FindReferencesAnimatorSupport
    {
        internal bool SearchAnimationClipReferences(AnimationClip clip, Object target, List<FindReferencesLocation> locations)
        {
            if (clip == null || target == null || locations == null)
            {
                return false;
            }

            EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
            {
                EditorCurveBinding binding = bindings[bindingIndex];
                ObjectReferenceKeyframe[] keyframes = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                for (int keyframeIndex = 0; keyframeIndex < keyframes.Length; keyframeIndex++)
                {
                    ObjectReferenceKeyframe keyframe = keyframes[keyframeIndex];
                    if (keyframe.value != target)
                    {
                        continue;
                    }

                    locations.Add(new FindReferencesLocation
                    {
                        SourceObject = clip,
                        DirectReferenceObject = clip,
                        MatchedContainer = target,
                        PropertyPath = $"{binding.path}:{binding.propertyName}"
                    });
                }
            }

            return locations.Count > 0;
        }

        internal bool SearchAnimatorControllerReferences(
            AnimatorController controller,
            Object target,
            List<FindReferencesLocation> locations,
            FindReferencesIndirectMatcher indirectMatcher,
            Object rootContainer,
            HashSet<int> visited,
            bool allowIndirect)
        {
            if (controller == null || target == null || locations == null)
            {
                return false;
            }

            bool found = false;
            HashSet<int> baseVisited = visited != null ? new HashSet<int>(visited) : new HashSet<int>();
            baseVisited.Add(controller.GetInstanceID());

            for (int layerIndex = 0; layerIndex < controller.layers.Length; layerIndex++)
            {
                AnimatorControllerLayer layer = controller.layers[layerIndex];
                if (layer.stateMachine == null)
                {
                    continue;
                }

                SearchStateMachine(
                    layer.stateMachine,
                    target,
                    controller,
                    layer.name,
                    locations,
                    indirectMatcher,
                    rootContainer,
                    baseVisited,
                    allowIndirect,
                    ref found);
            }

            return found;
        }

        private void SearchStateMachine(
            AnimatorStateMachine stateMachine,
            Object target,
            AnimatorController controller,
            string currentPath,
            List<FindReferencesLocation> locations,
            FindReferencesIndirectMatcher indirectMatcher,
            Object rootContainer,
            HashSet<int> visited,
            bool allowIndirect,
            ref bool found)
        {
            if (stateMachine == null)
            {
                return;
            }

            ChildAnimatorState[] states = stateMachine.states;
            for (int stateIndex = 0; stateIndex < states.Length; stateIndex++)
            {
                ChildAnimatorState childState = states[stateIndex];
                if (childState.state == null)
                {
                    continue;
                }

                string statePath = $"{currentPath} > {childState.state.name}";
                Motion motion = childState.state.motion;
                if (motion == target)
                {
                    locations.Add(new FindReferencesLocation
                    {
                        SourceObject = controller,
                        DirectReferenceObject = controller,
                        MatchedContainer = target,
                        PropertyPath = statePath
                    });
                    found = true;
                    continue;
                }

                if (!allowIndirect || motion == null || indirectMatcher == null)
                {
                    continue;
                }

                if (!indirectMatcher(motion, target, rootContainer, visited, out Object actualContainer))
                {
                    continue;
                }

                locations.Add(new FindReferencesLocation
                {
                    SourceObject = controller,
                    DirectReferenceObject = actualContainer != null ? actualContainer : motion,
                    MatchedContainer = actualContainer,
                    PropertyPath = statePath
                });
                found = true;
            }

            ChildAnimatorStateMachine[] childStateMachines = stateMachine.stateMachines;
            for (int machineIndex = 0; machineIndex < childStateMachines.Length; machineIndex++)
            {
                ChildAnimatorStateMachine childStateMachine = childStateMachines[machineIndex];
                if (childStateMachine.stateMachine == null)
                {
                    continue;
                }

                string childPath = $"{currentPath} > {childStateMachine.stateMachine.name}";
                SearchStateMachine(
                    childStateMachine.stateMachine,
                    target,
                    controller,
                    childPath,
                    locations,
                    indirectMatcher,
                    rootContainer,
                    visited,
                    allowIndirect,
                    ref found);
            }
        }
    }
}
