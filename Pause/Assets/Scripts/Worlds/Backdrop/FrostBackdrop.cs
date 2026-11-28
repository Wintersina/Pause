using System.Collections.Generic;
using UnityEngine;

// Frost's backdrop: the ship flies at atmosphere level just under the cloud
// ceiling, HIGH over a frozen ocean, its coast and ice-bound industry
// (FrostTuning.GroundZoom: the ground drawn at .41 of the view's width and
// repeated across, creeping by at .41 of its old rate). Nothing space-like:
// no stars, planets, stations or rocks.
//
//   GROUND     one of four tile sets per landing (FrostBackdropSelection):
//              sky / far / mid tiles and the floe / frost-streak flow strip,
//              which is pinned to the mid tile like everything on it.
//   LANDMARKS  rigs, platforms, refineries, relays, causeways, icebreakers,
//              cliffs, derricks, convoys (landmarks atlas) on one ground
//              plane PINNED to the mid tile and placed where its affinity
//              mask says they belong (GroundPlanner, FrostTuning.Rule): the
//              2026-11 redraw draws each on its own slab of snow / rock, so
//              each stands with its whole slab, and a clear rim round it, on
//              a FLAT TOP -- an ice plate, a snow flat, a plateau (mask O,
//              frost_redraw/flat_masks.py) -- never on a cliff face, the
//              floe rubble, across a gorge or a lead, never on the painted
//              quays, plants or props (mask B); a cliff stands on a flat top
//              with open water just beyond it; on the glacier (v4) nothing
//              hangs over a crevasse. Footprints never
//              overlap; never the same family twice running, mixed per
//              variant (FrostTuning.FamilyWeights). Their ambient loops --
//              beacons and strobes on the drawn lamps, searchlights, lit
//              windows, smoke from drawn stacks, a fire on a slab's snow
//              beside the building -- are FrostAmbientCatalog's emitters.
//   SITES      the elites' launch sites on the same ground plane (sites
//              atlas): ice-shelf hangar, rig lift bay, relay pad ring,
//              crawler garage, silo hatch. Drawn shut; the launch tell opens
//              them (open drawing, lamp row blinking) while the elite
//              emerges; they close again behind it. Four more come with a
//              6-cell launch strip each (docks atlas): an ice-locked offshore
//              rig and an icebreaker deck in the leads, a cryo silo on the
//              snow flats, a glacier cave hangar on the ridges. They play
//              idle (the elite's one small blinking launch light is the tell:
//              EliteLaunchTell, drawn by the ship at the pad) -> doors opening
//              -> open -> closing through the launch (FrostTuning.LaunchCellAt)
//              -- never a flash, flare, halo or ring, and no blink cell of
//              the site's own.
//   WEATHER    the CLOUD CEILING (cloud banks thick over the view at the
//              start -- the planetfall drops through it -- held for
//              FrostTuning.CeilingHold s, then clearing fast, gone by
//              CeilingClearSeconds; see CloudCover), then only a light
//              scattering of cloud wisps and low mist drifting with the wind
//              (FrostTuning.CloudDensity), snow fields at three depths, and BLIZZARD gusts:
//              translucent diagonal sheets sweeping across every so often.
//   PROPS      rare living accents on the same ground plane (FrostProps.cs): whales and orcas surfacing in the leads, an
//              icebreaker churning through, a research station, an abandoned lab, a crashed plane, their lamps lit.
//   AURORA     a faint animated overlay; strong only over the glacier night.
//
// Everything sits far below gameplay (BackdropCatalog.BaseOrder), so no
// weather ever covers an enemy or a bullet; the pools are fixed and a frame
// allocates nothing.
public static class FrostTuning
{
    // ---- brightness: THE knob -------------------------------------------
    // The art is painted dark (backdrop forms at HSV value <= .33); it is
    // lifted at draw time (BackdropGrade.shader) so the level reads brighter
    // without regrading a PNG. 1 = as painted; 1.7 lifts the ground's
    // midtones by ~+50% (shadows x1.7, highlights rolling off below white,
    // hue kept). The sky / far layers, mist, wisps, aurora and plumes take a
    // share of it (BackdropCatalog's .Graded); lights are never lifted.
    // "A bit brighter / darker" = change this one number. History: 1.7 (user,
    // 2026-10-08 "too dark"), 1.95 (2026-10-11 "too night-timey"). Ceiling: the
    // drawn-art caps in WorldBackdropTest (value p90 .55, tile luminance .24,
    // chroma .26) hold up to ~1.95; 2.3 = +15% frame luminance, 2.55 = +21%
    // need those caps raised (and Saturation eased to 1.0 for chroma).
    public static float Brightness = 1.95f;
    public static float Saturation = 1.05f;        // nudge on the lifted tones so the ice stays blue-cyan
    public static float PlumeShare = .6f;          // smoke / steam loops' share of the lift
    // The cloud ceiling's banks are painted translucent (alpha ~.28 at their
    // core); this thickens them (alpha a drawn as 1 - (1 - a)^k) so the
    // arrival reads as just having dropped out of the planetfall's bright
    // cloud deck (FrostBackdropTest holds it to the deck's brightness).
    public static float CeilingThicken = 7f;
    // ... and pales them toward the deck's white-blue (saturation x this).
    public static float CeilingSaturation = .7f;

    // ---- ALTITUDE: THE zoom knob ----------------------------------------
    // How big the ground is drawn: the tiles (sky / far / mid / flow) at this share of the view's width
    // (BackdropCatalog.Spec.groundZoom; BackdropTile repeats them across as well as down). User, 2026-11-27: "too zoomed in,
    // the player needs to be at atmosphere level ... much higher"; at .55: "if we can zoom out about 25% more that be great".
    // .41 = .55 x .75: the ground seen from ~2.4x the old height, ~6x the ground area in view. A tile is then ~450 screen px
    // wide on a 1080 px phone, so the tiles are installed at the size they are seen at -- the 896 px masters halved to
    // 448 px, one texel per 2 px art cell, drawn bilinear (frost_redraw/install_tiles.py, WorldBackdropImport.IsFrostHdTile):
    // point sampling the 896 px art at ~2x minification would skip every other texel and the ground would shimmer as it
    // scrolls. Everything on the ground scales with the zoom so it stays registered: the ground layers' rates (farther
    // ground creeps by slower: the same ground distance per second in tile terms), the pinned pieces' sizes, gaps and spawn
    // bands, the sites' launch size, the cover boulders (CoverBoulders.GroundZoom).
    public const float GroundZoom = .41f;
    // The ground (far / mid tiles, the floe strip) drawn this much darker than graded: a tint, a step of its own on top of the
    // art and its grade (FrostTuning.Brightness), so a colour regrade of the tiles can drop or retune it. .85 (2026-11-28) did
    // not rescue the pale shots over the finer ground (HostileProjectileTest's worst moved 8 -> 4 px) and the render read
    // dim / muddy: the shots and hearts are fixed on their own side now (EliteDef.boldShots on the whiteout sentinel's
    // shard, Spec.boldHearts), and the ground is only a touch shaded.
    public const float GroundShade = .92f;
    public const float GroundRate = .024f * GroundZoom;                // the mid tile's (and everything pinned to it) parallax rate
    // The pieces standing on the ground (landmarks, launch sites, with their lamps and loops, which ride them) are drawn at
    // this share of their old size against the ground art on top of the zoom: a slab must fit a flat plate top of the redrawn
    // tiles with a clear rim round it (PieceRule.rim, flat_masks.py). .6 at zoom .55; at .41 it is .7 so they stay readable
    // (~65 screen px a 256 cell, .87 of their size at .55): the flat plates still hold them -- in tile terms ~75-85% of the
    // pieces .6 placed (v2's busy harbour ~65%), the audit's frost_fix/compare_v*.png.
    public const float PieceFit = .7f;
    public const float PieceScale = GroundZoom * PieceFit;             // a pinned piece's world size vs. before the zoom

