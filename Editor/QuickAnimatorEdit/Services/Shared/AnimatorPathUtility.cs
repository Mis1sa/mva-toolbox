using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;

namespace MVA.Toolbox.QuickAnimatorEdit.Services.Shared
{
    public static class AnimatorPathUtility
    {
        private const char SlashEscapeChar = '\u001F';
        private const string SubStateMachineSuffix = " (子状态机)";

        /// <summary>
        /// 组合父路径和当前名称片段，自动处理斜杠转义
        /// </summary>
        public static string Combine(string parentPath, string segment)
        {
            string encodedSegment = EncodeSegment(segment);
            if (string.IsNullOrEmpty(parentPath))
            {
                return encodedSegment;
            }

            return parentPath + "/" + encodedSegment;
        }

        /// <summary>
        /// 转义片段中的斜杠
        /// </summary>
        public static string EncodeSegment(string segment)
        {
            if (string.IsNullOrEmpty(segment)) return string.Empty;
            return segment.Replace('/', SlashEscapeChar);
        }

        /// <summary>
        /// 还原片段中的斜杠
        /// </summary>
        public static string DecodeSegment(string segment)
        {
            if (string.IsNullOrEmpty(segment)) return string.Empty;
            return segment.Replace(SlashEscapeChar, '/');
        }

        /// <summary>
        /// 拆分路径为片段
        /// </summary>
        public static string[] SplitPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return System.Array.Empty<string>();

            var rawSegments = path.Split('/');
            var result = new string[rawSegments.Length];
            for (int i = 0; i < rawSegments.Length; i++)
            {
                result[i] = DecodeSegment(rawSegments[i]);
            }
            return result;
        }

        /// <summary>
        /// 递归收集状态机中的所有状态和子状态机（新版通用逻辑）
        /// </summary>
        /// <param name="stateMachine">根状态机</param>
        /// <param name="parentDisplayPath">父级显示路径（用于 UI 显示，未转义，使用 / 分隔）</param>
        /// <param name="parentActualPath">父级实际路径（用于内部查找，已转义）</param>
        /// <param name="displayPaths">输出：显示路径列表</param>
        /// <param name="actualPaths">输出：实际路径列表</param>
        /// <param name="displayToActualPath">输出：显示路径到实际路径的映射</param>
        /// <param name="stateByDisplayPath">输出：显示路径到状态的映射</param>
        /// <param name="stateMachineByDisplayPath">输出：显示路径到子状态机的映射</param>
        /// <param name="isStateMachineMap">输出：记录是否为子状态机</param>
        /// <param name="includeStates">是否收集状态</param>
        /// <param name="machinesAreSelectable">子状态机是否作为可选项（如果为false，只作为文件夹路径递归，不加入列表）</param>
        public static void CollectHierarchy(
            AnimatorStateMachine stateMachine,
            string parentDisplayPath,
            string parentActualPath,
            List<string> displayPaths,
            List<string> actualPaths,
            Dictionary<string, string> displayToActualPath,
            Dictionary<string, AnimatorState> stateByDisplayPath,
            Dictionary<string, AnimatorStateMachine> stateMachineByDisplayPath,
            Dictionary<string, bool> isStateMachineMap,
            bool includeStates = true,
            bool machinesAreSelectable = true)
        {
            if (stateMachine == null) return;

            // 1. 收集当前层的状态
            if (includeStates)
            {
                foreach (var childState in stateMachine.states)
                {
                    if (childState.state == null) continue;

                    string stateName = childState.state.name;
                    
                    // 完整显示路径：直接使用名称，不进行转义，让 / 形成层级
                    string fullDisplayPath = string.IsNullOrEmpty(parentDisplayPath)
                        ? stateName
                        : $"{parentDisplayPath}/{stateName}";

                    // 去重处理
                    string uniqueDisplayPath = fullDisplayPath;
                    int counter = 1;
                    // 检查是否与已有的状态或状态机路径冲突
                    while ((stateByDisplayPath != null && stateByDisplayPath.ContainsKey(uniqueDisplayPath)) || 
                           (stateMachineByDisplayPath != null && stateMachineByDisplayPath.ContainsKey(uniqueDisplayPath)))
                    {
                        uniqueDisplayPath = $"{fullDisplayPath} ({counter++})";
                    }

                    // 实际路径（用于查找）
                    string fullActualPath = Combine(parentActualPath, stateName);

                    if (displayPaths != null) displayPaths.Add(uniqueDisplayPath);
                    if (actualPaths != null) actualPaths.Add(fullActualPath);
                    if (displayToActualPath != null) displayToActualPath[uniqueDisplayPath] = fullActualPath;
                    if (isStateMachineMap != null) isStateMachineMap[uniqueDisplayPath] = false;  // 这是状态
                    if (stateByDisplayPath != null) stateByDisplayPath[uniqueDisplayPath] = childState.state;
                }
            }

            // 2. 处理子状态机
            foreach (var childMachine in stateMachine.stateMachines)
            {
                if (childMachine.stateMachine == null) continue;

                string machineName = childMachine.stateMachine.name;

                // 实际路径
                string fullActualPath = Combine(parentActualPath, machineName);

                // 构造子状态机的显示路径节点
                // 统一规则：直接追加 " <子状态机>" 后缀
                string machineNode = $"{machineName}{SubStateMachineSuffix}";

                // 组合父路径
                string selfDisplayPath = string.IsNullOrEmpty(parentDisplayPath)
                    ? machineNode
                    : $"{parentDisplayPath}/{machineNode}";

                // 只有当子状态机也是可选项时，才需要去重并添加到列表
                if (machinesAreSelectable)
                {
                    // 对路径进行去重
                    string uniqueSelfPath = selfDisplayPath;
                    int selfCounter = 1;
                    while ((stateByDisplayPath != null && stateByDisplayPath.ContainsKey(uniqueSelfPath)) || 
                           (stateMachineByDisplayPath != null && stateMachineByDisplayPath.ContainsKey(uniqueSelfPath)))
                    {
                        uniqueSelfPath = $"{selfDisplayPath} ({selfCounter++})";
                    }
                    
                    // 将子状态机自身作为可选项添加
                    if (displayPaths != null) displayPaths.Add(uniqueSelfPath);
                    if (actualPaths != null) actualPaths.Add(fullActualPath);
                    if (displayToActualPath != null) displayToActualPath[uniqueSelfPath] = fullActualPath;
                    if (isStateMachineMap != null) isStateMachineMap[uniqueSelfPath] = true;  // 这是子状态机
                    if (stateMachineByDisplayPath != null) stateMachineByDisplayPath[uniqueSelfPath] = childMachine.stateMachine;
                    
                    // 递归收集子状态机内的状态
                    // 注意：这里的 parentDisplayPath 应该使用 uniqueSelfPath，以保持层级一致性
                    CollectHierarchy(childMachine.stateMachine, uniqueSelfPath, fullActualPath, 
                        displayPaths, actualPaths, displayToActualPath, stateByDisplayPath, stateMachineByDisplayPath, isStateMachineMap, 
                        includeStates, machinesAreSelectable);
                }
                else
                {
                    // 如果子状态机不可选（如状态模式），则它只作为路径容器
                    // 不需要添加到列表，但需要作为父路径传递给递归调用
                    // 注意：这里不需要去重逻辑，因为不存入字典作为 key
                    CollectHierarchy(childMachine.stateMachine, selfDisplayPath, fullActualPath, 
                        displayPaths, actualPaths, displayToActualPath, stateByDisplayPath, stateMachineByDisplayPath, isStateMachineMap, 
                        includeStates, machinesAreSelectable);
                }
            }
        }

