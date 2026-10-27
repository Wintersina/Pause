using UnityEngine;

// THE SPACE SUN (user-approved art, docs/world-weather-hazards.md section 12): a huge, distant star at the edge of the
// Space backdrop. PURELY a backdrop piece: no collider, no gameplay effect, never over the middle of the lane.
//
//   * Two palettes (WARM yellow / orange, VIOLET / magenta). One is picked per RUN from the run's seed
//     (SpaceSunPick) and never changes mid-run; the menu backdrop shows the same one.
//   * It turns very slowly and smoothly (SpaceSunTuning.TurnSeconds per turn, on the director's scaled clock, so a
//     paused game holds it), its corona breathes, and its 8-frame flare flipbook (6 fps) loops over it; every
//     ArcEvery seconds a BIGGER prominence arc (the same flipbook, larger and out of phase) swells and fades.
//   * Sheets: sun_<p> (the drawn disc cell, cropped to its visible box: SpaceSunTuning.DiscCrop), sun_<p>_corona (2 variants,
//     1024 cells, additive), flares_<p> (4 x 2 cells of 1024, additive). Only the chosen palette is loaded and it is unloaded with the set.
//     Mobile: the disc stays RGBA32 (hard pixel art), the corona and the flare flipbook are ASTC 4x4 (soft additive layers).
//     The disc sheet was approved as 2048 x 1024 (two 1024 cells, variant 0 warm / 1 violet the one drawn) and only ~78 % of the
//     drawn cell held pixels, so the wired file is that cell's alpha box (scripts/crop_sun_disc.py: byte-identical pixels, every
//     dropped pixel clear) and the sprite's pivot puts the cell's centre back under the sun's root: 8.4 MB -> 2.6 / 3.5 MB RGBA32.
//   * Cheap: 4 renderers built once, 8 + 2 sprites made once, no per-frame allocation.
public static class SpaceSunPick
{
    public const string Warm = "warm", Violet = "violet";
    static int runSeed;
    static bool have;

    // The run's seed: set at a run's start (WorldManager.ResetProgress), random once if nobody did.
    public static int RunSeed
    {
        get { if (!have) { runSeed = new System.Random().Next(); have = true; } return runSeed; }
    }
    public static void NewRun(int seed) { runSeed = seed; have = true; }
    public static void NewRun() { NewRun(new System.Random().Next()); }

    // Which palette a seed picks (a hash, so consecutive seeds do not alternate).
    public static string PaletteFor(int seed)
    {
        unchecked { uint h = (uint)seed * 2654435761u; h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; return (h & 1) == 0 ? Warm : Violet; }
    }
    // Which side of the screen it sits on.
    public static float SideFor(int seed)
    {
        unchecked { uint h = (uint)seed * 3266489917u; h ^= h >> 16; h *= 668265263u; h ^= h >> 15; return (h & 1) == 0 ? -1f : 1f; }
    }
    // Tests and previews: PAUSE_SUN_FORCE=warm|violet[:left|right] pins the pick (the readability suites run each combination).
    static string forced; static float forcedSide; static bool forceRead;
    static void ReadForce()
    {
        if (forceRead) return;
        forceRead = true;
        string v = System.Environment.GetEnvironmentVariable("PAUSE_SUN_FORCE");
        if (string.IsNullOrEmpty(v)) return;
        var parts = v.Split(':');
        forced = parts[0] == Warm || parts[0] == Violet ? parts[0] : null;
        if (parts.Length > 1) forcedSide = parts[1] == "left" ? -1f : parts[1] == "right" ? 1f : 0f;
    }
    // The solar flares are born at the sun: +1 when they travel left to right (the sun is on the LEFT), -1 right to left. The sun is
    // one per-run pick shared by all four Space backdrop variants (it is a backdrop piece on the deep-planet layer of every one), so
    // there is no per-variant side. Null when its art is not installed (no sun to see): the caller picks a side itself.
    public static float? FlareTravel
    {
        get
        {
            if (Resources.Load<Texture2D>(BackdropCatalog.Folder("Space") + "sun_" + Current) == null) return null;
            return -Side;
        }
    }
    public static string Current { get { ReadForce(); return forced ?? PaletteFor(RunSeed); } }
    public static float Side { get { ReadForce(); return forcedSide != 0f ? forcedSide : SideFor(RunSeed); } }
}

