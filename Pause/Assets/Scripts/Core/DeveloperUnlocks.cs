using UnityEngine;
using UnityEngine.SceneManagement;

// Developer-only progression override. It writes the same preferences as real
// progression, so every existing shop and spawn check continues to work.
public static class DeveloperUnlocks
{
    public const string EnabledKey = "developerUnlockEverything";
    public const string SelectedWorldKey = "developerSelectedWorld";

    // Raised after the override flips ship ownership, so an open space dock
    // can refresh its owned/price markers without polling PlayerPrefs.
    public static event System.Action Changed;

    public static bool Enabled { get { return PlayerPrefs.GetInt(EnabledKey, 0) == 1; } }

    public static void SetEnabled(bool enabled)
    {
        if (enabled && !Enabled) SnapshotProgress();
        PlayerPrefs.SetInt(EnabledKey, enabled ? 1 : 0);
        if (enabled)
        {
            PlayerPrefs.SetString("HasDoneTut", "true");
            PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, WorldManager.Worlds.Length - 1);
            for (int i = 1; i < shopingShips.shipTotal; i++)
                PlayerPrefs.SetString("boughtship" + i, "True");
        }
        else RestoreProgress();
        PlayerPrefs.Save();
        Debug.Log("[DeveloperUnlocks] " + (enabled ? "all ships and worlds unlocked" : "override disabled"));
        if (Changed != null) Changed();
    }

    public static void SelectWorld(int index)
    {
        PlayerPrefs.SetInt(SelectedWorldKey, Mathf.Clamp(index, 0, WorldManager.Worlds.Length - 1));
        PlayerPrefs.Save();
    }

    public static int SelectedWorld => Mathf.Clamp(PlayerPrefs.GetInt(SelectedWorldKey,
        WorldManager.Worlds.Length - 1), 0, WorldManager.Worlds.Length - 1);

    static string BackupKey(string key) { return "developerBackup_" + key; }
    static string ExistsKey(string key) { return BackupKey(key) + "_exists"; }

    static void SnapshotProgress()
    {
        SnapshotString("HasDoneTut");
        SnapshotInt(WorldManager.PrefsHighestWorld);
        for (int i = 1; i < shopingShips.shipTotal; i++) SnapshotString("boughtship" + i);
    }

    static void RestoreProgress()
    {
        RestoreString("HasDoneTut");
        RestoreInt(WorldManager.PrefsHighestWorld);
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

    static void RestoreString(string key)
    {
        if (PlayerPrefs.GetInt(ExistsKey(key), 0) == 1)
            PlayerPrefs.SetString(key, PlayerPrefs.GetString(BackupKey(key)));
        else PlayerPrefs.DeleteKey(key);
    }

    static void RestoreInt(string key)
    {
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
        if (Input.GetKeyDown(KeyCode.F10)) DeveloperUnlocks.SetEnabled(!DeveloperUnlocks.Enabled);
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
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Object.FindFirstObjectByType<DeveloperUnlockHotkeys>() == null)
            new GameObject("~DeveloperUnlockHotkeys").AddComponent<DeveloperUnlockHotkeys>();
#endif
    }
}
