using System.Collections.Generic;
using UnityEngine;

// COVER BOULDERS: the world-level object and every rule that touches it (docs/cover-boulders.md).
//
// A huge indestructible boulder drifts slowly down the lane now and then (CoverBoulderDirector spawns it, the table
// CoverBoulderTable says how). It is SOLID:
//   * the player's ship is blocked by it (ResolveShip: slides round it, no damage, never trapped: section 7);
//   * shots, beams and area hazards die on its face or are cut by it (the hooks listed in section 4 call the queries here);
//   * enemy bodies are pushed out of it every frame (PushBodies), a shoved body crashes on it (ShoveMeet);
//   * nothing damages it; only a boss arriving destroys it (OnBossBegin) and no boulder spawns while a boss is up.
//
// Everything is pooled (Slots boulders and one CoverFx, built once by Prewarm) and steps on the world's clock.
public static class CoverBoulders
{
    // ---- tunables (the per-world numbers are CoverBoulderTable) ----
    public const int Slots = 2;                  // pool size; MaxAlive 1 on screen, the 2nd slot is a burst / fade still playing
    public const int MaxAlive = 1;
    public const float FirstSeconds = 20f;       // none in the first 20 s of a world (the 8 s calm start is inside it)
    public const float BossLeadSeconds = 14f;    // none in the run-up to a boss
    public const float SideGap = 1.0f;           // the circles stay this far off the drawn rail face on BOTH sides: a hull (0.78 u) always fits beside it (section 7)
    public const float SpawnMargin = .4f;        // enters this far above the view
    public const float HullShare = 1f;           // the ship's blocking radius = ShipScale.HullHalfWidth x this
    public const float BodyShare = .85f;         // a body's blocking radius = its measured radius x this
    public const float ThudVolume = .16f, ThudGap = .18f, RumbleVolume = .35f;
    public const float KickBlock = 0f, KickBurst = .05f;
    public const float MaxScreenShare = .62f;    // a boulder is at most this share of the rail-to-rail width wide

    // test / tool switches
    public static bool Disabled;                 // (a scene without boulders)
    public static bool TestAnyWorld;             // spawn in worlds whose art is not wired (they borrow Space's)

    // ---- counters (tests, previews) ----
    public static int Spawned, ShotsBlocked, PlayerShotsBlocked, LosBlocked, BodiesPushed, CrashKills, Destroyed, ShipPushes, Thuds;

    // ---- the pool ----
    static readonly CoverBoulder[] pool = new CoverBoulder[Slots];
    static CoverFx fx;
    static GameObject root;
    static readonly StepContext ctx = new StepContext();
    static float lastThud = -9f;
    static float simClock;

    public sealed class StepContext
    {
        public float viewBottom;
        public CoverFx fx;
        public void OnBurst(CoverBoulder b) { CoverBoulders.Burst(b); }
    }

    public static Transform Root
    {
        get
        {
            if (root == null) { root = new GameObject("~CoverBoulders"); pool[0] = pool[1] = null; fx = null; }
            return root.transform;
        }
    }

    // Builds the pool and the chips (once); the director calls it at scene start so nothing is made mid-run.
    public static void Prewarm()
    {
        var r = Root;
        for (int i = 0; i < pool.Length; i++)
            if (pool[i] == null) { pool[i] = new CoverBoulder(); pool[i].Build(r, "Boulder" + i); }
        if (fx == null) fx = new CoverFx(r);
        ctx.fx = fx;
    }

    public static CoverFx Fx { get { Prewarm(); return fx; } }

    public static IEnumerable<CoverBoulder> All
    {
        get { for (int i = 0; i < pool.Length; i++) if (pool[i] != null && pool[i].Alive) yield return pool[i]; }
    }

    public static int Alive
    {
        get { int n = 0; for (int i = 0; i < pool.Length; i++) if (pool[i] != null && pool[i].Alive) n++; return n; }
    }

    public static int SolidCount
    {
        get { int n = 0; for (int i = 0; i < pool.Length; i++) if (pool[i] != null && pool[i].Solid) n++; return n; }
    }

