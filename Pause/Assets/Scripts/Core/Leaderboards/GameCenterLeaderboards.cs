using System;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using UnityEngine.SocialPlatforms;
using UnityEngine.SocialPlatforms.GameCenter;
#endif

// Game Center leaderboards on iOS, through Unity's built-in Social API.
//
// Sign-in is GameCenterAccount's (CloudSync). Board ids are the App Store
// Connect leaderboard ids (LeaderboardBoards, e.g.
// me.sinaserati.Pause.top_score). Reading uses ILeaderboard with a global
// user scope, a time scope and a 1-based rank range.
public sealed class GameCenterLeaderboards : ILeaderboardPlatform
{
    public string PlatformName { get { return "Game Center"; } }

#if UNITY_IOS && !UNITY_EDITOR
    public bool IsAvailable { get { return true; } }

    public bool IsSignedIn
    {
        get { return Social.localUser != null && Social.localUser.authenticated; }
    }

    public void SignIn(Action<bool> done)
    {
        SocialBridge.Authenticate(ok => { if (done != null) done(ok); });
    }

    public void Submit(string boardId, long score, Action<bool> done)
    {
        if (!IsSignedIn) { done(false); return; }
        Social.ReportScore(score, boardId, ok => done(ok));
    }

    static TimeScope Scope(LeaderboardTimeScope scope)
    {
        switch (scope)
        {
            case LeaderboardTimeScope.Today: return TimeScope.Today;
            case LeaderboardTimeScope.Week: return TimeScope.Week;
            default: return TimeScope.AllTime;
        }
    }

    static void Load(string boardId, LeaderboardTimeScope scope, int from, int count, bool playerOnly,
                     Action<LeaderboardPage> done)
    {
        var board = Social.CreateLeaderboard();
        board.id = boardId;
        board.userScope = UserScope.Global;
        board.timeScope = Scope(scope);
        board.range = new UnityEngine.SocialPlatforms.Range(Mathf.Max(1, from), Mathf.Clamp(count, 1, 100));
        board.LoadScores(ok =>
        {
            if (!ok)
            {
                Debug.Log("[Leaderboards] Game Center load failed for " + boardId);
                done(LeaderboardPage.Failed(LeaderboardStatus.Error));
                return;
            }
            var user = Social.localUser;
            SocialScores.ToPage(playerOnly ? null : board.scores, board.localUserScore,
                user != null ? user.id : null, user != null ? user.userName : null, Social.LoadUsers, done);
        });
    }

    public void LoadTopScores(string boardId, LeaderboardTimeScope timeScope, int count, Action<LeaderboardPage> done)
    {
        if (!IsSignedIn) { done(LeaderboardPage.Failed(LeaderboardStatus.NotSignedIn)); return; }
        Load(boardId, timeScope, 1, count, false, done);
    }

    // Game Center has no "around me" query: find the player's rank, then load
    // the window centred on it.
    public void LoadPlayerCentered(string boardId, int count, Action<LeaderboardPage> done)
    {
        if (!IsSignedIn) { done(LeaderboardPage.Failed(LeaderboardStatus.NotSignedIn)); return; }
        LoadPlayerScore(boardId, me =>
        {
            if (!me.Ok || !me.hasPlayer) { Load(boardId, LeaderboardTimeScope.AllTime, 1, count, false, done); return; }
            Load(boardId, LeaderboardTimeScope.AllTime, me.player.rank - count / 2, count, false, done);
        });
    }

    public void LoadPlayerScore(string boardId, Action<LeaderboardPage> done)
    {
        if (!IsSignedIn) { done(LeaderboardPage.Failed(LeaderboardStatus.NotSignedIn)); return; }
        Load(boardId, LeaderboardTimeScope.AllTime, 1, 1, true, done);
    }

    public void ShowNativeUI(string boardId)
    {
        if (!IsSignedIn) return;
        if (string.IsNullOrEmpty(boardId)) Social.ShowLeaderboardUI();
        else GameCenterPlatform.ShowLeaderboardUI(boardId, TimeScope.AllTime);
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
