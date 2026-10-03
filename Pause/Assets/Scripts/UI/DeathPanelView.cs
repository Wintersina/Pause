using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The "FLIGHT COMPLETE" results panel shown when a run ends.
//
// It used to be assembled at runtime *inside* the scene's VerticalLayoutGroup
// (PanelBackground): the layout group kept resizing the runtime badge to its
// own 296x150 slot while the cards were hand-placed across ~270px around it,
// so the badge's cyan outline cut through the cards, the scene's grey dialog
// and black ResultsPanel sat offset behind everything, and the leftover dust
// particles never faded out. This builds one self-contained panel on its own
// root under PopUpCanvas, moves the scene's stat Texts and Replay/Menu Buttons
// into it (so their serialized references and onClick wiring stay intact) and
// switches the old scene-authored dialog off entirely, so nothing authored in
// the scene can fight the layout any more.
//
// Every position comes from the constants below, which is what DeathPanelTest
// checks. All animation runs on unscaled time -- the world is frozen at
// timeScale 0 while this is on screen.
public class DeathPanelView : MonoBehaviour, IPointerDownHandler
{
    public struct Results
    {
        public int bestSpeed;
        public int runSpeed;
        public bool newBest;
        public float dustAtStart;
        public float dustWon;
    }

    // ---- Layout (panel space: canvas units, origin at the panel centre) ----

    public const float Width = 680f, Height = 690f;
    // How far the frame sprite's neon bloom reaches outside the panel body.
    public const float GlowMargin = 20f;
    public const float CardWidth = 600f;
    public const float ButtonWidth = 288f, ButtonHeight = 100f;
    // Card-local columns: labels start right of the accent bar, values end
    // with the same inset from the right edge on every row.
    const float LabelLeft = -CardWidth * .5f + 40f;
    const float ValueRight = CardWidth * .5f - 28f;

    public static readonly Rect HeaderRect = Centered(0f, 281f, 600f, 56f);
    public static readonly Rect DividerRect = Centered(0f, 237f, 440f, 16f);
    public static readonly Rect[] CardRects =
    {
        Centered(0f, 149f, CardWidth, 112f),   // best speed
        Centered(0f, 21f, CardWidth, 112f),    // this run
        Centered(0f, -115f, CardWidth, 128f),  // star dust
    };
    public static readonly Rect ReplayRect = Centered(-156f, -259f, ButtonWidth, ButtonHeight);
    public static readonly Rect MenuRect = Centered(156f, -259f, ButtonWidth, ButtonHeight);
    public static Rect PanelRect { get { return Centered(0f, 0f, Width, Height); } }

    // ---- Timeline (seconds of unscaled time since the panel appeared) ----

    public const float IntroDuration = 1.2f;
    const float PanelIn = .38f;
    const float CardStart = .2f, CardStagger = .1f, CardDuration = .32f;
    const float BestCountFrom = .24f, BestCountTo = .64f;
    const float RunCountFrom = .34f, RunCountTo = .78f;
    const float DustCountFrom = .44f, DustCountTo = .84f;
    const float NewBestAt = .6f;
    const float DustBurstAt = .82f, BurstDuration = .38f;
    const float ButtonsStart = .66f, ButtonStagger = .07f, ButtonDuration = .3f;

    static readonly Color Cyan = new Color(.32f, .9f, 1f);
    static readonly Color Coral = new Color(1f, .45f, .35f);
    static readonly Color Gold = new Color(1f, .79f, .26f);
    static readonly Color Ink = new Color(0f, .03f, .12f, .9f);
    static readonly Color Muted = new Color(.8f, .87f, 1f, .6f);

    const string SpriteRoot = "DeathPanel/";
    const int SparklesPerBurst = 10;

    // ---- Built state (all cached; nothing is looked up per frame) ----

