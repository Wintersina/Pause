using System.Collections.Generic;
using UnityEngine;

// A rail mine's laser ("have the rail mines shoot a laser"): a straight
// beam from the mine's rail across the whole lane to the opposite rail, at a
// random angle off horizontal each shot ("shot at different angles too,
// randomly"). Driven by the mine's EnemyBrain (EnemyAttack.Laser):
//
//   Aim    the brain's windup (the mine's waking -> charging loop and charge
//          light, as before); for its last AimSeconds a thin blinking aim
//          line shows exactly where the beam will burn. Harmless.
//   Beam   BeamSeconds of sustained beam: the only phase with a hitbox.
//   Cool   CoolSeconds of flicker as it dies out. Harmless.
//
// LOOK. The world boss's own laser art (BossArt: the lane telegraph cell
// for the aim line, the two beam cells, BossAttackFx's muzzle flash and rail
// spark), so it is the same hard-edged pixel beam as the boss lasers, with a
// white core (lime in Verdant, that world's laser). The boss beams' hot pink
// sits inside the player's red band (HostileGlow.IsPlayerRed, 334 deg), so
// the mine's copy of those cells has every such pixel turned to neon
// magenta (MagentaHue), keeping its saturation and brightness: built once
// per world (MineLaserArt), never per fire or per frame.
//
// EMBER'S MINE IS A FLAME-THROWER instead (MineFlameArt, world 3 only; Tide
// keeps the cells above): a pilot flame at the muzzle grows through the
// windup like a gas flare building, the aim line is a dashed gas jet, the
// beam is six looping frames of white-yellow core, orange body and a deep
// red-orange edge with pink rim and ember specks over a faint heat haze,
// with a ragged muzzle burst and a scorch at the far rail. Pure art: the
// timings, angle stream, hitbox and Span geometry are the code above, unchanged.
//
// GEOMETRY. Every frame (LateUpdate, after RailMineMount places the mine:
// DefaultExecutionOrder) the beam is laid from the mine's MUZZLE -- its
// glowing core, RailMineArt.CoreOffset, mirrored for a right-hand mine --
// across the lane at this shot's Angle, to the opposite rail's drawn inner
// face (BossRails.DrawnInnerEdge: the measured rails, or where WorldPainter
// puts them on this screen, RailInset included): Length = (face - muzzle)
// / cos Angle. Everything pivots on the core, so a tilted beam still
// leaves the mine's head (it used to pivot on the rail face behind the
// mine, 0.26 u outboard of the core: at 35 deg it crossed the core 0.18 u
// off and left the sphere near its rim). Both ends of the drawn quads (the
// beam, the aim line) are cut along the rails' vertical, so the far end
// lies flush on the opposite face at every angle (a square-cut end left a
// wedge of gap on one side): each quad is the sheared parallelogram
// muzzle -> face, built as a rotate-scale-rotate pair (Span). It rides the
// board with the mine and is blocked by nothing but the rails.
//
// ANGLE. Drawn when the windup starts (Arm), so the blinking aim line shows
// exactly the line the beam will burn: uniform in +/-MaxAngleDeg off
// horizontal (positive: rising toward the far rail), never within
// MinAngleChangeDeg of the previous shot's. From the laser's own xorshift
// stream (NextAngle), seeded from UnityEngine.Random's state without
// drawing from it (the HazardSize approach), so every other seeded stream is
// consumed exactly as before. Mirrored left / right: the direction is
// (-side cos A, sin A), side +1 for the right rail. Everything -- the
// sprites, the hitbox, Touches, Burn, a blink's LandsOn -- follows the one
// rotated segment From (the muzzle) -> To (the far face).
//
// DAMAGE. The pilot: a trigger BoxCollider2D tagged "Enimey" on a child
// (HitboxName), HitThickness across the beam, enabled in the Beam phase
// only -- collisionDetection's normal hostile hit: a heart (the beam is not
// spent on the hull: it burns on and the heart's i-frames carry the ship
// through), or, shielded, absorbed (EliteShip.ShieldRam -> EraseHitbox ends
// the beam); a blink erases it only when the hull lands ON it (BlinkStrike:
// not anywhere in the jump's 0.95 u blast circle). The fatal hit destroys
// the hitbox and ends the beam. Nothing else near the ship touches it.
// Other hazards: TARGETS ARE DECIDED IN ONE PLACE, Burn(): when
// HurtsOtherEnemies is on, every ClearTarget hazard the live beam's rect
// touches (FriendlyFire.HostileFireCanHit: not the boss, not shot hitboxes,
// not a target still in its spawn-in protection, never its own mine; off in
// the tutorial and during a player death) takes FriendlyFire.HostileHit --
// the hostile-fire hit, a kill or an elite's heart, unpaid, within the
// frame's kill cap -- at most once per pulse.
//
// PAUSE / 60 FPS. Timers only advance through Step(dt), which the brain
// calls on running frames, so a frozen world freezes the laser. Pooled
// (RailMineLasers, at most Max), every sprite looked up once per fire;
// nothing allocates per frame.
[DefaultExecutionOrder(10)]   // LateUpdate after RailMineMount (0) has placed the mine this frame
public class RailMineLaser : MonoBehaviour
{
    public enum Phase { Off, Aim, Beam, Cool }

