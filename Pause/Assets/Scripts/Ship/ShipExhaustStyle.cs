using UnityEngine;

// How each ship's engine exhaust looks: its theme, colours and flipbook.
//
// The hull (ShipHullArt's identity hue), the ultimate (WeaponStyleTable) and
// the exhaust read as one theme per ship: every row's outer colour is one of
// the ship's hue tones and its mid/accent come from the ship's weapon family
// palette (ExhaustStyleTest checks both). The art is one atlas,
// Resources/ShipArt/Exhaust/exhaust_atlas.png, generated together with the
// GENERATED block below by Art/Resources/ShipArt/Hulls/src~/exhaust.py.
//
// Nozzle ships draw a plume per nozzle (ShipNozzles) from the Plume strip,
// and the PlumeBoost strip (longer, hotter) while boosting. The two spinners
// (Ninja, UFO) have no nozzle plume: a spin drift (ShipSpinDrift) draws a
// Ring strip that turns with the hull and a world-aligned Wake strip below
// it, each with a boost variant.
//
// Indexed by ShipId; index 0 (ShipId.None) borrows the starter's row.
public enum ExhaustKind
{
    CometTrail,       // Neon Comet: the Akira red tail-light streak
    VoltZigzag,       // Volt Viper: crackling zig-zag electric ribbon
    SolarTongues,     // Solar Fang: flaring solar flame tongues
    HaloRings,        // Crimson Halo: rail spike threaded with capacitor rings
    IonLance,         // Ion Lancer: dead-straight ion beam with pulse nodes
    GhostWisp,        // Jade Phantom: wispy waving ghost trail
    Afterburner,      // Gold Warden: chunky cone with Mach shock diamonds
    ForkedLightning,  // Lightning: a bolt with forks and spark chips
    Blowtorch,        // Ligher: tight jet with a hard cyan torch cone
    PodJets,          // Paranoid: twin pod jets through a watching hex iris
    GatlingSmoke,     // Saboteur: stuttering muzzle flash and smoke cels
    FeatherStreaks,   // Dove: feathery vanes around a slim streak
    HexJet,           // Turtle: a jet shedding angular hex shell cells
    SpinBlades,       // Ninja (spin drift): blade-arc afterimages, curling cuts
    SpinVortex,       // UFO (spin drift): orbiting light ring, tractor shimmer, vortex
}

public enum ExhaustLayer { Plume, PlumeBoost, Ring, RingBoost, Wake, WakeBoost }

public struct ExhaustStyle
{
    public int shipId;
    public string key;            // ShipId.KeyOf(shipId)
    public ExhaustKind kind;
    // outer: the plume's silhouette (a ship hue tone); mid: the weapon's
    // energy / the hue's highlight; core: the hot centre; dark: the hue's
    // shadow (smoke, ring backs); accent: sparks, diamonds, tips.
    public Color outer, mid, core, dark, accent;
    public int[] ticks;           // hold per drawing, 24 fps ticks
    public int loopTicks;

    public bool IsSpinDrift { get { return kind == ExhaustKind.SpinBlades || kind == ExhaustKind.SpinVortex; } }
}

// The one place a ship's exhaust colours are looked up. Code that needs an
// exhaust colour (renderer tints, tests, FX that match the exhaust) asks here.
//
// The atlas is drawn in each ship's stock palette (Stock). The exhaust
// follows the ship's skin "somewhat" (For): the flame keeps its themed shape,
// its hot core and any friendly-red accent, while the outer, mid and dark
// bands move SkinBlend of the way toward the skin's base, highlight and
// shadow (hue a little further, so the band reads as the skin's colour; the
// bands are never darkened below the flame's own brightness). The renderers
// get there by a palette-remap shader (ExhaustRemap), not new art. Stock
// skin = the stock palette exactly.
//
// Tint is the multiplier every exhaust renderer (plumes, boost plumes, spin
// drift) is drawn with, white = exactly as drawn.
public static class ExhaustColors
{
    public struct Palette
    {
        public Color outer, mid, core, dark, accent;

