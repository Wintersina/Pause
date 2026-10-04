using System;
using UnityEngine;
using UnityEngine.UI;

// The choice after the final world's boss (WorldManager.OfferFinalChoice):
//
//   KEEP FLYING   stay in the final world -- endless, ever faster
//   LOOP BACK     a portal to the world the run started in; the score carries
//                 on and the next loop is a little harder
//   (countdown)   no pick in time: one more pass of the final world, its boss
//                 again, then LOOP BACK automatically (FinalPick.Encore)
//
// While it is up the world is frozen (BossEncounter.ScriptedFreeze asks
// IsUp), and the freeze is scripted, so neither a press on it nor the first
// press after it spends a pause (FreePress). Back / Escape picks nothing and
// never quits (a BackNavigator layer that swallows the press). After
// LoopRules.AutoPickSeconds of real time it picks the encore by itself, with
// the countdown -- and what it will do -- on screen.
//
// Self-building, in the app's cel UI: the death panel's frame, cards and
// title slab (Resources/DeathPanel), BONE type with INK outlines, CelPress on
// both buttons, stepped poses on 24 fps ticks. Unscaled time throughout.
public enum FinalPick { KeepFlying, LoopBack, Encore }

public class FinalChoicePanel : MonoBehaviour
{
    public const float Width = 640f, Height = 600f;
    public const float CardWidth = 560f, CardHeight = 150f;
    public const float LineWidth = 500f, LineHeight = 48f;
    // Panel-local centres.
    public const float TitleY = 245f, SubY = 186f, KeepY = 72f, LoopY = -96f, CountdownY = -232f;
    public const float CountdownWidth = 590f;

    static FinalChoicePanel instance;

    Action<FinalPick> onChoose;
    string worldName = "EMBER";
    bool open, freeAfterClose, releasedSinceClose;
    float clock, closeClock = -1f;
    float autoPick;
    int lastShownSecond = -1;
    float countPunchAt = -1f;

    RectTransform panel;
    Image scrim;
    Text title, sub, countdown, keepLabel, keepLine, loopLabel, loopLine;
    Button keepButton, loopButton;
    RectTransform keepCard, loopCard;
    Font font;

    public static FinalChoicePanel Instance { get { return instance; } }
    // moveBackGround (via BossEncounter.ScriptedFreeze) and movePlayer.
    public static bool IsUp { get { return instance != null && instance.open; } }
    // score (via BossEncounter.FreePress): presses on the panel, and the first
    // one after it, are not spent pauses.
    public static bool FreePress { get { return instance != null && (instance.open || instance.freeAfterClose); } }

    public RectTransform Panel { get { return panel; } }
    public Button KeepButton { get { return keepButton; } }
    public Button LoopButton { get { return loopButton; } }
    public Text Countdown { get { return countdown; } }
    public Text KeepLine { get { return keepLine; } }
    public Text LoopLine { get { return loopLine; } }
    public float SecondsLeft { get { return Mathf.Max(0f, autoPick); } }

    public static string KeepLineFor(string world)
    {
        return "Stay in " + world + ". Endless, ever faster.";
    }

    public static string LoopLineFor(string startWorld, int nextLoopNumber)
    {
        return "Portal to " + startWorld + " as LOOP " + nextLoopNumber + ". Score kept.";
    }

    // What the countdown will do when it runs out: "AUTO IN 10: ONE MORE
    // EMBER, THEN LOOP".
    public static string CountdownLabel(string world, float secondsLeft)
    {
        return "AUTO IN " + Mathf.CeilToInt(Mathf.Max(0f, secondsLeft)) + ": ONE MORE " +
               world.ToUpperInvariant() + ", THEN LOOP";
    }

