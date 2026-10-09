using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The cloud cover over Frost and Verdant, for review: every ground variant
// rendered in gameS1 (rails, backdrop, a ship for scale) at 4 / 10 / 20 /
// 40 s after the landing (<world>-vN-SSs.png), and the measured cover
// (CloudCoverMeter) logged per shot and as a steady-state table over three
// seeds (12 / 20 / 40 / 70 s): "[CLOUDS] ..." lines.
//
//   CLOUD_PREVIEW_DIR=<dir> scripts/unity-batch.sh -executeMethod CloudCoverPreview.Run
//   (CLOUD_PREVIEW_SHOTS=0 skips the renders, CLOUD_PREVIEW_STATS=0 the table.)
public static class CloudCoverPreview
{
    const float Dt = 1f / 30f;
    const int W = 1080, H = 2400;
    static readonly float[] ShotAt = { 4f, 10f, 20f, 40f };
    static readonly (string name, int index)[] Worlds = { ("Frost", 1), ("Verdant", 2) };

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("CLOUD_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/CloudCoverPreview";
        bool shots = System.Environment.GetEnvironmentVariable("CLOUD_PREVIEW_SHOTS") != "0";
        bool stats = System.Environment.GetEnvironmentVariable("CLOUD_PREVIEW_STATS") != "0";
        Directory.CreateDirectory(dir);
        int failures = 0;
        using (new TestHarness.Sandbox())
        {
            try
            {
                foreach (var w in Worlds)
                {
                    var cam = Scene(w.index);
                    var wb = WorldBackdrop.Create(w.name);
                    var sel = BackdropVariants.For(w.name);
                    for (int v = 1; v <= BackdropVariants.MaxVariants; v++)
                    {
                        if (shots)
                        {
                            sel.Force = v;
                            wb.Show("Space", false);
                            wb.Show(w.name, false);
                            var d = wb.Current.Director;
                            float clock = 0f;
                            foreach (float at in ShotAt)
                            {
                                while (clock < at) { wb.Step(Dt); clock += Dt; }
                                Capture(cam, Path.Combine(dir, w.name + "-v" + v + "-" + at.ToString("00") + "s.png"));
                                var all = CloudCoverMeter.Measure(wb.Current);
                                var ceil = CloudCoverMeter.Measure(wb.Current, CloudCoverMeter.CeilingOnly);
                                Debug.Log("[CLOUDS] shot " + w.name + " v" + v + " " + at + "s covered " + all.covered.ToString("F3") +
                                          " (ceiling " + ceil.covered.ToString("F3") + ", mean opacity " + all.mean.ToString("F3") + ")");
                            }
                        }
                        if (stats)
                        {
                            float sum = 0f, lane = 0f, worst = 0f;
                            string detail = "";
                            for (int seed = 1; seed <= 3; seed++)
                            {
                                sel.Force = v;
                                wb.Show("Space", false);
                                wb.Show(w.name, false);
                                var d = wb.Current.Director;
                                CloudCoverMeter.Reseed(d, 7000 + seed * 31 + v);
                                var s = CloudCoverMeter.Run(wb, Dt);
                                sum += s.covered; lane = Mathf.Max(lane, s.laneRun); worst = Mathf.Max(worst, s.worst);
                                detail += " [" + s.samples + "]";
                            }
                            Debug.Log("[CLOUDS] steady " + w.name + " v" + v + " mean covered " + (sum / 3f).ToString("F3") +
                                      " worst " + worst.ToString("F3") + " longest lane cover " + lane.ToString("F1") + " s" + detail);
                        }
                    }
                    Object.DestroyImmediate(wb.gameObject);
                }
            }
            catch (System.Exception e) { Debug.LogException(e); failures++; }
            finally
            {
                foreach (var w in Worlds) BackdropVariants.For(w.name).Reset();
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
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        Random.InitState(7);
        var paused = SceneUtil.FindAny("paused");
        if (paused != null) paused.SetActive(false);
        WorldPainter.Apply(WorldManager.Worlds[world]);
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
        var pilot = new GameObject("~PfSpawner").AddComponent<spawnShips>().Spawn(ShipId.Starter);
        pilot.transform.position = new Vector3(-.6f, ShipReach.StartY, 0f);
        EliteSystem.PlayerOverride = pilot.transform;
        return cam;
    }

    static void Capture(Camera cam, string file)
    {
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previous = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        // half size: enough to judge the cover, a quarter of the bytes
        var full = new Texture2D(W, H, TextureFormat.RGB24, false);
        full.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        full.Apply();
        var half = new Texture2D(W / 2, H / 2, TextureFormat.RGB24, false);
        var src = full.GetPixels32();
        var dst = new Color32[(W / 2) * (H / 2)];
        for (int y = 0; y < H / 2; y++)
            for (int x = 0; x < W / 2; x++)
            {
                Color32 a = src[2 * y * W + 2 * x], b = src[2 * y * W + 2 * x + 1], c = src[(2 * y + 1) * W + 2 * x], d = src[(2 * y + 1) * W + 2 * x + 1];
                dst[y * (W / 2) + x] = new Color32((byte)((a.r + b.r + c.r + d.r) / 4), (byte)((a.g + b.g + c.g + d.g) / 4), (byte)((a.b + b.b + c.b + d.b) / 4), 255);
            }
        half.SetPixels32(dst);
        half.Apply();
        File.WriteAllBytes(file, half.EncodeToPNG());
        RenderTexture.active = old;
        cam.targetTexture = previous;
        Object.DestroyImmediate(full);
        Object.DestroyImmediate(half);
        Object.DestroyImmediate(rt);
    }
}
