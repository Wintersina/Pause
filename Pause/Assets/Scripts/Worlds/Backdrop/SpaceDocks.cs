using System.Collections.Generic;
using UnityEngine;

// Space's launch docks: four big drifting set pieces the Space elites come out of, rarer and larger than the stations --
//
//   gantry    an orbital shipyard gantry (a scaffold frame with a launch bay slung in it)      LandingKind.Gantry
//   carrier   a carrier hangar (a long hull, the bay door on its flank)                        LandingKind.Carrier
//   gate      a ring gate (a rusted ring round a dark, crackling iris)                         LandingKind.Gate
//   derelict  a derelict dock (a torn-open hulk, a cargo bay in its middle)                    LandingKind.Derelict
//
// They drift through like the stations do: upright (never a tilt or a spin), in the structures' class on the `mid` layer
// (DockTier), with clear sky round them (Fits: the stations and each other), one every DockGapMin..Max seconds (the first after
// DockFirst), the four kinds in a shuffled bag so every one turns up before any turns up twice. Driven by a stream of their own
// (`drng`, SpaceExtras), so the seeded station / planet sequence is unchanged; nothing spawns while Planetfall / Lift-off hold the
// sky quiet.
//
// Each offers one LandingSite at its pad (DockDefs: the pixel of the dark opening where the elite comes out, in the Codex master
// -- the fractions of the cell from its centre are what the site uses), `emerge`: docked inside, hidden, ONE small light blinking in
// the launch tell (EliteLaunchTell), then the ship simply flying out (no flash). The dock itself plays its 6-cell launch strip through the elite's launch
// (EliteShip.LaunchPhase): 0 idle through the tell's first DockBlinkShare (the light blink / flare cells 1 and 3 are never shown now),
// then the doors / iris open (2), fully open (4) until the ship is clear, then closing (5, DockCloseSeconds), idle again.
//
// An elite is meant to launch from a body it knows (EliteDef.launchFrom: rift_lancer gate / station, orbit_reaver carrier /
// asteroid, eventide_bastion gantry / station, singularity_hauler derelict / planet): when it is due and none of its kinds is in
// view, the elite director asks (LandingSites.BringHome -> BringHome here) and one of its dock kinds enters a few seconds ahead.
//
// Art: Backdrop/docks.png (+ .json), the Codex masters shrunk 4x to 256 px wide cells (Art/Worlds/Space/src~/build_docks_atlas.py):
// sprites <tag>_00..05. ~0.6 MB on a phone, inside the Space texture budget.
public partial class SpaceDirector
{
    public struct DockDef
    {
        public string tag;              // atlas prefix: <tag>_00..05
        public LandingKind kind;
        public string launchKey;        // the word a def's launchFrom uses
        public int padX, padY, srcW, srcH;   // the pad in the Codex master, pixels from its top-left; the master's size
        public Vector2 Pad => new Vector2((float)padX / srcW - .5f, .5f - (float)padY / srcH);   // fractions of the cell from its centre, x right, y UP
    }

    public static readonly DockDef[] DockDefs =
    {
        new DockDef { tag = "gantry",   kind = LandingKind.Gantry,   launchKey = "gantry",   padX = 512, padY = 593, srcW = 1024, srcH = 936 },
        new DockDef { tag = "carrier",  kind = LandingKind.Carrier,  launchKey = "carrier",  padX = 577, padY = 360, srcW = 1024, srcH = 512 },
        new DockDef { tag = "gate",     kind = LandingKind.Gate,     launchKey = "gate",     padX = 511, padY = 397, srcW = 1024, srcH = 950 },
        new DockDef { tag = "derelict", kind = LandingKind.Derelict, launchKey = "derelict", padX = 512, padY = 516, srcW = 1024, srcH = 836 },
    };

    public const string DocksAtlas = "docks";
    public const int DockCells = 6, DockIdBase = 500, DockKind = 4;
    public const int DockTier = 2;                                      // Tiers[2]: the `mid` layer, .86 light, .9 clarity
    public const float DockWidthMin = 1.45f, DockWidthMax = 1.85f;      // drawn width, world units (the view is ~5.9 wide)
    public const float DockReach = .62f;                                // half-extent used to keep clear of the other structures, x its width
    public const float DockLaneMin = .9f, DockLaneMax = 1.5f;           // |x| of its centre
    public const float DockGapMin = 55f, DockGapMax = 90f, DockFirst = 30f;
    // The launch strip: the tell's first share blinks the lights, the lift-off's first share is the flare, the doors then close for this long.
    public const float DockBlinkShare = .4f, DockFlareShare = .2f, DockCloseSeconds = .6f;
    public const int DockIdle = 0, DockBlink = 1, DockOpening = 2, DockFlare = 3, DockOpen = 4, DockClosing = 5;

