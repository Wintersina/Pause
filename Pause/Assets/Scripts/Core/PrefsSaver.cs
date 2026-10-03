using UnityEngine;

// Batches PlayerPrefs.Save().
//
// Save() rewrites the whole prefs file (a plist on iOS, an XML file on
// Android), and achievement counters used to call it on every kill and every
// star pickup -- dozens of disk writes a minute in a busy run. Frequent
// counters now just MarkDirty(); the file is written at most once every
// SaveInterval seconds, plus immediately when the app is backgrounded or
// quit and whenever a caller has something that must not be lost (a purchase,
// the end of a run) and calls SaveNow().
public static class PrefsSaver
{
    public const float SaveInterval = 10f;

    static bool dirty;
    static float lastSaveAt;

    public static bool Dirty { get { return dirty; } }

    // Number of real PlayerPrefs.Save() calls made through here. Lets the
    // tests prove a path saved without being able to observe the disk.
    public static int SaveCount { get; private set; }

    public static void MarkDirty()
    {
        dirty = true;
    }

    public static void SaveNow()
    {
        PlayerPrefs.Save();
        dirty = false;
        lastSaveAt = Time.unscaledTime;
        SaveCount++;
    }

    // Saves only if something is pending and the last save was long enough
    // ago. Returns true when it actually wrote.
    public static bool SaveIfDue(float now)
    {
        if (!dirty || now - lastSaveAt < SaveInterval) return false;
        SaveNow();
        lastSaveAt = now;
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        if (Object.FindFirstObjectByType<PrefsSaverRunner>() != null) return;
        var go = new GameObject("~PrefsSaver");
        go.hideFlags = HideFlags.HideInHierarchy;
        Object.DontDestroyOnLoad(go);
        go.AddComponent<PrefsSaverRunner>();
    }
}