    // ---- tunables ----
    public const float AimSeconds = .7f;       // the aim line: the last this-many seconds of the windup
    public const float BeamSeconds = .4f;      // the sustained beam: the only harmful phase
    public const float CoolSeconds = .2f;      // the flicker out
    public const float DrawWidth = .4f;        // drawn beam (the art cell's width)
    public const float HitThickness = .28f;    // hitbox across the beam (DrawWidth x BossConfig.BeamHitFraction)
    public const float SightWidth = .12f;      // the aim line (DrawWidth x BossConfig.BeamSightWidth)
    public const float FlashAlong = .19f;      // muzzle flash: this far along from the core (the sphere's lane-side rim)
    public const float FlashSize = .34f;
    public const int SortBeam = 11;            // under the mine (12): the beam leaves its body
    public const int SortFx = 13;
    public const string HitboxName = "RailMineLaserHit";
    // Friendly fire: does the live beam hit other hazards (rocks, enemies,
    // mines, elites)? The one switch for the beam's target filter.
    public static bool HurtsOtherEnemies = true;
    public const int MaxHitsPerPulse = 16;
    // A mine rides up its rail beside the ship (RailMineMount) and fires this
    // many lasers, this many seconds apart (end of one volley to the aim of
    // the next), whatever the board speed; then the board carries it away.
    public const int ShotsPerRide = 2;
    public const float ShotGapSeconds = 1f;
    // The random tilt of each shot.
    public const float MaxAngleDeg = 35f;       // either way off horizontal
    public const float MinAngleChangeDeg = 8f;  // consecutive shots differ by at least this
    // Tests / previews: every shot at this angle (degrees) instead of a draw.
    public static float? AngleOverride;

    SpriteRenderer sight, beam, flash, impact, pilot, haze;
    MineFlameArt flame;   // Ember's flame-thrower art (null: the boss-cell beam of every other world)
    float tellTotal;
    GameObject hitbox;
    BoxCollider2D box;
    Transform owner;
    Sprite sightSprite;
    readonly Sprite[] beamSprites = new Sprite[2];
    readonly Sprite[] flashSprites = new Sprite[BossAttackFx.FlashFrames];
    readonly Sprite[] sparkSprites = new Sprite[2];
    readonly int[] hit = new int[MaxHitsPerPulse];
    int hitCount, world;
    float t, age, aimDelay, length, side, y, angle;
    Vector2 from, to, dir = Vector2.left;

    public Phase State { get; private set; }
    public bool Active => State != Phase.Off;
    public bool Live => State == Phase.Beam && hitbox != null && box != null && box.enabled;
    public bool SightShown => sight != null && sight.enabled;
    public bool BeamShown => beam != null && beam.enabled;
    public GameObject Hitbox => hitbox;
    public Transform Owner => owner;
    public Vector2 From => from;            // the muzzle: the mine's core (MuzzleOf)
    public Vector2 To => to;                // the opposite rail's inner face, along Angle
    public Vector2 Direction => dir;        // unit, From -> To
    public float Angle => angle;            // degrees off horizontal (+: rising toward the far rail)
    public float Length => length;
    public float Y => y;
    public float PhaseTime => t;
    public int HitsThisPulse => hitCount;
    public SpriteRenderer BeamRenderer => beam;
    public SpriteRenderer SightRenderer => sight;

    // Where a laser from a mine at x runs: rail face to rail face.
    public static void SpanFor(float mineX, out float fromX, out float toX)
    {
        float edge = BossRails.DrawnInnerEdge;
        float s = mineX >= 0f ? 1f : -1f;
        fromX = s * edge;
        toX = -s * edge;
    }

