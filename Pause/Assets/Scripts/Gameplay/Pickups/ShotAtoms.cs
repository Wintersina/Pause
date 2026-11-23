using System.Collections.Generic;
using UnityEngine;

// SHOT ATOMS: six permanent, per-run upgrades to every ship's NORMAL gun only (the floaties' small bullets, FloatieGuns.cs).
// Ultimates, special attacks, secret powers and the floaties themselves are untouched.
//
//   TWIN      more shots a volley: 2 parallel, then a fan of 3 .. 7 (it widens a little with the level)
//   PIERCE    a shot passes through +1 enemy a level (up to 10)
//   SPLIT     on a hit the shot breaks into 2 .. 6 small diagonal shards (40% .. 65% damage each)
//   RICOCHET  shots bounce off the screen's side edges 1 .. 10 times
//   ARC       a hit zaps a spark to the nearest other enemies in range (1 .. 6 jumps, 2.0 .. 4.0 range, 50% .. 85% damage)
//   RAPID     the gun fires +20% .. +160% faster (never under ShotAtomTuning.MinCooldown between shots)
//
// Each catch adds a level (max 10; PowerCreep scales the enemy spawns with the sum of the levels); a catch past the max pays star dust instead. They drop from bosses (one always, a second half the time),
// elites (the first of a run always, later ones 80%) and, rarely, ordinary kills (1.5%, at most one per 20 s), one random atom that is not maxed yet (every atom can drop in every world and reach the top level in any of them).
// All of it is state of the run: BeginRun (score.Awake) wipes it.
//
// EVERY number lives in ShotAtomTuning.
public enum ShotAtom { Twin = 0, Pierce = 1, Split = 2, Ricochet = 3, Arc = 4, Rapid = 5 }

public static class ShotAtomTuning
{
    public const int Count = 6;
    public const int MaxLevel = 10;

    // ---- drops ----
    public const float EliteDropChance = .8f;     // a killed elite (was .5)
    public const float BossDropChance = 1f;       // a destroyed boss: the first drop
    public const float BossSecondDropChance = .5f; // ...and a second one this often
    public const float EnemyDropChance = .015f;   // an ordinary enemy kill (not a hazard, elite or boss)
    public const float EnemyDropCooldown = 20f;   // s: at most one drop from ordinary enemies per this (the atom spacing still applies)
    // ATOM RUSH booster (one run): every chance x RushMultiplier (capped at 1), the ordinary-enemy cooldown x RushCooldownScale.
    public const float RushMultiplier = 3f;       // +200%
    public const float RushCooldownScale = .5f;
    public const bool FirstEliteGuaranteed = true; // the first elite killed in a run always drops one (later ones use EliteDropChance)
    public const float MaxedDust = 2f;            // star dust a catch pays once the atom is at its level cap (the blue atom's)
    public const int MaxCoinsOnScreen = 3;        // coins waiting for the atom spacing at once, at most

    // Every table below is indexed by LEVEL (0..MaxLevel); level 0 is "no atom" (the one plain bullet).

    // ---- TWIN: bullets in a volley by level, the half-width of their fan (degrees off the gun's heading; the shots are spread evenly across
    // it, so it widens a little with every level) and the damage of each. The volley's worth grows ~11-13% a level from level 3 on
    // (shots x damage: 1.5 / 1.8 / 2.0 / 2.24 / 2.5 / 2.8 / 3.12 / 3.54 / 3.92 / 4.41 of one shot), never as fast as the shot count. ----
    public static readonly int[] TwinShots = { 1, 2, 3, 4, 4, 5, 5, 6, 6, 7, 7 };
    public static readonly float[] TwinDamage = { 1f, .75f, .6f, .5f, .56f, .5f, .56f, .52f, .59f, .56f, .63f };
    public const float TwinParallelGap = .16f;
    public static readonly float[] TwinHalfFan = { 0f, 0f, 8f, 12f, 13f, 15f, 16f, 18f, 19f, 20f, 21f };
    public static readonly float[][] TwinAngles = Fans(TwinShots, TwinHalfFan);
    public static readonly float[][] TwinOffsets = TwinOffsetsFor(TwinShots);

    // ---- PIERCE: enemies a shot passes through, by level ----
    public static readonly int[] PierceThrough = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
    public const int MaxPierce = 10;

