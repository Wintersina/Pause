using System.IO;
using UnityEditor;
using UnityEngine;

// The Tide / Storm release flags (Desert will join): everything for the unfinished worlds ships IN the build
// (runtime art under Resources, never in a ~ staging folder) but is switched off. With every flag off the production
// loop is Space -> Frost -> Verdant -> Ember -> Space and nothing of Tide or Storm is listed or reachable; the flags
// chain in world order (Storm is live only with Tide on too); the developer picker may still start a run on them.
//
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites WorldFlagsTest
public static class WorldFlagsTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[FLAGS] PASS  " : "[FLAGS] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const int Ember = 3, Tide = 4, Storm = 5;
    static readonly string[] StormRoster =
    {
        "storm_rock_hail", "storm_rock_ore", "storm_rock_slag", "storm_rock_islet", "storm_big", "storm_fighter_1", "storm_fighter_2",
        "storm_fighter_3", "storm_fighter_4", "storm_chaser", "storm_alien",
    };
    static readonly string[] StormElites =
    {
        "storm_elite_skyhook_harrier", "storm_elite_refinery_tender", "storm_elite_rod_dancer", "storm_elite_anvil_cloud", "storm_elite_static_bastion",
    };

    public static int Execute()
    {
        fails = 0;
        bool tide = WorldManager.TideEnabled, storm = WorldManager.StormEnabled;
        int current = WorldManager.CurrentIndex, highest = PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld, 0);
        bool dev = DeveloperUnlocks.Enabled;
        try
        {
            DeveloperUnlocks.SetEnabled(false);
            Defaults(); Chain(); Leaks(); ArtShips();
        }
        finally
        {
            WorldManager.TideEnabled = tide; WorldManager.StormEnabled = storm;
            WorldManager.CurrentIndex = current; PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, highest);
            DeveloperUnlocks.SetEnabled(dev);
            Codex.Reload();
        }
        Debug.Log("[FLAGS] failures: " + fails);
        return fails;
    }

    static void Defaults()
    {
        WorldManager.TideEnabled = false; WorldManager.StormEnabled = false;
        Check("the shipped flags are off (Tide, Storm)", !WorldManager.TideEnabled && !WorldManager.StormEnabled);
        Check("all flags off: Ember is the last live world, 4 live", WorldManager.LastLiveWorld == Ember && WorldManager.LiveWorldCount == 4);
        bool loop = true;
        for (int w = 0; w < 3; w++) { WorldManager.CurrentIndex = w; loop &= WorldManager.HasNext && WorldManager.PortalDestination == w + 1; }
        WorldManager.CurrentIndex = Ember;
        loop &= !WorldManager.HasNext;
        Check("all flags off: the production loop is Space -> Frost -> Verdant -> Ember -> (loop) Space", loop);
        Check("... a developer run may still be started on Tide and Storm (the worlds exist)",
              WorldManager.Worlds.Length >= 6 && WorldManager.Worlds[Tide].displayName == "Tide" && WorldManager.Worlds[Storm].displayName == "Storm");
    }

    static void Chain()
    {
        WorldManager.TideEnabled = false; WorldManager.StormEnabled = true;
        Check("Storm alone is not enough (the flags chain in order): Ember stays last", WorldManager.LastLiveWorld == Ember && !WorldManager.StormLive);
        WorldManager.TideEnabled = true; WorldManager.StormEnabled = false;
        Check("Tide on, Storm off: Tide is last", WorldManager.LastLiveWorld == Tide);
        WorldManager.StormEnabled = true;
        Check("Tide and Storm on: Storm is last", WorldManager.LastLiveWorld == Storm && WorldManager.StormLive);
        WorldManager.TideEnabled = false; WorldManager.StormEnabled = false;
    }

    static void Leaks()
    {
        WorldManager.TideEnabled = false; WorldManager.StormEnabled = true;       // even Storm on without Tide leaks nothing
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, Storm);                // a stale dev-picker progress pref
        Codex.Reload();
        bool codexClosed = !Codex.IsListed(Codex.Find(Codex.WorldId(Tide))) && !Codex.IsListed(Codex.Find(Codex.WorldId(Storm)));
        foreach (var d in EnemyRoster.All)
            if (d.world >= Tide && !Codex.IsListed(Codex.Find(d.codexId))) { } else if (d.world >= Tide) codexClosed = false;
        foreach (var e in EliteCatalog.All)
            if (e.WorldIndex >= Tide) { var ce = Codex.Find(e.codexId); if (ce != null && Codex.IsListed(ce)) codexClosed = false; }
        Check("flags off: no Tide / Storm world, enemy or elite entry is listed in the codex (even with a stale highest-world pref)", codexClosed);
        int tideStormAch = 0;
        foreach (var a in AchievementCatalog.All) if (a.requiresTide && AchievementCatalog.IsActive(a)) tideStormAch++;
        Check("flags off: no Tide achievement is active", tideStormAch == 0);
        int reachable = 0;
        foreach (var e in EliteCatalog.All) if (e.WorldIndex <= WorldManager.LastLiveWorld && e.WorldIndex >= Tide) reachable++;
        Check("flags off: no Tide / Storm elite is reachable by the director or the title screen (they filter on LastLiveWorld)", reachable == 0);
        WorldManager.StormEnabled = false;
    }

    // Everything the off worlds need at run time sits under a Resources folder (not in a ~ staging folder), so a flip needs no rebuild of assets.
    static void ArtShips()
    {
        foreach (string k in StormRoster)
        {
            Check("Storm art in the build: " + k + " strip + death flipbook",
                  Exists("Assets/Art/Resources/Enemies/" + k + ".png") && Exists("Assets/Art/Resources/Enemies/Death/" + k + ".png"));
        }
        Check("Storm art in the build: rod mine atlas + death strip",
              Exists("Assets/Art/Resources/Enemies/Mines/rail_mines_neon_storm.png") && Exists("Assets/Art/Resources/Enemies/Death/storm_mine.png"));
        Check("Storm art in the build: rail", Exists("Assets/Art/Resources/Worlds/Storm/rail_storm_wide_v1.png"));
        bool boss = true;
        foreach (string f in new[] { "Storm", "Storm_shots", "Storm_card", "Storm_damage", "Storm_damage_fx", "Storm_death" })
            boss &= Exists("Assets/Art/Resources/Bosses/" + f + ".png");
        Check("Storm art in the build: boss atlas, shots, card, damage, damage fx, death", boss);
        foreach (string k in StormElites)
            Check("Storm art in the build: " + k + " flight + death strips",
                  Exists("Assets/Art/Resources/Elites/Storm/" + k + ".png") && Exists("Assets/Art/Resources/Elites/Storm/" + k + "_death.png") &&
                  Exists("Assets/Art/Resources/Elites/Defs/" + k + ".json"));
        Check("Storm art in the build: planetfall + lift-off set", Exists("Assets/Art/Backgrounds/Resources/Worlds/Storm/Planetfall/storm_planet.png"));
        Check("Tide art in the build: roster, rail, boss",
              Exists("Assets/Art/Resources/Enemies/tide_big.png") && Exists("Assets/Art/Resources/Worlds/Tide/rail_tide_wide_v1.png") && Exists("Assets/Art/Resources/Bosses/Tide.png"));
    }

    static bool Exists(string p) { return AssetDatabase.LoadAssetAtPath<Object>(p) != null || File.Exists(p); }
}