    Results results;
    Font font;
    RectTransform panel;
    CanvasGroup panelGroup;
    Image scrim, divider, bestGlow;
    RectTransform[] headerSparkles = new RectTransform[2];
    RectTransform[] cards = new RectTransform[3];
    CanvasGroup[] cardGroups = new CanvasGroup[3];
    Text bestValue, runValue, dustValue, dustTotal;
    RectTransform pill;
    RectTransform[] buttonSlots = new RectTransform[2];
    CanvasGroup[] buttonGroups = new CanvasGroup[2];
    Image[] buttonGlows = new Image[2];
    DeathPanelPress[] presses = new DeathPanelPress[2];
    Image[] dustBurst, bestBurst;
    Vector2 dustValueCentre, bestValueCentre;

    float startedAt = -1f;
    bool finalApplied;
    float fitScale = 1f;
    int lastScreenW, lastScreenH;
    int shownBest = -1, shownRun = -1, shownDustCents = -1;

    public RectTransform Panel { get { return panel; } }
    public RectTransform ReplaySlot { get { return buttonSlots[0]; } }
    public RectTransform MenuSlot { get { return buttonSlots[1]; } }
    public bool IntroFinished { get { return finalApplied; } }

    // ---------------------------------------------------------------------
    // Building
    // ---------------------------------------------------------------------

    public static DeathPanelView Build(Transform canvasRoot, Text bestText, Text runText, Text dustText,
                                       Button replay, Button menu, Results results)
    {
        var old = canvasRoot.Find("FlightComplete");
        if (old != null) DestroyImmediate(old.gameObject);

        var root = new GameObject("FlightComplete", typeof(RectTransform));
        root.transform.SetParent(canvasRoot, false);
        var rootRt = (RectTransform)root.transform;
        rootRt.anchorMin = Vector2.zero; rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;
        root.transform.SetAsLastSibling();

        var view = root.AddComponent<DeathPanelView>();
        view.results = results;
        view.font = dustText.font;

        // Capture each button's current glyph before the button is re-skinned.
        Sprite replayGlyph = ResolveIcon("replayQuickAction", "replayWhenPausedButton", OwnSprite(replay));
        Sprite menuGlyph = ResolveIcon("leaveQuickAction", "mainMenuWhenPausedButton", OwnSprite(menu));

        // The old scene dialog: retire it once its parts have been moved out.
        Transform legacyDialog = dustText.transform;
        while (legacyDialog.parent != null && legacyDialog.parent != canvasRoot) legacyDialog = legacyDialog.parent;

        view.BuildScrim(rootRt);
        view.BuildPanel(rootRt);
        view.BuildHeader();
        view.BuildCards(bestText, runText, dustText);
        view.BuildButtons(replay, replayGlyph, menu, menuGlyph);
        view.dustBurst = view.BuildBurst("DustBurst", Gold);
        view.bestBurst = view.BuildBurst("BestBurst", Cyan);

        if (legacyDialog != null && legacyDialog != root.transform && legacyDialog.parent == canvasRoot)
            legacyDialog.gameObject.SetActive(false);

        view.ApplyAt(0f);
        return view;
    }

    void BuildScrim(RectTransform root)
    {
        scrim = NewImage("Scrim", root, null, new Color(0f, .01f, .05f, 0f));
        var rt = scrim.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        // Full-screen raycast blocker: taps outside the panel neither reach the
        // frozen game nor get lost -- they skip the intro (OnPointerDown).
        scrim.raycastTarget = true;
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
        Place(frame.rectTransform, new Rect(r.xMin - GlowMargin, r.yMin - GlowMargin,
                                            r.width + 2f * GlowMargin, r.height + 2f * GlowMargin));
    }

