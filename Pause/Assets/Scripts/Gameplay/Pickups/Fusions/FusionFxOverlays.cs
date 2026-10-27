using UnityEngine;

// The loops' extra pooled sprites (FusionFx, partial): thin, low-alpha dots, bars and rings built once from three
// tiny generated textures (no new art), drawn just behind the hull in the fusions' own palette. Everything is driven
// from the live rules state (AtomFusions.*Live); each effect eases in / out over FusionTuning.LoopFadeSeconds, the
// world clock freezes it in a pause, and fusions going off (death, tutorial, kill cam) clear it at once.
//
//   DUST STORM  violet dust streaming in toward the ship while the magnet pulls
//   BLOOM       green petals orbiting the shield; lit petals = progress to the next heart; pops on a heal
//   STASIS      a thin frozen-clock ring and one pip per free pause; a pip goes dark per use
//   LIFELINE    a soft band along the top edge (the extended reach) and motes drifting up
//   NOVA        a one-off ring from the ship out to exactly NovaClearRadius when the shield lets go
public sealed partial class FusionFx
{
    const int DotsPer = 10;
    const int EffDust = 0, EffBloom = 1, EffStasis = 2, EffLifeline = 3, Effects = 4;
    public const float NovaRingSeconds = .45f;       // the whole ring: grows, then fades
    public const float NovaGrowShare = .75f;         // it reaches the full radius at this share of it
    public const float NovaRingAlpha = .6f;
    const float RingArtRadius = 1f;                  // the ring sprite's radius in world units at scale 1

    static Sprite dotSprite, ringSprite, barSprite;
    SpriteRenderer[][] dots;
    SpriteRenderer stasisRing, novaRing, band;
    readonly float[] level = new float[Effects];
    readonly float[] clockT = new float[Effects];
    float popT = 1f, novaAge = -1f;
    int bloomSeen;
    Vector3 novaAt;
    public static int NovaRings { get; private set; }
    public float LevelOf(int eff) { return level[eff]; }
    public SpriteRenderer NovaRingRenderer => novaRing;
    public float NovaRingRadius => novaAge >= 0f ? novaRing.transform.localScale.x * RingArtRadius : 0f;
    public bool NovaRingShowing => novaRing != null && novaRing.enabled;
    public int DotsShowing(int eff)
    {
        int n = 0;
        if (dots != null) for (int i = 0; i < DotsPer; i++) if (dots[eff][i].enabled) n++;
        return n;
    }
    public float DotAlpha(int eff, int i) { return dots != null && dots[eff][i].enabled ? dots[eff][i].color.a : 0f; }
    public bool BandShowing => band != null && band.enabled;

