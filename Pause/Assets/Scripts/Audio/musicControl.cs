using UnityEngine;

// Drives the in-run music source ("MovingMusic"): full volume while playing,
// dimmed while the game is paused. The track never changes with the ship's
// state (there used to be a separate boost track faded in over it; that is
// gone, so a blue atom leaves the song playing uninterrupted).
//
// `level` is the logical fade (0..1); the source is set to level scaled by the
// player's music setting (SoundSettings.MusicGain), and opts out of the
// listener volume, which is the effects bus.
public class musicControl : MonoBehaviour {

    public AudioSource backgroundSound;
    private float level = 1f;

    void Start () {
        backgroundSound = GameObject.Find("MovingMusic").GetComponent<AudioSource>();
        level = Mathf.Clamp01(backgroundSound.volume);
        SoundSettings.MarkMusic(backgroundSound);
        KillCamMusic.Attach(backgroundSound);   // the kill cam's rewind plays the last of the song backwards
    }

    void Update () {
        UpdateMix();
        backgroundSound.volume = level * SoundSettings.MusicGain;
    }

    void UpdateMix() {
        // A boss's entrance is scripted, not a pause: its song plays at full
        // level from the first frame even with the finger up (BossEncounter).
        if (TouchInput.IsPressed || BossEncounter.ScriptedEntrance || score.pauseCounter <= 0)
        {
            level = Mathf.Clamp01(level + .25f);
        }
        // when paused, play background at a lower level
        else
        {
            // if background vol is greater than 15% then reduce it else just set it to 20%
            level = (level >= .15f) ? level - .015f : .20f;
        }
    }
}
