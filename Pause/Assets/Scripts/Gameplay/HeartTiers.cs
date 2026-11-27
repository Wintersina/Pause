using UnityEngine;

// THREE-TIER ENEMY HEARTS (user design). An enemy, elite or boss carries up to three bars of hearts, outermost first:
//
//     CRYSTAL  ->  BLUE  ->  RED
//
// Damage removes the outer bar first; crystal hearts go, then the blue ones show, then the red ones (today's hearts, unchanged).
// It dies only when the RED bar is empty. Red-only is the default and behaves exactly as before.
//
// How it is built, so the existing red code stays untouched: the red bar is still the owner's own quarter pool (EnemyHearts quarters,
// EliteShip.heartQuarters, BossEncounter damage/hp). The blue and crystal bars are a HeartShell in front of it: a hit's quarter units
// go through shell.Absorb first and only the LEFTOVER reaches the red pool. OVERFLOW CARRIES: a hit bigger than the outer bar's
// remainder spends the rest on the next bar (and on red once the shell is gone). A fresh spawn never heals, a tier is chosen once,
// when the entity is built, so collecting an upgrade never changes an enemy already being fought.
//
// Which tier an entity spawns with is decided by the RUN POWER (RunPower.Score01) through ONE curve (HeartTierCurve):
// no power -> always red-only; more power -> a growing share of blue+red; high power -> crystal+blue+red become common.
// Elites and bosses use the same curve with their power boosted one step (HeartTierTuning.PowerBoost).
// EVERY number lives in HeartTierTuning.
public enum HeartLayer { Red = 0, Blue = 1, Crystal = 2 }
public enum HeartTierKind { Enemy = 0, Elite = 1, Boss = 2 }

public static class HeartTierTuning
{
    // ---- extra bars: hearts in a bar, as a share of the entity's RED hearts (never above 1: the orbit is drawn with the red count) ----
    // Ordinary enemies (red 2..4): an extra bar is as big as the red bar. Elites: half of it. Bosses (red 13+): a quarter of it.
    public const float EnemyBarShare = 1f, EliteBarShare = .5f, BossBarShare = .25f;
    public const int MinBarHearts = 1;   // an extra bar always has at least this many hearts
    // Table: red hearts -> (blue, crystal) hearts:   enemy 2 -> 2,2 | 3 -> 3,3 | 4 -> 4,4
    //                                                elite 6 -> 3,3 | 10 -> 5,5     boss 13 -> 4,4 | 20 -> 5,5

    // ---- RunPower: weights of the parts (sum 1). THE DRONE SQUAD DRIVES IT (user, 2026-11-27: "once player has too many drones the
    // score gets out of hand"): a full squad on its own is already deep in crystal territory. ----
    public const float WDrones = .7f;     // the squad: active drones + their core levels (Drones01)
    public const float WAtoms = .15f;      // ShotAtoms.PowerLevel / 60: rapid fire + every drone's shot atoms + active drones
    public const float WShip = .05f;       // the equipped ship's weapon-upgrade level (ShipWeaponUpgrades): its ultimate / upgrade state
    public const float WDust = .10f;       // star dust gathered this run (RunScore dustCount) against DustFull
    public const int DustFull = 120;
    // Drones01 = DroneCountShare x (active / PowerFullSquad) + (1 - DroneCountShare) x (core levels / (PowerFullSquad x DroneCoreFull)).
    public const int PowerFullSquad = 3;       // three drones count as a full squad for the power score
    public const int DroneCoreFull = 3;        // a core at this level counts as levelled (the cap is 4)
    public const float DroneCountShare = .6f;  // 3 drones at core 2 = 0.87, at core 3 = 1.0; 1 fresh drone = 0.24

