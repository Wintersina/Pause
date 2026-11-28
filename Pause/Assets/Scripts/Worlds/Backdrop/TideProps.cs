using UnityEngine;

// Tide's LIVING PROPS (r1, user-approved 2026-11-28): small animated paintings standing on the sea -- a lighthouse, a platform, a
// stilt village, a freighter moored to its buoys, a wreck, an atoll, a buoy line, a floating farm, a dock with its boats -- and the
// whales and manta rays that live in the water. The atlas (Backdrop3/props: <kind>_<variant>_<frame>, 4 variants a kind, 8 frames,
// the whales and mantas 16; centre pivot; props.json carries each kind's frame time) is cut at the drawn px of the .49 altitude on a
// 1080 px screen (Art/Worlds/Tide/backdrop_r1~/build_atlas.py). Modelled on DesertProps.cs.
//
//   PINNED     every prop rides the mid tile (the "ground" layer, its rate; GroundPlanner), never mirrored, never bobbing: nothing flies
//              free -- the freighter is moored, the boats docked, the wreck wrecked -- and the marine life only surfaces in place.
//   CONTEXT    Tide's ground is mostly open WATER (GroundClass.Water, a few Open shoals, painted decks / city blocks = Built, painted
//              features = Canopy). Deep-water kinds (the freighter, the buoys) stand on water; the shoal-loving kinds (lighthouse,
//              platform, village, farm, dock, wreck, atoll) on water or shoal, preferring the shoals where there are any; the whales
//              and mantas ONLY in water (every sample of their footprint). NONE of a prop's footprint is ever a painted deck, block or
//              feature (Built / Canopy). The farm and the dock like to stand by the village (Kind.near, a preference, not a rule).
//   KEEP-OUT   every prop claims a box of half-size Keep (= .42 x its drawn px + KeepMarginPx, on a 1080 px screen) in the planner, so
//              no piece, cluster, pipe run or launch site of TideDirector is placed in it; no ocean crest is put there (and a crest
//              claims its own footprint, so no prop is dealt onto a live one); no smoke pall is raised whose straight drift would meet
//              one, and none is rising when a prop is dealt (PallPathClear); no piece's smoke / steam column (a box above the
//              piece, PlumeBoxTouchesProp) rises through one. A prop is NEVER razed.
//   VARIANTS   each copy's variant is a hash of its spawn count and kind, never the previous copy's of that kind; each copy starts at
//              its own animation phase, at its kind's frame time.
//   LIGHT      the lamps are baked into the frames; the props are drawn at PropTint (under the shots' full-strength cores), and darker
//              and cooler on the night side (NightTint), so they are never brighter than the ground.
// Animated from each piece's own age (scaled dt): a paused game is a still frame. Nothing is allocated per frame.
public static class TidePropsTuning
{
    public const string Atlas = "props";             // Backdrop3/props.png + .json (shared by the four ground sets)
    public const string Layer = "ground";
    public enum Place { Shoal, Deep, Marine }
    public struct Kind
    {
        public string name;
        public float px;          // drawn cell width on a 1080 px screen (at the .49 altitude)
        public Place on;
        public float perThree;    // copies per three screens of ground
        public int ms;            // frame time
        public int[] near;        // likes to stand within NearReach of one of these kinds (null: anywhere)
        public int frames;        // frames a variant's strip has (the atlas is the truth: the stack and cliffs are a single painting)
        public bool big;          // a landmark (stack, cliffs, starship): dealt first from its own pool, hard-pinned like the rest
    }
    public const int Lighthouse = 0, Platform = 1, Village = 2, Freighter = 3, Wreck = 4, Atoll = 5, Buoys = 6, Farm = 7, Dock = 8, Whales = 9, Mantas = 10,
                     Turbines = 11, Stack = 12, Cliffs = 13, Starship = 14;
    public static readonly Kind[] Kinds =
    {
        K("lighthouse", 160, Place.Shoal, 1.6f), K("platform", 170, Place.Shoal, 1.6f), K("village", 200, Place.Shoal, 1.2f),
        K("freighter", 260, Place.Deep, 1.2f), K("wreck", 180, Place.Shoal, 1.5f), K("atoll", 270, Place.Shoal, 1f),
        K("buoys", 180, Place.Deep, 1.5f), K("farm", 210, Place.Shoal, 1.2f, 125, 8, Village), K("dock", 220, Place.Shoal, 1.2f, 125, 8, Village, Farm),
        K("whales", 210, Place.Marine, 1.2f, 140, 16), K("mantas", 200, Place.Marine, 1.2f, 110, 16),
        K("turbines", 200, Place.Shoal, 1.3f), B("stack", 340, .7f, 1), B("cliffs", 380, .6f, 1), B("starship", 400, .5f, 8),
    };
    static Kind K(string n, float px, Place on, float per, int ms = 125, int frames = 8, params int[] near)
    {
        return new Kind { name = n, px = px, on = on, perThree = per, ms = ms, frames = frames, near = near != null && near.Length > 0 ? near : null };
    }
    static Kind B(string n, float px, float per, int frames) { return new Kind { name = n, px = px, on = Place.Shoal, perThree = per, ms = 125, frames = frames, big = true }; }
    public static int Count => Kinds.Length;
    public static int IndexOf(string name) { for (int i = 0; i < Kinds.Length; i++) if (Kinds[i].name == name) return i; return -1; }
    public static string Strip(int kind, int variant) { return Kinds[kind].name + "_" + variant; }
    public static bool Big(int kind) { return Kinds[kind].big; }
    public static bool Marine(int kind) { return Kinds[kind].on == Place.Marine; }

