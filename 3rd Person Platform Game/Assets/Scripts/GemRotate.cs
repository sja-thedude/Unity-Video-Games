using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// This script is for rotating the gem

public class GemRotate : MonoBehaviour
{
    public float rotateSpeed = 0.5f;
    
    void Update()
    {
        transform.Rotate(0, rotateSpeed * Time.timeScale, 0, Space.World);
    }
}