    // (a stream of its own: the extras' (xrng) sequence -- outposts, wrecks, debris -- and the seeded station / planet one are unchanged)
    readonly System.Random drng = new System.Random(5309);
    float DRand(float a, float b) { return a + (float)drng.NextDouble() * (b - a); }
    BackdropPool docks;
    Sprite[][] dockArt;                                 // per DockDefs entry: its 6 launch cells (null: atlas missing)
    int[] dockType, dockCell;                           // per pool item: which dock it is, the cell it shows
    bool[] dockOpened;                                  // per pool item: the doors have been opened for a launch
    float[] dockClose;                                  // per pool item: seconds of closing left
    Timer dockTimer;
    readonly int[] dockBag = new int[DockDefs.Length];
    int dockBagLeft, dockSide = 1;

    public BackdropPool Docks { get { return docks; } }
    public Sprite[] DockFrames(int type) { return dockArt != null && type >= 0 && type < dockArt.Length ? dockArt[type] : null; }
    // Which dock a pool item is (-1: not a dock), and the launch cell it shows now.
    public int DockTypeOf(BackdropPiece p) { int i = docks != null ? docks.items.IndexOf(p) : -1; return i >= 0 && p.active ? dockType[i] : -1; }
    public int DockCellOf(BackdropPiece p) { int i = docks != null ? docks.items.IndexOf(p) : -1; return i >= 0 ? dockCell[i] : -1; }

    void BuildDocks()
    {
        var atlas = set.Atlas(DocksAtlas);
        dockArt = new Sprite[DockDefs.Length][];
        bool any = false;
        for (int t = 0; t < DockDefs.Length; t++)
        {
            var f = atlas.Frames(DockDefs[t].tag);
            dockArt[t] = f.Length >= DockCells ? f : null;
            any |= dockArt[t] != null;
        }
        if (!any) { dockArt = null; return; }
        docks = Pool(Tiers[DockTier].layer, 2);
        int n = docks.items.Count;
        dockType = new int[n];
        dockCell = new int[n];
        dockOpened = new bool[n];
        dockClose = new float[n];
        dockTimer = new Timer(DockGapMin, DockGapMax, DockFirst);
    }

    int NextDockType()
    {
        if (dockBagLeft == 0)
        {
            int n = 0;
            for (int t = 0; t < DockDefs.Length; t++) if (dockArt[t] != null) dockBag[n++] = t;
            for (int i = n - 1; i > 0; i--)
            {
                int j = drng.Next(i + 1);
                int tmp = dockBag[i]; dockBag[i] = dockBag[j]; dockBag[j] = tmp;
            }
            dockBagLeft = n;
        }
        return dockBag[--dockBagLeft];
    }

    // Would a dock entering at (x, y) keep clear of the docks already in the sky (Fits asks, for the stations' lane)?
    bool DocksFit(float x, float y, float reach, float rate)
    {
        if (docks == null) return true;
        foreach (var e in docks.items)
        {
            if (!e.active) continue;
            float er = e.size * DockReach, gap = y - e.y;
            if (gap < MinSpacing + reach + er) return false;
            if (rate <= e.rate) continue;
            if (Mathf.Abs(x - e.x) >= reach + er + Clearance) continue;
            float scroll = (e.y + HalfH + e.size * 0.9f + 1f) / e.rate;
            if (gap - (rate - e.rate) * scroll < reach + er + Clearance) return false;
        }
        return true;
    }

    // Bring dock `type` in at the top; false if the sky has no room (unless `force`: a body an elite is waiting for).
    bool SpawnDock(int type, bool force)
    {
        if (docks == null || dockArt[type] == null) return false;
        float size = DRand(DockWidthMin, DockWidthMax);
        float reach = size * DockReach;
        float rate = set.Spec.Rate(Tiers[DockTier].layer);
        float y = HalfH + reach + .3f;
        float lane = DRand(DockLaneMin, DockLaneMax);
        float x = -dockSide * lane;
        if (!Fits(x, y, reach, rate, false))
        {
            x = dockSide * lane;
            if (!Fits(x, y, reach, rate, false) && !force) return false;
        }
        var p = docks.Spawn();
        if (p == null) return false;
        int i = docks.items.IndexOf(p);
        SetSprite(p, dockArt[type][DockIdle], size);
        p.kind = DockKind;
        p.tier = DockTier;
        p.rate = rate;
        p.x = x;
        p.y = y;
        p.phase = 0f;
        p.root.localRotation = Quaternion.identity;
        p.color = Lit(StationTint, Tiers[DockTier]);
        dockType[i] = type;
        dockCell[i] = DockIdle;
        dockOpened[i] = false;
        dockClose[i] = 0f;
        dockSide = x < 0f ? -1 : 1;
        Place(p);
        Paint(p, 1f);
        return true;
    }

