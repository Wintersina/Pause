using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders the Ember v3 backdrop in gameS1 (rails, backdrop, a ship for
// scale) as the phone camera sees it, for review:
//
//   handoff-<name>.png     the Verdant -> Ember planetfall's last moments and
//                          the level's first (continuity through the ash
//                          cloud ceiling), then the level at 10 / 20 / 35 s
//   variant-vN-<t>s.png    each ground set at 2 / 10 / 40 s
//   piece-<name>.png       a close crop of every smoking / burning / venting
//                          piece, alone on the ground, its loops running
//   site-tell.png / site-emerge.png  an Ember elite launching
//
//   EMBER_PREVIEW_DIR=<dir> [EMBER_PREVIEW_ONLY=variants|pieces|handoff] Unity -batchmode -quit -projectPath Pause
//       -executeMethod EmberBackdropPreview.Run
public static class EmberBackdropPreview
{
    const float Dt = 1f / 60f;
    static int W = 1080, H = 2400;

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("EMBER_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/EmberBackdropPreview";
        string only = System.Environment.GetEnvironmentVariable("EMBER_PREVIEW_ONLY") ?? "";
        Directory.CreateDirectory(dir);
        int failures = 0;
        using (new TestHarness.Sandbox())
        {
            try
            {
                if (only == "" || only.Contains("handoff")) Handoff(dir);
                if (only == "" || only.Contains("variants")) Variants(dir);
                if (only == "" || only.Contains("pieces")) Pieces(dir);
                if (only.Contains("smoke")) Smoke(dir);
                if (only.Contains("pools")) Pools(dir);
                if (only.Contains("occlusion")) Occlusion(dir);
            }
            catch (System.Exception e) { Debug.LogException(e); failures++; }
            finally
            {
                BackdropVariants.For("Ember").Reset();
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

    // ---- the planetfall into Ember, and the level after --------------------------

    static void Handoff(string dir)
    {
        var cam = Scene(0, 2);
        LiftoffCatalog.Enabled = false;     // Verdant's lift-off itself skipped (PlanetfallPreview does the same)
        BackdropVariants.For("Ember").Reset();
        BackdropVariants.For("Ember").Force = 1;
        var wb = WorldBackdrop.Create("Space");
        wb.Show("Space", false);
        for (int i = 0; i < 240; i++) wb.Step(Dt);
        var wm = new GameObject("~WorldManager").AddComponent<WorldManager>();
        wm.SendMessage("Awake");
        const System.Reflection.BindingFlags Inst = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        typeof(WorldManager).GetField("levelBegun", Inst).SetValue(wm, true);
        typeof(BossEncounter).GetField("doneWorld", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).SetValue(null, 2);
        typeof(WorldManager).GetField("distanceLeft", Inst).SetValue(wm, 0f);
        wm.EndLevel();
        var fall = Planetfall.Live;
        if (fall == null) throw new System.Exception("no planetfall opened for Verdant -> Ember");
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
            Debug.Log("[EMBER-PREVIEW] " + s.name + " world " + WorldManager.Current.displayName + " switch at " + switchAt);
        }
        var d = wb.Current.Director as EmberDirector;
        if (d == null) throw new System.Exception("Ember backdrop not up after the planetfall");
        foreach (float a in new[] { 10f, 20f, 35f })
        {
            while (d.Clock < a)
            {
                if (WorldManager.Flying) wm.Tick(Dt);
                wb.Step(Dt);
            }
            Capture(cam, Path.Combine(dir, "handoff-level-" + a.ToString("00") + "s.png"));
            Debug.Log("[EMBER-PREVIEW] level " + a + " s: ceiling " + d.CeilingCover.ToString("F2") + " ground " + d.Ground.ActiveCount +
                      " fires " + d.Fires.ActiveCount + " pipes " + d.Pipes.ActiveCount + " loops " + d.Ambient.LiveCount);
        }
        Object.DestroyImmediate(wm.gameObject);
        Object.DestroyImmediate(wb.gameObject);
        LiftoffCatalog.Enabled = true;
    }

    // ---- each ground set over time, and a launch ---------------------------------------

    static void Variants(string dir)
    {
        var cam = Scene(3, 3);
        var pilot = new GameObject("~PfSpawner").AddComponent<spawnShips>().Spawn(ShipId.Starter);
        pilot.transform.position = new Vector3(-.6f, ShipReach.StartY, 0f);
        EliteSystem.PlayerOverride = pilot.transform;
        var wb = WorldBackdrop.Create("Ember");
        for (int v = 1; v <= BackdropVariants.MaxVariants; v++)
        {
            BackdropVariants.For("Ember").Force = v;
            wb.Show("Space", false);
            wb.Show("Ember", false);
            var d = (EmberDirector)wb.Current.Director;
            foreach (float at in new[] { 2f, 10f, 40f })
            {
                while (d.Clock < at) wb.Step(Dt);
                Capture(cam, Path.Combine(dir, "variant-v" + v + "-" + at.ToString("00") + "s.png"));
                Debug.Log("[EMBER-PREVIEW] v" + v + " " + at + "s ground " + d.Ground.ActiveCount + " fires " + d.Fires.ActiveCount +
                          " pipes " + d.Pipes.ActiveCount + " sites " + d.Sites.ActiveCount + " loops " + d.Ambient.LiveCount +
                          " clusters " + d.Clusters + " runs " + d.PipeRuns);
            }
            if (v == 2) Launch(cam, wb, d, dir);
        }
        Object.DestroyImmediate(wb.gameObject);
    }

    static void Launch(Camera cam, WorldBackdrop wb, EmberDirector d, string dir)
    {
        for (int k = 0; k < 600 && !d.SpawnSite(cam.orthographicSize * .45f, 0); k++) wb.Step(Dt);
        var sites = new List<LandingSite>();
        LandingSites.Collect(sites);
        LandingSite site = default(LandingSite);
        bool have = false;
        foreach (var s in sites) if (s.kind == LandingKind.FoundryHangar) { site = s; have = true; }
        if (!have) { Debug.LogWarning("[EMBER-PREVIEW] no foundry hangar to launch from"); return; }
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
        Debug.Log("[EMBER-PREVIEW] launch tell " + tellShot + " emerge " + emergeShot);
        EliteSystem.Clear();
        Object.DestroyImmediate(dirGo.gameObject);
    }

    // ---- close crops of every piece with loops -------------------------------------------

    static void Pieces(string dir)
    {
        var cam = Scene(3, 3);
        var wb = WorldBackdrop.Create("Ember");
        BackdropVariants.For("Ember").Force = 1;
        wb.Show("Ember", false);
        var d = (EmberDirector)wb.Current.Director;
        while (d.Clock < 16f) wb.Step(Dt);       // past the ceiling
        var names = new List<string>();
        foreach (var p in EmberAmbientCatalog.Pieces)
            if (p.emit != null && p.emit.Length > 0) names.Add(p.name);
        int i = 0;
        foreach (string n in names)
        {
            var p = d.Showcase(n, 1.7f, 0f, 1f, i % 2 == 1);
            if (p == null) { Debug.LogWarning("[EMBER-PREVIEW] cannot show " + n); continue; }
            for (int k = 0; k < 40; k++) wb.Step(Dt);
            Crop(cam, p.root.position, 1.7f * 1.6f, Path.Combine(dir, "piece-" + n + ".png"));
            i++;
        }
        Object.DestroyImmediate(wb.gameObject);
    }

    // ---- plume bases on their craters, measured on the render -------------------------------
    //
    //   smoke-<piece>-m<0|1>.png   8 consecutive loop frames, 4x zoom on the emitter point
    //   [SMOKE] lines              per piece / mirror / loop / frame: the rendered plume's base
    //                              (the diff of the frame with and without that emitter) against
    //                              the host point, in screen px, plus whether the plume reaches
    //                              the top of the render
    static void Smoke(string dir)
    {
        var cam = Scene(3, 3);
        var wb = WorldBackdrop.Create("Ember");
        BackdropVariants.For("Ember").Force = 1;
        wb.Show("Ember", false);
        var d = (EmberDirector)wb.Current.Director;
        while (d.Clock < 16f) wb.Step(Dt);
        string only = System.Environment.GetEnvironmentVariable("EMBER_PREVIEW_PIECES") ?? "";
        float showY = float.TryParse(System.Environment.GetEnvironmentVariable("EMBER_PREVIEW_Y"), out var yy) ? yy : 1f;
        foreach (var piece in EmberAmbientCatalog.Pieces)
        {
            if (piece.emit == null) continue;
            if (only != "" && !only.Contains(piece.name)) continue;
            bool any = false;
            foreach (var e in piece.emit) any |= EmberAmbientCatalog.Plume(e.loop);
            if (!any) continue;
            for (int mirror = 0; mirror < 2; mirror++)
            {
                var p = d.Showcase(piece.name, 1.7f, 0f, showY, mirror == 1);
                if (p == null) continue;
                for (int k = 0; k < 40; k++) wb.Step(Dt);
                for (int slot = 0; slot < piece.emit.Length && slot < EmberAmbientCatalog.MaxPerPiece; slot++)
                {
                    string loop; Vector3 a, h;
                    if (!d.Ambient.Probe(p, slot, out loop, out a, out h) || !EmberAmbientCatalog.Plume(loop)) continue;
                    var sr = p.body.Find("ambient" + slot).GetComponent<SpriteRenderer>();
                    Vector3 hv = cam.WorldToViewportPoint(h);
                    float hx = hv.x * W, hy = hv.y * H;       // y up from the bottom of the render
                    var crops = new List<Texture2D>();
                    for (int f = 0; f < 8; f++)
                    {
                        for (int k = 0; k < 8; k++) wb.Step(Dt);
                        var full = Render(cam, W, H);
                        sr.enabled = false;
                        var without = Render(cam, W, H);
                        sr.enabled = true;
                        var A = full.GetPixels32(); var B = without.GetPixels32();
                        int minX = W, maxX = -1, minY = H, maxY = -1;
                        for (int i = 0; i < A.Length; i++)
                        {
                            int dd = Mathf.Abs(A[i].r - B[i].r) + Mathf.Abs(A[i].g - B[i].g) + Mathf.Abs(A[i].b - B[i].b);
                            if (dd < 24) continue;
                            int x = i % W, y = i / W;
                            if (x < minX) minX = x; if (x > maxX) maxX = x;
                            if (y < minY) minY = y; if (y > maxY) maxY = y;
                        }
                        // the base: the weighted centre of the diff in the lowest 14 px of the plume
                        double sw = 0, sx = 0;
                        for (int y = minY; maxY >= 0 && y < minY + 14; y++)
                            for (int x = minX; x <= maxX; x++)
                            {
                                int i = y * W + x;
                                int dd = Mathf.Abs(A[i].r - B[i].r) + Mathf.Abs(A[i].g - B[i].g) + Mathf.Abs(A[i].b - B[i].b);
                                if (dd < 24) continue;
                                sw += dd; sx += dd * (double)x;
                            }
                        float bx = sw > 0 ? (float)(sx / sw) : -1f;
                        Debug.Log("[SMOKE] " + piece.name + " m" + mirror + " " + loop + " f" + f + " host " + hx.ToString("F1") + "," + hy.ToString("F1") +
                                  " baseX " + bx.ToString("F1") + " baseY " + minY + " offX " + (bx - hx).ToString("F1") + " offY " + (minY - hy).ToString("F1") +
                                  " bbox x " + minX + ".." + maxX + " y " + minY + ".." + maxY + " top " + (maxY >= H - 3 ? "CUT" : "ok") +
                                  " sides " + (minX <= 2 || maxX >= W - 3 ? "CUT" : "ok"));
                        const int half = 70;
                        int cx = Mathf.Clamp(Mathf.RoundToInt(hx) - half, 0, W - 2 * half), cy = Mathf.Clamp(Mathf.RoundToInt(hy) - half + 30, 0, H - 2 * half);
                        var c = new Texture2D(2 * half, 2 * half, TextureFormat.RGB24, false);
                        c.SetPixels(full.GetPixels(cx, cy, 2 * half, 2 * half)); c.Apply();
                        crops.Add(c);
                        Object.DestroyImmediate(full); Object.DestroyImmediate(without);
                    }
                    const int Z = 3, cs = 140 * Z;
                    var sheet = new Texture2D(cs * 4, cs * 2, TextureFormat.RGB24, false);
                    for (int f = 0; f < crops.Count; f++)
                    {
                        var src = crops[f].GetPixels32();
                        for (int y = 0; y < cs; y++)
                            for (int x = 0; x < cs; x++)
                                sheet.SetPixel((f % 4) * cs + x, (1 - f / 4) * cs + y, src[(y / Z) * 140 + x / Z]);
                        Object.DestroyImmediate(crops[f]);
                    }
                    sheet.Apply();
                    File.WriteAllBytes(Path.Combine(dir, "smoke-" + piece.name + "-s" + slot + "-m" + mirror + ".png"), sheet.EncodeToPNG());
                    Object.DestroyImmediate(sheet);
                }
            }
        }
        Object.DestroyImmediate(wb.gameObject);
    }

    // Which pool draws what: at 2 / 4 s of each ground set, the screen with each pool alone hidden.
    static void Pools(string dir)
    {
        var cam = Scene(3, 3);
        var wb = WorldBackdrop.Create("Ember");
        for (int v = 1; v <= 4; v++)
        {
            BackdropVariants.For("Ember").Force = v;
            wb.Show("Space", false);
            wb.Show("Ember", false);
            var d = (EmberDirector)wb.Current.Director;
            foreach (float at in new[] { 2f, 4f })
            {
                while (d.Clock < at) wb.Step(Dt);
                var full = Render(cam, W, H);
                var A = full.GetPixels32();
                foreach (var pool in d.Pools)
                {
                    var on = new List<BackdropPiece>();
                    foreach (var q in pool.items) if (q.active && q.root.gameObject.activeSelf) { on.Add(q); q.root.gameObject.SetActive(false); }
                    var without = Render(cam, W, H);
                    foreach (var q in on) q.root.gameObject.SetActive(true);
                    var B = without.GetPixels32();
                    int minX = W, maxX = -1, minY = H, maxY = -1, n = 0;
                    for (int i = 0; i < A.Length; i++)
                    {
                        int dd = Mathf.Abs(A[i].r - B[i].r) + Mathf.Abs(A[i].g - B[i].g) + Mathf.Abs(A[i].b - B[i].b);
                        if (dd < 12) continue;
                        n++;
                        int x = i % W, y = i / W;
                        if (x < minX) minX = x; if (x > maxX) maxX = x;
                        if (y < minY) minY = y; if (y > maxY) maxY = y;
                    }
                    Debug.Log("[POOLS] v" + v + " " + at + "s " + pool.name + " pieces " + on.Count + " px " + n + " bbox x " + minX + ".." + maxX + " y " + minY + ".." + maxY);
                    if (pool.name == "ceiling" || pool.name == "palls")
                    {
                        var diff = new Texture2D(W, H, TextureFormat.RGB24, false);
                        var o = new Color32[A.Length];
                        for (int i = 0; i < A.Length; i++)
                        {
                            int dd = Mathf.Abs(A[i].r - B[i].r) + Mathf.Abs(A[i].g - B[i].g) + Mathf.Abs(A[i].b - B[i].b);
                            o[i] = dd >= 12 ? new Color32(255, 0, 255, 255) : B[i];
                        }
                        diff.SetPixels32(o); diff.Apply();
                        File.WriteAllBytes(Path.Combine(dir, "pool-" + pool.name + "-v" + v + "-" + at.ToString("00") + "s.png"), diff.EncodeToPNG());
                        Object.DestroyImmediate(diff);
                    }
                    Object.DestroyImmediate(without);
                }
                Object.DestroyImmediate(full);
            }
        }
        Object.DestroyImmediate(wb.gameObject);
    }

    // The same frame with every ground plate hidden (the loops stay): where a plume vanishes behind another piece's plate.
    static void Occlusion(string dir)
    {
        var cam = Scene(3, 3);
        var wb = WorldBackdrop.Create("Ember");
        for (int v = 1; v <= 4; v++)
        {
            BackdropVariants.For("Ember").Force = v;
            wb.Show("Space", false);
            wb.Show("Ember", false);
            var d = (EmberDirector)wb.Current.Director;
            foreach (float at in new[] { 20f, 40f, 70f })
            {
                while (d.Clock < at) wb.Step(Dt);
                Capture(cam, Path.Combine(dir, "occ-v" + v + "-" + at.ToString("00") + "s-normal.png"));
                var hid = new List<SpriteRenderer>();
                foreach (var pool in new[] { d.Ground, d.Fires, d.Pipes, d.Sites })
                    foreach (var q in pool.items)
                        if (q.active && q.sr.enabled) { q.sr.enabled = false; hid.Add(q.sr); }
                Capture(cam, Path.Combine(dir, "occ-v" + v + "-" + at.ToString("00") + "s-plates-hidden.png"));
                foreach (var sr in hid) sr.enabled = true;
                var order = new System.Text.StringBuilder();
                foreach (var pool in new[] { d.Pipes, d.Fires, d.Ground, d.Sites })
                    if (pool.items.Count > 0) order.Append(" " + pool.name + "=" + pool.items[0].sr.sortingOrder);
                Debug.Log("[OCC] v" + v + " " + at + "s plate sorting orders" + order);
            }
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
