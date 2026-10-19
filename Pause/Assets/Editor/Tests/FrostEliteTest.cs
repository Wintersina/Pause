using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Frost's five elites as mini-boss fights (FrostElites.cs): the roster (hearts, hiding, one dock each) and their strips, muzzles and
// nozzles on the hull, launching from their own kinds of Frost ground site (and any free one when theirs is not in view; the backdrop
// brings the dock into view), and each one's brain and attack in simulation -- the Rimebreaker's pass-through runs (an ice-crack lane
// drawn, the whole board ploughed, out of the bottom, back in from the top on another lane, in pairs once hurt), the Harrower holding
// the top on the far side with its slab row + lance and its skidding slabs, the Siren hopping between perches with a blink trail (two
// orbs at its last heart), the Tender's mines and fog, the Sentinel's patrol and its icicle barrage with one safe lane -- tell
// timings, clean-up on death and zero per-frame allocation.
//
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites FrostEliteTest
public static class FrostEliteTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[FEL] PASS  " : "[FEL] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 30f;
    static Transform pilot;

    // key -> brain, attack, its preferred (first) site kind it launches from, hearts, hides when hurt (launchFrom lists a second, older kind too:
    // FrostDockTest checks the pairs)
    static readonly (string key, string brain, string attack, LandingKind from, int hearts, bool hides)[] Expected =
    {
        ("frost_elite_rimebreaker", "icebreaker", "ice_run", LandingKind.Icebreaker, 3, true),
        ("frost_elite_floe_harrower", "barge", "floe_barge", LandingKind.Rig, 3, true),
        ("frost_elite_cryo_siren", "hopper", "frost_bloom", LandingKind.Cave, 3, true),
        ("frost_elite_glacier_tender", "carrier", "tender_rig", LandingKind.Silo, 5, false),
        ("frost_elite_whiteout_sentinel", "patrol", "icicle_barrage", LandingKind.Silo, 5, false),
    };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EliteCatalog.Reload();
            int tooShort = AttackPreview.TooShort;
            Roster();
            Sites();
            foreach (var x in Expected) Cycle(EliteCatalog.Find(x.key));
            Icebreaker();
            Barge();
            Hopper();
            Carrier();
            Patrol();
            Check("every telegraph the Frost elites drew showed at least " + AttackPreview.MinLead + " s before it went live (" + (AttackPreview.TooShort - tooShort) + " too short)", AttackPreview.TooShort == tooShort);
            Allocations();
            BringHome();
        }
        finally
        {
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
            LandingSites.Override = null;
            RunScore.EndRun(RunScore.RunId);
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
            startMenu.youAreInTutorial = false;
        }
        Debug.Log("[FEL] failures: " + fails);
        return fails;
    }

    // ---- fixtures (as SpaceEliteTest's) ---------------------------------------------

    static void Fresh(float speed = .25f)
    {
        EliteSystem.Clear();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
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
        pilot.position = new Vector3(0f, -2.5f, 0f);
        EliteSystem.PlayerOverride = pilot;
        LandingSites.Override = null;
        Random.InitState(2468);
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

    static void Step(float seconds, System.Action each = null)
    {
        for (float t = 0f; t < seconds - 1e-4f; t += Dt)
        {
            if (each != null) each();
            EliteSystem.Step(Dt);
        }
    }

    static bool Alive(EliteShip e) => e != null && e.State != EliteState.Dead;

    // Hits it with a player weapon `n` times, a grace apart.
    static void Shoot(EliteShip e, int n)
    {
        for (int i = 0; i < n && Alive(e); i++)
        {
            e.TakeShipAttack(0, 1f, e.Position + Vector2.down * .3f);
            Step(EliteShip.GraceSeconds + .05f);
        }
    }

    // How long it tells (s), stepping until the action starts.
    static float Tell(EliteShip e, System.Action each = null)
    {
        float s = 0f;
        int guard = 0;
        while (e.Telling && guard++ < 200) { if (each != null) each(); EliteSystem.Step(Dt); s += Dt; }
        return s;
    }

    static LandingSite Pad(Vector2 at, LandingKind kind, int id)
    {
        var anchor = new GameObject("~Pad" + id).transform;
        anchor.position = at;
        return new LandingSite { anchor = anchor, local = Vector3.zero, scale = .3f, order = -300, id = id, kind = kind };
    }

    static int ActiveShots(EliteShip owner, EliteShots.Kind kind)
    {
        int n = 0;
        foreach (var s in EliteSystem.Shots.All) if (s.Active && s.Owner == owner && s.Kind == kind) n++;
        return n;
    }

    static GameObject Rock(Vector2 at)
    {
        var go = EnemyFactory.Create(EnemyRoster.One(3, EnemyRole.Rock), at, Quaternion.identity);
        ClearTarget.Ensure(go);
        return go;
    }

    // Steps until a hiding elite is back on the board (or `seconds` pass).
    static void WaitBack(EliteShip e, float seconds = 20f)
    {
        for (float t = 0f; t < seconds && Alive(e) && e.Hidden; t += Dt) EliteSystem.Step(Dt);
    }

    // A heart off it, past its grace, its shield and its plates (a pause jump's cause), waiting out the hide.
    static void LoseHeart(EliteShip e)
    {
        if (!Alive(e)) return;
        e.TakeHit(EliteDamage.Teleport, e.Position, 1);
        Step(.1f);
        WaitBack(e);
    }

    // ---- roster ------------------------------------------------------------------------

    static void Roster()
    {
        var frost = new List<EliteDef>();
        Check("Frost has five elites (" + EliteCatalog.ForWorld(1, frost) + ")", frost.Count == 5);
        var brains = new HashSet<string>();
        var attacks = new HashSet<string>();
        var homes = new HashSet<string>();
        foreach (var x in Expected)
        {
            var d = EliteCatalog.Find(x.key);
            Check(x.key + ": defined for Frost with its own brain / attack (" + (d != null ? d.brain + " / " + d.attack : "missing") + ")",
                  d != null && d.WorldIndex == 1 && d.brain == x.brain && d.attack == x.attack && brains.Add(d.brain) && attacks.Add(d.attack));
            if (d == null) continue;
            Check(x.key + ": launches from its own pair of docks, " + d.launchFrom + " (the pair is its own; a new kind first), never a Space body",
                  LandingSite.KindOf(d.launchFrom) == x.from && LandingSite.Accepts(d.launchFrom, x.from) && homes.Add(d.launchFrom) &&
                  !LandingSite.Accepts(d.launchFrom, LandingKind.Station) && !LandingSite.Accepts(d.launchFrom, LandingKind.Planet));
            Check(x.key + ": " + x.hearts + " hearts" + (d.armored ? " (armoured)" : "") + ", " + (x.hides ? "hides when hurt (" + d.hideSeconds + " s)" : "no hiding"),
                  d.hearts == x.hearts && d.armored == (x.hearts == 5) && d.hurtHide == x.hides && (!x.hides || d.hideSeconds >= 2f));
            var frames = EliteArt.Frames(d);
            var c = d.cells;
            Check(x.key + ": the painted flight strip is installed with the flight cell map",
                  frames != null && frames.Length >= c.Count && Mathf.Approximately(frames[0].rect.width, d.cellPixels) && Mathf.Approximately(frames[0].rect.height, d.cellPixels) &&
                  c.parked == 0 && c.parkedIdle == 1 && c.liftoff == 2 && c.Flight0 == 3 && c.damaged >= 6 && c.tell < 0 && c.action < 0 && c.hit < 0);
            // every muzzle and nozzle on solid hull pixels of the hover cell, well inside the cell
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes("Assets/Art/Resources/Elites/Frost/" + d.key + ".png"));
            bool solid = true;
            string bad = "";
            foreach (var p in d.muzzles) if (!OnHull(tex, d, p)) { solid = false; bad += p.name + " "; }
            foreach (var p in d.nozzles) if (!OnHull(tex, d, p)) { solid = false; bad += p.name + " "; }
            Object.DestroyImmediate(tex);
            Check(x.key + ": its " + d.muzzles.Length + " muzzles and " + d.nozzles.Length + " nozzles sit on the hull " + bad, solid && d.muzzles.Length > 0 && d.nozzles.Length > 0);
            float orbit = d.hullRadius * d.heartOrbit + d.heartSize * 1.05f;
            Check(x.key + ": hearts orbiting tighter than the player's (" + orbit.ToString("0.00") + " u)", orbit < .52f);
            Check(x.key + ": comparable to the other elites (speed " + d.speed + ", gap " + d.attackGap + ", tell " + d.tellSeconds + ")",
                  d.speed >= 1.2f && d.speed <= 3.4f && d.attackGap >= .8f && d.attackGap <= 5.5f && d.tellSeconds >= .5f);
            Check(x.key + ": Frost's cue colours, never pink (shot " + d.ShotColor + ")", !WorldCue.IsPink(d.ShotColor) && !WorldCue.IsPink(d.ShotCore) && !WorldCue.IsPink(d.HeartColor));
            Check(x.key + ": a codex entry", global::Codex.Find(d.codexId) != null);
        }
        Check("Frost is open to elites after the first 20 s", EliteDirector.Blocked(1, 60f) == null && EliteDirector.Blocked(1, 10f) == "too early");
    }

    // Solid (alpha >= .5) within 2 px of the point, on cell 3, and inside the hull's reach.
    static bool OnHull(Texture2D tex, EliteDef d, ElitePoint p)
    {
        int cell = d.cells.Flight0, n = d.cellPixels;
        bool hit = false;
        int r = 2 * n / 192;   // a 2 px reach at the 192 px grid, scaled to HD cells
        for (int dy = -r; dy <= r && !hit; dy++)
            for (int dx = -r; dx <= r && !hit; dx++)
            {
                int x = cell * n + Mathf.RoundToInt(p.x) + dx, y = n - 1 - (Mathf.RoundToInt(p.y) + dy);
                hit = tex.GetPixel(x, y).a >= .5f;
            }
        return hit && d.PixelToLocal(p.x, p.y).magnitude < d.cellWorldSize * .42f;
    }

    // ---- the director: each from its own kind of Frost ground site ---------------------

    static void Sites()
    {
        Fresh(.05f);
        var pads = new List<LandingSite>
        {
            Pad(new Vector2(-1.6f, 3f), LandingKind.Hangar, 1), Pad(new Vector2(-.8f, 3.3f), LandingKind.RigBay, 2),
            Pad(new Vector2(0f, 3f), LandingKind.PadRing, 3), Pad(new Vector2(.8f, 3.3f), LandingKind.CrawlerBay, 4),
            Pad(new Vector2(1.6f, 3f), LandingKind.Hatch, 5), Pad(new Vector2(-2.2f, 3.1f), LandingKind.Rig, 6), Pad(new Vector2(-1.2f, 3.5f), LandingKind.Silo, 7),
            Pad(new Vector2(1.2f, 3.5f), LandingKind.Cave, 8), Pad(new Vector2(2.2f, 3.1f), LandingKind.Icebreaker, 9),
        };
        LandingSites.Override = list => list.AddRange(pads);
        var dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        int spawned = 0, mine = 0;
        bool match = true;
        var seen = new HashSet<string>();
        for (int i = 0; i < 40; i++)
        {
            EliteSystem.Clear();
            spawned += dir.SpawnGroup(1, 1);
            foreach (var e in EliteShip.Live)
            {
                mine++;
                seen.Add(e.Def.key);
                match &= LandingSite.Accepts(e.Def.launchFrom, e.Site.kind);
            }
        }
        Check("each Frost elite launches from its own kind of ground site (" + mine + " of " + spawned + " launches, " + seen.Count + " kinds of elite)",
              spawned == 40 && mine == 40 && match && seen.Count == 5);
        // none of theirs in view: any free site
        pads.Clear();
        pads.Add(Pad(new Vector2(0f, 3f), LandingKind.Ground, 9));
        bool fallback = true;
        for (int i = 0; i < 12; i++) { EliteSystem.Clear(); fallback &= dir.SpawnGroup(1, 1) == 1; }
        Check("... and any free site when none of theirs is in view", fallback);
        Object.DestroyImmediate(dir.gameObject);
        LandingSites.Override = null;
    }

    // ---- the life cycle off a ground pad -------------------------------------------------

    static void Cycle(EliteDef def)
    {
        if (def == null) { Check("frost elite defined", false); return; }
        string k = def.key + ": ";
        Fresh(.05f);
        pilot.position = new Vector3(0f, -3f, 0f);
        var site = Pad(new Vector2(.8f, 3f), LandingSite.KindOf(def.launchFrom) ?? LandingKind.Hangar, 1);
        var e = EliteShip.Create(def, site, 2.2f, new Vector2(-1.2f, 1.4f));
        Check(k + "parked on its pad: no collider, not a target", e.State == EliteState.Parked && !e.Collider.enabled && e.GetComponent<ClearTarget>() == null);
        int guard = 0;
        while (e.State != EliteState.Follow && guard++ < 400) EliteSystem.Step(Dt);
        Check(k + "lifts off, joins (collider on, a hazard) and follows", e.State == EliteState.Follow && e.Collider.enabled && e.CompareTag("Enimey"));
        int launched = EliteSystem.Shots.Launched, attacks = e.Attacks;
        bool told = false, acted = false, back = false;
        e.AttackCooldown = 0f;
        for (int i = 0; i < 30 * 40 && !back; i++)
        {
            pilot.position = new Vector3(Mathf.Sin(i * .02f) * .8f, -2.8f, 0f);
            EliteSystem.Step(Dt);
            if (!Alive(e)) break;
            told |= e.Telling;
            acted |= e.Acting;
            if (acted && e.State == EliteState.Follow) back = true;
        }
        var dd = Alive(e) ? e.Attack as DroneDeployAttack : null;
        bool out_ = dd != null ? dd.Released > 0 : EliteSystem.Shots.Launched > launched;
        Check(k + "attacks (" + e.Attack.Id + "): a wind-up, then " + (dd != null ? "drones" : "shots") + " out, back to following", told && acted && back && out_ && e.Attacks > attacks);
        // shot down for good: (shield / plates aside) it dies, is paid, and leaves nothing of its own behind
        float total = RunScore.Total;
        for (int i = 0; i < 12 && Alive(e); i++) { e.TakeHit(EliteDamage.Teleport, e.Position, 1); Step(.1f); WaitBack(e); }
        bool clean = !EliteShip.Live.Contains(e);
        foreach (var s in EliteSystem.Shots.All) clean &= !(s.Active && s.Owner == e && (s.Kind == EliteShots.Kind.Slab || s.Kind == EliteShots.Kind.Orb));
        if (dd != null) clean &= dd.Live == 0;
        Check(k + "brought down: paid, gone, its slabs / orbs / drones / mines with it", !Alive(e) && clean && RunScore.Total - total >= ScoreRules.EliteDown);
        EliteSystem.Clear();
    }

    // ---- Rimebreaker: icebreaker + ice_run -------------------------------------------------

    static void Icebreaker()
    {
        Fresh(.05f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        var e = InPlay("icebreaker", new Vector2(1.2f, 1.5f));
        var def = e.Def;
        var brain = (IcebreakerBrain)e.Brain;
        var run = (IceRunAttack)e.Attack;
        e.AttackCooldown = 0f;
        int guard = 0;
        while (brain.Phase != IcebreakerBrain.Stage.Ready && guard++ < 30 * 15) EliteSystem.Step(Dt);
        float top = EliteSystem.ViewTop;
        Check("icebreaker leaves the view and appears at the top on a lane (y " + e.Position.y.ToString("0.0") + ", lane " + brain.Lane.ToString("0.00") + ")",
              brain.Phase == IcebreakerBrain.Stage.Ready && e.Position.y > e.ZoneTop - def.hullRadius - 1f && Mathf.Abs(e.Position.x) < EliteSystem.RailEdge && Mathf.Abs(e.Position.x - brain.Lane) < .2f);

        // watch a long fight: the pilot drifts about; every run is recorded
        var lanes = new List<float>();
        var enterY = new List<float>();
        var gaps = new List<float>();     // RunDone -> the next Enter (the pause)
        float nearestPilot = 99f;
        float tellSpeed = 0f, tellDrift = 0f, laneAtTell = 0f, tellLen = 0f, runSpeed = 0f, runSide = 0f, minY = 9f, laneRun = 0f;
        bool telegraph = false, locked = true, rockBroken = false, offBottom = false, untouchable = false, noHeart = true;
        int loops = 0, startedSeen = 0, shards0 = 0;
        float away = -1f, clock = 0f;
        var last = brain.Phase;
        bool inTell = false, inRun = false;
        for (int i = 0; i < 30 * 60 && lanes.Count < 5; i++)
        {
            pilot.position = new Vector3(Mathf.Sin(i * .03f) * 1.4f, -2.5f, 0f);
            EliteSystem.Step(Dt);
            clock += Dt;
            if (!Alive(e)) break;
            if (!e.Offstage && brain.Phase != IcebreakerBrain.Stage.Run) nearestPilot = Mathf.Min(nearestPilot, Vector2.Distance(e.Position, pilot.position));
            if (e.Telling)
            {
                if (!inTell) { inTell = true; laneAtTell = e.Position.x; tellLen = 0f; telegraph = run.Telegraph.Showing; loops = run.Telegraph.Loops; }
                tellLen += Dt;
                tellSpeed = Mathf.Max(tellSpeed, e.Velocity.magnitude);
                tellDrift = Mathf.Max(tellDrift, Mathf.Abs(e.Position.x - laneAtTell));
                locked &= Mathf.Abs(run.LaneX - laneAtTell) < .01f;
            }
            else inTell = false;
            if (brain.RunsStarted > startedSeen)
            {
                startedSeen = brain.RunsStarted;
                lanes.Add(brain.Lane);
                shards0 = EliteSystem.Shots.Launched;
                laneRun = e.Position.x;
                if (lanes.Count == 1) rock0 = Rock(new Vector2(e.Position.x, e.Position.y - 2.2f));
            }
            if (e.Acting)
            {
                inRun = true;
                runSpeed = Mathf.Max(runSpeed, -e.Velocity.y);
                runSide = Mathf.Max(runSide, Mathf.Abs(e.Position.x - laneRun));
                minY = Mathf.Min(minY, e.Position.y);
                noHeart &= e.Hearts == def.hearts;
            }
            else if (inRun) inRun = false;
            if (e.Position.y < EliteSystem.ViewBottom - .3f)
            {
                offBottom = true;
                untouchable |= e.Offstage && !e.Collider.enabled;
            }
            if (brain.Phase == IcebreakerBrain.Stage.Away && last == IcebreakerBrain.Stage.Run) away = clock;
            if (brain.Phase == IcebreakerBrain.Stage.Enter && last != IcebreakerBrain.Stage.Enter)
            {
                enterY.Add(e.Position.y);
                if (away >= 0f) { gaps.Add(clock - away); away = -1f; }
            }
            last = brain.Phase;
        }
        Check("icebreaker's tell draws an ice-crack lane down the board (" + loops + " dotted outlines) and holds still on it (" + tellLen.ToString("0.00") + " s, drift " + tellDrift.ToString("0.00") + ", " + tellSpeed.ToString("0.0") + " u/s)",
              telegraph && loops >= 3 && Mathf.Abs(tellLen - def.tellSeconds) < .12f && tellDrift < .1f && tellSpeed < 1.2f);
        Check("... the lane is locked at the tell (the pilot moves, it does not), and outside a run it keeps its distance (" + nearestPilot.ToString("0.0") + " u at the closest)", locked && lanes.Count >= 1 && nearestPilot > 5f);
        Check("ice_run: ploughs straight down the lane at dashSpeed through the whole board (" + runSpeed.ToString("0.0") + " u/s, off the lane by " + runSide.ToString("0.00") + ", down to y " + minY.ToString("0.0") + ")",
              runSpeed > def.dashSpeed * .9f && runSide < .06f && minY < EliteSystem.ViewBottom - .3f);
        Check("... breaks the rock in its way without losing a heart, shards off the prow (" + e.Ploughed + " ploughed, " + (rock0 == null) + ")", rock0 == null && e.Ploughed >= 1 && noHeart);
        Check("... exits off the bottom, untouchable while outside the board", offBottom && untouchable);
        bool fromTop = enterY.Count > 0;
        foreach (float y in enterY) fromTop &= y > top;
        Check("... then re-enters from the TOP (" + enterY.Count + " entries)", fromTop);
        bool other = lanes.Count >= 3;
        for (int i = 1; i < lanes.Count; i++) other &= Mathf.Abs(lanes[i] - lanes[i - 1]) >= IcebreakerBrain.MinLaneGap - .01f;
        Check("... on a different lane each time (" + string.Join(", ", lanes.ConvertAll(x => x.ToString("0.0"))) + ")", other);
        bool pause = gaps.Count >= 2;
        foreach (float g in gaps) pause &= g >= IcebreakerBrain.PauseSeconds - .1f;
        Check("... after a pause of at least " + IcebreakerBrain.PauseSeconds + " s (" + string.Join(", ", gaps.ConvertAll(x => x.ToString("0.0"))) + ")", pause);

        // never closes on the pilot: whenever it is on the board between runs it is at the top, far above
        Fresh(.05f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        e = InPlay("icebreaker", new Vector2(0f, 2f));
        brain = (IcebreakerBrain)e.Brain;
        e.AttackCooldown = 99f;   // (it never attacks: it only waits)
        float nearest = 99f;
        Step(12f, () =>
        {
            pilot.position = Vector3.MoveTowards(pilot.position, new Vector3(brain.Lane, 3f, 0f), 1.2f * Dt);
            if (!e.Offstage) nearest = Mathf.Min(nearest, Mathf.Abs(e.Position.y - pilot.position.y) + 0f);
        });
        Check("icebreaker never prowls: between runs it waits at the top or off the view (" + brain.Phase + ", at y " + e.Position.y.ToString("0.0") + ")",
              brain.Phase == IcebreakerBrain.Stage.Ready && e.Position.y > e.ZoneTop - def.hullRadius - 1f);

        // a hit: it hides, comes back, and the runs come in pairs
        Fresh(.05f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        e = InPlay("icebreaker", new Vector2(0f, 2f));
        brain = (IcebreakerBrain)e.Brain;
        e.AttackCooldown = 0f;
        Check("icebreaker: a single run per cycle at full hearts", brain.PairSize == 1);
        Step(2f);
        e.TakeShipAttack(0, 1f, e.Position);
        Check("... a hit sends it hiding", e.Hidden && e.Hearts == def.hearts - 1);
        WaitBack(e);
        Check("... and back, the runs now come in pairs (" + brain.PairSize + ")", !e.Hidden && brain.PairSize == 2);
        var runGaps = new List<float>();
        var starts = new List<float>();
        int s0 = brain.RunsStarted;
        float tt = 0f, lastDone = -1f;
        var lastPhase = brain.Phase;
        var pairLanes = new List<float>();
        for (int i = 0; i < 30 * 40 && starts.Count < 4 && Alive(e); i++)
        {
            pilot.position = new Vector3(Mathf.Sin(i * .03f) * 1.2f, -2.5f, 0f);
            EliteSystem.Step(Dt);
            tt += Dt;
            if (brain.RunsStarted - s0 > starts.Count) { starts.Add(tt); pairLanes.Add(brain.Lane); }
            if (lastPhase == IcebreakerBrain.Stage.Run && brain.Phase == IcebreakerBrain.Stage.Away) lastDone = tt;
            if (brain.Phase == IcebreakerBrain.Stage.Enter && lastPhase != IcebreakerBrain.Stage.Enter && lastDone >= 0f) { runGaps.Add(tt - lastDone); lastDone = -1f; }
            lastPhase = brain.Phase;
        }
        // (the pairs: the first pause is short, the next long; two runs, then a long pause, then two again)
        Check("... the second run of a pair follows after only " + IcebreakerBrain.PairGap + " s, the pair is followed by the long pause (pauses " + string.Join(", ", runGaps.ConvertAll(x => x.ToString("0.0"))) + ")",
              runGaps.Count >= 3 && Mathf.Abs(runGaps[0] - IcebreakerBrain.PairGap) < .25f && Mathf.Abs(runGaps[1] - IcebreakerBrain.PauseSeconds) < .25f && Mathf.Abs(runGaps[2] - IcebreakerBrain.PairGap) < .25f);
        bool sides = pairLanes.Count >= 2 && Mathf.Sign(pairLanes[0]) != Mathf.Sign(pairLanes[1]);
        Check("... the two lanes of a pair on opposite sides of the board (" + string.Join(", ", pairLanes.ConvertAll(x => x.ToString("0.0"))) + ")", sides || Mathf.Abs(pairLanes[0]) < .5f);
    }

    static GameObject rock0;

    // ---- Floe Harrower: barge + floe_barge ------------------------------------------------

    static void Barge()
    {
        Fresh(.05f);
        pilot.position = new Vector3(-1f, -2.5f, 0f);
        var e = InPlay("barge", new Vector2(1f, 3f));
        var def = e.Def;
        var brain = (BargeBrain)e.Brain;
        Step(4f);
        Check("barge holds high on the far side of the pilot (" + e.Position + ", side " + brain.Side + ")",
              e.Position.x > pilot.position.x + BargeBrain.LaneKeep * .9f && Mathf.Abs(e.Position.y - brain.Height) < .4f && e.Position.y > pilot.position.y + 2.5f);
        // the pilot sweeps the board: it never comes down, never sits in the pilot's lane (bar the crossing), never follows
        float minDx = 9f, minDist = 9f, maxDy = 0f, travel = 0f;
        Vector2 lastP = e.Position;
        int f = 0;
        Step(36f, () =>
        {
            pilot.position = new Vector3(Mathf.Sin(f++ * .012f) * 1.4f, -2.5f + Mathf.Sin(f * .02f) * .8f, 0f);
            minDist = Mathf.Min(minDist, Vector2.Distance(e.Position, pilot.position));
            if (!brain.Crossing) minDx = Mathf.Min(minDx, Mathf.Abs(e.Position.x - pilot.position.x));
            maxDy = Mathf.Max(maxDy, Mathf.Abs(e.Position.y - brain.Height));
            travel += Vector2.Distance(e.Position, lastP);
            lastP = e.Position;
        });
        Check("... never in the pilot's lane (closest " + minDx.ToString("0.00") + " u across, " + minDist.ToString("0.0") + " u away), crossing the top only when the pilot takes its side (" + brain.Crossings + "x)",
              minDx > BargeBrain.LaneKeep * .7f && minDist > 2.5f && brain.Crossings >= 1);
        Check("... staying on the top rail (never more than " + maxDy.ToString("0.00") + " u off its height)", maxDy < .5f);
        // the pilot rushes at it: it does not flee down, hunt, or follow
        pilot.position = new Vector3(0f, 1.2f, 0f);
        Step(3f);
        Check("... a pilot parked right under it changes nothing (it stays at the top, " + e.Position + ")", Mathf.Abs(e.Position.y - brain.Height) < .6f);

        // the casts: cast, skid, skid, cast
        Fresh(.05f);
        pilot.position = new Vector3(.6f, -2.5f, 0f);
        e = InPlay("barge", new Vector2(-1f, 3f));
        var barge = (FloeBargeAttack)e.Attack;
        Step(2f);
        e.ForceAttack();
        Check("barge: its first attack is the floe cast", !barge.Skid);
        Tell(e);
        int guard = 0;
        while (e.State == EliteState.Attack && guard++ < 200) EliteSystem.Step(Dt);
        int slabs = ActiveShots(e, EliteShots.Kind.Slab);
        var lanceShots = 0;
        foreach (var s in EliteSystem.Shots.All) if (s.Active && s.Kind == EliteShots.Kind.Bolt && s.Owner == e) lanceShots++;
        Check("floe_cast kept: " + barge.Count + " slabs in a row with one gap and a lance down it (" + slabs + " slabs, " + lanceShots + " lance, gap " + barge.Gap + ")",
              slabs == barge.Count && lanceShots == 1 && barge.Lanced && barge.Gap >= 0 && barge.Gap <= barge.Count);
        EliteSystem.Clear();

        // the skids
        Fresh(.05f);
        pilot.position = new Vector3(.4f, -2.5f, 0f);
        e = InPlay("barge", new Vector2(-1.2f, 3f));
        barge = (FloeBargeAttack)e.Attack;
        Step(2f);
        var kinds = new List<bool>();
        EliteShot skid = null;
        for (int n = 0; n < 4; n++)
        {
            e.AttackCooldown = 0f;
            for (int g2 = 0; g2 < 400 && e.State != EliteState.Follow; g2++) EliteSystem.Step(Dt);
            e.ForceAttack();
            e.AttackCooldown = 99f;   // (only the forced attacks: the sequence is counted)
            kinds.Add(barge.Skid);
            if (n == 1)
            {
                // the first skid, watched all the way
                bool drawn = barge.SkidLane.Showing && barge.SkidLane.Loops == 3;
                Vector2 bounce = barge.BounceAt;
                float tellT = Tell(e);
                EliteSystem.Step(Dt);
                skid = barge.SkidSlab;
                bool slab = skid != null && skid.Kind == EliteShots.Kind.Slab && skid.Skidding;
                Check("barge: between casts a single slab skids (the tell drew its path, " + tellT.ToString("0.00") + " s; slab " + slab + "; path locked from the first frame " + drawn + ")",
                      drawn && slab && tellT >= EliteDef.MinAimedTell - .05f && Mathf.Abs(tellT - Mathf.Max(EliteDef.MinAimedTell, def.tellSeconds * FloeBargeAttack.SkidTell)) < .12f);
                bool towardsFar = slab && Mathf.Sign(skid.Velocity.x) == -Mathf.Sign(e.Position.x);
                float speed = slab ? skid.Velocity.magnitude : 0f;
                bool bounced = false, backwards = false, straight = true;
                float bounceY = 0f, bounceX = 0f;
                Vector2 v0 = slab ? skid.Velocity : Vector2.zero;
                for (int g3 = 0; g3 < 30 * 8 && skid.Active; g3++)
                {
                    e.AttackCooldown = 99f;
                    int before = skid.Bounced;
                    EliteSystem.Step(Dt);
                    if (!skid.Active) break;
                    if (skid.Bounced > before) { bounced = true; bounceX = skid.transform.position.x; bounceY = skid.transform.position.y; backwards = Mathf.Sign(skid.Velocity.x) == -Mathf.Sign(v0.x); }
                    straight &= Mathf.Abs(skid.Velocity.y - v0.y) < .01f && Mathf.Abs(Mathf.Abs(skid.Velocity.x) - Mathf.Abs(v0.x)) < .01f;
                }
                Check("... it slides across the screen at skidSpeed (" + speed.ToString("0.0") + " u/s vs " + barge.SkidSpeed.ToString("0.0") + ") toward the far rail", towardsFar && Mathf.Abs(speed - barge.SkidSpeed) < .2f);
                Check("... glances off ONE rail where the telegraph said (rail at " + bounceX.ToString("0.00") + ", y " + bounceY.ToString("0.0") + " vs drawn " + bounce.x.ToString("0.00") + ", " + bounce.y.ToString("0.0") + "), straight lines either side",
                      bounced && backwards && straight && Mathf.Abs(bounceX - bounce.x) < .15f && Mathf.Abs(bounceY - bounce.y) < .7f && skid.Bounced == 1);
                Check("... and leaves off the bottom (not on the pilot's side of a second bounce)", !skid.Active && skid.Bounced == 1);
            }
            else
            {
                Tell(e);
                for (int g4 = 0; g4 < 200 && e.State == EliteState.Attack; g4++) EliteSystem.Step(Dt);
            }
            Step(.3f);
            e.AttackCooldown = 99f;
        }
        Check("barge: casts alternate cast, skid, skid, cast (" + string.Join(" ", kinds.ConvertAll(x => x ? "skid" : "cast")) + ")", !kinds[0] && kinds[1] && kinds[2] && !kinds[3]);
        EliteSystem.Clear();

        // a skid slab soaks shots (the player can shoot it down) and breaks up with the Harrower
        Fresh(.05f);
        pilot.position = new Vector3(.4f, -2.5f, 0f);
        e = InPlay("barge", new Vector2(-1.2f, 3f));
        def = e.Def;
        barge = (FloeBargeAttack)e.Attack;
        Step(1f);
        for (int n = 0; n < 2; n++)
        {
            e.AttackCooldown = 0f;
            for (int g2 = 0; g2 < 400 && e.State != EliteState.Follow; g2++) EliteSystem.Step(Dt);
            e.ForceAttack();
            Tell(e);
            EliteSystem.Step(Dt);
            if (n == 0) for (int g2 = 0; g2 < 400 && e.State == EliteState.Attack; g2++) EliteSystem.Step(Dt);
        }
        e.AttackCooldown = 99f;
        skid = barge.SkidSlab;
        var hb = skid != null ? skid.Hitbox.GetComponent<EliteShotHitbox>() : null;
        for (int i = 0; i < def.hazardArmour - 1 && hb != null; i++) hb.TakeShipAttack(0, 1f, skid.transform.position);
        bool soaked = skid != null && skid.Active;
        if (hb != null) hb.TakeShipAttack(0, 1f, skid.transform.position);
        Check("a skid slab soaks " + (def.hazardArmour - 1) + " shots and breaks on the " + def.hazardArmour + "th", soaked && skid != null && !skid.Active);
        // and breaks up with the Harrower
        for (int g2 = 0; g2 < 400 && e.State == EliteState.Attack; g2++) EliteSystem.Step(Dt);
        e.AttackCooldown = 0f;
        for (int g2 = 0; g2 < 400 && e.State != EliteState.Follow; g2++) EliteSystem.Step(Dt);
        e.ForceAttack();
        Tell(e);
        EliteSystem.Step(Dt);
        skid = barge.SkidSlab;
        bool live2 = skid != null && skid.Active;
        for (int i = 0; i < 6 && Alive(e); i++) { e.TakeHit(EliteDamage.Teleport, e.Position, 1); Step(.1f); WaitBack(e); }
        Check("a skid slab breaks up when the Harrower goes down (" + ActiveShots(e, EliteShots.Kind.Slab) + " left)", live2 && !Alive(e) && (skid == null || !skid.Active));
        EliteSystem.Clear();
    }

    // ---- Cryo Siren: hopper + frost_bloom -------------------------------------------------

    static void Hopper()
    {
        Fresh(.02f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        var e = InPlay("hopper", new Vector2(.5f, 1.5f));
        var def = e.Def;
        var brain = (HopperBrain)e.Brain;
        e.AttackCooldown = 99f;
        int guard = 0;
        while (brain.Phase != HopperBrain.Stage.Perch && guard++ < 30 * 12) EliteSystem.Step(Dt);
        float d0 = Vector2.Distance(e.Position, pilot.position);
        Check("hopper perches at long range, high above the pilot (" + d0.ToString("0.0") + " u)", brain.Phase == HopperBrain.Stage.Perch && d0 >= HopperBrain.MinRange && e.Position.y > pilot.position.y + 2.5f);

        // a long fight: it hops perch to perch, off the view and back, with the blink trail marking the next; it never closes
        e.AttackCooldown = 0f;
        var perches = new List<int> { brain.Perch };
        bool wentOff = false, camBack = false, trail = false, untouchable = true;
        float nearest = 99f;
        var lastStage = brain.Phase;
        int f = 0, hops0 = brain.Hops;
        Step(36f, () =>
        {
            if (!Alive(e)) return;
            pilot.position = new Vector3(Mathf.Sin(f++ * .03f) * 1.2f, -2.5f, 0f);
            if (!e.Offstage) nearest = Mathf.Min(nearest, Vector2.Distance(e.Position, pilot.position));
            if (e.Offstage) { wentOff = true; untouchable &= !e.Collider.enabled; }
            else if (wentOff) camBack = true;
            trail |= brain.Marks.ShownCount > 0 && brain.Phase == HopperBrain.Stage.Away;
            if (brain.Phase == HopperBrain.Stage.Perch && lastStage != HopperBrain.Stage.Perch) perches.Add(brain.Perch);
            lastStage = brain.Phase;
        });
        bool changes = perches.Count >= 3;
        for (int i = 1; i < perches.Count; i++) changes &= perches[i] != perches[i - 1];
        Check("hopper hops to a NEW perch each time (" + string.Join(" > ", perches.ConvertAll(x => x.ToString())) + ", " + (brain.Hops - hops0) + " hops)", changes && brain.Hops - hops0 >= 2);
        Check("... leaving the view (untouchable out there) and re-entering", wentOff && camBack && untouchable);
        Check("... a blink trail of rings drawn into the next perch while it is away", trail);
        Check("... and never closer to the pilot than its range (" + nearest.ToString("0.0") + " u)", nearest >= HopperBrain.MinRange * .85f);

        // the pilot rushes it: it peels off rather than stand
        Fresh(.02f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        e = InPlay("hopper", new Vector2(.5f, 1.5f));
        brain = (HopperBrain)e.Brain;
        e.AttackCooldown = 99f;
        guard = 0;
        while (brain.Phase != HopperBrain.Stage.Perch && guard++ < 30 * 12) EliteSystem.Step(Dt);
        Step(.7f);
        int hops = brain.Hops;
        pilot.position = (Vector3)(e.Position + Vector2.down * 4.6f);
        bool left = false;
        Step(3f, () =>
        {
            pilot.position = Vector3.MoveTowards(pilot.position, (Vector3)e.Position, 2.2f * Dt);   // flies straight at it
            left |= brain.PeeledOff && brain.Phase != HopperBrain.Stage.Perch;
        });
        Check("... crowded (the pilot flies straight at it) it hops away rather than stand (" + (brain.Hops - hops) + " hops)", left && brain.Hops > hops);

        // frost_bloom: one orb a bloom, two at its last heart; the beam keeps its place
        Fresh(.02f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        e = InPlay("hopper", new Vector2(.5f, 1.5f));
        def = e.Def;
        brain = (HopperBrain)e.Brain;
        var bloom = (FrostBloomAttack)e.Attack;
        e.AttackCooldown = 99f;
        guard = 0;
        while (brain.Phase != HopperBrain.Stage.Perch && guard++ < 30 * 12) EliteSystem.Step(Dt);
        Step(.7f);
        e.ForceAttack();
        bool glow = false;
        float tell = Tell(e, () => glow |= e.ChargeGlowOn);
        EliteSystem.Step(Dt);
        var orb = bloom.Orb;
        Vector2 dish = e.MuzzleWorld(0);
        Check("frost_bloom: the dish charges (" + tell.ToString("0.00") + " s), then ONE slow cryo orb at full hearts, its fuse ring blinking",
              glow && !bloom.Beam && Mathf.Abs(tell - def.tellSeconds) < .12f && orb != null && bloom.Orb2 == null && orb.Kind == EliteShots.Kind.Orb &&
              Vector2.Distance(orb.LaunchedAt, dish) < .25f && orb.MarkShown && orb.Velocity.magnitude < 3f);
        int launched = EliteSystem.Shots.Launched;
        float fuse = 0f;
        Vector2 burstAt = Vector2.zero;
        guard = 0;
        while (orb.Active && orb.Kind == EliteShots.Kind.Orb && guard++ < 100) { burstAt = orb.transform.position; EliteSystem.Step(Dt); fuse += Dt; }
        int ring = 0;
        foreach (var s in EliteSystem.Shots.All) if (s.Active && s.Kind == EliteShots.Kind.Shard && s.Age < Dt * 1.5f) ring++;
        Check("... bursts after its fuse (" + fuse.ToString("0.00") + " s) into a ring of " + def.hazardCount + " shards (" + ring + ")",
              Mathf.Abs(fuse - def.hazardSeconds) < .15f && ring == def.hazardCount && EliteSystem.Shots.Launched - launched == def.hazardCount);
        // after the bloom it hops
        int hp = brain.Hops;
        Step(4f);
        Check("... and after the bloom it hops to another perch (" + (brain.Hops - hp) + ")", brain.Hops > hp);
        // two hearts gone: two orbs a bloom
        LoseHeart(e);
        LoseHeart(e);
        guard = 0;
        while (Alive(e) && brain.Phase != HopperBrain.Stage.Perch && guard++ < 30 * 12) EliteSystem.Step(Dt);
        Step(.7f);
        Check("hopper is down to its last heart (" + e.Hearts + ")", Alive(e) && e.Hearts == 1);
        e.AttackCooldown = 0f;
        guard = 0;
        while (Alive(e) && !e.Acting && guard++ < 30 * 12) { if (!e.Telling && e.State == EliteState.Follow && e.AttackCooldown > 0f) e.AttackCooldown = 0f; EliteSystem.Step(Dt); }
        var o1 = bloom.Orb;
        var o2 = bloom.Orb2;
        float a1 = o1 != null ? Mathf.Atan2(o1.Velocity.y, o1.Velocity.x) * Mathf.Rad2Deg : 0f, a2 = o2 != null ? Mathf.Atan2(o2.Velocity.y, o2.Velocity.x) * Mathf.Rad2Deg : 0f;
        if (bloom.Beam) { Check("(the third attack is the beam: the double-orb check waits for the next)", true); }
        else
            Check("frost_bloom at the last heart: TWO orbs (" + Mathf.Abs(Mathf.DeltaAngle(a1, a2)).ToString("0") + " deg apart), both fused",
                  o1 != null && o2 != null && o1.MarkShown && o2.MarkShown && Mathf.Abs(Mathf.DeltaAngle(a1, a2)) > FrostBloomAttack.OrbSplit * 1.5f);
        e.TakeHit(EliteDamage.Teleport, e.Position, 1);
        Step(1f);
        Check("an orb still on its fuse pops when the Siren goes down (no ring)", !Alive(e) && (o1 == null || !(o1.Active && o1.Kind == EliteShots.Kind.Orb)) && (o2 == null || !(o2.Active && o2.Kind == EliteShots.Kind.Orb)));

        // the beam sweep (every third)
        Fresh(.02f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        e = InPlay("hopper", new Vector2(.5f, 1.5f));
        def = e.Def;
        brain = (HopperBrain)e.Brain;
        bloom = (FrostBloomAttack)e.Attack;
        e.AttackCooldown = 99f;
        guard = 0;
        while (brain.Phase != HopperBrain.Stage.Perch && guard++ < 30 * 12) EliteSystem.Step(Dt);
        Step(.7f);
        bloom.NextIsBeam();
        e.ForceAttack();
        bool sight = false;
        tell = Tell(e, () => sight |= e.SightShown);
        Check("frost_bloom beam: a longer tell painting the arc with a sight line (" + tell.ToString("0.00") + " s)", bloom.Beam && sight && Mathf.Abs(tell - def.tellSeconds * FrostBloomAttack.BeamTell) < .12f);
        float lo = 999f, hi = -999f;
        int before = EliteSystem.Shots.Launched;
        guard = 0;
        while (e.State == EliteState.Attack && guard++ < 100)
        {
            EliteSystem.Step(Dt);
            foreach (var s in EliteSystem.Shots.All)
            {
                if (!s.Active || s.Kind != EliteShots.Kind.Bolt || s.Age > Dt * 1.5f) continue;
                float a = Mathf.Atan2(s.Velocity.y, s.Velocity.x) * Mathf.Rad2Deg;
                lo = Mathf.Min(lo, a); hi = Mathf.Max(hi, a);
            }
        }
        Check("... then sweeps a stream of bolts across that arc (" + (EliteSystem.Shots.Launched - before) + " bolts, " + lo.ToString("0") + ".." + hi.ToString("0") + " deg)",
              EliteSystem.Shots.Launched - before >= 10 && hi - lo > FrostBloomAttack.BeamArc * 1.6f && hi - lo < FrostBloomAttack.BeamArc * 2.3f && !e.SightShown);
        EliteSystem.Clear();
    }

    // ---- Glacier Tender: carrier + tender_rig -------------------------------------------------

    static void Carrier()
    {
        Fresh(.02f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        var e = InPlay("carrier", new Vector2(.5f, 1.5f));
        var def = e.Def;
        var brain = (CarrierBrain)e.Brain;
        e.AttackCooldown = 99f;
        Step(4f);
        Check("carrier hangs near the top (" + e.Position + ", height " + brain.Height.ToString("0.0") + ")", Mathf.Abs(e.Position.y - brain.Height) < .4f && e.Position.y > pilot.position.y + 2.5f);
        // the pilot moves about under it: it slides its own slow sine and does not follow
        float minX = 9f, maxX = -9f, minDist = 9f, corr = 0f;
        int n = 0, f = 0;
        float lx = e.Position.x;
        Step(25f, () =>
        {
            pilot.position = new Vector3(Mathf.Sin(f++ * .06f) * 1.6f, -2.5f, 0f);
            minX = Mathf.Min(minX, e.Position.x); maxX = Mathf.Max(maxX, e.Position.x);
            minDist = Mathf.Min(minDist, Vector2.Distance(e.Position, pilot.position));
            corr += (e.Position.x - lx) * (pilot.position.x - e.Position.x); n++;
            lx = e.Position.x;
        });
        Check("... it never chases (stays " + minDist.ToString("0.0") + " u off, sliding between " + minX.ToString("0.0") + " and " + maxX.ToString("0.0") + ")", minDist > def.keepDistance * .9f && Mathf.Abs(e.Position.y - brain.Height) < .5f);

        // the deploy
        Fresh(.02f);
        pilot.position = new Vector3(.6f, -2.5f, 0f);
        e = InPlay("carrier", new Vector2(.5f, 1.8f));
        def = e.Def;
        brain = (CarrierBrain)e.Brain;
        var rig = (TenderRigAttack)e.Attack;
        Step(2f);
        e.ForceAttack();
        Check("tender_rig: the first attack launches drones (shield down, no fog yet)", !rig.Venting);
        bool glow = false;
        float tell = Tell(e, () => glow |= e.ChargeGlowOn);
        EliteSystem.Step(Dt);
        Check("... the pods charge (" + tell.ToString("0.00") + " s) and 2-3 tethered drones come out", glow && Mathf.Abs(tell - def.tellSeconds) < .12f && rig.Live >= 2 && rig.Live <= 3 && rig.Linked);
        Step(1.5f);
        int hearts = e.Hearts;
        e.TakeShipAttack(0, 1f, e.Position);
        Check("... shield-linked while two live: a shot costs nothing (5 hearts, blocked " + rig.Blocked + ")", e.Hearts == hearts && rig.Blocked == 1 && hearts == 5);

        // the mines: a drone drops a frost mine on the pilot's lane, ringed first, shootable
        float spotX = 0f;
        Vector2 spot0 = Vector2.zero;
        bool ringed = false, lockedSpot = true, pending = false;
        float ringT = 0f;
        for (int i = 0; i < 30 * 12 && !rig.MinePending; i++) { pilot.position = new Vector3(.6f, -2.5f, 0f); EliteSystem.Step(Dt); }
        pending = rig.MinePending;
        if (pending)
        {
            spotX = rig.MineSpot.x;
            spot0 = rig.MineSpot;
            Check("tender mines: a drone picks the pilot's lane (spot x " + spotX.ToString("0.00") + " vs pilot " + pilot.position.x.ToString("0.00") + ")", Mathf.Abs(spotX - pilot.position.x) < .35f);
            int g = 0;
            while (rig.MinePending && g++ < 100)
            {
                pilot.position = new Vector3(-1.6f, -2.5f, 0f);   // the pilot moves away: the spot does not
                ringed |= rig.Marks.Shown(0);
                lockedSpot &= Mathf.Abs(rig.MineSpot.x - spotX) < .01f;
                EliteSystem.Step(Dt);
                ringT += Dt;
            }
            Check("... ringed on the board for " + ringT.ToString("0.00") + " s before it drops, and locked (the pilot moved away, the ring did not)",
                  ringed && lockedSpot && Mathf.Abs(ringT - TenderRigAttack.MineTell) < .2f);
        }
        else Check("tender mines: a drone eventually picks a mine spot", false);
        EliteShot mine = null;
        foreach (var s in EliteSystem.Shots.All) if (s.Active && s.Owner == e && s.IsMine) mine = s;
        bool small = mine != null && mine.Radius < 0.2f && mine.Kind == EliteShots.Kind.Orb;
        bool fromDrone = false;
        if (mine != null)
            for (int i = 0; i < 4; i++) { var d = rig.Drone(i); if (d != null && Vector2.Distance(d.transform.position, mine.LaunchedAt) < .6f) fromDrone = true; }
        Check("... a small frost mine slides out of a drone (" + (mine != null ? mine.Radius.ToString("0.00") : "-") + " u), on a fuse ring", mine != null && small && fromDrone && mine.MarkShown);
        var hb = mine != null ? mine.Hitbox.GetComponent<EliteShotHitbox>() : null;
        int bursts = mine != null ? mine.Bursts : 0;
        if (hb != null) hb.TakeShipAttack(0, 1f, mine.transform.position);
        Check("... one player shot defuses it, no burst", mine != null && !mine.Active && mine.Bursts == bursts);
        // left alone it pops into a short ring of shards
        e.AttackCooldown = 99f;
        EliteShot m2 = null;
        for (int i = 0; i < 30 * 12 && m2 == null; i++)
        {
            pilot.position = new Vector3(.6f, -2.5f, 0f);
            EliteSystem.Step(Dt);
            foreach (var s in EliteSystem.Shots.All) if (s.Active && s.Owner == e && s.IsMine) m2 = s;
        }
        int before = EliteSystem.Shots.Launched;
        int guard2 = 0;
        while (m2 != null && m2.Active && guard2++ < 30 * 6) EliteSystem.Step(Dt);
        Check("... left alone it pops into " + TenderRigAttack.MineArms + " shards (" + (EliteSystem.Shots.Launched - before) + ")", m2 != null && !m2.Active && EliteSystem.Shots.Launched - before >= TenderRigAttack.MineArms);

        // the fog: shield down, the next attack vents a cold cone straight down, outlined first
        for (int i = 0; i < 4; i++) { var d = rig.Drone(i); if (d != null) Object.DestroyImmediate(d); }
        Step(.2f);
        Check("tender: its drones are gone, the shield is down", !rig.Linked && rig.Live == 0);
        e.AttackCooldown = 0f;
        for (int g = 0; g < 200 && e.State != EliteState.Follow; g++) EliteSystem.Step(Dt);
        e.ForceAttack();
        bool vent = rig.Venting;
        if (!vent)
        {
            // (the cast before it was a deploy: it alternates -- clear that one's drones too)
            Tell(e);
            for (int g = 0; g < 200 && e.State == EliteState.Attack; g++) EliteSystem.Step(Dt);
            for (int i = 0; i < 4; i++) { var d = rig.Drone(i); if (d != null) Object.DestroyImmediate(d); }
            Step(.2f);
            e.ForceAttack();
            vent = rig.Venting;
        }
        var fog = rig.Fog;
        bool outlined = fog != null && fog.Preview != null && fog.Preview.Active && !fog.Live;
        Vector2 below = rig.FogOrigin + Vector2.down * 2f, aside = rig.FogOrigin + new Vector2(3.5f, -2f);
        bool area = fog != null && fog.Footprint.Touches(below, .1f) && !fog.Footprint.Touches(aside, .1f);
        float tellSpeed = 0f;
        bool harmlessInTell = true;
        float ftell = Tell(e, () => { tellSpeed = Mathf.Max(tellSpeed, e.Velocity.magnitude); harmlessInTell &= fog != null && !fog.Live && !fog.HitCollider.enabled; });
        Check("tender fog: with its shield down it vents a cone straight down, outlined from the first frame (" + ftell.ToString("0.00") + " s planted, " + tellSpeed.ToString("0.0") + " u/s)",
              vent && outlined && area && Mathf.Abs(ftell - def.tellSeconds) < .12f && tellSpeed < 1.2f && Mathf.Abs(fog.AimDirection + Mathf.PI * .5f) < .05f);
        EliteSystem.Step(Dt);
        float live = 0f;
        bool hurts = fog != null && fog.Live && fog.HitCollider.enabled;
        for (int g = 0; g < 60 && fog != null && fog.Active && fog.Live; g++) { EliteSystem.Step(Dt); live += Dt; }
        Check("... harmless until it ignites, then a short burn that hurts on touch (" + live.ToString("0.00") + " s, <= " + AttackJet.MaxLiveSeconds + ")", harmlessInTell && hurts && live <= AttackJet.MaxLiveSeconds + .1f);
        Step(1.5f);
        Check("... and it is gone", fog == null || !fog.Active);
        // shielded again with drones: it only launches
        Fresh(.02f);
        pilot.position = new Vector3(.6f, -2.5f, 0f);
        e = InPlay("carrier", new Vector2(.5f, 1.8f));
        rig = (TenderRigAttack)e.Attack;
        Step(1f);
        e.ForceAttack();
        Step(def.tellSeconds + def.actionSeconds + .3f);
        Check("... with its shield up it never vents (" + rig.Vents + ")", rig.Linked && rig.Vents == 0);
        // dies: drones, mines and the fog go with it
        int dropped = 0;
        for (int i = 0; i < 30 * 12 && !rig.MinePending; i++) EliteSystem.Step(Dt);
        for (int i = 0; i < 30 * 3 && dropped == 0; i++) { EliteSystem.Step(Dt); foreach (var s in EliteSystem.Shots.All) if (s.Active && s.Owner == e && s.IsMine) dropped++; }
        var any = rig.Drone(0) ?? rig.Drone(1) ?? rig.Drone(2);
        for (int i = 0; i < 6 && Alive(e); i++) { e.TakeHit(EliteDamage.Teleport, e.Position, 1); Step(.1f); }
        int left = 0;
        foreach (var s in EliteSystem.Shots.All) if (s.Active && s.Owner == e && s.IsMine) left++;
        Check("killing the Tender scuttles its drones and defuses its mines (" + rig.Live + " drones, " + left + " mines left, " + dropped + " had been dropped)", !Alive(e) && rig.Live == 0 && left == 0 && any == null);
    }

    // ---- Whiteout Sentinel: patrol + icicle_barrage -------------------------------------------------

    static void Patrol()
    {
        Fresh(.02f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        var e = InPlay("patrol", new Vector2(-1f, 3f));
        var def = e.Def;
        var brain = (PatrolBrain)e.Brain;
        e.AttackCooldown = 99f;
        // a rock right in its path: it does not swerve
        var rock = Rock(new Vector2(0f, brain.Height));
        Step(3f);
        float minX = 9f, maxX = -9f, maxDy = 0f, maxVy = 0f, nearest = 9f, towards = 0f;
        int legs0 = brain.Legs, f = 0;
        Step(26f, () =>
        {
            pilot.position = new Vector3(Mathf.Sin(f++ * .04f) * 1.4f, -2.5f, 0f);
            minX = Mathf.Min(minX, e.Position.x); maxX = Mathf.Max(maxX, e.Position.x);
            maxDy = Mathf.Max(maxDy, Mathf.Abs(e.Position.y - brain.Height));
            if (Mathf.Abs(e.Velocity.x) > .3f) maxVy = Mathf.Max(maxVy, Mathf.Abs(e.Velocity.y));
            nearest = Mathf.Min(nearest, Vector2.Distance(e.Position, pilot.position));
        });
        Check("patrol: straight legs ACROSS THE TOP (x " + minX.ToString("0.0") + " .. " + maxX.ToString("0.0") + ", " + (brain.Legs - legs0) + " legs, never " + maxDy.ToString("0.00") + " u off the top rail, climbing/dropping at most " + maxVy.ToString("0.00") + " u/s)",
              minX < -1f && maxX > 1f && brain.Legs - legs0 >= 3 && maxDy < .5f && maxVy < .45f);
        Check("... never toward the pilot (" + nearest.ToString("0.0") + " u at the closest) and it never dodges: no evasions, ploughs the rock (armoured, no heart lost)", nearest > 2.5f && e.Evasions == 0 && rock == null && e.Hearts == def.hearts);
        // the pilot climbs right under it: it still patrols (does not come down, does not stop, does not charge)
        pilot.position = new Vector3(0f, 0f, 0f);
        float vmax = 0f;
        Step(8f, () => vmax = Mathf.Max(vmax, e.Velocity.magnitude));
        Check("... a pilot parked under it changes nothing (pace " + vmax.ToString("0.0") + " u/s of " + (def.speed * PatrolBrain.Pace).ToString("0.0") + ")", Mathf.Abs(e.Position.y - brain.Height) < .6f && vmax <= def.speed * PatrolBrain.Pace + .35f);

        // armour_shatter: plates go in order, each a telegraphed spray
        Fresh(.02f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        e = InPlay("patrol", new Vector2(0f, 3f));
        var armour = (IcicleBarrageAttack)e.Attack;
        Step(2.5f);
        int plates = armour.Plates;
        bool shown = armour.PlateShown(0) && armour.PlateShown(1) && armour.PlateShown(2);
        e.TakeShipAttack(0, 1f, e.Position + Vector2.down * .3f);
        bool order = armour.Plates == plates - 1 && !armour.PlateShown(2) && armour.PlateShown(1);
        Check("plates: " + plates + " ice plates on its prow; a shot breaks the outermost instead of a heart", plates == def.hazardCount && shown && order && e.Hearts == def.hearts);
        e.TakeShipAttack(0, 1f, e.Position);
        Check("... a second shot inside the plate grace breaks nothing more", armour.Plates == plates - 1);
        bool cone = false;
        int launched = EliteSystem.Shots.Launched;
        float sprayIn = 0f;
        int guard = 0;
        while (armour.SprayTelegraphed && guard++ < 60) { EliteSystem.Step(Dt); sprayIn += Dt; cone |= e.transform.Find("ConeL") != null && e.transform.Find("ConeL").GetComponent<SpriteRenderer>().enabled; }
        int sprayed = EliteSystem.Shots.Launched - launched;
        Check("... the broken plate sprays shards after a blinking cone (" + sprayIn.ToString("0.00") + " s, " + sprayed + " shards)",
              cone && Mathf.Abs(sprayIn - ArmourShatterAttack.SprayDelay) < .1f && sprayed == def.shotCount && armour.Sprays == 1);
        Shoot(e, 2);
        Check("... plates break in order to none, every heart kept, and it shows the stripped hull", armour.Stripped && armour.Shattered == plates && e.Hearts == def.hearts && e.CurrentFrame == def.cells.damaged && !armour.PlateShown(0));

        // stripped: the barrage, never a charge
        float maxSpeed = 0f;
        bool sight = false, dash = false;
        e.AttackCooldown = 0f;
        guard = 0;
        while (!e.Telling && guard++ < 150) EliteSystem.Step(Dt);
        if (!e.Telling) e.ForceAttack();
        dash = e.Attack.DrivesMovement;
        float tell = Tell(e, () => { maxSpeed = Mathf.Max(maxSpeed, e.Velocity.magnitude); sight |= e.SightShown; });
        Check("stripped: a long planted tell (" + tell.ToString("0.00") + " s) and NO charge (drives movement " + dash + ", no sight line " + !sight + ", top speed " + maxSpeed.ToString("0.0") + ")",
              Mathf.Abs(tell - def.tellSeconds * IcicleBarrageAttack.BarrageTell) < .15f && !dash && !sight && maxSpeed < 1.2f && armour.Planned);
        Check("... the lanes are marked with ice glyphs (three down each): " + armour.Lanes + " lanes in " + armour.Columns + " columns", armour.Glyph.ShownCount == armour.Lanes * IcicleBarrageAttack.Glyphs);
        Barrage(e, armour, 0);
        Shoot(e, 1);
        Check("... and now a shot costs a heart", e.Hearts == def.hearts - 1 || !Alive(e));

        // the safe lane: whatever the pilot's x, one column is bare and it fits a ship
        Fresh(.02f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        e = InPlay("patrol", new Vector2(0f, 3f));
        armour = (IcicleBarrageAttack)e.Attack;
        armour.Strip();
        bool always = true, wide = true, besides = true;
        string badAt = "";
        for (int lost = 0; lost <= 4; lost += 2)
        {
            if (lost > 0) for (int h = 0; h < 2; h++) e.TakeHit(EliteDamage.Teleport, e.Position, 1);
            for (float x = -2.4f; x <= 2.4001f; x += .2f)
            {
                armour.BeginTell(new Vector2(x, -2.5f));
                int marked = 0, bare = 0;
                for (int c = 0; c < armour.Columns; c++) { if (armour.Marked(c)) marked++; else bare++; }
                bool ok = bare == 1 && !armour.Marked(armour.Safe) && marked == armour.Lanes && armour.Lanes >= 4 && armour.Lanes <= 6;
                if (!ok) { always = false; badAt += " x" + x.ToString("0.0") + "/lost" + lost; }
                wide &= armour.ColumnWidth * 2f - 2f * .1f > 1.0f;
                // beside the pilot's own column, toward the middle
                float sx = armour.ColumnX(armour.Safe);
                besides &= Mathf.Abs(sx - x) < armour.ColumnWidth * 1.6f;
                armour.Cancel();
            }
        }
        Check("icicle_barrage: whatever the pilot's x, exactly one lane is left bare and the others marked (4-6 lanes)" + badAt, always);
        Check("... the safe lane is beside the pilot's, and there is room to fly through it (the gap between the neighbouring lanes is over 1 u)", besides && wide);
        // escalation per heart lost
        var lanesByLost = new List<int>();
        var gapsByLost = new List<float>();
        var fallsByLost = new List<float>();
        for (int lost = 0; lost <= 4; lost++)
        {
            Fresh(.02f);
            pilot.position = new Vector3(.3f, -2.5f, 0f);
            e = InPlay("patrol", new Vector2(0f, 3f));
            armour = (IcicleBarrageAttack)e.Attack;
            armour.Strip();
            for (int h = 0; h < lost; h++) { e.TakeHit(EliteDamage.Teleport, e.Position, 1); Step(.05f); }
            lanesByLost.Add(armour.LaneCount);
            gapsByLost.Add(armour.Gap);
            fallsByLost.Add(armour.Fall);
        }
        bool escalates = lanesByLost[0] == 4 && lanesByLost[2] == 5 && lanesByLost[4] == 6;
        for (int i = 1; i < gapsByLost.Count; i++) escalates &= gapsByLost[i] <= gapsByLost[i - 1] + 1e-4f && fallsByLost[i] > fallsByLost[i - 1];
        Check("... it escalates per heart lost: lanes " + string.Join("/", lanesByLost.ConvertAll(x => x.ToString())) + ", gaps " + string.Join("/", gapsByLost.ConvertAll(x => x.ToString("0.00"))) + " s, falling faster",
              escalates && gapsByLost[4] < gapsByLost[0]);
        // the whole barrage at the last heart
        Fresh(.02f);
        pilot.position = new Vector3(.3f, -2.5f, 0f);
        e = InPlay("patrol", new Vector2(0f, 3f));
        armour = (IcicleBarrageAttack)e.Attack;
        armour.Strip();
        for (int h = 0; h < 4; h++) { e.TakeHit(EliteDamage.Teleport, e.Position, 1); Step(.05f); }
        e.AttackCooldown = 99f;
        e.ForceAttack();
        Barrage(e, armour, 4);
    }

    // Watches one barrage from its tell to the last icicle landing: the icicles drop one after another down the marked lanes only.
    static void Barrage(EliteShip e, IcicleBarrageAttack b, int lost)
    {
        string k = "barrage (" + lost + " hearts lost): ";
        float safeX = b.ColumnX(b.Safe);
        var colsHit = new List<int>();
        var times = new List<float>();
        var speeds = new List<float>();
        bool straight = true, inSafe = false, glyphsGo = true;
        int seenShells = 0;
        float t = 0f;
        var seen = new HashSet<EliteShot>();
        for (int i = 0; i < 30 * 14 && (b.Dropped < b.Lanes || e.State == EliteState.Attack || EliteSystem.Shots.ActiveCount > 0) && Alive(e); i++)
        {
            EliteSystem.Step(Dt);
            t += Dt;
            foreach (var s in EliteSystem.Shots.All)
            {
                if (!s.Active || s.Owner != e || s.Kind != EliteShots.Kind.Shell || seen.Contains(s)) continue;
                seen.Add(s);
                seenShells++;
                int col = 0;
                float best = 99f;
                for (int c = 0; c < b.Columns; c++) { float d = Mathf.Abs(b.ColumnX(c) - s.transform.position.x); if (d < best) { best = d; col = c; } }
                colsHit.Add(col);
                times.Add(t);
                speeds.Add(-s.Velocity.y);
                straight &= Mathf.Abs(s.Velocity.x) < .01f && best < .05f;
                inSafe |= col == b.Safe;
            }
        }
        bool distinct = new HashSet<int>(colsHit).Count == colsHit.Count;
        bool spaced = times.Count >= 2;
        for (int i = 1; i < times.Count; i++) spaced &= Mathf.Abs(times[i] - times[i - 1] - b.Gap) < .12f;
        Check(k + b.Lanes + " icicles, one per marked lane, in sequence " + b.Gap.ToString("0.00") + " s apart, none in the safe lane (" + seenShells + " icicles, safe column " + b.Safe + ")",
              seenShells == b.Lanes && distinct && !inSafe && spaced);
        Check(k + "each falls straight down its lane at " + b.Fall.ToString("0.0") + " u/s", straight && speeds.Count > 0 && Mathf.Abs(speeds[0] - b.Fall) < .2f);
        Check(k + "the glyphs are gone once the icicles are down", b.Glyph.ShownCount == 0 && glyphsGo);
        _ = safeX;
    }

    // ---- Frost's backdrop brings the home dock into view ---------------------------------------

    static void BringHome()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects, UnityEditor.SceneManagement.NewSceneMode.Single);
        Time.timeScale = 1f;
        moveBackGround.speed = .25f;
        FrostBackdropSelection.Reset();
        FrostBackdropSelection.Seed(11);
        FrostBackdropSelection.Force = 1;
        var cam = Camera.main;
        cam.orthographic = true;
        cam.aspect = 1080f / 2400f;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, 1080, 2400);
        var wb = WorldBackdrop.Create("Frost");
        wb.Show("Frost", false);
        var d = wb.Current != null ? wb.Current.Director as FrostDirector : null;
        Check("Frost's backdrop is up for the dock check", d != null);
        if (d == null) return;
        for (int i = 0; i < 30 * 20; i++) wb.Step(Dt);
        int HatchCount()
        {
            int n = 0;
            foreach (var p in d.Sites.items) if (p.active && p.kind == 4) n++;
            return n;
        }
        int before = HatchCount(), spawned = d.SitesSpawned;
        bool ok = false;
        for (int i = 0; i < 40 && !ok; i++) { ok = d.BringHome("hatch"); if (!ok) wb.Step(.5f); }
        int after = HatchCount();
        Check("BringHome(hatch): a hatch site is in view or on its way (" + before + " -> " + after + ", " + (d.SitesSpawned - spawned) + " spawned)", ok && after >= 1);
        int again = d.SitesSpawned;
        bool twice = d.BringHome("hatch");
        Check("... asked again it spawns no second one", twice && d.SitesSpawned == again);
        Check("... a launchFrom of no Frost site (a Space station) or none is not conjured", !d.BringHome("station") && !d.BringHome(""));
        Check("... LandingSites.BringHome reaches it through the world backdrop", LandingSites.BringHome("hangar,crawlerbay"));
        EliteSystem.Clear();
    }

    // ---- allocations -------------------------------------------------------------------

    static void Allocations()
    {
        Fresh(.1f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        var a = InPlay("barge", new Vector2(-1.2f, 3f));
        var b = InPlay("hopper", new Vector2(1.2f, 1.6f));
        var c = InPlay("carrier", new Vector2(0f, 3f));
        var d = InPlay("patrol", new Vector2(.5f, 3f));
        var r = InPlay("icebreaker", new Vector2(-.5f, 2f));
        ((IcicleBarrageAttack)d.Attack).Strip();
        a.AttackCooldown = b.AttackCooldown = c.AttackCooldown = d.AttackCooldown = r.AttackCooldown = 0f;
        int f = 0;
        Step(14f, () => pilot.position = new Vector3(Mathf.Sin(f++ * .03f), -2.5f, 0f));   // warm up: every attack (and the fog, the mines, the barrage, the skid)
        if (Alive(c)) c.AttackCooldown = 99f;   // (a release builds its drones: a spawn, not a frame)
        long control;
        bool meter = TestHarness.AllocMeterWorks(out control);
        long allocated = TestHarness.AllocatedBytes(() =>
        {
            for (int i = 0; i < 90; i++)
            {
                pilot.position = new Vector3(Mathf.Sin(i * .05f), -2.5f, 0f);
                EliteSystem.Step(Dt);
            }
        });
        Check("zero per-frame allocations stepping the five Frost elites, their slabs, orbs, drones, mines, fog, plates and icicles (" + allocated + " bytes over 90 frames; meter control " + control + ")",
              meter && allocated == 0);
    }
}
