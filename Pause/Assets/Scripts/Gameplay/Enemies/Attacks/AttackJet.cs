using UnityEngine;

// THE JET: a cone or a column of fire, water, frost or neon out of a nozzle (plan phase 1a;
// docs/world-attacks-design.md: Ember's flamethrower (Brand, Fighter 3), Tide's pressure jet, Frost's ray as a thin jet,
// Space's lance, and a boss's flame sweep).
//
//   Tell   (>= .7 s, FR1) a flare grows at the nozzle (3 stages) and the dotted outline of the jet's exact hit
//          shape is drawn from the first frame (>= .4 s before it goes live); a sweep also shows the
//          end footprint and the arc between. The nozzle rides its shooter until it ignites; the
//          direction is locked ONCE, when the tell starts ("aim locked at the tell"); the nozzle's place is locked
//          FootprintLockSeconds (.45 s) before ignition, so the outline the player dodges stays still for the whole
//          of the last stretch of the tell, whatever the shooter does.
//   Live   the jet burns for LiveSeconds (<= .6 s, FR3): a trapezoid from the nozzle (BaseHalf) to LENGTH (TipHalf).
//          A cone is TipHalf > BaseHalf; a column is equal. A sweep turns the whole jet about the nozzle at
//          a tip speed under MaxTipSpeed (so the ship, 7 u/s, always out-runs it).
//   After  the jet flickers out (harmless).
//
// LENGTH. A spec with maxLength > 0 reaches the locked target: length = dist + ReachPast, kept between the
// spec's length and maxLength, and a cone widens in proportion (the same half angle): a flamethrower on a
// shooter 4 u above the ship still reaches him.
//
// THE DRAWING is one body sprite per frame (the world's <w>_attack_jet.png when present, else the procedural
// strip from AttackHazardArt.JetBody), scaled to the hit shape x DrawFactor: drawn a fifth wider than it hits
// (the doc's "drawn bigger than the hitbox" rule, never past 2x). The hit shape, the preview outline and the
// drawing are all BuildShape / the same corners, so they cannot disagree.
//
// USE: AttackJet.Arm(JetSpec.Flame(world), muzzleWorldPos, targetWorldPos, tellSeconds, shooterGameObject);
// EnemyBrain does exactly this for EnemyAttack.Jet (EnemyBehaviour.Jet(JetSpec)); .Follow(transform, offset)
// rides the nozzle on the shooter until it ignites; .Ignite() is the brain's Release.
public enum JetStyle { Flame, Water, Frost, Lance }

[System.Serializable]
public struct JetSpec
{
    public JetStyle style;
    public float length;        // u, the nominal reach (the cone's tip is at this distance for its TipHalf)
    public float maxLength;     // > length: the jet reaches its locked target, up to this (0: fixed length)
    public float baseHalf;      // half width at the nozzle (u)
    public float tipHalf;       // half width at the far end; > baseHalf makes a cone
    public float liveSeconds;   // <= AttackJet.MaxLiveSeconds
    public float sweepDeg;      // the angle the jet turns through while it burns (>= 0); the tip speed is capped
    public bool centered;       // the sweep is centred on the locked aim (a boss's flame sweep); else it starts there and turns toward the lane's middle
    public float ride;         // share of the board's scroll the jet keeps (0: a pilot's, in world space)
    public int world;

    public bool IsCone => tipHalf > baseHalf + .01f;

    // Ember's flamethrower: a cone 1.8 u long, 12 deg half angle (.76 u wide at the tip), live .6 s, swept 10 deg.
    public static JetSpec Flame(int world) => new JetSpec
    {
        style = JetStyle.Flame, length = 1.8f, maxLength = 4.4f, baseHalf = .07f, tipHalf = .38f, liveSeconds = .6f, sweepDeg = 10f, world = world,
    };
    // Tide's pressure jet (the bubble mine, Needlefish): a straight column of water, .34 wide.
    public static JetSpec Pressure(int world) => new JetSpec
    {
        style = JetStyle.Water, length = 2.5f, maxLength = 4.4f, baseHalf = .17f, tipHalf = .17f, liveSeconds = .5f, world = world,
    };
    // Frost's ray as a thin jet: a hair of cold light, .26 wide.
    public static JetSpec Ray(int world) => new JetSpec
    {
        style = JetStyle.Frost, length = 2.5f, maxLength = 4.4f, baseHalf = .13f, tipHalf = .13f, liveSeconds = .45f, world = world,
    };
    // Space's lance: a short neon column pulse (1.8 u, .2 wide, .35 s live).
    public static JetSpec Lance(int world) => new JetSpec
    {
        style = JetStyle.Lance, length = 1.8f, maxLength = 4.4f, baseHalf = .1f, tipHalf = .1f, liveSeconds = .35f, world = world,
    };

