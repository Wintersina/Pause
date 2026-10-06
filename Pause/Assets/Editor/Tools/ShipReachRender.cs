using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Evidence renders for "Ship reach and boss height" (docs/enemy-behaviours.md),
// drawn by the game camera in gameS1 at 1080x1920, 1080x2520 and iPhone 15,
// before (the old constants: ShipReach / BossConfig .FitToView = false) and
// after. Nothing here is gameplay.
//
//   SHIP_REACH_OUT=<dir> Unity -batchmode -quit -projectPath Pause
//       -executeMethod ShipReachRender.Run
//
// Overlays (drawn over the world, not the real UI, which the camera does
// not render): the ship's reachable band shaded green with a ship at its
// floor and one at its ceiling; the HUD's top band (score read-out and
// quick actions) red; the safe area's insets dark blue; display cutouts
// black. Three shots per screen and state:
//   <shape>-<before|after>-reach.png  the reach, a pilot holding the deepest station
//   <shape>-<before|after>-pilot.png  a tier-4 pilot on station, the ship at its ceiling
//   <shape>-<before|after>-boss.png   the boss at rest with an attack in flight, the ship at the fight's ceiling
//   <shape>-<before|after>-boss-frost.png  the same for the Frost boss (its jaw is the lowest muzzle)
public static class ShipReachRender
{
    const float Dt = 1f / 60f;
    static Texture2D white;
    static Sprite pixel;

    public static void Run()
    {
        string outDir = System.Environment.GetEnvironmentVariable("SHIP_REACH_OUT");
        if (string.IsNullOrEmpty(outDir)) outDir = "Logs/ship-reach";
        Directory.CreateDirectory(outDir);
        using var sandbox = new TestHarness.Sandbox();
        EnemyThreat.ForceShooting = true;
        try
        {
            foreach (var id in new[] { "and-1080x1920", "flip7-1080x2520", "iphone-15" })
            {
                var d = FitDevice.Find(id);
                foreach (bool after in new[] { false, true })
                {
                    ShipReach.FitToView = after;
                    BossConfig.FitToView = after;
                    string tag = outDir + "/" + id + "-" + (after ? "after" : "before");
                    Reach(d, tag + "-reach.png", "verdant_fighter_3", 2);
                    Reach(d, tag + "-pilot.png", "space_fighter_4", 0);
                    Boss(d, tag + "-boss.png", 3, 0);          // Ember fire breath: the widest fan
                    Boss(d, tag + "-boss-frost.png", 1, 0);    // Frost: the lowest muzzle (the jaw), the lowest fight ceiling
                }
            }
        }
        finally
        {
            ShipReach.FitToView = true;
            BossConfig.FitToView = true;
            EnemyThreat.ForceShooting = false;
            EnemyThreat.Reset();
            SpawnSpace.ClockOverride = null;
            BossEncounter.ResetRun();
            BossRails.Reset();
            PlayField.Reset();
            ScreenInfo.ClearOverride();
        }
    }

