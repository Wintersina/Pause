using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The end-of-tutorial card, from the same family as the Flight Complete panel
// (DeathPanelView): same dark-glass frame, gold header with sparkles, accent
// cards and equal-width neon buttons, the same pop-with-overshoot intro on
// unscaled time.
//
// It replaces the scene's grey "End of tutorial" dialog (Model Panel, which
// still pointed players at an OPTIONS menu that no longer exists) and the
// loose blue "CONTINUE TO GAME" button TutorialFinishContinue used to float
// over it. Like DeathPanelView it moves the scene's own buttons into the new
// panel, so their persistent onClick wiring (tutButtonClicks.replay and
// tutButtonClicks.mainMenuButton) is untouched, then switches the old dialog
// off.
public class TutorialCompletePanel : MonoBehaviour, IPointerDownHandler
{
    // ---- Layout (panel space: canvas units, origin at the panel centre) ----

    public const float Width = 680f, Height = 600f;
    public const float GlowMargin = 20f;
    public const float CardWidth = 600f;
    public const float ButtonWidth = 288f, ButtonHeight = 100f;
    const float LabelLeft = -CardWidth * .5f + 40f;
    const float ValueRight = CardWidth * .5f - 28f;

    public static readonly Rect HeaderRect = Centered(0f, 236f, 600f, 56f);
    public static readonly Rect DividerRect = Centered(0f, 192f, 440f, 16f);
    public static readonly Rect[] CardRects =
    {
        Centered(0f, 110f, CardWidth, 112f),   // practice star dust
        Centered(0f, -14f, CardWidth, 112f),   // pauses in a real run
    };
    public static readonly Rect FooterRect = Centered(0f, -104f, 600f, 40f);
    public static readonly Rect PlayRect = Centered(-156f, -214f, ButtonWidth, ButtonHeight);
    public static readonly Rect MenuRect = Centered(156f, -214f, ButtonWidth, ButtonHeight);
    public static Rect PanelRect { get { return Centered(0f, 0f, Width, Height); } }

    public const string PlayButtonName = "playMainGameButton";
    public const string MenuButtonName = "MainMenuButton";
    public const string LegacyDialogName = "Model Panel";

    // ---- Timeline (unscaled seconds since shown) ----

    public const float IntroDuration = 1f;
    const float PanelIn = .38f;
    const float CardStart = .18f, CardStagger = .1f, CardDuration = .32f;
    const float DustCountFrom = .3f, DustCountTo = .75f;
    const float ButtonsStart = .55f, ButtonStagger = .07f, ButtonDuration = .3f;

    static readonly Color Cyan = new Color(.32f, .9f, 1f);
    static readonly Color Coral = new Color(1f, .45f, .35f);
    static readonly Color Gold = new Color(1f, .79f, .26f);
    static readonly Color Green = new Color(.62f, 1f, .70f);   // HudStyler's pause colour
    static readonly Color Ink = new Color(0f, .03f, .12f, .9f);
    static readonly Color Muted = new Color(.8f, .87f, 1f, .6f);

    float practiceDust;
    int realRunPauses;
    Font font;
    RectTransform panel;
    CanvasGroup panelGroup;
    Image scrim, divider;
    readonly RectTransform[] sparkles = new RectTransform[2];
    readonly RectTransform[] cards = new RectTransform[2];
    readonly CanvasGroup[] cardGroups = new CanvasGroup[2];
    Text dustValue;
    CanvasGroup footer;
    readonly RectTransform[] buttonSlots = new RectTransform[2];
    readonly CanvasGroup[] buttonGroups = new CanvasGroup[2];
    readonly Image[] buttonGlows = new Image[2];
    readonly DeathPanelPress[] presses = new DeathPanelPress[2];

    float startedAt = -1f;
    bool finalApplied;
    float fitScale = 1f;
    int lastScreenW, lastScreenH;
    int shownDustCents = -1;

    public RectTransform Panel { get { return panel; } }
    public RectTransform PlaySlot { get { return buttonSlots[0]; } }
    public RectTransform MenuSlot { get { return buttonSlots[1]; } }
    public bool IntroFinished { get { return finalApplied; } }

