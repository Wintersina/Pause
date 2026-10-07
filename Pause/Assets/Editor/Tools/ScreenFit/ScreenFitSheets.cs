using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Lays out one screen on one device (ScreenFitScreens x FitDevice), runs the
// generic fit checks, and optionally renders the frame.
public static class ScreenFitRunner
{
    [Serializable]
    public class Shot
    {
        public string screen, device, file, error;
        public float bare, minTextPt, halfWidth, halfHeight;
        public string minTextName;
        public int failures;
        public List<ScreenFitRig.Finding> findings = new List<ScreenFitRig.Finding>();
        public List<TapInfo> taps = new List<TapInfo>();
    }

    [Serializable]
    public class TapInfo
    {
        public string name;
        public float x, y, w, h;
    }

    // Bare pixels tolerated on a full-bleed screen (a 96 px wide probe).
    public const float BareTolerance = .0005f;

    public static Shot Run(FitScreen screen, FitDevice device, string pngPath, int maxHeight)
    {
        var shot = new Shot { screen = screen.id, device = device.id };
        if (screen.scene != null) EditorSceneLoader.Open(screen.scene);
        else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        ResetGameState();

        using (var rig = new ScreenFitRig(device, screen.MinHalfWidth))
        {
            foreach (var w in ScreenFitScreens.Waivers)
                if (w.Applies(screen.id, device)) rig.Waive(w.kind, w.element, w.reason);
            try
            {
                UiScaleFloor.AttachScene();   // what the scene-load hook does on a device
                rig.Sync();
                screen.stage(rig);
                rig.Sync();
                rig.RunChecks();
            }
            catch (Exception e)
            {
                var inner = e is System.Reflection.TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                shot.error = inner.GetType().Name + ": " + inner.Message;
                rig.Fail("STAGE", screen.id, shot.error);
                Debug.LogError("[FIT] " + screen.id + " @ " + device.id + " threw: " + inner);
            }

            bool render = pngPath != null || screen.fullBleed;
            if (render)
            {
                try
                {
                    var tex = rig.Capture(pngPath != null ? maxHeight : 160, screen.fullBleed);
                    if (pngPath != null)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(pngPath));
                        File.WriteAllBytes(pngPath, tex.EncodeToPNG());
                        shot.file = Path.GetFileName(pngPath);
                    }
                    UnityEngine.Object.DestroyImmediate(tex);
                    if (screen.fullBleed && rig.bareFraction > BareTolerance)
                        rig.Fail("BARE", screen.id, (rig.bareFraction * 100f).ToString("F2") + "% of the frame is not covered by any art (bare camera background)");
                }
                catch (Exception e)
                {
                    rig.Fail("STAGE", screen.id, "render failed: " + e.Message);
                    Debug.LogError("[FIT] render " + screen.id + " @ " + device.id + " threw: " + e);
                }
            }

            shot.bare = rig.bareFraction;
            shot.minTextPt = rig.minTextPt == float.MaxValue ? 0f : rig.minTextPt;
            shot.minTextName = rig.minTextName;
            shot.halfWidth = rig.HalfWidth;
            shot.halfHeight = rig.HalfHeight;
            shot.findings = rig.findings;
            shot.failures = rig.Failures;
            foreach (var t in rig.taps)
                shot.taps.Add(new TapInfo { name = t.name, x = t.rect.x, y = t.rect.y, w = t.rect.width, h = t.rect.height });
        }
        return shot;
    }

    // The statics a previous screen leaves behind that change how the next one stages.
    static void ResetGameState()
    {
        Time.timeScale = 1f;
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        startMenu.youAreInTutorial = false;
        if (DeveloperUnlocks.Enabled) DeveloperUnlocks.SetEnabled(false);
        AccountDialog.Close();
        LeaderboardPanel.Close();
        LeaderboardBoards.OverrideForTests(null);
        HudStyler.StackedReadout = default(Rect);
        ShipSkins.ClearPreview();
        PortalPressure.Reset();
    }
}