    // `world` is the final world just cleared, `startWorld` where LOOP BACK
    // goes, `loop` the run's RunLoop.Index.
    public static FinalChoicePanel Show(int world, int startWorld, int loop, Action<FinalPick> onChoose)
    {
        if (instance != null) BossUtil.Kill(instance.gameObject);
        var worlds = WorldManager.Worlds;
        string here = worlds[Mathf.Clamp(world, 0, worlds.Length - 1)].displayName;
        string back = worlds[Mathf.Clamp(startWorld, 0, worlds.Length - 1)].displayName;

        var root = new GameObject("~FinalChoice", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 620;   // over the HUD and the world banner
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(800, 1200);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        var p = root.AddComponent<FinalChoicePanel>();
        instance = p;
        p.onChoose = onChoose;
        p.worldName = here.ToUpperInvariant();
        p.open = true;
        p.autoPick = LoopRules.AutoPickSeconds;
        p.font = OrbitronOrBuiltin();
        p.Build((RectTransform)root.transform, here, back, loop);
        p.Fit();
        p.ApplyIntro(0f);
        BackNavigator.Register(p, p.OnBack);
        return p;
    }

    // ---- building -------------------------------------------------------------

    void Build(RectTransform root, string here, string back, int loop)
    {
        scrim = NewImage("Scrim", root, null, AkiraPalette.WithAlpha(AkiraPalette.Night0, 0f));
        Stretch(scrim.rectTransform);
        scrim.raycastTarget = true;   // taps outside the panel reach nothing

        var go = new GameObject("Panel", typeof(RectTransform));
        go.transform.SetParent(root, false);
        panel = (RectTransform)go.transform;
        Place(panel, 0f, 0f, Width, Height);

        var frame = NewImage("Frame", panel, Load("dp_panel"), Color.white);
        frame.type = Image.Type.Sliced;
        Place(frame.rectTransform, 0f, 0f, Width + 40f, Height + 40f);

        var slab = NewImage("TitleSlab", panel, Load("dp_slab"), Color.white);
        if (slab.sprite == null) slab.color = AkiraPalette.Red;
        Place(slab.rectTransform, 0f, TitleY + 2f, 600f, 84f);
        title = NewText("Title", panel, here.ToUpperInvariant() + " CLEARED", 40, AkiraPalette.Bone, TextAnchor.MiddleCenter);
        title.fontStyle = FontStyle.BoldAndItalic;
        Place(title.rectTransform, 0f, TitleY, 580f, 60f);
        Ink(title.gameObject, 3f);

        string subText = loop > 0 ? "LOOP " + (loop + 1) + " DONE  -  PICK YOUR ROUTE" : "FINAL WORLD  -  PICK YOUR ROUTE";
        sub = NewText("Sub", panel, subText, 20, AkiraPalette.Muted, TextAnchor.MiddleCenter);
        Place(sub.rectTransform, 0f, SubY, 580f, 30f);

        keepCard = BuildOption("KeepFlying", KeepY, AkiraPalette.Sodium, "KEEP FLYING", KeepLineFor(here),
                               out keepButton, out keepLabel, out keepLine);
        keepButton.onClick.AddListener(() => Pick(false));
        loopCard = BuildOption("LoopBack", LoopY, AkiraPalette.Teal, "LOOP BACK", LoopLineFor(back, loop + 2),
                               out loopButton, out loopLabel, out loopLine);
        loopButton.onClick.AddListener(() => Pick(true));

        countdown = NewText("Countdown", panel, LoopRules.AutoPickSeconds > 0f ? CountdownLabel(here, autoPick) : "",
                            22, AkiraPalette.Muted, TextAnchor.MiddleCenter);
        Place(countdown.rectTransform, 0f, CountdownY, CountdownWidth, 36f);
        // The widest count ("AUTO IN 10: ...") sets one size for the whole
        // countdown, so the line never jumps as the seconds tick.
        while (countdown.fontSize > 14 && countdown.preferredWidth > CountdownWidth) countdown.fontSize--;
        Ink(countdown.gameObject, 1.5f);
    }

    RectTransform BuildOption(string name, float y, Color accent, string label, string line,
                              out Button button, out Text labelText, out Text lineText)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(panel, false);
        var rt = (RectTransform)go.transform;
        Place(rt, 0f, y, CardWidth, CardHeight);

        var bg = go.GetComponent<Image>();
        bg.sprite = Load("dp_card");
        bg.type = Image.Type.Sliced;
        bg.color = bg.sprite != null ? Color.white : AkiraPalette.Card;
        bg.raycastTarget = true;

        var bar = NewImage("Accent", rt, Load("dp_bar"), accent);
        Place(bar.rectTransform, -CardWidth * .5f + 22f, 0f, 16f, CardHeight - 30f);
        var edge = NewImage("Edge", rt, Load("dp_button"), AkiraPalette.WithAlpha(accent, .9f));
        edge.type = Image.Type.Sliced;
        Place(edge.rectTransform, 0f, 0f, CardWidth, CardHeight);
        edge.raycastTarget = false;
        // The frame sits under the type: move it behind the bar.
        edge.transform.SetSiblingIndex(0);

        labelText = NewText("Label", rt, label, 36, AkiraPalette.Bone, TextAnchor.MiddleLeft);
        labelText.fontStyle = FontStyle.BoldAndItalic;
        Place(labelText.rectTransform, 14f, 30f, LineWidth, 48f);
        Ink(labelText.gameObject, 2.5f);
        var cel = labelText.gameObject.AddComponent<Shadow>();
        cel.effectColor = AkiraPalette.WithAlpha(accent, 1f);
        cel.effectDistance = new Vector2(4f, -5f);

        lineText = NewText("Line", rt, line, 19, AkiraPalette.Bone, TextAnchor.UpperLeft);
        lineText.horizontalOverflow = HorizontalWrapMode.Wrap;
        Place(lineText.rectTransform, 14f, -28f, LineWidth, LineHeight);
        Ink(lineText.gameObject, 1.5f);

        button = go.GetComponent<Button>();
        button.transition = Selectable.Transition.None;   // CelPress is the feedback
        button.targetGraphic = bg;
        CelPress.AddTo(go, rt);
        return rt;
    }

