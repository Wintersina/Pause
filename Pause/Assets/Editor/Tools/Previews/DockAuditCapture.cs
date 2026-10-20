using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Elite-dock audit: for every planet world and ground set (variant) it asks the real backdrop for each launch-site kind in turn
// (the director's SpawnSite), waits until that dock is in the upper-middle of the view, and writes
//
//   <dir>/<World>/v<V>_<kind>.png        the whole frame (540 x 1200)
//   <dir>/<World>/v<V>_<kind>_crop.png   a 360 px crop around the dock
//   <dir>/log.txt                        per world / variant / kind: spawn ok?, the most docks ever visible at once, ground class under the dock
//
//   DOCK_AUDIT_DIR=<dir> [DOCK_AUDIT_WORLDS=Frost,Tide] [DOCK_AUDIT_VARIANTS=1,2]
//   Unity -batchmode -quit -projectPath Pause -executeMethod DockAuditCapture.Run
public static class DockAuditCapture
{
    const float Dt = 1f / 30f;
    const int W = 1080, H = 2400, Down = 2;
    static readonly string[] AllWorlds = { "Frost", "Verdant", "Ember", "Tide", "Storm", "Desert" };

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("DOCK_AUDIT_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/DockAudit";
        string only = System.Environment.GetEnvironmentVariable("DOCK_AUDIT_WORLDS");
        string onlyV = System.Environment.GetEnvironmentVariable("DOCK_AUDIT_VARIANTS");
        var worlds = string.IsNullOrEmpty(only) ? AllWorlds : only.Split(',');
        Directory.CreateDirectory(dir);
        var log = new StringBuilder();
        int failures = 0;
        using (new TestHarness.Sandbox())
        {
            DockGrade.Strict = true;
            try
            {
                foreach (var w in worlds)
                {
                    if (w == "Space") { try { SpaceAudit(dir, log); } catch (System.Exception e) { Debug.LogException(e); failures++; } continue; }
                    Directory.CreateDirectory(Path.Combine(dir, w));
                    for (int v = 1; v <= BackdropVariants.MaxVariants; v++)
                    {
                        if (!string.IsNullOrEmpty(onlyV) && System.Array.IndexOf(onlyV.Split(','), v.ToString()) < 0) continue;
                        if (!BackdropVariants.For(w).Installed(v)) continue;
                        try { Audit(dir, w, v, log); }
                        catch (System.Exception e) { Debug.LogException(e); log.AppendLine(w + " v" + v + " EXCEPTION " + e.Message); failures++; }
                    }
                }
            }
            finally
            {
                foreach (var w in AllWorlds) BackdropVariants.For(w).Reset();
                EliteSystem.Clear();
            }
        }
        File.AppendAllText(Path.Combine(dir, "log.txt"), log.ToString());
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    // Space has no ground: its docks drift through the sky. One at a time; a still of each kind in the upper view.
    static void SpaceAudit(string dir, StringBuilder log)
    {
        Directory.CreateDirectory(Path.Combine(dir, "Space"));
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        EliteSystem.Clear();
        Time.timeScale = 1f;
        moveBackGround.speed = .6f;
        var cam = Camera.main;
        cam.orthographic = true;
        cam.aspect = W / (float)H;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, W, H);
        cam.transform.position = new Vector3(0f, 0f, -10f);
        var wb = WorldBackdrop.Create("Space");
        wb.Show("Space", false);
        var d = (SpaceDirector)wb.Current.Director;
        float t = 0f;
        while (t < 6f) { wb.Step(Dt); t += Dt; }
        string[] keys = { "gantry", "carrier", "gate", "derelict" };
        int maxSeen = 0;
        foreach (var key in keys)
        {
            bool shot = false;
            for (int n = 0; n < 30 * 200 && !shot; n++)
            {
                wb.Step(Dt);
                int vis = 0; BackdropPiece mine = null;
                foreach (var p in d.Docks.items) if (p.active) { vis++; if (SpaceDirector.DockDefs[d.DockTypeOf(p)].launchKey == key) mine = p; }
                maxSeen = Mathf.Max(maxSeen, vis);
                if (mine == null) { if (n % 30 == 0) LandingSites.BringHome(key); continue; }
                if (mine.y > 1.0f || mine.y < -0.5f) continue;
                var tex = Render(cam);
                File.WriteAllBytes(Path.Combine(dir, "Space", "v1_" + key + ".png"), Scaled(tex, Down).EncodeToPNG());
                var b = mine.sr.bounds;
                Vector3 c = cam.WorldToViewportPoint(b.center); c.x *= W; c.y *= H;
                int cs = 720, x0 = Mathf.Clamp((int)c.x - cs / 2, 0, W - cs), y0 = Mathf.Clamp((int)c.y - cs / 2, 0, H - cs);
                var crop = new Texture2D(cs, cs, TextureFormat.RGB24, false);
                crop.SetPixels(tex.GetPixels(x0, y0, cs, cs)); crop.Apply();
                File.WriteAllBytes(Path.Combine(dir, "Space", "v1_" + key + "_crop.png"), Scaled(crop, 2).EncodeToPNG());
                Object.DestroyImmediate(crop); Object.DestroyImmediate(tex);
                log.AppendLine("Space v1 " + key + " x=" + mine.x.ToString("0.00") + " y=" + mine.y.ToString("0.00") + " docksInSky=" + vis);
                shot = true;
            }
            if (!shot) log.AppendLine("Space v1 " + key + " NOT SEEN");
        }
        log.AppendLine("Space MAX DOCKS IN SKY AT ONCE = " + maxSeen);
        Object.DestroyImmediate(wb.gameObject);
    }

