using UnityEngine;

// ONE BOULDER (pooled by CoverBoulders; docs/cover-boulders.md): a decorative landform of the backdrop.
//
//   Off --Spawn--> Live --(world change, death, boss, scene)--> Fade --> Off
//
// It is PINNED to the ground tile of the backdrop it was spawned over, so it scrolls with the ground and never moves
// against it. Purely visual: no collision, nothing queries it. Nothing allocates after Build().
public sealed class CoverBoulder
{
    public enum Phase { Off, Live, Fade }

    public const float FadeSeconds = 1.8f;   // it sinks out slowly
    // EMERGE / RECEDE (docs 13): it comes out of the backdrop, hazy and see-through, and fades in (smooth in opacity: no pop);
    // on the way out it sinks back over the last RecedeUnits above the view bottom.
    public const float EmergeSeconds = 2.6f, RecedeUnits = 1.8f;
    public const int MaxCircles = 4;   // (the art's bounding circles: they only size the boulder now)
    // Drawn ON the backdrop: just above the world's ground plane (BackdropCatalog.Spec.BoulderOrder), so every other backdrop
    // layer (landmarks, docks, flow, mist, clouds, weather), enemies, elites, bosses, shots, pickups, the ship and FX sort above it.
    public static int OrderFor(string world) { return BackdropCatalog.For(world).BoulderOrder; }

    public int SortingOrder { get; private set; }
    public Phase State { get; private set; }
    public int World { get; private set; }
    public int Variant { get; private set; }
    public Vector2 Pos;                    // centre of the drawing (u)
    public float Width { get; private set; }
    public float Bound { get; private set; }   // radius round the centre that holds the whole drawing
    public float Age { get; private set; }
    public bool Alive => State != Phase.Off;
    public CoverBoulderEntry Entry => entry;
    public float PhaseTime => t;
    public Transform StormFxRoot => stormFx != null ? stormFx.Root : null;

    CoverBoulderEntry entry;
    CoverVariant art;
    Color bodyTint = Color.white;
    float presence = 1f, viewBottom = -100f;
    float scale, t;
    GameObject go;
    SpriteRenderer body, wash;
    CoverStormFx stormFx;   // the Storm landform's effects (built on first need, then pooled with the boulder)
    // The landform is PINNED to the ground tile of the backdrop it was spawned over (groundTile / groundGy:
    // y = gy - the tile's unwrapped travel, the same maths GroundPlanner uses for the pieces standing on that tile),
    // so it scrolls with the ground at exactly the ground's rate. Without a live backdrop (tests, previews) it falls
    // at the ground's nominal rate instead.
    BackdropTile groundTile;
    double groundGy;

