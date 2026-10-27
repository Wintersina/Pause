using UnityEngine;
using UnityEngine.SceneManagement;

// Scene changes made from the menu screens (home, Options, Credits, Space Dock).
//
// SceneManager.LoadScene is synchronous: the whole destination scene is read, awakened and
// integrated inside the frame of the tap, so the button press, the backdrop scroll and the music
// UI all freeze for the length of the load -- the "slight hiccup" on the way into the dock or
// Options. LoadSceneAsync reads and integrates the scene over several frames (the menu keeps
// drawing and scrolling while it loads) and activates it when ready; nothing else about the
// destination changes. Only the work that cannot be time-sliced (Awake/Start) remains in one frame.
//
// Only scenes left FROM a menu screen go async: a run (gameS1, tutorial) keeps simulating while an
// async load is pending, so leaving one stays synchronous. A second request while one is pending is
// ignored (a double tap used to queue two loads). Out of play (edit-mode suites) it is a plain
// LoadScene.
public static class MenuNav
{
    // Test seams: how an async load starts, and which scene is the active one.
    public static System.Func<string, AsyncOperation> StartAsync = name => SceneManager.LoadSceneAsync(name);
    public static System.Func<string> ActiveScene = () => SceneManager.GetActiveScene().name;
    public static System.Func<bool> Playing = () => Application.isPlaying;
    public static System.Action<string> LoadSync = name => SceneManager.LoadScene(name);

    public static bool Loading { get; private set; }
    public static string Pending { get; private set; }
    public static int AsyncLoads { get; private set; }
    public static int SyncLoads { get; private set; }

    public static bool FromMenu() { return MenuBackdropSelection.IsMenuScene(ActiveScene()); }

    public static void Load(string scene)
    {
        if (Loading) return;
        if (!Playing() || !FromMenu())
        {
            SyncLoads++;
            LoadSync(scene);
            return;
        }
        var op = StartAsync(scene);
        if (op == null) return;   // unknown scene: the engine has logged it; stay where we are
        Loading = true;
        Pending = scene;
        AsyncLoads++;
        op.completed += _ => Finish();
    }

    static void Finish() { Loading = false; Pending = null; }

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { if (mode == LoadSceneMode.Single) Finish(); }

    // Tests.
    public static void Reset()
    {
        Finish();
        AsyncLoads = SyncLoads = 0;
        StartAsync = name => SceneManager.LoadSceneAsync(name);
        ActiveScene = () => SceneManager.GetActiveScene().name;
        Playing = () => Application.isPlaying;
        LoadSync = name => SceneManager.LoadScene(name);
    }
}
