using System;
using System.Collections.Generic;
using UnityEngine;

// The player's achievement state, in PlayerPrefs (offline, batched saves via
// PrefsSaver) under per-id keys so the cloud merge (ProgressMerge: max) works
// on each independently:
//
//   ach_u_<id>     1 once unlocked (never cleared, so retuning a target cannot take one away)
//   ach_c_<id>     1 once the 25 star dust was claimed
//   ach_n_<name>   lifetime counters (AchievementCatalog lists them)
//   ach_e_<codex>  1 per elite ship the pilot has downed (feeds elite_w<world>)
//   ach_b_<world>  1 per world whose boss was destroyed (feeds bosses)
//   ach_s_<id>     store-sync mark: percent last confirmed by Play Games / Game Center
//                  (device-local, not cloud-synced; AchievementSync)
//   ach_schema     migration stamp (AchievementMigration)
//
// Unlocking never pays. The player claims in the Codex (Claim / ClaimAll),
// which is menu-only, so StarDustLedger's run bookkeeping is never in the way.
public static class AchievementStore
{
    public const string SchemaKey = "ach_schema";
    public const string CounterPrefix = "ach_n_", EliteFlagPrefix = "ach_e_", BossFlagPrefix = "ach_b_";

    // Raised once per achievement, the moment it is unlocked.
    public static event Action<AchievementDef> Unlocked;
    // Raised after a claim (single or all) pays out.
    public static event Action Claimed;

    // ---- keys ----

    static readonly Dictionary<string, string> counterKeys = new Dictionary<string, string>();

    public static string CounterKey(string counter)
    {
        string key;
        if (!counterKeys.TryGetValue(counter, out key)) counterKeys[counter] = key = CounterPrefix + counter;
        return key;
    }

    public static string BossFlagKey(int world) { return BossFlagPrefix + world; }
    public static string EliteFlagKey(string codexId) { return EliteFlagPrefix + codexId; }

    // ---- state ----

    public static bool IsUnlocked(AchievementDef def) { return PlayerPrefs.GetInt(def.unlockedKey, 0) == 1; }
    public static bool IsClaimed(AchievementDef def) { return PlayerPrefs.GetInt(def.claimedKey, 0) == 1; }
    public static bool IsClaimable(AchievementDef def)
    {
        return AchievementCatalog.IsActive(def) && IsUnlocked(def) && !IsClaimed(def);
    }

    public static int Counter(string counter) { return PlayerPrefs.GetInt(CounterKey(counter), 0); }

    // The count shown on a progress bar, capped at the target.
    public static int Progress(AchievementDef def)
    {
        if (def.counter == null) return IsUnlocked(def) ? 1 : 0;
        int target = def.Target;
        int v = Counter(def.counter);
        return IsUnlocked(def) ? Math.Max(v, target) : Math.Min(v, target);
    }

    public static double Percent(AchievementDef def)
    {
        if (IsUnlocked(def)) return 100.0;
        int target = def.Target;
        if (def.counter == null || target <= 0) return 0.0;
        return Math.Min(100.0, Math.Max(0, Counter(def.counter)) * 100.0 / target);
    }

    public static int UnlockedCount
    {
        get
        {
            int n = 0;
            foreach (var d in AchievementCatalog.All) if (AchievementCatalog.IsActive(d) && IsUnlocked(d)) n++;
            return n;
        }
    }

    public static int ClaimableCount
    {
        get
        {
            int n = 0;
            foreach (var d in AchievementCatalog.All) if (IsClaimable(d)) n++;
            return n;
        }
    }

    public static int ClaimedCount
    {
        get
        {
            int n = 0;
            foreach (var d in AchievementCatalog.All) if (AchievementCatalog.IsActive(d) && IsClaimed(d)) n++;
            return n;
        }
    }

    // ---- writes (callers go through Achievements, which applies the guard) ----

    // Sets the unlocked flag. False when it already was (or the achievement is dormant).
    public static bool Unlock(AchievementDef def)
    {
        if (def == null || !AchievementCatalog.IsActive(def) || IsUnlocked(def)) return false;
        PlayerPrefs.SetInt(def.unlockedKey, 1);
        PrefsSaver.MarkDirty();
        AchievementSync.OnUnlocked(def);   // the store is told now, or at the next sign-in
        var handler = Unlocked;
        if (handler != null)
        {
            try { handler(def); }
            catch (Exception e) { Debug.LogException(e); }   // a toast problem never costs an unlock
        }
        return true;
    }

    // Writes a counter and unlocks whatever it reaches. Returns the new value.
    public static int SetCounter(string counter, int value)
    {
        value = Math.Max(0, value);
        PlayerPrefs.SetInt(CounterKey(counter), value);
        PrefsSaver.MarkDirty();
        CheckCounter(counter, value);
        return value;
    }

