using UnityEngine;

// A boss's five hearts (BossConfig.Hearts): its health, spinning round it.
//
// The shared HeartOrbit engine (the player's lives, the elites' hearts) on
// a boss's terms: one flat ring in the screen plane round the body's drawn
// silhouette (Body, measured from each atlas's idle drawings), the hearts
// evenly spaced and turning together once every HeartRevolutionSeconds,
// drawn over the boss and its shots (HeartSortingOrder) and under the HUD.
// The ring is an ellipse just clear of the body (HeartClearance past it,
// plus half a heart); where it would cross a rail or reach the HUD band it
// flattens against that side instead (ClampRect), so a heart never goes
// under either on any screen. The white Vfx/eliteHeart drawn in the boss's
// own heartColor (never the player's red), with HeartOutline's trace.
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
    int left;
    bool impactPending;
    Vector3 impactAt;

    // The ellipse round the body's drawn silhouette (every pixel of alpha
    // >= 0.5 in every idle drawing lies inside it), relative to the boss's
    // centre, in world units at BossWorldSize 3.3: its centre and
    // half-axes, as a rect. Measured from the atlases (the silhouette's box,
    // grown to the ellipse through its furthest pixel); BossHeartsTest
    // re-measures them from the source files.
    public static Rect Body(BossDef boss)
    {
        switch (boss != null ? boss.artKey : null)
        {
            case "Space": return Around(0f, .05f, 1.78f, 1.69f);
            case "Frost": return Around(0f, -.10f, 1.94f, 1.80f);
            case "Verdant": return Around(.01f, -.16f, 1.72f, 1.67f);
            case "Ember": return Around(.02f, .12f, 1.82f, 1.55f);
            case "Tide": return Around(0f, -.03f, 2.06f, 1.78f);
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
        heartSize = BossConfig.HeartSize;
        BuildHearts(count);
        PopInAll();
        Place(0f, 0f);   // from nothing
    }

    public BossActor Owner => actor;
    public int Left => left;

    // BossEncounter: the hearts left now, and where the hit that cost them landed.
    public void SetLeft(int n, Vector3 at)
    {
        n = Mathf.Max(0, n);
        if (n == left) return;
        left = n;
        impactPending = true;
        impactAt = at;
        base.Update();   // the heart darts now, not next step
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

    public override void BuildHearts() { BuildHearts(Mathf.Max(1, BossConfig.Hearts)); }

    protected override int MaxHearts { get { return 8; } }
    protected override int RemainingHearts() { return left; }

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

    protected override void ResolveStyle(out HeartStyle s, out OrbitStyle o)
    {
        s = HeartStyle.Halo;
        o = new OrbitStyle
        {
            radius = 1f,
            speed = 2f * Mathf.PI / Mathf.Max(.5f, BossConfig.HeartRevolutionSeconds),
            tilt = 0f,   // flat: every heart in front of the boss all the way round
        };
        styleKey = 0;
    }

    protected override Bounds HullBounds()
    {
        var b = Body(actor != null ? actor.Boss : null);
        Vector3 p = transform.position;
        return new Bounds(new Vector3(p.x + b.center.x, p.y + b.center.y, p.z), new Vector3(b.width, b.height, .1f));
    }

    protected override SpriteRenderer HullRenderer() { return null; }

    protected override void SortOrders(int hullOrder, out int front, out int back)
    {
        front = back = BossConfig.HeartSortingOrder;
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
