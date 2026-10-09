using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// A small always-reachable leave/replay pair pinned to the top-right corner.
// It is shared by an ordinary pause and the death screen so the controls keep
// the same place and remain above the death dialog's raycast blocker.
//
// Clones of replayWhenPausedButton/mainMenuWhenPausedButton rather than new
// buttons from scratch. The originals remain hidden templates. The live
// copies sit on their own high-sorting canvas, above both gameplay and the
// death popup, and call buttonClicks directly (see Start).
//
// Icons: the templates' sprites (a 512x478 clip-art "redo" arrow and a
// padded hamburger) were squeezed into a non-square rect without
// preserveAspect, so they rendered stretched, at two different optical sizes,
// and blurred by mipmaps + compression. The live copies use a matched pair
// rasterized from SVG sources (Assets/Art/UI/Icons/src~, see render.sh
// there): square, same plate, same stroke weight, uncompressed and mip-free.
public class PauseQuickActions : MonoBehaviour
{
    // Reference-resolution units on an 800x1000 ScaleWithScreenSize canvas
    // (match 0.5). On a 390x844pt phone that is ~46pt per button, above the
    // ~44pt minimum comfortable tap target; on smaller ones UiScale's floor
    // raises the canvas so it never drops below 44 pt / 48 dp.
    public const float ButtonSize = 72f;
    public const float ButtonGap = 10f;
    // The least the band keeps clear of the safe area's side edges. Where
    // the side rails are, the band ends inside them instead (TopBand), the
    // buttons at its right end and HudStyler's score read-out at its left,
    // sharing one top edge.
    public const float EdgeMargin = 18f;
    // Inset from the safe area's top edge, shared by the buttons and the
    // score read-out: the whole top band sits 20% of a button higher than
    // the side margin, still inside the safe area (and lower still under a
    // display cutout: TopBand).
    public const float TopMargin = EdgeMargin - .2f * ButtonSize;
    // The buttons are never scaled down: on a 393x852pt phone they are
    // ~46pt, just above the smallest comfortable tap target.
    public const float MinTapPoints = 44f;
    public static readonly Vector2 ReferenceResolution = new Vector2(800f, 1000f);
    public const float MatchWidthOrHeight = 0.5f;

    public const string ReplayIconPath = "QuickActions/QuickAction_replay";
    public const string HomeIconPath = "QuickActions/QuickAction_home";

    GameObject replayClone, leaveClone;
    RectTransform safeArea;
    Rect appliedSafeArea;
    Vector2Int appliedScreen;
    TopBand.Frame appliedBand;
    bool bandApplied;

    // Player movement runs separately from Unity UI. This geometric check is
    // deliberately independent of EventSystem timing, so the first press on
    // a pause button cannot also be interpreted as a teleport destination.
    // It tests each button's whole square rect (the tap target), not just
    // the glyph's opaque pixels.
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

    // Screen pixels per canvas unit on RunActionCanvas for a given screen size
    // (CanvasScaler ScaleWithScreenSize, MatchWidthOrHeight).
    // Raised to UiScale's floor for a ButtonSize target on the device's
    // density, so a button is never under 44 pt / 48 dp (on a small phone
    // the buttons, and the band's margins with them, grow).
    public static float CanvasScaleFor(Vector2 screen)
    {
        return UiScale.Apply(HudStyler.ScaleWithScreenSize(screen, ReferenceResolution,
            CanvasScaler.ScreenMatchMode.MatchWidthOrHeight, MatchWidthOrHeight), ButtonSize, 0f);
    }

    // True when UiScale's floor, not the screen's pixels, sets the buttons'
    // size on this screen (a phone small in points / dp).
    public static bool RaisedByFloor(Vector2 screen)
    {
        float pixel = HudStyler.ScaleWithScreenSize(screen, ReferenceResolution,
            CanvasScaler.ScreenMatchMode.MatchWidthOrHeight, MatchWidthOrHeight);
        return UiScale.Floor(ButtonSize, 0f) > pixel + 1e-5f;
    }

