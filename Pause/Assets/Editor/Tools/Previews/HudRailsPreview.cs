using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Renders gameS1's top band (score read-out, home / replay, the boss chip)
// over the real rails for a set of screen shapes and display cutouts, with
// the safe area (green), the cutouts (magenta) and the rails' inner edges
// (cyan) drawn over the picture:
//
//   <shape>-<cutout>-before.png   the band pinned to the safe area's corners
//   <shape>-<cutout>-after.png    the band inside the rails, under cutouts (TopBand)
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod HudRailsPreview.Run
//   (writes to $HUD_RAILS_PREVIEW_DIR, else Builds/HudRailsPreview)
public static class HudRailsPreview
{
    const float Dt = 1f / 60f;

    static readonly (string name, Vector2Int size)[] Shapes =
    {
        ("flip7-1080x2520", new Vector2Int(1080, 2520)),
        ("1080x2400", new Vector2Int(1080, 2400)),
        ("1080x1920", new Vector2Int(1080, 1920)),
        ("iphone15-1179x2556", new Vector2Int(1179, 2556)),
        ("iphone15pm-1290x2796", new Vector2Int(1290, 2796)),
        ("iphonese-750x1334", new Vector2Int(750, 1334)),
        ("foldopen-1812x2176", new Vector2Int(1812, 2176)),
        ("ipad-1536x2048", new Vector2Int(1536, 2048)),
        ("720x1280", new Vector2Int(720, 1280)),
    };

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("HUD_RAILS_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/HudRailsPreview";
        Directory.CreateDirectory(dir);
        using (var sandbox = new TestHarness.Sandbox())
        {
            int n = 0;
            foreach (var shape in Shapes)
            {
                var cases = NextFeatures0907Test.CutoutCases(new Vector2(shape.size.x, shape.size.y));
                for (int c = 0; c < cases.Length; c++)
                {
                    string tag = Path.Combine(dir, shape.name + "-" + cases[c].name.Replace(", ", "-").Replace(' ', '-'));
                    int world = n++ % WorldManager.Worlds.Length;
                    Shot(shape.size, world, cases[c].safe, cases[c].cutouts, false, tag + "-before.png");
                    Shot(shape.size, world, cases[c].safe, cases[c].cutouts, true, tag + "-after.png");
                }
            }
        }
        EditorApplication.Exit(0);
    }

