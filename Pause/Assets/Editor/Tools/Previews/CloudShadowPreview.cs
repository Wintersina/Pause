using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders each world's backdrop with its cloud shadows (and without: off-*.png), as the phone camera sees it, at 1080x2340 and 480x854,
// with real elite sprites (first frame of the world's elite strips) and a few shots at gameplay order to show they stay on top of
// the shadow; plus frame sequences of Frost and Desert showing a cloud drifting and morphing.
// CLOUD_FROST_CAND=<dir with sky/far/mid/flow.png> draws Frost with those candidate tiles (brightness 1) under the layer
// (the candidate bright Frost art is not on master yet); CLOUD_SHADOW_DIR=<dir> is the output folder.
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
                string cand = System.Environment.GetEnvironmentVariable("CLOUD_FROST_CAND");
                foreach (string w in new[] { "Frost", "Verdant", "Ember", "Tide", "Desert", "Space", "Storm" })
                {
                    for (int s = 0; s < Sizes.GetLength(0); s++) Still(dir, w, 1, Sizes[s, 0], Sizes[s, 1], null);
                    if (w == "Frost" && !string.IsNullOrEmpty(cand))
                        for (int s = 0; s < Sizes.GetLength(0); s++) Still(dir, w, 1, Sizes[s, 0], Sizes[s, 1], cand);
                    if (w == "Ember" || w == "Desert") Still(dir, w, 4, 1080, 2340, null);   // the night sides
                }
                Sequence(dir, "Frost", cand, "seq-frost");
                Sequence(dir, "Desert", null, "seq-desert");
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

    // Real enemy art: the first 384x384 frame of the world's elite strips (Assets/Art/Enemies/Elite/<World>/<world>_elite_<name>.png).
    static void Standins(Camera cam, string world)
    {
        float hw = cam.orthographicSize * cam.aspect, hh = cam.orthographicSize;
        var spots = new[] { new Vector2(-.45f, .62f), new Vector2(.5f, .25f), new Vector2(-.1f, -.15f), new Vector2(.45f, -.6f) };
        int n = 0;
        string folder = "Assets/Art/Enemies/Elite/" + world + "/";
        if (Directory.Exists(folder))
            foreach (var f in Directory.GetFiles(folder, world.ToLower() + "_elite_*.png"))
            {
                if (f.Contains("damaged") || f.Contains("lights") || f.Contains("death") || f.Contains("concept") || f.Contains("banks") || f.Contains("candidate")) continue;
                if (n >= spots.Length - 1) break;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
                tex.LoadImage(File.ReadAllBytes(f));
                int fw = Mathf.Min(tex.height, tex.width);
                var sprite = Sprite.Create(tex, new Rect(0, 0, fw, tex.height), new Vector2(.5f, .5f), fw / 2.6f);
                var go = new GameObject("elite" + n);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite; sr.sortingOrder = 5;
                go.transform.position = new Vector3(spots[n].x * hw, spots[n].y * hh, 0f);
                n++;
            }
        // a few shots / bullets
        var dot = new Texture2D(16, 16, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
            dot.SetPixel(x, y, (x - 7.5f) * (x - 7.5f) + (y - 7.5f) * (y - 7.5f) < 56f ? Color.white : Color.clear);
        dot.Apply();
        var dsprite = Sprite.Create(dot, new Rect(0, 0, 16, 16), new Vector2(.5f, .5f), 16f);
        var colors = new[] { new Color(1f, .85f, .2f), new Color(.2f, 1f, .9f), new Color(1f, .35f, .2f), Color.white };
        for (int i = 0; i < 10; i++)
        {
            var go = new GameObject("shot" + i);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = dsprite; sr.sortingOrder = 6; sr.color = colors[i % colors.Length];
            go.transform.position = new Vector3(Mathf.Lerp(-hw * .85f, hw * .85f, (i * 7 % 10) / 9f), Mathf.Lerp(-hh * .9f, hh * .9f, i / 9f), 0f);
            go.transform.localScale = Vector3.one * (i % 3 == 0 ? .5f : .26f);
        }
    }

    // The candidate Frost tiles (sky / far / mid / flow from a folder): the tile renderers take them in place of the installed ones.
    static void ApplyCandidates(WorldBackdrop wb, string cand)
    {
        foreach (string name in new[] { "sky", "far", "mid", "flow" })
        {
            string file = Path.Combine(cand, name + ".png");
            if (!File.Exists(file)) continue;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.LoadImage(File.ReadAllBytes(file));
            var t = wb.Current.Root.Find("Tile_" + name);
            if (t == null) continue;
            foreach (var sr in t.GetComponentsInChildren<SpriteRenderer>(true))
            {
                float ppu = tex.width / sr.sprite.bounds.size.x;   // same world size as the installed tile
                sr.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, .5f), ppu);
            }
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

    static void Still(string dir, string world, int variant, int W, int H, string cand)
    {
        float savedBrightness = FrostTuning.Brightness;
        if (cand != null) FrostTuning.Brightness = 1f;     // the candidate tiles are painted bright: no draw-time lift
        try
        {
            var wb = Fresh(world, variant, W, H, out var cam);
            if (cand != null) ApplyCandidates(wb, cand);
            for (float t = 0f; t < 30f; t += Dt) wb.Step(Dt);
            Standins(cam, world);
            var cs = wb.Current.CloudShadows;
            string tag = (cand != null ? "frostcand" : world.ToLower()) + "-v" + variant + "-" + W + "x" + H;
            Debug.Log("[CSP] " + tag + " shadows=" + (cs != null ? cs.ActiveCount + "/" + cs.Count + " peak " + cs.Peak.ToString("F2") + " coverage " + cs.Coverage().ToString("F2") : "none"));
            Save(cam, W, H, Path.Combine(dir, tag + ".png"));
            if (cs != null) { cs.Root.gameObject.SetActive(false); Save(cam, W, H, Path.Combine(dir, "off-" + tag + ".png")); }
        }
        finally { FrostTuning.Brightness = savedBrightness; }
    }

    static void Sequence(string dir, string world, string cand, string name)
    {
        const int W = 540, H = 1170;
        float savedBrightness = FrostTuning.Brightness;
        if (cand != null) FrostTuning.Brightness = 1f;
        try
        {
            var wb = Fresh(world, 1, W, H, out var cam);
            if (cand != null) ApplyCandidates(wb, cand);
            for (float t = 0f; t < 25f; t += Dt) wb.Step(Dt);
            for (int f = 0; f < 12; f++)
            {
                Save(cam, W, H, Path.Combine(dir, name + "-" + f.ToString("00") + ".png"));
                for (float t = 0f; t < 4f; t += Dt) wb.Step(Dt);
            }
        }
        finally { FrostTuning.Brightness = savedBrightness; }
    }
}