        public Color this[int band]
        {
            get
            {
                switch (band)
                {
                    case 0: return outer;
                    case 1: return mid;
                    case 2: return core;
                    case 3: return dark;
                    default: return accent;
                }
            }
        }
        public const int Bands = 5;
    }

    // How far the exhaust's colour bands move toward the skin (0 = stock
    // colours whatever the skin, 1 = the skin's colours). The one knob.
    public const float SkinBlend = .5f;
    // Hue travels this much faster than lightness and chroma, so a band at
    // SkinBlend already reads as the skin's colour rather than a muddy mix.
    public const float HueLead = 1.6f;
    // A non-red accent (sparks, tips) moves this fraction of SkinBlend.
    public const float AccentShare = .5f;

    // The hull's friendly red (lights, trim): an accent in it never changes.
    public static readonly Color FriendlyRed = new Color(216f / 255f, 35f / 255f, 44f / 255f);

    // The colours the atlas is drawn in.
    public static Palette Stock(int shipId)
    {
        var s = ShipExhaustStyle.For(shipId);
        return new Palette { outer = s.outer, mid = s.mid, core = s.core, dark = s.dark, accent = s.accent };
    }

    // The exhaust's colours as shown now: the ship's shown skin (equipped,
    // or the dock's preview).
    public static Palette For(int shipId)
    {
        return For(shipId, ShipId.IsValid(shipId) ? ShipSkins.Shown(shipId) : ShipSkins.Stock);
    }

    public static bool IsStock(int shipId, int skin)
    {
        return skin == ShipSkins.Stock || !ShipSkins.Has(shipId, skin);
    }

    public static Palette For(int shipId, int skin)
    {
        var p = Stock(shipId);
        if (IsStock(shipId, skin)) return p;
        var s = ShipSkins.Get(shipId, skin);
        Color accentTarget = s.kind == ShipSkinKind.Special ? s.accentColor : s.highlight;
        return new Palette
        {
            outer = Toward(p.outer, s.primary, SkinBlend, true),
            mid = Toward(p.mid, s.highlight, SkinBlend, true),
            core = p.core,
            dark = Toward(p.dark, s.shadow, SkinBlend, false),
            accent = IsFriendlyRed(p.accent) ? p.accent : Toward(p.accent, accentTarget, SkinBlend * AccentShare, true),
        };
    }

    public static bool IsFriendlyRed(Color c)
    {
        return Mathf.Abs(c.r - FriendlyRed.r) < .01f && Mathf.Abs(c.g - FriendlyRed.g) < .01f
            && Mathf.Abs(c.b - FriendlyRed.b) < .01f;
    }

    public static Color Tint(int shipId)
    {
        return new Color(1f, 1f, 1f, .96f);
    }

    // ------------------------------------------------------------ colour math
    //
    // `from` moved `t` of the way to `to` in OKLCh: lightness and chroma by
    // t, hue (shortest way round) by t * HueLead. `keepBright`: a flame band
    // never goes darker than it was drawn, so a dark skin colour (Night's
    // indigo) is taken at the band's own brightness.
    public static Color Toward(Color from, Color to, float t, bool keepBright)
    {
        Vector3 a = ToOklab(from), b = ToOklab(to);
        if (keepBright && b.x < a.x) b.x = a.x;
        float ca = Mathf.Sqrt(a.y * a.y + a.z * a.z), cb = Mathf.Sqrt(b.y * b.y + b.z * b.z);
        float ha = Mathf.Atan2(a.z, a.y), hb = Mathf.Atan2(b.z, b.y);
        const float grey = .02f;     // below this chroma a hue means nothing
        if (cb < grey) hb = ha;
        if (ca < grey) ha = hb;
        float dh = Mathf.Repeat(hb - ha + Mathf.PI, 2f * Mathf.PI) - Mathf.PI;
        float h = ha + dh * Mathf.Min(1f, t * HueLead);
        float c = Mathf.Lerp(ca, cb, t);
        float l = Mathf.Lerp(a.x, b.x, t);
        var result = FromOklab(new Vector3(l, c * Mathf.Cos(h), c * Mathf.Sin(h)));
        result.a = from.a;
        return result;
    }

