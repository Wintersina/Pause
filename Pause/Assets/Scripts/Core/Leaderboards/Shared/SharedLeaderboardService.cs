using System;
using System.Collections.Generic;
using UnityEngine;

// The shared (Firebase) leaderboard's front door, next to LeaderboardService (docs/shared-leaderboard.md).
//
//   - DORMANT: nothing happens unless the backend is available (PAUSE_FIREBASE compiled in, the platform config
//     file present, Firebase initialised) and the player's sharing switch is on (default on). Without that no
//     run is queued and nothing is sent; the native boards are untouched either way.
//   - Upload: after each run (SubmitRun, same gates and same routing as the native boards: a boosted run goes to
//     the Power-Ups table only, any other run to Main only, the premium ship nowhere). Improve-only, offline-safe
//     queue in PlayerPrefs, per table one candidate run per column, debounced, retried with LeaderboardService's
//     backoff. A queued run leaves only when the server transaction commits.
//   - Failure never blocks or throws: the run stays queued, the panel falls back to the native boards.
//   - Reading uses the caching rule: a (table, sort) page is reused for CacheSeconds, a forced refresh is limited
//     to once per MinRefreshSeconds, and a failed load serves the last page with its age.
public sealed class SharedLeaderboardService
{
    // Player setting: share scores to the shared board. Default on.
    public const string SharingKey = "shared_leaderboard_on";
    // Device-local JSON: the best not-yet-uploaded runs per table.
    public const string PendingKey = "sharedlb_pending";
    // Device-local JSON: the last server documents this device saw.
    public const string SubmittedKey = "sharedlb_submitted";
    // The Firebase uid the submitted records belong to.
    public const string BoundUidKey = "sharedlb_uid";
    public const string CachePrefix = "sharedlb_cache_";

    public const float DebounceSeconds = 2f;
    public const float SendTimeoutSeconds = 30f;
    public const float QuotaWaitSeconds = 3600f;
    public const float SignInRetrySeconds = 60f;
    public const int MaxRejectedSends = 8;
    public const int PanelRows = 10;
    public const long CacheSeconds = 300;
    public const long MinRefreshSeconds = 60;

    static SharedLeaderboardService instance;

    public static SharedLeaderboardService Instance
    {
        get
        {
            if (instance == null) instance = new SharedLeaderboardService(SharedBoardBackends.CreateForPlatform());
            return instance;
        }
        set { instance = value; }
    }

    public static bool HasInstance { get { return instance != null; } }

    // Creates the service at launch (so a queue left by the last session is sent) and flushes it on every
    // platform sign-in. Idempotent. Harmless when the backend is not available.
    public static void Install()
    {
        var unused = Instance;
        SocialBridge.SignedIn -= OnSignedInStatic;
        SocialBridge.SignedIn += OnSignedInStatic;
    }

    static void OnSignedInStatic() { Instance.Flush(); }

    // The player's sharing switch (Leaderboard Settings toggle). Default on.
    public static bool SharingOn
    {
        get { return PlayerPrefs.GetInt(SharingKey, 1) == 1; }
        set { PlayerPrefs.SetInt(SharingKey, value ? 1 : 0); }
    }

    public readonly ISharedBoardBackend Backend;
    readonly Func<float> clock;

    // The platform (Play Games / Game Center) account is signed in and not disconnected in Pause. Tests replace these.
    public Func<bool> PlatformSignedIn = DefaultPlatformSignedIn;
    public Func<bool> Disconnected = () => AccountLink.Disconnected;
    public Func<string> PlayerName = DefaultPlayerName;
    public Func<long> UnixNow = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    static bool DefaultPlatformSignedIn()
    {
        var account = AccountLink.Account;
        return account != null && account.IsSignedIn;
    }

    static string DefaultPlayerName()
    {
        var profile = AccountLink.Account as IAccountProfile;
        return profile != null ? profile.DisplayName : null;
    }

    // Raised with the table once an upload commits.
    public event Action<SharedTable> Uploaded;

    [Serializable]
    sealed class Store
    {
        public SharedDoc main = new SharedDoc();
        public SharedDoc power = new SharedDoc();
        public SharedDoc Of(SharedTable t) { return t == SharedTable.Power ? power : main; }
    }

