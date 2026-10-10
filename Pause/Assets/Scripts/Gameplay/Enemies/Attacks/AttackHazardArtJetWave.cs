using System.Collections.Generic;
using UnityEngine;

// THE DRAWING OF THE JET AND THE WAVE (docs/world-attacks-art.md 3.4, 3.5) -- procedural, like the ring and the strike
// (AttackHazardArt.cs), with the world's art slot tried first:
//
//   <w>_attack_jet.png   Ember 768 x 384, Tide 768 x 448 (any world: 768 x (body + 128)): body x6 (apex at the top centre), nozzle x3, tip x3
//   tide_attack_wave.png 512 x 480: four tileable 512 x 96 bodies, capL, capR, gap marker a,b
//
// The same pink-cue contract (PC1-PC4) as every themed strip:
//   jet  a 2 px pink-white edge stroke all the way up both flanks, a hot pink-white core that steps between two
//        widths (the heat flicker), the world's material (flame orange, white water, pale ice, lilac neon) in
//        bands between them; jagged flanks for fire, faceted for ice, straight for water and the lance
//   wave a pink-white crest line on both edges of the band (the foam), the core between them flickering, the
//        material (white water, lilac neon) in the middle; the lower edge rolls (surf), the neon scan line is flat
//
// A procedural jet is made for a length CLASS (half units), a base and a tip width in pixels, then scaled to the exact
// hit shape (the length by <= 1, the width to hit x DrawFactor): a handful of sprites per world, never one per take.
public static partial class AttackHazardArt
{
    public const int JetProceduralFrames = 4;
    public const int WaveProceduralFrames = 2;
    public const int WaveTileW = 32, SurfH = 20, ScanH = 12;
    public const int NozzleSize = 24;

    // ---- the jet ------------------------------------------------------------------------------------

    static readonly Dictionary<long, Sprite> jetResolved = new Dictionary<long, Sprite>();
    static readonly Dictionary<long, float[]> jetDims = new Dictionary<long, float[]>();

    // Does the world ship a jet atlas?
    public static bool JetArt(int world)
    {
        int key = -100 - world;
        bool b;
        if (flags.TryGetValue(key, out b)) return b;
        b = AttackArt.JetBody(world, 0) != null;
        flags[key] = b;
        return b;
    }

    // The world's jet body for a frame, with the sprite's own length (u from the nozzle to the far end), tip width and base width (u, full).
    // Art: six 12 fps frames, nominal sizes per atlas. Procedural: JetProceduralFrames frames at a length class.
    public static Sprite JetBody(int world, JetStyle style, int frame, float length, float baseHalf, float tipHalf, bool bold,
                                 out float spriteLength, out float spriteTipWidth, out float spriteBaseWidth)
    {
        bool art = JetArt(world);
        int lenClass = art ? 0 : Mathf.Clamp(Mathf.CeilToInt(length * 2f - .001f), 2, 12);
        float classLen = lenClass * .5f;
        int basePx = art ? 0 : Mathf.Clamp(Mathf.RoundToInt(baseHalf * AttackJet.DrawFactor * Ppu), 1, 15);
        // the cone widens in proportion over the class length (the same half angle as the exact one)
        float tipForClass = baseHalf + (tipHalf - baseHalf) * (length > .01f ? classLen / length : 1f);
        int tipPx = art ? 0 : Mathf.Clamp(Mathf.RoundToInt(tipForClass * AttackJet.DrawFactor * Ppu), basePx, 62);
        long key = (((((((long)world * 4 + (int)style) * 8 + (art ? frame % 6 : frame % JetProceduralFrames)) * 2 + (bold ? 1 : 0)) * 16 + lenClass) * 64 + tipPx) * 16 + basePx);
        Sprite s;
        float[] dims;
        if (jetResolved.TryGetValue(key, out s) && s != null && s.texture != null && jetDims.TryGetValue(key, out dims))
        {
            spriteLength = dims[0]; spriteTipWidth = dims[1]; spriteBaseWidth = dims[2];
            return s;
        }
        if (art)
        {
            s = AttackArt.JetBody(world, frame);
            float nominal = AttackArt.JetBodyPx(world) == 256 ? .76f : .5f;
            dims = new[] { AttackArt.JetContentPx(world) / AttackArt.PixelsPerUnit, nominal, Mathf.Min(nominal, .2f) };
        }
        else
        {
            s = JetProcedural(world, style, frame, lenClass, basePx, tipPx, bold);
            // the painted tip width is the unpadded canvas: 2 * tipPx pixels
            dims = new[] { lenClass * .5f, 2f * tipPx / Ppu, 2f * basePx / Ppu };
        }
        jetResolved[key] = s;
        jetDims[key] = dims;
        spriteLength = dims[0]; spriteTipWidth = dims[1]; spriteBaseWidth = dims[2];
        return s;
    }

