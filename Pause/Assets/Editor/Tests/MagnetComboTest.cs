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
//     are never pulled, and nothing is pulled in the tutorial.
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

    // Magnet then shield vs shield then magnet: from the frame both are up,
    // the enemy moves exactly the same way.
    static void BothOrdersPullTheSame()
    {
        Vector3 start = new Vector3(2f, 4f, 0f);
        Vector3[] after = new Vector3[2];
        bool[] on = new bool[2];
        for (int order = 0; order < 2; order++)
        {
            var ship = NewShip();
            var enemy = Body("enemy", "Enimey", start);
            collisionDetection.atomCheck = false;
            MagnetPull pull;
            if (order == 0)
            {
                // magnet first: a pull without the shield moves no enemy...
                pull = MagnetPull.Begin(ship);
                pull.Step(.1f);
                Check("magnet first: no enemy moves before the shield", enemy.transform.position == start && !pull.Shielded);
                // ...then the shield is caught
                collisionDetection.atomCheck = true;
                MagnetPull.OnShieldCaught();
            }
            else
            {
                // shield first, then the magnet
                collisionDetection.atomCheck = true;
                pull = MagnetPull.Begin(ship);
            }
            for (int i = 0; i < 10; i++) pull.Step(.05f);
            after[order] = enemy.transform.position;
            on[order] = pull.Shielded;
            Check("order " + (order == 0 ? "magnet->shield" : "shield->magnet") + ": the enemy is sucked toward the hull and its mover held",
                  pull.Shielded && (after[order] - ship.transform.position).magnitude < (start - ship.transform.position).magnitude - 1f &&
                  !enemy.GetComponent<moveItemEnmInStrightLine>().enabled);
            Done(ship);
        }
        Check("the activation order cannot change the result", on[0] && on[1] && (after[0] - after[1]).sqrMagnitude < 1e-8f);
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
