using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// Feature: every hull colour bought makes that ship's main attack stronger
// (ShipWeaponUpgrades). Weapon level = non-stock skins owned for the ship;
// each level adds the next step of the ship's track -- more shots / hops /
// bounces, a bigger or longer effect, or a shorter charge time.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ShipWeaponUpgradesTest.Run
public static class ShipWeaponUpgradesTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[WU] PASS  " : "[WU] FAIL  ") + what);
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

        LevelComesFromOwnership();
        DeveloperModeIsMaxLevel();
        EveryLevelIsFeltOnEveryShip();
        ProjectileCountsFollowTheLevel();
        HopsBouncesAndRailsFollowTheLevel();
        HeldWeaponsLastLonger();
        CooldownShrinksForEveryShip();
        StaysFair();
        DockShowsTheWeaponLevel();

        AttackPool.StopAll();
        WorldTimeFx.Reset();
        Debug.Log("[WU] failures: " + fails);
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

    static GameObject Hazard(float x, float y)
    {
        var go = new GameObject("hazard");
        go.tag = "Enimey";
        go.transform.position = new Vector3(x, y, 0f);
        ClearTarget.Ensure(go).SetRadius(.3f);
        return go;
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
        collisionDetection.cloakTimer = 0f;
        collisionDetection.lifeCounter = 0;
    }

    static void Steps(ShipAttackRunner r, float seconds, float dt = .02f)
    {
        for (float t = 0f; t < seconds; t += dt) r.Step(dt);
    }

    static void Clear(List<GameObject> list)
    {
        foreach (var g in list) if (g != null) Object.DestroyImmediate(g);
        list.Clear();
    }

    // ---- cases ---------------------------------------------------------

    static void LevelComesFromOwnership()
    {
        Check("max level is one per non-stock colour (4)", ShipWeaponUpgrades.MaxLevel == ShipSkins.PerShip - 1 &&
              ShipWeaponUpgrades.MaxLevel == 4);
        bool every = true;
        foreach (int id in ShipId.All)
            every &= ShipWeaponUpgrades.Has(id) && ShipSkins.CountFor(id) == ShipSkins.PerShip;
        Check("every ship has an upgrade track and " + ShipSkins.PerShip + " skins", every && ShipId.Count == 15);

        SetLevel(3, 0);
        Check("only the stock colours -> level 0", ShipWeaponUpgrades.Level(3) == 0);
        PlayerPrefs.SetInt(ShipSkins.OwnedKey(3, 4), 1);
        Check("one colour bought (any one) -> level 1", ShipWeaponUpgrades.Level(3) == 1);
        PlayerPrefs.SetInt(ShipSkins.OwnedKey(3, 2), 1);
        Check("two -> level 2", ShipWeaponUpgrades.Level(3) == 2);
        PlayerPrefs.SetInt(ShipSkins.EquippedKey(3), 2);
        Check("the equipped skin doesn't change the level", ShipWeaponUpgrades.Level(3) == 2);
        PlayerPrefs.DeleteKey(ShipSkins.EquippedKey(3));
        SetLevel(3, 4);
        Check("every colour -> max level", ShipWeaponUpgrades.Level(3) == ShipWeaponUpgrades.MaxLevel);
        Check("another ship's colours don't count", ShipWeaponUpgrades.Level(4) == 0);
        Check("a stray id is level 0", ShipWeaponUpgrades.Level(0) == 0 && ShipWeaponUpgrades.Level(99) == 0);
        SetLevel(3, 0);
    }

    static void DeveloperModeIsMaxLevel()
    {
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        bool max = true;
        foreach (int id in ShipId.All) max &= ShipWeaponUpgrades.Level(id) == ShipWeaponUpgrades.MaxLevel;
        Check("developer mode flies every ship at max weapon level", max);
        Check("without writing any ownership", !PlayerPrefs.HasKey(ShipSkins.OwnedKey(5, 1)));
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        Check("and switching it off drops back to the real level", ShipWeaponUpgrades.Level(5) == 0);
    }

    static bool Stronger(ShipLoadout a, ShipLoadout b)
    {
        return b.shots > a.shots || b.maxHits > a.maxHits || b.duration > a.duration ||
               b.range > a.range || b.width > a.width || b.speed > a.speed;
    }

    static void EveryLevelIsFeltOnEveryShip()
    {
        bool felt = true, labelled = true, base0 = true, bounded = true;
        foreach (int id in ShipId.All)
        {
            var baseRow = ShipLoadoutTable.For(id);
            var l0 = ShipWeaponUpgrades.LoadoutAt(id, 0);
            base0 &= l0.shots == baseRow.shots && l0.maxHits == baseRow.maxHits && l0.duration == baseRow.duration &&
                     Mathf.Approximately(ShipWeaponUpgrades.CooldownScaleAt(id, 0), 1f);
            for (int lv = 1; lv <= ShipWeaponUpgrades.MaxLevel; lv++)
            {
                var prev = ShipWeaponUpgrades.LoadoutAt(id, lv - 1);
                var cur = ShipWeaponUpgrades.LoadoutAt(id, lv);
                bool faster = ShipWeaponUpgrades.CooldownScaleAt(id, lv) < ShipWeaponUpgrades.CooldownScaleAt(id, lv - 1) - .05f;
                if (!Stronger(prev, cur) && !faster)
                {
                    felt = false;
                    Debug.Log("[WU] ship " + id + " level " + lv + " changes nothing");
                }
                labelled &= !string.IsNullOrEmpty(ShipWeaponUpgrades.Step(id, lv).label);
            }
            var max = ShipWeaponUpgrades.LoadoutAt(id, ShipWeaponUpgrades.MaxLevel);
            bounded &= max.shots <= 26 && max.maxHits <= 9 && max.duration <= 7f &&
                       ShipWeaponUpgrades.CooldownScaleAt(id, ShipWeaponUpgrades.MaxLevel) >= .55f;
        }
        Check("level 0 is the ship's base loadout", base0);
        Check("every level of every ship adds something (more, bigger, longer or faster)", felt);
        Check("every step has a dock label", labelled);
        Check("max level stays bounded (shots <= 26, hops <= 9, cooldown >= 55%)", bounded);
    }

    static void ProjectileCountsFollowTheLevel()
    {
        // ship -> what it launches; step long enough for any burst
        foreach (int id in new[] { 1, 3, 8, 10, 12, 14 })
        {
            string counts = "";
            bool ok = true;
            for (int lv = 0; lv <= ShipWeaponUpgrades.MaxLevel; lv++)
            {
                SetLevel(id, lv);
                FreshScene();
                var c = Ship(id);
                int before = AttackProjectile.LaunchCount;
                c.Runner.Fire();
                Steps(c.Runner, 1.6f);
                int n = AttackProjectile.LaunchCount - before;
                int want = Mathf.Max(1, ShipWeaponUpgrades.LoadoutAt(id, lv).shots);
                ok &= n == want;
                counts += (lv > 0 ? "/" : "") + n;
                Teardown(c);
            }
            int at0 = Mathf.Max(1, ShipWeaponUpgrades.LoadoutAt(id, 0).shots);
            int atMax = Mathf.Max(1, ShipWeaponUpgrades.LoadoutAt(id, ShipWeaponUpgrades.MaxLevel).shots);
            Check(ShipLoadoutTable.For(id).attackName + " launches its level's shot count (" + counts + ")",
                  ok && (atMax > at0 || id == 3 && atMax == 2));
            SetLevel(id, 0);
        }

        // the comet's extra streaks splay, the extra fireball fans
        Check("upgraded comet streaks splay to alternate sides",
              ShipAttackRunner.StreakAngle(0) == 0f && ShipAttackRunner.StreakAngle(1) == 0f &&
              ShipAttackRunner.StreakAngle(2) * ShipAttackRunner.StreakAngle(3) < 0f);
    }

    static void HopsBouncesAndRailsFollowTheLevel()
    {
        var foes = new List<GameObject>();
        foreach (int id in new[] { 2, 11 })
        {
            string kills = "";
            bool ok = true;
            foreach (int lv in new[] { 0, ShipWeaponUpgrades.MaxLevel })
            {
                SetLevel(id, lv);
                FreshScene();
                var c = Ship(id);
                // chain: a tight grid; ricochet: a zigzag column (its tight
                // turn circles a mark that sits too close)
                for (int i = 0; i < 16; i++)
                    foes.Add(id == 2 ? Hazard(-1.2f + (i % 4) * .8f, -1.6f + (i / 4) * .9f)
                                     : Hazard(i % 2 == 0 ? -.9f : .9f, -1.6f + i * .75f));
                int k = ShipAttackHits.Kills;
                c.Runner.Fire();
                Steps(c.Runner, 5f);
                int n = ShipAttackHits.Kills - k;
                ok &= n == ShipWeaponUpgrades.LoadoutAt(id, lv).maxHits;
                kills += (lv > 0 ? "/" : "") + n;
                Teardown(c);
                Clear(foes);
            }
            Check(ShipLoadoutTable.For(id).attackName + " hits its level's max (" + kills + ")", ok);
            SetLevel(id, 0);
        }

        // rail: wider at level 2, side rails at level 4
        bool[] nearHit = new bool[5], sideHit = new bool[5];
        for (int lv = 0; lv <= ShipWeaponUpgrades.MaxLevel; lv++)
        {
            SetLevel(4, lv);
            FreshScene();
            var c = Ship(4);
            var near = Hazard(.64f, 0f);
            var side = Hazard(ShipAttackRunner.SideRailSpacing, 1.5f);
            c.Runner.Fire();
            nearHit[lv] = near == null;
            sideHit[lv] = side == null;
            Teardown(c);
            if (near != null) Object.DestroyImmediate(near);
            if (side != null) Object.DestroyImmediate(side);
        }
        Check("the rail widens at level 2", !nearHit[0] && !nearHit[1] && nearHit[2]);
        Check("the rail gains side rails at level 4", !sideHit[0] && !sideHit[3] && sideHit[4]);
        SetLevel(4, 0);
    }

    static void HeldWeaponsLastLonger()
    {
        foreach (int id in new[] { 9, 13, 15 })
        {
            float d0 = ShipWeaponUpgrades.LoadoutAt(id, 0).duration;
            float dMax = ShipWeaponUpgrades.LoadoutAt(id, ShipWeaponUpgrades.MaxLevel).duration;
            SetLevel(id, ShipWeaponUpgrades.MaxLevel);
            FreshScene();
            var c = Ship(id);
            c.Runner.Fire();
            Steps(c.Runner, d0 + .15f);
            bool stillOn = c.Runner.ActiveRuns == 1;
            Steps(c.Runner, dMax - d0);
            bool off = c.Runner.ActiveRuns == 0;
            Teardown(c);
            Check(ShipLoadoutTable.For(id).attackName + " lasts " + d0 + "s -> " + dMax + "s at max level",
                  dMax > d0 && stillOn && off);
            SetLevel(id, 0);
        }
    }

    static void CooldownShrinksForEveryShip()
    {
        bool shrinks = true, inRange = true;
        string table = "";
        foreach (int id in ShipId.All)
        {
            float sMax = ShipWeaponUpgrades.CooldownScaleAt(id, ShipWeaponUpgrades.MaxLevel);
            shrinks &= sMax <= .7f;
            table += id + ":" + sMax.ToString("0.00") + " ";
            foreach (int lv in new[] { 0, ShipWeaponUpgrades.MaxLevel })
            {
                SetLevel(id, lv);
                FreshScene();
                var c = Ship(id);
                float k = ShipWeaponUpgrades.CooldownScaleAt(id, lv);
                float left = c.SecondsLeft;
                inRange &= left >= c.cooldownRange.x * k - .01f && left <= c.cooldownRange.y * k + .01f &&
                           c.WeaponLevel == lv;
                Teardown(c);
            }
            SetLevel(id, 0);
        }
        Check("every ship charges at least 30% faster at max level (" + table + ")", shrinks);
        Check("the charge countdown is the cooldown range scaled by the level", inRange);
    }

    class FakeBoss : MonoBehaviour, IShipAttackTarget
    {
        public float weight;
        public int hits;
        public void TakeShipAttack(int ship, float w, Vector3 at) { weight += w; hits++; }
    }

    static void StaysFair()
    {
        bool capped = true;
        string got = "";
        foreach (int id in new[] { 1, 3, 4, 8, 10, 12, 14 })
        {
            SetLevel(id, ShipWeaponUpgrades.MaxLevel);
            FreshScene();
            var c = Ship(id);
            var go = Hazard(0f, 0f);
            go.GetComponent<ClearTarget>().SetRadius(1.2f);
            var boss = go.AddComponent<FakeBoss>();
            c.Runner.Fire();
            Steps(c.Runner, 3f);
            capped &= boss != null && boss.weight <= 1f + 1e-4f;
            got += id + ":" + (boss != null ? boss.weight.ToString("0.00") : "x") + " ";
            Teardown(c);
            if (go != null) Object.DestroyImmediate(go);
            SetLevel(id, 0);
        }
        Check("a max-level firing still deals a boss at most one hit (" + got + ")", capped);

        SetLevel(12, ShipWeaponUpgrades.MaxLevel);
        FreshScene();
        var gat = Ship(12);
        for (int i = 0; i < 4; i++) gat.Runner.Fire();
        Steps(gat.Runner, .8f);
        Check("a max-level gatling flood stays within the projectile cap",
              AttackPool.ProjectilePoolSize <= AttackPool.MaxProjectiles);
        Teardown(gat);
        SetLevel(12, 0);
    }

    static void DockShowsTheWeaponLevel()
    {
        EditorSceneLoader.Open("shopS6", OpenSceneMode.Single);
        for (int i = 0; i <= shopingShips.shipTotal; i++) PlayerPrefs.DeleteKey(ShipId.OwnedKey(i));
        PlayerPrefs.SetString(ShipId.OwnedKey(1), "True");
        PlayerPrefs.SetString(ShipId.OwnedKey(8), "True");
        PlayerPrefs.SetInt("spawnShip", 8);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 400f);
        SetLevel(8, 1);
        ShopSceneExtender.Build();
        var dock = SpaceDock.Instance;
        Check("dock built", dock != null);
        if (dock == null) return;
        var cam = Camera.main;
        cam.aspect = 1080f / 1920f;
        cam.orthographicSize = CameraFit.ComputeSize(5f, 2.85f, 1080, 1920);
        dock.Relayout();
        var popup = dock.popup;

        dock.Select(8);
        popup.SendMessage("LateUpdate");
        string next = ShipWeaponUpgrades.Step(8, 2).label;
        Check("an owned ship's popup shows its weapon level (1 of 4 pips lit)",
              popup.WeaponLevelShown == 1 && popup.WeaponPip(0).color == AkiraPalette.Cyan &&
              popup.WeaponPip(1).color == AkiraPalette.Hairline);
        Check("and what the next colour adds (" + popup.WeaponLine + ")", popup.WeaponLine == "NEXT " + next);
        Rect r = popup.WorldRect;
        Check("the popup with its weapon row still fits the dock's safe view",
              r.yMin >= popup.safeView.yMin - .001f && r.yMax <= popup.safeView.yMax + .001f &&
              popup.CurrentHeight > DockPopup.Height + DockPopup.SkinRowHeight);

        popup.TapSwatch(2);
        Check("previewing a locked colour names its upgrade and lights the pip it adds in gold",
              popup.CurrentMode == DockPopup.Mode.BuySkin && popup.WeaponLine == next &&
              popup.WeaponPip(1).color == DockArt.Gold && popup.WeaponPip(2).color == AkiraPalette.Hairline);

        popup.Press();
        Check("buying it raises the level at once (popup and game)",
              ShipWeaponUpgrades.Level(8) == 2 && popup.WeaponLevelShown == 2 &&
              popup.WeaponPip(1).color == AkiraPalette.Cyan &&
              popup.WeaponLine == "NEXT " + ShipWeaponUpgrades.Step(8, 3).label);

        SetLevel(8, ShipWeaponUpgrades.MaxLevel);
        dock.Select(1);
        dock.Select(8);
        Check("every colour owned reads MAX", popup.WeaponLevelShown == 4 && popup.WeaponLine == "MAX");

        SetLevel(8, 0);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        dock.Select(1);
        dock.Select(8);
        Check("developer mode shows MAX", popup.WeaponLevelShown == 4 && popup.WeaponLine == "MAX");
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);

        dock.Select(5);
        Check("an unbought ship shows no weapon row (no skin row)", !popup.SkinRowVisible);
    }
}
