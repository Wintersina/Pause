using UnityEngine;
using UnityEngine.UI;

// The HUD cue for the max-speed streak (ScoreMultiplier): hold the speed cap
// without losing a heart and the score doubles. It lives on the SCORE row,
// because it is the score that doubles.
//
//   hidden    nothing, until the streak has run CueShowAfterSeconds (2 s), so
//             it never nags a pilot who is only passing through the cap
//   charging  a slim cyan bar along the bottom of the SCORE row (the same strip
//             and meter sprite as the pause bar and the resume spool bar)
//             fills over the 15 s, in half-second steps
//   active    a small "x2" plate hugging the right edge of the score figure
//             ("SCORE  12,345 [x2]"); it follows the figure's rendered width as
//             digits are added, and pops in on a stepped punch with a one-tick
//             BONE flash
//   lost      the plate dims to MUTED and sinks a hair, in three steps, for
//             LossCueSeconds, then goes -- no sound, no red (red is the pilot)
//
// Everything lives inside the SCORE row's own rect (the plate is clamped to it,
// left of the kill-chain badge's column), so it cannot touch the hearts, the
// shield timer, the SPEED row or the quick actions at any screen size, and the
// panel is already placed inside the safe area (HudStyler). The bar yields to
// the resume slow-mo spool bar, which uses the same strip. Runs on unscaled
// time (the HUD lives through a freeze) and allocates nothing per frame (the
// plate is re-measured only when the score string changes).
public class ScoreX2Cue : MonoBehaviour
{
    public enum CueState { Hidden, Charging, Active, Lost }

    public const string BackName = "X2StreakBack";
    public const string BarName = "X2StreakBar";
    public const string PlateName = "X2Badge";
    public const string Label = "x2";
    const string MeterSprite = "Hud/hud_meter";

    // The plate: its size, and the gap between the score figure's last digit
    // and the plate's left edge (the figure's outline reaches ~2 units).
    public const float PlateWidth = 32f;
    public const float PlateHeight = 18f;
    public const float PlateGap = 6f;
    public const int LabelFontSize = 16;

    static readonly Color BarColour = AkiraPalette.Cyan;
    static readonly Color BackColour = AkiraPalette.WithAlpha(AkiraPalette.Indigo1, .9f);
    static readonly Color PlateColour = AkiraPalette.Teal;
    static readonly Color FlashColour = AkiraPalette.Bone;
    static readonly Color InkColour = AkiraPalette.Ink;
    static readonly Color LostColour = AkiraPalette.Muted;

    Text scoreText, chainText;
    string measuredFor, measuredChain;
    float plateX;
    Image back, bar, plate;
    Text label;
    int seenStarted, seenLost;
    float appearAt = -1f, lostAt = -1f;
    CueState state = CueState.Hidden;

    public Image Back { get { return back; } }
    public Image Bar { get { return bar; } }
    public Image Plate { get { return plate; } }
    public Text LabelText { get { return label; } }
    public CueState State { get { return state; } }

    public static ScoreX2Cue Attach(Text scoreText)
    {
        if (scoreText == null) return null;
        var cue = scoreText.GetComponent<ScoreX2Cue>() ?? scoreText.gameObject.AddComponent<ScoreX2Cue>();
        cue.Init(scoreText);
        return cue;
    }

    // ---- pure rules (tested) ----

    // What the cue shows. `sinceLost`: unscaled seconds since a streak that
    // had reached x2 ended (large when none did).
    public static CueState StateFor(float streakSeconds, bool active, float sinceLost)
    {
        if (active) return CueState.Active;
        if (sinceLost >= 0f && sinceLost < ScoreMultiplier.LossCueSeconds) return CueState.Lost;
        return streakSeconds >= ScoreMultiplier.CueShowAfterSeconds ? CueState.Charging : CueState.Hidden;
    }

    public const float NoRoom = -1f;

