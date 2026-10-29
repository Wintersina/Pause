using System.Linq;
using UnityEngine;

// Codex star dust: the payout table (every entry covered, 5..25, only the super rare pay 25); a find (entry, ship,
// log, combo, song, backdrop) pays NOTHING by itself and is a pending reward; COLLECT (per item, per tab, all) pays
// it exactly once (also across a reload, a cloud restore and a merge); older saves' finds are simply pending, ids
// already in the paid list stay collected; the starter ship is never pending; tutorial / developer mode pay nothing.
public static class CodexPayoutTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[CPO] PASS  " : "[CPO] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static float Dust() { return PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey); }

    static void Reset(string seen, float dust)
    {
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetString(Codex.PrefsKey, seen);
        PlayerPrefs.DeleteKey(Codex.PaidKey);
        PlayerPrefs.DeleteKey(Codex.BackPaidKey);
        PlayerPrefs.DeleteKey(WorldManager.PrefsHighestWorld);
        for (int i = 1; i <= shopingShips.RosterTotal; i++) PlayerPrefs.DeleteKey("boughtship" + i);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, dust);
        Codex.Reload();
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.SetString("HasDoneTut", "true");

        // ---- the table ----
        var superRare = CodexPayout.SuperRareIds.ToArray();
        Check("two super rare entries, both real catalogue entries", superRare.Length == 2 && superRare.All(id => Codex.Find(id) != null));
        string badTable = null;
        foreach (var e in Codex.Entries)
        {
            int v = CodexPayout.For(e);
            if (e.category == CodexCategory.Log) { if (v != CodexPayout.LogEntry) badTable = e.id + ": a Log entry pays 5, got " + v; }
            else if (v < CodexPayout.Min || v > CodexPayout.Max) badTable = e.id + ": pays " + v + " (outside 5..25)";
            else if ((v == CodexPayout.Max) != superRare.Contains(e.id)) badTable = e.id + ": pays " + v + " but super rare is " + superRare.Contains(e.id);
            if (badTable != null) break;
        }
        Check("every entry has a payout: 5..25, only the super rare ones pay 25 (" + (badTable ?? "ok") + ")", badTable == null);
        Check("a bigger prize for rarer kinds: boss > elite > common enemy",
              CodexPayout.Boss > CodexPayout.Elite && CodexPayout.Elite > CodexPayout.EnemyBase);
        int full = Codex.Entries.Sum(e => CodexPayout.For(e));
        Check("a full Codex is a bonus, not a shortcut (" + full + " dust)", full > 0 && full < 1500);

        var first = Codex.Entries.First(e => e.category == CodexCategory.Enemies && !e.secret && BossCatalog.Find(e.id) == null && EliteCatalog.FindByCodexId(e.id) == null);
        var boss = Codex.Entries.First(e => BossCatalog.Find(e.id) != null);

        string logIds = string.Join(",", Codex.Entries.Where(e => e.category == CodexCategory.Log).Select(e => e.id));

        // ---- a discovery pays nothing and is pending ----
        Reset("", 100f);
        Check("fresh save: pays 0 and only the free Log entries are pending (the starter ship is not)",
              Dust() == 100f && Codex.PendingTotal == Codex.PendingIn(CodexCategory.Log) && Codex.PendingIn(CodexCategory.Ships) == 0 && Codex.PendingIn(CodexCategory.Enemies) == 0);
        var starter = Codex.Entries.First(e => e.category == CodexCategory.Ships && CodexCatalogue.ShipIndex(e.id) == ShipId.Starter);
        Check("the starter ship is never pending and collect pays nothing for it", !Codex.IsRewardPending(starter) && Codex.CollectReward(starter.id) == 0 && Dust() == 100f);
        Check("discover: true on the first contact", Codex.Discover(first.id));
        Check("discover: pays nothing, the entry is pending with its table value",
              Dust() == 100f && Codex.IsRewardPending(first) && !Codex.IsRewardCollected(first.id) && Codex.PendingDust(first) == CodexPayout.For(first));
        Check("discover again: false, still nothing paid", !Codex.Discover(first.id) && Dust() == 100f);
        Codex.Reload();
        Check("after a reload: still pending, still unpaid", Codex.IsRewardPending(first) && Dust() == 100f);
        Codex.Discover(boss.id);
        Check("a boss is pending with its own (larger) value", Codex.PendingDust(boss) == CodexPayout.For(boss) && CodexPayout.For(boss) > CodexPayout.For(first));

        // ---- collect pays exactly once ----
        int logsDust = Codex.PendingDustIn(CodexCategory.Log);
        int c1 = Codex.CollectReward(first.id);
        Check("collect: pays the table value once, marked paid in the list", c1 == CodexPayout.For(first) && Dust() == 100f + c1 && Codex.IsRewardCollected(first.id) && !Codex.IsRewardPending(first));
        Check("collect again: nothing", Codex.CollectReward(first.id) == 0 && Dust() == 100f + c1);
        Codex.Reload();
        Check("after a reload: collected stays collected, collect pays nothing", Codex.CollectReward(first.id) == 0 && Dust() == 100f + c1);

        // cloud restore / merge / older snapshot never pay or un-collect
        float afterOne = Dust();
        var snap = ProgressSnapshot.Capture(1);
        var stale = ProgressSnapshot.Capture(2);
        stale.codexPaid = new string[0];
        stale.currency = afterOne;
        stale.Apply();
        Codex.Reload();
        Check("restore of an older save: the paid ids are kept (no double pay), the boss is still pending",
              Codex.CollectReward(first.id) == 0 && Dust() == afterOne && Codex.IsRewardPending(boss));
        var other = ProgressSnapshot.Capture(3);
        other.codexPaid = new[] { boss.id };
        var merged = ProgressMerge.Merge(snap, other);
        Check("merge: the paid list is a union", merged.codexPaid.Contains(first.id) && merged.codexPaid.Contains(boss.id));
        merged.Apply();
        Codex.Reload();
        Check("restoring the merged save: both are collected, collect all pays only the logs still owed",
              !Codex.IsRewardPending(boss) && Codex.CollectAllRewards() == logsDust && Codex.CollectAllRewards() == 0);

        // ---- tutorial / developer mode ----
        Reset("", 100f);
        DeveloperUnlocks.SetEnabled(true);
        Check("developer mode: no discovery, nothing pending, nothing collectable",
              !Codex.Discover(first.id) && Dust() == 100f && Codex.PendingTotal == 0 && Codex.CollectAllRewards() == 0 && Codex.CollectReward("log_pilot") == 0);
        DeveloperUnlocks.SetEnabled(false);

        // ---- the flag key stays readable (old saves / cloud merges) but nothing sets it ----
        Reset("", 100f);
        var withFlag = ProgressSnapshot.Capture(4);
        withFlag.codexBackPaid = true;
        var noFlag = ProgressSnapshot.Capture(5);
        noFlag.codexBackPaid = false;
        Check("merge: the retired back-pay flag is still OR-ed", ProgressMerge.Merge(withFlag, noFlag).codexBackPaid && ProgressMerge.Merge(noFlag, withFlag).codexBackPaid);
        withFlag.Apply();
        Codex.Reload();
        Check("a snapshot with the flag restores it and pays nothing; no lump back-pay exists", Codex.BackPayDone && Dust() == 100f && Codex.PendingTotal == Codex.PendingIn(CodexCategory.Log));

        // ---- older saves: finds are pending, ids in the paid list stay collected ----
        var older = Codex.Entries.Where(e => e.category != CodexCategory.Log && !e.secret && e.category != CodexCategory.Ships && e.category != CodexCategory.Worlds).Take(4).ToArray();
        Reset(string.Join(",", older.Select(e => e.id)) + ",music_world_ember,bd_frost_2", 100f);
        PlayerPrefs.SetString(Codex.PaidKey, older[0].id + "," + logIds);   // one entry (and the logs) were already paid by the auto-pay builds
        PlayerPrefs.SetString("boughtship2", "True");                       // a ship owned counts as discovered too
        Codex.Reload();
        var ship = Codex.Entries.First(e => e.id == CodexCatalogue.ShipPrefix + ShipId.KeyOf(2));
        int expect = older.Skip(1).Sum(e => CodexPayout.For(e)) + CodexPayout.For(ship) + CodexPayout.Track + CodexPayout.Backdrop;
        Check("old save: already-paid ids are not pending, every other find is", !Codex.IsRewardPending(older[0]) && older.Skip(1).All(e => Codex.IsRewardPending(e)) &&
              Codex.IsRewardPending(ship) && Codex.IsExtraPending("music_world_ember") && Codex.IsExtraPending("bd_frost_2") && Codex.PendingTotal == 3 + 1 + 2 && Dust() == 100f);
        int paid = Codex.CollectAllRewards();
        Check("old save: COLLECT ALL pays the table sum of the pending finds once (" + paid + " = " + expect + ")", paid == expect && Dust() == 100f + expect && Codex.PendingTotal == 0);
        Check("old save: a second collect pays nothing", Codex.CollectAllRewards() == 0 && Dust() == 100f + expect);

        // ---- every kind has a table value: combos, songs, backdrops ----
        string badExtra = null;
        foreach (var f in AtomFusionTable.All)
        {
            int v = CodexPayout.ForExtra(CodexPayout.FusionPayId(f));
            if (v < 8 || v > 15) badExtra = f.key + " pays " + v;
        }
        foreach (var t in CodexMusic.All)
        {
            if (t.id == CodexMusic.ThemeId) continue;   // the codex theme is always open: nothing to find
            int v = CodexPayout.ForExtra(t.id);
            if (v < CodexPayout.Min || v > 15) badExtra = t.id + " pays " + v;
        }
        foreach (var b in CodexBackdrops.All)
        {
            int v = CodexPayout.ForExtra(b.id);
            if (v < CodexPayout.Min || v > 15) badExtra = b.id + " pays " + v;
        }
        Check("every combo pays 8..15 and every song and backdrop 5..15 (" + (badExtra ?? "ok") + ")", badExtra == null);
        Check("the triple combo pays the top of the combo range", CodexPayout.ForExtra(CodexPayout.FusionPrefix + "singularity") == CodexPayout.FusionTriple);
        Check("unknown ids pay nothing", CodexPayout.ForExtra("music_nope") == 0 && CodexPayout.ForExtra("bd_nope") == 0 && CodexPayout.ForExtra("fusion_nope") == 0);

        // ---- a combo: found = pending, collect pays once ----
        var bloom = AtomFusionTable.All[(int)FusionId.Bloom];
        string bloomId = CodexPayout.FusionPayId(bloom);
        Reset("", 100f);
        PlayerPrefs.SetString(Codex.PaidKey, logIds);
        PlayerPrefs.DeleteKey(FusionDiscoveryStore.PrefsKey);
        FusionDiscoveryStore.Reload();
        Check("combo: not found yet: not pending", !Codex.IsFusionPending(bloom) && Codex.PendingTotal == 0);
        FusionDiscoveryStore.RecordFiring(FusionId.Bloom, 1, 20261026);
        Codex.OnFusionFound(bloom);
        Check("combo: first firing pays nothing, it is pending with its value", Dust() == 100f && Codex.IsFusionPending(bloom) && Codex.PendingDust(bloomId) == CodexPayout.FusionPair && Codex.PendingInExtra(Codex.ExtraKind.Combos) == 1);
        Check("combo: collect pays it once", Codex.CollectReward(bloomId) == CodexPayout.FusionPair && Dust() == 100f + CodexPayout.FusionPair && Codex.CollectReward(bloomId) == 0);
        Codex.Reload();
        Check("combo: after a reload still collected", !Codex.IsFusionPending(bloom) && Codex.CollectExtras(Codex.ExtraKind.Combos) == 0);

        // ---- songs and backdrops: heard / seen = pending, collect per item and per kind ----
        Reset("", 100f);
        PlayerPrefs.SetString(Codex.PaidKey, logIds);
        string track = CodexMusic.All.First(t => t.id.StartsWith("music_world_")).id, bossTrack = CodexMusic.All.First(t => t.id.StartsWith("music_boss_")).id;
        string bd = CodexBackdrops.All[0].id;
        Codex.Reload();
        Check("hear a song: true once, pays nothing, pending", Codex.HearTrack(track) && !Codex.HearTrack(track) && Dust() == 100f && Codex.IsRewardPending(track));
        Check("boss song and backdrop are pending with their own values", Codex.HearTrack(bossTrack) && Codex.SeeBackdrop(bd) && Dust() == 100f &&
              Codex.PendingDust(bossTrack) == CodexPayout.BossTrack && Codex.PendingDust(bd) == CodexPayout.Backdrop);
        Check("the music tab counts both songs, the backdrops count one", Codex.PendingInExtra(Codex.ExtraKind.Music) == 2 && Codex.PendingInExtra(Codex.ExtraKind.Backdrops) == 1 &&
              Codex.PendingDustInExtra(Codex.ExtraKind.Music) == CodexPayout.Track + CodexPayout.BossTrack);
        Check("collect one song: pays its value once", Codex.CollectReward(track) == CodexPayout.Track && Codex.CollectReward(track) == 0 && Dust() == 100f + CodexPayout.Track);
        Check("collect the music tab: the boss song only", Codex.CollectExtras(Codex.ExtraKind.Music) == CodexPayout.BossTrack && Codex.PendingInExtra(Codex.ExtraKind.Music) == 0);
        Check("the WORLDS tab collect includes the backdrop", Codex.PendingIn(CodexCategory.Worlds) == 1 && Codex.CollectRewards(CodexCategory.Worlds) == CodexPayout.Backdrop && Codex.PendingTotal == 0);

        // ---- COLLECT ALL sums everything: entries, combos, songs, backdrops ----
        Reset(first.id, 100f);
        PlayerPrefs.SetString(Codex.PaidKey, logIds);
        PlayerPrefs.DeleteKey(FusionDiscoveryStore.PrefsKey);
        FusionDiscoveryStore.Reload();
        FusionDiscoveryStore.RecordFiring(FusionId.Singularity, 1, 20261026);
        Codex.Reload();
        Codex.HearTrack(track);
        Codex.SeeBackdrop(bd);
        int all = CodexPayout.For(first) + CodexPayout.FusionTriple + CodexPayout.Track + CodexPayout.Backdrop;
        Check("COLLECT ALL: entry + combo + song + backdrop in one write (" + all + ")", Codex.PendingTotal == 4 && Codex.CollectAllRewards() == all && Dust() == 100f + all && Codex.PendingTotal == 0);
        Check("COLLECT ALL again pays nothing", Codex.CollectAllRewards() == 0 && Dust() == 100f + all);
        PlayerPrefs.DeleteKey(FusionDiscoveryStore.PrefsKey);
        FusionDiscoveryStore.Reload();

        Reset("", 0f);
        return fails;
    }
}
