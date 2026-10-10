using System.Collections.Generic;
using UnityEngine;

// The shield's parting shot. When the blue-atom shield lets go (its 5.8 s timer
// runs out and the plates shatter -- ShipShield.Hide, the only way a shield
// ends: lifting the finger only pauses the game, it never drops the shield),
// a small shockwave shoves the board away from the ship:
//
//   radial   every hazard within Radius of the ship is pushed straight away
//            from it, hardest next to the hull, fading to nothing at the ring
//   column   every hazard in front of the ship in the ship's own column (a
//            strip the hull's width plus ColumnMargin, up to the top of the
//            view) is pushed up-screen by ColumnPush, however far away it is
//
// The wave itself does no damage: nothing is hit, killed or scored by it. The
// BODIES it shoves can crash into other enemies for a moment (ShoveCrash: crash
// damage by size class, kills are the pilot's, capped per release).
//
// HOW A PUSH COMPOSES. Every board mover is relative -- the scrollers
// Translate from where the body is, EnemyBrain adds the frame's change of its
// offset, ChaserEnemy steers from where it stands -- so a shove is simply a
// displacement fed into transform.position over PushSeconds (ease-out) by
// EnemyShove; the mover and the brain carry on from the new place. Per kind:
//
//   hazards with a brain (rocks)  displaced; the brain's base moves with it,
//                                 and that base is kept inside the lane so the
//                                 pattern still fits between the rails
//   pilots (EnemyBrain.IsPilot)   hold a station in the world: the push takes
//                                 one off its line (held on the push's curve
//                                 while it lasts), then the pilot flies itself
//                                 back to its station (EnemyBrain's external
//                                 displacement rule, ShoveReturnSpeed). Its
//                                 station (Base) is never moved, and its
//                                 script and any windup carry on. One still
//                                 waiting above the view, or gone, is not moved
//   rocks / aliens still weaving  moveEnimes sets x from its weave every
//                                 frame, so only the up/down part shows
//   chasers                       knocked back, then steer in again
//   rail mines                    slid along their rail (RailMineMount.Shove),
//                                 away from the ship, never off it and never
//                                 further than MineMaxSlide from their clamp
//   elites                        in play: a velocity kick (their own steering
//                                 eases them back); parked or lifting off: not
//                                 moved
//   elites take EliteResist (25%) of the kick. TryRelease (the shield's entry)
//   refuses a shield held under MinHeldSeconds, a second release within
//   Cooldown, and any release in the tutorial or a world entry.
//   bosses, hostile shots         not moved (the shield already eats the
//                                 shots it touches)
//
// Targets are capped where they would leave the lane or land on another body
// (bodies that did not overlap before are not made to), and everything runs on
// the world's clock: a frozen world (finger up with pauses left, a dead
// pilot, timeScale 0) moves nothing, and a push caught by a pause plays out
// on resume.
//
// Nothing is allocated or created at release: the ring and the column streak
// are built with the shield (Prewarm), the push list is a fixed array, and
// the hazards come from ClearTarget's live registry.
public static class ShieldShockwave
{
    // ------------------------------------------------------------- tunables
    public static float Radius = 2.6f;          // world units from the ship's centre (to a body's edge)
    public static float RadialPush = 1.0f;       // displacement right next to the hull (keep < Radius)
    public static float RadialFalloff = 1f;     // 1 = linear to zero at the ring; higher = tighter
    public static float ColumnMargin = .12f;    // added each side of the hull's width
    public static float ColumnPush = 1.25f;     // up-screen displacement for the whole column
    public static float PushSeconds = .28f;     // how long a shove takes (ease-out)
    public static float MineMaxSlide = .6f;     // a mine never slides further than this from its clamp
    public static float EliteKick = 3.2f;       // u/s added to an elite's velocity next to the hull (before EliteResist)
    public static float EliteColumnKick = 2.4f; // u/s up-screen for an elite in the column (before EliteResist)
    public static float EliteResist = .25f;     // elites take this share of the kick; their own steering eases them back
    public const float ShockwaveDamage = 0f;    // push only. Raise to make the wave hurt (no damage path is wired yet)