    static void Shot(Vector2Int size, int world, Rect safe, Rect[] cutouts, bool inRails, string path)
    {
        EditorSceneLoader.Open("gameS1");
        buttonClicks.playerDied = false;
        score.pauseCounter = 3;
        moveBackGround.speed = .31f;
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        var screen = new Vector2(size.x, size.y);

        // the view and the rails as this phone shows them
        var camera = Camera.main;
        camera.aspect = size.x / (float)size.y;
        camera.orthographicSize = CameraFit.ComputeSize(CameraFit.AuthoredSize, CameraFit.GameplayHalfWidth, size.x, size.y);
        var theme = WorldManager.Worlds[world];
        var backdropGo = new GameObject("~PreviewBackdrop");
        var backdrop = backdropGo.AddComponent<WorldBackdrop>();
        WorldPainter.Apply(theme);
        foreach (var wall in new[] { GameObject.Find("leftPipe"), GameObject.Find("rightPipe") })
        {
            if (wall == null) continue;
            var s = wall.transform.localScale;
            s.y = camera.orthographicSize * 2f * 1.085f / wall.GetComponent<MeshFilter>().sharedMesh.bounds.size.y;   // RailFit
            wall.transform.localScale = s;
            RailFit.RefreshTextureTiling(wall);
        }
        BossRails.Measure();
        backdrop.Show(theme.displayName, false);
        for (int i = 0; i < 30; i++) backdrop.Step(Dt);
        float edge = BossRails.InnerEdge;

        var styler = new GameObject("~HudStyler").AddComponent<HudStyler>();
        styler.SendMessage("Start");
        styler.SendMessage("Update");
        var actions = new GameObject("~PauseQuickActions").AddComponent<PauseQuickActions>();
        actions.SendMessage("Start");
        foreach (var name in new[] { "replayQuickAction", "leaveQuickAction" })
        {
            var go = SceneUtil.FindAny(name);
            if (go != null) go.SetActive(true);
        }
        // the scene's own buttons are only templates (moveStarsBackground hides them in play)
        foreach (var name in new[] { "replayWhenPausedButton", "mainMenuWhenPausedButton" })
        {
            var go = SceneUtil.FindAny(name);
            if (go != null) go.SetActive(false);
        }
        var hud = BossWarningHud.Build();
        hud.BindHud(styler.HudRoot);

        var target = new RenderTexture(size.x, size.y, 24);
        camera.targetTexture = target;
        camera.cullingMask = -1;   // the scene camera skips the UI layer (its canvases are overlays)

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

        // before: the band at the safe area's corners; after: inside the rails, under the cutouts
        var band = inRails ? TopBand.FrameFor(safe, screen, edge, cutouts) : TopBand.FrameFor(safe, screen, 0f, null);
        Vector2 hudSize = styler.HudRoot.rect.size;
        HudStyler.ComputeHudLayout(band, screen, hudScale, hudSize, out Vector2 at, out float fit);
        styler.HudRoot.anchoredPosition = at;
        styler.HudRoot.localScale = new Vector3(fit, fit, 1f);
        actions.PlaceFor(safe, screen, band);
        Rect read = HudStyler.HudScreenRect(band, screen, hudScale, hudSize);
        hud.ApplyLayout(BossWarningHud.ComputeLayout(safe, screen, read, band));

        // the chip up, T-24
        float eta = 30f;
        hud.Step(BossWarningInput.Ahead, eta, Dt, Dt);
        while (hud.Countdown.Shown > 24.4f)
        {
            eta -= Dt;
            hud.Step(BossWarningInput.Ahead, eta, Dt, Dt);
        }

        Canvas.ForceUpdateCanvases();
        camera.Render();
        var old = RenderTexture.active;
        RenderTexture.active = target;
        var png = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
        png.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
        png.Apply();
        RenderTexture.active = old;

        // overlays
        var px = png.GetPixels32();
        float halfW = CameraFit.GameplayViewHalfWidth(screen);
        float railL = size.x * .5f - edge / halfW * size.x * .5f;
        var cyan = new Color32(0, 255, 255, 255);
        Fill(px, size, new Rect(railL - 1f, 0f, 3f, size.y), cyan, 1f);
        Fill(px, size, new Rect(size.x - railL - 1f, 0f, 3f, size.y), cyan, 1f);
        foreach (var cut in cutouts) Fill(px, size, cut, new Color32(255, 0, 255, 255), .75f);
        Outline(px, size, safe, new Color32(0, 255, 60, 255), 4);
        png.SetPixels32(px);
        png.Apply();
        File.WriteAllBytes(path, png.EncodeToPNG());

        camera.targetTexture = null;
        Object.DestroyImmediate(png);
        Object.DestroyImmediate(target);
        Rect buttons = PauseQuickActions.ScreenRectFor(band, screen);
        Debug.Log("[HUDRAILS] " + Path.GetFileName(path) + "  rails " + railL.ToString("F0") + ".." + (size.x - railL).ToString("F0") +
                  "  read-out " + read + " (x" + fit.ToString("F3") + ")  buttons " + buttons + "  chip " + hud.CurrentLayout.chip +
                  (hud.CurrentLayout.chipInBand ? " in band" : " under icons"));
    }

    static void Fill(Color32[] px, Vector2Int size, Rect r, Color32 c, float a)
    {
        int x0 = Mathf.Clamp(Mathf.RoundToInt(r.xMin), 0, size.x), x1 = Mathf.Clamp(Mathf.RoundToInt(r.xMax), 0, size.x);
        int y0 = Mathf.Clamp(Mathf.RoundToInt(r.yMin), 0, size.y), y1 = Mathf.Clamp(Mathf.RoundToInt(r.yMax), 0, size.y);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                px[y * size.x + x] = Color32.Lerp(px[y * size.x + x], c, a);
    }

    static void Outline(Color32[] px, Vector2Int size, Rect r, Color32 c, int t)
    {
        Fill(px, size, new Rect(r.xMin, r.yMin, r.width, t), c, 1f);
        Fill(px, size, new Rect(r.xMin, r.yMax - t, r.width, t), c, 1f);
        Fill(px, size, new Rect(r.xMin, r.yMin, t, r.height), c, 1f);
        Fill(px, size, new Rect(r.xMax - t, r.yMin, t, r.height), c, 1f);
    }
}
