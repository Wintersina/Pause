using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// The floaties' small guns (FloatieGuns.cs, FloatieBattery.cs): how many floaties a weapon level gives, every cooldown in every
// state (normal, boss, final level, the ultimate's pause, a paused world), floatie #2's random spread, the seeker's pick and its
// straight fallback, what a bullet stops on (hostile shot, small enemy, hazard, elite, boss), the dock text and a no-allocation run.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod FloatieGunTest.Run
public static class FloatieGunTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[FLOATIE] PASS  " : "[FLOATIE] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            Counts();
            Cooldowns();
            CooldownsInPlay();
            Holds();
            Spread();
            Seeker();
            Hits();
            EliteQuarterHearts();
            BossQuarterHit();
            DockText();
            NoAllocation();
        }
        finally
        {
            Reset();
        }
        Debug.Log("[FLOATIE] failures: " + fails);
        return fails;
    }

    // ---- fixtures -----------------------------------------------------------

    static void Reset()
    {
        TouchInput.PressedOverride = null;
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        Time.timeScale = 1f;
        FloatieBank.Clear();
        FloatieBullets.ReleaseAll();
        AttackPool.StopAll();
        EliteSystem.Clear();
        EliteSystem.PlayerOverride = null;
        BossEncounter.ResetRun();
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
        Reset();
        score.pauseCounter = 0;
        moveBackGround.speed = .2f;
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(true, true);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
        collisionDetection.lifeCounter = 0;
        collisionDetection.atomCheck = false;
        collisionDetection.cloakTimer = 0f;
        PlayerInvuln.Reset();
        Random.InitState(4242);
    }

    sealed class Rig
    {
        public GameObject ship;
        public ShipPowerController power;
        public UltimateGun gun;
        public FloatieBattery battery;
    }

    static Rig MakeShip(int id, int level)
    {
        PlayerPrefs.SetInt("spawnShip", id);
        var r = new Rig();
        r.ship = new GameObject("ship" + id, typeof(SpriteRenderer));
        r.ship.transform.position = new Vector3(0f, -3f, 0f);
        r.power = r.ship.AddComponent<ShipPowerController>();
        r.power.SendMessage("Awake");
        r.power.SendMessage("Start");
        r.gun = r.ship.GetComponentInChildren<UltimateGun>();
        if (r.gun != null) r.gun.SendMessage("Awake");
        r.battery = r.ship.GetComponent<FloatieBattery>();
        if (r.battery != null) r.battery.SetLevel(level);
        return r;
    }

    static void Teardown(Rig r)
    {
        FloatieBullets.ReleaseAll();
        if (r == null || r.ship == null) return;
        r.power.SendMessage("OnDestroy");
        if (r.power.Runner != null) r.power.Runner.SendMessage("OnDestroy");
        if (r.power.Secret != null) r.power.Secret.SendMessage("OnDestroy");
        Object.DestroyImmediate(r.ship);
        AttackPool.StopAll();
    }

    static void Steps(Rig r, float seconds)
    {
        for (float t = 0f; t < seconds - 1e-4f; t += Dt) r.battery.Step(Dt);
    }

    static GameObject Hazard(float x, float y, string tag = "Enimey")
    {
        var go = new GameObject("hazard");
        go.tag = tag;
        go.transform.position = new Vector3(x, y, 0f);
        ClearTarget.Ensure(go).SetRadius(.3f);
        return go;
    }

    sealed class FakeShot : MonoBehaviour, IHostileShot
    {
        public Vector2 pos;
        public bool live = true;
        public bool ShotCollidable => live;
        public Vector2 ShotPosition => pos;
        public float ShotRadius => .1f;
        public int ShotOwner => 1;
        public float ShotAge => 1f;
        public int ShotMass => HostileShots.Light;
        public Color ShotTint => Color.white;
        public void ShotPop(Vector2 at) { live = false; }
        public bool ShotShovable => live;
        public void ShotShove(Vector2 by) { pos += by; }
    }

    static FakeShot Shot(float x, float y)
    {
        var s = new GameObject("fakeshot").AddComponent<FakeShot>();
        s.pos = new Vector2(x, y);
        s.transform.position = new Vector3(x, y, 0f);
        HostileShots.Register(s);
        return s;
    }

    static void Unreg(FakeShot s)
    {
        HostileShots.Unregister(s);
        Object.DestroyImmediate(s.gameObject);
    }

    // A bullet launched just below `at`, flying up, stepped once.
    static FloatieBullet BulletAt(Vector3 at, int ship = 1)
    {
        var b = FloatieBullets.Take();
        b.Launch(null, ship, at + Vector3.down * .08f, Vector3.up, Color.white, default(FloatieAim.Pick));
        b.Tick(Dt);
        return b;
    }

    static FloatieBullet LastActive()
    {
        FloatieBullet last = null;
        foreach (var b in FloatieBullets.All) if (b.Active) last = b;
        return last;
    }

    // ---- checks ---------------------------------------------------------------

    static void Counts()
    {
        FreshScene();
        var r = MakeShip(1, 0);
        Check("the ship carries a floatie battery", r.battery != null);
        int[] want = { 1, 1, 2, 2, 3 };
        bool ok = true;
        for (int level = 0; level <= ShipWeaponUpgrades.MaxLevel; level++)
        {
            r.battery.SetLevel(level);
            bool pure = FloatieTuning.CountAt(level) == want[level];
            bool live = r.battery.Count == want[level];
            bool extras = (r.battery.Extra(0) != null && r.battery.Extra(0).gameObject.activeSelf) == (want[level] >= 2) &&
                          (r.battery.Extra(1) != null && r.battery.Extra(1).gameObject.activeSelf) == (want[level] >= 3);
            ok &= pure && live && extras;
        }
        Check("floaties by level 0..4: 1, 1, 2, 2, 3 (the extras appear with the level)", ok);

        // slots: #2 on the other side up by the nose, #3 on the gun's own side low; both registered, none on the hearts' spot list wrongly
        r.battery.SetLevel(4);
        float mainSide = 1 % 2 == 0 ? -1f : 1f;
        var e0 = r.battery.Extra(0);
        var e1 = r.battery.Extra(1);
        Check("floatie #2 sits on the other side of the hull, above the middle",
              e0 != null && Mathf.Sign(e0.RestLocal.x) == -mainSide && e0.RestLocal.y > 0f);
        Check("the seeker sits on the gun's own side, below the middle",
              e1 != null && Mathf.Sign(e1.RestLocal.x) == mainSide && e1.RestLocal.y < 0f);
        Check("both extras register their footprint with ShipUiSlots (the hearts / badges keep clear)",
              ShipUiSlots.IsRegistered(e0) && ShipUiSlots.IsRegistered(e1));
        var boxes = new Bounds[2];
        Check("the hearts are told about both extras' resting places", r.battery.ExtraRestBounds(boxes) == 2);
        r.battery.SetLevel(2);
        Check("... and one at level 2", r.battery.ExtraRestBounds(boxes) == 1);
        Teardown(r);
    }

    static void Cooldowns()
    {
        Check("level 0-3: #1 and #2 every 1 s, normal play",
              Mathf.Approximately(FloatieTuning.Cooldown(0, 0, false), 1f) && Mathf.Approximately(FloatieTuning.Cooldown(1, 2, false), 1f) &&
              Mathf.Approximately(FloatieTuning.Cooldown(0, 3, false), 1f));
        Check("level 4: #1 and #2 every 0.75 s, the seeker every 1.5 s",
              Mathf.Approximately(FloatieTuning.Cooldown(0, 4, false), .75f) && Mathf.Approximately(FloatieTuning.Cooldown(1, 4, false), .75f) &&
              Mathf.Approximately(FloatieTuning.Cooldown(2, 4, false), 1.5f));
        bool five = true;
        for (int level = 0; level <= 4; level++)
            for (int i = 0; i < FloatieTuning.CountAt(level); i++) five &= Mathf.Approximately(FloatieTuning.Cooldown(i, level, true), 5f);
        Check("a boss fight: 5 s for every floatie at every level", five);
    }

    static void CooldownsInPlay()
    {
        FreshScene();
        var r = MakeShip(1, 0);
        r.battery.ResetTimers();
        Steps(r, 10.05f);
        Check("level 0 fires once a second (" + r.battery.Shots[0] + " shots in 10 s)", r.battery.Shots[0] >= 9 && r.battery.Shots[0] <= 10);
        FloatieBullets.ReleaseAll();

        r.battery.SetLevel(4);
        r.battery.ResetTimers();
        for (int i = 0; i < 3; i++) r.battery.Shots[i] = 0;
        for (float t = 0f; t < 12f; t += Dt) { r.battery.Step(Dt); if (Mathf.Approximately(t % 1f, 0f)) FloatieBullets.ReleaseAll(); }
        Check("level 4: #1 and #2 every 0.75 s (" + r.battery.Shots[0] + " / " + r.battery.Shots[1] + " in 12 s), the seeker every 1.5 s (" + r.battery.Shots[2] + ")",
              r.battery.Shots[0] >= 15 && r.battery.Shots[0] <= 16 && r.battery.Shots[1] >= 15 && r.battery.Shots[1] <= 16 &&
              r.battery.Shots[2] >= 7 && r.battery.Shots[2] <= 8);
        Teardown(r);

        // a boss fight (its fly-in is the scripted entrance, which holds fire; the fight is 5 s)
        FreshScene();
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, 0);
        r = MakeShip(1, 4);
        BossEncounter.Begin(0, null);
        var e = BossEncounter.Instance;
        e.Step(.1f, 1f);
        for (int i = 0; i < 400 && e.State == BossEncounter.Phase.Intro; i++) e.Step(.1f, 1f);
        Check("(the boss fight is on)", BossEncounter.Running && e.State == BossEncounter.Phase.Fight);
        Check("a boss active: every floatie's cooldown reads 5 s",
              Mathf.Approximately(r.battery.Cooldown(0), 5f) && Mathf.Approximately(r.battery.Cooldown(1), 5f) && Mathf.Approximately(r.battery.Cooldown(2), 5f));
        r.battery.ResetTimers();
        for (int i = 0; i < 3; i++) r.battery.Shots[i] = 0;
        for (float t = 0f; t < 10.5f; t += Dt) { r.battery.Step(Dt); if (Mathf.Approximately(t % 1f, 0f)) FloatieBullets.ReleaseAll(); }
        Check("... and in 10 s each fires about twice (" + r.battery.Shots[0] + " / " + r.battery.Shots[1] + " / " + r.battery.Shots[2] + ")",
              r.battery.Shots[0] >= 1 && r.battery.Shots[0] <= 3 && r.battery.Shots[1] >= 1 && r.battery.Shots[1] <= 3 && r.battery.Shots[2] <= 3);
        Teardown(r);
        BossEncounter.ResetRun();
    }

    static void Holds()
    {
        FreshScene();
        var r = MakeShip(1, 2);
        r.battery.ResetTimers();
        Steps(r, 1.2f);
        int base0 = r.battery.Shots[0];
        Check("(it fires in normal play)", base0 >= 1 && r.battery.CurrentHold == FloatieBattery.Hold.None);

        // the ultimate sliding out / firing: the small bullets pause, then resume
        r.battery.ResetTimers();
        for (int i = 0; i < 40; i++) r.gun.Step(1f, Dt, Dt);
        int before = r.battery.Shots[0] + r.battery.Shots[1];
        for (float t = 0f; t < 3f; t += Dt) { r.gun.Step(1f, Dt, Dt); r.battery.Step(Dt); }
        Check("while the ultimate is out nothing is fired and the wait is kept (hold " + r.battery.CurrentHold + ")",
              r.battery.Shots[0] + r.battery.Shots[1] == before && r.battery.CurrentHold == FloatieBattery.Hold.Ultimate);
        for (int i = 0; i < 100; i++) r.gun.Step(0f, Dt, Dt);
        Steps(r, 1.6f);
        Check("... it resumes when the ultimate is back", r.battery.Shots[0] + r.battery.Shots[1] > before && r.battery.CurrentHold == FloatieBattery.Hold.None);

        // a paused world: Step(0) and the pause rule
        r.battery.ResetTimers();
        int shots = r.battery.Shots[0] + r.battery.Shots[1];
        float timer = r.battery.Timer(0);
        for (int i = 0; i < 300; i++) r.battery.Step(0f);
        Check("a frozen world (dt 0) fires nothing and does not run the timers",
              r.battery.Shots[0] + r.battery.Shots[1] == shots && Mathf.Approximately(r.battery.Timer(0), timer) && r.battery.CurrentHold == FloatieBattery.Hold.Frozen);
        TouchInput.PressedOverride = () => false;
        score.pauseCounter = 2;
        Steps(r, 3f);
        Check("a paused game (finger up with pauses left) holds fire", r.battery.Shots[0] + r.battery.Shots[1] == shots && r.battery.CurrentHold == FloatieBattery.Hold.Frozen);
        TouchInput.PressedOverride = () => true;
        Steps(r, 1.2f);
        Check("... and fires again while the finger is down", r.battery.Shots[0] + r.battery.Shots[1] > shots);
        TouchInput.PressedOverride = null;
        score.pauseCounter = 0;

        // death: nothing fires and the extras go
        shots = r.battery.Shots[0] + r.battery.Shots[1];
        buttonClicks.playerDied = true;
        Steps(r, 3f);
        Check("a dead pilot fires nothing and the extra floaties are hidden",
              r.battery.Shots[0] + r.battery.Shots[1] == shots && r.battery.CurrentHold == FloatieBattery.Hold.Dead && !r.battery.Extra(0).gameObject.activeSelf);
        buttonClicks.playerDied = false;
        Steps(r, .1f);
        Check("... and they are back after", r.battery.Extra(0).gameObject.activeSelf);
        Teardown(r);
    }

    static void Spread()
    {
        FreshScene();
        var r = MakeShip(1, 2);
        int left = 0, right = 0;
        bool within = true, headingMatches = true;
        for (int i = 0; i < 300; i++)
        {
            FloatieBullets.ReleaseAll();
            if (!r.battery.Fire(1)) { within = false; break; }
            float a = r.battery.LastAngle[1];
            within &= Mathf.Abs(a) >= FloatieTuning.SpreadMinDegrees - .01f && Mathf.Abs(a) <= FloatieTuning.SpreadMaxDegrees + .01f;
            if (a > 0f) left++; else right++;
            var b = LastActive();
            Vector3 h = b != null ? b.Heading : Vector3.up;
            headingMatches &= b != null && Mathf.Abs(Mathf.Atan2(-h.x, h.y) * Mathf.Rad2Deg - a) < .1f;
        }
        Check("floatie #2 angles every shot " + FloatieTuning.SpreadMinDegrees + "-" + FloatieTuning.SpreadMaxDegrees + " degrees off straight up", within);
        Check("... randomly left (" + left + ") or right (" + right + ")", left > 60 && right > 60);
        Check("... and the bullet really flies that way", headingMatches);
        FloatieBullets.ReleaseAll();
        r.battery.Fire(0);
        var s = LastActive();
        r.battery.Fire(1);
        Check("floatie #2 is turned to its shot's angle at the moment it fires (facing " + r.battery.Extra(0).Aim.ToString("0.0") + ", shot " + r.battery.LastAngle[1].ToString("0.0") + ")",
              Mathf.Abs(r.battery.Extra(0).Aim - r.battery.LastAngle[1]) < .01f && Mathf.Abs(r.battery.LastAngle[1]) >= 15f);
        // it tilts to the next random angle shortly BEFORE the shot (the wait is nearly over), and sits straight early in the wait
        FloatieBullets.ReleaseAll();
        r.battery.ResetTimers();
        r.battery.Extra(0).AimAt(0f, true);
        r.battery.Step(Dt);
        Check("... and sits straight early in its wait", Mathf.Abs(r.battery.Extra(0).AimTarget) < .01f);
        Steps(r, .35f);
        Check("... but tilts toward its next angle before it fires", Mathf.Abs(r.battery.Extra(0).AimTarget) >= 15f && Mathf.Abs(r.battery.Extra(0).Aim) > 1f);
        FloatieBullets.ReleaseAll();
        Check("floatie #1 shoots straight up", s != null && Vector3.Dot(s.Heading, Vector3.up) > .9999f && r.battery.LastAngle[0] == 0f);
        Check("the same pure rule: SpreadAngle is left for a low side roll, right for a high one",
              FloatieTuning.SpreadAngle(.1f, 0f) > 0f && FloatieTuning.SpreadAngle(.9f, 1f) < 0f &&
              Mathf.Approximately(FloatieTuning.SpreadAngle(.1f, 0f), 15f) && Mathf.Approximately(FloatieTuning.SpreadAngle(.9f, 1f), -30f));
        Teardown(r);
    }

    static void Seeker()
    {
        FreshScene();
        var r = MakeShip(1, 4);
        FloatieBullets.ReleaseAll();
        r.battery.Fire(2);
        var b = LastActive();
        Check("no target: the seeker shoots straight up", b != null && Vector3.Dot(b.Heading, Vector3.up) > .9999f && !r.battery.LastSeekerLocked);

        Vector3 from = r.battery.Extra(1).Muzzle;
        var far = Hazard(from.x + 2.5f, from.y + 4f);
        var near = Hazard(from.x - 1.2f, from.y + 2f);
        var tooFar = Hazard(from.x, from.y + 9f);   // out of range (and out of the view)
        FloatieBullets.ReleaseAll();
        r.battery.Fire(2);
        b = LastActive();
        Vector3 want = (near.transform.position - from).normalized;
        Check("the seeker aims at the NEAREST valid target in range", b != null && r.battery.LastSeekerLocked && Vector3.Dot(b.Heading, want) > .99f);

        r.battery.Fire(2);
        var face = r.battery.Extra(1);
        Vector3 toNear = (near.transform.position - face.Muzzle).normalized;
        Check("the seeker FACES its target when it fires (facing dot direction " + Vector3.Dot(face.FacingDir, toNear).ToString("0.000") + ")",
              Vector3.Dot(face.FacingDir, toNear) > .995f && Vector3.Dot(LastActive().Heading, face.FacingDir) > .995f);
        face.AimAt(0f, true);
        FloatieBullets.ReleaseAll();
        for (int i = 0; i < 40; i++) r.battery.Step(Dt);
        float expect = Mathf.Atan2(-(near.transform.position.x - face.Muzzle.x), near.transform.position.y - face.Muzzle.y) * Mathf.Rad2Deg;
        Check("... tracking it smoothly while it waits (" + face.Aim.ToString("0.0") + " of " + expect.ToString("0.0") + ")", Mathf.Abs(Mathf.DeltaAngle(face.Aim, expect)) < 3f);
        FloatieBullets.ReleaseAll();

        // a hostile projectile closer than the nearest enemy is the pick
        var shot = Shot(from.x + .6f, from.y + 1f);
        FloatieBullets.ReleaseAll();
        r.battery.Fire(2);
        b = LastActive();
        want = ((Vector3)shot.pos - from).normalized;
        Check("an enemy projectile counts as a target too", b != null && Vector3.Dot(b.Heading, want) > .99f);
        Unreg(shot);

        // a target in the lane beyond the range is ignored: back to straight
        Object.DestroyImmediate(near);
        Object.DestroyImmediate(far);
        Object.DestroyImmediate(tooFar);
        var beyond = Hazard(from.x, from.y + FloatieTuning.SeekerRange + .5f);
        FloatieBullets.ReleaseAll();
        r.battery.Fire(2);
        b = LastActive();
        Check("nothing within " + FloatieTuning.SeekerRange + " units: straight again", b != null && !r.battery.LastSeekerLocked && Vector3.Dot(b.Heading, Vector3.up) > .9999f);
        for (int i = 0; i < 60; i++) r.battery.Step(Dt);
        Check("with no target the seeker eases back to pointing straight up", Mathf.Abs(r.battery.Extra(1).Aim) < .5f);
        Object.DestroyImmediate(beyond);
        Teardown(r);
    }

    static void Hits()
    {
        FreshScene();
        var r = MakeShip(1, 0);
        FloatieBullets.ReleaseAll();

        // an enemy projectile: popped, the bullet is spent, no score
        var shot = Shot(0f, 1f);
        int down = HostileShots.ShotDown;
        var b = BulletAt(new Vector3(0f, 1f, 0f));
        Check("a bullet destroys an enemy projectile and is consumed", !shot.live && !b.Active && HostileShots.ShotDown == down + 1);
        Unreg(shot);

        // a small enemy dies in one hit, with the usual kill credit
        var enemy = EnemyFactory.Create(EnemyRoster.One(3, EnemyRole.Fighter), new Vector2(1f, 1f), Quaternion.identity);
        ClearTarget.Ensure(enemy);
        int kills = ShipAttackHits.Kills;
        b = BulletAt(new Vector3(1f, 1f, 0f));
        Check("a bullet kills a small enemy in one hit (counted as a weapon kill) and is consumed",
              enemy == null && !b.Active && ShipAttackHits.Kills == kills + 1);

        // a hazard (a rock / mine body)
        var rock = Hazard(-1f, 1f, "Astr");
        kills = ShipAttackHits.Kills;
        b = BulletAt(new Vector3(-1f, 1f, 0f));
        Check("a bullet destroys a hazard and is consumed", rock == null && !b.Active && ShipAttackHits.Kills == kills + 1);

        // one bullet, one hit: two stacked bodies, only the first dies
        var a1 = Hazard(0f, 2f);
        var a2 = Hazard(0f, 2.15f);
        b = BulletAt(new Vector3(0f, 2f, 0f));
        Check("one bullet is consumed per hit (the second body behind survives)", !b.Active && (a1 == null) != (a2 == null));
        if (a1 != null) Object.DestroyImmediate(a1);
        if (a2 != null) Object.DestroyImmediate(a2);

        // the nearest thing first: a shot in front of an enemy is what the bullet stops on
        var behind = Hazard(0f, 3f);
        var front = Shot(0f, 2.4f);
        b = BulletAt(new Vector3(0f, 2.3f, 0f));
        Check("it stops on the first thing along its way (the shot in front, not the enemy behind)", !front.live && behind != null);
        Unreg(front);
        if (behind != null) Object.DestroyImmediate(behind);

        // a bullet off the top of the view is released
        var up = FloatieBullets.Take();
        up.Launch(null, 1, new Vector3(0f, 5.2f, 0f), Vector3.up, Color.white, default(FloatieAim.Pick));
        for (int i = 0; i < 20; i++) up.Tick(Dt);
        Check("a bullet that leaves the view is released back to the pool", !up.Active);
        Teardown(r);
    }

    static void EliteQuarterHearts()
    {
        FreshScene();
        EliteSystem.Clear();
        var pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -2.5f, 0f);
        EliteSystem.PlayerOverride = pilot;
        var e = EliteShip.CreateInPlay(BaseEliteDefs.For("gunship"), new Vector2(0f, 1f));
        e.AttackCooldown = 99f;
        int hearts = e.Hearts;
        FloatieBank.Clear();
        var b = BulletAt(e.transform.position);
        Check("a bullet costs an elite a QUARTER heart: banked, no heart lost yet",
              !b.Active && e.Hearts == hearts && Mathf.Approximately(FloatieBank.Of(e), FloatieTuning.EliteWeight));
        for (int i = 0; i < 3; i++) BulletAt(e.transform.position);
        Check("... the fourth bullet takes the heart (" + hearts + " -> " + e.Hearts + "), through the elite's own hit rules",
              e.Hearts == hearts - 1 && FloatieBank.Of(e) < .01f);
        Object.DestroyImmediate(pilot.gameObject);
        EliteSystem.PlayerOverride = null;
    }

    static void BossQuarterHit()
    {
        FreshScene();
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, 0);
        BossEncounter.Begin(0, null);
        var e = BossEncounter.Instance;
        e.Step(.1f, 1f);
        for (int i = 0; i < 400 && e.State == BossEncounter.Phase.Intro; i++) e.Step(.1f, 1f);
        var body = Object.FindFirstObjectByType<BossTarget>();
        Check("(a boss is in the fight)", body != null && e.State == BossEncounter.Phase.Fight);
        if (body == null) return;
        float damage = e.Damage;
        int hits = e.Hits;
        var b = BulletAt(body.transform.position);
        Check("a bullet is a quarter hit on the boss, through its own hit rules (damage +" + (e.Damage - damage) + ")",
              !b.Active && e.Hits == hits + 1 && Mathf.Abs(e.Damage - damage - FloatieTuning.BossWeight) < .001f);
        BossEncounter.ResetRun();
    }

    static void DockText()
    {
        bool ok = true, none = true;
        for (int ship = 1; ship <= 16; ship++)
        {
            if (!ShipWeaponUpgrades.Has(ship)) continue;
            string two = ShipWeaponUpgrades.StepText(ship, 2), four = ShipWeaponUpgrades.StepText(ship, 4);
            ok &= two.Contains("+1 FLOATIE") && two.StartsWith(ShipWeaponUpgrades.Step(ship, 2).label) &&
                  four.Contains("+1 SEEKER FLOATIE") && four.Contains("-25%") && four.StartsWith(ShipWeaponUpgrades.Step(ship, 4).label);
            none &= ShipWeaponUpgrades.StepText(ship, 1) == ShipWeaponUpgrades.Step(ship, 1).label &&
                    ShipWeaponUpgrades.StepText(ship, 3) == ShipWeaponUpgrades.Step(ship, 3).label;
        }
        Check("every ship's step 2 says +1 FLOATIE and step 4 +1 SEEKER FLOATIE, -25% (its own label kept first)", ok);
        Check("steps 1 and 3 are unchanged", none);
    }

    static void NoAllocation()
    {
        FreshScene();
        var r = MakeShip(1, 4);
        // some distant targets so the seeker has something to pick (and nothing in the way of the bullets)
        var t1 = Hazard(2.2f, 4.6f);
        for (int i = 0; i < 400; i++) { r.battery.Step(Dt); if (i % 20 == 0) FloatieBullets.ReleaseAll(); }   // warm up
        FloatieBullets.ReleaseAll();
        r.battery.ResetTimers();
        int shotsBefore = r.battery.Shots[0] + r.battery.Shots[1] + r.battery.Shots[2];
        long bytes = TestHarness.AllocatedBytes(() => { for (int i = 0; i < 600; i++) r.battery.Step(Dt); });
        int fired = r.battery.Shots[0] + r.battery.Shots[1] + r.battery.Shots[2] - shotsBefore;
        Check("firing allocates nothing (" + bytes + " bytes over " + fired + " shots and 600 frames)", fired >= 20 && bytes == 0);
        Object.DestroyImmediate(t1);
        Teardown(r);
    }
}
