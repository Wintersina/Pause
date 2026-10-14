using UnityEngine;

// ONE COVER BOULDER (pooled by CoverBoulders; docs/cover-boulders.md).
//
//   Off --Spawn--> Live --(boss / OnBossBegin)--> Rumble -> Crack -> Burst --> Off
//                   |--(world change, death, scene)--> Fade --> Off
//
// Live, Rumble and Crack are SOLID (the shape blocks shots, bodies and the ship); Burst and Fade are not.
// Its position moves on the world's clock (Step(dt)), its drawing rides the same numbers, and the solid circles
// are recomputed from the drawing's centre / rotation every step, so what blocks is exactly what is drawn.
// Nothing allocates after Build().
public sealed class CoverBoulder
{
    public enum Phase { Off, Live, Rumble, Crack, Burst, Fade }

    public const float RumbleSeconds = .55f, CrackSeconds = .35f, BurstSeconds = .5f, FadeSeconds = .45f, DecorFadeSeconds = 1.8f;   // a landform sinks out slowly
    public const float RumbleShake = .04f;
    // EMERGE / RECEDE (docs 13): it comes out of the backdrop, small, hazy and see-through, and grows into the play layer over
    // EmergeSeconds (smooth in scale and opacity: no pop); on the way out it sinks back over the last RecedeUnits above the view bottom.
    public const float EmergeSeconds = 2.6f, EmergeScale = .62f, RecedeUnits = 1.8f;
    public const int MaxCircles = 4;
    public const int SortingOrder = -7;

    public Phase State { get; private set; }
    public int World { get; private set; }
    public int Variant { get; private set; }
    public Vector2 Pos;                    // centre of the drawing (u), bob excluded
    public Vector2 Vel;                    // own fall and sideways drift (u/s)
    public float Width { get; private set; }
    public float Bound { get; private set; }   // radius round the centre that holds every circle, whatever the rotation
    public float Rotation { get; private set; }
    public int Side { get; private set; }  // which side of the lane it hugs (-1 / +1)
    public float Age { get; private set; }
    // INTERACTIVE (Space) boulders are solid cover; every other world's is a decorative LANDFORM of the backdrop: drawn, never solid, never in any query.
    public bool Interactive => entry != null && entry.interactive;
    public bool Decorative => entry != null && !entry.interactive;
    public bool Solid => Interactive && (State == Phase.Live || State == Phase.Rumble || State == Phase.Crack);
    public bool Alive => State != Phase.Off;
    public bool DrawnFracture { get; private set; }   // the burst plays the drawn frames (else the procedural fracture)
    public int Count { get; private set; }
    public Vector2 CircleCentre(int i) { return new Vector2(cx[i], cy[i]); }
    public float CircleRadius(int i) { return cr[i]; }
    public CoverBoulderEntry Entry => entry;
    public Vector2 Visual => new Vector2(Pos.x, Pos.y + bobY);
    public float PhaseTime => t;
    public Transform StormFxRoot => stormFx != null ? stormFx.Root : null;

    CoverBoulderEntry entry;
    CoverVariant art;
    Color bodyTint = Color.white;
    float presence = 1f, viewBottom = -100f;
    float scale, spin, phaseOff, bobY, t, vyTarget, flicker;
    readonly float[] cx = new float[MaxCircles], cy = new float[MaxCircles], cr = new float[MaxCircles];
    GameObject go;
    SpriteRenderer body, wash;
    CoverStormFx stormFx;   // the Storm landform's effects (built on first need, then pooled with the boulder)
    SpriteRenderer[] pieces;
    readonly Vector2[] pieceVel = new Vector2[CoverBoulderArt.Tiles * CoverBoulderArt.Tiles];
    readonly float[] pieceSpin = new float[CoverBoulderArt.Tiles * CoverBoulderArt.Tiles];
    bool chipped, burstDone;

    public void Build(Transform root, string name)
    {
        go = new GameObject(name);
        go.transform.SetParent(root, false);
        body = NewRenderer("Body", SortingOrder);
        wash = NewRenderer("Wash", SortingOrder + 1);
        int n = CoverBoulderArt.Tiles * CoverBoulderArt.Tiles;
        pieces = new SpriteRenderer[n];
        for (int i = 0; i < n; i++) pieces[i] = NewRenderer("Piece", SortingOrder);
        go.SetActive(false);
    }

    SpriteRenderer NewRenderer(string n, int order)
    {
        var g = new GameObject(n);
        g.transform.SetParent(go.transform, false);
        var sr = g.AddComponent<SpriteRenderer>();
        sr.sortingOrder = order;
        sr.enabled = false;
        return sr;
    }