    // The screen-pixel rect (origin bottom-left) covered by both buttons for
    // a given safe area and screen: what the HUD must keep clear of.
    public static Rect ScreenRectFor(Rect safeArea, Vector2 screen)
    {
        return ScreenRectFor(TopBand.FrameFor(safeArea, screen), screen);
    }

    // The same, at the right end of a given band.
    public static Rect ScreenRectFor(TopBand.Frame band, Vector2 screen)
    {
        float s = CanvasScaleFor(screen);
        float width = (2f * ButtonSize + ButtonGap) * s;
        float height = ButtonSize * s;
        return new Rect(band.right - width, band.top - height, width, height);
    }

    // One button's screen-pixel rect: 0 is Replay (rightmost), 1 is Home.
    public static Rect ButtonScreenRect(TopBand.Frame band, Vector2 screen, int slotFromRight)
    {
        float s = CanvasScaleFor(screen);
        float right = band.right - slotFromRight * (ButtonSize + ButtonGap) * s;
        return new Rect(right - ButtonSize * s, band.top - ButtonSize * s, ButtonSize * s, ButtonSize * s);
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
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = MatchWidthOrHeight;
        // never under 44 pt / 48 dp a button (UiScale's floor; CanvasScaleFor)
        UiScaleFloor.Configure(scaler, ReferenceResolution, ButtonSize, 0f);

        // Notch / status-bar / rounded-corner aware container.
        var safeGo = new GameObject("SafeArea", typeof(RectTransform));
        safeGo.transform.SetParent(holder.transform, false);
        safeArea = safeGo.GetComponent<RectTransform>();
        ApplySafeArea();

        replayClone = Instantiate(replaySource, safeArea);
        leaveClone = Instantiate(leaveSource, safeArea);
        replayClone.name = "replayQuickAction";
        leaveClone.name = "leaveQuickAction";

        // Replay at the band's right end, Home to its left.
        PlaceButtons();

        var clicks = Object.FindFirstObjectByType<buttonClicks>();
        var replay = replayClone.GetComponent<Button>();
        var leave = leaveClone.GetComponent<Button>();
        if (clicks != null)
        {
            // The old persistent listeners lived behind a paused/death canvas
            // and were easy to block. These live copies call the action
            // directly, above every gameplay raycast blocker.
            //
            // A fresh event each, not RemoveAllListeners: Instantiate copies
            // the source's PERSISTENT onClick (buttonClicks.replay /
            // mainMenuButton, wired in the scene), which RemoveAllListeners
            // never touches -- so every tap ran the action twice and loaded
            // the scene twice back to back.
            if (replay != null)
            {
                replay.onClick = new Button.ButtonClickedEvent();
                replay.onClick.AddListener(clicks.replay);
            }
            if (leave != null)
            {
                leave.onClick = new Button.ButtonClickedEvent();
                leave.onClick.AddListener(clicks.mainMenuButton);
            }
        }
        StyleIcon(replayClone, ReplayIconPath);
        StyleIcon(leaveClone, HomeIconPath);
    }

    // Re-placed whenever the band moves: screen, safe area, cutouts, or the
    // rails' inner edge (a world's rails being painted).
    void PlaceButtons()
    {
        var screen = new Vector2(Screen.width, Screen.height);
        if (screen.x <= 0f || screen.y <= 0f) return;
        Rect safe = Screen.safeArea;
        var band = TopBand.FrameFor(safe, screen);
        if (bandApplied && band.Same(appliedBand)) return;
        appliedBand = band;
        bandApplied = true;
        Vector2 offset = TopRightOffset(band, safe, screen);
        PositionTopRight(replayClone, 0, offset);
        PositionTopRight(leaveClone, 1, offset);
    }

