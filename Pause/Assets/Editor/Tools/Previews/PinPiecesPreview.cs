using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// EVIDENCE that backdrop overlay pieces (landmarks, launch sites, pipes, vehicles) are PINNED to the ground tile.
//
//   PIN_DIR=<dir> Unity -batchmode -quit -projectPath Pause -executeMethod PinPiecesPreview.Run
//
// pin-data.csv   for every world and installed variant, 6 s of the real WorldBackdrop at 30 fps: every active piece of every
//                pinned pool, its worst drift against the ground (y + the mid tile's unwrapped travel, and x) in world units.
// convoy-strip   Frost's convoy (the caravan) over 6 s, cropped around it: MAGENTA cross = the ground spot it started on
//                (scrolled with the mid tile), CYAN ring = the piece now. Pinned: ring stays on the cross.
public static class PinPiecesPreview
{
    const float Dt = 1f / 30f;
    const int W = 1080, H = 2400, Crop = 420;
    static readonly string[] Worlds = { "Frost", "Verdant", "Ember", "Tide", "Storm", "Desert" };

    class Track { public string pool, sprite; public double gy0; public float x0; public double dy, dx; public bool moving; public int frames; }

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("PIN_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/PinPiecesPreview";
        Directory.CreateDirectory(dir);
        int failures = 0;
        float hold = FrostTuning.CeilingHold, clear = FrostTuning.CeilingClearSeconds, dens = FrostTuning.CloudDensity;
        using (new TestHarness.Sandbox())
        {
            try
            {
                FrostTuning.CeilingHold = .1f; FrostTuning.CeilingClearSeconds = .7f; FrostTuning.CloudDensity = 0f;
                var csv = new StringBuilder("world,variant,pool,sprite,moving,frames,maxDriftY,maxDriftX\n");
                foreach (string w in Worlds) Data(w, csv);
                File.WriteAllText(Path.Combine(dir, "pin-data.csv"), csv.ToString());
                Convoy(dir);
            }
            catch (System.Exception e) { Debug.LogException(e); failures++; }
            finally
            {
                FrostTuning.CeilingHold = hold; FrostTuning.CeilingClearSeconds = clear; FrostTuning.CloudDensity = dens;
                foreach (string w in Worlds) BackdropVariants.For(w).Reset();
                EliteSystem.Clear();
            }
        }
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    static WorldBackdrop Fresh(string world, int variant, int seed)
    {
        EliteSystem.Clear();
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        Time.timeScale = 1f;
        moveBackGround.speed = .25f;
        var vars = BackdropVariants.For(world);
        vars.Reset();
        vars.Seed(seed);
        vars.Force = variant;
        var cam = Camera.main;
        cam.orthographic = true;
        cam.aspect = W / (float)H;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, W, H);
        cam.transform.position = new Vector3(0f, 0f, -10f);
        var wb = WorldBackdrop.Create(world);
        wb.Show(world, false);
        return wb;
    }

    static BackdropTile Mid(BackdropSet set)
    {
        foreach (var t in set.Tiles) if (t.layer.name == "mid") return t;
        return null;
    }

    static void Data(string world, StringBuilder csv)
    {
        var spec = BackdropCatalog.For(world);
        for (int v = 1; v <= Mathf.Max(1, spec.variantSets); v++)
        {
            if (spec.variantSets > 0 && !BackdropVariants.For(world).Installed(v)) continue;
            var wb = Fresh(world, v, 11);
            var set = wb.Current;
            var mid = Mid(set);
            var tracks = new Dictionary<BackdropPiece, Track>();
            var pools = new List<BackdropPool>();
            foreach (var pool in set.Director.Pools)
                foreach (var l in spec.layers)
                    if (l.name == pool.name && !string.IsNullOrEmpty(l.pinTo)) pools.Add(pool);
            for (int i = 0; i < 40; i++) wb.Step(Dt);
            for (int i = 0; i < 180; i++)
            {
                wb.Step(Dt);
                foreach (var pool in pools)
                    foreach (var p in pool.items)
                    {
                        Track tr;
                        if (!p.active || p.parent != null) { tracks.Remove(p); continue; }
                        Vector3 pos = p.root.position;
                        double gy = pos.y + mid.travel;
                        if (!tracks.TryGetValue(p, out tr) || p.age < Dt * 1.5f)
                        {
                            tracks[p] = new Track { pool = pool.name, sprite = p.sr.sprite != null ? p.sr.sprite.name : "?", gy0 = gy, x0 = pos.x };
                            continue;
                        }
                        tr.frames++;
                        if (p.vx != 0f || p.vy != 0f) tr.moving = true;
                        tr.dy = System.Math.Max(tr.dy, System.Math.Abs(gy - tr.gy0));
                        tr.dx = System.Math.Max(tr.dx, System.Math.Abs(pos.x - tr.x0));
                    }
            }
            double worstStill = 0, worstMoving = 0; int n = 0, moving = 0;
            foreach (var tr in tracks.Values)
            {
                if (tr.frames < 5) continue;
                csv.Append(world).Append(',').Append(v).Append(',').Append(tr.pool).Append(',').Append(tr.sprite).Append(',').Append(tr.moving ? 1 : 0)
                   .Append(',').Append(tr.frames).Append(',').Append(tr.dy.ToString("F4")).Append(',').Append(tr.dx.ToString("F4")).Append('\n');
                n++;
                double d = System.Math.Max(tr.dy, tr.dx);
                if (tr.moving) { moving++; worstMoving = System.Math.Max(worstMoving, d); } else worstStill = System.Math.Max(worstStill, d);
            }
            Debug.Log("[PIN] " + world + " v" + v + ": " + n + " pieces tracked, " + moving + " self-moving; worst drift still " + worstStill.ToString("F4") +
                      " u, self-moving " + worstMoving.ToString("F4") + " u");
            Object.DestroyImmediate(wb.gameObject);
        }
    }

