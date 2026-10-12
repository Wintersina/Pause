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
// LOOK, THEMED. Ember (flame-thrower), Frost (cryo beam), Verdant (thorned vine), Tide (water jet) and Storm (lightning arc) each fire their own
// painted mine atlas (<w>_attack_minebeam.png, the mineflame layout, AttackArt.MineBeam / MineFlameArt.ForWorld): pilot, aim line, beam loop,
// muzzle burst, rail impact and haze in that world's material, all with the pink hostile cue. Where that atlas is missing: Space, Frost and Verdant
// fire their world's own painted laser kit (Resources/Attacks/<World>/<w>_attack_laser*.png via
// AttackArt.BossLaser: Space's ion beam, Frost's ice beam with crystal shards, Verdant's thorned vine beam), laid on the mine's geometry
// (PlaceKitAim / PlaceKitBeam). Tide and Storm then have no installed kit (Tide's is staged under Attacks/Tide~)
// and keep the boss-cell beam below. The older description follows.
//
// The world boss's own laser art (BossArt: the lane telegraph cell
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

    SpriteRenderer sight, beam, flash, impact, pilot, haze, lockOn, spark;
    AttackArt.LaserArt kit;   // the world's painted laser kit (Space ion, Frost ice, Verdant thorn vine); null: the other worlds' beams
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
    public SpriteRenderer FlashRenderer => flash;
    public SpriteRenderer ImpactRenderer => impact;
    public SpriteRenderer LockRenderer => lockOn;
    public SpriteRenderer SparkRenderer => spark;
    public SpriteRenderer PilotRenderer => pilot;
    public SpriteRenderer HazeRenderer => haze;
    public MineFlameArt Flame => flame;       // the painted mine beam this shot wears (Ember's flame, Frost / Verdant / Tide / Storm minebeam); null: kit or boss cells
    public AttackArt.LaserArt Kit => kit;     // the painted laser kit this shot wears (null: boss cells / flame)

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
        l.lockOn = Piece(go.transform, "Lock", SortFx);
        l.spark = Piece(go.transform, "Spark", SortFx + 1);
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
        kit = art.kit;
        ResetPieces();
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
        if (lockOn != null) lockOn.enabled = false;
        if (pilot != null) pilot.enabled = false;
        beam.enabled = flash.enabled = true;
        PewSound.Play(PewSound.Kind.Laser);
        Place();
    }

    // The shot's sound is the shared enemy laser cue (PewSound), through the effects bus like every other sound.

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
        if (spark != null) spark.enabled = false;
        if (lockOn != null) lockOn.enabled = false;
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
                if (kit != null) { sight.sprite = kit.sight[Mathf.FloorToInt((t - aimDelay) / (BossArt.TelegraphBlinkTicks * tick) * .5f) & 1]; SpanTiled(sight, KitSightScale, length, dir, false); }
                // the painted aim line has two drawings (aim a, b): one per blink
                if (flame != null && flame.sightB != null) sight.sprite = Mathf.FloorToInt((t - aimDelay) / (BossArt.TelegraphBlinkTicks * tick) * .5f) % 2 == 0 ? sightSprite : flame.sightB;
                if (kit == null) Span(sight.transform, SightWidth, length, dir, flame != null ? sight.sprite : null);
                if (flame != null) PlacePilot();
                if (kit != null) PlaceKitAim(on);
                break;
            }
            case Phase.Beam:
            {
                // a hot flicker on twos, never a smooth pulse (as the boss beams)
                float w = DrawWidth * (Mathf.FloorToInt(age / (2f * tick)) % 2 == 0 ? 1f : .85f);
                beam.enabled = beamSprites[0] != null;
                if (kit != null) PlaceKitBeam(1f, false);
                else if (flame != null)
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
                if (kit != null) { PlaceKitBeam(Mathf.Max(.2f, k), true); break; }
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


    // ---- the painted laser kit (Space ion, Frost ice, Verdant thorn vine): art only ----
    //
    // The world's boss-laser kit (AttackArt.BossLaser) laid on the mine's own geometry. The tell shows the kit's dotted aim line with its
    // lock reticle at the far rail and the windup cells gathering at the muzzle; the beam is the four body frames (12 fps) tiled down its
    // length (pixel-exact, not stretched) with the muzzle cells over its root and the impact burst and sparks where it meets the far rail;
    // the cool-down plays the fade cells. Every size derives from the body: its opaque columns are exactly HitThickness wide, so the
    // painted beam is the hit shape. The parallelogram (Span: both ends flush with a rail) and the hitbox are unchanged.
    public const float KitSightScale = 2.4f;   // the aim line's 128 px tile (its 2 px dashes are ~.04 u wide), as the boss's
    public const float KitLockScale = .8f;
    public const float KitFlareToBody = 1.5f;
    const float KitFps = 12f;

    public static float KitBodyScale(AttackArt.LaserArt k) => HitThickness * 128f / k.bodyOpaquePx;

    void ResetPieces()
    {
        ResetPiece(sight); ResetPiece(beam); ResetPiece(flash); ResetPiece(impact); ResetPiece(pilot); ResetPiece(haze); ResetPiece(lockOn); ResetPiece(spark);
    }

    static void ResetPiece(SpriteRenderer r)
    {
        if (r == null) return;
        r.drawMode = SpriteDrawMode.Simple;
        r.color = Color.white;
        r.transform.localRotation = Quaternion.identity;
        r.transform.localScale = Vector3.one;
    }

    // `art` (a renderer of a sprite one tile wide) tiled down the beam: `tile` is one tile's width in world units, the beam's parallelogram is
    // Span's. turned: the art's TOP is the root (the body strip), so the strip is turned half a circle.
    static void SpanTiled(SpriteRenderer r, float tile, float len, Vector2 dir, bool turned)
    {
        len = Mathf.Max(.001f, len);
        Span(r.transform, tile, len, dir);
        r.drawMode = SpriteDrawMode.Tiled;
        r.tileMode = SpriteTileMode.Continuous;
        float rows = len / tile;
        r.size = new Vector2(1f, rows);
        float sg = turned ? -1f : 1f;
        r.transform.localScale = new Vector3(sg, sg / rows, 1f);
    }

    void PlaceKitAim(bool sightOn)
    {
        float body = KitBodyScale(kit);
        float k = Mathf.Clamp01(t / tellTotal);
        flash.enabled = true;
        flash.sprite = kit.windup[Mathf.Min(7, Mathf.FloorToInt(k * 8f))];
        flash.transform.localPosition = new Vector3(0f, FlashAlong, 0f);
        flash.transform.localScale = Vector3.one * body * KitFlareToBody;
        lockOn.enabled = sightOn;
        lockOn.sprite = kit.lockOn[Mathf.FloorToInt(age / (2f * BossArt.Tick)) & 1];
        lockOn.transform.localPosition = new Vector3(0f, length, 0f);
        lockOn.transform.rotation = Quaternion.identity;
        lockOn.transform.localScale = Vector3.one * KitLockScale;
    }

    void PlaceKitBeam(float w, bool cooling)
    {
        float body = KitBodyScale(kit);
        beam.enabled = !cooling || Mathf.FloorToInt(t / BossArt.Tick) % 2 == 0;
        beam.sprite = kit.body[Mathf.FloorToInt((cooling ? BeamSeconds + t : age) * KitFps) & 3];
        SpanTiled(beam, body * w, length, dir, true);
        flash.enabled = true;
        flash.transform.localPosition = new Vector3(0f, FlashAlong, 0f);
        flash.transform.localScale = Vector3.one * body * KitFlareToBody;
        if (cooling)
        {
            flash.sprite = kit.fade[Mathf.Min(3, Mathf.FloorToInt(Mathf.Clamp01(t / CoolSeconds) * 4f))];
            return;
        }
        flash.sprite = kit.muzzle[Mathf.FloorToInt(age * KitFps) & 3];
        float size = body * KitFlareToBody;
        impact.enabled = true;
        impact.sprite = kit.impact[Mathf.FloorToInt(age * KitFps) & 3];
        impact.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);
        impact.transform.localPosition = new Vector3(0f, length - size * kit.impactBasePx / 128f, 0f);
        impact.transform.localScale = Vector3.one * size;
        spark.enabled = true;
        spark.sprite = kit.spark[Mathf.FloorToInt(age * 15f) & 3];
        spark.transform.localPosition = new Vector3(0f, length, 0f);
        spark.transform.rotation = Quaternion.identity;
        spark.transform.localScale = Vector3.one * size;
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
        if (spark != null) spark.enabled = false;
        if (lockOn != null) lockOn.enabled = false;
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

// The mine laser's sprites for a world. Ember (flame), Frost, Verdant, Tide and Storm wear their own painted atlas (MineFlameArt). The worlds
// without one (Space: its painted ion kit; Desert and any unpainted world: the boss's laser cells) are recoloured by MineCue.
//
// NO PINK ON A RAIL MINE (user, 2026-10-11): the mine's windup, beam and impact carry no pink. The painted atlases were remapped once
// (Art/Attacks/MineBeams~/recolor_nopink.py); the kit / boss cells are remapped here, once per world and cached, with the same
// constants. Each world's warning reads in its own colours instead: Space ion white-blue, Frost icy white-cyan, Verdant bright green-white,
// Ember amber / orange-white, Tide aqua-white, Storm electric white-violet, Desert sun-white.
public static class MineCue
{
    //                                       space  frost verdant ember  tide  storm desert
    public static readonly float[] Hue = { 215f, 195f, 135f, 32f, 188f, 258f, 45f };
    public static readonly float[] SatCap = { .45f, .40f, .55f, .85f, .38f, .38f, .35f };
    public static readonly float[] ValueMin = { .85f, .90f, .88f, .92f, .90f, .92f, .92f };
    public const int Worlds = 7;

    public static int Slot(int world) => Mathf.Clamp(world, 0, Worlds - 1);

    // The pink family of a boss / kit cell: ANY visibly pinkish pixel -- hue 280..352 at a chroma of at least 9/255 and not dark (saturated
    // pink AND the pale pinkish-white / lavender-white of rims and specks), or the hot pink of the boss beams that sits in the player's
    // red band (hue 352..360 / 0..28, saturated). Neutral whites are untouched. Same mask as recolor_nopink.py.
    public static bool IsPink(Color c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);
        float d = h * 360f;
        if (c.a <= 0f || v <= .25f) return false;
        if (s * v >= .035f && d >= 280f && d < 352f) return true;
        return s > .15f && (d >= 352f || d < 28f);
    }

    public static Color Remap(Color c, int world)
    {
        int k = Slot(world);
        Color.RGBToHSV(c, out float h, out float s, out float v);
        float sat = Mathf.Min(Mathf.Max(s * .8f, .10f), SatCap[k]) * (v < .95f ? 1f : .75f);
        var m = Color.HSVToRGB(Hue[k] / 360f, sat, Mathf.Max(v, ValueMin[k]));
        m.a = c.a;
        return m;
    }

    // The boss "WARNING" banner (BossArt.Warning, painted hot pink) in a world's own cue colour: the pink stripes become the world's saturated
    // cue (WorldCue.Body), the cream lettering and navy plate stay. Space keeps the painted banner. Cached per world.
    static readonly Dictionary<int, Sprite> banners = new Dictionary<int, Sprite>();
    public static Sprite Banner(Sprite warning, int world)
    {
        if (warning == null || world <= 0) return warning;
        int k = Slot(world);
        if (banners.TryGetValue(k, out var cached) && cached != null && cached.texture != null) return cached;
        int w, h;
        var px = ShieldContour.ReadPixels(warning, out w, out h);
        if (px == null) return warning;
        float hue = WorldCue.Hue[k] / 360f;
        for (int i = 0; i < px.Length; i++)
        {
            if (!IsPink(px[i])) continue;
            Color.RGBToHSV(px[i], out _, out float s, out float v);
            var m = Color.HSVToRGB(hue, Mathf.Min(s, WorldCue.BodySat[k]), v);
            m.a = px[i].a / 255f;
            px[i] = m;
        }
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.name = "BossWarningCue" + k;
        tex.filterMode = warning.texture != null ? warning.texture.filterMode : FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.hideFlags = HideFlags.HideAndDontSave;
        tex.SetPixels32(px);
        tex.Apply(false, false);
        var sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), warning.pixelsPerUnit, 0, SpriteMeshType.FullRect);
        sp.name = tex.name;
        sp.hideFlags = HideFlags.HideAndDontSave;
        banners[k] = sp;
        return sp;
    }

    // A copy of `cell` with every pink-family pixel (and the player-red band) turned to the world's cue colour. (None: the cell itself.)
    public static Sprite Recolour(Sprite cell, int world)
    {
        if (cell == null) return null;
        int w, h;
        var px = ShieldContour.ReadPixels(cell, out w, out h);
        if (px == null) return cell;
        bool changed = false;
        for (int i = 0; i < px.Length; i++)
        {
            if (!IsPink(px[i])) continue;
            px[i] = Remap(px[i], world);
            changed = true;
        }
        if (!changed) return cell;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.name = cell.name + "MineCue";
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

    static Sprite[] All(Sprite[] a, int world)
    {
        var o = new Sprite[a.Length];
        for (int i = 0; i < a.Length; i++) o[i] = Recolour(a[i], world);
        return o;
    }

    // The world's painted laser kit with its pink removed (same cells, same measurements).
    public static AttackArt.LaserArt Kit(AttackArt.LaserArt k, int world)
    {
        var o = new AttackArt.LaserArt { bodyOpaquePx = k.bodyOpaquePx, impactBasePx = k.impactBasePx };
        Copy(All(k.windup, world), o.windup); Copy(All(k.muzzle, world), o.muzzle); Copy(All(k.fade, world), o.fade); Copy(All(k.impact, world), o.impact);
        Copy(All(k.spark, world), o.spark); Copy(All(k.body, world), o.body); Copy(All(k.sight, world), o.sight); Copy(All(k.lockOn, world), o.lockOn);
        return o;
    }

    static void Copy(Sprite[] from, Sprite[] to) { for (int i = 0; i < to.Length; i++) to[i] = from[i]; }
}