    // Fairness. TryRelease (what the shield calls) fires only for a shield
    // that was up this long, and not twice within Cooldown seconds.
    public const float MinHeldSeconds = .25f;
    public const float Cooldown = .5f;
    public static bool? EntryOverride;          // tests: stands in for WorldEntry.Active
    public static System.Func<float> Clock;     // tests; default Time.time
    static float lastRelease = -99f;
    public static int Suppressed { get; private set; }   // releases refused by a guard

    // The look and feel.
    public const float KickAmount = .012f;      // CameraKick, under TargetExplosion's medium .014
    public const string SoundKey = "Shockwave/shield_release";   // Audio/Resources/Audio/Shockwave/shield_release_0..2
    public const float SoundVolume = .7f;
    public static bool PushRocks = true;        // asteroids are shoved like any hazard
    public static float BodyMargin = .03f;      // gap kept between two shoved bodies

    // The look: a transparent circle whose only visible part is its
    // circumference -- 3 thin, turbulent, pixel-stepped strands in a pale shield
    // blue, grown from the hull to Radius over PushSeconds and faded out by
    // RingSeconds. No fill, no glow, no flash; the inside stays alpha 0.
    public static float RingSeconds = .36f;
    public static float StreakSeconds = .26f;
    public const float PixelsPerUnit = 26f;            // chunky art pixels of the fx canvases
    public const float RingLeadAlpha = .50f;           // the leading strand
    public const float RingMaxAlpha = .62f;            // the brightest sparkle pixel, never above .7
    public const float StreakAlpha = .20f;
    public static readonly Color32 FxColor = new Color32(150, 208, 236, 255);     // pale shield blue
    public static readonly Color32 FxSpark = new Color32(196, 228, 246, 255);     // a few lighter pixels

    // ------------------------------------------------------------- counters
    public static int Releases { get; private set; }
    public static int LastPushed { get; private set; }     // bodies given a displacement
    public static int LastKicked { get; private set; }     // elites given a kick
    public static float LastColumnHalf { get; private set; }

    const int Capacity = EnemyShove.Capacity;
    static readonly Transform[] who = new Transform[Capacity];
    static readonly RailMineMount[] mounts = new RailMineMount[Capacity];
    static readonly bool[] pilots = new bool[Capacity];
    static readonly SpawnFootprint[] prints = new SpawnFootprint[Capacity];
    static readonly Vector2[] from = new Vector2[Capacity];
    static readonly Vector2[] push = new Vector2[Capacity];
    static readonly Vector2[] half = new Vector2[Capacity];

    // Builds the ring and the streak now (with the shield, as the ship
    // spawns), so the release creates nothing.
    public static void Prewarm() { ShieldShockwaveFx.Ensure(); }

    // The guarded entry the shield uses when it lets go: not in the tutorial
    // or a world-entry sequence, only for a shield that was up MinHeldSeconds,
    // and not within Cooldown of the last one. Returns bodies moved, -1 if refused.
    public static int TryRelease(Vector2 ship, float hullHalfWidth, float heldSeconds, int sortingLayer = 0, int sortingOrder = 4)
    {
        float now = Clock != null ? Clock() : Time.time;
        if (heldSeconds < MinHeldSeconds || now - lastRelease < Cooldown ||
            startMenu.youAreInTutorial || (EntryOverride ?? WorldEntry.Active)) { Suppressed++; return -1; }
        lastRelease = now;
        int moved = Release(ship, hullHalfWidth, sortingLayer, sortingOrder);
        CameraKick.Kick(KickAmount);
        EnemyDeathAudio.PlayAuthored(SoundKey, SoundVolume);
        AchievementEvents.RaiseShieldReleased(moved);
        return moved;
    }

    public static void ResetGuards() { lastRelease = -99f; Suppressed = 0; }

