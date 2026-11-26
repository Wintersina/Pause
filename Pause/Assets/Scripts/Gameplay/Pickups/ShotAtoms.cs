using System.Collections.Generic;
using UnityEngine;

// Six run-long modifiers for companion guns. TWIN, PIERCE, SPLIT, RICOCHET and ARC
// activate dormant slots first, then upgrade the weakest eligible drone deterministically.
// RAPID is shared. Directional cores set firing mode independently of these modifiers.
// BeginRun clears the squad and modifiers. The starter core precedes TWIN / TWIN / ARC;
// later drops can offer one random directional core. Bosses recruit drones instead of coins.
// PowerLevel uses a fixed starting-squad divisor so recruiting never reduces spawn pressure.
public enum ShotAtom { Twin = 0, Pierce = 1, Split = 2, Ricochet = 3, Arc = 4, Rapid = 5 }

public static class ShotAtomTuning
{
    public const int Count = 6;
    public const int PerDroneAtoms = 5;           // Twin .. Arc are per drone; Rapid (index 5) is one shared level
    public const int MaxDrones = FloatieTuning.MaxFloaties;
    public const int MaxLevel = 10;

    // ---- drops ----
    public const float EliteDropChance = 1f;      // a killed elite: every one drops (there are up to 12 drones x 5 atoms to fill)
    public const float EnemyDropChance = .08f;    // an ordinary enemy kill (not a hazard, elite or boss)
    public const float StartingLevelDropChance = .18f; // the world the run began on, not hard-coded to Space
    public const float EnemyDropCooldown = 12f;   // s: at most one drop from ordinary enemies per this (the atom spacing still applies)
    // ATOM RUSH booster (one run): every chance x RushMultiplier (capped at 1), the ordinary-enemy cooldown x RushCooldownScale.
    public const float RushMultiplier = 3f;       // +200%
    public const float RushCooldownScale = .5f;
    public const bool FirstEliteGuaranteed = true; // the first elite killed in a run always drops one (later ones use EliteDropChance)
    public const float MaxedDust = 2f;            // star dust a catch pays once the atom is at its level cap (the blue atom's)
    public const int MaxCoinsOnScreen = 3;        // coins waiting for the atom spacing at once, at most

    // Every table below is indexed by LEVEL (0..MaxLevel); level 0 is "no atom" (the one plain bullet).

    // TWIN keeps a consistent three-quarter heart per bullet at every upgraded level. More
    // bullets and the wider fan grow its volley without arbitrary percentage multipliers.
    public static readonly int[] TwinShots = { 1, 2, 3, 4, 4, 5, 5, 6, 6, 7, 7 };
    public static readonly float[] TwinDamage = { 1f, .75f, .75f, .75f, .75f, .75f, .75f, .75f, .75f, .75f, .75f };
    public const float TwinParallelGap = .16f;
    public static readonly float[] TwinHalfFan = { 0f, 0f, 8f, 12f, 13f, 15f, 16f, 18f, 19f, 20f, 21f };
    public static readonly float[][] TwinAngles = Fans(TwinShots, TwinHalfFan);
    public static readonly float[][] TwinOffsets = TwinOffsetsFor(TwinShots);

    // ---- PIERCE: enemies a shot passes through, by level ----
    public static readonly int[] PierceThrough = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
    public const int MaxPierce = 10;

    // ---- SPLIT: shards by level, the half-width of their spread off the shot's heading, speed, life and the damage of each (in hearts) ----
    public static readonly int[] SplitShards = { 0, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6 };
    public static readonly float[] SplitHalfFan = { 0f, 35f, 45f, 50f, 60f, 60f, 65f, 65f, 72f, 72f, 75f };
    public static readonly float[][] SplitAngles = Fans(SplitShards, SplitHalfFan);
    public static readonly float[] SplitDamage = { 0f, .50f, .50f, .50f, .50f, .50f, .75f, .75f, .75f, .75f, .75f };
    public const float ShardSpeed = 7f, ShardLife = .5f;

