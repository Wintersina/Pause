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

    public static int Execute()
    {
        int fails = 0, cells = 0, waived = 0;
        bool full = TestHarness.Slow("screen-fit: the whole device matrix");
        foreach (var dpi in new[] { ScreenFitRig.DpiMode.Reported, ScreenFitRig.DpiMode.Unreported })
        {
            ScreenFitRig.Dpi = dpi;
            string tag = dpi == ScreenFitRig.DpiMode.Unreported ? " (dpi unreported)" : "";
            try
            {
                foreach (var screen in ScreenFitScreens.All)
                {
                    using (new TestHarness.Sandbox())
                    {
                        foreach (var device in FitDevice.All)
                        {
                            if (!full && device.id != "flip7-1080x2520") continue;
                            if (dpi == ScreenFitRig.DpiMode.Unreported && System.Array.IndexOf(UnreportedDevices, device.id) < 0) continue;
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
}
