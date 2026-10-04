using UnityEngine;

// Developer-mode ways to meet the current world's boss without flying a
// whole level:
//   * Options > developer section > "BOSS RUSH  ON": every run's boss arrives
//     BossConfig.DevRushAfterSeconds into the flight (works on a phone);
//   * B in the editor or a development build: the boss right now.
// Both only while developer mode (DeveloperUnlocks.Enabled) is on, and both
// go through WorldManager.EndLevel, the same path as the real level clock.
//   * "BOSS RUSH  FINAL" (the switch's third setting): a few seconds into a
//     run it jumps straight to the final world with a 1.5s boss fight, so the
//     KEEP FLYING / LOOP BACK choice comes up almost at once; after a LOOP
//     BACK it rushes every boss like ON. Letting the choice time out rushes
//     the encore's Ember boss too, so the automatic LOOP BACK is quick to
//     reach. F in the editor / a dev build: the same jump, right now.
public static class BossDev
{
    public const string RushKey = "developerBossRush";

    // RushKey values: the switch cycles OFF -> ON -> FINAL -> OFF.
    public const int RushOff = 0, RushOn = 1, RushFinal = 2;

    public static int RushMode => Mathf.Clamp(PlayerPrefs.GetInt(RushKey, 0), RushOff, RushFinal);

    public static bool RushEnabled =>
        DeveloperUnlocks.Enabled && RushMode != RushOff;

    public static bool FinalRushEnabled =>
        DeveloperUnlocks.Enabled && RushMode == RushFinal;

    public static void SetRush(bool on)
    {
        SetRushMode(on ? RushOn : RushOff);
    }

    public static void SetRushMode(int mode)
    {
        PlayerPrefs.SetInt(RushKey, Mathf.Clamp(mode, RushOff, RushFinal));
        PlayerPrefs.Save();
    }

    public static int NextRushMode(int mode) { return (mode + 1) % (RushFinal + 1); }

    public static string RushLabel(int mode)
    {
        return "BOSS RUSH  " + (mode == RushFinal ? "FINAL" : mode == RushOn ? "ON" : "OFF");
    }

    // Jumps to the final world and starts its boss with a short fight; when
    // it ends the KEEP FLYING / LOOP BACK choice comes up. False when there
    // is no level running, developer mode is off, or it can't start.
    public static bool TriggerFinal()
    {
        if (!DeveloperUnlocks.Enabled) return false;
        var world = WorldManager.Instance;
        if (world == null || BossEncounter.Running) return false;
        if (world.Route != WorldManager.FinalRoute.None) return false;
        world.DevJumpToFinal();
        if (BossEncounter.DoneInWorld(WorldManager.CurrentIndex)) BossEncounter.ForgetDone();
        BossEncounter.DevShortFight = true;
        world.EndLevel();
        if (!BossEncounter.Running) BossEncounter.DevShortFight = false;
        return BossEncounter.Running;
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
        if (Input.GetKeyDown(KeyCode.F)) BossDev.TriggerFinal();
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
