using System;
using UnityEngine;
#if UNITY_ANDROID
using GooglePlayGames;
using GooglePlayGames.BasicApi;
#endif

// Google Play Games Services v2 leaderboards (play-games-plugin-for-unity 2.x).
//
// Sign-in is PlayGamesAccount's (CloudSync); this only reads and writes
// scores. Board ids are the Play Console ids (LeaderboardBoards). Scores are read
// from the PUBLIC collection: players who keep their Play Games profile
// private still see their own score, but not in the public ranks.
public sealed class PlayGamesLeaderboards : ILeaderboardPlatform
{
    public string PlatformName { get { return "Google Play Games"; } }

#if UNITY_ANDROID
    public bool IsAvailable { get { return Application.platform == RuntimePlatform.Android; } }

    public bool IsSignedIn
    {
        // PlayGamesAccount activates the platform before its first sign-in.
        get { return PlayerAccounts.Current.IsSignedIn && PlayGamesPlatform.Instance.IsAuthenticated(); }
    }

    public void SignIn(Action<bool> done)
    {
        SocialBridge.Authenticate(ok => { if (done != null) done(ok); });
    }

    public void Submit(string boardId, long score, Action<bool> done)
    {
        if (!IsSignedIn) { done(false); return; }
        PlayGamesPlatform.Instance.ReportScore(score, boardId, ok => done(ok));
    }

    static LeaderboardTimeSpan Span(LeaderboardTimeScope scope)
    {
        switch (scope)
        {
            case LeaderboardTimeScope.Today: return LeaderboardTimeSpan.Daily;
            case LeaderboardTimeScope.Week: return LeaderboardTimeSpan.Weekly;
            default: return LeaderboardTimeSpan.AllTime;
        }
    }

    void Load(string boardId, LeaderboardStart start, int count, LeaderboardTimeScope scope,
              bool playerOnly, Action<LeaderboardPage> done)
    {
        if (!IsSignedIn) { done(LeaderboardPage.Failed(LeaderboardStatus.NotSignedIn)); return; }
        var gpgs = PlayGamesPlatform.Instance;
        gpgs.LoadScores(boardId, start, Mathf.Clamp(count, 1, 25), LeaderboardCollection.Public, Span(scope), data =>
        {
            if (data == null || !data.Valid)
            {
                var status = data != null && data.Status == ResponseStatus.NotAuthorized
                    ? LeaderboardStatus.NotSignedIn : LeaderboardStatus.Error;
                Debug.Log("[Leaderboards] Play Games load failed: " + (data != null ? data.Status.ToString() : "null"));
                done(LeaderboardPage.Failed(status));
                return;
            }
            SocialScores.ToPage(playerOnly ? null : data.Scores, data.PlayerScore,
                gpgs.GetUserId(), gpgs.GetUserDisplayName(), gpgs.LoadUsers, done);
        });
    }

    public void LoadTopScores(string boardId, LeaderboardTimeScope timeScope, int count, Action<LeaderboardPage> done)
    {
        Load(boardId, LeaderboardStart.TopScores, count, timeScope, false, done);
    }

    public void LoadPlayerCentered(string boardId, int count, Action<LeaderboardPage> done)
    {
        Load(boardId, LeaderboardStart.PlayerCentered, count, LeaderboardTimeScope.AllTime, false, done);
    }

    public void LoadPlayerScore(string boardId, Action<LeaderboardPage> done)
    {
        Load(boardId, LeaderboardStart.PlayerCentered, 1, LeaderboardTimeScope.AllTime, true, done);
    }

    public void ShowNativeUI(string boardId)
    {
        if (!IsSignedIn) return;
        if (string.IsNullOrEmpty(boardId)) PlayGamesPlatform.Instance.ShowLeaderboardUI();
        else PlayGamesPlatform.Instance.ShowLeaderboardUI(boardId);
    }
#else
    public bool IsAvailable { get { return false; } }
    public bool IsSignedIn { get { return false; } }
    public void SignIn(Action<bool> done) { if (done != null) done(false); }
    public void Submit(string boardId, long score, Action<bool> done) { done(false); }
    public void LoadTopScores(string boardId, LeaderboardTimeScope timeScope, int count, Action<LeaderboardPage> done)
    { done(LeaderboardPage.Failed(LeaderboardStatus.Unavailable)); }
    public void LoadPlayerCentered(string boardId, int count, Action<LeaderboardPage> done)
    { done(LeaderboardPage.Failed(LeaderboardStatus.Unavailable)); }
    public void LoadPlayerScore(string boardId, Action<LeaderboardPage> done)
    { done(LeaderboardPage.Failed(LeaderboardStatus.Unavailable)); }
    public void ShowNativeUI(string boardId) { }
#endif
}
