using System.IO;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// The tutorial's first seconds as they really play (fly-in, damaged start, the
// first press), stepped frame by frame with the world frozen while the finger
// is up, rendered through the real camera: shows what floats beside the ship
// at the start and how a tap-then-jump moves the ship.
//
//   scripts/unity-batch.sh -projectPath <abs>/Pause -executeMethod TutorialFlowPreview.Run
//   (output: $TUTORIAL_FLOW_DIR, else Builds/TutorialFlow)
public static class TutorialFlowPreview
{
    const int Width = 1080, Height = 2340;
    const float Dt = 1f / 60f;
    const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    static bool pressed;
    static Vector2 screenPos;
    static string dir;
    static GameObject ship;
    static Hints hints;
    static movePlayerInTut mover;
    static MethodInfo hintsUpdate, moverUpdate;
    static ShipLivesIndicator hearts;
    static RenderTexture rt;
    static bool heartsBuilt;
    static ShipDamageFx damageFx;

    static void Own(object c, string method)
    {
        if (c == null) return;
        var m = c.GetType().GetMethod(method, Inst);
        if (m != null) m.Invoke(c, null);
    }

    public static void Run()
    {
        dir = System.Environment.GetEnvironmentVariable("TUTORIAL_FLOW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/TutorialFlow";
        Directory.CreateDirectory(dir);
        using (new TestHarness.Sandbox())
        {
            try { Flow(); }
            finally
            {
                TouchInput.PressedOverride = null;
                TouchInput.PositionOverride = null;
                Hints.DeltaOverride = null;
                movePlayerInTut.DeltaOverride = null;
                Time.timeScale = 1f;
            }
        }
        UnityEditor.EditorApplication.Exit(0);
    }

    static void Flow()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/tutorialS5.unity", OpenSceneMode.Single);
        Time.timeScale = 1f;
        buttonClicks.playerDied = false;
        moveBackGround.speed = 0f;
        score.pauseCounter = score.TUTPAUSECOUNTER;
        score.paysRealDust = false;
        startMenu.youAreInTutorial = true;
        ShipId.TutorialFlight = true;
        Hints.PlayFlyIn = true;
        collisionDetection.MAXLIFE = -1;
        collisionDetection.lifeCounter = 0;
        PlayerInvuln.Reset();
        TouchInput.PressedOverride = () => pressed;
        TouchInput.PositionOverride = () => screenPos;
        Hints.DeltaOverride = () => Dt;
        movePlayerInTut.DeltaOverride = () => Dt;
        RunScore.BeginRun(false, false);

        var cam = Camera.main;
        cam.orthographic = true;
        cam.aspect = Width / (float)Height;
        cam.orthographicSize = CameraFit.GameplayHalfWidth * Height / Width;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        var theme = WorldManager.Worlds[0];
        var wb = WorldBackdrop.Create(theme.displayName);
        wb.Show(theme.displayName, false);
        WorldPainter.Apply(theme);
        for (int i = 0; i < 300; i++) wb.Step(1f / 60f);
        rt = new RenderTexture(Width, Height, 24);

        ship = GameObject.Find("ship1");
        mover = ship.GetComponent<movePlayerInTut>();
        TutorialShipHull.Apply();
        Own(ship.GetComponent<lifeControler>(), "Start");
        Own(ship.GetComponent<collisionDetection>(), "Start");
        Own(mover, "Start");
        ship.transform.position = new Vector3(0f, -2.6f, 0f);
        hints = Object.FindFirstObjectByType<Hints>();
        hints.introDelay = -1f; hints.readSeconds = -1f; hints.liftOffSeconds = -1f;
        var dfx = ship.GetComponent<ShipDamageFx>();
        bool fxAfter = System.Environment.GetEnvironmentVariable("TUTORIAL_FLOW_FX_AFTER") == "1";
        if (dfx != null && !fxAfter) dfx.Build();
        hints.SendMessage("Start");
        Debug.Log("[FLOW] ship scale after Hints.Start " + ship.transform.localScale + " fx " + (dfx != null));
        if (dfx != null && fxAfter) dfx.Build();
        if (dfx != null) Debug.Log("[FLOW] damage fx hullSize " + dfx.HullSize + " (state " + dfx.State + ")");
        damageFx = dfx;
        Object.FindFirstObjectByType<spawnGoodStuffTut>().SendMessage("Start");
        var paused = GameObject.Find("paused");
        bool lateHearts = System.Environment.GetEnvironmentVariable("TUTORIAL_FLOW_LATE_HEARTS") == "1";
        hearts = ship.GetComponent<ShipLivesIndicator>() ?? ship.AddComponent<ShipLivesIndicator>();
        if (!lateHearts)
        {
            Own(hearts, "Start");
            // the hearts exist (and show every life) before the tutorial takes its first one
            Own(hearts, "Update");
        }
        hintsUpdate = typeof(Hints).GetMethod("Update", Inst);
        moverUpdate = typeof(movePlayerInTut).GetMethod("Update", Inst);

        float clock = 0f;
        float[] shots = { .05f, .5f, 1.2f, 2.0f, 2.8f, 3.5f };
        int next = 0;
        while (clock < 3.6f)
        {
            // a finger that lands early, during the fly-in (the world runs while it is down)
            bool early = System.Environment.GetEnvironmentVariable("TUTORIAL_FLOW_EARLY") == "1" && clock > .1f && clock < float.Parse(System.Environment.GetEnvironmentVariable("TUTORIAL_FLOW_EARLY_END") ?? "1.6", System.Globalization.CultureInfo.InvariantCulture);
            if (early) { Finger(new Vector3(.8f, -1.5f, 0f)); }
            pressed = early;
            if (lateHearts && clock > .15f && !heartsBuilt) { heartsBuilt = true; Own(hearts, "Start"); }
            Tick(false);
            clock += Dt;
            if (next < shots.Length && clock >= shots[next]) Shot("start_" + next + "_t" + clock.ToString("0.00"));
            if (next < shots.Length && clock >= shots[next]) next++;
        }
        pressed = false;
        // first press at a spot, then hold, then lift and tap elsewhere
        Debug.Log("[FLOW] ship at " + ship.transform.position + " scale " + ship.transform.localScale);
        Finger(new Vector3(1.0f, -1.0f, 0f));
        pressed = true;
        for (int i = 0; i < 40; i++) { Tick(true); if (i == 2 || i == 8 || i == 16 || i == 39) Shot("press_" + i); }
        pressed = false;
        for (int i = 0; i < 10; i++) Tick(false);
        Finger(new Vector3(-1.2f, 2.0f, 0f));
        pressed = true;
        Debug.Log("[FLOW] tap target (finger world) " + new Vector3(-1.2f, 2.0f, 0f) + " before ship " + ship.transform.position);
        for (int i = 0; i < 40; i++)
        {
            Tick(true);
            Debug.Log("[FLOW] tap frame " + i + " ship " + ship.transform.position.ToString("F2"));
            if (i == 0 || i == 6 || i == 12 || i == 39) Shot("tap_" + i);
        }
    }

