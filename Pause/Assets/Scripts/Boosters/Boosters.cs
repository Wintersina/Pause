using UnityEngine;

// BOOSTERS: three repeatable star-dust purchases (docs/economy.md, "Boosters").
//
//   DUST MULTIPLIER  2x star dust for 10 minutes of PLAY. The clock runs only on unfrozen frames of a real run (score.payDust),
//                    never in a menu, a frozen world, a scripted entrance or the tutorial. Buying more while it is active
//                    adds 10 minutes. Doubles pickups and rewards (score.AwardStarDust / flight trickle / kills / elites);
//                    never a claim (achievements, Codex), a purchase (packs) or the end-of-run score bonus. Score is untouched.
//   HAZARD CLEAR     a charge held in inventory, fires BY ITSELF when a hit would cost a heart (collisionDetection -> HazardClear.AutoSave), saving the heart.
//   HOMING ATOMS     10 minutes of PLAY (the same clock rules as the multiplier) during which every atom that comes on screen locks
//                    onto the ship and flies at it (AtomHoming): dodge it or teleport and it misses. Star dust is not pulled.
//
// All are bought here with dust and nothing else: no store product, no platform code (iOS = Android).
//
// STATE is six monotonic counters in PlayerPrefs (and the cloud save), never "how many are left":
//   boughtDust      purchases of the multiplier        remaining seconds = boughtDust x 600 - usedMs / 1000
//   usedMs          milliseconds of boosted play spent
//   boughtHoming    purchases of homing atoms          remaining seconds = boughtHoming x 600 - usedHomingMs / 1000
//   usedHomingMs    milliseconds of homing play spent
//   boughtClear     charges ever bought                 charges held      = boughtClear - usedClear
//   usedClear       charges ever fired
// Counters merge by MAX across devices (ProgressMerge), which can neither duplicate a paid item (a max, never a sum) nor
// bring back a used one, and the dust for a purchase only one side knows is charged on merge (ProgressMerge.UnpaidBoosters).
//
// PURCHASE. One synchronous PlayerPrefs write of the new balance AND the counter, then SaveNow: the platform commits the
// store as a whole, so a restart can never leave "paid, nothing received" or "received, never paid".
public static class Boosters
{
    // ---- named constants (prices are derived in docs/economy.md and proved by BoostersTest) ----
    public const float DustBoosterPrice = 600f;
    public const float DustBoosterSeconds = 600f;       // 10 minutes of play per purchase
    public const float HazardClearPrice = 200f;         // per charge
    public const int MaxClearCharges = 9;               // held at once
    public const float HomingPrice = 1000f;
    public const float HomingSeconds = 600f;            // 10 minutes of play per purchase
    public const float MaxStoredSeconds = 3600f;        // remaining boosted time never stacks past an hour
    public const float MaxDustMultiplier = 3f;          // booster + rewarded ad, together
    // The share of the price a typical player may earn back in 10 minutes. The multiplier's extra dust must stay below this.
    public const float MaxPaybackShare = .70f;

    public const string DustBoughtKey = "boosterDustBought";
    public const string DustUsedMsKey = "boosterDustUsedMs";
    public const string ClearBoughtKey = "boosterClearBought";
    public const string ClearUsedKey = "boosterClearUsed";
    public const string HomingBoughtKey = "boosterHomingBought";
    public const string HomingUsedMsKey = "boosterHomingUsedMs";

    public enum Kind { DustMultiplier, HazardClear, HomingAtoms }
    public enum BuyResult { Bought, NotEnoughDust, AtLimit, RunInProgress }

    // ---- the pure rule ----
    // The booster adds +1x and the rewarded double adds +1x (additive, never 2 x 2), capped at 3x.
    public static float DustMultiplier(bool boosterActive, bool adDoubled)
    {
        float m = 1f + (boosterActive ? 1f : 0f) + (adDoubled ? 1f : 0f);
        return Mathf.Min(m, MaxDustMultiplier);
    }

