using System;
using System.Collections.Generic;

// In-memory store for tests and editor previews. Everything is synchronous
// unless DeferLoads is set, in which case loads wait for CompleteLoads()
// (lets a test see the panel's loading state).
public sealed class FakeLeaderboards : ILeaderboardPlatform
{
    public bool Available = true;
    public bool SignedIn = true;
    public bool SignInSucceeds = true;
    public bool SubmitSucceeds = true;
    public bool LoadsFail;
    public bool DeferLoads;
    public string LocalPlayerId = "local";
    public string LocalPlayerName = "KANEDA";

    public struct Submission { public string board; public long score; }

    public readonly List<Submission> Submissions = new List<Submission>();
    public readonly List<string> NativeUICalls = new List<string>();
    public int SignInCalls;

    // Platform board id -> entries in rank order.
    public readonly Dictionary<string, List<LeaderboardEntry>> Boards = new Dictionary<string, List<LeaderboardEntry>>();

    readonly List<Action> deferred = new List<Action>();

    public string PlatformName { get { return "Fake"; } }
    public bool IsAvailable { get { return Available; } }
    public bool IsSignedIn { get { return Available && SignedIn; } }

    public void SignIn(Action<bool> done)
    {
        SignInCalls++;
        if (Available && SignInSucceeds) SignedIn = true;
        if (done != null) done(IsSignedIn);
    }

    public void Submit(string boardId, long score, Action<bool> done)
    {
        Submissions.Add(new Submission { board = boardId, score = score });
        if (done != null) done(IsSignedIn && SubmitSucceeds);
    }

    public int SubmissionsTo(string boardId)
    {
        int n = 0;
        foreach (var s in Submissions) if (s.board == boardId) n++;
        return n;
    }

    public void SetBoard(string boardId, params LeaderboardEntry[] entries)
    {
        Boards[boardId] = new List<LeaderboardEntry>(entries);
    }

    public void CompleteLoads()
    {
        var pending = deferred.ToArray();
        deferred.Clear();
        foreach (var a in pending) a();
    }

    void Answer(Action<LeaderboardPage> done, Func<LeaderboardPage> build)
    {
        Action run = () =>
        {
            if (!Available) { done(LeaderboardPage.Failed(LeaderboardStatus.Unavailable)); return; }
            if (!SignedIn) { done(LeaderboardPage.Failed(LeaderboardStatus.NotSignedIn)); return; }
            if (LoadsFail) { done(LeaderboardPage.Failed(LeaderboardStatus.Error)); return; }
            done(build());
        };
        if (DeferLoads) deferred.Add(run);
        else run();
    }

    List<LeaderboardEntry> Entries(string boardId)
    {
        List<LeaderboardEntry> list;
        return Boards.TryGetValue(boardId ?? "", out list) ? list : new List<LeaderboardEntry>();
    }

    LeaderboardPage Page(string boardId, int from, int count)
    {
        var page = new LeaderboardPage { status = LeaderboardStatus.Ok };
        var all = Entries(boardId);
        for (int i = Math.Max(0, from); i < all.Count && page.entries.Count < count; i++) page.entries.Add(all[i]);
        foreach (var e in all)
            if (e.isLocalPlayer || e.playerId == LocalPlayerId) { page.hasPlayer = true; page.player = e; break; }
        return page;
    }

    public void LoadTopScores(string boardId, LeaderboardTimeScope timeScope, int count, Action<LeaderboardPage> done)
    {
        Answer(done, () => Page(boardId, 0, count));
    }

    public void LoadPlayerCentered(string boardId, int count, Action<LeaderboardPage> done)
    {
        Answer(done, () =>
        {
            var probe = Page(boardId, 0, 0);
            int at = probe.hasPlayer ? probe.player.rank - 1 - count / 2 : 0;
            return Page(boardId, at, count);
        });
    }

    public void LoadPlayerScore(string boardId, Action<LeaderboardPage> done)
    {
        Answer(done, () => Page(boardId, 0, 0));
    }

    public void ShowNativeUI(string boardId)
    {
        NativeUICalls.Add(boardId);
    }

    public static LeaderboardEntry Entry(int rank, string name, long value, bool local = false)
    {
        return new LeaderboardEntry
        {
            rank = rank, playerId = local ? "local" : "p" + rank, playerName = name,
            value = value, isLocalPlayer = local,
        };
    }

    // Sample data for previews: the player sits at #14, outside the top 10.
    public static FakeLeaderboards Demo(bool ios = false)
    {
        var fake = new FakeLeaderboards();
        string[] names =
        {
            "TETSUO", "KAI", "KEI", "YAMAGATA", "COLONEL", "KIYOKO", "TAKASHI", "MASARU",
            "NEO-TOKYO", "CAPSULE", "RYU", "NEZU", "MIYAKO",
        };
        foreach (var board in LeaderboardBoards.All)
        {
            string id = board.PlatformId(ios);
            if (string.IsNullOrEmpty(id)) continue;
            var list = new List<LeaderboardEntry>();
            for (int i = 0; i < names.Length; i++) list.Add(Entry(i + 1, names[i], 400 - i * 17));
            list.Add(Entry(14, "KANEDA", 172, true));
            fake.Boards[id] = list;
        }
        return fake;
    }
}
