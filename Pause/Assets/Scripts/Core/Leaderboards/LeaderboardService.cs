using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Leaderboard front door: submission pipeline, loading for the in-game panel,
// and the store's own leaderboard screen. See docs/leaderboards.md.
//
// Submission rules
//   - A run's scores are OFFERED at run end (LeaderboardRunTracker): on death,
//     and when a run is left early after beating the local best.
//   - An offer only counts if it beats both the last value this device
//     submitted to that board and the value already waiting for it. Everything
//     else is dropped, so nothing is sent twice and the store is never spammed.
//   - Accepted offers wait in a device-local queue (PendingKey, one best value
//     per board, persisted at once) and are sent DebounceSeconds after the
//     last offer -- a run end offers every board in one go -- or as soon as
//     sign-in succeeds. Signed out or offline, they simply wait; a failed send
//     stays queued for the next sign-in or the next offer.
//   - Nothing is offered while developer mode is on (unlocked ships and a
//     picked start world aren't a fair score) or in the tutorial / any
//     practice run that doesn't pay real star dust (50 pauses). Keeps the
//     public boards clean.
//   - Nothing here ever blocks: every store call is async and fire-and-forget.
//
// The queue and the submitted marks are deliberately NOT part of the cloud
// save (ProgressSnapshot): they describe what THIS device still owes the
// store, and the store itself already keeps every player's best.
public sealed class LeaderboardService
{
    // Device-local JSON: the best not-yet-submitted value per board.
    public const string PendingKey = "leaderboards_pendingScores";
    // Device-local JSON: the last value successfully submitted per board.
    public const string SubmittedKey = "leaderboards_submittedScores";

    public const float DebounceSeconds = 2f;
    public const int PanelRows = 10;

    static LeaderboardService instance;

    public static LeaderboardService Instance
    {
        get
        {
            if (instance == null) instance = new LeaderboardService(LeaderboardPlatforms.CreateForPlatform());
            return instance;
        }
        // Tests (and the editor demo toggle) substitute their own.
        set { instance = value; }
    }

    public static bool HasInstance { get { return instance != null; } }

    public readonly ILeaderboardPlatform Platform;
    readonly Func<float> clock;

    // Which store's ids to use. Defaults to the build target.
    public bool Ios = AchievementIds.IsIOS;

    // Number of Submit calls handed to the platform (tests, diagnostics).
    public int SubmitCount { get; private set; }

    float flushDueAt = -1f;
    readonly HashSet<string> inFlight = new HashSet<string>();

    public LeaderboardService(ILeaderboardPlatform platform, Func<float> clock = null)
    {
        Platform = platform ?? new NullLeaderboards();
        this.clock = clock ?? (() => Time.unscaledTime);
    }

    // Hooks sign-in success (SocialBridge.SignedIn) up to Flush. Idempotent.
    public static void Install()
    {
        SocialBridge.SignedIn -= OnSignedInStatic;
        SocialBridge.SignedIn += OnSignedInStatic;
    }

    static void OnSignedInStatic() { Instance.OnSignedIn(); }

    public void OnSignedIn() { Flush(); }

    // ---- rules ----

    // Developer mode, or a practice run (tutorial scene, tutorial replay, or
    // any run before the tutorial was completed).
    public static bool SubmissionBlocked(out string reason)
    {
        if (DeveloperUnlocks.Enabled) { reason = "developer mode"; return true; }
        bool practice = !score.PaysRealDust(SceneManager.GetActiveScene().name,
            PlayerPrefs.GetString("HasDoneTut") == "true", startMenu.youAreInTutorial);
        if (practice) { reason = "tutorial"; return true; }
        reason = null;
        return false;
    }

    public static bool SubmissionBlocked()
    {
        string unused;
        return SubmissionBlocked(out unused);
    }

    public LeaderboardBoard Usable(string boardId)
    {
        var board = LeaderboardBoards.Get(boardId);
        return board != null && !string.IsNullOrEmpty(board.PlatformId(Ios)) ? board : null;
    }

    public List<LeaderboardBoard> UsableBoards()
    {
        var list = new List<LeaderboardBoard>();
        foreach (var b in LeaderboardBoards.All) if (!string.IsNullOrEmpty(b.PlatformId(Ios))) list.Add(b);
        return list;
    }

    // ---- submission ----

    // Offers every usable board its value from this run. Returns how many
    // were accepted into the queue.
    public int SubmitRun(LeaderboardRunStats run)
    {
        string reason;
        if (SubmissionBlocked(out reason))
        {
            Debug.Log("[Leaderboards] not submitting: " + reason);
            return 0;
        }
        int accepted = 0;
        foreach (var board in UsableBoards())
            if (OfferUnchecked(board, board.Measure(run))) accepted++;
        return accepted;
    }

    public bool Offer(string boardId, long value)
    {
        if (SubmissionBlocked()) return false;
        var board = Usable(boardId);
        return board != null && OfferUnchecked(board, value);
    }

    bool OfferUnchecked(LeaderboardBoard board, long value)
    {
        var submitted = ScoreTable.Load(SubmittedKey);
        var pending = ScoreTable.Load(PendingKey);
        long have;
        if (submitted.TryGet(board.id, out have) && !board.IsBetter(value, have)) return false;
        if (pending.TryGet(board.id, out have) && !board.IsBetter(value, have)) return false;

        pending.Set(board.id, value);
        pending.Save(PendingKey);
        PrefsSaver.SaveNow();   // a run end: must survive the app being killed
        flushDueAt = clock() + DebounceSeconds;
        return true;
    }