    // ---- where -------------------------------------------------------------------------------------------------------------
    public const GroundClass Painted = GroundClass.Built | GroundClass.Canopy;     // a painted deck / block / feature: never under a prop
    public static GroundClass On(Place p) { return p == Place.Shoal ? GroundClass.Water | GroundClass.Open : GroundClass.Water; }
    // the share of the context disc that must be `On`: the whole of it (a prop never touches a painted deck, feature or, for marine
    // life, anything but water); the deep-water kinds may have a shoal cell at the rim
    public static float Need(Place p) { return p == Place.Deep ? .9f : 1f; }
    public const float ContextShare = .45f;         // the context is read over a disc of this x the keep-out (~ the painting's base)
    public const float OpenShare = .25f;            // a shoal kind's first tries want this much open shoal (where the layout has any)

    // ---- sizes (a 1080 px wide screen spans the view: 2 x HalfWidth) -------------------------------------------------------
    public const float ScreenPx = 1080f;
    public static float Units(float px, float halfW) { return px / ScreenPx * 2f * halfW; }
    public static float CellSize(int kind, float halfW) { return Units(Kinds[kind].px, halfW); }
    public const float KeepCore = .42f, KeepMarginPx = 22f;
    // the keep-out half-size (a box in the planner): .42 x the drawn px + 22 px
    public static float Keep(int kind, float halfW) { return Units(Kinds[kind].px * KeepCore + KeepMarginPx, halfW); }
    public const float NearMin = 1f, NearMax = 2.6f, NearShare = .6f;  // a NEAR kind's centre: this x (its host's keep-out + .6 x its own) away, this often

    // ---- how many ----------------------------------------------------------------------------------------------------------
    public const int Pool = 20, BigPool = 4;
    public const int Tries = 40;
    public static float TotalPerThree(bool big) { float t = 0f; foreach (var k in Kinds) if (k.big == big) t += k.perThree; return t; }

    // ---- look --------------------------------------------------------------------------------------------------------------
    public static readonly Color PropTint = new Color(.92f, .92f, .92f, 1f);   // a touch under painted: every shot's core stays the brightest thing
    public static readonly Color NightTint = new Color(.45f, .5f, .58f, 1f);   // the night side (TideTuning.Night): darker and cooler than the ground
    public static Color TintFor(bool night)
    {
        return night ? new Color(PropTint.r * NightTint.r, PropTint.g * NightTint.g, PropTint.b * NightTint.b, 1f) : PropTint;
    }

    // a copy's variant: a hash of (spawn count, kind), never the previous copy's of that kind
    public static int Variant(int spawn, int kind, int count, int previous)
    {
        if (count <= 1) return 0;
        uint h = (uint)(spawn * 73856093) ^ (uint)((kind + 7) * 19349663) ^ 0x85ebca6bu;
        h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12; h *= 0x297a2d39u; h ^= h >> 15;
        int v = (int)(h % (uint)count);
        return v == previous ? (v + 1) % count : v;
    }

