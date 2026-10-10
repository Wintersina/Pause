using System.Collections.Generic;
using UnityEngine;

// THE DRAWING OF THE THEMED AREA HAZARDS (docs/world-attacks-art.md 3.3, 3.6) -- procedural, so
// the cores ship and pass their tests before Codex's strips exist.
//
// Every look-up tries the world's AttackArt cell first (frost_attack_ring.png: bar / gapMarker /
// glyph; <w>_attack_strike.png: column body / glyph / burst) and falls back to the pixel art
// painted here. Both follow the pink-cue contract (design 0.1):
//
//   PC1 keyline   a 2 px pink-white edge stroke on the whole silhouette; on bright worlds
//                 (ShotOutline.UseBold) the sprite also carries the near-black BoldKey ring
//                 outside it (one extra pixel; the same colour ShotOutline's bold keyline uses)
//   PC2 core      the innermost rows are pink-white and flicker between two frames (stepped,
//                 HostileShotPalette.FlickerHz), >= 30% of the opaque area is pink-family or white
//   PC3 material  the world's ramp (ice, flame, water ...) fills at most half the area, kept
//                 clear of the pickup hues (saturation <= .65 within 25 deg of 178/82/259/37)
//   PC4 shape     elongated, pointed bars; jagged / faceted columns; never a smooth disc
//
// Sprites are made once per (kind, world, frame, bold) and cached; a scene change that unloaded
// one is rebuilt. Nothing is made per frame.
public enum StrikeStyle { Icicle, Eruption, Thunder }
public enum LashLook { Vine, Tentacle }

public static class AttackHazardArt
{
    public const float Ppu = 32f;                    // procedural sprites: 32 px per unit
    public const int BarW = 28, BarH = 9;            // 0.875 x 0.28 u, the art cell's bar (112 x 36 px of 128)
    public const float BarLength = BarW / Ppu;       // 0.875 u: the length an unscaled bar has
    public const int ColW = 24, ColH = 32, TipH = 16;   // the column tile (0.75 x 1 u; the body is 16 px = .5 u wide)
    public const float ColBodyWidth = 16f / Ppu;     // the drawn width of a column (.5 u; the hit is .36)
    public const int GlyphSize = 32, GapSize = 12;

    // ---- the world's material ramp (PC3) -----------------------------------------------------------
    public struct Ramp { public Color32 light, dark; }

    public static Ramp RampOf(int world)
    {
        switch (world)
        {
            case 1: return new Ramp { light = Hsv(200, .22f, 1f), dark = Hsv(205, .36f, .92f) };    // frost: pale, never shield-cyan
            case 2: return new Ramp { light = Hsv(108, .5f, .6f), dark = Hsv(112, .55f, .42f) };    // verdant: deep leaf, never repair-green
            case 3: return new Ramp { light = Hsv(27, .6f, 1f), dark = Hsv(23, .62f, .9f) };         // ember: hot orange, never amber 37
            case 4: return new Ramp { light = Hsv(205, .2f, 1f), dark = Hsv(212, .4f, .9f) };        // tide: white water
            default: return new Ramp { light = Hsv(285, .25f, 1f), dark = Hsv(288, .4f, .9f) };      // space: lilac neon
        }
    }

    public static StrikeStyle StyleOf(int world)
    {
        switch (world)
        {
            case 1: return StrikeStyle.Icicle;
            case 3: return StrikeStyle.Eruption;
            default: return StrikeStyle.Thunder;
        }
    }

    public static Color32 Hsv(float deg, float s, float v) => (Color32)Color.HSVToRGB(deg / 360f, s, v);

    // the pink the whole family wears (HostileShotPalette)
    public static Color32 PinkBody => (Color32)HostileShotPalette.Body(EnemyBehaviours.SpaceShot);
    public static Color32 PinkEdge => (Color32)HostileShotPalette.Trace(HostileShotPalette.Body(EnemyBehaviours.SpaceShot));
    public static Color32 CoreA => (Color32)HostileShotPalette.Core(HostileShotPalette.Body(EnemyBehaviours.SpaceShot));
    public static Color32 CoreB => (Color32)Color.Lerp(HostileShotPalette.Core(HostileShotPalette.Body(EnemyBehaviours.SpaceShot)), Color.white, .8f);

