using UnityEngine;

// THE BLAST: an expanding ring of bars with a guaranteed crack (plan phase 1c;
// docs/world-attacks-design.md: Frost's "cold blast" for the Glacier Golem and the Frost boss).
//
//   Tell   (>= .7 s) a pulsing glyph ring at the muzzle, the crack's two edges marked, and the dotted
//          outline of the first ring plus the crack's wedge out to full reach (AttackPreview, FR2).
//          The crack is aimed ONCE, at the pilot, when the tell starts ("aim locked at the tell").
//   Live   the ring grows from StartRadius to Reach at Speed (<= 2.8 u/s, FR3). The bars are
//          tangent quads on the circle, spread evenly over everything but the crack
//          (BarAt): the free gap between the two bars beside it is never narrower than GapDeg,
//          so the corridor at the pilot's range is >= 1.4 u (2 r sin(GapDeg / 2), r >= 1.8 u).
//   After  the last bars flicker out.
//
// The hit shape, the preview outline and the drawn bars are all BarAt, so they cannot disagree.
// The bars are drawn thicker than they hit (the doc's 2x rule): the hit half-thickness is
// BarHalf (.09), the drawn bar .28.
//
// USE (a roster enemy, an elite, a boss; nothing here knows who fires it):
//
//   var spec = BlastSpec.Standard(world);                    // tune: bars, reach, speed, gapDeg...
//   AttackBlast.Arm(spec, muzzleWorldPos, targetWorldPos, tellSeconds, shooterGameObject);
//       // -> the AttackBlast in its tell (null when the pool is busy: a busy screen skips one);
//       //    it ignites itself when the tell is over, or call .Ignite() at your Release.
//   blast.Follow(shooterTransform, localOffset)             // the ring's origin rides the shooter until it ignites
//
// EnemyBrain does exactly this for EnemyAttack.Blast (EnemyBehaviour.Blast(...)).
[System.Serializable]
public struct BlastSpec
{
    public int bars;            // bars a full circle would have (density); fewer are drawn around the crack
    public float startRadius;   // u: the ring is born this far from the muzzle
    public float reach;         // u: it ends here
    public float speed;         // u/s, capped at AttackBlast.MaxSpeed
    public float gapDeg;        // the crack, degrees of arc (>= AttackBlast.MinGapDeg)
    public float gapOffsetDeg;  // turn the crack off the pilot by this much, toward the lane's middle (0: aimed straight at him, so standing still is safe)
    public float barHalf;       // hit half-thickness (u)
    public float ride;          // share of the board's scroll the ring keeps (0: a pilot's, in world space)
    public int world;           // whose material it wears (AttackHazardArt.RampOf)

    public static BlastSpec Standard(int world) => new BlastSpec
    {
        bars = 18, startRadius = .9f, reach = 3.4f, speed = 2.6f, gapDeg = 70f, gapOffsetDeg = 0f, barHalf = .09f, ride = 0f, world = world,
    };

    // A ring that can reach a pilot far below a hovering shooter (a Golem holds 5-6 u above the ship, the standard ring ends at 3.4 u):
    // 6 u of reach, a bar every ~11 deg so the wall stays closed (a gap under .35 u between bars at the rim, the ship is .56 wide), 2.7 u/s.
    public static BlastSpec Wide(int world)
    {
        var s = Standard(world);
        s.bars = 32; s.reach = 6f; s.speed = 2.7f;
        return s;
    }

    public float LiveSeconds => (Mathf.Max(reach, startRadius + .1f) - startRadius) / Mathf.Min(AttackBlast.MaxSpeed, Mathf.Max(.1f, speed));
}

public sealed class AttackBlast : AttackHazard
{
    public const float MaxSpeed = 2.8f;      // FR3
    public const float MinGapDeg = 60f;      // the crack never narrower than this (chord 2 r sin(30 deg) = r >= 1.8 u at the pilot's range)
    public const float MaxBarLength = .875f; // the art cell's bar
    public const float BarFill = .82f;       // a bar spans this share of its slot (never touches its neighbour)
    public const float DrawThickness = .28f;
    public const float FadeSeconds = .2f;
    public const int MaxBars = 32;
    public const int PoolSize = 3;
    public const int SortBars = 12, SortGlyph = 13;
    public const float FlickerFps = 10f;

