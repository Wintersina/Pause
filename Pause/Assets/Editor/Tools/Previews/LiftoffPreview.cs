using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders a lift-off (Liftoff: Frost's, or LIFTOFF_WORLD=2 Verdant's) in gameS1 -- backdrop, rails, the ship
// -- as the game camera sees it on a phone, for review:
//
//   moment-NN-<name>.png   ten full-size frames: spool-up, climb, deep
//                          cloud, flash, burst, clearing into space, the
//                          planet receding, the interlude's calm, the gateway
//                          opening (the next planet's approach, or the
//                          portal), the gateway waiting
//   frames/fNNN.png        the whole sequence at 12 fps, quarter size
//
//   LIFTOFF_PREVIEW_DIR=<dir> Unity -batchmode -quit -projectPath Pause
//       -executeMethod LiftoffPreview.Run
//   (LIFTOFF_DEVICES=flip7-1080x2520,... picks the screens. The UI is not
//    drawn: it is a screen overlay.)
public static class LiftoffPreview
{
    const float Dt = 1f / 60f;

    // Seconds after the boss is over (LiftoffTimeline's clock); the last two
    // run on past the gateway.
    static readonly (string name, float at)[] Moments =
    {
        ("spool-up", 1.4f), ("climb", 2.2f), ("deep-cloud", 3.6f), ("flash", 4.32f), ("burst", 4.6f),
        ("clearing-into-space", 5.0f), ("planet-receding", 6.0f), ("interlude-calm", 8.2f),
        ("portal-opening", 10.3f), ("portal-waiting", 13.5f),
    };

    static int world = 1;

