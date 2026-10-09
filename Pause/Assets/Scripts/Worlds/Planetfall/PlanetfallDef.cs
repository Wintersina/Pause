using UnityEngine;

// What a planetfall needs to know about the planet it lands on: its art and
// the art's pixel numbers, its colours and its words. One entry per world
// that is arrived at by planetfall instead of through a portal
// (PlanetfallCatalog); everything else about the sequence is the same for
// every planet (Planetfall, PlanetfallTimeline).
//
// To give another world a planetfall: render its art into
// Art/Backgrounds/Resources/Worlds/<World>/Planetfall/ with the same file
// roles (PlanetfallArtImporter picks the folder up), measure the numbers
// below from its manifest, and add an entry to PlanetfallCatalog.Defs.
public class PlanetfallDef
{
    public int world;               // WorldManager.Worlds index it lands on
    public string folder;           // Resources path of the art, with trailing '/'

    // ---- files (inside `folder`) ----
    public string planet;           // the whole planet, seen from orbit
    public string limb;             // its curved horizon, close up
    public string deck;             // the bright cloud deck (tileable both ways)
    public string deckDark;         // the deep cloud deck (tileable both ways)
    public string entryFx;          // the plasma shroud, a looping strip
    public string burst;            // the cloud breakthrough, a one-shot strip
    public string streaks;          // speed lines (tileable vertically)

    // ---- the art's numbers, in its own pixels (y down from the top, as
    // the manifest and the generator measure them) ----
    public Vector2 planetCentrePx;  // the disc's centre
    public float planetDiscPx;      // the disc's radius (the surface, not the halo)
    public float limbApexPx;        // the horizon's top, at the image's centre column
    public float limbEdgePx;        // the horizon's height at the image's side edges
    public int entryFrames;         // cells in entryFx, laid out in one row
    public Vector2 entryShipPx;     // where the ship sits in an entryFx cell: the opening's centre
    public float entryHolePx;       // the opening's width (0: the shroud is drawn ShroudWidth wide)
    public float[] entryHoleX;      // per cell, the opening's centre x (it wanders); null: entryShipPx.x
    public int burstFrames;         // cells in burst, one row; the burst's centre is the cell's

    // ---- colours ----
    public Color cue;               // the "land here" glow and the reticle
    public Color heat;              // the entry tint (warm)
    public Color cold;              // the deep-cloud tint
    public Color flash;             // the breakthrough flash
    public Color shade;             // the vignette

    // ---- words ----
    public string openBanner;       // when the planet appears (the portal says PORTAL OPEN)
    public string urgeBanner;       // when the pressure starts (ENTER THE PORTAL)
    public string chipPrefix;       // the pressure chip (PORTAL  DANGER n)

    // The horizon's radius in limb pixels: the circle through the apex and
    // both edge points (half-chord = half the image width).
    public float LimbArcPx(float limbWidthPx)
    {
        float half = limbWidthPx * .5f;
        float drop = Mathf.Max(1f, limbEdgePx - limbApexPx);
        return (half * half + drop * drop) / (2f * drop);
    }
}

// Which world changes are flown as a planetfall. Only the way OUT to a new
// planet: the final world's loop back round, and every pair not listed,
// keep the portal.
public static class PlanetfallCatalog
{
    // Off: every world change uses the portal (developer / tests).
    public static bool Enabled = true;

    public static readonly PlanetfallDef Frost = new PlanetfallDef
    {
        world = 1,
        folder = "Worlds/Frost/Planetfall/",
        planet = "frost_planet",
        limb = "frost_planet_limb",
        deck = "frost_cloud_deck",
        deckDark = "frost_cloud_deck_dark",
        entryFx = "frost_entry_fx",
        burst = "frost_breakthrough",
        streaks = "frost_entry_streaks",
        // Measured on the globe itself (a circle fitted to its polar caps):
        // the manifest's 512,512 / 474 include the ring city and the halo.
        planetCentrePx = new Vector2(518f, 536f),
        planetDiscPx = 392f,
        limbApexPx = 355f,
        limbEdgePx = 722f,
        entryFrames = 6,
        // The opening, measured per cell (flood fill of the clear pixels
        // round the ship): 146 px wide, 165 tall, centre y 338.5; its centre
        // x wanders 251 .. 268 through the loop. The manifest's 256, 300 is
        // the opening's upper part, which put the ship's nose out of it.
        entryShipPx = new Vector2(256f, 338.5f),
        entryHolePx = 146f,
        entryHoleX = new[] { 251f, 251.5f, 259.5f, 268f, 267.5f, 258.5f },
        burstFrames = 5,
        cue = new Color(0.45f, 0.92f, 1f),
        heat = new Color(0.95f, 0.30f, 0.80f),
        cold = new Color(0.35f, 0.78f, 1f),
        flash = new Color(0.86f, 0.98f, 1f),
        shade = new Color(0.05f, 0.03f, 0.14f),
        openBanner = "LAND ON FROST",
        urgeBanner = "DIVE INTO FROST",
        chipPrefix = "ORBIT  DANGER ",
    };


