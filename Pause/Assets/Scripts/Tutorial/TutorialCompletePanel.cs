using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The end-of-tutorial card: "TUTORIAL COMPLETE" and two choices, LIFT OFF
// (into the game, through the portal: TutorialLiftOff) and HOME. Nothing else.
//
// It is the Flight Complete panel's smaller sibling (DeathPanelView): the same
// frame (dp_panel), red title slab, divider, button plates and glow, the same
// Akira palette (docs/art-style.md), HUD font, button size, safe-area fit and
// pop-in on whole steps -- built on its own root under the scene's
// PopUpCanvas. Like DeathPanelView it moves the scene's own buttons into the
// panel, so their persistent onClick wiring (tutButtonClicks.replay and
// tutButtonClicks.mainMenuButton) is untouched, and switches the old dialog
// off.
public class TutorialCompletePanel : MonoBehaviour, IPointerDownHandler
{
    // ---- Layout (panel space: canvas units, origin at the panel centre) ----

    public const float Width = 680f, Height = 340f;
    public const float FrameMargin = DeathPanelView.GlowMargin;   // the frame's glow outside the body
    public const float ButtonWidth = DeathPanelView.ButtonWidth, ButtonHeight = DeathPanelView.ButtonHeight;

    public static readonly Rect HeaderRect = Centered(0f, 116f, 600f, 56f);
    public static readonly Rect DividerRect = Centered(0f, 70f, 440f, 16f);
    public static readonly Rect LiftOffRect = Centered(-156f, -92f, ButtonWidth, ButtonHeight);
    public static readonly Rect HomeRect = Centered(156f, -92f, ButtonWidth, ButtonHeight);
    public static Rect PanelRect { get { return Centered(0f, 0f, Width, Height); } }

    public const string PlayButtonName = "playMainGameButton";
    public const string MenuButtonName = "MainMenuButton";
    public const string LegacyDialogName = "Model Panel";
    public const string TitleText = "TUTORIAL COMPLETE", LiftOffText = "LIFT OFF", HomeText = "HOME";

    // ---- Timeline (unscaled seconds since shown), in whole steps ----

    const float Step = RobotSpeaker.Step;
    public const float IntroDuration = 8f * Step;
    const int DividerStep = 1, ButtonStep = 3, ButtonStagger = 1;

    static readonly Color Cyan = AkiraPalette.Cyan;
    static readonly Color Coral = AkiraPalette.Red;
    static readonly Color Gold = AkiraPalette.Amber;
    static readonly Color Ink = AkiraPalette.WithAlpha(AkiraPalette.Ink, .95f);
    static readonly Color Bone = AkiraPalette.Bone;
    const string SpriteRoot = "DeathPanel/";

    Font font;
    RectTransform panel;
    CanvasGroup panelGroup;
    Image scrim, divider, slab;
    readonly RectTransform[] sparkles = new RectTransform[2];
    readonly RectTransform[] buttonSlots = new RectTransform[2];
    readonly CanvasGroup[] buttonGroups = new CanvasGroup[2];
    readonly Image[] buttonGlows = new Image[2];
    readonly Text[] buttonLabels = new Text[2];

    float startedAt = -1f;
    bool finalApplied;
    float fitScale = 1f;
    int lastScreenW, lastScreenH;

    public RectTransform Panel { get { return panel; } }
    public RectTransform LiftOffSlot { get { return buttonSlots[0]; } }
    public RectTransform HomeSlot { get { return buttonSlots[1]; } }
    public bool IntroFinished { get { return finalApplied; } }
    public string LiftOffLabel { get { return buttonLabels[0] != null ? buttonLabels[0].text : null; } }
    public string HomeLabel { get { return buttonLabels[1] != null ? buttonLabels[1].text : null; } }
    public string TitleLabel { get { var t = panel != null ? panel.Find("Title") : null; return t != null ? t.GetComponent<Text>().text : null; } }

    // Builds the panel under the scene's PopUpCanvas. Safe to call twice.
    public static TutorialCompletePanel Show()
    {
        var canvasGo = SceneUtil.FindAny("PopUpCanvas");
        if (canvasGo == null) return null;
        canvasGo.SetActive(true);
        var existing = canvasGo.GetComponentInChildren<TutorialCompletePanel>(true);
        if (existing != null) return existing;

        var playGo = SceneUtil.FindAny(PlayButtonName);
        var menuGo = SceneUtil.FindAny(MenuButtonName);
        // The HUD's Orbitron (the dialog's own button labels use the legacy default font).
        var hud = Object.FindFirstObjectByType<score>();
        Font font = hud != null && hud.speedValue != null ? hud.speedValue.font : null;
        return Build(canvasGo.transform,
                     playGo != null ? playGo.GetComponent<Button>() : null,
                     menuGo != null ? menuGo.GetComponent<Button>() : null,
                     font);
    }

