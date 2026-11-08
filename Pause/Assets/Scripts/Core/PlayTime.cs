using UnityEngine;

// Seconds of real play, for the first 15 ad-free minutes (AdPolicy.AdFreePlaySeconds).
//
// What counts: frames of a real run (score.paysRealDust: not the menus, not the
// tutorial or practice) in which the ship is actually flying -- score.StepRunning,
// which does not run while the world is frozen by a PAUSE and does not run during a
// world entry or a boss entrance. Frozen PAUSE time deliberately does NOT count:
// that is the stricter reading, so "15 minutes" is an honest 15 minutes of flying. A
// backgrounded app runs no Update, and one step is clamped to MaxStep, so a stall
// or a resume spike cannot add time.
//
// Stored as a float in PlayerPrefs (Key) and as whole seconds in the cloud save
// (ProgressSnapshot.playTimeSeconds, merged by max), so reinstalling keeps it.
// Per-frame additions stay in memory and reach PlayerPrefs every FlushEvery
// seconds, on run end and when the app is paused.
public static class PlayTime
{
    public const string Key = "playTimeSeconds";
    public const float MaxStep = 0.5f;
    public const float FlushEvery = 5f;

    static float unflushed;

    public static float Seconds { get { return Mathf.Max(0f, PlayerPrefs.GetFloat(Key, 0f)) + unflushed; } }

    // One frame of play. Ignores non-positive and non-finite steps.
    public static void Add(float dt)
    {
        if (!(dt > 0f) || float.IsInfinity(dt)) return;
        unflushed += Mathf.Min(dt, MaxStep);
        if (unflushed >= FlushEvery) Flush();
    }

    public static void Flush()
    {
        if (unflushed <= 0f) return;
        PlayerPrefs.SetFloat(Key, Mathf.Max(0f, PlayerPrefs.GetFloat(Key, 0f)) + unflushed);
        unflushed = 0f;
        PrefsSaver.MarkDirty();
    }

    // Whole seconds for the cloud snapshot.
    public static int WholeSeconds { get { return (int)Mathf.Min(Seconds, int.MaxValue - 1f); } }

    // A restored value never lowers the local one (merge rule: larger wins).
    public static void RestoreAtLeast(int seconds)
    {
        Flush();
        if (seconds > PlayerPrefs.GetFloat(Key, 0f)) PlayerPrefs.SetFloat(Key, seconds);
    }

    public static int Merge(int a, int b) { return a > b ? a : b; }

    // Test hook: drop the in-memory part.
    public static void ResetPending() { unflushed = 0f; }
}