    // ---- SPLIT: shards by level, the half-width of their spread off the shot's heading, speed, life and the damage of each (of a whole shot) ----
    public static readonly int[] SplitShards = { 0, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6 };
    public static readonly float[] SplitHalfFan = { 0f, 45f, 45f, 60f, 60f, 60f, 60f, 72f, 72f, 75f, 75f };
    public static readonly float[][] SplitAngles = Fans(SplitShards, SplitHalfFan);
    public static readonly float[] SplitDamage = { 0f, .40f, .42f, .44f, .47f, .50f, .53f, .56f, .59f, .62f, .65f };
    public const float ShardSpeed = 7f, ShardLife = .5f;

    // ---- RICOCHET: side-edge bounces by level; a near-straight shot leans a little so a bounce can happen at all.
    // The bullet's life grows a little with its bounces so they can be used, but never past RicochetLifeCap (0: the plain MaxAge). ----
    public static readonly int[] Bounces = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
    public const float RicochetLeanDegrees = 14f, RicochetStraightWithin = 3f;
    public const float RicochetLifeCap = 3.6f;
    public static readonly float[] RicochetLife = { 0f, 2.52f, 2.64f, 2.76f, 2.88f, 3f, 3.12f, 3.24f, 3.36f, 3.48f, RicochetLifeCap };

    // ---- ARC: spark range (world units), damage (of a whole shot) and jumps by level ----
    public static readonly float[] ArcRange = { 0f, 2f, 2.22f, 2.44f, 2.67f, 2.89f, 3.11f, 3.33f, 3.56f, 3.78f, 4f };
    public static readonly float[] ArcDamage = { 0f, .50f, .54f, .58f, .62f, .65f, .69f, .73f, .77f, .81f, .85f };
    public static readonly int[] ArcJumps = { 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 6 };
    public const int MaxArcJumps = 6;

    // ---- RAPID: the gun's fire-rate bonus by level (cooldown = base / (1 + bonus), never under MinCooldown) ----
    public static readonly float[] RapidBonus = { 0f, .20f, .35f, .50f, .65f, .80f, .95f, 1.10f, 1.25f, 1.40f, 1.60f };
    public const float MinCooldown = .5f;   // s: the floor of any floatie's cooldown, so the gun never becomes a laser stream

    // ---- the cap on the gun's bullets in the air (the floaties' own pool; AttackPool.MaxProjectiles still caps the total).
    // A 7-shot volley from three floaties (21 bullets) plus a few shards needs more than the old 24; 32 stays well under the shared 40. ----
    public const int MaxBullets = 32;

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
    static readonly int[] levels = new int[ShotAtomTuning.Count];
    static readonly List<ShotAtom> order = new List<ShotAtom>(ShotAtomTuning.Count);

    // Bumped on every change (the HUD row redraws on it).
    public static int Version { get; private set; }
    public static int Catches { get; private set; }       // coins collected this run
    public static int MaxedCatches { get; private set; }  // ...that paid dust

    public static void BeginRun()
    {
        for (int i = 0; i < levels.Length; i++) levels[i] = 0;
        order.Clear();
        Catches = 0;
        MaxedCatches = 0;
        ShotAtomDrops.Clear();
        ShotAtomDrops.NewRun();
        Version++;
    }

    public static int Level(ShotAtom atom) { return levels[(int)atom]; }
    public static int OrderCount { get { return order.Count; } }
    public static ShotAtom OrderAt(int i) { return order[i]; }
    public static void SetLevel(ShotAtom atom, int level)   // tests and previews
    {
        level = Mathf.Clamp(level, 0, ShotAtomTuning.MaxLevel);
        levels[(int)atom] = level;
        if (level > 0 && !order.Contains(atom)) order.Add(atom);
        if (level == 0) order.Remove(atom);
        Version++;
    }

    // ---- drops: every atom can drop in every world, until it is at the top level ----
    public static bool CanDrop(ShotAtom atom)
    {
        return Level(atom) < ShotAtomTuning.MaxLevel;
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
        public int level;        // the level after the catch (the cap when it was already there)
        public bool maxed;       // already at the cap: the catch pays dust, no level
    }