    // ---- life ----

    public void Spawn(int world, int variant, CoverVariant v, float widthU, Vector2 at, Vector2 vel, int side, float rng01, Color tint)
    {
        bodyTint = tint;
        viewBottom = -100f;
        entry = CoverBoulderTable.For(world);
        World = world;
        art = v;
        Variant = variant;
        Width = widthU;
        scale = v.WorldScale(widthU);
        Pos = at;
        Vel = vel;
        Side = side;
        Age = t = 0f;
        State = Phase.Live;
        phaseOff = rng01 * Mathf.PI * 2f;
        spin = entry.drift == CoverDrift.Tumble
            ? Mathf.Lerp(entry.spin.x, entry.spin.y, Mathf.Repeat(rng01 * 7.31f, 1f)) * (rng01 < .5f ? -1f : 1f)
            : 0f;
        Rotation = entry.drift == CoverDrift.Tumble ? rng01 * 360f : 0f;
        bobY = 0f; flicker = 1f;
        chipped = burstDone = false;
        DrawnFracture = v.frames != null && v.frames.Length >= CoverBoulderArt.FramesPerStrip;
        float b = 0f;
        for (int i = 0; i < v.circles.Length && i < MaxCircles; i++)
            b = Mathf.Max(b, new Vector2(v.circles[i].x, v.circles[i].y).magnitude + v.circles[i].r);
        Count = Mathf.Min(MaxCircles, v.circles.Length);
        Bound = b / CoverBoulderArt.Ppu * scale;
        if (entry.stormFx)
        {
            if (stormFx == null) stormFx = new CoverStormFx(go.transform);
            stormFx.Begin(v, entry.folder, rng01);
        }
        else if (stormFx != null) stormFx.Hide();
        go.SetActive(true);
        body.sprite = v.intact;
        body.enabled = true;
        body.color = new Color(bodyTint.r, bodyTint.g, bodyTint.b, 0f);
        wash.enabled = false;
        for (int i = 0; i < pieces.Length; i++) pieces[i].enabled = false;
        Place();
    }

    public void Release()
    {
        State = Phase.Off;
        if (stormFx != null) stormFx.Hide();
        if (go != null) go.SetActive(false);
        if (body != null) body.enabled = false;
    }

    // The quiet exit (a world change, a death, a scene): it fades, nothing blows.
    public void Dismiss()
    {
        if (State == Phase.Live) { State = Phase.Fade; t = 0f; }
        else if (State == Phase.Rumble || State == Phase.Crack) { State = Phase.Fade; t = 0f; }
    }

    // A boss arrives (CoverBoulders.OnBossBegin): the destruction sequence.
    public void Destroy()
    {
        if (State != Phase.Live) return;
        if (Decorative) { Dismiss(); return; }   // a landform far below the ship simply sinks back into the backdrop
        State = Phase.Rumble;
        t = 0f;
        chipped = burstDone = false;
    }

    // A test / preview puts it exactly here (turned `rotDeg`) and holds it still.
    public void Hold(Vector2 at, float rotDeg = 0f, bool settled = true)
    {
        if (settled) Age = Mathf.Max(Age, EmergeSeconds);
        Pos = at; Vel = Vector2.zero; Rotation = rotDeg; spin = 0f;
        Place();
    }

    // ---- step ----

    // Emerge x recede: 0 (hidden in the backdrop) .. 1 (fully in the play layer). Smooth, so nothing pops.
    public float Presence => presence;
    public float VisualScale => EmergeK();
    public SpriteRenderer GetComponentOnBody() { return body; }
    public static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
    float Recede() { return viewBottom < -99f ? 1f : Smooth((Pos.y - viewBottom) / (Bound + RecedeUnits)); }
    float EmergeK() { return Mathf.Lerp(EmergeScale, 1f, Smooth(Age / EmergeSeconds)); }   // (the solid shape never shrinks on the way out)

    public void Step(float dt, CoverBoulders.StepContext ctx)
    {
        if (State == Phase.Off || dt <= 0f) return;
        viewBottom = ctx.viewBottom;
        Age += dt;
        t += dt;
        switch (State)
        {
            case Phase.Live:
                Drift(dt, ctx);
                if (Pos.y + Bound + .6f < ctx.viewBottom) { Release(); return; }
                break;
            case Phase.Rumble:
                Drift(dt * .35f, ctx);   // the rumble stills it
                if (t >= RumbleSeconds) { State = Phase.Crack; t = 0f; ctx.fx.Burst(Visual, Vector2.up, 1f, 6, .6f, 1.6f, .6f, entry.debris, entry.chip2); }
                break;
            case Phase.Crack:
                Drift(dt * .15f, ctx);
                if (t >= CrackSeconds)
                {
                    State = Phase.Burst; t = 0f;
                    ctx.OnBurst(this);
                }
                break;
            case Phase.Burst:
                if (t >= BurstSeconds) { Release(); return; }
                break;
            case Phase.Fade:
                if (t >= (Decorative ? DecorFadeSeconds : FadeSeconds)) { Release(); return; }
                Drift(dt, ctx);
                break;
        }
        Place();
    }