    // Where a mine's beam leaves it: its core (RailMineArt.CoreOffset, at
    // the mine's scale), mirrored for a right-hand mine (flipX about the pivot).
    public static Vector2 MuzzleOf(Transform mine, int world)
    {
        Vector3 p = mine.position, sc = mine.lossyScale;
        Vector2 core = RailMineArt.CoreOffset(world);
        float s = p.x >= 0f ? 1f : -1f;
        return new Vector2(p.x - s * core.x * Mathf.Abs(sc.x), p.y + core.y * Mathf.Abs(sc.y));
    }

    // The beam's direction from a rail (side +1: the right one) at `deg`.
    public static Vector2 DirectionFor(float side, float deg)
    {
        float r = deg * Mathf.Deg2Rad;
        return new Vector2(-side * Mathf.Cos(r), Mathf.Sin(r));
    }

    // Does a body at p (half-width halfW, half-height halfH: an upright box)
    // overlap the live beam's hit rect -- the hitbox, HitThickness across the
    // rotated segment From -> To? (Separating axes: the beam's two, the
    // box's two.)
    public bool Touches(Vector2 p, float halfW, float halfH)
    {
        if (!Live) return false;
        return RectOverlaps(from, dir, length, HitThickness * .5f, p, halfW, halfH);
    }

    public static bool RectOverlaps(Vector2 from, Vector2 dir, float length, float halfThick, Vector2 p, float halfW, float halfH)
    {
        Vector2 n = new Vector2(-dir.y, dir.x);
        Vector2 d = p - (from + dir * (length * .5f));
        float ax = Mathf.Abs(dir.x), ay = Mathf.Abs(dir.y);
        // the box's axes
        if (Mathf.Abs(d.x) >= halfW + length * .5f * ax + halfThick * ay) return false;
        if (Mathf.Abs(d.y) >= halfH + length * .5f * ay + halfThick * ax) return false;
        // the beam's axes
        if (Mathf.Abs(Vector2.Dot(d, dir)) >= length * .5f + halfW * ax + halfH * ay) return false;
        if (Mathf.Abs(Vector2.Dot(d, n)) >= halfThick + halfW * ay + halfH * ax) return false;
        return true;
    }

    // Distance from p to the beam's centre line (the segment From -> To).
    public float DistanceTo(Vector2 p) => Mathf.Sqrt(HostileShots.SegmentDistanceSq(from, to, p));

    // ---- the angle stream ----
    static uint rng;
    static float lastAngle = float.NaN;

    public static void Seed(uint seed)
    {
        rng = seed == 0u ? 0x9E3779B9u : seed;
        lastAngle = float.NaN;
        for (int i = 0; i < 4; i++) Next01();   // stir
    }

    // From UnityEngine.Random's current state, without drawing from it.
    static void SeedFromUnity()
    {
        var st = Random.state;
        var w = Unity.Collections.LowLevel.Unsafe.UnsafeUtility.As<Random.State, Words>(ref st);
        uint h = 2166136261u;
        h = (h ^ w.a) * 16777619u; h = (h ^ w.b) * 16777619u;
        h = (h ^ w.c) * 16777619u; h = (h ^ w.d) * 16777619u;
        Seed(h ^ 0x5bd1e995u);
    }

    struct Words { public uint a, b, c, d; }

    static float Next01()
    {
        rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
        return (rng & 0xFFFFFF) / 16777216f;
    }

    // The next shot's angle: uniform in +/-MaxAngleDeg, at least
    // MinAngleChangeDeg from the previous one.
    public static float NextAngle()
    {
        if (AngleOverride.HasValue) return Mathf.Clamp(AngleOverride.Value, -MaxAngleDeg, MaxAngleDeg);
        if (rng == 0u) SeedFromUnity();
        float a = Mathf.Lerp(-MaxAngleDeg, MaxAngleDeg, Next01());
        if (!float.IsNaN(lastAngle) && Mathf.Abs(a - lastAngle) < MinAngleChangeDeg)
        {
            // pushed clear of the last one, toward the side with room
            float up = lastAngle + MinAngleChangeDeg, down = lastAngle - MinAngleChangeDeg;
            a = a >= lastAngle ? (up <= MaxAngleDeg ? up : down) : (down >= -MaxAngleDeg ? down : up);
        }
        lastAngle = a;
        return a;
    }