    // Where the plate's left edge sits on the row: just past the score figure
    // (`textWidth`, its rendered width), in whole units for crisp pixels.
    // `rightReserve` is what the kill-chain badge takes on the right while a
    // chain is alive (0 when it is empty). NoRoom when the plate would not fit
    // between the two (a 7-digit score under a live chain): the plate waits
    // rather than cover a digit or the chain multiplier.
    public static float PlateLeftFor(float textWidth, float rowWidth, float rightReserve)
    {
        float x = Mathf.Round(Mathf.Max(0f, textWidth) + PlateGap);
        return x + PlateWidth <= rowWidth - rightReserve ? x : NoRoom;
    }

    // The bar's fill: the streak in CueStepSeconds steps, 0..1.
    public static float FillFor(float streakSeconds)
    {
        float step = Mathf.Max(.01f, ScoreMultiplier.CueStepSeconds);
        float whole = Mathf.Floor(Mathf.Max(0f, streakSeconds) / step) * step;
        return Mathf.Clamp01(whole / ScoreMultiplier.StreakSeconds);
    }

    // The lost plate's three steps: 0 .. 1/3 .. 2/3 .. 1 of LossCueSeconds.
    public static float LostAlpha(float sinceLost)
    {
        float q = sinceLost / Mathf.Max(.01f, ScoreMultiplier.LossCueSeconds);
        return q < .34f ? .85f : q < .67f ? .5f : .25f;
    }

    // ---- build ----

    void Init(Text score)
    {
        scoreText = score;
        var chain = score.transform.Find(ScoreHud.ChainName);
        chainText = chain != null ? chain.GetComponent<Text>() : null;
        if (plate != null) return;
        var rowRect = score.transform;

        var existingBack = rowRect.Find(BackName);
        if (existingBack != null)
        {
            back = existingBack.GetComponent<Image>();
            bar = rowRect.Find(BarName).GetComponent<Image>();
            plate = rowRect.Find(PlateName).GetComponent<Image>();
            label = plate.transform.GetChild(0).GetComponent<Text>();
        }
        else
        {
            back = BuildBar(rowRect, BackName);
            bar = BuildBar(rowRect, BarName);
            plate = BuildPlate(rowRect, score.font, out label);
        }
        back.color = BackColour;
        bar.color = BarColour;
        SetShown(CueState.Hidden);
        seenStarted = ScoreMultiplier.StartedCount;
        seenLost = ScoreMultiplier.LostCount;
    }

    static Image BuildBar(Transform row, string name)
    {
        var holder = new GameObject(name, typeof(RectTransform), typeof(Image));
        holder.transform.SetParent(row, false);
        var rt = (RectTransform)holder.transform;
        // the strip HudStyler's pause bar and the resume spool bar use
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, .16f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var img = holder.GetComponent<Image>();
        img.sprite = Resources.Load<Sprite>(MeterSprite);
        img.raycastTarget = false;
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillAmount = name == BackName ? 1f : 0f;
        return img;
    }

