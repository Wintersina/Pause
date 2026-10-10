using UnityEngine;

// THE PROCEDURAL BODIES OF THE PROJECTILE BEHAVIOURS (plan phase 1f; EliteShots.cs ShotMotions, ShotMotion flags).
//
// A world phase turns a behaviour on by giving a (world, kind) skin a ShotMotion flag; the drawing is the world's
// shots atlas when it is delivered (AttackArt cells) and, until then, these pixel sprites: a slug (Streak, also the
// ice spear of Shatter), a leaf (Flutter), a crescent (Slash), a log (Roll) and a pod (Burst). All of them follow the
// pink-cue contract (design 0.1): a pink-white edge stroke, a pink body line, a flickering pink-white core ridge,
// at most half material pixels (PC3), pointed or spiked silhouettes (PC4, no smooth disc), stepped frames (PC5).
//
// Shapes are masks; the shading is by depth from the rim (1 = edge, 2 = body, the deepest rows = core, the rest the
// world's material), so a new shape is one inside-test. Sprites are made once per (piece, world, frame) and cached.
//
//   ShotMotionArt.Skin(world, kind, ShotMotion.Flutter)   a ready ShotSkin for a test, a preview or a world's fallback
public static class ShotMotionArt
{
    public enum Piece { Slug, Leaf, Crescent, Log, Pod }
    public const float Ppu = 32f;

    public static Piece PieceOf(ShotMotion m)
    {
        if ((m & ShotMotion.Flutter) != 0) return Piece.Leaf;
        if ((m & ShotMotion.Slash) != 0) return Piece.Crescent;
        if ((m & ShotMotion.Roll) != 0) return Piece.Log;
        if ((m & ShotMotion.Burst) != 0) return Piece.Pod;
        return Piece.Slug;   // Streak, Shatter
    }

    // A skin for `kind` in `world` wearing the procedural body of `motion`.
    public static ShotSkin Skin(int world, EliteShots.Kind kind, ShotMotion motion)
    {
        var piece = PieceOf(motion);
        return new ShotSkin
        {
            world = world, kind = kind, motion = motion, procedural = false, drawScale = AttackArt.ThemedDrawScale, frameFps = 10f,
            a = Sprite_(piece, world, 0), b = Sprite_(piece, world, 1),
        };
    }

    public static Sprite Sprite_(Piece piece, int world, int frame)
    {
        int f = frame & 1;
        int w, h;
        System.Func<int, int, bool> inside;
        switch (piece)
        {
            case Piece.Leaf: w = 14; h = 20; inside = Leaf; break;
            case Piece.Crescent: w = 36; h = 10; inside = Crescent; break;
            case Piece.Log: w = 12; h = 28; inside = Log; break;
            case Piece.Pod: w = 18; h = 18; inside = PodMask; break;
            default: w = 7; h = 26; inside = Slug; break;
        }
        var ramp = MaterialOf(piece, world);
        string key = "ShotMot" + (int)piece + "_" + world + "_" + f;
        return AttackHazardArt.Made(key, w, h, Ppu, new Vector2(.5f, .5f), (px, ww, hh) => Shade(px, ww, hh, inside, f, ramp.light, ramp.dark), false);
    }

    static AttackHazardArt.Ramp MaterialOf(Piece piece, int world)
    {
        switch (piece)
        {
            case Piece.Log: return new AttackHazardArt.Ramp { light = AttackHazardArt.Hsv(28, .33f, .52f), dark = AttackHazardArt.Hsv(26, .34f, .36f) };   // grey bark: under the audit's saturation (.35) near the amber pickup
            case Piece.Slug: return AttackHazardArt.RampOf(world);
            default: return AttackHazardArt.RampOf(2);   // leaf, crescent blade, pod husk: the deep leaf ramp
        }
    }

    // ---- masks (pixel coordinates, y up) ---------------------------------------------------------------

    static bool Crescent(int x, int y)
    {
        float u = (x + .5f - 18f) / 18f;
        float cy = 3.2f + 3.2f * u * u;
        float half = .6f + 2.3f * Mathf.Pow(Mathf.Max(0f, 1f - u * u), .6f);
        return Mathf.Abs(y + .5f - cy) <= half;
    }

    static bool Leaf(int x, int y)
    {
        float v = (y + .5f) / 20f;
        float half = 6.4f * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(v)), .85f);
        return Mathf.Abs(x + .5f - 7f) <= half;
    }

    static bool Log(int x, int y)
    {
        // a trunk with chamfered corners
        int dx = Mathf.Min(x, 11 - x), dy = Mathf.Min(y, 27 - y);
        return dx + dy >= 2;
    }

    static bool PodMask(int x, int y)
    {
        float dx = (x + .5f - 9f) / 7.2f, dy = (y + .5f - 9f) / 8.2f;
        if (dx * dx + dy * dy <= 1f) return true;
        // four thorns (PC4: a spiked rim, never a smooth disc)
        float ox = x + .5f - 9f, oy = y + .5f - 9f;
        float ax = Mathf.Abs(ox), ay = Mathf.Abs(oy);
        if (ay < (9.6f - ax) * .45f + .2f && ax > 6.8f) return true;
        if (ax < (9.6f - ay) * .45f + .2f && ay > 7.6f) return true;
        return false;
    }

    static bool Slug(int x, int y)
    {
        float half = 3.4f * Mathf.Min(1f, Mathf.Min((y + .5f) / 7f, (25.5f - y) / 11f));   // blunt tail, long point
        return Mathf.Abs(x + .5f - 3.5f) <= Mathf.Max(.5f, half);
    }

    // ---- shading by depth from the rim ---------------------------------------------------------------------

    static void Shade(Color32[] px, int w, int h, System.Func<int, int, bool> inside, int f, Color32 light, Color32 dark)
    {
        var d = new int[w * h];
        var m = new bool[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) m[y * w + x] = inside(x, y);
        int maxD = 0;
        for (int layer = 1; layer < 16; layer++)
        {
            bool any = false;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (!m[i] || d[i] != 0) continue;
                    bool rim = layer == 1
                        ? (x == 0 || y == 0 || x == w - 1 || y == h - 1 || !m[i - 1] || !m[i + 1] || !m[i - w] || !m[i + w])
                        : (d[i - 1] == layer - 1 || d[i + 1] == layer - 1 || d[i - w] == layer - 1 || d[i + w] == layer - 1);
                    if (rim) { d[i] = -layer; any = true; }
                }
            for (int i = 0; i < d.Length; i++) if (d[i] < 0) d[i] = -d[i];
            if (!any) break;
            maxD = layer;
        }
        Color32 edge = AttackHazardArt.PinkEdge, body = AttackHazardArt.PinkBody, core = f == 0 ? AttackHazardArt.CoreA : AttackHazardArt.CoreB;
        int coreFrom = Mathf.Max(2, maxD - 2);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x, depth = d[i];
                if (depth == 0) continue;
                Color32 c;
                if (depth == 1) c = edge;
                else if (depth >= coreFrom) c = core;
                else if (depth == 2) c = body;
                else c = (((x >> 1) + (y >> 1)) & 1) == 0 ? light : dark;
                px[i] = c;
            }
    }
}
