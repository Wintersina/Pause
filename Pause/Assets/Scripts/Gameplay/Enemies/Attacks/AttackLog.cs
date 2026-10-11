using UnityEngine;

// THE THROWN TRUNK (plan section 8.2e; docs/world-attacks-design.md: Verdant's trunk toss, the `Roll` behaviour as a boss hazard).
//
// A boss cannot hold a lobbed EliteShot to the hazard contract (a shot has no AttackShape, no preview, no tell the boss waits for),
// so the trunk is an AttackHazard that rolls with the numbers of EliteShot's Roll (ShotMotions.RollSpeed, RollSeconds: down the board
// on a diagonal at 1.4 u/s, bouncing off a rail exactly once) and wears its Log body (ShotMotionArt / <w>_attack_log.png).
//
//   Tell   (>= .7 s, FR1) the trunk leaves the thrower's petal and arcs over the lane onto the marked spot: a dotted ring at the landing
//          spot and the dotted outline of its whole rolling track (down the diagonal, off the rail, on) are drawn from the first frame (FR2),
//          with a ghost trunk resting on the spot and pink-white dashes along the line it will roll. The spot, the heading and the track are
//          fixed ONCE, when the tell starts ("aim locked at the tell"), so a pilot reads the landing and the roll from the first frame.
//          The flight is harmless (a lob in the air). It lands exactly when the tell is up.
//   Live   the trunk rolls: a disc of HitHalf on the centre of the trunk (the drawn trunk is longer than it hits, the doc's 2x rule) moves
//          along the locked track at Speed u/s; it bounces off a rail once; it ends after RollSeconds or when it has left the bottom of the view.
//   After  it draws back (a short fade), harmless.
//
// USE:  AttackLog.Arm(LogSpec.Standard(world), muzzleWorldPos, landingWorldPos, dirX, tellSeconds, shooterGameObject)
// (BossExecutors' RollExecutor does it, one per trunk; .Follow() keeps the trunk in the thrower's petal until it is thrown).
[System.Serializable]
public struct LogSpec
{
    public float speed;         // u/s along the ground (ShotMotions.RollSpeed)
    public float rollSeconds;   // the roll's life (ShotMotions.RollSeconds for the elite log; shorter for a boss)
    public float throwHold;     // s the trunk stays in the thrower's petal at the start of the tell
    public float arcHeight;     // u the lob rises above the straight line
    public float hitHalf;       // radius of the hit disc
    public float length;        // drawn length of the trunk
    public int world;

    public static LogSpec Standard(int world) => new LogSpec
    {
        speed = ShotMotions.RollSpeed, rollSeconds = ShotMotions.RollSeconds, throwHold = .3f, arcHeight = 1.5f, hitHalf = .26f, length = 1.1f, world = world,
    };
}

public sealed class AttackLog : AttackHazard
{
    public const float MinSpeed = .6f, MaxSpeed = 2.2f;          // FR3: a slow, readable roll
    public const float MinRoll = 1.5f, MaxRoll = 7f;
    public const float RingRadius = .55f;
    public const float DiagonalAcross = .65f, DiagonalDown = .76f;   // EliteShot.StartRolling: x share .65, y share .76 of the speed
    public const float AfterSeconds = .24f;
    public const int MaxDashes = 12;
    public const int PoolSize = 3;
    public const int SortBody = 14, SortGhost = 11, SortDash = 11, SortGlow = 13;
    public const float FlipFps = 6f;

    static readonly System.Func<Transform, AttackLog> maker = Create;
    public static AttackPool<AttackLog> Pool => AttackPools.Get("logs", PoolSize, maker);

    static AttackLog Create(Transform root)
    {
        var go = new GameObject("AttackLog");
        go.transform.SetParent(root, false);
        var l = go.AddComponent<AttackLog>();
        l.body = Piece(go.transform, "Body", SortBody);
        l.glow = Piece(l.body.transform, "Outline", SortGlow);
        l.ghost = Piece(go.transform, "Ghost", SortGhost);
        l.dashes = new SpriteRenderer[MaxDashes];
        for (int i = 0; i < MaxDashes; i++) l.dashes[i] = Piece(go.transform, "Dash" + i, SortDash);
        l.Register();
        go.SetActive(false);
        return l;
    }

