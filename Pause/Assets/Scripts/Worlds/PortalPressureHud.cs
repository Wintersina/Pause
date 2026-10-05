using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// What the pilot is shown and told while a portal is kept waiting
// (PortalPressure): once its grace is over,
//
//   * an "ENTER THE PORTAL" banner (WorldBanner), once;
//   * a chip, "PORTAL  DANGER n" -- n goes up with every Level;
//   * a glow down both edges of the flight lane (just inside the rails) that
//     brightens and beats faster as the Level climbs, in the destination world's portal colour heating
//     toward amber. Never the player's red (HostileGlow.Tint; tested);
//   * sound: the boss warning's procedural klaxon as the pressure starts and
//     its thump on every Level (BossWarningAudio: no audio files).
//
// Its own overlay canvas, built at runtime; nothing in the scene refers to
// it and it moves nothing that exists. Everything animates on the
// pressure's own clock (flight seconds), so a paused game holds it still.
// No per-frame allocation: the chip's text changes only when the number does.
//
// PLACEMENT: everything sits inside the rails and clear of display cutouts
// through TopBand (the band the score read-out, the quick actions and the
// boss chip share): the chip is centred on the band, ChipTopOffset canvas
// units under its top (below the read-out / quick-action row and the boss
// chip), never wider than the band; the glows run down the band's two ends,
// the rails' inner edges, never over rail art. Placed by numbers and
// checked by OpenPortalTest, not by eye.
public class PortalPressureHud : MonoBehaviour
{
    public const string ObjectName = "~PortalPressure";
    // Under the boss warning (85), the quick actions (90) and every panel.
    public const int SortingOrder = 84;

    // ---- tunables ----
    public static float ChipTopOffset = 210f;
    public static int ChipFontSize = 30;
    // Each edge glow's width, as a share of the screen's.
    public static float GlowWidthShare = .10f;
    public static float GlowAlphaAtStart = .12f, GlowAlphaPerLevel = .04f, GlowAlphaMax = .55f;
    // Beats a second: at the start, per Level, at most.
    public static float BeatAtStart = .6f, BeatPerLevel = .15f, BeatMax = 3f;
    // The accent is fully hot (amber) by this Level.
    public static float HotLevel = 8f;

    public static PortalPressureHud Instance { get; private set; }

    Canvas canvas;
    GameObject body;
    Text chip;
    Image left, right;
    int shownDanger = -1;
    Vector2 laidOutFor = new Vector2(-1f, -1f);
    Rect laidOutSafe;
    TopBand.Frame laidOutBand;

    public Text Chip { get { return chip; } }
    public Image LeftGlow { get { return left; } }
    public Image RightGlow { get { return right; } }
    public bool Showing { get { return body != null && body.activeSelf; } }
    // Where the chip was last laid out, screen px (origin bottom-left): what
    // the codex toast drops in under while the chip is showing.
    public Rect ChipRect { get; private set; }

    // ---- the look (pure: tests read it) ----

    // The destination world's portal colour, moved out of the player's red
    // band, heating toward amber as the Level climbs.
    public static Color Accent(int destinationWorld, float level)
    {
        var worlds = WorldManager.Worlds;
        Color cool = HostileGlow.Tint(worlds[Mathf.Clamp(destinationWorld, 0, worlds.Length - 1)].portalColor);
        Color c = Color.Lerp(cool, AkiraPalette.Amber, Mathf.Clamp01(level / Mathf.Max(.01f, HotLevel)));
        c.a = 1f;
        return c;
    }

    public static float GlowAlpha(float level)
    {
        return Mathf.Min(GlowAlphaMax, GlowAlphaAtStart + GlowAlphaPerLevel * Mathf.Max(0f, level));
    }

    public static float BeatsPerSecond(float level)
    {
        return Mathf.Min(BeatMax, BeatAtStart + BeatPerLevel * Mathf.Max(0f, level));
    }

    // "PORTAL  DANGER 3", cached for the numbers anyone will see.
    static readonly string[] labels = new string[64];
    public static string ChipLabel(int danger)
    {
        if (danger <= 0) return "";
        if (danger >= labels.Length) return PortalPressure.ChipPrefix + danger;
        return labels[danger] ?? (labels[danger] = PortalPressure.ChipPrefix + danger);
    }