    public static RailMineLaser Create(Transform root)
    {
        var go = new GameObject("RailMineLaser");
        go.transform.SetParent(root, false);
        var l = go.AddComponent<RailMineLaser>();
        l.sight = Piece(go.transform, "Sight", SortBeam, true);
        l.beam = Piece(go.transform, "Beam", SortBeam, true);
        l.flash = Piece(go.transform, "Flash", SortFx);
        l.impact = Piece(go.transform, "Impact", SortFx);
        l.pilot = Piece(go.transform, "Pilot", SortFx);
        l.haze = Piece(go.transform, "Haze", SortBeam - 1, true);
        l.EnsureHitbox();
        l.box.enabled = false;
        go.SetActive(false);
        return l;
    }

    // `framed`: the sprite sits under its own frame transform, so Span can
    // shear it (a parallelogram, both ends cut along the rails).
    static SpriteRenderer Piece(Transform parent, string name, int order, bool framed = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        if (framed)
        {
            var art = new GameObject(name + "Art");
            art.transform.SetParent(go.transform, false);
            go = art;
        }
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = order;
        sr.enabled = false;
        return sr;
    }

    void EnsureHitbox()
    {
        if (hitbox != null) return;
        hitbox = new GameObject(HitboxName);
        hitbox.tag = "Enimey";
        hitbox.transform.SetParent(transform, false);
        box = hitbox.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = new Vector2(HitThickness, .01f);
        hitbox.AddComponent<RailMineLaserHitbox>().laser = this;
    }

    // The windup begins (`tellSeconds` long): the aim line will show for
    // its last AimSeconds.
    public void Arm(Transform mine, int world, float tellSeconds)
    {
        owner = mine;
        this.world = world;
        var art = MineLaserArt.For(world);
        sightSprite = art.sight;
        beamSprites[0] = art.beam0;
        beamSprites[1] = art.beam1;
        for (int i = 0; i < flashSprites.Length; i++) flashSprites[i] = art.flash[i];
        flame = art.flame;
        tellTotal = Mathf.Max(.01f, tellSeconds);
        sparkSprites[0] = art.spark[0];
        sparkSprites[1] = art.spark[1];
        sight.sprite = sightSprite;
        beam.sprite = beamSprites[0];
        angle = NextAngle();   // the aim line shows this exact line
        aimDelay = Mathf.Max(0f, tellSeconds - AimSeconds);
        t = age = 0f;
        hitCount = 0;
        State = Phase.Aim;
        if (box != null) box.enabled = false;
        HideAll();
        gameObject.SetActive(true);
        Place();
    }

    // The windup is over: the beam burns.
    public void Fire()
    {
        if (State != Phase.Aim) return;
        State = Phase.Beam;
        t = age = 0f;
        hitCount = 0;
        EnsureHitbox();
        box.enabled = true;
        sight.enabled = false;
        if (pilot != null) pilot.enabled = false;
        beam.enabled = flash.enabled = true;
        Place();
    }

    // Absorbed by a shield or erased by a blink: the beam is spent.
    public void Erase()
    {
        if (State == Phase.Beam) EndBeam();
    }

    void EndBeam()
    {
        State = Phase.Cool;
        t = 0f;
        if (box != null) box.enabled = false;
        flash.enabled = impact.enabled = false;
        if (haze != null) haze.enabled = false;
    }

    // Gone at once (its mine is gone).
    public void Cancel() { Recycle(); }

    public void Step(float dt)
    {
        if (!Active || dt <= 0f) return;
        t += dt;
        age += dt;
        switch (State)
        {
            case Phase.Beam:
                // hit the pilot (collisionDetection destroyed the hitbox) or done
                if (hitbox == null || t >= BeamSeconds) EndBeam();
                break;
            case Phase.Cool:
                if (t >= CoolSeconds) { Recycle(); return; }
                break;
        }
        Place();
        if (State == Phase.Beam) Burn();
    }

    // After RailMineMount has put the mine where it is this frame.
    void LateUpdate()
    {
        if (Active) Place();
    }