    // ---- the curve (smoothstep ramps on the boosted power) ----
    // share of spawns that are AT LEAST blue (blue+red or crystal+blue+red), and share that are crystal. The caps are raised
    // from .85 / .6 so a big squad meets crystal on most ordinary enemies:
    public const float BlueStart = .12f, BlueFull = .75f, BlueMax = .9f;
    // CRYSTAL comes from the DRONES ONLY (user, 2026-11-27: "tied to the power of drones and how many upgrades the drones have"):
    // DronePower01 (RunPower.DronePower01: how many drones are out, how upgraded they are) through this ramp. No drones = no crystal,
    // at any run power or loop. Blue-or-better is max(RunPower curve, crystal share), so crystal is always a subset of it.
    public const float CrystalStart = .2f, CrystalFull = .85f, CrystalMax = .8f;
    public const int DroneFullSquad = 6;                  // six drones (DroneSquad's cap) fill the count half of DronePower01
    public const float DroneCountWeight = .5f;            // DronePower01 = .5 x count + .5 x upgrades
    public const float DroneCoreWeight = .4f;             // upgrades = .4 x core levels + .6 x shot-atom levels, against a full upgraded squad
    public const int CoreLevelMax = 4;                    // DroneSquad.MaxCoreLevel
    // Elites and bosses: power x boost (zero stays zero: red-only at no power for everyone).
    public const float EliteBoost = 1.35f, BossBoost = 1.6f;

    // ---- hazards scale with the same power (HazardScaling): rocks and mines spawn up to HazardRateMax more often (x1.5 at power 1),
    // and the whole spawn-rate multiplier (PowerCreep x this) is capped at HazardRateCap so a dodge line always exists. Hazards have
    // no hearts (one hit kills them), so only their rate scales. Rails, rail mines on rides and elite docks are authored: untouched. ----
    public const float HazardRateMax = .5f, HazardRateCap = 2.4f;

    // ---- shield atoms thin out as the squad grows: x1 with no drone bonus, ShieldMin (never zero) at a full squad ----
    public const float ShieldMin = .4f;

    // ---- heals thin out as power rises (HealScaling): the green atom's chance and clock, the bank's size and the healing drone's
    // restore delay all follow one rate, x1 at no power down to HealMin (never zero) at full power ----
    public const float HealMin = .35f;

    // ---- hazards hit harder at high power (HazardDamage): a hazard hit costs 1 heart at no power, rising to 1 + HazardExtraHearts on
    // average at full power (carried in quarter hearts, so the sequence is 1,1,2,1,2,... never random), rocks fall up to
    // HazardSpeedMax faster, and a rock slot adds a second rock with a chance up to HazardBurstMax. Every rock still goes through the
    // spawn-space check, so a dodge gap always exists; rail mines, rides and elite docks are authored and untouched. ----
    public const float HazardExtraHearts = .75f, HazardSpeedMax = .2f, HazardBurstMax = .35f;

    // ---- LOOPS (LoopDifficulty): from the second pass on everything gets harder, exponentially: D(index) = LoopBase ^ index, index 0 (the
    // first pass) = exactly 1, so the first pass is identical to before. Every use below is CAPPED so a late loop is brutal, never
    // impossible and never a frame-rate problem. ----
    public const float LoopBase = 1.4f;
    public const int LoopMaxIndex = 5;                   // D stops growing at loop 6 (index 5): nothing grows past it (user: keep the caps)
    public const int EnemyHeartCap = 8;                  // red hearts on an ordinary enemy (the orbit draws at most 8)
    public const float FireRateCap = 2f;                 // enemy volley gap divided by min(D, this)
    public const float MinVolleyGap = .15f;              // seconds; no loop shortens a volley gap below it
    public const float ShotSpeedCap = 1.4f;              // enemy shot speed x min(sqrt(D), this)
    public const int ShotBonusPerD = 3, ShotAliveCap = 14;   // extra shots alive = round((D-1) x 3), never more than 14 alive in all
    public const float DensityCap = 1.8f;               // extra spawn density = min(sqrt(D), this); EnemyDensity.MaxThreats still caps enemies alive
    public const float BossRateCap = 2f, BossCooldownMin = .3f;   // boss cooldowns divided by min(D, 2), never under x0.3
    public const float HazardLoopRateMax = 1.8f, HazardLoopRateCap = 3.2f;   // extra hazard rate cap by loop; total rate cap from index 1
    public const float HazardCostPerLoop = .15f, HazardCostCap = 2f;       // hearts a hazard hit costs on average: capped at 2
    public const float RecoverMin = .2f;                // heals and shields x max(this, 1/D): never zero
    public const float KillScorePerLoop = .5f; public const int KillScoreLoopCap = 5;   // kill points x (1 + .5 x index)

