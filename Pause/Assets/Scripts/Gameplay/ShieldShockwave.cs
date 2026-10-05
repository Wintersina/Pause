using System.Collections.Generic;
using UnityEngine;

// The shield's parting shot. When the blue-atom shield lets go (its timer runs
// out and the plates shatter -- ShipShield.Hide, the only way a shield ends),
// a small shockwave shoves the board away from the ship:
//
//   radial   every hazard within Radius of the ship is pushed straight away
//            from it, hardest next to the hull, fading to nothing at the ring
//   column   every hazard in front of the ship in the ship's own column (a
//            strip the hull's width plus ColumnMargin, up to the top of the
//            view) is pushed up-screen by ColumnPush, however far away it is
//
// It is a push, not damage: nothing is hit, killed or scored by it.
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
    public static float Radius = 1.7f;          // world units from the ship's centre (to a body's edge)
    public static float RadialPush = .85f;      // displacement right next to the hull (keep < Radius)
    public static float RadialFalloff = 1f;     // 1 = linear to zero at the ring; higher = tighter
    public static float ColumnMargin = .12f;    // added each side of the hull's width
    public static float ColumnPush = 1.25f;     // up-screen displacement for the whole column
    public static float PushSeconds = .28f;     // how long a shove takes (ease-out)
    public static float MineMaxSlide = .6f;     // a mine never slides further than this from its clamp
    public static float EliteKick = 3.2f;       // u/s added to an elite's velocity next to the hull
    public static float EliteColumnKick = 2.4f; // u/s up-screen for an elite in the column
    public static bool PushRocks = true;        // asteroids are shoved like any hazard
    public static float BodyMargin = .03f;      // gap kept between two shoved bodies

    // The look (shield cyan, never the player's red).
    public static float RingSeconds = .32f;
    public static float StreakSeconds = .26f;
    public static readonly Color RingColor = new Color32(0x6E, 0xF2, 0xEE, 230);
    public static readonly Color StreakColor = new Color32(0x6E, 0xF2, 0xEE, 150);

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
                Vector2 kick = away * (EliteKick * k);
                if (inColumn) kick = new Vector2(kick.x * .35f, Mathf.Max(kick.y, EliteColumnKick));
                elite.Velocity += kick;
                kicked++;
                continue;
            }

            Vector2 move = away * (RadialPush * k);
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

        KeepApart(n);

        int pushed = 0;
        for (int i = 0; i < n; i++)
        {
            if (push[i].sqrMagnitude >= 1e-6f && EnemyShove.Add(who[i], mounts[i], push[i], half[i].x, PushSeconds, pilots[i])) pushed++;
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
    }
}

// The shockwave's look, and the clock that drives the shoves: a cyan ring
// growing to the push radius and a quick streak up the ship's column. Two
// sprite renderers, built once per scene with the shield and reused.
[DefaultExecutionOrder(-30)]   // LateUpdate before EnemyBrain (-20) reads where its body is
public class ShieldShockwaveFx : MonoBehaviour
{
    static ShieldShockwaveFx instance;
    static Sprite ringSprite, streakSprite;

    SpriteRenderer ring, streak;
    float ringAge = 99f, streakAge = 99f, ringRadius, streakHalf, streakTop;
    Vector2 origin;

    public static bool Exists { get { return instance != null; } }
    public static ShieldShockwaveFx Instance { get { return instance; } }
    public static int Created { get; private set; }
    public bool RingShowing { get { return ring != null && ring.enabled; } }
    public bool StreakShowing { get { return streak != null && streak.enabled; } }
    public float RingWorldRadius { get { return ring != null ? ring.bounds.extents.x : 0f; } }
    public Bounds StreakBounds { get { return streak != null ? streak.bounds : default; } }
    public Color RingTint { get { return ring != null ? ring.color : Color.clear; } }

    public static ShieldShockwaveFx Ensure()
    {
        if (instance != null) return instance;
        var go = new GameObject("~ShieldShockwave");
        instance = go.AddComponent<ShieldShockwaveFx>();
        instance.Build();
        Created++;
        return instance;
    }

    static Sprite Load(ref Sprite cache, string file)
    {
        if (cache != null) return cache;
        var tex = Resources.Load<Texture2D>("Prefabs/Vfx/" + file);
        if (tex == null) tex = Texture2D.whiteTexture;
        cache = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
        cache.name = "~" + file;
        return cache;
    }

    void Build()
    {
        ring = Layer("Ring", Load(ref ringSprite, "vfx_circle_05"));
        streak = Layer("Streak", Load(ref streakSprite, "vfx_trace_01"));
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

    public static void Play(Vector2 at, float radius, float columnHalf, float viewTop, int sortingLayer, int sortingOrder)
    {
        var fx = Ensure();
        fx.origin = at;
        fx.ringRadius = radius;
        fx.streakHalf = columnHalf;
        fx.streakTop = Mathf.Max(viewTop, at.y + .5f);
        fx.ringAge = 0f;
        fx.streakAge = 0f;
        fx.ring.sortingLayerID = fx.streak.sortingLayerID = sortingLayer;
        fx.ring.sortingOrder = sortingOrder;
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

    void Paint()
    {
        float rk = ringAge / ShieldShockwave.RingSeconds;
        if (rk < 1f)
        {
            float e = 1f - (1f - rk) * (1f - rk);
            float size = Mathf.Lerp(.25f, 1f, e) * ringRadius * 2f / Mathf.Max(.01f, ring.sprite.bounds.size.x);
            ring.transform.position = new Vector3(origin.x, origin.y, 0f);
            ring.transform.localScale = new Vector3(size, size, 1f);
            var c = ShieldShockwave.RingColor; c.a *= 1f - rk * rk;
            ring.color = c;
            ring.enabled = true;
        }
        else if (ring.enabled) ring.enabled = false;

        float sk = streakAge / ShieldShockwave.StreakSeconds;
        if (sk < 1f)
        {
            // the head runs up the column, the tail follows it out
            float head = Mathf.Lerp(origin.y, streakTop, Mathf.Clamp01(sk * 2.2f));
            float tail = Mathf.Lerp(origin.y, streakTop, Mathf.Clamp01(sk * 1.4f - .25f));
            float length = Mathf.Max(.05f, head - tail);
            var b = streak.sprite.bounds.size;
            streak.transform.position = new Vector3(origin.x, (head + tail) * .5f, 0f);
            streak.transform.localScale = new Vector3(streakHalf * 2f / Mathf.Max(.01f, b.x), length / Mathf.Max(.01f, b.y), 1f);
            var c = ShieldShockwave.StreakColor; c.a *= 1f - sk;
            streak.color = c;
            streak.enabled = true;
        }
        else if (streak.enabled) streak.enabled = false;
    }

    void OnDestroy()
    {
        if (instance == this) { instance = null; EnemyShove.Clear(); }
    }
}
