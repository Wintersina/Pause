using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Auto sign-in, account cloud saves and per-platform achievements.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod AccountCloudSaveTest.Run
public static class AccountCloudSaveTest
{
    static int failures;

    static void Check(string label, bool condition)
    {
        if (!condition) failures++;
        Debug.Log("[ACS] " + (condition ? "PASS " : "FAIL ") + label);
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    // A platform account that answers synchronously, as scripted.
    sealed class FakeAccount : IPlayerAccount
    {
        public bool signInResult = true;
        public bool loadOk = true;
        public string cloudJson;
        public string id = "player-A";
        public bool signedIn;
        public int signIns, interactiveSignIns, loads;
        public readonly List<string> writes = new List<string>();

        public string PlatformName { get { return "fake"; } }
        public bool IsSignedIn { get { return signedIn; } }
        public string PlayerId { get { return signedIn ? id : null; } }

        public void SignIn(bool interactive, Action<bool> done)
        {
            signIns++;
            if (interactive) interactiveSignIns++;
            signedIn = signInResult;
            done(signInResult);
        }

        public void LoadCloudSave(Action<bool, string> done)
        {
            loads++;
            done(loadOk, loadOk ? cloudJson : null);
        }

        public void WriteCloudSave(string json, Action<bool> done)
        {
            writes.Add(json);
            cloudJson = json;
            done(true);
        }
    }

    public static int Execute()
    {
        failures = 0;
        using var sandbox = new TestHarness.Sandbox();
        var savedAccount = PlayerAccounts.Current;
        try
        {
            MergeRules();
            RoundTrip();
            CodexSync();
            DeveloperModeSnapshot();
            SignInFailureLeavesLocalAlone();
            LoadFailureLeavesLocalAlone();
            SignedInMergeAndUpload();
            AccountSwitch();
            AchievementIdTable();
            AchievementResync();
            ResyncRunsAfterSignIn();
            RemovedHomeButtons();
        }
        finally
        {
            PlayerAccounts.Current = savedAccount;
        }
        Debug.Log("[ACS] failures: " + failures);
        return failures;
    }

    // ---- helpers ----

    static readonly string[] CloudKeys =
    {
        CloudSync.LastAccountKey, CloudSync.LocalSavedAtKey, CloudSync.LocalHashKey, CloudSync.BackupsKey,
    };

    // Clears every synced progress key and the cloud bookkeeping.
    static void ClearProgress()
    {
        if (DeveloperUnlocks.Enabled) PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        ProgressSnapshot.Fresh(0).Apply();
        PlayerPrefs.DeleteKey("PlayerCurrecny");
        PlayerPrefs.DeleteKey("HighestSpeed");
        foreach (string key in CloudKeys) PlayerPrefs.DeleteKey(key);
        AchievementStore.ResetAll();
        AchievementStores.Current = null;
    }

    static ProgressSnapshot Snap(long at, float currency, int[] ships, int spawn, float speed,
                                 int world, int highest, bool tut, params (string, int)[] counters)
    {
        return new ProgressSnapshot
        {
            savedAtUtc = at, currency = currency, boughtShips = ships, spawnShip = spawn,
            highestSpeed = speed, currentWorld = world, highestWorld = highest, hasDoneTut = tut,
            counters = counters.Select(c => new ProgressSnapshot.Counter(c.Item1, c.Item2)).ToArray(),
        };
    }

    static void SetLocal(float currency, int[] ships, int spawn, float speed, int world, int highest, bool tut)
    {
        PlayerPrefs.SetFloat("PlayerCurrecny", currency);
        for (int i = 0; i <= shopingShips.shipTotal; i++) PlayerPrefs.DeleteKey("boughtship" + i);
        foreach (int s in ships) PlayerPrefs.SetString("boughtship" + s, "True");
        PlayerPrefs.SetInt("spawnShip", spawn);
        PlayerPrefs.SetFloat("HighestSpeed", speed);
        PlayerPrefs.SetInt("currentWorld", world);
        PlayerPrefs.SetInt("highestWorld", highest);
        if (tut) PlayerPrefs.SetString("HasDoneTut", "true"); else PlayerPrefs.DeleteKey("HasDoneTut");
    }

    static string Ships()
    {
        var owned = new List<int>();
        for (int i = 0; i <= shopingShips.shipTotal; i++)
            if (PlayerPrefs.GetString("boughtship" + i) == "True") owned.Add(i);
        return string.Join(",", owned);
    }

    // ---- merge ----

    static void MergeRules()
    {
        var local = Snap(1000, 50f, new[] { 1, 2 }, 2, 300f, 1, 1, false,
                         ("achv_count_aliens", 40), ("achv_count_stars", 10));
        var cloud = Snap(2000, 75f, new[] { 1, 4 }, 4, 250f, 2, 3, true,
                         ("achv_count_aliens", 12), ("achv_count_deaths", 7));

        var m = ProgressMerge.Merge(local, cloud);
        Check("merge: bought ships are the union", string.Join(",", m.boughtShips) == "1,2,4");
        Check("merge: HighestSpeed (legacy field, unused by the game since the speed cap) is still the max", m.highestSpeed == 300f);
        Check("merge: highestWorld is the max", m.highestWorld == 3);
        Check("merge: counters take the max per key",
              m.CounterValue("achv_count_aliens") == 40 && m.CounterValue("achv_count_deaths") == 7 &&
              m.CounterValue("achv_count_stars") == 10);
        Check("merge: tutorial done if either side did it", m.hasDoneTut);
        Check("merge: newer cloud supplies currency/spawnShip/currentWorld",
              m.currency == 75f && m.spawnShip == 4 && m.currentWorld == 2);
        Check("merge: result carries the newer timestamp", m.savedAtUtc == 2000);

        local.savedAtUtc = 3000;
        var m2 = ProgressMerge.Merge(local, cloud);
        Check("merge: newer local supplies currency/spawnShip/currentWorld",
              m2.currency == 50f && m2.spawnShip == 2 && m2.currentWorld == 1);
        Check("merge: ships/max rules don't depend on which side is newer",
              string.Join(",", m2.boughtShips) == "1,2,4" && m2.highestWorld == 3 && m2.highestSpeed == 300f);

        var neither = ProgressMerge.Merge(Snap(1, 0, new int[0], 0, 0, 0, 0, false),
                                          Snap(2, 0, new int[0], 0, 0, 0, 0, false));
        Check("merge: tutorial stays undone if neither side did it", !neither.hasDoneTut);
        Check("merge: a missing side returns the other",
              ProgressMerge.Merge(local, null) == local && ProgressMerge.Merge(null, cloud) == cloud);
    }

    // ---- serialisation ----

    static void RoundTrip()
    {
        ClearProgress();
        PlayerPrefs.SetFloat("PlayerCurrecny", 123.5f);
        PlayerPrefs.SetString("boughtship1", "True");
        PlayerPrefs.SetString("boughtship7", "True");
        PlayerPrefs.SetInt("spawnShip", 7);
        PlayerPrefs.SetFloat("HighestSpeed", 412f);
        PlayerPrefs.SetInt("currentWorld", 2);
        PlayerPrefs.SetInt("highestWorld", 3);
        PlayerPrefs.SetString("HasDoneTut", "true");
        PlayerPrefs.SetInt("achv_count_aliens", 31);
        PlayerPrefs.SetInt("achv_count_stars", 999);
        string legacy = AchievementMigration.LegacyProgressPrefix + "CgkI3eXNjrQcEAIQBA";
        PlayerPrefs.SetInt(legacy, 17);
        PlayerPrefs.SetInt("ach_u_loop_1", 1);
        PlayerPrefs.SetInt("ach_c_loop_1", 1);
        PlayerPrefs.SetInt("ach_n_kills", 321);
        PlayerPrefs.SetInt(AchievementStore.BossFlagKey(2), 1);
        // Device-only keys that must stay out of the cloud.
        PlayerPrefs.SetInt(DeveloperUnlocks.SelectedWorldKey, 2);

        string json = ProgressSnapshot.Capture(5555).ToJson();
        Check("json has a schema version", json.Contains("\"schemaVersion\":1"));
        Check("json has the timestamp", json.Contains("\"savedAtUtc\":5555"));
        Check("json leaves developer settings out", !json.Contains("developer") && !json.Contains("devStartWorld"));

        ClearProgress();
        PlayerPrefs.SetString("boughtship3", "True");   // must be gone after Apply
        ProgressSnapshot parsed;
        Check("json parses", ProgressSnapshot.TryParse(json, out parsed) == ProgressSnapshot.ParseResult.Ok);
        parsed.Apply();

        Check("round trip: PlayerCurrecny", PlayerPrefs.GetFloat("PlayerCurrecny") == 123.5f);
        Check("round trip: boughtship1/7 are \"True\", others gone",
              PlayerPrefs.GetString("boughtship1") == "True" && PlayerPrefs.GetString("boughtship7") == "True" &&
              !PlayerPrefs.HasKey("boughtship3") && Ships() == "1,7");
        Check("round trip: spawnShip", PlayerPrefs.GetInt("spawnShip") == 7);
        Check("round trip: HighestSpeed (legacy field: old saves keep it)", PlayerPrefs.GetFloat("HighestSpeed") == 412f);
        Check("round trip: currentWorld/highestWorld",
              PlayerPrefs.GetInt("currentWorld") == 2 && PlayerPrefs.GetInt("highestWorld") == 3);
        Check("round trip: HasDoneTut is \"true\"", PlayerPrefs.GetString("HasDoneTut") == "true");
        Check("round trip: achv_count_* counters",
              PlayerPrefs.GetInt("achv_count_aliens") == 31 && PlayerPrefs.GetInt("achv_count_stars") == 999);
        Check("round trip: achv_progress_* legacy key", PlayerPrefs.GetInt(legacy) == 17);
        Check("round trip: achievement unlocked / claimed / counter / boss-set keys",
              PlayerPrefs.GetInt("ach_u_loop_1") == 1 && PlayerPrefs.GetInt("ach_c_loop_1") == 1 &&
              PlayerPrefs.GetInt("ach_n_kills") == 321 && PlayerPrefs.GetInt(AchievementStore.BossFlagKey(2)) == 1);
        Check("round trip: the device-local store marks are NOT synced",
              !ProgressSnapshot.CounterKeys().Contains(AchievementCatalog.Find("loop_1").syncedKey) &&
              !ProgressSnapshot.CounterKeys().Contains(AchievementStore.SchemaKey));
        Check("round trip: equal content after re-capture",
              ProgressSnapshot.Capture(5555).ToJson() == json);

        ProgressSnapshot ignored;
        Check("parse: empty is 'no save'", ProgressSnapshot.TryParse("", out ignored) == ProgressSnapshot.ParseResult.Empty);
        Check("parse: garbage is invalid", ProgressSnapshot.TryParse("{not json", out ignored) == ProgressSnapshot.ParseResult.Invalid);
        Check("parse: a newer schema is refused",
              ProgressSnapshot.TryParse("{\"schemaVersion\":2}", out ignored) == ProgressSnapshot.ParseResult.NewerSchema);
    }

    // ---- codex (discoveries, NEW markers, acknowledgements) ----

    static ProgressSnapshot CodexSnap(long at, string seen, string fresh, string ack)
    {
        return new ProgressSnapshot
        {
            savedAtUtc = at,
            codexSeen = ProgressSnapshot.IdList(seen),
            codexNew = ProgressSnapshot.IdList(fresh),
            codexNewAck = ProgressSnapshot.IdList(ack),
        };
    }

    static string J(string[] ids) { return string.Join(",", ids ?? new string[0]); }

    static void CodexSync()
    {
        ClearProgress();
        Codex.Reload();

        // round trip through json and PlayerPrefs; stored sorted and de-duplicated
        PlayerPrefs.SetString(Codex.PrefsKey, "c,a,b,a");
        PlayerPrefs.SetString(Codex.NewKey, "c,b");
        PlayerPrefs.SetString(Codex.AckKey, "c,ach:x");
        var cap = ProgressSnapshot.Capture(1);
        Check("codex: capture is sorted and de-duplicated",
              J(cap.codexSeen) == "a,b,c" && J(cap.codexNew) == "b,c" && J(cap.codexNewAck) == "ach:x,c");
        string json = cap.ToJson();
        ClearProgress();
        Check("codex: clearing removes the keys",
              !PlayerPrefs.HasKey(Codex.PrefsKey) && !PlayerPrefs.HasKey(Codex.NewKey) && !PlayerPrefs.HasKey(Codex.AckKey));
        ProgressSnapshot parsed;
        Check("codex: json parses", ProgressSnapshot.TryParse(json, out parsed) == ProgressSnapshot.ParseResult.Ok);
        parsed.Apply();
        Check("codex: round trip restores all three keys",
              PlayerPrefs.GetString(Codex.PrefsKey) == "a,b,c" && PlayerPrefs.GetString(Codex.NewKey) == "b,c" &&
              PlayerPrefs.GetString(Codex.AckKey) == "ach:x,c");
        Check("codex: json stays compact (< 2 KB for 5 ids)", json.Length < 2048);

        // union of discoveries
        var m = ProgressMerge.Merge(CodexSnap(10, "a,b", "", ""), CodexSnap(20, "b,c", "", ""));
        Check("codex merge: discoveries are a union", J(m.codexSeen) == "a,b,c");

        // new on one side only (other side has never met it) stays new
        m = ProgressMerge.Merge(CodexSnap(10, "a", "a", ""), CodexSnap(20, "b", "", ""));
        Check("codex merge: new on a side that alone knows it stays new", J(m.codexNew) == "a");

        // cleared on either side never comes back, whichever side is newer
        m = ProgressMerge.Merge(CodexSnap(10, "a,b", "a,b", ""), CodexSnap(20, "a,b", "b", ""));
        Check("codex merge: opened on the cloud (older local) stays opened", J(m.codexNew) == "b");
        m = ProgressMerge.Merge(CodexSnap(20, "a,b", "b", ""), CodexSnap(10, "a,b", "a,b", ""));
        Check("codex merge: opened on the device stays opened", J(m.codexNew) == "b");

        // acknowledged on either side stays acknowledged
        m = ProgressMerge.Merge(CodexSnap(10, "a,b", "a,b", "a"), CodexSnap(20, "a,b", "a,b", "b,ach:q"));
        Check("codex merge: acknowledged on either side stays acknowledged",
              J(m.codexNew) == "a,b" && J(m.codexNewAck) == "a,ach:q,b");
        m = ProgressMerge.Merge(CodexSnap(10, "a,b", "a,b", "a,b"), CodexSnap(20, "a,b", "b", ""));
        Check("codex merge: a cleared entry drops its ack, no resurrected dot",
              J(m.codexNew) == "b" && J(m.codexNewAck) == "b");

        // an older client's snapshot (no codex fields at all) changes nothing
        ProgressSnapshot old;
        Check("codex: an old snapshot without the fields parses",
              ProgressSnapshot.TryParse("{\"schemaVersion\":1,\"savedAtUtc\":5,\"currency\":3}", out old) == ProgressSnapshot.ParseResult.Ok &&
              old.codexSeen.Length == 0 && old.codexNew.Length == 0 && old.codexNewAck.Length == 0);
        m = ProgressMerge.Merge(CodexSnap(10, "a,b", "b", "b"), old);
        Check("codex merge: with an old snapshot the device keeps its state",
              J(m.codexSeen) == "a,b" && J(m.codexNew) == "b" && J(m.codexNewAck) == "b");
        ProgressSnapshot future;
        Check("codex: unknown future fields do not break parsing",
              ProgressSnapshot.TryParse("{\"schemaVersion\":1,\"codexSeen\":[\"a\"],\"codexSomethingNew\":[1,2]}", out future) == ProgressSnapshot.ParseResult.Ok &&
              J(future.codexSeen) == "a");

        // restore on a fresh install: restored discoveries do not light up
        ClearProgress();
        Codex.Reload();
        var real = Codex.Entries.Where(e => e.category != CodexCategory.Log && !e.secret).Take(3).ToArray();
        string ids = string.Join(",", real.Select(e => e.id));
        var cloud = CodexSnap(100, ids, "", "");
        var fresh = ProgressSnapshot.Capture(0);
        var restored = ProgressMerge.Merge(fresh, cloud);
        restored.Apply();
        Check("codex restore: discoveries come back", Codex.IsDiscovered(real[0].id) && Codex.IsDiscovered(real[2].id));
        Check("codex restore: nothing is NEW, no dots, no bubble count",
              !Codex.IsNew(real[0].id) && Codex.UnackedNewCount == 0 && !Codex.TabHasNew(real[0].category));
        // a NEW marker the cloud itself carried survives the restore
        ClearProgress();
        restored = ProgressMerge.Merge(ProgressSnapshot.Capture(0), CodexSnap(100, ids, real[1].id, ""));
        restored.Apply();
        Check("codex restore: only the cloud's own NEW entry is NEW",
              Codex.IsNew(real[1].id) && !Codex.IsNew(real[0].id) && Codex.UnackedNewCount == 1);

        // the cached sets are dropped by Apply, so the next change cannot undo the restore
        Codex.MarkSeen(real[1].id);
        Check("codex restore: marking seen after a restore clears the dot",
              !Codex.IsNew(real[1].id) && PlayerPrefs.GetString(Codex.NewKey) == "");

        // size bound
        var many = new List<string>();
        for (int i = 0; i < 5000; i++) many.Add("id" + i);
        PlayerPrefs.SetString(Codex.PrefsKey, string.Join(",", many));
        Check("codex: lists are capped", ProgressSnapshot.Capture(0).codexSeen.Length == ProgressSnapshot.MaxCodexIds);
        var entryIds = Codex.Entries.Select(e => e.id).ToList();
        PlayerPrefs.SetString(Codex.PrefsKey, string.Join(",", entryIds));
        PlayerPrefs.SetString(Codex.NewKey, string.Join(",", entryIds));
        PlayerPrefs.SetString(Codex.AckKey, string.Join(",", entryIds));
        int full = ProgressSnapshot.Capture(0).ToJson().Length;
        Check("codex: a snapshot with every entry in all three lists stays under 16 KB (" + full + ")", full < 16384);

        ClearProgress();
        Codex.Reload();
    }

    // ---- developer mode ----

    static void DeveloperModeSnapshot()
    {
        ClearProgress();
        SetLocal(40f, new[] { 1, 3 }, 3, 200f, 1, 1, false);
        DeveloperUnlocks.SetEnabled(true);
        Check("dev mode really unlocked everything live",
              PlayerPrefs.GetString("boughtship9") == "True" && PlayerPrefs.GetString("HasDoneTut") == "true");
        PlayerPrefs.SetInt("currentWorld", 3);   // a developer run on a locked world
        PlayerPrefs.SetInt("spawnShip", 9);      // flying an unowned ship

        var snap = ProgressSnapshot.Capture(1);
        Check("dev mode: snapshot has the real ships", string.Join(",", snap.boughtShips) == "1,3");
        Check("dev mode: snapshot has the real tutorial flag", !snap.hasDoneTut);
        Check("dev mode: snapshot has the real highestWorld", snap.highestWorld == 1);
        Check("dev mode: snapshot has the real currentWorld, not the dev run's", snap.currentWorld == 1);
        Check("dev mode: an unowned spawn ship isn't synced", snap.spawnShip == shopingShips.StarterShip);
        Check("dev mode: real currency still synced", snap.currency == 40f);

        // A cloud merge while in dev mode lands in the real (backed-up) progress.
        var merged = ProgressMerge.Merge(snap, Snap(2, 60f, new[] { 1, 5 }, 5, 250f, 2, 2, true));
        merged.Apply();
        Check("dev mode: applying keeps the unlocked live state",
              PlayerPrefs.GetString("boughtship9") == "True" &&
              PlayerPrefs.GetInt("highestWorld") == WorldManager.Worlds.Length - 1);
        DeveloperUnlocks.SetEnabled(false);
        Check("dev mode off: merged real progress restored", Ships() == "1,3,5" &&
              PlayerPrefs.GetString("HasDoneTut") == "true" && PlayerPrefs.GetInt("highestWorld") == 2);
        Check("dev mode off: merged real currentWorld restored", PlayerPrefs.GetInt("currentWorld") == 2);
    }

    // ---- sign-in / load failures ----

    static string ProgressFingerprint()
    {
        return ProgressSnapshot.Capture(0).ToJson() + "|" + PlayerPrefs.GetFloat("PlayerCurrecny");
    }

    static void SignInFailureLeavesLocalAlone()
    {
        ClearProgress();
        SetLocal(88f, new[] { 1, 2 }, 2, 150f, 1, 1, true);
        string before = ProgressFingerprint();
        var fake = new FakeAccount { signInResult = false, cloudJson = Snap(9, 1f, new[] { 1 }, 1, 1, 0, 0, false).ToJson() };
        var sync = new CloudSync(fake, () => 100000);
        sync.Start();
        sync.Poll(urgent: true, force: true);
        Check("sign-in failure: state is Offline", sync.Current == CloudSync.State.Offline);
        Check("sign-in failure: progress untouched", ProgressFingerprint() == before);
        Check("sign-in failure: no cloud read or write", fake.loads == 0 && fake.writes.Count == 0);
        Check("sign-in failure: only the one silent attempt", fake.signIns == 1 && fake.interactiveSignIns == 0);
        Check("sign-in failure: no account recorded", !PlayerPrefs.HasKey(CloudSync.LastAccountKey));
    }

    static void LoadFailureLeavesLocalAlone()
    {
        ClearProgress();
        SetLocal(88f, new[] { 1, 2 }, 2, 150f, 1, 1, true);
        string before = ProgressFingerprint();
        var fake = new FakeAccount { loadOk = false };
        var sync = new CloudSync(fake, () => 100000);
        sync.Start();
        PlayerPrefs.SetFloat("PlayerCurrecny", 99f);   // a later local change
        sync.Poll(urgent: true, force: true);
        Check("cloud read failure: never uploads over the unread cloud save", fake.writes.Count == 0);
        PlayerPrefs.SetFloat("PlayerCurrecny", 88f);
        Check("cloud read failure: progress untouched", ProgressFingerprint() == before);
    }

    // ---- the signed-in flow ----

    static void SignedInMergeAndUpload()
    {
        ClearProgress();
        SetLocal(10f, new[] { 1, 2 }, 2, 100f, 0, 0, false);
        long now = 500000;
        var fake = new FakeAccount
        {
            cloudJson = Snap(now + 1000, 70f, new[] { 1, 6 }, 6, 400f, 2, 2, true, ("achv_count_aliens", 9)).ToJson(),
        };
        var sync = new CloudSync(fake, () => now) ;
        sync.Start();
        Check("signed in: state is Ready", sync.Current == CloudSync.State.Ready);
        Check("signed in: ships merged into local", Ships() == "1,2,6");
        Check("signed in: newer cloud currency applied", PlayerPrefs.GetFloat("PlayerCurrecny") == 70f);
        Check("signed in: maxes applied (legacy HighestSpeed field, highestWorld)", PlayerPrefs.GetFloat("HighestSpeed") == 400f && PlayerPrefs.GetInt("highestWorld") == 2);
        Check("signed in: tutorial flag applied", PlayerPrefs.GetString("HasDoneTut") == "true");
        Check("signed in: counter applied", PlayerPrefs.GetInt("achv_count_aliens") == 9);
        Check("signed in: account remembered", PlayerPrefs.GetString(CloudSync.LastAccountKey) == "player-A");

        sync.Poll(urgent: true, force: true);
        ProgressSnapshot uploaded;
        bool parsed = fake.writes.Count == 1 &&
                      ProgressSnapshot.TryParse(fake.writes[0], out uploaded) == ProgressSnapshot.ParseResult.Ok &&
                      string.Join(",", uploaded.boughtShips) == "1,2,6";
        Check("signed in: merged progress uploaded once", parsed);

        sync.Poll(urgent: true, force: true);
        Check("signed in: no upload without a change", fake.writes.Count == 1);

        now += 1000;
        PlayerPrefs.SetString("boughtship8", "True");   // a purchase
        sync.Poll();
        Check("debounce: a change right after an upload waits", fake.writes.Count == 1);
        now += CloudSync.UploadIntervalMs;
        sync.Poll();
        Check("debounce: uploaded once the interval passed", fake.writes.Count == 2 && fake.writes[1].Contains("\"boughtShips\":[1,2,6,8]"));
    }

    static void AccountSwitch()
    {
        ClearProgress();
        SetLocal(30f, new[] { 1, 2 }, 2, 120f, 1, 1, true);
        PlayerPrefs.SetString(CloudSync.LastAccountKey, "player-A");
        PlayerPrefs.SetInt(AchievementCatalog.Find("kills_100").syncedKey, 100);

        // B has a cloud save: it wins outright (no union with A's ships).
        var b = new FakeAccount { id = "player-B", cloudJson = Snap(1, 5f, new[] { 1, 9 }, 9, 50f, 0, 0, false).ToJson() };
        new CloudSync(b, () => 600000) .Start();
        Check("switch: the new account's cloud progress replaces local", Ships() == "1,9" &&
              PlayerPrefs.GetFloat("PlayerCurrecny") == 5f && PlayerPrefs.GetFloat("HighestSpeed") == 50f &&
              !PlayerPrefs.HasKey("HasDoneTut"));
        Check("switch: last account is now B", PlayerPrefs.GetString(CloudSync.LastAccountKey) == "player-B");
        Check("switch: achievement confirmations reset",
              !PlayerPrefs.HasKey(AchievementCatalog.Find("kills_100").syncedKey));
        Check("switch: A's progress parked on the device", PlayerPrefs.GetString(CloudSync.BackupsKey).Contains("player-A"));

        // A comes back with no cloud save (its upload never happened): the parked progress returns.
        var a = new FakeAccount { id = "player-A", cloudJson = null };
        new CloudSync(a, () => 700000) .Start();
        Check("switch back: A's parked progress restored", Ships() == "1,2" &&
              PlayerPrefs.GetFloat("PlayerCurrecny") == 30f && PlayerPrefs.GetString("HasDoneTut") == "true");

        // A brand-new account with nothing anywhere starts fresh.
        var c = new FakeAccount { id = "player-C", cloudJson = null };
        new CloudSync(c, () => 800000) .Start();
        Check("switch to a new account: starts fresh", Ships() == "" &&
              PlayerPrefs.GetFloat("PlayerCurrecny") == 0f && !PlayerPrefs.HasKey("HasDoneTut"));

        // First sign-in ever on a device: local progress is kept and merged.
        ClearProgress();
        SetLocal(12f, new[] { 1, 4 }, 4, 90f, 0, 0, true);
        var first = new FakeAccount { id = "player-D", cloudJson = Snap(1, 3f, new[] { 1, 5 }, 5, 10f, 0, 0, false).ToJson() };
        new CloudSync(first, () => 900000) .Start();
        Check("first sign-in: local progress merged, not replaced", Ships() == "1,4,5" && PlayerPrefs.GetFloat("HighestSpeed") == 90f);
    }

    // ---- achievements ----

    static void AchievementIdTable()
    {
        var defs = AchievementCatalog.All;
        var android = defs.Select(AchievementIds.AndroidId).ToList();
        var ios = defs.Select(AchievementIds.IosId).ToList();
        Check("ids: every achievement has an Android and an iOS id",
              android.All(id => !string.IsNullOrEmpty(id)) && ios.All(id => id.StartsWith(AchievementIds.AchievementIosPrefix)));
        Check("ids: no duplicate Android ids", android.Distinct().Count() == android.Count);
        Check("ids: no duplicate iOS ids", ios.Distinct().Count() == ios.Count);
        Check("ids: iOS ids use only Game Center-safe characters",
              ios.All(id => id.All(ch => char.IsLetterOrDigit(ch) || ch == '.' || ch == '_')));
        Check("ids: placeholders are recognised, real ids are not",
              AchievementIds.IsPlaceholder(AchievementIds.Placeholder("loop_1")) && AchievementIds.IsPlaceholder("") &&
              !AchievementIds.IsPlaceholder("CgkI3eXNjrQcEAIQBw"));
        Check("ids: iOS reporting stays off until the Game Center ids exist",
              !AchievementIds.IsReportable(defs[0], true));
        Check("ids: points fit Game Center's 1000 cap", defs.Sum(d => d.points) <= 1000 && defs.All(d => d.points <= 100));
    }

    static void AchievementResync()
    {
        ClearProgress();
        var fake = new FakeAchievementStore { available = true };
        AchievementStores.Current = fake;
        var kills100 = AchievementCatalog.Find("kills_100");
        var kills1000 = AchievementCatalog.Find("kills_1000");
        var tut = AchievementCatalog.Find("meta_first_flight");
        // Earned while signed out: the unlock is local, nothing was reported.
        fake.available = false;
        AchievementStore.Unlock(tut);
        AchievementStore.SetCounter(AchievementCatalog.CKills, 130);
        Check("resync: nothing reported while signed out", fake.reports.Count == 0 && AchievementStore.IsUnlocked(tut));
        fake.available = true;
        var sent = AchievementSync.ResyncAll();
        Check("resync: the offline unlock is reported after sign-in (100%)",
              fake.reports.Any(r => r.Key == "meta_first_flight" && r.Value == 100.0));
        Check("resync: the completed counter is reported at 100% too",
              fake.reports.Any(r => r.Key == "kills_100" && r.Value == 100.0));
        Check("resync: the open counter reports its percent (13%)",
              fake.reports.Any(r => r.Key == "kills_1000" && Math.Abs(r.Value - 13.0) < 1e-9));
        Check("resync: confirmed marks remembered", AchievementSync.Confirmed(tut) == 100 && AchievementSync.Confirmed(kills100) == 100 &&
              AchievementSync.Confirmed(kills1000) == 13);
        fake.reports.Clear();
        AchievementSync.ResyncAll();
        Check("resync again: nothing the store already confirmed", fake.reports.Count == 0);

        fake.reports.Clear();
        AchievementStore.Unlock(AchievementCatalog.Find("world_frost_reached"));
        fake.acceptOk = false;
        AchievementStore.Unlock(AchievementCatalog.Find("world_verdant_reached"));
        Check("resync: a failed report confirms nothing", AchievementSync.Confirmed(AchievementCatalog.Find("world_verdant_reached")) == 0 &&
              AchievementSync.Confirmed(AchievementCatalog.Find("world_frost_reached")) == 100);
        fake.acceptOk = true;
        fake.reports.Clear();
        AchievementSync.ResyncAll();
        Check("resync: ... and is retried next time", fake.reports.Count == 1 && fake.reports[0].Key == "world_verdant_reached");
        AchievementStores.Current = null;
    }

    static void ResyncRunsAfterSignIn()
    {
        ClearProgress();
        SetLocal(0f, new[] { 1 }, 1, 0f, 0, 0, true);
        AchievementStore.Unlock(AchievementCatalog.Find("world_ember_reached"));
        var fakeStore = new FakeAchievementStore { available = false };
        AchievementStores.Current = fakeStore;

        var offline = new FakeAccount { signInResult = false };
        new CloudSync(offline, () => 1).Start();
        Check("resync: nothing reported while signed out", fakeStore.reports.Count == 0);

        fakeStore.available = true;
        var online = new FakeAccount { cloudJson = null };
        new CloudSync(online, () => 2).Start();
        Check("resync: offline progress reported after sign-in",
              fakeStore.reports.Any(r => r.Key == "world_ember_reached" && r.Value == 100.0));
        AchievementStores.Current = null;
    }

    // ---- home screen ----

    static void RemovedHomeButtons()
    {
        const string scenePath = "Assets/Scenes/startS4.unity";
        EditorSceneManager.OpenScene(scenePath);
        var names = new HashSet<string>();
        foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            names.Add(t.name);
        Check("startS4: home scene loaded", names.Contains("QuitButton") && names.Contains("PlayButton"));
        Check("startS4: no log-out button", !names.Contains("LogOutButton"));
        Check("startS4: no top-right sign-in icon", !names.Contains("LoginButton"));
        Check("startS4: no 'logged out' message", !names.Contains("LoggedoutText"));

        string yaml = File.ReadAllText(scenePath);
        Check("startS4: no UnityEvent calls the removed handlers",
              !yaml.Contains("m_MethodName: login\n") && !yaml.Contains("m_MethodName: logoutButton"));
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        Check("startMenu: login/logout handlers are gone",
              typeof(startMenu).GetMethod("login", flags) == null && typeof(startMenu).GetMethod("logoutButton", flags) == null);
    }
}
