using UnityEngine;
using System.Collections;

public class musicControl : MonoBehaviour {

    [Header("Boost sting")]
    [Tooltip("Ceiling for the boost track. It used to ramp unbounded to full " +
             "volume, which made grabbing a blue atom jarringly loud.")]
    [Range(0f, 1f)] public float boostVolumeCap = 0.55f;

    [Tooltip("How quickly the boost track fades up, per second.")]
    public float boostFadeInPerSecond = 0.9f;

    public AudioSource boostSound;
    public AudioSource backgroundSound;
    public static bool boostMusicChanger;
    private bool liftedFinger;
    // Use this for initialization
    void Start () {
        boostMusicChanger = false;
        liftedFinger = false;
        boostSound = GameObject.Find("BoostingMusic").GetComponent<AudioSource>();
        backgroundSound = GameObject.Find("MovingMusic").GetComponent<AudioSource>();
    }
	
	// Update is called once per frame
	void Update () {
        if (TouchInput.IsPressed)
        {
            // if boost has been picked up.
            if (boostMusicChanger)
            {

                playBoostAndRaiseBoostVolume();
            }
            else
            {
                playBackgroundAndRaiseBackgroundVolume();
            }
        }
        else if (score.pauseCounter <= 0)
        {
            // if boost has been picked up.
            if (boostMusicChanger)
            {

                playBoostAndRaiseBoostVolume();
            }
            else
            {
                playBackgroundAndRaiseBackgroundVolume();
            }

        }

        // when paused, reduce boost vol and play background and lower level
        else {
            boostVolIsZero();
        }
    }
    void playBoostAndRaiseBoostVolume()
    {
        if(liftedFinger && collisionDetection.invTimer < 1.05)
        {
            boostSound.volume = boostVolumeCap;
            liftedFinger = false;
        }
        boostSound.volume = (collisionDetection.invTimer > 1.05f)
            ? Mathf.Min(boostSound.volume + boostFadeInPerSecond * Time.deltaTime, boostVolumeCap)
            : Mathf.Max(boostSound.volume - .3f * Time.deltaTime, 0f);
        backgroundSound.volume -= .05f;

    }
    void playBackgroundAndRaiseBackgroundVolume()
    {
        boostSound.volume -= .25f;
        backgroundSound.volume += .25f;
    }
    void boostVolIsZero()
    {
        liftedFinger = true;
        boostSound.volume -= .25f;
        // if background vol is greather then 15% then raise reduce it else just set it to 15%
        backgroundSound.volume = (backgroundSound.volume >= .15f )?  backgroundSound.volume - .015f : .20f;
            
    }
}
