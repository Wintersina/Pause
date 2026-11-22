using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// In-game review captures of the round-5 Desert grounds (1080 x 2400, the phone camera), one set per ground variant:
//
//   v<V>-play.png          the ship, a handful of live enemies and their shots over the ground, the placed pieces in view
//   v<V>-late.png          the same ground ~40 s on (more pieces, pipe runs, ambient loops)
//   v<V>-worm-ridge.png    a medium worm's ridge and wake on the ground (over the enemies / ship)
//   v<V>-worm-erupt.png    the same worm erupting
//   v<V>-dock-<k>.png      an elite launch site (dock) parked on the ground with the elite still inside (the tell), k = site kind
//   fade-NN.png            the Space -> Desert crossfade (the only backdrop transition the game has; variants are rolled per landing)
//
//   DESERT_WIRE_DIR=<dir> Unity -batchmode -quit -projectPath Pause -executeMethod DesertWireCapture.Run
public static class DesertWireCapture
{
    const float Dt = 1f / 60f;
    const int W = 1080, H = 2400;

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("DESERT_WIRE_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/DesertWire";
        Directory.CreateDirectory(dir);
        int failures = 0;
        using (new TestHarness.Sandbox())
        {
            try
            {
                for (int v = 1; v <= 4; v++) Variant(dir, v);
                Fade(dir);
            }
            catch (System.Exception e) { Debug.LogException(e); failures++; }
            finally
            {
                EnemyThreat.ForceShooting = false;
                EnemyThreat.Reset();
                BackdropSet.AtlasOverride = null;
                BackdropVariants.For("Desert").Reset();
                EliteSystem.PlayerOverride = null;
                EliteSystem.Clear();
                BossEncounter.ResetRun();
                PortalPressure.Reset();
                BossRails.Reset();
                PlayField.Reset();
                ScreenInfo.ClearOverride();
                if (WorldBackdrop.Instance != null) Object.DestroyImmediate(WorldBackdrop.Instance.gameObject);
            }
        }
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    static Camera Scene()
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
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, 6);
        Random.InitState(7);
        var paused = SceneUtil.FindAny("paused");
        if (paused != null) paused.SetActive(false);
        WorldPainter.Apply(WorldManager.Worlds[6]);
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
        return cam;
    }

    static void Variant(string dir, int v)
    {
        var cam = Scene();
        var ship = new GameObject("~PfSpawner").AddComponent<spawnShips>().Spawn(ShipId.Starter);
        ship.transform.position = new Vector3(-.6f, ShipReach.StartY, 0f);
        EliteSystem.PlayerOverride = ship.transform;
        BackdropVariants.For("Desert").Force = v;
        var wb = WorldBackdrop.Create("Desert");
        wb.Show("Space", false);
        wb.Show("Desert", false);
        var d = (DesertDirector)wb.Current.Director;
        while (d.Clock < 2.5f) wb.Step(Dt);
        Capture(cam, Path.Combine(dir, "v" + v + "-ceiling.png"));
        while (d.Clock < 16f) wb.Step(Dt);
        HazardRuntime.Ensure();

        // live enemies with shots
        EnemyThreat.ForceShooting = true;
        var gos = new List<GameObject>();
        var brains = new List<EnemyBrain>();
        var flips = new List<EnemyFlipbook>();
        var spots = new[]
        {
            new { role = EnemyRole.Fighter, x = .9f, y = 2.6f },
            new { role = EnemyRole.Fighter, x = -1.3f, y = 3.4f },
            new { role = EnemyRole.Chaser, x = 1.5f, y = 1.2f },
            new { role = EnemyRole.Rock, x = -1.6f, y = 1.9f },
            new { role = EnemyRole.Alien, x = .1f, y = 4.2f },
        };
        foreach (var s in spots)
        {
            var def = EnemyRoster.One(6, s.role);
            if (def == null) continue;
            var go = EnemyFactory.Create(def, new Vector3(s.x, s.y, 0f), Quaternion.identity);
            var br = go.GetComponent<EnemyBrain>();
            if (br != null) br.TargetOverride = ship.transform;
            gos.Add(go); brains.Add(br); flips.Add(go.GetComponent<EnemyFlipbook>());
        }
        for (int i = 0; i < 60 * 3; i++)
        {
            wb.Step(Dt);
            for (int k = 0; k < brains.Count; k++)
            {
                if (brains[k] != null) brains[k].Step(Dt);
                if (flips[k] != null) flips[k].Advance(Dt);
            }
            EliteSystem.Step(Dt);
        }
        Capture(cam, Path.Combine(dir, "v" + v + "-play.png"));

        // later: more of the ground's pieces
        for (int i = 0; i < 60 * 22; i++)
        {
            wb.Step(Dt);
            for (int k = 0; k < brains.Count; k++)
            {
                if (brains[k] != null) brains[k].Step(Dt);
                if (flips[k] != null) flips[k].Advance(Dt);
            }
        }
        Capture(cam, Path.Combine(dir, "v" + v + "-late.png"));
        Debug.Log("[DESERT-WIRE] v" + v + " ground " + d.Ground.ActiveCount + " fires " + d.Fires.ActiveCount + " pipes " + d.Pipes.ActiveCount +
                  " sites " + d.Sites.ActiveCount + " clusters " + d.Clusters + " runs " + d.PipeRuns);
        foreach (var go in gos) if (go != null) Object.DestroyImmediate(go);
        EnemyThreat.ForceShooting = false;

        // a worm: ridge, then eruption
        d.ClearWorms();
        bool spawned = false;
        for (int t = 0; t < 60 * 90 && !(spawned = d.SpawnWorm(1, float.NaN)); t++) wb.Step(Dt);
        if (spawned)
        {
            var worm = d.Worms.items[0];
            bool ridgeShot = false, eruptShot = false;
            for (int i = 0; i < 60 * 40 && worm.active && !eruptShot; i++)
            {
                wb.Step(Dt);
                if (!ridgeShot && worm.tier == DesertDirector.WormRidge && worm.age > 1.2f) { Capture(cam, Path.Combine(dir, "v" + v + "-worm-ridge.png")); ridgeShot = true; }
                if (worm.tier == DesertDirector.WormArc && worm.phase > .3f) { Capture(cam, Path.Combine(dir, "v" + v + "-worm-erupt.png")); eruptShot = true; }
            }
            Debug.Log("[DESERT-WIRE] v" + v + " worm ridge " + ridgeShot + " erupt " + eruptShot);
        }
        else Debug.LogWarning("[DESERT-WIRE] v" + v + " no worm could be placed");

        // launch sites parked on the ground, two kinds
        var defs = new List<EliteDef>();
        EliteCatalog.ForWorld(6, defs);
        foreach (int k in new[] { 5, 7 })
        {
            d.ClearWorms();
            d.Showcase("worm_burrow_closed", 1.6f, 0f, -20f);
            bool ok = false;
            for (int t = 0; t < 60 * 30 && !ok; t++) { ok = d.SpawnSite(cam.orthographicSize * .3f, k); if (!ok) { for (int j = 0; j < 30; j++) wb.Step(Dt); } }
            for (int i = 0; i < 10; i++) wb.Step(Dt);
            var sites = new List<LandingSite>();
            LandingSites.Collect(sites);
            LandingSite site = default(LandingSite);
            bool have = false;
            foreach (var s in sites) if (s.kind == DesertTuning.SiteKinds[k]) { site = s; have = true; }
            EliteDef def = null;
            foreach (var e0 in defs) if (LandingSite.KindOf(e0.launchFrom) == DesertTuning.SiteKinds[k]) { def = e0; break; }
            if (!have || def == null) { Debug.LogWarning("[DESERT-WIRE] v" + v + " no site " + k); continue; }
            var dirGo = new GameObject("~EliteDir").AddComponent<EliteDirector>();
            var e = dirGo.Spawn(def, site, 2.2f);
            for (int i = 0; i < 60 * 8 && e != null; i++)
            {
                wb.Step(Dt);
                EliteSystem.Step(Dt);
                if (e.State == EliteState.Parked && e.StateTime >= e.ParkSeconds - .5f) { Capture(cam, Path.Combine(dir, "v" + v + "-dock-" + k + ".png")); break; }
            }
            EliteSystem.Clear();
            Object.DestroyImmediate(dirGo.gameObject);
        }
        Object.DestroyImmediate(wb.gameObject);
    }

    // Space -> Desert crossfade (WorldBackdrop.CrossfadeSeconds)
    static void Fade(string dir)
    {
        var cam = Scene();
        BackdropVariants.For("Desert").Force = 1;
        var wb = WorldBackdrop.Create("Space");
        wb.Show("Space", false);
        for (int i = 0; i < 60; i++) wb.Step(Dt);
        wb.Show("Desert", true);
        for (int n = 0; n < 4; n++)
        {
            for (int i = 0; i < 18; i++) wb.Step(Dt);
            Capture(cam, Path.Combine(dir, "fade-" + n.ToString("00") + ".png"));
        }
        Object.DestroyImmediate(wb.gameObject);
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
    }
}
