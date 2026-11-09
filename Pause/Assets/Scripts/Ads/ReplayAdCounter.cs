using UnityEngine;

// Replay presses since the last forced interstitial was shown. Persisted so killing the app does not reset it.
public static class ReplayAdCounter
{
    public const string Key = "ads.replaysSinceAd";

    public static int Count { get { return Mathf.Max(0, PlayerPrefs.GetInt(Key, 0)); } }

    public static int Increment()
    {
        int n = Count + 1;
        PlayerPrefs.SetInt(Key, n);
        PrefsSaver.MarkDirty();
        return n;
    }

    public static void Reset()
    {
        if (!PlayerPrefs.HasKey(Key)) return;
        PlayerPrefs.DeleteKey(Key);
        PrefsSaver.MarkDirty();
    }
}