    void BuildHeader()
    {
        var title = NewText("Title", panel, "FLIGHT COMPLETE", 40, Gold, TextAnchor.MiddleCenter);
        Place(title.rectTransform, HeaderRect);
        AddOutline(title.gameObject, Ink, 2f);

        float half = Mathf.Min(title.preferredWidth, HeaderRect.width - 80f) * .5f + 28f;
        for (int i = 0; i < 2; i++)
        {
            var sparkle = NewImage(i == 0 ? "SparkleLeft" : "SparkleRight", panel, Load("dp_sparkle"), Gold);
            float x = i == 0 ? -half : half;
            Place(sparkle.rectTransform, Centered(x, HeaderRect.center.y, 30f, 30f));
            headerSparkles[i] = sparkle.rectTransform;
        }

        divider = NewImage("Divider", panel, Load("dp_divider"), Color.white);
        divider.preserveAspect = true;
        Place(divider.rectTransform, DividerRect);
    }

    void BuildCards(Text bestText, Text runText, Text dustText)
    {
        cards[0] = BuildCard(0, "BEST SPEED", results.newBest ? null : "ALL-TIME", Cyan, bestText, out bestValue);
        cards[1] = BuildCard(1, "THIS RUN", "SPEED", Coral, runText, out runValue);
        cards[2] = BuildCard(2, "STAR DUST", "EARNED THIS RUN", Gold, dustText, out dustValue);

        // Star dust: the earned amount and the new total on separate lines,
        // both right-aligned on the same column as the speed values.
        Place(dustValue.rectTransform, Centered(ValueRight - 150f, 16f, 300f, 64f));
        dustValue.fontSize = 48;
        dustTotal = NewText("Total", cards[2], "", 22, Muted, TextAnchor.MiddleRight);
        Place(dustTotal.rectTransform, Centered(ValueRight - 150f, -32f, 300f, 30f));

        // Burst origins: roughly the middle of the right-aligned digits.
        dustValueCentre = CardRects[2].center + new Vector2(ValueRight - 95f, 16f);
        bestValueCentre = CardRects[0].center + new Vector2(ValueRight - 60f, 0f);

        if (results.newBest)
        {
            // Celebratory bloom behind the best-speed card ...
            bestGlow = NewImage("NewBestGlow", panel, Load("dp_glow"), new Color(Gold.r, Gold.g, Gold.b, 0f));
            bestGlow.type = Image.Type.Sliced;
            var c = CardRects[0];
            Place(bestGlow.rectTransform, new Rect(c.xMin - 20f, c.yMin - 20f, c.width + 40f, c.height + 40f));
            bestGlow.rectTransform.SetSiblingIndex(cards[0].GetSiblingIndex());

            // ... and a gold "NEW BEST" capsule in the sub-label slot.
            var p = NewImage("NewBest", cards[0], Load("dp_pill"), Gold);
            p.type = Image.Type.Sliced;
            Place(p.rectTransform, Centered(LabelLeft + 72f, -20f, 144f, 32f));
            var label = NewText("Label", p.rectTransform, "NEW BEST", 18, new Color(.04f, .06f, .16f), TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            pill = p.rectTransform;
        }
    }

    RectTransform BuildCard(int index, string label, string sub, Color accent, Text value, out Text valueOut)
    {
        var rect = CardRects[index];
        var go = new GameObject("Card" + index, typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(panel, false);
        var card = (RectTransform)go.transform;
        Place(card, rect);
        cardGroups[index] = go.GetComponent<CanvasGroup>();

        var bg = NewImage("Background", card, Load("dp_card"), accent);
        bg.type = Image.Type.Sliced;
        Stretch(bg.rectTransform);

        // Label column: name on top, a quieter sub-label (or the NEW BEST
        // capsule) underneath, both starting just right of the accent bar.
        var title = NewText("Label", card, label, 26, accent, TextAnchor.MiddleLeft);
        Place(title.rectTransform, Centered(LabelLeft + 150f, 18f, 300f, 36f));
        AddOutline(title.gameObject, Ink, 1.5f);
        if (sub != null)
        {
            var subText = NewText("Sub", card, sub, 18, Muted, TextAnchor.MiddleLeft);
            Place(subText.rectTransform, Centered(LabelLeft + 150f, -20f, 300f, 28f));
        }

        // The scene's own Text becomes the value, so playerIsDead's serialized
        // references keep pointing at the number the player reads.
        value.transform.SetParent(card, false);
        value.gameObject.SetActive(true);
        value.transform.localScale = Vector3.one;
        foreach (var le in value.GetComponents<LayoutElement>()) le.ignoreLayout = true;
        value.font = font;
        value.fontSize = 56;
        value.fontStyle = FontStyle.Bold;
        value.alignment = TextAnchor.MiddleRight;
        value.resizeTextForBestFit = false;
        value.horizontalOverflow = HorizontalWrapMode.Overflow;
        value.verticalOverflow = VerticalWrapMode.Overflow;
        value.raycastTarget = false;
        value.color = Color.Lerp(Color.white, accent, .35f);
        Place(value.rectTransform, Centered(ValueRight - 130f, 0f, 260f, 80f));
        AddOutline(value.gameObject, Ink, 2f);
        valueOut = value;
        return card;
    }

    void BuildButtons(Button replay, Sprite replayGlyph, Button menu, Sprite menuGlyph)
    {
        buttonSlots[0] = BuildButton(0, replay, replayGlyph, "REPLAY", Cyan, ReplayRect);
        buttonSlots[1] = BuildButton(1, menu, menuGlyph, "MENU", Coral, MenuRect);
    }

    RectTransform BuildButton(int index, Button button, Sprite glyph, string label, Color accent, Rect rect)
    {
        var slotGo = new GameObject(label == "REPLAY" ? "ReplaySlot" : "MenuSlot", typeof(RectTransform), typeof(CanvasGroup));
        slotGo.transform.SetParent(panel, false);
        var slot = (RectTransform)slotGo.transform;
        Place(slot, rect);
        buttonGroups[index] = slotGo.GetComponent<CanvasGroup>();

        var glow = NewImage("Glow", slot, Load("dp_glow"), new Color(accent.r, accent.g, accent.b, 0f));
        glow.type = Image.Type.Sliced;
        Place(glow.rectTransform, Centered(0f, 0f, rect.width + 40f, rect.height + 40f));
        buttonGlows[index] = glow;

        if (button == null) return slot;

        // Same Button object, so the persistent onClick (buttonClicks.replay /
        // mainMenuButton) set up in the scene is untouched.
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
        }

        // Icon + label as one centred group.
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
        text.raycastTarget = false;
        text.gameObject.SetActive(true);
        AddOutline(text.gameObject, Ink, 1.5f);

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
        presses[index] = press;
        return slot;
    }

    Image[] BuildBurst(string name, Color tint)
    {
        var sprite = Load("dp_sparkle");
        var pool = new Image[SparklesPerBurst];
        for (int i = 0; i < pool.Length; i++)
        {
            var s = NewImage(name, panel, sprite, tint);
            Place(s.rectTransform, Centered(0f, 0f, 1f, 1f));
            s.gameObject.SetActive(false);
            pool[i] = s;
        }
        return pool;
    }

    // ---------------------------------------------------------------------
    // Animation
    // ---------------------------------------------------------------------

    void Update()
    {
        // Start the clock the first frame the panel is actually on screen.
        if (startedAt < 0f) startedAt = Time.unscaledTime;
        if (Screen.width != lastScreenW || Screen.height != lastScreenH) Fit();
        ApplyAt(Time.unscaledTime - startedAt);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!finalApplied) Skip();
    }

