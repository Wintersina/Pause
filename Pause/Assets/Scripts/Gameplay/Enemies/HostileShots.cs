using System.Collections.Generic;
using UnityEngine;

// A hostile projectile, as the shot-vs-shot rules see it (BossProjectile,
// EliteShot).
public interface IHostileShot
{
    bool ShotCollidable { get; }   // in play and solid (not a glob in the air)
    Vector2 ShotPosition { get; }
    float ShotRadius { get; }
    int ShotOwner { get; }         // who fired it (the boss's pool, the elite)
    float ShotAge { get; }         // seconds since it was fired
    int ShotMass { get; }          // HostileShots.Light / Heavy / Fixed
    Color ShotTint { get; }
    void ShotPop(Vector2 at);      // destroyed by another shot / a player shot
}

// PROJECTILE vs PROJECTILE.
//
//   * Two hostile shots that touch destroy each other with a small pop --
//     boss vs elite, elite vs elite, and a boss's (or an elite's) shots vs
//     its own from another volley (a ricochet coming back through the next
//     fan). Shots of one volley -- the same owner, fired less than
//     VolleyGap apart -- never collide: a fan or a ring is one authored
//     pattern and would otherwise erase itself at the muzzle.
//   * Mass: light shots (bolts, shards) and heavy ones (slag, siege shells)
//     -- equal masses both break, the heavier one survives a lighter one.
//     A landed resin pool is fixed: it swallows any shot that touches it.
//   * A live laser burns through every shot crossing it (bar its own
//     volley's) and is never stopped by one. Pools and lasers ignore each
//     other.
//   * An area hazard (IHostileZone: a jet, a wave band, a blast ring, a
//     strike column, a lash) does the same while it is live: it burns the
//     light and heavy shots crossing it (bar its own volley's), is never
//     stopped by one, and is not shot down by the player's weapons.
//   * A player weapon projectile (AttackProjectile) shoots down every
//     hostile shot it passes through and flies on: defensive play, no
//     score, its kills and its budget against the boss unchanged. Pools
//     and lasers are not shot down.
//
// The pooled shots register once when built and stay registered; Resolve()
// walks the registry (a few dozen entries) without allocating.
public static class HostileShots
{
    public const int Light = 1, Heavy = 2, Fixed = 3;
    public const float VolleyGap = .6f;

    static readonly List<IHostileShot> shots = new List<IHostileShot>(128);
    static readonly List<BossBeam> beams = new List<BossBeam>(8);
    static readonly List<IHostileZone> zones = new List<IHostileZone>(16);

    // Counters (tests, previews).
    public static int Clashes, Pops, BeamBurns, ShotDown, ZoneBurns;

    public static IReadOnlyList<IHostileShot> All => shots;

    public static void Register(IHostileShot s) { if (s != null && !shots.Contains(s)) shots.Add(s); }
    public static void Unregister(IHostileShot s) { shots.Remove(s); }
    public static void Register(BossBeam b) { if (b != null && !beams.Contains(b)) beams.Add(b); }
    public static void Unregister(BossBeam b) { beams.Remove(b); }
    public static void Register(IHostileZone z) { if (z != null && !zones.Contains(z)) zones.Add(z); }
    public static void Unregister(IHostileZone z) { zones.Remove(z); }
    public static IReadOnlyList<IHostileZone> Zones => zones;

    public static void ResetCounters() { Clashes = Pops = BeamBurns = ShotDown = ZoneBurns = 0; }

    static bool Dead(IHostileShot s) => s == null || (Object)s == null;

    public static bool SameVolley(int ownerA, float ageA, int ownerB, float ageB) =>
        ownerA == ownerB && Mathf.Abs(ageA - ageB) < VolleyGap;

    public static int ActiveCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < shots.Count; i++) if (!Dead(shots[i]) && shots[i].ShotCollidable) n++;
            return n;
        }
    }

    // Every touching pair and every shot crossing a live laser. Called at the
    // end of each pool's step (boss, elites); cheap to repeat.
    public static void Resolve()
    {
        if (DeathCrash.Running) return;   // a player death: the domino has the board
        for (int i = shots.Count - 1; i >= 0; i--) if (Dead(shots[i])) shots.RemoveAt(i);
        for (int i = beams.Count - 1; i >= 0; i--) if (beams[i] == null) beams.RemoveAt(i);
        for (int i = zones.Count - 1; i >= 0; i--) if (zones[i] == null || (zones[i] is Object zo && zo == null)) zones.RemoveAt(i);

        for (int i = 0; i < shots.Count; i++)
        {
            var a = shots[i];
            if (!a.ShotCollidable) continue;
            Vector2 pa = a.ShotPosition;
            float ra = a.ShotRadius;
            for (int j = i + 1; j < shots.Count && a.ShotCollidable; j++)
            {
                var b = shots[j];
                if (!b.ShotCollidable) continue;
                if (a.ShotMass == Fixed && b.ShotMass == Fixed) continue;
                if (SameVolley(a.ShotOwner, a.ShotAge, b.ShotOwner, b.ShotAge)) continue;
                Vector2 pb = b.ShotPosition;
                float r = ra + b.ShotRadius;
                if ((pa - pb).sqrMagnitude > r * r) continue;
                Clash(a, b, (pa + pb) * .5f);
            }
            if (!a.ShotCollidable || a.ShotMass == Fixed) continue;
            for (int k = 0; k < beams.Count; k++)
            {
                var beam = beams[k];
                if (!beam.Live || beam.Hitbox == null) continue;
                if (SameVolley(a.ShotOwner, a.ShotAge, beam.OwnerId, beam.LiveAge)) continue;
                if (!beam.Touches(pa, ra)) continue;
                BeamBurns++;
                Pop(a, pa);
                break;
            }
            if (!a.ShotCollidable) continue;
            for (int k = 0; k < zones.Count; k++)
            {
                var z = zones[k];
                if (!z.ZoneLive) continue;
                if (SameVolley(a.ShotOwner, a.ShotAge, z.ZoneOwner, z.ZoneAge)) continue;
                if (!z.ZoneTouches(pa, ra)) continue;
                ZoneBurns++;
                Pop(a, pa);
                break;
            }
        }
    }

    static void Clash(IHostileShot a, IHostileShot b, Vector2 at)
    {
        Clashes++;
        int ma = a.ShotMass, mb = b.ShotMass;
        if (ma <= mb) Pop(a, at);
        if (mb <= ma) Pop(b, at);
    }

    static void Pop(IHostileShot s, Vector2 at)
    {
        Pops++;
        HazardRuntime.PopFx(at, s.ShotTint);
        s.ShotPop(at);
    }

    // A player projectile's step from `from` to `to` (hit radius r): every
    // light or heavy hostile shot it passes through is shot down. Pays
    // nothing; the projectile flies on.
    public static int ShootDownAlong(Vector2 from, Vector2 to, float r)
    {
        int n = 0;
        for (int i = 0; i < shots.Count; i++)
        {
            var s = shots[i];
            if (Dead(s) || !s.ShotCollidable || s.ShotMass == Fixed) continue;
            Vector2 p = s.ShotPosition;
            float reach = r + s.ShotRadius;
            if (SegmentDistanceSq(from, to, p) > reach * reach) continue;
            ShotDown++;
            n++;
            Pop(s, p);
        }
        return n;
    }

    public static float SegmentDistanceSq(Vector2 a, Vector2 b, Vector2 p)
    {
        Vector2 ab = b - a;
        float len = ab.sqrMagnitude;
        float t = len > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len) : 0f;
        return (a + ab * t - p).sqrMagnitude;
    }
}
