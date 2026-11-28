using UnityEngine;

// Frost's LIVING PROPS (user, 2026-11-28: colour and life from overlay things pinned to the ground, "like abandoned science
// centers, whales popping their head up"; the ground tiles are never recoloured, nothing pink): six small animated
// drawings standing on the ground like the landmarks -- PINNED to the mid tile (same rate, same scroll, placed by the
// same GroundPlanner, so they never overlap a landmark or a dock), drawn at the landmarks' scale (FrostTuning.PieceScale)
// and graded like them (the "props" layer, Graded(1)).
//
//   whale, orca        (open water) one-shots: a faint shadow under the surface (frame 0) for a random WaitMin..WaitMax s,
//                      then they surface, blow and dive (frames 1..7) and go back to the shadow
//   icebreaker         (open water) its churning wake and funnel puff loop; lit bridge windows, a masthead strobe
//   station, lab,      (a flat top, mask O, with a clear rim, like every landmark) loop: the drawing's own blinking beacon,
//   plane               flickering window, pulsing panels -- backed by lights (Lights below) so they read at phone size
//
// RARE: an accent, not clutter. At most Pool (2) stand on the ground at once (in view or just above it), one every
// GapMin..GapMax of scrolled ground (the view is ~16.5 u tall), so one or two are in view and often none. Nothing that
// floats comes to the glacier (v4: its dark mask class is the crevasses).
// Art: Backdrop3/props (128 px cells, <name>_00..07, frost_redraw/props/build_props_atlas.py from the approved strips).
// Animated from each piece's own age (scaled dt): a paused game is a still frame. Nothing is allocated per frame.
public static class FrostPropsTuning
{
    public const string Atlas = "props";
    public const string Layer = "props";
    public static readonly string[] Names = { "whale", "orca", "station", "lab", "icebreaker", "plane" };
    public const int Whale = 0, Orca = 1, Station = 2, Lab = 3, Icebreaker = 4, Plane = 5;
    public const int Frames = 8;
    public const float CellPx = 128f;

    // where each may stand: open water (mask W) or a flat top (mask O)
    public static readonly bool[] Water = { true, true, false, false, true, false };
    // the whales surface, blow and dive once, then wait; the rest loop
    public static readonly bool[] OneShot = { true, true, false, false, false, false };
    // ms per frame (frost props meta.json, the approved previews). A one-shot's frame 0 is its wait (WaitMin..WaitMax), not this.
    public static readonly int[][] FrameMs =
    {
        new[] { 1400, 110, 110, 130, 160, 150, 130, 150 },
        new[] { 1200, 110, 120, 140, 160, 140, 120, 140 },
        new[] { 125, 125, 125, 125, 125, 125, 125, 125 },
        new[] { 125, 125, 125, 125, 125, 125, 125, 125 },
        new[] { 120, 120, 120, 120, 120, 120, 120, 120 },
        new[] { 125, 125, 125, 125, 125, 125, 125, 125 },
    };
    public static float WaitMin = 3f, WaitMax = 8f;

    // Drawn width (a 128 cell) x FrostTuning.PieceScale: the approved previews' sizes on a 1080 px phone (whale 54 px,
    // orca 46, station 62, lab 58, icebreaker 58, plane 56; a 256 landmark cell is ~65 px), ~.75-1 x the landmarks' range.
    public static readonly float[] SizeK = { 1.30f, 1.10f, 1.49f, 1.39f, 1.39f, 1.34f };
    public const float SizeJitter = .06f;
    public static float Size(int kind) { return SizeK[kind] * FrostTuning.PieceScale; }
    // where the slab / ripples sit (share of the drawn width from the centre, y up): measured on the strips (rows >= 70% of
    // the widest, all frames)
    public static readonly float[] Feet = { 0f, 0f, -.06f, -.08f, 0f, .02f };
    // footprint half-size as a share of the drawn width: the whales' ripple rings and the icebreaker's wake fill most of the
    // cell, so the water they need is wider than a landmark's slab; the structures stand on their slab like a landmark
    public const float WaterCore = .42f, LandCore = .34f;
    // the ground a prop CLAIMS (GroundPlanner footprint) is its whole drawing, so no landmark or dock is later placed over it
    public const float ClaimCore = .5f;
    public static readonly int[] Weights = { 3, 2, 2, 2, 2, 2 };
    public const int Pool = 2;
    public static float GapMin = 9f, GapMax = 16f;        // scrolled ground (world units) between two props
    public const int Tries = 40;