    // ---- the cache --------------------------------------------------------------------------------

    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
    // What a look-up resolved to (the world's art cell, else the procedural sprite), by an int key: a per-frame
    // call allocates nothing (the string keys above are only built when a sprite is first made).
    static readonly Dictionary<int, Sprite> resolved = new Dictionary<int, Sprite>();
    static readonly Dictionary<int, bool> flags = new Dictionary<int, bool>();
    enum Kind { Bar, Gap, Glyph, Col, Tip, Burst, Link, LTip, Root, Dash }
    static int Key(Kind k, int world, int style, int frame, bool bold) =>
        ((((int)k * 8 + world) * 4 + style) * 8 + frame) * 2 + (bold ? 1 : 0);
    static bool Cached(int key, out Sprite s) => resolved.TryGetValue(key, out s) && s != null && s.texture != null;

    // `seam`: a tile that stacks (a column body): the keyline pads left and right only, so no dark line crosses a seam;
    // `tipBottom`: the spear point, which pads left, right and bottom (its top meets a body tile).
    public static Sprite Made(string key, int w, int h, float ppu, Vector2 pivot, System.Action<Color32[], int, int> paint, bool bold, bool seam = false, bool tipBottom = false)
    {
        Sprite s;
        if (cache.TryGetValue(key, out s) && s != null && s.texture != null) return s;
        int pad = bold ? 1 : 0;
        int padX = pad, padBottom = (seam ? 0 : pad), padTop = (seam || tipBottom ? 0 : pad);
        int W = w + padX * 2, H = h + padBottom + padTop;
        var px = new Color32[W * H];
        var inner = new Color32[w * h];
        paint(inner, w, h);
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[(y + padBottom) * W + x + padX] = inner[y * w + x];
        if (bold) Keyline(px, W, H);
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.name = key;
        tex.hideFlags = HideFlags.DontSave;
        tex.SetPixels32(px);
        tex.Apply(false, false);
        // (the pivot is given on the unpadded canvas)
        var pv = new Vector2((pivot.x * w + padX) / W, (pivot.y * h + padBottom) / H);
        s = Sprite.Create(tex, new Rect(0, 0, W, H), pv, ppu, 0, SpriteMeshType.FullRect);
        s.name = key;
        s.hideFlags = HideFlags.DontSave;
        cache[key] = s;
        return s;
    }