    static void Set(System.Type t, string f, float v) { var fi = t.GetField(f); if (fi != null) fi.SetValue(null, v); }
    static System.Type T(string n) { return System.Type.GetType(n + ", Assembly-CSharp"); }

    static void Audit(string dir, string world, int variant, StringBuilder log)
    {
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        EliteSystem.Clear();
        Time.timeScale = 1f;
        moveBackGround.speed = .6f;
        var vars = BackdropVariants.For(world);
        vars.Reset();
        vars.Force = variant;
        var cam = Camera.main;
        cam.orthographic = true;
        cam.aspect = W / (float)H;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, W, H);
        cam.transform.position = new Vector3(0f, 0f, -10f);
        var tun = T(world + "Tuning");
        Set(tun, "CeilingHold", .1f); Set(tun, "CeilingClearSeconds", .7f); Set(tun, "CloudDensity", 0f);
        var wb = WorldBackdrop.Create(world);
        wb.Show(world, false);
        var set = wb.Current;
        var d = set.Director;
        var dt = d.GetType();
        var sitesProp = dt.GetProperty("Sites");
        var spawn = dt.GetMethod("SpawnSite");
        var names = (string[])tun.GetField("SiteClosed").GetValue(null);
        var kinds = (LandingKind[])tun.GetField("SiteKinds").GetValue(null);
        var pool = (BackdropPool)sitesProp.GetValue(d);
        float t = 0f;
        while (t < 14f) { wb.Step(Dt); t += Dt; }
        int maxSeen = 0;
        for (int k = 0; k < names.Length; k++)
        {
            bool shot = false, spawned = false;
            for (int n = 0; n < 30 * 120 && !shot; n++)
            {
                wb.Step(Dt);
                int vis = 0;
                BackdropPiece mine = null;
                foreach (var p in pool.items) if (p.active && p.y > -set.HalfHeight && p.y < set.HalfHeight) { vis++; if (p.kind == k) mine = p; }
                maxSeen = Mathf.Max(maxSeen, vis);
                if (mine == null)
                {
                    if (!spawned || n % 15 == 0) spawned |= (bool)spawn.Invoke(d, new object[] { float.NaN, k });
                    continue;
                }
                spawned = true;
                if (mine.y > 1.2f || mine.y < -0.5f) continue;
                string stem = Path.Combine(dir, world, "v" + variant + "_" + names[k]);
                var tex = Render(cam);
                mine.sr.enabled = false;
                var bare = Render(cam);
                mine.sr.enabled = true;
                string lum = Luma(tex, bare);
                Object.DestroyImmediate(bare);
                File.WriteAllBytes(stem + ".png", Scaled(tex, Down).EncodeToPNG());
                var b = mine.sr.bounds;
                Vector3 c = cam.WorldToViewportPoint(new Vector3(b.center.x, b.min.y + b.size.y * .35f, 0f)); c.x *= W; c.y *= H;
                int cs = 720, x0 = Mathf.Clamp((int)c.x - cs / 2, 0, W - cs), y0 = Mathf.Clamp((int)c.y - cs / 2, 0, H - cs);
                var crop = new Texture2D(cs, cs, TextureFormat.RGB24, false);
                crop.SetPixels(tex.GetPixels(x0, y0, cs, cs));
                crop.Apply();
                File.WriteAllBytes(stem + "_crop.png", Scaled(crop, 2).EncodeToPNG());
                Object.DestroyImmediate(crop);
                Object.DestroyImmediate(tex);
                var planner = dt.GetProperty("Planner") != null ? dt.GetProperty("Planner").GetValue(d) as GroundPlanner : null;
                string cls = planner != null ? planner.ClassAt(mine.x, planner.GroundY(mine.y)).ToString() : "?";
                log.AppendLine(world + " v" + variant + " " + names[k] + " kind=" + kinds[k] + " x=" + mine.x.ToString("0.00") + " y=" + mine.y.ToString("0.00") +
                               " size=" + mine.size.ToString("0.00") + " class=" + cls + " visibleDocks=" + vis + " " + lum);
                shot = true;
            }
            if (!shot) log.AppendLine(world + " v" + variant + " " + names[k] + " NOT SPAWNED (spawn ok=" + spawned + ")");
        }
        log.AppendLine(world + " v" + variant + " MAX DOCKS VISIBLE AT ONCE = " + maxSeen);
        // natural spawning for 120 s: how many docks are ever visible together?
        int maxNatural = 0;
        Object.DestroyImmediate(wb.gameObject);
        wb = WorldBackdrop.Create(world);
        wb.Show(world, false);
        set = wb.Current; d = set.Director;
        pool = (BackdropPool)d.GetType().GetProperty("Sites").GetValue(d);
        var list = new List<LandingSite>();
        for (t = 0f; t < 150f; t += Dt)
        {
            wb.Step(Dt);
            int vis = 0;
            foreach (var p in pool.items) if (p.active && p.y > -set.HalfHeight && p.y < set.HalfHeight) vis++;
            maxNatural = Mathf.Max(maxNatural, vis);
        }
        log.AppendLine(world + " v" + variant + " natural 150 s: max docks visible together = " + maxNatural);
        Object.DestroyImmediate(wb.gameObject);
    }

    // Mean colour of the dock's own pixels (where the frame differs from the same frame without it) and of the ring of ground round it.
    static string Luma(Texture2D with, Texture2D bare)
    {
        var a = with.GetPixels32(); var b = bare.GetPixels32();
        double[] dk = new double[3], rg = new double[3]; long nd = 0, nr = 0;
        int minX = W, maxX = 0, minY = H, maxY = 0;
        for (int y = 0; y < H; y += 2)
            for (int x = 0; x < W; x += 2)
            {
                int i = y * W + x;
                int df = Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b);
                if (df > 24) { minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
            }
        if (maxX <= minX) return "dock=none";
        int cx = (minX + maxX) / 2, cy = (minY + maxY) / 2, hw = (maxX - minX) / 2, hh = (maxY - minY) / 2;
        for (int y = Mathf.Max(0, cy - hh * 2); y < Mathf.Min(H, cy + hh * 2); y += 2)
            for (int x = Mathf.Max(0, cx - hw * 2); x < Mathf.Min(W, cx + hw * 2); x += 2)
            {
                int i = y * W + x;
                int df = Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b);
                if (df > 24) { dk[0] += a[i].r; dk[1] += a[i].g; dk[2] += a[i].b; nd++; }
                else if (Mathf.Abs(x - cx) > hw * 1.15f || Mathf.Abs(y - cy) > hh * 1.15f) { rg[0] += a[i].r; rg[1] += a[i].g; rg[2] += a[i].b; nr++; }
            }
        if (nd == 0 || nr == 0) return "dock=none";
        for (int c = 0; c < 3; c++) { dk[c] /= nd * 255.0; rg[c] /= nr * 255.0; }
        return "dockRGB=" + dk[0].ToString("0.000") + "," + dk[1].ToString("0.000") + "," + dk[2].ToString("0.000") +
               " ringRGB=" + rg[0].ToString("0.000") + "," + rg[1].ToString("0.000") + "," + rg[2].ToString("0.000");
    }

    static Texture2D Scaled(Texture2D src, int down)
    {
        int w = src.width / down, h = src.height / down;
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(src, rt);
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var o = new Texture2D(w, h, TextureFormat.RGB24, false);
        o.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        o.Apply();
        RenderTexture.active = old;
        RenderTexture.ReleaseTemporary(rt);
        return o;
    }

    static Texture2D Render(Camera cam)
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
        RenderTexture.active = old;
        cam.targetTexture = previous;
        Object.DestroyImmediate(rt);
        return png;
    }
}
