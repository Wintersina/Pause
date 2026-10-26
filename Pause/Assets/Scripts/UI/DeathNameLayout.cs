using UnityEngine;

// Sizing for the Flight Complete KILLED BY / HURT BY card. Pure: the caller measures each line's
// width at its nominal font size and this decides the font size, wrap and card height so the text
// always keeps a consistent inner padding. A line first shrinks toward its minimum font size; only a
// line still too wide at the minimum wraps to a second line, and the card grows (up to MaxHeight)
// to hold it. Short names keep the original look (nominal size, base card height).
public static class DeathNameLayout
{
    public const float PadLeft = 40f, PadRight = 40f;   // card edge to text (left clears the accent bar)
    public const float PadV = 6f, LineGap = 3f;
    public const float LineHeightFactor = 1.05f;
    public const float MinHeight = 64f, MaxHeight = 84f;

    public struct Result
    {
        public int killedFont, hurtFont;
        public int killedLines, hurtLines;   // 0 for an empty line
        public float cardHeight;
        public float killedHeight, hurtHeight;
    }

    public static float InnerWidth(float cardWidth) { return Mathf.Max(1f, cardWidth - PadLeft - PadRight); }

    // Shrinks `nominal` toward `min` until measuredAtNominal fits `maxWidth`; lines = 2 when even `min` does not.
    public static void FitLine(float measuredAtNominal, int nominal, int min, float maxWidth, out int font, out int lines)
    {
        if (measuredAtNominal <= 0f) { font = nominal; lines = 0; return; }
        if (measuredAtNominal <= maxWidth) { font = nominal; lines = 1; return; }
        font = Mathf.Clamp(Mathf.FloorToInt(nominal * maxWidth / measuredAtNominal), min, nominal);
        lines = measuredAtNominal * font / nominal <= maxWidth ? 1 : 2;
    }

    public static Result Compute(float killedWidth, int killedNominal, int killedMin,
                                 float hurtWidth, int hurtNominal, int hurtMin, float innerWidth)
    {
        var r = new Result();
        FitLine(killedWidth, killedNominal, killedMin, innerWidth, out r.killedFont, out r.killedLines);
        FitLine(hurtWidth, hurtNominal, hurtMin, innerWidth, out r.hurtFont, out r.hurtLines);
        r.killedHeight = r.killedLines * r.killedFont * LineHeightFactor;
        r.hurtHeight = r.hurtLines * r.hurtFont * LineHeightFactor;
        float gap = r.killedLines > 0 && r.hurtLines > 0 ? LineGap : 0f;
        float content = r.killedHeight + r.hurtHeight + gap + 2f * PadV;
        r.cardHeight = Mathf.Clamp(content, MinHeight, MaxHeight);
        return r;
    }
}