    // Builds the panel under the scene's PopUpCanvas. Safe to call twice.
    public static TutorialCompletePanel Show(float practiceDust, int realRunPauses)
    {
        var canvasGo = SceneUtil.FindAny("PopUpCanvas");
        if (canvasGo == null) return null;
        canvasGo.SetActive(true);
        var existing = canvasGo.GetComponentInChildren<TutorialCompletePanel>(true);
        if (existing != null) return existing;

        var playGo = SceneUtil.FindAny(PlayButtonName);
        var menuGo = SceneUtil.FindAny(MenuButtonName);
        // The HUD's Orbitron, like the Flight Complete panel (the dialog's
        // own button labels use the legacy default font).
        var hud = Object.FindFirstObjectByType<score>();
        Font font = hud != null && hud.currencyText != null ? hud.currencyText.font : null;
        return Build(canvasGo.transform,
                     playGo != null ? playGo.GetComponent<Button>() : null,
                     menuGo != null ? menuGo.GetComponent<Button>() : null,
                     font, practiceDust, realRunPauses);
    }

    public static TutorialCompletePanel Build(Transform canvasRoot, Button play, Button menu, Font font,
                                              float practiceDust, int realRunPauses)
    {
        var root = new GameObject("TutorialComplete", typeof(RectTransform));
        root.transform.SetParent(canvasRoot, false);
        var rootRt = (RectTransform)root.transform;
        rootRt.anchorMin = Vector2.zero; rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;
        root.transform.SetAsLastSibling();

        var view = root.AddComponent<TutorialCompletePanel>();
        view.practiceDust = practiceDust;
        view.realRunPauses = realRunPauses;
        view.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var legacy = canvasRoot.Find(LegacyDialogName);

        view.BuildScrim(rootRt);
        view.BuildPanel();
        view.BuildHeader();
        view.BuildCards();
        view.BuildButtons(play, menu);

        if (legacy != null) legacy.gameObject.SetActive(false);
        view.ApplyAt(0f);
        return view;
    }

    void BuildScrim(RectTransform root)
    {
        scrim = NewImage("Scrim", root, null, new Color(0f, .01f, .05f, 0f));
        Stretch(scrim.rectTransform);
        scrim.raycastTarget = true;   // blocks the frozen game; a tap skips the intro
    }

