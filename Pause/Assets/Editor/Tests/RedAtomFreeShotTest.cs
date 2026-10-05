using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Feature: picking up a red (pause) atom fires the ship's main weapon once,
// for free -- every ship, at its weapon level -- without touching the charge
// timer (ShipPowerController.FreeShot). The atom still pays what it always
// did (+2 pauses, the atom score, the usual cut of the countdown). A free shot
// waits rather than break an attack in progress, the ultimate's slide-out,
// the cinematic, or a frozen world; the top tier fires a quick volley with no
// slow motion; kills score like any weapon kill.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod RedAtomFreeShotTest.Run
public static class RedAtomFreeShotTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[RA] PASS  " : "[RA] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    const BindingFlags Stat = BindingFlags.NonPublic | BindingFlags.Static;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            RedAtomCutsFiveSeconds();
            EveryShipFiresOnceOnPickup();
            QueuedWhileMidFire();
            QueuedThroughTheUltimateSlideOut();
            QueuedThroughTheCinematic();
            PausedAndDeadBehaviour();
            QuickVolleyKeepsTheBossRule();
            FreeShotKillsScore();
            NoAllocations();
        }
        finally
        {
            RunScore.EndRun(RunScore.RunId);
            AttackPool.StopAll();
            WorldTimeFx.Reset();
            Time.timeScale = 1f;
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
        }
        Debug.Log("[RA] failures: " + fails);
        return fails;
    }

    // ---- fixtures ------------------------------------------------------

    static void SetLevel(int id, int level)
    {
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        for (int n = 1; n < ShipSkins.PerShip; n++)
        {
            if (n <= level) PlayerPrefs.SetInt(ShipSkins.OwnedKey(id, n), 1);
            else PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(id, n));
        }
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

    class Rig
    {
        public ShipPowerController c;
        public collisionDetection cd;
        public Action<Collider2D> trigger;
    }

    // The flying ship: its power controller (gameS1 attaches it) plus the
    // collisionDetection that collects pickups.
    static Rig Ship(int id)
    {
        PlayerPrefs.SetInt("spawnShip", id);
        var go = new GameObject("ship" + id, typeof(SpriteRenderer));
        go.transform.position = new Vector3(0f, -3f, 0f);
        var r = new Rig();
        r.cd = go.AddComponent<collisionDetection>();
        r.cd.explosionAnimation = new GameObject("~TestExplosion");
        r.cd.boostSound = go.AddComponent<AudioSource>();
        r.cd.boostText = new GameObject("~boostText", typeof(RectTransform)).AddComponent<Text>();
        r.cd.hypeText = new GameObject("~hypeText", typeof(RectTransform)).AddComponent<Text>();
        r.cd.boost = new GameObject("~boost");
        r.cd.boost.SetActive(false);
        r.trigger = (Action<Collider2D>)Delegate.CreateDelegate(typeof(Action<Collider2D>), r.cd,
            typeof(collisionDetection).GetMethod("OnTriggerEnter2D", Inst));
        r.c = go.AddComponent<ShipPowerController>();
        r.c.SendMessage("Awake");
        r.c.SendMessage("Start");
        var gun = go.GetComponentInChildren<UltimateGun>();
        if (gun != null) gun.SendMessage("Awake");
        return r;
    }

    static void PickUpRedAtom(Rig r)
    {
        var atom = new GameObject("pauseAtom(Clone)", typeof(CircleCollider2D), typeof(SpriteRenderer));
        atom.tag = "pickUp";
        r.trigger(atom.GetComponent<Collider2D>());
        if (PickupBurst.LastPlayed != null) PickupBurst.LastPlayed.Finish();
        if (atom != null) Object.DestroyImmediate(atom);
    }

    static void Teardown(Rig r)
    {
        if (r == null || r.c == null) return;
        var c = r.c;
        c.SendMessage("OnDestroy");
        if (c.Runner != null) c.Runner.SendMessage("OnDestroy");
        if (c.Secret != null) c.Secret.SendMessage("OnDestroy");
        Object.DestroyImmediate(c.gameObject);
        AttackPool.StopAll();
        collisionDetection.cloakTimer = 0f;
        collisionDetection.lifeCounter = 0;
        typeof(ShipPowerController).GetMethod("FinishCinematic", Stat).Invoke(null, null);
    }

    static void SetTimer(ShipPowerController c, float seconds) =>
        typeof(ShipPowerController).GetField("timer", Inst).SetValue(c, seconds);

    static float Cooldown(ShipPowerController c) =>
        (float)typeof(ShipPowerController).GetField("cooldown", Inst).GetValue(c);

    static void Steps(ShipAttackRunner r, float seconds, float dt = .02f)
    {
        for (float t = 0f; t < seconds; t += dt) r.Step(dt);
    }

    // What one firing launches as projectiles at this loadout.
    static int ExpectedLaunches(ShipLoadout l, int inView)
    {
        switch (l.attack)
        {
            case ShipAttack.ScreenClear: return ShipAttackRunner.QuickVolleyShots(inView);
            case ShipAttack.Ricochet: return 1;
            case ShipAttack.SeekerEyes: return l.shots;
            case ShipAttack.RailSlug:
            case ShipAttack.ChainLightning:
            case ShipAttack.Blowtorch:
            case ShipAttack.TractorBeam:
            case ShipAttack.OrbitDisc:
                return 0;
            default: return Mathf.Max(1, l.shots);
        }
    }

    static bool Held(ShipAttack a) =>
        a == ShipAttack.Blowtorch || a == ShipAttack.TractorBeam || a == ShipAttack.OrbitDisc;

    // ---- cases ---------------------------------------------------------

    static void EveryShipFiresOnceOnPickup()
    {
        int shipsOk = 0, timerOk = 0, effectOk = 0, untouched = 0, countOk = 0, cueOk = 0;
        int cases = 0;
        string bad = "";
        foreach (int id in ShipId.All)
        {
            foreach (int lv in new[] { 0, ShipWeaponUpgrades.MaxLevel })
            {
                cases++;
                SetLevel(id, lv);
                FreshScene();
                var r = Ship(id);
                var c = r.c;
                var l = ShipWeaponUpgrades.LoadoutAt(id, lv);
                Hazard(-1.5f, 2f);
                Hazard(1.5f, 2.5f);
                Hazard(.3f, -1.5f);
                int inView = ShipTargets.CountOnScreen(false);

                SetTimer(c, 20f);
                float cooldown = Cooldown(c);
                int pauses = score.pauseCounter;
                int atoms = collisionDetection.pauseAtomPickups;
                int launches = AttackProjectile.LaunchCount;
                int sprites = AttackPool.ActiveSprites;
                int kills = ShipAttackHits.Kills;
                float scale = Time.timeScale;

                PickUpRedAtom(r);

                int launchedNow = AttackProjectile.LaunchCount - launches;
                int spritesNow = AttackPool.ActiveSprites - sprites;
                bool once = c.FreeShotsFired == 1 && c.Runner.FreeFireCount == 1 && c.PendingFreeShots == 0 &&
                            c.Runner.FireCount == 0;
                if (once) shipsOk++; else bad += " once:" + id + "/" + lv;

                // the timer: only the red atom's usual cut, nothing from the shot
                bool timer = Mathf.Approximately(c.SecondsLeft, 20f - c.secondsPerRedAtom) &&
                             Mathf.Approximately(Cooldown(c), cooldown);
                if (timer) timerOk++; else bad += " timer:" + id + "/" + lv;

                bool effect = score.pauseCounter == pauses + 2 && collisionDetection.pauseAtomPickups == atoms + 1;
                if (effect) effectOk++; else bad += " effect:" + id + "/" + lv;

                bool calm = c.Indicator.ReleaseCount == 0 && !ShipPowerController.CinematicClearActive &&
                            Mathf.Approximately(Time.timeScale, scale);
                if (calm) untouched++; else bad += " calm:" + id + "/" + lv;

                bool cue = c.Indicator.FreeFlashCount == 1 && c.Indicator.FreeFlashing;
                if (cue) cueOk++; else bad += " cue:" + id + "/" + lv;

                // what went out matches the loadout at this level
                bool count;
                if (l.attack == ShipAttack.RailSlug)
                    count = spritesNow == 2 * Mathf.Max(1, l.shots) && launchedNow == 0;
                else if (l.attack == ShipAttack.ChainLightning)
                {
                    Steps(c.Runner, 1f);
                    int k = ShipAttackHits.Kills - kills;
                    count = k >= 1 && k <= l.maxHits;
                }
                else if (Held(l.attack))
                {
                    bool on = c.Runner.ActiveRuns == 1;
                    Steps(c.Runner, l.duration - .1f);
                    bool still = c.Runner.ActiveRuns == 1;
                    Steps(c.Runner, .2f);
                    count = on && still && c.Runner.ActiveRuns == 0 && launchedNow == 0;
                }
                else
                {
                    Steps(c.Runner, 1.6f);
                    int n = AttackProjectile.LaunchCount - launches;
                    count = n == ExpectedLaunches(l, inView);
                    if (!count) bad += " n=" + n + "/" + ExpectedLaunches(l, inView);
                }
                if (count) countOk++; else bad += " count:" + id + "/" + lv;

                // the next auto-fire is still where it was
                timer = Mathf.Approximately(c.SecondsLeft, 20f - c.secondsPerRedAtom);
                if (!timer) { timerOk--; bad += " timer-after:" + id + "/" + lv; }

                Teardown(r);
            }
            SetLevel(id, 0);
        }
        if (bad.Length > 0) Debug.Log("[RA] per-ship issues:" + bad);
        Check("every ship (15) at levels 0 and 4 fires exactly once on a red atom (" + shipsOk + "/" + cases + ")",
              shipsOk == cases && cases == 30);
        Check("the free shot leaves the countdown and cooldown alone (only the atom's usual cut) (" + timerOk + "/" + cases + ")",
              timerOk == cases);
        Check("the red atom still gives +2 pauses and counts as a pause atom (" + effectOk + "/" + cases + ")",
              effectOk == cases);
        Check("no ultimate release, no cinematic, no slow motion (" + untouched + "/" + cases + ")", untouched == cases);
        Check("the charge indicator flashes red for the free shot (" + cueOk + "/" + cases + ")", cueOk == cases);
        Check("what went out matches each loadout at its level (" + countOk + "/" + cases + ")", countOk == cases);
    }

    // The red atom's countdown cut is 5 s (the violet capacitor is the
    // dedicated charge-cutter now); the green heal atom keeps the shared 7 s.
    static void RedAtomCutsFiveSeconds()
    {
        FreshScene();
        var r = Ship(4);
        var c = r.c;
        Check("the red atom's cut is 5 s, the shared atom cut stays 7 s",
              Mathf.Approximately(c.secondsPerRedAtom, 5f) && Mathf.Approximately(c.secondsPerAtom, 7f));
        SetTimer(c, 20f);
        PickUpRedAtom(r);
        Check("picking up a red atom cuts exactly 5 s (" + c.SecondsLeft + ")",
              Mathf.Approximately(c.SecondsLeft, 15f));
        SetTimer(c, 20f);
        var heal = new GameObject(HealAtom.ObjectName + "(Clone)", typeof(CircleCollider2D), typeof(SpriteRenderer));
        heal.tag = "pickUp";
        r.trigger(heal.GetComponent<Collider2D>());
        if (PickupBurst.LastPlayed != null) PickupBurst.LastPlayed.Finish();
        if (heal != null) Object.DestroyImmediate(heal);
        Check("a green heal atom still cuts 7 s (" + c.SecondsLeft + ")",
              Mathf.Approximately(c.SecondsLeft, 13f));
        Teardown(r);
    }

    static void QueuedWhileMidFire()
    {
        foreach (int id in new[] { 9, 12, 13, 15 })
        {
            FreshScene();
            var r = Ship(id);
            var c = r.c;
            SetTimer(c, 20f);
            Hazard(-1.5f, 3f);
            c.Runner.Fire();   // the ultimate's attack, in progress
            Steps(c.Runner, .1f);
            bool busy = c.Runner.ActiveRuns == 1;
            PickUpRedAtom(r);
            bool queued = c.PendingFreeShots == 1 && c.Runner.FreeFireCount == 0 && c.Runner.ActiveRuns == 1;
            for (int i = 0; i < 10; i++) c.ServiceFreeShots(true);
            bool held = c.PendingFreeShots == 1 && c.Runner.FreeFireCount == 0;
            // the attack runs its full course, unbroken
            var l = c.Runner.Loadout;
            int frames = 0;
            while (c.Runner.ActiveRuns > 0 && frames < 1000) { c.Runner.Step(.02f); c.ServiceFreeShots(true); frames++; }
            bool fired = c.Runner.FreeFireCount == 1 && c.PendingFreeShots == 0 && c.FreeShotsFired == 1;
            bool timer = Mathf.Approximately(c.SecondsLeft, 20f - c.secondsPerRedAtom);
            Check(l.attackName + ": a free shot mid-fire waits, never breaks the attack, then goes right after",
                  busy && queued && held && fired && timer && frames * .02f >= (Held(l.attack) ? l.duration - .2f : 0f));
            Teardown(r);
        }
    }

    static void QueuedThroughTheUltimateSlideOut()
    {
        FreshScene();
        var r = Ship(4);
        var c = r.c;
        SetTimer(c, 5f + .6f);   // the red atom's 5 s cut lands it inside the slide-out
        PickUpRedAtom(r);
        Check("in the ultimate's slide-out the free shot queues",
              c.PendingFreeShots == 1 && c.Runner.FreeFireCount == 0 && Mathf.Approximately(c.SecondsLeft, .6f));
        SetTimer(c, 0f);
        score.pauseCounter = 0;    // the world runs without a finger (no pauses banked)
        c.SendMessage("Update");   // the ultimate fires, then the queued free shot right after
        Check("the ultimate fires on time and the free shot follows in the same frame",
              c.Runner.FireCount == 1 && c.Runner.FreeFireCount == 1 && c.PendingFreeShots == 0);
        Check("the free shot doesn't touch the fresh countdown",
              Mathf.Approximately(c.SecondsLeft, Cooldown(c)) && c.Indicator.ReleaseCount == 1);
        Teardown(r);
    }

    static void QueuedThroughTheCinematic()
    {
        FreshScene();
        var r = Ship(7);
        var c = r.c;
        Hazard(0f, 2f);
        SetTimer(c, 0f);
        c.SendMessage("Update");   // the ultimate: the cinematic volley
        bool cine = ShipPowerController.CinematicClearActive;
        c.FreeShot();
        for (int i = 0; i < 5; i++) c.ServiceFreeShots(true);
        bool waits = c.PendingFreeShots == 1 && c.Runner.FreeFireCount == 0;
        typeof(ShipPowerController).GetMethod("FinishCinematic", Stat).Invoke(null, null);
        int launches = AttackProjectile.LaunchCount;
        c.ServiceFreeShots(true);
        Check("a top-tier free shot waits out the cinematic, then fires its quick volley",
              cine && waits && c.Runner.FreeFireCount == 1 && AttackProjectile.LaunchCount > launches);
        Check("... without starting another cinematic", !ShipPowerController.CinematicClearActive);
        Teardown(r);
    }

    static void PausedAndDeadBehaviour()
    {
        FreshScene();
        var r = Ship(14);
        var c = r.c;
        SetTimer(c, 20f);
        Time.timeScale = 0f;
        int launches = AttackProjectile.LaunchCount;
        c.FreeShot();
        c.ServiceFreeShots(false);
        c.Runner.Step(ShipAttackRunner.ScaledDelta());
        Check("frozen world: the free shot is held, nothing launches",
              c.PendingFreeShots == 1 && AttackProjectile.LaunchCount == launches && c.Runner.FreeFireCount == 0);
        Time.timeScale = 1f;
        c.ServiceFreeShots(true);
        Check("it goes as soon as the world runs again",
              c.PendingFreeShots == 0 && c.Runner.FreeFireCount == 1 && AttackProjectile.LaunchCount > launches &&
              Mathf.Approximately(c.SecondsLeft, 20f));
        Steps(c.Runner, 1f);

        SetTimer(c, .5f);
        for (int i = 0; i < 6; i++) c.FreeShot();
        Check("queued free shots are capped (" + c.PendingFreeShots + ")",
              c.PendingFreeShots == ShipPowerController.MaxPendingFreeShots);
        buttonClicks.playerDied = true;
        c.ServiceFreeShots(true);
        Check("death drops the queue", c.PendingFreeShots == 0 && c.Runner.FreeFireCount == 1);
        c.FreeShot();
        Check("no free shot once dead", c.PendingFreeShots == 0 && c.Runner.FreeFireCount == 1);
        buttonClicks.playerDied = false;
        Teardown(r);
    }

    class FakeBoss : MonoBehaviour, IShipAttackTarget
    {
        public float weight;
        public int hits;
        public void TakeShipAttack(int ship, float w, Vector3 at) { weight += w; hits++; }
    }

    static void QuickVolleyKeepsTheBossRule()
    {
        foreach (int id in new[] { 5, 6, 7 })
        {
            FreshScene();
            var r = Ship(id);
            var c = r.c;
            var bossGo = Hazard(0f, .5f);
            bossGo.GetComponent<ClearTarget>().SetRadius(1.2f);
            var boss = bossGo.AddComponent<FakeBoss>();
            // everything else sits behind the boss: every shot crosses it
            for (int i = 0; i < 6; i++) Hazard(-.6f + i * .24f, 3.2f + (i % 2) * .4f);
            SetTimer(c, 20f);
            c.FreeShot();
            Steps(c.Runner, 3f);
            Check(ShipLoadoutTable.For(id).attackName + " free volley: the boss takes at most one full hit (" +
                  boss.weight + " over " + boss.hits + ")",
                  boss != null && boss.hits >= 1 && boss.weight <= 1f + 1e-4f &&
                  !ShipPowerController.CinematicClearActive);
            Teardown(r);
        }
    }

    static GameObject Enemy(EnemyRole role, Vector3 at)
    {
        var def = EnemyRoster.One(0, role);
        var go = EnemyFactory.Create(def, at, Quaternion.identity);
        ClearTarget.Ensure(go);
        return go;
    }

    static void FreeShotKillsScore()
    {
        FreshScene();
        RunScore.EndRun(RunScore.RunId);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
        moveBackGround.speed = 0f;
        RunScore.BeginRun(true, true);
        var r = Ship(4);   // rail: instant, so the kill lands on the pickup frame
        var c = r.c;
        SetTimer(c, 20f);
        Enemy(EnemyRole.Rock, new Vector3(0f, 1f, 0f));
        var parts = RunScore.Parts;
        long total = RunScore.Total;
        int rocks = AchievementTiers.Count(AchievementCategory.Asteroids);
        int scored = 0; RunScore.Source src = RunScore.Source.Distance;
        Action<int, Vector3, RunScore.Source> spy = (p, w, s) => { if (s == RunScore.Source.Kill) { scored = p; src = s; } };
        RunScore.Scored += spy;
        try { c.FreeShot(); }
        finally { RunScore.Scored -= spy; }
        int rock = ScoreRules.KillPoints(EnemyRole.Rock, EnemyRoster.One(0, EnemyRole.Rock).tier);
        Check("a free-shot kill pays kill points (" + rock + ", got " + (RunScore.Parts.kills - parts.kills) + ")",
              RunScore.Parts.killCount == parts.killCount + 1 && RunScore.Parts.kills - parts.kills == rock &&
              RunScore.Total > total);
        Check("... raised as a kill for the HUD popup", scored == rock && src == RunScore.Source.Kill);
        Check("... counts toward the asteroid achievements",
              AchievementTiers.Count(AchievementCategory.Asteroids) == rocks + 1);

        Enemy(EnemyRole.Rock, new Vector3(0f, 2f, 0f));
        c.FreeShot();
        Check("free-shot kills build the kill chain (" + RunScore.Chain + ")", RunScore.Chain == 2);
        RunScore.EndRun(RunScore.RunId);
        Teardown(r);
    }

    static void NoAllocations()
    {
        foreach (int id in new[] { 14, 4, 13, 5 })
        {
            FreshScene();
            var r = Ship(id);
            var c = r.c;
            SetTimer(c, 30f);
            Hazard(2.2f, 4.6f);
            // warm up: pools, sound clip, loadout key cache
            for (int i = 0; i < 3; i++)
            {
                c.FreeShot();
                Steps(c.Runner, 4f);
                c.Indicator.Step(.02f, .4f);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10; i++)
            {
                c.FreeShot();
                for (int f = 0; f < 200; f++) { c.Runner.Step(.02f); c.ServiceFreeShots(true); c.Indicator.Step(.02f, .02f); }
            }
            long fired = GC.GetAllocatedBytesForCurrentThread() - before;

            // a queued shot waiting on the slide-out, frame after frame
            SetTimer(c, .5f);
            c.FreeShot();
            before = GC.GetAllocatedBytesForCurrentThread();
            for (int f = 0; f < 500; f++) c.ServiceFreeShots(true);
            long waiting = GC.GetAllocatedBytesForCurrentThread() - before;

            Check(ShipLoadoutTable.For(id).attackName + ": free shots allocate nothing (" + fired + " B firing, " +
                  waiting + " B waiting)", fired == 0 && waiting == 0 && c.Runner.FreeFireCount == 13);
            Teardown(r);
        }
    }
}
