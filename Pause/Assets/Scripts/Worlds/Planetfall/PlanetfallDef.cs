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

    public static readonly PlanetfallDef[] Defs = { Frost };

    // The planetfall for leaving `from` for `to`, or null for a portal.
    // A loop (the way back round after the final world) is always a portal.
    public static PlanetfallDef For(int from, int to, bool loop)
    {
        if (!Enabled || loop || to != from + 1) return null;
        foreach (var d in Defs) if (d.world == to) return d;
        return null;
    }
}