    public static PieceRule Rule(int kind)
    {
        var r = Water[kind] ? new PieceRule(GroundClass.Water, 1f, WaterCore) : new PieceRule(GroundClass.Open, FrostTuning.LandNeed, LandCore);
        return r.WithRim(FrostTuning.RimScale, FrostTuning.RimNeed).WithFoot(Feet[kind]);
    }

    public static bool Fits(int kind, int variant) { return kind >= 0 && kind < Names.Length && !(Water[kind] && FrostTuning.IsGlacier(variant)); }

    // The prop kind of a props-atlas sprite ("whale_03" -> Whale), -1 for none; and its frame.
    public static int KindOf(string sprite)
    {
        if (string.IsNullOrEmpty(sprite)) return -1;
        for (int i = 0; i < Names.Length; i++)
            if (sprite.Length == Names[i].Length + 3 && sprite.StartsWith(Names[i]) && sprite[Names[i].Length] == '_') return i;
        return -1;
    }
    public static int FrameOf(string sprite)
    {
        int k = KindOf(sprite);
        return k < 0 ? -1 : (sprite[sprite.Length - 2] - '0') * 10 + (sprite[sprite.Length - 1] - '0');
    }

    // A one-shot's surfacing (frames 1..7) in s; a loop's cycle.
    public static float ShowSeconds(int kind)
    {
        int ms = 0;
        for (int f = OneShot[kind] ? 1 : 0; f < Frames; f++) ms += FrameMs[kind][f];
        return ms / 1000f;
    }

    // ---- LIGHTS: FrostAmbient-style emitters riding the props ----
    // The drawings' baked lamps are 1-2 px at phone size; a soft glow (FrostGroundLights' sprite, tinted) sits on each, a
    // child of the prop's body (scrolls, scales, mirrors with it). Kinds:
    //   x  red beacon, lit exactly on the frames the drawing lights it (LitMask bit per frame)
    //   w  amber window, a gentle flicker (FrostGroundLights 'w')
    //   s  white masthead strobe, one short flash every few s (FrostGroundLights 's')
    // Never lifted by the grade, alpha <= Peak, never pink (WorldCue.IsPink), nothing faster than FrostGroundLightsTuning.MaxHz.
    public struct Light
    {
        public int prop;
        public Vector2 at;        // cell pixels from the top-left of the 128 cell
        public char kind;
        public float px;          // glow width in cell pixels
        public int litMask;       // 'x': frames the drawing shows its lamp lit
    }
    public const int MaxLights = 3;
    public static readonly Light[] Lights =
    {
        L(Station, 103, 4, 'x', 18, 0x03),       // the red mast beacon (lit frames 0-1)
        L(Station, 77, 66, 'w', 14),             // the amber windows of the hut
        L(Station, 89, 73, 'w', 14),
        L(Lab, 56, 11, 'x', 18, 0x03),           // the mast tip
        L(Plane, 33, 14, 'x', 18, 0x13),         // the red tail distress beacon (frames 0-1 and 4)
        L(Icebreaker, 48, 46, 'w', 15),          // the lit bridge windows
        L(Icebreaker, 61, 53, 'w', 15),
        L(Icebreaker, 60, 15, 's', 15),          // the masthead light
    };
    static Light L(int prop, float x, float y, char kind, float px, int mask = 0)
    {
        return new Light { prop = prop, at = new Vector2(x, y), kind = kind, px = px, litMask = mask };
    }

    public static readonly Color Red = new Color(1f, .27f, .2f, 1f);
    public const float Peak = .9f, BeaconLow = .1f, WindowLow = .5f, WindowPeak = .8f;
    public static Color ColorOf(char k) { return k == 'x' ? Red : k == 's' ? FrostGroundLightsTuning.White : FrostGroundLightsTuning.Amber; }

