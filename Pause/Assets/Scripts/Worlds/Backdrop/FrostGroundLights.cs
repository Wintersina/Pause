using UnityEngine;

// Frost's ground is ALIVE: the lamps the mid tiles paint (quay posts, plant windows, camp braziers, crane tops) glow,
// flicker, pulse and blink (user, 2026-11-27: "blinking lights and things in the backdrop that make it feel more alive").
//
// Seen from up here (FrostTuning.GroundZoom .41) the painted plant / harbour / camps are mostly IN the tile art, not the
// landmark pieces, so these lights are pinned to the mid tile itself: every point is a spot of the tile (master pixels of
// the 896 x 1792 drawing, frost_redraw/ground_lights.py finds the painted lamp dots: warm, lit, standing out of their
// neighbourhood) and is drawn on every copy of the tile in view, from the tile's own scroll (BackdropTile.travel), so it
// never drifts off its lamp (FrostGroundLightsTest checks every light against GroundPlanner.UV).
//
// Kinds (one letter per point, picked per variant by ground_lights.py):
//   w  window   warm amber, steady with a gentle flicker (a lit window, a quay lamp)
//   f  fire     orange, a livelier flicker (the camps' braziers)
//   b  beacon   amber, a slow pulse (an aircraft-warning lamp on a tall stack / crane)
//   c  beacon   cyan, a slow pulse
//   r  rim      cyan-white, a short blink running along its neighbours (pad / quay edge lights)
//   s  strobe   white, one short flash every few seconds
// Small (FrostGroundLightsTuning.Size of the tile's width: ~7-10 screen px on a phone, the painted dot ~2), never lifted by
// the grade, alpha-capped, no pink (WorldCue.IsPink: pink is the hostile cue), nothing changes faster than MaxHz (and the
// strobes flash at most every StrobeEveryMin s): photosensitivity-safe. Each copy of the tile gets its own phases, so the
// repeated ground never blinks in a grid. Scaled dt only (a paused game is a still frame), a fixed pool, no allocation per
// frame. Sorted just above the floe strip (flow) and under the landmarks, mist and clouds.
public static class FrostGroundLightsTuning
{
    public const int Pool = 192;                    // renderers: ~24 lights a tile x the ~6 tile copies a phone sees
    public static float Size = .022f;               // a light's drawn width, share of the tile's width (~10 px on a 1080 px phone)
    public const float MaxHz = 3f;                  // fastest change of any light (flicker noise / blink / pulse)
    public const float StrobeEveryMin = 2.2f, StrobeEveryMax = 3.4f, StrobeOn = .09f;
    public const float RimPeriod = 1.25f, RimOn = .22f, RimChase = .09f;   // s per blink, s lit, s between neighbours
    public const float BeaconHzMin = .35f, BeaconHzMax = .6f;
    public const float WindowHz = 1.6f, FireHz = 3f;                         // flicker noise rates (<= MaxHz)
    public static float Brightness = 1f;            // every light's alpha x this ("dimmer / brighter" = this one number)

    public static readonly Color Amber = new Color(1f, .72f, .30f, 1f);
    public static readonly Color Fire = new Color(1f, .58f, .22f, 1f);
    public static readonly Color Cyan = new Color(.48f, .93f, 1f, 1f);
    public static readonly Color White = new Color(.88f, .95f, 1f, 1f);

    public const string Kinds = "wfbcrs";
    // per kind (Kinds order): peak alpha, low alpha (the dimmest it gets), size x Size
    public static readonly float[] Peak = { .8f, .82f, .88f, .88f, .88f, .9f };
    public static readonly float[] Low = { .6f, .5f, .15f, .15f, .0f, .0f };
    public static readonly float[] Scale = { .8f, .9f, 1f, 1f, .8f, 1f };
    public static Color ColorOf(char k)
    {
        switch (k)
        {
            case 'f': return Fire;
            case 'b': return Amber;
            case 'c': return Cyan;
            case 'r': return Cyan;
            case 's': return White;
            default: return Amber;
        }
    }

