using UnityEngine;
using UnityEngine.SocialPlatforms;

// Platform-neutral wrapper around Unity's built-in Social API.
//
// On iOS this talks to Game Center with no extra plugin. On Android it is inert
// until a social plugin (e.g. Google Play Games v2) is installed and activated;
// the calls stay safe no-ops until then, so nothing here is platform-gated.
//
// Replaces the Android-only GooglePlayGames calls the project used until 2016.
public static class SocialBridge
{
    public delegate void SocialCallback(bool success);

    public static bool IsAuthenticated
    {
        get { return Social.localUser != null && Social.localUser.authenticated; }
    }

    public static void Authenticate(SocialCallback callback = null)
    {
        if (IsAuthenticated)
        {
            if (callback != null) callback(true);
            return;
        }

        Social.localUser.Authenticate(success =>
        {
            if (!success) Debug.Log("[SocialBridge] Sign-in failed or unavailable on this platform.");
            if (callback != null) callback(success);
        });
    }

    public static void SignOut()
    {
        // Unity's ISocialPlatform has no portable sign-out; Game Center manages
        // the session itself. Android plugins expose their own call.
        Debug.Log("[SocialBridge] Sign-out is handled by the platform.");
    }

    public static void ShowLeaderboard()
    {
        if (!IsAuthenticated)
        {
            Debug.Log("[SocialBridge] Not signed in; skipping leaderboard UI.");
            return;
        }
        Social.ShowLeaderboardUI();
    }

    public static void ShowAchievements()
    {
        if (!IsAuthenticated)
        {
            Debug.Log("[SocialBridge] Not signed in; skipping achievement UI.");
            return;
        }
        Social.ShowAchievementsUI();
    }

    public static void ReportScore(long score, string leaderboardId, SocialCallback callback = null)
    {
        if (string.IsNullOrEmpty(leaderboardId) || !IsAuthenticated)
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

    // Incremental achievements. Unity's portable API has no "increment", so the
    // running count is kept in PlayerPrefs and reported as percent-complete.
    // These counters are small enough that count maps directly onto percent;
    // revisit if an achievement ever needs more than 100 steps.
    public static void IncrementAchievement(string achievementId, int steps, SocialCallback callback = null)
    {
        if (string.IsNullOrEmpty(achievementId))
        {
            if (callback != null) callback(false);
            return;
        }

        string key = "achv_progress_" + achievementId;
        int progress = Mathf.Clamp(PlayerPrefs.GetInt(key, 0) + steps, 0, 100);
        PlayerPrefs.SetInt(key, progress);
        PlayerPrefs.Save();

        ReportProgress(achievementId, progress, callback);
    }

    static void ReportProgress(string achievementId, double percent, SocialCallback callback)
    {
        if (string.IsNullOrEmpty(achievementId) || !IsAuthenticated)
        {
            if (callback != null) callback(false);
            return;
        }
        Social.ReportProgress(achievementId, percent, success =>
        {
            if (callback != null) callback(success);
        });
    }
}