    // Is anything solid at all (the one cheap test every hook starts with)?
    public static bool Any
    {
        get { for (int i = 0; i < pool.Length; i++) if (pool[i] != null && pool[i].Solid) return true; return false; }
    }

    public static CoverBoulder First
    {
        get { for (int i = 0; i < pool.Length; i++) if (pool[i] != null && pool[i].Alive) return pool[i]; return null; }
    }

    // ---- life ----

    public static CoverBoulder Spawn(int world, int variant)
    {
        Prewarm();
        CoverBoulder slot = null;
        for (int i = 0; i < pool.Length; i++) if (!pool[i].Alive) { slot = pool[i]; break; }
        if (slot == null) return null;
        var e = CoverBoulderTable.For(world);
        var art = CoverBoulderArt.For(world);
        if (art == null || art.Length == 0) return null;
        variant = Mathf.Clamp(variant, 0, Mathf.Min(art.Length, e.Variants) - 1);
        var v = art[variant];
        float boundPx = 0f;
        for (int i = 0; i < v.circles.Length && i < CoverBoulder.MaxCircles; i++)
            boundPx = Mathf.Max(boundPx, new Vector2(v.circles[i].x, v.circles[i].y).magnitude + v.circles[i].r);
        // width: the table's, shrunk on a narrow lane so the free-reach rule can always be met (ScreenFit)
        float width = e.widths[variant];
        width *= FitScale(width, boundPx / v.artWidth);
        float bound = boundPx * width / v.artWidth;
        int side = Random.value < .5f ? -1 : 1;
        float lo, hi;
        Band(side, bound, out lo, out hi);
        float x = Mathf.Lerp(lo, hi, Random.value);
        float y = CameraFit.ViewTop + bound + SpawnMargin;
        var vel = new Vector2(Random.Range(-e.side, e.side), -Random.Range(e.fall.x, e.fall.y));
        slot.Spawn(world, variant, v, width, new Vector2(x, y), vel, side, Random.value, e.TintFor(CoverBoulderDirector.BackdropVariant(world)));
        Spawned++;
        return slot;
    }

    public static float LaneWidthLimit => Edge * 2f * MaxScreenShare;

    public static void Clear()
    {
        for (int i = 0; i < pool.Length; i++) if (pool[i] != null) pool[i].Release();
        if (fx != null) fx.Clear();
    }

    // Everything gone and the objects with it (scene change, tests).
    public static void Dispose()
    {
        for (int i = 0; i < pool.Length; i++) pool[i] = null;
        fx = null;
        if (root != null) BossUtil.Kill(root);
        root = null;
        lastShipSet = false;
        CoverBoulderArt.Forget();
    }

    // The quiet exit of every boulder (a world change, a death, a transition).
    public static void DismissAll()
    {
        for (int i = 0; i < pool.Length; i++) if (pool[i] != null) pool[i].Dismiss();
    }

    // A BOSS ARRIVES (BossEncounter.Setup): every boulder breaks (section 8).
    public static int OnBossBegin()
    {
        int n = 0;
        for (int i = 0; i < pool.Length; i++)
            if (pool[i] != null && pool[i].State == CoverBoulder.Phase.Live) { pool[i].Destroy(); n++; }
        if (n > 0)
        {
            Destroyed += n;
            PlayRumble();
        }
        return n;
    }

    static void PlayRumble()
    {
        if (!(Application.isPlaying || EnemyDeathAudio.Simulate)) return;
        EnemyDeathAudio.PlayAuthored(ShieldShockwave.SoundKey, RumbleVolume);
        CameraKick.Kick(CameraKick.MaxAmplitude * .35f);
    }

    // The burst frame: debris out of the face, the existing rock explosion.
    internal static void Burst(CoverBoulder b)
    {
        if (fx != null)
        {
            var e = b.Entry;
            for (int i = 0; i < b.Count; i++)
                fx.Burst(b.CircleCentre(i), Vector2.up, 1f, 18 / Mathf.Max(1, b.Count) + 1, 1.2f, 3.4f, .9f, e.debris, e.chip2);
        }
        if (Application.isPlaying || EnemyDeathAudio.Simulate)
        {
            collisionDetection.PlayExplosion();
            CameraKick.Kick(KickBurst);
        }
    }