    // A coin was caught. Already maxed (a second coin of the same atom was in the air when the first was caught): the catch
    // pays star dust, no level.
    public static Catch Collect(ShotAtom atom)
    {
        Catches++;
        var c = new Catch { atom = atom, level = levels[(int)atom] };
        if (c.level >= ShotAtomTuning.MaxLevel) { c.maxed = true; MaxedCatches++; Version++; return c; }
        c.level = ++levels[(int)atom];
        if (!order.Contains(atom)) order.Add(atom);
        Version++;
        return c;
    }

    public const string MaxedWord = "MAXED +DUST";

    public static readonly string[] Roman = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };

    // PowerLevel: the sum of every atom's level (0 .. Count * MaxLevel = 60). PowerCreep turns it into spawn pressure.
    public const int MaxPower = ShotAtomTuning.Count * ShotAtomTuning.MaxLevel;
    public static int PowerLevel
    {
        get
        {
            int sum = 0;
            for (int i = 0; i < levels.Length; i++) sum += levels[i];
            return sum;
        }
    }

    public static string Name(ShotAtom atom) { return atom.ToString().ToUpperInvariant(); }

    // "TWIN II"
    public static string Callout(ShotAtom atom, int level)
    {
        return Name(atom) + " " + Roman[Mathf.Clamp(level, 0, ShotAtomTuning.MaxLevel)];
    }

    // ---- the catch popup (ScoreHud.ShowAtomUpgrade): a title and a one-line effect hint ----
    public const float UpgradeSeconds = 1.8f;
    public const int UpgradeTitleSize = 40;

    // "TWIN SHOT  LV 2", "RAPID FIRE  LV 3"
    public static string UpgradeTitle(ShotAtom atom, int level)
    {
        level = Mathf.Clamp(level, 1, ShotAtomTuning.MaxLevel);
        return (atom == ShotAtom.Rapid ? "RAPID FIRE" : Name(atom) + " SHOT") + "  LV " + level;
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

    public static string MaxedHint() { return "already at level " + ShotAtomTuning.MaxLevel; }

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
    public static void NewRun() { FirstEliteDone = false; lastEnemyDrop = -9999f; }
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
    public static float EnemyChance { get { return Rushed(ShotAtomTuning.EnemyDropChance); } }
    public static float EnemyCooldown { get { return Boosters.RunRushActive ? ShotAtomTuning.EnemyDropCooldown * ShotAtomTuning.RushCooldownScale : ShotAtomTuning.EnemyDropCooldown; } }

    // A killed elite: a coin 80% of the time, the run's first always (`roll` 0..1: below the chance drops; -1 rolls it).
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

    // A destroyed boss: always one, and a second half the time (`roll2`: -1 rolls it).
    public static bool OnBossDestroyed(Vector3 at, float roll = -1f, float roll2 = -1f)
    {
        if (!Counts()) return false;
        if (roll < 0f) roll = Random.value;
        if (roll2 < 0f) roll2 = Random.value;
        if (!Wins(roll, ShotAtomTuning.BossDropChance)) return false;
        Bosses++;
        bool ok = Offer(at);
        if (Wins(roll2, Rushed(ShotAtomTuning.BossSecondDropChance)))
        {
            Bosses++;
            Offer(at);
        }
        return ok;
    }

    // An ordinary enemy kill (collisionDetection.AwardDestroyedTarget): a small chance, at most one per EnemyDropCooldown.
    public static bool OnEnemyKilled(GameObject target, float roll = -1f)
    {
        if (target == null || !target.CompareTag("Enimey")) return false;
        if (target.name.StartsWith("Boss", System.StringComparison.Ordinal) || target.TryGetComponent(out EliteShip _)) return false;
        var identity = target.GetComponent<EnemyIdentity>();
        if (identity != null && identity.Def != null ? identity.Def.IsHazard : PrefabName.Is(target, "mine")) return false;
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
        ShotAtom atom;
        if (!ShotAtoms.TryPick(roll >= 0f ? roll : Random.value, out atom))
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
        var spin = go.AddComponent<AtomSpin>();
        spin.degreesPerSecond = 0f;   // the border spins (the flipbook); the symbol stays still
        PickupFlipbook.AddTo(go, ShotAtoms.KindOf(atom));
        PickupGlow.Dress(go);
        return go;
    }
}
