using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// Feature: every ship has its own main attack and its own secret power
// (ShipLoadoutTable). Only the top price tier fires the cinematic
// auto-target volley; the rest fire directional / limited weapons at normal
// speed (ShipAttackRunner). The secret power has its own meter, filled by
// star dust and kills, and fires itself on its trigger (SecretPowerController).
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ShipAttacksTest.Run
public static class ShipAttacksTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SA] PASS  " : "[SA] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        EveryShipHasADistinctLoadout();
        OnlyTheTopPriceTierClearsTheScreen();
        RailHitsOnlyItsForwardLine();
        SpreadCountsAreRespected();
        RicochetHomingAndChainCountsAreRespected();
        OrbitDiscLastsItsDuration();
        SecretMeterFillsAndTriggers();
        CloakTriggersJustBeforeAHit();
        NothingAdvancesWhilePaused();
        PoolsStayBounded();
        BossTakesWeightedHits();
        MeterKeepsClearOfShipUi();

        AttackPool.StopAll();
        WorldTimeFx.Reset();
        Debug.Log("[SA] failures: " + fails);
        return fails;
    }

    // ---- fixtures ------------------------------------------------------

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
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        collisionDetection.cloakTimer = 0f;
        collisionDetection.atomCheck = false;
        collisionDetection.lifeCounter = 0;
        moveBackGround.speed = .2f;
        Time.timeScale = 1f;
        WorldTimeFx.Reset();
    }

    static GameObject Hazard(float x, float y, string tag = "Enimey")
    {
        var go = new GameObject("hazard");
        go.tag = tag;
        go.transform.position = new Vector3(x, y, 0f);
        ClearTarget.Ensure(go).SetRadius(.3f);
        return go;
    }

    static ShipPowerController Ship(int id)
    {
        PlayerPrefs.SetInt("spawnShip", id);
        var go = new GameObject("ship" + id, typeof(SpriteRenderer));
        go.transform.position = new Vector3(0f, -3f, 0f);
        var c = go.AddComponent<ShipPowerController>();
        c.SendMessage("Awake");
        c.SendMessage("Start");
        var gun = go.GetComponentInChildren<UltimateGun>();
        if (gun != null) gun.SendMessage("Awake");
        return c;
    }

    static void Teardown(ShipPowerController c)
    {
        if (c == null) return;
        c.SendMessage("OnDestroy");
        if (c.Runner != null) c.Runner.SendMessage("OnDestroy");
        if (c.Secret != null) c.Secret.SendMessage("OnDestroy");
        Object.DestroyImmediate(c.gameObject);
        AttackPool.StopAll();
        collisionDetection.cloakTimer = 0f;
        collisionDetection.lifeCounter = 0;
    }

    static void Steps(ShipAttackRunner r, float seconds, float dt = .02f)
    {
        for (float t = 0f; t < seconds; t += dt) r.Step(dt);
    }

    static bool Alive(GameObject go) => go != null;

    // ---- cases -----------------------------------------------------------

    static void EveryShipHasADistinctLoadout()
    {
        var pairs = new HashSet<string>();
        var powers = new HashSet<SecretPower>();
        bool all = true, distinct = true, named = true;
        foreach (int id in ShipId.All)
        {
            if (!ShipLoadoutTable.Has(id) || ShipLoadoutTable.For(id).shipId != id) { all = false; continue; }
            var l = ShipLoadoutTable.For(id);
            distinct &= pairs.Add(l.attack + "+" + l.power);
            powers.Add(l.power);
            named &= !string.IsNullOrEmpty(l.attackName) && !string.IsNullOrEmpty(l.powerName) &&
                     !string.IsNullOrEmpty(l.attackLine) && !string.IsNullOrEmpty(l.powerLine);
        }
        Check("every ShipId has an attack and a secret power", all && ShipId.Count == 15);
        Check("no two ships share the same attack + power pair", distinct);
        Check("every ship's secret power is its own", powers.Count == ShipId.Count);
        Check("every attack and power has a name and a one-line description", named);
        Check("a stray id falls back to the starter's loadout",
              ShipLoadoutTable.For(999).shipId == ShipId.Starter && ShipLoadoutTable.For(0).shipId == ShipId.Starter);

        var nonVolley = new HashSet<ShipAttack>();
        int below = 0;
        foreach (int id in ShipId.All)
            if (!ShipLoadoutTable.For(id).IsTopTier) { below++; nonVolley.Add(ShipLoadoutTable.For(id).attack); }
        Check("every ship below the top tier attacks its own way", nonVolley.Count == below);

        Check("the codex lists each ship's attack and secret power",
              CodexCatalogue.LoadoutLore(4).Contains("RAIL GUN") && CodexCatalogue.LoadoutLore(4).Contains("CAPACITOR DUMP"));
    }

    static void OnlyTheTopPriceTierClearsTheScreen()
    {
        var byPrice = new List<int>(ShipId.All);
        byPrice.Sort((a, b) => shopingShips.CostFor(b).CompareTo(shopingShips.CostFor(a)));
        var top = new HashSet<int> { byPrice[0], byPrice[1], byPrice[2] };
        bool match = true;
        int count = 0;
        foreach (int id in ShipId.All)
        {
            bool clears = ShipLoadoutTable.For(id).attack == ShipAttack.ScreenClear;
            if (clears) count++;
            match &= clears == top.Contains(id) && clears == ShipLoadoutTable.IsTopTierByPrice(id);
        }
        Check("exactly the three priciest hulls use the screen clear (" + string.Join(",", top) + ")",
              match && count == ShipLoadoutTable.TopTierCount);

        FreshScene();
        var h = Hazard(0f, 1f);
        var cheap = Ship(4);
        typeof(ShipPowerController).GetMethod("Fire", Inst).Invoke(cheap, null);
        Check("a cheaper ship's attack plays at normal speed (no cinematic)",
              !ShipPowerController.CinematicClearActive && cheap.Runner.FireCount == 1);
        Teardown(cheap);
        if (h != null) Object.DestroyImmediate(h);

        FreshScene();
        Hazard(0f, 1f);
        var rich = Ship(byPrice[0]);
        typeof(ShipPowerController).GetMethod("Fire", Inst).Invoke(rich, null);
        Check("a top-tier ship fires the cinematic volley", ShipPowerController.CinematicClearActive && rich.Runner.FireCount == 0);
        Teardown(rich);
    }

    static void RailHitsOnlyItsForwardLine()
    {
        FreshScene();
        var c = Ship(4);
        var inLine = Hazard(.1f, -1f);
        var farInLine = Hazard(-.15f, 3.5f, "Astr");
        var offLine = Hazard(1.4f, 0f);
        var behind = Hazard(0f, -4.6f);
        int kills = ShipAttackHits.Kills;
        c.Runner.Fire();
        Check("the rail hits everything in its line, near and far", !Alive(inLine) && !Alive(farInLine));
        Check("the rail misses what is beside its line", Alive(offLine));
        Check("the rail misses what is behind the ship", Alive(behind));
        Check("two kills counted", ShipAttackHits.Kills - kills == 2);
        Check("a rail kill adds a brief hit-stop", WorldTimeFx.HitStopping && WorldTimeFx.Scale < 1f);
        WorldTimeFx.Reset();
        Teardown(c);
        Object.DestroyImmediate(offLine);
        Object.DestroyImmediate(behind);
    }

    static void SpreadCountsAreRespected()
    {
        FreshScene();
        var dove = Ship(14);
        int before = AttackProjectile.LaunchCount;
        dove.Runner.Fire();
        Check("the feather fan launches 5 darts at once", AttackProjectile.LaunchCount - before == 5);
        float minX = float.MaxValue, maxX = float.MinValue;
        Steps(dove.Runner, .2f);
        foreach (var p in AttackPool.Projectiles)
            if (p.Active) { minX = Mathf.Min(minX, p.transform.position.x); maxX = Mathf.Max(maxX, p.transform.position.x); }
        Check("the darts fan out across a forward spread", maxX - minX > 1f && minX < 0f && maxX > 0f);
        Teardown(dove);

        FreshScene();
        var gat = Ship(12);
        before = AttackProjectile.LaunchCount;
        gat.Runner.Fire();
        Check("the gatling doesn't dump its burst in one frame", AttackProjectile.LaunchCount - before < 4);
        Steps(gat.Runner, 1.3f);
        Check("the gatling fires exactly 14 shells", AttackProjectile.LaunchCount - before == 14);
        Teardown(gat);

        FreshScene();
        var comet = Ship(1);
        before = AttackProjectile.LaunchCount;
        comet.Runner.Fire();
        Steps(comet.Runner, .5f);
        Check("the starter's comet fires 2 streaks", AttackProjectile.LaunchCount - before == 2);
        Teardown(comet);

        FreshScene();
        var light = Ship(8);
        var lanes = new[] { Hazard(0f, 1f), Hazard(1.2f, 2f), Hazard(-1.2f, 0f) };
        var between = Hazard(.6f, 1.5f);
        light.Runner.Fire();
        Steps(light.Runner, 1f);
        Check("sky javelins strike three lanes", !Alive(lanes[0]) && !Alive(lanes[1]) && !Alive(lanes[2]));
        Check("and nothing between the lanes", Alive(between));
        Teardown(light);
        Object.DestroyImmediate(between);
    }

    static void RicochetHomingAndChainCountsAreRespected()
    {
        FreshScene();
        var ninja = Ship(11);
        var foes = new List<GameObject>();
        for (int i = 0; i < 8; i++) foes.Add(Hazard((i % 2 == 0 ? -.6f : .6f), -1.5f + i * 1.0f));
        int kills = ShipAttackHits.Kills;
        ninja.Runner.Fire();
        Steps(ninja.Runner, 4f);
        Check("the ricochet star stops after its max hits (" + (ShipAttackHits.Kills - kills) + ")",
              ShipAttackHits.Kills - kills == ShipLoadoutTable.For(11).maxHits);
        Teardown(ninja);
        foreach (var f in foes) if (f != null) Object.DestroyImmediate(f);

        FreshScene();
        var paranoid = Ship(10);
        foes.Clear();
        for (int i = 0; i < 6; i++) foes.Add(Hazard(-1.2f + i * .5f, 1f + (i % 3)));
        kills = ShipAttackHits.Kills;
        int before = AttackProjectile.LaunchCount;
        paranoid.Runner.Fire();
        var marks = new HashSet<Transform>();
        foreach (var p in AttackPool.Projectiles) if (p.Active && p.Target != null) marks.Add(p.Target);
        Check("3 seeker eyes, each on its own target", AttackProjectile.LaunchCount - before == 3 && marks.Count == 3);
        Steps(paranoid.Runner, 3f);
        Check("the seekers kill exactly 3 of 6 (" + (ShipAttackHits.Kills - kills) + ")", ShipAttackHits.Kills - kills == 3);
        Teardown(paranoid);
        foreach (var f in foes) if (f != null) Object.DestroyImmediate(f);

        FreshScene();
        var viper = Ship(2);
        foes.Clear();
        for (int i = 0; i < 8; i++) foes.Add(Hazard(-1.2f + (i % 4) * .8f, -1.5f + (i / 4) * .9f));
        kills = ShipAttackHits.Kills;
        viper.Runner.Fire();
        Steps(viper.Runner, 1f);
        Check("chain lightning jumps to at most its max hits (" + (ShipAttackHits.Kills - kills) + ")",
              ShipAttackHits.Kills - kills == ShipLoadoutTable.For(2).maxHits);
        Teardown(viper);
        foreach (var f in foes) if (f != null) Object.DestroyImmediate(f);
    }

    static void OrbitDiscLastsItsDuration()
    {
        FreshScene();
        var turtle = Ship(15);
        float duration = ShipLoadoutTable.For(15).duration;
        var near = Hazard(-1.05f, -3f); // on the orbit, left of the ship
        turtle.Runner.Fire();
        Check("the disc orbit starts", turtle.Runner.ActiveRuns == 1);
        Steps(turtle.Runner, duration - .1f);
        Check("still orbiting just before its duration", turtle.Runner.ActiveRuns == 1);
        Check("it smashed what it touched", !Alive(near));
        Steps(turtle.Runner, .2f);
        Check("gone once its duration is up", turtle.Runner.ActiveRuns == 0);
        Teardown(turtle);
    }

    static void SecretMeterFillsAndTriggers()
    {
        FreshScene();
        var viper = Ship(2);
        var s = viper.Secret;
        Check("the secret meter starts empty", s.Meter == 0f && !s.Ready);
        SecretPowerController.OnDust(false);
        SecretPowerController.OnDust(true);
        Check("star dust fills it", Mathf.Approximately(s.Meter, s.perSmallDust + s.perLargeDust));
        float m = s.Meter;
        var dead = Hazard(2f, 4f);
        collisionDetection.AwardDestroyedTarget(dead);
        Object.DestroyImmediate(dead);
        Check("a kill fills it", Mathf.Approximately(s.Meter, m + s.perKill));
        Check("it fills far slower than one kill", s.perKill < SecretPowerController.Full / 10f);
        s.Step(.1f);
        Check("it never fills by itself", Mathf.Approximately(s.Meter, m + s.perKill));
        s.SetMeter(SecretPowerController.Full);
        Check("full -> ready", s.Ready);

        var foes = new List<GameObject>();
        for (int i = 0; i < 4; i++) foes.Add(Hazard(-1.2f + i * .8f, 3f));
        s.Step(.02f);
        Check("EMP waits while fewer than 5 enemies are on screen", s.FireCount == 0 && s.Ready);
        Check("the badge shows its ready tell", BadgeReady(s));
        foes.Add(Hazard(0f, 1f));
        s.Step(.02f);
        Check("EMP fires itself once 5 enemies are on screen", s.FireCount == 1 && !s.Ready && s.Meter == 0f);
        Check("and stuns them", s.Stunning);
        Teardown(viper);
        foreach (var f in foes) if (f != null) Object.DestroyImmediate(f);

        // EMP actually switches the enemies' movers off, then back on.
        FreshScene();
        viper = Ship(2);
        foes.Clear();
        var movers = new List<moveItemEnmInStrightLine>();
        for (int i = 0; i < 5; i++)
        {
            var f = Hazard(-1.2f + i * .6f, 2f);
            movers.Add(f.AddComponent<moveItemEnmInStrightLine>());
            foes.Add(f);
        }
        viper.Secret.SetMeter(SecretPowerController.Full);
        viper.Secret.Step(.02f);
        bool off = true;
        foreach (var mv in movers) off &= !mv.enabled;
        Check("EMP freezes every enemy on screen", off && viper.Secret.Stunning);
        viper.Secret.Step(viper.Secret.stunSeconds + .1f);
        bool on = true;
        foreach (var mv in movers) on &= mv.enabled;
        Check("and lets them go when it wears off", on && !viper.Secret.Stunning);
        Teardown(viper);
        foreach (var f in foes) if (f != null) Object.DestroyImmediate(f);

        // Mending: waits for damage, then repairs one hull point.
        FreshScene();
        var dove = Ship(14);
        dove.Secret.SetMeter(SecretPowerController.Full);
        dove.Secret.Step(.02f);
        Check("Mending waits while the hull is intact", dove.Secret.FireCount == 0);
        collisionDetection.lifeCounter = 2;
        dove.Secret.Step(.02f);
        Check("Mending repairs one hull point once damaged", dove.Secret.FireCount == 1 && collisionDetection.lifeCounter == 1);
        Teardown(dove);

        // Capacitor Dump: the attack comes straight back.
        FreshScene();
        var halo = Ship(4);
        for (int i = 0; i < 3; i++) foes.Add(Hazard(-1f + i, 4f));
        halo.Secret.SetMeter(SecretPowerController.Full);
        halo.Secret.Step(.02f);
        Check("Capacitor Dump recharges the attack on a busy screen",
              halo.Secret.FireCount == 1 && halo.SecondsLeft <= ChargeIndicator.ReadySeconds);
        Teardown(halo);
        foreach (var f in foes) if (f != null) Object.DestroyImmediate(f);
    }

    static bool BadgeReady(SecretPowerController s)
    {
        if (s.Badge == null) return false;
        s.Badge.Step(.02f);
        return s.Badge.ReadyShowing && s.Badge.ReadyTellCount == 1 &&
               s.Badge.CurrentSprite == ShipFxArt.MeterReady(s.Ship, 0);
    }

    static void CloakTriggersJustBeforeAHit()
    {
        FreshScene();
        var jade = Ship(6);
        var s = jade.Secret;
        s.SetMeter(SecretPowerController.Full);
        var far = Hazard(0f, 0f); // 3 units above, in the lane
        s.Step(.02f);
        Check("Cloak holds while the hazard is still far off", s.FireCount == 0 && !collisionDetection.Cloaked);
        far.transform.position = new Vector3(0f, -1.6f, 0f); // ~0.25 s away at this speed
        s.Step(.02f);
        Check("Cloak triggers just before the hit lands", s.FireCount == 1 && collisionDetection.Cloaked);
        Check("and grants real invulnerability", collisionDetection.Invulnerable &&
              Mathf.Approximately(collisionDetection.cloakTimer, s.cloakSeconds));
        Check("with its phase aura drawn", GameObject.Find(SecretPowerController.CloakAuraName) != null);
        Object.DestroyImmediate(far);

        // The guarantee: a hit arriving with a full meter is intercepted.
        collisionDetection.cloakTimer = 0f;
        var hit = Hazard(0f, -3f);
        Check("an empty meter doesn't save you", !SecretPowerController.InterceptHit(hit));
        s.SetMeter(SecretPowerController.Full);
        Check("a full meter spends itself on the incoming hit", SecretPowerController.InterceptHit(hit) &&
              collisionDetection.Invulnerable && s.FireCount == 2);
        Object.DestroyImmediate(hit);
        Teardown(jade);

        // Hard Shell: up in advance, eats exactly one hit.
        FreshScene();
        var turtle = Ship(15);
        turtle.Secret.SetMeter(SecretPowerController.Full);
        var lane = Hazard(0f, -1f);
        turtle.Secret.Step(.02f);
        Check("Hard Shell goes up when danger enters the lane", turtle.Secret.ShellUp);
        Check("it eats the next hit", SecretPowerController.InterceptHit(lane) && !turtle.Secret.ShellUp);
        Check("and only that one", !SecretPowerController.InterceptHit(lane));
        Object.DestroyImmediate(lane);
        Teardown(turtle);
    }

    static void NothingAdvancesWhilePaused()
    {
        FreshScene();
        var ufo = Ship(13);
        var gat = Ship(12);
        Hazard(0f, 3f);
        ufo.Runner.Fire();
        gat.Runner.Fire();
        Time.timeScale = 0f;
        Check("attacks read zero gameplay time while paused", ShipAttackRunner.ScaledDelta() == 0f);
        var positions = new List<Vector3>();
        foreach (var p in AttackPool.Projectiles) positions.Add(p.transform.position);
        int launched = AttackProjectile.LaunchCount;
        for (int i = 0; i < 30; i++)
        {
            ufo.Runner.SendMessage("Update");
            gat.Runner.SendMessage("Update");
            ufo.Secret.SendMessage("Update");
            foreach (var p in AttackPool.Projectiles) p.Tick(ShipAttackRunner.ScaledDelta());
        }
        bool still = true;
        int k = 0;
        foreach (var p in AttackPool.Projectiles) still &= p.transform.position == positions[k++];
        Check("no projectile moves at timeScale 0", still);
        Check("no new shells while paused", AttackProjectile.LaunchCount == launched);
        Check("the held beam doesn't run out while paused", ufo.Runner.ActiveRuns == 1);

        ufo.Secret.SetMeter(SecretPowerController.Full); // calm screen: Star Shower would fire
        ufo.Secret.SendMessage("Update");
        Check("secret powers don't trigger while paused", ufo.Secret.FireCount == 0);
        float shown = ufo.Secret.Meter;
        SecretPowerController.OnKill();
        Check("(the meter itself only moves on dust and kills)", ufo.Secret.Meter >= shown);
        Time.timeScale = 1f;
        Teardown(ufo);
        Teardown(gat);
    }

    static void PoolsStayBounded()
    {
        FreshScene();
        var gat = Ship(12);
        for (int round = 0; round < 3; round++)
        {
            gat.Runner.Fire();
            Steps(gat.Runner, 2f);
        }
        int settled = AttackPool.ProjectilePoolSize;
        gat.Runner.Fire();
        Steps(gat.Runner, 2f);
        Check("projectiles are reused, not re-created (" + settled + ")", AttackPool.ProjectilePoolSize == settled);
        for (int i = 0; i < 20; i++) gat.Runner.Fire();
        Steps(gat.Runner, .6f);
        Check("a flood of fire stays within the projectile cap", AttackPool.ProjectilePoolSize <= AttackPool.MaxProjectiles);
        Check("concurrent attack runs are capped", gat.Runner.ActiveRuns <= ShipAttackRunner.MaxRuns);
        for (int i = 0; i < 100; i++)
            AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, 12, Vector3.zero, Vector3.one, 0f,
                ShipFxArt.BurstTicks, false, 0f, null, Vector3.zero, 70);
        Check("drawn effects stay within their cap", AttackPool.SpritePoolSize <= AttackPool.MaxSprites);
        Check("the effect art resolves", ShipFxArt.AttackLoop(13, 0) != null && ShipFxArt.PowerBurst(6, 3) != null &&
              ShipFxArt.MeterFill(1, 0) != null && ShipFxArt.MeterReady(15, 3) != null);
        Teardown(gat);
    }

    // Stands in for the end-of-level boss.
    class FakeBoss : MonoBehaviour, IShipAttackTarget
    {
        public float weight;
        public int hits;
        public void TakeShipAttack(int ship, float w, Vector3 at) { weight += w; hits++; }
    }

    static FakeBoss Boss(float x, float y)
    {
        var go = Hazard(x, y);
        go.name = "boss";
        go.GetComponent<ClearTarget>().SetRadius(.8f);
        return go.AddComponent<FakeBoss>();
    }

    static void BossTakesWeightedHits()
    {
        FreshScene();
        var halo = Ship(4);
        var boss = Boss(0f, 2f);
        halo.Runner.Fire();
        Check("a rail slug hits the boss for one full hit without destroying it",
              boss != null && boss.hits == 1 && Mathf.Approximately(boss.weight, 1f));
        Teardown(halo);

        FreshScene();
        var gat = Ship(12);
        boss = Boss(0f, 1f);
        gat.Runner.Fire();
        Steps(gat.Runner, 2f);
        Check("a gatling burst damages the boss but never more than one hit in total (" + boss.weight + ")",
              boss.hits > 1 && boss.weight > .5f && boss.weight <= 1f + 1e-4f);
        Teardown(gat);

        FreshScene();
        var ufo = Ship(13);
        boss = Boss(0f, 1f);
        ufo.Runner.Fire();
        Steps(ufo.Runner, 2f);
        Check("a held beam damages the boss, capped at one hit (" + boss.weight + ")",
              boss.hits >= 2 && boss.weight <= 1f + 1e-4f && boss != null);
        Teardown(ufo);

        // The real boss body: a registered hazard that BossTarget answers for.
        FreshScene();
        halo = Ship(4);
        var body = Hazard(0f, 2f);
        body.AddComponent<BossTarget>();
        int kills = ShipAttackHits.Kills;
        int bossHits = ShipAttackHits.AttackTargetHits;
        halo.Runner.Fire();
        Check("the boss body (BossTarget) takes the rail as a hit and survives",
              body != null && ShipAttackHits.Kills == kills && ShipAttackHits.AttackTargetHits == bossHits + 1);
        Teardown(halo);
        Object.DestroyImmediate(body);

        // No secret power touches the world speed (a boss holds it at 0.20).
        bool speedHeld = true;
        foreach (int id in ShipId.All)
        {
            FreshScene();
            moveBackGround.speed = .2f;
            collisionDetection.lifeCounter = 1;
            var c = Ship(id);
            c.Secret.SetMeter(SecretPowerController.Full);
            c.Secret.Fire();
            c.Secret.Step(.5f);
            speedHeld &= Mathf.Approximately(moveBackGround.speed, .2f);
            Teardown(c);
            WorldTimeFx.Reset();
        }
        Check("no secret power changes the world speed", speedHeld);

        FreshScene();
        var warden = Ship(7);
        boss = Boss(0f, 1f);
        typeof(ShipPowerController).GetMethod("HitTarget", Inst).Invoke(warden, new object[] { boss.gameObject, Color.white });
        Check("a screen-clear homing shot lands on the boss as one full hit",
              boss != null && boss.hits == 1 && Mathf.Approximately(boss.weight, 1f));
        Teardown(warden);
    }

    static void MeterKeepsClearOfShipUi()
    {
        bool registered = true, clear = true, upright = true;
        var occupied = new List<Bounds>();
        foreach (int id in ShipId.All)
        {
            FreshScene();
            PlayerPrefs.SetInt("spawnShip", id);
            var go = new GameObject("ship" + id, typeof(SpriteRenderer));
            var sprite = shopingShips.SpriteFor(id);
            go.GetComponent<SpriteRenderer>().sprite = sprite;
            float k = shopingShips.NormalizedHullScale(sprite);
            go.transform.localScale = new Vector3(k, k, 1f);
            go.transform.position = new Vector3(0f, -2.5f, 0f);
            if (ShipUiSlots.Spins(id)) go.transform.rotation = Quaternion.Euler(0f, 0f, 70f);
            var c = go.AddComponent<ShipPowerController>();
            c.SendMessage("Awake");
            c.SendMessage("Start");
            var gun = go.GetComponentInChildren<UltimateGun>();
            if (gun != null) gun.SendMessage("Awake");
            var meter = c.Secret.Badge;
            meter.Step(.02f);
            registered &= ShipUiSlots.IsRegistered(meter);
            occupied.Clear();
            ShipUiSlots.Occupied(go.transform, occupied, meter);
            occupied.Add(ShipUiSlots.HullBounds(go.transform, id));
            foreach (var b in occupied)
                if (ShipUiSlots.Overlaps(b, meter.Footprint))
                {
                    clear = false;
                    Debug.Log("[SA] meter of ship " + id + " overlaps " + b + " (meter " + meter.Footprint + ")");
                }
            if (ShipUiSlots.Spins(id) && gun != null)
            {
                for (int i = 0; i < 5; i++) gun.Tick(0f);
                upright &= Quaternion.Angle(gun.transform.rotation, Quaternion.identity) < .5f;
            }
            Teardown(c);
        }
        Check("the secret meter registers its footprint with ShipUiSlots", registered);
        Check("the secret meter keeps clear of the hull, gun, charge indicator and exhaust", clear);
        Check("Ninja and UFO keep their gun upright instead of orbiting with the spin", upright);
    }
}
