using UnityEngine;
using UnityEngine.UI;

// The run score on the in-game read-out, plus the kill-chain badge and the
// "+N" popups. Added by HudStyler in gameS1 only -- the tutorial scores
// nothing, so its read-out stays SPEED / PAUSES. Neither shows star dust
// during a run: the dust a run earned is on the Flight Complete card.
//
//   SCORE  12,345  x3     top row of the read-out (BONE, like the other rows'
//                         flat cel type with an INK outline). The number ticks
//                         up towards RunScore.Total instead of jumping, and
//                         punches on a big gain. The chain badge shows the
//                         current kill multiplier while a chain is alive and
//                         fades as its window runs out.
//                         The max-speed streak's cue (ScoreX2Cue) sits on this
//                         row too: a slim bar along its bottom, then an "x2"
//                         plate hugging the figure's right edge.
//   SPEED  46   SPD x1.5  the speed multiplier (ScoreRules tiers) as a badge on
//                         the SPEED row, coloured by tier; it punches, flashes
//                         and calls out "SPD x2" under the read-out as it
//                         steps up, and hides at x1
//   PAUSES  5    LOOP 2   the run's loop (RunLoop) on the PAUSES row, only
//                         once the run has looped back past the final world.
//                         (SCORE and SPEED are too full for it: SCORE
//                         9,999,999 + x4, SPEED 60 + SPD x2.5 leave no room
//                         for a second badge in the 323-unit row.)
//   +5                    pops up and rises from where a kill, a star dust or
//                         atom pickup, a boss or a world clear happened
//                         (RunScore.Scored), coloured by what it was; bosses
//                         and worlds are bigger and stay up longer. Pooled,
//                         and on the world's clock, so they freeze with it.
//
// The row is one more slot in the read-out's VerticalLayoutGroup (PanelTexts):
// the stack and the panel both grow by one row + gap, so every row keeps its
// height and the panel keeps its padding. The read-out itself animates on
// unscaled time -- the HUD lives on through the freeze.
public class ScoreHud : MonoBehaviour
{
    public const string RowName = "ScoreText";
    public const string ChainName = "Chain";
    // One row (33) and the layout group's gap (7) in gameS1's read-out.
    public const float RowStep = 40f;
    public const int FontSize = 22;
    public const int PopupPool = 12;

    static readonly Color ScoreColour = AkiraPalette.Bone;
    static readonly Color ChainColour = AkiraPalette.RedHi;
    static readonly Color TextInk = AkiraPalette.WithAlpha(AkiraPalette.Ink, .95f);

    Text scoreText, chainText;
    // Right-hand badges on the rows under SCORE: the speed multiplier on the
    // SPEED row, the loop on the PAUSES row (once looping has started).
    Text speedBadge, loopBadge;
    float lastSpeedMultiplier = 1f;
    float speedPunchAt = -1f;
    int lastLoop;
    float loopPunchAt = -1f;
    RectTransform canvasRect;
    Canvas canvas;
    Font font;
    double shown;
    long lastTarget;
    long shownWhole = -1;
    float landedAt = -1f;
    float punchAt = -1f;
    int lastMultiplier = 1;
    float chainPunchAt = -1f;

    struct Popup { public Text text; public Outline glow; public float age, seconds, rise; public Vector2 from; }
    Popup[] popups;
    int nextPopup;

    public Text ScoreText { get { return scoreText; } }
    public Text ChainText { get { return chainText; } }
    public Text SpeedBadge { get { return speedBadge; } }
    public Text LoopBadge { get { return loopBadge; } }

    public const string SpeedBadgeName = "SpeedMultiplier";
    public const string LoopBadgeName = "Loop";
    // The row the LOOP badge sits on (gameS1's pause counter).
    public const string LoopRowName = "PauseCounter";
    public const float BadgeWidth = 132f;
    public const int BadgeFontSize = 22;

    // The read-out scores only outside the tutorial.
    public static bool ShouldShow(Text speedText)
    {
        return speedText != null && speedText.gameObject.scene.name != score.TutorialScene;
    }

