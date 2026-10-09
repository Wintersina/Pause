using System.Collections.Generic;
using UnityEngine;

// Boss projectiles, lasers and rail sparks, pooled and bounded.
//
// Damage goes through the game's existing rules, untouched: each projectile
// and each live laser carries a child "hitbox" tagged "Enimey" with a
// trigger collider, so collisionDetection treats a hit exactly like any
// enemy -- a life lost (or death on the last), or absorbed and destroyed
// under the blue atom's shield. The same tag makes the hitboxes hazards for
// the ultimate and the pause-teleport (which erases them; a shot pays
// ScoreRules.BossShot through its "BossShotHit" name, a beam nothing).
// Whatever destroys a hitbox only destroys that child: the pooled visual
// notices on its next step and recycles, and the next launch builds a fresh
// hitbox. Nothing else is allocated per frame; the encounter steps every
// live object from one loop.
//
// Movement uses the world's time (dt = real dt * timeScale), so a frozen
// world (finger up, the intro, death) freezes every shot, laser and spark.
public static class BossHitbox
{
    public const string Tag = "Enimey";

    public static GameObject Circle(Transform parent, string name, float radius)
    {
        var go = Child(parent, name);
        var col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = radius;
        return go;
    }

    public static GameObject Box(Transform parent, string name, Vector2 size)
    {
        var go = Child(parent, name);
        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = size;
        return go;
    }

    static GameObject Child(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.tag = Tag;
        go.transform.SetParent(parent, false);
        return go;
    }
}

// A shot. Flies straight (or, lobbed, under gravity to a capped fall speed)
// and meets the side rails as its attack says (BossRailMode): ricochets
// (mirrored off the rail's inner face, a spark, a bounce spent), splashes
// (a spark, gone) or flies on past.
public class BossProjectile : MonoBehaviour, IHostileShot
{
    SpriteRenderer sr, glow;     // glow: the rim outlining the art (BossArt.ShotRim); never a round halo
    bool rimmed;                 // false: the rim could not be built, the shot goes without (no round wrapper)
    Sprite rim0, rim1;           // the rim for each of the shot's two drawings, looked up once per launch
    float glowBase;              // its local scale before the pulse
    Color glowTint;
    GameObject hitbox;
    BossDef boss;
    BossShotStyle style;
    BossProjectilePool pool;
    Vector2 velocity;
    Vector3 launchedAt;
    float age, radius, gravity, fallSpeed;
    BossRailMode rail;
    int bouncesLeft;
    float viewTop = 5f, viewBottom = -5f;     // read once per shot, not per frame

    public bool Active { get; private set; }
    public Vector2 Velocity => velocity;
    public GameObject Hitbox => hitbox;
    public BossShotStyle Style => style;
    public BossRailMode Rail => rail;
    public int BouncesLeft => bouncesLeft;
    public int Bounces { get; private set; }
    public float Radius => radius;
    public Vector3 LaunchedAt => launchedAt;
    // Why it last left play (tests): 0 none, 1 off screen, 2 splashed on a
    // rail, 3 hitbox destroyed (hit the ship, shot down, erased), 4 hit a
    // hazard (friendly fire), 5 broken by another shot or a player shot.
    public int EndReason { get; private set; }
    public SpriteRenderer Glow => glow;
    public float Age => age;

    // IHostileShot (HostileShots: shot vs shot)
    public bool ShotCollidable => Active && hitbox != null;
    public Vector2 ShotPosition => transform.position;
    public float ShotRadius => radius;
    public int ShotOwner => pool != null ? pool.OwnerId : 0;
    public float ShotAge => age;
    public int ShotMass => HostileShots.Light;
    public Color ShotTint => boss != null ? boss.flash : Color.white;

    public void ShotPop(Vector2 at)
    {
        if (!Active) return;
        if (pool != null) pool.Spark(boss, at);
        EndReason = 5;
        Recycle();
    }

    void OnDestroy() { HostileShots.Unregister(this); }

