using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// This script is for displaying the score on screen during the level

public class GlobalScore : MonoBehaviour
{
    public GameObject scoreBox;
    public static int currentScore;
    public int internalScore;

    void Update()
    {
        internalScore = currentScore;
        scoreBox.GetComponent<Text>().text = "" + internalScore;
    }
}
