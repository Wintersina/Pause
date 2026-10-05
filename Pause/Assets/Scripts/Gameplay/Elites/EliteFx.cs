using UnityEngine;

// The elites' placeholder effects, one fixed particle pool (built up front,
// to Capacity, then the oldest is reused), stepped by EliteSystem on the
// world's clock. Drawn from EliteFxArt in the house neon pixel look: dusty
// violet pad dust, heat shimmer, magenta / cyan sparks and blink rings, and
// the death debris -- the ship's own drawing cut into shards.
public sealed class EliteFx
{
    public const int Capacity = 96;

    struct P
    {
        public bool live;
        public Vector2 pos, vel;
        public float age, life, size0, size1, spin, angle, gravity, drag;
        public Color color;
        public Sprite[] frames;      // a flipbook instead of one drawing
        public float fps;
        public bool rail;            // debris: slams into a side rail and sticks there
    }

    readonly Transform root;
    readonly SpriteRenderer[] renderers = new SpriteRenderer[Capacity];
    readonly P[] parts = new P[Capacity];
    int built, next;

    public EliteFx(Transform parent)
    {
        root = new GameObject("~EliteFx").transform;
        root.SetParent(parent, false);
        // built up front, so emitting never allocates mid-flight
        while (built < Capacity) Build();
    }

    void Build()
    {
        var go = new GameObject("fx");
        go.transform.SetParent(root, false);
        renderers[built] = go.AddComponent<SpriteRenderer>();
        go.SetActive(false);
        built++;
    }

    public bool Alive => root != null;
    public int Spawned { get; private set; }

