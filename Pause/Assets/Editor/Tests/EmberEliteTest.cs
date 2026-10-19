using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Ember's six elites as mini-boss fights (EmberElites.cs): the roster (hearts, hiding, a dock each, never pink, the base brains and attacks
// still Storm's and Desert's), their strips on the hull, the director launching them from Ember's own kinds of site (and BringHome asking
// the backdrop for one), the whole life cycle off a pad, and each one's brain and attack in simulation -- Sunstoke's lance passes (a thin
// sight line locked, rammed across the whole board from off the view and out of the far edge, needles back along the lane, a new edge each
// time, two passes once hurt), the Coalrunner's strafing runs across the top third (in one side, out the other, bolt lanes drawn and locked,
// coal clumps that bounce at <= 2 hearts), the Vulture's off-screen circle and ringed dive (an offset dive for every heart lost), the
// Wraith's perches and ember clouds (ringed first), the Kilnback's dump and spill and the Cauterizer's shell, double lane and firebombs --
// keeping their distance, never following, always leaving a gap -- tell timings, clean-up on death and zero per-frame allocation.
//
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites EmberEliteTest
public static class EmberEliteTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[EME] PASS  " : "[EME] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 30f;
    static Transform pilot;

    // key -> brain, attack, launchFrom, hearts, hides when hurt
    static readonly (string key, string brain, string attack, string from, int hearts, bool hides)[] Expected =
    {
        ("ember_elite_sunstoke", "stoker", "lance_pass", "kiln,foundryhangar", 3, true),
        ("ember_elite_coalrunner", "strafer", "coal_strafe", "crucible,magmabay", 3, true),
        ("ember_elite_brass_vulture", "scavenger", "scrap_dive", "brassgantry,slagpad", 3, true),
        ("ember_elite_ash_wraith", "ashling", "ember_veil", "kiln,hatch", 3, true),
        ("ember_elite_kilnback", "kiln", "kiln_dump", "crucible,furnacebay", 5, false),
        ("ember_elite_cauterizer", "furnace", "fire_mortar", "bunker,furnacebay", 5, false),
    };

    // Storm's and Desert's elites keep flying the BASE brains and attacks, unchanged.
    static readonly (string key, string brain, string attack)[] BaseUsers =
    {
        ("storm_elite_skyhook_harrier", "interceptor", "lance_dash"),
        ("storm_elite_refinery_tender", "hauler", "slag_drop"),
        ("storm_elite_rod_dancer", "skirmisher", "blink_shards"),
        ("storm_elite_anvil_cloud", "siege", "siege_cannon"),
        ("desert_elite_dune_stalker", "interceptor", "lance_dash"),
        ("desert_elite_caravan_hauler", "hauler", "slag_drop"),
        ("desert_elite_lens_matriarch", "skirmisher", "blink_shards"),
        ("desert_elite_glass_cannon", "siege", "siege_cannon"),
        ("desert_elite_scorpion_walker", "striker", "claw_dive"),
    };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EliteCatalog.Reload();
            BaseEliteDefs.Reset();
            int tooShort = AttackPreview.TooShort;
            Roster();
            Sites();
            foreach (var x in Expected) Cycle(EliteCatalog.Find(x.key));
            Stoker();
            Strafer();
            Scavenger();
            Ashling();
            Kiln();
            Furnace();
            Check("every telegraph the Ember elites drew showed at least " + AttackPreview.MinLead + " s before it went live (" + (AttackPreview.TooShort - tooShort) + " too short)", AttackPreview.TooShort == tooShort);
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
        Debug.Log("[EME] failures: " + fails);
        return fails;
    }

    // ---- fixtures (as FrostEliteTest's) ---------------------------------------------

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
        Random.InitState(1357);
    }

    static EliteDef Def(string brain)
    {
        foreach (var d in EliteCatalog.All) if (d.brain == brain && d.world == "ember") return d;
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

    static void WaitBack(EliteShip e, float seconds = 20f)
    {
        for (float t = 0f; t < seconds && Alive(e) && e.Hidden; t += Dt) EliteSystem.Step(Dt);
    }

    // A heart off it past its grace (a pause jump's cause), waiting out the hide.
    static void LoseHeart(EliteShip e)
    {
        if (!Alive(e)) return;
        e.TakeHit(EliteDamage.Teleport, e.Position, 1);
        Step(.1f);
        WaitBack(e);
        Step(EliteShip.GraceSeconds + .05f);
    }

    // ---- roster ------------------------------------------------------------------------

    static void Roster()
    {
        var ember = new List<EliteDef>();
        Check("Ember has six elites (" + EliteCatalog.ForWorld(3, ember) + ")", ember.Count == 6);
        var brains = new HashSet<string>();
        var attacks = new HashSet<string>();
        var docks = new HashSet<string>();
        foreach (var x in Expected)
        {
            var d = EliteCatalog.Find(x.key);
            Check(x.key + ": defined for Ember with its own brain / attack (" + (d != null ? d.brain + " / " + d.attack : "missing") + "), none of the base ids",
                  d != null && d.WorldIndex == 3 && d.brain == x.brain && d.attack == x.attack && brains.Add(d.brain) && attacks.Add(d.attack) &&
                  System.Array.IndexOf(EliteBrains.Ids, d.brain) >= 0 && System.Array.IndexOf(EliteAttacks.Ids, d.attack) >= 0 &&
                  EliteBrains.Create(d.brain).Id == d.brain && EliteAttacks.Create(d.attack).Id == d.attack);
            if (d == null) continue;
            Check(x.key + ": launches from " + d.launchFrom + ", its own pair of docks (one of the four launch-strip kinds first), all Ember's, unlike every other Ember elite's",
                  d.launchFrom == x.from && docks.Add(d.launchFrom) && LandingSite.KindOf(d.launchFrom) != null &&
                  System.Array.IndexOf(EmberTuning.SiteKinds, LandingSite.KindOf(d.launchFrom).Value) >= 0);
            Check(x.key + ": " + x.hearts + " hearts" + (d.armored ? " (armoured)" : "") + ", " + (x.hides ? "hides when hurt (" + d.hideSeconds + " s)" : "no hiding"),
                  d.hearts == x.hearts && d.armored == (x.hearts == 5) && d.hurtHide == x.hides && (!x.hides || d.hideSeconds >= 2f));
            Check(x.key + ": Ember's cue colours, never pink (shot " + d.ShotColor + ")", !WorldCue.IsPink(d.ShotColor) && !WorldCue.IsPink(d.ShotCore) && !WorldCue.IsPink(d.HeartColor));
            Check(x.key + ": not oversized (cell " + d.cellWorldSize + " u, hull " + d.hullRadius + " u)", d.cellWorldSize <= 2.1f && d.hullRadius <= .45f);
            float orbit = d.hullRadius * d.heartOrbit + d.heartSize * 1.05f;
            Check(x.key + ": hearts orbiting tighter than the player's (" + orbit.ToString("0.00") + " u)", orbit < .52f);
            var frames = EliteArt.Frames(d);
            Check(x.key + ": the painted strip loads with the Ember HD cell map", frames != null && frames.Length >= d.cells.Count && d.cells.flight.Length == 2 && d.cells.damaged == 7);
            Check(x.key + ": a codex entry", global::Codex.Find(d.codexId) != null);
            Check(x.key + ": has muzzles and nozzles (EliteTest checks them on the art)", d.muzzles.Length > 0 && d.nozzles.Length > 0);
        }
        foreach (var x in BaseUsers)
        {
            var d = EliteCatalog.Find(x.key);
            Check(x.key + ": still flies the unchanged base " + x.brain + " / " + x.attack, d != null && d.brain == x.brain && d.attack == x.attack);
        }
        Check("the base brains and attacks are still registered and build as themselves",
              EliteBrains.Create("interceptor").Id == "interceptor" && EliteBrains.Create("gunship").Id == "gunship" && EliteBrains.Create("striker").Id == "striker" &&
              EliteBrains.Create("hauler").Id == "hauler" && EliteBrains.Create("skirmisher").Id == "skirmisher" && EliteBrains.Create("siege").Id == "siege" &&
              EliteAttacks.Create("lance_dash").Id == "lance_dash" && EliteAttacks.Create("broadside").Id == "broadside" && EliteAttacks.Create("claw_dive").Id == "claw_dive" &&
              EliteAttacks.Create("slag_drop").Id == "slag_drop" && EliteAttacks.Create("blink_shards").Id == "blink_shards" && EliteAttacks.Create("siege_cannon").Id == "siege_cannon");
    }

    static bool OnHull(Texture2D tex, EliteDef d, ElitePoint p)
    {
        int cell = d.cells.Flight0, n = d.cellPixels;
        bool hit = false;
        int r = 2 * n / 192;
        for (int dy = -r; dy <= r && !hit; dy++)
            for (int dx = -r; dx <= r && !hit; dx++)
            {
                int x = cell * n + Mathf.RoundToInt(p.x) + dx, y = n - 1 - (Mathf.RoundToInt(p.y) + dy);
                hit = tex.GetPixel(x, y).a >= .5f;
            }
        return hit && d.PixelToLocal(p.x, p.y).magnitude < d.cellWorldSize * .42f;
    }

    // ---- the director: each from its own kind of Ember site ------------------------------

    static void Sites()
    {
        Fresh(.05f);
        var pads = new List<LandingSite>
        {
            Pad(new Vector2(-1.6f, 3f), LandingKind.FoundryHangar, 1), Pad(new Vector2(-.8f, 3.3f), LandingKind.MagmaBay, 2),
            Pad(new Vector2(0f, 3f), LandingKind.SlagPad, 3), Pad(new Vector2(.8f, 3.3f), LandingKind.FurnaceBay, 4),
            Pad(new Vector2(1.6f, 3f), LandingKind.Hatch, 5),
            Pad(new Vector2(-1.2f, 2.6f), LandingKind.CrucibleSilo, 6), Pad(new Vector2(-.4f, 2.8f), LandingKind.MoatBunker, 7),
            Pad(new Vector2(.4f, 2.6f), LandingKind.BrassGantry, 8), Pad(new Vector2(1.2f, 2.8f), LandingKind.KilnMouth, 9),
        };
        LandingSites.Override = list => list.AddRange(pads);
        var dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        int spawned = 0, mine = 0;
        bool match = true;
        var seen = new HashSet<string>();
        for (int i = 0; i < 120; i++)
        {
            EliteSystem.Clear();
            spawned += dir.SpawnGroup(3, 1);
            foreach (var e in EliteShip.Live)
            {
                mine++;
                seen.Add(e.Def.key);
                match &= LandingSite.Accepts(e.Def.launchFrom, e.Site.kind);
            }
        }
        Check("each Ember elite launches from its own kind of site (" + mine + " of " + spawned + " launches, " + seen.Count + " kinds of elite)", spawned == 120 && mine == 120 && match && seen.Count == 6);
        pads.Clear();
        pads.Add(Pad(new Vector2(0f, 3f), LandingKind.Ground, 9));
        bool fallback = true;
        for (int i = 0; i < 12; i++) { EliteSystem.Clear(); fallback &= dir.SpawnGroup(3, 1) == 1; }
        Check("... and any free site when none of theirs is in view", fallback);
        Object.DestroyImmediate(dir.gameObject);
        LandingSites.Override = null;
    }

    // ---- the life cycle off a pad -------------------------------------------------------

    static void Cycle(EliteDef def)
    {
        if (def == null) { Check("ember elite defined", false); return; }
        string k = def.key + ": ";
        Fresh(.05f);
        pilot.position = new Vector3(0f, -3f, 0f);
        var site = Pad(new Vector2(.8f, 3f), LandingSite.KindOf(def.launchFrom) ?? LandingKind.FurnaceBay, 1);
        var e = EliteShip.Create(def, site, 2.2f, new Vector2(-1.2f, 1.4f));
        Check(k + "parked on its pad: no collider, not a target", e.State == EliteState.Parked && !e.Collider.enabled && e.GetComponent<ClearTarget>() == null);
        bool light = false;
        int guard = 0;
        while (e.State != EliteState.Follow && guard++ < 400) { EliteSystem.Step(Dt); light |= e.LaunchLightOn; }
        Check(k + "one small blinking light in the launch tell, then lifts off, joins (collider on, a hazard) and follows", light && e.State == EliteState.Follow && (e.Offstage || (e.Collider.enabled && e.CompareTag("Enimey"))));   // (a roamer may already be out past the rail)
        int launched = EliteSystem.Shots.Launched, attacks = e.Attacks;
        bool told = false, acted = false, back = false;
        e.AttackCooldown = 0f;
        for (int i = 0; i < 30 * 45 && !back; i++)
        {
            pilot.position = new Vector3(Mathf.Sin(i * .02f) * .8f, -2.8f, 0f);
            EliteSystem.Step(Dt);
            if (!Alive(e)) break;
            told |= e.Telling;
            acted |= e.Acting;
            if (acted && e.State == EliteState.Follow) back = true;
        }
        Check(k + "attacks (" + e.Attack.Id + "): a wind-up, then shots out, back to following", told && acted && back && EliteSystem.Shots.Launched > launched && e.Attacks > attacks);
        float total = RunScore.Total;
        for (int i = 0; i < 12 && Alive(e); i++) { e.TakeHit(EliteDamage.Teleport, e.Position, 1); Step(.1f); WaitBack(e); Step(.6f); }
        bool clean = !EliteShip.Live.Contains(e);
        foreach (var s in EliteSystem.Shots.All) clean &= !(s.Active && s.Owner == e && s.Kind == EliteShots.Kind.Orb);
        Check(k + "brought down: paid, gone, its clouds / bombs with it", !Alive(e) && clean && RunScore.Total - total >= ScoreRules.EliteDown);
        EliteSystem.Clear();
    }

    // ---- Sunstoke: stoker + lance_pass ---------------------------------------------------

    static void Stoker()
    {
        Fresh(.05f);
        var e = InPlay("stoker", new Vector2(1.2f, 1.5f));
        var def = e.Def;
        var brain = (StokerBrain)e.Brain;
        var lance = (LancePassAttack)e.Attack;
        e.AttackCooldown = 0f;
        var edges = new List<int>();
        float nearest = 99f, tellDrift = 0f, locked = 0f;
        bool sight = false, lockedSame = true, startsOff = true, exitsOff = true, ploughed = false, noHeart = true, untouchable = true, needles = false;
        Vector2 lockDir = Vector2.zero, tellPos = Vector2.zero;
        int startedSeen = 0, passDone = 0;
        bool inAct = false;
        Rock0 = null;
        for (int i = 0; i < 30 * 70 && edges.Count < 5; i++)
        {
            pilot.position = new Vector3(Mathf.Sin(i * .03f) * 1.4f, -2.5f, 0f);
            EliteSystem.Step(Dt);
            if (!Alive(e)) break;
            if (!e.Offstage && brain.Phase != StokerBrain.Stage.Run) nearest = Mathf.Min(nearest, Vector2.Distance(e.Position, pilot.position));
            if (e.Telling)
            {
                if (tellPos == Vector2.zero) tellPos = e.Position;
                tellDrift = Mathf.Max(tellDrift, Vector2.Distance(e.Position, tellPos));
                sight |= e.SightShown;
                if (e.AimLocked) { if (lockDir == Vector2.zero) lockDir = lance.Dir; else lockedSame &= Vector2.Distance(lockDir, lance.Dir) < .001f; }
            }
            else tellPos = Vector2.zero;
            if (brain.PassesStarted > startedSeen)
            {
                startedSeen = brain.PassesStarted;
                edges.Add(brain.Edge);
                startsOff &= e.Offstage;
                lockedSame &= lockDir != Vector2.zero && Vector2.Distance(lockDir, lance.Dir) < .001f;
                locked += 1f;
                if (edges.Count == 1) Rock0 = Rock(e.Position + lance.Dir * 3.2f);
                lockDir = Vector2.zero;
            }
            if (e.Acting)
            {
                inAct = true;
                noHeart &= e.Hearts == def.hearts;
            }
            else if (inAct)
            {
                inAct = false;
                passDone++;
                exitsOff &= e.Offstage && !e.Collider.enabled;
                untouchable &= !e.CompareTag("Enimey");
                needles |= lance.NeedlesFlung;
            }
        }
        Check("lance pass: a thin sight line while it waits at its edge, holding still on it (drift " + tellDrift.ToString("0.00") + ")", sight && tellDrift < .5f);
        Check("... the lane is locked at the tell (it does not change through the ram), and outside a pass it keeps its distance (" + nearest.ToString("0.0") + " u at the closest)", lockedSame && edges.Count >= 3 && nearest > 3f);
        Check("... a pass starts off the view and ends off it, untouchable out there (" + passDone + " passes)", startsOff && exitsOff && untouchable && passDone >= 2);
        Check("... it ploughs the rock in its lane without losing a heart", Rock0 == null && e.Ploughed >= 1 && noHeart);
        Check("... heat needles flung back along the lane as it leaves the board", needles);
        bool other = edges.Count >= 3;
        for (int i = 1; i < edges.Count; i++) other &= edges[i] != edges[i - 1];
        Check("... and every pass from another edge than the last (" + string.Join(",", edges) + ")", other);

        // a hit: it hides, comes back, and the passes come in pairs
        Fresh(.05f);
        e = InPlay("stoker", new Vector2(0f, 2f));
        brain = (StokerBrain)e.Brain;
        e.AttackCooldown = 0f;
        Check("stoker: a single pass per cycle at full hearts", brain.PairSize == 1);
        Step(1f);
        e.TakeShipAttack(0, 1f, e.Position);
        Check("... a hit sends it hiding", e.Hidden && e.Hearts == def.hearts - 1);
        WaitBack(e);
        Check("... and back, two passes back to back (" + brain.PairSize + ")", !e.Hidden && brain.PairSize == 2);
        bool second = false;
        int doneAt = brain.Passes;
        for (int i = 0; i < 30 * 40 && !second; i++)
        {
            EliteSystem.Step(Dt);
            if (!Alive(e)) break;
            if (brain.Passes > doneAt && brain.Phase == StokerBrain.Stage.Away && brain.PassesLeft == 1) second = true;
        }
        Check("... the second follows the first after a short gap (PairGap " + StokerBrain.PairGap + " s)", second);
        EliteSystem.Clear();
    }

    static GameObject Rock0;

    static GameObject Rock(Vector2 at)
    {
        var go = EnemyFactory.Create(EnemyRoster.One(3, EnemyRole.Rock), at, Quaternion.identity);
        ClearTarget.Ensure(go);
        return go;
    }

    // ---- Coalrunner: strafer + coal_strafe -----------------------------------------------

    static void Strafer()
    {
        Fresh(.05f);
        var e = InPlay("strafer", new Vector2(0f, 2f));
        var def = e.Def;
        var brain = (StraferBrain)e.Brain;
        var strafe = (CoalStrafeAttack)e.Attack;
        e.AttackCooldown = 0f;
        float lo, hi;
        StraferBrain.Band(def, out lo, out hi);
        float nearest = 99f, minY = 99f, maxY = -99f, offFirst = 0f;
        var sides = new List<float>();
        bool crossed = true, entersOff = true, lanes = false, lockedAngles = true, bolts = false, noClump = true;
        int runSeen = 0, volleys = 0, lanesN = 0;
        float firstAngle = 0f, runMin = 99f, runMax = -99f;
        float startX = 0f;
        for (int i = 0; i < 30 * 40 && sides.Count < 4; i++)
        {
            pilot.position = new Vector3(Mathf.Sin(i * .03f) * 1.4f, -2.5f, 0f);
            EliteSystem.Step(Dt);
            if (!Alive(e)) break;
            if (!e.Offstage) nearest = Mathf.Min(nearest, Vector2.Distance(e.Position, pilot.position));
            if (brain.Runs > runSeen)
            {
                if (runSeen > 0) crossed &= (startX < 0f ? runMax > 2.5f : runMin < -2.5f);
                runSeen = brain.Runs;
                sides.Add(Mathf.Sign(brain.Start.x));
                startX = brain.Start.x;
                entersOff &= e.Offstage;
                runMin = 99f; runMax = -99f;
            }
            if (brain.Phase == StraferBrain.Stage.Run)
            {
                runMin = Mathf.Min(runMin, e.Position.x); runMax = Mathf.Max(runMax, e.Position.x);
                if (!e.Offstage) { minY = Mathf.Min(minY, e.Position.y); maxY = Mathf.Max(maxY, e.Position.y); }
            }
            if (e.Telling)
            {
                if (strafe.Telegraph.Showing) { lanes = true; lanesN = strafe.Telegraph.Loops; firstAngle = strafe.Angle(0); }
                noClump &= strafe.Clumps == 0;
            }
            if (e.Acting) { bolts |= ActiveShots(e, EliteShots.Kind.Bolt) > 0; lockedAngles &= Mathf.Approximately(firstAngle, strafe.Angle(0)); }
            volleys = Mathf.Max(volleys, brain.Volleys);
        }
        offFirst = nearest;
        bool alternates = sides.Count >= 3;
        for (int i = 1; i < sides.Count; i++) alternates &= sides[i] == -sides[i - 1];
        Check("coal strafe: runs enter off the view from alternating sides (" + string.Join(",", sides) + ") and leave by the other", alternates && entersOff && crossed);
        Check("... across the top third only (y " + minY.ToString("0.0") + " .. " + maxY.ToString("0.0") + ", band " + lo.ToString("0.0") + " .. " + hi.ToString("0.0") + "), never nearer the pilot than " + offFirst.ToString("0.0") + " u",
              minY >= lo - .3f && maxY <= EliteSystem.ViewTop && offFirst > 4f);
        Check("... the bolt lanes are drawn at the tell (" + lanesN + " thin outlines) and the bolts leave down them (angles locked), a volley or two a run (" + volleys + ")",
              lanes && lanesN >= 2 && lockedAngles && bolts && volleys >= 1 && noClump);

        // coal: none at 3 hearts, one at 2, two at 1; they skid and bounce
        Check("coal clumps: none at 3 hearts, one at 2, two at 1 (" + strafe.ClumpsFor(3) + "/" + strafe.ClumpsFor(2) + "/" + strafe.ClumpsFor(1) + ")",
              strafe.ClumpsFor(3) == 0 && strafe.ClumpsFor(2) == 1 && strafe.ClumpsFor(1) == 2);
        LoseHeart(e);
        Check("... hurt (hid, came back): " + e.Hearts + " hearts", Alive(e) && e.Hearts == def.hearts - 1 && !e.Hidden);
        bool clump = false, bounced = false, drawn = false;
        e.AttackCooldown = 0f;
        for (int i = 0; i < 30 * 40 && !(bounced && drawn); i++)
        {
            pilot.position = new Vector3(Mathf.Sin(i * .03f) * 1.4f, -2.5f, 0f);
            EliteSystem.Step(Dt);
            if (!Alive(e)) break;
            if (e.Telling && strafe.Clumps >= 1) drawn |= strafe.Telegraph.Showing;
            foreach (var s in EliteSystem.Shots.All)
                if (s.Active && s.Owner == e && s.Kind == EliteShots.Kind.Slag && s.Skidding) { clump = true; bounced |= s.Bounced >= 1; }
        }
        Check("... then clumps skid down the board (path drawn at the tell) and glance off a rail", clump && drawn && bounced);
        EliteSystem.Clear();
    }

    // ---- Brass Vulture: scavenger + scrap_dive -------------------------------------------

    static void Scavenger()
    {
        Fresh(.05f);
        var e = InPlay("scavenger", new Vector2(0f, 2f));
        var def = e.Def;
        var brain = (ScavengerBrain)e.Brain;
        var dive = (ScrapDiveAttack)e.Attack;
        e.AttackCooldown = 0f;
        float nearest = 99f;
        bool circleOff = true, ring = false, ringLocked = true, through = false, flung = false, backOff = true, circled = false;
        float minToSpot = 99f;
        Vector2 lockedSpot = Vector2.zero;
        int divesSeen = 0, done = 0;
        bool inDive = false;
        for (int i = 0; i < 30 * 40 && done < 3; i++)
        {
            pilot.position = new Vector3(Mathf.Sin(i * .03f) * 1.2f, -2.5f, 0f);
            EliteSystem.Step(Dt);
            if (!Alive(e)) break;
            if (!e.Offstage && brain.Phase == ScavengerBrain.Stage.Circle) nearest = Mathf.Min(nearest, Vector2.Distance(e.Position, pilot.position));
            if (brain.Phase == ScavengerBrain.Stage.Circle && e.Position.y > EliteSystem.ViewTop)
            {
                circled = true;
                circleOff &= e.Offstage;
            }
            else if (brain.Phase == ScavengerBrain.Stage.Circle && e.Position.y < EliteSystem.ViewTop - .2f) circleOff = false;
            if (e.Telling)
            {
                ring |= dive.Marks.Shown(0);
                if (e.AimLocked) { if (lockedSpot == Vector2.zero) lockedSpot = dive.Spot; else ringLocked &= Vector2.Distance(lockedSpot, dive.Spot) < .001f; }
            }
            if (brain.Dives > divesSeen)
            {
                divesSeen = brain.Dives;
                ringLocked &= lockedSpot != Vector2.zero && Vector2.Distance(lockedSpot, dive.Spot) < .001f;
                lockedSpot = Vector2.zero;
                minToSpot = 99f;
            }
            if (e.Acting)
            {
                inDive = true;
                minToSpot = Mathf.Min(minToSpot, Vector2.Distance(e.Position, dive.Spot));
                flung |= dive.Flung;
            }
            else if (inDive)
            {
                inDive = false;
                done++;
                through |= minToSpot < .6f;
                backOff &= e.Offstage;
            }
        }
        Check("scrap dive: it circles wholly off the view (" + circled + ") and keeps its distance (" + nearest.ToString("0.0") + " u)", circled && circleOff && nearest > 3f);
        Check("... a thin ring marks the spot at the tell, locked by the commit; it dives straight through it (" + minToSpot.ToString("0.00") + " u) flinging scrap", ring && ringLocked && through && flung);
        Check("... and pulls back up off the view (" + done + " dives)", backOff && done >= 2);

        // every heart lost: another, offset dive
        Fresh(.05f);
        e = InPlay("scavenger", new Vector2(0f, 2f));
        brain = (ScavengerBrain)e.Brain;
        dive = (ScrapDiveAttack)e.Attack;
        Check("a cycle is one dive at full hearts (" + brain.PlanDives + ")", brain.PlanDives == 1);
        LoseHeart(e);
        Check("... two after a lost heart (" + brain.PlanDives + ")", brain.PlanDives == 2);
        LoseHeart(e);
        Check("... three after two (" + brain.PlanDives + ")", brain.PlanDives == 3);
        var pos = new Vector2(.4f, -2.5f);
        Vector2 s0 = dive.SpotFor(pos, 0), s1 = dive.SpotFor(pos, 1), s2 = dive.SpotFor(pos, 2);
        Check("... the later dives ring spots offset from the first (" + s0.x.ToString("0.0") + ", " + s1.x.ToString("0.0") + ", " + s2.x.ToString("0.0") + ")",
              Mathf.Abs(s1.x - s0.x) >= 1.2f && Mathf.Abs(s2.x - s0.x) >= 1.2f && Mathf.Abs(s2.x - s1.x) >= 1.2f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        e.AttackCooldown = 0f;
        int starts = brain.Dives, seenIdx = 0;
        for (int i = 0; i < 30 * 40 && brain.Dives - starts < 3; i++)
        {
            EliteSystem.Step(Dt);
            if (!Alive(e)) break;
            seenIdx = Mathf.Max(seenIdx, brain.DiveIndex);
        }
        Check("... and with two hearts lost it dives three times in a cycle (" + (brain.Dives - starts) + ", index " + seenIdx + ")", brain.Dives - starts >= 3 && seenIdx >= 2);
        EliteSystem.Clear();
    }

    // ---- Ash Wraith: ashling + ember_veil -------------------------------------------------

    static void Ashling()
    {
        Fresh(.05f);
        var e = InPlay("ashling", new Vector2(1f, 2f));
        var def = e.Def;
        var brain = (AshlingBrain)e.Brain;
        var veil = (EmberVeilAttack)e.Attack;
        e.AttackCooldown = 0f;
        var perches = new List<int>();
        float nearest = 99f;
        bool ringed = false, cloud = false, expired = false, fanned = false;
        int attacksSeen = 0;
        EliteShot cloudShot = null;
        float cloudAge = 0f;
        for (int i = 0; i < 30 * 50 && perches.Count < 6; i++)
        {
            EliteSystem.Step(Dt);
            if (!Alive(e)) break;
            nearest = Mathf.Min(nearest, Vector2.Distance(e.Position, pilot.position));
            if (e.Telling) ringed |= veil.Marks.Shown(0) && Vector2.Distance(veil.Marks.At(0), veil.CloudAt) < .01f && veil.Marks.Shown(1);
            if (e.Attacks > attacksSeen && e.Acting)
            {
                attacksSeen = e.Attacks;
                foreach (var s in EliteSystem.Shots.All)
                    if (s.Active && s.Owner == e && s.Kind == EliteShots.Kind.Orb && cloudShot == null) { cloudShot = s; cloud = true; cloudAge = 0f; }
                fanned |= ActiveShots(e, EliteShots.Kind.Shard) >= def.shotCount;
            }
            if (e.Attacks > perches.Count && !e.Telling && !e.Acting) perches.Add(brain.Perch);
            if (cloudShot != null) { cloudAge += Dt; if (!cloudShot.Active) { expired = cloudAge < def.hazardSeconds + 1f; cloudShot = null; } }
        }
        bool changed = perches.Count >= 3;
        for (int i = 1; i < perches.Count; i++) changed &= perches[i] != perches[i - 1];
        var distinct = new HashSet<int>(perches);
        Check("ember veil: it blinks to a different perch every attack (" + string.Join(",", perches) + "), at least three of its four in use", changed && distinct.Count >= 3);
        Check("... never near the pilot (" + nearest.ToString("0.0") + " u at the closest, MinRange " + AshlingBrain.MinRange + ")", nearest > AshlingBrain.MinRange * .9f);
        Check("... the cloud is ringed at the tell (and the perch it will blink to), then left drifting where it stood, short-lived (gone after ~" + def.hazardSeconds + " s)", ringed && cloud && expired);
        Check("... and a fan of shards follows the blink (" + def.shotCount + ")", fanned);

        // closing in on it: it blinks away
        Fresh(.05f);
        e = InPlay("ashling", new Vector2(.4f, 1.5f));
        brain = (AshlingBrain)e.Brain;
        Step(1.5f);
        int flees = brain.Flees;
        // (a real approach, not a jump: a jump loses the elite)
        Step(3f, () => pilot.position = Vector3.MoveTowards(pilot.position, new Vector3(e.Position.x, e.Position.y - 1.2f, 0f), 2f * Dt));
        Check("pressed, it blinks away rather than let the pilot near (" + (brain.Flees - flees) + " blinks)", brain.Flees > flees);
        e.TakeShipAttack(0, 1f, e.Position);
        Check("hurt it hides, then comes back (" + e.Hidden + ")", e.Hidden);
        WaitBack(e);
        Check("... back on the board", Alive(e) && !e.Hidden && e.Hearts == def.hearts - 1);
        EliteSystem.Clear();
    }

    // ---- Kilnback: kiln + kiln_dump --------------------------------------------------------

    static void Kiln()
    {
        Fresh(.05f);
        var e = InPlay("kiln", new Vector2(0f, 3.4f));
        var def = e.Def;
        var dump = (KilnDumpAttack)e.Attack;
        var blobs = new List<int>();
        bool gaps = true;
        float worst = 99f;
        for (int lost = 0; lost < def.hearts; lost++)
        {
            blobs.Add(dump.Blobs);
            for (float x = -2.9f; x <= 2.9f; x += .25f)
                foreach (bool spill in new[] { false, true })
                {
                    dump.Plan(spill, x);
                    float g = EmberLanes.Gap(new List<float>(dump.Lanes), dump.Half, dump.Edge);
                    worst = Mathf.Min(worst, g);
                    gaps &= g >= EmberLanes.MinGap - 1e-3f && dump.Lanes.Count >= 1;
                }
            if (lost < def.hearts - 1) { e.TakeHit(EliteDamage.Teleport, e.Position, 1); Step(.6f); }
        }
        bool more = blobs.Count == def.hearts;
        for (int i = 1; i < blobs.Count; i++) more &= blobs[i] > blobs[i - 1] || blobs[i] == KilnDumpAttack.MaxBlobs;
        Check("kiln dump: more blobs for every heart lost (" + string.Join(",", blobs) + ")", more);
        Check("... a dump and a spill always leave a clear gap of at least " + EmberLanes.MinGap + " u whatever the pilot's x and the hearts (worst " + worst.ToString("0.00") + ")", gaps);

        // the real casts: a dump (lanes drawn, blobs down them), then a spill (a strip drawn, pools)
        Fresh(.05f);
        pilot.position = new Vector3(.6f, -3f, 0f);
        e = InPlay("kiln", new Vector2(0f, 3.4f));
        dump = (KilnDumpAttack)e.Attack;
        Step(1.5f);
        e.ForceAttack();
        bool drawn = false, locked = true;
        int loops = 0;
        var lanes0 = new List<float>();
        while (e.Telling)
        {
            EliteSystem.Step(Dt);
            drawn |= dump.Telegraph.Showing;
            loops = dump.Telegraph.Loops;
            if (e.AimLocked) { if (lanes0.Count == 0) lanes0.AddRange(dump.Lanes); else for (int i = 0; i < lanes0.Count; i++) locked &= Mathf.Approximately(lanes0[i], dump.Lanes[i]); }
        }
        int n = dump.Lanes.Count;
        Step(2f);
        Check("a dump draws a lane under each blob at the tell (" + loops + " outlines for " + n + "), locked, then " + n + " slag blobs sink (" + ActiveShots(e, EliteShots.Kind.Slag) + ")",
              !dump.IsSpill && drawn && loops == n && locked && ActiveShots(e, EliteShots.Kind.Slag) >= 1 && dump.Dumped == n);
        Step(1f);
        e.ForceAttack();
        bool spillDrawn = false;
        while (e.Telling) { EliteSystem.Step(Dt); spillDrawn |= dump.Telegraph.Showing; }
        int strips = dump.Lanes.Count;
        int pools = 0;
        Step(1.2f, () => pools = Mathf.Max(pools, ActiveShots(e, EliteShots.Kind.Glob)));
        Check("then a spill: a lane is drawn at the tell, and molten pools land down it (" + pools + " pools, " + strips + " strip)", dump.IsSpill && spillDrawn && pools >= KilnDumpAttack.SpillLen - 1);
        Step(def.poolSeconds + def.lobSeconds + 1f);
        Check("... which cool and are gone (" + ActiveShots(e, EliteShots.Kind.Glob) + " left)", ActiveShots(e, EliteShots.Kind.Glob) == 0);

        // it holds the top and slides; the pilot's moves do not draw it
        Fresh(.05f);
        e = InPlay("kiln", new Vector2(0f, 3.4f));
        e.AttackCooldown = 99f;
        float top = float.MaxValue, bottom = float.MinValue, minY = 99f, nearestToPilot = 99f;
        int f = 0;
        Step(14f, () =>
        {
            pilot.position = new Vector3(Mathf.Sin(f++ * .02f) * 2.2f, -2.5f, 0f);
            top = Mathf.Min(top, e.Position.x);
            bottom = Mathf.Max(bottom, e.Position.x);
            minY = Mathf.Min(minY, e.Position.y);
            nearestToPilot = Mathf.Min(nearestToPilot, Vector2.Distance(e.Position, pilot.position));
        });
        Check("kiln holds the top (y >= " + minY.ToString("0.0") + "), slides a crawl, never follows (x " + top.ToString("0.0") + " .. " + bottom.ToString("0.0") + ", " + nearestToPilot.ToString("0.0") + " u from the pilot at the closest)",
              minY > EliteSystem.ViewTop - e.Def.topMargin - 1f && nearestToPilot > 4f && bottom - top <= 2f * e.Def.laneOffset + .3f);
        EliteSystem.Clear();
    }

    // ---- Cauterizer: furnace + fire_mortar -------------------------------------------------

    static void Furnace()
    {
        Fresh(.05f);
        var e = InPlay("furnace", new Vector2(0f, 3.4f));
        var def = e.Def;
        var mortar = (FireMortarAttack)e.Attack;
        bool gaps = true;
        float worst = 99f;
        var two = new List<float>(2);
        for (float x = -2.9f; x <= 2.9f; x += .2f)
        {
            float a, b;
            mortar.PlanLanes(x, true, out a, out b);
            two.Clear();
            two.Add(a);
            two.Add(b);
            float g = EmberLanes.Gap(two, def.shotSize * .5f + .1f, mortar.Edge);
            worst = Mathf.Min(worst, g);
            gaps &= g >= EmberLanes.MinGap && a >= -mortar.Edge && b <= mortar.Edge && b - a > EmberLanes.MinGap;
        }
        Check("the double lane always leaves a safe gap of at least " + EmberLanes.MinGap + " u, between its two lanes and beyond them (worst " + worst.ToString("0.00") + ")", gaps);

        // full hearts: a shell down one lane (the pilot in it), then bombs
        Fresh(.05f);
        e = InPlay("furnace", new Vector2(0f, 3.4f));
        mortar = (FireMortarAttack)e.Attack;
        Step(2f);
        pilot.position = new Vector3(e.MuzzleWorld(0).x, -3f, 0f);
        Check("a shell is due when the pilot is in its lane", mortar.ShellDue(pilot.position.x));
        e.ForceAttack();
        bool sight = false, sight2 = false;
        float tellLen = 0f;
        while (e.Telling) { EliteSystem.Step(Dt); sight |= e.SightShown; sight2 |= mortar.Sight2.Shown; tellLen += Dt; pilot.position = new Vector3(e.MuzzleWorld(0).x, -3f, 0f); }
        Step(.3f);
        Check("shell: a long wind-up (" + tellLen.ToString("0.00") + " s) with a thin sight line down its lane, then one piercing shell",
              !mortar.Bombs && !mortar.Doubled && sight && !sight2 && tellLen >= e.Def.tellSeconds - .1f && mortar.ShellFired && EliteSystem.Shots.Launched >= 1);
        Step(1.5f);
        e.ForceAttack();
        bool rings = false;
        int spots = 0;
        while (e.Telling) { EliteSystem.Step(Dt); rings |= mortar.Marks.Shown(0) && mortar.Marks.Shown(1); spots = mortar.Spots; }
        int orbs = 0;
        Step(1.4f, () => orbs = Mathf.Max(orbs, ActiveShots(e, EliteShots.Kind.Orb)));
        Check("then bombs: " + spots + " ringed spots at the tell, " + orbs + " firebombs lobbed onto them", mortar.Bombs && rings && spots >= 2 && orbs >= 1);
        float lx = e.MuzzleWorld(0).x;
        Check("... and a shell is due again, but only with the pilot in its lane (in it " + mortar.ShellDue(lx) + ", 3 u off " + mortar.ShellDue(lx + 3f) + ")", mortar.ShellDue(lx) && !mortar.ShellDue(lx + 3f));

        // two lanes at <= 2 hearts
        Fresh(.05f);
        e = InPlay("furnace", new Vector2(0f, 3.4f));
        mortar = (FireMortarAttack)e.Attack;
        for (int i = 0; i < def.hearts - FireMortarAttack.TwoLanesAt; i++) { e.TakeHit(EliteDamage.Teleport, e.Position, 1); Step(.7f); }
        Step(1f);
        pilot.position = new Vector3(e.MuzzleWorld(0).x, -3f, 0f);
        e.ForceAttack();
        bool both = false;
        while (e.Telling) { EliteSystem.Step(Dt); both |= e.SightShown && mortar.Sight2.Shown; pilot.position = new Vector3(e.MuzzleWorld(0).x, -3f, 0f); }
        int shells = 0;
        Step(.3f, () => shells = Mathf.Max(shells, ActiveShots(e, EliteShots.Kind.Shell)));
        Check("at " + e.Hearts + " hearts it fires two lanes (" + mortar.LaneA.ToString("0.0") + ", " + mortar.LaneB.ToString("0.0") + "), both drawn with a sight line, two shells (" + shells + ")",
              !mortar.Bombs && mortar.Doubled && both && shells >= 2 && mortar.LaneB - mortar.LaneA > EmberLanes.MinGap);
        EliteSystem.Clear();
    }

    // ---- allocations -------------------------------------------------------------------

    static void Allocations()
    {
        Fresh(.1f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        var all = new[] { InPlay("stoker", new Vector2(-1.2f, 3f)), InPlay("strafer", new Vector2(1.2f, 1.6f)), InPlay("scavenger", new Vector2(0f, 3f)),
                          InPlay("ashling", new Vector2(.5f, 3f)), InPlay("kiln", new Vector2(-.5f, 2f)), InPlay("furnace", new Vector2(.8f, 2.5f)) };
        foreach (var x in all) x.AttackCooldown = 0f;
        int f = 0;
        Step(24f, () => pilot.position = new Vector3(Mathf.Sin(f++ * .03f), -2.5f, 0f));   // warm up: every attack
        foreach (var x in all) if (Alive(x)) x.AttackCooldown = 99f;                        // (a cast builds its telegraph: a spawn, not a frame)
        Step(6f);
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
        Check("zero per-frame allocations stepping the six Ember elites between attacks (" + allocated + " bytes over 90 frames; meter control " + control + ")", meter && allocated == 0);
    }

    // ---- BringHome: the backdrop brings the dock of an elite into view ------------------

    static void BringHome()
    {
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        Time.timeScale = 1f;
        moveBackGround.speed = .25f;
        var sel = BackdropVariants.For("Ember");
        sel.Reset();
        sel.Seed(11);
        sel.Force = 1;
        var cam = Camera.main;
        cam.orthographic = true;
        cam.aspect = 1080f / 2400f;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, 1080, 2400);
        var wb = WorldBackdrop.Create("Ember");
        wb.Show("Ember", false);
        var d = wb.Current != null ? wb.Current.Director as EmberDirector : null;
        Check("Ember's backdrop is up for the dock check", d != null);
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
        Check("... a launchFrom of no Ember site (a Space station) or none is not conjured", !d.BringHome("station") && !d.BringHome(""));
        Check("... LandingSites.BringHome reaches it through the world backdrop", LandingSites.BringHome("furnacebay,foundryhangar"));
        EliteSystem.Clear();
    }
}