    // ---- ground plane ----
    // world units: a 256 cell at its old 1.45..1.75 (~1:1 on a phone) x PieceScale
    public static float LandmarkMin = 1.45f * PieceScale, LandmarkMax = 1.75f * PieceScale;
    // scrolled ground distance between spawns, in world units (x GroundZoom: the same ground of the tile between two tries
    // as before; the pool, not the gap, caps how many stand in view). (The old spacing-only spawn also waited for the top
    // of the view to clear; the planner only keeps footprints apart, so the gap carries the density.)
    public static float GapMin = .8f * GroundZoom, GapMax = 2.4f * GroundZoom;
    public const int GroundPool = 24;                                  // landmarks alive at once (in view and just under it: ~6x the old ground)
    public static float SiteMin = 1.5f * PieceScale, SiteMax = 1.7f * PieceScale;
    public static float SiteFirst = 3.2f * GroundZoom;                // ground distance before the first site
    public static float SiteGapMin = 5f * GroundZoom, SiteGapMax = 8.5f * GroundZoom;
    public static float SiteCloseDelay = .9f;                         // seconds a site stays open after its elite left
    public static float SiteMinSize = 1.2f * PieceScale;
    // where a new piece is looked for: a band SpawnBand tall starting SpawnAbove over the top of the view (just out of sight
    // for the small pieces), and how much further on the next look is when nothing fitted
    public static float SpawnAbove = .95f * PieceScale, SpawnBand = .9f * GroundZoom, SpawnRetry = .25f * GroundZoom;
    // a launch site that finds no flat top in SiteTries random tries scans a band SiteScanBand tall in SiteScanStep steps
    public const int SiteTries = 64;
    public static float SiteScanBand = 2.7f * GroundZoom, SiteScanStep = .15f * GroundZoom;
    // landmark families and their mix per variant (index 0 = no variant)
    public static readonly string[][] Families =
    {
        new[] { "rig_00", "rig_01" },
        new[] { "platform_00", "platform_01", "platform_02" },
        new[] { "refinery_00", "refinery_01" },
        new[] { "relay_00", "relay_01" },
        new[] { "causeway_00" },
        new[] { "icebreaker_00", "icebreaker_01" },
        new[] { "cliff_00", "cliff_01" },
        new[] { "derrick_00" },
        new[] { "convoy_00" },
    };
    public const int FamRig = 0, FamPlatform = 1, FamCauseway = 4, FamIcebreaker = 5, FamCliff = 6, FamConvoy = 8;
    // v4 (glacier) has no open water: its dark mask class is the crevasse
    // depths, so nothing that floats (icebreaker) or stands in the sea (rig,
    // platform) is drawn there -- it would hang over a chasm.
    public static readonly int[][] FamilyWeights =
    {
        new[] { 3, 3, 1, 1, 1, 3, 1, 1, 0 },   // (none) = frozen ocean
        new[] { 3, 3, 1, 1, 1, 3, 1, 1, 0 },   // v1 frozen ocean: rigs, platforms, icebreakers
        new[] { 1, 1, 2, 1, 2, 3, 3, 0, 1 },   // v2 coast + harbour
        new[] { 0, 1, 3, 3, 2, 0, 1, 3, 3 },   // v3 inland tundra industry
        new[] { 0, 0, 1, 3, 1, 0, 3, 2, 2 },   // v4 glacier / crevasse night
    };

    // ---- WHERE A PIECE MAY STAND (GroundPlanner; mask.txt per variant from
    // Art/Worlds/Frost/backdrop_v3/src~/frost_redraw/flat_masks.py: 64 x 128
    // cells of 14 px read off the drawing itself. O = FLAT open ground: the lit
    // tops of the ice plates, snow flats and glacier plateaus; C = everything
    // else that is land: cliff faces, floe rubble, ridges, slopes; W open
    // water / harbour basin / gorge / crevasse; B painted quays, roads, plant
    // and the props painted on the ice -- crates, tents, braziers).
    // The 2026-11 redraw draws EVERY landmark as a building / wreck / spire
    // standing on its own slab of snow or rock, so every one stands on a FLAT
    // TOP: its whole slab (PieceRule.foot: the rules are read under the slab,
    // not under the roof) on O -- every one of its footprint samples -- with a
    // clear rim of O round it (PieceRule.rim: RimScale x the footprint, all
    // but one of its 16 samples), so no slab hangs over a cliff edge, a
    // gorge, a lead or the rubble between plates. (2026-11-27, user: "still
    // placed randomly over cliffs and stuff": the old rule took O or C at
    // .95, and C held the cliff faces and rubble.) Fewer pieces where the
    // ground has few flat tops (v2's busy harbour) rather than bad ones.
    public static readonly bool[] Glacier = { false, false, false, false, true };   // index = variant (0 none)
    public const float LandNeed = 1f;        // of the footprint samples: every one (21 samples)
    public const float RimScale = 1.15f, RimNeed = .9f, RimRelaxed = .75f;   // the clear rim: its ring's size and share
    static readonly PieceRule Land = new PieceRule(GroundClass.Open, LandNeed, .34f).WithRim(RimScale, RimNeed);                    // every landmark
    static readonly PieceRule IceRoad = new PieceRule(GroundClass.Open, LandNeed, .3f).WithRim(RimScale, RimNeed);                  // the convoy (a gun / crane depot on its slab)
    static readonly PieceRule Shore = new PieceRule(GroundClass.Open, LandNeed, .3f, GroundClass.Water).WithRim(RimScale, RimNeed); // an ice cliff / berg: on a flat top, open water just beyond it
    static readonly PieceRule Plateau = Land;                                                                                        // v4: on a plateau top, never over a crevasse
    // the causeway (redrawn: a rail embankment with its train, drawn on its own
    // snow slab): on a flat top like the rest, its two ends too (they keep it
    // from being mirrored: the train runs from the upper-left to the
    // lower-right end). Ends as a share of its drawn width from the centre, y up.
    public static readonly PieceRule Span = new PieceRule(GroundClass.Open, LandNeed, .34f).WithRim(RimScale, RimNeed)
        .WithEnds(new Vector2(-.32f, .19f), new Vector2(.30f, -.11f), GroundClass.Open, .12f);
    static readonly PieceRule Anywhere = new PieceRule(GroundClass.Any, 0f, .2f);
    // Where each landmark's slab sits (PieceRule.foot: its centre from the
    // drawing's centre, share of the drawn width, y up), measured on
    // landmarks.png (the rows at least 70% of the drawing's widest); re-measured
    // on the HD rebuild (sources_hd), whose drawings sit differently in their cells.
    static readonly string[] FootNames = { "rig_00", "rig_01", "platform_00", "platform_01", "platform_02", "refinery_00", "refinery_01", "relay_00",
                                           "relay_01", "causeway_00", "icebreaker_00", "icebreaker_01", "cliff_00", "cliff_01", "derrick_00", "convoy_00" };
    static readonly float[] Feet = { -.25f, -.12f, -.22f, -.24f, -.11f, -.14f, -.06f, -.33f, -.18f, -.03f, -.21f, -.05f, -.14f, -.08f, -.17f, -.02f };
    public static float FootOf(string piece)
    {
        for (int i = 0; i < FootNames.Length; i++) if (FootNames[i] == piece) return Feet[i];
        return 0f;
    }

    // v3 (inland tundra) is mostly painted plant and roads with snow flats
    // between: its flats are O like everywhere else (flat_masks.py reads them
    // off the drawing; the plant, the tank roofs and the canyon walls are not).
    public static readonly bool[] Inland = { false, false, false, true, false };
    static readonly PieceRule Unbuilt = Land;
    // Every launch pad stands like a landmark: its whole footprint on a flat top with a clear rim round it -- never in a lead,
    // on a cliff face, on rubble, a painted quay, plant or prop. Relaxed (nothing fitted in the band): the rim may be broken
    // at a quarter of its samples, the footprint itself still all flat.
    public static readonly PieceRule SiteSolid = new PieceRule(GroundClass.Open, LandNeed, .34f).WithRim(RimScale, RimNeed);
    public static readonly PieceRule SiteRelaxed = new PieceRule(GroundClass.Open, LandNeed, .34f).WithRim(RimScale, RimRelaxed);
    public static PieceRule SiteRule(int variant, bool relaxed) { return relaxed ? SiteRelaxed : SiteSolid; }
    public static readonly PieceRule SiteRelaxedOld = new PieceRule(GroundClass.Water | GroundClass.Open | GroundClass.Canopy, .6f, .3f);   // (suites only: DockGrade.Strict off)

