using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Reflection;

// Covers the 2026-09-07 difficulty rebalance: rail bombs (mine.prefab) back
// in rotation with a side-matching fix for their rail mounting, enemy
// density (enmiesOnBoard's SpawnPhase) now driven by elapsed flight time
// instead of moveBackGround.speed so it keeps escalating past the speed
// cap, per-world maxSpeed lowered ~20% with a new per-world enemyRampScale
// wired through WorldManager.ApplyDifficulty, and the new ChaserEnemy hazard.
public static class DifficultyRebalanceTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[DR] PASS  " : "[DR] FAIL  ") + what);
        if (!ok) fails++;
    }

    static FieldInfo Priv(System.Type t, string name) =>
        t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
    static MethodInfo PrivM(System.Type t, string name) =>
        t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        RailMinesRemainMounted();
        OpeningCalmWindowRespectsShipStartSpeed();
        PhaseProgressionIsTimeDrivenNotSpeedDriven();
        WorldSpeedCapsLoweredAndRampScaleWired();
        ChaserHomesThenWanders();
        ObsoleteRailAndAsteroidArtIsRemoved();
        EnemyDensityRampsEveryTenSeconds();

        Debug.Log("[DR] failures: " + fails);
        return fails;
    }

    static enmiesOnBoard NewBoard(string name)
    {
        var go = new GameObject(name);
        var comp = go.AddComponent<enmiesOnBoard>();
        comp.SendMessage("Start");
        return comp;
    }

    static void RailMinesRemainMounted()
    {
        EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
        var comp = NewBoard("~EnmiesOnBoardTest1");
        PrivM(typeof(enmiesOnBoard), "spawnMine").Invoke(comp, null);
        var liveMines = Priv(typeof(enmiesOnBoard), "liveMines").GetValue(comp) as System.Collections.IList;
        var mine = liveMines != null && liveMines.Count > 0 ? liveMines[0] as Transform : null;
        var mount = mine != null ? mine.GetComponent<RailMineMount>() : null;
        Check("the rail mine keeps its RailMineMount", mount != null);
        Check("the rail mine references a live moving rail", mount != null && mount.rail != null);
        Check("the rail mine is aligned to its assigned rail", mount != null && mount.IsOnRail());
        var rail = mount != null ? mount.rail : null;
        Object.DestroyImmediate(comp.gameObject);
        if (mine != null) Object.DestroyImmediate(mine.gameObject);
        if (rail != null) Object.DestroyImmediate(rail.gameObject);
    }

    static void ObsoleteRailAndAsteroidArtIsRemoved()
    {
        Check("obsolete rail3 prefab is removed", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Prefabs/rail3.prefab") == null);
        // Every world's mine, Ember's included, plays its row of the original
        // neon atlas, restored as Enemies/Mines/rail_mines_neon.png; the old
        // Vfx copies and the two Ember beat frames stay retired.
        foreach (string file in new[] { "rail_mine_ember_1", "rail_mine_ember_2", "rail_bomb_themes_atlas" })
            Check(file + " is retired",
                  AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Resources/Vfx/" + file + ".png") == null);
        Check("the Ember mine's neon atlas row is present",
              AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Resources/" + RailMineArt.AtlasPath + ".png") != null &&
              RailMineArt.Frame(3, RailMineArt.Dormant) != null);
        // The whole old asteroid folder went with the per-world rocks.
        Check("the old Art/Aestroids folder is gone", !AssetDatabase.IsValidFolder("Assets/Art/Aestroids"));
    }

    static void OpeningCalmWindowRespectsShipStartSpeed()
    {
        Check("SPEED 10 is the fast-arrival threshold", enmiesOnBoard.FastArrivalHudSpeed == 10);
        Check("stock ships receive a readable calm fly-in", enmiesOnBoard.CalmArrivalSeconds > 0f);
    }

    static void PhaseProgressionIsTimeDrivenNotSpeedDriven()
    {
        EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
        var comp = NewBoard("~EnmiesOnBoardTest3");

        var elapsedField = Priv(typeof(enmiesOnBoard), "elapsedFlightSeconds");
        var phaseField = Priv(typeof(enmiesOnBoard), "phase");
        var selectPhase = PrivM(typeof(enmiesOnBoard), "SelectPhase");

        string PhaseName()
        {
            var p = phaseField.GetValue(comp);
            return (string)p.GetType().GetField("name").GetValue(p);
        }

        // High speed, zero elapsed time -- the old speed-keyed phases would
        // have jumped straight to the hardest tier here; time-keyed phases
        // must not.
        moveBackGround.speed = 10f;
        elapsedField.SetValue(comp, 0f);
        selectPhase.Invoke(comp, null);
        Check("high speed with zero elapsed time still starts at Warm-up", PhaseName() == "Warm-up");

        // Zero speed (as if the cap had long since flattened it out), lots
        // of elapsed time -- phases must still have advanced regardless.
        moveBackGround.speed = 0f;
        elapsedField.SetValue(comp, 250f);
        selectPhase.Invoke(comp, null);
        Check("zero speed with lots of elapsed time still reaches Chaos (density keeps ramping past the speed cap)",
              PhaseName() == "Chaos");

        // Chaos comes before a 120s level's end (WorldManager.BaselineWorldSeconds),
        // not at the old 130s that a stock start never reached.
        elapsedField.SetValue(comp, enmiesOnBoard.ChaosStartSeconds - .5f);
        selectPhase.Invoke(comp, null);
        bool swarm = PhaseName() == "Swarm";
        elapsedField.SetValue(comp, enmiesOnBoard.ChaosStartSeconds);
        selectPhase.Invoke(comp, null);
        Check("Chaos starts at " + enmiesOnBoard.ChaosStartSeconds + "s, inside the 120s baseline level",
              swarm && PhaseName() == "Chaos" &&
              enmiesOnBoard.ChaosStartSeconds <= WorldManager.BaselineWorldSeconds * .9f);

        Object.DestroyImmediate(comp.gameObject);
    }

    static void WorldSpeedCapsLoweredAndRampScaleWired()
    {
        // 2026-10: lowered again (0.46 -> 0.38, 0.62 -> 0.44): "too hard past speed 35"
        Check("Space's maxSpeed was lowered from the old 0.58, and again from 0.46",
              WorldManager.Worlds[0].maxSpeed < 0.46f && WorldManager.Worlds[0].maxSpeed > 0.35f);
        Check("Ember's maxSpeed was lowered from the old 0.78, and again from 0.62",
              WorldManager.Worlds[3].maxSpeed < 0.50f && WorldManager.Worlds[3].maxSpeed > 0.40f);
        Check("worlds stay ordered least to most top speed",
              WorldManager.Worlds[0].maxSpeed < WorldManager.Worlds[1].maxSpeed &&
              WorldManager.Worlds[1].maxSpeed < WorldManager.Worlds[2].maxSpeed &&
              WorldManager.Worlds[2].maxSpeed < WorldManager.Worlds[3].maxSpeed);
        Check("later worlds ramp enemy density faster than earlier ones",
              WorldManager.Worlds[0].enemyRampScale < WorldManager.Worlds[1].enemyRampScale &&
              WorldManager.Worlds[1].enemyRampScale < WorldManager.Worlds[2].enemyRampScale &&
              WorldManager.Worlds[2].enemyRampScale < WorldManager.Worlds[3].enemyRampScale);

        EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
        var bgGo = new GameObject("~MoveBackgroundTest");
        var bg = bgGo.AddComponent<moveBackGround>();
        var enemies = NewBoard("~EnmiesOnBoardTest4");

        var applyDifficulty = typeof(WorldManager).GetMethod("ApplyDifficulty", BindingFlags.NonPublic | BindingFlags.Static);
        applyDifficulty.Invoke(null, new object[] { WorldManager.Worlds[3] }); // Ember

        Check("ApplyDifficulty sets moveBackGround.maxSpeed from the theme",
              Mathf.Approximately(bg.maxSpeed, WorldManager.Worlds[3].maxSpeed));
        Check("ApplyDifficulty sets enmiesOnBoard.phaseRampScale from the theme",
              Mathf.Approximately(enemies.phaseRampScale, WorldManager.Worlds[3].enemyRampScale));

        Object.DestroyImmediate(bgGo);
        Object.DestroyImmediate(enemies.gameObject);
    }

    static void ChaserHomesThenWanders()
    {
        EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0; // flying without needing touch input in batch mode

        var playerGo = new GameObject("~ChaserTestPlayer");
        playerGo.AddComponent<movePlayer>();
        playerGo.transform.position = new Vector3(2f, 0f, 0f);

        var chaserGo = new GameObject("~ChaserTest");
        chaserGo.tag = "Enimey"; // matches the real spawn path's borrowed hull
        chaserGo.transform.position = new Vector3(0f, -5f, 0f);
        var chaser = chaserGo.AddComponent<ChaserEnemy>();
        chaser.chaseSeconds = 3f;
        chaser.SendMessage("Start");

        Check("chaser carries the Enimey tag (a valid ultimate target, and " +
              "collides with the player like any other hazard)",
              chaserGo.CompareTag("Enimey"));

        var wanderingField = Priv(typeof(ChaserEnemy), "wandering");
        var chaseTimerField = Priv(typeof(ChaserEnemy), "chaseTimer");
        Check("starts in the chase state, not already wandering", !(bool)wanderingField.GetValue(chaser));

        // Time.deltaTime is 0 outside Play mode (confirmed elsewhere this
        // session), so the chase-vs-wander transition can't be driven
        // through repeated Update() calls -- force the timer past zero
        // directly and confirm Update() reacts to that, exactly as it would
        // after enough real frames elapsed. (It steers in LateUpdate, after
        // the board-locked movers, so SpawnSpace can resolve its move.)
        chaseTimerField.SetValue(chaser, 0f);
        chaserGo.SendMessage("LateUpdate");
        Check("switches to wandering once the chase timer runs out",
              (bool)wanderingField.GetValue(chaser));

        Object.DestroyImmediate(playerGo);
        Object.DestroyImmediate(chaserGo);
        buttonClicks.playerDied = false;
    }

    // Reported 2026-09-07: on Space specifically, enemy spawns felt way too
    // sparse as the run sped up -- Space has the lowest enemyRampScale (the
    // baseline every other world ramps faster than) and never actually
    // reaches its own speed cap within a level, so speed climbed the
    // whole time while density barely moved. Follow-up spec: density should
    // step up every 10s of active flight -- reaching 2x by the one-minute
    // mark, continuing to climb toward the end, and hitting a flat 3x for
    // the last 30 seconds of any level, independent of world.
    static void EnemyDensityRampsEveryTenSeconds()
    {
        EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
        var comp = NewBoard("~EnmiesOnBoardDensityTest");

        var elapsedField = Priv(typeof(enmiesOnBoard), "elapsedFlightSeconds");
        var densityMethod = PrivM(typeof(enmiesOnBoard), "DensityMultiplier");
        var rollMethod = PrivM(typeof(enmiesOnBoard), "Roll");

        float DensityAt(float t)
        {
            elapsedField.SetValue(comp, t);
            return (float)densityMethod.Invoke(comp, null);
        }

        Check("density starts at 1x (no ramp yet)", Mathf.Approximately(DensityAt(0f), 1f));

        float afterOneTick = DensityAt(15f); // one 10s step in
        Check("density has climbed after the first 10s tick", afterOneTick > 1f && afterOneTick < 2f);

        float afterAnotherTick = DensityAt(25f);
        Check("density keeps climbing tick over tick", afterAnotherTick > afterOneTick);

        Check("density reaches 2x right at the one-minute mark",
              Mathf.Approximately(DensityAt(60f), 2f));

        float midLevel = DensityAt(75f); // inside a 120s level, before the final stretch
        Check("density keeps climbing past the one-minute mark, toward the mid ceiling",
              midLevel > 2f && midLevel <= 2.5f);

        // No live WorldManager.Instance in this test, so DensityMultiplier()
        // goes by elapsed time against WorldManager.BaselineWorldSeconds
        // (120s; cut from 180s, from 300s before that) -- the last 30s of
        // that window is t >= 90.
        Check("final 30 seconds of the level (120s baseline) hits a flat 3x",
              Mathf.Approximately(DensityAt(100f), 3f));
        Check("one moment before the final stretch is still below 3x",
              DensityAt(89f) < 3f);

        // Roll() divides by the multiplier -- higher density means shorter
        // delays, i.e. more spawns per minute.
        var range = new Vector2(10f, 10f); // fixed, so any spread is purely from the multiplier
        elapsedField.SetValue(comp, 0f);
        float delayAtStart = (float)rollMethod.Invoke(comp, new object[] { range });
        elapsedField.SetValue(comp, 100f);
        float delayAtEnd = (float)rollMethod.Invoke(comp, new object[] { range });
        Check("Roll() actually shortens delays as density climbs (3x density -> 1/3 the delay)",
              Mathf.Approximately(delayAtEnd, delayAtStart / 3f));

        Object.DestroyImmediate(comp.gameObject);
    }
}
