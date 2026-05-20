using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MVA.Toolbox.AnimationQueryTool
{
    internal sealed partial class AnimationQueryToolService
    {
        private bool TryGetSelectedPathForController(
            AnimatorController controller,
            Transform controllerRoot,
            Dictionary<AnimatorController, string> cache,
            out string relativePath)
        {
            relativePath = null;
            if (controller == null)
            {
                return false;
            }

            if (cache.TryGetValue(controller, out string cached))
            {
                relativePath = cached;
                return relativePath != null;
            }

            if (_selectedTransform == null)
            {
                cache[controller] = null;
                return false;
            }

            Transform root = controllerRoot ?? _targetRoot?.transform;
            if (!TryGetRelativePath(_selectedTransform, root, out string path))
            {
                cache[controller] = null;
                return false;
            }

            cache[controller] = path;
            relativePath = path;
            return true;
        }

        private void ForEachLayerInScope(Action<AnimatorController, AnimatorControllerLayer, Transform> action)
        {
            if (action == null || _controllers == null || _controllers.Count == 0)
            {
                return;
            }

            if (_selectedControllerIndex < 0)
            {
                for (int i = 0; i < _controllers.Count; i++)
                {
                    AnimatorController controller = _controllers[i];
                    if (controller == null)
                    {
                        continue;
                    }

                    Transform controllerRoot = GetControllerRoot(controller) ?? _targetRoot?.transform;
                    IterateControllerLayers(controller, controllerRoot, _selectedLayerIndex, action);
                }
                return;
            }

            AnimatorController selectedController = SelectedController;
            if (selectedController == null)
            {
                return;
            }

            Transform selectedRoot = SelectedControllerRoot ?? _targetRoot?.transform;
            IterateControllerLayers(selectedController, selectedRoot, _selectedLayerIndex, action);
        }

        private static void IterateControllerLayers(
            AnimatorController controller,
            Transform controllerRoot,
            int selectedLayerIndex,
            Action<AnimatorController, AnimatorControllerLayer, Transform> action)
        {
            AnimatorControllerLayer[] layers = controller.layers ?? Array.Empty<AnimatorControllerLayer>();
            if (layers.Length == 0)
            {
                return;
            }

            if (selectedLayerIndex < 0)
            {
                for (int i = 0; i < layers.Length; i++)
                {
                    action(controller, layers[i], controllerRoot);
                }
                return;
            }

            if (selectedLayerIndex < layers.Length)
            {
                action(controller, layers[selectedLayerIndex], controllerRoot);
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
                ChildAnimatorState child = states[i];
                if (child.state == null)
                {
                    continue;
                }

                CollectClipsFromMotion(child.state.motion, clips);
            }

            ChildAnimatorStateMachine[] subMachines = stateMachine.stateMachines;
            for (int i = 0; i < subMachines.Length; i++)
            {
                ChildAnimatorStateMachine sub = subMachines[i];
                if (sub.stateMachine != null)
                {
                    CollectClipsFromStateMachine(sub.stateMachine, clips);
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

            if (motion is BlendTree tree)
            {
                ChildMotion[] children = tree.children;
                for (int i = 0; i < children.Length; i++)
                {
                    CollectClipsFromMotion(children[i].motion, clips);
                }
            }
        }

        private Object NormalizeSelectedObjectForScope(Object obj)
        {
            if (obj == null)
            {
                return null;
            }

            GameObject rootGo = _targetRoot;
            if (rootGo == null)
            {
                return obj;
            }

            GameObject go = obj switch
            {
                GameObject gameObject => gameObject,
                Component component => component.gameObject,
                _ => null
            };

            if (go == null || go == rootGo)
            {
                return null;
            }

            Transform rootTransform = rootGo.transform;
            Transform current = go.transform;
            bool underRoot = false;
            while (current != null)
            {
                if (current == rootTransform)
                {
                    underRoot = true;
                    break;
                }

                current = current.parent;
            }

            if (!underRoot)
            {
                return null;
            }

            if (_controllers != null && _controllers.Count > 0)
            {
                current = go.transform.parent;
                while (current != null && current != rootTransform)
                {
                    Animator animator = current.GetComponent<Animator>();
                    if (animator != null && animator.runtimeAnimatorController is AnimatorController controller && !_controllers.Contains(controller))
                    {
                        return null;
                    }

                    current = current.parent;
                }
            }

            SkinnedMeshRenderer smrOnGo = go.GetComponent<SkinnedMeshRenderer>();
            if (smrOnGo != null)
            {
                GameObject mergeOwner = FindAaoMergeOwnerForRendererUnderRoot(rootGo, smrOnGo);
                if (mergeOwner != null)
                {
                    go = mergeOwner;
                }
            }

            return go;
        }

        private static GameObject FindAaoMergeOwnerForRendererUnderRoot(GameObject root, SkinnedMeshRenderer targetSmr)
        {
            if (root == null || targetSmr == null)
            {
                return null;
            }

            Transform rootTransform = root.transform;
            Type mergeType = null;
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length && mergeType == null; i++)
            {
                var assembly = assemblies[i];
                if (assembly != null)
                {
                    mergeType = assembly.GetType("Anatawa12.AvatarOptimizer.MergeSkinnedMesh");
                }
            }

            if (mergeType == null)
            {
                return null;
            }

            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || component.GetType() != mergeType)
                {
                    continue;
                }

                GameObject mergeObject = component.gameObject;
                if (mergeObject == null)
                {
                    continue;
                }

                Transform mergeTransform = mergeObject.transform;
                bool underRoot = false;
                Transform current = mergeTransform;
                while (current != null)
                {
                    if (current == rootTransform)
                    {
                        underRoot = true;
                        break;
                    }

                    current = current.parent;
                }

                if (!underRoot)
                {
                    continue;
                }

                try
                {
                    var renderersField = mergeType.GetField("renderersSet", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    object setObject = renderersField != null ? renderersField.GetValue(component) : null;
                    if (setObject == null)
                    {
                        continue;
                    }

                    var setType = setObject.GetType();
                    System.Collections.IEnumerable enumerable = null;
                    var getAsSet = setType.GetMethod("GetAsSet", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (getAsSet != null)
                    {
                        enumerable = getAsSet.Invoke(setObject, null) as System.Collections.IEnumerable;
                    }
                    else if (setObject is System.Collections.IEnumerable existingEnumerable)
                    {
                        enumerable = existingEnumerable;
                    }

                    if (enumerable == null)
                    {
                        continue;
                    }

                    foreach (object item in enumerable)
                    {
                        if (item is SkinnedMeshRenderer smr && smr == targetSmr)
                        {
                            return mergeObject;
                        }
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static bool TryGetRelativePath(Transform target, Transform root, out string path)
        {
            path = string.Empty;
            if (target == null || root == null)
            {
                return false;
            }

            if (target == root)
            {
                return true;
            }

            Stack<string> stack = new Stack<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                stack.Push(current.name);
                current = current.parent;
            }

            if (current != root)
            {
                return false;
            }

            path = string.Join("/", stack.ToArray());
            return true;
        }
    }
}
