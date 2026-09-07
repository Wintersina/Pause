using UnityEngine;
using UnityEngine.SceneManagement;

// Clears transient gameplay state whenever a non-gameplay scene loads.
//
// buttonClicks.playerDied is a static and Time.timeScale is global, and neither
// was reset on the way out of a run. moveBackGround parks timeScale at 0 on
// death, so leaving to the menu carried a frozen clock and a stale "dead" flag
// into the next scene -- which is why the menu could come up unresponsive.
//
// Anything that depends on those is now guaranteed a clean slate.
public static class GameStateReset
{
    static readonly string[] GameplayScenes = { "gameS1", "tutorialS5" };

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (System.Array.IndexOf(GameplayScenes, scene.name) >= 0) return;
        Clear();
    }

    // Safe to call from anywhere; also used when leaving a run deliberately.
    public static void Clear()
    {
        Time.timeScale = 1f;
        buttonClicks.playerDied = false;
        startMenu.playerDied = false;
    }
}
