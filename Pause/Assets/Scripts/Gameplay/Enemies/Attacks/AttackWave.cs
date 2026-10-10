using UnityEngine;

// THE WAVE: a band across the whole lane that falls with ONE guaranteed gap (plan phase 1b;
// docs/world-attacks-design.md: Tide's surf wave (Hammerhead, the Kraken) and Space's scan line (the Void Archon)).
//
//   Tell   (>= .7 s, FR1) the band's outline sits at its start height, the gap's two edges are marked with
//          blinking chevrons, and the dotted outline of the gap's lane (the safe way down) runs from the band to the
//          bottom of the view. The gap is chosen ONCE, when the tell starts ("aim locked at the tell"), around
//          the pilot's x (turned GapOffset toward the lane's middle: standing still is not safe).
//   Live   the band falls at Speed relative to the board (<= 3 u/s, FR3) from its start height, which is
//          kept at least MinFrontSeconds of travel above the pilot's row (the wave is never on him in under .8 s).
//          It is two rectangles, left and right of the gap, HitHalf either side of the band's middle; the gap
//          is never narrower than MinGap (1.4 u, FR4) and always inside the rails. Nothing else of the band hurts.
//   After  harmless (a beat): the band is below the view.
//
// The hit shape, the preview outline and the drawn tiles are all the same two rectangles, so they cannot disagree.
// The drawing is a tiled strip (the world's tide_attack_wave.png when present, else a procedural surf / neon strip)
// cropped to each rectangle: drawn thicker than it hits (never past 2x).
//
// USE: AttackWave.Arm(WaveSpec.Standard(world), muzzleWorldPos, targetWorldPos, tellSeconds, shooterGameObject);
// EnemyBrain does exactly this for EnemyAttack.Wave (EnemyBehaviour.Wave(WaveSpec)); .Ignite() is its Release.
public enum WaveStyle { Surf, Scan }

[System.Serializable]
public struct WaveSpec
{
    public WaveStyle style;
    public float gapWidth;      // u, >= AttackWave.MinGap
    public float gapOffset;     // u: the gap is turned this far off the pilot toward the lane's middle (0: centred on him)
    public float speed;         // u/s relative to the board, <= AttackWave.MaxSpeed
    public float hitHalf;       // half the band's hit thickness (u)
    public float ride;          // share of the board's scroll the band keeps (0: a pilot's, in world space)
    public int world;

    // Tide's surf wave: a foam-crested band .4 thick, a 1.6 u gap, 2.6 u/s.
    public static WaveSpec Surf(int world) => new WaveSpec
    {
        style = WaveStyle.Surf, gapWidth = 1.6f, gapOffset = 0f, speed = 2.6f, hitHalf = .2f, world = world,
    };
    // Space's scan line: a thin neon line (.24 thick), a 1.6 u gap, 3 u/s.
    public static WaveSpec Scan(int world) => new WaveSpec
    {
        style = WaveStyle.Scan, gapWidth = 1.6f, gapOffset = 0f, speed = 3f, hitHalf = .12f, world = world,
    };

    public static WaveSpec Standard(int world) => world == 0 ? Scan(world) : Surf(world);
}

public sealed class AttackWave : AttackHazard
{
    public const float MinGap = 1.4f;              // FR4: the corridor in the band is never narrower than this
    public const float MaxSpeed = 3f;              // FR3: u/s relative to the board
    public const float MinFrontSeconds = .8f;      // FR3: from the start of its fall to the pilot's row, at least this
    public const float RailMargin = .05f;          // the gap stays this far inside the rails
    public const float OverRail = .35f;            // the band reaches this far under the rails
    public const float DrawHeightFactor = 1.55f;   // drawn this much thicker than it hits (< 2x)
    public const float AfterSeconds = .12f;
    public const float BodyFps = 12f;
    public const int PoolSize = 3;
    public const int SortBody = 12, SortMarker = 13;

