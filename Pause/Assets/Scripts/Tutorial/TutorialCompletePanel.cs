using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The end-of-tutorial card. Same layout grammar as the Flight Complete panel
// (DeathPanelView): header, divider, accent stat cards and two equal-width
// buttons, built on its own root and fitted to the safe area below the
// quick actions. Drawn in the tutorial's painted brass style (steampunk
// palette, ink outlines, chamfered panels, TutorialPalette) with limited
// animation: held poses that snap on whole steps.
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
    public const float FrameMargin = 6f;      // tut_bubble's drop shadow outside the body
    public const float CardWidth = 600f;
    public const float ButtonWidth = 288f, ButtonHeight = 100f;
    const float LabelLeft = -CardWidth * .5f + 44f;
    const float ValueRight = CardWidth * .5f - 28f;

    public static readonly Rect HeaderRect = Centered(0f, 236f, 600f, 56f);
    public static readonly Rect DividerRect = Centered(0f, 196f, 440f, 6f);
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

    // ---- Timeline (unscaled seconds since shown), in whole steps ----

    const float Step = RobotSpeaker.Step;
    public const float IntroDuration = 14f * Step;
    const int CardStep = 3, CardStagger = 1, FooterStep = 6, ButtonStep = 7, ButtonStagger = 1;
    const int DustCountFrom = 4, DustCountSteps = 6;

    static readonly Color Accent = TutorialPalette.Orange;

    float practiceDust;
    int realRunPauses;
    Font font;
    RectTransform panel;
    Image scrim;
    RectTransform divider;
    readonly RectTransform[] sparkles = new RectTransform[2];
    readonly RectTransform[] cards = new RectTransform[2];
    readonly CanvasGroup[] cardGroups = new CanvasGroup[2];
    Text dustValue;
    CanvasGroup footer;
    readonly RectTransform[] buttonSlots = new RectTransform[2];
    readonly CanvasGroup[] buttonGroups = new CanvasGroup[2];
    CanvasGroup panelGroup;

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
        // The HUD's Orbitron (the dialog's own button labels use the legacy
        // default font).
        var hud = Object.FindFirstObjectByType<score>();
        Font font = hud != null && hud.speedValue != null ? hud.speedValue.font : null;
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
        scrim = NewImage("Scrim", root, null, new Color(TutorialPalette.Night.r, TutorialPalette.Night.g, TutorialPalette.Night.b, 0f));
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

        var frame = NewImage("Frame", panel, Load("tut_bubble"), Color.white);
        frame.type = Image.Type.Sliced;
        frame.raycastTarget = true;
        var r = PanelRect;
        Place(frame.rectTransform, new Rect(r.xMin - FrameMargin, r.yMin - FrameMargin,
                                            r.width + 2f * FrameMargin, r.height + 2f * FrameMargin));
    }

    void BuildHeader()
    {
        var title = NewText("Title", panel, "TUTORIAL COMPLETE", 40, Accent, TextAnchor.MiddleCenter);
        Place(title.rectTransform, HeaderRect);
        AddOutline(title.gameObject, TutorialPalette.Ink, 3f);

        float half = Mathf.Min(title.preferredWidth, HeaderRect.width - 80f) * .5f + 28f;
        for (int i = 0; i < 2; i++)
        {
            var spark = NewImage(i == 0 ? "SparkLeft" : "SparkRight", panel, Load("tut_glow"), TutorialPalette.Red);
            Place(spark.rectTransform, Centered(i == 0 ? -half : half, HeaderRect.center.y, 30f, 30f));
            sparkles[i] = spark.rectTransform;
        }

        // A flat orange rule with an ink edge.
        var rule = NewImage("Divider", panel, null, Accent);
        Place(rule.rectTransform, DividerRect);
        AddOutline(rule.gameObject, TutorialPalette.Ink, 2f);
        divider = rule.rectTransform;
    }

    void BuildCards()
    {
        Text pausesValue;
        cards[0] = BuildCard(0, "STAR DUST", "PRACTICE, NOT SAVED", TutorialPalette.Orange, out dustValue);
        cards[1] = BuildCard(1, "PAUSES", "PER REAL RUN", TutorialPalette.Teal, out pausesValue);
        pausesValue.text = realRunPauses.ToString();

        var footerText = NewText("Footer", panel, "Replay this anytime from the leaderboard.", 20, TutorialPalette.Muted, TextAnchor.MiddleCenter);
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

        var bg = NewImage("Background", card, Load("tut_card"), Color.white);   // painted brass card, accent is the label
        bg.type = Image.Type.Sliced;
        Stretch(bg.rectTransform);

        var title = NewText("Label", card, label, 26, accent, TextAnchor.MiddleLeft);
        Place(title.rectTransform, Centered(LabelLeft + 150f, 18f, 300f, 36f));
        AddOutline(title.gameObject, TutorialPalette.Ink, 2f);
        var subText = NewText("Sub", card, sub, 18, TutorialPalette.Muted, TextAnchor.MiddleLeft);
        Place(subText.rectTransform, Centered(LabelLeft + 150f, -20f, 300f, 28f));

        value = NewText("Value", card, "", 56, TutorialPalette.Paper, TextAnchor.MiddleRight);
        Place(value.rectTransform, Centered(ValueRight - 130f, 0f, 260f, 80f));
        AddOutline(value.gameObject, TutorialPalette.Ink, 3f);
        return card;
    }

    void BuildButtons(Button play, Button menu)
    {
        // PLAY is the hero action: Kaneda red. MENU is secondary steel.
        buttonSlots[0] = BuildButton(0, play, Resources.Load<Sprite>("QuickActions/QuickAction_play" + DeathPanelView.GlyphSuffix), "PLAY", TutorialPalette.Red, PlayRect);
        buttonSlots[1] = BuildButton(1, menu, Resources.Load<Sprite>(PauseQuickActions.HomeIconPath + DeathPanelView.GlyphSuffix), "MENU", TutorialPalette.Steel, MenuRect);
    }

    RectTransform BuildButton(int index, Button button, Sprite glyph, string label, Color accent, Rect rect)
    {
        var slotGo = new GameObject(label == "PLAY" ? "PlaySlot" : "MenuSlot", typeof(RectTransform), typeof(CanvasGroup));
        slotGo.transform.SetParent(panel, false);
        var slot = (RectTransform)slotGo.transform;
        Place(slot, rect);
        buttonGroups[index] = slotGo.GetComponent<CanvasGroup>();

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
            frame.sprite = Load("tut_button");
            frame.type = Image.Type.Sliced;
            frame.preserveAspect = false;
            frame.color = Color.white;   // painted brass plate; the label carries the colour
            frame.raycastTarget = true;
            button.targetGraphic = frame;
            // The scene buttons used a colour-tint transition whose normal
            // colour is transparent; an inactive button keeps that tint on
            // its renderer even after the transition is switched off.
            frame.canvasRenderer.SetColor(Color.white);
        }

        var text = button.GetComponentInChildren<Text>(true);
        if (text == null) text = NewText("Label", rt, label, 28, TutorialPalette.Paper, TextAnchor.MiddleLeft);
        text.text = label;
        text.font = font;
        text.fontSize = 28;
        text.fontStyle = FontStyle.Bold;
        text.color = TutorialPalette.Paper;
        text.alignment = TextAnchor.MiddleLeft;
        text.resizeTextForBestFit = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.gameObject.SetActive(true);
        foreach (var le in text.GetComponents<LayoutElement>()) le.ignoreLayout = true;
        AddOutline(text.gameObject, TutorialPalette.Ink, 2f);

        const float iconSize = 40f, gap = 14f;
        float textWidth = Mathf.Ceil(text.preferredWidth);
        float group = textWidth + (glyph != null ? iconSize + gap : 0f);
        float x = -group * .5f;
        if (glyph != null)
        {
            var icon = NewImage("Icon", rt, glyph, Color.white);
            icon.preserveAspect = true;
            Place(icon.rectTransform, Centered(x + iconSize * .5f, 4f, iconSize, iconSize));
            x += iconSize + gap;
        }
        // Nudged up a little: the button art's bottom band is its shade.
        Place(text.rectTransform, new Rect(x, -20f, textWidth + 4f, 48f));

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
        float pop = k <= 0 ? .8f : k == 1 ? 1.06f : 1f;
        panel.localScale = Vector3.one * (fitScale * pop);

        var d = divider.localScale;
        d.x = k < 1 ? 0f : k == 1 ? .4f : k == 2 ? .8f : 1f;
        divider.localScale = d;

        for (int i = 0; i < cards.Length; i++)
        {
            int c = k - (CardStep + i * CardStagger);
            cardGroups[i].alpha = c < 0 ? 0f : 1f;
            float slide = c < 0 ? 48f : c == 0 ? 20f : c == 1 ? -4f : 0f;
            cards[i].anchoredPosition = CardRects[i].center + new Vector2(slide, 0f);
        }
        footer.alpha = k < FooterStep ? 0f : 1f;

        float p = Mathf.Clamp01((k - DustCountFrom) / (float)DustCountSteps);
        int cents = Mathf.RoundToInt(practiceDust * p * 100f);
        if (cents != shownDustCents)
        {
            shownDustCents = cents;
            dustValue.text = "+" + (cents / 100f).ToString("F2");
        }

        for (int i = 0; i < 2; i++)
        {
            int b = k - (ButtonStep + i * ButtonStagger);
            buttonGroups[i].alpha = b < 0 ? 0f : 1f;
            buttonGroups[i].interactable = b >= 0;
            float drop = b < 0 ? -24f : b == 0 ? 6f : 0f;
            buttonSlots[i].anchoredPosition = (i == 0 ? PlayRect : MenuRect).center + new Vector2(0f, drop);
        }
    }

    void AnimateIdle(int slowStep)
    {
        // Header sparks alternate between two held sizes.
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

    static Sprite Load(string name) { return Resources.Load<Sprite>("Tutorial/" + name); }

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