    // A light's alpha (before the set's fade) on frame `frame` at the prop's age `t`.
    public static float Alpha(Light l, int frame, float t, uint seed)
    {
        switch (l.kind)
        {
            case 'x': return ((l.litMask >> frame) & 1) != 0 ? Peak : BeaconLow;
            case 'w': return Mathf.Lerp(WindowLow, WindowPeak, FrostGroundLights.Level(FrostGroundLightsTuning.Kinds.IndexOf('w'), seed, t, 0f, 0f));
            default: return Peak * FrostGroundLights.Level(FrostGroundLightsTuning.Kinds.IndexOf(l.kind), seed, t, 0f, 0f);
        }
    }
}

public partial class FrostDirector
{
    BackdropPool props;
    Sprite[][] propFrames;                 // per kind: its 8 cells (null: not in the atlas)
    PieceRule[] propRules;                 // per pool slot: the rule it was placed by
    int[] propKind;                        // per pool slot
    float[] propSurface;                   // per pool slot, one-shots: the age its next surfacing starts
    SpriteRenderer[][] propLights;         // per pool slot: MaxLights glow renderers under its body
    int[][] propLight;                     // per pool slot, per renderer: index into FrostPropsTuning.Lights, -1 off
    uint[] propSeed;
    Texture2D propGlowTex;
    Sprite propGlow;
    float propTravel, propGap;
    int lastProp = -1;
    System.Random propRng;                 // its own stream: a prop never shifts the landmarks' or the weather's dice

    public BackdropPool Props => props;
    public int PropsSpawned { get; private set; }
    public int PropKind(int slot) { return propKind != null && slot >= 0 && slot < propKind.Length ? propKind[slot] : -1; }
    public PieceRule PropRule(int slot) { return propRules[slot]; }
    public SpriteRenderer[] PropLightRenderers(int slot) { return propLights[slot]; }

    float PRand(float a, float b) { return a + (float)propRng.NextDouble() * (b - a); }

