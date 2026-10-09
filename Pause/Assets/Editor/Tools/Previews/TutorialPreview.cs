using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The tutorial as a player sees it, without a build: tutorialS5 with the real
// Space world backdrop (as the runtime bootstrap sets it up), the robot and
// its brass bubble speaking a few of the script's lines, and the pickups the
// steps teach. One PNG per step into $TUTORIAL_PREVIEW_DIR (else
// Builds/TutorialPreview), 1080x2340, plus a crop of the robot and bubble.
//
//   scripts/unity-batch.sh -projectPath <abs>/Pause -executeMethod TutorialPreview.Run
public static class TutorialPreview
{
    const int Width = 1080, Height = 2340;
    const float Sf = 1.777f;   // canvas scale a 1080x2340 phone gets from the 800x1000 / 0.5 scaler
    static readonly string[] Shots = { "hold", "freeze", "dust", "heal", "refill", "enemies" };

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("TUTORIAL_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/TutorialPreview";
        Directory.CreateDirectory(dir);
        using (new TestHarness.Sandbox())
        // The overlay canvas is laid out in world space here (batch mode has no
        // 1080x2340 screen), so canvas units stand for screen pixels / Sf.
        using (ScreenInfo.Override(Mathf.RoundToInt(Width / Sf), Mathf.RoundToInt(Height / Sf),
               new Rect(0f, 90f / Sf, Width / Sf, (Height - 90f - 130f) / Sf)))
        {
            for (int i = 0; i < Shots.Length; i++) Step(dir, i);
        }
        EditorApplication.Exit(0);
    }

    static T Field<T>(object o, string name)
    {
        return (T)o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o);
    }

    static void SetField(object o, string name, object v)
    {
        o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(o, v);
    }

    static void Call(object o, string name, params object[] args)
    {
        o.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(o, args);
    }

    static void Step(string dir, int index)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/tutorialS5.unity", OpenSceneMode.Single);
        var step = System.Array.Find(TutorialScript.Steps, s => s.id == Shots[index]);
        Time.timeScale = 1f;
        moveBackGround.speed = 0f;

        var cam = Camera.main;
        cam.orthographic = true;
        cam.aspect = Width / (float)Height;
        cam.orthographicSize = CameraFit.GameplayHalfWidth * Height / Width;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;

        // What the runtime bootstrap does for tutorialS5.
        var theme = WorldManager.Worlds[0];
        var wb = WorldBackdrop.Create(theme.displayName);
        wb.Show(theme.displayName, false);
        WorldPainter.Apply(theme);
        for (int i = 0; i < 900; i++) wb.Step(1f / 60f);

        // The pickup a step teaches.
        int pickup = step.id == "dust" ? 4 : step.id == "heal" ? 3 : step.id == "shield" ? 0 : step.id == "refill" ? 1 : -1;
        if (pickup >= 0)
        {
            AtomClarityPreview.SpawnPickup(pickup, new Vector3(0f, -.8f, 0f));
            if (step.id == "dust") AtomClarityPreview.SpawnPickup(5, new Vector3(1.6f, -2.2f, 0f));
        }

        var paused = GameObject.Find("paused");
        if (paused != null) paused.SetActive(false);   // the scene's template PAUSED glow, not part of this shot
        // The canvas scales from the camera's target, so give it one first.
        var rt = new RenderTexture(Width, Height, 24);
        cam.targetTexture = rt;
        var speaker = RobotSpeaker.Create(null);
        var canvas = speaker.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cam;
        canvas.scaleFactor = 1f;
        var crt = (RectTransform)canvas.transform;
        crt.sizeDelta = new Vector2(Width / Sf, Height / Sf);
        crt.position = new Vector3(0f, 0f, -1f);
        crt.localScale = Vector3.one * Sf * (2f * CameraFit.GameplayHalfWidth / Width);
        Call(speaker, "Fit", true);
        var guides = TutorialGuides.Create(speaker.Root);
        if (step.id == "hold") guides.ShowTouch(true);

        var line = TutorialScript.Speak(step.line);
        speaker.Say(line);
        // Talking: half the syllables spoken (mouth shape, lamp, pulse) for
        // the second half of the shots, a finished line for the rest.
        bool talking = index % 2 == 1;
        int spoken = talking ? Mathf.Max(1, line.SyllableCount / 2) : line.SyllableCount;
        SetField(speaker, "robotShownAt", -10f);
        SetField(speaker, "bubbleShownAt", -10f);
        for (int i = 0; i < spoken; i++)
        {
            SetField(speaker, "nextSyllableAt", 0f);
            Call(speaker, "Update");
        }
        if (!talking) speaker.CompleteLine();
        // Settle the pop-in and keep the speaking pose.
        SetField(speaker, "robotShownAt", -10f);
        SetField(speaker, "bubbleShownAt", -10f);
        SetField(speaker, "nextSyllableAt", float.MaxValue);
        Canvas.ForceUpdateCanvases();
        Call(speaker, "Update");
        Canvas.ForceUpdateCanvases();

        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var png = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        png.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        png.Apply();
        RenderTexture.active = old;
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        string stem = Path.Combine(dir, (index + 1) + "-" + step.id);
        File.WriteAllBytes(stem + ".png", png.EncodeToPNG());

        Debug.Log("[TUTORIAL-PREVIEW] canvas sf=" + canvas.scaleFactor + " robot=" + speaker.Robot.position + " scale=" + speaker.Robot.localScale
                  + " bubble=" + speaker.Bubble.position + " root=" + speaker.Root.rect + " cam=" + cam.pixelWidth + "x" + cam.pixelHeight
                  + " canvasPos=" + canvas.transform.position + " layer=" + canvas.gameObject.layer);
        // Crop around the robot and bubble, from their laid-out rects.
        var rc = new Vector3[4];
        float x0 = Width, x1 = 0f, y0 = Height, y1 = 0f;
        foreach (var rect in new[] { speaker.Robot, speaker.Bubble })
        {
            rect.GetWorldCorners(rc);
            foreach (var c in rc)
            {
                // camera pixel size is the batch window's, not the shot's: map by hand
                float unit = Width / (2f * CameraFit.GameplayHalfWidth);
                var p = new Vector3(Width * .5f + (c.x - cam.transform.position.x) * unit, Height * .5f + (c.y - cam.transform.position.y) * unit);
                x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x);
                y0 = Mathf.Min(y0, p.y); y1 = Mathf.Max(y1, p.y);
            }
        }
        const int pad = 40;
        int cx = Mathf.Clamp(Mathf.FloorToInt(x0) - pad, 0, Width - 1), cy = Mathf.Clamp(Mathf.FloorToInt(y0) - pad, 0, Height - 1);
        int cw = Mathf.Clamp(Mathf.CeilToInt(x1 - x0) + 2 * pad, 1, Width - cx), ch = Mathf.Clamp(Mathf.CeilToInt(y1 - y0) + 2 * pad, 1, Height - cy);
        var zoom = new Texture2D(cw, ch, TextureFormat.RGB24, false);
        zoom.SetPixels(png.GetPixels(cx, cy, cw, ch));
        zoom.Apply();
        File.WriteAllBytes(stem + "-robot.png", zoom.EncodeToPNG());
        Object.DestroyImmediate(zoom);
        Object.DestroyImmediate(png);
        Object.DestroyImmediate(speaker.gameObject);
        Debug.Log("[TUTORIAL-PREVIEW] " + stem + ".png (" + step.line + ")");
    }
}