    // ------------------------------------------------------------ Frost convoy strip --

    static void Convoy(string dir)
    {
        for (int seed = 1; seed <= 24; seed++)
            for (int v = 1; v <= 4; v++)
            {
                if (!BackdropVariants.For("Frost").Installed(v)) continue;
                if (TryConvoy(dir, v, seed)) return;
            }
        Debug.Log("[PIN] no convoy appeared in 24 seeds x 4 variants");
    }

    static bool TryConvoy(string dir, int variant, int seed)
    {
        FrostDirector.SeedOverride = seed;
        var wb = Fresh("Frost", variant, seed);
        var fd = (FrostDirector)wb.Current.Director;
        var mid = Mid(wb.Current);
        var cam = Camera.main;
        bool needMoving = System.Environment.GetEnvironmentVariable("PIN_REQUIRE_MOVING") == "1";
        BackdropPiece pick = null;
        for (int i = 0; i < 30 * 70 && pick == null; i++)
        {
            wb.Step(Dt);
            foreach (var p in fd.Ground.items)
                if (p.active && p.sr.sprite != null && p.sr.sprite.name.StartsWith("convoy") && p.root.position.y > 3.5f && p.root.position.y < 7f && (!needMoving || p.vx != 0f)) pick = p;
        }
        FrostDirector.SeedOverride = 0;
        if (pick == null) { Object.DestroyImmediate(wb.gameObject); return false; }
        Vector3 start = pick.root.position;
        double gy0 = start.y + mid.travel;
        Debug.Log("[PIN] convoy found: v" + variant + " seed " + seed + " " + pick.sr.sprite.name + " at " + start + " vx=" + pick.vx + " vy=" + pick.vy);
        const int N = 6;
        var crops = new Texture2D[N];
        var log = new StringBuilder();
        float t0 = 0f;
        for (int k = 0; k < N; k++)
        {
            float target = k * 1.2f;
            while (t0 < target - 1e-4f) { wb.Step(Dt); t0 += Dt; }
            var tex = Render(cam);
            Vector3 cross = new Vector3(start.x, (float)(gy0 - mid.travel), 0f);
            Vector3 item = pick.root.position;
            Cross(tex, cam, cross, new Color(1f, 0f, 1f, 1f), 30);
            Ring(tex, cam, item, new Color(0f, 1f, 1f, 1f), 22);
            float dy = item.y - cross.y, dx = item.x - cross.x;
            Debug.Log("[PIN] convoy t=" + target.ToString("F1") + " item-vs-ground offset x=" + dx.ToString("F3") + " y=" + dy.ToString("F3") + " (active " + pick.active + ")");
            var c = Px(cam, cross);
            int x0 = Mathf.Clamp(c.x - Crop / 2, 0, W - Crop), y0 = Mathf.Clamp(c.y - Crop / 2, 0, H - Crop);
            var crop = new Texture2D(Crop, Crop, TextureFormat.RGB24, false);
            crop.SetPixels(tex.GetPixels(x0, y0, Crop, Crop));
            crop.Apply();
            crops[k] = crop;
            Object.DestroyImmediate(tex);
        }
        var strip = new Texture2D(Crop * N + 6 * (N - 1), Crop, TextureFormat.RGB24, false);
        var white = new Color[strip.width * Crop];
        for (int i = 0; i < white.Length; i++) white[i] = Color.white;
        strip.SetPixels(white);
        for (int k = 0; k < N; k++) { strip.SetPixels(k * (Crop + 6), 0, Crop, Crop, crops[k].GetPixels()); Object.DestroyImmediate(crops[k]); }
        strip.Apply();
        File.WriteAllBytes(Path.Combine(dir, "convoy-strip.png"), strip.EncodeToPNG());
        Object.DestroyImmediate(strip);
        Object.DestroyImmediate(wb.gameObject);
        return true;
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

    static Vector2Int Px(Camera cam, Vector3 w)
    {
        Vector3 v = cam.WorldToViewportPoint(w);
        return new Vector2Int(Mathf.RoundToInt(v.x * W), Mathf.RoundToInt(v.y * H));
    }

    static void Dot(Texture2D t, int x, int y, Color c)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        t.SetPixel(x, y, Color.Lerp(t.GetPixel(x, y), new Color(c.r, c.g, c.b), c.a));
    }

    static void Cross(Texture2D t, Camera cam, Vector3 w, Color c, int r)
    {
        var p = Px(cam, w);
        for (int i = -r; i <= r; i++)
            for (int d = -2; d <= 2; d++) { Dot(t, p.x + i, p.y + d, c); Dot(t, p.x + d, p.y + i, c); }
        t.Apply();
    }

    static void Ring(Texture2D t, Camera cam, Vector3 w, Color c, int r)
    {
        var p = Px(cam, w);
        for (int a = 0; a < 720; a++)
        {
            float ang = a * Mathf.PI / 360f;
            for (int d = -2; d <= 0; d++)
                Dot(t, p.x + Mathf.RoundToInt(Mathf.Cos(ang) * (r + d)), p.y + Mathf.RoundToInt(Mathf.Sin(ang) * (r + d)), c);
        }
        t.Apply();
    }
}
