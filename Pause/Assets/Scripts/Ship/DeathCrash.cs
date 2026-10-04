using System.Collections.Generic;
using UnityEngine;

// The ship's death: on the fatal hit the hull breaks into 3-6 fragments that
// spin and tumble on smoking, sparking arcs and slam into the side rails, and
// only once the last piece has hit a rail (and a short settle) does the
// Flight Complete panel come up.
//
// Fragments are cut from the hull's own sprite at the moment of death (the
// critical, wrecked frame of whatever skin is flown) -- no extra art. The
// cut is a Voronoi split of the drawing's pixels around seeds scattered
// inside the hull's baked body polygon (ShipHitbox), with jagged crack
// lines, so every piece carries a real part of the hull; how many pieces
// (3-6) comes from that outline (a spiky, stretched hull breaks into more
// than a compact one) plus a coin flip, and the seeds differ between deaths
// (Variants). The hull sheets aren't CPU-readable, so the drawing is copied
// through the GPU once (ReadableCopy); each piece is a small texture of its
// own pixels, pivoted at its centre of mass so it spins about it. The sets
// are cached per hull drawing and variant.
//
//   0          the fatal hit: a blast on the hull, the world freezes as it
//              always has on death (playerDied: spawns, scroll, the boss,
//              the score all stop), the hull is hidden and the pieces take
//              its place, flashing, the camera kicks
//   HitStop    the pieces fly, staggered (LaunchSpread): a quadratic arc
//              up and over to a random spot (side per piece, y anywhere on
//              screen) on the left or right rail, spinning, trailing smoke
//              and sparks (the DamageFx atlas)
//   landing    each piece slams into its rail: a blast at the contact point,
//              sparks and scrap off the wall, a camera shake; it crumples
//              against the wall, sticks, then fades
//   + Settle   after the last landing: the ship is gone (Destroy), the panel
//              shows (PanelReady; buttonClicks / tutButtonClicks /
//              playerIsDead wait for it)
//
// The thing that killed the ship -- an enemy craft, alien, rock or rail mine
// (Classify: Physical) -- tumbles into the opposite rail with its own blast.
// A boss shot or beam (Projectile) just dissipates as before; the boss body
// (BossBody) stays where it is, frozen with the aborted fight.
//
// The companion drone (UltimateGun) is knocked loose and follows the main
// hull chunk into the same rail. The last heart's shield / crumble
// (ShipLivesIndicator) and every blast (TargetExplosion.Delta) run on this
// sequence's clock while it plays, so they animate over the frozen world.
//
// Clock: unscaled time, each step clamped to MaxStep, so coming back from
// the background resumes where it was instead of jumping to the end. A fresh
// tap after SkipAfter skips straight to the panel. Everything is pooled (the
// pieces and a fixed particle pool, built once per scene); a frame
// allocates nothing.
[DefaultExecutionOrder(-60)]   // FrameDt is set before the hearts and the blasts read it
public class DeathCrash : MonoBehaviour
{
    // ---- timeline (seconds from the fatal hit) ----
    public const float HitStop = .15f;
    public const float LaunchSpread = .22f;
    public const float FlightMin = .52f, FlightMax = .9f;
    public const float MainDelay = .04f, MainFlightMin = .78f;
    public const float Settle = .35f;
    public const float SkipAfter = .4f;
    public const float MaxStep = 1f / 20f;
    public const float Crumple = .14f, StickHold = .45f, StickFade = .6f;
    // The tail after the panel shows: blasts and the fading wreckage finish.
    public const float Tail = 1.5f;
    // The whole sequence, fatal hit to panel, always lies in here.
    public const float MinTotal = 1.2f, MaxTotal = 1.8f;

    public const int MinFragments = 3, MaxFragments = 6, Variants = 3;
    public const int MaxPieces = MaxFragments + 2;   // + the drone + the killer
    public const int MaxParticles = 112;
    public const int SmokeOrder = 59, PieceOrder = 60, SparkOrder = 61;

    public enum KillerKind { None, Physical, Projectile, BossBody }
    public enum PieceKind { Hull, Drone, Killer }

    public static DeathCrash Instance { get; private set; }

    // The sequence is playing: the panel waits.
    public static bool Running => Instance != null && Instance.running;
    public static bool PanelReady => !Running;
    // The sequence or its tail is on screen: hearts and blasts run on FrameDt.
    public static bool Animating => Instance != null && Instance.animating;
    // This frame's step of the sequence clock (0 when nothing plays).
    public static float FrameDt { get; private set; }
    // Edit-mode previews: play the blasts too (they tick them by hand).
    public static bool EditorBlasts;

    struct Piece
    {
        public SpriteRenderer sr;
        public Transform tf;
        public PieceKind kind;
        public bool active, landed, main;
        public Vector3 start, control, target, outward, baseScale;
        public float delay, dur, angle, spin, landedAt, emit, radius;
        public int side;
        public TargetExplosion.Kind blast;
    }

    struct Particle
    {
        public Vector3 pos, vel;
        public float age, life, size0, size1, angle, spin, gravity, drag;
        public bool alive;
        public Color tint;
    }

    readonly Piece[] pieces = new Piece[MaxPieces];
    readonly Particle[] particles = new Particle[MaxParticles];
    readonly SpriteRenderer[] particleRenderers = new SpriteRenderer[MaxParticles];
    readonly List<GameObject> hiddenChildren = new List<GameObject>();
    readonly List<Behaviour> disabled = new List<Behaviour>();
    SpriteRenderer hiddenHull;
    GameObject ship;
    int pieceCount, fragmentCount, shipId, nextParticle;
    bool built, running, finished, animating, skipped, wasPressed, killerCrashed;
    float elapsed, finishedAt, lastLanding, plannedEnd, shake, shakeClock, edge, viewTop, viewBottom;
    Vector3 shakeApplied;
    Camera shakeCam;
    KillerKind killerKind;

