using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Ember's six elites as mini-boss fights (EmberElites.cs): the roster (hearts, hiding, a dock each, never pink, the base brains and attacks
// still Storm's and Desert's), their strips on the hull, the director launching them from Ember's own kinds of site (and BringHome asking
// the backdrop for one), the whole life cycle off a pad, and each one's scripted fight in simulation (the def json "script": a fixed loop
// that is the same under every random seed, a different loop in phase 2, the last heart's one exit) -- Sunstoke's rail-to-rail lance passes
// (a thin sight line locked, rammed across and braked at the far rail inside the view, needles back along the lane), the Coalrunner's
// stop-and-fire strafe along the top third (bolt lanes drawn and locked, coal clumps from phase 2), the Vulture's high circle and ringed
// dive (a ring slot cycle), the Wraith's scripted perches and ember clouds, the Kilnback's planted dump and spill and the Cauterizer's
// tracking shell, double lane and firebombs -- none of them ever leaving the view, tell timings, clean-up on death and zero per-frame
// allocation.
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

    // ---- script helpers --------------------------------------------------------------------

    static string Seq<T>(List<T> l) { return string.Join(",", l); }

    // The elite's centre is inside the view (between the rails, under the top, over the bottom).
    static bool InView(EliteShip e)
    {
        Vector2 p = e.Position;
        return !e.Hidden && !e.Offstage && Mathf.Abs(p.x) <= EliteSystem.RailEdge && p.y <= EliteSystem.ViewTop && p.y >= EliteSystem.ViewBottom;
    }

    static bool Alternating(List<float> l)
    {
        bool ok = l.Count >= 3;
        for (int i = 1; i < l.Count; i++) ok &= l[i] == -l[i - 1];
        return ok;
    }

    // A ship whose script is the given attacks (variant names, "" plain) round and round, with the catalog's own variants; the casts of
    // the real script, one at a time, without its movement beats or its phases.
    static EliteShip CastsInPlay(string brain, Vector2 at, params string[] attacks)
    {
        var src = Def(brain);
        var d = src.Instance();
        var loop = new EliteStep[attacks.Length];
        for (int i = 0; i < loop.Length; i++) loop[i] = new EliteStep { wait = 1f, attack = attacks[i] };
        d.script = new EliteScriptDef { variants = src.script.variants, phase1 = new ElitePhaseDef { loop = loop } };
        var e = EliteShip.CreateInPlay(d, at);
        e.AttackCooldown = 99f;
        return e;
    }

    // The next cast begins now (its wind-up has started); false when it never does.
    static bool NextTell(EliteShip e, float max = 15f)
    {
        e.AttackCooldown = 0f;
        for (float t = 0f; t < max && Alive(e); t += Dt)
        {
            if (e.Telling) return true;
            EliteSystem.Step(Dt);
        }
        return e.Telling;
    }

    // A hit that is a flinch (a phase change), taken between attacks.
    static void HitBetweenAttacks(EliteShip e)
    {
        e.AttackCooldown = 99f;
        Step(3f);
        e.TakeShipAttack(0, 1f, e.Position);
    }

    // ---- Sunstoke: stoker + lance_pass ---------------------------------------------------

    sealed class StokerLog
    {
        public List<int> lanes = new List<int>();
        public List<float> sides = new List<float>();
        public List<float> ends = new List<float>();
        public bool inView = true, sight, locked = true, noHeart = true, needles, swift, rockGone;
        public int ploughed;
        public float drift, nearest = 99f;
    }

    // A Sunstoke fight (from `hearts` lost already: 0 or 1) until it has started `passes` passes.
    static StokerLog StokerRun(int seed, int passes, int lost, bool rock)
    {
        Fresh(.05f);
        Random.InitState(seed);
        var log = new StokerLog();
        var e = InPlay("stoker", new Vector2(1.2f, 1.5f));
        var def = e.Def;
        var brain = (StokerBrain)e.Brain;
        var lance = (LancePassAttack)e.Attack;
        e.EngageLeft = 9999f;
        if (lost > 0) HitBetweenAttacks(e);
        e.AttackCooldown = 0f;
        int started = 0;
        bool inAct = false;
        Vector2 tellPos = Vector2.zero, lockDir = Vector2.zero;
        Rock0 = null;
        for (int i = 0; i < 30 * 90 && log.lanes.Count < passes; i++)
        {
            pilot.position = new Vector3(Mathf.Sin(i * .03f) * 1.4f, -2.5f, 0f);
            EliteSystem.Step(Dt);
            if (!Alive(e)) break;
            log.inView &= InView(e);
            if (brain.Phase != StokerBrain.Stage.Run) log.nearest = Mathf.Min(log.nearest, Vector2.Distance(e.Position, pilot.position));
            if (e.Telling)
            {
                if (tellPos == Vector2.zero) tellPos = e.Position;
                log.drift = Mathf.Max(log.drift, Vector2.Distance(e.Position, tellPos));
                log.sight |= e.SightShown;
                if (e.AimLocked) { if (lockDir == Vector2.zero) lockDir = lance.Dir; else log.locked &= Vector2.Distance(lockDir, lance.Dir) < .001f; }
                log.swift |= e.Script.VariantName == "swift";
            }
            else tellPos = Vector2.zero;
            if (brain.PassesStarted > started)
            {
                started = brain.PassesStarted;
                log.lanes.Add(brain.Lane);
                log.sides.Add(brain.Side);
                log.locked &= lockDir != Vector2.zero && Vector2.Distance(lockDir, lance.Dir) < .001f;
                lockDir = Vector2.zero;
                if (rock && log.lanes.Count == 1) Rock0 = Rock(e.Position + lance.Dir * 2.5f);
            }
            if (e.Acting) { inAct = true; log.noHeart &= e.Hearts == def.hearts - lost; }
            else if (inAct)
            {
                inAct = false;
                log.ends.Add(e.Position.x);
                log.needles |= lance.NeedlesFlung;
            }
        }
        log.rockGone = Rock0 == null;
        log.ploughed = e.Ploughed;
        EliteSystem.Clear();
        return log;
    }

    static void Stoker()
    {
        var a = StokerRun(1357, 6, 0, true);
        var b = StokerRun(99, 6, 0, false);
        var def = Def("stoker");
        Check("lance pass: the fixed phase-1 loop, lane 0, 1, 2, 0, 1, 2 (" + Seq(a.lanes) + ")", Seq(a.lanes) == "0,1,2,0,1,2");
        Check("... rail to rail, every pass from the rail the last one ended at (" + Seq(a.sides) + ")", Alternating(a.sides));
        Check("... the same fight under another random seed (" + Seq(b.lanes) + " / " + Seq(b.sides) + ")", Seq(a.lanes) == Seq(b.lanes) && Seq(a.sides) == Seq(b.sides));
        Check("... it never leaves the view, not for a pass and not for the turn", a.inView && b.inView);
        Check("... a thin sight line while it waits at its rail, holding still on it (drift " + a.drift.ToString("0.00") + ")", a.sight && a.drift < .5f);
        Check("... the lane is locked at the tell (it does not change through the ram), and outside a pass it keeps its distance (" + a.nearest.ToString("0.0") + " u at the closest)", a.locked && a.nearest > 1.5f);
        bool atRails = a.ends.Count >= 4;
        foreach (float x in a.ends) atRails &= Mathf.Abs(x) > EliteSystem.RailEdge - def.hullRadius - 1.4f && Mathf.Abs(x) < EliteSystem.RailEdge - def.hullRadius * .7f;
        Check("... every pass brakes at the far rail inside the view (end x " + Seq(a.ends.ConvertAll(v => Mathf.Round(v * 10f) / 10f)) + ")", atRails);
        Check("... it ploughs the rock in its lane without losing a heart", a.rockGone && a.ploughed >= 1 && a.noHeart);
        Check("... heat needles flung back along the lane as it arrives", a.needles);

        // hurt: phase 2 (pairs, the swift variant), then the last heart and its one scripted exit
        var b2 = StokerRun(1357, 4, 1, false);
        Fresh(.05f);
        var e = InPlay("stoker", new Vector2(0f, 2f));
        e.EngageLeft = 9999f;
        int hides = EliteShip.Hides;
        HitBetweenAttacks(e);
        Check("stoker: a hit is a flinch, no hiding, and the script moves to phase 2 (x" + e.Script.SpeedScale.ToString("0.00") + ")",
              e.InPlay && !e.Hidden && e.Flinching && e.Hearts == def.hearts - 1 && e.Script.Phase == 2 && e.Script.SpeedScale > 1.1f && EliteShip.Hides == hides);
        Check("... phase 2 is a different loop: lanes 1, 2, 0, 1 with a swift second pass (" + Seq(b2.lanes) + ")", Seq(b2.lanes) == "1,2,0,1" && b2.swift && b2.inView);
        e.AttackCooldown = 99f;
        Step(EliteShip.GraceSeconds + .2f);
        e.TakeHit(EliteDamage.Teleport, e.Position, 1);
        Step(.3f);
        Check("... the last heart: desperation phase (" + e.Script.Phase + ") and the one scripted exit (" + e.Script.LastExit + ")", e.Script.Phase == 3 && e.Hidden && e.LastExitDone && EliteShip.Hides == hides + 1);
        WaitBack(e);
        Check("... back on the board from the other side, hearts " + e.Hearts, Alive(e) && !e.Hidden && e.Hearts == 1);
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

    sealed class StraferLog
    {
        public List<float> sides = new List<float>();
        public List<int> bands = new List<int>();
        public bool inView = true, stood = true, atRails = true, inBand = true, lanes, bolts, noClump = true, clump, drawn, bounced;
        public float maxSpeed, minY = 99f, maxY = -99f;
        public int most;
    }

    static StraferLog StraferRun(int seed, int volleys, bool hurt, float seconds = 70f)
    {
        Fresh(.05f);
        Random.InitState(seed);
        var log = new StraferLog();
        var e = InPlay("strafer", new Vector2(0f, 2f));
        var def = e.Def;
        var brain = (StraferBrain)e.Brain;
        var strafe = (CoalStrafeAttack)e.Attack;
        e.EngageLeft = 9999f;
        if (hurt) HitBetweenAttacks(e);
        e.AttackCooldown = 0f;
        float lo, hi;
        StraferBrain.Band(def, out lo, out hi);
        int seen = e.Attacks;
        for (int i = 0; i < seconds * 30 && log.sides.Count < volleys + (hurt ? 99 : 0); i++)
        {
            pilot.position = new Vector3(Mathf.Sin(i * .03f) * 1.4f, -2.5f, 0f);
            EliteSystem.Step(Dt);
            if (!Alive(e)) break;
            log.inView &= InView(e);
            float speed = e.Velocity.magnitude;
            if (!e.Telling && !e.Acting) log.maxSpeed = Mathf.Max(log.maxSpeed, speed);
            else log.stood &= speed < 1.2f;
            if (e.Attacks > seen && e.Telling)
            {
                seen = e.Attacks;
                log.sides.Add(brain.Side);
                log.bands.Add(brain.BandIndex);
                log.atRails &= Mathf.Abs(e.Position.x) > EliteSystem.RailEdge - def.hullRadius - 1.3f;
                log.inBand &= e.Position.y >= lo - 1.2f && e.Position.y <= EliteSystem.ViewTop;
                log.minY = Mathf.Min(log.minY, e.Position.y); log.maxY = Mathf.Max(log.maxY, e.Position.y);
                if (strafe.Telegraph.Showing) log.lanes |= strafe.Telegraph.Loops >= 2;
                if (!hurt) log.noClump &= strafe.Clumps == 0;
            }
            if (e.Telling && strafe.Clumps >= 1) log.drawn |= strafe.Telegraph.Showing;
            if (e.Acting) log.bolts |= ActiveShots(e, EliteShots.Kind.Bolt) > 0;
            foreach (var s in EliteSystem.Shots.All)
                if (s.Active && s.Owner == e && s.Kind == EliteShots.Kind.Slag && s.Skidding) { log.clump = true; log.bounced |= s.Bounced >= 1; }
            if (hurt && log.bounced && log.drawn) break;
        }
        EliteSystem.Clear();
        return log;
    }

    static void Strafer()
    {
        var a = StraferRun(1357, 4, false);
        var b = StraferRun(99, 4, false);
        var def = Def("strafer");
        Check("coal strafe: it stops at the rails and fires there, alternating right, left, right, left (" + Seq(a.sides) + ")", a.atRails && a.sides.Count >= 4 && Seq(a.sides.GetRange(0, 4)) == "1,-1,1,-1");
        Check("... the height steps down the bands from the counter, never a coin (" + Seq(a.bands) + "), the same under another seed (" + Seq(b.bands) + ")",
              a.bands.Count >= 4 && Seq(a.bands.GetRange(0, 4)) == "0,1,2,0" && Seq(a.bands) == Seq(b.bands) && Seq(a.sides) == Seq(b.sides));
        Check("... it never leaves the view and keeps to the top third (y " + a.minY.ToString("0.0") + " .. " + a.maxY.ToString("0.0") + ")", a.inView && b.inView && a.inBand);
        Check("... it races between the rails (" + a.maxSpeed.ToString("0.0") + " u/s against a base " + def.speed + ") and stands still to fire", a.maxSpeed > def.speed * 1.05f && a.stood);
        Check("... the bolt lanes are drawn at the tell and the bolts leave down them, no coal in phase 1", a.lanes && a.bolts && a.noClump);

        // phase 2: coal clumps on the stop, skidding down the board and glancing off a rail
        var h = StraferRun(1357, 99, true, 60f);
        Check("coal: from phase 2 clumps skid down the board (path drawn at the tell) and glance off a rail", h.clump && h.drawn && h.bounced);
    }

    // ---- Brass Vulture: scavenger + scrap_dive -------------------------------------------

    sealed class VultureLog
    {
        public List<int> slots = new List<int>();
        public bool inView = true, ring, ringLocked = true, through, flung, fast;
        public float circleLo = 99f, ringY = 99f, minToSpot = 99f;
        public HashSet<float> radii = new HashSet<float>();
        public int done;
    }

    static VultureLog VultureRun(int seed, int dives, bool hurt)
    {
        Fresh(.05f);
        Random.InitState(seed);
        var log = new VultureLog();
        var e = InPlay("scavenger", new Vector2(0f, 2f));
        var brain = (ScavengerBrain)e.Brain;
        var dive = (ScrapDiveAttack)e.Attack;
        e.EngageLeft = 9999f;
        if (hurt) HitBetweenAttacks(e);
        e.AttackCooldown = 0f;
        Vector2 lockedSpot = Vector2.zero;
        float toSpot = 99f;
        int seen = e.Attacks;
        bool inDive = false;
        for (int i = 0; i < 30 * 80 && log.done < dives; i++)
        {
            pilot.position = new Vector3(Mathf.Sin(i * .03f) * 1.2f, -2.5f, 0f);
            EliteSystem.Step(Dt);
            if (!Alive(e)) break;
            log.inView &= InView(e);
            if (brain.Phase == ScavengerBrain.Stage.Circle) { log.circleLo = Mathf.Min(log.circleLo, e.Position.y); log.radii.Add(brain.RadiusX); }
            if (e.Telling)
            {
                if (e.Attacks > seen) { seen = e.Attacks; log.slots.Add(dive.Slot); lockedSpot = Vector2.zero; }
                log.ring |= dive.Marks.Shown(0);
                log.fast |= e.Script.VariantName == "fast";
                if (e.AimLocked) { if (lockedSpot == Vector2.zero) lockedSpot = dive.Spot; else log.ringLocked &= Vector2.Distance(lockedSpot, dive.Spot) < .001f; }
                log.ringY = Mathf.Min(log.ringY, dive.Spot.y);
            }
            if (e.Acting)
            {
                if (!inDive) toSpot = 99f;
                inDive = true;
                toSpot = Mathf.Min(toSpot, Vector2.Distance(e.Position, dive.Spot));
                log.flung |= dive.Flung;
            }
            else if (inDive)
            {
                inDive = false;
                log.done++;
                log.through |= toSpot < .6f;
            }
        }
        EliteSystem.Clear();
        return log;
    }

    static void Scavenger()
    {
        var a = VultureRun(1357, 6, false);
        var b = VultureRun(99, 6, false);
        Check("scrap dive: the ring cycles on the pilot, a hand to one side, a hand to the other (" + Seq(a.slots) + "), the same under another seed (" + Seq(b.slots) + ")",
              a.slots.Count >= 6 && Seq(a.slots.GetRange(0, 6)) == "0,1,2,0,1,2" && Seq(a.slots) == Seq(b.slots));
        Check("... it circles high inside the view, never leaving it (lowest circle point " + a.circleLo.ToString("0.0") + ", rings down at " + a.ringY.ToString("0.0") + ")", a.inView && b.inView && a.circleLo > a.ringY + 1f);
        Check("... the script widens and narrows the circle (" + a.radii.Count + " widths)", a.radii.Count >= 2);
        Check("... a thin ring marks the spot at the tell, locked by the commit; it dives straight through it (" + a.minToSpot.ToString("0.00") + " u) flinging scrap", a.ring && a.ringLocked && a.through && a.flung);
        Check("... and climbs back to its circle (" + a.done + " dives)", a.done >= 6);

        // hurt: the second dive of each pair is a fast one
        var h = VultureRun(1357, 4, true);
        Check("phase 2: pairs of dives, the second a fast variant with a bigger fan (" + h.done + " dives)", h.fast && h.done >= 4 && h.inView);
    }

    // ---- Ash Wraith: ashling + ember_veil -------------------------------------------------

    static void Ashling()
    {
        List<int> run1 = null, run2 = null;
        bool ringed = false, cloud = false, expired = false, fanned = false, view = true;
        float nearest = 99f;
        EliteDef def = null;
        for (int pass = 0; pass < 2; pass++)
        {
            Fresh(.05f);
            Random.InitState(pass == 0 ? 1357 : 99);
            var e = InPlay("ashling", new Vector2(1f, 2f));
            def = e.Def;
            var brain = (AshlingBrain)e.Brain;
            var veil = (EmberVeilAttack)e.Attack;
            e.EngageLeft = 9999f;
            e.AttackCooldown = 0f;
            var perches = new List<int>();
            int attacksSeen = 0;
            EliteShot cloudShot = null;
            float cloudAge = 0f;
            for (int i = 0; i < 30 * 70 && perches.Count < 7; i++)
            {
                EliteSystem.Step(Dt);
                if (!Alive(e)) break;
                view &= InView(e);
                nearest = Mathf.Min(nearest, Vector2.Distance(e.Position, pilot.position));
                if (pass == 0)
                {
                    if (e.Telling) ringed |= veil.Marks.Shown(0) && Vector2.Distance(veil.Marks.At(0), veil.CloudAt) < .01f && veil.Marks.Shown(1);
                    if (e.Attacks > attacksSeen && e.Acting)
                    {
                        attacksSeen = e.Attacks;
                        foreach (var s in EliteSystem.Shots.All)
                            if (s.Active && s.Owner == e && s.Kind == EliteShots.Kind.Orb && cloudShot == null) { cloudShot = s; cloud = true; cloudAge = 0f; }
                        fanned |= ActiveShots(e, EliteShots.Kind.Shard) >= def.shotCount;
                    }
                    if (cloudShot != null) { cloudAge += Dt; if (!cloudShot.Active) { expired = cloudAge < def.hazardSeconds + 1f; cloudShot = null; } }
                }
                if (e.Attacks > perches.Count && !e.Telling && !e.Acting) perches.Add(brain.Perch);
            }
            if (pass == 0) run1 = perches; else run2 = perches;
            EliteSystem.Clear();
        }
        // (the first hop may be the fallback: it starts on the perch the script names first)
        bool order = run1.Count >= 6;
        int[] pattern = { 1, 0, 1, 2 };
        for (int i = 1; i < run1.Count; i++) order &= run1[i] == pattern[(i - 1) % 4];
        Check("ember veil: the perches follow the script, 1, 0, 1, 2 round and round (" + Seq(run1) + ")", order);
        Check("... the same under another random seed (" + Seq(run2) + ")", Seq(run1) == Seq(run2));
        Check("... never near the pilot (" + nearest.ToString("0.0") + " u at the closest, MinRange " + AshlingBrain.MinRange + ") and never out of the view", nearest > AshlingBrain.MinRange * .9f && view);
        Check("... the cloud is ringed at the tell (and the perch it will blink to), then left drifting where it stood, short-lived (gone after ~" + def.hazardSeconds + " s)", ringed && cloud && expired);
        Check("... and a fan of shards follows the blink (" + def.shotCount + ")", fanned);

        // closing in on it: it blinks away
        Fresh(.05f);
        var w = InPlay("ashling", new Vector2(.4f, 1.5f));
        var wb = (AshlingBrain)w.Brain;
        Step(1.5f);
        int flees = wb.Flees;
        // (a real approach, not a jump: a jump loses the elite)
        Step(3f, () => pilot.position = Vector3.MoveTowards(pilot.position, new Vector3(w.Position.x, w.Position.y - 1.2f, 0f), 2f * Dt));
        Check("pressed, it blinks away rather than let the pilot near (" + (wb.Flees - flees) + " blinks)", wb.Flees > flees);
        EliteSystem.Clear();

        // phase 2: a second cloud where it lands; then the last heart with its one scripted exit
        Fresh(.05f);
        var e2 = InPlay("ashling", new Vector2(1f, 2f));
        var veil2 = (EmberVeilAttack)e2.Attack;
        e2.EngageLeft = 9999f;
        int hides = EliteShip.Hides;
        Check("a cast leaves one cloud at full hearts (" + veil2.CloudsPerCast + ")", veil2.CloudsPerCast == 1);
        HitBetweenAttacks(e2);
        Check("hurt it flinches on the board, no hiding, and the script moves to phase 2: two clouds a cast (" + veil2.CloudsPerCast + ")",
              !e2.Hidden && e2.Flinching && e2.Script.Phase == 2 && veil2.CloudsPerCast == 2 && EliteShip.Hides == hides);
        e2.AttackCooldown = 0f;
        int clouds0 = veil2.Clouds, attacks0 = e2.Attacks;
        for (int i = 0; i < 30 * 30 && e2.Attacks < attacks0 + 2; i++) { EliteSystem.Step(Dt); if (!Alive(e2)) break; }
        Step(1.5f);
        Check("... and the casts leave two clouds each (" + (veil2.Clouds - clouds0) + " in " + (e2.Attacks - attacks0) + " casts)", e2.Attacks > attacks0 && veil2.Clouds - clouds0 >= 2 * (e2.Attacks - attacks0) - 1);
        e2.AttackCooldown = 99f;
        Step(EliteShip.GraceSeconds + .3f);
        e2.TakeHit(EliteDamage.Teleport, e2.Position, 1);
        Step(.3f);
        Check("... the last heart: the one scripted exit (" + e2.Script.LastExit + ")", e2.Script.Phase == 3 && e2.Hidden && EliteShip.Hides == hides + 1);
        WaitBack(e2);
        Check("... back on the board, one heart left", Alive(e2) && !e2.Hidden && e2.Hearts == 1);
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
        // the script's numbers: 3 blobs in phase 1, 5 in phase 2, 7 at the end; one strip or two
        foreach (int count in new[] { 3, 5, 7 })
            foreach (int strips in new[] { 1, 2 })
            {
                e.Def.shotCount = count;
                e.Def.hazardCount = strips;
                blobs.Add(dump.Blobs);
                for (float x = -2.9f; x <= 2.9f; x += .25f)
                    foreach (bool spill in new[] { false, true })
                    {
                        dump.Plan(spill, x);
                        float g = EmberLanes.Gap(new List<float>(dump.Lanes), dump.Half, dump.Edge);
                        worst = Mathf.Min(worst, g);
                        gaps &= g >= EmberLanes.MinGap - 1e-3f && dump.Lanes.Count >= 1;
                    }
            }
        Check("kiln dump: shotCount blobs (" + string.Join(",", blobs) + ")", string.Join(",", blobs) == "3,3,5,5,7,7");
        Check("... a dump and a spill always leave a clear gap of at least " + EmberLanes.MinGap + " u whatever the pilot's x and the script's numbers (worst " + worst.ToString("0.00") + ")", gaps);

        // the real casts: a dump (lanes drawn, blobs down them), then a spill (a strip drawn, pools)
        Fresh(.05f);
        pilot.position = new Vector3(.6f, -3f, 0f);
        e = CastsInPlay("kiln", new Vector2(0f, 3.4f), "", "lava");
        dump = (KilnDumpAttack)e.Attack;
        Step(1.5f);
        NextTell(e);
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
        NextTell(e);      // (the script's `lava` beat)
        bool spillDrawn = false;
        while (e.Telling) { EliteSystem.Step(Dt); spillDrawn |= dump.Telegraph.Showing; }
        int strip = dump.Lanes.Count;
        int pools = 0;
        Step(1.2f, () => pools = Mathf.Max(pools, ActiveShots(e, EliteShots.Kind.Glob)));
        Check("then a spill: a lane is drawn at the tell, and molten pools land down it (" + pools + " pools, " + strip + " strip)", dump.IsSpill && spillDrawn && pools >= KilnDumpAttack.SpillLen - 1);
        Step(def.poolSeconds + def.lobSeconds + 1f);
        Check("... which cool and are gone (" + ActiveShots(e, EliteShots.Kind.Glob) + " left)", ActiveShots(e, EliteShots.Kind.Glob) == 0);

        // the script: planted at the centre in phase 1 (slag, lava, slag, lava), off-centre in phase 2, never a pendulum
        var kinds = new List<string>();
        float minX = 99f, maxX = -99f, nearestToPilot = 99f;
        var runs = new List<string>();
        for (int seed = 1; seed <= 2; seed++)
        {
            Fresh(.05f);
            Random.InitState(seed * 77);
            e = InPlay("kiln", new Vector2(0f, 3.4f));
            dump = (KilnDumpAttack)e.Attack;
            e.EngageLeft = 9999f;
            e.AttackCooldown = 0f;
            kinds.Clear();
            int seen = e.Attacks;
            for (int i = 0; i < 30 * 45 && kinds.Count < 4; i++)
            {
                EliteSystem.Step(Dt);
                if (!Alive(e)) break;
                if (e.Attacks > seen && e.Telling) { seen = e.Attacks; kinds.Add(dump.IsSpill ? "lava" : "slag"); }
                if (seed == 1)
                {
                    minX = Mathf.Min(minX, e.Position.x); maxX = Mathf.Max(maxX, e.Position.x);
                    nearestToPilot = Mathf.Min(nearestToPilot, Vector2.Distance(e.Position, pilot.position));
                }
            }
            runs.Add(Seq(kinds));
            EliteSystem.Clear();
        }
        Check("kiln: the phase-1 loop is slag, lava, slag, lava (" + runs[0] + "), the same under another seed", runs[0] == "slag,lava,slag,lava" && runs[0] == runs[1]);
        Check("... planted at the top centre, never a pendulum (x " + minX.ToString("0.00") + " .. " + maxX.ToString("0.00") + "), " + nearestToPilot.ToString("0.0") + " u from the pilot at the closest",
              maxX - minX < .8f && nearestToPilot > 4f);

        // phase 2: it takes the left and right stations, and the casts get bigger
        Fresh(.05f);
        e = InPlay("kiln", new Vector2(0f, 3.4f));
        dump = (KilnDumpAttack)e.Attack;
        e.EngageLeft = 9999f;
        e.AttackCooldown = 99f;
        for (int i = 0; i < 2; i++) { Step(EliteShip.GraceSeconds + .2f); e.TakeHit(EliteDamage.Teleport, e.Position, 1); }
        Step(.3f);
        Check("kiln: two hearts lost, the script is in phase 2 (" + e.Script.Phase + ", " + e.Hearts + " hearts), no hiding", e.Script.Phase == 2 && !e.Hidden && Alive(e));
        e.AttackCooldown = 0f;
        float lowX = 99f, highX = -99f;
        int maxBlobs = 0, seen2 = e.Attacks;
        for (int i = 0; i < 30 * 50; i++)
        {
            EliteSystem.Step(Dt);
            if (!Alive(e)) break;
            lowX = Mathf.Min(lowX, e.Position.x); highX = Mathf.Max(highX, e.Position.x);
            if (e.Telling && !dump.IsSpill) maxBlobs = Mathf.Max(maxBlobs, dump.Blobs);
            if (e.Attacks - seen2 >= 5 && !e.Telling && !e.Acting) break;
        }
        Check("... it heaves to the left and the right station (x " + lowX.ToString("0.0") + " .. " + highX.ToString("0.0") + ") and dumps " + maxBlobs + " blobs", lowX < -.8f && highX > .8f && maxBlobs >= 5);
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

        // the script's four casts, forced one by one (hazardArmour is the cast: the script's variants set it)
        Fresh(.05f);
        e = CastsInPlay("furnace", new Vector2(0f, 3.4f), "", "bombs", "double", "both");
        mortar = (FireMortarAttack)e.Attack;
        Step(2f);
        pilot.position = new Vector3(e.MuzzleWorld(0).x, -3f, 0f);
        NextTell(e);
        bool sight = false, sight2 = false;
        float tellLen = 0f;
        while (e.Telling) { EliteSystem.Step(Dt); sight |= e.SightShown; sight2 |= mortar.Sight2.Shown; tellLen += Dt; pilot.position = new Vector3(e.MuzzleWorld(0).x, -3f, 0f); }
        Step(.3f);
        Check("shell: a long wind-up (" + tellLen.ToString("0.00") + " s) with a thin sight line down its lane, then one piercing shell",
              mortar.Shell && !mortar.Bombs && !mortar.Doubled && sight && !sight2 && tellLen >= e.Def.tellSeconds - .1f && mortar.ShellFired && EliteSystem.Shots.Launched >= 1);
        Step(1.5f);
        NextTell(e);
        bool rings = false;
        int spots = 0;
        while (e.Telling) { EliteSystem.Step(Dt); rings |= mortar.Marks.Shown(0) && mortar.Marks.Shown(1); spots = mortar.Spots; }
        int orbs = 0;
        Step(1.4f, () => orbs = Mathf.Max(orbs, ActiveShots(e, EliteShots.Kind.Orb)));
        Check("then bombs: " + spots + " ringed spots at the tell, " + orbs + " firebombs lobbed onto them", mortar.Bombs && !mortar.Shell && rings && spots >= 2 && orbs >= 1);
        Step(2f);
        pilot.position = new Vector3(e.MuzzleWorld(0).x, -3f, 0f);
        NextTell(e);
        bool both = false;
        while (e.Telling) { EliteSystem.Step(Dt); both |= e.SightShown && mortar.Sight2.Shown; pilot.position = new Vector3(e.MuzzleWorld(0).x, -3f, 0f); }
        int shells = 0;
        Step(.3f, () => shells = Mathf.Max(shells, ActiveShots(e, EliteShots.Kind.Shell)));
        Check("a double shell: two lanes (" + mortar.LaneA.ToString("0.0") + ", " + mortar.LaneB.ToString("0.0") + "), both drawn with a sight line, two shells (" + shells + ")",
              mortar.Doubled && both && shells >= 2 && mortar.LaneB - mortar.LaneA > EmberLanes.MinGap);
        Step(2f);
        NextTell(e);
        bool lane = false, ringsToo = false;
        while (e.Telling) { EliteSystem.Step(Dt); lane |= e.SightShown; ringsToo |= mortar.Marks.Shown(0); }
        Step(.3f);
        Check("the last heart's cast: a shell AND firebombs at once (" + mortar.Spots + " spots)", mortar.Shell && mortar.Bombs && lane && ringsToo && mortar.ShellFired && mortar.Spots == 3);
        EliteSystem.Clear();

        // it tracks the pilot's column at a crawl, and holds where it is when the script says so
        Fresh(.05f);
        e = InPlay("furnace", new Vector2(0f, 3.4f));
        var brain = (FurnaceBrain)e.Brain;
        e.AttackCooldown = 99f;
        pilot.position = new Vector3(1.5f, -2.5f, 0f);
        Step(9f);
        float right = e.Position.x;
        pilot.position = new Vector3(-1.5f, -2.5f, 0f);
        Step(9f);
        float left = e.Position.x;
        brain.OnScriptMove("hold");
        float held = e.Position.x;
        pilot.position = new Vector3(1.5f, -2.5f, 0f);
        Step(5f);
        Check("furnace: parks high and tracks the pilot's column (x " + right.ToString("0.0") + " under a pilot at 1.5, " + left.ToString("0.0") + " under one at -1.5), then holds on `hold` (" + e.Position.x.ToString("0.0") + " from " + held.ToString("0.0") + ")",
              Mathf.Abs(right - 1.5f) < .6f && Mathf.Abs(left + 1.5f) < .6f && Mathf.Abs(e.Position.x - held) < .3f && e.Position.y > EliteSystem.ViewTop - e.Def.topMargin - 1.2f);
        EliteSystem.Clear();

        // the script's loop: shell, bombs, shell, bombs -- the same whatever the random seed
        var runs = new List<string>();
        for (int seed = 1; seed <= 2; seed++)
        {
            Fresh(.05f);
            Random.InitState(seed * 31);
            e = InPlay("furnace", new Vector2(0f, 3.4f));
            mortar = (FireMortarAttack)e.Attack;
            e.EngageLeft = 9999f;
            e.AttackCooldown = 0f;
            var casts = new List<string>();
            int seen = e.Attacks;
            for (int i = 0; i < 30 * 70 && casts.Count < 4; i++)
            {
                EliteSystem.Step(Dt);
                if (!Alive(e)) break;
                if (e.Attacks > seen && e.Telling) { seen = e.Attacks; casts.Add(mortar.Bombs ? (mortar.Shell ? "both" : "bombs") : mortar.Doubled ? "double" : "shell"); }
            }
            runs.Add(Seq(casts));
            EliteSystem.Clear();
        }
        Check("furnace: the phase-1 loop is shell, bombs, shell, bombs (" + runs[0] + "), the same under another seed", runs[0] == "shell,bombs,shell,bombs" && runs[0] == runs[1]);

        // phase 2: the shells come as two lanes with a gap
        Fresh(.05f);
        e = InPlay("furnace", new Vector2(0f, 3.4f));
        mortar = (FireMortarAttack)e.Attack;
        e.EngageLeft = 9999f;
        e.AttackCooldown = 99f;
        for (int i = 0; i < 2; i++) { Step(EliteShip.GraceSeconds + .2f); e.TakeHit(EliteDamage.Teleport, e.Position, 1); }
        Step(.3f);
        e.AttackCooldown = 0f;
        bool doubled = false;
        for (int i = 0; i < 30 * 40 && !doubled; i++)
        {
            EliteSystem.Step(Dt);
            if (!Alive(e)) break;
            doubled |= e.Telling && mortar.Doubled;
        }
        Check("furnace at " + e.Hearts + " hearts (phase " + e.Script.Phase + "): a cast of two lanes with a gap between them (" + doubled + ")", e.Script.Phase == 2 && doubled);
        EliteSystem.Clear();
    }

    // ---- allocations -------------------------------------------------------------------

    static void Allocations()
    {
        // (one at a time: none of them roams any more, so six together would crowd the top and crash into each other)
        long total = 0, control = 0;
        bool meter = true;
        foreach (string brain in new[] { "stoker", "strafer", "scavenger", "ashling", "kiln", "furnace" })
        {
            Fresh(.1f);
            pilot.position = new Vector3(0f, -2.5f, 0f);
            var x = InPlay(brain, new Vector2(-1.2f, 2.5f));
            x.EngageLeft = 9999f;
            x.AttackCooldown = 0f;
            int f = 0;
            Step(24f, () => pilot.position = new Vector3(Mathf.Sin(f++ * .03f), -2.5f, 0f));   // warm up: its attacks
            if (!Alive(x)) { Check(brain + ": alive after its warm-up", false); continue; }
            x.AttackCooldown = 99f;                                                              // (a cast builds its telegraph: a spawn, not a frame)
            Step(6f);
            bool ok = TestHarness.AllocMeterWorks(out control);
            meter &= ok;
            total += TestHarness.AllocatedBytes(() =>
            {
                for (int i = 0; i < 90; i++)
                {
                    pilot.position = new Vector3(Mathf.Sin(i * .05f), -2.5f, 0f);
                    EliteSystem.Step(Dt);
                }
            });
            EliteSystem.Clear();
        }
        Check("zero per-frame allocations stepping each of the six Ember elites between attacks (" + total + " bytes over 90 frames each; meter control " + control + ")", meter && total == 0);
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