    // Jump straight to the settled end state.
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
        // Backdrop dim and panel pop with a slight overshoot.
        SetAlpha(scrim, .5f * EaseOutCubic(t / .3f));
        float p = Mathf.Clamp01(t / PanelIn);
        panelGroup.alpha = EaseOutCubic(t / .2f);
        panel.localScale = Vector3.one * (fitScale * Mathf.LerpUnclamped(.86f, 1f, EaseOutBack(p)));

        var d = divider.rectTransform.localScale;
        d.x = EaseOutCubic((t - .14f) / .32f);
        divider.rectTransform.localScale = d;

        for (int i = 0; i < 3; i++)
        {
            float c = EaseOutCubic((t - (CardStart + i * CardStagger)) / CardDuration);
            cardGroups[i].alpha = c;
            cards[i].anchoredPosition = CardRects[i].center + new Vector2((1f - c) * 48f, 0f);
        }

        int best = Mathf.RoundToInt(results.bestSpeed * EaseOutCubic(Progress(t, BestCountFrom, BestCountTo)));
        if (best != shownBest) { shownBest = best; bestValue.text = best.ToString(); }
        int run = Mathf.RoundToInt(results.runSpeed * EaseOutCubic(Progress(t, RunCountFrom, RunCountTo)));
        if (run != shownRun) { shownRun = run; runValue.text = run.ToString(); }
        float won = results.dustWon * EaseOutCubic(Progress(t, DustCountFrom, DustCountTo));
        int cents = Mathf.RoundToInt(won * 100f);
        if (cents != shownDustCents)
        {
            shownDustCents = cents;
            dustValue.text = "+" + (cents / 100f).ToString("F2");
            dustTotal.text = "TOTAL  " + (results.dustAtStart + cents / 100f).ToString("F2");
        }

