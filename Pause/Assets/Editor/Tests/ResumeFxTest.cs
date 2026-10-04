using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Feature: "when the ship is coming off pause and slowly winding up to
// current speed, we need an indicator to show that's happening".
//
// ResumeFx is the indicator for ResumeSlowMo's window: a MAGENTA edge
// vignette, side speed streaks that stretch as speed returns and hull
// afterimages (ResumeFxView, under every gameplay renderer), plus the SPEED
// row reading "SLOW-MO n" with a spool bar (ResumeSpeedRow). It must be on
// exactly while ResumeSlowMo is, follow its time factor, snap off on a
// re-pause, never take input and never sit over the hearts, gun charge or
// HUD at any screen size.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ResumeFxTest.Run
public static class ResumeFxTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[RFX] PASS  " : "[RFX] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    const float Dt = 1f / 60f;
    static float clock;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            Sprites();
            ActiveExactlyDuringSlowMo();
            SnapsOffOnRepause();
            NoInputBlocking();
            NoPerFrameAllocation();
            SortsUnderGameplay();
            ScreenSizes();
            HudRowStaysInsideTheSpeedRow();
            Bootstrap();
        }
        finally
        {
            ResumeSlowMo.ClockOverride = null;
            ResumeSlowMo.ResetRun();
            Time.timeScale = 1f;
        }
        Debug.Log("[RFX] failures: " + fails);
        return fails;
    }

    // ---- fixtures ------------------------------------------------------

    static void Fresh(float speed)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        clock = 10f;
        ResumeSlowMo.ClockOverride = () => clock;
        ResumeSlowMo.ResetRun();
        buttonClicks.playerDied = false;
        startMenu.youAreInTutorial = false;
        moveBackGround.speed = speed;
    }

    class Rig
    {
        public Camera cam;
        public ResumeFxView view;
        public SpriteRenderer hull;
        public Text speedText;
        public ResumeSpeedRow row;
    }

    static Rig MakeRig(int w = 1080, int h = 2520)
    {
        var rig = new Rig();
        var camGo = new GameObject("cam");
        rig.cam = camGo.AddComponent<Camera>();
        rig.cam.orthographic = true;
        rig.cam.transform.position = new Vector3(0f, 0f, -10f);
        SetScreen(rig.cam, w, h);

        var hullGo = new GameObject("hull");
        rig.hull = hullGo.AddComponent<SpriteRenderer>();
        rig.hull.sprite = Resources.Load<Sprite>(ResumeFx.StreakSprite); // any sprite
        hullGo.transform.position = new Vector3(0.5f, -3.5f, 0f);

        rig.view = ResumeFxView.Create();
        rig.view.CameraOverride = rig.cam;
        rig.view.HullOverride = rig.hull;

        var canvas = new GameObject("hud", typeof(Canvas)).GetComponent<Canvas>();
        var textGo = new GameObject("SpeedText", typeof(RectTransform), typeof(Text));
        textGo.transform.SetParent(canvas.transform, false);
        rig.speedText = textGo.GetComponent<Text>();
        rig.speedText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        rig.speedText.color = AkiraPalette.Cyan;
        rig.speedText.text = ResumeSpeedRow.SpeedLabel(ResumeSpeedRow.HudSpeed);
        rig.row = ResumeSpeedRow.Attach(rig.speedText);
        return rig;
    }

    static void SetScreen(Camera cam, int w, int h)
    {
        cam.orthographicSize = CameraFit.ComputeSize(5f, 2.85f, w, h);
        cam.aspect = (float)w / h;
    }

    // One frame: moveBackGround's decision, then everyone's LateUpdate.
    static float Frame(Rig rig, bool running)
    {
        clock += Dt;
        Time.timeScale = running ? ResumeSlowMo.Apply(1f) : ResumeSlowMo.Freeze();
        rig.view.Tick(ResumeFx.Intensity, Dt);
        // HudStyler's Update writes the plain row first, then the row's LateUpdate.
        rig.speedText.text = ResumeSpeedRow.SpeedLabel(ResumeSpeedRow.HudSpeed);
        rig.row.Refresh(ResumeFx.Factor, clock);
        return Time.timeScale;
    }

    static void Frames(Rig rig, int n, bool running) { for (int i = 0; i < n; i++) Frame(rig, running); }

    static void LaunchedAndPaused(Rig rig)
    {
        Frames(rig, 5, false);
        Frames(rig, 90, true);
        Frames(rig, 30, false);
    }

    static bool AnyShown(Rig rig)
    {
        if (rig.view.Vignette.enabled) return true;
        foreach (var s in rig.view.Streaks) if (s.enabled) return true;
        foreach (var g in rig.view.GhostRenderers) if (g.enabled) return true;
        return rig.row.Bar.enabled || rig.speedText.text.StartsWith(ResumeSpeedRow.SlowLabel);
    }

    static float MaxStreakLength(Rig rig)
    {
        float m = 0f;
        foreach (var s in rig.view.Streaks) if (s.enabled) m = Mathf.Max(m, s.bounds.size.y);
        return m;
    }

    // ---- cases -----------------------------------------------------------

    static void Sprites()
    {
        var v = Resources.Load<Sprite>(ResumeFx.VignetteSprite);
        var s = Resources.Load<Sprite>(ResumeFx.StreakSprite);
        Check("vignette sprite imported", v != null);
        Check("streak sprite imported", s != null);
        if (s != null) Check("streak pivots on its head (bottom)", Mathf.Approximately(s.pivot.y, 0f));
        foreach (var path in new[] { "resume_vignette.png", "resume_streak.png" })
        {
            var imp = AssetImporter.GetAtPath(ResumeFxArtImporter.Folder + path) as TextureImporter;
            Check(path + ": point-filtered pixel art, no mips",
                  imp != null && imp.filterMode == FilterMode.Point && !imp.mipmapEnabled);
        }
    }

    static void ActiveExactlyDuringSlowMo()
    {
        Fresh(0.23f); // HUD 23
        var rig = MakeRig();
        Frames(rig, 5, false);
        Check("idle before launch: nothing shown", !AnyShown(rig) && ResumeFx.Intensity == 0f);
        Frames(rig, 90, true);
        Check("the launch / countdown shows nothing", !AnyShown(rig));
        Frames(rig, 30, false);
        Check("paused: nothing shown", !AnyShown(rig));
        Check("idle row reads SPEED  23", rig.speedText.text == "SPEED  23");

        bool onExactly = true, intensityMatches = true, alphaMatches = true, fillMatches = true;
        bool lengthMonotonic = true, countUpMonotonic = true, labelOk = true, ghostsFollow = true;
        float firstLen = -1f, lastLen = 0f, prevLen = 0f, firstIntensity = -1f;
        int prevShown = -1, framesOn = 0, firstShown = -1;
        int guard = 0;
        do
        {
            float scale = Frame(rig, true);
            bool active = ResumeSlowMo.IsActive;
            bool shown = rig.view.Shown;
            if (active != shown || shown != rig.row.Showing) onExactly = false;
            if (!active) break;
            framesOn++;

            float expected = (1f - scale) / (1f - ResumeSlowMo.SlowScale);
            float i = ResumeFx.Intensity;
            if (firstIntensity < 0f) firstIntensity = i;
            if (Mathf.Abs(i - expected) > 1e-4f) intensityMatches = false;
            if (Mathf.Abs(rig.view.Vignette.color.a - ResumeFx.VignetteAlpha * i) > 1e-4f) alphaMatches = false;
            if (Mathf.Abs(rig.row.Bar.fillAmount - scale) > 1e-4f || !rig.row.Bar.enabled) fillMatches = false;

            float len = MaxStreakLength(rig);
            if (firstLen < 0f) firstLen = len;
            if (len < prevLen - 1e-4f) lengthMonotonic = false;
            prevLen = lastLen = len;

            int n = ResumeSpeedRow.ShownSpeed(23, scale);
            if (rig.speedText.text != ResumeSpeedRow.SlowMoLabel(n)) labelOk = false;
            if (firstShown < 0) firstShown = n;
            if (n < prevShown) countUpMonotonic = false;
            prevShown = n;

            foreach (var g in rig.view.GhostRenderers)
                if (!g.enabled || g.sprite != rig.hull.sprite || g.transform.position.y > rig.hull.transform.position.y)
                    ghostsFollow = false;
        } while (guard++ < 1000);

        float expectFrames = ResumeSlowMo.TotalSeconds / Dt;
        Check("indicator is on for the whole slow-mo window (" + framesOn + " frames, ~" + expectFrames.ToString("F0") + ")",
              Mathf.Abs(framesOn - expectFrames) <= 2f);
        Check("indicator is on exactly while ResumeSlowMo is active (world layer and HUD row)", onExactly);
        Check("full intensity at 0.6x (" + firstIntensity + ")", Mathf.Approximately(firstIntensity, 1f));
        Check("intensity = (1 - factor) / 0.4 every frame", intensityMatches);
        Check("vignette alpha follows intensity", alphaMatches);
        Check("spool bar fill = time factor", fillMatches);
        Check("streaks stretch as speed returns (" + firstLen.ToString("F2") + " -> " + lastLen.ToString("F2") + ")",
              lengthMonotonic && firstLen <= ResumeFx.StreakMinLength * 1.6f + 1e-3f && lastLen > ResumeFx.StreakMaxLength * 0.6f);
        Check("row reads SLOW-MO with the effective speed", labelOk);
        Check("speed counts back up (" + firstShown + " -> " + prevShown + ")",
              countUpMonotonic && firstShown == Mathf.RoundToInt(23 * 0.6f) && prevShown >= 22);
        Check("afterimages trail below the hull", ghostsFollow);

        Check("off once back to speed", !AnyShown(rig) && Time.timeScale == 1f);
        Check("row back to SPEED  23 in its idle colour",
              rig.speedText.text == "SPEED  23" && rig.speedText.color == AkiraPalette.Cyan);
        Frames(rig, 30, true);
        Check("stays off", !AnyShown(rig));

        // Below 15 there is no slow-mo, so no indicator.
        Fresh(0.12f);
        rig = MakeRig();
        LaunchedAndPaused(rig);
        Frame(rig, true);
        Check("HUD 12: resume shows nothing", !AnyShown(rig));
    }

    static void SnapsOffOnRepause()
    {
        Fresh(0.3f);
        var rig = MakeRig();
        LaunchedAndPaused(rig);
        Frame(rig, true);
        Frames(rig, 10, true);
        Check("(on during the hold)", rig.view.Shown && rig.row.Showing);
        Frame(rig, false);
        Check("re-pause during the hold snaps everything off that frame", !AnyShown(rig));
        Check("row reads SPEED  30 again, no punch", rig.speedText.text == "SPEED  30" &&
              Mathf.Approximately(rig.speedText.rectTransform.localScale.x, 1f));

        Frames(rig, 10, false);
        Frame(rig, true);
        Check("next resume brings it back at full strength",
              rig.view.Shown && Mathf.Approximately(ResumeFx.Intensity, 1f));
        Frames(rig, 40, true); // into the ramp
        Check("(in the ramp)", ResumeFx.Intensity > 0f && ResumeFx.Intensity < 1f);
        Frame(rig, false);
        Check("re-pause during the ramp snaps off too", !AnyShown(rig));

        // Death and the cinematic cancel ResumeSlowMo; the indicator follows.
        Frames(rig, 5, false);
        Frame(rig, true);
        buttonClicks.playerDied = true;
        Frame(rig, false);
        Check("death snaps it off", !AnyShown(rig));
        buttonClicks.playerDied = false;
    }

    static void NoInputBlocking()
    {
        Fresh(0.3f);
        var rig = MakeRig();
        LaunchedAndPaused(rig);
        Frames(rig, 3, true);
        Check("view has no colliders",
              rig.view.GetComponentsInChildren<Collider2D>(true).Length == 0 &&
              rig.view.GetComponentsInChildren<Collider>(true).Length == 0);
        Check("view has no canvas / raycaster / UI graphics",
              rig.view.GetComponentsInChildren<Canvas>(true).Length == 0 &&
              rig.view.GetComponentsInChildren<GraphicRaycaster>(true).Length == 0 &&
              rig.view.GetComponentsInChildren<Graphic>(true).Length == 0);
        Check("spool bar is not a raycast target", rig.row.Bar != null && !rig.row.Bar.raycastTarget);
        bool rowGraphicsQuiet = true;
        foreach (var g in rig.speedText.GetComponentsInChildren<Graphic>(true))
            if (g != rig.speedText && g.raycastTarget) rowGraphicsQuiet = false;
        Check("nothing the row adds catches touches", rowGraphicsQuiet);
    }

    static void NoPerFrameAllocation()
    {
        Fresh(0.3f);
        var rig = MakeRig();
        LaunchedAndPaused(rig);
        Frames(rig, 60, true);   // one full window: warms the label cache
        Frames(rig, 10, false);
        Frame(rig, true);
        Frames(rig, 3, true);
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 30; i++)
        {
            rig.view.Tick(ResumeFx.IntensityFor(Mathf.Lerp(0.6f, 0.99f, i / 30f)), Dt);
            rig.row.Refresh(Mathf.Lerp(0.6f, 0.99f, i / 30f), clock + i * Dt);
        }
        long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
        Check("view + HUD row allocate nothing per frame (" + bytes + " bytes over 30 frames)", bytes == 0);
    }

    static void SortsUnderGameplay()
    {
        Fresh(0.3f);
        var rig = MakeRig();
        LaunchedAndPaused(rig);
        Frames(rig, 3, true);

        int hullOrder = rig.hull.sortingOrder;
        // ShipLivesIndicator: hull + 2; ChargeIndicator: hull + 4; exhaust: hull - 1.
        int lowestGameplay = hullOrder - 1;
        int backdropTop = BackdropCatalog.BaseOrder + 10 * 20;
        bool under = true, overBackdrop = true, defaultLayer = true;
        foreach (var r in rig.view.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (r.sortingOrder >= lowestGameplay) under = false;
            if (r.sortingOrder <= backdropTop) overBackdrop = false;
            if (r.sortingLayerName != "Default") defaultLayer = false;
        }
        Check("every FX renderer sorts below the hull, its exhaust, hearts and charge", under);
        Check("and above the world backdrop", overBackdrop);
        Check("all on the Default sorting layer (same as the ship)", defaultLayer);

        // With the real hearts / charge view on a ship, still underneath.
        var shipGo = new GameObject("ship");
        var shipHull = shipGo.AddComponent<SpriteRenderer>();
        shipHull.sprite = rig.hull.sprite;
        shipHull.sortingOrder = 7; // whatever the hull uses, the ghosts follow it
        rig.view.HullOverride = shipHull;
        Frames(rig, 10, false);
        Frame(rig, true);
        bool ghostsUnder = true;
        foreach (var g in rig.view.GhostRenderers)
            if (!g.enabled || g.sortingOrder >= shipHull.sortingOrder - 1) ghostsUnder = false;
        Check("afterimages stay under a hull with any sorting order (and its exhaust)", ghostsUnder);
        Check("afterimages are not parented to the ship (hearts/slot code never sees them)",
              shipGo.GetComponentsInChildren<SpriteRenderer>(true).Length == 1);
    }

    static void ScreenSizes()
    {
        Fresh(0.3f);
        var rig = MakeRig();
        LaunchedAndPaused(rig);
        Frame(rig, true);
        foreach (var s in TallScreenTest.Screens)
        {
            SetScreen(rig.cam, s.w, s.h);
            Frames(rig, 3, true);
            float halfH = rig.cam.orthographicSize, halfW = halfH * rig.cam.aspect;
            var vb = rig.view.Vignette.bounds;
            Check(s.name + ": vignette covers the whole view",
                  Mathf.Abs(vb.size.x - 2f * halfW) < 1e-3f && Mathf.Abs(vb.size.y - 2f * halfH) < 1e-3f &&
                  Mathf.Abs(vb.center.x) < 1e-3f && Mathf.Abs(vb.center.y) < 1e-3f);
            bool lanes = true;
            foreach (var st in rig.view.Streaks)
            {
                var b = st.bounds;
                // Side lanes only: inside the rails, clear of the middle third
                // (the bottom-centre thumb area and the ship's usual line).
                if (Mathf.Abs(b.center.x) < 1.15f) lanes = false;
                if (b.max.x > Mathf.Min(halfW, ResumeFx.RailInnerEdge) || b.min.x < -Mathf.Min(halfW, ResumeFx.RailInnerEdge))
                    lanes = false;
            }
            Check(s.name + ": streaks run in the side lanes inside the rails", lanes);
        }
        // Fold covers (the narrowest) still keep the lanes away from the centre.
        Check("lane geometry: inner edge clears the thumb zone", ResumeFx.LaneInner - 0.1f > 1.15f);
    }

    // The HUD half lives inside the SPEED row of the real gameS1 read-out.
    static void HudRowStaysInsideTheSpeedRow()
    {
        ResumeSlowMo.ClockOverride = null;
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        var go = new GameObject("~HudResumeTest");
        var styler = go.AddComponent<HudStyler>();
        styler.SendMessage("Start");
        styler.SendMessage("Update");
        var speedGo = SceneUtil.FindAny("SpeedText");
        var text = speedGo != null ? speedGo.GetComponent<Text>() : null;
        var row = speedGo != null ? speedGo.GetComponent<ResumeSpeedRow>() : null;
        Check("gameS1: HudStyler attaches the resume row to SPEED", row != null && row.Bar != null);
        if (row == null || text == null) return;
        Check("gameS1: row still reads SPEED when idle", text.text.StartsWith("SPEED"));

        var bar = row.Bar.rectTransform;
        Check("spool bar is a child of the SPEED row",
              bar.parent == text.transform);
        Check("spool bar is anchored inside the row's own rect (bottom strip)",
              bar.anchorMin.x >= 0f && bar.anchorMax.x <= 1f && bar.anchorMin.y >= 0f && bar.anchorMax.y <= 0.2f &&
              bar.offsetMin == Vector2.zero && bar.offsetMax == Vector2.zero);

        // The longest label fits the row on one line (the panel never grows).
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(styler.HudRoot);
        var settings = text.GetGenerationSettings(new Vector2(10000f, text.rectTransform.rect.height));
        float longest = text.cachedTextGeneratorForLayout.GetPreferredWidth(ResumeSpeedRow.SlowMoLabel(58), settings) /
                        text.pixelsPerUnit;
        float rowWidth = text.rectTransform.rect.width;
        Check("'SLOW-MO  58' fits the SPEED row (" + longest.ToString("F0") + " <= " + rowWidth.ToString("F0") + ")",
              longest <= rowWidth + 0.5f);

        // Screen sizes: the read-out (and so the row inside it) is placed by
        // HudStyler for every test screen; the quick actions stay clear of it.
        foreach (var s in TallScreenTest.Screens)
        {
            var screen = new Vector2(s.w, s.h);
            var safe = new Rect(0, 0, s.w, s.h);
            var canvas = styler.HudRoot.GetComponentInParent<Canvas>().rootCanvas;
            float scale = HudStyler.HudCanvasScale(canvas, canvas.GetComponent<CanvasScaler>(), screen);
            var hud = HudStyler.HudScreenRect(safe, screen, scale, styler.HudRoot.rect.size);
            var actions = PauseQuickActions.ScreenRectFor(safe, screen);
            Check(s.name + ": the SPEED row's read-out stays clear of the quick actions", !hud.Overlaps(actions));
        }
        Object.DestroyImmediate(go);
    }

    static void Bootstrap()
    {
        var src = System.IO.File.ReadAllText("Assets/Scripts/Gameplay/ResumeFx.cs");
        Check("bootstrap only builds the view in gameS1", ResumeFxBootstrap.Scene == "gameS1" &&
              src.Contains("if (scene.name != Scene) return;"));
        var styler = System.IO.File.ReadAllText("Assets/Scripts/UI/HudStyler.cs");
        Check("HudStyler attaches ResumeSpeedRow", styler.Contains("ResumeSpeedRow.Attach(speedText);"));
    }
}
