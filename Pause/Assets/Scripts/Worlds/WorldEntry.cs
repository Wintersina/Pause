using UnityEngine;

// How a run BEGINS: the entry animation every run started from the menu
// plays before the pilot has control.
//
//   Space        the PORTAL ARRIVAL (PortalArrival): the gateway opens at the
//                ship's start, the ship flies out of it, it closes (~2.6 s)
//   a planet     that planet's PLANETFALL (Planetfall.SpawnEntry): the ship
//                dives through its atmosphere and the cloud decks and breaks
//                out over the level (~5.6 s), the world's rails, backdrop and
//                music switching in under the clouds
//
// "Started from the menu" is every way into gameS1 except REPLAY: the menu's
// PLAY (and the developer's world picker, the player's START WORLD, the
// furthest-planet rule -- they only decide WHICH world, WorldManager.
// RunStartWorld), and the first real run after the tutorial (its PLAY button
// or SKIP): all of them play the entry. A REPLAY after a death (buttonClicks.
// replay pins the run's world, WorldManager.PinnedReplayWorld) flies the same
// run again at once -- the pilot has just watched the entry. A replay is
// quick, a run begun anywhere else is not. Death -> MENU -> PLAY is a menu
// start: it plays.
//
// The tutorial scene has no WorldManager, so it has no entry.
//
// While an entry plays (Active) the run has not begun: the level clock, the
// speed ramp, the score and the dust, the elite director and the calm
// start's window all wait for the hand-off (WorldManager.Tick, moveBackGround,
// score, EliteDirector, enmiesOnBoard). The entry is a transition like any
// other for the weapon charge (WorldTransition.InProgress).
public static class WorldEntry
{
    public enum Kind { None, Portal, Planetfall }

    // Off: every run starts on the ground at once (developer / tests).
    public static bool Enabled = true;

    // An entry is playing: the run has not begun yet.
    public static bool Active { get { return Planetfall.EntryActive || PortalArrival.Active; } }

    // Which entry a run starting on `world` plays. A replay and a disabled
    // entry play none; Space the portal arrival; a planet its planetfall
    // (the portal arrival when the planet has none -- planetfalls switched
    // off, its art missing is found out later by Begin).
    public static Kind Plan(int world, bool replay)
    {
        if (!Enabled || replay) return Kind.None;
        if (world > 0 && PlanetfallCatalog.For(world - 1, world, false) != null) return Kind.Planetfall;
        return Kind.Portal;
    }

    // Starts the entry. The kind actually playing (None: no ship in the scene,
    // or the art is missing for both).
    public static Kind Begin(Kind plan, int world)
    {
        if (plan == Kind.None) return Kind.None;
        var mover = Object.FindFirstObjectByType<movePlayer>();
        if (mover == null) return Kind.None;
        if (plan == Kind.Planetfall)
        {
            var def = PlanetfallCatalog.For(world - 1, world, false);
            if (Planetfall.SpawnEntry(def, mover.transform) != null) return Kind.Planetfall;
        }
        // Space's, and the fallback of a planet whose planetfall can't play.
        // The gateway wears the colour of the world the ship arrives in.
        var theme = WorldManager.Worlds[Mathf.Clamp(world, 0, WorldManager.Worlds.Length - 1)];
        return PortalArrival.Spawn(mover.transform, theme.portalColor) != null ? Kind.Portal : Kind.None;
    }

    // Ends whatever entry is playing, at once (a dev jump, a scene teardown).
    public static void Cancel()
    {
        if (PortalArrival.Live != null) BossUtil.Kill(PortalArrival.Live.gameObject);
        if (Planetfall.EntryActive) BossUtil.Kill(Planetfall.Live.gameObject);
    }
}