    // Lays everything along the beam at the mine's current y.
    public void Place()
    {
        if (owner == null) { Recycle(); return; }
        Vector3 m = owner.position;
        y = m.y;
        float fx, tx;
        SpanFor(m.x, out fx, out tx);
        side = m.x >= 0f ? 1f : -1f;
        dir = DirectionFor(side, angle);
        from = MuzzleOf(owner, world);
        // the mine's core to the opposite rail's face, at the angle
        length = Mathf.Abs(tx - from.x) / Mathf.Max(.05f, Mathf.Abs(dir.x));
        to = from + dir * length;
        transform.position = new Vector3(from.x, from.y, 0f);
        // local +y runs along the beam, toward the far rail
        transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
        transform.localScale = Vector3.one;

        float tick = BossArt.Tick;
        switch (State)
        {
            case Phase.Aim:
            {
                bool on = t >= aimDelay && Mathf.FloorToInt((t - aimDelay) / (BossArt.TelegraphBlinkTicks * tick)) % 2 == 0;
                sight.enabled = on && sightSprite != null;
                // the painted aim line has two drawings (aim a, b): one per blink
                if (flame != null && flame.sightB != null) sight.sprite = Mathf.FloorToInt((t - aimDelay) / (BossArt.TelegraphBlinkTicks * tick) * .5f) % 2 == 0 ? sightSprite : flame.sightB;
                Span(sight.transform, SightWidth, length, dir, flame != null ? sight.sprite : null);
                if (flame != null) PlacePilot();
                break;
            }
            case Phase.Beam:
            {
                // a hot flicker on twos, never a smooth pulse (as the boss beams)
                float w = DrawWidth * (Mathf.FloorToInt(age / (2f * tick)) % 2 == 0 ? 1f : .85f);
                beam.enabled = beamSprites[0] != null;
                if (flame != null)
                {
                    // Ember: the flame-thrower. Tongues flow at 12 fps; the burst and the scorch run on their own loops
                    beam.sprite = flame.beam[Mathf.FloorToInt(age * 12f) % MineFlameArt.BeamFrames];
                    Span(beam.transform, w, length, dir, beam.sprite);
                    haze.sprite = flame.haze[Mathf.FloorToInt(age * 8f) % MineFlameArt.HazeFrames];
                    haze.enabled = haze.sprite != null;
                    Span(haze.transform, w * (Mathf.FloorToInt(age * 8f) % 2 == 0 ? 2.1f : 2.4f), length, dir, haze.sprite);
                    flash.sprite = flame.flash[Mathf.FloorToInt(age * 20f) % MineFlameArt.FlashFramesN];
                    flash.enabled = flash.sprite != null;
                    flash.transform.localPosition = new Vector3(0f, FlashAlong, 0f);
                    flash.transform.localScale = Vector3.one * (FlashSize * 1.5f);
                    impact.sprite = flame.spark[Mathf.FloorToInt(age * 14f) % MineFlameArt.SparkFrames];
                    impact.enabled = impact.sprite != null;
                    impact.transform.localPosition = new Vector3(0f, length, 0f);
                    impact.transform.localScale = Vector3.one * (BossConfig.RailSparkSize * 1.3f);
                }
                else
                {
                    beam.sprite = beamSprites[BossArt.FrameAt(BossArt.BeamTicks, age, true) % 2];
                    Span(beam.transform, w, length, dir);
                    int f = Mathf.Min(BossArt.FrameAt(BossAttackFx.FlashTickTable, age, true), flashSprites.Length - 1);
                    flash.sprite = flashSprites[f];
                    flash.enabled = flash.sprite != null;
                    flash.transform.localPosition = new Vector3(0f, FlashAlong, 0f);
                    flash.transform.localScale = Vector3.one * FlashSize;
                    impact.sprite = sparkSprites[Mathf.FloorToInt(age / tick) % 2];
                    impact.enabled = impact.sprite != null;
                    impact.transform.localPosition = new Vector3(0f, length, 0f);
                    impact.transform.localScale = Vector3.one * BossConfig.RailSparkSize;
                }
                if (box != null)
                {
                    // the hitbox's centre sits level with the pilot along the
                    // beam (where a shield shows the absorb, a heart darts to)
                    var p = EliteSystem.Player;
                    float along = length * .5f;
                    if (p != null) along = Mathf.Clamp(Vector2.Dot((Vector2)p.position - from, dir), 0f, length);
                    hitbox.transform.localPosition = new Vector3(0f, along, 0f);
                    box.size = new Vector2(HitThickness, Mathf.Max(.01f, length));
                    box.offset = new Vector2(0f, length * .5f - along);
                }
                break;
            }
            case Phase.Cool:
            {
                // flickers out on ticks, thinning
                float k = 1f - Mathf.Clamp01(t / CoolSeconds);
                beam.enabled = beamSprites[0] != null && Mathf.FloorToInt(t / tick) % 2 == 0;
                if (flame != null) beam.sprite = flame.beam[Mathf.FloorToInt((BeamSeconds + t) * 12f) % MineFlameArt.BeamFrames];
                Span(beam.transform, DrawWidth * Mathf.Max(.2f, k), length, dir, flame != null ? beam.sprite : null);
                break;
            }
        }
    }