    public static BossProjectile Create(Transform root, BossProjectilePool owner)
    {
        var go = new GameObject("BossShot");
        go.transform.SetParent(root, false);
        var p = go.AddComponent<BossProjectile>();
        p.pool = owner;
        p.sr = go.AddComponent<SpriteRenderer>();
        p.sr.sortingOrder = 30;
        p.glow = HostileGlow.Attach(go.transform, HostileGlow.SortBehindShots);   // carries only the rim sprite
        HostileShots.Register(p);
        go.SetActive(false);
        return p;
    }

    public void Launch(BossDef def, BossShotStyle shotStyle, Vector3 at, Vector2 v,
                       BossRailMode railMode, int bounces, float gravityPull, float maxFall)
    {
        boss = def;
        style = shotStyle;
        velocity = v;
        rail = railMode;
        bouncesLeft = railMode == BossRailMode.Bounce ? Mathf.Max(0, bounces) : 0;
        Bounces = 0;
        gravity = Mathf.Max(0f, gravityPull);
        fallSpeed = maxFall > 0f ? maxFall : float.MaxValue;
        age = 0f;
        EndReason = 0;
        viewTop = CameraFit.ViewTop;
        viewBottom = CameraFit.ViewBottom;
        launchedAt = new Vector3(at.x, at.y, 0f);
        transform.position = launchedAt;
        float size = style == BossShotStyle.Bolt ? BossConfig.BoltWorldSize : BossConfig.ShardWorldSize;
        transform.localScale = Vector3.one * size;
        Face();
        radius = style == BossShotStyle.Bolt ? BossConfig.BoltHitRadius : BossConfig.ShardHitRadius;
        if (hitbox == null)
        {
            // Collider radius is in local units; the root is scaled to the art size.
            hitbox = BossHitbox.Circle(transform, "BossShotHit", radius / size);
        }
        else
        {
            var col = hitbox.GetComponent<CircleCollider2D>();
            if (col != null) col.radius = radius / size;
        }
        // The glow: a light rim hugging the drawing's silhouette (BossArt.
        // ShotRim, in the art's own units, so scale 1). The art is drawn at
        // the size it always was (the root's scale), the hitbox unchanged.
        // If the rim could not be built the shot has none: boss shots never
        // wear HostileGlow's round halo.
        rim0 = BossArt.ShotRim(boss, FirstCell);
        rim1 = BossArt.ShotRim(boss, FirstCell + 1);
        rimmed = rim0 != null && rim1 != null;
        glow.sprite = rimmed ? rim0 : null;
        glow.enabled = rimmed;
        glowBase = 1f;
        // the rim in the hostile family (HostileShotPalette): whatever the
        // boss's painted colours, every hostile shot carries a magenta-pink
        // edge and never an atom's hue
        glowTint = HostileGlow.Tint(HostileShotPalette.Body(boss != null ? boss.flash : Color.white));
        Pulse();
        Active = true;
        gameObject.SetActive(true);
        sr.sprite = BossArt.Shot(boss, FirstCell);
    }

    void Pulse()
    {
        float breath = BossArt.ShotRimPulseScale;
        glow.transform.localScale = Vector3.one * (glowBase * HostileGlow.PulseScaleAt(age, breath));
        var c = glowTint;
        c.a = HostileGlow.PulseAlphaAt(age);
        glow.color = c;
    }

    // Art points down the screen; turn it to face along its velocity.
    void Face()
    {
        if (velocity.sqrMagnitude < 1e-6f) return;
        float deg = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg + 90f;
        transform.rotation = Quaternion.Euler(0f, 0f, deg);
    }

    int FirstCell => style == BossShotStyle.Bolt ? BossArt.Bolt0 : BossArt.Shard0;