    // ---- build ----

    public static PortalPressureHud Ensure()
    {
        if (Instance != null) return Instance;
        var root = new GameObject(ObjectName, typeof(Canvas), typeof(CanvasScaler));
        var hud = root.AddComponent<PortalPressureHud>();
        hud.Build();
        Instance = hud;
        return hud;
    }

    void Build()
    {
        canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        var scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(800, 1200);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        body = new GameObject("Body", typeof(RectTransform));
        body.transform.SetParent(transform, false);
        var bodyRt = (RectTransform)body.transform;
        bodyRt.anchorMin = Vector2.zero;
        bodyRt.anchorMax = Vector2.one;
        bodyRt.offsetMin = bodyRt.offsetMax = Vector2.zero;

        left = Glow("GlowLeft", bodyRt, false);
        right = Glow("GlowRight", bodyRt, true);

        var chipGo = new GameObject("Chip", typeof(Text), typeof(Outline));
        chipGo.transform.SetParent(bodyRt, false);
        chip = chipGo.GetComponent<Text>();
        chip.font = WorldBanner.OrbitronOrBuiltin();
        chip.fontSize = ChipFontSize;
        chip.fontStyle = FontStyle.BoldAndItalic;
        chip.alignment = TextAnchor.MiddleCenter;
        chip.horizontalOverflow = HorizontalWrapMode.Overflow;
        chip.verticalOverflow = VerticalWrapMode.Overflow;
        chip.raycastTarget = false;
        chip.text = "";
        var outline = chipGo.GetComponent<Outline>();
        outline.effectColor = AkiraPalette.Ink;
        outline.effectDistance = new Vector2(2, -2);
        var rt = chip.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(.5f, 1f);
        rt.pivot = new Vector2(.5f, 1f);
        rt.sizeDelta = new Vector2(520, 44);
        rt.anchoredPosition = new Vector2(0f, -ChipTopOffset);

        body.SetActive(false);
    }

    static Sprite glowSprite;