    static SpriteRenderer Piece(Transform parent, string name, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = order;
        sr.enabled = false;
        return sr;
    }

    // Takes a trunk from the pool and starts its tell: thrown from `muzzle` onto `landing` (world), rolling toward `dirX` (+1 / -1). Null: all busy.
    public static AttackLog Arm(LogSpec spec, Vector2 muzzle, Vector2 landing, float dirX, float tellSeconds, GameObject shooter)
    {
        var l = Pool.Take();
        if (l == null) return null;
        l.Setup(spec, muzzle, landing, dirX, tellSeconds, shooter);
        return l;
    }

    // ---- geometry (static: the shape, the drawing, the dodge bot's scenario and the tests share it) ----------------

    // The roll's velocity before any bounce: down the board on a diagonal toward `dirX`.
    public static Vector2 Velocity(float speed, float dirX) => new Vector2(Mathf.Sign(dirX == 0f ? 1f : dirX) * speed * DiagonalAcross, -speed * DiagonalDown);

    // The side a trunk landing at x rolls toward: the nearer rail (EliteShot.StartRolling), `parity` breaks a tie in the lane's middle.
    public static float DirFor(float landingX, int parity) => landingX > .25f ? 1f : landingX < -.25f ? -1f : (parity == 0 ? 1f : -1f);

    // Seconds from the landing until the centre meets the rail (0: already there; infinity: it never does).
    public static float BounceTime(Vector2 landing, Vector2 v, float xLimit)
    {
        if (Mathf.Abs(v.x) < 1e-5f) return float.PositiveInfinity;
        float target = Mathf.Sign(v.x) * xLimit;
        float tb = (target - landing.x) / v.x;
        return Mathf.Max(0f, tb);
    }

    // Where the centre is `tau` seconds after the landing (one bounce off the rail at +-xLimit).
    public static Vector2 PositionAt(Vector2 landing, Vector2 v, float xLimit, float tau)
    {
        float tb = BounceTime(landing, v, xLimit);
        float x;
        if (tau <= tb) x = landing.x + v.x * tau;
        else x = Mathf.Sign(v.x) * xLimit - v.x * (tau - tb);   // reflected
        return new Vector2(x, landing.y + v.y * tau);
    }

    // Seconds from the landing until the roll ends: its life, or the moment the bounced trunk would reach the far rail (a trunk bounces ONCE).
    public static float EndTime(Vector2 landing, Vector2 v, float xLimit, float rollSeconds)
    {
        float tb = BounceTime(landing, v, xLimit);
        if (float.IsPositiveInfinity(tb)) return rollSeconds;
        return Mathf.Min(rollSeconds, tb + 2f * xLimit / Mathf.Max(.01f, Mathf.Abs(v.x)));
    }

    // The velocity at `tau` (the x component flips at the bounce).
    public static Vector2 VelocityAt(Vector2 landing, Vector2 v, float xLimit, float tau) => tau <= BounceTime(landing, v, xLimit) ? v : new Vector2(-v.x, v.y);

    // ---- instance ----

    SpriteRenderer body, glow, ghost;
    SpriteRenderer[] dashes;
    LogSpec spec;
    Vector2 origin, landing, vel, followOffset;
    Vector2 hold;               // where the trunk waits in the thrower's petal (rides it until thrown)
    Transform follow;
    float xLimit, rollTotal, holdFor, fadeFor;
    readonly Sprite[] rimSprites = new Sprite[8];
    float bottom;
    bool artBody;
    Vector2 pos;                // the centre now
    int dashCount;
    readonly Sprite[] bodySprites = new Sprite[8], dashSprites = new Sprite[2];
    int bodyFrames;
    Color glowTint;

