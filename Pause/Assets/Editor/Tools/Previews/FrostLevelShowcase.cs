using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The Frost level as the phone camera sees it (gameS1, 1080x2400), with the
// whole redrawn cast wired up through the game's own code paths, for review:
//
//   variant-vN.png            each ground set (v1..v4) mid-level: sky, far, mid,
//                             flow, landmarks, a dock / launch site, the ambient
//                             loops (smoke, steam, beacons, aurora), snow and the
//                             rails, with the player ship
//   v1-blizzard.png           a gust sweeping across v1
//   wave-idle.png             a Frost wave (frost_fighter_1..4, frost_chaser,
//   wave-tell.png             frost_alien, frost_big, a Frost rock and the Frost
//   wave-shots.png            rail mine on its rail) built by EnemyFactory, driven
//   wave-late.png             by their EnemyBrain / ChaserEnemy / flipbooks: idle,
//                             then tells, then shots in flight
//   elite-<key>.png           every Frost elite flying (EliteShip.CreateInPlay),
//   elite-<key>-attack.png    engines lit, then in its attack
//   boss-idle.png             the Frost boss (BossEncounter) after its entrance
//   boss-tell.png             its tell, then mid attack with shots out
//   boss-attack.png
//   boss-damaged.png          the boss at a damage stage (hearts knocked off
//   boss-damaged-attack.png   through OnShipAttackHit), idle and attacking
//   frost_v1_frames/NNN.png   24 frames at 15 fps of v1 gameplay (CI turns
//                             each *_frames folder into <name>.gif)
//   props-<a>-<b>.png         the living props (FrostProps.cs) two at a time, placed
//   props_<a>_<b>_frames/     in view by their own rules, + 30 frames at 15 fps
//                             (a whale / orca surfacing, the loops and lamps)
//
//   SHOWCASE_DIR=<dir> Unity -batchmode -quit -projectPath Pause -executeMethod FrostLevelShowcase.Run
//   (default dir Builds/Showcase; see FrostBackdropPreview for the scene setup this reuses)
public static class FrostLevelShowcase
{
    const float Dt = 1f / 60f;
    const int W = 1080, H = 2400;
    const int Frost = 1;           // WorldManager.Worlds index
    const float WarmSeconds = 38f; // backdrop clock before a shot: the cloud ceiling has cleared

