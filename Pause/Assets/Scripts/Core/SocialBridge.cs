using UnityEngine;
using UnityEngine.SocialPlatforms;

// Platform-neutral wrapper around Unity's built-in Social API.
//
// Android: Google Play Games v2 (PlayGamesAccount activates it as Unity's
// Social platform). iOS: Game Center, built into Unity. Elsewhere the calls
// are safe no-ops.
//
// Sign-in is attempted silently at launch (CloudSyncRunner -> CloudSync); the
// interactive one is AccountLink.SignIn (Options Account row, leaderboard
// panel, the store screens below). While the player is signed out in Pause
// (AccountLink.Disconnected) nothing is reported. Achievement and leaderboard
// ids are the GPGS ids from StringHolder; AchievementIds maps them to the
// Game Center ids on iOS.
public static class SocialBridge
{
    public delegate void SocialCallback(bool success);

    // Raised every time a sign-in succeeds (the silent launch one and any
    // interactive one). LeaderboardService flushes its offline queue on it.
    public static event System.Action SignedIn;

    public static void NotifySignedIn()
    {
        var handler = SignedIn;
        if (handler != null) handler();
    }

    // Signed in to the store AND not signed out in Pause.
    public static bool IsAuthenticated
    {
        get
        {
            return !AccountLink.Disconnected && Social.localUser != null && Social.localUser.authenticated;
        }
    }

    // Interactive sign-in. Only used when the player asks for it (or for a
    // platform UI) while signed out; the launch sign-in is silent.
    public static void Authenticate(SocialCallback callback = null)
    {
        if (IsAuthenticated)
        {
            if (callback != null) callback(true);
            return;
        }
        AccountLink.SignIn(success =>
        {
            if (!success) Debug.Log("[SocialBridge] Sign-in failed or unavailable on this platform.");
            if (callback != null) callback(success);
        });
    }

    // The id to send on this platform, or null when it has none.
    static string PlatformId(string achievementId)
    {
        string id = AchievementIds.ForCurrentPlatform(achievementId);
        if (string.IsNullOrEmpty(id)) Debug.Log("[SocialBridge] No id on this platform for " + achievementId);
        return id;
    }

    // Signed out: one interactive sign-in attempt, then the native UI.
    public static void ShowLeaderboard()
    {
        Authenticate(success => { if (success) Social.ShowLeaderboardUI(); });
    }

    public static void ShowAchievements()
    {
        Authenticate(success => { if (success) Social.ShowAchievementsUI(); });
    }

    public static void ReportScore(long score, string leaderboardId, SocialCallback callback = null)
    {
        if (string.IsNullOrEmpty(leaderboardId) || !IsAuthenticated ||
            string.IsNullOrEmpty(leaderboardId = PlatformId(leaderboardId)))
        {
            if (callback != null) callback(false);
            return;
        }
        Social.ReportScore(score, leaderboardId, success =>
        {
            if (callback != null) callback(success);
        });
    }

    public static void UnlockAchievement(string achievementId, SocialCallback callback = null)
    {
        ReportProgress(achievementId, 100.0, callback);
    }

    // Tiered achievements report percent-complete here; AchievementTiers keeps
    // the counts, since Unity's portable API has no "increment".
    public static void ReportProgress(string achievementId, double percent, SocialCallback callback = null)
    {
        if (string.IsNullOrEmpty(achievementId) || !IsAuthenticated ||
            string.IsNullOrEmpty(achievementId = PlatformId(achievementId)))
        {
            if (callback != null) callback(false);
            return;
        }
        Social.ReportProgress(achievementId, PlatformPercent(percent), success =>
        {
            if (callback != null) callback(success);
        });
    }

    // Game Center takes percentComplete (0-100) as is. Play Games turns the
    // percentage of an incremental achievement back into steps with a
    // truncating multiply, so 323 of 1000 (32.3%) came back as 322 steps;
    // a nudge far below one step keeps the count exact.
    public static double PlatformPercent(double percent)
    {
#if UNITY_ANDROID
        if (percent > 0.0 && percent < 100.0) return System.Math.Min(100.0, percent + 1e-7);
#endif
        return percent;
    }
}
