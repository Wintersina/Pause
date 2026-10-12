using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// The secondary-power rework for ships 1-7 (docs/powers-rework/DESIGN.md):
//
//   1 Neon Comet    Shield Pulse: 1.2 s invulnerable, the ring deletes enemy shots within 2.2
//   2 Volt Viper    EMP: at 3+ enemies, stuns them, cancels enemy shots, an arc per stunned enemy
//   3 Solar Fang    Solar Nova: kills within 3.0 and leaves a 2 s burning ring
//   4 Crimson Halo  Capacitor Dump: fires the rail gun at once (3+ targets, any charge)
//   5 Ion Lancer    Ion Battery: +2 pauses and 2 s slow time at <= 2 pauses; piercing lances (3 targets)
//   6 Jade Phantom  Phase Cloak: 4 s ghosted hull (passes through hazards, see CloakShieldTest); bouncing wisps (4)
//   7 Gold Warden   Gold Magnet: 5 s pull and dust x2; area missiles (blast 1.6)
//
// Batch 2, ships 8-15:
//   8  Lightning    Thunderclap: lightning KILLS hazards within 2.6 (a boss takes a weighted hit)
//   9  Ligher       Flare Decoy: 2.5 s, shooters aim at the flare too, pops with kill radius 1.5
//   10 Paranoid     Time Bubble: 0.45x for 2.5 s inside a visible, tinted dome sized to its radius
//   11 Ninja        Blink Dash: also fires when a hazard is within 2.0; dodges to the safest side
//   12 Saboteur     Black Hole: at 3+ enemies on screen
//   13 UFO          Tractor Pull: drags hazards in, kills drop dust; the Tractor Beam pulls to its centre
//   14 Dove         Mending: one heart back as soon as the hull is damaged, with a visible burst
//   15 Turtle       Hard Shell: shell drawn for its whole duration, a distinct break burst on a hit
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod PowersReworkTest.Run
public static class PowersReworkTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[PR] PASS  " : "[PR] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        ShieldPulseCancelsShots();
        EmpNeedsThreeAndCancelsShots();
        NovaLeavesABurningRing();
        CapacitorDumpFiresTheRail();
        IonBatteryAddsSlowTime();
        IonLancesPierceThree();
        PhaseCloakGhostsTheHull();
        WispsBounceFour();
        GoldMagnetDoublesDust();
        MissilesBlast();
        ThunderclapKills();
        DecoyRedirectsAim();
        TimeBubbleIsADome();
        BlinkDashFiresEarlyAndDodgesSafely();
        BlackHoleAtThree();
        TractorPullKillsAndDrops();
        TractorBeamPullsToCentre();
        MendingFiresWhenDamaged();
        ShellStaysDrawnAndShatters();
        DescriptionsMatch();

        AttackPool.StopAll();
        WorldTimeFx.Reset();
        HostileShots.ResetCounters();
        Debug.Log("[PR] failures: " + fails);
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
        HostileShots.ResetCounters();
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
        WorldTimeFx.Reset();
    }

    static void Steps(ShipAttackRunner r, float seconds, float dt = .02f)
    {
        for (float t = 0f; t < seconds; t += dt) r.Step(dt);
    }

    static void Run(SecretPowerController s, float seconds, float dt = .02f)
    {
        for (float t = 0f; t < seconds; t += dt) s.Step(dt);
    }

    static void Kill(List<GameObject> list)
    {
        foreach (var go in list) if (go != null) Object.DestroyImmediate(go);
        list.Clear();
    }

    // A hostile projectile that records being popped.
    sealed class FakeShot : MonoBehaviour, IHostileShot
    {
        public Vector2 pos;
        public bool live = true;
        public int mass = HostileShots.Light;
        public bool ShotCollidable => live;
        public Vector2 ShotPosition => pos;
        public float ShotRadius => .1f;
        public int ShotOwner => 1;
        public float ShotAge => 1f;
        public int ShotMass => mass;
        public Color ShotTint => Color.white;
        public void ShotPop(Vector2 at) { live = false; }
        public bool ShotShovable => live;
        public void ShotShove(Vector2 by) { pos += by; }
    }

    static FakeShot Shot(float x, float y, int mass = HostileShots.Light)
    {
        var s = new GameObject("fakeshot").AddComponent<FakeShot>();
        s.pos = new Vector2(x, y);
        s.mass = mass;
        HostileShots.Register(s);
        return s;
    }

    static void Unreg(params FakeShot[] shots)
    {
        foreach (var s in shots) { HostileShots.Unregister(s); Object.DestroyImmediate(s.gameObject); }
    }

    // ---- 1 Shield Pulse --------------------------------------------------

    static void ShieldPulseCancelsShots()
    {
        FreshScene();
        var comet = Ship(1);
        var s = comet.Secret;
        Check("Shield Pulse lasts 1.2 s", Mathf.Approximately(s.pulseInvulnerable, 1.2f));
        Check("and its ring reaches 2.2", Mathf.Approximately(s.pulseShotRadius, 2.2f));
        Vector3 at = comet.transform.position;
        var near = Shot(at.x + 2.0f, at.y + .3f);
        var far = Shot(at.x + 2.7f, at.y);
        var pool = Shot(at.x + .5f, at.y, HostileShots.Fixed);
        var close = Hazard(at.x + .6f, at.y + .6f);
        var wide = Hazard(at.x + 2f, at.y + 1f);
        s.SetMeter(SecretPowerController.Full);
        s.Fire();
        Check("the pulse makes the ship invulnerable for 1.2 s",
              collisionDetection.Invulnerable && Mathf.Approximately(collisionDetection.cloakTimer, 1.2f));
        Check("the ring deletes an enemy shot within 2.2", !near.live);
        Check("and leaves one beyond it", far.live);
        Check("a landed pool is not a shot to delete", pool.live);
        Check("it still kills hazards right on top of the ship", ShipAttackHits.AlreadyHit(close));
        Check("but not the ones past the kill radius", wide != null && !ShipAttackHits.AlreadyHit(wide));
        Unreg(near, far, pool);
        Object.DestroyImmediate(wide);
        Teardown(comet);
    }

    // ---- 2 EMP -----------------------------------------------------------

    static void EmpNeedsThreeAndCancelsShots()
    {
        FreshScene();
        var viper = Ship(2);
        var s = viper.Secret;
        var foes = new List<GameObject>();
        var movers = new List<moveItemEnmInStrightLine>();
        for (int i = 0; i < 2; i++)
        {
            var f = Hazard(-1f + i * 1f, 1f);
            movers.Add(f.AddComponent<moveItemEnmInStrightLine>());
            foes.Add(f);
        }
        var inView = Shot(1f, 2f);
        var offScreen = Shot(1f, 30f);
        s.SetMeter(SecretPowerController.Full);
        s.Step(.02f);
        Check("EMP waits at 2 enemies", s.FireCount == 0);
        var third = Hazard(0f, 3f);
        movers.Add(third.AddComponent<moveItemEnmInStrightLine>());
        foes.Add(third);
        s.Step(.02f);
        Check("EMP fires at 3 enemies", s.FireCount == 1 && s.Stunning);
        bool frozen = true;
        foreach (var m in movers) frozen &= !m.enabled;
        Check("it stuns all three", frozen && s.LockedCount == 3);
        Check("it cancels the enemy shots on screen", !inView.live);
        Check("and leaves a shot far off screen", offScreen.live);
        Check("an arc is drawn for each stunned enemy", s.ArcCount == 3);
        Unreg(inView, offScreen);
        Teardown(viper);
        Kill(foes);
    }

    // ---- 3 Solar Nova ----------------------------------------------------

    static void NovaLeavesABurningRing()
    {
        FreshScene();
        var fang = Ship(3);
        var s = fang.Secret;
        Vector3 at = fang.transform.position;
        Check("the nova's kill radius is 3.0", Mathf.Approximately(s.novaRadius, 3f));
        var inside = Hazard(at.x + 2.8f, at.y);
        var outside = Hazard(at.x + 1.2f, at.y + 3.6f);
        s.SetMeter(SecretPowerController.Full);
        s.Fire();
        Check("the nova kills out to 3.0", ShipAttackHits.AlreadyHit(inside));
        Check("but not past it", outside != null && !ShipAttackHits.AlreadyHit(outside));
        Check("and leaves a burning ring", s.Burning);
        Object.DestroyImmediate(outside);

        // an enemy that drifts into the ring a second later dies in it
        s.Step(1f);
        var drifter = Hazard(at.x + 2.5f, at.y + 1f);
        s.Step(.02f);
        Check("the ring kills what enters it", ShipAttackHits.AlreadyHit(drifter) && s.Burning);
        Object.DestroyImmediate(drifter);

        s.Step(1.1f);
        Check("the ring burns out after 2 s", !s.Burning);
        var late = Hazard(at.x + 1f, at.y + 1f);
        s.Step(.02f);
        Check("and then kills nothing", late != null && !ShipAttackHits.AlreadyHit(late));
        Object.DestroyImmediate(late);
        Teardown(fang);
    }

    // ---- 4 Capacitor Dump ------------------------------------------------

    static void CapacitorDumpFiresTheRail()
    {
        FreshScene();
        var halo = Ship(4);
        var s = halo.Secret;
        halo.Arm(10f);
        halo.ReduceTimer(8f);
        Check("the attack is mostly charged (the old < 65% rule would block it)", halo.Charge01 > .65f);
        var line = Hazard(0f, 1f);
        var aside = Hazard(1.6f, 0f);
        var more = Hazard(-1.6f, 2f);
        s.SetMeter(SecretPowerController.Full);
        s.Step(.02f);
        Check("it fires on 3 targets whatever the charge", s.FireCount == 1 && halo.Runner.FireCount == 1);
        Check("the rail goes off at once: its line is cleared", ShipAttackHits.AlreadyHit(line));
        Check("and what is beside the line is not", aside != null && !ShipAttackHits.AlreadyHit(aside));
        Object.DestroyImmediate(aside);
        Object.DestroyImmediate(more);

        FreshScene();
        halo = Ship(4);
        Hazard(0f, 1f);
        Hazard(1f, 1f);
        halo.Secret.SetMeter(SecretPowerController.Full);
        halo.Secret.Step(.02f);
        Check("it still waits at 2 targets", halo.Secret.FireCount == 0 && halo.Runner.FireCount == 0);
        Teardown(halo);
    }

    // ---- 5 Ion Battery, Ion Lances ---------------------------------------

    static void IonBatteryAddsSlowTime()
    {
        FreshScene();
        var ion = Ship(5);
        var s = ion.Secret;
        score.pauseCounter = 3;
        s.SetMeter(SecretPowerController.Full);
        s.Step(.02f);
        Check("Ion Battery waits while 3 pauses are left", s.FireCount == 0);
        score.pauseCounter = 2;
        s.Step(.02f);
        Check("it fires at 2 pauses and adds 2", s.FireCount == 1 && score.pauseCounter == 4);
        Check("with a slow-time on the world", WorldTimeFx.BubbleScale < 1f && WorldTimeFx.Scale < 1f);
        s.Step(1.9f);
        Check("still slow just before 2 s", WorldTimeFx.BubbleScale < 1f);
        s.Step(.2f);
        Check("normal speed after 2 s", Mathf.Approximately(WorldTimeFx.BubbleScale, 1f));
        Teardown(ion);
        score.pauseCounter = 0;
    }

    static void IonLancesPierceThree()
    {
        FreshScene();
        var ion = Ship(5);
        Check("Ion Lancer's volley pierces", ion.Loadout.volley == VolleyKind.Pierce && ion.Loadout.maxHits == 3);
        var from = new Vector3(0f, -3f, 0f);
        var first = Hazard(0f, -1f);
        var line = new List<GameObject> { Hazard(.1f, 0f), Hazard(-.1f, 1.2f), Hazard(0f, 2.4f) };
        var aside = Hazard(1.5f, .6f);
        float budget = 1f;
        int kills = ShipVolley.AfterHit(5, from, first.transform.position, first, ref budget);
        Check("a lance goes on through two more targets (" + kills + ")", kills == 2);
        Check("the next two in line die", ShipAttackHits.AlreadyHit(line[0]) && ShipAttackHits.AlreadyHit(line[1]));
        Check("the fourth in line lives (3 targets in all)", !ShipAttackHits.AlreadyHit(line[2]));
        Check("one off its line lives", !ShipAttackHits.AlreadyHit(aside));
        Object.DestroyImmediate(first);
        Object.DestroyImmediate(aside);
        Kill(line);
        Teardown(ion);
    }

    // ---- 6 Phase Cloak, Wisps --------------------------------------------

    static void PhaseCloakGhostsTheHull()
    {
        FreshScene();
        var jade = Ship(6);
        var s = jade.Secret;
        var sr = jade.GetComponent<SpriteRenderer>();
        Check("Phase Cloak lasts 4 s", Mathf.Approximately(s.cloakSeconds, 4f));
        Check("not ghosted until it goes off", !SecretPowerController.Ghosted && Mathf.Approximately(sr.color.a, 1f));
        s.SetMeter(SecretPowerController.Full);
        s.Fire();
        Check("the hull is ghosted and invulnerable", SecretPowerController.Ghosted && collisionDetection.Invulnerable);
        Check("and drawn translucent", sr.color.a < .6f);
        s.Step(3.9f);
        Check("still a ghost at 3.9 s", SecretPowerController.Ghosted && sr.color.a < .6f);
        s.Step(.2f);
        Check("solid again after 4 s", !SecretPowerController.Ghosted && Mathf.Approximately(sr.color.a, 1f));
        Teardown(jade);
    }

    static void WispsBounceFour()
    {
        FreshScene();
        var jade = Ship(6);
        Check("Jade Phantom's volley bounces 4 times", jade.Loadout.volley == VolleyKind.Bounce && jade.Loadout.maxHits == 4);
        var first = Hazard(0f, -2.6f);
        var chain = new List<GameObject>();
        for (int i = 0; i < 5; i++) chain.Add(Hazard(i % 2 == 0 ? .3f : -.3f, -1.2f + i * 1.4f));
        float budget = 1f;
        int kills = ShipVolley.AfterHit(6, new Vector3(0f, -3f, 0f), first.transform.position, first, ref budget);
        Check("the wisp bounces on to 4 more targets (" + kills + ")", kills == 4);
        bool four = true;
        for (int i = 0; i < 4; i++) four &= ShipAttackHits.AlreadyHit(chain[i]);
        Check("the first four in the chain die", four);
        Check("the fifth lives", !ShipAttackHits.AlreadyHit(chain[4]));
        Object.DestroyImmediate(first);
        Kill(chain);
        Teardown(jade);
    }

    // ---- 7 Gold Magnet, Missiles ----------------------------------------

    static void GoldMagnetDoublesDust()
    {
        FreshScene();
        var gold = Ship(7);
        var s = gold.Secret;
        Check("dust is worth 1x with the magnet off", Mathf.Approximately(SecretPowerController.DustMultiplier, 1f));
        float before = score.totalCurrency + score.tutorialCurrency;
        var go = new GameObject("cd");
        var cd = go.AddComponent<collisionDetection>();
        var award = typeof(collisionDetection).GetMethod("awardDust", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        award.Invoke(cd, new object[] { 1f });
        float plain = score.totalCurrency + score.tutorialCurrency - before;

        s.SetMeter(SecretPowerController.Full);
        s.Fire();
        Check("the magnet pulls for 5 s", s.MagnetActive && Mathf.Approximately(s.magnetSeconds, 5f));
        Check("and dust is worth x2 meanwhile", Mathf.Approximately(SecretPowerController.DustMultiplier, 2f));
        before = score.totalCurrency + score.tutorialCurrency;
        award.Invoke(cd, new object[] { 1f });
        float doubled = score.totalCurrency + score.tutorialCurrency - before;
        Check("a pickup pays double (" + plain + " -> " + doubled + ")", plain > 0f && Mathf.Approximately(doubled, plain * 2f));

        // and it pulls pickups in
        var star = Hazard(1.5f, -1f, "pickUp");
        s.Step(.1f);
        Check("pickups are pulled toward the ship", star.transform.position.x < 1.5f);
        Object.DestroyImmediate(star);
        s.Step(5.1f);
        Check("back to normal after 5 s", !s.MagnetActive && Mathf.Approximately(SecretPowerController.DustMultiplier, 1f));
        Object.DestroyImmediate(go);
        Teardown(gold);
    }

    static void MissilesBlast()
    {
        FreshScene();
        var gold = Ship(7);
        Check("Gold Warden's volley blasts 1.6 around the mark",
              gold.Loadout.volley == VolleyKind.Blast && Mathf.Approximately(gold.Loadout.width, 1.6f));
        var first = Hazard(0f, 1f);
        var inside = new List<GameObject> { Hazard(1.3f, 1f), Hazard(-.9f, 2f), Hazard(0f, -.4f) };
        var outside = Hazard(2.2f, 1f);
        float budget = 1f;
        int kills = ShipVolley.AfterHit(7, new Vector3(0f, -3f, 0f), first.transform.position, first, ref budget);
        bool all = true;
        foreach (var go in inside) all &= ShipAttackHits.AlreadyHit(go);
        Check("the blast takes everything within 1.6 (" + kills + ")", kills == 3 && all);
        Check("and nothing past it", !ShipAttackHits.AlreadyHit(outside));
        Object.DestroyImmediate(first);
        Object.DestroyImmediate(outside);
        Kill(inside);
        Teardown(gold);
    }

    // ---- batch 2 --------------------------------------------------------

    // A boss stand-in: takes weighted hits, never dies.
    sealed class FakeBoss : MonoBehaviour, IShipAttackTarget
    {
        public int hits;
        public float weight;
        public void TakeShipAttack(int ship, float w, Vector3 at) { hits++; weight += w; }
    }

    static int SpritesActiveNamed(string name)
    {
        int n = 0;
        foreach (var sr in Object.FindObjectsByType<AttackSprite>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (sr.Active && sr.name == name) n++;
        return n;
    }

    // ---- 8 Thunderclap ---------------------------------------------------

    static void ThunderclapKills()
    {
        FreshScene();
        var light = Ship(8);
        var s = light.Secret;
        Vector3 at = light.transform.position;
        Check("Thunderclap reaches 2.6", Mathf.Approximately(s.clapRadius, 2.6f));
        var near = Hazard(at.x, at.y + 2.4f);
        var close = Hazard(at.x - .5f, at.y + 1f);
        var far = Hazard(at.x, at.y + 3.4f);
        var boss = Hazard(at.x + .4f, at.y + 1.5f);
        var fb = boss.AddComponent<FakeBoss>();
        s.SetMeter(SecretPowerController.Full);
        s.Fire();
        Check("lightning kills hazards within the radius", ShipAttackHits.AlreadyHit(near) && ShipAttackHits.AlreadyHit(close));
        Check("it kills them, not just shoves them (" + s.ClapKills + ")", s.ClapKills == 2);
        Check("hazards past 2.6 live", far != null && !ShipAttackHits.AlreadyHit(far));
        Check("a boss takes one weighted hit and is not killed", fb.hits == 1 && fb.weight > 0f && fb.weight < 1f && boss != null);
        Object.DestroyImmediate(far);
        Object.DestroyImmediate(boss);
        Teardown(light);
    }

    // ---- 9 Flare Decoy ---------------------------------------------------

    static void DecoyRedirectsAim()
    {
        FreshScene();
        var ligher = Ship(9);
        var s = ligher.Secret;
        Vector2 pilot = ligher.transform.position;
        Check("with no flare a shooter aims at the pilot", ShipDecoy.AimPoint(pilot) == pilot);
        Check("the decoy lasts 2.5 s and pops at 1.5", Mathf.Approximately(s.decoySeconds, 2.5f) && Mathf.Approximately(s.decoyBlastRadius, 1.5f));
        s.SetMeter(SecretPowerController.Full);
        s.Fire();
        Vector2 spot = ShipDecoy.Position;
        Check("the flare is placed and shooters aim at it", ShipDecoy.Active && ShipDecoy.AimPoint(pilot) == spot && spot != pilot);
        Check("the flare is drawn", s.DecoyFx != null && s.DecoyFx.Active);
        var next = Hazard(spot.x + 1.2f, spot.y);
        var wide = Hazard(spot.x + 2.4f, spot.y);
        s.Step(2.4f);
        Check("still the target at 2.4 s", ShipDecoy.Active && ShipDecoy.AimPoint(pilot) == spot);
        s.Step(.2f);
        Check("it pops after 2.5 s: aim back on the pilot", !ShipDecoy.Active && ShipDecoy.AimPoint(pilot) == pilot);
        Check("the pop kills within 1.5", ShipAttackHits.AlreadyHit(next));
        Check("and not past it", wide != null && !ShipAttackHits.AlreadyHit(wide));
        Object.DestroyImmediate(wide);
        Teardown(ligher);
    }

    // ---- 10 Time Bubble --------------------------------------------------

    static void TimeBubbleIsADome()
    {
        FreshScene();
        var paranoid = Ship(10);
        var s = paranoid.Secret;
        s.SetMeter(SecretPowerController.Full);
        s.Fire();
        Check("the world runs at 0.45x", Mathf.Approximately(WorldTimeFx.BubbleScale, .45f));
        var dome = s.BubbleFx;
        Check("a dome is drawn", dome != null && dome.Active && dome.name == SecretPowerController.BubbleDomeName);
        Check("sized to the bubble radius", Mathf.Approximately(dome.transform.localScale.x, s.bubbleRadius * 2f));
        var c = dome.Renderer.color;
        Check("with a clear tint, not plain white", c.a < 1f && c.r < .99f);
        s.Step(2.4f);
        Check("slow and domed until 2.5 s", WorldTimeFx.BubbleScale < 1f && dome.Active);
        s.Step(.2f);
        Check("normal speed and no dome after", Mathf.Approximately(WorldTimeFx.BubbleScale, 1f) && !dome.Active);
        Teardown(paranoid);
    }

    // ---- 11 Blink Dash ---------------------------------------------------

    static void BlinkDashFiresEarlyAndDodgesSafely()
    {
        FreshScene();
        var ninja = Ship(11);
        var s = ninja.Secret;
        Vector3 at = ninja.transform.position;
        var far = Hazard(at.x, at.y + 3.4f);
        s.SetMeter(SecretPowerController.Full);
        s.Step(.02f);
        Check("a hazard 3.4 away does not set the dash off", s.FireCount == 0);
        Object.DestroyImmediate(far);
        var side = Hazard(at.x + 1.8f, at.y);   // not about to hit, but within 2.0
        s.Step(.02f);
        Check("a hazard within 2.0 sets it off before any hit is imminent", s.FireCount == 1 && s.DashCount == 1);
        Object.DestroyImmediate(side);
        Teardown(ninja);

        FreshScene();
        ninja = Ship(11);
        s = ninja.Secret;
        at = ninja.transform.position;
        var threat = Hazard(at.x - .3f, at.y + .9f);
        var wall = Hazard(at.x + 1.25f, at.y);
        Check("the safer side is the one away from the second hazard", s.SafestDashDirection(threat) < 0f);
        s.SetMeter(SecretPowerController.Full);
        s.Fire(threat);
        Check("it dodges that way, past the old away-from-threat side",
              s.LastDashDirection < 0f && ninja.transform.position.x < -1f);
        Object.DestroyImmediate(threat);
        Object.DestroyImmediate(wall);
        Teardown(ninja);
    }

    // ---- 12 Black Hole ---------------------------------------------------

    static void BlackHoleAtThree()
    {
        FreshScene();
        var sab = Ship(12);
        var s = sab.Secret;
        var foes = new List<GameObject>();
        for (int i = 0; i < 2; i++) foes.Add(Hazard(-1f + i, 1f));
        s.SetMeter(SecretPowerController.Full);
        s.Step(.02f);
        Check("the hole waits at 2 enemies", s.FireCount == 0);
        foes.Add(Hazard(0f, 2f));
        s.Step(.02f);
        Check("it opens at 3 enemies", s.FireCount == 1 && s.HoleActive);
        Teardown(sab);
        Kill(foes);
    }

    // ---- 13 Tractor Pull, Tractor Beam -----------------------------------

    static void TractorPullKillsAndDrops()
    {
        FreshScene();
        var ufo = Ship(13);
        var s = ufo.Secret;
        Check("UFO's secret is the Tractor Pull", ufo.Loadout.power == SecretPower.TractorPull);
        var a = Hazard(1.5f, -1f);
        var b = Hazard(-1.5f, -1f);
        var boss = Hazard(1f, 0f);
        boss.AddComponent<FakeBoss>();
        s.SetMeter(SecretPowerController.Full);
        s.Step(.02f);
        Check("it fires at 2 hazards close by and starts pulling", s.FireCount == 1 && s.TractorActive);
        Run(s, .1f);
        Check("hazards are pulled toward the ship", a.transform.position.x < 1.5f - .3f && b.transform.position.x > -1.5f + .3f);
        Check("a boss is never moved", Mathf.Approximately(boss.transform.position.x, 1f));
        Run(s, 1.0f);
        Check("what reaches the ship dies (" + s.TractorKills + ")", s.TractorKills == 2);
        Check("kills drop star dust", s.TractorStars == 2 * s.tractorStarsPerKill);
        Run(s, 1f);
        Check("the pull ends after 1.8 s", !s.TractorActive);
        Object.DestroyImmediate(boss);
        Teardown(ufo);
    }

    static void TractorBeamPullsToCentre()
    {
        FreshScene();
        var ufo = Ship(13);
        var beside = Hazard(1.2f, 1f);       // well outside the 0.38 beam line
        float x0 = beside.transform.position.x;
        var boss = Hazard(-1.2f, 1f);
        boss.AddComponent<FakeBoss>();
        ufo.Runner.Fire();
        Steps(ufo.Runner, .1f);
        Check("a target beside the beam is dragged towards its centre", beside != null && beside.transform.position.x < x0 - .5f);
        Check("a boss beside it is left where it was", Mathf.Approximately(boss.transform.position.x, -1.2f));
        Check("the pull was counted", ufo.Runner.BeamPulls > 0);
        Steps(ufo.Runner, 1.2f);
        Check("and once in the line it is destroyed", ShipAttackHits.AlreadyHit(beside));
        Object.DestroyImmediate(boss);
        Teardown(ufo);
    }

    // ---- 14 Mending ------------------------------------------------------

    static void MendingFiresWhenDamaged()
    {
        FreshScene();
        var dove = Ship(14);
        var s = dove.Secret;
        s.SetMeter(SecretPowerController.Full);
        s.Step(.02f);
        Check("Mending waits while the hull is whole", s.FireCount == 0 && s.Ready);
        collisionDetection.lifeCounter = 1;
        int sprites = AttackPool.ActiveSprites;
        s.Step(.02f);
        Check("it fires the moment the hull is damaged and heals one heart",
              s.FireCount == 1 && s.Mends == 1 && collisionDetection.lifeCounter == 0);
        Check("with a visible burst (" + (AttackPool.ActiveSprites - sprites) + " effects)", AttackPool.ActiveSprites - sprites >= 2);
        s.SetMeter(SecretPowerController.Full);
        collisionDetection.lifeCounter = 2;
        s.Step(.02f);
        Check("and again later: one heart each time", s.Mends == 2 && collisionDetection.lifeCounter == 1);
        Teardown(dove);
    }

    // ---- 15 Hard Shell ---------------------------------------------------

    static void ShellStaysDrawnAndShatters()
    {
        FreshScene();
        var turtle = Ship(15);
        var s = turtle.Secret;
        var lane = Hazard(0f, -1f);
        s.SetMeter(SecretPowerController.Full);
        s.Step(.02f);
        Check("the shell goes up", s.ShellUp);
        bool drawn = s.ShellFx != null && s.ShellFx.Active && s.ShellFx.name == SecretPowerController.ShellAuraName;
        for (int sec = 1; sec <= 7; sec++)
        {
            s.Step(1f);
            drawn &= s.ShellUp && s.ShellFx != null && s.ShellFx.Active && s.ShellFx.name == SecretPowerController.ShellAuraName;
            if (sec == 3) s.ShellFx.Stop();   // the pool took the effect back: it is drawn again
        }
        Check("it is drawn on the hull the whole time", drawn);
        s.Step(1f);
        Check("and is gone when its 8 s are up", !s.ShellUp && s.ShellFx == null && SpritesActiveNamed(SecretPowerController.ShellAuraName) == 0);
        Object.DestroyImmediate(lane);
        Teardown(turtle);

        FreshScene();
        turtle = Ship(15);
        s = turtle.Secret;
        var hit = Hazard(0f, -3f);
        s.SetMeter(SecretPowerController.Full);
        s.Fire();
        int before = AttackPool.ActiveSprites;
        Check("the shell eats a hit", SecretPowerController.InterceptHit(hit) && !s.ShellUp);
        Check("with its own break burst (" + s.ShellBreaks + ")", s.ShellBreaks == 1 && AttackPool.ActiveSprites >= before + 1 &&
              SpritesActiveNamed(SecretPowerController.ShellAuraName) == 0);
        Object.DestroyImmediate(hit);
        Teardown(turtle);
    }

    // ---- text ------------------------------------------------------------

    static void DescriptionsMatch()
    {
        string all = "";
        for (int id = 1; id <= 7; id++) all += CodexCatalogue.LoadoutLore(id) + "\n";
        Check("the codex says what the pulse does now", all.Contains("1.2 s") && all.Contains("deletes enemy shots"));
        Check("EMP fires on 3 enemies", ShipLoadoutTable.TriggerLine(ShipLoadoutTable.For(2)) == "3+ enemies on screen");
        Check("Capacitor Dump fires the rail", CodexCatalogue.LoadoutLore(4).Contains("Fires the rail gun at once"));
        Check("Ion Battery fires at 2 pauses", ShipLoadoutTable.TriggerLine(ShipLoadoutTable.For(5)) == "down to 2 pauses");
        Check("the three top-tier volleys are described", all.Contains("Piercing ion lances") && all.Contains("Bouncing wisps") && all.Contains("Area missiles"));
        Check("Gold Magnet says double", all.Contains("worth double"));
        string b2 = "";
        for (int id = 8; id <= 15; id++) b2 += CodexCatalogue.LoadoutLore(id) + "\n";
        Check("the codex says Thunderclap kills", b2.Contains("Lightning strikes everything within 2.6 dead"));
        Check("Flare Decoy mentions shooters", b2.Contains("shooters aim at"));
        Check("Time Bubble mentions the dome and 0.45x", b2.Contains("visible dome") && b2.Contains("0.45x"));
        Check("Blink Dash mentions the close threat", b2.Contains("within 2"));
        Check("Black Hole says 3 enemies", ShipLoadoutTable.For(12).powerLine.Contains("3 enemies") &&
              ShipLoadoutTable.TriggerLine(ShipLoadoutTable.For(12)) == "3+ enemies on screen");
        Check("UFO is the Tractor Pull", b2.Contains("TRACTOR PULL") && !b2.Contains("STAR SHOWER") && b2.Contains("pulls targets in"));
        Check("Mending and Hard Shell lines match", b2.Contains("burst of feathers") && b2.Contains("shatters"));
        Check("Blink Dash trigger line", ShipLoadoutTable.TriggerLine(ShipLoadoutTable.For(11)).StartsWith("a hit or a threat within 2"));
    }
}