    void BuildPanel()
    {
        var go = new GameObject("Panel", typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(transform, false);
        panel = (RectTransform)go.transform;
        Place(panel, PanelRect);
        panelGroup = go.GetComponent<CanvasGroup>();

        var frame = NewImage("Frame", panel, LoadPanel("dp_panel"), Color.white);
        frame.type = Image.Type.Sliced;
        frame.raycastTarget = true;
        var r = PanelRect;
        Place(frame.rectTransform, new Rect(r.xMin - GlowMargin, r.yMin - GlowMargin,
                                            r.width + 2f * GlowMargin, r.height + 2f * GlowMargin));
    }

    void BuildHeader()
    {
        var title = NewText("Title", panel, "TUTORIAL COMPLETE", 40, Gold, TextAnchor.MiddleCenter);
        Place(title.rectTransform, HeaderRect);
        AddOutline(title.gameObject, Ink, 2f);

        float half = Mathf.Min(title.preferredWidth, HeaderRect.width - 80f) * .5f + 28f;
        for (int i = 0; i < 2; i++)
        {
            var sparkle = NewImage(i == 0 ? "SparkleLeft" : "SparkleRight", panel, LoadPanel("dp_sparkle"), Gold);
            Place(sparkle.rectTransform, Centered(i == 0 ? -half : half, HeaderRect.center.y, 30f, 30f));
            sparkles[i] = sparkle.rectTransform;
        }

        divider = NewImage("Divider", panel, LoadPanel("dp_divider"), Color.white);
        divider.preserveAspect = true;
        Place(divider.rectTransform, DividerRect);
    }

    void BuildCards()
    {
        Text pausesValue;
        cards[0] = BuildCard(0, "STAR DUST", "PRACTICE, NOT SAVED", Gold, out dustValue);
        cards[1] = BuildCard(1, "PAUSES", "PER REAL RUN", Green, out pausesValue);
        pausesValue.text = realRunPauses.ToString();

        var footerText = NewText("Footer", panel, "Replay this anytime from the leaderboard.", 20, Muted, TextAnchor.MiddleCenter);
        Place(footerText.rectTransform, FooterRect);
        footer = footerText.gameObject.AddComponent<CanvasGroup>();
    }

    RectTransform BuildCard(int index, string label, string sub, Color accent, out Text value)
    {
        var go = new GameObject("Card" + index, typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(panel, false);
        var card = (RectTransform)go.transform;
        Place(card, CardRects[index]);
        cardGroups[index] = go.GetComponent<CanvasGroup>();

        var bg = NewImage("Background", card, LoadPanel("dp_card"), accent);
        bg.type = Image.Type.Sliced;
        Stretch(bg.rectTransform);

        var title = NewText("Label", card, label, 26, accent, TextAnchor.MiddleLeft);
        Place(title.rectTransform, Centered(LabelLeft + 150f, 18f, 300f, 36f));
        AddOutline(title.gameObject, Ink, 1.5f);
        var subText = NewText("Sub", card, sub, 18, Muted, TextAnchor.MiddleLeft);
        Place(subText.rectTransform, Centered(LabelLeft + 150f, -20f, 300f, 28f));

        value = NewText("Value", card, "", 56, Color.Lerp(Color.white, accent, .35f), TextAnchor.MiddleRight);
        Place(value.rectTransform, Centered(ValueRight - 130f, 0f, 260f, 80f));
        AddOutline(value.gameObject, Ink, 2f);
        return card;
    }

    void BuildButtons(Button play, Button menu)
    {
        buttonSlots[0] = BuildButton(0, play, Resources.Load<Sprite>("QuickActions/QuickAction_play"), "PLAY", Cyan, PlayRect);
        buttonSlots[1] = BuildButton(1, menu, Resources.Load<Sprite>("QuickActions/QuickAction_home"), "MENU", Coral, MenuRect);
    }

    RectTransform BuildButton(int index, Button button, Sprite glyph, string label, Color accent, Rect rect)
    {
        var slotGo = new GameObject(label == "PLAY" ? "PlaySlot" : "MenuSlot", typeof(RectTransform), typeof(CanvasGroup));
        slotGo.transform.SetParent(panel, false);
        var slot = (RectTransform)slotGo.transform;
        Place(slot, rect);
        buttonGroups[index] = slotGo.GetComponent<CanvasGroup>();

        var glow = NewImage("Glow", slot, LoadPanel("dp_glow"), new Color(accent.r, accent.g, accent.b, 0f));
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
        button.transition = Selectable.Transition.None;

        var frame = button.GetComponent<Image>();
        if (frame != null)
        {
            frame.sprite = LoadPanel("dp_button");
            frame.type = Image.Type.Sliced;
            frame.preserveAspect = false;
            frame.color = accent;
            frame.raycastTarget = true;
            button.targetGraphic = frame;
            // The scene buttons used a colour-tint transition whose normal
            // colour is transparent; an inactive button keeps that tint on
            // its renderer even after the transition is switched off.
            frame.canvasRenderer.SetColor(Color.white);
        }

        var text = button.GetComponentInChildren<Text>(true);
        if (text == null) text = NewText("Label", rt, label, 28, Color.white, TextAnchor.MiddleLeft);
        text.text = label;
        text.font = font;
        text.fontSize = 28;
        text.fontStyle = FontStyle.Bold;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleLeft;
        text.resizeTextForBestFit = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.gameObject.SetActive(true);
        foreach (var le in text.GetComponents<LayoutElement>()) le.ignoreLayout = true;
        AddOutline(text.gameObject, Ink, 1.5f);

        const float iconSize = 40f, gap = 14f;
        float textWidth = Mathf.Ceil(text.preferredWidth);
        float group = textWidth + (glyph != null ? iconSize + gap : 0f);
        float x = -group * .5f;
        if (glyph != null)
        {
            var icon = NewImage("Icon", rt, glyph, Color.white);
            icon.preserveAspect = true;
            Place(icon.rectTransform, Centered(x + iconSize * .5f, 0f, iconSize, iconSize));
            x += iconSize + gap;
        }
        Place(text.rectTransform, new Rect(x, -24f, textWidth + 4f, 48f));

        var press = button.GetComponent<DeathPanelPress>() ?? button.gameObject.AddComponent<DeathPanelPress>();
        press.target = rt;
        presses[index] = press;
        return slot;
    }

    // ---------------------------------------------------------------------
    // Animation (unscaled time)
    // ---------------------------------------------------------------------

    void Update()
    {
        if (startedAt < 0f) startedAt = Time.unscaledTime;
        if (Screen.width != lastScreenW || Screen.height != lastScreenH) Fit();
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
            AnimateIntro(t);
        }
        else if (!finalApplied)
        {
            AnimateIntro(IntroDuration);
            finalApplied = true;
        }
        AnimateIdle(t);
    }

    void AnimateIntro(float t)
    {
        SetAlpha(scrim, .5f * EaseOutCubic(t / .3f));
        panelGroup.alpha = EaseOutCubic(t / .2f);
        panel.localScale = Vector3.one * (fitScale * Mathf.LerpUnclamped(.86f, 1f, EaseOutBack(t / PanelIn)));

        var d = divider.rectTransform.localScale;
        d.x = EaseOutCubic((t - .14f) / .32f);
        divider.rectTransform.localScale = d;

        for (int i = 0; i < cards.Length; i++)
        {
            float c = EaseOutCubic((t - (CardStart + i * CardStagger)) / CardDuration);
            cardGroups[i].alpha = c;
            cards[i].anchoredPosition = CardRects[i].center + new Vector2((1f - c) * 48f, 0f);
        }
        footer.alpha = EaseOutCubic((t - (CardStart + 2f * CardStagger)) / CardDuration);

        float won = practiceDust * EaseOutCubic(Mathf.Clamp01((t - DustCountFrom) / (DustCountTo - DustCountFrom)));
        int cents = Mathf.RoundToInt(won * 100f);
        if (cents != shownDustCents)
        {
            shownDustCents = cents;
            dustValue.text = "+" + (cents / 100f).ToString("F2");
        }

        for (int i = 0; i < 2; i++)
        {
            float b = EaseOutCubic((t - (ButtonsStart + i * ButtonStagger)) / ButtonDuration);
            buttonGroups[i].alpha = b;
            buttonGroups[i].interactable = b > .5f;
            buttonSlots[i].anchoredPosition = (i == 0 ? PlayRect : MenuRect).center + new Vector2(0f, (1f - b) * -24f);
        }
    }

    void AnimateIdle(float t)
    {
        float settle = Mathf.Clamp01((t - ButtonsStart) / .5f);
        for (int i = 0; i < 2; i++)
        {
            // The primary action (PLAY) breathes a little stronger.
            float breathe = .5f + .5f * Mathf.Sin(t * 2.2f + i * 1.6f);
            float strength = i == 0 ? 1.3f : 1f;
            float pressed = presses[i] != null ? presses[i].Pressed01 : 0f;
            SetAlpha(buttonGlows[i], settle * strength * (.16f + .14f * breathe) + .4f * pressed);
        }
        float twinkle = 1f + .1f * Mathf.Sin(t * 3.1f);
        sparkles[0].localScale = Vector3.one * twinkle;
        sparkles[1].localScale = Vector3.one * (2f - twinkle);
    }

    // ---------------------------------------------------------------------
    // Fitting: safe area, aspect ratio, the top-right quick actions
    // ---------------------------------------------------------------------

    void Fit()
    {
        lastScreenW = Screen.width;
        lastScreenH = Screen.height;
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        canvas = canvas.rootCanvas;
        var rootRect = ((RectTransform)transform).rect;
        float sf = Mathf.Max(canvas.scaleFactor, .0001f);

        Rect safe = Screen.safeArea;
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
    }

    // Same rule as DeathPanelView.ComputeFit, for this panel's size.
    public static void ComputeFit(Rect safe, Rect? blocker, out Vector2 centre, out float scale)
    {
        const float margin = 12f;
        Rect area = safe;
        for (int pass = 0; pass < 2; pass++)
        {
            float w = Width + 2f * GlowMargin, h = Height + 2f * GlowMargin;
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

    static Sprite LoadPanel(string name) { return Resources.Load<Sprite>("DeathPanel/" + name); }

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

    static float EaseOutCubic(float x) { x = Mathf.Clamp01(x); float i = 1f - x; return 1f - i * i * i; }
    static float EaseOutBack(float x)
    {
        x = Mathf.Clamp01(x);
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float m = x - 1f;
        return 1f + c3 * m * m * m + c1 * m * m;
    }
}
