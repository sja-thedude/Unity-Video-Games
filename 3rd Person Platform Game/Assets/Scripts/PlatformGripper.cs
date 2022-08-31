using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// This script is used for gripping the charater on the moving platform

public class PlatformGripper : MonoBehaviour
{
    public GameObject theLedge;
    public GameObject thePlayer;

    void OnTriggerEnter()
    {
        thePlayer.transform.parent = theLedge.transform;
    }

    void OnTriggerExit()
    {
        thePlayer.transform.parent = null;
    }
}