    // ---- RICOCHET: side-edge bounces by level; a near-straight shot leans a little so a bounce can happen at all.
    // The bullet's life grows a little with its bounces so they can be used, but never past RicochetLifeCap (0: the plain MaxAge). ----
    public static readonly int[] Bounces = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
    public const float RicochetLeanDegrees = 14f, RicochetStraightWithin = 3f;
    public const float RicochetLifeCap = 3.6f;
    public static readonly float[] RicochetLife = { 0f, 2.52f, 2.64f, 2.76f, 2.88f, 3f, 3.12f, 3.24f, 3.36f, 3.48f, RicochetLifeCap };

    // ---- ARC: spark range (world units), damage (in hearts) and jumps by level ----
    public static readonly float[] ArcRange = { 0f, 2f, 2.25f, 2.5f, 2.75f, 3f, 3.25f, 3.5f, 3.75f, 4f, 4.25f };
    public static readonly float[] ArcDamage = { 0f, .50f, .75f, .75f, .75f, .75f, .75f, .75f, 1f, 1f, 1f };
    public static readonly int[] ArcJumps = { 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 6 };
    public const int MaxArcJumps = 6;

    // ---- RAPID: the gun's fire-rate bonus by level (cooldown = base / (1 + bonus), never under MinCooldown) ----
    public static readonly float[] RapidBonus = { 0f, .25f, .50f, .75f, 1f, 1.25f, 1.50f, 1.75f, 2f, 2.25f, 2.50f };
    public const float MinCooldown = .5f;   // s: the floor of any floatie's cooldown, so the gun never becomes a laser stream

    // Separate drone budget; AttackPool reserves another 40 ultimate projectiles.
    public const int MaxBullets = 128;

    // ---- the fan of n shots spread evenly over +-half degrees (n = 1: straight; the parallel pair has no fan) ----
    static float[][] Fans(int[] counts, float[] half)
    {
        var all = new float[counts.Length][];
        for (int l = 0; l < counts.Length; l++)
        {
            int n = counts[l];
            all[l] = new float[n];
            if (n < 2) continue;
            for (int k = 0; k < n; k++) all[l][k] = -half[l] + 2f * half[l] * k / (n - 1);
        }
        return all;
    }

    static float[][] TwinOffsetsFor(int[] counts)
    {
        var all = new float[counts.Length][];
        for (int l = 0; l < counts.Length; l++)
        {
            all[l] = new float[counts[l]];
            if (l == 1) { all[l][0] = -TwinParallelGap * .5f; all[l][1] = TwinParallelGap * .5f; }
        }
        return all;
    }

    // ---- look ----
    // ARC's own lightning: pale, cool, never pink (the Volt Viper arc is sodium white on teal).
    public static readonly Color ArcCore = new Color(.88f, .92f, 1f, 1f);
    public static readonly Color ArcOuter = new Color(.55f, .5f, .95f, .3f);
    public static readonly Color ArcStar = new Color(.78f, .84f, 1f, 1f);
}

public static class ShotAtoms
{
    static readonly int[] droneLevels = new int[ShotAtomTuning.MaxDrones * ShotAtomTuning.PerDroneAtoms];
    static int DL(int drone, int atom) { return drone * ShotAtomTuning.PerDroneAtoms + atom; }
    static int rapidLevel;
    static int droneCount = DroneSquad.StartingSlots;
    static readonly List<ShotAtom> order = new List<ShotAtom>(ShotAtomTuning.Count);

    // Bumped on every change (the HUD row redraws on it).
    public static int Version { get; private set; }
    public static int Catches { get; private set; }       // coins collected this run
    public static int MaxedCatches { get; private set; }  // ...that paid dust

    public static void BeginRun(int startingSlots = -1)
    {
        DroneSquad.BeginRun(startingSlots);
        droneCount = DroneSquad.StartingSlots;
        for (int d = 0; d < ShotAtomTuning.MaxDrones; d++)
            for (int a = 0; a < ShotAtomTuning.PerDroneAtoms; a++) droneLevels[DL(d, a)] = 0;
        rapidLevel = 0;
        order.Clear();
        Catches = 0;
        MaxedCatches = 0;
        ShotAtomDrops.Clear();
        ShotAtomDrops.NewRun();
        Version++;
    }