    static readonly System.Func<Transform, AttackWave> maker = Create;
    public static AttackPool<AttackWave> Pool => AttackPools.Get("waves", PoolSize, maker);

    static AttackWave Create(Transform root)
    {
        var go = new GameObject("AttackWave");
        go.transform.SetParent(root, false);
        var w = go.AddComponent<AttackWave>();
        w.left = Piece(go.transform, "Left", SortBody);
        w.right = Piece(go.transform, "Right", SortBody);
        w.capL = Piece(go.transform, "CapL", SortBody + 1);
        w.capR = Piece(go.transform, "CapR", SortBody + 1);
        w.markA = Piece(go.transform, "GapA", SortMarker);
        w.markB = Piece(go.transform, "GapB", SortMarker);
        w.left.drawMode = SpriteDrawMode.Tiled;
        w.right.drawMode = SpriteDrawMode.Tiled;
        w.Register();
        go.SetActive(false);
        return w;
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

    // Takes a band from the pool and starts its tell: the shooter's nozzle and the target in world space. Null: all busy.
    public static AttackWave Arm(WaveSpec spec, Vector2 muzzle, Vector2 target, float tellSeconds, GameObject shooter)
    {
        var w = Pool.Take();
        if (w == null) return null;
        w.Setup(spec, muzzle, target, tellSeconds, shooter);
        return w;
    }

    // ---- geometry (static: the shape, the drawing, the dodge bot and the tests share it) ----------------

    // Half the band's length: it spans the lane and runs a little under the rails.
    public static float BandHalfWidth => BossRails.DrawnInnerEdge + OverRail;

    // The gap's middle for a pilot at x: his x turned `gapOffset` toward the lane's middle, kept inside the rails.
    public static float GapCenter(in WaveSpec spec, float pilotX, float railEdge)
    {
        float gap = Mathf.Max(MinGap, spec.gapWidth);
        float x = pilotX;
        if (spec.gapOffset != 0f) x += (pilotX > 0f ? -1f : 1f) * Mathf.Abs(spec.gapOffset);
        float limit = Mathf.Max(0f, railEdge - gap * .5f - RailMargin);
        return Mathf.Clamp(x, -limit, limit);
    }

    // The band's start height: at the shooter, but at least MinFrontSeconds of fall above the pilot's row (a band that rides the
    // board falls that much faster on the screen: the scroll counts).
    public static float StartY(in WaveSpec spec, float muzzleY, float pilotY)
    {
        float speed = Mathf.Clamp(spec.speed, .5f, MaxSpeed) + Mathf.Max(0f, EliteSystem.Scroll * spec.ride);
        return Mathf.Max(muzzleY, pilotY + speed * MinFrontSeconds + Mathf.Max(.05f, spec.hitHalf) + .1f);
    }

    // The two rectangles of the band at height y, as their x extents: left [-half, gapX - gap/2], right [gapX + gap/2, half].
    public static void Extents(float gapX, float gapWidth, float half, out float leftTo, out float rightFrom)
    {
        leftTo = gapX - gapWidth * .5f;
        rightFrom = gapX + gapWidth * .5f;
    }

    // ---- instance ----

    SpriteRenderer left, right, capL, capR, markA, markB;
    WaveSpec spec;
    float gapX, gapWidth, half, y, startY, endY, fallSpeed, hitHalf, drawHeight;
    bool bold, artBody;
    readonly Sprite[] bodySprites = new Sprite[4];
    readonly Sprite[] markSprites = new Sprite[2];
    Sprite capLSprite, capRSprite;
    int bodyFrames;
    float afterFor;

    public override float ThreatWeight => 2f;                  // FR7
    protected override string Label => "surf wave";
    public WaveSpec Spec => spec;
    public float GapX => gapX;
    public float GapWidth => gapWidth;
    public float Y => y;
    public float StartHeight => startY;
    public float EndHeight => endY;
    public float HalfWidth => half;
    public float HitHalf => hitHalf;
    public float FallSpeed => fallSpeed;                       // relative to the board
    public float DrawnHeight => drawHeight;
    public bool ArtBody => artBody;
    public SpriteRenderer LeftRenderer => left;
    public SpriteRenderer RightRenderer => right;
    public SpriteRenderer MarkerA => markA;
    public SpriteRenderer MarkerB => markB;
    public SpriteRenderer CapLRenderer => capL;
    public SpriteRenderer CapRRenderer => capR;
    // the band's two rectangles' x extents now
    public bool HasLeft => gapX - gapWidth * .5f > -half + .05f;
    public bool HasRight => gapX + gapWidth * .5f < half - .05f;

    void Setup(WaveSpec s, Vector2 muzzle, Vector2 aimAt, float tellSeconds, GameObject by)
    {
        spec = s;
        spec.speed = Mathf.Clamp(s.speed, .5f, MaxSpeed);
        spec.gapWidth = Mathf.Max(MinGap, s.gapWidth);
        spec.hitHalf = Mathf.Max(.05f, s.hitHalf);
        fallSpeed = spec.speed;
        gapWidth = spec.gapWidth;
        hitHalf = spec.hitHalf;
        half = BandHalfWidth;
        gapX = GapCenter(in spec, aimAt.x, BossRails.DrawnInnerEdge);
        startY = StartY(in spec, muzzle.y, aimAt.y);
        y = startY;
        endY = CameraFit.ViewBottom - hitHalf - .35f;
        afterFor = 0f;
        // every sprite is looked up once per take (a cached int-keyed look-up), never per frame
        bold = ShotOutline.UseBold;
        artBody = AttackHazardArt.WaveArt(s.world);
        bodyFrames = artBody ? 4 : AttackHazardArt.WaveProceduralFrames;
        for (int i = 0; i < bodyFrames; i++) bodySprites[i] = AttackHazardArt.WaveBody(s.world, spec.style, i, bold);
        for (int i = 0; i < 2; i++) markSprites[i] = AttackHazardArt.WaveGapMarker(s.world, i, bold);
        capLSprite = AttackHazardArt.WaveCap(s.world, true);
        capRSprite = AttackHazardArt.WaveCap(s.world, false);
        var b0 = bodySprites[0];
        drawHeight = b0 != null ? b0.bounds.size.y : hitHalf * 2f * DrawHeightFactor;
        BeginTell(tellSeconds, by, s.world);
    }

    void BuildShape(bool guides)
    {
        shape.Clear();
        float leftTo, rightFrom;
        Extents(gapX, gapWidth, half, out leftTo, out rightFrom);
        if (HasLeft) shape.AddQuad(new Vector2(-half, y), new Vector2(leftTo, y), hitHalf, guides);
        if (HasRight) shape.AddQuad(new Vector2(rightFrom, y), new Vector2(half, y), hitHalf, guides);
        if (!guides) return;
        // the gap's lane, outlined from the band down to the bottom of the view: the way through
        float bottom = Mathf.Min(y - hitHalf, CameraFit.ViewBottom) - .2f;
        float top = Mathf.Min(y + hitHalf, CameraFit.ViewTop + .4f);
        shape.BeginLoop();
        shape.LoopPoint(new Vector2(leftTo, top));
        shape.LoopPoint(new Vector2(leftTo, bottom));
        shape.LoopPoint(new Vector2(rightFrom, bottom));
        shape.LoopPoint(new Vector2(rightFrom, top));
        shape.EndLoop();
    }

    // ---- drawing ----

    void PlaceSegment(SpriteRenderer sr, float x0, float x1, bool exists, int frame)
    {
        var spr = bodySprites[frame];
        sr.sprite = spr;
        sr.enabled = exists && spr != null && State != Phase.Tell && State != Phase.Off && x1 - x0 > .02f;
        if (!sr.enabled) return;
        sr.size = new Vector2(x1 - x0, drawHeight);
        sr.transform.position = new Vector3((x0 + x1) * .5f, y, 0f);
        sr.transform.rotation = Quaternion.identity;
        sr.transform.localScale = Vector3.one;
    }

    void PlaceBody()
    {
        float leftTo, rightFrom;
        Extents(gapX, gapWidth, half, out leftTo, out rightFrom);
        int frame = Mathf.FloorToInt(age * BodyFps) % bodyFrames;
        PlaceSegment(left, -half, leftTo, HasLeft, frame);
        PlaceSegment(right, rightFrom, half, HasRight, frame);
        PlaceCap(capL, capLSprite, leftTo, HasLeft && State == Phase.Live, drawHeight);
        PlaceCap(capR, capRSprite, rightFrom, HasRight && State == Phase.Live, drawHeight);
    }

    void PlaceCap(SpriteRenderer sr, Sprite s, float x, bool on, float h)
    {
        sr.sprite = s;
        sr.enabled = on && s != null;
        if (!sr.enabled) return;
        sr.transform.position = new Vector3(x, y, 0f);
        sr.transform.rotation = Quaternion.identity;
        sr.transform.localScale = Vector3.one * (h / Mathf.Max(.01f, s.bounds.size.y));
    }

    // The chevrons at the gap's two edges, pointing into the gap; they blink during the tell and stay while the band falls.
    void PlaceMarkers()
    {
        int f = Mathf.FloorToInt((State == Phase.Tell ? t : age) * 8f) & 1;
        var m = markSprites[f];
        float edge = gapWidth * .5f;
        float yy = y + (State == Phase.Tell ? hitHalf + .45f : hitHalf + .1f) ;
        PlaceMarker(markA, m, gapX - edge - .3f, yy, 90f);
        PlaceMarker(markB, m, gapX + edge + .3f, yy, -90f);
    }

    void PlaceMarker(SpriteRenderer sr, Sprite s, float x, float yy, float rotationDeg)
    {
        sr.sprite = s;
        sr.enabled = s != null && (State == Phase.Tell || State == Phase.Live);
        sr.transform.position = new Vector3(x, yy, 0f);
        sr.transform.rotation = Quaternion.Euler(0f, 0f, rotationDeg);
        float size = s != null ? Mathf.Max(.01f, s.bounds.size.x) : 1f;
        sr.transform.localScale = Vector3.one * (.55f / size);
    }

    // ---- the hazard's life ----

    protected override void OnArmed()
    {
        BuildShape(true);
        HideBody();
        PlaceMarkers();
    }

    void HideBody()
    {
        if (left != null) left.enabled = false;
        if (right != null) right.enabled = false;
        if (capL != null) capL.enabled = false;
        if (capR != null) capR.enabled = false;
    }

    protected override void OnTellStep(float dt)
    {
        if (spec.ride > 0f)
        {
            float dy = -EliteSystem.Scroll * spec.ride * dt;
            y += dy;
            startY += dy;
            shape.Offset(new Vector2(0f, dy));
            var pv = Preview;
            if (pv != null) pv.Follow(new Vector2(0f, dy));
        }
        PlaceMarkers();
    }

    protected override void OnIgnited()
    {
        y = startY;
        BuildShape(false);
        PlaceBody();
        PlaceMarkers();
    }

    protected override bool OnLiveStep(float dt)
    {
        y -= (fallSpeed + EliteSystem.Scroll * spec.ride) * dt;
        BuildShape(false);
        PlaceBody();
        PlaceMarkers();
        if (y > endY) return true;
        HideBody();   // below the view: nothing of it stays drawn
        markA.enabled = markB.enabled = false;
        return false;
    }

    protected override bool OnAfterStep(float dt)
    {
        if (afterFor <= 0f) { HideBody(); markA.enabled = markB.enabled = false; }
        afterFor += dt;
        return afterFor < AfterSeconds;
    }

    protected override void OnEnded()
    {
        HideBody();
        if (markA != null) markA.enabled = false;
        if (markB != null) markB.enabled = false;
    }

    protected override void ReturnToPool() { var p = AttackPools.Find<AttackWave>("waves"); if (p != null) p.Release(this); }
}
