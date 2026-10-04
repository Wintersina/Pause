using UnityEngine;
using UnityEngine.UI;

// The HUD half of the resume slow-mo indicator (see ResumeFx): while
// ResumeSlowMo is winding the world back up, the SPEED row of the read-out
// reads "SLOW-MO n" in MAGENTA, where n is the effective speed (HUD speed x
// the time factor) counting back up, and a spool bar along the bottom of the
// row fills from 60% to 100%. When the wind-up completes the row punches back
// to "SPEED n" in CYAN.
//
// Everything stays inside the SPEED row's own rect (the bar is anchored to
// its bottom edge exactly like HudStyler's pause bar), so it cannot overlap
// any other HUD element, the hearts or the quick actions at any screen size,
// and nothing here is a raycast target.
//
// Runs in LateUpdate, after HudStyler's Update has written "SPEED n" and
// after moveBackGround decided this frame's time scale, so a re-pause snaps
// the row back on the very frame it happens. Label strings are cached:
// no per-frame allocation.
public class ResumeSpeedRow : MonoBehaviour
{
    public const string SlowLabel = "SLOW-MO";
    public const string SpeedLabelPrefix = "SPEED  ";
    public const string BarName = "ResumeSpoolBar";
    const string MeterSprite = "Hud/hud_meter";
    const int CachedSpeeds = 1000;

    static readonly string[] speedLabels = new string[CachedSpeeds];
    static readonly string[] slowLabels = new string[CachedSpeeds];

    Text text;
    Image bar;
    Color idleColour;
    bool wasShowing;
    float punchAt = -1f;
    float lastFactor = 1f;

    public Image Bar => bar;
    public bool Showing => wasShowing;

    public static ResumeSpeedRow Attach(Text speedText)
    {
        if (speedText == null) return null;
        var row = speedText.GetComponent<ResumeSpeedRow>();
        if (row == null) row = speedText.gameObject.AddComponent<ResumeSpeedRow>();
        row.Init(speedText);
        return row;
    }

    // "SPEED  23", cached per value.
    public static string SpeedLabel(int hudSpeed)
    {
        if (hudSpeed < 0 || hudSpeed >= CachedSpeeds) return SpeedLabelPrefix + hudSpeed;
        return speedLabels[hudSpeed] ?? (speedLabels[hudSpeed] = SpeedLabelPrefix + hudSpeed);
    }

    // "SLOW-MO  14", cached per value.
    public static string SlowMoLabel(int shownSpeed)
    {
        if (shownSpeed < 0 || shownSpeed >= CachedSpeeds) return SlowLabel + "  " + shownSpeed;
        return slowLabels[shownSpeed] ?? (slowLabels[shownSpeed] = SlowLabel + "  " + shownSpeed);
    }

    public static int HudSpeed => Mathf.RoundToInt(moveBackGround.speed * 100f);

    // The speed the world is effectively moving at: HUD speed x time factor.
    public static int ShownSpeed(int hudSpeed, float factor)
    {
        return Mathf.RoundToInt(hudSpeed * factor);
    }

    void Init(Text speedText)
    {
        text = speedText;
        idleColour = text.color;
        if (bar == null)
        {
            var existing = text.transform.Find(BarName);
            bar = existing != null ? existing.GetComponent<Image>() : BuildBar(text.transform);
        }
        bar.enabled = false;
    }

    static Image BuildBar(Transform row)
    {
        var holder = new GameObject(BarName, typeof(RectTransform), typeof(Image));
        holder.transform.SetParent(row, false);
        var rt = holder.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0.16f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var img = holder.GetComponent<Image>();
        img.sprite = Resources.Load<Sprite>(MeterSprite);
        img.raycastTarget = false;
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.enabled = false;
        return img;
    }

    void LateUpdate()
    {
        Refresh(ResumeFx.Factor, Time.unscaledTime);
    }

    public void Refresh(float factor, float now)
    {
        if (text == null) return;
        float intensity = ResumeFx.IntensityFor(factor);
        bool showing = intensity > 0f;

        if (showing)
        {
            if (!wasShowing) idleColour = text.color;
            Set(SlowMoLabel(ShownSpeed(HudSpeed, factor)));
            SetColour(Color.Lerp(idleColour, ResumeFx.Slow, Mathf.Clamp01(intensity * 1.5f)));
            if (bar != null)
            {
                bar.enabled = true;
                bar.fillAmount = factor;
                Color b = Color.Lerp(ResumeFx.Fast, ResumeFx.Slow, intensity);
                if (bar.color != b) bar.color = b;
            }
            punchAt = -1f;
            lastFactor = factor;
        }
        else if (wasShowing)
        {
            // Back up to speed (or snapped off by a re-pause): plain row again.
            Set(SpeedLabel(HudSpeed));
            SetColour(idleColour);
            if (bar != null) bar.enabled = false;
            // Only a completed wind-up earns the "back to speed" punch.
            // A re-pause, death or the cinematic cuts it off mid-curve instead.
            if (lastFactor >= 0.97f && Time.timeScale > 0f) punchAt = now;
        }
        wasShowing = showing;
        Punch(now);
    }

    void Set(string s)
    {
        if (!ReferenceEquals(text.text, s) && text.text != s) text.text = s;
    }

    void SetColour(Color c)
    {
        if (text.color != c) text.color = c;
    }

    // Stepped punch like HudStyler's: 1 tick big, 2 ticks small, then rest.
    void Punch(float now)
    {
        if (punchAt < 0f) return;
        float k = (now - punchAt) * 24f;
        float s = k < 1f ? 1.18f : k < 3f ? .94f : 1f;
        var rt = text.rectTransform;
        if (!Mathf.Approximately(rt.localScale.x, s)) rt.localScale = new Vector3(s, s, 1f);
        if (k >= 3f) punchAt = -1f;
    }
}