public static class SpaceSunTuning
{
    public const float CellUnits = 6.6f;          // world width of a 1024 cell (the disc is ~78% of it: ~5.2 u across)
    public const float DiscRadiusShare = .39f;    // of the cell width
    public const float InnerEdge = 2.1f;          // the disc's inner edge sits this far from the lane's centre line (the rails are at 2.35)
    public const float TurnSeconds = 210f;        // one full turn of the body, corona and flares
    public const float PulseSeconds = 9f, PulseScale = .025f, PulseAlpha = .12f;
    public const float FlareFps = 6f;
    public const float DiscAlpha = .62f, CoronaAlpha = .6f, FlareAlpha = .5f;
    public const float ArcFirst = 14f, ArcEveryMin = 28f, ArcEveryMax = 58f, ArcSeconds = 8f, ArcScale = 1.28f, ArcAlpha = .5f;
    public const float VioletGain = .72f;   // the violet palette is the brighter on the dark sky: held to the readability suites (BossHeartsTest, AtomClarityTest)
    public const int VariantWarm = 0, VariantViolet = 1;   // the disc cell each palette's flares were drawn for
    public const int SortingOffset = 0;

    // The wired disc file is the drawn 1024 cell cropped to its visible box (scripts/crop_sun_disc.py --check prints these):
    // x / y are the box's bottom-left in the cell's frame (y up), w / h its size and the file's size. The cell's centre
    // (512, 512) is the sun's centre, so the sprite's pivot is ((512 - x) / w, (512 - y) / h) and the disc lands exactly
    // where the full cell put it. A file of another size (older art) is drawn the old way, as a 1024 cell.
    public const int Cell = 1024;
    public struct Crop { public int x, y, w, h; public Crop(int x, int y, int w, int h) { this.x = x; this.y = y; this.w = w; this.h = h; } }
    public static readonly Crop DiscCropWarm = new Crop(92, 115, 828, 798);
    public static readonly Crop DiscCropViolet = new Crop(53, 32, 926, 956);
    public static Crop DiscCrop(string palette) { return palette == SpaceSunPick.Warm ? DiscCropWarm : DiscCropViolet; }
    public static bool IsCropped(Texture2D disc, string palette)
    {
        var c = DiscCrop(palette);
        return disc != null && disc.width == c.w && disc.height == c.h;
    }
    // The disc sprite's pivot (normalised) for a cropped file: the cell centre's place in the crop.
    public static Vector2 DiscPivot(string palette)
    {
        var c = DiscCrop(palette);
        return new Vector2((Cell * .5f - c.x) / c.w, (Cell * .5f - c.y) / c.h);
    }
}

public sealed class SpaceSunLayer
{
    BackdropSet set;
    float clock;
    float HalfH { get { return set.HalfHeight; } }
    static bool Quiet { get { return SpaceDirector.Quiet; } }
    Transform sunRoot;
    SpriteRenderer sunDisc, sunCorona, sunFlare, sunArc;
    Sprite sunDiscSprite, sunCoronaSprite;
    Sprite[] sunFlares;
    Material sunAdditive;
    Texture2D sunTexDisc, sunTexCorona, sunTexFlares;
    float sunAngle, sunArcIn = SpaceSunTuning.ArcFirst, sunArcAge = -1f, sunArcAngle;
    readonly System.Random sunRng = new System.Random(SpaceSunPick.RunSeed);
    public string Palette { get; private set; }
    public float Side { get; private set; }
    public float Angle { get { return sunAngle; } }
    public int FlareFrame { get; private set; }
    public float ArcAge { get { return sunArcAge; } }
    public int ArcCount { get; private set; }
    public Transform Root { get { return sunRoot; } }
    public SpriteRenderer Disc { get { return sunDisc; } }
    public SpriteRenderer Corona { get { return sunCorona; } }
    public SpriteRenderer Flare { get { return sunFlare; } }
    public SpriteRenderer Arc { get { return sunArc; } }

