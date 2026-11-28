using UnityEngine;

// A boss's hearts (thirteen on the starting world, increasing each world): its health, spinning round it.
//
// The shared HeartOrbit engine (the player's lives, the elites' hearts) on
// a boss's terms: the player's own 3D spin (the Halo style: one tilted ring
// whose plane nods, rocks and precesses round the boss) with the hearts evenly
// spaced and turning together once every HeartRevolutionSeconds. A heart
// swings round the back, behind the boss (under it, smaller and dimmer), and
// comes round the front over it and its shots (HeartSortingOrder), under
// the HUD. The ring is sized from the body's drawn silhouette (Body, measured
// from each atlas's idle drawings): an ellipse just clear of it (HeartClearance
// past it, plus half a heart), so the ring's wide ends pass outside the art.
// Where the tilt swings a back heart across the art it is drawn above the body,
// dimmed, rather than vanishing under it (HidesBehindHull). Where the ring would
// cross a rail or reach the HUD band it flattens against that side instead
// (ClampRect), so a heart never goes under either on any screen. The
// player's own heart (same drawing and colour as ShipLivesIndicator's), with
// HeartOutline's trace.
//
// Hearts are drawings only -- no collider, nothing to hit or block. They pop
// in when the fight starts; each one lost (BossEncounter: every HeartWeight
// of damage) darts to the hit and crumbles like an elite's, and the rest
// close up to even spacing again. They retreat with the boss at once and
// go with its body when it dies.
//
// Stepped by BossActor (Step), never by its own Update, on world time: the
// ring holds still while the world is frozen. Pooled: nothing is built after
// Bind, nothing allocates a frame.
public class BossHearts : HeartOrbit
{
    public const string SpritePath = EliteHearts.SpritePath;

    BossActor actor;
    int left, quartersLeft, layer, top;
    bool impactPending;
    Vector3 impactAt;

    // The ellipse round the body's drawn silhouette (every pixel of alpha
    // >= 0.5 in every idle drawing lies inside it), relative to the boss's
    // centre, in world units at BossWorldSize 3.3: its centre and
    // half-axes, as a rect. Measured from the atlases (the silhouette's box,
    // grown to the ellipse through its furthest pixel); BossHeartsTest
    // re-measures them from the source files. Space / Frost / Verdant / Ember / Tide / Desert: the v3 redraws
    // (Art/Bosses~/redraw-v3/build.py prints them).
    public static Rect Body(BossDef boss)
    {
        switch (boss != null ? boss.artKey : null)
        {
            case "Space": return Around(0f, .11f, 1.84f, 1.90f);
            case "Frost": return Around(0f, .04f, 1.61f, 1.63f);
            case "Verdant": return Around(0f, .07f, 1.69f, 1.78f);
            case "Ember": return Around(0f, .01f, 1.53f, 1.50f);
            case "Tide": return Around(0f, .02f, 1.71f, 1.72f);
            case "Storm": return Around(0f, .01f, 1.61f, 1.57f);
            case "Desert": return Around(0f, .06f, 1.71f, 1.76f);
            default:
                float h = BossConfig.BossWorldSize * .5f * 1.42f;
                return Around(0f, 0f, h, h);
        }
    }

    static Rect Around(float cx, float cy, float ax, float ay)
    {
        return Rect.MinMaxRect(cx - ax, cy - ay, cx + ax, cy + ay);
    }

    // Half the heart as drawn (the drawing fills ~60% of its 64 px cell,
    // plus its outline): what has to clear the silhouette.
    public static float VisibleHalf(float size) { return size * .5f * .62f; }

    public static BossHearts Attach(BossActor owner, int count)
    {
        var go = new GameObject("Hearts");
        go.transform.SetParent(owner.transform, false);
        var h = go.AddComponent<BossHearts>();
        h.Bind(owner, count);
        return h;
    }

    void Bind(BossActor owner, int count)
    {
        actor = owner;
        left = count;
        quartersLeft = count * HeartDamage.PerHeart;
        var enc = BossEncounter.Instance;
        if (enc != null && enc.Shell.Any) { top = enc.Shell.Tier; layer = enc.ShownLayer; quartersLeft = enc.ShownQuarters; }   // (HeartTiers: the bar showing first)
        heartSize = BossConfig.HeartSize;
        BuildHearts(count);
        PopInAll();
        Place(0f, 0f);   // from nothing
    }

    public BossActor Owner => actor;
    public int Left => left;

    // BossEncounter: the hearts left now, and where the hit that cost them landed.
    public void SetLeft(int n, Vector3 at) => SetQuarterHealth(Mathf.Max(0, n) * HeartDamage.PerHeart, at);

    public void SetQuarterHealth(int quarters, Vector3 at, int newLayer = 0)
    {
        quarters = Mathf.Max(0, quarters);
        if (quarters == quartersLeft && newLayer == layer) return;
        quartersLeft = quarters;
        layer = newLayer;
        left = HeartDamage.WholeHearts(quarters);
        impactPending = true;
        impactAt = at;
        base.Update();
    }