    // How many drones this run's ship flies with (FloatieBattery sets it): a coin is dealt among them.
    public static int DroneCount { get { return droneCount; } }
    public static void SetDroneCount(int n)
    {
        n = Mathf.Clamp(n, 1, ShotAtomTuning.MaxDrones);
        DroneSquad.SetSlots(n);
        if (n == droneCount) return;
        droneCount = n;
        Version++;
    }

    public static bool IsShared(ShotAtom atom) { return atom == ShotAtom.Rapid; }

    // Drone `drone`'s level of a per-drone atom (Rapid: the shared level, whatever the drone).
    public static int Level(int drone, ShotAtom atom)
    {
        if (IsShared(atom)) return rapidLevel;
        if (drone < 0 || drone >= ShotAtomTuning.MaxDrones) return 0;
        return droneLevels[DL(drone, (int)atom)];
    }

    // The atom's level overall: Rapid's shared level, or the highest any drone has (the old single-gun reading).
    public static int Level(ShotAtom atom)
    {
        if (IsShared(atom)) return rapidLevel;
        int best = 0;
        for (int d = 0; d < droneCount; d++) best = Mathf.Max(best, droneLevels[DL(d, (int)atom)]);
        return best;
    }

    public static int OrderCount { get { return order.Count; } }
    public static ShotAtom OrderAt(int i) { return order[i]; }

    // Tests and previews: every drone to `level` (Rapid: the shared level).
    public static void SetLevel(ShotAtom atom, int level)
    {
        level = Mathf.Clamp(level, 0, ShotAtomTuning.MaxLevel);
        if (IsShared(atom)) rapidLevel = level;
        else for (int d = 0; d < ShotAtomTuning.MaxDrones; d++) droneLevels[DL(d, (int)atom)] = level;
        Touch(atom);
    }

    // Tests and previews: one drone's level.
    public static void SetLevel(int drone, ShotAtom atom, int level)
    {
        level = Mathf.Clamp(level, 0, ShotAtomTuning.MaxLevel);
        if (IsShared(atom)) rapidLevel = level;
        else if (drone >= 0 && drone < ShotAtomTuning.MaxDrones) droneLevels[DL(drone, (int)atom)] = level;
        Touch(atom);
    }

    static void Touch(ShotAtom atom)
    {
        bool any = rapidLevel > 0 && IsShared(atom) || Level(atom) > 0;
        if (any && !order.Contains(atom)) order.Add(atom);
        if (!any) order.Remove(atom);
        Version++;
    }

    // Is drone `drone` below the cap for this atom (Rapid: the shared level below the cap)?
    public static bool DroneCanTake(int drone, ShotAtom atom)
    {
        return Level(drone, atom) < ShotAtomTuning.MaxLevel;
    }

    public static int EligibleDrones(ShotAtom atom)
    {
        if (IsShared(atom)) return rapidLevel < ShotAtomTuning.MaxLevel ? droneCount : 0;
        int n = 0;
        for (int d = 0; d < droneCount; d++) if (droneLevels[DL(d, (int)atom)] < ShotAtomTuning.MaxLevel) n++;
        return n;
    }

    // ---- drops: every atom can drop in every world, until it is at the top level ----
    public static bool CanDrop(ShotAtom atom)
    {
        return EligibleDrones(atom) > 0;   // not maxed on EVERY drone (Rapid: its one shared level)
    }

    public static int EligibleCount()
    {
        int n = 0;
        for (int i = 0; i < ShotAtomTuning.Count; i++) if (CanDrop((ShotAtom)i)) n++;
        return n;
    }

