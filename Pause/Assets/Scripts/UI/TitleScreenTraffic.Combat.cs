using UnityEngine;
using UnityEngine.UI;

// Home-screen traffic, part 2: ultimates, shoot-downs and crashes into the
// logo and the menu buttons.
//
// Ultimates. About one flight in seven (UltChance) lets its ship's main
// attack go once, at a random moment while it is well inside the screen: a
// short wind-up (it lines up on a ship on its own layer, a charge burst at
// the nose), then a cosmetic copy of its gameplay attack (ShipLoadoutTable:
// comet streaks, chain lightning, the fireball, the rail, javelins from
// above, the blowtorch cone, seeker eyes, the ricochet star, the gatling,
// the tractor beam, the feather fan, the shell disc; the top tier's volley
// as a short homing salvo -- no slow motion, Time.timeScale is never
// touched). Every drawing is the ship's own (WeaponArt shots,
// ShipFxArt attack loops and bursts, WeaponStyleTable colours), scaled to
// the layer. None of gameplay's attack runner, attack pools, target lists
// or hit routing is used: nothing here scores, unlocks, pays out, buzzes or
// needs a gameplay object. At most MaxUlts play at once.
//
// Shoot-downs. A shot only ever hits ships on its shooter's depth layer. A
// hit ship breaks into its wreck fragments (DeathCrash.CutFragments, the
// same Voronoi cut as the gameplay death, in the flight's skin) that spin
// on smoking arcs and slam into the side of the screen -- the home screen
// has no rails, so the screen edges are the walls -- with sparks, a flash
// and a crumple, then fade. At most MaxWrecks wrecks fly at once; past
// that a hit ship just pops (the old crash explosion).
//
// Logo and button crashes. Now and then (every PlungeInterval, or when a
// shot ship careens) a mid or front ship loses control, spins and dives
// into the PAUSE logo or one of the menu buttons: a short flash, ring and
// sparks on top of it, and the hull breaks into pieces that bounce off and
// fall away. The logo itself is never moved, recoloured or resized; every
// piece of the impact is gone within LogoFxMax seconds. A button's label
// gives a held cel wobble (CelPress-style poses) that ends at exactly its
// own scale; its RectTransform, Image, Button and raycast target are never
// touched, so it stays tappable throughout.
public partial class TitleScreenTraffic
{
    public const float UltChance = .15f;
    public const int MaxUlts = 2;
    public const float UltWindup = .32f;
    public const float UltSlow = .55f;
    public const int BoltPool = 24, FxPool = 14, PiecePool = 24, ParticlePool = 72, MaxWrecks = 3;
    public const int BoltSegments = 4;
    public const float LogoFxMax = .6f;
    public const int PlungeSortBase = -1;      // hull at +3: over the logo (0), under the front band (10)
    public const int LogoFxSort = 6;
    public const float StrickenPlungeChance = .25f;

    [Tooltip("Seconds after a flight starts before its ultimate (if it has one) may go off.")]
    public Vector2 ultDelay = new Vector2(.8f, 3.2f);

    [Tooltip("Seconds between ships losing control into the logo or a button.")]
    public Vector2 plungeInterval = new Vector2(26f, 44f);

    // ---- stats, read by the tests
    public int Flights { get; private set; }
    public int UltsPlanned { get; private set; }
    public int UltsFired { get; private set; }
    public int UltsBlocked { get; private set; }
    public int PeakUlts { get; private set; }
    public int ShootDowns { get; private set; }
    public int CrossLayerKills { get; private set; }
    public int Wrecks { get; private set; }
    public int PeakWrecks { get; private set; }
    public int WallImpacts { get; private set; }
    public int WallImpactsOffEdge { get; private set; }
    public int Pops { get; private set; }
    public int Plunges { get; private set; }
    public int LogoCrashes { get; private set; }
    public int ButtonCrashes { get; private set; }
    public float LastLogoImpactAt { get; private set; } = -1f;
    public float LastButtonImpactAt { get; private set; } = -1f;
    public int LastButtonHit { get; private set; } = -1;
    public Vector2 LastImpactPoint { get; private set; }
    public readonly int[] AttacksFired = new int[(int)ShipAttack.ScreenClear + 1];

    // ------------------------------------------------------------- pools

    class Ult
    {
        public bool on, firing;
        public Flyer owner;
        public ShipLoadout l;
        public float t, next;
        public int fired, hops;
        public Vector2 point;
        public Fx held, disc;
        public float angle;
        public readonly Flyer[] hit = new Flyer[8];
        public int hitCount;
    }

    public enum BoltKind { Straight, Homing, Ricochet, Javelin, Fireball }

    class Bolt
    {
        public SpriteRenderer body;
        public readonly SpriteRenderer[] outer = new SpriteRenderer[BoltSegments];
        public readonly SpriteRenderer[] core = new SpriteRenderer[BoltSegments];
        public readonly Vector2[] hist = new Vector2[BoltSegments + 1];
        public int histCount;
        public bool on;
        public BoltKind kind;
        public int ship;
        public Depth layer;
        public Flyer owner, target;
        public Vector2 pos, dir;
        public float speed, radius, size, age, travelled, range, burst, spinAngle, stopY, scale;
        public int hops, maxHops;
        public WeaponStyle style;
    }

    enum FxSource { Loop, Burst, Shot }

    class Fx
    {
        public SpriteRenderer sr;
        public bool on, loop;
        public FxSource src;
        public int ship, frame;
        public int[] ticks;
        public float clock, life, along, angleOff;
        public Flyer follow;
        public Color tint;
    }

    class Piece
    {
        public SpriteRenderer sr;
        public bool on, landed, fall, main;
        public int group, side;
        public Depth layer;
        public int ship;
        public Vector2 start, control, target, vel;
        public float delay, dur, t, angle, spin, landedAt, emit, radius, life, gravity;
        public Vector3 baseScale;
    }

    class Particle
    {
        public SpriteRenderer sr;
        public bool on;
        public Vector2 pos, vel;
        public float age, life, size0, size1, angle, spin, gravity, drag;
        public Color tint;
    }

    struct MenuButton
    {
        public RectTransform rt;
        public Transform label;
        public Text text;
        public Vector3 labelScale;
        public Button button;
        public float wobbleT;
        public bool wobbling;
    }

    readonly Ult[] ults = new Ult[MaxUlts];
    readonly Bolt[] bolts = new Bolt[BoltPool];
    readonly Fx[] fxs = new Fx[FxPool];
    readonly Piece[] pieces = new Piece[PiecePool];
    readonly Particle[] particles = new Particle[ParticlePool];
    MenuButton[] buttons = new MenuButton[0];
    Canvas buttonCanvas;
    int nextParticle, wreckGroup;
    float nextPlungeAt;
    Flyer plunger;
    readonly Flyer[] picks = new Flyer[8];

    public int ButtonCount => buttons.Length;
    public RectTransform ButtonAt(int i) => buttons[i].rt;
    public bool ButtonWobbling(int i) => buttons[i].wobbling;
    public float NextPlungeAt { get => nextPlungeAt; set => nextPlungeAt = value; }
    public Flyer Plunger => plunger;

    public int ActiveUlts
    {
        get { int n = 0; for (int i = 0; i < MaxUlts; i++) if (ults[i].on) n++; return n; }
    }

    public int ActiveBolts
    {
        get { int n = 0; for (int i = 0; i < BoltPool; i++) if (bolts[i].on) n++; return n; }
    }

    public int ActiveWrecks
    {
        get
        {
            int n = 0;
            for (int i = 0; i < PiecePool; i++)
            {
                var p = pieces[i];
                if (!p.on || p.fall || !p.main) continue;
                n++;
            }
            return n;
        }
    }

    // Renderers the impacts draw over the logo with (tests: nothing of a
    // logo crash may linger on it).
    public int ImpactRenderersOn(Rect r)
    {
        int n = 0;
        for (int i = 0; i < PiecePool; i++)
            if (pieces[i].on && pieces[i].sr.sortingOrder > 0 && pieces[i].sr.sortingOrder < Depths[2].sortBase && Overlaps(r, pieces[i].sr.transform.position, 0f)) n++;
        for (int i = 0; i < ParticlePool; i++)
            if (particles[i].on && particles[i].sr.sortingOrder > 0 && particles[i].sr.sortingOrder < Depths[2].sortBase && Overlaps(r, particles[i].pos, 0f)) n++;
        for (int i = 0; i < fxCount; i++)
        {
            var book = fx[i];
            if (book == null || !book.Active) continue;
            var sr = book.GetComponent<SpriteRenderer>();
            if (sr.sortingOrder > 0 && sr.sortingOrder < Depths[2].sortBase && Overlaps(r, book.transform.position, 0f)) n++;
        }
        return n;
    }

