using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// Elite evasion (EliteEvasion, EliteShip.Navigate; docs/enemy-behaviours.md,
// "Elite evasion"): elites read what the board is about to do to them and
// fly round it.
//
//   - an elite sidesteps a shot on a collision course (and is hit with the
//     evasion switched off: the test bites)
//   - it does not lift off into an occupied column: it waits, slides its
//     join point or hovers, and joins unhurt
//   - it never leaves the rails or the view while evading, and never dies on
//     a rail it steered into
//   - two elites do not collide while evading
//   - a wind-up that holds its ground jinks or is abandoned, not flown through
//   - it holds fire while a friendly elite is in its line; a roster enemy
//     does not lunge through an elite
//   - nothing advances while paused
//   - zero allocations with many shots alive
//   - the player can still kill it, and it does not dodge the player's shots
//   - the survival probe: board deaths soon after joining are rare, and the
//     hunted elites still die to the pilot (slow; skipped by RunFast)
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod EliteEvasionTest.Run
public static class EliteEvasionTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[EVA] PASS  " : "[EVA] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;
    static Transform pilot;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EliteCatalog.Reload();
            Tunables();
            SidestepsAShot();
            LiftOffWaitsForClearAir();
            StaysInBounds();
            TwoElitesDoNotCollide();
            WindUpJinksOrBreaksOff();
            HoldsFireForAFriend();
            NoLungeThroughAnElite();
            SpawnShadow();
            Paused();
            Allocations();
            KillableByThePlayer();
            if (TestHarness.Slow("elite survival probe (before / after, hunted)")) Survival();
        }
        finally
        {
            EliteEvasion.Enabled = true;
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
            LandingSites.Override = null;
            EnemyThreat.ForceShooting = false;
            EnemyThreat.Reset();
            SpawnSpace.ClockOverride = null;
            RunScore.EndRun(RunScore.RunId);
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
            startMenu.youAreInTutorial = false;
        }
        Debug.Log("[EVA] failures: " + fails);
        return fails;
    }

    // ---- fixtures -----------------------------------------------------------

    static readonly List<GameObject> rocks = new List<GameObject>();

    static void Fresh(float speed = .05f)
    {
        EliteSystem.Clear();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        rocks.Clear();
        RunScore.EndRun(RunScore.RunId);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = speed;
        collisionDetection.lifeCounter = 0;
        collisionDetection.atomCheck = false;
        collisionDetection.cloakTimer = 0f;
        PlayerInvuln.Reset();
        RunScore.BeginRun(true, true);
        pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -3.5f, 0f);
        EliteSystem.PlayerOverride = pilot;
        LandingSites.Override = null;
        EliteEvasion.Enabled = true;
        Random.InitState(4321);
    }

    static EliteDef Def(string brain)
    {
        foreach (var d in EliteCatalog.All) if (d.brain == brain) return d;
        return null;
    }

    static EliteShip InPlay(string brain, Vector2 at)
    {
        var e = EliteShip.CreateInPlay(Def(brain), at);
        e.AttackCooldown = 99f;
        return e;
    }

    static GameObject Rock(Vector2 at)
    {
        var go = EnemyFactory.Create(EnemyRoster.One(3, EnemyRole.Rock), at, Quaternion.identity);
        ClearTarget.Ensure(go);
        rocks.Add(go);
        return go;
    }

    // The test's rocks fall with the board; the ones gone below are removed.
    static void FallRocks()
    {
        float fall = SpawnSpace.ScrollSpeed * Dt;
        for (int i = rocks.Count - 1; i >= 0; i--)
        {
            var r = rocks[i];
            if (r == null) { rocks.RemoveAt(i); continue; }
            r.transform.position += Vector3.down * fall;
            if (r.transform.position.y < -7f) { Object.DestroyImmediate(r); rocks.RemoveAt(i); }
        }
    }

    static void Step(float seconds, System.Action each = null)
    {
        for (float t = 0f; t < seconds - 1e-4f; t += Dt)
        {
            if (each != null) each();
            FallRocks();
            EliteSystem.Step(Dt);
        }
    }

    static LandingSite Site(Vector2 at, int id = 1)
    {
        var anchor = new GameObject("~Pad" + id).transform;
        anchor.position = at;
        return new LandingSite { anchor = anchor, local = Vector3.zero, scale = .3f, order = -420, id = id };
    }

    static bool Alive(EliteShip e) => e != null && e.State != EliteState.Dead;

    // ---- tunables -----------------------------------------------------------

    static void Tunables()
    {
        Check("the evasion is on, and elites do not dodge the pilot's shots by default (PlayerShotAwareness " + EliteEvasion.PlayerShotAwareness + ")",
              EliteEvasion.Enabled && EliteEvasion.PlayerShotAwareness == 0f);
        Check("a reaction delay and a bounded look-ahead (" + EliteEvasion.ReactionSeconds + " s, " + EliteEvasion.LookAheadSeconds + " s)",
              EliteEvasion.ReactionSeconds >= .08f && EliteEvasion.LookAheadSeconds > EliteEvasion.ReactionSeconds * 3f && EliteEvasion.LookAheadSeconds <= 2f);
        bool capped = true, slower = true;
        foreach (var d in EliteCatalog.All)
        {
            capped &= EliteEvasion.EvadeAccelFor(d) <= EliteEvasion.MaxEvadeAccel + 1e-4f && EliteEvasion.EvadeAccelFor(d) >= d.accel;
            slower &= EliteEvasion.ReactionFor(d) >= EliteEvasion.ReactionSeconds - 1e-4f && EliteEvasion.LookAheadFor(d) <= EliteEvasion.LookAheadSeconds + 1e-4f;
        }
        Check("every elite's evasive acceleration is capped (" + EliteEvasion.MaxEvadeAccel + " u/s^2)", capped);
        Check("a clumsier elite (lower avoidance) reacts later and reads less far ahead, never the other way", slower);
        Check("lift-off waits are capped (pad " + EliteEvasion.LiftDelayMax + " s, hover " + EliteEvasion.LiftHoldMax + " s)",
              EliteEvasion.LiftDelayMax <= 3f && EliteEvasion.LiftHoldMax <= 2f);
    }

    // ---- a shot on a collision course ---------------------------------------

    // A siege elite holding its station; a bolt comes straight up its lane.
    static int ShotRun(bool evasion, out float moved, out int evasions)
    {
        Fresh(.05f);
        EliteEvasion.Enabled = evasion;
        var e = InPlay("siege", new Vector2(0f, 3.5f));
        Step(.6f);
        Vector2 start = e.Position;
        EliteSystem.Shots.Fire(null, e.Def, EliteShots.Kind.Bolt, new Vector2(start.x, -1.5f), Vector2.up * 4.5f);
        float far = 0f;
        Step(2f, () => { if (Alive(e)) far = Mathf.Max(far, Mathf.Abs(e.Position.x - start.x)); });
        moved = far;
        evasions = Alive(e) ? e.Evasions : 0;
        return Alive(e) ? e.Hearts : 0;
    }

    static void SidestepsAShot()
    {
        float moved;
        int evasions;
        int hearts = ShotRun(true, out moved, out evasions);
        Check("an elite sidesteps a shot on a collision course (hearts " + hearts + ", moved " + moved.ToString("F2") + " u, sidesteps " + evasions + ")",
              hearts == 2 && moved > .25f && evasions >= 1);
        Check("... smoothly: a sidestep, not a jump across the lane (" + moved.ToString("F2") + " u)", moved < 2.2f);
        int off = ShotRun(false, out moved, out evasions);
        Check("... and with the evasion off the same shot hits it (hearts " + off + ")", off == 1);

        // a landed resin pool riding the board at it, and the spot a glob is about to land on
        Fresh(.2f);
        var w = InPlay("interceptor", new Vector2(.4f, -1f));
        pilot.position = new Vector3(.4f, 1.2f, 0f);
        Step(.5f);
        var warden = Def("warden");
        var glob = EliteSystem.Shots.Fire(null, warden, EliteShots.Kind.Glob, new Vector2(-2f, 3f), Vector2.zero);
        glob.Lob(w.Position + Vector2.up * (SpawnSpace.ScrollSpeed * warden.lobSeconds), warden.lobSeconds);
        Step(warden.lobSeconds + 1.2f);
        Check("an elite gets off the spot a lobbed glob is about to pool on (hearts " + (Alive(w) ? w.Hearts : 0) + ")", Alive(w) && w.Hearts == 2);
    }

    // ---- lift-off -------------------------------------------------------------

    // Rocks pour down the column the elite means to join in.
    static bool LiftRun(bool evasion, out float waited, out float slid, out float joinedAfter)
    {
        Fresh(.3f);
        EliteEvasion.Enabled = evasion;
        pilot.position = new Vector3(-.5f, -3.5f, 0f);
        Vector2 join = new Vector2(1.2f, .4f);
        var e = EliteShip.Create(Def("siege"), Site(new Vector2(1.2f, 2.2f)), 2.5f, join);
        float nextRock = 0f, clock = 0f;
        System.Action pour = () =>
        {
            clock += Dt;
            if (clock < nextRock) return;
            nextRock = clock + .22f;
            Rock(new Vector2(1.2f, EliteSystem.ViewTop + 1.5f));
        };
        int guard = 0;
        while (Alive(e) && !e.InPlay && guard++ < 60 * 14) Step(Dt, pour);
        joinedAfter = guard * Dt;
        waited = Alive(e) ? e.PadWait + e.HoverWait : 0f;
        slid = Alive(e) ? Vector2.Distance(e.Position, join) : 0f;
        bool hurt = false;
        // its first second and a half in play, the rocks still pouring
        for (int i = 0; i < 90 && Alive(e); i++) { Step(Dt, pour); hurt |= !Alive(e) || e.Hearts < e.Def.hearts; }
        return !hurt && Alive(e);
    }

    static void LiftOffWaitsForClearAir()
    {
        float waited, slid, after;
        bool unhurt = LiftRun(true, out waited, out slid, out after);
        Check("it does not lift off into an occupied column: joins unhurt (waited " + waited.ToString("F2") + " s, join point moved " + slid.ToString("F2") + " u)",
              unhurt && (waited > .1f || slid > .5f));
        Check("... and still joins: the waits are capped (in play after " + after.ToString("F1") + " s)",
              after < 2.5f + EliteShip.LiftSeconds + EliteEvasion.LiftDelayMax + EliteEvasion.LiftHoldMax + .5f);
        bool off = LiftRun(false, out waited, out slid, out after);
        Check("... with the evasion off the same lift-off is hit in its first moments", !off);

        // clear air: nothing changes -- the lift-off takes exactly as long, to exactly the join point
        Fresh(.1f);
        pilot.position = new Vector3(0f, -3f, 0f);
        var join = new Vector2(-1.5f, 1.5f);
        var e = EliteShip.Create(Def("interceptor"), Site(new Vector2(.6f, 3f)), 2.5f, join);
        int frames = 0;
        while (Alive(e) && !e.InPlay && frames++ < 60 * 10) Step(Dt);
        Check("clear air: no wait, and it joins where the director sent it (" + (frames * Dt).ToString("F2") + " s)",
              e.PadWait == 0f && e.HoverWait == 0f && Mathf.Abs(frames * Dt - (e.ParkSeconds + EliteShip.LiftSeconds)) < .1f &&
              Vector2.Distance(e.Position, join) < .3f);
    }

    // ---- bounds ---------------------------------------------------------------

    static void StaysInBounds()
    {
        Fresh(.2f);
        pilot.position = new Vector3(0f, -3f, 0f);
        var elites = new List<EliteShip>
        {
            InPlay("skirmisher", new Vector2(-1.2f, -.5f)),
            InPlay("gunship", new Vector2(1.4f, -3f)),
            InPlay("siege", new Vector2(0f, 3.4f)),
        };
        bool inside = true;
        float worstX = 0f;
        int railHits = 0, frame = 0;
        var style = Def("gunship");
        var rng = new System.Random(99);
        System.Action storm = () =>
        {
            frame++;
            pilot.position = new Vector3(Mathf.Sin(frame * .02f) * 1.7f, -3f, 0f);
            // rocks all across the lane, the rails' edge included, and shots from above and from the sides
            if (frame % 14 == 0) Rock(new Vector2((float)(rng.NextDouble() * 4.6 - 2.3), EliteSystem.ViewTop + 1.5f));
            if (frame % 23 == 0)
                EliteSystem.Shots.Fire(null, style, EliteShots.Kind.Bolt, new Vector2((float)(rng.NextDouble() * 4 - 2), EliteSystem.ViewTop), Vector2.down * 5f);
            if (frame % 41 == 0)
                EliteSystem.Shots.Fire(null, style, EliteShots.Kind.Shard, new Vector2(-2.2f, (float)(rng.NextDouble() * 6 - 3)), new Vector2(4f, -.5f));
            foreach (var e in elites)
            {
                if (!Alive(e)) continue;
                float edge = EliteSystem.RailEdge - e.Def.hullRadius * .7f;
                worstX = Mathf.Max(worstX, Mathf.Abs(e.Position.x) - edge);
                inside &= Mathf.Abs(e.Position.x) <= edge + 1e-3f &&
                          e.Position.y >= EliteSystem.ViewBottom + e.Def.hullRadius * .6f - 1e-3f && e.Position.y <= EliteSystem.ViewTop + 1.5f + 1e-3f;
                if (e.LastHitCause == EliteDamage.Rail && e.Hearts < e.Def.hearts) railHits++;
            }
        };
        int sidesteps = 0, lost = 0;
        EliteShip.Died = (e, cause, by) => { if (cause == EliteDamage.Rail) railHits++; sidesteps += e.Evasions; lost++; };
        Step(20f, storm);
        EliteShip.Died = null;
        Check("it never leaves the rails or the view while evading (worst " + worstX.ToString("F2") + " u past a rail's face)", inside);
        Check("... and never flies itself into a rail (" + railHits + " rail hits in a 20 s storm)", railHits == 0);
        foreach (var e in elites) if (Alive(e)) sidesteps += e.Evasions;
        Check("... while really dodging (" + sidesteps + " sidesteps; " + lost + " of 3 lost to the storm)", sidesteps >= 3);
    }

    // ---- two elites -------------------------------------------------------------

    static int PairRun(bool evasion, out float nearest)
    {
        Fresh(.2f);
        EliteEvasion.Enabled = evasion;
        pilot.position = new Vector3(0f, -1f, 0f);
        // two interceptors want the very same spot behind the pilot; rocks come down on both
        var a = InPlay("interceptor", new Vector2(-1.2f, -3.2f));
        var b = InPlay("interceptor", new Vector2(1.2f, -3.2f));
        int bodyHits = 0, frame = 0;
        float near = 99f;
        EliteShip.Died = (e, cause, by) => { if (by == "elite body") bodyHits++; };
        Step(12f, () =>
        {
            frame++;
            if (frame % 50 == 0) Rock(new Vector2(frame % 100 == 0 ? -.9f : .9f, EliteSystem.ViewTop + 1.5f));
            if (!Alive(a) || !Alive(b)) return;
            near = Mathf.Min(near, Vector2.Distance(a.Position, b.Position));
            if (a.LastHitBy == "elite body" || b.LastHitBy == "elite body") bodyHits++;
        });
        EliteShip.Died = null;
        nearest = near;
        return bodyHits;
    }

    static void TwoElitesDoNotCollide()
    {
        float near;
        int hits = PairRun(true, out near);
        float touch = Def("interceptor").hullRadius * 1.65f;
        Check("two elites after the same spot do not collide while evading (nearest " + near.ToString("F2") + " u, touching at " + touch.ToString("F2") + ")",
              hits == 0 && near > touch);
        int off = PairRun(false, out near);
        Check("... with the evasion off they crash into each other (" + off + ")", off > 0);
    }

    // ---- wind-ups ----------------------------------------------------------------

    static void WindUpJinksOrBreaksOff()
    {
        Fresh(.2f);
        pilot.position = new Vector3(0f, -3.5f, 0f);
        var e = InPlay("siege", new Vector2(0f, 3.5f));
        Step(.5f);
        e.AttackCooldown = 0f;
        e.EscapeLeft = 0f;
        int guard = 0;
        while (Alive(e) && !e.Telling && guard++ < 120) Step(Dt);
        Check("the siege elite starts its long wind-up", Alive(e) && e.Telling);
        Vector2 planted = e.Position;
        Rock(new Vector2(planted.x, planted.y + 3.6f));   // comes down its lane while it charges
        Step(1.4f);
        Check("a wind-up that holds its ground is not flown through: it jinks or gives the attack up (hearts " + (Alive(e) ? e.Hearts : 0) +
              ", broke off " + (Alive(e) ? e.BreakOffs : 0) + ", moved " + (Alive(e) ? Vector2.Distance(e.Position, planted).ToString("F2") : "-") + ")",
              Alive(e) && e.Hearts == 2 && (e.BreakOffs > 0 || Vector2.Distance(e.Position, planted) > .2f));
        Step(6f);
        Check("... and it attacks again afterwards (" + (Alive(e) ? e.Attack.Fired : 0) + " shells)", Alive(e) && e.Attack.Fired >= 1);
    }

    static void HoldsFireForAFriend()
    {
        Fresh(.05f);
        pilot.position = new Vector3(0f, -3.8f, 0f);
        var siege = InPlay("siege", new Vector2(0f, 3.5f));
        var friend = InPlay("hauler", new Vector2(0f, -1.6f));   // sits in the siege's lane, above the pilot
        Step(.4f);
        siege.AttackCooldown = 0f;
        siege.EscapeLeft = 0f;
        Step(1.5f);
        Check("it holds fire while a friendly elite is in its line (held " + siege.HeldFire + " times, " + siege.Attacks + " attacks)",
              siege.HeldFire > 0 && siege.Attacks == 0 && Alive(friend) && friend.Hearts == friend.Def.hearts);
        Object.DestroyImmediate(friend.gameObject);
        Step(3f);
        Check("... and fires once the line is clear (" + siege.Attack.Fired + ")", siege.Attack.Fired >= 1);
    }

    static void NoLungeThroughAnElite()
    {
        Fresh(.2f);
        EnemyThreat.ForceShooting = true;
        SpawnSpace.ClockOverride = 50f;
        pilot.position = new Vector3(0f, -3.5f, 0f);
        EnemyDef lunger = null;
        foreach (var d in EnemyRoster.All)
            if (d.Behaviour != null && d.Behaviour.attack == EnemyAttack.Lunge && d.Behaviour.lungeDive > 0f && d.role != EnemyRole.Chaser) { lunger = d; break; }
        var go = EnemyFactory.Create(lunger, new Vector3(0f, 3.2f, 0f), Quaternion.identity);
        var brain = go.GetComponent<EnemyBrain>();
        brain.TargetOverride = pilot;
        var e = InPlay("gunship", new Vector2(.1f, 1.2f));   // right under it, in its dive
        for (int i = 0; i < 180; i++) brain.Step(Dt);
        Check(lunger.key + " does not lunge through a friendly elite (" + brain.Windups + " wind-ups in 3 s)", brain.Windups == 0);
        Object.DestroyImmediate(e.gameObject);
        for (int i = 0; i < 180; i++) brain.Step(Dt);
        Check("... and lunges once it is out of the way (" + brain.Windups + ")", brain.Windups > 0);
        Vector2 reach;
        float inSeconds;
        bool told = false;
        var go2 = EnemyFactory.Create(lunger, new Vector3(1f, 3.2f, 0f), Quaternion.identity);
        var brain2 = go2.GetComponent<EnemyBrain>();
        brain2.TargetOverride = pilot;
        for (int i = 0; i < 240 && !told; i++)
        {
            brain2.Step(Dt);
            told = brain2.State == EnemyBrain.Phase.Windup && brain2.LungeAhead(out reach, out inSeconds) && reach.y < -.2f && inSeconds >= 0f;
        }
        Check("a telegraphed lunge tells the elites where it is about to dash", told);
        Object.DestroyImmediate(go);
        Object.DestroyImmediate(go2);
        EnemyThreat.ForceShooting = false;
        SpawnSpace.ClockOverride = null;
    }

    // ---- the spawn shadow -----------------------------------------------------------

    static void SpawnShadow()
    {
        Fresh(.3f);
        var e = InPlay("siege", new Vector2(.5f, 1f));
        Step(.2f);
        Vector2 half = Vector2.one * .25f;
        float row = EliteSystem.ViewTop + 2f;
        var above = new SpawnCandidate(new Vector2(e.Position.x, row), half);
        var beside = new SpawnCandidate(new Vector2(e.Position.x - 1.6f, row), half);
        Check("the spawner drops nothing new into the column an elite is flying", !SpawnSpace.Fits(above));
        Check("... and still spawns beside it", SpawnSpace.Fits(beside));
        float keep = EliteEvasion.SpawnShadowSeconds;
        EliteEvasion.SpawnShadowSeconds = 0f;
        Check("... SpawnShadowSeconds 0 switches the shadow off", SpawnSpace.Fits(above));
        EliteEvasion.SpawnShadowSeconds = keep;
        moveBackGround.speed = .05f;
        Check("... a slow board has a short shadow: what spawns far above leaves it time to move", SpawnSpace.Fits(above));
    }

    // ---- paused ----------------------------------------------------------------------

    static void Paused()
    {
        Fresh(.2f);
        pilot.position = new Vector3(0f, -3.5f, 0f);
        var e = InPlay("siege", new Vector2(0f, 3.5f));
        Step(.4f);
        Rock(new Vector2(e.Position.x, e.Position.y + 4f));
        Step(.25f);
        var parked = EliteShip.Create(Def("interceptor"), Site(new Vector2(1.5f, 2f), 7), 2.5f, new Vector2(1.5f, 0f));
        Vector2 p = e.Position, v = e.Velocity, to = e.EvadeTarget, claim = e.Claim;
        bool evading = e.Evading;
        float hitIn = e.HitIn, parkedTime = parked.StateTime;
        int plans = EliteEvasion.Plans, senses = EliteEvasion.Senses, sidesteps = e.Evasions;
        for (int i = 0; i < 60; i++) EliteSystem.Step(0f);   // a frozen world: dt 0
        Check("evasion does not advance while paused: position, velocity, plan, the sensor and a parked elite's clock all hold",
              e.Position == p && e.Velocity == v && e.EvadeTarget == to && e.Claim == claim && e.Evading == evading && e.HitIn == hitIn &&
              EliteEvasion.Plans == plans && EliteEvasion.Senses == senses && e.Evasions == sidesteps && parked.StateTime == parkedTime);
        Step(.5f);
        Check("... and picks up where it left off (it had begun to dodge: " + evading + ")", Alive(e) && EliteEvasion.Plans > plans);
    }

    // ---- allocations -------------------------------------------------------------------

    static void Allocations()
    {
        Fresh(.05f);
        pilot.position = new Vector3(0f, -3f, 0f);
        InPlay("interceptor", new Vector2(-.6f, -4f));
        InPlay("gunship", new Vector2(.9f, -2.6f));
        InPlay("siege", new Vector2(0f, 3.4f));
        var style = Def("hauler");
        // forty slow shots hanging in the air either side, and a dozen rocks coming down
        for (int i = 0; i < 40; i++)
            EliteSystem.Shots.Fire(null, style, EliteShots.Kind.Slag, new Vector2(i % 2 == 0 ? -2f : 2f, -3f + i * .19f), Vector2.zero);
        for (int i = 0; i < 12; i++) Rock(new Vector2(-2.1f + (i % 4) * 1.4f, 5.5f + i * .7f));
        Step(1.5f);   // warm up: every plan, sidestep and puff once
        int live = EliteSystem.Shots.ActiveCount, plans = EliteEvasion.Plans;
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 180; i++)
        {
            FallRocks();
            EliteSystem.Step(Dt);
        }
        long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
        Check("zero allocations over 180 frames: three elites sensing and planning with " + live + " shots and " + rocks.Count + " rocks alive (" +
              allocated + " bytes, " + (EliteEvasion.Plans - plans) + " plans)", allocated == 0 && live >= 30 && EliteEvasion.Plans - plans >= 30);
        Check("the threat picture is bounded (" + EliteEvasion.ThreatCount + " of " + EliteEvasion.MaxThreats + ")",
              EliteEvasion.ThreatCount > 30 && EliteEvasion.ThreatCount <= EliteEvasion.MaxThreats);
    }

    // ---- the player -------------------------------------------------------------------

    static void KillableByThePlayer()
    {
        foreach (var def in EliteCatalog.All)
        {
            Fresh(.05f);
            var e = EliteShip.CreateInPlay(def, new Vector2(0f, 1f));
            e.AttackCooldown = 99f;
            Step(.3f);
            int hits = 0;
            for (int i = 0; i < def.hearts + 2 && Alive(e); i++)
            {
                e.TakeShipAttack(0, 1f, e.Position);
                hits++;
                Step(EliteShip.GraceSeconds + .05f);
            }
            Check(def.key + ": the player still kills it, a heart a hit (" + hits + " hits for " + def.hearts + " hearts)",
                  !Alive(e) && hits == def.hearts && EliteShip.LastKillCause == EliteDamage.PlayerWeapon);
        }

        // straight from the lift-off: no protection of any kind against the pilot
        Fresh(.05f);
        pilot.position = new Vector3(0f, -3f, 0f);
        var fresh = EliteShip.Create(Def("interceptor"), Site(new Vector2(.6f, 3f)), 2.5f, new Vector2(-1.5f, 1.5f));
        int guard = 0;
        while (Alive(fresh) && !fresh.InPlay && guard++ < 600) Step(Dt);
        fresh.TakeShipAttack(0, 1f, fresh.Position);
        Check("the frame it joins the play it can be shot (hearts " + fresh.Hearts + ")", fresh.Hearts == fresh.Def.hearts - 1);

        // the pilot's shots are not in the threat picture at the default awareness
        bool sawPlayerShot = false;
        for (int i = 0; i < EliteEvasion.ThreatCount; i++) sawPlayerShot |= EliteEvasion.ThreatKind(i) == EliteEvasion.Kind.PlayerShot;
        Check("the pilot's shots are not threats to dodge at the default awareness", !sawPlayerShot && EliteEvasion.PlayerShotAwareness == 0f);
    }

    // ---- the survival probe (slow) -------------------------------------------------------

    static void Survival()
    {
        var huds = new[] { 20, 30 };
        try
        {
            EliteSurvivalProbe.Open();
            EliteEvasion.Enabled = false;
            var before = EliteSurvivalProbe.Solo(huds, 2, false).Total();
            EliteEvasion.Enabled = true;
            var after = EliteSurvivalProbe.Solo(huds, 2, false).Total();
            var hunted = EliteSurvivalProbe.Solo(new[] { 20 }, 2, true).Total();
            Debug.Log(string.Format("[EVA] survival, {0} solo elites at HUD 20 / 30, passive pilot: board deaths within {1} s of joining {2:P0} -> {3:P0}; within {4} s {5:P0} -> {6:P0}",
                                    after.flown, EliteSurvivalProbe.EarlySeconds, before.EarlyRate, after.EarlyRate, EliteSurvivalProbe.WatchSeconds, before.DeathRate, after.DeathRate));
            Check("without the evasion most elites die to the board soon after joining (" + before.EarlyRate.ToString("P0") + ")", before.EarlyRate > .4f);
            Check("with it, board deaths soon after joining are rare (" + after.EarlyRate.ToString("P0") + " within " + EliteSurvivalProbe.EarlySeconds + " s)", after.EarlyRate <= .15f);
            Check("... and most elites outlast the whole watch (" + after.DeathRate.ToString("P0") + " lost in " + EliteSurvivalProbe.WatchSeconds + " s)", after.DeathRate <= .4f);
            Check("... still flying their own attacks (" + after.AttacksPerSurvivor.ToString("F1") + " per survivor, " + before.AttacksPerSurvivor.ToString("F1") + " before)",
                  after.AttacksPerSurvivor >= 1.5f);
            Check("a pilot who shoots still kills them (" + hunted.playerKills + " of " + hunted.flown + ")", hunted.playerKills >= hunted.flown * .8f);
        }
        finally
        {
            EliteEvasion.Enabled = true;
            EliteSurvivalProbe.Close();
        }
    }
}