    // A random atom that is not maxed yet (`roll01` in 0..1 picks which); false when all six are maxed (a drop pays dust then).
    public static bool TryPick(float roll01, out ShotAtom atom)
    {
        int n = EligibleCount();
        atom = ShotAtom.Twin;
        if (n == 0) return false;
        int pick = Mathf.Min(n - 1, Mathf.FloorToInt(Mathf.Clamp01(roll01) * n));
        for (int i = 0; i < ShotAtomTuning.Count; i++)
        {
            if (!CanDrop((ShotAtom)i)) continue;
            if (pick-- == 0) { atom = (ShotAtom)i; return true; }
        }
        return false;
    }

    // ---- catching a coin ----
    public struct Catch
    {
        public ShotAtom atom;
        public int drone;        // the drone that got the level (-1: Rapid, which every drone shares; also when maxed)
        public int beforeLevel;
        public bool activated;
        public int level;        // the level after the catch (the cap when it was already there)
        public bool maxed;       // maxed on every drone: the catch pays dust, no level
    }

    // A coin was caught: ONE drone, picked at random among those not maxed for the atom (`roll01` 0..1 picks which; -1 rolls it), goes up a
    // level. Rapid levels the one shared value. Maxed on every drone (a second coin of the atom was in the air when the last level
    // landed): the catch pays star dust, no level.
    public static Catch Collect(ShotAtom atom, float roll01 = -1f)
    {
        Catches++;
        var c = new Catch { atom = atom, drone = -1, level = Level(atom) };
        int eligible = EligibleDrones(atom);
        if (eligible == 0) { c.maxed = true; MaxedCatches++; Version++; return c; }
        if (IsShared(atom))
        {
            c.beforeLevel = rapidLevel;
            c.level = ++rapidLevel;
            if (DroneSquad.ActiveCount == 0) c.activated = DroneSquad.Activate(0);
        }
        else
        {
            int target = DroneSquad.ModifierRecipient(atom);
            if (target < 0) { c.maxed = true; MaxedCatches++; Version++; return c; }
            c.beforeLevel = Level(target, atom);
            c.activated = DroneSquad.Activate(target);
            c.drone = target;
            c.level = ++droneLevels[DL(target, (int)atom)];
            DroneSquad.Select(target);
        }
        if (!order.Contains(atom)) order.Add(atom);
        Version++;
        return c;
    }

    public static string BenefitChange(ShotAtom atom, int before, int after)
    {
        switch (atom)
        {
            case ShotAtom.Twin:
                if (ShotAtomTuning.TwinShots[before] == ShotAtomTuning.TwinShots[after]) return "FAN " + (ShotAtomTuning.TwinHalfFan[before] * 2f) + " → " + (ShotAtomTuning.TwinHalfFan[after] * 2f) + " DEGREES";
                return ShotAtomTuning.TwinShots[before] + " SHOT" + (ShotAtomTuning.TwinShots[before] == 1 ? "" : "S") + " → " + ShotAtomTuning.TwinShots[after] + " SHOTS";
            case ShotAtom.Pierce: return "PASSES THROUGH " + ShotAtomTuning.PierceThrough[before] + " → " + ShotAtomTuning.PierceThrough[after] + " ENEMIES";
            case ShotAtom.Split:
                if (ShotAtomTuning.SplitShards[before] == ShotAtomTuning.SplitShards[after])
                    return ShotAtomTuning.SplitDamage[before] != ShotAtomTuning.SplitDamage[after] ? "SHARD DAMAGE " + Mathf.RoundToInt(ShotAtomTuning.SplitDamage[before] * 100f) + "% → " + Mathf.RoundToInt(ShotAtomTuning.SplitDamage[after] * 100f) + "%" : "SHARD FAN " + (ShotAtomTuning.SplitHalfFan[before] * 2f) + " → " + (ShotAtomTuning.SplitHalfFan[after] * 2f) + " DEGREES";
                return ShotAtomTuning.SplitShards[before] + " → " + ShotAtomTuning.SplitShards[after] + " SHARDS, " + Mathf.RoundToInt(ShotAtomTuning.SplitDamage[after] * 100f) + "% EACH";
            case ShotAtom.Arc:
                if (ShotAtomTuning.ArcJumps[before] == ShotAtomTuning.ArcJumps[after])
                    return ShotAtomTuning.ArcDamage[before] != ShotAtomTuning.ArcDamage[after] ? "ARC DAMAGE " + Mathf.RoundToInt(ShotAtomTuning.ArcDamage[before] * 100f) + "% → " + Mathf.RoundToInt(ShotAtomTuning.ArcDamage[after] * 100f) + "%" : "ARC REACH " + ShotAtomTuning.ArcRange[before] + " → " + ShotAtomTuning.ArcRange[after];
                return ShotAtomTuning.ArcJumps[before] + " → " + ShotAtomTuning.ArcJumps[after] + " CHAIN JUMPS";
            case ShotAtom.Ricochet: return ShotAtomTuning.Bounces[before] + " → " + ShotAtomTuning.Bounces[after] + " BOUNCES";
            default: return "+" + Mathf.RoundToInt(ShotAtomTuning.RapidBonus[before] * 100f) + "% → +" + Mathf.RoundToInt(ShotAtomTuning.RapidBonus[after] * 100f) + "% FIRE RATE";
        }
    }

