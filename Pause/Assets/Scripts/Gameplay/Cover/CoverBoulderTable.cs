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

    public int Variants => widths.Length;
}

public static class CoverBoulderTable
{
    static Color C(float r, float g, float b) { return new Color(r, g, b, 1f); }

    //                                      world     folder              enabled
    public static readonly CoverBoulderEntry[] Entries =
    {
        new CoverBoulderEntry {
            world = "Space", folder = "Boulders/Space", enabled = true,
            concept = "purple-lit cratered asteroid with magenta cracks",
            widths = new[] { 2.0f, 2.4f, 2.8f }, drift = CoverDrift.Tumble,
            fall = new Vector2(.75f, 1.1f), side = .15f, spin = new Vector2(3f, 7f),
            gap = new Vector2(45f, 85f),
            crack = C(1f, .33f, .91f), debris = C(.42f, .40f, .55f), chip2 = C(1f, .33f, .91f),
        },
        new CoverBoulderEntry {
            world = "Frost", folder = "Boulders/Frost", enabled = false,
            concept = "ice mountain / iceberg crag",
            widths = new[] { 2.4f, 2.6f, 2.8f }, drift = CoverDrift.Slide,
            fall = new Vector2(.6f, .9f), side = .10f, swayDeg = 0f, swayPeriod = 1f,
            gap = new Vector2(50f, 90f),
            crack = C(.75f, .95f, 1f), debris = C(.80f, .92f, 1f), chip2 = C(.45f, .70f, .90f),
        },
        new CoverBoulderEntry {
            world = "Verdant", folder = "Boulders/Verdant", enabled = false,
            concept = "giant tree trunk / root mass",
            widths = new[] { 2.3f, 2.6f, 2.9f }, drift = CoverDrift.Sway,
            fall = new Vector2(.65f, .95f), side = .12f, swayDeg = 3f, swayPeriod = 9f,
            gap = new Vector2(45f, 85f),
            crack = C(.72f, 1f, .77f), debris = C(.30f, .45f, .22f), chip2 = C(.55f, .85f, .45f),
        },
        new CoverBoulderEntry {
            world = "Ember", folder = "Boulders/Ember", enabled = false,
            concept = "obsidian spire / lava-veined slab",
            widths = new[] { 2.2f, 2.6f, 2.8f }, drift = CoverDrift.Rock,
            fall = new Vector2(.7f, 1.0f), side = .10f, swayDeg = 6f, swayPeriod = 7f,
            gap = new Vector2(45f, 85f),
            crack = C(1f, .71f, .35f), debris = C(.16f, .14f, .20f), chip2 = C(1f, .60f, .24f),
        },
        new CoverBoulderEntry {
            world = "Tide", folder = "Boulders/Tide", enabled = false,
            concept = "sea stack / coral reef arch",
            widths = new[] { 2.2f, 2.9f, 2.7f }, drift = CoverDrift.Bob,
            fall = new Vector2(.65f, .95f), side = .15f, swayDeg = 2f, swayPeriod = 8f, bobAmp = .12f, bobPeriod = 6f,
            gap = new Vector2(45f, 85f),
            crack = C(.61f, 1f, .88f), debris = C(.55f, .75f, .72f), chip2 = C(.90f, .95f, .80f),
        },
        new CoverBoulderEntry {
            world = "Storm", folder = "Boulders/Storm", enabled = false,
            concept = "floating thunderhead rock / storm island",
            widths = new[] { 2.4f, 2.9f, 2.6f }, drift = CoverDrift.Hover,
            fall = new Vector2(.7f, 1.0f), side = .20f, swayDeg = 1.5f, swayPeriod = 11f, bobAmp = .10f, bobPeriod = 5f,
            gap = new Vector2(40f, 80f),
            crack = C(.95f, .96f, 1f), debris = C(.50f, .54f, .62f), chip2 = C(1f, .95f, .63f),
        },
        new CoverBoulderEntry {
            world = "Desert", folder = "Boulders/Desert", enabled = false,
            concept = "sandstone mesa / rock arch",
            widths = new[] { 2.5f, 2.9f, 2.6f }, drift = CoverDrift.Slide,
            fall = new Vector2(.6f, .9f), side = .10f, swayDeg = 0f, swayPeriod = 1f,
            gap = new Vector2(50f, 90f),
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
            if (e.folder != "Boulders/" + e.world) return e.world + ": folder " + e.folder;
            if (w > 0)
            {
                if (WorldCue.IsPink(e.crack) || WorldCue.IsPink(e.debris) || WorldCue.IsPink(e.chip2)) return e.world + ": pink outside Space";
            }
        }
        return null;
    }
}
