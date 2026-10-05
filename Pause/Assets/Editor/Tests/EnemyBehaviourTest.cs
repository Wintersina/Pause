using System.Collections.Generic;
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
//   - shots are roster shots (no friendly fire), never the player's red,
//     and the shot budget holds
//   - the four chasers hunt differently
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
            StunHoldsTheBrain();
            AllocatesNothing();
        }
        finally
        {
            EnemyThreat.ForceShooting = false;
            EnemyThreat.Reset();
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
        EnemyThreat.Reset();
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

    static void Clear()
    {
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
                if (EliteSystem.Shots.Launched > launched)
                {
                    fired = true;
                    firedFromIdle = before != EnemyBrain.Phase.Windup;
                    releaseCell = fb.CurrentFrame == EnemyRoster.TellFrame + 1 && brain.State == EnemyBrain.Phase.Release;
                }
            }
            float need = Mathf.Max(EnemyBrain.TellFloorSeconds, b.tell);
            shortest = Mathf.Min(shortest, told);
            bool ok = fired && !firedFromIdle && told >= need - 2f * Dt && brain.LastTellSeconds >= need - 1e-4f &&
                      lightOn && tellCellOk && releaseCell && brain.ShotsFired >= 1 && fb.BrainDriven;
            if (!ok)
                bad.Add(def.key + "(fired " + fired + " told " + told.ToString("F2") + "/" + need.ToString("F2") + " light " + lightOn +
                        " tellCell " + tellCellOk + " releaseCell " + releaseCell + ")");
            Object.DestroyImmediate(brain.gameObject);
            if (rail != null) Object.DestroyImmediate(rail);
        }
        Check("every shooter holds its tell cell with the charge light on for its full tell, then shows the release cell as it fires (" +
              shooters + " shooters; " + string.Join(", ", bad) + ")", bad.Count == 0 && shooters >= 12);
        Check("no tell is shorter than the floor (" + shortest.ToString("F2") + " s >= " + EnemyBrain.TellFloorSeconds + ")",
              shortest >= EnemyBrain.TellFloorSeconds - 2f * Dt);

        // the lungers telegraph the same way
        var badLunge = new List<string>();
        foreach (var def in EnemyRoster.All)
        {
            var b = EnemyBehaviours.For(def.key);
            if (b == null || b.attack != EnemyAttack.Lunge) continue;
            Fresh();
            ship.position = new Vector3(.6f, -3f, 0f);
            var brain = Build(def, new Vector2(0f, 1.8f));
            float told = 0f;
            bool lunged = false;
            Vector2 before = brain.Offset;
            for (int i = 0; i < 60 * 10 && !lunged; i++)
            {
                Step(brain);
                if (brain.State == EnemyBrain.Phase.Windup) told += Dt;
                if (brain.State == EnemyBrain.Phase.Release) lunged = true;
            }
            for (int i = 0; i < 40; i++) Step(brain);
            bool ok = lunged && told >= Mathf.Max(EnemyBrain.TellFloorSeconds, b.tell) - 2f * Dt && brain.ShotsFired == 0;
            if (b.lungeDive > 0f) ok &= brain.Offset.y <= before.y - b.lungeDive * .6f || brain.Offset.y <= -b.lungeDive + .05f;
            if (!ok) badLunge.Add(def.key + "(told " + told.ToString("F2") + ", offset " + brain.Offset + ")");
            Object.DestroyImmediate(brain.gameObject);
        }
        Check("every lunger winds up before it dashes, and fires nothing (" + string.Join(", ", badLunge) + ")", badLunge.Count == 0);
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
        Check("the shell rides the board and never hurts other hazards (roster shot)", shot.RosterShot && shot.Ride > 0f);

        // a rock in the shell's path is untouched: no friendly fire
        var rock = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Rock), shot.transform.position, Quaternion.identity);
        for (int i = 0; i < 20; i++) EliteSystem.Step(Dt);
        Check("a rock sitting on the shell's path is not destroyed by it", rock != null && rock.GetComponent<ClearTarget>().enabled);
        Object.DestroyImmediate(rock);
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
            if (b == null || def.role == EnemyRole.Chaser || def.role == EnemyRole.Mine) continue;
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
              "the lane, and its SpawnSpace sweep covers it (" + checkedCount + " enemies; " + string.Join(", ", bad) + ")",
              bad.Count == 0 && checkedCount >= 35);
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
        for (int i = 0; i < 6; i++) brains.Add(Build(EnemyRoster.Find("ember_fighter_4"), new Vector2(-2f + i * .8f, 1.4f + (i % 2) * .6f)));
        int peak = 0, sameFrameVolleys = 0;
        for (int i = 0; i < 60 * 8; i++)
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
        Check("six ring-shooters together never pass the shot budget (peak " + peak + " <= " + EnemyThreat.MaxEnemyShots + ")",
              peak <= EnemyThreat.MaxEnemyShots && peak > 0);
        Check("... and no two of them begin a windup in the same frame (volley gap " + EnemyThreat.VolleyGap + " s)", sameFrameVolleys == 0);
        foreach (var b in brains) Object.DestroyImmediate(b.gameObject);

        // no fire from off screen, from below the pilot, or point blank
        Fresh();
        var high = Build(EnemyRoster.Find("space_fighter_3"), new Vector2(0f, CameraFit.ViewTop + 1f));
        var low = Build(EnemyRoster.Find("space_fighter_3"), new Vector2(1.5f, -4.2f));
        var close = Build(EnemyRoster.Find("space_fighter_3"), new Vector2(-.2f, -1.9f));
        for (int i = 0; i < 60 * 8; i++) { Step(high); low.Step(Dt); close.Step(Dt); }
        Check("a shooter above the view, below the pilot or point blank never starts a windup",
              high.Windups == 0 && low.Windups == 0 && close.Windups == 0);
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
        bool lancerAims = false, weaverWeaves = false;
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
        {
            Fresh();
            ship.position = new Vector3(0f, 2f, 0f);
            var def = EnemyRoster.One(w, EnemyRole.Chaser);
            var go = EnemyFactory.Create(def, new Vector3(0f, -5f, 0f), Quaternion.identity);
            var c = go.GetComponent<ChaserEnemy>();
            c.Target = ship;
            styles.Add(c.style);
            float sideways = 0f, aimed = 0f;
            Vector3 last = go.transform.position;
            for (int i = 0; i < 120; i++)
            {
                c.Step(Dt);
                sideways += Mathf.Abs(go.transform.position.x - last.x);
                if (c.Aiming) aimed += Dt;
                last = go.transform.position;
            }
            paths.Add(go.transform.position.y);
            if (c.style == ChaserStyle.Lancer) lancerAims = aimed > .3f;
            if (c.style == ChaserStyle.Weaver) weaverWeaves = sideways > .5f;
            Check(def.displayName + " (" + c.style + ") closes on a pilot straight above it (y -5 -> " + go.transform.position.y.ToString("F2") + ")",
                  go.transform.position.y > -4.9f && c.chaseSeconds == EnemyBehaviours.For(def.key).chaseSeconds);
            Object.DestroyImmediate(go);
        }
        Check("the four worlds' chasers hunt in four different styles", styles.Count == 4);
        Check("the Frost Lancer stops to aim between dashes", lancerAims);
        Check("the Dragonsting weaves sideways even at a pilot straight ahead", weaverWeaves);
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

    static void AllocatesNothing()
    {
        Fresh();
        var brains = new List<EnemyBrain>();
        foreach (var def in EnemyRoster.All)
        {
            if (def.role == EnemyRole.Chaser || def.role == EnemyRole.Mine) continue;
            brains.Add(Build(def, new Vector2(Random.Range(-1.5f, 1.5f), Random.Range(0f, 4f))));
        }
        long bytes = 0;
        for (int warm = 0; warm < 2; warm++)
        {
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 300; i++)
            {
                clock += Dt;
                SpawnSpace.ClockOverride = clock;
                for (int k = 0; k < brains.Count; k++) brains[k].Step(Dt);
                EliteSystem.Step(Dt);
            }
            bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Check(brains.Count + " brains moving, telling and firing for 300 frames allocate nothing (" + bytes + " bytes)", bytes == 0);
        foreach (var b in brains) Object.DestroyImmediate(b.gameObject);
    }
}