    // The painted jet: apex at the TOP centre (pivot), lenClass * 16 px tall, widening from basePx to tipPx half width.
    public static Sprite JetProcedural(int world, JetStyle style, int frame, int lenClass, int basePx, int tipPx, bool bold)
    {
        int f = Mathf.Abs(frame) % JetProceduralFrames;
        int h = lenClass * 16;
        int w = 2 * tipPx + 2;
        var ramp = RampOf(world);
        return Made("AtkJet" + world + (int)style + "_" + f + "_" + lenClass + "_" + basePx + "_" + tipPx + (bold ? "b" : ""), w, h, Ppu, new Vector2(.5f, 1f), (px, ww, hh) =>
        {
            Color32 edge = PinkEdge, body = PinkBody, core = (f & 1) == 0 ? CoreA : CoreB;
            float cx = ww * .5f;
            for (int row = 0; row < hh; row++)
            {
                int y = hh - 1 - row;                        // row 0 is the nozzle (the top of the sprite)
                float k = hh > 1 ? row / (float)(hh - 1) : 1f;
                float hw = basePx + (tipPx - basePx) * k;
                float jl = 0f, jr = 0f;
                switch (style)
                {
                    case JetStyle.Flame:    // licking flanks: they breathe a pixel or two, more toward the tip
                        jl = Mathf.Round(1.6f * Mathf.Sin(row * .85f + f * 1.7f) * (.3f + k));
                        jr = Mathf.Round(1.6f * Mathf.Sin(row * .85f + 2.3f + f * 2.1f) * (.3f + k));
                        break;
                    case JetStyle.Water:    // spray: a ragged flank
                        jl = (((row * 7 + f * 5) % 11) < 2) ? -1f : (((row * 3 + f) % 13) < 2 ? 1f : 0f);
                        jr = (((row * 5 + f * 3) % 11) < 2) ? -1f : (((row * 11 + f) % 13) < 2 ? 1f : 0f);
                        break;
                    case JetStyle.Frost:    // faceted: a step every eight rows
                        jl = jr = (row % 8 == 0 && row > 0) ? 1f : 0f;
                        break;
                }
                for (int x = 0; x < ww; x++)
                {
                    float dx = x + .5f - cx;
                    float side = dx < 0f ? hw + jl : hw + jr;
                    float u = side - Mathf.Abs(dx);
                    if (u <= 0f) continue;
                    if (side < 1f) continue;
                    Color32 c;
                    // the hot core: a fire is white-hot at the nozzle and cools toward the tip; the other jets keep a slim, steady core. Two widths (the flicker).
                    float coreF = style == JetStyle.Flame ? Mathf.Lerp(.46f, .2f, k) : .26f;
                    int coreHalf = Mathf.Max(1, Mathf.RoundToInt(hw * coreF)) + (f & 1);
                    if (u <= 2f) c = edge;                        // two px of pink-white stroke
                    else if (u <= 3f) c = body;                   // one px of pink body
                    else if (Mathf.Abs(dx) <= coreHalf) c = core; // the hot core (two widths: the flicker)
                    else
                    {
                        bool light;
                        switch (style)
                        {
                            case JetStyle.Flame: light = ((row / 5 + f) & 1) == 0; break;           // banded heat, rolling with the frame
                            case JetStyle.Water: light = (((x >> 1) + (row >> 2) + f) & 1) == 0; break;   // streaks
                            case JetStyle.Frost: light = dx < 0f; break;                            // facets
                            default: light = ((row / 3 + f) & 1) == 0; break;                       // lance: crackle
                        }
                        c = light ? ramp.light : ramp.dark;
                    }
                    px[y * ww + x] = c;
                }
            }
        }, bold);
    }