    static float ToLinear(float c) { return c <= .04045f ? c / 12.92f : Mathf.Pow((c + .055f) / 1.055f, 2.4f); }
    static float ToGamma(float c)
    {
        c = Mathf.Clamp01(c);
        return c <= .0031308f ? c * 12.92f : 1.055f * Mathf.Pow(c, 1f / 2.4f) - .055f;
    }

    public static Vector3 ToOklab(Color c)
    {
        float r = ToLinear(c.r), g = ToLinear(c.g), b = ToLinear(c.b);
        float l = Cbrt(.4122214708f * r + .5363325363f * g + .0514459929f * b);
        float m = Cbrt(.2119034982f * r + .6806995451f * g + .1073969566f * b);
        float s = Cbrt(.0883024619f * r + .2817188376f * g + .6299787005f * b);
        return new Vector3(.2104542553f * l + .7936177850f * m - .0040720468f * s,
                           1.9779984951f * l - 2.4285922050f * m + .4505937099f * s,
                           .0259040371f * l + .7827717662f * m - .8086757660f * s);
    }

    public static Color FromOklab(Vector3 lab)
    {
        float l = lab.x + .3963377774f * lab.y + .2158037573f * lab.z;
        float m = lab.x - .1055613458f * lab.y - .0638541728f * lab.z;
        float s = lab.x - .0894841775f * lab.y - 1.2914855480f * lab.z;
        l = l * l * l; m = m * m * m; s = s * s * s;
        return new Color(ToGamma(4.0767416621f * l - 3.3077115913f * m + .2309699292f * s),
                         ToGamma(-1.2684380046f * l + 2.6097574011f * m - .3413193965f * s),
                         ToGamma(-.0041960863f * l - .5034186967f * m + 1.5153396970f * s), 1f);
    }

    static float Cbrt(float x) { return x < 0f ? -Mathf.Pow(-x, 1f / 3f) : Mathf.Pow(x, 1f / 3f); }
}

public static class ShipExhaustStyle
{
    public const string AtlasPath = "ShipArt/Exhaust/exhaust_atlas";
    public const float PixelsPerUnit = 100f;
    // Flame head: 3 u below the top of a 128 u plume frame; the wake's head
    // 4 u below the top of a 64 u spinner frame.
    public const float PlumeHeadPivotY = 1f - 3f / 128f;
    public const float WakeHeadPivotY = 1f - 4f / 64f;
    // Boost plumes are drawn longer and are shown longer still.
    public const float BoostLength = 1.15f;
    public const int LayerCount = 6;

    struct Strip
    {
        public int shipId;
        public ExhaustLayer layer;
        public int x, y, w, h, frames, stride;
        public Strip(int shipId, ExhaustLayer layer, int x, int y, int w, int h, int frames, int stride)
        {
            this.shipId = shipId; this.layer = layer; this.x = x; this.y = y;
            this.w = w; this.h = h; this.frames = frames; this.stride = stride;
        }
    }

    static Color Hex(string hex)
    {
        Color c;
        return ColorUtility.TryParseHtmlString(hex, out c) ? c : Color.white;
    }

    static ExhaustStyle Row(int id, string key, ExhaustKind kind, string outer, string mid, string core,
                            string dark, string accent, int[] ticks)
    {
        int loop = 0;
        for (int i = 0; i < ticks.Length; i++) loop += ticks[i];
        return new ExhaustStyle
        {
            shipId = id, key = key, kind = kind,
            outer = Hex(outer), mid = Hex(mid), core = Hex(core), dark = Hex(dark), accent = Hex(accent),
            ticks = ticks, loopTicks = loop,
        };
    }

