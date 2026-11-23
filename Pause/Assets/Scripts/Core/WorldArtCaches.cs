using UnityEngine;

// Lets go of one world's lazily made art when the run moves to another world, and of all of it when the run's
// scene goes.
//
// The lazy loaders keep what they slice in static tables (the boss's atlases and shot rims, the enemies' and
// elites' strips, the pieces enemies break into), and nothing ever took an entry out: every world visited
// left its textures and sprites resident for the rest of the session (a level added ~250 textures, and the
// next run started ~290 above the first). Now the tables are emptied at the moment the world changes
// (ArtWarm.Begin: under the planetfall / lift-off clouds, or at the run's start) and when a scene unloads;
// the engine's own sweep (AssetSweep at the next covered moment, or the scene load) then frees whatever no
// live object still shows. Nothing in use is destroyed: a renderer still drawing a released sprite keeps it
// (and its texture) alive until it stops, and a loader asked again simply slices again. The only objects
// destroyed outright are the boss's generated shot rims, which the sweep cannot see (HideAndDontSave), once
// that boss is gone.
//
// Not released: art a whole session shares (the ship's own sheets, pickups, explosions, the hostile shots'
// outlines and attack atlases, the procedural shapes): a handful of small textures per world, made once.
public static class WorldArtCaches
{
    // The world whose art the tables hold now (-1: none, the next Enter releases nothing twice).
    public static int World { get; private set; } = -1;
    public static int Releases { get; private set; }

    // World `world` begins (the run's start, or a world change). Returns true when the tables were emptied.
    public static bool Enter(int world)
    {
        if (world == World) return false;
        bool had = World >= 0;
        World = world;
        if (!had) return false;   // the first world of a scene: Leave already emptied them
        Release();
        AssetSweep.Defer();       // freed at the next moment the view is covered
        return true;
    }

    // The run's scene is going: everything world-specific goes with it (the scene load sweeps).
    public static void Leave()
    {
        World = -1;
        Release();
    }

    public static void Release()
    {
        Releases++;
        DeathCrash.ForgetTargetFragments();
        BossArt.Release();
        FirstUseWarm.Release("boss:");
        EnemyArt.Release();
        EnemyDeathFlipbook.ReleaseCache();
        EliteArt.Release();
    }

    // Tests.
    public static void Reset() { World = -1; Releases = 0; }
}