    public static bool IsGlacier(int variant) { return variant >= 0 && variant < Glacier.Length && Glacier[variant]; }
    public static bool IsInland(int variant) { return variant >= 0 && variant < Inland.Length && Inland[variant]; }

    // A landmark's rule (landmarks atlas names).
    public static PieceRule Rule(string piece, int variant = 1)
    {
        if (piece == null) return Anywhere;
        float foot = FootOf(piece);
        if (piece.StartsWith("causeway")) return Span.WithFoot(foot);
        if (piece.StartsWith("cliff")) return Shore.WithFoot(foot);
        if (piece.StartsWith("convoy")) return IceRoad.WithFoot(foot);
        if (IsGlacier(variant)) return Plateau.WithFoot(foot);
        return (IsInland(variant) ? Unbuilt : Land).WithFoot(foot);
    }

    // A launch site's own rule (SiteKinds index), for DockGrade.Strict off (suites): the docks' rig and icebreaker deck share their
    // names (rig_00, icebreaker_00) with the landmarks, so they are looked up by kind. The rig stands on its legs in a lead.
    static readonly PieceRule Offshore = new PieceRule(GroundClass.Water, .6f, .34f);
    static readonly PieceRule Floating = new PieceRule(GroundClass.Water, .8f, .26f);
    static readonly PieceRule Flats = new PieceRule(GroundClass.Open, .72f, .34f);                            // a cryo silo: squat on a snow flat
    static readonly PieceRule Ridge = new PieceRule(GroundClass.Canopy, .7f, .34f);                           // a glacier cave hangar: cut into the ridge / pack ice
    static readonly PieceRule Island = new PieceRule(GroundClass.Water | GroundClass.Open, .72f, .34f);
    public static PieceRule DockRule(int kind, int variant = 1)
    {
        if (IsGlacier(variant)) return Plateau;
        var k = SiteKinds[Mathf.Clamp(kind, 0, SiteKinds.Length - 1)];
        if (k == LandingKind.Rig || k == LandingKind.RigBay) return Offshore;
        if (k == LandingKind.Icebreaker) return Floating;
        if (k == LandingKind.Silo) return Flats;
        if (k == LandingKind.Cave) return Ridge;
        return IsInland(variant) ? Unbuilt : Island;
    }

    // launch sites: drawings (closed, open) and how often each comes
    // Entries 0-4 are the old two-drawing sites (sites atlas); 5-8 the launch-strip ones (docks atlas: <name>_00..05, DocksAtlas), for which
    // SiteClosed / SiteOpen name the idle and the open cell (the rule: SiteRule, or DockRule by kind with DockGrade.Strict off).
    public static readonly string[] SiteNames = { "hangar", "rigbay", "padring", "crawlerbay", "hatch", "rig", "silo", "cave", "icebreaker" };
    public static readonly string[] SiteClosed = { "hangar_closed", "rigbay_closed", "padring_idle", "crawlerbay_closed", "hatch_closed", "rig_00", "silo_00", "cave_00", "icebreaker_00" };
    public static readonly string[] SiteOpen = { "hangar_open", "rigbay_open", "padring_active", "crawlerbay_open", "hatch_open", "rig_03", "silo_03", "cave_03", "icebreaker_03" };
    public static readonly LandingKind[] SiteKinds = { LandingKind.Hangar, LandingKind.RigBay, LandingKind.PadRing, LandingKind.CrawlerBay, LandingKind.Hatch,
                                                       LandingKind.Rig, LandingKind.Silo, LandingKind.Cave, LandingKind.Icebreaker };
    public static readonly int[] SiteWeights = { 3, 2, 2, 3, 2, 2, 2, 2, 2 };
    // where the elite comes out: fractions of the drawing's bounds from its centre (y up); the first five re-measured on the 2026-11
    // sites redraw (the open drawing's bay / ring centre), again on the HD rebuild (sources_hd). The launch-strip sites' are the pad pixels of
    // Codex's masters (build_art.py: .5 across; .5 / .519 / .4375 / .514 down the 768 px cell): the dark bay the elite rises from; kept for
    // the 2026-11 docks redraw, whose bays (rig / icebreaker: the scripted iris at the pad) hold them (frost_redraw/check_atlases.py).
    public static readonly Vector2[] SitePads =
    {
        new Vector2(-.02f, 0f), new Vector2(0f, .11f), new Vector2(0f, 0f), new Vector2(0f, -.02f), new Vector2(-.05f, -.01f),
        new Vector2(0f, 0f), new Vector2(0f, -.0195f), new Vector2(0f, .0625f), new Vector2(0f, -.0143f),
    };

    // ---- the launch strips (the docks atlas): idle, blink, opening, open, open hold, closing ----
    public const string DocksAtlas = "docks";
    public const int LaunchCells = 6, LaunchIdle = 0, LaunchBlink = 1, LaunchOpening = 2, LaunchOpen = 3, LaunchHold = 4, LaunchClosing = 5;
    public const int FirstLaunchSite = 5;                      // SiteNames[FirstLaunchSite..] have a strip
    // The rig and the icebreaker deck are drawn in a frozen lead: the glacier (v4) has no sea, so they never come there (nothing that floats
    // is drawn over its crevasses: FrostBackdropTest); its silo and cave, and the old five, do.
    // Also: the offshore rig, the icebreaker deck and the rig bay float on open water, so no dock is ever one of them (the elites that name
    // them fall back to the dock that is on screen).
    public static bool SiteFits(int kind, int variant) { return !DockGrade.Strict ? !(IsGlacier(variant) && (SiteKinds[kind] == LandingKind.Rig || SiteKinds[kind] == LandingKind.Icebreaker)) : SiteKinds[kind] != LandingKind.Rig && SiteKinds[kind] != LandingKind.Icebreaker && SiteKinds[kind] != LandingKind.RigBay; }
    public static bool HasLaunchStrip(int kind) { return kind >= FirstLaunchSite && kind < SiteNames.Length; }
    // drawn width of a strip site, as a share of the usual SiteMin..SiteMax (a 192 px wide icebreaker cell is 3/4 of a 256 px one)
    public static readonly float[] SiteWidthK = { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, .85f };
    // The tell: the site stays idle through its first share (the ship's own small blinking launch light, EliteShip.LaunchTell, is the
    // tell; the strip's lamp-blink cell 1 is never shown), then the doors open (2); the lift-off has them open (3 for its first share,
    // then 4 held until the elite is clear); then they close for LaunchCloseSeconds (5) and the site is idle again. No flash, flare,
    // halo or ring at any point (as SpaceDirector.DockCellAt after the launch-tell change).
    public const float LaunchBlinkShare = .4f, LaunchOpenShare = .25f, LaunchCloseSeconds = .6f;

    // The cell a strip site shows at EliteShip.LaunchPhase `ph` in (0, 2): (0, 1] the tell, [1, 2) the lift-off.
    public static int LaunchCellAt(float ph)
    {
        if (ph < 1f)
        {
            float t = ph * EliteShip.EngineTellSeconds;
            return t >= LaunchBlinkShare * EliteShip.EngineTellSeconds ? LaunchOpening : LaunchIdle;
        }
        return ph - 1f < LaunchOpenShare ? LaunchOpen : LaunchHold;
    }
    // the lamp row (lights_off / lights_on) under the door: cell point and scale (y 204: on the drawing in all five HD
    // site drawings; the rig bay and hatch end at y 217)
    public static Vector2 LampsAt = new Vector2(128f, 204f);
    public static float LampsScale = .6f;
    // the elite's size as it comes out of the pad: x the pad's drawn width (so it fits the bay), its floor x PieceScale (the
    // pads are drawn that much smaller from up here)
    public const float EmergeScalePerUnit = .22f, EmergeScaleMin = .26f * PieceScale, EmergeScaleMax = .4f;
    public const int SiteIdBase = 500;