public sealed class MineLaserArt
{
    public Sprite sight, beam0, beam1;
    public readonly Sprite[] flash = new Sprite[BossAttackFx.FlashFrames];
    public readonly Sprite[] spark = new Sprite[2];
    public AttackArt.LaserArt kit;   // Space (when a world's atlas is missing: Frost / Verdant too): the painted laser kit (AttackArt.BossLaser), pink removed by MineCue; sight / beam0 / beam1 / flash / spark below are its first cells
    public MineFlameArt flame;   // Ember's flame-thrower or a world's painted mine beam (Frost, Verdant, Tide, Storm; MineFlameArt); sight / beam0 / beam1 / flash / spark above are its first cells

    // preview / tests only: Ember's mine draws the boss-cell beam like every other world (the before-picture)
    public static bool NoFlame;
    // preview / tests only: Space / Frost / Verdant draw the recoloured boss-cell beam (the before-picture)
    public static bool NoKit;
    public static void ResetCache() { for (int i = 0; i < worlds.Length; i++) worlds[i] = null; }
    public const int EmberWorld = 3;   // the flame-thrower; Frost / Verdant / Tide / Storm wear their own minebeam atlas, the rest the kit / boss-cell beam in their cue colour

    static readonly MineLaserArt[] worlds = new MineLaserArt[MineCue.Worlds];