    // Set by the rewarded-ad code while a double-dust ad reward is running (default off). Read by RunDustMultiplier.
    public static bool AdDoubled;

    // ---- cached counters ----
    static bool loaded;
    static int boughtDust, usedMs, boughtClear, usedClear, boughtHoming, usedHomingMs;
    static float fracMs, fracHomingMs;

    static void EnsureLoaded() { if (!loaded) Reload(); }

    // Re-reads the counters from PlayerPrefs (start-up, after a cloud apply, tests).
    public static void Reload()
    {
        loaded = true;
        boughtDust = Mathf.Max(0, PlayerPrefs.GetInt(DustBoughtKey, 0));
        usedMs = Mathf.Max(0, PlayerPrefs.GetInt(DustUsedMsKey, 0));
        boughtClear = Mathf.Max(0, PlayerPrefs.GetInt(ClearBoughtKey, 0));
        usedClear = Mathf.Max(0, PlayerPrefs.GetInt(ClearUsedKey, 0));
        boughtHoming = Mathf.Max(0, PlayerPrefs.GetInt(HomingBoughtKey, 0));
        usedHomingMs = Mathf.Max(0, PlayerPrefs.GetInt(HomingUsedMsKey, 0));
        fracMs = fracHomingMs = 0f;
    }

    public static int BoughtDust { get { EnsureLoaded(); return boughtDust; } }
    public static int UsedMs { get { EnsureLoaded(); return usedMs; } }
    public static int BoughtClear { get { EnsureLoaded(); return boughtClear; } }
    public static int UsedClear { get { EnsureLoaded(); return usedClear; } }
    public static int BoughtHoming { get { EnsureLoaded(); return boughtHoming; } }
    public static int UsedHomingMs { get { EnsureLoaded(); return usedHomingMs; } }

    // ---- state ----
    static long RemainingMs { get { EnsureLoaded(); return System.Math.Max(0L, (long)boughtDust * (long)(DustBoosterSeconds * 1000f) - usedMs); } }
    public static float RemainingSeconds { get { return RemainingMs / 1000f; } }
    public static bool DustActive { get { return RemainingMs > 0; } }
    public static int ClearCharges { get { EnsureLoaded(); return Mathf.Max(0, boughtClear - usedClear); } }
    static long HomingRemainingMs { get { EnsureLoaded(); return System.Math.Max(0L, (long)boughtHoming * (long)(HomingSeconds * 1000f) - usedHomingMs); } }
    public static float HomingRemainingSeconds { get { return HomingRemainingMs / 1000f; } }
    public static bool HomingActive { get { return HomingRemainingMs > 0; } }
    // Atoms home in on the ship right now: time left and a real run (never the tutorial or a practice run). AtomHoming reads it.
    public static bool HomingRunning { get { return score.paysRealDust && !startMenu.youAreInTutorial && HomingActive; } }

    // True while the dust multiplier has time left: the one switch the ad code reads. Hazard charges held in inventory
    // deliberately do NOT count (unused charges would switch ads off for good), and neither does homing-atom time
    // (it is a play aid, not the paid ad-free 10 minutes of the multiplier).
    public static bool AnyActive { get { return DustActive; } }

    // A charge is held. Developer mode grants nothing here: it fires, and spends, only on real held charges (buy them in the Shop).
    public static bool ClearAvailable { get { return ClearCharges > 0; } }

    // The multiplier applied to dust earned in the live run right now (1 in the tutorial and practice runs).
    public static float RunDustMultiplier
    {
        get { return score.paysRealDust ? DustMultiplier(DustActive, AdDoubled) : 1f; }
    }

    // Dust the booster alone added to the live run (so the ad code can add its own x on the unboosted base:
    // base = earned - RunBoostExtra, final = base x DustMultiplier(...) stays at 3x). Reset by ResetRun.
    public static float RunBoostExtra { get; private set; }
    public static void ResetRun() { RunBoostExtra = 0f; RunDustUsed = false; RunHomingUsed = false; }