    // The weather atlas bakes its translucency into the art (cloud banks
    // peak at alpha ~.28, blizzard sheets ~.19, snow ~.35), so the draw
    // alphas below multiply that: 1 draws the art as painted.
    // ---- CLOUD COVER: the knobs (CloudCover documents the timeline) ----
    // How much cloud drifts over the ground once the ceiling has cleared:
    // 1 = a light scattering (default), 2 = about twice as much, 0 = none.
    public static float CloudDensity = CloudCover.Density;
    public static float CeilingHold = CloudCover.CeilingHold;                   // s at full thickness after landing
    public static float CeilingClearSeconds = CloudCover.CeilingClearSeconds;   // s: the ceiling is gone
    public static float Wind = -1f;                // the level wind: -1 blows toward the left, with the blizzard
    // ---- the cloud ceiling ----
    public static float CeilingMin = 4.2f, CeilingMax = 5.6f;
    public static float CeilingAlpha = 1f;
    public static float CeilingGap = 1.5f;         // scrolled distance between new banks while it lasts
    public static float CeilingLowShare = .55f;    // alpha share left at the bottom of the view (the ceiling is above)
    // ---- air ----
    // wisps and mist at CloudDensity 1 (density divides the gaps, lifts the alpha)
    public static float WispMin = 2.2f, WispMax = 3.2f, WispAlphaMin = .45f, WispAlphaMax = .65f;
    public static float WispEveryMin = 6f, WispEveryMax = 10f, WispDrift = .18f;
    public static float MistAlphaMin = .35f, MistAlphaMax = .5f;
    public static float MistEveryMin = 14f, MistEveryMax = 22f, MistDrift = .08f;
    // snow fields at three depths: size, alpha, count, fall speed
    public static readonly float[] SnowSize = { 2.0f, 2.6f, 3.2f };
    public static readonly float[] SnowAlpha = { .6f, .65f, .45f };
    public static readonly int[] SnowCount = { 7, 5, 4 };
    public static readonly float[] SnowFall = { .25f, .45f, .8f };
    // ---- blizzard gusts ----
    public static float BlizzardFirst = 11f, BlizzardEveryMin = 13f, BlizzardEveryMax = 21f;
    public static int BlizzardSheets = 4;
    public static float BlizzardStagger = .45f;
    public static float BlizzardSpeed = 4.8f;
    public static float BlizzardMaxAlpha = 1f;
    // Optional: gust sheets add light (BackdropAdditive) instead of
    // alpha-blending (off: the brightness pass decides).
    public static bool BlizzardAdditive = false;
    public static float BlizzardMin = 5f, BlizzardMax = 7f;
    public static float BlizzardWind = 1.6f;       // sideways push given to the snow while a gust blows
}

public partial class FrostDirector : PlanetDirector
{
    BackdropPool ground, sites, auroraPool, mist, wisps, ceiling, blizzard;
    readonly BackdropPool[] snow = new BackdropPool[3];
    readonly SpriteRenderer[] lamps = new SpriteRenderer[3];
    BackdropAtlas landmarkArt, siteArt, dockArt, weather;
    Color[] dockTints;                                 // per site kind: its DockGrade colour on this ground set
    Sprite[][] siteLaunch;                             // per site kind: its 6 launch cells (null: an old two-drawing site, or the atlas is missing)
    AmbientEmitters ambient;
    FrostGroundLights groundLights;                    // the tile's own painted lamps, glowing / blinking (FrostGroundLights.cs)
    Sprite[] auroraFrames;
    Sprite[] siteClosed, siteOpen, banks, wispArt, mistArt, gustArt, snowArt;
    Sprite lampsOff, lampsOn;
    Color tintGround, tintAurora, tintMist, tintWisps, tintCeiling, tintBlizzard;
    readonly Color[] tintSnow = new Color[3];
    float auroraAlpha;

    float groundTravel, groundGap, siteTravel, siteGap, ceilingTravel;
    int lastFamily = -1;
    GroundPlanner planner;
    // the rule each pooled piece was placed by (index = its pool slot), so a
    // ship under way can check the ground ahead without a name lookup
    PieceRule[] groundRules, siteRules;
    float mistIn = 2.5f, wispIn = 6f;
    readonly Timer auroraTimer = new Timer(10f, 18f, 1.5f);
    readonly Timer gustTimer = new Timer(FrostTuning.BlizzardEveryMin, FrostTuning.BlizzardEveryMax, FrostTuning.BlizzardFirst);
    int sheetsLeft;
    float sheetIn, wind;
    Material additive;
    readonly List<Material> grades = new List<Material>();

    public override void Teardown()
    {
        BackdropAtlas.Kill(additive);
        additive = null;
        foreach (var m in grades) BackdropAtlas.Kill(m);
        grades.Clear();
        if (ambient != null) ambient.Destroy();
        if (groundLights != null) groundLights.Destroy();
        TeardownProps();
    }

    // Draws `pool` brightened by its layer's share of FrostTuning.Brightness
    // (BackdropGrade), its alpha thickened by `alphaLift`.
    Material Grade(BackdropPool pool, string layer, float alphaLift = 1f, float satScale = 1f)
    {
        float lift = BackdropGrade.Lift(set.Spec, set.Spec.Find(layer));
        var m = BackdropGrade.Create(pool.name, lift, BackdropGrade.Saturation(set.Spec, lift) * satScale, alphaLift);
        if (m == null) return null;
        grades.Add(m);
        foreach (var p in pool.items)
        {
            p.sr.sharedMaterial = m;
            if (p.blend != null) p.blend.sharedMaterial = m;
        }
        return m;
    }

    public Material CeilingMaterial { get; private set; }

    // Tests: > 0 seeds the next directors' streams (statistics over many runs).
    public static int SeedOverride;
    public FrostDirector() : base(SeedOverride > 0 ? SeedOverride : 1989)
    {
        groundRng = new System.Random((SeedOverride > 0 ? SeedOverride : 1989) * 31 + 7);
    }

    // The ground plane (landmarks, sites) draws from its own stream, so how
    // many tries the placement takes never shifts the weather (ceiling banks,
    // wisps, gusts) and the reverse.
    readonly System.Random groundRng;
    float GRand(float a, float b) { return a + (float)groundRng.NextDouble() * (b - a); }
    bool GChance(double p) { return groundRng.NextDouble() < p; }
    T GPick<T>(T[] options) { return options[groundRng.Next(options.Length)]; }

    public float Clock => clock;
    public int Gusts { get; private set; }
    public int SitesSpawned { get; private set; }
    public AmbientEmitters Ambient => ambient;
    public FrostGroundLights GroundLights => groundLights;
    public BackdropPool Ground => ground;
    public BackdropPool Sites => sites;
    public GroundPlanner Planner => planner;
    public BackdropPool Ceiling => ceiling;
    public BackdropPool Blizzard => blizzard;
    public BackdropPool Aurora => auroraPool;

    // 1 while the ceiling is thick, falling fast to 0 at CeilingClearSeconds (CloudCover.Ceiling).
    public static float CeilingDensity(float t) { return BackdropCatalog.For("Frost").CeilingDensity(t); }

    // The share of the view the ceiling's banks cover, alpha-weighted (0..1).
    public float CeilingCover
    {
        get
        {
            float sum = 0f;
            foreach (var p in ceiling.items)
            {
                if (!p.active || !p.sr.enabled) continue;
                Bounds b = p.sr.bounds;
                b.center -= set.Root.position;
                float w = Mathf.Min(b.max.x, HalfW) - Mathf.Max(b.min.x, -HalfW);
                float h = Mathf.Min(b.max.y, HalfH) - Mathf.Max(b.min.y, -HalfH);
                if (w <= 0f || h <= 0f) continue;
                sum += w * h * p.sr.color.a;
            }
            return Mathf.Clamp01(sum / (4f * HalfW * HalfH));
        }
    }

    // The mean draw alpha of the banks in view (the thick ceiling covers
    // the whole view, so CeilingCover saturates at 1 until it thins).
    public float CeilingBankAlpha
    {
        get
        {
            float sum = 0f; int n = 0;
            foreach (var p in ceiling.items)
                if (p.active && p.sr.enabled && Mathf.Abs(p.y) < HalfH + p.size * .5f) { sum += p.sr.color.a; n++; }
            return n > 0 ? sum / n : 0f;
        }
    }