    // ---- read-outs (tests) ----
    public bool IsRunning => running;
    public bool Finished => finished;
    public bool Skipped => skipped;
    public float Elapsed => elapsed;
    public float FinishedAt => finishedAt;
    public float PlannedEnd => plannedEnd;
    public float LastLanding => lastLanding;
    public int PieceCount => pieceCount;
    public int FragmentCount => fragmentCount;
    public int Impacts { get; private set; }
    public KillerKind Killer => killerKind;
    public bool KillerCrashed => killerCrashed;
    public float RailEdge => edge;
    public float ViewTopAtStart => viewTop;
    public float ViewBottomAtStart => viewBottom;
    public GameObject Ship => ship;
    public PieceKind KindOf(int i) => pieces[i].kind;
    public bool Landed(int i) => pieces[i].landed;
    public bool PieceVisible(int i) => pieces[i].active && pieces[i].sr != null && pieces[i].sr.enabled;
    public int SideOf(int i) => pieces[i].side;
    public Vector3 TargetOf(int i) => pieces[i].target;
    public Vector3 PositionOf(int i) => pieces[i].tf != null ? pieces[i].tf.position : Vector3.zero;
    public Sprite SpriteOf(int i) => pieces[i].sr != null ? pieces[i].sr.sprite : null;
    public float RadiusOf(int i) => pieces[i].radius;
    public bool IsMain(int i) => pieces[i].main;
    public int LiveParticles
    {
        get { int n = 0; for (int i = 0; i < MaxParticles; i++) if (particles[i].alive) n++; return n; }
    }

    // ---- entry points ----

