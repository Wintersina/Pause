using UnityEngine;

// Ember's LIVING PROPS (r1, user-approved 2026-11-28): small animated paintings standing on the repainted high-altitude ground
// (EmberTuning.GroundZoom) -- a geothermal power plant, a lava foundry, a mining rig on a crater rim, a stuck ore-hauler train on a
// trestle over the lava, a heat-shield bunker, an obsidian quarry, a research station on stilts in a lava lake, abandoned colony domes
// half-flooded with cooled lava, a wrecked crawler sinking in a lava pool, a basalt column field, hot-spring terraces, a flare-stack
// vent pad, a MASSIVE crashed starship half-melted into the lava (with its molten crash SCAR) -- and the BIG MOUNTAINS (a volcano, a
// caldera, a range of obsidian spires), pinned hard. They replace the round-1 forge landmarks and pipes (retired: chunky isometric
// art that did not match the new ground). Modelled on DesertProps.cs, with Frost's crash scar.
//
//   PINNED     every prop rides the mid tile (the "ground" layer, its rate; GroundPlanner), never mirrored (the paintings are lit),
//              never bobbing: nothing flies free -- the train is stuck on its trestle, the crawler sinks, the starship is a wreck.
//   CONTEXT    each kind stands where it makes sense (Kind.on, the layout's ctx.txt flags, GroundMask.WithContext): the plant, the
//              foundry and the bunker on FLAT crust; the rig and the vent pad on OPEN crust; the quarry, the domes, the columns and
//              the springs on ROCKY ground; the ore-hauler, the stilts and the crawler IN THE LAVA (mask W: GroundClass.Water); the
//              starship on open crust; the mountains on rocky or open ground. `need` of the footprint must be that context.
//   KEEP-OUT   every prop claims a box of half-size Keep (= .42 x its drawn px + KeepMarginPx, on a 1080 px screen) in the planner,
//              so no eruption, ash pall, launch site / dock or other prop is placed in it (their keep-outs may touch). The eruptions
//              claim their whole drawn cell (the plume's reach) and every pall the whole box it will drift over, so a prop is never
//              placed under a plume or a smoke pall; a pall that would meet a prop is not drawn at all.
//   DATA       a kind is one row of Kinds plus its strips in the layout atlases (v<n>/props: <name>_<variant>_<frame>, cut to the
//              kind's box with the cell's centre as the pivot, ../Art/Worlds/Ember/backdrop_r1~/build_r1.py); the count of variants
//              and frames comes from the atlas. A layout atlas without a kind simply never deals it.
//   VARIANTS   each copy's variant is a hash of its spawn count and kind, never the previous copy's of that kind. Each copy starts at
//              its own animation phase.
//   LIGHT      the lamps, windows and lava are baked into the 8 frames (beacons blink at 1 Hz, strobes once a second, lava shimmers,
//              nothing flashes the whole field); the props are drawn at PropTint (under the shots' full-strength cores).
//   SCAR       the starship drags a crash scar (scar atlas, scar_0..3 one per livery): a ground decal child of its body, feathered
//              into the ground, its keep-out boxes (ScarBoxes) claimed on the planner beside the wreck's own.
// Animated from each piece's own age (scaled dt): a paused game is a still frame. Nothing is allocated per frame.
public static class EmberPropsTuning
{
    public const string Atlas = "props";             // in each layout folder (v<n>/props.png + .json)
    public const string Layer = "ground";
    public enum Place { Flat, Open, Rocky, Lava, Mountain }
    public struct Kind
    {
        public string name;
        public float px;          // drawn cell width on a 1080 px screen (as approved at .41)
        public Place on;
        public float perThree;    // copies per three screens of ground
        public bool mountain;
    }
    public const int Geoplant = 0, Foundry = 1, Miningrig = 2, Orehauler = 3, Bunker = 4, Quarry = 5, Stilts = 6, Domes = 7, Crawler = 8,
                     Columns = 9, Springs = 10, Vents = 11, Starship = 12, Volcano = 13, Caldera = 14, Spires = 15;
    public static readonly Kind[] Kinds =
    {
        K("geoplant", 190, Place.Flat, 1.5f), K("foundry", 170, Place.Flat, 1.5f), K("miningrig", 150, Place.Open, 1.5f),
        K("orehauler", 190, Place.Lava, 1f), K("bunker", 140, Place.Flat, 1.5f), K("quarry", 170, Place.Rocky, 1.2f),
        K("stilts", 150, Place.Lava, 1.2f), K("domes", 150, Place.Rocky, 1.2f), K("crawler", 120, Place.Lava, 1.5f),
        K("columns", 140, Place.Rocky, 1.5f), K("springs", 130, Place.Rocky, 1.5f), K("vents", 130, Place.Open, 1.5f),
        K("starship", 330, Place.Open, .5f),
        M("volcano", 350), M("caldera", 360), M("spires", 330),
    };
    static Kind K(string n, float px, Place on, float per) { return new Kind { name = n, px = px, on = on, perThree = per }; }
    static Kind M(string n, float px) { return new Kind { name = n, px = px, on = Place.Mountain, perThree = MountainPerThree / 3f, mountain = true }; }
    public static int Count => Kinds.Length;
    public static int IndexOf(string name) { for (int i = 0; i < Kinds.Length; i++) if (Kinds[i].name == name) return i; return -1; }
    public static string Strip(int kind, int variant) { return Kinds[kind].name + "_" + variant; }
    public static bool Lava(int kind) { return Kinds[kind].on == Place.Lava; }

