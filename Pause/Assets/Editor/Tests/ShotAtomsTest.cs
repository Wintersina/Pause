using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// The SHOT ATOMS (ShotAtoms.cs, FloatieGuns.cs, ShotAtomHud.cs, the Codex entries): every atom's effect at levels 1-10, the world
// schedule and level caps, the dust at the cap, drops (only elites and bosses, at their rates), the coin's catch and callout, the
// bullet cap, the HUD row on every ScreenFit device, the Codex entries and the reset between runs.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ShotAtomsTest.Run
public static class ShotAtomsTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SHOT] PASS  " : "[SHOT] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;
    const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            Schedule();
            DropPicking();
            OpeningDrops();
            DropRates();
            DropHooks();
            Twin();
            Pierce();
            Split();
            Ricochet();
            Arc();
            Rapid();
            BulletCap();
            Catching();
            HudLayout();
            HudLive();
            Spin();
            CodexEntries();
            Reset();
        }
        finally
        {
            Cleanup();
        }
        Debug.Log("[SHOT] failures: " + fails);
        return fails;
    }

    // ---- fixtures -----------------------------------------------------------

    static void Cleanup()
    {
        ShotAtoms.BeginRun(3);
        FloatieBullets.ReleaseAll();
        AttackPool.StopAll();
        VoltArcs.StopAll();
        EliteSystem.Clear();
        EliteSystem.PlayerOverride = null;
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, 0);
        AtomSpacing.Reset();
        ShotAtomDrops.Spawned = null;
        ShotAtomDrops.ResetSeams();
        Boosters.SetRunRushForTest(false);
        buttonClicks.playerDied = false;
        startMenu.youAreInTutorial = false;
        TouchInput.PressedOverride = null;
        Time.timeScale = 1f;
    }

    static void ClearCoins()
    {
        foreach (var c in Object.FindObjectsByType<ShotAtomPickup>(FindObjectsSortMode.None)) Object.DestroyImmediate(c.gameObject);
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
        Cleanup();
        score.pauseCounter = 0;
        moveBackGround.speed = .2f;
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(true, true);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
        collisionDetection.lifeCounter = 0;
        collisionDetection.atomCheck = false;
        PlayerInvuln.Reset();
        TouchInput.PressedOverride = () => true;
        UnityEngine.Random.InitState(777);
    }

    sealed class Rig
    {
        public GameObject ship;
        public ShipPowerController power;
        public FloatieBattery battery;
    }

    static Rig MakeShip(int id = 1)
    {
        ShotFeelTest.ArmSquad();
        PlayerPrefs.SetInt("spawnShip", id);
        var r = new Rig();
        r.ship = new GameObject("ship" + id, typeof(SpriteRenderer));
        r.ship.transform.position = new Vector3(0f, -3f, 0f);
        r.power = r.ship.AddComponent<ShipPowerController>();
        r.power.SendMessage("Awake");
        r.power.SendMessage("Start");
        var gun = r.ship.GetComponentInChildren<UltimateGun>();
        if (gun != null) gun.SendMessage("Awake");
        r.battery = r.ship.GetComponent<FloatieBattery>();
        if (r.battery != null) r.battery.SetLevel(0);
        return r;
    }

    static void Teardown(Rig r)
    {
        FloatieBullets.ReleaseAll();
        if (r == null || r.ship == null) return;
        r.power.SendMessage("OnDestroy");
        if (r.power.Runner != null) r.power.Runner.SendMessage("OnDestroy");
        if (r.power.Secret != null) r.power.Secret.SendMessage("OnDestroy");
        Object.DestroyImmediate(r.ship);
        AttackPool.StopAll();
    }

    static GameObject Hazard(float x, float y)
    {
        var go = new GameObject("hazard");
        go.tag = "Enimey";
        go.transform.position = new Vector3(x, y, 0f);
        ClearTarget.Ensure(go).SetRadius(.3f);
        return go;
    }

    static bool Dead(GameObject go) { return go == null; }

    static FloatieBullet.Mods ModsFor(int pierce = 0, int bounces = 0, int shards = 0, int arc = 0, float damage = 1f)
    {
        var m = FloatieBullet.Mods.Plain;
        m.damage = damage; m.pierce = pierce; m.bounces = bounces; m.shards = shards; m.arc = arc;
        return m;
    }

    static FloatieBullet Launch(Vector3 at, Vector3 heading, FloatieBullet.Mods mods)
    {
        var b = FloatieBullets.Take();
        b.Launch(null, 1, at, heading, Color.white, default(FloatieAim.Pick), mods);
        return b;
    }

    static void Fly(FloatieBullet b, int maxSteps = 400)
    {
        for (int i = 0; i < maxSteps && b.Active; i++) b.Tick(Dt);
    }

    static int ActiveBullets()
    {
        return FloatieBullets.Active;
    }

    // ---- no world schedule: every atom, every world ------------------------------------------

    static void Schedule()
    {
        ShotAtoms.BeginRun(3); ShotAtoms.SetDroneCount(1);
        bool every = true, reach = true;
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
        {
            PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, w);
            // every atom can drop in each world (a fresh run: all six eligible)...
            ShotAtoms.BeginRun(3); ShotAtoms.SetDroneCount(1);
            every &= ShotAtoms.EligibleCount() == 6;
            var seen = new HashSet<ShotAtom>();
            ShotAtom a;
            for (int i = 0; i < 120; i++) if (ShotAtoms.TryPick(i / 120f, out a)) seen.Add(a);
            every &= seen.Count == 6;
            // ...and every atom reaches the top level there, then pays dust
            for (int atom = 0; atom < 6; atom++)
            {
                for (int n = 1; n <= ShotAtomTuning.MaxLevel; n++)
                {
                    var got = ShotAtoms.Collect((ShotAtom)atom);
                    reach &= !got.maxed && got.level == n;
                }
                var over = ShotAtoms.Collect((ShotAtom)atom);
                reach &= over.maxed && ShotAtoms.Level((ShotAtom)atom) == ShotAtomTuning.MaxLevel;
            }
        }
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, 0);
        Check("every atom can drop in every one of the " + WorldManager.Worlds.Length + " worlds from the start of a run", every);
        Check("every atom reaches level 10 in any world, and a catch past it pays dust", reach);
        string src = File.ReadAllText("Assets/Scripts/Gameplay/Pickups/ShotAtoms.cs");
        Check("no world unlock or level-cap tables are left", !src.Contains("UnlockWorld") && !src.Contains("MaxLevelOfWorld"));
    }

    static void DropPicking()
    {
        ShotAtoms.BeginRun(3);
        ShotAtom a;
        Check("a fresh run: all six atoms are eligible, the roll picks across them (0 -> TWIN, 0.99 -> RAPID)",
              ShotAtoms.EligibleCount() == 6 && ShotAtoms.TryPick(0f, out a) && a == ShotAtom.Twin && ShotAtoms.TryPick(.99f, out a) && a == ShotAtom.Rapid && ShotAtoms.TryPick(1f, out a));
        var c = ShotAtoms.Collect(ShotAtom.Rapid);
        Check("a catch raises the level (RAPID I)", !c.maxed && c.level == 1 && ShotAtoms.Level(ShotAtom.Rapid) == 1 && ShotAtoms.Callout(ShotAtom.Rapid, c.level) == "RAPID I");
        for (int n = 2; n < ShotAtomTuning.MaxLevel; n++) ShotAtoms.Collect(ShotAtom.Rapid);
        Check("a drop picks only atoms that are not maxed yet: RAPID IX still drops, RAPID X does not",
              ShotAtoms.CanDrop(ShotAtom.Rapid) && ShotAtoms.Collect(ShotAtom.Rapid).level == ShotAtomTuning.MaxLevel && !ShotAtoms.CanDrop(ShotAtom.Rapid) && ShotAtoms.EligibleCount() == 5);
        var seen = new HashSet<ShotAtom>();
        for (int i = 0; i < 100; i++) if (ShotAtoms.TryPick(i / 100f, out a)) seen.Add(a);
        Check("... the roll reaches the five others and never the maxed one", seen.Count == 5 && !seen.Contains(ShotAtom.Rapid));
        var over = ShotAtoms.Collect(ShotAtom.Rapid);
        Check("a catch of a maxed atom pays dust and keeps level 10 (counted)", over.maxed && ShotAtoms.Level(ShotAtom.Rapid) == ShotAtomTuning.MaxLevel && ShotAtoms.MaxedCatches == 1);
        for (int atom = 0; atom < ShotAtomTuning.Count; atom++) ShotAtoms.SetLevel((ShotAtom)atom, ShotAtomTuning.MaxLevel);
        Check("all six at level 10: no atom can drop (a drop pays dust instead)", !ShotAtoms.TryPick(.5f, out a) && ShotAtoms.EligibleCount() == 0);
        Check("the callouts read TWIN II / ARC X", ShotAtoms.Callout(ShotAtom.Twin, 2) == "TWIN II" && ShotAtoms.Callout(ShotAtom.Arc, 10) == "ARC X");
        Check("PowerLevel is Rapid plus the per-drone levels over the drone count (60 with all six maxed)", ShotAtoms.PowerLevel == 60 && ShotAtoms.MaxPower == 60);
        ShotAtoms.BeginRun(3);
    }

    // ---- drops --------------------------------------------------------------------

    static void OpeningDrops()
    {
        FreshScene();
        ShotAtoms.BeginRun(3); AtomSpacing.Reset();
        var seen = new List<ShotAtom>();
        ShotAtomDrops.Spawned = (atom, at) => seen.Add(atom);
        try
        {
            var pos = new Vector3(0f, 4f, 0f);
            ShotAtomDrops.TickOpening(1.9f, pos);
            Check("opening waits only two playable seconds, independent of kills", seen.Count == 0);
            ShotAtomDrops.TickOpening(.11f, pos);
            Check("the first early coin is TWIN", seen.Count == 1 && seen[0] == ShotAtom.Twin);
            // No pickup is collected: the order is determined by appearance.
            AtomSpacing.Tick(6f); ShotAtomDrops.TickOpening(6f, pos);
            Check("the second early coin is TWIN even if the first was missed", seen.Count == 2 && seen[1] == ShotAtom.Twin);
            AtomSpacing.Tick(6f); ShotAtomDrops.TickOpening(6f, pos);
            Check("the third early coin is ARC", seen.Count == 3 && seen[2] == ShotAtom.Arc);
            ShotAtomDrops.TickOpening(100f, pos);
            Check("scheduled opening stops after three visible coins", seen.Count == 3);
            AtomSpacing.Released();
            ShotAtomDrops.OnEliteKilled(pos);
            AtomSpacing.Tick(4f); ShotAtomDrops.Pump(.79f);
            Check("ordinary random selection resumes after the opener (a high roll gives RAPID)", seen.Count == 4 && seen[3] == ShotAtom.Rapid);
            ShotAtoms.BeginRun(3); AtomSpacing.Reset(); seen.Clear();
            ShotAtomDrops.OnEliteKilled(pos);
            Check("a kill before the schedule still gives the first TWIN", seen.Count == 1 && seen[0] == ShotAtom.Twin);
            AtomSpacing.Reset(); ShotAtomDrops.TickOpening(2.1f, pos);
            Check("an early kill consumes its opening slot, preventing a duplicate scheduled first drop", seen.Count == 1);
            ShotAtomDrops.Clear(); AtomSpacing.Reset();
            ShotAtomDrops.TickOpening(6f, pos);
            Check("clearing a queue does not restart the opening order", seen.Count == 2 && seen[1] == ShotAtom.Twin);
            ShotAtoms.BeginRun(3); seen.Clear(); AtomSpacing.Released();
            ShotAtomDrops.TickOpening(3f, pos);
            ShotAtomDrops.TickOpening(1f, pos);
            Check("a blocked opening coin queues once until pickup spacing is free", seen.Count == 0 && ShotAtomDrops.Pending == 1);
            AtomSpacing.Tick(4f); ShotAtomDrops.Pump(.99f);
            Check("the spacing queue preserves TWIN regardless of random roll", seen.Count == 1 && seen[0] == ShotAtom.Twin);
            ShotAtoms.BeginRun(3); seen.Clear(); AtomSpacing.Reset();
            buttonClicks.playerDied = true; ShotAtomDrops.TickOpening(100f, pos);
            buttonClicks.playerDied = false; startMenu.youAreInTutorial = true; ShotAtomDrops.TickOpening(100f, pos);
            startMenu.youAreInTutorial = false; ShotAtomDrops.TickOpening(1f, pos);
            Check("death/tutorial time never advances the opening clock", seen.Count == 0);
        }
        finally { ShotAtomDrops.Spawned = null; buttonClicks.playerDied = false; startMenu.youAreInTutorial = false; ShotAtoms.BeginRun(3); }
    }

    static void DropRates()
    {
        FreshScene();
        Check("the tuning: every elite, ordinary kills 8% or 18% / one per 12 s, Atom Rush x3 (cooldown halved)",
              ShotAtomTuning.EliteDropChance == 1f &&
              ShotAtomTuning.EnemyDropChance == .08f && ShotAtomTuning.StartingLevelDropChance == .18f && ShotAtomTuning.EnemyDropCooldown == 12f && ShotAtomTuning.RushMultiplier == 3f && ShotAtomTuning.RushCooldownScale == .5f);
        int before = ShotAtomDrops.Elites;
        // the first elite of a run always drops, whatever the roll
        ShotAtoms.BeginRun(3);
        Check("a new run has not had its first elite kill", !ShotAtomDrops.FirstEliteDone);
        int offered = 0;
        for (int i = 0; i <= 1000; i++)
        {
            ShotAtomDrops.Clear();
            AtomSpacing.Released();   // (the spacing gap is closed: an earned coin waits in the queue, nothing is built)
            int e0 = ShotAtomDrops.Elites;
            ShotAtomDrops.OnEliteKilled(Vector3.zero, i / 1000f);
            offered += ShotAtomDrops.Elites - e0;
        }
        Check("every elite drops a coin, whatever the roll (" + offered + " of 1001)", offered == 1001);
        ShotAtomDrops.Clear();
        int boss = ShotAtomDrops.Bosses;
        ShotAtomDrops.OnBossDestroyed(Vector3.zero, 0f, 0f, 0f);
        ShotAtomDrops.OnBossDestroyed(Vector3.zero, 1f, 1f, 1f);
        var recruits = Object.FindObjectsByType<DroneRewardPickup>(FindObjectsSortMode.None);
        Check("every boss drops exactly one drone recruit, independent of coin rolls", ShotAtomDrops.Bosses == boss + 2 && recruits.Length == 2);
        foreach (var recruit in recruits) Object.DestroyImmediate(recruit.gameObject);
        ShotAtomDrops.Clear();

        // a drop appears as a coin of an eligible atom; with the atom spacing busy it waits, then comes
        ShotAtoms.BeginRun(3);
        AtomSpacing.Reset();
        ShotAtomDrops.Clear();
        var spawned = new List<ShotAtom>();
        ShotAtomDrops.Spawned = (atom, at) => spawned.Add(atom);
        ShotAtomDrops.OnEliteKilled(new Vector3(.5f, 2f, 0f));
        var coins = Object.FindObjectsByType<ShotAtomPickup>(FindObjectsSortMode.None);
        Check("an elite kill puts its coin in the lane", coins.Length == 1 && spawned.Count == 1 && coins[0].atom == spawned[0] &&
              coins[0].CompareTag("pickUp") && coins[0].name == ShotAtomPickup.NameFor(spawned[0]));
        Check("... it floats with the atom drift (the world scroller + AtomSpin, no spin) and carries its flipbook",
              coins[0].GetComponent<moveItemEnmInStrightLine>() != null && coins[0].GetComponent<AtomSpin>() != null &&
              Mathf.Approximately(coins[0].GetComponent<AtomSpin>().degreesPerSecond, 0f) && coins[0].GetComponent<PickupFlipbook>() != null &&
              coins[0].GetComponent<PickupFlipbook>().kind == ShotAtoms.KindOf(coins[0].atom) && coins[0].GetComponent<PickupFlipbook>().FrameCount == 14);
        Check("... and the atom spacing now holds the next atom back", !AtomSpacing.Ready);
        ShotAtomDrops.OnEliteKilled(Vector3.up);
        Check("a second elite coin inside the spacing gap waits its turn", ShotAtomDrops.Pending == 1 && spawned.Count == 1);
        AtomSpacing.Tick(AtomSpacing.Gap + .1f);
        ShotAtomDrops.Pump(0f);
        Check("... and is released once the gap has passed (coins that were never caught do not use up the roll)", ShotAtomDrops.Pending == 0 && ShotAtomDrops.Released >= 2);
        ClearCoins();

        // everything maxed: dust, no coin
        ShotAtoms.BeginRun(3);
        for (int atom = 0; atom < ShotAtomTuning.Count; atom++) ShotAtoms.SetLevel((ShotAtom)atom, ShotAtomTuning.MaxLevel);
        AtomSpacing.Reset();
        ShotAtomDrops.Clear();
        int dust = ShotAtomDrops.DustDrops;
        ShotAtomDrops.OnEliteKilled(Vector3.zero);
        Check("a drop with everything maxed pays dust instead of a coin", ShotAtomDrops.DustDrops >= dust + 1 && ShotAtomDrops.LastWasDust &&
              Object.FindObjectsByType<ShotAtomPickup>(FindObjectsSortMode.None).Length == 0);
        ShotAtomDrops.Spawned = null;
        ShotAtoms.BeginRun(3);
    }

    static void DropHooks()
    {
        string elites = File.ReadAllText("Assets/Scripts/Gameplay/Elites/EliteFx.cs");
        string boss = File.ReadAllText("Assets/Scripts/Bosses/BossEncounter.cs");
        Check("a killed elite (EliteRewards.Pay) can drop one", elites.Contains("ShotAtomDrops.OnEliteKilled("));
        Check("a destroyed boss (BeginOutro, not an escape) drops one (and maybe a second)", boss.Contains("if (explode) ShotAtomDrops.OnBossDestroyed("));
        int callers = 0;
        foreach (var file in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            if (file.EndsWith("ShotAtoms.cs")) continue;
            if (text.Contains("ShotAtomDrops.OnEliteKilled(") || text.Contains("ShotAtomDrops.OnBossDestroyed(")) callers++;
        }
        Check("elites and bosses are earned in two places (" + callers + " files), ordinary kills in a third: collisionDetection.AwardDestroyedTarget",
              callers == 2 && File.ReadAllText("Assets/Scripts/Ship/collisionDetection.cs").Contains("ShotAtomDrops.OnEnemyKilled("));

        FreshScene();
        AtomSpacing.Reset();
        ShotAtomDrops.Clear();
        ShotAtomDrops.ResetSeams();
        int released = ShotAtomDrops.Released;
        for (int i = 0; i < 30; i++)
        {
            var enemy = Hazard(i * .01f, 1f);
            ShipAttackHits.Hit(enemy, 1);
        }
        Check("an unrelated edit-mode kill never rolls a coin by itself (no override, no roll)", ShotAtomDrops.Released == released && ShotAtomDrops.Pending == 0);

        // ---- ordinary kills: level-specific chance, at most one per 12 s, spacing respected ----
        float clock = 100f;
        ShotAtomDrops.ClockOverride = () => clock;
        ShotAtoms.BeginRun(3);
        AtomSpacing.Reset();
        ShotAtomDrops.Clear();
        var plain = Hazard(0f, 1f);
        int enemies = ShotAtomDrops.Enemies;
        Check("an ordinary kill on a bad roll drops nothing", !ShotAtomDrops.OnEnemyKilled(plain, .5f) && ShotAtomDrops.Enemies == enemies);
        Check("... a roll under 8% drops one", ShotAtomDrops.OnEnemyKilled(plain, .01f) && ShotAtomDrops.Enemies == enemies + 1);
        ShotAtomDrops.Clear();
        AtomSpacing.Reset();
        clock += 11f;
        Check("... but not again within 12 s", !ShotAtomDrops.OnEnemyKilled(plain, .0f) && ShotAtomDrops.Enemies == enemies + 1);
        clock += 1.5f;
        Check("... and again after the cooldown", ShotAtomDrops.OnEnemyKilled(plain, .0f) && ShotAtomDrops.Enemies == enemies + 2);
        ShotAtomDrops.Clear();
        AtomSpacing.Reset();
        int hit = 0;
        for (int i = 0; i < 1000; i++)
        {
            clock += 15f;
            ShotAtomDrops.Clear();
            AtomSpacing.Reset();
            if (ShotAtomDrops.OnEnemyKilled(plain, i / 1000f)) hit++;
        }
        Check("evenly spread rolls match the current level chance (of 1000: " + hit + ")", hit == Mathf.RoundToInt(ShotAtomDrops.EnemyChance * 1000f));
        // a boss body or a hazard that happens to carry an identity never rolls
        var rock = Hazard(0f, 1f);
        rock.tag = "Astr";
        var bossBit = Hazard(0f, 1f);
        bossBit.name = "BossPart";
        clock += 100f;
        Check("rocks (tag Astr) and boss parts never drop from the kill roll", !ShotAtomDrops.OnEnemyKilled(rock, 0f) && !ShotAtomDrops.OnEnemyKilled(bossBit, 0f));
        // spacing: with the gap busy the coin waits in the queue, it is not built at once
        ShotAtomDrops.Clear();
        AtomSpacing.Released();
        int rel = ShotAtomDrops.Released;
        clock += 100f;
        ShotAtomDrops.OnEnemyKilled(plain, 0f);
        Check("an ordinary drop respects the atom spacing (queued, not released)", ShotAtomDrops.Pending == 1 && ShotAtomDrops.Released == rel);
        ShotAtomDrops.Clear();
        ClearCoins();

        // ---- ATOM RUSH: every chance x3 for the run it was spent in ----
        Boosters.SetRunRushForTest(true);
        Check("rush: ordinary chance 9%, cooldown 6 s, elite 100%, boss second 100%",
              Mathf.Approximately(ShotAtomDrops.EnemyChance, (WorldManager.CurrentIndex == WorldManager.RunStart ? .54f : .24f)) && Mathf.Approximately(ShotAtomDrops.EnemyCooldown, 6f) &&
              ShotAtomDrops.Rushed(ShotAtomTuning.EliteDropChance) == 1f && ShotAtomDrops.Rushed(1f) == 1f);
        ShotAtoms.BeginRun(3);
        ShotAtomDrops.Clear();
        AtomSpacing.Reset();
        clock += 100f;
        Check("rush: a roll of 8% drops (it would not without the booster)", ShotAtomDrops.OnEnemyKilled(plain, .08f));
        ShotAtomDrops.Clear();
        AtomSpacing.Reset();
        clock += 7f;
        Check("rush: the cooldown is 6 s", ShotAtomDrops.OnEnemyKilled(plain, 0f));
        ShotAtomDrops.Clear();
        AtomSpacing.Reset();
        int firstElite = ShotAtomDrops.Elites;
        ShotAtomDrops.OnEliteKilled(Vector3.zero, .999f);   // the first: guaranteed
        ShotAtomDrops.Clear();
        AtomSpacing.Reset();
        ShotAtomDrops.OnEliteKilled(Vector3.zero, .999f);
        Check("rush: every elite drops on the worst roll", ShotAtomDrops.Elites == firstElite + 2);
        ShotAtomDrops.Clear();
        AtomSpacing.Reset();
        int b0 = ShotAtomDrops.Bosses;
        ShotAtomDrops.OnBossDestroyed(Vector3.zero, 0f, 0f, .999f);
        Check("rush does not duplicate a boss drone recruit", ShotAtomDrops.Bosses == b0 + 1);
        Boosters.SetRunRushForTest(false);
        ShotAtomDrops.Clear();
        AtomSpacing.Reset();
        clock += 100f;
        Check("rush off: back to the normal level chance", !ShotAtomDrops.OnEnemyKilled(plain, .20f));
        ShotAtomDrops.ResetSeams();
        ShotAtomDrops.Clear();
        ClearCoins();
        FreshScene();
        startMenu.youAreInTutorial = true;
        Check("no drop in the tutorial", !ShotAtomDrops.OnBossDestroyed(Vector3.zero, 0f));
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = true;
        Check("... nor once the pilot is dead", !ShotAtomDrops.OnEliteKilled(Vector3.zero, 0f));
        buttonClicks.playerDied = false;
        ClearCoins();
    }

    // ---- the atoms' effects -----------------------------------------------------------

    static void Twin()
    {
        FreshScene();
        var r = MakeShip();
        bool counts = true, angles = true, damage = true, growth = true, widening = true;
        for (int level = 0; level <= ShotAtomTuning.MaxLevel; level++)
        {
            ShotAtoms.SetLevel(ShotAtom.Twin, level);
            FloatieBullets.ReleaseAll();
            counts &= r.battery.Fire(0);
            var active = new List<FloatieBullet>();
            foreach (var b in FloatieBullets.All) if (b.Active) active.Add(b);
            counts &= active.Count == ShotAtomTuning.TwinShots[level];
            for (int k = 0; k < active.Count && k < ShotAtomTuning.TwinAngles[level].Length; k++)
            {
                Vector3 h = active[k].Heading;
                float a = Mathf.Atan2(-h.x, h.y) * Mathf.Rad2Deg;   // + is left
                angles &= Mathf.Abs(a - ShotAtomTuning.TwinAngles[level][k]) < .1f;
                damage &= Mathf.Approximately(active[k].Applied.damage, ShotAtomTuning.TwinDamage[level]);
            }
            if (level >= 4)
            {
                float prev = ShotAtomTuning.TwinShots[level - 1] * ShotAtomTuning.TwinDamage[level - 1];
                float now = ShotAtomTuning.TwinShots[level] * ShotAtomTuning.TwinDamage[level];
                growth &= now >= prev && Mathf.Approximately(ShotAtomTuning.TwinDamage[level], .75f);
                widening &= ShotAtomTuning.TwinHalfFan[level] > ShotAtomTuning.TwinHalfFan[level - 1];
            }
        }
        Check("TWIN: 1, 2, 3, 4, 4, 5, 5, 6, 6, 7, 7 bullets a volley at levels 0-10", counts);
        Check("... L1 two parallel, L2+ a fan whose angles are the tuning's (it widens with the level, at most +-" + ShotAtomTuning.TwinHalfFan[10] + " degrees)", angles && widening && ShotAtomTuning.TwinHalfFan[10] <= 22f);
        Check("... the volley never loses damage on an upgrade and each bullet uses three quarters (L10 = " +
              (ShotAtomTuning.TwinShots[10] * ShotAtomTuning.TwinDamage[10]).ToString("0.00") + "x of one shot)", damage && growth &&
              Mathf.Approximately(ShotAtomTuning.TwinShots[10] * ShotAtomTuning.TwinDamage[10], 5.25f) && ShotAtomTuning.TwinDamage[1] < 1f);
        ShotAtoms.SetLevel(ShotAtom.Twin, 1);
        FloatieBullets.ReleaseAll();
        r.battery.Fire(0);
        var two = new List<FloatieBullet>();
        foreach (var b in FloatieBullets.All) if (b.Active) two.Add(b);
        float gap = Mathf.Abs(two[0].transform.position.x - two[1].transform.position.x);
        Check("... the level 1 pair flies parallel, " + ShotAtomTuning.TwinParallelGap + " apart", two.Count == 2 && Mathf.Abs(gap - ShotAtomTuning.TwinParallelGap) < .01f &&
              Vector3.Dot(two[0].Heading, two[1].Heading) > .9999f);
        Check("... it is one volley: the floatie's shot counter counts it once", r.battery.Shots[0] == ShotAtomTuning.MaxLevel + 2);  // one Fire per level 0..max, plus the level 1 pair
        Teardown(r);

        // an elite takes a quarter heart times the bullet's damage
        FreshScene();
        EliteSystem.Clear();
        var pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -2.5f, 0f);
        EliteSystem.PlayerOverride = pilot;
        var e = EliteShip.CreateInPlay(BaseEliteDefs.For("gunship"), new Vector2(0f, 1f));
        e.AttackCooldown = 99f;
        float initialHealth = e.Health;
        var bullet = Launch(e.transform.position + Vector3.down * .08f, Vector3.up, ModsFor(damage: ShotAtomTuning.TwinDamage[2]));
        bullet.Tick(Dt);
        Check("a TWIN bullet removes three quarters of an elite heart, with no extra scaling",
              Mathf.Approximately(initialHealth - e.Health, .75f) && !bullet.Active);
        Object.DestroyImmediate(pilot.gameObject);
        EliteSystem.PlayerOverride = null;
    }

    static void Pierce()
    {
        FreshScene();
        bool ok = true;
        string detail = "";
        for (int level = 0; level <= ShotAtomTuning.MaxLevel; level++)
        {
            var targets = new List<GameObject>();
            for (int i = 0; i < ShotAtomTuning.MaxLevel + 2; i++) targets.Add(Hazard(0f, 0f + i * .4f));
            var b = Launch(new Vector3(0f, -.6f, 0f), Vector3.up, ModsFor(pierce: ShotAtomTuning.PierceThrough[level]));
            Fly(b);
            int killed = 0;
            foreach (var t in targets) if (Dead(t)) killed++;
            ok &= killed == level + 1;
            detail += " L" + level + "=" + killed;
            foreach (var t in targets) if (t != null) Object.DestroyImmediate(t);
            FloatieBullets.ReleaseAll();
        }
        Check("PIERCE: a shot kills 1 .. 11 enemies in a line at levels 0-10 (through +1 enemy a level):" + detail, ok);
        // never twice through the same enemy: a big body is hit once
        var wide = Hazard(0f, 1f);
        wide.GetComponent<ClearTarget>().SetRadius(.6f);
        var pb = Launch(new Vector3(0f, .2f, 0f), Vector3.up, ModsFor(pierce: 3));
        int before = FloatieBullet.Pierced;
        Fly(pb, 4);
        Check("... a pierced enemy is counted once (it is not hit again while the shot is inside it)", FloatieBullet.Pierced - before <= 1);
        if (wide != null) Object.DestroyImmediate(wide);
        FloatieBullets.ReleaseAll();
    }

    static void Split()
    {
        FreshScene();
        bool counts = true, shardsOk = true;
        for (int level = 1; level <= ShotAtomTuning.MaxLevel; level++)
        {
            FloatieBullets.ReleaseAll();
            var target = Hazard(0f, 1f);
            int before = FloatieBullet.ShardsLaunched;
            var b = Launch(new Vector3(0f, .92f, 0f), Vector3.up, ModsFor(shards: level));
            b.Tick(Dt);
            counts &= FloatieBullet.ShardsLaunched - before == ShotAtomTuning.SplitShards[level] && !b.Active;
            int shards = 0;
            foreach (var s in FloatieBullets.All)
            {
                if (!s.Active) continue;
                shards++;
                shardsOk &= s.Applied.shard && Mathf.Approximately(s.Applied.damage, ShotAtomTuning.SplitDamage[level]) && s.Applied.shards == 0 && s.Applied.arc == 0 && s.Applied.pierce == 0;
                Vector3 h = s.Heading;
                shardsOk &= Mathf.Abs(Mathf.Atan2(-h.x, h.y) * Mathf.Rad2Deg) > 1f || ShotAtomTuning.SplitShards[level] % 2 == 1;   // diagonal (an odd count keeps one straight on)
            }
            counts &= shards == ShotAtomTuning.SplitShards[level];
            if (target != null) Object.DestroyImmediate(target);
        }
        Check("SPLIT: a hit breaks the shot into 2, 2, 3, 3, 4, 4, 5, 5, 6, 6 shards at levels 1-10 (and the shot itself ends), shard damage 50% -> 75%", counts &&
              ShotAtomTuning.SplitDamage[1] == .50f && ShotAtomTuning.SplitDamage[10] == .75f);
        Check("... small partial-damage shards that never split, zap or pierce again, flying diagonally", shardsOk);

        // a shard really hits: level 1 sends shards at +-45 degrees; an enemy on that line dies
        FloatieBullets.ReleaseAll();
        var first = Hazard(0f, 1f);
        float d = 1.2f;
        var side = Hazard(Mathf.Sin(ShotAtomTuning.SplitHalfFan[1] * Mathf.Deg2Rad) * d, 1f + Mathf.Cos(ShotAtomTuning.SplitHalfFan[1] * Mathf.Deg2Rad) * d);
        var b2 = Launch(new Vector3(0f, .92f, 0f), Vector3.up, ModsFor(shards: 1));
        var pool = FloatieBullets.All;
        for (int i = 0; i < 60; i++)
            for (int k = 0; k < pool.Count; k++) if (pool[k].Active) pool[k].Tick(Dt);
        Check("... a shard kills an enemy standing on its diagonal", Dead(first) && Dead(side));
        if (side != null) Object.DestroyImmediate(side);
        FloatieBullets.ReleaseAll();
    }

    static void Ricochet()
    {
        FreshScene();
        bool ok = true;
        string detail = "";
        for (int level = 0; level <= ShotAtomTuning.MaxLevel; level++)
        {
            FloatieBullets.ReleaseAll();
            int before = FloatieBullet.Bounced;
            Vector3 heading = Quaternion.Euler(0f, 0f, -88f) * Vector3.up;   // nearly flat to the right: the most bounces a life can hold
            var m = ModsFor(bounces: ShotAtomTuning.Bounces[level]);
            m.life = ShotAtomTuning.RicochetLife[level];
            var b = Launch(new Vector3(0f, -3f, 0f), heading, m);
            bool inside = true;
            float aged = 0f;
            for (int i = 0; i < 900 && b.Active; i++)
            {
                b.Tick(Dt);
                aged += Dt;
                inside &= b.transform.position.x >= -2.82f && b.transform.position.x <= 2.82f || b.BouncesLeft == 0;
            }
            int bounced = FloatieBullet.Bounced - before;
            // every bounce a life can hold is made (a crossing takes ~.63 s); the cap on the life is never passed
            bool lifeOk = aged <= Mathf.Max(FloatieBullet.MaxAge, ShotAtomTuning.RicochetLife[level]) + .05f;
            ok &= (level <= 5 ? bounced == level : bounced >= 5 && bounced <= level) && inside && lifeOk;
            detail += " L" + level + "=" + bounced;
        }
        Check("RICOCHET: a shot bounces off the side edges up to 0 .. 10 times (all of them while its life lasts, at most " + ShotAtomTuning.RicochetLifeCap + " s) and stays on screen while it has bounces:" + detail,
              ok && ShotAtomTuning.RicochetLife[10] == ShotAtomTuning.RicochetLifeCap && ShotAtomTuning.Bounces[10] == 10);

        FreshScene();
        var r = MakeShip();
        ShotAtoms.SetLevel(ShotAtom.Ricochet, 1);
        bool leans = true;
        for (int i = 0; i < 20; i++)
        {
            FloatieBullets.ReleaseAll();
            r.battery.Fire(0);
            FloatieBullet b = null;
            foreach (var x in FloatieBullets.All) if (x.Active) b = x;
            Vector3 h = b.Heading;
            leans &= Mathf.Abs(Mathf.Abs(Mathf.Atan2(-h.x, h.y) * Mathf.Rad2Deg) - ShotAtomTuning.RicochetLeanDegrees) < .1f && b.BouncesLeft == 1;
        }
        Check("... the straight gun's shots lean " + ShotAtomTuning.RicochetLeanDegrees + " degrees so a bounce can happen at all", leans);
        Teardown(r);
    }

    static void Arc()
    {
        FreshScene();
        // a grid of enemies a unit apart inside the view: the chain always has a next one in range
        bool ok = true;
        string detail = "";
        for (int level = 1; level <= ShotAtomTuning.MaxLevel; level++)
        {
            FloatieBullets.ReleaseAll();
            VoltArcs.StopAll();
            var grid = new List<GameObject>();
            for (int gx = -2; gx <= 2; gx++)
                for (int gy = 1; gy <= 3; gy++) grid.Add(Hazard(gx, gy));
            int zaps = FloatieBullet.ArcZaps, arcs = VoltArcs.TotalDrawn;
            var bullet = Launch(new Vector3(0f, .92f, 0f), Vector3.up, ModsFor(arc: level));
            bullet.Tick(Dt);
            int dead = 0;
            foreach (var t in grid) if (Dead(t)) dead++;
            int jumps = FloatieBullet.ArcZaps - zaps;
            bool levelOk = jumps == ShotAtomTuning.ArcJumps[level] && dead == 1 + ShotAtomTuning.ArcJumps[level] &&
                           VoltArcs.TotalDrawn - arcs == ShotAtomTuning.ArcJumps[level];
            ok &= levelOk;
            detail += " L" + level + "=" + jumps;
            foreach (var t in grid) if (t != null) Object.DestroyImmediate(t);
        }
        Check("ARC: a hit zaps the nearest other enemies in range, 1, 1, 2, 2, 3, 3, 4, 4, 5, 6 jumps at levels 1-10:" + detail, ok);

        // range: a target just inside a level's range is reached, just past it is not
        bool rangeOk = true;
        for (int level = 1; level <= ShotAtomTuning.MaxLevel; level++)
        {
            float r = ShotAtomTuning.ArcRange[level];
            var a = Hazard(-2f, 1f);
            var near = Hazard(-2f + r - .05f, 1f);
            var bullet = Launch(new Vector3(-2f, .92f, 0f), Vector3.up, ModsFor(arc: level));
            bullet.Tick(Dt);
            rangeOk &= Dead(near);
            foreach (var t in new[] { a, near }) if (t != null) Object.DestroyImmediate(t);
            FloatieBullets.ReleaseAll();
            VoltArcs.StopAll();
            var a2 = Hazard(-2f, 1f);
            var far = Hazard(-2f + r + .05f, 1f);
            var bullet2 = Launch(new Vector3(-2f, .92f, 0f), Vector3.up, ModsFor(arc: level));
            bullet2.Tick(Dt);
            rangeOk &= !Dead(far);
            foreach (var t in new[] { a2, far }) if (t != null) Object.DestroyImmediate(t);
            FloatieBullets.ReleaseAll();
        }
        Check("... a target just inside a level's range is reached, just past it is not", rangeOk);
        bool grows = ShotAtomTuning.ArcRange[1] == 2f && ShotAtomTuning.ArcRange[10] == 4.25f && ShotAtomTuning.ArcDamage[1] == .5f && ShotAtomTuning.ArcDamage[10] == 1f &&
                     ShotAtomTuning.ArcJumps[10] == 6 && ShotAtomTuning.ArcJumps[10] + 2 <= 8;
        for (int l = 2; l <= ShotAtomTuning.MaxLevel; l++)
            grows &= ShotAtomTuning.ArcRange[l] > ShotAtomTuning.ArcRange[l - 1] && ShotAtomTuning.ArcDamage[l] >= ShotAtomTuning.ArcDamage[l - 1] && ShotAtomTuning.ArcJumps[l] >= ShotAtomTuning.ArcJumps[l - 1];
        Check("... range 2.0 -> 4.25 grows every level and quarter damage 50% -> 100% never drops, the jumps never go down (1 .. 6)", grows);
        VoltArcs.StopAll();
        var a1 = Hazard(0f, 1f);
        var b1 = Hazard(1f, 1f);
        var shot = Launch(new Vector3(0f, .92f, 0f), Vector3.up, ModsFor(arc: 1));
        shot.Tick(Dt);
        var live = VoltArcs.LiveAt(0);
        Check("... drawn with the Volt Viper's arc in ARC's own pale lightning (not the Viper's sodium white / teal, and no pink)",
              live != null && live.coreColor == ShotAtomTuning.ArcCore && live.outerColor == ShotAtomTuning.ArcOuter && live.starColor == ShotAtomTuning.ArcStar &&
              ShotAtomTuning.ArcCore != VoltArcs.CoreColor && ShotAtomTuning.ArcOuter.b > ShotAtomTuning.ArcOuter.r && ShotAtomTuning.ArcStar.b >= ShotAtomTuning.ArcStar.r);
        foreach (var t in new[] { a1, b1 }) if (t != null) Object.DestroyImmediate(t);
        VoltArcs.StopAll();
        FloatieBullets.ReleaseAll();
    }

    static void Rapid()
    {
        FreshScene();
        float baseCd = FloatieTuning.Cooldown(0, 0, false), baseSeeker = FloatieTuning.Cooldown(2, 4, false), baseBoss = FloatieTuning.Cooldown(0, 0, true);
        bool ok = true;
        float[] bonus = ShotAtomTuning.RapidBonus;
        for (int level = 0; level <= ShotAtomTuning.MaxLevel; level++)
        {
            ShotAtoms.SetLevel(ShotAtom.Rapid, level);
            ok &= Mathf.Abs(FloatieTuning.Cooldown(0, 0, false) - Mathf.Max(ShotAtomTuning.MinCooldown, baseCd / (1f + bonus[level]))) < 1e-4f;
            ok &= Mathf.Abs(FloatieTuning.Cooldown(2, 4, false) - Mathf.Max(ShotAtomTuning.MinCooldown, baseSeeker / (1f + bonus[level]))) < 1e-4f;
            ok &= Mathf.Abs(FloatieTuning.Cooldown(0, 0, true) - Mathf.Max(ShotAtomTuning.MinCooldown, baseBoss / (1f + bonus[level]))) < 1e-4f;
        }
        Check("RAPID: the gun's cooldown is base / (1 + 25% .. 250%), the seeker's and a boss fight's too (base " + baseCd + " s on this build)", ok &&
              bonus[1] == .25f && bonus[2] == .50f && bonus[9] == 2.25f && bonus[10] == 2.50f);
        ShotAtoms.SetLevel(ShotAtom.Rapid, ShotAtomTuning.MaxLevel);
        Check("... and never under the floor (" + ShotAtomTuning.MinCooldown + " s): a 1 s cooldown at level 10 is " + ShotAtoms.Rapid(1f).ToString("0.00") + " s, a cooldown already lower is left alone",
              Mathf.Abs(ShotAtoms.Rapid(1f) - ShotAtomTuning.MinCooldown) < 1e-5f && Mathf.Abs(ShotAtoms.Rapid(.3f) - .3f) < 1e-5f && FloatieTuning.Cooldown(0, 0, false) >= ShotAtomTuning.MinCooldown);
        var r = MakeShip();
        r.battery.ResetTimers();
        for (int i = 0; i < 3; i++) r.battery.Shots[i] = 0;
        for (float t = 0f; t < 12f; t += Dt) { r.battery.Step(Dt); if (Mathf.Approximately(t % 1f, 0f)) FloatieBullets.ReleaseAll(); }
        float expected = 12f / (baseCd / (1f + ShotAtomTuning.RapidBonus[ShotAtomTuning.MaxLevel]));
        Check("... and it fires that much faster in play (" + r.battery.Shots[0] + " shots in 12 s, about " + expected.ToString("0") + ")",
              Mathf.Abs(r.battery.Shots[0] - expected) <= 2f);
        Check("the pure rule: Rapid(seconds) = seconds / (1 + bonus), floored", Mathf.Abs(ShotAtoms.Rapid(2f) - 2f / 3.5f) < 1e-4f && Mathf.Abs(ShotAtoms.RapidBonus() - 2.5f) < 1e-5f);
        Teardown(r);
    }

    static void BulletCap()
    {
        FreshScene();
        FloatieBullets.ReleaseAll();
        AttackPool.StopAll();
        int made = 0;
        for (int i = 0; i < ShotAtomTuning.MaxBullets + 8; i++)
        {
            var b = FloatieBullets.Take();
            if (b == null) break;
            b.Launch(null, 1, new Vector3(0f, -3f, 0f), Vector3.up, Color.white, default(FloatieAim.Pick));
            made++;
        }
        Check("the cap: at most " + ShotAtomTuning.MaxBullets + " player bullets in the air (" + made + ")", made == ShotAtomTuning.MaxBullets && ActiveBullets() == ShotAtomTuning.MaxBullets &&
              FloatieTuning.PoolSize == ShotAtomTuning.MaxBullets && FloatieBullets.Take() == null);
        Check("... and the shared projectile cap still stands over it", ShotAtomTuning.MaxBullets + AttackPool.MaxProjectiles == AttackPool.MaxCombinedProjectiles);
        FloatieBullets.ReleaseAll();

        // ALL-L10 SOAK: every atom at level 10, three floaties, a target to hit, one simulated minute: never over the cap, the object
        // count flat, nothing allocated per frame
        var r = MakeShip();
        r.battery.SetLevel(4);
        for (int i = 0; i < ShotAtomTuning.Count; i++) ShotAtoms.SetLevel((ShotAtom)i, ShotAtomTuning.MaxLevel);
        var target = Hazard(1.5f, 4.4f);
        int peak = 0, peakShared = 0, peakTotal = 0;
        r.battery.ResetTimers();
        int objects0 = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Length;
        for (int i = 0; i < 3600; i++)
        {
            r.battery.Step(Dt);
            peak = Mathf.Max(peak, FloatieBullets.Active);
            peakTotal = Mathf.Max(peakTotal, FloatieBullets.Active + AttackPool.ActiveProjectiles);
            peakShared = Mathf.Max(peakShared, FloatieBullets.Size);
            if (i % 300 == 0 && (target == null || !target.activeInHierarchy)) target = Hazard(1.5f, 4.4f);
        }
        Check("every atom at level 10 on a three-floatie ship for a minute: the bullets in the air never pass the cap (peak " + peak + " of " + ShotAtomTuning.MaxBullets + ")", peak <= ShotAtomTuning.MaxBullets && peak > 0);
        Check("... and with the attack pool they never pass its shared cap (peak " + peakTotal + " of " + AttackPool.MaxCombinedProjectiles + "), the bullet pool never grows past its size (" + peakShared + ")",
              peakTotal <= AttackPool.MaxCombinedProjectiles && peakShared <= ShotAtomTuning.MaxBullets && VoltArcs.PoolSize <= VoltArcs.MaxArcs);
        for (int i = 0; i < 400; i++) { r.battery.Step(Dt); if (i % 20 == 0) FloatieBullets.ReleaseAll(); }
        FloatieBullets.ReleaseAll();
        r.battery.ResetTimers();
        int shots = r.battery.Shots[0] + r.battery.Shots[1] + r.battery.Shots[2];
        long bytes = TestHarness.AllocatedBytes(() => { for (int i = 0; i < 600; i++) r.battery.Step(Dt); });
        int fired = r.battery.Shots[0] + r.battery.Shots[1] + r.battery.Shots[2] - shots;
        Check("... and firing with every atom at level 10 allocates nothing (" + bytes + " bytes over " + fired + " volleys)", fired >= 20 && bytes == 0);
        int objects1 = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Length;
        Check("... and the object count is bounded: " + objects0 + " -> " + objects1 + " (at most the bullet pool + its root, the arc pool at 5 objects an arc + its root, and a few enemies more)",
              objects1 - objects0 <= ShotAtomTuning.MaxBullets + 1 + VoltArcs.MaxArcs * 5 + 1 + 4);
        if (target != null) Object.DestroyImmediate(target);
        Teardown(r);
    }

    // ---- catching a coin ---------------------------------------------------------------

    internal static Rig2 SpawnShip()
    {
        var r = new Rig2();
        r.ship = new GameObject(ShipId.ObjectName(1) + "(Clone)", typeof(SpriteRenderer), typeof(BoxCollider2D));
        r.ship.GetComponent<BoxCollider2D>().isTrigger = true;
        r.ship.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        spawnShips.ApplyHull(r.ship, 1);
        r.cd = r.ship.AddComponent<collisionDetection>();
        r.cd.explosionAnimation = new GameObject("~TestExplosion");
        r.cd.boostSound = r.ship.AddComponent<AudioSource>();
        r.cd.boostText = new GameObject("~boostText", typeof(RectTransform)).AddComponent<Text>();
        r.cd.hypeText = new GameObject("~hypeText", typeof(RectTransform)).AddComponent<Text>();
        r.cd.boost = new GameObject("~boost");
        r.cd.boost.SetActive(false);
        r.trigger = (Action<Collider2D>)Delegate.CreateDelegate(typeof(Action<Collider2D>), r.cd, typeof(collisionDetection).GetMethod("OnTriggerEnter2D", Inst));
        r.cd.shield = ShipShield.For(r.ship).Visual;
        collisionDetection.MAXLIFE = 3;
        collisionDetection.lifeCounter = 0;
        collisionDetection.atomCheck = false;
        collisionDetection.invTimer = 0f;
        return r;
    }

    internal sealed class Rig2
    {
        public GameObject ship;
        public collisionDetection cd;
        public Action<Collider2D> trigger;
    }

    static void Catching()
    {
        FreshScene();
        ShotAtoms.BeginRun(3);
        var rig = SpawnShip();
        var canvasGo = new GameObject("~AtomPopupCanvas", typeof(Canvas));
        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var speedGo = new GameObject("SpeedText", typeof(RectTransform), typeof(Text));
        speedGo.transform.SetParent(canvasGo.transform, false);
        var popupHud = ScoreHud.AttachPopupsOnly(canvasGo, speedGo.GetComponent<Text>());
        if (popupHud != null) popupHud.SendMessage("OnEnable");   // edit mode: subscribe by hand
        int pool0 = popupHud != null ? popupHud.PopupPoolSize : -1;
        var coin = ShotAtomPickup.Build(ShotAtom.Rapid, Vector3.zero);
        var coinCollider = coin.GetComponent<Collider2D>();
        Check("the coin is a pickUp with the atom's name and the shot art (196 px, 700 px/unit like every atom)",
              coin.CompareTag("pickUp") && coin.name == "shotAtom_rapid" && coin.GetComponent<SpriteRenderer>().sprite != null &&
              coin.GetComponent<SpriteRenderer>().sprite.texture.width == 196 && Mathf.Approximately(coin.GetComponent<SpriteRenderer>().sprite.pixelsPerUnit, 700f));
        int caught = collisionDetection.shotAtomPickups;
        rig.trigger(coin.GetComponent<Collider2D>());
        if (PickupBurst.LastPlayed != null) PickupBurst.LastPlayed.Finish();
        Check("catching it adds a level (RAPID I), counts the pickup and calls it out",
              ShotAtoms.Level(ShotAtom.Rapid) == 1 && collisionDetection.shotAtomPickups == caught + 1 && rig.cd.hypeText.text == "RAPID I");
        Check("... with its own pickup burst (the shot_rapid_burst flipbook)", PickupBurst.LastPlayed != null &&
              PickupArt.BurstName(PickupKind.ShotRapid) == "shot_rapid_burst" && PickupArt.Frames("shot_rapid_burst", 6)[0] != null);
        {
            var shown = popupHud != null ? popupHud.AtomUpgradeText : null;
            Check("catching a coin shows the upgrade popup: 'RAPID FIRE  ALL DRONES', the big 'LV 1' and the hint '+25% fire rate', in the atom's own colour",
                  shown != null && shown.text.Contains("RAPID FIRE  ALL DRONES") && shown.text.Contains("LV 1") && shown.text.Contains("+25% FIRE RATE") && shown.color == ShotAtoms.Colour(ShotAtom.Rapid));
        }
        var third = ShotAtomPickup.Build(ShotAtom.Arc, Vector3.zero);
        var firstPopup = popupHud != null ? popupHud.AtomUpgradeText : null;
        rig.trigger(third.GetComponent<Collider2D>());
        Check("an ARC coin in the same world is ARC I (any atom, any world) and the HUD order is catch order (RAPID, ARC)", ShotAtoms.Level(ShotAtom.Arc) == 1 && rig.cd.hypeText.text == "ARC I" &&
              ShotAtoms.OrderCount == 2 && ShotAtoms.OrderAt(0) == ShotAtom.Rapid && ShotAtoms.OrderAt(1) == ShotAtom.Arc);
        {
            var shown = popupHud != null ? popupHud.AtomUpgradeText : null;
            Check("a second catch while a popup shows WAITS in the queue (the first stays, nothing lost, no new popup object)",
                  shown != null && ReferenceEquals(shown, firstPopup) && shown.text.Contains("RAPID FIRE") && popupHud.AtomQueued == 1 && popupHud.PopupPoolSize == pool0);
            popupHud.StepPopups(ShotAtoms.UpgradeSeconds);
            shown = popupHud.AtomUpgradeText;
            Check("... and plays the moment the first leaves (ARC SHOT, LV 1)",
                  shown != null && popupHud.AtomQueued == 0 && shown.text.Contains("ARC SHOT -> ") && shown.text.Contains("DRONE") && shown.text.Contains("LV 1") && shown.text.Contains("0 → 1 CHAIN JUMPS"));
            popupHud.StepPopups(ShotAtoms.UpgradeSeconds);
        }
        ShotAtoms.SetLevel(ShotAtom.Arc, ShotAtomTuning.MaxLevel);
        var again = ShotAtomPickup.Build(ShotAtom.Arc, Vector3.zero);
        rig.trigger(again.GetComponent<Collider2D>());
        if (PickupBurst.LastPlayed != null) PickupBurst.LastPlayed.Finish();
        {
            var shown = popupHud != null ? popupHud.AtomUpgradeText : null;
            Check("at the cap the popup says 'ARC MAXED  +2 DUST'", shown != null && shown.text.Contains("ARC MAXED  +2 DUST") && shown.text.Contains("maxed on every drone"));
            popupHud.StepPopups(ShotAtoms.UpgradeSeconds * .5f);
            bool half = popupHud.AtomUpgradeShowing;
            popupHud.StepPopups(ShotAtoms.UpgradeSeconds);
            Check("the popup holds 1.5 to 2 s, then it is gone and back in the pool (no object made or destroyed)",
                  ShotAtoms.UpgradeSeconds >= 1.5f && ShotAtoms.UpgradeSeconds <= 2f && half && !popupHud.AtomUpgradeShowing && popupHud.PopupPoolSize == pool0);
            bool texts = true;
            foreach (ShotAtom a in System.Enum.GetValues(typeof(ShotAtom)))
                for (int lv = 1; lv <= ShotAtomTuning.MaxLevel; lv++)
                    texts &= ShotAtoms.UpgradeLevel(lv) == "LV " + lv && ShotAtoms.UpgradeHint(a, lv).Length > 4 &&
                             ShotAtoms.UpgradeTitle(a, 0, "LEFT DRONE") == (a == ShotAtom.Rapid ? "RAPID FIRE  ALL DRONES" : ShotAtoms.Name(a) + " SHOT -> LEFT DRONE");
            Check("every atom and level has a title and a hint; the twin hints read '2 shots at once' / '3 shots in a fan'",
                  texts && ShotAtoms.UpgradeTitle(ShotAtom.Twin, 0, "LEFT DRONE") == "TWIN SHOT -> LEFT DRONE" && ShotAtoms.UpgradeHint(ShotAtom.Twin, 1) == "2 shots at once" &&
                  ShotAtoms.UpgradeHint(ShotAtom.Twin, 2) == "3 shots in a fan" && ShotAtoms.MaxedTitle(ShotAtom.Twin) == "TWIN MAXED  +2 DUST");
            Check("the popup reads 'LV 10' at the top, with MAXED on the hint line (two digits, the title stays short)",
                  ShotAtoms.UpgradeLevel(10) == "LV 10" && ShotAtoms.UpgradeTitle(ShotAtom.Ricochet, 0, "RIGHT LOW DRONE") == "RICOCHET SHOT -> RIGHT LOW DRONE" &&
                  ShotAtoms.UpgradeHint(ShotAtom.Twin, 10) == "MAXED  7 shots in a fan" && ShotAtoms.UpgradeHint(ShotAtom.Rapid, 10) == "MAXED  +250% fire rate" &&
                  !ShotAtoms.UpgradeHint(ShotAtom.Twin, 9).StartsWith("MAXED") && ShotAtoms.UpgradeLevel(1) == "LV 1");
        }
        Check("an ARC past level 10 pays dust (\"" + rig.cd.hypeText.text + "\") and the level stays 10",
              ShotAtoms.Level(ShotAtom.Arc) == ShotAtomTuning.MaxLevel && rig.cd.hypeText.text == ShotAtoms.MaxedWord && ShotAtoms.MaxedCatches == 1);
        Check("a pickup collected once is spent (its collider is off or it is gone)", coinCollider == null || !coinCollider.enabled);
        foreach (var go in new[] { coin, again, third }) if (go != null) Object.DestroyImmediate(go);
        Object.DestroyImmediate(canvasGo);
        Object.DestroyImmediate(rig.ship);
        Object.DestroyImmediate(rig.cd.explosionAnimation);
        collisionDetection.atomCheck = false;
        ShotAtoms.BeginRun(3);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, 0);
    }

    // ---- the HUD row ---------------------------------------------------------------------

    static Rect AuthoredHud(TopBand.Frame band, Vector2 screen)
    {
        float hudScale = HudStyler.ScaleWithScreenSize(screen, new Vector2(800f, 600f), CanvasScaler.ScreenMatchMode.MatchWidthOrHeight, 0f);
        return HudStyler.HudScreenRect(band, screen, hudScale, new Vector2(351f, 131f));
    }

    static void HudLayout()
    {
        bool fit = true, clear = true, apart = true, under = true, high = true, whole = true, inBoard = true, snug = true, railsClear = true;
        string firstBad = null;
        int checkedRects = 0;
        foreach (var d in FitDevice.All)
        {
            var screen = new Vector2(d.w, d.h);
            Rect safe = d.Safe;
            foreach (float rail in new[] { 0f, 2.2f })
            {
                var band = TopBand.FrameFor(safe, screen, rail, d.Cutouts);
                Rect hud = AuthoredHud(band, screen);
                for (int world = 0; world < HudSkin.WorldCount; world++)
                foreach (int rows in new[] { 1, 3, 6 })
                {
                var geo = ShotAtomHud.Compute(band, safe, screen, hud, world, rows);
                var basic = ShotAtomHud.Obstacles(band, safe, screen, hud);
                var obstacles = new Rect[basic.Length + BoosterHud.MaxSlots + 1];
                basic.CopyTo(obstacles, 0);
                for (int b = 0; b < BoosterHud.MaxSlots; b++) obstacles[basic.Length + b] = BoosterHud.SlotRect(band, safe, screen, hud, b);
                obstacles[obstacles.Length - 1] = hud;
                var rects = new Rect[rows * ShotAtomHud.Columns];
                float gapPx = Mathf.Max(1f, Mathf.Round(ShotAtomHud.GapUnits * PauseQuickActions.CanvasScaleFor(screen)));
                float frameBottom = hud.yMin - HudStyler.BoardVisibleBottomPx(hud, world);
                for (int i = 0; i < rects.Length; i++)
                {
                    Rect r = rects[i] = ShotAtomHud.SlotRect(geo, i);
                    Rect icon = ShotAtomHud.IconRect(geo, r);
                    bool inside = r.xMin >= safe.xMin - .5f && r.xMax <= safe.xMax + .5f && r.yMin >= safe.yMin && r.yMax <= safe.yMax && r.xMin >= band.left - .5f && r.xMax <= band.right + .5f;
                    bool clash = false;
                    foreach (var o in obstacles) if (o.width > 0f && o.height > 0f && r.Overlaps(o)) clash = true;
                    bool below = hud.height <= 0f || r.yMax <= hud.yMin + .5f;
                    bool low = r.yMin >= screen.y * BoosterHud.LowestShare - .5f;
                    for (int j = 0; j < i; j++) apart &= !r.Overlaps(rects[j]);
                    fit &= inside;
                    clear &= !clash;
                    under &= below;
                    high &= low;
                    whole &= icon.width == icon.height && icon.x == Mathf.Round(icon.x) && icon.y == Mathf.Round(icon.y) && icon.xMin >= r.xMin - .5f && icon.xMax <= r.xMax + .5f && icon.yMin >= r.yMin - .5f;
                    checkedRects++;
                    inBoard &= hud.height <= 0f || (r.xMin >= hud.xMin - .5f && r.xMax <= hud.xMax + .5f);
                    railsClear &= r.xMin >= band.left - .5f && r.xMax <= band.right + .5f;
                    // with nothing else under the board the row sits right under the board's bottom frame (a gap, no more)
                    if (i == 0) snug &= hud.height <= 0f || Mathf.Abs(frameBottom - ShotAtomHud.VisibleIconTop(geo) - geo.originalVisibleGap * .5f) <= 2f;
                    if ((!inside || clash || !below || !low) && firstBad == null) firstBad = d.id + " rail " + rail + " world " + world + " rows " + rows + " slot " + i + " " + r + " hud " + hud;
                }
                }
            }
        }
        string bad = firstBad != null ? " (first bad: " + firstBad + ")" : "";
        Check("the shot-atom block (one row per drone: 1, 3 and 6) sits inside the safe area and the top band on every ScreenFit device, with and without rails (" + checkedRects + " slots)" + bad, fit);
        Check("... clear of the score board, the Replay/Home pair, the boss chip and banner, PORTAL DANGER's chip and the booster column" + bad, clear);
        Check("... under the score board" + bad, under);
        Check("... horizontally inside the score board's width (so never beside the rails)" + bad, inBoard && railsClear);
        Check("... the visible coin art is halfway closer to the board (+-2 px) for every world's frame on every ScreenFit device" + bad, snug);
        Check("... never in the bottom " + BoosterHud.LowestShare.ToString("0%") + " of the screen" + bad, high);
        Check("... with 1, 3 and 6 drones (one row each) no slot overlaps another, and all stay in the safe area" + bad, apart);
        Check("... coins are whole-pixel squares inside their slots", whole);
        string src = File.ReadAllText("Assets/Scripts/UI/ShotAtomHud.cs");
        Check("the row never moves for the boss banner / chip or PORTAL DANGER's chip (it reads neither) and is drawn above both canvases",
              !src.Contains("BossWarningHud.") && !src.Contains("PortalPressureHud.") && ShotAtomHud.SortOrder > BossWarningHud.SortingOrder && ShotAtomHud.SortOrder > PortalPressureHud.SortingOrder);
        BoardFrameArt();
        Check("the row is not interactive (no Button, no GraphicRaycaster, nothing blocks a press) and runs on the run scene only",
              !src.Contains("typeof(Button)") && !src.Contains("typeof(GraphicRaycaster)") && src.Contains("slot.icon.raycastTarget = false") &&
              src.Contains("group.blocksRaycasts = false") && src.Contains("scene.name != \"gameS1\""));
        Check("the row reads the shot atoms only (never the boosters' or the timed atoms' state)", !src.Contains("Boosters.") && !src.Contains("MagnetPull") && !src.Contains("atomCheck"));
        Check("the labels read x1 / x2 / x3 ... x10", ShotAtomHud.LevelLabel(1) == "x1" && ShotAtomHud.LevelLabel(2) == "x2" && ShotAtomHud.LevelLabel(3) == "x3" && ShotAtomHud.LevelLabel(10) == "x10" && ShotAtomHud.LevelLabel(11) == "x10");
        bool labelFits = true;
        foreach (var d in FitDevice.All)
        {
            var screen = new Vector2(d.w, d.h);
            foreach (float rail in new[] { 0f, 2.2f })
            {
                var band = TopBand.FrameFor(d.Safe, screen, rail, d.Cutouts);
                var g = ShotAtomHud.Compute(band, d.Safe, screen, AuthoredHud(band, screen));
                int baseSize = Mathf.Max(9, Mathf.RoundToInt(ShotAtomHud.LabelUnits * PauseQuickActions.CanvasScaleFor(screen)));
                int size = ShotAtomHud.LabelFontSize(baseSize, g.slotWidth, 3);
                labelFits &= size >= 6 && size <= baseSize && (size == 6 || size * 3 * ShotAtomHud.LabelCharWidth <= g.slotWidth + .01f);
            }
        }
        Check("... the two-digit label 'x10' fits its slot on every ScreenFit device (the font shrinks on a narrow board, never under 6)", labelFits);
    }

    // The visible bottom comes from the art: the table of transparent bottom rows matches every world's hud_board.png, and
    // the anchor follows the frame when the world changes.
    static void BoardFrameArt()
    {
        bool table = true; string bad = null;
        for (int w = 0; w < HudSkin.WorldCount; w++)
        {
            string path = "Assets/Resources/" + HudSkin.FramePath(w) + ".png";
            if (!File.Exists(path)) path = "Assets/Art/Resources/" + HudSkin.FramePath(w) + ".png";
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(path));
            var px = tex.GetPixels32();
            int clear = 0;
            for (int y = 0; y < tex.height; y++)
            {
                bool any = false;
                for (int x = 0; x < tex.width && !any; x++) any = px[y * tex.width + x].a >= 32;
                if (any) break;
                clear++;
            }
            Object.DestroyImmediate(tex);
            float expected = clear * HudSkin.BoardCanvasRefPpu / HudSkin.BoardSpritePpu;
            if (Mathf.Abs(expected - HudSkin.BoardVisibleBottomClear(w)) > .01f) { table = false; if (bad == null) bad = "world " + w + " art " + expected + " table " + HudSkin.BoardVisibleBottomClear(w); }
        }
        Check("the table of transparent bottom rows matches every world's hud_board.png" + (bad != null ? " (" + bad + ")" : ""), table);

        var screen = new Vector2(1080f, 2340f);
        var safe = new Rect(0f, 0f, screen.x, screen.y);
        var band = TopBand.FrameFor(safe, screen, 0f, new Rect[0]);
        Rect hud = AuthoredHud(band, screen);
        float frost = ShotAtomHud.Compute(band, safe, screen, hud, 1).top, space = ShotAtomHud.Compute(band, safe, screen, hud, 0).top;
        Check("the row re-anchors to a different frame (Frost " + frost + " vs Space " + space + "): the visible bottoms differ, so do the rows", !Mathf.Approximately(frost, space));
    }

    static void HudLive()
    {
        ShotAtoms.BeginRun(3); ShotFeelTest.ArmSquad(); DroneSquad.Select(0);
        ShotAtoms.SetLevel(0, ShotAtom.Arc, 1); ShotAtoms.SetLevel(0, ShotAtom.Twin, 2);
        ShotAtoms.SetLevel(1, ShotAtom.Pierce, 4); ShotAtoms.SetLevel(ShotAtom.Rapid, 1);
        var host = new GameObject(ShotAtomHud.HostName);
        var hud = host.AddComponent<ShotAtomHud>(); hud.SendMessage("OnEnable"); hud.SendMessage("Update");
        int twin = ShotAtomHud.SelectedAtomSlot(ShotAtom.Twin), arc = ShotAtomHud.SelectedAtomSlot(ShotAtom.Arc),
            pierce = ShotAtomHud.SelectedAtomSlot(ShotAtom.Pierce), rapid = ShotAtomHud.SelectedAtomSlot(ShotAtom.Rapid);
        Check("compact HUD shows the selected drone's modifiers and all three mode pips", hud.VisibleCount == 3 && hud.LabelAt(twin) == "x2" && hud.LabelAt(arc) == "x1" && hud.LabelAt(rapid) == "x1" && hud.ActiveAt(ShotAtomHud.PipSlot(0)) && hud.ActiveAt(ShotAtomHud.PipSlot(2)));
        Check("modifier icons retain their idle art and the selected mode is shown", hud.IconAt(twin).name.StartsWith("shot_twin_idle") && hud.IconAt(0).name.StartsWith("DroneMode"));
        var next = ShotAtoms.Collect(ShotAtom.Twin, 0f); hud.SendMessage("Update");
        Check("a pickup switches the detail row to its predictable recipient", next.drone == 1 && DroneSquad.Selected == 1 && hud.LabelAt(twin) == "x1" && hud.LabelAt(pierce) == "x4" && hud.VisibleCount == 3);
        var split = ShotAtoms.Collect(ShotAtom.Split, .99f); hud.SendMessage("Update");
        Check("switching recipients keeps fixed coin columns and shows new levels", split.drone == 0 && hud.LabelAt(ShotAtomHud.SelectedAtomSlot(ShotAtom.Split)) == "x1" && hud.LabelAt(twin) == "x2" && hud.VisibleCount == 4);
        Check("compact row has four modifiers, one selected glyph, three mode pips", host.GetComponentsInChildren<Image>(false).Length == 8);
        Check("read-only drone HUD never intercepts flight input", host.GetComponentsInChildren<GraphicRaycaster>(true).Length == 0 && host.GetComponentsInChildren<Button>(true).Length == 0);
        ShotAtoms.BeginRun(3); hud.SendMessage("Update");
        Check("new run clears modifiers while showing its dormant slots", hud.VisibleCount == 0 && hud.ActiveAt(ShotAtomHud.PipSlot(0)) && hud.ActiveAt(ShotAtomHud.PipSlot(2)));
        Object.DestroyImmediate(host);
    }

    static void Spin()
    {
        FreshScene();
        var coin = ShotAtomPickup.Build(ShotAtom.Twin, Vector3.zero);
        var book = coin.GetComponent<PickupFlipbook>();
        var sr = coin.GetComponent<SpriteRenderer>();
        Quaternion rot0 = coin.transform.rotation;
        var seen = new HashSet<int>();
        var sprites = new HashSet<Sprite>();
        int first = book.Frame;
        int advanced = 0, last = first;
        for (int i = 0; i < 40; i++)
        {
            book.Advance(PickupArt.ShotFrameSeconds * 1.01f);
            seen.Add(book.Frame);
            sprites.Add(sr.sprite);
            if (book.Frame != last) advanced++;
            last = book.Frame;
        }
        Check("the coin's border index advances over time and visits every one of the 14 drawings (" + seen.Count + " seen, " + advanced + " steps in 40)", advanced >= 39 && seen.Count == 14 && sprites.Count == 14);
        float turn = PickupArt.ShotFrameSeconds * 14f;
        Check("... one full turn of the ring (14 drawings) takes " + turn.ToString("0.0") + " s: clearly visible, between 1.5 and 2 s", turn >= 1.5f && turn <= 2f);
        Check("... the symbol stays upright and still: nothing on the coin rotates (the drawings carry the still symbol; AtomSpin is at 0 deg/s)",
              coin.transform.rotation == rot0 && Mathf.Approximately(coin.GetComponent<AtomSpin>().degreesPerSecond, 0f) && coin.transform.childCount >= 0);
        coin.GetComponent<AtomSpin>().SendMessage("Update");
        Check("... still not turned after an AtomSpin frame", coin.transform.rotation == rot0);
        Object.DestroyImmediate(coin);

        // the HUD row's coins spin too
        ShotAtoms.BeginRun(3);
        ShotAtoms.Collect(ShotAtom.Rapid);
        var host = new GameObject(ShotAtomHud.HostName);
        var hud = host.AddComponent<ShotAtomHud>();
        hud.SendMessage("Update");
        int f0 = hud.FrameIndex;
        int rapidSlot = ShotAtomHud.RapidColumn;   // row 0's last column: the shared RAPID coin (column 0 is the drone glyph)
        Sprite s0 = hud.IconAt(rapidSlot);
        hud.Advance(PickupArt.ShotFrameSeconds * 1.01f);
        hud.SendMessage("Update");
        bool moved = hud.FrameIndex != f0 && hud.IconAt(rapidSlot) != s0;
        var icon = host.GetComponentInChildren<Image>(false);
        Check("the HUD coin's border advances over time (frame " + f0 + " -> " + hud.FrameIndex + ") while its icon is never rotated", moved && icon != null && icon.rectTransform.localRotation == Quaternion.identity);
        Object.DestroyImmediate(host);
        ShotAtoms.BeginRun(3);

        // the Codex entry loops the same drawings at the same pace
        var anim = CodexAnimations.For(Codex.Find("atom_shot_arc"));
        Check("the Codex entries loop the 14 drawings at the coin's pace (" + (anim != null ? anim.idle.Length : 0) + " frames)",
              anim != null && anim.idle.Length == 14 && anim.idleHold != null && anim.idleHold.Length == 14 && Mathf.Abs(anim.idleHold[0] - PickupArt.ShotFrameSeconds) < 1e-5f);
    }

    // ---- the Codex ----------------------------------------------------------------------

    static void CodexEntries()
    {
        PlayerPrefs.DeleteKey(Codex.PrefsKey);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        Codex.Reload();
        var wanted = new[]
        {
            (ShotAtom.Twin, "Twin Atom", ""), (ShotAtom.Pierce, "Pierce Atom", ""), (ShotAtom.Split, "Split Atom", ""),
            (ShotAtom.Ricochet, "Ricochet Atom", ""), (ShotAtom.Arc, "Arc Atom", ""), (ShotAtom.Rapid, "Rapid Atom", ""),
        };
        bool all = true, lore = true, art = true, locked = true, bulleted = true, match = true;
        foreach (var (atom, name, world) in wanted)
        {
            var e = Codex.Find(CodexCatalogue.ShotAtomId(atom));
            all &= e != null && e.name == name && e.category == CodexCategory.Atoms;
            if (e == null) continue;
            lore &= e.lore.Contains("DROPPED BY  every elite") && !e.lore.Contains("UNLOCKS") && e.lore.Contains("LEVEL 1  ") && e.lore.Contains("LEVEL 10  ") &&
                    e.lore.Contains("MAX LEVEL  10");
            art &= e.Sprite != null && e.Sprite.name.StartsWith("shot_" + ShotAtoms.Key(atom) + "_idle");
            PickupKind kind;
            art &= CodexAnimations.TryPickupKind(e.id, out kind) && kind == ShotAtoms.KindOf(atom) && CodexAnimations.For(e) != null;
            bulleted &= CodexCatalogue.IsBulleted(e);
            var coin = ShotAtomPickup.Build(atom, Vector3.zero);
            match &= Codex.IdFor(coin) == e.id;
            Object.DestroyImmediate(coin);
            locked &= !Codex.IsDiscovered(e.id);
        }
        Check("the Codex lists the six shot atoms with their names in the Atoms tab", all);
        Check("... each with its level effects, 'dropped by every elite and boss', no world unlock line, and the level cap", lore);
        Check("... each with its coin as the icon and its idle flipbook", art);
        Check("... the lore is a bullet list (one line each)", bulleted);
        Check("... catching a coin (its name) resolves to its own entry, so the first catch discovers it like every atom", match);
        Check("... and a fresh profile keeps every coin locked until discovery", locked);
        var twin = Codex.Find("atom_shot_twin");
        Check("the TWIN lore quotes the tuning range (2 parallel shots at level 1, 7 in a fan at level 10) and the max level", twin != null && twin.lore.Contains("2 parallel shots") && twin.lore.Contains("7 shots in a narrow fan") && twin.lore.Contains("MAX LEVEL  10"));
        Check("the old atoms are untouched (blue, red, green, violet, magnetic still there)", Codex.Find("atom_blue") != null && Codex.Find("atom_red") != null && Codex.Find("atom_green") != null &&
              Codex.Find(CodexCatalogue.VioletAtomId) != null && Codex.Find(CodexCatalogue.MagnetAtomId) != null);
        Check("every shot atom kind has its art (14 idle frames, 6 burst frames)", AllFrames());
    }

    static bool AllFrames()
    {
        for (int i = 0; i < ShotAtomTuning.Count; i++)
        {
            var kind = ShotAtoms.KindOf((ShotAtom)i);
            var idle = PickupArt.Frames(PickupArt.IdleName(kind), PickupArt.IdleTicks(kind).Length);
            var burst = PickupArt.Frames(PickupArt.BurstName(kind), PickupArt.BurstTicks.Length);
            if (idle.Length != 14 || burst.Length != 6) return false;
            foreach (var f in idle) if (f == null || f.texture.width != 196 || f.texture.filterMode != FilterMode.Point) return false;
            foreach (var f in burst) if (f == null || f.texture.width != 388) return false;
        }
        return true;
    }

    // ---- reset ----------------------------------------------------------------------------

    static void Reset()
    {
        ShotAtoms.BeginRun(3);
        for (int i = 0; i < ShotAtomTuning.Count; i++) ShotAtoms.SetLevel((ShotAtom)i, 2);
        ShotAtomDrops.Spawned = null;
        AtomSpacing.Released();
        ShotAtomDrops.OnBossDestroyed(Vector3.zero, 0f, 0f, .999f);
        int version = ShotAtoms.Version;
        ShotAtoms.BeginRun(3);
        bool zero = true;
        for (int i = 0; i < ShotAtomTuning.Count; i++) zero &= ShotAtoms.Level((ShotAtom)i) == 0;
        Check("a new run wipes every level, the HUD order, the catch counters and the waiting drops",
              zero && ShotAtoms.OrderCount == 0 && ShotAtoms.Catches == 0 && ShotAtoms.MaxedCatches == 0 && ShotAtomDrops.Pending == 0 && ShotAtoms.Version != version);
        Check("... and with nothing caught the gun is the plain gun (1 bullet, no rapid)", ShotAtoms.RapidBonus() == 0f && ShotAtomTuning.TwinShots[ShotAtoms.Level(ShotAtom.Twin)] == 1);
        string score = File.ReadAllText("Assets/Scripts/Core/score.cs");
        Check("score.Awake starts every run with ShotAtoms.BeginRun() (the shot atoms are per run, nothing is saved)", score.Contains("ShotAtoms.BeginRun()"));
        string save = File.ReadAllText("Assets/Scripts/Gameplay/Pickups/ShotAtoms.cs");
        Check("the shot atoms never touch PlayerPrefs (state of the run only)", !save.Contains("PlayerPrefs"));
        ClearCoins();
    }
}