    // Is a prop of `kind` centred at (x, gy) on its context, clear of painted features? (placement and the tests)
    public static bool OnContext(GroundPlanner planner, int kind, float x, double gy, float halfW)
    {
        if (planner == null || planner.Mask == null) return true;
        float r = Keep(kind, halfW) * ContextShare;
        var on = Kinds[kind].on;
        return planner.Match(x, gy, r, r, On(on)) >= Need(on) && planner.Match(x, gy, r, r, Painted) <= 0f;
    }
}

public partial class TideDirector
{
    BackdropPool props, bigs;
    BackdropAtlas propArt;
    Sprite[][][] propStrips;                 // [kind][variant] -> its frames (null: not in the atlas)
    int[] lastPropVariant;
    int lastPropKind = -1, landing;
    float propTravel, propGap, bigTravel, bigGap;
    Color propColor;
    System.Random propRng;                   // its own stream: a prop never shifts the pieces' or the weather's dice

    public BackdropPool Props => props;
    public BackdropPool Bigs => bigs;
    public int PropsSpawned { get; private set; }
    public float ViewHalfWidth => HalfW;

    float PRand(float a, float b) { return a + (float)propRng.NextDouble() * (b - a); }

    void BuildProps()
    {
        // its own stream, a new one each landing (BackdropVariants.Picks): deterministic under a test seed
        landing = BackdropVariants.For("Tide").Picks;
        propRng = new System.Random((SeedOverride > 0 ? SeedOverride : 1990 + landing * 7919) * 53 + 29);
        propArt = set.Atlas(TidePropsTuning.Atlas);
        int n = TidePropsTuning.Count;
        propStrips = new Sprite[n][][];
        lastPropVariant = new int[n];
        var list = new System.Collections.Generic.List<Sprite[]>();
        for (int k = 0; k < n; k++)
        {
            lastPropVariant[k] = -1;
            list.Clear();
            for (int v = 0; v < 16; v++)
            {
                var f = propArt != null ? propArt.Frames(TidePropsTuning.Strip(k, v)) : new Sprite[0];
                if (f.Length == 0) break;
                list.Add(f);
            }
            propStrips[k] = list.Count > 0 ? list.ToArray() : null;
        }
        propColor = TidePropsTuning.TintFor(night);
        // back to front on the ground plane: crests (-8), pipes (-4), props (-3), fires (-2), pieces and sites (0)
        bigs = Pool(TidePropsTuning.Layer, TidePropsTuning.BigPool, false, -6);
        props = Pool(TidePropsTuning.Layer, TidePropsTuning.Pool, false, -3);
        Grade(bigs, TidePropsTuning.Layer);
        Grade(props, TidePropsTuning.Layer);
        propGap = PRand(.3f, 1f) * 6f * HalfH / TidePropsTuning.TotalPerThree(false);
        bigGap = PRand(.2f, .8f) * 6f * HalfH / TidePropsTuning.TotalPerThree(true);
    }

    public bool HasPropArt(int kind) { return propStrips != null && kind >= 0 && kind < propStrips.Length && propStrips[kind] != null; }
    public int PropVariants(int kind) { return HasPropArt(kind) ? propStrips[kind].Length : 0; }
    public float PropKeep(int kind) { return TidePropsTuning.Keep(kind, HalfW); }
    float CellHalf(int kind) { return TidePropsTuning.CellSize(kind, HalfW) * .5f; }

    // the opening view: props already standing (over the whole view and the band above it)
    void OpeningProps()
    {
        if (props == null || !planner.Ready) return;
        double t = planner.Travel;
        int m = Mathf.RoundToInt(TidePropsTuning.TotalPerThree(true) / 3f * 1.3f + (float)propRng.NextDouble());
        for (int i = 0; i < m; i++) SpawnProp(-1, t - HalfH * .7, t + HalfH * 1.2, -1, TidePropsTuning.Tries, false, true);   // the big ones first
        int n = Mathf.RoundToInt(TidePropsTuning.TotalPerThree(false) / 3f * 1.3f);
        for (int i = 0; i < n; i++) SpawnProp(-1, t - HalfH * .9, t + HalfH * 1.3);
    }