    // The near-black ring around the silhouette (ShotOutline.BoldKey): the bright worlds' hard keyline.
    static void Keyline(Color32[] px, int w, int h)
    {
        var key = ShotOutline.BoldKey;
        var copy = (Color32[])px.Clone();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (copy[y * w + x].a > 0) continue;
                bool near = (x > 0 && copy[y * w + x - 1].a > 0) || (x < w - 1 && copy[y * w + x + 1].a > 0) ||
                            (y > 0 && copy[(y - 1) * w + x].a > 0) || (y < h - 1 && copy[(y + 1) * w + x].a > 0);
                if (near) px[y * w + x] = key;
            }
    }

    static bool Bold => ShotOutline.UseBold;

    public static void Forget() { cache.Clear(); resolved.Clear(); flags.Clear(); }   // (tests: after injecting art)

    // ---- the ring's bar (Frost's cold blast; any world's material) -----------------------------------

    // frame 0 / 1: the pink-white core steps between two heats; art cells 0-3 take the place of both when present
    public static Sprite RingBar(int world, int frame, bool bold)
    {
        int key = Key(Kind.Bar, world, 0, frame & 3, bold);
        Sprite s;
        if (Cached(key, out s)) return s;
        s = AttackArt.RingBar(world, frame & 3) ?? RingBarProcedural(world, frame, bold);
        resolved[key] = s;
        return s;
    }

    public static Sprite RingBarProcedural(int world, int frame, bool bold)
    {
        var ramp = RampOf(world);
        int f = frame & 1;
        return Made("AtkBar" + world + "_" + f + (bold ? "b" : ""), BarW, BarH, Ppu, new Vector2(.5f, .5f), (px, w, h) =>
        {
            Color32 edge = PinkEdge, body = PinkBody, core = f == 0 ? CoreA : CoreB;
            for (int x = 0; x < w; x++)
            {
                int end = Mathf.Min(x, w - 1 - x);
                int half = Mathf.Min(4, (end + 1) / 2);      // pointed ends (PC4)
                for (int y = 0; y < h; y++)
                {
                    int d = Mathf.Abs(y - 4);
                    if (d > half) continue;
                    Color32 c;
                    if (d == half) c = edge;
                    else if (d == half - 1) c = body;
                    else if (d <= (f == 0 ? 0 : 1)) c = core;
                    else c = ((x / 3 + d) & 1) == 0 ? ramp.light : ramp.dark;
                    px[y * w + x] = c;
                }
            }
        }, bold);
    }

    public static Sprite RingGlyph(int world, int frame, bool bold)
    {
        int key = Key(Kind.Glyph, world, 3, frame & 1, bold);
        Sprite s;
        if (Cached(key, out s)) return s;
        s = AttackArt.RingGlyph(world, frame & 1) ?? Glyph(StrikeStyle.Icicle, world, frame, bold, false);
        resolved[key] = s;
        return s;
    }

    // the crack's edge marker: a pink-white chevron, 2 blinking frames
    public static Sprite GapMarker(int world, int frame, bool bold)
    {
        int key = Key(Kind.Gap, world, 0, frame & 1, bold);
        Sprite s;
        if (Cached(key, out s)) return s;
        s = AttackArt.RingGapMarker(world, frame & 1) ?? GapProcedural(world, frame, bold);
        resolved[key] = s;
        return s;
    }

    static Sprite GapProcedural(int world, int frame, bool bold)
    {
        int f = frame & 1;
        return Made("AtkGap" + world + "_" + f + (bold ? "b" : ""), GapSize, GapSize, Ppu, new Vector2(.5f, .5f), (px, w, h) =>
        {
            Color32 c = f == 0 ? PinkEdge : CoreB, d = PinkBody;
            for (int i = 0; i < 6; i++)
            {
                // a chevron pointing down (toward the crack's middle is the caller's rotation)
                int y = h - 2 - i;
                int xl = 5 - i, xr = 6 + i;
                if (xl >= 0 && y >= 0) { px[y * w + xl] = c; px[y * w + xr] = c; }
                if (xl + 1 < w && y >= 0 && i > 0) { px[y * w + xl + 1] = d; px[y * w + xr - 1] = d; }
            }
        }, bold);
    }

    // ---- the strike -------------------------------------------------------------------------------

    // The column body (frame 0 / 1: the core's flicker). Tiles vertically; TileHeight is its height in units.
    public static Sprite ColumnBody(int world, StrikeStyle style, int frame, bool bold, out float tileHeight, out float drawWidth)
    {
        bool art = StrikeArt(world);
        int key = Key(Kind.Col, world, (int)style, art ? frame % 6 : frame & 1, bold);
        Sprite s;
        if (!Cached(key, out s))
        {
            s = (art ? AttackArt.StrikeBody(world, frame) : null) ?? ColumnProcedural(world, style, frame, bold, false);
            resolved[key] = s;
        }
        tileHeight = s.bounds.size.y;
        drawWidth = art ? s.bounds.size.x * (16f / 128f * 3f) : ColBodyWidth;   // (the art's column is ~46 px of its 128 cell)
        return s;
    }

    // The drawn width of the column body (the art's, else the procedural .5 u).
    public static float ColumnDrawWidth(int world, StrikeStyle style, bool bold)
    {
        float th, dw;
        ColumnBody(world, style, 0, bold, out th, out dw);
        return dw;
    }

    // Does the world ship a strike atlas?
    public static bool StrikeArt(int world)
    {
        int key = -1 - world;
        bool b;
        if (flags.TryGetValue(key, out b)) return b;
        b = AttackArt.StrikeBody(world, 0) != null;
        flags[key] = b;
        return b;
    }

    public static bool HasTip(StrikeStyle style) => style == StrikeStyle.Icicle;

    // The spear point at the impact end (Icicle). Null for the styles with a flat foot.
    public static Sprite ColumnTip(int world, StrikeStyle style, bool bold)
    {
        if (!HasTip(style)) return null;
        int key = Key(Kind.Tip, world, (int)style, 0, bold);
        Sprite s;
        if (Cached(key, out s)) return s;
        s = ColumnProcedural(world, style, 0, bold, true);
        resolved[key] = s;
        return s;
    }

    public static Sprite ColumnProcedural(int world, StrikeStyle style, int frame, bool bold, bool tip)
    {
        var ramp = RampOf(world);
        int f = frame & 1;
        int h = tip ? TipH : ColH;
        return Made("AtkCol" + world + (int)style + "_" + f + (tip ? "t" : "") + (bold ? "b" : ""), ColW, h, Ppu, new Vector2(.5f, tip ? 0f : .5f), (px, w, hh) =>
        {
            Color32 edge = PinkEdge, body = PinkBody, core = f == 0 ? CoreA : CoreB;
            for (int y = 0; y < hh; y++)
            {
                float shift = 0f, hw = 8f;
                switch (style)
                {
                    case StrikeStyle.Thunder:   // a jagged bolt: the centre line wanders, periodic over the tile
                        shift = Mathf.Round(2.2f * Mathf.Sin(y / 32f * 6.2832f) + 1.1f * Mathf.Sin(y / 32f * 6.2832f * 3f));
                        break;
                    case StrikeStyle.Eruption:  // flame edges: the width breathes
                        hw = 8f + Mathf.Round(1.2f * Mathf.Sin(y / 32f * 6.2832f * 2f));
                        break;
                    default:                    // icicle: straight, faceted
                        break;
                }
                if (tip) hw = 1f + 7f * (y / (float)(hh - 1));   // the spear point narrows to the bottom (y = 0)
                float cx = w * .5f + shift;
                for (int x = 0; x < w; x++)
                {
                    float dx = Mathf.Abs(x + .5f - cx);
                    float u = hw - dx;
                    if (u <= 0f) continue;
                    Color32 c;
                    if (u <= 2f) c = edge;                       // two px of pink-white stroke
                    else if (u <= 3f) c = body;                  // one px of the pink body
                    else if (dx <= (f == 0 ? 2f : 3f)) c = core; // the hot core, 4 px wide, 6 px on the other frame
                    else
                    {
                        bool lightSide;
                        if (style == StrikeStyle.Icicle) lightSide = x + .5f < cx;                 // facets: one flank light, one dark
                        else if (style == StrikeStyle.Eruption) lightSide = ((y / 4) & 1) == 0;    // banded flame
                        else lightSide = ((y / 3 + x / 2) & 1) == 0;                              // crackle
                        c = lightSide ? ramp.light : ramp.dark;
                    }
                    px[y * w + x] = c;
                }
            }
        }, bold, !tip, tip);
    }

    // The lane marker (1 u): a dotted pink-white ring around the style's motif; frame 1 is the blink.
    public static Sprite StrikeGlyph(int world, StrikeStyle style, int frame, bool bold)
    {
        int key = Key(Kind.Glyph, world, (int)style, frame & 1, bold);
        Sprite s;
        if (Cached(key, out s)) return s;
        s = AttackArt.StrikeGlyph(world, frame & 1) ?? Glyph(style, world, frame, bold, false);
        resolved[key] = s;
        return s;
    }

    public static Sprite Glyph(StrikeStyle style, int world, int frame, bool bold, bool useArt = true)
    {
        if (useArt) return StrikeGlyph(world, style, frame, bold);
        int f = frame & 1;
        return Made("AtkGlyph" + (int)style + "_" + f + (bold ? "b" : ""), GlyphSize, GlyphSize, Ppu, new Vector2(.5f, .5f), (px, w, h) =>
        {
            Color32 edge = PinkEdge, hot = f == 0 ? CoreA : CoreB;
            float c = (w - 1) * .5f;
            for (int k = 0; k < 24; k++)
            {
                float a = (k * 15f + (f == 0 ? 0f : 7.5f)) * Mathf.Deg2Rad;
                int x = Mathf.RoundToInt(c + Mathf.Cos(a) * 13.5f), y = Mathf.RoundToInt(c + Mathf.Sin(a) * 13.5f);
                if ((k & 1) == 0) { Dot(px, w, h, x, y, edge); Dot(px, w, h, x + 1, y, edge); }
                else Dot(px, w, h, x, y, edge);
            }
            switch (style)
            {
                case StrikeStyle.Icicle:   // a snowflake: three crossing rays
                    for (int ray = 0; ray < 3; ray++)
                        for (int i = -6; i <= 6; i++)
                        {
                            float a = ray * 60f * Mathf.Deg2Rad;
                            Dot(px, w, h, Mathf.RoundToInt(c + Mathf.Cos(a) * i), Mathf.RoundToInt(c + Mathf.Sin(a) * i), Mathf.Abs(i) > 3 ? edge : hot);
                        }
                    break;
                case StrikeStyle.Eruption: // an up chevron over a bar
                    for (int i = 0; i < 6; i++) { Dot(px, w, h, 16 - i, 20 - i, hot); Dot(px, w, h, 15 + i, 20 - i, hot); }
                    for (int i = -6; i <= 6; i++) Dot(px, w, h, 16 + i, 9, edge);
                    break;
                default:                   // a zig-zag bolt
                    for (int i = 0; i < 13; i++) Dot(px, w, h, 16 + ((i / 3) % 2 == 0 ? 2 : -2) + (i % 3) - 1, 24 - i, hot);
                    break;
            }
        }, bold);
    }

    public static void Dot(Color32[] px, int w, int h, int x, int y, Color32 c)
    {
        if (x < 0 || y < 0 || x >= w || y >= h) return;
        px[y * w + x] = c;
    }

    // A white 1 x 1 u square (pixel art: one texel), scaled to a footprint rectangle and tinted pink: the tell's faint fill of a column.
    public static Sprite Band()
    {
        Sprite s;
        if (cache.TryGetValue("AtkBand", out s) && s != null && s.texture != null) return s;
        return Made("AtkBand", 1, 1, 1f, new Vector2(.5f, .5f), (px, w, h) => { px[0] = new Color32(255, 255, 255, 255); }, false);
    }

    // The ground burst (3 frames, 1 u): a spiked ring in the material, growing and breaking up.
    public static Sprite Burst(int world, StrikeStyle style, int frame, bool bold)
    {
        int key = Key(Kind.Burst, world, (int)style, Mathf.Clamp(frame, 0, 2), bold);
        Sprite s;
        if (Cached(key, out s)) return s;
        s = AttackArt.StrikeBurst(world, frame) ?? BurstProcedural(world, style, frame, bold);
        resolved[key] = s;
        return s;
    }

    static Sprite BurstProcedural(int world, StrikeStyle style, int frame, bool bold)
    {
        int f = Mathf.Clamp(frame, 0, 2);
        var ramp = RampOf(world);
        return Made("AtkBurst" + world + (int)style + "_" + f + (bold ? "b" : ""), GlyphSize, GlyphSize, Ppu, new Vector2(.5f, .5f), (px, w, h) =>
        {
            Color32 edge = PinkEdge, body = PinkBody, hot = CoreB;
            float c = (w - 1) * .5f;
            float radius = 5f + f * 4.5f;
            for (int k = 0; k < 12; k++)
            {
                float a = (k * 30f + f * 9f) * Mathf.Deg2Rad;
                float len = radius + ((k & 1) == 0 ? 3f : -1f);
                for (float d = Mathf.Max(0f, radius - 3f - f); d <= len; d += .5f)
                {
                    int x = Mathf.RoundToInt(c + Mathf.Cos(a) * d), y = Mathf.RoundToInt(c + Mathf.Sin(a) * d);
                    // pink-white at the tips, pink behind them, the world's material only in the hub
                    Color32 col = d > len * .55f ? edge : (d > len * .38f ? body : (((k + f) & 1) == 0 ? ramp.light : ramp.dark));
                    Dot(px, w, h, x, y, col);
                }
            }
            if (f < 2) for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) Dot(px, w, h, (int)c + dx, (int)c + dy, hot);
        }, bold);
    }

    // ---- the lash (plan phase 1e; docs/world-attacks-art.md 3.8) --------------------------------------------
    // A whip is a chain of link tiles ending in a pink thorn / sucker tip, a bud at its root and a dotted arc for the tell.
    // <w>_attack_lash.png cells (link a,b / tip a,b / root a,b / dash a,b) take the place of each when present; otherwise:
    //   vine      a green chain (the Verdant ramp) with pink thorns on alternating sides, a pink-white edge stroke and core
    //   tentacle  brass-teal segments with pink sucker dots (Tide)
    public const int LinkW = 16, LinkH = 20, LashTipW = 16, LashTipH = 24, LashRootSize = 20, LashDashSize = 6;
    public const float LinkLength = LinkH / Ppu;        // .625 u: the length an unscaled procedural link has
    public const float LashBodyWidth = 12f / Ppu;       // .375 u drawn (the hit is .28)

    public static LashLook LookOf(int world) => world == 4 ? LashLook.Tentacle : LashLook.Vine;

    // the tentacle's brass-teal pair: a sea teal at hue 150-154 (24+ deg from the pickup cyan 178) and a pale brass (saturation .33, under the audit's .35)
    static Color32 TealLight => Hsv(154, .45f, .58f);
    static Color32 TealDark => Hsv(150, .5f, .36f);
    static Color32 Brass => Hsv(44, .33f, .72f);

    static Color32 Material(LashLook look, int world, int band, bool light)
    {
        if (look == LashLook.Tentacle) return (band % 3) == 2 ? Brass : (light ? TealLight : TealDark);
        var r = RampOf(2);   // (a vine is always the deep leaf ramp)
        return light ? r.light : r.dark;
    }

    public static bool LashArt(int world)
    {
        int key = -100 - world;
        bool b;
        if (flags.TryGetValue(key, out b)) return b;
        b = AttackArt.LashLink(world, 0) != null;
        flags[key] = b;
        return b;
    }

    // one section of the chain, pointing down (frame 0 / 1: the core's flicker and the thorn's side)
    public static Sprite LashLink(int world, int frame, bool bold)
    {
        int key = Key(Kind.Link, world, 0, frame & 1, bold);
        Sprite s;
        if (Cached(key, out s)) return s;
        s = AttackArt.LashLink(world, frame & 1) ?? LinkProcedural(world, frame, bold);
        resolved[key] = s;
        return s;
    }

    public static Sprite LinkProcedural(int world, int frame, bool bold)
    {
        var look = LookOf(world);
        int f = frame & 1;
        return Made("AtkLink" + (int)look + "_" + f + (bold ? "b" : ""), LinkW, LinkH, Ppu, new Vector2(.5f, .5f), (px, w, h) =>
        {
            Color32 edge = PinkEdge, body = PinkBody, core = f == 0 ? CoreA : CoreB;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = Mathf.Abs(x + .5f - w * .5f);
                    if (dx >= 6f) continue;
                    float u = 6f - dx;
                    Color32 c;
                    if (u <= 2f) c = edge;
                    else if (u <= 3f || y == 0 || y == h - 1) c = body;
                    else if (dx <= (f == 0 ? 1f : 1.99f)) c = core;
                    else if (look == LashLook.Tentacle && (y == 9 || y == 10) && (x == 5 || x == 10) ) c = edge;   // a sucker
                    else c = Material(look, world, y / 3, ((y / 3) & 1) == 0);
                    px[y * w + x] = c;
                }
            if (look == LashLook.Vine)
            {
                // a thorn, pink, on the left on one frame and the right on the other (stepped, not a smooth sway: PC5)
                int xs = f == 0 ? 1 : w - 2, step = f == 0 ? -1 : 1;
                Dot(px, w, h, xs, 10, edge); Dot(px, w, h, xs + step, 10, edge);
                Dot(px, w, h, xs, 9, body); Dot(px, w, h, xs, 11, body);
                Dot(px, w, h, xs - step, 10, edge);
            }
        }, bold, true);
    }

    // the tip at the end of the chain (attaches at its top, points down): a pink thorn (vine) or a barbed sucker tip (tentacle)
    public static Sprite LashTip(int world, int frame, bool bold)
    {
        int key = Key(Kind.LTip, world, 0, frame & 1, bold);
        Sprite s;
        if (Cached(key, out s)) return s;
        s = AttackArt.LashTip(world, frame & 1) ?? TipProcedural(world, frame, bold);
        resolved[key] = s;
        return s;
    }

    static Sprite TipProcedural(int world, int frame, bool bold)
    {
        var look = LookOf(world);
        int f = frame & 1;
        return Made("AtkLTip" + (int)look + "_" + f + (bold ? "b" : ""), LashTipW, LashTipH, Ppu, new Vector2(.5f, .5f), (px, w, h) =>
        {
            Color32 edge = PinkEdge, body = PinkBody, core = f == 0 ? CoreA : CoreB;
            for (int y = 0; y < h; y++)
            {
                float hw = 1f + 5f * Mathf.Min(1f, y / 15f);              // y = 0 is the point
                if (look == LashLook.Tentacle && y >= 9 && y <= 11) hw += 2f;   // a barb either side
                for (int x = 0; x < w; x++)
                {
                    float dx = Mathf.Abs(x + .5f - w * .5f);
                    float u = hw - dx;
                    if (u <= 0f) continue;
                    Color32 c;
                    if (u <= 2f) c = edge;
                    else if (u <= 3f) c = body;
                    else if (dx <= (f == 0 ? 1f : 1.99f)) c = core;
                    else c = Material(look, world, y / 3, (x + y) % 2 == 0);
                    if (look == LashLook.Vine && y < 5 && u > 0f) c = (u <= 3f) ? edge : core;   // the point itself is all pink-white
                    px[y * w + x] = c;
                }
            }
        }, bold);
    }

    // the bud / hatch collar at the root (shown while it telegraphs and at the root of the live whip)
    public static Sprite LashRoot(int world, int frame, bool bold)
    {
        int key = Key(Kind.Root, world, 0, frame & 1, bold);
        Sprite s;
        if (Cached(key, out s)) return s;
        s = AttackArt.LashRoot(world, frame & 1) ?? RootProcedural(world, frame, bold);
        resolved[key] = s;
        return s;
    }

    static Sprite RootProcedural(int world, int frame, bool bold)
    {
        var look = LookOf(world);
        int f = frame & 1;
        return Made("AtkRoot" + (int)look + "_" + f + (bold ? "b" : ""), LashRootSize, LashRootSize, Ppu, new Vector2(.5f, .5f), (px, w, h) =>
        {
            Color32 edge = PinkEdge, body = PinkBody, hot = f == 0 ? CoreA : CoreB;
            float c = (w - 1) * .5f;
            // eight pink spikes (PC4: a spiked rim, never a smooth disc) round a small material hub
            for (int k = 0; k < 8; k++)
            {
                float a = (k * 45f + f * 22.5f) * Mathf.Deg2Rad;
                for (float d = 3f; d <= 9.2f; d += .5f)
                {
                    Color32 col = d > 6.5f ? edge : (d > 4.5f ? body : hot);
                    Dot(px, w, h, Mathf.RoundToInt(c + Mathf.Cos(a) * d), Mathf.RoundToInt(c + Mathf.Sin(a) * d), col);
                }
            }
            for (int y = -3; y <= 3; y++)
                for (int x = -3; x <= 3; x++)
                {
                    if (x * x + y * y > 11) continue;
                    Dot(px, w, h, (int)c + x, (int)c + y, (x * x + y * y <= 2) ? hot : (x * x + y * y >= 8 ? body : Material(look, world, (x + y + 8) / 2, (x + y) % 2 == 0)));
                }
        }, bold);
    }

    // one dot of the dotted arc (the tell's sweep line), pink-white
    public static Sprite LashDash(int world, int frame, bool bold)
    {
        int key = Key(Kind.Dash, world, 0, frame & 1, bold);
        Sprite s;
        if (Cached(key, out s)) return s;
        s = AttackArt.LashDash(world, frame & 1) ?? DashProcedural(frame, bold);
        resolved[key] = s;
        return s;
    }

    static Sprite DashProcedural(int frame, bool bold)
    {
        int f = frame & 1;
        return Made("AtkDash" + f + (bold ? "b" : ""), LashDashSize, LashDashSize, Ppu, new Vector2(.5f, .5f), (px, w, h) =>
        {
            Color32 a = f == 0 ? CoreB : PinkEdge, b = PinkBody;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int d = Mathf.Abs(x - 2) + Mathf.Abs(y - 2);
                    int d2 = Mathf.Abs(x - 3) + Mathf.Abs(y - 3);
                    int m = Mathf.Min(d, d2);
                    if (m <= 1) px[y * w + x] = a; else if (m == 2) px[y * w + x] = b;
                }
        }, bold);
    }
}
