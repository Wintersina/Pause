using UnityEngine;

// The in-game popup size rule (ScoreHud.PopupScaleFor / ScaledPopupSize): popups are authored for a 16:9-or-taller
// view, so the user's phone and every phone keep their size, and a squarer view (a 4:3 iPad, a tablet) shrinks them
// with its height-to-width ratio so a popup keeps its share of the view's height. Pure functions of the screen.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod PopupScaleTest.Run
public static class PopupScaleTest
{
    static int fails;

    static void Check(string what, bool ok)
    {
        if (ok) Debug.Log("[PSC] PASS  " + what);
        else { Debug.Log("[PSC] FAIL  " + what); fails++; }
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    // The HUD canvas is 800 units wide on every screen (it matches the width).
    static float CanvasHeight(float w, float h) { return 800f * h / w; }

    public static int Execute()
    {
        fails = 0;
        var screens = new[]
        {
            new Vector2(1080, 2520), new Vector2(1080, 1920), new Vector2(1536, 2048),
            new Vector2(1668, 2388), new Vector2(2048, 2732),
        };
        var names = new[] { "Flip7 1080x2520", "1080x1920", "iPad 1536x2048", "1668x2388", "2048x2732" };

        // ---- phones are untouched ----
        Check("Flip7 (21:9): scale 1", ScoreHud.PopupScaleFor(1080, 2520) == 1f);
        Check("16:9 phone: scale 1", ScoreHud.PopupScaleFor(1080, 1920) == 1f);
        Check("a narrow 9:21 canvas (800x1867): scale 1", ScoreHud.PopupScaleFor(800, CanvasHeight(1080, 2520)) == 1f);
        Check("the old 9:16 phone (720x1280): scale 1", ScoreHud.PopupScaleFor(720, 1280) == 1f);

        // ---- tablets shrink with their shape ----
        float ipad = ScoreHud.PopupScaleFor(1536, 2048);
        Check("iPad 4:3 -> 0.75 (" + ipad + ")", Mathf.Approximately(ipad, .75f));
        float t1668 = ScoreHud.PopupScaleFor(1668, 2388);
        Check("1668x2388 -> between the iPad and a phone (" + t1668 + ")", t1668 > ipad && t1668 < 1f);
        Check("2048x2732 matches the iPad's shape (" + ScoreHud.PopupScaleFor(2048, 2732) + ")",
              Mathf.Abs(ScoreHud.PopupScaleFor(2048, 2732) - ipad) < .001f);
        Check("the scale is the same for pixels and canvas units", Mathf.Approximately(ScoreHud.PopupScaleFor(1536, 2048), ScoreHud.PopupScaleFor(800, CanvasHeight(1536, 2048))));
        Check("a square view is held at the floor", ScoreHud.PopupScaleFor(1000, 1000) == ScoreHud.PopupMinScale);
        Check("a landscape view is held at the floor", ScoreHud.PopupScaleFor(2732, 2048) == ScoreHud.PopupMinScale);
        Check("never under the floor, never over 1", ScoreHud.PopupScaleFor(100, 1) == ScoreHud.PopupMinScale && ScoreHud.PopupScaleFor(1, 100) == 1f);
        Check("degenerate sizes give 1", ScoreHud.PopupScaleFor(0, 0) == 1f && ScoreHud.PopupScaleFor(float.NaN, 5) == 1f && ScoreHud.PopupScaleFor(5, -3) == 1f);

        // ---- monotonic: a squarer view never gets a bigger popup ----
        float last = 1f;
        bool mono = true;
        for (float ratio = 2.4f; ratio >= .6f; ratio -= .05f)
        {
            float s = ScoreHud.PopupScaleFor(1000, 1000 * ratio);
            mono &= s <= last + 1e-5f;
            last = s;
        }
        Check("the scale never grows as the view gets squarer", mono);

        // ---- the fusion callouts, per screen ----
        int[] sizes = { FusionTuning.CalloutSize, FusionTuning.DiscoverySize };
        for (int i = 0; i < screens.Length; i++)
        {
            float w = screens[i].x, h = screens[i].y, scale = ScoreHud.PopupScaleFor(w, h);
            float canvasH = CanvasHeight(w, h);
            foreach (int size in sizes)
            {
                int px = ScoreHud.ScaledPopupSize(size, scale, 0f);
                float shareOfHeight = px / canvasH;
                Debug.Log("[PSC] " + names[i] + ": scale " + scale.ToString("F2") + ", font " + size + " -> " + px + " units, " +
                          (shareOfHeight * 100f).ToString("F2") + "% of the view's height");
                Check(names[i] + " " + size + ": never bigger than authored", px <= size);
                // a tablet's popup takes no bigger a share of the view's height than 16:9's (the biggest share a phone has)
                Check(names[i] + " " + size + ": share of height <= a 16:9 phone's", shareOfHeight <= size / CanvasHeight(1080, 1920) + .0006f);
            }
        }

        // ---- size floor ----
        Check("floor lifts a too-small size to the legible minimum", ScoreHud.ScaledPopupSize(22, .6f, 16f) == 16);
        Check("... but never above the authored size", ScoreHud.ScaledPopupSize(14, .6f, 16f) == 14);
        Check("no floor: plain scaling", ScoreHud.ScaledPopupSize(40, .75f, 0f) == 30);
        Check("scale 1 returns the authored size", ScoreHud.ScaledPopupSize(46, 1f, 0f) == 46);

        Debug.Log("[PSC] failures: " + fails);
        return fails;
    }
}