    public void Step(float dt)
    {
        if (!Active) return;
        // The player or the ultimate destroyed the hitbox: this shot is spent.
        if (hitbox == null) { EndReason = 3; Recycle(); return; }
        if (dt <= 0f) return;

        age += dt;
        if (gravity > 0f)
        {
            velocity.y = Mathf.Max(velocity.y - gravity * dt, -fallSpeed);
            Face();
        }
        Vector3 p = transform.position;
        p.x += velocity.x * dt;
        p.y += velocity.y * dt;

        // The rails' inner faces: the shot's edge touching one, moving into it.
        if (rail != BossRailMode.Pass)
        {
            float edge = BossRails.InnerEdge - radius;
            int side = p.x > edge && velocity.x > 0f ? 1 : p.x < -edge && velocity.x < 0f ? -1 : 0;
            if (side != 0)
            {
                Vector3 contact = new Vector3(side * BossRails.InnerEdge, p.y, 0f);
                if (pool != null) pool.Spark(boss, contact);
                if (bouncesLeft > 0)
                {
                    bouncesLeft--;
                    Bounces++;
                    p.x = side * edge - (p.x - side * edge);   // mirrored off the face
                    velocity.x = -velocity.x;
                    Face();
                }
                else
                {
                    transform.position = new Vector3(side * edge, p.y, 0f);
                    EndReason = 2;
                    Recycle();
                    return;
                }
            }
        }
        transform.position = p;
        int frame = BossArt.FrameAt(BossArt.ShotTicks, age, true);
        sr.sprite = BossArt.Shot(boss, FirstCell + frame);
        if (rimmed) glow.sprite = frame == 0 ? rim0 : rim1;   // the rim follows the drawing
        Pulse();

        // Friendly fire: a rock, an enemy, a mine or an elite in its way
        // takes the hit (unpaid; FriendlyFire) and the shot is spent.
        if (HitsHazard(p)) { EndReason = 4; Recycle(); return; }

        // off screen: past the view's edge (it grows on tall screens), never
        // nearer than the authored -6.5 / 7.5
        if (p.y < Mathf.Min(-6.5f, viewBottom - 1f) || p.y > Mathf.Max(7.5f, viewTop + 1f) || Mathf.Abs(p.x) > 4.2f)
        {
            EndReason = 1;
            Recycle();
        }
    }

    bool HitsHazard(Vector3 p)
    {
        if (DeathCrash.Running) return false;
        var live = ClearTarget.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (t == null || !t.isActiveAndEnabled) continue;
            float R = radius + t.Radius * FriendlyFire.HostileFireReach;
            Vector3 d = t.transform.position - p;
            if (d.x * d.x + d.y * d.y > R * R) continue;
            // (the boss itself is Immune; a target just come in is protected)
            if (!FriendlyFire.HostileFireCanHit(t, null)) continue;
            if (FriendlyFire.HostileHit(t, p, "boss shot")) return true;   // else the frame's kill cap: next frame
        }
        return false;
    }

    public void Recycle()
    {
        Active = false;
        gameObject.SetActive(false);
    }
}

// A laser out of a body part. While its attack's tell runs, a thin
// blinking sight line grows out of the part and scans the arc the beam will
// sweep; then the beam itself grows out of the part (BossConfig.BeamGrowSpeed
// -- it never pops in at full length) behind a muzzle flash, sweeps by
// sweepDeg over its hold, thins out and is gone. Its root follows the part
// on the boss every frame (drift, bob, the current drawing). It ends where
// it meets a side rail -- sparking there -- or past the bottom of the view.
// The hitbox only exists while the beam is live, so the sight line never
// hurts.
public class BossBeam : MonoBehaviour
{
    SpriteRenderer sight, beam, flash, impact, sheath;   // sheath: the glow along its length (HostileGlow)
    Color sheathTint;
    BossProjectilePool pool;
    GameObject hitbox;
    BoxCollider2D box;
    BossDef boss;
    BossActor owner;
    int part;
    float startDeg, sweepDeg, tellLeft, tellTotal, holdLeft, holdTotal, fadeLeft, age, width;
    float length;            // current drawn length
    float reach;             // to the rail or past the view's bottom, this frame
    bool live, fading, railHit;
    Vector3 origin;

    public bool Active { get; private set; }
    public bool Live => Active && live && !fading;
    public bool Telegraphing => Active && !live;
    public GameObject Hitbox => hitbox;
    public Vector3 Origin => origin;
    public float Length => length;
    public float Reach => reach;
    public float Angle { get; private set; }
    public float Width => width;
    public bool EndsOnRail => railHit;
    public int Part => part;
    public BossActor Owner => owner;
    public Vector2 Direction => Heading(Angle);
    public float X => origin.x;
    public SpriteRenderer Sheath => sheath;
    public int OwnerId => pool != null ? pool.OwnerId : 0;
    public float LiveAge => age;   // seconds since it ignited (once live)

