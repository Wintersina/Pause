using UnityEngine;

// THE EMBER RAIL MINE'S FLAME-THROWER (docs/world-attacks-art.md 3.7b): the
// beam, its aim line, charge-up flare, muzzle burst, rail impact and heat
// haze, drawn procedurally once (cached) until Codex paints
// Resources/Attacks/Ember/ember_attack_mineflame.png (AttackArt.MineFlame),
// which takes over slot by slot (installed: the painted cells are what plays; this stays the
// fallback for any missing cell or file).
//
// PALETTE EXCEPTION (Ember mine only). The standing rule is "no red but the
// player's" (HostileGlow.IsPlayerRed: hue within 28 deg of red). The user
// asked for a red / flame look for Ember's mines, so this one attack may use
// a deep red-orange, under three limits:
//   * no pixel closer than MinHueGapDeg (22) to the player's red
//     (PlayerRedHueDeg 355, #FF3E4E): the reddest flame pixel is
//     DeepEdge, hue 19 = 24 deg away (measured by RailMineLaserTest);
//   * the hostile cue stays: a thin neon-pink rim (hue 326, 29 deg off) on
//     the outermost pixels and pink ember specks (Pink);
//   * the body is fire: white-yellow core, amber, orange, then the red-orange
//     edge, with a dark plum outline so it separates from the lava backdrop.
// Every other world's mine, and Tide's (which shares the Ember boss cells),
// keeps MineLaserArt's recoloured boss cells.
//
// All textures are 1 world unit wide (pixels-per-unit = width) so RailMineLaser.Span,
// which lays a unit square, normalises by Sprite.bounds. Nothing here runs
// per shot or per frame: the sprites are built when the mine spawns.
public sealed class MineFlameArt
{
    public const float PlayerRedHueDeg = 355f;
    public const float MinHueGapDeg = 22f;

    // the ramp (hot to cool); DeepEdge is the only pixel colour inside the player's red band
    public static readonly Color32 White = new Color32(255, 252, 230, 255);
    public static readonly Color32 PaleYellow = new Color32(255, 234, 140, 255);
    public static readonly Color32 Amber = new Color32(255, 190, 50, 255);
    public static readonly Color32 Orange = new Color32(255, 140, 24, 255);
    public static readonly Color32 DeepEdge = new Color32(208, 76, 14, 255);   // hue 19
    public static readonly Color32 PinkRim = new Color32(255, 76, 178, 255);   // hue 326: the hostile cue
    public static readonly Color32 Pink = new Color32(255, 150, 205, 255);
    public static readonly Color32 Plum = new Color32(34, 12, 38, 160);        // outline, over the lava

    public const int BeamFrames = 6, PilotFrames = 4, FlashFramesN = 4, SparkFrames = 3, HazeFrames = 2;
    public const int BeamW = 48, BeamH = 448, SightW = 16, SightH = 256;

    public Sprite sight;
    public Sprite sightB;   // the painted aim line's second drawing (null: procedural, one drawing)
    public readonly Sprite[] beam = new Sprite[BeamFrames];
    public readonly Sprite[] pilot = new Sprite[PilotFrames];
    public readonly Sprite[] flash = new Sprite[FlashFramesN];
    public readonly Sprite[] spark = new Sprite[SparkFrames];
    public readonly Sprite[] haze = new Sprite[HazeFrames];
    public bool painted;   // true: the Codex strip supplied the sprites

    static MineFlameArt cached;

    public bool Alive => beam[0] != null && sight != null && pilot[0] != null && flash[0] != null && spark[0] != null && haze[0] != null;

    public static MineFlameArt Ember()
    {
        if (cached != null && cached.Alive) return cached;
        var a = AttackArt.MineFlame(3);
        if (a == null || !a.Alive) a = Build();
        cached = a;
        return a;
    }

    // tests: forget the cache (a scene swap destroyed the textures, or an atlas was injected)
    public static void Reset() { cached = null; }

    public static MineFlameArt Build()
    {
        var a = new MineFlameArt();
        a.sight = Make("EmberMineSight", SightW, SightH, Sight(), SightW);
        for (int f = 0; f < BeamFrames; f++) a.beam[f] = Make("EmberMineBeam" + f, BeamW, BeamH, BeamFrame(f), BeamW);
        for (int f = 0; f < PilotFrames; f++) a.pilot[f] = Make("EmberMinePilot" + f, 32, 48, Pilot(f), 32);
        for (int f = 0; f < FlashFramesN; f++) a.flash[f] = Make("EmberMineFlash" + f, 40, 40, Burst(40, f, 6, .36f, .5f, 17, 1f), 40);
        for (int f = 0; f < SparkFrames; f++) a.spark[f] = Make("EmberMineSpark" + f, 32, 32, Burst(32, f, 5, .22f + .1f * f, .45f, 29, .9f - .2f * f), 32);
        for (int f = 0; f < HazeFrames; f++) a.haze[f] = Make("EmberMineHaze" + f, 32, 64, Haze(f), 32);
        return a;
    }

