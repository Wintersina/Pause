using System.IO;
using UnityEditor;
using UnityEngine;

// The Tide / Storm / Desert release flags are gone (all three worlds are live): the loop is
// Space -> Frost -> Verdant -> Ember -> Tide -> Storm -> Desert -> Space, and everything those worlds need at run time
// ships IN the build (runtime art under Resources, never in a ~ staging folder).
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
        int current = WorldManager.CurrentIndex, highest = PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld, 0);
        bool dev = DeveloperUnlocks.Enabled;
        try
        {
            DeveloperUnlocks.SetEnabled(false);
            Chain(); ArtShips();
        }
        finally
        {
            WorldManager.CurrentIndex = current; PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, highest);
            DeveloperUnlocks.SetEnabled(dev);
            Codex.Reload();
        }
        Debug.Log("[FLAGS] failures: " + fails);
        return fails;
    }

    static void Chain()
    {
        Check("seven worlds, Desert is the last", WorldManager.Worlds.Length == 7 && WorldManager.LastLiveWorld == Desert && WorldManager.LiveWorldCount == 7 &&
              WorldManager.Worlds[Tide].displayName == "Tide" && WorldManager.Worlds[Storm].displayName == "Storm" && WorldManager.Worlds[Desert].displayName == "Desert");
        bool loop = true;
        for (int w = 0; w < Desert; w++) { WorldManager.CurrentIndex = w; loop &= WorldManager.HasNext && WorldManager.PortalDestination == w + 1; }
        WorldManager.CurrentIndex = Desert;
        loop &= !WorldManager.HasNext;
        Check("the loop is Space -> Frost -> Verdant -> Ember -> Tide -> Storm -> Desert -> (loop) Space", loop);
        Codex.Reload();
        bool listed = Codex.IsListed(Codex.Find(Codex.WorldId(Tide))) && Codex.IsListed(Codex.Find(Codex.WorldId(Storm))) && Codex.IsListed(Codex.Find(Codex.WorldId(Desert)));
        Check("the Tide, Storm and Desert world entries are listed in the codex", listed);
    }

    // Everything the later worlds need at run time sits under a Resources folder (not in a ~ staging folder).
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
