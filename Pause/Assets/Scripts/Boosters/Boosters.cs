using UnityEngine;

// BOOSTERS: three repeatable star-dust purchases (docs/economy.md, "Boosters").
//
//   DUST MULTIPLIER  2x star dust for 5 minutes of PLAY. The clock runs only on unfrozen frames of a real run (score.payDust),
//                    never in a menu, a frozen world, a scripted entrance or the tutorial. Buying more adds 5 minutes onto
//                    what is left. Doubles pickups and rewards (score.AwardStarDust / flight trickle / kills / elites);
//                    never a claim (achievements, Codex), a purchase (packs) or the end-of-run score bonus. Score is untouched.
//   HAZARD CLEAR     a charge held in inventory, fires BY ITSELF when a hit would cost a heart (collisionDetection -> HazardClear.AutoSave), saving the heart.
//   HOMING ATOMS     5 minutes of PLAY (the same clock rules as the multiplier) during which every atom that comes on screen locks
//                    onto the ship and flies at it (AtomHoming): dodge it or teleport and it misses. Star dust is not pulled.
//
// All are bought here with dust and nothing else: no store product, no platform code (iOS = Android).
//
// THE ON/OFF SWITCH (Shop row). A booster that is held can be switched OFF: it then does nothing (no x2, no homing, no auto-fired
// clear, ads come back) and, because its clock is PLAY time that only Tick spends, its time left simply stays where it was. ON
// again resumes it. Buying one switches it on. The switch is a device-local PlayerPrefs flag (DustOffKey / HomingOffKey /
// ClearOffKey, 1 = off), not synced: a restore never flips it.
//
// STATE is eight monotonic counters in PlayerPrefs (and the cloud save), never "how many are left":
//   boughtDust      purchases of the multiplier (5 min)          remaining seconds = boughtDustLegacy x 600 + boughtDust x 300
//   boughtDustLegacy purchases made before Nov 2026 (10 min each)                     - usedMs / 1000
//   usedMs          milliseconds of boosted play spent
//   boughtHoming    purchases of homing atoms (5 min)            remaining seconds = boughtHomingLegacy x 600 + boughtHoming x 300
//   boughtHomingLegacy purchases made before Nov 2026 (10 min)                         - usedHomingMs / 1000
//   usedHomingMs    milliseconds of homing play spent
// The legacy counters keep the old keys ("boosterDustBought", "boosterHomingBought") and the old cloud fields, so time bought at
// 10 minutes stays 10 minutes on every device and an older build still reads them; nothing new is ever added to them.
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
    public const float DustBoosterSeconds = 300f;       // 5 minutes of play per purchase
    public const float HazardClearPrice = 200f;         // per charge
    public const int MaxClearCharges = 9;               // held at once
    public const float HomingPrice = 1000f;
    public const float HomingSeconds = 300f;            // 5 minutes of play per purchase
    public const float LegacyBoosterSeconds = 600f;     // what a purchase gave before Nov 2026 (the legacy counters)
    public const float MaxStoredSeconds = 3600f;        // remaining boosted time never stacks past an hour
    public const float MaxDustMultiplier = 3f;          // booster + rewarded ad, together
    // The share of the price a typical player may earn back in 10 minutes. The multiplier's extra dust must stay below this.
    public const float MaxPaybackShare = .70f;

    public const string DustBoughtKey = "boosterDustBought5m";
    public const string DustBoughtLegacyKey = "boosterDustBought";      // 10-minute purchases, frozen
    public const string DustUsedMsKey = "boosterDustUsedMs";
    public const string ClearBoughtKey = "boosterClearBought";
    public const string ClearUsedKey = "boosterClearUsed";
    public const string HomingBoughtKey = "boosterHomingBought5m";
    public const string HomingBoughtLegacyKey = "boosterHomingBought";  // 10-minute purchases, frozen
    public const string HomingUsedMsKey = "boosterHomingUsedMs";
    // the player's switch, 1 = off (device-local, not synced)
    public const string DustOffKey = "boosterDustOff";
    public const string HomingOffKey = "boosterHomingOff";
    public const string ClearOffKey = "boosterClearOff";

    // Every pref the boosters own (tests and the sandbox clear / restore these).
    public static readonly string[] AllKeys =
    {
        DustBoughtKey, DustBoughtLegacyKey, DustUsedMsKey, ClearBoughtKey, ClearUsedKey,
        HomingBoughtKey, HomingBoughtLegacyKey, HomingUsedMsKey, DustOffKey, HomingOffKey, ClearOffKey,
    };

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
    static int boughtDust, boughtDustLegacy, usedMs, boughtClear, usedClear, boughtHoming, boughtHomingLegacy, usedHomingMs;
    static bool dustOff, homingOff, clearOff;
    static float fracMs, fracHomingMs;

    static void EnsureLoaded() { if (!loaded) Reload(); }

    // Re-reads the counters from PlayerPrefs (start-up, after a cloud apply, tests).
    public static void Reload()
    {
        loaded = true;
        boughtDust = Mathf.Max(0, PlayerPrefs.GetInt(DustBoughtKey, 0));
        boughtDustLegacy = Mathf.Max(0, PlayerPrefs.GetInt(DustBoughtLegacyKey, 0));
        usedMs = Mathf.Max(0, PlayerPrefs.GetInt(DustUsedMsKey, 0));
        boughtClear = Mathf.Max(0, PlayerPrefs.GetInt(ClearBoughtKey, 0));
        usedClear = Mathf.Max(0, PlayerPrefs.GetInt(ClearUsedKey, 0));
        boughtHoming = Mathf.Max(0, PlayerPrefs.GetInt(HomingBoughtKey, 0));
        boughtHomingLegacy = Mathf.Max(0, PlayerPrefs.GetInt(HomingBoughtLegacyKey, 0));
        usedHomingMs = Mathf.Max(0, PlayerPrefs.GetInt(HomingUsedMsKey, 0));
        dustOff = PlayerPrefs.GetInt(DustOffKey, 0) == 1;
        homingOff = PlayerPrefs.GetInt(HomingOffKey, 0) == 1;
        clearOff = PlayerPrefs.GetInt(ClearOffKey, 0) == 1;
        fracMs = fracHomingMs = 0f;
        RaiseChanged();
    }

    // Purchases of each kind, old (10 min) and new (5 min) together.
    public static int BoughtDust { get { EnsureLoaded(); return boughtDust + boughtDustLegacy; } }
    public static int UsedMs { get { EnsureLoaded(); return usedMs; } }
    public static int BoughtClear { get { EnsureLoaded(); return boughtClear; } }
    public static int UsedClear { get { EnsureLoaded(); return usedClear; } }
    public static int BoughtHoming { get { EnsureLoaded(); return boughtHoming + boughtHomingLegacy; } }
    public static int UsedHomingMs { get { EnsureLoaded(); return usedHomingMs; } }

    // ---- state ----
    static long GrantedMs(int bought, int legacy, float seconds)
    {
        return (long)bought * (long)(seconds * 1000f) + (long)legacy * (long)(LegacyBoosterSeconds * 1000f);
    }
    static long RemainingMs { get { EnsureLoaded(); return System.Math.Max(0L, GrantedMs(boughtDust, boughtDustLegacy, DustBoosterSeconds) - usedMs); } }
    // Time held, whether switched on or off (off: frozen).
    public static float RemainingSeconds { get { return RemainingMs / 1000f; } }
    // The multiplier works: time left AND switched on.
    public static bool DustActive { get { return RemainingMs > 0 && !dustOff; } }
    public static int ClearCharges { get { EnsureLoaded(); return Mathf.Max(0, boughtClear - usedClear); } }
    static long HomingRemainingMs { get { EnsureLoaded(); return System.Math.Max(0L, GrantedMs(boughtHoming, boughtHomingLegacy, HomingSeconds) - usedHomingMs); } }
    public static float HomingRemainingSeconds { get { return HomingRemainingMs / 1000f; } }
    // Homing works: time left AND switched on.
    public static bool HomingActive { get { return HomingRemainingMs > 0 && !homingOff; } }
    // Atoms home in on the ship right now: time left and a real run (never the tutorial or a practice run). AtomHoming reads it.
    public static bool HomingRunning { get { return score.paysRealDust && !startMenu.youAreInTutorial && HomingActive; } }

    // True while the dust multiplier has time left: the one switch the ad code reads. Hazard charges held in inventory
    // deliberately do NOT count (unused charges would switch ads off for good), and neither does homing-atom time
    // (it is a play aid, not the paid ad-free 10 minutes of the multiplier).
    public static bool AnyActive { get { return DustActive; } }

    // A charge is held and the switch is on. Developer mode grants nothing here: it fires, and spends, only on real held charges.
    public static bool ClearAvailable { get { return ClearCharges > 0 && !clearOff; } }

    // ================================================================== READ-ONLY API (Shop rows, the in-game HUD)
    // Stable: other screens read boosters only through these (plus Changed). Nothing here writes.

    // Fired after anything a booster readout shows may have changed: a purchase, the switch, a spent charge, a timed booster
    // running out, a cloud apply / Reload. NOT every second of a ticking clock: poll SecondsLeft for the countdown.
    public static event System.Action Changed;
    static void RaiseChanged()
    {
        var h = Changed;
        if (h == null) return;
        try { h(); } catch (System.Exception e) { Debug.LogException(e); }
    }

    // Dust x2 and Homing Atoms count play time; Hazard Clear counts charges.
    public static bool IsTimed(Kind kind) { return kind != Kind.HazardClear; }

    // Something is held: time left (timed) or a charge (Hazard Clear), switched on or off.
    public static bool Owns(Kind kind) { return IsTimed(kind) ? SecondsLeft(kind) > 0f : ChargesLeft(kind) > 0; }

    // The player's switch (default on). A booster that is off keeps what is left and does nothing.
    public static bool IsOn(Kind kind)
    {
        EnsureLoaded();
        switch (kind)
        {
            case Kind.DustMultiplier: return !dustOff;
            case Kind.HomingAtoms: return !homingOff;
            default: return !clearOff;
        }
    }

    // Held AND switched on: it works in the next real run (Dust x2 doubles, atoms home, a charge auto-fires).
    public static bool IsActive(Kind kind) { return Owns(kind) && IsOn(kind); }

    // Seconds of play left on a timed booster (frozen while off); 0 for Hazard Clear.
    public static float SecondsLeft(Kind kind)
    {
        switch (kind)
        {
            case Kind.DustMultiplier: return RemainingSeconds;
            case Kind.HomingAtoms: return HomingRemainingSeconds;
            default: return 0f;
        }
    }

    // Hazard Clear charges held; 0 for the timed boosters.
    public static int ChargesLeft(Kind kind) { return kind == Kind.HazardClear ? ClearCharges : 0; }

    // ================================================================== the switch (Shop only)

    // Switches a booster on or off; saved at once. Off is refused (false) when nothing is held; on always goes through.
    public static bool SetOn(Kind kind, bool on)
    {
        EnsureLoaded();
        if (!on && !Owns(kind)) return false;
        if (IsOn(kind) == on) return true;
        switch (kind)
        {
            case Kind.DustMultiplier: dustOff = !on; PlayerPrefs.SetInt(DustOffKey, on ? 0 : 1); break;
            case Kind.HomingAtoms: homingOff = !on; PlayerPrefs.SetInt(HomingOffKey, on ? 0 : 1); break;
            default: clearOff = !on; PlayerPrefs.SetInt(ClearOffKey, on ? 0 : 1); break;
        }
        Flush();   // the clock as it stands, so the frozen time is on disk with the switch
        PrefsSaver.SaveNow();
        RaiseChanged();
        return true;
    }

    public static bool Toggle(Kind kind) { return SetOn(kind, !IsOn(kind)); }

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
        bool ranOut = false;
        if (DustActive)
        {
            RunDustUsed = true;
            if (Spend(ref usedMs, ref fracMs, RemainingMs, dt)) WriteUsed();
            ranOut |= !DustActive;
        }
        if (HomingActive)
        {
            RunHomingUsed = true;
            if (Spend(ref usedHomingMs, ref fracHomingMs, HomingRemainingMs, dt)) WriteHomingUsed();
            ranOut |= !HomingActive;
        }
        if (ranOut) RaiseChanged();
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
        dustOff = false; PlayerPrefs.SetInt(DustOffKey, 0);   // buying switches it on
        PrefsSaver.SaveNow();
        RaiseChanged();
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
        clearOff = false; PlayerPrefs.SetInt(ClearOffKey, 0);
        PrefsSaver.SaveNow();
        RaiseChanged();
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
        homingOff = false; PlayerPrefs.SetInt(HomingOffKey, 0);
        PrefsSaver.SaveNow();
        RaiseChanged();
        return BuyResult.Bought;
    }

    // Spends one charge (the clear fired). False when there is none (developer mode included) or it is switched off.
    public static bool ConsumeClear()
    {
        EnsureLoaded();
        if (!ClearAvailable) return false;
        usedClear++;
        PlayerPrefs.SetInt(ClearUsedKey, usedClear);
        PrefsSaver.MarkDirty();
        RaiseChanged();
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
                return "Double star dust for 5 minutes of play. Pickups and rewards only. Buy again to add 5 minutes. No ads while it runs.";
            case Kind.HomingAtoms:
                return "For 5 minutes of play every atom flies at your ship. Dodge or teleport and it misses. Buy again to add 5 minutes.";
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

    // "4:32 left" / "x3" / "" -- what is held (on or off; IsOn says which).
    public static string Status(Kind kind)
    {
        if (!Owns(kind)) return "";
        return IsTimed(kind) ? FormatTime(SecondsLeft(kind)) + " left" : "x" + ChargesLeft(kind);
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
