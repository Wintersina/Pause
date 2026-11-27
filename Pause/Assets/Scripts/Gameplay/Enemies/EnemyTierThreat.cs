using System.Collections.Generic;
using UnityEngine;

// TIERED ENEMIES FIGHT BACK. An enemy that spawned with a blue or crystal bar (HeartTiers.cs) is more dangerous, not only tankier.
// The tier is rolled once at spawn (EnemyHearts.Tier); Toughness.Attach then hands it to the enemy's EnemyBrain, which swaps its
// behaviour for a TUNED COPY of the table's row (Tuned, built once per key and tier, never per frame). Red-only keeps the table's own
// behaviour object, so its numbers are exactly today's.
//
//   tier      fire interval   shot speed   dive / run speed   extra
//   red       x1              x1           x1                 nothing (exactly as before)
//   blue      x.75            x1.1         x1.1               nothing
//   crystal   x.55            x1.2         x1.2               +1 shot (a small spread) on a Shot / Cross attack of up to 3 shots;
//                                                             an enemy that never shoots gets one simple shot (Mites, Gnats, Jellies...)
//
// Per behaviour: Shot / Cross: interval, speed, and at crystal one extra shot (spread >= ExtraSpreadDegrees, so a lone aimed shot becomes
// a pair). Ring / Lob / area hazards (Blast, Strike, Jet, Wave, Lash): interval only (their pattern is already dense; the speed row
// applies to the ring's shots). Lunge: the dive is faster (shorter lunge time, faster attack run). No attack at all: crystal only, one
// slow straight-down shot of the world's own kind. The shots keep the world's material: they are fired through the same pooled
// EliteShots with the behaviour's own shot kind and the world's ShotSkins / cue colours (Space neon-pink, Frost ice shards, Verdant
// spores, Ember embers, Tide droplets, Storm sparks, Desert sand): NO new art, and pink only in Space (EnemyBehaviours.ShotColor).
// The extra shots are reserved in the roster shot budget with the rest of the volley (EnemyThreat.TryReserveVolley), so the live
// total can never pass EnemyThreat.ShotBudget (12 + loop / portal bonuses, capped by PortalPressure.ShotCap).
public static class EnemyTierThreat
{
    // ---- THE TABLE (index = tier 0 red, 1 blue, 2 crystal) ----
    public static readonly float[] IntervalMul = { 1f, .75f, .55f };   // x the cooldown and the first delay
    public static readonly float[] SpeedMul = { 1f, 1.1f, 1.2f };      // x shot speed, the dive and the run
    public static readonly float[] LungeMul = { 1f, .9f, .75f };       // x the lunge seconds (smaller = faster)
    public static readonly int[] ExtraShots = { 0, 0, 1 };

    // ---- caps ----
    public const float MinIntervalMul = .5f, MaxSpeedMul = 1.3f;       // no row may go past these
    public const float MinCooldownSeconds = .8f;                        // a tuned cooldown never drops under this (or under the table's own)
    public const int MaxFanForExtra = 3;                                // only fans of up to this many get the extra shot
    public const float ExtraSpreadDegrees = 14f;
    // the simple shot of an enemy that never fired (crystal only)
    public const float SimpleSpeed = 2.2f, SimpleSize = .16f, SimpleTell = .7f, SimpleCooldown = 3.4f, SimpleFirstDelay = 1.2f;
    public const int SimpleVolleys = 2;

    // ---- LOOPS (user: loop 2..n is exponentially harder). Loop 1 (index 0) is exactly the table above. After it every tiered
    // enemy's interval shrinks and speed grows with LoopFactor, up to the hard caps below. (LoopFactor is LoopDifficulty.For(index): 1 on loop 1.) Red-only enemies never scale. ----
    public const float LoopMinIntervalMul = .4f;        // hard floor of a fire interval multiplier at any loop
    public const float LoopMaxSpeedMul = 1.45f;         // hard ceiling of a speed multiplier at any loop
    public const int LoopCap = 20;                      // loop indices above this stop growing the numbers
    public static int LoopOverride = -1;                // tests: stand in for RunLoop.Index
    public static int LoopIndex { get { return Mathf.Clamp(LoopOverride >= 0 ? LoopOverride : RunLoop.Index, 0, LoopCap); } }
    public static float LoopFactor(int loopIndex) { return LoopDifficulty.For(Mathf.Clamp(loopIndex, 0, LoopCap)); }   // (the shared exponential loop factor; this file keeps its own caps)