        /// <summary>
        /// 通过路径查找状态
        /// </summary>
        public static AnimatorState FindStateByPath(AnimatorStateMachine root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path)) return null;
            var segments = SplitPath(path);
            return FindStateRecursive(root, segments, 0);
        }

        /// <summary>
        /// 通过路径查找状态及其父状态机
        /// </summary>
        /// <returns>找到的状态（若未找到则为null），通过 out 参数返回父状态机</returns>
        public static AnimatorState FindStateAndParent(AnimatorStateMachine root, string path, out AnimatorStateMachine parentMachine)
        {
            parentMachine = null;
            if (root == null || string.IsNullOrEmpty(path)) return null;
            var segments = SplitPath(path);
            return FindStateAndParentRecursive(root, segments, 0, ref parentMachine);
        }

        private static AnimatorState FindStateRecursive(AnimatorStateMachine current, string[] segments, int index)
        {
            if (current == null || index >= segments.Length) return null;

            string segmentName = segments[index];
            bool isLast = index == segments.Length - 1;

            if (isLast)
            {
                // 在当前层查找状态
                foreach (var childState in current.states)
                {
                    if (childState.state != null && childState.state.name == segmentName)
                        return childState.state;
                }
            }
            else
            {
                // 查找子状态机并继续
                foreach (var childMachine in current.stateMachines)
                {
                    if (childMachine.stateMachine != null && childMachine.stateMachine.name == segmentName)
                    {
                        return FindStateRecursive(childMachine.stateMachine, segments, index + 1);
                    }
                }
            }

            return null;
        }

        private static AnimatorState FindStateAndParentRecursive(AnimatorStateMachine current, string[] segments, int index, ref AnimatorStateMachine parentResult)
        {
            if (current == null || index >= segments.Length) return null;

            string segmentName = segments[index];
            bool isLast = index == segments.Length - 1;

            if (isLast)
            {
                // 在当前层查找状态
                foreach (var childState in current.states)
                {
                    if (childState.state != null && childState.state.name == segmentName)
                    {
                        parentResult = current;
                        return childState.state;
                    }
                }
            }
            else
            {
                // 查找子状态机并继续
                foreach (var childMachine in current.stateMachines)
                {
                    if (childMachine.stateMachine != null && childMachine.stateMachine.name == segmentName)
                    {
                        return FindStateAndParentRecursive(childMachine.stateMachine, segments, index + 1, ref parentResult);
                    }
                }
            }

            return null;
        }
    }
}