    public const string MaxedWord = "MAXED +DUST";

    public static readonly string[] Roman = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };

    // Shared Rapid + modifier levels normalized to this run's dock-unlocked starting slots + active-drone bonus.
    // Each additional active drone contributes four points; recruitment never dilutes existing power.
    public const int MaxPower = ShotAtomTuning.Count * ShotAtomTuning.MaxLevel;
    public static int PowerLevel
    {
        get
        {
            int sum = 0;
            for (int d = 0; d < droneCount; d++)
                for (int a = 0; a < ShotAtomTuning.PerDroneAtoms; a++) sum += droneLevels[DL(d, a)];
            return Mathf.Min(MaxPower, rapidLevel + Mathf.RoundToInt(sum / (float)DroneSquad.StartingSlots) + 4 * Mathf.Max(0, DroneSquad.ActiveCount - 1));
        }
    }

    public static string Name(ShotAtom atom) { return atom.ToString().ToUpperInvariant(); }

    // "TWIN II"
    public static string Callout(ShotAtom atom, int level)
    {
        return Name(atom) + " " + Roman[Mathf.Clamp(level, 0, ShotAtomTuning.MaxLevel)];
    }

    // ---- the catch popup (ScoreHud.ShowAtomUpgrade): a title and a one-line effect hint ----
    public const float UpgradeSeconds = 1.1f;

    // Recipient and atom metadata for the compact upgrade card; UpgradeHint supplies its benefit line.
    public static string UpgradeTitle(ShotAtom atom, int drone, string droneName)
    {
        if (atom == ShotAtom.Rapid) return "RAPID FIRE  ALL DRONES";
        return Name(atom) + " SHOT -> " + (string.IsNullOrEmpty(droneName) ? "DRONE" : droneName);
    }

    // Small header metadata: "LV 4"
    public static string UpgradeLevel(int level)
    {
        return "LV " + Mathf.Clamp(level, 1, ShotAtomTuning.MaxLevel);
    }

    // "2 shots at once", "+20% fire rate": what this level does; the top level leads with "MAXED" (the title stays short: LV 10)
    public static string UpgradeHint(ShotAtom atom, int level)
    {
        level = Mathf.Clamp(level, 1, ShotAtomTuning.MaxLevel);
        string effect = EffectHint(atom, level);
        return level >= ShotAtomTuning.MaxLevel ? MaxedTag + effect : effect;
    }

    public const string MaxedTag = "MAXED  ";

    static string EffectHint(ShotAtom atom, int level)
    {
        switch (atom)
        {
            case ShotAtom.Twin:
                return ShotAtomTuning.TwinShots[level] + (level == 1 ? " shots at once" : " shots in a fan");
            case ShotAtom.Pierce:
                return "shots pass through " + ShotAtomTuning.PierceThrough[level] + (level == 1 ? " enemy" : " enemies");
            case ShotAtom.Split:
                return "hits break into " + ShotAtomTuning.SplitShards[level] + " shards";
            case ShotAtom.Ricochet:
                return "shots bounce " + ShotAtomTuning.Bounces[level] + (level == 1 ? " time" : " times") + " off the sides";
            case ShotAtom.Arc:
                return ShotAtomTuning.ArcJumps[level] == 1 ? "hits zap a nearby enemy" : "zaps jump to " + ShotAtomTuning.ArcJumps[level] + " enemies";
            default:
                return "+" + Mathf.RoundToInt(ShotAtomTuning.RapidBonus[level] * 100f) + "% fire rate";
        }
    }

    // "TWIN MAXED  +2 DUST"
    public static string MaxedTitle(ShotAtom atom)
    {
        return Name(atom) + " MAXED  +" + ShotAtomTuning.MaxedDust.ToString("0.##") + " DUST";
    }

    public static string MaxedHint() { return "maxed on every drone"; }

    public static Color Colour(ShotAtom atom)
    {
        switch (atom)
        {
            case ShotAtom.Twin: return AkiraPalette.Amber;
            case ShotAtom.Pierce: return AkiraPalette.Cyan;
            case ShotAtom.Split: return new Color(.91f, .91f, .29f);
            case ShotAtom.Ricochet: return AkiraPalette.Sodium;
            case ShotAtom.Arc: return new Color(.86f, .84f, 1f);
            default: return AkiraPalette.RedHi;
        }
    }

    // ---- what the gun reads (FloatieGuns) ----
    public static float RapidBonus() { return ShotAtomTuning.RapidBonus[Level(ShotAtom.Rapid)]; }
    // A cooldown after RAPID (a cooldown already under the floor is left alone: the floor only stops RAPID from going below it).
    public static float Rapid(float seconds) { return Mathf.Max(Mathf.Min(seconds, ShotAtomTuning.MinCooldown), seconds / (1f + RapidBonus())); }

    public static PickupKind KindOf(ShotAtom atom) { return (PickupKind)((int)PickupKind.ShotTwin + (int)atom); }
    public static bool TryAtomOf(PickupKind kind, out ShotAtom atom)
    {
        int i = (int)kind - (int)PickupKind.ShotTwin;
        atom = (ShotAtom)Mathf.Clamp(i, 0, ShotAtomTuning.Count - 1);
        return i >= 0 && i < ShotAtomTuning.Count;
    }
    public static string Key(ShotAtom atom) { return atom.ToString().ToLowerInvariant(); }
}

