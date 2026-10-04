using UnityEngine;
using UnityEngine.UI;

// The run score on the in-game read-out, plus the kill-chain badge and the
// "+N" popups. Added by HudStyler in gameS1 only -- the tutorial scores
// nothing, so its read-out stays SPEED / star dust / PAUSES.
//
//   SCORE  12,345  x3     top row of the read-out (BONE, like the other rows'
//                         flat cel type with an INK outline). The number ticks
//                         up towards RunScore.Total instead of jumping, and
//                         punches on a big gain. The chain badge shows the
//                         current kill multiplier while a chain is alive and
//                         fades as its window runs out.
//   +250                  rises from where a kill, a boss or a world clear
//                         happened (RunScore.Scored), on the HUD canvas.
//
// The row is one more slot in the read-out's VerticalLayoutGroup (PanelTexts):
// the stack and the panel both grow by one row + gap, so every row keeps its
// height and the panel keeps its padding. All motion is on unscaled time --
// the HUD lives on through the freeze.
public class ScoreHud : MonoBehaviour
{
    public const string RowName = "ScoreText";
    public const string ChainName = "Chain";
    // One row (33) and the layout group's gap (7) in gameS1's read-out.
    public const float RowStep = 40f;
    public const int FontSize = 26;
    public const int PopupPool = 8;
    public const float PopupSeconds = .8f;

    static readonly Color ScoreColour = AkiraPalette.Bone;
    static readonly Color ChainColour = AkiraPalette.RedHi;
    static readonly Color TextInk = AkiraPalette.WithAlpha(AkiraPalette.Ink, .95f);

    Text scoreText, chainText;
    RectTransform canvasRect;
    Canvas canvas;
    Font font;
    double shown;
    long lastTarget;
    float punchAt = -1f;
    int lastMultiplier = 1;
    float chainPunchAt = -1f;

    struct Popup { public Text text; public float bornAt; public Vector2 from; }
    Popup[] popups;
    int nextPopup;

    public Text ScoreText { get { return scoreText; } }
    public Text ChainText { get { return chainText; } }

    // The read-out scores only outside the tutorial.
    public static bool ShouldShow(Text speedText)
    {
        return speedText != null && speedText.gameObject.scene.name != score.TutorialScene;
    }

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

        var c = rows.GetComponentInParent<Canvas>();
        hud.canvas = c != null ? c.rootCanvas : null;
        hud.canvasRect = hud.canvas != null ? (RectTransform)hud.canvas.transform : null;
        hud.shown = RunScore.Total;
        hud.lastTarget = RunScore.Total;
        return hud;
    }

    static void Grow(RectTransform rt, float by)
    {
        if (rt == null) return;
        var size = rt.sizeDelta;
        size.y += by;
        rt.sizeDelta = size;
    }

    public static string Label(long points)
    {
        return "SCORE  " + RunScore.Format(points);
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

    void OnEnable() { RunScore.Scored += OnScored; }
    void OnDisable() { RunScore.Scored -= OnScored; }

    void Update()
    {
        TickDisplay(Mathf.Min(Time.unscaledDeltaTime, .1f));
    }

    void TickDisplay(float dt)
    {
        if (scoreText == null) return;
        float now = Time.unscaledTime;
        long target = RunScore.Total;

        // Tick up: close most of the gap quickly, never overshoot, and
        // always land on the exact number.
        if (target < shown) shown = target;   // a new run
        if (target - lastTarget >= ScoreRules.PopupMinPoints) punchAt = now;
        lastTarget = target;
        double gap = target - shown;
        if (gap > 0d)
        {
            double step = gap * (1d - System.Math.Exp(-10d * dt)) + 30d * dt;
            shown = System.Math.Min(target, shown + step);
        }
        scoreText.text = Label((long)System.Math.Floor(shown));
        Punch(scoreText.rectTransform, now - punchAt);

        if (chainText != null)
        {
            int m = RunScore.Multiplier;
            if (m > 1)
            {
                if (m != lastMultiplier) chainPunchAt = now;
                chainText.text = "x" + m;
                // Fades out over the last of the chain window, stepped like the
                // rest of the HUD (no smooth fade).
                float left = RunScore.ChainLeft01;
                chainText.color = AkiraPalette.WithAlpha(ChainColour, left > .35f ? 1f : left > .15f ? .6f : .3f);
                Punch(chainText.rectTransform, now - chainPunchAt);
            }
            else if (chainText.text.Length > 0) chainText.text = "";
            lastMultiplier = m;
        }

        StepPopups(now);
    }

    // Stepped punch: 1 tick big, 2 ticks small, then rest (24 fps ticks).
    static void Punch(RectTransform rt, float since)
    {
        float k = since < 0f ? 99f : since * 24f;
        float s = k < 1f ? 1.12f : k < 3f ? .96f : 1f;
        if (!Mathf.Approximately(rt.localScale.x, s)) rt.localScale = new Vector3(s, s, 1f);
    }

    // ---- "+N" popups -------------------------------------------------------

    void OnScored(int points, Vector3 at, RunScore.Source source)
    {
        ShowPopup(points, at, source);
    }

    public Text ShowPopup(int points, Vector3 at, RunScore.Source source)
    {
        if (canvasRect == null || points <= 0) return null;
        if (popups == null) BuildPopups();

        int index = nextPopup;
        nextPopup = (nextPopup + 1) % popups.Length;
        var p = popups[index];
        bool big = source == RunScore.Source.Boss || source == RunScore.Source.World;
        p.text.text = "+" + RunScore.Format(points);
        p.text.fontSize = big ? 40 : 24;
        p.text.color = source == RunScore.Source.Boss ? AkiraPalette.Amber
                     : source == RunScore.Source.World ? AkiraPalette.Cyan : AkiraPalette.Bone;
        p.from = ToCanvas(at);
        p.bornAt = Time.unscaledTime;
        p.text.gameObject.SetActive(true);
        p.text.rectTransform.anchoredPosition = p.from;
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
            rt.sizeDelta = new Vector2(240f, 50f);
            var t = go.GetComponent<Text>();
            Style(t, font, 24, ScoreColour, TextAnchor.MiddleCenter);
            go.SetActive(false);
            popups[i] = new Popup { text = t, bornAt = -1f };
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

    void StepPopups(float now)
    {
        if (popups == null) return;
        for (int i = 0; i < popups.Length; i++)
        {
            var p = popups[i];
            if (p.bornAt < 0f) continue;
            float age = now - p.bornAt;
            if (age >= PopupSeconds)
            {
                p.text.gameObject.SetActive(false);
                p.bornAt = -1f;
                popups[i] = p;
                continue;
            }
            // Rises on twos (12 drawings a second), pops in, fades out late.
            float q = Mathf.Floor(age * 12f) / 12f / PopupSeconds;
            var rt = p.text.rectTransform;
            rt.anchoredPosition = p.from + new Vector2(0f, 56f * (1f - (1f - q) * (1f - q)));
            float s = q < .08f ? 1.25f : 1f;
            rt.localScale = new Vector3(s, s, 1f);
            var c = p.text.color;
            c.a = q < .6f ? 1f : q < .8f ? .6f : .3f;
            p.text.color = c;
        }
    }
}
