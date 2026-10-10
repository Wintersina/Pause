using System.Collections.Generic;
using System.IO;
using UnityEngine;

// "Parts of the rails are transparent like the first world; Frost shows shadowing around the rails."
// For every world, at a 1170x2532 phone screen with the world's own backdrop, the rail is rendered:
//   art    rails alone with every edge treatment off (the PNG's own alpha), on black and on white
//   game   the same with WorldPainter's real settings
//   plate  the backdrop alone, and the full frame (backdrop + rails)
// Matte alpha = 1 - (onWhite - onBlack). Asserted per world:
//   leak   pixels the ART leaves transparent (a < .02) that the game paints (a > .05): < LeakMax of the screen
//   halo   within HaloPx of the opaque art (a > .5), the backdrop's luminance drop (plate - frame, over the art's
//          transparent pixels) minus the same far from the rails, <= HaloMax, and no worse than Space's + HaloSlack
// Set RAIL_TRANSP_OUT=<dir> to also write <world>-{before,after}.png (before = the old 60% near-black shadow fill).
public static class RailTransparencyTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[RAILTRANSP] PASS  " : "[RAILTRANSP] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public const int W = 1170, H = 2532, HaloPx = 16;
    public const float LeakMax = .0002f, HaloMax = .01f, HaloSlack = .004f;

    struct Result { public float transparentShare, leak, halo; }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        string outDir = System.Environment.GetEnvironmentVariable("RAIL_TRANSP_OUT");
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
        var res = new Dictionary<string, Result>();
        try
        {
            foreach (var theme in WorldManager.Worlds)
            {
                if (WorldPainter.RailTextureName(theme.displayName) == null) continue;
                var r = MeasureWorld(theme, outDir);
                res[theme.displayName] = r;
                Debug.Log("[RAILTRANSP] " + theme.displayName + ": transparent share of rail footprint " + r.transparentShare.ToString("P1") +
                          ", leak " + r.leak.ToString("P3") + ", halo luminance drop " + r.halo.ToString("F4"));
            }
        }
        finally { BossRails.Reset(); PlayField.Reset(); ScreenInfo.ClearOverride(); }
        foreach (var kv in res)
        {
            Check(kv.Key + ": art-transparent pixels stay transparent in the game (leak " + kv.Value.leak.ToString("P3") + " <= " + LeakMax.ToString("P2") + ")", kv.Value.leak <= LeakMax);
            Check(kv.Key + ": no dark halo around the rail (drop " + kv.Value.halo.ToString("F4") + " <= " + HaloMax + ", Space " + res["Space"].halo.ToString("F4") + " + " + HaloSlack + ")",
                  kv.Value.halo <= HaloMax && kv.Value.halo <= res["Space"].halo + HaloSlack);
        }
        Debug.Log("[RAILTRANSP] failures: " + fails);
        return fails;
    }

    static Result MeasureWorld(WorldTheme theme, string outDir)
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity");
        BossRails.Reset();
        RailInset.Enabled = true; RailInset.WidenLane = true;
        var cam = Camera.main;
        cam.aspect = (float)W / H;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, W, H);
        var d = FitDevice.Find("iphone-13");
        ScreenInfo.ClearOverride();
        ScreenInfo.Override(W, H, d.Safe, d.Cutouts, d.ReportedDpi, d.ios);
        PlayField.Reset();
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, System.Array.IndexOf(WorldManager.Worlds, theme));
        var wb = new GameObject("~RtBackdrop").AddComponent<WorldBackdrop>();
        WorldPainter.Apply(theme);
        var walls = new[] { GameObject.Find("leftPipe"), GameObject.Find("rightPipe") };
        foreach (var wall in walls)
        {
            var s = wall.transform.localScale;
            s.y = cam.orthographicSize * 2f * 1.085f / wall.GetComponent<MeshFilter>().sharedMesh.bounds.size.y;
            wall.transform.localScale = s;
            RailFit.RefreshTextureTiling(wall);
        }
        BossRails.Measure();
        wb.Show(theme.displayName, false);
        for (int i = 0; i < 30; i++) wb.Step(1f / 60f);

        var mats = new[] { walls[0].GetComponent<Renderer>().sharedMaterial, walls[1].GetComponent<Renderer>().sharedMaterial };
        var flags = cam.clearFlags; var bg = cam.backgroundColor;
        var others = new List<Renderer>();
        foreach (var rr in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            if (rr.enabled && rr.gameObject != walls[0] && rr.gameObject != walls[1]) others.Add(rr);

        var frame = Render(cam);                                        // backdrop + rails (the game)
        if (!string.IsNullOrEmpty(outDir)) Save(frame, outDir + "/" + theme.displayName.ToLowerInvariant() + "-after.png");
        foreach (var m in mats) m.SetFloat("_EdgeShadow", .6f);
        if (!string.IsNullOrEmpty(outDir)) Save(Render(cam), outDir + "/" + theme.displayName.ToLowerInvariant() + "-before.png");
        foreach (var m in mats) WorldPainter.ApplyEdge(m, theme.displayName);
        foreach (var wall in walls) wall.GetComponent<Renderer>().enabled = false;
        var plate = Render(cam);                                        // backdrop alone
        foreach (var wall in walls) wall.GetComponent<Renderer>().enabled = true;

        foreach (var rr in others) rr.enabled = false;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black; var gameK = Render(cam);
        cam.backgroundColor = Color.white; var gameW = Render(cam);
        foreach (var m in mats) { m.SetFloat("_EdgeDark", 0f); m.SetFloat("_EdgeShadow", 0f); m.SetFloat("_OuterDark", 0f); }
        cam.backgroundColor = Color.black; var artK = Render(cam);
        cam.backgroundColor = Color.white; var artW = Render(cam);
        foreach (var m in mats) WorldPainter.ApplyEdge(m, theme.displayName);
        cam.clearFlags = flags; cam.backgroundColor = bg;
        foreach (var rr in others) if (rr != null) rr.enabled = true;

        int n = W * H;
        var artA = new float[n]; var gameA = new float[n];
        for (int i = 0; i < n; i++)
        {
            artA[i] = Mathf.Clamp01(1f - (artW[i].g - artK[i].g) / 255f);
            gameA[i] = Mathf.Clamp01(1f - (gameW[i].g - gameK[i].g) / 255f);
        }
        // columns of the rail footprint: any art alpha > .5 in that column
        int lo = W, hi = -1;
        for (int y = 0; y < H; y += 8) for (int x = 0; x < W / 2; x++)
            if (artA[y * W + x] > .5f) { lo = Mathf.Min(lo, x); hi = Mathf.Max(hi, x); }
        long foot = 0, footTrans = 0, leak = 0;
        double nearSum = 0, farSum = 0; long nearN = 0, farN = 0;
        var dist = new int[W];
        for (int y = 0; y < H; y++)
        {
            int row = y * W;
            int last = -10000;
            for (int x = 0; x < W; x++) { if (artA[row + x] > .5f) last = x; dist[x] = x - last; }
            last = 10000;
            for (int x = W - 1; x >= 0; x--) { if (artA[row + x] > .5f) last = x; dist[x] = Mathf.Min(dist[x], last - x); }
            for (int x = 0; x < W; x++)
            {
                float a = artA[row + x];
                bool inFoot = (x >= lo && x <= hi) || (x >= W - 1 - hi && x <= W - 1 - lo);
                if (inFoot) { foot++; if (a < .02f) footTrans++; }
                if (a >= .02f) continue;
                if (gameA[row + x] > .05f) leak++;
                float drop = Mathf.Max(0f, Lum(plate[row + x]) - Lum(frame[row + x]));
                if (dist[x] <= HaloPx) { nearSum += drop; nearN++; }
                else if (x > W * .35f && x < W * .65f) { farSum += drop; farN++; }
            }
        }
        return new Result
        {
            transparentShare = foot > 0 ? footTrans / (float)foot : 0f,
            leak = leak / (float)n,
            halo = (float)(nearSum / Mathf.Max(1, nearN) - farSum / Mathf.Max(1, farN))
        };
    }

    static float Lum(Color32 c) { return (.299f * c.r + .587f * c.g + .114f * c.b) / 255f; }

    static Color32[] Render(Camera cam)
    {
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var prevT = cam.targetTexture; var prevA = RenderTexture.active;
        cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        cam.targetTexture = prevT; RenderTexture.active = prevA;
        var px = tex.GetPixels32();
        Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        return px;
    }

    static void Save(Color32[] px, string file)
    {
        var t = new Texture2D(W, H, TextureFormat.RGBA32, false);
        t.SetPixels32(px); t.Apply();
        File.WriteAllBytes(file, t.EncodeToPNG());
        Object.DestroyImmediate(t);
    }
}