    // ---- stepping ----

    // One running frame of world time.
    public static void Step(float dt)
    {
        if (dt <= 0f) return;
        simClock += dt;
        ctx.viewBottom = CameraFit.ViewBottom;
        ctx.fx = fx;
        for (int i = 0; i < pool.Length; i++) if (pool[i] != null && pool[i].Alive) pool[i].Step(dt, ctx);
        if (fx != null) fx.Step(dt);
    }

    // ---- geometry of the lane (section 7) ----

    public static float Reach => ShipReach.HalfWidth;                // the ship's centre range is +- this
    public static float Edge => BossRails.DrawnInnerEdge;            // the rails' drawn inner face
    public static float HullRadius => ShipScale.HullHalfWidth * HullShare;

    // The x range a boulder of `bound` radius may have its centre in: both of its sides keep SideGap between its circles and the drawn rail face, so the
    // ship (0.78 u wide) fits beside it whichever way it sits, and a ship at a rail is never squeezed against it. (`side` only picks the way it first drifts;
    // lo <= hi always.)
    public static void Band(int side, float bound, out float lo, out float hi)
    {
        Band(side, bound, Reach, HullRadius, Edge, out lo, out hi);
    }

    public static void Band(int side, float bound, float h, float hull, float edge, out float lo, out float hi)
    {
        float m = Mathf.Max(0f, edge - SideGap - bound);
        lo = -m; hi = m;
    }

    // The width factor (<= 1) that fits a boulder of `widthU` (bounding radius `boundOverWidth` x its width) in the lane: at most MaxScreenShare of the
    // rail-to-rail width, and small enough that its centre keeps .25 u to move in between its two SideGaps.
    public static float FitScale(float widthU, float boundOverWidth)
    {
        return FitScale(widthU, boundOverWidth, Reach, HullRadius, Edge);
    }

    public static float FitScale(float widthU, float boundOverWidth, float h, float hull, float edge)
    {
        float limit = edge * 2f * MaxScreenShare;
        float k = widthU > limit ? limit / widthU : 1f;
        float room = edge - SideGap - .25f;   // the bounding radius must fit in this
        float bound = boundOverWidth * widthU * k;
        if (bound > room && bound > 0f) k *= room / bound;
        return Mathf.Clamp(k, .5f, 1f);
    }

    // The smaller of the two gaps between this boulder's circles and the drawn rail faces (the section 7 guarantee, from the REAL circles).
    public static float SideGapOf(CoverBoulder b)
    {
        float minX = float.MaxValue, maxX = float.MinValue;
        for (int i = 0; i < b.Count; i++)
        {
            Vector2 c = b.CircleCentre(i);
            minX = Mathf.Min(minX, c.x - b.CircleRadius(i));
            maxX = Mathf.Max(maxX, c.x + b.CircleRadius(i));
        }
        return Mathf.Min(minX + Edge, Edge - maxX);
    }

    // ---- solid-shape queries (every hook uses these) ----

    // Does a disc (p, r) touch a solid boulder?
    public static bool Inside(Vector2 p, float r)
    {
        for (int i = 0; i < pool.Length; i++)
            if (pool[i] != null && pool[i].Solid && pool[i].Touches(p, r)) return true;
        return false;
    }

