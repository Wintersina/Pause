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
//     picked start world aren't a fair score) or in the tutorial
//     (the tutorial scene or its replay; skipping it still ranks).
//     Keeps the public boards clean.
//   - A store's "submitted" answer is not trusted (the Play Games plugin says
//     true at once and the server may still refuse the score). After each
//     send the player's own entry is read back, and the value only counts as
//     submitted once the board holds it or better (Judge). A refused or
//     unreadable result stays queued and is retried with growing delays
//     (BackoffSeconds), never in a tight loop.
//   - The submitted marks are tied to the store player (BoundPlayerKey). Once
//     per player on this device, BindAccount backfills: marks of an unknown
//     owner go back into the queue and the saved best score is offered, so a
//     score a store never took is finally sent (the store keeps only the best,
//     so a resend can't lower a rank). It passes the same developer-mode,
//     premium and tutorial gates as a run end, and uses only values that
//     already went through them (marks) or that RunScore never saves from
//     developer mode or the premium ship (the saved best).
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

    // Device-local: the store player the submitted marks belong to.
    public const string BoundPlayerKey = "leaderboards_boundPlayer";

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
    readonly Dictionary<string, float> inFlightSince = new Dictionary<string, float>();
    // A submit the platform never answered is treated as lost after this long,
    // so the board is sent again instead of staying stuck for the session.
    public const float SubmitTimeoutSeconds = 30f;

    // The store player's id, or null when unknown. Tests replace it.
    public Func<string> PlayerId = DefaultPlayerId;

    static string DefaultPlayerId()
    {
        var account = AccountLink.Account;
        return account != null && account.IsSignedIn ? account.PlayerId : null;
    }

    // A send the store said yes to, whose read-back is still to come.
    sealed class Verifying
    {
        public long value;
        public string tag;
        public float readDueAt;
        public bool reading;
        public float readStartedAt;
        public int token;
    }

    readonly Dictionary<string, Verifying> verifying = new Dictionary<string, Verifying>();
    // Per board: read-backs that did not show the value, and when the next send may go out.
    readonly Dictionary<string, int> failures = new Dictionary<string, int>();
    readonly Dictionary<string, float> nextSendAt = new Dictionary<string, float>();
    // A refusal is only believed after this many read-backs (the first can be stale).
    public const int ResendAfterReads = 2;

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

    // Signed in to the store and not signed out in Pause (AccountLink).
    public bool SignedIn
    {
        get { return Platform.IsAvailable && Platform.IsSignedIn && !AccountLink.Disconnected; }
    }

    // ---- rules ----

    // Developer mode, the premium ship, or a practice run (tutorial scene or
    // tutorial replay). A skipped tutorial ranks like a completed one.
    public static bool SubmissionBlocked(out string reason)
    {
        if (DeveloperUnlocks.Enabled) { reason = "developer mode"; return true; }
        // The premium (cheat) ship never ranks: the flown ship is checked here
        // too, besides the run's own start ship (SubmitRun).
        if (PremiumShips.IsPremium(ShipId.Equipped())) { reason = "premium ship"; return true; }
        // Only the tutorial itself is unranked, not "tutorial never completed":
        // skipping it (or a lost HasDoneTut flag) must not close the boards.
        bool practice = score.IsTutorialRun(SceneManager.GetActiveScene().name, startMenu.youAreInTutorial);
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
        if (run.UsedPremium)
        {
            Debug.Log("[Leaderboards] not submitting: premium ship");
            return 0;
        }
        int accepted = 0;
        string tag = run.Tag;
        foreach (var board in UsableBoards())
            if (OfferUnchecked(board, board.Measure(run), tag)) accepted++;
        return accepted;
    }

    public bool Offer(string boardId, long value, string tag = null)
    {
        if (SubmissionBlocked()) return false;
        var board = Usable(boardId);
        return board != null && OfferUnchecked(board, value, tag);
    }

    bool OfferUnchecked(LeaderboardBoard board, long value, string tag)
    {
        // The last gate before the queue: nothing from the premium ship, ever.
        if (PremiumShips.IsPremium(ShipId.Equipped()) || LeaderboardRunTag.IsPremium(tag)) return false;
        var submitted = ScoreTable.Load(SubmittedKey);
        var pending = ScoreTable.Load(PendingKey);
        long have;
        if (submitted.TryGet(board.id, out have) && !Supersedes(board, value, tag, have, submitted.TagOf(board.id))) return false;
        if (pending.TryGet(board.id, out have) && !Supersedes(board, value, tag, have, pending.TagOf(board.id))) return false;

        pending.Set(board.id, value, tag);
        pending.Save(PendingKey);
        failures.Remove(board.id);
        nextSendAt.Remove(board.id);   // a new value is sent without waiting out the old backoff
        PrefsSaver.SaveNow();   // a run end: must survive the app being killed
        flushDueAt = clock() + DebounceSeconds;
        return true;
    }

    // A new value replaces a stored one when it is better, or when it ties a
    // record that has no run tag and the new one has: the score is the same,
    // but the row can now show the ship and hearts. Matters most for Furthest
    // Loop, where a pilot keeps tying their best loop (the values are small
    // whole numbers), so a tagless old best would otherwise never get a ship.
    static bool Supersedes(LeaderboardBoard board, long value, string tag, long have, string haveTag)
    {
        if (board.IsBetter(value, have)) return true;
        return value == have && string.IsNullOrEmpty(haveTag) && !string.IsNullOrEmpty(tag);
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
        TickVerifying();
        if (flushDueAt < 0f || clock() < flushDueAt)
        {
            // Nothing is due, but a submit that never answered must be retried.
            if (inFlight.Count > 0 && flushDueAt < 0f && ExpireInFlight()) Flush();
            return;
        }
        flushDueAt = -1f;
        Flush();
    }

    // Forgets submits that never answered; true if any were dropped.
    bool ExpireInFlight()
    {
        List<string> expired = null;
        foreach (var pair in inFlightSince)
            if (clock() - pair.Value >= SubmitTimeoutSeconds)
                (expired ?? (expired = new List<string>())).Add(pair.Key);
        if (expired == null) return false;
        foreach (string id in expired) { inFlight.Remove(id); inFlightSince.Remove(id); }
        return true;
    }

    // Sends everything queued, if the player is signed in (and not signed out
    // in Pause: then scores keep waiting). Safe to call any time; boards
    // already being sent are skipped.
    public void Flush()
    {
        if (!SignedIn) return;
        BindAccount();
        var pending = ScoreTable.Load(PendingKey);
        float soonest = -1f;
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
            if (LeaderboardRunTag.IsPremium(entry.tag))
            {
                // Never send a premium-ship score (nothing queues one; this is the net).
                pending.Remove(entry.board);
                pending.Save(PendingKey);
                continue;
            }
            if (verifying.ContainsKey(board.id)) continue;   // sent; waiting for the read-back
            float due;
            if (nextSendAt.TryGetValue(board.id, out due) && clock() < due)
            {
                if (soonest < 0f || due < soonest) soonest = due;
                continue;
            }
            if (inFlight.Contains(board.id))
            {
                float since;
                if (!inFlightSince.TryGetValue(board.id, out since) || clock() - since < SubmitTimeoutSeconds) continue;
                Debug.Log("[Leaderboards] submit to " + board.id + " got no answer; sending again.");
            }
            inFlight.Add(board.id);
            inFlightSince[board.id] = clock();
            SubmitCount++;
            long value = entry.value;
            string id = board.id;
            string tag = entry.tag ?? "";
            Platform.Submit(board.PlatformId(Ios), value, tag, ok => OnSubmitted(id, value, tag, ok));
        }
        // A backed-off board is sent by a later Tick.
        if (soonest >= 0f && (flushDueAt < 0f || soonest < flushDueAt)) flushDueAt = soonest;
    }

    void OnSubmitted(string boardId, long value, string tag, bool ok)
    {
        inFlight.Remove(boardId);
        inFlightSince.Remove(boardId);
        var board = LeaderboardBoards.Get(boardId);
        if (!ok || board == null)
        {
            Debug.Log("[Leaderboards] submit to " + boardId + " failed; kept for later.");
            return;
        }
        // The store said yes, which proves little: check what the board holds.
        verifying[boardId] = new Verifying { value = value, tag = tag };
        ReadBack(boardId);
    }

    // ---- confirming a send ----

    public enum Verdict { Accepted, Rejected, Unknown }

    // Did the store really take `submitted`? `me` is the player's own entry as
    // read back. Accepted when the board holds that value or a better one;
    // Rejected when it holds a worse value or none; Unknown when the read failed.
    public static Verdict Judge(LeaderboardBoard board, long submitted, LeaderboardPage me)
    {
        if (board == null || me == null || !me.Ok) return Verdict.Unknown;
        if (!me.hasPlayer) return Verdict.Rejected;
        return board.IsBetter(submitted, me.player.value) ? Verdict.Rejected : Verdict.Accepted;
    }

    // Wait after the nth failed confirmation (0 = first): 3, 10, 30, 90, then 300 s.
    public static float BackoffSeconds(int failed)
    {
        switch (failed)
        {
            case 0: return 3f;
            case 1: return 10f;
            case 2: return 30f;
            case 3: return 90f;
            default: return 300f;
        }
    }

    void ReadBack(string boardId)
    {
        Verifying v;
        if (!verifying.TryGetValue(boardId, out v)) return;
        var board = Usable(boardId);
        if (board == null) { verifying.Remove(boardId); return; }
        v.reading = true;
        v.readStartedAt = clock();
        int token = ++v.token;
        Platform.LoadPlayerScore(board.PlatformId(Ios), page =>
        {
            Verifying cur;
            // A late answer to a read that timed out, or for a send since dropped.
            if (!verifying.TryGetValue(boardId, out cur) || cur != v || cur.token != token) return;
            cur.reading = false;
            OnReadBack(boardId, cur, page);
        });
    }

    void OnReadBack(string boardId, Verifying v, LeaderboardPage page)
    {
        var board = LeaderboardBoards.Get(boardId);
        var verdict = Judge(board, v.value, page);
        if (verdict == Verdict.Accepted)
        {
            verifying.Remove(boardId);
            failures.Remove(boardId);
            nextSendAt.Remove(boardId);
            MarkSubmitted(board, v.value, v.tag);
            return;
        }
        int n;
        failures.TryGetValue(boardId, out n);
        failures[boardId] = n + 1;
        float delay = BackoffSeconds(n);
        if (verdict == Verdict.Rejected && n + 1 >= ResendAfterReads)
        {
            // The board still lacks the value: send it again later, queue untouched.
            Debug.Log("[Leaderboards] " + boardId + " did not take " + v.value + "; sending again in " + delay + " s.");
            verifying.Remove(boardId);
            nextSendAt[boardId] = clock() + delay;
            if (flushDueAt < 0f || clock() + delay < flushDueAt) flushDueAt = clock() + delay;
        }
        else
        {
            v.readDueAt = clock() + delay;
        }
    }

    // Reads that are due, and reads the store never answered.
    void TickVerifying()
    {
        if (verifying.Count == 0) return;
        foreach (string id in new List<string>(verifying.Keys))
        {
            Verifying v;
            if (!verifying.TryGetValue(id, out v)) continue;
            if (v.reading)
            {
                if (clock() - v.readStartedAt < SubmitTimeoutSeconds) continue;
                v.token++;   // the late answer is ignored
                v.reading = false;
                OnReadBack(id, v, null);
            }
            else if (clock() >= v.readDueAt) ReadBack(id);
        }
    }

    void MarkSubmitted(LeaderboardBoard board, long value, string tag)
    {
        string boardId = board.id;
        var submitted = ScoreTable.Load(SubmittedKey);
        long have;
        if (!submitted.TryGet(boardId, out have) || Supersedes(board, value, tag, have, submitted.TagOf(boardId)))
        {
            submitted.Set(boardId, value, tag);
            submitted.Save(SubmittedKey);
        }
        var pending = ScoreTable.Load(PendingKey);
        if (pending.TryGet(boardId, out have))
        {
            if (!board.IsBetter(have, value))
            {
                pending.Remove(boardId);
                pending.Save(PendingKey);
            }
            else if (flushDueAt < 0f)
            {
                // A better score was queued while this one was being confirmed.
                flushDueAt = clock() + DebounceSeconds;
            }
        }
        PrefsSaver.MarkDirty();
        Debug.Log("[Leaderboards] submitted " + value + " to " + boardId + ".");
    }

    // ---- account binding and backfill ----

    // Once per store player on this device (BoundPlayerKey); run from Flush, so
    // only signed in and not disconnected.
    //   - Marks of another player (an account switch) are dropped: they say
    //     nothing about this player's boards.
    //   - Marks of no known player (written before the binding existed) may be
    //     scores the store never took (the plugin's instant "true"), so they
    //     go back into the queue.
    //   - The saved best score is offered too (a score made before its board
    //     existed was never sent). RunScore saves a best neither from developer
    //     mode nor from the premium ship.
    // Blocked right now (developer mode, premium ship, tutorial): nothing
    // happens and the binding is left for a later Flush, so a tainted session
    // can neither send anything nor use up the backfill.
    void BindAccount()
    {
        string id = PlayerId != null ? PlayerId() : null;
        if (string.IsNullOrEmpty(id)) return;
        string bound = PlayerPrefs.GetString(BoundPlayerKey, "");
        if (bound == id) return;
        string reason;
        if (SubmissionBlocked(out reason) || PremiumShips.IsPremium(ShipId.Equipped()))
        {
            Debug.Log("[Leaderboards] backfill waits: " + (reason ?? "premium ship"));
            return;
        }

        var submitted = ScoreTable.Load(SubmittedKey);
        if (string.IsNullOrEmpty(bound))
        {
            var pending = ScoreTable.Load(PendingKey);
            foreach (var e in submitted.entries)
            {
                var board = Usable(e.board);
                if (board == null || LeaderboardRunTag.IsPremium(e.tag)) continue;
                long have;
                if (pending.TryGet(e.board, out have) && !Supersedes(board, e.value, e.tag, have, pending.TagOf(e.board))) continue;
                pending.Set(e.board, e.value, e.tag);
            }
            pending.Save(PendingKey);
        }
        submitted.entries.Clear();
        submitted.Save(SubmittedKey);

        PlayerPrefs.SetString(BoundPlayerKey, id);
        PrefsSaver.SaveNow();
        verifying.Clear();
        failures.Clear();
        nextSendAt.Clear();

        var top = Usable(LeaderboardBoards.TopScore);
        int best = RunScore.SavedBest;
        if (top != null && best > 0) OfferUnchecked(top, best, null);
        Debug.Log("[Leaderboards] backfilled for player " + id + ".");
    }

    // ---- reading ----

    public delegate void BoardLoaded(LeaderboardPage top, LeaderboardPage player);

    // The top `count` all-time scores plus the player's own entry.
    public void LoadBoard(string boardId, int count, BoardLoaded done)
    {
        var board = Usable(boardId);
        if (board == null) { done(LeaderboardPage.Failed(LeaderboardStatus.Error), null); return; }
        if (!Platform.IsAvailable) { done(LeaderboardPage.Failed(LeaderboardStatus.Unavailable), null); return; }
        if (!SignedIn) { done(LeaderboardPage.Failed(LeaderboardStatus.NotSignedIn), null); return; }

        string id = board.PlatformId(Ios);
        LeaderboardPage top = null, me = null;
        int remaining = 2;
        Action finish = () => { if (--remaining == 0) done(top, me); };
        string boardLogical = board.id;
        Platform.LoadTopScores(id, LeaderboardTimeScope.AllTime, count, page => { top = FillOwnTag(boardLogical, page); finish(); });
        Platform.LoadPlayerScore(id, page => { me = FillOwnTag(boardLogical, page); finish(); });
    }

    // The player's own entry with no tag from the store (Game Center, or a
    // score from an older build) gets the one this device kept for the score
    // it submitted -- only when the stored value is the same, so a score made
    // elsewhere is never given this device's ship.
    LeaderboardPage FillOwnTag(string boardId, LeaderboardPage page)
    {
        if (page == null || !page.Ok) return page;
        var submitted = ScoreTable.Load(SubmittedKey);
        long have;
        string tag = submitted.TagOf(boardId);
        if (string.IsNullOrEmpty(tag) || !submitted.TryGet(boardId, out have)) return page;
        int ship, hearts, end;
        if (!LeaderboardRunTag.TryParse(tag, out ship, out hearts, out end)) return page;
        for (int i = 0; i < page.entries.Count; i++)
        {
            var e = page.entries[i];
            if (e.isLocalPlayer && e.value == have && e.startShip == 0 && e.startHearts == 0)
            {
                e.startShip = ship;
                e.startHearts = hearts;
                e.hasEnd = end >= 0;
                e.endHearts = Mathf.Max(end, 0);
                page.entries[i] = e;
            }
        }
        if (page.hasPlayer && page.player.value == have && page.player.startShip == 0 && page.player.startHearts == 0)
        {
            page.player.startShip = ship;
            page.player.startHearts = hearts;
            page.player.hasEnd = end >= 0;
            page.player.endHearts = Mathf.Max(end, 0);
        }
        return page;
    }

    // The panel's SIGN IN. The store platforms route this to
    // AccountLink.SignIn, the one interactive sign-in.
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
        if (SignedIn) { Platform.ShowNativeUI(id); return; }
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
            // Start ship, start and end hearts of the run that made the value
            // (LeaderboardRunTag); empty on entries an older build wrote.
            public string tag;
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

        public void Set(string board, long value, string tag = null)
        {
            Remove(board);
            entries.Add(new Entry { board = board, value = value, tag = tag ?? "" });
        }

        public string TagOf(string board)
        {
            foreach (var e in entries)
                if (e.board == board) return e.tag ?? "";
            return "";
        }

        public void Remove(string board)
        {
            entries.RemoveAll(e => e.board == board);
        }
    }
}
