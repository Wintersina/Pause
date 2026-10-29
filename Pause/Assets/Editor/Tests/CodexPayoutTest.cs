using System.Linq;
using UnityEngine;

// Codex discovery star dust: the payout table (every entry covered, 5..25, only the super rare pay 25),
// the first discovery paying exactly once (also across a cloud restore), and the one-time back-pay (sums the
// already discovered entries once, a second launch / sync pays nothing, a fresh save pays 0).
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
            if (e.category == CodexCategory.Log) { if (v != 0) badTable = e.id + ": the free Pilot's Log must pay 0"; }
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

        // ---- first discovery pays once ----
        Reset("", 100f);
        Check("discover: true on the first contact", Codex.Discover(first.id));
        Check("discover: paid the table value once", Dust() == 100f + CodexPayout.For(first) && Codex.IsRewardCollected(first.id) && !Codex.IsRewardPending(first));
        Check("discover again: false, nothing more", !Codex.Discover(first.id) && Dust() == 100f + CodexPayout.For(first));
        Codex.Reload();
        Check("after a reload: still paid once", !Codex.Discover(first.id) && Dust() == 100f + CodexPayout.For(first));
        Codex.Discover(boss.id);
        Check("a boss pays its own (larger) value", Dust() == 100f + CodexPayout.For(first) + CodexPayout.For(boss));

        // cloud restore handing back the discovered flags (and an older save without the paid list) never pays again
        float afterTwo = Dust();
        var snap = ProgressSnapshot.Capture(1);
        var stale = ProgressSnapshot.Capture(2);
        stale.codexPaid = new string[0];
        stale.currency = afterTwo;
        stale.Apply();
        Codex.Reload();
        Check("restore of an older save: the paid ids are kept, re-discovery pays nothing",
              !Codex.Discover(first.id) && !Codex.Discover(boss.id) && Dust() == afterTwo && Codex.PendingTotal == 0);
        snap.Apply();
        Codex.Reload();

        // developer mode: nothing recorded, nothing paid
        Reset("", 100f);
        DeveloperUnlocks.SetEnabled(true);
        Check("developer mode: no discovery, no dust", !Codex.Discover(first.id) && Dust() == 100f && Codex.PayBackOnce() == 0 && !Codex.BackPayDone);
        DeveloperUnlocks.SetEnabled(false);

        // ---- back-pay ----
        var older = Codex.Entries.Where(e => e.category != CodexCategory.Log && !e.secret && e.category != CodexCategory.Ships && e.category != CodexCategory.Worlds).Take(4).ToArray();
        Reset(string.Join(",", older.Select(e => e.id)) + ",music_world_ember,bd_frost_2", 100f);
        PlayerPrefs.SetString(Codex.PaidKey, older[0].id);   // one was already collected under the old claim system
        PlayerPrefs.SetString("boughtship2", "True");        // a ship owned counts as discovered too
        Codex.Reload();
        var ship = Codex.Entries.First(e => e.id == CodexCatalogue.ShipPrefix + ShipId.KeyOf(2));
        int expect = older.Skip(1).Sum(e => CodexPayout.For(e)) + CodexPayout.For(ship);
        Check("back-pay: nothing flagged yet", !Codex.BackPayDone);
        int paid = Codex.PayBackOnce();
        Check("back-pay: the lump is the table sum of the unpaid discovered entries (" + paid + " = " + expect + ")", paid == expect && Dust() == 100f + expect);
        Check("back-pay: flag set and every entry is marked paid", Codex.BackPayDone && Codex.PendingTotal == 0 && Codex.IsRewardCollected(ship.id));
        Codex.Reload();
        Check("second launch: pays nothing", Codex.PayBackOnce() == 0 && Dust() == 100f + expect);

        // the flag rides the cloud save: a restore / merge never clears it, so it can't pay twice
        var withFlag = ProgressSnapshot.Capture(3);
        Check("snapshot carries the flag", withFlag.codexBackPaid);
        var noFlag = ProgressSnapshot.Capture(4);
        noFlag.codexBackPaid = false;
        Check("merge: the flag is OR-ed", ProgressMerge.Merge(withFlag, noFlag).codexBackPaid && ProgressMerge.Merge(noFlag, withFlag).codexBackPaid);
        noFlag.Apply();
        Codex.Reload();
        Check("restoring a snapshot without the flag keeps it: sync / account switch pays nothing", Codex.BackPayDone && Codex.PayBackOnce() == 0);
        // another device whose flag arrives through the merge never pays either
        Reset(older[0].id, 100f);
        var flagged = ProgressSnapshot.Capture(5);
        flagged.codexBackPaid = true;
        flagged.Apply();
        Codex.Reload();
        Check("a flag restored from the cloud: no back-pay on this device", Codex.PayBackOnce() == 0 && Dust() == 100f);

        // fresh save: 0 dust, flag set, nothing to announce
        Reset("", 100f);
        Check("fresh save: back-pay is 0 (so no popup), dust unchanged, flag set", Codex.PayBackOnce() == 0 && Dust() == 100f && Codex.BackPayDone);

        Reset("", 0f);
        return fails;
    }
}
