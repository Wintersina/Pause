using System.Collections.Generic;
using UnityEngine;

// The outline the elites' and the bosses' hearts wear (HeartOrbit, with
// Outlined on), so a heart reads at a glance over any world and over the
// ship's own art: a thin crisp trace hugging the heart's shape, built once
// from the heart sprite's opaque pixels (the boss-shot rim technique,
// BossArt's shot rims: a chamfer distance from the silhouette, linear
// falloff) -- never a round halo.
//
// Two tones, both baked into the outline's texture (its renderer is only
// ever tinted white, its alpha pulsing slowly):
//   core     a light amber-white line right at the silhouette (CoreTexels
//            wide), bright against every world's dark backdrop -- not red
//            (red is the player's);
//   keyline  a thin dark line round that (KeyTexels, its outer half fading
//            out), so the trace holds over a bright patch too (Frost's ice,
//            a flare).
// BOLD style, for a world whose backdrop is drawn brightened (Frost:
// BackdropCatalog.Spec.brightness, its pale cloud ceiling and lifted ice), or
// whose art is painted bright (Spec.brightArt: Verdant's lit jungle):
// a slightly thinner core and a wider, solid keyline, so the dark ring
// carries the trace over pale cloud as the light core does over dark
// ground. Same reach (<= 8% of the cell); the dark worlds keep the
// standard trace untouched.
// Under the drawing itself the outline is solid core colour (hidden by the
// heart, so its soft edge never shows a gap).
//
// One sprite per heart drawing, plus one per crumble shard (each shard's
// own piece of the silhouette), all cached: building costs a read-back and
// a few small textures once; drawing costs nothing extra a frame.
public static class HeartOutline
{
    // In the heart sprite's own texels (64 across the heart's cell: about
    // 0.86 screen px a texel for a boss heart, 0.6 for an elite's on a phone).
    public const float CoreTexels = 2.5f;
    public const float KeyTexels = 2f;
    public const int Pad = 6;                       // clear texels round the cell (must exceed the reach)
    public const float Coverage = .25f;             // alpha a texel needs to count as the drawing
    public static readonly Color32 Core = new Color32(255, 244, 210, 255);
    public static readonly Color32 Key = new Color32(14, 10, 32, 235);
    // The outline's slow alpha pulse (it never swells: its scale is the heart's).
    public const float PulseMin = .78f, PulseHz = .55f;
    // How far past the drawing the trace reaches, as a share of the heart's cell.
    public static float ReachShare(int cellTexels) => (CoreTexels + KeyTexels) / Mathf.Max(1, cellTexels);
    // The bold style (bright backdrops).
    public const float BoldCoreTexels = 2f, BoldKeyTexels = 3f, BoldKeySolid = 1f;
    public static readonly Color32 BoldKey = new Color32(10, 8, 26, 255);
    public static float BoldReachShare(int cellTexels) => (BoldCoreTexels + BoldKeyTexels) / Mathf.Max(1, cellTexels);
    // A world's backdrop counts as bright when it is lifted by this much.
    public const float BrightLift = BackdropCatalog.Spec.BrightLift;

    // Bold outlines for the current world? (Tests may force it: Bold.)
    public static bool? Bold;
    public static bool UseBold
    {
        get
        {
            if (Bold.HasValue) return Bold.Value;
            return BackdropCatalog.CurrentIsBright;
        }
    }

    sealed class Entry { public Sprite whole; public Sprite[] shards; public bool bold; }
    static readonly Dictionary<Sprite, Entry> cache = new Dictionary<Sprite, Entry>();
    static readonly Dictionary<Sprite, Entry> boldCache = new Dictionary<Sprite, Entry>();

    // The outline for the whole heart drawing (null when its pixels can't be
    // read), in the current world's style.
    public static Sprite For(Sprite heart) { return For(heart, UseBold); }

    public static Sprite For(Sprite heart, bool bold)
    {
        var e = Get(heart, bold);
        return e != null ? e.whole : null;
    }

    // The outline for each of `cols` x `rows` crumble shards (row-major,
    // the same cells HeartOrbit cuts its shards from).
    public static Sprite[] ForShards(Sprite heart, int cols, int rows)
    {
        var e = Get(heart, UseBold);
        if (e == null) return null;
        if (e.shards == null || e.shards.Length != cols * rows || e.shards[0] == null)
        {
            int w, h;
            var px = ShieldContour.ReadPixels(heart, out w, out h);
            if (px == null) return null;
            e.shards = new Sprite[cols * rows];
            int sw = w / cols, sh = h / rows;
            for (int row = 0; row < rows; row++)
                for (int col = 0; col < cols; col++)
                    e.shards[row * cols + col] = Build(px, w, h, new RectInt(col * sw, row * sh, sw, sh),
                                                       heart.pixelsPerUnit, heart.name + "OutlineShard" + (row * cols + col), e.bold);
        }
        return e.shards;
    }

