using UnityEngine;
using UnityEngine.SceneManagement;

// Keeps a scene-authored UI element inside the screen's safe area.
//
// Several scenes' canvases scale with the screen's WIDTH (800x600 reference,
// match 0) and place an element a fixed distance below the centre: the
// credits / options BACK button (-533 / -433) and the run's "Place finger on
// screen." prompt (-614). That holds on any phone (the canvas is >= 1400
// units tall), but on a wide screen -- a foldable's inner display, a tablet,
// an iPad -- the canvas is only ~960-1070 units tall and the element ends
// up under the bottom edge. This moves it up just enough to sit `margin`
// units inside the safe area (clear of the home indicator / nav bar), and
// leaves it exactly where it was authored whenever it already fits.
public class SafeAreaClamp : MonoBehaviour
{
    // Scene object names this is attached to on load (like CameraFit's
    // BackdropNames): only elements that are centre-anchored with a big fixed
    // offset need it.
    public static readonly string[] Names = { "BackButton", "placeFingerHereText" };
    public static readonly string[] Scenes = { "creditsS7", "leaderboardS3", "gameS1", "tutorialS5" };
    public const float Margin = 16f;

    RectTransform rect;
    Vector2 authored;
    bool captured;
    int lastW = -1, lastH = -1;
    Rect lastSafe;

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        AttachAll(SceneManager.GetActiveScene().name);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { AttachAll(scene.name); }

    // Adds the clamp to this scene's listed elements and applies it at once.
    public static void AttachAll(string sceneName)
    {
        if (System.Array.IndexOf(Scenes, sceneName) < 0) return;
        foreach (string name in Names)
        {
            var go = SceneUtil.FindAny(name);
            if (go == null || !(go.transform is RectTransform)) continue;
            var c = go.GetComponent<SafeAreaClamp>() ?? go.AddComponent<SafeAreaClamp>();
            c.Apply();
        }
    }

    void LateUpdate()
    {
        if (ScreenInfo.Width != lastW || ScreenInfo.Height != lastH || ScreenInfo.SafeArea != lastSafe) Apply();
    }

    public void Apply()
    {
        if (rect == null) rect = transform as RectTransform;
        var canvas = GetComponentInParent<Canvas>();
        if (rect == null || canvas == null) return;
        if (!captured) { authored = rect.anchoredPosition; captured = true; }
        lastW = ScreenInfo.Width;
        lastH = ScreenInfo.Height;
        lastSafe = ScreenInfo.SafeArea;

        var parent = rect.parent as RectTransform;
        float sf = Mathf.Max(canvas.rootCanvas.scaleFactor, .0001f);
        if (parent == null || parent != canvas.rootCanvas.transform) { rect.anchoredPosition = authored; return; }
        Rect area = parent.rect;   // canvas units, centre origin
        float safeBottom = area.yMin + ScreenInfo.SafeBottomInset / sf + Margin;
        float safeTop = area.yMax - ScreenInfo.SafeTopInset / sf - Margin;
        float anchorY = Mathf.Lerp(area.yMin, area.yMax, rect.anchorMin.y);
        float h = rect.rect.height;
        float bottom = anchorY + authored.y - rect.pivot.y * h;
        float y = authored.y;
        if (bottom < safeBottom) y += safeBottom - bottom;
        if (y + anchorY + (1f - rect.pivot.y) * h > safeTop) y = Mathf.Min(authored.y, safeTop - anchorY - (1f - rect.pivot.y) * h);
        rect.anchoredPosition = new Vector2(authored.x, y);
    }
}
