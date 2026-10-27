using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

// The perf pass: Physics2D without automatic transform sync (every query that follows a move syncs by hand,
// the simulation step still syncs itself), the kill-cam recorder's cached walk, and the next world's art
// preloaded so the swap hits the cache.
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites PerfPassTest
public static class PerfPassTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[PERF] PASS  " : "[PERF] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        var made = new List<Object>();
        var mode = Physics2D.simulationMode;
        try
        {
            PhysicsSettings();
            PhysicsQueriesSyncThemselves(made);
            PhysicsTriggersStillSeeMoves(made);
            RecorderGap();
            RecorderCache(made);
            PreloadHitsTheCache();
            PreloadFallbackAndDedupe();
            PreloadSurvivesASweep();
            VariantReservation();
            MainCamera(made);
        }
        finally
        {
            Physics2D.simulationMode = mode;
            WorldArtPreload.Clear();
            WorldArtPreload.SyncLoader = null;
            BackdropVariants.For("Frost").Reset();
            KillCamRecorder.RequireVisible = true;
            KillCamRecorder.DeviceGap = KillCamRecorder.Gap(SystemInfo.systemMemorySize, SystemInfo.graphicsMemorySize);
            if (KillCamRecorder.Instance != null) Object.DestroyImmediate(KillCamRecorder.Instance.gameObject);
            foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
        }
        Debug.Log("[PERF] " + (fails == 0 ? "ALL PASS" : fails + " FAILED"));
        return fails;
    }

    // ---- 1. physics ----
    static void PhysicsSettings()
    {
        Check("Physics2D.autoSyncTransforms is off", !Physics2D.autoSyncTransforms);
        Check("Physics2D iterations are 4 velocity / 2 position (" + Physics2D.velocityIterations + "/" + Physics2D.positionIterations + ")",
              Physics2D.velocityIterations == 4 && Physics2D.positionIterations == 2);
        string proj = File.ReadAllText("ProjectSettings/Physics2DSettings.asset");
        Check("the project file holds them", proj.Contains("m_AutoSyncTransforms: 0") && proj.Contains("m_VelocityIterations: 4") && proj.Contains("m_PositionIterations: 2"));

        // Every physics QUERY in game code is preceded by a manual sync (auto sync is off; queries do not sync themselves).
        var q = new Regex(@"Physics2D\.(Overlap\w*|Raycast\w*|Linecast\w*|Circle\w*|Box\w*|Capsule\w*|Distance|GetContacts|ClosestPoint)\(");
        int calls = 0, unsynced = 0;
        string bad = "";
        foreach (var f in Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories))
        {
            if (f.Replace('\\', '/').Contains("/Editor/")) continue;
            var lines = TestHarness.StripComments(File.ReadAllText(f)).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (!q.IsMatch(lines[i])) continue;
                calls++;
                bool synced = false;
                for (int k = Mathf.Max(0, i - 4); k <= i; k++) synced |= lines[k].Contains("SyncTransforms");
                if (!synced) { unsynced++; bad += " " + Path.GetFileName(f) + ":" + (i + 1); }
            }
        }
        Check("all " + calls + " Physics2D queries in game code sync transforms just before (unsynced:" + bad + ")", calls >= 8 && unsynced == 0);

        // Collider2D.bounds follows the last sync: the readers sync first.
        foreach (var f in new[] { "Gameplay/Weapons/ClearTarget.cs", "Gameplay/Spawning/SpawnSpace.cs", "Tutorial/TutorialWall.cs" })
            Check(f + " syncs before it reads collider bounds", File.ReadAllText("Assets/Scripts/" + f).Contains("SyncTransforms"));
    }

    static GameObject Box(List<Object> made, string name, Vector2 at, Vector2 size, bool body)
    {
        var go = new GameObject(name);
        made.Add(go);
        go.transform.position = at;
        var c = go.AddComponent<BoxCollider2D>();
        c.size = size;
        c.isTrigger = true;
        if (body) go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        return go;
    }

    // Queries after a move, no manual sync in the test: the production readers must do it themselves.
    static void PhysicsQueriesSyncThemselves(List<Object> made)
    {
        var rock = Box(made, "~PerfRock", Vector2.zero, Vector2.one, true);
        Physics2D.SyncTransforms();
        rock.transform.localScale = Vector3.one * 2f;   // bigger than it was when the physics last saw it
        float r = ClearTarget.MeasureRadius(rock);
        Check("ClearTarget measures a just-scaled collider at its new size (" + r.ToString("0.00") + ")", Mathf.Abs(r - 1f) < .02f);

        var pickup = Box(made, "~PerfPickup", new Vector2(5f, 5f), Vector2.one, true);
        Physics2D.SyncTransforms();
        pickup.transform.localScale = Vector3.one * 3f;
        Vector2 half = SpawnSpace.BodyHalf(pickup);
        Check("SpawnSpace.BodyHalf reads a just-scaled collider at its new size (" + half + ")", Mathf.Abs(half.x - 1.5f) < .02f);
    }

    // The trigger step syncs by itself: after a move with auto sync off, one simulation step sees the touch the same frame
    // (player hull vs a hazard, a bullet vs an enemy, a hazard vs the portal).
    static void PhysicsTriggersStillSeeMoves(List<Object> made)
    {
        Physics2D.simulationMode = SimulationMode2D.Script;
        var player = Box(made, "~PerfPlayer", new Vector2(0f, -3f), new Vector2(.6f, .8f), true);
        var hazard = Box(made, "~PerfHazard", new Vector2(0f, 4f), Vector2.one * .8f, true);
        var bullet = Box(made, "~PerfBullet", new Vector2(2f, -3f), new Vector2(.1f, .4f), true);
        var enemy = Box(made, "~PerfEnemy", new Vector2(-6f, 2f), Vector2.one * .8f, true);
        var portal = Box(made, "~PerfPortal", new Vector2(6f, 6f), Vector2.one * 2f, true);
        var mine = Box(made, "~PerfMine", new Vector2(-6f, -6f), Vector2.one * .5f, true);
        Physics2D.Simulate(Time.fixedDeltaTime);
        Check("far apart they do not touch", !player.GetComponent<Collider2D>().IsTouching(hazard.GetComponent<Collider2D>()));

        hazard.transform.position = new Vector3(0f, -3.2f, 0f);
        bullet.transform.position = new Vector3(-6f, 2f, 0f);
        mine.transform.position = new Vector3(6f, 6f, 0f);
        Physics2D.Simulate(Time.fixedDeltaTime);
        Check("a hazard moved onto the ship touches it in the next step",
              player.GetComponent<Collider2D>().IsTouching(hazard.GetComponent<Collider2D>()));
        Check("a bullet moved onto an enemy touches it in the next step",
              bullet.GetComponent<Collider2D>().IsTouching(enemy.GetComponent<Collider2D>()));
        Check("a hazard moved into the portal touches it in the next step",
              mine.GetComponent<Collider2D>().IsTouching(portal.GetComponent<Collider2D>()));

        // ... and a move away ends it
        hazard.transform.position = new Vector3(0f, 4f, 0f);
        Physics2D.Simulate(Time.fixedDeltaTime);
        Check("moved off again, they part", !player.GetComponent<Collider2D>().IsTouching(hazard.GetComponent<Collider2D>()));
        Physics2D.simulationMode = SimulationMode2D.FixedUpdate;
    }

    // ---- 2. the kill-cam recorder ----
    static void RecorderGap()
    {
        Check("a normal phone tapes at MinGap (24 Hz)", KillCamRecorder.Gap(6000, 2000) == KillCamRecorder.MinGap && Mathf.Abs(1f / KillCamRecorder.MinGap - 24f) < .01f);
        Check("a low-RAM phone tapes at LowEndMinGap (20 Hz)", KillCamRecorder.Gap(3000, 2000) == KillCamRecorder.LowEndMinGap);
        Check("a low-VRAM phone tapes at LowEndMinGap", KillCamRecorder.Gap(6000, 512) == KillCamRecorder.LowEndMinGap);
        Check("unknown sizes (0) are not treated as low-end", KillCamRecorder.Gap(0, 0) == KillCamRecorder.MinGap);
    }

    static void RecorderCache(List<Object> made)
    {
        KillCamRecorder.RequireVisible = false;
        KillCamRecorder.DeviceGap = KillCamRecorder.MinGap;
        KillCamRecorder.BeginRun();
        var rec = KillCamRecorder.Instance;
        made.Add(rec.gameObject);
        var tex = new Texture2D(4, 4);
        var sprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f), 4);
        made.Add(tex); made.Add(sprite);
        var rs = new List<SpriteRenderer>();
        for (int i = 0; i < 40; i++)
        {
            var go = new GameObject("~PerfSprite" + i, typeof(SpriteRenderer));
            made.Add(go);
            var sr = go.GetComponent<SpriteRenderer>();
            sr.sprite = sprite;
            rs.Add(sr);
        }
        const float dt = 1f / 60f;
        rec.Flight(dt); rec.Flight(dt); rec.Flight(dt);   // warm
        int frames0 = rec.Frames, gathers0 = rec.Gathers;
        float t = 0f;
        int walkAndCapture = 0;
        for (int f = 0; f < 120; f++)
        {
            int g = rec.Gathers, fr = rec.Frames;
            rec.Flight(dt);
            t += dt;
            if (rec.Gathers != g && rec.Frames != fr) walkAndCapture++;
        }
        int captured = rec.Frames - frames0, gathered = rec.Gathers - gathers0;
        Check("2 s of flight tapes about 24 frames a second (" + captured + ")", captured >= 46 && captured <= 50);
        Check("the renderer walk runs once per " + KillCamRecorder.RefreshEvery + " captures, not per capture (" + gathered + " walks, " + captured + " captures)",
              gathered <= captured / KillCamRecorder.RefreshEvery + 2 && gathered >= 1);
        Check("no frame carries both the walk and the capture (" + walkAndCapture + ")", walkAndCapture == 0);
        Check("a captured frame holds every sprite (" + rec.LastFrameEntries + ")", rec.LastFrameEntries == 40);

        // a sprite made after the last walk is in the tape within RefreshEvery captures, or at once after Invalidate()
        var late = new GameObject("~PerfLate", typeof(SpriteRenderer));
        made.Add(late);
        late.GetComponent<SpriteRenderer>().sprite = sprite;
        KillCamRecorder.Invalidate();
        for (int f = 0; f < 4; f++) rec.Flight(dt);
        Check("an Invalidate()d cache sees a new sprite at the next capture (" + rec.LastFrameEntries + ")", rec.LastFrameEntries == 41);
        // ... a switched-off sprite and a destroyed one drop out without a new walk
        rs[0].enabled = false;
        Object.DestroyImmediate(rs[1].gameObject);
        int g2 = rec.Gathers;
        for (int f = 0; f < 3; f++) rec.Flight(dt);
        Check("a disabled / destroyed sprite leaves the next capture (" + rec.LastFrameEntries + ")", rec.LastFrameEntries == 39 || rec.Gathers != g2);

        long control;
        bool meter = TestHarness.AllocMeterWorks(out control);
        long used = TestHarness.AllocatedBytes(() => { for (int i = 0; i < 240; i++) rec.Flight(dt); });
        Check("flight taping allocates nothing per frame: " + used + " bytes (meter control " + control + ")", meter && used == 0);
    }

    // ---- 4. the preload ----
    static void PreloadHitsTheCache()
    {
        string world = "Frost";
        WorldArtPreload.SyncLoader = null;
        WorldArtPreload.Clear();
        BackdropVariants.For(world).Reset();
        WorldArtPreload.BackdropFor(world);   // out of play: loaded at once
        Check("the preload holds the world's art (" + WorldArtPreload.HeldCount + " objects)", WorldArtPreload.HeldCount >= 4 && WorldArtPreload.Done);
        var spec = BackdropCatalog.For(world);
        var vs = BackdropVariants.For(world);
        Check("the variant was reserved ahead", spec.variantSets == 0 || vs.Reserved > 0);
        int variant = vs.Reserved;
        WorldArtPreload.ResetCounters();
        var root = new GameObject("~PerfBackdropRoot");
        var set = new BackdropSet(world, root.transform, 3f, 6f, new BackdropSet.MenuOptions { variant = variant });
        try
        {
            Check("the swap's tile loads all hit the cache (" + WorldArtPreload.Hits + " hits, " + WorldArtPreload.SyncLoads + " sync loads)",
                  set.Complete && set.Tiles.Count > 0 && WorldArtPreload.SyncLoads == 0 && WorldArtPreload.Hits >= set.Tiles.Count);
            WorldArtPreload.ResetCounters();
            var anim = BackdropSet.LoadAnimAtlas(BackdropCatalog.Folder(world));
            Check("the anim atlas comes from the cache (" + WorldArtPreload.SyncLoads + " sync loads)", anim != null && WorldArtPreload.SyncLoads == 0);
        }
        finally { set.Destroy(); Object.DestroyImmediate(root); WorldArtPreload.Clear(); }
        Check("clearing lets go of the hold", WorldArtPreload.HeldCount == 0 && WorldArtPreload.Key == null);

        // planetfall: the seven textures are held and read from the cache
        var def = PlanetfallCatalog.For(0, 1, false);
        WorldArtPreload.ResetCounters();
        WorldArtPreload.Begin("pf-test", () => WorldArtPreload.QueuePlanetfall(def));
        WorldArtPreload.ResetCounters();
        var art = PlanetfallArt.Load(def);
        Check("the planetfall art's seven textures come from the cache (" + WorldArtPreload.Hits + " hits, " + WorldArtPreload.SyncLoads + " sync)",
              art.Complete && WorldArtPreload.Hits == 7 && WorldArtPreload.SyncLoads == 0);
        art.Release();
        WorldArtPreload.Clear();
    }

    static void PreloadFallbackAndDedupe()
    {
        WorldArtPreload.Clear();
        WorldArtPreload.ResetCounters();
        var root = new GameObject("~PerfBackdropRoot2");
        var set = new BackdropSet("Frost", root.transform, 3f, 6f, new BackdropSet.MenuOptions { variant = 1 });
        try
        {
            Check("without a preload the swap falls back to the synchronous load and still builds (" + WorldArtPreload.SyncLoads + " sync loads)",
                  set.Complete && WorldArtPreload.SyncLoads >= set.Tiles.Count && WorldArtPreload.Hits == 0);
        }
        finally { set.Destroy(); Object.DestroyImmediate(root); }

        // asking twice for the same world / path loads once
        var calls = new List<string>();
        WorldArtPreload.SyncLoader = (t, p) => { calls.Add(p); return null; };
        bool first = WorldArtPreload.Begin("dup", () => { WorldArtPreload.Request<Texture2D>("a/b"); WorldArtPreload.Request<Texture2D>("a/b"); WorldArtPreload.Request<Sprite>("a/b"); });
        bool again = WorldArtPreload.Begin("dup", () => WorldArtPreload.Request<Texture2D>("a/c"));
        Check("a repeated key is a no-op and a repeated path is queued once (" + string.Join(",", calls) + ")",
              first && !again && calls.Count == 2 && calls[0] == "a/b" && calls[1] == "a/b");
        // a skip: another world replaces the hold
        WorldArtPreload.Begin("other", () => { });
        Check("a different key lets go of the previous hold", WorldArtPreload.Key == "other" && WorldArtPreload.HeldCount == 0);
        WorldArtPreload.SyncLoader = null;
        WorldArtPreload.Clear();
    }

    static void PreloadSurvivesASweep()
    {
        WorldArtPreload.Clear();
        WorldArtPreload.Begin("sweep", () => WorldArtPreload.Request<Sprite>(BackdropCatalog.TileFolder("Frost", 1) + "sky"));
        string path = BackdropCatalog.TileFolder("Frost", 1) + "sky";
        bool held = WorldArtPreload.Holds(path, typeof(Sprite));
        Resources.UnloadUnusedAssets();   // what AssetSweep runs under the clouds
        WorldArtPreload.ResetCounters();
        var s = WorldArtPreload.Load<Sprite>(path);
        Check("a held preload survives an asset sweep and is still served from the cache", held && s != null && WorldArtPreload.Hits == 1 && WorldArtPreload.SyncLoads == 0);
        WorldArtPreload.Clear();
    }

    // ---- cheap hot paths ----
    static void MainCamera(List<Object> made)
    {
        var go = new GameObject("~PerfCam", typeof(Camera));
        go.tag = "MainCamera";
        made.Add(go);
        var cam = go.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 7f;
        go.transform.position = new Vector3(0f, 2f, -10f);
        Check("out of play CameraFit.Main is Camera.main (tests that swap cameras see it at once)", CameraFit.Main == Camera.main && CameraFit.Main == cam);
        Check("ViewTop / ViewBottom follow the main camera (" + CameraFit.ViewTop + " / " + CameraFit.ViewBottom + ")",
              Mathf.Abs(CameraFit.ViewTop - 9f) < 1e-4f && Mathf.Abs(CameraFit.ViewBottom + 5f) < 1e-4f);
    }

    static void VariantReservation()
    {
        var vs = BackdropVariants.For("Frost");
        vs.Reset();
        vs.Seed(7);
        int a = vs.Reserve(4);
        int again = vs.Reserve(4);
        int got = vs.Pick(4);
        Check("a reserved variant is the one the next Pick returns, once", a == again && got == a && vs.Reserved == 0 && vs.Picks == 1);
        int next = vs.Pick(4);
        Check("the pick after it is a fresh one (never the same twice)", next != got || vs.InstalledCount(4) <= 1);
        vs.Reset();
    }
}
