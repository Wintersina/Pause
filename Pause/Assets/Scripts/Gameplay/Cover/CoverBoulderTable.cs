using UnityEngine;

// THE ONE PLACE EVERY COVER BOULDER NUMBER LIVES (docs/cover-boulders.md section 9).
//
// One Entry per world, in WorldManager.Worlds order. A world whose art has not been vetted is not Enabled: nothing
// spawns there (tests spawn through CoverBoulders.TestAnyWorld). To wire a world once its art is approved: move
// Art/Boulders~/<World>/{boulder.png, break.png, manifest.json} to Art/Resources/Boulders/<World>/ (the importer
// settings come from BoulderArtImporter) and set `enabled = true` here.
public enum CoverDrift
{
    Tumble,   // a slow continuous spin, either way (the Space asteroid)
    Slide,    // never rotates, flat foot down (mountain, mesa): only the sideways sway
    Sway,     // a pendulum about its centre (the tree)
    Rock,     // a heavy rocking (the volcanic spire)
    Bob,      // a vertical bob and a small sway (the reef)
    Hover,    // a bob, a slow lean and a flicker (the storm island)
}

public sealed class CoverBoulderEntry
{
    public string world;
    public string folder;                 // Resources folder of the art: Boulders/<World>
    public bool enabled;                  // art vetted and wired
    public bool interactive;              // true: a COVER boulder (blocks shots, bodies, the ship; destroyed by a boss). false: a decorative backdrop LANDFORM seen from far above the ship (affects nothing; fades out when a boss starts). Only Space is interactive: one switch per world
    public string concept;                // what it is (docs table)
    public float[] widths;                // drawn art width per variant, world u (the art's alpha bounding box)
    public CoverDrift drift;
    public Vector2 fall;                  // own fall down the screen, u/s (min, max)
    public float side;                    // largest sideways speed, u/s
    public Vector2 spin;                  // Tumble: deg/s (min, max), either way
    public float swayDeg, swayPeriod;     // Sway / Rock / Bob / Hover: lean amplitude (deg) and its period (s)
    public float bobAmp, bobPeriod;       // Bob / Hover: vertical bob (u) and its period (s)
    public Vector2 gap;                   // seconds between one arrival and the next
    public Color crack;                   // the light in the cracks (rumble flicker, burst)
    public Color debris;                  // chips / debris pixels
    public Color chip2;                   // a second debris tone

    // BACKDROP RULES (docs/cover-boulders.md section 13): the world's backdrop has four variants (BackdropSet.Variant 1..4, one per
    // visit). shapes[b-1] is the bit mask of the boulder shapes that blend into backdrop variant b (bit i = shape i; 0 = NO boulder
    // spawns over that backdrop). tint[b-1] multiplies the sprite so it takes that variant's colour; haze is the fog it emerges from.
    public int[] shapes = { 7, 7, 7, 7 };
    public Color[] tint;
    public Color haze = new Color(.45f, .5f, .55f, 1f);
    public bool stormFx;                  // the animated storm-effects layer (CoverStormFx): wisps, rain, hail, static, cloud shadow
    public bool outline;                  // a thin pale one-sided rim (Storm: the user asked for it so the rock stays visible); every other world has none (docs 13)
    public string[] notes;                // one line per backdrop variant: what it is and why these shapes (docs / PR table)

    public int Variants => widths.Length;

    // The shapes allowed over backdrop variant `b` (1..4; 0 / unknown: the union, for tests and menus).
    public int Mask(int b)
    {
        if (b >= 1 && b <= shapes.Length) return shapes[b - 1];
        int m = 0;
        for (int i = 0; i < shapes.Length; i++) m |= shapes[i];
        return m;
    }

    public Color TintFor(int b)
    {
        return tint != null && b >= 1 && b <= tint.Length ? tint[b - 1] : Color.white;
    }
}

public static class CoverBoulderTable
{
    static Color C(float r, float g, float b) { return new Color(r, g, b, 1f); }