    static Sprite MakeSprite(string name, int size, System.Func<float, float, float> alphaAt, float ppu, FilterMode mode)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name, filterMode = mode, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + .5f) / size * 2f - 1f, v = (y + .5f) / size * 2f - 1f;
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alphaAt(u, v)) * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(.5f, .5f), ppu, 0, SpriteMeshType.FullRect);
    }

    static void BuildSprites()
    {
        if (dotSprite != null && dotSprite.texture != null) return;
        // a soft round dot, 1 u across at scale 1
        dotSprite = MakeSprite("~FusionDot", 16, (u, v) => 1f - Mathf.Clamp01((Mathf.Sqrt(u * u + v * v) - .55f) / .45f), 16f, FilterMode.Bilinear);
        // a thin circle (about 2 px of 128), radius 1 u at scale 1
        ringSprite = MakeSprite("~FusionRing", 256, (u, v) => 1f - Mathf.Clamp01(Mathf.Abs(Mathf.Sqrt(u * u + v * v) - .985f) / .012f), 128f, FilterMode.Bilinear);
        // a band: bright core fading to nothing at its top and bottom edges, 1 u square at scale 1
        barSprite = MakeSprite("~FusionBar", 16, (u, v) => 1f - Mathf.Abs(v), 16f, FilterMode.Bilinear);
    }

    SpriteRenderer NewOverlay(string name, Sprite sprite, SpriteRenderer hull)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var r = go.AddComponent<SpriteRenderer>();
        r.sprite = sprite;
        r.enabled = false;
        if (hull != null) { r.sortingLayerID = hull.sortingLayerID; r.sortingOrder = hull.sortingOrder - 1; }
        return r;
    }

    void MakeOverlays(SpriteRenderer hull)
    {
        BuildSprites();
        dots = new SpriteRenderer[Effects][];
        for (int e = 0; e < Effects; e++)
        {
            dots[e] = new SpriteRenderer[DotsPer];
            for (int i = 0; i < DotsPer; i++) dots[e][i] = NewOverlay("~Dot" + e + "_" + i, dotSprite, hull);
        }
        stasisRing = NewOverlay("~StasisRing", ringSprite, hull);
        novaRing = NewOverlay("~NovaRing", ringSprite, hull);
        band = NewOverlay("~LifelineBand", barSprite, hull);
    }

    static void Show(SpriteRenderer r, Vector3 at, float size, Color c, float a)
    {
        if (a <= .01f) { if (r.enabled) r.enabled = false; return; }
        r.transform.position = at;
        r.transform.localScale = new Vector3(size, size, 1f);
        c.a = Mathf.Min(a, .7f);
        r.color = c;
        r.enabled = true;
    }

    static bool EffLive(int e)
    {
        switch (e)
        {
            case EffDust: return AtomFusions.DustStormLive;
            case EffBloom: return AtomFusions.BloomLive;
            case EffStasis: return AtomFusions.StasisLive;
            default: return AtomFusions.LifelineLive;
        }
    }

    // A Nova clear fired at `at` (AtomFusions.OnShieldEnding): the ring grows to exactly the clear radius.
    public static void NovaRing(Vector3 at)
    {
        var fx = instance;
        if (fx == null || fx.novaRing == null) return;
        fx.novaAge = 0f;
        fx.novaAt = at;
        NovaRings++;
        fx.DrawOverlays();
    }

    // The world's clock: ease each effect in / out, advance its own time, then draw.
    void StepOverlays(float dt)
    {
        if (dots == null) return;
        for (int e = 0; e < Effects; e++)
        {
            bool live = EffLive(e);
            level[e] = Mathf.Clamp01(level[e] + (live ? dt : -dt) / FusionTuning.LoopFadeSeconds);
            if (level[e] > 0f) clockT[e] += dt;
            else clockT[e] = 0f;
        }
        if (popT < 1f) popT += dt / FusionTuning.HealFlashSeconds;
        int heals = AtomFusions.BloomHeals;
        if (heals > bloomSeen) popT = 0f;
        bloomSeen = heals;
        if (novaAge >= 0f) { novaAge += dt; if (novaAge >= NovaRingSeconds) { novaAge = -1f; novaRing.enabled = false; } }
        DrawOverlays();
    }

    void ResetOverlays()
    {
        if (dots == null) return;
        for (int e = 0; e < Effects; e++)
        {
            level[e] = 0f; clockT[e] = 0f;
            for (int i = 0; i < DotsPer; i++) dots[e][i].enabled = false;
        }
        stasisRing.enabled = false;
        novaRing.enabled = false;
        band.enabled = false;
        novaAge = -1f;
        popT = 1f;
        bloomSeen = AtomFusions.BloomHeals;
    }

    static float Frac(float x) { return x - Mathf.Floor(x); }

    void DrawOverlays()
    {
        if (dots == null || follow == null) return;
        Vector3 s = follow.position;

        // DUST STORM: violet dust spiralling in from the edge of the pull to the hull
        float L = level[EffDust];
        for (int i = 0; i < DotsPer; i++)
        {
            if (L <= 0f) { dots[EffDust][i].enabled = false; continue; }
            float ph = Frac(clockT[EffDust] * .8f + i / (float)DotsPer);
            float ang = i * 2.399f + ph * 1.2f, rad = Mathf.Lerp(3.4f, .9f, ph);
            Show(dots[EffDust][i], s + new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * rad, .13f, AkiraPalette.VioletHi, L * .7f * Mathf.Sin(ph * Mathf.PI));
        }

        // BLOOM: six petals round the shield; the lit ones are the kills banked toward the next heart
        L = level[EffBloom];
        float pop = popT < 1f ? 1f - popT : 0f;
        int lit = AtomFusions.BloomKills * 2;
        int petals = Mathf.Min(DotsPer, 6);
        for (int i = 0; i < DotsPer; i++)
        {
            if (L <= 0f || i >= petals) { dots[EffBloom][i].enabled = false; continue; }
            float ang = clockT[EffBloom] * 1.4f + i * (Mathf.PI * 2f / petals), rad = 1.15f + pop * .6f;
            bool on = i < lit || pop > 0f;
            Show(dots[EffBloom][i], s + new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * rad, .16f + pop * .12f, PickupGlow.HealGreen, L * (on ? .7f : .22f));
        }

        // STASIS: a thin frozen-clock ring and a pip per free pause above the hull
        L = level[EffStasis];
        Show(stasisRing, s, 1.5f / RingArtRadius, AkiraPalette.Cyan, L * .28f);
        int free = AtomFusions.StasisFreeLeft;
        for (int i = 0; i < DotsPer; i++)
        {
            if (L <= 0f || i >= FusionTuning.StasisFreePauses) { dots[EffStasis][i].enabled = false; continue; }
            float x = (i - (FusionTuning.StasisFreePauses - 1) * .5f) * .4f;
            Show(dots[EffStasis][i], s + new Vector3(x, 1.75f, 0f), .17f, AkiraPalette.Cyan, L * (i < free ? .7f : .1f));
        }

        // LIFELINE: a soft band along the top edge (the reach above the view) and motes drifting up toward it
        L = level[EffLifeline];
        Rect view = ShipTargets.View();
        if (L > 0f && view.width < 1e4f)
        {
            Show(band, new Vector3(view.center.x, view.yMax - .1f, s.z), 1f, Color.Lerp(PickupGlow.HealGreen, AkiraPalette.Cyan, .5f), L * .22f);
            if (band.enabled) band.transform.localScale = new Vector3(view.width, .6f, 1f);
        }
        else if (band.enabled) band.enabled = false;
        for (int i = 0; i < DotsPer; i++)
        {
            if (L <= 0f || view.width > 1e4f) { dots[EffLifeline][i].enabled = false; continue; }
            float ph = Frac(clockT[EffLifeline] * .35f + i * .137f);
            float x = view.xMin + Frac(i * .618f + .1f) * view.width;
            Show(dots[EffLifeline][i], new Vector3(x, Mathf.Lerp(s.y, view.yMax, ph), s.z), .12f, PickupGlow.HealGreen, L * .6f * Mathf.Sin(ph * Mathf.PI));
        }

        // NOVA: the clear ring, out to exactly the clear radius at NovaGrowShare of its life, then fading
        if (novaAge >= 0f)
        {
            float k = Mathf.Clamp01(novaAge / (NovaRingSeconds * NovaGrowShare));
            float r = FusionTuning.NovaClearRadius * (1f - (1f - k) * (1f - k));
            float a = NovaRingAlpha * (novaAge < NovaRingSeconds * NovaGrowShare ? 1f : 1f - (novaAge - NovaRingSeconds * NovaGrowShare) / (NovaRingSeconds * (1f - NovaGrowShare)));
            Show(novaRing, novaAt, r / RingArtRadius, AkiraPalette.Cyan, a);
        }
    }
}