// The coins waiting to be released: a drop is decided when an elite or boss dies, and the coin appears at its spot as soon as the
// atom spacing (AtomSpacing: no two atoms within a few seconds) allows, in a run's own spawn space.
public static class ShotAtomDrops
{
    static readonly List<Vector3> queue = new List<Vector3>();

    public static int Pending { get { return queue.Count; } }
    public static int Elites { get; private set; }      // drops earned from elites / bosses this session (tests)
    public static int Bosses { get; private set; }
    public static int Released { get; private set; }
    public static int DustDrops { get; private set; }
    public static ShotAtom LastAtom { get; private set; }
    public static bool LastWasDust { get; private set; }
    public static System.Action<ShotAtom, Vector3> Spawned;   // tests

    public static void Clear() { queue.Clear(); }

    // Has this run's first elite kill happened (it always drops)? Reset by ShotAtoms.BeginRun.
    public static bool FirstEliteDone { get; private set; }
    public static int RunCoinsReleased { get; private set; }
    public static void NewRun()
    {
        FirstEliteDone = false; lastEnemyDrop = -9999f;
        RunCoinsReleased = 0;
    }

    public static void ResetSeams() { ClockOverride = null; EnemyRollOverride = -1f; lastEnemyDrop = -9999f; }

    static bool Counts()
    {
        if (startMenu.youAreInTutorial) return false;
        if (buttonClicks.playerDied) return false;
        return true;
    }