    // The first solid hit along the segment a -> b swept by a disc of radius r: t in 0..1 and the surface normal there.
    public static bool FirstHit(Vector2 a, Vector2 b, float r, out float t, out Vector2 normal)
    {
        t = 2f; normal = Vector2.up;
        Vector2 d = b - a;
        float len2 = d.sqrMagnitude;
        for (int k = 0; k < pool.Length; k++)
        {
            var bd = pool[k];
            if (bd == null || !bd.Solid) continue;
            for (int i = 0; i < bd.Count; i++)
            {
                Vector2 c = bd.CircleCentre(i);
                float R = bd.CircleRadius(i) + r;
                Vector2 f = a - c;
                float tt;
                if (f.sqrMagnitude <= R * R) tt = 0f;
                else
                {
                    if (len2 < 1e-10f) continue;
                    float bq = Vector2.Dot(f, d), cq = f.sqrMagnitude - R * R;
                    float disc = bq * bq - len2 * cq;
                    if (disc < 0f) continue;
                    tt = (-bq - Mathf.Sqrt(disc)) / len2;
                    if (tt < 0f || tt > 1f) continue;
                }
                if (tt < t)
                {
                    t = tt;
                    Vector2 p = a + d * tt;
                    Vector2 n = p - c;
                    normal = n.sqrMagnitude > 1e-8f ? n.normalized : Vector2.up;
                }
            }
        }
        return t <= 1f;
    }

    // Is the line of sight a -> b cut by a boulder (a point-sized ray)?
    public static bool Blocked(Vector2 a, Vector2 b)
    {
        if (!Any) return false;
        float t; Vector2 n;
        return FirstHit(a, b, 0f, out t, out n);
    }

    // The length of a beam going straight up from `from`, `half` wide: cut at the first boulder face in its column.
    public static float ClipUp(Vector2 from, float len, float half)
    {
        if (!Any) return len;
        for (int k = 0; k < pool.Length; k++)
        {
            var bd = pool[k];
            if (bd == null || !bd.Solid) continue;
            for (int i = 0; i < bd.Count; i++)
            {
                Vector2 c = bd.CircleCentre(i);
                float R = bd.CircleRadius(i) + half;
                float dx = c.x - from.x;
                if (Mathf.Abs(dx) >= R) continue;
                float dy = Mathf.Sqrt(R * R - dx * dx);
                float bottom = c.y - dy, top = c.y + dy;
                if (top < from.y) continue;          // wholly below the muzzle
                float cut = Mathf.Max(0f, bottom - from.y);
                if (cut < len) len = cut;
            }
        }
        return len;
    }

    // Is this landing site (a backdrop point an elite parks on) under a boulder?
    public static bool SiteCovered(Vector2 p) { return Alive > 0 && Inside(p, .6f); }

    // ---- the ship (section 7) ----

    static Vector2 lastShip;
    static bool lastShipSet;
    static readonly float[] ivLo = new float[CoverBoulder.MaxCircles * Slots + 2], ivHi = new float[CoverBoulder.MaxCircles * Slots + 2];

    // Where the ship may be for a finger asking for `want` (already clamped to the reach): out of every boulder, never trapped.
    public static Vector2 ResolveShip(Vector2 want)
    {
        if (!Any) { lastShip = want; lastShipSet = true; return want; }
        float dt = Application.isPlaying ? Mathf.Min(Time.unscaledDeltaTime, .05f) : 1f / 60f;
        Vector2 got = ResolveStep(want, lastShipSet ? lastShip : want, lastShipSet ? lastWant : want, HullRadius, -Reach, Reach, ShipReach.Bottom, ShipReach.Top, dt);
        if ((got - want).sqrMagnitude > 1e-6f) ShipPushes++;
        lastShip = got;
        lastWant = want;
        lastShipSet = true;
        return got;
    }

    static Vector2 lastWant;

    // The hull may only SLIDE round a boulder (SlideSpeed u/s): when the finger comes out on the far side of it the ship walks round the
    // face instead of jumping across. A finger that is clear of every boulder (a pause-teleport, a blink) places the ship at once.
    public const float SlideSpeed = 18f, TeleportDistance = 3.2f;