    // The shield at `ship` (world) lets go. `hullHalfWidth` is half the
    // hull's visible width in world units. Returns how many bodies it moved.
    public static int Release(Vector2 ship, float hullHalfWidth, int sortingLayer = 0, int sortingOrder = 4)
    {
        Releases++;
        float columnHalf = Mathf.Max(.05f, hullHalfWidth) + ColumnMargin;
        LastColumnHalf = columnHalf;
        float top = CameraFit.ViewTop, bottom = CameraFit.ViewBottom;
        int n = 0, kicked = 0;

        var live = ClearTarget.Live;
        for (int i = 0; i < live.Count && n < Capacity; i++)
        {
            var t = live[i];
            if (t == null || !t.isActiveAndEnabled) continue;
            var go = t.gameObject;
            bool rock = go.CompareTag("Astr");
            if (!rock && !go.CompareTag("Enimey")) continue;
            if (rock && !PushRocks) continue;
            if (go.TryGetComponent(out BossTarget _) || go.TryGetComponent(out BossActor _)) continue;
            if (RunScore.IsHostileShot(go)) continue;

            EnemyBrain brain;
            if (!go.TryGetComponent(out brain) || !brain.enabled || brain.Behaviour == null) brain = null;
            bool pilot = brain != null && brain.IsPilot;
            // above the view with its column reserved, or already off the board: not in play
            if (pilot && brain.Stage != EnemyBrain.PilotStage.Entering &&
                brain.Stage != EnemyBrain.PilotStage.Engaging && brain.Stage != EnemyBrain.PilotStage.Exiting) continue;

            Vector2 p = t.transform.position;
            SpawnFootprint print;
            bool hasPrint = go.TryGetComponent(out print) && print.isActiveAndEnabled;
            Vector2 h = hasPrint ? print.half : new Vector2(t.Radius, t.Radius);

            Vector2 d = p - ship;
            float centre = d.magnitude;
            float edge = Mathf.Max(0f, centre - Mathf.Max(h.x, h.y));
            float k = edge < Radius ? Mathf.Pow(1f - edge / Radius, Mathf.Max(.01f, RadialFalloff)) : 0f;
            Vector2 away = centre > 1e-4f ? d / centre : Vector2.up;
            bool inColumn = p.y > ship.y && p.y - h.y <= top && Mathf.Abs(d.x) <= columnHalf + h.x;
            if (k <= 0f && !inColumn) continue;

            EliteShip elite;
            if (go.TryGetComponent(out elite))
            {
                if (!elite.InPlay) continue;   // parked on its site, or lifting off: not shoved
                Vector2 kick = away * (EliteKick * k * EliteResist);
                if (inColumn) kick = new Vector2(kick.x * .35f, Mathf.Max(kick.y, EliteColumnKick * EliteResist));
                elite.Velocity += kick;
                kicked++;
                continue;
            }

            // a rock's size is its mass: a small one flies further, a big one
            // less far (HazardSize.ShoveScale; 1 for everything else). The
            // column's clearing push is the same for every body.
            float mass = rock ? HazardSize.ShoveScale(EnemyIdentity.ScaleOf(go)) : 1f;
            Vector2 move = away * (RadialPush * k * mass);
            if (inColumn) move = new Vector2(0f, Mathf.Max(move.y, ColumnPush));

            RailMineMount mount;
            if (go.TryGetComponent(out mount) && mount.rail != null)
            {
                // along the rail only, away from the ship, within reach of its clamp
                float along = inColumn ? move.y : (d.y >= 0f ? 1f : -1f) * move.magnitude;
                along = Mathf.Clamp(mount.Shove + along, -MineMaxSlide, MineMaxSlide) - mount.Shove;
                move = new Vector2(0f, along);
            }
            else
            {
                mount = null;
                // sideways: the body -- and a brain's base, which its whole
                // pattern hangs from -- stays between the rails
                float lane = SpawnLane.LaneHalf - h.x;
                // (a pilot's Base is its station, which a shove never moves: its body is what is kept in)
                float offset = brain != null && !pilot ? p.x - brain.Base.x : 0f;
                float baseX = p.x - offset;
                if (Mathf.Abs(baseX) <= lane) move.x = Mathf.Clamp(baseX + move.x, -lane, lane) - baseX;
                else if (Mathf.Abs(baseX + move.x) > Mathf.Abs(baseX)) move.x = 0f;   // already outside: never further
                // down: not out through the bottom of the view
                if (move.y < 0f && p.y - h.y >= bottom) move.y = Mathf.Max(move.y, bottom + h.y - p.y);
            }
            if (move.sqrMagnitude < 1e-6f) continue;

            who[n] = t.transform;
            mounts[n] = mount;
            pilots[n] = pilot;
            prints[n] = hasPrint ? print : null;
            from[n] = p;
            push[n] = move;
            half[n] = h;
            n++;
        }

        ShoveCrash.BeginWave();
        KeepApart(n);

        int pushed = 0;
        for (int i = 0; i < n; i++)
        {
            if (push[i].sqrMagnitude >= 1e-6f && EnemyShove.Add(who[i], mounts[i], push[i], half[i].x, PushSeconds, pilots[i]))
            {
                pushed++;
                ShoveCrash.Carry(who[i]);   // a shoved body is a battering ram for a moment
            }
            who[i] = null; mounts[i] = null; prints[i] = null;
        }
        LastPushed = pushed;
        LastKicked = kicked;

        ShieldShockwaveFx.Play(ship, Radius, columnHalf, top, sortingLayer, sortingOrder);
        return pushed + kicked;
    }

