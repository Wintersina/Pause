using UnityEditor.SceneManagement;
using UnityEngine;

// The combat rework's toughness (ToughEnemies.cs): every ordinary enemy has 2 hearts (orbiting, one pops per hit), every hit from a weapon
// takes exactly one heart, hazards (rocks, mines, field rocks) die to ANY player hit in one hit (no deflect), a repeat hit inside the ignore window does not count,
// the orbiting hearts are capped and cleaned up, elites carry double hearts, the tutorial's enemies stay one-hit.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ToughEnemiesTest.Run
public static class ToughEnemiesTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[TOUGH] PASS  " : "[TOUGH] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EveryEnemyHasTwoHearts();
            HitTakesOneHeart();
            HeartsPopAndCleanUp();
            PoolCap();
            IgnoreWindow();
            TutorialStaysOneHit();
            HazardsDieInOneHit();
            FieldRockOneHit();
            EliteDoubleHearts();
            UltimateOneShots();
        }
        finally
        {
            Toughness.ClockOverride = null;
            Toughness.EditModeWindow = 0f;
            ShipAttackHits.HitFrom = null;
            EnemyHeartsView.ResetLive();
            EliteDef.ScaleOverride = 1;   // (back to the sandbox's value)
        }
        Debug.Log("[TOUGH] failures: " + fails);
        return fails;
    }

    static void FreshScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        cam.aspect = .5625f;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        Toughness.ClockOverride = null;
        Toughness.EditModeWindow = 0f;
        ShipAttackHits.HitFrom = null;
        EnemyHeartsView.ResetLive();
        Toughness.ResetCounters();
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(true, true);
        Random.InitState(77);
    }

    static GameObject Make(EnemyDef def, float x = 0f, float y = 1f, bool tough = true)
    {
        var go = EnemyFactory.Create(def, new Vector3(x, y, 0f), Quaternion.identity, 1f, tough);
        ClearTarget.Ensure(go);
        return go;
    }

    static bool HitOnce(GameObject go, float weight = 1f, Vector3? from = null)
    {
        float budget = 1f;
        ShipAttackHits.HitFrom = from ?? go.transform.position + Vector3.down;
        bool dead = ShipAttackHits.Hit(go, 1, weight, ref budget);
        ShipAttackHits.HitFrom = null;
        return dead;
    }

    // ---- checks ---------------------------------------------------------------

    static void EveryEnemyHasTwoHearts()
    {
        FreshScene();
        int checkedDefs = 0;
        bool hearts = true, view = true, rocks = true;
        string bad = "";
        foreach (var def in EnemyRoster.All)
        {
            var go = Make(def);
            if (def.IsHazard)
            {
                rocks &= go.GetComponent<EnemyHearts>() == null && go.GetComponent<EnemyHeartsView>() == null;
            }
            else
            {
                var h = go.GetComponent<EnemyHearts>();
                bool ok = h != null && h.Max == 2 && h.Hearts == 2 && Toughness.HeartsPerEnemy == 2;
                if (!ok) bad += def.key + " ";
                hearts &= ok;
                view &= h != null && h.View != null && h.View.ShownCount == h.Hearts;
                checkedDefs++;
            }
            Object.DestroyImmediate(go);
        }
        Check("every ordinary enemy of every world has 2 hearts (" + checkedDefs + " defs; bad: " + bad + ")", checkedDefs > 20 && hearts);
        Check("... and two hearts orbit it (as many shown as it has)", view);
        Check("rocks and mines carry no hearts at all (one hit kills them)", rocks);
        Check("the normal enemies' hearts are 1.6-1.8x the old .11 (" + EnemyHeartsView.Size + ") and sit further out",
              EnemyHeartsView.Size >= .11f * 1.6f && EnemyHeartsView.Size <= .11f * 1.8f && EnemyHeartsView.Clearance > .04f);
        Check("(the hearts' drawings are all cleaned up with the bodies: " + EnemyHeartsView.Live + " live)", EnemyHeartsView.Live == 0);
    }

    static void HitTakesOneHeart()
    {
        FreshScene();
        var def = EnemyRoster.One(0, EnemyRole.Fighter);
        // however heavy the hit, ONE heart: a whole attack hit, a fractional tick, a very heavy one
        foreach (float w in new[] { 1f, .5f, 3f })
        {
            var go = Make(def);
            var h = go.GetComponent<EnemyHearts>();
            int kills = ShipAttackHits.Kills;
            bool dead = HitOnce(go, w);
            Check("an attack hit of weight " + w + " takes exactly 1 heart and the enemy lives (hearts " + h.Hearts + ")", !dead && h.Hearts == 1 && go != null && ShipAttackHits.Kills == kills);
            dead = HitOnce(go, w);
            Check("... the second hit kills it (one kill counted)", dead && go == null && ShipAttackHits.Kills == kills + 1);
            if (go != null) Object.DestroyImmediate(go);
        }
        // one attack hit plus one bullet-sized hit also finishes it
        var a = Make(def);
        HitOnce(a, 1f);
        Check("one attack hit and one more hit of any weight is enough", HitOnce(a, .25f) && a == null);
        // a bare body (no hearts: a test target, a boss part) still dies in one hit
        var bare = new GameObject("bare");
        bare.tag = "Enimey";
        ClearTarget.Ensure(bare);
        Check("a body without hearts still dies to one hit", HitOnce(bare, 1f) && bare == null);
    }

    static void HeartsPopAndCleanUp()
    {
        FreshScene();
        var go = Make(EnemyRoster.One(0, EnemyRole.Alien));
        var h = go.GetComponent<EnemyHearts>();
        var view = h.View;
        Check("(it wears 2 orbiting hearts)", view != null && view.ShownCount == 2);
        int hurts = Toughness.Hurts;
        HitOnce(go);
        Check("a hit pops one heart: one left orbiting, a burst plays, the hit is counted",
              view.ShownCount == 1 && view.ActiveBreaks >= 1 && Toughness.Hurts == hurts + 1);
        int live = EnemyHeartsView.Live;
        var body = go;
        HitOnce(go);
        Check("the killing hit removes the body and its hearts (live views " + live + " -> " + EnemyHeartsView.Live + ")", body == null && EnemyHeartsView.Live == live - 1);
    }

    static void PoolCap()
    {
        FreshScene();
        var def = EnemyRoster.One(0, EnemyRole.Fighter);
        var all = new GameObject[Toughness.MaxHeartViews + 6];
        int withView = 0, withHearts = 0;
        for (int i = 0; i < all.Length; i++)
        {
            all[i] = Make(def, -2f + i * .3f, 2f);
            var h = all[i].GetComponent<EnemyHearts>();
            if (h != null && h.Hearts == 2) withHearts++;
            if (h != null && h.View != null) withView++;
        }
        Check("the orbiting hearts are capped (" + withView + " views for " + all.Length + " enemies, cap " + Toughness.MaxHeartViews + ")",
              withView == Toughness.MaxHeartViews && EnemyHeartsView.Live == Toughness.MaxHeartViews);
        Check("... every enemy still has its 2 hearts (the cap only skips the drawing)", withHearts == all.Length);
        var capped = all[all.Length - 1].GetComponent<EnemyHearts>();
        HitOnce(all[all.Length - 1]);
        Check("... and an enemy without a view still loses a heart per hit", capped.Hearts == 1);
        foreach (var go in all) if (go != null) Object.DestroyImmediate(go);
        Check("destroying them frees the views", EnemyHeartsView.Live == 0);
    }

    static void IgnoreWindow()
    {
        FreshScene();
        float clock = 10f;
        Toughness.ClockOverride = () => clock;
        Toughness.EditModeWindow = Toughness.HitWindow;
        var go = Make(EnemyRoster.One(0, EnemyRole.Fighter));
        var h = go.GetComponent<EnemyHearts>();
        HitOnce(go);
        int ignored = Toughness.Ignored;
        clock += .05f;
        bool dead = HitOnce(go);
        Check("a second hit 0.05 s after the first is ignored (hearts " + h.Hearts + ", ignored +" + (Toughness.Ignored - ignored) + ")", !dead && h.Hearts == 1 && Toughness.Ignored == ignored + 1);
        clock += .06f;
        dead = HitOnce(go);
        Check("a hit past the 0.1 s window counts and kills", dead && go == null);

        var rock = Make(EnemyRoster.One(0, EnemyRole.Rock));
        Check("a hazard ignores no hit: the first one destroys it, window or not", HitOnce(rock) && rock == null);
        Toughness.ClockOverride = null;
        Toughness.EditModeWindow = 0f;
    }

    static void TutorialStaysOneHit()
    {
        FreshScene();
        var go = Make(EnemyRoster.One(0, EnemyRole.Alien), 0f, 1f, false);
        Check("a tutorial enemy (tough: false) has no hearts", go.GetComponent<EnemyHearts>() == null);
        Check("... and dies to the first hit", HitOnce(go) && go == null);
    }

    static void HazardsDieInOneHit()
    {
        FreshScene();
        foreach (var role in new[] { EnemyRole.Rock, EnemyRole.Mine })
        {
            foreach (float w in new[] { 1f, .5f, .25f })
            {
                var hz = Make(EnemyRoster.One(0, role), role == EnemyRole.Mine ? 1f : 0f, 1f);
                int kills = ShipAttackHits.Kills;
                bool dead = HitOnce(hz, w, hz.transform.position + new Vector3(-.3f, -1f, 0f));
                Check(role + ": one hit of weight " + w + " destroys it (the ordinary kill, once)", dead && hz == null && ShipAttackHits.Kills == kills + 1);
            }
        }
    }

    static void FieldRockOneHit()
    {
        FreshScene();
        for (int tier = 0; tier < 3; tier++)
        {
            var rock = SpaceAsteroidField.Spawn(tier, 0, new Vector2(0f, 1f), 0f, 0f, 20f);
            Check("(a tier " + tier + " field rock exists)", rock != null && !rock.Broken);
            if (rock == null) continue;
            rock.TakeShipAttack(1, tier == 0 ? .25f : 1f, rock.transform.position + new Vector3(-.4f, -1f, 0f));
            Check("tier " + tier + ": one player hit breaks it, whatever its size or the hit's weight", rock == null || rock.Broken);
        }
    }

    static void EliteDoubleHearts()
    {
        FreshScene();
        EliteDef.ScaleOverride = 0;   // the real rule (the sandbox runs the other suites at x1)
        var plain = new EliteDef { hearts = 3, armored = false };
        var armoured = new EliteDef { hearts = 5, armored = true };
        Check("an elite's health is doubled: 3 authored hearts -> 6, an armoured 5 -> 10",
              EliteDef.HealthScale == 2 && plain.MaxHearts == 6 && armoured.MaxHearts == 10);
        EliteSystem.Clear();
        var pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -2.5f, 0f);
        EliteSystem.PlayerOverride = pilot;
        var def = BaseEliteDefs.For("gunship");
        var e = EliteShip.CreateInPlay(def, new Vector2(0f, 1f));
        e.AttackCooldown = 99f;
        Check("a live elite starts with the doubled hearts (" + e.Hearts + " of authored " + def.hearts + ")", e.Hearts == def.hearts * 2 && e.Hearts == def.MaxHearts);
        Check("... its fight script still reads the authored count (script hearts " + e.ScriptHearts + ")", e.ScriptHearts == def.hearts);
        e.TakeHit(EliteDamage.PlayerWeapon, e.transform.position);
        Check("... and one hit takes one real heart (script hearts unchanged until a whole authored heart is gone)", e.Hearts == def.hearts * 2 - 1 && e.ScriptHearts == def.hearts);
        EliteSystem.Clear();
        EliteSystem.PlayerOverride = null;
        Object.DestroyImmediate(pilot.gameObject);
    }

    // An ultimate hit (HitKind.Ultimate) kills a 2-heart enemy outright, paid once; a normal hit takes one heart; an elite still loses one heart.
    static void UltimateOneShots()
    {
        FreshScene();
        var def = EnemyRoster.One(0, EnemyRole.Fighter);
        var go = Make(def);
        int kills = ShipAttackHits.Kills;
        float budget = 1f;
        bool dead = ShipAttackHits.Hit(go, 1, 1f, ref budget, HitKind.Ultimate);
        Check("an ultimate hit kills a fresh 2-heart enemy in one hit, paid once (kills +" + (ShipAttackHits.Kills - kills) + ")",
              dead && ShipAttackHits.Kills == kills + 1 && (go == null || !go.GetComponent<ClearTarget>().enabled));
        Object.DestroyImmediate(go);

        var wounded = Make(def);
        HitOnce(wounded);   // one heart gone, one left
        kills = ShipAttackHits.Kills;
        budget = 1f;
        dead = ShipAttackHits.Hit(wounded, 1, 1f, ref budget, HitKind.Ultimate);
        Check("an ultimate finishes a wounded enemy too, once", dead && ShipAttackHits.Kills == kills + 1);
        Object.DestroyImmediate(wounded);

        var plain = Make(def);
        kills = ShipAttackHits.Kills;
        bool normalDead = HitOnce(plain);
        var hearts = plain.GetComponent<EnemyHearts>();
        Check("a normal special-attack hit still takes exactly one heart", !normalDead && hearts.Hearts == 1 && ShipAttackHits.Kills == kills);
        Object.DestroyImmediate(plain);

        EliteDef.ScaleOverride = 0;
        EliteSystem.Clear();
        var pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -2.5f, 0f);
        EliteSystem.PlayerOverride = pilot;
        var edef = BaseEliteDefs.For("gunship");
        var e = EliteShip.CreateInPlay(edef, new Vector2(0f, 1f));
        e.AttackCooldown = 99f;
        int before = e.Hearts;
        budget = 1f;
        ShipAttackHits.Hit(e.gameObject, 1, 1f, ref budget, HitKind.Ultimate);
        Check("an elite still loses exactly one heart to an ultimate hit (" + before + " -> " + e.Hearts + ")", e.Hearts == before - 1);
        EliteSystem.Clear();
        EliteSystem.PlayerOverride = null;
        Object.DestroyImmediate(pilot.gameObject);
        EliteDef.ScaleOverride = 1;
    }
}