    static Entry Get(Sprite heart, bool bold)
    {
        if (heart == null) return null;
        var table = bold ? boldCache : cache;
        Entry e;
        if (table.TryGetValue(heart, out e) && e != null && e.whole != null) return e;
        int w, h;
        var px = ShieldContour.ReadPixels(heart, out w, out h);
        if (px == null || px.Length < w * h) return null;
        e = new Entry { bold = bold,
                        whole = Build(px, w, h, new RectInt(0, 0, w, h), heart.pixelsPerUnit, heart.name + (bold ? "OutlineBold" : "Outline"), bold) };
        table[heart] = e;
        return e;
    }

    // The outline of the drawing's pixels inside `region` (texels of px),
    // on a canvas Pad texels bigger all round, centred like the region.
    static Sprite Build(Color32[] px, int w, int h, RectInt region, float ppu, string name, bool bold)
    {
        float coreT = bold ? BoldCoreTexels : CoreTexels, keyT = bold ? BoldKeyTexels : KeyTexels;
        Color32 key = bold ? BoldKey : Key;
        int rw = region.width, rh = region.height;
        int sw = rw + 2 * Pad, sh = rh + 2 * Pad;
        const int Far = 1 << 20;
        var dist = new int[sw * sh];
        for (int i = 0; i < dist.Length; i++) dist[i] = Far;
        for (int y = 0; y < rh; y++)
            for (int x = 0; x < rw; x++)
            {
                int sx = region.x + x, sy = region.y + y;
                if (sx < 0 || sy < 0 || sx >= w || sy >= h) continue;
                if (px[sy * w + sx].a >= Coverage * 255f) dist[(y + Pad) * sw + x + Pad] = 0;
            }

        // a two-pass 3-4 chamfer (thirds of a texel)
        for (int y = 0; y < sh; y++)
            for (int x = 0; x < sw; x++)
            {
                int i = y * sw + x, v = dist[i];
                if (x > 0) v = Mathf.Min(v, dist[i - 1] + 3);
                if (y > 0)
                {
                    v = Mathf.Min(v, dist[i - sw] + 3);
                    if (x > 0) v = Mathf.Min(v, dist[i - sw - 1] + 4);
                    if (x < sw - 1) v = Mathf.Min(v, dist[i - sw + 1] + 4);
                }
                dist[i] = v;
            }
        for (int y = sh - 1; y >= 0; y--)
            for (int x = sw - 1; x >= 0; x--)
            {
                int i = y * sw + x, v = dist[i];
                if (x < sw - 1) v = Mathf.Min(v, dist[i + 1] + 3);
                if (y < sh - 1)
                {
                    v = Mathf.Min(v, dist[i + sw] + 3);
                    if (x < sw - 1) v = Mathf.Min(v, dist[i + sw + 1] + 4);
                    if (x > 0) v = Mathf.Min(v, dist[i + sw - 1] + 4);
                }
                dist[i] = v;
            }

        var outPx = new Color32[sw * sh];
        // the keyline's inner half is solid, its outer half fades out (bold: all solid)
        float keyFull = keyT * (bold ? BoldKeySolid : .5f);
        for (int i = 0; i < outPx.Length; i++)
        {
            float d = dist[i] / 3f;
            if (d <= coreT) { outPx[i] = Core; continue; }
            float k = d - coreT;
            if (k > keyT) { outPx[i] = new Color32(key.r, key.g, key.b, 0); continue; }
            float a = k <= keyFull ? 1f : 1f - (k - keyFull) / Mathf.Max(.01f, keyT - keyFull);   // linear (keyFull == keyT: solid)
            outPx[i] = new Color32(key.r, key.g, key.b, (byte)Mathf.RoundToInt(key.a * Mathf.Clamp01(a)));
        }

        var tex = new Texture2D(sw, sh, TextureFormat.RGBA32, false);
        tex.name = name;
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.hideFlags = HideFlags.HideAndDontSave;
        tex.SetPixels32(outPx);
        tex.Apply(false, false);   // kept readable: tiny, and the tests read it back
        var s = Sprite.Create(tex, new Rect(0, 0, sw, sh), new Vector2(.5f, .5f), ppu, 0, SpriteMeshType.FullRect);
        s.name = name;
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }

    // The outline's alpha at `seconds` of the owner's clock: a slow pulse.
    public static float Pulse(float seconds)
    {
        return Mathf.Lerp(PulseMin, 1f, .5f + .5f * Mathf.Sin(seconds * PulseHz * 2f * Mathf.PI));
    }
}
