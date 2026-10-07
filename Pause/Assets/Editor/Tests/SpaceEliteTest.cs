using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// The four Space elites (Eventide Bastion, Orbit Reaver, Rift Lancer,
// Singularity Hauler): the roster, their launch sites INSIDE the Space
// backdrop's stations, planets and big asteroids (SpaceDirector's
// LandingSites, LandingSite.emerge), the director choosing each one's own
// kind of site, the launch (docked and hidden, the engine-light tell, the
// dock flare, flying out and fading in), joining, following the pilot,
// attacking, a clean despawn -- and each one's own brain + attack
// signature (bastion / ward_curtain, reaver / crescent_volley, lancer /
// rift_rail, tug / gravity_sling) with zero per-frame allocation.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod SpaceEliteTest.Run
public static class SpaceEliteTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SPE] PASS  " : "[SPE] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 30f;
    static Transform pilot;

    // key -> brain, attack, where it launches from
    static readonly (string key, string brain, string attack, LandingKind from)[] Expected =
    {
        ("space_elite_eventide_bastion", "bastion", "ward_curtain", LandingKind.Station),
        ("space_elite_orbit_reaver", "reaver", "crescent_volley", LandingKind.Planet),
        ("space_elite_rift_lancer", "lancer", "rift_rail", LandingKind.Station),
        ("space_elite_singularity_hauler", "tug", "gravity_sling", LandingKind.Asteroid),
    };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EliteCatalog.Reload();
            Roster();
            BackdropSites();
            foreach (var x in Expected) LaunchFollowAttackDespawn(EliteCatalog.Find(x.key), x.from);
            Bastion();
            Reaver();
            Lancer();
            Tug();
            Allocations();
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
        Debug.Log("[SPE] failures: " + fails);
        return fails;
    }

    // ---- fixtures (as EliteTest's) -----------------------------------------------

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

    static void Step(float seconds, System.Action each = null)
    {
        for (float t = 0f; t < seconds - 1e-4f; t += Dt)
        {
            if (each != null) each();
            EliteSystem.Step(Dt);
        }
    }

    static bool Alive(EliteShip e) => e != null && e.State != EliteState.Dead;

    // A Space site: a body drifting down the view, the ship docked inside it.
    static LandingSite Body(Vector2 at, LandingKind kind, int id = 1)
    {
        var anchor = new GameObject("~Body" + id).transform;
        anchor.position = at;
        return new LandingSite { anchor = anchor, local = Vector3.zero, scale = .3f, order = -300, id = id, kind = kind, emerge = true };
    }

    // ---- roster --------------------------------------------------------------------

    static void Roster()
    {
        var space = new List<EliteDef>();
        Check("Space has four elites (" + EliteCatalog.ForWorld(0, space) + ")", space.Count == 4);
        foreach (var x in Expected)
        {
            var d = EliteCatalog.Find(x.key);
            Check(x.key + ": defined for Space with its own brain / attack (" + (d != null ? d.brain + " / " + d.attack : "missing") + ")",
                  d != null && d.WorldIndex == 0 && d.brain == x.brain && d.attack == x.attack);
            if (d == null) continue;
            Check(x.key + ": launches from a " + x.from, LandingSite.KindOf(d.launchFrom) == x.from);
            var frames = EliteArt.Frames(d);
            var c = d.cells;
            Check(x.key + ": Codex's flight strip (landed, idle, ignition, hover, banks, damaged) is installed",
                  frames != null && frames.Length == 7 && c.parked == 0 && c.parkedIdle == 1 && c.liftoff == 2 && c.Flight0 == 3 &&
                  c.bankLeft == 4 && c.bankRight == 5 && c.damaged == 6);
            Check(x.key + ": hearts orbit tighter than the player's (" + (d.hullRadius * d.heartOrbit + d.heartSize * 1.05f).ToString("0.00") + " u)",
                  d.hullRadius * d.heartOrbit + d.heartSize * 1.05f < .52f);
            Check(x.key + ": a codex entry", global::Codex.Find(d.codexId) != null);
        }
        Check("Space is open to elites after the first 20 s", EliteDirector.Blocked(0, 60f) == null && EliteDirector.Blocked(0, 10f) == "too early");
    }

    // ---- the real Space backdrop: stations, planets, big asteroids ------------------

    static void BackdropSites()
    {
        Fresh(.3f);
        var wb = WorldBackdrop.Create("Space");
        wb.Show("Space", false);
        var sites = new List<LandingSite>();
        var seen = new HashSet<LandingKind>();
        bool emerge = true, depth = true, inView = true;
        string bad = "";
        int samples = 0;
        for (int i = 0; i < 15 * 240 && seen.Count < 3; i++)
        {
            wb.Step(1f / 15f);
            if (LandingSites.Collect(sites) == 0) continue;
            samples++;
            foreach (var st in sites)
            {
                seen.Add(st.kind);
                bool em = st.emerge && st.Valid;
                bool dp = st.order < EliteShip.PlayOrder - 2 && st.scale < .5f && st.scale > .05f;
                Vector3 p = st.Position;
                bool iv = p.y > -CameraFit.ViewTop * .5f;
                if (!(em && dp && iv) && bad.Length < 200) bad += st.kind + " order " + st.order + " scale " + st.scale.ToString("0.00") + " at " + p.ToString("0.0") + "; ";
                emerge &= em;
                depth &= dp;
                inView &= iv;
            }
        }
        string kinds = string.Join(",", seen);
        Check("the Space backdrop offers launch sites in stations, planets and big asteroids (" + kinds + ")",
              seen.Contains(LandingKind.Station) && seen.Contains(LandingKind.Planet) && seen.Contains(LandingKind.Asteroid));
        Check("... every one inside its body (emerge), behind the gameplay, small, in the upper view (" + samples + " samples; " + emerge + depth + inView + " " + bad + ")", samples > 0 && emerge && depth && inView);

        // the director, on the real backdrop: Space elites docked in its bodies, each in its own kind when free
        for (int i = 0; i < 15 * 60; i++)
        {
            wb.Step(1f / 15f);
            if (LandingSites.Collect(sites) >= 3) break;
        }
        var dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        int made = dir.SpawnGroup(0, 3);
        bool own = made > 0, docked = true, preferred = true;
        foreach (var e in EliteShip.Live)
        {
            own &= e.Def.WorldIndex == 0 && e.State == EliteState.Parked;
            docked &= e.IsDocked && e.Hull.color.a == 0f && !e.Collider.enabled;
            var want = LandingSite.KindOf(e.Def.launchFrom);
            bool offered = false;
            foreach (var st in sites) offered |= st.kind == want;
            // (its own kind unless another elite of the group took the only one)
            int same = 0;
            foreach (var o in EliteShip.Live) if (o != e && LandingSite.KindOf(o.Def.launchFrom) == want) same++;
            if (offered && same == 0) preferred &= e.Site.kind == want;
        }
        Check("the director docks Space elites in the backdrop's bodies (" + made + ")", own && made == Mathf.Min(3, sites.Count));
        Check("... hidden inside them until the launch", docked);
        Check("... each in its own kind of body when one is free", preferred);
        var first = EliteShip.Live.Count > 0 ? EliteShip.Live[0] : null;
        for (int i = 0; i < 10; i++) { wb.Step(1f / 15f); EliteSystem.Step(1f / 15f); }
        Check("... and rides its body as it drifts", first != null && first.State == EliteState.Parked && Vector2.Distance(first.transform.position, first.Site.Position) < .01f);
        Object.DestroyImmediate(dir.gameObject);
        EliteSystem.Clear();
        Object.DestroyImmediate(wb.gameObject);

        // a group's preference with synthetic bodies: one of each kind
        Fresh(.05f);
        var pads = new List<LandingSite> { Body(new Vector2(-1.4f, 3f), LandingKind.Asteroid, 1), Body(new Vector2(0f, 3.2f), LandingKind.Planet, 2),
                                           Body(new Vector2(1.4f, 3f), LandingKind.Station, 3) };
        LandingSites.Override = list => list.AddRange(pads);
        dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        bool match = true;
        int spawned = 0;
        for (int i = 0; i < 12; i++)
        {
            EliteSystem.Clear();
            spawned += dir.SpawnGroup(0, 1);
            foreach (var e in EliteShip.Live) match &= e.Site.kind == LandingSite.KindOf(e.Def.launchFrom);
        }
        Check("an elite launches from its own kind of body (" + spawned + " launches)", spawned == 12 && match);
        pads.RemoveAt(1);   // no planet in view: the Reaver takes whatever is free
        bool fallback = true;
        for (int i = 0; i < 12; i++)
        {
            EliteSystem.Clear();
            fallback &= dir.SpawnGroup(0, 1) == 1;
        }
        Check("... and any free one when its kind is not in view", fallback);
        Object.DestroyImmediate(dir.gameObject);
        LandingSites.Override = null;
    }

    // ---- the life cycle out of a body ----------------------------------------------------

    static void LaunchFollowAttackDespawn(EliteDef def, LandingKind kind)
    {
        if (def == null) { Check("space elite defined", false); return; }
        string k = def.key + ": ";
        Fresh(.05f);
        pilot.position = new Vector3(0f, -3f, 0f);
        var site = Body(new Vector2(.8f, 3f), kind);
        int flares = EliteSystem.Fx.DockFlares;
        var e = EliteShip.Create(def, site, 2.5f, new Vector2(-1.4f, 1.4f));
        Check(k + "docked inside its " + kind + ": hidden, no collider, not a target",
              e.IsDocked && e.Hull.color.a == 0f && !e.Collider.enabled && !e.CompareTag("Enimey") && e.GetComponent<ClearTarget>() == null);
        bool lightsEarly = false, lightsLate = false, rides = true;
        int guard = 0;
        while (e.State == EliteState.Parked && guard++ < 400)
        {
            site.anchor.position += Vector3.down * .2f * Dt;     // the body drifts down the view
            EliteSystem.Step(Dt);
            if (e.State != EliteState.Parked) break;
            rides &= Vector2.Distance(e.transform.position, site.anchor.position) < .01f;
            if (e.StateTime < e.ParkSeconds - EliteShip.EngineTellSeconds - .05f) lightsEarly |= e.EngineLightsOn;
            else lightsLate |= e.EngineLightsOn;
        }
        Check(k + "rides its body; only its engine lights blink at the dock, in its last parked second", rides && !lightsEarly && lightsLate);
        Check(k + "launches with a dock flare (no ground dust)", e.State == EliteState.LiftOff && EliteSystem.Fx.DockFlares == flares + 1);
        bool steps = true, grows = true, hidden = true, sawFade = false;
        float lastScale = e.HullScale;
        Vector2 start = e.transform.position;
        guard = 0;
        while (e.State == EliteState.LiftOff && guard++ < 300)
        {
            hidden &= !e.Collider.enabled && !e.CompareTag("Enimey");
            EliteSystem.Step(Dt);
            if (e.State != EliteState.LiftOff) break;
            float a = e.Hull.color.a;
            steps &= Mathf.Approximately(a, .34f) || Mathf.Approximately(a, .67f) || Mathf.Approximately(a, 1f);
            sawFade |= a < .5f;
            grows &= e.HullScale >= lastScale - 1e-4f;
            lastScale = e.HullScale;
        }
        Check(k + "flies out of the body: fades in in hard steps, grows to play size, collider off until it joins",
              steps && sawFade && grows && hidden);
        Check(k + "joins the play: full size, opaque, collider on, a hazard, in the play layer, away from the pilot (" +
              Vector2.Distance(e.Position, pilot.position).ToString("0.0") + ")",
              e.State == EliteState.Join && Mathf.Approximately(e.HullScale, 1f) && e.Hull.color.a == 1f && e.Collider.enabled &&
              e.CompareTag("Enimey") && e.Hull.sortingOrder == EliteShip.PlayOrder && Vector2.Distance(e.Position, pilot.position) >= 2f &&
              Vector2.Distance(start, e.Position) > 1f);

        // follows: the pilot crosses the board, it comes along
        float right = 0f, left = 0f, far = 0f, path = 0f;
        int nr = 0, nl = 0;
        Vector2 last = e.Position;
        e.AttackCooldown = 99f;
        for (int i = 0; i < 30 * 8; i++)
        {
            float px = i < 30 * 4 ? 1.3f : -1.3f;
            pilot.position = new Vector3(px, -2.6f, 0f);
            EliteSystem.Step(Dt);
            if (!Alive(e)) break;
            if (i >= 30 * 2 && i < 30 * 4) { right += e.Position.x; nr++; far = Mathf.Max(far, Vector2.Distance(e.Position, pilot.position)); }
            if (i >= 30 * 6) { left += e.Position.x; nl++; far = Mathf.Max(far, Vector2.Distance(e.Position, pilot.position)); }
            path += Vector2.Distance(e.Position, last);
            last = e.Position;
            e.AttackCooldown = 99f;
        }
        right /= Mathf.Max(1, nr);
        left /= Mathf.Max(1, nl);
        // (it moves with the pilot and stays within reach of it: the lancer flips flanks, so its mean x need not follow the pilot's)
        bool tracks = def.brain == "lancer" || right - left > .5f;
        Check(k + "follows the pilot across the board (mean x " + right.ToString("0.0") + " -> " + left.ToString("0.0") + ", never more than " +
              far.ToString("0.0") + " u away, " + path.ToString("0.0") + " u flown)", Alive(e) && tracks && far < 4.5f && path > 2f);

        // attacks: its own attack, then back to following
        int launched = EliteSystem.Shots.Launched, attacks = e.Attacks;
        bool told = false, acted = false, back = false;
        e.AttackCooldown = 0f;
        for (int i = 0; i < 30 * 25 && !back; i++)
        {
            pilot.position = new Vector3(Mathf.Sin(i * .02f) * .8f, -2.8f, 0f);
            EliteSystem.Step(Dt);
            if (!Alive(e)) break;
            told |= e.Telling;
            acted |= e.Acting;
            if (acted && e.State == EliteState.Follow) back = true;
        }
        Check(k + "attacks (" + e.Attack.Id + "): a wind-up, shots out, back to following (" + (EliteSystem.Shots.Launched - launched) + " shots)",
              told && acted && back && e.Attacks > attacks && EliteSystem.Shots.Launched > launched);

        // despawns cleanly: shot down, paid, gone
        float total = RunScore.Total;
        var go = e.gameObject;
        for (int i = 0; i < def.hearts && Alive(e); i++)
        {
            e.TakeShipAttack(0, 1f, e.Position);
            Step(EliteShip.GraceSeconds + .05f);
        }
        Check(k + "two hits shoot it down: paid, removed, its object gone",
              !Alive(e) && go == null && !EliteShip.Live.Contains(e) && RunScore.Total - total == ScoreRules.EliteDown);
        Step(1f);
        EliteSystem.Clear();
        Check(k + "nothing left behind after a clear (" + EliteShip.Live.Count + " live)", EliteShip.Live.Count == 0);
    }

    // ---- Eventide Bastion: bastion + ward_curtain ---------------------------------------

    static void Bastion()
    {
        Fresh(.02f);
        pilot.position = new Vector3(1.4f, -2.5f, 0f);
        var e = InPlay("bastion", new Vector2(-1f, 1f));
        var def = e.Def;
        var brain = (BastionBrain)e.Brain;
        Step(5f);
        Check("bastion holds high over the middle: lane follows the pilot, never more than laneOffset off centre (" + e.Position + ")",
              Mathf.Abs(e.Position.x - def.laneOffset) < .35f && e.Position.y > pilot.position.y + def.followDistance - .5f);
        Check("bastion is armoured (rocks don't cost it a heart)", def.armored);
        e.ForceAttack();
        var curtain = (WardCurtainAttack)e.Attack;
        bool glow = false;
        float tellSpeed = 0f;
        while (e.Telling) { EliteSystem.Step(Dt); if (!e.Telling) break; glow |= e.ChargeGlowOn; tellSpeed = Mathf.Max(tellSpeed, e.Velocity.magnitude); }
        Check("ward_curtain tell: holds still while the pods charge (" + tellSpeed.ToString("0.0") + ")", glow && tellSpeed < 1.2f);
        int before = EliteSystem.Shots.Launched;
        Step(def.actionSeconds + Dt);
        int n = curtain.Count;
        var crossings = new List<float>();
        bool pods = true;
        float podL = 0f, podR = 0f;
        foreach (var s in EliteSystem.Shots.All)
        {
            if (!s.Active || s.Kind != EliteShots.Kind.Bolt) continue;
            Vector2 v = s.Velocity, a = s.LaunchedAt;
            if (v.y >= -.01f) { pods = false; continue; }
            float k = (curtain.Row.y - a.y) / v.y;
            crossings.Add(a.x + v.x * k);
            if (a.x < e.Position.x) podL++; else podR++;
        }
        crossings.Sort();
        Vector2 gapSpot = curtain.Spot(curtain.Gap);
        bool onPilot = false, gapClear = true;
        foreach (float x in crossings)
        {
            onPilot |= Mathf.Abs(x - curtain.Row.x) < .12f;
            gapClear &= Mathf.Abs(x - gapSpot.x) > def.lobSpacing * .6f;
        }
        Check("ward_curtain: " + (n - 1) + " slow bolts out of both pods onto a row across the pilot's height (" + (EliteSystem.Shots.Launched - before) + ")",
              EliteSystem.Shots.Launched - before == n - 1 && crossings.Count == n - 1 && pods && podL > 0 && podR > 0);
        Check("... one on the pilot, a gap beside it toward the middle of the board (gap x " + gapSpot.x.ToString("0.00") + ", pilot " + curtain.Row.x.ToString("0.00") + ")",
              onPilot && gapClear && gapSpot.x < curtain.Row.x);
        Check("... slow enough to read (" + def.shotSpeed + " u/s)", def.shotSpeed <= 4f);
        // the gap is safe, standing still is not
        Fresh(.02f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        e = InPlay("bastion", new Vector2(0f, 1f));
        Step(1f);
        e.ForceAttack();
        Step(def.tellSeconds + def.actionSeconds + Dt * 2f);
        curtain = (WardCurtainAttack)e.Attack;
        float nearGap = 9f, nearPilot = 9f;
        Vector2 gap = curtain.Spot(curtain.Gap), stay = curtain.Row;
        Step(3f, () =>
        {
            foreach (var s in EliteSystem.Shots.All)
            {
                if (!s.Active) continue;
                nearGap = Mathf.Min(nearGap, Vector2.Distance(s.transform.position, gap));
                nearPilot = Mathf.Min(nearPilot, Vector2.Distance(s.transform.position, stay));
            }
        });
        Check("... a ship in the gap stays clear (closest bolt " + nearGap.ToString("0.00") + " u), one that stays put is hit (" + nearPilot.ToString("0.00") + " u)",
              nearGap > .3f && nearPilot < .15f);
    }

    // ---- Orbit Reaver: reaver + crescent_volley ----------------------------------------

    static void Reaver()
    {
        Fresh(.02f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        var e = InPlay("reaver", new Vector2(1.5f, 0f));
        var def = e.Def;
        var brain = (ReaverBrain)e.Brain;
        float lowest = 9f, minX = 9f, maxX = -9f, cruise = 0f;
        int turns = brain.Turns, cruiseN = 0;
        Step(8f, () => { cruise += e.Velocity.magnitude; cruiseN++; lowest = Mathf.Min(lowest, e.Position.y - pilot.position.y); minX = Mathf.Min(minX, e.Position.x); maxX = Mathf.Max(maxX, e.Position.x); });
        Check("reaver circles wholly above the pilot (lowest " + lowest.ToString("0.00") + " u above), swinging across it (" + minX.ToString("0.0") + ".." + maxX.ToString("0.0") + ")",
              lowest > .6f && minX < -1f && maxX > 1f);
        Check("... and turns round at the end of a lap (" + (brain.Turns - turns) + ")", brain.Turns > turns);
        e.ForceAttack();
        var volley = (CrescentVolleyAttack)e.Attack;
        Vector2 locked = volley.Aim;
        float tellSpeed = 0f;
        while (e.Telling) { EliteSystem.Step(Dt); if (e.Telling) tellSpeed = Mathf.Max(tellSpeed, e.Velocity.magnitude); }
        pilot.position = new Vector3(1.2f, -2.5f, 0f);     // the pilot moves off: the volley still goes to the locked spot
        int before = EliteSystem.Shots.Launched, spinBefore = (int)brain.Spin, turnsBefore = brain.Turns;
        float actSpeed = 0f;
        int actN = 0;
        cruise /= Mathf.Max(1, cruiseN);
        var froms = new List<Vector2>();
        bool aimed = true;
        int guard = 0;
        while (e.State == EliteState.Attack && guard++ < 200)
        {
            int was = EliteSystem.Shots.Launched;
            EliteSystem.Step(Dt);
            if (e.Acting) { actSpeed += e.Velocity.magnitude; actN++; }
            if (EliteSystem.Shots.Launched == was) continue;
            foreach (var s in EliteSystem.Shots.All)
            {
                if (!s.Active || s.Age > Dt * 1.5f) continue;
                froms.Add(s.LaunchedAt);
                Vector2 to = (locked - s.LaunchedAt).normalized;
                aimed &= Vector2.Dot(s.Velocity.normalized, to) > .995f;
            }
        }
        actSpeed /= Mathf.Max(1, actN);
        bool both = false;
        for (int i = 1; i < froms.Count; i++) both |= Vector2.Distance(froms[i], froms[0]) > .5f;
        Check("crescent_volley: keeps circling through the wind-up (" + tellSpeed.ToString("0.0") + " u/s)", tellSpeed > def.speed * .3f);
        Check("... races round while the claws take turns firing " + def.shotCount + " shards (" + (EliteSystem.Shots.Launched - before) + ", " + actSpeed.ToString("0.0") + " u/s vs " + cruise.ToString("0.0") + " cruising)",
              EliteSystem.Shots.Launched - before == def.shotCount && both && actSpeed > cruise * 1.1f);
        Check("... every shard at the spot it locked, not where the pilot went", aimed && froms.Count == def.shotCount);
        Check("... then it turns round on its orbit", brain.Turns > turnsBefore && brain.Lapped < .5f && (int)brain.Spin != spinBefore);
    }

    // ---- Rift Lancer: lancer + rift_rail ------------------------------------------------

    static void Lancer()
    {
        Fresh(.02f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        var e = InPlay("lancer", new Vector2(1.5f, 0f));
        var def = e.Def;
        var brain = (LancerBrain)e.Brain;
        Step(3f);
        float side = brain.Side;
        Check("lancer holds a flank above the pilot, nose on it (" + e.Position + ")",
              Mathf.Abs(e.Position.x - side * def.laneOffset) < .5f && e.Position.y > pilot.position.y + def.followDistance - .6f &&
              Mathf.Abs(Mathf.DeltaAngle(e.Facing, Mathf.Atan2(pilot.position.y - e.Position.y, pilot.position.x - e.Position.x) * Mathf.Rad2Deg)) < 25f);
        e.ForceAttack();
        var rail = (RiftRailAttack)e.Attack;
        bool sight = false, trackedEarly = false, lockedStill = true;
        Vector2 lockedDir = Vector2.zero;
        float tellSpeed = 0f;
        int i0 = 0;
        while (e.Telling)
        {
            // the pilot keeps moving all through the wind-up
            pilot.position = new Vector3(Mathf.Sin(i0++ * .15f) * .9f, -2.5f, 0f);
            Vector2 d0 = rail.Dir;
            EliteSystem.Step(Dt);
            if (!e.Telling) break;
            sight |= e.SightShown;
            tellSpeed = Mathf.Max(tellSpeed, e.Velocity.magnitude);
            if (!rail.Locked) trackedEarly |= Vector2.Dot(d0, rail.Dir) < .9999f;
            else { if (lockedDir == Vector2.zero) lockedDir = rail.Dir; lockedStill &= Vector2.Dot(lockedDir, rail.Dir) > .9999f; }
        }
        pilot.position = new Vector3(0f, -2.5f, 0f);
        Check("rift_rail tell: planted, a sight line tracks the pilot, then locks (" + tellSpeed.ToString("0.0") + ")", sight && trackedEarly && lockedStill && tellSpeed < 1.2f);
        int before = EliteSystem.Shots.Launched;
        Step(def.actionSeconds + Dt);
        int rails = 0;
        bool parallel = true, fast = true;
        foreach (var s in EliteSystem.Shots.All)
        {
            if (!s.Active) continue;
            rails++;
            parallel &= Vector2.Dot(s.Velocity.normalized, lockedDir) > .999f;
            fast &= s.Velocity.magnitude > def.shotSpeed * .95f;
        }
        Check("... then a rail of " + def.shotCount + " fast bolts straight down the locked line (" + rails + ")",
              EliteSystem.Shots.Launched - before == def.shotCount && rails == def.shotCount && parallel && fast && !e.SightShown);
        Step(.3f);
        Check("... and it dashes across to its other flank", brain.Side == -side && brain.Crossings == 1);
        float speed = 0f;
        Step(1.2f, () => speed = Mathf.Max(speed, e.Velocity.magnitude));
        Check("... fast (" + speed.ToString("0.0") + " u/s) and settles there", speed > def.speed * 1.2f &&
              Mathf.Abs(e.Position.x - (pilot.position.x - side * def.laneOffset)) < .9f);
    }

    // ---- Singularity Hauler: tug + gravity_sling ----------------------------------------

    static void Tug()
    {
        Fresh(.05f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        var e = InPlay("tug", new Vector2(.5f, 1f));
        var def = e.Def;
        var brain = (TugBrain)e.Brain;
        Step(4f);
        float side = brain.Side;
        Check("tug hangs ahead of the pilot, a lane to one side (" + e.Position + ")",
              Mathf.Abs(e.Position.x - side * def.laneOffset) < .45f && Mathf.Abs(e.Position.y - (pilot.position.y + def.followDistance)) < .5f);
        e.ForceAttack();
        var sling = (GravitySlingAttack)e.Attack;
        bool glow = false;
        float tellSpeed = 0f;
        while (e.Telling) { EliteSystem.Step(Dt); if (!e.Telling) break; glow |= e.ChargeGlowOn; tellSpeed = Mathf.Max(tellSpeed, e.Velocity.magnitude); }
        Check("gravity_sling tell: holds while the core charges (" + tellSpeed.ToString("0.0") + ")", glow && tellSpeed < 1.2f);
        var shots = new List<EliteShot>();
        var closest = new List<float>();
        int guard = 0;
        bool marked = true;
        while ((e.State == EliteState.Attack || AnySlung()) && guard++ < 200)
        {
            EliteSystem.Step(Dt);
            foreach (var s in EliteSystem.Shots.All)
            {
                if (!s.Active || !s.Slung) continue;
                int i = shots.IndexOf(s);
                if (i < 0) { shots.Add(s); closest.Add(9f); i = shots.Count - 1; marked &= s.MarkShown; }
                closest[i] = Mathf.Min(closest[i], Vector2.Distance(s.transform.position, s.SlingTarget));
            }
        }
        bool through = shots.Count > 0, l = false, r = false;
        foreach (float c in closest) through &= c < .2f;
        foreach (var s in shots) { if (s.LaunchedAt.x < e.Position.x) l = true; else r = true; }
        Check("gravity_sling: " + def.shotCount + " shots flung out of both tow claws (" + shots.Count + ")", shots.Count == def.shotCount && l && r);
        Check("... each whipped round a curve through the ringed well (closest " + (closest.Count > 0 ? closest[0].ToString("0.00") : "-") + " u)", through && marked);
        Check("... the well lies ahead of the pilot, inside the rails (" + sling.Well + ")",
              sling.Well.y > pilot.position.y && Mathf.Abs(sling.Well.x) < EliteSystem.RailEdge);
        Step(.2f);
        Check("... then the tug swaps sides", brain.Side == -side);

        // armoured: a rock in its way doesn't cost it a heart
        Fresh(.05f);
        e = InPlay("tug", new Vector2(0f, 1f));
        var rock = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Rock), e.Position, Quaternion.identity);
        ClearTarget.Ensure(rock);
        EliteSystem.Step(Dt);
        Check("the hauler is armoured: smashes a rock without losing a heart", rock == null && e.Hearts == def.hearts);
    }

    static bool AnySlung()
    {
        foreach (var s in EliteSystem.Shots.All) if (s.Slung) return true;
        return false;
    }

    // ---- allocations ------------------------------------------------------------------

    static void Allocations()
    {
        Fresh(.1f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        var a = InPlay("bastion", new Vector2(0f, 2.5f));
        var b = InPlay("reaver", new Vector2(-1.5f, 0f));
        var c = InPlay("lancer", new Vector2(1.5f, 0f));
        var d = InPlay("tug", new Vector2(-1f, 1.2f));
        a.AttackCooldown = b.AttackCooldown = c.AttackCooldown = d.AttackCooldown = 0f;
        Step(5f, () => pilot.position = new Vector3(Mathf.Sin(Time.frameCount * .01f), -2.5f, 0f));   // warm up: every attack once
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 90; i++)
        {
            pilot.position = new Vector3(Mathf.Sin(i * .05f), -2.5f, 0f);
            EliteSystem.Step(Dt);
        }
        long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
        Check("zero per-frame allocations stepping the four Space elites (" + allocated + " bytes over 90 frames)", allocated == 0);
    }
}
