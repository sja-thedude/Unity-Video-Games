using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// This script is for rotating the the background scene

public class RotateSky : MonoBehaviour
{
    public float rotateSpeed = 3.5f;
    
    void Update()
    {
        RenderSettings.skybox.SetFloat("_Rotation", Time.time * rotateSpeed);
    }
}
