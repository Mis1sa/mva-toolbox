using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace MVA.Toolbox.QuickAnimatorEdit.Services.Parameter
{
    /// <summary>
    /// 参数检查服务
    /// 检查缺失参数引用、无用参数、参数类型不匹配等问题
    /// </summary>
    public static class ParameterCheckService
    {
        private static readonly AnimatorControllerParameterType[] _expectedTypePriority =
        {
            AnimatorControllerParameterType.Bool,
            AnimatorControllerParameterType.Int,
            AnimatorControllerParameterType.Float,
            AnimatorControllerParameterType.Trigger
        };

        public enum IssueType
        {
            MissingReference,    // 缺失参数引用 (使用了不存在的参数)
            UnusedParameter,     // 无用参数 (定义了但未被使用)
            TypeMismatch,        // 类型不匹配 (暂未实现完全检测)
            BrokenPPtr           // YAML 中存在损坏的 PPtr 引用
        }

        private sealed class BrokenPPtrLocation
        {
            public int LineIndex;
            public int MatchIndex;
            public int MatchLength;
            public string RawToken;
            public string PropertyLabel;
        }

        private static readonly Regex _yamlDocumentHeaderRegex = new Regex(
            @"^\s*---\s*!u!\d+\s*&(-?\d+)\s*$",
            RegexOptions.Compiled);

        private static readonly Regex _yamlInlinePPtrRegex = new Regex(
            @"\{fileID:\s*(-?\d+)(?:,\s*guid:\s*([0-9a-fA-F]{32}))?(?:,\s*type:\s*-?\d+)?\}",
            RegexOptions.Compiled);

        public class ParameterReferenceContext
        {
            public string Description;
            public Action<string> FixAction; // 用于修复引用的回调（传入新的参数名）
            public AnimatorTransitionBase Transition;
            public int ConditionIndex = -1;
            public AnimatorConditionMode? ConditionMode;
            public float ConditionThreshold;
        }

        public class ParameterIssue
        {
            public IssueType Type;
            public string ParameterName;
            public AnimatorControllerParameterType? ExpectedType; // 根据上下文推断的期望类型
            public AnimatorControllerParameterType? ActualType;   // 实际定义的类型
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

        public class CheckResult
        {
            public List<ParameterIssue> Issues = new List<ParameterIssue>();
            public bool HasIssues => Issues.Count > 0;
        }

        /// <summary>
        /// 执行参数检查
        /// </summary>
        public static CheckResult Execute(AnimatorController controller)
        {
            var result = new CheckResult();
            if (controller == null) return result;

            // 1. 获取所有已定义的参数
            var definedParameters = controller.parameters.ToDictionary(p => p.name, p => p);
            var definedParameterNames = new HashSet<string>(definedParameters.Keys, StringComparer.Ordinal);
            var usedParameterNames = new HashSet<string>();
            var typeHintMap = new Dictionary<string, HashSet<AnimatorControllerParameterType>>();
            var referenceMap = new Dictionary<string, List<ParameterReferenceContext>>(StringComparer.Ordinal);

            // 辅助方法：注册引用
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
                if (string.IsNullOrEmpty(paramName)) return;

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

                if (!referenceMap.TryGetValue(paramName, out var referenceList))
                {
                    referenceList = new List<ParameterReferenceContext>();
                    referenceMap[paramName] = referenceList;
                }
                referenceList.Add(context);

                if (typeHint.HasValue)
                {
                    if (!typeHintMap.TryGetValue(paramName, out var hints))
                    {
                        hints = new HashSet<AnimatorControllerParameterType>();
                        typeHintMap[paramName] = hints;
                    }
                    hints.Add(typeHint.Value);
                }

                // 检查是否缺失
                if (!definedParameters.ContainsKey(paramName))
                {
                    var issue = result.Issues.FirstOrDefault(i => i.Type == IssueType.MissingReference && i.ParameterName == paramName);
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
                    
                    // 如果之前的推断类型为空，尝试补充
                    if (!issue.ExpectedType.HasValue && typeHint.HasValue)
                    {
                        issue.ExpectedType = typeHint;
                    }

                    issue.References.Add(context);
                }
            }

            // 2. 遍历控制器收集引用
            for (int i = 0; i < controller.layers.Length; i++)
            {
                var layer = controller.layers[i];
                AnalyzeStateMachine(layer.stateMachine, $"Layer {i}", RegisterReference);
            }

            // 2.1 补充动画曲线中对 Animator 参数的引用统计
            RegisterAnimationCurveParameterReferences(
                controller,
                definedParameterNames,
                usedParameterNames,
                typeHintMap,
                referenceMap);

            // 2.2 检查控制器 YAML 中是否存在损坏 PPtr
            var brokenPPtrIssue = BuildBrokenPPtrIssue(controller);
            if (brokenPPtrIssue != null)
            {
                result.Issues.Add(brokenPPtrIssue);
            }

            // 3. 检查无用参数
            foreach (var param in controller.parameters)
            {
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

            // 4. 检查类型不匹配
            foreach (var param in controller.parameters)
            {
                if (!typeHintMap.TryGetValue(param.name, out var hints) || hints.Count == 0)
                    continue;

                if (hints.Contains(param.type))
                    continue;

                var issue = new ParameterIssue
                {
                    Type = IssueType.TypeMismatch,
                    ParameterName = param.name,
                    ExpectedType = SelectExpectedType(hints),
                    ActualType = param.type
                };

                if (referenceMap.TryGetValue(param.name, out var references))
                {
                    issue.References.AddRange(references);
                }

                result.Issues.Add(issue);
            }

            // 排序：缺失引用在前，无用参数在后
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

            for (int i = 0; i < _expectedTypePriority.Length; i++)
            {
                var candidate = _expectedTypePriority[i];
                if (hints.Contains(candidate))
                {
                    return candidate;
                }
            }

            return hints.OrderBy(x => (int)x).First();
        }

        private static ParameterIssue BuildBrokenPPtrIssue(AnimatorController controller)
        {
            if (!TryCollectBrokenPPtrs(controller, out var assetPath, out _, out _, out var brokenLocations))
                return null;

            if (brokenLocations.Count == 0)
                return null;

            var issue = new ParameterIssue
            {
                Type = IssueType.BrokenPPtr,
                ParameterName = Path.GetFileName(assetPath)
            };

            foreach (var location in brokenLocations)
            {
                issue.References.Add(new ParameterReferenceContext
                {
                    Description = $"第 {location.LineIndex + 1} 行 {location.PropertyLabel}: {location.RawToken}"
                });
            }

            return issue;
        }

        public static bool FixBrokenPPtrs(AnimatorController controller)
        {
            if (!TryCollectBrokenPPtrs(controller, out var assetPath, out var absolutePath, out var lines, out var brokenLocations))
                return false;

            if (brokenLocations.Count == 0)
                return false;

            var groupedByLine = brokenLocations.GroupBy(x => x.LineIndex);
            foreach (var group in groupedByLine)
            {
                var lineIndex = group.Key;
                if (lineIndex < 0 || lineIndex >= lines.Length)
                    continue;

                var line = lines[lineIndex];
                foreach (var location in group.OrderByDescending(x => x.MatchIndex))
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
                return false;

            assetPath = AssetDatabase.GetAssetPath(controller);
            if (string.IsNullOrEmpty(assetPath))
                return false;

            absolutePath = GetAbsoluteAssetPath(assetPath);
            if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath))
                return false;

            try
            {
                lines = File.ReadAllLines(absolutePath);
            }
            catch
            {
                return false;
            }

            if (lines.Length == 0 ||
                lines[0].IndexOf("%YAML", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return true;
            }

            var localIds = new HashSet<long>();
            for (int i = 0; i < lines.Length; i++)
            {
                var headerMatch = _yamlDocumentHeaderRegex.Match(lines[i]);
                if (headerMatch.Success && long.TryParse(headerMatch.Groups[1].Value, out var localId))
                {
                    localIds.Add(localId);
                }
            }

            var guidExistenceCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var matches = _yamlInlinePPtrRegex.Matches(line);
                foreach (Match match in matches)
                {
                    if (!match.Success)
                        continue;

                    if (!long.TryParse(match.Groups[1].Value, out var fileId))
                        continue;

                    var guid = match.Groups[2].Success ? match.Groups[2].Value : string.Empty;
                    if (!IsBrokenPPtr(fileId, guid, localIds, guidExistenceCache))
                        continue;

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

        private static bool IsBrokenPPtr(
            long fileId,
            string guid,
            HashSet<long> localIds,
            Dictionary<string, bool> guidExistenceCache)
        {
            if (fileId == 0)
                return false;

            if (string.IsNullOrEmpty(guid) || IsZeroGuid(guid))
            {
                return localIds == null || !localIds.Contains(fileId);
            }

            if (IsBuiltinGuid(guid))
                return false;

            if (!guidExistenceCache.TryGetValue(guid, out var exists))
            {
                exists = !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid));
                guidExistenceCache[guid] = exists;
            }

            return !exists;
        }

        private static bool IsZeroGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid) || guid.Length != 32)
                return false;

            for (int i = 0; i < guid.Length; i++)
            {
                if (guid[i] != '0')
                    return false;
            }

            return true;
        }

        private static bool IsBuiltinGuid(string guid)
        {
            return string.Equals(guid, "0000000000000000e000000000000000", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(guid, "0000000000000000f000000000000000", StringComparison.OrdinalIgnoreCase);
        }

        private static string TryExtractPropertyLabel(string line, int matchIndex)
        {
            if (string.IsNullOrEmpty(line))
                return "PPtr";

            var keySeparator = line.IndexOf(':');
            if (keySeparator > 0 && keySeparator < matchIndex)
            {
                var key = line.Substring(0, keySeparator).Trim();
                if (!string.IsNullOrEmpty(key))
                    return key;
            }

            return "PPtr";
        }

        private static string GetAbsoluteAssetPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;

            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot))
                return null;

            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        private static void AnalyzeStateMachine(AnimatorStateMachine stateMachine, string path, ParameterReferenceRegister register)
        {
            if (stateMachine == null) return;

            // StateMachine Behaviours
            if (stateMachine.behaviours != null)
            {
                for (int i = 0; i < stateMachine.behaviours.Length; i++)
                {
                    var behaviour = stateMachine.behaviours[i];
                    AnalyzeBehaviour(behaviour, $"{path} (StateMachine Driver {i})", register);
                }
            }

            // States
            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                if (state == null) continue;
                string statePath = $"{path}/{state.name}";

                AnalyzeState(state, statePath, register);
                
                // Transitions
                foreach (var t in state.transitions)
                {
                    AnalyzeTransition(t, $"{statePath} -> {(t.destinationState?.name ?? "Exit")}", register);
                }
            }

            // AnyState Transitions
            foreach (var t in stateMachine.anyStateTransitions)
            {
                AnalyzeTransition(t, $"{path}/AnyState -> {(t.destinationState?.name ?? "Exit")}", register);
            }

            // Entry Transitions
            foreach (var t in stateMachine.entryTransitions)
            {
                AnalyzeTransition(t, $"{path}/Entry -> {(t.destinationState?.name ?? "Exit")}", register);
            }

            // Sub State Machines
            foreach (var sub in stateMachine.stateMachines)
            {
                var subStateMachine = sub.stateMachine;
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
                return;

            var clips = new HashSet<AnimationClip>();
            for (int i = 0; i < controller.layers.Length; i++)
            {
                var layer = controller.layers[i];
                if (layer?.stateMachine == null)
                    continue;

                CollectClipsFromStateMachine(layer.stateMachine, clips);
            }

            foreach (var clip in clips)
            {
                if (clip == null)
                    continue;

                var bindings = AnimationUtility.GetCurveBindings(clip);
                for (int i = 0; i < bindings.Length; i++)
                {
                    var binding = bindings[i];
                    if (binding.type != typeof(Animator))
                        continue;

                    if (!TryExtractAnimatorParameterName(binding.propertyName, definedParameterNames, out var parameterName))
                        continue;

                    usedParameterNames.Add(parameterName);

                    if (!typeHintMap.TryGetValue(parameterName, out var hints))
                    {
                        hints = new HashSet<AnimatorControllerParameterType>();
                        typeHintMap[parameterName] = hints;
                    }
                    hints.Add(AnimatorControllerParameterType.Float);

                    if (!referenceMap.TryGetValue(parameterName, out var references))
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
                return;

            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                if (state == null)
                    continue;

                CollectClipsFromMotion(state.motion, clips);
            }

            foreach (var sub in stateMachine.stateMachines)
            {
                var subStateMachine = sub.stateMachine;
                if (subStateMachine != null)
                {
                    CollectClipsFromStateMachine(subStateMachine, clips);
                }
            }
        }

        private static void CollectClipsFromMotion(Motion motion, HashSet<AnimationClip> clips)
        {
            if (motion == null || clips == null)
                return;

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

        private static bool TryExtractAnimatorParameterName(
            string propertyName,
            HashSet<string> definedParameterNames,
            out string parameterName)
        {
            parameterName = null;
            if (string.IsNullOrEmpty(propertyName))
                return false;

            if (definedParameterNames != null && definedParameterNames.Contains(propertyName))
            {
                parameterName = propertyName;
                return true;
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

            if (state.motion is UnityEditor.Animations.BlendTree bt)
            {
                AnalyzeBlendTree(bt, path, register);
            }

            foreach (var behaviour in state.behaviours)
            {
                AnalyzeBehaviour(behaviour, path, register);
            }
        }

        private static void AnalyzeBlendTree(UnityEditor.Animations.BlendTree bt, string path, ParameterReferenceRegister register)
        {
            if (bt == null) return;
            string btPath = $"{path}/{bt.name}";

            if (bt.blendType != BlendTreeType.Simple1D)
            {
                // 2D Blend Trees use X and Y
                if (bt.blendType != BlendTreeType.Direct)
                {
                    register(bt.blendParameter, $"{btPath} (Blend X)", AnimatorControllerParameterType.Float, newName =>
                    {
                        bt.blendParameter = newName;
                        EditorUtility.SetDirty(bt);
                    });
                    register(bt.blendParameterY, $"{btPath} (Blend Y)", AnimatorControllerParameterType.Float, newName =>
                    {
                        bt.blendParameterY = newName;
                        EditorUtility.SetDirty(bt);
                    });
                }
            }
            else
            {
                // 1D
                register(bt.blendParameter, $"{btPath} (Blend)", AnimatorControllerParameterType.Float, newName =>
                {
                    bt.blendParameter = newName;
                    EditorUtility.SetDirty(bt);
                });
            }

            // Recursive children
            foreach (var child in bt.children)
            {
                if (child.motion is UnityEditor.Animations.BlendTree childBt)
                {
                    AnalyzeBlendTree(childBt, btPath, register);
                }
                
                // Direct BlendTree children parameters
                if (bt.blendType == BlendTreeType.Direct)
                {
                    register(child.directBlendParameter, $"{btPath} (Direct Child)", AnimatorControllerParameterType.Float, newName =>
                    {
                        // Direct blend parameter needs to be updated in the ChildMotion struct array
                        // Note: Modifying child struct in array requires re-assigning the array or finding the index.
                        // Here we use a closure capturing the BlendTree and child index logic would be complex.
                        // Simplified approach: Re-fetch children, update, set back.
                        var children = bt.children;
                        for(int i=0; i<children.Length; i++)
                        {
                            if (children[i].directBlendParameter == child.directBlendParameter && children[i].motion == child.motion) // Weak identification
                            {
                                var c = children[i];
                                c.directBlendParameter = newName;
                                children[i] = c;
                                bt.children = children; // Re-assign
                                EditorUtility.SetDirty(bt);
                                break;
                            }
                        }
                    });
                }
            }
        }

        private static void AnalyzeTransition(AnimatorTransitionBase transition, string path, ParameterReferenceRegister register)
        {
            var conditions = transition.conditions;
            for (int i = 0; i < conditions.Length; i++)
            {
                var cond = conditions[i];
                int index = i; // Capture for closure
                AnimatorControllerParameterType? typeHint = cond.mode switch
                {
                    AnimatorConditionMode.If => AnimatorControllerParameterType.Bool,
                    AnimatorConditionMode.IfNot => AnimatorControllerParameterType.Bool,
                    AnimatorConditionMode.Equals => AnimatorControllerParameterType.Int,
                    AnimatorConditionMode.NotEqual => AnimatorControllerParameterType.Int,
                    _ => null
                };

                register(cond.parameter, $"{path} (Condition {index})", typeHint, newName =>
                {
                    // Need to re-fetch conditions array, modify, and set back
                    var currentConditions = transition.conditions;
                    if (index < currentConditions.Length)
                    {
                        currentConditions[index].parameter = newName;
                        transition.conditions = currentConditions;
                        EditorUtility.SetDirty(transition);
                    }
                },
                transition,
                index,
                cond.mode,
                cond.threshold);
            }
        }

        private static void AnalyzeBehaviour(StateMachineBehaviour behaviour, string path, ParameterReferenceRegister register)
        {
            if (behaviour == null) return;

            // Support VRCAvatarParameterDriver
            if (IsAvatarParameterDriverBehaviour(behaviour))
            {
                var so = new SerializedObject(behaviour);
                var paramsProp = so.FindProperty("parameters");
                if (paramsProp != null && paramsProp.isArray)
                {
                    for (int i = 0; i < paramsProp.arraySize; i++)
                    {
                        int index = i;
                        var elem = paramsProp.GetArrayElementAtIndex(i);
                        var nameProp = elem.FindPropertyRelative("name");
                        if (nameProp != null)
                        {
                            register(nameProp.stringValue, $"{path} (Driver {index})", null, newName =>
                            {
                                var freshSo = new SerializedObject(behaviour);
                                var freshParams = freshSo.FindProperty("parameters");
                                if (freshParams != null && freshParams.isArray && index < freshParams.arraySize)
                                {
                                    var freshElem = freshParams.GetArrayElementAtIndex(index);
                                    var freshName = freshElem.FindPropertyRelative("name");
                                    if (freshName != null)
                                    {
                                        freshName.stringValue = newName;
                                        freshSo.ApplyModifiedProperties();
                                        EditorUtility.SetDirty(behaviour);
                                    }
                                }
                            });
                        }

                        var sourceProp = elem.FindPropertyRelative("source");
                        if (sourceProp != null)
                        {
                            register(sourceProp.stringValue, $"{path} (Driver Source {index})", null, newName =>
                            {
                                var freshSo = new SerializedObject(behaviour);
                                var freshParams = freshSo.FindProperty("parameters");
                                if (freshParams != null && freshParams.isArray && index < freshParams.arraySize)
                                {
                                    var freshElem = freshParams.GetArrayElementAtIndex(index);
                                    var freshSource = freshElem.FindPropertyRelative("source");
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
            }
        }

        private static bool IsAvatarParameterDriverBehaviour(StateMachineBehaviour behaviour)
        {
            if (behaviour == null)
                return false;

            var typeName = behaviour.GetType().Name;
            return !string.IsNullOrEmpty(typeName) &&
                   typeName.IndexOf("AvatarParameterDriver", StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// 修复缺失参数引用
        /// </summary>
        public static bool FixMissingReference(
            AnimatorController controller,
            ParameterIssue issue,
            string fixOption,
            string targetParameterName = null)
        {
            if (issue == null || issue.Type != IssueType.MissingReference) return false;

            if (fixOption == "UseExisting")
            {
                if (string.IsNullOrEmpty(targetParameterName)) return false;
                foreach (var refer in issue.References)
                {
                    refer.FixAction?.Invoke(targetParameterName);
                }
                return true;
            }
            else if (fixOption == "CreateNew")
            {
                // Create parameter
                var type = issue.ExpectedType ?? AnimatorControllerParameterType.Float;
                var newParam = new AnimatorControllerParameter { name = issue.ParameterName, type = type };
                controller.AddParameter(newParam);
                // No need to update references since they already point to this name
                return true;
            }
            else if (fixOption == "Remove")
            {
                // Set to empty string usually removes/invalidates the reference
                foreach (var refer in issue.References)
                {
                    refer.FixAction?.Invoke(string.Empty);
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// 移除无用参数
        /// </summary>
        public static bool RemoveUnusedParameter(AnimatorController controller, string parameterName)
        {
            if (controller == null || string.IsNullOrEmpty(parameterName)) return false;

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

        /// <summary>
        /// 修复参数类型不匹配：同步参数类型并调整条件
        /// </summary>
        public static bool FixTypeMismatch(
            AnimatorController controller,
            ParameterIssue issue,
            AnimatorControllerParameterType targetType)
        {
            if (controller == null || issue == null || issue.Type != IssueType.TypeMismatch)
                return false;

            bool updatedParameter = UpdateParameterType(controller, issue.ParameterName, targetType);

            foreach (var reference in issue.References)
            {
                if (reference?.Transition == null || reference.ConditionIndex < 0)
                    continue;

                AdjustConditionForType(reference.Transition, reference.ConditionIndex, targetType);
            }

            return updatedParameter;
        }

        private static bool UpdateParameterType(AnimatorController controller, string parameterName, AnimatorControllerParameterType targetType)
        {
            var parameters = controller.parameters;
            bool changed = false;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == parameterName)
                {
                    if (parameters[i].type != targetType)
                    {
                        parameters[i].type = targetType;
                        changed = true;
                    }
                    break;
                }
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
            if (transition == null) return;
            var conditions = transition.conditions;
            if (conditionIndex < 0 || conditionIndex >= conditions.Length) return;

            var cond = conditions[conditionIndex];
            switch (targetType)
            {
                case AnimatorControllerParameterType.Bool:
                case AnimatorControllerParameterType.Trigger:
                    cond.mode = cond.mode switch
                    {
                        AnimatorConditionMode.IfNot => AnimatorConditionMode.IfNot,
                        AnimatorConditionMode.Less => AnimatorConditionMode.IfNot,
                        AnimatorConditionMode.NotEqual => AnimatorConditionMode.IfNot,
                        _ => AnimatorConditionMode.If
                    };
                    cond.threshold = 0f;
                    break;
                case AnimatorControllerParameterType.Float:
                    cond.mode = cond.mode switch
                    {
                        AnimatorConditionMode.Equals => AnimatorConditionMode.Greater,
                        AnimatorConditionMode.NotEqual => AnimatorConditionMode.Less,
                        AnimatorConditionMode.If => AnimatorConditionMode.Greater,
                        AnimatorConditionMode.IfNot => AnimatorConditionMode.Less,
                        _ => cond.mode
                    };
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
                    cond.mode = cond.mode switch
                    {
                        AnimatorConditionMode.If => AnimatorConditionMode.Equals,
                        AnimatorConditionMode.IfNot => AnimatorConditionMode.NotEqual,
                        _ => cond.mode
                    };
                    cond.threshold = Mathf.Round(cond.threshold);
                    break;
            }

            conditions[conditionIndex] = cond;
            transition.conditions = conditions;
            EditorUtility.SetDirty(transition);
        }
    }
}
