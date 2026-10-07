using UnityEngine;
using UnityEngine.UI;

// UiScale's density estimate and minimum UI scale (the rule every canvas's
// floor uses), and that on the user's own phone (1080x2520, 420 dpi) and the
// recent iPhones the rule changes nothing.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod UiScaleTest.Run
public static class UiScaleTest
{
    static int fails;

    static void Check(string what, bool ok)
    {
        if (ok) Debug.Log("[UIS] PASS  " + what);
        else { Debug.Log("[UIS] FAIL  " + what); fails++; }
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    // The canvases that carry a floor: design reference, match, floor units.
    struct Config
    {
        public string name;
        public Vector2 reference;
        public CanvasScaler.ScreenMatchMode mode;
        public float match, tap, text;
    }

    static readonly Config[] Configs =
    {
        new Config { name = "scene canvases (800x600, width)", reference = new Vector2(800, 600), mode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight, match = 0f,
                     tap = UiScaleFloor.SceneTapUnits, text = UiScaleFloor.SceneTextUnits },
        new Config { name = "options / credits (800x1422 expand)", reference = SafeAreaClamp.PortraitReference, mode = CanvasScaler.ScreenMatchMode.Expand,
                     tap = UiScaleFloor.SceneTapUnits, text = UiScaleFloor.SceneTextUnits },
        new Config { name = "codex / toasts / dialog (800x1280 expand)", reference = CodexUi.Reference, mode = CanvasScaler.ScreenMatchMode.Expand,
                     tap = CodexUi.TapUnits, text = CodexUi.TextUnits },
        new Config { name = "home menu (800x1000, height)", reference = new Vector2(800, 1000), mode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight, match = 1f,
                     tap = startMenu.HomeRowUnits },
        new Config { name = "quick actions (800x1000, 0.5)", reference = PauseQuickActions.ReferenceResolution, mode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight,
                     match = PauseQuickActions.MatchWidthOrHeight, tap = PauseQuickActions.ButtonSize },
    };

    public static int Execute()
    {
        fails = 0;

        // ---- density ----
        Check("Android 420 dpi -> 2.625 px/dp", Mathf.Approximately(UiScale.PxPerPointFor(420f, false, 1080, 2520), 2.625f));
        Check("Android 240 dpi -> 1.5 px/dp", Mathf.Approximately(UiScale.PxPerPointFor(240f, false, 480, 854), 1.5f));
        Check("iOS 460 ppi -> @3x", UiScale.PxPerPointFor(460f, true, 1179, 2556) == 3f);
        Check("iOS 401 ppi (Plus, downsampled) -> @3x", UiScale.PxPerPointFor(401f, true, 1080, 1920) == 3f);
        Check("iOS 326 ppi -> @2x", UiScale.PxPerPointFor(326f, true, 750, 1334) == 2f);
        Check("iPad 264 ppi -> @2x", UiScale.PxPerPointFor(264f, true, 2048, 2732) == 2f);
        foreach (float dpi in new[] { 0f, -1f, 40f, 99f, 1200f, float.NaN })
            Check("dpi " + dpi + " is implausible -> the fallback", !UiScale.Plausible(dpi) &&
                  Mathf.Approximately(UiScale.PxPerPointFor(dpi, false, 720, 1280), 720f / UiScale.FallbackPhoneWidthDp));
        Check("fallback: a phone shape is taken as 320 dp wide", Mathf.Approximately(UiScale.PxPerPointFor(0f, false, 480, 854), 1.5f));
        Check("fallback: a squarer shape (tablet / fold / iPad) as 600 dp wide",
              Mathf.Approximately(UiScale.PxPerPointFor(0f, false, 1600, 2560), 1600f / 600f));
        Check("fallback is clamped (1..4)", UiScale.PxPerPointFor(0f, false, 200, 400) == 1f && UiScale.PxPerPointFor(0f, false, 3000, 6000) == 4f);
        // Never an underestimate on any device of the matrix: a target sized
        // from it is never under the minimum.
        bool never = true;
        foreach (var d in FitDevice.All)
        {
            float guess = UiScale.PxPerPointFor(0f, d.ios, d.w, d.h);
            float real = UiScale.PxPerPointFor(d.ReportedDpi, d.ios, d.w, d.h);
            if (guess < real - .001f || Mathf.Abs(real - d.pxPerPt) > .001f)
            {
                never = false;
                Debug.Log("[UIS]   " + d.id + ": fallback " + guess + " real " + real + " (device " + d.pxPerPt + ")");
            }
        }
        Check("the matrix's reported dpi gives its real density, and the fallback is never under it", never);

        // ---- the floor ----
        Check("floor: 96 units at 2.625 px/dp -> 48 dp", Mathf.Approximately(UiScale.FloorFor(96f, 0f, 2.625f, false) * 96f / 2.625f, 48f));
        Check("floor: iOS 44 pt", Mathf.Approximately(UiScale.FloorFor(96f, 0f, 3f, true) * 96f / 3f, 44f));
        Check("floor: type term (7 pt / 14 units)", Mathf.Approximately(UiScale.FloorFor(0f, 14f, 2f, false), 1f));
        Check("floor: the larger term wins", Mathf.Approximately(UiScale.FloorFor(96f, 10f, 1f, false), .7f));
        Check("no floor stated -> 0", UiScale.FloorFor(0f, 0f, 3f, false) == 0f);

        // ---- on devices ----
        var go = new GameObject("~UiScaleTest", typeof(Canvas), typeof(CanvasScaler));
        var scaler = go.GetComponent<CanvasScaler>();
        try
        {
            foreach (var c in Configs)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.screenMatchMode = c.mode;
                scaler.matchWidthOrHeight = c.match;
                foreach (var d in FitDevice.All)
                {
                    foreach (bool reported in new[] { true, false })
                    {
                        using (ScreenInfo.Override(d.w, d.h, d.Safe, d.Cutouts, reported ? d.ReportedDpi : 0f, d.ios))
                        {
                            float pixel = UiScaleFloor.PixelScale(scaler, c.reference);
                            Vector2 r = UiScaleFloor.ReferenceFor(scaler, c.reference, c.tap, c.text);
                            float scale = HudStyler.ScaleWithScreenSize(ScreenInfo.Size, r, c.mode, c.match);
                            // what the device really gets, in its real points / dp
                            float tapPt = c.tap > 0f ? c.tap * scale / d.pxPerPt : 999f;
                            float textPt = c.text > 0f ? c.text * scale / d.pxPerPt : 999f;
                            bool ok = tapPt >= (d.ios ? 44f : 48f) - .01f && textPt >= UiScale.MinTextPt - .01f && scale >= pixel - 1e-4f;
                            string tag = c.name + " @ " + d.id + (reported ? "" : " (dpi unreported)");
                            if (!ok) Check(tag + ": tap " + tapPt.ToString("F1") + " text " + textPt.ToString("F1"), false);
                            if (reported && (d.id == "flip7-1080x2520" || d.id == "iphone-15" || d.id == "iphone-15pm" || d.id == "iphone-16pro"))
                                Check(tag + ": unchanged (" + (scale / pixel).ToString("F3") + "x)", Mathf.Abs(scale - pixel) < 1e-4f);
                        }
                    }
                }
                Check(c.name + ": touch targets and type at or above the floor on every device, dpi reported or not", true);
            }

            // The leaderboard panel (ConstantPixelSize, its own fit).
            bool lb = true;
            foreach (var d in FitDevice.All)
                using (ScreenInfo.Override(d.w, d.h, d.Safe, d.Cutouts, d.ReportedDpi, d.ios))
                {
                    var l = LeaderboardPanel.ComputeLayout(new Vector2(d.w, d.h), d.Safe);
                    float tap = LeaderboardPanel.MinTapUnits * l.scale / d.pxPerPt;
                    lb &= tap >= (d.ios ? 44f : 48f) - .01f && l.panelSize.x <= LeaderboardPanel.DesignWidth + .01f &&
                          l.panelPixels.xMin >= d.Safe.xMin - .5f && l.panelPixels.xMax <= d.Safe.xMax + .5f &&
                          l.panelPixels.yMin >= d.Safe.yMin - .5f && l.panelPixels.yMax <= d.Safe.yMax + .5f;
                }
            Check("leaderboard panel: 100-unit targets >= 44 pt / 48 dp and inside the safe area on every device", lb);
            using (ScreenInfo.Override(1080, 2520, new Rect(0, 0, 1080, 2410), null, 420f))
            {
                var l = LeaderboardPanel.ComputeLayout(new Vector2(1080, 2520), new Rect(0, 0, 1080, 2410));
                Check("leaderboard panel on the 1080x2520 phone: the whole design size, as before",
                      l.panelSize == new Vector2(LeaderboardPanel.DesignWidth, LeaderboardPanel.DesignHeight));
            }
        }
        finally
        {
            Object.DestroyImmediate(go);
        }

        Debug.Log("[UIS] failures: " + fails);
        return fails;
    }
}