    void BuildCombat()
    {
        for (int i = 0; i < MaxUlts; i++) ults[i] = new Ult();
        for (int i = 0; i < BoltPool; i++)
        {
            var b = new Bolt { body = Renderer("~bolt", null) };
            for (int s = 0; s < BoltSegments; s++)
            {
                b.outer[s] = Renderer("~bolt", WeaponFx.Solid);
                b.core[s] = Renderer("~bolt", WeaponFx.Solid);
            }
            bolts[i] = b;
        }
        for (int i = 0; i < FxPool; i++) fxs[i] = new Fx { sr = Renderer("~ultfx", null) };
        for (int i = 0; i < PiecePool; i++) pieces[i] = new Piece { sr = Renderer("~wreck", null) };
        for (int i = 0; i < ParticlePool; i++) particles[i] = new Particle { sr = Renderer("~spark", null) };
    }

    void FindButtons()
    {
        if (menuPanel == null) { buttons = new MenuButton[0]; return; }
        var canvas = menuPanel.GetComponentInParent<Canvas>();
        buttonCanvas = canvas != null ? canvas.rootCanvas : null;
        var found = menuPanel.GetComponentsInChildren<Button>(true);
        buttons = new MenuButton[found.Length];
        for (int i = 0; i < found.Length; i++)
        {
            var label = found[i].GetComponentInChildren<Text>(true);
            buttons[i] = new MenuButton
            {
                rt = found[i].transform as RectTransform,
                button = found[i],
                label = label != null ? label.transform : null,
                text = label,
                labelScale = label != null ? label.transform.localScale : Vector3.one,
            };
        }
    }

    // Material for a layer's effects (the back layer is hazed).
    Material LayerMaterial(Depth layer)
    {
        var spec = Depths[(int)layer];
        return spec.haze && hazeMaterial != null ? hazeMaterial : spriteMaterial;
    }

    int SortOrderOf(Flyer f)
    {
        if (f.state == State.Plunge) return PlungeSortBase;
        return Depths[(int)f.layer].sortBase + f.slot * SortSlots;
    }

