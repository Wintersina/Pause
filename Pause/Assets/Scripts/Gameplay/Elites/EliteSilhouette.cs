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
//
// BAKED. The reach is a share of the cell (EliteDef.silhouetteShare), measured from the strips' pixels at edit
// time by EliteSilhouetteBaker and kept in the def's JSON, so a spawn reads no pixel. It used to be measured
// on the elite's first appearance: one GPU read-back per drawing of the strip (15-20 of them, 9-12 MB of
// managed arrays in the spawn's frame). A def with no bake still measures (the safety net, reported to
// PerfProbe; PerfHitchTest and HeartsInArtTest fail on a missing or a stale bake).
public static class EliteSilhouette
{
    static readonly Dictionary<string, float> cache = new Dictionary<string, float>();

    public static void ClearCache() { cache.Clear(); }

    // Defs measured at runtime because they had no bake (0 in a shipped build).
    public static int Unbaked { get; private set; }

    // The silhouette's radius (world units, hull scale 1); a guess from the
    // collider radius when the strip is missing or unreadable.
    public static float Radius(EliteDef def)
    {
        if (def == null) return .5f;
        if (def.silhouetteShare > 0f) return def.silhouetteShare * def.cellWorldSize;
        float r;
        if (cache.TryGetValue(def.key, out r)) return r;
        Unbaked++;
        r = MeasureShare(def) * def.cellWorldSize;
        if (r <= 0f) return def.hullRadius * 1.3f;   // (not cached: the art may arrive)
        cache[def.key] = r;
        return r;
    }

    // The reach as a share of the cell's height, read from every drawing of the strip (the baker and the
    // unbaked fallback). 0 when the strip is missing or unreadable.
    public static float MeasureShare(EliteDef def)
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
            float cx = w * .5f, cy = h * .5f, far = 0f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (px[y * w + x].a < 128) continue;
                    float dx = x + .5f - cx, dy = y + .5f - cy;
                    far = Mathf.Max(far, dx * dx + dy * dy);
                }
            best = Mathf.Max(best, Mathf.Sqrt(far) / Mathf.Max(1, h));
        }
        return best;
    }
}
