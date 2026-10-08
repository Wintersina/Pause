using UnityEngine;

// One planet's planetfall art, loaded when its planet appears and released
// when the descent is over (Planetfall). The textures are plain imports
// (PlanetfallArtImporter); the sprites are cut here from PlanetfallDef's
// pixel numbers, every one ONE world unit wide (pixels per unit = its width
// in pixels), so a renderer's scale is simply its width in world units.
//
// Also the few procedural textures the sequence draws with (a soft ring for
// the atmosphere glow, the reticle, the vignette, plain white), built once.
public class PlanetfallArt
{
    public readonly PlanetfallDef Def;
    public Texture2D PlanetTex, LimbTex, DeckTex, DeckDarkTex, EntryTex, BurstTex, StreaksTex;
    public Sprite Planet, Limb, Deck, DeckDark, Streaks;
    public Sprite[] Entry, Burst;
    public Sprite White, Ring, Reticle, Vignette;
    // Where the ring and reticle textures draw their circle, as a share of
    // the sprite's half width.
    public const float RingRadius = .78f, ReticleRadius = .9f;

    // Every file loaded at the expected layout.
    public bool Complete { get; private set; }

    Texture2D whiteTex, ringTex, reticleTex, vignetteTex;

    PlanetfallArt(PlanetfallDef def) { Def = def; }

    public static PlanetfallArt Load(PlanetfallDef def)
    {
        var a = new PlanetfallArt(def);
        a.LoadAll();
        return a;
    }

