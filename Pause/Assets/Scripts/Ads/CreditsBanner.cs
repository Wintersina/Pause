using UnityEngine;
using UnityEngine.SceneManagement;

// A banner ad at the bottom of the Credits screen (creditsS7), under the same rules as the run-end banner (AdPolicy:
// never for Remove Ads / Paragon owners, never inside the first ad-free minutes; developer mode skips the window).
// The layout keeps clear of it: SafeAreaClamp lifts BACK above it and CreditsMove's roll never bounces into it
// (both read AdManager.BannerReserveUnits). It is hidden when the screen closes, when the app is paused (and put back on
// resume) and on any scene change, so it never leaks onto the title screen or into a run.
public sealed class CreditsBanner : MonoBehaviour
{
    public const string SceneName = "creditsS7";
    public const string ObjectName = "~CreditsBanner";

    bool started;

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Attach(SceneManager.GetActiveScene().name);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { Attach(scene.name); }

    public static CreditsBanner Attach(string sceneName)
    {
        if (sceneName != SceneName) return null;
        var existing = FindFirstObjectByType<CreditsBanner>();
        if (existing != null) return existing;
        return new GameObject(ObjectName).AddComponent<CreditsBanner>();
    }

    // Start, not OnEnable: the ad runner's scene-change handler hides banners, and it must have run first.
    void Start() { started = true; Show(); }

    void OnEnable() { if (started) Show(); }
    void OnDisable() { Hide(); }
    void OnDestroy() { Hide(); }

    void OnApplicationPause(bool paused)
    {
        if (paused) Hide(); else if (started && isActiveAndEnabled) Show();
    }

    public void Show() { AdManager.TryShowMenuBanner(); }
    public void Hide() { AdManager.HideBanner(); }
}