    // Shortens pushes that would land a body on another one (at its own
    // target if it is being pushed too, where it is otherwise). Of two pushed
    // bodies that would meet, the one pushed further gives way -- it is the
    // one catching the other up. Two bodies that were apart stay apart;
    // halving converges in a few rounds, and whatever is left is dropped.
    static void KeepApart(int n)
    {
        var all = SpawnSpace.Live(SpawnLayer.Enemy);
        const int Rounds = 6;
        for (int round = 0; round <= Rounds; round++)
        {
            bool changed = false;
            for (int i = 0; i < n; i++)
            {
                if (prints[i] == null || mounts[i] != null || push[i].sqrMagnitude < 1e-6f) continue;
                Rect before = Body(from[i], half[i]);
                Rect after = Body(from[i] + push[i], half[i]);
                for (int j = 0; j < all.Count; j++)
                {
                    var other = all[j];
                    if (other == null || other == prints[i]) continue;
                    Vector2 at = other.transform.position;
                    Rect otherBefore = Body(at, other.half);
                    Rect otherAfter = otherBefore;
                    float otherPush = 0f;
                    for (int m = 0; m < n; m++)
                        if (prints[m] == other) { otherAfter = Body(at + push[m], other.half); otherPush = push[m].sqrMagnitude; break; }
                    if (!Touch(after, otherAfter) || Touch(before, otherBefore)) continue;
                    if (otherPush > push[i].sqrMagnitude) continue;   // its turn will shorten it
                    if (ShoveCrash.WouldBreak(prints[i].gameObject, other.gameObject)) continue;   // a crash is allowed to land (ShoveCrash)
                    push[i] *= .5f;
                    if (round == Rounds || push[i].sqrMagnitude < .0025f) push[i] = Vector2.zero;
                    changed = true;
                    break;
                }
            }
            if (!changed) return;
        }
    }

    static Rect Body(Vector2 c, Vector2 h) { return new Rect(c.x - h.x, c.y - h.y, h.x * 2f, h.y * 2f); }

    static bool Touch(Rect a, Rect b)
    {
        return a.xMin < b.xMax + BodyMargin && b.xMin < a.xMax + BodyMargin &&
               a.yMin < b.yMax + BodyMargin && b.yMin < a.yMax + BodyMargin;
    }
}

// Displacements in progress: each feeds its body the next slice of its push
// on every running frame (ease-out), on top of whatever moves the body.
// A fixed array; nothing allocates. ShieldShockwaveFx steps it in the game,
// tests call Step(dt).
public static class EnemyShove
{
    public const int Capacity = 64;

    struct Entry
    {
        public Transform body;
        public RailMineMount mount;
        public Vector2 total;
        public bool hold;        // a body that flies itself back (a pilot): kept on the push's curve meanwhile
        public Vector3 last;     // where the push last put it
        public float halfX, seconds, time, done;   // done: eased share already applied
    }

    static readonly Entry[] entries = new Entry[Capacity];
    static int count;

    public static int Active => count;

    // Pushes `body` by `total` (world) over `seconds`. A mount means "along
    // its rail" (total.y). `hold`: the body steers itself back to its own
    // line (a pilot), so while the push lasts each slice is added to where
    // the push last put it, not to wherever it has flown back to since -- it
    // gets the whole distance, then returns under its own power. A body
    // already being pushed takes the new push on top of what is left of the
    // old one.
    public static bool Add(Transform body, RailMineMount mount, Vector2 total, float halfX, float seconds, bool hold = false)
    {
        if (body == null) return false;
        for (int i = 0; i < count; i++)
        {
            if (entries[i].body != body) continue;
            var e = entries[i];
            e.total = e.total * (1f - e.done) + total;
            e.time = 0f; e.done = 0f; e.seconds = Mathf.Max(.01f, seconds);
            entries[i] = e;
            return true;
        }
        if (count >= Capacity) return false;
        entries[count++] = new Entry
        {
            body = body, mount = mount, total = total, hold = hold, last = body.position, halfX = halfX, seconds = Mathf.Max(.01f, seconds),
        };
        return true;
    }

