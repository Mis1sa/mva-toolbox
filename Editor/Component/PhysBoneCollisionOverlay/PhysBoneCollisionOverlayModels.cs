using UnityEngine;

namespace MVA.Toolbox.PhysBoneCollisionOverlay
{
    internal enum PhysBoneColliderShapeKind
    {
        Sphere,
        Capsule,
        Plane
    }

    internal readonly struct PhysBoneBoneSegment
    {
        internal readonly Vector3 Start;
        internal readonly Vector3 End;
        internal readonly float StartNormalized;
        internal readonly float EndNormalized;

        internal PhysBoneBoneSegment(Vector3 start, Vector3 end, float startNormalized, float endNormalized)
        {
            Start = start;
            End = end;
            StartNormalized = startNormalized;
            EndNormalized = endNormalized;
        }
    }

    internal readonly struct PhysBoneRawBoneSegment
    {
        internal readonly Vector3 Start;
        internal readonly Vector3 End;
        internal readonly float StartDistance;
        internal readonly float EndDistance;

        internal PhysBoneRawBoneSegment(Vector3 start, Vector3 end, float startDistance, float endDistance)
        {
            Start = start;
            End = end;
            StartDistance = startDistance;
            EndDistance = endDistance;
        }
    }

    internal readonly struct PhysBoneColliderShape
    {
        internal readonly PhysBoneColliderShapeKind Kind;
        internal readonly Vector3 Center;
        internal readonly Vector3 PointA;
        internal readonly Vector3 PointB;
        internal readonly Vector3 Normal;
        internal readonly float Radius;

        internal PhysBoneColliderShape(
            PhysBoneColliderShapeKind kind,
            Vector3 center,
            Vector3 pointA,
            Vector3 pointB,
            Vector3 normal,
            float radius)
        {
            Kind = kind;
            Center = center;
            PointA = pointA;
            PointB = pointB;
            Normal = normal;
            Radius = radius;
        }
    }
}