    // For previews and tests: lay the buttons out for a given screen rather
    // than the live one.
    public void PlaceFor(Rect safe, Vector2 screen, TopBand.Frame band)
    {
        if (safeArea == null || screen.x <= 0f || screen.y <= 0f) return;
        safeArea.anchorMin = new Vector2(safe.xMin / screen.x, safe.yMin / screen.y);
        safeArea.anchorMax = new Vector2(safe.xMax / screen.x, safe.yMax / screen.y);
        safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
        appliedBand = band;
        bandApplied = true;
        Vector2 offset = TopRightOffset(band, safe, screen);
        PositionTopRight(replayClone, 0, offset);
        PositionTopRight(leaveClone, 1, offset);
    }

    // The band's top-right corner relative to the safe area's, in canvas units.
    public static Vector2 TopRightOffset(TopBand.Frame band, Rect safe, Vector2 screen)
    {
        float s = Mathf.Max(CanvasScaleFor(screen), .0001f);
        return new Vector2((band.right - safe.xMax) / s, (band.top - safe.yMax) / s);
    }

    static void PositionTopRight(GameObject go, int slotFromRight, Vector2 offset)
    {
        if (go == null) return;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.localScale = Vector3.one;
        rt.sizeDelta = new Vector2(ButtonSize, ButtonSize);
        rt.anchoredPosition = new Vector2(offset.x - slotFromRight * (ButtonSize + ButtonGap), offset.y);
    }

    // The icon carries its own plate, rim and glyph, so the button is just
    // the sprite: no text label, no tint, no extra decoration.
    static void StyleIcon(GameObject go, string resourcePath)
    {
        if (go == null) return;

        // Any label inherited from the template would draw over the glyph.
        foreach (var text in go.GetComponentsInChildren<Text>(true))
            text.gameObject.SetActive(false);
        foreach (var outline in go.GetComponents<Outline>())
            outline.enabled = false;

        var image = go.GetComponent<Image>();
        if (image != null)
        {
            var sprite = Resources.Load<Sprite>(resourcePath);
            if (sprite != null) image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = Color.white;
            // Default rect-based raycast (not alpha-tested): taps anywhere in
            // the square count, matching IsScreenPointOnAction.
            image.raycastTarget = true;
        }

        var button = go.GetComponent<Button>();
        if (button != null)
        {
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(1f, .86f, .82f, 1f);
            button.colors = colors;
        }

        // Motion: a cartoon squash-and-pop on press, and now and then a hard
        // BONE glint sweeping across the plate (render.sh --shine frames).
        CelPress.AddTo(go);
        if (image != null)
        {
            var shine = new Sprite[ShineFrames];
            for (int i = 0; i < ShineFrames; i++)
                shine[i] = Resources.Load<Sprite>(ShinePath(resourcePath, i));
            UiShimmer.AddTo(image, shine, 4.5f, resourcePath == HomeIconPath ? 0.35f : 0f);
        }
    }

    public const int ShineFrames = 4;

    // QuickActions/QuickAction_replay -> QuickActions/Shine/QuickAction_replay_shine_<i>
    public static string ShinePath(string iconPath, int frame)
    {
        int slash = iconPath.LastIndexOf('/');
        return iconPath.Substring(0, slash) + "/Shine/" + iconPath.Substring(slash + 1) + "_shine_" + frame;
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
        PlaceButtons();

        // Once the pause stock is empty the run continues even with no finger
        // on screen. Leave remains available in that state as well.
        bool stoppedTouching = !TouchInput.IsPressed;
        bool pointerOnAction = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        bool touchOnAction = TouchInput.IsPressed && Contains(TouchInput.Position);
        bool show = buttonClicks.playerDied || stoppedTouching || pointerOnAction || touchOnAction;
        // Nothing to pause or leave by mid-crash: the sequence owns the screen
        // (a tap skips it) until the Flight Complete panel is up.
        if (DeathCrash.Running) show = false;
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
        if (scene.name != "gameS1" && scene.name != "tutorialS5") return;
        if (Object.FindFirstObjectByType<PauseQuickActions>() != null) return;
        new GameObject("~PauseQuickActions").AddComponent<PauseQuickActions>();
    }
}
