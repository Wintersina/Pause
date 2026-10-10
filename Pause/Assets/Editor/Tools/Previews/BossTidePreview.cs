using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders the Iron Kraken in gameS1 over the Tide backdrop (rails, backdrop):
// the six-drawing tentacle idle, each of the three tells, and the fire frames.
// One crop round the boss per body-frame change, named by the atlas cell:
//
//   idle-<cell>.png          the resting idle loop
//   atk<N>-<cell>.png        attack N's frames as the boss goes through its tell and fire
//   full-atk<N>.png          the whole screen mid-attack
//
//   BOSSTIDE_PREVIEW_DIR=<dir> Unity -batchmode -quit -projectPath Pause -executeMethod BossTidePreview.Run
public static class BossTidePreview
{
    const float Dt = 1f / 60f;
    const int W = 1080, H = 2400;

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("BOSSTIDE_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/BossTidePreview";
        Directory.CreateDirectory(dir);
        int failures = 0;
        using (new TestHarness.Sandbox())
        {
            try { Go(dir); }
            catch (System.Exception e) { Debug.LogException(e); failures++; }
            finally
            {
                BackdropVariants.For("Tide").Reset();
                BossEncounter.ResetRun();
                BossRails.Reset();
                PlayField.Reset();
                ScreenInfo.ClearOverride();
                buttonClicks.playerDied = false;
            }
        }
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    static void Go(string dir)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity");
        BossEncounter.ResetRun();
        BossRails.Reset();
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
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, 4);
        Random.InitState(7);
        var paused = SceneUtil.FindAny("paused");
        if (paused != null) paused.SetActive(false);
        WorldPainter.Apply(WorldManager.Worlds[4]);
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
        var wb = WorldBackdrop.Create("Tide");
        BackdropVariants.For("Tide").Force = 1;
        wb.Show("Space", false);
        wb.Show("Tide", false);
        for (int i = 0; i < 600; i++) wb.Step(Dt);

        BossEncounter.Begin(4, null);
        var e = BossEncounter.Instance;
        e.Step(.1f, 1f);
        for (int i = 0; i < 400 && e.State == BossEncounter.Phase.Intro; i++) e.Step(.1f, 1f);

        // the resting idle: a crop on each new drawing
        int last = -1, saved = 0;
        for (int i = 0; i < 600 && saved < 6; i++)
        {
            e.Step(Dt, 1f); wb.Step(Dt);
            if (e.Actor.CurrentAttack != null) break;
            int f = e.Actor.BodyFrame;
            if (f != last && f >= BossArt.SpaceIdle0 && f < BossArt.SpaceIdle0 + BossArt.SpaceIdleFrames)
            { Crop(cam, e.Actor.transform.position, Path.Combine(dir, "idle-" + f + ".png")); saved++; }
            last = f;
        }
        Debug.Log("[BOSSTIDE-PREVIEW] idle frames saved " + saved);
        for (int a = 0; a < e.Boss.attacks.Length; a++)
        {
            e.Actor.ForcedAttack = a;
            int started = e.Actor.AttacksStarted;
            for (int i = 0; i < 600 && e.Actor.AttacksStarted == started; i++) { e.Step(Dt, 1f); wb.Step(Dt); }
            last = -1;
            bool full = false;
            for (int i = 0; i < 360 && e.State == BossEncounter.Phase.Fight; i++)
            {
                e.Step(Dt, 1f); wb.Step(Dt);
                int f = e.Actor.BodyFrame;
                if (f != last) { Crop(cam, e.Actor.transform.position, Path.Combine(dir, "atk" + a + "-" + f + "-" + i.ToString("000") + ".png")); last = f; }
                if (!full && e.Actor.CurrentAttack != null && !e.Actor.Telegraphing && i > 100)
                { Capture(cam, Path.Combine(dir, "full-atk" + a + ".png")); full = true; }
                if (e.Actor.CurrentAttack == null && i > 120) break;
            }
        }
        Object.DestroyImmediate(wb.gameObject);
    }

    static void Crop(Camera cam, Vector3 at, string file)
    {
        var full = Render(cam, W, H);
        float ppu = H / (cam.orthographicSize * 2f);
        Vector3 v = cam.WorldToViewportPoint(at);
        int size = Mathf.RoundToInt(4.4f * ppu);
        int cx = Mathf.RoundToInt(v.x * W), cy = Mathf.RoundToInt(v.y * H);
        int x0 = Mathf.Clamp(cx - size / 2, 0, W - size), y0 = Mathf.Clamp(cy - size / 2 - size / 8, 0, H - size);
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

    static void Capture(Camera cam, string file)
    {
        var png = Render(cam, W, H);
        File.WriteAllBytes(file, png.EncodeToPNG());
        Object.DestroyImmediate(png);
    }
}
