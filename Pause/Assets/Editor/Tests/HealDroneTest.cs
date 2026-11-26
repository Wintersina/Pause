using System;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// The healing mini-drone (HealDrone) and the banked green atom (HealBank, HealBankIcon):
//   * the drone deploys only while the ship is damaged with 2+ hearts left, sprays the broken parts from its own
//     nozzle on a calm cadence, tucks back, hides at the last heart, never shoots
//   * a green atom caught at full hearts banks (cap 3); below full it heals at once; the tutorial never banks
//   * a banked atom is spent on a lost heart: the drone sprays and the heart comes back after a beat, at any
//     heart count, and the last heart's hit is absorbed
//   * the bank's icon (count when > 1) sits behind the tail, clear of hull and exhaust, on every ScreenFit device
//   * the bank resets per run; the gun drones (UltimateGun) are not touched; nothing allocates
public static class HealDroneTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[HEALDRONE] PASS  " : "[HEALDRONE] FAIL  ") + what);
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
            BankRules();
            WiredIntoTheRun();
            EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
            buttonClicks.playerDied = false;
            Deploys();
            LastHeartStaysTucked();
            BankedAtomIsSpent();
            LastHeartAbsorbed();
            GunDronesUntouched();
            FrozenAndNoGarbage();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            IconBehindTheTail();
        }
        finally
        {
            HealBank.Reset();
            collisionDetection.lifeCounter = 0;
            collisionDetection.MAXLIFE = 3;
            buttonClicks.playerDied = false;
            ScreenInfo_Clear();
        }
        Debug.Log("[HEALDRONE] failures: " + fails);
        return fails;
    }

    static IDisposable screen;
    static void ScreenInfo_Clear() { if (screen != null) { screen.Dispose(); screen = null; } }

    // ---- the bank's rules -----------------------------------------------------

    static void BankRules()
    {
        HealBank.Reset();
        Check("a green atom below full hearts heals at once", HealBank.OnCatch(true, false) == HealBank.Catch.Heal && HealBank.Count == 0);
        Check("at full hearts it banks", HealBank.OnCatch(false, false) == HealBank.Catch.Bank && HealBank.Count == 1);
        HealBank.OnCatch(false, false);
        HealBank.OnCatch(false, false);
        Check("up to " + HealBank.Max + " banked", HealBank.Count == 3 && HealBank.Max == 3);
        Check("a fourth is wasted, the bank stays at the cap", HealBank.OnCatch(false, false) == HealBank.Catch.Wasted && HealBank.Count == 3);
        Check("below full hearts it still heals with a full bank", HealBank.OnCatch(true, false) == HealBank.Catch.Heal && HealBank.Count == 3);

        HealBank.Reset();
        Check("the tutorial never banks (its heal step is unchanged: damaged -> repairs; whole -> as before)",
              HealBank.OnCatch(true, true) == HealBank.Catch.Heal && HealBank.OnCatch(false, true) == HealBank.Catch.Wasted && HealBank.Count == 0);

        HealBank.Reset();
        Check("no atom banked: a lost heart stays lost", HealBank.OnHit(false) == HealBank.Hit.None && HealBank.OnHit(true) == HealBank.Hit.None && !HealBank.NeedSpray);
        HealBank.OnCatch(false, false);
        HealBank.OnCatch(false, false);
        Check("a banked atom is spent on a lost heart: restored after a beat, the drone is asked to spray",
              HealBank.OnHit(false) == HealBank.Hit.Restore && HealBank.Count == 1 && HealBank.Pending == 1 && HealBank.NeedSpray &&
              Mathf.Abs(HealBank.RestoreDelay - .6f) < 1e-4f);
        bool early = HealBank.Tick(.5f);
        bool due = HealBank.Tick(.15f);
        Check("the heart is due .6 s after the hit, once", !early && due && HealBank.Pending == 0 && !HealBank.Tick(1f));
        Check("the last heart's hit is absorbed (works at any heart count)", HealBank.OnHit(true) == HealBank.Hit.Absorb && HealBank.Count == 0 && HealBank.Pending == 0);
        HealBank.OnCatch(false, false);
        HealBank.Reset();
        Check("the bank resets", HealBank.Count == 0 && HealBank.Pending == 0 && !HealBank.NeedSpray && HealBank.SprayRequests == 0);
        Check("the heal atom is no longer held back at full hearts while the bank has room",
              File.ReadAllText("Assets/Scripts/Gameplay/Pickups/HealAtomSpawner.cs").Contains("HealBank.Count >= HealBank.Max"));
    }

    static void WiredIntoTheRun()
    {
        string hit = File.ReadAllText("Assets/Scripts/Ship/collisionDetection.cs");
        Check("collisionDetection routes the green atom through the bank and the hit through HealBank.OnHit",
              hit.Contains("HealBank.OnCatch(") && hit.Contains("HealBank.OnHit(") && hit.Contains("HealBank.Reset()") &&
              hit.Contains("HealDrone.Attach(gameObject)") && hit.Contains("HealBankIcon.Attach(gameObject)") &&
              hit.Contains("caught == HealBank.Catch.Heal) lifeCounter--"));
        Check("leaving gameplay resets the bank too", File.ReadAllText("Assets/Scripts/Core/GameStateReset.cs").Contains("HealBank.Reset()"));
    }

    // ---- a live ship ----------------------------------------------------------

    static GameObject Fly(int id, int max, out ShipDamageFx fx, out lifeControler life, out HealDrone drone, out UltimateGun gun)
    {
        HealBank.Reset();
        collisionDetection.MAXLIFE = max;
        collisionDetection.lifeCounter = 0;
        var go = new GameObject(ShipId.ObjectName(id) + "(Clone)", typeof(SpriteRenderer));
        life = go.AddComponent<lifeControler>();
        life.SendMessage("Start");
        fx = go.GetComponent<ShipDamageFx>();
        fx.Build();
        gun = UltimateGun.Attach(go);
        if (gun.transform.childCount == 0) gun.SendMessage("Awake");
        drone = HealDrone.Attach(go);
        return go;
    }

    static void Frame(ShipDamageFx fx, lifeControler life, HealDrone drone, UltimateGun gun, float dt = Dt)
    {
        TestHarness.Send(life, "Update");
        if (gun != null) gun.Step(0f, dt, dt);
        drone.Step(dt);   // runs before ShipDamageFx in the game (execution order 55 < 60)
        fx.Tick(dt);
    }

    static void Cleanup(GameObject go, HealDrone drone)
    {
        Object.DestroyImmediate(drone.gameObject);
        Object.DestroyImmediate(go);
        HealBank.Reset();
        collisionDetection.lifeCounter = 0;
        collisionDetection.MAXLIFE = 3;
    }

    static void Deploys()
    {
        ShipDamageFx fx; lifeControler life; HealDrone drone; UltimateGun gun;
        var go = Fly(ShipId.Starter, 4, out fx, out life, out drone, out gun);
        Check("the ship gets a green mini-drone, tucked away while whole", fx != null && drone != null && !drone.Shown && drone.Deploy == 0f);
        float maxDeploy = 0f;
        for (float t = 0f; t < 3f; t += Dt) { Frame(fx, life, drone, gun); maxDeploy = Mathf.Max(maxDeploy, drone.Deploy); }
        Check("whole ship: the drone never comes out and nothing sprays", maxDeploy == 0f && fx.Sprays == 0 && !drone.NozzleShown);
        Check("the drone is drawn in green", drone.BodyRenderer.color == HealDrone.Green && drone.BodyRenderer.sprite != null);

        collisionDetection.lifeCounter = 1;   // 3 of 4 hearts left: damaged, 2+ hearts
        bool deployed = false, nozzle = false, spurt = false, fromNozzle = true, tucked = false, sprayWhileOut = true;
        Vector3 under = default;
        for (float t = 0f; t < 20f; t += Dt)
        {
            Frame(fx, life, drone, gun);
            if (drone.OnStation) deployed = true;
            if (drone.NozzleShown) nozzle = true;
            var ns = drone.NozzleRenderer.sprite;
            if (drone.NozzleShown && (ns == ShipDamageFx.Frame(ShipDamageFx.RowNozzle, 2) || ns == ShipDamageFx.Frame(ShipDamageFx.RowNozzle, 3))) spurt = true;
            if (fx.Spraying)
            {
                fromNozzle &= (fx.SprayFrom - drone.NozzlePosition).sqrMagnitude < 1e-6f;
                sprayWhileOut &= drone.Shown;
            }
            if (deployed && drone.Deploy <= 0f) tucked = true;
            if (t < .01f) under = drone.transform.position;
        }
        Check("damaged with 2+ hearts: the drone deploys, its nozzle out and spurting, and tucks back (" + fx.Sprays + " sprays in 20 s)",
              deployed && nozzle && spurt && tucked && fx.Sprays >= 2 && fx.Sprays <= 4 && sprayWhileOut);
        Check("the spray leaves the mini-drone's nozzle", fromNozzle && fx.Healer == drone);
        Check("the first deployment is under the ship (the drone starts at the hull)", Vector3.Distance(under, go.transform.position) < .6f);
        Cleanup(go, drone);
    }

    static void LastHeartStaysTucked()
    {
        ShipDamageFx fx; lifeControler life; HealDrone drone; UltimateGun gun;
        var go = Fly(ShipId.Starter, 4, out fx, out life, out drone, out gun);
        collisionDetection.lifeCounter = 2;   // 2 of 4 left: still deploys
        for (float t = 0f; t < 4f; t += Dt) Frame(fx, life, drone, gun);
        Check("2 hearts left: it deploys", fx.Sprays >= 1);
        collisionDetection.lifeCounter = 3;   // the last heart
        int sprays = fx.Sprays;
        float maxDeploy = 0f;
        for (float t = 0f; t < 1.5f; t += Dt) Frame(fx, life, drone, gun);
        for (float t = 0f; t < 14f; t += Dt) { Frame(fx, life, drone, gun); maxDeploy = Mathf.Max(maxDeploy, drone.Deploy); }
        Check("1 heart left: no more sprays, the drone stays tucked (low-health tension)",
              fx.Sprays == sprays && maxDeploy == 0f && !drone.Shown && !fx.Spraying);
        Cleanup(go, drone);

        // the tutorial's ship (2 hearts, starts at 1): the drone has nothing to do, the heal step is the green atom's
        var go2 = Fly(ShipId.Starter, ShipLives.TutorialHearts, out fx, out life, out drone, out gun);
        collisionDetection.lifeCounter = ShipLives.TutorialHearts - 1;
        for (float t = 0f; t < 8f; t += Dt) Frame(fx, life, drone, gun);
        Check("tutorial start (1 of 2 hearts): the drone stays tucked, the green atom's repair is the lesson",
              fx.Sprays == 0 && !drone.Shown && ShipLives.Left == 1);
        Cleanup(go2, drone);
    }

    static void BankedAtomIsSpent()
    {
        ShipDamageFx fx; lifeControler life; HealDrone drone; UltimateGun gun;
        var go = Fly(ShipId.Starter, 4, out fx, out life, out drone, out gun);
        // at full hearts a green atom is banked
        Check("full hearts: caught green atom is banked", HealBank.OnCatch(collisionDetection.lifeCounter > 0, false) == HealBank.Catch.Bank);
        // a hit leaves 2 of 4 hearts; the drone spends the bank
        for (float t = 0f; t < 1f; t += Dt) Frame(fx, life, drone, gun);
        var bank = HealBank.OnHit(false);
        collisionDetection.lifeCounter += 1;
        collisionDetection.lifeCounter += 1;   // (a second heart gone too: 2 hearts left, so the beat is visible)
        int sprays = fx.Sprays;
        bool restoredEarly = false, deployed = false;
        for (float t = 0f; t < .5f; t += Dt) { Frame(fx, life, drone, gun); deployed |= drone.Deploy > .3f; restoredEarly |= collisionDetection.lifeCounter < 2; }
        for (float t = 0f; t < .3f; t += Dt) Frame(fx, life, drone, gun);
        Check("a spent atom: the heart is restored about .6 s after the hit, not before (hearts lost " + collisionDetection.lifeCounter + ")",
              bank == HealBank.Hit.Restore && !restoredEarly && collisionDetection.lifeCounter == 1 && HealBank.Count == 0);
        for (float t = 0f; t < 3f; t += Dt) { Frame(fx, life, drone, gun); deployed |= drone.OnStation; }
        Check("the drone comes out for it and sprays", deployed && fx.Sprays > sprays);
        Cleanup(go, drone);

        // at 1 heart left: the same, though the drone would otherwise stay hidden
        go = Fly(ShipId.Starter, 4, out fx, out life, out drone, out gun);
        HealBank.OnCatch(false, false);
        collisionDetection.lifeCounter = 2;
        for (float t = 0f; t < 2f; t += Dt) Frame(fx, life, drone, gun);
        sprays = fx.Sprays;
        bank = HealBank.OnHit(false);
        collisionDetection.lifeCounter = 3;   // the last heart
        bool out1 = false;
        for (float t = 0f; t < 3f; t += Dt) { Frame(fx, life, drone, gun); out1 |= drone.OnStation; }
        Check("at 1 heart a banked atom still brings the drone out, sprays and restores the heart (hearts lost " + collisionDetection.lifeCounter + ")",
              bank == HealBank.Hit.Restore && out1 && fx.Sprays > sprays && collisionDetection.lifeCounter == 2 && HealBank.Count == 0);
        for (float t = 0f; t < 3f; t += Dt) Frame(fx, life, drone, gun);
        Check("...and it tucks away again at 2 hearts, the request served", !HealBank.NeedSpray);
        Cleanup(go, drone);
    }

    static void LastHeartAbsorbed()
    {
        ShipDamageFx fx; lifeControler life; HealDrone drone; UltimateGun gun;
        var go = Fly(ShipId.Starter, 3, out fx, out life, out drone, out gun);
        HealBank.OnCatch(false, false);
        collisionDetection.lifeCounter = 2;   // the last heart
        for (float t = 0f; t < 1f; t += Dt) Frame(fx, life, drone, gun);
        var r = HealBank.OnHit(true);
        // collisionDetection keeps lifeCounter at MAXLIFE-1 on an absorbed hit
        int sprays = fx.Sprays;
        bool out1 = false;
        for (float t = 0f; t < 3f; t += Dt) { Frame(fx, life, drone, gun); out1 |= drone.OnStation; }
        Check("the last heart's hit is absorbed by a banked atom: the drone sprays and the ship lives on",
              r == HealBank.Hit.Absorb && collisionDetection.lifeCounter == 2 && HealBank.Count == 0 && out1 && fx.Sprays > sprays);
        Cleanup(go, drone);
    }

    static void GunDronesUntouched()
    {
        ShipDamageFx fx; lifeControler life; HealDrone drone; UltimateGun gun;
        var go = Fly(ShipId.Starter, 4, out fx, out life, out drone, out gun);
        collisionDetection.lifeCounter = 1;
        bool gunBusy = false;
        for (float t = 0f; t < 12f; t += Dt)
        {
            Frame(fx, life, drone, gun);
            gunBusy |= gun.Duty > 0f || gun.NozzleShown || gun.Startling || gun.Lean != 0f;
        }
        Check("the gun drone never firefights, startles or leans: the mini-drone does the spraying", !gunBusy && gun.Startles == 0 && fx.Sprays >= 1);
        // the ultimate slides the gun out while the mini-drone sprays: neither waits for the other
        for (float t = 0f; t < 8f && !fx.Spraying; t += Dt) Frame(fx, life, drone, gun);
        bool sprayed = fx.Spraying, slid = false, kept = true;
        for (float t = 0f; t < 1.6f; t += Dt)
        {
            TestHarness.Send(life, "Update");
            gun.Step(Mathf.Clamp(t / 1.2f, .01f, 1f), Dt, Dt);
            slid |= gun.Extend01 > .5f;
            fx.Tick(Dt);
            drone.Step(Dt);
            if (t < .2f) kept &= fx.Spraying || !sprayed;
        }
        Check("the ultimate's slide-out and the mini-drone's spray don't block each other", sprayed && slid && kept);
        Check("the mini-drone is its own object, not a child of the gun or the hull", drone.transform.parent == null && drone.GetComponent<UltimateGun>() == null);
        Cleanup(go, drone);
    }

    static void FrozenAndNoGarbage()
    {
        ShipDamageFx fx; lifeControler life; HealDrone drone; UltimateGun gun;
        var go = Fly(ShipId.Starter, 4, out fx, out life, out drone, out gun);
        collisionDetection.lifeCounter = 1;
        HealBank.OnCatch(false, false);
        HealBank.OnCatch(false, false);
        var icon = HealBankIcon.Attach(go);
        for (float t = 0f; t < 4f && !(fx.Spraying && drone.OnStation); t += Dt) { Frame(fx, life, drone, gun); icon.Refresh(); }
        Vector3 at = drone.transform.position;
        float deploy = drone.Deploy;
        int sprays = fx.Sprays;
        for (int k = 0; k < 120; k++) { fx.Tick(0f); drone.Step(0f); }
        Check("at timeScale 0 the mini-drone holds still", fx.Spraying && fx.Sprays == sprays && drone.Deploy == deploy &&
              (drone.transform.position - at).sqrMagnitude < 1e-10f);

        for (int k = 0; k < 120; k++) { Frame(fx, life, drone, gun); icon.Refresh(); }   // warm
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 300; k++) { fx.Tick(Dt); drone.Step(Dt); icon.Refresh(); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check("the drone, its spray and the bank icon allocate nothing (" + allocated + " B)", allocated == 0);
        Object.DestroyImmediate(icon.gameObject);
        Cleanup(go, drone);
    }

    // ---- the icon at the tail ---------------------------------------------------

    static void IconBehindTheTail()
    {
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        int cells = 0, bad = 0;
        string first = "";
        foreach (var d in FitDevice.All)
        {
            if (!ScreenFitTest.InLane(d)) continue;
            ScreenInfo_Clear();
            cam.aspect = d.Aspect;
            cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, d.w, d.h);
            screen = ScreenInfo.Override(d.w, d.h, d.Safe, d.Cutouts, d.ReportedDpi, d.ios);
            foreach (int id in new[] { ShipId.Starter, 11, 7 })   // a plain hull, a spinner, a big one
            {
                var go = new GameObject(ShipId.ObjectName(id) + "(Clone)", typeof(SpriteRenderer));
                var sr = go.GetComponent<SpriteRenderer>();
                sr.sprite = ShipHullArt.Get(id, 0, 0);
                float scale = shopingShips.NormalizedHullScale(sr.sprite);
                go.transform.localScale = new Vector3(scale, scale, 1f);
                HealBank.Reset();
                HealBank.OnCatch(false, false);
                HealBank.OnCatch(false, false);
                var icon = HealBankIcon.Attach(go);
                Rect view = ShipUiSlots.ScreenRect(cam);
                Bounds hb0 = ShipUiSlots.HullBounds(go.transform, id);
                float hx = hb0.extents.x, hy = hb0.extents.y;
                foreach (float fx in new[] { 0f, -1f, 1f })
                {
                    float x = fx < 0f ? view.xMin + hx : fx > 0f ? view.xMax - hx : view.center.x;
                    go.transform.position = new Vector3(x, view.yMin + hy * 1.6f, 0f);
                    icon.Refresh();
                    Bounds hb = ShipUiSlots.HullBounds(go.transform, id), ex;
                    bool hasEx = ShipUiSlots.ExhaustBounds(go.transform, id, out ex);
                    Bounds box = icon.Footprint;
                    const float tol = 1e-3f;
                    bool inside = box.min.x >= view.xMin - tol && box.max.x <= view.xMax + tol &&
                                  box.min.y >= view.yMin - tol && box.max.y <= view.yMax + tol;
                    bool clear = !ShipUiSlots.Overlaps(box, hb);
                    bool behind = Mathf.Abs(icon.Center.x - hb.center.x) < 1e-3f && icon.Visible && icon.CountShown && icon.CountText == "x2";
                    cells++;
                    if (!(inside && clear && behind))
                    {
                        bad++;
                        if (first == "") first = d.id + " ship " + id + " x " + fx + " inside " + inside + " clear " + clear + " behind " + behind;
                    }
                }
                HealBank.Reset();
                icon.Refresh();
                bool hidden = !icon.Visible && !icon.CountShown;
                HealBank.OnCatch(false, false);
                icon.Refresh();
                bool one = icon.Visible && !icon.CountShown;
                Check(d.id + " ship " + id + ": no icon with an empty bank, the icon alone with one atom, no count", hidden && one);
                Object.DestroyImmediate(icon.gameObject);
                Object.DestroyImmediate(go);
            }
        }
        ScreenInfo_Clear();
        Check("the banked icon (with its count) stays centred on the hull and clear of it, on screen: " + cells + " placements, " + bad + " bad " + first,
              cells >= 18 && bad == 0);
        HealBank.Reset();
        Object.DestroyImmediate(camGo);
    }
}
