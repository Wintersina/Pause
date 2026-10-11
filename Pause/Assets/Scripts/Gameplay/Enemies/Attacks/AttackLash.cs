using UnityEngine;

// THE LASH: a whip swept in an arc (plan phase 1e; docs/world-attacks-design.md: Verdant's vine whip for the
// Mantis / Warden / thorn-vine mine and the Bloom Queen, Tide's tentacle lash).
//
//   Tell   (>= .7 s, FR1) a bud pulses at the root, a ghost of the whip lies along the sweep's first line and the
//          dotted outline of the whole swept area -- the start line, the arc of the tip, the end line -- is drawn from
//          the first frame (AttackPreview, FR2). The sweep is aimed ONCE, when the tell starts ("aim locked at the tell").
//   Live   the whip sweeps its arc in SweepSeconds (.25 .. .45 s, FR3). It is a chain of links (the hit shape IS that
//          chain: one non-overlapping ribbon of trapezoids, the drawn links lie on it), the tip leading by TipLead of the
//          sweep so the chain bends back like a real lash and straightens as the sweep ends. A pink thorn (vine) or a
//          barbed sucker (tentacle) caps it.
//   After  the whip draws back, link by link, harmless.
//
// SAFE GAP (FR4). The lane is narrow, so a wide arc would fill it; Aim therefore starts the sweep at one side of the pilot's
// spot, Entry u beside him, and sweeps it across him toward the other: the floor on the far side of the start line stays open,
// at least MinCorridor + CorridorMargin wide where the rails allow it (Aim mirrors the sweep, and lengthens the
// reach to the pilot's range, to get that), and stepping toward it is always a way out.
// The fast whip is paid for with the long tell and the drawn arc, as the Warden's slug is.
//
// USE:  AttackLash.Arm(LashSpec.Standard(world), muzzleWorldPos, targetWorldPos, tellSeconds, shooterGameObject)
// (EnemyBrain does it for EnemyAttack.Lash and ignites it at its Release; .Follow() keeps the root on the shooter).
[System.Serializable]
public struct LashSpec
{
    public float length;        // u: the whip's reach (raised to the pilot's range when autoReach)
    public float arcDeg;        // degrees the tip sweeps
    public float sweepSeconds;  // live time, clamped to AttackLash.MinSweep .. MaxSweep
    public float hitHalf;       // half the whip's hit width (u)
    public float tipLead;       // 0 .. .6: how far the tip runs ahead of the root end of the chain (share of the sweep)
    public float entry;         // u: how far inside the swept lane the pilot stands when the sweep starts (stepping this far out is the dodge)
    public bool autoReach;      // lengthen the whip to reach the pilot's range
    public float ride;          // share of the board's scroll the whip keeps (0: a pilot's, in world space)
    public int world;           // 4 = a tentacle, otherwise a vine (AttackHazardArt.LookOf)

    public static LashSpec Standard(int world) => new LashSpec
    {
        length = 4.6f, arcDeg = 60f, sweepSeconds = .4f, hitHalf = .14f, tipLead = .3f, entry = 1.5f, autoReach = true, ride = 0f, world = world,
    };

    public float LiveSeconds => Mathf.Clamp(sweepSeconds, AttackLash.MinSweep, AttackLash.MaxSweep);
}

public sealed class AttackLash : AttackHazard
{
    public const float MinSweep = .25f, MaxSweep = .45f;   // FR3
    public const float MinArcDeg = 30f, MaxArcDeg = 120f;
    public const float MinLength = 2f, MaxLength = 8.4f;
    public const float ReachBeyond = 1.6f;      // autoReach: the whip reaches this far past the pilot's range
    public const float LinkTarget = .625f;      // u a link has when the art is unscaled
    public const int MinLinks = 4, MaxLinks = 10, MaxDashes = 12;
    public const float RetractSeconds = .24f;
    public const int PoolSize = 3;
    public const int SortLinks = 12, SortTip = 13, SortRoot = 14, SortDash = 11;
    public const float FlickerFps = 10f;
    public const float MinCorridor = 1.4f, CorridorMargin = .25f;
    // the drawn chain is wider than it hits (the doc's 2x rule): the hit is HitHalf, the link tile .375 u
    public const float TipDraw = .75f;

    // ---- the pool ----
    static readonly System.Func<Transform, AttackLash> maker = Create;
    public static AttackPool<AttackLash> Pool => AttackPools.Get("lashes", PoolSize, maker);

