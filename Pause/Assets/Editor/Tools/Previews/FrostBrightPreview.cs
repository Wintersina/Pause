using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Frost brightness review: each ground variant (v1..v4) at mid-run with the player ship, a Frost fighter shooting and the Frost rail
// mine firing, rendered at each FrostTuning.Brightness in $FROST_BRIGHT_LEVELS (comma list), frames <dir>/b<level>-v<N>.png, and the
// mean luminance logged ([FROST-BRIGHT]).
//
//   FROST_BRIGHT_DIR=<dir> FROST_BRIGHT_LEVELS=1.7,1.95 Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod FrostBrightPreview.Run
public static class FrostBrightPreview
{
    const float Dt = 1f / 60f;
    const int W = 720, H = 1600;

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("FROST_BRIGHT_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/FrostBright";
        Directory.CreateDirectory(dir);
        string lv = System.Environment.GetEnvironmentVariable("FROST_BRIGHT_LEVELS");
        if (string.IsNullOrEmpty(lv)) lv = FrostTuning.Brightness.ToString(CultureInfo.InvariantCulture);
        float saved = FrostTuning.Brightness;
        using (new TestHarness.Sandbox())
        {
            try
            {
                foreach (var s in lv.Split(','))
                {
                    float b = float.Parse(s, CultureInfo.InvariantCulture);
                    FrostTuning.Brightness = b;
                    for (int v = 1; v <= 4; v++) Shoot(dir, b, v);
                }
            }
            finally
            {
                FrostTuning.Brightness = saved;
                FrostBackdropSelection.Reset();
                EnemyThreat.ForceShooting = false;
                EnemyThreat.Reset();
                EliteSystem.PlayerOverride = null;
                EliteSystem.Clear();
                BossEncounter.ResetRun();
            }
        }
        EditorApplication.Exit(0);
    }

    static void Shoot(string dir, float b, int variant)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity");
        BossEncounter.ResetRun(); BossRails.Reset(); PortalPressure.Reset(); RunLoop.Reset();
        EnemyThreat.Reset(); RailMineLasers.Clear(); EliteSystem.Clear();
        var cam = Camera.main;
        cam.aspect = W / (float)H;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, W, H);
        PlayField.Reset();
        buttonClicks.playerDied = false;
        startMenu.youAreInTutorial = false;
        score.pauseCounter = 0;
        Time.timeScale = 1f;
        moveBackGround.speed = .25f;
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, 1);
        Random.InitState(7);
        var paused = SceneUtil.FindAny("paused");
        if (paused != null) paused.SetActive(false);
        WorldPainter.Apply(WorldManager.Worlds[1]);
        foreach (var name in new[] { "leftPipe", "rightPipe" })
        {
            var wall = GameObject.Find(name);
            if (wall == null) continue;
            var sc = wall.transform.localScale;
            sc.y = cam.orthographicSize * 2f * 1.085f / wall.GetComponent<MeshFilter>().sharedMesh.bounds.size.y;
            wall.transform.localScale = sc;
            RailFit.RefreshTextureTiling(wall);
        }
        BossRails.Measure();

        var ship = new GameObject("~PfSpawner").AddComponent<spawnShips>().Spawn(ShipId.Starter);
        ship.transform.position = new Vector3(-.6f, ShipReach.StartY, 0f);
        EliteSystem.PlayerOverride = ship.transform;
        FrostBackdropSelection.Reset();
        FrostBackdropSelection.Force = variant;
        var wb = WorldBackdrop.Create("Frost");
        wb.Show("Space", false);
        wb.Show("Frost", false);
        var d = (FrostDirector)wb.Current.Director;
        while (d.Clock < 38f) wb.Step(Dt);
        HazardRuntime.Ensure();

        EnemyThreat.ForceShooting = true;
        float clock = 100f;
        SpawnSpace.ClockOverride = clock;
        RailMineLaser.AngleOverride = 14f;
        // the rail mine
        var mdef = EnemyRoster.One(1, EnemyRole.Mine);
        float x = enmiesOnBoard.WorldRailX(false);
        var rail = new GameObject("RailMineLane");
        rail.transform.position = new Vector3(x, .5f, 0f);
        rail.AddComponent<RailLaneScroller>();
        var mgo = EnemyFactory.Create(mdef, new Vector3(x, .5f, 0f), Quaternion.identity);
        var mbrain = mgo.GetComponent<EnemyBrain>();
        var mount = mgo.AddComponent<RailMineMount>();
        mount.MountTo(rail.transform);
        mount.brain = mbrain;
        mbrain.TargetOverride = ship.transform;
        // a fighter shooting from the upper lane
        var fdef = EnemyRoster.One(1, EnemyRole.Fighter);
        var fgo = EnemyFactory.Create(fdef, new Vector3(.9f, 2.4f, 0f), Quaternion.identity);
        var fbrain = fgo.GetComponent<EnemyBrain>();
        fbrain.TargetOverride = ship.transform;
        var mfb = mgo.GetComponent<EnemyFlipbook>();
        var ffb = fgo.GetComponent<EnemyFlipbook>();

        bool fired = false;
        float sinceFire = 0f;
        for (int i = 0; i < 60 * 12; i++)
        {
            clock += Dt;
            SpawnSpace.ClockOverride = clock;
            wb.Step(Dt);
            mbrain.Step(Dt);
            fbrain.Step(Dt);
            mount.SendMessage("LateUpdate");
            if (mfb != null) mfb.Advance(Dt);
            if (ffb != null) ffb.Advance(Dt);
            EliteSystem.Step(Dt);
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (mb != null && mb.GetType().Name == "RailMineLaser") mb.SendMessage("LateUpdate");
            if (mbrain.State == EnemyBrain.Phase.Release) fired = true;
            if (fired) { sinceFire += Dt; if (sinceFire >= .2f) break; }
        }
        string file = Path.Combine(dir, "b" + b.ToString("0.00", CultureInfo.InvariantCulture) + "-v" + variant + ".png");
        float lum = Capture(cam, file);
        Debug.Log("[FROST-BRIGHT] b=" + b.ToString("F2") + " v" + variant + " meanLum=" + lum.ToString("F4") + " mineFired=" + fired);
        Object.DestroyImmediate(mgo); Object.DestroyImmediate(fgo); Object.DestroyImmediate(rail);
        Object.DestroyImmediate(wb.gameObject);
    }

    static float Capture(Camera cam, string file)
    {
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
        double sum = 0;
        var px = png.GetPixels32();
        for (int i = 0; i < px.Length; i++) sum += (.2126 * px[i].r + .7152 * px[i].g + .0722 * px[i].b) / 255.0;
        RenderTexture.active = old;
        cam.targetTexture = previous;
        Object.DestroyImmediate(png);
        Object.DestroyImmediate(rt);
        return (float)(sum / px.Length);
    }
}