    // ---- the pool ----
    static readonly System.Func<Transform, AttackBlast> maker = Create;   // (a cached delegate: a method group converted per call allocates)
    public static AttackPool<AttackBlast> Pool => AttackPools.Get("blasts", PoolSize, maker);

    static AttackBlast Create(Transform root)
    {
        var go = new GameObject("AttackBlast");
        go.transform.SetParent(root, false);
        var b = go.AddComponent<AttackBlast>();
        b.bars = new SpriteRenderer[MaxBars];
        for (int i = 0; i < MaxBars; i++) b.bars[i] = Piece(go.transform, "Bar" + i, SortBars);
        b.glyph = Piece(go.transform, "Glyph", SortGlyph);
        b.markerA = Piece(go.transform, "GapA", SortGlyph);
        b.markerB = Piece(go.transform, "GapB", SortGlyph);
        b.Register();
        go.SetActive(false);
        return b;
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

    // Takes a blast from the pool and starts its tell: muzzle and target in world space. Null: all busy.
    public static AttackBlast Arm(BlastSpec spec, Vector2 muzzle, Vector2 target, float tellSeconds, GameObject shooter)
    {
        var b = Pool.Take();
        if (b == null) return null;
        b.Setup(spec, muzzle, target, tellSeconds, shooter);
        return b;
    }

    // ---- geometry (static: the shape, the drawing and the dodge bot's scenario share it) ----------------

    // How many bars a ring of `spec` draws around its crack.
    public static int BarCount(in BlastSpec spec)
    {
        float gap = Mathf.Clamp(spec.gapDeg, MinGapDeg, 180f);
        return Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp(spec.bars, 6, MaxBars) * (360f - gap) / 360f), 3, MaxBars);
    }

    public static float StepDeg(in BlastSpec spec) => (360f - Mathf.Clamp(spec.gapDeg, MinGapDeg, 180f)) / BarCount(in spec);

    // Bar k (0 .. BarCount-1) of the ring at `radius` round `origin`, crack centred on world angle `gapRad`:
    // the segment a -> b along its tangent. Always present (the crack is simply where no bar is).
    public static void BarAt(in BlastSpec spec, Vector2 origin, float gapRad, float radius, int k, out Vector2 a, out Vector2 b, out float angleRad)
    {
        float gap = Mathf.Clamp(spec.gapDeg, MinGapDeg, 180f);
        float step = StepDeg(in spec);
        angleRad = gapRad + (gap * .5f + (k + .5f) * step) * Mathf.Deg2Rad;
        float length = Mathf.Min(MaxBarLength, radius * step * Mathf.Deg2Rad * BarFill);
        Vector2 u = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
        Vector2 tg = new Vector2(-u.y, u.x);
        Vector2 c = origin + u * radius;
        a = c - tg * (length * .5f);
        b = c + tg * (length * .5f);
    }

    // The crack's world angle for a muzzle and a target: straight at the target (+ the offset, toward the lane's middle), then
    // turned (at most MaxLaneTurnDeg, the smallest turn that does it) until the free chord at the target's range, clipped by the rails,
    // is a real corridor of at least MinCorridor + a margin -- so a pilot hugging a rail still has room to stand in the crack (FR4).
    public const float MinCorridor = 1.4f, CorridorMargin = .25f, MaxLaneTurnDeg = 40f;

    public static float AimGap(in BlastSpec spec, Vector2 muzzle, Vector2 target, float railEdge)
    {
        Vector2 to = target - muzzle;
        float range = Mathf.Max(1.2f, to.magnitude);
        float aim = Mathf.Atan2(to.y, to.x);
        if (spec.gapOffsetDeg != 0f)
        {
            // off the pilot, toward the lane's middle (never into a rail)
            float off = Mathf.Abs(spec.gapOffsetDeg) * Mathf.Deg2Rad;
            float xa = muzzle.x + Mathf.Cos(aim + off) * range, xb = muzzle.x + Mathf.Cos(aim - off) * range;
            aim += Mathf.Abs(xa) <= Mathf.Abs(xb) ? off : -off;
        }
        if (railEdge <= 0f) return aim;
        // the free chord at that range spans this half-angle either side of the crack's middle
        float half = (Mathf.Clamp(spec.gapDeg, MinGapDeg, 180f) * .5f + StepDeg(in spec) * .5f) * Mathf.Deg2Rad;
        int steps = Mathf.RoundToInt(MaxLaneTurnDeg / 2f);
        for (int i = 0; i <= steps; i++)
        {
            for (int s = 0; s < 2; s++)
            {
                if (i == 0 && s == 1) continue;
                float cand = aim + (s == 0 ? 1f : -1f) * i * 2f * Mathf.Deg2Rad;
                if (ClippedChord(muzzle, cand, half, range, railEdge) >= MinCorridor + CorridorMargin) return cand;   // the smallest turn that leaves a corridor
            }
        }
        return aim;
    }

    // Length (u) of the chord between the crack's two ends at `range`, the part of it that lies between the rails.
    static float ClippedChord(Vector2 muzzle, float crackRad, float halfRad, float range, float rail)
    {
        Vector2 a = muzzle + new Vector2(Mathf.Cos(crackRad - halfRad), Mathf.Sin(crackRad - halfRad)) * range;
        Vector2 b = muzzle + new Vector2(Mathf.Cos(crackRad + halfRad), Mathf.Sin(crackRad + halfRad)) * range;
        float len = Vector2.Distance(a, b);
        float dx = b.x - a.x;
        if (Mathf.Abs(dx) < 1e-5f) return Mathf.Abs(a.x) <= rail ? len : 0f;
        float t0 = (-rail - a.x) / dx, t1 = (rail - a.x) / dx;
        if (t0 > t1) { float t = t0; t0 = t1; t1 = t; }
        t0 = Mathf.Max(0f, t0); t1 = Mathf.Min(1f, t1);
        return t1 > t0 ? (t1 - t0) * len : 0f;
    }

    // ---- instance ----

    SpriteRenderer[] bars;
    SpriteRenderer glyph, markerA, markerB;
    BlastSpec spec;
    Vector2 origin, target;
    float gapRad, radius, speed;
    int count;
    bool bold;
    readonly Sprite[] barSprites = new Sprite[4], glyphSprites = new Sprite[2], gapSprites = new Sprite[2];   // resolved once per take
    Transform follow;
    Vector2 followOffset;
    float fadeFor;

    public override float ThreatWeight => 2f;                  // FR7: a blast counts as two shots
    protected override string Label => "cold blast";
    public BlastSpec Spec => spec;
    public Vector2 Origin => origin;
    public float GapRad => gapRad;
    public float Radius => radius;
    public int Bars => count;
    public float RadialSpeed => speed;
    public SpriteRenderer BarRenderer(int k) => bars[k];
    public SpriteRenderer GlyphRenderer => glyph;

    void Setup(BlastSpec s, Vector2 muzzle, Vector2 aimAt, float tellSeconds, GameObject by)
    {
        spec = s;
        spec.speed = Mathf.Clamp(s.speed, .5f, MaxSpeed);
        spec.gapDeg = Mathf.Max(MinGapDeg, s.gapDeg);
        spec.startRadius = Mathf.Max(.5f, s.startRadius);
        spec.reach = Mathf.Max(spec.startRadius + .5f, s.reach);
        origin = muzzle;
        target = aimAt;
        speed = spec.speed;
        count = BarCount(in spec);
        gapRad = AimGap(in spec, origin, target, BossRails.DrawnInnerEdge);
        radius = spec.startRadius;
        follow = null;
        fadeFor = 0f;
        // every sprite is looked up once per take (a cached int-keyed look-up), never per frame
        bold = ShotOutline.UseBold;
        for (int i = 0; i < 4; i++) barSprites[i] = AttackHazardArt.RingBar(s.world, i, bold);
        for (int i = 0; i < 2; i++)
        {
            glyphSprites[i] = AttackHazardArt.RingGlyph(s.world, i, bold);
            gapSprites[i] = AttackHazardArt.GapMarker(s.world, i, bold);
        }
        BeginTell(tellSeconds, by, s.world);
    }

    // The ring's origin rides `t` (+ a local offset) until it ignites; the crack stays where it was aimed.
    public void Follow(Transform t, Vector2 localOffset)
    {
        follow = t;
        followOffset = localOffset;
    }

    protected override void OnArmed()
    {
        BuildShape(spec.startRadius, true);
        HideBars();
        PlaceGuides();
    }

    void BuildShape(float r, bool guides)
    {
        shape.Clear();
        Vector2 firstOuter = Vector2.zero;
        int pts = count * 4 <= AttackShape.MaxLoopPoints ? 2 : 1;
        // the quads: hit geometry only (their outline is drawn as the ring's two arcs below)
        for (int k = 0; k < count; k++)
        {
            Vector2 a, b; float ang;
            BarAt(in spec, origin, gapRad, r, k, out a, out b, out ang);
            shape.AddQuad(a, b, spec.barHalf, false);
        }
        if (!guides) return;
        // the ring's outline: the outer edge from the crack's one side to the other, back along the inner edge
        shape.BeginLoop();
        for (int k = 0; k < count; k++)
        {
            Vector2 a, b; float ang;
            BarAt(in spec, origin, gapRad, r, k, out a, out b, out ang);
            Vector2 n = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * spec.barHalf;
            if (pts == 2) shape.LoopPoint(a + n);
            shape.LoopPoint(b + n);
        }
        for (int k = count - 1; k >= 0; k--)
        {
            Vector2 a, b; float ang;
            BarAt(in spec, origin, gapRad, r, k, out a, out b, out ang);
            Vector2 n = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * spec.barHalf;
            shape.LoopPoint(b - n);
            if (pts == 2) shape.LoopPoint(a - n);
        }
        shape.EndLoop();
        // the crack: its wedge from the first ring out to full reach (the safe way through, outlined)
        float gap = spec.gapDeg * .5f * Mathf.Deg2Rad;
        Vector2 lo = new Vector2(Mathf.Cos(gapRad - gap), Mathf.Sin(gapRad - gap)), hi = new Vector2(Mathf.Cos(gapRad + gap), Mathf.Sin(gapRad + gap));
        shape.BeginLoop();
        shape.LoopPoint(origin + lo * r);
        shape.LoopPoint(origin + lo * spec.reach);
        for (int i = 1; i < 4; i++)
        {
            float a = Mathf.Lerp(-gap, gap, i / 4f);
            shape.LoopPoint(origin + new Vector2(Mathf.Cos(gapRad + a), Mathf.Sin(gapRad + a)) * spec.reach);
        }
        shape.LoopPoint(origin + hi * spec.reach);
        shape.LoopPoint(origin + hi * r);
        shape.EndLoop();
    }

    // ---- drawing ----

    void DrawBars()
    {
        int flick = HostileShotPalette.FlickerHot(age) ? 1 : 0;
        float step = StepDeg(in spec) * Mathf.Deg2Rad;
        for (int i = 0; i < MaxBars; i++)
        {
            var sr = bars[i];
            if (i >= count) { sr.enabled = false; continue; }
            Vector2 a, b; float ang;
            BarAt(in spec, origin, gapRad, radius, i, out a, out b, out ang);
            Vector2 c = (a + b) * .5f;
            float len = Vector2.Distance(a, b);
            // two flicker frames; art cells 0-3: the pair is (i % 2) then +2
            int frame = flick + ((i & 1) << 1);
            var spr = barSprites[frame];
            sr.sprite = spr;
            sr.enabled = spr != null;
            var tr = sr.transform;
            tr.position = new Vector3(c.x, c.y, 0f);
            tr.rotation = Quaternion.Euler(0f, 0f, (ang + Mathf.PI * .5f) * Mathf.Rad2Deg);
            tr.localScale = new Vector3(Mathf.Max(.05f, len / AttackHazardArt.BarLength), 1f, 1f);
        }
    }

    void PlaceGuides()
    {
        float tick = Mathf.FloorToInt(t * 8f);
        int f = ((int)tick) & 1;
        Vector2 at = origin;
        var g = glyphSprites[f];
        glyph.sprite = g;
        glyph.enabled = g != null && State == Phase.Tell;
        glyph.transform.position = new Vector3(at.x, at.y, 0f);
        float k = Mathf.Clamp01(tellTotal > 0f ? t / tellTotal : 1f);
        glyph.transform.localScale = Vector3.one * Mathf.Lerp(.7f, 1.5f, k);
        glyph.transform.rotation = Quaternion.identity;
        var m = gapSprites[f];
        float gap = spec.gapDeg * .5f * Mathf.Deg2Rad;
        float mr = Mathf.Min(spec.reach, spec.startRadius + 1.1f);
        PlaceMarker(markerA, m, at, gapRad - gap, mr);
        PlaceMarker(markerB, m, at, gapRad + gap, mr);
    }

    void PlaceMarker(SpriteRenderer sr, Sprite s, Vector2 o, float ang, float r)
    {
        sr.sprite = s;
        sr.enabled = s != null && State == Phase.Tell;
        Vector2 p = o + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
        sr.transform.position = new Vector3(p.x, p.y, 0f);
        sr.transform.rotation = Quaternion.identity;
        sr.transform.localScale = Vector3.one;
    }

    // ---- the hazard's life ----

    protected override void OnTellStep(float dt)
    {
        if (follow != null)
        {
            Vector2 now = (Vector2)follow.position + followOffset;
            Vector2 d = now - origin;
            if (d.sqrMagnitude > 1e-8f)
            {
                origin = now;
                shape.Offset(d);
                var pv = Preview;
                if (pv != null) pv.Follow(d);
            }
        }
        else if (spec.ride > 0f)
        {
            Vector2 d = new Vector2(0f, -EliteSystem.Scroll * spec.ride * dt);
            origin += d;
            shape.Offset(d);
            var pv = Preview;
            if (pv != null) pv.Follow(d);
        }
        PlaceGuides();
    }

    void HideBars() { for (int i = 0; i < bars.Length; i++) bars[i].enabled = false; }

    protected override void OnIgnited()
    {
        glyph.enabled = markerA.enabled = markerB.enabled = false;
        radius = spec.startRadius;
        BuildShape(radius, false);
        DrawBars();
    }

    protected override bool OnLiveStep(float dt)
    {
        if (spec.ride > 0f) origin.y -= EliteSystem.Scroll * spec.ride * dt;
        radius += speed * dt;
        if (radius >= spec.reach) { radius = spec.reach; BuildShape(radius, false); DrawBars(); return false; }
        BuildShape(radius, false);
        DrawBars();
        return true;
    }

    protected override bool OnAfterStep(float dt)
    {
        fadeFor += dt;
        // the last bars flicker out, thinning on stepped frames
        bool on = (Mathf.FloorToInt(fadeFor * 20f) & 1) == 0 && fadeFor < FadeSeconds;
        for (int i = 0; i < count; i++) bars[i].enabled = bars[i].sprite != null && on;
        return fadeFor < FadeSeconds;
    }

    protected override void OnEnded()
    {
        if (bars != null) for (int i = 0; i < bars.Length; i++) if (bars[i] != null) bars[i].enabled = false;
        if (glyph != null) glyph.enabled = false;
        if (markerA != null) markerA.enabled = false;
        if (markerB != null) markerB.enabled = false;
        follow = null;
    }

    protected override void ReturnToPool() { var p = AttackPools.Find<AttackBlast>("blasts"); if (p != null) p.Release(this); }
}
