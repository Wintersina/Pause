using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// The four Space elites (Eventide Bastion, Orbit Reaver, Rift Lancer,
// Singularity Hauler): the roster, their launch sites INSIDE the Space
// backdrop's stations, planets and big asteroids (SpaceDirector's
// LandingSites, LandingSite.emerge), the director choosing each one's own
// kind of site, the launch (docked and hidden, the engine-light tell, the
// dock flare, flying out and fading in), joining, following the pilot,
// attacking, a clean despawn -- and each one's own brain + attack
// signature (fortress / ward_barrage, swooper / swoop_volley, sniper /
// rift_snipe, wrecker / tow_sling: SpaceElites.cs) with zero per-frame
// allocation, and the pause attack's 2 damage.
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

    // key -> brain, attack, its HOME (the first kind of its launchFrom: a backdrop dock; its second is the ordinary body it also takes)
    static readonly (string key, string brain, string attack, LandingKind from)[] Expected =
    {
        ("space_elite_eventide_bastion", "fortress", "ward_barrage", LandingKind.Gantry),
        ("space_elite_orbit_reaver", "swooper", "swoop_volley", LandingKind.Carrier),
        ("space_elite_rift_lancer", "sniper", "rift_snipe", LandingKind.Gate),
        ("space_elite_singularity_hauler", "wrecker", "tow_sling", LandingKind.Derelict),
    };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EliteCatalog.Reload();
            Roster();
            Scripts();
            BackdropSites();
            foreach (var x in Expected) LaunchFollowAttackDespawn(EliteCatalog.Find(x.key), x.from);
            Bastion();
            Reaver();
            Lancer();
            Wrecker();
            PauseDamage();
            EngineFlames();
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

    // The nearest each may come to the pilot outside its committed attacks.
    static float MinDistance(string brain)
    {
        switch (brain)
        {
            case "sniper": return SniperBrain.MinRange * .95f;
            case "swooper": return 2.1f;
            default: return 3f;
        }
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
            Check(x.key + ": the engine-burn strip (parked, pilot lights, ignition, an 8-drawing burn cycle, a 6-drawing damaged burn loop) is installed",
                  frames != null && frames.Length == 17 && c.parked == 0 && c.parkedIdle == 1 && c.liftoff == 2 && c.Flight0 == 3 &&
                  c.flight.Length == 8 && c.damaged == 11 && c.HasDamagedLoop && c.damagedLoop.Length == 6 && c.damagedLoop[0] == 11 && c.damagedLoop[5] == 16);
            Check(x.key + ": it stays level: no bank cells, no bank tilt, the codex loop is the burn cycle (" + c.CodexLoop.Length + " drawings)",
                  !c.Banks && c.bankLeft < 0 && c.bankRight < 0 && d.maxBank == 0f && c.CodexLoop.Length == 8);
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
        for (int i = 0; i < 15 * 240 && !(seen.Contains(LandingKind.Station) && seen.Contains(LandingKind.Planet) && seen.Contains(LandingKind.Asteroid)); i++)
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
        // Wait for a moment with three bodies offering sites -- or, after a
        // minute, two: edge-peeking stations (about 40% of them) are never
        // offered, so three at once can take a while.
        for (int i = 0; i < 15 * 180; i++)
        {
            wb.Step(1f / 15f);
            int n = LandingSites.Collect(sites);
            if (n >= 3 || (n >= 2 && i >= 15 * 60)) break;
        }
        var dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        int made = dir.SpawnGroup(0, 3);
        bool own = made > 0, docked = true, preferred = true;
        foreach (var e in EliteShip.Live)
        {
            own &= e.Def.WorldIndex == 0 && e.State == EliteState.Parked;
            docked &= e.IsDocked && e.Hull.color.a == 0f && !e.Collider.enabled;
            bool offered = false;
            foreach (var st in sites) offered |= LandingSite.Accepts(e.Def.launchFrom, st.kind);
            // (one of its own kinds unless another elite of the group took the only one)
            int same = 0;
            foreach (var o in EliteShip.Live) if (o != e && o.Def.launchFrom == e.Def.launchFrom) same++;
            if (offered && same == 0) preferred &= LandingSite.Accepts(e.Def.launchFrom, e.Site.kind);
        }
        Check("the director docks Space elites in the backdrop's bodies (" + made + ")", own && made == Mathf.Min(EliteDirector.MaxAlive, sites.Count));
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
            foreach (var e in EliteShip.Live) match &= LandingSite.Accepts(e.Def.launchFrom, e.Site.kind);
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
        int flares = EliteSystem.Fx.DockFlares, spawned0 = EliteSystem.Fx.Spawned;
        var e = EliteShip.Create(def, site, 2.5f, new Vector2(-1.4f, 1.4f));
        Check(k + "docked inside its " + kind + ": hidden, no collider, not a target",
              e.IsDocked && e.Hull.color.a == 0f && !e.Collider.enabled && !e.CompareTag("Enimey") && e.GetComponent<ClearTarget>() == null);
        bool lightsEarly = false, lightsLate = false, rides = true, blinkEarly = false, blinkLate = false, blinkOff = false;
        int guard = 0;
        while (e.State == EliteState.Parked && guard++ < 400)
        {
            site.anchor.position += Vector3.down * .2f * Dt;     // the body drifts down the view
            EliteSystem.Step(Dt);
            if (e.State != EliteState.Parked) break;
            rides &= Vector2.Distance(e.transform.position, site.anchor.position) < .01f;
            if (e.StateTime < e.ParkSeconds - EliteShip.EngineTellSeconds - .05f) lightsEarly |= e.EngineLightsOn;
            else lightsLate |= e.EngineLightsOn;
            if (e.StateTime < e.ParkSeconds - EliteShip.EngineTellSeconds - .05f) blinkEarly |= e.LaunchLightOn;
            else { blinkLate |= e.LaunchLightOn; blinkOff |= !e.LaunchLightOn; }
        }
        Check(k + "rides its body; only its pilot light blinks at the dock, in its last parked second", rides && !lightsEarly && lightsLate);
        Check(k + "ONE small launch light blinks (on and off) in the launch tell only, no ring or burst spawned (" + (EliteSystem.Fx.Spawned - spawned0) + ")",
              !blinkEarly && blinkLate && blinkOff && EliteSystem.Fx.Spawned == spawned0);
        Check(k + "launches with no dock flare and no ground dust, the launch light out",
              e.State == EliteState.LiftOff && EliteSystem.Fx.DockFlares == flares && EliteSystem.Fx.Spawned == spawned0 && !e.LaunchLightOn);
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

        // antagonises, keeps its distance: the pilot crosses the board, it never comes in on it (outside its committed attacks)
        float nearest = 99f, path = 0f;
        Vector2 last = e.Position;
        e.AttackCooldown = 99f;
        for (int i = 0; i < 30 * 12; i++)
        {
            float px = Mathf.Sin(i * .03f) * 1.3f;
            pilot.position = new Vector3(px, -2.6f, 0f);
            EliteSystem.Step(Dt);
            if (!Alive(e)) break;
            if (!e.Telling && !e.Acting) nearest = Mathf.Min(nearest, Vector2.Distance(e.Position, pilot.position));
            path += Vector2.Distance(e.Position, last);
            last = e.Position;
            e.AttackCooldown = 99f;
        }
        Check(k + "keeps its distance from the pilot (nearest " + nearest.ToString("0.0") + " u, " + path.ToString("0.0") + " u flown)",
              Alive(e) && nearest >= MinDistance(def.brain) && path > 1f);

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
        Check(k + "attacks (" + e.Attack.Id + "): a wind-up, something thrown, back to following (" + (EliteSystem.Shots.Launched - launched) + " shots, " + e.Attack.Fired + " fired)",
              told && acted && back && e.Attacks > attacks && (EliteSystem.Shots.Launched > launched || e.Attack.Fired > 0));

        // despawns cleanly: shot down, paid, gone
        float total = RunScore.Total;
        var go = e.gameObject;
        for (int i = 0; i < def.hearts && Alive(e); i++)
        {
            e.TakeShipAttack(0, 1f, e.Position);
            Step(EliteShip.GraceSeconds + .05f);
            for (int w = 0; w < 600 && Alive(e) && e.Hidden; w++) EliteSystem.Step(Dt);   // (the hide-when-hurt ones are waited out)
        }
        Check(k + "two hits shoot it down: paid, removed, its object gone",
              !Alive(e) && go == null && !EliteShip.Live.Contains(e) && RunScore.Total - total == ScoreRules.EliteDown);
        Step(1f);
        EliteSystem.Clear();
        Check(k + "nothing left behind after a clear (" + EliteShip.Live.Count + " live)", EliteShip.Live.Count == 0);
    }

    // ---- Eventide Bastion: fortress + ward_barrage ---------------------------------------

    // Hearts the shot-down way (a player weapon, the grace waited out).
    static void LoseHeart(EliteShip e)
    {
        e.TakeShipAttack(0, 1f, e.Position);
        Step(EliteShip.GraceSeconds + .05f);
    }

    // The script's next attacking beat is `attack` (the beats before it are stepped over, tokens delivered), and it begins now.
    static void Cast(EliteShip e, string attack)
    {
        for (int i = 0; i < 20 && e.Script.Step.attack != attack; i++) e.Script.Advance();
        e.ForceAttack();
    }

    // The x where each live bolt of the curtain crosses the row's height.
    static List<float> RowCrossings(float rowY, System.Func<EliteShot, bool> pick)
    {
        var xs = new List<float>();
        foreach (var s in EliteSystem.Shots.All)
        {
            if (!s.Active || s.Kind != EliteShots.Kind.Bolt || !pick(s)) continue;
            Vector2 v = s.Velocity, a = s.LaunchedAt;
            if (v.y >= -.01f) continue;
            xs.Add(a.x + v.x * (rowY - a.y) / v.y);
        }
        xs.Sort();
        return xs;
    }

    static void Bastion()
    {
        Fresh(.02f);
        pilot.position = new Vector3(1.4f, -2.5f, 0f);
        var e = InPlay("fortress", new Vector2(-1f, 1f));
        var def = e.Def;
        var brain = (FortressBrain)e.Brain;
        float minX = 9f, maxX = -9f, topDev = 0f, fastest = 0f, near = 99f;
        Step(4f);   // (to its station first)
        Step(14f, () =>
        {
            minX = Mathf.Min(minX, e.Position.x); maxX = Mathf.Max(maxX, e.Position.x);
            topDev = Mathf.Max(topDev, Mathf.Abs(e.Position.y - brain.Height));
            fastest = Mathf.Max(fastest, e.Velocity.magnitude);
            near = Mathf.Min(near, Vector2.Distance(e.Position, pilot.position));
        });
        Check("fortress holds near the top of the view and slides sideways at a crawl (x " + minX.ToString("0.0") + ".." + maxX.ToString("0.0") +
              ", " + fastest.ToString("0.0") + " u/s, " + topDev.ToString("0.0") + " off its height)",
              maxX - minX > .8f && fastest < 1.6f && topDev < .5f && e.Position.y > pilot.position.y + 4f);
        Check("... and never closes in on the pilot (nearest " + near.ToString("0.0") + " u)", near > 4f);
        Check("fortress is armoured (rocks don't cost it a heart)", def.armored && def.hearts == 5);

        // the curtain, hearts 5..1: the gap narrows a little per heart, a second row at <= 2
        var widths = new List<float>();
        bool narrows = true, doubled = true, otherSide = true, delayed = true, flanked = true, openGap = true;
        float lastWidth = 99f;
        for (int h = 5; h >= 1; h--)
        {
            Fresh(.02f);
            pilot.position = new Vector3(.4f, -2.5f, 0f);
            e = InPlay("fortress", new Vector2(0f, 1f));
            for (int k = def.hearts; k > h; k--) LoseHeart(e);
            Step(.3f);
            e.AttackCooldown = 99f;
            var barrage = (WardBarrageAttack)e.Attack;
            e.ForceAttack();
            Check("fortress " + h + " hearts: its first cast is a curtain", !barrage.MineDrop);
            bool rings = false, ringsLocked = false;
            while (e.Telling) { EliteSystem.Step(Dt); if (!e.Telling) break; rings |= barrage.Marks.ShownCount > 0; ringsLocked |= barrage.Marks.ShownCount >= barrage.Count - 1; }
            int n = barrage.Count;
            Vector2 gap1 = barrage.Spot(barrage.Gap, barrage.Gap), pilotAt = barrage.Row;
            float width = barrage.GapWidth;
            widths.Add(width);
            narrows &= h == 5 || width < lastWidth - .05f || width <= WardBarrageAttack.MinGap + .001f;
            lastWidth = width;
            int before = EliteSystem.Shots.Launched;
            Step(.7f);
            int row1 = EliteSystem.Shots.Launched - before;
            var xs1 = RowCrossings(barrage.Row.y, s => true);
            // the gap between the two bolts beside the open slot
            float lo = -99f, hi = 99f;
            foreach (float x in xs1) { if (x < gap1.x) lo = Mathf.Max(lo, x); else hi = Mathf.Min(hi, x); }
            flanked &= Mathf.Abs((hi - lo) - width) < .08f;
            openGap &= xs1.TrueForAll(x => Mathf.Abs(x - gap1.x) > width * .4f);
            Check("fortress " + h + " hearts: rings mark the row's spots from the tell (" + rings + "), " + (n - 1) + " bolts in the first row (" + row1 + "), gap " + width.ToString("0.00") + " u",
                  rings && ringsLocked && row1 == n - 1);
            if (h > 3)
            {
                Step(1.5f);
                doubled &= EliteSystem.Shots.Launched - before == n - 1;
            }
            else
            {
                // the second row, RowDelay after the first, its gap on the other side of the pilot
                Vector2 gap2 = barrage.Spot(barrage.Gap2, barrage.Gap2);
                bool distinct = barrage.Gap2 != barrage.Gap;
                Step(WardBarrageAttack.RowDelay - .7f + .4f);
                int total = EliteSystem.Shots.Launched - before;
                doubled &= total == 2 * (n - 1) && barrage.SecondRow;
                var xs2 = RowCrossings(barrage.Row.y, s => s.Age < .6f);
                otherSide &= distinct && (gap1.x - pilotAt.x) * (gap2.x - pilotAt.x) < 0f;
                openGap &= xs2.TrueForAll(x => Mathf.Abs(x - gap2.x) > width * .4f);
                delayed &= WardBarrageAttack.RowDelay >= WardBarrageAttack.CrossNeeded(def.lobSpacing);
                Step(1f);
            }
            while (e.State == EliteState.Attack) EliteSystem.Step(Dt);
        }
        Check("the gap narrows as hearts drop (" + string.Join(", ", widths.ConvertAll(w => w.ToString("0.00"))) + ")", narrows && widths[0] > widths[widths.Count - 1] + .2f && widths[widths.Count - 1] >= WardBarrageAttack.MinGap - .001f);
        Check("... the two bolts beside the gap close in on its width, and nothing is aimed into it", flanked && openGap);
        Check("... a second row follows in phase 2 and the last phase (<= 3 hearts) and only then", doubled);
        Check("... its gap is on the other side of the pilot, and the delay (" + WardBarrageAttack.RowDelay + " s) outlasts a crossing (" +
              WardBarrageAttack.CrossNeeded(def.lobSpacing).ToString("0.00") + " s)", otherSide && delayed);

        // the mine drop: telegraphed, slow, shootable (two shots), pops into a ring
        Fresh(.02f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        e = InPlay("fortress", new Vector2(0f, 1f));
        var bar = (WardBarrageAttack)e.Attack;
        Cast(e, "mine");
        Check("the script's mine beat is a mine drop", bar.MineDrop);
        bool ringed = false;
        while (e.Telling) { EliteSystem.Step(Dt); if (!e.Telling) break; ringed |= bar.Marks.ShownCount == bar.Mines && bar.Mines >= 1; }
        Step(def.actionSeconds + .3f);
        e.AttackCooldown = 99f;
        EliteShot mine = bar.Mine(0);
        Check("... the drop spot is ringed through the tell, then a mine glides out of a pod (" + (mine != null && mine.Active ? "live" : "none") + ")",
              ringed && mine != null && mine.IsMine && mine.Kind == EliteShots.Kind.Orb && mine.MarkShown);
        if (mine != null && mine.Active)
        {
            Step(1.2f);
            Vector2 v = mine.Velocity;
            Check("... a slow shield mine drifting with the board (" + v.magnitude.ToString("0.0") + " u/s), a fuse ring on it (" + mine.FuseLeft.ToString("0.0") + " s left)",
                  v.magnitude < EliteSystem.Scroll + 1f && mine.FuseLeft > 0f && mine.FuseLeft < def.hazardSeconds + WardBarrageAttack.MineGlide);
            mine.Struck();
            Check("... one shot only cracks it (" + def.hazardArmour + " defuse it)", mine.Active && mine.Armour == def.hazardArmour - 1);
            int bolts0 = EliteSystem.Shots.Launched;
            Step(def.hazardSeconds + 1f);
            int popped = EliteSystem.Shots.Launched - bolts0;
            Check("... left alone it pops into a ring of " + def.hazardCount + " short bolts (" + popped + ")", popped == def.hazardCount && !mine.Active);
        }
        // a second mine, shot down twice: gone, no ring
        e.AttackCooldown = 99f;
        Cast(e, "mine");
        while (e.Telling) EliteSystem.Step(Dt);
        Step(def.actionSeconds + .3f);
        e.AttackCooldown = 99f;
        var m2 = bar.Mine(0);
        if (m2 != null && m2.Active)
        {
            int launched = EliteSystem.Shots.Launched;
            for (int k = 0; k < def.hazardArmour; k++) m2.Struck();
            Step(def.hazardSeconds + 1f);
            Check("a ward mine shot down " + def.hazardArmour + "x is defused: no ring", !m2.Active && EliteSystem.Shots.Launched == launched);
        }
        else Check("a second mine drop made a mine", false);
    }

    // ---- Orbit Reaver: swooper + swoop_volley ----------------------------------------

    static void Reaver()
    {
        Fresh(.02f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        var e = InPlay("swooper", new Vector2(1.5f, 0f));
        var def = e.Def;
        var brain = (SwooperBrain)e.Brain;
        e.EngageLeft = 9999f;   // (60 s is longer than its engagement: it would go home)
        Check("reaver does not roam: it flies a racetrack inside the view", !brain.Roams);
        bool onBoard = true, touchable = true;
        float near = 99f, topSpeed = 0f, maxX = 0f;
        int flips = 0, lastSign = 0;
        float clock = 0f;
        for (int i = 0; i < 30 * 60; i++)
        {
            clock += Dt;
            pilot.position = new Vector3(Mathf.Sin(clock * .5f) * 1.2f, -2.5f, 0f);
            EliteSystem.Step(Dt);
            if (!Alive(e)) break;
            e.AttackCooldown = Mathf.Min(e.AttackCooldown, 1f);
            onBoard &= !e.Offstage && !e.Hidden;
            touchable &= e.Collider.enabled && e.CompareTag("Enimey");
            maxX = Mathf.Max(maxX, Mathf.Abs(e.Position.x));
            if (!e.Telling && !e.Acting) near = Mathf.Min(near, Vector2.Distance(e.Position, pilot.position));
            topSpeed = Mathf.Max(topSpeed, e.Velocity.magnitude);
            int sign = e.Velocity.x > .8f ? 1 : e.Velocity.x < -.8f ? -1 : 0;
            if (sign != 0 && sign != lastSign) { if (lastSign != 0) flips++; lastSign = sign; }
        }
        Check("reaver stays on the board all the time and never past the rails (x up to " + maxX.ToString("0.0") + ")", onBoard && maxX <= EliteSystem.RailEdge + .2f);
        Check("... a target all the time (collider on, a hazard)", touchable);
        Check("... it turns round at the rails: " + brain.Turns + " U-turns, " + flips + " reversals of its sideways run in 60 s, " + brain.Laps + " laps", brain.Turns >= 6 && flips >= 6 && brain.Laps >= 4);
        Check("... and it never comes near the pilot between volleys (nearest " + near.ToString("0.0") + " u, flying up to " + topSpeed.ToString("0.0") + " u/s)", near > 2.2f);
        Check("... its volleys are the script's: " + e.Attacks + " in 60 s", e.Attacks >= 6);
        Check("... the same fixed sequence of beats whatever the random seed",
              string.Join(",", BeatTrace("swooper", 7, 6)) == string.Join(",", BeatTrace("swooper", 4242, 6)));

        // the volley: lock ring, then shards crossing on the locked spot
        Fresh(.02f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        e = InPlay("swooper", new Vector2(1.5f, 0f));
        brain = (SwooperBrain)e.Brain;
        var volley = (SwoopVolleyAttack)e.Attack;
        e.AttackCooldown = 0f;
        int guard = 0;
        while (!e.Telling && guard++ < 30 * 20) { EliteSystem.Step(Dt); e.AttackCooldown = 0f; }
        Check("reaver winds up on a lane pass (no hold): it keeps flying through the tell", e.Telling && brain.InWindow && (brain.Segment & 1) == 0);
        float tellSpeed = 99f;
        bool ring = false, ringFast = false, early = false, lockedStill = true;
        Vector2 locked = Vector2.zero;
        int tellSteps = 0;
        while (e.Telling)
        {
            pilot.position = new Vector3(-.8f + tellSteps * .02f, -2.5f, 0f);   // the pilot keeps moving
            EliteSystem.Step(Dt);
            tellSteps++;
            if (!e.Telling) break;
            tellSpeed = Mathf.Min(tellSpeed, e.Velocity.magnitude);
            ring |= volley.Marks.Shown(0);
            if (e.AimLocked) { if (locked == Vector2.zero) locked = volley.LockedAt; else lockedStill &= Vector2.Distance(locked, volley.LockedAt) < .001f; ringFast = true; }
            else early = true;
        }
        Check("... a marker ring on the pilot's spot from the first frame, tracking, then locked (" + locked + ")", ring && ringFast && early && lockedStill && locked != Vector2.zero);
        Check("... still flying through the wind-up (" + tellSpeed.ToString("0.0") + " u/s)", tellSpeed > def.speed * .8f);
        pilot.position = new Vector3(1.6f, -2.5f, 0f);   // moves off: the shards still go to the locked spot
        int before = EliteSystem.Shots.Launched;
        var froms = new List<Vector2>();
        bool aimed = true;
        guard = 0;
        while (e.State == EliteState.Attack && guard++ < 200)
        {
            int was = EliteSystem.Shots.Launched;
            EliteSystem.Step(Dt);
            if (EliteSystem.Shots.Launched == was) continue;
            foreach (var s in EliteSystem.Shots.All)
            {
                if (!s.Active || s.Age > Dt * 1.5f) continue;
                froms.Add(s.LaunchedAt);
                aimed &= Vector2.Dot(s.Velocity.normalized, (locked - s.LaunchedAt).normalized) > .995f;
            }
        }
        bool both = false;
        for (int i = 1; i < froms.Count; i++) both |= Vector2.Distance(froms[i], froms[0]) > .3f;
        Check("... " + def.shotCount + " shards from the claws, every one at the locked spot, crossing there from the pass angle (" + (EliteSystem.Shots.Launched - before) + ")",
              EliteSystem.Shots.Launched - before == def.shotCount && aimed && both);

        // hurt: a flinch on the board, then the faster phase 2 loops with a bigger volley
        Fresh(.02f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        e = InPlay("swooper", new Vector2(1.5f, 0f));
        brain = (SwooperBrain)e.Brain;
        e.EngageLeft = 9999f;
        e.AttackCooldown = 99f;
        float v0 = 0f;
        Step(20f, () => v0 = Mathf.Max(v0, e.Velocity.magnitude));
        float p0 = brain.PointerSpeed;
        LoseHeart(e);
        Check("reaver hit: a flinch on the board, no hiding", e.InPlay && !e.Hidden);
        int g2 = 0;
        while (e.Hidden && g2++ < 600) EliteSystem.Step(Dt);
        float v1 = 0f;
        e.AttackCooldown = 99f;
        Step(20f, () => v1 = Mathf.Max(v1, e.Velocity.magnitude));
        Check("... and comes back with faster loops: the track pointer runs " + brain.PointerSpeed.ToString("0.0") + " u/s (was " + p0.ToString("0.0") + "; ship top speed " + v0.ToString("0.0") + " -> " + v1.ToString("0.0") + ")",
              brain.PointerSpeed > p0 * 1.2f && v1 >= v0 && Alive(e));
        Check("... and a bigger volley (" + e.Def.shotCount + " shards in phase 2)", e.Script.Phase == 2 && e.Def.shotCount == 5);

        // the last heart: the one scripted exit by the left, then the figure eight
        e.TakeHit(EliteDamage.PlayerWeapon, e.Position + Vector2.left * .4f);
        Check("reaver's last heart: the one scripted exit, by the left", e.Hidden && e.LastExitDone && e.HideSide == 0);
        int g3 = 0;
        while (e.Hidden && g3++ < 900) EliteSystem.Step(Dt);
        e.AttackCooldown = 99f;
        Step(1f);
        Check("... back from the other side onto the figure eight (" + (brain.Eight ? "eight" : "racetrack") + ")", e.InPlay && brain.Eight && e.Script.Phase == 3);
        bool crossesMid = false;
        float minMidX = 9f;
        Step(8f, () => { if (Mathf.Abs(e.Position.x) < .5f) crossesMid = true; minMidX = Mathf.Min(minMidX, Mathf.Abs(e.Position.x)); });
        Check("... crossing the middle of the board (closest to x = 0: " + minMidX.ToString("0.0") + ")", crossesMid && !e.Hidden && !e.Offstage);
    }

    // ---- Rift Lancer: sniper + rift_snipe ------------------------------------------------

    static void Lancer()
    {
        Fresh(.02f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        var e = InPlay("sniper", new Vector2(1.5f, 0f));
        var def = e.Def;
        var brain = (SniperBrain)e.Brain;
        Step(4f);
        float edge = EliteSystem.RailEdge;
        Check("lancer perches at long range on a flank, at the edge of the board (" + e.Position + ", " + Vector2.Distance(e.Position, pilot.position).ToString("0.0") + " u off)",
              brain.Phase == SniperBrain.Stage.Perch && Mathf.Abs(e.Position.x) > edge - 1f && Vector2.Distance(e.Position, pilot.position) >= SniperBrain.MinRange &&
              Mathf.Abs(Mathf.DeltaAngle(e.Facing, Mathf.Atan2(pilot.position.y - e.Position.y, pilot.position.x - e.Position.x) * Mathf.Rad2Deg)) < 30f);
        e.ForceAttack();
        var rail = (RiftSnipeAttack)e.Attack;
        bool sight = false, trackedEarly = false, lockedStill = true;
        float maxW = 0f, maxA = 0f, minA = 9f;
        Vector2 lockedDir = Vector2.zero;
        float tellSpeed = 0f;
        int i0 = 0;
        while (e.Telling)
        {
            pilot.position = new Vector3(Mathf.Sin(i0++ * .15f) * .9f, -2.5f, 0f);   // the pilot keeps moving all through the wind-up
            Vector2 d0 = rail.Dir;
            EliteSystem.Step(Dt);
            if (!e.Telling) break;
            sight |= e.SightShown;
            if (e.SightShown) { maxW = Mathf.Max(maxW, e.SightWidth); maxA = Mathf.Max(maxA, e.SightAlpha); minA = Mathf.Min(minA, e.SightAlpha); }
            tellSpeed = Mathf.Max(tellSpeed, e.Velocity.magnitude);
            if (!rail.Locked) trackedEarly |= Vector2.Dot(d0, rail.Dir) < .9999f;
            else { if (lockedDir == Vector2.zero) lockedDir = rail.Dir; lockedStill &= Vector2.Dot(lockedDir, rail.Dir) > .9999f; }
        }
        pilot.position = new Vector3(0f, -2.5f, 0f);
        Check("rift_snipe tell: planted, a sight line tracks the pilot, then locks (" + tellSpeed.ToString("0.0") + ")", sight && trackedEarly && lockedStill && tellSpeed < 1.2f);
        Check("rift_snipe sight: a subtle hairline (" + maxW.ToString("0.000") + " u <= " + TelegraphStyle.MaxLineWidth + ", alpha " + minA.ToString("0.00") + ".." + maxA.ToString("0.00") + ", gentle blink)",
              maxW > 0f && maxW <= TelegraphStyle.MaxLineWidth + 1e-4f && maxA <= TelegraphStyle.LineAlpha + 1e-3f && minA >= TelegraphStyle.LineAlphaLow - 1e-3f);
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
              EliteSystem.Shots.Launched - before == def.shotCount && rails == def.shotCount && parallel && fast && !e.SightShown && !rail.MineRun);

        // the script: perch right (snipe, snipe, mine), cross to the left flank, (snipe, snipe, mine), cross back ...
        Fresh(.02f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        e = InPlay("sniper", new Vector2(1.5f, 0f));
        brain = (SniperBrain)e.Brain;
        rail = (RiftSnipeAttack)e.Attack;
        Check("lancer does not roam: it never leaves the view", !brain.Roams);
        e.AttackCooldown = 0f;
        e.EngageLeft = 9999f;
        var steps = new List<int>();
        var variants = new List<string>();
        var sides = new List<float>();
        int lastAttacks = e.Attacks, minesSeen = 0;
        float nearest = 99f;
        bool onBoard = true, touchable = true;
        float clock = 0f;
        for (int i = 0; i < 30 * 120 && steps.Count < 9; i++)
        {
            clock += Dt;
            pilot.position = new Vector3(Mathf.Sin(clock * .5f) * 1.2f, -2.5f, 0f);
            EliteSystem.Step(Dt);
            if (!Alive(e)) break;
            onBoard &= !e.Offstage && !e.Hidden && Mathf.Abs(e.Position.x) <= EliteSystem.RailEdge + .2f;
            touchable &= e.Collider.enabled && e.CompareTag("Enimey");
            if (e.Attacks != lastAttacks)
            {
                lastAttacks = e.Attacks;
                // (a wind-up broken off repeats its beat: the sequence is by beat)
                if (steps.Count == 0 || steps[steps.Count - 1] != e.Script.StepIndex)
                {
                    steps.Add(e.Script.StepIndex);
                    variants.Add(e.Script.VariantName);
                    sides.Add(brain.Side);
                    if (rail.MineRun) minesSeen++;
                }
            }
            if (!e.Telling && !e.Acting && brain.Phase == SniperBrain.Stage.Perch) nearest = Mathf.Min(nearest, Vector2.Distance(e.Position, pilot.position));
            e.AttackCooldown = Mathf.Min(e.AttackCooldown, 1.2f);
        }
        string want = string.Join(",", ExpectedBeats(def, 1, steps.Count));
        Check("lancer's phase 1 is its fixed loop: beats " + string.Join(",", steps) + " (script " + want + ")", steps.Count >= 6 && string.Join(",", steps) == want);
        Check("... snipe, snipe, mine (" + string.Join("/", variants) + ")", variants.Count >= 6 && variants[0] == "" && variants[1] == "" && variants[2] == "mine" && variants[3] == "" && variants[4] == "" && variants[5] == "mine");
        Check("... from the right flank, then the left (" + string.Join(",", sides) + ")", sides.Count >= 6 && sides[0] > 0f && sides[1] > 0f && sides[2] > 0f && sides[3] < 0f && sides[4] < 0f && sides[5] < 0f);
        Check("... it never leaves the view and is a target all the time (" + e.Hearts + " hearts)", onBoard && touchable);
        Check("... a rift mine on each mine beat (" + minesSeen + " seen), never within " + SniperBrain.MinRange + " u of the pilot at its perch (" + nearest.ToString("0.0") + ")",
              minesSeen >= 2 && nearest >= SniperBrain.MinRange * .95f);
        Check("... the same fixed sequence whatever the random seed",
              string.Join(",", BeatTrace("sniper", 7, 6)) == string.Join(",", BeatTrace("sniper", 12345, 6)));

        // the rift mine itself: the script's mine beat
        Fresh(.02f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        e = InPlay("sniper", new Vector2(1.5f, 0f));
        brain = (SniperBrain)e.Brain;
        rail = (RiftSnipeAttack)e.Attack;
        Step(4f);
        Cast(e, "mine");
        Check("the mine beat is a mine run", rail.MineRun && e.Telling);
        bool ringShown = false;
        Vector2 spot = Vector2.zero;
        while (e.Telling) { EliteSystem.Step(Dt); if (!e.Telling) break; ringShown |= rail.Marks.Shown(0); spot = rail.MineSpot; }
        Check("... its spot is ringed from the tell (on the line the sight locked)", rail.MineRun && ringShown);
        Step(def.actionSeconds + .1f);
        var mine = rail.Mine;
        Check("... a pink rift leaves the prong on the rail line", mine != null && mine.IsMine && mine.Active && mine.MarkShown);
        if (mine != null)
        {
            Step(def.lobSeconds + .3f);
            Vector2 d = (Vector2)mine.transform.position - spot;
            Check("... settles on its spot (" + d.magnitude.ToString("0.0") + " u off, riding the board), fuse ring running (" + mine.FuseLeft.ToString("0.0") + " s)", mine.FuseLeft > 1f && mine.FuseLeft < def.hazardSeconds + .1f && d.magnitude < 1.2f);
            e.AttackCooldown = 99f;
            for (int g = 0; g < 30 * 5 && mine.Active; g++) EliteSystem.Step(Dt);
            Step(Dt);
            var crossed = new List<Vector2>();
            foreach (var s in EliteSystem.Shots.All) if (s.Active && s.IsChip) crossed.Add(s.Velocity.normalized);
            bool cross = crossed.Count == def.hazardCount;
            if (cross)
            {
                // a "+": the four bolts are at right angles
                float a0 = Mathf.Atan2(crossed[0].y, crossed[0].x) * Mathf.Rad2Deg;
                var seen = new HashSet<int>();
                foreach (var c in crossed) seen.Add(Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(c.y, c.x) * Mathf.Rad2Deg - a0, 360f) / 90f) % 4);
                cross = seen.Count == 4;
            }
            Check("... pops into a short cross of " + def.hazardCount + " bolts after ~" + (def.lobSeconds + def.hazardSeconds).ToString("0.0") + " s (" + crossed.Count + ")", !mine.Active && cross);
            Step(RiftSnipeAttack.ArmSeconds + .2f);
            int live = 0;
            foreach (var s in EliteSystem.Shots.All) if (s.Active && s.IsChip) live++;
            Check("... and the bolts are short-lived (" + live + " left)", live == 0);
        }
        // a mine shot down before its fuse: no cross
        Fresh(.02f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        e = InPlay("sniper", new Vector2(1.5f, 0f));
        brain = (SniperBrain)e.Brain;
        rail = (RiftSnipeAttack)e.Attack;
        Step(4f);
        Cast(e, "mine");
        while (e.State == EliteState.Attack) EliteSystem.Step(Dt);
        e.AttackCooldown = 99f;
        mine = rail.Mine;
        int chips = 0;
        if (mine != null && mine.Active)
        {
            mine.Struck();
            Step(def.hazardSeconds + 1f);
            foreach (var s in EliteSystem.Shots.All) if (s.Active && s.IsChip) chips++;
        }
        Check("a rift mine is shootable: one shot defuses it, no cross", mine != null && !mine.Active && chips == 0);

        // phases: every shot from a new flank in phase 2; the last heart is the top-centre perch after the one scripted exit
        Fresh(.02f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        e = InPlay("sniper", new Vector2(1.5f, 0f));
        brain = (SniperBrain)e.Brain;
        Step(4f);
        float side0 = brain.Side;
        LoseHeart(e);
        e.AttackCooldown = 99f;
        Check("lancer phase 2: its loop crosses before every shot (" + e.Script.Step.move + ")", e.Script.Phase == 2 && e.Script.Step.move == "cross" && e.Script.Step.attack == "quick");
        e.AttackCooldown = 0f;
        for (int g = 0; g < 30 * 6 && e.Attacks == 0; g++) EliteSystem.Step(Dt);
        Check("... the first shot of phase 2 comes from the other flank (" + side0 + " -> " + brain.Side + ")", brain.Side == -side0);
        e.AttackCooldown = 99f;
        while (e.State == EliteState.Attack) EliteSystem.Step(Dt);
        e.TakeHit(EliteDamage.PlayerWeapon, e.Position + Vector2.left * .4f);
        Check("lancer's last heart: the one scripted exit, by the right", e.Hidden && e.LastExitDone && e.HideSide == 1);
        int guardX = 0;
        while (e.Hidden && guardX++ < 900) EliteSystem.Step(Dt);
        Step(.5f);
        e.AttackCooldown = 99f;
        Step(5f);
        Check("... and back on the board it perches at the top centre (x " + e.Position.x.ToString("0.0") + ")", e.InPlay && brain.Side == 0f && Mathf.Abs(e.Position.x) < 1.2f && e.Script.Phase == 3);
    }

    // The attacking beats' step indexes of `phase` of a def's script, in order and repeating, `n` of them.
    static List<int> ExpectedBeats(EliteDef def, int phase, int n)
    {
        var sd = def.script;
        var p = phase == 1 ? sd.phase1 : phase == 2 ? sd.phase2 : sd.last;
        var list = new List<int>();
        for (int k = 0; list.Count < n && k < 400; k++)
        {
            int i = k % p.loop.Length;
            if (p.loop[i].attack != "-") list.Add(i);
        }
        return list;
    }

    // The first `n` beats (step indexes) a fresh elite of `brain` flies in its first phase with the world seeded `seed`, left to itself.
    static List<int> BeatTrace(string brain, int seed, int n)
    {
        Fresh(.02f);
        Random.InitState(seed);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        var e = InPlay(brain, new Vector2(1.5f, 1f));
        e.AttackCooldown = 0f;
        e.EngageLeft = 9999f;
        var steps = new List<int>();
        int lastAttacks = e.Attacks;
        float clock = 0f;
        for (int i = 0; i < 30 * 150 && steps.Count < n; i++)
        {
            clock += Dt;
            pilot.position = new Vector3(Mathf.Sin(clock * .5f) * 1.2f, -2.5f, 0f);
            EliteSystem.Step(Dt);
            if (!Alive(e)) break;
            if (e.Attacks != lastAttacks)
            {
                lastAttacks = e.Attacks;
                if (steps.Count == 0 || steps[steps.Count - 1] != e.Script.StepIndex) steps.Add(e.Script.StepIndex);
            }
            e.AttackCooldown = Mathf.Min(e.AttackCooldown, 1.2f);
        }
        EliteSystem.Clear();
        return steps;
    }

    // ---- Singularity Hauler: wrecker + tow_sling ----------------------------------------

    static void Wrecker()
    {
        Fresh(.05f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        var e = InPlay("wrecker", new Vector2(.5f, 1f));
        var def = e.Def;
        var brain = (WreckerBrain)e.Brain;
        float near = 99f;
        Step(3f);   // (to its station first)
        Step(5f, () => near = Mathf.Min(near, Vector2.Distance(e.Position, pilot.position)));
        Check("hauler parks high in the board and never comes down to the pilot (" + e.Position + ", nearest " + near.ToString("0.0") + ")",
              e.Position.y > pilot.position.y + 4f && near > 4f && def.armored);

        // the V: the script's tokens haul it to the left station, down to the dip in the middle, then to the right station
        float xLeft = e.Position.x, yLeft = e.Position.y;
        e.Script.Advance();   // beat 1: "D"
        Step(5f);
        float xDip = e.Position.x, yDip = e.Position.y;
        e.Script.Advance();   // beat 2: "R"
        Step(5f);
        float xRight = e.Position.x;
        Check("hauler hauls itself in a V: left station (x " + xLeft.ToString("0.0") + "), a dip in the middle (" + xDip.ToString("0.0") + ", " + (yLeft - yDip).ToString("0.0") + " down), right station (" + xRight.ToString("0.0") + ")",
              xLeft < -.8f && Mathf.Abs(xDip) < .6f && yLeft - yDip > .3f && xRight > .8f && e.Position.y > pilot.position.y + 4f);

        // TOW: a sight line, a rock into the claws, a lane, the fling
        var tow = (TowSlingAttack)e.Attack;
        e.ForceAttack();
        Check("the first cast is the tow", !tow.SlingCast);
        bool sight = false, still = true;
        float towW = 0f;
        while (e.Telling) { EliteSystem.Step(Dt); if (!e.Telling) break; sight |= e.SightShown; if (e.SightShown) towW = Mathf.Max(towW, e.SightWidth); still &= e.Velocity.magnitude < 1.2f; }
        Check("tow tell: held while a sight line drops from the top of the view to the claws", sight && still);
        Check("tow sight: a subtle hairline (" + towW.ToString("0.000") + " u)", towW > 0f && towW <= TelegraphStyle.MaxLineWidth + 1e-4f);
        Step(Dt * 3f);
        var rock = tow.Rock;
        Check("the asteroid drifts in from above the view (a real Space rock: tag Astr, hazard, tied to the hauler)",
              rock != null && rock.transform.position.y > EliteSystem.ViewTop - .5f && rock.CompareTag("Astr") && ClearTarget.IsHazard(rock) &&
              rock.GetComponent<ClearTarget>().Mother == e && tow.Thrown != null && !tow.Thrown.Flung);
        pilot.position = new Vector3(1.0f, -2.5f, 0f);
        int guard = 0;
        while (tow.TowPhase == TowSlingAttack.Phase.Tow && guard++ < 200) EliteSystem.Step(Dt);
        Vector2 claw = tow.ClawPoint;
        float inClaws = rock != null ? Vector2.Distance(rock.transform.position, claw) : 99f;
        Check("... into its claws (" + inClaws.ToString("0.00") + " u off them)", rock != null && inClaws < .8f);
        Step(Dt * 2f);
        bool laneUp = tow.Lane.Showing && tow.TowPhase == TowSlingAttack.Phase.Lane;
        Vector2 dirLocked = tow.FlingDir;
        Check("... then a dotted lane is marked from the claws down the board to the pilot (dir " + dirLocked + ")", laneUp && dirLocked.y < -.3f);
        pilot.position = new Vector3(-1.0f, -2.5f, 0f);   // moves away: the lane is locked
        guard = 0;
        while (tow.TowPhase == TowSlingAttack.Phase.Lane && guard++ < 200) EliteSystem.Step(Dt);
        var thrown = tow.Thrown;
        Check("... and the rock is flung down it at " + def.hazardSpeed + " u/s (" + (thrown != null ? thrown.Velocity.magnitude.ToString("0.0") : "-") + "), the lane unchanged",
              thrown != null && thrown.Flung && Mathf.Abs(thrown.Velocity.magnitude - def.hazardSpeed) < .1f && Vector2.Dot(thrown.Velocity.normalized, dirLocked) > .999f);
        float bestOff = 99f;
        Vector2 aimPoint = new Vector2(1.0f, -2.5f);
        for (int i = 0; i < 30 * 4 && rock != null; i++)
        {
            EliteSystem.Step(Dt);
            if (rock == null) break;
            bestOff = Mathf.Min(bestOff, Vector2.Distance(rock.transform.position, aimPoint));
        }
        Check("... the hurtful hazard sweeps through the spot the lane was locked on (" + bestOff.ToString("0.0") + " u)", bestOff < 1.0f);
        Step(3f);
        Check("... and is gone off the bottom of the board (" + ThrownRock.All.Count + " left)", ThrownRock.All.Count == 0);

        // shot apart, with the same hit every weapon makes
        Fresh(.05f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        e = InPlay("wrecker", new Vector2(.5f, 1f));
        tow = (TowSlingAttack)e.Attack;
        e.ForceAttack();
        while (e.Telling) EliteSystem.Step(Dt);
        guard = 0;
        while (tow.TowPhase != TowSlingAttack.Phase.Settle && guard++ < 300) EliteSystem.Step(Dt);
        rock = tow.Rock;
        bool shot = rock != null && ShipAttackHits.Hit(rock, 0);
        Step(Dt);
        Check("the flung asteroid is shot apart like any rock (a player weapon destroys it)", shot && rock == null);
        Step(2f);

        // it bounces off the rails like a body with weight
        Fresh(.05f);
        pilot.position = new Vector3(2.1f, -2.5f, 0f);
        e = InPlay("wrecker", new Vector2(-.5f, 1f));
        tow = (TowSlingAttack)e.Attack;
        e.ForceAttack();
        guard = 0;
        while (tow.Thrown == null || !tow.Thrown.Flung) { EliteSystem.Step(Dt); if (guard++ > 600) break; }
        thrown = tow.Thrown;
        float vx0 = thrown != null ? thrown.Velocity.x : 0f;
        int bounces = 0;
        float vxAfter = vx0;
        for (int i = 0; i < 30 * 3 && thrown != null; i++)
        {
            EliteSystem.Step(Dt);
            if (thrown == null) break;
            bounces = thrown.Bounces;
            vxAfter = thrown.Velocity.x;
        }
        Check("... and glances off a side rail, reversing its sideways run (" + vx0.ToString("0.0") + " -> " + vxAfter.ToString("0.0") + ", " + bounces + " bounces)", vx0 > 0f && bounces >= 1 && vxAfter < 0f);
        Step(3f);

        // SLING: the well
        Fresh(.05f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        e = InPlay("wrecker", new Vector2(.5f, 1f));
        tow = (TowSlingAttack)e.Attack;
        e.ForceAttack();
        while (e.State == EliteState.Attack) EliteSystem.Step(Dt);     // cast 0: the tow
        Step(1f);
        e.ForceAttack();
        Check("the next cast is the gravity sling", tow.SlingCast);
        bool ringsUp = false;
        while (e.Telling) { EliteSystem.Step(Dt); if (!e.Telling) break; ringsUp |= tow.Marks.Shown(0) && tow.Marks.Shown(1); }
        Check("sling tell: the well is ringed ahead of the pilot from the first frame (" + tow.Well + ")", ringsUp && tow.Well.y > pilot.position.y && Mathf.Abs(tow.Well.x) < EliteSystem.RailEdge);
        var shots = new List<EliteShot>();
        var closest = new List<float>();
        guard = 0;
        while ((e.State == EliteState.Attack || AnySlung()) && guard++ < 300)
        {
            EliteSystem.Step(Dt);
            foreach (var s in EliteSystem.Shots.All)
            {
                if (!s.Active || !s.Slung) continue;
                int i = shots.IndexOf(s);
                if (i < 0) { shots.Add(s); closest.Add(9f); i = shots.Count - 1; }
                closest[i] = Mathf.Min(closest[i], Vector2.Distance(s.transform.position, s.SlingTarget));
            }
        }
        bool through = shots.Count > 0, l = false, r = false;
        foreach (float c in closest) through &= c < .3f;
        foreach (var s in shots) { if (s.LaunchedAt.x < e.Position.x) l = true; else r = true; }
        Check("gravity sling: " + def.shotCount + " shots out of both claws, each curved through the well (" + shots.Count + ", closest " + string.Join("/", closest.ConvertAll(c => c.ToString("0.00"))) + ")",
              shots.Count == def.shotCount && l && r && through);
        // the pull: gentle, bounded, toward the well, never into the ring
        Fresh(.05f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        Vector2 wellAt = new Vector2(0f, -.8f);
        EliteWell.Open(wellAt, TowSlingAttack.WellRing, TowSlingAttack.WellSeconds);
        float peak = 0f, closestEff = 99f;
        bool bounded = true, toward = true;
        for (float dist = 0f; dist <= EliteWell.Range + 1f; dist += .1f) bounded &= EliteWell.Strength(dist, TowSlingAttack.WellRing) >= 0f && EliteWell.Strength(dist, TowSlingAttack.WellRing) <= 1f;
        bool nothingInside = EliteWell.Strength(TowSlingAttack.WellRing + EliteWell.Margin * .9f, TowSlingAttack.WellRing) == 0f && EliteWell.Strength(EliteWell.Range + .1f, TowSlingAttack.WellRing) == 0f;
        pilot.position = new Vector3(wellAt.x + 1.5f, wellAt.y, 0f);
        for (int i = 0; i < 30 * 5; i++)
        {
            EliteSystem.Step(Dt);
            peak = Mathf.Max(peak, EliteWell.Offset.magnitude);
            Vector2 effective = (Vector2)pilot.position + EliteWell.Offset;
            closestEff = Mathf.Min(closestEff, Vector2.Distance(effective, wellAt));
            if (EliteWell.Offset.sqrMagnitude > 1e-6f) toward &= Vector2.Dot(EliteWell.Offset, wellAt - (Vector2)pilot.position) > 0f;
        }
        Check("the well tugs the ship gently: up to " + peak.ToString("0.00") + " u off the finger (cap " + EliteWell.Cap + "), toward the well, never into its ring (closest " + closestEff.ToString("0.00") + " u)",
              peak > .05f && peak <= EliteWell.Cap + 1e-4f && toward && closestEff > TowSlingAttack.WellRing + .25f && bounded && nothingInside);
        Check("... and it lets go when the well closes (" + EliteWell.Offset.magnitude.ToString("0.000") + " u left)", !EliteWell.Up && EliteWell.Offset.magnitude < .01f);
    }

    static bool AnySlung()
    {
        foreach (var s in EliteSystem.Shots.All) if (s.Slung) return true;
        return false;
    }

    // ---- the pause attack: 2 damage -------------------------------------------------------

    static void PauseDamage()
    {
        Check("the pause attack's damage is 2", EliteShip.PauseDamage == 2);
        foreach (var x in Expected)
        {
            Fresh(.05f);
            pilot.position = new Vector3(0f, -3f, 0f);
            var def = EliteCatalog.Find(x.key);
            var e = EliteShip.CreateInPlay(def, new Vector2(0f, 1.5f));
            e.AttackCooldown = 99f;
            int hearts = def.hearts;
            Step(.3f);
            EliteShip.TeleportStrike(e.gameObject, e.Position);
            Check(def.displayName + " (" + hearts + " hearts): a pause jump takes 2 (" + e.Hearts + " left)", Alive(e) && e.Hearts == hearts - 2);
            if (def.hurtHide) Check("... and the hurt one breaks off and hides", e.Hidden);
            // a second strike in the grace window still connects
            if (!e.Hidden)
            {
                EliteShip.TeleportStrike(e.gameObject, e.Position);
                Check("... the next pause jump (inside the grace window) takes 2 more (" + e.Hearts + " left)", e.Hearts == Mathf.Max(0, hearts - 4));
            }
        }
    }

    // ---- engine flames: baked into the strips, leaving each bell's mouth, in front of the hull ----------------
    // The burn cells (2..11) must show every engine's flame OVER the hull (a flame under the hull reads as
    // coming from behind the engine): the nozzle point sits in the dark bell mouth (cell 0), the flame covers it
    // in every burn cell, runs out past the hull's bottom edge, each engine on its own phase, no runtime plume
    // doubling it, and every cell keeps 6 px clear of the strip's border.
    static Color32[] LoadCell(string key, int cell, int size)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        // (cells 12.. are in the companion strip <key>_damaged.png)
        string path = "Assets/Art/Resources/Elites/Space/" + key + (cell >= 12 ? "_damaged" : "") + ".png";
        if (cell >= 12) cell -= 12;
        if (!tex.LoadImage(File.ReadAllBytes(path))) return null;
        var px = tex.GetPixels32(); int w = tex.width;
        Object.DestroyImmediate(tex);
        var cellPx = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                cellPx[y * size + x] = px[y * w + cell * size + x];   // rows run bottom-up (row 0 = the cell's bottom)
        return cellPx;
    }

    static void EngineFlames()
    {
        foreach (var e in Expected)
        {
            var def = EliteCatalog.Find(e.key);
            int S = def.cellPixels;
            var hullCell = LoadCell(e.key, 0, S);
            Check(e.key + ": strip loads for the flame check", hullCell != null);
            if (hullCell == null) continue;
            Check(e.key + ": no runtime plume doubles the baked flames (exhaustScale 0)", def.exhaustScale <= 0f);
            int hullBottom = 0;       // lowest hull row, counted from the top
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) if (hullCell[(S - 1 - y) * S + x].a > 128) hullBottom = Mathf.Max(hullBottom, y);
            var lengths = new HashSet<int>();
            for (int cell = 0; cell < 17; cell++)
            {
                var c = LoadCell(e.key, cell, S);
                int minX = S, minY = S, maxX = -1, maxY = -1;
                for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                    if (c[(S - 1 - y) * S + x].a > 0) { minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
                Check(e.key + " cell " + cell + ": 6 px clear of the border (" + minX + "," + minY + " .. " + maxX + "," + maxY + ")",
                      minX >= 6 && minY >= 6 && maxX <= S - 7 && maxY <= S - 7);
                if (cell < 2) continue;
                foreach (var nz in def.nozzles)
                {
                    int nx = Mathf.RoundToInt(nz.x), ny = Mathf.RoundToInt(nz.y);
                    var h0 = hullCell[(S - 1 - ny) * S + nx];
                    if (cell == 2)
                        Check(e.key + " " + nz.name + ": nozzle point is inside the dark bell mouth", h0.a == 255 && (h0.r * .3f + h0.g * .59f + h0.b * .11f) < 70f);
                    var p = c[(S - 1 - ny) * S + nx];
                    bool flameAtMouth = p.a == 255 && (p.r != h0.r || p.g != h0.g || p.b != h0.b) && Mathf.Max(p.r, Mathf.Max(p.g, p.b)) > 150;
                    Check(e.key + " cell " + cell + " " + nz.name + ": flame covers the nozzle point (in front of the hull, not hidden by the bell)", flameAtMouth);
                    // how far down the nozzle's column the flame reaches (rows that differ from the bare hull)
                    int tip = ny;
                    for (int y = ny; y < S; y++)
                    {
                        var q = c[(S - 1 - y) * S + nx]; var r0 = hullCell[(S - 1 - y) * S + nx];
                        if (q.a == 255 && (q.r != r0.r || q.g != r0.g || q.b != r0.b || q.a != r0.a)) tip = y;
                    }
                    if (cell >= 3 && cell <= 10)
                    {
                        Check(e.key + " cell " + cell + " " + nz.name + ": flame leaves the bell and runs past the hull's bottom edge (tip " + tip + " vs " + hullBottom + ")", tip >= hullBottom + 10);
                        if (cell == 3) lengths.Add(tip);
                    }
                }
            }
            Check(e.key + ": each engine keeps its own burn phase (flame lengths differ in cell 3)", lengths.Count >= 2);
        }
    }

    // ---- the authored scripts: roles, phases, the fixed sequence ------------------------------

    static bool SameBeats(ElitePhaseDef a, ElitePhaseDef b)
    {
        if (a.loop.Length != b.loop.Length) return false;
        for (int i = 0; i < a.loop.Length; i++)
            if (a.loop[i].attack != b.loop[i].attack || a.loop[i].move != b.loop[i].move) return false;
        return true;
    }

    // A phase differs from another in its pace, its numbers or its beats.
    static bool Differs(ElitePhaseDef a, ElitePhaseDef b)
    {
        if (!Mathf.Approximately(a.speed, b.speed) || !Mathf.Approximately(a.gap, b.gap)) return true;
        if ((a.set == null ? 0 : a.set.Length) != (b.set == null ? 0 : b.set.Length)) return true;
        return !SameBeats(a, b);
    }

    static void Scripts()
    {
        foreach (var x in Expected)
        {
            var d = EliteCatalog.Find(x.key);
            var sd = d.script;
            string k = d.displayName + ": ";
            bool has = sd != null && sd.phase1 != null && sd.phase2 != null && sd.last != null && sd.phase1.loop != null && sd.phase1.loop.Length >= 2 &&
                       sd.phase2.loop != null && sd.last.loop != null && sd.variants != null && sd.entrance != null;
            Check(k + "an authored script: a loop for each of the three phases, its variants and an entrance", has);
            if (!has) continue;
            Check(k + "entrance '" + sd.entrance.kind + "' of " + sd.entrance.seconds + " s (1-2 s)", sd.entrance.seconds >= 1f && sd.entrance.seconds <= 2f);
            Check(k + "phase 2 is a visibly different loop from phase 1, and the last heart from phase 2", Differs(sd.phase1, sd.phase2) && Differs(sd.phase2, sd.last));
            bool tokens = false, known = true;
            foreach (var p in new[] { sd.phase1, sd.phase2, sd.last })
                foreach (var st in p.loop)
                {
                    tokens |= !string.IsNullOrEmpty(st.move);
                    if (string.IsNullOrEmpty(st.attack) || st.attack == "-") continue;
                    bool found = false;
                    foreach (var v in sd.variants) found |= v.name == st.attack;
                    known &= found;
                }
            Check(k + "it moves by tokens (the Reaver flies its track by itself) and every beat names a variant it defines", (tokens || x.brain == "swooper") && known);
            bool exits = d.hurtHide && !d.armored;
            Check(k + "exit: " + (exits ? "the one scripted exit by the " + sd.lastExit : "none (armoured, it stays)"),
                  exits ? (sd.lastExit == "left" || sd.lastExit == "right" || sd.lastExit == "top") : (!d.hurtHide && sd.lastExit == "none"));
            Check(k + "it stays inside the view: its brain does not roam", !EliteBrains.Create(d.brain).Roams);
        }
        // the fixed sequence of the first phase, whatever the random seed
        foreach (var x in Expected)
        {
            var d = EliteCatalog.Find(x.key);
            var a = BeatTrace(x.brain, 3, 5);
            var b = BeatTrace(x.brain, 999, 5);
            string want = string.Join(",", ExpectedBeats(d, 1, a.Count));
            Check(d.displayName + ": its first beats are its loop, " + string.Join(",", a) + " (script " + want + "), identical on another seed (" + string.Join(",", b) + ")",
                  a.Count >= 4 && string.Join(",", a) == want && string.Join(",", a) == string.Join(",", b));
        }
    }

    // ---- allocations ------------------------------------------------------------------

    static void Allocations()
    {
        Fresh(.1f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        var a = InPlay("fortress", new Vector2(0f, 2.5f));
        var b = InPlay("swooper", new Vector2(-1.5f, 0f));
        var c = InPlay("sniper", new Vector2(1.5f, 0f));
        var d = InPlay("wrecker", new Vector2(-1f, 1.2f));
        a.AttackCooldown = b.AttackCooldown = c.AttackCooldown = d.AttackCooldown = 0f;
        Step(25f, () => pilot.position = new Vector3(Mathf.Sin(Time.frameCount * .01f), -2.5f, 0f));   // warm up: every attack
        d.AttackCooldown = 99f;   // (the hauler's tow builds a real rock: that allocates, as every spawn does)
        Step(4f);
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