    // The painted lamps, per variant (index 1..4): "x,y,k" per point, ';' between, x / y in master pixels (896 x 1792,
    // from the top-left). Written by frost_redraw/ground_lights.py from tiles_hd/v*/mid.png -- re-run it after a redraw.
    // <ground_lights>
    public static readonly string[] Points =
    {
        "",
        "481,801,b;517,869,f;571,889,w;823,889,s;531,899,f;559,923,f;811,923,f;365,1011,w;331,1037,w;3,1045,w;647,1045,w;375,1059,s;411,1059,c;291,1067,w;319,1077,b;377,1113,w;413,1119,w;571,1145,s;301,1243,w;335,1245,w",
        "455,3,r;527,97,r;349,173,c;459,305,r;99,315,r;125,359,w;577,377,f;845,391,r;595,479,c;883,479,w;559,531,b;575,581,r;863,581,r;347,661,s;335,763,r;397,775,r;9,781,r;617,781,c;455,1105,r;527,1199,r;387,1299,r;329,1437,c;293,1577,r;805,1601,r",
        "657,329,w;579,353,w;23,461,w;217,461,b;263,461,r;873,461,b;333,491,w;791,543,b;387,587,s;387,651,b;353,659,w;437,763,w;119,969,b;769,983,s;789,1015,w;747,1061,w;829,1061,b;347,1073,b;355,1175,r;411,1187,w;689,1267,s;745,1299,b;521,1437,w;579,1445,r",
        "801,15,c;761,19,c;813,501,c;605,509,c;801,529,w;761,533,s;479,755,c;105,961,w;363,1075,c;409,1143,s;735,1241,f;823,1241,f;137,1277,f;813,1779,w;605,1787,c",
    };
    // </ground_lights>
}

public sealed class FrostGroundLights
{
    struct Point { public float u, v; public int kind; public char k; public uint seed; }

    readonly BackdropSet set;
    readonly BackdropTile tile;
    readonly Transform root;
    readonly SpriteRenderer[] pool;
    readonly int[] slotPoint;                       // per renderer this frame: the point it shows (tests), -1 off
    readonly int[] slotRow, slotCol;
    readonly Point[] points;
    Texture2D texture;
    Sprite sprite;
    float clock;

    public int PointCount => points.Length;
    public int Live { get; private set; }
    public int Overflow { get; private set; }       // lights in view this frame that found no renderer
    public float Clock => clock;