    // The nozzle's flare (stage 0-2 grows through the tell).
    public static Sprite JetNozzle(int world, JetStyle style, int stage, bool bold)
    {
        int key = Key(Kind.Nozzle, world, (int)style, Mathf.Clamp(stage, 0, 2), bold);
        Sprite s;
        if (Cached(key, out s)) return s;
        s = AttackArt.JetNozzle(world, stage) ?? Flare(world, style, stage, bold);
        resolved[key] = s;
        return s;
    }

    // The sparks / splash at the far end (3 stepped frames).
    public static Sprite JetTip(int world, JetStyle style, int frame, bool bold)
    {
        int key = Key(Kind.JetTip, world, (int)style, Mathf.Clamp(frame, 0, 2), bold);
        Sprite s;
        if (Cached(key, out s)) return s;
        s = AttackArt.JetTip(world, frame) ?? Sparks(world, style, frame, bold);
        resolved[key] = s;
        return s;
    }

    public static Sprite Flare(int world, JetStyle style, int stage, bool bold)
    {
        int st = Mathf.Clamp(stage, 0, 2);
        var ramp = RampOf(world);
        return Made("AtkFlare" + world + (int)style + "_" + st + (bold ? "b" : ""), NozzleSize, NozzleSize, Ppu, new Vector2(.5f, .5f), (px, w, h) =>
        {
            Color32 edge = PinkEdge, body = PinkBody, hot = st == 1 ? CoreB : CoreA;
            float c = (w - 1) * .5f;
            float radius = 5f + st * 3.2f;
            for (int k = 0; k < 8; k++)
            {
                float a = (k * 45f + st * 11f) * Mathf.Deg2Rad;
                float len = radius * ((k & 1) == 0 ? 1f : .62f);
                for (float d = 0f; d <= len; d += .5f)
                {
                    int x = Mathf.RoundToInt(c + Mathf.Cos(a) * d), y = Mathf.RoundToInt(c + Mathf.Sin(a) * d);
                    Color32 col = d > len * .72f ? edge : (d > len * .5f ? body : ((k & 1) == 1 && d > len * .25f ? ramp.light : hot));
                    Dot(px, w, h, x, y, col);
                    Dot(px, w, h, x + 1, y, d > len * .72f ? edge : body);   // two px thick: a glowing ray, not a hair
                }
            }
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) Dot(px, w, h, (int)c + dx, (int)c + dy, hot);
        }, bold);
    }

    public static Sprite Sparks(int world, JetStyle style, int frame, bool bold)
    {
        int f = Mathf.Clamp(frame, 0, 2);
        var ramp = RampOf(world);
        return Made("AtkSparks" + world + (int)style + "_" + f + (bold ? "b" : ""), NozzleSize, NozzleSize, Ppu, new Vector2(.5f, .5f), (px, w, h) =>
        {
            Color32 edge = PinkEdge, hot = f == 0 ? CoreA : CoreB;
            float c = (w - 1) * .5f;
            int n = 9 - f * 2;
            for (int k = 0; k < n; k++)
            {
                float a = (k * (360f / n) + f * 17f) * Mathf.Deg2Rad;
                float d = 4f + f * 2.5f + (k % 3);
                int x = Mathf.RoundToInt(c + Mathf.Cos(a) * d), y = Mathf.RoundToInt(c + Mathf.Sin(a) * d);
                Dot(px, w, h, x, y, (k & 1) == 0 ? edge : hot);
                Dot(px, w, h, x + 1, y, (k & 1) == 0 ? edge : hot);
                Dot(px, w, h, x, y + 1, (k & 1) == 0 ? hot : ramp.light);
            }
            for (int dx = -1; dx <= 0; dx++) for (int dy = -1; dy <= 0; dy++) Dot(px, w, h, (int)c + dx, (int)c + dy, hot);
        }, bold);
    }

    // ---- the wave -----------------------------------------------------------------------------------

    // Does the world ship a wave atlas?
    public static bool WaveArt(int world)
    {
        int key = -200 - world;
        bool b;
        if (flags.TryGetValue(key, out b)) return b;
        b = AttackArt.WaveBody(world, 0) != null;
        flags[key] = b;
        return b;
    }

    // The band's tile: art frames (4, 12 fps) or the procedural strip (2 frames of the core's flicker).
    public static Sprite WaveBody(int world, WaveStyle style, int frame, bool bold)
    {
        bool art = WaveArt(world);
        int key = Key(Kind.Wave, world, (int)style, art ? frame & 3 : frame & 1, bold);
        Sprite s;
        if (Cached(key, out s)) return s;
        s = (art ? AttackArt.WaveBody(world, frame) : null) ?? WaveProcedural(world, style, frame, bold);
        resolved[key] = s;
        return s;
    }

    // The rounded ends that meet the gap (art only).
    public static Sprite WaveCap(int world, bool left)
    {
        if (!WaveArt(world)) return null;
        int key = Key(Kind.Wave, world, 3, left ? 6 : 7, false);
        Sprite s;
        if (Cached(key, out s)) return s;
        s = left ? AttackArt.WaveCapL(world) : AttackArt.WaveCapR(world);
        resolved[key] = s;
        return s;
    }

    // The chevron at a gap edge (art cells, else the same pink-white chevron the blast's crack wears).
    public static Sprite WaveGapMarker(int world, int frame, bool bold)
    {
        int key = Key(Kind.WaveMarker, world, 0, frame & 1, bold);
        Sprite s;
        if (Cached(key, out s)) return s;
        s = AttackArt.WaveGapMarker(world, frame & 1) ?? GapProcedural(world, frame, bold);
        resolved[key] = s;
        return s;
    }

    public static Sprite WaveProcedural(int world, WaveStyle style, int frame, bool bold)
    {
        int f = frame & 1;
        int h = style == WaveStyle.Scan ? ScanH : SurfH;
        var ramp = RampOf(world);
        return Made("AtkWave" + world + (int)style + "_" + f + (bold ? "b" : ""), WaveTileW, h, Ppu, new Vector2(.5f, .5f), (px, w, hh) =>
        {
            Color32 edge = PinkEdge, body = PinkBody, core = f == 0 ? CoreA : CoreB;
            for (int x = 0; x < w; x++)
            {
                float ph = x / (float)w * 6.2832f;
                int low, high;
                if (style == WaveStyle.Surf)
                {
                    low = Mathf.RoundToInt(1.3f * (1f + Mathf.Sin(ph + f * 1.57f)));                  // the foam crest rolls along the lower edge
                    high = hh - 1 - Mathf.RoundToInt(.7f * (1f + Mathf.Sin(ph * 2f + 1f)));           // a ripple on the calm top
                }
                else { low = 0; high = hh - 1; }                                                      // a neon line is flat
                float mid = (low + high) * .5f;
                for (int y = low; y <= high; y++)
                {
                    int d = Mathf.Min(y - low, high - y);
                    Color32 c;
                    if (d <= 1) c = edge;
                    else if (d == 2) c = body;
                    else if (Mathf.Abs(y - mid) <= (style == WaveStyle.Scan ? 1f : 2f) + f) c = core;
                    else c = (((x >> 2) + (y >> 1)) & 1) == 0 ? ramp.light : ramp.dark;
                    px[y * w + x] = c;
                }
            }
        }, bold, false, false, true);
    }

    static void ForgetJetWave() { jetResolved.Clear(); jetDims.Clear(); }
}