    static Sprite Make(string name, int w, int h, Color32[] px, int ppu)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.name = name;
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.hideFlags = HideFlags.HideAndDontSave;
        tex.SetPixels32(px);
        tex.Apply(false, false);
        var s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), ppu, 0, SpriteMeshType.FullRect);
        s.name = name;
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }

    // ---- the ramp -------------------------------------------------------------

    // heat -> colour (false: transparent). Hotter is whiter; the outermost
    // pixels are the pink rim, then the red-orange edge, then a plum outline.
    public static bool Ramp(float h, out Color32 c)
    {
        c = default;
        if (h < -.14f) return false;
        if (h < 0f) { c = Plum; return true; }
        if (h < .03f) c = PinkRim;
        else if (h < .26f) c = DeepEdge;
        else if (h < .44f) c = Orange;
        else if (h < .60f) c = Amber;
        else if (h < .72f) c = PaleYellow;
        else c = White;
        return true;
    }

    // ---- noise ----------------------------------------------------------------

    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(seed * 83492791);
            h ^= h >> 13; h *= 1274126177u; h ^= h >> 16;
            return (h & 0xFFFF) / 65535f;
        }
    }

    // value noise, periodic in y with `period` lattice cells (so a flow scrolled one full period loops)
    static float VNoise(float x, float y, int period, int seed)
    {
        int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        int y0 = ((iy % period) + period) % period, y1 = (y0 + 1) % period;
        float a = Hash(ix, y0, seed), b = Hash(ix + 1, y0, seed), c = Hash(ix, y1, seed), d = Hash(ix + 1, y1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    static float Smooth(float a, float b, float x) { x = Mathf.Clamp01((x - a) / (b - a)); return x * x * (3f - 2f * x); }

    // ---- the beam: 48 x 448, local +y runs from the muzzle to the far rail -----------

    // One frame of a six-frame loop: tongues and turbulence flow away from the muzzle.
    static Color32[] BeamFrame(int f)
    {
        int W = BeamW, H = BeamH;
        var px = new Color32[W * H];
        float ph = f / (float)BeamFrames;
        for (int y = 0; y < H; y++)
        {
            float v = (y + .5f) / H;
            // the two edges lick in and out on their own noise, more toward the far end
            float amp = Mathf.Lerp(.07f, .15f, v);
            float eL = Mathf.Lerp(.82f, .88f, Smooth(0f, .25f, v)) + (VNoise(0f, v * 10f - ph * 10f, 10, 11) - .5f) * 2f * amp;
            float eR = Mathf.Lerp(.82f, .88f, Smooth(0f, .25f, v)) + (VNoise(0f, v * 10f - ph * 10f, 10, 12) - .5f) * 2f * amp;
            float sway = (VNoise(0f, v * 8f - ph * 8f, 8, 21) - .5f) * .16f;
            float cool = Smooth(.55f, 1f, v) * .16f - (1f - v) * .1f;
            for (int x = 0; x < W; x++)
            {
                float u = ((x + .5f) / W) * 2f - 1f;
                float uw = u - sway;
                float e = uw < 0f ? eL : eR;
                float r = Mathf.Abs(uw) / Mathf.Max(.3f, e);
                float t = 1f - r;
                float n = .55f * VNoise(u * 2.2f + 5f, v * 12f - ph * 12f, 12, 3)
                        + .3f * VNoise(u * 4.5f, v * 24f - ph * 24f, 24, 4)
                        + .15f * VNoise(u * 9f, v * 48f - ph * 48f, 48, 5);
                float h = t * .95f + (n - .5f) * .55f * (1f - .35f * Mathf.Clamp01(t)) - cool * .6f;
                Color32 c;
                if (Ramp(h, out c)) px[y * W + x] = c;
            }
        }
        // embers: bright specks spat off the flame, flying down the beam and drifting sideways
        for (int k = 0; k < 14; k++)
        {
            float v0 = Hash(k, 1, 77);
            float vv = Mathf.Repeat(v0 + ph * (1 + k % 2), 1f);
            float uu = (Hash(k, 2, 78) * 2f - 1f) * .92f + Mathf.Sin((vv + k) * 9f) * .05f;
            int x = Mathf.Clamp(Mathf.FloorToInt((uu * .5f + .5f) * W), 0, W - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(vv * H), 1, H - 3);
            Color32 c = k % 3 == 0 ? Pink : (k % 3 == 1 ? PaleYellow : Amber);
            px[y * W + x] = c;
            px[(y + 1) * W + x] = c;
            if (k % 4 == 0) px[(y + 2) * W + x] = c;
        }
        return px;
    }

    // ---- the aim line: 16 x 256, dashes of a gas jet, pink specks between -------------

    static Color32[] Sight()
    {
        int W = SightW, H = SightH;
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
        {
            int m = y % 32;
            bool dash = m < 22;
            float taper = dash ? Mathf.Min(m, 21 - m) / 5f : 0f;   // narrow at both ends of a dash
            for (int x = 0; x < W; x++)
            {
                float u = Mathf.Abs(((x + .5f) / W) * 2f - 1f);
                if (dash)
                {
                    float half = Mathf.Lerp(.35f, 1f, Mathf.Clamp01(taper));
                    if (u > half) continue;
                    px[y * W + x] = u < .26f * half ? PaleYellow : (u < .62f * half ? Orange : DeepEdge);
                }
                else if (m >= 26 && m <= 27 && u < .14f)
                    px[y * W + x] = Pink;   // the hostile-cue speck in the gap
            }
        }
        return px;
    }

    // ---- the pilot flame (the charge-up): 32 x 48, base at the bottom, tip up ---------

    static Color32[] Pilot(int f)
    {
        int W = 32, H = 48;
        var px = new Color32[W * H];
        float lean = Mathf.Sin(f * 1.57f) * .22f;
        for (int y = 0; y < H; y++)
        {
            float v = y / (float)(H - 1);
            float hw = Mathf.Sin(Mathf.PI * Mathf.Pow(Mathf.Clamp01(v), .55f)) * (.85f - .35f * v) + .02f;
            float cx = lean * v * v * 2f + Mathf.Sin(v * 6f + f * 1.9f) * .05f * v;
            for (int x = 0; x < W; x++)
            {
                float u = ((x + .5f) / W) * 2f - 1f - cx;
                float t = 1f - Mathf.Abs(u) / Mathf.Max(.05f, hw);
                float h = t * 1.0f + (1f - v) * .2f - v * .15f + (Hash(x, y, f + 40) - .5f) * .12f;
                Color32 c;
                if (hw > .05f && Ramp(h, out c)) px[y * W + x] = c;
            }
        }
        return px;
    }

    // ---- a burst (muzzle flash, rail impact): ragged tongues round a hot centre ------------

    static Color32[] Burst(int S, int f, int lobes, float baseR, float amp, int seed, float heat)
    {
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = ((x + .5f) / S) * 2f - 1f, dy = ((y + .5f) / S) * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy), th = Mathf.Atan2(dy, dx);
                float lobe = Mathf.Pow(.5f + .5f * Mathf.Cos(lobes * th + f * 1.7f + Hash(f, 0, seed) * 6f), 2f);
                float R = baseR + amp * lobe * .9f + (Hash(x, y, seed + f) - .5f) * .06f;
                float h = (1f - r / Mathf.Max(.1f, R)) * heat * 1.05f;
                Color32 c;
                if (Ramp(h, out c)) px[y * S + x] = c;
            }
        // embers flung out
        for (int k = 0; k < 5; k++)
        {
            float a = Hash(k, f, seed + 9) * 6.283f, d = .55f + .4f * Hash(k, f, seed + 10);
            int x = Mathf.Clamp(Mathf.FloorToInt((Mathf.Cos(a) * d * .5f + .5f) * S), 0, S - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt((Mathf.Sin(a) * d * .5f + .5f) * S), 0, S - 1);
            px[y * S + x] = k % 2 == 0 ? Pink : PaleYellow;
        }
        return px;
    }

    // ---- the heat haze: 32 x 64, a faint warm veil that wobbles ---------------------

    static Color32[] Haze(int f)
    {
        int W = 32, H = 64;
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
        {
            float v = y / (float)H;
            for (int x = 0; x < W; x++)
            {
                float u = ((x + .5f) / W) * 2f - 1f;
                float rip = .82f + .18f * Mathf.Sin(v * 38f + f * 3.1f + u * 4f);
                float a = Mathf.Exp(-u * u * 3.2f) * rip;
                int q = Mathf.Clamp(Mathf.FloorToInt(a * 4f), 0, 3);   // quantised: pixel-art soft
                if (q == 0) continue;
                px[y * W + x] = new Color32(255, 110, 40, (byte)(q * 13));
            }
        }
        return px;
    }
}
