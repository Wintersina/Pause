using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders each world's backdrop with its cloud shadows, as the phone camera sees it, at 1080x2340 and 480x854, with a few bright
// stand-in "enemies / shots" at gameplay order (0+) to show they stay on top of the shadow; plus a frame sequence of Frost.
//
//   CLOUD_SHADOW_DIR=<dir> Unity -batchmode -quit -projectPath Pause -executeMethod CloudShadowPreview.Run
public static class CloudShadowPreview
{
    const float Dt = 1f / 60f;
    static readonly int[,] Sizes = { { 1080, 2340 }, { 480, 854 } };

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("CLOUD_SHADOW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/CloudShadowPreview";
        Directory.CreateDirectory(dir);
        int failures = 0;
        using (new TestHarness.Sandbox())
        {
            try
            {
                CloudShadowLayer.ForceInEditMode = true;
                foreach (string w in new[] { "Frost", "Verdant", "Ember", "Tide", "Desert", "Space", "Storm" })
                {
                    var spec = BackdropCatalog.For(w);
                    for (int v = 1; v <= (spec.variantSets > 0 ? 4 : 1); v++)
                    {
                        if (v == 2 || v == 3 && !spec.Night(v)) continue;     // variant 1, 4 and any night variant
                        for (int s = 0; s < Sizes.GetLength(0); s++) Still(dir, w, v, Sizes[s, 0], Sizes[s, 1]);
                    }
                }
                Sequence(dir);
            }
            catch (System.Exception e) { Debug.LogException(e); failures++; }
            finally
            {
                CloudShadowLayer.ForceInEditMode = false;
                foreach (string w in new[] { "Frost", "Verdant", "Ember", "Tide", "Desert", "Storm" }) BackdropVariants.For(w).Reset();
                if (WorldBackdrop.Instance != null) Object.DestroyImmediate(WorldBackdrop.Instance.gameObject);
            }
        }
        UnityEditor.EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    static WorldBackdrop Fresh(string world, int variant, int W, int H, out Camera cam)
    {
        if (WorldBackdrop.Instance != null) Object.DestroyImmediate(WorldBackdrop.Instance.gameObject);
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        Time.timeScale = 1f;
        moveBackGround.speed = .25f;
        Random.InitState(5);
        if (world != "Space") { BackdropVariants.For(world).Reset(); BackdropVariants.For(world).Force = variant; }
        cam = Camera.main;
        cam.orthographic = true;
        cam.aspect = W / (float)H;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, W, H);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        var wb = WorldBackdrop.Create(world);
        wb.Show(world, false);
        return wb;
    }

    static void Standins(Camera cam)
    {
        var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
            tex.SetPixel(x, y, (x - 7.5f) * (x - 7.5f) + (y - 7.5f) * (y - 7.5f) < 56f ? Color.white : Color.clear);
        tex.Apply();
        var sprite = Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(.5f, .5f), 16f);
        var colors = new[] { new Color(1f, .85f, .2f), new Color(.2f, 1f, .9f), new Color(1f, .35f, .2f), Color.white };
        float hw = cam.orthographicSize * cam.aspect;
        for (int i = 0; i < 12; i++)
        {
            var go = new GameObject("standin" + i);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite; sr.sortingOrder = 5; sr.color = colors[i % colors.Length];
            go.transform.position = new Vector3(Mathf.Lerp(-hw * .8f, hw * .8f, (i * 5 % 12) / 11f), Mathf.Lerp(-cam.orthographicSize * .85f, cam.orthographicSize * .85f, i / 11f), 0f);
            go.transform.localScale = Vector3.one * (i % 3 == 0 ? .6f : .3f);
        }
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

    static void Still(string dir, string world, int variant, int W, int H)
    {
        var wb = Fresh(world, variant, W, H, out var cam);
        for (float t = 0f; t < 10f; t += Dt) wb.Step(Dt);
        Standins(cam);
        var cs = wb.Current.CloudShadows;
        Debug.Log("[CSP] " + world + " v" + variant + " " + W + "x" + H + " shadows=" + (cs != null ? cs.ActiveCount + "/" + cs.Count + " peak " + cs.Peak.ToString("F2") : "none"));
        if (cs != null) for (int i = 0; i < cs.Count; i++) { var r = cs.Renderer(i); Debug.Log("[CSP]   slot " + i + " active " + cs.IsActive(i) + " en " + r.enabled + " a " + r.color.a.ToString("F2") + " bounds " + r.bounds + " sprite " + (r.sprite != null ? r.sprite.name + " tex " + (r.sprite.texture != null) : "null") + " layer " + r.gameObject.layer + " order " + r.sortingOrder + " mat " + (r.sharedMaterial != null ? r.sharedMaterial.shader.name : "null") + " cam " + cam.transform.position); }
        Save(cam, W, H, Path.Combine(dir, world.ToLower() + "-v" + variant + "-" + W + "x" + H + ".png"));
        if (cs != null) { cs.Root.gameObject.SetActive(false); Save(cam, W, H, Path.Combine(dir, "off-" + world.ToLower() + "-v" + variant + "-" + W + "x" + H + ".png")); }
    }

    static void Sequence(string dir)
    {
        const int W = 540, H = 1170;
        var wb = Fresh("Frost", 1, W, H, out var cam);
        for (float t = 0f; t < 20f; t += Dt) wb.Step(Dt);
        for (int f = 0; f < 8; f++)
        {
            Save(cam, W, H, Path.Combine(dir, "seq-frost-" + f + ".png"));
            for (float t = 0f; t < 5f; t += Dt) wb.Step(Dt);
        }
    }
}