    public static bool IsShoved(Transform body)
    {
        for (int i = 0; i < count; i++) if (entries[i].body == body) return true;
        return false;
    }

    // One running frame of world time.
    public static void Step(float dt)
    {
        if (dt <= 0f) return;
        for (int i = count - 1; i >= 0; i--)
        {
            var e = entries[i];
            if (e.body == null) { Remove(i); continue; }
            e.time += dt;
            float k = Mathf.Clamp01(e.time / e.seconds);
            float eased = 1f - (1f - k) * (1f - k) * (1f - k);
            Vector2 step = e.total * (eased - e.done);
            e.done = eased;
            if (e.mount != null) e.mount.Shove += step.y;
            else
            {
                Vector3 p = e.hold ? e.last : e.body.position;
                float x = p.x + step.x;
                // whatever else moved it meanwhile, the shove never carries it into a rail
                // (a body its own pattern already holds further out is never carried further still)
                float lane = Mathf.Max(SpawnLane.LaneHalf - e.halfX, Mathf.Abs(p.x));
                x = Mathf.Clamp(x, -lane, lane);
                e.last = new Vector3(x, p.y + step.y, p.z);
                e.body.position = e.last;
            }
            if (k >= 1f) Remove(i); else entries[i] = e;
        }
        ShoveCrash.Step(dt);
    }

    static void Remove(int i)
    {
        count--;
        entries[i] = entries[count];
        entries[count] = default;
    }

    public static void Clear()
    {
        for (int i = 0; i < count; i++) entries[i] = default;
        count = 0;
        ShoveCrash.Clear();
    }
}

// The shockwave's look, and the clock that drives the shoves.
//
// The look is drawn procedurally into two small point-filtered textures that
// are built once with the shield and rewritten in place on every running frame
// of the effect (preallocated Color32 buffers, SetPixels32: nothing allocates):
//
//   ring    a canvas covering the push radius. Three thin strands (a leading
//           one and two behind it) are plotted one pixel at a time around the
//           circle; their radius wobbles with precomputed periodic noise that
//           drifts around the ring (small waves and eddies), they break into
//           gaps, a few pixels are lighter, and short wisps peel off outward.
//           The disc inside is never touched: alpha 0.
//   streak  a canvas up the ship's column: one faint broken turbulent line on
//           each edge of the pushed strip, nothing between them.
//
// Both use the game's time (Tick(dt) from LateUpdate; frozen while paused).
[DefaultExecutionOrder(-30)]   // LateUpdate before EnemyBrain (-20) reads where its body is
public class ShieldShockwaveFx : MonoBehaviour
{
    static ShieldShockwaveFx instance;

    const int Samples = 720;            // angle steps round the ring
    const int Strands = 3;
    const int Wisps = 9;

    // ring canvas
    SpriteRenderer ring, streak;
    Texture2D ringTex, streakTex;
    Color32[] ringPx, streakPx;
    int ringSize, ringHalf, streakW, streakH;
    Sprite ringSprite, streakSprite;

    // precomputed noise (periodic over the ring): two drifting layers per strand, a gap rank per strand
    float[][] waveA, waveB, gapRank;
    int[] wispAt;
    float[] edgeNoise;                  // along the column, 2 edges x 512

    float ringAge = 99f, streakAge = 99f, ringRadius, streakHalf, streakTop, leadRadius;
    Vector2 origin;
    int ringMaxA, streakMaxA;

    public static bool Exists { get { return instance != null; } }
    public static ShieldShockwaveFx Instance { get { return instance; } }
    public static int Created { get; private set; }
    public bool RingShowing { get { return ring != null && ring.enabled; } }
    public bool StreakShowing { get { return streak != null && streak.enabled; } }
    // the leading strand's mean radius now, world units from the ship
    public float RingWorldRadius { get { return RingShowing ? leadRadius : 0f; } }
    public Bounds StreakBounds { get { return streak != null ? streak.bounds : default; } }
    public SpriteRenderer RingRenderer { get { return ring; } }
    public SpriteRenderer StreakRenderer { get { return streak; } }
    public Color32[] RingPixels { get { return ringPx; } }
    public Color32[] StreakPixels { get { return streakPx; } }
    public int RingCanvas { get { return ringSize; } }
    public int StreakCanvasWidth { get { return streakW; } }
    public int StreakCanvasHeight { get { return streakH; } }
    public int RingMaxAlpha { get { return ringMaxA; } }
    public int StreakMaxAlpha { get { return streakMaxA; } }
    public Vector2 Origin { get { return origin; } }

