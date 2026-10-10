using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders the Tide v3 backdrop in gameS1 (rails, backdrop, a ship for
// scale) as the phone camera sees it, for review:
//
//   handoff-<name>.png     the Ember -> Tide planetfall's last moments and
//                          the level's first (continuity through the ash
//                          cloud ceiling), then the level at 10 / 20 / 35 s
//   variant-vN-<t>s.png    each ground set at 2 / 10 / 40 s
//   piece-<name>.png       a close crop of every smoking / burning / venting
//                          piece, alone on the ground, its loops running
//   site-tell.png / site-emerge.png  an Ember elite (Tide has none yet) launching from a Tide site
//
//   TIDE_PREVIEW_DIR=<dir> [TIDE_PREVIEW_FAKELOOPS=1] [TIDE_PREVIEW_ONLY=variants|pieces|handoff] Unity -batchmode -quit -projectPath Pause
//       -executeMethod TideBackdropPreview.Run
public static class TideBackdropPreview
{
    const float Dt = 1f / 60f;
    static int W = 1080, H = 2400;

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("TIDE_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/TideBackdropPreview";
        string only = System.Environment.GetEnvironmentVariable("TIDE_PREVIEW_ONLY") ?? "";
        // TIDE_PREVIEW_FAKELOOPS=1: until run C is painted, stand TideBackdropTest's synthetic loop bars in (white bars on the
        // emitter anchors) so the PLACEMENT of smoke / flares / bubbles can be looked at
        if (System.Environment.GetEnvironmentVariable("TIDE_PREVIEW_FAKELOOPS") == "1") BackdropSet.AtlasOverride = TideBackdropTest.FakeLoops;
        Directory.CreateDirectory(dir);
        int failures = 0;
        using (new TestHarness.Sandbox())
        {
            try
            {
                if (only.Contains("handoff")) Handoff(dir);       // opt-in: needs the Ember -> Tide planetfall (WorldManager.TideEnabled)
                if (only == "" || only.Contains("variants")) Variants(dir);
                if (only == "" || only.Contains("pieces")) Pieces(dir);
                if (only.Contains("motion")) Motion(dir);
            }
            catch (System.Exception e) { Debug.LogException(e); failures++; }
            finally
            {
                BackdropSet.AtlasOverride = null;
                BackdropVariants.For("Tide").Reset();
                LiftoffCatalog.Enabled = true;
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

    // ---- the planetfall into Tide, and the level after --------------------------

    static void Handoff(string dir)
    {
        var cam = Scene(3, 3);
        LiftoffCatalog.Enabled = false;     // Ember's lift-off itself skipped (PlanetfallPreview does the same)
        BackdropVariants.For("Tide").Reset();
        BackdropVariants.For("Tide").Force = 1;
        var wb = WorldBackdrop.Create("Space");
        wb.Show("Space", false);
        for (int i = 0; i < 240; i++) wb.Step(Dt);
        var wm = new GameObject("~WorldManager").AddComponent<WorldManager>();
        wm.SendMessage("Awake");
        const System.Reflection.BindingFlags Inst = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        typeof(WorldManager).GetField("levelBegun", Inst).SetValue(wm, true);
        typeof(BossEncounter).GetField("doneWorld", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).SetValue(null, 3);
        typeof(WorldManager).GetField("distanceLeft", Inst).SetValue(wm, 0f);
        wm.EndLevel();
        var fall = Planetfall.Live;
        if (fall == null) throw new System.Exception("no planetfall opened for Ember -> Tide");
        var ship = new GameObject("~PfSpawner").AddComponent<spawnShips>().Spawn(ShipId.Starter);
        ship.transform.position = fall.transform.position + new Vector3(.2f, -fall.ZoneRadius * .8f, 0f);
        if (!fall.Commit(ship.transform)) throw new System.Exception("commit refused");
        var shots = new List<(string name, float at)>
        {
            ("pf-deep-cloud", 3.4f), ("pf-break", 5.2f), ("pf-clearing", 5.85f), ("pf-release", 7.0f), ("pf-after", 7.6f),
        };
        float t = 0f, switchAt = -1f;
        int seq = 0;
        foreach (var s in shots)
        {
            while (t < s.at - 1e-4f)
            {
                int before = WorldManager.CurrentIndex;
                if (fall != null && fall.State != Planetfall.Stage.Done) fall.Step(Dt);
                if (WorldManager.Flying) wm.Tick(Dt);
                wb.Step(Dt);
                t += Dt;
                if (WorldManager.CurrentIndex != before) switchAt = t;
                if (fall == null || fall.State == Planetfall.Stage.Done) fall = null;
                if (t > 4.6f && t < 7.6f && ((int)(t / Dt)) % 6 == 0)
                    Capture(cam, Path.Combine(dir, "seq", "s" + (seq++).ToString("000") + ".png"), 4);
            }
            Capture(cam, Path.Combine(dir, "handoff-" + s.name + ".png"));
            Debug.Log("[TIDE-PREVIEW] " + s.name + " world " + WorldManager.Current.displayName + " switch at " + switchAt);
        }
        var d = wb.Current.Director as TideDirector;
        if (d == null) throw new System.Exception("Tide backdrop not up after the planetfall");
        foreach (float a in new[] { 10f, 20f, 35f })
        {
            while (d.Clock < a)
            {
                if (WorldManager.Flying) wm.Tick(Dt);
                wb.Step(Dt);
            }
            Capture(cam, Path.Combine(dir, "handoff-level-" + a.ToString("00") + "s.png"));
            Debug.Log("[TIDE-PREVIEW] level " + a + " s: ceiling " + d.CeilingCover.ToString("F2") + " ground " + d.Ground.ActiveCount +
                      " fires " + d.Fires.ActiveCount + " pipes " + d.Pipes.ActiveCount + " loops " + d.Ambient.LiveCount);
        }
        Object.DestroyImmediate(wm.gameObject);
        Object.DestroyImmediate(wb.gameObject);
        LiftoffCatalog.Enabled = true;
    }

    // ---- each ground set over time, and a launch ---------------------------------------

    static void Variants(string dir)
    {
        var cam = Scene(4, 4);
        var pilot = new GameObject("~PfSpawner").AddComponent<spawnShips>().Spawn(ShipId.Starter);
        pilot.transform.position = new Vector3(-.6f, ShipReach.StartY, 0f);
        EliteSystem.PlayerOverride = pilot.transform;
        var wb = WorldBackdrop.Create("Tide");
        for (int v = 1; v <= BackdropVariants.MaxVariants; v++)
        {
            BackdropVariants.For("Tide").Force = v;
            wb.Show("Space", false);
            wb.Show("Tide", false);
            var d = (TideDirector)wb.Current.Director;
            foreach (float at in new[] { 2f, 10f, 40f })
            {
                while (d.Clock < at) wb.Step(Dt);
                Capture(cam, Path.Combine(dir, "variant-v" + v + "-" + at.ToString("00") + "s.png"));
                Debug.Log("[TIDE-PREVIEW] v" + v + " " + at + "s ground " + d.Ground.ActiveCount + " fires " + d.Fires.ActiveCount +
                          " pipes " + d.Pipes.ActiveCount + " sites " + d.Sites.ActiveCount + " loops " + d.Ambient.LiveCount +
                          " clusters " + d.Clusters + " runs " + d.PipeRuns);
            }
            if (v == 2) Launch(cam, wb, d, dir);
        }
        Object.DestroyImmediate(wb.gameObject);
    }

    // motion-vN-k.png: six frames 0.4 s apart of each ground set once it is busy (run C loops running)
    static void Motion(string dir)
    {
        var cam = Scene(4, 4);
        var wb = WorldBackdrop.Create("Tide");
        for (int v = 1; v <= BackdropVariants.MaxVariants; v++)
        {
            BackdropVariants.For("Tide").Force = v;
            wb.Show("Space", false);
            wb.Show("Tide", false);
            var d = (TideDirector)wb.Current.Director;
            while (d.Clock < 16f) wb.Step(Dt);
            for (int k = 0; k < 6; k++)
            {
                Capture(cam, Path.Combine(dir, "motion-v" + v + "-" + k + ".png"));
                for (int i = 0; i < 24; i++) wb.Step(Dt);
            }
            Debug.Log("[TIDE-PREVIEW] motion v" + v + " loops " + d.Ambient.LiveCount);
        }
        Object.DestroyImmediate(wb.gameObject);
    }

    static void Launch(Camera cam, WorldBackdrop wb, TideDirector d, string dir)
    {
        for (int k = 0; k < 600 && !d.SpawnSite(cam.orthographicSize * .45f, 1); k++) wb.Step(Dt);
        var sites = new List<LandingSite>();
        LandingSites.Collect(sites);
        LandingSite site = default(LandingSite);
        bool have = false;
        foreach (var s in sites) if (s.kind == LandingKind.TideRigDeck) { site = s; have = true; }
        if (!have) { Debug.LogWarning("[TIDE-PREVIEW] no rig bay to launch from"); return; }
        var defs = new List<EliteDef>();
        EliteCatalog.ForWorld(3, defs);
        var dirGo = new GameObject("~EliteDir").AddComponent<EliteDirector>();
        var e = dirGo.Spawn(defs[0], site, 2.2f);
        bool tellShot = false, emergeShot = false;
        for (int i = 0; i < 60 * 8 && e != null && !emergeShot; i++)
        {
            wb.Step(Dt);
            EliteSystem.Step(Dt);
            if (!tellShot && e.State == EliteState.Parked && e.StateTime >= e.ParkSeconds - .5f) { Capture(cam, Path.Combine(dir, "site-tell.png")); tellShot = true; }
            if (e.State == EliteState.LiftOff && e.StateTime >= .55f) { Capture(cam, Path.Combine(dir, "site-emerge.png")); emergeShot = true; }
        }
        Debug.Log("[TIDE-PREVIEW] launch tell " + tellShot + " emerge " + emergeShot);
        EliteSystem.Clear();
        Object.DestroyImmediate(dirGo.gameObject);
    }

    // ---- close crops of every piece with loops -------------------------------------------

    static void Pieces(string dir)
    {
        var cam = Scene(4, 4);
        var wb = WorldBackdrop.Create("Tide");
        BackdropVariants.For("Tide").Force = 1;
        wb.Show("Tide", false);
        var d = (TideDirector)wb.Current.Director;
        while (d.Clock < 16f) wb.Step(Dt);       // past the ceiling
        var names = new List<string>();
        foreach (var p in TideAmbientCatalog.Pieces)
            if (p.emit != null && p.emit.Length > 0) names.Add(p.name);
        int i = 0;
        foreach (string n in names)
        {
            var p = d.Showcase(n, 1.7f, 0f, 1f, i % 2 == 1);
            if (p == null) { Debug.LogWarning("[TIDE-PREVIEW] cannot show " + n); continue; }
            for (int k = 0; k < 40; k++) wb.Step(Dt);
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
