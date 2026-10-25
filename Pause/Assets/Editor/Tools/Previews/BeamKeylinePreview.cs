using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Before / after frames of the warning beams' hollow-squares look (BeamKeyline), per world, over that world's backdrop, drawn by the real code:
// a rail mine's aim line, a boss laser's aim line and a pattern marker lane. Each is grabbed twice from the same frame (plain, then squares). The contact sheet is cut from the PNGs and the [BKP] log lines.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod BeamKeylinePreview.Run     (BEAM_KEYLINE_DIR, else Builds/BeamKeyline)
public static class BeamKeylinePreview
{
    const int Width = 1080, Height = 1920;   // a phone's own pixels: 1x of the sheet is the device
    const float Dt = 1f / 60f;
    static readonly string[] Worlds = { "Space", "Frost", "Verdant", "Ember", "Tide", "Storm", "Desert" };
    static Camera cam;
    static RenderTexture rt;
    static Texture2D png;
    static Transform pilot;

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("BEAM_KEYLINE_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/BeamKeyline";
        Directory.CreateDirectory(dir);
        using (new TestHarness.Sandbox())
        {
            try
            {
                for (int w = 0; w < Worlds.Length; w++)
                {
                    try { Mine(dir, w); } catch (System.Exception e) { Debug.Log("[BKP] mine " + Worlds[w] + " failed: " + e); }
                    try { BossLine(dir, w); } catch (System.Exception e) { Debug.Log("[BKP] boss " + Worlds[w] + " failed: " + e); }
                }
            }
            finally
            {
                BeamKeyline.Suppress = false;
                EnemyThreat.ForceShooting = false;
                EnemyThreat.Reset();
                BossEncounter.ResetRun();
                EliteSystem.Clear();
                EliteSystem.PlayerOverride = null;
                SpawnSpace.ClockOverride = null;
            }
        }
        EditorApplication.Exit(0);
    }

    static void Scene(int world)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        BossRails.Reset();
        EliteSystem.Clear();
        EnemyThreat.Reset();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.aspect = Width / (float)Height;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, Width, Height);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        startMenu.youAreInTutorial = false;
        moveBackGround.speed = 0f;
        Time.timeScale = 1f;
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        var bgGo = new GameObject("~Backdrop");
        var backdrop = bgGo.AddComponent<WorldBackdrop>();
        backdrop.Show(Worlds[world], false);
        for (int i = 0; i < 600; i++) backdrop.Step(1f / 60f);
        pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -4f, 0f);
        EliteSystem.PlayerOverride = pilot;
        HazardRuntime.Ensure();
        if (rt == null) rt = new RenderTexture(Width, Height, 24);
        if (png == null) png = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        cam.targetTexture = rt;
    }

    static void Save(string path)
    {
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        png.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        png.Apply();
        RenderTexture.active = old;
        File.WriteAllBytes(path, png.EncodeToPNG());
    }

    // Grabs `stem`-before.png (keylines suppressed) and `stem`-after.png from the same frame; `refresh` re-places the pieces after each toggle.
    static void Pair(string stem, string tag, int world, Vector2 centre, System.Action refresh)
    {
        BeamKeyline.Suppress = true; refresh(); Save(stem + "-before.png");
        BeamKeyline.Suppress = false;
        BeamKeyline.ClockOverride = .3f;   // a fixed march phase
        refresh(); Save(stem + "-after.png");
        BeamKeyline.ClockOverride = -1f;
        var s = cam.WorldToScreenPoint(centre);
        Debug.Log("[BKP] " + tag + " " + Worlds[world] + " " + Path.GetFileName(stem) + " " + Mathf.RoundToInt(s.x) + " " + Mathf.RoundToInt(s.y));
    }

    static void Mine(string dir, int world)
    {
        Scene(world);
        EnemyThreat.ForceShooting = true;
        float clock = 100f;
        SpawnSpace.ClockOverride = clock;
        var def = EnemyRoster.One(world, EnemyRole.Mine);
        if (def == null) { Debug.Log("[BKP] mine " + Worlds[world] + ": no mine in this world"); return; }
        float x = enmiesOnBoard.WorldRailX(false);
        var rail = new GameObject("RailMineLane");
        rail.transform.position = new Vector3(x, .5f, 0f);
        rail.AddComponent<RailLaneScroller>();
        var go = EnemyFactory.Create(def, new Vector3(x, .5f, 0f), Quaternion.identity);
        var brain = go.GetComponent<EnemyBrain>();
        var mount = go.AddComponent<RailMineMount>();
        mount.MountTo(rail.transform);
        mount.brain = brain;
        brain.TargetOverride = pilot;
        pilot.position = new Vector3(-.5f, -1.5f, 0f);
        var fb = go.GetComponent<EnemyFlipbook>();
        System.Action late = () =>
        {
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (mb != null && mb.GetType().Name == "RailMineLaser") mb.SendMessage("LateUpdate");
        };
        bool done = false;
        for (int i = 0; i < 60 * 10 && !done; i++)
        {
            clock += Dt;
            SpawnSpace.ClockOverride = clock;
            brain.Step(Dt);
            mount.SendMessage("LateUpdate");
            if (fb != null) fb.Advance(Dt);
            EliteSystem.Step(Dt);
            late();
            if (brain.Laser != null && brain.Laser.SightShown && brain.Laser.State == RailMineLaser.Phase.Aim)
            {
                var l = brain.Laser;
                Pair(Path.Combine(dir, "mine-" + Worlds[world].ToLower()), "mine", world, (l.From + l.To) * .5f, late);
                done = true;
            }
        }
        if (!done) Debug.Log("[BKP] mine " + Worlds[world] + ": aim line never showed");
        Object.DestroyImmediate(go);
        Object.DestroyImmediate(rail);
        EnemyThreat.ForceShooting = false;
    }

    // A boss laser's aim line (angled, across the lane) and a pattern-marker lane (a dotted pair).
    static void BossLine(string dir, int world)
    {
        Scene(world);
        var boss = BossCatalog.ForWorld(world);
        var root = new GameObject("~Beams").transform;
        var b = BossBeam.Create(root);
        var atk = System.Array.Find(boss.DefaultAttacks, x => x.kind == BossAttackKind.Beam);
        float tell = atk != null ? Mathf.Max(1.5f, atk.tellSeconds) : 1.5f, hold = atk != null ? atk.hold : 1f, w = atk != null ? atk.beamWidth : .6f;
        b.Begin(boss, null, -1, new Vector3(-1.6f, 2.2f, 0f), -18f, 0f, tell, hold, w);
        for (int i = 0; i < 40; i++) b.Step(Dt);   // (inside the tell: the line has grown across the lane)
        for (int i = 0; i < 30 && !b.SightShown; i++) b.Step(Dt);
        Pair(Path.Combine(dir, "boss-" + Worlds[world].ToLower()), "boss", world, new Vector2(1.3f, 1.27f), () => b.Step(1e-4f));

        var mk = new BossPatternMarkers();
        mk.Build(root, boss);
        mk.Intensity = 0f;
        mk.Add(new Vector2(-1.4f, 5f), new Vector2(-1.4f, -5f), .9f, 1.6f);
        for (int i = 0; i < 70; i++) mk.Step(Dt);
        Pair(Path.Combine(dir, "marker-" + Worlds[world].ToLower()), "marker", world, new Vector2(-.5f, 0f), () => mk.Step(1e-4f));
    }
}