    public int LiveCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < built; i++) if (parts[i].live) n++;
            return n;
        }
    }

    int Slot()
    {
        for (int k = 0; k < Capacity; k++)
        {
            int i = (next + k) % Capacity;
            if (!parts[i].live) { next = (i + 1) % Capacity; return i; }
        }
        int oldest = next;
        next = (next + 1) % Capacity;
        return oldest;
    }

    // Returns the particle's slot (-1: nothing emitted).
    public int Emit(Sprite sprite, Vector2 pos, Vector2 vel, float life, float size0, float size1, Color color,
                    int order, float spin = 0f, float gravity = 0f, float drag = 0f, float angle = 0f)
    {
        if (root == null || sprite == null) return -1;
        int i = Slot();
        var sr = renderers[i];
        sr.sprite = sprite;
        sr.sortingOrder = order;
        sr.color = color;
        parts[i] = new P
        {
            live = true, pos = pos, vel = vel, life = Mathf.Max(.05f, life), size0 = size0, size1 = size1,
            spin = spin, angle = angle, gravity = gravity, drag = drag, color = color,
        };
        Place(i);
        if (!sr.gameObject.activeSelf) sr.gameObject.SetActive(true);
        Spawned++;
        return i;
    }

    void Place(int i)
    {
        var p = parts[i];
        var sr = renderers[i];
        float k = Mathf.Clamp01(p.age / p.life);
        float size = Mathf.Lerp(p.size0, p.size1, k);
        float span = sr.sprite != null ? Mathf.Max(sr.sprite.bounds.size.x, sr.sprite.bounds.size.y) : 1f;
        var t = sr.transform;
        t.position = new Vector3(p.pos.x, p.pos.y, 0f);
        t.rotation = Quaternion.Euler(0f, 0f, p.angle);
        t.localScale = Vector3.one * size / Mathf.Max(.0001f, span);
        Color c = p.color;
        // fades in hard steps, never a smooth ramp
        float a = k < .55f ? 1f : k < .8f ? .6f : .3f;
        c.a *= a;
        sr.color = c;
    }

    public void Step(float dt)
    {
        if (dt <= 0f) return;
        for (int i = 0; i < built; i++)
        {
            if (!parts[i].live) continue;
            var p = parts[i];
            p.age += dt;
            if (p.age >= p.life)
            {
                p.live = false;
                parts[i] = p;
                renderers[i].gameObject.SetActive(false);
                continue;
            }
            p.vel.y -= p.gravity * dt;
            if (p.drag > 0f) p.vel *= Mathf.Max(0f, 1f - p.drag * dt);
            p.pos += p.vel * dt;
            p.angle += p.spin * dt;
            if (p.rail)
            {
                float edge = EliteSystem.RailEdge - .05f;
                if (Mathf.Abs(p.pos.x) >= edge)
                {
                    // slammed into the rail: sticks, a spark burst at the wall
                    p.pos.x = Mathf.Sign(p.pos.x) * edge;
                    p.vel = Vector2.down * .3f;
                    p.spin = 0f;
                    p.gravity = 0f;
                    p.rail = false;
                    p.age = Mathf.Max(p.age, p.life * .5f);
                    Impacts++;
                    parts[i] = p;
                    Place(i);
                    Sparks(p.pos, p.color.a > 0f ? RailSpark : Color.white, 4);
                    continue;
                }
            }
            parts[i] = p;
            if (p.frames != null)
            {
                var f = p.frames[Mathf.Min(p.frames.Length - 1, Mathf.FloorToInt(p.age * p.fps))];
                if (renderers[i].sprite != f) renderers[i].sprite = f;
            }
            Place(i);
        }
    }

    // A drawn sequence played once (an elite's death strip).
    public void Flipbook(Sprite[] frames, float fps, Vector2 at, Vector2 vel, float size, float angle)
    {
        if (frames == null || frames.Length == 0) return;
        int i = Emit(frames[0], at, vel, frames.Length / Mathf.Max(1f, fps), size, size, Color.white, EliteShip.PlayOrder, 0f, 0f, 0f, angle);
        if (i < 0) return;
        parts[i].frames = frames;
        parts[i].fps = fps;
    }

    // ---- the effects -------------------------------------------------------

    static readonly Color Dust = new Color(.55f, .47f, .62f, .85f);
    static readonly Color DustLight = new Color(.78f, .66f, .74f, .7f);
    static readonly Color Heat = new Color(1f, .78f, .55f, .45f);
    static readonly Color RailSpark = new Color(1f, .45f, .9f, 1f);

    // Debris pieces that hit a rail (tests, previews).
    public int Impacts { get; private set; }

    // Lift-off: dust kicked off the pad, behind the ship (background order).
    public void LiftOffDust(Vector2 at, int order, float shipSize)
    {
        for (int i = 0; i < 9; i++)
        {
            float a = (i / 9f) * Mathf.PI + Random.Range(-.2f, .2f);
            Vector2 v = new Vector2(Mathf.Cos(a) * Random.Range(.5f, 1.1f), Mathf.Abs(Mathf.Sin(a)) * .25f - .1f) * shipSize;
            Emit(EliteFxArt.Puff, at + Vector2.down * shipSize * .15f, v, Random.Range(.7f, 1.2f),
                 shipSize * .35f, shipSize * Random.Range(.9f, 1.3f), i % 3 == 0 ? DustLight : Dust, order, Random.Range(-40f, 40f), 0f, 1.6f);
        }
    }

    // Lift-off: wavering heat rising off the engines.
    public void HeatShimmer(Vector2 at, int order, float shipSize)
    {
        Emit(EliteFxArt.Streak, at + new Vector2(Random.Range(-.3f, .3f) * shipSize, -.25f * shipSize),
             new Vector2(Random.Range(-.1f, .1f), .5f + shipSize * .4f), .45f, shipSize * .3f, shipSize * .55f, Heat, order);
    }

    public void Sparks(Vector2 at, Color color, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float a = Random.value * Mathf.PI * 2f;
            float s = Random.Range(1.2f, 3f);
            Emit(EliteFxArt.Spark, at, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s, Random.Range(.2f, .4f),
                 .1f, .04f, i % 2 == 0 ? color : Color.white, 40, 0f, 0f, 3f);
        }
    }

    public void BlinkBurst(Vector2 at, Color color, float size)
    {
        Emit(EliteFxArt.Ring, at, Vector2.zero, .3f, size * .3f, size * 1.1f, color, 39);
        Emit(EliteFxArt.Glow, at, Vector2.zero, .2f, size * .6f, size * .15f, color, 39);
        Sparks(at, color, 6);
    }

    // The default death: the ship's drawing breaks into shards that tumble
    // out along its momentum, with magenta / cyan sparks and a ring.
    public void Debris(EliteShip ship)
    {
        var def = ship.Def;
        var shards = EliteArt.Shards(def);
        Vector2 at = ship.Position;
        float rot = ship.ArtRotation;
        if (shards != null)
        {
            for (int i = 0; i < shards.Length; i++)
            {
                Vector2 home = EliteShip.Rotate(EliteArt.ShardHome(def, i), rot);
                // the wreck breaks up and crashes into the nearer rail
                float side = Mathf.Sign(at.x + home.x * .3f + .001f);
                Vector2 v = home.normalized * Random.Range(.8f, 1.8f) + ship.Velocity * .3f +
                            new Vector2(side * Random.Range(2.5f, 4.5f), Random.Range(.5f, 2f));
                float size = def.cellWorldSize * .7f / EliteArt.ShardCols * 1.05f;
                int k = Emit(shards[i], at + home, v, Random.Range(1.1f, 1.5f), size, size * .8f, Color.white,
                             EliteShip.PlayOrder, Random.Range(-360f, 360f), 2.2f, .2f, rot);
                if (k >= 0) parts[k].rail = true;
            }
        }
        Emit(EliteFxArt.Ring, at, Vector2.zero, .35f, def.cellWorldSize * .4f, def.cellWorldSize * 1.6f, def.ShotColor, 41);
        Sparks(at, def.ShotColor, 10);
        Sparks(at, def.ShotCore, 6);
    }
}