    // ---- drone kills pay less as the squad grows (DroneScoring): x1 with one drone, DroneScoreMin at DampFullSquad drones. Drone kills
    // extend the kill chain but cannot lift its multiplier past the top step by themselves (a squad of 2+ needs
    // DroneChainUnlockKills of the pilot's own kills in the chain first). The player's own ship kills are unchanged. ----
    public const int DampFullSquad = 6;
    public const float DroneScoreOne = .5f, DroneScoreMin = .2f;
    public const int DroneChainUnlockKills = 2;

    public static float PowerBoost(HeartTierKind kind) { return kind == HeartTierKind.Boss ? BossBoost : kind == HeartTierKind.Elite ? EliteBoost : 1f; }
    public static float BarShare(HeartTierKind kind) { return kind == HeartTierKind.Boss ? BossBarShare : kind == HeartTierKind.Elite ? EliteBarShare : EnemyBarShare; }

    // Hearts in one extra bar for an entity with `red` red hearts.
    public static int BarHearts(HeartTierKind kind, int red) { return BarHearts(kind, red, 1f); }

    // `loopD` (LoopDifficulty.Current) grows an elite's and a boss's bars exponentially, never past the red count.
    public static int BarHearts(HeartTierKind kind, int red, float loopD)
    {
        int n = Mathf.CeilToInt(Mathf.Max(1, red) * BarShare(kind) * Mathf.Max(1f, loopD) - .0001f);
        return Mathf.Clamp(n, MinBarHearts, Mathf.Max(MinBarHearts, red));
    }
}

public static class HeartTierCurve
{
    static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    // Share of spawns that are blue-or-better at this power (0..1). 0 at power 0, monotonic.
    public static float BlueOrBetter(float power01, HeartTierKind kind)
    {
        float p = Mathf.Clamp01(power01 * HeartTierTuning.PowerBoost(kind));
        return HeartTierTuning.BlueMax * Smooth((p - HeartTierTuning.BlueStart) / (HeartTierTuning.BlueFull - HeartTierTuning.BlueStart));
    }

    // Blue-or-better share including the crystal ones (so crystal is always a subset): max(RunPower curve, crystal share).
    public static float BlueOrBetter(float power01, float dronePower01, HeartTierKind kind)
    {
        return Mathf.Max(BlueOrBetter(power01, kind), Crystal(dronePower01, kind));
    }

    // Share of spawns that are crystal at this DRONE power (RunPower.DronePower01), boosted for elites and bosses. 0 with no drones.
    public static float Crystal(float dronePower01, HeartTierKind kind)
    {
        float p = Mathf.Clamp01(dronePower01 * HeartTierTuning.PowerBoost(kind));
        return HeartTierTuning.CrystalMax * Smooth((p - HeartTierTuning.CrystalStart) / (HeartTierTuning.CrystalFull - HeartTierTuning.CrystalStart));
    }

    // The tier for a roll in [0,1): 0 red only, 1 blue+red, 2 crystal+blue+red. Rolls under the crystal share are crystal, then blue.
    public static int Tier(float power01, float dronePower01, HeartTierKind kind, float roll01) { return Tier(power01, dronePower01, kind, roll01, 0); }

    // From the second pass (index 1) nothing is red-only. Loops never force crystal: that is the drones' alone.
    public static int Tier(float power01, float dronePower01, HeartTierKind kind, float roll01, int loopIndex)
    {
        float roll = Mathf.Clamp01(roll01);
        float crystal = Crystal(dronePower01, kind);
        float blue = loopIndex >= 1 ? 1f : BlueOrBetter(power01, dronePower01, kind);
        if (roll < crystal) return 2;
        return roll < blue ? 1 : 0;
    }
}

// The run's strength, 0..1, from what the pilot has built so far this run. One number every tier decision reads.
public static class RunPower
{
    // Tests: stand in for the whole score (negative: measure the run).
    public static float Override = -1f;

    public static float Score01
    {
        get
        {
            if (Override >= 0f) return Mathf.Clamp01(Override);
            if (startMenu.youAreInTutorial || !HeartTierRoll.Live) return 0f;
            return Compute(ShotAtoms.PowerLevel, DroneSquad.ActiveCount, CoreLevelSum(), ShipWeaponUpgrades.Level(ShipId.Equipped()), RunScore.Parts.dustCount);
        }
    }

