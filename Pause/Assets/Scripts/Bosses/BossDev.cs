using UnityEngine;

// Developer-mode ways to meet the current world's boss without flying a
// whole level:
//   * Options > developer section > "BOSS RUSH  ON": every run's boss arrives
//     BossConfig.DevRushAfterSeconds into the flight (works on a phone);
//   * B in the editor or a development build: the boss right now.
// Both only while developer mode (DeveloperUnlocks.Enabled) is on, and both
// go through WorldManager.EndLevel, the same path as the real level clock.
public static class BossDev
{
    public const string RushKey = "developerBossRush";

    public static bool RushEnabled =>
        DeveloperUnlocks.Enabled && PlayerPrefs.GetInt(RushKey, 0) == 1;

    public static void SetRush(bool on)
    {
        PlayerPrefs.SetInt(RushKey, on ? 1 : 0);
        PlayerPrefs.Save();
    }

    // Ends the current level now, which starts its boss. False when there is
    // no level running, developer mode is off, or it can't start.
    public static bool TriggerNow()
    {
        if (!DeveloperUnlocks.Enabled) return false;
        var world = WorldManager.Instance;
        if (world == null || BossEncounter.Running) return false;
        if (BossEncounter.DoneInWorld(WorldManager.CurrentIndex)) return false;
        world.EndLevel();
        return BossEncounter.Running;
    }
}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
public class BossDevHotkey : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.B)) BossDev.TriggerNow();
    }

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (scene.name != "gameS1") return;
        new GameObject("~BossDevHotkey").AddComponent<BossDevHotkey>();
    }
}
#endif