    // ---- where -------------------------------------------------------------------------------------------------------------
    public static GroundClass On(Place p)
    {
        switch (p)
        {
            case Place.Flat: return GroundClass.Flat;
            case Place.Rocky: return GroundClass.Rocky;
            case Place.Lava: return GroundClass.Water;      // mask W
            case Place.Mountain: return GroundClass.Rocky | GroundClass.Plain;
            default: return GroundClass.Plain;
        }
    }
    public static float Need(Place p) { return p == Place.Flat ? .8f : p == Place.Rocky || p == Place.Mountain ? .55f : p == Place.Lava ? .4f : .85f; }
    public const float ContextShare = .45f;         // the context is read over a disc of this x the keep-out (~ the painting's base)
    public const GroundClass Painted = GroundClass.Canopy;   // mask C: a painted feature of the ground, never under a prop

    // ---- sizes (a 1080 px wide screen spans the view: 2 x HalfWidth) -------------------------------------------------------
    public const float ScreenPx = 1080f;
    // Kind.px is the size approved at the .41 altitude; at GroundZoom every prop is drawn larger by the same share as the ground
    // (DrawnPx; the atlas is cut at the drawn size, build_r1.py ALT), and the copies per screen thin by the area (DensityK)
    public const float ApprovedZoom = .41f;
    public static float AltitudeK => EmberTuning.GroundZoom / ApprovedZoom;
    public static float DensityK => 1f / (AltitudeK * AltitudeK);
    public static float DrawnPx(int kind) { return Kinds[kind].px * AltitudeK; }
    public static float Units(float px, float halfW) { return px / ScreenPx * 2f * halfW; }
    public static float CellSize(int kind, float halfW) { return Units(DrawnPx(kind), halfW); }
    public const float KeepCore = .42f, KeepMarginPx = 22f;
    // the keep-out half-size (a box in the planner; a disc in the plan): .42 x the drawn px + 22 px
    public static float Keep(int kind, float halfW) { return Units(DrawnPx(kind) * KeepCore + KeepMarginPx, halfW); }

    // ---- how many ----------------------------------------------------------------------------------------------------------
    public const float MountainPerThree = .65f;      // mountains (all three kinds together) per three screens, placed first
    public const int Pool = 26, MountainPool = 4;
    public const int Tries = 40;
    // (as approved at .41: per three screens of the drawn ground; the director thins it by DensityK)
    public static float TotalPerThree(bool mountains)
    {
        float t = 0f;
        foreach (var k in Kinds) if (k.mountain == mountains) t += k.perThree;
        return t;
    }

