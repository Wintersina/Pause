using System;
using System.Collections.Generic;
using UnityEngine;

// One store's leaderboard backend.
//
//   Android -> Google Play Games Services v2 (PlayGamesLeaderboards)
//   iOS     -> Game Center (GameCenterLeaderboards)
//   others  -> NullLeaderboards (editor, Mac, ...): never available
//   tests   -> FakeLeaderboards
//
// Every board id passed in here is the PLATFORM id (already resolved from the
// logical id by LeaderboardBoards). Callbacks arrive on the main thread and
// are always called exactly once, also on failure.
public interface ILeaderboardPlatform
{
    string PlatformName { get; }

    // The store exists on this device/build at all. False -> nothing works.
    bool IsAvailable { get; }

    bool IsSignedIn { get; }

    // Interactive sign-in (may show the store's UI).
    void SignIn(Action<bool> done);

    void Submit(string boardId, long score, Action<bool> done);

    void LoadTopScores(string boardId, LeaderboardTimeScope timeScope, int count, Action<LeaderboardPage> done);

    void LoadPlayerCentered(string boardId, int count, Action<LeaderboardPage> done);

    // The signed-in player's own entry (page.player); entries may be empty.
    void LoadPlayerScore(string boardId, Action<LeaderboardPage> done);

    // The store's own leaderboard screen; null boardId -> all boards.
    void ShowNativeUI(string boardId);
}

public enum LeaderboardTimeScope { Today, Week, AllTime }

public enum LeaderboardStatus
{
    Ok,
    NotSignedIn,
    Unavailable,   // no store on this device/build
    Error,         // offline, misconfigured board, store error
}

[Serializable]
public struct LeaderboardEntry
{
    public int rank;            // 1-based; 0 = unranked
    public string playerId;
    public string playerName;
    public long value;
    public bool isLocalPlayer;
}

public sealed class LeaderboardPage
{
    public LeaderboardStatus status;
    public readonly List<LeaderboardEntry> entries = new List<LeaderboardEntry>();
    public bool hasPlayer;
    public LeaderboardEntry player;

    public bool Ok { get { return status == LeaderboardStatus.Ok; } }

    public static LeaderboardPage Failed(LeaderboardStatus status)
    {
        return new LeaderboardPage { status = status };
    }
}

public static class LeaderboardPlatforms
{
    // Same choice as PlayerAccounts.CreateForPlatform.
    public static ILeaderboardPlatform CreateForPlatform()
    {
        switch (Application.platform)
        {
            case RuntimePlatform.Android: return new PlayGamesLeaderboards();
            case RuntimePlatform.IPhonePlayer: return new GameCenterLeaderboards();
            default: return new NullLeaderboards();
        }
    }
}
