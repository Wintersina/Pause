using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders the Storm backdrop in gameS1 as the phone camera sees it, for review:
//
//   variant-vN-<t>s.png    each ground set at 2 / 10 / 40 s
//   bolt-vN-k.png          a forked bolt, a sheet flash and (when a mast is up) a strike, at the light's peak
//   flash-vN-k.png         the frames around one flash (0.1 s steps) to judge the build-up and fall-off
//   piece-<name>.png       every landmark alone on the ground
//
//   STORM_PREVIEW_DIR=<dir> [STORM_PREVIEW_ONLY=variants|bolts|pieces] Unity -batchmode -quit -projectPath Pause
//       -executeMethod StormBackdropPreview.Run
public static class StormBackdropPreview
{
    const float Dt = 1f / 60f;
    static int W = 1080, H = 2400;

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("STORM_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/StormBackdropPreview";
        string only = System.Environment.GetEnvironmentVariable("STORM_PREVIEW_ONLY") ?? "";
        Directory.CreateDirectory(dir);
        int failures = 0;
        using (new TestHarness.Sandbox())
        {
            try
            {
                if (only == "" || only.Contains("variants")) Variants(dir);
                if (only == "" || only.Contains("bolts")) Bolts(dir);
                if (only == "" || only.Contains("pieces")) Pieces(dir);
            }
            catch (System.Exception e) { Debug.LogException(e); failures++; }
            finally
            {
                BackdropVariants.For("Storm").Reset();
                EliteSystem.PlayerOverride = null;
                EliteSystem.Clear();
                BossEncounter.ResetRun();
                PortalPressure.Reset();
                BossRails.Reset();
                PlayField.Reset();
                ScreenInfo.ClearOverride();
                buttonClicks.playerDied = false;
            }
        }
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    static Camera Scene(int world, int prefsWorld)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity");
        BossEncounter.ResetRun();
        BossRails.Reset();
        PortalPressure.Reset();
        RunLoop.Reset();
        var cam = Camera.main;
        cam.aspect = W / (float)H;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, W, H);
        PlayField.Reset();
        buttonClicks.playerDied = false;
        startMenu.youAreInTutorial = false;
        score.pauseCounter = 0;
        Time.timeScale = 1f;
        moveBackGround.speed = .25f;
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, prefsWorld);
        Random.InitState(7);
        var paused = SceneUtil.FindAny("paused");
        if (paused != null) paused.SetActive(false);
        Paint(cam, WorldManager.Worlds[world]);
        return cam;
    }

    static void Paint(Camera cam, WorldTheme theme)
    {
        WorldPainter.Apply(theme);
        foreach (var name in new[] { "leftPipe", "rightPipe" })
        {
            var wall = GameObject.Find(name);
            if (wall == null) continue;
            var s = wall.transform.localScale;
            s.y = cam.orthographicSize * 2f * 1.085f / wall.GetComponent<MeshFilter>().sharedMesh.bounds.size.y;
            wall.transform.localScale = s;
            RailFit.RefreshTextureTiling(wall);
        }
        BossRails.Measure();
    }

    static float HalfEyeY(StormDirector d) { return 1.6f; }

    static void Variants(string dir)
    {
        var cam = Scene(5, 5);
        var wb = WorldBackdrop.Create("Storm");
        for (int v = 1; v <= StormTuning.Variants; v++)
        {
            BackdropVariants.For("Storm").Force = v;
            wb.Show("Space", false);
            wb.Show("Storm", false);
            var d = (StormDirector)wb.Current.Director;
            foreach (float at in new[] { 2f, 10f, 40f })
            {
                while (d.Clock < at) wb.Step(Dt);
                Capture(cam, Path.Combine(dir, "variant-v" + v + "-" + at.ToString("00") + "s.png"));
                Debug.Log("[STORM-PREVIEW] v" + v + " " + at + "s ground " + d.Ground.ActiveCount + " sites " + d.Sites.ActiveCount +
                          " clusters " + d.Clusters + " flashes " + d.Flashes + " forks " + d.Forks + " strikes " + d.Strikes);
            }
            // the rare eye of the storm, brought on screen (a real run meets it far less often)
            d.Showcase("spire_00", .2f, 9f, 9f);   // clears the ground plane (the eye claims a big footprint)
            bool eyeOk = d.SpawnEye(HalfEyeY(d));
            Debug.Log("[STORM-PREVIEW] eye spawned " + eyeOk + " live " + d.Eye.ActiveCount);
            for (int i = 0; i < 20; i++) wb.Step(Dt);
            Capture(cam, Path.Combine(dir, "eye-v" + v + ".png"));
            if (v == 1)
            {
                // one full turn in 12 frames (every EyeTurnSeconds / 12 s), the eye held at the same ground spot
                BackdropPiece eyePiece = null;
                foreach (var q in d.Eye.items) if (q.active) eyePiece = q;
                for (int k = 0; k < 12 && eyePiece != null; k++)
                {
                    eyePiece.age = StormTuning.EyeTurnSeconds * k / 12f;
                    wb.Step(0.0001f);
                    Capture(cam, Path.Combine(dir, "eyerot-" + k.ToString("00") + ".png"));
                    Debug.Log("[STORM-PREVIEW] eye rot " + k + " angle " + eyePiece.body.localEulerAngles.z.ToString("F1") + " gy " + eyePiece.gy.ToString("F3") + " x " + eyePiece.x.ToString("F2"));
                }
            }
        }
        Object.DestroyImmediate(wb.gameObject);
    }

    // Each lightning kind at its peak, per variant, and the build-up / fall-off frames of one flash.
    static void Bolts(string dir)
    {
        var cam = Scene(5, 5);
        var wb = WorldBackdrop.Create("Storm");
        for (int v = 1; v <= StormTuning.Variants; v++)
        {
            BackdropVariants.For("Storm").Force = v;
            wb.Show("Space", false);
            wb.Show("Storm", false);
            var d = (StormDirector)wb.Current.Director;
            while (d.Clock < 15f) wb.Step(Dt);
            int shots = 0, flashShots = 0, lastForks = d.Forks, lastStrikes = d.Strikes, lastFlashes = d.Flashes;
            for (int i = 0; i < 60 * 150 && (shots < 3 || flashShots < 1); i++)
            {
                wb.Step(Dt);
                if (d.Flashes == lastFlashes) continue;
                lastFlashes = d.Flashes;
                string kind = d.Strikes != lastStrikes ? "strike" : d.Forks != lastForks ? "fork" : "sheet";
                lastForks = d.Forks; lastStrikes = d.Strikes;
                bool want = kind == "strike" || (kind == "fork" && shots < 2) || (kind == "sheet" && shots < 3);
                if (!want && flashShots > 0) continue;
                // the peak of the glow is ~0.1 s in
                for (int k = 0; k < 6; k++) wb.Step(Dt);
                if (want) { Capture(cam, Path.Combine(dir, "bolt-v" + v + "-" + kind + "-" + shots + ".png")); shots++; }
                if (flashShots == 0)
                {
                    // the fall-off: 0.1 s steps over the next half second
                    for (int k = 0; k < 6; k++)
                    {
                        for (int j = 0; j < 6; j++) wb.Step(Dt);
                        Capture(cam, Path.Combine(dir, "flash-v" + v + "-" + k + ".png"), 2);
                    }
                    flashShots++;
                }
            }
            Debug.Log("[STORM-PREVIEW] bolts v" + v + " shots " + shots + " forks " + d.Forks + " strikes " + d.Strikes);
        }
        Object.DestroyImmediate(wb.gameObject);
    }

    static void Pieces(string dir)
    {
        var cam = Scene(5, 5);
        var wb = WorldBackdrop.Create("Storm");
        BackdropVariants.For("Storm").Force = 2;
        wb.Show("Storm", false);
        var d = (StormDirector)wb.Current.Director;
        while (d.Clock < 16f) wb.Step(Dt);
        var names = new List<string>();
        names.AddRange(StormTuning.Towers); names.AddRange(StormTuning.Spires); names.AddRange(StormTuning.Skyhooks);
        names.AddRange(StormTuning.Refineries); names.AddRange(StormTuning.Neighbours); names.Add("relay_dish_00");
        names.AddRange(StormTuning.SiteClosed); names.AddRange(StormTuning.SiteOpen);
        var seen = new HashSet<string>();
        int i = 0;
        foreach (string n in names)
        {
            if (!seen.Add(n)) continue;
            var p = d.Showcase(n, 1.7f, 0f, 1f, i % 2 == 1);
            if (p == null) { Debug.LogWarning("[STORM-PREVIEW] cannot show " + n); continue; }
            for (int k = 0; k < 10; k++) wb.Step(Dt);
            Crop(cam, p.root.position, 1.7f * 1.6f, Path.Combine(dir, "piece-" + n + ".png"));
            i++;
        }
        Object.DestroyImmediate(wb.gameObject);
    }

    static void Crop(Camera cam, Vector3 at, float units, string file)
    {
        var full = Render(cam, W, H);
        float ppu = H / (cam.orthographicSize * 2f);
        Vector3 v = cam.WorldToViewportPoint(at);
        int size = Mathf.RoundToInt(units * ppu);
        int cx = Mathf.RoundToInt(v.x * W), cy = Mathf.RoundToInt(v.y * H);
        int x0 = Mathf.Clamp(cx - size / 2, 0, W - size), y0 = Mathf.Clamp(cy - size / 2 + size / 8, 0, H - size);
        var crop = new Texture2D(size, size, TextureFormat.RGB24, false);
        crop.SetPixels(full.GetPixels(x0, y0, size, size));
        crop.Apply();
        File.WriteAllBytes(file, crop.EncodeToPNG());
        Object.DestroyImmediate(crop);
        Object.DestroyImmediate(full);
    }

    static Texture2D Render(Camera cam, int w, int h)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previous = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var png = new Texture2D(w, h, TextureFormat.RGB24, false);
        png.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        png.Apply();
        RenderTexture.active = old;
        cam.targetTexture = previous;
        Object.DestroyImmediate(rt);
        return png;
    }

    static void Capture(Camera cam, string file, int downscale = 1)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        var png = Render(cam, W / downscale, H / downscale);
        File.WriteAllBytes(file, png.EncodeToPNG());
        Object.DestroyImmediate(png);
    }
}
