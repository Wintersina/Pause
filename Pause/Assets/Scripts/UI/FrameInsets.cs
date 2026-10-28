using UnityEngine;

// How far a death-panel frame's top / bottom border art reaches into the
// panel, in canvas units, measured from the frame sprite itself: each column
// of the middle band is scanned inward from the edge for the last pixel that
// differs from the flat fill; the 60th percentile of those depths is the
// border's body (a few spikes and crystals reach further, a few columns are
// bare). The 9-slice border is drawn at its own scale and anything beyond it
// at the stretched middle's scale. Unreadable textures fall back to a table
// measured from the shipped art (FrameInsetTest checks the two agree).
public static class FrameInsets
{
    const float ReferencePixelsPerUnit = 100f;
    const int Threshold = 60;      // summed RGB difference from the fill
    const float Percentile = .6f;
    // Pixel depths measured from the shipped art: Space, Frost, Verdant, Ember, Tide, Storm, Desert.
    static readonly int[] TopFallback = { 67, 83, 81, 87, 95, 107, 104 };
    static readonly int[] BottomFallback = { 61, 83, 100, 87, 108, 121, 82 };

    public static int MeasureTopPixels(Texture2D t, out bool ok) { return Measure(t, true, out ok); }
    public static int MeasureBottomPixels(Texture2D t, out bool ok) { return Measure(t, false, out ok); }

    static int Measure(Texture2D t, bool top, out bool ok)
    {
        ok = false;
        if (t == null || !t.isReadable) return 0;
        int w = t.width, h = t.height;
        Color32[] px;
        try { px = t.GetPixels32(); } catch (System.Exception) { return 0; }
        int x0 = (int)(w * .2f), x1 = (int)(w * .8f);
        int reach = (int)(h * .3f);
        int refRow = top ? (int)(h * .29f) : (int)(h * .71f);
        var depths = new System.Collections.Generic.List<int>();
        for (int x = x0; x < x1; x++)
        {
            Color32 r = px[refRow * w + x];
            int last = 0;
            for (int k = 0; k < reach; k++)
            {
                int y = top ? h - 1 - k : k;   // texture rows run bottom-up
                Color32 p = px[y * w + x];
                int d = Mathf.Abs(p.r - r.r) + Mathf.Abs(p.g - r.g) + Mathf.Abs(p.b - r.b);
                if (d > Threshold) last = k + 1;
            }
            depths.Add(last);
        }
        depths.Sort();
        ok = true;
        return depths[Mathf.Min(depths.Count - 1, (int)(depths.Count * Percentile))];
    }

    // Pixel depth to canvas units under the sliced draw (borders at 1/borderScale).
    public static float PixelsToUnits(Sprite s, int px, float borderScale, float frameHeight, bool top)
    {
        float ppu = s != null ? s.pixelsPerUnit : 100f;
        float perPx = ReferencePixelsPerUnit / ppu / borderScale;
        float border = s != null ? (top ? s.border.w : s.border.y) : 100f;
        if (px <= border) return px * perPx;
        float texH = s != null ? s.rect.height : 900f;
        float middleScale = (frameHeight - 2f * border * perPx) / Mathf.Max(1f, texH - 2f * border);
        return border * perPx + (px - border) * middleScale;
    }

    static readonly System.Collections.Generic.Dictionary<int, int> topCache = new System.Collections.Generic.Dictionary<int, int>();
    static readonly System.Collections.Generic.Dictionary<int, int> bottomCache = new System.Collections.Generic.Dictionary<int, int>();

    static int Pixels(Sprite s, int world, bool top)
    {
        var cache = top ? topCache : bottomCache;
        int key = s != null ? s.GetInstanceID() : 0;
        int v;
        if (s != null && cache.TryGetValue(key, out v)) return v;
        bool ok = false;
        v = s != null ? Measure(s.texture, top, out ok) : 0;
        if (s == null || !ok)
        {
            var table = top ? TopFallback : BottomFallback;
            v = table[Mathf.Clamp(world, 0, table.Length - 1)];
        }
        if (s != null) cache[key] = v;
        return v;
    }

    // ---- Side clearance: how far the frame's art reaches in from the left / right edge ----
    // For every texture row, the innermost column (scanning from the centre
    // outward) that differs from the flat fill; measured once per sprite and
    // cached, so asking for a clearance later allocates nothing. Used by the
    // tests and the capture tool to keep the cards clear of icicles, vines,
    // coral and the like (the median border depth above does not see those).
    sealed class SideEdges { public int[] left, right; public bool ok; }
    static readonly System.Collections.Generic.Dictionary<int, SideEdges> sideCache = new System.Collections.Generic.Dictionary<int, SideEdges>();

