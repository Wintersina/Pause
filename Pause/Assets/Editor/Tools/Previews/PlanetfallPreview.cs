using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders the Space -> Frost planetfall (Planetfall) in gameS1 -- backdrop,
// rails, the ship -- as the game camera sees it on a phone, for review:
//
//   moment-NN-<name>.png   ten full-size frames: planet far, planet near +
//                          cue, commit, limb, entry heat, deep cloud, flash,
//                          burst, clouds clearing, the Frost level's start
//   entry/eNN.png          the entry shroud round the ship, full size at
//                          30 fps from 2.0 s to 2.5 s and 4.2 s to 4.5 s
//                          (its animation and fit)
//   frames/fNNN.png        the whole descent at 12 fps, quarter size (a GIF
//                          or contact sheet is assembled from them outside
//                          Unity)
//
//   PLANETFALL_PREVIEW_DIR=<dir> Unity -batchmode -quit -projectPath Pause
//       -executeMethod PlanetfallPreview.Run
//   (PLANETFALL_DEVICES=flip7-1080x2520,and-1080x1920 picks the screens;
//    the first is the default. PLANETFALL_FROM=1 renders Frost -> Verdant:
//    the approach in the Space sky of Frost's lift-off interlude, the lift-off
//    itself skipped; default 0, Space -> Frost. The UI is not drawn: it is a
//    screen overlay.)
public static class PlanetfallPreview
{
    const float Dt = 1f / 60f;

    static readonly (string name, float at)[] Moments =
    {
        ("planet-far", .9f), ("planet-near-cue", 5.5f),   // approach seconds
    };
    static readonly (string name, float at)[] Descent =
    {
        ("commit", .55f), ("limb-start", 1.1f), ("limb-mid", 1.25f), ("limb", 1.45f), ("entry-heat", 2.05f), ("deep-cloud", 3.4f),
        ("breakthrough-flash", 5.0f), ("burst", 5.3f), ("cloud-clear", 5.85f), ("frost-start", 7.6f),
    };

    static int from;

