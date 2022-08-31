using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// This script is to redirect scene

public class RedirectToLevel : MonoBehaviour
{
    public static int redirectToLevel;
    public static int nextLevel;

    void Update()
    {
        if (redirectToLevel == 3)
        {
            SceneManager.LoadScene(redirectToLevel);
        }
    }
}