    static SideEdges Edges(Sprite s)
    {
        int key = s.GetInstanceID();
        SideEdges e;
        if (sideCache.TryGetValue(key, out e)) return e;
        e = new SideEdges();
        var t = s.texture;
        if (t != null && t.isReadable)
        {
            Color32[] px = null;
            try { px = t.GetPixels32(); } catch (System.Exception) { }
            if (px != null)
            {
                int w = t.width, h = t.height;
                e.left = new int[h]; e.right = new int[h];
                for (int row = 0; row < h; row++)
                {
                    // texture rows run bottom-up; the reference is the fill on the middle band's nearest row
                    int refRow = Mathf.Clamp(row, (int)(h * .29f), (int)(h * .71f));
                    Color32 r = px[refRow * w + w / 2];
                    int l = -1, rr = w;
                    for (int x = w / 2; x >= 0; x--)
                    {
                        Color32 p = px[row * w + x];
                        if (Mathf.Abs(p.r - r.r) + Mathf.Abs(p.g - r.g) + Mathf.Abs(p.b - r.b) + Mathf.Abs(p.a - r.a) > Threshold) { l = x; break; }
                    }
                    for (int x = w / 2; x < w; x++)
                    {
                        Color32 p = px[row * w + x];
                        if (Mathf.Abs(p.r - r.r) + Mathf.Abs(p.g - r.g) + Mathf.Abs(p.b - r.b) + Mathf.Abs(p.a - r.a) > Threshold) { rr = x; break; }
                    }
                    e.left[row] = l; e.right[row] = rr;
                }
                e.ok = true;
            }
        }
        sideCache[key] = e;
        return e;
    }

    // Minimum horizontal gap (canvas units) between `content` (panel space, origin at the frame's
    // centre) and the frame art's innermost painted pixel on the given side, over the texture rows
    // the content spans. Negative: art reaches into the content. NaN: sprite unreadable.
    // The frame is drawn frameW x frameH, 9-sliced with borders at 1/borderScale of the sprite's own density.
    public static float SideClearance(Sprite s, float borderScale, float frameW, float frameH, Rect content, bool left)
    {
        if (s == null) return float.NaN;
        var e = Edges(s);
        if (!e.ok) return float.NaN;
        int w = (int)s.rect.width, h = (int)s.rect.height;
        float perPx = ReferencePixelsPerUnit / s.pixelsPerUnit / borderScale;
        float bl = s.border.x, br = s.border.z, bt = s.border.w, bb = s.border.y;
        float midX = (frameW - (bl + br) * perPx) / Mathf.Max(1f, w - bl - br);
        float midY = (frameH - (bt + bb) * perPx) / Mathf.Max(1f, h - bt - bb);
        float best = float.MaxValue;
        for (int row = 0; row < h; row++)
        {
            // unit y of the row's centre, panel space (origin at the frame's centre, y up)
            float fromBottom;
            if (row + .5f < bb) fromBottom = (row + .5f) * perPx;
            else if (row + .5f >= h - bt) fromBottom = frameH - (h - row - .5f) * perPx;
            else fromBottom = bb * perPx + (row + .5f - bb) * midY;
            float y = fromBottom - frameH * .5f;
            if (y < content.yMin || y > content.yMax) continue;
            int col = left ? e.left[row] : e.right[row];
            if (col < 0 || col >= w) continue;   // no art on this row
            // unit x of the art's inner side of that column
            float c = left ? col + 1f : col;
            float x;
            if (c <= bl) x = c * perPx;
            else if (c >= w - br) x = frameW - (w - c) * perPx;
            else x = bl * perPx + (c - bl) * midX;
            x -= frameW * .5f;
            float gap = left ? content.xMin - x : x - content.xMax;
            if (gap < best) best = gap;
        }
        return best == float.MaxValue ? 1e4f : best;
    }

    public static float Top(Sprite frame, int world, float borderScale, float frameWidth, float frameHeight)
    {
        return PixelsToUnits(frame, Pixels(frame, world, true), borderScale, frameHeight, true);
    }

    public static float Bottom(Sprite frame, int world, float borderScale, float frameWidth, float frameHeight)
    {
        return PixelsToUnits(frame, Pixels(frame, world, false), borderScale, frameHeight, false);
    }
}