    bool NearReady(int kind)
    {
        var near = TidePropsTuning.Kinds[kind].near;
        if (near == null) return true;
        foreach (var p in props.items)
            if (p.active && System.Array.IndexOf(near, p.kind) >= 0) return true;
        return false;
    }

    int PickProp(int not, bool big)
    {
        float total = 0f;
        var ks = TidePropsTuning.Kinds;
        for (int i = 0; i < ks.Length; i++)
            if (ks[i].big == big && i != not && (big || i != lastPropKind) && HasPropArt(i)) total += ks[i].perThree;
        if (total <= 0f) return -1;
        float r = PRand(0f, total);
        for (int i = 0; i < ks.Length; i++)
        {
            if (ks[i].big != big || i == not || (!big && i == lastPropKind) || !HasPropArt(i)) continue;
            r -= ks[i].perThree;
            if (r <= 0f) return i;
        }
        return -1;
    }

    // a candidate centre: anywhere in the band, or (a NEAR kind that likes company) round one of its hosts
    bool PropCandidate(int kind, double lo, double hi, out float x, out double gy)
    {
        var near = TidePropsTuning.Kinds[kind].near;
        float keep = PropKeep(kind);
        if (near != null && PRand(0f, 1f) < TidePropsTuning.NearShare)
        {
            int hosts = 0;
            foreach (var p in props.items) if (p.active && System.Array.IndexOf(near, p.kind) >= 0) hosts++;
            if (hosts > 0)
            {
                int pick = propRng.Next(hosts);
                foreach (var p in props.items)
                {
                    if (!p.active || System.Array.IndexOf(near, p.kind) < 0 || pick-- > 0) continue;
                    float a = PRand(0f, 2f * Mathf.PI);
                    float d = PRand(TidePropsTuning.NearMin, TidePropsTuning.NearMax) * (PropKeep(p.kind) + keep * .6f);
                    x = p.x + Mathf.Cos(a) * d;
                    gy = p.gy + Mathf.Sin(a) * d;
                    return x >= XMin(keep) && x <= XMax(keep) && gy >= lo - (hi - lo) && gy <= hi + (hi - lo) + keep * 2f;
                }
            }
        }
        x = PRand(XMin(keep), XMax(keep));
        gy = lo + propRng.NextDouble() * (hi - lo);
        return true;
    }

    // A prop (kind < 0: one picked) centred in the ground band [lo, hi] on its context, its keep-out free of every piece, crest, pall
    // and smoke column. `variant` < 0: the copy's own. `above`: the whole painting starts over the top of the view. False: nothing placed.
    public bool SpawnProp(int kind, double lo, double hi, int variant = -1, int tries = TidePropsTuning.Tries, bool above = false, bool big = false)
    {
        if (props == null || !planner.Ready) return false;
        if (kind >= 0) big = TidePropsTuning.Kinds[kind].big;
        var pool = big ? bigs : props;
        if (pool.ActiveCount >= pool.Capacity) return false;
        int tried = -1;
        bool shoals = planner.Mask != null && planner.Mask.Share(GroundClass.Open) > 0f;
        for (int k = 0; k < 3; k++)
        {
            int kd = kind >= 0 ? kind : PickProp(tried, big);
            if (kd < 0 || !HasPropArt(kd)) return false;
            tried = kd;
            float keep = PropKeep(kd);
            double off = above ? TidePropsTuning.CellSize(kd, HalfW) * .55f : 0.0;
            bool wantOpen = shoals && !big && TidePropsTuning.Kinds[kd].on == TidePropsTuning.Place.Shoal;
            float x = 0f; double gy = 0.0; bool found = false;
            for (int t = 0; t < tries && !found; t++)
            {
                float cx; double cy;
                if (!PropCandidate(kd, lo + off, hi + off, out cx, out cy)) continue;
                if (above && cy - CellHalf(kd) < planner.Travel + HalfH) continue;
                if (!planner.Free(cx, cy, keep, keep)) continue;
                if (!TidePropsTuning.OnContext(planner, kd, cx, cy, HalfW)) continue;
                if (wantOpen && t < tries / 2)       // the shoals first, the open water when there is no shoal to be had
                {
                    float r = keep * TidePropsTuning.ContextShare;
                    if (planner.Match(cx, cy, r, r, GroundClass.Open) < TidePropsTuning.OpenShare) continue;
                }
                if (PieceColumnsTouch(cx, cy, keep) || !PallPathClear(cx, cy, keep)) continue;
                x = cx; gy = cy; found = true;
            }
            if (!found) { if (kind >= 0) return false; continue; }
            return Commit(kd, x, gy, variant) != null;
        }
        return false;
    }