    public override float ThreatWeight => 1f;                  // FR7: a rolling trunk counts as one shot
    protected override string Label => "trunk";
    protected override Color PreviewTint => HostileShotPalette.Body(EnemyBehaviours.SpaceShot);
    public LogSpec Spec => spec;
    public Vector2 Landing => landing;
    public Vector2 Origin => origin;
    public Vector2 RollVelocity => vel;
    public float XLimit => xLimit;
    public float RollSeconds => rollTotal;   // the roll's effective life: its seconds, or until it would meet the far rail
    public float HitHalf => spec.hitHalf;
    public Vector2 Position => pos;
    public bool Flying => State == Phase.Tell && t >= holdFor;
    public SpriteRenderer BodyRenderer => body;
    public SpriteRenderer GhostRenderer => ghost;
    public SpriteRenderer DashRenderer(int i) => dashes[i];
    public int DashesShown { get; private set; }
    public bool Bounced { get; private set; }
    public bool Fading => State == Phase.After;

    void Setup(LogSpec s, Vector2 muzzle, Vector2 landAt, float dirX, float tellSeconds, GameObject by)
    {
        spec = s;
        spec.speed = Mathf.Clamp(s.speed, MinSpeed, MaxSpeed);
        spec.rollSeconds = Mathf.Clamp(s.rollSeconds, MinRoll, MaxRoll);
        spec.hitHalf = Mathf.Max(.08f, s.hitHalf);
        spec.length = Mathf.Max(.4f, s.length);
        origin = hold = muzzle;
        follow = null;
        followOffset = Vector2.zero;
        xLimit = Mathf.Max(.5f, BossRails.DrawnInnerEdge - spec.hitHalf);
        landing = new Vector2(Mathf.Clamp(landAt.x, -xLimit, xLimit), landAt.y);
        vel = Velocity(spec.speed, dirX);
        rollTotal = EndTime(landing, vel, xLimit, spec.rollSeconds);
        bottom = CameraFit.ViewBottom - 1f;
        fadeFor = 0f;
        Bounced = false;
        pos = muzzle;
        holdFor = Mathf.Clamp(spec.throwHold, 0f, Mathf.Max(0f, Mathf.Max(MinTellSeconds, tellSeconds) - .5f));
        // every sprite is looked up once per take (a cached look-up), never per frame
        artBody = AttackHazardArt.LogArt(s.world);
        bodyFrames = artBody ? 8 : 2;
        for (int i = 0; i < bodyFrames; i++) bodySprites[i] = AttackHazardArt.LogBody(s.world, i);
        bool bold = ShotOutline.UseBold;
        // the shot outline (visible over any backdrop), looked up once per take for the trunk at its drawn size; it scales with the body
        for (int i = 0; i < bodyFrames; i++)
        {
            var bs = bodySprites[i];
            float nat = bs == null ? 1f : (artBody ? bs.bounds.size.x : bs.bounds.size.y);
            rimSprites[i] = bs == null ? null : ShotOutline.For(bs, bs.bounds.size.y * spec.length / Mathf.Max(.01f, nat));
        }
        for (int i = 0; i < 2; i++) dashSprites[i] = AttackHazardArt.LashDash(s.world, i, bold);
        glowTint = bold ? ShotOutline.BoldTrace(HostileShotPalette.Trace(PreviewTint)) : HostileShotPalette.Trace(PreviewTint);
        BeginTell(tellSeconds, by, s.world);
    }

    // The trunk rides `t` (+ a local offset) while it waits in the petal; the landing and the track stay where they were locked.
    public void Follow(Transform tr, Vector2 localOffset)
    {
        follow = tr;
        followOffset = localOffset;
        hold = tr != null ? (Vector2)tr.position + localOffset : origin;
    }

    // ---- the footprint ----

