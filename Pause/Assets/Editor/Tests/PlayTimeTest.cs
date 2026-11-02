using UnityEngine;

// The play-time tracker (PlayTime): adds only positive, clamped steps; saves through PlayerPrefs; round-trips
// the cloud snapshot; merges by the larger value; an older blob reads as 0; a restore never lowers local time.
public static class PlayTimeTest
{
    static int failures;

    static void Check(string label, bool condition)
    {
        if (!condition) failures++;
        Debug.Log("[PLAYTIME] " + (condition ? "PASS " : "FAIL ") + label);
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        failures = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.DeleteKey(PlayTime.Key);
        PlayTime.ResetPending();

        Check("starts at 0", PlayTime.Seconds == 0f);
        PlayTime.Add(1f); PlayTime.Add(0.5f);
        Check("adds steps", Mathf.Approximately(PlayTime.Seconds, 1.5f));
        PlayTime.Add(-3f); PlayTime.Add(0f); PlayTime.Add(float.NaN); PlayTime.Add(float.PositiveInfinity);
        Check("ignores zero, negative and non-finite steps", Mathf.Approximately(PlayTime.Seconds, 1.5f));
        PlayTime.Add(60f);
        Check("one step is clamped (a stall adds MaxStep)", Mathf.Approximately(PlayTime.Seconds, 1.5f + PlayTime.MaxStep));

        PlayTime.Flush();
        Check("flush moves it into PlayerPrefs", Mathf.Approximately(PlayerPrefs.GetFloat(PlayTime.Key), 2f) && Mathf.Approximately(PlayTime.Seconds, 2f));
        for (int i = 0; i < 20; i++) PlayTime.Add(0.4f);
        Check("auto-flushes after FlushEvery seconds", PlayerPrefs.GetFloat(PlayTime.Key) > 2f + 4f && Mathf.Approximately(PlayTime.Seconds, 10f));

        // Cloud snapshot.
        var snap = ProgressSnapshot.Capture(1000);
        Check("capture reads whole seconds", snap.playTimeSeconds == 10);
        ProgressSnapshot back;
        Check("round trips through JSON", ProgressSnapshot.TryParse(snap.ToJson(), out back) == ProgressSnapshot.ParseResult.Ok && back.playTimeSeconds == 10);
        string old = snap.ToJson().Replace(",\"playTimeSeconds\":10", "").Replace("\"playTimeSeconds\":10,", "");
        Check("an older blob lacks the field", !old.Contains("playTimeSeconds"));
        ProgressSnapshot parsed;
        Check("an older blob reads as 0", ProgressSnapshot.TryParse(old, out parsed) == ProgressSnapshot.ParseResult.Ok && parsed.playTimeSeconds == 0);

        var a = new ProgressSnapshot { savedAtUtc = 5, playTimeSeconds = 600 };
        var b = new ProgressSnapshot { savedAtUtc = 9, playTimeSeconds = 2400 };
        Check("merge: the larger value wins (older side)", ProgressMerge.Merge(a, b).playTimeSeconds == 2400);
        Check("merge: the larger value wins (newer side)", ProgressMerge.Merge(b, a).playTimeSeconds == 2400);
        Check("merge: a save without the field changes nothing", ProgressMerge.Merge(a, parsed).playTimeSeconds == 600);
        Check("Merge helper", PlayTime.Merge(3, 7) == 7 && PlayTime.Merge(7, 3) == 7 && PlayTime.Merge(0, 0) == 0);

        // Restore (reinstall) and apply.
        PlayerPrefs.DeleteKey(PlayTime.Key);
        PlayTime.ResetPending();
        new ProgressSnapshot { savedAtUtc = 1, playTimeSeconds = 1800 }.Apply();
        Check("apply restores the cloud time on a fresh install", Mathf.Approximately(PlayTime.Seconds, 1800f));
        new ProgressSnapshot { savedAtUtc = 2, playTimeSeconds = 100 }.Apply();
        Check("apply never lowers local time", Mathf.Approximately(PlayTime.Seconds, 1800f));
        new ProgressSnapshot { savedAtUtc = 3 }.Apply();
        Check("apply of an older save keeps local time", Mathf.Approximately(PlayTime.Seconds, 1800f));

        return failures;
    }
}
