// What a lift-off needs to know about the planet it leaves: the planet's
// own planetfall art (the same globe, limb, cloud decks, plasma and burst,
// flown the other way), the world whose backdrop is the space between
// planets, and its words. One entry per world that is left by lift-off
// (LiftoffCatalog: Frost, Verdant, Ember, Tide, Storm, Desert); everything else about the sequence is the same for
// every planet (Liftoff, LiftoffTimeline).
//
// The way on after the interlude is not the lift-off's business: it is
// WorldManager.OpenGateway, which flies the next planet's planetfall when
// PlanetfallCatalog has one (Verdant after Frost, Ember after Verdant, Tide
// after Ember, Storm after Tide, Desert after Storm) and opens the portal otherwise.
// The final world's lift-off (Desert's) has no gateway: the interlude already
// flies Space's sky, so the run simply starts Space again, one loop on
// (WorldManager.StartLoop).
public class LiftoffDef
{
    public int world;                   // WorldManager.Worlds index it leaves
    public PlanetfallDef planet;        // the planet's art, numbers and colours
    public int interludeWorld;          // whose backdrop (and rails) the interlude flies: Space
    public string banner;               // as the ship is taken: LIFT OFF
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

    // Ember: leaving the forge world after its boss. Its gateway is Tide's planetfall.
    public static readonly LiftoffDef Ember = new LiftoffDef
    {
        world = 3,
        planet = PlanetfallCatalog.Ember,
        interludeWorld = 0,
        banner = "LIFT OFF",
    };

    // Tide: leaving the ocean planet after its boss; its gateway is Storm's planetfall.
    public static readonly LiftoffDef Tide = new LiftoffDef
    {
        world = 4,
        planet = PlanetfallCatalog.Tide,
        interludeWorld = 0,
        banner = "LIFT OFF",
    };

    // Storm: leaving the gas giant after its boss; its gateway is Desert's planetfall.
    public static readonly LiftoffDef Storm = new LiftoffDef
    {
        world = 5,
        planet = PlanetfallCatalog.Storm,
        interludeWorld = 0,
        banner = "LIFT OFF",
    };

    // Desert: leaving the dune planet after its boss, the final world: its interlude ends in Space's loop, no portal.
    public static readonly LiftoffDef Desert = new LiftoffDef
    {
        world = 6,
        planet = PlanetfallCatalog.Desert,
        interludeWorld = 0,
        banner = "LIFT OFF",
    };

    public static readonly LiftoffDef[] Defs = { Frost, Verdant, Ember, Tide, Storm, Desert };

    // The lift-off for leaving `from` for `to` (the next planet, or the
    // loop's way round), or null for the portal straight away.
    public static LiftoffDef For(int from, int to, bool loop)
    {
        if (!Enabled || to == from) return null;
        foreach (var d in Defs) if (d.world == from && d.planet != null) return d;
        return null;
    }
}
