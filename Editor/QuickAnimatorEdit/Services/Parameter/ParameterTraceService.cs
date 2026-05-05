using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.QuickAnimatorEdit.Services.Parameter
{
    public static class ParameterTraceService
    {
        public enum TraceSourceKind
        {
            TransitionCondition,
            StateParameter,
            BlendTreeParameter,
            AvatarParameterDriverSource,
            AvatarParameterDriverTarget,
            AnimationCurve,
            SceneComponent
        }

        public sealed class TraceEntry
        {
            public TraceSourceKind SourceKind;
            public string Description;
            public string LayerName;
            public string SourceState;
            public string DestinationState;
            public string ComponentName;
            public string BlendTreePath;
            public string HierarchyPath;
            public string PropertyPath;
            public UnityEngine.Object RelatedObject;
        }

        public sealed class TraceResult
        {
            public string ParameterName;
            public List<TraceEntry> References = new List<TraceEntry>();
            public List<TraceEntry> Modifications = new List<TraceEntry>();
            public bool HasAny => References.Count > 0 || Modifications.Count > 0;
        }

        private static readonly string[] _physBoneSuffixes =
        {
            "_IsGrabbed",
            "_IsPosed",
            "_Angle",
            "_Stretch",
            "_Squish"
        };

        public static TraceResult Execute(
            AnimatorController controller,
            string parameterName,
            GameObject sceneRoot)
        {
            var result = new TraceResult
            {
                ParameterName = parameterName ?? string.Empty
            };

            if (controller == null || string.IsNullOrEmpty(parameterName))
            {
                return result;
            }

            var referenceDedup = new HashSet<string>(StringComparer.Ordinal);
            var modificationDedup = new HashSet<string>(StringComparer.Ordinal);

            RegisterControllerUsages(
                controller,
                parameterName,
                result,
                referenceDedup,
                modificationDedup);

            RegisterAnimationCurveModifications(
                controller,
                parameterName,
                result,
                modificationDedup);

            RegisterSceneComponentModifications(
                sceneRoot,
                parameterName,
                result,
                modificationDedup);

            result.References = result.References
                .OrderBy(x => x.LayerName)
                .ThenBy(x => x.SourceState)
                .ThenBy(x => x.DestinationState)
                .ThenBy(x => x.BlendTreePath)
                .ThenBy(x => x.ComponentName)
                .ThenBy(x => x.HierarchyPath)
                .ThenBy(x => x.Description)
                .ToList();

            result.Modifications = result.Modifications
                .OrderBy(x => x.LayerName)
                .ThenBy(x => x.SourceState)
                .ThenBy(x => x.ComponentName)
                .ThenBy(x => x.HierarchyPath)
                .ThenBy(x => x.Description)
                .ToList();

            return result;
        }

        private static void RegisterControllerUsages(
            AnimatorController controller,
            string parameterName,
            TraceResult result,
            HashSet<string> referenceDedup,
            HashSet<string> modificationDedup)
        {
            var layers = controller.layers;
            if (layers == null || layers.Length == 0)
            {
                return;
            }

            for (int i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                AnalyzeStateMachine(
                    layer.stateMachine,
                    BuildLayerName(layer, i),
                    string.Empty,
                    parameterName,
                    result,
                    referenceDedup,
                    modificationDedup);
            }
        }

        private static bool TryRegisterKnownSceneWriterModification(
            Component component,
            GameObject sceneRoot,
            string parameterName,
            TraceResult result,
            HashSet<string> modificationDedup)
        {
            if (component == null || sceneRoot == null)
            {
                return false;
            }

            SerializedObject serializedObject;
            try
            {
                serializedObject = new SerializedObject(component);
            }
            catch
            {
                return false;
            }

            var parameterProp = serializedObject.FindProperty("parameter");
            if (parameterProp == null || parameterProp.propertyType != SerializedPropertyType.String)
            {
                return false;
            }

            if (!string.Equals(parameterProp.stringValue, parameterName, StringComparison.Ordinal))
            {
                return false;
            }

            AddModification(
                result,
                modificationDedup,
                TraceSourceKind.SceneComponent,
                "场景组件写入参数",
                component,
                hierarchyPath: GetRelativeHierarchyPath(sceneRoot.transform, component.transform),
                componentName: component.GetType().Name,
                propertyPath: "parameter");

            return true;
        }

        private static string BuildLayerName(AnimatorControllerLayer layer, int layerIndex)
        {
            string layerName = layer != null && !string.IsNullOrEmpty(layer.name)
                ? layer.name
                : $"Layer {layerIndex}";
            return $"{layerName} (#{layerIndex})";
        }

        private static void AnalyzeStateMachine(
            AnimatorStateMachine stateMachine,
            string layerName,
            string stateMachinePath,
            string parameterName,
            TraceResult result,
            HashSet<string> referenceDedup,
            HashSet<string> modificationDedup)
        {
            if (stateMachine == null)
            {
                return;
            }

            if (stateMachine.behaviours != null)
            {
                string stateMachineDisplay = BuildStateMachineDisplay(stateMachinePath);
                for (int i = 0; i < stateMachine.behaviours.Length; i++)
                {
                    AnalyzeBehaviour(
                        stateMachine.behaviours[i],
                        layerName,
                        stateMachineDisplay,
                        parameterName,
                        result,
                        referenceDedup,
                        modificationDedup);
                }
            }

            var states = stateMachine.states;
            for (int i = 0; i < states.Length; i++)
            {
                var state = states[i].state;
                if (state == null)
                {
                    continue;
                }

                string sourceState = BuildStatePath(stateMachinePath, state.name);
                AnalyzeState(
                    state,
                    layerName,
                    sourceState,
                    parameterName,
                    result,
                    referenceDedup,
                    modificationDedup);

                var transitions = state.transitions;
                for (int t = 0; t < transitions.Length; t++)
                {
                    var transition = transitions[t];
                    string destinationState = ResolveDestinationState(transition, stateMachinePath);
                    AnalyzeTransition(
                        transition,
                        layerName,
                        sourceState,
                        destinationState,
                        parameterName,
                        result,
                        referenceDedup);
                }
            }

            var anyStateTransitions = stateMachine.anyStateTransitions;
            for (int i = 0; i < anyStateTransitions.Length; i++)
            {
                var transition = anyStateTransitions[i];
                string sourceState = BuildStatePath(stateMachinePath, "AnyState");
                string destinationState = ResolveDestinationState(transition, stateMachinePath);
                AnalyzeTransition(
                    transition,
                    layerName,
                    sourceState,
                    destinationState,
                    parameterName,
                    result,
                    referenceDedup);
            }

            var entryTransitions = stateMachine.entryTransitions;
            for (int i = 0; i < entryTransitions.Length; i++)
            {
                var transition = entryTransitions[i];
                string sourceState = BuildStatePath(stateMachinePath, "Entry");
                string destinationState = ResolveDestinationState(transition, stateMachinePath);
                AnalyzeTransition(
                    transition,
                    layerName,
                    sourceState,
                    destinationState,
                    parameterName,
                    result,
                    referenceDedup);
            }

            var subMachines = stateMachine.stateMachines;
            for (int i = 0; i < subMachines.Length; i++)
            {
                var subMachine = subMachines[i].stateMachine;
                if (subMachine == null)
                {
                    continue;
                }

                string subStateMachinePath = BuildStatePath(stateMachinePath, subMachine.name);
                AnalyzeStateMachine(
                    subMachine,
                    layerName,
                    subStateMachinePath,
                    parameterName,
                    result,
                    referenceDedup,
                    modificationDedup);
            }
        }

        private static string BuildStatePath(string stateMachinePath, string stateName)
        {
            string safeStateName = string.IsNullOrEmpty(stateName) ? "(Unnamed State)" : stateName;
            if (string.IsNullOrEmpty(stateMachinePath))
            {
                return safeStateName;
            }

            return $"{stateMachinePath} > {safeStateName}";
        }

        private static string BuildStateMachineDisplay(string stateMachinePath)
        {
            if (string.IsNullOrEmpty(stateMachinePath))
            {
                return "(Root StateMachine)";
            }

            return stateMachinePath + " > (StateMachine)";
        }

        private static string ResolveDestinationState(AnimatorTransitionBase transition, string stateMachinePath)
        {
            if (transition == null)
            {
                return "Exit";
            }

            if (transition.destinationState != null)
            {
                return BuildStatePath(stateMachinePath, transition.destinationState.name);
            }

            if (transition is AnimatorStateTransition stateTransition && stateTransition.destinationStateMachine != null)
            {
                return BuildStatePath(stateMachinePath, stateTransition.destinationStateMachine.name);
            }

            return "Exit";
        }

        private static void AnalyzeState(
            AnimatorState state,
            string layerName,
            string sourceState,
            string parameterName,
            TraceResult result,
            HashSet<string> referenceDedup,
            HashSet<string> modificationDedup)
        {
            if (state == null)
            {
                return;
            }

            if (state.speedParameterActive && string.Equals(state.speedParameter, parameterName, StringComparison.Ordinal))
            {
                AddReference(
                    result,
                    referenceDedup,
                    TraceSourceKind.StateParameter,
                    "State Speed 参数",
                    state,
                    layerName: layerName,
                    sourceState: sourceState,
                    propertyPath: "speedParameter");
            }

            if (state.mirrorParameterActive && string.Equals(state.mirrorParameter, parameterName, StringComparison.Ordinal))
            {
                AddReference(
                    result,
                    referenceDedup,
                    TraceSourceKind.StateParameter,
                    "State Mirror 参数",
                    state,
                    layerName: layerName,
                    sourceState: sourceState,
                    propertyPath: "mirrorParameter");
            }

            if (state.cycleOffsetParameterActive && string.Equals(state.cycleOffsetParameter, parameterName, StringComparison.Ordinal))
            {
                AddReference(
                    result,
                    referenceDedup,
                    TraceSourceKind.StateParameter,
                    "State Cycle Offset 参数",
                    state,
                    layerName: layerName,
                    sourceState: sourceState,
                    propertyPath: "cycleOffsetParameter");
            }

            if (state.timeParameterActive && string.Equals(state.timeParameter, parameterName, StringComparison.Ordinal))
            {
                AddReference(
                    result,
                    referenceDedup,
                    TraceSourceKind.StateParameter,
                    "State Time 参数",
                    state,
                    layerName: layerName,
                    sourceState: sourceState,
                    propertyPath: "timeParameter");
            }

            if (state.motion is UnityEditor.Animations.BlendTree blendTree)
            {
                AnalyzeBlendTree(
                    blendTree,
                    layerName,
                    sourceState,
                    blendTree.name,
                    parameterName,
                    result,
                    referenceDedup);
            }

            var behaviours = state.behaviours;
            if (behaviours == null)
            {
                return;
            }

            for (int i = 0; i < behaviours.Length; i++)
            {
                AnalyzeBehaviour(
                    behaviours[i],
                    layerName,
                    sourceState,
                    parameterName,
                    result,
                    referenceDedup,
                    modificationDedup);
            }
        }

        private static void AnalyzeBlendTree(
            UnityEditor.Animations.BlendTree blendTree,
            string layerName,
            string sourceState,
            string blendTreePath,
            string parameterName,
            TraceResult result,
            HashSet<string> referenceDedup)
        {
            if (blendTree == null)
            {
                return;
            }

            string currentBlendTreePath = string.IsNullOrEmpty(blendTreePath) ? blendTree.name : blendTreePath;

            if (blendTree.blendType != BlendTreeType.Direct &&
                !string.IsNullOrEmpty(blendTree.blendParameter) &&
                string.Equals(blendTree.blendParameter, parameterName, StringComparison.Ordinal))
            {
                AddReference(
                    result,
                    referenceDedup,
                    TraceSourceKind.BlendTreeParameter,
                    "BlendTree 参数 (X/1D)",
                    blendTree,
                    layerName: layerName,
                    sourceState: sourceState,
                    blendTreePath: currentBlendTreePath,
                    propertyPath: "blendParameter");
            }

            if (blendTree.blendType != BlendTreeType.Simple1D &&
                blendTree.blendType != BlendTreeType.Direct &&
                !string.IsNullOrEmpty(blendTree.blendParameterY) &&
                string.Equals(blendTree.blendParameterY, parameterName, StringComparison.Ordinal))
            {
                AddReference(
                    result,
                    referenceDedup,
                    TraceSourceKind.BlendTreeParameter,
                    "BlendTree 参数 (Y)",
                    blendTree,
                    layerName: layerName,
                    sourceState: sourceState,
                    blendTreePath: currentBlendTreePath,
                    propertyPath: "blendParameterY");
            }

            var children = blendTree.children;
            for (int i = 0; i < children.Length; i++)
            {
                var child = children[i];

                if (blendTree.blendType == BlendTreeType.Direct &&
                    !string.IsNullOrEmpty(child.directBlendParameter) &&
                    string.Equals(child.directBlendParameter, parameterName, StringComparison.Ordinal))
                {
                    AddReference(
                        result,
                        referenceDedup,
                        TraceSourceKind.BlendTreeParameter,
                        "Direct BlendTree 子项参数",
                        blendTree,
                        layerName: layerName,
                        sourceState: sourceState,
                        blendTreePath: currentBlendTreePath,
                        propertyPath: $"children[{i}].directBlendParameter");
                }

                if (child.motion is UnityEditor.Animations.BlendTree childTree)
                {
                    string childBlendTreePath = currentBlendTreePath + " > " + childTree.name;
                    AnalyzeBlendTree(
                        childTree,
                        layerName,
                        sourceState,
                        childBlendTreePath,
                        parameterName,
                        result,
                        referenceDedup);
                }
            }
        }

        private static void AnalyzeTransition(
            AnimatorTransitionBase transition,
            string layerName,
            string sourceState,
            string destinationState,
            string parameterName,
            TraceResult result,
            HashSet<string> referenceDedup)
        {
            if (transition == null)
            {
                return;
            }

            var conditions = transition.conditions;
            for (int i = 0; i < conditions.Length; i++)
            {
                var condition = conditions[i];
                if (!string.Equals(condition.parameter, parameterName, StringComparison.Ordinal))
                {
                    continue;
                }

                AddReference(
                    result,
                    referenceDedup,
                    TraceSourceKind.TransitionCondition,
                    "Transition Condition 参数",
                    transition,
                    layerName: layerName,
                    sourceState: sourceState,
                    destinationState: destinationState,
                    propertyPath: $"conditions[{i}] mode={condition.mode} threshold={condition.threshold}");
            }
        }

        private static void AnalyzeBehaviour(
            StateMachineBehaviour behaviour,
            string layerName,
            string sourceState,
            string parameterName,
            TraceResult result,
            HashSet<string> referenceDedup,
            HashSet<string> modificationDedup)
        {
            if (!IsAvatarParameterDriverBehaviour(behaviour))
            {
                return;
            }

            SerializedObject serializedObject;
            try
            {
                serializedObject = new SerializedObject(behaviour);
            }
            catch
            {
                return;
            }

            var parametersProp = serializedObject.FindProperty("parameters");
            if (parametersProp == null || !parametersProp.isArray)
            {
                return;
            }

            for (int i = 0; i < parametersProp.arraySize; i++)
            {
                var element = parametersProp.GetArrayElementAtIndex(i);
                var nameProp = element.FindPropertyRelative("name");
                if (nameProp != null && string.Equals(nameProp.stringValue, parameterName, StringComparison.Ordinal))
                {
                    AddModification(
                        result,
                        modificationDedup,
                        TraceSourceKind.AvatarParameterDriverTarget,
                        "Avatar Parameter Driver 写入参数",
                        behaviour,
                        layerName: layerName,
                        sourceState: sourceState,
                        componentName: behaviour.GetType().Name,
                        propertyPath: $"parameters[{i}].name");
                }

                var sourceProp = element.FindPropertyRelative("source");
                if (sourceProp != null && string.Equals(sourceProp.stringValue, parameterName, StringComparison.Ordinal))
                {
                    AddReference(
                        result,
                        referenceDedup,
                        TraceSourceKind.AvatarParameterDriverSource,
                        "Avatar Parameter Driver 读取参数",
                        behaviour,
                        layerName: layerName,
                        sourceState: sourceState,
                        componentName: behaviour.GetType().Name,
                        propertyPath: $"parameters[{i}].source");
                }
            }
        }

        private static bool IsAvatarParameterDriverBehaviour(StateMachineBehaviour behaviour)
        {
            if (behaviour == null)
            {
                return false;
            }

            var typeName = behaviour.GetType().Name;
            return !string.IsNullOrEmpty(typeName) &&
                   typeName.IndexOf("AvatarParameterDriver", StringComparison.Ordinal) >= 0;
        }

        private static void RegisterAnimationCurveModifications(
            AnimatorController controller,
            string parameterName,
            TraceResult result,
            HashSet<string> modificationDedup)
        {
            var clips = new HashSet<AnimationClip>();
            var layers = controller.layers;
            if (layers == null || layers.Length == 0)
            {
                return;
            }

            for (int i = 0; i < layers.Length; i++)
            {
                CollectClipsFromStateMachine(layers[i].stateMachine, clips);
            }

            foreach (var clip in clips)
            {
                if (clip == null)
                {
                    continue;
                }

                var bindings = AnimationUtility.GetCurveBindings(clip);
                bool matched = false;
                for (int i = 0; i < bindings.Length; i++)
                {
                    var binding = bindings[i];
                    if (binding.type != typeof(Animator))
                    {
                        continue;
                    }

                    if (!TryExtractAnimatorParameterName(binding.propertyName, out var curveParameter))
                    {
                        continue;
                    }

                    if (!string.Equals(curveParameter, parameterName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    matched = true;
                    break;
                }

                if (matched)
                {
                    AddModification(
                        result,
                        modificationDedup,
                        TraceSourceKind.AnimationCurve,
                        "动画剪辑写入参数",
                        clip);
                }
            }
        }

        private static void CollectClipsFromStateMachine(AnimatorStateMachine stateMachine, HashSet<AnimationClip> clips)
        {
            if (stateMachine == null)
            {
                return;
            }

            var states = stateMachine.states;
            for (int i = 0; i < states.Length; i++)
            {
                var state = states[i].state;
                if (state == null)
                {
                    continue;
                }

                CollectClipsFromMotion(state.motion, clips);
            }

            var subMachines = stateMachine.stateMachines;
            for (int i = 0; i < subMachines.Length; i++)
            {
                CollectClipsFromStateMachine(subMachines[i].stateMachine, clips);
            }
        }

        private static void CollectClipsFromMotion(Motion motion, HashSet<AnimationClip> clips)
        {
            if (motion == null)
            {
                return;
            }

            if (motion is AnimationClip clip)
            {
                clips.Add(clip);
                return;
            }

            if (motion is UnityEditor.Animations.BlendTree blendTree)
            {
                var children = blendTree.children;
                for (int i = 0; i < children.Length; i++)
                {
                    CollectClipsFromMotion(children[i].motion, clips);
                }
            }
        }

        private static bool TryExtractAnimatorParameterName(string propertyName, out string parameterName)
        {
            parameterName = null;
            if (string.IsNullOrEmpty(propertyName))
            {
                return false;
            }

            const string parametersPrefix = "Parameters.";
            if (propertyName.StartsWith(parametersPrefix, StringComparison.Ordinal))
            {
                var candidate = propertyName.Substring(parametersPrefix.Length);
                if (!string.IsNullOrEmpty(candidate))
                {
                    parameterName = candidate;
                    return true;
                }
            }

            const string parameterPrefix = "parameter.";
            if (propertyName.StartsWith(parameterPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var candidate = propertyName.Substring(parameterPrefix.Length);
                if (!string.IsNullOrEmpty(candidate))
                {
                    parameterName = candidate;
                    return true;
                }
            }

            parameterName = propertyName;
            return true;
        }

        private static void RegisterSceneComponentModifications(
            GameObject sceneRoot,
            string parameterName,
            TraceResult result,
            HashSet<string> modificationDedup)
        {
            if (sceneRoot == null)
            {
                return;
            }

            var components = sceneRoot.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null)
                {
                    continue;
                }

                var fullTypeName = component.GetType().FullName ?? component.GetType().Name;
                bool isPhysBone = IsPhysBoneComponent(fullTypeName);
                bool isKnownWriter = IsKnownSceneWriterComponent(fullTypeName);

                if (isPhysBone && TryMatchPhysBoneWrite(component, parameterName, out var suffix))
                {
                    AddModification(
                        result,
                        modificationDedup,
                        TraceSourceKind.SceneComponent,
                        "场景组件写入参数",
                        component,
                        hierarchyPath: GetRelativeHierarchyPath(sceneRoot.transform, component.transform),
                        componentName: component.GetType().Name,
                        propertyPath: "parameter -> " + parameterName + " (" + suffix + ")");
                }

                if (isPhysBone)
                {
                    continue;
                }

                if (isKnownWriter)
                {
                    if (TryRegisterKnownSceneWriterModification(
                        component,
                        sceneRoot,
                        parameterName,
                        result,
                        modificationDedup))
                    {
                        continue;
                    }
                }

                SerializedObject serializedObject;
                try
                {
                    serializedObject = new SerializedObject(component);
                }
                catch
                {
                    continue;
                }

                var iterator = serializedObject.GetIterator();
                bool enterChildren = true;
                while (iterator.NextVisible(enterChildren))
                {
                    enterChildren = false;

                    if (iterator.propertyType != SerializedPropertyType.String)
                    {
                        continue;
                    }

                    var value = iterator.stringValue;
                    if (string.IsNullOrEmpty(value) || !string.Equals(value, parameterName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var propertyPath = iterator.propertyPath ?? string.Empty;
                    if (!LooksLikeParameterProperty(propertyPath))
                    {
                        continue;
                    }

                    AddModification(
                        result,
                        modificationDedup,
                        TraceSourceKind.SceneComponent,
                        "场景组件写入参数",
                        component,
                        hierarchyPath: GetRelativeHierarchyPath(sceneRoot.transform, component.transform),
                        componentName: component.GetType().Name,
                        propertyPath: propertyPath);
                }
            }
        }

        private static bool TryMatchPhysBoneWrite(Component component, string parameterName, out string suffix)
        {
            suffix = string.Empty;
            if (component == null)
            {
                return false;
            }

            SerializedObject serializedObject;
            try
            {
                serializedObject = new SerializedObject(component);
            }
            catch
            {
                return false;
            }

            var parameterProp = serializedObject.FindProperty("parameter");
            if (parameterProp == null)
            {
                return false;
            }

            string baseName = parameterProp.stringValue;
            if (string.IsNullOrEmpty(baseName))
            {
                return false;
            }

            for (int i = 0; i < _physBoneSuffixes.Length; i++)
            {
                var currentSuffix = _physBoneSuffixes[i];
                if (string.Equals(baseName + currentSuffix, parameterName, StringComparison.Ordinal))
                {
                    suffix = currentSuffix;
                    return true;
                }
            }

            return false;
        }

        private static bool LooksLikeParameterProperty(string propertyPath)
        {
            return !string.IsNullOrEmpty(propertyPath) &&
                   propertyPath.IndexOf("parameter", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsKnownSceneWriterComponent(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return false;
            }

            if (typeName.IndexOf("ContactReceiver", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (typeName.IndexOf("PhysBone", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return typeName.IndexOf("VRC", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   typeName.IndexOf("Raycast", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsPhysBoneComponent(string typeName)
        {
            return !string.IsNullOrEmpty(typeName) &&
                   typeName.IndexOf("PhysBone", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetRelativeHierarchyPath(Transform root, Transform target)
        {
            if (target == null)
            {
                return string.Empty;
            }

            if (root == null)
            {
                return GetFullHierarchyPath(target);
            }

            var names = new Stack<string>();
            var current = target;
            while (current != null)
            {
                names.Push(current.name);
                if (current == root)
                {
                    break;
                }
                current = current.parent;
            }

            return string.Join("/", names);
        }

        private static string GetFullHierarchyPath(Transform target)
        {
            var names = new Stack<string>();
            var current = target;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names);
        }

        private static void AddReference(
            TraceResult result,
            HashSet<string> dedup,
            TraceSourceKind sourceKind,
            string description,
            UnityEngine.Object relatedObject,
            string layerName = null,
            string sourceState = null,
            string destinationState = null,
            string componentName = null,
            string blendTreePath = null,
            string hierarchyPath = null,
            string propertyPath = null)
        {
            AddEntry(
                result.References,
                dedup,
                sourceKind,
                description,
                relatedObject,
                layerName,
                sourceState,
                destinationState,
                componentName,
                blendTreePath,
                hierarchyPath,
                propertyPath);
        }

        private static void AddModification(
            TraceResult result,
            HashSet<string> dedup,
            TraceSourceKind sourceKind,
            string description,
            UnityEngine.Object relatedObject,
            string layerName = null,
            string sourceState = null,
            string destinationState = null,
            string componentName = null,
            string blendTreePath = null,
            string hierarchyPath = null,
            string propertyPath = null)
        {
            AddEntry(
                result.Modifications,
                dedup,
                sourceKind,
                description,
                relatedObject,
                layerName,
                sourceState,
                destinationState,
                componentName,
                blendTreePath,
                hierarchyPath,
                propertyPath);
        }

        private static void AddEntry(
            List<TraceEntry> targetList,
            HashSet<string> dedup,
            TraceSourceKind sourceKind,
            string description,
            UnityEngine.Object relatedObject,
            string layerName,
            string sourceState,
            string destinationState,
            string componentName,
            string blendTreePath,
            string hierarchyPath,
            string propertyPath)
        {
            string dedupKey =
                $"{sourceKind}|{relatedObject?.GetInstanceID() ?? 0}|{layerName}|{sourceState}|{destinationState}|{componentName}|{blendTreePath}|{hierarchyPath}|{description}";
            if (!dedup.Add(dedupKey))
            {
                return;
            }

            targetList.Add(new TraceEntry
            {
                SourceKind = sourceKind,
                Description = description,
                LayerName = layerName ?? string.Empty,
                SourceState = sourceState ?? string.Empty,
                DestinationState = destinationState ?? string.Empty,
                ComponentName = componentName ?? string.Empty,
                BlendTreePath = blendTreePath ?? string.Empty,
                HierarchyPath = hierarchyPath ?? string.Empty,
                PropertyPath = propertyPath ?? string.Empty,
                RelatedObject = relatedObject
            });
        }
    }
}
