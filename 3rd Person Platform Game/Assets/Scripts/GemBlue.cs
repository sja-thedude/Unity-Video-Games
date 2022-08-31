using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// This script is for calculating gem points on the game, playing sound on collect and destroying after taking it

public class GemBlue : MonoBehaviour
{
    public GameObject scoreBox;
    public AudioSource collectSound;

    void OnTriggerEnter()
    {
        GlobalScore.currentScore += 5000;
        collectSound.Play();
        Destroy(gameObject);
    }
}
