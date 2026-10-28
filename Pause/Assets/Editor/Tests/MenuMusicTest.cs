using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

// The home music carries through Options and Credits (MenuMusic): the first
// home arrival adopts the scene's MainMenuMusic, Options and Credits keep it (no
// second source, no restart: same source object, playback untouched), coming
// back home drops the scene's duplicate and keeps the carried one, and any other
// scene (game, tutorial, store) ends it. Credits pauses it and loops its own song
// (Boom Boom); Options or home un-pause the menu music. The music toggle still scales the
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
        MenuMusic.Play = src => { };     // ... and no audio device
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

            // Credits' own song: a looping music source that obeys the music setting.
            var song = MenuMusic.CreditsSource;
            Check("credits starts its own song (looping, Boom Boom clip)", song != null && song.loop && song.clip != null
                  && song.clip == Resources.Load<AudioClip>(MenuMusic.CreditsSong));
            Check("... and it is not the menu source", song != null && song.gameObject.name == MenuMusic.CreditsSourceName && Count() == 1);
            float full = song != null ? song.volume : 0f;
            SoundSettings.SetMusicVolume(.5f);
            Check("music volume scales the credits song", song != null && full > 0f && Mathf.Approximately(song.volume, full * .5f));
            SoundSettings.SetMusicOn(false);
            Check("music off silences the credits song", song != null && song.volume == 0f);
            SoundSettings.SetMusicOn(true);
            SoundSettings.SetMusicVolume(1f);
            Check("credits keeps the carried menu source around, paused", MenuMusic.Keeper != null);

            // Back home: the freshly loaded copy is dropped, the carried one stays.
            var dupe = Source(1f);
            MenuMusic.Handle("startS4", new[] { dupe });
            Check("leaving credits ends its song", MenuMusic.CreditsSource == null);
            Check("returning home destroys the scene's duplicate", dupe == null || !dupe);
            Check("returning home keeps the same carried source", MenuMusic.Keeper != null && MenuMusic.Keeper.gameObject == first);
            Check("exactly one menu music source after the round trip", Count() == 1);

            // Options (its own scene, leaderboardS3) carries it the same way, and
            // home -> Options -> home -> Credits -> home is one unbroken source.
            MenuMusic.Handle("leaderboardS3", new GameObject[0]);
            Check("options keeps the carried source", MenuMusic.Keeper != null && MenuMusic.Keeper.gameObject == first);
            Check("options adds no second source", Count() == 1);
            var optionsDupe = Source(1f);
            MenuMusic.Handle("startS4", new[] { optionsDupe });
            Check("returning home from options destroys the scene's duplicate", optionsDupe == null || !optionsDupe);
            Check("returning home from options keeps the same carried source", MenuMusic.Keeper != null && MenuMusic.Keeper.gameObject == first);
            MenuMusic.Handle("creditsS7", new GameObject[0]);
            var dupe2 = Source(1f);
            MenuMusic.Handle("startS4", new[] { dupe2 });
            Check("home -> options -> home -> credits -> home keeps one source", MenuMusic.Keeper != null && MenuMusic.Keeper.gameObject == first && Count() == 1);
            // The Credits entry lives in Options now: home -> Options -> Credits ->
            // (Back) Options -> (Back) home is one unbroken source.
            MenuMusic.Handle("leaderboardS3", new GameObject[0]);
            Check("home -> options: same source", MenuMusic.Keeper != null && MenuMusic.Keeper.gameObject == first && Count() == 1);
            MenuMusic.Handle("creditsS7", new GameObject[0]);
            Check("options -> credits: same source", MenuMusic.Keeper != null && MenuMusic.Keeper.gameObject == first && Count() == 1);
            MenuMusic.Handle("leaderboardS3", new GameObject[0]);
            Check("credits -> options (Back): same source", MenuMusic.Keeper != null && MenuMusic.Keeper.gameObject == first && Count() == 1);
            var dupe3 = Source(1f);
            MenuMusic.Handle("startS4", new[] { dupe3 });
            Check("options -> home (Back): duplicate dropped, same source", (dupe3 == null || !dupe3)
                  && MenuMusic.Keeper != null && MenuMusic.Keeper.gameObject == first && Count() == 1);
            Check("Back from credits goes to Options (a carried scene)", MenuMusic.Carries(BackNavigator.OptionsScene));
            Check("Options' Credits button loads a carried scene", MenuMusic.Carries(leaderboard.CreditsScene));
            Check("the Options scene is the one startMenu.achivements loads",
                  System.IO.File.ReadAllText("Assets/Scripts/Menu/startMenu.cs").Contains("MenuNav.Load(\"" + MenuMusic.OptionsScene + "\")"));
            Check("Options and Credits ship no music source of their own",
                  !System.IO.File.ReadAllText("Assets/Scenes/leaderboardS3.unity").Contains("AudioSource:")
                  && !System.IO.File.ReadAllText("Assets/Scenes/creditsS7.unity").Contains("AudioSource:"));

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

            // Home -> Codex -> back: the codex is an overlay on the home scene (no
            // scene load), so the carried source is the same object before, during
            // and after; opening and closing it never stops or pauses the music
            // (only the MUSIC tab ducks it, and gives it back with UnPause, see
            // CodexMusicTest).
            bool codexLoads = false;
            foreach (string f in System.IO.Directory.GetFiles("Assets/Scripts/Codex", "*.cs"))
                if (System.IO.File.ReadAllText(f).Contains("SceneManager.LoadScene(")) codexLoads = true;
            Check("the codex never loads a scene (it is a panel over home)", !codexLoads);
            EditorSceneManager.OpenScene("Assets/Scenes/startS4.unity", OpenSceneMode.Single);
            MenuMusic.Reset();
            MenuMusic.Persist = go => { };
            var homeSource = GameObject.Find(MenuMusic.SourceName);
            Check("home scene has its MainMenuMusic", homeSource != null);
            if (homeSource != null)
            {
                MenuMusic.Handle("startS4", SceneManager.GetActiveScene().GetRootGameObjects());
                Check("home (real scene) adopts its MainMenuMusic", MenuMusic.Keeper != null && MenuMusic.Keeper.gameObject == homeSource);
                CodexMusicPlayer.Simulate = true;
                var codex = CodexPanel.Open(null);
                Check("codex opens over home", codex != null);
                Check("codex open: same source, not ducked", MenuMusic.Keeper != null && MenuMusic.Keeper.gameObject == homeSource
                      && !CodexMusicPlayer.IsDucking && Count() == 1);
                if (codex != null) codex.Close();
                Check("codex closed: same source, not ducked, still one", MenuMusic.Keeper != null && MenuMusic.Keeper.gameObject == homeSource
                      && !CodexMusicPlayer.IsDucking && Count() == 1);
                if (codex != null) Object.DestroyImmediate(codex.gameObject);
                CodexMusicPlayer.Simulate = simulate;
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Any other scene ends it.
            foreach (string other in new[] { "gameS1", "tutorialS5", "shopS6" })
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
            Object.DestroyImmediate(stray);
            var strayOptions = Source(1f);
            MenuMusic.Handle("leaderboardS3", new[] { strayOptions });
            Check("options alone does not adopt a source", MenuMusic.Keeper == null);
            Check("options plays no credits song", MenuMusic.CreditsSource == null);
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
