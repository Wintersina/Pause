using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Renders the max-speed streak's HUD cue (ScoreX2Cue) in its four states over
// four world backdrops, on a 1080x2400 phone:
//
//   1-not-started   speed at the cap for 0 s: no cue
//   2-charging-8s   8 s in: the slim bar under the score, half full
//   3-active-x2     the x2 plate beside the score
//   4-just-lost     a heart lost: the plate dimming
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ScoreX2Preview.Run
//   (writes to $SCORE_X2_PREVIEW_DIR, else Builds/ScoreX2Preview; each state as
//    a full screen and a 2x crop of the read-out)
public static class ScoreX2Preview
{
    const float Dt = 1f / 60f;
    static readonly Vector2Int Size = new Vector2Int(1080, 2400);
    static readonly Rect Safe = new Rect(0, 0, 1080, 2400 - 118);

    static readonly string[] Names = { "1-not-started", "2-charging-8s", "3-active-x2", "4-just-lost" };

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("SCORE_X2_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/ScoreX2Preview";
        Directory.CreateDirectory(dir);
        using (var sandbox = new TestHarness.Sandbox())
        {
            for (int i = 0; i < Names.Length; i++) Shot(i, i, Path.Combine(dir, Names[i]));
        }
        EditorApplication.Exit(0);
    }

    static void Shot(int state, int world, string path)
    {
        EditorSceneLoader.Open("gameS1");
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = false;
        score.pauseCounter = 3;
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(true, true);
        moveBackGround.speed = SpeedRamp.Cap;
        var screen = new Vector2(Size.x, Size.y);

        var camera = Camera.main;
        camera.aspect = Size.x / (float)Size.y;
        camera.orthographicSize = CameraFit.ComputeSize(CameraFit.AuthoredSize, CameraFit.GameplayHalfWidth, Size.x, Size.y);
        var theme = WorldManager.Worlds[world];
        var backdrop = new GameObject("~PreviewBackdrop").AddComponent<WorldBackdrop>();
        WorldPainter.Apply(theme);
        backdrop.Show(theme.displayName, false);
        for (int i = 0; i < 40; i++) backdrop.Step(Dt);

        var styler = new GameObject("~HudStyler").AddComponent<HudStyler>();
        styler.SendMessage("Start");
        styler.SendMessage("Update");
        var hud = styler.GetComponent<ScoreHud>();

        // some score on the clock, then the streak to the wanted state
        for (int i = 0; i < 40; i++) RunScore.OnDust(true);
        float seconds = state == 0 ? 0f : state == 1 ? 8f : 15.2f;
        int frames = Mathf.RoundToInt(seconds / Dt);
        for (int i = 0; i < frames; i++) RunScore.Tick(Dt, SpeedRamp.Cap);
        var cue = SceneUtil.FindAny(ScoreHud.RowName).GetComponent<ScoreX2Cue>();
        float now = 1000f;
        cue.Refresh(now);
        if (state == 2) cue.Refresh(now += .6f);   // settled
        if (state == 3)
        {
            cue.Refresh(now += .6f);
            ScoreMultiplier.OnHeartLost();
            cue.Refresh(now += .1f);
        }
        for (int i = 0; i < 400; i++) typeof(ScoreHud).GetMethod("TickDisplay", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Invoke(hud, new object[] { .05f });
        styler.SendMessage("Update");
        // at rest: edit mode's frozen clock would hold the badges' one-tick pop
        foreach (var rt in new[] { hud.SpeedBadge.rectTransform, hud.LoopBadge.rectTransform, hud.ScoreText.rectTransform })
            rt.localScale = Vector3.one;

        var target = new RenderTexture(Size.x, Size.y, 24);
        camera.targetTexture = target;
        camera.cullingMask = -1;
        var hudCanvas = styler.HudRoot.GetComponentInParent<Canvas>().rootCanvas;
        float hudScale = HudStyler.HudCanvasScale(hudCanvas, hudCanvas.GetComponent<CanvasScaler>(), screen);
        foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (canvas.renderMode == RenderMode.WorldSpace || !canvas.isRootCanvas) continue;
            var scaler = canvas.GetComponent<CanvasScaler>();
            float scale = HudStyler.HudCanvasScale(canvas, scaler, screen);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            if (scaler != null) scaler.enabled = false;
            canvas.scaleFactor = scale;
        }
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(styler.HudRoot);
        var band = TopBand.FrameFor(Safe, screen, 0f, null);
        HudStyler.ComputeHudLayout(band, screen, hudScale, styler.HudRoot.rect.size, out Vector2 at, out float fit);
        styler.HudRoot.anchoredPosition = at;
        styler.HudRoot.localScale = new Vector3(fit, fit, 1f);
        Canvas.ForceUpdateCanvases();
        cue.Remeasure();
        cue.Refresh(now);

        camera.Render();
        var old = RenderTexture.active;
        RenderTexture.active = target;
        var png = new Texture2D(Size.x, Size.y, TextureFormat.RGB24, false);
        png.ReadPixels(new Rect(0, 0, Size.x, Size.y), 0, 0);
        png.Apply();
        RenderTexture.active = old;
        File.WriteAllBytes(path + "-" + theme.displayName + ".png", png.EncodeToPNG());

        // a 2x nearest-neighbour crop of the read-out
        Rect r = HudStyler.HudScreenRect(band, screen, hudScale, styler.HudRoot.rect.size);
        int x0 = Mathf.Max(0, Mathf.FloorToInt(r.xMin) - 20), y0 = Mathf.Max(0, Mathf.FloorToInt(r.yMin) - 20);
        int w = Mathf.Min(Size.x - x0, Mathf.CeilToInt(r.width) + 40), h = Mathf.Min(Size.y - y0, Mathf.CeilToInt(r.height) + 40);
        var src = png.GetPixels32();
        var crop = new Texture2D(w * 2, h * 2, TextureFormat.RGB24, false);
        var dst = new Color32[w * 2 * h * 2];
        for (int y = 0; y < h * 2; y++)
            for (int x = 0; x < w * 2; x++)
                dst[y * w * 2 + x] = src[(y0 + y / 2) * Size.x + x0 + x / 2];
        crop.SetPixels32(dst);
        crop.Apply();
        File.WriteAllBytes(path + "-crop.png", crop.EncodeToPNG());

        camera.targetTexture = null;
        Object.DestroyImmediate(png);
        Object.DestroyImmediate(crop);
        Object.DestroyImmediate(target);
        Debug.Log("[X2PREVIEW] " + Path.GetFileName(path) + " state " + cue.State + " fill " + cue.Bar.fillAmount.ToString("F2"));
        RunScore.EndRun(RunScore.RunId);
        ScoreMultiplier.Reset();
    }
}