    public static Vector2 ResolveStep(Vector2 want, Vector2 prev, Vector2 prevWant, float rs, float xMin, float xMax, float yMin, float yMax, float dt)
    {
        Vector2 got = Resolve(want, rs, xMin, xMax, yMin, yMax, prev);
        Vector2 d = got - prev;
        float cap = SlideSpeed * dt;
        if (d.magnitude <= cap) return got;
        // a pause-teleport (the finger lands more than TeleportDistance from the hull) places it at once; a held finger that comes out on the
        // far side of a boulder, or is pressed into it, walks the hull round the face
        if ((want - prevWant).magnitude > TeleportDistance) return got;
        float t; Vector2 n;
        bool displaced = (got - want).sqrMagnitude > 1e-6f;
        bool lagging = (prev - prevWant).sqrMagnitude > 4e-4f;   // still walking back to the finger after a boulder held it off
        if (!displaced && !lagging && !FirstHit(prev, got, rs, out t, out n)) return got;
        Vector2 cand = Resolve(prev + d.normalized * cap, rs, xMin, xMax, yMin, yMax, prev);
        // (squeezed against a rail the only exit may be the far one: never worse than the plain answer)
        return (cand - prev).sqrMagnitude < d.sqrMagnitude ? cand : got;
    }

    public static void ForgetShip() { lastShipSet = false; }

    // The slide: push `p` out along the circle normal (from where the ship was when the finger is deep inside), clamp to
    // the box, repeat; if the clamp put it back in, take the nearest free exit on its row, then on its column.
    public static Vector2 Resolve(Vector2 p, float rs, float xMin, float xMax, float yMin, float yMax, Vector2 prev)
    {
        for (int pass = 0; pass < 4; pass++)
        {
            bool moved = false;
            for (int k = 0; k < pool.Length; k++)
            {
                var bd = pool[k];
                if (bd == null || !bd.Solid) continue;
                for (int i = 0; i < bd.Count; i++)
                {
                    Vector2 c = bd.CircleCentre(i);
                    float R = bd.CircleRadius(i) + rs;
                    Vector2 d = p - c;
                    float m2 = d.sqrMagnitude;
                    if (m2 >= R * R) continue;
                    float m = Mathf.Sqrt(m2);
                    Vector2 dir;
                    if (m > R * .35f) dir = d / m;
                    else
                    {
                        // near the centre: leave on the side the ship came from (it never flips through the boulder)
                        Vector2 q = prev - c;
                        dir = q.sqrMagnitude > 1e-6f ? q.normalized : Vector2.up;
                    }
                    p = c + dir * R;
                    moved = true;
                }
            }
            p.x = Mathf.Clamp(p.x, xMin, xMax);
            p.y = Mathf.Clamp(p.y, yMin, yMax);
            if (!moved) return p;
        }
        if (!InsideAny(p, rs)) return p;
        p = RowExit(p, rs, xMin, xMax, prev.x);
        if (!InsideAny(p, rs)) return p;
        return ColumnExit(p, rs, yMin, yMax);
    }

    static bool InsideAny(Vector2 p, float rs)
    {
        for (int k = 0; k < pool.Length; k++)
        {
            var bd = pool[k];
            if (bd == null || !bd.Solid) continue;
            for (int i = 0; i < bd.Count; i++)
            {
                Vector2 c = bd.CircleCentre(i);
                float R = bd.CircleRadius(i) + rs - .002f;
                if ((p - c).sqrMagnitude < R * R) return true;
            }
        }
        return false;
    }