    // World position of the sun's centre for a view this wide: the disc's inner edge stays InnerEdge from the centre line.
    public static float SunCentreX(float side)
    {
        float r = SpaceSunTuning.CellUnits * SpaceSunTuning.DiscRadiusShare;
        return side * (SpaceSunTuning.InnerEdge + r);
    }

    public SpaceSunLayer(BackdropSet owner) { set = owner; }

    public bool Built { get { return sunRoot != null; } }

    public void Build()
    {
        string palette = SpaceSunPick.Current;
        string folder = BackdropCatalog.Folder("Space");
        sunTexDisc = Resources.Load<Texture2D>(folder + "sun_" + palette);
        sunTexCorona = Resources.Load<Texture2D>(folder + "sun_" + palette + "_corona");
        sunTexFlares = Resources.Load<Texture2D>(folder + "flares_" + palette);
        if (sunTexDisc == null || sunTexFlares == null) return;       // art not installed: no sun, nothing breaks
        Palette = palette;
        Side = SpaceSunPick.Side;
        int variant = palette == SpaceSunPick.Warm ? SpaceSunTuning.VariantWarm : SpaceSunTuning.VariantViolet;
        float ppu = 1024f / SpaceSunTuning.CellUnits;

        // the disc file is the drawn cell's visible box: the whole file, pivoted so the cell's centre sits under the root
        sunDiscSprite = SpaceSunTuning.IsCropped(sunTexDisc, palette)
            ? Sprite.Create(sunTexDisc, new Rect(0, 0, sunTexDisc.width, sunTexDisc.height), SpaceSunTuning.DiscPivot(palette), ppu, 0, SpriteMeshType.FullRect)
            : Sprite.Create(sunTexDisc, new Rect(variant * 1024, 0, 1024, 1024), new Vector2(.5f, .5f), ppu, 0, SpriteMeshType.FullRect);
        if (sunTexCorona != null) sunCoronaSprite = Sprite.Create(sunTexCorona, new Rect(variant * 1024, 0, 1024, 1024), new Vector2(.5f, .5f), ppu, 0, SpriteMeshType.FullRect);
        sunFlares = new Sprite[8];
        for (int i = 0; i < 8; i++)
        {
            int col = i % 4, row = i / 4;   // row 0 is the TOP row of the sheet
            sunFlares[i] = Sprite.Create(sunTexFlares, new Rect(col * 1024, (1 - row) * 1024, 1024, 1024), new Vector2(.5f, .5f), ppu, 0, SpriteMeshType.FullRect);
        }

        var shader = Resources.Load<Shader>("BackdropShaders/BackdropAdditive");
        if (shader != null) sunAdditive = new Material(shader) { name = "SpaceSunAdditive" };

        int order = set.Spec.Order("planet_deep") + SpaceSunTuning.SortingOffset;
        sunRoot = new GameObject("SpaceSun").transform;
        sunRoot.SetParent(set.Root, false);
        sunRoot.localPosition = new Vector3(SunCentreX(Side), HalfH * .5f, set.DepthZ("planet_deep"));
        sunDisc = SunRenderer("disc", order, sunDiscSprite, null);
        sunCorona = SunRenderer("corona", order + 1, sunCoronaSprite, sunAdditive);
        sunFlare = SunRenderer("flares", order + 2, sunFlares[0], sunAdditive);
        sunArc = SunRenderer("arc", order + 3, sunFlares[0], sunAdditive);
        sunArc.enabled = false;
        sunArcIn = SpaceSunTuning.ArcFirst + (float)sunRng.NextDouble() * 10f;
        sunAngle = 0f;
        Step(0f);
    }