    // The landing ring and the whole rolling track as dotted outlines: preview only (nothing is hit until it lands).
    void BuildTell()
    {
        shape.Clear();
        const int ringPts = 16;
        shape.BeginLoop();
        for (int i = 0; i < ringPts; i++)
        {
            float a = i * Mathf.PI * 2f / ringPts;
            shape.LoopPoint(landing + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * RingRadius);
        }
        shape.EndLoop();
        // the track: the strip the disc sweeps, one rectangle per leg (down to the rail, then back off it)
        float tb = BounceTime(landing, vel, xLimit);
        float end = rollTotal;
        float legEnd = Mathf.Min(tb, end);
        float hw = spec.hitHalf;
        AddLeg(landing, PositionAt(landing, vel, xLimit, legEnd), hw);
        if (tb < end) AddLeg(PositionAt(landing, vel, xLimit, tb), PositionAt(landing, vel, xLimit, end), hw);
    }

    void AddLeg(Vector2 a, Vector2 b, float half)
    {
        Vector2 d = b - a;
        float len = d.magnitude;
        if (len < .05f) return;
        Vector2 n = new Vector2(-d.y, d.x) / len * half;
        shape.BeginLoop();
        shape.LoopPoint(a - n); shape.LoopPoint(b - n); shape.LoopPoint(b + n); shape.LoopPoint(a + n);
        shape.EndLoop();
    }

    // The live footprint: a disc (an octagon just outside the circle) on the centre.
    void BuildDisc()
    {
        shape.Clear();
        float r = spec.hitHalf / Mathf.Cos(Mathf.PI / 8f);
        shape.BeginPoly(false);
        for (int i = 0; i < 8; i++)
        {
            float a = (i + .5f) * Mathf.PI / 4f;
            shape.Point(pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
        }
        shape.EndPoly();
    }

    protected override void OnArmed()
    {
        BuildTell();
        HideAll();
        DrawTell();
    }

    // ---- drawing ----

    void HideAll()
    {
        if (body != null) body.enabled = false;
        if (glow != null) glow.enabled = false;
        if (ghost != null) ghost.enabled = false;
        if (dashes != null) for (int i = 0; i < dashes.Length; i++) dashes[i].enabled = false;
        DashesShown = 0;
    }

    // Lays the trunk at `at`, its axis across `heading`, `scale` times its drawn length. The glow is the shot outline (visible over any backdrop).
    void PlaceBody(Vector2 at, float angleDeg, float scale, int frame, float alpha)
    {
        var spr = bodySprites[frame % bodyFrames];
        body.sprite = spr;
        body.enabled = spr != null;
        var c = Color.white;
        c.a = alpha;
        body.color = c;
        if (spr == null) return;
        float natural = artBody ? spr.bounds.size.x : spr.bounds.size.y;
        float k = spec.length / Mathf.Max(.01f, natural) * scale;
        body.transform.position = new Vector3(at.x, at.y, 0f);
        body.transform.rotation = Quaternion.Euler(0f, 0f, angleDeg + (artBody ? 0f : -90f));
        body.transform.localScale = Vector3.one * k;
        var rim = rimSprites[frame % bodyFrames];
        glow.sprite = rim;
        glow.enabled = rim != null;
        glow.transform.localPosition = Vector3.zero;
        glow.transform.localRotation = Quaternion.identity;
        glow.transform.localScale = Vector3.one;
        var g = glowTint;
        g.a = HostileGlow.PulseAlphaAt(t + age) * alpha;
        glow.color = g;
    }

    // The ghost trunk resting on the landing spot, across the line it will roll, blinking in 8 fps steps.
    void PlaceGhost(int blink)
    {
        var spr = bodySprites[0];
        ghost.sprite = spr;
        ghost.enabled = spr != null;
        if (spr == null) return;
        var c = Color.white;
        c.a = blink == 0 ? .5f : .26f;
        ghost.color = c;
        float natural = artBody ? spr.bounds.size.x : spr.bounds.size.y;
        float k = spec.length / Mathf.Max(.01f, natural);
        float axis = Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg + 90f;
        ghost.transform.position = new Vector3(landing.x, landing.y, 0f);
        ghost.transform.rotation = Quaternion.Euler(0f, 0f, axis + (artBody ? 0f : -90f));
        ghost.transform.localScale = Vector3.one * k;
    }

    // Pink-white dashes along the centre line of the track (the "rolling line" drawn by the landing spot).
    void PlaceDashes(int frame)
    {
        float tb = BounceTime(landing, vel, xLimit);
        float total = rollTotal;
        float pathLen = spec.speed * total;
        int n = Mathf.Clamp(Mathf.RoundToInt(pathLen / .6f), 4, MaxDashes);
        for (int i = 0; i < dashes.Length; i++)
        {
            var sr = dashes[i];
            if (i >= n) { sr.enabled = false; continue; }
            float tau = total * (i + 1f) / (n + 1f);
            Vector2 p = PositionAt(landing, vel, xLimit, tau);
            Vector2 v = VelocityAt(landing, vel, xLimit, tau);
            var spr = dashSprites[(frame + i) & 1];
            sr.sprite = spr;
            sr.enabled = spr != null;
            sr.transform.position = new Vector3(p.x, p.y, 0f);
            sr.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg + 90f);
            sr.transform.localScale = Vector3.one * 1.4f;
        }
        DashesShown = n;
    }

