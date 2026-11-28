using UnityEngine;

// Desert's LIVING PROPS (r1, user-approved 2026-11-28): small animated paintings standing on the stitched .41-altitude ground --
// an oasis town, camel caravans, crawler trains, oil rigs, solar farms, a research station, wind farms, ruins with braziers, a
// crashed freighter, nomad camps, a TETHERED balloon field, a zeppelin MOORED to its mast, an excavation dig, sand skiffs on a
// shore, a spaceport, an observatory, a market with its roads -- and the BIG MOUNTAINS (mesa, ridge, hoodoo, volcano), pinned hard.
// They replace the round-1 landmarks, pipes and ground features (retired: dark-base art that shrank to specks at this altitude).
//
//   PINNED     every prop rides the mid tile (the "ground" layer, its rate; GroundPlanner), never mirrored (the paintings are lit,
//              the zeppelin's port / starboard lamps are red / green), never bobbing: nothing flies free -- the zeppelin is moored
//              and the balloons tethered in the paintings themselves, so they are ground props like the rest.
//   CONTEXT    each kind stands where it makes sense (Kind.on, the layout's ctx.txt flags, GroundMask.WithContext): camps, caravans,
//              solar farms, the station, the spaceport, the market, the zeppelin and the balloons on FLAT sand; the dig, the ruins
//              and the observatory on ROCKY ground; crawlers, rigs, wind farms and the wreck on OPEN dunes; the skiffs on a SHORE
//              (v2 / v3 only: their atlas has no skiffs elsewhere); the mountains on rocky or open ground. `need` of the footprint
//              must be that context and NONE of it a painted feature (mask C). NEAR: a caravan only within reach of a camp, an oasis
//              or a market; balloons beside a camp or the station; the market beside an oasis (Kind.near).
//   KEEP-OUT   every prop claims a box of half-size Keep (= .42 x its drawn px + KeepMarginPx, on a 1080 px screen) in the planner,
//              so no site / dock, wanderer (dust devil, grit gust, mirage glint) or other prop is placed in it (their keep-outs may
//              touch: the observatory and the dig may stand right beside a mountain); a worm's whole planned path keeps out of it
//              (DesertDirector.PathClear), no prop is placed on a live worm's remaining path (WormPathClear), and a worm that still
//              meets one (it cannot: belt and braces) dives and fades -- a prop is NEVER razed.
//   DATA       a kind is one row of Kinds plus its strips in the layout atlases (v<n>/props: <name>_<variant>_<frame>, cut to the
//              kind's box with the cell's centre as the pivot, ../Art/Worlds/Desert/backdrop_r1~/build_r1.py); the count of variants
//              and frames comes from the atlas. A layout atlas without a kind simply never deals it.
//   VARIANTS   each copy's variant is a hash of its spawn count and kind, never the previous copy's of that kind; the balloon field
//              leads each landing with a different livery (BackdropVariants.Picks). Each copy starts at its own animation phase.
//   LIGHT      the lamps are baked into the 8 frames (beacons blink at 1 Hz, strobes once a second, fires and windows flicker,
//              nothing flashes the whole field); the props are drawn at PropTint (under the shots' full-strength cores) and v4's
//              atlas carries the night grade (lamps keep their colour, the mountains' aprons feathered so no pale halo shows).
// Animated from each piece's own age (scaled dt): a paused game is a still frame. Nothing is allocated per frame.
public static class DesertPropsTuning
{
    public const string Atlas = "props";             // in each layout folder (v<n>/props.png + .json)
    public const string Layer = "ground";
    public enum Place { Flat, Open, Rocky, Shore, Mountain }
    public struct Kind
    {
        public string name;
        public float px;          // drawn cell width on a 1080 px screen
        public Place on;
        public float perThree;    // copies per three screens of ground
        public int[] near;        // must stand within NearReach of one of these kinds (null: anywhere)
        public bool mountain;
    }
    public const int Oasis = 0, Station = 1, Camp = 2, Zeppelin = 3, Spaceport = 4, Market = 5, Skiffs = 6, Solar = 7, Ruins = 8, Dig = 9,
                     Observatory = 10, Wind = 11, Rig = 12, Crawler = 13, Caravan = 14, Balloon = 15, Wreck = 16,
                     Mesa = 17, Ridge = 18, Hoodoo = 19, Volcano = 20;
    // composite.py GROUND / CTX / NEAR (install_plan.txt B, F), placed in this order's spirit: hosts first
    public static readonly Kind[] Kinds =
    {
        K("oasis", 150, Place.Flat, 1.5f), K("station", 180, Place.Flat, 1.2f), K("camp", 104, Place.Flat, 2f), K("zeppelin", 250, Place.Flat, 1f),
        K("spaceport", 210, Place.Flat, 1f), K("market", 210, Place.Flat, 1f, Oasis), K("skiffs", 120, Place.Shore, 1.5f),
        K("solar", 120, Place.Flat, 2f), K("ruins", 124, Place.Rocky, 1.5f), K("dig", 180, Place.Rocky, 1.2f), K("observatory", 190, Place.Rocky, 1f),
        K("wind", 120, Place.Open, 2f), K("rig", 112, Place.Open, 2f), K("crawler", 110, Place.Open, 2f), K("caravan", 110, Place.Flat, 2f, Camp, Oasis, Market),
        K("balloon", 190, Place.Flat, 1f, Camp, Station), K("wreck", 140, Place.Open, 1.5f),
        M("mesa", 340), M("ridge", 360), M("hoodoo", 320), M("volcano", 350),
    };
    static Kind K(string n, float px, Place on, float per, params int[] near)
    {
        return new Kind { name = n, px = px, on = on, perThree = per, near = near != null && near.Length > 0 ? near : null };
    }
    static Kind M(string n, float px) { return new Kind { name = n, px = px, on = Place.Mountain, perThree = MountainPerThree / 4f, mountain = true }; }
    public static int Count => Kinds.Length;
    public static int IndexOf(string name) { for (int i = 0; i < Kinds.Length; i++) if (Kinds[i].name == name) return i; return -1; }
    public static string Strip(int kind, int variant) { return Kinds[kind].name + "_" + variant; }

