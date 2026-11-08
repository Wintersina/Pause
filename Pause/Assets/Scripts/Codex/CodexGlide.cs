using UnityEngine;

// A smooth (unscaled time) scroll of a list's content to a y, shared by the codex lists that are not the card grid.
public sealed class CodexGlide
{
    public const float Duration = .45f;
    float from, to, at, lastSet;
    bool running;

    public bool Running { get { return running; } }

    // Content y that puts a row whose top edge is rowTop at the top of the list, kept inside it.
    public static float TargetY(float rowTop, float inset, float maxScroll)
    {
        return Mathf.Clamp(rowTop - inset, 0f, Mathf.Max(0f, maxScroll));
    }

    public void Start(RectTransform content, float y)
    {
        from = lastSet = content.anchoredPosition.y;
        to = y;
        at = Time.unscaledTime;
        running = true;
    }

    public void Tick(RectTransform content)
    {
        if (!running) return;
        // The player grabbed the list mid-glide: let go of it.
        if (Mathf.Abs(content.anchoredPosition.y - lastSet) > .5f) { running = false; return; }
        float t = (Time.unscaledTime - at) / Duration;
        lastSet = Mathf.LerpUnclamped(from, to, CodexUi.EaseOutCubic(t));
        var p = content.anchoredPosition;
        p.y = lastSet;
        content.anchoredPosition = p;
        if (t >= 1f) running = false;
    }
}