    public static TutorialCompletePanel Build(Transform canvasRoot, Button liftOff, Button home, Font font)
    {
        var root = new GameObject("TutorialComplete", typeof(RectTransform));
        root.transform.SetParent(canvasRoot, false);
        var rootRt = (RectTransform)root.transform;
        rootRt.anchorMin = Vector2.zero; rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;
        root.transform.SetAsLastSibling();

        var view = root.AddComponent<TutorialCompletePanel>();
        view.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var legacy = canvasRoot.Find(LegacyDialogName);

        view.BuildScrim(rootRt);
        view.BuildPanel(rootRt);
        view.BuildHeader();
        view.BuildButtons(liftOff, home);

        if (legacy != null) legacy.gameObject.SetActive(false);
        view.ApplyAt(0f);
        return view;
    }

    void BuildScrim(RectTransform root)
    {
        scrim = NewImage("Scrim", root, null, AkiraPalette.WithAlpha(AkiraPalette.Night0, 0f));
        Stretch(scrim.rectTransform);
        scrim.raycastTarget = true;   // blocks the frozen game; a tap skips the intro
    }

    void BuildPanel(RectTransform root)
    {
        var go = new GameObject("Panel", typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(root, false);
        panel = (RectTransform)go.transform;
        Place(panel, PanelRect);
        panelGroup = go.GetComponent<CanvasGroup>();

        var frame = NewImage("Frame", panel, Load("dp_panel"), Color.white);
        frame.type = Image.Type.Sliced;
        frame.raycastTarget = true;
        var r = PanelRect;
        Place(frame.rectTransform, new Rect(r.xMin - FrameMargin, r.yMin - FrameMargin,
                                            r.width + 2f * FrameMargin, r.height + 2f * FrameMargin));
    }

    void BuildHeader()
    {
        // The Akira title-card stripe: a red slab behind the heading.
        slab = NewImage("TitleSlab", panel, Load("dp_slab"), Color.white);
        Place(slab.rectTransform, Centered(0f, HeaderRect.center.y + 2f, 640f, 88f));

        var title = NewText("Title", panel, TitleText, 40, Bone, TextAnchor.MiddleCenter);
        Place(title.rectTransform, HeaderRect);
        AddOutline(title.gameObject, Ink, 2f);

        float half = Mathf.Min(title.preferredWidth, HeaderRect.width - 80f) * .5f + 28f;
        for (int i = 0; i < 2; i++)
        {
            var sparkle = NewImage(i == 0 ? "SparkleLeft" : "SparkleRight", panel, Load("dp_sparkle"), Gold);
            Place(sparkle.rectTransform, Centered(i == 0 ? -half : half, HeaderRect.center.y, 30f, 30f));
            sparkles[i] = sparkle.rectTransform;
        }

        divider = NewImage("Divider", panel, Load("dp_divider"), Color.white);
        divider.preserveAspect = true;
        Place(divider.rectTransform, DividerRect);
    }

    void BuildButtons(Button liftOff, Button home)
    {
        // LIFT OFF goes on (the play glyph, cyan); HOME leaves (the home glyph, red), as MENU does on Flight Complete.
        buttonSlots[0] = BuildButton(0, liftOff, Resources.Load<Sprite>("QuickActions/QuickAction_play" + DeathPanelView.GlyphSuffix), LiftOffText, Cyan, LiftOffRect);
        buttonSlots[1] = BuildButton(1, home, Resources.Load<Sprite>(PauseQuickActions.HomeIconPath + DeathPanelView.GlyphSuffix), HomeText, Coral, HomeRect);
    }

    RectTransform BuildButton(int index, Button button, Sprite glyph, string label, Color accent, Rect rect)
    {
        var slotGo = new GameObject(index == 0 ? "LiftOffSlot" : "HomeSlot", typeof(RectTransform), typeof(CanvasGroup));
        slotGo.transform.SetParent(panel, false);
        var slot = (RectTransform)slotGo.transform;
        Place(slot, rect);
        buttonGroups[index] = slotGo.GetComponent<CanvasGroup>();

        var glow = NewImage("Glow", slot, Load("dp_glow"), new Color(accent.r, accent.g, accent.b, 0f));
        glow.type = Image.Type.Sliced;
        Place(glow.rectTransform, Centered(0f, 0f, rect.width + 40f, rect.height + 40f));
        buttonGlows[index] = glow;

        if (button == null) return slot;

        // The scene's own Button: its persistent onClick stays as authored.
        var rt = (RectTransform)button.transform;
        rt.SetParent(slot, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = rect.size;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        foreach (var le in button.GetComponents<LayoutElement>()) le.ignoreLayout = true;
        button.gameObject.SetActive(true);
        button.interactable = true;
        button.transition = Selectable.Transition.None;   // press feedback is DeathPanelPress

        var frame = button.GetComponent<Image>();
        if (frame != null)
        {
            frame.sprite = Load("dp_button");
            frame.type = Image.Type.Sliced;
            frame.preserveAspect = false;
            frame.color = accent;
            frame.raycastTarget = true;
            button.targetGraphic = frame;
            // the scene buttons' tint transition left a transparent colour on the renderer
            frame.canvasRenderer.SetColor(Color.white);
        }

        var text = button.GetComponentInChildren<Text>(true);
        if (text == null) text = NewText("Label", rt, label, 28, Bone, TextAnchor.MiddleLeft);
        text.text = label;
        text.font = font;
        text.fontSize = 28;
        text.fontStyle = FontStyle.Bold;
        text.color = Bone;
        text.alignment = TextAnchor.MiddleLeft;
        text.resizeTextForBestFit = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.gameObject.SetActive(true);
        foreach (var le in text.GetComponents<LayoutElement>()) le.ignoreLayout = true;
        AddOutline(text.gameObject, Ink, 1.5f);
        buttonLabels[index] = text;

        const float iconSize = 40f, gap = 14f;
        float textWidth = Mathf.Ceil(text.preferredWidth);
        bool showIcon = glyph != null;
        float group = textWidth + (showIcon ? iconSize + gap : 0f);
        float x = -group * .5f;
        if (showIcon)
        {
            var icon = NewImage("Icon", rt, glyph, Color.white);
            icon.preserveAspect = true;
            Place(icon.rectTransform, Centered(x + iconSize * .5f, 0f, iconSize, iconSize));
            x += iconSize + gap;
        }
        Place(text.rectTransform, new Rect(x, -24f, textWidth + 4f, 48f));

        var press = button.GetComponent<DeathPanelPress>() ?? button.gameObject.AddComponent<DeathPanelPress>();
        press.target = rt;
        return slot;
    }

    // ---------------------------------------------------------------------
    // Animation (unscaled time, limited: held poses on whole steps)
    // ---------------------------------------------------------------------

    void Update()
    {
        if (startedAt < 0f) startedAt = Time.unscaledTime;
        if (ScreenInfo.Width != lastScreenW || ScreenInfo.Height != lastScreenH) Fit();
        ApplyAt(Time.unscaledTime - startedAt);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!finalApplied) Skip();
    }

    public void Skip()
    {
        startedAt = Time.unscaledTime - IntroDuration;
        ApplyAt(IntroDuration);
    }

    public void ApplyAt(float t)
    {
        if (t < IntroDuration)
        {
            finalApplied = false;
            AnimateIntro(Mathf.FloorToInt(t / Step));
        }
        else if (!finalApplied)
        {
            AnimateIntro(int.MaxValue / 2);
            finalApplied = true;
        }
        AnimateIdle(Mathf.FloorToInt(t / RobotSpeaker.SlowStep));
    }

    void AnimateIntro(int k)
    {
        SetAlpha(scrim, k <= 0 ? .3f : .55f);
        panelGroup.alpha = 1f;
        float pop = k <= 0 ? .86f : k == 1 ? 1.04f : 1f;
        panel.localScale = Vector3.one * (fitScale * pop);

        float slabScale = k < 0 ? 0f : k == 0 ? .5f : 1f;
        slab.rectTransform.localScale = new Vector3(slabScale, 1f, 1f);
        var d = divider.rectTransform.localScale;
        d.x = k < DividerStep ? 0f : k == DividerStep ? .5f : 1f;
        divider.rectTransform.localScale = d;

        for (int i = 0; i < 2; i++)
        {
            int b = k - (ButtonStep + i * ButtonStagger);
            buttonGroups[i].alpha = b < 0 ? 0f : 1f;
            buttonGroups[i].interactable = b >= 0;
            float drop = b < 0 ? -24f : b == 0 ? 6f : 0f;
            buttonSlots[i].anchoredPosition = (i == 0 ? LiftOffRect : HomeRect).center + new Vector2(0f, drop);
            var gc = buttonGlows[i].color;
            gc.a = b < 0 ? 0f : .35f;
            buttonGlows[i].color = gc;
        }
    }

    void AnimateIdle(int slowStep)
    {
        // Header sparkles alternate between two held sizes.
        bool big = (slowStep & 2) == 0;
        sparkles[0].localScale = Vector3.one * (big ? 1.15f : .85f);
        sparkles[1].localScale = Vector3.one * (big ? .85f : 1.15f);
    }

    // ---------------------------------------------------------------------
    // Fitting: safe area, aspect ratio, the top-right quick actions
    // ---------------------------------------------------------------------

    void Fit()
    {
        lastScreenW = ScreenInfo.Width;
        lastScreenH = ScreenInfo.Height;
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        canvas = canvas.rootCanvas;
        var rootRect = ((RectTransform)transform).rect;
        float sf = Mathf.Max(canvas.scaleFactor, .0001f);

        Rect safe = ScreenInfo.SafeArea;
        var safeUnits = new Rect(safe.x / sf - rootRect.width * .5f, safe.y / sf - rootRect.height * .5f,
                                 safe.width / sf, safe.height / sf);

        Rect? blocker = null;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        var corners = new Vector3[4];
        foreach (string n in new[] { "replayQuickAction", "leaveQuickAction" })
        {
            var go = GameObject.Find(n);
            if (go == null) continue;
            ((RectTransform)go.transform).GetWorldCorners(corners);
            foreach (var c in corners)
            {
                minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x);
                minY = Mathf.Min(minY, c.y); maxY = Mathf.Max(maxY, c.y);
            }
        }
        if (minX <= maxX)
            blocker = new Rect(minX / sf - rootRect.width * .5f, minY / sf - rootRect.height * .5f,
                               (maxX - minX) / sf, (maxY - minY) / sf);

        Vector2 centre;
        ComputeFit(safeUnits, blocker, out centre, out fitScale);
        panel.anchoredPosition = centre;
        if (finalApplied) panel.localScale = Vector3.one * fitScale;
        DeathPanelView.PadButtonTouch(buttonSlots, fitScale, ButtonHeight);
    }

