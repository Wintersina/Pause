using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The death crash (DeathCrash): for every ship, the fatal hit breaks the hull
// into 3-6 fragments cut from its own wrecked sprite, every piece slams into
// a rail (x at the rail's inner face, y on screen), a solid killer (rock,
// alien, mine) crashes into a rail too while a boss shot / the boss body
// does not, the Flight Complete panel waits for the sequence (and appears
// once it is done), the timing stays in bounds, a tap after SkipAfter skips,
// a revive (Cancel) puts the ship back, the world stays stopped, a step
// allocates nothing, and it all holds on every TallScreenTest screen.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod DeathCrashTest.Run
public static class DeathCrashTest
{
    static int fails;
    const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly MethodInfo Trigger = typeof(collisionDetection).GetMethod("OnTriggerEnter2D", Inst);
    const float Dt = 1f / 60f;

    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[DCT] PASS  " : "[DCT] FAIL  ") + what);
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
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);

        Classification();
        FragmentCounts();
        foreach (int id in ShipId.All) EveryShip(id);
        Killers();
        PanelWaits();
        TapToSkip();
        Revive();
        NoAllocations();
        TallScreens();

        Debug.Log("[DCT] failures: " + fails);
        return fails;
    }

    // ---------------------------------------------------------------- rig

    sealed class Rig
    {
        public GameObject ship, heart, gun;
        public SpriteRenderer hull;
        public collisionDetection cd;
        public int id;

        public Rig(int id)
        {
            this.id = id;
            buttonClicks.playerDied = false;
            collisionDetection.lifeCounter = 0;
            ship = new GameObject(ShipId.ObjectName(id) + "(Clone)", typeof(SpriteRenderer));
            ship.transform.position = new Vector3(Random.Range(-1.5f, 1.5f), Random.Range(-3f, 0f), 1f);
            hull = ship.GetComponent<SpriteRenderer>();
            hull.sprite = ShipHullArt.Rest(id, 2);
            float scale = shopingShips.NormalizedHullScale(hull.sprite);
            ship.transform.localScale = new Vector3(scale, scale, 1f);
            ship.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            ship.AddComponent<BoxCollider2D>().isTrigger = true;
            cd = ship.AddComponent<collisionDetection>();
            cd.explosionAnimation = new GameObject("~TestExplosion");
            if (GameObject.Find("hypeText") == null) new GameObject("hypeText", typeof(RectTransform)).AddComponent<Text>();
            if (GameObject.Find("boostText") == null) new GameObject("boostText", typeof(RectTransform)).AddComponent<Text>();
            if (GameObject.Find("RocketsSound") == null) new GameObject("RocketsSound", typeof(AudioSource));
            if (GameObject.Find("AstroidExplotionSound") == null) new GameObject("AstroidExplotionSound", typeof(AudioSource));
            new GameObject("~boost").tag = "boost";
            typeof(collisionDetection).GetMethod("Start", Inst).Invoke(cd, null);
            collisionDetection.atomCheck = false;
            collisionDetection.cloakTimer = 0f;
            buttonClicks.playerDied = false;

            // a heart on its orbit (must stay to play the last shield) and the drone
            heart = new GameObject("Heart0", typeof(SpriteRenderer));
            heart.transform.SetParent(ship.transform, false);
            var g = UltimateGun.Attach(ship);
            if (g.transform.childCount == 0) g.SendMessage("Awake");
            gun = g.gameObject;
            // the last heart
            collisionDetection.lifeCounter = collisionDetection.MAXLIFE - 1;
        }

        public void Hit(GameObject killer)
        {
            Trigger.Invoke(cd, new object[] { killer.GetComponent<Collider2D>() });
        }

        public void Dispose()
        {
            if (DeathCrash.Instance != null) DeathCrash.Instance.Cancel(false);
            if (cd != null && cd.explosionAnimation != null) Object.DestroyImmediate(cd.explosionAnimation);
            foreach (var name in new[] { "~boost", "~TestExplosion(Clone)" })
                for (var go = GameObject.Find(name); go != null; go = GameObject.Find(name))
                    Object.DestroyImmediate(go);
            if (ship != null) Object.DestroyImmediate(ship);
            collisionDetection.lifeCounter = 0;
            buttonClicks.playerDied = false;
        }
    }

    static GameObject Killer(string name, string tag, Vector3 at)
    {
        var go = new GameObject(name, typeof(SpriteRenderer), typeof(CircleCollider2D));
        go.tag = tag;
        go.transform.position = at;
        go.GetComponent<SpriteRenderer>().sprite = ShipDamageFx.Frame(ShipDamageFx.RowChunk, 0);
        go.transform.localScale = Vector3.one * .5f;
        return go;
    }

    static GameObject BossShot(Vector3 at, out GameObject root)
    {
        root = new GameObject("BossShot");
        root.transform.position = at;
        var hit = new GameObject("BossShotHit", typeof(CircleCollider2D));
        hit.tag = BossHitbox.Tag;
        hit.transform.SetParent(root.transform, false);
        return hit;
    }

    static void Cleanup(GameObject go) { if (go != null) Object.DestroyImmediate(go); }

    // Runs the sequence to its end (or 4 s). Returns the time the panel became ready.
    static float RunToEnd(DeathCrash crash, System.Action<float> eachStep = null)
    {
        float t = 0f;
        while (DeathCrash.Running && t < 4f)
        {
            crash.Step(Dt);
            t += Dt;
            if (eachStep != null) eachStep(t);
        }
        return t;
    }

    // ---------------------------------------------------------------- suites

    static void Classification()
    {
        var rock = Killer("aestroid_1", "Astr", Vector3.zero);
        var alien = Killer(EnemyRoster.AlienObjectName, "Enimey", Vector3.zero);
        var mine = Killer(EnemyRoster.MineObjectName, "Enimey", Vector3.zero);
        var shot = BossShot(Vector3.zero, out var shotRoot);
        var body = new GameObject("BossBody", typeof(BoxCollider2D)) { tag = "Enimey" };
        var beam = new GameObject("BossBeamHit", typeof(BoxCollider2D)) { tag = "Enimey" };
        Check("a rock is a physical killer", DeathCrash.Classify(rock) == DeathCrash.KillerKind.Physical);
        Check("an alien is a physical killer", DeathCrash.Classify(alien) == DeathCrash.KillerKind.Physical);
        Check("a rail mine is a physical killer", DeathCrash.Classify(mine) == DeathCrash.KillerKind.Physical);
        Check("a boss shot is a projectile", DeathCrash.Classify(shot) == DeathCrash.KillerKind.Projectile);
        Check("a boss beam is a projectile", DeathCrash.Classify(beam) == DeathCrash.KillerKind.Projectile);
        Check("the boss body is the boss body", DeathCrash.Classify(body) == DeathCrash.KillerKind.BossBody);
        foreach (var go in new[] { rock, alien, mine, shotRoot, body, beam }) Cleanup(go);
    }

    static void FragmentCounts()
    {
        var seen = new HashSet<int>();
        var line = new System.Text.StringBuilder();
        bool inRange = true;
        foreach (int id in ShipId.All)
        {
            var sprite = ShipHullArt.Rest(id, 2);
            line.Append(ShipId.KeyOf(id)).Append(':');
            for (int v = 0; v < DeathCrash.Variants; v++)
            {
                int n = DeathCrash.FragmentsFor(id, sprite, v);
                inRange &= n >= DeathCrash.MinFragments && n <= DeathCrash.MaxFragments;
                seen.Add(n);
                line.Append(n);
            }
            line.Append(' ');
        }
        Debug.Log("[DCT] fragments per ship (variants): " + line);
        Check("every ship and variant breaks into 3-6 fragments", inRange);
        Check("ships break differently (more than one fragment count in use)", seen.Count > 1);
    }

    // Each ship, killed by a rock: fragments from its own art, every piece in
    // a rail, the rock too, panel only after, timing in bounds.
    static void EveryShip(int id)
    {
        string who = ShipId.KeyOf(id);
        var rig = new Rig(id);
        Sprite hullSprite = rig.hull.sprite;
        var rock = Killer("aestroid_1", "Astr", rig.ship.transform.position + new Vector3(.2f, .4f, 0f));
        rig.Hit(rock);
        var crash = DeathCrash.Instance;
        Check(who + ": the fatal hit kills the run", buttonClicks.playerDied);
        Check(who + ": the crash runs and the panel waits", crash != null && DeathCrash.Running && !DeathCrash.PanelReady);
        if (crash == null) { Cleanup(rock); rig.Dispose(); return; }

        int frags = crash.FragmentCount;
        Check(who + ": 3-6 fragments (" + frags + ")", frags >= 3 && frags <= 6);
        // The pieces are the hull's own pixels: each draws a part of it, and
        // together they hold every drawn pixel of the wrecked hull exactly once.
        var copy = DeathCrash.ReadableCopy(hullSprite);
        int hullPixels = DeathCrash.DrawnPixels(copy);
        Object.DestroyImmediate(copy);
        int sum = 0, mains = 0;
        bool part = true;
        for (int i = 0; i < crash.PieceCount; i++)
        {
            if (crash.KindOf(i) != DeathCrash.PieceKind.Hull) continue;
            var s = crash.SpriteOf(i);
            int n = s != null ? DeathCrash.DrawnPixels(s.texture) : 0;
            part &= n > 0 && n < hullPixels * .9f;
            sum += n;
            if (crash.IsMain(i)) mains++;
        }
        Check(who + ": every fragment is a part of the hull drawing", part && hullPixels > 100);
        Check(who + ": the fragments hold the hull's " + hullPixels + " drawn pixels exactly once (" + sum + ")", sum == hullPixels);
        Check(who + ": one main hull chunk", mains == 1);
        Check(who + ": hull hidden, hearts kept, drone hidden",
              !rig.hull.enabled && rig.heart.activeSelf && !rig.gun.activeSelf);
        Check(who + ": the world stays stopped (no scroll)", !TargetExplosion.WorldScrolling);

        bool panelEarly = false;
        float end = RunToEnd(crash, t => { if (DeathCrash.PanelReady && crash.LastLanding <= 0f) panelEarly = true; });
        Check(who + ": the panel never comes up before the last landing", !panelEarly);
        Check(who + ": the sequence ends in " + DeathCrash.MinTotal + "-" + DeathCrash.MaxTotal + "s (" + end.ToString("F2") + ")",
              end >= DeathCrash.MinTotal && end <= DeathCrash.MaxTotal + Dt);
        Check(who + ": the panel waits the settle after the last landing",
              crash.FinishedAt >= crash.LastLanding + DeathCrash.Settle - .001f);
        Check(who + ": then the panel is ready", DeathCrash.PanelReady && crash.Finished);
        Check(who + ": the ship is gone once it is over", rig.ship == null);

        RailChecks(who, crash);
        bool killerPiece = false;
        int mainSide = 0, killerSide = 0;
        for (int i = 0; i < crash.PieceCount; i++)
        {
            if (crash.KindOf(i) == DeathCrash.PieceKind.Killer) { killerPiece = true; killerSide = crash.SideOf(i); }
            if (crash.IsMain(i)) mainSide = crash.SideOf(i);
        }
        Check(who + ": the rock crashed into a rail", killerPiece && crash.KillerCrashed);
        Check(who + ": ...the other rail from the main chunk", killerSide == -mainSide);
        Check(who + ": the drone crashed too", Has(crash, DeathCrash.PieceKind.Drone));
        Cleanup(rock);
        rig.Dispose();
    }

    static bool Has(DeathCrash crash, DeathCrash.PieceKind kind)
    {
        for (int i = 0; i < crash.PieceCount; i++) if (crash.KindOf(i) == kind) return true;
        return false;
    }

    static void RailChecks(string who, DeathCrash crash)
    {
        bool onRail = true, inView = true, landed = true;
        float edge = crash.RailEdge;
        for (int i = 0; i < crash.PieceCount; i++)
        {
            var p = crash.PositionOf(i);
            landed &= crash.Landed(i);
            onRail &= Mathf.Abs(Mathf.Abs(p.x) - edge) <= .17f && Mathf.Sign(p.x) == crash.SideOf(i)
                      && (p - crash.TargetOf(i)).sqrMagnitude < 1e-4f;
            inView &= p.y > crash.ViewBottomAtStart + .5f && p.y < crash.ViewTopAtStart - .5f;
        }
        Check(who + ": every piece landed", landed);
        Check(who + ": every piece ends against a rail's inner face (+/-" + edge.ToString("F3") + ")", onRail);
        Check(who + ": every piece ends on screen", inView);
    }

    static void Killers()
    {
        // Ninja by an alien, Gold Warden by a mine: they crash too.
        foreach (var (id, name) in new[] { (11, EnemyRoster.AlienObjectName), (7, EnemyRoster.MineObjectName) })
        {
            var rig = new Rig(id);
            var k = Killer(name, "Enimey", rig.ship.transform.position + Vector3.up * .4f);
            rig.Hit(k);
            var crash = DeathCrash.Instance;
            RunToEnd(crash);
            Check(ShipId.KeyOf(id) + " by " + name + ": the killer crashes into a rail", crash.KillerCrashed && Has(crash, DeathCrash.PieceKind.Killer));
            RailChecks(ShipId.KeyOf(id) + " by " + name, crash);
            Cleanup(k);
            rig.Dispose();
        }

        // UFO by a boss shot: the shot dissipates, no killer piece.
        {
            var rig = new Rig(13);
            var shot = BossShot(rig.ship.transform.position + Vector3.up * .3f, out var root);
            rig.Hit(shot);
            var crash = DeathCrash.Instance;
            Check("UFO by a boss shot: the crash runs", DeathCrash.Running);
            Check("UFO by a boss shot: the shot does not crash", crash.Killer == DeathCrash.KillerKind.Projectile && !Has(crash, DeathCrash.PieceKind.Killer));
            RunToEnd(crash);
            Check("UFO by a boss shot: no killer crashed", !crash.KillerCrashed);
            RailChecks("UFO by a boss shot", crash);
            Cleanup(root);
            rig.Dispose();
        }

        // The boss body stays.
        {
            var rig = new Rig(1);
            var body = new GameObject("BossBody", typeof(BoxCollider2D)) { tag = "Enimey" };
            rig.Hit(body);
            var crash = DeathCrash.Instance;
            Check("boss body: stays (no killer piece)", crash.Killer == DeathCrash.KillerKind.BossBody && !Has(crash, DeathCrash.PieceKind.Killer));
            RunToEnd(crash);
            Cleanup(body);
            rig.Dispose();
        }
    }

    // The real gameS1 panel driver (playerIsDead) builds the panel only once
    // the crash is over.
    static void PanelWaits()
    {
        var driver = Object.FindFirstObjectByType<playerIsDead>(FindObjectsInactive.Include);
        Check("gameS1 has its playerIsDead", driver != null);
        if (driver == null) return;
        var update = typeof(playerIsDead).GetMethod("Update", Inst);
        var rig = new Rig(1);
        var rock = Killer("aestroid_1", "Astr", rig.ship.transform.position + Vector3.up * .4f);
        rig.Hit(rock);
        var crash = DeathCrash.Instance;
        bool early = false;
        for (float t = 0f; DeathCrash.Running && t < 4f; t += Dt)
        {
            update.Invoke(driver, null);
            if (Object.FindFirstObjectByType<DeathPanelView>(FindObjectsInactive.Include) != null) early = true;
            crash.Step(Dt);
        }
        Check("the Flight Complete panel is not built during the crash", !early);
        update.Invoke(driver, null);
        Check("the Flight Complete panel is built once it is over",
              Object.FindFirstObjectByType<DeathPanelView>(FindObjectsInactive.Include) != null);
        Cleanup(rock);
        rig.Dispose();
    }

    static void TapToSkip()
    {
        var rig = new Rig(2);
        var rock = Killer("aestroid_1", "Astr", rig.ship.transform.position + Vector3.up * .4f);
        rig.Hit(rock);
        var crash = DeathCrash.Instance;
        for (float t = 0f; t < DeathCrash.SkipAfter - .1f; t += Dt) crash.Step(Dt);
        Check("a tap before " + DeathCrash.SkipAfter + "s does not skip", !crash.Skip() && DeathCrash.Running);
        for (float t = 0f; t < .12f; t += Dt) crash.Step(Dt);
        Check("a tap after " + DeathCrash.SkipAfter + "s skips straight to the panel", crash.Skip() && DeathCrash.PanelReady && crash.Skipped);
        Check("skipped: the ship is gone", rig.ship == null);
        RailChecks("skipped", crash);
        Cleanup(rock);
        rig.Dispose();
    }

    static void Revive()
    {
        var rig = new Rig(5);
        var rock = Killer("aestroid_1", "Astr", rig.ship.transform.position + Vector3.up * .4f);
        rig.Hit(rock);
        var crash = DeathCrash.Instance;
        for (float t = 0f; t < .5f; t += Dt) crash.Step(Dt);
        crash.Cancel(true);
        var col = rig.ship != null ? rig.ship.GetComponent<BoxCollider2D>() : null;
        Check("revive: the crash stops", !DeathCrash.Running && !DeathCrash.Animating);
        Check("revive: the ship is restored (hull, collider, drone, hearts)",
              rig.ship != null && rig.hull.enabled && col != null && col.enabled && rig.gun.activeSelf && rig.heart.activeSelf);
        bool anyPiece = false;
        for (int i = 0; i < crash.PieceCount; i++) anyPiece |= crash.PieceVisible(i);
        Check("revive: no wreckage left on screen", !anyPiece && crash.LiveParticles == 0);
        Cleanup(rock);
        rig.Dispose();
    }

    static void NoAllocations()
    {
        // A warm-up death (the fragment set is cut and cached), then a second.
        for (int pass = 0; pass < 2; pass++)
        {
            var rig = new Rig(1);
            var rock = Killer("aestroid_1", "Astr", rig.ship.transform.position + Vector3.up * .4f);
            rig.Hit(rock);
            var crash = DeathCrash.Instance;
            int allocFrames = 0, frames = 0;
            for (float t = 0f; DeathCrash.Animating && t < 5f; t += Dt)
            {
                int impacts = crash.Impacts;
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                crash.Step(Dt);
                long after = System.GC.GetAllocatedBytesForCurrentThread();
                if (crash.Impacts == impacts && crash.IsRunning) { frames++; if (after != before) allocFrames++; }
            }
            if (pass == 1) Check("a crash frame allocates nothing (" + allocFrames + "/" + frames + " frames allocated)", allocFrames == 0 && frames > 30);
            Cleanup(rock);
            rig.Dispose();
        }
    }

    static void TallScreens()
    {
        var cam = Camera.main;
        Check("gameS1 has an orthographic main camera", cam != null && cam.orthographic);
        if (cam == null) return;
        float baseSize = cam.orthographicSize;
        float baseAspect = cam.aspect;
        foreach (var s in TallScreenTest.Screens)
        {
            cam.orthographicSize = CameraFit.ComputeSize(baseSize, 2.85f, s.w, s.h);
            cam.aspect = (float)s.w / s.h;
            foreach (int id in new[] { 1, 11, 7, 13 })
            {
                var rig = new Rig(id);
                var rock = Killer("aestroid_1", "Astr", rig.ship.transform.position + Vector3.up * .4f);
                rig.Hit(rock);
                var crash = DeathCrash.Instance;
                float end = RunToEnd(crash);
                string who = s.name + " " + ShipId.KeyOf(id);
                Check(who + ": ends in bounds (" + end.ToString("F2") + "s)", end >= DeathCrash.MinTotal && end <= DeathCrash.MaxTotal + Dt);
                bool inView = true, onRail = true;
                float top = CameraFit.ViewTop, bottom = CameraFit.ViewBottom;
                for (int i = 0; i < crash.PieceCount; i++)
                {
                    var p = crash.PositionOf(i);
                    inView &= p.y > bottom + .5f && p.y < top - .5f;
                    onRail &= Mathf.Abs(Mathf.Abs(p.x) - crash.RailEdge) <= .17f;
                }
                Check(who + ": every piece on a rail, on screen", inView && onRail);
                Cleanup(rock);
                rig.Dispose();
            }
        }
        cam.orthographicSize = baseSize;
        cam.aspect = baseAspect;
    }
}
