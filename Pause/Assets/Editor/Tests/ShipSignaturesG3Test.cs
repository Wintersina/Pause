using UnityEditor.SceneManagement;
using UnityEngine;

// The signature upgrade effects of the UFO, Dove, Turtle and Paragon (ShipSignaturesG3.cs): each one is OFF
// below its unlock level and ON at / after it, shows in the step label that unlocks it, and (UFO blast,
// Paragon's second warhead) plays out in the runner without extra hits on a boss.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ShipSignaturesG3Test.Run
public static class ShipSignaturesG3Test
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SIG] PASS  " : "[SIG] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        Unlocks();
        Labels();
        UfoBlastOnlyFromItsLevel();
        NukeSecondWarheadOnlyAtTheTop();
        AttackPool.StopAll();
        EnemyShove.Clear();
        Debug.Log("[SIG] failures: " + fails);
        return fails;
    }

    // ---- fixtures (as ShipAttacksV3CheapTest) ------------------------------

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
        moveBackGround.speed = 0f;
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

    class FakeBoss : MonoBehaviour, IShipAttackTargetUncapped
    {
        public float weight;
        public int hits;
        public void TakeShipAttack(int ship, float w, Vector3 at) { weight += w; hits++; }
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
        EnemyShove.Clear();
        collisionDetection.cloakTimer = 0f;
        collisionDetection.lifeCounter = 0;
    }

    static void SetLevel(int id, int level)
    {
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        for (int n = 1; n < ShipSkins.PerShip; n++)
        {
            if (n <= level) PlayerPrefs.SetInt(ShipSkins.OwnedKey(id, n), 1);
            else PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(id, n));
        }
    }

    static void Steps(ShipAttackRunner r, float seconds, float dt = .02f)
    {
        for (float t = 0f; t < seconds; t += dt) { r.Step(dt); EnemyShove.Step(dt); }
    }

    // ---- cases ---------------------------------------------------------------

    static void Unlocks()
    {
        int mild = ShipSignaturesG3.MildLevel, full = ShipSignaturesG3.FullLevel;
        Check("the unlock levels are 2 and the max level", mild == 2 && full == ShipWeaponUpgrades.MaxLevel);
        for (int lv = 0; lv <= full; lv++)
            Check("the loadout carries its weapon level " + lv, ShipWeaponUpgrades.LoadoutAt(13, lv).weaponLevel == lv);

        // UFO: a blast from level 2, bigger at 4
        Check("UFO blast: off at 0 and 1, on at 2, bigger at 4",
              ShipSignaturesG3.UfoBlastRadius(0) == 0f && ShipSignaturesG3.UfoBlastRadius(1) == 0f &&
              ShipSignaturesG3.UfoBlastRadius(mild) > 0f && ShipSignaturesG3.UfoBlastRadius(3) == ShipSignaturesG3.UfoBlastRadius(mild) &&
              ShipSignaturesG3.UfoBlastRadius(full) > ShipSignaturesG3.UfoBlastRadius(mild));
        // Dove: harder, bigger returns from 2, twins at 4
        Check("Dove returns: stock below 2, harder at 2, twins only at 4",
              Mathf.Approximately(ShipSignaturesG3.DoveReturnWeight(1), ShipAttackRunner.ReturnWeight) &&
              ShipSignaturesG3.DoveReturnWeight(mild) > ShipAttackRunner.ReturnWeight &&
              ShipSignaturesG3.DoveReturnScale(mild) > ShipSignaturesG3.DoveReturnScale(1) &&
              !ShipSignaturesG3.DoveTwins(3) && ShipSignaturesG3.DoveTwins(full));
        Check("Dove returns never outhit a boss hit", ShipSignaturesG3.DoveReturnWeight(full) <= ShipAttackRunner.ReturnBossWeight);
        // Turtle: 8 bounces, 10 at 2, 14 at 4; sparks from 2; trail only at 4
        int bounces = ShipLoadoutTable.For(15).maxHits;
        Check("Turtle bounces: 8, 8, 10, 10, 14",
              bounces == 8 && ShipSignaturesG3.DiscExtraBounces(1) == 0 && bounces + ShipSignaturesG3.DiscExtraBounces(mild) == 10 &&
              bounces + ShipSignaturesG3.DiscExtraBounces(full) == 14);
        Check("Turtle sparks grow at 2 and the trail starts at 4",
              ShipSignaturesG3.DiscSparkScale(mild) > ShipSignaturesG3.DiscSparkScale(1) && !ShipSignaturesG3.DiscTrail(3) && ShipSignaturesG3.DiscTrail(full));
        // Paragon: bigger fireball from 2, two warheads at 4 sharing one boss hit
        Check("Paragon: fireball bigger from 2, second warhead only at 4",
              ShipSignaturesG3.NukeBlastScale(1) == 1f && ShipSignaturesG3.NukeBlastScale(mild) > 1f &&
              ShipSignaturesG3.NukeWarheads(3) == 1 && ShipSignaturesG3.NukeWarheads(full) == 2);
        Check("Paragon's warheads share one hit", Mathf.Approximately(ShipSignaturesG3.NukeWarheadWeight(full) * 2f, 1f) &&
              Mathf.Approximately(ShipSignaturesG3.NukeWarheadWeight(0), 1f));
        // the numeric tracks are untouched
        Check("the numeric steps are unchanged (UFO pierce/grab, Dove gust, Turtle disc, Paragon speed)",
              ShipWeaponUpgrades.LoadoutAt(13, full).maxHits == 2 && ShipWeaponUpgrades.LoadoutAt(13, full).shots == 2 &&
              ShipWeaponUpgrades.LoadoutAt(14, full).range > ShipWeaponUpgrades.LoadoutAt(14, 0).range &&
              ShipWeaponUpgrades.LoadoutAt(15, full).maxHits == 8 &&
              Mathf.Approximately(ShipWeaponUpgrades.LoadoutAt(16, full).speed, 28f));
    }

    static void Labels()
    {
        // the dock label of the step that unlocks an effect names it; the other steps do not
        string[] mildWord = { null, null, ShipSignaturesG3.UfoBlastMildLabel, ShipSignaturesG3.DoveReturnMildLabel, ShipSignaturesG3.DiscMildLabel, ShipSignaturesG3.NukeMildLabel };
        string[] fullWord = { null, null, ShipSignaturesG3.UfoBlastFullLabel, ShipSignaturesG3.DoveReturnFullLabel, ShipSignaturesG3.DiscFullLabel, ShipSignaturesG3.NukeFullLabel };
        for (int k = 2; k < 6; k++)
        {
            int ship = 11 + k;   // 13..16
            Check("ship " + ship + ": step 2 names its mild effect, step 4 its full one, steps 1 and 3 neither",
                  ShipWeaponUpgrades.Step(ship, 2).label.Contains(mildWord[k]) && ShipWeaponUpgrades.Step(ship, 4).label.Contains(fullWord[k]) &&
                  !ShipWeaponUpgrades.Step(ship, 1).label.Contains(mildWord[k]) && !ShipWeaponUpgrades.Step(ship, 3).label.Contains(fullWord[k]));
            Check("ship " + ship + ": the cooldown cuts are still 15% and 20%",
                  Mathf.Approximately(ShipWeaponUpgrades.Step(ship, 2).cooldownCut, .15f) && Mathf.Approximately(ShipWeaponUpgrades.Step(ship, 4).cooldownCut, .20f));
            string line = ShipLoadoutTable.For(ship).attackLine.ToLowerInvariant();
            Check("ship " + ship + ": the dock / codex attack line mentions colours", line.Contains("colours"));
        }
    }

    // A pair of enemies, one in the hurl's line and one off to its side (inside the blast reach, outside the line).
    static bool SideEnemyDies(int level)
    {
        SetLevel(13, level);
        FreshScene();
        var c = Ship(13);
        var grabbed = Hazard(0f, 0f);
        grabbed.AddComponent<BoxCollider2D>();
        var inLine = Hazard(0f, 3f);
        var side = Hazard(.8f, 3f);
        var boss = Hazard(-2f, 3f);
        var fake = boss.AddComponent<FakeBoss>();
        boss.GetComponent<ClearTarget>().SetRadius(.5f);
        c.Runner.Fire();
        Steps(c.Runner, 3f);
        bool dead = side == null;
        Check("level " + level + ": the boss beside the impact takes nothing from the blast (" + fake.hits + ")", fake.hits == 0);
        Teardown(c);
        foreach (var g in new[] { grabbed, inLine, side, boss }) if (g != null) Object.DestroyImmediate(g);
        SetLevel(13, 0);
        return dead;
    }

    static void UfoBlastOnlyFromItsLevel()
    {
        Check("UFO level 0: the hurled body leaves the enemy beside the impact alone", !SideEnemyDies(0));
        Check("UFO level 1: still no blast", !SideEnemyDies(1));
        Check("UFO level 2: the impact blast takes the enemy beside it", SideEnemyDies(2));
        Check("UFO level 4: and so does the bigger blast", SideEnemyDies(ShipWeaponUpgrades.MaxLevel));
    }

    static void NukeSecondWarheadOnlyAtTheTop()
    {
        foreach (int level in new[] { 0, 2, ShipWeaponUpgrades.MaxLevel })
        {
            SetLevel(16, level);
            FreshScene();
            var p = Ship(16);
            var a = Hazard(0f, 0f);
            var far = Hazard(-2.4f, -4.5f);
            var boss = Hazard(0f, 2f);
            var fake = boss.AddComponent<FakeBoss>();
            p.Runner.Fire();
            Steps(p.Runner, 4f, .05f);
            int want = level >= ShipSignaturesG3.FullLevel ? 2 : 1;
            Check("Paragon level " + level + ": " + want + " warhead(s) detonate (" + p.Runner.Detonations + ") and everything ordinary dies",
                  p.Runner.Detonations == want && a == null && far == null && p.Runner.WarheadsNow == 0 && p.Runner.ActiveRuns == 0);
            Check("Paragon level " + level + ": the boss takes about one hit in all (" + fake.weight.ToString("F2") + ")",
                  fake.weight > .45f && fake.weight <= 1.01f);
            Check("Paragon level " + level + ": every detonation had its visible fall", p.Runner.FallsShown == p.Runner.Detonations);
            Teardown(p);
            if (boss != null) Object.DestroyImmediate(boss);
            SetLevel(16, 0);
        }
    }
}
