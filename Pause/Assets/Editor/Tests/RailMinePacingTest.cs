using UnityEditor.SceneManagement;
using UnityEngine;

// "The rail mines at high speed fall past too fast, they should ride up the
// rails to keep up with the player, fire 2 lasers, then fall behind."
//
// For every world, both rails, at a low, a mid and the maximum game speed
// (the board falls speed x 30 u/s) a mine comes down the board from above the
// view with the ship low on it, and:
//   RIDE      once it reaches the hold row it stays within HoldBand of
//             ship.y + HoldAboveShip for the whole sequence, its slide
//             against the board never beyond RideSpeedCap + the board's
//             speed, and it is still on its rail (x) with its lane alive
//   SHOTS     exactly RailMineLaser.ShotsPerRide lasers fire (two separate
//             beams, both while it rides), the same at every speed
//   LEAVE     then it is released and falls away with the board: strictly
//             down each frame, out of the view, no third beam, never gone
//             early (no vanishing)
//   FREEZE    a world that is not stepping (pause, a transition) leaves it put
//   ROOM      a riding mine moves against the board only into free room: a rock
//             the board brings down beside its rail never ends up inside it
public static class RailMinePacingTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[MINEPACE] PASS  " : "[MINEPACE] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;
    static float clock;
    static Transform ship;
    static readonly float[] Speeds = { .08f, .2f, .35f };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            for (int w = 0; w < 4; w++)
                foreach (bool right in new[] { false, true })
                    foreach (float speed in Speeds)
                        Ride(w, right, speed, false);
            Ride(0, true, .35f, true);
            Ride(1, false, .2f, true);
            Freeze();
            foreach (float speed in Speeds) Room(speed);
        }
        finally
        {
            EnemyThreat.ForceShooting = false;
            EnemyThreat.Reset();
            RailMineLaser.AngleOverride = null;
            SpawnSpace.ClockOverride = null;
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
            moveBackGround.speed = 0f;
            buttonClicks.playerDied = false;
        }
        Debug.Log("[MINEPACE] failures: " + fails);
        return fails;
    }

    static void Fresh(float speed)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EliteSystem.Clear();
        EnemyThreat.Reset();
        EnemyThreat.ForceShooting = true;
        RailMineLaser.AngleOverride = null;
        RailMineLaser.Seed(12345u);
        BossRails.Reset();
        ScreenInfo.ClearOverride();
        FriendlyFire.ResetCounters();
        FriendlyFire.OnSceneLoaded("gameS1");
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        startMenu.youAreInTutorial = false;
        moveBackGround.speed = speed;
        Time.timeScale = 1f;
        clock = 100f;
        SpawnSpace.ClockOverride = clock;
        ship = new GameObject("~PaceShip").transform;
        ship.position = new Vector3(0f, -3f, 0f);
        EliteSystem.PlayerOverride = ship;
    }

    static void Ride(int world, bool right, float speed, bool shipMoves)
    {
        Fresh(speed);
        var def = EnemyRoster.One(world, EnemyRole.Mine);
        float x = enmiesOnBoard.WorldRailX(!right);
        var rail = new GameObject("RailMineLane");
        rail.transform.position = new Vector3(x, 6f, 0f);
        var scroller = rail.AddComponent<RailLaneScroller>();
        var go = EnemyFactory.Create(def, new Vector3(x, 6f, 0f), Quaternion.identity);
        var brain = go.GetComponent<EnemyBrain>();
        var mount = go.AddComponent<RailMineMount>();
        mount.MountTo(rail.transform);
        mount.brain = brain;
        brain.TargetOverride = ship;

        float scroll = speed * BoardRoll.BoardScroll;
        string tag = EnemyRoster.WorldKeys[world] + (right ? " right" : " left") + " @" + speed.ToString("F2") +
                     " (" + scroll.ToString("F1") + " u/s)" + (shipMoves ? " ship moving" : "") + ": ";
        int beams = 0, framesRiding = 0;
        bool prevBeam = false, beamsInBand = true, inBand = true, onRail = true, laneAlive = true, everRode = false;
        float worstBand = 0f, worstRel = 0f, prevY = go.transform.position.y, releasedY = float.NaN;
        bool fallsMonotonic = true, endedBelowView = false, goneEarly = false;
        float t = 0f;
        for (int i = 0; i < 60 * 40; i++)
        {
            t += Dt;
            clock += Dt;
            SpawnSpace.ClockOverride = clock;
            if (shipMoves) ship.position = new Vector3(0f, -3f + .8f * Mathf.Sin(t * 1.3f), 0f);
            rail.transform.position += Vector3.down * (scroll * Dt);   // the lane's own step (RailLaneScroller)
            if (scroller != null) TestHarness.Send(scroller, "Update");
            if (go == null) { goneEarly = !endedBelowView; break; }
            brain.Step(Dt);
            TestHarness.Send(mount, "LateUpdate");
            var l = brain.Laser;
            if (l != null) l.Place();
            if (rail == null) { laneAlive = false; break; }

            float y = go.transform.position.y;
            if (mount.RideState == RailMineMount.RidePhase.Holding)
            {
                everRode = true;
                framesRiding++;
                float dev = Mathf.Abs(y - mount.HoldRow(ship));
                worstBand = Mathf.Max(worstBand, dev);
                if (dev > RailMineMount.HoldBand) inBand = false;
                if (framesRiding > 1) worstRel = Mathf.Max(worstRel, Mathf.Abs((y - prevY) / Dt));
            }
            bool beam = l != null && l.State == RailMineLaser.Phase.Beam;
            if (beam && !prevBeam)
            {
                beams++;
                beamsInBand &= mount.RideState == RailMineMount.RidePhase.Holding;
            }
            prevBeam = beam;
            onRail &= Mathf.Abs(go.transform.position.x - x) < .001f;
            if (mount.RideState == RailMineMount.RidePhase.Released)
            {
                if (float.IsNaN(releasedY)) releasedY = y;
                if (y > prevY + 1e-4f) fallsMonotonic = false;
                if (y < CameraFit.ViewBottom - 1f) { endedBelowView = true; break; }
            }
            prevY = y;
        }
        Check(tag + "rode " + framesRiding * Dt + " s, within " + worstBand.ToString("F2") + " u of the hold row (band " + RailMineMount.HoldBand +
              "), slid at most " + worstRel.ToString("F1") + " u/s against the screen",
              everRode && inBand && framesRiding > 1.5f / Dt && worstRel < 3f);
        Check(tag + beams + " lasers (" + brain.ShotsFired + " counted), both while riding", beams == RailMineLaser.ShotsPerRide &&
              brain.ShotsFired == RailMineLaser.ShotsPerRide && beamsInBand);
        Check(tag + "then released and carried down past the view (monotonic " + fallsMonotonic + "), still on its rail, lane alive, never vanished",
              endedBelowView && fallsMonotonic && onRail && laneAlive && !goneEarly && !float.IsNaN(releasedY));
        if (go != null) Object.DestroyImmediate(go);
        if (rail != null) Object.DestroyImmediate(rail);
    }

    // A world that is not stepping leaves the riding mine where it is.
    static void Freeze()
    {
        Fresh(.35f);
        var def = EnemyRoster.One(1, EnemyRole.Mine);
        float x = enmiesOnBoard.WorldRailX(false);
        var rail = new GameObject("RailMineLane");
        rail.transform.position = new Vector3(x, -2.4f, 0f);
        rail.AddComponent<RailLaneScroller>();
        var go = EnemyFactory.Create(def, new Vector3(x, -2.4f, 0f), Quaternion.identity);
        var brain = go.GetComponent<EnemyBrain>();
        var mount = go.AddComponent<RailMineMount>();
        mount.MountTo(rail.transform);
        mount.brain = brain;
        brain.TargetOverride = ship;
        for (int i = 0; i < 90; i++)
        {
            rail.transform.position += Vector3.down * (.35f * BoardRoll.BoardScroll * Dt);
            brain.Step(Dt);
            TestHarness.Send(mount, "LateUpdate");
        }
        Vector3 before = go.transform.position;
        float rideBefore = mount.Ride;
        for (int i = 0; i < 120; i++) TestHarness.Send(mount, "LateUpdate");   // nobody steps the brain: paused
        Check("a frozen world leaves a riding mine put (y " + before.y.ToString("F3") + " -> " + go.transform.position.y.ToString("F3") + ")",
              mount.RideState == RailMineMount.RidePhase.Holding && go.transform.position == before && mount.Ride == rideBefore);
        Object.DestroyImmediate(go);
        Object.DestroyImmediate(rail);
    }

    // A rock coming down the lane beside a riding mine's rail: the mine holds
    // against the board, the rock does not, so the mine must give way (go with
    // the board) rather than let the rock through its body.
    static void Room(float speed)
    {
        Fresh(speed);
        var def = EnemyRoster.One(0, EnemyRole.Mine);
        var rockDef = EnemyRoster.One(0, EnemyRole.Rock);
        float x = enmiesOnBoard.WorldRailX(false);
        float scroll = speed * BoardRoll.BoardScroll;
        var rail = new GameObject("RailMineLane");
        rail.transform.position = new Vector3(x, 6f, 0f);
        var scroller = rail.AddComponent<RailLaneScroller>();
        var go = EnemyFactory.Create(def, new Vector3(x, 6f, 0f), Quaternion.identity);
        var brain = go.GetComponent<EnemyBrain>();
        var mount = go.AddComponent<RailMineMount>();
        mount.MountTo(rail.transform);
        mount.brain = brain;
        brain.TargetOverride = ship;
        // a rock that will meet the mine at its hold row (flush against the rail)
        var rock = EnemyFactory.Create(rockDef, new Vector3(x + .55f, 6f + 3f, 0f), Quaternion.identity);
        var rockFoot = rock.GetComponent<SpawnFootprint>();
        int overlaps = 0, ridden = 0;
        for (int i = 0; i < 60 * 25 && go != null; i++)
        {
            clock += Dt;
            SpawnSpace.ClockOverride = clock;
            rail.transform.position += Vector3.down * (scroll * Dt);
            rock.transform.position += Vector3.down * (scroll * Dt);   // an ordinary hazard: it goes with the board
            if (scroller != null) TestHarness.Send(scroller, "Update");
            brain.Step(Dt);
            TestHarness.Send(mount, "LateUpdate");
            SpawnFootprint a, b;
            if (SpawnSpace.AnyBodiesOverlap(out a, out b)) overlaps++;
            if (mount.RideState == RailMineMount.RidePhase.Holding) ridden++;
            if (go.transform.position.y < CameraFit.ViewBottom - 1f) break;
        }
        Check("a rock beside the rail @" + speed.ToString("F2") + " (" + scroll.ToString("F1") + " u/s): the riding mine never overlaps it (" +
              overlaps + " frames overlapping; rode " + (ridden * Dt).ToString("F1") + " s)", rockFoot != null && overlaps == 0);
        if (go != null) Object.DestroyImmediate(go);
        if (rock != null) Object.DestroyImmediate(rock);
        if (rail != null) Object.DestroyImmediate(rail);
    }
}
