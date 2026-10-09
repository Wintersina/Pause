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

    // Every planet arrived at by planetfall (Frost from Space, Verdant after
    // Frost's lift-off). A new planet (Ember) is one more entry here; the
    // tests swap the list.
    public static readonly PlanetfallDef[] All = { Frost, Verdant };
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
