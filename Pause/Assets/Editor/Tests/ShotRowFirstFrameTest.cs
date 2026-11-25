using UnityEngine;

// The shot-atom row, on the first frame it is ever visible. The run's HUD is built the way the game builds it, then the first atom
// is caught: ShotAtomHud builds lazily in that same Update. The visible coin art must move halfway toward the score board's VISIBLE bottom edge
// (measured on the drawn board frame, not through the row's own maths), for every world, on phones and on desktop / Mac windows.
//   Unity -batchmode -quit -projectPath Pause -executeMethod ShotRowFirstFrameTest.Run
public static class ShotRowFirstFrameTest
{
    public static void Run() { TestHarness.Exit(Execute()); }

    // Pure sweep over desktop windows: compare the visible art gap with half its former gap.
    static int Sweep()
    {
        int bad = 0, n = 0;
        string first = null;
        for (int w = 600; w <= 3000; w += 50)
            for (int h = 400; h <= 1800; h += 50)
                foreach (float rail in new[] { 0f, 2.2f })
                {
                    var screen = new Vector2(w, h);
                    var safe = new Rect(0, 0, w, h);
                    var band = TopBand.FrameFor(safe, screen, rail, new Rect[0]);
                    float scale = HudStyler.ScaleWithScreenSize(screen, new Vector2(800f, 600f), UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight, 0f);
                    Rect hud = HudStyler.HudScreenRect(band, screen, scale, new Vector2(351f, 131f));
                    for (int world = 0; world < HudSkin.WorldCount; world++)
                    {
                        var g = ShotAtomHud.Compute(band, safe, screen, hud, world);
                        float gap = Mathf.Max(1f, Mathf.Round(ShotAtomHud.GapUnits * PauseQuickActions.CanvasScaleFor(screen)));
                        float want = hud.yMin - HudStyler.BoardVisibleBottomPx(hud, world) - g.originalVisibleGap * .5f;
                        n++;
                        if (Mathf.Abs(ShotAtomHud.VisibleIconTop(g) - want) > 2f)
                        {
                            bad++;
                            if (first == null) first = w + "x" + h + " rail " + rail + " world " + world + ": top " + g.top + " vs " + want + " hud " + hud + " band " + band.left + "," + band.right + "," + band.top;
                        }
                    }
                }
        Debug.Log("[SHOTROW] sweep " + n + " layouts, " + bad + " off" + (first != null ? " (first: " + first + ")" : ""));
        return bad > 0 ? 1 : 0;
    }

    public static int Execute()
    {
        int fails = 0, cells = 0;
        fails += Sweep();
        var devices = new System.Collections.Generic.List<FitDevice>(FitDevice.Desktops);
        foreach (var id in new[] { "and-1080x2340-notch", "and-480x854", "iphone-se", "tab-1600x2560", "flip7-cover-948x1048" }) devices.Add(FitDevice.Find(id));
        foreach (var d in devices)
            for (int world = 0; world < HudSkin.WorldCount; world++)
                using (new TestHarness.Sandbox())
                {
                    var shot = ScreenFitRunner.Run(ScreenFitScreens.ShotRow(world), d, null, 0);
                    cells++;
                    foreach (var f in shot.findings)
                        if (f.kind == "SHOTROW" || f.kind == "STAGE")
                        {
                            Debug.Log("[SHOTROW] FAIL " + d.id + " world " + world + " " + f.element + ": " + f.detail);
                            fails++;
                        }
                }
        // a window resized after the run started (Mac: dragged / maximised / full screen)
        var from = FitDevice.Find("desk-1100x800");
        foreach (var id in new[] { "desk-1920x1080", "desk-1440x900", "desk-2200x1600", "and-1080x2340-notch", "mac-1100x800-dpi144" })
            foreach (int world in new[] { 0, 3 })
                using (new TestHarness.Sandbox())
                {
                    var d = FitDevice.Find(id);
                    var shot = ScreenFitRunner.Run(ScreenFitScreens.ShotRow(world, from), d, null, 0);
                    cells++;
                    foreach (var f in shot.findings)
                        if (f.kind == "SHOTROW" || f.kind == "STAGE")
                        {
                            Debug.Log("[SHOTROW] FAIL resized " + from.id + " -> " + d.id + " world " + world + " " + f.element + ": " + f.detail);
                            fails++;
                        }
                }
        Debug.Log("[SHOTROW] " + cells + " cells, failures: " + fails);
        return fails;
    }
}