    public void Build(Transform root, string name)
    {
        go = new GameObject(name);
        go.transform.SetParent(root, false);
        body = NewRenderer("Body", 0);
        wash = NewRenderer("Wash", 1);
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

    public void Spawn(int world, int variant, CoverVariant v, float widthU, Vector2 at, float rng01, Color tint)
    {
        bodyTint = tint;
        viewBottom = -100f;
        entry = CoverBoulderTable.For(world);
        SortingOrder = OrderFor(entry.world);
        body.sortingOrder = SortingOrder;
        wash.sortingOrder = SortingOrder + 1;
        World = world;
        art = v;
        Variant = variant;
        Width = widthU;
        scale = v.WorldScale(widthU);
        Pos = at;
        Age = t = 0f;
        State = Phase.Live;
        groundTile = CoverBoulders.GroundTile(entry.world);
        groundGy = Pos.y + (groundTile != null ? groundTile.travel : 0.0);
        float b = 0f;
        for (int i = 0; i < v.circles.Length && i < MaxCircles; i++)
            b = Mathf.Max(b, new Vector2(v.circles[i].x, v.circles[i].y).magnitude + v.circles[i].r);
        Bound = b / CoverBoulderArt.Ppu * scale;
        if (entry.stormFx)
        {
            if (stormFx == null) stormFx = new CoverStormFx(go.transform);
            stormFx.Begin(v, entry.folder, rng01, SortingOrder);
        }
        else if (stormFx != null) stormFx.Hide();
        go.SetActive(true);
        body.sprite = v.intact;
        body.enabled = true;
        body.color = new Color(bodyTint.r, bodyTint.g, bodyTint.b, 0f);
        wash.enabled = false;
        Place();
    }

    public void Release()
    {
        State = Phase.Off;
        groundTile = null;
        if (stormFx != null) stormFx.Hide();
        if (go != null) go.SetActive(false);
        if (body != null) body.enabled = false;
    }

    // The quiet exit (a world change, a death, a boss, a scene): it fades.
    public void Dismiss()
    {
        if (State == Phase.Live) { State = Phase.Fade; t = 0f; }
    }

    // A test / preview puts it exactly here and holds it still.
    public void Hold(Vector2 at, bool settled = true)
    {
        if (settled) Age = Mathf.Max(Age, EmergeSeconds);
        Pos = at;
        if (groundTile != null) groundGy = Pos.y + groundTile.travel;
        Place();
    }

    // ---- step ----

    // Emerge x recede: 0 (hidden in the backdrop) .. 1 (fully in the play layer). Smooth, so nothing pops.
    public float Presence => presence;
    public SpriteRenderer GetComponentOnBody() { return body; }
    public static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
    float Recede() { return viewBottom < -99f ? 1f : Smooth((Pos.y - viewBottom) / (Bound + RecedeUnits)); }

    public void Step(float dt, CoverBoulders.StepContext ctx)
    {
        if (State == Phase.Off || dt <= 0f) return;
        viewBottom = ctx.viewBottom;
        Age += dt;
        t += dt;
        // scenery: the ground carries it, nothing else moves it
        if (groundTile != null) Pos.y = (float)(groundGy - groundTile.travel);
        else Pos.y -= CoverBoulders.GroundRate(entry.world) * dt;
        if (State == Phase.Live && Pos.y + Bound + .6f < ctx.viewBottom) { Release(); return; }
        if (State == Phase.Fade && t >= FadeSeconds) { Release(); return; }
        Place();
    }

    // Called by WorldBackdrop after it has scrolled its tiles this frame (CoverBoulders.AnchorToBackdrop), so the landform
    // is drawn on the very travel its ground tile is drawn at, whichever of the two updates first.
    public void Anchor()
    {
        if (State == Phase.Off || groundTile == null) return;
        Pos.y = (float)(groundGy - groundTile.travel);
        Place();
    }

    void Place()
    {
        float emerge = Smooth(Age / EmergeSeconds);
        presence = emerge * Mathf.Lerp(.25f, 1f, Recede());

        var tr = go.transform;
        Vector2 origin = CoverBoulders.AnchorOrigin;   // the landform rides the backdrop, which rides the shaken camera
        tr.position = new Vector3(Pos.x + origin.x, Pos.y + origin.y, 0f);
        tr.rotation = Quaternion.identity;
        tr.localScale = new Vector3(scale, scale, 1f);

        if (stormFx != null && stormFx.Active) stormFx.Step(Age, FxAlpha());

        float a = State == Phase.Fade ? 1f - Smooth(t / FadeSeconds) : 1f;
        body.enabled = true; body.sprite = art.intact;
        body.color = new Color(bodyTint.r, bodyTint.g, bodyTint.b, presence * a);
        // a steady depth haze over the silhouette, on top of the emerge fog
        wash.enabled = State == Phase.Live; wash.sprite = art.intact;
        var hz = entry.haze; hz.a = (1f - presence) * presence + CoverBoulders.DecorHaze * presence; wash.color = hz;
    }

    float FxAlpha()
    {
        switch (State)
        {
            case Phase.Live: return presence;
            case Phase.Fade: return presence * (1f - Smooth(t / FadeSeconds));
            default: return 0f;
        }
    }
}
