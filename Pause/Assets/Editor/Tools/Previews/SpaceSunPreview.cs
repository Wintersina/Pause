using System.IO;
using UnityEngine;

// Renders the Space backdrop with the sun in both palettes (frames 0 s, 5 s and 20 s, the last during a prominence arc) into
// docs/space-sun-in-backdrop.png.   Unity -batchmode -quit -projectPath Pause -executeMethod SpaceSunPreview.Run
public static class SpaceSunPreview
{
    const int W = 270, H = 540;
    public static void Run()
    {
        var camGo = new GameObject("cam", typeof(Camera)); camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = 6.6f; cam.aspect = W / (float)H;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.03f, .03f, .08f);
        camGo.transform.position = new Vector3(0, 0, -10);
        var rt = new RenderTexture(W, H, 24); cam.targetTexture = rt;
        var sheet = new Texture2D(W * 3, H * 2, TextureFormat.RGB24, false);
        var read = new Texture2D(W, H, TextureFormat.RGB24, false);
        float[] times = { 0f, 6f, 24f };
        for (int row = 0; row < 2; row++)
        {
            SpaceSunPick.NewRun(row == 0 ? 1 : 2);
            for (int tries = 1; SpaceSunPick.PaletteFor(SpaceSunPick.RunSeed) != (row == 0 ? SpaceSunPick.Warm : SpaceSunPick.Violet); tries++) SpaceSunPick.NewRun(tries);
            var root = new GameObject("root").transform;
            var set = new BackdropSet("Space", root, 3.3f, 6.6f);
            float t = 0f;
            for (int c = 0; c < 3; c++)
            {
                while (t < times[c]) { set.Tick(.1f, 1f); t += .1f; }
                cam.Render();
                RenderTexture.active = rt; read.ReadPixels(new Rect(0, 0, W, H), 0, 0); RenderTexture.active = null;
                sheet.SetPixels(c * W, (1 - row) * H, W, H, read.GetPixels());
            }
            Debug.Log("[SUNPREV] row " + row + " palette " + (set.Sun != null ? set.Sun.Palette : "none") + " arcs " + (set.Sun != null ? set.Sun.ArcCount : 0));
            set.Destroy(); Object.DestroyImmediate(root.gameObject);
        }
        sheet.Apply();
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/space-sun-in-backdrop.png"));
        File.WriteAllBytes(path, sheet.EncodeToPNG());
        Debug.Log("[SUNPREV] wrote " + path);
    }
}
