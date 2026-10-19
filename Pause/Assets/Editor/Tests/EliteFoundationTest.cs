using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// Phase 1 of the elite overhaul (shared groundwork): one elite at a time, the
// Space hearts (5 armoured / 3 not), escape (retreat after Def.engageSeconds,
// half the kill's score lost, floored at 0), the thinned board during a fight,
// engines that keep animating on the damaged drawing, and hurt -> hide.
//
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites EliteFoundationTest
public static class EliteFoundationTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[EFD] PASS  " : "[EFD] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 30f;
    static Transform pilot;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EliteCatalog.Reload();
            OneAtATime();
            SpaceHearts();
            Escape();
            EscapeScoreClamp();
            EscapeIntoDock();
            Thinning();
            DamagedEngines();
            HurtHide();
            PauseAttackRule();
        }
        finally
        {
            EliteFight.Reset();
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
            LandingSites.Override = null;
            RunScore.EndRun(RunScore.RunId);
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
            startMenu.youAreInTutorial = false;
        }
        Debug.Log("[EFD] failures: " + fails);
        return fails;
    }

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
        EliteFight.Reset();
        Random.InitState(777);
    }

    static EliteDef Space(string name) { return EliteCatalog.Find("space_elite_" + name); }

    static EliteShip InPlay(EliteDef def, Vector2 at)
    {
        var e = EliteShip.CreateInPlay(def, at);
        e.AttackCooldown = 999f;
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

    static LandingSite Body(Vector2 at, LandingKind kind, int id = 1)
    {
        var anchor = new GameObject("~Body" + id).transform;
        anchor.position = at;
        return new LandingSite { anchor = anchor, local = Vector3.zero, scale = .3f, order = -300, id = id, kind = kind, emerge = true };
    }

    // ---- 1. one at a time ------------------------------------------------------

    static void OneAtATime()
    {
        Fresh();
        Check("at most one elite alive", EliteDirector.MaxAlive == 1);
        var sites = new List<LandingSite> { Body(new Vector2(-1.5f, 3f), LandingKind.Station, 1), Body(new Vector2(1.5f, 3f), LandingKind.Planet, 2), Body(new Vector2(0f, 4f), LandingKind.Asteroid, 3) };
        LandingSites.Override = list => list.AddRange(sites);
        var dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        int made = dir.SpawnGroup(0, 3);
        Check("a group of three asked for: exactly one parks (" + made + ")", made == 1 && EliteDirector.AliveCount == 1);
        Check("a parked elite counts: no second one", dir.SpawnGroup(0, 1) == 0 && EliteShip.Live.Count == 1);
        var e = EliteShip.Live[0];
        e.ForceLiftOff();
        Step(1.2f);
        Check("a launching elite counts too", e.State == EliteState.LiftOff && dir.SpawnGroup(0, 1) == 0);
        Step(3f);
        Check("... and one in play", e.InPlay && dir.SpawnGroup(0, 1) == 0 && EliteShip.Live.Count == 1);

        // the gap timer does not run while it lives, and starts afresh the moment it is gone
        float before = dir.UntilNext;
        for (int i = 0; i < 90; i++) dir.Tick(Dt);
        Check("while it lives the next one is not timed (" + before.ToString("0.0") + " -> " + dir.UntilNext.ToString("0.0") + ")", Mathf.Approximately(before, dir.UntilNext));
        e.TakeHit(EliteDamage.Teleport, e.Position, 99);
        Check("it dies", !Alive(e));
        dir.Tick(Dt);
        Check("then the normal gap runs (" + dir.UntilNext.ToString("0.0") + " s)", dir.UntilNext > EliteDirector.GapSeconds.x * .6f && dir.UntilNext <= EliteDirector.GapSeconds.y);
        // and after an ESCAPE the same
        var f = InPlay(Space("orbit_reaver"), new Vector2(0f, 1f));
        for (int i = 0; i < 10; i++) dir.Tick(Dt);
        f.EngageLeft = 0f;
        Step(20f);
        dir.Tick(Dt);
        Check("after an escape the gap runs afresh too", !Alive(f) && dir.UntilNext > EliteDirector.GapSeconds.x * .6f);
        Object.DestroyImmediate(dir.gameObject);
        LandingSites.Override = null;
    }

    // ---- 2. hearts --------------------------------------------------------------

    static void SpaceHearts()
    {
        Fresh(.05f);
        Check("Eventide Bastion: 5 hearts (armoured)", Space("eventide_bastion").hearts == 5 && Space("eventide_bastion").armored);
        Check("Singularity Hauler: 5 hearts (armoured)", Space("singularity_hauler").hearts == 5 && Space("singularity_hauler").armored);
        Check("Orbit Reaver: 3 hearts", Space("orbit_reaver").hearts == 3 && !Space("orbit_reaver").armored);
        Check("Rift Lancer: 3 hearts", Space("rift_lancer").hearts == 3 && !Space("rift_lancer").armored);
        foreach (var d in EliteCatalog.All)
            if (d.world != "space" && d.world != "frost" && d.world != "tide" && d.world != "ember" && d.world != "verdant") Check(d.key + ": other worlds keep two hearts", d.hearts == 2);
        // five hearts lay out cleanly: shown, orbiting, never on top of each other
        foreach (var name in new[] { "eventide_bastion", "singularity_hauler" })
        {
            var e = InPlay(Space(name), new Vector2(0f, 1f));
            var h = e.GetComponent<EliteHearts>();
            Check(name + ": five hearts shown", h != null && h.Hearts.Length == 5 && h.ShownCount == 5);
            float minGap = float.MaxValue;
            for (int f = 0; f < 30 * 6; f++)
            {
                h.Place(Dt, Dt);
                if (f < 30) continue;   // (let them settle onto the orbit)
                for (int a = 0; a < 5; a++)
                    for (int b = a + 1; b < 5; b++)
                        minGap = Mathf.Min(minGap, ((Vector2)(h.Hearts[a].position - h.Hearts[b].position)).magnitude);
            }
            Check(name + ": the five never overlap (closest " + minGap.ToString("0.000") + " vs size " + h.heartSize.ToString("0.000") + ")", minGap > h.heartSize * .6f);
            e.TakeHit(EliteDamage.PlayerWeapon, e.Position + Vector2.left * .5f);
            Check(name + ": a hit costs one of five", e.Hearts == 4 && h.ShownCount == 4);
        }
    }

    // ---- 3. escape ----------------------------------------------------------------

    static void Escape()
    {
        Fresh(.05f);
        var def = Space("eventide_bastion");
        var e = InPlay(def, new Vector2(0f, 1.5f));
        RunScore.OnElite(Vector3.zero, 300);
        long before = RunScore.Total;
        Check("a fresh elite has its engagement timer (" + e.EngageLeft + ")", Mathf.Approximately(e.EngageLeft, def.engageSeconds) && def.engageSeconds >= 40f);
        var popups = new List<(int, RunScore.Source)>();
        System.Action<int, Vector3, RunScore.Source> cb = (p, at, s) => popups.Add((p, s));
        RunScore.Scored += cb;
        int paid = EliteRewards.Paid, escapes = EliteShip.Escapes, kills = EliteShip.Kills;
        float dustBefore = EliteRewards.LastDust;
        Step(10f);
        Check("it is not retreating early", e.State != EliteState.Retreat && Mathf.Abs(e.EngageLeft - (def.engageSeconds - 10f)) < .2f);
        e.EngageLeft = 0f;
        e.AttackCooldown = 0f;   // (it WANTS to attack: the retreat wins)
        int attacks = e.Attacks;
        bool retreating = false;
        Step(1.2f, () => retreating |= e.State == EliteState.Retreat);
        Check("the timer runs out: it retreats", retreating || !Alive(e));
        Step(20f);
        RunScore.Scored -= cb;
        Check("it left without a death (no kill, no reward, no dust)", !Alive(e) && EliteShip.Escapes == escapes + 1 && EliteShip.Kills == kills &&
              EliteRewards.Paid == paid && Mathf.Approximately(EliteRewards.LastDust, dustBefore));
        Check("it never attacked on the way", e == null || e.Attacks == attacks);
        int half = def.Score / 2;
        Check("the pilot lost half the kill's score (" + before + " -> " + RunScore.Total + ", -" + half + ")", RunScore.Total == before - half && EliteShip.LastEscapeLoss == half);
        Check("a '-N' ESCAPED popup was raised", popups.Contains((-half, RunScore.Source.Escape)));
        Check("it is gone from the live list", EliteShip.Live.Count == 0);
    }

    static void EscapeScoreClamp()
    {
        Fresh(.05f);
        Check("an empty run loses nothing", RunScore.Total == 0 && RunScore.OnEliteEscaped(Vector3.zero, 25) == 0 && RunScore.Total == 0);
        RunScore.OnElite(Vector3.zero, 10);
        long have = RunScore.Total;
        int lost = RunScore.OnEliteEscaped(Vector3.zero, 25);
        Check("10 points, 25 asked: only what it has is lost, never below 0 (" + have + " -> " + RunScore.Total + ")", lost == have && RunScore.Total == 0);
        RunScore.OnElite(Vector3.zero, 100);
        have = RunScore.Total;
        RunScore.OnEliteEscaped(Vector3.zero, 25);
        Check("the score keeps the rest", RunScore.Total == have - 25);
        // through the elite: score 0, escape
        Fresh(.05f);
        var e = InPlay(Space("rift_lancer"), new Vector2(0f, 1.5f));
        e.EngageLeft = 0f;
        Step(20f);
        Check("an escape at score 0 leaves it at 0", !Alive(e) && RunScore.Total == 0 && EliteShip.LastEscapeLoss == 0);
    }

    static void EscapeIntoDock()
    {
        Fresh(.05f);
        var site = Body(new Vector2(2f, 3f), LandingKind.Station, 9);
        var dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        LandingSites.Override = list => list.Add(site);
        var s = dir.Spawn(Space("eventide_bastion"), site, 2.6f);
        s.ForceLiftOff();
        Step(5f);
        Check("it launched into play", s.InPlay);
        int flares = EliteSystem.Fx.DockFlares;
        s.EngageLeft = 0f;
        Vector2 goal = Vector2.zero;
        Step(1.5f, () => { if (s != null && s.State == EliteState.Retreat) goal = s.RetreatGoal; });
        Check("it heads for its dock (goal " + goal + ")", Vector2.Distance(goal, (Vector2)site.anchor.position) < .01f || !Alive(s));
        Step(15f);
        Check("it reaches the dock and is removed, the dock not flaring", !Alive(s) && EliteSystem.Fx.DockFlares == flares);
        // the dock scrolled away: it leaves off the top instead
        var s2 = dir.Spawn(Space("eventide_bastion"), Body(new Vector2(2f, 3f), LandingKind.Station, 10), 2.6f);
        s2.ForceLiftOff();
        Step(5f);
        s2.EngageLeft = 0f;
        Step(.3f);
        s2.Site.anchor.position = new Vector3(2f, EliteSystem.ViewBottom - 3f, 0f);
        Step(.1f);
        Check("the dock gone, the goal is off the top", s2.State != EliteState.Retreat || s2.RetreatGoal.y > EliteSystem.ViewTop);
        Step(20f);
        Check("... and it leaves", !Alive(s2));
        Object.DestroyImmediate(dir.gameObject);
        LandingSites.Override = null;
    }

    // ---- 4. the thinned board ------------------------------------------------------

    static void Thinning()
    {
        Fresh(.05f);
        Check("no fight: the spawners run at full rate", Mathf.Approximately(EliteFight.SpawnScale, 1f));
        EliteFight.ActiveOverride = true;
        EliteFight.Step(Dt);
        Check("a fight: " + EliteFight.ThinScale + " of normal at once", Mathf.Approximately(EliteFight.SpawnScale, EliteFight.ThinScale) && EliteFight.ThinScale > .2f && EliteFight.ThinScale < .5f);
        EliteFight.ActiveOverride = false;
        EliteFight.Step(EliteFight.RampSeconds * .5f);
        float mid = EliteFight.SpawnScale;
        Check("it ramps back (" + mid.ToString("0.00") + " half-way)", mid > EliteFight.ThinScale + .1f && mid < .95f);
        EliteFight.Step(EliteFight.RampSeconds * .5f + .01f);
        Check("normal again after " + EliteFight.RampSeconds + " s", Mathf.Approximately(EliteFight.SpawnScale, 1f));
        EliteFight.Reset();
        // the real thing: a parked elite does not thin the board, one in play does, and its going ramps back
        var sites = new List<LandingSite> { Body(new Vector2(0f, 3f), LandingKind.Planet, 1) };
        LandingSites.Override = list => list.AddRange(sites);
        var dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        dir.SpawnGroup(0, 1);
        var e = EliteShip.Live[0];
        Step(.5f);
        Check("a parked elite leaves the board alone", Mathf.Approximately(EliteFight.SpawnScale, 1f));
        e.ForceLiftOff();
        Step(.6f);
        Check("lifting off: the board thins", e.State == EliteState.LiftOff && Mathf.Approximately(EliteFight.SpawnScale, EliteFight.ThinScale));
        Step(4f);
        Check("in play: still thin", e.InPlay && Mathf.Approximately(EliteFight.SpawnScale, EliteFight.ThinScale));
        e.EngageLeft = 0f;
        for (int w = 0; w < 30 * 8 && Alive(e) && e.State != EliteState.Retreat; w++) Step(Dt);   // (it finishes the attack it is in first)
        Check("retreating: still thin", !Alive(e) || (e.State == EliteState.Retreat && EliteFight.SpawnScale < .4f));
        Step(20f);
        Check("gone: ramping back or back (" + EliteFight.SpawnScale.ToString("0.00") + ")", !Alive(e) && EliteFight.SpawnScale > EliteFight.ThinScale);
        Step(EliteFight.RampSeconds + .2f);
        Check("... normal within the ramp", Mathf.Approximately(EliteFight.SpawnScale, 1f));
        // the spawner reads it (it scales the speed every spawn timer runs at)
        string src = System.IO.File.ReadAllText("Assets/Scripts/Gameplay/Spawning/enmiesOnBoard.cs");
        Check("the enemy / rock spawner reads EliteFight.SpawnScale", src.Contains("dt *= EliteFight.SpawnScale;"));
        Object.DestroyImmediate(dir.gameObject);
        LandingSites.Override = null;
    }

    // ---- 5. damaged engines ---------------------------------------------------------

    static void DamagedEngines()
    {
        Fresh(.05f);
        int worlds = 0, looped = 0;
        foreach (var d in EliteCatalog.All)
        {
            if (d.cells.damaged >= 0 && d.cells.HasDamagedLoop) looped++;
            if (d.cells.damaged < 0 || d.cells.HasDamagedLoop) continue;   // (a damaged burn loop draws every engine in the art: EmberDockTest, SpaceDockTest)
            var e = InPlay(d, new Vector2(0f, 1f));
            Step(.5f);
            e.TakeHit(EliteDamage.Crash, e.Position + Vector2.left * .3f);
            if (!Alive(e)) { Check(d.key + ": survives a hit", false); continue; }
            if (!e.Damaged) { Check(d.key + ": its armour soaked the hit (no heart, nothing to show)", e.Hearts == d.hearts); EliteSystem.Clear(); Fresh(.05f); continue; }
            var plume = e.HullTransform.Find("Plume0").GetComponent<SpriteRenderer>();
            var glow = e.HullTransform.Find("Glow0").GetComponent<SpriteRenderer>();
            if (d.cells.HasDamagedLoop)
            {
                // a damaged BURN LOOP: every engine's flames are drawn in the cells and animate; no procedural plume doubles them
                var shown = new HashSet<int>();
                int fr = 0, plumes = 0, inLoop = 0, same = 0, worstSame = 0, last = -1;
                Step(.5f);
                Step(3f, () =>
                {
                    int cf = e.CurrentFrame;
                    shown.Add(cf); fr++; if (plume.enabled) plumes++;
                    if (System.Array.IndexOf(d.cells.damagedLoop, cf) >= 0) inLoop++;
                    same = cf == last ? same + 1 : 1; last = cf; worstSame = Mathf.Max(worstSame, same);
                });
                bool loopOk = e.Damaged && e.DamagedLoops && plumes == 0 && inLoop == fr && shown.Count == d.cells.damagedLoop.Length && worstSame <= 5;
                Check(d.key + ": [" + shown.Count + "/" + d.cells.damagedLoop.Length + " drawings, " + plumes + " plume frames, longest hold " + worstSame + " frames] damaged: the burn loop animates every engine (no drawing held more than 5 frames, no procedural plume over it)", loopOk);
                worlds++;
                EliteSystem.Clear();
                Fresh(.05f);
                continue;
            }
            var sprites = new HashSet<Sprite>();
            var glowScales = new HashSet<float>();
            var offsets = new HashSet<float>();
            int frames = 0, plumeFrames = 0; bool glowOn = true;
            Step(2f, () =>
            {
                sprites.Add(plume.sprite);
                glowScales.Add(glow.transform.localScale.x);
                offsets.Add(e.HullTransform.localPosition.y);
                frames++; if (plume.enabled) plumeFrames++; glowOn &= glow.enabled;
            });
            bool ok = e.Damaged && e.CurrentFrame == d.cells.damaged && plumeFrames > frames * .8f && glowOn && sprites.Count > 1 && glowScales.Count > 1 && offsets.Count > 1;
            Check(d.key + ": [" + e.Damaged + " " + e.CurrentFrame + "/" + d.cells.damaged + " " + plumeFrames + "/" + frames + " " + glowOn + "] damaged, still held on one cell but the plume flickers (" + sprites.Count + " drawings), the glow pulses (" + glowScales.Count + "), it bobs (" + offsets.Count + ")", ok);
            worlds++;
            EliteSystem.Clear();
            Fresh(.05f);
        }
        Check("every world's elites checked (" + worlds + " + " + looped + " with a damaged burn loop, covered by the dock tests)", worlds + looped >= EliteCatalog.All.Length - 2);
        // (an elite whose damaged hull keeps flames painted in the art but has no damaged burn loop is listed in ElitePolishTest.PaintedFlameDamaged: see ElitePolishTest)
        // an undamaged elite keeps its drawn flames: no procedural plume where exhaustScale is 0
        var h = InPlay(Space("eventide_bastion"), new Vector2(0f, 1f));
        Step(1f);
        Check("undamaged (flames drawn in the cells): no procedural plume", !h.HullTransform.Find("Plume0").GetComponent<SpriteRenderer>().enabled);
    }

    // ---- 6. hurt -> hide ---------------------------------------------------------------

    static void HurtHide()
    {
        Fresh(.05f);
        Check("only the two non-armoured Space elites hide when hurt",
              Space("orbit_reaver").hurtHide && Space("rift_lancer").hurtHide && !Space("eventide_bastion").hurtHide && !Space("singularity_hauler").hurtHide);
        foreach (var d in EliteCatalog.All) if (d.hurtHide) Check(d.key + ": hides only if Space, Frost, Tide, Ember or Verdant and not armoured", (d.world == "space" || d.world == "frost" || d.world == "tide" || d.world == "ember" || d.world == "verdant") && !d.armored);

        // an armoured one just takes the hit
        var tank = InPlay(Space("eventide_bastion"), new Vector2(0f, 1f));
        tank.TakeHit(EliteDamage.PlayerWeapon, tank.Position + Vector2.left);
        Check("armoured: a lost heart, no hiding", tank.State == EliteState.Follow && tank.Hearts == 4);
        EliteSystem.Clear();
        Fresh(.05f);

        foreach (var name in new[] { "orbit_reaver", "rift_lancer" })
        {
            var def = Space(name);
            var e = InPlay(def, new Vector2(0f, 1f));
            e.AttackCooldown = 0f;
            Step(2.5f);
            float engage = e.EngageLeft;
            e.TakeHit(EliteDamage.PlayerWeapon, e.Position + Vector2.left * .4f);
            Check(name + ": a lost heart (" + e.Hearts + "/" + def.hearts + ") sends it hiding", e.State == EliteState.Hide && e.Hidden && e.Hearts == def.hearts - 1);
            Check(name + ": not InPlay, collider and hazard tag off", !e.InPlay && !e.Collider.enabled && e.tag == "Untagged");
            int exitSide = e.HideSide;
            int hearts = e.Hearts, attacks = e.Attacks, shots = EliteSystem.Shots.Launched;
            Check(name + ": not shootable while away", !e.TakeHit(EliteDamage.PlayerWeapon, e.Position) && e.Hearts == hearts);
            e.TakeShipAttack(0, 1f, e.Position);
            Check(name + ": ... by a player weapon either", e.Hearts == hearts);
            // away: off the view, waiting
            bool firedAway = false, offscreen = false, waited = false, hittable = false, escapeTimerRan = false;
            float eng0 = e.EngageLeft;
            int waitSide = -1;
            Step(def.hideSeconds + 6f, () =>
            {
                if (!e.Hidden) return;
                firedAway |= e.Attacks != attacks || EliteSystem.Shots.Launched != shots;
                Vector2 p = e.Position;
                bool off = Mathf.Abs(p.x) > EliteSystem.ViewHalfWidth + .2f || p.y > EliteSystem.ViewTop + .2f;
                offscreen |= off;
                if (e.HideLeft > 0f && off) { waited = true; waitSide = e.HideSide; }
                hittable |= e.InPlay || e.Collider.enabled;
                escapeTimerRan |= !Mathf.Approximately(e.EngageLeft, eng0);
            });
            Check(name + ": it flew off-screen and waited there", offscreen && waited);
            Check(name + ": it never became hittable while away", !hittable);
            Check(name + ": the escape timer paused while it was hidden (" + eng0.ToString("0.00") + ")", !escapeTimerRan && Mathf.Approximately(eng0, engage));
            Check(name + ": nothing was fired while away ", !firedAway);
            Check(name + ": it came back, hittable, on a different side (" + exitSide + " -> " + e.HideSide + ")",
                  e.InPlay && e.State == EliteState.Follow && (e.Offstage || (e.Collider.enabled && e.tag == "Enimey")) && e.HideSide != exitSide);
            float after = e.EngageLeft;
            Step(1f);
            Check(name + ": ... " + after.ToString("0.0") + " -> " + e.EngageLeft.ToString("0.0"), e.EngageLeft < after - .5f);
            Check(name + ": and it is shootable again", e.TakeShipAttack_Test());
            EliteSystem.Clear();
            Fresh(.05f);
        }
        // hurt twice: hides twice, dies on the last
        var r = InPlay(Space("orbit_reaver"), new Vector2(0f, 1f));
        r.TakeHit(EliteDamage.PlayerWeapon, r.Position + Vector2.left);
        Step(14f);
        Check("it returned after the first hide", r.InPlay);
        r.TakeHit(EliteDamage.PlayerWeapon, r.Position + Vector2.left);
        Check("hurt again (2 of 3): hides again", r.Hidden && r.Hearts == 1);
        Step(14f);
        r.TakeHit(EliteDamage.PlayerWeapon, r.Position + Vector2.left);
        Check("the last heart kills it (no hiding)", !Alive(r));
    }

    static bool TakeShipAttack_Test(this EliteShip e)
    {
        int h = e.Hearts;
        e.TakeShipAttack(0, 1f, e.Position);
        return !Alive(e) || e.Hearts < h || e.Hidden;
    }

    // ---- 2b. the pause attack -----------------------------------------------------------

    // Where it lives: EliteShip.TeleportStrike (from TeleportFx.Strike) -> TakeHit(Teleport, at, PauseDamage): a pause jump deals 2
    // hearts whatever the elite has (a 5-heart one is left on 3).
    static void PauseAttackRule()
    {
        Fresh(.05f);
        var e = InPlay(Space("eventide_bastion"), new Vector2(0f, 1f));
        Check("the pause jump on a 5-heart elite", e.Hearts == 5);
        EliteShip.TeleportStrike(e.gameObject, e.Position);
        Check("... takes 2 hearts (" + e.Hearts + " left)", Alive(e) && e.Hearts == 3 && EliteShip.PauseDamage == 2);
        EliteShip.TeleportStrike(e.gameObject, e.Position);
        EliteShip.TeleportStrike(e.gameObject, e.Position);
        Check("... and three of them kill it", !Alive(e));
    }

    // The launch tell is ONE small blinking light: for every world with elites, on a hidden-in-body site (emerge) and on a plain
    // ground pad, nothing is emitted (no ring, flare, halo, glow burst) through the whole parked time and the lift-off, and the
    // launch light shows (blinking) in the last parked second only.
    static void LaunchTellIsOneLight()
    {
        for (int w = 0; w <= 6; w++)
        {
            var defs = new List<EliteDef>();
            if (EliteCatalog.ForWorld(w, defs) == 0) continue;
            foreach (bool emerge in new[] { true, false })
            {
                Fresh(.05f);
                var def = defs[0];
                string k = "world " + w + " " + def.key + (emerge ? " (out of a body)" : " (off a pad)") + ": ";
                var anchor = new GameObject("~Pad").transform;
                anchor.position = new Vector3(.8f, 3f, 0f);
                var site = new LandingSite { anchor = anchor, local = Vector3.zero, scale = .3f, order = -300, id = 1,
                                             kind = emerge ? LandingKind.Station : LandingKind.PadRing, emerge = emerge };
                int spawned = EliteSystem.Fx.Spawned, flares = EliteSystem.Fx.DockFlares;
                var e = EliteShip.Create(def, site, 2.5f, new Vector2(-1.4f, 1.4f));
                int early = 0, on = 0, off = 0, guard = 0, spawnedInPark = 0;
                while (e.State == EliteState.Parked && guard++ < 400)
                {
                    EliteSystem.Step(Dt);
                    if (e.State != EliteState.Parked) break;
                    spawnedInPark = EliteSystem.Fx.Spawned - spawned;
                    bool tell = e.StateTime >= e.ParkSeconds - EliteShip.EngineTellSeconds + .02f;
                    if (!tell) { if (e.LaunchLightOn) early++; }
                    else if (e.LaunchLightOn) on++; else off++;
                }
                Check(k + "no ring / flare / halo spawned while parked (" + spawnedInPark + ")", spawnedInPark == 0 && EliteSystem.Fx.DockFlares == flares);
                Check(k + "a launch blink light shows in the tell only, blinking (on " + on + ", off " + off + ", early " + early + ")", on > 0 && off > 0 && early == 0);
                var light = e.transform.Find("~launch_light");
                Check(k + "it is one small light (a few pixels, tight glow)", light != null &&
                      light.GetComponent<SpriteRenderer>().bounds.size.x <= EliteLaunchTell.CoreSize * 1.2f &&
                      light.GetChild(0).GetComponent<SpriteRenderer>().bounds.size.x <= EliteLaunchTell.GlowSize * 1.2f);
                int dockFlares = EliteSystem.Fx.DockFlares;
                for (int i = 0; i < 12 && e.State == EliteState.LiftOff; i++) EliteSystem.Step(Dt);
                Check(k + "the lift-off has no flare and the light is out", EliteSystem.Fx.DockFlares == dockFlares && !e.LaunchLightOn);
                EliteSystem.Clear();
            }
        }
        Check("the tell blinks at 4 Hz, quicker in its last half second",
              EliteLaunchTell.On(.1f) && !EliteLaunchTell.On(.3f) && EliteLaunchTell.On(.55f) && !EliteLaunchTell.On(.7f) && EliteLaunchTell.On(.85f) && !EliteLaunchTell.On(-.1f));
    }
}