    public static ShieldShockwaveFx Ensure()
    {
        if (instance != null) return instance;
        var go = new GameObject("~ShieldShockwave");
        instance = go.AddComponent<ShieldShockwaveFx>();
        instance.Build();
        Created++;
        return instance;
    }

    void Build()
    {
        const float ppu = ShieldShockwave.PixelsPerUnit;
        ringHalf = Mathf.CeilToInt((ShieldShockwave.Radius + .45f) * ppu);
        ringSize = ringHalf * 2;
        ringPx = new Color32[ringSize * ringSize];
        ringTex = NewTex("~ShockwaveRing", ringSize, ringSize);
        ringSprite = Sprite.Create(ringTex, new Rect(0, 0, ringSize, ringSize), new Vector2(.5f, .5f), ppu, 0, SpriteMeshType.FullRect);
        ring = Layer("Ring", ringSprite);

        streakW = 2 * Mathf.CeilToInt(1.4f * ppu);
        streakH = Mathf.CeilToInt(14f * ppu);
        streakPx = new Color32[streakW * streakH];
        streakTex = NewTex("~ShockwaveStreak", streakW, streakH);
        streakSprite = Sprite.Create(streakTex, new Rect(0, 0, streakW, streakH), new Vector2(.5f, 0f), ppu, 0, SpriteMeshType.FullRect);
        streak = Layer("Streak", streakSprite);

        BuildNoise();
    }