    protected override void Build()
    {
        landmarkArt = set.Atlas("landmarks");
        siteArt = set.Atlas("sites");
        planner = new GroundPlanner(set, "mid", GroundMask.Load("Frost", set.Variant));
        weather = set.Atlas("weather");
        ambient = new AmbientEmitters(set);
        groundLights = new FrostGroundLights(set, planner.Tile, set.Variant, set.Spec.Order("flow") + 1);
        var spec = set.Spec;
        tintGround = spec.Tint("landmarks");
        tintAurora = spec.Tint("aurora");
        tintMist = spec.Tint("mist");
        tintWisps = spec.Tint("wisps");
        tintCeiling = spec.Tint("ceiling");
        tintBlizzard = spec.Tint("blizzard");

        dockArt = set.Atlas(FrostTuning.DocksAtlas);
        siteClosed = Sprites(siteArt, FrostTuning.SiteClosed);
        dockTints = DockGrade.Tints("Frost", set.Variant, FrostTuning.SiteClosed);
        siteOpen = Sprites(siteArt, FrostTuning.SiteOpen);
        // the launch-strip sites take their idle / open cells (and all six) from the docks atlas; without it they are simply never spawned
        siteLaunch = new Sprite[FrostTuning.SiteNames.Length][];
        for (int k = FrostTuning.FirstLaunchSite; k < siteLaunch.Length; k++)
        {
            var f = dockArt.Frames(FrostTuning.SiteNames[k]);
            if (f.Length < FrostTuning.LaunchCells) { siteClosed[k] = siteOpen[k] = null; continue; }
            siteLaunch[k] = f;
            siteClosed[k] = f[FrostTuning.LaunchIdle];
            siteOpen[k] = f[FrostTuning.LaunchOpen];
        }
        lampsOff = siteArt.Get("lights_off");
        lampsOn = siteArt.Get("lights_on");
        banks = weather.Frames("cloud_bank");
        wispArt = weather.Frames("cloud_wisp");
        mistArt = weather.Frames("mist");
        gustArt = weather.Frames("blizzard");
        snowArt = weather.Frames("snow");
        auroraFrames = ambient.Frames(FrostAmbientCatalog.AuroraLoop);
        int v = Mathf.Clamp(set.Variant, 0, FrostAmbientCatalog.AuroraAlpha.Length - 1);
        auroraAlpha = FrostAmbientCatalog.AuroraAlpha[v];

        auroraPool = Pool("aurora", 2);
        ground = LandmarkPool("landmarks", FrostTuning.GroundPool);
        sites = LandmarkPool("landmarks", 3, 3);
        groundRules = new PieceRule[ground.items.Count];
        siteRules = new PieceRule[sites.items.Count];
        // loops above both pools' plates (the sites' sit 3 above the ground's): a
        // site plate must not slice a neighbouring stack's plume off flat
        ambient.Rig(ground, 4);
        ambient.Rig(sites, 1);
        for (int i = 0; i < sites.items.Count; i++)
        {
            var go = new GameObject("lamps");
            go.transform.SetParent(sites.items[i].body, false);
            lamps[i] = go.AddComponent<SpriteRenderer>();
            lamps[i].sortingOrder = sites.items[i].sr.sortingOrder + 1;
            lamps[i].sprite = lampsOff;
            lamps[i].enabled = false;
            const float ppu = BackdropAtlas.PixelsPerUnit;
            // the lamp row's own centre sits ~150 px down its cell
            float k = FrostTuning.LampsScale;
            go.transform.localPosition = new Vector3((FrostTuning.LampsAt.x - 128f) / ppu,
                                                     (128f - FrostTuning.LampsAt.y) / ppu - (128f - 150f) / ppu * k, 0f);
            go.transform.localScale = new Vector3(k, k, 1f);
        }
        mist = Pool("mist", 3);
        wisps = Pool("wisps", 4);
        ceiling = Pool("ceiling", 26);
        string[] snowLayers = { "snow_far", "snow_mid", "snow_near" };
        for (int d = 0; d < 3; d++)
        {
            snow[d] = Pool(snowLayers[d], FrostTuning.SnowCount[d]);
            tintSnow[d] = spec.Tint(snowLayers[d]);
        }
        blizzard = Pool("blizzard", FrostTuning.BlizzardSheets + 1);
        Grade(auroraPool, "aurora");
        Grade(ground, "landmarks");
        Grade(sites, "landmarks");
        Grade(mist, "mist");
        Grade(wisps, "wisps");
        CeilingMaterial = Grade(ceiling, "ceiling", FrostTuning.CeilingThicken, FrostTuning.CeilingSaturation);
        if (!FrostTuning.BlizzardAdditive) Grade(blizzard, "blizzard");
        BuildProps();     // the living props (FrostProps.cs): whales, orcas, an icebreaker, a station, a lab, a crashed plane
        if (FrostTuning.BlizzardAdditive)
        {
            var shader = Resources.Load<Shader>(SpaceAsteroidDrift.AdditiveShader);
            if (shader != null)
            {
                additive = new Material(shader) { name = "FrostGust" };
                foreach (var g in blizzard.items) g.sr.sharedMaterial = additive;
            }
        }

        // the opening view: ground already in place, under a thick ceiling
        groundGap = GRand(FrostTuning.GapMin, FrostTuning.GapMax);
        siteGap = FrostTuning.SiteFirst;
        if (planner.Ready)
        {
            double t = planner.Travel;
            OpeningProp();
            for (float y = -HalfH * .85f; y < HalfH * .8f; y += GRand(FrostTuning.GapMin, FrostTuning.GapMax))
                SpawnLandmark(t + y, t + y + FrostTuning.SpawnBand);
        }
        BuildCeiling();
        BuildSnow();
        SpawnMist(Rand(-HalfH * .2f, HalfH * .4f));
    }

    static Sprite[] Sprites(BackdropAtlas atlas, string[] names)
    {
        var s = new Sprite[names.Length];
        for (int i = 0; i < names.Length; i++) s[i] = atlas.Get(names[i]);
        return s;
    }

    protected override void Step(float dt, float v)
    {
        float groundRate = set.Spec.Rate("landmarks");
        float scrolled = groundRate * v * dt;
        groundTravel += scrolled;
        siteTravel += scrolled;
        planner.Prune();
        if (planner.Ready && groundTravel >= groundGap)
        {
            if (SpawnGround()) { groundTravel = 0f; groundGap = GRand(FrostTuning.GapMin, FrostTuning.GapMax); }
            else groundGap = groundTravel + FrostTuning.SpawnRetry;     // nothing fitted: look again a little further on
        }
        ceilingTravel += set.Spec.Rate("ceiling") * v * dt;
        float density = set.Spec.CeilingDensity(clock);
        if (ceilingTravel >= FrostTuning.CeilingGap && density > CloudCover.BankSpawnFloor)
        {
            ceilingTravel = 0f;
            SpawnBank(Rand(-HalfW * .8f, HalfW * .8f), float.NaN);
        }
        float air = set.Spec.CloudDensity();
        if (CloudCover.Tick(ref mistIn, dt, air, FrostTuning.MistEveryMin, FrostTuning.MistEveryMax, rng)) SpawnMist(float.NaN);
        if (CloudCover.Tick(ref wispIn, dt, air, FrostTuning.WispEveryMin, FrostTuning.WispEveryMax, rng)) SpawnWisp();
        if (auroraTimer.Tick(dt, rng)) SpawnAurora();
        if (gustTimer.Tick(dt, rng)) { sheetsLeft = FrostTuning.BlizzardSheets; sheetIn = 0f; Gusts++; }
        if (sheetsLeft > 0)
        {
            sheetIn -= dt;
            if (sheetIn <= 0f) { SpawnSheet(); sheetsLeft--; sheetIn = FrostTuning.BlizzardStagger; }
        }

        foreach (var a in auroraPool.items)
        {
            if (!a.active || !Drift(a, dt, v)) continue;
            a.Animate();
            float swell = .75f + .25f * Mathf.Sin(a.age * .5f + a.phase);
            Color c = tintAurora;
            c.a *= auroraAlpha * swell * set.Alpha * FrostAmbientCatalog.Brightness;
            a.sr.color = c;
        }
        StepPinned(ground, groundRules, dt);
        StepPinned(sites, siteRules, dt);
        StepProps(dt, scrolled);
        StepSites(dt);
        ambient.Step(1f);
        groundLights.Step(dt);

        foreach (var m in mist.items)
            if (m.active && Drift(m, dt, v)) Tinted(m, tintMist, m.phase);
        foreach (var w in wisps.items)
            if (w.active && Drift(w, dt, v)) Tinted(w, tintWisps, w.phase);
        foreach (var b in ceiling.items)
        {
            if (!b.active) continue;
            if (density <= 0f) { Despawn(b); continue; }
            b.x += Mathf.Sign(b.x == 0f ? 1f : b.x) * CloudCover.PartSpeed * (1f - density) * dt;    // parting from the middle
            if (!Drift(b, dt, v)) continue;
            float y01 = Mathf.Clamp01((b.y / HalfH + 1f) * .5f);
            float low = Mathf.Lerp(FrostTuning.CeilingLowShare, 1f, y01 * y01 * (3f - 2f * y01));
            Tinted(b, tintCeiling, b.phase * density * low);
        }

        wind = Mathf.MoveTowards(wind, 0f, dt * .8f);
        foreach (var g in blizzard.items)
        {
            if (!g.active || !Drift(g, dt, v)) continue;
            float k = Mathf.Clamp01(g.age / Mathf.Max(.01f, g.life));
            if (k >= 1f) { Despawn(g); continue; }
            float env = Mathf.Sin(k * Mathf.PI);
            wind = Mathf.Max(wind, env);
            Tinted(g, tintBlizzard, FrostTuning.BlizzardMaxAlpha * env);
        }
        for (int d = 0; d < 3; d++)
            foreach (var s in snow[d].items)
            {
                StepFlakes(s, d, dt, v);
                Tinted(s, tintSnow[d], FrostTuning.SnowAlpha[d]);
            }
    }