    public static void Run()
    {
        if (!int.TryParse(System.Environment.GetEnvironmentVariable("LIFTOFF_WORLD") ?? "1", out world)) world = 1;
        string dir = System.Environment.GetEnvironmentVariable("LIFTOFF_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/LiftoffPreview";
        string devices = System.Environment.GetEnvironmentVariable("LIFTOFF_DEVICES");
        if (string.IsNullOrEmpty(devices)) devices = "flip7-1080x2520";
        int failures = 0;
        using (new TestHarness.Sandbox())
        {
            try
            {
                foreach (string id in devices.Split(','))
                {
                    var d = FitDevice.Find(id.Trim());
                    if (d == null) { Debug.LogError("[LIFTOFF-PREVIEW] no device " + id); failures++; continue; }
                    string sub = Path.Combine(dir, d.id);
                    Directory.CreateDirectory(Path.Combine(sub, "frames"));
                    Render(d, sub);
                }
            }
            catch (System.Exception e) { Debug.LogException(e); failures++; }
            finally
            {
                BossEncounter.ResetRun();
                PortalPressure.Reset();
                BossRails.Reset();
                PlayField.Reset();
                ScreenInfo.ClearOverride();
                buttonClicks.playerDied = false;
            }
        }
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    static void Render(FitDevice d, string dir)
    {
        int Frost = world;   // the world lifted off from
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity");
        BossEncounter.ResetRun();
        BossRails.Reset();
        PortalPressure.Reset();
        RunLoop.Reset();
        var cam = Camera.main;
        cam.aspect = d.Aspect;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, d.w, d.h);
        ScreenInfo.ClearOverride();
        ScreenInfo.Override(d.w, d.h, d.Safe, d.Cutouts, d.ReportedDpi, d.ios);
        PlayField.Reset();
        buttonClicks.playerDied = false;
        startMenu.youAreInTutorial = false;
        score.pauseCounter = 0;
        Time.timeScale = 1f;
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, Frost);
        Random.InitState(7);

        var paused = SceneUtil.FindAny("paused");
        if (paused != null) paused.SetActive(false);
        var theme = WorldManager.Worlds[Frost];
        WorldPainter.Apply(theme);
        foreach (var name in new[] { "leftPipe", "rightPipe" })
        {
            var wall = GameObject.Find(name);
            if (wall == null) continue;
            var s = wall.transform.localScale;
            s.y = cam.orthographicSize * 2f * 1.085f / wall.GetComponent<MeshFilter>().sharedMesh.bounds.size.y;   // RailFit
            wall.transform.localScale = s;
            RailFit.RefreshTextureTiling(wall);
        }
        BossRails.Measure();
        var wb = WorldBackdrop.Create(theme.displayName);
        wb.Show(theme.displayName, false);
        // the lift-off comes after the boss: the level's opening cloud ceiling
        // (FrostTuning.CeilingClearSeconds) is long gone by then
        for (int i = 0; i < (int)((FrostTuning.CeilingClearSeconds + 4f) / Dt); i++) wb.Step(Dt);

        var ship = new GameObject("~LoSpawner").AddComponent<spawnShips>().Spawn(ShipId.Starter);
        ship.transform.position = new Vector3(.9f, ShipReach.StartY + .6f, 0f);

        var wm = new GameObject("~WorldManager").AddComponent<WorldManager>();
        wm.SendMessage("Awake");
        const System.Reflection.BindingFlags Inst = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        typeof(WorldManager).GetField("levelBegun", Inst).SetValue(wm, true);
        typeof(BossEncounter).GetField("doneWorld", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .SetValue(null, Frost);
        typeof(WorldManager).GetField("distanceLeft", Inst).SetValue(wm, 0f);
        wm.EndLevel();
        var lift = Liftoff.Live;
        if (lift == null) throw new System.Exception("no lift-off opened for world " + world);

        int frame = 0, shot = 0, n = 0;
        float t = 0f;
        foreach (var m in Moments)
        {
            while (t < m.at - 1e-4f)
            {
                if (WorldManager.Flying) wm.Tick(Dt);
                if (lift != null && lift.State != Liftoff.Stage.Done) lift.Step(Dt);
                if (Liftoff.Live == null) lift = null;
                if (Portal.Live != null) Portal.Live.Step(Dt);
                if (lift == null && Planetfall.Live != null) Planetfall.Live.Step(Dt);
                wb.Step(Dt);
                t += Dt;
                if (frame++ % 5 == 0) Capture(cam, d.w / 4, d.h / 4, Path.Combine(dir, "frames", "f" + shot++.ToString("000") + ".png"));
            }
            n++;
            Capture(cam, d.w, d.h, Path.Combine(dir, "moment-" + n.ToString("00") + "-" + m.name + ".png"));
            Debug.Log(string.Format("[LIFTOFF-PREVIEW] {0}: t {1:F2} {2} cover {3:F2} boost {4:F2} backdrop {5} world {6} ship {7} portal {8}",
                                    m.name, t, lift != null ? lift.State.ToString() : "gone", LiftoffTimeline.Cover(t),
                                    WorldBackdrop.ScrollBoost, wb.Current != null ? wb.Current.Spec.world : "-",
                                    WorldManager.Current.displayName, ship.transform.position,
                                    Portal.Live != null ? Portal.Live.transform.position.ToString() :
                                    Planetfall.Live != null ? "planetfall " + Planetfall.Live.Def.openBanner + " " + Planetfall.Live.transform.position : "none"));
            if (lift != null && lift.PlumeRenderer.enabled)
                Debug.Log("[LIFTOFF-PREVIEW]   plume " + lift.PlumeRenderer.bounds + " order " + lift.PlumeRenderer.sortingOrder +
                          " colour " + lift.PlumeRenderer.color + " sprite " + lift.PlumeRenderer.sprite);
            if (lift != null && lift.PlanetRenderer.enabled)
                Debug.Log("[LIFTOFF-PREVIEW]   planet " + lift.PlanetRenderer.bounds + " limb " + lift.LimbRenderer.enabled);
        }
        Debug.Log("[LIFTOFF-PREVIEW] " + d.id + " done -> " + dir);
        Object.DestroyImmediate(wm.gameObject);
    }

    static void Capture(Camera cam, int width, int height, string file)
    {
        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previous = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var png = new Texture2D(width, height, TextureFormat.RGB24, false);
        png.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        png.Apply();
        File.WriteAllBytes(file, png.EncodeToPNG());
        RenderTexture.active = old;
        cam.targetTexture = previous;
        Object.DestroyImmediate(png);
        Object.DestroyImmediate(rt);
    }
}