    static Texture2D NewTex(string n, int w, int h)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.name = n;
        t.filterMode = FilterMode.Point;
        t.wrapMode = TextureWrapMode.Clamp;
        return t;
    }

    SpriteRenderer Layer(string layerName, Sprite sprite)
    {
        var go = new GameObject(layerName, typeof(SpriteRenderer));
        go.transform.SetParent(transform, false);
        var sr = go.GetComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.enabled = false;
        return sr;
    }

    // Smooth periodic noise (a sum of harmonics with random phases), unit-ish range.
    static float[] Periodic(System.Random rng, int n, int kMin, int kMax, float falloff)
    {
        var v = new float[n];
        float peak = 0f;
        for (int k = kMin; k <= kMax; k++)
        {
            float amp = 1f / Mathf.Pow(k, falloff), ph = (float)rng.NextDouble() * Mathf.PI * 2f;
            for (int i = 0; i < n; i++) v[i] += amp * Mathf.Sin(k * i * (Mathf.PI * 2f / n) + ph);
        }
        for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(v[i]));
        for (int i = 0; i < n; i++) v[i] /= Mathf.Max(1e-4f, peak);
        return v;
    }

    void BuildNoise()
    {
        var rng = new System.Random(20261009);
        waveA = new float[Strands][]; waveB = new float[Strands][]; gapRank = new float[Strands][];
        for (int s = 0; s < Strands; s++)
        {
            waveA[s] = Periodic(rng, Samples, 4, 46, .7f);     // eddies, wavelength ~10-100 px
            waveB[s] = Periodic(rng, Samples, 2, 20, .8f);     // slower swell
            var g = Periodic(rng, Samples, 3, 16, .6f);
            // turn the values into ranks 0..1, so a threshold is the fraction of ring left open
            var idx = new int[Samples];
            for (int i = 0; i < Samples; i++) idx[i] = i;
            System.Array.Sort(idx, (a, b) => g[a].CompareTo(g[b]));
            gapRank[s] = new float[Samples];
            for (int r = 0; r < Samples; r++) gapRank[s][idx[r]] = r / (float)(Samples - 1);
        }
        wispAt = new int[Wisps];
        for (int j = 0; j < Wisps; j++) wispAt[j] = (int)((j + (float)rng.NextDouble() * .8f) * Samples / Wisps) % Samples;
        edgeNoise = Periodic(rng, 512, 3, 60, .7f);
    }

    public static void Play(Vector2 at, float radius, float columnHalf, float viewTop, int sortingLayer, int sortingOrder)
    {
        var fx = Ensure();
        fx.origin = at;
        fx.ringRadius = radius;
        fx.streakHalf = Mathf.Min(columnHalf, 1.25f);
        fx.streakTop = Mathf.Min(Mathf.Max(viewTop, at.y + .5f), at.y + 13.5f);
        fx.ringAge = 0f;
        fx.streakAge = 0f;
        fx.ring.sortingLayerID = fx.streak.sortingLayerID = sortingLayer;
        fx.ring.sortingOrder = sortingOrder;          // under hazards (5) and shots, the ship and its shield
        fx.streak.sortingOrder = sortingOrder - 1;
        fx.Paint();
    }

    public static bool Running
    {
        get { return !buttonClicks.playerDied && (TouchInput.IsPressed || score.pauseCounter <= 0); }
    }

    void LateUpdate()
    {
        if (!Running) return;
        float dt = Time.deltaTime;
        if (dt <= 0f || Time.timeScale <= 0f) return;
        Tick(dt);
    }

    // One running frame: the shoves, then the picture.
    public void Tick(float dt)
    {
        if (dt <= 0f) return;
        EnemyShove.Step(dt);
        ringAge += dt;
        streakAge += dt;
        Paint();
    }

    // -------------------------------------------------------------- drawing

    static void Plot(Color32[] px, int w, int h, int x, int y, float a, Color32 c, ref int maxA)
    {
        if (x < 0 || y < 0 || x >= w || y >= h) return;
        int ai = Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
        int at = y * w + x;
        if (ai <= px[at].a) return;
        px[at] = new Color32(c.r, c.g, c.b, (byte)ai);
        if (ai > maxA) maxA = ai;
    }

    static int Wrap(int i) { i %= Samples; return i < 0 ? i + Samples : i; }

    void Paint()
    {
        float rk = ringAge / ShieldShockwave.RingSeconds;
        if (rk < 1f)
        {
            PaintRing(rk);
            ring.transform.position = new Vector3(origin.x, origin.y, 0f);
            ring.enabled = true;
        }
        else if (ring.enabled) ring.enabled = false;

        float sk = streakAge / ShieldShockwave.StreakSeconds;
        if (sk < 1f)
        {
            PaintStreak(sk);
            streak.transform.position = new Vector3(origin.x, origin.y, 0f);
            streak.enabled = true;
        }
        else if (streak.enabled) streak.enabled = false;
    }

    void PaintRing(float rk)
    {
        const float ppu = ShieldShockwave.PixelsPerUnit;
        System.Array.Clear(ringPx, 0, ringPx.Length);
        ringMaxA = 0;

        // the circle grows from the hull to the push radius over PushSeconds (ease-out, as the shove)
        float pk = Mathf.Clamp01(ringAge / Mathf.Max(.01f, ShieldShockwave.PushSeconds));
        float e = 1f - (1f - pk) * (1f - pk);
        leadRadius = Mathf.Lerp(.3f, ringRadius, e);
        float fade = 1f - rk * rk;                       // thins out as it goes
        float rPx = leadRadius * ppu;
        float maxR = ringHalf - 2f;
        int step = Mathf.FloorToInt(ringAge * 60f);       // pixel-stepped time: the eddies move in jumps
        int driftA = step * 3, driftB = -step * 2;
        float turb = 1.2f + 1.8f * Mathf.Min(1f, ringAge / .2f);      // eddies build up

        for (int s = 0; s < Strands; s++)
        {
            float back = s == 0 ? 0f : (s == 1 ? 3.4f : 6.6f);
            float alpha = (s == 0 ? ShieldShockwave.RingLeadAlpha : (s == 1 ? .30f : .20f)) * fade;
            float open = s == 0 ? .10f : (s == 1 ? .32f : .48f);
            float[] wa = waveA[s], wb = waveB[s], gr = gapRank[s];
            for (int i = 0; i < Samples; i++)
            {
                if (gr[Wrap(i + step * (s + 1))] < open) continue;                    // a gap
                float n = wa[Wrap(i + driftA)] * .65f + wb[Wrap(i + driftB)] * .5f;
                float r = Mathf.Min(maxR, rPx - back + n * turb * 1.5f);
                float ang = i * (Mathf.PI * 2f / Samples);
                int x = ringHalf + Mathf.RoundToInt(Mathf.Cos(ang) * r);
                int y = ringHalf + Mathf.RoundToInt(Mathf.Sin(ang) * r);
                float a = alpha;
                Color32 c = ShieldShockwave.FxColor;
                if (s == 0 && ((i * 73 + step) % 17) == 0) { a = ShieldShockwave.RingMaxAlpha * fade; c = ShieldShockwave.FxSpark; }
                Plot(ringPx, ringSize, ringSize, x, y, a, c, ref ringMaxA);
                if (s == 0 && wb[Wrap(i + driftB)] > .62f)         // the strand thickens where it swells
                {
                    float r2 = Mathf.Min(maxR, r + 1f);
                    Plot(ringPx, ringSize, ringSize, ringHalf + Mathf.RoundToInt(Mathf.Cos(ang) * r2), ringHalf + Mathf.RoundToInt(Mathf.Sin(ang) * r2), alpha * .55f, ShieldShockwave.FxColor, ref ringMaxA);
                }
            }
        }

        // wisps peeling off the leading strand, curling outward
        for (int j = 0; j < Wisps; j++)
        {
            int at = Wrap(wispAt[j] + driftA);
            int len = 5 + (j * 3) % 5;
            float r0 = rPx + waveA[0][at] * turb * 1.0f;
            for (int k = 1; k <= len; k++)
            {
                float ang = (at + k * .8f) * (Mathf.PI * 2f / Samples);
                float r = Mathf.Min(maxR, r0 + k * 1.1f);
                float a = .26f * (1f - k / (len + 1f)) * fade;
                Plot(ringPx, ringSize, ringSize, ringHalf + Mathf.RoundToInt(Mathf.Cos(ang) * r), ringHalf + Mathf.RoundToInt(Mathf.Sin(ang) * r), a, ShieldShockwave.FxColor, ref ringMaxA);
            }
        }
        ringTex.SetPixels32(ringPx);
        ringTex.Apply(false);
    }

    void PaintStreak(float sk)
    {
        const float ppu = ShieldShockwave.PixelsPerUnit;
        System.Array.Clear(streakPx, 0, streakPx.Length);
        streakMaxA = 0;
        float height = Mathf.Max(.05f, streakTop - origin.y);
        float head = Mathf.Clamp01(sk * 2.2f) * height;                     // the head runs up the column,
        float tail = Mathf.Clamp01(sk * 1.4f - .25f) * height;             // the tail follows it out
        float fade = 1f - sk;
        int step = Mathf.FloorToInt(streakAge * 60f);
        int cx = streakW / 2;
        int halfPx = Mathf.RoundToInt(streakHalf * ppu);
        int y0 = Mathf.Max(0, Mathf.FloorToInt(tail * ppu)), y1 = Mathf.Min(streakH - 1, Mathf.FloorToInt(head * ppu));
        for (int edge = 0; edge < 2; edge++)
        {
            int sign = edge == 0 ? -1 : 1;
            for (int y = y0; y <= y1; y++)
            {
                int ni = (y * 3 + step * 5 + edge * 211) & 511;
                if (edgeNoise[(ni * 7 + 100 * edge) & 511] < -.35f) continue;            // gaps
                int wob = Mathf.RoundToInt(edgeNoise[ni] * 1.6f);
                float a = StreakFalloff(y, y0, y1) * ShieldShockwave.StreakAlpha * fade;
                Plot(streakPx, streakW, streakH, cx + sign * (halfPx + wob), y, a, ShieldShockwave.FxColor, ref streakMaxA);
            }
        }
        streakTex.SetPixels32(streakPx);
        streakTex.Apply(false);
    }

    static float StreakFalloff(int y, int y0, int y1)
    {
        // the leading end is the strongest, the tail end thins away
        float t = y1 > y0 ? (y - y0) / (float)(y1 - y0) : 1f;
        return .35f + .65f * t;
    }

    void OnDestroy()
    {
        if (instance == this) { instance = null; EnemyShove.Clear(); }
        if (ringTex != null) Destroy(ringTex);
        if (streakTex != null) Destroy(streakTex);
        if (ringSprite != null) Destroy(ringSprite);
        if (streakSprite != null) Destroy(streakSprite);
    }
}
