using System.Collections.Generic;
using UnityEngine;

// How far an elite's drawn hull reaches from its centre: the circle through
// the furthest opaque pixel (alpha >= 0.5) of any of its drawings, in world
// units at hull scale 1. EliteHearts orbits outside it, so a heart never
// sails through the art on the big hulls (the 384 px Space / Frost strips at
// their cellWorldSize, the armoured ones), and hides behind it only inside it.
//
// A circle, not an ellipse: most elites turn to face their heading, and a
// circle about the centre is the same silhouette whatever the drawing's turn.
// Measured once per def from the art itself (ShieldContour.ReadPixels: the
// texture if readable, else one GPU read-back per drawing) and cached, so the
// only cost is the elite's first appearance. EliteHeartsTest re-measures the
// radius from the PNG on disk.
public static class EliteSilhouette
{
    static readonly Dictionary<string, float> cache = new Dictionary<string, float>();

    public static void ClearCache() { cache.Clear(); }

    // The silhouette's radius (world units, hull scale 1); a guess from the
    // collider radius when the strip is missing or unreadable.
    public static float Radius(EliteDef def)
    {
        if (def == null) return .5f;
        float r;
        if (cache.TryGetValue(def.key, out r)) return r;
        r = Measure(def);
        if (r <= 0f) return def.hullRadius * 1.3f;   // (not cached: the art may arrive)
        cache[def.key] = r;
        return r;
    }

    static float Measure(EliteDef def)
    {
        var frames = EliteArt.Frames(def);
        if (frames == null) return 0f;
        float best = 0f;
        foreach (var f in frames)
        {
            if (f == null) continue;
            int w, h;
            var px = ShieldContour.ReadPixels(f, out w, out h);
            if (px == null) continue;
            float pixelWorld = def.cellWorldSize / Mathf.Max(1, h);
            float cx = w * .5f, cy = h * .5f, far = 0f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (px[y * w + x].a < 128) continue;
                    float dx = x + .5f - cx, dy = y + .5f - cy;
                    far = Mathf.Max(far, dx * dx + dy * dy);
                }
            best = Mathf.Max(best, Mathf.Sqrt(far) * pixelWorld);
        }
        return best;
    }
}
