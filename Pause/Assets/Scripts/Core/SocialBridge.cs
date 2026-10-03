using UnityEngine;
using UnityEngine.SocialPlatforms;

// Platform-neutral wrapper around Unity's built-in Social API.
//
// Android: Google Play Games v2 (PlayGamesAccount activates it as Unity's
// Social platform). iOS: Game Center, built into Unity. Elsewhere the calls
// are safe no-ops.
//
// Sign-in is automatic at launch (CloudSyncRunner -> CloudSync); nothing here
// needs a button. Achievement and leaderboard ids are the GPGS ids from
// StringHolder; AchievementIds maps them to the Game Center ids on iOS.
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

    public static bool IsAuthenticated
    {
        get { return Social.localUser != null && Social.localUser.authenticated; }
    }

    // Interactive sign-in. Only used when the player asks for a platform UI
    // while signed out; the launch sign-in is silent.
    public static void Authenticate(SocialCallback callback = null)
    {
        if (IsAuthenticated)
        {
            if (callback != null) callback(true);
            return;
        }

        System.Action<bool> done = success =>
        {
            if (!success) Debug.Log("[SocialBridge] Sign-in failed or unavailable on this platform.");
            if (callback != null) callback(success);
        };
        if (CloudSync.Instance != null) CloudSync.Instance.SignInInteractive(done);
        else PlayerAccounts.Current.SignIn(true, success =>
        {
            if (success) NotifySignedIn();   // CloudSync raises it itself
            done(success);
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