    // ---- where -------------------------------------------------------------------------------------------------------------
    public static GroundClass On(Place p)
    {
        switch (p)
        {
            case Place.Flat: return GroundClass.Flat;
            case Place.Rocky: return GroundClass.Rocky;
            case Place.Shore: return GroundClass.Shore;
            case Place.Mountain: return GroundClass.Rocky | GroundClass.Plain;
            default: return GroundClass.Plain;
        }
    }
    public static float Need(Place p) { return p == Place.Flat ? .8f : p == Place.Rocky || p == Place.Mountain ? .55f : p == Place.Shore ? .45f : .85f; }
    public const float ContextShare = .8f;          // the context is read over a disc of this x the keep-out
    public const GroundClass Painted = GroundClass.Canopy;   // mask C: a painted feature, never under a prop

    // ---- sizes (a 1080 px wide screen spans the view: 2 x HalfWidth) -------------------------------------------------------
    public const float ScreenPx = 1080f;
    public static float Units(float px, float halfW) { return px / ScreenPx * 2f * halfW; }
    public static float CellSize(int kind, float halfW) { return Units(Kinds[kind].px, halfW); }
    public const float KeepCore = .42f, KeepMarginPx = 22f;
    // the keep-out half-size (a box in the planner; a disc in the plan): footprint + margin
    public static float Keep(int kind, float halfW) { return Units(Kinds[kind].px * KeepCore + KeepMarginPx, halfW); }
    public const float NearMin = 1f, NearMax = 2.6f;  // a NEAR kind's centre: this x (its host's keep-out + .6 x its own) away

    // ---- how many ----------------------------------------------------------------------------------------------------------
    public const float MountainPerThree = .65f;      // mountains (all four kinds together) per three screens, placed first
    public const int Pool = 26, MountainPool = 4;
    public const int Tries = 40;
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

    // Is a prop of `kind` centred at (x, gy) on its context, clear of painted features? (placement and the tests)
    public static bool OnContext(GroundPlanner planner, int kind, float x, double gy, float halfW)
    {
        if (planner == null || planner.Mask == null) return true;
        float r = Keep(kind, halfW) * ContextShare;
        var on = Kinds[kind].on;
        return planner.Match(x, gy, r, r, On(on)) >= Need(on) && planner.Match(x, gy, r, r, Painted) <= 0f;
    }
}

