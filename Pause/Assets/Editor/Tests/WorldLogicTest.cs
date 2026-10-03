using UnityEditor;
using UnityEngine;

// Headless check of world progression invariants.
public static class WorldLogicTest
{
    static int failures;

    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[WT] PASS  " : "[WT] FAIL  ") + what);
        if (!ok) failures++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    public static int Execute()
    {
        failures = 0;
        using var sandbox = new TestHarness.Sandbox();

        // start clean
        PlayerPrefs.DeleteKey(WorldManager.PrefsCurrentWorld);
        PlayerPrefs.DeleteKey(WorldManager.PrefsHighestWorld);

        Check("four worlds defined", WorldManager.Worlds.Length == 4);
        Check("world 0 is Space", WorldManager.Worlds[0].displayName == "Space");
        Check("space keeps authored art", WorldManager.Worlds[0].resourceFolder == "");
        Check("space keeps authored music", WorldManager.Worlds[0].musicResource == "");
        Check("teleport portal atlas is present",
              AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Resources/Vfx/teleport_portal_atlas.png") != null);
        Check("teleport portal has sixteen animation frames", TeleportPortalSprites.FrameCount == 16);
        Check("teleport warp sound is present",
              AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Resources/Audio/teleport_warp.wav") != null);
        Check("progressive music exposes six Ember arrangements",
              AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Resources/WorldMusic/EmberStage06.wav") != null);
        Check("world music exposes level-clock escalation", typeof(WorldMusic).GetMethod("TryEscalate") != null);
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, 1);
        PlayerPrefs.DeleteKey("boughtship15");
        // The editor shares PlayerPrefs with the Mac build; developer mode may
        // already be on there. Start from off so turning it on snapshots.
        PlayerPrefs.DeleteKey(DeveloperUnlocks.EnabledKey);
        DeveloperUnlocks.SetEnabled(true);
        Check("developer flag unlocks every ship", PlayerPrefs.GetString("boughtship15") == "True");
        Check("developer flag unlocks every world", PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld) == 3);
        DeveloperUnlocks.SetEnabled(false);
        Check("turning developer flag off restores locked ship state", !PlayerPrefs.HasKey("boughtship15"));
        Check("turning developer flag off restores saved world progress",
              PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld) == 1);
        foreach (string world in new[] { "Space", "Frost", "Verdant", "Ember" })
        {
            for (int stage = 1; stage <= 6; stage++)
                Check(world + " has progressive stage " + stage,
                    AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Resources/WorldMusic/" +
                        world + "Stage" + stage.ToString("00") + ".wav") != null);
        }
        Check("the themed rail-bomb animation atlas is present",
              AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Resources/Vfx/rail_bomb_themes_atlas.png") != null);

        Check("starts at index 0", WorldManager.CurrentIndex == 0);
        Check("has a next world", WorldManager.HasNext);

        // walk the whole progression
        for (int i = 1; i < WorldManager.Worlds.Length; i++)
        {
            WorldManager.CurrentIndex = i;
            Check("index " + i + " -> " + WorldManager.Worlds[i].displayName,
                  WorldManager.CurrentIndex == i);
            Check("world " + i + " has art folder",
                  !string.IsNullOrEmpty(WorldManager.Current.resourceFolder));
            Check("world " + i + " has music",
                  !string.IsNullOrEmpty(WorldManager.Current.musicResource));
            Check("world " + i + " ramps harder than previous",
                  WorldManager.Worlds[i].speedRampPerSecond >
                  WorldManager.Worlds[i - 1].speedRampPerSecond);
        }

        Check("last world has no next", !WorldManager.HasNext);
        Check("highest recorded", PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld, 0) == 3);

        // clamping
        WorldManager.CurrentIndex = 99;
        Check("clamps above range", WorldManager.CurrentIndex == WorldManager.Worlds.Length - 1);
        WorldManager.CurrentIndex = -5;
        Check("clamps below range", WorldManager.CurrentIndex == 0);
        Check("highest is not lowered by going back",
              PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld, 0) == 3);

        // the assets the themes point at must actually resolve
        for (int i = 1; i < WorldManager.Worlds.Length; i++)
        {
            var t = WorldManager.Worlds[i];
            string p = "Assets/Art/Resources/Worlds/" + t.resourceFolder + "/backdrop.png";
            Check("art present for " + t.displayName + " (" + p + ")",
                  AssetDatabase.LoadAssetAtPath<Texture2D>(p) != null);
            string m = "Assets/Audio/Resources/WorldMusic/" + t.displayName + ".wav";
            Check("music present for " + t.displayName,
                  AssetDatabase.LoadAssetAtPath<AudioClip>(m) != null);
        }

        PlayerPrefs.DeleteKey(WorldManager.PrefsCurrentWorld);
        PlayerPrefs.DeleteKey(WorldManager.PrefsHighestWorld);

        Debug.Log("[WT] failures: " + failures);
        return failures;
    }
}