    public static int AddCounter(string counter, int n)
    {
        if (n == 0) return Counter(counter);
        return SetCounter(counter, Counter(counter) + n);
    }

    public static int SetCounterAtLeast(string counter, int value)
    {
        int now = Counter(counter);
        return value > now ? SetCounter(counter, value) : now;
    }

    // Unlocks every achievement of the counter that has reached its target.
    public static void CheckCounter(string counter, int value)
    {
        var defs = AchievementCatalog.ForCounter(counter);
        for (int i = 0; i < defs.Length; i++)
        {
            var d = defs[i];
            if (IsUnlocked(d)) continue;
            if (value >= d.Target) Unlock(d);
            else if (d.storeSteps > 0) AchievementSync.NoteProgress(d);
        }
    }

    // ---- claiming ----

    public static float RewardOf(AchievementDef def) { return AchievementCatalog.RewardDust; }

    // Pays the 25 star dust once. Returns the dust paid (0: locked, dormant or already claimed).
    public static int Claim(AchievementDef def)
    {
        int paid = ClaimNoSave(def);
        if (paid > 0) Finish();
        return paid;
    }

    // Claims everything claimable with ONE save at the end. Returns the dust paid.
    public static int ClaimAll()
    {
        int total = 0;
        foreach (var d in AchievementCatalog.All) total += ClaimNoSave(d);
        if (total > 0) Finish();
        return total;
    }

    static int ClaimNoSave(AchievementDef def)
    {
        if (def == null || !IsClaimable(def)) return 0;
        // Both writes land before the one save, so a kill in between can
        // neither give dust without the flag nor the flag without the dust.
        PlayerPrefs.SetInt(def.claimedKey, 1);
        StarDustLedger.Grant(AchievementCatalog.RewardDust);
        return AchievementCatalog.RewardDust;
    }

    static void Finish()
    {
        PrefsSaver.SaveNow();
        var handler = Claimed;
        if (handler != null) handler();
    }

    // ---- sets feeding derived counters ----

    // Marks a world's boss as destroyed. True the first time.
    public static bool MarkBoss(int world)
    {
        string key = BossFlagKey(world);
        if (PlayerPrefs.GetInt(key, 0) == 1) return false;
        PlayerPrefs.SetInt(key, 1);
        PrefsSaver.MarkDirty();
        return true;
    }

    public static bool MarkElite(string codexId)
    {
        string key = EliteFlagKey(codexId);
        if (PlayerPrefs.GetInt(key, 0) == 1) return false;
        PlayerPrefs.SetInt(key, 1);
        PrefsSaver.MarkDirty();
        return true;
    }

    public static int BossesDestroyed()
    {
        int n = 0;
        for (int w = 0; w < WorldManager.Worlds.Length; w++) if (PlayerPrefs.GetInt(BossFlagKey(w), 0) == 1) n++;
        return n;
    }

    public static int ElitesDestroyedIn(int world)
    {
        int n = 0;
        var all = EliteCatalog.All;
        for (int i = 0; i < all.Length; i++)
            if (all[i].WorldIndex == world && PlayerPrefs.GetInt(EliteFlagKey(all[i].codexId), 0) == 1) n++;
        return n;
    }

    // After a cloud restore the counters and the flag sets may disagree
    // (counters merge by max, flags by union): the sets win when larger.
    public static void RecountDerived()
    {
        SetCounterAtLeast(AchievementCatalog.CBosses, BossesDestroyed());
        for (int w = 0; w < AchievementCatalog.EliteWorlds; w++)
            SetCounterAtLeast(AchievementCatalog.EliteCounter(w), ElitesDestroyedIn(w));
    }

    // ---- cloud sync & housekeeping ----

    // Every PlayerPrefs int that belongs to the account's progress (ProgressSnapshot).
    public static List<string> SyncKeys()
    {
        var keys = new List<string>(AchievementCatalog.All.Length * 2 + 40);
        foreach (var d in AchievementCatalog.All) { keys.Add(d.unlockedKey); keys.Add(d.claimedKey); }
        var counters = new HashSet<string>();
        foreach (var d in AchievementCatalog.All)
            if (d.counter != null && counters.Add(d.counter)) keys.Add(CounterKey(d.counter));
        for (int w = 0; w < WorldManager.Worlds.Length; w++) keys.Add(BossFlagKey(w));
        foreach (var e in EliteCatalog.All) keys.Add(EliteFlagKey(e.codexId));
        return keys;
    }

    // Everything the achievements write, for the test sandbox and a full reset.
    public static List<string> AllKeys()
    {
        var keys = SyncKeys();
        foreach (var d in AchievementCatalog.All) keys.Add(d.syncedKey);
        keys.Add(SchemaKey);
        return keys;
    }

    public static void ResetAll()
    {
        foreach (string key in AllKeys()) PlayerPrefs.DeleteKey(key);
    }
}
