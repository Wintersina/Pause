using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// A small always-reachable leave/replay pair pinned to the top-right corner.
// It is shared by an ordinary pause and the death screen so the controls keep
// the same place and remain above the death dialog's raycast blocker.
//
// Clones of replayWhenPausedButton/mainMenuWhenPausedButton rather than new
// buttons from scratch, so they inherit the same Button.onClick wiring
// straight to the existing buttonClicks instance -- Instantiate() does not
// touch a persistent listener's reference to an object outside the cloned
// hierarchy, so that wiring carries over intact. The originals remain hidden
// templates. The live copies sit on their own high-sorting canvas, above both
// gameplay and the death popup.
//
// Icons: the templates' sprites (a 512x478 clip-art "redo" arrow and a
// padded hamburger) were squeezed into a 64x58 rect without preserveAspect,
// so they rendered stretched, at two different optical sizes, and blurred by
// mipmaps + compression. The live copies now use a matched pair rasterized
// from SVG sources (Assets/Art/UI/Icons/src~, see render.sh there):
// square, same plate, same stroke weight, uncompressed and mip-free.
public class PauseQuickActions : MonoBehaviour
{
    // Reference-resolution units on an 800x1000 ScaleWithScreenSize canvas
    // (match 0.5). On a 390x844pt phone that is ~46pt per button, above the
    // ~44pt minimum comfortable tap target.
    public const float ButtonSize = 72f;
    const float ButtonGap = 14f;
    const float EdgeMargin = 18f;

    public const string ReplayIconPath = "QuickActions/QuickAction_replay";
    public const string HomeIconPath = "QuickActions/QuickAction_home";

    GameObject replayClone, leaveClone;
    RectTransform safeArea;
    Rect appliedSafeArea;
    Vector2Int appliedScreen;

    // Geometric hit-test against the live buttons' rects (the whole square is
    // the tap target, not just the glyph's opaque pixels), independent of
    // EventSystem timing so gameplay input can ignore presses on the actions.
    public static bool IsScreenPointOnAction(Vector2 screenPoint)
    {
        var actions = Object.FindFirstObjectByType<PauseQuickActions>();
        return actions != null && actions.Contains(screenPoint);
    }

    bool Contains(Vector2 screenPoint)
    {
        return Contains(replayClone, screenPoint) || Contains(leaveClone, screenPoint);
    }

    static bool Contains(GameObject go, Vector2 screenPoint)
    {
        if (go == null || !go.activeInHierarchy) return false;
        var rect = go.GetComponent<RectTransform>();
        return rect != null && RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, null);
    }

    void Start()
    {
        var replaySource = SceneUtil.FindAny("replayWhenPausedButton");
        var leaveSource = SceneUtil.FindAny("mainMenuWhenPausedButton");
        if (replaySource == null || leaveSource == null) return;

        var holder = new GameObject("RunActionCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = holder.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;
        var scaler = holder.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(800f, 1000f);
        scaler.matchWidthOrHeight = 0.5f;

        // Notch / status-bar / rounded-corner aware container.
        var safeGo = new GameObject("SafeArea", typeof(RectTransform));
        safeGo.transform.SetParent(holder.transform, false);
        safeArea = safeGo.GetComponent<RectTransform>();
        ApplySafeArea();

        replayClone = Instantiate(replaySource, safeArea);
        leaveClone = Instantiate(leaveSource, safeArea);
        replayClone.name = "replayQuickAction";
        leaveClone.name = "leaveQuickAction";

        // Replay in the corner, Home to its left.
        PositionTopRight(replayClone, 0);
        PositionTopRight(leaveClone, 1);
        ApplyIcon(replayClone, ReplayIconPath);
        ApplyIcon(leaveClone, HomeIconPath);
    }

    static void PositionTopRight(GameObject go, int slotFromRight)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.localScale = Vector3.one;
        rt.sizeDelta = new Vector2(ButtonSize, ButtonSize);
        rt.anchoredPosition = new Vector2(-EdgeMargin - slotFromRight * (ButtonSize + ButtonGap), -EdgeMargin);
    }

    static void ApplyIcon(GameObject go, string resourcePath)
    {
        var image = go.GetComponent<Image>();
        if (image == null) return;
        var sprite = Resources.Load<Sprite>(resourcePath);
        if (sprite != null) image.sprite = sprite;
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.color = Color.white;
        // Default rect-based raycast (not alpha-tested): taps anywhere in the
        // square count, matching IsScreenPointOnAction.
        image.raycastTarget = true;
    }

    void ApplySafeArea()
    {
        if (safeArea == null) return;
        Rect area = Screen.safeArea;
        var screen = new Vector2Int(Screen.width, Screen.height);
        if (area == appliedSafeArea && screen == appliedScreen) return;
        appliedSafeArea = area;
        appliedScreen = screen;

        if (screen.x <= 0 || screen.y <= 0)
        {
            safeArea.anchorMin = Vector2.zero;
            safeArea.anchorMax = Vector2.one;
        }
        else
        {
            safeArea.anchorMin = new Vector2(area.xMin / screen.x, area.yMin / screen.y);
            safeArea.anchorMax = new Vector2(area.xMax / screen.x, area.yMax / screen.y);
        }
        safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
    }

    void Update()
    {
        ApplySafeArea();

        // Once the pause stock is empty the run continues even with no finger
        // on screen. Leave remains available in that state as well.
        bool stoppedTouching = !TouchInput.IsPressed;
        bool show = buttonClicks.playerDied || (!buttonClicks.playerDied && stoppedTouching);
        if (replayClone != null) replayClone.SetActive(show);
        if (leaveClone != null) leaveClone.SetActive(show);
    }
}

public static class PauseQuickActionsBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "gameS1") return;
        if (Object.FindFirstObjectByType<PauseQuickActions>() != null) return;
        new GameObject("~PauseQuickActions").AddComponent<PauseQuickActions>();
    }
}