    // ---- look --------------------------------------------------------------------------------------------------------------
    public const int FrameMs = 125;
    public static readonly Color PropTint = new Color(.92f, .92f, .92f, 1f);   // a touch under painted: every shot's core stays the brightest thing

    // a copy's variant: a hash of (spawn count, kind), never the previous copy's of that kind
    public static int Variant(int spawn, int kind, int count, int previous)
    {
        if (count <= 1) return 0;
        uint h = (uint)(spawn * 73856093) ^ (uint)((kind + 7) * 19349663) ^ 0x85ebca6bu;
        h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12; h *= 0x297a2d39u; h ^= h >> 15;
        int v = (int)(h % (uint)count);
        return v == previous ? (v + 1) % count : v;
    }

    // ---- the starship's CRASH SCAR (a decal child of its body: scrolls with it, drawn just under it) ----------------------------
    // All offsets are in DRAWN CELL WIDTHS of the starship (Units(DrawnPx), x right, y up) from its centre, so they hold at any altitude:
    // scar_meta.json (cell px of the 256 px cell / 256). The starship has ONE drawing, so all four liveries share the geometry
    // (scar_<variant> differ only in the ground's tint). The furrow runs down and to the left of the wreck.
    public const string ScarAtlas = "scar";
    public static readonly string[] ScarCells = { "scar_0", "scar_1", "scar_2", "scar_3" };
    public static readonly Vector2 ScarCentre = new Vector2(-0.3047f, -0.6719f);      // the decal's centre
    public const float ScarWidth = 2.1875f;                                       // the decal's drawn width (ScarSpan 560 / 256)
    // how far the decal's visible art reaches below / above the starship's centre, in drawn widths (the deal ahead keeps its bottom out
    // of sight; the despawn waits for the wreck's own top, .5, or the decal's, whichever is higher)
    public const float ScarBelow = 1.15f, ScarAbove = 0.27f;
    // the furrow's keep-out boxes per scar cell (dx, dy, half-size; drawn widths from the centre), claimed beside the wreck's own footprint
    public static readonly Vector3[][] ScarBoxes =
    {
        new[] { new Vector3(-0.1164f, -0.1078f, 0.2305f),new Vector3(-0.3629f, -0.3129f, 0.2070f),new Vector3(-0.6098f, -0.5176f, 0.1797f),new Vector3(-0.8562f, -0.7227f, 0.1562f) },
        new[] { new Vector3(-0.1164f, -0.1078f, 0.2305f),new Vector3(-0.3629f, -0.3129f, 0.2070f),new Vector3(-0.6098f, -0.5176f, 0.1797f),new Vector3(-0.8562f, -0.7227f, 0.1562f) },
        new[] { new Vector3(-0.1164f, -0.1078f, 0.2305f),new Vector3(-0.3629f, -0.3129f, 0.2070f),new Vector3(-0.6098f, -0.5176f, 0.1797f),new Vector3(-0.8562f, -0.7227f, 0.1562f) },
        new[] { new Vector3(-0.1164f, -0.1078f, 0.2305f),new Vector3(-0.3629f, -0.3129f, 0.2070f),new Vector3(-0.6098f, -0.5176f, 0.1797f),new Vector3(-0.8562f, -0.7227f, 0.1562f) },
    };
    public const float ScarMargin = 0.06f;
    public static bool HasScar(int kind) { return kind == Starship; }
    // box j of the scar of variant v on a wreck whose cell is `size` wide at x, gy: centre and half-size (world units)
    public static void ScarBox(int variant, int j, float size, float x, double gy, out float cx, out double cy, out float h)
    {
        var b = ScarBoxes[Mathf.Clamp(variant, 0, ScarBoxes.Length - 1)][j];
        cx = x + b.x * size;
        cy = gy + b.y * size;
        h = (b.z + ScarMargin) * size;
    }