    // ---- the choice -------------------------------------------------------------

    // A button: KEEP FLYING (false) or LOOP BACK (true).
    public void Pick(bool loopBack)
    {
        Pick(loopBack ? FinalPick.LoopBack : FinalPick.KeepFlying);
    }

    // A button, or the countdown running out (FinalPick.Encore).
    public void Pick(FinalPick pick)
    {
        if (!open) return;
        var cb = onChoose;
        onChoose = null;
        Close();
        if (cb != null) cb(pick);
    }

    // Back / Escape: picks nothing, and never quits the run.
    bool OnBack()
    {
        return open;
    }

    // Releases the freeze at once; the panel snaps away over a few ticks and
    // stays (hidden) until the first press after it has been seen as free.
    public void Close()
    {
        if (!open) return;
        open = false;
        freeAfterClose = true;
        releasedSinceClose = !TouchInput.IsPressed;
        closeClock = 0f;
        BackNavigator.Unregister(this);
        if (keepButton != null) keepButton.interactable = false;
        if (loopButton != null) loopButton.interactable = false;
        if (scrim != null) scrim.raycastTarget = false;
    }

    void OnDestroy()
    {
        BackNavigator.Unregister(this);
        if (instance == this) instance = null;
    }

    // ---- per frame ----------------------------------------------------------------

    void Update()
    {
        Step(Mathf.Min(Time.unscaledDeltaTime, .1f));
    }

    // One frame of real time (tests drive it directly).
    public void Step(float realDt)
    {
        if (open)
        {
            clock += realDt;
            ApplyIntro(clock);
            if (LoopRules.AutoPickSeconds > 0f)
            {
                autoPick -= realDt;
                int second = Mathf.CeilToInt(Mathf.Max(0f, autoPick));
                if (second != lastShownSecond)
                {
                    lastShownSecond = second;
                    countdown.text = CountdownLabel(worldName, autoPick);
                    if (second <= 3) countPunchAt = clock;
                }
                countdown.color = autoPick <= 3f ? AkiraPalette.Amber : AkiraPalette.Muted;
                PunchScale(countdown.rectTransform, clock - countPunchAt, 1.25f);
                if (autoPick <= 0f) { Pick(FinalPick.Encore); return; }
            }
            return;
        }

        if (closeClock >= 0f)
        {
            closeClock += realDt;
            ApplyOutro(closeClock);
        }
    }

    // After closing: the first press that starts once the finger has been up
    // is the free one; score has seen it by now, so the panel can go.
    void LateUpdate()
    {
        if (open || !freeAfterClose) return;
        if (!TouchInput.IsPressed) { releasedSinceClose = true; return; }
        if (!releasedSinceClose) return;
        freeAfterClose = false;
        BossUtil.Kill(gameObject);
    }

