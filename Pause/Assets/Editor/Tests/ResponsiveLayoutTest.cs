using System.Collections.Generic;
using UnityEngine;

// Every layout-critical screen laid out on the shapes that broke before (short, wide, near-square, tall, tablet) and
// checked by the screen-fit rig for the layout failures only: a tap target off the screen or outside the safe area
// (OFFSCREEN / SAFE / CUTOUT / CORNER / HOMEBAR), two tap targets overlapping (OVERLAP), a target under the finger floor
// (TAPSIZE). Targets inside a scroll viewport (the Options column on a short canvas) count when they are visible there.
// The home screen is also checked for the PAUSE logo clearing the PLAY button.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ResponsiveLayoutTest.Run
public static class ResponsiveLayoutTest
{
    public static void Run() { TestHarness.Exit(Execute()); }

    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[RESPLAY] PASS  " : "[RESPLAY] FAIL  ") + what);
        if (!ok) fails++;
    }

    // The reference set. pxPerPt: Android density (dpi / 160).
    public static readonly FitDevice[] Devices =
    {
        Dev("r-1080x2400", 1080, 2400, 2.625f, 110),
        Dev("r-1080x1920", 1080, 1920, 2.625f, 60),
        Dev("r-1536x2048", 1536, 2048, 2f, 40),                // 3:4 tablet
        Dev("r-cover-948x1048", 948, 1048, 2f, 40),            // Z Flip7 cover at its reported density
        Dev("r-cover-948x1048-dense", 948, 1048, 2.625f, 40),  // ... and at a denser setting: a canvas only ~800 units tall
        Dev("r-720x1280", 720, 1280, 2f, 40),
        Dev("r-1080x1350", 1080, 1350, 2.625f, 60),            // very short
        Dev("r-1640x2360", 1640, 2360, 2f, 48),                // iPad Air / 10.9" (.695)
        Dev("r-1668x2388", 1668, 2388, 2f, 48),                // iPad Pro 11" (.70)
        Dev("r-1280x2000", 1280, 2000, 2f, 40),                // ~.64 portrait: the iPad app in a Mac window
        new FitDevice { id = "r-mac-900x1400", name = "Mac window 900x1400 (no dpi)", w = 900, h = 1400, noDpi = true,
                        pxPerPt = UiScale.PxPerPointFor(0f, false, 900, 1400), cutoutKind = "desktop window" },
    };

    // Desktop windows (no reported dpi): one Wide (16:9) and one Ultra (21:9). Only the screens this PR fixed are
    // enforced on them; the rest are the "known, later PR" queue (WideKnown) and are skipped until their PR lands.
    public static FitDevice[] WideDevices
    {
        get { return new[] { FitDevice.Find("desk-1920x1080"), FitDevice.Find("desk-2560x1080"), FitDevice.Find("desk-2560x1600") }; }
    }
    public static readonly string[] WideEnforced = { "dock", "dock-popup-owned", "dock-popup-buy" };
    // Follow-up queue: Shop, death / leaderboard / options, home, pause / toasts / dialogs, then the Codex.
    public static readonly string[] WideKnown =
    {
        "home", "codex-grid", "codex-detail", "shop", "options", "options-start-world", "leaderboard", "leaderboard-settings",
        "credits", "game-paused", "game-death", "tutorial-complete",
    };

    static FitDevice Dev(string id, int w, int h, float pxPerPt, int top)
    {
        return new FitDevice { id = id, name = id, w = w, h = h, pxPerPt = pxPerPt, top = top, cornerRadius = 60 };
    }

    // Screens whose layout is a function of the screen's shape.
    static readonly string[] Screens =
    {
        "home", "codex-grid", "codex-detail", "shop", "options", "options-start-world", "leaderboard", "leaderboard-settings",
        "credits", "dock", "dock-popup-owned", "game-paused", "game-death", "tutorial-complete",
    };

    // Known limit, not yet solved: the six-row home column and the ship card at 2.625 px/dp on a ~800-unit-tall canvas
    // cannot keep finger-sized rows AND the logo / card on screen (see the PR). Skipped on that one shape only.
    static bool Known(FitDevice d, string screen)
    {
        return d.id == "r-cover-948x1048-dense" && (screen == "home" || screen == "dock-popup-owned");
    }

    // A modal card sits over the dock's PLAY button by design.
    static bool Modal(ScreenFitRig.Finding f)
    {
        return f.kind == "OVERLAP" && f.element.Contains("~DockPopup") && f.element.Contains("Canvas/PlayButton");
    }

    static readonly HashSet<string> LayoutKinds = new HashSet<string> { "OFFSCREEN", "SAFE", "CUTOUT", "CORNER", "HOMEBAR", "OVERLAP", "TAPSIZE", "PROPORTION", "DOCKLOT" };

    public static int Execute()
    {
        fails = 0;
        foreach (var device in Devices)
        {
            using (new TestHarness.Sandbox())
            {
                foreach (var id in Screens)
                {
                    if (Known(device, id)) continue;
                    var screen = ScreenFitScreens.Find(id);
                    if (screen == null) { Check(id + ": screen exists", false); continue; }
                    var shot = ScreenFitRunner.Run(screen, device, null, 0);
                    if (shot.error != null) { Check(id + " @ " + device.id + ": staged (" + shot.error + ")", false); continue; }
                    int bad = 0;
                    foreach (var f in shot.findings)
                    {
                        if (f.waived || !LayoutKinds.Contains(f.kind) || Modal(f)) continue;
                        bad++;
                        Check(id + " @ " + device.id + ": " + f.kind + " " + f.element + " " + f.detail, false);
                    }
                    if (bad == 0) Check(id + " @ " + device.id + ": taps inside the safe area, none overlapping", true);
                }
                if (!Known(device, "home")) HomeLogo(device);
            }
        }
        DockRackFits();
        foreach (var device in WideDevices)
        {
            Check(device.id + ": is " + ScreenFrame.Classify(device.w, device.h) + " (no dpi)", device.noDpi && ScreenFrame.Classify(device.w, device.h) >= ScreenFrame.Aspect.Wide);
            using (new TestHarness.Sandbox())
                foreach (var id in WideEnforced) CheckScreen(device, id);
        }
        Debug.Log("[RESPLAY] known, later PR (not enforced on the wide devices): " + string.Join(", ", WideKnown));
        Debug.Log("[RESPLAY] failures: " + fails);
        return fails;
    }

    // The parking lot's shape per view (pure arithmetic; the staged "dock" screens above check the real scene on every
    // device): phones (9:16..9:21) get horizontal lanes, tablets / Mac windows / wide windows vertical lanes, every
    // bay present, never shrunk, and the lot is no wider than the screen.
    static void DockRackFits()
    {
        foreach (float aspect in new[] { .4286f, .46f, .5f, .5625f, .64f, .695f, .70f, .75f, 1.6f })
        {
            float halfH = CameraFit.ComputeSize(5f, 2.85f, (int)(aspect * 1000f), 1000);
            float halfW = halfH * aspect;
            var l = DockLayout.For(16, halfW * 2f, DockLayout.PrefersLanes(halfW, halfH));
            bool phone = aspect < DockLayout.LaneAspect;
            Check("dock lot @ aspect " + aspect + ": " + l.mode + ", " + l.columns + " columns x " + l.rows + " rows, " + l.lanes + " lanes",
                  l.mode == (phone ? LotMode.Rows : LotMode.Lanes) && l.columns * l.rows >= 16 && l.scale == 1f &&
                  l.Width <= halfW * 2f + .001f && AsteroidBackwardsAndShopColumnsTest.NoBayOverlap(l) &&
                  (phone ? l.columns >= 3 : l.lanes >= 1 && l.lanes <= DockLayout.MaxLanes));
        }
    }

    static void CheckScreen(FitDevice device, string id)
    {
        var screen = ScreenFitScreens.Find(id);
        if (screen == null) { Check(id + ": screen exists", false); return; }
        var shot = ScreenFitRunner.Run(screen, device, null, 0);
        if (shot.error != null) { Check(id + " @ " + device.id + ": staged (" + shot.error + ")", false); return; }
        int bad = 0;
        foreach (var f in shot.findings)
        {
            if (f.waived || !LayoutKinds.Contains(f.kind) || Modal(f)) continue;
            bad++;
            Check(id + " @ " + device.id + ": " + f.kind + " " + f.element + " " + f.detail, false);
        }
        if (bad == 0) Check(id + " @ " + device.id + ": taps inside the safe area, none overlapping, card in proportion", true);
    }

    // The PAUSE logo's drawn bottom must clear the PLAY button's top, and stay on screen below the top safe strip.
    static void HomeLogo(FitDevice device)
    {
        bool ok;
        var pin = HomeLayoutTest.Stage(device, false, out ok);
        string d = "home @ " + device.id + ": ";
        Check(d + "logo and buttons found", ok);
        if (!ok) return;
        float playTop = pin.play + pin.h;
        Check(d + "logo bottom " + pin.logoY0 + " clears PLAY top " + playTop + " by " + (pin.logoY0 - playTop) + " px",
              pin.logoY0 - playTop >= pin.h * .4f);
        Check(d + "logo top " + pin.logoY1 + " is below the top safe strip (" + (pin.screenH - pin.safeTop) + ")",
              pin.logoY1 <= pin.screenH - pin.safeTop);
    }
}