    // A soft bar: opaque at the screen's edge, clear toward the lane.
    static Sprite GlowSprite()
    {
        if (glowSprite != null) return glowSprite;
        const int w = 32;
        var tex = new Texture2D(w, 1, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        for (int x = 0; x < w; x++)
        {
            float k = 1f - x / (float)(w - 1);
            tex.SetPixel(x, 0, new Color(1f, 1f, 1f, k * k));
        }
        tex.Apply();
        glowSprite = Sprite.Create(tex, new Rect(0, 0, w, 1), new Vector2(.5f, .5f), 100f);
        return glowSprite;
    }

    static Image Glow(string name, RectTransform parent, bool rightEdge)
    {
        var go = new GameObject(name, typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = GlowSprite();
        image.raycastTarget = false;
        image.color = Color.clear;
        var rt = image.rectTransform;
        rt.anchorMin = new Vector2(rightEdge ? 1f - GlowWidthShare : 0f, 0f);
        rt.anchorMax = new Vector2(rightEdge ? 1f : GlowWidthShare, 1f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        if (rightEdge) rt.localScale = new Vector3(-1f, 1f, 1f);
        return image;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        Refresh();
    }

    // One frame. Public so edit-mode tests can drive it.
    public void Refresh()
    {
        bool show = PortalPressure.Pressing && !buttonClicks.playerDied;
        if (body.activeSelf != show) body.SetActive(show);
        if (!show)
        {
            shownDanger = -1;
            return;
        }

        PlaceChip();

        int danger = PortalPressure.DangerNumber;
        if (danger != shownDanger)
        {
            shownDanger = danger;
            chip.text = ChipLabel(danger);
        }

        float level = PortalPressure.Level;
        Color accent = Accent(PortalPressure.Destination, level);
        // the beat runs on the pressure's own clock: flight seconds
        float beat = .5f + .5f * Mathf.Sin(PortalPressure.Seconds * 2f * Mathf.PI * BeatsPerSecond(level));
        chip.color = accent;
        float punch = 1f + .06f * beat;
        chip.rectTransform.localScale = new Vector3(punch, punch, 1f);
        accent.a = GlowAlpha(level) * (.65f + .35f * beat);
        left.color = accent;
        right.color = accent;
    }

    // Inside the rails and under any cutout (TopBand), whatever the screen
    // (re-laid only when the screen, its safe area or the band changes).
    void PlaceChip()
    {
        var screen = new Vector2(Screen.width, Screen.height);
        Rect safe = Screen.safeArea;
        var band = TopBand.FrameFor(safe, screen);
        if (screen == laidOutFor && safe == laidOutSafe && band.Same(laidOutBand)) return;
        laidOutFor = screen;
        laidOutSafe = safe;
        laidOutBand = band;
        float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        Layout(band, screen, scale);
    }

    // Pure placement in screen pixels (tests call it for any screen).
    public static Rect ChipScreenRect(TopBand.Frame band, float scale)
    {
        float w = Mathf.Min(ChipWidth * scale, Mathf.Max(0f, band.right - band.left));
        float h = ChipHeight * scale;
        float cx = .5f * (band.left + band.right);
        float top = band.top - ChipTopOffset * scale;
        // under the read-out when a small phone stacks it under the actions
        Rect readout = HudStyler.StackedReadout;
        if (readout.height > 0f) top = Mathf.Min(top, readout.yMin - 12f * scale);
        return new Rect(cx - .5f * w, top - h, w, h);
    }

    public static float ChipWidth = 520f, ChipHeight = 44f;

    // The glows' outer edges: the band's ends (the rails' inner edges).
    public static Vector2 GlowEdges(TopBand.Frame band) { return new Vector2(band.left, band.right); }

    // Applies the pure placement above for `band` on a `screen` whose overlay
    // canvas has scale factor `scale`. Public so the screen-fit rig can lay
    // the HUD out for a device it is faking (this reads Screen.* itself, as
    // BossWarningHud does).
    public void Layout(TopBand.Frame band, Vector2 screen, float scale)
    {
        if (screen.x <= 0f || screen.y <= 0f) return;
        Rect r = ChipScreenRect(band, scale);
        ChipRect = r;
        var rt = chip.rectTransform;
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(.5f, 1f);
        rt.sizeDelta = new Vector2(r.width / scale, r.height / scale);
        rt.anchoredPosition = new Vector2(r.center.x / scale, r.yMax / scale);
        // the text shrinks to the band rather than spill over a rail
        chip.resizeTextForBestFit = true;
        chip.resizeTextMinSize = 12;
        chip.resizeTextMaxSize = ChipFontSize;
        chip.horizontalOverflow = HorizontalWrapMode.Wrap;

        float l = band.left / screen.x, rr = band.right / screen.x;
        float w = Mathf.Min(GlowWidthShare, .5f * Mathf.Max(0f, rr - l));
        left.rectTransform.anchorMin = new Vector2(l, 0f);
        left.rectTransform.anchorMax = new Vector2(l + w, 1f);
        right.rectTransform.anchorMin = new Vector2(rr - w, 0f);
        right.rectTransform.anchorMax = new Vector2(rr, 1f);
        left.rectTransform.offsetMin = left.rectTransform.offsetMax = Vector2.zero;
        right.rectTransform.offsetMin = right.rectTransform.offsetMax = Vector2.zero;
    }

    // ---- signals: the banner and the sound ----

    static void OnSignal(PortalPressure.Signal signal)
    {
        switch (signal)
        {
            case PortalPressure.Signal.GraceOver:
                WorldBanner.Show(PortalPressure.UrgeBanner);
                BossWarningAudio.Play(BossWarningBeat.Announce, 0);
                break;
            case PortalPressure.Signal.LevelUp:
                BossWarningAudio.Play(BossWarningBeat.Second, Mathf.RoundToInt(BossWarningConfig.CloseAt));
                break;
        }
    }

    // ---- bootstrap ----

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        PortalPressure.Beat -= OnSignal;
        PortalPressure.Beat += OnSignal;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "gameS1") return;
        PortalPressure.Reset();
        BossWarningAudio.Prewarm();
        Ensure();
    }
}