    // ---- motion (stepped, 24 fps ticks) ----------------------------------------------

    // (panel scale, card slide x, title scale) per pose; .w = hold ticks.
    static readonly Vector4[] IntroPoses =
    {
        new Vector4(.4f, 420f, 0f, 1),
        new Vector4(1.1f, 120f, 0f, 1),    // overshoot
        new Vector4(.96f, -18f, 1.3f, 2),  // the title pops
        new Vector4(1f, 0f, .95f, 2),
        new Vector4(1f, 0f, 1f, 0),
    };

    public void ApplyIntro(float t)
    {
        int tick = Mathf.FloorToInt(t * 24f);
        int i = 0;
        for (int sum = 0; i < IntroPoses.Length - 1; i++)
        {
            sum += (int)IntroPoses[i].w;
            if (tick < sum) break;
        }
        var p = IntroPoses[i];
        float s = p.x * fitScale;
        panel.localScale = new Vector3(s, s, 1f);
        title.rectTransform.localScale = new Vector3(p.z, p.z, 1f);
        // The cards slide in from opposite sides, one tick apart.
        keepCard.anchoredPosition = new Vector2(p.y, KeepY);
        var q = IntroPoses[Mathf.Max(0, i - 1)];
        loopCard.anchoredPosition = new Vector2(-q.y, LoopY);
        float a = Mathf.Min(.62f, .2f * (tick + 1));
        if (!Mathf.Approximately(scrim.color.a, a)) scrim.color = AkiraPalette.WithAlpha(AkiraPalette.Night0, a);
    }

    void ApplyOutro(float t)
    {
        int tick = Mathf.FloorToInt(t * 24f);
        float s = (tick == 0 ? 1.06f : tick == 1 ? .7f : tick == 2 ? .3f : 0f) * fitScale;
        panel.localScale = new Vector3(s, s, 1f);
        float a = Mathf.Max(0f, .62f - .2f * (tick + 1));
        scrim.color = AkiraPalette.WithAlpha(AkiraPalette.Night0, a);
        if (tick >= 3) panel.gameObject.SetActive(false);
    }

    static void PunchScale(RectTransform rt, float since, float big)
    {
        float k = since < 0f ? 99f : since * 24f;
        float s = k < 1f ? big : k < 3f ? .95f : 1f;
        if (!Mathf.Approximately(rt.localScale.x, s)) rt.localScale = new Vector3(s, s, 1f);
    }

    // ---- fitting ------------------------------------------------------------------------

    float fitScale = 1f;

    // Shrinks (never grows) the panel to fit the canvas's safe area.
    void Fit()
    {
        var root = (RectTransform)transform;
        Vector2 size = root.rect.size;
        if (size.x <= 0f || size.y <= 0f) size = new Vector2(800f, 1200f);
        Rect safe = Screen.safeArea;
        float sx = Screen.width > 0 ? safe.width / Screen.width : 1f;
        float sy = Screen.height > 0 ? safe.height / Screen.height : 1f;
        fitScale = FitScale(new Vector2(size.x * sx, size.y * sy));
    }

    // The panel's scale on a canvas area of `available` units (glow included).
    public static float FitScale(Vector2 available)
    {
        float w = (Width + 40f) + 32f, h = (Height + 40f) + 32f;
        return Mathf.Min(1f, available.x / w, available.y / h);
    }

    // ---- helpers -------------------------------------------------------------------------

    static Sprite Load(string name) { return Resources.Load<Sprite>("DeathPanel/" + name); }

    static Font OrbitronOrBuiltin()
    {
        foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
            if (f != null && f.name.StartsWith("Orbitron")) return f;
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

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

    Text NewText(string name, Transform parent, string text, int size, Color color, TextAnchor align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = font;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = FontStyle.Bold;
        t.color = color;
        t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static void Ink(GameObject go, float d)
    {
        var o = go.AddComponent<Outline>();
        o.effectColor = AkiraPalette.WithAlpha(AkiraPalette.Ink, .95f);
        o.effectDistance = new Vector2(d, -d);
    }

    static void Place(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
