using UnityEngine;
using UnityEngine.SceneManagement;

// Developer-only progression override. It writes the same preferences as real
// progression, so every existing shop and spawn check continues to work.
//
// Turning it on snapshots the real progress (tutorial flag, world progress,
// owned ships); turning it off puts that snapshot back, so nothing done while
// it was on -- the unlocks, the start-world pick, worlds reached in dev runs --
// leaks into the player's real save.
public static class DeveloperUnlocks
{
    public const string EnabledKey = "developerUnlockEverything";

    // The start world picked in Options. Only read while the mode is on.
    public const string SelectedWorldKey = "developerSelectedWorld";

    // Raised after the override flips ship ownership, so an open space dock
    // can refresh its owned/price markers without polling PlayerPrefs.
    public static event System.Action Changed;

    // The Application.buildGUID of the build in which the player last switched
    // the mode on or off themselves. A developer build (PAUSE_DEV) defaults the
    // mode on unless that choice was made in this very build.
    public const string ChoiceBuildKey = "developerModeChoiceBuild";

    public static bool Enabled { get { return PlayerPrefs.GetInt(EnabledKey, 0) == 1; } }

    // Whether this binary offers the developer switch at all. Release builds
    // never show it, so players can't unlock everything from Options.
    public static bool Available
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || PAUSE_DEV
            return true;
#else
            return false;
#endif
        }
    }

    public static bool IsDevBuild
    {
        get
        {
#if PAUSE_DEV
            return true;
#else
            return false;
#endif
        }
    }

    public static void SetEnabled(bool enabled)
    {
        bool was = Enabled;
        if (enabled && !was) SnapshotProgress();
        PlayerPrefs.SetInt(EnabledKey, enabled ? 1 : 0);
        if (enabled)
        {
            PlayerPrefs.SetString("HasDoneTut", "true");
            PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, WorldManager.Worlds.Length - 1);
            for (int i = 1; i < shopingShips.shipTotal; i++)
                PlayerPrefs.SetString("boughtship" + i, "True");
        }
        // Only an on -> off transition restores. Restoring while already off
        // would replay a stale snapshot over newer real progress.
        else if (was) RestoreProgress();
        // Hull skins are never written while the mode is on (ShipSkins answers
        // "owned" at read time); just forget the skins equipped during it.
        if (!enabled) ShipSkins.ClearDeveloperChoices();
        PlayerPrefs.Save();
        Debug.Log("[DeveloperUnlocks] " + (enabled ? "all ships and worlds unlocked" : "override disabled"));
        if (Changed != null) Changed();
    }

    // The Options switch (and the F10 hotkey): an explicit choice, remembered
    // against this build so a developer build's default-on never overrides it.
    public static void SetEnabledByUser(bool enabled)
    {
        PlayerPrefs.SetString(ChoiceBuildKey, CurrentBuildId);
        SetEnabled(enabled);
    }

    public static string CurrentBuildId
    {
        get { return Application.buildGUID ?? ""; }
    }

    // Pure decision for a developer build's launch: switch the mode on unless
    // it already is, or the player switched it themselves in this same build.
    //
    // "Never touched" (no choice recorded) -> on. A choice recorded by another
    // build -- e.g. an older install that `adb install -r` upgraded, keeping
    // its PlayerPrefs -- also comes up on, once: after that the mode is on, and
    // any later choice in Options is stamped with this build's id and sticks.
    public static bool ShouldDefaultOn(bool enabled, string choiceBuild, string thisBuild)
    {
        if (enabled) return false;
        return string.IsNullOrEmpty(choiceBuild) || choiceBuild != thisBuild;
    }

    // Applies the developer-build default. Returns true if it switched it on.
    public static bool ApplyDevBuildDefault(string thisBuild)
    {
        if (!ShouldDefaultOn(Enabled, PlayerPrefs.GetString(ChoiceBuildKey, ""), thisBuild))
            return false;
        SetEnabled(true);
        return true;
    }

    public static void SelectWorld(int index)
    {
        PlayerPrefs.SetInt(SelectedWorldKey, Mathf.Clamp(index, 0, WorldManager.Worlds.Length - 1));
        PlayerPrefs.Save();
    }

    public static bool HasSelectedWorld { get { return PlayerPrefs.HasKey(SelectedWorldKey); } }

    // Unset, it shows the furthest world -- where a developer run starts
    // anyway, since the mode unlocks every world.
    public static int SelectedWorld => Mathf.Clamp(PlayerPrefs.GetInt(SelectedWorldKey,
        WorldManager.Worlds.Length - 1), 0, WorldManager.Worlds.Length - 1);

    // The world a new run begins on. With the mode on and a start world picked
    // in Options, that pick wins; otherwise the normal progression rule.
    public static int StartWorld(bool startAtHighestUnlocked)
    {
        if (Enabled && HasSelectedWorld) return SelectedWorld;
        return startAtHighestUnlocked ? PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld, 0) : 0;
    }

    static string BackupKey(string key) { return "developerBackup_" + key; }
    static string ExistsKey(string key) { return BackupKey(key) + "_exists"; }

    static void SnapshotProgress()
    {
        SnapshotString("HasDoneTut");
        SnapshotInt(WorldManager.PrefsHighestWorld);
        SnapshotInt(WorldManager.PrefsCurrentWorld);
        for (int i = 1; i < shopingShips.shipTotal; i++) SnapshotString("boughtship" + i);
    }

    static void RestoreProgress()
    {
        RestoreString("HasDoneTut");
        RestoreInt(WorldManager.PrefsHighestWorld);
        RestoreInt(WorldManager.PrefsCurrentWorld);
        for (int i = 1; i < shopingShips.shipTotal; i++) RestoreString("boughtship" + i);
    }

    static void SnapshotString(string key)
    {
        bool exists = PlayerPrefs.HasKey(key);
        PlayerPrefs.SetInt(ExistsKey(key), exists ? 1 : 0);
        if (exists) PlayerPrefs.SetString(BackupKey(key), PlayerPrefs.GetString(key));
    }

    static void SnapshotInt(string key)
    {
        bool exists = PlayerPrefs.HasKey(key);
        PlayerPrefs.SetInt(ExistsKey(key), exists ? 1 : 0);
        if (exists) PlayerPrefs.SetInt(BackupKey(key), PlayerPrefs.GetInt(key));
    }

    // A key with no snapshot at all (one taken before it was tracked) is left
    // alone rather than deleted.
    static void RestoreString(string key)
    {
        if (!PlayerPrefs.HasKey(ExistsKey(key))) return;
        if (PlayerPrefs.GetInt(ExistsKey(key), 0) == 1)
            PlayerPrefs.SetString(key, PlayerPrefs.GetString(BackupKey(key)));
        else PlayerPrefs.DeleteKey(key);
    }

    static void RestoreInt(string key)
    {
        if (!PlayerPrefs.HasKey(ExistsKey(key))) return;
        if (PlayerPrefs.GetInt(ExistsKey(key), 0) == 1)
            PlayerPrefs.SetInt(key, PlayerPrefs.GetInt(BackupKey(key)));
        else PlayerPrefs.DeleteKey(key);
    }
}