    static Image BuildPlate(Transform row, Font font, out Text text)
    {
        var go = new GameObject(PlateName, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(row, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, .5f);
        rt.pivot = new Vector2(0f, .5f);
        rt.sizeDelta = new Vector2(PlateWidth, PlateHeight);
        rt.anchoredPosition = Vector2.zero;
        var img = go.GetComponent<Image>();   // a flat rect: crisp pixel edges
        img.color = PlateColour;
        img.raycastTarget = false;

        var t = new GameObject("Label", typeof(RectTransform), typeof(Text));
        t.transform.SetParent(go.transform, false);
        var lrt = (RectTransform)t.transform;
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        text = t.GetComponent<Text>();
        if (font != null) text.font = font;
        text.fontSize = LabelFontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.resizeTextForBestFit = false;
        text.raycastTarget = false;
        text.color = InkColour;
        text.text = Label;
        return img;
    }

    // ---- per frame ----

    void LateUpdate() { Refresh(Time.unscaledTime); }

    public void Refresh(float now)
    {
        if (plate == null) return;
        int started = ScoreMultiplier.StartedCount, lost = ScoreMultiplier.LostCount;
        if (started != seenStarted)
        {
            seenStarted = started;
            if (ScoreMultiplier.Active) appearAt = now;
        }
        if (lost != seenLost)
        {
            seenLost = lost;
            lostAt = now;
        }

        float sinceLost = lostAt < 0f ? -1f : now - lostAt;
        var next = StateFor(ScoreMultiplier.Streak, ScoreMultiplier.Active, sinceLost);
        if (!Live) next = CueState.Hidden;
        if (next != state) SetShown(next);
        state = next;

        var rt = plate.rectTransform;
        if (state == CueState.Active || state == CueState.Lost) Measure();
        switch (state)
        {
            case CueState.Charging:
                {
                    // the resume slow-mo's spool bar owns the strip while it runs
                    bool yield = ResumeFx.IntensityFor(ResumeFx.Factor) > 0f;
                    if (back.enabled == yield) { back.enabled = !yield; bar.enabled = !yield; }
                    float fill = FillFor(ScoreMultiplier.Streak);
                    if (!Mathf.Approximately(bar.fillAmount, fill)) bar.fillAmount = fill;
                    break;
                }
            case CueState.Active:
                {
                    float since = appearAt < 0f ? 99f : now - appearAt;
                    float k = since * 24f;
                    // soft stepped pop: small, a touch big, settled; a one-tick BONE flash
                    float s = k < 1f ? .7f : k < 3f ? 1.15f : 1f;
                    SetScale(rt, s);
                    SetColour(plate, k < 1f ? FlashColour : PlateColour);
                    rt.anchoredPosition = new Vector2(Mathf.Max(0f, plateX), 0f);
                    break;
                }
            case CueState.Lost:
                {
                    SetScale(rt, .9f);
                    SetColour(plate, AkiraPalette.WithAlpha(LostColour, LostAlpha(sinceLost)));
                    rt.anchoredPosition = new Vector2(Mathf.Max(0f, plateX), -2f);
                    break;
                }
        }
    }

    public float PlateX { get { return plateX; } }

    // Measures now (the plate's x is also kept up to date in Refresh).
    public void Remeasure()
    {
        measuredFor = null; measuredChain = null;
        if (scoreText != null) Measure();
    }

    // Re-measures the score figure only when its string changed.
    void Measure()
    {
        string now = scoreText.text;
        string chain = chainText != null ? chainText.text : "";
        if (ReferenceEquals(now, measuredFor) && ReferenceEquals(chain, measuredChain)) return;
        if (scoreText.rectTransform.rect.width <= 0f) return;   // not laid out yet: try next frame
        measuredFor = now;
        measuredChain = chain;
        float reserve = string.IsNullOrEmpty(chain) ? 0f : chainText.preferredWidth + 3f;
        plateX = PlateLeftFor(scoreText.preferredWidth, scoreText.rectTransform.rect.width, reserve);
        bool room = plateX >= 0f;
        if (plate.enabled != room && (state == CueState.Active || state == CueState.Lost))
        {
            plate.enabled = room;
            label.enabled = room;
        }
    }

    // Only a live, scoring run shows the cue (the tutorial and the death
    // panel's frames do not).
    static bool Live { get { return RunScore.Scoring && !RunScore.Ended; } }

    void SetShown(CueState s)
    {
        measuredFor = null; measuredChain = null;   // re-measure the figure as the plate comes up
        bool bars = s == CueState.Charging;
        bool badge = s == CueState.Active || s == CueState.Lost;
        back.enabled = bars;
        bar.enabled = bars;
        plate.enabled = badge;
        label.enabled = badge;
        if (s == CueState.Active)
        {
            label.color = InkColour;
        }
        else if (s == CueState.Lost)
        {
            label.color = AkiraPalette.WithAlpha(InkColour, .8f);
        }
    }

    static void SetScale(RectTransform rt, float s)
    {
        if (!Mathf.Approximately(rt.localScale.x, s)) rt.localScale = new Vector3(s, s, 1f);
    }

    static void SetColour(Image img, Color c)
    {
        if (img.color != c) img.color = c;
    }
}