    void Drift(float dt, CoverBoulders.StepContext ctx)
    {
        // sideways: inside the band that keeps one side of the lane free (docs 7); turn round at its edges
        Pos.x += Vel.x * dt;
        Pos.y += Vel.y * dt;
        if (Interactive)
        {
            float lo, hi;
            CoverBoulders.Band(Side, Bound, out lo, out hi);
            if (Pos.x < lo) { Pos.x = lo; if (Vel.x < 0f) Vel.x = -Vel.x; }
            else if (Pos.x > hi) { Pos.x = hi; if (Vel.x > 0f) Vel.x = -Vel.x; }
        }
        float tt = Age;
        switch (entry.drift)
        {
            case CoverDrift.Tumble:
                Rotation += spin * dt;
                break;
            case CoverDrift.Slide:
                Rotation = 0f;
                break;
            case CoverDrift.Sway:
            case CoverDrift.Rock:
                Rotation = entry.swayDeg * Mathf.Sin(tt * 6.2831853f / entry.swayPeriod + phaseOff);
                break;
            case CoverDrift.Bob:
                Rotation = entry.swayDeg * Mathf.Sin(tt * 6.2831853f / entry.swayPeriod + phaseOff);
                bobY = entry.bobAmp * Mathf.Sin(tt * 6.2831853f / entry.bobPeriod + phaseOff * .5f);
                break;
            case CoverDrift.Hover:
                Rotation = entry.swayDeg * Mathf.Sin(tt * 6.2831853f / entry.swayPeriod + phaseOff);
                bobY = entry.bobAmp * Mathf.Sin(tt * 6.2831853f / entry.bobPeriod + phaseOff * .5f);
                flicker = .92f + .08f * Mathf.Sin(tt * 23f) * Mathf.Sin(tt * 7.3f);
                break;
        }
    }