public class DeveloperUnlockHotkeys : MonoBehaviour
{
    void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Input.GetKeyDown(KeyCode.F10)) DeveloperUnlocks.SetEnabledByUser(!DeveloperUnlocks.Enabled);
        if (!DeveloperUnlocks.Enabled) return;
        if (Input.GetKeyDown(KeyCode.F1)) DeveloperUnlocks.SelectWorld(0);
        if (Input.GetKeyDown(KeyCode.F2)) DeveloperUnlocks.SelectWorld(1);
        if (Input.GetKeyDown(KeyCode.F3)) DeveloperUnlocks.SelectWorld(2);
        if (Input.GetKeyDown(KeyCode.F4)) DeveloperUnlocks.SelectWorld(3);
#endif
    }
}

public static class DeveloperUnlockBootstrap
{
    // A PAUSE_DEV build (BuildScript.BuildAndroidDev / BuildMacDev) comes up
    // with the mode on; see DeveloperUnlocks.ShouldDefaultOn.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ApplyBuildDefault()
    {
#if PAUSE_DEV
        if (DeveloperUnlocks.ApplyDevBuildDefault(DeveloperUnlocks.CurrentBuildId))
            Debug.Log("[DeveloperUnlocks] developer build: mode switched on by default");
#endif
    }

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
#if PAUSE_DEV
        DevBuildBadge.Create();
#endif
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        // The first scene finished loading before this hook existed.
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // The Options screen (opened from the start menu's Options button).
        if (scene.name == "leaderboardS3" && DeveloperUnlocks.Available &&
            Object.FindFirstObjectByType<DeveloperOptions>() == null)
            new GameObject("~DeveloperOptions").AddComponent<DeveloperOptions>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Object.FindFirstObjectByType<DeveloperUnlockHotkeys>() == null)
            new GameObject("~DeveloperUnlockHotkeys").AddComponent<DeveloperUnlockHotkeys>();
#endif
    }
}
