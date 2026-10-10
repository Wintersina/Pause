using UnityEngine;

// THE STRIKE: a telegraphed lane, then a column, then a ground burst (plan phase 1d;
// docs/world-attacks-design.md: Frost's icicle drop, Ember's eruption column, Tide's thunder strike).
//
//   Tell   (>= .7 s, FR1) a lane glyph blinks on the ground at the lane and the dotted outline of the
//          column's exact hit rectangle is drawn from the first frame (>= .4 s before it goes live).
//          The lane is chosen ONCE, when the tell starts ("aim locked at the tell"); the glyph
//          rides the board with the ground (spec.ride).
//   Live   the column is harmful for LiveSeconds (<= .25 s, FR3): a rectangle HitHalf either side
//          of the lane from just under the impact point up to Height above it (0: out of the
//          top of the view). The body plays its frames at 24 fps (art) or flickers its pink-white
//          core on two frames (procedural).
//   After  the ground burst (3 frames) plays; harmless.
//
// A pattern of several lanes (the icicle drop's three, the eruption's four) is several strikes armed
// in the same tell. StrikeLanes.Pick spreads them around the pilot's lane with the columns
// at least MinLaneSpacing apart centre to centre, so the corridor between two columns is never
// under 1.4 u (FR4) -- including against a rail.
//
// USE: AttackStrike.Arm(StrikeSpec.Standard(world), laneX, impactY, tellSeconds, shooterGameObject)
// (EnemyBrain does it for EnemyAttack.Strike: one per lane, all ignited at its Release).
[System.Serializable]
public struct StrikeSpec
{
    public StrikeStyle style;
    public float hitHalf;      // half the column's hit width (u): .18 -> the doc's .36
    public float liveSeconds;  // <= AttackStrike.MaxLiveSeconds
    public float height;       // column height above the impact (u); 0 = out of the top of the view
    public float ride;         // share of the board's scroll the ground keeps (0: a pilot's, in world space)
    public int world;

    public static StrikeSpec Standard(int world)
    {
        var style = AttackHazardArt.StyleOf(world);
        return new StrikeSpec
        {
            style = style, hitHalf = .18f, liveSeconds = .25f, height = style == StrikeStyle.Eruption ? 4.2f : 0f, ride = 0f, world = world,
        };
    }
}

public sealed class AttackStrike : AttackHazard
{
    public const float MaxLiveSeconds = .25f;   // FR3
    public const float BurstSeconds = .36f;     // 3 frames at ~8 fps
    public const float MinLaneSpacing = 1.9f;   // centre to centre: the corridor between two columns is >= 1.54 u
    public const float FootDepth = .15f;        // the column ends a little under the impact point
    public const int MaxTiles = 8;
    public const int PoolSize = 8;
    public const int SortBody = 12, SortGlyph = 11, SortBurst = 13;
    public const float GlyphSize = 1f;
    public const float ColumnFps = 24f;

    static readonly System.Func<Transform, AttackStrike> maker = Create;
    public static AttackPool<AttackStrike> Pool => AttackPools.Get("strikes", PoolSize, maker);

