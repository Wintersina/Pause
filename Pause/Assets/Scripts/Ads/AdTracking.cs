using System;
using UnityEngine;

// iOS App Tracking Transparency: the native prompt (PauseATT.mm) is answered later, so the
// answer is polled. Ticked by AdRunner. Never blocks: after TimeoutSeconds it carries on.
public static class AdTracking
{
    public const float TimeoutSeconds = 60f;

    static Func<int> poll;
    static Action done;
    static float deadline;

    public static bool Pending { get { return done != null; } }

    public static void WaitForAnswer(Func<int> status, Action onDone)
    {
        poll = status;
        done = onDone;
        deadline = Time.realtimeSinceStartup + TimeoutSeconds;
    }

    public static void Tick(float now)
    {
        if (done == null) return;
        int s = 0;
        try { s = poll != null ? poll() : 99; } catch (Exception) { s = 99; }
        if (s != 0 && s != -1 && now < deadline) { Finish(); return; }
        if (now >= deadline) Finish();
    }

    static void Finish()
    {
        var cb = done;
        done = null; poll = null;
        if (cb != null) cb();
    }
}