    // The dust multiplier was ACTIVE on at least one unfrozen frame of real run flight this run (set by Tick, so it stays true after the
    // time runs out mid-run). The leaderboards route a run with this set to the Power-Ups board. Merely owning booster time does not set it.
    public static bool RunDustUsed { get; private set; }
    // The same latch for homing atoms (the Power-Ups board's HOMING bit).
    public static bool RunHomingUsed { get; private set; }

    // The run's earned dust as the leaderboards see it: WITHOUT what the booster added, so the "run star dust" board ranks play, not purchases.
    public static float RankedDust(float earned) { return Mathf.Max(0f, earned - RunBoostExtra); }

    // Scales one award; remembers the extra for RunBoostExtra. `amount` is the base value.
    public static float ApplyToAward(float amount)
    {
        float m = RunDustMultiplier;
        if (m <= 1f || amount <= 0f) return amount;
        float scaled = amount * m;
        if (DustActive) RunBoostExtra += amount * (DustMultiplier(true, false) - 1f);
        return scaled;
    }

    // Test seam: when set, Tick uses this instead of the dt it is given (edit-mode tests have no frame time).
    public static float? TestDelta;

    // Spends `dt` seconds of boosted play on every timed booster that has time left. The caller (score.payDust) calls it on
    // unfrozen real-run frames only.
    public static void Tick(float dt)
    {
        if (TestDelta.HasValue) dt = TestDelta.Value;
        if (dt <= 0f) return;
        if (DustActive)
        {
            RunDustUsed = true;
            if (Spend(ref usedMs, ref fracMs, RemainingMs, dt)) WriteUsed();
        }
        if (HomingActive)
        {
            RunHomingUsed = true;
            if (Spend(ref usedHomingMs, ref fracHomingMs, HomingRemainingMs, dt)) WriteHomingUsed();
        }
    }

    // Adds `dt` to one clock's used milliseconds, never past the `left` it had. True when the prefs should be written:
    // the second changed or the time ran out (not every frame).
    static bool Spend(ref int used, ref float frac, long left, float dt)
    {
        frac += dt * 1000f;
        int whole = (int)frac;
        frac -= whole;
        if (whole <= 0) return false;
        int before = used;
        used += (int)System.Math.Min(whole, left);
        return used / 1000 != before / 1000 || whole >= left;
    }

    // Writes the in-memory clock out (run end, app pause); the caller's save flushes it.
    public static void Flush()
    {
        if (!loaded) return;
        WriteUsed();
        WriteHomingUsed();
    }

    static void WriteUsed()
    {
        PlayerPrefs.SetInt(DustUsedMsKey, usedMs);
        PrefsSaver.MarkDirty();
    }

    static void WriteHomingUsed()
    {
        PlayerPrefs.SetInt(HomingUsedMsKey, usedHomingMs);
        PrefsSaver.MarkDirty();
    }

    // ---- buying ----
    static bool InRun { get { return StarDustLedger.IsActive; } }

