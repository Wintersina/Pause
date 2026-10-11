using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Captures every planet world's composed backdrop (and Space's) over many
// seeds, variants and scroll positions, for the placement review: does each
// ground piece sit on ground that makes sense under it?
//
// For each world / variant / seed it advances the real WorldBackdrop (the
// cloud ceiling, mist and wisps switched off so the ground shows) and writes,
// at each capture time,
//
//   <dir>/<World>/v<V>_s<seed>_t<tt>.png   the frame (1080 x 2400 / 2)
//   <dir>/<World>/v<V>_s<seed>_t<tt>.json  every pinned ground piece: name,
//                                          pool, screen footprint, whether the
//                                          world's PieceRule accepts the ground
//                                          under it, the class under its centre,
//                                          and the ground mask sampled on a
//                                          24 px screen grid
//
// A python pass (scratchpad tools/compose_sheets.py) draws the contact sheets.
//
//   PLACEMENT_SHEET_DIR=<dir> [PLACEMENT_SHEET_WORLDS=Frost,Tide] [PLACEMENT_SHEET_SEEDS=3]
//   Unity -batchmode -quit -projectPath Pause -executeMethod BackdropPlacementSheet.Run
public static class BackdropPlacementSheet
{
    const float Dt = 1f / 30f;
    const int W = 1080, H = 2400, Down = 2, Grid = 24;
    const float Speed = .6f;
    static readonly float[] Times = { 2f, 14f, 26f, 38f };
    static readonly string[] AllWorlds = { "Frost", "Verdant", "Ember", "Tide", "Desert", "Space" };

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("PLACEMENT_SHEET_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/PlacementSheet";
        string only = System.Environment.GetEnvironmentVariable("PLACEMENT_SHEET_WORLDS");
        string seedsEnv = System.Environment.GetEnvironmentVariable("PLACEMENT_SHEET_SEEDS");
        int seeds = 3;
        if (!string.IsNullOrEmpty(seedsEnv)) int.TryParse(seedsEnv, out seeds);
        var worlds = string.IsNullOrEmpty(only) ? AllWorlds : only.Split(',');
        int failures = 0;
        using (new TestHarness.Sandbox())
        {
            var saved = Knobs.Save();
            try
            {
                Knobs.Clear();
                foreach (var w in worlds)
                {
                    Directory.CreateDirectory(Path.Combine(dir, w));
                    int variants = w == "Space" ? 1 : BackdropVariants.MaxVariants;
                    for (int v = 1; v <= variants; v++)
                    {
                        if (w != "Space" && !BackdropVariants.For(w).Installed(v)) continue;
                        for (int s = 0; s < seeds; s++) Capture(dir, w, v, 101 + s * 7);
                    }
                }
            }
            catch (System.Exception e) { Debug.LogException(e); failures++; }
            finally
            {
                saved.Restore();
                SetSeed(0);
                foreach (var w in AllWorlds) if (w != "Space") BackdropVariants.For(w).Reset();
                EliteSystem.Clear();
            }
        }
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    // The cloud knobs of every planet world: off, so the ground is in view from the start.
    class Knobs
    {
        float[] v;
        public static Knobs Save()
        {
            return new Knobs { v = new[] {
                FrostTuning.CeilingHold, FrostTuning.CeilingClearSeconds, FrostTuning.CloudDensity,
                VerdantTuning.CeilingHold, VerdantTuning.CeilingClearSeconds, VerdantTuning.CloudDensity,
                EmberTuning.CeilingHold, EmberTuning.CeilingClearSeconds, EmberTuning.CloudDensity,
                TideTuning.CeilingHold, TideTuning.CeilingClearSeconds, TideTuning.CloudDensity,
                DesertTuning.CeilingHold, DesertTuning.CeilingClearSeconds, DesertTuning.CloudDensity } };
        }
        public static void Clear()
        {
            FrostTuning.CeilingHold = VerdantTuning.CeilingHold = EmberTuning.CeilingHold = TideTuning.CeilingHold = DesertTuning.CeilingHold = .1f;
            FrostTuning.CeilingClearSeconds = VerdantTuning.CeilingClearSeconds = EmberTuning.CeilingClearSeconds =
                TideTuning.CeilingClearSeconds = DesertTuning.CeilingClearSeconds = .7f;
            FrostTuning.CloudDensity = VerdantTuning.CloudDensity = EmberTuning.CloudDensity = TideTuning.CloudDensity = DesertTuning.CloudDensity = 0f;
        }
        public void Restore()
        {
            int i = 0;
            FrostTuning.CeilingHold = v[i++]; FrostTuning.CeilingClearSeconds = v[i++]; FrostTuning.CloudDensity = v[i++];
            VerdantTuning.CeilingHold = v[i++]; VerdantTuning.CeilingClearSeconds = v[i++]; VerdantTuning.CloudDensity = v[i++];
            EmberTuning.CeilingHold = v[i++]; EmberTuning.CeilingClearSeconds = v[i++]; EmberTuning.CloudDensity = v[i++];
            TideTuning.CeilingHold = v[i++]; TideTuning.CeilingClearSeconds = v[i++]; TideTuning.CloudDensity = v[i++];
            DesertTuning.CeilingHold = v[i++]; DesertTuning.CeilingClearSeconds = v[i++]; DesertTuning.CloudDensity = v[i++];
        }
    }

    static void SetSeed(int seed)
    {
        FrostDirector.SeedOverride = seed;
        VerdantDirector.SeedOverride = seed;
        EmberDirector.SeedOverride = seed;
        TideDirector.SeedOverride = seed;
        DesertDirector.SeedOverride = seed;
    }

    public static PieceRule RuleFor(string world, int variant, string name)
    {
        switch (world)
        {
            case "Frost": return FrostTuning.Rule(name, variant);
            case "Verdant": return VerdantTuning.Rule(name);
            case "Ember": return EmberTuning.Rule(name);
            case "Tide": return TideTuning.Rule(name, variant >= 0 && variant < TideTuning.OpenSea.Length && TideTuning.OpenSea[variant]);
            case "Desert": return DesertTuning.Rule(name);
        }
        return new PieceRule(GroundClass.Any, 0f, .2f);
    }

    static GroundPlanner PlannerOf(BackdropSet set, string world)
    {
        var d = set.Director;
        var prop = d != null ? d.GetType().GetProperty("Planner") : null;
        var p = prop != null ? prop.GetValue(d) as GroundPlanner : null;
        if (p != null) return p;
        var mask = GroundMask.Load(world, set.Variant);
        return mask != null ? new GroundPlanner(set, "mid", mask) : null;
    }

    static void Capture(string dir, string world, int variant, int seed)
    {
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        EliteSystem.Clear();
        Time.timeScale = 1f;
        moveBackGround.speed = Speed;
        SetSeed(seed);
        if (world != "Space")
        {
            var vars = BackdropVariants.For(world);
            vars.Reset();
            vars.Seed(seed);
            vars.Force = variant;
        }
        var cam = Camera.main;
        cam.orthographic = true;
        cam.aspect = W / (float)H;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, W, H);
        cam.transform.position = new Vector3(0f, 0f, -10f);
        var wb = WorldBackdrop.Create(world);
        wb.Show(world, false);
        var set = wb.Current;
        var planner = PlannerOf(set, world);
        float t = 0f;
        foreach (float at in Times)
        {
            while (t < at - 1e-4f) { wb.Step(Dt); t += Dt; }
            string stem = Path.Combine(dir, world, "v" + variant + "_s" + seed + "_t" + at.ToString("00"));
            var tex = Render(cam);
            File.WriteAllBytes(stem + ".png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            File.WriteAllText(stem + ".json", Describe(cam, set, world, variant, planner));
        }
        Object.DestroyImmediate(wb.gameObject);
    }

    static string Describe(Camera cam, BackdropSet set, string world, int variant, GroundPlanner planner)
    {
        var sb = new StringBuilder();
        sb.Append("{\"world\":\"").Append(world).Append("\",\"variant\":").Append(variant).Append(",\"w\":").Append(W / Down)
          .Append(",\"h\":").Append(H / Down).Append(",\"pieces\":[");
        bool first = true;
        var pd = set.Director as PlanetDirector;
        if (pd != null)
        {
            int poolIndex = 0;
            foreach (var pool in pd.Landmarks)
            {
                poolIndex++;
                foreach (var p in pool.items)
                {
                    if (!p.active || p.sr.sprite == null || !p.sr.enabled) continue;
                    string name = p.sr.sprite.name;
                    var rule = RuleFor(world, variant, name);
                    double gy = planner != null ? planner.GroundY(p.y) : 0.0;
                    bool ok = planner == null || planner.Accepts(rule, p.x, gy, p.size);
                    float match = planner != null ? planner.Match(p.x, gy, GroundPlanner.HalfX(rule, p.size), GroundPlanner.HalfY(rule, p.size), rule.on) : 1f;
                    string cls = planner != null ? planner.ClassAt(p.x, gy).ToString() : "-";
                    float rx = GroundPlanner.HalfX(rule, p.size), ry = GroundPlanner.HalfY(rule, p.size);
                    var a = Px(cam, set, p.x - rx, p.y - ry);
                    var b = Px(cam, set, p.x + rx, p.y + ry);
                    var bb = p.sr.bounds;
                    var s0 = Px(cam, bb.min);
                    var s1 = Px(cam, bb.max);
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"n\":\"").Append(name).Append("\",\"pool\":").Append(poolIndex).Append(",\"kind\":").Append(p.kind)
                      .Append(",\"ok\":").Append(ok ? "true" : "false").Append(",\"match\":").Append(match.ToString("F2"))
                      .Append(",\"need\":").Append(rule.need.ToString("F2")).Append(",\"on\":\"").Append(rule.on).Append("\"")
                      .Append(",\"cls\":\"").Append(cls).Append("\",\"moving\":").Append(p.vx != 0f || p.vy != 0f ? "true" : "false")
                      .Append(",\"foot\":[").Append(a.x).Append(',').Append(b.y).Append(',').Append(b.x).Append(',').Append(a.y).Append(']')
                      .Append(",\"box\":[").Append(s0.x).Append(',').Append(s1.y).Append(',').Append(s1.x).Append(',').Append(s0.y).Append("]}");
                }
            }
        }
        sb.Append("],\"grid\":").Append(Grid / Down).Append(",\"mask\":[");
        if (planner != null && planner.Mask != null)
        {
            for (int gy = 0; gy < H / Grid; gy++)
            {
                if (gy > 0) sb.Append(',');
                sb.Append('"');
                for (int gx = 0; gx < W / Grid; gx++)
                {
                    // screen pixel (top-left origin) -> set-root local
                    var world0 = cam.ViewportToWorldPoint(new Vector3((gx + .5f) * Grid / W, 1f - (gy + .5f) * Grid / H, 10f));
                    var local = set.Root.InverseTransformPoint(world0);
                    var c = planner.ClassAt(local.x, planner.GroundY(local.y));
                    sb.Append(c == GroundClass.Water ? 'W' : c == GroundClass.Built ? 'B' : c == GroundClass.Open ? 'O' : 'C');
                }
                sb.Append('"');
            }
        }
        sb.Append("]}");
        return sb.ToString();
    }

    // Set-root local point -> downscaled screen pixel (top-left origin).
    static Vector2Int Px(Camera cam, BackdropSet set, float x, float y) { return Px(cam, set.Root.TransformPoint(new Vector3(x, y, 0f))); }

    static Vector2Int Px(Camera cam, Vector3 world)
    {
        Vector3 v = cam.WorldToViewportPoint(world);
        return new Vector2Int(Mathf.RoundToInt(v.x * W / Down), Mathf.RoundToInt((1f - v.y) * H / Down));
    }

    static Texture2D Render(Camera cam)
    {
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previous = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        var small = RenderTexture.GetTemporary(W / Down, H / Down, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(rt, small);
        var old = RenderTexture.active;
        RenderTexture.active = small;
        var png = new Texture2D(W / Down, H / Down, TextureFormat.RGB24, false);
        png.ReadPixels(new Rect(0, 0, W / Down, H / Down), 0, 0);
        png.Apply();
        RenderTexture.active = old;
        cam.targetTexture = previous;
        RenderTexture.ReleaseTemporary(small);
        Object.DestroyImmediate(rt);
        return png;
    }
}
