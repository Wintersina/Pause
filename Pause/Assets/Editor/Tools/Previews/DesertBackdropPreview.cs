using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders the Desert v3 backdrop in gameS1 (rails, backdrop, a ship for
// scale) as the phone camera sees it, for review:
//
//   variant-vN-<t>s.png    each ground set at 2 / 10 / 40 s
//   piece-<name>.png       a close crop of every smoking / venting / lit piece, alone on the ground, its loops running
//   worm-<size>-NNN.png    a giant sand worm's whole sequence, cropped round it, every 4th frame at 30 fps
//                          (worm-<size>-sheet.png: eight key frames; worm-wide-NNN.png: full-view frames of the
//                          dunes with a medium worm erupting, for the GIF)
//   site-<kind>-tell.png / -emerge.png  each Desert elite launching from its own site
//
//   DESERT_PREVIEW_DIR=<dir> [DESERT_PREVIEW_ONLY=variants|pieces|worms|sites|motion] Unity -batchmode -quit -projectPath Pause
//       -executeMethod DesertBackdropPreview.Run
public static class DesertBackdropPreview
{
    const float Dt = 1f / 60f;
    static int W = 1080, H = 2400;

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("DESERT_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/DesertBackdropPreview";
        string only = System.Environment.GetEnvironmentVariable("DESERT_PREVIEW_ONLY") ?? "";
        Directory.CreateDirectory(dir);
        int failures = 0;
        using (new TestHarness.Sandbox())
        {
            try
            {
                if (only == "" || only.Contains("variants")) Variants(dir);
                if (only == "" || only.Contains("pieces")) Pieces(dir);
                if (only.Contains("motion")) Motion(dir);
                if (only == "" || only.Contains("worms")) Worms(dir);
                if (only == "" || only.Contains("sites")) SitesShots(dir);
                if (only == "" || only.Contains("raze")) Raze(dir);
            }
            catch (System.Exception e) { Debug.LogException(e); failures++; }
            finally
            {
                BackdropSet.AtlasOverride = null;
                BackdropVariants.For("Desert").Reset();
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

    // ---- each ground set over time, and a launch ---------------------------------------

    static void Variants(string dir)
    {
        var cam = Scene(6, 6);
        var pilot = new GameObject("~PfSpawner").AddComponent<spawnShips>().Spawn(ShipId.Starter);
        pilot.transform.position = new Vector3(-.6f, ShipReach.StartY, 0f);
        EliteSystem.PlayerOverride = pilot.transform;
        var wb = WorldBackdrop.Create("Desert");
        for (int v = 1; v <= BackdropVariants.MaxVariants; v++)
        {
            BackdropVariants.For("Desert").Force = v;
            wb.Show("Space", false);
            wb.Show("Desert", false);
            var d = (DesertDirector)wb.Current.Director;
            foreach (float at in new[] { 2f, 10f, 40f })
            {
                while (d.Clock < at) wb.Step(Dt);
                Capture(cam, Path.Combine(dir, "variant-v" + v + "-" + at.ToString("00") + "s.png"));
                Debug.Log("[DESERT-PREVIEW] v" + v + " " + at + "s ground " + d.Ground.ActiveCount + " fires " + d.Fires.ActiveCount +
                          " pipes " + d.Pipes.ActiveCount + " sites " + d.Sites.ActiveCount + " loops " + d.Ambient.LiveCount +
                          " clusters " + d.Clusters + " runs " + d.PipeRuns);
            }
        }
        Object.DestroyImmediate(wb.gameObject);
    }

    // motion-vN-k.png: six frames 0.4 s apart of each ground set once it is busy (run C loops running)
    static void Motion(string dir)
    {
        var cam = Scene(6, 6);
        var wb = WorldBackdrop.Create("Desert");
        for (int v = 1; v <= BackdropVariants.MaxVariants; v++)
        {
            BackdropVariants.For("Desert").Force = v;
            wb.Show("Space", false);
            wb.Show("Desert", false);
            var d = (DesertDirector)wb.Current.Director;
            while (d.Clock < 16f) wb.Step(Dt);
            for (int k = 0; k < 6; k++)
            {
                Capture(cam, Path.Combine(dir, "motion-v" + v + "-" + k + ".png"));
                for (int i = 0; i < 24; i++) wb.Step(Dt);
            }
            Debug.Log("[DESERT-PREVIEW] motion v" + v + " loops " + d.Ambient.LiveCount);
        }
        Object.DestroyImmediate(wb.gameObject);
    }

    static void SitesShots(string dir)
    {
        var cam = Scene(6, 6);
        var pilot = new GameObject("~PfSpawner").AddComponent<spawnShips>().Spawn(ShipId.Starter);
        pilot.transform.position = new Vector3(-.6f, ShipReach.StartY, 0f);
        EliteSystem.PlayerOverride = pilot.transform;
        var defs = new List<EliteDef>();
        EliteCatalog.ForWorld(6, defs);
        var wb = WorldBackdrop.Create("Desert");
        BackdropVariants.For("Desert").Force = 3;
        wb.Show("Space", false);
        wb.Show("Desert", false);
        var d = (DesertDirector)wb.Current.Director;
        while (d.Clock < 14f) wb.Step(Dt);
        for (int k = 0; k < DesertTuning.SiteKinds.Length; k++)
        {
            d.Showcase("worm_burrow_closed", 1.6f, 0f, -20f);      // clear the ground plane (parked off screen)
            d.SpawnSite(cam.orthographicSize * .4f, k);
            for (int i = 0; i < 10; i++) wb.Step(Dt);
            var sites = new List<LandingSite>();
            LandingSites.Collect(sites);
            LandingSite site = default(LandingSite);
            bool have = false;
            foreach (var s in sites) if (s.kind == DesertTuning.SiteKinds[k]) { site = s; have = true; }
            EliteDef def = null;
            foreach (var e0 in defs) if (LandingSite.KindOf(e0.launchFrom) == DesertTuning.SiteKinds[k]) { def = e0; break; }
            if (!have || def == null) { Debug.LogWarning("[DESERT-PREVIEW] no site " + DesertTuning.SiteKinds[k]); continue; }
            var dirGo = new GameObject("~EliteDir").AddComponent<EliteDirector>();
            var e = dirGo.Spawn(def, site, 2.2f);
            bool tellShot = false, emergeShot = false;
            string name = DesertTuning.SiteKinds[k].ToString();
            for (int i = 0; i < 60 * 8 && e != null && !emergeShot; i++)
            {
                wb.Step(Dt);
                EliteSystem.Step(Dt);
                if (!tellShot && e.State == EliteState.Parked && e.StateTime >= e.ParkSeconds - .5f) { Capture(cam, Path.Combine(dir, "site-" + name + "-tell.png")); tellShot = true; }
                if (e.State == EliteState.LiftOff && e.StateTime >= .55f) { Capture(cam, Path.Combine(dir, "site-" + name + "-emerge.png")); emergeShot = true; }
            }
            Debug.Log("[DESERT-PREVIEW] launch " + name + " (" + (def != null ? def.key : "-") + ") tell " + tellShot + " emerge " + emergeShot);
            EliteSystem.Clear();
            Object.DestroyImmediate(dirGo.gameObject);
        }
        Object.DestroyImmediate(wb.gameObject);
    }

    // raze-NNN.png: a medium worm's ridge meets a planted radio mast and a drill head and destroys them (distant blasts, dust)
    static void Raze(string dir)
    {
        var cam = Scene(6, 6);
        var wb = WorldBackdrop.Create("Desert");
        BackdropVariants.For("Desert").Force = 1;
        wb.Show("Space", false);
        wb.Show("Desert", false);
        var d = (DesertDirector)wb.Current.Director;
        while (d.Clock < 15f) wb.Step(Dt);
        d.ClearWorms();
        d.ClearItems();
        d.SpawnWorm(1, 0f, false, 0f, true);
        var w = d.Worms.items[0];
        d.Plant("radio_mast", 1.5f, w.x - .2f, w.gy + .9);
        d.Plant("drill_head_tower", 1.5f, w.x + .3f, w.gy + 2.0);
        int n = 0;
        for (int i = 0; i < 60 * 40 && (d.Worms.ActiveCount > 0 || d.Blasts.ActiveCount > 0); i++)
        {
            wb.Step(Dt);
            if (i % 5 == 0 && d.ItemsRazed > 0 || i % 40 == 0) Capture(cam, Path.Combine(dir, "raze-" + (n++).ToString("000") + ".png"), 2);
        }
        Debug.Log("[DESERT-PREVIEW] raze: razed " + d.ItemsRazed + " frames " + n);
        Object.DestroyImmediate(wb.gameObject);
    }

    // ---- the worms ---------------------------------------------------------------------------

    static void Worms(string dir)
    {
        var cam = Scene(6, 6);
        var wb = WorldBackdrop.Create("Desert");
        string[] sizeName = { "small", "medium", "giant" };
        foreach (int variant in new[] { 1, 4 })
        {
            BackdropVariants.For("Desert").Force = variant;
            wb.Show("Space", false);
            wb.Show("Desert", false);
            var d = (DesertDirector)wb.Current.Director;
            while (d.Clock < 15f) wb.Step(Dt);      // past the ceiling
            for (int size = 0; size < 3; size++)
            {
                d.ClearWorms();
                for (int t = 0; t < 600 && !d.SpawnWorm(size, size == 2 ? 0f : (size - .5f) * .6f); t++) wb.Step(Dt);
                if (d.Worms.ActiveCount == 0) { Debug.LogWarning("[DESERT-PREVIEW] no worm of size " + size); continue; }
                var worm = d.Worms.items[0];
                float cell = DesertTuning.WormCell[size];
                var frames = new List<string>();
                int n = 0, frame = 0;
                // the whole life: ridge, eruption, dive, ridge, fade (crop round the worm, a wider one for the giant)
                while (worm.active && n < 60 * 60)
                {
                    wb.Step(Dt);
                    n++;
                    if (n % 8 != 0) continue;
                    string file = Path.Combine(dir, "worm-v" + variant + "-" + sizeName[size] + "-" + frame.ToString("000") + ".png");
                    Crop(cam, worm.root.position + new Vector3(0f, cell * .25f, 0f), cell * (size == 2 ? 1.05f : 1.6f), file);
                    frames.Add(file);
                    frame++;
                }
                Debug.Log("[DESERT-PREVIEW] worm v" + variant + " " + sizeName[size] + ": " + frame + " frames, eruptions " + d.Eruptions);
            }
            // full view: one medium worm erupting over the dunes, for the GIF
            d.ClearWorms();
            for (int t = 0; t < 600 && !d.SpawnWorm(1, 0f); t++) wb.Step(Dt);
            int wide = 0;
            for (int n = 0; d.Worms.ActiveCount > 0 && n < 60 * 40; n++)
            {
                wb.Step(Dt);
                if (n % 6 == 0) Capture(cam, Path.Combine(dir, "worm-wide-v" + variant + "-" + (wide++).ToString("000") + ".png"), 2);
            }
        }
        Object.DestroyImmediate(wb.gameObject);
    }

    // ---- close crops of every piece with loops -------------------------------------------

    static void Pieces(string dir)
    {
        var cam = Scene(6, 6);
        var wb = WorldBackdrop.Create("Desert");
        BackdropVariants.For("Desert").Force = 1;
        wb.Show("Desert", false);
        var d = (DesertDirector)wb.Current.Director;
        while (d.Clock < 16f) wb.Step(Dt);       // past the ceiling
        var names = new List<string>();
        foreach (var p in DesertAmbientCatalog.Pieces)
            if (p.emit != null && p.emit.Length > 0) names.Add(p.name);
        int i = 0;
        foreach (string n in names)
        {
            var p = d.Showcase(n, 1.7f, 0f, 1f, i % 2 == 1);
            if (p == null) { Debug.LogWarning("[DESERT-PREVIEW] cannot show " + n); continue; }
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