    // Verdant: after Frost's lift-off, the green jungle world under its
    // orbital vine ring (Art/Worlds/Verdant/descent/src~/manifest.json,
    // re-measured on the pixels).
    public static readonly PlanetfallDef Verdant = new PlanetfallDef
    {
        world = 2,
        folder = "Worlds/Verdant/Planetfall/",
        planet = "verdant_planet",
        limb = "verdant_planet_limb",
        deck = "verdant_cloud_deck",
        deckDark = "verdant_cloud_deck_dark",
        entryFx = "verdant_entry_fx",
        burst = "verdant_breakthrough",
        streaks = "verdant_entry_streaks",
        // The globe is centred in its square (opaque from y 76 to 949, x 80
        // to 944); the radius is the middle of its lime atmosphere band (the
        // surface 424, the band's outer edge 438), where the limb's horizon
        // line (the middle of its own glow band at y 355) sits too. The vine
        // ring reaches past it and is clipped away as the limb takes over.
        planetCentrePx = new Vector2(512f, 512.5f),
        planetDiscPx = 430f,
        limbApexPx = 355f,
        limbEdgePx = 722f,
        entryFrames = 6,
        // The opening, measured per cell (flood fill of the clear pixels
        // round 256, 338): round, 167 tall (y 255 .. 421), widest 148 .. 154
        // px across its middle row, whose centre wanders 253 .. 259; the
        // lime-gold plasma is a thin ribbon trailing below it.
        entryShipPx = new Vector2(256f, 338f),
        entryHolePx = 150f,
        entryHoleX = new[] { 253.5f, 253f, 256f, 258.5f, 259f, 256f },
        burstFrames = 5,
        cue = new Color(0.70f, 1f, 0.36f),          // lime: the atmosphere band, the river light
        heat = new Color(0.86f, 0.90f, 0.22f),      // lime-gold plasma
        cold = new Color(0.22f, 0.80f, 0.58f),      // the emerald / teal canopy under the clouds
        flash = new Color(0.94f, 1f, 0.80f),        // lime-white
        shade = new Color(0.02f, 0.08f, 0.05f),     // deep jungle green
        openBanner = "LAND ON VERDANT",
        urgeBanner = "DIVE INTO VERDANT",
        chipPrefix = "ORBIT  DANGER ",
    };

    // Ember: after Verdant's lift-off, the amber volcanic forge world, its
    // lava-veined globe girdled by a ring of brass radiator panels
    // (Art/Worlds/Ember/descent~/manifest.json, re-measured on the pixels).
    public static readonly PlanetfallDef Ember = new PlanetfallDef
    {
        world = 3,
        folder = "Worlds/Ember/Planetfall/",
        planet = "ember_planet",
        limb = "ember_planet_limb",
        deck = "ember_cloud_deck",
        deckDark = "ember_cloud_deck_dark",
        entryFx = "ember_entry_fx",
        burst = "ember_breakthrough",
        streaks = "ember_entry_streaks",
        // A circle fitted to the globe's outer edge (360 rays, the brass
        // ring, the ash plumes and the ring's hanging pods rejected): centre
        // 505, 513, edge 404. The radius is the middle of its amber rim band
        // (glow peak ~388, edge 404), where the limb's horizon (the middle of
        // its own glow band: alpha from y 346, peak 353) sits too. The
        // manifest's 512, 512 / 376 is the inner basalt, inside the rim.
        planetCentrePx = new Vector2(505f, 513f),
        planetDiscPx = 396f,
        limbApexPx = 353f,
        limbEdgePx = 718f,
        entryFrames = 6,
        // The opening, measured per cell (flood fill of the clear pixels
        // round 256, 338): round, 168 tall (y 254 .. 421), 147 .. 149 px
        // across its middle row, centred 255 .. 256 (it barely wanders); the
        // amber-gold plasma is an S-shaped ribbon trailing below it.
        entryShipPx = new Vector2(256f, 337.5f),
        entryHolePx = 148f,
        entryHoleX = new[] { 255f, 256f, 256f, 256f, 256f, 255.5f },
        burstFrames = 5,
        cue = new Color(1f, 0.74f, 0.26f),          // amber-gold: the rim band, the lava light (hue ~37 deg)
        heat = new Color(1f, 0.64f, 0.20f),         // amber-gold plasma (hue ~33 deg)
        cold = new Color(0.66f, 0.50f, 0.40f),      // the ash deck under the plasma, warm charcoal
        flash = new Color(1f, 0.95f, 0.80f),        // white-gold
        shade = new Color(0.07f, 0.04f, 0.03f),     // ash-charcoal
        openBanner = "LAND ON EMBER",
        urgeBanner = "DIVE INTO EMBER",
        chipPrefix = "ORBIT  DANGER ",
    };

