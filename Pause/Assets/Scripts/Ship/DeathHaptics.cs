using UnityEngine;

// The death vibration: one heavy thump at the fatal hit, then a brief fading
// rumble (~200 ms in all). Called from ONE place, collisionDetection's fatal
// branch, which runs once per run (buttonClicks.playerDied); the tutorial can
// never reach it (TutorialCannotDie), and nothing in the kill cam replay or
// revive calls it. OPTIONS > VIBRATION turns it off (PlayerPrefs, default ON).
//
//   Android  VibrationEffect.createWaveform (API 26+, with amplitudes), else
//            Vibrator.vibrate(ms) with the same timings; needs the VIBRATE
//            permission (Plugins/Android/HapticsManifest.androidlib).
//   iOS      Plugins/iOS/PauseHaptics.mm (UIImpactFeedbackGenerator).
//   other    no-op (Editor, desktop).
public static class DeathHaptics
{
    public const string EnabledKey = "vibrationEnabled";

    // Android waveform: wait, THUMP, gap, rumble. Amplitudes are 1..255.
    public static readonly long[] Timings = { 0, 70, 40, 90 };
    public static readonly int[] Amplitudes = { 0, 255, 0, 110 };
    public const int ThumpMs = 70, MaxTotalMs = 250;
    // A second call this soon after the first is the same death.
    public const float DebounceSeconds = 2f;

    static float lastFire = float.NegativeInfinity;

    public static bool Enabled
    {
        get { return PlayerPrefs.GetInt(EnabledKey, 1) != 0; }
        set
        {
            if (value) PlayerPrefs.DeleteKey(EnabledKey);
            else PlayerPrefs.SetInt(EnabledKey, 0);
            PrefsSaver.MarkDirty();
        }
    }

    public static long TotalMs()
    {
        long t = 0;
        for (int i = 0; i < Timings.Length; i++) t += Timings[i];
        return t;
    }

    // Pure gate: on, a phone, not the same death twice, and the game running.
    public static bool ShouldFire(bool enabled, bool platformSupported, float now, float last, float timeScale)
    {
        return enabled && platformSupported && timeScale > 0f && now - last >= DebounceSeconds;
    }

    static bool Supported
    {
        get { return Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer; }
    }

    // The fatal hit. Never throws: a vibration problem must not break the death.
    public static void OnFatalHit()
    {
        float now = Time.realtimeSinceStartup;
        if (!ShouldFire(Enabled, Supported, now, lastFire, Time.timeScale)) return;
        lastFire = now;
        try { Play(); } catch (System.Exception e) { Debug.Log("[Haptics] death vibration failed: " + e.Message); }
    }

    static void Play()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator"))
        {
            if (vibrator == null || !vibrator.Call<bool>("hasVibrator")) return;
            int sdk;
            using (var version = new AndroidJavaClass("android.os.Build$VERSION")) sdk = version.GetStatic<int>("SDK_INT");
            if (sdk >= 26)
            {
                using (var effectClass = new AndroidJavaClass("android.os.VibrationEffect"))
                using (var effect = effectClass.CallStatic<AndroidJavaObject>("createWaveform", Timings, Amplitudes, -1))
                    vibrator.Call("vibrate", effect);
            }
            else vibrator.Call("vibrate", Timings, -1);
        }
#elif UNITY_IOS && !UNITY_EDITOR
        PauseHapticDeath();
#endif
    }

#if UNITY_IOS && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    static extern void PauseHapticDeath();
#endif
}
