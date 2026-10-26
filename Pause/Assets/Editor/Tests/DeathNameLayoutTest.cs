using UnityEngine;

// The KILLED BY / HURT BY card sizing (DeathNameLayout): padding is applied, short names keep the
// base look, long ones shrink then wrap and grow the card, never past the clamp, and every real codex
// name fits on one line. Pure logic: widths are estimated at a conservative 0.70 em per bold capital.
public static class DeathNameLayoutTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[DNL] PASS  " : "[DNL] FAIL  ") + what);
        if (!ok) fails++;
    }

    const float Em = 0.70f;
    static float Width(string s, int font) { return s.Length * font * Em; }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        float inner = DeathNameLayout.InnerWidth(DeathPanelView.CardWidth);
        Check("inner width = card minus both paddings", Mathf.Approximately(inner, DeathPanelView.CardWidth - DeathNameLayout.PadLeft - DeathNameLayout.PadRight));
        Check("a degenerate card still has a positive inner width", DeathNameLayout.InnerWidth(10f) >= 1f);

        var shortR = DeathNameLayout.Compute(Width("KILLED BY  MINE", 24), 24, 18, Width("HURT BY  NO OTHER HITS", 19), 19, 18, inner);
        Check("short names keep nominal fonts and one line each", shortR.killedFont == 24 && shortR.hurtFont == 19 && shortR.killedLines == 1 && shortR.hurtLines == 1);
        Check("short names keep the base card height", Mathf.Approximately(shortR.cardHeight, DeathNameLayout.MinHeight));
        Check("short block has vertical padding on both sides",
            shortR.killedHeight + shortR.hurtHeight + DeathNameLayout.LineGap + 2f * DeathNameLayout.PadV <= shortR.cardHeight + .001f);

        // widths that overflow at nominal shrink, but never below the minimum, and then fit
        int f, lines;
        DeathNameLayout.FitLine(inner * 1.2f, 24, 18, inner, out f, out lines);
        Check("slightly long line shrinks and stays on one line", f < 24 && f >= 18 && lines == 1);
        DeathNameLayout.FitLine(inner * 1.2f, 24, 18, inner, out f, out lines);
        Check("shrunk width fits the inner width", inner * 1.2f * f / 24 <= inner + .001f);
        DeathNameLayout.FitLine(inner * 3f, 24, 18, inner, out f, out lines);
        Check("far too long: min font and a wrap", f == 18 && lines == 2);
        DeathNameLayout.FitLine(0f, 24, 18, inner, out f, out lines);
        Check("empty text: no lines", lines == 0);

        var wrap = DeathNameLayout.Compute(inner * 3f, 24, 18, inner * 3f, 19, 18, inner);
        Check("wrapped lines grow the card", wrap.cardHeight > DeathNameLayout.MinHeight);
        Check("card growth is clamped", wrap.cardHeight <= DeathNameLayout.MaxHeight + .001f);
        var empty = DeathNameLayout.Compute(0f, 24, 18, 0f, 19, 18, inner);
        Check("empty text keeps the base height", Mathf.Approximately(empty.cardHeight, DeathNameLayout.MinHeight));

        // every real codex name (enemies, elites, bosses, hazards, mines...) on the KILLED BY line and
        // as a worst-case HURT BY entry, at the narrowest and widest cards the panel uses
        string longest = ""; int n = 0;
        foreach (var e in CodexCatalogue.All)
        {
            if (string.IsNullOrEmpty(e.name)) continue;
            n++;
            if (e.name.Length > longest.Length) longest = e.name;
            string k = "KILLED BY  " + e.name.ToUpperInvariant();
            var r = DeathNameLayout.Compute(Width(k, 24), 24, 18, Width("HURT BY  " + e.name.ToUpperInvariant() + " X9", 19), 19, 18, inner);
            if (r.killedLines != 1 || r.hurtLines != 1 || r.cardHeight > DeathNameLayout.MinHeight + .001f)
                Check("codex name fits the card with padding: " + e.name, false);
        }
        Check("checked every codex name (" + n + "), longest '" + longest + "'", n > 0);

        // the full HURT BY line (HurtLineChars of names plus the +N more tail) always fits or wraps within the clamp
        string full = "HURT BY  " + new string('W', DeathPanelView.HurtLineChars + 8);
        var fr = DeathNameLayout.Compute(Width("KILLED BY  X", 24), 24, 18, Width(full, 19), 19, 18, inner);
        Check("longest hurt line stays within the card clamp", fr.cardHeight <= DeathNameLayout.MaxHeight + .001f && fr.hurtFont >= 18);

        Debug.Log("[DNL] " + (fails == 0 ? "ALL PASS" : fails + " FAILED"));
        return fails;
    }
}
