using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Draws the boss warning (timing and rules: BossWarning.cs).
//
//   T-30  the BANNER snaps in under the top band -- "BOSS INCOMING" over the
//         boss's name (or UNKNOWN SIGNAL until it has been met), hazard
//         stripes blinking in the world's accent -- holds, then collapses
//         up into ...
//   ...   the CHIP: a compact plate in the top band, between the score
//         read-out and the home / replay icons: BOSS, the seconds left, and
//         a bar that drains with them.
//   T-10  the chip steps up: figures in the accent, a punch on every second,
//         the screen's edges start to throb in the accent.
//   T-3   bigger again, stripes flashing, a big 3 - 2 - 1 under the band,
//         the edge glow strong.
//   T-0   the boss intro starts: the chip bursts and the edge glow flares
//         and drains out under the intro's own flash and WARNING slab.
//
// Everything is built once at scene load (hidden by CanvasGroup alpha, so
// the meshes and the font atlas are already there when it first shows) and
// nothing is created, formatted or looked up while it runs: the figures are
// cached strings, motion is transforms and CanvasRenderer colour / alpha.
// All of it runs on the countdown's own clock -- flight time -- so while the
// player holds the world frozen the warning holds its pose too; only the
// hand-off runs on real time (the intro freezes the world).
// It has no raycast targets and no raycaster: it can never take a touch.
public class BossWarningHud : MonoBehaviour
{
    // Over the scene HUD, under the quick actions (90) and every panel.
    public const int SortingOrder = 85;
    public const string ObjectName = "~BossWarning";

    // ---- layout, in quick-action canvas units (PauseQuickActions) ----
    public const float ChipW = 96f, ChipH = 84f;
    // The chip's largest pose (stage scale x punch); its slot is sized for it.
    public const float ChipMaxPunch = 1.16f;
    public const float ChipGap = 10f;
    // Squeezed narrower than this share of its size, it moves under the icons.
    public const float ChipMinFit = .6f;
    public const float BannerW = 560f, BannerH = 120f;
    public const float BannerGap = 16f;
    public const float BannerMaxWidthShare = .92f;

    public struct Layout
    {
        public Vector2 chipTop;     // the chip's top-centre, screen px
        public float chipScale;     // px per chip unit at rest
        public Rect chip;           // its largest footprint, screen px
        public bool chipInBand;     // between the read-out and the icons
        public Vector2 bannerTop;   // the banner's top-centre, screen px
        public float bannerScale;
        public Rect banner;         // screen px (the 3-2-1 sits in it too)
    }

    // Pure: where everything goes for a screen, its safe area and the score
    // read-out's rect (HudStyler.HudScreenRect; zero-sized when there is none).
    public static Layout ComputeLayout(Rect safe, Vector2 screen, Rect hud)
    {
        var l = new Layout();
        float s = PauseQuickActions.CanvasScaleFor(screen);
        Rect actions = PauseQuickActions.ScreenRectFor(safe, screen);
        float top = safe.yMax - PauseQuickActions.TopMargin * s;

        float left = Mathf.Max(hud.xMax, safe.xMin) + ChipGap * s;
        float right = actions.xMin - ChipGap * s;
        float need = ChipW * ChipMaxPunch * s;
        float fit = Mathf.Min(1f, (right - left) / need);
        l.chipInBand = fit >= ChipMinFit;
        if (l.chipInBand)
        {
            l.chipScale = s * fit;
            l.chipTop = new Vector2((left + right) * .5f, top);
        }
        else
        {
            l.chipScale = s;
            l.chipTop = new Vector2(actions.xMax - need * .5f, actions.yMin - ChipGap * s);
        }
        float w = ChipW * ChipMaxPunch * l.chipScale, h = ChipH * ChipMaxPunch * l.chipScale;
        l.chip = new Rect(l.chipTop.x - w * .5f, l.chipTop.y - h, w, h);

        float bandBottom = Mathf.Min(actions.yMin, l.chip.yMin);
        if (hud.height > 0f) bandBottom = Mathf.Min(bandBottom, hud.yMin);
        l.bannerScale = Mathf.Min(s, safe.width * BannerMaxWidthShare / BannerW);
        l.bannerTop = new Vector2(safe.center.x, bandBottom - BannerGap * s);
        float bw = BannerW * l.bannerScale, bh = BannerH * l.bannerScale;
        l.banner = new Rect(l.bannerTop.x - bw * .5f, l.bannerTop.y - bh, bw, bh);
        return l;
    }