    BackdropPiece Commit(int kd, float x, double gy, int variant)
    {
        float keep = PropKeep(kd);
        int foot = planner.Claim(x, gy, keep, keep);
        if (foot < 0) return null;
        var p = (TidePropsTuning.Big(kd) ? bigs : props).Spawn();
        if (p == null) { planner.Release(foot); return null; }
        int n = propStrips[kd].Length;
        int v;
        if (variant >= 0) v = Mathf.Min(variant, n - 1);
        else v = TidePropsTuning.Variant(PropsSpawned + landing * 131, kd, n, lastPropVariant[kd]);
        if (v == lastPropVariant[kd] && variant < 0 && n > 1) v = (v + 1) % n;
        lastPropVariant[kd] = v;
        p.frames = propStrips[kd][v];
        var s = p.frames[0];
        // the atlas cells are cut at their drawn px on a 1080 px screen: 100 sheet px a sprite unit
        float k = 2f * HalfW / TidePropsTuning.ScreenPx * BackdropAtlas.PixelsPerUnit;
        SetSprite(p, s, s.bounds.size.x * k);
        p.size = TidePropsTuning.CellSize(kd, HalfW);
        p.body.localScale = Vector3.one;                       // never mirrored, never turned
        p.body.localPosition = Vector3.zero;
        p.body.localRotation = Quaternion.identity;
        p.footprint = foot;
        p.core = TidePropsTuning.KeepCore;
        p.gy = gy;
        p.x = x;
        p.y = planner.Y(gy);
        p.vx = p.vy = 0f;
        p.rate = set.Spec.Rate(TidePropsTuning.Layer);
        p.color = propColor;
        p.dockTint = Color.white;
        p.kind = kd;
        p.tier = v;
        p.age = 0f;
        p.phase = PRand(0f, p.frames.Length * TidePropsTuning.Kinds[kd].ms / 1000f);   // its own animation phase
        p.dyingT = -1f;
        Place(p);
        PaintProp(p);
        if (!TidePropsTuning.Big(kd)) lastPropKind = kd;
        PropsSpawned++;
        return p;
    }

