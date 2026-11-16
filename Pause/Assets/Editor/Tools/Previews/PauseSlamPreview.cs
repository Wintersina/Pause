using System.IO;
using UnityEngine;
using UnityEngine.UI;

// Frames of the finger-lift "PAUSED!!" slam and the "-1" tick beside the PAUSES counter on a
// dark field with a stand-in HUD, for review.
//
//   scripts/unity-batch.sh -executeMethod PauseSlamPreview.Run
//   (writes to $PAUSESLAM_PREVIEW_DIR, else Builds/PauseSlamPreview)
public static class PauseSlamPreview
{
    const int Width = 1080, Height = 2340;
    static readonly float[] Ages = { 0.03f, 0.07f, 0.10f, 0.14f, 0.20f, 0.32f, 0.46f, 0.55f, 0.64f, 0.70f };

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("PAUSESLAM_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/PauseSlamPreview";
        Directory.CreateDirectory(dir);
        using (new TestHarness.Sandbox())
        using (ScreenInfo.Override(Width, Height, new Rect(0, 0, Width, Height), null, 420f))
        {
            var objs = new System.Collections.Generic.List<Object>();
            try { Capture(dir, objs); }
            finally
            {
                if (PauseSlam.Live != null) Object.DestroyImmediate(PauseSlam.Live.gameObject);
                foreach (var o in objs) if (o != null) Object.DestroyImmediate(o);
            }
        }
        UnityEditor.EditorApplication.Exit(0);
    }

    static void Capture(string dir, System.Collections.Generic.List<Object> objs)
    {
        var camGo = new GameObject("PreviewCam", typeof(Camera));
        objs.Add(camGo);
        var cam = camGo.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = AkiraPalette.Night1;
        cam.orthographic = true;
        var rt = new RenderTexture(Width, Height, 24);
        cam.targetTexture = rt;

        // stand-in HUD: the PAUSES row top-left, on its own world-space-free canvas
        var hudCanvasGo = new GameObject("PreviewHud", typeof(Canvas));
        objs.Add(hudCanvasGo);
        var hc = hudCanvasGo.GetComponent<Canvas>();
        hc.renderMode = RenderMode.ScreenSpaceCamera; hc.worldCamera = cam; hc.planeDistance = 5;
        var counterGo = new GameObject("PauseCounter", typeof(RectTransform), typeof(Text));
        counterGo.transform.SetParent(hudCanvasGo.transform, false);
        var counter = counterGo.GetComponent<Text>();
        counter.font = WorldBanner.OrbitronOrBuiltin();
        counter.fontSize = 48; counter.fontStyle = FontStyle.Bold; counter.color = AkiraPalette.RedHi;
        counter.alignment = TextAnchor.MiddleLeft; counter.horizontalOverflow = HorizontalWrapMode.Overflow;
        var crt = (RectTransform)counterGo.transform;
        crt.anchorMin = crt.anchorMax = new Vector2(0, 1); crt.pivot = new Vector2(0, .5f);
        crt.sizeDelta = new Vector2(500, 70); crt.anchoredPosition = new Vector2(60, -160);
        var ink = counterGo.AddComponent<Outline>(); ink.effectColor = AkiraPalette.Ink; ink.effectDistance = new Vector2(3, -3);

        buttonClicks.playerDied = false;
        score.RunHasStarted = true;
        score.pauseCounter = 5;
        counter.text = "PAUSES  5";

        foreach (float age in Ages)
        {
            // restart the slam and run it up to this age in 60 fps steps
            PauseSlam.OnResumed();
            PauseSlam.OnPaused(null);
            var canvas = PauseSlam.Live.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 1;
            for (float t = 0f; t < age - 1e-4f; t += 1f / 60f) PauseSlam.Advance(Mathf.Min(1f / 60f, age - t));
            counter.text = "PAUSES  " + PauseSlam.ShownPauses(5);
            counter.rectTransform.sizeDelta = counter.rectTransform.sizeDelta;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            Save(rt, Path.Combine(dir, "frame_" + Mathf.RoundToInt(age * 1000f).ToString("D4") + "ms.png"));
            PauseSlam.OnResumed();
            PauseSlam.Advance(0.1f);
            score.pauseCounter = 5;
            PauseSlam.Advance(0.1f);
        }
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
    }

    static void Save(RenderTexture rt, string path)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }
}