    // DRONE POWER 0..1 (drives the CRYSTAL share): drones out (of DroneFullSquad) and how upgraded they are -- each active drone's core
    // level and its per-drone shot atoms (Twin, Split, Ricochet, Pierce, Arc), against a full squad at the maxima. 0 with no drones.
    public static float DronePower01
    {
        get
        {
            if (Override >= 0f) return Mathf.Clamp01(Override);
            if (startMenu.youAreInTutorial || !HeartTierRoll.Live) return 0f;
            int cores = 0, atoms = 0, active = 0;
            for (int d = 0; d < DroneSquad.Slots; d++)
            {
                if (!DroneSquad.IsActive(d)) continue;
                active++;
                cores += Mathf.Min(DroneSquad.CoreLevel(d), HeartTierTuning.CoreLevelMax);
                for (int a = 0; a < ShotAtomTuning.PerDroneAtoms; a++) atoms += ShotAtoms.Level(d, (ShotAtom)a);
            }
            return DronePower01For(active, cores, atoms);
        }
    }

    // The pure formula (tests): active drones, their core level sum, their per-drone atom level sum.
    public static float DronePower01For(int active, int coreSum, int atomSum)
    {
        float full = HeartTierTuning.DroneFullSquad;
        float count = Mathf.Clamp01(active / full);
        float cores = Mathf.Clamp01(coreSum / (full * HeartTierTuning.CoreLevelMax));
        float atoms = Mathf.Clamp01(atomSum / (full * ShotAtomTuning.PerDroneAtoms * ShotAtomTuning.MaxLevel));
        float upgrades = HeartTierTuning.DroneCoreWeight * cores + (1f - HeartTierTuning.DroneCoreWeight) * atoms;
        return Mathf.Clamp01(HeartTierTuning.DroneCountWeight * count + (1f - HeartTierTuning.DroneCountWeight) * upgrades * (active > 0 ? 1f : 0f));
    }

    // Public, stable (read by other systems): the squad's drone count, and its 0..1 term.
    public static int ActiveDrones { get { return DroneSquad.ActiveCount; } }
    // The squad on its own, 0..1 (also what thins the shield atoms).
    public static float Drones01
    {
        get
        {
            if (Override >= 0f) return Mathf.Clamp01(Override);
            if (startMenu.youAreInTutorial || !HeartTierRoll.Live) return 0f;
            return DroneScore01(DroneSquad.ActiveCount, CoreLevelSum());
        }
    }

    static int CoreLevelSum()
    {
        int n = 0;
        for (int i = 0; i < DroneSquad.Slots; i++) if (DroneSquad.IsActive(i)) n += Mathf.Min(DroneSquad.CoreLevel(i), HeartTierTuning.DroneCoreFull);
        return n;
    }

    public static float DroneScore01(int active, int coreSum)
    {
        float count = Mathf.Clamp01(active / (float)HeartTierTuning.PowerFullSquad);
        float cores = Mathf.Clamp01(coreSum / (float)(HeartTierTuning.PowerFullSquad * HeartTierTuning.DroneCoreFull));
        return HeartTierTuning.DroneCountShare * count + (1f - HeartTierTuning.DroneCountShare) * cores;
    }

    // The pure formula (tests): atom power 0..60, active drones, their core level sum (each capped at DroneCoreFull), ship weapon
    // level 0..MaxLevel, dust count.
    public static float Compute(int atomPower, int activeDrones, int coreSum, int shipLevel, int dustCount)
    {
        float atoms = Mathf.Clamp01(atomPower / (float)ShotAtoms.MaxPower);
        float ship = Mathf.Clamp01(shipLevel / (float)Mathf.Max(1, ShipWeaponUpgrades.MaxLevel));
        float dust = Mathf.Clamp01(dustCount / (float)HeartTierTuning.DustFull);
        return Mathf.Clamp01(HeartTierTuning.WDrones * DroneScore01(activeDrones, coreSum) + HeartTierTuning.WAtoms * atoms +
                             HeartTierTuning.WShip * ship + HeartTierTuning.WDust * dust);
    }
}

