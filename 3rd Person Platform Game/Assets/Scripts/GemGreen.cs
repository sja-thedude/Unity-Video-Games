using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// This script is for calculating gem points on the game, playing sound on collect and destroying after taking it

public class GemGreen : MonoBehaviour
{
    public GameObject scoreBox;
    public AudioSource collectSound;

    void OnTriggerEnter()
    {
        GlobalScore.currentScore += 100;
        collectSound.Play();
        Destroy(gameObject);
    }
}
