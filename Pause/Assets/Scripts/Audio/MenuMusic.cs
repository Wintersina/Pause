using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Keeps the home screen's music playing, uninterrupted, across the Options and
// Credits screens and back.
//
// The Codex is an overlay inside the home scene (it never reloads it), but
// Options (leaderboardS3) and Credits (creditsS7) are their own scenes and Back
// returns to a freshly loaded home scene, each of which would otherwise cut the
// music and restart it. So the home scene's "MainMenuMusic" source is adopted the
// first time home loads (DontDestroyOnLoad) and carried through the menu scenes
// in CarriedScenes; when home loads again the scene's own duplicate source is
// destroyed on the spot and the carried one keeps playing from where it was. Any
// other scene (game, tutorial, store) ends the carried music, so it never leaks
// into gameplay or the store (which has its own StoreMusic) and the next home
// arrival starts the track fresh.
//
// Credits has its own song ("Boom Boom", CreditsSong): arriving there pauses the
// carried menu music and loops the song; any other scene stops it, and Options
// or home un-pause the menu music where it left off. The song source is created
// at runtime (the Credits scene itself ships no AudioSource) and dies with the
// scene.
//
// Volume stays under SoundSettings (the source is registered as music there, so
// the MUSIC toggle/slider keep applying); pausing on app focus loss is the
// platform's, as before. The Codex MUSIC tab still finds the source by name and
// pauses/resumes it.
public static class MenuMusic
{
    public const string SourceName = CodexMusic.MenuSourceName;
    public const string HomeScene = "startS4";
    public const string CreditsScene = "creditsS7";
    public const string OptionsScene = "leaderboardS3";
    public const string CreditsSourceName = "CreditsMusic";
    public const string CreditsSong = "CreditsMusic/credits_boom_boom";   // Resources path

    /// <summary>Menu scenes (besides home) the home music plays on through.</summary>
    public static readonly string[] CarriedScenes = { CreditsScene, OptionsScene };

    public static bool Carries(string sceneName)
    {
        return sceneName == HomeScene || Array.IndexOf(CarriedScenes, sceneName) >= 0;
    }

    // Tests (edit mode forbids DontDestroyOnLoad).
    public static Action<GameObject> Persist = DefaultPersist;
    static void DefaultPersist(GameObject go) { UnityEngine.Object.DontDestroyOnLoad(go); }

    // Tests (edit mode has no audio device to start playback on).
    public static Action<AudioSource> Play = DefaultPlay;
    static void DefaultPlay(AudioSource src) { src.Play(); }

    static AudioSource keeper;
    static AudioSource credits;
    static bool menuPaused;

    /// <summary>The carried home music source, or null when none is being carried.</summary>
    public static AudioSource Keeper { get { return keeper; } }

    /// <summary>The Credits song's source while the Credits scene is up, else null.</summary>
    public static AudioSource CreditsSource { get { return credits; } }

    public static void Reset()
    {
        keeper = null; menuPaused = false; Persist = DefaultPersist; Play = DefaultPlay;
        if (credits != null) UnityEngine.Object.DestroyImmediate(credits.gameObject);
        credits = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        keeper = null; credits = null; menuPaused = false;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        Handle(s.name, s.GetRootGameObjects());
    }

    /// <summary>One scene arrived (roots are its root objects).</summary>
    public static void Handle(string sceneName, IEnumerable<GameObject> roots)
    {
        Carry(sceneName, roots);
        if (sceneName == CreditsScene) StartCredits(); else EndCredits();
    }

    // Credits: the menu music steps aside (kept, paused) for the song.
    static void StartCredits()
    {
        if (keeper != null && !menuPaused) { keeper.Pause(); menuPaused = true; }
        if (credits != null) return;
        var clip = Resources.Load<AudioClip>(CreditsSong);
        if (clip == null) return;
        var go = new GameObject(CreditsSourceName);
        credits = go.AddComponent<AudioSource>();
        credits.clip = clip;
        credits.loop = true;
        credits.playOnAwake = false;
        credits.spatialBlend = 0f;
        SoundSettings.MarkMusic(credits);
        SoundSettings.RegisterMusic(credits);   // MUSIC toggle / slider apply
        Play(credits);
        Codex.HearTrack(CodexMusic.CreditsId);
    }

    // Anywhere else: the song stops (its object goes with the scene) and the
    // menu music, if it was stepped aside, carries on from where it was.
    static void EndCredits()
    {
        if (credits != null) UnityEngine.Object.Destroy(credits.gameObject);
        credits = null;
        if (menuPaused && keeper != null) keeper.UnPause();
        menuPaused = false;
    }

    static void Carry(string sceneName, IEnumerable<GameObject> roots)
    {
        if (!Carries(sceneName))
        {
            if (keeper != null) UnityEngine.Object.Destroy(keeper.gameObject);
            keeper = null;
            return;
        }

        GameObject own = null;
        foreach (var r in roots) if (r != null && r.name == SourceName) { own = r; break; }
        if (own == null) return;

        if (keeper != null)
        {
            // Already carrying the music: drop the scene's own copy before it is heard.
            if (own != keeper.gameObject) UnityEngine.Object.DestroyImmediate(own);
            return;
        }
        if (sceneName != HomeScene) return;
        var src = own.GetComponent<AudioSource>();
        if (src == null) return;
        Persist(own);
        keeper = src;
    }
}