    // ---- pieces ----------------------------------------------------------

    readonly BossCountdown countdown = new BossCountdown();
    public BossCountdown Countdown { get { return countdown; } }

    RectTransform chip, banner, finalRoot, barFill, titleRt, subRt;
    CanvasGroup chipGroup, bannerGroup, finalGroup;
    Text digits, label, title, sub, finalDigit;
    Graphic chipStripes, bannerStripes, vignette, barGraphic, chipPlate;
    Texture2D vignetteTex;
    Sprite vignetteSprite;

    static readonly string[] Figures = BuildFigures();
    string[] knownLines;

    Layout layout;
    bool laidOut;
    Rect appliedSafe;
    Vector2 appliedScreen, appliedHudSize;
    RectTransform hudRoot;
    Canvas hudCanvas;
    CanvasScaler hudScaler;

    Color accent = BossWarningConfig.Accents[0], accentDim, accentHi;
    BossWarningStage styled = BossWarningStage.Idle;
    int shownSeconds = -1, shownFinal = -1;
    float chipSince = -1f;      // Since when the chip came up
    float handoff = -1f;        // real seconds into the hand-off; < 0: none
    float handoffFrom;
    bool visible;
    System.Action<BossWarningBeat> onBeat;

    // ---- read-outs for tests and the preview ----
    public Layout CurrentLayout { get { return layout; } }
    public bool Visible { get { return visible; } }
    public bool ChipVisible { get { return chipGroup != null && chipGroup.alpha > 0f; } }
    public bool BannerVisible { get { return bannerGroup != null && bannerGroup.alpha > 0f; } }
    public bool FinalVisible { get { return finalGroup != null && finalGroup.alpha > 0f; } }
    public bool HandingOff { get { return handoff >= 0f; } }
    public string DigitsShown { get { return digits != null ? digits.text : ""; } }
    public string TitleShown { get { return title != null ? title.text : ""; } }
    public string SubShown { get { return sub != null ? sub.text : ""; } }
    public float VignetteAlpha { get { return vignette != null ? vignette.canvasRenderer.GetAlpha() : 0f; } }
    public float BarFill01 { get { return barFill != null ? barFill.localScale.x : 0f; } }
    public Color Accent { get { return accent; } }
    public RectTransform ChipRect { get { return chip; } }
    public RectTransform BannerRect { get { return banner; } }

    static string[] BuildFigures()
    {
        var f = new string[100];
        for (int i = 0; i < f.Length; i++) f[i] = i.ToString();
        return f;
    }

    // Only gameS1 has a WorldManager and bosses; the tutorial has neither.
    public static bool ShouldAttach(string sceneName)
    {
        return sceneName == "gameS1";
    }

    // Builds the whole warning, hidden. Explicit (not Awake) so edit-mode
    // tests and the preview build it the same way.
    public static BossWarningHud Build()
    {
        var root = new GameObject(ObjectName, typeof(Canvas), typeof(CanvasScaler));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        // One canvas unit is one pixel: ComputeLayout places in screen px.
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;

        var hud = root.AddComponent<BossWarningHud>();
        hud.BuildPieces(root.transform);
        hud.onBeat = hud.OnBeat;
        hud.countdown.OnBeat = hud.onBeat;
        BossWarning.Countdown = hud.countdown;
        BossWarningAudio.Prewarm();
        hud.HideAll();
        return hud;
    }

