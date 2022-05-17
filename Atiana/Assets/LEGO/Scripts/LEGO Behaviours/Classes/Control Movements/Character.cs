using LEGOModelImporter;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Unity.LEGO.Behaviours.Controls
{
    public class Character : ControlMovement
    {
        readonly Vector3 k_Gravity = new Vector3(0.0f, -40.0f, 0.0f);

        const float k_VelocityBounceAmplification = 1.5f;
        const float k_RotationBounceRestitution = 1.0f;

        const float k_CollisionAngle = 45.0f; // The character will only bounce when the angle between the collision direction and up is more than this.
        const float k_SlideAngle = 55.0f; // The character will slide when the angle between the surface normal and up is more than this.
        const float k_JumpImpulse = 20.0f;
        const float k_MaxBumpHeight = 1.2f; // The character can go over bumps below this height.
        const float k_BumpAnimationRatio = 0.33f;

        const float k_BumpEpsilon = 0.01f;

        const float k_AnimationDuration = 0.4f;
        const float k_AnimationScale = 0.1f;

        HashSet<Brick> m_Bricks;

        Bounds m_Bounds;
        LayerMask m_LayerMask;

        List<Vector3> m_LocalGroundingCheckPoints = new List<Vector3>();
        List<Vector3> m_WorldGroundingCheckPoints = new List<Vector3>();

        Collider[] m_GroundingColliders = new Collider[32];
        RaycastHit[] m_GroundingRaycastHits = new RaycastHit[32];

        Transform m_GroundedTransform;
        Vector3 m_GroundedSurfaceNormal;
        Vector3 m_GroundedLocalPosition;
        Vector3 m_OldGroundedPosition;
        Quaternion m_OldGroundedRotation;

        Vector3 m_FallVelocity;

        float m_RotationSpeed;

        Vector3 m_AnimationPivot;
        bool m_Animating;
        float m_AnimationTime;

        List<MeshRenderer> m_ScopedPartRenderers = new List<MeshRenderer>();
        List<Shader> m_OriginalShaders = new List<Shader>();
        List<Material> m_Materials = new List<Material>();

        static readonly int s_DeformMatrix1ID = Shader.PropertyToID("_DeformMatrix1");
        static readonly int s_DeformMatrix2ID = Shader.PropertyToID("_DeformMatrix2");
        static readonly int s_DeformMatrix3ID = Shader.PropertyToID("_DeformMatrix3");

        void Start()
        {
            m_LayerMask = LayerMask.GetMask("Environment") | LayerMask.GetMask("Default");

            FindLocalGroundingCheckPoints();

            UpdateGrounding(k_MaxBumpHeight, true);
        }

        public override void Setup(ModelGroup group, HashSet<Brick> bricks, List<MeshRenderer> scopedPartRenderers, Vector3 brickPivotOffset, Bounds scopedBounds, bool cameraAlignedRotation, bool cameraRelativeMovement)
        {
            base.Setup(group, bricks, scopedPartRenderers, brickPivotOffset, scopedBounds, cameraAlignedRotation, cameraRelativeMovement);

            m_Bricks = bricks;
            m_ScopedPartRenderers = scopedPartRenderers;
            m_Bounds = scopedBounds;

            // Change the shader of all scoped part renderers if not already changed.
            foreach (var partRenderer in m_ScopedPartRenderers)
            {
                if (partRenderer.sharedMaterial.shader.name != "Deformed")
                {
                    m_OriginalShaders.Add(partRenderer.sharedMaterial.shader);

                    // The renderQueue value is reset when changing the shader, so transfer it.
                    var renderQueue = partRenderer.material.renderQueue;
                    partRenderer.material.shader = Shader.Find("Deformed");
                    partRenderer.material.renderQueue = renderQueue;
                }

                m_Materials.Add(partRenderer.material);
            }

            m_AnimationPivot = transform.InverseTransformVector(new Vector3(m_Bounds.center.x, m_Bounds.min.y, m_Bounds.center.z) - transform.position);
        }

        public override void Movement(Vector3 targetDirection, float minSpeed, float maxSpeed, float idleSpeed)
        {
            var topSpeed = Mathf.Max(Mathf.Abs(minSpeed), Mathf.Abs(maxSpeed));

            // Compute target velocity.
            var targetVelocity = Vector3.zero;

            // Make the target direction follow the surface if moving away from it.
            if (m_GroundedTransform && Vector3.Dot(targetDirection, m_GroundedSurfaceNormal) > 0.0f)
            {
                targetDirection = Vector3.ProjectOnPlane(targetDirection, m_GroundedSurfaceNormal);
                targetDirection.Normalize();
            }

            var projectedTargetDirection = Vector3.Project(targetDirection, transform.forward);
            var sideDirection = targetDirection - projectedTargetDirection;

            if (Vector3.Dot(projectedTargetDirection, transform.forward) < 0.0f && (m_CameraAlignedRotation || !m_CameraRelativeMovement)) // If steering backwards and not a direct input type.
            {
                targetVelocity += projectedTargetDirection * (idleSpeed - minSpeed);
            }
            else
            {
                targetVelocity += projectedTargetDirection * (maxSpeed - idleSpeed);
            }
            targetVelocity += transform.forward * idleSpeed;
            targetVelocity += sideDirection * topSpeed;
            targetVelocity *= LEGOBehaviour.LEGOHorizontalModule;

            // Acceleration.
            var acceleration = topSpeed * 2.0f;
            m_Velocity = Acceleration(targetVelocity, m_Velocity, acceleration);
            m_CollisionVelocity = Acceleration(Vector3.zero, m_CollisionVelocity, acceleration);

            // Slide down inclined surfaces.
            if (m_GroundedTransform && Vector3.Angle(m_GroundedSurfaceNormal, Vector3.up) > k_SlideAngle)
            {
                m_Velocity += Vector3.ProjectOnPlane(k_Gravity * Time.deltaTime, m_GroundedSurfaceNormal);
            }

            if (m_GroundedTransform && Input.GetButtonDown("Jump"))
            {
                // Jump.
                PlayAnimation();
                m_FallVelocity = Vector3.up * k_JumpImpulse;
                m_GroundedTransform = null;
            }

            // Move bricks.
            m_Group.transform.position += (m_Velocity + m_FallVelocity + m_CollisionVelocity) * Time.deltaTime;

            // Update points used to perform grounding checks.
            for (var i = 0; i < m_WorldGroundingCheckPoints.Count; i++)
            {
                m_WorldGroundingCheckPoints[i] = transform.TransformPoint(m_LocalGroundingCheckPoints[i]);
                m_WorldGroundingCheckPoints[i] += Vector3.up * k_MaxBumpHeight;
            }

            // Update grounding.
            if (!m_GroundedTransform)
            {
                m_FallVelocity += k_Gravity * Time.deltaTime;

                UpdateGrounding(-m_FallVelocity.y * Time.deltaTime + Mathf.Epsilon, false);
                if (m_GroundedTransform)
                {
                    // Land.
                    PlayAnimation();
                    m_FallVelocity = Vector3.zero;
                }
            }
            else
            {
                UpdateGrounding(k_MaxBumpHeight, true);
            }

            // Animation.
            if (m_Animating)
            {
                m_AnimationTime += Time.deltaTime;

                var clampedTime = Mathf.Min(1.0f, m_AnimationTime / k_AnimationDuration);
                Vector3 scale;
                scale.x = 1.0f + Mathf.Sin(clampedTime * Mathf.PI) * k_AnimationScale;
                scale.y = 1.0f - Mathf.Sin(clampedTime * Mathf.PI) * k_AnimationScale;
                scale.z = 1.0f + Mathf.Sin(clampedTime * Mathf.PI) * k_AnimationScale;

                var worldPivot = transform.position + transform.TransformVector(m_AnimationPivot);
                var deformMatrix = Matrix4x4.Translate(worldPivot) * Matrix4x4.Scale(scale) * Matrix4x4.Translate(-worldPivot);

                foreach (var material in m_Materials)
                {
                    material.SetVector(s_DeformMatrix1ID, deformMatrix.GetRow(0));
                    material.SetVector(s_DeformMatrix2ID, deformMatrix.GetRow(1));
                    material.SetVector(s_DeformMatrix3ID, deformMatrix.GetRow(2));
                }

                if (m_AnimationTime >= k_AnimationDuration)
                {
                    m_Animating = false;
                }
            }
        }

        public override void Rotation(Vector3 targetDirection, float rotationSpeed)
        {
            var worldPivot = transform.position + transform.TransformVector(m_BrickPivotOffset);

            RotationBounce(worldPivot, Vector3.up);

            float angleDiff;
            var pointingDirection = new Vector3(transform.forward.x, 0.0f, transform.forward.z);

            if (m_CameraAlignedRotation) // Strafe
            {
                var forwardXZ = new Vector3(m_MainCamera.transform.forward.x, 0.0f, m_MainCamera.transform.forward.z);
                angleDiff = Vector3.SignedAngle(pointingDirection, forwardXZ, Vector3.up);
            }
            else if (m_CameraRelativeMovement) // Direct
            {
                var forwardXZ = new Vector3(targetDirection.x, 0.0f, targetDirection.z);
                angleDiff = Vector3.SignedAngle(pointingDirection, forwardXZ, Vector3.up);
            }
            else // Tank
            {
                angleDiff = Input.GetAxisRaw("Horizontal") * rotationSpeed;
            }

            if (angleDiff < 0.0f)
            {
                rotationSpeed = -rotationSpeed;
            }

            // Assumes that x > NaN is false - otherwise we need to guard against Time.deltaTime being zero.
            if (Mathf.Abs(rotationSpeed) > Mathf.Abs(angleDiff) / Time.deltaTime)
            {
                rotationSpeed = angleDiff / Time.deltaTime;
            }

            m_RotationSpeed = rotationSpeed;

            // Rotate bricks.
            m_Group.transform.RotateAround(worldPivot, Vector3.up, rotationSpeed * Time.deltaTime);
        }

        public override void Collision(Vector3 direction)
        {
            // Do not bounce if collision came from below.
            if (Vector3.Angle(direction, Vector3.up) > k_CollisionAngle)
            {
                if (Vector3.Dot(m_Velocity, direction) < 0.0f)
                {
                    m_CollisionVelocity = -Vector3.Project(m_Velocity, direction) * 2.0f;
                }
                else
                {
                    m_CollisionVelocity = -m_Velocity * 2.0f;
                }

                m_CollisionVelocity += direction * k_VelocityBounceAmplification;

                if (Mathf.Abs(m_RotationSpeed) > 0.0f)
                {
                    m_RotationBounceAngle = -m_RotationSpeed * k_RotationBounceRestitution;
                    m_RotationBounceDamping = 1.0f;
                }
            }
        }

        void FixedUpdate()
        {
            // Apply external motion and rotation.
            if (m_GroundedTransform)
            {
                var newGroundedPosition = m_GroundedTransform.TransformPoint(m_GroundedLocalPosition);
                m_Group.transform.position += newGroundedPosition - m_OldGroundedPosition;
                m_OldGroundedPosition = newGroundedPosition;

                var newGroundedRotation = m_GroundedTransform.rotation;
                // Breaks down if rotating more than 180 degrees per fixed update.
                var diffRotation = newGroundedRotation * Quaternion.Inverse(m_OldGroundedRotation);
                var rotatedRight = diffRotation * Vector3.right;
                rotatedRight.y = 0.0f;
                if (rotatedRight.magnitude > 0.0f)
                {
                    rotatedRight.Normalize();
                    m_Group.transform.RotateAround(m_OldGroundedPosition, Vector3.up, Vector3.SignedAngle(Vector3.right, rotatedRight, Vector3.up));
                }
                m_OldGroundedRotation = newGroundedRotation;
            }
        }

        void PlayAnimation()
        {
            m_AnimationTime = 0.0f;
            m_Animating = true;
        }

        void FindLocalGroundingCheckPoints()
        {
            var overlappingColliders = Physics.OverlapBox(new Vector3(m_Bounds.center.x, m_Bounds.min.y + k_MaxBumpHeight * 0.5f, m_Bounds.center.z),
                new Vector3(m_Bounds.extents.x, k_MaxBumpHeight * 0.5f, m_Bounds.extents.z), Quaternion.identity, -1, QueryTriggerInteraction.Ignore);

            foreach (var collider in overlappingColliders)
            {
                var brick = collider.GetComponentInParent<Brick>();
                if (m_Bricks.Contains(brick))
                {
                    var colliderType = collider.GetType();
                    if (colliderType == typeof(BoxCollider))
                    {
                        var boxCollider = (BoxCollider)collider;
                        var colliderSize = new Vector3(boxCollider.size.x + 0.1f, boxCollider.size.y, boxCollider.size.z + 0.1f);
                        m_LocalGroundingCheckPoints.Add(transform.InverseTransformPoint(boxCollider.transform.TransformPoint(boxCollider.center + Vector3.Scale(new Vector3(-0.5f, -0.5f, -0.5f), colliderSize))));
                        m_LocalGroundingCheckPoints.Add(transform.InverseTransformPoint(boxCollider.transform.TransformPoint(boxCollider.center + Vector3.Scale(new Vector3(-0.5f, -0.5f, 0.5f), colliderSize))));
                        m_LocalGroundingCheckPoints.Add(transform.InverseTransformPoint(boxCollider.transform.TransformPoint(boxCollider.center + Vector3.Scale(new Vector3(-0.5f, 0.5f, -0.5f), colliderSize))));
                        m_LocalGroundingCheckPoints.Add(transform.InverseTransformPoint(boxCollider.transform.TransformPoint(boxCollider.center + Vector3.Scale(new Vector3(-0.5f, 0.5f, 0.5f), colliderSize))));
                        m_LocalGroundingCheckPoints.Add(transform.InverseTransformPoint(boxCollider.transform.TransformPoint(boxCollider.center + Vector3.Scale(new Vector3(0.5f, -0.5f, -0.5f), colliderSize))));
                        m_LocalGroundingCheckPoints.Add(transform.InverseTransformPoint(boxCollider.transform.TransformPoint(boxCollider.center + Vector3.Scale(new Vector3(0.5f, -0.5f, 0.5f), colliderSize))));
                        m_LocalGroundingCheckPoints.Add(transform.InverseTransformPoint(boxCollider.transform.TransformPoint(boxCollider.center + Vector3.Scale(new Vector3(0.5f, 0.5f, -0.5f), colliderSize))));
                        m_LocalGroundingCheckPoints.Add(transform.InverseTransformPoint(boxCollider.transform.TransformPoint(boxCollider.center + Vector3.Scale(new Vector3(0.5f, 0.5f, 0.5f), colliderSize))));
                    }
                    else if (colliderType == typeof(SphereCollider))
                    { 
                        var sphereCollider = (SphereCollider)collider;
                        sphereCollider.radius += 0.1f;
                        m_LocalGroundingCheckPoints.Add(transform.InverseTransformPoint(sphereCollider.transform.TransformPoint(sphereCollider.center) + new Vector3(1.0f, 0.0f, 0.0f) * sphereCollider.radius));
                        m_LocalGroundingCheckPoints.Add(transform.InverseTransformPoint(sphereCollider.transform.TransformPoint(sphereCollider.center) + new Vector3(-1.0f, 0.0f, 0.0f) * sphereCollider.radius));
                        m_LocalGroundingCheckPoints.Add(transform.InverseTransformPoint(sphereCollider.transform.TransformPoint(sphereCollider.center) + new Vector3(0.0f, 1.0f, 0.0f) * sphereCollider.radius));
                        m_LocalGroundingCheckPoints.Add(transform.InverseTransformPoint(sphereCollider.transform.TransformPoint(sphereCollider.center) + new Vector3(0.0f, -1.0f, 0.0f) * sphereCollider.radius));
                        m_LocalGroundingCheckPoints.Add(transform.InverseTransformPoint(sphereCollider.transform.TransformPoint(sphereCollider.center) + new Vector3(0.0f, 0.0f, 1.0f) * sphereCollider.radius));
                        m_LocalGroundingCheckPoints.Add(transform.InverseTransformPoint(sphereCollider.transform.TransformPoint(sphereCollider.center) + new Vector3(0.0f, 0.0f, -1.0f) * sphereCollider.radius));
                    }
                }
            }

            // Remove points higher than max bump height.
            var minPoint = transform.InverseTransformPoint(m_Bounds.min).y + k_MaxBumpHeight;
            m_LocalGroundingCheckPoints = m_LocalGroundingCheckPoints.Where(localPoint => localPoint.y < minPoint).ToList();

            foreach (var localPoint in m_LocalGroundingCheckPoints)
            {
                var worldPoint = transform.TransformPoint(localPoint);
                worldPoint += Vector3.up * k_MaxBumpHeight;
                m_WorldGroundingCheckPoints.Add(worldPoint);
            }
        }

        void UpdateGrounding(float rayExtension, bool bump)
        {
            var minDistance = float.MaxValue;

            foreach (var point in m_WorldGroundingCheckPoints)
            {
                int numColliders = Physics.OverlapSphereNonAlloc(point, Mathf.Epsilon, m_GroundingColliders, m_LayerMask, QueryTriggerInteraction.Ignore);
                var ignorePoint = false;

                for (var i = 0; i < numColliders; ++i)
                {
                    if (!m_Bricks.Contains(m_GroundingColliders[i].transform.GetComponentInParent<Brick>()))
                    {
                        ignorePoint = true;
                        break;
                    }
                }

                if (!ignorePoint)
                {
                    var numHits = Physics.RaycastNonAlloc(point, Vector3.down, m_GroundingRaycastHits, k_MaxBumpHeight + rayExtension, m_LayerMask, QueryTriggerInteraction.Ignore);

                    for (var i = 0; i < numHits; ++i)
                    {
                        var hit = m_GroundingRaycastHits[i];
                        if (!m_Bricks.Contains(hit.transform.GetComponentInParent<Brick>()))
                        {
                            if (hit.distance < minDistance)
                            {
                                minDistance = hit.distance;
                                m_GroundedSurfaceNormal = hit.normal;
                                if (hit.collider.transform != m_GroundedTransform)
                                {
                                    m_GroundedTransform = hit.collider.transform;
                                    m_OldGroundedPosition = hit.point;
                                    m_GroundedLocalPosition = m_GroundedTransform.InverseTransformPoint(m_OldGroundedPosition);
                                    m_OldGroundedRotation = m_GroundedTransform.rotation;
                                }
                            }
                        }
                    }
                }
            }

            if (minDistance < float.MaxValue)
            {
                if (bump)
                {
                    Bump(k_MaxBumpHeight - minDistance);
                }
            }
            else
            {
                m_GroundedTransform = null;
            }
        }

        void Bump(float distance)
        {
            if (Mathf.Abs(distance) < k_MaxBumpHeight)
            {
                // Bump the character.
                m_Group.transform.position += Vector3.up * (distance + k_BumpEpsilon);

                // Play animation if bump was more than some amount of the maximum bump height.
                if (Mathf.Abs(distance) > k_MaxBumpHeight * k_BumpAnimationRatio) 
                {
                    PlayAnimation();
                }
            }
        }

        void OnDestroy()
        {
            // Check if the original materials have been stored for all scoped part renderers.
            if (m_ScopedPartRenderers.Count > m_OriginalShaders.Count)
            {
                return;
            }

            // Change the material back to original for all scoped part renderers.
            for (var i = 0; i < m_ScopedPartRenderers.Count; ++i)
            {
                if (m_ScopedPartRenderers[i])
                {
                    var partRenderer = m_ScopedPartRenderers[i];
                    // The renderQueue value is reset when changing the shader, so transfer it.
                    var renderQueue = partRenderer.material.renderQueue;
                    partRenderer.material.shader = m_OriginalShaders[i];
                    partRenderer.material.renderQueue = renderQueue;
                }
            }
        }
    }
}