// What the run's power does outside the hearts (all numbers in HeartTierTuning).
public static class HazardScaling
{
    // Extra hazard spawn rate at this power (>= 1), before the cap.
    public static float RateFor(float power01) { return 1f + HeartTierTuning.HazardRateMax * Mathf.Clamp01(power01); }

    // The factor rock and mine spawn delays are divided by on top of PowerCreep.SpawnScale: never pushes the two together past HazardRateCap.
    public static float ExtraRate(float power01, float existingScale) { return ExtraRate(power01, existingScale, 1f, 0); }

    public static float ExtraRate(float power01, float existingScale, float loopD, int loopIndex)
    {
        float want = RateFor(power01) * Mathf.Min(Mathf.Max(1f, loopD), HeartTierTuning.HazardLoopRateMax);
        float room = (loopIndex > 0 ? HeartTierTuning.HazardLoopRateCap : HeartTierTuning.HazardRateCap) / Mathf.Max(1f, existingScale);
        return Mathf.Max(1f, Mathf.Min(want, room));
    }

    public static float ExtraRateNow { get { return ExtraRate(RunPower.Score01, PowerCreep.SpawnScale, LoopDifficulty.Current, RunLoop.Index); } }

    // Hazard fall speed multiplier, burst chance (a second rock in a rock slot) at this power.
    public static float SpeedFor(float power01) { return 1f + HeartTierTuning.HazardSpeedMax * Mathf.Clamp01(power01); }
    public static float BurstChanceFor(float power01) { return HeartTierTuning.HazardBurstMax * Mathf.Clamp01(power01); }
    public static float SpeedNow { get { return SpeedFor(RunPower.Score01); } }
    public static float BurstChanceNow { get { return BurstChanceFor(RunPower.Score01); } }

    // Shield atom spawn clock multiplier: 1 with no squad, ShieldMin with a full one, never zero.
    public static float ShieldRateFor(float drones01) { return Mathf.Lerp(1f, HeartTierTuning.ShieldMin, Mathf.Clamp01(drones01)); }
    public static float ShieldRate { get { return ShieldRateFor(RunPower.Drones01) * LoopDifficulty.RecoverScale; } }
}

// Heals (the green atom, the heal bank, the healing drone) get rarer and slower as the run's power rises.
public static class HealScaling
{
    public static float RateFor(float power01) { return Mathf.Lerp(1f, HeartTierTuning.HealMin, Mathf.Clamp01(power01)); }
    public static float Rate { get { return RateFor(RunPower.Score01) * LoopDifficulty.RecoverScale; } }
    // The heal bank's size now (never below 1, HealBank.Max at no power).
    public static int BankMax(int max, float power01) { return Mathf.Max(1, Mathf.CeilToInt(max * RateFor(power01) - 1e-4f)); }
    // The healing drone waits this much longer to restore a heart (x1 at no power).
    public static float DelayScale { get { return 1f / Mathf.Max(.05f, Rate); } }
}

// What a hazard (rock or mine) costs the ship's hearts. Whole hearts, carried in quarters.
public static class HazardDamage
{
    static int bankQuarters;
    public static void Reset() { bankQuarters = 0; }

    // Hearts one hazard hit costs at this power, on average (1 .. 1 + HazardExtraHearts).
    public static float HeartsFor(float power01) { return HeartsFor(power01, 0); }

    // On a later loop a hazard hits harder still, but never past HazardCostCap (2) hearts on average.
    public static float HeartsFor(float power01, int loopIndex)
    {
        float h = 1f + HeartTierTuning.HazardExtraHearts * Mathf.Clamp01(power01);
        if (loopIndex > 0) h = Mathf.Min(HeartTierTuning.HazardCostCap, h * (1f + HeartTierTuning.HazardCostPerLoop * loopIndex));
        return h;
    }

    // The cost of this hit: always at least 1, the extra quarters carried until they make a whole heart. Not a hazard: 1.
    public static int Cost(bool isHazard, float power01) { return Cost(isHazard, power01, 0); }

