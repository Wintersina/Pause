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
        ReleaseLeftover();
        NoSnapshotLeftover();
        DevBuildUnchanged();
        CloudSnapshotWhileOn();

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

    static void SeedRealProgress()
    {
        Clean();
        for (int i = 1; i < shopingShips.RosterTotal; i++) PlayerPrefs.DeleteKey("boughtship" + i);
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, 1);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, 1);
        PlayerPrefs.SetString("HasDoneTut", "true");
        PlayerPrefs.SetString("boughtship3", "True");
    }

    static void CleanShips()
    {
        for (int i = 1; i < shopingShips.RosterTotal; i++) PlayerPrefs.DeleteKey("boughtship" + i);
        PlayerPrefs.DeleteKey("HasDoneTut");
        Clean();
    }

    // A release build (available == false) finding the mode left on.
    static void ReleaseLeftover()
    {
        SeedRealProgress();
        PlayerPrefs.DeleteKey("HasDoneTut");   // real player had not finished the tutorial
        DeveloperUnlocks.SetEnabled(true);     // the earlier developer build
        DeveloperUnlocks.SelectWorld(2);
        int paragon = PremiumShips.Paragon;
        Check("setup: developer mode granted Paragon", PlayerPrefs.GetString("boughtship" + paragon) == "True");

        Check("a developer build does not restore", !DeveloperUnlocks.RestoreLeftoverIfUnavailable(true));
        Check("still on after the dev-build call", PlayerPrefs.GetInt(DeveloperUnlocks.EnabledKey) == 1);

        Check("release startup found leftover state", DeveloperUnlocks.RestoreLeftoverIfUnavailable(false));
        Check("flag cleared", PlayerPrefs.GetInt(DeveloperUnlocks.EnabledKey, 0) == 0);
        Check("Paragon no longer owned", !PlayerPrefs.HasKey("boughtship" + paragon));
        Check("genuine ship 3 kept", PlayerPrefs.GetString("boughtship3") == "True");
        Check("other ships rolled back", !PlayerPrefs.HasKey("boughtship7"));
        Check("world progress restored", PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld) == 1);
        Check("current world restored", PlayerPrefs.GetInt(WorldManager.PrefsCurrentWorld) == 1);
        Check("tutorial flag restored (absent)", !PlayerPrefs.HasKey("HasDoneTut"));
        Check("developer start-world pick cleared", !PlayerPrefs.HasKey(DeveloperUnlocks.SelectedWorldKey));
        Check("idempotent: second run finds nothing", !DeveloperUnlocks.RestoreLeftoverIfUnavailable(false));
        Check("real progress survives the second run", PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld) == 1);
        CleanShips();
    }

    static void NoSnapshotLeftover()
    {
        CleanShips();
        // an earlier case left its snapshot behind: this one starts with none at all
        var tracked = new System.Collections.Generic.List<string> { "HasDoneTut", WorldManager.PrefsHighestWorld, WorldManager.PrefsCurrentWorld };
        for (int i = 1; i < shopingShips.RosterTotal; i++) tracked.Add("boughtship" + i);
        foreach (var k in tracked) { PlayerPrefs.DeleteKey("developerBackup_" + k); PlayerPrefs.DeleteKey("developerBackup_" + k + "_exists"); }
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);   // flag with no snapshot at all
        PlayerPrefs.SetString("boughtship5", "True");          // genuine purchase
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, 2);
        Check("no snapshot: leftover found", DeveloperUnlocks.RestoreLeftoverIfUnavailable(false));
        Check("no snapshot: flag cleared", PlayerPrefs.GetInt(DeveloperUnlocks.EnabledKey, 0) == 0);
        Check("no snapshot: genuine ownership untouched", PlayerPrefs.GetString("boughtship5") == "True");
        Check("no snapshot: world progress untouched", PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld) == 2);
        CleanShips();
    }

    static void DevBuildUnchanged()
    {
        SeedRealProgress();
        DeveloperUnlocks.SetEnabled(true);
        DeveloperUnlocks.RestoreLeftoverIfUnavailable(DeveloperUnlocks.Available);   // Available is true here
        Check("dev build: mode stays on", PlayerPrefs.GetInt(DeveloperUnlocks.EnabledKey) == 1);
        Check("dev build: everything stays unlocked", PlayerPrefs.GetString("boughtship7") == "True");
        DeveloperUnlocks.SetEnabled(false);
        Check("dev build: switching off still restores", !PlayerPrefs.HasKey("boughtship7") &&
              PlayerPrefs.GetString("boughtship3") == "True");
        CleanShips();
    }

    // The cloud snapshot taken while developer mode is on holds only real progress.
    static void CloudSnapshotWhileOn()
    {
        SeedRealProgress();
        DeveloperUnlocks.SetEnabled(true);
        var snap = ProgressSnapshot.Capture(1);
        var owned = new System.Collections.Generic.List<int>(snap.boughtShips);
        Check("cloud snapshot keeps the genuine ship", owned.Contains(3));
        Check("cloud snapshot has no developer-granted ship", !owned.Contains(7));
        Check("cloud snapshot has no Paragon", !owned.Contains(PremiumShips.Paragon));
        Check("cloud snapshot has real highest world", snap.highestWorld == 1);
        DeveloperUnlocks.SetEnabled(false);
        CleanShips();
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
