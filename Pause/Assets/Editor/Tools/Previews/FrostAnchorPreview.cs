using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Frame strips (t = 0 / 5 / 10 / 20 s) of every Frost backdrop variant with
// a drawn reference marker on an anchored overlay item, to show whether the
// item slides over the ground it stands on.
//
//   MAGENTA cross  fixed to the GROUND (mid tile): where the item's centre
//                  was at t = 0, scrolled with the ground.
//   CYAN ring      the item's actual centre now.
//
// An anchored item keeps ring and cross together; a drifting one walks away
// from its cross. The same screen y is used by both markers (the horizontal
// rule through the cross at t = 0 is repeated on every frame).
// The cloud ceiling is cleared at once so the ground is visible from t = 0.
//
//   FROST_ANCHOR_DIR=<dir> Unity -batchmode -quit -projectPath Pause -executeMethod FrostAnchorPreview.Run
public static class FrostAnchorPreview
{
    const float Dt = 1f / 30f;
    const int W = 1080, H = 2400, Down = 2;
    static readonly float[] Times = { 0f, 5f, 10f, 20f };

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("FROST_ANCHOR_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/FrostAnchorPreview";
        Directory.CreateDirectory(dir);
        int failures = 0;
        float hold = FrostTuning.CeilingHold, clear = FrostTuning.CeilingClearSeconds, dens = FrostTuning.CloudDensity;
        using (new TestHarness.Sandbox())
        {
            try
            {
                FrostTuning.CeilingHold = .1f;
                FrostTuning.CeilingClearSeconds = .7f;
                FrostTuning.CloudDensity = 0f;
                for (int v = 1; v <= 4; v++)
                    if (BackdropVariants.For("Frost").Installed(v)) Variant(dir, v);
            }
            catch (System.Exception e) { Debug.LogException(e); failures++; }
            finally
            {
                FrostTuning.CeilingHold = hold;
                FrostTuning.CeilingClearSeconds = clear;
                FrostTuning.CloudDensity = dens;
                BackdropVariants.For("Frost").Reset();
                EliteSystem.Clear();
            }
        }
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    static void Variant(string dir, int variant)
    {
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        Time.timeScale = 1f;
        moveBackGround.speed = .25f;
        var vars = BackdropVariants.For("Frost");
        vars.Reset();
        vars.Seed(11);
        vars.Force = variant;
        var cam = Camera.main;
        cam.orthographic = true;
        cam.aspect = W / (float)H;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, W, H);
        cam.transform.position = new Vector3(0f, 0f, -10f);
        var wb = WorldBackdrop.Create("Frost");
        wb.Show("Frost", false);
        var set = wb.Current;
        var dir0 = (FrostDirector)set.Director;
        BackdropTile mid = null;
        foreach (var t in set.Tiles) if (t.layer.name == "mid") mid = t;
        for (int i = 0; i < 40; i++) wb.Step(Dt);          // ceiling gone, ground settled

        // the anchored item: the highest stationary landmark that is well in view
        BackdropPiece pick = null;
        foreach (var p in dir0.Ground.items)
            if (p.active && p.vx == 0f && p.vy == 0f && p.y < 8f && p.y > 2f && (pick == null || p.y > pick.y)) pick = p;
        if (pick == null) throw new System.Exception("no stationary landmark on v" + variant);
        Vector3 start = pick.root.position;
        double gy0 = start.y + mid.travel;
        float y0 = start.y;
        Debug.Log("[FAP] v" + variant + " item " + pick.sr.sprite.name + " at " + start);

        var frames = new Texture2D[Times.Length];
        float t0 = 0f;
        for (int k = 0; k < Times.Length; k++)
        {
            while (t0 < Times[k] - 1e-4f) { wb.Step(Dt); t0 += Dt; }
            var tex = Render(cam);
            // ground-locked reference cross + the screen-y rule from t = 0
            Vector3 cross = new Vector3(start.x, (float)(gy0 - mid.travel), 0f);
            Rule(tex, cam, y0, new Color(1f, 0f, 1f, .45f));
            Cross(tex, cam, cross, new Color(1f, 0f, 1f, 1f), 30);
            Ring(tex, cam, pick.root.position, new Color(0f, 1f, 1f, 1f), 22);
            float dy = pick.root.position.y - cross.y;
            Debug.Log("[FAP] v" + variant + " t=" + Times[k] + " item-vs-ground offset y=" + dy.ToString("F3") + " (active " + pick.active + ")");
            frames[k] = tex;
        }
        int fw = W / Down, fh = H / Down;
        var strip = new Texture2D(fw * frames.Length + 6 * (frames.Length - 1), fh, TextureFormat.RGB24, false);
        var clearPx = new Color[strip.width * fh];
        for (int i = 0; i < clearPx.Length; i++) clearPx[i] = Color.white;
        strip.SetPixels(clearPx);
        for (int k = 0; k < frames.Length; k++)
        {
            strip.SetPixels(k * (fw + 6), 0, fw, fh, Shrink(frames[k]));
            Object.DestroyImmediate(frames[k]);
        }
        strip.Apply();
        File.WriteAllBytes(Path.Combine(dir, "variant-v" + variant + ".png"), strip.EncodeToPNG());
        Object.DestroyImmediate(strip);
        Object.DestroyImmediate(wb.gameObject);
    }

    static Color[] Shrink(Texture2D t)
    {
        int fw = t.width / Down, fh = t.height / Down;
        var o = new Color[fw * fh];
        var src = t.GetPixels();
        for (int y = 0; y < fh; y++)
            for (int x = 0; x < fw; x++)
            {
                Color a = Color.black;
                for (int dy = 0; dy < Down; dy++)
                    for (int dx = 0; dx < Down; dx++) a += src[(y * Down + dy) * t.width + x * Down + dx];
                o[y * fw + x] = a / (Down * Down);
            }
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

    static Vector2Int Px(Camera cam, Vector3 w)
    {
        Vector3 v = cam.WorldToViewportPoint(w);
        return new Vector2Int(Mathf.RoundToInt(v.x * W), Mathf.RoundToInt(v.y * H));
    }

    static void Dot(Texture2D t, int x, int y, Color c)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        Color b = t.GetPixel(x, y);
        t.SetPixel(x, y, Color.Lerp(b, new Color(c.r, c.g, c.b), c.a));
    }

    static void Rule(Texture2D t, Camera cam, float worldY, Color c)
    {
        int y = Px(cam, new Vector3(0f, worldY, 0f)).y;
        for (int x = 0; x < W; x++) for (int d = -1; d <= 1; d++) if ((x / 12) % 2 == 0) Dot(t, x, y + d, c);
        t.Apply();
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