    public static BuyResult BuyDustBooster()
    {
        EnsureLoaded();
        if (InRun) return BuyResult.RunInProgress;
        if (RemainingSeconds + DustBoosterSeconds > MaxStoredSeconds + .01f) return BuyResult.AtLimit;
        float have = StarDustLedger.Saved;
        if (have < DustBoosterPrice) return BuyResult.NotEnoughDust;
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, have - DustBoosterPrice);
        boughtDust++;
        PlayerPrefs.SetInt(DustBoughtKey, boughtDust);
        PrefsSaver.SaveNow();
        return BuyResult.Bought;
    }

    public static BuyResult BuyHazardClear()
    {
        EnsureLoaded();
        if (InRun) return BuyResult.RunInProgress;
        if (ClearCharges >= MaxClearCharges) return BuyResult.AtLimit;
        float have = StarDustLedger.Saved;
        if (have < HazardClearPrice) return BuyResult.NotEnoughDust;
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, have - HazardClearPrice);
        boughtClear++;
        PlayerPrefs.SetInt(ClearBoughtKey, boughtClear);
        PrefsSaver.SaveNow();
        return BuyResult.Bought;
    }

    public static BuyResult BuyHoming()
    {
        EnsureLoaded();
        if (InRun) return BuyResult.RunInProgress;
        if (HomingRemainingSeconds + HomingSeconds > MaxStoredSeconds + .01f) return BuyResult.AtLimit;
        float have = StarDustLedger.Saved;
        if (have < HomingPrice) return BuyResult.NotEnoughDust;
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, have - HomingPrice);
        boughtHoming++;
        PlayerPrefs.SetInt(HomingBoughtKey, boughtHoming);
        PrefsSaver.SaveNow();
        return BuyResult.Bought;
    }

    // Spends one charge (the clear fired). False when there is none (developer mode included).
    public static bool ConsumeClear()
    {
        EnsureLoaded();
        if (ClearCharges <= 0) return false;
        usedClear++;
        PlayerPrefs.SetInt(ClearUsedKey, usedClear);
        PrefsSaver.MarkDirty();
        return true;
    }

    // ---- what the Shop shows ----
    public static string FormatTime(float seconds)
    {
        int s = Mathf.CeilToInt(Mathf.Max(0f, seconds));
        return (s / 60) + ":" + (s % 60).ToString("00");
    }

    public static string Name(Kind kind)
    {
        switch (kind)
        {
            case Kind.DustMultiplier: return "DUST x2";
            case Kind.HomingAtoms: return "HOMING ATOMS";
            default: return "HAZARD CLEAR";
        }
    }

    public static string Description(Kind kind)
    {
        switch (kind)
        {
            case Kind.DustMultiplier:
                return "Double star dust for 10 minutes of play. Pickups and rewards only. Buy again to add time. No ads while it runs.";
            case Kind.HomingAtoms:
                return "For 10 minutes of play every atom flies at your ship. Dodge or teleport and it misses. Buy again to add time.";
            default:
                return "Fires by itself when a hit would cost a heart: saves the heart and wipes rocks, mines and shots on screen. No score. Bosses stay.";
        }
    }

    public static float Price(Kind kind)
    {
        switch (kind)
        {
            case Kind.DustMultiplier: return DustBoosterPrice;
            case Kind.HomingAtoms: return HomingPrice;
            default: return HazardClearPrice;
        }
    }

    // "8:20 left" / "x3" / "" -- what is held.
    public static string Status(Kind kind)
    {
        switch (kind)
        {
            case Kind.DustMultiplier: return DustActive ? FormatTime(RemainingSeconds) + " left" : "";
            case Kind.HomingAtoms: return HomingActive ? FormatTime(HomingRemainingSeconds) + " left" : "";
            default: return ClearCharges > 0 ? "x" + ClearCharges : "";
        }
    }

    public static BuyResult Buy(Kind kind)
    {
        switch (kind)
        {
            case Kind.DustMultiplier: return BuyDustBooster();
            case Kind.HomingAtoms: return BuyHoming();
            default: return BuyHazardClear();
        }
    }

    // Buying one more would pass the hold limit (stored time over an hour, or 9 charges).
    public static bool AtLimit(Kind kind)
    {
        switch (kind)
        {
            case Kind.DustMultiplier: return RemainingSeconds + DustBoosterSeconds > MaxStoredSeconds + .01f;
            case Kind.HomingAtoms: return HomingRemainingSeconds + HomingSeconds > MaxStoredSeconds + .01f;
            default: return ClearCharges >= MaxClearCharges;
        }
    }

    // Whether a Buy tap would go through right now (a disabled row greys out).
    public static bool CanBuy(Kind kind)
    {
        EnsureLoaded();
        if (InRun || StarDustLedger.Saved < Price(kind)) return false;
        return !AtLimit(kind);
    }
}