    void LoadAll()
    {
        string f = Def.folder;
        PlanetTex = Resources.Load<Texture2D>(f + Def.planet);
        LimbTex = Resources.Load<Texture2D>(f + Def.limb);
        DeckTex = Resources.Load<Texture2D>(f + Def.deck);
        DeckDarkTex = Resources.Load<Texture2D>(f + Def.deckDark);
        EntryTex = Resources.Load<Texture2D>(f + Def.entryFx);
        BurstTex = Resources.Load<Texture2D>(f + Def.burst);
        StreaksTex = Resources.Load<Texture2D>(f + Def.streaks);
        Complete = PlanetTex != null && LimbTex != null && DeckTex != null && DeckDarkTex != null &&
                   EntryTex != null && BurstTex != null && StreaksTex != null &&
                   Def.entryFrames > 0 && Def.burstFrames > 0;
        if (!Complete) return;

        Planet = Whole(PlanetTex, new Vector2(Def.planetCentrePx.x / PlanetTex.width, 1f - Def.planetCentrePx.y / PlanetTex.height));
        Limb = Whole(LimbTex, new Vector2(.5f, 1f - Def.limbApexPx / LimbTex.height));
        Deck = Whole(DeckTex, new Vector2(.5f, .5f));
        DeckDark = Whole(DeckDarkTex, new Vector2(.5f, .5f));
        Streaks = Whole(StreaksTex, new Vector2(.5f, .5f));
        Entry = Cells(EntryTex, Def.entryFrames, new Vector2(Def.entryShipPx.x, Def.entryShipPx.y), Def.entryHoleX);
        Burst = Cells(BurstTex, Def.burstFrames, new Vector2(-1f, -1f));

        whiteTex = Procedural(4, 4, (x, y) => 1f);
        White = Whole(whiteTex, new Vector2(.5f, .5f));
        // A soft glow ring (the atmosphere's pull) with a faint fill inside.
        ringTex = Procedural(256, 256, (x, y) =>
        {
            float d = Mathf.Sqrt(x * x + y * y);
            float ring = Mathf.Exp(-Mathf.Pow((d - RingRadius) / .075f, 2f));
            float fill = d < RingRadius ? .10f * Mathf.SmoothStep(0f, 1f, d / RingRadius) : 0f;
            return Mathf.Clamp01(ring + fill);
        });
        Ring = Whole(ringTex, new Vector2(.5f, .5f));
        // The landing reticle: a thin ring broken at the diagonals, with four
        // ticks pointing in.
        reticleTex = Procedural(256, 256, (x, y) =>
        {
            float d = Mathf.Sqrt(x * x + y * y);
            float deg = Mathf.Repeat(Mathf.Atan2(y, x) * Mathf.Rad2Deg, 90f);
            bool gap = deg > 33f && deg < 57f;
            float line = Mathf.Clamp01(1.4f - Mathf.Abs(d - ReticleRadius) / .03f);
            float glow = .35f * Mathf.Exp(-Mathf.Pow((d - ReticleRadius) / .07f, 2f));
            float ring = gap ? 0f : Mathf.Max(line, glow);
            bool tickArm = Mathf.Abs(x) < .035f || Mathf.Abs(y) < .035f;
            float tick = tickArm && d > ReticleRadius - .24f && d < ReticleRadius - .05f ? 1f : 0f;
            return Mathf.Max(ring, tick);
        });
        Reticle = Whole(reticleTex, new Vector2(.5f, .5f));
        // Dark at the edges, clear in the middle (portrait: x and y are both
        // shares of their own half-size).
        vignetteTex = Procedural(64, 128, (x, y) =>
        {
            float d = Mathf.Sqrt(x * x * .8f + y * y);
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.55f, 1.15f, d));
        });
        Vignette = Whole(vignetteTex, new Vector2(.5f, .5f));
    }

    static Sprite Whole(Texture2D tex, Vector2 pivot)
    {
        var s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, tex.width, 0, SpriteMeshType.FullRect);
        s.name = tex.name;
        return s;
    }

    // The width of one entryFx cell, in pixels (its sprites are 1 unit wide).
    public float EntryCellPx { get { return EntryTex != null && Def.entryFrames > 0 ? EntryTex.width / (float)Def.entryFrames : 0f; } }

    // One row of `count` cells. `shipPx` is the pivot in a cell's own pixels
    // (y down); negative: the cell's centre. `cellX`, if given, overrides
    // the pivot's x per cell (the shroud's opening wanders; the ship doesn't).
    static Sprite[] Cells(Texture2D tex, int count, Vector2 shipPx, float[] cellX = null)
    {
        var cells = new Sprite[count];
        float w = tex.width / (float)count, h = tex.height;
        for (int i = 0; i < count; i++)
        {
            float px = cellX != null && i < cellX.Length ? cellX[i] : shipPx.x;
            Vector2 pivot = shipPx.x < 0f ? new Vector2(.5f, .5f) : new Vector2(px / w, 1f - shipPx.y / h);
            cells[i] = Sprite.Create(tex, new Rect(i * w, 0, w, h), pivot, w, 0, SpriteMeshType.FullRect);
            cells[i].name = tex.name + "_" + i;
        }
        return cells;
    }

    // A white texture whose alpha is `alpha(x, y)`, x and y from -1 to 1.
    static Texture2D Procedural(int w, int h, System.Func<float, float, float> alpha)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "~planetfall", wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float a = alpha((x + .5f) / w * 2f - 1f, (y + .5f) / h * 2f - 1f);
            px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    // Everything back: the generated textures destroyed, the imported ones
    // unloaded (they are only needed for one descent).
    public void Release()
    {
        Kill(Planet); Kill(Limb); Kill(Deck); Kill(DeckDark); Kill(Streaks);
        Kill(White); Kill(Ring); Kill(Reticle); Kill(Vignette);
        if (Entry != null) foreach (var s in Entry) Kill(s);
        if (Burst != null) foreach (var s in Burst) Kill(s);
        Kill(whiteTex); Kill(ringTex); Kill(reticleTex); Kill(vignetteTex);
        foreach (var t in new[] { PlanetTex, LimbTex, DeckTex, DeckDarkTex, EntryTex, BurstTex, StreaksTex })
            if (t != null) Resources.UnloadAsset(t);
        Entry = Burst = null;
        Planet = Limb = Deck = DeckDark = Streaks = White = Ring = Reticle = Vignette = null;
        PlanetTex = LimbTex = DeckTex = DeckDarkTex = EntryTex = BurstTex = StreaksTex = null;
        Complete = false;
    }

    static void Kill(Object o) { if (o != null) BossUtil.Kill(o); }
}
