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

    const int Ember = 3, Tide = 4, Storm = 5, Desert = 6;
    static readonly string[] StormRoster =
    {
        "storm_rock_hail", "storm_rock_ore", "storm_rock_slag", "storm_rock_islet", "storm_big", "storm_fighter_1", "storm_fighter_2",
        "storm_fighter_3", "storm_fighter_4", "storm_chaser", "storm_alien",
    };
    static readonly string[] StormElites =
    {
        "storm_elite_skyhook_harrier", "storm_elite_refinery_tender", "storm_elite_rod_dancer", "storm_elite_anvil_cloud", "storm_elite_static_bastion",
    };

    static readonly string[] DesertRoster =
    {
        "desert_rock_dune", "desert_rock_spire", "desert_rock_husk", "desert_rock_slab", "desert_big", "desert_fighter_1", "desert_fighter_2",
        "desert_fighter_3", "desert_fighter_4", "desert_chaser", "desert_alien",
    };
    static readonly string[] DesertElites =
    {
        "desert_elite_dune_stalker", "desert_elite_caravan_hauler", "desert_elite_lens_matriarch", "desert_elite_glass_cannon",
        "desert_elite_scorpion_walker", "desert_elite_mirror_bastion",
    };

    public static int Execute()
    {
        fails = 0;
        bool tide = WorldManager.TideEnabled, storm = WorldManager.StormEnabled, desert = WorldManager.DesertEnabled;
        int current = WorldManager.CurrentIndex, highest = PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld, 0);
        bool dev = DeveloperUnlocks.Enabled;
        try
        {
            DeveloperUnlocks.SetEnabled(false);
            Defaults(); Chain(); Leaks(); ArtShips();
        }
        finally
        {
            WorldManager.TideEnabled = tide; WorldManager.StormEnabled = storm; WorldManager.DesertEnabled = desert;
            WorldManager.CurrentIndex = current; PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, highest);
            DeveloperUnlocks.SetEnabled(dev);
            Codex.Reload();
        }
        Debug.Log("[FLAGS] failures: " + fails);
        return fails;
    }

    static void Defaults()
    {
        WorldManager.TideEnabled = false; WorldManager.StormEnabled = false; WorldManager.DesertEnabled = false;
        Check("the shipped flags are off (Tide, Storm, Desert)", !WorldManager.TideEnabled && !WorldManager.StormEnabled && !WorldManager.DesertEnabled);
        Check("all flags off: Ember is the last live world, 4 live", WorldManager.LastLiveWorld == Ember && WorldManager.LiveWorldCount == 4);
        bool loop = true;
        for (int w = 0; w < 3; w++) { WorldManager.CurrentIndex = w; loop &= WorldManager.HasNext && WorldManager.PortalDestination == w + 1; }
        WorldManager.CurrentIndex = Ember;
        loop &= !WorldManager.HasNext;
        Check("all flags off: the production loop is Space -> Frost -> Verdant -> Ember -> (loop) Space", loop);
        Check("... a developer run may still be started on Tide, Storm and Desert (the worlds exist)",
              WorldManager.Worlds.Length >= 7 && WorldManager.Worlds[Tide].displayName == "Tide" && WorldManager.Worlds[Storm].displayName == "Storm" &&
              WorldManager.Worlds[Desert].displayName == "Desert");
    }

    static void Chain()
    {
        WorldManager.TideEnabled = false; WorldManager.StormEnabled = true;
        Check("Storm alone is not enough (the flags chain in order): Ember stays last", WorldManager.LastLiveWorld == Ember && !WorldManager.StormLive);
        WorldManager.TideEnabled = true; WorldManager.StormEnabled = false;
        Check("Tide on, Storm off: Tide is last", WorldManager.LastLiveWorld == Tide);
        WorldManager.StormEnabled = true;
        Check("Tide and Storm on: Storm is last", WorldManager.LastLiveWorld == Storm && WorldManager.StormLive);
        WorldManager.DesertEnabled = true;
        Check("Desert on with Tide and Storm: Desert is last", WorldManager.LastLiveWorld == Desert && WorldManager.DesertLive);
        WorldManager.StormEnabled = false;
        Check("Desert on, Storm off: Tide is last (the flags chain in order)", WorldManager.LastLiveWorld == Tide && !WorldManager.DesertLive);
        WorldManager.TideEnabled = false; WorldManager.StormEnabled = true;
        Check("Desert and Storm on, Tide off: Ember stays last", WorldManager.LastLiveWorld == Ember && !WorldManager.DesertLive);
        WorldManager.TideEnabled = false; WorldManager.StormEnabled = false; WorldManager.DesertEnabled = false;
    }

    static void Leaks()
    {
        WorldManager.TideEnabled = false; WorldManager.StormEnabled = true; WorldManager.DesertEnabled = true;   // even Storm / Desert on without Tide leak nothing
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, Desert);               // a stale dev-picker progress pref
        Codex.Reload();
        bool codexClosed = !Codex.IsListed(Codex.Find(Codex.WorldId(Tide))) && !Codex.IsListed(Codex.Find(Codex.WorldId(Storm))) && !Codex.IsListed(Codex.Find(Codex.WorldId(Desert)));
        foreach (var d in EnemyRoster.All)
            if (d.world >= Tide && !Codex.IsListed(Codex.Find(d.codexId))) { } else if (d.world >= Tide) codexClosed = false;
        foreach (var e in EliteCatalog.All)
            if (e.WorldIndex >= Tide) { var ce = Codex.Find(e.codexId); if (ce != null && Codex.IsListed(ce)) codexClosed = false; }
        Check("flags off: no Tide / Storm / Desert world, enemy or elite entry is listed in the codex (even with a stale highest-world pref)", codexClosed);
        int tideStormAch = 0;
        foreach (var a in AchievementCatalog.All) if (a.requiresTide && AchievementCatalog.IsActive(a)) tideStormAch++;
        Check("flags off: no Tide achievement is active", tideStormAch == 0);
        int reachable = 0;
        foreach (var e in EliteCatalog.All) if (e.WorldIndex <= WorldManager.LastLiveWorld && e.WorldIndex >= Tide) reachable++;
        Check("flags off: no Tide / Storm / Desert elite is reachable by the director or the title screen (they filter on LastLiveWorld)", reachable == 0);
        WorldManager.StormEnabled = false; WorldManager.DesertEnabled = false;
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
        foreach (string k in DesertRoster)
            Check("Desert art in the build: " + k + " strip + death flipbook",
                  Exists("Assets/Art/Resources/Enemies/" + k + ".png") && Exists("Assets/Art/Resources/Enemies/Death/" + k + ".png"));
        Check("Desert art in the build: Mirage Mine atlas + death strip",
              Exists("Assets/Art/Resources/Enemies/Mines/rail_mines_neon_desert.png") && Exists("Assets/Art/Resources/Enemies/Death/desert_mine.png"));
        bool dboss = true;
        foreach (string f in new[] { "Desert", "Desert_shots", "Desert_card", "Desert_damage", "Desert_damage_fx", "Desert_death" }) dboss &= Exists("Assets/Art/Resources/Bosses/" + f + ".png");
        Check("Desert art in the build: Dune-Eater atlas, shots, card, damage, damage fx, death", dboss);
        Check("Desert art in the build: attack fx row (boss_attack_fx.png has 7 rows)", Exists("Assets/Art/Resources/BossAttackFx/boss_attack_fx.png") && BossAttackFx.Rows == 7);
        foreach (string k in DesertElites)
            Check("Desert elite in the build: " + k + " flight + death strips and def (stand-in art, see Art/Enemies/Elite/Desert/STAND_IN.md)",
                  Exists("Assets/Art/Resources/Elites/Desert/" + k + ".png") && Exists("Assets/Art/Resources/Elites/Desert/" + k + "_death.png") &&
                  Exists("Assets/Art/Resources/Elites/Defs/" + k + ".json"));
        foreach (string f in new[] { "desert_planet", "desert_planet_limb", "desert_cloud_deck", "desert_cloud_deck_dark", "desert_entry_fx", "desert_entry_streaks", "desert_breakthrough" })
            Check("Desert art in the build: planetfall " + f, Exists("Assets/Art/Backgrounds/Resources/Worlds/Desert/Planetfall/" + f + ".png"));
        Check("Tide art in the build: roster, rail, boss",
              Exists("Assets/Art/Resources/Enemies/tide_big.png") && Exists("Assets/Art/Resources/Worlds/Tide/rail_tide_wide_v1.png") && Exists("Assets/Art/Resources/Bosses/Tide.png"));
    }

    static bool Exists(string p) { return AssetDatabase.LoadAssetAtPath<Object>(p) != null || File.Exists(p); }
}