    // Is a prop of `kind` centred at (x, gy) on its context? (placement and the tests)
    public static bool OnContext(GroundPlanner planner, int kind, float x, double gy, float halfW)
    {
        if (planner == null || planner.Mask == null) return true;
        float r = Keep(kind, halfW) * ContextShare;
        var on = Kinds[kind].on;
        return planner.Match(x, gy, r, r, On(on)) >= Need(on) && planner.Match(x, gy, r, r, Painted) <= 0f;
    }
}

public partial class EmberDirector
{
    BackdropPool props, mountains;
    BackdropAtlas propArt;
    Sprite[][][] propStrips;                 // [kind][variant] -> its frames (null: not in this layout's atlas)
    int[] lastPropVariant;
    int lastPropKind = -1, landing;
    float propTravel, propGap, mountainTravel, mountainGap;
    System.Random propRng;                   // its own stream: a prop never shifts the eruptions' or the weather's dice
    SpriteRenderer[] propScar;               // per props slot: the crash-scar decal under its body (the starship's)
    int[][] propScarFeet;                    // per props slot: the scar's planner footprints (-1 none)
    int[] propVar;                           // per props slot: its variant (the scar's cell)
    Sprite[] scarArt;                        // ScarCells (null entries: not installed)

    public BackdropPool Props => props;
    public BackdropPool Mountains => mountains;
    public int PropsSpawned { get; private set; }
    public float ViewHalfWidth => HalfW;

    float PRand(float a, float b) { return a + (float)propRng.NextDouble() * (b - a); }

    void BuildProps()
    {
        // its own stream, a new one each landing (BackdropVariants.Picks): the ground is dealt afresh, deterministic under a test seed
        landing = BackdropVariants.For("Ember").Picks;
        propRng = new System.Random((SeedOverride > 0 ? SeedOverride : 1990 + landing * 7919) * 53 + 29);
        propArt = set.VariantAtlas(EmberPropsTuning.Atlas);
        int n = EmberPropsTuning.Count;
        propStrips = new Sprite[n][][];
        lastPropVariant = new int[n];
        for (int k = 0; k < n; k++)
        {
            lastPropVariant[k] = -1;
            var list = new System.Collections.Generic.List<Sprite[]>();
            for (int v = 0; v < 16; v++)
            {
                var f = propArt != null ? propArt.Frames(EmberPropsTuning.Strip(k, v)) : new Sprite[0];
                if (f.Length == 0) break;
                list.Add(f);
            }
            propStrips[k] = list.Count > 0 ? list.ToArray() : null;
        }
        // back to front on the ground plane: mountains (-6), props (-3), eruptions (-2), sites (0)
        mountains = Pool(EmberPropsTuning.Layer, EmberPropsTuning.MountainPool, false, -6);
        props = Pool(EmberPropsTuning.Layer, EmberPropsTuning.Pool, false, -3);
        Grade(mountains, EmberPropsTuning.Layer);
        Grade(props, EmberPropsTuning.Layer);
        BuildScars();
        float screens3 = 6f * HalfH;
        propGap = PRand(.3f, 1f) * screens3 / (EmberPropsTuning.TotalPerThree(false) * EmberPropsTuning.DensityK);
        mountainGap = PRand(.2f, .8f) * screens3 / (EmberPropsTuning.MountainPerThree * EmberPropsTuning.DensityK);
    }