    static readonly ExhaustStyle[] table =
    {
        // BEGIN GENERATED EXHAUST
        /*  1 NeonComet    */ Row(1, "NeonComet", ExhaustKind.CometTrail, "#D8232C", "#F2862B", "#F4EAD4", "#86121F", "#6EF2EE", new[] { 2, 2, 1, 2, 2, 2 }),
        /*  2 VoltViper    */ Row(2, "VoltViper", ExhaustKind.VoltZigzag, "#2E9BE6", "#6EF2EE", "#F4EAD4", "#174C8C", "#F2862B", new[] { 2, 2, 1, 2, 2, 2, 2 }),
        /*  3 SolarFang    */ Row(3, "SolarFang", ExhaustKind.SolarTongues, "#F07A1E", "#FFB43C", "#F4EAD4", "#9A3E12", "#F2862B", new[] { 3, 2, 1, 2, 2, 2, 2 }),
        /*  4 CrimsonHalo  */ Row(4, "CrimsonHalo", ExhaustKind.HaloRings, "#C8285E", "#FFB43C", "#F4EAD4", "#6E1136", "#FF7AA2", new[] { 2, 2, 1, 2, 2, 2 }),
        /*  5 IonLancer    */ Row(5, "IonLancer", ExhaustKind.IonLance, "#3A5BE0", "#6EF2EE", "#F4EAD4", "#1C2A80", "#9AB0FF", new[] { 3, 2, 1, 2, 2, 2 }),
        /*  6 JadePhantom  */ Row(6, "JadePhantom", ExhaustKind.GhostWisp, "#22B07A", "#8EF0C4", "#F4EAD4", "#0E5A44", "#1FB5B9", new[] { 3, 2, 1, 2, 2, 2, 2, 2 }),
        /*  7 GoldWarden   */ Row(7, "GoldWarden", ExhaustKind.Afterburner, "#D99A1A", "#FFD36A", "#F4EAD4", "#7E4E0C", "#D8232C", new[] { 2, 2, 1, 2, 2, 2 }),
        /*  8 Lightning    */ Row(8, "Lightning", ExhaustKind.ForkedLightning, "#EEDC32", "#6EF2EE", "#F4EAD4", "#8E8414", "#F2862B", new[] { 2, 2, 1, 2, 2, 2, 2 }),
        /*  9 Ligher       */ Row(9, "Ligher", ExhaustKind.Blowtorch, "#C8662E", "#FFB43C", "#F4EAD4", "#6E3014", "#6EF2EE", new[] { 3, 2, 1, 2, 2, 2 }),
        /* 10 Paranoid     */ Row(10, "Paranoid", ExhaustKind.PodJets, "#5DBB3A", "#1FB5B9", "#B6EE8A", "#2A5E1E", "#F4EAD4", new[] { 2, 2, 1, 2, 2, 2 }),
        /* 11 Ninja        */ Row(11, "Ninja", ExhaustKind.SpinBlades, "#4A58C8", "#9FA8F0", "#F4EAD4", "#232A6E", "#D8232C", new[] { 2, 2, 1, 2, 2, 2, 2, 2 }),
        /* 12 Saboteur     */ Row(12, "Saboteur", ExhaustKind.GatlingSmoke, "#B83CC0", "#F2862B", "#F4EAD4", "#5E1A66", "#3A2A5C", new[] { 2, 2, 1, 2, 2, 2, 2, 2 }),
        /* 13 UFO          */ Row(13, "UFO", ExhaustKind.SpinVortex, "#7A52DC", "#BBA4FF", "#F4EAD4", "#3A2478", "#6EF2EE", new[] { 2, 2, 1, 2, 2, 2, 2, 2 }),
        /* 14 Dove         */ Row(14, "Dove", ExhaustKind.FeatherStreaks, "#36C2C2", "#A6F2EE", "#F4EAD4", "#146A70", "#FFB43C", new[] { 3, 2, 1, 2, 2, 2, 2 }),
        /* 15 Turtle       */ Row(15, "Turtle", ExhaustKind.HexJet, "#1FB5B9", "#6EF2EE", "#F4EAD4", "#5A3A18", "#E2BE84", new[] { 2, 2, 1, 2, 2, 2, 2 }),
    };