    // Does a circle at p (radius r) touch the live beam's hitbox?
    public bool Touches(Vector2 p, float r)
    {
        if (!Live) return false;
        Vector2 o = origin, d = Direction;
        float t = Mathf.Clamp(Vector2.Dot(p - o, d), 0f, length);
        float half = width * BossConfig.BeamHitFraction * .5f + r;
        return (o + d * t - p).sqrMagnitude < half * half;
    }

    void OnDestroy() { HostileShots.Unregister(this); }

    public static BossBeam Create(Transform root, BossProjectilePool owner = null)
    {
        var go = new GameObject("BossBeam");
        go.transform.SetParent(root, false);
        var b = go.AddComponent<BossBeam>();
        b.pool = owner;
        b.sheath = HostileGlow.Attach(go.transform, HostileGlow.SortBehindBeam, beam: true);
        b.sheath.enabled = false;
        HostileShots.Register(b);
        b.sight = Piece(go.transform, "Sight", 24);
        b.beam = Piece(go.transform, "Beam", 26);
        b.flash = Piece(go.transform, "Flash", 31);
        b.impact = Piece(go.transform, "Impact", 31);
        go.SetActive(false);
        return b;
    }

    static SpriteRenderer Piece(Transform parent, string name, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = order;
        return sr;
    }

    // `from` is used when there is no owner (tests, previews); with one the
    // root is the owner's part, re-read every frame.
    public void Begin(BossDef def, BossActor actor, int bodyPart, Vector3 from, float angleDeg, float sweep,
                      float tellSeconds, float holdSeconds, float beamWidth)
    {
        boss = def;
        owner = actor;
        part = bodyPart;
        origin = new Vector3(from.x, from.y, 0f);
        startDeg = angleDeg;
        sweepDeg = sweep;
        tellTotal = tellLeft = Mathf.Max(0f, tellSeconds);
        holdTotal = holdLeft = Mathf.Max(.05f, holdSeconds);
        fadeLeft = BossConfig.BeamFadeSeconds;
        width = Mathf.Max(.05f, beamWidth);
        age = 0f;
        length = 0f;
        live = fading = railHit = false;
        Angle = startDeg;
        if (hitbox != null) { BossUtil.Kill(hitbox); hitbox = null; box = null; }

        sight.sprite = BossArt.Shot(boss, BossArt.Telegraph);
        beam.sprite = BossArt.Shot(boss, BossArt.Beam0);
        flash.sprite = BossAttackFx.Get(boss, BossAttackFx.Flash0);
        impact.sprite = BossAttackFx.Get(boss, BossAttackFx.Spark0);
        sight.enabled = true;
        beam.enabled = flash.enabled = impact.enabled = false;
        sheath.enabled = false;
        sheathTint = HostileGlow.Tint(boss != null ? boss.flash : Color.white);

        Active = true;
        gameObject.SetActive(true);
        Place(0f);
    }

    public static Vector2 Heading(float deg)
    {
        float r = deg * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
    }

    // How far a line from `from` along `dir` runs before a side rail's inner
    // face or the bottom of the view (just past it, lower on a tall screen).
    public static float ReachFrom(Vector3 from, Vector2 dir, out bool railHit)
    {
        float edge = BossRails.InnerEdge;
        float bottom = Mathf.Min(-5.6f, CameraFit.ViewBottom - .6f);
        float best = 30f;
        railHit = false;
        if (Mathf.Abs(dir.x) > 1e-4f)
        {
            float t = (Mathf.Sign(dir.x) * edge - from.x) / dir.x;
            if (t > 0f && t < best) { best = t; railHit = true; }
        }
        if (dir.y < -1e-4f)
        {
            float t = (bottom - from.y) / dir.y;
            if (t > 0f && t < best) { best = t; railHit = false; }
        }
        return Mathf.Max(0f, best);
    }