    // Per-table runtime state.
    sealed class Lane
    {
        public bool inFlight;
        public float inFlightSince;
        public int failures;
        public int rejected;
        public float nextSendAt;
        public float lastWriteAt = -1e9f;
    }

    readonly Lane[] lanes = { new Lane(), new Lane() };
    float flushDueAt = -1f;
    bool signingIn;
    float nextSignInAt;
    bool deleting;
    // Set when the server refused writes repeatedly: no more sends this session.
    bool parked;

    public int UploadCount { get; private set; }

    public SharedLeaderboardService(ISharedBoardBackend backend, Func<float> clock = null)
    {
        Backend = backend ?? new NullSharedBoard();
        this.clock = clock ?? (() => Time.unscaledTime);
    }

    // The shared board is live: a usable backend and the player's switch on.
    public bool Enabled { get { return SharingOn && Backend.IsAvailable; } }

    // The home Leaderboard button shows the shared board only when it can actually be read: live, and the Firebase
    // user exists. Otherwise the native Play Games / Game Center panel opens as it always did.
    public bool BoardUsable { get { return Enabled && Backend.IsSignedIn; } }

    // ---- stored state ----

    static Store LoadStore(string key)
    {
        string json = PlayerPrefs.GetString(key, "");
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                var s = JsonUtility.FromJson<Store>(json);
                if (s != null && s.main != null && s.power != null) return s;
            }
            catch (Exception) { }
        }
        return new Store();
    }

    static void SaveStore(string key, Store s)
    {
        if (s.main.IsEmpty && s.power.IsEmpty) PlayerPrefs.DeleteKey(key);
        else PlayerPrefs.SetString(key, JsonUtility.ToJson(s));
    }

    // What this device still owes the server, for tests and diagnostics.
    public SharedDoc Pending(SharedTable table) { return LoadStore(PendingKey).Of(table).Clone(); }
    public SharedDoc Submitted(SharedTable table) { return LoadStore(SubmittedKey).Of(table).Clone(); }
    public bool HasPending { get { var s = LoadStore(PendingKey); return !s.main.IsEmpty || !s.power.IsEmpty; } }

    // ---- offering runs ----

    // Offers a finished run (LeaderboardService.SubmitRun calls this after its own gates). Returns how many
    // columns the run is now queued for.
    public int OfferRun(LeaderboardRunStats run)
    {
        if (!Enabled || deleting) return 0;
        if (LeaderboardService.SubmissionBlocked() || run.UsedPremium) return 0;
        var table = run.PoweredUp ? SharedTable.Power : SharedTable.Main;
        return OfferCandidate(table, SharedRun.From(run));
    }

    int OfferCandidate(SharedTable table, SharedRun run)
    {
        if (!SharedBoardLimits.ValidRun(run, table)) return 0;
        var pendingStore = LoadStore(PendingKey);
        var submitted = LoadStore(SubmittedKey).Of(table);
        var pending = pendingStore.Of(table);
        int taken = 0;
        var filtered = run;
        foreach (var c in SharedDoc.Columns)
        {
            if (Beats(run, submitted, c) && Beats(run, pending, c)) { taken++; continue; }
            // not an improvement in this column: the run must not drag it along
            if (c == SharedColumn.Score) filtered.score = 0; else if (c == SharedColumn.Dust) filtered.dust = 0; else filtered.worlds = 0;
        }
        if (taken > 0) pending.Offer(filtered);
        if (taken == 0) return 0;
        SaveStore(PendingKey, pendingStore);
        var lane = lanes[(int)table];
        lane.failures = 0;
        lane.nextSendAt = 0f;
        PrefsSaver.SaveNow();   // a run end: must survive the app being killed
        flushDueAt = clock() + DebounceSeconds;
        return taken;
    }

    // Better in this column, or a tie that adds the ship the stored details lack (as the native marks do).
    static bool Beats(SharedRun run, SharedDoc have, SharedColumn c)
    {
        long mine = run.Metric(c);
        if (mine <= 0) return false;
        long theirs = have.Metric(c);
        if (mine != theirs) return mine > theirs;
        return have.run.ship == 0 && run.ship != 0;
    }

    // ---- sending ----

    // Driven every frame by LeaderboardRunner.
    public void Tick()
    {
        if (!Enabled) return;
        if (Backend.IsSignedIn && Disconnected()) Backend.SignOut();   // SIGN OUT in Pause also leaves Firebase
        if (flushDueAt < 0f || clock() < flushDueAt) return;
        flushDueAt = -1f;
        Flush();
    }

    // Sends what is queued, if sharing is live and the player is signed in. Safe to call any time.
    public void Flush()
    {
        if (!Enabled || parked || deleting || Disconnected() || LeaderboardService.CloudSettling()) return;
        if (!Backend.IsSignedIn)
        {
            // Sign in to Firebase when something is queued, or once per install to put the device's bests up.
            if (HasPending || !PlayerPrefs.HasKey(BoundUidKey)) EnsureSignedIn();
            return;
        }
        BindUid();
        if (!HasPending) return;
        float soonest = -1f;
        foreach (SharedTable table in new[] { SharedTable.Main, SharedTable.Power })
        {
            var store = LoadStore(PendingKey);
            var overlay = store.Of(table);
            if (overlay.IsEmpty) continue;
            var lane = lanes[(int)table];
            if (lane.inFlight)
            {
                if (clock() - lane.inFlightSince < SendTimeoutSeconds) continue;
                lane.inFlight = false;   // the call never answered
            }
            float due = Math.Max(lane.nextSendAt, lane.lastWriteAt + SharedBoardLimits.MinWriteIntervalSeconds + 0.5f);
            if (clock() < due)
            {
                if (soonest < 0f || due < soonest) soonest = due;
                continue;
            }
            lane.inFlight = true;
            lane.inFlightSince = clock();
            UploadCount++;
            var sent = overlay.Clone();
            var t = table;
            string name = SharedBoardLimits.CleanName(PlayerName(), Backend.UserId);
            Backend.Upload(t, name, sent, (result, doc) => OnUploaded(t, sent, result, doc));
        }
        if (soonest >= 0f && (flushDueAt < 0f || soonest < flushDueAt)) flushDueAt = soonest;
    }

    // Like Flush, but a table waiting out a backoff is sent now. The server's write interval still applies.
    public void FlushNow()
    {
        foreach (var lane in lanes) { lane.nextSendAt = 0f; lane.failures = 0; }
        parked = false;
        Flush();
    }

    void EnsureSignedIn()
    {
        if (signingIn || clock() < nextSignInAt || !PlatformSignedIn()) return;
        signingIn = true;
        Backend.SignIn(ok =>
        {
            signingIn = false;
            if (ok) Flush();
            else
            {
                nextSignInAt = clock() + SignInRetrySeconds;
                if (flushDueAt < 0f) flushDueAt = nextSignInAt;
            }
        });
    }

    void OnUploaded(SharedTable table, SharedDoc sent, SharedResult result, SharedDoc doc)
    {
        var lane = lanes[(int)table];
        lane.inFlight = false;
        switch (result)
        {
            case SharedResult.Ok:
            {
                lane.failures = 0;
                lane.rejected = 0;
                lane.nextSendAt = 0f;
                lane.lastWriteAt = clock();
                var submittedStore = LoadStore(SubmittedKey);
                if (doc != null && doc.IsFull) { var cur = submittedStore.Of(table); CopyInto(cur, doc); SaveStore(SubmittedKey, submittedStore); }
                var pendingStore = LoadStore(PendingKey);
                var pending = pendingStore.Of(table);
                var known = LoadStore(SubmittedKey).Of(table);
                if (known.IsFull) pending.Prune(known);
                else foreach (var c in SharedDoc.Columns) if (sent.Has(c) && pending.Has(c) && pending.Metric(c) <= sent.Metric(c)) pending.Clear(c);
                SaveStore(PendingKey, pendingStore);
                InvalidateCache(table);
                PrefsSaver.MarkDirty();
                Debug.Log("[SharedBoard] uploaded to " + SharedBoardLimits.Collection(table) + ".");
                if (!pending.IsEmpty && flushDueAt < 0f) flushDueAt = clock() + DebounceSeconds;
                if (Uploaded != null) Uploaded(table);
                return;
            }
            case SharedResult.SignedOut:
                lane.nextSendAt = clock() + SignInRetrySeconds;
                break;
            case SharedResult.Quota:
                Debug.Log("[SharedBoard] free quota is spent; keeping the run queued.");
                lane.nextSendAt = clock() + QuotaWaitSeconds;
                break;
            case SharedResult.Denied:
                lane.rejected++;
                if (lane.rejected >= MaxRejectedSends)
                {
                    Debug.LogWarning("[SharedBoard] the server keeps refusing writes (rules or App Check); not sending again this session.");
                    parked = true;
                }
                lane.nextSendAt = clock() + LeaderboardService.BackoffSeconds(lane.failures++);
                break;
            default:
                lane.nextSendAt = clock() + LeaderboardService.BackoffSeconds(lane.failures++);
                break;
        }
        Debug.Log("[SharedBoard] upload to " + SharedBoardLimits.Collection(table) + " failed: " + result + ".");
        float due = lane.nextSendAt;
        if (!parked && (flushDueAt < 0f || due < flushDueAt)) flushDueAt = due;
    }

    static void CopyInto(SharedDoc target, SharedDoc source)
    {
        target.name = source.name;
        target.updatedAtMs = source.updatedAtMs;
        target.platform = source.platform;
        target.score = source.score; target.dust = source.dust; target.worlds = source.worlds;
        target.run = source.run;
        target.legacy = false;
    }

    // The submitted records belong to one Firebase uid. A different uid forgets them and queues this device's
    // bests again. Waits while the session is blocked (developer mode, premium ship, tutorial).
    void BindUid()
    {
        string id = Backend.UserId;
        if (string.IsNullOrEmpty(id) || PlayerPrefs.GetString(BoundUidKey, "") == id) return;
        if (LeaderboardService.SubmissionBlocked()) return;
        SaveStore(SubmittedKey, new Store());
        PlayerPrefs.SetString(BoundUidKey, id);
        PrefsSaver.SaveNow();
        QueueDeviceBests();
    }

    // ---- sync now ----

    // Queues this device's bests (the native score marks and the saved best) for the shared board and sends them.
    // Returns how many columns were queued. Nothing happens while sharing is off or the session is blocked.
    public int SyncNow()
    {
        if (!Enabled || deleting || LeaderboardService.SubmissionBlocked()) return 0;
        int n = QueueDeviceBests();
        FlushNow();
        return n;
    }

    // The native marks carry the run behind each board value in their tag, so the shared slots can be rebuilt
    // from them; the normal local best is offered as a score-only run.
    int QueueDeviceBests()
    {
        int n = 0;
        foreach (string key in new[] { LeaderboardService.SubmittedKey, LeaderboardService.PendingKey })
        {
            var marks = LeaderboardService.ScoreTable.Load(key);
            foreach (var e in marks.entries)
            {
                SharedRun run;
                if (!SharedRun.TryFromMark(e.board, e.value, e.tag, out run)) continue;
                n += OfferCandidate(e.board == LeaderboardBoards.BoostedRuns ? SharedTable.Power : SharedTable.Main, run);
            }
        }
        long best = LeaderboardService.LocalBest(false);
        if (best > 0)
        {
            var run = SharedRun.Blank();
            run.score = Math.Min(best, SharedBoardLimits.MaxScore);
            n += OfferCandidate(SharedTable.Main, run);
        }
        return n;
    }

    // ---- reading ----

    public sealed class Loaded
    {
        public LeaderboardPage page;
        public bool fromCache;     // the page is the cached one (fresh, or served because the load failed)
        public long ageSeconds;    // how old the cached page is
    }

    [Serializable]
    sealed class CachedPage
    {
        public long savedAt;
        public long fetchedAt;
        public List<LeaderboardEntry> entries = new List<LeaderboardEntry>();
        public bool hasPlayer;
        public LeaderboardEntry player;
    }

    readonly Dictionary<string, CachedPage> memory = new Dictionary<string, CachedPage>();

    static string CacheKey(SharedTable t, SharedColumn c) { return CachePrefix + (int)t + "_" + (int)c; }

    CachedPage ReadCache(SharedTable t, SharedColumn c)
    {
        string key = CacheKey(t, c);
        CachedPage cp;
        if (memory.TryGetValue(key, out cp)) return cp;
        string json = PlayerPrefs.GetString(key, "");
        if (string.IsNullOrEmpty(json)) return null;
        try { cp = JsonUtility.FromJson<CachedPage>(json); }
        catch (Exception) { return null; }
        if (cp != null) memory[key] = cp;
        return cp;
    }

    void WriteCache(SharedTable t, SharedColumn c, LeaderboardPage page)
    {
        var cp = new CachedPage { savedAt = UnixNow(), fetchedAt = UnixNow(), hasPlayer = page.hasPlayer, player = page.player };
        cp.entries.AddRange(page.entries);
        memory[CacheKey(t, c)] = cp;
        PlayerPrefs.SetString(CacheKey(t, c), JsonUtility.ToJson(cp));
    }

    void InvalidateCache(SharedTable t)
    {
        foreach (var c in SharedDoc.Columns)
        {
            memory.Remove(CacheKey(t, c));
            PlayerPrefs.DeleteKey(CacheKey(t, c));
        }
    }

    static LeaderboardPage ToPage(CachedPage cp)
    {
        var page = new LeaderboardPage { status = LeaderboardStatus.Ok, hasPlayer = cp.hasPlayer, player = cp.player };
        page.entries.AddRange(cp.entries);
        return page;
    }

    // Loads one table's board sorted by one column. Reads cost 0 within CacheSeconds. `force` (a retry button)
    // bypasses the cache but not MinRefreshSeconds. A failed read serves the last cached page, with its age.
    public void LoadBoard(SharedTable table, SharedColumn column, bool force, Action<Loaded> done)
    {
        if (!Enabled) { done(new Loaded { page = LeaderboardPage.Failed(LeaderboardStatus.Unavailable) }); return; }
        if (!Backend.IsSignedIn)
        {
            done(new Loaded { page = LeaderboardPage.Failed(LeaderboardStatus.NotSignedIn) });
            if (PlatformSignedIn() && !Disconnected()) EnsureSignedIn();
            return;
        }
        long now = UnixNow();
        var cached = ReadCache(table, column);
        if (cached != null)
        {
            long age = Math.Max(0, now - cached.fetchedAt);
            bool fresh = age < CacheSeconds;
            if ((!force && fresh) || (force && age < MinRefreshSeconds))
            {
                done(new Loaded { page = ToPage(cached), fromCache = true, ageSeconds = age });
                return;
            }
        }
        Backend.LoadTop(table, column, PanelRows, page =>
        {
            if (page != null && page.Ok)
            {
                WriteCache(table, column, page);
                done(new Loaded { page = page });
                return;
            }
            if (cached != null)
            {
                done(new Loaded { page = ToPage(cached), fromCache = true, ageSeconds = Math.Max(0, UnixNow() - cached.fetchedAt) });
                return;
            }
            done(new Loaded { page = page ?? LeaderboardPage.Failed(LeaderboardStatus.Error) });
        });
    }

    // ---- delete ----

    public sealed class DeleteReport
    {
        public bool docsDeleted;   // false: nothing was removed, nothing changed
        public bool authDeleted;   // false: the Firebase user was signed out instead
    }

    // Removes the player's documents in both tables and the Firebase user, forgets every local shared record
    // and turns sharing off (uploads stay off until the player turns it on again).
    public void DeleteMyScores(Action<DeleteReport> done)
    {
        if (!Backend.IsAvailable || !Backend.IsSignedIn) { done(new DeleteReport()); return; }
        deleting = true;
        Backend.DeleteMine((docs, auth) =>
        {
            deleting = false;
            if (docs)
            {
                PlayerPrefs.DeleteKey(PendingKey);
                PlayerPrefs.DeleteKey(SubmittedKey);
                PlayerPrefs.DeleteKey(BoundUidKey);
                InvalidateCache(SharedTable.Main);
                InvalidateCache(SharedTable.Power);
                SharingOn = false;
                if (!auth) Backend.SignOut();
                PrefsSaver.SaveNow();
            }
            done(new DeleteReport { docsDeleted = docs, authDeleted = docs && auth });
        });
    }
}

// "Sync now": sends the device's bests to the native boards and the shared board.
public static class LeaderboardSync
{
    public struct Report { public int native, shared; }

    public static Report Now()
    {
        var r = new Report();
        r.shared = SharedLeaderboardService.Instance.SyncNow();   // reads the native marks before the native resend moves them
        r.native = LeaderboardService.Instance.SyncNow();
        return r;
    }
}