    // Atlas strips: ship, layer, x, y (bottom-up), frame w, h, frames, stride (px).
    static readonly Strip[] strips =
    {
        new Strip(1, ExhaustLayer.Plume, 0, 1280, 48, 192, 6, 50),
        new Strip(1, ExhaustLayer.PlumeBoost, 300, 1280, 48, 192, 6, 50),
        new Strip(2, ExhaustLayer.Plume, 600, 1280, 48, 192, 7, 50),
        new Strip(2, ExhaustLayer.PlumeBoost, 950, 1280, 48, 192, 7, 50),
        new Strip(3, ExhaustLayer.Plume, 1300, 1280, 48, 192, 7, 50),
        new Strip(3, ExhaustLayer.PlumeBoost, 1650, 1280, 48, 192, 7, 50),
        new Strip(4, ExhaustLayer.Plume, 0, 1086, 48, 192, 6, 50),
        new Strip(4, ExhaustLayer.PlumeBoost, 300, 1086, 48, 192, 6, 50),
        new Strip(5, ExhaustLayer.Plume, 600, 1086, 48, 192, 6, 50),
        new Strip(5, ExhaustLayer.PlumeBoost, 900, 1086, 48, 192, 6, 50),
        new Strip(6, ExhaustLayer.Plume, 1200, 1086, 48, 192, 8, 50),
        new Strip(6, ExhaustLayer.PlumeBoost, 1600, 1086, 48, 192, 8, 50),
        new Strip(7, ExhaustLayer.Plume, 0, 892, 48, 192, 6, 50),
        new Strip(7, ExhaustLayer.PlumeBoost, 300, 892, 48, 192, 6, 50),
        new Strip(8, ExhaustLayer.Plume, 600, 892, 48, 192, 7, 50),
        new Strip(8, ExhaustLayer.PlumeBoost, 950, 892, 48, 192, 7, 50),
        new Strip(9, ExhaustLayer.Plume, 1300, 892, 48, 192, 6, 50),
        new Strip(9, ExhaustLayer.PlumeBoost, 1600, 892, 48, 192, 6, 50),
        new Strip(10, ExhaustLayer.Plume, 0, 698, 48, 192, 6, 50),
        new Strip(10, ExhaustLayer.PlumeBoost, 300, 698, 48, 192, 6, 50),
        new Strip(11, ExhaustLayer.Ring, 0, 382, 120, 120, 8, 122),
        new Strip(11, ExhaustLayer.RingBoost, 976, 382, 120, 120, 8, 122),
        new Strip(11, ExhaustLayer.Wake, 0, 260, 120, 120, 8, 122),
        new Strip(11, ExhaustLayer.WakeBoost, 976, 260, 120, 120, 8, 122),
        new Strip(12, ExhaustLayer.Plume, 600, 698, 48, 192, 8, 50),
        new Strip(12, ExhaustLayer.PlumeBoost, 1000, 698, 48, 192, 8, 50),
        new Strip(13, ExhaustLayer.Ring, 0, 138, 120, 120, 8, 122),
        new Strip(13, ExhaustLayer.RingBoost, 976, 138, 120, 120, 8, 122),
        new Strip(13, ExhaustLayer.Wake, 0, 16, 120, 120, 8, 122),
        new Strip(13, ExhaustLayer.WakeBoost, 976, 16, 120, 120, 8, 122),
        new Strip(14, ExhaustLayer.Plume, 1400, 698, 48, 192, 7, 50),
        new Strip(14, ExhaustLayer.PlumeBoost, 0, 504, 48, 192, 7, 50),
        new Strip(15, ExhaustLayer.Plume, 350, 504, 48, 192, 7, 50),
        new Strip(15, ExhaustLayer.PlumeBoost, 700, 504, 48, 192, 7, 50),
        // END GENERATED EXHAUST
    };

    static Texture2D atlas;
    static readonly Sprite[][] sprites = new Sprite[16 * LayerCount][];

    public static int Count { get { return table.Length; } }

    public static bool Has(int shipId)
    {
        return ShipId.IsValid(shipId) && IndexOf(shipId) >= 0;
    }