    void StepDocks(float dt, float v)
    {
        if (docks == null) return;
        if (dockTimer.Tick(dt, drng) && !Quiet && docks.ActiveCount == 0) SpawnDock(NextDockType(), false);
        for (int i = 0; i < docks.items.Count; i++)
        {
            var p = docks.items[i];
            if (!p.active || !Drift(p, dt, v)) continue;
            Paint(p, 1f);
            AnimateDock(i, p, dt);
        }
    }

    // The launch strip, from the elite parked in this dock (EliteShip.LaunchPhase).
    void AnimateDock(int i, BackdropPiece p, float dt)
    {
        EliteShip ship = null;
        var live = EliteShip.Live;
        for (int k = 0; k < live.Count; k++)
        {
            var e = live[k];
            if (e != null && e.State != EliteState.Dead && e.Site.anchor == p.root) { ship = e; break; }
        }
        float ph = ship != null ? ship.LaunchPhase : -1f;
        int cell = DockIdle;
        if (ph > 0f && ph < 2f)
        {
            dockOpened[i] = true;
            dockClose[i] = 0f;
            cell = DockCellAt(ph);
        }
        else
        {
            // the elite is clear of it (or gone): the doors close, then it is idle again
            if (dockOpened[i]) { dockOpened[i] = false; dockClose[i] = DockCloseSeconds; }
            if (dockClose[i] > 0f) { dockClose[i] -= dt; cell = DockClosing; }
        }
        dockCell[i] = cell;
        var s = dockArt[dockType[i]][cell];
        if (p.sr.sprite != s) p.sr.sprite = s;
    }

    // The launch cell at LaunchPhase `ph` in (0, 2).
    public static int DockCellAt(float ph)
    {
        if (ph < 1f)
        {
            // (the dock's own lights stay out: the one small launch light is the tell; the doors then open)
            float t = ph * EliteShip.EngineTellSeconds;
            return t >= DockBlinkShare * EliteShip.EngineTellSeconds ? DockOpening : DockIdle;
        }
        // (the strip's flare cell is skipped: doors open -> open, no flash)
        return DockOpen;
    }

    // ---- LandingSites -----------------------------------------------------------------------------------------

    void DockSites(List<LandingSite> into)
    {
        if (docks == null) return;
        for (int i = 0; i < docks.items.Count; i++)
        {
            var p = docks.items[i];
            if (!p.active || p.sr == null || p.sr.sprite == null) continue;
            if (p.y < -HalfH * .15f || p.y > HalfH - p.size * .3f) continue;
            if (Mathf.Abs(p.x) > HalfW - .25f) continue;
            var d = DockDefs[dockType[i]];
            Bounds b = p.sr.sprite.bounds;
            Vector2 pad = d.Pad;
            into.Add(new LandingSite
            {
                anchor = p.root,
                local = new Vector3(b.center.x + pad.x * b.size.x, b.center.y + pad.y * b.size.y, 0f),
                scale = Mathf.Clamp(p.size * EmergeScalePerUnit, EmergeScaleMin, EmergeScaleMax),
                order = p.sr.sortingOrder + 1,
                id = DockIdBase + i,
                kind = d.kind,
                emerge = true,
            });
        }
    }

    // The elite director wants a body of one of `launchFrom`'s kinds in view soon: one of those docks enters (or is already
    // in the sky). False when none of the kinds is a dock (a planet, a station: they are not summoned).
    public override bool BringHome(string launchFrom)
    {
        if (docks == null || string.IsNullOrEmpty(launchFrom)) return false;
        var want = new List<int>(DockDefs.Length);   // (a rare call: one per elite)
        foreach (string part in launchFrom.Split(','))
        {
            var k = LandingSite.KindOf(part.Trim());
            if (k == null) continue;
            for (int t = 0; t < DockDefs.Length; t++)
                if (DockDefs[t].kind == k.Value && dockArt[t] != null && !want.Contains(t)) want.Add(t);
        }
        if (want.Count == 0) return false;
        // one dock at a time (as on every ground world): a dock already drifting through serves, whatever its kind (the elite falls
        // back to any free body); a second is never forced in beside it
        for (int i = 0; i < docks.items.Count; i++)
            if (docks.items[i].active && (DockGrade.Strict || want.Contains(dockType[i]))) return true;
        return SpawnDock(want[drng.Next(want.Count)], true);
    }
}
