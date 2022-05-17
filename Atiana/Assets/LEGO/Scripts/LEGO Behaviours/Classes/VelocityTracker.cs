using UnityEngine;

namespace Unity.LEGO.Behaviours
{
    public class VelocityTracker : MonoBehaviour
    {
        Vector3 m_lastPosition;
        Quaternion m_lastRotation;
        Vector3 m_velocity;
        Vector3 m_angularVelocity;

        public Vector3 GetVelocity()
        {
            return m_velocity;
        }

        public Vector3 GetAngularVelocity()
        {
            return m_angularVelocity;
        }

        void Start()
        {
            m_lastPosition = transform.position;
            m_lastRotation = transform.rotation;
        }

        void Update()
        {
            float magnitude;
            Vector3 axis;
            Quaternion deltaRotation = transform.rotation * Quaternion.Inverse(m_lastRotation);
            deltaRotation.ToAngleAxis(out magnitude, out axis);

            m_velocity = (transform.position - m_lastPosition) / Time.deltaTime;
            m_angularVelocity = (magnitude * axis) * Mathf.Deg2Rad / Time.deltaTime;
            m_lastPosition = transform.position;
            m_lastRotation = transform.rotation;
        }
    }
}
