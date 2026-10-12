using System;
using UnityEngine;

// Web addresses the game opens in the player's browser. One constant each,
// so the store listing, the policy page and the in-game button agree.
public static class ExternalLinks
{
    public const string PrivacyPolicy = "https://hapticgategames.com/privacy/";

    // Ignore a second tap this soon after an open (a double tap would
    // otherwise launch two browser tabs).
    public const float GuardSeconds = 1.5f;

    // Tests replace this; null means Application.OpenURL.
    public static Action<string> Opener;

    static float lastOpen = float.NegativeInfinity;
    public static void ResetGuard() { lastOpen = float.NegativeInfinity; }

    // True when the link was opened, false when swallowed by the guard.
    public static bool Open(string url)
    {
        float now = Time.realtimeSinceStartup;
        if (now - lastOpen < GuardSeconds && now >= lastOpen) return false;
        lastOpen = now;
        if (Opener != null) Opener(url);
        else Application.OpenURL(url);
        return true;
    }
}
