using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Renders the home screen's elite snipe run for review: a populated sky, an
// elite flying in, sniping every ship, zooming away and the traffic coming
// back -- then a ship diving into the PAUSE logo and the logo's shake.
// Phone-shaped (1080x2340); the menu canvas is drawn in the camera for the
// capture only (the scene is not saved).
//
//   elite/NNN.png        15 fps frames
//   elite_strip.png      a filmstrip of 12 moments of the run
//   logo_shake.png       the logo crash, 8 frames (one per 24 fps tick)
//
//   scripts/unity-batch.sh -executeMethod HomeElitePreview.Run
//   (writes to $HOME_PREVIEW_DIR, else Builds/HomeElitePreview)
public static class HomeElitePreview
{
    const float Fps = 15f;
    const int Px = 270;

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("HOME_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/HomeElitePreview";
        string frames = Path.Combine(dir, "elite");
        if (Directory.Exists(frames)) Directory.Delete(frames, true);
        Directory.CreateDirectory(frames);
        using (var sandbox = new TestHarness.Sandbox())
        {
            Clip(dir, frames);
        }
        EditorApplication.Exit(0);
    }

    static void Clip(string dir, string frames)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/startS4.unity", OpenSceneMode.Single);
        Random.InitState(4141);
        var cam = Camera.main;
        cam.aspect = 1080f / 2340f;
        cam.orthographicSize = CameraFit.ComputeSize(cam.orthographicSize, 2.85f, 1080, 2340);
        float halfH = cam.orthographicSize;

        MenuStyler.StyleScene();
        foreach (var cb in Object.FindObjectsByType<CodexHomeButton>(FindObjectsSortMode.None)) cb.Build();
        var panel = GameObject.Find("UIPanel");
        var canvas = panel.GetComponentInParent<Canvas>().rootCanvas;
        var scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler != null) scaler.enabled = false;
        canvas.renderMode = RenderMode.WorldSpace;
        cam.cullingMask |= 1 << canvas.gameObject.layer;
        canvas.sortingOrder = 1000;
        var crt = (RectTransform)canvas.transform;
        float w = 800f, h = w * 2340f / 1080f;
        crt.sizeDelta = new Vector2(w, h);
        float k = 2f * halfH / h;
        crt.localScale = new Vector3(k, k, 1f);
        crt.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 0f);
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)panel.transform);
        Canvas.ForceUpdateCanvases();

        var go = new GameObject("~TitleScreenTraffic");
        var t = go.AddComponent<TitleScreenTraffic>();
        t.plungeInterval = new Vector2(1e6f, 1e6f);
        t.Init();
        t.NextEliteAt = 1e9f;
        t.NextPlungeAt = 1e9f;
        for (int i = 0; i < 30 * 14; i++) t.Step(1f / 30f);   // warm, populated
        while (!t.TryStartElite()) t.Step(1f / 30f);
        Debug.Log("[EP] elite " + t.EliteDefNow.key + ", " + t.ShipsOnScreen + " ships on screen");

        float dt = 1f / Fps;
        int frame = 0;
        var strip = new System.Collections.Generic.List<Texture2D>();
        float stripEvery = 0f, nextStrip = 0f;
        bool backDone = false;
        float after = 0f;
        for (float s = 0f; s < 16f; s += dt)
        {
            t.Step(dt);
            foreach (var book in go.GetComponentsInChildren<ShipFlameFlipbook>()) book.Step(dt);
            var png = Shoot(cam);
            File.WriteAllBytes(Path.Combine(frames, (frame++).ToString("000") + ".png"), png.EncodeToPNG());
            // 12 moments: the run (dense while it snipes) and the return
            if (stripEvery == 0f) stripEvery = .45f;
            if (s >= nextStrip && strip.Count < 12) { strip.Add(png); nextStrip = s + (t.EliteBusy ? stripEvery : .8f); }
            else Object.DestroyImmediate(png);
            if (!t.EliteBusy) { after += dt; if (!backDone && after > 2.5f) { backDone = true; break; } }
        }
        Debug.Log("[EP] run frames " + frame + ", sniped " + t.EliteSnipes + ", shots " + t.EliteShotsFired);
        Sheet(strip, Path.Combine(dir, "elite_strip.png"), 6);

        // the logo shake: a ship dives into the lettering
        var shake = new System.Collections.Generic.List<Texture2D>();
        TitleScreenTraffic.Flyer diver = null;
        foreach (var f in t.Pool)
        {
            if (!f.active || f.layer == TitleScreenTraffic.Depth.Back || f.state != TitleScreenTraffic.State.Cruise) continue;
            if (t.StartPlunge(f, false, -1)) { diver = f; break; }
        }
        for (int i = 0; i < 30 * 5 && diver != null && t.LogoCrashes == 0; i++) t.Step(1f / 30f);
        for (int i = 0; i < 10 && t.LogoCrashes > 0; i++)
        {
            shake.Add(Shoot(cam));
            t.Step(1f / 24f);
        }
        Debug.Log("[EP] logo crashes " + t.LogoCrashes + ", shakes " + t.LogoShakes + ", peak shift " + t.PeakLogoShift + " tilt " + t.PeakLogoTilt);
        if (shake.Count > 0) Sheet(shake, Path.Combine(dir, "logo_shake.png"), 5);
        t.Shutdown();
        Object.DestroyImmediate(go);
    }

    static Texture2D Shoot(Camera cam)
    {
        int py = Mathf.RoundToInt(Px / cam.aspect);
        var rt = new RenderTexture(Px, py, 24);
        cam.targetTexture = rt;
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var png = new Texture2D(Px, py, TextureFormat.RGB24, false);
        png.ReadPixels(new Rect(0, 0, Px, py), 0, 0);
        png.Apply();
        RenderTexture.active = old;
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        return png;
    }

    static void Sheet(System.Collections.Generic.List<Texture2D> shots, string path, int cols)
    {
        if (shots.Count == 0) return;
        int w = shots[0].width, h = shots[0].height, gap = 4;
        int rows = (shots.Count + cols - 1) / cols;
        var sheet = new Texture2D(cols * (w + gap) - gap, rows * (h + gap) - gap, TextureFormat.RGB24, false);
        var fill = new Color32[sheet.width * sheet.height];
        for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(20, 20, 28, 255);
        sheet.SetPixels32(fill);
        for (int i = 0; i < shots.Count; i++)
        {
            int c = i % cols, r = rows - 1 - i / cols;
            sheet.SetPixels(c * (w + gap), r * (h + gap), w, h, shots[i].GetPixels());
        }
        sheet.Apply();
        File.WriteAllBytes(path, sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet);
        foreach (var s in shots) Object.DestroyImmediate(s);
    }
}