    // FRIENDLY FIRE: the one place the beam picks its targets (besides the
    // pilot's hitbox). Every hazard whose body the live beam's rect touches,
    // once per pulse, never its own mine, through the hostile-fire hit
    // (FriendlyFire.HostileHit: unpaid, the frame's kill cap, off in the
    // tutorial / during a death).
    void Burn()
    {
        if (!HurtsOtherEnemies || length <= 0f || !FriendlyFire.HostileFireAllowed) return;
        var live = ClearTarget.Live;
        var shooter = owner != null ? owner.gameObject : null;
        float half = HitThickness * .5f;
        for (int i = 0; i < live.Count; i++)
        {
            var c = live[i];
            if (c == null || !FriendlyFire.HostileFireCanHit(c, shooter)) continue;
            var go = c.gameObject;
            int id = go.GetInstanceID();
            if (AlreadyHit(id)) continue;
            Vector2 p = c.transform.position;
            float r = c.Radius * .8f;
            float reach = half + r;
            if (HostileShots.SegmentDistanceSq(from, to, p) > reach * reach) continue;
            // where it is struck: its foot on the beam
            float along = Mathf.Clamp(Vector2.Dot(p - from, dir), 0f, length);
            Vector2 at = from + dir * along;
            // capped this frame: not spent, the beam tries again next frame
            if (!FriendlyFire.HostileHit(c, new Vector3(at.x, at.y, 0f), "mine laser")) return;
            if (hitCount < hit.Length) hit[hitCount++] = id;
            return;   // one a frame: the registry changes under a kill
        }
    }

    bool AlreadyHit(int id)
    {
        for (int i = 0; i < hitCount; i++) if (hit[i] == id) return true;
        return false;
    }

    // Lays a unit sprite (art, under its frame) as the parallelogram from
    // the muzzle to the far face: in the laser's frame (local +y along the
    // beam) its edges are (0, len) along the beam and (w, k) across it,
    // k = -w dir.y / dir.x, so the across edge is the rails' vertical (both
    // ends flush with a rail face) and the beam is w wide. A transform
    // cannot shear, so the linear map [[w, 0], [k, len]] is split as
    // R(phi) S(sx, sy) R(theta) (the closed-form 2x2 SVD): the frame takes
    // R(phi) S, the art R(theta).
    static void Span(Transform art, float w, float len, Vector2 dir, Sprite sprite = null)
    {
        len = Mathf.Max(.001f, len);
        float dx = Mathf.Abs(dir.x) > 1e-4f ? dir.x : -1e-4f;
        float k = -w * dir.y / dx;
        float e = (w + len) * .5f, f = (w - len) * .5f, g = k * .5f, h = k * .5f;
        float q = Mathf.Sqrt(e * e + h * h), r = Mathf.Sqrt(f * f + g * g);
        float a1 = Mathf.Atan2(g, f), a2 = Mathf.Atan2(h, e);
        var frame = art.parent;
        frame.localPosition = new Vector3(0f, len * .5f, 0f);
        frame.localRotation = Quaternion.Euler(0f, 0f, (a2 + a1) * .5f * Mathf.Rad2Deg);
        frame.localScale = new Vector3(q + r, Mathf.Max(1e-5f, q - r), 1f);
        art.localPosition = Vector3.zero;
        art.localRotation = Quaternion.Euler(0f, 0f, (a2 - a1) * .5f * Mathf.Rad2Deg);
        // (Ember's flame strips are not one unit square and are normalised to one; the boss cells, which are not exactly one either, are laid as they always were: sprite null)
        Vector3 unit = Vector3.one;
        if (sprite != null)
        {
            var sz = sprite.bounds.size;
            if (sz.x > 1e-4f && sz.y > 1e-4f && (Mathf.Abs(sz.x - 1f) > 1e-4f || Mathf.Abs(sz.y - 1f) > 1e-4f)) unit = new Vector3(1f / sz.x, 1f / sz.y, 1f);
        }
        art.localScale = unit;
    }

