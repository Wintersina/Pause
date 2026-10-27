using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Restyles the in-game read-out.
//
// It was three lines of flat grey label text on a plain panel -- readable, but
// it looked like a debug overlay rather than part of the game. This gives each
// stat a colour and an icon-ish prefix, adds outlines so they hold up over a
// bright starfield, and keeps the pause counter plain ("PAUSES  5").
//
// Purely presentational: it never changes what the numbers say.
public class HudStyler : MonoBehaviour
{
    // Akira palette (docs/art-style.md, art-samples/ui_hud.png): CYAN speed,
    // Kaneda red pauses that blink BONE when nearly out.
    //
    // There is no star dust row: the run's dust is shown when it ends (the
    // Flight Complete card), so the read-out is SCORE / SPEED / PAUSES in
    // gameS1 and SPEED / PAUSES in the tutorial.
    static readonly Color Speed = AkiraPalette.Cyan;
    static readonly Color Pause = AkiraPalette.RedHi;
    static readonly Color PauseLow = AkiraPalette.Bone;

    // The panel is a chamfered NIGHT plate with an INK contour and the red
    // title-card tab (Art/UI/Hud/src~/hud_panel.svg), drawn final.
    // (Per-world frames: HudSkin.)
    static readonly Color TextInk = AkiraPalette.WithAlpha(AkiraPalette.Ink, .95f);

    // The world frames' growth along the edges and corners is drawn OUTSIDE
    // the panel's layout rect (canvas units; HudSkin.BoardOut, per world), so
    // it never reaches the text rows and the layout, the read-out's size and
    // everything placed around it stay exactly as they were.
    static int basePadLeft = -1;
    const string SkinFrameName = "SkinFrame";

    // The read-out's authored size and canvas, for the button panel's height
    // (ReadoutHeightFor). Set from the live read-out; gameS1's own numbers until then.
    static float authoredHeight = 131f, authoredWidth = 351f;
    static Vector2 authoredReference = new Vector2(800f, 600f);
    static CanvasScaler.ScreenMatchMode authoredMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
    static float authoredMatch = 0f;

    // The score board's height in px on `screen`, at full size (no lane shrink).
    public static float ReadoutHeightFor(Vector2 screen)
    {
        return authoredHeight * ScaleWithScreenSize(screen, authoredReference, authoredMode, authoredMatch);
    }

    // How far the read-out shrinks to share the lane with the button panel (the
    // width logic of ComputeHudLayout, from the authored size), so the panel can
    // match the board's drawn height.
    public static float ReadoutFitFor(TopBand.Frame band, Vector2 screen)
    {
        float hudScale = Mathf.Max(ScaleWithScreenSize(screen, authoredReference, authoredMode, authoredMatch), 0.0001f);
        float actionScale = PauseQuickActions.CanvasScaleFor(screen);
        float width = authoredWidth * hudScale;
        float available = band.right - PauseQuickActions.PanelWidthUnits * actionScale - MinGapToActions * actionScale - band.left;
        float fit = width > 0f && available < width ? Mathf.Max(0.1f, available / width) : 1f;
        if (fit < TopBand.ReadoutMinScale && PauseQuickActions.RaisedByFloor(screen))
        {
            float full = band.right - band.left;
            fit = width > 0f && full < width ? Mathf.Max(0.1f, full / width) : 1f;
        }
        return fit;
    }

    Text speedText, pauseText;
    int skinWorld = -1;

    // Reactive motion (unscaled: the HUD lives on through the freeze).
    // A stat that changes snaps to a punch pose for a couple of ticks.
    int lastPauses = int.MinValue;
    int pauseTextFor = int.MinValue;
    float pausePunchAt = -1f;

    // The read-out's root: the child of the HUD's root canvas that holds the
    // stats (gameS1 "Model Panel", tutorialS5 "Panel").
    RectTransform hudRoot;
    Canvas hudCanvas;
    CanvasScaler hudScaler;
    Rect appliedSafeArea;
    Vector2Int appliedScreen;
    TopBand.Frame appliedBand;

    public RectTransform HudRoot { get { return hudRoot; } }

    // The read-out leaves with its scene: nothing (the home menu's codex
    // toasts, the next run before its HUD is placed) keeps dodging it.
    void OnDestroy()
    {
        StackedReadout = default(Rect);
        PauseQuickActions.LiveReadoutHeightPx = 0f;
        authoredHeight = 131f;
        authoredWidth = 351f;
        authoredReference = new Vector2(800f, 600f);
        authoredMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        authoredMatch = 0f;
    }