    public void Step(float dt)
    {
        if (!Active) return;
        // Shot down, rammed under a shield or erased by a blink: gone.
        if (live && !fading && hitbox == null) { Recycle(); return; }
        if (dt <= 0f) return;
        age += dt;

        if (!live)
        {
            tellLeft -= dt;
            // The sight line scans the arc the beam will sweep, so the tell
            // shows where it will burn and which way it will go.
            float scan = BossConfig.BeamScanSeconds > 0f ? Mathf.Repeat(age, BossConfig.BeamScanSeconds) / BossConfig.BeamScanSeconds : 0f;
            Angle = startDeg + sweepDeg * scan;
            if (tellLeft <= 0f) { Ignite(); return; }
            Place(dt);
            return;
        }

        if (!fading)
        {
            holdLeft -= dt;
            Angle = startDeg + sweepDeg * Mathf.Clamp01(1f - holdLeft / holdTotal);
            if (holdLeft <= 0f) BeginFade();
        }
        else
        {
            fadeLeft -= dt;
            if (fadeLeft <= 0f) { Recycle(); return; }
        }
        Place(dt);
    }

    void Ignite()
    {
        burned.NewPulse();
        live = true;
        age = 0f;
        length = 0f;
        Angle = startDeg;
        sight.enabled = false;
        beam.enabled = flash.enabled = true;
        sheath.enabled = true;
        hitbox = BossHitbox.Box(transform, "BossBeamHit", new Vector2(width * BossConfig.BeamHitFraction, .01f));
        box = hitbox.GetComponent<BoxCollider2D>();
        Place(0f);
    }

    void BeginFade()
    {
        fading = true;
        if (hitbox != null) { BossUtil.Kill(hitbox); hitbox = null; box = null; }
    }

    // Root at the part, pointing along Angle, `length` grown towards `reach`.
    void Place(float dt)
    {
        if (owner != null && part >= 0) origin = owner.Emitter(part);
        Vector2 dir = Heading(Angle);
        reach = ReachFrom(origin, dir, out railHit);
        // (x ShotScale: it reaches the ship's rows as soon on every screen)
        float grow = BossConfig.BeamGrowSpeed * BossConfig.ShotScale * (live ? 1f : 2f);
        length = Mathf.Min(reach, length + grow * dt);
        if (dt <= 0f && !live) length = Mathf.Min(length, reach);

        transform.position = origin;
        // local +y runs down the beam
        transform.rotation = Quaternion.Euler(0f, 0f, Angle - 90f);
        transform.localScale = Vector3.one;

        if (!live)
        {
            int blink = Mathf.FloorToInt(age / (BossArt.TelegraphBlinkTicks * BossArt.Tick));
            sight.enabled = blink % 2 == 0 && length > 0f;
            Span(sight.transform, width * BossConfig.BeamSightWidth, length);
            return;
        }

        float w = width;
        if (fading) w *= Mathf.Clamp01(fadeLeft / Mathf.Max(.01f, BossConfig.BeamFadeSeconds));
        else
        {
            // a hot flicker on twos, never a smooth pulse
            int step = Mathf.FloorToInt(age / (2f * BossArt.Tick));
            w *= step % 2 == 0 ? 1f : .85f;
        }
        Span(beam.transform, w, length);
        // the sheath: the same wrapper as a shot, along the beam's length
        sheath.transform.localPosition = new Vector3(0f, length * .5f, 0f);
        sheath.transform.localScale = new Vector3(HostileGlow.DiameterFor(w * HostileGlow.BeamBody) * HostileGlow.PulseScaleAt(age),
                                                  Mathf.Max(.001f, length) / HostileGlow.SheathHeight, 1f);
        var sc = sheathTint;
        sc.a = HostileGlow.PulseAlphaAt(age);
        sheath.color = sc;
        beam.sprite = BossArt.Shot(boss, BossArt.Beam0 + BossArt.FrameAt(BossArt.BeamTicks, age, true));

        int f = BossArt.FrameAt(BossAttackFx.FlashTickTable, age, true);
        flash.sprite = BossAttackFx.Get(boss, BossAttackFx.Flash0 + Mathf.Min(f, BossAttackFx.FlashFrames - 1));
        flash.transform.localPosition = Vector3.zero;
        flash.transform.localScale = Vector3.one * BossConfig.BeamFlashSize * (fading ? .6f : 1f);

        bool touching = railHit && length >= reach - 1e-3f;
        impact.enabled = touching && !fading;
        if (impact.enabled)
        {
            int s = Mathf.FloorToInt(age / BossArt.Tick) % 2;
            impact.sprite = BossAttackFx.Get(boss, BossAttackFx.Spark0 + 1 + s);
            impact.transform.localPosition = new Vector3(0f, length, 0f);
            impact.transform.localScale = Vector3.one * BossConfig.RailSparkSize;
        }

        if (box != null)
        {
            box.size = new Vector2(width * BossConfig.BeamHitFraction, Mathf.Max(.01f, length));
            box.offset = new Vector2(0f, length * .5f);
            BurnHazards();
        }
    }