    void Tinted(BackdropPiece p, Color tint, float alpha)
    {
        tint.r *= p.dockTint.r; tint.g *= p.dockTint.g; tint.b *= p.dockTint.b;
        tint.a *= alpha * set.Alpha;
        p.sr.color = tint;
    }

    // Pinned pieces: placed from their ground coordinate every frame, so they
    // stay on their spot of the mid tile: nothing here has a velocity of its
    // own, the ships and the convoy included (BackdropAnchorTest holds every piece to it).
    void StepPinned(BackdropPool pool, PieceRule[] rules, float dt)
    {
        for (int i = 0; i < pool.items.Count; i++)
        {
            var p = pool.items[i];
            if (!p.active) continue;
            p.age += dt;
            p.y = planner.Y(p.gy);
            Place(p);
            if (p.y < GroundDespawnY(p, ambient))
            {
                planner.Release(p.footprint);
                p.footprint = -1;
                Despawn(p);
                continue;
            }
            Tinted(p, tintGround, 1f);
        }
    }

    // ------------------------------------------------------------ ground --

    float XMin(float size) { return -EdgeX + size * .3f; }
    float XMax(float size) { return EdgeX - size * .3f; }

    // A landmark (or, when one is due, a launch site) entering at the top.
    bool SpawnGround()
    {
        double lo = planner.Travel + HalfH + FrostTuning.SpawnAbove, hi = lo + FrostTuning.SpawnBand;
        if (siteTravel >= siteGap && SpawnSite(float.NaN))
        {
            siteTravel = 0f;
            siteGap = GRand(FrostTuning.SiteGapMin, FrostTuning.SiteGapMax);
            return true;
        }
        return SpawnLandmark(lo, hi);
    }

    int PickFamily(int not)
    {
        var weights = FrostTuning.FamilyWeights[Mathf.Clamp(set.Variant, 0, FrostTuning.FamilyWeights.Length - 1)];
        int total = 0;
        for (int i = 0; i < weights.Length; i++) if (i != lastFamily && i != not) total += weights[i];
        if (total <= 0) return -1;
        int r = groundRng.Next(total);
        for (int i = 0; i < weights.Length; i++)
        {
            if (i == lastFamily || i == not || weights[i] <= 0) continue;
            r -= weights[i];
            if (r < 0) return i;
        }
        return -1;
    }

    // A landmark with its centre in the ground band [lo, hi], where the
    // ground under it suits its family (FrostTuning.Rule). A family that
    // fits nowhere in the band is swapped for another (up to three).
    // False when nothing fitted.
    bool SpawnLandmark(double lo, double hi)
    {
        int tried = -1;
        for (int k = 0; k < 3; k++)
        {
            int fam = PickFamily(tried);
            if (fam < 0) return false;
            tried = fam;
            string name = GPick(FrostTuning.Families[fam]);
            float size = GRand(FrostTuning.LandmarkMin, FrostTuning.LandmarkMax);
            var rule = FrostTuning.Rule(name, set.Variant);
            bool mirror = !rule.HasEnds && GChance(.5);
            float x; double gy;
            if (!Spot(rule, size, lo, hi, out x, out gy)) continue;
            var p = Put(ground, groundRules, landmarkArt.Get(name), rule, size, x, gy, mirror);
            if (p == null) return false;
            lastFamily = fam;
            p.kind = fam;
            // The icebreaker and the convoy are PINNED like every other landmark: they used to creep over the ice on a velocity of
            // their own (up to 0.3 u in 6 s), which read as a vehicle sliding over the ground it stands on.
            ambient.Attach(p, name, groundRng);
            return true;
        }
        return false;
    }

    // A spot for `rule` in the band (a causeway's rule also wants both
    // landfalls on land: PieceRule ends, checked by the planner). 48 tries for
    // every piece: a whole slab on land is rarer than the old shares were.
    bool Spot(PieceRule rule, float size, double lo, double hi, out float x, out double gy)
    {
        return planner.Find(rule, size, XMin(size), XMax(size), lo, hi, groundRng, out x, out gy, 48);
    }

    // Draws `sprite` at x, gy (pinned), claiming its footprint.
    BackdropPiece Put(BackdropPool pool, PieceRule[] rules, Sprite sprite, PieceRule rule, float size, float x, double gy, bool mirror)
    {
        if (sprite == null) return null;
        float rx = GroundPlanner.HalfX(rule, size), ry = GroundPlanner.HalfY(rule, size);
        int foot = planner.Claim(x, gy, rx, ry);
        if (foot < 0) return null;
        var p = pool.Spawn();
        if (p == null) { planner.Release(foot); return null; }
        int i = pool.items.IndexOf(p);
        if (i >= 0) rules[i] = rule;
        SetSprite(p, sprite, Mathf.Min(size, BackdropCatalog.MaxLandmarkSize));
        p.body.localScale = mirror ? new Vector3(-1f, 1f, 1f) : Vector3.one;
        p.footprint = foot;
        p.core = rule.core;
        p.gy = gy;
        p.x = x;
        p.y = planner.Y(gy);
        p.vx = p.vy = 0f;
        p.rate = set.Spec.Rate("landmarks");
        p.color = tintGround;
        p.dockTint = Color.white;
        p.kind = 0;
        Place(p);
        Tinted(p, tintGround, 1f);
        return p;
    }

