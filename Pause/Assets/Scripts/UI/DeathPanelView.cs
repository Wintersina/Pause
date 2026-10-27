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
//
// Cards, top to bottom: the run SCORE (headline, counting up, NEW BEST), the
// breakdown of where it came from (one counting row per source, staggered),
// and the star dust earned / new total. No speed is shown: speed is capped,
// so it is not a result.
public class DeathPanelView : MonoBehaviour, IPointerDownHandler
{
    public struct Results
    {
        public long score;          // this run's points (RunScore.Total)
        public long bestScore;      // the best after this run
        public bool newBest;        // this run set a new best score
        public bool ranked;         // a run that may set a best (not developer mode / practice)
        public bool unranked;       // flown on the premium cheat ship: never ranked (RunScore.Unranked)
        public bool practice;       // the run scored nothing (practice run)
        public RunScore.Breakdown parts;
        public float dustAtStart;
        public float dustWon;       // includes dustBonus
        public float dustBonus;     // the end-of-run score bonus (ScoreRules.ScoreDustBonus)
        public string killedBy;     // the codex name of what landed the fatal hit (null: unknown)
        public string hurtBy;       // hearts lost on the way, "Name x2, Name" (null: none)
    }

    // Breakdown rows, in display order.
    public static readonly string[] BreakdownLabels =
        { "DISTANCE", "KILLS", "STAR DUST", "ATOMS", "TELEPORTS", "BOSSES", "WORLDS", "DEATH COMBO" };

    public static long[] BreakdownPoints(RunScore.Breakdown b)
    {
        return new[] { b.distance, b.kills, b.dust, b.atoms, b.teleports, b.bosses, b.worlds, b.deathCombo };
    }

    // The count shown beside each row (distance has none; DEATH COMBO counts
    // the kills of the death's domino, DeathCrash).
    public static int[] BreakdownCounts(RunScore.Breakdown b)
    {
        return new[] { -1, b.killCount, b.dustCount, b.atomCount, b.teleportCount, b.bossCount, b.worldCount, b.deathComboKills };
    }

    // ---- Layout (panel space: canvas units, origin at the panel centre) ----

    public const float Width = 680f, Height = 952f;
    // How far the frame sprite's neon bloom reaches outside the panel body.
    public const float GlowMargin = 20f;
    public const float CardWidth = 600f;
    // The frame's 9-slice borders are drawn this many times smaller than the sprite's own pixel density gives.
    public const float FrameBorderScale = 1.8f;
    public const float ButtonWidth = 288f, ButtonHeight = 96f;   // ButtonWidth: the tutorial panel's pair
    // The world frames' bottom borders (ice, vines, coral, lightning...) rise
    // well into the panel at their native size, so the frame's 9-slice borders
    // are drawn at 1/FrameBorderScale of that (FrameBorderScale above) and the
    // stack of cards and buttons is packed 6 units apart: SPACE DOCK then ends
    // above every world's bottom border art with the panel no taller than before
    // (the 480x854 phone has no height to spare: its text is at the 7 dp floor).
    // The side borders reach different distances inward (measured per world
    // from renders of the frames alone; the Desert one is the deepest), so
    // the buttons' span is per world: BorderHalfSpan is the distance from the
    // panel's centre line to the border's inner edge on the button rows.
    public const float ButtonClearance = 10f;   // glow edge to border
    public const float ButtonGlowPad = 4f;      // glow reaches this far past the button body
    public const float ButtonColumnGap = 24f;
    // Space, Frost, Verdant, Ember, Tide, Storm, Desert (units).
    static readonly float[] BorderHalfSpan = { 335f, 273f, 310f, 316f, 296f, 319f, 299f };
    public static float BorderSpan(int world) { return BorderHalfSpan[Mathf.Clamp(world, 0, BorderHalfSpan.Length - 1)]; }
    public static float ButtonHalfSpan(int world)
    {
        float inner = BorderSpan(world);
        return Mathf.Min(CardWidth * .5f, inner - ButtonClearance - ButtonGlowPad);
    }
    public const float ReplayY = -208f, DockY = -310f;
    // [REPLAY, MENU, SPACE DOCK] for a world's frame.
    public static Rect[] ButtonRects(int world)
    {
        float span = ButtonHalfSpan(world);
        float w = span - ButtonColumnGap * .5f;
        float down = StackShift(world);
        return new[]
        {
            new Rect(-span, ReplayY - down - ButtonHeight * .5f, w, ButtonHeight),
            new Rect(ButtonColumnGap * .5f, ReplayY - down - ButtonHeight * .5f, w, ButtonHeight),
            new Rect(-span, DockY - down - ButtonHeight * .5f, 2f * span, ButtonHeight),
        };
    }
    // ---- Anchored to the frame art ----
    // Where the frame's top border art ends (units below the frame's top edge,
    // measured from the sprite, FrameInsets) and the same at the bottom.
    public static float FrameTopInset(int world) { return FrameInsets.Top(HudSkin.DeathPanel(world), world, FrameBorderScale, Width + 2f * GlowMargin, Height + 2f * GlowMargin); }
    public static float FrameBottomInset(int world) { return FrameInsets.Bottom(HudSkin.DeathPanel(world), world, FrameBorderScale, Width + 2f * GlowMargin, Height + 2f * GlowMargin); }
    // Panel-space y of the frame's top border's inner edge.
    public static float FrameInnerTop(int world) { return Height * .5f + GlowMargin - FrameTopInset(world); }
    public static float FrameInnerBottom(int world) { return -Height * .5f - GlowMargin + FrameBottomInset(world); }
    // How far the banner, its rule and the whole stack below it move down (units)
    // so the banner's top edge sits BannerGap under the frame's top border art:
    // as much as that needs, never more than the room above the frame's bottom
    // border under SPACE DOCK (keeping ButtonClearance there).
    public static float StackShift(int world)
    {
        float need = BannerTopBase - (FrameInnerTop(world) - BannerGap);
        float room = (DockY - ButtonHeight * .5f) - (FrameInnerBottom(world) + ButtonClearance);
        return Mathf.Max(0f, Mathf.Min(need, room));
    }
    // The banner's top edge (panel space) for a world's frame.
    public static float BannerTop(int world) { return BannerTopBase - StackShift(world); }
    float stackShift;
    Rect Shifted(Rect r) { return new Rect(r.x, r.y - stackShift, r.width, r.height); }
    Vector2 Shifted(Vector2 v) { return new Vector2(v.x, v.y - stackShift); }