    // Same rule as DeathPanelView.ComputeFit, for this panel's size.
    public static void ComputeFit(Rect safe, Rect? blocker, out Vector2 centre, out float scale)
    {
        const float margin = 12f;
        Rect area = safe;
        for (int pass = 0; pass < 2; pass++)
        {
            // 1.06: the pop-in overshoot must stay on screen too.
            float w = (Width + 2f * FrameMargin) * 1.06f, h = (Height + 2f * FrameMargin) * 1.06f;
            scale = Mathf.Max(.1f, Mathf.Min(1f, (area.width - 2f * margin) / w, (area.height - 2f * margin) / h));
            centre = area.center;
            var visual = new Rect(centre.x - w * scale * .5f, centre.y - h * scale * .5f, w * scale, h * scale);
            if (pass == 0 && blocker.HasValue && blocker.Value.Overlaps(visual))
            {
                area.yMax = Mathf.Min(area.yMax, blocker.Value.yMin - 4f);
                continue;
            }
            return;
        }
        centre = area.center;
        scale = .1f;
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static Sprite Load(string name) { return Resources.Load<Sprite>(SpriteRoot + name); }

    static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    Text NewText(string name, Transform parent, string content, int size, Color color, TextAnchor align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<Text>();
        text.font = font;
        text.text = content;
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.color = color;
        text.alignment = align;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    static void AddOutline(GameObject go, Color color, float distance)
    {
        var o = go.GetComponent<Outline>() ?? go.AddComponent<Outline>();
        o.effectColor = color;
        o.effectDistance = new Vector2(distance, -distance);
    }

    static void Place(RectTransform rt, Rect r)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
        rt.anchoredPosition = r.center;
        rt.sizeDelta = r.size;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static Rect Centered(float x, float y, float w, float h) { return new Rect(x - w * .5f, y - h * .5f, w, h); }

    static void SetAlpha(Graphic g, float a)
    {
        if (g == null) return;
        var c = g.color;
        if (Mathf.Approximately(c.a, a)) return;
        c.a = a;
        g.color = c;
    }
}
