using UnityEngine;

// Developer mode's start-world pick and the developer-build default.
public static class DeveloperModeTest
{
    static int failures;

    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[DM] PASS  " : "[DM] FAIL  ") + what);
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

        StartWorldPick();
        RealProgressSurvives();
        OffWhileOffKeepsProgress();
        DevBuildDefault();

        Debug.Log("[DM] failures: " + failures);
        return failures;
    }

    static void Clean()
    {
        PlayerPrefs.DeleteKey(DeveloperUnlocks.EnabledKey);
        PlayerPrefs.DeleteKey(DeveloperUnlocks.SelectedWorldKey);
        PlayerPrefs.DeleteKey(DeveloperUnlocks.ChoiceBuildKey);
        PlayerPrefs.DeleteKey(WorldManager.PrefsCurrentWorld);
        PlayerPrefs.DeleteKey(WorldManager.PrefsHighestWorld);
    }

    static void StartWorldPick()
    {
        Clean();
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, 2);

        DeveloperUnlocks.SelectWorld(1);
        Check("dev off: pick is ignored, run starts at highest unlocked",
              DeveloperUnlocks.StartWorld(true) == 2);
        Check("dev off: pick is ignored, journey mode starts at Space",
              DeveloperUnlocks.StartWorld(false) == 0);

        DeveloperUnlocks.SetEnabled(true);
        Check("dev on: picked Frost overrides highest unlocked", DeveloperUnlocks.StartWorld(true) == 1);
        Check("dev on: picked Frost overrides journey mode", DeveloperUnlocks.StartWorld(false) == 1);
        DeveloperUnlocks.SelectWorld(0);
        Check("dev on: picking Space starts at Space", DeveloperUnlocks.StartWorld(true) == 0);
        DeveloperUnlocks.SelectWorld(99);
        Check("pick clamps to the last world",
              DeveloperUnlocks.StartWorld(true) == WorldManager.Worlds.Length - 1);

        PlayerPrefs.DeleteKey(DeveloperUnlocks.SelectedWorldKey);
        Check("dev on, nothing picked: normal rule (every world unlocked)",
              DeveloperUnlocks.StartWorld(true) == WorldManager.Worlds.Length - 1);

        // A run started from the pick still advances through portals.
        DeveloperUnlocks.SelectWorld(1);
        WorldManager.CurrentIndex = DeveloperUnlocks.StartWorld(true);
        Check("run begins on the picked world", WorldManager.Current.displayName == "Frost");
        Check("picked world still has a portal onward", WorldManager.HasNext);
        WorldManager.CurrentIndex = WorldManager.CurrentIndex + 1;
        Check("portal progression continues from the pick", WorldManager.Current.displayName == "Verdant");

        DeveloperUnlocks.SetEnabled(false);
        Clean();
    }

    static void RealProgressSurvives()
    {
        Clean();
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, 1);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, 1);
        PlayerPrefs.SetString("HasDoneTut", "true");
        PlayerPrefs.DeleteKey("boughtship7");

        DeveloperUnlocks.SetEnabledByUser(true);
        DeveloperUnlocks.SelectWorld(3);
        // a dev run on Ember
        WorldManager.CurrentIndex = DeveloperUnlocks.StartWorld(true);
        Check("dev run is on Ember", WorldManager.CurrentIndex == 3);
        DeveloperUnlocks.SetEnabledByUser(false);

        Check("real highestWorld restored", PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld) == 1);
        Check("real currentWorld restored", PlayerPrefs.GetInt(WorldManager.PrefsCurrentWorld) == 1);
        Check("real tutorial flag kept", PlayerPrefs.GetString("HasDoneTut") == "true");
        Check("ship unlocks rolled back", !PlayerPrefs.HasKey("boughtship7"));
        Check("next real run starts on real progress, not the pick",
              DeveloperUnlocks.StartWorld(true) == 1);

        // A save that had never reached any world stays that way too.
        Clean();
        DeveloperUnlocks.SetEnabledByUser(true);
        DeveloperUnlocks.SelectWorld(2);
        WorldManager.CurrentIndex = DeveloperUnlocks.StartWorld(true);
        DeveloperUnlocks.SetEnabledByUser(false);
        Check("absent highestWorld is absent again", !PlayerPrefs.HasKey(WorldManager.PrefsHighestWorld));
        Check("absent currentWorld is absent again", !PlayerPrefs.HasKey(WorldManager.PrefsCurrentWorld));
        Clean();
    }

    static void OffWhileOffKeepsProgress()
    {
        Clean();
        // An old snapshot is lying around from an earlier dev session...
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, 0);
        DeveloperUnlocks.SetEnabled(true);
        DeveloperUnlocks.SetEnabled(false);
        // ...then real play reaches Verdant.
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, 2);
        DeveloperUnlocks.SetEnabledByUser(false);
        Check("switching off while already off keeps newer real progress",
              PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld) == 2);
        Clean();
    }

    static void DevBuildDefault()
    {
        // The pure decision.
        Check("never touched -> on", DeveloperUnlocks.ShouldDefaultOn(false, "", "buildB"));
        Check("never touched (null) -> on", DeveloperUnlocks.ShouldDefaultOn(false, null, "buildB"));
        Check("already on -> nothing to do", !DeveloperUnlocks.ShouldDefaultOn(true, "", "buildB"));
        Check("switched off in this build -> stays off",
              !DeveloperUnlocks.ShouldDefaultOn(false, "buildB", "buildB"));
        Check("switched off in an older build -> on once for the new build",
              DeveloperUnlocks.ShouldDefaultOn(false, "buildA", "buildB"));

        // Through PlayerPrefs, as the PAUSE_DEV launch hook runs it.
        Clean();
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, 1);
        Check("fresh prefs: launch switches the mode on", DeveloperUnlocks.ApplyDevBuildDefault("buildB"));
        Check("mode is on", DeveloperUnlocks.Enabled);
        Check("second launch does nothing more", !DeveloperUnlocks.ApplyDevBuildDefault("buildB"));

        // The player switches it off in Options in this build: that sticks.
        PlayerPrefs.SetString(DeveloperUnlocks.ChoiceBuildKey, "buildB");   // what SetEnabledByUser stamps
        DeveloperUnlocks.SetEnabled(false);
        Check("default-on snapshot restores real progress",
              PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld) == 1);
        Check("explicit off survives relaunch of the same build",
              !DeveloperUnlocks.ApplyDevBuildDefault("buildB") && !DeveloperUnlocks.Enabled);

        // An existing install that had it explicitly off, upgraded in place.
        Check("new dev build turns it on once", DeveloperUnlocks.ApplyDevBuildDefault("buildC"));
        Check("mode is on after the upgrade", DeveloperUnlocks.Enabled);

        // SetEnabledByUser stamps the running build's id.
        DeveloperUnlocks.SetEnabledByUser(false);
        Check("explicit choice is stamped with this build",
              PlayerPrefs.GetString(DeveloperUnlocks.ChoiceBuildKey, "<none>") == DeveloperUnlocks.CurrentBuildId);
        Clean();
    }
}
