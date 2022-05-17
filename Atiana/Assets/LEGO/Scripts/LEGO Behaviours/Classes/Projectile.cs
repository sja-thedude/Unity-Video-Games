using LEGOModelImporter;
using System.Collections.Generic;
using UnityEngine;
using Unity.LEGO.Game;
using Unity.LEGO.Minifig;

namespace Unity.LEGO.Behaviours
{
    [RequireComponent(typeof(Rigidbody))]
    public class Projectile : MonoBehaviour
    {
        [SerializeField, Range(0.0f, 1080.0f), Tooltip("The rotation speed in degrees per second.")]
        float m_RotationSpeed = 0.0f;

        public bool Deadly { get; private set; } = true;

        Rigidbody m_RigidBody;
        CapsuleCollider m_Collider;
        ParticleSystem m_ParticleSystem;
        bool m_Rotate;
        Vector3 m_Rotation;
        HashSet<Brick> m_ConnectedBricks;
        List<Collider> m_IgnoredColliders;
        bool m_Launched;

        public void Init(HashSet<Brick> connectedBricks, float velocity, bool useGravity, float time)
        {
            m_ConnectedBricks = connectedBricks;

            m_RigidBody.velocity = transform.forward * velocity;

            m_RigidBody.useGravity = useGravity;

            // Make sure to initially ignore all collisions with the connected bricks of the firing Shoot Action.
            m_IgnoredColliders = new List<Collider>();
            foreach(var brick in connectedBricks)
            {
                var colliders = brick.GetComponentsInChildren<Collider>();
                foreach(var collider in colliders)
                {
                    Physics.IgnoreCollision(m_Collider, collider, true);
                    m_IgnoredColliders.Add(collider);
                }
            }

            Destroy(gameObject, time);
        }

        void Awake()
        {
            m_Collider = GetComponent<CapsuleCollider>();

            // Disable the collider until the first update to avoid false collisions with CharacterController's OnControllerColliderHit.
            m_Collider.enabled = false;

            m_RigidBody = GetComponent<Rigidbody>();

            m_RigidBody.isKinematic = false;
            m_RigidBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            m_ParticleSystem = GetComponent<ParticleSystem>();
            m_ParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            if (m_RotationSpeed > 0.0f)
            {
                m_Rotation = Random.onUnitSphere * m_RotationSpeed;
                m_Rotate = true;
            }
        }

        void Update()
        {
            m_Collider.enabled = true;

            // Check if the projectile has been launched out of the firing Shoot Action.
            if (!m_Launched)
            {
                // Assumes that the capsule collider is aligned with local forward axis in projectile.
                var c0 = transform.TransformPoint(m_Collider.center - Vector3.forward * (m_Collider.height * 0.5f - m_Collider.radius));
                var c1 = transform.TransformPoint(m_Collider.center + Vector3.forward * (m_Collider.height * 0.5f - m_Collider.radius));
                var colliders = Physics.OverlapCapsule(c0, c1, m_Collider.radius, Physics.AllLayers, QueryTriggerInteraction.Ignore);
                var collisions = false;
                foreach (var collider in colliders)
                {
                    // Do not collide with self, minifigs, the connected bricks of the firing Shoot Action or colliders from other LEGOBehaviourColliders.
                    if (collider != m_Collider &&
                        !collider.GetComponent<MinifigController>() &&
                        m_ConnectedBricks.Contains(collider.GetComponentInParent<Brick>()) &&
                        !collider.GetComponent<LEGOBehaviourCollider>())
                    {
                        collisions = true;
                        break;
                    }
                }

                // Play launch particle effect when projectile is no longer colliding with anything.
                if (!collisions)
                {
                    m_ParticleSystem.Play();
                    m_Launched = true;
                }
            }

            if (Deadly)
            {
                if (m_Rotate)
                {
                    transform.Rotate(m_Rotation * Time.deltaTime);
                }
                else
                {
                    transform.rotation = Quaternion.LookRotation(m_RigidBody.velocity);
                }
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            // Check if the player was hit.
            if (Deadly && collision.collider.gameObject.CompareTag("Player"))
            {
                // If the player is a minifig or a brick, do an explosion.
                var minifigController = collision.collider.GetComponent<MinifigController>();
                if (minifigController)
                {
                    minifigController.Explode();
                }
                else
                {
                    var brick = collision.collider.GetComponentInParent<Brick>();
                    if (brick)
                    {
                        BrickExploder.ExplodeConnectedBricks(brick);
                    }
                }

                GameOverEvent evt = Events.GameOverEvent;
                evt.Win = false;
                EventManager.Broadcast(evt);
            }

            // Turn on gravity and make non-deadly.
            m_RigidBody.useGravity = true;
            Deadly = false;

            // Re-establish all collisions with ignored colliders.
            foreach (var collider in m_IgnoredColliders)
            {
                if (collider)
                {
                    Physics.IgnoreCollision(m_Collider, collider, false);
                }
            }
        }
    }
}