public partial class DesertDirector
{
    BackdropPool props, mountains;
    BackdropAtlas propArt;
    Sprite[][][] propStrips;                 // [kind][variant] -> its frames (null: not in this layout's atlas)
    int[] lastPropVariant;
    int lastPropKind = -1, balloonsDealt;
    float propTravel, propGap, mountainTravel, mountainGap;
    System.Random propRng;                   // its own stream: a prop never shifts the worms' or the weather's dice

    public BackdropPool Props => props;
    public BackdropPool Mountains => mountains;
    public int PropsSpawned { get; private set; }
    public int PropsBlockedByWorm { get; private set; }
    public int WormsStoppedByProp { get; private set; }
    public float ViewHalfWidth => HalfW;

    float PRand(float a, float b) { return a + (float)propRng.NextDouble() * (b - a); }

    void BuildProps()
    {
        propRng = new System.Random((SeedOverride > 0 ? SeedOverride : 1990) * 53 + 29);
        propArt = set.VariantAtlas(DesertPropsTuning.Atlas);
        int n = DesertPropsTuning.Count;
        propStrips = new Sprite[n][][];
        lastPropVariant = new int[n];
        for (int k = 0; k < n; k++)
        {
            lastPropVariant[k] = -1;
            var list = new System.Collections.Generic.List<Sprite[]>();
            for (int v = 0; v < 16; v++)
            {
                var f = propArt != null ? propArt.Frames(DesertPropsTuning.Strip(k, v)) : new Sprite[0];
                if (f.Length == 0) break;
                list.Add(f);
            }
            propStrips[k] = list.Count > 0 ? list.ToArray() : null;
        }
        // back to front on the ground plane: worms (-9), wanderers (-8), mountains (-6), props (-3), sites (0)
        mountains = Pool(DesertPropsTuning.Layer, DesertPropsTuning.MountainPool, false, -6);
        props = Pool(DesertPropsTuning.Layer, DesertPropsTuning.Pool, false, -3);
        Grade(mountains, DesertPropsTuning.Layer);
        Grade(props, DesertPropsTuning.Layer);
        float screens3 = 6f * HalfH;
        propGap = PRand(.3f, 1f) * screens3 / DesertPropsTuning.TotalPerThree(false);
        mountainGap = PRand(.2f, .8f) * screens3 / DesertPropsTuning.MountainPerThree;
    }

    public bool HasPropArt(int kind) { return propStrips != null && kind >= 0 && kind < propStrips.Length && propStrips[kind] != null; }
    public int PropVariants(int kind) { return HasPropArt(kind) ? propStrips[kind].Length : 0; }
    public float PropKeep(int kind) { return DesertPropsTuning.Keep(kind, HalfW); }

    // the opening view: mountains and props already standing (over the whole view and the band above it)
    void OpeningProps()
    {
        if (props == null || !planner.Ready) return;
        double t = planner.Travel;
        int m = Mathf.RoundToInt(DesertPropsTuning.MountainPerThree / 3f * 1.3f + (float)propRng.NextDouble());
        for (int i = 0; i < m; i++) SpawnProp(-1, t - HalfH * .7, t + HalfH * 1.2, true);
        int n = Mathf.RoundToInt(DesertPropsTuning.TotalPerThree(false) / 3f * 1.3f);
        for (int i = 0; i < n; i++) SpawnProp(-1, t - HalfH * .9, t + HalfH * 1.3, false);
    }

    bool NearReady(int kind)
    {
        var near = DesertPropsTuning.Kinds[kind].near;
        if (near == null) return true;
        foreach (var p in props.items)
            if (p.active && System.Array.IndexOf(near, p.kind) >= 0) return true;
        return false;
    }

    int PickProp(bool mountain, int not)
    {
        float total = 0f;
        var ks = DesertPropsTuning.Kinds;
        for (int i = 0; i < ks.Length; i++)
            if (ks[i].mountain == mountain && i != not && (i != lastPropKind || mountain) && HasPropArt(i) && NearReady(i)) total += ks[i].perThree;
        if (total <= 0f) return -1;
        float r = PRand(0f, total);
        for (int i = 0; i < ks.Length; i++)
        {
            if (ks[i].mountain != mountain || i == not || (i == lastPropKind && !mountain) || !HasPropArt(i) || !NearReady(i)) continue;
            r -= ks[i].perThree;
            if (r <= 0f) return i;
        }
        return -1;
    }

