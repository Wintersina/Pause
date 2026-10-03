using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Restyles the in-game read-out.
//
// It was three lines of flat grey label text on a plain panel -- readable, but
// it looked like a debug overlay rather than part of the game. This gives each
// stat a colour and an icon-ish prefix, adds outlines so they hold up over a
// bright starfield, and puts a subtle bar behind the pause counter so the
// resource you actually spend is the thing your eye lands on.
//
// Purely presentational: it never changes what the numbers say.
public class HudStyler : MonoBehaviour
{
    static readonly Color Speed = new Color(0.53f, 0.85f, 1f);
    static readonly Color Dust = new Color(1f, 0.79f, 0.26f);
    static readonly Color Pause = new Color(0.62f, 1f, 0.70f);
    static readonly Color PauseLow = new Color(1f, 0.42f, 0.38f);

    // Synthwave panel behind the read-out, matching the quick-action icons'
    // plate (deep violet) and neon rim (magenta). Kept translucent so the
    // starfield still reads through.
    static readonly Color PanelFill = new Color(0.06f, 0.02f, 0.15f, 0.62f);
    static readonly Color PanelRim = new Color(1f, 0.31f, 0.69f, 0.9f);
    // Ink outline behind the text: a very dark violet rather than pure black,
    // so it reads as part of the neon palette while keeping the contrast.
    static readonly Color TextInk = new Color(0.04f, 0f, 0.1f, 0.92f);

    Text speedText, dustText, pauseText;
    Image pauseBar;

    // The read-out's root: the child of the HUD's root canvas that holds the
    // stats (gameS1 "Model Panel", tutorialS5 "Panel").
    RectTransform hudRoot;
    Canvas hudCanvas;
    CanvasScaler hudScaler;
    Rect appliedSafeArea;
    Vector2Int appliedScreen;

    public RectTransform HudRoot { get { return hudRoot; } }

    void Start()
    {
        speedText = Find("SpeedText");
        dustText = Find("CurrecnyGatheredText");
        // gameS1 names this PauseCounter; tutorialS5 names the same readout
        // PausesRemainingText. Find() returning null for the first name used
        // to short-circuit Update() entirely (see below), which is why none
        // of the three stats styled in the tutorial, not just the pause one.
        pauseText = Find("PauseCounter") ?? Find("PausesRemainingText");

        Style(speedText, Speed, 26);
        Style(dustText, Dust, 26);
        Style(pauseText, Pause, 30);

        if (pauseText != null) pauseBar = BuildPauseBar(pauseText);

        hudRoot = FindHudRoot(speedText ?? dustText ?? pauseText, out hudCanvas);
        if (hudRoot != null)
        {
            hudScaler = hudCanvas.GetComponent<CanvasScaler>();
            StylePanel(hudRoot);
            // Pinned by its top-left corner from here on (see PlaceHud).
            hudRoot.anchorMin = hudRoot.anchorMax = hudRoot.pivot = new Vector2(0f, 1f);
            PlaceHud(true);
        }
    }

    // ---------------------------------------------------------------------
    // Placement
    //
    // Both scenes author the read-out centre-anchored with a fixed offset
    // (gameS1: (-137, 583) on an 800-wide, match-width canvas). The canvas
    // grows taller with the screen's aspect while that offset stays fixed, so
    // the panel was only near the top on the ~9:16 screen it was laid out on:
    // on a 19.5:9 phone it hung ~130pt down and ~60pt in from the left,
    // floating mid-screen, and it ignored the notch/safe area entirely. It is
    // now pinned to the safe area's top-left corner with the quick actions'
    // EdgeMargin, top-aligned with them, and re-placed whenever the screen
    // or safe area changes (rotation, foldables, window resize).
    // ---------------------------------------------------------------------

    // The direct child of the root canvas that contains `t`.
    static RectTransform FindHudRoot(Component t, out Canvas canvas)
    {
        canvas = null;
        if (t == null) return null;
        var root = t.GetComponentInParent<Canvas>();
        if (root == null) return null;
        canvas = root.rootCanvas;
        Transform cur = t.transform;
        while (cur.parent != null && cur.parent != canvas.transform) cur = cur.parent;
        return cur.parent == canvas.transform ? cur as RectTransform : null;
    }