    //                                      world     folder              enabled
    public static readonly CoverBoulderEntry[] Entries =
    {
        new CoverBoulderEntry {
            world = "Space", folder = "Boulders/Space", enabled = true, interactive = true,
            concept = "purple-lit cratered asteroid with magenta cracks",
            widths = new[] { 2.0f, 2.4f, 2.8f }, drift = CoverDrift.Tumble,
            fall = new Vector2(.75f, 1.1f), side = .15f, spin = new Vector2(3f, 7f),
            gap = new Vector2(45f, 85f),
            shapes = new[] { 7,7,7,7 },
            notes = new[] {
                "open space: an asteroid belongs on every sky variant",
                "open space: an asteroid belongs on every sky variant",
                "open space: an asteroid belongs on every sky variant",
                "open space: an asteroid belongs on every sky variant"
            },
            crack = C(1f, .33f, .91f), debris = C(.42f, .40f, .55f), chip2 = C(1f, .33f, .91f),
        },
        new CoverBoulderEntry {
            world = "Frost", folder = "Boulders/Frost", enabled = true,
            concept = "ice mountain / iceberg crag",
            widths = new[] { 2.4f, 2.6f, 2.8f }, drift = CoverDrift.Slide,
            fall = new Vector2(.6f, .9f), side = .10f, swayDeg = 0f, swayPeriod = 1f,
            gap = new Vector2(50f, 90f),
            shapes = new[] { 7,0,0,7 },
            notes = new[] {
                "v1 broken pack ice and floes: the ice mountain drifts out of the floes (all three shapes)",
                "v2 harbour wharf with wrecks and docks: industrial, a mountain would clash: NONE",
                "v3 industrial pipe / rail lattice with refinery towers on frozen ground: NONE (the user: 'doesn't need any boulders')",
                "v4 sheer ice cliffs and crevasses: natural ice mountain terrain (all three shapes)"
            },
            crack = C(.75f, .95f, 1f), debris = C(.80f, .92f, 1f), chip2 = C(.45f, .70f, .90f),
        },
        new CoverBoulderEntry {
            world = "Verdant", folder = "Boulders/Verdant", enabled = true,
            concept = "giant tree trunk / root mass",
            widths = new[] { 2.3f, 2.6f, 2.9f }, drift = CoverDrift.Sway,
            fall = new Vector2(.65f, .95f), side = .12f, swayDeg = 3f, swayPeriod = 9f,
            gap = new Vector2(45f, 85f),
            shapes = new[] { 7,7,0,7 },
            tint = new[] { Color.white, Color.white, Color.white, C(.78f, .95f, .95f) },
            notes = new[] {
                "v1 jungle canopy with rivers: giant tree masses grow out of the canopy",
                "v2 mangrove islands and channels: tree masses and root-balls belong",
                "v3 ruined overgrown city grid: concrete and roads, a tree mass would clash: NONE",
                "v4 deep bioluminescent teal jungle: tree masses, tinted teal"
            },
            crack = C(.72f, 1f, .77f), debris = C(.30f, .45f, .22f), chip2 = C(.55f, .85f, .45f),
        },
        new CoverBoulderEntry {
            world = "Ember", folder = "Boulders/Ember", enabled = true,
            concept = "obsidian spire / lava-veined slab",
            widths = new[] { 2.2f, 2.6f, 2.8f }, drift = CoverDrift.Rock,
            fall = new Vector2(.7f, 1.0f), side = .10f, swayDeg = 6f, swayPeriod = 7f,
            gap = new Vector2(45f, 85f),
            shapes = new[] { 7,0,7,7 },
            notes = new[] {
                "v1 lava rivers between basalt plateaus: volcanic stone belongs",
                "v2 foundry plant (pipes, vents, machinery): NONE",
                "v3 ash ridges and lava dunes: volcanic stone belongs",
                "v4 cracked caldera floor with a magma vortex: volcanic stone belongs"
            },
            crack = C(1f, .71f, .35f), debris = C(.16f, .14f, .20f), chip2 = C(1f, .60f, .24f),
        },
        new CoverBoulderEntry {
            world = "Tide", folder = "Boulders/Tide", enabled = true,
            concept = "sea stack / coral reef arch",
            widths = new[] { 2.2f, 2.9f, 2.7f }, drift = CoverDrift.Bob,
            fall = new Vector2(.65f, .95f), side = .15f, swayDeg = 2f, swayPeriod = 8f, bobAmp = .12f, bobPeriod = 6f,
            gap = new Vector2(45f, 85f),
            shapes = new[] { 7,0,0,7 },
            notes = new[] {
                "v1 storm-tossed open sea: a reef stack drifts in the swell",
                "v2 rusted oil-platform field: industrial brown, a reef would clash: NONE",
                "v3 sunken glowing city (buildings, streets, towers): a built-up city, NONE (the user)",
                "v4 whirlpool bioluminescent ocean: reef belongs"
            },
            crack = C(.61f, 1f, .88f), debris = C(.55f, .75f, .72f), chip2 = C(.90f, .95f, .80f),
        },
        new CoverBoulderEntry {
            world = "Storm", folder = "Boulders/Storm", enabled = true,
            concept = "floating thunderhead rock / storm island",
            widths = new[] { 2.4f, 2.9f, 2.6f }, drift = CoverDrift.Hover,
            fall = new Vector2(.7f, 1.0f), side = .20f, swayDeg = 1.5f, swayPeriod = 11f, bobAmp = .10f, bobPeriod = 5f,
            gap = new Vector2(40f, 80f),
            outline = true, stormFx = true,
            shapes = new[] { 7,7,7,7 },
            notes = new[] {
                "v1 sheared cloud banks: a floating thunderhead rock sits in the deck",
                "v2 towering cumulus field: thunderhead rock belongs",
                "v3 cyclone eye over the deck: thunderhead rock belongs",
                "v4 lightning fields: thunderhead rock belongs"
            },
            crack = C(.95f, .96f, 1f), debris = C(.50f, .54f, .62f), chip2 = C(1f, .95f, .63f),
        },
        new CoverBoulderEntry {
            world = "Desert", folder = "Boulders/Desert", enabled = true,
            concept = "sandstone mesa / rock arch",
            widths = new[] { 2.5f, 2.9f, 2.6f }, drift = CoverDrift.Slide,
            fall = new Vector2(.6f, .9f), side = .10f, swayDeg = 0f, swayPeriod = 1f,
            gap = new Vector2(50f, 90f),
            shapes = new[] { 7,0,0,7 },
            tint = new[] { Color.white, Color.white, Color.white, C(.62f, .70f, .95f) },
            notes = new[] {
                "v1 sand dunes: a rock outcrop belongs",
                "v2 mesa canyon crossed by giant pipelines and mining rigs: an industrial complex, NONE",
                "v3 cracked flats with mining scaffolds and rails (strip mine): industrial, NONE",
                "v4 night dunes (blue): rock outcrop, tinted blue"
            },
            crack = C(1f, .91f, .66f), debris = C(.78f, .60f, .38f), chip2 = C(.95f, .85f, .62f),
        },
    };

