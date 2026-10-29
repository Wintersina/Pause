using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// Feature: signature upgrade effects of group 1 (ShipSignatures), on top of the numeric tracks:
// Neon Comet pierce (lvl 2 extras / lvl 4 all), Volt Viper chain stun (lvl 3, longer lvl 4),
// Crimson Halo shield-piercing rail (lvl 3), Ion Lancer crossing beam (lvl 4).
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod SignatureUpgradesG1Test.Run
public static class SignatureUpgradesG1Test
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SG1] PASS  " : "[SG1] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        StatesUnlockAtTheirLevel();
        LabelsNameTheUnlock();
        CometPiercesAtLevelFour();
        ViperStunsBystanders();
        HaloKeepsPiercingEverythingInLine();
        IonCrossesAtLevelFour();
        AttackPool.StopAll();
        Debug.Log("[SG1] failures: " + fails);
        return fails;
    }

    // ---- fixtures (the same shape as ShipWeaponUpgradesTest) --------------

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

    static ShipLoadout At(int id, int lv) { return ShipWeaponUpgrades.LoadoutAt(id, lv); }

    // ---- cases ---------------------------------------------------------

    // Off below the unlock level, on at / after it, and never on another ship's loadout.
    static void StatesUnlockAtTheirLevel()
    {
        bool comet = true;
        for (int lv = 0; lv <= ShipWeaponUpgrades.MaxLevel; lv++)
        {
            var l = At(ShipSignatures.NeonComet, lv);
            int extras = ShipSignatures.CometPierce(l, 2), first = ShipSignatures.CometPierce(l, 0);
            comet &= extras == (lv >= ShipSignatures.CometExtrasPierceLevel ? ShipSignatures.CometPierceCount : 0);
            comet &= first == (lv >= ShipSignatures.CometAllPierceLevel ? ShipSignatures.CometPierceCount : 0);
        }
        Check("comet: extra streaks pierce from level 2, every streak from level 4, nothing before", comet);

        bool viper = true, longer = false;
        for (int lv = 0; lv <= ShipWeaponUpgrades.MaxLevel; lv++)
        {
            float s = ShipSignatures.ViperStun(At(ShipSignatures.VoltViper, lv));
            viper &= (s > 0f) == (lv >= ShipSignatures.ViperStunLevel);
        }
        longer = ShipSignatures.ViperStun(At(2, 4)) > ShipSignatures.ViperStun(At(2, 3)) &&
                 ShipSignatures.ViperRadius(At(2, 4)) > ShipSignatures.ViperRadius(At(2, 3));
        Check("viper: chain stun off below level 3, on at 3, longer and wider at 4", viper && longer);

        bool halo = true, ion = true;
        for (int lv = 0; lv <= ShipWeaponUpgrades.MaxLevel; lv++)
        {
            halo &= ShipSignatures.HaloPierces(At(ShipSignatures.CrimsonHalo, lv)) == (lv >= ShipSignatures.HaloPierceLevel);
            ion &= ShipSignatures.IonCross(At(ShipSignatures.IonLancer, lv)) == (lv >= ShipSignatures.IonCrossLevel);
        }
        Check("halo: shield-piercing rail off below level 3, on from 3", halo);
        Check("ion: crossing beam off below level 4, on at 4", ion);

        var wrong = At(3, 4);
        wrong.shipId = 3;
        Check("another ship at max level has none of them",
              ShipSignatures.CometPierce(wrong, 0) == 0 && ShipSignatures.ViperStun(wrong) == 0f &&
              !ShipSignatures.HaloPierces(wrong) && !ShipSignatures.IonCross(wrong));
        Check("a base loadout has level 0", ShipLoadoutTable.For(1).weaponLevel == 0 && At(1, 3).weaponLevel == 3);
    }

    // The unlocking step says so; the others keep their plain label.
    static void LabelsNameTheUnlock()
    {
        Check("comet labels name the pierce at steps 2 and 4",
              ShipWeaponUpgrades.Step(1, 2).label.Contains(ShipSignatures.CometExtrasPierceNote) &&
              ShipWeaponUpgrades.Step(1, 4).label.Contains(ShipSignatures.CometAllPierceNote) &&
              ShipWeaponUpgrades.Step(1, 1).label == "+1 STREAK");
        Check("viper labels name the stun at steps 3 and 4 (numbers kept)",
              ShipWeaponUpgrades.Step(2, 3).label.StartsWith("+2 JUMPS") && ShipWeaponUpgrades.Step(2, 3).label.Contains(ShipSignatures.ViperStunNote) &&
              ShipWeaponUpgrades.Step(2, 4).label.Contains(ShipSignatures.ViperLongStunNote) &&
              ShipWeaponUpgrades.Step(2, 2).label == "-15% COOLDOWN");
        Check("halo label names the shield pierce at step 3",
              ShipWeaponUpgrades.Step(4, 3).label == "-20% COOLDOWN, " + ShipSignatures.HaloPierceNote &&
              ShipWeaponUpgrades.Step(4, 2).label == "WIDER RAIL");
        Check("ion label names the crossing beam at step 4",
              ShipWeaponUpgrades.Step(5, 4).label == "-20% COOLDOWN, " + ShipSignatures.IonCrossNote &&
              ShipWeaponUpgrades.Step(5, 3).label == "+1 PASS");
        Check("the codex lists the unlock", CodexCatalogue.ColoursLore(5).Contains(ShipSignatures.IonCrossNote) &&
              CodexCatalogue.ColoursLore(2).Contains(ShipSignatures.ViperStunNote));
    }

    // Two enemies close together in the streak's line: the first streak alone takes both at level 4, one before.
    static void CometPiercesAtLevelFour()
    {
        var foes = new List<GameObject>();
        var kills = new int[ShipWeaponUpgrades.MaxLevel + 1];
        for (int lv = 0; lv <= ShipWeaponUpgrades.MaxLevel; lv++)
        {
            SetLevel(1, lv);
            FreshScene();
            var c = Ship(1);
            foes.Add(Hazard(0f, -2f));
            foes.Add(Hazard(0f, -1.4f));
            int k = ShipAttackHits.Kills;
            c.Runner.Fire();
            Steps(c.Runner, .12f, .01f);   // before the second streak leaves (0.16 s)
            kills[lv] = ShipAttackHits.Kills - k;
            Teardown(c);
            Clear(foes);
        }
        Check("a comet streak stops at its first enemy below level 4 (" + kills[0] + "/" + kills[3] + ") and takes two at level 4 (" + kills[4] + ")",
              kills[0] == 1 && kills[3] == 1 && kills[4] == 2);

        SetLevel(1, 4);
        FreshScene();
        var c4 = Ship(1);
        c4.Runner.Fire();
        bool piercing = false;
        foreach (var p in AttackPool.Projectiles) if (p != null && p.Active && p.Owner == c4.Runner && p.PierceLeft > 0) piercing = true;
        Check("a level 4 streak is launched carrying its pierce", piercing);
        Teardown(c4);
        SetLevel(1, 0);
    }

    // A tight grid has more enemies than hops: the ones left standing beside the chain are stunned at level 3+.
    static void ViperStunsBystanders()
    {
        var foes = new List<GameObject>();
        var stunned = new int[ShipWeaponUpgrades.MaxLevel + 1];
        var total = new int[ShipWeaponUpgrades.MaxLevel + 1];
        foreach (int lv in new[] { 2, 3, 4 })
        {
            SetLevel(2, lv);
            FreshScene();
            var c = Ship(2);
            for (int i = 0; i < 20; i++)
                foes.Add(Hazard(-1.2f + (i % 4) * .6f, -1.6f + (i / 4) * .6f));
            c.Runner.Fire();
            Steps(c.Runner, 5f);
            foreach (var g in foes)
            {
                if (g == null) continue;
                var fs = g.GetComponent<FrozenState>();
                if (fs != null && fs.IsFrozen) stunned[lv]++;
            }
            total[lv] = c.Runner.JoltStunnedTotal;
            Teardown(c);
            Clear(foes);
        }
        Check("the chain stuns nobody at level 2 (" + stunned[2] + ")", stunned[2] == 0 && total[2] == 0);
        Check("the chain stuns the enemies beside it at level 3 (" + stunned[3] + ") and 4 (" + stunned[4] + ")",
              stunned[3] > 0 && stunned[4] > 0 && total[3] > 0);
        SetLevel(2, 0);
    }

    // Level 3 and 4: the rail still hits everything in its line.
    static void HaloKeepsPiercingEverythingInLine()
    {
        var foes = new List<GameObject>();
        foreach (int lv in new[] { 2, 3 })
        {
            SetLevel(4, lv);
            FreshScene();
            var c = Ship(4);
            for (int i = 0; i < 4; i++) foes.Add(Hazard(0f, -1.5f + i * 1.2f));
            int k = ShipAttackHits.Kills;
            c.Runner.Fire();
            int n = ShipAttackHits.Kills - k;
            Check("rail at level " + lv + " takes all four in its line (" + n + ")", n == 4);
            Teardown(c);
            Clear(foes);
        }
        SetLevel(4, 0);
    }

    // The second beam sweeps only at level 4, and the pair never hits a target twice in one pass.
    static void IonCrossesAtLevelFour()
    {
        var foes = new List<GameObject>();
        var frames = new int[ShipWeaponUpgrades.MaxLevel + 1];
        var kills = new int[ShipWeaponUpgrades.MaxLevel + 1];
        foreach (int lv in new[] { 3, 4 })
        {
            SetLevel(5, lv);
            FreshScene();
            var c = Ship(5);
            foes.Add(Hazard(-1.6f, 0f));
            foes.Add(Hazard(1.6f, 0f));
            foes.Add(Hazard(0f, 2f));
            int k = ShipAttackHits.Kills;
            c.Runner.Fire();
            Steps(c.Runner, 4f);
            frames[lv] = c.Runner.IonCrossFrames;
            kills[lv] = ShipAttackHits.Kills - k;
            Teardown(c);
            Clear(foes);
        }
        Check("the crossing beam sweeps at level 4 (" + frames[4] + " frames) and not at level 3 (" + frames[3] + ")",
              frames[3] == 0 && frames[4] > 0);
        Check("it never costs a target a second hit: the same three die either way", kills[3] == 3 && kills[4] == 3);
        SetLevel(5, 0);
    }
}
