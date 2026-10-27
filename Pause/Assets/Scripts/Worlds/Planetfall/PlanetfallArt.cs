using UnityEngine;

// One planet's planetfall art, loaded when its planet appears and released
// when the descent is over (Planetfall). The textures are plain imports
// (PlanetfallArtImporter); the sprites are cut here from PlanetfallDef's
// pixel numbers, every one ONE world unit wide (pixels per unit = its width
// in pixels), so a renderer's scale is simply its width in world units.
//
// Also the few procedural textures the sequence draws with (a soft ring for
// the atmosphere glow, the reticle, the vignette, the plume, plain white):
// built ONCE per process (PlanetfallProc), not per descent. They used to
// be drawn per pixel through a delegate in the frame a planetfall was
// created; now WorldArtPreload spreads the drawing over the frames of the
// portal window (PlanetfallProc.Step, a fixed pixel budget per call), and a descent
// that finds them missing finishes the job synchronously (PlanetfallProc.Ensure).
public class PlanetfallArt
{
    public readonly PlanetfallDef Def;
    public Texture2D PlanetTex, LimbTex, DeckTex, DeckDarkTex, EntryTex, BurstTex, StreaksTex;
    public Sprite Planet, Limb, Deck, DeckDark, Streaks;
    public Sprite[] Entry, Burst;
    public Sprite White, Ring, Reticle, Vignette, Plume;
    // Where the ring and reticle textures draw their circle, as a share of
    // the sprite's half width.
    public const float RingRadius = .78f, ReticleRadius = .9f;

    // Every file loaded at the expected layout.
    public bool Complete { get; private set; }

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
        PlanetTex = WorldArtPreload.Load<Texture2D>(f + Def.planet);
        LimbTex = WorldArtPreload.Load<Texture2D>(f + Def.limb);
        DeckTex = WorldArtPreload.Load<Texture2D>(f + Def.deck);
        DeckDarkTex = WorldArtPreload.Load<Texture2D>(f + Def.deckDark);
        EntryTex = WorldArtPreload.Load<Texture2D>(f + Def.entryFx);
        BurstTex = WorldArtPreload.Load<Texture2D>(f + Def.burst);
        StreaksTex = WorldArtPreload.Load<Texture2D>(f + Def.streaks);
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

        PlanetfallProc.Ensure();
        White = PlanetfallProc.White; Ring = PlanetfallProc.Ring; Reticle = PlanetfallProc.Reticle; Vignette = PlanetfallProc.Vignette; Plume = PlanetfallProc.Plume;
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

    // Everything back: the generated textures destroyed, the imported ones
    // unloaded (they are only needed for one descent).
    public void Release()
    {
        Kill(Planet); Kill(Limb); Kill(Deck); Kill(DeckDark); Kill(Streaks);
        if (Entry != null) foreach (var s in Entry) Kill(s);
        if (Burst != null) foreach (var s in Burst) Kill(s);
        foreach (var t in new[] { PlanetTex, LimbTex, DeckTex, DeckDarkTex, EntryTex, BurstTex, StreaksTex })
            if (t != null) Resources.UnloadAsset(t);
        Entry = Burst = null;
        Planet = Limb = Deck = DeckDark = Streaks = White = Ring = Reticle = Vignette = Plume = null;
        PlanetTex = LimbTex = DeckTex = DeckDarkTex = EntryTex = BurstTex = StreaksTex = null;
        Complete = false;
    }

    static void Kill(Object o) { if (o != null) BossUtil.Kill(o); }
}

// The shared procedural textures and their sprites. Same pixels as the old
// per-descent delegates (the formulas are unchanged), drawn straight into a
// Color32[] with no delegate and no allocation per pixel. Never released: they
// are ~190 KB in all and every planetfall / lift-off reuses them.
public static class PlanetfallProc
{
    // Pixels drawn per Step call (about one 128x128 block): a few hundred
    // microseconds of math, so a preload frame never notices it.
    public const int PixelsPerStep = 16384;

    enum Kind { White, Ring, Reticle, Vignette, Plume }
    static readonly int[] W = { 4, 256, 256, 64, 64 };
    static readonly int[] H = { 4, 256, 256, 128, 128 };

    static readonly Color32[][] pixels = new Color32[5][];
    static readonly int[] rowsDone = new int[5];
    static readonly Texture2D[] tex = new Texture2D[5];
    static readonly Sprite[] sprite = new Sprite[5];

    // Tests read the pixels back; the real game uploads and frees the CPU copy.
    public static bool KeepReadable;

    // Tests: how many textures were created since the last ResetCounters.
    public static int Builds { get; private set; }
    public static void ResetCounters() { Builds = 0; }

    public static bool Ready
    {
        get
        {
            for (int i = 0; i < 5; i++) if (sprite[i] == null || sprite[i].texture == null) return false;
            return true;
        }
    }
    public static Sprite White { get { return sprite[0]; } }
    public static Sprite Ring { get { return sprite[1]; } }
    public static Sprite Reticle { get { return sprite[2]; } }
    public static Sprite Vignette { get { return sprite[3]; } }
    public static Sprite Plume { get { return sprite[4]; } }

