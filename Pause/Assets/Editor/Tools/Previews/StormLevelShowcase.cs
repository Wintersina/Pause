using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The Storm level as the phone camera sees it (gameS1, 1080x2400) with the r3 ground (StormTuning.GroundZoom) and the living
// props (StormProps.cs), for review:
//
//   storm-vN.png              each ground set (v1..v4) mid-level, past the cloud ceiling: the ground, the vapour strip, the
//                             landmarks and whatever props the director dealt, with the player ship
//   props-vN-<a>-<b>-<c>.png  two ground props placed by their own rules and one ALOFT prop above them, per ground set
//   props_<tag>_frames/         + 36 frames at 15 fps (lights, the vortex turning, the cell's flash, the moored aloft prop's bob and sway, the crashed gunship's boiling cloud)
//   storm_v2_frames/          36 frames at 15 fps of v2 as it plays (CI turns each *_frames folder into <name>.gif)
//
//   SHOWCASE_DIR=<dir> Unity -batchmode -quit -projectPath Pause -executeMethod StormLevelShowcase.Run
public static class StormLevelShowcase
{
    const float Dt = 1f / 60f;
    const int W = 1080, H = 2400;
    const int Storm = 5;            // WorldManager.Worlds index
    const float WarmSeconds = 20f;  // the cloud ceiling has cleared (~15 s)

    static string dir;
    static Camera cam;
    static WorldBackdrop wb;
    static StormDirector director;
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
                failures += Section("props", Props);
                failures += Section("gif", Gif);
            }
            finally
            {
                BackdropVariants.For("Storm").Reset();
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
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, Storm);
        Random.InitState(7);
        var paused = SceneUtil.FindAny("paused");
        if (paused != null) paused.SetActive(false);
        WorldPainter.Apply(WorldManager.Worlds[Storm]);
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

        BackdropVariants.For("Storm").Reset();
        BackdropVariants.For("Storm").Force = variant;
        wb = WorldBackdrop.Create("Storm");
        wb.Show("Space", false);
        wb.Show("Storm", false);
        director = wb.Current.Director as StormDirector;
        if (director == null) throw new System.Exception("Storm backdrop has no StormDirector");
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
        for (int v = 1; v <= StormTuning.Variants; v++)
        {
            Scene(v);
            Steps(.5f);
            Capture("storm-v" + v + ".png");
            Log("v" + v);
        }
    }

    static void Props()
    {
        // (variant, two ground kinds, one aloft kind)
        var shots = new[]
        {
            // (rods come beside a refinery, a tug / skimmer beside a buoy chain: their companions are placed with them)
            new[] { 1, StormPropsTuning.Rods, StormPropsTuning.Mast, StormPropsTuning.Aerostat },
            new[] { 2, StormPropsTuning.Cell, StormPropsTuning.Gunship, StormPropsTuning.Rays },
            new[] { 3, StormPropsTuning.Tug, StormPropsTuning.Skimmer, StormPropsTuning.Convoy },
            new[] { 4, StormPropsTuning.Gunship, StormPropsTuning.Vortex, StormPropsTuning.Jelly },
            new[] { 1, StormPropsTuning.Wreck, StormPropsTuning.Buoys, StormPropsTuning.Jelly },
        };
        foreach (var shot in shots)
        {
            Scene(shot[0]);
            director.ClearProps();
            director.HoldProps(1e6f, 1e6f);
            float h = cam.orthographicSize;
            double tr = director.Planner.Travel;
            for (int j = 1; j <= 2; j++)
            {
                bool ok = false;
                double lo = j == 1 ? tr + h * .05f : tr - h * .55f, hi = j == 1 ? tr + h * .6f : tr - h * .05f;
                for (int a = 0; a < 6 && !ok; a++) ok = director.SpawnProp(shot[j], lo, hi, -1, 800);
                if (!ok) for (int a = 0; a < 6 && !ok; a++) ok = director.SpawnProp(shot[j], tr - h * .85f, tr + h * .85f, -1, 800);
                Debug.Log("[SHOWCASE] props v" + shot[0] + " " + StormPropsTuning.Ground[shot[j]] + " placed " + ok);
            }
            bool up = director.SpawnAloft(shot[3], float.NaN, h * .25f);   // (a column clear of every pinned piece, or none)
            Debug.Log("[SHOWCASE] aloft v" + shot[0] + " " + StormPropsTuning.Aloft[shot[3]] + " placed " + up);
            string tag = "v" + shot[0] + "-" + StormPropsTuning.Ground[shot[1]] + "-" + StormPropsTuning.Ground[shot[2]] + "-" + StormPropsTuning.Aloft[shot[3]];
            Steps(.3f);
            Capture("props-" + tag + ".png");
            Log("props " + tag);
            string frames = Path.Combine(dir, "props_" + tag.Replace('-', '_') + "_frames");
            if (Directory.Exists(frames)) Directory.Delete(frames, true);
            Directory.CreateDirectory(frames);
            for (int f = 0; f < 36; f++)
            {
                if (f > 0) for (int s = 0; s < 4; s++) Step(Dt);
                Capture(Path.Combine(Path.GetFileName(frames), f.ToString("000") + ".png"));
            }
        }
    }

    static void Gif()
    {
        Scene(2);
        director.SpawnAloft(-1, float.NaN, cam.orthographicSize * .5f);
        string frames = Path.Combine(dir, "storm_v2_frames");
        if (Directory.Exists(frames)) Directory.Delete(frames, true);
        Directory.CreateDirectory(frames);
        for (int f = 0; f < 36; f++)
        {
            if (f > 0) for (int s = 0; s < 4; s++) Step(Dt);
            Capture(Path.Combine("storm_v2_frames", f.ToString("000") + ".png"));
        }
    }

    static void Log(string what)
    {
        float ppu = H / (2f * cam.orthographicSize);
        foreach (var pool in new[] { director.Props, director.Cells, director.AloftPool, director.Ground })
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