// What happens to the ship itself when its last heart goes. Pluggable: the
// death-crash fragment system (or anything else) sets Handler and takes
// over; by default it is a big blast plus EliteFx.Debris.
public static class EliteDeath
{
    public delegate void DeathHandler(EliteShip ship, EliteDamage cause);
    public static DeathHandler Handler;
    public static int Played { get; private set; }

    public static void Play(EliteShip ship, EliteDamage cause)
    {
        Played++;
        if (Handler != null) { Handler(ship, cause); return; }
        Default(ship, cause);
    }

    public static void Default(EliteShip ship, EliteDamage cause)
    {
        var death = EliteArt.ExtraFrames(ship.Def, EliteArt.Extra.Death);
        Vector2 at = ship.Position;
        if (death != null)
        {
            EliteSystem.Fx.Flipbook(death, 12f, at, ship.Velocity * .3f, ship.Def.cellWorldSize, ship.ArtRotation);
        }
        // sometimes it breaks into spinning pieces of its hull (EnemySplit), over a smaller blast
        bool split = EnemySplit.TrySplitElite(ship);
        TargetExplosion.Spawn(at, TargetExplosion.Kind.Metal, split ? TargetExplosion.Size.Medium : TargetExplosion.Size.Large,
                              cause == EliteDamage.Crash || cause == EliteDamage.Rail || cause == EliteDamage.FriendlyFire ? ShipId.None : ShipId.Equipped());
        collisionDetection.PlayExplosion();
        EliteSystem.Fx.Debris(ship);
    }
}

// The pilot's reward for any elite going down.
public static class EliteRewards
{
    public static int Paid { get; private set; }
    public static int LastPoints { get; private set; }
    public static float LastDust { get; private set; }

    public static void Pay(EliteShip ship)
    {
        Paid++;
        var def = ship.Def;
        LastPoints = RunScore.OnElite(ship.Position, def.Score);
        LastDust = def.Dust;
        score.AwardStarDust(def.Dust);
        Codex.Discover(ship.gameObject);
        SecretPowerController.OnKill();
    }
}