    // Forgets everything (tests; a destroyed texture is also noticed by Step).
    public static void Reset()
    {
        for (int i = 0; i < 5; i++)
        {
            if (sprite[i] != null) Object.DestroyImmediate(sprite[i]);
            if (tex[i] != null) Object.DestroyImmediate(tex[i]);
            sprite[i] = null; tex[i] = null; pixels[i] = null; rowsDone[i] = 0;
        }
    }

    // Draws up to PixelsPerStep more pixels (finishing and uploading a texture
    // when its last row is done). True while something is left to do.
    public static bool Step()
    {
        int budget = PixelsPerStep;
        for (int i = 0; i < 5 && budget > 0; i++)
        {
            if (sprite[i] != null && sprite[i].texture != null) continue;   // built (a destroyed one rebuilds)
            if (sprite[i] != null) { sprite[i] = null; pixels[i] = null; rowsDone[i] = 0; }
            int w = W[i], h = H[i];
            if (pixels[i] == null) pixels[i] = new Color32[w * h];
            var px = pixels[i];
            while (rowsDone[i] < h && budget > 0)
            {
                Row((Kind)i, px, w, h, rowsDone[i]);
                rowsDone[i]++;
                budget -= w;
            }
            if (rowsDone[i] < h) continue;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "~planetfall", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            t.SetPixels32(px);
            t.Apply(false, !KeepReadable);
            var pivot = (Kind)i == Kind.Plume ? new Vector2(.5f, 1f) : new Vector2(.5f, .5f);
            var s = Sprite.Create(t, new Rect(0, 0, w, h), pivot, w, 0, SpriteMeshType.FullRect);
            s.name = t.name;
            s.hideFlags = HideFlags.HideAndDontSave;
            tex[i] = t; sprite[i] = s; pixels[i] = null;
            Builds++;
        }
        return !Ready;
    }

    // Finishes whatever is missing now.
    public static void Ensure()
    {
        while (Step()) { }
    }

    static void Row(Kind k, Color32[] px, int w, int h, int y)
    {
        float fy = (y + .5f) / h * 2f - 1f;
        for (int x = 0; x < w; x++)
        {
            float fx = (x + .5f) / w * 2f - 1f;
            float a = Alpha(k, fx, fy);
            px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
        }
    }

    // x and y from -1 to 1.
    static float Alpha(Kind k, float x, float y)
    {
        switch (k)
        {
            case Kind.Ring:
            {
                // A soft glow ring (the atmosphere's pull) with a faint fill inside.
                float d = Mathf.Sqrt(x * x + y * y);
                float ring = Mathf.Exp(-Mathf.Pow((d - PlanetfallArt.RingRadius) / .075f, 2f));
                float fill = d < PlanetfallArt.RingRadius ? .10f * Mathf.SmoothStep(0f, 1f, d / PlanetfallArt.RingRadius) : 0f;
                return Mathf.Clamp01(ring + fill);
            }
            case Kind.Reticle:
            {
                // The landing reticle: a thin ring broken at the diagonals, with four ticks pointing in.
                float R = PlanetfallArt.ReticleRadius;
                float d = Mathf.Sqrt(x * x + y * y);
                float deg = Mathf.Repeat(Mathf.Atan2(y, x) * Mathf.Rad2Deg, 90f);
                bool gap = deg > 33f && deg < 57f;
                float line = Mathf.Clamp01(1.4f - Mathf.Abs(d - R) / .03f);
                float glow = .35f * Mathf.Exp(-Mathf.Pow((d - R) / .07f, 2f));
                float ring = gap ? 0f : Mathf.Max(line, glow);
                bool tickArm = Mathf.Abs(x) < .035f || Mathf.Abs(y) < .035f;
                float tick = tickArm && d > R - .24f && d < R - .05f ? 1f : 0f;
                return Mathf.Max(ring, tick);
            }
            case Kind.Vignette:
            {
                // Dark at the edges, clear in the middle (portrait: x and y are both shares of their own half-size).
                float d = Mathf.Sqrt(x * x * .8f + y * y);
                return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.55f, 1.15f, d));
            }
            case Kind.Plume:
            {
                // A thruster plume for the lift-off: hot and wide at the nozzles (the top, its pivot), narrowing and fading downward.
                float along = (1f - y) * .5f;                     // 0 at the nozzles, 1 at the tip
                float half = Mathf.Lerp(.95f, .12f, Mathf.Pow(along, .7f));
                float across = Mathf.Exp(-3f * (x / half) * (x / half));
                return across * (1f - PlanetfallTimeline.Ramp(along, .15f, 1f)) * PlanetfallTimeline.Ramp(along, 0f, .06f);
            }
            default: return 1f;
        }
    }
}