    // Test seam: a clock that can be moved by hand (null: Time.time).
    public static System.Func<float> ClockOverride;
    static float Now { get { return ClockOverride != null ? ClockOverride() : Time.time; } }
    static float lastEnemyDrop = -9999f;
    // Test seam: the roll an ordinary kill uses when none is given (negative: Random.value).
    public static float EnemyRollOverride = -1f;
    public static int Enemies { get; private set; }     // drops earned from ordinary kills this session (tests)

    // Atom Rush multiplies a chance (never past certain).
    public static float Rushed(float chance) { return Boosters.RunRushActive ? Mathf.Min(1f, chance * ShotAtomTuning.RushMultiplier) : chance; }
    public static float EnemyChanceFor(int world, int startingWorld)
    {
        return Rushed(world == startingWorld ? ShotAtomTuning.StartingLevelDropChance : ShotAtomTuning.EnemyDropChance);
    }
    public static float EnemyChance { get { return EnemyChanceFor(WorldManager.CurrentIndex, WorldManager.RunStart); } }
    public static float EnemyCooldown { get { return Boosters.RunRushActive ? ShotAtomTuning.EnemyDropCooldown * ShotAtomTuning.RushCooldownScale : ShotAtomTuning.EnemyDropCooldown; } }

    // A killed elite: a coin every time (EliteDropChance; Atom Rush keeps it certain) (`roll` 0..1: below the chance drops; -1 rolls it).
    public static bool OnEliteKilled(Vector3 at, float roll = -1f)
    {
        if (!Counts()) return false;
        if (roll < 0f) roll = Random.value;
        bool first = !FirstEliteDone;
        FirstEliteDone = true;
        if (!(first && ShotAtomTuning.FirstEliteGuaranteed) && !Wins(roll, Rushed(ShotAtomTuning.EliteDropChance))) return false;
        Elites++;
        return Offer(at);
    }

    // A destroyed boss: two coins always, and a third half the time (`roll`, `roll2`, `roll3`: -1 rolls it).
    public static bool OnBossDestroyed(Vector3 at, float roll = -1f, float roll2 = -1f, float roll3 = -1f)
    {
        if (!Counts()) return false;
        Bosses++;
        DroneRewardPickup.Spawn(at);
        return true;
    }

    // An ordinary enemy kill (collisionDetection.AwardDestroyedTarget): a small chance, at most one per EnemyDropCooldown.
    public static bool OnEnemyKilled(GameObject target, float roll = -1f)
    {
        if (target == null || !target.CompareTag("Enimey")) return false;
        if (target.name.StartsWith("Boss", System.StringComparison.Ordinal) || target.TryGetComponent(out EliteShip _)) return false;
        var identity = target.GetComponent<EnemyIdentity>();
        if (identity != null && identity.Def != null ? identity.Def.IsHazard : PrefabName.Is(target, "mine")) return false;
        if (!Application.isPlaying && roll < 0f && EnemyRollOverride < 0f) return false;
        // The directional starter is the only free opening pickup. The scripted
        // TWIN / TWIN / ARC modifier sequence is earned one coin per enemy kill.
        if (Counts() && RunCoinsReleased + queue.Count < 3)
        {
            if (!Offer(target.transform.position)) return false;
            Enemies++; return true;
        }
        return RollEnemyDrop(target.transform.position, roll);
    }

    public static bool RollEnemyDrop(Vector3 at, float roll = -1f)
    {
        if (!Counts()) return false;
        // edit-mode tests never get a random coin out of an unrelated kill: only an explicit roll (or the override) can drop one there
        if (!Application.isPlaying && roll < 0f && EnemyRollOverride < 0f) return false;
        float now = Now;
        if (now - lastEnemyDrop < EnemyCooldown) return false;
        if (roll < 0f) roll = EnemyRollOverride >= 0f ? EnemyRollOverride : Random.value;
        if (!Wins(roll, EnemyChance)) return false;
        if (!Offer(at)) return false;
        lastEnemyDrop = now;
        Enemies++;
        return true;
    }

