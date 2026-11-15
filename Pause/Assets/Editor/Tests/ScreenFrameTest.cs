using UnityEngine;

// ScreenFrame (the shared aspect class + content / side / footer rects) and UiScale's desktop unit.
public static class ScreenFrameTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SFRAME] PASS  " : "[SFRAME] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    // UiScale.PxPerPointFor as it was before the desktop unit: the phone results must be byte-identical.
    static float OldPxPerPoint(float dpi, bool ios, int w, int h)
    {
        if (dpi >= 100f && dpi <= 1000f) return ios ? (dpi >= 380f ? 3f : 2f) : dpi / 160f;
        float shortSide = Mathf.Max(1f, Mathf.Min(w, h)), longSide = Mathf.Max(w, h);
        float widthDp = longSide >= shortSide * 1.7f || shortSide < 1100f ? 320f : 600f;
        return Mathf.Clamp(shortSide / widthDp, 1f, 4f);
    }

    public static int Execute()
    {
        fails = 0;
        // ---- classes ----
        Check("tall: 1080x2400 (.45) and a .79 view", ScreenFrame.Classify(1080, 2400) == ScreenFrame.Aspect.Tall && ScreenFrame.Classify(790, 1000) == ScreenFrame.Aspect.Tall);
        Check("square: .8 up to 1.25 (the cover 948x1048)",
              ScreenFrame.Classify(801, 1000) == ScreenFrame.Aspect.Square && ScreenFrame.Classify(948, 1048) == ScreenFrame.Aspect.Square &&
              ScreenFrame.Classify(1249, 1000) == ScreenFrame.Aspect.Square);
        Check("wide: 1.25 up to 1.9 (1280x720, the 4:3 tablet, 1440x900, 1920x1080)",
              ScreenFrame.Classify(1250, 1000) == ScreenFrame.Aspect.Wide && ScreenFrame.Classify(1280, 720) == ScreenFrame.Aspect.Wide && ScreenFrame.Classify(1024, 768) == ScreenFrame.Aspect.Wide &&
              ScreenFrame.Classify(1440, 900) == ScreenFrame.Aspect.Wide && ScreenFrame.Classify(1920, 1080) == ScreenFrame.Aspect.Wide);
        Check("ultra: above 1.9 (2560x1080, 3440x1440)", ScreenFrame.Classify(2560, 1080) == ScreenFrame.Aspect.Ultra && ScreenFrame.Classify(3440, 1440) == ScreenFrame.Aspect.Ultra);
        Check("ScreenLayout.IsWide keeps its .8 breakpoint", ScreenLayout.IsWide(801, 1000) && !ScreenLayout.IsWide(790, 1000) && ScreenLayout.WideAspect == .8f);
        Check("a zero-height safe area does not divide by zero", ScreenFrame.Classify(100, 0) == ScreenFrame.Aspect.Tall);

        // ---- rects ----
        var sizes = new[] { new Vector2(1080, 2400), new Vector2(948, 1048), new Vector2(1280, 720), new Vector2(1440, 900), new Vector2(1920, 1080), new Vector2(2560, 1080), new Vector2(3440, 1440) };
        float lastShare = 2f;
        foreach (var sz in sizes)
        {
            var safe = new Rect(20f, 30f, sz.x, sz.y);   // an offset safe area: nothing may assume a (0, 0) origin
            var f = ScreenFrame.Compute(safe);
            string t = sz.x + "x" + sz.y + " (" + f.aspect + "): ";
            float share = f.content.width / safe.width;
            Check(t + "content width is the class's fraction of the safe width", Mathf.Abs(share - ScreenFrame.ContentShare(f.aspect)) < 1e-4f);
            Check(t + "content is centred and inside the safe area",
                  Mathf.Abs(f.content.center.x - safe.center.x) < .01f && f.content.xMin >= safe.xMin - .01f && f.content.xMax <= safe.xMax + .01f && f.content.yMax <= safe.yMax + .01f && f.content.yMin >= safe.yMin - .01f);
            Check(t + "left and right rects flank the content and tile the width",
                  Mathf.Abs(f.left.xMax - f.content.xMin) < .01f && Mathf.Abs(f.right.xMin - f.content.xMax) < .01f &&
                  Mathf.Abs(f.left.width + f.content.width + f.right.width - safe.width) < .02f && Mathf.Abs(f.left.width - f.right.width) < .01f);
            Check(t + "footer sits under the content, the same width, and content + footer fill the height",
                  Mathf.Abs(f.footer.yMax - f.content.yMin) < .01f && Mathf.Abs(f.footer.width - f.content.width) < .01f && Mathf.Abs(f.footer.height + f.content.height - safe.height) < .02f && f.footer.height > 0f);
            Check(t + "scale hint " + f.scale.ToString("F2") + (f.IsWideOrUltra ? " tracks the height" : " is 1"),
                  f.IsWideOrUltra ? Mathf.Abs(f.scale - safe.height / ScreenFrame.ReferenceHeight) < 1e-4f : f.scale == 1f);
            if (sz.x > sz.y * .8f) { Check(t + "content share never grows with the aspect", share <= lastShare + 1e-4f); lastShare = share; }
        }
        Check("tall content is the whole safe width", Mathf.Approximately(ScreenFrame.Compute(new Rect(0, 0, 1080, 2400)).content.width, 1080f));

        // ---- UiScale: phones unchanged, desktop tracks the height ----
        int phones = 0;
        bool same = true;
        foreach (var d in FitDevice.All)
            foreach (float dpi in new[] { d.ReportedDpi, 0f, 50f, 2000f })
            {
                float now = UiScale.UnitPxFor(dpi, d.ios, d.w, d.h);
                same &= now == OldPxPerPoint(dpi, d.ios, d.w, d.h) && UiScale.PxPerPointFor(dpi, d.ios, d.w, d.h) == OldPxPerPoint(dpi, d.ios, d.w, d.h);
                phones++;
            }
        Check("all " + phones + " phone / tablet device x dpi cases: sizes identical to before", same);
        Check("a landscape window WITH a plausible dpi is not a desktop (reported density wins)", UiScale.UnitPxFor(160f, false, 1920, 1080) == 1f && UiScale.UnitPxFor(480f, false, 2400, 1080) == 3f);
        Check("1280x720 / 1920x1080 desktop: unit is 1 px (the point unit)", UiScale.UnitPxFor(0f, false, 1280, 720) == 1f && UiScale.UnitPxFor(0f, false, 1920, 1080) == 1f);
        Check("a 2560x1440 / 3840x2160 window grows with its height (1.31, 1.96 px)",
              Mathf.Abs(UiScale.UnitPxFor(0f, false, 2560, 1440) - 1440f / 1100f) < 1e-4f && Mathf.Abs(UiScale.UnitPxFor(0f, false, 3840, 2160) - 2160f / 1100f) < 1e-4f);
        Check("a tall unreported-dpi window keeps the phone guess", UiScale.UnitPxFor(0f, false, 1080, 1920) == OldPxPerPoint(0f, false, 1080, 1920));
        foreach (var d in FitDevice.Desktops)
            Check(d.id + ": registered, findable, rig unit = UiScale.UnitPxFor", FitDevice.Find(d.id) == d && Mathf.Approximately(d.pxPerPt, UiScale.UnitPxFor(d.ReportedDpi, d.ios, d.w, d.h)));
        Check("the four desktop windows report no dpi", FitDevice.Find("desk-1280x720").ReportedDpi == 0f && FitDevice.Find("desk-2560x1080").ReportedDpi == 0f && FitDevice.Find("tab-1024x768").ReportedDpi > 0f);
        Debug.Log("[SFRAME] failures: " + fails);
        return fails;
    }
}
