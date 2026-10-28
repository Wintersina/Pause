using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Ember v1..v4 with the cloud-shadow layer on and off at 1080x2340 and 480x854, real boss shots and elite shots hanging over the
// ground (they sort above the shadow layer), 45 s of backdrop time so the clouds are out.
//
//   CLOUD_SHADOW_DIR=<dir> Unity -batchmode -quit -projectPath Pause -executeMethod EmberCloudPreview.Run
public static class EmberCloudPreview
{
    const float Dt = 1f / 60f;
    static readonly int[,] Sizes = { { 1080, 2340 }, { 480, 854 } };

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("CLOUD_SHADOW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/EmberCloudPreview";
        Directory.CreateDirectory(dir);
        int failures = 0;
        using (new TestHarness.Sandbox())
        {
            try
            {
                CloudShadowLayer.ForceInEditMode = true;
                for (int v = 1; v <= 4; v++)
                    for (int s = 0; s < Sizes.GetLength(0); s++) Still(dir, v, Sizes[s, 0], Sizes[s, 1]);
            }
            catch (System.Exception e) { Debug.LogException(e); failures++; }
            finally
            {
                CloudShadowLayer.ForceInEditMode = false;
                BackdropVariants.For("Ember").Reset();
                if (WorldBackdrop.Instance != null) Object.DestroyImmediate(WorldBackdrop.Instance.gameObject);
            }
        }
        UnityEditor.EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    static void Still(string dir, int variant, int W, int H)
    {
        if (WorldBackdrop.Instance != null) Object.DestroyImmediate(WorldBackdrop.Instance.gameObject);
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        EliteSystem.Clear();
        Time.timeScale = 1f;
        moveBackGround.speed = .25f;
        Random.InitState(5);
        BackdropVariants.For("Ember").Reset();
        BackdropVariants.For("Ember").Force = variant;
        var cam = Camera.main;
        cam.orthographic = true;
        cam.aspect = W / (float)H;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, W, H);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        var wb = WorldBackdrop.Create("Ember");
        wb.Show("Ember", false);
        for (float t = 0f; t < 45f; t += Dt) wb.Step(Dt);

        var pool = new BossProjectilePool(40, 1);
        var boss = BossCatalog.ForWorld(3);
        var eliteDef = BaseEliteDefs.For("gunship");
        for (int gx = 0; gx < 4; gx++)
            for (int gy = 0; gy < 6; gy++)
            {
                var at = new Vector3(-2.1f + gx * 1.4f, -4f + gy * 1.6f, 0f);
                if (((gx + gy) & 1) == 0) pool.Fire(boss, gy % 3 == 0 ? BossShotStyle.Shard : BossShotStyle.Bolt, at, Vector2.zero);
                else EliteSystem.Shots.Fire(null, eliteDef, (EliteShots.Kind)(gx % 4), at, Vector2.zero);
            }
        var cs = wb.Current.CloudShadows;
        Debug.Log("[ECP] v" + variant + " " + W + "x" + H + " shadows " + (cs != null ? cs.ActiveCount + "/" + cs.Count + " peak " + cs.Peak.ToString("F2") + " budget " + cs.Budget.ToString("F2") : "none"));
        Save(cam, W, H, Path.Combine(dir, "ember-v" + variant + "-" + W + "x" + H + "-on.png"));
        if (cs != null) { cs.Root.gameObject.SetActive(false); Save(cam, W, H, Path.Combine(dir, "ember-v" + variant + "-" + W + "x" + H + "-off.png")); }
        pool.Dispose();
    }

    static void Save(Camera cam, int W, int H, string path)
    {
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        var prevTarget = cam.targetTexture; var prevActive = RenderTexture.active;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        cam.targetTexture = prevTarget; RenderTexture.active = prevActive;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(rt);
    }
}
