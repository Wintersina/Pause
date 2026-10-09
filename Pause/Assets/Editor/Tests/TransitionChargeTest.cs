using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The weapon charge holds through every world transition
// (WorldTransition.InProgress, ShipPowerController.TransitionFrozen):
//
//   1  outside a transition nothing changed: the charge ticks on running
//      time, pickups cut it, it fires at zero
//   2  the portal (and the loop portal): from the moment it opens until the
//      ship flies through, the charge doesn't tick, pickups don't cut it,
//      nothing fires (no ultimate, no red-atom free shot, the gun tucked
//      in); after, the same charge carries on and fires as usual
//   3  the planetfall: the approach and the whole descent (commit, shroud,
//      the switch, the breakthrough) until control returns
//   4  the lift-off (Frost's, Verdant's and Ember's): the beat, the rise,
//      the interlude and the gateway after (Verdant's planetfall after
//      Frost, Ember's after Verdant, flown down until control returns);
//      after Ember's no portal: held to the interlude's last frame, then
//      Space's level starts (the loop) and the charge carries on at once
//   5  the boss: the fight charges as normal; its end opens the portal and
//      the charge holds from there
//   6  the signal can't stick: a scene reload, a dead pilot's reload, a
//      dev skip, a torn-down sequence or manager all clear it
//   7  reading it allocates nothing
//
//   scripts/unity-batch.sh -projectPath Pause -executeMethod AllTests.RunSuites -suites TransitionChargeTest
public static class TransitionChargeTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[TCHARGE] PASS  " : "[TCHARGE] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
    const BindingFlags Stat = BindingFlags.NonPublic | BindingFlags.Static;
    const float Dt = 1f / 60f;
    const float Held = .5f;   // a charge inside the gun's slide-out: it would fire within half a second
    static int frame;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            OutsideTransitions();
            Portals();
            PlanetfallDescent();
            LiftoffClimb(1);
            LiftoffClimb(2);
            LiftoffClimb(3);
            BossKillToPortal();
            NeverSticks();
            NoAllocations();
        }
        finally
        {
            Gone();
            LiftoffCatalog.Enabled = true;
            PlanetfallCatalog.Defs = PlanetfallCatalog.All;
            AttackPool.StopAll();
            typeof(ShipPowerController).GetMethod("FinishCinematic", Stat).Invoke(null, null);
            PortalPressure.Reset();
            ShipStartSpeed.EquippedHudOverride = null;
            SpeedRamp.FrameOverride = null;
            SpeedRamp.DeltaOverride = null;
            SpeedRamp.ResetFrameGuard();
            SpeedRamp.ResetBoost();
            ResumeSlowMo.ClockOverride = null;
            ResumeSlowMo.ResetRun();
            BossEncounter.ResetRun();
            RunLoop.Reset();
            buttonClicks.playerDied = false;
            score.pauseCounter = 0;
            moveBackGround.speed = 0f;
            Time.timeScale = 1f;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
        Debug.Log("[TCHARGE] failures: " + fails);
        return fails;
    }

    // ---- fixtures (PlanetfallTest's / LiftoffTest's) ---------------------------

    static void FreshScene(int world)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        RunLoop.Reset();
        PortalPressure.Reset();
        AttackPool.StopAll();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.aspect = 1080f / 2340f;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, 1080, 2340);
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        startMenu.youAreInTutorial = false;
        score.pauseCounter = 0;
        moveBackGround.speed = .37f;
        Time.timeScale = 1f;
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        PlayerPrefs.SetInt("spawnShip", 0);
        ShipStartSpeed.EquippedHudOverride = () => ShipStartSpeed.StockHud;
        frame = 90000;
        SpeedRamp.FrameOverride = () => frame;
        SpeedRamp.DeltaOverride = () => Dt;
        SpeedRamp.ResetFrameGuard();
        SpeedRamp.ResetBoost();
        ResumeSlowMo.ClockOverride = () => frame * Dt;
        ResumeSlowMo.ResetRun();
    }

    static WorldManager World()
    {
        var wm = new GameObject("~WorldManager").AddComponent<WorldManager>();
        wm.SendMessage("Awake");
        typeof(WorldManager).GetField("distanceLeft", Inst).SetValue(wm, wm.WorldDistance);
        typeof(WorldManager).GetField("levelBegun", Inst).SetValue(wm, true);
        return wm;
    }

    // The level flown and the boss already dealt with: the real EndLevel path.
    static void FinishLevel(WorldManager wm)
    {
        typeof(BossEncounter).GetField("doneWorld", Stat).SetValue(null, WorldManager.CurrentIndex);
        typeof(WorldManager).GetField("distanceLeft", Inst).SetValue(wm, 0f);
        wm.EndLevel();
    }

    static void Gone()
    {
        if (Liftoff.Live != null) Object.DestroyImmediate(Liftoff.Live.gameObject);
        if (Planetfall.Live != null) Object.DestroyImmediate(Planetfall.Live.gameObject);
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
    }

    // The flying ship as gameS1 has it: movePlayer (what the planetfall and
    // the lift-off take hold of) and its power controller.
    static ShipPowerController Ship()
    {
        var go = new GameObject("~Ship");
        go.AddComponent<SpriteRenderer>().sortingOrder = 0;
        go.AddComponent<movePlayer>();
        var child = new GameObject("Plume");
        child.transform.SetParent(go.transform, false);
        child.AddComponent<SpriteRenderer>().sortingOrder = -1;
        go.transform.position = new Vector3(-1f, -3f, 0f);
        var c = go.AddComponent<ShipPowerController>();
        c.SendMessage("Awake");
        c.SendMessage("Start");
        var gun = go.GetComponentInChildren<UltimateGun>();
        if (gun != null) gun.SendMessage("Awake");
        return c;
    }

    static void SetTimer(ShipPowerController c, float seconds) =>
        typeof(ShipPowerController).GetField("timer", Inst).SetValue(c, seconds);

    static float GunTarget(ShipPowerController c)
    {
        var gun = c.GetComponentInChildren<UltimateGun>();
        if (gun == null) return 0f;
        return (float)typeof(UltimateGun).GetField("targetExtend", Inst).GetValue(gun);
    }

    // One frame the way the game runs it: the power controller every frame
    // (it checks for itself), the world's pieces only while Flying. The
    // controller goes first, so the frame a sequence hands control back is
    // still a held one and the next frame is the first free one.
    static void Fly(WorldManager wm, ShipPowerController c)
    {
        frame++;
        if (c != null) c.Step(Dt);
        if (WorldManager.Flying)
        {
            if (wm != null) wm.Tick(Dt);
            var fall = Planetfall.Live;
            if (fall != null && fall.State != Planetfall.Stage.Done) fall.Step(Dt);
            var lift = Liftoff.Live;
            if (lift != null && lift.State != Liftoff.Stage.Done) lift.Step(Dt);
            var portal = Portal.Live;
            if (portal != null) portal.Step(Dt);
        }
    }

    // What a transition must look like to the charge: held at `charge`,
    // nothing fired.
    struct Watch
    {
        public bool held, quiet, tucked, signal;
        public int frames;
        public void Begin() { held = quiet = tucked = signal = true; frames = 0; }
        public void See(ShipPowerController c, float charge, int fired)
        {
            frames++;
            held &= Mathf.Approximately(c.SecondsLeft, charge);
            quiet &= c.UltimatesFired == fired && c.FreeShotsFired == 0 && c.PendingFreeShots == 0;
            tucked &= GunTarget(c) == 0f;
            signal &= WorldTransition.InProgress && ShipPowerController.TransitionFrozen;
        }
    }

    // Every pickup's charge, tried mid-transition: none of it moves the
    // charge or fires anything.
    static bool PickupsInert(ShipPowerController c, float charge)
    {
        c.ReduceTimer(c.secondsPerAtom);
        c.ReduceTimer(c.secondsPerDust);
        c.ReduceTimer(c.secondsPerBrightStar);
        c.ReduceTimer(c.secondsPerRedAtom);
        c.FreeShot();   // the red atom's free shot: dropped, not queued
        string word = c.CollectCooldownAtom();
        float cut = c.ReduceWeaponCooldown();
        return Mathf.Approximately(c.SecondsLeft, charge) && c.PendingFreeShots == 0 && c.FreeShotsFired == 0 &&
               word == ShipPowerController.ChargeHeldLabel && cut == 0f;
    }

    // After a transition: the very charge it went in with, then ticking,
    // the gun sliding out, and the ultimate going off on time.
    static void Resumes(string name, ShipPowerController c, WorldManager wm, float charge, int fired)
    {
        Check(name + ": over, the signal is off and the charge is the same (" + c.SecondsLeft.ToString("F3") + " s)",
              !WorldTransition.InProgress && Mathf.Approximately(c.SecondsLeft, charge));
        Fly(wm, c);
        Check(name + ": ... and carries on at once (ticks, the gun slides out again)",
              c.SecondsLeft < charge && Mathf.Abs(charge - Dt - c.SecondsLeft) < 1e-4f && GunTarget(c) > 0f);
        int i = 0;
        for (; i < (int)(2f / Dt) && c.UltimatesFired == fired; i++) Fly(wm, c);
        Check(name + ": ... and fires when it runs out (" + ((i + 1) * Dt).ToString("F2") + " s after)",
              c.UltimatesFired == fired + 1 && (i + 1) * Dt <= charge + 2f * Dt);
        float before = c.SecondsLeft;
        c.ReduceTimer(5f);
        Check(name + ": ... pickups cut it again", Mathf.Abs(before - 5f - c.SecondsLeft) < 1e-3f);
    }

    static void Teardown(ShipPowerController c)
    {
        if (c == null) return;
        c.SendMessage("OnDestroy");
        if (c.Runner != null) c.Runner.SendMessage("OnDestroy");
        if (c.Secret != null) c.Secret.SendMessage("OnDestroy");
        Object.DestroyImmediate(c.gameObject);
        AttackPool.StopAll();
        typeof(ShipPowerController).GetMethod("FinishCinematic", Stat).Invoke(null, null);
    }

    // ---- 1. outside transitions -------------------------------------------------

    static void OutsideTransitions()
    {
        FreshScene(0);
        var c = Ship();
        Check("no world at all (the tutorial, a bare scene): no transition", !WorldTransition.InProgress);
        var wm = World();
        Check("a world mid-level: no transition", wm.Stage == WorldManager.LevelStage.Level && !WorldTransition.InProgress);
        SetTimer(c, 10f);
        for (int i = 0; i < 60; i++) Fly(wm, c);
        Check("the charge ticks on running time (" + c.SecondsLeft.ToString("F3") + " s left of 10)",
              Mathf.Abs(c.SecondsLeft - (10f - 60 * Dt)) < 1e-3f);
        float s = c.SecondsLeft;
        c.ReduceTimer(c.secondsPerAtom);
        Check("an atom cuts it as before", Mathf.Approximately(c.SecondsLeft, s - c.secondsPerAtom));
        string word = c.CollectCooldownAtom();
        Check("the capacitor's word is as before (" + word + ")", word != ShipPowerController.ChargeHeldLabel);
        SetTimer(c, Held);
        int i2 = 0;
        for (; i2 < 120 && c.UltimatesFired == 0; i2++) Fly(wm, c);
        Check("it fires at zero as before", c.UltimatesFired == 1);
        Check("the source: Update steps the controller on Time.deltaTime",
              File.ReadAllText("Assets/Scripts/Gameplay/Weapons/ShipPowerController.cs").Contains("Step(Time.deltaTime);"));
        Teardown(c);
        Object.DestroyImmediate(wm.gameObject);
    }

    // ---- 2. the portal ------------------------------------------------------------

    static void Portals()
    {
        LiftoffCatalog.Enabled = false;   // Verdant's and Ember's own ends are lift-offs (LiftoffClimb); here their portals
        // ... and Verdant -> Ember without Ember's planetfall: the portal as it was before Ember's art
        PlanetfallCatalog.Defs = new[] { PlanetfallCatalog.Frost, PlanetfallCatalog.Verdant };
        foreach (int world in new[] { 2, 3 })   // Verdant -> Ember, and Ember's loop portal
        {
            string name = world == 3 ? "the loop portal" : "the portal";
            FreshScene(world);
            var c = Ship();
            var wm = World();
            SetTimer(c, Held);
            FinishLevel(wm);
            var portal = Portal.Live;
            Check(name + ": open, the transition is on", portal != null && wm.PortalIsOpen && WorldTransition.InProgress);
            var w = new Watch();
            w.Begin();
            for (int i = 0; i < (int)(6f / Dt); i++) { Fly(wm, c); w.See(c, Held, 0); }
            Check(name + ": 6 s open, the charge holds at " + Held + " s (" + c.SecondsLeft.ToString("F3") + ")", w.held);
            Check(name + ": ... nothing fires, the gun stays tucked in", w.quiet && w.tucked && w.signal);
            Check(name + ": ... pickups don't move it, the red atom's free shot is dropped", PickupsInert(c, Held));
            SetTimer(c, 0f);   // even fully charged, it waits
            for (int i = 0; i < 30; i++) Fly(wm, c);
            Check(name + ": a full charge waits too", c.UltimatesFired == 0 && c.SecondsLeft == 0f);
            SetTimer(c, Held);
            int was = WorldManager.CurrentIndex;
            portal.Enter();
            Check(name + ": through it, the next world (" + WorldManager.CurrentIndex + ")",
                  WorldManager.CurrentIndex != was && !wm.PortalIsOpen);
            Resumes(name, c, wm, Held, 0);
            Teardown(c);
            Gone();
            Object.DestroyImmediate(wm.gameObject);
        }
        PlanetfallCatalog.Defs = PlanetfallCatalog.All;
        LiftoffCatalog.Enabled = true;
    }

    // ---- 3. the planetfall -------------------------------------------------------

    static void PlanetfallDescent()
    {
        FreshScene(0);
        var c = Ship();
        var wm = World();
        SetTimer(c, Held);
        FinishLevel(wm);
        var p = Planetfall.Live;
        Check("planetfall: the approach is a transition", p != null && p.State == Planetfall.Stage.Approach && WorldTransition.InProgress);
        var w = new Watch();
        w.Begin();
        for (int i = 0; i < 300; i++) { Fly(wm, c); w.See(c, Held, 0); }
        Check("planetfall: the approach holds the charge, fires nothing", w.held && w.quiet && w.tucked && w.signal);
        Check("planetfall: ... pickups during the approach don't count", PickupsInert(c, Held));

        Check("planetfall: the commit", p.Commit(c.transform));
        w.Begin();
        bool switchedSeen = false, heldAfterSwitch = true;
        int world = WorldManager.CurrentIndex;
        for (int i = 0; i < (int)(12f / Dt) && Planetfall.Live != null; i++)
        {
            Fly(wm, c);
            if (Planetfall.Live == null) break;   // control returned this frame
            w.See(c, Held, 0);
            if (WorldManager.CurrentIndex != world) switchedSeen = true;
            if (switchedSeen) heldAfterSwitch &= WorldTransition.InProgress && !wm.PortalIsOpen;
            if (i == 120)
            {
                bool inert = PickupsInert(c, Held);
                Check("planetfall: ... pickups mid-descent don't count", inert);
            }
        }
        Check("planetfall: the descent (" + (w.frames * Dt).ToString("F1") + " s) holds the charge at " + Held + " s", w.held && w.frames > 60);
        Check("planetfall: ... nothing fires, the gun tucked in, the signal on throughout", w.quiet && w.tucked && w.signal);
        Check("planetfall: ... still a transition after the world switched under the clouds, until control returns",
              switchedSeen && heldAfterSwitch && WorldManager.CurrentIndex == 1);
        Resumes("planetfall", c, wm, Held, 0);
        Teardown(c);
        Gone();
        Object.DestroyImmediate(wm.gameObject);
    }

    // ---- 4. the lift-off ----------------------------------------------------------

    static void LiftoffClimb(int from)
    {
        string name = "lift-off (" + WorldManager.Worlds[from].displayName + ")";
        FreshScene(from);
        var c = Ship();
        var wm = World();
        SetTimer(c, Held);
        FinishLevel(wm);
        var l = Liftoff.Live;
        if (l == null)
        {
            Check(name + ": art present (skipped otherwise)", Portal.Live != null && WorldTransition.InProgress);
            Teardown(c); Gone(); Object.DestroyImmediate(wm.gameObject);
            return;
        }
        var w = new Watch();
        w.Begin();
        bool rose = false, interlude = false;
        for (int i = 0; i < (int)(40f / Dt) && Liftoff.Live != null; i++)
        {
            Fly(wm, c);
            w.See(c, Held, 0);
            if (Liftoff.Live != null && Liftoff.Live.State == Liftoff.Stage.Rise) rose = true;
            if (Liftoff.Live != null && Liftoff.Live.State == Liftoff.Stage.Interlude)
            {
                if (!interlude && !PickupsInert(c, Held)) Check(name + ": pickups in the interlude don't count", false);
                interlude = true;
            }
        }
        Check(name + ": beat, rise and interlude (" + (w.frames * Dt).ToString("F1") + " s) hold the charge",
              rose && interlude && w.held && Liftoff.Live == null);
        Check(name + ": ... nothing fires, the gun tucked in, the signal on throughout", w.quiet && w.tucked && w.signal);
        // Frost's gateway: Verdant's planetfall; Verdant's: Ember's; Ember's
        // (the last world): no gateway, the loop starts Space at once
        bool fall = from < WorldManager.Worlds.Length - 1;
        string onto = fall ? WorldManager.Worlds[from + 1].displayName : "";
        if (!fall && LiftoffCatalog.Ember.autoLoop)
        {
            Check(name + ": no portal: the loop began at the interlude's end (" + WorldManager.Current.displayName + ", loop " +
                  RunLoop.Index + "), the transition over",
                  Portal.Live == null && Planetfall.Live == null && WorldManager.CurrentIndex == RunLoop.StartWorld &&
                  RunLoop.Index == 1 && !wm.PortalIsOpen && wm.Stage == WorldManager.LevelStage.Level);
            Resumes(name, c, wm, Held, 0);
            Teardown(c);
            Gone();
            Object.DestroyImmediate(wm.gameObject);
            return;
        }
        Check(name + ": the gateway it opened (" + (fall ? onto + "'s planetfall" : "the loop portal") + ") is still the transition",
              (fall ? Planetfall.Live != null && Portal.Live == null : Portal.Live != null && Planetfall.Live == null) &&
              WorldTransition.InProgress);
        for (int i = 0; i < 60; i++) Fly(wm, c);
        Check(name + ": ... the charge still held at the gateway", Mathf.Approximately(c.SecondsLeft, Held) && c.UltimatesFired == 0);
        if (fall && Planetfall.Live != null)
        {
            Planetfall.Live.Commit(c.transform);
            var wf = new Watch();
            wf.Begin();
            for (int i = 0; i < (int)(12f / Dt) && Planetfall.Live != null; i++)
            {
                Fly(wm, c);
                if (Planetfall.Live == null) break;
                wf.See(c, Held, 0);
            }
            Check(name + ": ... " + onto + "'s descent holds it too, the world now " + onto,
                  wf.held && wf.quiet && wf.frames > 60 && WorldManager.CurrentIndex == from + 1);
        }
        else if (Portal.Live != null) Portal.Live.Enter();
        Resumes(name, c, wm, Held, 0);
        Teardown(c);
        Gone();
        Object.DestroyImmediate(wm.gameObject);
    }

    // ---- 5. the boss's end ---------------------------------------------------------

    static void BossKillToPortal()
    {
        LiftoffCatalog.Enabled = false;   // Verdant's end straight to its portal (its lift-off: LiftoffClimb)
        PlanetfallCatalog.Defs = new[] { PlanetfallCatalog.Frost, PlanetfallCatalog.Verdant };   // ... not Ember's planet
        FreshScene(2);
        var c = Ship();
        var wm = World();
        typeof(WorldManager).GetField("distanceLeft", Inst).SetValue(wm, 0f);
        wm.EndLevel();
        var e = BossEncounter.Instance;
        Check("boss: the level's end starts the boss, not a transition", e != null && BossEncounter.Running && !WorldTransition.InProgress);
        e.Step(.1f, 1f);
        for (int i = 0; i < 2000 && e.State == BossEncounter.Phase.Intro; i++) e.Step(.1f, 1f);
        SetTimer(c, 20f);
        for (int i = 0; i < 60; i++) Fly(wm, c);
        Check("boss: the fight charges as normal (" + c.SecondsLeft.ToString("F2") + " s)",
              e.State == BossEncounter.Phase.Fight && Mathf.Abs(c.SecondsLeft - (20f - 60 * Dt)) < 1e-3f);
        for (int i = 0; i < BossConfig.HitPoints; i++) e.OnUltimateHit();
        SetTimer(c, 20f);
        for (int i = 0; i < 4000 && e.State != BossEncounter.Phase.Done; i++) e.Step(.05f, 1f);
        Check("boss: destroyed, its end opens the portal: the transition", e.Destroyed && e.State == BossEncounter.Phase.Done &&
              Portal.Live != null && wm.PortalIsOpen && WorldTransition.InProgress);
        SetTimer(c, Held);
        var w = new Watch();
        w.Begin();
        for (int i = 0; i < (int)(4f / Dt); i++) { Fly(wm, c); w.See(c, Held, 0); }
        Check("boss: after the kill the charge holds, nothing fires", w.held && w.quiet && w.tucked && w.signal);
        Check("boss: ... pickups after the kill don't count", PickupsInert(c, Held));
        Portal.Live.Enter();
        Resumes("boss -> portal", c, wm, Held, 0);
        Teardown(c);
        Gone();
        Object.DestroyImmediate(wm.gameObject);
        BossEncounter.ResetRun();
        PlanetfallCatalog.Defs = PlanetfallCatalog.All;
        LiftoffCatalog.Enabled = true;
    }

    // ---- 6. it never sticks ----------------------------------------------------------

    static void NeverSticks()
    {
        // a scene reload mid-descent
        FreshScene(0);
        var wm = World();
        var c = Ship();
        FinishLevel(wm);
        Planetfall.Live.Commit(c.transform);
        for (int i = 0; i < 90; i++) Fly(wm, c);
        bool on = WorldTransition.InProgress && Planetfall.HoldsShip;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Check("a scene reload mid-descent clears it", on && !WorldTransition.InProgress);

        // a dead pilot at the open portal: nothing fires, and the run's reload clears it
        LiftoffCatalog.Enabled = false;   // Ember's loop portal (its lift-off: LiftoffClimb)
        FreshScene(3);
        wm = World();
        c = Ship();
        SetTimer(c, 0f);
        FinishLevel(wm);
        buttonClicks.playerDied = true;
        for (int i = 0; i < 60; i++) Fly(wm, c);
        bool quiet = c.UltimatesFired == 0;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        buttonClicks.playerDied = false;
        Check("death at the portal: nothing fires; the reload clears it", quiet && !WorldTransition.InProgress);
        LiftoffCatalog.Enabled = true;

        // the dev skip (BOSS RUSH FINAL) mid-descent
        FreshScene(0);
        wm = World();
        c = Ship();
        FinishLevel(wm);
        Planetfall.Live.Commit(c.transform);
        for (int i = 0; i < 30; i++) Fly(wm, c);
        on = WorldTransition.InProgress;
        wm.DevJumpToFinal();
        if (Planetfall.Live != null) Object.DestroyImmediate(Planetfall.Live.gameObject);   // BossUtil.Kill defers in edit mode
        Check("the dev skip mid-descent clears it", on && !WorldTransition.InProgress && !wm.PortalIsOpen);
        Teardown(c);
        Object.DestroyImmediate(wm.gameObject);

        // a torn-down descent (its world already switched) clears it
        FreshScene(0);
        wm = World();
        c = Ship();
        FinishLevel(wm);
        Planetfall.Live.Commit(c.transform);
        for (int i = 0; i < (int)(12f / Dt) && Planetfall.Live != null && !Planetfall.Live.Switched; i++) Fly(wm, c);
        on = WorldTransition.InProgress && !wm.PortalIsOpen;
        Object.DestroyImmediate(Planetfall.Live.gameObject);
        Check("a descent torn down after the switch clears it", on && !WorldTransition.InProgress);
        Teardown(c);
        Object.DestroyImmediate(wm.gameObject);

        // the manager going with the portal open (the scene going) clears it
        LiftoffCatalog.Enabled = false;   // Ember's loop portal straight away
        FreshScene(3);
        wm = World();
        FinishLevel(wm);
        on = WorldTransition.InProgress && Portal.Live != null;
        Object.DestroyImmediate(wm.gameObject);
        Check("the world manager destroyed with the portal open clears it", on && !WorldTransition.InProgress);
        Gone();
        LiftoffCatalog.Enabled = true;

        // a lift-off destroyed mid-rise along with its scene
        FreshScene(1);
        wm = World();
        c = Ship();
        FinishLevel(wm);
        if (Liftoff.Live != null)
        {
            for (int i = 0; i < 2000 && Liftoff.Live != null && Liftoff.Live.State != Liftoff.Stage.Rise; i++) Fly(wm, c);
            on = WorldTransition.InProgress;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Check("a scene reload mid-lift-off clears it", on && !WorldTransition.InProgress);
        }
        Check("the signal is derived, not a remembered flag (no static bool to clear)",
              typeof(WorldTransition).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).Length == 0);
    }

    // ---- 7. allocations ------------------------------------------------------------------

    static void NoAllocations()
    {
        FreshScene(0);
        var wm = World();
        FinishLevel(wm);
        bool any = false;
        for (int i = 0; i < 10; i++) any |= WorldTransition.InProgress;
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) any |= WorldTransition.InProgress;
        long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
        Check("reading the signal allocates nothing (" + bytes + " B over 10000 reads)", any && bytes == 0);
        Gone();
        Object.DestroyImmediate(wm.gameObject);
    }
}
