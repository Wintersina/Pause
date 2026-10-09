using System.Collections.Generic;
using UnityEngine;

// The visibility treatment of an elite's or an ordinary enemy's shot
// (EliteShot): a thin, crisp outline traced from the shot drawing's own
// opaque pixels -- the same technique as the boss shots' rim (BossArt.
// ShotRim) -- instead of HostileGlow's round halo ("the weapon shots have 2
// large circles"). It hugs the silhouette, reaches only OutlineReach world
// units past it with a linear falloff, never swells (only its alpha
// pulses), and is drawn by one extra SpriteRenderer at the shot's own scale.
//
// Two tones, both a hairline: a light trace in the source's pale tint
// (HostileGlow.Tint: the dark nebulae) right at the drawing, and a dark
// hairline just outside it for the bright backdrops (Frost's snow and
// aurora, a flare) -- the job the round wrapper's dark ring did, now
// following the shot's shape.
//
// Built once per (drawing, drawn size) from the drawing's pixels, upsampled
// so the outline is finer than the art's own pixels, and cached: nothing is
// made per shot or per frame. The procedural shot drawings (EliteFxArt) are
// readable; anything else is read back once through ShieldContour.
public static class ShotOutline
{
    // ---- tuning ----
    public const float OutlineReach = .03f;   // world units past the silhouette (a boss bolt's rim: .06 x .46 = .028)
    public const float DarkShare = .4f;       // the outer share of the reach drawn as the dark hairline
    public const float Alpha = .95f;          // light trace's opacity at the silhouette (near opaque: the sky behind must not tint it)
    public const float DarkAlpha = .8f;       // the dark hairline's opacity
    public const float Coverage = .25f;       // alpha a pixel needs to count as the drawing
    public const int Upsample = 4;            // outline texels per art pixel
    public const float PulseScale = 0f;       // it never swells; only its alpha pulses (HostileGlow.PulseAlphaAt)

    sealed class Entry { public Sprite art; public Sprite rim; }
    static readonly Dictionary<long, Entry> cache = new Dictionary<long, Entry>();

    // The outline for `art` drawn `drawnSize` world units tall (its height
    // at the shot's scale). Null when the art can't be read.
    public static Sprite For(Sprite art, float drawnSize)
    {
        if (art == null || drawnSize <= 0f) return null;
        long key = ((long)art.GetInstanceID() << 16) ^ Mathf.RoundToInt(drawnSize * 1000f);
        Entry e;
        if (cache.TryGetValue(key, out e) && e.art == art && e.rim != null && e.rim.texture != null) return e.rim;
        var rim = Build(art, drawnSize);
        cache[key] = new Entry { art = art, rim = rim };
        return rim;
    }

    // World units of the outline past the drawing (tests).
    public static float ReachWorld => OutlineReach;

    static Color32[] Read(Sprite art, out int w, out int h)
    {
        var tex = art.texture;
        Rect r = art.rect;
        w = Mathf.RoundToInt(r.width);
        h = Mathf.RoundToInt(r.height);
        if (tex != null && tex.isReadable)
        {
            var all = tex.GetPixels32();
            var px = new Color32[w * h];
            int x0 = Mathf.RoundToInt(r.x), y0 = Mathf.RoundToInt(r.y);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) px[y * w + x] = all[(y0 + y) * tex.width + x0 + x];
            return px;
        }
        return ShieldContour.ReadPixels(art, out w, out h);
    }

    static Sprite Build(Sprite art, float drawnSize)
    {
        int w, h;
        var src = Read(art, out w, out h);
        if (src == null || w <= 0 || h <= 0) return null;
        int s = Upsample;
        // texels per world unit at this drawn size, and the reach in texels
        float texelsPerUnit = h * s / drawnSize;
        float reach = Mathf.Max(1.5f, OutlineReach * texelsPerUnit);
        int pad = Mathf.CeilToInt(reach) + 2;
        int W = w * s + 2 * pad, H = h * s + 2 * pad;
        const int Far = 1 << 20;
        var dist = new int[W * H];
        for (int i = 0; i < dist.Length; i++) dist[i] = Far;
        byte cut = (byte)Mathf.RoundToInt(Coverage * 255f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (src[y * w + x].a < cut) continue;
                for (int dy = 0; dy < s; dy++)
                    for (int dx = 0; dx < s; dx++) dist[(y * s + dy + pad) * W + x * s + dx + pad] = 0;
            }
        // a two-pass 3-4 chamfer distance (thirds of a texel)
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x, v = dist[i];
                if (x > 0) v = Mathf.Min(v, dist[i - 1] + 3);
                if (y > 0)
                {
                    v = Mathf.Min(v, dist[i - W] + 3);
                    if (x > 0) v = Mathf.Min(v, dist[i - W - 1] + 4);
                    if (x < W - 1) v = Mathf.Min(v, dist[i - W + 1] + 4);
                }
                dist[i] = v;
            }
        for (int y = H - 1; y >= 0; y--)
            for (int x = W - 1; x >= 0; x--)
            {
                int i = y * W + x, v = dist[i];
                if (x < W - 1) v = Mathf.Min(v, dist[i + 1] + 3);
                if (y < H - 1)
                {
                    v = Mathf.Min(v, dist[i + W] + 3);
                    if (x < W - 1) v = Mathf.Min(v, dist[i + W + 1] + 4);
                    if (x > 0) v = Mathf.Min(v, dist[i + W - 1] + 4);
                }
                dist[i] = v;
            }
        var px = new Color32[W * H];
        float lightEnd = reach * (1f - DarkShare);
        for (int i = 0; i < px.Length; i++)
        {
            float d = dist[i] / 3f;
            if (d > reach) { px[i] = new Color32(255, 255, 255, 0); continue; }
            if (d <= lightEnd)
            {
                // the light trace: full at (and under) the drawing, fading linearly
                float k = lightEnd > 0f ? 1f - d / lightEnd * .5f : 1f;
                px[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * Alpha * k));
            }
            else px[i] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(255f * DarkAlpha));   // the dark hairline
        }
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.name = art.name + "Outline";
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.hideFlags = HideFlags.HideAndDontSave;
        tex.SetPixels32(px);
        tex.Apply(false, false);   // kept readable: small, and the tests read it back
        // the same pivot and pixels-per-unit as the art (x Upsample), so at
        // local scale 1 under the shot it sits exactly on the drawing
        Vector2 pivot = new Vector2((art.pivot.x * s + pad) / W, (art.pivot.y * s + pad) / H);
        var rim = Sprite.Create(tex, new Rect(0, 0, W, H), pivot, art.pixelsPerUnit * s, 0, SpriteMeshType.FullRect);
        rim.name = tex.name;
        rim.hideFlags = HideFlags.HideAndDontSave;
        return rim;
    }
}