    // Card-local columns: labels start right of the accent bar, values end
    // with the same inset from the right edge on every row.
    // (instance: the cards are as wide as the world's button column, which
    // is the frame's inner span less the clearances; CardWidth is the widest.)
    // The inset tightens a little as the card narrows (shrink gaps, not text).
    float SideInset { get { return Mathf.Clamp(44f - (CardWidth - cardW) * .1f, 34f, 44f); } }
    float LabelLeft { get { return -cardW * .5f + SideInset; } }
    float ValueRight { get { return cardW * .5f - SideInset; } }
    // The cards' width for a world's frame: the button column's width.
    public static float CardWidthFor(int world) { return 2f * ButtonHalfSpan(world); }
    public static Rect[] CardRectsFor(int world)
    {
        float w = CardWidthFor(world);
        var r = new Rect[CardRects.Length];
        for (int i = 0; i < r.Length; i++) r[i] = new Rect(-w * .5f, CardRects[i].y, w, CardRects[i].height);
        return r;
    }
    float cardW = CardWidth;

    // The panel grew by 116 for the SPACE DOCK row (a button plus a 16 gap)
    // and by 76 for the KILLED BY card (64 plus a 12 gap); everything above
    // them moved up by half of that so the centre-origin layout stays put
    // relative to the top edge.
    // The banner tag (slab) is BannerHeight tall and its bottom edge stays
    // where it was; BannerTopBase is where its top edge sits before the frame
    // pushes the stack down (StackShift).
    public const float BannerHeight = 64f, BannerBottom = 380f;
    public const float BannerTopBase = BannerBottom + BannerHeight;
    // Clear space kept between the frame's top border art and the banner's top edge.
    public const float BannerGap = 8f;
    public static readonly Rect HeaderRect = Centered(0f, BannerBottom + BannerHeight * .5f, 600f, 56f);
    public static readonly Rect DividerRect = Centered(0f, 376f, 440f, 16f);
    public static readonly Rect[] CardRects =
    {
        Centered(0f, 306f, CardWidth, 100f),   // score
        Centered(0f, 134f, CardWidth, 232f),    // breakdown
        Centered(0f, -36f, CardWidth, 96f),   // star dust
        Centered(0f, -122f, CardWidth, 64f),   // killed by / hurt by
    };
    // (the widest set, Space's; the frames' own spans come from ButtonRects)
    public static readonly Rect ReplayRect = ButtonRects(0)[0];
    public static readonly Rect MenuRect = ButtonRects(0)[1];
    // SPACE DOCK: a full-column button under REPLAY / MENU that goes straight
    // to the ship dock (shopS6) so the run's star dust can be spent at once.
    public static readonly Rect DockRect = ButtonRects(0)[2];
    public const string DockScene = "shopS6";
    public const int ButtonCount = 3;
    public const int CardCount = 4;
    public const string KilledByCardName = "Card3";

    // Breakdown card (card-local): title row, then one row per source.
    public const float BreakdownTitleY = 92f;
    // Eight source rows (the last, DEATH COMBO, the death's domino), then the
    // LOOPS line (loops flown, and the highest score multiplier reached) --
    // nine rows in the same card.
    public const float BreakdownFirstRowY = 64f, BreakdownRowStep = 21f, BreakdownRowHeight = 21f;
    public const string LoopsRowName = "Loops";
    const float CountRight = 84f;
    public static Rect PanelRect { get { return Centered(0f, 0f, Width, Height); } }

    // ---- Timeline (seconds of unscaled time since the panel appeared) ----

    public const float IntroDuration = 1.2f;
    const float PanelIn = .38f;
    const float CardStart = .2f, CardStagger = .1f, CardDuration = .32f;
    const float ScoreCountFrom = .22f, ScoreCountTo = .8f;
    const float RowCountFrom = .3f, RowStagger = .07f, RowCountDuration = .28f;
    const float DustCountFrom = .5f, DustCountTo = .9f;
    const float NewBestAt = .78f;
    const float DustBurstAt = .82f, BurstDuration = .38f;
    const float ButtonsStart = .66f, ButtonStagger = .07f, ButtonDuration = .3f;
    // The score bonus lands after the run's own dust has counted up: its line
    // counts, then pops with a sparkle and a rising "+x", and EARNED / TOTAL
    // tick up to include it.
    const float BonusCountFrom = .9f, BonusCountTo = 1.02f, BonusBurstAt = 1.02f;
    public const string ScoreBonusName = "ScoreBonus", BonusPopupName = "BonusPopup";

    // Akira palette (docs/art-style.md): CYAN score, Kaneda RED for the
    // breakdown and MENU, AMBER star dust, BONE type over INK.
    static readonly Color Cyan = AkiraPalette.Cyan;
    static readonly Color Coral = AkiraPalette.Red;
    static readonly Color Gold = AkiraPalette.Amber;
    static readonly Color Ink = AkiraPalette.WithAlpha(AkiraPalette.Ink, .95f);
    static readonly Color Muted = AkiraPalette.Muted;
    static readonly Color Bone = AkiraPalette.Bone;

