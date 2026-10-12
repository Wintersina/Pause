using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

// The home music carries through Credits (MenuMusic): the first home arrival
// adopts the scene's MainMenuMusic, Credits keeps it (no second source, no
// restart: same source object, playback untouched), coming back home drops the
// scene's duplicate and keeps the carried one, and any other scene (game,
// tutorial, store, leaderboard) ends it. The music toggle still scales the
// carried source through SoundSettings, and the Codex music tab still ducks
// and un-ducks it. Scene arrivals are simulated by calling MenuMusic.Handle.
public static class MenuMusicTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[MMU] PASS  " : "[MMU] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static GameObject Source(float volume)
    {
        var go = new GameObject(MenuMusic.SourceName);
        var a = go.AddComponent<AudioSource>();
        a.playOnAwake = false;
        a.loop = true;
        a.volume = volume;
        return go;
    }

    static int Count()
    {
        int n = 0;
        foreach (var a in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (a.gameObject.name == MenuMusic.SourceName) n++;
        return n;
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        MenuMusic.Reset();
        MenuMusic.Persist = go => { };   // edit mode: no DontDestroyOnLoad
        bool simulate = CodexMusicPlayer.Simulate;
        PlayerPrefs.DeleteKey(SoundSettings.MusicOnKey);
        PlayerPrefs.DeleteKey(SoundSettings.MusicVolumeKey);
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Home loads for the first time: adopted.
            var first = Source(1f);
            MenuMusic.Handle("startS4", new[] { first });
            Check("home adopts its music source", MenuMusic.Keeper == first.GetComponent<AudioSource>());

            // Credits: nothing of its own, the carried source stays.
            MenuMusic.Handle("creditsS7", new GameObject[0]);
            Check("credits keeps the carried source", MenuMusic.Keeper != null && MenuMusic.Keeper.gameObject == first);
            Check("credits adds no second source", Count() == 1);

            // Back home: the freshly loaded copy is dropped, the carried one stays.
            var dupe = Source(1f);
            MenuMusic.Handle("startS4", new[] { dupe });
            Check("returning home destroys the scene's duplicate", dupe == null || !dupe);
            Check("returning home keeps the same carried source", MenuMusic.Keeper != null && MenuMusic.Keeper.gameObject == first);
            Check("exactly one menu music source after the round trip", Count() == 1);

            // Codex music tab: ducks and resumes the carried source (found by name).
            CodexMusicPlayer.Simulate = true;
            Check("codex finds the carried source by name", GameObject.Find(CodexMusic.MenuSourceName) == first);
            CodexMusicPlayer.Simulate = simulate;

            // Music setting still scales it.
            SoundSettings.RegisterMusic(MenuMusic.Keeper);
            SoundSettings.SetMusicVolume(.5f);
            Check("music volume scales the carried source", Mathf.Approximately(MenuMusic.Keeper.volume, .5f));
            SoundSettings.SetMusicOn(false);
            Check("music off silences the carried source", MenuMusic.Keeper.volume == 0f);
            SoundSettings.SetMusicOn(true);
            Check("music on restores it", Mathf.Approximately(MenuMusic.Keeper.volume, .5f));

            // Any other scene ends it.
            foreach (string other in new[] { "gameS1", "tutorialS5", "shopS6", "leaderboardS3" })
            {
                MenuMusic.Reset();
                MenuMusic.Persist = go => { };
                var h = Source(1f);
                MenuMusic.Handle("startS4", new[] { h });
                MenuMusic.Handle(other, new GameObject[0]);
                Check(other + " does not carry the home music", MenuMusic.Keeper == null);
            }

            // Credits first (no home before it) never adopts anything.
            MenuMusic.Reset();
            MenuMusic.Persist = go => { };
            var stray = Source(1f);
            MenuMusic.Handle("creditsS7", new[] { stray });
            Check("credits alone does not adopt a source", MenuMusic.Keeper == null);
        }
        finally
        {
            SoundSettings.SetMusicVolume(1f);
            PlayerPrefs.DeleteKey(SoundSettings.MusicOnKey);
            PlayerPrefs.DeleteKey(SoundSettings.MusicVolumeKey);
            CodexMusicPlayer.Simulate = simulate;
            MenuMusic.Reset();
        }
        Debug.Log("[MMU] failures: " + fails);
        return fails;
    }
}
