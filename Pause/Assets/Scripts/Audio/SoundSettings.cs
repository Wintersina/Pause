using UnityEngine;
using UnityEngine.SceneManagement;

// The player-facing SOUND setting (Options > SOUND: ON / OFF).
//
// One central switch: when muted, AudioListener.volume is 0, which silences
// every AudioSource in every scene -- music, SFX, UI clicks, explosions, boss
// cues, the tutorial voice, PlayClipAtPoint and sources created later -- while
// the sources keep playing (so isPlaying / clip timing and anything that
// waits on them is unaffected, and un-muting resumes the music mid-track).
// Nothing else in the game touches AudioListener.volume.
//
// Persisted as PlayerPrefs int "soundMuted" (absent or 0 = sound ON, so
// existing players hear exactly what they heard before). It is a per-device
// preference and is deliberately not part of the cloud progress snapshot.
// Haptics are a separate matter: this does not touch vibration.
//
// Applied before the first scene loads, and re-applied on every scene load and
// app focus/pause change, in case anything resets the listener.
public static class SoundSettings
{
    public const string Key = "soundMuted";

    public static bool Muted { get { return PlayerPrefs.GetInt(Key, 0) != 0; } }

    public static void SetMuted(bool muted)
    {
        if (muted) PlayerPrefs.SetInt(Key, 1);
        else PlayerPrefs.DeleteKey(Key);
        PlayerPrefs.Save();
        Apply();
    }

    public static void Toggle() { SetMuted(!Muted); }

    // Makes the audio output match the saved setting.
    public static void Apply()
    {
        AudioListener.volume = Muted ? 0f : 1f;
    }

    public static string Label(bool muted) { return muted ? "SOUND: OFF" : "SOUND: ON"; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        Apply();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Application.focusChanged -= OnFocus;
        Application.focusChanged += OnFocus;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { Apply(); }
    static void OnFocus(bool focus) { Apply(); }
}