// Renders every screen on every device of the matrix and writes the frames
// plus results.json (what ScreenFitTest asserts, with the rects), for the
// contact sheets:
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ScreenFitSheets.Run
//   python3 Tools/screen_fit_sheets.py <dir>/<label>
//
// Environment:
//   SCREEN_FIT_DIR      output root (default Builds/ScreenFit)
//   SCREEN_FIT_LABEL    sub-folder, e.g. "before" / "after" (default "run")
//   SCREEN_FIT_SCREENS  comma-separated screen ids (default: all)
//   SCREEN_FIT_DEVICES  comma-separated device ids (default: all)
//   SCREEN_FIT_HEIGHT   frame height in px (default 720)
public static class ScreenFitSheets
{
    [Serializable]
    class DeviceInfo
    {
        public string id, name, cutoutKind;
        public int w, h, top, bottom, left, right, cornerRadius;
        public bool ios, homeBar;
        public float pxPerPt;
        public float homeX, homeY, homeW, homeH;
        public List<Rect> cutouts = new List<Rect>();
    }

    [Serializable]
    class ScreenInfoRow { public string id, title, scene; }

    [Serializable]
    class Results
    {
        public string label;
        public List<DeviceInfo> devices = new List<DeviceInfo>();
        public List<ScreenInfoRow> screens = new List<ScreenInfoRow>();
        public List<ScreenFitRunner.Shot> shots = new List<ScreenFitRunner.Shot>();
    }

    static string Env(string name, string fallback)
    {
        string v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(v) ? fallback : v;
    }

    static HashSet<string> Filter(string name)
    {
        string v = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(v)) return null;
        return new HashSet<string>(v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
    }

    public static void Run()
    {
        // SCREEN_FIT_DPI=unreported: every device reports no density (UiScale's fallback)
        ScreenFitRig.Dpi = Env("SCREEN_FIT_DPI", "reported") == "unreported" ? ScreenFitRig.DpiMode.Unreported : ScreenFitRig.DpiMode.Reported;
        string dir = Path.Combine(Env("SCREEN_FIT_DIR", "Builds/ScreenFit"), Env("SCREEN_FIT_LABEL", "run"));
        int height = int.Parse(Env("SCREEN_FIT_HEIGHT", "720"));
        var screens = Filter("SCREEN_FIT_SCREENS");
        var devices = Filter("SCREEN_FIT_DEVICES");
        Directory.CreateDirectory(Path.Combine(dir, "shots"));
        foreach (var t in new[] { LogType.Log, LogType.Warning, LogType.Assert })
            Application.SetStackTraceLogType(t, StackTraceLogType.None);

        var results = new Results { label = Env("SCREEN_FIT_LABEL", "run") };
        foreach (var d in FitDevice.All)
        {
            if (devices != null && !devices.Contains(d.id)) continue;
            var hb = d.HomeBarRect;
            var info = new DeviceInfo
            {
                id = d.id, name = d.name, cutoutKind = d.cutoutKind, w = d.w, h = d.h, top = d.top, bottom = d.bottom,
                left = d.left, right = d.right, cornerRadius = d.cornerRadius, ios = d.ios, homeBar = d.homeBar, pxPerPt = d.pxPerPt,
                homeX = hb.x, homeY = hb.y, homeW = hb.width, homeH = hb.height,
            };
            info.cutouts.AddRange(d.Cutouts);
            results.devices.Add(info);
        }

        int total = 0;
        foreach (var s in ScreenFitScreens.All)
        {
            if (screens != null && !screens.Contains(s.id)) continue;
            results.screens.Add(new ScreenInfoRow { id = s.id, title = s.title, scene = s.scene });
            using (new TestHarness.Sandbox())
            {
                foreach (var d in FitDevice.All)
                {
                    if (devices != null && !devices.Contains(d.id)) continue;
                    string png = Path.Combine(dir, "shots", s.id + "__" + d.id + ".png");
                    var shot = ScreenFitRunner.Run(s, d, png, height);
                    results.shots.Add(shot);
                    total += shot.failures;
                    Debug.Log("[FIT] " + s.id + " @ " + d.id + ": " + (shot.failures == 0 ? "ok" : shot.failures + " failing") +
                              "  minText " + shot.minTextPt.ToString("F1") + "  bare " + (shot.bare * 100f).ToString("F2") + "%");
                    foreach (var f in shot.findings)
                        Debug.Log("[FIT]     " + (f.waived ? "(waived) " : "") + f.kind + "  " + f.element + ": " + f.detail);
                }
            }
        }
        File.WriteAllText(Path.Combine(dir, "results.json"), JsonUtility.ToJson(results, true));
        Debug.Log("[FIT] wrote " + dir + " (" + results.shots.Count + " frames, " + total + " failing checks)");
        EditorApplication.Exit(0);
    }
}