    public static CoverBoulderEntry For(int world)
    {
        return Entries[Mathf.Clamp(world, 0, Entries.Length - 1)];
    }

    // Is a boulder allowed to spawn in this world (its art wired)?
    public static bool Enabled(int world)
    {
        return world >= 0 && world < Entries.Length && Entries[world].enabled;
    }

    // The data checks (CoverBoulderTest): an entry per world, sane ranges, no pink outside Space.
    // Returns null when fine, else what is wrong.
    public static string Validate()
    {
        if (Entries.Length != WorldManager.Worlds.Length) return "entries " + Entries.Length + " != worlds " + WorldManager.Worlds.Length;
        for (int w = 0; w < Entries.Length; w++)
        {
            var e = Entries[w];
            if (e == null) return "world " + w + ": no entry";
            if (e.world != WorldManager.Worlds[w].displayName) return "world " + w + ": named " + e.world;
            if (e.widths == null || e.widths.Length != 3) return e.world + ": needs three variants";
            foreach (var wd in e.widths) if (wd < 1.8f || wd > 3.2f) return e.world + ": width " + wd + " outside 1.8-3.2 u";
            if (e.fall.x < .3f || e.fall.y < e.fall.x || e.fall.y > 1.3f) return e.world + ": fall " + e.fall + " is not slow";
            if (e.side < 0f || e.side > .3f) return e.world + ": side speed " + e.side;
            if (e.gap.x < 40f || e.gap.y < e.gap.x || e.gap.y > 90f) return e.world + ": gap " + e.gap + " outside 40-90 s";
            if (e.drift == CoverDrift.Tumble && (e.spin.x <= 0f || e.spin.y > 10f)) return e.world + ": spin " + e.spin;
            if (e.drift != CoverDrift.Tumble && e.drift != CoverDrift.Slide && (e.swayDeg <= 0f || e.swayPeriod < 3f)) return e.world + ": sway";
            if (e.drift == CoverDrift.Slide && e.swayDeg != 0f) return e.world + ": a Slide boulder never leans";
            if ((e.drift == CoverDrift.Bob || e.drift == CoverDrift.Hover) && (e.bobAmp <= 0f || e.bobAmp > .2f || e.bobPeriod < 3f)) return e.world + ": bob";
            if (e.shapes == null || e.shapes.Length != 4) return e.world + ": shapes needs one mask per backdrop variant (4)";
            foreach (var m in e.shapes) if (m < 0 || m > 7) return e.world + ": shape mask " + m;
            if (e.enabled && e.Mask(0) == 0) return e.world + ": enabled with no shape over any backdrop";
            if (e.notes == null || e.notes.Length != 4) return e.world + ": notes needs one line per backdrop variant (4)";
            if (e.tint != null && e.tint.Length != 4) return e.world + ": tint needs 4 colours";
            if (e.folder != "Boulders/" + e.world) return e.world + ": folder " + e.folder;
            if (w > 0)
            {
                if (WorldCue.IsPink(e.crack) || WorldCue.IsPink(e.debris) || WorldCue.IsPink(e.chip2) || WorldCue.IsPink(e.haze)) return e.world + ": pink outside Space";
            }
        }
        return null;
    }
}
