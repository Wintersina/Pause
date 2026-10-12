using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Full-frame captures of the HapticGate splash at real phone / tablet sizes.
//   -executeMethod GateShotPreview.Render -shotDir <dir> -shotPrefix <before|after>
// Writes <prefix>_<device>_<name>.png for a few moments (natural, mid-open, taps 1..3).
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

        var dt = Resources.Load<Texture2D>(GateArt.DoorSlot);
        Debug.Log("[GATE SHOT] door texture " + dt.width + "x" + dt.height + " format " + dt.format + " mips " + dt.mipmapCount + " mem " + UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(dt));
        Device("phone", 1080, 2520, dir, prefix);
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
        // natural at 1.2 s and 1.5 s (mid-open)
        card.Restart();
        if (dev == "phone") Debug.Log("[GATE SHOT] leaf scale " + card.View.leftLeaf.localScale);
        Run(new[] { 1.2f, 1.4f, 1.55f, 1.8f, 2.15f }, new[] { "natural_1.2s", "wordmark_emerging_1.4s", "wordmark_midflight_1.55s", "wordmark_landing_1.8s", "final_2.15s" }, null, dir, prefix, dev);
        // taps at 0.4 / 0.8 / 1.2 s; frames right after each
        card.Restart();
        Run(new[] { 0.55f, 0.95f, 1.27f }, new[] { "after_tap1", "after_tap2", "after_tap3_crack3" }, new[] { 0.40f, 0.80f, 1.20f }, dir, prefix, dev);
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
                Debug.Log("[GATE SHOT] " + dev + " " + names[f] + " t=" + card.Sim.time.ToString("F2") + " crack=" + card.Sim.crackStage);
                f++;
            }
            if (card.Sim.finished) break;
        }
    }
}
