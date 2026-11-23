using System.Collections.Generic;
using UnityEngine;

// SHOT ATOMS: six permanent, per-run upgrades to every ship's NORMAL gun only (the floaties' small bullets, FloatieGuns.cs).
// Ultimates, special attacks, secret powers and the floaties themselves are untouched.
//
//   TWIN      more shots a volley: 2 parallel, then 3 in a narrow fan, then 4
//   PIERCE    a shot passes through +1 enemy a level
//   SPLIT     on a hit the shot breaks into 2 / 3 / 4 small diagonal shards
//   RICOCHET  shots bounce off the screen's side edges 1 / 2 / 3 times
//   ARC       a hit zaps a spark to the nearest other enemy in range (1 / 1 / 2 jumps)
//   RAPID     the gun fires +20% / +45% / +75% faster
//
// Each catch adds a level (max 3); a catch past the max pays star dust instead. They drop from bosses (one always, a second half the time),
// elites (the first of a run always, later ones 80%) and, rarely, ordinary kills (1.5%, at most one per 20 s), one random atom that is not maxed yet (every atom can drop in every world and reach level 3 in any of them).
// All of it is state of the run: BeginRun (score.Awake) wipes it.
//
// EVERY number lives in ShotAtomTuning.
public enum ShotAtom { Twin = 0, Pierce = 1, Split = 2, Ricochet = 3, Arc = 4, Rapid = 5 }

public static class ShotAtomTuning
{
    public const int Count = 6;
    public const int MaxLevel = 3;

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

    // ---- TWIN: bullets in a volley by level (index = level), their angles (degrees off the gun's heading) and
    // sideways offsets (world units), and the damage of each (the total is never a straight multiple: 1.5 / 1.8 / 2.0 of one shot) ----
    public static readonly int[] TwinShots = { 1, 2, 3, 4 };
    public static readonly float[] TwinDamage = { 1f, .75f, .6f, .5f };
    public const float TwinParallelGap = .16f;
    public static readonly float[][] TwinAngles = { new[] { 0f }, new[] { 0f, 0f }, new[] { -8f, 0f, 8f }, new[] { -12f, -4f, 4f, 12f } };
    public static readonly float[][] TwinOffsets = { new[] { 0f }, new[] { -TwinParallelGap * .5f, TwinParallelGap * .5f }, new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f, 0f } };

    // ---- PIERCE: enemies a shot passes through, by level ----
    public static readonly int[] PierceThrough = { 0, 1, 2, 3 };

    // ---- SPLIT: shards by level, their angles off the shot's heading, speed, life and damage (of a whole shot) ----
    public static readonly int[] SplitShards = { 0, 2, 3, 4 };
    public static readonly float[][] SplitAngles = { new float[0], new[] { -45f, 45f }, new[] { -60f, 0f, 60f }, new[] { -60f, -20f, 20f, 60f } };
    public const float ShardSpeed = 7f, ShardLife = .5f, ShardDamage = .4f;

    // ---- RICOCHET: side-edge bounces by level; a near-straight shot leans a little so a bounce can happen at all ----
    public static readonly int[] Bounces = { 0, 1, 2, 3 };
    public const float RicochetLeanDegrees = 14f, RicochetStraightWithin = 3f;

    // ---- ARC: spark range (world units), damage (of a whole shot) and jumps by level ----
    public static readonly float[] ArcRange = { 0f, 2f, 2.6f, 3.2f };
    public static readonly float[] ArcDamage = { 0f, .5f, .6f, .7f };
    public static readonly int[] ArcJumps = { 0, 1, 1, 2 };

    // ---- RAPID: the gun's fire-rate bonus by level (cooldown = base / (1 + bonus)) ----
    public static readonly float[] RapidBonus = { 0f, .20f, .45f, .75f };

    // ---- the cap on the gun's bullets in the air (the floaties' own pool; AttackPool.MaxProjectiles still caps the total) ----
    public const int MaxBullets = 24;

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

    // ---- drops: every atom can drop in every world, until it is at level 3 ----
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

    // A random atom that is not maxed yet (`roll01` in 0..1 picks which); false when all six are at level 3 (a drop pays dust then).
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

    // A coin was caught. Already at level 3 (a second coin of the same atom was in the air when the first was caught): the catch
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

    public static readonly string[] Roman = { "", "I", "II", "III" };

    public static string Name(ShotAtom atom) { return atom.ToString().ToUpperInvariant(); }

    // "TWIN II"
    public static string Callout(ShotAtom atom, int level)
    {
        return Name(atom) + " " + Roman[Mathf.Clamp(level, 0, ShotAtomTuning.MaxLevel)];
    }

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
    // A cooldown after RAPID.
    public static float Rapid(float seconds) { return seconds / (1f + RapidBonus()); }

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
