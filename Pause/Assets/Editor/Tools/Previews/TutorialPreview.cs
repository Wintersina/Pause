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
// Builds/TutorialPreview), 1080x2340, plus a 2x crop of the robot and bubble.
//
//   scripts/unity-batch.sh -projectPath <abs>/Pause -executeMethod TutorialPreview.Run
public static class TutorialPreview
{
    const int Width = 1080, Height = 2340;
    static readonly string[] Shots = { "hold", "freeze", "dust", "heal", "refill", "enemies" };

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("TUTORIAL_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/TutorialPreview";
        Directory.CreateDirectory(dir);
        using (new TestHarness.Sandbox())
        using (ScreenInfo.Override(Width, Height, new Rect(0f, 90f, Width, Height - 90f - 130f)))
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

        var speaker = RobotSpeaker.Create(null);
        var canvas = speaker.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 1f;
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

        var png = AtomClarityPreview.Shoot(cam, Width, Height);
        string stem = Path.Combine(dir, (index + 1) + "-" + step.id);
        File.WriteAllBytes(stem + ".png", png.EncodeToPNG());

        // 2x crop of the robot and bubble, found from the laid-out rects.
        var corners = new Vector3[4];
        speaker.Root.GetWorldCorners(corners);
        int cropH = 560;
        var crop = png.GetPixels(0, Height - 90 - cropH - 40, Width, cropH);
        var zoom = new Texture2D(Width, cropH, TextureFormat.RGB24, false);
        zoom.SetPixels(crop);
        zoom.Apply();
        File.WriteAllBytes(stem + "-robot.png", zoom.EncodeToPNG());
        Object.DestroyImmediate(zoom);
        Object.DestroyImmediate(png);
        Object.DestroyImmediate(speaker.gameObject);
        Debug.Log("[TUTORIAL-PREVIEW] " + stem + ".png (" + step.line + ")");
    }
}