    // gameS1 painted as `world`, fitted to `d` (camera, rails, backdrop,
    // screen and HUD for PlayField). Returns the scene's camera.
    static Camera Stage(FitDevice d, int world, out HudStyler styler)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity");
        BossEncounter.ResetRun();
        BossRails.Reset();
        var cam = Camera.main;
        cam.aspect = d.Aspect;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, d.w, d.h);
        ScreenInfo.ClearOverride();
        ScreenInfo.Override(d.w, d.h, d.Safe, d.Cutouts);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        var theme = WorldManager.Worlds[world];
        var wb = new GameObject("~ReachBackdrop").AddComponent<WorldBackdrop>();
        WorldPainter.Apply(theme);
        foreach (var name in new[] { "leftPipe", "rightPipe" })
        {
            var wall = GameObject.Find(name);
            var s = wall.transform.localScale;
            s.y = cam.orthographicSize * 2f * 1.085f / wall.GetComponent<MeshFilter>().sharedMesh.bounds.size.y;   // RailFit
            wall.transform.localScale = s;
            RailFit.RefreshTextureTiling(wall);
        }
        BossRails.Measure();
        wb.Show(theme.displayName, false);
        for (int i = 0; i < 30; i++) wb.Step(Dt);
        styler = new GameObject("~ReachHud").AddComponent<HudStyler>();
        styler.SendMessage("Start");
        Canvas.ForceUpdateCanvases();
        PlayField.Reset();
        PlayField.UseHud(styler);
        return cam;
    }

    static GameObject Ship(Vector3 at)
    {
        var go = new GameObject("~ReachShip", typeof(SpriteRenderer));
        var sr = go.GetComponent<SpriteRenderer>();
        sr.sprite = shopingShips.SpriteFor(ShipId.Starter);
        sr.sortingOrder = 40;
        float k = shopingShips.NormalizedHullScale(sr.sprite);
        go.transform.localScale = new Vector3(k, k, 1f);
        go.transform.position = at;
        return go;
    }

    static void Box(Transform root, float x0, float y0, float x1, float y1, Color c, int order = 300)
    {
        if (x1 <= x0 || y1 <= y0) return;
        if (pixel == null)
        {
            white = new Texture2D(1, 1);
            white.SetPixel(0, 0, Color.white);
            white.Apply();
            pixel = Sprite.Create(white, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), 1f);
        }
        var go = new GameObject("~ReachOverlay");
        go.transform.SetParent(root, false);
        go.transform.position = new Vector3((x0 + x1) * .5f, (y0 + y1) * .5f, 0f);
        go.transform.localScale = new Vector3(x1 - x0, y1 - y0, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = pixel;
        sr.color = c;
        sr.sortingOrder = order;
    }

    static void Line(Transform root, float y, Color c) { Box(root, -3.72f, y - .015f, 3.72f, y + .015f, c, 310); }

    // The HUD band, safe-area insets and cutouts over the world.
    static void Frame(Transform root, FitDevice d, Camera cam, bool reach, float floor, float ceiling)
    {
        var f = PlayField.Live;
        float half = cam.orthographicSize * cam.aspect, unit = f.Height / d.h;
        if (reach)
        {
            Box(root, -ShipReach.HalfWidth - .29f, floor - ShipReach.HullBelow, ShipReach.HalfWidth + .29f, ceiling + ShipReach.HullAbove, new Color(.2f, 1f, .35f, .16f), 290);
            Line(root, floor, new Color(.2f, 1f, .35f, .9f));
            Line(root, ceiling, new Color(.2f, 1f, .35f, .9f));
        }
        Box(root, -half, f.bandBottom, half, f.safeTop, new Color(1f, .15f, .2f, .28f));
        Line(root, f.bandBottom, new Color(1f, .2f, .25f, .95f));
        Box(root, -half, f.bottom, half, f.safeBottom, new Color(.1f, .2f, .9f, .45f));
        Box(root, -half, f.safeTop, half, f.top, new Color(.1f, .2f, .9f, .45f));
        foreach (var c in d.Cutouts)
            Box(root, (c.xMin - d.w * .5f) * unit, f.bottom + c.yMin * unit, (c.xMax - d.w * .5f) * unit, f.bottom + c.yMax * unit, Color.black, 320);
    }

    // The reach (ship at floor and ceiling) with `pilotKey` brought onto station.
    static void Reach(FitDevice d, string path, string pilotKey, int world)
    {
        HudStyler styler;
        var cam = Stage(d, world, out styler);
        var root = new GameObject("~ReachStage").transform;
        float floor = ShipReach.Bottom, ceiling = ShipReach.Top;
        var low = Ship(new Vector3(-1.2f, floor, 0f));
        var high = Ship(new Vector3(.2f, ceiling, 0f));
        var def = EnemyRoster.Find(pilotKey);
        var go = EnemyFactory.Create(def, new Vector3(.2f, CameraFit.ViewTop + PilotAirspace.WaitAbove, 0f), Quaternion.identity);
        var brain = go.GetComponent<EnemyBrain>();
        brain.TargetOverride = high.transform;
        var fb = go.GetComponent<EnemyFlipbook>();
        float clock = 900f;
        for (int i = 0; i < 60 * 8 && !(brain.Stage == EnemyBrain.PilotStage.Engaging && brain.EngagedSeconds > 1f); i++)
        {
            clock += Dt;
            SpawnSpace.ClockOverride = clock;
            brain.Step(Dt);
            if (fb != null) fb.Advance(Dt);
            EliteSystem.Step(Dt);
        }
        SpawnSpace.ClockOverride = null;
        Frame(root, d, cam, true, floor, ceiling);
        var f = PlayField.Live;
        Debug.Log(string.Format("[REACHRENDER] {0}: reach {1:F2}..{2:F2} ({3:P0}..{4:P0}), {5} at {6:F2} ({7:P0}), band from {8:F2}",
                                path, floor, ceiling, f.ShareOf(floor), f.ShareOf(ceiling), pilotKey, go.transform.position.y,
                                f.ShareOf(go.transform.position.y), f.bandBottom));
        Shot(cam, d.w, d.h, path);
        EliteSystem.Clear();
        PilotAirspace.Clear();
    }

    static void Boss(FitDevice d, string path, int world, int attack)
    {
        HudStyler styler;
        var cam = Stage(d, world, out styler);
        var root = new GameObject("~ReachStage").transform;
        // the ship at the fight's ceiling, a little off the middle
        var ship = new GameObject("~ReachBossShip").AddComponent<movePlayer>();
        var sr = ship.gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = shopingShips.SpriteFor(ShipId.Starter);
        sr.sortingOrder = 40;
        float k = shopingShips.NormalizedHullScale(sr.sprite);
        ship.transform.localScale = new Vector3(k, k, 1f);
        moveBackGround.speed = .2f;
        BossEncounter.Begin(world, null);
        var e = BossEncounter.Instance;
        e.Step(.1f, 1f);
        for (int i = 0; i < 400 && e.State == BossEncounter.Phase.Intro; i++) e.Step(.1f, 1f);
        float ceiling = ShipReach.Top;
        ship.transform.position = new Vector3(.9f, ceiling, 0f);
        e.Actor.ForcedAttack = attack;
        for (int i = 0; i < 60 * 6; i++)
        {
            e.Step(Dt, 1f);
            if (e.Actor.VolleysFired >= 1 && e.Pool.ActiveShots > 0)
            {
                for (int j = 0; j < 24; j++) e.Step(Dt, 1f);
                break;
            }
        }
        Frame(root, d, cam, true, ShipReach.Bottom, ceiling);
        var f = PlayField.Live;
        Debug.Log(string.Format("[REACHRENDER] {0}: boss at {1:F2} ({2:P0}), shots x{3:F2}, ship ceiling {4:F2} ({5:P0}), {6} shots in flight",
                                path, e.Actor.transform.position.y, f.ShareOf(e.Actor.transform.position.y), BossConfig.ShotScale, ceiling,
                                f.ShareOf(ceiling), e.Pool.ActiveShots));
        Shot(cam, d.w, d.h, path);
        BossEncounter.ResetRun();
    }

    static void Shot(Camera cam, int w, int h, string path)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new UnityEngine.Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
    }
}
