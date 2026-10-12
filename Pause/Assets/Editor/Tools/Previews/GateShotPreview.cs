using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Full-frame captures of the HapticGate splash at real phone / tablet sizes.
//   -executeMethod GateShotPreview.Render -shotDir <dir> -shotPrefix <before|after>
// Writes <prefix>_<device>_<name>.png for a few moments of the gate film (0.2, 1, 2, 3 s and the
// held final frame at 3.6 s) and for taps 1..3 (sparks, steam, the smash flash over the final frame).
// Devices: phone 1080x2400, Z Flip 1080x2520, tablet 1600x2560.
public static class GateShotPreview
{
    const float Dt = 1f / 60f;
    static Camera cam; static RenderTexture rt; static splashScene card;

    public static void Render()
    {
        string dir = "GateShots", prefix = "shot";
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-shotDir") dir = args[i + 1];
            if (args[i] == "-shotPrefix") prefix = args[i + 1];
        }
        Directory.CreateDirectory(dir);
        EditorSceneLoader.Open("spashS7", OpenSceneMode.Single);
        splashScene.BuildGateInEditMode = true;
        splashScene.LoadScene = name => { };
        card = Object.FindFirstObjectByType<splashScene>();
        cam = card.GetComponentInParent<Camera>();
        var canvas = card.wordsScaler.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 0.5f;

        var ft = Resources.Load<Texture2D>(GateFilm.PathFor(46));
        Debug.Log("[GATE SHOT] film frame texture " + ft.width + "x" + ft.height + " format " + ft.format + " mips " + ft.mipmapCount + " mem " + UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(ft));
        Device("phone", 1080, 2400, dir, prefix);
        Device("flip", 1080, 2520, dir, prefix);
        Device("tablet", 1600, 2560, dir, prefix);
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        EditorApplication.Exit(0);
    }

    static void Device(string dev, int w, int h, string dir, string prefix)
    {
        if (rt != null) Object.DestroyImmediate(rt);
        rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 1;
        cam.targetTexture = rt; cam.aspect = (float)w / h;
        card.ApplyLayout(w, h, new Rect(0, 0, w, h));
        canvasScale();
        card.Restart();
        Run(new[] { 0.2f, 1f, 2f, 2.6f, 3f, 3.6f }, new[] { "t0.2s", "t1.0s", "t2.0s", "t2.6s", "t3.0s", "t3.6s_final" }, null, dir, prefix, dev);
        // taps at 0.4 / 0.8 / 1.2 s; frames right after each
        card.Restart();
        Run(new[] { 0.45f, 0.85f, 1.27f }, new[] { "after_tap1", "after_tap2", "after_tap3_smash" }, new[] { 0.40f, 0.80f, 1.20f }, dir, prefix, dev);
    }

    static void canvasScale() { card.wordsScaler.GetComponent<Canvas>().scaleFactor = card.wordsScaler.scaleFactor; }

    static void Run(float[] times, string[] names, float[] taps, string dir, string prefix, string dev)
    {
        int ti = 0, f = 0;
        while (f < times.Length)
        {
            if (taps != null && ti < taps.Length && card.Sim.time >= taps[ti] - 0.0001f) { card.Tap(); ti++; }
            card.Step(Dt);
            if (card.Sim.time >= times[f] - 0.0001f)
            {
                Canvas.ForceUpdateCanvases();
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, prefix + "_" + dev + "_" + names[f] + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Debug.Log("[GATE SHOT] " + dev + " " + names[f] + " t=" + card.Sim.time.ToString("F2") + " frame=" + card.Film.ShownFrame + " crack=" + card.Sim.crackStage);
                f++;
            }
            if (card.Sim.finished) break;
        }
    }
}
