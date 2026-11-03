using UnityEngine;

// BOOSTERS: two repeatable star-dust purchases (docs/economy.md, "Boosters").
//
//   DUST MULTIPLIER  2x star dust for 10 minutes of PLAY. The clock runs only on unfrozen frames of a real run (score.payDust),
//                    never in a menu, a frozen world, a scripted entrance or the tutorial. Buying more while it is active
//                    adds 10 minutes. Doubles pickups and rewards (score.AwardStarDust / flight trickle / kills / elites);
//                    never a claim (achievements, Codex), a purchase (packs) or the end-of-run score bonus. Score is untouched.
//   HAZARD CLEAR     a charge held in inventory, fires BY ITSELF when a hit would cost a heart (collisionDetection -> HazardClear.AutoSave), saving the heart.
//
// Both are bought here with dust and nothing else: no store product, no platform code (iOS = Android).
//
// STATE is four monotonic counters in PlayerPrefs (and the cloud save), never "how many are left":
//   boughtDust      purchases of the multiplier        remaining seconds = boughtDust x 600 - usedMs / 1000
//   usedMs          milliseconds of boosted play spent
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
    public const float MaxStoredSeconds = 3600f;        // remaining boosted time never stacks past an hour
    public const float MaxDustMultiplier = 3f;          // booster + rewarded ad, together
    // The share of the price a typical player may earn back in 10 minutes. The multiplier's extra dust must stay below this.
    public const float MaxPaybackShare = .70f;

    public const string DustBoughtKey = "boosterDustBought";
    public const string DustUsedMsKey = "boosterDustUsedMs";
    public const string ClearBoughtKey = "boosterClearBought";
    public const string ClearUsedKey = "boosterClearUsed";

    public enum Kind { DustMultiplier, HazardClear }
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
    static int boughtDust, usedMs, boughtClear, usedClear;
    static float fracMs;

    static void EnsureLoaded() { if (!loaded) Reload(); }

    // Re-reads the counters from PlayerPrefs (start-up, after a cloud apply, tests).
    public static void Reload()
    {
        loaded = true;
        boughtDust = Mathf.Max(0, PlayerPrefs.GetInt(DustBoughtKey, 0));
        usedMs = Mathf.Max(0, PlayerPrefs.GetInt(DustUsedMsKey, 0));
        boughtClear = Mathf.Max(0, PlayerPrefs.GetInt(ClearBoughtKey, 0));
        usedClear = Mathf.Max(0, PlayerPrefs.GetInt(ClearUsedKey, 0));
        fracMs = 0f;
    }

    public static int BoughtDust { get { EnsureLoaded(); return boughtDust; } }
    public static int UsedMs { get { EnsureLoaded(); return usedMs; } }
    public static int BoughtClear { get { EnsureLoaded(); return boughtClear; } }
    public static int UsedClear { get { EnsureLoaded(); return usedClear; } }

    // ---- state ----
    static long RemainingMs { get { EnsureLoaded(); return System.Math.Max(0L, (long)boughtDust * (long)(DustBoosterSeconds * 1000f) - usedMs); } }
    public static float RemainingSeconds { get { return RemainingMs / 1000f; } }
    public static bool DustActive { get { return RemainingMs > 0; } }
    public static int ClearCharges { get { EnsureLoaded(); return Mathf.Max(0, boughtClear - usedClear); } }

    // True while the dust multiplier has time left: the one switch the ad code reads. Hazard charges held in inventory
    // deliberately do NOT count (unused charges would switch ads off for good).
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
    public static void ResetRun() { RunBoostExtra = 0f; }

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

    // Spends `dt` seconds of boosted play. The caller (score.payDust) calls it on unfrozen real-run frames only.
    public static void Tick(float dt)
    {
        if (TestDelta.HasValue) dt = TestDelta.Value;
        if (dt <= 0f || !DustActive) return;
        long left = RemainingMs;
        fracMs += dt * 1000f;
        int whole = (int)fracMs;
        fracMs -= whole;
        if (whole <= 0) return;
        int before = usedMs;
        usedMs += (int)System.Math.Min(whole, left);
        // The prefs are written when the second changes or the time runs out, not every frame.
        if (usedMs / 1000 != before / 1000 || RemainingMs == 0) WriteUsed();
    }

    // Writes the in-memory clock out (run end, app pause); the caller's save flushes it.
    public static void Flush()
    {
        if (!loaded) return;
        WriteUsed();
    }

    static void WriteUsed()
    {
        PlayerPrefs.SetInt(DustUsedMsKey, usedMs);
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

    public static string Name(Kind kind) { return kind == Kind.DustMultiplier ? "DUST x2" : "HAZARD CLEAR"; }

    public static string Description(Kind kind)
    {
        return kind == Kind.DustMultiplier
            ? "Double star dust for 10 minutes of play. Pickups and rewards only. Buy again to add time. No ads while it runs."
            : "Fires by itself when a hit would cost a heart: saves the heart and wipes rocks, mines and shots on screen. No score. Bosses stay.";
    }

    public static float Price(Kind kind) { return kind == Kind.DustMultiplier ? DustBoosterPrice : HazardClearPrice; }

    // "ACTIVE 8:20" / "x3" / "" -- what is held.
    public static string Status(Kind kind)
    {
        if (kind == Kind.DustMultiplier) return DustActive ? FormatTime(RemainingSeconds) + " left" : "";
        return ClearCharges > 0 ? "x" + ClearCharges : "";
    }

    public static BuyResult Buy(Kind kind) { return kind == Kind.DustMultiplier ? BuyDustBooster() : BuyHazardClear(); }

    // Whether a Buy tap would go through right now (a disabled row greys out).
    public static bool CanBuy(Kind kind)
    {
        EnsureLoaded();
        if (InRun || StarDustLedger.Saved < Price(kind)) return false;
        return kind == Kind.DustMultiplier ? RemainingSeconds + DustBoosterSeconds <= MaxStoredSeconds + .01f
                                           : ClearCharges < MaxClearCharges;
    }
}
