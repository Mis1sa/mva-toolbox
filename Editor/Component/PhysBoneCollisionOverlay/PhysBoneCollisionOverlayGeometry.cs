using UnityEngine;

namespace MVA.Toolbox.PhysBoneCollisionOverlay
{
    internal static class PhysBoneCollisionOverlayGeometry
    {
        internal static bool IsSegmentColliding(Vector3 start, Vector3 end, float startRadius, float endRadius, PhysBoneColliderShape shape)
        {
            int steps = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(start, end) / 0.02f), 2, 12);
            Vector3 subStart = start;
            float subStartRadius = Mathf.Max(0f, startRadius);

            for (int step = 1; step <= steps; step++)
            {
                float t = step / (float)steps;
                Vector3 subEnd = Vector3.Lerp(start, end, t);
                float subEndRadius = Mathf.Lerp(startRadius, endRadius, t);
                float subRadius = Mathf.Max(0f, Mathf.Max(subStartRadius, subEndRadius));

                if (IsSimpleSegmentColliding(subStart, subEnd, subRadius, shape))
                {
                    return true;
                }

                subStart = subEnd;
                subStartRadius = subEndRadius;
            }

            return false;
        }

        internal static bool TryBuildColliderShape(Component collider, out PhysBoneColliderShape shape)
        {
            shape = default;
            if (collider == null)
            {
                return false;
            }

            Transform rootTransform = PhysBoneCollisionOverlaySerializedAccess.ReadObjectReference<Transform>(collider, "rootTransform");
            if (rootTransform == null)
            {
                rootTransform = collider.transform;
            }

            if (rootTransform == null)
            {
                return false;
            }

            int shapeType = PhysBoneCollisionOverlaySerializedAccess.ReadInt(collider, 0, "shapeType");
            int direction = PhysBoneCollisionOverlaySerializedAccess.ReadInt(collider, 1, "direction", "_direction", "axis");
            float radius = Mathf.Max(0f, PhysBoneCollisionOverlaySerializedAccess.ReadFloat(collider, 0f, "radius"));
            float height = Mathf.Max(0f, PhysBoneCollisionOverlaySerializedAccess.ReadFloat(collider, 0f, "height"));
            Vector3 centerOffset = PhysBoneCollisionOverlaySerializedAccess.ReadVector3(collider, Vector3.zero, "position");
            Quaternion localRotation = PhysBoneCollisionOverlaySerializedAccess.ReadQuaternion(collider, Quaternion.identity, "rotation");

            Vector3 localAxis;
            if (direction == 0)
            {
                localAxis = Vector3.right;
            }
            else if (direction == 2)
            {
                localAxis = Vector3.forward;
            }
            else
            {
                localAxis = Vector3.up;
            }

            Vector3 rotatedLocalAxis = localRotation * localAxis;
            Vector3 worldCenter = rootTransform.TransformPoint(centerOffset);
            Vector3 worldAxis = rootTransform.TransformDirection(rotatedLocalAxis);
            if (worldAxis.sqrMagnitude <= 1e-10f)
            {
                worldAxis = rootTransform.up;
            }

            worldAxis.Normalize();

            float radiusScale = Mathf.Max(Mathf.Abs(rootTransform.lossyScale.x), Mathf.Abs(rootTransform.lossyScale.y), Mathf.Abs(rootTransform.lossyScale.z));
            float axisScale = rootTransform.TransformVector(rotatedLocalAxis).magnitude;
            if (axisScale <= 1e-8f)
            {
                axisScale = 1f;
            }

            float worldRadius = radius * radiusScale;
            float worldHeight = height * axisScale;

            if (shapeType == 2)
            {
                shape = new PhysBoneColliderShape(PhysBoneColliderShapeKind.Plane, worldCenter, Vector3.zero, Vector3.zero, worldAxis, 0f);
                return true;
            }

            bool useCapsule = shapeType == 1 || worldHeight > worldRadius * 2f;
            if (!useCapsule)
            {
                shape = new PhysBoneColliderShape(PhysBoneColliderShapeKind.Sphere, worldCenter, Vector3.zero, Vector3.zero, Vector3.zero, worldRadius);
                return true;
            }

            float halfLine = Mathf.Max(0f, worldHeight * 0.5f - worldRadius);
            Vector3 pointA = worldCenter + worldAxis * halfLine;
            Vector3 pointB = worldCenter - worldAxis * halfLine;
            shape = new PhysBoneColliderShape(PhysBoneColliderShapeKind.Capsule, worldCenter, pointA, pointB, Vector3.zero, worldRadius);
            return true;
        }

        private static bool IsSimpleSegmentColliding(Vector3 start, Vector3 end, float boneRadius, PhysBoneColliderShape shape)
        {
            float totalRadius = boneRadius + shape.Radius;

            if (shape.Kind == PhysBoneColliderShapeKind.Sphere)
            {
                float distanceSquared = DistancePointToSegmentSquared(shape.Center, start, end);
                if (distanceSquared <= totalRadius * totalRadius)
                {
                    return true;
                }
            }
            else if (shape.Kind == PhysBoneColliderShapeKind.Capsule)
            {
                float distanceSquared = DistanceSegmentToSegmentSquared(start, end, shape.PointA, shape.PointB);
                if (distanceSquared <= totalRadius * totalRadius)
                {
                    return true;
                }
            }
            else
            {
                float d0 = Vector3.Dot(start - shape.Center, shape.Normal);
                float d1 = Vector3.Dot(end - shape.Center, shape.Normal);
                if (Mathf.Abs(d0) <= boneRadius || Mathf.Abs(d1) <= boneRadius || d0 * d1 <= 0f)
                {
                    return true;
                }
            }

            return false;
        }

        private static float DistancePointToSegmentSquared(Vector3 point, Vector3 segmentStart, Vector3 segmentEnd)
        {
            Vector3 segment = segmentEnd - segmentStart;
            float segmentLengthSquared = Vector3.Dot(segment, segment);
            if (segmentLengthSquared <= 1e-12f)
            {
                return Vector3.SqrMagnitude(point - segmentStart);
            }

            float t = Vector3.Dot(point - segmentStart, segment) / segmentLengthSquared;
            t = Mathf.Clamp01(t);
            Vector3 projection = segmentStart + segment * t;
            return Vector3.SqrMagnitude(point - projection);
        }

        private static float DistanceSegmentToSegmentSquared(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            const float epsilon = 1e-8f;
            Vector3 d1 = q1 - p1;
            Vector3 d2 = q2 - p2;
            Vector3 r = p1 - p2;

            float a = Vector3.Dot(d1, d1);
            float e = Vector3.Dot(d2, d2);
            float f = Vector3.Dot(d2, r);

            float s;
            float t;

            if (a <= epsilon && e <= epsilon)
            {
                return Vector3.SqrMagnitude(p1 - p2);
            }

            if (a <= epsilon)
            {
                s = 0f;
                t = Mathf.Clamp01(f / e);
            }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= epsilon)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else
                {
                    float b = Vector3.Dot(d1, d2);
                    float denominator = a * e - b * b;

                    if (denominator != 0f)
                    {
                        s = Mathf.Clamp01((b * f - c * e) / denominator);
                    }
                    else
                    {
                        s = 0f;
                    }

                    t = (b * s + f) / e;

                    if (t < 0f)
                    {
                        t = 0f;
                        s = Mathf.Clamp01(-c / a);
                    }
                    else if (t > 1f)
                    {
                        t = 1f;
                        s = Mathf.Clamp01((b - c) / a);
                    }
                }
            }

            Vector3 c1 = p1 + d1 * s;
            Vector3 c2 = p2 + d2 * t;
            return Vector3.SqrMagnitude(c1 - c2);
        }
    }
}
