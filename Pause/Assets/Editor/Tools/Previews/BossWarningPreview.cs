using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Renders gameS1's HUD with the boss warning in each of its states, for
// review without a device:
//
//   <size>-1-announce-<world>.png   the banner, a second in
//   <size>-2-countdown.png          the chip, T-24
//   <size>-3-t10-<world>.png        T-9, on the beat
//   <size>-4-t3.png                 T-3 / 2 / 1 with the big numeral
//   <size>-5-paused.png             T-17 with the quick actions up (paused)
//   <size>-6-handoff.png            the first tick of the hand-off
//
// at 1080x2520 and 1080x1920.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod BossWarningPreview.Run
//   (writes to $BOSS_WARNING_PREVIEW_DIR, else Builds/BossWarningPreview)
public static class BossWarningPreview
{
    const float Dt = 1f / 60f;

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("BOSS_WARNING_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/BossWarningPreview";
        Directory.CreateDirectory(dir);
        using (var sandbox = new TestHarness.Sandbox())
        {
            foreach (var size in new[] { new Vector2Int(1080, 2520), new Vector2Int(1080, 1920) })
            {
                string tag = Path.Combine(dir, size.x + "x" + size.y);
                for (int w = 0; w < WorldManager.Worlds.Length; w++)
                {
                    string world = WorldManager.Worlds[w].displayName.ToLowerInvariant();
                    // Frost and Ember with their names known, the others still secret
                    Shot(size, w, 30f - 1.1f, w % 2 == 1, false, false, tag + "-1-announce-" + world + ".png");
                    Shot(size, w, 8.97f, false, false, false, tag + "-3-t10-" + world + ".png");
                }
                Shot(size, 0, 24.4f, false, false, false, tag + "-2-countdown.png");
                Shot(size, 3, 2.97f, false, false, false, tag + "-4-t3-ember.png");
                Shot(size, 1, 1.6f, false, false, false, tag + "-4-t2-frost.png");
                Shot(size, 2, 16.5f, false, true, false, tag + "-5-paused-verdant.png");
                Shot(size, 0, .05f, false, false, true, tag + "-6-handoff.png");
            }
        }
        EditorApplication.Exit(0);
    }

    static void Shot(Vector2Int size, int world, float shown, bool known, bool paused, bool handoff, string path)
    {
        EditorSceneLoader.Open("gameS1");
        buttonClicks.playerDied = false;
        score.pauseCounter = paused ? 3 : 0;
        moveBackGround.speed = .31f;
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        BossWarningConfig.RevealNameBeforeFirstSight = known;

        var screen = new Vector2(size.x, size.y);
        var safe = new Rect(0, 0, size.x, size.y);

        var styler = new GameObject("~HudStyler").AddComponent<HudStyler>();
        styler.SendMessage("Start");
        styler.SendMessage("Update");
        var actions = new GameObject("~PauseQuickActions").AddComponent<PauseQuickActions>();
        actions.SendMessage("Start");
        foreach (var name in new[] { "replayQuickAction", "leaveQuickAction" })
        {
            var go = SceneUtil.FindAny(name);
            if (go != null) go.SetActive(paused);
        }
        var actionSafe = SceneUtil.FindAny("SafeArea");
        if (actionSafe != null)
        {
            var rt = actionSafe.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        var hud = BossWarningHud.Build();
        hud.BindHud(styler.HudRoot);

        var camera = Camera.main;
        var target = new RenderTexture(size.x, size.y, 24);
        camera.targetTexture = target;
        camera.cullingMask = -1;   // the scene camera skips the UI layer (its canvases are overlays)
        camera.aspect = size.x / (float)size.y;
        camera.orthographicSize = CameraFit.ComputeSize(camera.orthographicSize, 2.85f, size.x, size.y);

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

        Vector2 hudSize = styler.HudRoot.rect.size;
        HudStyler.ComputeHudLayout(safe, screen, hudScale, hudSize, out Vector2 at, out float fit);
        styler.HudRoot.anchoredPosition = at;
        styler.HudRoot.localScale = new Vector3(fit, fit, 1f);
        hud.ApplyLayout(BossWarningHud.ComputeLayout(safe, screen, HudStyler.HudScreenRect(safe, screen, hudScale, hudSize)));

        // fly the countdown from T-30 to the moment wanted
        float eta = 30f;
        hud.Step(BossWarningInput.Ahead, eta, Dt, Dt);
        while (hud.Countdown.Shown > shown)
        {
            eta -= Dt;
            hud.Step(BossWarningInput.Ahead, eta, Dt, Dt);
        }
        if (paused) for (int i = 0; i < 90; i++) hud.Step(BossWarningInput.Ahead, eta, 0f, Dt);
        if (handoff) hud.Step(BossWarningInput.Arriving, 0f, 0f, Dt);

        Canvas.ForceUpdateCanvases();
        camera.Render();
        var old = RenderTexture.active;
        RenderTexture.active = target;
        var png = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
        png.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
        png.Apply();
        File.WriteAllBytes(path, png.EncodeToPNG());
        RenderTexture.active = old;
        camera.targetTexture = null;
        Object.DestroyImmediate(png);
        Object.DestroyImmediate(target);
        Debug.Log("[PREVIEW] " + path + "  chip " + hud.CurrentLayout.chip + "  banner " + hud.CurrentLayout.banner);
    }
}