    static AttackLash Create(Transform root)
    {
        var go = new GameObject("AttackLash");
        go.transform.SetParent(root, false);
        var l = go.AddComponent<AttackLash>();
        l.links = new SpriteRenderer[MaxLinks];
        for (int i = 0; i < MaxLinks; i++) l.links[i] = Piece(go.transform, "Link" + i, SortLinks);
        l.tip = Piece(go.transform, "Tip", SortTip);
        l.rootBud = Piece(go.transform, "Root", SortRoot);
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

    // Takes a lash from the pool and starts its tell: muzzle and target in world space. Null: all busy.
    public static AttackLash Arm(LashSpec spec, Vector2 muzzle, Vector2 target, float tellSeconds, GameObject shooter)
    {
        var l = Pool.Take();
        if (l == null) return null;
        l.Setup(spec, muzzle, target, tellSeconds, shooter);
        return l;
    }

    // ---- geometry (static: the shape, the drawing, the dodge bot's scenario and the tests share it) ----------------

    public static float ArcRad(in LashSpec s) => Mathf.Clamp(s.arcDeg, MinArcDeg, MaxArcDeg) * Mathf.Deg2Rad;
    public static float Lead(in LashSpec s) => Mathf.Clamp(s.tipLead, 0f, .6f);
    public static int LinksFor(float length) => Mathf.Clamp(Mathf.RoundToInt(length / LinkTarget), MinLinks, MaxLinks);

    // Where the sweep starts, which way it turns and how long the whip is, for a muzzle and a target (the pilot).
    // The start line stands at the pilot's row `entry` u beside him -- or closer, when the lane is too narrow to leave
    // MinCorridor + CorridorMargin of floor beyond it -- and the sweep turns across him toward the far side; of the two
    // mirror images the one that keeps him nearest `entry` inside the sweep wins. `room` is the open floor beyond the start line.
    public static void Aim(in LashSpec spec, Vector2 root, Vector2 target, float railEdge, out float startRad, out int dir, out float length, out float room)
    {
        Vector2 to = target - root;
        float range = Mathf.Max(1.8f, to.magnitude);
        length = spec.autoReach ? Mathf.Clamp(range + ReachBeyond, Mathf.Clamp(spec.length, MinLength, MaxLength), MaxLength) : Mathf.Clamp(spec.length, MinLength, MaxLength);
        float entry = Mathf.Max(.6f, spec.entry);
        float down = to.y < 0f ? 1f : -1f;   // a shooter above the pilot: turning counter-clockwise carries the tip toward +x
        float want = MinCorridor + CorridorMargin;
        float bestScore = float.NegativeInfinity;
        startRad = Mathf.Atan2(to.y, to.x);
        dir = 1;
        room = 0f;
        for (int s = 0; s < 2; s++)
        {
            float d = s == 0 ? 1f : -1f;
            float edgeX = target.x - d * entry;
            if (railEdge > 0f)
            {
                // beyond the line (toward -d) the floor to the rail must be at least `want`
                float limit = d > 0f ? want - railEdge : railEdge - want;     // d=+1: edgeX >= limit; d=-1: edgeX <= limit
                edgeX = d > 0f ? Mathf.Max(edgeX, limit) : Mathf.Min(edgeX, limit);
            }
            float inside = (target.x - edgeX) * d;
            // the pilot should stand inside the sweep (nearest `entry`, never under .5 u in); a tie is a coin
            float score = inside < .5f ? -10f - (.5f - inside) : -Mathf.Abs(inside - entry);
            if (score > bestScore + .01f || (Mathf.Abs(score - bestScore) <= .01f && Random.value < .5f))
            {
                bestScore = score;
                room = railEdge > 0f ? (d > 0f ? edgeX + railEdge : railEdge - edgeX) : 99f;
                dir = (int)(d * down);
                startRad = Mathf.Atan2(target.y - root.y, edgeX - root.x);
            }
        }
    }

    // The joints of the chain `tau` seconds into the sweep: joints[0] = root, joints[links] = the tip.
    // The tip runs ahead by Lead of the sweep; the whole chain has reached the end line when tau == sweep.
    public static void Chain(in LashSpec spec, int links, float length, Vector2 root, float startRad, int dir, float tau, Vector2[] joints)
    {
        float arc = ArcRad(in spec), lead = Lead(in spec);
        float k0 = Mathf.Clamp01(tau / spec.LiveSeconds) * (1f + lead);
        joints[0] = root;
        for (int i = 1; i <= links; i++)
        {
            float frac = i / (float)links;
            float k = Mathf.Clamp01(k0 - lead * (1f - frac));
            float ang = startRad + dir * arc * k;
            joints[i] = root + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (length * frac);
        }
    }

    // The tip's angle at `tau` (the chain's outermost joint).
    public static float TipAngle(in LashSpec spec, float startRad, int dir, float tau)
    {
        float k = Mathf.Clamp01(Mathf.Clamp01(tau / spec.LiveSeconds) * (1f + Lead(in spec)));
        return startRad + dir * ArcRad(in spec) * k;
    }

    // ---- instance ----

    SpriteRenderer[] links, dashes;
    SpriteRenderer tip, rootBud;
    LashSpec spec;
    Vector2 root, target;
    float startRad, length, linkLen, room, fadeFor;
    int dir, count;
    bool bold;
    Vector2[] joints = new Vector2[MaxLinks + 1];
    readonly Vector2[] left = new Vector2[MaxLinks + 1], right = new Vector2[MaxLinks + 1];
    readonly Sprite[] linkSprites = new Sprite[2], tipSprites = new Sprite[2], rootSprites = new Sprite[2], dashSprites = new Sprite[2];   // resolved once per take
    Transform follow;
    Vector2 followOffset;

    public override float ThreatWeight => 1.5f;               // FR7: a lash counts as 1.5 shots
    protected override string Label => spec.world == 4 ? "tentacle lash" : "vine lash";
    protected override Color PreviewTint => HostileShotPalette.Body(EnemyBehaviours.SpaceShot);
    public LashSpec Spec => spec;
    public Vector2 Root => root;
    public float StartRad => startRad;
    public int Dir => dir;
    public float Length => length;
    public int Links => count;
    public float Room => room;
    public float SweepSeconds => spec.LiveSeconds;
    public float HitHalf => spec.hitHalf;
    public Vector2 Joint(int i) => joints[i];
    public SpriteRenderer LinkRenderer(int i) => links[i];
    public SpriteRenderer TipRenderer => tip;
    public SpriteRenderer RootRenderer => rootBud;
    public SpriteRenderer DashRenderer(int i) => dashes[i];
    public int DashesShown { get; private set; }
    public int LinksShown { get; private set; }
    public bool Retracting => State == Phase.After;

    void Setup(LashSpec s, Vector2 muzzle, Vector2 aimAt, float tellSeconds, GameObject by)
    {
        spec = s;
        spec.hitHalf = Mathf.Max(.05f, s.hitHalf);
        spec.sweepSeconds = Mathf.Clamp(s.sweepSeconds, MinSweep, MaxSweep);
        spec.arcDeg = Mathf.Clamp(s.arcDeg, MinArcDeg, MaxArcDeg);
        root = muzzle;
        target = aimAt;
        Aim(in spec, root, target, BossRails.DrawnInnerEdge, out startRad, out dir, out length, out room);
        count = LinksFor(length);
        linkLen = length / count;
        follow = null;
        fadeFor = 0f;
        // every sprite is looked up once per take (a cached int-keyed look-up), never per frame
        bold = ShotOutline.UseBold;
        for (int i = 0; i < 2; i++)
        {
            linkSprites[i] = AttackHazardArt.LashLink(s.world, i, bold);
            tipSprites[i] = AttackHazardArt.LashTip(s.world, i, bold);
            rootSprites[i] = AttackHazardArt.LashRoot(s.world, i, bold);
            dashSprites[i] = AttackHazardArt.LashDash(s.world, i, bold);
        }
        BeginTell(tellSeconds, by, s.world);
    }

    // The root rides `t` (+ a local offset) while the whip telegraphs and sweeps; the aim stays where it was locked.
    public void Follow(Transform t, Vector2 localOffset)
    {
        follow = t;
        followOffset = localOffset;
    }

    // ---- the footprint ----

    // The whole swept area as one dotted outline: out along the start line, round the tip's arc, back along the end line.
    void BuildOutline()
    {
        float arc = ArcRad(in spec);
        float hw = spec.hitHalf;
        float endRad = startRad + dir * arc;
        Vector2 us = new Vector2(Mathf.Cos(startRad), Mathf.Sin(startRad)), ue = new Vector2(Mathf.Cos(endRad), Mathf.Sin(endRad));
        // the sides facing out of the sweep are the whip's own width further
        Vector2 os = new Vector2(us.y, -us.x) * (dir > 0 ? 1f : -1f) * hw;     // (right of the start line when turning counter-clockwise)
        Vector2 oe = new Vector2(-ue.y, ue.x) * (dir > 0 ? 1f : -1f) * hw;
        shape.BeginLoop();
        shape.LoopPoint(root + os);
        shape.LoopPoint(root + us * length + os);
        const int steps = 8;
        for (int i = 1; i < steps; i++)
        {
            float a = startRad + dir * arc * (i / (float)steps);
            shape.LoopPoint(root + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (length + hw));
        }
        shape.LoopPoint(root + ue * length + oe);
        shape.LoopPoint(root + oe);
        shape.EndLoop();
    }

    // The chain at `tau` as a ribbon of trapezoids sharing their joint edges: no two polygons overlap (a collider fills even-odd).
    void BuildRibbon(float tau)
    {
        Chain(in spec, count, length, root, startRad, dir, tau, joints);
        float hw = spec.hitHalf;
        for (int i = 0; i <= count; i++)
        {
            Vector2 d = joints[Mathf.Min(i + 1, count)] - joints[Mathf.Max(i - 1, 0)];
            float m = d.magnitude;
            Vector2 n = m > 1e-5f ? new Vector2(-d.y, d.x) / m * hw : new Vector2(0f, hw);
            left[i] = joints[i] + n;
            right[i] = joints[i] - n;
        }
        for (int i = 0; i < count; i++)
        {
            shape.BeginPoly(false);
            shape.Point(right[i]); shape.Point(right[i + 1]); shape.Point(left[i + 1]); shape.Point(left[i]);
            shape.EndPoly();
        }
    }

    protected override void OnArmed()
    {
        BuildRibbon(0f);        // (the first pose: hit geometry only, nothing burns until it is live)
        BuildOutline();
        HideAll();
        DrawTell();
    }

    // ---- drawing ----

    void HideAll()
    {
        if (links != null) for (int i = 0; i < links.Length; i++) links[i].enabled = false;
        if (dashes != null) for (int i = 0; i < dashes.Length; i++) dashes[i].enabled = false;
        if (tip != null) tip.enabled = false;
        if (rootBud != null) rootBud.enabled = false;
        LinksShown = DashesShown = 0;
    }

    // Lays the link sprites on the current joints. `upTo`: how many links are drawn (the retract hides the tip end first); alpha for the ghost.
    void DrawChain(int frame, int upTo, float alpha)
    {
        var tint = Color.white;
        tint.a = alpha;
        float linkFill = AttackHazardArt.LinkFill(spec.world);
        for (int i = 0; i < links.Length; i++)
        {
            var sr = links[i];
            if (i >= upTo || i >= count) { sr.enabled = false; continue; }
            Vector2 a = joints[i], b = joints[i + 1];
            Vector2 d = b - a;
            float len = d.magnitude;
            var spr = linkSprites[(frame + i) & 1];
            sr.sprite = spr;
            sr.enabled = spr != null && len > 1e-4f;
            sr.color = tint;
            var tr = sr.transform;
            Vector2 c = (a + b) * .5f;
            tr.position = new Vector3(c.x, c.y, 0f);
            tr.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg + 90f);
            tr.localScale = new Vector3(1f, spr != null ? Mathf.Max(.05f, len / Mathf.Max(.01f, spr.bounds.size.y * linkFill)) : 1f, 1f);
        }
        LinksShown = Mathf.Min(upTo, count);
        // the tip caps the chain (drawn only while the whole chain is)
        Vector2 e = joints[count] - joints[Mathf.Max(0, count - 1)];
        float em = e.magnitude;
        var ts = tipSprites[frame & 1];
        tip.sprite = ts;
        tip.enabled = ts != null && upTo >= count && em > 1e-4f;
        tip.color = tint;
        if (tip.enabled)
        {
            Vector2 u = e / em;
            float scale = TipDraw / Mathf.Max(.01f, ts.bounds.size.y);
            Vector2 c = joints[count] + u * (ts.bounds.size.y * scale * .5f - .06f);
            tip.transform.position = new Vector3(c.x, c.y, 0f);
            tip.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(u.y, u.x) * Mathf.Rad2Deg + 90f);
            tip.transform.localScale = Vector3.one * scale;
        }
        var rs = rootSprites[frame & 1];
        rootBud.sprite = rs;
        rootBud.enabled = rs != null;
        rootBud.color = Color.white;
        rootBud.transform.position = new Vector3(root.x, root.y, 0f);
        rootBud.transform.rotation = Quaternion.identity;
        rootBud.transform.localScale = Vector3.one * (rs != null ? .7f / Mathf.Max(.01f, rs.bounds.size.x) : 1f);
    }

    // The dotted arc the tip will draw, beside the preview's own dots: art cell `dash`, or the pink-white diamond.
    void DrawDashes(int frame)
    {
        float arc = ArcRad(in spec);
        int n = Mathf.Clamp(Mathf.RoundToInt(arc * length / .55f), 5, MaxDashes);
        for (int i = 0; i < dashes.Length; i++)
        {
            var sr = dashes[i];
            if (i >= n) { sr.enabled = false; continue; }
            float a = startRad + dir * arc * ((i + .5f) / n);
            Vector2 p = root + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * length;
            var spr = dashSprites[(frame + i) & 1];
            sr.sprite = spr;
            sr.enabled = spr != null;
            sr.transform.position = new Vector3(p.x, p.y, 0f);
            sr.transform.rotation = Quaternion.Euler(0f, 0f, (a + Mathf.PI * .5f) * Mathf.Rad2Deg);
            sr.transform.localScale = Vector3.one * 1.4f;
        }
        DashesShown = n;
    }

    // The tell: the whip lies along the start line as a ghost (blinking in 8 fps steps), a bud at the root, dashes on the arc.
    void DrawTell()
    {
        int frame = Mathf.FloorToInt(t * 8f) & 1;
        Chain(in spec, count, length, root, startRad, dir, 0f, joints);
        DrawChain(frame, count, frame == 0 ? .55f : .3f);
        DrawDashes(frame);
        rootBud.transform.localScale = Vector3.one * (Mathf.Lerp(.6f, 1.1f, Mathf.Clamp01(tellTotal > 0f ? t / tellTotal : 1f)) / Mathf.Max(.01f, rootSprites[0] != null ? rootSprites[0].bounds.size.x : 1f));
    }

    void MoveRoot(Vector2 d)
    {
        if (d.sqrMagnitude < 1e-10f) return;
        root += d;
        shape.Offset(d);
        var pv = Preview;
        if (pv != null) pv.Follow(d);
    }

    // ---- the hazard's life ----

    protected override void OnTellStep(float dt)
    {
        if (follow != null) MoveRoot((Vector2)follow.position + followOffset - root);
        else if (spec.ride > 0f) MoveRoot(new Vector2(0f, -EliteSystem.Scroll * spec.ride * dt));
        DrawTell();
    }

    protected override void OnIgnited()
    {
        for (int i = 0; i < dashes.Length; i++) dashes[i].enabled = false;
        DashesShown = 0;
        BuildLive(0f);
    }

    void BuildLive(float tau)
    {
        shape.Clear();
        BuildRibbon(tau);
        DrawChain(HostileShotPalette.FlickerHot(age) ? 1 : 0, count, 1f);
    }

    protected override bool OnLiveStep(float dt)
    {
        Vector2 d = Vector2.zero;
        if (follow != null) d = (Vector2)follow.position + followOffset - root;
        else if (spec.ride > 0f) d = new Vector2(0f, -EliteSystem.Scroll * spec.ride * dt);
        root += d;
        float tau = Mathf.Min(age, spec.LiveSeconds);
        BuildLive(tau);
        return age < spec.LiveSeconds;
    }

    protected override bool OnAfterStep(float dt)
    {
        fadeFor += dt;
        // the whip draws back, tip end first, in stepped frames
        int upTo = Mathf.CeilToInt(count * (1f - Mathf.Clamp01(fadeFor / RetractSeconds)));
        if (follow != null) { Vector2 d = (Vector2)follow.position + followOffset - root; root += d; }
        Chain(in spec, count, length, root, startRad, dir, spec.LiveSeconds, joints);
        DrawChain(Mathf.FloorToInt(fadeFor * 20f) & 1, upTo, 1f);
        return fadeFor < RetractSeconds;
    }

    protected override void OnEnded()
    {
        HideAll();
        follow = null;
    }

    protected override void ReturnToPool() { var p = AttackPools.Find<AttackLash>("lashes"); if (p != null) p.Release(this); }
}