    static string dir;
    static Camera cam;
    static WorldBackdrop wb;
    static FrostDirector director;
    static GameObject ship;
    static float clock;
    static readonly List<GameObject> wave = new List<GameObject>();
    static readonly List<EnemyFlipbook> books = new List<EnemyFlipbook>();
    static RailMineMount mount;

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
                EliteCatalog.Reload();
                failures += Section("variants", Variants);
                failures += Section("wave", Wave);
                failures += Section("elites", Elites);
                failures += Section("boss", Boss);
                failures += Section("gif", Gif);
                failures += Section("props", Props);
            }
            finally
            {
                Clean();
                FrostBackdropSelection.Reset();
                EnemyThreat.ForceShooting = false;
                RailMineLaser.AngleOverride = null;
                SpawnSpace.ClockOverride = null;
                EliteSystem.PlayerOverride = null;
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

    // ---- the scene (FrostBackdropPreview / FrostBrightPreview's setup) -------------

    static void Clean()
    {
        wave.Clear();
        books.Clear();
        mount = null;
        EnemyThreat.Reset();
        RailMineLasers.Clear();
        EliteSystem.Clear();
        BossEncounter.ResetRun();
    }

    static void Scene(int variant)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity");
        Clean();
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
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, Frost);
        Random.InitState(7);
        var paused = SceneUtil.FindAny("paused");
        if (paused != null) paused.SetActive(false);
        WorldPainter.Apply(WorldManager.Worlds[Frost]);
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

        FrostBackdropSelection.Reset();
        FrostBackdropSelection.Force = variant;
        wb = WorldBackdrop.Create("Frost");
        wb.Show("Space", false);
        wb.Show("Frost", false);
        director = wb.Current.Director as FrostDirector;
        if (director == null) throw new System.Exception("Frost backdrop has no FrostDirector");
        while (director.Clock < WarmSeconds) wb.Step(Dt);
        HazardRuntime.Ensure();
        clock = 100f;
        SpawnSpace.ClockOverride = clock;
    }

    // One fixed step of everything on screen. Edit mode runs no Update, so each
    // piece is stepped through the same public entry its tests use.
    static void Step(float dt)
    {
        clock += dt;
        SpawnSpace.ClockOverride = clock;
        wb.Step(dt);
        for (int i = 0; i < wave.Count; i++)
        {
            var go = wave[i];
            if (go == null || !go.activeInHierarchy) continue;
            var brain = go.GetComponent<EnemyBrain>();
            if (brain != null && brain.enabled) brain.Step(dt);
            var chaser = go.GetComponent<ChaserEnemy>();
            if (chaser != null && chaser.enabled) chaser.Step(dt);
            var spin = go.GetComponent<AsteroidSpin>();
            if (spin != null) spin.Advance(dt);
            var fb = go.GetComponent<EnemyFlipbook>();
            if (fb != null) fb.Advance(dt);
        }
        foreach (var m in Object.FindObjectsByType<RailMineMount>(FindObjectsSortMode.None)) if (m != null) m.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
        EliteSystem.Step(dt);
        var live = EliteShip.Live;
        for (int i = 0; i < live.Count; i++)
        {
            if (live[i] == null) continue;
            var h = live[i].GetComponent<EliteHearts>();
            if (h != null) { h.Place(dt, dt); h.StepBreaks(dt); }
        }
        foreach (var laser in Object.FindObjectsByType<RailMineLaser>(FindObjectsSortMode.None))
            if (laser != null) laser.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
        foreach (var fx in Object.FindObjectsByType<FlipbookFx>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) fx.Tick(dt);
        if (HazardRuntime.Instance != null) HazardRuntime.Instance.Step(dt);
        Flames(dt);
    }

    static void Flames(float dt)
    {
        if (ship == null) return;
        foreach (var f in ship.GetComponentsInChildren<ShipFlameFlipbook>(true)) f.Step(dt);
    }

    static void Steps(float seconds)
    {
        for (float t = 0f; t < seconds - 1e-4f; t += Dt) Step(Dt);
    }

    // ---- each ground set -----------------------------------------------------------

    static void Variants()
    {
        for (int v = 1; v <= FrostBackdropSelection.MaxVariants; v++)
        {
            Scene(v);
            // a dock / launch site in the upper view, as FrostBackdropPreview's launch waits for one
            bool site = false;
            for (int k = 0; k < 600 && !(site = director.SpawnSite(cam.orthographicSize * .35f)); k++) Step(Dt);
            Steps(1.5f);
            Capture("variant-v" + v + ".png");
            Debug.Log("[SHOWCASE] variant v" + v + " (shown " + wb.Current.Variant + ") landmarks " + director.Ground.ActiveCount +
                      " sites " + director.Sites.ActiveCount + " (forced " + site + ") loops " + director.Ambient.LiveCount +
                      " auroras " + director.Aurora.ActiveCount + " clock " + director.Clock.ToString("F1"));
            if (v == 1)
            {
                director.ForceGust();
                Steps(80 * Dt);
                Capture("v1-blizzard.png");
                Debug.Log("[SHOWCASE] blizzard sheets " + director.Blizzard.ActiveCount + " gusts " + director.Gusts);
            }
        }
    }

    // ---- a Frost wave ------------------------------------------------------------

    static EnemyDef Def(string key, EnemyRole fallback)
    {
        var def = EnemyRoster.Find(key);
        if (def == null || def.world != Frost)
        {
            Debug.LogWarning("[SHOWCASE] no Frost roster entry '" + key + "', using the world's " + fallback);
            def = EnemyRoster.One(Frost, fallback);
        }
        return def;
    }

    static GameObject Spawn(EnemyDef def, Vector2 at)
    {
        if (def == null) return null;
        var go = EnemyFactory.Create(def, at, Quaternion.identity);
        if (go == null) return null;
        var brain = go.GetComponent<EnemyBrain>();
        if (brain != null) brain.TargetOverride = ship.transform;
        var chaser = go.GetComponent<ChaserEnemy>();
        if (chaser != null) chaser.Target = ship.transform;
        var fb = go.GetComponent<EnemyFlipbook>();
        if (fb != null) books.Add(fb);
        wave.Add(go);
        return go;
    }

    // The cast of a typical Frost board, spread over the view, through the
    // spawner's factory (art, mover, brain, flipbook, footprint, toughness).
    static void BuildWave()
    {
        EnemyThreat.ForceShooting = true;
        RailMineLaser.AngleOverride = 14f;
        float top = cam.orthographicSize;
        Spawn(Def("frost_fighter_1", EnemyRole.Fighter), new Vector2(-1.5f, top * .72f));
        Spawn(Def("frost_fighter_2", EnemyRole.Fighter), new Vector2(.3f, top * .80f));
        Spawn(Def("frost_fighter_3", EnemyRole.Fighter), new Vector2(1.5f, top * .58f));
        Spawn(Def("frost_fighter_4", EnemyRole.Fighter), new Vector2(-.5f, top * .46f));
        Spawn(Def("frost_big", EnemyRole.Big), new Vector2(1.1f, top * .22f));
        Spawn(Def("frost_alien", EnemyRole.Alien), new Vector2(-1.4f, top * .16f));
        Spawn(Def("frost_rock_chunk", EnemyRole.Rock), new Vector2(.2f, -top * .05f));
        Spawn(Def("frost_chaser", EnemyRole.Chaser), new Vector2(1.4f, -top * .25f));

        // a rail mine on each rail, clamped as enmiesOnBoard mounts them (MountTo turns its clamp to the rail, eye to the lane)
        var mdef = Def("frost_mine", EnemyRole.Mine);
        foreach (bool left in new[] { false, true })
        {
            float x = enmiesOnBoard.WorldRailX(left, Frost), y = top * (left ? -.35f : .05f);
            var rail = new GameObject("RailMineLane");
            rail.transform.position = new Vector3(x, y, 0f);
            rail.AddComponent<RailLaneScroller>();
            var mgo = Spawn(mdef, new Vector2(x, y));
            if (mgo == null) continue;
            var m = mgo.AddComponent<RailMineMount>();
            m.MountTo(rail.transform);
            m.brain = mgo.GetComponent<EnemyBrain>();
            if (!left) mount = m;
        }
        foreach (var go in wave)
        {
            var id = go != null ? EnemyIdentity.Of(go) : null;
            var fb = go != null ? go.GetComponent<EnemyFlipbook>() : null;
            Debug.Log("[SHOWCASE] wave " + (id != null ? id.key : "?") + " at " + (go != null ? go.transform.position.ToString() : "-") +
                      " frames " + (fb != null && fb.HasFrames) + " brain " + (go != null && go.GetComponent<EnemyBrain>() != null));
        }
    }

    static int Telling()
    {
        int n = 0;
        foreach (var fb in books) if (fb != null && fb.Telling) n++;
        return n;
    }

    static void Wave()
    {
        Scene(1);
        BuildWave();
        Steps(1f);
        Capture("wave-idle.png");
        Debug.Log("[SHOWCASE] wave-idle telling " + Telling() + " shots " + EnemyThreat.LiveShots);

        int guard = 0;
        while (Telling() < 2 && guard++ < 60 * 6) Step(Dt);
        Capture("wave-tell.png");
        Debug.Log("[SHOWCASE] wave-tell telling " + Telling() + " shots " + EnemyThreat.LiveShots + " after " + (guard * Dt).ToString("F2") + " s");

        guard = 0;
        while (EnemyThreat.LiveShots < 3 && guard++ < 60 * 6) Step(Dt);
        Steps(.15f);
        Capture("wave-shots.png");
        Debug.Log("[SHOWCASE] wave-shots telling " + Telling() + " shots " + EnemyThreat.LiveShots + " volleys " + EnemyVolley.Volleys);

        Steps(1f);
        Capture("wave-late.png");
        Debug.Log("[SHOWCASE] wave-late telling " + Telling() + " shots " + EnemyThreat.LiveShots);
    }

    // ---- elites ------------------------------------------------------------------

    static void Elites()
    {
        Scene(1);
        var defs = new List<EliteDef>();
        EliteCatalog.ForWorld(Frost, defs);
        if (defs.Count == 0) throw new System.Exception("no Frost elites in EliteCatalog");
        foreach (var def in defs)
        {
            EliteSystem.Clear();
            var e = EliteShip.CreateInPlay(def, new Vector2(-.3f, cam.orthographicSize * .25f));
            if (e == null) { Debug.LogWarning("[SHOWCASE] elite " + def.key + " not created"); continue; }
            e.AttackCooldown = 99f;
            Steps(1.4f);
            Capture("elite-" + def.key + ".png");
            Debug.Log("[SHOWCASE] elite " + def.key + " state " + e.State + " nozzles " + def.nozzles.Length + " exhaust " + def.exhaustScale);
            if (e.State != EliteState.Dead)
            {
                e.ForceAttack();
                bool shot = false;
                for (int g = 0; g < 60 * 4 && e != null && e.State == EliteState.Attack; g++)
                {
                    Step(Dt);
                    if (!shot && e.Acting && e.StateTime > .25f) { Steps(.2f); Capture("elite-" + def.key + "-attack.png"); shot = true; }
                }
                if (!shot) Capture("elite-" + def.key + "-attack.png");
            }
            e.AttackCooldown = 99f;
        }
        EliteSystem.Clear();
    }

    // ---- the boss ----------------------------------------------------------------

    static BossEncounter boss;

    static void BossStep(float dt)
    {
        boss.Step(dt, 1f);
        wb.Step(dt);
        EliteSystem.Step(dt);
        Flames(dt);
        if (buttonClicks.playerDied) { Debug.LogWarning("[SHOWCASE] the ship died during the boss; reviving for the capture"); buttonClicks.playerDied = false; }
    }

    static int PickAttack(BossDef def)
    {
        for (int a = 0; a < def.attacks.Length; a++)
            if (def.attacks[a].kind == BossAttackKind.Fan) return a;
        return 0;
    }

    static void Attack(string tellFile, string fireFile)
    {
        int pick = PickAttack(boss.Boss);
        boss.Actor.ForcedAttack = pick;
        int started = boss.Actor.AttacksStarted;
        for (int i = 0; i < 600 && boss.Actor.AttacksStarted == started && boss.State == BossEncounter.Phase.Fight; i++) BossStep(Dt);
        bool tell = tellFile == null, fire = false;
        float inTell = 0f;
        for (int i = 0; i < 60 * 6 && boss.State == BossEncounter.Phase.Fight && !fire; i++)
        {
            BossStep(Dt);
            if (boss.Actor.Telegraphing) inTell += Dt;
            if (!tell && boss.Actor.Telegraphing && inTell >= .3f) { Capture(tellFile); tell = true; }
            if (boss.Actor.CurrentAttack != null && !boss.Actor.Telegraphing && boss.Pool.ActiveShots > 0)
            {
                for (int j = 0; j < 20; j++) BossStep(Dt);
                Capture(fireFile);
                fire = true;
            }
        }
        if (!fire) Capture(fireFile);
        Debug.Log("[SHOWCASE] boss attack " + pick + " (" + boss.Boss.attacks[pick].name + ") -> " + fireFile + " shots " + boss.Pool.ActiveShots +
                  " beams " + boss.Pool.ActiveBeams + " frame " + boss.Actor.BodyFrame + " stage " + boss.Actor.DamageStage);
        boss.Actor.ForcedAttack = -1;
    }

    static void Boss()
    {
        Scene(1);
        ship.transform.position = new Vector3(-.9f, ShipReach.StartY, 0f);
        if (!BossEncounter.Begin(Frost, null)) throw new System.Exception("BossEncounter.Begin refused Frost");
        boss = BossEncounter.Instance;
        boss.Step(.1f, 1f);
        for (int i = 0; i < 400 && boss.State == BossEncounter.Phase.Intro; i++) { boss.Step(.1f, 1f); wb.Step(.1f); }
        if (boss.State != BossEncounter.Phase.Fight) throw new System.Exception("boss not in its fight: " + boss.State);
        Debug.Log("[SHOWCASE] boss " + boss.Boss.name + " (" + boss.Boss.artKey + ") hearts " + boss.HeartsLeft);

        for (int i = 0; i < 600; i++)
        {
            BossStep(Dt);
            if (i > 30 && boss.Actor.CurrentAttack == null && BossArt.IsIdleFrame(boss.Actor.BodyFrame)) break;
        }
        Capture("boss-idle.png");
        Attack("boss-tell.png", "boss-attack.png");

        // knock hearts off through the ship-attack path until it shows a later damage stage
        int target = Mathf.Max(1, boss.HeartsLeft / 3);
        for (int i = 0; i < 200 && boss.HeartsLeft > target && boss.State == BossEncounter.Phase.Fight; i++)
        {
            boss.OnShipAttackHit(BossConfig.HeartWeight * .5f);
            BossStep(Dt);
        }
        for (int i = 0; i < 90; i++) BossStep(Dt);
        for (int i = 0; i < 600 && (boss.Actor.CurrentAttack != null || !BossArt.IsIdleFrame(boss.Actor.BodyFrame)); i++) BossStep(Dt);
        for (int i = 0; i < 120 && boss.Actor.DamageStage == 3 && !boss.Actor.DamageArcs.enabled; i++) BossStep(Dt);
        Capture("boss-damaged.png");
        Debug.Log("[SHOWCASE] boss damaged: hearts " + boss.HeartsLeft + " stage " + boss.Actor.DamageStage + " smoke " +
                  boss.Actor.DamageSmoke.enabled + " arcs " + boss.Actor.DamageArcs.enabled + " state " + boss.State);
        if (boss.State == BossEncounter.Phase.Fight) Attack(null, "boss-damaged-attack.png");
        BossEncounter.ResetRun();
        boss = null;
    }

    // ---- the animated sequence ---------------------------------------------------

    // ---- the living props ------------------------------------------------------------

    static void Props()
    {
        // (variant, two kinds): the water props on v2's leads, the structures on v1's ice plates
        var shots = new[]
        {
            new[] { 2, FrostPropsTuning.Whale, FrostPropsTuning.Icebreaker },
            new[] { 1, FrostPropsTuning.Station, FrostPropsTuning.Plane },
            new[] { 2, FrostPropsTuning.Orca, FrostPropsTuning.Lab },
        };
        foreach (var shot in shots)
        {
            Scene(shot[0]);
            director.ClearProps();
            float h = cam.orthographicSize;
            double tr = director.Planner.Travel;
            string tag = FrostPropsTuning.Names[shot[1]] + "-" + FrostPropsTuning.Names[shot[2]];
            for (int j = 1; j <= 2; j++)
            {
                bool ok = false;
                // the first in the upper half, the second in the lower half; any variant's ground if this one has no room
                double lo = j == 1 ? tr - h * .05f : tr - h * .6f, hi = j == 1 ? tr + h * .6f : tr + h * .05f;
                for (int a = 0; a < 6 && !ok; a++) ok = director.SpawnProp(shot[j], lo, hi, 800);
                if (!ok) for (int a = 0; a < 6 && !ok; a++) ok = director.SpawnProp(shot[j], tr - h * .8f, tr + h * .8f, 800);
                Debug.Log("[SHOWCASE] props v" + shot[0] + " " + FrostPropsTuning.Names[shot[j]] + " placed " + ok);
            }
            director.SurfaceNow();
            Steps(.45f);
            Capture("props-" + tag + ".png");
            foreach (var p in director.Props.items)
            {
                if (!p.active) continue;
                // (the camera renders into a W x H target only inside Capture: map world -> capture pixels by hand)
                Vector3 rel = p.root.position - cam.transform.position;
                float ppu = H / (2f * cam.orthographicSize);
                Debug.Log("[SHOWCASE] prop " + p.sr.sprite.name + " at capture px " + Mathf.RoundToInt(W * .5f + rel.x * ppu) + "," + Mathf.RoundToInt(H * .5f - rel.y * ppu) +
                          " size " + Mathf.RoundToInt(p.size * W / (2f * cam.orthographicSize * cam.aspect)) + " px");
            }
            string frames = Path.Combine(dir, "props_" + tag.Replace('-', '_') + "_frames");
            if (Directory.Exists(frames)) Directory.Delete(frames, true);
            Directory.CreateDirectory(frames);
            director.SurfaceNow();
            for (int f = 0; f < 30; f++)
            {
                if (f > 0) for (int s2 = 0; s2 < 4; s2++) Step(Dt);
                Capture(Path.Combine(Path.GetFileName(frames), f.ToString("000") + ".png"));
            }
        }
    }

    static void Gif()
    {
        Scene(1);
        BuildWave();
        Steps(.8f);
        string frames = Path.Combine(dir, "frost_v1_frames");
        if (Directory.Exists(frames)) Directory.Delete(frames, true);
        Directory.CreateDirectory(frames);
        const int Count = 24, StepsPerFrame = 4;   // 15 fps of 60 Hz steps
        for (int f = 0; f < Count; f++)
        {
            if (f > 0) for (int s = 0; s < StepsPerFrame; s++) Step(Dt);
            Capture(Path.Combine("frost_v1_frames", f.ToString("000") + ".png"));
        }
        Debug.Log("[SHOWCASE] gif frames " + Count + " -> " + frames + " shots " + EnemyThreat.LiveShots);
    }

    // ---- capture -----------------------------------------------------------------

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
