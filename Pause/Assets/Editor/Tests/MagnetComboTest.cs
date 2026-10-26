using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// The magnet + shield combo (MagnetCombo, applied by MagnetPull):
//   * magnet alone pulls the goodies (dust, atoms) and nothing else;
//   * shield alone pulls nothing;
//   * both up pulls the plain enemies in too, whichever was caught first,
//     and the moment either ends the enemies are let go;
//   * bosses, elites, what elites fly, shots, rail mines, rocks and scenery
//     are never pulled, and nothing is pulled in the tutorial;
//   * catching either atom while the other runs restarts BOTH to the synced
//     length (both orders, any number of times: chaining), and they end
//     together; neither atom creates the other power;
//   * the pull field is 90% of the camera's visible rect at any aspect;
//   * the big combo callout: on the combo's start in both orders, "CHAIN xN"
//     on re-chains, never for one power alone, without a HUD or in the tutorial.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod MagnetComboTest.Run
public static class MagnetComboTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[MC] PASS  " : "[MC] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            startMenu.youAreInTutorial = false;
            PureRules();
            Classification();
            SpeedRamp();
            BothOrdersPullTheSame();
            SyncTimers();
            ChainedPickupsKeepTheVacuum();
            Range();
            Callouts();
            EitherEndingLetsGo();
            ExcludedBodiesStayPut();
            TutorialPullsNoEnemy();
        }
        finally
        {
            collisionDetection.atomCheck = false;
            collisionDetection.invTimer = 0f;
            startMenu.youAreInTutorial = false;
            ClearTarget.Live.Clear();
        }
        Debug.Log("[MC] failures: " + fails);
        return fails;
    }

    static readonly MagnetCombo.Kind[] AllKinds = (MagnetCombo.Kind[])System.Enum.GetValues(typeof(MagnetCombo.Kind));

    static void PureRules()
    {
        Check("enemy pull: only with the magnet running AND the shield up, never in the tutorial",
              MagnetCombo.EnemyPullActive(true, true, false) &&
              !MagnetCombo.EnemyPullActive(true, false, false) &&
              !MagnetCombo.EnemyPullActive(false, true, false) &&
              !MagnetCombo.EnemyPullActive(false, false, false) &&
              !MagnetCombo.EnemyPullActive(true, true, true));

        bool magnetAlone = true, shieldAlone = true, both = true;
        foreach (var k in AllKinds)
        {
            bool goodie = k == MagnetCombo.Kind.Dust || k == MagnetCombo.Kind.Atom;
            magnetAlone &= MagnetCombo.Pulls(k, true, false, false) == goodie;
            shieldAlone &= !MagnetCombo.Pulls(k, false, true, false);
            both &= MagnetCombo.Pulls(k, true, true, false) == (goodie || k == MagnetCombo.Kind.Enemy);
        }
        Check("magnet alone pulls the goodies (dust and atoms) and nothing else", magnetAlone);
        Check("shield alone pulls nothing", shieldAlone);
        Check("magnet + shield pulls the goodies and the plain enemies, nothing else", both);
        Check("bosses, elites, elite-owned bodies, shots, rail mines, rocks and scenery are never pulled, even in the combo",
              !MagnetCombo.Pulls(MagnetCombo.Kind.Boss, true, true, false) &&
              !MagnetCombo.Pulls(MagnetCombo.Kind.Elite, true, true, false) &&
              !MagnetCombo.Pulls(MagnetCombo.Kind.EliteOwned, true, true, false) &&
              !MagnetCombo.Pulls(MagnetCombo.Kind.HostileShot, true, true, false) &&
              !MagnetCombo.Pulls(MagnetCombo.Kind.RailMine, true, true, false) &&
              !MagnetCombo.Pulls(MagnetCombo.Kind.Rock, true, true, false) &&
              !MagnetCombo.Pulls(MagnetCombo.Kind.Scenery, true, true, false));
        Check("the tutorial keeps the goodies pull but never pulls an enemy",
              MagnetCombo.Pulls(MagnetCombo.Kind.Dust, true, true, true) && !MagnetCombo.Pulls(MagnetCombo.Kind.Enemy, true, true, true));
    }

    static MagnetCombo.Kind C(bool scenery = false, bool pickup = false, bool dust = false, bool rock = false, bool enemy = false,
                              bool boss = false, bool elite = false, bool owned = false, bool shot = false, bool mine = false)
    {
        return MagnetCombo.Classify(scenery, pickup, dust, rock, enemy, boss, elite, owned, shot, mine);
    }

    static void Classification()
    {
        Check("pickups: star dust is Dust, any other pickup an Atom",
              C(pickup: true, dust: true) == MagnetCombo.Kind.Dust && C(pickup: true) == MagnetCombo.Kind.Atom);
        Check("a plain Enimey body is an Enemy, an Astr body a Rock, an untagged one Other",
              C(enemy: true) == MagnetCombo.Kind.Enemy && C(rock: true) == MagnetCombo.Kind.Rock && C() == MagnetCombo.Kind.Other);
        Check("an elite is an Elite even though it is also a special hit target",
              C(enemy: true, boss: true, elite: true) == MagnetCombo.Kind.Elite);
        Check("bosses, elite-owned bodies, shots and rail mines classify as themselves",
              C(enemy: true, boss: true) == MagnetCombo.Kind.Boss &&
              C(enemy: true, owned: true) == MagnetCombo.Kind.EliteOwned &&
              C(enemy: true, shot: true) == MagnetCombo.Kind.HostileShot &&
              C(enemy: true, mine: true) == MagnetCombo.Kind.RailMine);
        Check("scenery wins over every tag", C(scenery: true, enemy: true) == MagnetCombo.Kind.Scenery);
    }

    static void SpeedRamp()
    {
        float h = 15f;
        float s0 = MagnetCombo.EnemySpeed(0f, h), sHalf = MagnetCombo.EnemySpeed(.5f, h), s1 = MagnetCombo.EnemySpeed(MagnetCombo.EnemyRampSeconds, h);
        Check("the enemy pull ramps from a slide to a rush",
              Mathf.Approximately(s0, MagnetCombo.EnemySpeedStartPerView * h) && sHalf > s0 && s1 > sHalf &&
              Mathf.Approximately(s1, MagnetCombo.EnemySpeedEndPerView * h) &&
              Mathf.Approximately(MagnetCombo.EnemySpeed(99f, h), s1));
        Check("the speed follows the view height, clamped (no camera: the max)",
              MagnetCombo.EnemySpeed(1f, 2e5f) == MagnetCombo.EnemySpeed(1f, MagnetCombo.MaxViewHeight) &&
              MagnetCombo.EnemySpeed(1f, 1f) == MagnetCombo.EnemySpeed(1f, MagnetCombo.MinViewHeight));
        // ramped, an enemy at the top of a tall view reaches the hull well inside the shortest combo (the 2 s shield extension)
        float travelled = 0f;
        for (float t = 0f; t < 2f; t += .02f) travelled += MagnetCombo.EnemySpeed(t, 17.4f) * .02f;
        Check("within 2 s of combo an enemy crosses a whole 17.4 u view", travelled > 17.4f);
    }

    // ---- headless scenes ----

    static GameObject NewShip()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        ClearTarget.Live.Clear();
        var ship = new GameObject("~comboShip");
        ship.transform.position = new Vector3(0f, -3f, 0f);
        return ship;
    }

    static GameObject Body(string name, string tag, Vector3 at, bool mover = true)
    {
        var go = new GameObject(name);
        go.tag = tag;
        go.transform.position = at;
        if (mover) go.AddComponent<moveItemEnmInStrightLine>();
        var ct = ClearTarget.Ensure(go);
        if (!ClearTarget.Live.Contains(ct)) ClearTarget.Live.Add(ct);
        return go;
    }

    static void Done(GameObject ship)
    {
        Object.DestroyImmediate(ship);
        ClearTarget.Live.Clear();
        collisionDetection.atomCheck = false;
    }

    // One running frame as the game has it: the shield's clock
    // (collisionDetection.turnTextsOff) then the pull.
    static void Frame(MagnetPull pull, float dt)
    {
        if (collisionDetection.atomCheck)
        {
            collisionDetection.invTimer -= dt;
            if (collisionDetection.invTimer <= 0f) collisionDetection.atomCheck = false;
        }
        pull.Step(dt);
    }

    // collisionDetection's shield catch: the shield's own clock, then the sync if a magnet runs.
    static void CatchShield(Vector3 at)
    {
        bool comboBefore = MagnetPull.ComboOn, magnet = MagnetPull.MagnetRunning;
        collisionDetection.atomCheck = true;
        collisionDetection.invTimer = collisionDetection.ShieldAtomSeconds;
        if (magnet) collisionDetection.SyncMagnetShield(comboBefore, at);
    }

    // collisionDetection's magnet catch: the pull, then the sync if a shield is up.
    static MagnetPull CatchMagnet(GameObject ship)
    {
        bool comboBefore = MagnetPull.ComboOn, shielded = collisionDetection.atomCheck;
        var pull = MagnetPull.Begin(ship);
        if (shielded) collisionDetection.SyncMagnetShield(comboBefore, ship.transform.position);
        return pull;
    }

    // Magnet then shield vs shield then magnet: from the frame both are up,
    // the enemy moves exactly the same way and both clocks read the same.
    static void BothOrdersPullTheSame()
    {
        Vector3 start = new Vector3(2f, 4f, 0f);
        Vector3[] after = new Vector3[2];
        bool[] on = new bool[2];
        float[] magnetLeft = new float[2], shieldLeft = new float[2];
        for (int order = 0; order < 2; order++)
        {
            var ship = NewShip();
            var enemy = Body("enemy", "Enimey", start);
            collisionDetection.atomCheck = false;
            collisionDetection.invTimer = 0f;
            MagnetPull pull;
            if (order == 0)
            {
                // magnet first: a pull without the shield moves no enemy...
                pull = CatchMagnet(ship);
                Frame(pull, .1f);
                Check("magnet first: no enemy moves before the shield, no shield is raised",
                      enemy.transform.position == start && !pull.Shielded && !collisionDetection.atomCheck);
                // ...then the shield is caught
                CatchShield(ship.transform.position);
            }
            else
            {
                // shield first: no magnet is started by it...
                CatchShield(ship.transform.position);
                Check("shield first: no magnet pull is started", !MagnetPull.MagnetRunning || MagnetPull.Active == null || !MagnetPull.Active.Running);
                // ...then the magnet
                pull = CatchMagnet(ship);
            }
            Check("order " + (order == 0 ? "magnet->shield" : "shield->magnet") + ": both restart to the synced length",
                  Mathf.Approximately(pull.Remaining, MagnetCombo.SyncSeconds) &&
                  Mathf.Approximately(collisionDetection.invTimer, MagnetCombo.SyncSeconds) && pull.Synced && MagnetPull.Chain == 1);
            for (int i = 0; i < 10; i++) Frame(pull, .05f);
            after[order] = enemy.transform.position;
            on[order] = pull.Shielded;
            magnetLeft[order] = pull.Remaining;
            shieldLeft[order] = collisionDetection.invTimer;
            Check("order " + (order == 0 ? "magnet->shield" : "shield->magnet") + ": the enemy is sucked toward the hull and its mover held",
                  pull.Shielded && (after[order] - ship.transform.position).magnitude < (start - ship.transform.position).magnitude - 1f &&
                  !enemy.GetComponent<moveItemEnmInStrightLine>().enabled);
            pull.End();
            Done(ship);
        }
        Check("the activation order cannot change the result",
              on[0] && on[1] && (after[0] - after[1]).sqrMagnitude < 1e-8f &&
              Mathf.Abs(magnetLeft[0] - magnetLeft[1]) < 1e-4f && Mathf.Abs(shieldLeft[0] - shieldLeft[1]) < 1e-4f &&
              Mathf.Abs(magnetLeft[0] - shieldLeft[0]) < 1e-4f);
    }

    static void SyncTimers()
    {
        var off = new MagnetCombo.Timers();
        var m = MagnetCombo.AfterPickup(off, MagnetCombo.Pickup.Magnet);
        var sh = MagnetCombo.AfterPickup(off, MagnetCombo.Pickup.Shield);
        Check("a magnet alone starts only the magnet, at its own 6 s (no shield created)",
              m.magnet == MagnetCombo.MagnetSeconds && m.shield == 0f && !m.Combo);
        Check("a shield alone starts only the shield, at its own 5.8 s (no magnet created)",
              sh.shield == MagnetCombo.ShieldSeconds && sh.magnet == 0f && !sh.Combo);
        Check("the synced length is the longer of the two (6 s): syncing never shortens either",
              MagnetCombo.SyncSeconds == Mathf.Max(MagnetCombo.MagnetSeconds, MagnetCombo.ShieldSeconds) && MagnetCombo.SyncSeconds == 6f);
        var ms = MagnetCombo.AfterPickup(MagnetCombo.Tick(m, 3f), MagnetCombo.Pickup.Shield);
        var sm = MagnetCombo.AfterPickup(MagnetCombo.Tick(sh, 3f), MagnetCombo.Pickup.Magnet);
        Check("magnet then shield: both restart to full together",
              ms.magnet == MagnetCombo.SyncSeconds && ms.shield == MagnetCombo.SyncSeconds);
        Check("shield then magnet: both restart to full together (symmetric)",
              sm.magnet == MagnetCombo.SyncSeconds && sm.shield == MagnetCombo.SyncSeconds);
        var late = MagnetCombo.AfterPickup(MagnetCombo.Tick(ms, 5.5f), MagnetCombo.Pickup.Magnet);
        var late2 = MagnetCombo.AfterPickup(MagnetCombo.Tick(ms, 5.5f), MagnetCombo.Pickup.Shield);
        Check("a refresh of either mid-combo resets both to full (a reset, not banked time)",
              late.magnet == MagnetCombo.SyncSeconds && late.shield == MagnetCombo.SyncSeconds &&
              late2.magnet == MagnetCombo.SyncSeconds && late2.shield == MagnetCombo.SyncSeconds);
        var a = MagnetCombo.AfterPickup(MagnetCombo.AfterPickup(ms, MagnetCombo.Pickup.Shield), MagnetCombo.Pickup.Magnet);
        Check("two catches in the same instant bank nothing past one full length", a.magnet == MagnetCombo.SyncSeconds && a.shield == MagnetCombo.SyncSeconds);
        var expired = MagnetCombo.AfterPickup(MagnetCombo.Tick(m, 7f), MagnetCombo.Pickup.Shield);
        Check("a shield after the magnet has run out is just a shield", expired.magnet == 0f && expired.shield == MagnetCombo.ShieldSeconds);
        var t = ms;
        bool together = true;
        for (int i = 0; i < 100 && t.Combo; i++) { t = MagnetCombo.Tick(t, .1f); together &= t.MagnetOn == t.ShieldOn; }
        Check("synced, the two run out on the same tick", together && !t.MagnetOn && !t.ShieldOn);
    }

    // 3+ catches in a row keep both alive with the enemy pull on throughout,
    // and both expire together one full SyncSeconds after the last catch.
    static void ChainedPickupsKeepTheVacuum()
    {
        // pure model
        var t = MagnetCombo.AfterPickup(new MagnetCombo.Timers(), MagnetCombo.Pickup.Shield);
        var order = new[] { MagnetCombo.Pickup.Magnet, MagnetCombo.Pickup.Shield, MagnetCombo.Pickup.Magnet, MagnetCombo.Pickup.Shield, MagnetCombo.Pickup.Shield };
        bool alive = true;
        int chain = 0;
        bool before = false;
        for (int k = 0; k < order.Length; k++)
        {
            for (int i = 0; i < 40; i++) { t = MagnetCombo.Tick(t, .1f); if (k > 0) alive &= t.Combo; }
            before = t.Combo;
            t = MagnetCombo.AfterPickup(t, order[k]);
            chain = MagnetCombo.NextChain(chain, before);
        }
        Check("pure: five chained catches keep both alive throughout, chain counted to 5", alive && t.Combo && chain == 5);

        // the game's pull, driven like the game drives it
        var ship = NewShip();
        Body("enemy", "Enimey", new Vector3(3f, 7f, 0f));
        collisionDetection.atomCheck = false;
        collisionDetection.invTimer = 0f;
        CatchShield(ship.transform.position);
        collisionDetection.invTimer -= 1f;                   // a second of shield alone
        var pull = CatchMagnet(ship);                        // combo starts: chain 1
        bool pulling = true, timersEqual = true;
        int catches = 1;
        for (int round = 0; round < 4; round++)
        {
            for (int i = 0; i < 40; i++)
            {
                Frame(pull, .1f);
                pulling &= pull.Shielded && pull.Running && collisionDetection.atomCheck;
                timersEqual &= Mathf.Abs(pull.Remaining - collisionDetection.invTimer) < 1e-3f;
            }
            if (round % 2 == 0) CatchShield(ship.transform.position); else pull = CatchMagnet(ship);
            catches++;
        }
        Check("game: 4 re-chains in a row (16 s, far past one length) keep the enemy pull on every frame",
              pulling && timersEqual && MagnetPull.Chain == catches);
        int frames = 0;
        bool stillOn = true;
        while (pull.Running && frames < 200)
        {
            Frame(pull, .1f);
            frames++;
            if (pull.Running) stillOn &= pull.Shielded && collisionDetection.atomCheck;
        }
        Check("game: after the last catch both expire on the same frame, one full length later, and the enemy pull stops that instant",
              stillOn && !pull.Running && !collisionDetection.atomCheck && !pull.Shielded && pull.EnemiesLocked == 0 &&
              Mathf.Abs(frames * .1f - MagnetCombo.SyncSeconds) <= .11f && MagnetPull.Chain == 0);
        Done(ship);
    }

    static void Range()
    {
        // (orthographic size from CameraFit: the gameplay half width 3.72 u / aspect)
        float[] aspects = { 9f / 16f, 9f / 20f, 9f / 21f, 3f / 4f };
        bool ok = true;
        foreach (float a in aspects)
        {
            float ortho = CameraFit.GameplayHalfWidth / a;
            if (a > .7f) ortho = 5f;   // a tablet: the authored height
            var view = MagnetCombo.ViewRect(new Vector2(0f, 1f), ortho, a);
            var field = MagnetCombo.PullRect(view);
            float share = field.width * field.height / (view.width * view.height);
            bool inside = field.xMin >= view.xMin - 1e-4f && field.xMax <= view.xMax + 1e-4f &&
                          field.yMin >= view.yMin - 1e-4f && field.yMax <= view.yMax + 1e-4f;
            bool centred = (field.center - view.center).sqrMagnitude < 1e-8f;
            Debug.Log("[MC] aspect " + a.ToString("F3") + ": view " + view.width.ToString("F2") + " x " + view.height.ToString("F2") +
                      " u, pull field " + field.width.ToString("F2") + " x " + field.height.ToString("F2") + " u = " + (share * 100f).ToString("F1") + "%");
            ok &= Mathf.Abs(share - MagnetCombo.AreaShare) < .005f && inside && centred;
        }
        Check("the pull field is 90% of the visible rect, centred on it, at 9:16, 9:20, 9:21 and 3:4", ok);
        var none = MagnetCombo.PullRect(new Rect(-1e5f, -1e5f, 2e5f, 2e5f));
        var tiny = MagnetCombo.PullRect(new Rect(-.5f, -.5f, 1f, 1f));
        Check("no camera / a tiny view: the field is clamped",
              Mathf.Approximately(none.width, MagnetCombo.MaxHalfWidth * 2f) && Mathf.Approximately(none.height, MagnetCombo.MaxHalfHeight * 2f) &&
              Mathf.Approximately(tiny.width, MagnetCombo.MinHalfWidth * 2f) && Mathf.Approximately(tiny.height, MagnetCombo.MinHalfHeight * 2f));
        // bodies outside the field are left alone, inside are pulled
        var ship = NewShip();
        var cam = new GameObject("cam").AddComponent<Camera>();
        cam.tag = "MainCamera";
        cam.orthographic = true;
        cam.orthographicSize = 8f;
        cam.aspect = .45f;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        var view2 = ShipTargets.View();
        var field2 = MagnetCombo.PullRect(view2);
        var inDust = Body("smStar1", "pickUp", new Vector3(0f, field2.yMax - .1f, 0f));
        var edgeDust = Body("LargeStar1", "pickUp", new Vector3(0f, (field2.yMax + view2.yMax) * .5f, 0f));
        Vector3 inStart = inDust.transform.position, edgeStart = edgeDust.transform.position;
        collisionDetection.atomCheck = false;
        var pull = MagnetPull.Begin(ship);
        pull.Step(.05f);
        Check("with a real camera: dust inside the field is pulled, dust in the outer 10% band waits",
              Camera.main == cam && inDust.transform.position != inStart && edgeDust.transform.position == edgeStart);
        pull.End();
        Object.DestroyImmediate(cam.gameObject);
        Done(ship);
    }

    static void Callouts()
    {
        Check("combo start announces the full callout (comboBefore false), whichever atom completed it",
              MagnetCombo.CalloutFor(false, true, true, false, false) == MagnetCombo.Callout.Combo);
        Check("a re-chain announces CHAIN", MagnetCombo.CalloutFor(true, true, true, false, false) == MagnetCombo.Callout.Chain);
        Check("magnet only or shield only: no combo callout",
              MagnetCombo.CalloutFor(false, false, true, false, false) == MagnetCombo.Callout.None);
        Check("no HUD, the tutorial or the kill cam replay: no callout",
              MagnetCombo.CalloutFor(false, true, false, false, false) == MagnetCombo.Callout.None &&
              MagnetCombo.CalloutFor(false, true, true, true, false) == MagnetCombo.Callout.None &&
              MagnetCombo.CalloutFor(true, true, true, false, true) == MagnetCombo.Callout.None);
        int c = MagnetCombo.NextChain(0, false);
        int c2 = MagnetCombo.NextChain(c, true), c3 = MagnetCombo.NextChain(c2, true);
        Check("the chain counts 1, 2, 3 and restarts at 1 on a fresh combo",
              c == 1 && c2 == 2 && c3 == 3 && MagnetCombo.NextChain(c3, false) == 1);
        Check("the words: the full two-line callout, then CHAIN x2, x3 (built once, the same string each time)",
              MagnetCombo.WordFor(MagnetCombo.Callout.Combo, 1) == MagnetCombo.ComboWord &&
              MagnetCombo.WordFor(MagnetCombo.Callout.Chain, 2) == "CHAIN x2" &&
              MagnetCombo.WordFor(MagnetCombo.Callout.Chain, 3) == "CHAIN x3" &&
              ReferenceEquals(MagnetCombo.WordFor(MagnetCombo.Callout.Chain, 3), MagnetCombo.WordFor(MagnetCombo.Callout.Chain, 3)) &&
              MagnetCombo.WordFor(MagnetCombo.Callout.None, 1) == null && MagnetCombo.WordFor(MagnetCombo.Callout.Chain, 40) == "CHAIN x40");
        Check("the callout is bigger, longer and higher than a pickup word",
              MagnetCombo.CalloutSize > 28 && MagnetCombo.CalloutSeconds > .8f && MagnetCombo.CalloutRise > 50f &&
              MagnetCombo.CalloutLift > collisionDetection.PickupWordLift);
        // the game path with no HUD (edit mode): announces nothing, still syncs
        var ship = NewShip();
        collisionDetection.atomCheck = true;
        collisionDetection.invTimer = 3f;
        MagnetPull.Begin(ship);
        var shown = collisionDetection.SyncMagnetShield(false, ship.transform.position);
        Check("no HUD: the sync still happens, nothing is announced",
              shown == MagnetCombo.Callout.None && Mathf.Approximately(collisionDetection.invTimer, MagnetCombo.SyncSeconds));
        MagnetPull.Active.End();
        Done(ship);
    }

    static void EitherEndingLetsGo()
    {
        // the shield ends first: the enemy is let go at once, the dust pull goes on
        {
            var ship = NewShip();
            var enemy = Body("enemy", "Enimey", new Vector3(2f, 4f, 0f));
            var dust = Body("smStar1", "pickUp", new Vector3(-2f, 5f, 0f));
            collisionDetection.atomCheck = true;
            var pull = MagnetPull.Begin(ship);
            pull.Step(.1f);
            var mover = enemy.GetComponent<moveItemEnmInStrightLine>();
            Check("combo on: enemy held", !mover.enabled && pull.EnemiesLocked > 0);
            collisionDetection.atomCheck = false;   // the shield runs out
            Vector3 held = enemy.transform.position, dustAt = dust.transform.position;
            pull.Step(.1f);
            Check("shield gone: the enemy pull ends that frame and its mover is back on",
                  !pull.Shielded && enemy.transform.position == held && mover.enabled && pull.EnemiesLocked == 0);
            Check("... while the magnet still pulls the dust", dust.transform.position != dustAt && pull.Running);
            collisionDetection.atomCheck = true;    // a fresh shield while the magnet still runs
            pull.Step(.1f);
            Check("a fresh shield mid-pull switches the enemy pull back on", pull.Shielded && enemy.transform.position != held);
            Done(ship);
        }
        // the magnet ends first: the shield alone pulls nothing
        {
            var ship = NewShip();
            var enemy = Body("enemy", "Enimey", new Vector3(2f, 4f, 0f));
            collisionDetection.atomCheck = true;
            var pull = MagnetPull.Begin(ship);
            pull.Step(.1f);
            int guard = 0;
            while (pull.Running && guard++ < 1000) pull.Step(.1f);
            var mover = enemy.GetComponent<moveItemEnmInStrightLine>();
            Vector3 held = enemy.transform.position;
            pull.Step(.1f);
            Check("magnet over: the enemy is let go though the shield is still up",
                  !pull.Running && !pull.Shielded && mover.enabled && pull.EnemiesLocked == 0 && enemy.transform.position == held);
            Done(ship);
        }
        // re-catching the magnet while the combo runs keeps it on
        {
            var ship = NewShip();
            Body("enemy", "Enimey", new Vector3(2f, 4f, 0f));
            collisionDetection.atomCheck = true;
            var pull = MagnetPull.Begin(ship);
            for (int i = 0; i < 20; i++) pull.Step(.1f);
            var again = MagnetPull.Begin(ship);
            again.Step(.1f);
            Check("a magnet re-caught mid-combo: same pull, fresh clock, combo still on",
                  again == pull && pull.Shielded && pull.Remaining > MagnetPull.PullSeconds - .2f);
            Done(ship);
        }
    }

    static void ExcludedBodiesStayPut()
    {
        var ship = NewShip();
        var rock = Body("rock", "Astr", new Vector3(2f, 4f, 0f));
        var boss = Body("boss", "Enimey", new Vector3(-2f, 4f, 0f), mover: false);
        boss.AddComponent<BossTarget>();
        var untagged = Body("scenery", "Untagged", new Vector3(0f, 5f, 0f));
        var sceneryEnemy = Body("backdropRock", "Enimey", new Vector3(1f, 6f, 0f));
        sceneryEnemy.GetComponent<ClearTarget>().Scenery = true;
        Vector3 r = rock.transform.position, b = boss.transform.position, u = untagged.transform.position, s = sceneryEnemy.transform.position;
        collisionDetection.atomCheck = true;
        var pull = MagnetPull.Begin(ship);
        for (int i = 0; i < 10; i++) pull.Step(.1f);
        Check("in the combo a rock, a boss, scenery and an untagged body are never moved",
              rock.transform.position == r && boss.transform.position == b && untagged.transform.position == u &&
              sceneryEnemy.transform.position == s && rock.GetComponent<moveItemEnmInStrightLine>().enabled);
        Done(ship);
    }

    static void TutorialPullsNoEnemy()
    {
        var ship = NewShip();
        var enemy = Body("enemy", "Enimey", new Vector3(2f, 4f, 0f));
        Vector3 start = enemy.transform.position;
        startMenu.youAreInTutorial = true;
        collisionDetection.atomCheck = true;
        var pull = MagnetPull.Begin(ship);
        for (int i = 0; i < 5; i++) pull.Step(.1f);
        Check("tutorial: magnet + shield leaves the enemies where they are", enemy.transform.position == start && !pull.Shielded);
        startMenu.youAreInTutorial = false;
        Done(ship);
    }
}
