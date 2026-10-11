using UnityEngine;
using System.Collections;

public class musicControl : MonoBehaviour {

    [Header("Boost sting")]
    [Tooltip("Ceiling for the boost track. It used to ramp unbounded to full " +
             "volume, which made grabbing a blue atom jarringly loud.")]
    [Range(0f, 1f)] public float boostVolumeCap = 0.55f;

    [Tooltip("How quickly the boost track fades up, per second.")]
    public float boostFadeInPerSecond = 0.9f;

    [Tooltip("The boost track keeps playing (silent) between boosts. If it has " +
             "been silent for longer than this, a fresh boost restarts it from " +
             "the top once; a shorter gap, or a pickup while it is still audible " +
             "(including during its fade-out), just carries on from where it is.")]
    public float coldStartSeconds = 3f;

    /// <summary>How many times the boost track has been rewound (cold starts only).</summary>
    public static int ColdRestarts;

    private float silentFor;

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
        Tick(Time.unscaledDeltaTime);
        UpdateMix();
    }

    // The boost track is never Play()ed, Stop()ped or rewound by a pickup: it
    // loops silently from scene start and the pickup only raises its volume.
    // The one rewind is a cold start, a boost after the track sat silent for
    // coldStartSeconds, so a fresh boost opens on the song's beginning.
    public void Tick(float unscaledDt)
    {
        if (boostSound == null) return;
        if (boostSound.volume > 0.001f) { silentFor = 0f; return; }
        silentFor += unscaledDt;
        if (boostMusicChanger && silentFor > coldStartSeconds)
        {
            boostSound.time = 0f;
            ColdRestarts++;
            silentFor = 0f;
        }
    }

    void UpdateMix() {
        // A boss's entrance is scripted, not a pause: its song plays at full
        // level from the first frame even with the finger up (BossEncounter).
        if (TouchInput.IsPressed || BossEncounter.ScriptedEntrance)
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
