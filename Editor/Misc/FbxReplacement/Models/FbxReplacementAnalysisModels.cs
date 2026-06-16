using System.Collections.Generic;
using UnityEngine;

namespace MVA.Toolbox.FbxReplacement
{
    internal sealed class FbxReplacementAnalysisResult
    {
        internal FbxReplacementAnalysisResult(
            FbxReplacementObjectSnapshot referenceSnapshot,
            FbxReplacementObjectSnapshot targetSnapshot,
            List<FbxReplacementNodeMatch> nodeMatches)
        {
            ReferenceSnapshot = referenceSnapshot;
            TargetSnapshot = targetSnapshot;
            NodeMatches = nodeMatches ?? new List<FbxReplacementNodeMatch>();
        }

        internal FbxReplacementObjectSnapshot ReferenceSnapshot { get; }
        internal FbxReplacementObjectSnapshot TargetSnapshot { get; }
        internal IReadOnlyList<FbxReplacementNodeMatch> NodeMatches { get; }
    }

    internal sealed class FbxReplacementObjectSnapshot
    {
        internal FbxReplacementObjectSnapshot(GameObject rootObject, List<FbxReplacementNodeSnapshot> nodes)
        {
            RootObject = rootObject;
            Nodes = nodes ?? new List<FbxReplacementNodeSnapshot>();
        }

        internal GameObject RootObject { get; }
        internal IReadOnlyList<FbxReplacementNodeSnapshot> Nodes { get; }
    }

    internal sealed class FbxReplacementNodeSnapshot
    {
        internal FbxReplacementNodeSnapshot(
            GameObject gameObject,
            string path,
            string parentPath,
            int depth,
            List<string> childNames,
            List<FbxReplacementComponentSnapshot> components,
            FbxReplacementRendererSignature renderer)
        {
            GameObject = gameObject;
            Path = path;
            ParentPath = parentPath;
            Depth = depth;
            ChildNames = childNames ?? new List<string>();
            Components = components ?? new List<FbxReplacementComponentSnapshot>();
            Renderer = renderer ?? FbxReplacementRendererSignature.Empty;

            Name = gameObject != null ? gameObject.name : string.Empty;
            Transform = gameObject != null ? gameObject.transform : null;
            LocalPosition = Transform != null ? Transform.localPosition : Vector3.zero;
            LocalRotation = Transform != null ? Transform.localRotation : Quaternion.identity;
            LocalScale = Transform != null ? Transform.localScale : Vector3.one;
        }

        internal GameObject GameObject { get; }
        internal Transform Transform { get; }
        internal string Name { get; }
        internal string Path { get; }
        internal string ParentPath { get; }
        internal int Depth { get; }
        internal Vector3 LocalPosition { get; }
        internal Quaternion LocalRotation { get; }
        internal Vector3 LocalScale { get; }
        internal IReadOnlyList<string> ChildNames { get; }
        internal IReadOnlyList<FbxReplacementComponentSnapshot> Components { get; }
        internal FbxReplacementRendererSignature Renderer { get; }
        internal int ChildCount => ChildNames.Count;
    }

    internal sealed class FbxReplacementComponentSnapshot
    {
        internal FbxReplacementComponentSnapshot(Component component, string ownerPath)
        {
            Component = component;
            OwnerPath = ownerPath;
            OwnerGameObject = component != null ? component.gameObject : null;
            Type = component != null ? component.GetType() : null;
            TypeName = Type != null ? Type.Name : string.Empty;
            FullTypeName = Type != null ? Type.FullName : string.Empty;
        }

        internal Component Component { get; }
        internal GameObject OwnerGameObject { get; }
        internal string OwnerPath { get; }
        internal System.Type Type { get; }
        internal string TypeName { get; }
        internal string FullTypeName { get; }
    }

    internal sealed class FbxReplacementRendererSignature
    {
        internal static FbxReplacementRendererSignature Empty { get; } = new FbxReplacementRendererSignature();

        internal string RendererTypeName { get; private set; }
        internal string MeshName { get; private set; }
        internal int MaterialSlotCount { get; private set; }
        internal int BoneCount { get; private set; }
        internal bool Exists { get; private set; }

        internal static FbxReplacementRendererSignature Create(Renderer renderer)
        {
            if (renderer == null)
            {
                return Empty;
            }

            var signature = new FbxReplacementRendererSignature
            {
                Exists = true,
                RendererTypeName = renderer.GetType().Name,
                MaterialSlotCount = renderer.sharedMaterials != null ? renderer.sharedMaterials.Length : 0,
                MeshName = ResolveMeshName(renderer),
                BoneCount = renderer is SkinnedMeshRenderer skinnedMeshRenderer && skinnedMeshRenderer.bones != null
                    ? skinnedMeshRenderer.bones.Length
                    : 0
            };

            return signature;
        }

        private static string ResolveMeshName(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
            {
                return skinnedMeshRenderer.sharedMesh != null ? skinnedMeshRenderer.sharedMesh.name : string.Empty;
            }

            if (renderer is MeshRenderer)
            {
                var meshFilter = renderer.GetComponent<MeshFilter>();
                return meshFilter != null && meshFilter.sharedMesh != null ? meshFilter.sharedMesh.name : string.Empty;
            }

            return string.Empty;
        }
    }

    internal sealed class FbxReplacementNodeMatch
    {
        internal FbxReplacementNodeMatch(
            FbxReplacementNodeSnapshot referenceNode,
            FbxReplacementNodeSnapshot targetNode,
            float score,
            bool isMeshAnchor,
            string reason)
        {
            ReferenceNode = referenceNode;
            TargetNode = targetNode;
            Score = score;
            IsMeshAnchor = isMeshAnchor;
            Reason = reason;
        }

        internal FbxReplacementNodeSnapshot ReferenceNode { get; }
        internal FbxReplacementNodeSnapshot TargetNode { get; }
        internal float Score { get; }
        internal bool IsMeshAnchor { get; }
        internal string Reason { get; }
    }

}
