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
    public const float CrystalStart = .38f, CrystalFull = .9f, CrystalMax = .8f;
    // Elites and bosses: power x boost (zero stays zero: red-only at no power for everyone).
    public const float EliteBoost = 1.35f, BossBoost = 1.6f;

    // ---- hazards scale with the same power (HazardScaling): rocks and mines spawn up to HazardRateMax more often (x1.5 at power 1),
    // and the whole spawn-rate multiplier (PowerCreep x this) is capped at HazardRateCap so a dodge line always exists. Hazards have
    // no hearts (one hit kills them), so only their rate scales. Rails, rail mines on rides and elite docks are authored: untouched. ----
    public const float HazardRateMax = .5f, HazardRateCap = 2.4f;

    // ---- shield atoms thin out as the squad grows: x1 with no drone bonus, ShieldMin (never zero) at a full squad ----
    public const float ShieldMin = .4f;

    // ---- drone kills pay less as the squad grows (DroneScoring): x1 with one drone, DroneScoreMin at DampFullSquad drones. Drone kills
    // extend the kill chain but cannot lift its multiplier past the top step by themselves (a squad of 2+ needs
    // DroneChainUnlockKills of the pilot's own kills in the chain first). The player's own ship kills are unchanged. ----
    public const int DampFullSquad = 6;
    public const float DroneScoreMin = .5f;
    public const int DroneChainUnlockKills = 2;

    public static float PowerBoost(HeartTierKind kind) { return kind == HeartTierKind.Boss ? BossBoost : kind == HeartTierKind.Elite ? EliteBoost : 1f; }
    public static float BarShare(HeartTierKind kind) { return kind == HeartTierKind.Boss ? BossBarShare : kind == HeartTierKind.Elite ? EliteBarShare : EnemyBarShare; }

    // Hearts in one extra bar for an entity with `red` red hearts.
    public static int BarHearts(HeartTierKind kind, int red)
    {
        int n = Mathf.CeilToInt(Mathf.Max(1, red) * BarShare(kind) - .0001f);
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

    // Share of spawns that are crystal (always <= BlueOrBetter). 0 at power 0, monotonic.
    public static float Crystal(float power01, HeartTierKind kind)
    {
        float p = Mathf.Clamp01(power01 * HeartTierTuning.PowerBoost(kind));
        return Mathf.Min(BlueOrBetter(power01, kind),
            HeartTierTuning.CrystalMax * Smooth((p - HeartTierTuning.CrystalStart) / (HeartTierTuning.CrystalFull - HeartTierTuning.CrystalStart)));
    }

    // The tier for a roll in [0,1): 0 red only, 1 blue+red, 2 crystal+blue+red. Rolls under the crystal share are crystal, then blue.
    public static int Tier(float power01, HeartTierKind kind, float roll01)
    {
        float roll = Mathf.Clamp01(roll01);
        float crystal = Crystal(power01, kind), blue = BlueOrBetter(power01, kind);
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
    public static float ExtraRate(float power01, float existingScale)
    {
        float want = RateFor(power01);
        float room = HeartTierTuning.HazardRateCap / Mathf.Max(1f, existingScale);
        return Mathf.Max(1f, Mathf.Min(want, room));
    }

    public static float ExtraRateNow { get { return ExtraRate(RunPower.Score01, PowerCreep.SpawnScale); } }

    // Shield atom spawn clock multiplier: 1 with no squad, ShieldMin with a full one, never zero.
    public static float ShieldRateFor(float drones01) { return Mathf.Lerp(1f, HeartTierTuning.ShieldMin, Mathf.Clamp01(drones01)); }
    public static float ShieldRate { get { return ShieldRateFor(RunPower.Drones01); } }
}

// A big squad pays less per kill and cannot lift the chain to the top multiplier on its own.
public static class DroneScoring
{
    // Set by ShipAttackHits around a kill made by a drone's shot (HitKind.Drone).
    public static bool InProgress;

    public static float Squad01(int active) { return Mathf.Clamp01((active - 1) / (float)(HeartTierTuning.DampFullSquad - 1)); }
    // Points scale of a drone kill: 1 with one drone, DroneScoreMin at DampFullSquad.
    public static float Scale(int active) { return Mathf.Lerp(1f, HeartTierTuning.DroneScoreMin, Squad01(active)); }
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
    public static void BeginRun(int seed, bool live = true) { Seed = seed; counter = 0; Live = live && !startMenu.youAreInTutorial; }

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
        if (power <= 0f) return 0;   // (no roll spent: red-only is free)
        return HeartTierCurve.Tier(power, kind, NextRoll());
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
        if (tier >= 1) { s.blueMax = HeartTierTuning.BarHearts(kind, redHearts); s.blueQ = s.blueMax * HeartDamage.PerHeart; }
        if (tier >= 2) { s.crystalMax = HeartTierTuning.BarHearts(kind, redHearts); s.crystalQ = s.crystalMax * HeartDamage.PerHeart; }
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
