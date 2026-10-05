using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

// Feature: "make sure no 2 enemies spawn on top of each other -- later I want
// custom attack / movement patterns per enemy and don't want them to
// interfere" (SpawnSpace, SpawnFootprint, IMovementFootprint).
//
//   - long seeded runs of the real spawner in every world, at three scroll
//     speeds and first-pass / max loop density, with every enemy moved by
//     its real mover (weave, straight, rail mine, steering chaser) stepped
//     headless: no two live enemy bodies ever overlap, sampled every frame
//   - spawns with no room are deferred, and nearly all of them land
//   - spawn counts stay within 5% of the pre-SpawnSpace spawner's
//   - a mock future movement pattern's declared sweep (a timed dash) is
//     respected, both by the planner and by the real spawner
//   - pickups land clear of enemy footprints
//   - the planner allocates nothing per call
public static class SpawnSpaceTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SPAWN] PASS  " : "[SPAWN] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly MethodInfo SpawnStep = typeof(enmiesOnBoard).GetMethod("spawn", Inst, null, new[] { typeof(float) }, null);
    static readonly MethodInfo Select = typeof(enmiesOnBoard).GetMethod("SelectPhase", Inst);
    static readonly FieldInfo Elapsed = typeof(enmiesOnBoard).GetField("elapsedFlightSeconds", Inst);

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
            Check("the spawner steps headless (spawn(float), SelectPhase, elapsedFlightSeconds)",
                  SpawnStep != null && Select != null && Elapsed != null);
            Footprints();
            MockFuturePatternIsRespected();
            DeferredSpawnLands();
            PickupsKeepOffEnemies();
            ChaserGivesWay();
            PlannerAllocatesNothing();
            OffScreenEdgesFollowTheCamera();
            LongRunsNeverOverlap();
        }
        finally
        {
            SpawnSpace.ClockOverride = null;
            SetWorldManager(null);
            LoopDifficulty.Reset();
            ClearBoard();
        }
        Debug.Log("[SPAWN] failures: " + fails);
        return fails;
    }

    // ---- fixtures ----------------------------------------------------------

    static void SetWorldManager(WorldManager wm)
    {
        typeof(WorldManager).GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, wm);
    }

    static enmiesOnBoard NewBoard()
    {
        var board = new GameObject("~SpawnSpaceBoard").AddComponent<enmiesOnBoard>();
        board.transform.position = new Vector3(0f, 7f, 0f);
        board.SendMessage("Start");
        return board;
    }

    static void ClearBoard()
    {
        foreach (var f in Object.FindObjectsByType<SpawnFootprint>(FindObjectsSortMode.None)) Object.DestroyImmediate(f.gameObject);
        foreach (var r in Object.FindObjectsByType<RailLaneScroller>(FindObjectsSortMode.None)) Object.DestroyImmediate(r.gameObject);
        foreach (var b in Object.FindObjectsByType<enmiesOnBoard>(FindObjectsSortMode.None)) Object.DestroyImmediate(b.gameObject);
        foreach (var e in Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None)) Object.DestroyImmediate(e.gameObject);
    }

    static GameObject Build(EnemyRole role, Vector2 at, int world = 0)
    {
        return EnemyFactory.Create(EnemyRoster.One(world, role), new Vector3(at.x, at.y, 0f), Quaternion.identity);
    }

    // A future movement pattern: holds at its spawn x, then between 0.3s and
    // 1s of flight dashes all the way across the lane to the other wall.
    // Plain class -- patterns needn't be components (SpawnFootprint.Bind).
    sealed class MockDash : IMovementFootprint
    {
        public float toX = 2f;
        public Rect SweptBounds(Vector2 center, Vector2 half, float from, float to)
        {
            float lo = center.x, hi = center.x;
            if (to > .3f) { lo = Mathf.Min(lo, toX); hi = Mathf.Max(hi, toX); }
            return Rect.MinMaxRect(lo - half.x, center.y - half.y, hi + half.x, center.y + half.y);
        }
        public bool SelfSteering => false;
    }

    // Occupies only the first 0.3s of flight (then leaves the board -- a
    // stand-in for a pattern that is somewhere else entirely later on).
    sealed class EarlyOnly : IMovementFootprint
    {
        public Rect SweptBounds(Vector2 center, Vector2 half, float from, float to)
        {
            return from < .3f ? SpawnSpace.BodyRect(center, half) : new Rect(-100f, -100f, .01f, .01f);
        }
        public bool SelfSteering => false;
    }

    // ---- 1: footprints -----------------------------------------------------

    static void Footprints()
    {
        ClearBoard();
        int before = SpawnSpace.EnemyCount;
        bool all = true, bodies = true, plans = true;
        foreach (EnemyRole role in Enum.GetValues(typeof(EnemyRole)))
        {
            var go = Build(role, new Vector2(0f, 40f));
            var f = go.GetComponent<SpawnFootprint>();
            all &= f != null && f.layer == SpawnLayer.Enemy;
            if (f == null) { Object.DestroyImmediate(go); continue; }
            var def = EnemyIdentity.Of(go);
            // body covers both the drawn silhouette and the collider
            bodies &= f.half.x * 2f >= EnemyRoster.TargetWidth(role) - 1e-4f &&
                      f.half.x * 2f >= def.ColliderSize.x - 1e-4f && f.half.y * 2f >= def.ColliderSize.y - 1e-4f;
            bool steering = f.Plan != null && f.Plan.SelfSteering;
            // pilots (fighters, heavies, aliens) hold their place in the world like the chaser
            plans &= f.Plan != null && steering == EnemyRoster.One(0, role).Behaviour.IsPilot;
            Object.DestroyImmediate(go);
        }
        Check("every roster enemy gets a SpawnFootprint on the enemy layer", all);
        Check("every footprint body covers the drawn silhouette and the collider", bodies);
        Check("every footprint is bound to its mover (pilots and chasers self-steer, hazards are board-locked)", plans);
        Check("footprints unregister when destroyed", SpawnSpace.EnemyCount == before);

        // a weaving rock reserves the whole band it weaves across
        var rock = Build(EnemyRole.Rock, new Vector2(0f, 3f));
        rock.GetComponent<moveEnimes>().SetWeave(1.8f);
        var rf = rock.GetComponent<SpawnFootprint>();
        Rect sweep = rf.Sweep(0f, SpawnSpace.Lifetime);
        Check("a weaving rock's sweep spans its weave band [0, 1.8] plus its body",
              sweep.xMin <= -rf.half.x + 1e-4f && sweep.xMax >= 1.8f + rf.half.x - 1e-4f);
        Check("a fighter can't spawn in a weaving rock's band, even where the rock isn't right now",
              !SpawnSpace.Fits(new SpawnCandidate(new Vector2(1.5f, 3f), SpawnSpace.BodyHalf(EnemyRoster.Fighter(0, 1)))));
        Check("... but can beside it", SpawnSpace.Fits(new SpawnCandidate(new Vector2(-1.6f, 3f), SpawnSpace.BodyHalf(EnemyRoster.Fighter(0, 1)))));
        Check("... and above it", SpawnSpace.Fits(new SpawnCandidate(new Vector2(1.5f, 4.2f), SpawnSpace.BodyHalf(EnemyRoster.Fighter(0, 1)))));

        // a held (stunned) enemy stands still in the world: it rises through
        // the board, so the space just above it is reserved too
        var held = Build(EnemyRole.Fighter, new Vector2(-1.5f, 0f));
        held.GetComponent<moveItemEnmInStrightLine>().enabled = false;
        moveBackGround.speed = .3f;
        var hf = held.GetComponent<SpawnFootprint>();
        Check("a stunned enemy reads as held", hf.Held);
        Check("a held enemy reserves the board rising past it", !SpawnSpace.Fits(new SpawnCandidate(new Vector2(-1.5f, 1.5f), hf.half)));
        ClearBoard();
    }

    // ---- 2: a future pattern's declared sweep ------------------------------

    static void MockFuturePatternIsRespected()
    {
        ClearBoard();
        var go = new GameObject("~MockDasher");
        go.transform.position = new Vector3(-2f, 7f, 0f);
        var f = SpawnFootprint.Attach(go, new Vector2(.4f, .4f));
        SpawnFootprint.Bind(go, new MockDash { toX = 2f });

        var half = new Vector2(.35f, .35f);
        Check("a straight scroller can't spawn across the dasher's future dash lane",
              !SpawnSpace.Fits(new SpawnCandidate(new Vector2(1.2f, 7f), half)));
        Check("a pattern that's gone before the dash starts may share the row (time windows)",
              SpawnSpace.Fits(new SpawnCandidate(new Vector2(1.2f, 7f), half, new EarlyOnly())));
        Check("the dasher's row is free just above", SpawnSpace.Fits(new SpawnCandidate(new Vector2(1.2f, 8.3f), half)));

        // the real spawner keeps every spawn out of the dash, across a burst
        // of every slot fired with the board standing still
        SetWorldManager(new GameObject("~SpawnSpaceWorlds").AddComponent<WorldManager>());
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, 0);
        Random.InitState(4242);
        var board = NewBoard();
        typeof(enmiesOnBoard).GetField("astroidSelector", Inst).SetValue(board, 4);
        string[] slots = { "spawnAstroid1", "spawnAstroid2", "spawnSmallAstroid", "spawnMidAstroid", "spawnLargeAstroid",
                           "spawnAnimatedEnimeOne", "spawnExtraEnemy", "spawnMine" };
        foreach (string slot in slots)
            typeof(enmiesOnBoard).GetMethod(slot, Inst).Invoke(board, null);
        Rect dash = f.Sweep(0f, SpawnSpace.Lifetime);
        int inside = 0, spawned = 0;
        foreach (var other in SpawnSpace.Live(SpawnLayer.Enemy))
        {
            if (other == f) continue;
            spawned++;
            if (other.Sweep(0f, SpawnSpace.Lifetime).Overlaps(dash)) inside++;
        }
        Check("the spawner placed enemies around a mock dashing pattern (" + spawned + ") and none in its dash lane (" + inside + ")",
              spawned > 0 && inside == 0);
        SetWorldManager(null);
        foreach (var w in Object.FindObjectsByType<WorldManager>(FindObjectsSortMode.None)) Object.DestroyImmediate(w.gameObject);
        ClearBoard();
    }

    // ---- 3: deferral -------------------------------------------------------

    static void DeferredSpawnLands()
    {
        ClearBoard();
        // a wall across the whole spawn line (and the lifts above it)
        var wall = new GameObject("~Wall");
        wall.transform.position = new Vector3(0f, 8f, 0f);
        SpawnFootprint.Attach(wall, new Vector2(3f, 2.5f));

        var board = NewBoard();
        typeof(enmiesOnBoard).GetMethod("spawnAstroid2", Inst).Invoke(board, null);   // a rock (the heavy is a pilot now)
        Check("a rock with no room is deferred, not dropped on the wall",
              board.SpawnedCount == 0 && board.PendingCount == 1 && board.DeferredTotal == 1);
        moveBackGround.speed = .3f;
        SpawnStep.Invoke(board, new object[] { .05f });
        Check("it keeps waiting while there's no room", board.SpawnedCount == 0 && board.PendingCount == 1);

        wall.transform.position = new Vector3(0f, -20f, 0f);   // the board scrolled on
        SpawnStep.Invoke(board, new object[] { .05f });
        Check("the deferred spawn lands as soon as there's room", board.SpawnedCount >= 1 && board.PendingCount == 0);

        wall.transform.position = new Vector3(0f, 8f, 0f);
        typeof(enmiesOnBoard).GetMethod("spawnAstroid2", Inst).Invoke(board, null);   // a rock (the heavy is a pilot now)
        int spawned = board.SpawnedCount;
        for (int i = 0; i < 30; i++) SpawnStep.Invoke(board, new object[] { .05f });
        Check("a spawn that never finds room is let go after MaxDeferSeconds",
              board.PendingCount == 0 && board.DroppedTotal >= 1 && board.SpawnedCount == spawned);
        ClearBoard();
    }

    // ---- 4: pickups ----------------------------------------------------------

    static void PickupsKeepOffEnemies()
    {
        ClearBoard();
        var heavy = Build(EnemyRole.Big, new Vector2(0f, 7f));
        var hf = heavy.GetComponent<SpawnFootprint>();
        var half = new Vector2(.2f, .2f);
        Random.InitState(77);
        int clear = 0;
        for (int i = 0; i < 40; i++)
        {
            Vector3 p = SpawnSpace.PickupSpot(new Vector3(0f, 7f, 0f), half, -2.2f, 2.2f);
            if (!SpawnSpace.BodyRect(p, half).Overlaps(hf.Body)) clear++;
        }
        Check("a pickup asked for a spot on top of a heavy lands clear of it (" + clear + "/40)", clear == 40);

        // the real spawner's stars register as pickups, clear of the heavy
        var goods = new GameObject("~Goods").AddComponent<spawnGoodStuff>();
        goods.transform.position = new Vector3(0f, 7f, 0f);
        var template = new GameObject("~starTemplate");
        template.AddComponent<BoxCollider2D>().size = new Vector2(.3f, .3f);
        goods.smStar = template;
        for (int i = 0; i < 3; i++)
            typeof(spawnGoodStuff).GetMethod("spawnSmStar", Inst).Invoke(goods, new object[] { 0, new Vector3(0f, 7f, 0f) });
        int stars = 0, onHeavy = 0;
        foreach (var p in SpawnSpace.Live(SpawnLayer.Pickup))
        {
            stars++;
            if (p.Body.Overlaps(hf.Body)) onHeavy++;
        }
        Check("spawnGoodStuff's stars register as pickups (" + stars + ") and none lands on the heavy (" + onHeavy + ")",
              stars >= 3 && onHeavy == 0);

        // and an enemy prefers a spot clear of the pickups
        var board = NewBoard();
        Random.InitState(78);
        typeof(enmiesOnBoard).GetMethod("spawnAstroid2", Inst).Invoke(board, null);   // a rock (the heavy is a pilot now)
        int enemyOnPickup = 0;
        foreach (var e in SpawnSpace.Live(SpawnLayer.Enemy))
            foreach (var p in SpawnSpace.Live(SpawnLayer.Pickup))
                if (e.Body.Overlaps(p.Body)) enemyOnPickup++;
        Check("enemy spawns keep off the live pickups when there's room (" + board.SpawnedCount + " spawned, " + enemyOnPickup + " overlaps)",
              board.SpawnedCount > 0 && enemyOnPickup == 0);
        Object.DestroyImmediate(goods.gameObject);
        Object.DestroyImmediate(template);
        ClearBoard();
    }

    // ---- 5: a chaser gives way ---------------------------------------------

    static void ChaserGivesWay()
    {
        ClearBoard();
        moveBackGround.speed = .4f;   // 12 u/s
        var target = new GameObject("~Ship").transform;
        target.position = new Vector3(0f, 6f, 0f);   // straight up through the rocks
        var chaser = Build(EnemyRole.Chaser, new Vector2(0f, -4f)).GetComponent<ChaserEnemy>();
        chaser.Target = target;
        // a column of fighters coming straight down at it
        var column = new List<moveItemEnmInStrightLine>();
        for (int i = 0; i < 6; i++)
            column.Add(Build(EnemyRole.Fighter, new Vector2(0f, 2f + i * 1.2f)).GetComponent<moveItemEnmInStrightLine>());
        SpawnFootprint a, b;
        int overlapping = 0;
        const float dt = 1f / 60f;
        for (int frame = 0; frame < 120; frame++)
        {
            foreach (var m in column) if (m != null) m.Step(dt);
            chaser.Step(dt);
            if (SpawnSpace.AnyBodiesOverlap(out a, out b)) overlapping++;
        }
        Check("a chaser hunting straight up a column of fighters never touches one (" + overlapping + " frames)", overlapping == 0);
        Check("... it sidestepped the column", Mathf.Abs(chaser.transform.position.x) > .5f);
        Object.DestroyImmediate(target.gameObject);
        ClearBoard();
    }

    // ---- 6: allocations --------------------------------------------------------

    static void PlannerAllocatesNothing()
    {
        ClearBoard();
        moveBackGround.speed = .3f;
        Random.InitState(9);
        for (int i = 0; i < 10; i++) Build(EnemyRole.Rock, new Vector2(Random.Range(-2f, 2f), i * .9f - 4f)).GetComponent<moveEnimes>().SetWeave(Random.Range(-1f, 2f));
        for (int i = 0; i < 10; i++) Build(EnemyRole.Fighter, new Vector2(Random.Range(-2f, 2f), i * .9f - 4f));
        var chaser = Build(EnemyRole.Chaser, new Vector2(0f, -6f));
        var cf = chaser.GetComponent<SpawnFootprint>();
        var weave = new WeavePlan { amplitude = 1.2f };
        var half = new Vector2(.4f, .4f);
        var rockDef = EnemyRoster.One(0, EnemyRole.Rock);
        bool sink = false;
        Rect threat;
        for (int warm = 0; warm < 2; warm++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++)
            {
                float x = -2f + i * .02f;
                sink ^= SpawnSpace.Fits(new SpawnCandidate(new Vector2(x, 5f), half));
                sink ^= SpawnSpace.Fits(new SpawnCandidate(new Vector2(x, 5f), half, weave));
                sink ^= SpawnSpace.Fits(new SpawnCandidate(new Vector2(x, 5f), half), SpawnLayer.Pickup);
                sink ^= SpawnSpace.ClearForSteerer(cf, new Vector2(x, -6f), cf.half);
                sink ^= SpawnSpace.ThreatAhead(cf, new Vector2(x, -6f), cf.half, 4f, out threat);
                SpawnSpace.ResolveSteer(cf, new Vector2(x, -6f), new Vector2(x + .01f, -5.9f), .2f, .07f);
                sink ^= SpawnLane.Fits(rockDef, x, 5f);
            }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            if (warm == 1)
                Check("the planner allocates nothing per call (Fits, steering, lane guard: " + bytes + " bytes over 1400 calls)", bytes == 0);
        }
        Debug.Log("[SPAWN] (sink " + sink + ")");
        ClearBoard();
    }

    // ---- 7: the board's off-screen edges --------------------------------------

    // The heal atom spawns just above the visible top, and the bottom
    // Destroyer sits below the visible bottom, on every screen height; a
    // chaser spawned under the view starts clear of the Destroyer.
    static void OffScreenEdgesFollowTheCamera()
    {
        var cam = Camera.main;
        var destroyer = SceneUtil.FindAny("Destroyer");
        Check("gameS1 has the main camera and the Destroyer", cam != null && destroyer != null);
        if (cam == null || destroyer == null) return;
        float size0 = cam.orthographicSize;
        Vector3 pos0 = destroyer.transform.position;
        var keeper = destroyer.AddComponent<BelowCameraDestroyer>();
        var box = destroyer.GetComponent<BoxCollider2D>();
        bool below = true, chaserClear = true;
        ClearBoard();
        var board = NewBoard();
        foreach (float size in new[] { 5f, 6.65f, 7.6f })
        {
            cam.orthographicSize = size;
            keeper.Reposition();
            float top = destroyer.transform.position.y + box.offset.y + box.size.y * .5f;
            below &= top < CameraFit.ViewBottom - 1f;
            Random.InitState(31);
            for (int i = 0; i < 12; i++) typeof(enmiesOnBoard).GetMethod("spawnChaser", Inst).Invoke(board, null);
            foreach (var f in SpawnSpace.Live(SpawnLayer.Enemy))
                chaserClear &= f.Body.yMin > top && f.Body.yMax < CameraFit.ViewBottom;
            ClearBoard();
            board = NewBoard();
        }
        Check("the Destroyer stays below the visible bottom on 5 / 6.65 / 7.6 half-height views", below);
        Check("chasers spawn under the view but clear of the Destroyer", chaserClear);
        Check("the heal atom spawns just above the visible top (CameraFit.ViewTop), not a fixed y 7",
              System.IO.File.ReadAllText("Assets/Scripts/Gameplay/HealAtomSpawner.cs").Contains("CameraFit.ViewTop + SpawnAboveTop"));
        Object.DestroyImmediate(keeper);
        destroyer.transform.position = pos0;
        cam.orthographicSize = size0;
        ClearBoard();
    }

    // ---- 8: long runs ------------------------------------------------------------

    // Spawn counts of the pre-SpawnSpace spawner (3526b769) over the same
    // runs -- 120s, dt 1/60, worlds 0-3 x scroll 6/12/18 u/s, seeded the
    // same way -- with the same movement emulated (weave, scroll, rails).
    // Summed per density level.
    const int BaselineFirstPass = 9420;   // density x1
    const int BaselineMaxLoop = 19219;    // max loop density
    const float BaselineChaosSeconds = 130f;

    static void LongRunsNeverOverlap()
    {
        ClearBoard();
        var wmGo = new GameObject("~SpawnSpaceRunWorlds");
        SetWorldManager(wmGo.AddComponent<WorldManager>());
        float maxDensity = LoopRules.Density(LoopRules.MaxScaledLoops, 1e6f);
        float[] speeds = { 6f, 12f, 18f };
        float[] densities = { 1f, maxDensity };
        const float dt = 1f / 60f, runSeconds = 120f;
        int[] spawnedAt = new int[2];
        int totalOverlapFrames = 0, frames = 0, deferred = 0, dropped = 0, chasersSeen = 0, brainsMoved = 0;
        string firstOverlap = null;
        var target = new GameObject("~SimShip").transform;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var buffer = new List<SpawnFootprint>(128);
        try
        {
            for (int w = 0; w < WorldManager.Worlds.Length; w++)
                for (int si = 0; si < speeds.Length; si++)
                    for (int di = 0; di < densities.Length; di++)
                    {
                        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, w);
                        LoopDifficulty.DensityScale = densities[di];
                        Random.InitState(777 + w * 31 + si * 7 + di);
                        float v = speeds[si];
                        moveBackGround.speed = v / 30f;
                        var board = NewBoard();
                        // the baseline was counted on the old phase schedule
                        // (Chaos at 130s, so never in a 120s run); Chaos now
                        // comes at enmiesOnBoard.ChaosStartSeconds, a
                        // deliberate difficulty change, not a planner one
                        board.phases[board.phases.Length - 1].activeAfterSeconds = BaselineChaosSeconds;
                        var chasers = new HashSet<ChaserEnemy>();
                        int overlapFrames = 0;
                        for (float t = 0f; t < runSeconds; t += dt)
                        {
                            frames++;
                            SpawnSpace.ClockOverride = t;
                            target.position = new Vector3(Mathf.Sin(t * .7f) * 1.8f, -4f, 0f);
                            Elapsed.SetValue(board, t);
                            Select.Invoke(board, null);
                            SpawnStep.Invoke(board, new object[] { dt });

                            // Update: the board-locked movers and the rails
                            buffer.Clear();
                            buffer.AddRange(SpawnSpace.Live(SpawnLayer.Enemy));
                            foreach (var f in buffer)
                            {
                                moveEnimes weave;
                                moveItemEnmInStrightLine straight;
                                if (f.TryGetComponent(out weave) && weave.enabled) weave.Step(dt, t);
                                else if (f.TryGetComponent(out straight) && straight.enabled) straight.Step(dt);
                            }
                            foreach (var r in Object.FindObjectsByType<RailLaneScroller>(FindObjectsSortMode.None))
                            {
                                r.transform.position += Vector3.down * v * dt;
                                if (r.transform.position.y < -12f) Object.DestroyImmediate(r.gameObject);   // RailLaneScroller
                            }
                            // LateUpdate: the brains run their patterns (EnemyBrain), mines
                            // settle on their rails, then the chasers steer
                            foreach (var f in buffer)
                            {
                                EnemyBrain brain;
                                if (!f.TryGetComponent(out brain) || !brain.enabled) continue;
                                brain.TargetOverride = target;
                                brain.Step(dt);
                                if (brain.Behaviour != null && brain.Behaviour.Moves) brainsMoved++;
                            }
                            foreach (var f in buffer)
                            {
                                RailMineMount mount;
                                if (!f.TryGetComponent(out mount)) continue;
                                // its rail left the board: the mount takes the mine with it
                                if (mount.rail == null) Object.DestroyImmediate(f.gameObject);
                                else mount.SendMessage("LateUpdate");
                            }
                            buffer.Clear();
                            buffer.AddRange(SpawnSpace.Live(SpawnLayer.Enemy));
                            foreach (var f in buffer)
                            {
                                ChaserEnemy c;
                                if (!f.TryGetComponent(out c)) continue;
                                if (chasers.Add(c)) { c.Target = target; chasersSeen++; }
                                c.Step(dt);
                            }
                            // off the board (the Destroyer below, the chaser's safety net above)
                            foreach (var f in buffer)
                            {
                                float y = f.transform.position.y;
                                if (y < -14f || y > 20f) Object.DestroyImmediate(f.gameObject);
                            }

                            SpawnFootprint a, b;
                            if (SpawnSpace.AnyBodiesOverlap(out a, out b))
                            {
                                overlapFrames++;
                                if (firstOverlap == null)
                                    firstOverlap = string.Format("world {0} scroll {1} density {2:F2} t={3:F2}: {4} at {5} and {6} at {7}",
                                        w, v, densities[di], t, a.name, (Vector2)a.transform.position, b.name, (Vector2)b.transform.position);
                            }
                        }
                        spawnedAt[di] += board.SpawnedCount;
                        deferred += board.DeferredTotal;
                        dropped += board.DroppedTotal;
                        totalOverlapFrames += overlapFrames;
                        Debug.Log(string.Format("[SPAWN] world {0} scroll {1} density {2:F2}: spawned {3}, deferred {4}, dropped {5}, overlap frames {6}",
                                                w, v, densities[di], board.SpawnedCount, board.DeferredTotal, board.DroppedTotal, overlapFrames));
                        ClearBoard();
                    }
        }
        finally
        {
            Object.DestroyImmediate(target.gameObject);
            SetWorldManager(null);
            Object.DestroyImmediate(wmGo);
            LoopDifficulty.Reset();
            SpawnSpace.ClockOverride = null;
        }
        Debug.Log("[SPAWN] long runs took " + sw.Elapsed.TotalSeconds.ToString("F1") + "s over " + frames + " frames");
        Check("no two live enemy bodies ever overlap, every frame of 24 two-minute runs (" + totalOverlapFrames + " frames" +
              (firstOverlap != null ? "; first: " + firstOverlap : "") + ")", totalOverlapFrames == 0);
        Check("chasers flew in the runs (" + chasersSeen + "; they are capped now, EnemyDensity.MaxChasers)", chasersSeen > 40);
        Check("spawns with no room were deferred (" + deferred + ")", deferred > 0);
        // (hazards are routed round the pilots' columns now: one that finds
        // no free column in a second is let go rather than squeezed in)
        Check(string.Format("most deferred spawns landed ({0} of {1} let go, <= 40%)", dropped, deferred), dropped <= deferred * .4f);
        Check("the enemies ran their own patterns in the runs (" + brainsMoved + " brain steps)", brainsMoved > 10000);
        // 2026-10: the spawner fields fewer, smarter enemies on purpose
        // (EnemyDensity; EnemyDensityTest holds the cut itself), and each one
        // reserves its whole pattern, so the counts are no longer the
        // pre-SpawnSpace spawner's: well below it, and never above.
        float r0 = spawnedAt[0] / (float)BaselineFirstPass, r1 = spawnedAt[1] / (float)BaselineMaxLoop;
        Check(string.Format("first-pass density is the deliberate cut, not a planner loss: {0} spawns vs {1} before ({2:P1})",
                            spawnedAt[0], BaselineFirstPass, r0 - 1f), r0 >= .2f && r0 <= .8f);
        Check(string.Format("max loop density likewise: {0} spawns vs {1} before ({2:P1})", spawnedAt[1], BaselineMaxLoop, r1 - 1f),
              r1 >= .12f && r1 <= .8f);
    }
}