    // a candidate centre: anywhere in the band, or (a NEAR kind) round one of its hosts
    bool PropCandidate(int kind, double lo, double hi, out float x, out double gy)
    {
        var near = DesertPropsTuning.Kinds[kind].near;
        float keep = PropKeep(kind);
        if (near != null)
        {
            int hosts = 0;
            foreach (var p in props.items) if (p.active && System.Array.IndexOf(near, p.kind) >= 0) hosts++;
            int pick = propRng.Next(Mathf.Max(1, hosts));
            foreach (var p in props.items)
            {
                if (!p.active || System.Array.IndexOf(near, p.kind) < 0 || pick-- > 0) continue;
                float a = PRand(0f, 2f * Mathf.PI);
                float d = PRand(DesertPropsTuning.NearMin, DesertPropsTuning.NearMax) * (PropKeep(p.kind) + keep * .6f);
                x = p.x + Mathf.Cos(a) * d;
                gy = p.gy + Mathf.Sin(a) * d;
                return x >= XMin(keep) && x <= XMax(keep) && gy >= lo - (hi - lo) && gy <= hi + (hi - lo) + keep * 2f;
            }
        }
        x = PRand(XMin(keep), XMax(keep));
        gy = lo + propRng.NextDouble() * (hi - lo);
        return true;
    }

    // A prop (kind < 0: one picked) centred in the ground band [lo, hi] on its context, its keep-out free and off every live
    // worm's path. `variant` < 0: the copy's own. False: nothing placed.
    public bool SpawnProp(int kind, double lo, double hi, bool mountain = false, int variant = -1, int tries = DesertPropsTuning.Tries)
    {
        if (props == null || !planner.Ready) return false;
        if (kind >= 0) mountain = DesertPropsTuning.Kinds[kind].mountain;
        var pool = mountain ? mountains : props;
        if (pool.ActiveCount >= pool.Capacity) return false;
        int tried = -1;
        for (int k = 0; k < 3; k++)
        {
            int kd = kind >= 0 ? kind : PickProp(mountain, tried);
            if (kd < 0 || !HasPropArt(kd) || (kind >= 0 && !NearReady(kd))) return false;
            tried = kd;
            float keep = PropKeep(kd);
            float x = 0f; double gy = 0.0; bool found = false;
            for (int t = 0; t < tries && !found; t++)
            {
                float cx; double cy;
                if (!PropCandidate(kd, lo, hi, out cx, out cy)) continue;
                if (!planner.Free(cx, cy, keep, keep)) continue;
                if (!DesertPropsTuning.OnContext(planner, kd, cx, cy, HalfW)) continue;
                if (!WormPathClear(cx, cy, keep)) { PropsBlockedByWorm++; continue; }
                x = cx; gy = cy; found = true;
            }
            if (!found) { if (kind >= 0) return false; continue; }
            return Commit(kd, x, gy, variant) != null;
        }
        return false;
    }

    BackdropPiece Commit(int kd, float x, double gy, int variant)
    {
        bool mountain = DesertPropsTuning.Kinds[kd].mountain;
        var pool = mountain ? mountains : props;
        float keep = PropKeep(kd);
        int foot = planner.Claim(x, gy, keep, keep);
        if (foot < 0) return null;
        var p = pool.Spawn();
        if (p == null) { planner.Release(foot); return null; }
        int n = propStrips[kd].Length;
        int v;
        if (variant >= 0) v = Mathf.Min(variant, n - 1);
        else if (kd == DesertPropsTuning.Balloon && balloonsDealt == 0) v = (BackdropVariants.For("Desert").Picks + (SeedOverride > 0 ? SeedOverride : 0)) % n;   // each landing leads with another livery
        else v = DesertPropsTuning.Variant(PropsSpawned, kd, n, lastPropVariant[kd]);
        if (v == lastPropVariant[kd] && variant < 0 && n > 1) v = (v + 1) % n;
        if (kd == DesertPropsTuning.Balloon) balloonsDealt++;
        lastPropVariant[kd] = v;
        p.frames = propStrips[kd][v];
        var s = p.frames[0];
        // the atlas cells are cut at their drawn px on a 1080 px screen: 100 sheet px a sprite unit
        float k = 2f * HalfW / DesertPropsTuning.ScreenPx * BackdropAtlas.PixelsPerUnit;
        SetSprite(p, s, s.bounds.size.x * k);
        p.size = DesertPropsTuning.CellSize(kd, HalfW);
        p.body.localScale = Vector3.one;                       // never mirrored, never turned
        p.body.localPosition = Vector3.zero;
        p.body.localRotation = Quaternion.identity;
        p.footprint = foot;
        p.core = DesertPropsTuning.KeepCore;
        p.gy = gy;
        p.x = x;
        p.y = planner.Y(gy);
        p.vx = p.vy = 0f;
        p.rate = set.Spec.Rate(DesertPropsTuning.Layer);
        p.color = DesertPropsTuning.PropTint;
        p.dockTint = Color.white;
        p.kind = kd;
        p.tier = v;
        p.age = 0f;
        p.phase = PRand(0f, p.frames.Length * DesertPropsTuning.FrameMs / 1000f);   // its own animation phase
        p.dyingT = -1f;
        Place(p);
        PaintProp(p);
        if (!mountain) lastPropKind = kd;
        PropsSpawned++;
        return p;
    }

