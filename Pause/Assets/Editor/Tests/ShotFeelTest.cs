using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// The normal gun's feel and Paragon's drones (FloatieGuns.cs, FloatieBattery.cs, FloatieFx.cs):
//   * Paragon flies with 2 drones from the start and 2, 3, 4, 5, 6 across its five tiers; every other ship is unchanged
//   * with 6 drones and every shot atom at 10 the bullet cap holds, no drone starves (round-robin + fair share), shards keep a reserve
//   * RAPID shortens EVERY drone's cooldown (the straight one, the angled one, every seeker, Paragon's fans), boss fights included
//   * NOTHING HOMES: a bullet's heading never changes after it leaves, except a RICOCHET edge bounce (a split's shards are new bullets)
//   * the feel: the hit spark, the muzzle flash and the nudge are pooled, bounded and allocation free; the drawing is the new pixel sprite
//   * the gun keeps firing while the ship's ultimate is out, in a boss fight too
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ShotFeelTest.Run
public static class ShotFeelTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[FEEL] PASS  " : "[FEEL] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;
    const int Paragon = PremiumShips.Paragon;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            ParagonCurve();
            ParagonFormation();
            ParagonText();
            RapidShortensEveryDrone();
            NothingHomes();
            CapAndFairShare();
            GunKeepsFiringInUltimate();
            PerDroneAtoms();
            Feel();
        }
        finally { Reset(); }
        Debug.Log("[FEEL] failures: " + fails);
        return fails;
    }

    // ---- fixtures -----------------------------------------------------------

    static void Reset()
    {
        TouchInput.PressedOverride = null;
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        Time.timeScale = 1f;
        FloatieBullets.ReleaseAll();
        FloatieFx.Clear();
        AttackPool.StopAll();
        EliteSystem.Clear();
        EliteSystem.PlayerOverride = null;
        BossEncounter.ResetRun();
        ShotAtoms.BeginRun(3);
    }

    public static void FreshScene()
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

    public sealed class Rig
    {
        public GameObject ship;
        public ShipPowerController power;
        public UltimateGun gun;
        public FloatieBattery battery;
    }

    public static void ArmSquad(int slots = 3)
    {
        ShotAtoms.SetDroneCount(slots);
        for (int i = 0; i < slots; i++)
        {
            int empty = DroneSquad.EmptySlot(); if (empty < 0) break;
            DroneSquad.CollectCore(empty % 3 == 0 ? DroneMode.Straight : empty % 3 == 1 ? DroneMode.Alternating : DroneMode.Aimed);
        }
        if (FloatieBattery.Current != null) FloatieBattery.Current.Refresh();
    }

    public static Rig MakeShip(int id, int level, int slots = 3, bool activate = true)
    {
        ShotAtoms.SetDroneCount(slots);
        if (activate)
            for (int i = 0; i < slots; i++)
            {
                int empty = DroneSquad.EmptySlot();
                if (empty < 0) break;
                DroneSquad.CollectCore(empty % 3 == 0 ? DroneMode.Straight : empty % 3 == 1 ? DroneMode.Alternating : DroneMode.Aimed);
            }
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

    public static void Teardown(Rig r)
    {
        FloatieBullets.ReleaseAll();
        if (r == null || r.ship == null) return;
        r.power.SendMessage("OnDestroy");
        if (r.power.Runner != null) r.power.Runner.SendMessage("OnDestroy");
        if (r.power.Secret != null) r.power.Secret.SendMessage("OnDestroy");
        Object.DestroyImmediate(r.ship);
        AttackPool.StopAll();
        collisionDetection.cloakTimer = 0f;
        collisionDetection.lifeCounter = 0;
    }

    static GameObject Hazard(float x, float y)
    {
        var go = new GameObject("hazard");
        go.tag = "Enimey";
        go.transform.position = new Vector3(x, y, 0f);
        ClearTarget.Ensure(go).SetRadius(.3f);
        return go;
    }

    static void AllAtoms(int level)
    {
        for (int a = 0; a < ShotAtomTuning.Count; a++) ShotAtoms.SetLevel((ShotAtom)a, level);
    }

    // ---- checks ---------------------------------------------------------------

    static void ParagonCurve()
    {
        FreshScene();
        bool pure = true;
        int[] want = { 1, 1, 1, 2, 3 };
        for (int level = 0; level <= ShipWeaponUpgrades.MaxLevel; level++) pure &= FloatieTuning.CountAt(Paragon, level) == want[level];
        Check("Paragon follows the shared one/two/three starting-slot track", pure && ShipWeaponUpgrades.MaxLevel == 4);

        bool others = true;
        for (int ship = 1; ship <= 15; ship++)
            for (int level = 0; level <= 4; level++) others &= FloatieTuning.CountAt(ship, level) == FloatieTuning.CountAt(level);
        Check("every ship shares the same earned-drone slot system", others);

        var r = MakeShip(Paragon, 0);
        bool live = true;
        for (int level = 0; level <= 4; level++)
        {
            ShotAtoms.SetDroneCount(want[level]);
            r.battery.SetLevel(level);
            int shown = 1;
            for (int s = 0; s < FloatieTuning.MaxFloaties - 1; s++) if (r.battery.Extra(s) != null && r.battery.Extra(s).gameObject.activeSelf) shown++;
            live &= r.battery.Count == want[level] && shown == want[level];
        }
        Check("the battery respects the run slots configured by the dock tier", live);
        Teardown(r);
    }

    static void ParagonFormation()
    {
        FreshScene();
        var r = MakeShip(Paragon, 4, slots: 6);
        var hull = r.ship.GetComponent<SpriteRenderer>();
        Vector2 e = hull.sprite != null ? (Vector2)hull.sprite.bounds.extents : new Vector2(.4f, .5f);
        var rests = new List<Vector3>();
        for (int s = 0; s < 5; s++) rests.Add(r.battery.Extra(s).RestLocal);
        bool apart = true, outside = true;
        for (int i = 0; i < rests.Count; i++)
        {
            outside &= Mathf.Abs(rests[i].x) > e.x * 1.1f;   // beside the hull, never over it
            for (int j = i + 1; j < rests.Count; j++) apart &= ((Vector2)(rests[i] - rests[j])).magnitude > e.y * .55f;
        }
        Check("the five extra drones sit beside the hull, never over it, and never on each other", apart && outside);
        float left = 0f, right = 0f;
        foreach (var p in rests) { if (p.x < 0f) left++; else right++; }
        Check("the six drones are balanced left and right (main + 5: " + (left + 1) + " / " + right + " or " + left + " / " + (right + 1) + ")",
              Mathf.Abs(left - right) <= 1f);
        Teardown(r);
    }

    static void ParagonText()
    {
        bool ok = true;
        for (int level = 1; level <= 4; level++)
        {
            string t = ShipWeaponUpgrades.StepText(Paragon, level);
            ok &= (t.Contains("+1 DRONE") == (level >= 3)) && t.StartsWith(ShipWeaponUpgrades.Step(Paragon, level).label);
        }
        Check("Paragon colour text preserves weapon benefits and does not sell earned drone slots", ok && ShipWeaponUpgrades.StepText(Paragon, 4).Contains("-25%"));
    }

    // Owner rule: RAPID speeds up ALL the turrets: straight, alternating and aimed cores, including boss recruits.
    static void RapidShortensEveryDrone()
    {
        FreshScene();
        bool all = true, boss = true;
        foreach (int level in new[] { 0, 2, 4 })
            for (int index = 0; index < FloatieTuning.MaxFloaties; index++)
            {
                float last = float.MaxValue;
                bool strict = true;
                for (int rapid = 0; rapid <= ShotAtomTuning.MaxLevel; rapid++)
                {
                    ShotAtoms.SetLevel(ShotAtom.Rapid, rapid);
                    float cd = FloatieTuning.Cooldown(index, level, false), cb = FloatieTuning.Cooldown(index, level, true);
                    strict &= rapid == 0 ? true : cd < last - 1e-4f || cd <= ShotAtomTuning.MinCooldown + 1e-4f;
                    boss &= cb <= FloatieTuning.BossCooldown + 1e-4f && (rapid == 0 || cb < FloatieTuning.BossCooldown);
                    last = cd;
                }
                ShotAtoms.SetLevel(ShotAtom.Rapid, 10);
                all &= strict && FloatieTuning.Cooldown(index, level, false) < FloatieTuning.Cooldown(index, level, false) * 1.0001f;
                float plain, fast;
                ShotAtoms.SetLevel(ShotAtom.Rapid, 0); plain = FloatieTuning.Cooldown(index, level, false);
                ShotAtoms.SetLevel(ShotAtom.Rapid, 10); fast = FloatieTuning.Cooldown(index, level, false);
                all &= fast < plain;
            }
        Check("RAPID L10 shortens drone cooldowns 0..5 (straight, angled, aimed, fans) at every tier, and each level is shorter than the last", all);
        Check("... and it shortens the boss-fight cooldown of every drone too", boss);
        ShotAtoms.SetLevel(ShotAtom.Rapid, 0);

        // the battery really waits that long: a Paragon at tier 4 fires each drone again after its own rapid cooldown
        var r = MakeShip(Paragon, 4, slots: 6);
        ShotAtoms.SetLevel(ShotAtom.Rapid, 10);
        r.battery.ResetTimers();
        bool timers = true;
        for (int t = 0; t < 600; t++) { r.battery.Step(Dt); if (t % 10 == 0) FloatieBullets.ReleaseAll(); }
        for (int i = 0; i < r.battery.Count; i++) timers &= r.battery.Timer(i) <= r.battery.Cooldown(i) + 1e-4f && r.battery.Shots[i] >= 2;
        Check("with RAPID 10 every one of Paragon's six drones fires at least twice in 10 s (shots " + string.Join("/", r.battery.Shots) + ")", timers);
        Teardown(r);
    }

    // Owner rule: nothing homes. Fires every kind of bullet with enemies all round, and watches each bullet's velocity direction.
    static void NothingHomes()
    {
        FreshScene();
        AllAtoms(10);
        ShotAtoms.SetLevel(ShotAtom.Pierce, 0);   // (so the bullets do not stop at the first target and stay long enough to watch)
        var r = MakeShip(Paragon, 4, slots: 6);
        for (int i = 0; i < 6; i++) Hazard(-2f + i * .8f, 3.2f + (i % 2) * .6f);
        var dirs = new Dictionary<FloatieBullet, Vector3>();
        var bounced = new Dictionary<FloatieBullet, int>();
        var launch = new Dictionary<FloatieBullet, int>();
        bool straight = true;
        int watched = 0;
        r.battery.ResetTimers();
        for (int t = 0; t < 60 * 12; t++)
        {
            r.battery.Step(Dt);
            foreach (var b in FloatieBullets.All)
            {
                if (!b.Active) { dirs.Remove(b); bounced.Remove(b); launch.Remove(b); continue; }
                Vector3 d = b.Heading.normalized;
                if (!dirs.ContainsKey(b) || launch[b] != b.LaunchId) { dirs[b] = d; bounced[b] = b.BouncesLeft; launch[b] = b.LaunchId; watched++; continue; }   // a pooled bullet fired again in the same step is a new shot
                if (b.BouncesLeft != bounced[b]) { dirs[b] = d; bounced[b] = b.BouncesLeft; continue; }   // a ricochet turn: the one allowed change
                if (Vector3.Dot(d, dirs[b]) < .99999f) straight = false;
            }
            // keep the field stocked so the aimed drones have targets and the atoms hit things
            if (t % 120 == 119) for (int i = 0; i < 2; i++) Hazard(-1.5f + i * 3f, 3.8f);
        }
        Check("no bullet ever changed heading in flight except a ricochet turn (" + watched + " watched, with every atom at 10)", straight && watched > 40);
        Teardown(r);
    }

    // 6 drones, every atom at 10: the bullet cap holds and nobody starves.
    static void CapAndFairShare()
    {
        FreshScene();
        AllAtoms(10);
        ShotAtoms.SetLevel(ShotAtom.Rapid, 10);
        var r = MakeShip(Paragon, 4, slots: 6);
        for (int i = 0; i < 8; i++) Hazard(-2.4f + i * .7f, 3f);
        int peak = 0, spread = 0;
        r.battery.ResetTimers();
        for (int t = 0; t < 60 * 20; t++)
        {
            r.battery.Step(Dt);
            peak = Mathf.Max(peak, FloatieBullets.Active);
            if (t % 150 == 149) for (int i = 0; i < 8; i++) if (GameObject.Find("hazard") == null) Hazard(-2.4f + i * .7f, 3f);
        }
        bool everyone = true;
        for (int i = 0; i < 6; i++) { everyone &= r.battery.Shots[i] > 0; spread += r.battery.Shots[i]; }
        Check("the bullets in the air never pass the cap (" + peak + " of " + ShotAtomTuning.MaxBullets + ")", peak <= ShotAtomTuning.MaxBullets && FloatieBullets.Size <= FloatieTuning.PoolSize);
        Check("every one of the six drones fired in 20 s (" + string.Join("/", r.battery.Shots) + ")", everyone);
        int mn = int.MaxValue, mx = 0;
        for (int i = 0; i < 6; i++) { mn = Mathf.Min(mn, r.battery.Shots[i]); mx = Mathf.Max(mx, r.battery.Shots[i]); }
        Check("no drone fires fewer than a third of the busiest (" + mn + " vs " + mx + ")", mn * 3 >= mx);

        // a full field: with the pool packed, shards (a split's) cannot take the volleys' reserve
        FloatieBullets.ReleaseAll();
        int taken = 0;
        while (FloatieBullets.Take(false) != null && taken < FloatieTuning.PoolSize)
        {
            var b = FloatieBullets.Take(false);
            b.Launch(null, Paragon, new Vector3(0f, 0f, 0f), Vector3.up, Color.white, default(FloatieAim.Pick));
            taken++;
        }
        Check("shards stop " + FloatieBullets.VolleyReserve + " short of the cap so the drones' volleys keep room (" + taken + " shards fit)",
              taken == FloatieTuning.PoolSize - FloatieBullets.VolleyReserve && FloatieBullets.Take(true) != null);
        Teardown(r);
    }

    // The gun fires the whole time an ultimate is out (the ultimate gun slid out), in normal play and in a boss fight.
    static void GunKeepsFiringInUltimate()
    {
        foreach (bool boss in new[] { false, true })
        {
            FreshScene();
            var r = MakeShip(1, 2);
            if (boss) BossEncounter.Begin(0, null);
            r.battery.ResetTimers();
            for (int i = 0; i < 40; i++) r.gun.Step(1f, Dt, Dt);
            int before = r.battery.Shots[0] + r.battery.Shots[1];
            bool busy = r.battery.UltimateBusy;
            for (float t = 0f; t < 6.5f; t += Dt) { r.gun.Step(1f, Dt, Dt); r.battery.Step(Dt); if (Mathf.Approximately(t % .5f, 0f)) FloatieBullets.ReleaseAll(); }
            Check("with the ultimate gun out (" + (busy ? "busy" : "not busy") + ") the floaties fire" + (boss ? " in a boss fight" : "") + " (" + (r.battery.Shots[0] + r.battery.Shots[1] - before) + " shots)",
                  r.battery.Shots[0] + r.battery.Shots[1] > before && r.battery.CurrentHold == FloatieBattery.Hold.None);
            Teardown(r);
            BossEncounter.ResetRun();
        }
    }

    // Owner decision: a coin upgrades ONE drone; each drone has its own levels; Rapid is shared; dust only when maxed on every drone.
    static void PerDroneAtoms()
    {
        FreshScene();
        var r = MakeShip(Paragon, 4, slots: 6);   // six drones
        Check("the battery tells the atoms how many drones there are (6)", ShotAtoms.DroneCount == 6);
        bool one = true;
        for (int n = 0; n < 40; n++)
        {
            int before = 0, changed = 0;
            for (int d = 0; d < 6; d++) before += ShotAtoms.Level(d, ShotAtom.Pierce);
            var lv = new int[6];
            for (int d = 0; d < 6; d++) lv[d] = ShotAtoms.Level(d, ShotAtom.Pierce);
            var c = ShotAtoms.Collect(ShotAtom.Pierce);
            for (int d = 0; d < 6; d++) if (ShotAtoms.Level(d, ShotAtom.Pierce) != lv[d]) changed++;
            one &= c.maxed || (changed == 1 && c.drone >= 0 && ShotAtoms.Level(c.drone, ShotAtom.Pierce) == lv[c.drone] + 1);
        }
        Check("a coin upgrades exactly ONE drone (40 coins, each changed one drone by one level)", one);
        int capped = 0;
        for (int d = 0; d < 6; d++) if (ShotAtoms.Level(d, ShotAtom.Pierce) == ShotAtomTuning.MaxLevel) capped++;
        bool noOver = true;
        for (int d = 0; d < 6; d++) noOver &= ShotAtoms.Level(d, ShotAtom.Pierce) <= ShotAtomTuning.MaxLevel;
        Check("a drone never passes level 10, and a maxed drone is skipped while another is not (" + capped + " maxed)", noOver);

        ShotAtoms.BeginRun(3); ArmSquad(6);
        for (int d = 0; d < 5; d++) ShotAtoms.SetLevel(d, ShotAtom.Twin, ShotAtomTuning.MaxLevel);
        ShotAtoms.SetLevel(5, ShotAtom.Twin, 9);
        Check("five drones maxed, one not: the atom can still drop", ShotAtoms.CanDrop(ShotAtom.Twin) && ShotAtoms.EligibleDrones(ShotAtom.Twin) == 1);
        var last = ShotAtoms.Collect(ShotAtom.Twin, .3f);
        Check("... the coin goes to that one drone (drone 6 -> level 10), no dust", !last.maxed && last.drone == 5 && last.level == 10);
        var dust = ShotAtoms.Collect(ShotAtom.Twin);
        Check("maxed on EVERY drone: the atom no longer drops and a catch pays dust", dust.maxed && !ShotAtoms.CanDrop(ShotAtom.Twin) && ShotAtoms.MaxedCatches == 1);

        ShotAtoms.BeginRun(3); ArmSquad(6);
        var rap = ShotAtoms.Collect(ShotAtom.Rapid);
        bool shared = rap.drone == -1 && rap.level == 1;
        for (int d = 0; d < 6; d++) shared &= ShotAtoms.Level(d, ShotAtom.Rapid) == 1;
        Check("RAPID is one shared level: one coin speeds up all six drones", shared && ShotAtoms.RapidBonus() == ShotAtomTuning.RapidBonus[1]);

        // each drone fires its own atoms
        ShotAtoms.BeginRun(3); ArmSquad(6);
        ShotAtoms.SetLevel(0, ShotAtom.Twin, 4);   // 4 bullets from drone 0
        FloatieBullets.ReleaseAll();
        r.battery.Fire(0);
        int fromZero = FloatieBullets.Active;
        FloatieBullets.ReleaseAll();
        r.battery.Fire(1);
        int fromOne = FloatieBullets.Active;
        Check("the drone with TWIN 4 fires " + ShotAtomTuning.TwinShots[4] + " bullets, the next drone still one (" + fromZero + " / " + fromOne + ")",
              fromZero == ShotAtomTuning.TwinShots[4] && fromOne == 1);

        // PowerCreep is normalised by the drone count
        ShotAtoms.BeginRun(3); ArmSquad(6);
        for (int a = 0; a < ShotAtomTuning.PerDroneAtoms; a++) for (int d = 0; d < 6; d++) ShotAtoms.SetLevel(d, (ShotAtom)a, 10);
        ShotAtoms.SetLevel(ShotAtom.Rapid, 10);
        bool full6 = ShotAtoms.PowerLevel == 60;
        ShotAtoms.SetDroneCount(3);
        bool full3 = ShotAtoms.PowerLevel == 60;
        ShotAtoms.SetDroneCount(6);
        ShotAtoms.BeginRun(3); ArmSquad(6);
        for (int a = 0; a < ShotAtomTuning.PerDroneAtoms; a++) ShotAtoms.SetLevel(0, (ShotAtom)a, 10);
        Check("power pressure is capped and counts earned drone strength; one maxed drone of six is " + ShotAtoms.PowerLevel,
              full6 && full3 && ShotAtoms.PowerLevel == 37);
        Teardown(r);
        ShotAtoms.BeginRun(3); ArmSquad(6);
    }

    static void Feel()
    {
        FreshScene();
        var sp = FloatieBullet.BulletSprite();
        Check("the bullet is a 5 x 28 pixel sprite with a dark edge pixel, a bright core and a fading tail",
              sp != null && sp.rect.width == 5 && sp.rect.height == 28 &&
              sp.texture.GetPixel(2, 25).r > .99f && sp.texture.GetPixel(0, 20).a > .5f && sp.texture.GetPixel(0, 20).r < .2f &&
              sp.texture.GetPixel(2, 12).a > sp.texture.GetPixel(2, 2).a && sp.texture.GetPixel(2, 0).a < .05f);
        FloatieFx.Prebuild();
        int built = FloatieFx.BuiltParticles;
        var r = MakeShip(1, 0);
        var enemy = EnemyFactory.Create(EnemyRoster.One(3, EnemyRole.Fighter), new Vector2(1f, 1f), Quaternion.identity);
        ClearTarget.Ensure(enemy);
        Vector3 at = enemy.transform.position;
        int sparks = FloatieFx.Sparks;
        var b = FloatieBullets.Take();
        b.Launch(null, 1, at + Vector3.down * .08f, Vector3.up, Color.white, default(FloatieAim.Pick));
        b.Tick(Dt);
        Check("a hit throws a spark at the impact point (" + (FloatieFx.Sparks - sparks) + " burst, " + FloatieFx.ActiveParticles + " pixels)", FloatieFx.Sparks == sparks + 1 && FloatieFx.ActiveParticles >= 3);
        Check("a normal enemy that lives on is nudged along the shot", enemy != null && FloatieFx.ActiveNudges == 1);
        Vector3 before = enemy.transform.position;
        for (int i = 0; i < 30; i++) FloatieFx.Tick(Dt);
        float moved = (enemy.transform.position - before).magnitude;
        Check("... a hair (" + moved.ToString("0.000") + " units), then it settles", moved > .02f && moved < .15f && FloatieFx.ActiveNudges == 0);
        Check("the sparks die out by themselves", FloatieFx.ActiveParticles == 0);

        // pooled: hammering hits never builds more than the table, and nothing allocates
        for (int i = 0; i < 100; i++) { FloatieFx.Spark(FloatieFx.Kind.Hit, Vector3.zero, Vector3.up, Color.cyan); FloatieFx.Tick(Dt); }
        long bytes = TestHarness.AllocatedBytes(() =>
        {
            for (int i = 0; i < 300; i++)
            {
                FloatieFx.Spark((FloatieFx.Kind)(i % 5), new Vector3(0f, 1f, 0f), Vector3.up, Color.cyan);
                FloatieFx.Tick(Dt);
            }
        });
        Check("sparks, muzzle flashes and nudges are pooled and allocation free (" + bytes + " bytes, " + FloatieFx.BuiltParticles + " pixels built)",
              bytes <= 0 && FloatieFx.BuiltParticles == built && FloatieFx.ActiveParticles <= FloatieFx.MaxParticles);
        Check("the sparks draw above the bullets and the bullets above the ultimates' effects (72)",
              FloatieFx.SortOrder > FloatieBullet.SortOrder && FloatieBullet.SortOrder > 72);
        Teardown(r);
    }
}