    static AttackStrike Create(Transform root)
    {
        var go = new GameObject("AttackStrike");
        go.transform.SetParent(root, false);
        var s = go.AddComponent<AttackStrike>();
        s.tiles = new SpriteRenderer[MaxTiles];
        for (int i = 0; i < MaxTiles; i++) s.tiles[i] = Piece(go.transform, "Col" + i, SortBody);
        s.tip = Piece(go.transform, "Tip", SortBody);
        s.glyph = Piece(go.transform, "Glyph", SortGlyph);
        s.burst = Piece(go.transform, "Burst", SortBurst);
        s.Register();
        go.SetActive(false);
        return s;
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

    // Takes a strike from the pool and starts its tell on lane `laneX`, ground at `impactY`. Null: all busy.
    public static AttackStrike Arm(StrikeSpec spec, float laneX, float impactY, float tellSeconds, GameObject shooter)
    {
        var s = Pool.Take();
        if (s == null) return null;
        s.Setup(spec, laneX, impactY, tellSeconds, shooter);
        return s;
    }

    // ---- geometry (static: shared with the dodge bot's scenario) ----

    // The column's centre line for a strike at (laneX, impactY): from just under the impact up.
    public static void Column(in StrikeSpec spec, float laneX, float impactY, out Vector2 foot, out Vector2 top)
    {
        float topY = spec.height > 0f ? impactY + spec.height : Mathf.Max(CameraFit.ViewTop + .6f, impactY + 1f);
        foot = new Vector2(laneX, impactY - FootDepth);
        top = new Vector2(laneX, topY);
    }

    // ---- instance ----

    SpriteRenderer[] tiles;
    SpriteRenderer tip, glyph, burst;
    StrikeSpec spec;
    float laneX, impactY, burstFor, liveTotal, tileHeight;
    StrikeStyle style;
    bool bold, artBody;
    int bodyFrames;
    readonly Sprite[] bodySprites = new Sprite[6], glyphSprites = new Sprite[2], burstSprites = new Sprite[3];
    Sprite tipSprite;

    public override float ThreatWeight => 1f;                  // FR7: a strike counts as one shot
    protected override string Label => "strike";
    public StrikeSpec Spec => spec;
    public float LaneX => laneX;
    public float ImpactY => impactY;
    public float LiveSeconds => liveTotal;
    public float HitHalf => spec.hitHalf;
    public SpriteRenderer GlyphRenderer => glyph;
    public SpriteRenderer BodyRenderer(int i) => tiles[i];
    public SpriteRenderer BurstRenderer => burst;
    public int TilesShown { get; private set; }
    public float DrawnWidth { get; private set; }

    void Setup(StrikeSpec s, float lane, float impact, float tellSeconds, GameObject by)
    {
        spec = s;
        spec.hitHalf = Mathf.Max(.05f, s.hitHalf);
        spec.liveSeconds = Mathf.Clamp(s.liveSeconds, .05f, MaxLiveSeconds);
        liveTotal = spec.liveSeconds;
        style = s.style;
        laneX = lane;
        impactY = impact;
        burstFor = 0f;
        // every sprite is looked up once per take (a cached int-keyed look-up), never per frame
        bold = ShotOutline.UseBold;
        artBody = AttackHazardArt.StrikeArt(s.world);
        bodyFrames = artBody ? 6 : 2;
        float dw;
        for (int i = 0; i < bodyFrames; i++) bodySprites[i] = AttackHazardArt.ColumnBody(s.world, style, i, bold, out tileHeight, out dw);
        DrawnWidth = AttackHazardArt.ColumnDrawWidth(s.world, style, bold);
        tipSprite = AttackHazardArt.ColumnTip(s.world, style, bold);
        for (int i = 0; i < 2; i++) glyphSprites[i] = AttackHazardArt.StrikeGlyph(s.world, style, i, bold);
        for (int i = 0; i < 3; i++) burstSprites[i] = AttackHazardArt.Burst(s.world, style, i, bold);
        BeginTell(tellSeconds, by, s.world);
    }

    void BuildShape()
    {
        shape.Clear();
        Vector2 foot, top;
        Column(in spec, laneX, impactY, out foot, out top);
        shape.AddQuad(foot, top, spec.hitHalf, true);   // the hit rectangle IS the outline
    }

    protected override void OnArmed()
    {
        BuildShape();
        HideBody();
        PlaceGlyph();
    }

    void PlaceGlyph()
    {
        int f = Mathf.FloorToInt(t * 8f) & 1;
        var g = glyphSprites[f];
        glyph.sprite = g;
        glyph.enabled = g != null && State == Phase.Tell;
        glyph.transform.position = new Vector3(laneX, impactY, 0f);
        float size = g != null ? Mathf.Max(.01f, g.bounds.size.x) : 1f;
        glyph.transform.localScale = Vector3.one * (GlyphSize / size);
    }

    void HideBody()
    {
        for (int i = 0; i < tiles.Length; i++) if (tiles[i] != null) tiles[i].enabled = false;
        if (tip != null) tip.enabled = false;
        TilesShown = 0;
    }

    // The column's tiles, laid foot to top; frame 0 / 1 (flicker) or the art's six (24 fps over the live window).
    void DrawBody()
    {
        Vector2 foot, top;
        Column(in spec, laneX, impactY, out foot, out top);
        int frame = artBody ? Mathf.Min(bodyFrames - 1, Mathf.FloorToInt(age * ColumnFps)) : (HostileShotPalette.FlickerHot(age) ? 1 : 0);
        var body = bodySprites[frame];
        float y = foot.y;
        int n = 0;
        if (tipSprite != null)
        {
            tip.sprite = tipSprite;
            tip.enabled = true;
            tip.transform.position = new Vector3(laneX, foot.y, 0f);
            tip.transform.localScale = Vector3.one;
            y = foot.y + tipSprite.bounds.size.y;
        }
        else tip.enabled = false;
        while (y < top.y && n < tiles.Length)
        {
            var sr = tiles[n++];
            sr.sprite = body;
            sr.enabled = body != null;
            sr.transform.position = new Vector3(laneX, y + tileHeight * .5f, 0f);
            sr.transform.localScale = Vector3.one;
            y += tileHeight;
        }
        for (int i = n; i < tiles.Length; i++) tiles[i].enabled = false;
        TilesShown = n;
    }

    protected override void OnTellStep(float dt)
    {
        if (spec.ride > 0f)
        {
            float dy = -EliteSystem.Scroll * spec.ride * dt;
            impactY += dy;
            shape.Offset(new Vector2(0f, dy));
            var pv = Preview;
            if (pv != null) pv.Follow(new Vector2(0f, dy));
        }
        PlaceGlyph();
    }

    protected override void OnIgnited()
    {
        glyph.enabled = false;
        BuildShape();
        DrawBody();
    }

    protected override bool OnLiveStep(float dt)
    {
        if (spec.ride > 0f)
        {
            impactY -= EliteSystem.Scroll * spec.ride * dt;
            BuildShape();
        }
        DrawBody();
        return age < liveTotal;
    }

    protected override bool OnAfterStep(float dt)
    {
        if (burstFor <= 0f)
        {
            HideBody();
            burst.transform.position = new Vector3(laneX, impactY, 0f);
        }
        burstFor += dt;
        int f = Mathf.Min(2, Mathf.FloorToInt(burstFor / (BurstSeconds / 3f)));
        var spr = burstSprites[f];
        burst.sprite = spr;
        burst.enabled = spr != null && burstFor < BurstSeconds;
        float size = spr != null ? Mathf.Max(.01f, spr.bounds.size.x) : 1f;
        burst.transform.localScale = Vector3.one * (1.3f / size);
        return burstFor < BurstSeconds;
    }

    protected override void OnEnded()
    {
        if (tiles != null) HideBody();
        if (glyph != null) glyph.enabled = false;
        if (burst != null) burst.enabled = false;
    }

    protected override void ReturnToPool() { var p = AttackPools.Find<AttackStrike>("strikes"); if (p != null) p.Release(this); }
}

// Where a pattern's lanes go: the pilot's lane first, the others either side at MinLaneSpacing or more,
// all inside the rails. The columns are .36 wide, so between any two there is a corridor of
// spacing - .36 >= 1.54 u (FR4); the open lane is wherever the pilot steps.
public static class StrikeLanes
{
    // Fills `into` with up to `count` lane x positions (nearest the pilot first); returns how many.
    public static int Pick(float pilotX, int count, float spacing, float railEdge, float hitHalf, float[] into)
    {
        spacing = Mathf.Max(AttackStrike.MinLaneSpacing, spacing);
        float limit = Mathf.Max(0f, railEdge - hitHalf - .1f);
        int n = 0;
        float first = Mathf.Clamp(pilotX, -limit, limit);
        into[n++] = first;
        // alternate sides, the side with more room first
        float dir = first >= 0f ? -1f : 1f;
        for (int k = 1; k <= 4 && n < count && n < into.Length; k++)
        {
            float x1 = first + dir * spacing * k, x2 = first - dir * spacing * k;
            if (Mathf.Abs(x1) <= limit && n < count) into[n++] = x1;
            if (Mathf.Abs(x2) <= limit && n < count && n < into.Length) into[n++] = x2;
        }
        return n;
    }
}