    // Tide: after Ember's lift-off (only once WorldManager.TideEnabled), the
    // ocean world: a blue-green marble under white storm bands, girdled by a
    // ring of rig platforms (Art/Worlds/Tide/descent~/manifest.json,
    // re-measured on the pixels with verify_planetfall_art.py).
    public static readonly PlanetfallDef Tide = new PlanetfallDef
    {
        world = 4,
        folder = "Worlds/Tide/Planetfall/",
        planet = "tide_planet",
        limb = "tide_planet_limb",
        deck = "tide_cloud_deck",
        deckDark = "tide_cloud_deck_dark",
        entryFx = "tide_entry_fx",
        burst = "tide_breakthrough",
        streaks = "tide_entry_streaks",
        // 360 rays from the manifest's centre, circle fitted to the globe's
        // outer edge (tether platforms and plumes rejected, 355 of 360 rays
        // used): centre 512.3, 515.2, edge 422.2 (the manifest's 423.5). The
        // radius is 8 px inside the edge, in the glow of the rim band (as
        // Ember's 396 inside its 404), where the limb's horizon sits too.
        // The limb's horizon is a hard edge: nothing above row 355 at the
        // centre column, 720 at both side columns (no soft glow band above it
        // as Ember's, so apex = the first opaque row).
        planetCentrePx = new Vector2(512.5f, 514.5f),
        planetDiscPx = 414f,
        limbApexPx = 355f,
        limbEdgePx = 720f,
        entryFrames = 6,
        // The opening, measured per cell (flood fill of the clear pixels at
        // y 338): 145 .. 149 px wide, 165 .. 169 tall, centre y 338, centre x
        // wandering 254 .. 259 (manifest hole_center_px_per_frame). White-hot
        // core, mint-white and blue edge flames, wide (not a ribbon).
        entryShipPx = new Vector2(256f, 338f),
        entryHolePx = 147f,
        entryHoleX = new[] { 256f, 258f, 254f, 259f, 255f, 257f },
        burstFrames = 5,
        cue = new Color(0.49f, 0.95f, 0.75f),       // bioluminescent mint #7CF2C0 (hue ~158 deg)
        heat = new Color(0.55f, 0.90f, 0.85f),      // mint-white plasma (core 0.83 .97 .91)
        cold = new Color(0.20f, 0.45f, 0.48f),      // the slate grey-teal storm cloud under the plasma
        flash = new Color(0.90f, 1f, 0.96f),        // white-mint spray
        shade = new Color(0.02f, 0.07f, 0.08f),     // abyssal blue-green black
        openBanner = "LAND ON TIDE",
        urgeBanner = "DIVE INTO TIDE",
        chipPrefix = "ORBIT  DANGER ",
    };

    // Every planet arrived at by planetfall (Frost from Space, Verdant after
    // Frost's lift-off, Ember after Verdant's, Tide after Ember's -- the last
    // only while WorldManager.TideEnabled: until then Ember is the last live
    // world, PlanetfallCatalog.For never gets asked for Tide). A new planet
    // is one more entry here; the tests swap the list. Space, the loop's way
    // back round, has none: the loop keeps the portal.
    public static readonly PlanetfallDef[] All = { Frost, Verdant, Ember, Tide };
    public static PlanetfallDef[] Defs = All;

    // The planetfall for leaving `from` for `to`, or null for a portal.
    // A loop (the way back round after the final world) is always a portal.
    public static PlanetfallDef For(int from, int to, bool loop)
    {
        if (!Enabled || loop || to != from + 1) return null;
        foreach (var d in Defs) if (d.world == to) return d;
        return null;
    }
}
