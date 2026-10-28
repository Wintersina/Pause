using System.Collections.Generic;
using UnityEngine;

// Plates of the flight-complete panel that are not shipped sprites: the title banner (a plain rectangle with
// the buttons' 45 degree cut at its top-left and bottom-right corners, sized per world to clear its frame) and
// the cards' mirrored background (dp_card is cut top-right / bottom-left; the panel wants top-left / bottom-right,
// like dp_button). Same palette and rim as the shipped sprites; 200 px per unit like them.
public static class DeathPanelShapes
{
    const float PxPerUnit = 2f;
    const float Rim = 6f;                        // ink rim, px
    static readonly Color32 Ink = new Color32(20, 12, 20, 255);
    static readonly Color32 Red = new Color32(216, 35, 44, 255);
    static readonly Color32 RedLight = new Color32(255, 91, 69, 255);
    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
    static Sprite mirroredCard, mirroredSource;

    // Signed distance (px, positive inside) of a rectangle with 45 degree cuts of `leg` px at its top-left
    // and bottom-right corners (y down).
    static float Cut(float x, float y, float w, float h, float leg)
    {
        const float r2 = 0.70710678f;
        float d = Mathf.Min(Mathf.Min(x, w - x), Mathf.Min(y, h - y));
        d = Mathf.Min(d, (x + y - leg) * r2);
        d = Mathf.Min(d, ((w - x) + (h - y) - leg) * r2);
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

    // The title banner: body of bodyW x bodyH units plus its ink drop shadow (`shadow` units right and down);
    // the sprite is (bodyW+shadow) x (bodyH+shadow). Top-left / bottom-right cut by `leg` units.
    public static Sprite Banner(float bodyW, float bodyH, float shadow, float leg)
    {
        float bw = bodyW * PxPerUnit, bh = bodyH * PxPerUnit, sh = shadow * PxPerUnit, l = leg * PxPerUnit;
        float band = 8f * PxPerUnit;   // the lighter top band
        int w = Mathf.CeilToInt(bw + sh), h = Mathf.CeilToInt(bh + sh);
        string key = "dp_slab_cut" + Mathf.RoundToInt(bodyW) + "_" + Mathf.RoundToInt(bodyH) + "_" + Mathf.RoundToInt(leg);
        Sprite s;
        if (cache.TryGetValue(key, out s) && s != null) return s;
        var px = new Color32[w * h];
        for (int row = 0; row < h; row++)
            for (int x = 0; x < w; x++)
            {
                var p = new Px();
                float fx = x + .5f, fy = row + .5f;
                float shadowD = Cut(fx - sh, fy - sh, bw, bh, l);
                float body = Cut(fx, fy, bw, bh, l);
                float co = Cov(body), ci = Cov(body - Rim);
                Over(ref p, Ink, Cov(shadowD) * (1f - co));
                Over(ref p, Ink, co * (1f - ci));
                Over(ref p, fy < Rim + band ? RedLight : Red, ci);
                px[(h - 1 - row) * w + x] = new Color32((byte)Mathf.RoundToInt(p.r * 255f), (byte)Mathf.RoundToInt(p.g * 255f),
                                                        (byte)Mathf.RoundToInt(p.b * 255f), (byte)Mathf.RoundToInt(p.a * 255f));
            }
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        tex.Apply(false, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.name = key;
        s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 100f * PxPerUnit, 0, SpriteMeshType.FullRect);
        s.name = key;
        cache[key] = s;
        return s;
    }

    // dp_card flipped left-right: same pixels, the cuts move to top-left / bottom-right, the 9-slice's
    // left and right borders swap. Cached; the Blit + ReadPixels copy is done once.
    public static Sprite MirroredCard(Sprite source)
    {
        if (source == null) return null;
        if (mirroredCard != null && mirroredSource == source) return mirroredCard;
        var tex = source.texture;
        var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var prev = RenderTexture.active;
        Graphics.Blit(tex, rt);
        RenderTexture.active = rt;
        var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        var px = copy.GetPixels32();
        int w = tex.width, h = tex.height;
        var flipped = new Color32[px.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) flipped[y * w + x] = px[y * w + (w - 1 - x)];
        copy.SetPixels32(flipped);
        copy.Apply(false, false);
        copy.wrapMode = TextureWrapMode.Clamp;
        copy.filterMode = tex.filterMode;
        copy.name = "dp_card_mirrored";
        var b = source.border;
        mirroredSource = source;
        mirroredCard = Sprite.Create(copy, new Rect(0, 0, w, h), new Vector2(.5f, .5f), source.pixelsPerUnit, 0,
                                     SpriteMeshType.FullRect, new Vector4(b.z, b.y, b.x, b.w));
        mirroredCard.name = "dp_card";
        return mirroredCard;
    }
}