    public static MineLaserArt For(int world)
    {
        int slot = MineCue.Slot(world);
        world = Mathf.Clamp(world, 0, 3);
        var a = worlds[slot];
        if (a != null && a.beam0 != null && a.flash[0] != null) return a;
        a = new MineLaserArt();
        // Ember's flame-thrower, and the painted beam of Frost (ice), Verdant (thorned vine), Tide (water jet) and Storm (lightning):
        // one MineFlameArt-shaped atlas each, drawn by the same pilot / beam / haze / burst / impact code
        var fl = NoFlame ? null : (slot == EmberWorld || AttackArt.HasMineBeam(slot) ? MineFlameArt.ForWorld(slot) : null);
        if (fl != null)
        {
            a.flame = fl;
            a.sight = fl.sight;
            a.beam0 = fl.beam[0]; a.beam1 = fl.beam[1];
            a.flash[0] = fl.flash[0]; a.flash[1] = fl.flash[1];
            a.spark[0] = fl.spark[0]; a.spark[1] = fl.spark[1];
            worlds[slot] = a;
            return a;
        }
        var laser = slot <= 2 && !NoKit ? AttackArt.BossLaser(slot) : null;
        if (laser != null)
        {
            laser = MineCue.Kit(laser, slot);
            a.kit = laser;
            a.sight = laser.sight[0];
            a.beam0 = laser.body[0]; a.beam1 = laser.body[1];
            for (int i = 0; i < a.flash.Length; i++) a.flash[i] = laser.muzzle[Mathf.Min(i, laser.muzzle.Length - 1)];
            a.spark[0] = laser.spark[0]; a.spark[1] = laser.spark[1];
            worlds[slot] = a;
            return a;
        }
        var boss = BossCatalog.ForWorld(world);
        a.sight = MineCue.Recolour(BossArt.Shot(boss, BossArt.Telegraph), slot);
        a.beam0 = MineCue.Recolour(BossArt.Shot(boss, BossArt.Beam0), slot);
        a.beam1 = MineCue.Recolour(BossArt.Shot(boss, BossArt.Beam0 + 1), slot);
        for (int i = 0; i < a.flash.Length; i++) a.flash[i] = MineCue.Recolour(BossAttackFx.Get(boss, BossAttackFx.Flash0 + i), slot);
        a.spark[0] = MineCue.Recolour(BossAttackFx.Get(boss, BossAttackFx.Spark0 + 1), slot);
        a.spark[1] = MineCue.Recolour(BossAttackFx.Get(boss, BossAttackFx.Spark0 + 2), slot);
        worlds[slot] = a;
        return a;
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