    public static float Interval(int tier, int loopIndex = 0)
    {
        tier = Mathf.Clamp(tier, 0, 2);
        if (tier == 0) return 1f;
        float m = Mathf.Max(MinIntervalMul, IntervalMul[tier]);
        if (loopIndex <= 0) return m;
        return Mathf.Max(LoopMinIntervalMul, m / LoopFactor(loopIndex));
    }
    public static float Speed(int tier, int loopIndex = 0)
    {
        tier = Mathf.Clamp(tier, 0, 2);
        if (tier == 0) return 1f;
        float m = Mathf.Min(MaxSpeedMul, SpeedMul[tier]);
        if (loopIndex <= 0) return m;
        return Mathf.Min(LoopMaxSpeedMul, m * Mathf.Sqrt(LoopFactor(loopIndex)));
    }

    // The world's own shot kind for a simple shot: ice shards (Frost), droplets (Tide), sand (Desert) are shards; the rest are bolts
    // (neon Space, spores Verdant, embers Ember, sparks Storm: their skin makes the look, ShotSkins).
    public static EliteShots.Kind SimpleKind(int world)
    {
        return world == 1 || world == 4 || world == 6 ? EliteShots.Kind.Shard : EliteShots.Kind.Bolt;
    }

    // Can this enemy be given the crystal simple shot (it has no attack of its own and flies as a pilot)?
    public static bool GetsSimpleShot(EnemyDef def, EnemyBehaviour b)
    {
        return def != null && b != null && !b.Attacks && b.IsPilot && def.role != EnemyRole.Chaser && def.role != EnemyRole.Rock && def.role != EnemyRole.Mine;
    }

    static readonly Dictionary<long, EnemyBehaviour> cache = new Dictionary<long, EnemyBehaviour>();

    // The behaviour this enemy flies with at `tier`: the table's own at red; otherwise a tuned copy (one per row and tier, cached).
    public static EnemyBehaviour Tuned(EnemyDef def, EnemyBehaviour b, int tier, int loopIndex = 0)
    {
        loopIndex = Mathf.Clamp(loopIndex, 0, LoopCap);
        tier = Mathf.Clamp(tier, 0, 2);
        if (b == null || tier == 0) return b;
        if (b.IsPilot == false && b.attack == EnemyAttack.Laser) return b;   // (a mine never has hearts)
        long id = ((long)b.key.GetHashCode() << 8) | (long)(loopIndex << 2) | (uint)tier;
        EnemyBehaviour tuned;
        if (cache.TryGetValue(id, out tuned) && tuned.key == b.key && tuned.basis == b) return tuned;
        tuned = Build(def, b, tier, loopIndex);
        tuned.basis = b;
        cache[id] = tuned;
        return tuned;
    }

    public static void ClearCache() { cache.Clear(); }

    static EnemyBehaviour Build(EnemyDef def, EnemyBehaviour b, int tier, int loopIndex)
    {
        var t = b.Clone();
        if (tier == 2 && GetsSimpleShot(def, b))
        {
            int world = def != null ? def.world : 0;
            t.attack = EnemyAttack.Shot; t.shotKind = SimpleKind(world); t.shotCount = 1; t.shotSpread = 0f; t.shotGap = 0f;
            t.shotSpeed = SimpleSpeed; t.shotSize = SimpleSize; t.aimCone = 0f; t.muzzle = .3f;
            t.tell = SimpleTell; t.cooldown = SimpleCooldown; t.firstDelay = SimpleFirstDelay; t.maxVolleys = SimpleVolleys; t.armedChance = 1f;
        }
        float interval = Interval(tier, loopIndex), speed = Speed(tier, loopIndex);
        if (b.Attacks)
        {
            t.cooldown = Mathf.Max(b.cooldown * interval, Mathf.Min(b.cooldown, MinCooldownSeconds));
            t.firstDelay = b.firstDelay * interval;
            t.shotSpeed = b.shotSpeed * speed;
            if (b.attack == EnemyAttack.Lunge)
            {
                t.lungeSeconds = Mathf.Max(.1f, b.lungeSeconds * LungeMul[tier]);
                t.shotSpeed = b.shotSpeed;
            }
            if ((b.attack == EnemyAttack.Shot || b.attack == EnemyAttack.Cross) && b.shotCount <= MaxFanForExtra)
            {
                int extra = ExtraShots[tier];
                if (extra > 0)
                {
                    t.shotCount = b.shotCount + extra;
                    t.shotSpread = Mathf.Max(b.shotSpread, ExtraSpreadDegrees);
                }
            }
        }
        t.runSpeed = b.runSpeed * speed;   // a pilot that leaves on an attack run runs faster
        t.entrySpeed = b.entrySpeed * speed;
        return t;
    }
}