    void BuildProps()
    {
        propRng = new System.Random((SeedOverride > 0 ? SeedOverride : 1989) * 53 + 11);
        var art = set.Atlas(FrostPropsTuning.Atlas);
        propFrames = new Sprite[FrostPropsTuning.Names.Length][];
        for (int k = 0; k < propFrames.Length; k++)
        {
            var f = art.Frames(FrostPropsTuning.Names[k]);
            propFrames[k] = f.Length >= FrostPropsTuning.Frames ? f : null;
        }
        props = Pool(FrostPropsTuning.Layer, FrostPropsTuning.Pool);
        int n = props.items.Count;
        propRules = new PieceRule[n];
        propKind = new int[n];
        propSurface = new float[n];
        propSeed = new uint[n];
        propLights = new SpriteRenderer[n][];
        propLight = new int[n][];
        propGlow = FrostGroundLights.MakeSprite(out propGlowTex);
        for (int i = 0; i < n; i++)
        {
            propKind[i] = -1;
            propLights[i] = new SpriteRenderer[FrostPropsTuning.MaxLights];
            propLight[i] = new int[FrostPropsTuning.MaxLights];
            for (int j = 0; j < FrostPropsTuning.MaxLights; j++)
            {
                var go = new GameObject("proplight" + j);
                go.transform.SetParent(props.items[i].body, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = propGlow;
                sr.sortingOrder = props.items[i].sr.sortingOrder + 1;
                sr.enabled = false;
                propLights[i][j] = sr;
                propLight[i][j] = -1;
            }
        }
        Grade(props, FrostPropsTuning.Layer);
        propGap = PRand(FrostPropsTuning.GapMin, FrostPropsTuning.GapMax);
    }

    void TeardownProps()
    {
        if (propGlow != null) BackdropAtlas.Kill(propGlow);
        if (propGlowTex != null) BackdropAtlas.Kill(propGlowTex);
        propGlow = null;
        propGlowTex = null;
    }

    // the opening view: one prop already standing in the upper middle of the view (the landmarks are dealt around it)
    void OpeningProp()
    {
        double t = planner.Travel;
        SpawnProp(-1, t - HalfH * .15f, t + HalfH * .55f);
    }

    int PickProp(int not)
    {
        int total = 0;
        for (int i = 0; i < FrostPropsTuning.Weights.Length; i++)
            if (i != lastProp && i != not && propFrames[i] != null && FrostPropsTuning.Fits(i, set.Variant)) total += FrostPropsTuning.Weights[i];
        if (total <= 0) return -1;
        int r = propRng.Next(total);
        for (int i = 0; i < FrostPropsTuning.Weights.Length; i++)
        {
            if (i == lastProp || i == not || propFrames[i] == null || !FrostPropsTuning.Fits(i, set.Variant)) continue;
            r -= FrostPropsTuning.Weights[i];
            if (r < 0) return i;
        }
        return -1;
    }

    // A prop (kind < 0: one picked) with its centre in the ground band [lo, hi] where the ground suits it and nothing stands
    // on its whole drawing. A kind that fits nowhere in the band is swapped for another (up to three). False: nothing placed.
    public bool SpawnProp(int kind, double lo, double hi, int tries = FrostPropsTuning.Tries)
    {
        if (props == null || !planner.Ready || props.ActiveCount >= props.Capacity) return false;
        int tried = -1;
        for (int k = 0; k < 3; k++)
        {
            int kd = kind >= 0 ? kind : PickProp(tried);
            if (kd < 0 || !FrostPropsTuning.Fits(kd, set.Variant) || propFrames[kd] == null) return false;
            tried = kd;
            float size = FrostPropsTuning.Size(kd) * (1f + FrostPropsTuning.SizeJitter * PRand(-1f, 1f));
            var rule = FrostPropsTuning.Rule(kd);
            float rx = size * FrostPropsTuning.ClaimCore, ry = rx * .8f;
            float x = 0f; double gy = 0.0; bool found = false;
            for (int t = 0; t < tries && !found; t++)
            {
                float cx = PRand(XMin(size), XMax(size));
                double cy = lo + propRng.NextDouble() * (hi - lo);
                if (!planner.Free(cx, cy, rx, ry) || !planner.Accepts(rule, cx, cy, size)) continue;
                x = cx; gy = cy; found = true;
            }
            if (!found) { if (kind >= 0) return false; continue; }
            int foot = planner.Claim(x, gy, rx, ry);
            if (foot < 0) return false;
            var p = props.Spawn();
            if (p == null) { planner.Release(foot); return false; }
            int i = props.items.IndexOf(p);
            propRules[i] = rule;
            propKind[i] = kd;
            propSeed[i] = (uint)propRng.Next() | 1u;
            propSurface[i] = FrostPropsTuning.OneShot[kd] ? PRand(FrostPropsTuning.WaitMin, FrostPropsTuning.WaitMax) : 0f;
            SetSprite(p, propFrames[kd][0], Mathf.Min(size, BackdropCatalog.MaxLandmarkSize));
            p.body.localScale = propRng.NextDouble() < .5 ? new Vector3(-1f, 1f, 1f) : Vector3.one;
            p.footprint = foot;
            p.core = FrostPropsTuning.ClaimCore;
            p.gy = gy;
            p.x = x;
            p.y = planner.Y(gy);
            p.vx = p.vy = 0f;
            p.rate = set.Spec.Rate(FrostPropsTuning.Layer);
            p.color = tintGround;
            p.dockTint = Color.white;
            p.kind = kd;
            Place(p);
            Tinted(p, tintGround, 1f);
            AttachPropLights(i, kd);
            lastProp = kd;
            PropsSpawned++;
            return true;
        }
        return false;
    }

    // Tests / previews: a prop of `kind` around screen y (searched a little up and down).
    public bool SpawnPropAt(int kind, float y)
    {
        double g = planner.GroundY(y);
        return SpawnProp(kind, g - 1.2, g + 1.2, 400);
    }

    // Tests / previews: every prop gone, its ground released.
    public void ClearProps()
    {
        if (props == null) return;
        foreach (var p in props.items)
        {
            if (!p.active) continue;
            planner.Release(p.footprint);
            p.footprint = -1;
            Despawn(p);
        }
    }

    // Tests / previews: every whale / orca on the ground starts its surfacing now.
    public void SurfaceNow()
    {
        if (props == null) return;
        for (int i = 0; i < props.items.Count; i++)
            if (props.items[i].active && propKind[i] >= 0 && FrostPropsTuning.OneShot[propKind[i]]) propSurface[i] = props.items[i].age;
    }

    void AttachPropLights(int slot, int kind)
    {
        var rs = propLights[slot];
        int j = 0;
        for (int l = 0; l < FrostPropsTuning.Lights.Length && j < rs.Length; l++)
        {
            var li = FrostPropsTuning.Lights[l];
            if (li.prop != kind) continue;
            const float ppu = BackdropAtlas.PixelsPerUnit, half = FrostPropsTuning.CellPx * .5f;
            rs[j].transform.localPosition = new Vector3((li.at.x - half) / ppu, (half - li.at.y) / ppu, 0f);
            float s = li.px / ppu;
            rs[j].transform.localScale = new Vector3(s, s, 1f);
            propLight[slot][j] = l;
            j++;
        }
        for (; j < rs.Length; j++) { propLight[slot][j] = -1; rs[j].enabled = false; }
    }

    // The frame a prop shows at its age (a one-shot's wait / surfacing advanced here).
    int PropFrame(int slot, BackdropPiece p)
    {
        int kind = propKind[slot];
        var ms = FrostPropsTuning.FrameMs[kind];
        if (FrostPropsTuning.OneShot[kind])
        {
            if (p.age < propSurface[slot]) return 0;
            float t = (p.age - propSurface[slot]) * 1000f;
            for (int f = 1; f < FrostPropsTuning.Frames; f++)
            {
                if (t < ms[f]) return f;
                t -= ms[f];
            }
            propSurface[slot] = p.age + PRand(FrostPropsTuning.WaitMin, FrostPropsTuning.WaitMax);
            return 0;
        }
        int cycle = 0;
        for (int f = 0; f < ms.Length; f++) cycle += ms[f];
        float c = (p.age * 1000f) % cycle;
        for (int f = 0; f < ms.Length; f++)
        {
            if (c < ms[f]) return f;
            c -= ms[f];
        }
        return 0;
    }

    void StepProps(float dt, float scrolled)
    {
        if (props == null) return;
        propTravel += scrolled;
        if (planner.Ready && propTravel >= propGap && props.ActiveCount < props.Capacity)
        {
            double lo = planner.Travel + HalfH + FrostTuning.SpawnAbove, hi = lo + FrostTuning.SpawnBand;
            if (SpawnProp(-1, lo, hi)) { propTravel = 0f; propGap = PRand(FrostPropsTuning.GapMin, FrostPropsTuning.GapMax); }
            else propGap = propTravel + FrostTuning.SpawnRetry;
        }
        StepPinned(props, propRules, dt);
        float fade = set.Alpha * FrostGroundLightsTuning.Brightness;
        for (int i = 0; i < props.items.Count; i++)
        {
            var p = props.items[i];
            if (!p.active || propKind[i] < 0) continue;
            int f = PropFrame(i, p);
            var want = propFrames[propKind[i]][f];
            if (p.sr.sprite != want) p.sr.sprite = want;
            var rs = propLights[i];
            for (int j = 0; j < rs.Length; j++)
            {
                int l = propLight[i][j];
                if (l < 0) continue;
                float a = FrostPropsTuning.Alpha(FrostPropsTuning.Lights[l], f, p.age, propSeed[i] * (uint)(j + 1)) * fade;
                if (a <= .004f) { if (rs[j].enabled) rs[j].enabled = false; continue; }
                Color c = FrostPropsTuning.ColorOf(FrostPropsTuning.Lights[l].kind);
                c.a = a;
                rs[j].color = c;
                if (!rs[j].enabled) rs[j].enabled = true;
            }
        }
    }
}
