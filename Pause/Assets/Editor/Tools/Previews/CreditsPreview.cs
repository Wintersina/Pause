using System.IO;
using UnityEditor;
using UnityEngine;

// Credits screen at fixed times after it opens, on a 1080x2400 phone (the
// ScreenFit rig: device-sized render, canvases scaled as on the phone), driven
// by a fake clock (1/60 s steps through CreditsMove.Step, which also advances
// the HapticGate gate-opening animation at the head of the roll):
//   credits_t0.3.png, credits_t2.0.png, credits_t5.0.png  (+ any -times list)
// Output dir: -previewDir <path>, else <repo>/CreditsFrames.
//   scripts/unity-batch.sh -executeMethod CreditsPreview.Render -previewDir /abs/dir
public static class CreditsPreview
{
    const float Dt = 1f / 60f;

    public static void Render()
    {
        string dir = null;
        float[] times = { 0.3f, 2f, 5f };
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-previewDir") dir = args[i + 1];
            if (args[i] == "-times")
            {
                var parts = args[i + 1].Split(',');
                times = new float[parts.Length];
                for (int k = 0; k < parts.Length; k++) times[k] = float.Parse(parts[k], System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        if (dir == null) dir = Path.Combine(Application.dataPath, "../../CreditsFrames");
        Directory.CreateDirectory(dir);

        FitScreen screen = null;
        foreach (var s in ScreenFitScreens.All) if (s.id == "credits") screen = s;
        var device = FitDevice.Find("and-1080x2400-gesture");
        EditorSceneLoader.Open(screen.scene);
        using (var rig = new ScreenFitRig(device, screen.MinHalfWidth))
        {
            UiScaleFloor.AttachScene();
            rig.Sync();
            screen.stage(rig);
            rig.Sync();
            var mover = Object.FindFirstObjectByType<CreditsMove>();
            float t = 0f;
            foreach (float at in times)
            {
                while (t < at - 0.0001f) { mover.Step(Dt); t += Dt; }
                var tex = rig.Capture(0, false);
                string file = Path.Combine(dir, "credits_t" + at.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ".png");
                File.WriteAllBytes(file, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                var text = mover.GetComponent<UnityEngine.UI.Text>();
                Rect r = rig.PixelRect(text.rectTransform);
                Debug.Log("[CREDITS PREVIEW] t=" + t.ToString("F2") + " text top px=" + r.yMax.ToString("F0") +
                          " frame=" + mover.GateFrame + " wrote " + file);
            }
        }
        EditorApplication.Exit(0);
    }
}