    void PlaceHud(bool force)
    {
        if (hudRoot == null) return;
        var screen = new Vector2Int(Screen.width, Screen.height);
        Rect safe = Screen.safeArea;
        if (!force && safe == appliedSafeArea && screen == appliedScreen) return;
        if (screen.x <= 0 || screen.y <= 0) return;
        appliedSafeArea = safe;
        appliedScreen = screen;

        float hudScale = HudCanvasScale(hudCanvas, hudScaler, screen);
        Vector2 position;
        float fit;
        ComputeHudLayout(safe, screen, hudScale, hudRoot.rect.size, out position, out fit);
        hudRoot.anchoredPosition = position;
        hudRoot.localScale = new Vector3(fit, fit, 1f);
    }

    // Pixels per unit of the HUD's own canvas. Computed from the scaler for the
    // given screen rather than read from canvas.scaleFactor, which the scaler
    // only refreshes in its own Update -- possibly after this one runs.
    public static float HudCanvasScale(Canvas canvas, CanvasScaler scaler, Vector2 screen)
    {
        if (scaler != null && scaler.enabled && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
            return ScaleWithScreenSize(screen, scaler.referenceResolution, scaler.screenMatchMode, scaler.matchWidthOrHeight);
        if (scaler != null && scaler.enabled && scaler.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize)
            return scaler.scaleFactor;
        return canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
    }

    // Same maths as CanvasScaler.HandleScaleWithScreenSize.
    public static float ScaleWithScreenSize(Vector2 screen, Vector2 reference,
                                            CanvasScaler.ScreenMatchMode mode, float match)
    {
        if (screen.x <= 0f || screen.y <= 0f || reference.x <= 0f || reference.y <= 0f) return 1f;
        switch (mode)
        {
            case CanvasScaler.ScreenMatchMode.Expand:
                return Mathf.Min(screen.x / reference.x, screen.y / reference.y);
            case CanvasScaler.ScreenMatchMode.Shrink:
                return Mathf.Max(screen.x / reference.x, screen.y / reference.y);
            default:
                float logW = Mathf.Log(screen.x / reference.x, 2f);
                float logH = Mathf.Log(screen.y / reference.y, 2f);
                return Mathf.Pow(2f, Mathf.Lerp(logW, logH, match));
        }
    }

    // Gap kept between the read-out and the quick actions if a very narrow
    // screen ever brings them close, in quick-action canvas units.
    public const float MinGapToActions = 14f;

    // Pure, so it can be tested for any screen. `hudSize` is the panel's size
    // in its own canvas units, `hudScale` that canvas's pixels per unit.
    // Returns the anchoredPosition for a top-left anchor/pivot, and a scale
    // (<= 1) that only shrinks the panel if it would otherwise reach the
    // top-right quick actions.
    public static void ComputeHudLayout(Rect safeArea, Vector2 screen, float hudScale, Vector2 hudSize,
                                        out Vector2 anchoredPosition, out float fitScale)
    {
        hudScale = Mathf.Max(hudScale, 0.0001f);
        float actionScale = PauseQuickActions.CanvasScaleFor(screen);
        float margin = PauseQuickActions.EdgeMargin * actionScale;
        float left = safeArea.xMin + margin;
        float top = safeArea.yMax - margin;

        float available = PauseQuickActions.ScreenRectFor(safeArea, screen).xMin
                          - MinGapToActions * actionScale - left;
        float width = hudSize.x * hudScale;
        fitScale = width > 0f && available < width ? Mathf.Max(0.1f, available / width) : 1f;

        anchoredPosition = new Vector2(left / hudScale, (top - screen.y) / hudScale);
    }

    // Where the read-out lands on screen, in pixels (origin bottom-left).
    public static Rect HudScreenRect(Rect safeArea, Vector2 screen, float hudScale, Vector2 hudSize)
    {
        Vector2 position;
        float fit;
        ComputeHudLayout(safeArea, screen, hudScale, hudSize, out position, out fit);
        float w = hudSize.x * hudScale * fit, h = hudSize.y * hudScale * fit;
        float left = position.x * hudScale;
        float top = screen.y + position.y * hudScale;
        return new Rect(left, top - h, w, h);
    }

    // ---------------------------------------------------------------------
    // Styling
    // ---------------------------------------------------------------------

    static void StylePanel(RectTransform panel)
    {
        var image = panel.GetComponent<Image>();
        if (image == null) return;
        image.color = PanelFill;
        if (panel.Find(RimName) != null) return;

        // A thin magenta neon rim, like the quick-action plates: four edge
        // strips (not an Outline effect, whose offset copies would tint the
        // translucent fill), ignored by any layout group on the panel.
        var rim = new GameObject(RimName, typeof(RectTransform), typeof(LayoutElement));
        rim.GetComponent<LayoutElement>().ignoreLayout = true;
        var rimRt = (RectTransform)rim.transform;
        rimRt.SetParent(panel, false);
        rimRt.anchorMin = Vector2.zero;
        rimRt.anchorMax = Vector2.one;
        rimRt.offsetMin = rimRt.offsetMax = Vector2.zero;
        Edge(rimRt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -RimWidth), Vector2.zero);
        Edge(rimRt, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, RimWidth));
        Edge(rimRt, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(RimWidth, 0f));
        Edge(rimRt, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-RimWidth, 0f), Vector2.zero);
    }

    const string RimName = "NeonRim";
    const float RimWidth = 2.5f;

    static void Edge(RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        var go = new GameObject("Edge", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        var img = go.GetComponent<Image>();
        img.color = PanelRim;
        img.raycastTarget = false;
    }

    static Text Find(string name)
    {
        var go = SceneUtil.FindAny(name);
        return go != null ? go.GetComponent<Text>() : null;
    }

    static void Style(Text t, Color colour, int size)
    {
        if (t == null) return;
        t.color = colour;
        t.fontSize = size;
        t.fontStyle = FontStyle.Bold;
        t.resizeTextForBestFit = false;

        var outline = t.GetComponent<Outline>();
        if (outline == null) outline = t.gameObject.AddComponent<Outline>();
        outline.effectColor = TextInk;
        outline.effectDistance = new Vector2(2f, -2f);
    }

    // A thin depleting bar under the pause counter, so the most important
    // number is readable at a glance instead of being parsed as text.
    //
    // This used to hang 4-10px *below* the pause counter's own rect. The
    // panel's VerticalLayoutGroup has no idea that extra height exists --
    // PauseBar is a plain child of PauseCounter, invisible to the group's own
    // spacing math -- so the fixed gap it left before the next stacked
    // element (the blue-atom timer, gotAtomText) was not enough to clear it,
    // and the two overlapped. Contained within the bottom of PauseCounter's
    // own allocated rect instead, it cannot spill into whatever the layout
    // group stacks next, on any screen size.
    static Image BuildPauseBar(Text anchor)
    {
        var holder = new GameObject("PauseBar", typeof(Image));
        holder.transform.SetParent(anchor.transform, false);

        var rt = holder.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0.16f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = holder.GetComponent<Image>();
        img.color = Pause;
        img.raycastTarget = false;
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        return img;
    }

    void Update()
    {
        // Each stat is independently guarded rather than one shared early
        // return -- a single missing element (the pause counter's name
        // mismatch in the tutorial) used to silently skip every stat here,
        // not just the one that could not be found.
        PlaceHud(false);

        if (pauseText != null)
        {
            int left = Mathf.Max(0, score.pauseCounter);
            bool low = left <= 1;

            pauseText.color = low ? PauseLow : Pause;
            pauseText.text = "PAUSES  " + left;

            if (pauseBar != null)
            {
                // five is a full run's allotment; anything above that just fills it
                pauseBar.fillAmount = Mathf.Clamp01(left / 5f);
                pauseBar.color = low ? PauseLow : Pause;
            }
        }

        if (speedText != null)
            speedText.text = "SPEED  " + Mathf.RoundToInt(moveBackGround.speed * 100f);

        if (dustText != null)
            dustText.text = "★ " + score.totalCurrency.ToString("F1");
    }
}

public static class HudStylerBootstrap
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
        if (Object.FindFirstObjectByType<HudStyler>() != null) return;
        new GameObject("~HudStyler").AddComponent<HudStyler>();
    }
}
