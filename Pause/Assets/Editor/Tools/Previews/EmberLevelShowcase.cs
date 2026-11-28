using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The Ember level as the phone camera sees it (gameS1, 1080x2400) with the r1 ground (EmberTuning.GroundZoom .49) and the living
// props and mountains (EmberProps.cs), for review:
//
//   ember-vN.png         each ground set (v1..v4) mid-level, past the ash ceiling: the stitched ground, the props and mountains the
//                         director dealt, the eruptions / sites about, with the player ship
//   ember_v1_frames/     36 frames at 15 fps of v1 as it plays (CI turns each *_frames folder into <name>.gif)
//
//   SHOWCASE_DIR=<dir> Unity -batchmode -quit -projectPath Pause -executeMethod EmberLevelShowcase.Run
public static class EmberLevelShowcase
{
    const float Dt = 1f / 60f;
    const int W = 1080, H = 2400;
    const int Ember = 3;            // WorldManager.Worlds index
    const float WarmSeconds = 20f;  // the cloud ceiling has cleared (~15 s)

    static string dir;
    static Camera cam;
    static WorldBackdrop wb;
    static EmberDirector director;
    static GameObject ship;

    public static void Run()
    {
        dir = System.Environment.GetEnvironmentVariable("SHOWCASE_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/Showcase";
        Directory.CreateDirectory(dir);
        int failures = 0;
        using (new TestHarness.Sandbox())
        {
            try
            {
                failures += Section("variants", Variants);
                failures += Section("kinds", Kinds);
                failures += Section("gif", Gif);
            }
            finally
            {
                BackdropVariants.For("Ember").Reset();
                EliteSystem.PlayerOverride = null;
                EliteSystem.Clear();
                BossEncounter.ResetRun();
                PortalPressure.Reset();
                BossRails.Reset();
                PlayField.Reset();
                ScreenInfo.ClearOverride();
                buttonClicks.playerDied = false;
            }
        }
        Debug.Log("[SHOWCASE] done, " + failures + " section(s) failed -> " + dir);
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    static int Section(string name, System.Action body)
    {
        try { body(); Debug.Log("[SHOWCASE] " + name + " ok"); return 0; }
        catch (System.Exception e) { Debug.LogError("[SHOWCASE] " + name + " failed"); Debug.LogException(e); return 1; }
    }

    static void Scene(int variant)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity");
        EliteSystem.Clear();
        BossEncounter.ResetRun();
        BossRails.Reset();
        PortalPressure.Reset();
        RunLoop.Reset();
        cam = Camera.main;
        cam.aspect = W / (float)H;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, W, H);
        PlayField.Reset();
        buttonClicks.playerDied = false;
        startMenu.youAreInTutorial = false;
        score.pauseCounter = 0;
        Time.timeScale = 1f;
        moveBackGround.speed = .25f;
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, Ember);
        Random.InitState(7);
        var paused = SceneUtil.FindAny("paused");
        if (paused != null) paused.SetActive(false);
        WorldPainter.Apply(WorldManager.Worlds[Ember]);
        foreach (var name in new[] { "leftPipe", "rightPipe" })
        {
            var wall = GameObject.Find(name);
            if (wall == null) continue;
            var s = wall.transform.localScale;
            s.y = cam.orthographicSize * 2f * 1.085f / wall.GetComponent<MeshFilter>().sharedMesh.bounds.size.y;
            wall.transform.localScale = s;
            RailFit.RefreshTextureTiling(wall);
        }
        BossRails.Measure();
        ship = new GameObject("~PfSpawner").AddComponent<spawnShips>().Spawn(ShipId.Starter);
        ship.transform.position = new Vector3(-.6f, ShipReach.StartY, 0f);
        EliteSystem.PlayerOverride = ship.transform;

        BackdropVariants.For("Ember").Reset();
        BackdropVariants.For("Ember").Force = variant;
        wb = WorldBackdrop.Create("Ember");
        wb.Show("Space", false);
        wb.Show("Ember", false);
        director = wb.Current.Director as EmberDirector;
        if (director == null) throw new System.Exception("Ember backdrop has no EmberDirector");
        while (director.Clock < WarmSeconds) wb.Step(Dt);
    }

    static void Step(float dt)
    {
        wb.Step(dt);
        if (ship != null) foreach (var f in ship.GetComponentsInChildren<ShipFlameFlipbook>(true)) f.Step(dt);
    }

    static void Steps(float seconds) { for (float t = 0f; t < seconds - 1e-4f; t += Dt) Step(Dt); }

    static void Variants()
    {
        for (int v = 1; v <= BackdropVariants.MaxVariants; v++)
        {
            Scene(v);
            Steps(.5f);
            Capture("ember-v" + v + ".png");
            Log("v" + v);
        }
    }

    // every kind alone, planted on its own ground at the middle of the view (the proof crops: the prop blended into the real ground)
    static void Kinds()
    {
        Scene(1);
        for (int k = 0; k < EmberPropsTuning.Count; k++)
        {
            director.ClearEruptions();
            director.ClearProps();
            director.HoldProps(1e6f);
            director.HoldGround(1e6f);
            if (!director.HasPropArt(k)) continue;
            for (int variant = 0; variant < director.PropVariants(k); variant++)
            {
                director.ClearProps();
                if (!director.SpawnPropAt(k, 1.2f, variant)) director.PlantProp(k, 0f, director.Planner.GroundY(1.2f), variant);
                Steps(.2f);
                Capture("ember-kind-" + EmberPropsTuning.Kinds[k].name + "-" + variant + ".png");
            }
        }
    }

    static void Gif()
    {
        Scene(1);
        string frames = Path.Combine(dir, "ember_v1_frames");
        if (Directory.Exists(frames)) Directory.Delete(frames, true);
        Directory.CreateDirectory(frames);
        for (int f = 0; f < 36; f++)
        {
            if (f > 0) for (int s = 0; s < 4; s++) Step(Dt);
            Capture(Path.Combine("ember_v1_frames", f.ToString("000") + ".png"));
        }
    }

    static void Log(string what)
    {
        float ppu = H / (2f * cam.orthographicSize);
        foreach (var pool in new[] { director.Mountains, director.Props, director.Fires, director.Sites })
            foreach (var p in pool.items)
            {
                if (!p.active || p.sr.sprite == null) continue;
                Vector3 rel = p.root.position - cam.transform.position;
                Debug.Log("[SHOWCASE] " + what + ": " + pool.name + " " + p.sr.sprite.name + " at px " + Mathf.RoundToInt(W * .5f + rel.x * ppu) + "," +
                          Mathf.RoundToInt(H * .5f - rel.y * ppu) + " size " + Mathf.RoundToInt(p.size * ppu) + " px");
            }
    }

    static void Capture(string name)
    {
        string file = Path.Combine(dir, name);
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previous = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var png = new Texture2D(W, H, TextureFormat.RGB24, false);
        png.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        png.Apply();
        File.WriteAllBytes(file, png.EncodeToPNG());
        RenderTexture.active = old;
        cam.targetTexture = previous;
        Object.DestroyImmediate(png);
        Object.DestroyImmediate(rt);
        Debug.Log("[SHOWCASE] wrote " + file);
    }
}