    public static void Run()
    {
        int.TryParse(System.Environment.GetEnvironmentVariable("PLANETFALL_FROM") ?? "0", out from);
        from = Mathf.Clamp(from, 0, WorldManager.Worlds.Length - 2);
        LiftoffCatalog.Enabled = false;
        string dir = System.Environment.GetEnvironmentVariable("PLANETFALL_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/PlanetfallPreview";
        string devices = System.Environment.GetEnvironmentVariable("PLANETFALL_DEVICES");
        if (string.IsNullOrEmpty(devices)) devices = "flip7-1080x2520";
        int failures = 0;
        using (new TestHarness.Sandbox())
        {
            try
            {
                foreach (string id in devices.Split(','))
                {
                    var d = FitDevice.Find(id.Trim());
                    if (d == null) { Debug.LogError("[PLANETFALL-PREVIEW] no device " + id); failures++; continue; }
                    string sub = Path.Combine(dir, d.id);
                    Directory.CreateDirectory(Path.Combine(sub, "frames"));
                    Directory.CreateDirectory(Path.Combine(sub, "entry"));
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
                LiftoffCatalog.Enabled = true;
            }
        }
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    static void Render(FitDevice d, string dir)
    {
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
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, from);
        Random.InitState(7);

        // flying: the PAUSED icon is hidden in the game
        var paused = SceneUtil.FindAny("paused");
        if (paused != null) paused.SetActive(false);
        // the approach is always seen in Space's sky (a lift-off's interlude
        // shows Space's backdrop and rails before the gateway)
        var theme = WorldManager.Worlds[0];
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
        for (int i = 0; i < 240; i++) wb.Step(Dt);

        var wm = new GameObject("~WorldManager").AddComponent<WorldManager>();
        wm.SendMessage("Awake");
        const System.Reflection.BindingFlags Inst = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        typeof(WorldManager).GetField("levelBegun", Inst).SetValue(wm, true);
        typeof(BossEncounter).GetField("doneWorld", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .SetValue(null, from);
        typeof(WorldManager).GetField("distanceLeft", Inst).SetValue(wm, 0f);
        wm.EndLevel();
        var fall = Planetfall.Live;
        if (fall == null) throw new System.Exception("no planetfall opened from world " + from);

        var ship = new GameObject("~PfSpawner").AddComponent<spawnShips>().Spawn(ShipId.Starter);
        ship.transform.position = new Vector3(-1.1f, ShipReach.StartY, 0f);

        int frame = 0, shot = 0;
        float clock = 0f;
        foreach (var m in Moments)
        {
            while (clock < m.at - 1e-4f)
            {
                Frame(wm, fall, wb);
                clock += Dt;
                // the pilot drifts toward the planet as it settles
                Vector3 p = ship.transform.position;
                float k = Mathf.Clamp01((clock - 3f) / 3f);
                ship.transform.position = Vector3.Lerp(p, new Vector3(fall.transform.position.x, ShipReach.StartY + 1.2f * k, 0f), Dt * 1.5f);
                if (frame++ % 5 == 0) Capture(cam, d.w / 4, d.h / 4, Path.Combine(dir, "frames", "f" + shot++.ToString("000") + ".png"));
            }
            Capture(cam, d.w, d.h, Path.Combine(dir, "moment-" + (System.Array.IndexOf(Moments, m) + 1).ToString("00") + "-" + m.name + ".png"));
            Log(fall, m.name);
        }

        // the pilot flies into the zone
        ship.transform.position = fall.transform.position + new Vector3(.2f, -fall.ZoneRadius * .8f, 0f);
        if (!fall.Commit(ship.transform)) throw new System.Exception("commit refused");
        float t = 0f;
        int n = Moments.Length, entryShot = 0;
        bool advanced = false;
        foreach (var m in Descent)
        {
            while (t < m.at - 1e-4f)
            {
                int before = WorldManager.CurrentIndex;
                if (fall != null) Frame(wm, fall, wb); else Frame(wm, null, wb);
                if (WorldManager.CurrentIndex != before)
                {
                    advanced = true;
                    Debug.Log("[PLANETFALL-PREVIEW] world switched at t " + t.ToString("F2") + " (cover " +
                              PlanetfallTimeline.Cover(t + Dt).ToString("F3") + ")");
                }
                t += Dt;
                if (fall == null || fall.State == Planetfall.Stage.Done) fall = null;
                if (fall != null && frame % 2 == 0 && ((t >= 2f && t < 2.5f) || (t >= 4.2f && t < 4.5f)))
                    Capture(cam, d.w, d.h, Path.Combine(dir, "entry", "e" + entryShot++.ToString("00") + ".png"));
                if (frame++ % 5 == 0) Capture(cam, d.w / 4, d.h / 4, Path.Combine(dir, "frames", "f" + shot++.ToString("000") + ".png"));
            }
            n++;
            Capture(cam, d.w, d.h, Path.Combine(dir, "moment-" + n.ToString("00") + "-" + m.name + ".png"));
            Log(fall, m.name);
        }
        Debug.Log("[PLANETFALL-PREVIEW] " + d.id + " world now " + WorldManager.Current.displayName + " advanced " + advanced +
                  " ship at " + ship.transform.position + " -> " + dir);
        if (wm != null) Object.DestroyImmediate(wm.gameObject);
    }

    static void Frame(WorldManager wm, Planetfall fall, WorldBackdrop wb)
    {
        if (WorldManager.Flying) wm.Tick(Dt);
        if (fall != null && fall.State != Planetfall.Stage.Done) fall.Step(Dt);
        wb.Step(Dt);
    }

    static void Log(Planetfall fall, string name)
    {
        if (fall == null) { Debug.Log("[PLANETFALL-PREVIEW] " + name + ": done"); return; }
        var r = fall.ReticleRenderer;
        Debug.Log(string.Format("[PLANETFALL-PREVIEW] {0}: {1} {2:F2}s radius {3:F2} cover {4:F2} shake {5:F3} world {6} reticle {7} {8} {9} order {10} planet {11}",
                                name, fall.State, fall.Seconds, fall.Radius, PlanetfallTimeline.Cover(fall.Seconds), fall.ShakeNow,
                                WorldManager.Current.displayName, r.enabled, r.color, r.bounds, r.sortingOrder, fall.PlanetRenderer.bounds));
        var sh = fall.ShroudRenderer;
        if (sh.enabled)
            Debug.Log(string.Format("[PLANETFALL-PREVIEW] {0}: shroud {1} scale {2:F3} (ship span {3:F3}, mid {4}) at {5} glow {6}",
                                    name, sh.sprite.name, fall.ShroudScale, fall.ShipSpan, fall.ShipMid, sh.transform.position,
                                    fall.ShroudGlowRenderer.color));
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
