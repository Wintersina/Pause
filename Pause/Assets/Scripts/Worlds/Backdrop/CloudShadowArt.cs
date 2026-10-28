using UnityEngine;

// The cloud-shadow art (CloudShadows.cs), generated once per process: a 1024x512 one-channel DENSITY MASK atlas of six cloud shapes
// (two cumulus, two stratocumulus, two cirrus; 384 cells across at most, one texel = one 4-art-pixel cell) and a 128x128 tileable fBm
// detail map the shader scrolls over them to morph the cloud. Built time-sliced (Pump, from WorldArtPreload, about 1.5 ms a frame while a
// destination world's art loads) so a world swap never hitches; Shapes() finishes the build synchronously if the art is needed first
// (a developer jump, a skipped preload, an edit-mode test).
//
// SHAPES  (all fractal-noise made, deterministic)
//   cumulus        a union of 7 soft discs for the big lobes, a billowy domain-warped fBm on the rim (ragged, scalloped edges),
//                  small satellite wisps drifting off the rim; dense core, thin ragged edge
//   stratocumulus  a big soft ellipse broken into rolls by a jittered cellular (Worley) pattern with clear lanes between the cells
//   cirrus         thin, long, slightly tilted streaks (stretched fBm) under a narrow elliptical envelope; thin density
// The mask is smooth density 0..1; the SHADER turns it into pixel art (stepped alpha, dither on the rims).
public static class CloudShadowArt
{
    public const int Size = 1024;                       // mask atlas width (cells); its height is Height
    public const int Height = 512;
    public const int Steps = 5;
    public const int DetailSize = 128;
    public const float PixelsPerUnit = 100f;
    public const int Coarse = 16;                       // the coarse ink maps' resolution (spawn gate, coverage measure)
    public const float InkLevel = .4f;                  // a mask value at or above this counts as shadow ink

    struct Def { public int x, y, w, h, kind, seed; }

    // kind: 0 cumulus, 1 stratocumulus, 2 cirrus
    static readonly Def[] Defs =
    {
        new Def { x = 0,   y = 0,   w = 320, h = 320, kind = 0, seed = 11 },
        new Def { x = 320, y = 0,   w = 320, h = 384, kind = 0, seed = 23 },
        new Def { x = 640, y = 0,   w = 384, h = 256, kind = 1, seed = 37 },
        new Def { x = 640, y = 256, w = 384, h = 192, kind = 1, seed = 41 },
        new Def { x = 0,   y = 320, w = 320, h = 192, kind = 2, seed = 53 },
        new Def { x = 320, y = 384, w = 320, h = 128, kind = 2, seed = 59 },
    };
    public static int ShapeCount { get { return Defs.Length; } }
    public static int ShapeW(int s) { return Defs[s].w; }
    public static int ShapeH(int s) { return Defs[s].h; }
    public static int ShapeKind(int s) { return Defs[s].kind; }

    static Texture2D mask, detail;
    static Sprite[] sprites;
    static bool pending;
    static byte[] px;
    static int curShape, curRow;
    static float[] balls = new float[24 * 4];
    static int ballCount;
    static float[] ink = new float[6];
    static float[] coarse = new float[6 * Coarse * Coarse];
    static float[] isoperimetric = new float[6];
    static int[] lobes = new int[6];
    static readonly System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();

    public static bool Ready { get { return mask != null && sprites != null && sprites.Length > 0 && sprites[0] != null; } }
    public static bool Pending { get { return pending && !Ready; } }
    public static Texture2D Mask { get { Shapes(); return mask; } }
    public static Texture2D Detail { get { Shapes(); return detail; } }
    // share of a shape's rect that is ink (mask >= InkLevel)
    public static float Ink(int s) { Shapes(); return ink[s]; }
    // P^2 / (4 pi A) of the ink's outline (4-connected cell edges): ~1.3 for a square or disc, higher the more ragged and lobed
    public static float Isoperimetric(int s) { Shapes(); return isoperimetric[s]; }
    // local density peaks on the coarse map (the cloud's lobes)
    public static int Lobes(int s) { Shapes(); return lobes[s]; }
    // the fraction of ink in the coarse cell at (u, v) of the shape (0..1 across it)
    public static float CoarseInk(int s, float u, float v)
    {
        int gx = Mathf.Clamp((int)(u * Coarse), 0, Coarse - 1), gy = Mathf.Clamp((int)(v * Coarse), 0, Coarse - 1);
        return coarse[(s * Coarse + gy) * Coarse + gx];
    }
    // Asks for the art to be built, a slice a frame by Pump (WorldArtPreload.QueueBackdrop calls it for a world with shadows).
    public static void Request() { if (!Ready) pending = true; }