        if (pill != null)
        {
            float q = Progress(t, NewBestAt, NewBestAt + .24f);
            pill.localScale = Vector3.one * (q <= 0f ? 0f : EaseOutBack(q));
        }

        AnimateBurst(bestBurst, results.newBest ? Progress(t, NewBestAt, NewBestAt + BurstDuration) : 1f,
                     bestValueCentre, 1.3f);
        AnimateBurst(dustBurst, results.dustWon > 0f ? Progress(t, DustBurstAt, DustBurstAt + BurstDuration) : 1f,
                     dustValueCentre, 1f);

        for (int i = 0; i < 2; i++)
        {
            float b = EaseOutCubic((t - (ButtonsStart + i * ButtonStagger)) / ButtonDuration);
            buttonGroups[i].alpha = b;
            buttonGroups[i].interactable = b > .5f;
            buttonSlots[i].anchoredPosition = (i == 0 ? ReplayRect : MenuRect).center + new Vector2(0f, (1f - b) * -24f);
        }
    }

    void AnimateIdle(float t)
    {
        float settle = Mathf.Clamp01((t - ButtonsStart) / .5f);
        for (int i = 0; i < 2; i++)
        {
            float breathe = .5f + .5f * Mathf.Sin(t * 2.2f + i * 1.6f);
            float pressed = presses[i] != null ? presses[i].Pressed01 : 0f;
            SetAlpha(buttonGlows[i], settle * (.16f + .14f * breathe) + .4f * pressed);
        }

        float twinkle = 1f + .1f * Mathf.Sin(t * 3.1f);
        if (headerSparkles[0] != null) headerSparkles[0].localScale = Vector3.one * twinkle;
        if (headerSparkles[1] != null) headerSparkles[1].localScale = Vector3.one * (2f - twinkle);

        if (bestGlow != null)
        {
            // A strong pulse on the new record, then a slow breathing glow.
            float pulse = Progress(t, NewBestAt, NewBestAt + .5f);
            float flash = pulse > 0f && pulse < 1f ? Mathf.Sin(pulse * Mathf.PI) * .5f : 0f;
            float idle = pulse >= 1f ? .1f + .06f * Mathf.Sin(t * 3f) : 0f;
            SetAlpha(bestGlow, Mathf.Max(flash, idle));
        }
    }

    void AnimateBurst(Image[] pool, float p, Vector2 origin, float reach)
    {
        bool live = p > 0f && p < 1f;
        for (int i = 0; i < pool.Length; i++)
        {
            var s = pool[i];
            if (s.gameObject.activeSelf != live) s.gameObject.SetActive(live);
            if (!live) continue;
            float angle = (i * 36f + 14f) * Mathf.Deg2Rad;
            float dist = (52f + (i % 3) * 22f) * reach;
            float e = EaseOutCubic(p);
            var rt = s.rectTransform;
            rt.anchoredPosition = origin + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * .7f) * (dist * e);
            float size = 18f + (i % 4) * 5f;
            rt.sizeDelta = new Vector2(size, size);
            rt.localScale = Vector3.one * Mathf.Sin(p * Mathf.PI);
            rt.localRotation = Quaternion.Euler(0f, 0f, p * 90f);
            var c = s.color; c.a = 1f - p * p; s.color = c;
        }
    }

    // ---------------------------------------------------------------------
    // Fitting to the screen: safe area, aspect ratio, the top-right actions
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
            ((RectTransform)go.transform).GetWorldCorners(corners);   // overlay canvas: screen pixels
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

    // Pure, so it can be tested for any screen: places the panel (including
    // its glow) inside `safe` with a small margin, never above full size, and
    // drops it below `blocker` (the top-right quick actions) if they would
    // otherwise overlap.
    public static void ComputeFit(Rect safe, Rect? blocker, out Vector2 centre, out float scale)
    {
        const float margin = 12f;
        Rect area = safe;
        for (int pass = 0; pass < 2; pass++)
        {
            float w = Width + 2f * GlowMargin, h = Height + 2f * GlowMargin;
            scale = Mathf.Min(1f, (area.width - 2f * margin) / w, (area.height - 2f * margin) / h);
            scale = Mathf.Max(scale, .1f);
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

    // The death buttons use the same glyphs as the top-right quick actions.
    // Those are cloned from the scene templates (replayWhenPausedButton /
    // mainMenuWhenPausedButton), so read the glyph from the same place: an
    // explicit "Icon" child on the live quick action first, then the template,
    // then whatever this button carried itself.
    static Sprite ResolveIcon(string quickAction, string template, Sprite fallback)
    {
        var live = SceneUtil.FindAny(quickAction);
        if (live != null)
            foreach (var img in live.GetComponentsInChildren<Image>(true))
                if (img.gameObject != live && img.sprite != null &&
                    img.name.IndexOf("Icon", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return img.sprite;
        var source = SceneUtil.FindAny(template);
        var image = source != null ? source.GetComponent<Image>() : null;
        if (image != null && image.sprite != null) return image.sprite;
        return fallback;
    }

    static Sprite OwnSprite(Button b)
    {
        var img = b != null ? b.GetComponent<Image>() : null;
        return img != null ? img.sprite : null;
    }

    static Sprite Load(string name)
    {
        return Resources.Load<Sprite>(SpriteRoot + name);
    }

    Image NewImage(string name, Transform parent, Sprite sprite, Color color)
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

    // Rect is in the parent's centre-origin space.
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

    static Rect Centered(float x, float y, float w, float h)
    {
        return new Rect(x - w * .5f, y - h * .5f, w, h);
    }

    static void SetAlpha(Graphic g, float a)
    {
        if (g == null) return;
        var c = g.color;
        if (Mathf.Approximately(c.a, a)) return;
        c.a = a;
        g.color = c;
    }

    static float Progress(float t, float from, float to) { return Mathf.Clamp01((t - from) / (to - from)); }
    static float EaseOutCubic(float x) { x = Mathf.Clamp01(x); float i = 1f - x; return 1f - i * i * i; }
    static float EaseOutBack(float x)
    {
        x = Mathf.Clamp01(x);
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float m = x - 1f;
        return 1f + c3 * m * m * m + c1 * m * m;
    }
}
