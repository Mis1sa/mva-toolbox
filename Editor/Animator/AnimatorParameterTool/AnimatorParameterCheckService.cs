using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.AnimatorParameterTool
{
    internal static class AnimatorParameterCheckService
    {
        private static readonly AnimatorControllerParameterType[] ExpectedTypePriority =
        {
            AnimatorControllerParameterType.Bool,
            AnimatorControllerParameterType.Int,
            AnimatorControllerParameterType.Float,
            AnimatorControllerParameterType.Trigger
        };

        internal enum IssueType
        {
            MissingReference,
            UnusedParameter,
            TypeMismatch,
            BrokenPPtr
        }

        private sealed class BrokenPPtrLocation
        {
            public int LineIndex;
            public int MatchIndex;
            public int MatchLength;
            public string RawToken;
            public string PropertyLabel;
        }

        private static readonly Regex YamlDocumentHeaderRegex = new Regex(
            @"^\s*---\s*!u!\d+\s*&(-?\d+)\s*$",
            RegexOptions.Compiled);

        private static readonly Regex YamlLocalPPtrRegex = new Regex(
            @"\{fileID:\s*(-?\d+)\s*\}",
            RegexOptions.Compiled);

        internal sealed class ParameterReferenceContext
        {
            public string Description;
            public Action<string> FixAction;
            public AnimatorTransitionBase Transition;
            public int ConditionIndex = -1;
            public AnimatorConditionMode? ConditionMode;
            public float ConditionThreshold;
        }

        internal sealed class ParameterIssue
        {
            public IssueType Type;
            public string ParameterName;
            public AnimatorControllerParameterType? ExpectedType;
            public AnimatorControllerParameterType? ActualType;
            public List<ParameterReferenceContext> References = new List<ParameterReferenceContext>();
        }

        private delegate void ParameterReferenceRegister(
            string paramName,
            string description,
            AnimatorControllerParameterType? typeHint,
            Action<string> fixAction,
            AnimatorTransitionBase transition = null,
            int conditionIndex = -1,
            AnimatorConditionMode? conditionMode = null,
            float conditionThreshold = 0f);

        internal sealed class CheckResult
        {
            public List<ParameterIssue> Issues = new List<ParameterIssue>();
            public bool HasIssues => Issues.Count > 0;
        }

        internal static CheckResult Execute(AnimatorController controller)
        {
            var result = new CheckResult();
            if (controller == null)
            {
                return result;
            }

            Dictionary<string, AnimatorControllerParameter> definedParameters = controller.parameters.ToDictionary(p => p.name, p => p);
            var definedParameterNames = new HashSet<string>(definedParameters.Keys, StringComparer.Ordinal);
            var usedParameterNames = new HashSet<string>();
            var typeHintMap = new Dictionary<string, HashSet<AnimatorControllerParameterType>>();
            var referenceMap = new Dictionary<string, List<ParameterReferenceContext>>(StringComparer.Ordinal);

            void RegisterReference(
                string paramName,
                string description,
                AnimatorControllerParameterType? typeHint,
                Action<string> fixAction,
                AnimatorTransitionBase transition = null,
                int conditionIndex = -1,
                AnimatorConditionMode? conditionMode = null,
                float conditionThreshold = 0f)
            {
                if (string.IsNullOrEmpty(paramName))
                {
                    return;
                }

                usedParameterNames.Add(paramName);
                var context = new ParameterReferenceContext
                {
                    Description = description,
                    FixAction = fixAction,
                    Transition = transition,
                    ConditionIndex = conditionIndex,
                    ConditionMode = conditionMode,
                    ConditionThreshold = conditionThreshold
                };

                if (!referenceMap.TryGetValue(paramName, out List<ParameterReferenceContext> referenceList))
                {
                    referenceList = new List<ParameterReferenceContext>();
                    referenceMap[paramName] = referenceList;
                }

                referenceList.Add(context);

                if (typeHint.HasValue)
                {
                    if (!typeHintMap.TryGetValue(paramName, out HashSet<AnimatorControllerParameterType> hints))
                    {
                        hints = new HashSet<AnimatorControllerParameterType>();
                        typeHintMap[paramName] = hints;
                    }

                    hints.Add(typeHint.Value);
                }

                if (definedParameters.ContainsKey(paramName))
                {
                    return;
                }

                ParameterIssue issue = result.Issues.FirstOrDefault(i => i.Type == IssueType.MissingReference && i.ParameterName == paramName);
                if (issue == null)
                {
                    issue = new ParameterIssue
                    {
                        Type = IssueType.MissingReference,
                        ParameterName = paramName,
                        ExpectedType = typeHint
                    };
                    result.Issues.Add(issue);
                }

                if (!issue.ExpectedType.HasValue && typeHint.HasValue)
                {
                    issue.ExpectedType = typeHint;
                }

                issue.References.Add(context);
            }

            for (int i = 0; i < controller.layers.Length; i++)
            {
                AnimatorControllerLayer layer = controller.layers[i];
                AnalyzeStateMachine(layer.stateMachine, $"Layer {i}", RegisterReference);
            }

            RegisterAnimationCurveParameterReferences(
                controller,
                definedParameterNames,
                usedParameterNames,
                typeHintMap,
                referenceMap);

            ParameterIssue brokenPPtrIssue = BuildBrokenPPtrIssue(controller);
            if (brokenPPtrIssue != null)
            {
                result.Issues.Add(brokenPPtrIssue);
            }

            AnimatorControllerParameter[] controllerParameters = controller.parameters;
            for (int i = 0; i < controllerParameters.Length; i++)
            {
                AnimatorControllerParameter param = controllerParameters[i];
                if (!usedParameterNames.Contains(param.name))
                {
                    result.Issues.Add(new ParameterIssue
                    {
                        Type = IssueType.UnusedParameter,
                        ParameterName = param.name,
                        ActualType = param.type
                    });
                }
            }

            for (int i = 0; i < controllerParameters.Length; i++)
            {
                AnimatorControllerParameter param = controllerParameters[i];
                if (!typeHintMap.TryGetValue(param.name, out HashSet<AnimatorControllerParameterType> hints) || hints.Count == 0)
                {
                    continue;
                }

                if (hints.Contains(param.type))
                {
                    continue;
                }

                var issue = new ParameterIssue
                {
                    Type = IssueType.TypeMismatch,
                    ParameterName = param.name,
                    ExpectedType = SelectExpectedType(hints),
                    ActualType = param.type
                };

                if (referenceMap.TryGetValue(param.name, out List<ParameterReferenceContext> references))
                {
                    issue.References.AddRange(references);
                }

                result.Issues.Add(issue);
            }

            result.Issues = result.Issues
                .OrderBy(i => i.Type)
                .ThenBy(i => i.ParameterName)
                .ToList();

            return result;
        }

        private static AnimatorControllerParameterType SelectExpectedType(HashSet<AnimatorControllerParameterType> hints)
        {
            if (hints == null || hints.Count == 0)
            {
                return AnimatorControllerParameterType.Float;
            }

            for (int i = 0; i < ExpectedTypePriority.Length; i++)
            {
                AnimatorControllerParameterType candidate = ExpectedTypePriority[i];
                if (hints.Contains(candidate))
                {
                    return candidate;
                }
            }

            return hints.OrderBy(x => (int)x).First();
        }

        private static ParameterIssue BuildBrokenPPtrIssue(AnimatorController controller)
        {
            if (!TryCollectBrokenPPtrs(controller, out string assetPath, out _, out string[] _, out List<BrokenPPtrLocation> brokenLocations))
            {
                return null;
            }

            if (brokenLocations.Count == 0)
            {
                return null;
            }

            var issue = new ParameterIssue
            {
                Type = IssueType.BrokenPPtr,
                ParameterName = Path.GetFileName(assetPath)
            };

            for (int i = 0; i < brokenLocations.Count; i++)
            {
                BrokenPPtrLocation location = brokenLocations[i];
                issue.References.Add(new ParameterReferenceContext
                {
                    Description = $"第 {location.LineIndex + 1} 行 {location.PropertyLabel}: {location.RawToken}"
                });
            }

            return issue;
        }

        internal static bool FixBrokenPPtrs(AnimatorController controller)
        {
            if (!TryCollectBrokenPPtrs(controller, out string assetPath, out string absolutePath, out string[] lines, out List<BrokenPPtrLocation> brokenLocations))
            {
                return false;
            }

            if (brokenLocations.Count == 0)
            {
                return false;
            }

            IEnumerable<IGrouping<int, BrokenPPtrLocation>> groupedByLine = brokenLocations.GroupBy(x => x.LineIndex);
            foreach (IGrouping<int, BrokenPPtrLocation> group in groupedByLine)
            {
                int lineIndex = group.Key;
                if (lineIndex < 0 || lineIndex >= lines.Length)
                {
                    continue;
                }

                string line = lines[lineIndex];
                foreach (BrokenPPtrLocation location in group.OrderByDescending(x => x.MatchIndex))
                {
                    if (location.MatchIndex < 0 ||
                        location.MatchLength <= 0 ||
                        location.MatchIndex + location.MatchLength > line.Length)
                    {
                        continue;
                    }

                    line = line.Remove(location.MatchIndex, location.MatchLength)
                        .Insert(location.MatchIndex, "{fileID: 0}");
                }

                lines[lineIndex] = line;
            }

            try
            {
                File.WriteAllLines(absolutePath, lines, new UTF8Encoding(false));
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.SaveAssets();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryCollectBrokenPPtrs(
            AnimatorController controller,
            out string assetPath,
            out string absolutePath,
            out string[] lines,
            out List<BrokenPPtrLocation> brokenLocations)
        {
            assetPath = string.Empty;
            absolutePath = string.Empty;
            lines = Array.Empty<string>();
            brokenLocations = new List<BrokenPPtrLocation>();

            if (controller == null)
            {
                return false;
            }

            assetPath = AssetDatabase.GetAssetPath(controller);
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            absolutePath = GetAbsoluteAssetPath(assetPath);
            if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath))
            {
                return false;
            }

            try
            {
                lines = File.ReadAllLines(absolutePath);
            }
            catch
            {
                return false;
            }

            if (lines.Length == 0 || lines[0].IndexOf("%YAML", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return true;
            }

            HashSet<long> definedIds = new HashSet<long>();
            for (int i = 0; i < lines.Length; i++)
            {
                Match headerMatch = YamlDocumentHeaderRegex.Match(lines[i]);
                if (headerMatch.Success && long.TryParse(headerMatch.Groups[1].Value, out long localId))
                {
                    definedIds.Add(localId);
                }
            }

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                MatchCollection matches = YamlLocalPPtrRegex.Matches(line);
                foreach (Match match in matches)
                {
                    if (!match.Success)
                    {
                        continue;
                    }

                    if (!long.TryParse(match.Groups[1].Value, out long fileId) || fileId == 0)
                    {
                        continue;
                    }

                    if (definedIds.Contains(fileId))
                    {
                        continue;
                    }

                    brokenLocations.Add(new BrokenPPtrLocation
                    {
                        LineIndex = i,
                        MatchIndex = match.Index,
                        MatchLength = match.Length,
                        RawToken = match.Value,
                        PropertyLabel = TryExtractPropertyLabel(line, match.Index)
                    });
                }
            }

            return true;
        }

        private static string TryExtractPropertyLabel(string line, int matchIndex)
        {
            if (string.IsNullOrEmpty(line))
            {
                return "PPtr";
            }

            int keySeparator = line.IndexOf(':');
            if (keySeparator > 0 && keySeparator < matchIndex)
            {
                string key = line.Substring(0, keySeparator).Trim();
                if (!string.IsNullOrEmpty(key))
                {
                    return key;
                }
            }

            return "PPtr";
        }

        private static string GetAbsoluteAssetPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot))
            {
                return null;
            }

            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        private static void AnalyzeStateMachine(AnimatorStateMachine stateMachine, string path, ParameterReferenceRegister register)
        {
            if (stateMachine == null)
            {
                return;
            }

            StateMachineBehaviour[] stateMachineBehaviours = stateMachine.behaviours;
            if (stateMachineBehaviours != null)
            {
                for (int i = 0; i < stateMachineBehaviours.Length; i++)
                {
                    AnalyzeBehaviour(stateMachineBehaviours[i], $"{path} (StateMachine Driver {i})", register);
                }
            }

            ChildAnimatorState[] states = stateMachine.states;
            for (int i = 0; i < states.Length; i++)
            {
                AnimatorState state = states[i].state;
                if (state == null)
                {
                    continue;
                }

                string statePath = $"{path}/{state.name}";
                AnalyzeState(state, statePath, register);

                AnimatorStateTransition[] transitions = state.transitions;
                for (int t = 0; t < transitions.Length; t++)
                {
                    AnalyzeTransition(transitions[t], $"{statePath} -> {(transitions[t].destinationState?.name ?? "Exit")}", register);
                }
            }

            AnimatorStateTransition[] anyStateTransitions = stateMachine.anyStateTransitions;
            for (int i = 0; i < anyStateTransitions.Length; i++)
            {
                AnalyzeTransition(anyStateTransitions[i], $"{path}/AnyState -> {(anyStateTransitions[i].destinationState?.name ?? "Exit")}", register);
            }

            AnimatorTransition[] entryTransitions = stateMachine.entryTransitions;
            for (int i = 0; i < entryTransitions.Length; i++)
            {
                AnalyzeTransition(entryTransitions[i], $"{path}/Entry -> {(entryTransitions[i].destinationState?.name ?? "Exit")}", register);
            }

            ChildAnimatorStateMachine[] subMachines = stateMachine.stateMachines;
            for (int i = 0; i < subMachines.Length; i++)
            {
                AnimatorStateMachine subStateMachine = subMachines[i].stateMachine;
                if (subStateMachine != null)
                {
                    AnalyzeStateMachine(subStateMachine, $"{path}/{subStateMachine.name}", register);
                }
            }
        }

        private static void RegisterAnimationCurveParameterReferences(
            AnimatorController controller,
            HashSet<string> definedParameterNames,
            HashSet<string> usedParameterNames,
            Dictionary<string, HashSet<AnimatorControllerParameterType>> typeHintMap,
            Dictionary<string, List<ParameterReferenceContext>> referenceMap)
        {
            if (controller == null)
            {
                return;
            }

            var clips = new HashSet<AnimationClip>();
            for (int i = 0; i < controller.layers.Length; i++)
            {
                AnimatorControllerLayer layer = controller.layers[i];
                if (layer?.stateMachine == null)
                {
                    continue;
                }

                CollectClipsFromStateMachine(layer.stateMachine, clips);
            }

            foreach (AnimationClip clip in clips)
            {
                if (clip == null)
                {
                    continue;
                }

                EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
                for (int i = 0; i < bindings.Length; i++)
                {
                    EditorCurveBinding binding = bindings[i];
                    if (binding.type != typeof(Animator))
                    {
                        continue;
                    }

                    if (!TryExtractAnimatorParameterName(binding.propertyName, definedParameterNames, out string parameterName))
                    {
                        continue;
                    }

                    usedParameterNames.Add(parameterName);
                    if (!typeHintMap.TryGetValue(parameterName, out HashSet<AnimatorControllerParameterType> hints))
                    {
                        hints = new HashSet<AnimatorControllerParameterType>();
                        typeHintMap[parameterName] = hints;
                    }

                    hints.Add(AnimatorControllerParameterType.Float);
                    if (!referenceMap.TryGetValue(parameterName, out List<ParameterReferenceContext> references))
                    {
                        references = new List<ParameterReferenceContext>();
                        referenceMap[parameterName] = references;
                    }

                    references.Add(new ParameterReferenceContext
                    {
                        Description = $"动画曲线: {clip.name} ({binding.path}/{binding.propertyName})"
                    });
                }
            }
        }

        private static void CollectClipsFromStateMachine(AnimatorStateMachine stateMachine, HashSet<AnimationClip> clips)
        {
            if (stateMachine == null || clips == null)
            {
                return;
            }

            ChildAnimatorState[] states = stateMachine.states;
            for (int i = 0; i < states.Length; i++)
            {
                AnimatorState state = states[i].state;
                if (state == null)
                {
                    continue;
                }

                CollectClipsFromMotion(state.motion, clips);
            }

            ChildAnimatorStateMachine[] subMachines = stateMachine.stateMachines;
            for (int i = 0; i < subMachines.Length; i++)
            {
                AnimatorStateMachine subStateMachine = subMachines[i].stateMachine;
                if (subStateMachine != null)
                {
                    CollectClipsFromStateMachine(subStateMachine, clips);
                }
            }
        }

        private static void CollectClipsFromMotion(Motion motion, HashSet<AnimationClip> clips)
        {
            if (motion == null || clips == null)
            {
                return;
            }

            if (motion is AnimationClip clip)
            {
                clips.Add(clip);
                return;
            }

            if (motion is BlendTree blendTree)
            {
                ChildMotion[] children = blendTree.children;
                for (int i = 0; i < children.Length; i++)
                {
                    CollectClipsFromMotion(children[i].motion, clips);
                }
            }
        }

        private static bool TryExtractAnimatorParameterName(
            string propertyName,
            HashSet<string> definedParameterNames,
            out string parameterName)
        {
            parameterName = null;
            if (string.IsNullOrEmpty(propertyName))
            {
                return false;
            }

            if (definedParameterNames != null && definedParameterNames.Contains(propertyName))
            {
                parameterName = propertyName;
                return true;
            }

            const string parametersPrefix = "Parameters.";
            if (propertyName.StartsWith(parametersPrefix, StringComparison.Ordinal))
            {
                string candidate = propertyName.Substring(parametersPrefix.Length);
                if (!string.IsNullOrEmpty(candidate))
                {
                    parameterName = candidate;
                    return true;
                }
            }

            const string parameterPrefix = "parameter.";
            if (propertyName.StartsWith(parameterPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string candidate = propertyName.Substring(parameterPrefix.Length);
                if (!string.IsNullOrEmpty(candidate))
                {
                    parameterName = candidate;
                    return true;
                }
            }

            return false;
        }

        private static void AnalyzeState(AnimatorState state, string path, ParameterReferenceRegister register)
        {
            if (state.speedParameterActive)
            {
                register(state.speedParameter, $"{path} (Speed)", AnimatorControllerParameterType.Float, newName =>
                {
                    state.speedParameter = newName;
                    EditorUtility.SetDirty(state);
                });
            }

            if (state.mirrorParameterActive)
            {
                register(state.mirrorParameter, $"{path} (Mirror)", AnimatorControllerParameterType.Bool, newName =>
                {
                    state.mirrorParameter = newName;
                    EditorUtility.SetDirty(state);
                });
            }

            if (state.cycleOffsetParameterActive)
            {
                register(state.cycleOffsetParameter, $"{path} (Cycle Offset)", AnimatorControllerParameterType.Float, newName =>
                {
                    state.cycleOffsetParameter = newName;
                    EditorUtility.SetDirty(state);
                });
            }

            if (state.timeParameterActive)
            {
                register(state.timeParameter, $"{path} (Time)", AnimatorControllerParameterType.Float, newName =>
                {
                    state.timeParameter = newName;
                    EditorUtility.SetDirty(state);
                });
            }

            if (state.motion is BlendTree blendTree)
            {
                AnalyzeBlendTree(blendTree, path, register);
            }

            StateMachineBehaviour[] behaviours = state.behaviours;
            for (int i = 0; i < behaviours.Length; i++)
            {
                AnalyzeBehaviour(behaviours[i], path, register);
            }
        }

        private static void AnalyzeBlendTree(BlendTree blendTree, string path, ParameterReferenceRegister register)
        {
            if (blendTree == null)
            {
                return;
            }

            string blendTreePath = $"{path}/{blendTree.name}";
            if (blendTree.blendType != BlendTreeType.Simple1D)
            {
                if (blendTree.blendType != BlendTreeType.Direct)
                {
                    register(blendTree.blendParameter, $"{blendTreePath} (Blend X)", AnimatorControllerParameterType.Float, newName =>
                    {
                        blendTree.blendParameter = newName;
                        EditorUtility.SetDirty(blendTree);
                    });
                    register(blendTree.blendParameterY, $"{blendTreePath} (Blend Y)", AnimatorControllerParameterType.Float, newName =>
                    {
                        blendTree.blendParameterY = newName;
                        EditorUtility.SetDirty(blendTree);
                    });
                }
            }
            else
            {
                register(blendTree.blendParameter, $"{blendTreePath} (Blend)", AnimatorControllerParameterType.Float, newName =>
                {
                    blendTree.blendParameter = newName;
                    EditorUtility.SetDirty(blendTree);
                });
            }

            ChildMotion[] children = blendTree.children;
            for (int i = 0; i < children.Length; i++)
            {
                ChildMotion child = children[i];
                if (child.motion is BlendTree childBlendTree)
                {
                    AnalyzeBlendTree(childBlendTree, blendTreePath, register);
                }

                if (blendTree.blendType != BlendTreeType.Direct)
                {
                    continue;
                }

                int childIndex = i;
                register(child.directBlendParameter, $"{blendTreePath} (Direct Child)", AnimatorControllerParameterType.Float, newName =>
                {
                    ChildMotion[] freshChildren = blendTree.children;
                    if (childIndex < 0 || childIndex >= freshChildren.Length)
                    {
                        return;
                    }

                    ChildMotion c = freshChildren[childIndex];
                    c.directBlendParameter = newName;
                    freshChildren[childIndex] = c;
                    blendTree.children = freshChildren;
                    EditorUtility.SetDirty(blendTree);
                });
            }
        }

        private static void AnalyzeTransition(AnimatorTransitionBase transition, string path, ParameterReferenceRegister register)
        {
            AnimatorCondition[] conditions = transition.conditions;
            for (int i = 0; i < conditions.Length; i++)
            {
                AnimatorCondition cond = conditions[i];
                int index = i;
                AnimatorControllerParameterType? typeHint = null;
                switch (cond.mode)
                {
                    case AnimatorConditionMode.If:
                    case AnimatorConditionMode.IfNot:
                        typeHint = AnimatorControllerParameterType.Bool;
                        break;
                    case AnimatorConditionMode.Equals:
                    case AnimatorConditionMode.NotEqual:
                        typeHint = AnimatorControllerParameterType.Int;
                        break;
                }

                register(cond.parameter, $"{path} (Condition {index})", typeHint, newName =>
                {
                    AnimatorCondition[] currentConditions = transition.conditions;
                    if (index < currentConditions.Length)
                    {
                        currentConditions[index].parameter = newName;
                        transition.conditions = currentConditions;
                        EditorUtility.SetDirty(transition);
                    }
                }, transition, index, cond.mode, cond.threshold);
            }
        }

        private static void AnalyzeBehaviour(StateMachineBehaviour behaviour, string path, ParameterReferenceRegister register)
        {
            if (!IsAvatarParameterDriverBehaviour(behaviour))
            {
                return;
            }

            var so = new SerializedObject(behaviour);
            SerializedProperty paramsProp = so.FindProperty("parameters");
            if (paramsProp == null || !paramsProp.isArray)
            {
                return;
            }

            for (int i = 0; i < paramsProp.arraySize; i++)
            {
                int index = i;
                SerializedProperty elem = paramsProp.GetArrayElementAtIndex(i);
                SerializedProperty nameProp = elem.FindPropertyRelative("name");
                if (nameProp != null)
                {
                    register(nameProp.stringValue, $"{path} (Driver {index})", null, newName =>
                    {
                        var freshSo = new SerializedObject(behaviour);
                        SerializedProperty freshParams = freshSo.FindProperty("parameters");
                        if (freshParams != null && freshParams.isArray && index < freshParams.arraySize)
                        {
                            SerializedProperty freshElem = freshParams.GetArrayElementAtIndex(index);
                            SerializedProperty freshName = freshElem.FindPropertyRelative("name");
                            if (freshName != null)
                            {
                                freshName.stringValue = newName;
                                freshSo.ApplyModifiedProperties();
                                EditorUtility.SetDirty(behaviour);
                            }
                        }
                    });
                }

                SerializedProperty sourceProp = elem.FindPropertyRelative("source");
                if (sourceProp != null)
                {
                    register(sourceProp.stringValue, $"{path} (Driver Source {index})", null, newName =>
                    {
                        var freshSo = new SerializedObject(behaviour);
                        SerializedProperty freshParams = freshSo.FindProperty("parameters");
                        if (freshParams != null && freshParams.isArray && index < freshParams.arraySize)
                        {
                            SerializedProperty freshElem = freshParams.GetArrayElementAtIndex(index);
                            SerializedProperty freshSource = freshElem.FindPropertyRelative("source");
                            if (freshSource != null)
                            {
                                freshSource.stringValue = newName;
                                freshSo.ApplyModifiedProperties();
                                EditorUtility.SetDirty(behaviour);
                            }
                        }
                    });
                }
            }
        }

        private static bool IsAvatarParameterDriverBehaviour(StateMachineBehaviour behaviour)
        {
            if (behaviour == null)
            {
                return false;
            }

            string typeName = behaviour.GetType().Name;
            return !string.IsNullOrEmpty(typeName) &&
                   typeName.IndexOf("AvatarParameterDriver", StringComparison.Ordinal) >= 0;
        }

        internal static bool FixMissingReference(
            AnimatorController controller,
            ParameterIssue issue,
            string fixOption,
            string targetParameterName = null)
        {
            if (issue == null || issue.Type != IssueType.MissingReference)
            {
                return false;
            }

            if (fixOption == "UseExisting")
            {
                if (string.IsNullOrEmpty(targetParameterName))
                {
                    return false;
                }

                for (int i = 0; i < issue.References.Count; i++)
                {
                    issue.References[i].FixAction?.Invoke(targetParameterName);
                }

                return true;
            }

            if (fixOption == "CreateNew")
            {
                AnimatorControllerParameterType type = issue.ExpectedType ?? AnimatorControllerParameterType.Float;
                var newParam = new AnimatorControllerParameter { name = issue.ParameterName, type = type };
                controller.AddParameter(newParam);
                return true;
            }

            if (fixOption == "Remove")
            {
                for (int i = 0; i < issue.References.Count; i++)
                {
                    issue.References[i].FixAction?.Invoke(string.Empty);
                }

                return true;
            }

            return false;
        }

        internal static bool RemoveUnusedParameter(AnimatorController controller, string parameterName)
        {
            if (controller == null || string.IsNullOrEmpty(parameterName))
            {
                return false;
            }

            for (int i = 0; i < controller.parameters.Length; i++)
            {
                if (controller.parameters[i].name == parameterName)
                {
                    controller.RemoveParameter(i);
                    return true;
                }
            }

            return false;
        }

        internal static bool FixTypeMismatch(
            AnimatorController controller,
            ParameterIssue issue,
            AnimatorControllerParameterType targetType)
        {
            if (controller == null || issue == null || issue.Type != IssueType.TypeMismatch)
            {
                return false;
            }

            bool updatedParameter = UpdateParameterType(controller, issue.ParameterName, targetType);
            for (int i = 0; i < issue.References.Count; i++)
            {
                ParameterReferenceContext reference = issue.References[i];
                if (reference?.Transition == null || reference.ConditionIndex < 0)
                {
                    continue;
                }

                AdjustConditionForType(reference.Transition, reference.ConditionIndex, targetType);
            }

            return updatedParameter;
        }

        private static bool UpdateParameterType(AnimatorController controller, string parameterName, AnimatorControllerParameterType targetType)
        {
            AnimatorControllerParameter[] parameters = controller.parameters;
            bool changed = false;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name != parameterName)
                {
                    continue;
                }

                if (parameters[i].type != targetType)
                {
                    parameters[i].type = targetType;
                    changed = true;
                }

                break;
            }

            if (changed)
            {
                controller.parameters = parameters;
                EditorUtility.SetDirty(controller);
            }

            return changed;
        }

        private static void AdjustConditionForType(AnimatorTransitionBase transition, int conditionIndex, AnimatorControllerParameterType targetType)
        {
            if (transition == null)
            {
                return;
            }

            AnimatorCondition[] conditions = transition.conditions;
            if (conditionIndex < 0 || conditionIndex >= conditions.Length)
            {
                return;
            }

            AnimatorCondition cond = conditions[conditionIndex];
            switch (targetType)
            {
                case AnimatorControllerParameterType.Bool:
                case AnimatorControllerParameterType.Trigger:
                    switch (cond.mode)
                    {
                        case AnimatorConditionMode.IfNot:
                        case AnimatorConditionMode.Less:
                        case AnimatorConditionMode.NotEqual:
                            cond.mode = AnimatorConditionMode.IfNot;
                            break;
                        default:
                            cond.mode = AnimatorConditionMode.If;
                            break;
                    }
                    cond.threshold = 0f;
                    break;
                case AnimatorControllerParameterType.Float:
                    switch (cond.mode)
                    {
                        case AnimatorConditionMode.Equals:
                        case AnimatorConditionMode.If:
                            cond.mode = AnimatorConditionMode.Greater;
                            break;
                        case AnimatorConditionMode.NotEqual:
                        case AnimatorConditionMode.IfNot:
                            cond.mode = AnimatorConditionMode.Less;
                            break;
                    }
                    if (cond.mode == AnimatorConditionMode.Greater)
                    {
                        cond.threshold = Mathf.Max(cond.threshold, 0.5f);
                    }
                    else if (cond.mode == AnimatorConditionMode.Less)
                    {
                        cond.threshold = cond.threshold <= 0f ? 0f : Mathf.Min(cond.threshold, 0.5f);
                    }
                    break;
                case AnimatorControllerParameterType.Int:
                    switch (cond.mode)
                    {
                        case AnimatorConditionMode.If:
                            cond.mode = AnimatorConditionMode.Equals;
                            break;
                        case AnimatorConditionMode.IfNot:
                            cond.mode = AnimatorConditionMode.NotEqual;
                            break;
                    }
                    cond.threshold = Mathf.Round(cond.threshold);
                    break;
            }

            conditions[conditionIndex] = cond;
            transition.conditions = conditions;
            EditorUtility.SetDirty(transition);
        }
    }
}
