using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// The portal that stays open (docs/speed-and-loops.md, section 3).
//
//   1  the escalation curve: monotonic, unbounded in lethality, bodies held
//      under the body cap (60 fps); its clock is flight time only
//   2  every level's portal waits for ever: ten minutes of flight and it is
//      still open, in view, in the ship's reach, nothing else moving on;
//      entering works at any moment (arriving, in the grace, deep in the
//      pressure)
//   3  the final world's portal is the same flow and loops back
//   4  anti-farm: past the grace nothing earns until the portal is flown
//   5  the approach stays clear (pilots, elites)
//   6  the board under pressure: the real spawner, more bodies and shots,
//      never past the ceiling
//   7  what the pilot sees and hears: the chip, the glow (never the
//      player's red), the beats
//   8  no per-frame allocation
public static class OpenPortalTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[PORTAL] PASS  " : "[PORTAL] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
    const BindingFlags Stat = BindingFlags.NonPublic | BindingFlags.Static;
    const float Dt = 1f / 30f;
    static int frame;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            Curve();
            FlightTimeOnly();
            WaitsForEver();
            EnterAnyTime();
            FinalPortalLoopsBack();
            NoDeadBranches();
            AntiFarm();
            ApproachStaysClear();
            Hud();
            NoAllocations();
            if (TestHarness.Slow("the spawner under portal pressure (headless runs)")) BoardUnderPressure();
        }
        finally
        {
            PortalPressure.Reset();
            ShipStartSpeed.EquippedHudOverride = null;
            SpeedRamp.FrameOverride = null;
            SpeedRamp.DeltaOverride = null;
            SpeedRamp.ResetFrameGuard();
            SpeedRamp.ResetBoost();
            ResumeSlowMo.ClockOverride = null;
            ResumeSlowMo.ResetRun();
            BossEncounter.ResetRun();
            RunLoop.Reset();
            EnemyThreat.ForceShooting = false;
            EnemyDensityProbe.RestoreView();
            EnemyDensityProbe.Clear();
            SpawnSpace.ClockOverride = null;
            if (PortalPressureHud.Instance != null) Object.DestroyImmediate(PortalPressureHud.Instance.gameObject);
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
        }
        Debug.Log("[PORTAL] failures: " + fails);
        return fails;
    }

    // ---- fixtures -------------------------------------------------------------

    static void FreshScene(int world)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        RunLoop.Reset();
        PortalPressure.Reset();
        // This suite is the portal's: Space's planetfall (PlanetfallTest)
        // stands aside so world 0 opens a portal as every world once did.
        PlanetfallCatalog.Enabled = false;
        // ... and Frost's, Verdant's and Ember's lift-offs (LiftoffTest): their portals open at once
        // (Ember's planetfall after Verdant stands aside with the rest).
        LiftoffCatalog.Enabled = false;
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        startMenu.youAreInTutorial = false;
        score.pauseCounter = 0;
        Time.timeScale = 1f;
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        ShipStartSpeed.EquippedHudOverride = () => ShipStartSpeed.StockHud;
        frame = 70000;
        SpeedRamp.FrameOverride = () => frame;
        SpeedRamp.DeltaOverride = () => Dt;
        SpeedRamp.ResetFrameGuard();
        SpeedRamp.ResetBoost();
        ResumeSlowMo.ClockOverride = () => frame * Dt;
        ResumeSlowMo.ResetRun();
    }

    static WorldManager World()
    {
        var wm = new GameObject("~WorldManager").AddComponent<WorldManager>();
        wm.SendMessage("Awake");
        typeof(WorldManager).GetField("distanceLeft", Inst).SetValue(wm, wm.WorldDistance);
        typeof(WorldManager).GetField("levelBegun", Inst).SetValue(wm, true);
        Apply();
        return wm;
    }

    static void Apply()
    {
        typeof(WorldManager).GetMethod("ApplyDifficulty", Stat).Invoke(null, new object[] { WorldManager.Current });
    }

    static moveBackGround[] Walls()
    {
        var walls = new moveBackGround[2];
        for (int i = 0; i < 2; i++)
        {
            walls[i] = new GameObject("~wall" + i).AddComponent<moveBackGround>();
            walls[i].speedRampPerSecond = WorldManager.Current.speedRampPerSecond;
            walls[i].maxSpeed = SpeedRamp.Cap;
        }
        return walls;
    }

    // The level is flown and this world's boss is dealt with: the real
    // EndLevel path from there opens the portal.
    static void FinishLevel(WorldManager wm)
    {
        typeof(BossEncounter).GetField("doneWorld", Stat).SetValue(null, WorldManager.CurrentIndex);
        typeof(WorldManager).GetField("distanceLeft", Inst).SetValue(wm, 0f);
        wm.EndLevel();
    }

    // One flying frame: the walls, the level / portal clock, the portal.
    static void Fly(WorldManager wm, moveBackGround[] walls)
    {
        frame++;
        if (walls != null) foreach (var w in walls) TestHarness.Send(w, "Update");
        if (WorldManager.Flying) wm.Tick(Dt);
        var p = Portal.Live;
        if (p != null && WorldManager.Flying) p.Step(Dt);
    }

    static void Enter()
    {
        var p = Portal.Live;
        try { p.Enter(); }
        catch (System.Exception e) { Debug.Log("[PORTAL] Advance side effect threw (ignored): " + e.Message); }
    }

    // ---- 1. the curve ------------------------------------------------------------

    static readonly float[] Moments = { 0f, 8f, 18f, 30f, 60f, 90f, 120f, 180f, 240f, 600f };

    static void Curve()
    {
        Check("tunables: grace 8 s, a Level every 10 s, overdrive past Level 6, body cap 24 (36 absolute), shots 30",
              PortalPressure.GraceSeconds == 8f && PortalPressure.LevelSeconds == 10f && PortalPressure.OverdriveLevel == 6f &&
              PortalPressure.BodyCap == 24f && PortalPressure.BodyCapAbsolute == 36f && PortalPressure.ShotCap == 30);

        Debug.Log("[PORTAL] TABLE seconds waiting | Level | spawn rate | threat ceiling (10 u view) | pilot load + | chasers + | shots alive | volley gap s | shot speed | chaser speed | row gap | lethality");
        float baseCeiling = Mathf.Lerp(EnemyDensity.ThreatsAtLowSpeed, EnemyDensity.ThreatsAtHighSpeed, 1f);
        foreach (float s in Moments)
            Debug.Log(string.Format("[PORTAL] TABLE {0,4:F0} s | {1,5:F1} | x{2:F2} | {3,5:F1} | +{4:F1} | +{5} | {6} | {7:F2} | x{8:F2} | x{9:F2} | x{10:F2} | {11:F1}",
                                    s, PortalPressure.LevelAt(s), PortalPressure.DensityAt(s), PortalPressure.CeilingAt(s, baseCeiling, 1f),
                                    PortalPressure.PilotLoadBonusAt(s), PortalPressure.ChaserBonusAt(s),
                                    Mathf.Min(Mathf.Max(EnemyThreat.MaxEnemyShots, PortalPressure.ShotCap), EnemyThreat.MaxEnemyShots + PortalPressure.ShotBonusAt(s)),
                                    EnemyThreat.VolleyGap * PortalPressure.VolleyGapScaleAt(s), PortalPressure.ShotSpeedScaleAt(s),
                                    PortalPressure.ChaserSpeedScaleAt(s), PortalPressure.ShipGapScaleAt(s), PortalPressure.LethalityAt(s)));

        bool monotone = true, strictly = true, bounded = true;
        float prevLevel = -1f, prevLethal = -1f, prevCeiling = -1f, prevDensity = -1f;
        for (float s = 0f; s <= 3600f; s += .5f)
        {
            float level = PortalPressure.LevelAt(s), lethal = PortalPressure.LethalityAt(s);
            float ceiling = PortalPressure.CeilingAt(s, baseCeiling, 1f), density = PortalPressure.DensityAt(s);
            monotone &= level >= prevLevel && lethal >= prevLethal - 1e-4f && ceiling >= prevCeiling && density >= prevDensity &&
                        PortalPressure.ShipGapScaleAt(s) <= PortalPressure.ShipGapScaleAt(Mathf.Max(0f, s - .5f));
            if (s > PortalPressure.GraceSeconds) strictly &= lethal > prevLethal;
            foreach (float view in new[] { 1f, 1.32f, 1.74f, 3f })
                bounded &= PortalPressure.CeilingAt(s, baseCeiling, view) <= Mathf.Max(PortalPressure.BodyCapAbsolute, baseCeiling * view) + 1e-4f &&
                           PortalPressure.CeilingAt(s, baseCeiling, view) <= PortalPressure.BodyCap * view + 1e-4f;
            bounded &= PortalPressure.PilotLoadBonusAt(s) <= PortalPressure.PilotLoadBonusCap &&
                       PortalPressure.ChaserBonusAt(s) <= PortalPressure.ChaserBonusCap &&
                       EnemyThreat.MaxEnemyShots + LoopRules.ShotBonus(9) + PortalPressure.ShotBonusAt(s) >= 0;
            prevLevel = level; prevLethal = lethal; prevCeiling = ceiling; prevDensity = density;
        }
        Check("every dial is monotonic in the time waited (an hour, every half second)", monotone);
        Check("lethality strictly rises the whole time after the grace", strictly);
        Check("lethality is unbounded: an hour in is over 50x a minute in (" +
              (PortalPressure.LethalityAt(3600f) / PortalPressure.LethalityAt(60f)).ToString("F0") + "x)",
              PortalPressure.LethalityAt(3600f) > 50f * PortalPressure.LethalityAt(60f));
        Check("bodies on screen are bounded: the threat ceiling stops at the body cap in every view, pilot load and chasers at theirs", bounded);
        Check("the ceiling is reached in the second minute, then overdrive takes over (" +
              PortalPressure.CeilingAt(100f, baseCeiling, 1f).ToString("F1") + " at 100 s; overdrive from " +
              (PortalPressure.GraceSeconds + PortalPressure.OverdriveLevel * PortalPressure.LevelSeconds) + " s)",
              Mathf.Approximately(PortalPressure.CeilingAt(150f, baseCeiling, 1f), PortalPressure.BodyCap) &&
              PortalPressure.OverdriveAt(68f) == 0f && PortalPressure.OverdriveAt(70f) > 0f);
        Check("deep in overdrive the row guard's ship-wide gap closes completely (" +
              PortalPressure.ShipGapScaleAt(200f).ToString("F2") + " at 200 s)", PortalPressure.ShipGapScaleAt(200f) == 0f &&
              PortalPressure.ShipGapScaleAt(60f) == 1f);
        Check("the grace is a breath: thinner than a normal board (x" + PortalPressure.GraceDensity + "), the first Level gentle (x" +
              PortalPressure.DensityAt(18f).ToString("F2") + " at 18 s)",
              PortalPressure.DensityAt(4f) < 1f && PortalPressure.DensityAt(18f) < 1.5f);

        // the live budget the shooters read is capped too
        PortalPressure.Open(1);
        PortalPressure.Tick(3600f);
        int budget = EnemyThreat.ShotBudget;
        Check("the live shot budget an hour in is the shot cap (" + budget + ")", budget == PortalPressure.ShotCap);
        Check("the live volley gap keeps shrinking (" + EnemyThreat.Gap.ToString("F4") + " s an hour in)",
              EnemyThreat.Gap < EnemyThreat.VolleyGap * .01f && EnemyThreat.Gap > 0f);
        Check("the lane guard's gap is gone an hour in", SpawnLane.GuaranteedGap == 0f);
        PortalPressure.Reset();
        Check("no portal: every dial neutral", PortalPressure.DensityScale == 1f && PortalPressure.ThreatBonus == 0f &&
              PortalPressure.ShotBonus == 0 && PortalPressure.VolleyGapScale == 1f && PortalPressure.ShotSpeedScale == 1f &&
              PortalPressure.ChaserSpeedScale == 1f && PortalPressure.ShipGapScale == 1f && SpawnLane.GuaranteedGap == SpawnLane.ShipGap &&
              EnemyThreat.ShotBudget == EnemyThreat.MaxEnemyShots + LoopRules.ShotBonus(RunLoop.Index));
    }

    // ---- the clock: flight time only ------------------------------------------------

    static void FlightTimeOnly()
    {
        FreshScene(0);
        var wm = World();
        FinishLevel(wm);
        PortalPressure.Tick(0f);
        Check("a frozen frame (dt 0) adds nothing to the wait", PortalPressure.Seconds == 0f);
        score.pauseCounter = 3;   // no touch in batch mode: paused
        Check("paused: not flying, so WorldManager.Update and Portal.Update feed nothing", !WorldManager.Flying);
        for (int i = 0; i < 300; i++) { frame++; if (WorldManager.Flying) wm.Tick(Dt); }
        Check("ten paused seconds: the wait has not moved", PortalPressure.Seconds == 0f);
        score.pauseCounter = 0;
        buttonClicks.playerDied = true;
        Check("dead: not flying", !WorldManager.Flying);
        buttonClicks.playerDied = false;
        for (int i = 0; i < 300; i++) { frame++; if (WorldManager.Flying) wm.Tick(Dt); }
        Check("ten flying seconds: ten seconds waited (" + PortalPressure.Seconds.ToString("F2") + ")", Mathf.Abs(PortalPressure.Seconds - 10f) < .01f);
        Check("the source: WorldManager.Update ticks only when Flying, Portal.Update steps only when Flying",
              Source("Worlds/WorldManager.cs").Contains("if (Flying) Tick(Time.deltaTime);") &&
              Source("Worlds/Portal.cs").Contains("if (WorldManager.Flying) Step(Time.deltaTime);"));
        Object.DestroyImmediate(wm.gameObject);
    }

    static string Source(string rel) { return File.ReadAllText("Assets/Scripts/" + rel); }

    // ---- 2. waits for ever --------------------------------------------------------------

    static void WaitsForEver()
    {
        FreshScene(0);
        var wm = World();
        var walls = Walls();
        moveBackGround.speed = BossConfig.FightSpeed;   // where a boss leaves it
        FinishLevel(wm);
        Check("the level flown and the boss done: the portal opens (stage Portal, pressure on, to Frost)",
              wm.Stage == WorldManager.LevelStage.Portal && wm.PortalIsOpen && PortalPressure.Active && PortalPressure.Destination == 1 &&
              Portal.Live != null);
        var portal = Portal.Live;
        float fall = (Portal.SpawnY - Portal.StationY) / Portal.FallSpeed;
        bool open = true, inView = true, inReach = true, still = true, noWarning = true, capped = true;
        float stationAt = -1f, minX = 99f, maxX = -99f;
        float t = 0f;
        for (int i = 0; i < (int)(600f / Dt); i++)
        {
            Fly(wm, walls);
            t += Dt;
            open &= Portal.Live == portal && portal != null && wm.Stage == WorldManager.LevelStage.Portal;
            if (portal == null) break;
            if (stationAt < 0f && portal.OnStation) stationAt = t;
            Vector3 at = portal.transform.position;
            if (portal.OnStation)
            {
                inView &= at.y - Portal.Radius >= CameraFit.ViewBottom && at.y + Portal.Radius <= CameraFit.ViewTop;
                inReach &= Mathf.Abs(at.x) <= ShipReach.HalfWidth && at.y >= ShipReach.Bottom && at.y <= ShipReach.Top;   // (was y -4.15..4.5)
                minX = Mathf.Min(minX, at.x);
                maxX = Mathf.Max(maxX, at.x);
            }
            still &= wm.DistanceLeft == 0f && wm.LevelClockSeconds == WorldManager.BaselineWorldSeconds && WorldManager.CurrentIndex == 0;
            noWarning &= BossWarning.Read(wm) == BossWarningInput.None;
            capped &= moveBackGround.speed <= SpeedRamp.Cap + 1e-6f;
        }
        Check("ten minutes of flight: the same portal is still open, stage Portal the whole time", open);
        Check("it comes down to its station in " + stationAt.ToString("F1") + " s (" + fall.ToString("F1") + " s of fall) and holds there",
              stationAt > 0f && Mathf.Abs(stationAt - fall) < .2f);
        Check("on station it never leaves the view", inView);
        Check("... and stays where the ship can fly (x " + minX.ToString("F2") + " to " + maxX.ToString("F2") + ", the ship reaches +/-2.4)", inReach);
        Check("... drifting side to side, never parked in the middle", maxX - minX > Portal.DriftHalf * 1.5f && (minX > 0f || maxX < 0f));
        Check("the level clock is stopped and the world unchanged while it waits", still);
        Check("no boss warning while a portal waits", noWarning);
        Check("the speed cap holds through the wait (natural climb from the boss's 20 to 35, never past)",
              capped && Mathf.Approximately(moveBackGround.speed, SpeedRamp.Cap));
        Check("the wait is ten minutes on the pressure's clock (Level " + PortalPressure.Level.ToString("F1") + ", DANGER " + PortalPressure.DangerNumber + ")",
              Mathf.Abs(PortalPressure.Seconds - 600f) < .5f && PortalPressure.DangerNumber == 1 + Mathf.FloorToInt(PortalPressure.Level));

        Enter();
        Check("flying in, after ten minutes: Frost, a fresh level, the pressure over",
              WorldManager.CurrentIndex == 1 && wm.Stage == WorldManager.LevelStage.Level && !wm.PortalIsOpen && !PortalPressure.Active &&
              Portal.Live == null && Mathf.Approximately(wm.DistanceLeft, WorldManager.WorldDistanceFor(1)));
        Check("... every dial neutral again", PortalPressure.DensityScale == 1f && PortalPressure.ShipGapScale == 1f && PortalPressure.ShotBonus == 0);
        Check("... a boss ahead again", BossWarning.Read(wm) == BossWarningInput.Ahead);
        Check("... at the arrival speed, under the cap", Mathf.Approximately(SpeedRamp.Natural, WorldManager.ArrivalSpeed(0)));
        foreach (var w in walls) Object.DestroyImmediate(w.gameObject);
        Object.DestroyImmediate(wm.gameObject);
    }

    static void EnterAnyTime()
    {
        bool all = true;
        var lines = new List<string>();
        foreach (float wait in new[] { .2f, 1f, 5f, 8.5f, 30f, 120f, 900f })
            for (int world = 0; world < WorldManager.Worlds.Length - 1; world++)
            {
                FreshScene(world);
                var wm = World();
                FinishLevel(wm);
                for (int i = 0; i < (int)(wait / Dt); i++) Fly(wm, null);
                bool ok = Portal.Live != null;
                Enter();
                ok &= WorldManager.CurrentIndex == world + 1 && wm.Stage == WorldManager.LevelStage.Level && !PortalPressure.Active;
                if (!ok) lines.Add(WorldManager.Worlds[world].displayName + " after " + wait + " s");
                all &= ok;
                Object.DestroyImmediate(wm.gameObject);
            }
        Check("entering always works: every world's portal, after 0.2 s (still arriving), 1, 5, 8.5 (just past the grace), 30, 120 and 900 s" +
              (lines.Count > 0 ? " (failed: " + string.Join(", ", lines) + ")" : ""), all);

        // the trigger: the ship flying into it
        FreshScene(0);
        var wm2 = World();
        FinishLevel(wm2);
        var ship = new GameObject("~ship", typeof(BoxCollider2D));
        ship.AddComponent<movePlayer>().enabled = false;
        typeof(Portal).GetMethod("OnTriggerEnter2D", Inst).Invoke(Portal.Live, new object[] { ship.GetComponent<Collider2D>() });
        Check("the ship's collider touching the portal takes it through", WorldManager.CurrentIndex == 1 && !wm2.PortalIsOpen);
        var rock = new GameObject("~rock", typeof(BoxCollider2D));
        FinishLevel(wm2);
        typeof(Portal).GetMethod("OnTriggerEnter2D", Inst).Invoke(Portal.Live, new object[] { rock.GetComponent<Collider2D>() });
        Check("anything else touching it does not", WorldManager.CurrentIndex == 1 && wm2.PortalIsOpen && Portal.Live != null);
        Object.DestroyImmediate(ship);
        Object.DestroyImmediate(rock);
        Object.DestroyImmediate(wm2.gameObject);
    }

    // ---- 3. the final portal: the same flow, back round ------------------------------

    static void FinalPortalLoopsBack()
    {
        int last = WorldManager.Worlds.Length - 1;
        foreach (int start in new[] { 0, 2 })
        {
            FreshScene(last);
            RunLoop.StartWorld = start;
            var wm = World();
            FinishLevel(wm);
            var ring = Portal.Live != null ? Portal.Live.GetComponentsInChildren<SpriteRenderer>()[0] : null;
            Check("final world, run started in " + WorldManager.Worlds[start].displayName + ": the same open portal, leading back there in its colour",
                  wm.Stage == WorldManager.LevelStage.Portal && PortalPressure.Active && PortalPressure.Destination == start &&
                  WorldManager.PortalDestination == start && ring != null && ring.color == WorldManager.Worlds[start].portalColor);
            for (int i = 0; i < (int)(120f / Dt); i++) Fly(wm, null);
            Check("... two minutes on it is still open and pressing like any other (Level " + PortalPressure.Level.ToString("F1") + ")",
                  Portal.Live != null && wm.PortalIsOpen && Mathf.Abs(PortalPressure.Level - PortalPressure.LevelAt(120f)) < .05f &&
                  BossWarning.Read(wm) == BossWarningInput.None);
            Enter();
            Check("... through it: " + WorldManager.Current.displayName + ", loop 2, every boss to fight again, the pressure over",
                  WorldManager.CurrentIndex == start && RunLoop.Index == 1 && !BossEncounter.DoneInWorld(last) &&
                  wm.Stage == WorldManager.LevelStage.Level && !PortalPressure.Active && BossWarning.Read(wm) == BossWarningInput.Ahead);
            Check("... arriving at loop 2's arrival speed, under the cap",
                  Mathf.Approximately(SpeedRamp.Natural, WorldManager.ArrivalSpeed(1)) && SpeedRamp.Natural <= SpeedRamp.Cap);
            Check("... with loop 2's density (x" + LoopDifficulty.DensityScale + ")", Mathf.Approximately(LoopDifficulty.DensityScale, LoopRules.DensityScale(1)));

            // and round again: the final world's portal on loop 2 is the same
            PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, last);
            Apply();
            FinishLevel(wm);
            for (int i = 0; i < (int)(30f / Dt); i++) Fly(wm, null);
            Enter();
            Check("... and the next time round the same: loop 3", WorldManager.CurrentIndex == start && RunLoop.Index == 2 && !PortalPressure.Active);
            Object.DestroyImmediate(wm.gameObject);
        }
    }

    static void NoDeadBranches()
    {
        Check("FinalChoicePanel is gone", System.Type.GetType("FinalChoicePanel, Assembly-CSharp") == null &&
              !File.Exists("Assets/Scripts/Worlds/FinalChoicePanel.cs"));
        Check("WorldManager's stages are Level, Boss, Portal and nothing else",
              System.Enum.GetNames(typeof(WorldManager.LevelStage)).SequenceEqual(new[] { "Level", "Boss", "Portal" }) &&
              typeof(WorldManager).GetNestedType("FinalRoute") == null);
        Check("RunLoop has no encore", typeof(RunLoop).GetProperty("EncorePass") == null && typeof(RunLoop).GetProperty("DifficultyIndex") == null);
        Check("no missed-portal lap: no lifetime, no OnPortalMissed",
              typeof(WorldManager).GetMethod("OnPortalMissed", Inst) == null && typeof(Portal).GetField("lifetime", Inst) == null &&
              typeof(LoopRules).GetField("LoopPortalRetrySeconds") == null && typeof(LoopRules).GetField("AutoPickSeconds") == null);
        var hits = new List<string>();
        foreach (string path in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(path);
            foreach (string word in new[] { "KEEP FLYING", "LOOP BACK", "KeepFlying", "FinalChoice", "Encore", "encore", "OnPortalMissed", "FinalRoute" })
                if (text.Contains(word)) hits.Add(Path.GetFileName(path) + ": " + word);
        }
        Check("no script mentions KEEP FLYING, LOOP BACK, the final choice or the encore (" + string.Join(", ", hits) + ")", hits.Count == 0);
    }

    // ---- 4. anti-farm ------------------------------------------------------------

    static void AntiFarm()
    {
        FreshScene(0);
        var wm = World();
        int token = RunScore.BeginRun(true, false);
        moveBackGround.speed = .30f;
        score.paysRealDust = true;
        float dust0 = score.totalCurrency;
        try
        {
            var rock = new GameObject("~rock");
            rock.tag = "Astr";
            FinishLevel(wm);

            // the grace: everything pays
            for (int i = 0; i < (int)(5f / Dt); i++) Fly(wm, null);
            long before = RunScore.Total;
            int k = RunScore.OnKill(rock);
            RunScore.Tick(1f, moveBackGround.speed);
            int d = RunScore.OnDust(false);
            score.AwardStarDust(1f);
            Check("in the grace everything pays: a kill " + k + ", a second of flight, star dust (" + (RunScore.Total - before) + " points)",
                  k > 0 && d > 0 && RunScore.Total - before > k + d && score.totalCurrency > dust0 && !PortalPressure.EarningsClosed &&
                  PickupRules.StarDustRate() > 0f);

            // past it: nothing
            for (int i = 0; i < (int)(10f / Dt); i++) Fly(wm, null);
            RunScore.Tick(5f, moveBackGround.speed);   // let the grace's chain lapse
            float dust1 = score.totalCurrency;
            before = RunScore.Total;
            int kills = 0;
            for (int i = 0; i < 20; i++) kills += RunScore.OnKill(rock);
            RunScore.Tick(60f, SpeedRamp.Cap);
            int other = RunScore.OnDust(true) + RunScore.OnAtom(RunScore.Atom.Shield) + RunScore.OnElite(Vector3.zero, 50) +
                        RunScore.OnDeathCombo(100, 5, true) + RunScore.OnTeleport(Vector3.zero, Vector3.up * 4f);
            score.AwardStarDust(5f);
            Check("past the grace nothing earns: 20 kills, a minute of flight at the cap, dust, an atom, an elite, a death combo, a teleport -> " +
                  (RunScore.Total - before) + " points", RunScore.Total == before && kills == 0 && other == 0 && PortalPressure.EarningsClosed);
            Check("... no kill chain starts", RunScore.Chain == 0);
            Check("... no star dust (pickups or the flight trickle), no star clusters released",
                  score.totalCurrency == dust1 && PickupRules.StarDustRate() == 0f &&
                  Source("Core/score.cs").Contains("PortalPressure.EarningsClosed ? 0f : ScoreRules.FlightDust"));

            // the world bonus still waits on the other side
            before = RunScore.Total;
            for (int i = 0; i < (int)(1f / Dt); i++) Fly(wm, null);
            Enter();
            long bonus = RunScore.Total - before;
            Check("flying through pays the world bonus (" + bonus + ") and earning resumes",
                  bonus >= ScoreRules.WorldClearedPerWorld && RunScore.OnKill(rock) > 0);
            Check("so lingering is never the best way to score: a minute past the grace earns 0, a minute flying on earns " +
                  Mathf.RoundToInt(ScoreRules.DistancePoints(SpeedRamp.Cap, 60f) * 2f) + "+ from flight alone",
                  ScoreRules.DistancePoints(SpeedRamp.Cap, 60f) > 0f);
            Object.DestroyImmediate(rock);
        }
        finally
        {
            RunScore.EndRun(token);
            score.paysRealDust = false;
            Object.DestroyImmediate(wm.gameObject);
        }
    }

    // ---- 5. the approach -------------------------------------------------------------

    static void ApproachStaysClear()
    {
        FreshScene(1);
        var wm = World();
        FinishLevel(wm);
        for (int i = 0; i < (int)(3f / Dt); i++) Fly(wm, null);
        Check("in the grace pilots clear out and none is admitted", PilotAirspace.MustClear);
        Check("elites stay out: \"" + EliteDirector.Blocked(1, 100f) + "\"", EliteDirector.Blocked(1, 100f) == "portal");
        for (int i = 0; i < (int)(10f / Dt); i++) Fly(wm, null);
        Check("past the grace pilots are let back in (part of what waiting costs)", !PilotAirspace.MustClear && PortalPressure.AdmitsPilots);
        Check("elites still stay out", EliteDirector.Blocked(1, 100f) == "portal");
        var p = Portal.Live;
        float home = p.HomeX, half = Portal.ColumnHalf;
        Check("the portal's column is closed to pilots (its drift, its body and a margin: +/-" + half.ToString("F2") + " u)",
              Portal.Reserves(home - .1f, home + .1f) && Portal.Reserves(home + half - .2f, home + half + 1f) &&
              Portal.Reserves(p.transform.position.x - .05f, p.transform.position.x + .05f));
        float otherSide = -Mathf.Sign(home) * 1.8f;
        Check("... and only its column: the other side of the lane is open", !Portal.Reserves(otherSide - .3f, otherSide + .3f));
        Check("pilots are placed through it (PilotAirspace asks Portal.Reserves)", Source("Gameplay/Enemies/PilotAirspace.cs").Contains("Portal.Reserves("));
        Enter();
        Check("gone through: nothing reserved any more", !Portal.Reserves(-5f, 5f));
        Object.DestroyImmediate(wm.gameObject);
    }

    // ---- 6. the board under pressure ---------------------------------------------------

    static void BoardUnderPressure()
    {
        EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
        EnemyThreat.ForceShooting = true;
        EnemyDensityProbe.SetAuthoredView();
        var rows = new List<string>();
        float prevSpawns = -1f;
        bool rising = true, underCeiling = true, underCap = true;
        foreach (float waited in new[] { -1f, 30f, 120f, 600f })
        {
            PortalPressure.Reset();
            if (waited >= 0f) { PortalPressure.Open(1); PortalPressure.Tick(waited); }
            float ceiling = EnemyDensity.MaxThreats(35f);
            var s = EnemyDensityProbe.Pinned(35, 110f, 2);
            rows.Add(string.Format("{0}: spawns/s {1:F2}, on screen {2:F2} (peak {3:F0}, ceiling {4:F1}), shots {5:F2}, pilots {6:F2}",
                                   waited < 0f ? "no portal" : waited + " s", s.spawnsPerSecond, s.onScreen, s.peakOnScreen, ceiling, s.shots, s.pilots));
            rising &= s.spawnsPerSecond >= prevSpawns * .95f;
            prevSpawns = s.spawnsPerSecond;
            // The ceiling is checked as a spawn is placed, above the view;
            // bodies already on their way in are counted only once they
            // reach the counted band, so a faster spawn rate overshoots it a
            // little (EnemyDensityTest allows +2 on a normal board). The
            // absolute body cap is the hard line.
            underCeiling &= s.peakOnScreen <= ceiling * 1.25f + 1f;
            underCap &= s.peakOnScreen <= PortalPressure.BodyCapAbsolute;
        }
        PortalPressure.Reset();
        foreach (var r in rows) Debug.Log("[PORTAL] BOARD " + r);
        Check("the real spawner fields more as the wait goes on", rising);
        Check("... the bodies in view stay within a quarter of the ceiling (it is checked as a spawn is placed)", underCeiling);
        Check("... and never past the absolute body cap, " + PortalPressure.BodyCapAbsolute + " (60 fps)", underCap);
        EnemyDensityProbe.RestoreView();
        EnemyThreat.ForceShooting = false;
    }

    // ---- 7. what the pilot sees and hears ------------------------------------------------

    static readonly List<PortalPressure.Signal> heard = new List<PortalPressure.Signal>();
    static void Hear(PortalPressure.Signal s) { heard.Add(s); }

    static void Hud()
    {
        FreshScene(0);
        var wm = World();
        var hud = PortalPressureHud.Ensure();
        heard.Clear();
        PortalPressure.Beat += Hear;
        try
        {
            hud.Refresh();
            Check("no portal: no chip, no glow", !hud.Showing);
            FinishLevel(wm);
            for (int i = 0; i < (int)(5f / Dt); i++) Fly(wm, null);
            hud.Refresh();
            Check("in the grace: still nothing (the \"PORTAL OPEN\" banner only)", !hud.Showing && PortalPressure.OpenBanner == "PORTAL OPEN");
            for (int i = 0; i < (int)(4f / Dt); i++) Fly(wm, null);
            hud.Refresh();
            Check("past the grace: the chip reads \"" + hud.Chip.text + "\"", hud.Showing && hud.Chip.text == "PORTAL  DANGER 1");
            for (int i = 0; i < (int)(30f / Dt); i++) Fly(wm, null);
            hud.Refresh();
            Check("thirty seconds on: \"" + hud.Chip.text + "\"", hud.Chip.text == PortalPressureHud.ChipLabel(1 + Mathf.FloorToInt(PortalPressure.Level)) &&
                  PortalPressure.DangerNumber == 4);
            int levels = heard.Count(s => s == PortalPressure.Signal.LevelUp);
            Check("the beats: Open, GraceOver once, a LevelUp each whole Level (" + levels + ")",
                  heard.Count(s => s == PortalPressure.Signal.Open) == 1 && heard.Count(s => s == PortalPressure.Signal.GraceOver) == 1 &&
                  levels == Mathf.FloorToInt(PortalPressure.Level));
            Check("the sound hook is the boss warning's procedural klaxon and thump (no audio files)",
                  Source("Worlds/PortalPressureHud.cs").Contains("BossWarningAudio.Play(BossWarningBeat.Announce") &&
                  Source("Worlds/PortalPressureHud.cs").Contains("BossWarningAudio.Play(BossWarningBeat.Second"));

            bool notRed = true, warming = true;
            for (int w = 0; w < WorldManager.Worlds.Length; w++)
            {
                float prevAlpha = -1f;
                for (float level = 0f; level <= 60f; level += .25f)
                {
                    Color c = PortalPressureHud.Accent(w, level);
                    notRed &= !HostileGlow.IsPlayerRed(c);
                    float a = PortalPressureHud.GlowAlpha(level);
                    warming &= a >= prevAlpha && PortalPressureHud.BeatsPerSecond(level) <= PortalPressureHud.BeatMax;
                    prevAlpha = a;
                }
            }
            Check("the chip and the edge glow are never the player's red, in any world's colour, at any Level", notRed);
            Check("the glow only brightens and quickens with the Level (to a ceiling)", warming);
            Check("live: the chip and both glows wear the accent", hud.Chip.color.a > 0f && hud.LeftGlow.color.a > 0f &&
                  !HostileGlow.IsPlayerRed(hud.Chip.color));

            InsideTheRails();

            Enter();
            hud.Refresh();
            Check("flown through: the chip and glow are gone, and the beat says Entered",
                  !hud.Showing && heard.Last() == PortalPressure.Signal.Entered);
        }
        finally
        {
            PortalPressure.Beat -= Hear;
            Object.DestroyImmediate(hud.gameObject);
            Object.DestroyImmediate(wm.gameObject);
        }
    }

    // The chip and the glows sit inside the rails and under any cutout
    // (TopBand), on phones short and tall, with and without a notch.
    static void InsideTheRails()
    {
        bool inside = true;
        var shapes = new[] { new Vector2(1080f, 1920f), new Vector2(1080f, 2340f), new Vector2(1080f, 2520f), new Vector2(1170f, 2532f),
                             new Vector2(1536f, 2048f), new Vector2(720f, 1280f) };
        foreach (var screen in shapes)
            foreach (bool notch in new[] { false, true })
            {
                var safe = notch ? new Rect(0f, 0f, screen.x, screen.y - 130f) : new Rect(0f, 0f, screen.x, screen.y);
                var cutouts = notch ? new[] { new Rect(screen.x * .5f - 150f, screen.y - 100f, 300f, 100f) } : new Rect[0];
                var band = TopBand.FrameFor(safe, screen, BossRails.InnerEdge, cutouts);
                float scale = Mathf.Min(screen.x / 800f, screen.y / 1200f);   // the overlay canvas: 800x1200, Expand
                Rect chip = PortalPressureHud.ChipScreenRect(band, scale);
                Vector2 glow = PortalPressureHud.GlowEdges(band);
                bool ok = chip.xMin >= band.left - .5f && chip.xMax <= band.right + .5f && chip.yMax <= band.top && chip.yMin > 0f &&
                          glow.x >= band.left - .5f && glow.y <= band.right + .5f;
                foreach (var c in cutouts) ok &= !chip.Overlaps(c);
                if (!ok) Debug.Log("[PORTAL] chip out of the band at " + screen + (notch ? " (notch)" : "") + ": " + chip + " band " + band.left + ".." + band.right + " top " + band.top);
                inside &= ok;
            }
        Check("the danger chip and the edge glows sit inside the rails and clear of cutouts on six screens, notched or not (TopBand)", inside);
    }

    // ---- 8. allocations ---------------------------------------------------------------

    static void NoAllocations()
    {
        FreshScene(0);
        var wm = World();
        var walls = Walls();
        var hud = PortalPressureHud.Ensure();
        try
        {
            FinishLevel(wm);
            for (int n = 1; n < 64; n++) PortalPressureHud.ChipLabel(n);   // the labels are built once each
            // into a Level's middle, so the measured stretch has no Level change
            while (PortalPressure.Seconds < PortalPressure.GraceSeconds + 3.5f * PortalPressure.LevelSeconds) Fly(wm, walls);
            float sink = 0f;
            var portal = Portal.Live;
            float rate = WorldManager.Current.speedRampPerSecond;
            // a flying frame as Unity runs it (TestHarness.Send reflects, so
            // it is not used inside the meter)
            System.Action work = () =>
            {
                for (int i = 0; i < 150; i++)
                {
                    frame++;
                    SpeedRamp.Tick(rate, SpeedRamp.Cap);
                    wm.Tick(Dt);
                    portal.Step(Dt);
                    hud.Refresh();
                    sink += EnemyThreat.ShotBudget + EnemyThreat.Gap + EnemyDensity.MaxThreats(35f) + EnemyDensity.MaxPilotLoad(35f, 0) +
                            EnemyDensity.MaxChasers(35f) + SpawnLane.GuaranteedGap + PortalPressure.Ceiling(10f, 1.3f) +
                            (PilotAirspace.MustClear ? 1f : 0f) + (Portal.Reserves(-.2f, .2f) ? 1f : 0f) + PortalPressure.DangerNumber;
                }
            };
            work();
            bool meter = TestHarness.AllocMeterWorks(out long control);
            long bytes = TestHarness.AllocatedBytes(work);
            Check("waiting at the portal allocates nothing per frame (the clock, the dials, the portal, the chip: " + bytes +
                  " bytes over 150 frames; meter control " + control + ")", meter && bytes == 0 && sink > 0f);
        }
        finally
        {
            Object.DestroyImmediate(hud.gameObject);
            foreach (var w in walls) Object.DestroyImmediate(w.gameObject);
            Object.DestroyImmediate(wm.gameObject);
        }
    }
}