    // One step: the loss check, the ring, the crumbles. realDt: re-measure
    // clock; dt: world time (0 while frozen).
    public void Step(float realDt, float dt)
    {
        if (!isActiveAndEnabled) return;
        base.Update();
        Place(realDt, dt);
        StepBreaks(dt);
    }

    // Retreating: the hearts go at once.
    public void Hide() { gameObject.SetActive(false); }

    protected override void Start() { }
    protected override void Update() { }       // stepped by BossActor
    protected override void LateUpdate() { }

    public override void BuildHearts() { BuildHearts(MaxHearts); }

    protected override int MaxHearts { get { return actor != null ? BossConfig.HeartsForWorld(actor.World) : BossConfig.Hearts; } }
    protected override int RemainingHearts() { return left; }
    protected override bool UsesQuarterHearts => true;
    protected override int RemainingQuarterUnits() => quartersLeft;
    protected override int CurrentLayer => layer;
    protected override int TopLayer => top;

    protected override bool TakeImpact(out Vector3 at)
    {
        at = impactAt;
        bool had = impactPending;
        impactPending = false;
        return had;
    }

    protected override Sprite HeartSprite() { return Resources.Load<Sprite>(SpritePath); }

    protected override Color HeartTint
    {
        get { return actor != null && actor.Boss != null ? actor.Boss.heartColor : new Color(.8f, .4f, 1f); }
    }

    protected override bool Outlined { get { return true; } }
    // The fight can open with the world frozen (no finger down): the hearts
    // still pop in.
    protected override bool PopOnRealTime { get { return true; } }

    // The player's Halo orbit (ShipHeartStyles) made a boss's: one revolution
    // every HeartRevolutionSeconds, a steady spin (no loops or turn-backs: the
    // hearts are its health), and the plane precessing round the boss so the
    // ring tumbles slowly in 3D instead of sitting on one tilt.
    protected override void ResolveStyle(out HeartStyle s, out OrbitStyle o)
    {
        s = HeartStyle.Halo;
        o = ShipHeartStyles.Orbit(s);
        o.radius = 1f;
        o.speed = 2f * Mathf.PI / Mathf.Max(.5f, BossConfig.HeartRevolutionSeconds);
        o.flourishEvery = 0f;
        o.precession = BossConfig.HeartPrecession;
        styleKey = 0;
    }

    // Body() switches on the art key's text; the ring asks for it for every heart every frame, so the last answer is kept.
    BossDef bodyFor;
    string bodyKey;
    Rect bodyRect;

    Rect CachedBody()
    {
        var boss = actor != null ? actor.Boss : null;
        if (!Application.isPlaying) return Body(boss);   // (edit-mode tests change the config the fallback reads)
        string key = boss != null ? boss.artKey : null;
        if (!ReferenceEquals(boss, bodyFor) || !ReferenceEquals(key, bodyKey) || bodyRect.width <= 0f)
        {
            bodyRect = Body(boss);
            bodyFor = boss;
            bodyKey = key;
        }
        return bodyRect;
    }

    // A back heart over the body's ellipse would be hidden by it.
    protected override bool HidesBehindHull(Vector2 at)
    {
        Rect b = CachedBody();
        Vector3 p = transform.position;
        float dx = (at.x - (p.x + b.center.x)) / (b.width * .5f), dy = (at.y - (p.y + b.center.y)) / (b.height * .5f);
        return dx * dx + dy * dy < 1f;
    }

    protected override Bounds HullBounds()
    {
        var b = CachedBody();
        Vector3 p = transform.position;
        return new Bounds(new Vector3(p.x + b.center.x, p.y + b.center.y, p.z), new Vector3(b.width, b.height, .1f));
    }

    protected override SpriteRenderer HullRenderer() { return null; }

    protected override void SortOrders(int hullOrder, out int front, out int back)
    {
        front = BossConfig.HeartSortingOrder;   // over the boss, its charges and its shots
        back = BossActor.PlayOrder - 1;         // round the back: just under the body
    }

    // Clear of the silhouette's ellipse by HeartClearance at the heart's edge.
    protected override Vector2 OrbitRadiiFor(Bounds hull)
    {
        float r = BossConfig.HeartClearance + VisibleHalf(heartSize);
        return new Vector2(hull.extents.x + r, hull.extents.y + r);
    }

    protected override float CrowdGrowth { get { return 0f; } }
    protected override bool WarpOrbit { get { return false; } }
    // A lost heart's neighbours close up in about 0.3 s.
    protected override float KeepGain { get { return 9f; } }
    protected override float KeepMax { get { return 4f; } }

    // Inside the rails and under the HUD band, on this screen.
    protected override Rect ClampRect()
    {
        float edge = Mathf.Min(BossRails.InnerEdge, BossRails.DrawnInnerEdge) - BossConfig.HeartScreenMargin;
        float top = PlayField.Live.bandBottom - BossConfig.HeartScreenMargin;
        return Rect.MinMaxRect(-edge, CameraFit.ViewBottom, edge, top);
    }

    public Rect SafeRect => ClampRect();
    // Half the box a heart is kept inside the safe rect by (HeartOrbit.HalfBox).
    public float HeartHalfBox => heartSize * .5f * (1f + DepthScale) * 1.2f;
}
