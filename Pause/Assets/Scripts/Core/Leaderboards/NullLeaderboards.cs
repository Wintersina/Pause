using System;

// No store on this device (editor, Mac, any other platform). Never available:
// submissions stay queued (harmlessly, on a device that will never flush
// them) and the panel shows its "unavailable" state.
public sealed class NullLeaderboards : ILeaderboardPlatform
{
    public string PlatformName { get { return "none"; } }
    public bool IsAvailable { get { return false; } }
    public bool IsSignedIn { get { return false; } }
    public void SignIn(Action<bool> done) { if (done != null) done(false); }
    public void Submit(string boardId, long score, Action<bool> done) { if (done != null) done(false); }

    public void LoadTopScores(string boardId, LeaderboardTimeScope timeScope, int count, Action<LeaderboardPage> done)
    { done(LeaderboardPage.Failed(LeaderboardStatus.Unavailable)); }

    public void LoadPlayerCentered(string boardId, int count, Action<LeaderboardPage> done)
    { done(LeaderboardPage.Failed(LeaderboardStatus.Unavailable)); }

    public void LoadPlayerScore(string boardId, Action<LeaderboardPage> done)
    { done(LeaderboardPage.Failed(LeaderboardStatus.Unavailable)); }

    public void ShowNativeUI(string boardId) { }
}
