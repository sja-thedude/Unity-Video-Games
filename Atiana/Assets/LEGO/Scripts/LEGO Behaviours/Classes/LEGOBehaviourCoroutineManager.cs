using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.LEGO.Behaviours
{
    public class LEGOBehaviourCoroutineManager : MonoBehaviour
    {
        static LEGOBehaviourCoroutineManager m_Instance;
        static readonly Dictionary<Object, Dictionary<string, Coroutine>> s_ExistingCoroutines = new Dictionary<Object, Dictionary<string, Coroutine>>();

        public static void StartCoroutine(Object owner, string key, IEnumerator coroutine, bool stopExisting = false)
        {
            if (m_Instance)
            {
                if (stopExisting && s_ExistingCoroutines.ContainsKey(owner))
                {
                    if (s_ExistingCoroutines[owner].ContainsKey(key))
                    {
                        m_Instance.StopCoroutine(s_ExistingCoroutines[owner][key]);
                    }
                }

                if (s_ExistingCoroutines.ContainsKey(owner))
                {
                    s_ExistingCoroutines[owner].Remove(key);
                }

                if (!s_ExistingCoroutines.ContainsKey(owner))
                {
                    s_ExistingCoroutines.Add(owner, new Dictionary<string, Coroutine>());
                }

                s_ExistingCoroutines[owner].Add(key, m_Instance.StartCoroutine(coroutine));
            }
        }

        void Awake()
        {
            if (m_Instance && m_Instance != this)
            {
                Destroy(this);
            }
            else
            {
                m_Instance = this;
            }
        }
    }
}