    // A launch site entering at the top (or at screen `y`), where its rule
    // fits (relaxed to "not on a painted quay", then anywhere free, so the
    // elite still gets its site). False when the pool is full, the art is
    // missing or nothing is free.
    public bool SpawnSite(float y, int kind = -1)
    {
        if (!planner.Ready) return false;
        if (DockGrade.AnyOnGround(sites, HalfH)) return false;      // one dock at a time: the next comes once this one has scrolled off
        if (kind < 0)
        {
            int total = 0;
            for (int k = 0; k < FrostTuning.SiteWeights.Length; k++) if (FrostTuning.SiteFits(k, set.Variant)) total += FrostTuning.SiteWeights[k];
            int r = groundRng.Next(total);
            for (kind = 0; kind < FrostTuning.SiteWeights.Length - 1; kind++)
            {
                if (!FrostTuning.SiteFits(kind, set.Variant)) continue;
                r -= FrostTuning.SiteWeights[kind];
                if (r < 0) break;
            }
        }
        if (!FrostTuning.SiteFits(kind, set.Variant)) return false;
        var closed = siteClosed[kind];
        if (closed == null || siteOpen[kind] == null) return false;
        float size = GRand(FrostTuning.SiteMin, FrostTuning.SiteMax) * FrostTuning.SiteWidthK[kind];
        double lo = float.IsNaN(y) ? planner.Travel + HalfH + FrostTuning.SpawnAbove : planner.GroundY(y) - .3f;
        double hi = lo + (float.IsNaN(y) ? FrostTuning.SpawnBand : .6f);
        var rule = DockGrade.Strict ? FrostTuning.SiteRule(set.Variant, false) : FrostTuning.DockRule(kind, set.Variant);
        float x; double gy;
        if (DockGrade.Strict)
        {
            // a flat top with its rim clear, else with the rim a little broken: random tries first, then a scan of a taller band
            // (flat tops big enough for a pad are few: a site the elite director asks for must not be missed by bad luck)
            var relaxed = FrostTuning.SiteRule(set.Variant, true);
            double far = lo + (float.IsNaN(y) ? FrostTuning.SiteScanBand : .6f);
            if (!planner.Find(rule, size, XMin(size), XMax(size), lo, hi, groundRng, out x, out gy, FrostTuning.SiteTries) &&
                !planner.Find(rule = relaxed, size, XMin(size), XMax(size), lo, hi, groundRng, out x, out gy, FrostTuning.SiteTries) &&
                !planner.Scan(rule = FrostTuning.SiteRule(set.Variant, false), size, XMin(size), XMax(size), lo, far, FrostTuning.SiteScanStep, groundRng, out x, out gy) &&
                !planner.Scan(rule = relaxed, size, XMin(size), XMax(size), lo, far, FrostTuning.SiteScanStep, groundRng, out x, out gy))
                return false;
        }
        else if (!planner.Find(rule, size, XMin(size), XMax(size), lo, hi, groundRng, out x, out gy) &&
                 !planner.Find(rule = FrostTuning.SiteRelaxedOld, size, XMin(size), XMax(size), lo, hi, groundRng, out x, out gy) &&
                 !planner.Find(rule = new PieceRule(GroundClass.Any, 0f, FrostTuning.SiteRelaxed.core), size, XMin(size), XMax(size), lo, hi, groundRng, out x, out gy))
            return false;
        var p = Put(sites, siteRules, closed, rule, size, x, gy, false);     // doors face the viewer: never mirrored
        if (p == null) return false;
        p.dockTint = dockTints[kind];
        Tinted(p, tintGround, 1f);
        p.kind = kind;
        p.tier = 0;
        p.life = 0f;
        ambient.Attach(p, SiteKey(kind), groundRng);
        int i = sites.items.IndexOf(p);
        if (i >= 0 && lamps[i] != null) { lamps[i].sprite = lampsOff; lamps[i].enabled = lampsOff != null && siteLaunch[kind] == null; }   // (a strip site has its own lamp drawn in)
        SitesSpawned++;
        return true;
    }

    static string SiteKey(int kind) { return FrostTuning.SiteClosed[kind]; }