    // One frame's slice of the build, at most `budgetMs`; true once the art is complete.
    public static bool Pump(float budgetMs)
    {
        if (Ready) { pending = false; return true; }
        if (!pending) return true;
        if (px == null) { px = new byte[Size * Height]; curShape = 0; curRow = 0; BeginShape(curShape); }
        watch.Restart();
        while (curShape < Defs.Length)
        {
            FillRow(Defs[curShape], curRow);
            if (++curRow >= Defs[curShape].h)
            {
                curRow = 0;
                if (++curShape < Defs.Length) BeginShape(curShape);
            }
            if (watch.Elapsed.TotalMilliseconds >= budgetMs) break;
        }
        if (curShape < Defs.Length) return false;
        Finish();
        return true;
    }

    public static Sprite[] Shapes()
    {
        if (Ready) return sprites;
        pending = true;
        Pump(float.MaxValue);
        return sprites;
    }

    // ---- noise ----

    static float Hash(int x, int y, int seed)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1274126177);
        h = (h ^ (h >> 13)) * 1274126177u;
        return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
    }

    static float VNoise(float x, float y, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        float a = Hash(xi, yi, seed), b = Hash(xi + 1, yi, seed), c = Hash(xi, yi + 1, seed), d = Hash(xi + 1, yi + 1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    // value noise that tiles every `period` lattice cells (the detail map)
    static float PNoise(float x, float y, int period, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        int x0 = ((xi % period) + period) % period, y0 = ((yi % period) + period) % period;
        int x1 = (x0 + 1) % period, y1 = (y0 + 1) % period;
        return Mathf.Lerp(Mathf.Lerp(Hash(x0, y0, seed), Hash(x1, y0, seed), fx), Mathf.Lerp(Hash(x0, y1, seed), Hash(x1, y1, seed), fx), fy);
    }

    static float Fbm(float x, float y, int octaves, int seed)
    {
        float amp = .5f, f = 1f, sum = 0f, norm = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * VNoise(x * f, y * f, seed + i * 17);
            norm += amp; amp *= .5f; f *= 2.03f;
        }
        return sum / norm;
    }

    // puffy: the fBm of |2n - 1| folded, so its maxima are rounded bumps (cumulus lobes)
    static float Billow(float x, float y, int octaves, int seed)
    {
        float amp = .5f, f = 1f, sum = 0f, norm = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * (1f - Mathf.Abs(2f * VNoise(x * f, y * f, seed + i * 17) - 1f));
            norm += amp; amp *= .5f; f *= 2.03f;
        }
        return sum / norm;
    }

    static float Smooth(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    // ---- shapes ----

    static void BeginShape(int s)
    {
        var d = Defs[s];
        ballCount = 0;
        if (d.kind != 0) return;
        // the big lobes: 7 discs spread over the middle, then 5 satellites on the rim
        float m = Mathf.Min(d.w, d.h);
        for (int k = 0; k < 12; k++)
        {
            float ang = Hash(k, s, 3) * 6.283f, rad = Mathf.Sqrt(Hash(k, s, 5));
            AddBall(d.w * .5f + Mathf.Cos(ang) * rad * d.w * .28f, d.h * .5f + Mathf.Sin(ang) * rad * d.h * .27f, m * (.17f + .12f * Hash(k, s, 7)), 1f);
        }
        for (int k = 0; k < 5; k++)
        {
            float ang = Hash(k, s, 13) * 6.283f;
            float rad = .36f + .1f * Hash(k, s, 17);
            AddBall(d.w * .5f + Mathf.Cos(ang) * rad * d.w, d.h * .5f + Mathf.Sin(ang) * rad * d.h, 6f + 9f * Hash(k, s, 19), .75f);
        }
    }

    static void AddBall(float cx, float cy, float r, float weight)
    {
        balls[ballCount * 4] = cx; balls[ballCount * 4 + 1] = cy; balls[ballCount * 4 + 2] = r; balls[ballCount * 4 + 3] = weight;
        ballCount++;
    }

    static void FillRow(Def d, int y)
    {
        int row = (d.y + y) * Size + d.x;
        for (int x = 0; x < d.w; x++)
        {
            float v = Density(d, x, y);
            px[row + x] = (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);
        }
    }

    // The density 0..1 of shape `d` at its cell (x, y).
    static float Density(Def d, int x, int y)
    {
        float m;
        switch (d.kind)
        {
            case 0: m = Cumulus(d, x, y); break;
            case 1: m = Strato(d, x, y); break;
            default: m = Cirrus(d, x, y); break;
        }
        int edge = Mathf.Min(Mathf.Min(x, d.w - 1 - x), Mathf.Min(y, d.h - 1 - y));
        return m * Mathf.Clamp01((edge - 1f) / 8f);
    }

    static float Cumulus(Def d, int x, int y)
    {
        float L = 0f;
        for (int k = 0; k < ballCount; k++)
        {
            float dx = x - balls[k * 4], dy = y - balls[k * 4 + 1], br = balls[k * 4 + 2];
            if (dx >= br || dx <= -br || dy >= br || dy <= -br) continue;
            float f = 1f - Mathf.Sqrt(dx * dx + dy * dy) / br;
            if (f <= 0f) continue;
            f = f * f * (3f - 2f * f) * balls[k * 4 + 3];
            L = 1f - (1f - L) * (1f - f);
        }
        if (L <= 0f && Mathf.Min(x, y) > 0)
        {
            // nothing near: skip the noise (the corners)
            float dxc = Mathf.Abs(x - d.w * .5f) / d.w, dyc = Mathf.Abs(y - d.h * .5f) / d.h;
            if (dxc * dxc + dyc * dyc > .13f) return 0f;
        }
        float warp = (Fbm(x * .011f + 3f, y * .011f, 3, d.seed) - .5f) * 2.4f;
        float b = Billow(x * .034f + warp, y * .034f + warp * .7f, 4, d.seed + 5);
        float F = L + (b - .5f) * .9f * Mathf.Clamp01(L * 1.7f + .25f);
        return Smooth(0f, .4f, F);
    }

    static float Strato(Def d, int x, int y)
    {
        float rx = (x / (float)d.w - .5f) * 2f, ry = (y / (float)d.h - .5f) * 2f;
        float rho = Mathf.Sqrt(rx * rx * .85f + ry * ry * 1.15f);
        float E = Smooth(1.1f, .22f, rho + (Fbm(x * .009f, y * .009f, 3, d.seed) - .5f) * .5f);
        if (E <= 0f) return 0f;
        // jittered cellular rolls: F1 = distance to the nearest feature point, ~38-cell cells, the lanes between them stay clear
        const float cs = 38f;
        float px_ = x / cs + (Fbm(x * .02f, y * .02f, 2, d.seed + 3) - .5f) * .9f, py_ = y / cs + (Fbm(x * .02f + 9f, y * .02f, 2, d.seed + 4) - .5f) * .9f;
        int cx = Mathf.FloorToInt(px_), cy = Mathf.FloorToInt(py_);
        float f1 = 9f;
        for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                float fx = cx + i + Hash(cx + i, cy + j, d.seed + 21) * .8f + .1f - px_;
                float fy = cy + j + Hash(cx + i, cy + j, d.seed + 22) * .8f + .1f - py_;
                f1 = Mathf.Min(f1, fx * fx + fy * fy);
            }
        f1 = Mathf.Sqrt(f1);
        float roll = 1f - Smooth(.22f, .78f, f1);
        float pat = roll * (.7f + .6f * Fbm(x * .04f, y * .04f, 3, d.seed + 7));
        return E * Smooth(.1f, .4f, pat);
    }

    static float Cirrus(Def d, int x, int y)
    {
        float rx = (x / (float)d.w - .5f) * 2f, ry = (y / (float)d.h - .5f) * 2f;
        float rho = Mathf.Sqrt(rx * rx * .75f + ry * ry * 1.5f);
        float E = Smooth(1f, .2f, rho);
        if (E <= 0f) return 0f;
        const float ca = .9798f, sa = -.2f;     // a slight tilt
        float xr = x * ca - y * sa, yr = x * sa + y * ca;
        float s1 = Fbm(xr * .010f, yr * .085f, 4, d.seed);
        float s2 = Fbm(xr * .021f + 9f, yr * .2f, 3, d.seed + 5);
        return .85f * E * Smooth(.34f, .62f, s1 * .68f + s2 * .32f + (E - .5f) * .3f);
    }

    // ---- finish ----

    static void Finish()
    {
        if (mask != null) Object.DestroyImmediate(mask);
        if (detail != null) Object.DestroyImmediate(detail);
        bool r8 = SystemInfo.SupportsTextureFormat(TextureFormat.R8);
        mask = new Texture2D(Size, Height, r8 ? TextureFormat.R8 : TextureFormat.RGBA32, false, true)
        {
            name = "CloudShadowMask", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave,
        };
        if (r8) mask.SetPixelData(px, 0);
        else
        {
            var c32 = new Color32[px.Length];
            for (int i = 0; i < px.Length; i++) c32[i] = new Color32(px[i], px[i], px[i], 255);
            mask.SetPixels32(c32);
        }
        mask.Apply(false, true);

        detail = new Texture2D(DetailSize, DetailSize, r8 ? TextureFormat.R8 : TextureFormat.RGBA32, false, true)
        {
            name = "CloudShadowDetail", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.HideAndDontSave,
        };
        var dpx = new byte[DetailSize * DetailSize];
        for (int y = 0; y < DetailSize; y++)
            for (int x = 0; x < DetailSize; x++)
            {
                float u = x / (float)DetailSize, v = y / (float)DetailSize;
                // 4 tiling octaves (periods 4, 8, 16, 32 lattice cells)
                float n = .5f * PNoise(u * 4f, v * 4f, 4, 1) + .27f * PNoise(u * 8f, v * 8f, 8, 2) + .15f * PNoise(u * 16f, v * 16f, 16, 3) + .08f * PNoise(u * 32f, v * 32f, 32, 4);
                // stretch the contrast so it spans most of 0..1 (value noise piles up around .5)
                dpx[y * DetailSize + x] = (byte)Mathf.RoundToInt(Mathf.Clamp01((n - .5f) * 2.1f + .5f) * 255f);
            }
        if (r8) detail.SetPixelData(dpx, 0);
        else
        {
            var c32 = new Color32[dpx.Length];
            for (int i = 0; i < dpx.Length; i++) c32[i] = new Color32(dpx[i], dpx[i], dpx[i], 255);
            detail.SetPixels32(c32);
        }
        detail.Apply(false, true);

        sprites = new Sprite[Defs.Length];
        for (int s = 0; s < Defs.Length; s++)
        {
            var d = Defs[s];
            sprites[s] = Sprite.Create(mask, new Rect(d.x, d.y, d.w, d.h), new Vector2(.5f, .5f), PixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprites[s].name = "cloud_shadow_" + s;
            sprites[s].hideFlags = HideFlags.HideAndDontSave;
            Stats(s);
        }
        px = null;
        pending = false;
    }

    static void Stats(int s)
    {
        var d = Defs[s];
        int inkCells = 0, perim = 0;
        byte lvl = (byte)Mathf.RoundToInt(InkLevel * 255f);
        for (int y = 0; y < d.h; y++)
            for (int x = 0; x < d.w; x++)
            {
                bool on = px[(d.y + y) * Size + d.x + x] >= lvl;
                if (!on) continue;
                inkCells++;
                if (x == 0 || px[(d.y + y) * Size + d.x + x - 1] < lvl) perim++;
                if (x == d.w - 1 || px[(d.y + y) * Size + d.x + x + 1] < lvl) perim++;
                if (y == 0 || px[(d.y + y - 1) * Size + d.x + x] < lvl) perim++;
                if (y == d.h - 1 || px[(d.y + y + 1) * Size + d.x + x] < lvl) perim++;
            }
        ink[s] = inkCells / (float)(d.w * d.h);
        isoperimetric[s] = inkCells > 0 ? perim * (float)perim / (4f * Mathf.PI * inkCells) : 0f;
        for (int gy = 0; gy < Coarse; gy++)
            for (int gx = 0; gx < Coarse; gx++)
            {
                int x0 = gx * d.w / Coarse, x1 = (gx + 1) * d.w / Coarse, y0 = gy * d.h / Coarse, y1 = (gy + 1) * d.h / Coarse, n = 0;
                for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) if (px[(d.y + y) * Size + d.x + x] >= lvl) n++;
                coarse[(s * Coarse + gy) * Coarse + gx] = n / (float)Mathf.Max(1, (x1 - x0) * (y1 - y0));
            }
        // lobes: local maxima of the mean density on the coarse grid (>= .4, strictly above every neighbour)
        var mean = new float[Coarse * Coarse];
        for (int gy = 0; gy < Coarse; gy++)
            for (int gx = 0; gx < Coarse; gx++)
            {
                int x0 = gx * d.w / Coarse, x1 = (gx + 1) * d.w / Coarse, y0 = gy * d.h / Coarse, y1 = (gy + 1) * d.h / Coarse;
                float sum = 0f;
                for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) sum += px[(d.y + y) * Size + d.x + x];
                mean[gy * Coarse + gx] = sum / (255f * Mathf.Max(1, (x1 - x0) * (y1 - y0)));
            }
        int peaks = 0;
        for (int gy = 0; gy < Coarse; gy++)
            for (int gx = 0; gx < Coarse; gx++)
            {
                float c = mean[gy * Coarse + gx];
                if (c < .4f) continue;
                bool peak = true;
                for (int j = -1; j <= 1 && peak; j++)
                    for (int i = -1; i <= 1; i++)
                    {
                        int nx = gx + i, ny = gy + j;
                        if ((i == 0 && j == 0) || nx < 0 || ny < 0 || nx >= Coarse || ny >= Coarse) continue;
                        float o = mean[ny * Coarse + nx];
                        if (o > c || (o == c && (j < 0 || (j == 0 && i < 0)))) { peak = false; break; }
                    }
                if (peak) peaks++;
            }
        lobes[s] = peaks;
    }
}
