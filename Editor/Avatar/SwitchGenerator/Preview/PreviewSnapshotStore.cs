using System.Collections.Generic;
using UnityEngine;

namespace MVA.Toolbox.SwitchGenerator.Preview
{
    internal sealed class PreviewSnapshotStore
    {
        private readonly Dictionary<GameObject, bool> _activeStates = new Dictionary<GameObject, bool>();
        private readonly Dictionary<SkinnedMeshRenderer, Dictionary<string, float>> _blendStates = new Dictionary<SkinnedMeshRenderer, Dictionary<string, float>>();

        public void Capture(GameObject root)
        {
            _activeStates.Clear();
            _blendStates.Clear();

            if (root == null)
            {
                return;
            }

            var stack = new Stack<Transform>();
            stack.Push(root.transform);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current == null)
                {
                    continue;
                }

                var go = current.gameObject;
                _activeStates[go] = go.activeSelf;

                var renderers = go.GetComponents<SkinnedMeshRenderer>();
                for (int r = 0; r < renderers.Length; r++)
                {
                    var smr = renderers[r];
                    if (smr == null || smr.sharedMesh == null)
                    {
                        continue;
                    }

                    var map = new Dictionary<string, float>();
                    for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                    {
                        string shapeName = smr.sharedMesh.GetBlendShapeName(i);
                        map[shapeName] = smr.GetBlendShapeWeight(i);
                    }
                    _blendStates[smr] = map;
                }

                foreach (Transform child in current)
                {
                    if (child != null)
                    {
                        stack.Push(child);
                    }
                }
            }
        }

        public void Restore()
        {
            foreach (var pair in _activeStates)
            {
                if (pair.Key != null)
                {
                    pair.Key.SetActive(pair.Value);
                }
            }

            foreach (var pair in _blendStates)
            {
                var smr = pair.Key;
                if (smr == null || smr.sharedMesh == null)
                {
                    continue;
                }

                foreach (var weight in pair.Value)
                {
                    int index = smr.sharedMesh.GetBlendShapeIndex(weight.Key);
                    if (index >= 0)
                    {
                        smr.SetBlendShapeWeight(index, weight.Value);
                    }
                }
            }
        }
    }
}