    static Vector2 Dir(float radians) { return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)); }

    Vector2 Nose(Flyer f) { return f.pos + Dir(f.heading) * (ReferenceHull * f.scale * .5f); }

    // ---------------------------------------------------------- ultimates

    void TickUltimates(float dt)
    {
        int on = ActiveUlts;
        for (int i = 0; i < pool.Length; i++)
        {
            var f = pool[i];
            if (!f.active || f.ulting || now < f.ultAt) continue;
            if (!CanUlt(f)) continue;
            if (on >= MaxUlts) { f.ultAt = now + .5f; UltsBlocked++; continue; }
            Ult u = null;
            for (int k = 0; k < MaxUlts; k++) if (!ults[k].on) { u = ults[k]; break; }
            if (u == null) continue;
            StartUlt(f, u);
            on++;
        }
        PeakUlts = Mathf.Max(PeakUlts, on);
        for (int k = 0; k < MaxUlts; k++) if (ults[k].on) StepUlt(ults[k], dt);
    }

    bool CanUlt(Flyer f)
    {
        if (f.state != State.Cruise && f.state != State.Boost) return false;
        if (f.trick != Trick.None) return false;
        return InsideSafe(f.pos, .7f);
    }

    void StartUlt(Flyer f, Ult u)
    {
        f.ultAt = float.MaxValue;
        f.ulting = true;
        f.ultT = 0f;
        if (f.state == State.Boost) { f.state = State.Cruise; f.speedMul = 1f; f.flame = IdleFlame; f.stretchX = f.stretchY = 1f; }
        u.on = true;
        u.firing = false;
        u.owner = f;
        u.l = ShipLoadoutTable.For(f.id);
        u.t = 0f; u.next = 0f; u.fired = 0; u.hops = 0; u.hitCount = 0; u.angle = 90f;
        u.held = u.disc = null;
        f.aim = Nearest(f, Nose(f), 5f * Mathf.Max(.6f, f.scale), true);
        // the tell: the attack's own burst flaring at the nose
        Play(FxSource.Burst, f.id, Nose(f), Vector3.one * .55f * f.scale, f.heading * Mathf.Rad2Deg - 90f, ShipFxArt.BurstTicks,
             false, 0f, f, ReferenceHull * f.scale * .5f, f.layer, 2);
        UltsFired++;
        AttacksFired[(int)u.l.attack]++;
    }

    // While it winds up (and holds a beam) the ship turns onto its mark.
    float UltSteer(Flyer f, float steer, float turnRate)
    {
        if (f.aim == null || !f.aim.active || f.aim.layer != f.layer) return steer * .3f;
        float want = Mathf.Atan2(f.aim.pos.y - f.pos.y, f.aim.pos.x - f.pos.x);
        float diff = Mathf.DeltaAngle(f.heading * Mathf.Rad2Deg, want * Mathf.Rad2Deg) * Mathf.Deg2Rad;
        return Mathf.Clamp(diff * 3f, -2.6f, 2.6f);
    }

    void EndUltimate(Flyer f)
    {
        f.ulting = false;
        f.aim = null;
        for (int k = 0; k < MaxUlts; k++)
        {
            var u = ults[k];
            if (!u.on || u.owner != f) continue;
            StopUlt(u);
        }
        // a held beam or cone riding this ship goes with it
        for (int i = 0; i < FxPool; i++) if (fxs[i].on && fxs[i].follow == f) StopFx(fxs[i]);
    }

    void StopUlt(Ult u)
    {
        if (u.held != null) StopFx(u.held);
        if (u.disc != null) StopFx(u.disc);
        u.held = u.disc = null;
        u.on = false;
        if (u.owner != null) { u.owner.ulting = false; u.owner.aim = null; }
        u.owner = null;
    }

    void StepUlt(Ult u, float dt)
    {
        var f = u.owner;
        if (f == null || !f.active) { StopUlt(u); return; }
        f.ultT += dt;
        if (!u.firing)
        {
            if (f.ultT < UltWindup) return;
            u.firing = true;
            u.t = 0f;
            Fire(u);
            if (!u.on) return;
        }
        else u.t += dt;
        Step(u, dt);
    }

    void Fire(Ult u)
    {
        var f = u.owner;
        var l = u.l;
        float s = f.scale;
        Vector2 nose = Nose(f);
        float deg = f.heading * Mathf.Rad2Deg - 90f;
        switch (l.attack)
        {
            case ShipAttack.ScreenClear:
                {
                    // the top tier's volley, quick: a homing lance at each
                    // ship on its layer (a few at most), no slow motion
                    int n = Pick(f, nose, 99f, 4);
                    int shots = Mathf.Max(3, n);
                    for (int i = 0; i < shots; i++)
                    {
                        float a = (i - (shots - 1) * .5f) * (50f / Mathf.Max(1, shots - 1));
                        Launch(f, BoltKind.Homing, nose, Rotate(Dir(f.heading), -a * Mathf.Deg2Rad), 16f * .5f * s,
                               .16f * s, .5f * s, i < n ? picks[i] : null);
                    }
                    Play(FxSource.Burst, f.id, nose, Vector3.one * .8f * s, deg, ShipFxArt.BurstTicks, false, 0f, f,
                         ReferenceHull * s * .5f, f.layer, 3);
                    Done(u);
                }
                break;
            case ShipAttack.RailSlug:
                {
                    Vector2 d = Dir(f.heading);
                    float len = RayExit(nose, d) + .4f;
                    float half = l.width * s;
                    Play(FxSource.Loop, f.id, nose + d * len * .5f, new Vector3(half * 2f * 1.6f, len, 1f), deg,
                         ShipFxArt.BurstTicks, false, 0f, null, 0f, f.layer, 1);
                    Play(FxSource.Burst, f.id, nose, Vector3.one * 1.1f * s, deg, ShipFxArt.BurstTicks, false, 0f, null, 0f, f.layer, 3);
                    KillInLine(f, nose, d, len, half);
                    Done(u);
                }
                break;
            case ShipAttack.SolarFireball:
                Launch(f, BoltKind.Fireball, nose, Dir(f.heading), Mathf.Max(2.2f, l.speed * .6f * s), .28f * s, .72f * s, null);
                Done(u);
                break;
            case ShipAttack.SeekerEyes:
                {
                    int n = Pick(f, nose, 6f * s + 2f, l.shots);
                    for (int i = 0; i < l.shots; i++)
                    {
                        float a = (i - (l.shots - 1) * .5f) * 24f;
                        Launch(f, BoltKind.Homing, nose, Rotate(Dir(f.heading), -a * Mathf.Deg2Rad),
                               Mathf.Max(2.5f, l.speed * .6f * s), .18f * s, .5f * s, i < n ? picks[i] : null);
                    }
                    Done(u);
                }
                break;
            case ShipAttack.Ricochet:
                {
                    var first = Nearest(f, nose, 99f, false);
                    var b = Launch(f, BoltKind.Ricochet, nose, Dir(f.heading), Mathf.Max(3f, l.speed * .55f * s),
                                   .18f * s, .5f * s, first);
                    if (b != null) b.maxHops = Mathf.Min(3, l.maxHits);
                    Done(u);
                }
                break;
            case ShipAttack.FeatherFan:
                for (int i = 0; i < l.shots; i++)
                {
                    float a = l.shots <= 1 ? 0f : -l.width * .5f + l.width * i / (l.shots - 1);
                    Launch(f, BoltKind.Straight, nose, Rotate(Dir(f.heading), -a * Mathf.Deg2Rad),
                           Mathf.Max(3f, l.speed * .55f * s), .16f * s, .44f * s, null);
                }
                Done(u);
                break;
            case ShipAttack.Blowtorch:
                {
                    float range = l.range * .7f * s;
                    float halfWidth = Mathf.Tan(l.width * Mathf.Deg2Rad) * range;
                    u.held = Play(FxSource.Loop, f.id, nose, new Vector3(halfWidth * 2f * 1.15f, range, 1f), deg,
                                  ShipFxArt.LoopTicks, true, l.duration * .8f, f, ReferenceHull * s * .5f + range * .5f, f.layer, 1);
                }
                break;
            case ShipAttack.TractorBeam:
                {
                    Vector2 d = Dir(f.heading);
                    float len = Mathf.Min(RayExit(nose, d) + .3f, 5.5f * s + 1f);
                    u.point = new Vector2(len, 0f);
                    u.held = Play(FxSource.Loop, f.id, nose, new Vector3(l.width * s * 2f * 1.5f, len, 1f), deg,
                                  ShipFxArt.LoopTicks, true, l.duration * .8f, f, ReferenceHull * s * .5f + len * .5f, f.layer, 1);
                    Play(FxSource.Burst, f.id, nose, Vector3.one * .9f * s, deg, ShipFxArt.BurstTicks, false, 0f, f,
                         ReferenceHull * s * .5f, f.layer, 3);
                }
                break;
            case ShipAttack.OrbitDisc:
                u.held = Play(FxSource.Loop, f.id, f.pos, Vector3.one * (l.width * 2f + .5f) * s, 0f, ShipFxArt.LoopTicks,
                              true, 3f, f, 0f, f.layer, 0);
                u.disc = Play(FxSource.Shot, f.id, f.pos, Vector3.one * .55f * s, 0f, ShotLoop, true, 3f, null, 0f, f.layer, 3);
                break;
            case ShipAttack.ChainLightning:
                u.point = nose;
                u.next = 0f;
                break;
        }
    }

    static readonly int[] ShotLoop = { 2, 2, 2, 2 };

    void Done(Ult u)
    {
        if (u.owner != null) { u.owner.ulting = false; u.owner.aim = null; }
        u.on = false;
        u.held = u.disc = null;
        u.owner = null;
    }

    void Step(Ult u, float dt)
    {
        if (!u.on) return;
        var f = u.owner;
        var l = u.l;
        float s = f.scale;
        switch (l.attack)
        {
            case ShipAttack.CometStreak:
                while (u.fired < l.shots && u.t >= u.fired * .16f)
                {
                    Launch(f, BoltKind.Straight, Nose(f), Rotate(Dir(f.heading), -ShipAttackRunner.StreakAngle(u.fired) * Mathf.Deg2Rad),
                           Mathf.Max(3f, l.speed * .55f * s), l.width * .6f * s, .55f * s, null);
                    u.fired++;
                }
                if (u.fired >= l.shots) Done(u);
                break;

            case ShipAttack.Gatling:
                {
                    int shots = Mathf.Min(10, l.shots);
                    float every = l.duration / Mathf.Max(1, shots);
                    while (u.fired < shots && u.t >= u.fired * every)
                    {
                        float a = Mathf.Sin(u.fired * 2.4f) * l.width;
                        Vector2 nose = Nose(f);
                        Launch(f, BoltKind.Straight, nose, Rotate(Dir(f.heading), -a * Mathf.Deg2Rad),
                               Mathf.Max(3.5f, l.speed * .5f * s), .13f * s, .32f * s, null);
                        Play(FxSource.Burst, f.id, nose, Vector3.one * .5f * s, f.heading * Mathf.Rad2Deg - 90f - a,
                             ShipFxArt.BurstTicks, false, 0f, f, ReferenceHull * s * .5f, f.layer, 3);
                        u.fired++;
                    }
                    if (u.fired >= shots) Done(u);
                }
                break;

            case ShipAttack.SkyJavelins:
                while (u.fired < l.shots && u.t >= u.fired * .08f)
                {
                    int lane = u.fired == 0 ? 0 : (u.fired % 2 == 1 ? -1 : 1) * ((u.fired + 1) / 2);
                    float x = Mathf.Clamp(f.pos.x + lane * l.range * s, view.xMin + .3f, view.xMax - .3f);
                    var b = Launch(f, BoltKind.Javelin, new Vector2(x, view.yMax + .6f), Vector2.down,
                                   l.speed * .45f, l.width * s, .9f * s, null);
                    if (b != null) b.stopY = f.pos.y + .3f * s;
                    u.fired++;
                }
                if (u.fired >= l.shots) Done(u);
                break;

            case ShipAttack.ChainLightning:
                while (u.on && u.t >= u.next)
                {
                    float reach = (u.hops == 0 ? l.range : l.width) * .8f * s + .4f;
                    var t = NearestUnhit(u, u.point, reach);
                    if (t == null || u.hops >= l.maxHits)
                    {
                        // nothing in reach: the bolt fizzles out ahead
                        if (u.hops == 0) Arc(f, u.point, u.point + Dir(f.heading) * 1.8f * s);
                        Done(u);
                        break;
                    }
                    Arc(f, u.point, t.pos);
                    Play(FxSource.Burst, f.id, t.pos, Vector3.one * .8f * s, 0f, ShipFxArt.BurstTicks, false, 0f, null, 0f, f.layer, 3);
                    if (u.hitCount < u.hit.Length) u.hit[u.hitCount++] = t;
                    u.point = t.pos;
                    ShootDown(t, (t.pos - f.pos).normalized, f.id, f.layer);
                    u.hops++;
                    u.next += .06f;
                }
                break;

            case ShipAttack.Blowtorch:
                {
                    float range = l.range * .7f * s;
                    KillInCone(f, Nose(f), Dir(f.heading), range, l.width);
                    if (u.held == null || !u.held.on) Done(u);
                }
                break;

            case ShipAttack.TractorBeam:
                KillInLine(f, Nose(f), Dir(f.heading), u.point.x, l.width * s);
                if (u.held == null || !u.held.on) Done(u);
                break;

            case ShipAttack.OrbitDisc:
                {
                    u.angle += l.speed * dt;
                    float a = u.angle * Mathf.Deg2Rad;
                    Vector2 disc = f.pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * l.width * s;
                    if (u.disc != null && u.disc.on) u.disc.sr.transform.position = new Vector3(disc.x, disc.y, 0f);
                    KillInRadius(f, disc, l.range * s);
                    if (u.held == null || !u.held.on) Done(u);
                }
                break;

            default:
                Done(u);
                break;
        }
    }

    void Arc(Flyer f, Vector2 a, Vector2 b)
    {
        Vector2 d = b - a;
        float len = Mathf.Max(.2f, d.magnitude);
        Play(FxSource.Loop, f.id, (a + b) * .5f, new Vector3(len, .55f * f.scale, 1f), Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg,
             ShipFxArt.LoopTicks, true, .22f, null, 0f, f.layer, 1);
    }

    // ---------------------------------------------------------- targeting

    // Only ships on the shooter's own depth layer can be hit.
    bool Shootable(Flyer t, Depth layer, Flyer exclude)
    {
        return t != null && t != exclude && t.active && t.layer == layer && t.state != State.Plunge &&
               t.state != State.ZipIn && Overlaps(view, t.pos, -.1f);
    }

    bool Shootable(Flyer t, Flyer by) { return by != null && Shootable(t, by.layer, by); }

    Flyer Nearest(Flyer by, Vector2 from, float reach, bool aheadFirst)
    {
        Flyer best = null;
        float bestD = reach * reach;
        Vector2 fwd = Dir(by.heading);
        for (int i = 0; i < pool.Length; i++)
        {
            var t = pool[i];
            if (!Shootable(t, by)) continue;
            Vector2 d = t.pos - from;
            float score = d.sqrMagnitude;
            if (aheadFirst && Vector2.Dot(d, fwd) < 0f) score *= 2.5f;
            if (score < bestD) { bestD = score; best = t; }
        }
        return best;
    }

    Flyer NearestUnhit(Ult u, Vector2 from, float reach)
    {
        Flyer best = null;
        float bestD = reach * reach;
        for (int i = 0; i < pool.Length; i++)
        {
            var t = pool[i];
            if (!Shootable(t, u.owner)) continue;
            bool hit = false;
            for (int k = 0; k < u.hitCount; k++) if (u.hit[k] == t) { hit = true; break; }
            if (hit) continue;
            float d = (t.pos - from).sqrMagnitude;
            if (d < bestD) { bestD = d; best = t; }
        }
        return best;
    }

    // Fills `picks` with up to n distinct targets, nearest first.
    int Pick(Flyer by, Vector2 from, float reach, int n)
    {
        int count = 0;
        n = Mathf.Min(n, picks.Length);
        while (count < n)
        {
            Flyer best = null;
            float bestD = reach * reach;
            for (int i = 0; i < pool.Length; i++)
            {
                var t = pool[i];
                if (!Shootable(t, by)) continue;
                bool taken = false;
                for (int k = 0; k < count; k++) if (picks[k] == t) { taken = true; break; }
                if (taken) continue;
                float d = (t.pos - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = t; }
            }
            if (best == null) break;
            picks[count++] = best;
        }
        for (int k = count; k < picks.Length; k++) picks[k] = null;
        return count;
    }

    static float SegmentDistanceSq(Vector2 a, Vector2 b, Vector2 p)
    {
        Vector2 ab = b - a;
        float len2 = ab.sqrMagnitude;
        float t = len2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
        return (a + ab * t - p).sqrMagnitude;
    }

    Flyer FirstAlong(Depth layer, Flyer exclude, Vector2 a, Vector2 b, float radius)
    {
        Flyer best = null;
        float bestT = float.MaxValue;
        for (int i = 0; i < pool.Length; i++)
        {
            var t = pool[i];
            if (!Shootable(t, layer, exclude)) continue;
            float r = radius + t.radius;
            if (SegmentDistanceSq(a, b, t.pos) > r * r) continue;
            float along = (t.pos - a).sqrMagnitude;
            if (along < bestT) { bestT = along; best = t; }
        }
        return best;
    }

    void KillInLine(Flyer by, Vector2 nose, Vector2 d, float len, float half)
    {
        Vector2 end = nose + d * len;
        for (int i = 0; i < pool.Length; i++)
        {
            var t = pool[i];
            if (!Shootable(t, by)) continue;
            float r = half + t.radius * .5f;
            if (Vector2.Dot(t.pos - nose, d) < -.2f) continue;
            if (SegmentDistanceSq(nose, end, t.pos) <= r * r) ShootDown(t, d, by.id, by.layer);
        }
    }

    void KillInCone(Flyer by, Vector2 origin, Vector2 d, float range, float halfAngle)
    {
        for (int i = 0; i < pool.Length; i++)
        {
            var t = pool[i];
            if (!Shootable(t, by)) continue;
            Vector2 to = t.pos - origin;
            float dist = to.magnitude;
            if (dist > range + t.radius) continue;
            float slack = dist > t.radius ? Mathf.Asin(Mathf.Clamp01(t.radius / dist)) * Mathf.Rad2Deg : 180f;
            if (Vector2.Angle(d, to) <= halfAngle + slack) ShootDown(t, d, by.id, by.layer);
        }
    }

    void KillInRadius(Flyer by, Vector2 at, float r0)
    {
        for (int i = 0; i < pool.Length; i++)
        {
            var t = pool[i];
            if (!Shootable(t, by)) continue;
            float r = r0 + t.radius;
            if ((t.pos - at).sqrMagnitude <= r * r) ShootDown(t, (t.pos - by.pos).normalized, by.id, by.layer);
        }
    }

    // --------------------------------------------------------------- bolts

    Bolt Launch(Flyer by, BoltKind kind, Vector2 at, Vector2 dir, float speed, float radius, float size, Flyer target)
    {
        Bolt b = null;
        for (int i = 0; i < BoltPool; i++) if (!bolts[i].on) { b = bolts[i]; break; }
        if (b == null) return null;   // over the cap: this shot just isn't fired
        b.on = true;
        b.kind = kind;
        b.ship = by.id;
        b.layer = by.layer;
        b.owner = by;
        b.target = target;
        b.pos = at;
        b.dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.up;
        b.speed = speed;
        b.radius = radius;
        b.size = size;
        b.scale = by.scale;
        b.age = b.travelled = b.spinAngle = 0f;
        b.hops = 0;
        b.maxHops = 1;
        b.range = by.id > 0 ? ShipLoadoutTable.For(by.id).range * .7f * by.scale : 3f;
        b.burst = ShipLoadoutTable.For(by.id).width * .7f * by.scale;
        b.stopY = float.MinValue;
        b.style = WeaponStyleTable.For(by.id);
        for (int h = 0; h < b.hist.Length; h++) b.hist[h] = at;
        b.histCount = 1;
        var spec = Depths[(int)by.layer];
        var mat = LayerMaterial(by.layer);
        b.body.sortingOrder = spec.fxSort + 2;
        b.body.color = spec.fxTint;
        if (mat != null) b.body.sharedMaterial = mat;
        for (int s = 0; s < BoltSegments; s++)
        {
            b.outer[s].sortingOrder = spec.fxSort + 1;
            b.core[s].sortingOrder = spec.fxSort + 1;
            b.outer[s].color = b.style.main * spec.fxTint;
            b.core[s].color = b.style.energy * spec.fxTint;
            if (mat != null) { b.outer[s].sharedMaterial = mat; b.core[s].sharedMaterial = mat; }
        }
        b.body.sprite = WeaponArt.Shot(b.ship, WeaponArt.ShotLoopFrames);   // launch smear
        b.body.enabled = b.body.sprite != null;
        DrawBolt(b, 0f);
        return b;
    }

    void TickBolts(float dt)
    {
        for (int i = 0; i < BoltPool; i++)
        {
            var b = bolts[i];
            if (!b.on) continue;
            b.age += dt;
            Vector2 from = b.pos;
            if (b.target != null && (!b.target.active || b.target.layer != b.layer || b.target.state == State.Plunge)) b.target = null;
            if ((b.kind == BoltKind.Homing || b.kind == BoltKind.Ricochet) && b.target != null)
                b.dir = PowerFx.SteerHeading(b.dir, b.target.pos - from, b.kind == BoltKind.Ricochet ? 900f : 420f, dt);
            Vector2 to = from + b.dir * b.speed * dt;
            b.travelled += b.speed * dt;
            b.pos = to;

            // the shooter may have gone (or flown again elsewhere): the shot
            // keeps its own layer and only spares the ship that fired it
            var owner = b.owner != null && b.owner.active && b.owner.layer == b.layer ? b.owner : null;
            switch (b.kind)
            {
                case BoltKind.Javelin:
                    for (int k = 0; k < pool.Length; k++)
                    {
                        var t = pool[k];
                        if (!Shootable(t, b.layer, owner)) continue;
                        if (Mathf.Abs(t.pos.x - to.x) > b.radius + t.radius * .5f) continue;
                        if (t.pos.y - t.radius > from.y || t.pos.y + t.radius < to.y) continue;
                        ShootDown(t, Vector2.down, b.ship, b.layer);
                    }
                    if (to.y <= b.stopY)
                    {
                        Vector2 land = new Vector2(to.x, b.stopY);
                        Play(FxSource.Burst, b.ship, land, Vector3.one * 1.1f * b.scale, 0f, ShipFxArt.BurstTicks, false, 0f, null, 0f, b.layer, 3);
                        float top = view.yMax;
                        Play(FxSource.Loop, b.ship, new Vector2(to.x, (b.stopY + top) * .5f),
                             new Vector3(b.radius * 2f * 1.4f, Mathf.Max(1f, top - b.stopY), 1f), 0f, ShipFxArt.BurstTicks, false, 0f,
                             null, 0f, b.layer, 1);
                        FinishBolt(b, false);
                        continue;
                    }
                    break;
                case BoltKind.Fireball:
                    {
                        var hit = FirstAlong(b.layer, owner, from, to, b.radius);
                        if (hit != null || b.travelled >= b.range) { Burst(b, hit != null ? hit.pos : to, owner); continue; }
                    }
                    break;
                case BoltKind.Ricochet:
                    {
                        var hit = FirstAlong(b.layer, owner, from, to, b.radius);
                        if (hit != null)
                        {
                            ShootDown(hit, b.dir, b.ship, b.layer);
                            b.hops++;
                            b.age = 0f;
                            b.target = b.hops < b.maxHops ? NearestOn(b.layer, owner, to, 3.1f * b.scale + 1f) : null;
                            if (b.target == null && b.hops >= b.maxHops) { FinishBolt(b, true); continue; }
                        }
                        else if (b.target == null && b.hops > 0 && b.age > .5f) { FinishBolt(b, false); continue; }
                    }
                    break;
                default:
                    {
                        var hit = FirstAlong(b.layer, owner, from, to, b.radius);
                        if (hit != null) { ShootDown(hit, b.dir, b.ship, b.layer); FinishBolt(b, true); continue; }
                    }
                    break;
            }
            if (!Overlaps(view, to, 1f) || b.age > 4f) { FinishBolt(b, false); continue; }
            DrawBolt(b, dt);
        }
    }

    Flyer NearestOn(Depth layer, Flyer exclude, Vector2 from, float reach)
    {
        Flyer best = null;
        float bestD = reach * reach;
        for (int i = 0; i < pool.Length; i++)
        {
            var t = pool[i];
            if (!Shootable(t, layer, exclude)) continue;
            float d = (t.pos - from).sqrMagnitude;
            if (d < bestD) { bestD = d; best = t; }
        }
        return best;
    }

    void Burst(Bolt b, Vector2 at, Flyer owner)
    {
        float r = b.burst;
        Play(FxSource.Burst, b.ship, at, Vector3.one * r * 2.3f, 0f, ShipFxArt.BurstTicks, false, 0f, null, 0f, b.layer, 3);
        for (int i = 0; i < pool.Length; i++)
        {
            var t = pool[i];
            if (!Shootable(t, b.layer, owner)) continue;
            float reach = r + t.radius * .5f;
            if ((t.pos - at).sqrMagnitude <= reach * reach) ShootDown(t, (t.pos - at).normalized, b.ship, b.layer);
        }
        FinishBolt(b, false);
    }

    void FinishBolt(Bolt b, bool hit)
    {
        if (hit)
        {
            var spec = Depths[(int)b.layer];
            PlayFx(FlipbookFx.Mode.WeaponImpact, b.pos, .5f * b.scale, b.ship, spec.fxTint, spec.fxSort + 4);
        }
        b.on = false;
        b.owner = b.target = null;
        b.body.enabled = false;
        for (int s = 0; s < BoltSegments; s++) { b.outer[s].enabled = false; b.core[s].enabled = false; }
    }

    void DrawBolt(Bolt b, float dt)
    {
        int frame = b.age < .07f ? WeaponArt.ShotLoopFrames : WeaponArt.FrameAt(ShotLoop, b.age, true);
        var sprite = WeaponArt.Shot(b.ship, frame);
        if (sprite != null && b.body.sprite != sprite) b.body.sprite = sprite;
        var t = b.body.transform;
        t.position = new Vector3(b.pos.x, b.pos.y, 0f);
        t.localScale = new Vector3(b.size, b.size, 1f);
        if (b.style.shotSpin != 0f)
        {
            b.spinAngle += b.style.shotSpin * dt;
            t.rotation = Quaternion.Euler(0f, 0f, b.spinAngle);
        }
        else t.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(b.dir.y, b.dir.x) * Mathf.Rad2Deg - 90f);

        var h = b.hist;
        h[0] = b.pos;
        float spacing = .1f * b.scale;
        if ((h[0] - h[1]).sqrMagnitude >= spacing * spacing || b.histCount == 1)
        {
            for (int i = h.Length - 1; i > 0; i--) h[i] = h[i - 1];
            if (b.histCount < h.Length) b.histCount++;
        }
        float w = Mathf.Clamp(b.size * .22f, .04f, .14f);
        for (int i = 0; i < BoltSegments; i++)
        {
            bool on = i + 1 < b.histCount;
            float taper = 1f - i / (float)BoltSegments;
            Vector3 a = h[i], c = h[i + 1];
            Segment(b.outer[i], on, a, c, w * taper, b.outer[i].color.a);
            Segment(b.core[i], on && i < 2, a, c, w * taper * .45f, b.core[i].color.a);
        }
    }

    // ----------------------------------------------------- attack drawings

    Fx Play(FxSource src, int ship, Vector2 at, Vector3 scale, float angle, int[] ticks, bool loop, float life,
            Flyer follow, float along, Depth layer, int sortOffset)
    {
        Fx e = null;
        for (int i = 0; i < FxPool; i++) if (!fxs[i].on) { e = fxs[i]; break; }
        if (e == null) return null;
        var spec = Depths[(int)layer];
        e.on = true;
        e.src = src;
        e.ship = ship;
        e.ticks = ticks;
        e.loop = loop;
        e.life = life;
        e.clock = 0f;
        e.frame = 0;
        e.follow = follow;
        e.along = along;
        e.angleOff = follow != null ? angle - (follow.heading * Mathf.Rad2Deg - 90f) : 0f;
        e.tint = spec.fxTint;
        var mat = LayerMaterial(layer);
        if (mat != null) e.sr.sharedMaterial = mat;
        e.sr.sortingOrder = spec.fxSort + sortOffset;
        e.sr.color = e.tint;
        var t = e.sr.transform;
        t.position = new Vector3(at.x, at.y, 0f);
        t.rotation = Quaternion.Euler(0f, 0f, angle);
        t.localScale = scale;
        ApplyFx(e);
        Follow(e);
        e.sr.enabled = e.sr.sprite != null;
        return e;
    }

    void ApplyFx(Fx e)
    {
        Sprite s;
        switch (e.src)
        {
            case FxSource.Loop: s = ShipFxArt.AttackLoop(e.ship, e.frame); break;
            case FxSource.Burst: s = ShipFxArt.AttackBurst(e.ship, e.frame); break;
            default: s = WeaponArt.Shot(e.ship, e.frame); break;
        }
        if (s != null && e.sr.sprite != s) e.sr.sprite = s;
    }

    void Follow(Fx e)
    {
        var f = e.follow;
        if (f == null) return;
        Vector2 p = f.pos + Dir(f.heading) * e.along;
        var t = e.sr.transform;
        t.position = new Vector3(p.x, p.y, 0f);
        t.rotation = Quaternion.Euler(0f, 0f, f.heading * Mathf.Rad2Deg - 90f + e.angleOff);
    }

    void StopFx(Fx e)
    {
        e.on = false;
        e.follow = null;
        e.sr.enabled = false;
    }

    void TickAttackFx(float dt)
    {
        for (int i = 0; i < FxPool; i++)
        {
            var e = fxs[i];
            if (!e.on) continue;
            if (e.follow != null && !e.follow.active) { StopFx(e); continue; }
            e.clock += dt;
            if (e.life > 0f && e.clock >= e.life) { StopFx(e); continue; }
            int f = WeaponArt.FrameAt(e.ticks, e.clock, e.loop);
            if (!e.loop && f >= e.ticks.Length) { StopFx(e); continue; }
            if (f != e.frame) { e.frame = f; ApplyFx(e); }
            Follow(e);
            if (e.life > 0f)
            {
                float left = 1f - e.clock / e.life;
                var c = e.tint;
                c.a *= left > .2f ? 1f : left > .1f ? .6f : .3f;
                if (e.sr.color != c) e.sr.color = c;
            }
        }
    }

    // ------------------------------------------------------- shoot-downs

    // `ship` fired the shot, from `layer`.
    void ShootDown(Flyer victim, Vector2 dir, int ship, Depth layer)
    {
        if (victim == null || !victim.active || victim.state == State.Plunge) return;
        ShootDowns++;
        if (layer != victim.layer) CrossLayerKills++;   // can't happen; the tests watch it
        var spec = Depths[(int)victim.layer];
        PlayFx(FlipbookFx.Mode.WeaponImpact, victim.pos, .75f * victim.scale, ship, spec.fxTint, spec.fxSort + 4);
        if (pursuerA == victim || pursuerB == victim) EndPursuit();

        // now and then the stricken ship careens into the logo or a button
        if (victim.layer != Depth.Back && plunger == null && now >= nextPlungeAt - plungeInterval.x * .5f &&
            Random.value < StrickenPlungeChance && StartPlunge(victim, true))
        {
            nextPlungeAt = Mathf.Max(nextPlungeAt, now + plungeInterval.x * .6f);
            return;
        }

        if (!Wreck(victim, dir))
        {
            Explode(victim, .9f);
            Pops++;
        }
        Retire(victim);
        nextSpawnAt[(int)victim.layer] = Mathf.Max(nextSpawnAt[(int)victim.layer], now + respawnDelay.x);
    }

    // The hull's wreck fragments fly off on arcs into the screen's side
    // walls (DeathCrash's flight, on this layer and scale).
    bool Wreck(Flyer f, Vector2 dir)
    {
        if (ActiveWrecks >= MaxWrecks) return false;
        var set = FragsFor(f);
        if (set == null || set.count <= 0) return false;
        int free = 0;
        for (int i = 0; i < PiecePool; i++) if (!pieces[i].on) free++;
        if (free < set.count) return false;

        wreckGroup++;
        Wrecks++;
        f.fragsBusyUntil = now + 3f;
        var spec = Depths[(int)f.layer];
        var mat = LayerMaterial(f.layer);
        float s = f.scale;
        int mainSide = Mathf.Abs(dir.x) > .25f ? (dir.x < 0f ? -1 : 1) : (f.pos.x < view.center.x ? -1 : 1);
        Vector3 scale = new Vector3(Mathf.Abs(f.tr.localScale.x), Mathf.Abs(f.tr.localScale.y), 1f);
        float worldScale = Mathf.Max(scale.x, scale.y);
        int k = 0;
        for (int i = 0; i < PiecePool && k < set.count; i++)
        {
            var p = pieces[i];
            if (p.on) continue;
            Vector3 start3 = f.tr.TransformPoint(set.local[k].x, set.local[k].y, 0f);
            Vector2 start = start3;
            Vector2 outward = start - f.pos;
            if (outward.sqrMagnitude < 1e-6f) outward = Random.insideUnitCircle;
            outward.Normalize();
            bool main = k == set.main;
            int side = main ? mainSide
                     : Mathf.Abs(outward.x) < .2f || Random.value < .3f ? (Random.value < .5f ? -1 : 1)
                     : outward.x < 0f ? -1 : 1;
            p.on = true; p.landed = false; p.fall = false; p.main = main;
            p.group = wreckGroup; p.side = side; p.layer = f.layer; p.ship = f.id;
            p.radius = Mathf.Max(.03f, set.radius[k] * worldScale);
            p.start = start;
            float inset = Mathf.Clamp(p.radius * .35f, .02f, .12f);
            float y = Mathf.Clamp(start.y + Random.Range(-1.4f, 1f) * s, safe.yMin + .5f, safe.yMax - .5f);
            p.target = new Vector2(side > 0 ? view.xMax - inset : view.xMin + inset, y);
            Vector2 mid = Vector2.Lerp(start, p.target, .35f);
            p.control = new Vector2(mid.x + outward.x * .4f * s, Mathf.Min(Mathf.Max(start.y, y) + Random.Range(.4f, 1.1f) * s, view.yMax - .3f));
            p.delay = main ? .04f : Random.Range(0f, .18f);
            p.dur = main ? Random.Range(.72f, .9f) : Random.Range(.5f, .82f);
            p.spin = (Random.value < .5f ? -1f : 1f) * (main ? Random.Range(240f, 420f) : Random.Range(480f, 1080f));
            p.t = 0f; p.emit = Random.Range(0f, .03f);
            p.angle = f.tr.eulerAngles.z;
            p.baseScale = scale;
            p.sr.sprite = set.sprites[k];
            p.sr.color = Color.white * spec.fxTint;
            p.sr.sortingOrder = spec.fxSort + 2 + (main ? 1 : 0);
            if (mat != null) p.sr.sharedMaterial = mat;
            p.sr.enabled = p.sr.sprite != null;
            var t = p.sr.transform;
            t.position = start3;
            t.rotation = Quaternion.Euler(0f, 0f, p.angle);
            t.localScale = scale;
            k++;
        }
        PeakWrecks = Mathf.Max(PeakWrecks, ActiveWrecks);
        // the blast where it was hit, sparks off the hull
        float size = TargetExplosion.WorldSizeFor(TargetExplosion.Size.Small, TargetExplosion.Kind.Metal) * s * .8f;
        PlayFx(FlipbookFx.Mode.Flash, f.pos, size * 1.15f, f.id, WeaponStyleTable.For(f.id).energy * spec.fxTint, spec.fxSort);
        for (int i = 0; i < 5; i++)
            Emit(ShipDamageFx.RowSpark, f.pos, Random.insideUnitCircle.normalized * Random.Range(1.2f, 2.4f) * Mathf.Sqrt(s),
                 Random.Range(.2f, .32f), .09f * s, .04f * s, -5f, 0f, 1.5f, spec.fxTint, spec.fxSort + 4, f.layer);
        return true;
    }

    void TickPieces(float dt)
    {
        for (int i = 0; i < PiecePool; i++)
        {
            var p = pieces[i];
            if (!p.on) continue;
            p.t += dt;
            if (p.fall) { Tumble(p, dt); continue; }
            if (!p.landed) FlyPiece(p, dt);
            else StickPiece(p, dt);
        }
    }

    void FlyPiece(Piece p, float dt)
    {
        float t = p.t - p.delay;
        var tr = p.sr.transform;
        if (t < 0f) return;
        float u = Mathf.Clamp01(t / p.dur);
        float e = u * (.55f + .45f * u);   // speeds up into the wall
        Vector2 a = Vector2.Lerp(p.start, p.control, e), b = Vector2.Lerp(p.control, p.target, e);
        Vector2 pos = Vector2.Lerp(a, b, e);
        tr.position = new Vector3(pos.x, pos.y, 0f);
        p.angle += p.spin * dt;
        tr.rotation = Quaternion.Euler(0f, 0f, p.angle);
        var spec = Depths[(int)p.layer];
        p.emit -= dt;
        if (p.emit <= 0f)
        {
            p.emit = p.main ? .035f : .06f;
            Emit(ShipDamageFx.RowSmoke, pos, Random.insideUnitCircle * .2f + new Vector2(0f, .15f), Random.Range(.4f, .6f),
                 (p.main ? .1f : .07f) * Mathf.Sqrt(p.baseScale.x * 2f), (p.main ? .24f : .16f), 0f, Random.Range(-60f, 60f), .8f,
                 new Color(.52f, .48f, .58f, .8f) * spec.fxTint, spec.fxSort + 1, p.layer);
            if (Random.value < .35f)
                Emit(ShipDamageFx.RowSpark, pos, Random.insideUnitCircle * 1.4f, Random.Range(.14f, .24f), .08f, .035f, -4f, 0f,
                     1f, spec.fxTint, spec.fxSort + 4, p.layer);
        }
        if (u >= 1f) Land(p);
    }

    void Land(Piece p)
    {
        p.landed = true;
        p.landedAt = p.t;
        WallImpacts++;
        float edge = p.side > 0 ? view.xMax : view.xMin;
        if (Mathf.Abs(p.target.x - edge) > .3f) WallImpactsOffEdge++;
        Vector2 contact = new Vector2(edge, p.target.y);
        var spec = Depths[(int)p.layer];
        float k = Mathf.Sqrt(p.baseScale.x * 2.5f);
        if (p.main)
        {
            PlayFx(FlipbookFx.Mode.Flash, contact, .55f * k, p.ship, WeaponStyleTable.For(p.ship).energy * spec.fxTint, spec.fxSort + 3);
            PlayFx(FlipbookFx.Mode.Ring, contact, .7f * k, p.ship, WeaponStyleTable.For(p.ship).energy * spec.fxTint, spec.fxSort + 3);
        }
        int sparks = p.main ? 7 : 4;
        for (int s = 0; s < sparks; s++)
        {
            var d = new Vector2(-p.side * Random.Range(.35f, 1f), Random.Range(-.8f, 1f)).normalized;
            Emit(ShipDamageFx.RowSpark, contact, d * Random.Range(1.4f, 3f) * k * .6f, Random.Range(.22f, .4f), .1f * k * .6f, .04f,
                 -7f, 0f, 1.2f, spec.fxTint, spec.fxSort + 4, p.layer);
        }
        Emit(ShipDamageFx.RowScrap, contact, new Vector2(-p.side * Random.Range(.3f, 1f), Random.Range(.3f, 1.2f)),
             Random.Range(.45f, .7f), .08f * k * .6f, .07f, -6f, Random.Range(-720f, 720f), .5f, spec.fxTint, spec.fxSort + 4, p.layer);
        Emit(ShipDamageFx.RowSmoke, contact, new Vector2(-p.side * Random.Range(.1f, .35f), Random.Range(.15f, .4f)),
             Random.Range(.5f, .8f), .12f, p.main ? .34f : .24f, 0f, Random.Range(-40f, 40f), 1f,
             new Color(.6f, .56f, .64f, .85f) * spec.fxTint, spec.fxSort + 1, p.layer);
    }

    // Stuck on the wall: crumples against it, holds, fades (DeathCrash's beat).
    void StickPiece(Piece p, float dt)
    {
        float s = p.t - p.landedAt;
        float k = Mathf.Clamp01(s / DeathCrash.Crumple);
        float squash = Mathf.Sin(k * Mathf.PI) * .3f;
        float rest = k >= 1f ? .12f : 0f;
        var tr = p.sr.transform;
        tr.localScale = new Vector3(p.baseScale.x * (1f - squash - rest), p.baseScale.y * (1f + squash * .45f), 1f);
        const float Hold = .35f, Fade = .45f;
        float alpha = s < Hold ? 1f : 1f - (s - Hold) / Fade;
        if (alpha <= 0f) { p.on = false; p.sr.enabled = false; return; }
        var tint = Depths[(int)p.layer].fxTint;
        var c = Color.Lerp(new Color(.55f, .5f, .55f, 1f), Color.white, alpha) * tint;
        c.a = alpha * tint.a;
        p.sr.color = c;
    }

    // A piece knocked off something (the logo, a button, a plain pop):
    // ballistic, spinning, gone in its life.
    void Tumble(Piece p, float dt)
    {
        if (p.t >= p.life) { p.on = false; p.sr.enabled = false; return; }
        p.vel.y += p.gravity * dt;
        var tr = p.sr.transform;
        Vector3 pos = tr.position + new Vector3(p.vel.x, p.vel.y, 0f) * dt;
        tr.position = pos;
        p.angle += p.spin * dt;
        tr.rotation = Quaternion.Euler(0f, 0f, p.angle);
        float k = p.t / p.life;
        float a = k < .6f ? 1f : k < .8f ? .6f : .3f;   // stepped fade, cel style
        var c = Color.white;
        c.a = a;
        p.sr.color = c;
    }

    // ------------------------------------------------- logo / button crashes

    void MaybePlunge()
    {
        if (now < nextPlungeAt) return;
        if (plunger != null) { nextPlungeAt = now + 1f; return; }
        Flyer best = null;
        float bestD = float.MaxValue;
        for (int i = 0; i < pool.Length; i++)
        {
            var f = pool[i];
            if (!f.active || f.layer == Depth.Back || f.state != State.Cruise || f.zoomer || f.ulting || f.trick != Trick.None) continue;
            if (!InsideSafe(f.pos, .6f)) continue;
            float d = logo.width > 0f ? (f.pos - logo.center).sqrMagnitude : 0f;
            d *= Random.Range(.6f, 1.4f);
            if (d < bestD) { bestD = d; best = f; }
        }
        if (best != null && StartPlunge(best, false))
            nextPlungeAt = now + Random.Range(plungeInterval.x, plungeInterval.y);
        else nextPlungeAt = now + 1f;
    }

    // Sends f diving into the logo (or a button). False when there is
    // nothing to hit on this screen.
    // target: -2 pick (logo or a button), -1 the logo, else that button.
    public bool StartPlunge(Flyer f, bool stricken, int target = -2)
    {
        if (f == null || !f.active || plunger != null) return false;
        int into = -1;
        bool logoOk = logo.width > .2f && logo.height > .1f;
        int live = 0;
        for (int i = 0; i < buttons.Length; i++) if (ButtonLive(i)) live++;
        if (!logoOk && live == 0) return false;
        if (target >= 0 && target < buttons.Length && ButtonLive(target)) into = target;
        else if (target == -1 && logoOk) into = -1;
        else if (live > 0 && (!logoOk || Random.value < .4f))
        {
            int pick = Random.Range(0, live);
            for (int i = 0; i < buttons.Length; i++)
                if (ButtonLive(i) && pick-- == 0) { into = i; break; }
        }
        Vector2 at;
        if (into < 0)
        {
            // into the lettering, not the logo's glow margin
            Rect r = LogoLetters;
            at = new Vector2(r.center.x + Random.Range(-.3f, .3f) * r.width, r.center.y + Random.Range(-.15f, .15f) * r.height);
        }
        else
        {
            // into the end of the button's word, the side it comes from
            Rect r = LabelRect(into);
            float side = f.pos.x < r.center.x ? -1f : 1f;
            at = new Vector2(r.center.x + side * r.width * .42f, r.center.y);
        }
        EndUltimate(f);
        if (pursuerA == f || pursuerB == f) EndPursuit();
        if (f.trail != null) ReleaseTrail(f);
        ReleaseStars(f);
        for (int i = 0; i < pool.Length; i++)
            if (pool[i].active && pool[i].partner == f && pool[i].state == State.Formation) BreakFormation(pool[i]);
        f.partner = null;
        f.state = State.Plunge;
        f.stateT = 0f;
        f.trick = Trick.None;
        f.stricken = stricken;
        f.plungeInto = into;
        f.plungeAt = at;
        f.spinVel = (Random.value < .5f ? -1f : 1f) * (stricken ? 420f : 90f);
        f.speed = Mathf.Max(f.speed, f.baseSpeed);
        f.alpha = 1f;
        f.grow = 1f;
        f.stretchX = f.stretchY = 1f;
        ApplyLook(f);
        plunger = f;
        Plunges++;
        return true;
    }

    bool ButtonLive(int i)
    {
        var b = buttons[i];
        if (b.rt == null || b.button == null || !b.button.isActiveAndEnabled) return false;
        Rect r = ButtonRect(i);
        return r.width > .1f && r.height > .05f && Overlaps(safe, r.center, 0f);
    }

    // The PAUSE lettering inside the logo sprite's bounds (the bounds carry
    // a wide glow margin).
    public Rect LogoLetters
    {
        get
        {
            return new Rect(logo.center.x - logo.width * .34f, logo.center.y - logo.height * .14f, logo.width * .68f, logo.height * .28f);
        }
    }

    // Where a button's word is drawn: the label's preferred size inside its
    // box, placed by its alignment (rows are as wide as the menu).
    public Rect LabelRect(int i)
    {
        Rect box = ButtonRect(i);
        var text = buttons[i].text;
        var lrt = text != null ? text.rectTransform : null;
        if (lrt == null || lrt.rect.width <= 0f || lrt.rect.height <= 0f) return box;
        Rect lbox = WorldRectOf(lrt);
        float w = Mathf.Min(1f, text.preferredWidth / lrt.rect.width) * lbox.width;
        float h = Mathf.Min(1f, text.preferredHeight / lrt.rect.height) * lbox.height;
        float x;
        switch (text.alignment)
        {
            case TextAnchor.UpperLeft: case TextAnchor.MiddleLeft: case TextAnchor.LowerLeft: x = lbox.xMin + w * .5f; break;
            case TextAnchor.UpperRight: case TextAnchor.MiddleRight: case TextAnchor.LowerRight: x = lbox.xMax - w * .5f; break;
            default: x = lbox.center.x; break;
        }
        float y;
        switch (text.alignment)
        {
            case TextAnchor.UpperLeft: case TextAnchor.UpperCenter: case TextAnchor.UpperRight: y = lbox.yMax - h * .5f; break;
            case TextAnchor.LowerLeft: case TextAnchor.LowerCenter: case TextAnchor.LowerRight: y = lbox.yMin + h * .5f; break;
            default: y = lbox.center.y; break;
        }
        return new Rect(x - w * .5f, y - h * .5f, w, h);
    }

    public Rect ButtonRect(int i)
    {
        return buttons[i].rt != null ? WorldRectOf(buttons[i].rt) : Rect.zero;
    }

    Rect WorldRectOf(RectTransform rt)
    {
        rt.GetWorldCorners(corners);
        Vector2 a = corners[0], b = corners[2];
        if (buttonCanvas == null || buttonCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            // an overlay canvas's corners are screen pixels
            int w = screenW > 0 ? screenW : Screen.width, h = screenH > 0 ? screenH : Screen.height;
            if (w <= 0 || h <= 0) return Rect.zero;
            a = ScreenToWorld(corners[0], w, h);
            b = ScreenToWorld(corners[2], w, h);
        }
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    Vector2 ScreenToWorld(Vector3 p, int w, int h)
    {
        return new Vector2(view.x + view.width * p.x / w, view.y + view.height * p.y / h);
    }

    void FlyPlunge(Flyer f, float dt)
    {
        // out of control: the spin winds up, it dives harder and harder
        float want = Mathf.Atan2(f.plungeAt.y - f.pos.y, f.plungeAt.x - f.pos.x);
        float diff = Mathf.DeltaAngle(f.heading * Mathf.Rad2Deg, want * Mathf.Rad2Deg) * Mathf.Deg2Rad;
        float rate = 2.5f + 3f * f.stateT;
        float prev = f.heading;
        f.heading += Mathf.Clamp(diff * 4f, -rate, rate) * dt;
        f.turn = (f.heading - prev) / dt;
        float top = 3.3f * Mathf.Sqrt(f.scale);
        f.speed = Mathf.MoveTowards(f.speed, top, 3.2f * dt);
        f.pos += Dir(f.heading) * f.speed * dt;
        f.spinVel = Mathf.MoveTowards(f.spinVel, Mathf.Sign(f.spinVel) * (f.stricken ? 900f : 620f), 500f * dt);
        f.spin += f.spinVel * dt;
        f.flame = f.stricken ? .2f : DockLaunch.Flare(Mathf.Clamp01(f.stateT));
        f.emit -= dt;
        if (f.emit <= 0f)
        {
            f.emit = .045f;
            Emit(ShipDamageFx.RowSmoke, f.pos, Random.insideUnitCircle * .2f, Random.Range(.35f, .5f), .1f * f.scale,
                 .22f * f.scale, 0f, Random.Range(-60f, 60f), .8f, new Color(.52f, .48f, .58f, .8f), PlungeSortBase + 1, f.layer);
        }

        bool there;
        if (f.plungeInto < 0)
        {
            // well into the letters, not grazing their edge
            Rect l = LogoLetters;
            var core = new Rect(l.center.x - l.width * .35f, l.center.y - l.height * .2f, l.width * .7f, l.height * .4f);
            there = Overlaps(core, f.pos, 0f) || (f.pos - f.plungeAt).sqrMagnitude < .1f * .1f;
        }
        else
        {
            Rect r = LabelRect(f.plungeInto);
            there = Overlaps(r, f.pos, f.radius * .3f) || (f.pos - f.plungeAt).sqrMagnitude < .1f * .1f;
        }
        if (there) { PlungeImpact(f); return; }
        if (f.stateT > 3.5f || !Overlaps(view, f.pos, .5f))
        {
            // missed somehow: it goes down into the wall instead
            plunger = null;
            f.state = State.Cruise;
            if (!Wreck(f, Dir(f.heading))) { Explode(f, .9f); Pops++; }
            Retire(f);
        }
    }

    void PlungeImpact(Flyer f)
    {
        plunger = null;
        Vector2 at = f.pos;
        int ship = f.id;
        Color energy = WeaponStyleTable.For(ship).energy;
        float s = f.scale;
        LastImpactPoint = at;
        if (f.plungeInto < 0) { LogoCrashes++; LastLogoImpactAt = now; }
        else { ButtonCrashes++; LastButtonImpactAt = now; LastButtonHit = f.plungeInto; Wobble(f.plungeInto); }

        // a short flash and ring on top of it, sparks spraying back
        PlayFx(FlipbookFx.Mode.Flash, at, 1.25f * s, ship, energy, LogoFxSort + 1);
        PlayFx(FlipbookFx.Mode.Ring, at, 1.6f * s, ship, energy, LogoFxSort);
        PlayFx(FlipbookFx.Mode.WeaponImpact, at, .9f * s, ship, Color.white, LogoFxSort + 2);
        Vector2 back = -Dir(f.heading);
        for (int i = 0; i < 14; i++)
        {
            Vector2 d = (back + Random.insideUnitCircle * 1.1f).normalized;
            Emit(ShipDamageFx.RowSpark, at, d * Random.Range(2.2f, 4f), Random.Range(.18f, .32f), .1f * s, .04f, -8f, 0f, 1.2f,
                 Color.white, LogoFxSort + 2, Depth.Front);
        }

        // the hull bounces off in pieces and falls away, quickly
        var set = FragsFor(f);
        if (set != null)
        {
            f.fragsBusyUntil = now + 1f;
            Vector3 scale = new Vector3(Mathf.Abs(f.tr.localScale.x), Mathf.Abs(f.tr.localScale.y), 1f);
            int k = 0;
            for (int i = 0; i < PiecePool && k < set.count; i++)
            {
                var p = pieces[i];
                if (p.on) continue;
                Vector3 start = f.tr.TransformPoint(set.local[k].x, set.local[k].y, 0f);
                Vector2 out2 = (Vector2)start - at;
                out2 = out2.sqrMagnitude > 1e-6f ? out2.normalized : Random.insideUnitCircle.normalized;
                p.on = true; p.fall = true; p.landed = false; p.main = false;
                p.layer = f.layer; p.ship = ship; p.t = 0f;
                p.life = Random.Range(.42f, .52f);
                p.vel = (out2 * 1.2f + back * 1.6f) * Random.Range(1.6f, 2.6f) + new Vector2(0f, 1.6f);
                p.gravity = -11f;
                p.spin = (Random.value < .5f ? -1f : 1f) * Random.Range(500f, 1000f);
                p.angle = f.tr.eulerAngles.z;
                p.baseScale = scale;
                p.sr.sprite = set.sprites[k];
                p.sr.color = Color.white;
                p.sr.sortingOrder = LogoFxSort + 1;
                if (spriteMaterial != null) p.sr.sharedMaterial = spriteMaterial;
                p.sr.enabled = p.sr.sprite != null;
                var tr = p.sr.transform;
                tr.position = start;
                tr.rotation = Quaternion.Euler(0f, 0f, p.angle);
                tr.localScale = scale;
                k++;
            }
        }
        f.state = State.Cruise;
        Retire(f);
        nextSpawnAt[(int)f.layer] = Mathf.Max(nextSpawnAt[(int)f.layer], now + respawnDelay.x);
    }

    // ------------------------------------------------------- button wobble

    // Held cel poses on the label (x, y, ticks); the last is exactly its own scale.
    static readonly Vector3[] WobblePoses =
    {
        new Vector3(1.1f, .88f, 2), new Vector3(.95f, 1.06f, 2), new Vector3(1.03f, .98f, 2), new Vector3(1f, 1f, 0),
    };

    void Wobble(int i)
    {
        if (i < 0 || i >= buttons.Length || buttons[i].label == null) return;
        if (!buttons[i].wobbling) buttons[i].labelScale = buttons[i].label.localScale;
        buttons[i].wobbling = true;
        buttons[i].wobbleT = 0f;
    }

    void TickWobbles(float dt)
    {
        for (int i = 0; i < buttons.Length; i++)
        {
            if (!buttons[i].wobbling) continue;
            var label = buttons[i].label;
            if (label == null) { buttons[i].wobbling = false; continue; }
            buttons[i].wobbleT += dt;
            float ticks = buttons[i].wobbleT * 24f;
            int pose = 0;
            float acc = 0f;
            while (pose < WobblePoses.Length - 1 && ticks >= acc + WobblePoses[pose].z) { acc += WobblePoses[pose].z; pose++; }
            var b = buttons[i].labelScale;
            if (pose >= WobblePoses.Length - 1)
            {
                label.localScale = b;   // exactly as it was
                buttons[i].wobbling = false;
                continue;
            }
            var p = WobblePoses[pose];
            label.localScale = new Vector3(b.x * p.x, b.y * p.y, b.z);
        }
    }

    void RestoreButtons()
    {
        for (int i = 0; i < buttons.Length; i++)
        {
            if (!buttons[i].wobbling) continue;
            if (buttons[i].label != null) buttons[i].label.localScale = buttons[i].labelScale;
            buttons[i].wobbling = false;
        }
    }

    // ----------------------------------------------------------- particles

    void Emit(int row, Vector2 pos, Vector2 vel, float life, float size0, float size1, float gravity, float spin, float drag,
              Color tint, int order, Depth layer)
    {
        int slot = -1;
        for (int n = 0; n < ParticlePool; n++)
        {
            int j = (nextParticle + n) % ParticlePool;
            if (!particles[j].on) { slot = j; break; }
        }
        if (slot < 0) slot = nextParticle;
        nextParticle = (slot + 1) % ParticlePool;
        var sprite = ShipDamageFx.Frame(row, Random.Range(0, 4));
        var q = particles[slot];
        if (sprite == null) return;
        q.on = true;
        q.pos = pos; q.vel = vel; q.age = 0f; q.life = life;
        q.size0 = size0; q.size1 = size1; q.gravity = gravity; q.spin = spin; q.drag = drag;
        q.angle = Random.Range(0f, 360f);
        q.tint = tint;
        q.sr.sprite = sprite;
        q.sr.sortingOrder = order;
        var mat = LayerMaterial(layer);
        if (mat != null) q.sr.sharedMaterial = mat;
        q.sr.color = tint;
        q.sr.enabled = true;
        var t = q.sr.transform;
        t.position = new Vector3(pos.x, pos.y, 0f);
        t.localScale = new Vector3(size0, size0, 1f);
    }

    void TickParticles(float dt)
    {
        for (int i = 0; i < ParticlePool; i++)
        {
            var q = particles[i];
            if (!q.on) continue;
            q.age += dt;
            if (q.age >= q.life) { q.on = false; q.sr.enabled = false; continue; }
            q.vel.y += q.gravity * dt;
            q.vel *= Mathf.Max(0f, 1f - q.drag * dt);
            q.pos += q.vel * dt;
            q.angle += q.spin * dt;
            float k = q.age / q.life;
            var t = q.sr.transform;
            t.position = new Vector3(q.pos.x, q.pos.y, 0f);
            t.rotation = Quaternion.Euler(0f, 0f, q.angle);
            float sz = Mathf.Lerp(q.size0, q.size1, k);
            t.localScale = new Vector3(sz, sz, 1f);
            var c = q.tint;
            c.a *= 1f - k * k;
            q.sr.color = c;
        }
    }

    void TickCombatFx(float dt)
    {
        TickBolts(dt);
        TickAttackFx(dt);
        TickPieces(dt);
        TickParticles(dt);
        TickWobbles(dt);
    }
}
