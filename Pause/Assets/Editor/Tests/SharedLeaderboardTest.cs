using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// The shared (Firebase) leaderboard, SDK-free half (docs/shared-leaderboard.md), against FakeSharedBoard:
//   1  the model: runs, the per-column merge, marks from the native boards
//   2  the rules mirror: SharedBoardLimits against firebase/test-vectors.json (the same file the emulator test reads)
//   3  the service: dormant, routing, improve-only queue, offline/denied/quota, sign-in, uid binding, sign-out
//   4  reading: true #1 per column, the caching rule, failures serve the cache
//   5  sync now and delete my shared scores
//   6  the hook in LeaderboardService.SubmitRun and the native SyncNow
public static class SharedLeaderboardTest
{
    static int failures;

    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SLB] PASS  " : "[SLB] FAIL  ") + what);
        if (!ok) failures++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static float now;
    static long unix;
    static FakeSharedBoard fake;

    static readonly string[] Keys =
    {
        SharedLeaderboardService.SharingKey, SharedLeaderboardService.PendingKey, SharedLeaderboardService.SubmittedKey,
        SharedLeaderboardService.BoundUidKey,
        LeaderboardService.PendingKey, LeaderboardService.SubmittedKey, LeaderboardService.NormalBestKey, LeaderboardService.BoostedBestKey,
        LeaderboardService.BoundPlayerKey, LeaderboardService.SeasonKey, RunScore.BestScoreKey, AccountLink.DisconnectedKey,
    };

    public static int Execute()
    {
        failures = 0;
        using var sandbox = new TestHarness.Sandbox();
        var settling = LeaderboardService.CloudSettling;
        var shared = SharedLeaderboardService.HasInstance ? SharedLeaderboardService.Instance : null;
        var native = LeaderboardService.HasInstance ? LeaderboardService.Instance : null;
        LeaderboardService.CloudSettling = () => false;
        try
        {
            Model();
            Vectors();
            ServiceTests();
            Reading();
            SyncAndDelete();
            Hook();
        }
        finally
        {
            LeaderboardService.CloudSettling = settling;
            SharedLeaderboardService.Instance = shared;
            LeaderboardService.Instance = native;
            Context();
        }
        Debug.Log("[SLB] failures: " + failures);
        return failures;
    }

    static void Context()
    {
        foreach (string k in Keys) PlayerPrefs.DeleteKey(k);
        for (int t = 0; t < 2; t++) for (int c = 0; c < 3; c++) PlayerPrefs.DeleteKey(SharedLeaderboardService.CachePrefix + t + "_" + c);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
    }

    // A service wired to a fresh fake. Platform signed in, one fake clock for the queue and one for the server.
    static SharedLeaderboardService Service(bool signedIn = true)
    {
        Context();
        now = 100f;
        unix = 1700000000;
        fake = new FakeSharedBoard { SignedIn = signedIn };
        fake.Now = 1000000 + now;
        var s = new SharedLeaderboardService(fake, () => now)
        {
            PlatformSignedIn = () => true,
            Disconnected = () => false,
            PlayerName = () => "KANEDA",
            UnixNow = () => unix,
        };
        SharedLeaderboardService.Instance = s;
        return s;
    }

    static void Advance(SharedLeaderboardService s, float seconds)
    {
        now += seconds;
        unix += (long)seconds;
        fake.Now = 1000000 + now;
        s.Tick();
    }

    static LeaderboardRunStats Stats(long score, float dust, int worlds, int ship = 7, int powerUps = 0, float earned = -1f)
    {
        return new LeaderboardRunStats
        {
            score = score, starDust = dust, dustEarned = earned < 0f ? dust : earned, worldsCleared = worlds, startShip = ship, startHearts = 4,
            hasEnd = true, endHearts = 2, hasPauses = true, endPauses = 3, powerUps = powerUps,
        };
    }

    // ---- 1 model ----

    static void Model()
    {
        var run = SharedRun.From(Stats(5000, 12.34f, 4));
        Check("model: a normal run keeps score, dust in hundredths, worlds and the start data",
              run.score == 5000 && run.dust == 1234 && run.worlds == 4 && run.ship == 7 && run.hearts == 4 && run.endHearts == 2
              && run.pauses == 3 && run.powerUps == 0);
        var boosted = SharedRun.From(Stats(5000, 12.34f, 4, 7, LeaderboardRunTag.PowerAll, 20.5f));
        Check("model: a boosted run keeps all the dust it earned and its power-up bits", boosted.dust == 2050 && boosted.powerUps == 3);
        var unknownEnd = SharedRun.From(new LeaderboardRunStats { score = 10, startShip = 3, startHearts = 3 });
        Check("model: no end hearts and no pauses are unknown (-1)", unknownEnd.endHearts == -1 && unknownEnd.pauses == -1);

        // The per-column merge: each slot keeps its own best run, whole.
        var a = SharedRun.From(Stats(9000, 1f, 2, 5));   // best score
        var b = SharedRun.From(Stats(4000, 30f, 3, 6));  // best dust
        var c = SharedRun.From(Stats(3000, 2f, 9, 8));   // best worlds
        var doc = new SharedDoc();
        bool changed = doc.Offer(a);
        Check("model: the first run fills all three slots", changed && doc.IsFull && doc.byScore.ship == 5 && doc.byDust.ship == 5 && doc.byWorlds.ship == 5);
        doc.Offer(b); doc.Offer(c);
        Check("model: each slot holds its column's own best run",
              doc.byScore.ship == 5 && doc.byDust.ship == 6 && doc.byWorlds.ship == 8
              && doc.byScore.score == 9000 && doc.byDust.dust == 3000 && doc.byWorlds.worlds == 9);
        Check("model: a worse run changes nothing", !doc.Offer(SharedRun.From(Stats(10, 0.1f, 1))));
        Check("model: slots stay consistent after merges", SharedBoardLimits.Consistent(doc));
        var tieWithShip = SharedRun.From(Stats(9000, 1f, 2, 11));
        var blank = new SharedDoc(); var tagless = SharedRun.Blank(); tagless.score = 9000; tagless.dust = 100; tagless.worlds = 2;
        blank.Offer(tagless);
        Check("model: a tie that adds the ship replaces a ship-less run", blank.Offer(tieWithShip) && blank.byScore.ship == 11);

        bool ch;
        var merged = SharedBoardMerge.Apply(null, doc, "NAME", out ch);
        Check("model: merge into no document creates it with the name", ch && merged.name == "NAME" && merged.IsFull);
        merged = SharedBoardMerge.Apply(merged, doc, "OTHER", out ch);
        Check("model: merging the same runs again changes nothing and keeps the name", !ch && merged.name == "NAME");

        // Marks from the native boards.
        string tag = LeaderboardRunTag.Encode(7, 4, 2, 3, 0, 1234, 4, 0, 5000);
        SharedRun m;
        Check("marks: a Top Score mark with its tag becomes the whole run",
              SharedRun.TryFromMark(LeaderboardBoards.TopScore, 5000, tag, out m) && m.score == 5000 && m.dust == 1234 && m.worlds == 4 && m.ship == 7);
        Check("marks: a Worlds mark with no tag has only the worlds",
              SharedRun.TryFromMark(LeaderboardBoards.FurthestWorld, 6, "", out m) && m.worlds == 6 && m.ship == 0 && m.score == 1);
        Check("marks: a Power-Ups mark without power-up bits is refused", !SharedRun.TryFromMark(LeaderboardBoards.BoostedRuns, 100, "s7h4", out m));
        Check("marks: a Power-Ups mark with bits is kept",
              SharedRun.TryFromMark(LeaderboardBoards.BoostedRuns, 100, LeaderboardRunTag.Encode(7, 4, 2, 3, 0, 500, 2, 3), out m) && m.powerUps == 3 && m.score == 100);
        Check("marks: a premium tag is refused", !SharedRun.TryFromMark(LeaderboardBoards.TopScore, 100, "s16h14", out m));
        Check("marks: an unknown board is refused", !SharedRun.TryFromMark("top_speed", 100, "", out m));

        Check("name: control characters are dropped and 24 characters kept",
              SharedBoardLimits.CleanName("Ab\ncd" + new string('x', 40), "u") .Length == 24
              && SharedBoardLimits.CleanName("A\tB", "u") == "AB");
        Check("name: empty becomes PILOT plus the last four of the uid", SharedBoardLimits.CleanName("  ", "abcdef1234") == "PILOT1234");
    }

    // ---- 2 the rules mirror ----

    [Serializable]
    sealed class Vector
    {
        public string name, table, op, expect;
        public bool owner, rulesOnly, hasExisting;
        public SharedDoc doc, existing;
        public int sinceLastSeconds;
        public string provider;
    }

    [Serializable]
    sealed class VectorFile { public List<Vector> cases; }

    static void Vectors()
    {
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "firebase", "test-vectors.json"));
        if (!File.Exists(path)) { Check("vectors: firebase/test-vectors.json exists", false); return; }
        var file = JsonUtility.FromJson<VectorFile>(File.ReadAllText(path));
        Check("vectors: the file lists cases", file != null && file.cases != null && file.cases.Count >= 30);
        int ran = 0, wrong = 0;
        foreach (var v in file.cases)
        {
            if (v.rulesOnly) continue;
            var table = v.table == "power" ? SharedTable.Power : SharedTable.Main;
            v.doc.hasScore = v.doc.hasDust = v.doc.hasWorlds = true;
            bool allow;
            string provider = string.IsNullOrEmpty(v.provider) ? SharedBoardLimits.ProviderFor(v.doc.platform) : v.provider;
            if (!v.owner) allow = false;
            else if (v.hasExisting)
            {
                v.existing.hasScore = v.existing.hasDust = v.existing.hasWorlds = true;
                allow = SharedBoardLimits.AllowUpdate(v.existing, v.doc, table, v.sinceLastSeconds, provider);
            }
            else allow = SharedBoardLimits.AllowCreate(v.doc, table, provider);
            ran++;
            if (allow != (v.expect == "allow")) { wrong++; Check("vectors: " + v.name, false); }
        }
        Check("rules mirror: lists of 25 or fewer are open to anyone, 26 or none are refused",
              SharedBoardLimits.AllowList(10) && SharedBoardLimits.AllowList(25) && !SharedBoardLimits.AllowList(26) && !SharedBoardLimits.AllowList(0));
        Check("rules mirror: the provider ids", SharedBoardLimits.ProviderFor("android") == "playgames.google.com" && SharedBoardLimits.ProviderFor("ios") == "gc.apple.com");
        {
            var board = new FakeSharedBoard { PlatformId = SharedBoardLimits.Ios };
            var ov = new SharedDoc();
            var run = SharedRun.Blank(); run.score = 100; run.ship = 3; run.hearts = 3;
            ov.Offer(run);
            SharedResult res = SharedResult.Error;
            board.Upload(SharedTable.Main, "IOSPILOT", ov, (r, d) => res = r);
            var stored = board.Get(SharedTable.Main, board.Uid);
            Check("platform: an ios upload stores platform ios and the row carries it",
                  res == SharedResult.Ok && stored != null && stored.platform == "ios"
                  && SharedBoardRows.ToEntry(1, "u", stored, SharedColumn.Score, false).platform == "ios");
            var bad = new FakeSharedBoard { PlatformId = SharedBoardLimits.Ios, SignInProvider = SharedBoardLimits.ProviderAndroid };
            res = SharedResult.Ok;
            bad.Upload(SharedTable.Main, "X", ov, (r, d) => res = r);
            Check("platform: ios with the Play Games provider is denied", res == SharedResult.Denied);
        }
        Check("vectors: the C# mirror agrees with every non-rules-only case (" + ran + " run)", ran >= 25 && wrong == 0);
    }

    // ---- 3 the service ----

    static void ServiceTests()
    {
        // Dormant: no backend, nothing queued.
        Context();
        var dormant = new SharedLeaderboardService(new NullSharedBoard(), () => 0f);
        SharedLeaderboardService.Instance = dormant;
        Check("dormant: the Null backend is not enabled and queues nothing",
              !dormant.Enabled && dormant.OfferRun(Stats(5000, 1f, 1)) == 0 && !dormant.HasPending
              && !PlayerPrefs.HasKey(SharedLeaderboardService.PendingKey));

        var s = Service();
        Check("service: enabled with an available backend and sharing on by default", s.Enabled);
        SharedLeaderboardService.SharingOn = false;
        Check("sharing off: nothing is queued", !s.Enabled && s.OfferRun(Stats(5000, 1f, 1)) == 0 && !s.HasPending);
        SharedLeaderboardService.SharingOn = true;

        // Routing.
        Check("routing: a normal run is queued for Main only",
              s.OfferRun(Stats(5000, 1f, 1)) == 3 && !s.Pending(SharedTable.Main).IsEmpty && s.Pending(SharedTable.Power).IsEmpty);
        Context();
        Check("routing: a boosted run is queued for Power-Ups only",
              s.OfferRun(Stats(5000, 1f, 1, 7, 1)) == 3 && s.Pending(SharedTable.Main).IsEmpty && !s.Pending(SharedTable.Power).IsEmpty);
        Context();
        Check("routing: the premium ship is queued nowhere",
              s.OfferRun(Stats(5000, 1f, 1, PremiumShips.Paragon)) == 0 && !s.HasPending);
        var premiumFlag = Stats(5000, 1f, 1); premiumFlag.premiumRun = true;
        Check("routing: a run that ever flew the premium ship is queued nowhere", s.OfferRun(premiumFlag) == 0 && !s.HasPending);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        Check("routing: developer mode is queued nowhere", s.OfferRun(Stats(5000, 1f, 1)) == 0 && !s.HasPending);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        startMenu.youAreInTutorial = true;
        bool tutorialBlocked = LeaderboardService.SubmissionBlocked();
        Check("routing: the tutorial is queued nowhere", !tutorialBlocked || s.OfferRun(Stats(5000, 1f, 1)) == 0);
        startMenu.youAreInTutorial = false;
        Check("routing: a run with an out-of-range value is refused", s.OfferRun(Stats(0, 1f, 1)) == 0 && !s.HasPending);

        // Improve-only queue, debounce, one upload per table.
        s = Service();
        s.OfferRun(Stats(9000, 1f, 2, 5));
        s.OfferRun(Stats(4000, 30f, 3, 6));
        s.OfferRun(Stats(3000, 2f, 9, 8));
        Check("queue: three runs fill three slots (each column's best)",
              s.Pending(SharedTable.Main).byScore.ship == 5 && s.Pending(SharedTable.Main).byDust.ship == 6 && s.Pending(SharedTable.Main).byWorlds.ship == 8);
        Check("queue: nothing is sent before the debounce", fake.UploadCalls == 0);
        Advance(s, 1f);
        Check("queue: still nothing at 1 s", fake.UploadCalls == 0);
        Advance(s, 1.5f);
        Check("queue: one upload after the debounce", fake.UploadCalls == 1 && fake.Uploads.Count == 1);
        var server = fake.Get(SharedTable.Main, fake.Uid);
        Check("queue: the server doc holds each column's own best run, named",
              server != null && server.name == "KANEDA" && server.byScore.score == 9000 && server.byDust.dust == 3000 && server.byWorlds.worlds == 9
              && server.byScore.ship == 5 && server.byDust.ship == 6 && server.byWorlds.ship == 8);
        Check("queue: a confirmed upload empties the queue and is remembered",
              !s.HasPending && s.Submitted(SharedTable.Main).IsFull && s.UploadCount == 1);
        Check("queue: a worse run is dropped (improve-only)", s.OfferRun(Stats(100, 0.5f, 1)) == 0 && !s.HasPending);
        Check("queue: a run that improves one column is queued for that column only",
              s.OfferRun(Stats(9500, 1f, 2, 9)) == 1 && s.Pending(SharedTable.Main).hasScore && !s.Pending(SharedTable.Main).hasDust);

        // The server's 15 s write interval is respected by the client (the fake enforces it like the rules).
        Advance(s, 2.5f);
        Check("interval: the second upload waits for the write interval", fake.UploadCalls == 1);
        Advance(s, 13f);
        Check("interval: and then goes out without a denial", fake.UploadCalls == 2 && !s.HasPending && fake.Get(SharedTable.Main, fake.Uid).byScore.score == 9500);

        // Offline: stays queued, retried with backoff.
        s = Service();
        fake.Fail = SharedResult.Offline;
        s.OfferRun(Stats(7000, 1f, 1));
        Advance(s, 3f);
        Check("offline: the first attempt fails and the run stays queued", fake.UploadCalls == 1 && s.HasPending);
        Advance(s, 2f);
        Check("offline: no retry inside the 3 s backoff", fake.UploadCalls == 1);
        fake.Fail = null;
        Advance(s, 2f);
        Check("offline: back online the run goes out", fake.UploadCalls == 2 && !s.HasPending && fake.Get(SharedTable.Main, fake.Uid) != null);

        // Denied (rules or App Check): parked after 8 refusals.
        s = Service();
        fake.Fail = SharedResult.Denied;
        s.OfferRun(Stats(7000, 1f, 1));
        for (int i = 0; i < 40 && fake.UploadCalls < SharedLeaderboardService.MaxRejectedSends; i++) Advance(s, 301f);
        Check("denied: refused writes stop after " + SharedLeaderboardService.MaxRejectedSends + " tries", fake.UploadCalls == SharedLeaderboardService.MaxRejectedSends && s.HasPending);
        int calls = fake.UploadCalls;
        fake.Fail = null;
        for (int i = 0; i < 5; i++) Advance(s, 301f);
        Check("denied: and stays parked for the session (nothing else is blocked)", fake.UploadCalls == calls && s.HasPending);
        s.FlushNow();
        Check("denied: FlushNow tries once more", fake.UploadCalls == calls + 1 && !s.HasPending);

        // Quota.
        s = Service();
        fake.Fail = SharedResult.Quota;
        s.OfferRun(Stats(7000, 1f, 1));
        Advance(s, 3f);
        fake.Fail = null;
        Advance(s, 600f);
        Check("quota: the run waits an hour, not a tight loop", fake.UploadCalls == 1 && s.HasPending);
        Advance(s, 3100f);
        Check("quota: and goes out after the wait", fake.UploadCalls == 2 && !s.HasPending);

        // Sign-in.
        s = Service(signedIn: false);
        fake.SignInSucceeds = false;
        s.OfferRun(Stats(7000, 1f, 1));
        Advance(s, 3f);
        Check("sign-in: a refused Firebase sign-in keeps the run queued and uploads nothing",
              fake.SignInCalls == 1 && fake.UploadCalls == 0 && s.HasPending);
        Advance(s, 5f);
        Check("sign-in: it is not retried inside a minute", fake.SignInCalls == 1);
        fake.SignInSucceeds = true;
        Advance(s, 60f);
        Check("sign-in: then it signs in and uploads", fake.SignInCalls == 2 && fake.UploadCalls == 1 && !s.HasPending);

        s = Service(signedIn: false);
        s.PlatformSignedIn = () => false;
        s.OfferRun(Stats(7000, 1f, 1));
        Advance(s, 3f);
        Check("sign-in: without the Play Games / Game Center account nothing is attempted", fake.SignInCalls == 0 && fake.UploadCalls == 0 && s.HasPending);

        // Disconnected in Pause.
        s = Service();
        bool disconnected = true;
        s.Disconnected = () => disconnected;
        s.OfferRun(Stats(7000, 1f, 1));
        Advance(s, 3f);
        Check("disconnected: SIGN OUT in Pause stops uploads and signs out of Firebase", fake.UploadCalls == 0 && s.HasPending && !fake.IsSignedIn);
        disconnected = false;
        Advance(s, 60f);
        s.Flush();
        Check("disconnected: signing back in sends the queue", fake.UploadCalls == 1 && !s.HasPending);

        // A different Firebase uid forgets the submitted records and queues the device's bests again.
        s = Service();
        s.OfferRun(Stats(7000, 1f, 1));
        Advance(s, 3f);
        Check("uid: the submitted record is bound to the uid", PlayerPrefs.GetString(SharedLeaderboardService.BoundUidKey, "") == fake.Uid);
        var tags = LeaderboardService.ScoreTable.Load(LeaderboardService.SubmittedKey);
        tags.Set(LeaderboardBoards.TopScore, 7000, LeaderboardRunTag.Encode(7, 4, 2, 3, 0, 100, 1, 0, 7000));
        tags.Save(LeaderboardService.SubmittedKey);
        fake.Uid = "uid-second";
        s.Flush();
        Check("uid: a new uid drops the old record and re-queues the device's bests from the native marks",
              !s.Submitted(SharedTable.Main).IsFull && PlayerPrefs.GetString(SharedLeaderboardService.BoundUidKey, "") == "uid-second");
        Advance(s, 3f);
        Advance(s, 13f);
        Check("uid: and uploads them under the new uid", fake.Get(SharedTable.Main, "uid-second") != null);
    }

    // ---- 4 reading ----

    static SharedDoc Player(string name, long score, long dust, int worlds, int ship, int powerUps = 0)
    {
        var d = new SharedDoc { name = name };
        var run = SharedRun.Blank();
        run.ship = ship; run.hearts = 4; run.powerUps = powerUps;
        var r1 = run; r1.score = score;
        var r2 = run; r2.score = Math.Max(1, score / 2); r2.dust = dust;
        var r3 = run; r3.score = Math.Max(1, score / 3); r3.worlds = worlds;
        d.Set(SharedColumn.Score, r1); d.Set(SharedColumn.Dust, r2); d.Set(SharedColumn.Worlds, r3);
        // dominate: keep the slots consistent
        r1.dust = Math.Max(r1.dust, dust); r1.worlds = Math.Max(r1.worlds, worlds);
        d.Set(SharedColumn.Score, r1);
        r2.worlds = worlds; d.Set(SharedColumn.Dust, r2);
        r3.dust = dust; d.Set(SharedColumn.Worlds, r3);
        return d;
    }

    static void Reading()
    {
        var s = Service();
        // ada has the best score, bob the best dust, cyd the best worlds, and every player is mid-table elsewhere.
        fake.Seed(SharedTable.Main, "ada", Player("ADA", 9000, 100, 2, 3));
        fake.Seed(SharedTable.Main, "bob", Player("BOB", 3000, 9000, 3, 4));
        fake.Seed(SharedTable.Main, "cyd", Player("CYD", 2400, 500, 9, 5));
        fake.Seed(SharedTable.Main, "zed", Player("ZED", 100, 0, 0, 6));
        Loaded(s, SharedTable.Main, SharedColumn.Score, false, out var byScore);
        Loaded(s, SharedTable.Main, SharedColumn.Dust, false, out var byDust);
        Loaded(s, SharedTable.Main, SharedColumn.Worlds, false, out var byWorlds);
        Check("read: sorting by Score puts the best score first", byScore.page.Ok && byScore.page.entries[0].playerName == "ADA");
        Check("read: sorting by Dust puts the best dust first, the full run on the row",
              byDust.page.entries[0].playerName == "BOB" && byDust.page.entries[0].value == 9000 && byDust.page.entries[0].startShip == 4
              && byDust.page.entries[0].hasRunStats && byDust.page.entries[0].starDustHundredths == 9000);
        Check("read: sorting by Worlds puts the most worlds first", byWorlds.page.entries[0].playerName == "CYD" && byWorlds.page.entries[0].worlds == 9);
        Check("read: players with nothing in Dust / Worlds are left out of those boards",
              byDust.page.entries.TrueForAll(e => e.playerName != "ZED") && byWorlds.page.entries.TrueForAll(e => e.playerName != "ZED"));
        Check("read: ranks count from 1", byScore.page.entries[0].rank == 1 && byScore.page.entries[1].rank == 2);
        Check("read: no own document yet, so no own row", !byScore.page.hasPlayer);

        // Own row, on the page and off it.
        s.OfferRun(Stats(5000, 3f, 4, 7));
        Advance(s, 3f);
        Loaded(s, SharedTable.Main, SharedColumn.Score, true, out var again);   // cache was dropped by the upload
        Check("read: an own upload drops that table's cached pages", !again.fromCache);
        Check("read: the own row is on the page with its rank", again.page.hasPlayer && again.page.player.isLocalPlayer && again.page.player.rank == 2);
        for (int i = 0; i < 10; i++) fake.Seed(SharedTable.Main, "w" + i, Player("W" + i, 100, 0, 20 + i, 2));
        unix += 400;
        Loaded(s, SharedTable.Main, SharedColumn.Worlds, false, out var worlds2);
        Check("read: the own row off the page has its run but no rank number",
              worlds2.page.entries.Count == 10 && worlds2.page.hasPlayer && worlds2.page.player.rank == 0 && worlds2.page.player.worlds == 4);

        // The caching rule.
        s = Service();
        fake.Seed(SharedTable.Main, "ada", Player("ADA", 9000, 100, 2, 3));
        Loaded(s, SharedTable.Main, SharedColumn.Score, false, out var l1);
        Check("cache: the first open reads the server", fake.LoadCalls == 1 && !l1.fromCache);
        Loaded(s, SharedTable.Main, SharedColumn.Score, false, out var l2);
        Check("cache: reopening within 5 minutes costs no reads", fake.LoadCalls == 1 && l2.fromCache && l2.page.entries.Count == 1);
        unix += 120;
        Loaded(s, SharedTable.Main, SharedColumn.Score, true, out var l3);
        Check("cache: after 120 s a forced refresh reads again", fake.LoadCalls == 2 && !l3.fromCache);
        Loaded(s, SharedTable.Main, SharedColumn.Score, true, out var l4);
        Check("cache: a second forced refresh right away is limited to once a minute", fake.LoadCalls == 2 && l4.fromCache);
        unix += 301;
        Loaded(s, SharedTable.Main, SharedColumn.Score, false, out var l5);
        Check("cache: after 5 minutes an ordinary open reads again", fake.LoadCalls == 3 && !l5.fromCache);
        Loaded(s, SharedTable.Main, SharedColumn.Dust, false, out var l6);
        Check("cache: each (table, sort) has its own page", fake.LoadCalls == 4 && !l6.fromCache);
        Loaded(s, SharedTable.Power, SharedColumn.Score, false, out var l7);
        Check("cache: and so does each table", fake.LoadCalls == 5 && !l7.fromCache);

        // The cache survives a relaunch (PlayerPrefs) and serves when the load fails.
        var s2 = new SharedLeaderboardService(fake, () => now) { PlatformSignedIn = () => true, Disconnected = () => false, UnixNow = () => unix };
        fake.Fail = SharedResult.Offline;
        unix += 400;
        Loaded(s2, SharedTable.Main, SharedColumn.Score, false, out var l8);
        Check("cache: a failed load serves the last page with its age", l8.fromCache && l8.ageSeconds >= 400 && l8.page.entries.Count == 1);
        Loaded(s2, SharedTable.Power, SharedColumn.Dust, false, out var l9);
        Check("cache: a failed load with nothing cached is an error page, not an exception", !l9.page.Ok && l9.page.status == LeaderboardStatus.Error);
        fake.Fail = null;

        // Other states.
        var dormant = new SharedLeaderboardService(new NullSharedBoard(), () => 0f);
        Loaded(dormant, SharedTable.Main, SharedColumn.Score, false, out var l10);
        Check("read: no backend is the Unavailable state", l10.page.status == LeaderboardStatus.Unavailable);
        fake.SignedIn = false;
        Loaded(s, SharedTable.Main, SharedColumn.Score, false, out var l11);
        Check("read: signed out is the NotSignedIn state", l11.page.status == LeaderboardStatus.NotSignedIn);
        fake.SignedIn = true;
        fake.DeferLoads = true;
        unix += 1000;
        SharedLeaderboardService.Loaded pending = null;
        s.LoadBoard(SharedTable.Main, SharedColumn.Score, false, l => pending = l);
        Check("read: a deferred load stays loading", pending == null);
        fake.CompleteLoads();
        Check("read: and completes later", pending != null && pending.page.Ok);
        fake.DeferLoads = false;
    }

    static void Loaded(SharedLeaderboardService s, SharedTable t, SharedColumn c, bool force, out SharedLeaderboardService.Loaded result)
    {
        SharedLeaderboardService.Loaded got = null;
        s.LoadBoard(t, c, force, l => got = l);
        result = got ?? new SharedLeaderboardService.Loaded { page = LeaderboardPage.Failed(LeaderboardStatus.Error) };
    }

    // ---- 5 sync now, delete ----

    static void SyncAndDelete()
    {
        var s = Service();
        // The native record: a Top Score mark with its run, a Worlds mark with a different run, a Power-Ups mark.
        var marks = LeaderboardService.ScoreTable.Load(LeaderboardService.SubmittedKey);
        marks.Set(LeaderboardBoards.TopScore, 9000, LeaderboardRunTag.Encode(5, 4, 2, 3, 0, 150, 2, 0, 9000));
        marks.Set(LeaderboardBoards.RunStarDust, 4000, LeaderboardRunTag.Encode(6, 4, 1, 1, 0, 4000, 3, 0, 3000));
        marks.Set(LeaderboardBoards.FurthestWorld, 9, LeaderboardRunTag.Encode(8, 4, 0, 0, 0, 200, 9, 0, 2000));
        marks.Set(LeaderboardBoards.BoostedRuns, 8000, LeaderboardRunTag.Encode(5, 4, 2, 3, 0, 900, 2, 1));
        marks.Save(LeaderboardService.SubmittedKey);
        int queued = s.SyncNow();
        Check("sync: the marks are queued for both tables", queued >= 6);
        var main = fake.Get(SharedTable.Main, fake.Uid);
        var power = fake.Get(SharedTable.Power, fake.Uid);
        Check("sync: Main is rebuilt per column from the marks' runs",
              main != null && main.byScore.ship == 5 && main.byScore.score == 9000 && main.byDust.ship == 6 && main.byDust.dust == 4000
              && main.byWorlds.ship == 8 && main.byWorlds.worlds == 9);
        Check("sync: Power-Ups is rebuilt from its mark", power != null && power.byScore.score == 8000 && power.byScore.powerUps == 1);
        Check("sync: nothing is written twice", fake.UploadCalls == 2);

        s = Service();
        PlayerPrefs.SetInt(LeaderboardService.NormalBestKey, 1234);
        Check("sync: the local best alone is offered as a score-only run", s.SyncNow() == 3 && fake.Get(SharedTable.Main, fake.Uid).byScore.score == 1234);
        s = Service();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        PlayerPrefs.SetInt(LeaderboardService.NormalBestKey, 1234);
        Check("sync: developer mode sends nothing", s.SyncNow() == 0 && fake.UploadCalls == 0);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        s = Service();
        SharedLeaderboardService.SharingOn = false;
        PlayerPrefs.SetInt(LeaderboardService.NormalBestKey, 1234);
        Check("sync: sharing off sends nothing", s.SyncNow() == 0 && fake.UploadCalls == 0);
        SharedLeaderboardService.SharingOn = true;

        // Delete my shared scores.
        s = Service();
        s.OfferRun(Stats(9000, 1f, 2));
        s.OfferRun(Stats(8000, 1f, 2, 7, 1));
        Advance(s, 3f);
        s.OfferRun(Stats(9500, 1f, 2));   // queued, not yet sent
        SharedLeaderboardService.DeleteReport report = null;
        s.DeleteMyScores(r => report = r);
        Check("delete: both tables' documents and the Auth user are removed",
              report != null && report.docsDeleted && report.authDeleted
              && fake.Get(SharedTable.Main, "uid-local") == null && fake.Get(SharedTable.Power, "uid-local") == null);
        Check("delete: the queue, the records and the cache are forgotten, sharing is off",
              !s.HasPending && !PlayerPrefs.HasKey(SharedLeaderboardService.SubmittedKey) && !SharedLeaderboardService.SharingOn);
        Check("delete: nothing is queued afterwards", s.OfferRun(Stats(9999, 1f, 2)) == 0);
        SharedLeaderboardService.SharingOn = true;

        s = Service();
        fake.AuthDeleteSucceeds = false;
        s.OfferRun(Stats(9000, 1f, 2));
        Advance(s, 3f);
        s.DeleteMyScores(r => report = r);
        Check("delete: when the Auth user cannot be deleted the player is signed out instead",
              report.docsDeleted && !report.authDeleted && !fake.IsSignedIn);
        SharedLeaderboardService.SharingOn = true;

        s = Service();
        s.OfferRun(Stats(9000, 1f, 2));
        Advance(s, 3f);
        fake.DeleteFails = true;
        s.DeleteMyScores(r => report = r);
        Check("delete: offline changes nothing", !report.docsDeleted && fake.Get(SharedTable.Main, "uid-local") != null && SharedLeaderboardService.SharingOn);
    }

    // ---- 6 the hook ----

    static void Hook()
    {
        var s = Service();
        var platform = new FakeLeaderboards();
        var native = new LeaderboardService(platform, () => now) { Ios = false, PlayerId = () => "p1" };
        LeaderboardService.Instance = native;
        int accepted = native.SubmitRun(Stats(5000, 2f, 3));
        Check("hook: SubmitRun feeds the native boards and the shared board", accepted > 0 && !s.Pending(SharedTable.Main).IsEmpty);
        Check("hook: the shared queue holds the same run the native tag describes", s.Pending(SharedTable.Main).byScore.score == 5000 && s.Pending(SharedTable.Main).byScore.ship == 7);

        s = Service();
        native = new LeaderboardService(platform, () => now) { Ios = false, PlayerId = () => "p1" };
        LeaderboardService.Instance = native;
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        native.SubmitRun(Stats(5000, 2f, 3));
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        Check("hook: a blocked run reaches neither", !s.HasPending);

        // A failing shared board never touches the native boards.
        s = Service();
        fake.Fail = SharedResult.Error;
        native = new LeaderboardService(platform, () => now) { Ios = false, PlayerId = () => "p1" };
        LeaderboardService.Instance = native;
        native.SubmitRun(Stats(6000, 2f, 3));
        native.Flush();
        Advance(s, 3f);
        Check("hook: a failing shared board leaves the native submission alone", platform.Submissions.Count > 0 && s.HasPending);

        // Native SyncNow: queued again and resent.
        s = Service();
        platform = new FakeLeaderboards();
        native = new LeaderboardService(platform, () => now) { Ios = false, PlayerId = () => "p1" };
        LeaderboardService.Instance = native;
        native.SubmitRun(Stats(7000, 2f, 3));
        native.Flush();
        int first = platform.Submissions.Count;
        for (int i = 0; i < 10; i++) { now += 5f; native.Tick(); }
        int settled = platform.Submissions.Count;
        int queuedAgain = native.SyncNow();
        Check("native sync: the confirmed marks go out again", first > 0 && queuedAgain > 0 && platform.Submissions.Count > settled);
        var report = LeaderboardSync.Now();
        Check("sync now: one call reports both", report.native >= 0 && report.shared >= 0);
    }
}