    // The tell: the trunk waits in the petal, then arcs onto the spot; the ghost, the dashes and the dotted track are there from the first frame.
    void DrawTell()
    {
        int blink = Mathf.FloorToInt(t * 8f) & 1;
        PlaceGhost(blink);
        PlaceDashes(blink);
        float flight = Mathf.Clamp01(tellTotal - holdFor > .01f ? (t - holdFor) / (tellTotal - holdFor) : 1f);
        if (t < holdFor)
        {
            // gathering in the petal: a small trunk that swells
            float g = holdFor > .01f ? t / holdFor : 1f;
            PlaceBody(hold, 90f, Mathf.Lerp(.45f, .75f, g), 0, 1f);
        }
        else
        {
            float lift = 4f * flight * (1f - flight);
            Vector2 p = Vector2.Lerp(hold, landing, flight) + new Vector2(0f, spec.arcHeight * lift);
            // tumbling end over end in 45 degree steps, bigger at the top of the arc (it is nearer the camera)
            float ang = Mathf.Floor(flight * 8f) * 45f * 2f;
            PlaceBody(p, ang, Mathf.Lerp(.75f, 1f, flight) + .3f * lift, Mathf.FloorToInt(t * FlipFps), 1f);
        }
    }

    // ---- the hazard's life ----

    protected override void OnTellStep(float dt)
    {
        if (follow != null && t < holdFor) hold = (Vector2)follow.position + followOffset;
        DrawTell();
    }

    protected override void OnIgnited()
    {
        for (int i = 0; i < dashes.Length; i++) dashes[i].enabled = false;
        DashesShown = 0;
        ghost.enabled = false;
        follow = null;
        pos = landing;
        BuildDisc();
        DrawRoll(0f);
    }

    void DrawRoll(float tau)
    {
        Vector2 v = VelocityAt(landing, vel, xLimit, tau);
        // the trunk lies across its heading (it rolls, it does not slide): a cylinder seen from above, two stepped frames of bark
        float axis = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg + 90f;
        PlaceBody(pos, axis, 1f, Mathf.FloorToInt(age * (artBody ? 12f : FlipFps)), 1f);
    }

    protected override bool OnLiveStep(float dt)
    {
        float tau = Mathf.Min(age, rollTotal);
        pos = PositionAt(landing, vel, xLimit, tau);
        if (!Bounced && age > BounceTime(landing, vel, xLimit)) Bounced = true;
        BuildDisc();
        DrawRoll(tau);
        return age < rollTotal && pos.y > bottom;
    }

    protected override bool OnAfterStep(float dt)
    {
        fadeFor += dt;
        var c = body.color;
        c.a = 1f - Mathf.Clamp01(fadeFor / AfterSeconds);
        body.color = c;
        var g = glow.color;
        g.a *= c.a;
        glow.color = g;
        return fadeFor < AfterSeconds;
    }

    protected override void OnEnded()
    {
        HideAll();
        follow = null;
    }

    protected override void ReturnToPool() { var p = AttackPools.Find<AttackLog>("logs"); if (p != null) p.Release(this); }
}
