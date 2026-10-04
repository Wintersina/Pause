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
    // Akira palette (docs/art-style.md, art-samples/ui_hud.png): CYAN speed,
    // AMBER star dust, Kaneda red pauses that blink BONE when nearly out.
    static readonly Color Speed = AkiraPalette.Cyan;
    static readonly Color Dust = AkiraPalette.Amber;
    static readonly Color Pause = AkiraPalette.RedHi;
    static readonly Color PauseLow = AkiraPalette.Bone;
    static readonly Color MeterOn = AkiraPalette.Red;
    static readonly Color MeterOff = AkiraPalette.WithAlpha(AkiraPalette.Indigo1, .9f);

    // The panel is a chamfered NIGHT plate with an INK contour and the red
    // title-card tab (Art/UI/Hud/src~/hud_panel.svg), drawn final.
    const string PanelSprite = "Hud/hud_panel";
    const string MeterSprite = "Hud/hud_meter";
    static readonly Color TextInk = AkiraPalette.WithAlpha(AkiraPalette.Ink, .95f);

    Text speedText, dustText, pauseText;
    Image pauseBar;
    Image pauseBarBack;

    // Reactive motion (unscaled: the HUD lives on through the freeze).
    // A stat that changes snaps to a punch pose for a couple of ticks.
    int lastPauses = int.MinValue;
    float lastDust = float.NaN;
    float pausePunchAt = -1f, dustPunchAt = -1f;

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

        // SCORE row on top (gameS1 only). Before placement: it grows the panel.
        if (ScoreHud.ShouldShow(speedText)) ScoreHud.Attach(gameObject, speedText);

        Style(speedText, Speed, 26);
        Style(dustText, Dust, 26);
        Style(pauseText, Pause, 30);

        if (pauseText != null)
        {
            pauseBarBack = BuildPauseBar(pauseText, "PauseBarBack");
            pauseBarBack.fillAmount = 1f;
            pauseBarBack.color = MeterOff;
            pauseBar = BuildPauseBar(pauseText, "PauseBar");
        }

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
        var sprite = Resources.Load<Sprite>(PanelSprite);
        if (sprite != null)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
        }
        else
        {
            image.color = AkiraPalette.WithAlpha(AkiraPalette.Night1, .88f);
        }
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
    static Image BuildPauseBar(Text anchor, string name)
    {
        var holder = new GameObject(name, typeof(Image));
        holder.transform.SetParent(anchor.transform, false);

        var rt = holder.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0.16f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = holder.GetComponent<Image>();
        img.sprite = Resources.Load<Sprite>(MeterSprite);
        img.color = MeterOn;
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
            float now = Time.unscaledTime;
            if (lastPauses != int.MinValue && left != lastPauses) pausePunchAt = now;
            lastPauses = left;

            // Nearly out: a hard red/bone blink on 4s (no fading).
            bool blinkOn = low && Mathf.FloorToInt(now * 6f) % 2 == 0;
            pauseText.color = blinkOn ? PauseLow : Pause;
            pauseText.text = "PAUSES  " + left;
            Punch(pauseText.rectTransform, now - pausePunchAt);

            if (pauseBar != null)
            {
                // five is a full run's allotment; anything above that just fills it
                pauseBar.fillAmount = Mathf.Clamp01(left / 5f);
                pauseBar.color = blinkOn ? PauseLow : MeterOn;
            }
        }

        if (speedText != null)
            speedText.text = "SPEED  " + Mathf.RoundToInt(moveBackGround.speed * 100f);

        if (dustText != null)
        {
            float dust = score.totalCurrency;
            float now = Time.unscaledTime;
            if (!float.IsNaN(lastDust) && dust > lastDust + .0001f) dustPunchAt = now;
            lastDust = dust;
            dustText.text = "★ " + dust.ToString("F1");
            float since = now - dustPunchAt;
            // A pickup flashes the figure BONE for two ticks as it punches.
            dustText.color = since >= 0f && since < 2f / 24f ? AkiraPalette.Bone : Dust;
            Punch(dustText.rectTransform, since);
        }
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
