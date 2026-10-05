using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class WorldRailReview
{
    public static void Run()
    {
        using var sandbox = new TestHarness.Sandbox();
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity");
        int failures = 0;
        var left = GameObject.Find("leftPipe");
        var right = GameObject.Find("rightPipe");
        var lm = left.GetComponent<Renderer>().material;
        var rm = right.GetComponent<Renderer>().material;
        float width = left.transform.localScale.x;
        float leftX = left.transform.localPosition.x;
        var cam = Camera.main;
        // Match the approved 900x1600 reference for direct visual comparison.
        const int captureWidth = 900, captureHeight = 1600;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, captureWidth, captureHeight);
        cam.aspect = captureWidth / (float)captureHeight;
        // RailFit.Start does not run in this editor-only review.
        foreach (var wall in new[] { left, right })
        {
            var scale = wall.transform.localScale;
            scale.y = cam.orthographicSize * 2f * 1.085f /
                      wall.GetComponent<MeshFilter>().sharedMesh.bounds.size.y;
            wall.transform.localScale = scale;
        }
        var backdrop = new GameObject("~RailReviewBackdrop").AddComponent<WorldBackdrop>();
        foreach (var theme in WorldManager.Worlds)
        {
            string name = WorldPainter.RailTextureName(theme.displayName);
            if (name != null) failures += WorldRailTest.CheckArt(theme);
            string folder = string.IsNullOrEmpty(theme.resourceFolder) ? theme.displayName : theme.resourceFolder;
            string path = "Assets/Art/Resources/Worlds/" + folder + "/" + name + ".png";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Verify(theme.displayName + " crisp scrolling import", importer.filterMode == FilterMode.Point &&
                   importer.wrapModeV == TextureWrapMode.Repeat && !importer.mipmapEnabled, ref failures);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(path));
            var px = tex.GetPixels32();
            int minX = tex.width, maxX = -1;
            for (int y = 0; y < tex.height; y++)
                for (int x = 0; x < tex.width; x++)
                {
                    var pixel = px[y * tex.width + x];
                    if (pixel.a <= 128 || Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b)) <= 4) continue;
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                }
            float visibleFraction = (maxX - minX + 1f) / tex.width;
            float innerBoundary = (maxX + 1f) / tex.width - .5f;
            float seam = RowDifference(px, tex.width, 0, tex.height - 1), largest = 0;
            for (int y = 1; y < tex.height; y++) largest = Mathf.Max(largest, RowDifference(px, tex.width, y, y - 1));
            Debug.Log("[WRR] " + theme.displayName + " source wrap difference " + seam.ToString("F3"));
            Object.DestroyImmediate(tex);
            WorldPainter.Apply(theme);
            Verify(theme.displayName + " scrolling join and transparent material",
                   lm.shader.name == "Pause/WorldRailRepeat" && rm.shader == lm.shader && lm.shader.isSupported &&
                   lm.GetFloat("_Overlap") == .03f && lm.GetFloat("_BlackCutout") == 1 &&
                   lm.renderQueue == 3000 && rm.renderQueue == 3000, ref failures);
            if (name != null) Verify(theme.displayName + " binds new rail on both sides", lm.mainTexture == rm.mainTexture &&
                   lm.mainTexture == Resources.Load<Texture2D>("Worlds/" + folder + "/" + name), ref failures);
            Verify(theme.displayName + " mirrors inward and matches authored thickness", lm.mainTextureScale.x == 1 &&
                   rm.mainTextureScale.x == (name == null ? 1 : -1) && Mathf.Approximately(left.transform.localScale.x,
                   width * WorldPainter.RailWidthFactor(theme.displayName)), ref failures);
            Verify(theme.displayName + " visible silhouette matches Frost width",
                Mathf.Abs(left.transform.localScale.x * visibleFraction - width * 1.25f * 443f / 725f) < .001f, ref failures);
            Verify(theme.displayName + " inner edge matches Frost",
                Mathf.Abs(left.transform.localPosition.x + left.transform.localScale.x * innerBoundary -
                (leftX + width * 1.25f * (583f / 725f - .5f))) < .001f, ref failures);
            // Imported dimensions can be reduced; use the known world-space
            // silhouette width and its verified inner edge instead.
            float actualInner = left.transform.localPosition.x + left.transform.localScale.x * innerBoundary;
            float actualOuter = actualInner - left.transform.localScale.x * visibleFraction;
            Verify(theme.displayName + " full wide rail remains in the phone frame",
                actualOuter >= -cam.orthographicSize * cam.aspect - .001f &&
                left.transform.localScale.x * visibleFraction / (2 * cam.orthographicSize * cam.aspect) >= .13f,
                ref failures);
            VerifyProportions(left, theme.displayName, ref failures);
            VerifyProportions(right, theme.displayName, ref failures);
            CheckRenderedColors(lm, path, theme.displayName, ref failures);
            backdrop.Show(theme.displayName, false);
            backdrop.Step(1f);
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r.gameObject != left && r.gameObject != right && r.GetComponentInParent<WorldBackdrop>() == null) r.enabled = false;
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) canvas.enabled = false;
            Capture(cam, theme.displayName, captureWidth, captureHeight);
        }
        WorldPainter.Apply(WorldManager.Worlds[0]);
        Verify("Space restores its high-resolution rail at the common Frost width",
               lm.mainTexture == Resources.Load<Texture2D>("Worlds/Space/rail_space_wide_v1") &&
               Mathf.Approximately(left.transform.localScale.x, width * WorldPainter.RailWidthFactor("Space")) &&
               rm.mainTextureScale.x == -1, ref failures);
        foreach (var screen in new[] { new Vector2Int(900, 1600), new Vector2Int(1080, 2340), new Vector2Int(1080, 2520) })
        {
            cam.aspect = screen.x / (float)screen.y;
            cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, screen.x, screen.y);
            foreach (var wall in new[] { left, right })
            {
                var fit = wall.GetComponent<RailFit>() ?? wall.AddComponent<RailFit>();
                fit.SendMessage("Start");
                var bounds = wall.GetComponent<Renderer>().bounds;
                Verify(screen + " " + wall.name + " covers the phone height",
                    bounds.min.y <= cam.transform.position.y - cam.orthographicSize &&
                    bounds.max.y >= cam.transform.position.y + cam.orthographicSize, ref failures);
                VerifyProportions(wall, screen.ToString(), ref failures);
            }
            Verify(screen + " preserves full rail framing", Mathf.Approximately(cam.orthographicSize * cam.aspect,
                CameraFit.GameplayHalfWidth), ref failures);
        }
        Object.DestroyImmediate(backdrop.gameObject);
        Debug.Log("[WRR] failures: " + failures);
        TestHarness.Exit(failures);
    }

    static void Verify(string label, bool ok, ref int failures)
    {
        Debug.Log("[WRR] " + (ok ? "PASS " : "FAIL ") + label);
        if (!ok) failures++;
    }

    static float RowDifference(Color32[] px, int width, int a, int b)
    {
        float sum = 0;
        for (int x = 0; x < width; x++)
        {
            Color ca = px[a * width + x], cb = px[b * width + x];
            sum += Mathf.Abs(ca.r * ca.a - cb.r * cb.a) + Mathf.Abs(ca.g * ca.a - cb.g * cb.a) +
                   Mathf.Abs(ca.b * ca.a - cb.b * cb.a) + Mathf.Abs(ca.a - cb.a);
        }
        return sum / (width * 4);
    }

    static void VerifyProportions(GameObject wall, string world, ref int failures)
    {
        var mat = wall.GetComponent<Renderer>().material;
        var size = wall.GetComponent<MeshFilter>().sharedMesh.bounds.size;
        float horizontalPixel = Mathf.Abs(size.x * wall.transform.lossyScale.x) / mat.mainTexture.width;
        float verticalPixel = Mathf.Abs(size.y * wall.transform.lossyScale.y) /
            (mat.mainTexture.height * mat.mainTextureScale.y * (1 - mat.GetFloat("_Overlap")));
        Verify(world + " " + wall.name + " preserves square art pixels",
               Mathf.Abs(horizontalPixel - verticalPixel) < .00001f, ref failures);
    }

    // Render onto a known background and probe real GPU pixels. This catches
    // opaque mattes and faded neon that binding-only checks cannot detect.
    static void CheckRenderedColors(Material live, string path, string world, ref int failures)
    {
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        source.LoadImage(File.ReadAllBytes(path));
        var pixels = source.GetPixels32();
        int neon = -1, matte = -1;
        for (int i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            int max = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            bool lampHue = world == "Verdant" ? c.g >= 240 && c.r < 180 && c.b < 180 :
                world == "Frost" ? c.b >= 240 && c.r < 150 && c.g >= 130 :
                world == "Space" ? c.r >= 240 && c.g < 100 && c.b >= 100 :
                c.r >= 240 && c.g >= 120 && c.g < 230 && c.b < 100;
            if (neon < 0 && c.a >= 250 && lampHue) neon = i;
            // Probe an actual opaque black pixel in the exterior margin.
            if (matte < 0 && c.a > 240 && max <= 1) matte = i;
            if (neon >= 0 && matte >= 0) break;
        }
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.layer = 31;
        quad.transform.position = new Vector3(0, 0, -100);
        quad.transform.localScale = new Vector3(source.width, source.height, 1);
        var mat = new Material(live);
        mat.mainTexture = source;
        mat.mainTextureScale = Vector2.one;
        mat.mainTextureOffset = Vector2.zero;
        mat.SetFloat("_Overlap", 0);
        quad.GetComponent<Renderer>().sharedMaterial = mat;
        var go = new GameObject("~RailColorProbe", typeof(Camera));
        var cam = go.GetComponent<Camera>();
        cam.cullingMask = 1 << 31;
        cam.orthographic = true;
        cam.orthographicSize = source.height * .5f;
        cam.aspect = source.width / (float)source.height;
        cam.transform.position = new Vector3(0, 0, -110);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(.2f, .4f, .6f, 1);
        var rt = new RenderTexture(source.width, source.height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        cam.targetTexture = rt;
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var rendered = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        rendered.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        rendered.Apply();
        RenderTexture.active = old;
        if (neon >= 0)
        {
            Color32 expected = pixels[neon];
            Color32 actual = rendered.GetPixel(neon % source.width, neon / source.width);
            Verify(world + " neon retains source brightness and hue (" + expected + " -> " + actual + ")",
                Mathf.Abs(expected.r - actual.r) <= 12 && Mathf.Abs(expected.g - actual.g) <= 12 &&
                Mathf.Abs(expected.b - actual.b) <= 12, ref failures);
        }
        else Verify(world + " contains a bright neon probe", false, ref failures);
        if (matte >= 0)
        {
            Color c = rendered.GetPixel(matte % source.width, matte / source.width);
            Verify(world + " black exterior shows background", Mathf.Abs(c.r - .2f) < .02f &&
                   Mathf.Abs(c.g - .4f) < .02f && Mathf.Abs(c.b - .6f) < .02f, ref failures);
        }
        else Verify(world + " contains a black matte probe", false, ref failures);
        Object.DestroyImmediate(source);
        Object.DestroyImmediate(rendered);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(mat);
        Object.DestroyImmediate(quad);
        Object.DestroyImmediate(go);
    }

    static void Capture(Camera cam, string world, int width, int height)
    {
        string dir = "Builds/RailPreview";
        Directory.CreateDirectory(dir);
        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previous = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var png = new Texture2D(width, height, TextureFormat.RGB24, false);
        png.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        png.Apply();
        File.WriteAllBytes(Path.Combine(dir, world.ToLowerInvariant() + "_rails.png"), png.EncodeToPNG());
        RenderTexture.active = old;
        cam.targetTexture = previous;
        Object.DestroyImmediate(png);
        Object.DestroyImmediate(rt);
    }
}