    const string SpriteRoot = "DeathPanel/";
    const int SparklesPerBurst = 10;

    // ---- Built state (all cached; nothing is looked up per frame) ----

    Results results;
    Font font;
    RectTransform panel;
    CanvasGroup panelGroup;
    Image scrim, divider, bestGlow, slab;
    RectTransform[] headerSparkles = new RectTransform[2];
    RectTransform[] cards = new RectTransform[CardCount];
    CanvasGroup[] cardGroups = new CanvasGroup[CardCount];
    Text scoreValue, dustValue, dustTotal;
    Text[] rowValues = new Text[0];
    long[] rowPoints = new long[0];
    long[] shownRows = new long[0];
    RectTransform pill;
    RectTransform[] buttonSlots = new RectTransform[ButtonCount];
    CanvasGroup[] buttonGroups = new CanvasGroup[ButtonCount];
    Image[] buttonGlows = new Image[ButtonCount];
    DeathPanelPress[] presses = new DeathPanelPress[ButtonCount];
    Image[] dustBurst, bestBurst, bonusBurst;
    Text bonusLine, bonusPopup;
    Vector2 bonusCentre;
    int shownBonusCents = -1;
    Vector2 dustValueCentre, bestValueCentre;

    float startedAt = -1f;
    bool finalApplied;
    float fitScale = 1f;
    int lastScreenW, lastScreenH;
    long shownScore = -1;
    int shownDustCents = -1;

    public RectTransform Panel { get { return panel; } }
    public RectTransform ReplaySlot { get { return buttonSlots[0]; } }
    public RectTransform MenuSlot { get { return buttonSlots[1]; } }
    public RectTransform DockSlot { get { return buttonSlots[2]; } }
    Rect[] buttonRects = ButtonRects(0);
    Rect ButtonRect(int i) { return buttonRects[i]; }
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
        view.stackShift = StackShift(HudSkin.CurrentWorld);
        view.cardW = CardWidthFor(HudSkin.CurrentWorld);

        // MENU leaves to the start menu, so it wears the quick actions' home glyph.
        Sprite replayGlyph = HudSkin.ReplayGlyph(HudSkin.CurrentWorld);
        Sprite menuGlyph = HudSkin.HomeGlyph(HudSkin.CurrentWorld);

        // The old scene dialog: retire it once its parts have been moved out.
        Transform legacyDialog = dustText.transform;
        while (legacyDialog.parent != null && legacyDialog.parent != canvasRoot) legacyDialog = legacyDialog.parent;

        view.BuildScrim(rootRt);
        view.BuildPanel(rootRt);
        view.BuildHeader();
        view.BuildCards(bestText, runText, dustText);
        view.buttonRects = ButtonRects(HudSkin.CurrentWorld);
        view.BuildButtons(replay, replayGlyph, menu, menuGlyph);
        view.dustBurst = view.BuildBurst("DustBurst", Gold);
        view.bestBurst = view.BuildBurst("BestBurst", Cyan);
        view.BuildScoreBonus();

        if (legacyDialog != null && legacyDialog != root.transform && legacyDialog.parent == canvasRoot)
            legacyDialog.gameObject.SetActive(false);

