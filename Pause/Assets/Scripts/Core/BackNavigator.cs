using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// The one "Back" for the whole app: every on-screen Back button and the
// Android system back (Unity reports it as KeyCode.Escape) call Back(), and
// each press undoes exactly one level.
//
// UI layers that can be backed out of (the dock's ship selection, the codex,
// the Options leaderboard panel) Register a handler while they are up. Back()
// asks them newest-first; the first one that returns true consumed the press.
// When none does, the scene's root fallback runs:
//
//   startS4 (home)                          quit, behind "press back again"
//   shopS6 / leaderboardS3 / creditsS7      home
//   gameS1 (flying or dead)                 home, exactly like the Home
//                                           quick action / death-panel MENU
//   tutorialS5                              home (the tutorial's Home action;
//                                           the tutorial stays unfinished)
//
// Escape is read in exactly one place (BackNavigatorRunner) and at most once
// per frame, so no two scripts can react to the same press.
//
// iOS has no system back and apps must not quit themselves: there the home
// root does nothing and QuitAllowed is false (startMenu hides its Quit button).
public static class BackNavigator
{
    public const string HomeScene = "startS4";
    // How long the first back on the home screen keeps quitting armed.
    public const float QuitWindow = 2f;

    class Layer
    {
        public object owner;
        public Func<bool> handler;
    }

    static readonly List<Layer> layers = new List<Layer>();
    static int escapeFrame = -1;
    static bool quitArmed;
    static float quitArmedAt;

    // ---- hooks (tests stub these; the defaults are the real thing) ----

    public static Action<string> LoadScene = DefaultLoadScene;
    public static Action QuitApplication = DefaultQuit;
    public static Func<RuntimePlatform> Platform = DefaultPlatform;
    public static Func<float> Clock = DefaultClock;

    // Raised when the first home back arms quitting (the toast listens).
    public static event Action QuitArmed;

    static void DefaultLoadScene(string scene) { SceneManager.LoadScene(scene); }
    static void DefaultQuit() { Application.Quit(); }
    static RuntimePlatform DefaultPlatform() { return Application.platform; }
    static float DefaultClock() { return Time.unscaledTime; }

    public static void ResetHooks()
    {
        LoadScene = DefaultLoadScene;
        QuitApplication = DefaultQuit;
        Platform = DefaultPlatform;
        Clock = DefaultClock;
    }

    // ---- the stack ----

    // Push (or move to the top) a back handler. The handler returns true if
    // it consumed the press. A destroyed UnityEngine.Object owner drops out
    // by itself.
    public static void Register(object owner, Func<bool> handler)
    {
        if (owner == null || handler == null) return;
        Unregister(owner);
        layers.Add(new Layer { owner = owner, handler = handler });
    }

    public static void Unregister(object owner)
    {
        for (int i = layers.Count - 1; i >= 0; i--)
            if (ReferenceEquals(layers[i].owner, owner)) layers.RemoveAt(i);
    }

    public static bool IsRegistered(object owner)
    {
        Prune();
        foreach (var l in layers) if (ReferenceEquals(l.owner, owner)) return true;
        return false;
    }

    // The newest live handler's owner (null if none).
    public static object Top
    {
        get
        {
            Prune();
            return layers.Count > 0 ? layers[layers.Count - 1].owner : null;
        }
    }

    public static int LayerCount { get { Prune(); return layers.Count; } }

    static bool Dead(object owner)
    {
        var unityObject = owner as UnityEngine.Object;
        return owner == null || (!ReferenceEquals(unityObject, null) && unityObject == null);
    }

    static void Prune()
    {
        for (int i = layers.Count - 1; i >= 0; i--)
            if (Dead(layers[i].owner)) layers.RemoveAt(i);
    }

    // ---- the press ----

    // One Back press: the topmost layer that wants it, else the scene root.
    // Wired to on-screen Back buttons (menuButton.back, everythingCredit.back).
    public static void Back()
    {
        Prune();
        // Snapshot: handlers may unregister themselves while handling.
        var snapshot = layers.ToArray();
        for (int i = snapshot.Length - 1; i >= 0; i--)
        {
            if (Dead(snapshot[i].owner)) continue;
            if (snapshot[i].handler()) return;
        }
        Root(SceneManager.GetActiveScene().name);
    }

    // Escape / Android back for a given frame. Returns false (and does
    // nothing) if this frame's press was already handled.
    public static bool HandleEscape(int frame)
    {
        if (frame == escapeFrame) return false;
        escapeFrame = frame;
        Back();
        return true;
    }

    // The scene's fallback once every layer has passed.
    public static void Root(string scene)
    {
        switch (scene)
        {
            case HomeScene:
                HomeBack();
                break;
            case "shopS6":
            case "leaderboardS3":
            case "creditsS7":
            case "gameS1":
            case "tutorialS5":
                GoHome();
                break;
            default:
                // Splash: nothing to go back to.
                break;
        }
    }

    // Leave for the home screen: the same thing every MENU / Home / Back
    // button has always done (hide any ad, clear run state, load startS4).
    public static void GoHome()
    {
        if (AdMob.isAdsShowwing) AdMob.hide();
        GameStateReset.Clear();
        LoadScene(HomeScene);
    }

    // ---- quitting ----

    public static bool QuitAllowed
    {
        get { return Platform() != RuntimePlatform.IPhonePlayer; }
    }

    public static bool QuitIsArmed
    {
        get { return quitArmed && Clock() - quitArmedAt <= QuitWindow; }
    }

    // Home root: the first back arms quitting and shows the toast; a second
    // one within QuitWindow quits. Never quits on iOS.
    public static void HomeBack()
    {
        if (!QuitAllowed) return;
        if (QuitIsArmed)
        {
            Disarm();
            QuitNow();
            return;
        }
        quitArmed = true;
        quitArmedAt = Clock();
        if (QuitArmed != null) QuitArmed();
        BackQuitToast.Show();
    }

    public static void Disarm() { quitArmed = false; }

    // A deliberate quit (the home Quit button). No-op on iOS.
    public static void QuitNow()
    {
        if (!QuitAllowed) return;
        if (AdMob.isAdsShowwing) AdMob.hide();
        GameStateReset.Clear();
        QuitApplication();
    }

    // ---- runtime wiring ----

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        if (UnityEngine.Object.FindFirstObjectByType<BackNavigatorRunner>() != null) return;
        var go = new GameObject("~BackNavigator");
        go.hideFlags = HideFlags.HideInHierarchy;
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<BackNavigatorRunner>();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single) Disarm();
        Prune();
    }
}