    public FrostGroundLights(BackdropSet set, BackdropTile tile, int variant, int order)
    {
        this.set = set;
        this.tile = tile;
        points = Parse(variant);
        root = new GameObject("GroundLights").transform;
        root.SetParent(set.Root, false);
        root.localPosition = new Vector3(0f, 0f, set.DepthZ("flow") - .001f);
        pool = new SpriteRenderer[points.Length > 0 ? FrostGroundLightsTuning.Pool : 0];
        slotPoint = new int[pool.Length];
        slotRow = new int[pool.Length];
        slotCol = new int[pool.Length];
        if (pool.Length == 0) return;
        sprite = MakeSprite(out texture);
        for (int i = 0; i < pool.Length; i++)
        {
            var go = new GameObject("light");
            go.transform.SetParent(root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            sr.enabled = false;
            pool[i] = sr;
            slotPoint[i] = -1;
        }
    }

    public static int Count(int variant) { return Parse(variant).Length; }

    static Point[] Parse(int variant)
    {
        var all = FrostGroundLightsTuning.Points;
        if (variant < 1 || variant >= all.Length || string.IsNullOrEmpty(all[variant])) return new Point[0];
        var items = all[variant].Split(';');
        var list = new System.Collections.Generic.List<Point>(items.Length);
        foreach (var it in items)
        {
            var f = it.Split(',');
            if (f.Length != 3 || f[2].Length != 1) continue;
            int kind = FrostGroundLightsTuning.Kinds.IndexOf(f[2][0]);
            if (kind < 0) continue;
            float x = float.Parse(f[0], System.Globalization.CultureInfo.InvariantCulture);
            float y = float.Parse(f[1], System.Globalization.CultureInfo.InvariantCulture);
            list.Add(new Point { u = (x + .5f) / 896f, v = 1f - (y + .5f) / 1792f, kind = kind, k = f[2][0], seed = Hash((uint)list.Count * 2654435761u + (uint)variant) });
        }
        return list.ToArray();
    }

    // A 7 x 7 pixel-art glow: a bright plus-shaped core, a soft ring, nothing in the corners. White: tinted per light.
    // (also the living props' lamps, FrostProps.cs)
    internal static Sprite MakeSprite(out Texture2D tex)
    {
        const int n = 7;
        tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "FrostGroundLight" };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = x - 3, dy = y - 3, d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = d < .5f ? 1f : d < 1.1f ? .9f : d < 1.5f ? .55f : d < 2.3f ? .28f : d < 3.2f ? .08f : 0f;
                byte w = (byte)(d < 1.1f ? 255 : 235);
                px[y * n + x] = new Color32(w, w, w, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), n, 0, SpriteMeshType.FullRect);   // 1 unit wide
    }

    public void Destroy()
    {
        if (sprite != null) BackdropAtlas.Kill(sprite);
        if (texture != null) BackdropAtlas.Kill(texture);
        sprite = null;
        texture = null;
    }

    // The light's alpha at time t (0..1 before the peak), from its kind and its own seed.
    public static float Level(int kind, uint seed, float t, float u, float v)
    {
        float r1 = Rand01(seed), r2 = Rand01(seed * 747796405u + 1u);
        switch (FrostGroundLightsTuning.Kinds[kind])
        {
            case 'w':
            {
                float n = Noise(seed, t * FrostGroundLightsTuning.WindowHz);
                return Mathf.Lerp(.82f, 1f, n);
            }
            case 'f':
            {
                float n = Noise(seed, t * FrostGroundLightsTuning.FireHz) * .6f + Noise(seed ^ 0x9e3779b9u, t * FrostGroundLightsTuning.FireHz * .5f) * .4f;
                return Mathf.Lerp(.6f, 1f, n);
            }
            case 'b':
            case 'c':
            {
                float hz = Mathf.Lerp(FrostGroundLightsTuning.BeaconHzMin, FrostGroundLightsTuning.BeaconHzMax, r1);
                float s = .5f - .5f * Mathf.Cos(2f * Mathf.PI * (t * hz + r2));
                return s * s;
            }
            case 'r':
            {
                // a chase along the ground: neighbours light one after the other (by their place on the tile)
                float p = FrostGroundLightsTuning.RimPeriod;
                float ph = t + (u * 7f + v * 3f) * FrostGroundLightsTuning.RimChase * 10f + r1 * .15f;
                float m = ph - Mathf.Floor(ph / p) * p;
                float on = FrostGroundLightsTuning.RimOn;
                return m < on ? Mathf.Sin(m / on * Mathf.PI) : 0f;
            }
            default:
            {
                float every = Mathf.Lerp(FrostGroundLightsTuning.StrobeEveryMin, FrostGroundLightsTuning.StrobeEveryMax, r1);
                float m = t + r2 * every;
                m -= Mathf.Floor(m / every) * every;
                float on = FrostGroundLightsTuning.StrobeOn;
                return m < on ? Mathf.Sin(m / on * Mathf.PI) : 0f;
            }
        }
    }

    public void Step(float dt)
    {
        clock += dt;
        if (pool.Length == 0 || tile == null || tile.TileHeight <= 0f) return;
        float w = tile.TileWidth, h = tile.TileHeight;
        float halfW = set.HalfWidth, halfH = set.HalfHeight;
        float size = FrostGroundLightsTuning.Size * w;
        float margin = size;
        double travel = tile.travel;
        int cMax = Mathf.CeilToInt((halfW + margin) / w) + 1;
        float fade = set.Alpha * FrostGroundLightsTuning.Brightness;
        int used = 0, over = 0;
        for (int i = 0; i < points.Length; i++)
        {
            var pt = points[i];
            // rows: y = -V/2 + (v + K) h - travel, K the ground row (stable for as long as it is in view)
            long k0 = (long)System.Math.Ceiling((travel - margin) / h - pt.v);
            long k1 = (long)System.Math.Floor((travel + 2.0 * halfH + margin) / h - pt.v);
            for (long K = k0; K <= k1; K++)
            {
                float y = (float)(-halfH + (pt.v + K) * h - travel);
                for (int c = -cMax; c <= cMax; c++)
                {
                    float x = (pt.u - .5f + c) * w;
                    if (x < -halfW - margin || x > halfW + margin) continue;
                    if (used >= pool.Length) { over++; continue; }
                    uint seed = Hash(pt.seed ^ Hash((uint)K * 73856093u ^ (uint)c * 19349663u));
                    float lvl = Level(pt.kind, seed, clock, pt.u, pt.v);
                    float lo = FrostGroundLightsTuning.Low[pt.kind], peak = FrostGroundLightsTuning.Peak[pt.kind];
                    float a = Mathf.Lerp(lo, peak, lvl) * fade;
                    var sr = pool[used];
                    slotPoint[used] = i;
                    slotRow[used] = (int)K;
                    slotCol[used] = c;
                    used++;
                    // placed on its lamp every frame, lit or not: a renderer handed to another lamp while dark must not keep the
                    // last lamp's spot (Probe reports where it is; FrostGroundLightsTest held every in-use renderer to its point)
                    var t = sr.transform;
                    t.localPosition = new Vector3(x, y, 0f);
                    if (a <= .004f) { if (sr.enabled) sr.enabled = false; continue; }
                    float s = size * FrostGroundLightsTuning.Scale[pt.kind] * (pt.k == 'r' || pt.k == 's' ? .85f + .15f * lvl : 1f);
                    t.localScale = new Vector3(s, s, 1f);
                    Color col = FrostGroundLightsTuning.ColorOf(pt.k);
                    col.a = a;
                    sr.color = col;
                    if (!sr.enabled) sr.enabled = true;
                }
            }
        }
        for (int i = used; i < pool.Length; i++)
        {
            slotPoint[i] = -1;
            if (pool[i].enabled) pool[i].enabled = false;
        }
        Live = used;
        Overflow = over;
    }

    // Renderer `i` this frame: the tile point it shows (u across, v up a copy: 0..1), where it is drawn (set-root local),
    // its colour and width. False when the renderer is not in use.
    public bool Probe(int i, out Vector2 uv, out Vector3 local, out Color color, out float width, out char kind)
    {
        uv = Vector2.zero; local = Vector3.zero; color = Color.clear; width = 0f; kind = ' ';
        if (i < 0 || i >= pool.Length || slotPoint[i] < 0) return false;
        var pt = points[slotPoint[i]];
        uv = new Vector2(pt.u, pt.v);
        local = pool[i].transform.localPosition;
        color = pool[i].enabled ? pool[i].color : new Color(0, 0, 0, 0);
        width = pool[i].transform.localScale.x;
        kind = pt.k;
        return true;
    }

    public int Capacity => pool.Length;
    public Transform Root => root;

    static uint Hash(uint x)
    {
        x ^= x >> 16; x *= 0x7feb352du;
        x ^= x >> 15; x *= 0x846ca68bu;
        x ^= x >> 16;
        return x;
    }

    static float Rand01(uint s) { return (Hash(s) & 0xffffff) / 16777216f; }

    // smooth value noise in 0..1 (one new value per unit of t)
    static float Noise(uint seed, float t)
    {
        float f = Mathf.Floor(t);
        uint i = (uint)(int)f;
        float a = Rand01(seed ^ Hash(i)), b = Rand01(seed ^ Hash(i + 1u));
        float x = t - f;
        x = x * x * (3f - 2f * x);
        return a + (b - a) * x;
    }
}
