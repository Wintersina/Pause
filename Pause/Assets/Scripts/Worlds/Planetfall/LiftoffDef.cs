// What a lift-off needs to know about the planet it leaves: the planet's
// own planetfall art (the same globe, limb, cloud decks, plasma and burst,
// flown the other way), the world whose backdrop is the space between
// planets, and its words. One entry per world that is left by lift-off
// (LiftoffCatalog: Frost, Verdant, Ember, Tide, Storm); everything else about the sequence is the same for
// every planet (Liftoff, LiftoffTimeline).
//
// The way on after the interlude is not the lift-off's business: it is
// WorldManager.OpenGateway, which flies the next planet's planetfall when
// PlanetfallCatalog has one (Verdant after Frost, Ember after Verdant, Tide
// after Ember once WorldManager.TideEnabled, Storm after Tide once WorldManager.StormEnabled) and opens the portal otherwise.
// A lift-off that loops (AutoLoopNow: Tide's, the last world; Ember's only
// while the release switch is off and it is still the last LIVE world) skips
// the loop portal: the interlude already flies Space's sky, so the run
// simply starts its first world again, one loop on (WorldManager.StartLoop;
// the portal only if that fails).
public class LiftoffDef
{
    public int world;                   // WorldManager.Worlds index it leaves
    public PlanetfallDef planet;        // the planet's art, numbers and colours
    public int interludeWorld;          // whose backdrop (and rails) the interlude flies: Space
    public string banner;               // as the ship is taken: LIFT OFF
    public bool autoLoop;               // the final world (Tide, Storm; AutoLoopNow says whether it is the last LIVE one): after the interlude the loop starts at once, no portal
                                        // (when the loop leads back to interludeWorld; else its portal)

    // Ember's only: until the release switch flips (WorldManager.TideEnabled) it
    // is the last LIVE world, so its lift-off starts the loop like the final
    // world's does. Tide's gateway for Ember takes over the moment Tide is live.
    public bool loopsWhileLastLive;

    // Whether the loop starts at this lift-off's gateway right now.
    public bool AutoLoopNow
    {
        // world >= LastLiveWorld: a world past the live chain (a developer run begun on Tide
        // or Storm while its switch is off) is the last world of that run; a world with a
        // live successor never loops.
        get { return (autoLoop || loopsWhileLastLive) && world >= WorldManager.LastLiveWorld; }
    }
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

    // Ember: leaving the forge world after its boss. Its gateway is Tide's
    // planetfall once WorldManager.TideEnabled; until then Ember is the last
    // live world and loopsWhileLastLive (AutoLoopNow) makes this lift-off start the loop (no
    // portal) exactly as before Tide existed. autoLoop itself is false: it is
    // Tide's, the final world.
    public static readonly LiftoffDef Ember = new LiftoffDef
    {
        world = 3,
        planet = PlanetfallCatalog.Ember,
        interludeWorld = 0,
        banner = "LIFT OFF",
        loopsWhileLastLive = true,
    };

    // Tide: leaving the ocean planet after its boss, the final world until
    // WorldManager.StormEnabled (AutoLoopNow is false then: Storm's planetfall
    // is the gateway). No loop portal while it loops: once the interlude is over the run starts its first world
    // (Space) again, one loop on, through the loop's own world change
    // (LoopRules counting it as before).
    public static readonly LiftoffDef Tide = new LiftoffDef
    {
        world = 4,
        planet = PlanetfallCatalog.Tide,
        interludeWorld = 0,
        banner = "LIFT OFF",
        autoLoop = true,
    };

    // Storm: leaving the gas giant after its boss, the final world once
    // WorldManager.StormEnabled. No loop portal (as Tide's, while Tide was last).
    public static readonly LiftoffDef Storm = new LiftoffDef
    {
        world = 5,
        planet = PlanetfallCatalog.Storm,
        interludeWorld = 0,
        banner = "LIFT OFF",
        autoLoop = true,
    };

    public static readonly LiftoffDef[] Defs = { Frost, Verdant, Ember, Tide, Storm };

    // The lift-off for leaving `from` for `to` (the next planet, or the
    // loop's way round), or null for the portal straight away.
    public static LiftoffDef For(int from, int to, bool loop)
    {
        if (!Enabled || to == from) return null;
        foreach (var d in Defs) if (d.world == from && d.planet != null) return d;
        return null;
    }
}
