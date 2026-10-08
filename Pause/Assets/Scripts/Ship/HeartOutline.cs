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

    sealed class Entry { public Sprite whole; public Sprite[] shards; }
    static readonly Dictionary<Sprite, Entry> cache = new Dictionary<Sprite, Entry>();

    // The outline for the whole heart drawing (null when its pixels can't be read).
    public static Sprite For(Sprite heart)
    {
        var e = Get(heart);
        return e != null ? e.whole : null;
    }

    // The outline for each of `cols` x `rows` crumble shards (row-major,
    // the same cells HeartOrbit cuts its shards from).
    public static Sprite[] ForShards(Sprite heart, int cols, int rows)
    {
        var e = Get(heart);
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
                                                       heart.pixelsPerUnit, heart.name + "OutlineShard" + (row * cols + col));
        }
        return e.shards;
    }

    static Entry Get(Sprite heart)
    {
        if (heart == null) return null;
        Entry e;
        if (cache.TryGetValue(heart, out e) && e != null && e.whole != null) return e;
        int w, h;
        var px = ShieldContour.ReadPixels(heart, out w, out h);
        if (px == null || px.Length < w * h) return null;
        e = new Entry { whole = Build(px, w, h, new RectInt(0, 0, w, h), heart.pixelsPerUnit, heart.name + "Outline") };
        cache[heart] = e;
        return e;
    }

    // The outline of the drawing's pixels inside `region` (texels of px),
    // on a canvas Pad texels bigger all round, centred like the region.
    static Sprite Build(Color32[] px, int w, int h, RectInt region, float ppu, string name)
    {
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
        float keyFull = KeyTexels * .5f;   // the keyline's inner half is solid, its outer half fades out
        for (int i = 0; i < outPx.Length; i++)
        {
            float d = dist[i] / 3f;
            if (d <= CoreTexels) { outPx[i] = Core; continue; }
            float k = d - CoreTexels;
            if (k > KeyTexels) { outPx[i] = new Color32(Key.r, Key.g, Key.b, 0); continue; }
            float a = k <= keyFull ? 1f : 1f - (k - keyFull) / Mathf.Max(.01f, KeyTexels - keyFull);   // linear
            outPx[i] = new Color32(Key.r, Key.g, Key.b, (byte)Mathf.RoundToInt(Key.a * Mathf.Clamp01(a)));
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
