using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// THE SHIELD BURST'S RAIL CRASHES (ShieldShockwave + ShoveCrash.RailCrash): the
// burst throws bodies much harder than it did, and one flung at a side rail
// fast enough can crash there and die (a roll weighted by the impact speed).
//
//   - the burst displaces a body far more than the old 1.0 u maximum
//   - impact speed / chance numbers: nothing under the minimum speed, a
//     certain kill over the sure speed, in between a share; heavy things take more
//   - an impact the roll allows kills (paid as the pilot's crash kill: score,
//     counters, no death combo); the roll refusing it leaves a body grinding
//     along the rail, alive
//   - a heavy body whose impact is under the threshold never dies
//   - at most RailMaxKillsPerWave a release
//   - elites are kicked hard but are never killed by a full-heart rail hit;
//     bosses are not moved or killed
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ShieldRailCrashTest.Run
public static class ShieldRailCrashTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[RAIL] PASS  " : "[RAIL] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;
    const float HullHalf = .29f;
    static readonly Vector2 Ship = new Vector2(0f, -3f);
    static readonly List<GameObject> made = new List<GameObject>();
    static Transform pilot;
    const float OldRadialPush = 1.0f, OldEliteKick = 3.2f, OldEliteResist = .25f;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
            Debug.Log("[RAIL] RadialPush " + ShieldShockwave.RadialPush + " (was " + OldRadialPush + "), EliteKick " + ShieldShockwave.EliteKick +
                      " x resist " + ShieldShockwave.EliteResist + " (was " + OldEliteKick + " x " + OldEliteResist + "), rail speeds min " +
                      ShoveCrash.RailMinSpeed + " / sure " + ShoveCrash.RailSureSpeed + " u/s, max chance " + ShoveCrash.RailMaxChance);
            FartherThanBefore();
            ImpactNumbers();
            AboveThresholdKills();
            BelowThresholdDoesNot();
            CapPerRelease();
            ElitesAndBosses();
        }
        finally
        {
            ShoveCrash.RollOverride = null;
            ShoveCrash.Clear();
            ShoveCrash.Enabled = true;
            EnemyShove.Clear();
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
            ShieldShockwave.Clock = null;
            Clear();
            buttonClicks.playerDied = false;
        }
        Debug.Log("[RAIL] failures: " + fails);
        return fails;
    }

    static void Clear()
    {
        foreach (var e in Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None)) Object.DestroyImmediate(e.gameObject);
        foreach (var go in made) if (go != null) Object.DestroyImmediate(go);
        made.Clear();
        pilot = null;
    }

    static void Fresh(bool crashes)
    {
        Clear();
        EliteSystem.Clear();
        EnemyShove.Clear();
        ShoveCrash.Clear();
        ShoveCrash.ResetCounters();
        ShoveCrash.Enabled = crashes;
        ShoveCrash.RollOverride = null;
        ShieldShockwave.ResetGuards();
        EnemyThreat.Reset();
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        startMenu.youAreInTutorial = false;
        moveBackGround.speed = 0f;
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(true, true);
        pilot = new GameObject("~RailPilot").transform;
        pilot.position = new Vector3(Ship.x, Ship.y, 0f);
        made.Add(pilot.gameObject);
        EliteSystem.PlayerOverride = pilot;
    }

    static GameObject Body(Vector2 at, float radius, bool rock = false, float scale = 1f)
    {
        var go = new GameObject("~railBody");
        go.tag = rock ? "Astr" : "Enimey";
        go.transform.position = new Vector3(at.x, at.y, 0f);
        var box = go.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = Vector2.one * radius * 2f;
        ClearTarget.Ensure(go).SetRadius(radius);
        SpawnFootprint.Attach(go, Vector2.one * radius);
        var id = go.AddComponent<EnemyIdentity>();
        id.SetScale(scale);
        made.Add(go);
        return go;
    }

    static GameObject Fighter(Vector2 at) { return Body(at, .2f); }
    static bool Alive(GameObject go) { return go != null && go.activeInHierarchy && go.GetComponent<ClearTarget>().enabled; }
    static float Lane(float half) { return SpawnLane.LaneHalf - half; }

    static void Step(float seconds)
    {
        for (float t = 0f; t < seconds - 1e-4f; t += Dt) { EnemyShove.Step(Dt); EliteSystem.Step(Dt); }
    }

    static int Release() { return ShieldShockwave.Release(Ship, HullHalf); }

    // ---- 1 ---------------------------------------------------------------------

    static void FartherThanBefore()
    {
        Fresh(false);
        var body = Fighter(Ship + new Vector2(.6f, 1f));
        Vector2 start = body.transform.position;
        Release();
        Step(ShieldShockwave.PushSeconds + .1f);
        float moved = ((Vector2)body.transform.position - start).magnitude;
        // (what the old numbers gave this body: RadialPush 1.0 x its falloff)
        float old = OldRadialPush * Mathf.Max(0f, 1f - (((start - Ship).magnitude - .2f) / ShieldShockwave.Radius));
        Check("a body beside the hull is thrown " + moved.ToString("F2") + " u (the old numbers gave " + old.ToString("F2") + ")",
              moved > old * 2.5f && moved > OldRadialPush * 1.5f);
        Check("the radial push is at least 3x the old one (" + ShieldShockwave.RadialPush + " vs " + OldRadialPush + ")",
              ShieldShockwave.RadialPush >= OldRadialPush * 3f);
    }

    // ---- 2 ---------------------------------------------------------------------

    static void ImpactNumbers()
    {
        float lane = Lane(.2f);
        Check("a throw that never reaches the rail has no impact", ShieldShockwave.RailImpact(0f, 1f, lane) == 0f);
        float near = ShieldShockwave.RailImpact(1.8f, 1.3f, lane), far = ShieldShockwave.RailImpact(.4f, 2.4f, lane);
        Check("the nearer the rail the harder it is hit (" + near.ToString("F1") + " vs " + far.ToString("F1") + " u/s) for the same throw",
              ShieldShockwave.RailImpact(1.8f, 2.4f, lane) > far);
        Check("no chance under " + ShoveCrash.RailMinSpeed + " u/s, certain over " + ShoveCrash.RailSureSpeed + " u/s, a share between (" +
              ShoveCrash.RailChance(ShoveCrash.RailMinSpeed, 1) + ", " + ShoveCrash.RailChance(16f, 1).ToString("F2") + ", " +
              ShoveCrash.RailChance(ShoveCrash.RailSureSpeed, 1) + ")",
              ShoveCrash.RailChance(ShoveCrash.RailMinSpeed, 1) == 0f && ShoveCrash.RailChance(ShoveCrash.RailSureSpeed, 1) == 1f &&
              ShoveCrash.RailChance(16f, 1) > 0f && ShoveCrash.RailChance(16f, 1) < 1f);
        Check("a heavy body needs more speed (16 u/s: " + ShoveCrash.RailChance(16f, 1).ToString("F2") + " light, " +
              ShoveCrash.RailChance(16f, 3).ToString("F2") + " heavy)", ShoveCrash.RailChance(16f, 3) < ShoveCrash.RailChance(16f, 1));
        Check("occasional, never a sure thing below the top speed (max " + ShoveCrash.RailMaxChance + ")",
              ShoveCrash.RailChance(ShoveCrash.RailSureSpeed - .1f, 1) <= ShoveCrash.RailMaxChance + 1e-3f);
    }

    // ---- 3 ---------------------------------------------------------------------

    static void AboveThresholdKills()
    {
        // beside the hull: thrown hard at the rail
        Vector2 at = Ship + new Vector2(.7f, .1f);
        Fresh(true);
        var strong = Fighter(at);
        float v = ShieldShockwave.RailImpact(at.x, ThrowX(at), Lane(.2f));
        float chance = ShoveCrash.RailChance(v, 1);
        Debug.Log("[RAIL] beside-the-hull throw: " + ThrowX(at).ToString("F2") + " u sideways, impact " + v.ToString("F1") + " u/s, chance " + chance.ToString("F2"));
        Check("the throw beside the hull is over the minimum speed (" + v.ToString("F1") + " u/s, chance " + chance.ToString("F2") + ")", chance > .2f);

        ShoveCrash.RollOverride = () => chance * .5f;   // the roll passes
        long total = RunScore.Total;
        Release();
        Check("a body marked for the rail is planned at release (" + ShieldShockwave.LastRailDoomed + ")", ShieldShockwave.LastRailDoomed == 1);
        Step(ShieldShockwave.PushSeconds + .15f);
        Check("it crashes on the rail and dies with its normal death (rail kills " + ShoveCrash.RailKills + ")", !Alive(strong) && ShoveCrash.RailKills == 1);
        Check("... paid like any crash kill: score up (" + (RunScore.Total - total) + ")", RunScore.Total > total);

        Fresh(true);
        // (the burst is hard enough that a body beside the hull is a sure kill; a body further out has a share)
        Vector2 farAt = Ship + new Vector2(2.3f, .3f);
        float farChance = 1f;
        for (float fx = 2.2f; fx <= 3.2f && farChance >= .9f; fx += .05f)
        {
            farAt = Ship + new Vector2(fx, .3f);
            farChance = ShoveCrash.RailChance(ShieldShockwave.RailImpact(farAt.x, ThrowX(farAt), Lane(.2f)), 1);
        }
        var spared = Fighter(farAt);
        Check("a body further out has only a share of a chance (" + farChance.ToString("F2") + ")", farChance > 0f && farChance < .95f);
        ShoveCrash.RollOverride = () => Mathf.Min(.999f, farChance + .01f);   // the roll fails
        Release();
        Step(ShieldShockwave.PushSeconds + .15f);
        float x = Mathf.Abs(spared.transform.position.x);
        Check("a roll above the chance leaves it alive, ground against the rail (|x| " + x.ToString("F2") + " of " + Lane(.2f).ToString("F2") + ")",
              Alive(spared) && ShoveCrash.RailKills == 0 && x > Lane(.2f) - .05f);

        // the crash scoring convention matches the shove crash: counters and the kill are the pilot's
        Fresh(false);
        var off = Fighter(at);
        ShoveCrash.RollOverride = () => 0f;
        Release();
        Step(ShieldShockwave.PushSeconds + .15f);
        Check("with shove crashes switched off nothing dies on a rail", Alive(off) && ShoveCrash.RailKills == 0);
    }

    // the sideways part the burst gives a body at `at` (the lane aside)
    static float ThrowX(Vector2 at)
    {
        Vector2 d = at - Ship;
        float k = Mathf.Pow(Mathf.Clamp01(1f - (d.magnitude - .2f) / ShieldShockwave.Radius), ShieldShockwave.RadialFalloff);
        return ShieldShockwave.WallThrow(d.normalized).x * ShieldShockwave.RadialPush * k;
    }

    // ---- 4 ---------------------------------------------------------------------

    static void BelowThresholdDoesNot()
    {
        Fresh(true);
        ShoveCrash.RollOverride = () => 0f;   // the most generous roll there is
        // a big rock (weight 3) beside the hull: same throw, impact under the minimum once its weight counts
        var rock = Body(Ship + new Vector2(-.8f, -.4f), .4f, true, 1.4f);
        float v = ShieldShockwave.RailImpact(-.8f, ThrowX(Ship + new Vector2(-.8f, -.4f)), Lane(.4f));
        float chance = ShoveCrash.RailChance(v, ShoveCrash.Weight(rock));
        Release();
        Step(ShieldShockwave.PushSeconds + .15f);
        Check("a large rock hits the rail at " + v.ToString("F1") + " u/s (chance " + chance.ToString("F2") + ") and, with the best roll, " +
              (chance > 0f ? "may die" : "survives"), chance > 0f || (Alive(rock) && ShoveCrash.RailKills == 0));
        // a throw too short for the rail: never any chance
        Fresh(true);
        ShoveCrash.RollOverride = () => 0f;
        var off = Fighter(Ship + new Vector2(0f, 1.5f));   // in the ship's column: pushed up, not at a rail
        Release();
        Step(ShieldShockwave.PushSeconds + .15f);
        Check("a body thrown up the column never meets a rail (alive, rail kills " + ShoveCrash.RailKills + ")", Alive(off) && ShoveCrash.RailKills == 0);
        // a weak impact: far edge of the radius, pushed a little; under the minimum the roll is never asked
        Fresh(true);
        ShoveCrash.RollOverride = () => 0f;
        var edge = Fighter(Ship + new Vector2(.4f, 2.3f));
        Release();
        Step(ShieldShockwave.PushSeconds + .15f);
        Check("a body at the edge of the burst is shoved but not crashed (alive)", Alive(edge) && ShoveCrash.RailKills == 0);
    }

    // ---- 5 ---------------------------------------------------------------------

    static void CapPerRelease()
    {
        Fresh(true);
        ShoveCrash.RollOverride = () => 0f;
        var all = new List<GameObject>();
        for (int i = 0; i < 6; i++)
        {
            float side = i % 2 == 0 ? 1f : -1f;
            all.Add(Fighter(Ship + new Vector2(side * (.6f + i * .12f), .2f + i * .1f)));
        }
        Release();
        Step(ShieldShockwave.PushSeconds + .2f);
        int dead = 0;
        foreach (var g in all) if (!Alive(g)) dead++;
        Check("six bodies beside the hull, the best roll: " + dead + " crash on the rails, at most " + ShoveCrash.RailMaxKillsPerWave +
              " (rail kills " + ShoveCrash.RailKills + ", crash kills " + ShoveCrash.Kills + ")",
              dead >= 1 && ShoveCrash.RailKills >= 1 && ShoveCrash.RailKills <= ShoveCrash.RailMaxKillsPerWave);
    }

    // ---- 6 ---------------------------------------------------------------------

    static EliteDef AnyElite() { foreach (var d in EliteCatalog.All) if (d.brain == "interceptor" && d.WorldIndex <= 3) return d; return EliteCatalog.All[0]; }

    static void ElitesAndBosses()
    {
        Fresh(true);
        ShoveCrash.RollOverride = () => 0f;
        var def = AnyElite();
        var elite = EliteShip.CreateInPlay(def, Ship + new Vector2(1.2f, .5f));
        elite.AttackCooldown = 99f;
        elite.Velocity = Vector2.zero;
        int hearts = elite.Hearts;
        Release();
        float speed = elite.Velocity.magnitude;
        float oldMax = OldEliteKick * OldEliteResist;
        Check("an elite is kicked at " + speed.ToString("F1") + " u/s (the old kick topped out at " + oldMax.ToString("F1") + ")", speed > oldMax * 2f);
        Step(2f);
        Check("it is never killed by the burst or its rail (state " + elite.State + ", hearts " + elite.Hearts + " of " + hearts + ")",
              elite.State != EliteState.Dead && elite.Hearts >= hearts - 1 && ShoveCrash.RailKills == 0);

        Fresh(true);
        ShoveCrash.RollOverride = () => 0f;
        var boss = Body(Ship + new Vector2(.5f, 1f), .8f);
        boss.AddComponent<BossTarget>();
        Vector3 at = boss.transform.position;
        Release();
        Step(ShieldShockwave.PushSeconds + .2f);
        Check("a boss is not moved or killed by the burst", Alive(boss) && boss.transform.position == at && ShoveCrash.RailKills == 0);
    }
}
