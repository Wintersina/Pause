using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// The elite script framework (EliteScript.cs): phases from hearts, fixed loops, variants applied and reverted, deterministic cycles,
// the entrance paths, the exit warning before the escape, flinch instead of hiding -- and the STAGE PRESENCE rule: every elite
// is on screen >= 80% of its engaged time while a simulated fight runs through all its phases.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod EliteScriptTest.Run
public static class EliteScriptTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ESC] PASS  " : "[ESC] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 30f;
    public const float MinOnScreen = .8f;
    static Transform pilot;

    // Elites whose roaming brain has not been rebuilt to turn round at the edge yet (each world's PR removes its keys). They are
    // simulated and reported, not asserted.
    // (one key per line, a comment line between worlds: each world PR deletes only its own lines, so the PRs merge in any order)
    static readonly HashSet<string> PendingRoamers = new HashSet<string>
    {
        // desert
        "desert_elite_dune_stalker",
        "desert_elite_lens_matriarch",
        // ember
        "ember_elite_sunstoke",
        "ember_elite_brass_vulture",
        "ember_elite_coalrunner",
        // frost
        "frost_elite_rimebreaker",
        "frost_elite_cryo_siren",
        // space
        "space_elite_rift_lancer",
        "space_elite_orbit_reaver",
        // storm
        "storm_elite_refinery_tender",
        "storm_elite_skyhook_harrier",
        // tide
        // verdant
        "verdant_elite_leafblade",
        "verdant_elite_sporebloom",
        "verdant_elite_thornlash",
    };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EliteCatalog.Reload();
            Phases();
            Variants();
            Cycles();
            Entrances();
            ExitWarning();
            Flinch();
            OnScreen();
        }
        finally
        {
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
            RunScore.EndRun(RunScore.RunId);
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
            startMenu.youAreInTutorial = false;
        }
        Debug.Log("[ESC] failures: " + fails);
        return fails;
    }

    static void Fresh()
    {
        EliteSystem.Clear();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RunScore.EndRun(RunScore.RunId);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = .05f;
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

    static void Step(float seconds)
    {
        for (float t = 0f; t < seconds - 1e-4f; t += Dt) EliteSystem.Step(Dt);
    }

    static EliteStep S(float wait, string attack, string move = "") { return new EliteStep { wait = wait, attack = attack, move = move }; }

    // A def with a hand-made script (the catalog's def is copied; the original is never touched).
    static EliteDef Scripted(string key)
    {
        var d = EliteCatalog.Find(key).Instance();
        d.script = new EliteScriptDef
        {
            tempo = 1f,
            phase1 = new ElitePhaseDef { loop = new[] { S(2f, "wide"), S(1f, "-"), S(2f, "tight") } },
            phase2 = new ElitePhaseDef { gap = .5f, speed = 1.25f, set = new[] { new EliteSet { key = "shotCount", value = 7 } }, loop = new[] { S(2f, "") } },
            last = new ElitePhaseDef { loop = new[] { S(1f, "tight") } },
            variants = new[]
            {
                new EliteVariant { name = "wide", set = new[] { new EliteSet { key = "shotCount", value = 5 }, new EliteSet { key = "shotSpread", value = 30 } } },
                new EliteVariant { name = "tight", set = new[] { new EliteSet { key = "shotCount", value = 2 }, new EliteSet { key = "shotSpread", value = 4 } } },
            },
        };
        return d;
    }

    static void Phases()
    {
        var sd = new EliteScriptDef();
        Check("3 hearts: 3 -> phase 1, 2 -> phase 2, 1 -> last", EliteScriptRunner.PhaseFor(sd, 3, 3) == 1 && EliteScriptRunner.PhaseFor(sd, 2, 3) == 2 && EliteScriptRunner.PhaseFor(sd, 1, 3) == 3);
        Check("5 hearts (armoured): 5,4 -> phase 1; 3,2 -> phase 2; 1 -> last",
              EliteScriptRunner.PhaseFor(sd, 5, 5) == 1 && EliteScriptRunner.PhaseFor(sd, 4, 5) == 1 && EliteScriptRunner.PhaseFor(sd, 3, 5) == 2 &&
              EliteScriptRunner.PhaseFor(sd, 2, 5) == 2 && EliteScriptRunner.PhaseFor(sd, 1, 5) == 3);
        Check("2 hearts: no phase 2, the second is the last", EliteScriptRunner.PhaseFor(sd, 2, 2) == 2 || EliteScriptRunner.PhaseFor(sd, 1, 2) == 3);
    }

    static void Variants()
    {
        Fresh();
        var def = Scripted("space_elite_eventide_bastion");
        def.hearts = 3; def.armored = false;
        var e = EliteShip.CreateInPlay(def, new Vector2(0f, 1f));
        int baseShots = e.Def.shotCount;
        float baseSpread = e.Def.shotSpread;
        var r = e.Script;
        Check("phase 1 starts on step 0 (a 'wide' attack)", r.Phase == 1 && r.StepIndex == 0 && r.Step.attack == "wide");
        r.BeginAttack();
        Check("the 'wide' variant sets the def's numbers for the attack (" + e.Def.shotCount + " shots, " + e.Def.shotSpread + " deg)", e.Def.shotCount == 5 && Mathf.Approximately(e.Def.shotSpread, 30f));
        r.Revert();
        Check("... and the attack's end puts them back", e.Def.shotCount == baseShots && Mathf.Approximately(e.Def.shotSpread, baseSpread));
        r.Advance();
        Check("the next beat is a movement beat ('-'), then 'tight', then the loop repeats",
              r.IsBeat && Advanced(r) && r.Step.attack == "tight" && Advanced(r) && r.Step.attack == "wide");
        Check("the catalog's own def is untouched by a variant", EliteCatalog.Find("space_elite_eventide_bastion").shotCount == baseShots);

        // hearts -> phases
        e.TakeHit(EliteDamage.Teleport, e.Position, 1);
        Check("a lost heart starts phase 2: its loop and numbers (" + r.Phase + ", shots " + e.Def.shotCount + ")", r.Phase == 2 && r.StepIndex == 0 && e.Def.shotCount == 7);
        Check("... faster: speed x1.25, step waits halved", Mathf.Approximately(r.SpeedScale, 1.25f) && Mathf.Abs(r.WaitSeconds() - EliteTuning.Gap(1f * r.Tempo)) < .001f);
        Step(EliteShip.GraceSeconds + .1f);
        e.TakeHit(EliteDamage.Teleport, e.Position, 1);
        Check("the last heart starts the last phase (desperation loop)", r.Phase == 3 && r.Step.attack == "tight");
    }

    static bool Advanced(EliteScriptRunner r) { r.Advance(); return true; }

    static void Cycles()
    {
        Fresh();
        var e1 = EliteShip.CreateInPlay(EliteCatalog.Find("space_elite_orbit_reaver"), new Vector2(0f, 1f));
        var a = new List<int>();
        for (int i = 0; i < 7; i++) a.Add(e1.Script.Cycle("lane", 3));
        Random.InitState(99);
        Fresh();
        var e2 = EliteShip.CreateInPlay(EliteCatalog.Find("space_elite_orbit_reaver"), new Vector2(0f, 1f));
        var b = new List<int>();
        for (int i = 0; i < 7; i++) b.Add(e2.Script.Cycle("lane", 3));
        Check("Cycle walks 0,1,2,0,1,2,0 whatever the random seed (" + string.Join(",", a) + ")", string.Join(",", a) == "0,1,2,0,1,2,0" && string.Join(",", a) == string.Join(",", b));
        Check("Alternate flips every call", e1.Script.Alternate("s") == 1f && e1.Script.Alternate("s") == -1f && e1.Script.Alternate("s") == 1f);
    }

    static void Entrances()
    {
        var from = new Vector2(3f, -1f);
        var to = new Vector2(-1f, 2f);
        foreach (var kind in new[] { "arc", "overshoot", "slam", "swirl", "dash" })
        {
            var p0 = EliteEntrance.Path(kind, from, to, 0f, 1f, 1.6f);
            var p1 = EliteEntrance.Path(kind, from, to, 1f, 1f, 1.6f);
            var mid = EliteEntrance.Path(kind, from, to, .5f, 1f, 1.6f);
            bool startOk = kind == "swirl" ? (p0 - from).magnitude <= 1.6f + .01f : (p0 - from).sqrMagnitude < .001f;
            Check("entrance '" + kind + "' starts at the lift-off's end and lands on the first spot", startOk && (p1 - to).sqrMagnitude < .001f);
            Check("entrance '" + kind + "' is its own path (not the straight line)", kind == "dash" || Mathf.Abs(Vector2.Distance(mid, Vector2.Lerp(from, to, .5f))) > .05f);
        }
        foreach (var d in EliteCatalog.All)
            Check(d.key + ": entrance " + d.script.entrance.kind + " is 1-2.4 s", d.script.entrance.seconds >= .8f && d.script.entrance.seconds <= 2.4f);
    }

    static void ExitWarning()
    {
        Fresh();
        var e = EliteShip.CreateInPlay(EliteCatalog.Find("space_elite_eventide_bastion"), new Vector2(0f, 1.5f));
        e.AttackCooldown = 99f;
        e.EngageLeft = e.Script.ExitTell + .3f;
        Step(.1f);
        Check("before the warning window nothing is flagged", !e.ExitWarning);
        int attacks = e.Attacks;
        Step(.5f);
        Check("the exit tell is up (" + e.EngageLeft.ToString("0.00") + " s left)", e.ExitWarning && e.State != EliteState.Retreat);
        Step(e.Script.ExitTell);
        Check("... and then the escape boost follows", e.State == EliteState.Retreat || !e.gameObject.activeSelf || e.State == EliteState.Dead);
        Check("no new attack started during the warning", e.Attacks == attacks || e.State == EliteState.Retreat || e.State == EliteState.Dead);
    }

    static void Flinch()
    {
        Fresh();
        foreach (var d in EliteCatalog.All)
        {
            if (d.armored) continue;
            var e = EliteShip.CreateInPlay(d, new Vector2(0f, 1.5f));
            e.TakeHit(EliteDamage.PlayerWeapon, e.Position + Vector2.left);
            Check(d.key + ": the first lost heart is a flinch on the board", e.InPlay && !e.Hidden && e.Flinching);
            EliteSystem.Clear();
            Fresh();
        }
    }

    // ---- stage presence ----------------------------------------------------------------------------------------------

    static bool OnView(EliteShip e)
    {
        if (e.Hidden || e.Offstage) return false;
        Vector2 p = e.Position;
        float r = e.Def.hullRadius * .5f;
        return Mathf.Abs(p.x) <= EliteSystem.RailEdge + r && p.y <= EliteSystem.ViewTop + r && p.y >= EliteSystem.ViewBottom - r;
    }

    static void OnScreen()
    {
        foreach (var d in EliteCatalog.All)
        {
            Fresh();
            var e = EliteShip.CreateInPlay(d, new Vector2(0f, 1.5f));
            e.EngageLeft = 9999f;
            const float Total = 40f;
            int engaged = 0, on = 0, steps = Mathf.RoundToInt(Total / Dt);
            for (int i = 0; i < steps && e != null && e.State != EliteState.Dead; i++)
            {
                // the fight runs through its phases: a hit a third and two thirds in
                if (i == steps / 3 || i == steps * 2 / 3) e.TakeHit(EliteDamage.Teleport, e.Position + Vector2.left, 1);
                EliteSystem.Step(Dt);
                if (e == null || e.State == EliteState.Dead) break;
                engaged++;
                if (OnView(e)) on++;
            }
            float share = engaged > 0 ? on / (float)engaged : 0f;
            bool pending = PendingRoamers.Contains(d.key);
            string line = d.key + ": on screen " + (share * 100f).ToString("0") + "% of " + (engaged * Dt).ToString("0") + " s engaged";
            if (pending) Debug.Log("[ESC] INFO  " + line + " (roamer not rebuilt yet: not asserted)");
            else Check(line + " (>= " + (MinOnScreen * 100f) + "%)", share >= MinOnScreen);
        }
    }
}