    // Ember's charge-up: a pilot flame at the muzzle that grows through the whole windup like a gas flare building,
    // flickering faster as it nears the burst, flaring just before it. Harmless art; nothing here touches the hitbox.
    void PlacePilot()
    {
        float k = Mathf.Clamp01(t / tellTotal);
        int f = Mathf.FloorToInt(t * (10f + 14f * k)) % MineFlameArt.PilotFrames;
        pilot.sprite = flame.pilot[f];
        pilot.enabled = pilot.sprite != null;
        float width = Mathf.Lerp(.1f, .36f, k * Mathf.Sqrt(k));
        if (tellTotal - t < .12f) width *= 1.25f;
        var sz = pilot.sprite != null ? pilot.sprite.bounds.size : Vector3.one;
        // base on the muzzle sphere's lane-side rim, tip along the beam
        pilot.transform.localScale = new Vector3(width / Mathf.Max(.01f, sz.x), width / Mathf.Max(.01f, sz.x), 1f);
        pilot.transform.localPosition = new Vector3(0f, FlashAlong * .6f + sz.y * .5f * width / Mathf.Max(.01f, sz.x), 0f);
    }

    void HideAll()
    {
        sight.enabled = beam.enabled = flash.enabled = impact.enabled = false;
        if (pilot != null) pilot.enabled = false;
        if (haze != null) haze.enabled = false;
    }

    public void Recycle()
    {
        State = Phase.Off;
        owner = null;
        if (box != null) box.enabled = false;
        if (sight != null) HideAll();
        gameObject.SetActive(false);
    }

    // collisionDetection's shielded path (EliteShip.ShieldRam): a mine
    // laser's hitbox is absorbed, the beam ends. False if `go` is not one.
    public static bool EraseHitbox(GameObject go)
    {
        if (go == null || !go.TryGetComponent(out RailMineLaserHitbox hb)) return false;
        if (hb.laser != null) hb.laser.Erase();
        return true;
    }

    public static bool IsHitbox(GameObject go) => go != null && go.TryGetComponent(out RailMineLaserHitbox _);

    // A pause jump (TeleportFx.Strike -> EliteShip.TeleportStrike) found
    // this hitbox inside its BlastRadius (0.95 u). That blast is sized for a
    // body; the beam is one long thin hitbox crossing the whole lane, so the
    // circle used to catch it from a landing well clear of it -- the beam
    // vanished whenever a jump landed near its row. It is erased only when
    // the landed hull itself is on the beam (LandsOn). True: it was a mine
    // laser's hitbox (handled, erased or not).
    public static bool BlinkStrike(GameObject go, Vector3 at)
    {
        if (go == null || !go.TryGetComponent(out RailMineLaserHitbox hb)) return false;
        if (hb.laser != null && hb.laser.LandsOn(at)) hb.laser.Erase();
        return true;
    }

    // Half the hull's extent across the beam when the pilot has no hit zone
    // to ask (the 1.35x hull's hitbox is ~0.66 u tall).
    public const float BlinkHullReach = .33f;

    // Does a hull landed at `at` lie on the live beam? The pilot's own hit
    // zone (ShipHitbox) when it is the one standing there, else its reach.
    public bool LandsOn(Vector2 at)
    {
        if (!Live) return false;
        var p = EliteSystem.Player;
        var zone = p != null ? ShipHitbox.Of(p.gameObject) : null;
        var col = zone != null ? zone.Active : null;
        if (col != null && col.enabled && box != null && ((Vector2)p.position - at).sqrMagnitude < 1e-4f)
        {
            Physics2D.SyncTransforms();
            return Physics2D.Distance(col, box).isOverlapped;
        }
        return DistanceTo(at) <= HitThickness * .5f + BlinkHullReach;
    }
}

// Marks a mine laser's hitbox (RailMineLaser.EraseHitbox, RamKill, DeathCrash).
public class RailMineLaserHitbox : MonoBehaviour
{
    public RailMineLaser laser;
}

// The mine laser's sprites for a world: the world boss's laser cells with
// their player-red pixels turned magenta, and the boss's flash / spark
// cells (which are drawn small, at the muzzle and the far rail). Built once
// per world and cached (rebuilt only if an editor scene swap destroyed them).
public sealed class MineLaserArt
{
    public const float MagentaHue = 312f / 360f;   // neon magenta: 44 deg clear of the player's red
    public Sprite sight, beam0, beam1;
    public readonly Sprite[] flash = new Sprite[BossAttackFx.FlashFrames];
    public readonly Sprite[] spark = new Sprite[2];
    public MineFlameArt flame;   // Ember only: the flame-thrower (MineFlameArt); sight / beam0 / beam1 / flash / spark above are its first cells

