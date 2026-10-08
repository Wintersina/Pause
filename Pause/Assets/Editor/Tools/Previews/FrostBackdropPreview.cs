using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders the Frost v3 backdrop in gameS1 (rails, backdrop, a ship for
// scale) as the phone camera sees it, for review:
//
//   handoff-NN-<name>.png  the Space -> Frost planetfall's last moments and
//                          the level's first ones (continuity through the
//                          cloud ceiling), then the level at ~10 / 20 / 35 s
//   variant-vN.png         each ground set once the ceiling has cleared
//   blizzard.png           a gust sweeping across
//   site-tell.png          a launch site opened for the tell, the elite's
//   site-emerge.png        engine lights blinking; then the elite flying out
//
//   FROST_PREVIEW_DIR=<dir> Unity -batchmode -quit -projectPath Pause
//       -executeMethod FrostBackdropPreview.Run
//   (FROST_PREVIEW_DEVICE picks the screen; default 1080x2400.)
public static class FrostBackdropPreview
{
    const float Dt = 1f / 60f;
    static int W = 1080, H = 2400;

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("FROST_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/FrostBackdropPreview";
        Directory.CreateDirectory(dir);
        int failures = 0;
        using (new TestHarness.Sandbox())
        {
            try
            {
                Handoff(dir);
                Variants(dir);
            }
            catch (System.Exception e) { Debug.LogException(e); failures++; }
            finally
            {
                FrostBackdropSelection.Reset();
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

    static Camera Scene(int world)
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
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, 0);
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

    // ---- planetfall into Frost, and the level after ----------------------------

    static void Handoff(string dir)
    {
        var cam = Scene(0);
        FrostBackdropSelection.Reset();
        FrostBackdropSelection.Force = 1;
        var wb = WorldBackdrop.Create("Space");
        wb.Show("Space", false);
        for (int i = 0; i < 240; i++) wb.Step(Dt);
        var wm = new GameObject("~WorldManager").AddComponent<WorldManager>();
        wm.SendMessage("Awake");
        const System.Reflection.BindingFlags Inst = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        typeof(WorldManager).GetField("levelBegun", Inst).SetValue(wm, true);
        typeof(BossEncounter).GetField("doneWorld", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).SetValue(null, 0);
        typeof(WorldManager).GetField("distanceLeft", Inst).SetValue(wm, 0f);
        wm.EndLevel();
        var fall = Planetfall.Live;
        if (fall == null) throw new System.Exception("no planetfall opened for Space -> Frost");
        var ship = new GameObject("~PfSpawner").AddComponent<spawnShips>().Spawn(ShipId.Starter);
        ship.transform.position = fall.transform.position + new Vector3(.2f, -fall.ZoneRadius * .8f, 0f);
        if (!fall.Commit(ship.transform)) throw new System.Exception("commit refused");

        // planetfall clock -> shots; after it, level seconds since the switch
        var shots = new List<(string name, float at)>
        {
            ("pf-deep-cloud", 3.4f), ("pf-break", 5.2f), ("pf-clearing", 5.85f), ("pf-release", 7.0f),
        };
        float t = 0f, switchAt = -1f;
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
            }
            Capture(cam, Path.Combine(dir, "handoff-" + s.name + ".png"));
            Debug.Log("[FROST-PREVIEW] " + s.name + " world " + WorldManager.Current.displayName + " switch at " + switchAt);
        }
        var d = wb.Current.Director as FrostDirector;
        if (d == null) throw new System.Exception("Frost backdrop not up after the planetfall");
        // the level
        float[] at = { 10f, 20f, 35f };
        foreach (float a in at)
        {
            while (d.Clock < a)
            {
                if (WorldManager.Flying) wm.Tick(Dt);
                wb.Step(Dt);
            }
            Capture(cam, Path.Combine(dir, "level-" + a.ToString("00") + "s.png"));
            Debug.Log("[FROST-PREVIEW] level " + a + " s: ceiling cover " + d.CeilingCover.ToString("F2") + " landmarks " +
                      d.Ground.ActiveCount + " loops " + d.Ambient.LiveCount + " variant " + wb.Current.Variant);
        }
        Object.DestroyImmediate(wm.gameObject);
        Object.DestroyImmediate(wb.gameObject);
    }

    // ---- each ground set, a gust, a launch -----------------------------------------

    static void Variants(string dir)
    {
        var cam = Scene(1);
        var pilot = new GameObject("~PfSpawner").AddComponent<spawnShips>().Spawn(ShipId.Starter);
        pilot.transform.position = new Vector3(-.6f, ShipReach.StartY, 0f);
        EliteSystem.PlayerOverride = pilot.transform;
        var wb = WorldBackdrop.Create("Frost");
        for (int v = 1; v <= FrostBackdropSelection.MaxVariants; v++)
        {
            FrostBackdropSelection.Force = v;
            wb.Show("Space", false);
            wb.Show("Frost", false);
            var d = (FrostDirector)wb.Current.Director;
            while (d.Clock < 38f) wb.Step(Dt);
            Capture(cam, Path.Combine(dir, "variant-v" + v + ".png"));
            Debug.Log("[FROST-PREVIEW] variant v" + v + " landmarks " + d.Ground.ActiveCount + " loops " + d.Ambient.LiveCount +
                      " auroras " + d.Aurora.ActiveCount);
            if (v == 1)
            {
                d.ForceGust();
                for (int i = 0; i < 80; i++) wb.Step(Dt);
                Capture(cam, Path.Combine(dir, "blizzard.png"));
                foreach (var g in d.Blizzard.items)
                    if (g.active)
                        Debug.Log("[FROST-PREVIEW] gust sheet at " + g.root.position + " size " + g.size + " alpha " + g.sr.color.a +
                                  " order " + g.sr.sortingOrder + " sprite " + (g.sr.sprite != null ? g.sr.sprite.name : "-"));
            }
            if (v == 2) Launch(cam, wb, d, dir);
        }
        Object.DestroyImmediate(wb.gameObject);
    }

    static void Launch(Camera cam, WorldBackdrop wb, FrostDirector d, string dir)
    {
        // a hangar just entering the upper view
        for (int k = 0; k < 600 && !d.SpawnSite(cam.orthographicSize * .45f, 0); k++) wb.Step(Dt);
        var sites = new List<LandingSite>();
        LandingSites.Collect(sites);
        LandingSite site = default(LandingSite);
        bool have = false;
        foreach (var s in sites) if (s.kind == LandingKind.Hangar) { site = s; have = true; }
        if (!have) { Debug.LogWarning("[FROST-PREVIEW] no hangar site to launch from"); return; }
        var defs = new List<EliteDef>();
        EliteCatalog.ForWorld(1, defs);
        var dirGo = new GameObject("~EliteDir").AddComponent<EliteDirector>();
        var e = dirGo.Spawn(defs[0], site, 2.2f);
        bool tellShot = false, emergeShot = false;
        for (int i = 0; i < 60 * 8 && e != null && !emergeShot; i++)
        {
            wb.Step(Dt);
            EliteSystem.Step(Dt);
            if (!tellShot && e.State == EliteState.Parked && e.StateTime >= e.ParkSeconds - .5f)
            {
                Capture(cam, Path.Combine(dir, "site-tell.png"));
                tellShot = true;
            }
            if (e.State == EliteState.LiftOff && e.StateTime >= .55f)
            {
                Capture(cam, Path.Combine(dir, "site-emerge.png"));
                emergeShot = true;
            }
        }
        Debug.Log("[FROST-PREVIEW] launch tell " + tellShot + " emerge " + emergeShot + " site at " + site.Position);
        EliteSystem.Clear();
        Object.DestroyImmediate(dirGo.gameObject);
    }

    static void Capture(Camera cam, string file)
    {
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previous = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var png = new Texture2D(W, H, TextureFormat.RGB24, false);
        png.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        png.Apply();
        File.WriteAllBytes(file, png.EncodeToPNG());
        RenderTexture.active = old;
        cam.targetTexture = previous;
        Object.DestroyImmediate(png);
        Object.DestroyImmediate(rt);
    }
}
