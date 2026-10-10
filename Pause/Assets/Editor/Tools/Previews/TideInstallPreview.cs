using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Evidence renders for the Tide install (add-world phases 5, 12): gameS1 painted as Tide at a phone's size
// (1170x2532), the camera the game uses, 1x.
//
//   rails-before.png / rails-after.png    Tide's backdrop between the stand-in Ember rails (and Ember's mine) and
//                                         between Tide's own, with a mine on each rail and the ship
//   mines-<variant>.png                   the Limpet Mine's four states (dormant, waking, charging, burst) stacked on
//                                         both rails
//   roster-v<N>.png                       Tide's 11 enemies (rocks, fighters 1-4, big, chaser, alien) at 1x over
//                                         backdrop variant N at 10 s
//
//   TIDE_INSTALL_OUT=<dir> [TIDE_INSTALL_ONLY=rails,mines,roster] Unity -batchmode -quit -projectPath Pause
//       -executeMethod TideInstallPreview.Run
public static class TideInstallPreview
{
    const float Dt = 1f / 60f;
    const int W = 1170, H = 2532;
    const int Ember = 3, Tide = 4;

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("TIDE_INSTALL_OUT");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/TideInstallPreview";
        string only = System.Environment.GetEnvironmentVariable("TIDE_INSTALL_ONLY") ?? "";
        Directory.CreateDirectory(dir);
        int failures = 0;
        using (new TestHarness.Sandbox())
        {
            try
            {
                if (only == "" || only.Contains("rails")) Rails(dir);
                if (only == "" || only.Contains("mines")) Mines(dir);
                if (only == "" || only.Contains("roster")) Roster(dir);
            }
            catch (System.Exception e) { Debug.LogException(e); failures++; }
            finally
            {
                BackdropVariants.For("Tide").Reset();
                BossRails.Reset();
                PlayField.Reset();
                ScreenInfo.ClearOverride();
                buttonClicks.playerDied = false;
            }
        }
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    // gameS1 painted with `railWorld`'s rails over Tide's backdrop (variant `variant`) after `seconds` of its clock.
    static Camera Scene(int railWorld, int variant, float seconds, out WorldBackdrop wb)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity");
        BossRails.Reset();
        var cam = Camera.main;
        cam.aspect = W / (float)H;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, W, H);
        var d = FitDevice.Find("iphone-13");
        ScreenInfo.ClearOverride();
        ScreenInfo.Override(d.w, d.h, d.Safe, d.Cutouts, d.ReportedDpi, d.ios);
        PlayField.Reset();
        buttonClicks.playerDied = false;
        startMenu.youAreInTutorial = false;
        score.pauseCounter = 0;
        Time.timeScale = 1f;
        moveBackGround.speed = .25f;
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, Tide);
        Random.InitState(7);
        var paused = SceneUtil.FindAny("paused");
        if (paused != null) paused.SetActive(false);
        WorldPainter.Apply(WorldManager.Worlds[railWorld]);
        foreach (var name in new[] { "leftPipe", "rightPipe" })
        {
            var wall = GameObject.Find(name);
            var s = wall.transform.localScale;
            s.y = cam.orthographicSize * 2f * 1.085f / wall.GetComponent<MeshFilter>().sharedMesh.bounds.size.y;   // RailFit
            wall.transform.localScale = s;
            RailFit.RefreshTextureTiling(wall);
        }
        BossRails.Measure();
        BackdropVariants.For("Tide").Reset();
        BackdropVariants.For("Tide").Force = variant;
        wb = new GameObject("~TiBackdrop").AddComponent<WorldBackdrop>();
        wb.Show("Tide", false);
        for (int i = 0; i < (int)(seconds / Dt); i++) wb.Step(Dt);
        return cam;
    }

    static void Rails(string dir)
    {
        foreach (bool after in new[] { false, true })
        {
            var cam = Scene(after ? Tide : Ember, 2, 10f, out var wb);
            float top = CameraFit.ViewTop, h = cam.orthographicSize;
            var root = new GameObject("~TiStage");
            int mineWorld = after ? Tide : Ember;
            var mineDef = EnemyRoster.One(mineWorld, EnemyRole.Mine);
            Mine(root, mineDef, enmiesOnBoard.WorldRailX(true), top - h * .7f, RailMineArt.Dormant);
            Mine(root, mineDef, enmiesOnBoard.WorldRailX(false), top - h * 1.2f, RailMineArt.Dormant);
            Mine(root, mineDef, enmiesOnBoard.WorldRailX(false), top - h * .45f, RailMineArt.Charging);
            var ship = new GameObject("~TiSpawner").AddComponent<spawnShips>().Spawn(ShipId.Starter);
            ship.transform.position = new Vector3(0f, ShipReach.StartY, 0f);
            var sr = ship.GetComponent<SpriteRenderer>();
            if (sr != null) sr.sortingOrder = 40;
            Debug.Log("[TIP] rails " + (after ? "after" : "before") + ": inner edge +/-" + BossRails.InnerEdge.ToString("F3") + ", mine x " + enmiesOnBoard.WorldRailX(false).ToString("F3"));
            Capture(cam, Path.Combine(dir, after ? "rails-after.png" : "rails-before.png"));
            Object.DestroyImmediate(root);
        }
    }

    static void Mines(string dir)
    {
        for (int variant = 1; variant <= 4; variant += 3)   // v1 (day) and v4 (night)
        {
            var cam = Scene(Tide, variant, 10f, out var wb);
            float top = CameraFit.ViewTop, h = cam.orthographicSize;
            var root = new GameObject("~TiStage");
            var def = EnemyRoster.One(Tide, EnemyRole.Mine);
            for (int c = 0; c < RailMineArt.Columns; c++)
            {
                float y = top - h * (.45f + .38f * c);
                Mine(root, def, enmiesOnBoard.WorldRailX(true), y, c);
                Mine(root, def, enmiesOnBoard.WorldRailX(false), y, c);
            }
            Capture(cam, Path.Combine(dir, "mines-v" + variant + ".png"));
            Object.DestroyImmediate(root);
        }
    }

    static void Roster(string dir)
    {
        for (int variant = 1; variant <= 4; variant++)
        {
            var cam = Scene(Tide, variant, 10f, out var wb);
            float top = CameraFit.ViewTop, h = cam.orthographicSize;
            var root = new GameObject("~TiStage");
            var rocks = EnemyRoster.For(Tide, EnemyRole.Rock);
            float[] xs = { -1.6f, -.55f, .55f, 1.6f };
            float y = top - h * .32f, step = h * .36f;
            for (int i = 0; i < rocks.Count; i++) Place(root, rocks[i], xs[i], y);
            y -= step;
            for (int t = 1; t <= 4; t++) Place(root, EnemyRoster.Fighter(Tide, t), xs[t - 1], y);
            y -= step;
            Place(root, EnemyRoster.One(Tide, EnemyRole.Chaser), -1.2f, y);
            Place(root, EnemyRoster.One(Tide, EnemyRole.Alien), 0f, y);
            Place(root, EnemyRoster.One(Tide, EnemyRole.Big), 1.3f, y);
            // the shooters' own shots, big to small, beside them (the generic bolts / shards)
            var ship = new GameObject("~TiSpawner").AddComponent<spawnShips>().Spawn(ShipId.Starter);
            ship.transform.position = new Vector3(0f, ShipReach.StartY, 0f);
            Capture(cam, Path.Combine(dir, "roster-v" + variant + ".png"));
            Object.DestroyImmediate(root);
        }
    }

    static void Place(GameObject root, EnemyDef def, float x, float y)
    {
        var go = EnemyFactory.Create(def, new Vector3(x, y, 0f), Quaternion.identity);
        go.transform.SetParent(root.transform, true);
    }

    static void Mine(GameObject root, EnemyDef def, float x, float y, int column)
    {
        var rail = new GameObject("RailMineLane");
        rail.transform.SetParent(root.transform, false);
        rail.transform.position = new Vector3(x, y, 0f);
        var go = EnemyFactory.Create(def, new Vector3(x, y, 0f), Quaternion.identity);
        go.transform.SetParent(root.transform, true);
        go.GetComponent<SpriteRenderer>().flipX = x > 0f;
        var mount = go.AddComponent<RailMineMount>();
        mount.MountTo(rail.transform);
        mount.SendMessage("LateUpdate");
        go.GetComponent<SpriteRenderer>().sprite = RailMineArt.Frame(def.world, column);
    }

    static void Capture(Camera cam, string file)
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
        RenderTexture.active = old;
        cam.targetTexture = previous;
        Object.DestroyImmediate(png);
        Object.DestroyImmediate(rt);
        Debug.Log("[TIP] " + file);
    }
}