    static bool Wins(float roll, float chance) { return chance >= 1f || roll < chance; }

    static bool Offer(Vector3 at)
    {
        if (queue.Count >= ShotAtomTuning.MaxCoinsOnScreen) return false;
        queue.Add(at);
        Pump();
        return true;
    }

    // Releases the waiting coins the atom spacing allows (the spawner calls it every frame too).
    public static void Pump(float roll = -1f)
    {
        while (queue.Count > 0 && AtomSpacing.Ready)
        {
            Vector3 at = queue[0];
            queue.RemoveAt(0);
            ReleaseOne(at, roll);
        }
    }

    static void ReleaseOne(Vector3 at, float roll)
    {
        if (RunCoinsReleased >= 3)
        {
            float choice = roll >= 0f ? roll : Random.value;
            if (choice >= .8f)
            {
                DroneCorePickup.SpawnRandom(at, (choice - .8f) / .2f);
                RunCoinsReleased++; Released++; LastWasDust = false; AtomSpacing.Released();
                return;
            }
            roll = Mathf.Clamp01(choice / .8f);
        }
        ShotAtom atom = RunCoinsReleased < 2 ? ShotAtom.Twin : ShotAtom.Arc;
        bool opening = RunCoinsReleased < 3 && ShotAtoms.CanDrop(atom);
        if (!opening && !ShotAtoms.TryPick(roll >= 0f ? roll : Random.value, out atom))
        {
            // everything unlocked is maxed: dust instead
            DustDrops++;
            LastWasDust = true;
            Released++;
            AtomSpacing.Released();
            if (spawnGoodStuff.Current != null) spawnGoodStuff.Current.DropDust(at);
            return;
        }
        LastWasDust = false;
        LastAtom = atom;
        Released++;
        AtomSpacing.Released();
        ShotAtomPickup.Spawn(atom, at);
        RunCoinsReleased++;
        if (Spawned != null) Spawned(atom, at);
    }
}

// A coin in the lane (built in code, like the green atom: no prefab). Tag pickUp, the atom drift and homing of every atom
// (moveItemEnmInStrightLine + AtomSpin at 0 deg/s so the symbol stays upright), its own idle flipbook (PickupFlipbook).
public class ShotAtomPickup : MonoBehaviour
{
    public const string ObjectPrefix = "shotAtom_";
    public ShotAtom atom;

    public static string NameFor(ShotAtom atom) { return ObjectPrefix + ShotAtoms.Key(atom); }

    // The 196 px coin at 700 px/unit: the same 0.28 u as every atom (PickupArt.AtomVisualScale grows it in play).
    public const float Diameter = .28f;

    public static GameObject Spawn(ShotAtom atom, Vector3 at)
    {
        Vector2 half = Vector2.one * Diameter * .5f * PickupArt.AtomVisualScale;
        at = SpawnAboveCamera.PickupEntry(at, half.y);
        at = SpawnSpace.PickupSpot(at, half, -RailInset.PickupLaneHalf, RailInset.PickupLaneHalf);
        var go = Build(atom, at);
        SpawnFootprint.Attach(go, half, SpawnLayer.Pickup);
        return go;
    }

    public static GameObject Build(ShotAtom atom, Vector3 position)
    {
        var go = new GameObject(NameFor(atom));
        go.transform.position = position;
        go.tag = "pickUp";
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 8;
        var col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = Diameter * .5f;
        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        go.AddComponent<moveItemEnmInStrightLine>();
        go.AddComponent<ShotAtomPickup>().atom = atom;
        go.AddComponent<DronePickupPreview>();
        var spin = go.AddComponent<AtomSpin>();
        spin.degreesPerSecond = 0f;   // the border spins (the flipbook); the symbol stays still
        PickupFlipbook.AddTo(go, ShotAtoms.KindOf(atom));
        PickupGlow.Dress(go);
        return go;
    }
}
