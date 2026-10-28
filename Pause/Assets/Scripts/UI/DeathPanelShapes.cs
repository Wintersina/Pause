using System.Collections.Generic;
using UnityEngine;

// Procedural plates for the flight-complete panel when a world's frame wants its own corner cut
// (DeathPanelView.CornerCut): the banner slab, the card background and the button frame are drawn as
// octagons whose diagonal cuts have the frame's angle (45 degrees) and size. Same palette and rim width
// as the shipped dp_slab / dp_card / dp_button; 200 px per unit like those.
public static class DeathPanelShapes
{
    const float PxPerUnit = 2f;                  // the shipped sprites are 200 ppu on a 100 ppu canvas
    const float Rim = 6f;                        // ink rim, px
    static readonly Color32 Ink = new Color32(20, 12, 20, 255);
    static readonly Color32 CardFill = new Color32(21, 27, 48, 255);
    static readonly Color32 ButtonFill = new Color32(44, 51, 70, 255);
    static readonly Color32 Red = new Color32(216, 35, 44, 255);
    static readonly Color32 RedLight = new Color32(255, 91, 69, 255);
    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    // Signed distance (px, positive inside) of an octagon: the rect [x0,x1]x[y0,y1] (y down) with 45 degree
    // cuts of the given legs (px) at top-left, top-right, bottom-right, bottom-left.
    static float Oct(float x, float y, float x0, float y0, float x1, float y1, float tl, float tr, float br, float bl)
    {
        const float r2 = 0.70710678f;
        float d = Mathf.Min(Mathf.Min(x - x0, x1 - x), Mathf.Min(y - y0, y1 - y));
        if (tl > 0) d = Mathf.Min(d, ((x - x0) + (y - y0) - tl) * r2);
        if (tr > 0) d = Mathf.Min(d, ((x1 - x) + (y - y0) - tr) * r2);
        if (br > 0) d = Mathf.Min(d, ((x1 - x) + (y1 - y) - br) * r2);
        if (bl > 0) d = Mathf.Min(d, ((x - x0) + (y1 - y) - bl) * r2);
        return d;
    }

    static float Cov(float d) { return Mathf.Clamp01(d + .5f); }

    struct Px { public float r, g, b, a; }
    static void Over(ref Px p, Color32 c, float alpha)
    {
        float ca = alpha * c.a / 255f;
        if (ca <= 0f) return;
        float oa = ca + p.a * (1f - ca);
        p.r = (c.r / 255f * ca + p.r * p.a * (1f - ca)) / oa;
        p.g = (c.g / 255f * ca + p.g * p.a * (1f - ca)) / oa;
        p.b = (c.b / 255f * ca + p.b * p.a * (1f - ca)) / oa;
        p.a = oa;
    }

    delegate Px Shade(float x, float y);

    static Sprite Build(string key, int w, int h, Vector4 border, Shade shade)
    {
        Sprite s;
        if (cache.TryGetValue(key, out s) && s != null) return s;
        var px = new Color32[w * h];
        for (int row = 0; row < h; row++)
            for (int x = 0; x < w; x++)
            {
                var p = shade(x + .5f, row + .5f);
                px[(h - 1 - row) * w + x] = new Color32((byte)Mathf.RoundToInt(p.r * 255f), (byte)Mathf.RoundToInt(p.g * 255f),
                                                        (byte)Mathf.RoundToInt(p.b * 255f), (byte)Mathf.RoundToInt(p.a * 255f));
            }
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        tex.Apply(false, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.name = key;
        s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 100f * PxPerUnit, 0, SpriteMeshType.FullRect, border);
        s.name = key;
        cache[key] = s;
        return s;
    }

    // Card background: 9-sliced, all four corners cut by `leg` units.
    public static Sprite Card(float leg)
    {
        float l = leg * PxPerUnit;
        int b = Mathf.CeilToInt(Mathf.Max(52f, l + Rim + 4f));
        int n = 2 * b + 8;
        return Build("dp_card_cut" + Mathf.RoundToInt(leg), n, n, new Vector4(b, b, b, b), (x, y) =>
        {
            var p = new Px();
            float d = Oct(x, y, 0, 0, n, n, l, l, l, l);
            Over(ref p, Ink, Cov(d));
            Over(ref p, CardFill, Cov(d - Rim));
            return p;
        });
    }

    // Button frame: 9-sliced; the accent slab (white, tinted by the Image colour) peeks out bottom and right.
    public static Sprite Button(float leg, float fillAlpha)
    {
        float l = leg * PxPerUnit;
        const float off = 12f;
        int b = Mathf.CeilToInt(Mathf.Max(48f, l + Rim + off + 4f));
        int n = 2 * b + 16;
        return Build("dp_button_cut" + Mathf.RoundToInt(leg), n, n, new Vector4(b, b, b, b), (x, y) =>
        {
            var p = new Px();
            float slab = Oct(x, y, off, off, n, n, l, l, l, l);
            float body = Oct(x, y, 0, 0, n - off, n - off, l, l, l, l);
            float co = Cov(body), ci = Cov(body - Rim);
            Over(ref p, new Color32(255, 255, 255, 255), Cov(slab) * (1f - co));
            Over(ref p, Ink, co * (1f - ci));
            Over(ref p, ButtonFill, ci * fillAlpha);
            return p;
        });
    }

    // The title slab: body of bodyW x bodyH units with its ink shadow (shadow units to the right and down);
    // the top corners are cut by topLeg, the bottom ones by bottomLeg. The sprite is (bodyW+shadow) x (bodyH+shadow).
    public static Sprite Banner(float bodyW, float bodyH, float shadow, float topLeg, float bottomLeg)
    {
        float bw = bodyW * PxPerUnit, bh = bodyH * PxPerUnit, sh = shadow * PxPerUnit;
        float tl = topLeg * PxPerUnit, bl = bottomLeg * PxPerUnit, hl = 8f * PxPerUnit;
        int w = Mathf.CeilToInt(bw + sh), h = Mathf.CeilToInt(bh + sh);
        string key = "dp_slab_cut" + Mathf.RoundToInt(bodyW) + "_" + Mathf.RoundToInt(bodyH) + "_" + Mathf.RoundToInt(topLeg) + "_" + Mathf.RoundToInt(bottomLeg);
        return Build(key, w, h, Vector4.zero, (x, y) =>
        {
            var p = new Px();
            float shadowD = Oct(x, y, sh, sh, bw + sh, bh + sh, tl, tl, bl, bl);
            float body = Oct(x, y, 0, 0, bw, bh, tl, tl, bl, bl);
            float co = Cov(body), ci = Cov(body - Rim);
            Over(ref p, Ink, Cov(shadowD) * (1f - co));
            Over(ref p, Ink, co * (1f - ci));
            Over(ref p, y < Rim + hl ? RedLight : Red, ci);
            return p;
        });
    }
}
