using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Close-ups of Space's turning spheres (the anim atlas's giant_NN / rocky_NN
// cells) at the size a phone draws them, through the same shader, disc and
// property block SpaceDirector uses, for judging how sharp they read.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod SpacePlanetPreview.Run
//   $PLANET_PREVIEW_DIR   output folder (default Builds/PlanetPreview)
//   $PLANET_PREVIEW_PX    phone width in pixels (default 1170): the run's view is
//                         CameraFit.GameplayHalfWidth * 2 = 7.44 u across it
//   $PLANET_PREVIEW_TURNS comma list of surface turns, radians (default 0,1,2)
// Giants are drawn at the largest hero width (near tier, top size jitter),
// rocks at the largest planetoid width. Writes <cell>_t<turn>.png and
// sizes.csv (cell, source px wide, drawn px wide, upscale factor).
public static class SpacePlanetPreview
{
    public const float GiantWidth = 4.125f;    // SpaceDirector: KindSize 1.0 x near tier 3.30 x jitter 1.25
    public const float RockWidth = 1.0f;       // KindSize 0.5 x mid tier 1.60 x jitter 1.25

    public static void Run()
    {
        string dir = Env("PLANET_PREVIEW_DIR", "Builds/PlanetPreview");
        int phonePx = int.Parse(Env("PLANET_PREVIEW_PX", "1170"));
        string[] turnList = Env("PLANET_PREVIEW_TURNS", "0,1,2").Split(',');
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        float pxPerUnit = phonePx / (CameraFit.GameplayHalfWidth * 2f);
        var atlas = BackdropSet.LoadAnimAtlas(BackdropCatalog.Folder("Space"));
        var shader = Resources.Load<Shader>(SpaceDirector.PlanetShader);
        var mat = new Material(shader) { name = "PreviewPlanet" };
        var camGo = new GameObject("~PlanetCam", typeof(Camera));
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        var go = new GameObject("~Planet", typeof(SpriteRenderer));
        var sr = go.GetComponent<SpriteRenderer>();
        sr.sharedMaterial = mat;
        var mpb = new MaterialPropertyBlock();
        var csv = new System.Text.StringBuilder("cell,source_px,drawn_px,upscale,sheet_w\n");
        try
        {
            foreach (string prefix in new[] { "giant", "rocky" })
                foreach (var s in atlas.Frames(prefix))
                {
                    float width = prefix == "giant" ? GiantWidth : RockWidth;
                    int px = Mathf.CeilToInt(width * pxPerUnit) + 8;
                    sr.sprite = s;
                    float k = width / s.bounds.size.x;
                    go.transform.localScale = new Vector3(k, k, 1f);
                    cam.orthographicSize = px * 0.5f / pxPerUnit;
                    cam.aspect = 1f;
                    var rt = new RenderTexture(px, px, 24);
                    cam.targetTexture = rt;
                    float drawn = width * pxPerUnit;
                    csv.Append(s.name).Append(',').Append(s.rect.width).Append(',')
                       .Append(drawn.ToString("F0")).Append(',').Append((drawn / s.rect.width).ToString("F2"))
                       .Append(',').Append(s.texture.width).Append('\n');
                    foreach (string t in turnList)
                    {
                        float turn = float.Parse(t, System.Globalization.CultureInfo.InvariantCulture);
                        mpb.Clear();
                        mpb.SetTexture("_MainTex", s.texture);
                        mpb.SetVector("_Disc", SpaceDirector.DiscOf(s));
                        mpb.SetFloat("_Spin", turn);
                        sr.SetPropertyBlock(mpb);
                        Shoot(cam, rt, px, Path.Combine(dir, s.name + "_t" + t + ".png"));
                    }
                    cam.targetTexture = null;
                    Object.DestroyImmediate(rt);
                }
            File.WriteAllText(Path.Combine(dir, "sizes.csv"), csv.ToString());
            Debug.Log("[PLANETPREVIEW] sheet " + atlas.texture.width + "x" + atlas.texture.height + " -> " + dir);
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(mat);
            atlas.Destroy();
        }
        EditorApplication.Exit(0);
    }

    static void Shoot(Camera cam, RenderTexture rt, int px, string path)
    {
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var png = new Texture2D(px, px, TextureFormat.RGB24, false);
        png.ReadPixels(new Rect(0, 0, px, px), 0, 0);
        png.Apply();
        File.WriteAllBytes(path, png.EncodeToPNG());
        RenderTexture.active = old;
        Object.DestroyImmediate(png);
    }

    static string Env(string key, string fallback)
    {
        string v = System.Environment.GetEnvironmentVariable(key);
        return string.IsNullOrEmpty(v) ? fallback : v;
    }
}