    // Friendly fire: everything the live beam crosses -- rocks, enemies,
    // mines -- is destroyed (unpaid), an elite loses a heart: each target
    // once per pulse (one ignition), the frame's kill cap permitting
    // (FriendlyFire.HostileBeam).
    readonly FriendlyFire.BeamHits burned = new FriendlyFire.BeamHits();

    void BurnHazards()
    {
        if (DeathCrash.Running || length <= 0f) return;
        FriendlyFire.HostileBeam(burned, origin, Direction, length, width * BossConfig.BeamHitFraction * .5f, null, "boss laser");
    }

    static void Span(Transform t, float w, float len)
    {
        t.localPosition = new Vector3(0f, len * .5f, 0f);
        t.localScale = new Vector3(w, Mathf.Max(.001f, len), 1f);
    }

    public void Recycle()
    {
        Active = false;
        live = fading = false;
        if (sheath != null) sheath.enabled = false;
        if (hitbox != null) { BossUtil.Kill(hitbox); hitbox = null; box = null; }
        gameObject.SetActive(false);
    }
}

// The ping a shot makes on a rail: three hard frames, then gone.
public class BossSpark : MonoBehaviour
{
    SpriteRenderer sr;
    BossDef boss;
    float age;

    public bool Active { get; private set; }

    public static BossSpark Create(Transform root)
    {
        var go = new GameObject("BossSpark");
        go.transform.SetParent(root, false);
        var s = go.AddComponent<BossSpark>();
        s.sr = go.AddComponent<SpriteRenderer>();
        s.sr.sortingOrder = 32;
        go.SetActive(false);
        return s;
    }

    public void Play(BossDef def, Vector3 at, float size)
    {
        boss = def;
        age = 0f;
        transform.position = new Vector3(at.x, at.y, 0f);
        transform.localScale = Vector3.one * size;
        sr.sprite = BossAttackFx.Get(boss, BossAttackFx.Spark0);
        Active = true;
        gameObject.SetActive(true);
    }

    public void Step(float dt)
    {
        if (!Active || dt <= 0f) return;
        age += dt;
        int f = BossArt.FrameAt(BossAttackFx.SparkTicks, age, false);
        if (f >= BossAttackFx.SparkFrames) { Recycle(); return; }
        sr.sprite = BossAttackFx.Get(boss, BossAttackFx.Spark0 + f);
    }

    public void Recycle()
    {
        Active = false;
        gameObject.SetActive(false);
    }
}

public sealed class BossProjectilePool
{
    readonly List<BossProjectile> shots = new List<BossProjectile>();
    readonly List<BossBeam> beams = new List<BossBeam>();
    readonly List<BossSpark> sparks = new List<BossSpark>();
    readonly int maxShots, maxBeams, maxSparks;
    GameObject root;

    public BossProjectilePool(int maxShots, int maxBeams)
    {
        this.maxShots = Mathf.Max(1, maxShots);
        this.maxBeams = Mathf.Max(1, maxBeams);
        maxSparks = Mathf.Max(1, BossConfig.SparkPoolMax);
        root = new GameObject("~BossProjectiles");
        OwnerId = root.GetInstanceID();
    }