    public static int PropFrame(BackdropPiece p)
    {
        int n = p.frames != null ? p.frames.Length : 1;
        return n <= 1 ? 0 : Mathf.FloorToInt((p.age + p.phase) * 1000f / TidePropsTuning.Kinds[p.kind].ms) % n;
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

    void StepProps(float dt, float scrolled)
    {
        if (props == null) return;
        propTravel += scrolled;
        bigTravel += scrolled;
        if (planner.Ready)
        {
            double lo = planner.Travel + HalfH, hi = lo + TideTuning.SpawnBand;
            // the landmarks first: their ground is taken before the smaller props look for theirs
            if (bigTravel >= bigGap)
            {
                if (SpawnProp(-1, lo, hi, -1, TidePropsTuning.Tries, true, true))
                { bigTravel = 0f; bigGap = PRand(.6f, 1.4f) * 6f * HalfH / TidePropsTuning.TotalPerThree(true); }
                else bigGap = bigTravel + TideTuning.SpawnRetry;
            }
            if (propTravel >= propGap)
            {
                if (SpawnProp(-1, lo, hi, -1, TidePropsTuning.Tries, true))
                { propTravel = 0f; propGap = PRand(.6f, 1.4f) * 6f * HalfH / TidePropsTuning.TotalPerThree(false); }
                else propGap = propTravel + TideTuning.SpawnRetry;
            }
        }
        StepPool(bigs, dt);
        StepPool(props, dt);
    }

    void StepPool(BackdropPool pool, float dt)
    {
        foreach (var p in pool.items)
        {
            if (!p.active) continue;
            p.age += dt;
            p.y = planner.Y(p.gy);
            Place(p);
            if (p.y < -HalfH - p.size * .7f - .3f)
            {
                planner.Release(p.footprint);
                p.footprint = -1;
                Despawn(p);
                continue;
            }
            PaintProp(p);
        }
    }

    // ---- keep-out queries ---------------------------------------------------------------------------------------------------

    // Is there a live prop whose keep-out box meets the box (x, gy, rx, ry)?
    public bool TouchesProp(float x, double gy, float rx, float ry)
    {
        return TouchesIn(bigs, x, gy, rx, ry) || TouchesIn(props, x, gy, rx, ry);
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

    // The smoke / steam column a piece of drawn width `size` at (x, gy) may raise: a box above it (PlumeReach x its width). Does one
    // meet a prop's keep-out? (A piece is not laid where its column would rise through a prop.)
    public bool PlumeBoxTouchesProp(float x, double gy, float size)
    {
        return TouchesProp(x, gy + size * .65f, size * .5f, size * .65f);
    }

    // Does any live piece's column (pipes, fires, ground pieces, launch sites) meet the keep-out box of a prop at (x, gy)?
    bool PieceColumnsTouch(float x, double gy, float keep)
    {
        return ColumnsIn(pipes, x, gy, keep) || ColumnsIn(fires, x, gy, keep) || ColumnsIn(ground, x, gy, keep) || ColumnsIn(sites, x, gy, keep);
    }

    bool ColumnsIn(BackdropPool pool, float x, double gy, float keep)
    {
        if (pool == null) return false;
        foreach (var q in pool.items)
        {
            if (!q.active) continue;
            float s = Mathf.Min(q.size, BackdropCatalog.MaxLandmarkSize);
            if (Mathf.Abs(q.x - x) < keep + s * .5f && System.Math.Abs(q.gy + s * .65f - gy) < keep + s * .65f) return true;
        }
        return false;
    }

    // A prop with keep-out half-size `keep` at (x, gy) stays off every live smoke pall's REMAINING straight drift.
    public bool PallPathClear(float x, double gy, float keep)
    {
        if (palls == null) return true;
        foreach (var p in palls.items)
        {
            if (!p.active) continue;
            float h = p.size * TideTuning.PallKeep;
            float left = Mathf.Max(0f, p.life - p.age);
            for (float a = 0f; a <= left; a += 1f)
            {
                float px = p.x + p.vx * a;
                double pgy = p.gy + p.vy * (p.age + a);
                if (Mathf.Abs(px - x) < keep + h && System.Math.Abs(pgy - gy) < keep + h) return false;
            }
        }
        return true;
    }

    // Tests / previews: a prop of `kind` around screen y (searched a little up and down), its rules kept.
    public bool SpawnPropAt(int kind, float y, int variant = -1)
    {
        double g = planner.GroundY(y);
        float band = Mathf.Max(1.2f, PropKeep(kind) * 3f);
        return SpawnProp(kind, g - band, g + band, variant, 600);
    }

    // Tests: a prop of `kind` put exactly at (x, gy) -- no context rule, its keep-out still claimed. Null: no room.
    public BackdropPiece PlantProp(int kind, float x, double gy, int variant = 0)
    {
        if (!HasPropArt(kind) || !planner.Free(x, gy, PropKeep(kind), PropKeep(kind))) return null;
        return Commit(kind, x, gy, variant);
    }

    // Tests / previews: every prop gone, its ground released.
    public void ClearProps()
    {
        foreach (var pool in new[] { props, bigs })
        {
            if (pool == null) continue;
            foreach (var p in pool.items)
            {
                if (!p.active) continue;
                planner.Release(p.footprint);
                p.footprint = -1;
                Despawn(p);
            }
        }
    }

    // Tests / previews: hold off the props' own spawner for this much scrolled ground.
    public void HoldProps(float ground)
    {
        propTravel = 0f; propGap = ground;
        bigTravel = 0f; bigGap = ground;
    }
}