    static int IndexOf(int shipId)
    {
        for (int i = 0; i < table.Length; i++) if (table[i].shipId == shipId) return i;
        return -1;
    }

    public static ExhaustStyle For(int shipId)
    {
        int i = IndexOf(shipId);
        if (i < 0) i = IndexOf(ShipId.Starter);
        return table[i];
    }

    public static bool IsSpinDrift(int shipId)
    {
        return Has(shipId) && For(shipId).IsSpinDrift;
    }

    static int StripOf(int shipId, ExhaustLayer layer)
    {
        for (int i = 0; i < strips.Length; i++)
            if (strips[i].shipId == shipId && strips[i].layer == layer) return i;
        return -1;
    }

    public static bool HasLayer(int shipId, ExhaustLayer layer) { return StripOf(shipId, layer) >= 0; }

    public static int FrameCount(int shipId, ExhaustLayer layer)
    {
        int s = StripOf(shipId, layer);
        return s >= 0 ? strips[s].frames : 0;
    }

    public static Texture2D Atlas
    {
        get
        {
            if (atlas == null) atlas = Resources.Load<Texture2D>(AtlasPath);
            return atlas;
        }
    }

    static Vector2 PivotOf(ExhaustLayer layer)
    {
        switch (layer)
        {
            case ExhaustLayer.Plume:
            case ExhaustLayer.PlumeBoost: return new Vector2(.5f, PlumeHeadPivotY);
            case ExhaustLayer.Wake:
            case ExhaustLayer.WakeBoost: return new Vector2(.5f, WakeHeadPivotY);
            default: return new Vector2(.5f, .5f);
        }
    }

    // Drawing `frame` of a ship's strip (wraps), cached; null if the ship has
    // no such layer or the atlas is missing.
    public static Sprite Frame(int shipId, ExhaustLayer layer, int frame)
    {
        if (!ShipId.IsValid(shipId)) shipId = ShipId.Starter;
        int s = StripOf(shipId, layer);
        if (s < 0) return null;
        var strip = strips[s];
        int slot = shipId * LayerCount + (int)layer;
        if (slot >= sprites.Length) return null;
        var row = sprites[slot];
        if (row == null) row = sprites[slot] = new Sprite[strip.frames];
        frame = ((frame % strip.frames) + strip.frames) % strip.frames;
        if (row[frame] == null)
        {
            var tex = Atlas;
            if (tex == null) return null;
            var rect = new Rect(strip.x + frame * strip.stride, strip.y, strip.w, strip.h);
            row[frame] = Sprite.Create(tex, rect, PivotOf(layer), PixelsPerUnit, 0, SpriteMeshType.FullRect);
            row[frame].name = strip.shipId + "_" + layer + "_" + frame;
        }
        return row[frame];
    }

    // Cuts every drawing of every strip the ship has (cruise, boost, a
    // spinner's ring and wake) now -- a ship being dressed -- so the flipbook
    // never creates a sprite mid-flight; the boost drawings were otherwise
    // first cut on the frames after a blue atom.
    public static void Prewarm(int shipId)
    {
        if (!ShipId.IsValid(shipId)) shipId = ShipId.Starter;
        for (int s = 0; s < strips.Length; s++)
        {
            if (strips[s].shipId != shipId) continue;
            for (int f = 0; f < strips[s].frames; f++) Frame(shipId, strips[s].layer, f);
        }
    }

    // The drawing to show `ticks` (24 fps) into a ship's loop.
    public static int FrameAt(int shipId, float ticks)
    {
        var style = For(shipId);
        int at = Mathf.FloorToInt(Mathf.Repeat(ticks, style.loopTicks));
        for (int i = 0; i < style.ticks.Length; i++)
        {
            at -= style.ticks[i];
            if (at < 0) return i;
        }
        return 0;
    }

    // The loop's 1-tick smear drawing (every loop has exactly one).
    public static int SmearFrame(int shipId)
    {
        var t = For(shipId).ticks;
        for (int i = 0; i < t.Length; i++) if (t[i] == 1) return i;
        return -1;
    }
}