    // The elite (if any) launching from site piece `p`: 2 lifting off, 1 in
    // its launch tell, 0 parked quietly / none.
    static int LaunchState(BackdropPiece p)
    {
        var live = EliteShip.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var e = live[i];
            if (e == null || e.Site.anchor != p.root) continue;
            if (e.State == EliteState.LiftOff) return 2;
            if (e.State == EliteState.Parked && e.StateTime >= e.ParkSeconds - EliteShip.EngineTellSeconds) return 1;
        }
        return 0;
    }

    // The launch phase of the elite (if any) parked in / leaving site piece `p` (EliteShip.LaunchPhase), -1 with none.
    static float PhaseOf(BackdropPiece p)
    {
        var live = EliteShip.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var e = live[i];
            if (e != null && e.State != EliteState.Dead && e.Site.anchor == p.root) return e.LaunchPhase;
        }
        return -1f;
    }

    // The cell of a strip site: the launch tell and lift-off by the elite's phase, then closing
    // behind it for the last LaunchCloseSeconds of the close delay (p.tier 3, p.life counting down), held open before that.
    int LaunchCell(BackdropPiece p)
    {
        if (p.tier == 1 || p.tier == 2)
        {
            float ph = PhaseOf(p);
            if (ph > 0f && ph < 2f) return FrostTuning.LaunchCellAt(ph);
            return p.tier == 1 ? FrostTuning.LaunchOpening : FrostTuning.LaunchHold;
        }
        if (p.tier == 3) return p.life > FrostTuning.LaunchCloseSeconds ? FrostTuning.LaunchHold : FrostTuning.LaunchClosing;
        return FrostTuning.LaunchIdle;
    }

    void StepSites(float dt)
    {
        for (int i = 0; i < sites.items.Count; i++)
        {
            var p = sites.items[i];
            if (!p.active) continue;
            int launch = LaunchState(p);
            if (launch > 0) { p.tier = launch; p.life = FrostTuning.SiteCloseDelay; }
            else if (p.tier > 0)
            {
                p.life -= dt;
                if (p.life <= 0f) p.tier = 0;
                else p.tier = 3;         // closing behind it
            }
            bool open = p.tier > 0;
            var strip = siteLaunch[p.kind];
            var want = strip != null ? strip[LaunchCell(p)] : open ? siteOpen[p.kind] : siteClosed[p.kind];
            if (p.sr.sprite != want) p.sr.sprite = want;
            var lamp = lamps[i];
            if (strip != null) { if (lamp != null) lamp.enabled = false; continue; }
            if (lamp != null && lampsOn != null)
            {
                bool lit = p.tier == 2 || p.tier == 3 || (p.tier == 1 && Mathf.Repeat(p.age * 4f, 1f) < .55f);
                lamp.sprite = lit ? lampsOn : lampsOff;
                Color c = tintGround;
                c.a *= set.Alpha;
                lamp.color = c;
            }
        }
    }

    public bool SiteOpen(BackdropPiece p) { return p.tier > 0; }

    // Launch sites for the elites: the sites still in the upper part of the
    // view (a launch has time to be seen), inside the view sideways, drawn
    // big enough. The elite comes out of the door (emerge).
    public override void LandingSites(List<LandingSite> into)
    {
        if (sites == null) return;
        for (int i = 0; i < sites.items.Count; i++)
        {
            var p = sites.items[i];
            if (!p.active || p.sr.sprite == null) continue;
            if (p.size < FrostTuning.SiteMinSize) continue;
            if (p.y < -HalfH * .15f || p.y > HalfH - p.size * .3f) continue;
            if (Mathf.Abs(p.x) > HalfW - .25f) continue;
            Bounds b = p.sr.sprite.bounds;
            Vector2 pad = FrostTuning.SitePads[p.kind];
            into.Add(new LandingSite
            {
                anchor = p.root,
                local = new Vector3(b.center.x + pad.x * b.size.x, b.center.y + pad.y * b.size.y, 0f),
                scale = Mathf.Clamp(p.size * FrostTuning.EmergeScalePerUnit, FrostTuning.EmergeScaleMin, FrostTuning.EmergeScaleMax),
                order = p.sr.sortingOrder + 2,
                id = FrostTuning.SiteIdBase + i,
                kind = FrostTuning.SiteKinds[p.kind],
                emerge = true,
            });
        }
    }

    // The elite director wants a site of one of `launchFrom`'s kinds in view soon (LandingSites.BringHome): the ground is fixed, so
    // none is conjured -- but the site spawner is asked for one of that kind at the top of the view now (it scrolls in over the next
    // seconds, as the ground pieces always do), unless one of them is already in view or on its way. False when none of the kinds is
    // one of Frost's sites or there is no room for one on the ground.
    public override bool BringHome(string launchFrom)
    {
        if (sites == null || !planner.Ready || string.IsNullOrEmpty(launchFrom)) return false;
        var want = new List<int>(FrostTuning.SiteKinds.Length);   // (a rare call: one per elite)
        bool mine = false;      // (one of the kinds it names is a dock of this world, even if none can be placed here)
        foreach (string part in launchFrom.Split(','))
        {
            var k = LandingSite.KindOf(part.Trim());
            if (k == null) continue;
            for (int t = 0; t < FrostTuning.SiteKinds.Length; t++)
            {
                if (FrostTuning.SiteKinds[t] == k.Value) mine = true;
                if (FrostTuning.SiteKinds[t] == k.Value && siteClosed[t] != null && siteOpen[t] != null && FrostTuning.SiteFits(t, set.Variant) && !want.Contains(t)) want.Add(t);
            }
        }
        if (want.Count == 0 && mine && DockGrade.Strict)     // (none of the kinds it names has a dock here: any dock that stands on solid ground will do)
            for (int t = 0; t < FrostTuning.SiteKinds.Length; t++)
                if (siteClosed[t] != null && siteOpen[t] != null && FrostTuning.SiteFits(t, set.Variant)) want.Add(t);
        if (want.Count == 0) return false;
        for (int i = 0; i < sites.items.Count; i++)
        {
            var p = sites.items[i];
            if (p.active && (DockGrade.Strict || want.Contains(p.kind)) && p.y >= -HalfH * .15f) return true;   // (a dock in view, or still on its way in from the top: the one dock)
        }
        return SpawnSite(float.NaN, want[groundRng.Next(want.Count)]);
    }

    // ----------------------------------------------------------- weather --

    void BuildCeiling()
    {
        if (banks.Length == 0) return;
        // staggered rows over the whole view (thinner low down,
        // CeilingLowShare): the deck the planetfall has just dropped through
        float[] rows = { 1.05f, .8f, .55f, .3f, .05f, -.25f, -.55f };
        for (int r = 0; r < rows.Length; r++)
            for (int c = 0; c < 3; c++)
                SpawnBank(HalfW * (-.62f + .62f * c + Rand(-.12f, .12f) + (r % 2 == 0 ? 0f : .2f)), HalfH * rows[r] + Rand(-.3f, .3f));
    }

    void SpawnBank(float x, float y)
    {
        if (banks.Length == 0) return;
        var b = ceiling.Spawn();
        if (b == null) return;
        SetSprite(b, Pick(banks), Rand(FrostTuning.CeilingMin, FrostTuning.CeilingMax));
        if (Chance(.5)) b.body.localScale = new Vector3(-1f, 1f, 1f);
        b.x = x;
        b.y = float.IsNaN(y) ? HalfH + b.size * .35f : y;
        // drifting apart: the ceiling opens from the middle
        b.vx = Mathf.Sign(x == 0f ? 1f : x) * Rand(.04f, .14f);
        b.vy = Rand(.0f, .15f);
        b.rate = set.Spec.Rate("ceiling");
        b.phase = FrostTuning.CeilingAlpha * Rand(.8f, 1f);
        Place(b);
        Tinted(b, tintCeiling, 0f);
    }

    void SpawnWisp()
    {
        if (wispArt.Length == 0) return;
        var w = wisps.Spawn();
        if (w == null) return;
        SetSprite(w, Pick(wispArt), Rand(FrostTuning.WispMin, FrostTuning.WispMax));
        if (Chance(.5)) w.body.localScale = new Vector3(-1f, 1f, 1f);
        w.x = Rand(-HalfW * .7f, HalfW * .7f);
        w.y = SpawnY(w.size);
        w.vx = FrostTuning.Wind * FrostTuning.WispDrift * Rand(.7f, 1.3f);
        w.rate = set.Spec.Rate("wisps");
        w.phase = Rand(FrostTuning.WispAlphaMin, FrostTuning.WispAlphaMax) * CloudCover.Alpha(set.Spec.CloudDensity());
        Place(w);
        Tinted(w, tintWisps, 0f);
    }

    void SpawnMist(float y)
    {
        if (mistArt.Length == 0) return;
        var m = mist.Spawn();
        if (m == null) return;
        SetSprite(m, Pick(mistArt), HalfW * 2f * Rand(.85f, 1.1f));
        if (Chance(.5)) m.body.localScale = new Vector3(-1f, 1f, 1f);
        m.x = Rand(-HalfW * .3f, HalfW * .3f);
        m.y = float.IsNaN(y) ? SpawnY(m.size * .5f) : y;
        m.vx = FrostTuning.Wind * FrostTuning.MistDrift * Rand(.6f, 1.4f);
        m.rate = set.Spec.Rate("mist");
        m.phase = Rand(FrostTuning.MistAlphaMin, FrostTuning.MistAlphaMax) * CloudCover.Alpha(set.Spec.CloudDensity());
        Place(m);
        Tinted(m, tintMist, 0f);
    }

    void SpawnAurora()
    {
        if (auroraFrames.Length == 0 || auroraAlpha <= 0f) return;
        var a = auroraPool.Spawn();
        if (a == null) return;
        a.frames = auroraFrames;
        a.fps = FrostAmbientCatalog.Loops[FrostAmbientCatalog.LoopIndex(FrostAmbientCatalog.AuroraLoop)].fps;
        SetSprite(a, auroraFrames[0], HalfW * 2f * Rand(1.1f, 1.4f));
        a.x = Rand(-.6f, .6f);
        a.y = SpawnY(a.size * .4f);
        a.rate = set.Spec.Rate("aurora");
        a.phase = Rand(0f, 6.28f);
        a.root.localRotation = Quaternion.Euler(0, 0, Rand(-14f, 14f));
        if (Chance(.5)) a.body.localScale = new Vector3(-1f, 1f, 1f);
        Place(a);
        a.sr.color = new Color(1f, 1f, 1f, 0f);
    }

    // One blizzard sheet: in from the upper right, sweeping down-left along
    // its streaks, faded in and out over its crossing.
    void SpawnSheet()
    {
        if (gustArt.Length == 0) return;
        var g = blizzard.Spawn();
        if (g == null) return;
        SetSprite(g, Pick(gustArt), Rand(FrostTuning.BlizzardMin, FrostTuning.BlizzardMax));
        g.x = HalfW + g.size * .25f;
        g.y = Rand(HalfH * .2f, HalfH * 1.0f);
        float speed = FrostTuning.BlizzardSpeed * Rand(.85f, 1.15f);
        g.vx = -speed;
        g.vy = -speed * .55f;
        g.life = (2f * HalfW + g.size * .6f) / speed;
        g.rate = set.Spec.Rate("blizzard");
        Place(g);
        Tinted(g, tintBlizzard, 0f);
    }

    public void ForceGust() { sheetsLeft = FrostTuning.BlizzardSheets; sheetIn = 0f; Gusts++; }

    void BuildSnow()
    {
        if (snowArt.Length == 0) return;
        string[] layers = { "snow_far", "snow_mid", "snow_near" };
        for (int d = 0; d < 3; d++)
            foreach (var p in snow[d].items)
            {
                p.Show(true);
                SetSprite(p, Pick(snowArt), FrostTuning.SnowSize[d] * Rand(.85f, 1.15f));
                if (Chance(.5)) p.body.localScale = new Vector3(-1f, 1f, 1f);
                p.x = Rand(-HalfW, HalfW);
                p.y = Rand(-HalfH, HalfH);
                p.phase = Rand(0f, 6.28f);
                p.rate = set.Spec.Rate(layers[d]);
                Place(p);
            }
    }

    void StepFlakes(BackdropPiece p, int depth, float dt, float v)
    {
        if (!p.active) return;
        p.age += dt;
        p.y += (-FrostTuning.SnowFall[depth] - p.rate * v) * dt;
        p.x += (Mathf.Sin(p.age * .9f + p.phase) * .25f - wind * FrostTuning.BlizzardWind * (.5f + .5f * depth)) * dt;
        float my = HalfH + p.size * .55f, mx = HalfW + p.size * .55f;
        if (p.y < -my) { p.y += 2f * my; p.x = Rand(-HalfW, HalfW); }
        else if (p.y > my) p.y -= 2f * my;
        if (p.x < -mx) p.x += 2f * mx;
        else if (p.x > mx) p.x -= 2f * mx;
        Place(p);
    }
}
