using System.Collections.Generic;
using UnityEngine;

// Every screen and overlay (ScreenFitScreens) laid out on every phone /
// tablet shape of the matrix (FitDevice.All: 16:9 to 22:9 Android, the Z Flip7,
// a low-dpi phone, foldable inner / tablet shapes, iPhone SE to 16 Pro Max,
// iPads), each with its safe area, cutouts (notch, Dynamic Island,
// punch-holes, waterfall edges), rounded corners and home indicator / nav
// bar, and checked by ScreenFitRig:
//
//   * every tap target on screen, inside the safe area, clear of cutouts,
//     rounded corners and the home indicator, finger-sized, not overlapping
//     another one
//   * every text inside the safe area, clear of cutouts, not truncated, above
//     the legibility floor
//   * full-bleed screens leave no bare pixel (behind cutouts too)
//   * gameplay framing: rails span the view, spawn / despawn / portal / boss
//     arrival lines off screen, backdrop laid out for the whole view
//   * overlays near the top (codex toast, PAUSED, boss name) clear the top
//     band (read-out, quick actions, boss chip and banner)
//
// Findings accepted on purpose (ScreenFitScreens.Waivers: design decisions
// still open) are logged as WAIVED and do not fail. The Galaxy Z Flip7 column
// always runs; the rest of the matrix is a slow check (RunAll, not RunFast).
//
// Contact sheets: ScreenFitSheets.Run, then docs/tools/screen_fit_sheets.py.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ScreenFitTest.Run
public static class ScreenFitTest
{
    public static void Run() { TestHarness.Exit(Execute()); }

    // Second pass: every device reporting no dpi at all (UiScale's
    // fallback density), on the shapes where that matters most.
    static readonly string[] UnreportedDevices =
    {
        "and-480x854", "and-720x1280", "and-1080x1920", "and-1080x1920-navbar", "and-1080x2340-waterfall", "flip7-1080x2520",
        "fold-1812x2176", "tab-1600x2560", "iphone-se", "iphone-15",
    };

    // Two lanes (env PAUSE_TEST_FULL): unset = GATE lane, every screen on the
    // Flip7 plus the aspect / safe-area extremes below; PAUSE_TEST_FULL=1 = the
    // whole matrix (every device, plus the dpi-unreported pass).
    public static readonly string[] GateDevices =
    {
        "flip7-1080x2520",           // the user's phone: 21:9, centre punch-hole
        "and-480x854",               // smallest, low dpi, 16:9
        "and-1080x2340-waterfall",   // left / right insets + rounded corners
        "fold-1812x2176",            // widest (near square)
        "iphone-16pm",               // Dynamic Island + home indicator
    };
    static readonly string[] GateUnreported = { "flip7-1080x2520" };

    // Is this device part of the current lane's matrix (all of them in the full lane)?
    public static bool InLane(FitDevice d) { return FullLane || System.Array.IndexOf(GateDevices, d.id) >= 0; }

    public static bool FullLane { get { return TestHarness.FullLane; } }

    public static int Execute() { return Execute(0, 1); }

    // Slice k of n of ScreenFitScreens.All (contiguous, so a scene stays in one slice).
    public static int Execute(int slice, int slices)
    {
        int fails = 0, cells = 0, waived = 0;
        bool full = FullLane && TestHarness.Slow("screen-fit: the whole device matrix");
        var screens = new List<FitScreen>();
        int total = ScreenFitScreens.All.Length;
        int from = slice * total / slices, to = (slice + 1) * total / slices;
        if (slices == SliceStarts.Length + 1)
        {
            // equal TIME, not equal count: the codex screens cost 2-4x the rest (measured, gate lane)
            from = slice == 0 ? 0 : IndexOf(SliceStarts[slice - 1]);
            to = slice == slices - 1 ? total : IndexOf(SliceStarts[slice]);
        }
        for (int i = from; i < to; i++) screens.Add(ScreenFitScreens.All[i]);
        foreach (var dpi in new[] { ScreenFitRig.DpiMode.Reported, ScreenFitRig.DpiMode.Unreported })
        {
            ScreenFitRig.Dpi = dpi;
            string tag = dpi == ScreenFitRig.DpiMode.Unreported ? " (dpi unreported)" : "";
            try
            {
                foreach (var screen in screens)
                {
                    var swScreen = System.Diagnostics.Stopwatch.StartNew();
                    using (new TestHarness.Sandbox())
                    {
                        foreach (var device in FitDevice.All)
                        {
                            if (!full && System.Array.IndexOf(GateDevices, device.id) < 0) continue;
                            var unrep = full ? UnreportedDevices : GateUnreported;
                            if (dpi == ScreenFitRig.DpiMode.Unreported && System.Array.IndexOf(unrep, device.id) < 0) continue;
                            var shot = ScreenFitRunner.Run(screen, device, null, 0);
                            cells++;
                            foreach (var f in shot.findings)
                            {
                                if (f.waived) { waived++; continue; }
                                Debug.Log("[FIT] FAIL  " + screen.id + " @ " + device.id + tag + "  " + f.kind + "  " + f.element + ": " + f.detail);
                                fails++;
                            }
                            if (shot.failures == 0) Debug.Log("[FIT] PASS  " + screen.id + " @ " + device.id + tag);
                        }
                    }
                    Debug.Log("[FITTIME] " + screen.id + tag + " " + swScreen.Elapsed.TotalSeconds.ToString("F2") + "s");
                }
            }
            finally
            {
                ScreenFitRig.Dpi = ScreenFitRig.DpiMode.Reported;
            }
        }
        Debug.Log("[FIT] " + cells + " screen x device cells, " + fails + " failing checks, " + waived + " waived findings");
        return fails;
    }

    // First screen of slices 2..4 (4 slices of ~15-21 s each in the gate lane; re-measure with the [FITTIME] log lines).
    static readonly string[] SliceStarts = { "codex-music", "options-start-world", "tutorial" };
    static int IndexOf(string id)
    {
        for (int i = 0; i < ScreenFitScreens.All.Length; i++) if (ScreenFitScreens.All[i].id == id) return i;
        throw new System.InvalidOperationException("ScreenFitTest.SliceStarts names an unknown screen: " + id);
    }

    public static int Slice1() { return Execute(0, 4); }
    public static int Slice2() { return Execute(1, 4); }
    public static int Slice3() { return Execute(2, 4); }
    public static int Slice4() { return Execute(3, 4); }
}