    public static DeathCrash Ensure()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("~DeathCrash");
        var crash = go.AddComponent<DeathCrash>();
        crash.Build();
        Instance = crash;
        return crash;
    }

    // collisionDetection's last life: plays the crash for `ship`, killed by
    // `killer` (the collider's object; null for none).
    public static DeathCrash Begin(GameObject ship, GameObject killer)
    {
        var crash = Ensure();
        crash.Play(ship, killer);
        return crash;
    }

    // What killed the ship: a physical hazard crashes into a rail too; a
    // boss shot or beam dissipates; the boss body stays.
    public static KillerKind Classify(GameObject hit)
    {
        if (hit == null) return KillerKind.None;
        string n = hit.name;
        if (n == "BossShotHit" || n == "BossBeamHit") return KillerKind.Projectile;
        if (n == "BossBody") return KillerKind.BossBody;
        if (hit.GetComponentInParent<BossProjectile>() != null || hit.GetComponentInParent<BossBeam>() != null)
            return KillerKind.Projectile;
        if (hit.GetComponentInParent<BossActor>() != null) return KillerKind.BossBody;
        if (hit.CompareTag("Enimey") || hit.CompareTag("Astr")) return KillerKind.Physical;
        return KillerKind.None;
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        Build();
    }

    void OnDestroy()
    {
        UndoShake();
        if (Instance == this) { Instance = null; FrameDt = 0f; }
    }

    // The pools: one renderer per piece, a fixed particle pool (idempotent).
    public void Build()
    {
        if (built) return;
        built = true;
        for (int i = 0; i < MaxPieces; i++)
        {
            var go = new GameObject("~CrashPiece" + i, typeof(SpriteRenderer));
            go.transform.SetParent(transform, false);
            var sr = go.GetComponent<SpriteRenderer>();
            sr.sortingOrder = PieceOrder;
            sr.enabled = false;
            pieces[i].sr = sr;
            pieces[i].tf = go.transform;
        }
        for (int i = 0; i < MaxParticles; i++)
        {
            var go = new GameObject("~CrashBit" + i, typeof(SpriteRenderer));
            go.transform.SetParent(transform, false);
            var sr = go.GetComponent<SpriteRenderer>();
            sr.enabled = false;
            particleRenderers[i] = sr;
        }
    }

    // ---- the sequence ----

    public void Play(GameObject deadShip, GameObject killer)
    {
        Build();
        if (running || deadShip == null) return;
        HideAll();
        elapsed = 0f;
        finishedAt = -1f;
        lastLanding = 0f;
        running = animating = true;
        finished = skipped = killerCrashed = false;
        Impacts = 0;
        pieceCount = fragmentCount = 0;
        wasPressed = TouchInput.IsPressed;   // the finger already down is not a skip

        ship = deadShip;
        shipId = ShipId.Of(ship, ShipId.Equipped());
        BossRails.Measure();
        edge = BossRails.InnerEdge;
        viewTop = CameraFit.ViewTop;
        viewBottom = CameraFit.ViewBottom;

        var shipTf = ship.transform;
        var hull = ship.GetComponent<SpriteRenderer>();
        Sprite sprite = hull != null && hull.sprite != null ? hull.sprite : ShipHullArt.Rest(shipId, 2);
        int mainSide = Random.value < .5f ? -1 : 1;
        int mainPiece = -1;

        var set = sprite != null ? SetFor(sprite, shipId, Random.Range(0, Variants)) : null;
        if (set != null)
        {
            Vector3 scale = Abs(shipTf.lossyScale);
            float worldScale = Mathf.Max(scale.x, scale.y);
            for (int k = 0; k < set.count; k++)
            {
                Vector3 start = shipTf.TransformPoint(new Vector3(set.local[k].x, set.local[k].y, 0f));
                Vector3 outward = start - shipTf.position;
                outward.z = 0f;
                if (outward.sqrMagnitude < 1e-6f) outward = Random.insideUnitCircle;
                outward.Normalize();
                bool main = k == set.main;
                int side;
                if (main) side = mainSide;
                else if (Mathf.Abs(outward.x) < .2f || Random.value < .3f) side = Random.value < .5f ? -1 : 1;
                else side = outward.x < 0f ? -1 : 1;
                int at = Add(PieceKind.Hull, set.sprites[k], start, shipTf.rotation, scale, set.radius[k] * worldScale,
                             side, main, outward, hull != null && hull.flipX, hull != null && hull.flipY,
                             TargetExplosion.Kind.Metal, float.NaN);
                if (main) mainPiece = at;
            }
            fragmentCount = pieceCount;
        }

        // The drone, knocked loose, follows the main chunk into its rail.
        var gun = ship.GetComponentInChildren<UltimateGun>();
        var gunSr = gun != null ? LargestRenderer(gun.transform) : null;
        if (gunSr != null)
        {
            float nearY = mainPiece >= 0 ? pieces[mainPiece].target.y : 0f;
            float y = nearY + (Random.value < .5f ? -1f : 1f) * Random.Range(.5f, .8f);
            Vector3 outward = gunSr.transform.position - shipTf.position;
            outward.z = 0f;
            outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector3.up;
            var b = gunSr.bounds.extents;
            Add(PieceKind.Drone, gunSr.sprite, gunSr.transform.position, gunSr.transform.rotation,
                Abs(gunSr.transform.lossyScale), Mathf.Max(b.x, b.y), mainSide, false, outward,
                gunSr.flipX, gunSr.flipY, TargetExplosion.Kind.Metal, y);
        }

        // The killer, if it was something solid, crashes into the other rail.
        killerKind = Classify(killer);
        if (killerKind == KillerKind.Physical)
        {
            var ksr = killer.GetComponent<SpriteRenderer>();
            if (ksr == null || ksr.sprite == null) ksr = killer.GetComponentInChildren<SpriteRenderer>();
            if (ksr != null && ksr.sprite != null)
            {
                Vector3 outward = ksr.transform.position - shipTf.position;
                outward.z = 0f;
                outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector3.up;
                var b = ksr.bounds.extents;
                Add(PieceKind.Killer, ksr.sprite, ksr.transform.position, ksr.transform.rotation,
                    Abs(ksr.transform.lossyScale), Mathf.Max(b.x, b.y), -mainSide, false, outward,
                    ksr.flipX, ksr.flipY, TargetExplosion.KindFor(killer), float.NaN);
            }
        }

        plannedEnd = 0f;
        for (int i = 0; i < pieceCount; i++)
            plannedEnd = Mathf.Max(plannedEnd, HitStop + pieces[i].delay + pieces[i].dur);
        plannedEnd += Settle;

        HideShip(ship, hull);

        // The fatal blast on the hull, and the kick.
        Blast(shipTf.position, TargetExplosion.Kind.Metal, 1.05f, true);
        shake = .06f;
        for (int i = 0; i < 6; i++)
            Emit(ShipDamageFx.RowSpark, Random.Range(0, 4), shipTf.position,
                 Random.insideUnitCircle.normalized * Random.Range(1.4f, 2.8f), Random.Range(.22f, .36f),
                 .1f, .05f, -5f, 0f, 1.5f, Color.white, SparkOrder);
    }

    int Add(PieceKind kind, Sprite sprite, Vector3 start, Quaternion rotation, Vector3 scale, float radius,
            int side, bool main, Vector3 outward, bool flipX, bool flipY, TargetExplosion.Kind blast, float wantY)
    {
        if (pieceCount >= MaxPieces || sprite == null) return -1;
        int i = pieceCount++;
        ref var p = ref pieces[i];
        p.kind = kind;
        p.active = true;
        p.landed = false;
        p.main = main;
        p.side = side;
        p.radius = Mathf.Max(.04f, radius);
        p.blast = blast;
        p.start = start;
        p.outward = outward;
        p.baseScale = scale;
        p.angle = rotation.eulerAngles.z;
        p.emit = Random.Range(0f, .03f);

        float inset = Mathf.Clamp(p.radius * .35f, .03f, .16f);
        float y = float.IsNaN(wantY) ? PickY(side) : Mathf.Clamp(wantY, MinY, MaxY);
        p.target = new Vector3(side * (edge - inset), y, start.z);

        Vector3 mid = Vector3.Lerp(start, p.target, .35f);
        float lift = Random.Range(.6f, 1.5f);
        float cx = Mathf.Clamp(mid.x + outward.x * .5f, -edge + .2f, edge - .2f);
        float cy = Mathf.Min(Mathf.Max(start.y, y) + lift, viewTop - .4f);
        p.control = new Vector3(cx, cy, start.z);

        switch (kind)
        {
            case PieceKind.Hull:
                p.delay = main ? MainDelay : Random.Range(0f, LaunchSpread);
                p.dur = main ? Random.Range(MainFlightMin, FlightMax) : Random.Range(FlightMin, FlightMax - .08f);
                p.spin = (Random.value < .5f ? -1f : 1f) * (main ? Random.Range(240f, 420f) : Random.Range(480f, 1080f));
                break;
            case PieceKind.Drone:
                p.delay = Random.Range(.16f, LaunchSpread + .04f);
                p.dur = Random.Range(.66f, .82f);
                p.spin = (Random.value < .5f ? -1f : 1f) * Random.Range(600f, 900f);
                break;
            default:
                p.delay = Random.Range(.03f, .08f);
                p.dur = Random.Range(.62f, .84f);
                p.spin = (Random.value < .5f ? -1f : 1f) * Random.Range(300f, 620f);
                break;
        }

        p.sr.sprite = sprite;
        p.sr.flipX = flipX;
        p.sr.flipY = flipY;
        p.sr.color = Color.white;
        p.sr.sortingOrder = PieceOrder + (main ? 1 : 0);
        p.sr.enabled = true;
        p.tf.position = start;
        p.tf.rotation = Quaternion.Euler(0f, 0f, p.angle);
        p.tf.localScale = scale;
        return i;
    }

    float MinY => viewBottom + .9f;
    float MaxY => Mathf.Max(viewBottom + 1f, viewTop - 1.4f);

    // A random height on the rail, kept apart from the pieces already headed
    // for the same side.
    float PickY(int side)
    {
        float y = 0f;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            y = Random.Range(MinY, MaxY);
            bool clear = true;
            for (int j = 0; j < pieceCount - 1; j++)
                if (pieces[j].side == side && Mathf.Abs(pieces[j].target.y - y) < .45f) { clear = false; break; }
            if (clear) break;
        }
        return y;
    }

    void Update()
    {
        bool pressed = TouchInput.IsPressed;
        if (animating && pressed && !wasPressed) Skip();
        wasPressed = pressed;
        Tick(Time.unscaledDeltaTime);
    }

    // One frame: clamps the step (a long frame -- back from the background --
    // never jumps the sequence ahead), publishes it as FrameDt, steps.
    public void Tick(float unscaledDt)
    {
        if (!animating) { FrameDt = 0f; return; }
        float dt = Mathf.Clamp(unscaledDt, 0f, MaxStep);
        FrameDt = dt;
        Step(dt);
    }

    void LateUpdate()
    {
        UndoShake();
        if (!animating || shake < .002f) return;
        var cam = Camera.main;
        if (cam == null) return;
        shakeApplied = new Vector3(Mathf.Sin(shakeClock * 83f), Mathf.Cos(shakeClock * 61f) * .7f, 0f) * shake;
        cam.transform.position += shakeApplied;
        shakeCam = cam;
    }

    void UndoShake()
    {
        if (shakeCam != null) shakeCam.transform.position -= shakeApplied;
        shakeCam = null;
        shakeApplied = Vector3.zero;
    }

    // A fresh tap: skip to the panel (only after SkipAfter). True if it did.
    public bool Skip()
    {
        if (!running || elapsed < SkipAfter) return false;
        for (int i = 0; i < pieceCount; i++)
        {
            ref var p = ref pieces[i];
            if (!p.active || p.landed) continue;
            p.landed = true;
            p.landedAt = elapsed - Crumple;
            p.tf.position = p.target;
            p.sr.color = Color.white;
            if (p.kind == PieceKind.Killer) killerCrashed = true;
        }
        lastLanding = elapsed;
        skipped = true;
        shake = Mathf.Max(shake, .03f);
        Finish();
        return true;
    }

    // One step of the sequence clock.
    public void Step(float dt)
    {
        if (!animating || dt < 0f) return;
        elapsed += dt;
        shakeClock += dt;
        shake *= Mathf.Exp(-11f * dt);

        bool allLanded = true;
        for (int i = 0; i < pieceCount; i++)
        {
            ref var p = ref pieces[i];
            if (!p.active) continue;
            if (!p.landed) { allLanded = false; Fly(i, dt); }
            else Stick(i, dt);
        }
        StepParticles(dt);

        if (running && allLanded && elapsed >= lastLanding + Settle) Finish();
        if (finished && elapsed >= finishedAt + Tail && !AnyVisible()) Stop();
    }

    void Fly(int i, float dt)
    {
        ref var p = ref pieces[i];
        float t = elapsed - HitStop - p.delay;
        if (t < 0f)
        {
            // Hit-stop: the hull holds together, flashing and shuddering,
            // already cracking apart a hair along its cuts.
            float crack = Mathf.Clamp01(elapsed / (HitStop + p.delay)) * .025f;
            Vector3 jitter = elapsed < HitStop ? (Vector3)(Random.insideUnitCircle * .018f) : Vector3.zero;
            p.tf.position = p.start + p.outward * crack + jitter;
            bool hot = Mathf.FloorToInt(elapsed * 30f) % 2 == 0;
            p.sr.color = hot ? new Color(1f, .5f, .42f, 1f) : Color.white;
            return;
        }
        p.sr.color = Color.white;
        float u = Mathf.Clamp01(t / p.dur);
        float e = u * (.55f + .45f * u);   // speeds up into the wall
        p.tf.position = Bezier(p.start, p.control, p.target, e);
        p.angle += p.spin * dt;
        p.tf.rotation = Quaternion.Euler(0f, 0f, p.angle);
        Trail(i, dt);
        if (u >= 1f) Land(i);
    }

    void Land(int i)
    {
        ref var p = ref pieces[i];
        p.landed = true;
        p.landedAt = elapsed;
        p.tf.position = p.target;
        lastLanding = Mathf.Max(lastLanding, elapsed);
        Impacts++;
        if (p.kind == PieceKind.Killer) killerCrashed = true;

        bool big = p.main || p.kind == PieceKind.Killer;
        var contact = new Vector3(p.side * edge, p.target.y, p.target.z);
        Blast(contact, p.blast, big ? .95f : .6f, big);
        shake = Mathf.Max(shake, big ? .075f : .045f);

        // sparks spray off the wall, scrap falls away, a puff of smoke
        int sparks = big ? 9 : 6;
        for (int s = 0; s < sparks; s++)
        {
            var dir = new Vector2(-p.side * Random.Range(.35f, 1f), Random.Range(-.8f, 1f)).normalized;
            Emit(ShipDamageFx.RowSpark, Random.Range(0, 4), contact, dir * Random.Range(1.6f, 3.4f),
                 Random.Range(.25f, .45f), .11f, .05f, -7f, 0f, 1.2f, Color.white, SparkOrder);
        }
        for (int s = 0; s < (big ? 4 : 2); s++)
        {
            var v = new Vector2(-p.side * Random.Range(.3f, 1.1f), Random.Range(.3f, 1.3f));
            Emit(s % 2 == 0 ? ShipDamageFx.RowScrap : ShipDamageFx.RowChunk, Random.Range(0, 4), contact, v,
                 Random.Range(.5f, .8f), .1f, .09f, -6f, Random.Range(-720f, 720f), .5f, Color.white, SparkOrder);
        }
        for (int s = 0; s < 2; s++)
            Emit(ShipDamageFx.RowSmoke, Random.Range(0, 4), contact,
                 new Vector2(-p.side * Random.Range(.1f, .35f), Random.Range(.15f, .4f)),
                 Random.Range(.6f, .9f), .16f, big ? .42f : .3f, 0f, Random.Range(-40f, 40f), 1f,
                 new Color(.6f, .56f, .64f, .9f), SmokeOrder);
    }

    // Stuck in the rail: crumples against it, holds, fades.
    void Stick(int i, float dt)
    {
        ref var p = ref pieces[i];
        float s = elapsed - p.landedAt;
        float k = Mathf.Clamp01(s / Crumple);
        float squash = Mathf.Sin(k * Mathf.PI) * .3f;
        float rest = k >= 1f ? .12f : 0f;
        var scale = p.baseScale;
        p.tf.localScale = new Vector3(scale.x * (1f - squash - rest), scale.y * (1f + squash * .45f), scale.z);
        float alpha = s < StickHold ? 1f : 1f - (s - StickHold) / StickFade;
        if (alpha <= 0f)
        {
            p.active = false;
            p.sr.enabled = false;
            p.tf.localScale = p.baseScale;
            return;
        }
        var c = Color.Lerp(new Color(.55f, .5f, .55f, 1f), Color.white, alpha);
        c.a = alpha;
        p.sr.color = c;
        // the main wreck smoulders on the wall
        if (p.main && alpha > .3f)
        {
            p.emit -= dt;
            if (p.emit <= 0f)
            {
                p.emit = .1f;
                Emit(ShipDamageFx.RowSmoke, Random.Range(0, 4), p.tf.position,
                     new Vector2(-p.side * Random.Range(.05f, .2f), Random.Range(.25f, .5f)),
                     Random.Range(.5f, .75f), .1f, .26f, 0f, Random.Range(-40f, 40f), .8f,
                     new Color(.5f, .47f, .55f, .8f * alpha), SmokeOrder);
            }
        }
    }

    // Smoke behind every flying piece, sparks off the hull pieces and the killer.
    void Trail(int i, float dt)
    {
        ref var p = ref pieces[i];
        p.emit -= dt;
        if (p.emit > 0f) return;
        bool big = p.main || p.kind == PieceKind.Killer;
        p.emit = big ? .03f : p.kind == PieceKind.Drone ? .07f : .05f;
        var pos = p.tf.position;
        Emit(ShipDamageFx.RowSmoke, Random.Range(0, 4), pos, Random.insideUnitCircle * .2f + new Vector2(0f, .15f),
             Random.Range(.45f, .7f), big ? .12f : .08f, big ? .3f : .2f, 0f, Random.Range(-60f, 60f), .8f,
             new Color(.52f, .48f, .58f, .85f), SmokeOrder);
        if (Random.value < (p.kind == PieceKind.Drone ? .5f : .4f))
            Emit(ShipDamageFx.RowSpark, Random.Range(0, 4), pos, Random.insideUnitCircle * 1.6f,
                 Random.Range(.16f, .28f), .09f, .04f, -4f, 0f, 1f, Color.white, SparkOrder);
    }

    void Finish()
    {
        if (!running) return;
        running = false;
        finished = true;
        finishedAt = elapsed;
        if (ship != null) BossUtil.Kill(ship);   // the ship is gone; the panel can come up
        ship = null;
        hiddenHull = null;
        hiddenChildren.Clear();
        disabled.Clear();
    }

    void Stop()
    {
        animating = false;
        FrameDt = 0f;
        HideAll();
        UndoShake();
    }

    // A revive (or anything else that must take the death back): stops the
    // sequence and, with restoreShip, puts the hidden ship back as it was.
    public void Cancel(bool restoreShip)
    {
        if (restoreShip && ship != null)
        {
            if (hiddenHull != null) hiddenHull.enabled = true;
            foreach (var b in disabled) if (b != null) b.enabled = true;
            foreach (var go in hiddenChildren) if (go != null) go.SetActive(true);
        }
        hiddenHull = null;
        hiddenChildren.Clear();
        disabled.Clear();
        ship = null;
        running = finished = false;
        Stop();
    }

    bool AnyVisible()
    {
        for (int i = 0; i < pieceCount; i++) if (pieces[i].active) return true;
        for (int i = 0; i < MaxParticles; i++) if (particles[i].alive) return true;
        return false;
    }

    void HideAll()
    {
        for (int i = 0; i < MaxPieces; i++)
        {
            pieces[i].active = false;
            if (pieces[i].sr != null) pieces[i].sr.enabled = false;
        }
        for (int i = 0; i < MaxParticles; i++)
        {
            particles[i].alive = false;
            if (particleRenderers[i] != null) particleRenderers[i].enabled = false;
        }
        pieceCount = 0;
    }

    // The ship stays (its hearts play the last shield) but is out of play:
    // hull, colliders, controls and every attachment but the hearts hidden.
    void HideShip(GameObject go, SpriteRenderer hull)
    {
        hiddenChildren.Clear();
        disabled.Clear();
        hiddenHull = null;
        if (hull != null && hull.enabled) { hull.enabled = false; hiddenHull = hull; }
        foreach (var c in go.GetComponents<Collider2D>()) Disable(c);
        Disable(go.GetComponent<movePlayer>());
        Disable(go.GetComponent<movePlayerInTut>());
        Disable(go.GetComponent<ShipDamageFx>());
        var tf = go.transform;
        for (int i = 0; i < tf.childCount; i++)
        {
            var child = tf.GetChild(i).gameObject;
            if (child.name.StartsWith("Heart") || child.name == "~HeartBreaks") continue;
            if (!child.activeSelf) continue;
            child.SetActive(false);
            hiddenChildren.Add(child);
        }
    }

    void Disable(Behaviour b)
    {
        if (b == null || !b.enabled) return;
        b.enabled = false;
        disabled.Add(b);
    }

    void Blast(Vector3 at, TargetExplosion.Kind kind, float size, bool flash)
    {
        if (!Application.isPlaying && !EditorBlasts) return;   // edit-mode tests: no pooled flipbooks
        Color energy = WeaponStyleTable.For(shipId).energy;
        if (flash) WeaponFx.Flipbook().Play(FlipbookFx.Mode.Flash, at, size * 1.15f, shipId, kind, energy, 1f, 64);
        WeaponFx.Flipbook().Play(FlipbookFx.Mode.Ring, at, size * 1.3f, shipId, kind, energy, 1f, 65);
        WeaponFx.Flipbook().Play(FlipbookFx.Mode.Explosion, at, size, shipId, kind, Color.white, 1f, 66);
    }

    // ---- particles ----

    void Emit(int row, int drawing, Vector3 pos, Vector2 vel, float life, float size0, float size1,
              float gravity, float spin, float drag, Color tint, int order)
    {
        int slot = -1;
        for (int n = 0; n < MaxParticles; n++)
        {
            int j = (nextParticle + n) % MaxParticles;
            if (!particles[j].alive) { slot = j; break; }
        }
        if (slot < 0) slot = nextParticle;   // full: the oldest-ish goes
        nextParticle = (slot + 1) % MaxParticles;
        var sprite = ShipDamageFx.Frame(row, drawing);
        var sr = particleRenderers[slot];
        if (sprite == null || sr == null) return;
        ref var q = ref particles[slot];
        q.alive = true;
        q.pos = new Vector3(pos.x, pos.y, pos.z);
        q.vel = new Vector3(vel.x, vel.y, 0f);
        q.age = 0f;
        q.life = life;
        q.size0 = size0;
        q.size1 = size1;
        q.gravity = gravity;
        q.spin = spin;
        q.drag = drag;
        q.angle = Random.Range(0f, 360f);
        q.tint = tint;
        sr.sprite = sprite;
        sr.sortingOrder = order;
        sr.color = tint;
        sr.enabled = true;
        sr.transform.position = q.pos;
        sr.transform.localScale = Vector3.one * size0;
    }

    void StepParticles(float dt)
    {
        for (int i = 0; i < MaxParticles; i++)
        {
            ref var q = ref particles[i];
            if (!q.alive) continue;
            q.age += dt;
            var sr = particleRenderers[i];
            if (q.age >= q.life) { q.alive = false; sr.enabled = false; continue; }
            q.vel.y += q.gravity * dt;
            q.vel *= Mathf.Max(0f, 1f - q.drag * dt);
            q.pos += q.vel * dt;
            q.angle += q.spin * dt;
            float k = q.age / q.life;
            var t = sr.transform;
            t.position = q.pos;
            t.rotation = Quaternion.Euler(0f, 0f, q.angle);
            t.localScale = Vector3.one * Mathf.Lerp(q.size0, q.size1, k);
            var c = q.tint;
            c.a *= 1f - k * k;
            sr.color = c;
        }
    }

    // ---- fragments ----

    sealed class FragmentSet
    {
        public Sprite[] sprites;
        public Texture2D[] textures;
        public Vector2[] local;    // each piece's pivot, sprite-local units from the hull's pivot
        public float[] radius;     // sprite-local units
        public int main, count;

        public void Destroy()
        {
            for (int i = 0; i < count; i++)
            {
                BossUtil.Kill(sprites[i]);
                BossUtil.Kill(textures[i]);
            }
        }
    }

    static readonly Dictionary<long, FragmentSet> sets = new Dictionary<long, FragmentSet>();
    const int MaxCachedSets = 12;

    // Scratch (fragment cutting happens once per new hull drawing at most).
    const int PolyMax = 48;
    static readonly Vector2[] poly = new Vector2[PolyMax];
    static readonly Vector2[] seeds = new Vector2[MaxFragments];
    static readonly int[] mass = new int[MaxFragments];
    static readonly Vector2[] massSum = new Vector2[MaxFragments];
    static readonly int[] minX = new int[MaxFragments], minY = new int[MaxFragments];
    static readonly int[] maxX = new int[MaxFragments], maxY = new int[MaxFragments];

    // How many pieces a hull breaks into for a given seed (tests).
    public static int FragmentsFor(int id, Sprite hull, int variant)
    {
        var set = SetFor(hull, id, variant);
        return set != null ? set.count : 0;
    }

    static FragmentSet SetFor(Sprite hull, int id, int variant)
    {
        if (hull == null || hull.texture == null) return null;
        long key = ((long)hull.GetInstanceID() << 8) ^ (long)(id * Variants + variant);
        if (sets.TryGetValue(key, out var cached) && Alive(cached)) return cached;
        if (sets.Count >= MaxCachedSets)
        {
            foreach (var old in sets.Values) if (old != null) old.Destroy();
            sets.Clear();
        }
        var set = Cut(hull, id, variant);
        if (set != null) sets[key] = set;
        return set;
    }

    static bool Alive(FragmentSet set)
    {
        if (set == null) return false;
        for (int i = 0; i < set.count; i++) if (set.sprites[i] == null || set.textures[i] == null) return false;
        return true;
    }

    // The hull drawing's pixels. The hull sheets aren't CPU-readable, so the
    // drawing is copied through the GPU once (per new drawing, at a death).
    public static Texture2D ReadableCopy(Sprite hull)
    {
        if (hull == null || hull.texture == null) return null;
        var src = hull.texture;
        Rect r = hull.textureRect;
        int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
        if (w < 1 || h < 1) return null;
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        var previous = RenderTexture.active;
        Graphics.Blit(src, rt, new Vector2(r.width / src.width, r.height / src.height),
                      new Vector2(r.x / src.width, r.y / src.height));
        RenderTexture.active = rt;
        var copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
        copy.Apply(false, false);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        return copy;
    }

    // Cuts the hull drawing into pieces: every pixel goes to the nearest of a
    // few seeds spread over the hull's body polygon (a Voronoi split), with
    // the distance jittered in small blocks so the cracks run jagged. Each
    // piece is its own small texture, cropped to its pixels and pivoted on
    // their centre of mass.
    static FragmentSet Cut(Sprite hull, int id, int variant)
    {
        var copy = ReadableCopy(hull);
        if (copy == null) return null;
        int w = copy.width, h = copy.height;
        float ppu = hull.pixelsPerUnit;
        Vector2 pivot = hull.pivot;
        var pixels = copy.GetPixels32();

        // The hull's body outline, in sprite pixels.
        int pn = 0;
        var shape = ShipHitbox.ShapeFor(id);
        if (shape != null && shape.hull != null && shape.hull.Length >= 3)
        {
            pn = Mathf.Min(shape.hull.Length, PolyMax);
            for (int i = 0; i < pn; i++)
            {
                var v = shape.hull[i] * ppu + pivot;
                poly[i] = new Vector2(Mathf.Clamp(v.x, 0f, w), Mathf.Clamp(v.y, 0f, h));
            }
        }
        float area = pn >= 3 ? Mathf.Abs(SignedArea(poly, pn)) : 0f;
        if (pn < 3 || area < 4f)
        {
            pn = 4;
            poly[0] = new Vector2(w * .2f, h * .2f); poly[1] = new Vector2(w * .8f, h * .2f);
            poly[2] = new Vector2(w * .8f, h * .8f); poly[3] = new Vector2(w * .2f, h * .8f);
            area = Mathf.Abs(SignedArea(poly, pn));
        }
        float perimeter = 0f;
        Vector2 min = poly[0], max = poly[0];
        for (int i = 0; i < pn; i++)
        {
            perimeter += (poly[(i + 1) % pn] - poly[i]).magnitude;
            min = Vector2.Min(min, poly[i]);
            max = Vector2.Max(max, poly[i]);
        }

        // Spiky or stretched outlines break into more pieces than compact
        // ones; a per-variant coin adds one more now and then.
        uint rng = Hash((uint)(id * 7919 + variant * 104729 + 17));
        float compact = perimeter * perimeter / (4f * Mathf.PI * area);
        float aspect = Mathf.Max(max.x - min.x, max.y - min.y) / Mathf.Max(1f, Mathf.Min(max.x - min.x, max.y - min.y));
        int count = 3 + Mathf.Clamp(Mathf.FloorToInt((compact - 1.25f) * 1.6f + (aspect - 1f) * .8f), 0, 2)
                      + (Next01(ref rng) < .5f ? 1 : 0);
        count = Mathf.Clamp(count, MinFragments, MaxFragments);

        // Seeds inside the body, spread out (best of several candidates).
        for (int k = 0; k < count; k++)
        {
            Vector2 best = (min + max) * .5f;
            float bestD = -1f;
            for (int c = 0; c < 12; c++)
            {
                Vector2 cand = best;
                for (int tries = 0; tries < 40; tries++)
                {
                    cand = new Vector2(Mathf.Lerp(min.x, max.x, Next01(ref rng)), Mathf.Lerp(min.y, max.y, Next01(ref rng)));
                    if (Inside(poly, pn, cand)) break;
                }
                float d = float.MaxValue;
                for (int j = 0; j < k; j++) d = Mathf.Min(d, (cand - seeds[j]).sqrMagnitude);
                if (k == 0) d = Next01(ref rng);
                if (d > bestD) { bestD = d; best = cand; }
            }
            seeds[k] = best;
            mass[k] = 0;
            massSum[k] = Vector2.zero;
            minX[k] = w; minY[k] = h; maxX[k] = -1; maxY[k] = -1;
        }

        // Every drawn pixel to its piece.
        float spacing = Mathf.Sqrt(area / count);
        float jag = Mathf.Max(2f, spacing * .12f);
        uint salt = (uint)(variant * 977 + id * 131);
        var owner = new byte[w * h];
        int drawn = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int at = y * w + x;
                if (pixels[at].a < 8) { owner[at] = 255; continue; }
                var p = new Vector2(x + .5f, y + .5f);
                uint bx = (uint)(x / 3), by = (uint)(y / 3);
                int near = 0;
                float nd = float.MaxValue;
                for (int k = 0; k < count; k++)
                {
                    uint hsh = Hash(bx * 73856093u ^ by * 19349663u ^ (uint)k * 83492791u ^ salt);
                    float jitter = ((hsh & 1023) / 1023f - .5f) * jag;
                    float d = (p - seeds[k]).magnitude + jitter;
                    if (d < nd) { nd = d; near = k; }
                }
                owner[at] = (byte)near;
                mass[near]++;
                massSum[near] += p;
                if (x < minX[near]) minX[near] = x;
                if (y < minY[near]) minY[near] = y;
                if (x > maxX[near]) maxX[near] = x;
                if (y > maxY[near]) maxY[near] = y;
                drawn++;
            }

        var set = new FragmentSet
        {
            sprites = new Sprite[count], textures = new Texture2D[count],
            local = new Vector2[count], radius = new float[count], count = count,
        };
        int mainK = 0;
        var filter = hull.texture.filterMode;
        for (int k = 0; k < count; k++)
        {
            if (maxX[k] < minX[k])
            {
                // owns nothing drawn: a speck at its seed
                minX[k] = maxX[k] = Mathf.Clamp((int)seeds[k].x, 0, w - 1);
                minY[k] = maxY[k] = Mathf.Clamp((int)seeds[k].y, 0, h - 1);
            }
            int x0 = minX[k], y0 = minY[k];
            int bw = maxX[k] - x0 + 1, bh = maxY[k] - y0 + 1;
            var part = new Color32[bw * bh];
            for (int y = 0; y < bh; y++)
                for (int x = 0; x < bw; x++)
                {
                    int at = (y0 + y) * w + x0 + x;
                    part[y * bw + x] = owner[at] == k ? pixels[at] : new Color32(0, 0, 0, 0);
                }
            var tex = new Texture2D(bw, bh, TextureFormat.RGBA32, false);
            tex.name = "crash_" + id + "_" + variant + "_" + k;
            tex.filterMode = filter;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixels32(part);
            tex.Apply(false, false);

            Vector2 centre = mass[k] > 0 ? massSum[k] / mass[k] : seeds[k];
            var sprite = Sprite.Create(tex, new Rect(0, 0, bw, bh),
                                       new Vector2((centre.x - x0) / bw, (centre.y - y0) / bh), ppu, 0, SpriteMeshType.FullRect);
            sprite.name = tex.name;
            set.sprites[k] = sprite;
            set.textures[k] = tex;
            set.local[k] = (centre - pivot) / ppu;
            float share = drawn > 0 ? mass[k] / (float)drawn : 1f / count;
            set.radius[k] = Mathf.Sqrt(Mathf.Max(share, .02f) * area / Mathf.PI) / ppu;
            if (mass[k] > mass[mainK]) mainK = k;
        }
        set.main = mainK;
        BossUtil.Kill(copy);
        return set;
    }

    // Pixels with any alpha, in a readable texture (tests).
    public static int DrawnPixels(Texture2D tex)
    {
        if (tex == null) return 0;
        int n = 0;
        foreach (var c in tex.GetPixels32()) if (c.a >= 8) n++;
        return n;
    }

    static float SignedArea(Vector2[] p, int n)
    {
        float a = 0f;
        for (int i = 0; i < n; i++) { var u = p[i]; var v = p[(i + 1) % n]; a += u.x * v.y - v.x * u.y; }
        return a * .5f;
    }

    static bool Inside(Vector2[] p, int n, Vector2 q)
    {
        bool inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            if ((p[i].y > q.y) != (p[j].y > q.y) &&
                q.x < (p[j].x - p[i].x) * (q.y - p[i].y) / (p[j].y - p[i].y) + p[i].x)
                inside = !inside;
        }
        return inside;
    }

    static uint Hash(uint x)
    {
        x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16;
        return x == 0 ? 1u : x;
    }

    static float Next01(ref uint s)
    {
        s ^= s << 13; s ^= s >> 17; s ^= s << 5;
        return (s & 0xFFFFFF) / 16777216f;
    }

    // ---- helpers ----

    static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        float u = 1f - t;
        return u * u * a + 2f * u * t * b + t * t * c;
    }

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z) > 0f ? Mathf.Abs(v.z) : 1f);

    static SpriteRenderer LargestRenderer(Transform root)
    {
        SpriteRenderer best = null;
        float size = 0f;
        foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>())
        {
            if (sr == null || sr.sprite == null || !sr.enabled || !sr.gameObject.activeInHierarchy) continue;
            var e = sr.bounds.extents;
            float s = e.x * e.y;
            if (s > size) { size = s; best = sr; }
        }
        return best;
    }
}