    // preview / tests only: Ember's mine draws the boss-cell beam like every other world (the before-picture)
    public static bool NoFlame;
    public static void ResetCache() { for (int i = 0; i < worlds.Length; i++) worlds[i] = null; }
    public const int EmberWorld = 3;   // Tide (4) shares Ember's boss cells but keeps the recoloured beam: only world 3 is the flame

    static readonly MineLaserArt[] worlds = new MineLaserArt[5];

    public static MineLaserArt For(int world)
    {
        int slot = Mathf.Clamp(world, 0, worlds.Length - 1);
        world = Mathf.Clamp(world, 0, 3);
        var a = worlds[slot];
        if (a != null && a.beam0 != null && a.flash[0] != null) return a;
        a = new MineLaserArt();
        if (slot == EmberWorld && !NoFlame)
        {
            var fl = MineFlameArt.Ember();
            a.flame = fl;
            a.sight = fl.sight;
            a.beam0 = fl.beam[0]; a.beam1 = fl.beam[1];
            a.flash[0] = fl.flash[0]; a.flash[1] = fl.flash[1];
            a.spark[0] = fl.spark[0]; a.spark[1] = fl.spark[1];
            worlds[slot] = a;
            return a;
        }
        var boss = BossCatalog.ForWorld(world);
        a.sight = Recolour(BossArt.Shot(boss, BossArt.Telegraph));
        a.beam0 = Recolour(BossArt.Shot(boss, BossArt.Beam0));
        a.beam1 = Recolour(BossArt.Shot(boss, BossArt.Beam0 + 1));
        for (int i = 0; i < a.flash.Length; i++) a.flash[i] = BossAttackFx.Get(boss, BossAttackFx.Flash0 + i);
        a.spark[0] = BossAttackFx.Get(boss, BossAttackFx.Spark0 + 1);
        a.spark[1] = BossAttackFx.Get(boss, BossAttackFx.Spark0 + 2);
        worlds[slot] = a;
        return a;
    }

    // A copy of `cell` with every player-red pixel turned to MagentaHue.
    // (No red in it: the cell itself.)
    static Sprite Recolour(Sprite cell)
    {
        if (cell == null) return null;
        int w, h;
        var px = ShieldContour.ReadPixels(cell, out w, out h);
        if (px == null) return cell;
        bool changed = false;
        for (int i = 0; i < px.Length; i++)
        {
            Color c = px[i];
            if (c.a <= 0f || !HostileGlow.IsPlayerRed(c)) continue;
            Color.RGBToHSV(c, out float hh, out float ss, out float vv);
            var m = Color.HSVToRGB(MagentaHue, ss, vv);
            m.a = c.a;
            px[i] = m;
            changed = true;
        }
        if (!changed) return cell;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.name = cell.name + "MineLaser";
        tex.filterMode = cell.texture != null ? cell.texture.filterMode : FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.hideFlags = HideFlags.HideAndDontSave;
        tex.SetPixels32(px);
        tex.Apply(false, false);
        var s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), cell.pixelsPerUnit, 0, SpriteMeshType.FullRect);
        s.name = tex.name;
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }
}

// The mine lasers, pooled and bounded: all Max built together on first use
// (so a later fire never allocates), reused.
public static class RailMineLasers
{
    public const int Max = 6;
    static readonly List<RailMineLaser> all = new List<RailMineLaser>(Max);
    static GameObject root;

    public static IReadOnlyList<RailMineLaser> All => all;
    public static int Created => all.Count;

    // A free laser, or null when all Max are busy (a busy screen skips one).
    public static RailMineLaser Take()
    {
        Prewarm();
        for (int i = 0; i < all.Count; i++)
            if (all[i] != null && !all[i].Active) return all[i];
        return null;
    }

    // Builds the pool (once per scene).
    public static void Prewarm()
    {
        if (root == null)
        {
            // a new scene took the old ones with it
            all.Clear();
            root = new GameObject("~RailMineLasers");
            while (all.Count < Max) all.Add(RailMineLaser.Create(root.transform));
        }
    }

    // Beams burning right now (the threat budget counts them as shots).
    public static int LiveBeams
    {
        get
        {
            int n = 0;
            for (int i = 0; i < all.Count; i++) if (all[i] != null && all[i].State == RailMineLaser.Phase.Beam) n++;
            return n;
        }
    }

    public static int ActiveCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < all.Count; i++) if (all[i] != null && all[i].Active) n++;
            return n;
        }
    }

    public static void Clear()
    {
        for (int i = 0; i < all.Count; i++) if (all[i] != null) all[i].Recycle();
    }
}