    public static int Cost(bool isHazard, float power01, int loopIndex)
    {
        if (!isHazard) return 1;
        bankQuarters += Mathf.RoundToInt(HeartsFor(power01, loopIndex) * HeartDamage.PerHeart);
        int cost = Mathf.Max(1, bankQuarters / HeartDamage.PerHeart);
        bankQuarters -= cost * HeartDamage.PerHeart;
        return cost;
    }

    public static bool IsHazard(GameObject go)
    {
        if (go == null) return false;
        var def = EnemyIdentity.Of(go);
        return def != null ? def.IsHazard : go.CompareTag("Astr");
    }

    public static int Cost(GameObject go) { return Cost(IsHazard(go), RunPower.Score01, RunLoop.Index); }
}

// A big squad pays less per kill and cannot lift the chain to the top multiplier on its own.
public static class DroneScoring
{
    // Set by ShipAttackHits around a kill made by a drone's shot (HitKind.Drone).
    public static bool InProgress;

    public static float Squad01(int active) { return Mathf.Clamp01((active - 1) / (float)(HeartTierTuning.DampFullSquad - 1)); }
    // Points scale of a drone kill (elites too): DroneScoreOne with one drone, falling in a line to DroneScoreMin at DampFullSquad
    // (1 -> .5, 2 -> .44, 3 -> .38, 4 -> .32, 5 -> .26, 6 -> .2). The pilot's own kills are never scaled.
    public static float Scale(int active) { return Mathf.Lerp(HeartTierTuning.DroneScoreOne, HeartTierTuning.DroneScoreMin, Squad01(active)); }
    public static bool CapsChain(int active) { return active >= 2; }

    // The chain length a drone-extended chain is scored at: capped just under the top step until the pilot's own kills in it unlock it.
    public static int EffectiveChain(int chain, int ownKills, int active)
    {
        if (!CapsChain(active) || ownKills >= HeartTierTuning.DroneChainUnlockKills) return chain;
        var t = ScoreRules.ComboThresholds;
        return Mathf.Min(chain, t[t.Length - 1] - 1);
    }
}

// Picks a tier for each new entity. Deterministic: the roll is a hash of a per-run seed and a spawn counter (BeginRun resets both),
// so the same run seed gives the same sequence of tiers.
public static class HeartTierRoll
{
    public static int Seed;
    static int counter;
    // Tests: force a tier for every pick (negative: roll).
    public static int ForceTier = -1;

    // Only a real run rolls tiers (set by BeginRun; the tutorial, menus and editor-mode fixtures stay red-only unless a test forces it).
    public static bool Live;
    public static void BeginRun(int seed, bool live = true) { Seed = seed; counter = 0; HazardDamage.Reset(); Live = live && !startMenu.youAreInTutorial; }

    public static float NextRoll()
    {
        unchecked
        {
            uint x = (uint)(Seed * 374761393 + (++counter) * 668265263 + 12345);
            x = (x ^ (x >> 13)) * 1274126177u;
            x ^= x >> 16;
            return (x & 0xFFFFFF) / 16777216f;
        }
    }

    public static int Pick(HeartTierKind kind)
    {
        if (ForceTier >= 0) return Mathf.Clamp(ForceTier, 0, 2);
        if (!Live) return 0;
        float power = RunPower.Score01;
        int loop = RunLoop.Index;
        float drones = RunPower.DronePower01;
        if (power <= 0f && drones <= 0f && loop <= 0) return 0;   // (no roll spent: red-only is free on the first pass)
        return HeartTierCurve.Tier(power, drones, kind, NextRoll(), loop);
    }
}

// The blue and crystal bars in front of an entity's red hearts.
public struct HeartShell
{
    public int blueMax, crystalMax;      // hearts per bar (0: no such bar)
    public int blueQ, crystalQ;          // quarter units left

    public int Tier { get { return crystalMax > 0 ? 2 : blueMax > 0 ? 1 : 0; } }
    public bool Any { get { return blueQ + crystalQ > 0; } }
    public HeartLayer Layer { get { return crystalQ > 0 ? HeartLayer.Crystal : blueQ > 0 ? HeartLayer.Blue : HeartLayer.Red; } }
    public int QuartersLeft { get { return blueQ + crystalQ; } }
    // Quarter units left in the bar now showing (0 when only red is left).
    public int LayerQuarters { get { return crystalQ > 0 ? crystalQ : blueQ; } }
    public int LayerMaxHearts { get { return crystalQ > 0 ? crystalMax : blueQ > 0 ? blueMax : 0; } }