    static void Finger(Vector3 world)
    {
        var sp = Camera.main.WorldToScreenPoint(world);
        screenPos = new Vector2(sp.x, sp.y);
    }

    // One frame: scaled time is zero while the finger is up (the world is frozen).
    static void Tick(bool live)
    {
        bool moving = pressed || score.pauseCounter <= 0;
        float scaled = moving ? Dt : 0f;
        moverUpdate.Invoke(mover, null);
        hintsUpdate.Invoke(hints, null);
        var lc = ship.GetComponent<lifeControler>();
        if (damageFx != null)
        {
            if (lc != null) Own(lc, "Update");
            damageFx.GetType().GetMethod("Tick", Inst).Invoke(damageFx, new object[] { scaled });
        }
        if (hearts.enabled && heartsBuilt || System.Environment.GetEnvironmentVariable("TUTORIAL_FLOW_LATE_HEARTS") != "1") Own(hearts, "Update");
        else return;
        hearts.GetType().GetMethod("Place", Inst).Invoke(hearts, new object[] { Dt, scaled });
        hearts.StepBreaks(scaled);
    }

    static void Shot(string name)
    {
        var cam = Camera.main;
        cam.targetTexture = rt;
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var png = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        png.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        png.Apply();
        RenderTexture.active = old;
        cam.targetTexture = null;
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), png.EncodeToPNG());
        Object.DestroyImmediate(png);
    }
}
