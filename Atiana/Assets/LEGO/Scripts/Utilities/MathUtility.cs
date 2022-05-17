using UnityEngine;

namespace Unity.LEGO.Utilities
{
    public static class MathUtility
    {
        public static Vector3 QuadraticBezier(Vector3 start, Vector3 middle, Vector3 end, float time)
        {
            return (1.0f - time) * (1.0f - time) * start + 2.0f * (1.0f - time) * time * middle + time * time * end;
        }

        public static float EaseOutQuadratic(float time, float duration)
        {
            var x = Mathf.Clamp01(time / duration);
            return 1 - (1 - x) * (1 - x);
        }

        public static float EaseOutSine(float time, float duration)
        {
            var x = Mathf.Clamp01(time / duration);
            return Mathf.Sin(x * (Mathf.PI / 2.0f));
        }

        public static float EaseOutBack(float time, float duration)
        {
            var x = Mathf.Clamp01(time / duration);
            return 1.0f + 2.70158f * (x - 1.0f) * (x - 1.0f) * (x - 1.0f) + 1.70158f * (x - 1.0f) * (x - 1.0f);
        }

        public static Quaternion ComputeHorizontalRotationDelta(Vector3 currentDirection, Vector3 desiredTargetDirection, float rotationSpeed)
        {
            // Project directions onto X-Z plane.
            desiredTargetDirection.y = 0.0f;
            currentDirection.y = 0.0f;

            // When directions are completely opposite, the rotation axis is not well defined.
            // Try to make it well defined, by rotating the target direction slightly.
            if (Vector3.Angle(currentDirection, desiredTargetDirection) > 179.0f)
            {
                desiredTargetDirection = Quaternion.Euler(0.0f, 0.1f, 0.0f) * desiredTargetDirection;
            }

            var desiredRotationDelta = Quaternion.FromToRotation(currentDirection, desiredTargetDirection);
            return Quaternion.RotateTowards(Quaternion.identity, desiredRotationDelta, rotationSpeed * Time.deltaTime);
        }

        public static Quaternion ComputeVerticalRotationDelta(Vector3 currentDirection, Vector3 desiredTargetDirection, float rotationSpeed)
        {
            // Rotate desiredTargetDirection into plane defined by current right and world up.
            var currentDirectionInPlane = currentDirection;
            currentDirectionInPlane.y = 0.0f;
            var desiredTargetDirectionInPlane = desiredTargetDirection;
            desiredTargetDirectionInPlane.y = 0.0f;
            var angleDiffInPlane = Vector3.SignedAngle(desiredTargetDirectionInPlane, currentDirectionInPlane, Vector3.up);
            desiredTargetDirection = Quaternion.Euler(0.0f, angleDiffInPlane, 0.0f) * desiredTargetDirection;

            // When directions are completely opposite, the rotation axis is not well defined.
            // Try to make it well defined, by rotating the target direction slightly.
            if (Vector3.Angle(currentDirection, desiredTargetDirection) > 179.0f)
            {
                desiredTargetDirection = Quaternion.Euler(0.0f, 0.0f, 0.1f) * desiredTargetDirection;
            }

            var desiredRotationDelta = Quaternion.FromToRotation(currentDirection, desiredTargetDirection);
            return Quaternion.RotateTowards(Quaternion.identity, desiredRotationDelta, rotationSpeed * Time.deltaTime);
        }

        public static float SignedAngleFromPlaneProjection(Vector3 directionToProject, Vector3 inPlaneDirection, Vector3 planeNormal)
        {
            var projection = Vector3.ProjectOnPlane(directionToProject, planeNormal);
            return Vector3.SignedAngle(projection, inPlaneDirection, planeNormal);
        }

        public static bool IntersectRayAABB(Vector3 origin, Vector3 direction, Vector3 center, Vector3 size, out Vector3 result)
        {
            // Solution from:
            // https://www.scratchapixel.com/lessons/3d-basic-rendering/minimal-ray-tracer-rendering-simple-shapes/ray-box-intersection

            result = Vector3.zero;

            Vector3 min = center - 0.5f * size;
            Vector3 max = center + 0.5f * size;

            float t0 = (min.x - origin.x) / direction.x;
            float t1 = (max.x - origin.x) / direction.x;

            if (t0 > t1)
            {
                var temp = t0;
                t0 = t1;
                t1 = temp;
            }

            float tymin = (min.y - origin.y) / direction.y;
            float tymax = (max.y - origin.y) / direction.y;

            if (tymin > tymax)
            {
                var temp = tymin;
                tymin = tymax;
                tymax = temp;

            }

            if (t0 > tymax || tymin > t1)
            {
                return false;
            }

            if (tymin > t0)
            {
                t0 = tymin;
            }

            if (tymax < t1)
            {
                t1 = tymax;
            }

            float tzmin = (min.z - origin.z) / direction.z;
            float tzmax = (max.z - origin.z) / direction.z;

            if (tzmin > tzmax)
            {
                var temp = tzmin;
                tzmin = tzmax;
                tzmax = temp;
            }

            if (t0 > tzmax || tzmin > t1)
            {
                return false;
            }

            if (tzmin > t0)
            {
                t0 = tzmin;
            }

            if (tzmax < t1)
            {
                t1 = tzmax;
            }

            if (GetSmallestPositiveSolution(t0, t1, out var t))
            {
                result = origin + direction * t;

                return true;
            }

            return false;
        }

        public static bool IntersectRaySphere(Vector3 origin, Vector3 direction, Vector3 center, float radius, out Vector3 result)
        {
            // Geometric solution from:
            // https://www.scratchapixel.com/lessons/3d-basic-rendering/minimal-ray-tracer-rendering-simple-shapes/ray-sphere-intersection

            result = Vector3.zero;

            var L = center - origin;
            float tca = Vector3.Dot(L, direction);
            float d2 = Vector3.Dot(L, L) - tca * tca;
            if (d2 > radius * radius)
            {
                return false;
            }
            float thc = Mathf.Sqrt(radius * radius - d2);
            var t0 = tca - thc;
            var t1 = tca + thc;

            if (GetSmallestPositiveSolution(t0, t1, out var t))
            {
                result = origin + direction * t;

                return true;
            }

            return false;
        }

        static bool GetSmallestPositiveSolution(float t0, float t1, out float t)
        {
            if (t0 > t1)
            {
                var temp = t0;
                t0 = t1;
                t1 = temp;
            }

            if (t0 < 0)
            {
                t = t1;
                if (t < 0)
                {
                    return false;
                }
            }
            else
            {
                t = t0;
            }

            return true;
        }
    }
}