    // The drawing and the solid circles, from the numbers above.
    void Place()
    {
        Vector2 vis = Visual;
        float shake = 0f;
        if (State == Phase.Rumble) shake = RumbleShake * Mathf.Clamp01(t / RumbleSeconds * 1.4f);
        else if (State == Phase.Crack) shake = RumbleShake * 1.3f;
        Vector2 shaken = vis;
        if (shake > 0f) shaken += new Vector2(Mathf.Sin(Age * 83f), Mathf.Cos(Age * 67f)) * shake;

        float rad = Rotation * Mathf.Deg2Rad, c = Mathf.Cos(rad), s = Mathf.Sin(rad);
        float ek = EmergeK();
        float emerge = Smooth(Age / EmergeSeconds);
        presence = emerge * Mathf.Lerp(.25f, 1f, Recede());
        float k = scale * ek / CoverBoulderArt.Ppu;
        for (int i = 0; i < Count; i++)
        {
            float lx = art.circles[i].x * k, ly = art.circles[i].y * k;
            cx[i] = vis.x + lx * c - ly * s;
            cy[i] = vis.y + lx * s + ly * c;
            cr[i] = art.circles[i].r * k;
        }

        var tr = go.transform;
        tr.position = new Vector3(shaken.x, shaken.y, 0f);
        tr.rotation = Quaternion.Euler(0f, 0f, Rotation);
        tr.localScale = new Vector3(scale * ek, scale * ek, 1f);

        if (stormFx != null && stormFx.Active) stormFx.Step(Age, FxAlpha());

        switch (State)
        {
            case Phase.Live:
                body.enabled = true; body.sprite = art.intact;
                body.color = new Color(bodyTint.r * flicker, bodyTint.g * flicker, bodyTint.b * flicker, presence);
                // the fog it comes out of: a flat haze over the silhouette that thins as it grows into the play layer
                // (a landform far below the ship keeps a steady depth haze on top of the emerge fog)
                wash.enabled = presence < .985f || Decorative; wash.sprite = art.intact;
                { var hz = entry.haze; hz.a = (1f - presence) * presence + (Decorative ? CoverBoulders.DecorHaze * presence : 0f); wash.color = hz; }
                break;
            case Phase.Rumble:
                {
                    body.enabled = true;
                    body.sprite = DrawnFracture && t > RumbleSeconds * .5f ? art.frames[0] : art.intact;
                    body.color = new Color(bodyTint.r, bodyTint.g, bodyTint.b, presence);
                    // the crack light flickers through it, faster and stronger as the burst nears
                    float f = Mathf.Clamp01(t / RumbleSeconds);
                    float flick = .5f + .5f * Mathf.Sin(Age * (28f + 40f * f));
                    wash.enabled = true; wash.sprite = body.sprite;
                    var col = entry.crack; col.a = f * (.12f + .22f * flick);
                    wash.color = col;
                }
                break;
            case Phase.Crack:
                {
                    float f = Mathf.Clamp01(t / CrackSeconds);
                    if (DrawnFracture)
                    {
                        body.enabled = true; body.sprite = art.frames[f < .5f ? 0 : 1];
                        body.color = new Color(bodyTint.r, bodyTint.g, bodyTint.b, presence);
                        wash.enabled = true; wash.sprite = body.sprite;
                        var col = entry.crack; col.a = .22f + .25f * f; wash.color = col;
                    }
                    else
                    {
                        // procedural: the body hides behind its 3 x 3 pieces, which part a little along the cracks
                        body.enabled = false; wash.enabled = false;
                        PlacePieces(shaken, c, s, k, f * .06f, 1f);
                    }
                }
                break;
            case Phase.Burst:
                {
                    float f = Mathf.Clamp01(t / BurstSeconds);
                    wash.enabled = false;
                    if (DrawnFracture)
                    {
                        int frame = 2 + Mathf.Min(2, (int)(f * 3f));
                        body.enabled = true; body.sprite = art.frames[frame];
                        body.color = new Color(bodyTint.r, bodyTint.g, bodyTint.b, presence);
                    }
                    else
                    {
                        body.enabled = false;
                        PlacePieces(shaken, c, s, k, .06f + f * 1.6f, 1f - f * f);
                    }
                }
                break;
            case Phase.Fade:
                {
                    float a = 1f - Smooth(t / (Decorative ? DecorFadeSeconds : FadeSeconds));
                    body.enabled = true; body.sprite = art.intact; body.color = new Color(bodyTint.r, bodyTint.g, bodyTint.b, a * presence);
                    wash.enabled = false;
                }
                break;
        }
    }

    float FxAlpha()
    {
        switch (State)
        {
            case Phase.Live: return presence;
            case Phase.Rumble: return presence * (1f - Smooth(t / RumbleSeconds));
            case Phase.Fade: return presence * (1f - Smooth(t / (Decorative ? DecorFadeSeconds : FadeSeconds)));
            default: return 0f;
        }
    }

    // `spread` is how far the pieces have flown out (u at scale 1 x k), `alpha` their opacity.
    void PlacePieces(Vector2 centre, float c, float s, float k, float spread, float alpha)
    {
        int n = pieces.Length;
        for (int i = 0; i < n; i++)
        {
            var sr = pieces[i];
            if (!chipped) { /* one-off setup below */ }
            sr.enabled = alpha > .01f;
            if (!sr.enabled) continue;
            sr.sprite = art.pieces[i];
            Vector2 off = art.pieceCentre[i];
            // outward from the centre, the centre tile (4) a little up; each tile its own speed and turn
            Vector2 dir = off.sqrMagnitude > 1e-4f ? off.normalized : Vector2.up;
            float sp = 1f + (i * 7 % 5) * .35f;
            Vector2 p = off + dir * spread * sp * (Burst() ? 3.5f : 1f);
            // the tile is a child of the boulder's transform at pre-scaled local space
            sr.transform.localPosition = new Vector3(p.x, p.y, 0f);
            sr.transform.localRotation = Quaternion.Euler(0f, 0f, spread * 90f * ((i & 1) == 0 ? 1f : -1f) * (Burst() ? 4f : 0f));
            sr.transform.localScale = Vector3.one;
            var col = Color.Lerp(Color.white, entry.crack, Burst() ? .25f : 0f);
            col.a = alpha;
            sr.color = col;
        }
        chipped = true;
    }

    bool Burst() { return State == Phase.Burst; }

    // ---- queries ----

    // Does a disc (centre p, radius r) touch the solid shape?
    public bool Touches(Vector2 p, float r)
    {
        for (int i = 0; i < Count; i++)
        {
            float dx = p.x - cx[i], dy = p.y - cy[i], R = cr[i] + r;
            if (dx * dx + dy * dy < R * R) return true;
        }
        return false;
    }
}
