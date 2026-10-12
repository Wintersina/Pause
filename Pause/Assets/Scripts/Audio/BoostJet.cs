using UnityEngine;

// The blue atom's jet-engine spool-up (Resources/Audio/Boost/boost_jet.wav,
// built by Audio/EnemyDeathSrc~/build_boost_jet_sound.py: noise through a
// rising low-pass, a rumble bed and a noise-band turbine whine, never a tone).
//
// One pooled AudioSource lives for the whole session; a second pickup while the
// first is still spooling restarts it instead of stacking a copy. It rides the
// effects bus like every SFX (AudioListener.volume = SoundSettings.SfxGain), and
// is skipped outright while effects are off or at volume 0. Plays allocate
// nothing after the first call.
public static class BoostJet
{
    public const string ClipPath = "Audio/Boost/boost_jet";
    public const float Volume = .85f;

    public static int Plays;          // pickups that actually started the sound (tests)
    public static float LastGain;     // the gain the last Play was started at (Volume * effects gain)

    static AudioSource source;
    static AudioClip clip;

    public static AudioSource Source { get { return source; } }

    public static void Play()
    {
        float sfx = SoundSettings.SfxGain;
        if (sfx <= 0f) return;
        if (clip == null) clip = Resources.Load<AudioClip>(ClipPath);
        if (clip == null) return;
        if (source == null)
        {
            var go = new GameObject("~BoostJet");
            go.hideFlags = HideFlags.HideAndDontSave;
            if (Application.isPlaying) Object.DontDestroyOnLoad(go);
            source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
        }
        source.Stop();                 // restart, never stack
        source.clip = clip;
        source.volume = Volume;
        source.Play();
        LastGain = Volume * sfx;
        Plays++;
    }
}