    public static int PropFrame(BackdropPiece p)
    {
        int n = p.frames != null ? p.frames.Length : 1;
        return n <= 1 ? 0 : Mathf.FloorToInt((p.age + p.phase) * 1000f / DesertPropsTuning.FrameMs) % n;
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

    void StepPropPool(BackdropPool pool, float dt)
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

    void StepProps(float dt, float scrolled)
    {
        if (props == null) return;
        propTravel += scrolled;
        mountainTravel += scrolled;
        float screens3 = 6f * HalfH;
        if (planner.Ready)
        {
            double lo = planner.Travel + HalfH, hi = lo + DesertTuning.SpawnBand;
            // the mountains first: their ground is taken before the smaller props look for theirs
            if (mountainTravel >= mountainGap)
            {
                if (SpawnProp(-1, lo + PropKeep(DesertPropsTuning.Ridge), hi + PropKeep(DesertPropsTuning.Ridge), true))
                { mountainTravel = 0f; mountainGap = PRand(.6f, 1.4f) * screens3 / DesertPropsTuning.MountainPerThree; }
                else mountainGap = mountainTravel + DesertTuning.SpawnRetry;
            }
            if (propTravel >= propGap)
            {
                if (SpawnProp(-1, lo + DesertTuning.SpawnAbove, hi + DesertTuning.SpawnAbove, false))
                { propTravel = 0f; propGap = PRand(.6f, 1.4f) * screens3 / DesertPropsTuning.TotalPerThree(false); }
                else propGap = propTravel + DesertTuning.SpawnRetry;
            }
        }
        StepPropPool(mountains, dt);
        StepPropPool(props, dt);
    }

    // ---- keep-out queries ---------------------------------------------------------------------------------------------------

    // Is there a live prop (or mountain) whose keep-out box meets the box (x, gy, rx, ry)?
    public bool TouchesProp(float x, double gy, float rx, float ry)
    {
        return TouchesIn(mountains, x, gy, rx, ry) || TouchesIn(props, x, gy, rx, ry);
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

    // A prop with keep-out half-size `keep` at (x, gy) stays off every live worm's REMAINING path (its ridge, eruption and the
    // ridge after it, as PathClear plans them) plus the worm margin.
    public bool WormPathClear(float x, double gy, float keep)
    {
        if (worms == null) return true;
        float m = DesertTuning.WormMargin;
        foreach (var w in worms.items)
        {
            if (!w.active) continue;
            float rx, ry;
            WormFoot(w.kind, true, out rx, out ry);
            float ahead = w.tier == WormRidge ? Mathf.Max(0f, w.life - w.phase) + 1.2f + 4.3f : w.tier == WormFading ? DesertTuning.WormFade : 5.5f;
            for (float t = 0f; t <= ahead; t += .25f)
            {
                float px = w.x + w.vx * t; double pgy = w.gy + w.vy * t;
                if (Mathf.Abs(px - x) < keep + rx + m && System.Math.Abs(pgy - gy) < keep + ry + m) return false;
            }
        }
        return true;
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
                Despawn(p);
            }
        }
    }

    // Tests / previews: hold off the props' own spawner for this much scrolled ground.
    public void HoldProps(float ground)
    {
        propTravel = 0f; propGap = ground;
        mountainTravel = 0f; mountainGap = ground;
    }
}