    public static HeartShell Make(int tier, HeartTierKind kind, int redHearts)
    {
        var s = new HeartShell();
        if (tier >= 1) { s.blueMax = HeartTierTuning.BarHearts(kind, redHearts, LoopDifficulty.Current); s.blueQ = s.blueMax * HeartDamage.PerHeart; }
        if (tier >= 2) { s.crystalMax = HeartTierTuning.BarHearts(kind, redHearts, LoopDifficulty.Current); s.crystalQ = s.crystalMax * HeartDamage.PerHeart; }
        return s;
    }

    // Spends `units` on the outer bars first and returns what is left over for the red hearts. `broken` gets the number of bars
    // emptied by this hit (0..2).
    public int Absorb(int units, out int broken)
    {
        broken = 0;
        if (units <= 0) return 0;
        if (crystalQ > 0)
        {
            int take = Mathf.Min(units, crystalQ);
            crystalQ -= take; units -= take;
            if (crystalQ == 0) broken++;
        }
        if (units > 0 && blueQ > 0)
        {
            int take = Mathf.Min(units, blueQ);
            blueQ -= take; units -= take;
            if (blueQ == 0) broken++;
        }
        return units;
    }

    // Colour of the break flash for a layer.
    public static Color FlashColour(int layer)
    {
        return layer == 2 ? new Color(.86f, .88f, 1f, 1f) : layer == 1 ? new Color(.35f, .75f, 1f, 1f) : Color.white;
    }
}

// The heart art, loaded by name from Resources/Vfx/Hearts: heart_<red|blue|crystal>_idle_0..5 (a looping flipbook), heart_<tier>_break_0..5
// (played once, from a pool, when a whole heart of that bar is lost or the bar breaks) and heart_empty. Missing frames fall back to the
// player's lifeHeart so nothing ever draws blank. (The blue and crystal frames shipped first are PLACEHOLDER recolours.)
public static class HeartArt
{
    public const string Folder = "Vfx/Hearts/";
    public const string FallbackPath = "Vfx/lifeHeart";
    public const int Frames = 6;
    public const float IdleFps = 8f;
    public static readonly string[] Tiers = { "red", "blue", "crystal" };
    static Sprite[][] idle, brk;
    static Sprite empty, fallback;

    static Sprite Fallback { get { if (fallback == null) fallback = Resources.Load<Sprite>(FallbackPath); return fallback; } }

    static Sprite[] Load(string kind, int layer)
    {
        var a = new Sprite[Frames];
        for (int i = 0; i < Frames; i++)
        {
            a[i] = Resources.Load<Sprite>(Folder + "heart_" + Tiers[layer] + "_" + kind + "_" + i);
            if (a[i] == null) a[i] = Fallback;
        }
        return a;
    }

    public static Sprite[] Idle(int layer)
    {
        if (idle == null) idle = new Sprite[3][];
        layer = Mathf.Clamp(layer, 0, 2);
        if (idle[layer] == null) idle[layer] = Load("idle", layer);
        return idle[layer];
    }

    public static Sprite[] Break(int layer)
    {
        if (brk == null) brk = new Sprite[3][];
        layer = Mathf.Clamp(layer, 0, 2);
        if (brk[layer] == null) brk[layer] = Load("break", layer);
        return brk[layer];
    }

    public static Sprite Empty { get { if (empty == null) empty = Resources.Load<Sprite>(Folder + "heart_empty"); return empty; } }
    public static Sprite Full(int layer) { return Idle(layer)[0]; }
    public const float BreakSeconds = .42f;
    public static int BreakFrame(float age) { return Mathf.Clamp(Mathf.FloorToInt(age / BreakSeconds * Frames), 0, Frames - 1); }
    public static int IdleFrame(float clock, int heart) { return ((int)(clock * IdleFps) + heart) % Frames; }

    // Builds every layer's frames, outlines and quarter sets now (a run start), so no first break stalls.
    public static void Warm()
    {
        for (int l = 0; l < 3; l++)
        {
            var f = Idle(l); Break(l);
            HeartQuarterFrames.For(f[0]);
        }
    }
}
