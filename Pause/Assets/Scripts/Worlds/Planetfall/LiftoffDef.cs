// What a lift-off needs to know about the planet it leaves: the planet's
// own planetfall art (the same globe, limb, cloud decks, plasma and burst,
// flown the other way), the world whose backdrop is the space between
// planets, and its words. One entry per world that is left by lift-off
// (LiftoffCatalog: Frost, Verdant, Ember); everything else about the sequence is the same for
// every planet (Liftoff, LiftoffTimeline).
//
// The way on after the interlude is not the lift-off's business: it is
// WorldManager.OpenGateway, which flies the next planet's planetfall when
// PlanetfallCatalog has one (Verdant after Frost, Ember after Verdant) and
// opens the portal otherwise. A lift-off with autoLoop (Ember's: the last
// world) skips the loop portal: the interlude already flies Space's sky, so
// the run simply starts its first world again, one loop on
// (WorldManager.StartLoop; the portal only if that fails).
public class LiftoffDef
{
    public int world;                   // WorldManager.Worlds index it leaves
    public PlanetfallDef planet;        // the planet's art, numbers and colours
    public int interludeWorld;          // whose backdrop (and rails) the interlude flies: Space
    public string banner;               // as the ship is taken: LIFT OFF
    public bool autoLoop;               // the last world: after the interlude the loop starts at once, no portal
                                        // (when the loop leads back to interludeWorld; else its portal)
}

// Which worlds are left by lift-off. A world not listed ends at its portal,
// as before.
public static class LiftoffCatalog
{
    // Off: every world ends at its portal (developer / tests).
    public static bool Enabled = true;

    public static readonly LiftoffDef Frost = new LiftoffDef
    {
        world = 1,
        planet = PlanetfallCatalog.Frost,
        interludeWorld = 0,
        banner = "LIFT OFF",
    };

    // Verdant: leaving the green planet after its boss, into the same calm
    // space; its gateway is Ember's planetfall.
    public static readonly LiftoffDef Verdant = new LiftoffDef
    {
        world = 2,
        planet = PlanetfallCatalog.Verdant,
        interludeWorld = 0,
        banner = "LIFT OFF",
    };

    // Ember: leaving the forge world after its boss, the last world. No
    // loop portal: once the interlude is over the run starts its first world
    // (Space) again, one loop on, through the loop's own world change
    // (LoopRules counting it as before).
    public static readonly LiftoffDef Ember = new LiftoffDef
    {
        world = 3,
        planet = PlanetfallCatalog.Ember,
        interludeWorld = 0,
        banner = "LIFT OFF",
        autoLoop = true,
    };

    public static readonly LiftoffDef[] Defs = { Frost, Verdant, Ember };

    // The lift-off for leaving `from` for `to` (the next planet, or the
    // loop's way round), or null for the portal straight away.
    public static LiftoffDef For(int from, int to, bool loop)
    {
        if (!Enabled || to == from) return null;
        foreach (var d in Defs) if (d.world == from && d.planet != null) return d;
        return null;
    }
}