    SpriteRenderer SunRenderer(string name, int order, Sprite s, Material m)
    {
        var go = new GameObject(name, typeof(SpriteRenderer));
        go.transform.SetParent(sunRoot, false);
        var r = go.GetComponent<SpriteRenderer>();
        r.sprite = s;
        r.sortingOrder = order;
        if (m != null) r.sharedMaterial = m;
        r.enabled = s != null;
        return r;
    }

    public void Teardown()
    {
        BackdropAtlas.Kill(sunAdditive);
        if (sunDiscSprite != null) BackdropAtlas.Kill(sunDiscSprite);
        if (sunCoronaSprite != null) BackdropAtlas.Kill(sunCoronaSprite);
        if (sunFlares != null) foreach (var s in sunFlares) if (s != null) BackdropAtlas.Kill(s);
        sunAdditive = null; sunDiscSprite = sunCoronaSprite = null; sunFlares = null;
        sunTexDisc = sunTexCorona = sunTexFlares = null;     // (Resources textures are shared assets: the unused-asset pass frees them)
        if (sunRoot != null) BackdropAtlas.Kill(sunRoot.gameObject);
        sunRoot = null;
    }

    // One frame of the sun (the director's scaled dt: a paused game holds it).
    public void Step(float dt)
    {
        if (sunRoot == null) return;
        clock += dt;
        float t = SpaceSunTuning.TurnSeconds;
        sunAngle = Mathf.Repeat(sunAngle + 360f * dt / t, 360f);
        sunRoot.localRotation = Quaternion.Euler(0f, 0f, sunAngle);
        float a = set.Alpha * set.SunDim * (Palette == SpaceSunPick.Violet ? SpaceSunTuning.VioletGain : 1f);
        float pulse = Mathf.Sin(clock * 2f * Mathf.PI / SpaceSunTuning.PulseSeconds);

        var c = sunDisc.color; c.r = c.g = c.b = 1f; c.a = SpaceSunTuning.DiscAlpha * a; sunDisc.color = c;
        if (sunCorona.enabled)
        {
            float s = 1f + SpaceSunTuning.PulseScale * pulse;
            sunCorona.transform.localScale = new Vector3(s, s, 1f);
            c.a = SpaceSunTuning.CoronaAlpha * (1f - SpaceSunTuning.PulseAlpha * .5f + SpaceSunTuning.PulseAlpha * .5f * pulse) * a;
            sunCorona.color = c;
        }
        int frame = (int)(clock * SpaceSunTuning.FlareFps) & 7;
        FlareFrame = frame;
        if (sunFlares != null) sunFlare.sprite = sunFlares[frame];
        c.a = SpaceSunTuning.FlareAlpha * (.9f + .1f * pulse) * a; sunFlare.color = c;

        // an occasional BIGGER prominence arc: the flipbook again, larger, out of phase, swelling in and fading out
        if (sunArcAge < 0f)
        {
            sunArcIn -= dt;
            if (sunArcIn <= 0f && !Quiet)
            {
                sunArcAge = 0f; ArcCount++;
                sunArcAngle = (float)sunRng.NextDouble() * 360f;
                sunArc.enabled = true;
            }
        }
        if (sunArcAge >= 0f)
        {
            sunArcAge += dt;
            float k = sunArcAge / SpaceSunTuning.ArcSeconds;
            if (k >= 1f)
            {
                sunArcAge = -1f; sunArc.enabled = false;
                sunArcIn = SpaceSunTuning.ArcEveryMin + (float)sunRng.NextDouble() * (SpaceSunTuning.ArcEveryMax - SpaceSunTuning.ArcEveryMin);
            }
            else
            {
                float env = Mathf.Sin(Mathf.PI * k); env *= env;
                sunArc.sprite = sunFlares[(frame + 3) & 7];
                float s = SpaceSunTuning.ArcScale * (.92f + .08f * env);
                sunArc.transform.localScale = new Vector3(s, s, 1f);
                sunArc.transform.localRotation = Quaternion.Euler(0f, 0f, sunArcAngle);
                c.a = SpaceSunTuning.ArcAlpha * env * a; sunArc.color = c;
            }
        }
    }
}
