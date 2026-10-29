using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// Feature: signature upgrade effects of Jade Phantom (chill), Gold Warden (pierce, drift), Paranoid (bigger
// blast) and Saboteur (bigger blast, chain), unlocked by weapon level on top of the plain tracks
// (ShipSignatures2). Each effect is OFF below its unlock level and ON at / after it.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ShipSignatures2Test.Run
public static class ShipSignatures2Test
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SG2] PASS  " : "[SG2] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        TablesUnlockAtTheirLevels();
        LabelsMentionTheEffectsAndKeepTheNumbers();
        JadeChillsOutsideTheSplash();
        GoldBoltsPierceAndSentriesDrift();
        ParanoidBlastGrowsWithTheLevel();
        SaboteurBlastGrowsAndChains();

        AttackPool.StopAll();
        Debug.Log("[SG2] failures: " + fails);
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
        moveBackGround.speed = 0f;   // a still world: mines and bodies stay where the test put them
        Time.timeScale = 1f;
        EnemyShove.Clear();
    }

    static GameObject Hazard(float x, float y)
    {
        var go = new GameObject("hazard");
        go.tag = "Enimey";
        go.transform.position = new Vector3(x, y, 0f);
        ClearTarget.Ensure(go).SetRadius(.3f);
        return go;
    }

    static bool Alive(GameObject go) { return go != null; }

    static ShipPowerController Ship(int id, int level)
    {
        SetLevel(id, level);
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

    static void Teardown(ShipPowerController c, int id)
    {
        if (c != null)
        {
            c.SendMessage("OnDestroy");
            if (c.Runner != null) c.Runner.SendMessage("OnDestroy");
            if (c.Secret != null) c.Secret.SendMessage("OnDestroy");
            Object.DestroyImmediate(c.gameObject);
        }
        AttackPool.StopAll();
        EnemyShove.Clear();
        collisionDetection.cloakTimer = 0f;
        collisionDetection.lifeCounter = 0;
        SetLevel(id, 0);
    }

    static void Steps(ShipAttackRunner r, float seconds, float dt = .02f)
    {
        for (float t = 0f; t < seconds - 1e-4f; t += dt) r.Step(dt);
    }

    static void Destroy(params GameObject[] list)
    {
        foreach (var g in list) if (g != null) Object.DestroyImmediate(g);
    }

    // ---- cases ---------------------------------------------------------

    static void TablesUnlockAtTheirLevels()
    {
        Check("chill is off at levels 0-1 and on from level 2",
              !ShipSignatures2.ChillsAt(0) && !ShipSignatures2.ChillsAt(1) && ShipSignatures2.ChillsAt(2) && ShipSignatures2.ChillsAt(4));
        Check("the chill is wider and longer at level 4 than at level 2",
              ShipSignatures2.ChillReachAt(4) > ShipSignatures2.ChillReachAt(2) && ShipSignatures2.ChillSecondsAt(4) > ShipSignatures2.ChillSecondsAt(2));
        Check("pierce: none below level 2, one extra at 2, two at 4",
              ShipSignatures2.SentryPierceAt(1) == 0 && ShipSignatures2.SentryPierceAt(2) == 1 && ShipSignatures2.SentryPierceAt(4) == 2);
        Check("sentries drift only at the top level",
              !ShipSignatures2.SentriesDriftAt(3) && ShipSignatures2.SentriesDriftAt(4));
        Check("the eye's blast is the base 1.2 until level 2, then grows to 1.8",
              Mathf.Approximately(ShipSignatures2.EyeBlastAt(0), ShipAttackRunner.EyeBlast) &&
              Mathf.Approximately(ShipSignatures2.EyeBlastAt(1), ShipAttackRunner.EyeBlast) &&
              ShipSignatures2.EyeBlastAt(2) > ShipAttackRunner.EyeBlast && Mathf.Approximately(ShipSignatures2.EyeBlastAt(4), 1.8f));
        Check("the mine blast is the base until level 2; the chain starts at level 4",
              Mathf.Approximately(ShipSignatures2.MineBlastAt(1), ShipAttackRunner.MineBlast) &&
              ShipSignatures2.MineBlastAt(2) > ShipAttackRunner.MineBlast &&
              !ShipSignatures2.MinesChainAt(3) && ShipSignatures2.MinesChainAt(4));
        bool stamped = true;
        for (int lv = 0; lv <= ShipWeaponUpgrades.MaxLevel; lv++)
            stamped &= ShipWeaponUpgrades.LoadoutAt(6, lv).weaponLevel == lv && ShipWeaponUpgrades.LoadoutAt(12, lv).weaponLevel == lv;
        Check("a loadout carries the weapon level it was built at", stamped && ShipLoadoutTable.For(6).weaponLevel == 0);
    }

    static void LabelsMentionTheEffectsAndKeepTheNumbers()
    {
        // the dock row and the codex read the step labels: the unlocking step names the effect
        Check("Jade Phantom: level 2 and 4 name the chill",
              ShipWeaponUpgrades.Step(6, 2).label.Contains("CHILL") && ShipWeaponUpgrades.Step(6, 4).label.Contains("CHILL") &&
              !ShipWeaponUpgrades.Step(6, 1).label.Contains("CHILL"));
        Check("Gold Warden: level 2 names the pierce, level 4 the drift",
              ShipWeaponUpgrades.Step(7, 2).label.Contains("PIERCE") && ShipWeaponUpgrades.Step(7, 4).label.Contains("DRIFT"));
        Check("Paranoid: level 2 and 4 name the blast",
              ShipWeaponUpgrades.Step(10, 2).label.Contains("BLAST") && ShipWeaponUpgrades.Step(10, 4).label.Contains("BLAST"));
        Check("Saboteur: level 2 names the blast, level 4 the chain",
              ShipWeaponUpgrades.Step(12, 2).label.Contains("BLAST") && ShipWeaponUpgrades.Step(12, 4).label.Contains("CHAIN"));
        bool numbers = true;
        foreach (int id in new[] { 6, 7, 10, 12 })
        {
            numbers &= Mathf.Approximately(ShipWeaponUpgrades.Step(id, 2).cooldownCut, .15f) &&
                       Mathf.Approximately(ShipWeaponUpgrades.Step(id, 4).cooldownCut, .20f) &&
                       ShipWeaponUpgrades.Step(id, 2).label.StartsWith("-15% COOLDOWN") &&
                       ShipWeaponUpgrades.Step(id, 4).label.StartsWith("-20% COOLDOWN");
        }
        Check("the cooldown steps keep their numbers (and their label's start)", numbers);
        Check("the other steps are unchanged",
              ShipWeaponUpgrades.Step(6, 1).label == "SHATTER SPLASH" && ShipWeaponUpgrades.Step(7, 3).label == "LONGER SENTRIES" &&
              ShipWeaponUpgrades.Step(10, 3).label == "+1 MARK" && ShipWeaponUpgrades.Step(12, 1).label == "+1 MINE");
    }

    // Jade Phantom: one enemy ahead freezes and shatters; a second one arrives after the ring has passed, so only
    // the shatter's chill can touch it.
    static void JadeChillsOutsideTheSplash()
    {
        foreach (var row in new[] { new { level = 1, dx = 1.5f }, new { level = 2, dx = 1.5f }, new { level = 4, dx = 2.5f } })
        {
            FreshScene();
            var jade = Ship(6, row.level);
            var a = Hazard(0f, -1f);
            jade.Runner.Fire();
            Steps(jade.Runner, 1.04f);   // the ring has covered the screen, a is frozen
            var b = Hazard(row.dx, -1f);
            int guard = 0;
            while (jade.Runner.ShatterCount < 1 && guard++ < 40) jade.Runner.Step(.02f);
            bool chills = ShipSignatures2.ChillsAt(row.level);
            var fs = b.GetComponent<FrozenState>();
            Check("level " + row.level + ": a shatters (" + jade.Runner.ShatterCount + ") and b outside the splash lives", !Alive(a) && Alive(b));
            Check("level " + row.level + ": the chill is " + (chills ? "ON: b stops under an overlay" : "OFF: b is untouched"),
                  chills ? fs != null && fs.IsFrozen && jade.Runner.ChilledNow == 1 && jade.Runner.ChillCount == 1
                         : (fs == null || !fs.IsFrozen) && jade.Runner.ChillCount == 0);
            if (chills)
            {
                float hold = ShipSignatures2.ChillSecondsAt(row.level);
                Steps(jade.Runner, hold - .3f);
                Check("level " + row.level + ": still chilled before " + hold + " s", fs.IsFrozen && Alive(b));
                Steps(jade.Runner, .5f);
                Check("level " + row.level + ": then it thaws by itself, unharmed", !fs.IsFrozen && Alive(b) && jade.Runner.ChilledNow == 0);
            }
            Destroy(b);
            Teardown(jade, 6);
        }
    }

    // Gold Warden: bolts carry on to a second enemy from level 2; the sentries follow the ship at level 4.
    static void GoldBoltsPierceAndSentriesDrift()
    {
        foreach (int level in new[] { 1, 2 })
        {
            FreshScene();
            var gold = Ship(7, level);
            var row = new[] { Hazard(-1.2f, 3.5f), Hazard(0f, 3.5f), Hazard(1.2f, 3.5f) };
            gold.Runner.Fire();
            Steps(gold.Runner, 4f);
            bool pierces = ShipSignatures2.SentryPierceAt(level) > 0;
            Check("level " + level + ": bolts " + (pierces ? "carry on to another enemy (" + gold.Runner.PierceCount + ")" : "do not pierce"),
                  pierces ? gold.Runner.PierceCount > 0 : gold.Runner.PierceCount == 0);
            Check("level " + level + ": the row is cleared either way", !Alive(row[0]) && !Alive(row[1]) && !Alive(row[2]));
            Destroy(row);
            Teardown(gold, 7);
        }

        // a boss-like target is never a pierce destination: it takes at most one hit per bolt fired
        FreshScene();
        var g = Ship(7, ShipWeaponUpgrades.MaxLevel);
        var bossGo = Hazard(0f, 3.5f);
        var fake = bossGo.AddComponent<FakeBoss>();
        var other = Hazard(1f, 3.5f);
        g.Runner.Fire();
        Steps(g.Runner, 3f);
        Check("a boss-like target takes only bolts aimed at it, never a carried-on one (" + fake.hits + " hits, " + g.Runner.PierceCount + " carried)",
              fake.hits <= g.Runner.SentryBolts);
        Destroy(bossGo, other);
        Teardown(g, 7);

        foreach (int level in new[] { 2, 4 })
        {
            FreshScene();
            var gold = Ship(7, level);
            gold.Runner.Fire();
            Steps(gold.Runner, 1.5f);
            float x0 = gold.Runner.FirstSentryX;
            gold.transform.position += Vector3.right * 1.5f;
            Steps(gold.Runner, 1.2f);
            float moved = Mathf.Abs(gold.Runner.FirstSentryX - x0);
            bool drifts = ShipSignatures2.SentriesDriftAt(level);
            Check("level " + level + ": the sentries " + (drifts ? "drift along with the ship (moved " + moved.ToString("0.00") + ")" : "hold their place"),
                  drifts ? moved > 1.2f : moved < 1e-3f);
            Teardown(gold, 7);
        }
    }

    // Paranoid: a neighbour 1.5 from the mark dies from level 2 on, one 1.8 away only at level 4. The neighbour
    // sits level with the eye so the gaze never marks it itself.
    static void ParanoidBlastGrowsWithTheLevel()
    {
        foreach (var row in new[] { new { level = 1, d = 1.5f, dies = false }, new { level = 2, d = 1.5f, dies = true },
                                    new { level = 2, d = 1.8f, dies = false }, new { level = 4, d = 1.8f, dies = true } })
        {
            FreshScene();
            var c = Ship(10, row.level);
            float eyeY = c.Runner.Nose.y + ShipAttackRunner.EyeAhead;
            var mark = Hazard(0f, eyeY + .3f);
            float dx = Mathf.Sqrt(row.d * row.d - .4f * .4f);
            var near = Hazard(dx, eyeY - .1f);   // behind the eye: never marked
            c.Runner.Fire();
            Steps(c.Runner, 2f);
            Check("level " + row.level + ": the mark detonates", !Alive(mark));
            Check("level " + row.level + ": a neighbour " + row.d + " away " + (row.dies ? "is caught in the bigger blast" : "is out of reach"),
                  Alive(near) != row.dies);
            Destroy(near);
            Teardown(c, 10);
        }
    }

    // Saboteur: a hazard 1.4 from a mine's spot dies from level 2 (blast 1.35 + its radius); from level 4 a
    // blowing mine sets off the next one.
    static void SaboteurBlastGrowsAndChains()
    {
        foreach (int level in new[] { 1, 2 })
        {
            FreshScene();
            var c = Ship(12, level);
            Vector3 nose = c.Runner.Nose;
            Vector3 spot = nose + Quaternion.Euler(0f, 0f, 35f) * Vector3.up * 2.2f;   // the first mine lands here
            var trip = Hazard(spot.x, spot.y);
            var edge = Hazard(spot.x - 1.4f, spot.y);
            c.Runner.Fire();
            Steps(c.Runner, .9f);
            bool bigger = ShipSignatures2.MineBlastAt(level) > ShipAttackRunner.MineBlast;
            Check("level " + level + ": the mine trips", !Alive(trip));
            Check("level " + level + ": a hazard 1.4 away " + (bigger ? "is caught in the bigger blast" : "is out of reach"), Alive(edge) != bigger);
            Destroy(edge);
            Teardown(c, 12);
        }

        foreach (int level in new[] { 3, 4 })
        {
            FreshScene();
            var c = Ship(12, level);
            Vector3 nose = c.Runner.Nose;
            Vector3 spot = nose + Quaternion.Euler(0f, 0f, 35f) * Vector3.up * 2.2f;
            var trip = Hazard(spot.x, spot.y);
            c.Runner.Fire();
            int launched = c.Runner.LiveMines;
            Steps(c.Runner, 1.2f);
            bool chains = ShipSignatures2.MinesChainAt(level);
            Check("level " + level + ": one mine trips and the others " + (chains ? "go with it (" + c.Runner.LiveMines + " of " + launched + " left)" : "stay"),
                  !Alive(trip) && (chains ? c.Runner.LiveMines < launched - 1 : c.Runner.LiveMines == launched - 1));
            Teardown(c, 12);
        }
    }

    sealed class FakeBoss : MonoBehaviour, IShipAttackTargetUncapped
    {
        public int hits;
        public float weight;
        public void TakeShipAttack(int ship, float w, Vector3 at) { hits++; weight += w; }
    }
}