    // The nearest x on the row of p that no circle covers, within [xMin, xMax].
    static Vector2 RowExit(Vector2 p, float rs, float xMin, float xMax, float preferX)
    {
        int n = 0;
        for (int k = 0; k < pool.Length; k++)
        {
            var bd = pool[k];
            if (bd == null || !bd.Solid) continue;
            for (int i = 0; i < bd.Count && n < ivLo.Length; i++)
            {
                Vector2 c = bd.CircleCentre(i);
                float R = bd.CircleRadius(i) + rs;
                float dy = p.y - c.y;
                if (Mathf.Abs(dy) >= R) continue;
                float h = Mathf.Sqrt(R * R - dy * dy);
                ivLo[n] = c.x - h; ivHi[n] = c.x + h; n++;
            }
        }
        float best = p.x, bestD = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            for (int s = 0; s < 2; s++)
            {
                float x = s == 0 ? ivLo[i] - .003f : ivHi[i] + .003f;
                if (x < xMin || x > xMax) continue;
                bool free = true;
                for (int j = 0; j < n && free; j++) if (x > ivLo[j] && x < ivHi[j]) free = false;
                if (!free) continue;
                float d = Mathf.Abs(x - p.x) + (Mathf.Abs(x - preferX) < Mathf.Abs(x - p.x) ? -1e-4f : 0f);
                if (d < bestD) { bestD = d; best = x; }
            }
        }
        return new Vector2(best, p.y);
    }

    static Vector2 ColumnExit(Vector2 p, float rs, float yMin, float yMax)
    {
        int n = 0;
        for (int k = 0; k < pool.Length; k++)
        {
            var bd = pool[k];
            if (bd == null || !bd.Solid) continue;
            for (int i = 0; i < bd.Count && n < ivLo.Length; i++)
            {
                Vector2 c = bd.CircleCentre(i);
                float R = bd.CircleRadius(i) + rs;
                float dx = p.x - c.x;
                if (Mathf.Abs(dx) >= R) continue;
                float h = Mathf.Sqrt(R * R - dx * dx);
                ivLo[n] = c.y - h; ivHi[n] = c.y + h; n++;
            }
        }
        float best = p.y, bestD = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            for (int s = 0; s < 2; s++)
            {
                float y = s == 0 ? ivLo[i] - .003f : ivHi[i] + .003f;
                if (y < yMin || y > yMax) continue;
                bool free = true;
                for (int j = 0; j < n && free; j++) if (y > ivLo[j] && y < ivHi[j]) free = false;
                if (!free) continue;
                float d = Mathf.Abs(y - p.y);
                if (d < bestD) { bestD = d; best = y; }
            }
        }
        return new Vector2(p.x, best);
    }

    // ---- shots (section 4) ----

    // A projectile that was stopped at `at` (normal `n`): a few chips of the boulder's colours and a soft thud.
    public static void Impact(Vector2 at, Vector2 n, int chips = 3)
    {
        var b = First;
        if (b == null) return;
        var e = b.Entry;
        Fx.Burst(at, n, .55f, chips, .8f, 2.2f, .45f, e.debris, e.chip2);
        Thud();
    }

    static void Thud()
    {
        if (!(Application.isPlaying || EnemyDeathAudio.Simulate)) return;
        float now = Application.isPlaying ? Time.unscaledTime : simClock;
        if (now - lastThud < ThudGap) return;
        lastThud = now;
        Thuds++;
        EnemyDeathAudio.PlayAuthored(ShieldShockwave.SoundKey, ThudVolume);
    }

    // A hostile shot at `p` (radius r) is on a boulder: popped by HostileShots (it returns true to say so).
    public static bool StopsShot(Vector2 p, float r)
    {
        if (!Inside(p, r)) return false;
        ShotsBlocked++;
        return true;
    }

    // The player's projectile moving from `from` to `to` (radius r): where it ends if a boulder is in the way.
    public static bool StopsPlayerShot(Vector2 from, Vector2 to, float r, out Vector2 at)
    {
        at = to;
        if (!Any) return false;
        float t; Vector2 n;
        if (!FirstHit(from, to, r, out t, out n)) return false;
        at = from + (to - from) * t;
        PlayerShotsBlocked++;
        Impact(at, n);
        return true;
    }

    // ---- line of sight to the ship (section 4: beams and area hazards) ----

    // True when `hit` (the collider that touched the ship) belongs to a hazard whose shooter is hidden from the ship by a
    // boulder: the hit does not land. The shooter's position is the hazard's own (its mine, its elite, its boss).
    public static bool ShadowsHit(GameObject hit, Vector2 ship)
    {
        if (!Any || hit == null) return false;
        Vector2 from;
        if (!OriginOf(hit, out from)) return false;
        if (!Blocked(from, ship)) return false;
        LosBlocked++;
        return true;
    }

    public static bool OriginOf(GameObject hit, out Vector2 from)
    {
        from = Vector2.zero;
        AttackHazardHitbox hb;
        if (hit.TryGetComponent(out hb) && hb.hazard != null)
        {
            var sh = hb.hazard.Shooter;
            if (sh != null) { from = sh.transform.position; return true; }
            return false;
        }
        RailMineLaserHitbox lh;
        if (hit.TryGetComponent(out lh) && lh.laser != null) { from = lh.laser.From; return true; }
        BossBeam beam;
        if (hit.transform.parent != null && hit.transform.parent.TryGetComponent(out beam)) { from = beam.Origin; return true; }
        return false;
    }

    // ---- bodies (section 4) ----

    // Every enemy body, rock and atom on the board is pushed out of the boulders (after the movers ran this frame).
    public static void PushBodies()
    {
        if (!Any) return;
        var live = ClearTarget.Live;
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var t = live[i];
            if (t == null) continue;
            if (!t.isActiveAndEnabled || !Pushable(t)) continue;
            Vector3 p3 = t.transform.position;
            Vector2 p = p3;
            float r = t.Radius * BodyShare;
            Vector2 q = PushOut(p, r);
            if ((q - p).sqrMagnitude > 1e-8f)
            {
                t.transform.position = new Vector3(q.x, q.y, p3.z);
                BodiesPushed++;
            }
        }
    }

    static bool Pushable(ClearTarget t)
    {
        var go = t.gameObject;
        if (!(go.CompareTag("Enimey") || go.CompareTag("Astr") || go.CompareTag("pickUp"))) return false;
        if (t.IsShotHitbox) return false;
        if (go.TryGetComponent(out RailMineMount _)) return false;
        if (go.TryGetComponent(out BossTarget _) || go.TryGetComponent(out BossActor _)) return false;
        if (go.TryGetComponent(out AttackHazardHitbox _) || go.TryGetComponent(out RailMineLaserHitbox _)) return false;
        EliteShip e;
        if (go.TryGetComponent(out e) && !e.InPlay) return false;
        return true;
    }

    // `p` (a disc of radius r) moved out of every solid circle along the normal.
    public static Vector2 PushOut(Vector2 p, float r)
    {
        for (int pass = 0; pass < 3; pass++)
        {
            bool moved = false;
            for (int k = 0; k < pool.Length; k++)
            {
                var bd = pool[k];
                if (bd == null || !bd.Solid) continue;
                for (int i = 0; i < bd.Count; i++)
                {
                    Vector2 c = bd.CircleCentre(i);
                    float R = bd.CircleRadius(i) + r;
                    Vector2 d = p - c;
                    float m2 = d.sqrMagnitude;
                    if (m2 >= R * R) continue;
                    float m = Mathf.Sqrt(m2);
                    Vector2 dir = m > 1e-4f ? d / m : Vector2.up;
                    p = c + dir * R;
                    moved = true;
                }
            }
            if (!moved) break;
        }
        return p;
    }

    // A SHOVED body (EnemyShove) at `body` with half width `half` that moved `step` this frame meets a boulder: crash by the
    // rail-crash roll (ShoveCrash.RailChance of its impact speed and weight), else a scrape and pushed out.
    // Returns true when it crashed (the shove ends: the body is gone).
    public static bool ShoveMeet(Transform body, float half, Vector2 step, float dt)
    {
        if (!Any || body == null) return false;
        Vector2 p = body.position;
        float r = Mathf.Max(.15f, half) * BodyShare;
        if (!Inside(p, r)) return false;
        float speed = dt > 1e-5f ? step.magnitude / dt : 0f;
        Vector2 q = PushOut(p, r);
        Vector2 normal = (q - p).sqrMagnitude > 1e-8f ? (q - p).normalized : Vector2.up;
        var go = body.gameObject;
        bool crash = false;
        if (ShoveCrash.CanRailCrash(go) && ShoveCrash.WaveKills < ShoveCrash.MaxKillsPerWave)
        {
            float chance = ShoveCrash.RailChance(speed, ShoveCrash.Weight(go));
            crash = chance > 0f && ShoveCrash.Roll() < chance;
        }
        Impact(p, -normal, crash ? 8 : 3);
        if (crash && ShoveCrash.RailCrash(go, p)) { CrashKills++; return true; }
        body.position = new Vector3(q.x, q.y, body.position.z);
        BodiesPushed++;
        return false;
    }
}