    public int Capacity => maxShots;
    // Who fired, for the shot-vs-shot volley rule (HostileShots).
    public int OwnerId { get; }
    public int BeamCapacity => maxBeams;
    public int Created => shots.Count;
    public int BeamsCreated => beams.Count;
    public int SparksCreated => sparks.Count;
    public int SparksPlayed { get; private set; }
    public IReadOnlyList<BossProjectile> Shots => shots;
    public IReadOnlyList<BossBeam> Beams => beams;

    public int ActiveShots
    {
        get
        {
            int n = 0;
            for (int i = 0; i < shots.Count; i++) if (shots[i] != null && shots[i].Active) n++;
            return n;
        }
    }

    public int ActiveBeams
    {
        get
        {
            int n = 0;
            for (int i = 0; i < beams.Count; i++) if (beams[i] != null && beams[i].Active) n++;
            return n;
        }
    }

    // A plain shot that flies past the rails (tests).
    public BossProjectile Fire(BossDef boss, BossShotStyle style, Vector3 at, Vector2 velocity) =>
        Fire(boss, style, at, velocity, BossRailMode.Pass, 0, 0f, 0f);

    // Null when the pool is spent: a full screen simply skips a shot rather
    // than growing without bound.
    public BossProjectile Fire(BossDef boss, BossShotStyle style, Vector3 at, Vector2 velocity,
                               BossRailMode rail, int bounces, float gravity, float fallSpeed)
    {
        BossProjectile p = null;
        for (int i = 0; i < shots.Count; i++)
            if (shots[i] != null && !shots[i].Active) { p = shots[i]; break; }
        if (p == null)
        {
            if (shots.Count >= maxShots || root == null) return null;
            p = BossProjectile.Create(root.transform, this);
            shots.Add(p);
        }
        p.Launch(boss, style, at, velocity, rail, bounces, gravity, fallSpeed);
        return p;
    }

    public BossBeam Beam(BossDef boss, BossActor owner, int part, Vector3 from, float angleDeg, float sweepDeg,
                         float tell, float hold, float width)
    {
        BossBeam b = null;
        for (int i = 0; i < beams.Count; i++)
            if (beams[i] != null && !beams[i].Active) { b = beams[i]; break; }
        if (b == null)
        {
            if (beams.Count >= maxBeams || root == null) return null;
            b = BossBeam.Create(root.transform, this);
            beams.Add(b);
        }
        b.Begin(boss, owner, part, from, angleDeg, sweepDeg, tell, hold, width);
        return b;
    }

    public void Spark(BossDef boss, Vector3 at)
    {
        SparksPlayed++;
        BossSpark s = null;
        for (int i = 0; i < sparks.Count; i++)
            if (sparks[i] != null && !sparks[i].Active) { s = sparks[i]; break; }
        if (s == null)
        {
            if (sparks.Count >= maxSparks || root == null) return;
            s = BossSpark.Create(root.transform);
            sparks.Add(s);
        }
        s.Play(boss, at, BossConfig.RailSparkSize);
    }

    public void Step(float dt)
    {
        if (dt > 0f) FriendlyFire.HostileStep();
        for (int i = 0; i < shots.Count; i++) if (shots[i] != null) shots[i].Step(dt);
        for (int i = 0; i < beams.Count; i++) if (beams[i] != null) beams[i].Step(dt);
        for (int i = 0; i < sparks.Count; i++) if (sparks[i] != null) sparks[i].Step(dt);
        if (dt > 0f) HostileShots.Resolve();   // shot vs shot, lasers burning shots
    }

    public void RecycleAll()
    {
        for (int i = 0; i < shots.Count; i++) if (shots[i] != null && shots[i].Active) shots[i].Recycle();
        for (int i = 0; i < beams.Count; i++) if (beams[i] != null && beams[i].Active) beams[i].Recycle();
        for (int i = 0; i < sparks.Count; i++) if (sparks[i] != null && sparks[i].Active) sparks[i].Recycle();
    }

    public void Dispose()
    {
        if (root != null) BossUtil.Kill(root);
        root = null;
        shots.Clear();
        beams.Clear();
        sparks.Clear();
    }

    public GameObject Root => root;
}
