using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// The player-facing audio settings (Options > EFFECTS / MUSIC): each has an
// on/off toggle and a 0..1 volume slider.
//
// The project has no AudioMixer, so the two buses are made like this:
//   - EFFECTS is everything that is not music: AudioListener.volume carries the
//     effects gain, which silences/scales every AudioSource in every scene
//     (SFX, UI clicks, boss cues, the tutorial voice, PlayClipAtPoint and
//     sources created later) while they keep playing (isPlaying and clip timing
//     are unaffected).
//   - MUSIC sources opt out of the listener volume (ignoreListenerVolume) and
//     are scaled by the music gain themselves: the scene music
//     (MainMenuMusic, StoreMusic) is registered here (RegisterMusic) and scaled
//     from its authored volume; the in-run track (MovingMusic) is driven by
//     musicControl, which multiplies its own fade level by MusicGain; the
//     crossfade/loop tails (WorldMusic) copy that source's volume.
//
// Persisted per device in PlayerPrefs (never part of the cloud snapshot):
//   sfxOn / musicOn      int, absent = ON
//   sfxVolume / musicVolume  float 0..1, absent = 1
// The old single SOUND switch ("soundMuted" = 1) is migrated at startup to both
// toggles off, so an existing muted player stays silent; Muted / SetMuted keep
// meaning "both buses off".
//
// Applied before the first scene loads and re-applied on every scene load and
// app focus/pause change, in case anything resets the listener.
public static class SoundSettings
{
    public const string Key = "soundMuted";            // legacy master mute (migrated away)
    public const string SfxOnKey = "sfxOn", MusicOnKey = "musicOn";
    public const string SfxVolumeKey = "sfxVolume", MusicVolumeKey = "musicVolume";

    public static bool SfxOn { get { return PlayerPrefs.GetInt(SfxOnKey, 1) != 0; } }
    public static bool MusicOn { get { return PlayerPrefs.GetInt(MusicOnKey, 1) != 0; } }
    public static float SfxVolume { get { return Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumeKey, 1f)); } }
    public static float MusicVolume { get { return Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumeKey, 1f)); } }

    /// <summary>What the effects bus is actually at: 0 while off.</summary>
    public static float SfxGain { get { return SfxOn ? SfxVolume : 0f; } }
    /// <summary>What the music bus is actually at: 0 while off.</summary>
    public static float MusicGain { get { return MusicOn ? MusicVolume : 0f; } }

    /// <summary>Both buses off (the old SOUND: OFF).</summary>
    public static bool Muted { get { return !SfxOn && !MusicOn; } }

    public static void SetSfxOn(bool on) { Store(SfxOnKey, on); Apply(); }
    public static void SetMusicOn(bool on) { Store(MusicOnKey, on); Apply(); }
    public static void SetSfxVolume(float v) { PlayerPrefs.SetFloat(SfxVolumeKey, Mathf.Clamp01(v)); Apply(); }
    public static void SetMusicVolume(float v) { PlayerPrefs.SetFloat(MusicVolumeKey, Mathf.Clamp01(v)); Apply(); }

    public static void SetMuted(bool muted)
    {
        Store(SfxOnKey, !muted);
        Store(MusicOnKey, !muted);
        Apply();
    }

    public static void Toggle() { SetMuted(!Muted); }

    public static void Save() { PlayerPrefs.Save(); }

    static void Store(string key, bool on)
    {
        if (on) PlayerPrefs.DeleteKey(key); else PlayerPrefs.SetInt(key, 0);
    }

    // Makes the audio output match the saved settings.
    public static void Apply()
    {
        AudioListener.volume = SfxGain;
        float gain = MusicGain;
        for (int i = scene.Count - 1; i >= 0; i--)
        {
            var m = scene[i];
            if (m.src == null) { scene.RemoveAt(i); continue; }
            m.src.ignoreListenerVolume = true;
            m.src.volume = m.baseVolume * gain;
        }
    }

    // ---- music sources owned by SoundSettings (static-volume scene music) ----

    struct Music { public AudioSource src; public float baseVolume; }
    static readonly List<Music> scene = new List<Music>();

    /// <summary>Puts a source on the music bus: its current volume becomes its
    /// authored level, scaled by the music gain from now on.</summary>
    public static void RegisterMusic(AudioSource src)
    {
        if (src == null) return;
        for (int i = 0; i < scene.Count; i++) if (scene[i].src == src) return;
        scene.Add(new Music { src = src, baseVolume = src.volume });
        Apply();
    }

    /// <summary>Marks a source driven elsewhere (musicControl, the loop tails)
    /// as music: it ignores the effects gain; the caller scales its volume.</summary>
    public static void MarkMusic(AudioSource src)
    {
        if (src != null) src.ignoreListenerVolume = true;
    }

    public static string Label(bool on) { return on ? "ON" : "OFF"; }

    // One-time move from the single SOUND switch to the two toggles.
    public static void Migrate()
    {
        if (!PlayerPrefs.HasKey(Key)) return;
        if (PlayerPrefs.GetInt(Key, 0) != 0)
        {
            PlayerPrefs.SetInt(SfxOnKey, 0);
            PlayerPrefs.SetInt(MusicOnKey, 0);
        }
        PlayerPrefs.DeleteKey(Key);
        PlayerPrefs.Save();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        Migrate();
        scene.Clear();
        Apply();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Application.focusChanged -= OnFocus;
        Application.focusChanged += OnFocus;
    }

    // The scenes' own looping music players (not the in-run MovingMusic, which
    // musicControl drives).
    static readonly string[] SceneMusic = { "MainMenuMusic", "StoreMusic" };

    static void OnSceneLoaded(Scene s, LoadSceneMode mode)
    {
        foreach (var name in SceneMusic)
        {
            var go = GameObject.Find(name);
            if (go != null) RegisterMusic(go.GetComponent<AudioSource>());
        }
        Apply();
    }

    static void OnFocus(bool focus) { Apply(); }
}