    public static JetSpec Standard(int world)
    {
        switch (world)
        {
            case 1: return Ray(world);
            case 3: return Flame(world);
            case 4: return Pressure(world);
            default: return Lance(world);
        }
    }
}

public sealed class AttackJet : AttackHazard
{
    public const float MaxLiveSeconds = .6f;   // FR3
    public const float MaxTipSpeed = 3f;       // u/s: how fast a sweep may move the far end
    public const float MaxAimDeg = 60f;        // the locked direction stays within this of straight down
    public const float ReachPast = .6f;        // a reaching jet ends this far beyond its target
    public const float FootprintLockSeconds = .45f;   // the nozzle rides its shooter until this long before ignition; the last stretch of the tell shows the exact, still footprint (FR2)
    public const float DrawFactor = 1.2f;      // drawn this much wider than it hits
    public const float FadeSeconds = .2f;
    public const float BodyFps = 12f;
    public const int PoolSize = 4;
    public const int SortBody = 12, SortFlare = 13;
    public const float FlareSize = .9f;        // u: the nozzle flare's drawn size at stage 2

    static readonly System.Func<Transform, AttackJet> maker = Create;
    public static AttackPool<AttackJet> Pool => AttackPools.Get("jets", PoolSize, maker);

    static AttackJet Create(Transform root)
    {
        var go = new GameObject("AttackJet");
        go.transform.SetParent(root, false);
        var j = go.AddComponent<AttackJet>();
        j.body = Piece(go.transform, "Body", SortBody);
        j.nozzle = Piece(go.transform, "Nozzle", SortFlare);
        j.tip = Piece(go.transform, "Tip", SortFlare);
        j.Register();
        go.SetActive(false);
        return j;
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

    // Takes a jet from the pool and starts its tell: nozzle and target in world space. Null: all busy.
    public static AttackJet Arm(JetSpec spec, Vector2 muzzle, Vector2 target, float tellSeconds, GameObject shooter)
    {
        var j = Pool.Take();
        if (j == null) return null;
        j.Setup(spec, muzzle, target, tellSeconds, shooter);
        return j;
    }

    // ---- geometry (static: the shape, the drawing, the dodge bot and the tests share it) ----------------

    public static Vector2 Dir(float rad) => new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

    // The locked direction: from the nozzle toward the target, kept within MaxAimDeg of straight down.
    public static float AimRad(Vector2 muzzle, Vector2 target)
    {
        Vector2 to = target - muzzle;
        if (to.sqrMagnitude < 1e-6f) return -Mathf.PI * .5f;
        float deg = Mathf.Clamp(Vector2.SignedAngle(Vector2.down, to), -MaxAimDeg, MaxAimDeg);   // + : toward -x ... (SignedAngle is counter-clockwise)
        return -Mathf.PI * .5f + deg * Mathf.Deg2Rad;
    }

    // The length a spec runs at for a nozzle and a locked target.
    public static float LengthFor(in JetSpec spec, Vector2 muzzle, Vector2 target)
    {
        float len = Mathf.Max(.5f, spec.length);
        if (spec.maxLength <= len) return len;
        return Mathf.Clamp(Vector2.Distance(muzzle, target) + ReachPast, len, spec.maxLength);
    }

    // Half width at the far end for a length (a cone widens in proportion).
    public static float TipHalfFor(in JetSpec spec, float length)
    {
        if (!spec.IsCone) return spec.baseHalf;
        return spec.baseHalf + (spec.tipHalf - spec.baseHalf) * length / Mathf.Max(.5f, spec.length);
    }

    // The sweep a spec may run at for a length: the far end never moves faster than MaxTipSpeed.
    public static float SweepFor(in JetSpec spec, float length, float liveSeconds)
    {
        if (spec.sweepDeg <= 0f) return 0f;
        float cap = MaxTipSpeed * liveSeconds / Mathf.Max(.5f, length) * Mathf.Rad2Deg;
        return Mathf.Min(spec.sweepDeg, cap);
    }

    // The four corners of the jet at a direction: nozzle left, tip left, tip right, nozzle right.
    public static void Corners(Vector2 origin, float dirRad, float length, float baseHalf, float tipHalf, out Vector2 a, out Vector2 b, out Vector2 c, out Vector2 d)
    {
        Vector2 u = Dir(dirRad), n = new Vector2(-u.y, u.x);
        a = origin - n * baseHalf;
        b = origin + u * length - n * tipHalf;
        c = origin + u * length + n * tipHalf;
        d = origin + n * baseHalf;
    }

    // ---- instance ----

    SpriteRenderer body, nozzle, tip;
    JetSpec spec;
    Vector2 origin;
    float aimRad, sweepRad, dirRad, length, baseHalf, tipHalf, liveTotal, fadeFor;
    float bodyLen, bodyTipW, bodyBaseW, bodyTopShift;
    bool bold, artBody;
    int bodyFrames;
    readonly Sprite[] bodySprites = new Sprite[6], nozzleSprites = new Sprite[3], tipSprites = new Sprite[3];
    Transform follow;
    Vector2 followOffset;

    public override float ThreatWeight => 1.5f;                 // FR7
    protected override string Label => "jet";
    public JetSpec Spec => spec;
    public Vector2 Origin => origin;
    public float Direction => dirRad;                           // now
    public float AimDirection => aimRad;                        // as locked at the tell
    public float SweepRadians => sweepRad;                      // signed: the turn from the start to the end of the live window
    public float Length => length;
    public float BaseHalf => baseHalf;
    public float TipHalf => tipHalf;
    public float LiveSeconds => liveTotal;
    public SpriteRenderer BodyRenderer => body;
    public SpriteRenderer NozzleRenderer => nozzle;
    public SpriteRenderer TipRenderer => tip;
    public bool ArtBody => artBody;

    // The direction the jet points at `t` seconds into its burn (the start is the locked aim, or aim - sweep / 2 when centred).
    public float DirectionAt(float t)
    {
        float k = liveTotal > 0f ? Mathf.Clamp01(t / liveTotal) : 0f;
        return StartRad + sweepRad * k;
    }
    public float StartRad => spec.centered ? aimRad - sweepRad * .5f : aimRad;

    void Setup(JetSpec s, Vector2 muzzle, Vector2 aimAt, float tellSeconds, GameObject by)
    {
        spec = s;
        spec.liveSeconds = Mathf.Clamp(s.liveSeconds, .1f, MaxLiveSeconds);
        liveTotal = spec.liveSeconds;
        origin = muzzle;
        aimRad = AimRad(muzzle, aimAt);
        length = LengthFor(in spec, muzzle, aimAt);
        baseHalf = Mathf.Max(.04f, s.baseHalf);
        tipHalf = Mathf.Max(baseHalf, TipHalfFor(in spec, length));
        // the sweep turns toward the lane's middle (a centred one is symmetric; the sign is then +)
        float sweep = SweepFor(in spec, length, liveTotal) * Mathf.Deg2Rad;
        sweepRad = sweep;
        if (sweep > 0f && !spec.centered)
        {
            Vector2 endTip = muzzle + Dir(aimRad + sweep) * length, startTip = muzzle + Dir(aimRad) * length;
            if (Mathf.Abs(endTip.x) > Mathf.Abs(startTip.x)) sweepRad = -sweep;
        }
        dirRad = StartRad;
        fadeFor = 0f;
        follow = null;
        // every sprite is looked up once per take (a cached int-keyed look-up), never per frame
        bold = ShotOutline.UseBold;
        artBody = AttackHazardArt.JetArt(s.world);
        bodyFrames = artBody ? 6 : AttackHazardArt.JetProceduralFrames;
        for (int i = 0; i < bodyFrames; i++)
            bodySprites[i] = AttackHazardArt.JetBody(s.world, spec.style, i, length, baseHalf, tipHalf, bold, out bodyLen, out bodyTipW, out bodyBaseW);
        for (int i = 0; i < 3; i++)
        {
            nozzleSprites[i] = AttackHazardArt.JetNozzle(s.world, spec.style, i, bold);
            tipSprites[i] = AttackHazardArt.JetTip(s.world, spec.style, i, bold);
        }
        BeginTell(tellSeconds, by, s.world);
    }

    // The nozzle rides `t` (+ a local offset) until FootprintLockSeconds before it ignites; the direction stays where it was aimed.
    public void Follow(Transform t, Vector2 localOffset)
    {
        follow = t;
        followOffset = localOffset;
    }

    void BuildShape(float dir, bool guides)
    {
        shape.Clear();
        Corners(origin, dir, length, baseHalf, tipHalf, out Vector2 a, out Vector2 b, out Vector2 c, out Vector2 d);
        shape.BeginPoly(guides);
        shape.Point(a); shape.Point(b); shape.Point(c); shape.Point(d);
        shape.EndPoly();
        if (!guides || Mathf.Abs(sweepRad) < .01f) return;
        // a sweep: the end footprint and the arc the far end travels
        float endDir = StartRad + sweepRad;
        Corners(origin, endDir, length, baseHalf, tipHalf, out Vector2 a2, out Vector2 b2, out Vector2 c2, out Vector2 d2);
        shape.BeginLoop();
        shape.LoopPoint(a2); shape.LoopPoint(b2); shape.LoopPoint(c2); shape.LoopPoint(d2);
        shape.EndLoop();
        shape.BeginLoop();
        int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(sweepRad) * Mathf.Rad2Deg / 6f), 2, 12);
        for (int i = 0; i <= steps; i++)
            shape.LoopPoint(origin + Dir(Mathf.Lerp(StartRad, endDir, i / (float)steps)) * length);
        shape.EndLoop();
    }

    // ---- drawing ----

    void PlaceNozzle(int stage, bool on)
    {
        var spr = nozzleSprites[Mathf.Clamp(stage, 0, 2)];
        nozzle.sprite = spr;
        nozzle.enabled = on && spr != null;
        nozzle.transform.position = new Vector3(origin.x, origin.y, 0f);
        float size = spr != null ? Mathf.Max(.01f, spr.bounds.size.x) : 1f;
        nozzle.transform.localScale = Vector3.one * (FlareSize * (stage == 0 ? .55f : stage == 1 ? .8f : 1f) / size * (artBody ? 1.1f : 1f));
    }

    void DrawBody()
    {
        int frame = Mathf.FloorToInt(age * BodyFps) % bodyFrames;
        var spr = bodySprites[frame];
        body.sprite = spr;
        body.enabled = spr != null;
        if (spr == null) return;
        // scaled to the hit shape x DrawFactor: the length to the hit length, the tip's width to the hit tip's width
        float sy = length / Mathf.Max(.01f, bodyLen);
        float sx = (tipHalf * 2f * DrawFactor) / Mathf.Max(.01f, bodyTipW);
        float py = spr.pivot.y / Mathf.Max(1f, spr.rect.height);
        Vector2 u = Dir(dirRad);
        float shift = (1f - py) * spr.bounds.size.y * sy;   // a centre-pivot sprite sits half its height along the jet; a top-pivot one at the nozzle
        Vector2 at = origin + u * shift;
        var tr = body.transform;
        tr.position = new Vector3(at.x, at.y, 0f);
        tr.rotation = Quaternion.Euler(0f, 0f, dirRad * Mathf.Rad2Deg + 90f);
        tr.localScale = new Vector3(sx, sy, 1f);
        // the far end: sparks / splash on three stepped frames
        var ts = tipSprites[Mathf.Min(2, Mathf.FloorToInt(age * 8f) % 3)];
        tip.sprite = ts;
        tip.enabled = ts != null;
        Vector2 end = origin + u * length;
        tip.transform.position = new Vector3(end.x, end.y, 0f);
        float tsize = ts != null ? Mathf.Max(.01f, ts.bounds.size.x) : 1f;
        tip.transform.localScale = Vector3.one * (Mathf.Clamp(tipHalf * 2f * 1.3f, .5f, .85f) / tsize);
    }

    // ---- the hazard's life ----

    protected override void OnArmed()
    {
        BuildShape(StartRad, true);
        body.enabled = false;
        tip.enabled = false;
        PlaceNozzle(0, true);
    }

    protected override void OnTellStep(float dt)
    {
        if (follow != null && TellLeft > FootprintLockSeconds)
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
        // the flare grows through the tell: three stages, then blinks at the last
        float k = tellTotal > 0f ? t / tellTotal : 1f;
        int stage = k < .34f ? 0 : (k < .67f ? 1 : 2);
        PlaceNozzle(stage, stage < 2 || (Mathf.FloorToInt(t * 12f) & 1) == 0);
    }

    protected override void OnIgnited()
    {
        dirRad = StartRad;
        BuildShape(dirRad, false);
        PlaceNozzle(2, true);
        DrawBody();
    }

    protected override bool OnLiveStep(float dt)
    {
        if (spec.ride > 0f) origin.y -= EliteSystem.Scroll * spec.ride * dt;
        float at = Mathf.Min(age, liveTotal);
        dirRad = DirectionAt(at);
        BuildShape(dirRad, false);
        PlaceNozzle(2, true);
        DrawBody();
        return age < liveTotal;
    }

    protected override bool OnAfterStep(float dt)
    {
        fadeFor += dt;
        // the jet flickers out in stepped frames
        bool on = (Mathf.FloorToInt(fadeFor * 20f) & 1) == 0 && fadeFor < FadeSeconds;
        body.enabled = body.sprite != null && on;
        tip.enabled = tip.sprite != null && on && fadeFor < FadeSeconds * .6f;
        nozzle.enabled = nozzle.sprite != null && fadeFor < FadeSeconds * .6f;
        return fadeFor < FadeSeconds;
    }

    protected override void OnEnded()
    {
        if (body != null) body.enabled = false;
        if (nozzle != null) nozzle.enabled = false;
        if (tip != null) tip.enabled = false;
        follow = null;
    }

    protected override void ReturnToPool() { var p = AttackPools.Find<AttackJet>("jets"); if (p != null) p.Release(this); }
}