    void OnDestroy()
    {
        if (BossWarning.Countdown == countdown) BossWarning.Countdown = null;
        Drop(vignetteSprite);
        Drop(vignetteTex);
    }

    static void Drop(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }

    void BuildPieces(Transform root)
    {
        Font font = WorldBanner.OrbitronOrBuiltin();

        // -- edge glow --
        var vg = new GameObject("EdgeGlow", typeof(RectTransform), typeof(Image));
        vg.transform.SetParent(root, false);
        var vrt = (RectTransform)vg.transform;
        vrt.anchorMin = Vector2.zero;
        vrt.anchorMax = Vector2.one;
        vrt.offsetMin = vrt.offsetMax = Vector2.zero;
        var vimg = vg.GetComponent<Image>();
        vignetteTex = BuildEdgeTexture();
        vignetteSprite = Sprite.Create(vignetteTex, new Rect(0, 0, vignetteTex.width, vignetteTex.height), new Vector2(.5f, .5f));
        vimg.sprite = vignetteSprite;
        vimg.raycastTarget = false;
        vignette = vimg;

        // -- banner --
        banner = Holder(root, "Banner", new Vector2(BannerW, BannerH), out bannerGroup);
        Slab(banner, "Plate", BossWarningSlabGraphic.Part.Plate, 14f, 15f, 15f, true);
        bannerStripes = Slab(banner, "Stripes", BossWarningSlabGraphic.Part.Stripes, 14f, 15f, 15f, true);
        title = Label(banner, "Title", font, 50, FontStyle.BoldAndItalic, AkiraPalette.Bone,
                      new Vector2(0f, 9f), new Vector2(BannerW - 60f, 60f), 3f);
        title.gameObject.AddComponent<Shadow>().effectColor = AkiraPalette.Ink;
        title.GetComponent<Shadow>().effectDistance = new Vector2(4f, -5f);
        title.text = BossWarningConfig.Title;
        titleRt = title.rectTransform;
        sub = Label(banner, "Name", font, 22, FontStyle.Bold, AkiraPalette.Bone,
                    new Vector2(-4f, -26f), new Vector2(BannerW - 110f, 28f), 2f);
        sub.resizeTextForBestFit = true;
        sub.resizeTextMinSize = 12;
        sub.resizeTextMaxSize = 22;
        subRt = sub.rectTransform;

        // -- the 3 - 2 - 1 --
        finalRoot = Holder(root, "Final", new Vector2(BannerW, BannerH), out finalGroup);
        finalDigit = Label(finalRoot, "Digit", font, 112, FontStyle.BoldAndItalic, AkiraPalette.Bone,
                           new Vector2(0f, -14f), new Vector2(BannerW, BannerH), 5f);
        finalDigit.verticalOverflow = VerticalWrapMode.Overflow;

        // -- chip --
        chip = Holder(root, "Chip", new Vector2(ChipW, ChipH), out chipGroup);
        chipPlate = Slab(chip, "Plate", BossWarningSlabGraphic.Part.Plate, 5f, 9f, 9f, false);
        chipStripes = Slab(chip, "Stripes", BossWarningSlabGraphic.Part.Stripes, 5f, 9f, 9f, false);
        label = Label(chip, "Label", font, 15, FontStyle.Bold, AkiraPalette.Bone,
                      new Vector2(2f, 16f), new Vector2(ChipW - 12f, 20f), 1.5f);
        label.text = BossWarningConfig.ChipLabel;
        digits = Label(chip, "Seconds", font, 40, FontStyle.Bold, AkiraPalette.Bone,
                       new Vector2(0f, -9f), new Vector2(ChipW - 8f, 46f), 2.5f);
        var back = Bar(chip, "BarBack", AkiraPalette.WithAlpha(AkiraPalette.Ink, .9f));
        back.rectTransform.sizeDelta += new Vector2(4f, 4f);
        barGraphic = Bar(chip, "BarFill", Color.white);
        barFill = barGraphic.rectTransform;
        // drains towards its left end
        barFill.pivot = new Vector2(0f, .5f);
        barFill.anchoredPosition -= new Vector2(barFill.sizeDelta.x * .5f, 0f);

        // Every glyph the warning will ever show, into the font atlas now.
        knownLines = new string[BossCatalog.All.Length];
        string all = BossWarningConfig.Title + BossWarningConfig.UnknownLine + BossWarningConfig.ChipLabel + "0123456789";
        for (int i = 0; i < knownLines.Length; i++)
        {
            knownLines[i] = BossCatalog.All[i].name + BossWarningConfig.KnownSuffix;
            all += knownLines[i];
        }
        if (font != null && font.dynamic)
        {
            font.RequestCharactersInTexture(all, title.fontSize, title.fontStyle);
            font.RequestCharactersInTexture(all, sub.fontSize, sub.fontStyle);
            font.RequestCharactersInTexture("0123456789", finalDigit.fontSize, finalDigit.fontStyle);
            font.RequestCharactersInTexture(all, digits.fontSize, digits.fontStyle);
            font.RequestCharactersInTexture(all, label.fontSize, label.fontStyle);
        }
        SetAccent(0);
    }

