using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Filmstrips of the HapticGate splash, driven by a fake clock (1/60 s steps):
//   natural.png  12 frames of the untouched intro
//   tap1.png     tap 1: the door cracks
//   tap2.png     tap 2: more cracks, bulge, steam leaks
//   tap3.png     tap 3: the smash and the scene change
// Output dir: -previewDir <path> on the command line, else <repo>/HapticGateFrames.
public static class HapticGatePreview
{
    const int Width = 1080, Height = 1920;
    const int CropX = 40, CropY = 330, CropW = 1000, CropH = 1260, Down = 4;
    const float Dt = 1f / 60f;

    static Camera cam;
    static RenderTexture rt;
    static splashScene card;
    static string dir;

    public static void Render()
    {
        dir = null;
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-previewDir") dir = args[i + 1];
        if (dir == null) dir = Path.Combine(Application.dataPath, "../../HapticGateFrames");
        Directory.CreateDirectory(dir);

        EditorSceneLoader.Open("spashS7", OpenSceneMode.Single);
        splashScene.BuildGateInEditMode = true;
        splashScene.LoadScene = name => Debug.Log("[GATE PREVIEW] scene load -> " + name);
        card = Object.FindFirstObjectByType<splashScene>();
        cam = card.GetComponentInParent<Camera>();
        card.ApplyLayout(Width, Height, new Rect(0, 0, Width, Height));
        var canvas = card.wordsScaler.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 0.5f;
        canvas.scaleFactor = card.wordsScaler.scaleFactor;

        rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.aspect = (float)Width / Height;

        // natural: 12 frames across the intro
        card.Restart();
        var times = new float[12];
        for (int i = 0; i < 12; i++) times[i] = 0.05f + i * 0.2f;
        Save("natural", Strip(times, null, 6));

        // tap 1 at 0.40 s; frames after it
        card.Restart();
        Save("tap1", Strip(new[] { 0.42f, 0.46f, 0.52f, 0.60f, 0.72f, 0.9f }, new[] { 0.40f }, 6));

        // tap 2 at 0.8 (tap 1 at 0.4)
        card.Restart();
        Save("tap2", Strip(new[] { 0.82f, 0.86f, 0.92f, 1.0f, 1.12f, 1.3f }, new[] { 0.40f, 0.80f }, 6));

        // tap 3 at 1.2: crack 3 beat then the smash
        card.Restart();
        Save("tap3", Strip(new[] { 1.22f, 1.26f, 1.30f, 1.35f, 1.41f, 1.48f, 1.56f, 1.63f }, new[] { 0.40f, 0.80f, 1.20f }, 8));

        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        EditorApplication.Exit(0);
    }

    static Texture2D Strip(float[] frames, float[] taps, int cols)
    {
        int fw = CropW / Down, fh = CropH / Down;
        int rows = (frames.Length + cols - 1) / cols;
        var strip = new Texture2D(fw * cols, fh * rows, TextureFormat.RGB24, false);
        var fill = new Color32[strip.width * strip.height];
        for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(30, 0, 40, 255);
        strip.SetPixels32(fill);
        int ti = 0, f = 0;
        while (f < frames.Length)
        {
            // let the taps land a hair before their time so the frame after shows the stage
            if (taps != null && ti < taps.Length && card.Sim.time >= taps[ti] - 0.0001f) { card.Tap(); ti++; }
            card.Step(Dt);
            if (card.Sim.time >= frames[f] - 0.0001f)
            {
                Canvas.ForceUpdateCanvases();
                cam.Render();
                RenderTexture.active = rt;
                var grab = new Texture2D(CropW, CropH, TextureFormat.RGB24, false);
                grab.ReadPixels(new Rect(CropX, CropY, CropW, CropH), 0, 0);
                RenderTexture.active = null;
                var px = grab.GetPixels32();
                Object.DestroyImmediate(grab);
                int col = f % cols, row = rows - 1 - f / cols;
                for (int y = 0; y < fh; y++)
                    for (int x = 0; x < fw; x++)
                    {
                        int r = 0, g = 0, b = 0;
                        for (int dy = 0; dy < Down; dy++)
                            for (int dx = 0; dx < Down; dx++)
                            {
                                var c = px[(y * Down + dy) * CropW + x * Down + dx];
                                r += c.r; g += c.g; b += c.b;
                            }
                        int n = Down * Down;
                        strip.SetPixel(col * fw + x, row * fh + y, new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255));
                    }
                Debug.Log("[GATE PREVIEW] frame t=" + card.Sim.time.ToString("F2") + " crack=" + card.Sim.crackStage + " open=" + card.Sim.open.ToString("F2"));
                f++;
            }
            if (card.Sim.finished && f < frames.Length) break;   // the card is over; nothing more to draw
        }
        strip.Apply();
        return strip;
    }

    static void Save(string name, Texture2D strip)
    {
        string file = Path.Combine(dir, name + ".png");
        File.WriteAllBytes(file, strip.EncodeToPNG());
        Object.DestroyImmediate(strip);
        Debug.Log("[GATE PREVIEW] wrote " + file);
    }
}