    void Start()
    {
        speedText = Find("SpeedText");
        // gameS1 names this PauseCounter; tutorialS5 names the same readout
        // PausesRemainingText. Find() returning null for the first name used
        // to short-circuit Update() entirely (see below), which is why none
        // of the three stats styled in the tutorial, not just the pause one.
        pauseText = Find("PauseCounter") ?? Find("PausesRemainingText");

        // SCORE row on top (gameS1 only). Before placement: it grows the panel.
        if (ScoreHud.ShouldShow(speedText)) ScoreHud.Attach(gameObject, speedText);
        else ScoreHud.AttachPopupsOnly(gameObject, speedText);   // the tutorial: popups, no score row

        Style(speedText, Speed, ScoreHud.FontSize);
        Style(pauseText, Pause, ScoreHud.FontSize);
        // all three rows centred in their equal-height slots, so the pitch between the glyphs is equal too
        if (speedText != null) speedText.alignment = TextAnchor.MiddleLeft;
        if (pauseText != null) pauseText.alignment = TextAnchor.MiddleLeft;
        // Resume slow-mo indicator: SLOW-MO count-up + spool bar in this row.
        ResumeSpeedRow.Attach(speedText);

        hudRoot = FindHudRoot(speedText ?? pauseText, out hudCanvas);
        if (hudRoot != null)
        {
            hudScaler = hudCanvas.GetComponent<CanvasScaler>();
            StylePanel(hudRoot);
            skinWorld = HudSkin.CurrentWorld;
            if (hudScaler != null && hudScaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
            {
                authoredHeight = hudRoot.rect.size.y;
                authoredWidth = hudRoot.rect.size.x;
                authoredReference = hudScaler.referenceResolution;
                authoredMode = hudScaler.screenMatchMode;
                authoredMatch = hudScaler.matchWidthOrHeight;
            }
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
    // now pinned to the left end of the top band (TopBand: inside the left
    // rail's inner edge and the safe area, under any display cutout),
    // top-aligned with the quick actions at the band's right end, and
    // re-placed whenever the band moves (rotation, foldables, window resize,
    // a world's rails being painted).
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
        if (screen.x <= 0 || screen.y <= 0) return;
        var band = TopBand.FrameFor(safe, screen);
        if (!force && safe == appliedSafeArea && screen == appliedScreen && band.Same(appliedBand)) return;
        appliedSafeArea = safe;
        appliedScreen = screen;
        appliedBand = band;

        float hudScale = HudCanvasScale(hudCanvas, hudScaler, screen);
        Vector2 position;
        float fit;
        bool stacked;
        ComputeHudLayout(band, screen, hudScale, hudRoot.rect.size, out position, out fit, out stacked);
        hudRoot.anchoredPosition = position;
        hudRoot.localScale = new Vector3(fit, fit, 1f);
        // the home / replay panel beside it matches this height
        PauseQuickActions.LiveReadoutHeightPx = hudRoot.rect.size.y * hudScale * fit;
        StackedReadout = stacked ? HudScreenRect(band, screen, hudScale, hudRoot.rect.size) : default(Rect);
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

    // Gap kept between the read-out and the quick actions where the lane
    // brings them together (every phone), in quick-action canvas units.
    public const float MinGapToActions = 10f;

    // Pure, so it can be tested for any screen. `hudSize` is the panel's size
    // in its own canvas units, `hudScale` that canvas's pixels per unit.
    // Returns the anchoredPosition for a top-left anchor/pivot, and a scale
    // (<= 1) that shrinks the panel just enough to share the band with the
    // quick actions (TopBand.ReadoutMinScale: how far that goes).
    public static void ComputeHudLayout(Rect safeArea, Vector2 screen, float hudScale, Vector2 hudSize,
                                        out Vector2 anchoredPosition, out float fitScale)
    {
        ComputeHudLayout(TopBand.FrameFor(safeArea, screen), screen, hudScale, hudSize, out anchoredPosition, out fitScale);
    }

    // On a phone small in points / dp (UiScale's floor makes the quick
    // actions >= 44 pt / 48 dp, so they take more of a narrow lane) the
    // read-out would have to shrink under TopBand.ReadoutMinScale to sit
    // beside them: it drops to a second row instead, under the actions, with
    // the band's whole width (BossWarningHud then puts its chip in the first
    // row's free space, left of the actions).
    public static void ComputeHudLayout(TopBand.Frame band, Vector2 screen, float hudScale, Vector2 hudSize,
                                        out Vector2 anchoredPosition, out float fitScale)
    {
        bool stacked;
        ComputeHudLayout(band, screen, hudScale, hudSize, out anchoredPosition, out fitScale, out stacked);
    }

    public static void ComputeHudLayout(TopBand.Frame band, Vector2 screen, float hudScale, Vector2 hudSize,
                                        out Vector2 anchoredPosition, out float fitScale, out bool stacked)
    {
        hudScale = Mathf.Max(hudScale, 0.0001f);
        float actionScale = PauseQuickActions.CanvasScaleFor(screen);
        Rect actions = PauseQuickActions.ScreenRectFor(band, screen);
        float available = actions.xMin - MinGapToActions * actionScale - band.left;
        float width = hudSize.x * hudScale;
        fitScale = width > 0f && available < width ? Mathf.Max(0.1f, available / width) : 1f;
        float top = band.top;
        float panelH = PauseQuickActions.PanelHeightPx(screen);
        // only where UiScale's floor grew the actions (the trade-off it
        // brings); elsewhere the band is exactly as it always was
        stacked = fitScale < TopBand.ReadoutMinScale && PauseQuickActions.RaisedByFloor(screen);
        if (stacked)
        {
            float full = band.right - band.left;
            fitScale = width > 0f && full < width ? Mathf.Max(0.1f, full / width) : 1f;
            // under the first row: the actions, and BOSS INCOMING's chip
            // beside them (its full punch) while a boss is coming
            float row = Mathf.Max(panelH, BossWarningHud.ChipH * BossWarningHud.ChipMaxPunch * actionScale);
            // the two frames overhang their rects (bottom of the actions', top of the read-out's): the frames may still touch by a few units (as they always did) but no more; capped so the death panel keeps its room
            top = band.top - row - MinGapToActions * actionScale;
        }
        anchoredPosition = new Vector2(band.left / hudScale, (top - screen.y) / hudScale);
    }

    // The read-out's screen rect while it is stacked under the quick actions
    // (a small phone), else empty: what the codex toast and PORTAL DANGER's
    // chip, which sit under the band, drop below. Set by the live HUD
    // whenever it is placed (and by the screen-fit rig for its device).
    public static Rect StackedReadout;

    // Where the read-out lands on screen, in pixels (origin bottom-left).
    public static Rect HudScreenRect(Rect safeArea, Vector2 screen, float hudScale, Vector2 hudSize)
    {
        return HudScreenRect(TopBand.FrameFor(safeArea, screen), screen, hudScale, hudSize);
    }

    public static Rect HudScreenRect(TopBand.Frame band, Vector2 screen, float hudScale, Vector2 hudSize)
    {
        Vector2 position;
        float fit;
        ComputeHudLayout(band, screen, hudScale, hudSize, out position, out fit);
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
        // The scene's own panel image stays (invisible); the skin is a child
        // that reaches past the rect by HudSkin.BoardOut (per world).
        image.sprite = null;
        image.color = new Color(1f, 1f, 1f, 0f);
        var child = panel.Find(SkinFrameName);
        Image frame;
        if (child == null)
        {
            var go = new GameObject(SkinFrameName, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(panel, false);
            go.transform.SetAsFirstSibling();
            frame = go.GetComponent<Image>();
            frame.raycastTarget = false;
            frame.type = Image.Type.Sliced;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            var le = go.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
        }
        else frame = child.GetComponent<Image>();
        var vlg = panel.GetComponentInChildren<VerticalLayoutGroup>();
        if (vlg != null)
        {
            if (basePadLeft < 0) basePadLeft = vlg.padding.left;
            int left = basePadLeft + Mathf.RoundToInt(HudSkin.BoardTextExtraLeft(HudSkin.CurrentWorld));
            if (vlg.padding.left != left) { vlg.padding.left = left; LayoutRebuilder.MarkLayoutForRebuild(panel); }
        }
        var o = HudSkin.BoardOut(HudSkin.CurrentWorld);
        var frt = (RectTransform)frame.transform;
        frt.offsetMin = new Vector2(-o.x, -o.y);
        frt.offsetMax = new Vector2(o.z, o.w);
        var sprite = HudSkin.Frame(HudSkin.CurrentWorld);
        if (sprite != null) { frame.sprite = sprite; frame.color = Color.white; }
        else frame.color = AkiraPalette.WithAlpha(AkiraPalette.Night1, .88f);
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

    // Stepped punch: 1 tick big, 2 ticks small, then rest (24 fps ticks).
    static void Punch(RectTransform rt, float since)
    {
        if (rt == null) return;
        float k = since < 0f ? 99f : since * 24f;
        float s = k < 1f ? 1.18f : k < 3f ? .94f : 1f;
        if (!Mathf.Approximately(rt.localScale.x, s)) rt.localScale = new Vector3(s, s, 1f);
    }

    void Update()
    {
        // Each stat is independently guarded rather than one shared early
        // return -- a single missing element (the pause counter's name
        // mismatch in the tutorial) used to silently skip every stat here,
        // not just the one that could not be found.
        PlaceHud(false);
        if (hudRoot != null && skinWorld != HudSkin.CurrentWorld)
        {
            skinWorld = HudSkin.CurrentWorld;
            StylePanel(hudRoot);
        }

        if (pauseText != null)
        {
            int left = Mathf.Max(0, score.pauseCounter);
            bool low = left <= 1;
            float now = Time.unscaledTime;
            if (lastPauses != int.MinValue && left != lastPauses) pausePunchAt = now;
            lastPauses = left;

            // Nearly out: a hard red/bone blink on 4s (no fading).
            bool blinkOn = low && Mathf.FloorToInt(now * 6f) % 2 == 0;
            pauseText.color = blinkOn ? PauseLow : Pause;
            if (left != pauseTextFor)
            {
                pauseTextFor = left;
                pauseText.text = "PAUSES  " + left;
            }
            Punch(pauseText.rectTransform, now - pausePunchAt);
        }

        if (speedText != null)
            speedText.text = ResumeSpeedRow.SpeedLabel(ResumeSpeedRow.HudSpeed);
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