    // A holder pinned by its top-centre, placed in screen px by ApplyLayout.
    static RectTransform Holder(Transform root, string name, Vector2 size, out CanvasGroup group)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(root, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(.5f, 1f);
        rt.sizeDelta = size;
        group = go.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        return rt;
    }

    static BossWarningSlabGraphic Slab(RectTransform parent, string name, BossWarningSlabGraphic.Part part,
                                       float lean, float band, float stripe, bool bottomBand)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var g = go.AddComponent<BossWarningSlabGraphic>();
        g.part = part;
        g.lean = lean;
        g.band = band;
        g.stripeWidth = stripe;
        g.bottomBand = bottomBand;
        g.raycastTarget = false;
        return g;
    }

    static Text Label(RectTransform parent, string name, Font font, int size, FontStyle style, Color colour,
                      Vector2 at, Vector2 box, float outline)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
        rt.anchoredPosition = at;
        rt.sizeDelta = box;
        var t = go.GetComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.supportRichText = false;
        t.raycastTarget = false;
        t.color = colour;
        var o = go.GetComponent<Outline>();
        o.effectColor = AkiraPalette.Ink;
        o.effectDistance = new Vector2(outline, -outline);
        return t;
    }

    static Image Bar(RectTransform parent, string name, Color colour)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(.5f, 0f);
        rt.pivot = new Vector2(.5f, .5f);
        rt.sizeDelta = new Vector2(ChipW - 30f, 5f);
        rt.anchoredPosition = new Vector2(-2f, 11f);
        var img = go.GetComponent<Image>();
        img.color = colour;
        img.raycastTarget = false;
        return img;
    }

    // White, opaque at the screen's edges and clear in the middle; stretched
    // over the screen and tinted with the accent.
    static Texture2D BuildEdgeTexture()
    {
        const int n = 48;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = Mathf.Abs((x + .5f) / n - .5f) * 2f, v = Mathf.Abs((y + .5f) / n - .5f) * 2f;
                // sides glow further in than top and bottom (a tall screen)
                float side = Mathf.Clamp01((u - .62f) / .38f), ends = Mathf.Clamp01((v - .84f) / .16f);
                float a = Mathf.Clamp01(side * side + ends * ends * .8f);
                px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    // ---- runtime ---------------------------------------------------------

    IEnumerator Start()
    {
        // HudStyler builds the read-out in its own Start; look once it has.
        yield return null;
        var styler = FindFirstObjectByType<HudStyler>();
        if (styler != null) BindHud(styler.HudRoot);
    }

    // The score read-out the chip sits beside (null: none).
    public void BindHud(RectTransform root)
    {
        hudRoot = root;
        hudCanvas = root != null ? root.GetComponentInParent<Canvas>() : null;
        if (hudCanvas != null) hudCanvas = hudCanvas.rootCanvas;
        hudScaler = hudCanvas != null ? hudCanvas.GetComponent<CanvasScaler>() : null;
        laidOut = false;
    }

    void Update()
    {
        var screen = new Vector2(Screen.width, Screen.height);
        Rect safe = Screen.safeArea;
        Vector2 hudSize = hudRoot != null ? hudRoot.rect.size : Vector2.zero;
        if (!laidOut || safe != appliedSafe || screen != appliedScreen || hudSize != appliedHudSize)
        {
            Rect hud = new Rect(safe.xMin, safe.yMax, 0f, 0f);
            if (hudRoot != null && hudRoot.gameObject.activeInHierarchy)
                hud = HudStyler.HudScreenRect(safe, screen, HudStyler.HudCanvasScale(hudCanvas, hudScaler, screen), hudSize);
            ApplyLayout(ComputeLayout(safe, screen, hud));
            appliedSafe = safe;
            appliedScreen = screen;
            appliedHudSize = hudSize;
        }

        var wm = WorldManager.Instance;
        var input = BossWarning.Read(wm);
        bool ahead = input == BossWarningInput.Ahead;
        float eta = ahead ? wm.SecondsLeftInWorld : float.PositiveInfinity;
        float dt = ahead && WorldManager.Flying ? Time.deltaTime : 0f;
        Step(input, eta, dt, Mathf.Min(Time.unscaledDeltaTime, .1f));
    }

    public void ApplyLayout(Layout l)
    {
        layout = l;
        laidOut = true;
        chip.anchoredPosition = l.chipTop;
        banner.anchoredPosition = l.bannerTop;
        banner.localScale = new Vector3(l.bannerScale, l.bannerScale, 1f);
        finalRoot.anchoredPosition = l.bannerTop;
        finalRoot.localScale = new Vector3(l.bannerScale, l.bannerScale, 1f);
        SetScale(chip, l.chipScale, l.chipScale);
    }

    // One frame: `flightDt` moves the countdown and its animation, `realDt`
    // only the hand-off. Public so edit-mode tests and the preview step it.
    public void Step(BossWarningInput input, float eta, float flightDt, float realDt)
    {
        countdown.Step(input, eta, flightDt);

        if (handoff >= 0f)
        {
            handoff += realDt;
            AnimateHandoff();
            return;
        }
        if (!countdown.Active)
        {
            if (visible) HideAll();
            return;
        }
        Animate();
    }

    void OnBeat(BossWarningBeat beat)
    {
        switch (beat)
        {
            case BossWarningBeat.Announce:
                handoff = -1f;
                SetAccent(WorldManager.CurrentIndex);
                SetNameLine(WorldManager.CurrentIndex);
                styled = BossWarningStage.Idle;
                shownSeconds = shownFinal = -1;
                chipSince = -1f;
                visible = true;
                break;
            case BossWarningBeat.Arrive:
                handoff = 0f;
                handoffFrom = vignette.canvasRenderer.GetAlpha();
                SetText(digits, ref shownSeconds, 0);
                Show(bannerGroup, false);
                Show(finalGroup, false);
                Show(chipGroup, true);
                chipStripes.canvasRenderer.SetColor(AkiraPalette.Bone);
                break;
            case BossWarningBeat.Cancel:
                HideAll();
                break;
        }
        BossWarningAudio.Play(beat, countdown.Seconds);
        BossWarning.Raise(beat);
    }

    void SetAccent(int world)
    {
        accent = BossWarningConfig.Accent(world);
        accentDim = new Color(accent.r * .5f, accent.g * .5f, accent.b * .5f, 1f);
        accentHi = Color.Lerp(accent, AkiraPalette.Bone, .45f);
        vignette.color = accent;
        vignette.canvasRenderer.SetAlpha(0f);
        bannerStripes.canvasRenderer.SetColor(accent);
        chipStripes.canvasRenderer.SetColor(accentDim);
        barGraphic.canvasRenderer.SetColor(accent);
        sub.color = accentHi;
    }

    void SetNameLine(int world)
    {
        var boss = BossCatalog.ForWorld(world);
        bool known = BossWarningConfig.RevealNameBeforeFirstSight || Codex.IsDiscovered(boss.id);
        string line = known ? knownLines[Mathf.Clamp(world, 0, knownLines.Length - 1)] : BossWarningConfig.UnknownLine;
        if (!ReferenceEquals(sub.text, line)) sub.text = line;
    }

    void HideAll()
    {
        visible = false;
        handoff = -1f;
        Show(chipGroup, false);
        Show(bannerGroup, false);
        Show(finalGroup, false);
        if (vignette != null) vignette.canvasRenderer.SetAlpha(0f);
    }

    // ---- animation (all on held 24 fps ticks, like the rest of the UI) ----

    const float Tick = 1f / 24f;
    const float BannerOutTicks = 4f;

    void Animate()
    {
        float since = countdown.Since;
        var stage = countdown.Stage;
        if (stage != styled) Style(stage);

        // -- banner --
        bool bannerUp = countdown.BannerUp;
        Show(bannerGroup, bannerUp);
        float glow = 0f;
        if (bannerUp)
        {
            float k = since / Tick;                               // ticks in
            float left = (countdown.BannerFor - since) / Tick;    // ticks to go
            float sx = 1f, text = 1f, lift = 0f, shrink = 1f;
            if (k < 1f) { sx = .35f; text = 0f; }
            else if (k < 2f) { sx = 1.12f; text = 0f; }
            else if (k < 4f) { sx = .97f; text = 1.3f; }
            else if (k < 6f) { text = .95f; }
            // the collapse: lean back, then up into the chip's corner
            if (left < 1f) { shrink = .22f; lift = .92f; }
            else if (left < 2f) { shrink = .5f; lift = .6f; }
            else if (left < 3f) { shrink = .8f; lift = .25f; }
            else if (left < BannerOutTicks) { shrink = 1.05f; }
            float s = layout.bannerScale * shrink;
            SetScale(banner, s * sx, s);
            banner.anchoredPosition = Vector2.Lerp(layout.bannerTop, layout.chipTop, lift);
            SetScale(titleRt, text, text);
            SetScale(subRt, text > 0f ? 1f : 0f, text > 0f ? 1f : 0f);
            // stripes blink for the first second, then hold
            bool dim = since < 1f && Mathf.FloorToInt(since * 8f) % 2 == 1;
            bannerStripes.canvasRenderer.SetColor(dim ? accentDim : accent);
            // one flare of the edges as it lands
            glow = BossWarningConfig.AnnounceVignette * Mathf.Clamp01(1f - since / .7f);
        }

        // -- chip: up as the banner starts to collapse --
        bool chipUp = !bannerUp || countdown.BannerFor - since <= 2f * Tick;
        Show(chipGroup, chipUp);
        if (chipUp)
        {
            if (chipSince < 0f) chipSince = since;
            SetText(digits, ref shownSeconds, countdown.Seconds);
            float fill = Mathf.Clamp01(countdown.Shown / Mathf.Max(.01f, BossWarningConfig.LeadSeconds));
            barFill.localScale = new Vector3(fill, 1f, 1f);

            float rest = stage == BossWarningStage.Final ? 1.08f : stage == BossWarningStage.Close ? 1.04f : 1f;
            // a punch when it lands, and on every beat from T-10
            float sincePop = since - chipSince;
            if (stage >= BossWarningStage.Close) sincePop = Mathf.Min(sincePop, since - countdown.BeatAt);
            float p = sincePop / Tick;
            float punch = p < 1f ? 1.07f : p < 3f ? .96f : 1f;
            float s = layout.chipScale * rest * punch;
            SetScale(chip, s, s);

            if (stage == BossWarningStage.Final)
            {
                // stripes flash bone / accent four times a second
                bool hot = Mathf.FloorToInt(since * 8f) % 2 == 0;
                chipStripes.canvasRenderer.SetColor(hot ? AkiraPalette.Bone : accent);
            }
        }

        // -- edge glow: throbs once a second from T-10 --
        float beat = Mathf.Clamp01(1f - (since - countdown.BeatAt));   // 1 on the second, 0 a second on
        beat *= beat;
        if (stage == BossWarningStage.Close)
            glow = Mathf.Max(glow, BossWarningConfig.CloseVignette + BossWarningConfig.CloseVignettePulse * beat);
        else if (stage == BossWarningStage.Final)
            glow = Mathf.Max(glow, BossWarningConfig.FinalVignette + BossWarningConfig.FinalVignettePulse * beat);
        vignette.canvasRenderer.SetAlpha(glow);

        // -- the 3 - 2 - 1 --
        bool final = stage == BossWarningStage.Final && !bannerUp;
        Show(finalGroup, final);
        if (final)
        {
            SetText(finalDigit, ref shownFinal, countdown.Seconds);
            float p = (since - countdown.BeatAt) / Tick;
            float pop = p < 1f ? 1.22f : p < 3f ? .94f : 1f;
            SetScale(finalDigit.rectTransform, pop, pop);
            float a = p < 14f ? .92f : .6f;
            if (finalGroup.alpha != a) finalGroup.alpha = a;
        }
    }

    void Style(BossWarningStage stage)
    {
        styled = stage;
        digits.color = stage >= BossWarningStage.Close ? accentHi : AkiraPalette.Bone;
        label.color = stage >= BossWarningStage.Close ? AkiraPalette.Bone : AkiraPalette.Muted;
        finalDigit.color = accentHi;
        chipStripes.canvasRenderer.SetColor(stage >= BossWarningStage.Close ? accent : accentDim);
    }

    // The boss intro has begun: the chip bursts, the edges flare and drain.
    void AnimateHandoff()
    {
        float k = handoff / Tick;
        if (k < 2f) SetScale(chip, layout.chipScale * 1.3f, layout.chipScale * 1.3f);
        else if (k < 3f) SetScale(chip, layout.chipScale * 1.6f, layout.chipScale * .4f);
        else Show(chipGroup, false);
        float t = Mathf.Clamp01(handoff / Mathf.Max(.01f, BossWarningConfig.HandoffSeconds));
        float from = Mathf.Max(handoffFrom, BossWarningConfig.HandoffVignette);
        vignette.canvasRenderer.SetAlpha(from * (1f - t) * (1f - t));
        if (t >= 1f) HideAll();
    }

    // ---- small, allocation-free setters ----

    static void Show(CanvasGroup group, bool on)
    {
        float a = on ? 1f : 0f;
        if (on ? group.alpha <= 0f : group.alpha > 0f) group.alpha = a;
    }

    static void SetScale(RectTransform rt, float x, float y)
    {
        Vector3 s = rt.localScale;
        if (s.x != x || s.y != y) rt.localScale = new Vector3(x, y, 1f);
    }

    static void SetText(Text text, ref int shown, int value)
    {
        if (shown == value) return;
        shown = value;
        text.text = Figures[Mathf.Clamp(value, 0, Figures.Length - 1)];
    }
}

public static class BossWarningBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        BossWarning.Countdown = null;
        if (!BossWarningHud.ShouldAttach(scene.name)) return;
        if (Object.FindFirstObjectByType<BossWarningHud>() != null) return;
        BossWarningHud.Build();
    }
}