    // the starship's scar decals: one renderer per props slot, under its body, off until a wreck is dealt into the slot
    void BuildScars()
    {
        int n = props.items.Count;
        propScar = new SpriteRenderer[n];
        propScarFeet = new int[n][];
        propVar = new int[n];
        var atlas = set.Atlas(EmberPropsTuning.ScarAtlas);
        scarArt = new Sprite[EmberPropsTuning.ScarCells.Length];
        if (atlas != null) for (int s = 0; s < scarArt.Length; s++) scarArt[s] = atlas.Get(EmberPropsTuning.ScarCells[s]);
        for (int i = 0; i < n; i++)
        {
            var p = props.items[i];
            var go = new GameObject("scar");
            go.transform.SetParent(p.body, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingLayerID = p.sr.sortingLayerID;
            sr.sortingOrder = p.sr.sortingOrder - 1;
            sr.sharedMaterial = p.sr.sharedMaterial;
            sr.enabled = false;
            propScar[i] = sr;
            propScarFeet[i] = new int[EmberPropsTuning.ScarBoxes[0].Length];
            for (int j = 0; j < propScarFeet[i].Length; j++) propScarFeet[i][j] = -1;
        }
    }

    public bool HasPropArt(int kind) { return propStrips != null && kind >= 0 && kind < propStrips.Length && propStrips[kind] != null; }
    public int PropVariants(int kind) { return HasPropArt(kind) ? propStrips[kind].Length : 0; }
    public float PropKeep(int kind) { return EmberPropsTuning.Keep(kind, HalfW); }
    float CellHalf(int kind) { return EmberPropsTuning.CellSize(kind, HalfW) * .5f; }
    public SpriteRenderer PropScar(int slot) { return propScar != null && slot >= 0 && slot < propScar.Length ? propScar[slot] : null; }

    // box j of the scar in props slot `slot` (false: no live scar there)
    public bool PropScarBox(int slot, int j, out float x, out double gy, out float h)
    {
        x = 0f; gy = 0.0; h = 0f;
        if (props == null || slot < 0 || slot >= props.items.Count || propScar[slot] == null || !propScar[slot].enabled) return false;
        if (j < 0 || j >= EmberPropsTuning.ScarBoxes[0].Length) return false;
        var p = props.items[slot];
        if (!p.active) return false;
        EmberPropsTuning.ScarBox(propVar[slot], j, p.size, p.x, p.gy, out x, out gy, out h);
        return true;
    }

    // the opening view: mountains and props already standing (over the whole view and the band above it)
    void OpeningProps()
    {
        if (props == null || !planner.Ready) return;
        double t = planner.Travel;
        int m = Mathf.RoundToInt(EmberPropsTuning.MountainPerThree * EmberPropsTuning.DensityK / 3f * 1.3f + (float)propRng.NextDouble());
        for (int i = 0; i < m; i++) SpawnProp(-1, t - HalfH * .7, t + HalfH * 1.2, true);
        int n = Mathf.RoundToInt(EmberPropsTuning.TotalPerThree(false) * EmberPropsTuning.DensityK / 3f * 1.3f);
        for (int i = 0; i < n; i++) SpawnProp(-1, t - HalfH * .9, t + HalfH * 1.3, false);
    }

    int PickProp(bool mountain, int not)
    {
        float total = 0f;
        var ks = EmberPropsTuning.Kinds;
        for (int i = 0; i < ks.Length; i++)
            if (ks[i].mountain == mountain && i != not && (i != lastPropKind || mountain) && HasPropArt(i)) total += ks[i].perThree;
        if (total <= 0f) return -1;
        float r = PRand(0f, total);
        for (int i = 0; i < ks.Length; i++)
        {
            if (ks[i].mountain != mountain || i == not || (i == lastPropKind && !mountain) || !HasPropArt(i)) continue;
            r -= ks[i].perThree;
            if (r <= 0f) return i;
        }
        return -1;
    }

    // A prop (kind < 0: one picked) centred in the ground band [lo, hi] on its context, its keep-out free (and its scar's).
    // `variant` < 0: the copy's own. False: nothing placed.
    public bool SpawnProp(int kind, double lo, double hi, bool mountain = false, int variant = -1, int tries = EmberPropsTuning.Tries, bool above = false)
    {
        if (props == null || !planner.Ready) return false;
        if (kind >= 0) mountain = EmberPropsTuning.Kinds[kind].mountain;
        var pool = mountain ? mountains : props;
        if (pool.ActiveCount >= pool.Capacity) return false;
        int tried = -1;
        for (int k = 0; k < 3; k++)
        {
            int kd = kind >= 0 ? kind : PickProp(mountain, tried);
            if (kd < 0 || !HasPropArt(kd)) return false;
            tried = kd;
            float keep = PropKeep(kd);
            float size = EmberPropsTuning.CellSize(kd, HalfW);
            bool scar = EmberPropsTuning.HasScar(kd);
            if (scar && variant < 0) variant = EmberPropsTuning.Variant(PropsSpawned + landing * 131, kd, PropVariants(kd), lastPropVariant[kd]);   // (the scar's cell follows the livery)
            // entering at the top: the whole painting (and a scar's lower reach) starts above the view, never popping in
            double off = above ? (scar ? EmberPropsTuning.ScarBelow * size + .1f : size * .55f) : 0.0;
            float x = 0f; double gy = 0.0; bool found = false;
            for (int t = 0; t < tries && !found; t++)
            {
                float cx = PRand(XMin(keep), XMax(keep));
                double cy = lo + off + propRng.NextDouble() * (hi - lo);
                if (above && cy - (scar ? EmberPropsTuning.ScarBelow * size : CellHalf(kd)) < planner.Travel + HalfH) continue;
                if (!planner.Free(cx, cy, keep, keep)) continue;
                if (!EmberPropsTuning.OnContext(planner, kd, cx, cy, HalfW)) continue;
                if (scar && !ScarClear(variant, size, cx, cy)) continue;
                x = cx; gy = cy; found = true;
            }
            if (!found) { if (kind >= 0) return false; continue; }
            return Commit(kd, x, gy, variant) != null;
        }
        return false;
    }

    bool ScarClear(int variant, float size, float x, double gy)
    {
        for (int j = 0; j < EmberPropsTuning.ScarBoxes[0].Length; j++)
        {
            float bx, h; double by;
            EmberPropsTuning.ScarBox(variant, j, size, x, gy, out bx, out by, out h);
            if (Mathf.Abs(bx) > EdgeX + h * .3f) return false;           // the furrow stays on the ground it can be seen on
            if (!planner.Free(bx, by, h, h)) return false;
        }
        return true;
    }

    BackdropPiece Commit(int kd, float x, double gy, int variant)
    {
        bool mountain = EmberPropsTuning.Kinds[kd].mountain;
        var pool = mountain ? mountains : props;
        float keep = PropKeep(kd);
        int foot = planner.Claim(x, gy, keep, keep);
        if (foot < 0) return null;
        var p = pool.Spawn();
        if (p == null) { planner.Release(foot); return null; }
        int n = propStrips[kd].Length;
        int v;
        if (variant >= 0) v = Mathf.Min(variant, n - 1);
        else v = EmberPropsTuning.Variant(PropsSpawned + landing * 131, kd, n, lastPropVariant[kd]);
        if (v == lastPropVariant[kd] && variant < 0 && n > 1) v = (v + 1) % n;
        lastPropVariant[kd] = v;
        p.frames = propStrips[kd][v];
        var s = p.frames[0];
        // the atlas cells are cut at their drawn px on a 1080 px screen: 100 sheet px a sprite unit
        float k = 2f * HalfW / EmberPropsTuning.ScreenPx * BackdropAtlas.PixelsPerUnit;
        SetSprite(p, s, s.bounds.size.x * k);
        p.size = EmberPropsTuning.CellSize(kd, HalfW);
        p.body.localScale = Vector3.one;                       // never mirrored, never turned
        p.body.localPosition = Vector3.zero;
        p.body.localRotation = Quaternion.identity;
        p.footprint = foot;
        p.core = EmberPropsTuning.KeepCore;
        p.gy = gy;
        p.x = x;
        p.y = planner.Y(gy);
        p.vx = p.vy = 0f;
        p.rate = set.Spec.Rate(EmberPropsTuning.Layer);
        p.color = EmberPropsTuning.PropTint;
        p.dockTint = Color.white;
        p.kind = kd;
        p.tier = v;
        p.age = 0f;
        p.phase = PRand(0f, p.frames.Length * EmberPropsTuning.FrameMs / 1000f);   // its own animation phase
        p.dyingT = -1f;
        Place(p);
        if (!mountain) AttachScar(pool.items.IndexOf(p), kd, v, p);
        PaintProp(p);
        if (!mountain) lastPropKind = kd;
        PropsSpawned++;
        return p;
    }

    public static int PropFrame(BackdropPiece p)
    {
        int n = p.frames != null ? p.frames.Length : 1;
        return n <= 1 ? 0 : Mathf.FloorToInt((p.age + p.phase) * 1000f / EmberPropsTuning.FrameMs) % n;
    }

    void PaintProp(BackdropPiece p)
    {
        if (p.frames != null)
        {
            var want = p.frames[PropFrame(p)];
            if (p.sr.sprite != want) p.sr.sprite = want;
        }
        Color c = p.color;
        c.a = set.Alpha;
        p.sr.color = c;
    }

    // ---- the scar -------------------------------------------------------------------------------------------------------------

    void AttachScar(int slot, int kind, int variant, BackdropPiece p)
    {
        if (slot < 0 || propScar == null || slot >= propScar.Length) return;
        var sr = propScar[slot];
        ReleaseScar(slot);
        propVar[slot] = variant;
        var art = EmberPropsTuning.HasScar(kind) && variant >= 0 && variant < scarArt.Length ? scarArt[variant] : null;
        if (art == null) { sr.enabled = false; return; }
        sr.sprite = art;
        // the decal is ScarWidth drawn cell widths wide, centred ScarCentre from the body's centre (the body's local unit is the root's scale)
        float rootScale = Mathf.Max(.0001f, p.root.localScale.x);
        float wLocal = EmberPropsTuning.ScarWidth * p.size / rootScale;
        float k = wLocal / Mathf.Max(.0001f, art.bounds.size.x);
        sr.transform.localScale = new Vector3(k, k, 1f);
        sr.transform.localPosition = new Vector3(EmberPropsTuning.ScarCentre.x * p.size / rootScale, EmberPropsTuning.ScarCentre.y * p.size / rootScale, 0f);
        sr.enabled = true;
        for (int j = 0; j < propScarFeet[slot].Length; j++)
        {
            float bx, h; double by;
            EmberPropsTuning.ScarBox(variant, j, p.size, p.x, p.gy, out bx, out by, out h);
            propScarFeet[slot][j] = planner.Claim(bx, by, h, h);
        }
    }

    void ReleaseScar(int slot)
    {
        if (propScarFeet == null || slot < 0 || slot >= propScarFeet.Length || propScarFeet[slot] == null) return;
        for (int j = 0; j < propScarFeet[slot].Length; j++)
        {
            if (propScarFeet[slot][j] >= 0) planner.Release(propScarFeet[slot][j]);
            propScarFeet[slot][j] = -1;
        }
    }

    // ---- stepping ---------------------------------------------------------------------------------------------------------------

    void StepPropPool(BackdropPool pool, float dt)
    {
        foreach (var p in pool.items)
        {
            if (!p.active) continue;
            p.age += dt;
            p.y = planner.Y(p.gy);
            Place(p);
            float above = p.size * (pool == props && EmberPropsTuning.HasScar(p.kind) ? Mathf.Max(.7f, EmberPropsTuning.ScarAbove) : .7f);
            if (p.y < -HalfH - above - .3f)
            {
                planner.Release(p.footprint);
                p.footprint = -1;
                if (pool == props)
                {
                    int slot = props.items.IndexOf(p);
                    ReleaseScar(slot);
                    if (slot >= 0 && propScar[slot].enabled) propScar[slot].enabled = false;
                }
                Despawn(p);
                continue;
            }
            PaintProp(p);
            if (pool == props)
            {
                int slot = props.items.IndexOf(p);
                if (slot >= 0 && propScar[slot].enabled) propScar[slot].color = p.sr.color;
            }
        }
    }

    void StepProps(float dt, float scrolled)
    {
        if (props == null) return;
        propTravel += scrolled;
        mountainTravel += scrolled;
        float screens3 = 6f * HalfH;
        if (planner.Ready)
        {
            double lo = planner.Travel + HalfH, hi = lo + EmberTuning.SpawnBand;
            // the mountains first: their ground is taken before the smaller props look for theirs
            if (mountainTravel >= mountainGap)
            {
                if (SpawnProp(-1, lo, hi, true, -1, EmberPropsTuning.Tries, true))
                { mountainTravel = 0f; mountainGap = PRand(.6f, 1.4f) * screens3 / (EmberPropsTuning.MountainPerThree * EmberPropsTuning.DensityK); }
                else mountainGap = mountainTravel + EmberTuning.SpawnRetry;
            }
            if (propTravel >= propGap)
            {
                if (SpawnProp(-1, lo, hi, false, -1, EmberPropsTuning.Tries, true))
                { propTravel = 0f; propGap = PRand(.6f, 1.4f) * screens3 / (EmberPropsTuning.TotalPerThree(false) * EmberPropsTuning.DensityK); }
                else propGap = propTravel + EmberTuning.SpawnRetry;
            }
        }
        StepPropPool(mountains, dt);
        StepPropPool(props, dt);
    }

    // ---- keep-out queries ---------------------------------------------------------------------------------------------------

    // Is there a live prop (or mountain, or a wreck's scar) whose keep-out box meets the box (x, gy, rx, ry)?
    public bool TouchesProp(float x, double gy, float rx, float ry)
    {
        if (TouchesIn(mountains, x, gy, rx, ry) || TouchesIn(props, x, gy, rx, ry)) return true;
        if (propScar == null) return false;
        for (int i = 0; i < propScar.Length; i++)
            for (int j = 0; j < EmberPropsTuning.ScarBoxes[0].Length; j++)
            {
                float bx, h; double by;
                if (!PropScarBox(i, j, out bx, out by, out h)) break;
                if (Mathf.Abs(bx - x) < h + rx && System.Math.Abs(by - gy) < h + ry) return true;
            }
        return false;
    }

    bool TouchesIn(BackdropPool pool, float x, double gy, float rx, float ry)
    {
        if (pool == null) return false;
        foreach (var p in pool.items)
        {
            if (!p.active) continue;
            float k = PropKeep(p.kind);
            if (Mathf.Abs(p.x - x) < k + rx && System.Math.Abs(p.gy - gy) < k + ry) return true;
        }
        return false;
    }

    // Tests / previews: a prop of `kind` around screen y (searched a little up and down), its rules kept.
    public bool SpawnPropAt(int kind, float y, int variant = -1)
    {
        double g = planner.GroundY(y);
        float band = Mathf.Max(1.2f, PropKeep(kind) * 3f);
        return SpawnProp(kind, g - band, g + band, false, variant, 600);
    }

    // Tests: a prop of `kind` put exactly at (x, gy) -- no context rule, its keep-out still claimed. Null: no room.
    public BackdropPiece PlantProp(int kind, float x, double gy, int variant = 0)
    {
        if (!HasPropArt(kind) || !planner.Free(x, gy, PropKeep(kind), PropKeep(kind))) return null;
        return Commit(kind, x, gy, variant);
    }

    // Tests: stop dealing props (a gap that never ends)
    public void HoldProps(float gap) { propGap = mountainGap = gap; propTravel = mountainTravel = 0f; }

    // Tests / previews: every prop gone, its ground released.
    public void ClearProps()
    {
        foreach (var pool in new[] { props, mountains })
        {
            if (pool == null) continue;
            foreach (var p in pool.items)
            {
                if (!p.active) continue;
                planner.Release(p.footprint);
                p.footprint = -1;
                if (pool == props)
                {
                    int slot = props.items.IndexOf(p);
                    ReleaseScar(slot);
                    if (slot >= 0 && propScar != null) propScar[slot].enabled = false;
                }
                Despawn(p);
            }
        }
    }
}