    // The tutorial's popups: the same pooled "+N" / word popups as a run (same
    // look, motion and clock), with no SCORE row -- the tutorial scores nothing
    // (RunScore.PracticePopups only shows what each pickup is worth). Idempotent.
    public static ScoreHud AttachPopupsOnly(GameObject host, Text speedText)
    {
        if (speedText == null) return null;
        var hud = host.GetComponent<ScoreHud>() ?? host.AddComponent<ScoreHud>();
        hud.font = speedText.font;
        hud.popupsOnly = true;
        var c = speedText.GetComponentInParent<Canvas>();
        hud.canvas = c != null ? c.rootCanvas : null;
        hud.canvasRect = hud.canvas != null ? (RectTransform)hud.canvas.transform : null;
        return hud;
    }

    bool popupsOnly;
    public bool PopupsOnly { get { return popupsOnly; } }

    // Adds the SCORE row to the top of the read-out that holds `speedText`
    // and grows the stack and its panel by one row. Idempotent.
    public static ScoreHud Attach(GameObject host, Text speedText)
    {
        var rows = speedText.transform.parent as RectTransform;
        if (rows == null) return null;
        var hud = host.GetComponent<ScoreHud>() ?? host.AddComponent<ScoreHud>();
        hud.font = speedText.font;

        var existing = rows.Find(RowName);
        if (existing != null)
        {
            hud.scoreText = existing.GetComponent<Text>();
            hud.chainText = existing.Find(ChainName)?.GetComponent<Text>();
        }
        else
        {
            var go = new GameObject(RowName, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(rows, false);
            go.transform.SetSiblingIndex(0);
            hud.scoreText = go.GetComponent<Text>();
            Style(hud.scoreText, hud.font, FontSize, ScoreColour, TextAnchor.MiddleLeft);
            hud.scoreText.text = Label(0);

            var chain = new GameObject(ChainName, typeof(RectTransform), typeof(Text));
            chain.transform.SetParent(go.transform, false);
            var crt = (RectTransform)chain.transform;
            crt.anchorMin = new Vector2(1f, 0f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, .5f);
            crt.sizeDelta = new Vector2(64f, 0f);
            crt.anchoredPosition = Vector2.zero;
            hud.chainText = chain.GetComponent<Text>();
            Style(hud.chainText, hud.font, 22, ChainColour, TextAnchor.MiddleRight);
            hud.chainText.text = "";

            Grow(rows, RowStep);
            Grow(rows.parent as RectTransform, RowStep);
        }

        // The badges sit right-aligned on the SPEED and PAUSES rows, a
        // column under the chain badge, so neither the panel nor any row grows.
        hud.speedBadge = Badge(speedText.transform, SpeedBadgeName, hud.font);
        var loopRow = rows.Find(LoopRowName);
        hud.loopBadge = Badge(loopRow != null ? loopRow : speedText.transform, LoopBadgeName, hud.font);
        ScoreX2Cue.Attach(hud.scoreText);   // the max-speed streak's bar and x2 plate, on the SCORE row
        hud.lastSpeedMultiplier = 1f;
        hud.lastLoop = 0;

        var c = rows.GetComponentInParent<Canvas>();
        hud.canvas = c != null ? c.rootCanvas : null;
        hud.canvasRect = hud.canvas != null ? (RectTransform)hud.canvas.transform : null;
        hud.shown = RunScore.Total;
        hud.lastTarget = RunScore.Total;
        return hud;
    }

    // A right-anchored badge Text on `row` (idempotent).
    static Text Badge(Transform row, string name, Font font)
    {
        var existing = row.Find(name);
        if (existing != null) return existing.GetComponent<Text>();
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(row, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, .5f);
        rt.sizeDelta = new Vector2(BadgeWidth, 0f);
        rt.anchoredPosition = Vector2.zero;
        var t = go.GetComponent<Text>();
        Style(t, font, BadgeFontSize, AkiraPalette.Cyan, TextAnchor.MiddleRight);
        t.text = "";
        return t;
    }

    public static string SpeedBadgeLabel(float multiplier)
    {
        return multiplier > 1f ? "SPD " + ScoreRules.MultiplierLabel(multiplier) : "";
    }

    public const string LimitBreakWord = "LIMIT BREAK";

    // The callout as a tier is reached: "SPD x2", or "LIMIT BREAK x2.5".
    public static string SpeedCalloutLabel(float multiplier, bool limitBreak)
    {
        return limitBreak ? LimitBreakWord + " " + ScoreRules.MultiplierLabel(multiplier) : SpeedBadgeLabel(multiplier);
    }

    public static string LoopBadgeLabel(int loopIndex)
    {
        return loopIndex > 0 ? "LOOP " + (loopIndex + 1) : "";
    }

    // Tier colours, slow to fast: TEAL, CYAN, AMBER, Kaneda red.
    public static Color SpeedBadgeColour(float multiplier)
    {
        if (multiplier >= 2.5f) return AkiraPalette.RedHi;
        if (multiplier >= 2f) return AkiraPalette.Amber;
        if (multiplier >= 1.5f) return AkiraPalette.Cyan;
        return AkiraPalette.Teal;
    }

    static void Grow(RectTransform rt, float by)
    {
        if (rt == null) return;
        var size = rt.sizeDelta;
        size.y += by;
        rt.sizeDelta = size;
    }

    public const string LabelPrefix = "SCORE  ";
    // One string per rebuilt figure (the roll changes it most frames): the
    // digits go straight into a kept buffer, no N0 / culture string first.
    static readonly char[] labelBuf = new char[LabelPrefix.Length + 28];
    public static string Label(long points)
    {
        LabelPrefix.CopyTo(0, labelBuf, 0, LabelPrefix.Length);
        int n = RunScore.FormatInto(labelBuf, LabelPrefix.Length, points);
        return new string(labelBuf, 0, n);
    }

    static void Style(Text t, Font font, int size, Color colour, TextAnchor align)
    {
        if (font != null) t.font = font;
        t.fontSize = size;
        t.fontStyle = FontStyle.Bold;
        t.color = colour;
        t.alignment = align;
        t.resizeTextForBestFit = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        var outline = t.GetComponent<Outline>() ?? t.gameObject.AddComponent<Outline>();
        outline.effectColor = TextInk;
        outline.effectDistance = new Vector2(2f, -2f);
    }

    // The live run HUD (gameS1 only), for one-word callouts from gameplay.
    public static ScoreHud Current { get; private set; }

    void OnEnable()
    {
        RunScore.Scored += OnScored;
        DeathCrash.DominoKill += OnDominoKill;
        DeathCrash.MegaDominoStarted += OnMegaDomino;
        DeathCrash.DeathCombo += OnDeathCombo;
        Current = this;
    }

    void OnDisable()
    {
        RunScore.Scored -= OnScored;
        DeathCrash.DominoKill -= OnDominoKill;
        DeathCrash.MegaDominoStarted -= OnMegaDomino;
        DeathCrash.DeathCombo -= OnDeathCombo;
        if (Current == this) Current = null;
    }

    void Update()
    {
        if (popupsOnly)
        {
            StepPopups(!buttonClicks.playerDied ? TargetExplosion.Delta() : -1f);
            return;
        }
        TickDisplay(Mathf.Min(Time.unscaledDeltaTime, .1f));
    }

    void TickDisplay(float dt)
    {
        if (scoreText == null) return;
        float now = Time.unscaledTime;
        long target = RunScore.Total;

        // Tick up: a quick roll that eases into the exact number (never
        // overshoots). While it rolls the figure glows AMBER; a real gain
        // punches it, and it flashes BONE-bright as it lands.
        if (target < shown) shown = target;   // a new run
        if (target - lastTarget >= ScoreRules.PopupMinPoints) punchAt = now;
        lastTarget = target;
        double gap = target - shown;
        if (gap > 0d)
        {
            double step = gap * (1d - System.Math.Exp(-9d * dt)) + 12d * dt;
            shown = System.Math.Min(target, shown + step);
            if (shown >= target) landedAt = now;
        }
        long whole = (long)System.Math.Floor(shown);
        if (whole != shownWhole)   // only rebuild the string when the figure changes
        {
            shownWhole = whole;
            scoreText.text = Label(whole);
        }
        bool rolling = shown < target;
        float sinceLand = now - landedAt;
        scoreText.color = rolling ? AkiraPalette.Amber
                        : sinceLand >= 0f && sinceLand < 2f / 24f ? Color.white : ScoreColour;
        Punch(scoreText.rectTransform, now - punchAt);

        if (chainText != null)
        {
            int m = RunScore.Multiplier;
            if (m != lastMultiplier)
            {
                if (m > 1) chainPunchAt = now;
                chainText.text = m > 1 ? ChainLabels[Mathf.Min(m, ChainLabels.Length - 1)] : "";
                lastMultiplier = m;
            }
            if (m > 1)
            {
                // Fades out over the last of the chain window, stepped like the
                // rest of the HUD (no smooth fade).
                float left = RunScore.ChainLeft01;
                chainText.color = AkiraPalette.WithAlpha(ChainColour, left > .35f ? 1f : left > .15f ? .6f : .3f);
                Punch(chainText.rectTransform, now - chainPunchAt, 1.4f);
            }
        }

        TickBadges(now);

        // Popups live on the explosions' clock (TargetExplosion.Delta): they
        // freeze with the world, and through the ultimate's deep slow motion
        // (x0.06) they still pop and rise at half speed beside the blasts
        // that scored them instead of hanging there for ten seconds.
        // On death they clear -- except through the death crash, whose
        // DOMINO popups and banners run on its clock until the panel shows.
        StepPopups(!buttonClicks.playerDied ? TargetExplosion.Delta()
                   : DeathCrash.Running ? DeathCrash.FrameDt : -1f);
    }

    // ---- the death crash's domino (DeathCrash) ----------------------------
    //
    //   DOMINO x3  +45      over each chain kill (amber, Kaneda red from x3)
    //   MEGA DOMINO!        mid-screen as the death takes the whole screen
    //   DEATH COMBO +N      mid-screen as the chain ends, before the panel

    public const string MegaDominoLabel = "MEGA DOMINO!";

    public static string DominoLabel(int multiplier, int points)
    {
        return (multiplier <= 1 ? "DOMINO" : "DOMINO x" + multiplier) + "  +" + RunScore.Format(points);
    }

    public static string DeathComboLabel(int points) { return "DEATH COMBO +" + RunScore.Format(points); }

    void OnDominoKill(Vector3 at, int multiplier, int points)
    {
        if (!RunScore.Scoring) return;
        ShowWord(DominoLabel(multiplier, points), at, multiplier >= 3 ? AkiraPalette.RedHi : AkiraPalette.Amber,
                 Mathf.Min(38, 26 + multiplier * 2));
    }

    void OnMegaDomino(Vector3 at)
    {
        if (!RunScore.Scoring) return;
        ShowBanner(MegaDominoLabel, AkiraPalette.Magenta, 58, 1.8f, .2f);
    }

    void OnDeathCombo(int points, int kills, bool mega)
    {
        if (!RunScore.Scoring || points <= 0) return;
        ShowBanner(DeathComboLabel(points), mega ? AkiraPalette.Magenta : AkiraPalette.Amber, mega ? 54 : 48, 1.4f, .05f);
    }

    // A big line across the middle of the screen (`y`: a fraction of the
    // canvas height above centre), in the popups' pool and motion.
    public Text ShowBanner(string text, Color colour, int size, float seconds, float y)
    {
        if (canvasRect == null || string.IsNullOrEmpty(text)) return null;
        if (popups == null) BuildPopups();
        int index = nextPopup;
        nextPopup = (nextPopup + 1) % popups.Length;
        var p = popups[index];
        if (p.glow != null && p.glow.enabled) p.glow.enabled = false;
        p.text.text = text;
        p.text.fontSize = size;
        p.text.color = colour;
        p.seconds = seconds;
        p.rise = 24f;
        p.from = KeepInSafeArea(p.text, new Vector2(0f, canvasRect.rect.size.y * y), p.rise);
        p.age = 0f;
        p.text.gameObject.SetActive(true);
        p.text.transform.SetAsLastSibling();
        p.text.rectTransform.anchoredPosition = p.from;
        p.text.rectTransform.localScale = Vector3.zero;
        popups[index] = p;
        return p.text;
    }

    // SPD xN: shown above x1, punches and flashes BONE-white as it steps up
    // (with a callout under the read-out); LOOP N once looping has begun.
    void TickBadges(float now)
    {
        if (speedBadge != null)
        {
            float m = RunScore.SpeedMultiplier;
            if (!Mathf.Approximately(m, lastSpeedMultiplier))
            {
                if (m > lastSpeedMultiplier && m > 1f)
                {
                    speedPunchAt = now;
                    // past the cap on the boost shield: the callout says so
                    ShowCallout(SpeedCalloutLabel(m, ScoreRules.IsLimitBreak(moveBackGround.speed)),
                                SpeedBadgeColour(m), speedBadge.rectTransform);
                }
                speedBadge.text = SpeedBadgeLabel(m);
                lastSpeedMultiplier = m;
            }
            if (m > 1f)
            {
                float since = now - speedPunchAt;
                speedBadge.color = since >= 0f && since < 2f / 24f ? Color.white : SpeedBadgeColour(m);
                Punch(speedBadge.rectTransform, since, 1.4f);
            }
        }

        if (loopBadge != null)
        {
            int loop = RunLoop.Index;
            if (loop != lastLoop)
            {
                if (loop > lastLoop) loopPunchAt = now;
                loopBadge.text = LoopBadgeLabel(loop);
                lastLoop = loop;
            }
            if (loop > 0)
            {
                float since = now - loopPunchAt;
                loopBadge.color = since >= 0f && since < 2f / 24f ? Color.white : AkiraPalette.Magenta;
                Punch(loopBadge.rectTransform, since, 1.5f);
            }
        }
    }

    // A word popup ("SPD x2") that pops just under the read-out, beside the
    // badge that changed. Same pool and motion as the "+N" popups.
    public Text ShowCallout(string text, Color colour, RectTransform near)
    {
        if (canvasRect == null || string.IsNullOrEmpty(text)) return null;
        if (popups == null) BuildPopups();
        int index = nextPopup;
        nextPopup = (nextPopup + 1) % popups.Length;
        var p = popups[index];
        if (p.glow != null && p.glow.enabled) p.glow.enabled = false;
        p.text.text = text;
        p.text.fontSize = 30;
        p.text.color = colour;
        p.seconds = 1.1f;
        p.rise = 30f;
        Vector2 at = near != null ? (Vector2)canvasRect.InverseTransformPoint(near.position) : Vector2.zero;
        Vector2 size = canvasRect.rect.size;
        // Below the read-out (its bottom is ~60 units under the SPEED row),
        // kept on screen.
        at.y -= 110f;
        at.x = Mathf.Clamp(at.x - 40f, -size.x * .5f + 120f, size.x * .5f - 120f);
        p.from = KeepInSafeArea(p.text, at, p.rise);
        p.age = 0f;
        p.text.gameObject.SetActive(true);
        p.text.transform.SetAsLastSibling();
        p.text.rectTransform.anchoredPosition = p.from;
        p.text.rectTransform.localScale = Vector3.zero;
        popups[index] = p;
        return p.text;
    }

    // A one-word popup over a world position ("FREE SHOT" on the ship), in
    // the "+N" popups' pool, motion and clock.
    public Text ShowWord(string text, Vector3 world, Color colour, int size = 26)
    {
        if (canvasRect == null || string.IsNullOrEmpty(text)) return null;
        if (popups == null) BuildPopups();
        int index = nextPopup;
        nextPopup = (nextPopup + 1) % popups.Length;
        var p = popups[index];
        if (p.glow != null && p.glow.enabled) p.glow.enabled = false;
        p.text.text = text;
        p.text.fontSize = size;
        p.text.color = colour;
        p.seconds = .8f;
        p.rise = 50f;
        p.from = KeepInSafeArea(p.text, ToCanvas(world), p.rise);
        p.age = 0f;
        p.text.gameObject.SetActive(true);
        p.text.transform.SetAsLastSibling();
        p.text.rectTransform.anchoredPosition = p.from;
        p.text.rectTransform.localScale = Vector3.zero;
        popups[index] = p;
        return p.text;
    }

    // A big callout over a world position (the magnet + shield combo:
    // MagnetCombo), in the popups' pool and motion but with its own size,
    // hold and rise. One slot: a callout still on screen is replaced in
    // place (restarted, its pop played again), never stacked.
    int calloutSlot = -1;
    string calloutText;
    public Text ShowCallout(string text, Vector3 world, Color colour, int size, float seconds, float rise)
    {
        if (canvasRect == null || string.IsNullOrEmpty(text)) return null;
        if (popups == null) BuildPopups();
        int index;
        if (CalloutShowing) index = calloutSlot;
        else
        {
            index = nextPopup;
            nextPopup = (nextPopup + 1) % popups.Length;
        }
        calloutSlot = index;
        calloutText = text;
        var p = popups[index];
        if (p.glow != null && p.glow.enabled) p.glow.enabled = false;
        p.text.text = text;
        p.text.fontSize = size;
        p.text.color = colour;
        p.seconds = seconds;
        p.rise = rise;
        p.from = KeepInSafeArea(p.text, ToCanvas(world), p.rise);
        p.age = 0f;
        p.text.gameObject.SetActive(true);
        p.text.transform.SetAsLastSibling();
        p.text.rectTransform.anchoredPosition = p.from;
        p.text.rectTransform.localScale = Vector3.zero;
        popups[index] = p;
        return p.text;
    }

    // A fusion's callout (AtomFusions): ShowCallout plus the fusion's own
    // glow colour round the letters. Same single slot.
    public const float GlowDistance = 4f;
    public Text ShowCallout(string text, Vector3 world, Color colour, int size, float seconds, float rise, Color glow)
    {
        var t = ShowCallout(text, world, colour, size, seconds, rise);
        if (t == null || calloutSlot < 0) return t;
        var g = popups[calloutSlot].glow;
        if (g != null) { g.effectColor = glow; g.enabled = glow.a > 0f; }
        return t;
    }

    // The live callout's glow (tests).
    public Outline CalloutGlow => calloutSlot >= 0 && popups != null && calloutSlot < popups.Length ? popups[calloutSlot].glow : null;

    // Is the combo callout on screen (tests)?
    // (the slot still holding the callout: a recycled "+N" there is not replaced)
    public bool CalloutShowing => calloutSlot >= 0 && popups != null && calloutSlot < popups.Length && popups[calloutSlot].age >= 0f &&
                                  ReferenceEquals(popups[calloutSlot].text.text, calloutText);

    static readonly string[] ChainLabels = { "", "", "x2", "x3", "x4" };

    // Stepped punch: 1 tick big, 2 ticks small, then rest (24 fps ticks).
    static void Punch(RectTransform rt, float since, float big = 1.16f)
    {
        float k = since < 0f ? 99f : since * 24f;
        float s = k < 1f ? big : k < 3f ? .95f : 1f;
        if (!Mathf.Approximately(rt.localScale.x, s)) rt.localScale = new Vector3(s, s, 1f);
    }

    // ---- "+N" popups -------------------------------------------------------
    //
    // A fixed pool (PopupPool Texts, built once); the oldest is recycled when
    // all are busy. Each pops in big (stepped 0 -> 1.6 -> 0.9 -> 1), rises,
    // holds and fades in steps. Colour says what it was; bosses and world
    // clears are bigger, carry a word and stay up longer. Everything runs on
    // the world's scaled clock, so a pause freezes them mid-flight.

    public struct PopupStyle
    {
        public Color colour;
        public int size;
        public float seconds, rise;
        public string suffix;
    }

    public static PopupStyle StyleFor(RunScore.Source source, bool chained)
    {
        switch (source)
        {
            case RunScore.Source.Boss:
                return new PopupStyle { colour = AkiraPalette.RedHi, size = 46, seconds = 1.6f, rise = 70f, suffix = "  BOSS" };
            case RunScore.Source.World:
                return new PopupStyle { colour = AkiraPalette.Cyan, size = 44, seconds = 1.6f, rise = 70f, suffix = "  WORLD" };
            case RunScore.Source.Elite:
                return new PopupStyle { colour = AkiraPalette.Magenta, size = 40, seconds = 1.4f, rise = 64f, suffix = "  ELITE DOWN" };
            case RunScore.Source.Escape:   // an elite got away: the points it cost (negative), in the warning amber
                return new PopupStyle { colour = AkiraPalette.Amber, size = 34, seconds = 1.4f, rise = 64f, suffix = "  ESCAPED" };
            case RunScore.Source.BossEscape:   // a boss got away: the points it cost (negative), in the warning amber
                return new PopupStyle { colour = AkiraPalette.Amber, size = 44, seconds = 1.6f, rise = 70f, suffix = "  BOSS ESCAPE" };
            case RunScore.Source.Shield:   // a hostile shot absorbed by the shield
                return new PopupStyle { colour = AkiraPalette.Cyan, size = 28, seconds = .8f, rise = 56f, suffix = "  ABSORB" };
            case RunScore.Source.Dust:
                return new PopupStyle { colour = AkiraPalette.Amber, size = 22, seconds = .6f, rise = 44f, suffix = "" };
            case RunScore.Source.Atom:
                return new PopupStyle { colour = AkiraPalette.Teal, size = 26, seconds = .75f, rise = 52f, suffix = "" };
            default:   // kills: BONE, Kaneda red once a chain is multiplying them
                return new PopupStyle { colour = chained ? AkiraPalette.RedHi : AkiraPalette.Bone,
                                        size = chained ? 30 : 26, seconds = .75f, rise = 56f, suffix = "" };
        }
    }

    void OnScored(int points, Vector3 at, RunScore.Source source)
    {
        ShowPopup(points, at, source);
    }

    public Text ShowPopup(int points, Vector3 at, RunScore.Source source)
    {
        // (an escape is the only popup that is not a gain: "-N  ESCAPED", or just "ESCAPED" when nothing was left to lose)
        if (canvasRect == null || (points <= 0 && source != RunScore.Source.Escape && source != RunScore.Source.BossEscape)) return null;
        if (popups == null) BuildPopups();

        int index = nextPopup;
        nextPopup = (nextPopup + 1) % popups.Length;
        var p = popups[index];
        if (p.glow != null && p.glow.enabled) p.glow.enabled = false;
        var style = StyleFor(source, source == RunScore.Source.Kill && RunScore.Multiplier > 1);
        p.text.text = (points < 0 ? "-" + RunScore.Format(-points) : points == 0 ? "" : "+" + RunScore.Format(points)) + (points == 0 ? style.suffix.TrimStart() : style.suffix);
        p.text.fontSize = style.size;
        p.text.color = style.colour;
        p.seconds = style.seconds;
        p.rise = style.rise;
        p.from = KeepInSafeArea(p.text, ToCanvas(at), p.rise);
        p.age = 0f;
        p.text.gameObject.SetActive(true);
        p.text.transform.SetAsLastSibling();
        var rt = p.text.rectTransform;
        rt.anchoredPosition = p.from;
        rt.localScale = Vector3.zero;
        popups[index] = p;
        return p.text;
    }

    void BuildPopups()
    {
        popups = new Popup[PopupPool];
        for (int i = 0; i < popups.Length; i++)
        {
            var go = new GameObject("ScorePopup", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(canvasRect, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
            rt.sizeDelta = new Vector2(360f, 60f);
            var t = go.GetComponent<Text>();
            Style(t, font, 24, ScoreColour, TextAnchor.MiddleCenter);
            // a fusion callout's own glow, round the ink outline (off otherwise)
            var glow = go.AddComponent<Outline>();
            glow.effectDistance = new Vector2(GlowDistance, -GlowDistance);
            glow.useGraphicAlpha = true;
            glow.enabled = false;
            go.SetActive(false);
            popups[i] = new Popup { text = t, glow = glow, age = -1f };
        }
    }

    // A world position on the HUD canvas (centre origin); NaN = the middle of
    // the screen, a little above centre. Kept clear of the screen edges.
    Vector2 ToCanvas(Vector3 world)
    {
        Vector2 size = canvasRect.rect.size;
        if (float.IsNaN(world.x) || Camera.main == null) return new Vector2(0f, size.y * .12f);
        Vector2 screen = Camera.main.WorldToScreenPoint(world);
        Camera uiCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, uiCam, out local))
            return Vector2.zero;
        local.x = Mathf.Clamp(local.x, -size.x * .5f + 120f, size.x * .5f - 120f);
        local.y = Mathf.Clamp(local.y + 40f, -size.y * .5f + 60f, size.y * .5f - 200f);
        return local;
    }

    // The part of the HUD canvas popups may use: the screen's safe area, in
    // canvas units (centre origin). Insets are taken as fractions of the
    // screen, so this holds whatever the canvas's scale factor is.
    Rect SafeUnits()
    {
        Vector2 size = canvasRect.rect.size;
        var full = new Rect(-size.x * .5f, -size.y * .5f, size.x, size.y);
        float w = ScreenInfo.Width, h = ScreenInfo.Height;
        Rect safe = ScreenInfo.SafeArea;
        if (w <= 0f || h <= 0f || safe.width <= 0f || safe.height <= 0f) return full;
        return Rect.MinMaxRect(full.xMin + size.x * Mathf.Clamp01(safe.xMin / w), full.yMin + size.y * Mathf.Clamp01(safe.yMin / h),
                               full.xMin + size.x * Mathf.Clamp01(safe.xMax / w), full.yMin + size.y * Mathf.Clamp01(safe.yMax / h));
    }

    // A popup's start point moved so that its whole line -- at rest and at
    // the top of its rise -- stays inside the safe area (clear of the notch,
    // the rounded corners' insets and the home indicator). A line wider than
    // the safe area (a long banner on a narrow phone) is set smaller to fit.
    // The fixed clamps in ToCanvas assumed a short "+N" and the bare screen.
    Vector2 KeepInSafeArea(Text t, Vector2 at, float rise)
    {
        const float margin = 12f;
        Rect safe = SafeUnits();
        float room = safe.width - 2f * margin;
        float width = t.preferredWidth;
        if (width > room && width > 0f && room > 0f)
        {
            t.fontSize = Mathf.Max(12, Mathf.FloorToInt(t.fontSize * room / width));
            width = Mathf.Min(t.preferredWidth, room);
        }
        float halfW = width * .5f, halfH = t.fontSize * .7f;
        float xMin = safe.xMin + margin + halfW, xMax = safe.xMax - margin - halfW;
        float yMin = safe.yMin + margin + halfH, yMax = safe.yMax - margin - halfH - Mathf.Max(0f, rise);
        at.x = xMax >= xMin ? Mathf.Clamp(at.x, xMin, xMax) : safe.center.x;
        at.y = yMax >= yMin ? Mathf.Clamp(at.y, yMin, yMax) : safe.center.y;
        return at;
    }

    // Advances every live popup by `dt` of world time (0 while frozen: they
    // hold still). A negative dt (the run is over) clears them all.
    public void StepPopups(float dt)
    {
        if (popups == null) return;
        for (int i = 0; i < popups.Length; i++)
        {
            var p = popups[i];
            if (p.age < 0f) continue;
            if (dt < 0f) p.age = p.seconds;
            else p.age += dt;
            if (p.age >= p.seconds)
            {
                p.text.gameObject.SetActive(false);
                if (p.glow != null && p.glow.enabled) p.glow.enabled = false;
                p.age = -1f;
                popups[i] = p;
                continue;
            }
            popups[i] = p;
            // Held on twos (12 drawings a second) like the art guide's flipbooks.
            float t = Mathf.Floor(p.age * 12f) / 12f;
            float q = t / p.seconds;
            var rt = p.text.rectTransform;
            float ease = 1f - (1f - Mathf.Min(1f, q * 1.6f)) * (1f - Mathf.Min(1f, q * 1.6f));
            rt.anchoredPosition = p.from + new Vector2(0f, p.rise * ease);
            // Pop: big on the first drawing, a squash, then settled.
            float s = t < 1f / 12f ? 1.6f : t < 2f / 12f ? .9f : 1f;
            if (!Mathf.Approximately(rt.localScale.x, s)) rt.localScale = new Vector3(s, s, 1f);
            var c = p.text.color;
            float a = q < .65f ? 1f : q < .82f ? .6f : .3f;
            if (!Mathf.Approximately(c.a, a)) { c.a = a; p.text.color = c; }
        }
    }

    public int LivePopups
    {
        get
        {
            int n = 0;
            if (popups != null) foreach (var p in popups) if (p.age >= 0f) n++;
            return n;
        }
    }
}