        view.ApplyAt(0f);
        return view;
    }

    void BuildScrim(RectTransform root)
    {
        scrim = NewImage("Scrim", root, null, AkiraPalette.WithAlpha(AkiraPalette.Night0, 0f));
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

        var frame = NewImage("Frame", panel, HudSkin.DeathPanel(HudSkin.CurrentWorld), Color.white);
        frame.type = Image.Type.Sliced;
        frame.pixelsPerUnitMultiplier = FrameBorderScale;
        frame.raycastTarget = true;
        var r = PanelRect;
        Place(frame.rectTransform, new Rect(r.xMin - GlowMargin, r.yMin - GlowMargin,
                                            r.width + 2f * GlowMargin, r.height + 2f * GlowMargin));
    }

    void BuildHeader()
    {
        // The Akira title-card stripe: a red slab behind the heading.
        slab = NewImage("TitleSlab", panel, Load("dp_slab"), Color.white);
        Place(slab.rectTransform, Shifted(Centered(0f, HeaderRect.center.y, 640f, BannerHeight)));

        var title = NewText("Title", panel, "FLIGHT COMPLETE", 40, Bone, TextAnchor.MiddleCenter);
        Place(title.rectTransform, Shifted(HeaderRect));
        AddOutline(title.gameObject, Ink, 2f);

        float half = Mathf.Min(title.preferredWidth, HeaderRect.width - 80f) * .5f + 28f;
        for (int i = 0; i < 2; i++)
        {
            var sparkle = NewImage(i == 0 ? "SparkleLeft" : "SparkleRight", panel, Load("dp_sparkle"), Gold);
            float x = i == 0 ? -half : half;
            Place(sparkle.rectTransform, Shifted(Centered(x, HeaderRect.center.y, 30f, 30f)));
            headerSparkles[i] = sparkle.rectTransform;
        }

        divider = NewImage("Divider", panel, Load("dp_divider"), Color.white);
        divider.preserveAspect = true;
        Place(divider.rectTransform, Shifted(DividerRect));
    }

    // The score card's sub-label: the best to beat, or why this run can't set one.
    public static string ScoreSubLabel(Results r)
    {
        if (r.practice) return "PRACTICE RUN";
        if (r.unranked) return PremiumShips.UnrankedRunLabel;
        if (!r.ranked) return "DEV RUN - NOT SAVED";
        return "BEST  " + RunScore.Format(r.bestScore);
    }

    // The scene's Texts keep their jobs under new names: bestText is the
    // score, dustText the star dust. runText (the scene's old speed line) has
    // no job any more: it is switched off and left in the old dialog.
    void BuildCards(Text bestText, Text runText, Text dustText)
    {
        cards[0] = BuildCard(0, "SCORE", results.newBest ? null : ScoreSubLabel(results), Cyan, bestText, out scoreValue);
        Place(scoreValue.rectTransform, Centered(ValueRight - 170f, 0f, 340f, 80f));
        cards[1] = BuildBreakdown(runText);
        cards[2] = BuildCard(2, "STAR DUST", "EARNED THIS RUN", Gold, dustText, out dustValue);
        cards[3] = BuildKilledBy();

        // Star dust: the earned amount and the new total on separate lines,
        // both right-aligned on the same column as the score.
        Place(dustValue.rectTransform, Centered(ValueRight - 150f, 13f, 300f, 52f));
        dustValue.fontSize = 40;
        dustTotal = NewText("Total", cards[2], "", 18, Muted, TextAnchor.MiddleRight);
        Place(dustTotal.rectTransform, Centered(ValueRight - 150f, -28f, 300f, 26f));

        // Burst origins: roughly the middle of the right-aligned digits.
        dustValueCentre = Shifted(CardRects[2].center + new Vector2(ValueRight - 95f, 13f));
        bestValueCentre = Shifted(CardRects[0].center + new Vector2(ValueRight - 90f, 0f));

        if (results.newBest)
        {
            // Celebratory bloom behind the score card ...
            bestGlow = NewImage("NewBestGlow", panel, Load("dp_glow"), new Color(Gold.r, Gold.g, Gold.b, 0f));
            bestGlow.type = Image.Type.Sliced;
            var c = Shifted(CardRectsFor(HudSkin.CurrentWorld)[0]);
            Place(bestGlow.rectTransform, new Rect(c.xMin - 20f, c.yMin - 20f, c.width + 40f, c.height + 40f));
            bestGlow.rectTransform.SetSiblingIndex(cards[0].GetSiblingIndex());

            // ... and a gold "NEW BEST" capsule in the sub-label slot.
            var p = NewImage("NewBest", cards[0], Load("dp_pill"), Gold);
            p.type = Image.Type.Sliced;
            Place(p.rectTransform, Centered(LabelLeft + 62f, -20f, 124f, 32f));
            var label = NewText("Label", p.rectTransform, "NEW BEST", 17, AkiraPalette.Ink, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            pill = p.rectTransform;
        }
    }

    RectTransform CardBody(int index, Color accent)
    {
        var rect = Shifted(CardRectsFor(HudSkin.CurrentWorld)[index]);
        var go = new GameObject("Card" + index, typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(panel, false);
        var card = (RectTransform)go.transform;
        Place(card, rect);
        cardGroups[index] = go.GetComponent<CanvasGroup>();

        // The card is drawn in its final colours; the accent is a slanted
        // slab down its left edge (dp_bar, tinted).
        var bg = NewImage("Background", card, Load("dp_card"), Color.white);
        bg.type = Image.Type.Sliced;
        Stretch(bg.rectTransform);
        var bar = NewImage("Accent", card, Load("dp_bar"), accent);
        Place(bar.rectTransform, Centered(-cardW * .5f + 20f, 0f, 14f, rect.height - 30f));
        return card;
    }

    // Where the run's points came from: a title row and one row per source
    // -- label, count, points.
    RectTransform BuildBreakdown(Text unusedSceneText)
    {
        if (unusedSceneText != null) unusedSceneText.gameObject.SetActive(false);
        var card = CardBody(1, Coral);
        var title = NewText("Label", card, "BREAKDOWN", 20, Coral, TextAnchor.MiddleLeft);
        Place(title.rectTransform, Centered(LabelLeft + 100f, BreakdownTitleY, 200f, 30f));
        AddOutline(title.gameObject, Ink, 1.5f);

        rowPoints = BreakdownPoints(results.parts);
        int[] counts = BreakdownCounts(results.parts);
        rowValues = new Text[rowPoints.Length];
        shownRows = new long[rowPoints.Length];
        for (int i = 0; i < rowPoints.Length; i++)
        {
            float y = BreakdownFirstRowY - i * BreakdownRowStep;
            var row = new GameObject("Row" + i, typeof(RectTransform));
            row.transform.SetParent(card, false);
            var rt = (RectTransform)row.transform;
            Place(rt, Centered(0f, y, cardW - 40f, BreakdownRowHeight));

            var label = NewText("Label", rt, BreakdownLabels[i], 17, Bone, TextAnchor.MiddleLeft);
            Place(label.rectTransform, new Rect(LabelLeft, -BreakdownRowHeight * .5f, 200f, BreakdownRowHeight));
            var count = NewText("Count", rt, counts[i] >= 0 ? counts[i].ToString() : "", 17, Muted, TextAnchor.MiddleRight);
            Place(count.rectTransform, new Rect(CountRight - 90f, -BreakdownRowHeight * .5f, 90f, BreakdownRowHeight));
            var value = NewText("Points", rt, "0", 19, rowPoints[i] > 0 ? Bone : Muted, TextAnchor.MiddleRight);
            Place(value.rectTransform, new Rect(ValueRight - 160f, -BreakdownRowHeight * .5f, 160f, BreakdownRowHeight));
            AddOutline(value.gameObject, Ink, 1.5f);
            rowValues[i] = value;
            shownRows[i] = -1;
        }
        BuildLoopsRow(card, BreakdownFirstRowY - rowPoints.Length * BreakdownRowStep);
        return card;
    }

    // KILLED BY <name> over HURT BY <names>: what ended the run and what chipped at it.
    // Nothing is made up: an unknown killer shows a dash, no earlier hits reads "NO OTHER HITS".
    public const int HurtLineChars = 28;   // names fit the 520 px line at HurtFontSize before "+N more"
    const int HurtFontSize = 18, HurtMinFontSize = 18;   // 18 units stays >= 7 dp on the smallest (480x854) phones
    public static string KilledByLabel(Results r) { return string.IsNullOrEmpty(r.killedBy) ? "-" : r.killedBy; }
    public static string HurtByLabel(Results r) { return string.IsNullOrEmpty(r.hurtBy) ? "NO OTHER HITS" : r.hurtBy.ToUpperInvariant(); }

    const int KilledFontSize = 22, KilledMinFontSize = 18;

    RectTransform BuildKilledBy()
    {
        var card = CardBody(3, Coral);
        var killed = NewText("Killed", card, "KILLED BY  " + KilledByLabel(results).ToUpperInvariant(), KilledFontSize, Bone, TextAnchor.MiddleLeft);
        var hurt = NewText("Hurt", card, "HURT BY  " + HurtByLabel(results), HurtFontSize, Muted, TextAnchor.MiddleLeft);

        // Measured once, here (the panel is built per death): padding stays constant, the text
        // shrinks then wraps, and the card grows to fit instead of the text filling the box.
        float inner = DeathNameLayout.InnerWidth(cardW);
        var lay = DeathNameLayout.Compute(killed.preferredWidth, KilledFontSize, KilledMinFontSize,
                                          hurt.preferredWidth, HurtFontSize, HurtMinFontSize, inner);
        float h = lay.cardHeight;
        card.sizeDelta = new Vector2(cardW, h);
        var bar = card.Find("Accent") as RectTransform;
        if (bar != null) bar.sizeDelta = new Vector2(bar.sizeDelta.x, h - 30f);

        float left = -cardW * .5f + DeathNameLayout.PadLeft;
        float gap = lay.killedLines > 0 && lay.hurtLines > 0 ? DeathNameLayout.LineGap : 0f;
        float top = (lay.killedHeight + lay.hurtHeight + gap) * .5f;   // block centred in the card
        killed.fontSize = lay.killedFont;
        hurt.fontSize = lay.hurtFont;
        SetLine(killed, left, top, lay.killedHeight, inner, lay.killedLines);
        SetLine(hurt, left, top - lay.killedHeight - gap, lay.hurtHeight, inner, lay.hurtLines);
        return card;
    }

    // A text box whose top edge is y (card space, y up), `width` wide, wrapping when it needs a second line.
    static void SetLine(Text t, float left, float y, float height, float width, int lines)
    {
        t.horizontalOverflow = lines > 1 ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
        t.resizeTextForBestFit = false;
        t.alignment = TextAnchor.MiddleLeft;
        Place(t.rectTransform, new Rect(left, y - height, width, height));
    }

    public static string LoopsCount(RunScore.Breakdown b) { return b.loops.ToString(); }

    // The highest multiplier any points were earned at (speed x chain).
    public static string BestMultiplierLabel(RunScore.Breakdown b)
    {
        return "MAX " + ScoreRules.MultiplierLabel(Mathf.Max(1f, b.bestMultiplier));
    }

    // LOOPS  <n>  MAX x2.5 -- not points, so it shows straight away.
    void BuildLoopsRow(RectTransform card, float y)
    {
        var row = new GameObject(LoopsRowName, typeof(RectTransform));
        row.transform.SetParent(card, false);
        var rt = (RectTransform)row.transform;
        Place(rt, Centered(0f, y, cardW - 40f, BreakdownRowHeight));

        var label = NewText("Label", rt, "LOOPS", 17, Gold, TextAnchor.MiddleLeft);
        Place(label.rectTransform, new Rect(LabelLeft, -BreakdownRowHeight * .5f, 200f, BreakdownRowHeight));
        var count = NewText("Count", rt, LoopsCount(results.parts), 17, results.parts.loops > 0 ? Gold : Muted, TextAnchor.MiddleRight);
        Place(count.rectTransform, new Rect(CountRight - 90f, -BreakdownRowHeight * .5f, 90f, BreakdownRowHeight));
        var value = NewText("Points", rt, BestMultiplierLabel(results.parts), 19, Gold, TextAnchor.MiddleRight);
        Place(value.rectTransform, new Rect(ValueRight - 160f, -BreakdownRowHeight * .5f, 160f, BreakdownRowHeight));
        AddOutline(value.gameObject, Ink, 1.5f);
    }

    // A scene Text moved into the panel, its old layout switched off.
    void AdoptSceneText(Text t, Transform parent, int size, Color color, TextAnchor align)
    {
        t.transform.SetParent(parent, false);
        t.gameObject.SetActive(true);
        t.transform.localScale = Vector3.one;
        foreach (var le in t.GetComponents<LayoutElement>()) le.ignoreLayout = true;
        t.font = font;
        t.fontSize = size;
        t.alignment = align;
        t.resizeTextForBestFit = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        t.color = color;
    }

    RectTransform BuildCard(int index, string label, string sub, Color accent, Text value, out Text valueOut)
    {
        var card = CardBody(index, accent);

        // Label column: name on top, a quieter sub-label (or the NEW BEST
        // capsule) underneath, both starting just right of the accent bar.
        var title = NewText("Label", card, label, 23, accent, TextAnchor.MiddleLeft);
        Place(title.rectTransform, Centered(LabelLeft + 150f, 18f, 300f, 36f));
        AddOutline(title.gameObject, Ink, 1.5f);
        if (sub != null)
        {
            var subText = NewText("Sub", card, sub, 17, Muted, TextAnchor.MiddleLeft);
            Place(subText.rectTransform, Centered(LabelLeft + 150f, -20f, 300f, 28f));
        }

        // The scene's own Text becomes the value, so playerIsDead's serialized
        // references keep pointing at the number the player reads.
        AdoptSceneText(value, card, 50, Bone, TextAnchor.MiddleRight);
        value.fontStyle = FontStyle.Bold;
        Place(value.rectTransform, Centered(ValueRight - 130f, 0f, 260f, 80f));
        AddOutline(value.gameObject, Ink, 2f);
        valueOut = value;
        return card;
    }

    void BuildButtons(Button replay, Sprite replayGlyph, Button menu, Sprite menuGlyph)
    {
        buttonSlots[0] = BuildButton(0, replay, replayGlyph, "REPLAY", Cyan, buttonRects[0]);
        buttonSlots[1] = BuildButton(1, menu, menuGlyph, "MENU", Coral, buttonRects[1]);
        buttonSlots[2] = BuildButton(2, NewDockButton(), null, "SPACE DOCK", Gold, buttonRects[2]);
    }

    // The dock button is made here (the scene has no third button). It is
    // the same kind of Button as the scene's two, restyled by BuildButton.
    Button NewDockButton()
    {
        var go = new GameObject("SpaceDock", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(panel, false);
        var button = go.GetComponent<Button>();
        button.onClick.AddListener(OpenSpaceDock);
        return button;
    }

    // Leave for the Space Dock. The run is already banked (playerIsDead
    // settles score and saves the dust before this panel is built); this is
    // MENU's exit with a different destination: clear run state (timeScale,
    // dead flags), then load the dock. The dock's Back goes home as always.
    static bool leavingForDock;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void HookSceneLoaded()
    {
        leavingForDock = false;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) => leavingForDock = false;
    }

    public static void ResetLeavingForDock() { leavingForDock = false; }

    public static void OpenSpaceDock()
    {
        if (leavingForDock) return;   // a double tap would clear and load twice
        leavingForDock = true;
        moveBackGround.speed = 0f;
        GameStateReset.Clear();
        BackNavigator.LoadScene(DockScene);
    }

    RectTransform BuildButton(int index, Button button, Sprite glyph, string label, Color accent, Rect rect)
    {
        var slotGo = new GameObject(label == "REPLAY" ? "ReplaySlot" : label == "MENU" ? "MenuSlot" : "DockSlot", typeof(RectTransform), typeof(CanvasGroup));
        slotGo.transform.SetParent(panel, false);
        var slot = (RectTransform)slotGo.transform;
        Place(slot, rect);
        buttonGroups[index] = slotGo.GetComponent<CanvasGroup>();

        var glow = NewImage("Glow", slot, Load("dp_glow"), new Color(accent.r, accent.g, accent.b, 0f));
        glow.type = Image.Type.Sliced;
        Place(glow.rectTransform, Centered(0f, 0f, rect.width + 2f * ButtonGlowPad, rect.height + 2f * ButtonGlowPad));
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
            frame.sprite = TranslucentButtonSprite(Load("dp_button"));
            frame.type = Image.Type.Sliced;
            frame.preserveAspect = false;
            frame.color = accent;
            frame.raycastTarget = true;
            button.targetGraphic = frame;
        }

        // Icon + label as one centred group.
        var text = button.GetComponentInChildren<Text>(true);
        if (text == null) text = NewText("Label", rt, label, 24, Bone, TextAnchor.MiddleLeft);
        text.text = label;
        text.font = font;
        text.fontSize = 24;
        text.fontStyle = FontStyle.Bold;
        text.color = Bone;
        text.alignment = TextAnchor.MiddleLeft;
        text.resizeTextForBestFit = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.raycastTarget = false;
        text.gameObject.SetActive(true);
        AddOutline(text.gameObject, Ink, 1.5f);

        const float iconSize = 32f, gap = 12f;
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

    public static string ScoreBonusLabel(float bonus)
    {
        return "SCORE BONUS  +" + bonus.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
    }

    // The star dust card's sub-label becomes the score bonus line when the
    // run earned one.
    void BuildScoreBonus()
    {
        if (results.dustBonus <= 0f) return;
        var sub = cards[2].Find("Sub");
        if (sub != null) sub.gameObject.SetActive(false);
        bonusLine = NewText(ScoreBonusName, cards[2], ScoreBonusLabel(results.dustBonus), 18, Gold, TextAnchor.MiddleLeft);
        Place(bonusLine.rectTransform, Centered(LabelLeft + 150f, -20f, 300f, 28f));
        AddOutline(bonusLine.gameObject, Ink, 1.5f);
        bonusCentre = Shifted(CardRects[2].center + new Vector2(LabelLeft + 90f, -20f));
        bonusBurst = BuildBurst("BonusBurst", Gold);
        bonusPopup = NewText(BonusPopupName, panel, "+" + results.dustBonus.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " ★",
                             26, Gold, TextAnchor.MiddleCenter);
        Place(bonusPopup.rectTransform, Centered(bonusCentre.x, bonusCentre.y, 160f, 34f));
        AddOutline(bonusPopup.gameObject, Ink, 2f);
        bonusPopup.gameObject.SetActive(false);
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
        if (ScreenInfo.Width != lastScreenW || ScreenInfo.Height != lastScreenH) Fit();
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
        // Moves are held on 2s (12 drawings a second) like the art guide's
        // flipbooks; fades and the counting numbers stay smooth.
        float tq = OnTwos(t);
        // Backdrop dim and panel pop with a slight overshoot.
        SetAlpha(scrim, .6f * EaseOutCubic(t / .3f));
        float p = Mathf.Clamp01(tq / PanelIn);
        panelGroup.alpha = EaseOutCubic(t / .2f);
        panel.localScale = Vector3.one * (fitScale * Mathf.LerpUnclamped(.86f, 1f, EaseOutBack(p)));

        var d = divider.rectTransform.localScale;
        d.x = EaseOutCubic((tq - .14f) / .32f);
        divider.rectTransform.localScale = d;

        // Title slab: snaps in from the left, overshoots, settles.
        if (slab != null)
        {
            float sp = Progress(tq, .08f, .34f);
            slab.rectTransform.localScale = new Vector3(EaseOutBack(sp), sp <= 0f ? 0f : 1f, 1f);
        }

        for (int i = 0; i < CardCount; i++)
        {
            float c = EaseOutCubic((t - (CardStart + i * CardStagger)) / CardDuration);
            cardGroups[i].alpha = c;
            float cq = EaseOutBack(Progress(tq, CardStart + i * CardStagger, CardStart + i * CardStagger + CardDuration));
            cards[i].anchoredPosition = Shifted(CardRects[i].center) + new Vector2((1f - cq) * 48f, 0f);
        }

        long score = CountUp(results.score, Progress(t, ScoreCountFrom, ScoreCountTo));
        if (score != shownScore) { shownScore = score; scoreValue.text = RunScore.Format(score); }
        for (int i = 0; i < rowValues.Length; i++)
        {
            float from = RowCountFrom + i * RowStagger;
            long v = CountUp(rowPoints[i], Progress(t, from, from + RowCountDuration));
            if (v == shownRows[i]) continue;
            shownRows[i] = v;
            rowValues[i].text = rowPoints[i] > 0 ? "+" + RunScore.Format(v) : "0";
        }
        float bonus = Mathf.Max(0f, results.dustBonus);
        float bonusIn = bonus * EaseOutCubic(Progress(t, BonusCountFrom, BonusCountTo));
        float won = Mathf.Max(0f, results.dustWon - bonus) * EaseOutCubic(Progress(t, DustCountFrom, DustCountTo))
                    + (results.dustWon >= bonus ? bonusIn : 0f);
        if (bonusLine != null)
        {
            int bc = Mathf.RoundToInt(bonusIn * 100f);
            if (bc != shownBonusCents) { shownBonusCents = bc; bonusLine.text = ScoreBonusLabel(bc / 100f); }
            AnimateBurst(bonusBurst, Progress(t, BonusBurstAt, IntroDuration), bonusCentre, .8f);
            float pp = Progress(t, BonusBurstAt, IntroDuration);
            bool live = pp > 0f && pp < 1f;
            if (bonusPopup.gameObject.activeSelf != live) bonusPopup.gameObject.SetActive(live);
            if (live)
            {
                bonusPopup.rectTransform.anchoredPosition = bonusCentre + new Vector2(0f, 20f + 46f * EaseOutCubic(pp));
                float pop = pp < .2f ? 1.4f : pp < .35f ? .92f : 1f;
                bonusPopup.rectTransform.localScale = Vector3.one * pop;
                var pc = bonusPopup.color; pc.a = pp < .7f ? 1f : .5f; bonusPopup.color = pc;
            }
        }
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

        for (int i = 0; i < ButtonCount; i++)
        {
            float b = EaseOutCubic((t - (ButtonsStart + i * ButtonStagger)) / ButtonDuration);
            buttonGroups[i].alpha = b;
            buttonGroups[i].interactable = b > .5f;
            float bq = EaseOutBack(Progress(tq, ButtonsStart + i * ButtonStagger, ButtonsStart + i * ButtonStagger + ButtonDuration));
            buttonSlots[i].anchoredPosition = ButtonRect(i).center + new Vector2(0f, (1f - bq) * -24f);
        }
    }

    void AnimateIdle(float t)
    {
        float settle = Mathf.Clamp01((t - ButtonsStart) / .5f);
        for (int i = 0; i < ButtonCount; i++)
        {
            // A rim flash on a beat (2 ticks bright, 2 ticks half), the two
            // buttons a half-beat apart, instead of a soft breathing bloom.
            float pressed = presses[i] != null ? presses[i].Pressed01 : 0f;
            SetAlpha(buttonGlows[i], settle * BeatFlash(t + i * 1.2f, 2.4f) + .6f * pressed);
        }

        // Header sparkles twinkle on 3s: rest, big, small, rest.
        int step = Mathf.FloorToInt(t * 8f) % 12;
        float twinkle = step == 9 ? 1.25f : step == 10 ? .8f : 1f;
        if (headerSparkles[0] != null) headerSparkles[0].localScale = Vector3.one * twinkle;
        int step2 = (step + 6) % 12;
        float twinkle2 = step2 == 9 ? 1.25f : step2 == 10 ? .8f : 1f;
        if (headerSparkles[1] != null) headerSparkles[1].localScale = Vector3.one * twinkle2;

        if (bonusLine != null)
        {
            // The bonus line punches as it lands (stepped: big, small, rest).
            float k = (t - BonusBurstAt) * 24f;
            float s = k < 0f ? 1f : k < 1f ? 1.25f : k < 3f ? .95f : 1f;
            bonusLine.rectTransform.localScale = new Vector3(s, s, 1f);
        }

        if (bestGlow != null)
        {
            // A strong pulse on the new record, then a slow breathing glow.
            float pulse = Progress(t, NewBestAt, NewBestAt + .5f);
            float flash = pulse > 0f && pulse < 1f ? Mathf.Sin(pulse * Mathf.PI) * .5f : 0f;
            float idle = pulse >= 1f ? .1f + .5f * BeatFlash(t, 1.6f) : 0f;
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
            ((RectTransform)go.transform).GetWorldCorners(corners);   // overlay canvas: screen pixels
            foreach (var c in corners)
            {
                minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x);
                minY = Mathf.Min(minY, c.y); maxY = Mathf.Max(maxY, c.y);
            }
        }
        // The score read-out shares that top band (pinned top-left by
        // HudStyler, top-aligned with the actions), so the panel drops below
        // it as well rather than covering the final speed / star dust.
        var hud = FindFirstObjectByType<HudStyler>();
        if (hud != null && hud.HudRoot != null && hud.HudRoot.gameObject.activeInHierarchy)
        {
            var hudCanvas = hud.HudRoot.GetComponentInParent<Canvas>();
            Camera cam = hudCanvas != null && hudCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? hudCanvas.worldCamera : null;
            hud.HudRoot.GetWorldCorners(corners);
            foreach (var w in corners)
            {
                Vector2 c = RectTransformUtility.WorldToScreenPoint(cam, w);
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
        PadButtonTouch(buttonSlots, fitScale, ButtonHeight);
        SplitRowGap(fitScale);
    }

    // REPLAY / MENU sit a 16-unit gap above SPACE DOCK. On a short screen the
    // panel shrinks and PadButtonTouch grows every touch area by more than half
    // that gap, so the two rows' targets would meet in it: each row reaches at
    // most half the gap into it and takes the rest of its pad on its far side
    // (same total touch height, no overlap).
    void SplitRowGap(float fitScale)
    {
        float pad = Mathf.Max(0f, (UiScaleFloor.SceneTapUnits / Mathf.Max(fitScale, .1f) - ButtonHeight) * .5f);
        float half = ((ReplayY - ButtonHeight * .5f) - (DockY + ButtonHeight * .5f)) * .5f;
        if (pad <= half) return;
        float far = 2f * pad - half;
        for (int i = 0; i < ButtonCount; i++)
        {
            var button = buttonSlots[i] != null ? buttonSlots[i].GetComponentInChildren<Button>(true) : null;
            var g = button != null ? button.targetGraphic : null;
            if (g == null) continue;
            // raycastPadding: (left, bottom, right, top); negative grows the area
            g.raycastPadding = i < 2 ? new Vector4(0f, -half, 0f, -far) : new Vector4(0f, -far, 0f, -half);
        }
    }

    // The panel scales down to fit a short canvas (ComputeFit); UiScale's
    // floor keeps the canvas at >= 96 units per 48 dp, so the buttons'
    // touch areas reach past their art by whatever the scale took off them.
    public static void PadButtonTouch(RectTransform[] slots, float fitScale, float buttonHeight)
    {
        float pad = Mathf.Max(0f, (UiScaleFloor.SceneTapUnits / Mathf.Max(fitScale, .1f) - buttonHeight) * .5f);
        foreach (var slot in slots)
        {
            var button = slot != null ? slot.GetComponentInChildren<Button>(true) : null;
            var g = button != null ? button.targetGraphic : null;
            if (g != null) g.raycastPadding = new Vector4(0f, -pad, 0f, -pad);
        }
    }


    // Pure, so it can be tested for any screen: places the panel (including
    // its glow) inside `safe` with a small margin, never above full size, and
    // drops it below `blocker` (the top band: the top-right quick actions and
    // the top-left score read-out) if they would otherwise overlap.
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

    // The death buttons use the same icon set as the top-right quick actions
    // (PauseQuickActions.ReplayIconPath / HomeIconPath), in the glyph-only
    // variant: the full quick-action tiles carry their own plate and rim,
    // which would read as a box inside this panel's button frame. Both are
    // rendered from Art/UI/Icons/src~ (render.sh / render.sh --glyph).
    public const string GlyphSuffix = "_glyph";

    static Sprite QuickActionGlyph(string iconPath)
    {
        return Resources.Load<Sprite>(iconPath + GlyphSuffix);
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

    // dp_button's body fill is a flat (44,51,70); the tutorial and offer cards
    // share the sprite and keep it solid, so the death buttons get their own
    // copy with a less opaque fill. The ink rim and the accent slab stay solid.
    public const float ButtonFillAlpha = .62f;
    static Sprite translucentButton, translucentSource;

    static Sprite TranslucentButtonSprite(Sprite source)
    {
        if (source == null) return null;
        if (translucentButton != null && translucentSource == source) return translucentButton;
        var tex = source.texture;
        var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var prev = RenderTexture.active;
        Graphics.Blit(tex, rt);
        RenderTexture.active = rt;
        var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        var px = copy.GetPixels32();
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            if (c.a == 255 && Mathf.Abs(c.r - 44) <= 2 && Mathf.Abs(c.g - 51) <= 2 && Mathf.Abs(c.b - 70) <= 2)
            {
                c.a = (byte)Mathf.RoundToInt(255f * ButtonFillAlpha);
                px[i] = c;
            }
        }
        copy.SetPixels32(px);
        copy.Apply(false, false);
        copy.wrapMode = TextureWrapMode.Clamp;
        copy.filterMode = source.texture.filterMode;
        copy.name = "dp_button_translucent";
        translucentSource = source;
        translucentButton = Sprite.Create(copy, new Rect(0, 0, copy.width, copy.height), new Vector2(.5f, .5f),
                                          source.pixelsPerUnit, 0, SpriteMeshType.FullRect, source.border);
        translucentButton.name = "dp_button";
        return translucentButton;
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

    // 0 most of the time; a hard two-step flash once per period (seconds).
    static float BeatFlash(float t, float period)
    {
        float k = Mathf.Repeat(t, period) * 24f;
        return k < 2f ? .7f : k < 4f ? .35f : .08f;
    }

    // Hold each pose two 24 fps ticks; the settled end time stays exact.
    static float OnTwos(float t) { return t >= IntroDuration ? t : Mathf.Floor(t * 12f) / 12f; }

    static float Progress(float t, float from, float to) { return Mathf.Clamp01((t - from) / (to - from)); }
    static long CountUp(long target, float p) { return p >= 1f ? target : (long)System.Math.Round(target * (double)EaseOutCubic(p)); }
    static float EaseOutCubic(float x) { x = Mathf.Clamp01(x); float i = 1f - x; return 1f - i * i * i; }
    static float EaseOutBack(float x)
    {
        x = Mathf.Clamp01(x);
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float m = x - 1f;
        return 1f + c3 * m * m * m + c1 * m * m;
    }
}
