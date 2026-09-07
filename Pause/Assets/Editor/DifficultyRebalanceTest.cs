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
        MinesAlwaysMountToMatchingSideRail();
        MineFieldLoadsAndSpawnsOntoARail();
        PhaseProgressionIsTimeDrivenNotSpeedDriven();
        WorldSpeedCapsLoweredAndRampScaleWired();
        ChaserHomesThenWanders();
        ObsoleteRailAndAsteroidArtIsRemoved();
        EnemyDensityRampsEveryTenSeconds();

        Debug.Log("[DR] failures: " + fails);
        EditorApplication.Exit(0);
    }

    static enmiesOnBoard NewBoard(string name)
    {
        var go = new GameObject(name);
        var comp = go.AddComponent<enmiesOnBoard>();
        comp.SendMessage("Start");
        return comp;
    }

    static void MinesAlwaysMountToMatchingSideRail()
    {
        EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
        var comp = NewBoard("~EnmiesOnBoardTest1");

        var spawnRail = PrivM(typeof(enmiesOnBoard), "SpawnRail");
        var nearestLiveRail = PrivM(typeof(enmiesOnBoard), "NearestLiveRail");

        var rightRail = (Transform)spawnRail.Invoke(comp, new object[] { true });
        var leftRail = (Transform)spawnRail.Invoke(comp, new object[] { false });
        Check("SpawnRail(true) lands on the positive-x side", rightRail.position.x > 0f);
        Check("SpawnRail(false) lands on the negative-x side", leftRail.position.x < 0f);
        Check("right rail is inside the visible play lane", rightRail.position.x <= 2.35f);
        Check("left rail is inside the visible play lane", leftRail.position.x >= -2.35f);

        // Move the right rail far away in Y and leave the left one close --
        // a search that ignored side would now prefer the (far) right rail
        // for neither request, or worse, hand a left-side request the
        // nearby right rail. Both must still resolve strictly by side.
        rightRail.position += new Vector3(0f, 50f, 0f);

        var pickedForLeft = (Transform)nearestLiveRail.Invoke(comp, new object[] { false });
        var pickedForRight = (Transform)nearestLiveRail.Invoke(comp, new object[] { true });
        Check("a left-side request never returns the right-side rail", pickedForLeft == leftRail);
        Check("a right-side request never returns the left-side rail even when it's far away", pickedForRight == rightRail);

        Object.DestroyImmediate(comp.gameObject);
        Object.DestroyImmediate(leftRail.gameObject);
        Object.DestroyImmediate(rightRail.gameObject);
    }

    static void ObsoleteRailAndAsteroidArtIsRemoved()
    {
        Check("obsolete rail3 prefab is removed", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Prefabs/rail3.prefab") == null);
        Check("Ember mine keeps approved beat frame one",
              AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Resources/Vfx/rail_mine_ember_1.png") != null);
        Check("Ember mine keeps approved beat frame two",
              AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Resources/Vfx/rail_mine_ember_2.png") != null);
        foreach (string name in new[] { "aestroid_brown_2", "aestroid_dark_3", "aestroid_gay_2", "aestroid_gay_4", "aestroid_gray_crooked", "aestroid_gray_crooked_3" })
            Check(name + " art removed from rotation", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Aestroids/" + name + ".png") == null);
    }

    static void MineFieldLoadsAndSpawnsOntoARail()
    {
        EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
        var comp = NewBoard("~EnmiesOnBoardTest2");

        Check("legacy mine prefab is intentionally absent; themed rail mine is runtime-built", comp.mine == null);

        var spawnMine = PrivM(typeof(enmiesOnBoard), "spawnMine");
        spawnMine.Invoke(comp, null);

        var liveMines = Priv(typeof(enmiesOnBoard), "liveMines").GetValue(comp) as System.Collections.IList;
        Check("spawning a mine adds it to the live-mines list", liveMines != null && liveMines.Count == 1);
        if (liveMines == null || liveMines.Count == 0) { Object.DestroyImmediate(comp.gameObject); return; }

        var mineTransform = liveMines[0] as Transform;
        var mount = mineTransform != null ? mineTransform.GetComponent<RailMineMount>() : null;
        Check("the spawned mine got a RailMineMount", mount != null);
        Check("the mount references a real, live rail", mount != null && mount.rail != null);
        if (mount != null && mount.rail != null)
        {
            Check("the mine spawned exactly on its rail's x", Mathf.Approximately(mineTransform.position.x, mount.rail.position.x));
        Check("the mine receives the themed rail-bomb animator",
                  mineTransform.GetComponent<RailBombAnimator>() != null);
            Check("the mine reports itself on its assigned rail", mount.IsOnRail());

            // This is the important live-play case: rails scroll and other
            // movement code may alter a bomb during the frame. LateUpdate
            // must snap it back to the assigned rail, never leave it in the
            // middle of the play field.
            mount.rail.position += new Vector3(0.18f, -0.4f, 0f);
            mineTransform.position += new Vector3(-1.5f, 0f, 0f);
            mount.SendMessage("LateUpdate");
            Check("a moving rail carries its mine to the new rail x", mount.IsOnRail());
            Check("the mine's x equals the moved rail x",
                  Mathf.Approximately(mineTransform.position.x, mount.rail.position.x));
        }

        Object.DestroyImmediate(comp.gameObject);
        if (mineTransform != null) Object.DestroyImmediate(mineTransform.gameObject);
        if (mount != null && mount.rail != null) Object.DestroyImmediate(mount.rail.gameObject);
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

        Object.DestroyImmediate(comp.gameObject);
    }

    static void WorldSpeedCapsLoweredAndRampScaleWired()
    {
        Check("Space's maxSpeed was lowered from the old 0.58",
              WorldManager.Worlds[0].maxSpeed < 0.55f && WorldManager.Worlds[0].maxSpeed > 0.35f);
        Check("Ember's maxSpeed was lowered from the old 0.78",
              WorldManager.Worlds[3].maxSpeed < 0.70f && WorldManager.Worlds[3].maxSpeed > 0.50f);
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
        // after enough real frames elapsed.
        chaseTimerField.SetValue(chaser, 0f);
        chaserGo.SendMessage("Update");
        Check("switches to wandering once the chase timer runs out",
              (bool)wanderingField.GetValue(chaser));

        Object.DestroyImmediate(playerGo);
        Object.DestroyImmediate(chaserGo);
        buttonClicks.playerDied = false;
    }

    // Reported 2026-09-07: on Space specifically, enemy spawns felt way too
    // sparse as the run sped up -- Space has the lowest enemyRampScale (the
    // baseline every other world ramps faster than) and never actually
    // reaches its own speed cap within a 300s level, so speed climbed the
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

        float midLevel = DensityAt(150f); // comfortably inside a 300s level, before the final stretch
        Check("density keeps climbing past the one-minute mark, toward the mid ceiling",
              midLevel > 2f && midLevel <= 2.5f);

        // No live WorldManager.Instance in this test, so DensityMultiplier()
        // falls back to the same 300s default gameS1's own WorldManager
        // ships with -- the last 30s of that window is t >= 270.
        Check("final 30 seconds of the level (no WorldManager -> 300s default) hits a flat 3x",
              Mathf.Approximately(DensityAt(280f), 3f));
        Check("one moment before the final stretch is still below 3x",
              DensityAt(269f) < 3f);

        // Roll() divides by the multiplier -- higher density means shorter
        // delays, i.e. more spawns per minute.
        var range = new Vector2(10f, 10f); // fixed, so any spread is purely from the multiplier
        elapsedField.SetValue(comp, 0f);
        float delayAtStart = (float)rollMethod.Invoke(comp, new object[] { range });
        elapsedField.SetValue(comp, 280f);
        float delayAtEnd = (float)rollMethod.Invoke(comp, new object[] { range });
        Check("Roll() actually shortens delays as density climbs (3x density -> 1/3 the delay)",
              Mathf.Approximately(delayAtEnd, delayAtStart / 3f));

        Object.DestroyImmediate(comp.gameObject);
    }
}