    public bool HasPending(string boardId, out long value)
    {
        return ScoreTable.Load(PendingKey).TryGet(boardId, out value);
    }

    public bool HasSubmitted(string boardId, out long value)
    {
        return ScoreTable.Load(SubmittedKey).TryGet(boardId, out value);
    }

    // Driven every frame by LeaderboardRunner.
    public void Tick()
    {
        if (flushDueAt < 0f || clock() < flushDueAt) return;
        flushDueAt = -1f;
        Flush();
    }

    // Sends everything queued, if the player is signed in. Safe to call any
    // time; boards already being sent are skipped.
    public void Flush()
    {
        if (!Platform.IsAvailable || !Platform.IsSignedIn) return;
        var pending = ScoreTable.Load(PendingKey);
        foreach (var entry in pending.entries.ToArray())
        {
            var board = Usable(entry.board);
            if (board == null)
            {
                // The board was disabled since; nothing to send it to.
                pending.Remove(entry.board);
                pending.Save(PendingKey);
                continue;
            }
            if (inFlight.Contains(board.id)) continue;
            inFlight.Add(board.id);
            SubmitCount++;
            long value = entry.value;
            string id = board.id;
            Platform.Submit(board.PlatformId(Ios), value, ok => OnSubmitted(id, value, ok));
        }
    }

    void OnSubmitted(string boardId, long value, bool ok)
    {
        inFlight.Remove(boardId);
        var board = LeaderboardBoards.Get(boardId);
        if (!ok || board == null)
        {
            Debug.Log("[Leaderboards] submit to " + boardId + " failed; kept for later.");
            return;
        }
        var submitted = ScoreTable.Load(SubmittedKey);
        long have;
        if (!submitted.TryGet(boardId, out have) || board.IsBetter(value, have))
        {
            submitted.Set(boardId, value);
            submitted.Save(SubmittedKey);
        }
        var pending = ScoreTable.Load(PendingKey);
        // A better score may have been queued while this one was in flight.
        if (pending.TryGet(boardId, out have) && !board.IsBetter(have, value))
        {
            pending.Remove(boardId);
            pending.Save(PendingKey);
        }
        PrefsSaver.MarkDirty();
        Debug.Log("[Leaderboards] submitted " + value + " to " + boardId + ".");
    }

    // ---- reading ----

    public delegate void BoardLoaded(LeaderboardPage top, LeaderboardPage player);

    // The top `count` all-time scores plus the player's own entry.
    public void LoadBoard(string boardId, int count, BoardLoaded done)
    {
        var board = Usable(boardId);
        if (board == null) { done(LeaderboardPage.Failed(LeaderboardStatus.Error), null); return; }
        if (!Platform.IsAvailable) { done(LeaderboardPage.Failed(LeaderboardStatus.Unavailable), null); return; }
        if (!Platform.IsSignedIn) { done(LeaderboardPage.Failed(LeaderboardStatus.NotSignedIn), null); return; }

        string id = board.PlatformId(Ios);
        LeaderboardPage top = null, me = null;
        int remaining = 2;
        Action finish = () => { if (--remaining == 0) done(top, me); };
        Platform.LoadTopScores(id, LeaderboardTimeScope.AllTime, count, page => { top = page; finish(); });
        Platform.LoadPlayerScore(id, page => { me = page; finish(); });
    }

    public void SignIn(Action<bool> done)
    {
        if (!Platform.IsAvailable) { if (done != null) done(false); return; }
        Platform.SignIn(ok =>
        {
            if (ok) Flush();
            if (done != null) done(ok);
        });
    }

    // The store's leaderboard screen for one board (null: all boards). Signed
    // out: one interactive sign-in attempt first.
    public void ShowNativeUI(string boardId)
    {
        var board = boardId == null ? null : Usable(boardId);
        string id = board != null ? board.PlatformId(Ios) : null;
        if (Platform.IsSignedIn) { Platform.ShowNativeUI(id); return; }
        SignIn(ok => { if (ok) Platform.ShowNativeUI(id); });
    }

    // ---- persistence ----

    [Serializable]
    public sealed class ScoreTable
    {
        [Serializable]
        public struct Entry
        {
            public string board;
            public long value;
        }

        public List<Entry> entries = new List<Entry>();

        public static ScoreTable Load(string key)
        {
            string json = PlayerPrefs.GetString(key, "");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var table = JsonUtility.FromJson<ScoreTable>(json);
                    if (table != null && table.entries != null) return table;
                }
                catch (Exception) { }
            }
            return new ScoreTable();
        }

        public void Save(string key)
        {
            if (entries.Count == 0) PlayerPrefs.DeleteKey(key);
            else PlayerPrefs.SetString(key, JsonUtility.ToJson(this));
        }

        public bool TryGet(string board, out long value)
        {
            foreach (var e in entries)
                if (e.board == board) { value = e.value; return true; }
            value = 0;
            return false;
        }

        public void Set(string board, long value)
        {
            Remove(board);
            entries.Add(new Entry { board = board, value = value });
        }

        public void Remove(string board)
        {
            entries.RemoveAll(e => e.board == board);
        }
    }
}
