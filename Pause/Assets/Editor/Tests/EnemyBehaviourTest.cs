using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// Feature: every roster enemy behaves as what it is (EnemyBehaviours,
// EnemyBrain; docs/enemy-behaviours.md).
//
//   - every roster key has a behaviour, the factory wires it
//   - an enemy that does not shoot never spawns a shot
//   - every shooter telegraphs (tell cell held, charge light) for at least
//     its tell before a shot leaves, and shows the release cell as it fires
//   - an aimed shot flies where the pilot was when the tell began
//   - nothing advances while the world is paused
//   - every enemy stays inside its envelope and the lane; a mine stays on
//     its rail
//   - shots are roster shots (friendly fire on: HostileFireTest), never the player's red,
//     and the shot budget holds
//   - the four chasers hunt differently, and leave after their linger
//   - PRESENCE: every key is a hazard or a pilot; hazards ride the scroll;
//     a pilot's time on screen does not depend on the scroll speed and is
//     bounded; pilots enter from the top and leave by the top or the bottom;
//     hazards are routed round reserved columns and a pilot waits for its
//     column; the pilot budget holds; pilots clear out for a boss / portal;
//     an escaped pilot pays nothing
//   - the brain allocates nothing per step
public static class EnemyBehaviourTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[BRAIN] PASS  " : "[BRAIN] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;
    static float clock;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
            Fresh();
            EveryKeyHasABehaviour();
            NonShootersNeverShoot();
            ShootersTelegraph();
            AimIsLockedAtTheTell();
            NothingAdvancesWhilePaused();
            StaysInsideEnvelopeAndLane();
            MinesStayOnTheirRail();
            ShotsAreFairAndBudgeted();
            ChasersDiffer();
            EveryKeyIsHazardOrPilot();
            HazardsRideTheScroll();
            PilotsFlyTheirScripts();
            PilotAttackRuns();
            AirspaceRoutesHazardsRoundPilots();
            PilotBudgetHolds();
            PilotsClearForBossAndPortal();
            StunHoldsTheBrain();
            ShovedEnemiesRecover();
            AllocatesNothing();
        }
        finally
        {
            EnemyThreat.ForceShooting = false;
            EnemyThreat.Reset();
            EnemyBrain.PilotsEnabled = true;
            PilotAirspace.Clear();
            SetWorldManager(null);
            SpawnSpace.ClockOverride = null;
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
            Clear();
            buttonClicks.playerDied = false;
        }
        Debug.Log("[BRAIN] failures: " + fails);
        return fails;
    }

    // ---- fixtures ----------------------------------------------------------

    static Transform ship;

    static void Fresh()
    {
        Clear();
        EliteSystem.Clear();
        RailMineLasers.Clear();   // a mine's burning beam counts against the shot budget (one per world's mine would pile up)
        EnemyThreat.Reset();
        PilotAirspace.Clear();
        PilotAirspace.ResetStats();
        EnemyBrain.PilotsEnabled = true;
        EnemyThreat.ForceShooting = true;
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;   // running without a touch in batch mode
        startMenu.youAreInTutorial = false;
        moveBackGround.speed = .2f;
        clock = 100f;
        SpawnSpace.ClockOverride = clock;
        if (ship == null) ship = new GameObject("~BrainTestShip").transform;
        ship.position = new Vector3(0f, -3f, 0f);
    }

    static void SetWorldManager(WorldManager wm)
    {
        typeof(WorldManager).GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, wm);
    }

    static void Clear()
    {
        foreach (var b in Object.FindObjectsByType<enmiesOnBoard>(FindObjectsSortMode.None)) Object.DestroyImmediate(b.gameObject);
        foreach (var e in Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None)) Object.DestroyImmediate(e.gameObject);
        foreach (var r in Object.FindObjectsByType<RailLaneScroller>(FindObjectsSortMode.None)) Object.DestroyImmediate(r.gameObject);
        if (ship != null) Object.DestroyImmediate(ship.gameObject);
        ship = null;
    }

    static EnemyBrain Build(EnemyDef def, Vector2 at, bool armed = true)
    {
        // (an enemy that only sometimes attacks -- Bile Mite -- is rolled until it does)
        for (int k = 0; k < 200; k++)
        {
            var go = EnemyFactory.Create(def, new Vector3(at.x, at.y, 0f), Quaternion.identity);
            var brain = go.GetComponent<EnemyBrain>();
            if (brain == null) return null;
            brain.TargetOverride = ship;
            if (!armed || !brain.Behaviour.Attacks || brain.Armed) return brain;
            Object.DestroyImmediate(go);
        }
        return null;
    }

    // One running frame for `brain`: the brain, its rail mount, the shots.
    static void Step(EnemyBrain brain)
    {
        clock += Dt;
        SpawnSpace.ClockOverride = clock;
        brain.Step(Dt);
        var mount = brain.GetComponent<RailMineMount>();
        if (mount != null) mount.SendMessage("LateUpdate");
        var fb = brain.GetComponent<EnemyFlipbook>();
        if (fb != null) fb.Advance(Dt);
        EliteSystem.Step(Dt);
    }

    static GameObject Rail(float x, float y)
    {
        var rail = new GameObject("RailMineLane");
        rail.transform.position = new Vector3(x, y, 0f);
        rail.AddComponent<RailLaneScroller>();
        return rail;
    }

    static EnemyBrain BuildMine(EnemyDef def, float x, float y, out GameObject rail)
    {
        rail = Rail(x, y);
        var brain = Build(def, new Vector2(x, y));
        var mount = brain.gameObject.AddComponent<RailMineMount>();
        mount.MountTo(rail.transform);
        mount.brain = brain;
        return brain;
    }

    static int LiveRosterShots() { return EnemyThreat.LiveShots; }

    // ---- 1 -------------------------------------------------------------------

    static void EveryKeyHasABehaviour()
    {
        var missing = new List<string>();
        var unwired = new List<string>();
        int shooters = 0, lungers = 0, movers = 0;
        foreach (var def in EnemyRoster.All)
        {
            var b = EnemyBehaviours.For(def.key);
            if (b == null) { missing.Add(def.key); continue; }
            if (b.Shoots) shooters++;
            if (b.attack == EnemyAttack.Lunge) lungers++;
            if (b.Moves || def.role == EnemyRole.Chaser) movers++;
            var go = EnemyFactory.Create(def, new Vector3(0f, 40f, 0f), Quaternion.identity);
            var brain = go.GetComponent<EnemyBrain>();
            bool ok = def.role == EnemyRole.Chaser
                ? brain == null && go.GetComponent<ChaserEnemy>().style == b.chaser
                : brain != null && brain.Behaviour == b && def.Behaviour == b;
            if (def.role == EnemyRole.Rock || def.role == EnemyRole.Alien)
                ok &= !go.GetComponent<moveEnimes>().Weaving;   // the brain moves it sideways, not the old ping-pong
            if (!ok) unwired.Add(def.key);
            Object.DestroyImmediate(go);
        }
        Check("every roster key has a behaviour (" + EnemyRoster.All.Length + " enemies; missing: " + string.Join(",", missing) + ")",
              missing.Count == 0);
        Check("the behaviour table has no entry without a roster enemy (" + EnemyBehaviours.Count + ")",
              EnemyBehaviours.Count == EnemyRoster.All.Length);
        Check("the factory wires every behaviour: a brain on its mover, the chaser's style (" + string.Join(",", unwired) + ")",
              unwired.Count == 0);
        Check("some shoot and most do not (" + shooters + " shooters, " + lungers + " lungers of " + EnemyRoster.All.Length + ")",
              shooters >= 12 && shooters <= EnemyRoster.All.Length / 2 && lungers >= 4);
        Check("every enemy does something of its own: moves, attacks or both (" + movers + ")", movers == EnemyRoster.All.Length ||
              AllActive());

        bool rocksInert = true, envelopes = true;
        var signatures = new HashSet<string>();
        foreach (var def in EnemyRoster.All)
        {
            var b = EnemyBehaviours.For(def.key);
            if (b == null) continue;
            if (def.role == EnemyRole.Rock) rocksInert &= b.attack == EnemyAttack.None;
            envelopes &= b.bandX >= 0f && b.Up >= 0f && b.Down >= 0f && b.bandX <= 1.3f && b.Up <= 2f && b.Down <= 2.1f;
            signatures.Add(Signature(b));
        }
        Check("rocks are inert: none of them attacks", rocksInert);
        Check("every envelope is bounded (band <= 1.3, up <= 2, down <= 2.1)", envelopes);
        Check("no two enemies share the same combination of primitives and numbers (" + signatures.Count + " distinct)",
              signatures.Count == EnemyRoster.All.Length);
    }

    static bool AllActive()
    {
        foreach (var def in EnemyRoster.All)
        {
            var b = EnemyBehaviours.For(def.key);
            if (b != null && !b.Moves && !b.Attacks && def.role != EnemyRole.Chaser) return false;
        }
        return true;
    }

    static string Signature(EnemyBehaviour b)
    {
        return string.Join("|", b.lateral, b.bandX, b.lateralSpeed, b.lateralPeriod, b.vertical, b.rise, b.sink, b.verticalPeriod,
                           b.attack, b.shotKind, b.shotCount, b.shotSpread, b.lungeDive, b.chaser, b.spin, b.tilt);
    }

    // ---- 2 -------------------------------------------------------------------

    static void NonShootersNeverShoot()
    {
        Fresh();
        int launchedBefore = EliteSystem.Shots.Launched;
        var offenders = new List<string>();
        int checkedCount = 0;
        foreach (var def in EnemyRoster.All)
        {
            var b = EnemyBehaviours.For(def.key);
            if (b == null || b.Shoots || def.role == EnemyRole.Chaser) continue;
            checkedCount++;
            GameObject rail = null;
            var brain = def.role == EnemyRole.Mine ? BuildMine(def, 2.35f, 2.5f, out rail) : Build(def, new Vector2(.4f, 2.5f));
            for (int i = 0; i < 60 * 12; i++)
            {
                ship.position = new Vector3(Mathf.Sin(i * .02f) * 1.5f, -3f, 0f);
                Step(brain);
            }
            if (brain.ShotsFired != 0 || brain.ChargeLight != null) offenders.Add(def.key);
            Object.DestroyImmediate(brain.gameObject);
            if (rail != null) Object.DestroyImmediate(rail);
        }
        Check("12 s in the pilot's face: no non-shooter fires or even carries a charge light (" + checkedCount + " enemies; " +
              string.Join(",", offenders) + ")", offenders.Count == 0 && checkedCount >= 20);
        Check("... and the shot pool launched nothing", EliteSystem.Shots.Launched == launchedBefore && LiveRosterShots() == 0);
    }

    // ---- 3 -------------------------------------------------------------------

    static void ShootersTelegraph()
    {
        var bad = new List<string>();
        int shooters = 0;
        float shortest = float.MaxValue;
        foreach (var def in EnemyRoster.All)
        {
            var b = EnemyBehaviours.For(def.key);
            if (b == null || !b.Shoots) continue;
            shooters++;
            Fresh();
            GameObject rail = null;
            var brain = def.role == EnemyRole.Mine ? BuildMine(def, 2.35f, 1.5f, out rail) : Build(def, new Vector2(.3f, 1.8f));
            var fb = brain.GetComponent<EnemyFlipbook>();
            float told = 0f;          // seconds the tell cell was showing before the shot
            bool fired = false, lightOn = false, releaseCell = false, firedFromIdle = false, tellCellOk = true;
            int launched = EliteSystem.Shots.Launched;
            for (int i = 0; i < 60 * 12 && !fired; i++)
            {
                var before = brain.State;
                Step(brain);
                if (brain.State == EnemyBrain.Phase.Windup)
                {
                    told += Dt;
                    lightOn |= brain.ChargeLight != null && brain.ChargeLight.enabled;
                    int f = fb.CurrentFrame;
                    tellCellOk &= def.role == EnemyRole.Mine
                        ? f == EnemyRoster.TellFrame || f == EnemyRoster.TellFrame + 1
                        : f == EnemyRoster.TellFrame;
                }
                // (a mine's laser leaves no shot in the pool: its brain counts it)
                if (EliteSystem.Shots.Launched > launched || brain.ShotsFired > 0)
                {
                    fired = true;
                    firedFromIdle = before != EnemyBrain.Phase.Windup;
                    releaseCell = fb.CurrentFrame == EnemyRoster.TellFrame + 1 && brain.State == EnemyBrain.Phase.Release;
                }
            }
            float need = Mathf.Max(EnemyBrain.TellFloorSeconds, b.tell);
            shortest = Mathf.Min(shortest, told);
            bool ok = fired && !firedFromIdle && told >= need - 2f * Dt && brain.LastTellSeconds >= need - 1e-4f &&
                      lightOn && tellCellOk && releaseCell && brain.ShotsFired >= 1 && (fb.BrainDriven || def.role == EnemyRole.Mine);
            if (!ok)
                bad.Add(def.key + "(fired " + fired + " told " + told.ToString("F2") + "/" + need.ToString("F2") + " light " + lightOn +
                        " tellCell " + tellCellOk + " releaseCell " + releaseCell + " live " + EnemyThreat.LiveShots + " (beams " + RailMineLasers.LiveBeams + " hazards " + AttackHazard.LiveThreat + ") pending " + EnemyThreat.PendingShots +
                        " budget " + EnemyThreat.ShotBudget + " state " + brain.State + " y " + brain.transform.position.y.ToString("F2") + ")");
            Object.DestroyImmediate(brain.gameObject);
            if (rail != null) Object.DestroyImmediate(rail);
        }
        Check("every shooter holds its tell cell with the charge light on for its full tell, then shows the release cell as it fires (" +
              shooters + " shooters; " + string.Join(", ", bad) + ")", bad.Count == 0 && shooters >= 12);
        Check("no tell is shorter than the floor (" + shortest.ToString("F2") + " s >= " + EnemyBrain.TellFloorSeconds + ")",
              shortest >= EnemyBrain.TellFloorSeconds - 2f * Dt);

        // a pilot's lunge is told the same way, then it dives and comes back to its station
        var badLunge = new List<string>();
        int lungers = 0;
        foreach (var def in EnemyRoster.All)
        {
            var b = EnemyBehaviours.For(def.key);
            if (b == null || b.attack != EnemyAttack.Lunge || b.maxVolleys <= 0) continue;
            lungers++;
            Fresh();
            ship.position = new Vector3(.6f, -3f, 0f);
            var brain = Build(def, new Vector2(0f, 1.8f));
            float told = 0f, deepest = 0f, widest = 0f;
            bool lunged = false, recovered = false;
            Vector2 before = brain.Offset;   // where it stood as the windup ended
            for (int i = 0; i < 60 * 10 && !recovered; i++)
            {
                Step(brain);
                if (!lunged && brain.State == EnemyBrain.Phase.Windup) { told += Dt; before = brain.Offset; }
                if (brain.State == EnemyBrain.Phase.Release) lunged = true;
                if (!lunged) continue;
                deepest = Mathf.Min(deepest, brain.Offset.y - before.y);
                widest = Mathf.Max(widest, Mathf.Abs(brain.Offset.x - before.x));
                if (brain.State == EnemyBrain.Phase.Idle) recovered = true;
            }
            bool ok = lunged && recovered && told >= Mathf.Max(EnemyBrain.TellFloorSeconds, b.tell) - 2f * Dt && brain.ShotsFired == 0;
            if (b.lungeDive > 0f) ok &= deepest <= -b.lungeDive * .9f && brain.Offset.y >= before.y - .05f;   // dived, and came back
            else ok &= widest >= b.bandX * .9f;                                                                // a slash: right across its band
            if (!ok) badLunge.Add(def.key + "(told " + told.ToString("F2") + ", deepest " + deepest.ToString("F2") + ", widest " + widest.ToString("F2") + ")");
            Object.DestroyImmediate(brain.gameObject);
        }
        Check("every lunging pilot winds up, dives (or slashes) and recovers to its station, and fires nothing (" + lungers + "; " +
              string.Join(", ", badLunge) + ")", badLunge.Count == 0 && lungers >= 3);
    }

    // ---- 4 -------------------------------------------------------------------

    static void AimIsLockedAtTheTell()
    {
        Fresh();
        var def = EnemyRoster.Find("space_fighter_4");   // Warden: one aimed shell
        ship.position = new Vector3(1.2f, -3f, 0f);
        var brain = Build(def, new Vector2(0f, 1.8f));
        bool movedAway = false;
        EliteShot shot = null;
        for (int i = 0; i < 60 * 10 && shot == null; i++)
        {
            Step(brain);
            if (brain.State == EnemyBrain.Phase.Windup && !movedAway)
            {
                movedAway = true;
                ship.position = new Vector3(-1.8f, -3f, 0f);   // the pilot dodges once the tell starts
            }
            foreach (var s in EliteSystem.Shots.All) if (s.Active && s.RosterShot) shot = s;
        }
        Check("the Warden fired its shell", shot != null && movedAway);
        if (shot == null) return;
        Check("the shell flies toward where the pilot was when the tell began, not where it went (vx " + shot.Velocity.x.ToString("F2") + ")",
              shot.Velocity.x > .2f);
        float deg = Vector2.Angle(Vector2.down, shot.Velocity);
        Check("... inside its aim cone (" + deg.ToString("F1") + " <= " + brain.Behaviour.aimCone + " degrees)", deg <= brain.Behaviour.aimCone + .5f);
        Check("a pilot's shell flies in world space (it does not ride the board) and is a roster shot", shot.RosterShot && shot.Ride == 0f);

        // (changed with hostile fire: roster shots used to pass through every
        // hazard; now they hit one that has been on the board past its
        // spawn-in protection, and never their own shooter -- HostileFireTest)
        var fresh = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Rock), shot.transform.position, Quaternion.identity);
        for (int i = 0; i < 2; i++) EliteSystem.Step(Dt);
        Check("a rock that has just come in is spared by the shell (spawn-in protection)", fresh != null && fresh.GetComponent<ClearTarget>().enabled);
        if (fresh != null) Object.DestroyImmediate(fresh);
        var rock = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Rock), shot.transform.position + (Vector3)(shot.Velocity.normalized * .4f), Quaternion.identity);
        FriendlyFire.Settle(rock);
        for (int i = 0; i < 20; i++) EliteSystem.Step(Dt);
        Check("a rock that has been on the board a while is destroyed by the shell (hostile fire)", rock == null || !rock.GetComponent<ClearTarget>().enabled);
        Check("... and the Warden that fired it is untouched", brain != null && brain.gameObject.activeInHierarchy);
        if (rock != null) Object.DestroyImmediate(rock);
        Object.DestroyImmediate(brain.gameObject);
    }

    // ---- 5 -------------------------------------------------------------------

    static void NothingAdvancesWhilePaused()
    {
        Fresh();
        var brains = new List<EnemyBrain>();
        foreach (string key in new[] { "space_fighter_4", "frost_fighter_4", "verdant_fighter_2", "ember_rock_islet", "frost_alien" })
            brains.Add(Build(EnemyRoster.Find(key), new Vector2(brains.Count - 2f, 1.8f)));
        // run until the first of them is mid-windup: the worst moment to freeze
        bool winding = false;
        for (int i = 0; i < 60 * 8 && !winding; i++)
        {
            foreach (var b in brains) Step(b);
            foreach (var b in brains) winding |= b.State == EnemyBrain.Phase.Windup && b.StateTime > .2f;
        }
        Check("an enemy is mid-windup when the pilot lets go", winding);

        // pause: finger up with pauses left (the game's rule), timeScale 0
        score.pauseCounter = 3;
        bool flying = !buttonClicks.playerDied && (TouchInput.IsPressed || score.pauseCounter <= 0);
        var states = new List<EnemyBrain.Phase>();
        var times = new List<float>();
        var places = new List<Vector3>();
        var frames = new List<int>();
        foreach (var b in brains)
        {
            states.Add(b.State); times.Add(b.StateTime); places.Add(b.transform.position);
            frames.Add(b.GetComponent<EnemyFlipbook>().CurrentFrame);
        }
        int launched = EliteSystem.Shots.Launched, live = LiveRosterShots();
        var shotAt = new List<Vector3>();
        foreach (var s in EliteSystem.Shots.All) shotAt.Add(s.transform.position);
        for (int i = 0; i < 600; i++)
        {
            foreach (var b in brains)
            {
                TestHarness.Send(b, "LateUpdate");                 // what Unity calls on a frozen frame
                b.Step(0f);                                        // a zero-dt step
                b.GetComponent<EnemyFlipbook>().Advance(0f);
            }
            EliteSystem.Step(0f);
        }
        bool same = true;
        for (int i = 0; i < brains.Count; i++)
            same &= brains[i].State == states[i] && Mathf.Approximately(brains[i].StateTime, times[i]) &&
                    brains[i].transform.position == places[i] && brains[i].GetComponent<EnemyFlipbook>().CurrentFrame == frames[i];
        bool shotsStill = true;
        for (int i = 0; i < shotAt.Count; i++) shotsStill &= EliteSystem.Shots.All[i].transform.position == shotAt[i];
        Check("the pause rule reads as frozen (finger up, pauses left)", !flying);
        Check("600 frozen frames: no brain changes state, timer, drawing or place", same);
        Check("... nothing fires and no shot moves", EliteSystem.Shots.Launched == launched && LiveRosterShots() == live && shotsStill);

        // dead pilot: frozen as well
        score.pauseCounter = 0;
        buttonClicks.playerDied = true;
        foreach (var b in brains) for (int i = 0; i < 60; i++) TestHarness.Send(b, "LateUpdate");
        bool deadSame = true;
        for (int i = 0; i < brains.Count; i++) deadSame &= brains[i].State == states[i] && brains[i].transform.position == places[i];
        Check("a dead pilot: brains hold too", deadSame);
        buttonClicks.playerDied = false;

        // resume: the windup carries on from where it stopped and completes
        bool fired = false;
        for (int i = 0; i < 60 * 6 && !fired; i++)
        {
            foreach (var b in brains) Step(b);
            fired = EliteSystem.Shots.Launched > launched;
        }
        Check("on resume the interrupted windup carries on and fires", fired);
        foreach (var b in brains) Object.DestroyImmediate(b.gameObject);
    }

    // ---- 6 -------------------------------------------------------------------

    static void StaysInsideEnvelopeAndLane()
    {
        var bad = new List<string>();
        int checkedCount = 0;
        foreach (var def in EnemyRoster.All)
        {
            var b = EnemyBehaviours.For(def.key);
            if (b == null || b.IsPilot || def.role == EnemyRole.Mine) continue;   // (pilots: PilotsFlyTheirScripts)
            checkedCount++;
            float half = def.ColliderSize.x * .5f;
            float lane = SpawnLane.LaneHalf - half;
            // the spawner's extremes: as far out as it may place this enemy
            float maxX = Mathf.Max(0f, SpawnLane.MaxX(def) - b.bandX);
            foreach (float x0 in new[] { -maxX, 0f, maxX })
            {
                Fresh();
                Random.InitState(def.key.GetHashCode() + (int)(x0 * 10f));
                var brain = Build(def, new Vector2(x0, 3f));
                float worstX = 0f, worstUp = 0f, worstDown = 0f, worstLane = 0f;
                for (int i = 0; i < 60 * 20; i++)
                {
                    // the pilot sweeps the whole lane, to drag trackers and lungers to their limits
                    ship.position = new Vector3(Mathf.Sin(i * .013f) * 2.4f, -3f, 0f);
                    Step(brain);
                    Vector2 o = brain.Offset;
                    worstX = Mathf.Max(worstX, Mathf.Abs(o.x) - b.bandX);
                    worstUp = Mathf.Max(worstUp, o.y - b.Up);
                    worstDown = Mathf.Max(worstDown, -o.y - b.Down);
                    worstLane = Mathf.Max(worstLane, Mathf.Abs(brain.transform.position.x) - lane);
                }
                // its SpawnSpace sweep covers wherever it has been
                var f = brain.GetComponent<SpawnFootprint>();
                Rect sweep = f.Sweep(0f, SpawnSpace.Lifetime);
                bool covered = sweep.Contains((Vector2)brain.transform.position) &&
                               sweep.width >= 2f * (f.half.x + b.bandX) - 1e-3f && sweep.height >= 2f * f.half.y + b.Up + b.Down - 1e-3f;
                if (worstX > 1e-3f || worstUp > 1e-3f || worstDown > 1e-3f || worstLane > 1e-3f || !covered)
                    bad.Add(def.key + "@" + x0.ToString("F1") + "(x " + worstX.ToString("F3") + " up " + worstUp.ToString("F3") + " down " +
                            worstDown.ToString("F3") + " lane " + worstLane.ToString("F3") + " covered " + covered + ")");
                Object.DestroyImmediate(brain.gameObject);
            }
        }
        Check("20 s each, spawned at the lane's centre and both extremes, the pilot sweeping the lane: no enemy leaves its envelope or " +
              "the lane, and its SpawnSpace sweep covers it (" + checkedCount + " board-riding hazards; " + string.Join(", ", bad) + ")",
              bad.Count == 0 && checkedCount >= 14);
    }

    // ---- 7 -------------------------------------------------------------------

    static void MinesStayOnTheirRail()
    {
        var bad = new List<string>();
        int slid = 0;
        foreach (var def in EnemyRoster.All)
        {
            if (def.role != EnemyRole.Mine) continue;
            var b = EnemyBehaviours.For(def.key);
            foreach (float x in new[] { -2.35f, 2.35f })
            {
                Fresh();
                GameObject rail;
                var brain = BuildMine(def, x, 2f, out rail);
                var mount = brain.GetComponent<RailMineMount>();
                float worstRail = 0f, worstSlide = 0f, travelled = 0f;
                for (int i = 0; i < 60 * 15; i++)
                {
                    rail.transform.position += Vector3.down * .5f * Dt;   // the rail scrolls; the mine must follow it
                    if (rail.transform.position.y < -1f) rail.transform.position += Vector3.up * 3f;
                    Step(brain);
                    worstRail = Mathf.Max(worstRail, mount.AlignmentError);
                    worstSlide = Mathf.Max(worstSlide, Mathf.Max(mount.Slide - b.Up, -mount.Slide - b.Down));
                    travelled = Mathf.Max(travelled, Mathf.Abs(mount.Slide));
                }
                if (travelled > .2f) slid++;
                if (worstRail > .015f || worstSlide > 1e-3f)
                    bad.Add(def.key + "@" + x + "(off rail " + worstRail.ToString("F3") + ", slide over " + worstSlide.ToString("F3") + ")");
                Object.DestroyImmediate(brain.gameObject);
                Object.DestroyImmediate(rail);
            }
        }
        Check("every world's mine stays exactly on its rail, on either wall, sliding only inside its envelope (" + string.Join(", ", bad) + ")",
              bad.Count == 0);
        Check("the sliding mines do slide along the rail (" + slid + " of 8 wall/mine pairs)", slid >= 6);
    }

    // ---- 8 -------------------------------------------------------------------

    static void ShotsAreFairAndBudgeted()
    {
        bool colours = true;
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
        {
            Color c = EnemyBehaviours.ShotColor(w);
            colours &= !HostileGlow.IsPlayerRed(c) && !HostileGlow.IsPlayerRed(HostileGlow.Tint(c));
        }
        Check("no world's shot colour (or its glow) is the player's red", colours);
        bool styles = true;
        foreach (var def in EnemyRoster.All)
        {
            var b = EnemyBehaviours.For(def.key);
            if (b == null || !b.Shoots) continue;
            var style = b.ShotStyle;
            styles &= style != null && style.ShotColor == EnemyBehaviours.ShotColor(def.world) && style.shotSize >= .15f &&
                      b.shotSpeed <= 4f && b.shotCount <= 8;
        }
        Check("every shooter's shots wear its world's colour, at a readable size (>= 0.15 u) and a dodgeable speed (<= 4 u/s over the board)",
              styles);

        // six Pyres (a ring of eight each) all wanting to fire at once
        Fresh();
        var brains = new List<EnemyBrain>();
        for (int i = 0; i < 4; i++) brains.Add(Build(EnemyRoster.Find("ember_fighter_4"), new Vector2(-1.8f + i * 1.2f, 1.4f)));
        int peak = 0, sameFrameVolleys = 0;
        for (int i = 0; i < 60 * 14; i++)
        {
            int before = EliteSystem.Shots.Launched;
            int windups = 0;
            foreach (var b in brains) windups += b.Windups;
            foreach (var b in brains) { clock += Dt / brains.Count; SpawnSpace.ClockOverride = clock; b.Step(Dt); }
            EliteSystem.Step(Dt);
            int after = 0;
            foreach (var b in brains) after += b.Windups;
            if (after - windups > 1) sameFrameVolleys++;
            peak = Mathf.Max(peak, LiveRosterShots());
        }
        Check("four ring-shooters (32 shots wanted) together never pass the shot budget (peak " + peak + " <= " + EnemyThreat.MaxEnemyShots + ")",
              peak <= EnemyThreat.MaxEnemyShots && peak > 0);
        Check("... and no two of them begin a windup in the same frame (volley gap " + EnemyThreat.VolleyGap + " s)", sameFrameVolleys == 0);
        foreach (var b in brains) Object.DestroyImmediate(b.gameObject);

        // no fire from off screen, from below the pilot, or point blank (the
        // rule itself, held still: pilots off, so nothing flies to a station)
        Fresh();
        EnemyBrain.PilotsEnabled = false;
        var high = Build(EnemyRoster.Find("space_fighter_3"), new Vector2(0f, CameraFit.ViewTop + 1f));
        var low = Build(EnemyRoster.Find("space_fighter_3"), new Vector2(1.5f, -4.2f));
        var close = Build(EnemyRoster.Find("space_fighter_3"), new Vector2(-.2f, -1.9f));
        for (int i = 0; i < 60 * 8; i++) { Step(high); low.Step(Dt); close.Step(Dt); }
        Check("a shooter above the view, below the pilot or point blank never starts a windup",
              high.Windups == 0 && low.Windups == 0 && close.Windups == 0 && !high.IsPilot);
        EnemyBrain.PilotsEnabled = true;
        Object.DestroyImmediate(high.gameObject);
        Object.DestroyImmediate(low.gameObject);
        Object.DestroyImmediate(close.gameObject);

        // the tutorial: nobody shoots
        Fresh();
        EnemyThreat.ForceShooting = false;
        startMenu.youAreInTutorial = true;
        var tut = Build(EnemyRoster.Find("space_fighter_3"), new Vector2(0f, 1.8f));
        for (int i = 0; i < 60 * 8; i++) Step(tut);
        Check("in the tutorial (and wherever nothing steps the shots) no enemy fires", tut.Windups == 0 && tut.ShotsFired == 0);
        startMenu.youAreInTutorial = false;
        EnemyThreat.ForceShooting = true;
        Object.DestroyImmediate(tut.gameObject);
    }

    // ---- 9 -------------------------------------------------------------------

    static void ChasersDiffer()
    {
        var styles = new HashSet<ChaserStyle>();
        var paths = new List<float>();
        bool lancerAims = false, weaverWeaves = false, slitherSurges = false, slitherSways = false;
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
        {
            Fresh();
            ship.position = new Vector3(0f, 2f, 0f);
            var def = EnemyRoster.One(w, EnemyRole.Chaser);
            var go = EnemyFactory.Create(def, new Vector3(0f, -5f, 0f), Quaternion.identity);
            var c = go.GetComponent<ChaserEnemy>();
            c.Target = ship;
            styles.Add(c.style);
            float sideways = 0f, aimed = 0f, slowest = 99f, fastest = 0f;
            Vector3 last = go.transform.position;
            for (int i = 0; i < 120; i++)
            {
                c.Step(Dt);
                float climb = (go.transform.position.y - last.y) / Dt;
                if (i > 5) { slowest = Mathf.Min(slowest, climb); fastest = Mathf.Max(fastest, climb); }
                sideways += Mathf.Abs(go.transform.position.x - last.x);
                if (c.Aiming) aimed += Dt;
                last = go.transform.position;
            }
            paths.Add(go.transform.position.y);
            if (c.style == ChaserStyle.Lancer) lancerAims = aimed > .3f;
            if (c.style == ChaserStyle.Weaver) weaverWeaves = sideways > .5f;
            if (c.style == ChaserStyle.Slither) { slitherSways = sideways > .5f; slitherSurges = fastest > slowest * 2f && slowest < 2f; }
            Check(def.displayName + " (" + c.style + ") closes on a pilot straight above it (y -5 -> " + go.transform.position.y.ToString("F2") + ")",
                  go.transform.position.y > -4.9f && c.chaseSeconds == EnemyBehaviours.For(def.key).chaseSeconds);
            Object.DestroyImmediate(go);
        }
        Check("every world's chaser hunts in its own style (" + styles.Count + " styles over " + WorldManager.Worlds.Length + " worlds)", styles.Count == WorldManager.Worlds.Length);

        // they do not stay for ever: chase, linger, then climb out the top
        bool allLeft = true;
        float longest = 0f;
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
        {
            Fresh();
            ship.position = new Vector3(0f, -1f, 0f);
            var def = EnemyRoster.One(w, EnemyRole.Chaser);
            var go = EnemyFactory.Create(def, new Vector3(1f, -6f, 0f), Quaternion.identity);
            var c = go.GetComponent<ChaserEnemy>();
            c.Target = ship;
            float bound = c.chaseSeconds + c.lingerSeconds + 6f;
            bool sawLeaving = false;
            float lastY = -6f;
            for (int i = 0; i < 60 * 30 && c.enabled; i++) { c.Step(Dt); sawLeaving |= c.Leaving; if (c.enabled) lastY = go.transform.position.y; }
            longest = Mathf.Max(longest, c.SecondsAlive);
            allLeft &= !c.enabled && sawLeaving && c.SecondsAlive <= bound && lastY > CameraFit.ViewTop;
            Object.DestroyImmediate(go);
        }
        Check("every chaser leaves after its chase and linger, climbing out the top (longest stay " + longest.ToString("F1") + " s)", allLeft);
        Check("the Frost Lancer stops to aim between dashes", lancerAims);
        Check("the Dragonsting weaves sideways even at a pilot straight ahead", weaverWeaves);
        Check("the Wire Eel slithers: it surges and coils in sinusoidal lunges (climb speed swings > 2x) on an S-curve", slitherSurges && slitherSways);
    }

    // ---- 10 ------------------------------------------------------------------

    static void StunHoldsTheBrain()
    {
        Fresh();
        var brain = Build(EnemyRoster.Find("ember_fighter_3"), new Vector2(0f, 3f));   // Brand: strafes
        for (int i = 0; i < 30; i++) Step(brain);
        var held = new Behaviour[4];
        int n = HazardMovers.Disable(brain.gameObject, held, 0);
        Vector3 at = brain.transform.position;
        var state = brain.State;
        for (int i = 0; i < 120; i++) Step(brain);
        Check("a stunned enemy (its mover switched off) does not move or attack under its brain", n > 0 &&
              brain.transform.position == at && brain.State == state && brain.GetComponent<SpawnFootprint>().Held);
        for (int i = 0; i < n; i++) held[i].enabled = true;
        for (int i = 0; i < 60; i++) Step(brain);
        Check("... and carries on when released", brain.transform.position != at);
        Object.DestroyImmediate(brain.gameObject);
    }

    // ---- 11 ------------------------------------------------------------------

    // ---- allocation meter ---------------------------------------------------
    //
    // GC.GetAllocatedBytesForCurrentThread() reads 0 for everything under this
    // Unity Mono, which would make a "0 bytes" result meaningless. So the
    // meter is chosen by a POSITIVE CONTROL: the same loop shape with one small
    // allocation per brain per frame must read as allocating, or that meter is
    // not used. Candidates, in order: the thread counter; the profiler's
    // GC.Alloc recorder (managed bytes allocated); the Mono
    // heap's used size; GC.GetTotalMemory. (The collector cannot be switched
    // off in the editor, so the heap meters collect first and a collection
    // mid-run would read negative, never a false zero on the control.)

    static readonly string[] MeterNames =
    {
        "GC.GetAllocatedBytesForCurrentThread (bytes)", "ProfilerRecorder GC.Alloc (bytes)",
        "Profiler.GetMonoUsedSizeLong (bytes)", "GC.GetTotalMemory (bytes)",
    };
    // What the positive control must read at least, per meter (it makes 16,800 allocations of >= 32 bytes).
    static readonly long[] MeterFloor = { 100000, 8000, 100000, 100000 };
    static object controlSink;

    static long Measure(int meter, System.Action work)
    {
        switch (meter)
        {
            case 0:
            {
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                work();
                return System.GC.GetAllocatedBytesForCurrentThread() - before;
            }
            case 1:
            {
                using (var rec = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC.Alloc", 1,
                           Unity.Profiling.ProfilerRecorderOptions.SumAllSamplesInFrame | Unity.Profiling.ProfilerRecorderOptions.StartImmediately))
                {
                    if (!rec.Valid) return 0;
                    long before = rec.CurrentValue;
                    work();
                    return rec.CurrentValue - before;
                }
            }
            case 2:
            {
                System.GC.Collect();
                long before = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                work();
                return UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() - before;
            }
            default:
            {
                System.GC.Collect();
                long before = System.GC.GetTotalMemory(false);
                work();
                return System.GC.GetTotalMemory(false) - before;
            }
        }
    }

    const int ControlPerFrame = 28, ControlFrames = 300;

    // One 32-byte array and a short string per "brain" per frame: what a
    // careless per-frame allocation in the brain loop would look like.
    static void Control()
    {
        for (int i = 0; i < ControlFrames; i++)
            for (int k = 0; k < ControlPerFrame; k++)
            {
                controlSink = new byte[32];
                controlSink = "f" + i + k;
            }
    }

    static int PickMeter(out long controlBytes)
    {
        controlBytes = 0;
        for (int m = 0; m < MeterNames.Length; m++)
        {
            long bytes = Measure(m, Control);
            Debug.Log("[BRAIN] allocation meter candidate " + MeterNames[m] + ": positive control reads " + bytes + " bytes");
            if (bytes >= MeterFloor[m]) { controlBytes = bytes; return m; }
        }
        return -1;
    }

    static void AllocatesNothing()
    {
        long controlBytes;
        int meter = PickMeter(out controlBytes);
        Check("an allocation meter passes its positive control (" + (meter >= 0 ? MeterNames[meter] + ": " + controlBytes + " bytes for " +
              (ControlFrames * ControlPerFrame) + " small allocations" : "none of them sees the control") + ")", meter >= 0);
        if (meter < 0) return;

        Fresh();
        var brains = new List<EnemyBrain>();
        // every hazard and every pilot, spread out so they fly their scripts
        int n = 0;
        foreach (var def in EnemyRoster.All)
        {
            if (def.role == EnemyRole.Chaser || def.role == EnemyRole.Mine) continue;
            brains.Add(Build(def, new Vector2(-1.8f + (n % 4) * 1.2f, 1f + (n / 4) * .35f)));
            n++;
        }
        var chasers = new List<ChaserEnemy>();
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
        {
            var c = EnemyFactory.Create(EnemyRoster.One(w, EnemyRole.Chaser), new Vector3(-1.5f + w, -5.5f, 0f), Quaternion.identity).GetComponent<ChaserEnemy>();
            c.Target = ship;
            chasers.Add(c);
        }
        int pilots = 0;
        foreach (var b in brains) if (b.IsPilot) pilots++;
        System.Action frames = () =>
        {
            for (int i = 0; i < 300; i++)
            {
                clock += Dt;
                SpawnSpace.ClockOverride = clock;
                for (int k = 0; k < brains.Count; k++) brains[k].Step(Dt);
                for (int k = 0; k < chasers.Count; k++) if (chasers[k].enabled) chasers[k].Step(Dt);
                EliteSystem.Step(Dt);
                EnemyDensity.Threats();
                PilotAirspace.Blocks(-1f, 1f);
            }
        };
        // warm: pools built, statics initialised, first shots fired. Two passes:
        // pilots now hold in the player's reach (HostileReach) and fire on a
        // different rhythm, so the shot pool reaches its peak in the second
        // (a one-off growth of the pool, not a per-frame allocation)
        frames();
        frames();
        int shotsBefore = EliteSystem.Shots.Launched;
        long bytes = Measure(meter, frames);
        long again = Measure(meter, frames);
        Check(brains.Count + " brains (" + pilots + " pilots flying their scripts) and " + chasers.Count + " chasers moving, telling, firing (" +
              (EliteSystem.Shots.Launched - shotsBefore) + " shots) and sidestepping for 2 x 300 frames allocate nothing, by the meter that saw the " +
              "control (" + bytes + " and " + again + " bytes; control " + controlBytes + ")", bytes == 0 && again == 0 && pilots >= 20);
        foreach (var b in brains) if (b != null) Object.DestroyImmediate(b.gameObject);
        foreach (var c in chasers) if (c != null) Object.DestroyImmediate(c.gameObject);
    }

    // ---- external displacement (a shove) -------------------------------------

    static void ShovedEnemiesRecover()
    {
        // a hazard: its brain only adds its offset, so a shove stays
        Fresh();
        var rock = Build(EnemyRoster.Find("space_rock_cluster"), new Vector2(0f, 2f));
        for (int i = 0; i < 30; i++) Step(rock);
        Vector2 baseBefore = rock.Base;
        rock.transform.position += new Vector3(.8f, .5f, 0f);
        Check("a shoved hazard: its Base moves with the shove", ((rock.Base - baseBefore) - new Vector2(.8f, .5f)).magnitude < 1e-4f);
        for (int i = 0; i < 120; i++) Step(rock);
        Check("... and stays moved (the brain adds to its Base, it never snaps back)", ((rock.Base - baseBefore) - new Vector2(.8f, .5f)).magnitude < 1e-3f);
        rock.Base = baseBefore;
        Check("... and Base can be set", (rock.Base - baseBefore).magnitude < 1e-4f);
        Object.DestroyImmediate(rock.gameObject);

        // a pilot on station: pushed off its line, it flies back over time
        Fresh();
        float top = CameraFit.ViewTop;
        var pilot = Build(EnemyRoster.Find("space_fighter_4"), new Vector2(0f, top - 1.6f), armed: false);
        for (int i = 0; i < 90; i++) Step(pilot);
        Vector2 station = pilot.Base;
        Vector3 pushed = pilot.transform.position + new Vector3(1.2f, -1f, 0f);
        pilot.transform.position = pushed;
        Step(pilot);
        float firstStep = (pilot.transform.position - pushed).magnitude;
        Check("a shoved pilot does not snap back: one frame later it has moved " + firstStep.ToString("F3") + " u of the 1.56 u it was pushed",
              pilot.Displaced && firstStep <= EnemyBrain.ShoveReturnSpeed * Dt + 1e-3f && (pilot.Base - station).magnitude < 1e-4f);
        float secs = Dt, worstStep = 0f;
        Vector3 last = pilot.transform.position;
        while (pilot.Displaced && secs < 5f)
        {
            Step(pilot);
            worstStep = Mathf.Max(worstStep, (pilot.transform.position - last).magnitude);
            last = pilot.transform.position;
            secs += Dt;
        }
        Check("... it flies back to its line at no more than " + EnemyBrain.ShoveReturnSpeed + " u/s (" + secs.ToString("F2") + " s) and its station (Base) never moved",
              !pilot.Displaced && secs > .3f && secs < 2f && worstStep <= EnemyBrain.ShoveReturnSpeed * Dt + 1e-3f && (pilot.Base - station).magnitude < 1e-4f &&
              Mathf.Abs(pilot.transform.position.x - station.x) <= pilot.Behaviour.bandX + .05f);
        pilot.Base = station + new Vector2(.5f, 0f);
        for (int i = 0; i < 120; i++) Step(pilot);
        Check("setting a pilot's Base moves its station", Mathf.Abs(pilot.Base.x - (station.x + .5f)) < 1e-4f &&
              Mathf.Abs(pilot.transform.position.x - pilot.Base.x) <= pilot.Behaviour.bandX + .05f);
        Object.DestroyImmediate(pilot.gameObject);
    }

    // ---- presence --------------------------------------------------------------

    static void EveryKeyIsHazardOrPilot()
    {
        Fresh();
        var wrong = new List<string>();
        int hazards = 0, pilots = 0;
        foreach (var def in EnemyRoster.All)
        {
            var b = EnemyBehaviours.For(def.key);
            if (b == null) { wrong.Add(def.key); continue; }
            bool shouldFly = def.role != EnemyRole.Rock && def.role != EnemyRole.Mine;
            if (b.IsPilot) pilots++; else hazards++;
            var go = EnemyFactory.Create(def, new Vector3(0f, 40f, 0f), Quaternion.identity);
            var brain = go.GetComponent<EnemyBrain>();
            var scroller = go.GetComponent<moveItemEnmInStrightLine>();
            var weaver = go.GetComponent<moveEnimes>();
            bool station = (scroller != null && scroller.station) || (weaver != null && weaver.station);
            var f = go.GetComponent<SpawnFootprint>();
            bool ok = b.IsPilot == shouldFly;
            if (def.role == EnemyRole.Chaser) ok &= brain == null && go.GetComponent<ChaserEnemy>() != null;
            else ok &= brain.IsPilot == shouldFly && station == shouldFly && (f.Plan != null && f.Plan.SelfSteering) == shouldFly;
            if (b.IsPilot && def.role != EnemyRole.Chaser)
                ok &= b.engageSeconds >= 2f && b.engageSeconds <= 10f && b.stationDepth >= 1f && b.stationDepth <= 3.2f;
            if (!ok) wrong.Add(def.key);
            Object.DestroyImmediate(go);
        }
        Check("every roster key is a hazard or a pilot: rocks and rail mines ride the board (" + hazards + "), fighters, heavies, chasers and " +
              "aliens fly (" + pilots + "), and the factory builds them so (" + string.Join(",", wrong) + ")",
              wrong.Count == 0 && hazards + pilots == EnemyRoster.All.Length && pilots == 7 * EnemyRoster.WorldKeys.Length);

        EnemyBrain.PilotsEnabled = false;
        var off = EnemyFactory.Create(EnemyRoster.Fighter(0, 2), new Vector3(0f, 40f, 0f), Quaternion.identity);
        Check("with pilots switched off a fighter rides the scroll as before",
              !off.GetComponent<EnemyBrain>().IsPilot && !off.GetComponent<moveItemEnmInStrightLine>().station);
        Object.DestroyImmediate(off);
        EnemyBrain.PilotsEnabled = true;
    }

    static void HazardsRideTheScroll()
    {
        var bad = new List<string>();
        foreach (var def in EnemyRoster.All)
        {
            var b = EnemyBehaviours.For(def.key);
            if (b == null || b.IsPilot || def.role == EnemyRole.Mine) continue;
            foreach (float speed in new[] { .1f, .4f })
            {
                Fresh();
                moveBackGround.speed = speed;
                var brain = Build(def, new Vector2(0f, 6f));
                var mover = brain.GetComponent<moveEnimes>();
                float y0 = brain.transform.position.y;
                for (int i = 0; i < 60; i++) { mover.Step(Dt, clock); Step(brain); }
                float fell = y0 - brain.transform.position.y, scrolled = speed * 30f;
                if (Mathf.Abs(fell - scrolled) > b.Up + b.Down + .02f) bad.Add(def.key + "@" + speed + "(fell " + fell.ToString("F2") + " of " + scrolled.ToString("F2") + ")");
                Object.DestroyImmediate(brain.gameObject);
            }
        }
        Check("every rock still falls with the board: one second at HUD 10 and at HUD 40 carries it the scroll's distance, give or take its " +
              "own pattern (" + string.Join(", ", bad) + ")", bad.Count == 0);
    }

    // One pilot flown from its wait above the view until it has left.
    struct Flight
    {
        public float inView, total, worstColumn, worstLane, firstY, lastY, minY;
        public bool gone, enteredFromTop, unfairWindup;
        public PilotExit leftBy;
        public int windups, shots;
    }

    static Flight Fly(EnemyDef def, float speed, float shipX)
    {
        Fresh();
        moveBackGround.speed = speed;
        ship.position = new Vector3(shipX, -3f, 0f);
        float top = CameraFit.ViewTop, bottom = CameraFit.ViewBottom;
        var brain = Build(def, new Vector2(.2f, top + PilotAirspace.WaitAbove), armed: false);
        var b = brain.Behaviour;
        float lane = SpawnLane.LaneHalf - def.ColliderSize.x * .5f;
        var fl = new Flight { minY = 99f, firstY = float.NaN };
        bool wasAbove = true;
        int windups = 0;
        for (int i = 0; i < 60 * 40 && brain.Stage != EnemyBrain.PilotStage.Gone; i++)
        {
            ship.position = new Vector3(shipX + Mathf.Sin(i * .01f) * .8f, -3f, 0f);
            Vector3 was = brain.transform.position;
            Step(brain);
            if (brain.Stage == EnemyBrain.PilotStage.Gone) break;
            Vector3 at = brain.transform.position;
            fl.total += Dt;
            bool inView = at.y <= top && at.y >= bottom;
            if (inView)
            {
                fl.inView += Dt;
                if (float.IsNaN(fl.firstY)) { fl.firstY = was.y; fl.enteredFromTop = wasAbove && was.y >= top - .2f; }
            }
            else if (float.IsNaN(fl.firstY)) wasAbove = at.y > top;
            fl.lastY = at.y;
            fl.minY = Mathf.Min(fl.minY, at.y);
            fl.worstColumn = Mathf.Max(fl.worstColumn, Mathf.Abs(at.x - brain.Anchor.x) - b.bandX);
            fl.worstLane = Mathf.Max(fl.worstLane, Mathf.Abs(at.x) - lane);
            if (brain.Windups > windups)
            {
                // a windup just began: only on station, in view, well above the pilot
                windups = brain.Windups;
                fl.unfairWindup |= brain.Stage != EnemyBrain.PilotStage.Engaging || at.y > top - EnemyBrain.ViewInset + .05f ||
                                   at.y - ship.position.y < EnemyBrain.MinFireAbove - .05f;
            }
        }
        fl.gone = brain.Stage == EnemyBrain.PilotStage.Gone;
        fl.leftBy = brain.LeftBy;
        fl.windups = brain.Windups;
        fl.shots = brain.ShotsFired;
        Object.DestroyImmediate(brain.gameObject);
        return fl;
    }

    static void PilotsFlyTheirScripts()
    {
        var bad = new List<string>();
        int flown = 0, shooters = 0, shootersThatFiredFast = 0;
        float shortest = 99f, longest = 0f, worstSpread = 0f;
        float top = CameraFit.ViewTop, bottom = CameraFit.ViewBottom;
        foreach (var def in EnemyRoster.All)
        {
            var b = EnemyBehaviours.For(def.key);
            if (b == null || !b.IsPilot || def.role == EnemyRole.Chaser) continue;
            flown++;
            Random.InitState(1234);
            var slow = Fly(def, .05f, .4f);
            Random.InitState(1234);
            var fast = Fly(def, .40f, .4f);
            float spread = Mathf.Abs(slow.inView - fast.inView);
            worstSpread = Mathf.Max(worstSpread, spread);
            shortest = Mathf.Min(shortest, Mathf.Min(slow.inView, fast.inView));
            longest = Mathf.Max(longest, Mathf.Max(slow.inView, fast.inView));
            bool exitOk = true;
            foreach (var fl in new[] { slow, fast })
            {
                bool down = fl.leftBy == PilotExit.Run;
                exitOk &= fl.gone && fl.enteredFromTop && (down ? fl.lastY < bottom : fl.lastY > top) &&
                          fl.worstColumn <= .02f && fl.worstLane <= .02f && !fl.unfairWindup;
                // a retreating pilot never came down to the ship's rows
                if (!down && b.entry != PilotEntry.Descend) exitOk &= fl.minY > 0f;
            }
            bool expectDown = b.entry == PilotEntry.Descend || b.exit == PilotExit.Run;
            exitOk &= (slow.leftBy == PilotExit.Run) == expectDown;
            if (b.Shoots && b.armedChance >= 1f) { shooters++; if (fast.shots > 0) shootersThatFiredFast++; }
            if (!exitOk || spread > .25f || slow.inView < 1.5f || slow.inView > 14f)
                bad.Add(def.key + "(slow " + slow.inView.ToString("F1") + "s fast " + fast.inView.ToString("F1") + "s gone " + slow.gone + "/" + fast.gone +
                        " top " + slow.enteredFromTop + " col " + fast.worstColumn.ToString("F2") + " lane " + fast.worstLane.ToString("F2") +
                        " unfair " + (slow.unfairWindup || fast.unfairWindup) + " last " + fast.lastY.ToString("F1") + ")");
        }
        Check(flown + " pilots flown at HUD 5 and at HUD 40: each enters from the top, keeps to its column and the lane, starts every " +
              "windup on station in view above the pilot, and leaves by the top or the bottom as its script says (" + string.Join(", ", bad) + ")",
              bad.Count == 0 && flown == 6 * EnemyRoster.WorldKeys.Length);
        Check("a pilot's time on screen does not depend on the scroll speed (worst difference HUD 5 vs 40: " + worstSpread.ToString("F2") + " s)",
              worstSpread <= .25f);
        // (13 s before HostileReach: a heavy holding in the player's reach climbs
        // ~1.3 u further out of the view at its slow exit speed -- frost_big 13.3 s)
        Check("... and is bounded: " + shortest.ToString("F1") + " s to " + longest.ToString("F1") + " s in view", shortest >= 1.5f && longest <= 14f);
        Check("at HUD 40 every shooting pilot gets its shots off (" + shootersThatFiredFast + " of " + shooters + ")",
              shooters >= 12 && shootersThatFiredFast == shooters);

        // tiers: the higher the tier, the longer it stays and the more it does
        bool tiers = true;
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
        {
            var t1 = EnemyRoster.Fighter(w, 1).Behaviour; var t2 = EnemyRoster.Fighter(w, 2).Behaviour;
            var t3 = EnemyRoster.Fighter(w, 3).Behaviour; var t4 = EnemyRoster.Fighter(w, 4).Behaviour;
            tiers &= t1.engageSeconds < t2.engageSeconds && t2.engageSeconds < t3.engageSeconds && t3.engageSeconds < t4.engageSeconds &&
                     t1.exit == PilotExit.Run && t4.exit == PilotExit.Climb && t4.Shoots && t4.maxVolleys >= 3 && t4.stationDepth <= 1.7f &&
                     EnemyRoster.One(w, EnemyRole.Big).Behaviour.entrySpeed < 2f;
        }
        Check("tiers escalate in every world: 1 makes one pass and runs, each tier stays longer, 4 holds range at the top with several volleys " +
              "and retreats; heavies arrive slowly", tiers);

        // an escape pays nothing and leaves the kill chain alone
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(true, false);
        long before = RunScore.Total;
        int chain = RunScore.Chain;
        var gone = Fly(EnemyRoster.Fighter(0, 1), .2f, 0f);
        Check("a pilot that escapes pays nothing and does not touch the kill chain", gone.gone && RunScore.Total == before && RunScore.Chain == chain &&
              PilotAirspace.Escaped >= 1);
        RunScore.EndRun(RunScore.RunId);
    }

    static void PilotAttackRuns()
    {
        var bad = new List<string>();
        int runners = 0;
        foreach (var def in EnemyRoster.All)
        {
            var b = EnemyBehaviours.For(def.key);
            if (b == null || !b.IsPilot || def.role == EnemyRole.Chaser || b.entry == PilotEntry.Descend || b.exit != PilotExit.Run) continue;
            runners++;
            Fresh();
            ship.position = new Vector3(.5f, -3f, 0f);
            var brain = Build(def, new Vector2(0f, CameraFit.ViewTop + 1f), armed: false);
            var fb = brain.GetComponent<EnemyFlipbook>();
            float told = 0f, yAtExit = float.NaN;
            bool tellCell = true, releaseCell = false, held = true;
            for (int i = 0; i < 60 * 30 && brain.Stage != EnemyBrain.PilotStage.Gone; i++)
            {
                Step(brain);
                if (brain.Stage != EnemyBrain.PilotStage.Exiting) continue;
                float y = brain.Anchor.y;
                if (float.IsNaN(yAtExit)) yAtExit = y;
                if (Mathf.Abs(y - yAtExit) < 1e-4f)
                {
                    told += Dt;                                      // still holding: the tell
                    tellCell &= fb.CurrentFrame == EnemyRoster.TellFrame;
                }
                else
                {
                    held &= y < yAtExit;                             // then only ever down
                    releaseCell |= fb.CurrentFrame == EnemyRoster.TellFrame + 1;
                }
            }
            bool ok = brain.Stage == EnemyBrain.PilotStage.Gone && brain.LeftBy == PilotExit.Run &&
                      told >= Mathf.Max(EnemyBrain.TellFloorSeconds, EnemyBrain.RunTellSeconds) - 2f * Dt && tellCell && releaseCell && held;
            if (!ok) bad.Add(def.key + "(told " + told.ToString("F2") + " tellCell " + tellCell + " release " + releaseCell + ")");
            Object.DestroyImmediate(brain.gameObject);
        }
        Check("every attack run is told first: the pilot holds its tell cell for at least " + EnemyBrain.RunTellSeconds + " s, then dives straight " +
              "out the bottom showing its release cell (" + runners + " runners; " + string.Join(", ", bad) + ")", bad.Count == 0 && runners >= 8);
    }

    static void AirspaceRoutesHazardsRoundPilots()
    {
        Fresh();
        float top = CameraFit.ViewTop;
        var def = EnemyRoster.Fighter(0, 2);
        var pilot = Build(def, new Vector2(.3f, top + PilotAirspace.WaitAbove), armed: false);
        float half = pilot.ColumnHalf;
        Check("a pilot reserves its column: its body and a margin (" + (2f * half).ToString("F2") + " u wide)",
              Mathf.Abs(half - (SpawnSpace.BodyHalf(def).x + PilotAirspace.ColumnMargin)) < 1e-4f &&
              PilotAirspace.Blocks(.3f - half + .05f, .3f + half - .05f) && PilotAirspace.Blocks(.3f + half - .1f, 2f) &&
              !PilotAirspace.Blocks(.3f + half + .01f, 2.3f) && !PilotAirspace.Blocks(-2.3f, .3f - half - .01f));

        // a rock already in its column: the pilot waits above the view for it to go by
        var rockDef = EnemyRoster.Find("ember_rock_cinder");
        var rock = EnemyFactory.Create(rockDef, new Vector3(.3f, top + 1f, 0f), Quaternion.identity);
        Check("a rock coming down its column: the column is not clear", !PilotAirspace.ColumnClear(pilot, top - def.Behaviour.stationDepth));
        for (int i = 0; i < 60; i++) Step(pilot);
        Check("... so the pilot waits above the view", pilot.Stage == EnemyBrain.PilotStage.Waiting && pilot.transform.position.y > top);
        rock.transform.position = new Vector3(.3f, top - def.Behaviour.stationDepth - 3f, 0f);   // it has scrolled past the station
        Check("... until the rock has gone by", PilotAirspace.ColumnClear(pilot, top - def.Behaviour.stationDepth));
        for (int i = 0; i < 5; i++) Step(pilot);
        Check("... then it comes in", pilot.Stage == EnemyBrain.PilotStage.Entering || pilot.Stage == EnemyBrain.PilotStage.Engaging);
        Object.DestroyImmediate(rock);

        // a hazard coming down BESIDE its column, inside its band: the pilot sidesteps
        // back toward its column while it passes, then uses its whole band again
        Fresh();
        var sweeper = Build(EnemyRoster.Fighter(0, 3), new Vector2(0f, top - 2f), armed: false);   // Twin Claw: a wide sweep
        for (int i = 0; i < 120; i++) Step(sweeper);
        float bandX = sweeper.Behaviour.bandX, reachFree = 0f, reachBeside = 0f;
        for (int i = 0; i < 240; i++) { Step(sweeper); reachFree = Mathf.Max(reachFree, sweeper.transform.position.x - sweeper.Anchor.x); }
        // (beside it, just above where it holds -- in the player's reach now, HostileReach --
        // and clear of its body however its pattern carries it: not dropped onto it)
        float besideY = sweeper.Anchor.y + Mathf.Max(.4f, EnemyBrain.PatternUp(sweeper.Behaviour) + SpawnSpace.BodyHalf(sweeper.Def).y + SpawnSpace.BodyHalf(rockDef).y + .05f);
        var beside = EnemyFactory.Create(rockDef, new Vector3(sweeper.ColumnHalf + SpawnSpace.BodyHalf(rockDef).x + .15f, besideY, 0f), Quaternion.identity);
        bool overlap = false;
        int besideFrames = 0;
        string firstOverlap = "";
        for (int i = 0; i < 240; i++)
        {
            Step(sweeper);
            // (the sidestep is judged while it holds beside the hazard, after half a
            // second to step back: once its stay is over it climbs out past the
            // rock -- the rock behind it now -- and sweeps freely again)
            if (i >= 30 && sweeper.Stage == EnemyBrain.PilotStage.Engaging)
            {
                besideFrames++;
                reachBeside = Mathf.Max(reachBeside, sweeper.transform.position.x - sweeper.Anchor.x);
            }
            bool o = sweeper.GetComponent<SpawnFootprint>().Body.Overlaps(beside.GetComponent<SpawnFootprint>().Body);
            if (o && !overlap) firstOverlap = " (touched at frame " + i + ", " + sweeper.Stage + " at " + sweeper.transform.position + ", anchor " + sweeper.Anchor + ", rock " + beside.transform.position + ")";
            overlap |= o;
        }
        Check("a pilot uses its whole band when the lane beside it is empty (" + reachFree.ToString("F2") + " of " + bandX + " u) and sidesteps " +
              "back toward its column while a hazard passes there (" + reachBeside.ToString("F2") + " u over " + besideFrames + " frames), never touching it" + firstOverlap,
              reachFree > bandX * .9f && besideFrames >= 20 && reachBeside < .3f && !overlap);
        Object.DestroyImmediate(beside);
        Object.DestroyImmediate(sweeper.gameObject);

        // never for ever: a column that never clears is entered anyway after MaxWaitSeconds
        Fresh();
        pilot = Build(def, new Vector2(.3f, top + PilotAirspace.WaitAbove), armed: false);
        var wall = EnemyFactory.Create(rockDef, new Vector3(.3f, top + 1f, 0f), Quaternion.identity);
        int frames = 0;
        while (pilot.Stage == EnemyBrain.PilotStage.Waiting && frames < 60 * 20) { Step(pilot); frames++; }
        Check("a pilot never waits longer than " + EnemyBrain.MaxWaitSeconds + " s for its column (" + (frames * Dt).ToString("F1") + " s)",
              frames * Dt <= EnemyBrain.MaxWaitSeconds + .1f && frames * Dt >= EnemyBrain.MaxWaitSeconds - .1f);
        // ... and it still never steps into the rock (SpawnSpace.ResolveSteer)
        bool touched = false;
        for (int i = 0; i < 240; i++)
        {
            Step(pilot);
            touched |= pilot.GetComponent<SpawnFootprint>().Body.Overlaps(wall.GetComponent<SpawnFootprint>().Body);
        }
        Check("... and even then it never steps into the hazard in its way", !touched);
        Object.DestroyImmediate(wall);

        // the real spawner: with a pilot on station, no rock is placed down its column
        Fresh();
        pilot = Build(def, new Vector2(.3f, top - def.Behaviour.stationDepth), armed: false);
        var board = new GameObject("~BrainTestBoard").AddComponent<enmiesOnBoard>();
        board.transform.position = new Vector3(0f, top + 2f, 0f);
        board.SendMessage("Start");
        Random.InitState(55);
        var slot = typeof(enmiesOnBoard).GetMethod("spawnAstroid2", BindingFlags.NonPublic | BindingFlags.Instance);
        int rocks = 0, inColumn = 0;
        for (int i = 0; i < 80; i++)
        {
            slot.Invoke(board, null);
            foreach (var f in new List<SpawnFootprint>(SpawnSpace.Live(SpawnLayer.Enemy)))
            {
                if (f == null || f.gameObject == pilot.gameObject) continue;
                rocks++;
                Rect e = f.Sweep(0f, SpawnSpace.Lifetime);
                if (e.xMin < .3f + half && e.xMax > .3f - half) inColumn++;
                Object.DestroyImmediate(f.gameObject);
            }
        }
        Check("the spawner routes hazards round a pilot: " + rocks + " rocks placed, " + inColumn + " with any part of their pattern in its column",
              rocks >= 40 && inColumn == 0);
        Object.DestroyImmediate(board.gameObject);
    }

    static void PilotBudgetHolds()
    {
        float lane = 2f * SpawnLane.LaneHalf;
        bool ok = true;
        float worstLoad = 0f, worstShare = 0f;
        int most = 0;
        foreach (float hud in new[] { 5f, 20f, 35f, 44f })
            for (int w = 0; w < WorldManager.Worlds.Length; w++)
            {
                Fresh();
                moveBackGround.speed = hud / 100f;
                Random.InitState(900 + w + (int)hud);
                var made = new List<GameObject>();
                for (int i = 0; i < 60; i++)
                {
                    // whatever the spawner might ask for, over and over
                    EnemyDef def = i % 5 == 0 ? EnemyRoster.One(w, EnemyRole.Big) : i % 5 == 1 ? EnemyRoster.One(w, EnemyRole.Alien)
                                 : EnemyRoster.Fighter(w, 1 + i % 4);
                    float x;
                    if (!PilotAirspace.TryAdmit(def, def.Behaviour, float.NaN, out x)) continue;
                    made.Add(EnemyFactory.Create(def, new Vector3(x, 9f, 0f), Quaternion.identity));
                    float load = PilotAirspace.Load, cap = EnemyDensity.MaxPilotLoad(hud, w);
                    ok &= load <= cap + 1e-3f;
                    worstLoad = Mathf.Max(worstLoad, load);
                    if (PilotAirspace.Count > 1)
                    {
                        ok &= PilotAirspace.ReservedWidth <= PilotAirspace.MaxReservedShare * lane + 1e-3f;
                        worstShare = Mathf.Max(worstShare, PilotAirspace.ReservedWidth / lane);
                    }
                }
                // no two columns overlap, none reaches the rail hardware
                var live = PilotAirspace.Live;
                most = Mathf.Max(most, live.Count);
                for (int i = 0; i < live.Count; i++)
                {
                    ok &= Mathf.Abs(live[i].Anchor.x) + live[i].ColumnHalf <= PilotAirspace.RailClear + 1e-3f;
                    for (int j = i + 1; j < live.Count; j++)
                        ok &= Mathf.Abs(live[i].Anchor.x - live[j].Anchor.x) >= live[i].ColumnHalf + live[j].ColumnHalf - 1e-3f;
                }
                ok &= made.Count >= 1;
                foreach (var go in made) Object.DestroyImmediate(go);
            }
        Check("however often the spawner asks, the pilot load stays inside its cap (worst " + worstLoad.ToString("F1") + "), reserved columns never " +
              "overlap or reach the rails, and never take more than " + PilotAirspace.MaxReservedShare.ToString("P0") + " of the lane (worst " +
              worstShare.ToString("P0") + ", " + most + " pilots at most)", ok && most >= 2);
        Check("the cap falls with speed in every world and is never below one pilot",
              EnemyDensity.MaxPilotLoad(35f, 0) < EnemyDensity.MaxPilotLoad(5f, 0) && EnemyDensity.MaxPilotLoad(50f, 0) >= 1.5f &&
              EnemyDensity.MaxPilotLoad(5f, 3) >= EnemyDensity.MaxPilotLoad(5f, 0) && EnemyDensity.MaxChasers(40f) <= EnemyDensity.MaxChasers(5f));
    }

    static void PilotsClearForBossAndPortal()
    {
        // ordered out in every stage: waiting, entering, on station mid-windup
        Fresh();
        float top = CameraFit.ViewTop;
        var def = EnemyRoster.Find("space_fighter_4");   // Warden: 9 s on station if left alone
        var waiting = Build(def, new Vector2(-1.2f, top + PilotAirspace.WaitAbove));
        waiting.Order();
        Step(waiting);
        Check("a pilot still waiting above the view simply never comes in", waiting.Stage == EnemyBrain.PilotStage.Gone);

        var entering = Build(def, new Vector2(1.2f, top + 1f));
        for (int i = 0; i < 10; i++) Step(entering);
        bool wasEntering = entering.Stage == EnemyBrain.PilotStage.Entering;
        entering.Order();
        float secs = 0f;
        while (entering.Stage != EnemyBrain.PilotStage.Gone && secs < 20f) { Step(entering); secs += Dt; }
        Check("a pilot on its way in turns round and climbs out (" + secs.ToString("F1") + " s)", wasEntering &&
              entering.Stage == EnemyBrain.PilotStage.Gone && entering.LeftBy == PilotExit.Climb && secs < 4f);

        Fresh();
        var fighting = Build(def, new Vector2(0f, top - 1.6f));
        for (int i = 0; i < 60 * 6 && fighting.State != EnemyBrain.Phase.Windup; i++) Step(fighting);
        bool midWindup = fighting.State == EnemyBrain.Phase.Windup;
        fighting.Order();
        secs = 0f;
        float lowest = 99f;
        while (fighting.Stage != EnemyBrain.PilotStage.Gone && secs < 20f) { Step(fighting); secs += Dt; lowest = Mathf.Min(lowest, fighting.transform.position.y); }
        Check("a pilot ordered out mid-windup finishes that attack, then climbs out the top instead of staying its 9 s (" + secs.ToString("F1") + " s)",
              midWindup && fighting.Stage == EnemyBrain.PilotStage.Gone && fighting.LeftBy == PilotExit.Climb && secs < 5f && lowest > 0f);

        // an open portal: admissions close, pilots and chasers leave by themselves
        Fresh();
        var wmGo = new GameObject("~BrainTestWorld");
        var wm = wmGo.AddComponent<WorldManager>();
        SetWorldManager(wm);
        try
        {
            Check("no portal, no boss: the airspace is open", !PilotAirspace.MustClear);
            var onStation = Build(def, new Vector2(0f, top - 1.6f));
            for (int i = 0; i < 30; i++) Step(onStation);
            var chaserGo = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Chaser), new Vector3(1.5f, -2f, 0f), Quaternion.identity);
            var chaser = chaserGo.GetComponent<ChaserEnemy>();
            chaser.Target = ship;
            chaser.Step(Dt);
            typeof(WorldManager).GetField("portalOpen", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(wm, true);
            float x;
            Check("a portal opens: the airspace must clear and admits nobody",
                  PilotAirspace.MustClear && PilotAirspace.AdmissionClosed && !PilotAirspace.TryAdmit(def, def.Behaviour, 1f, out x));
            secs = 0f;
            while ((onStation.Stage != EnemyBrain.PilotStage.Gone || chaser.enabled) && secs < 20f) { Step(onStation); chaser.Step(Dt); secs += Dt; }
            Check("... the pilot on station and the chaser both leave by the top within seconds (" + secs.ToString("F1") + " s)",
                  onStation.Stage == EnemyBrain.PilotStage.Gone && onStation.Ordered && !chaser.enabled && chaser.Leaving && secs < 6f);
            Object.DestroyImmediate(chaserGo);
        }
        finally
        {
            SetWorldManager(null);
            Object.DestroyImmediate(wmGo);
        }

        // the boss's board clear takes whatever is left, pilots included (the existing rule)
        var left = Build(def, new Vector2(0f, top - 1.6f));
        Check("a pilot is an ordinary hazard to the boss's board clear (ClearTarget, tagged)", left.GetComponent<ClearTarget>() != null &&
              ClearTarget.IsHazard(left.gameObject) && File.ReadAllText("Assets/Scripts/Bosses/BossEncounter.cs").Contains("ClearTarget.IsHazard(t.gameObject)"));
        Object.DestroyImmediate(left.gameObject);
    }
}
