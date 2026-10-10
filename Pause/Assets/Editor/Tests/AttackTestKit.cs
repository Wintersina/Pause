using UnityEditor.SceneManagement;
using UnityEngine;

// Shared fixtures for the themed-hazard suites (AttackHazardTest, AttackFairnessTest) and the
// AttackHazardPreview tool: a clean scene with a camera and a pilot stand-in, the world's clock
// stepped by hand.
public static class AttackTestKit
{
    public const float Dt = 1f / 60f;
    public static Transform Pilot;
    public static Camera Cam;

    // An empty scene, the hostile systems reset, a still board, a camera (view +-5), a pilot at (0, -3).
    public static void Fresh()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EliteSystem.Clear();
        ShotSkins.ResetForTests();
        AttackArt.Clear();
        AttackHazardArt.Forget();
        AttackPools.Forget();
        AttackHazard.ForgetAll();
        AttackPreview.ResetCounters();
        HostileShots.ResetCounters();
        BossEncounter.ResetRun();
        BossRails.Reset();
        FriendlyFire.ClearPending();
        FriendlyFire.ResetCounters();
        FriendlyFire.ResetHostileCounters();
        FriendlyFire.HostileFireEnabled = true;
        FriendlyFire.OnSceneLoaded("gameS1");
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = 0f;
        Time.timeScale = 1f;
        PlayerInvuln.Reset();
        ShotOutline.Bold = false;
        EnemyThreat.Reset();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        Cam = camGo.GetComponent<Camera>();
        Cam.orthographic = true;
        Cam.orthographicSize = 5f;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        Pilot = new GameObject("~Pilot").transform;
        Pilot.position = new Vector3(0f, -3f, 0f);
        EliteSystem.PlayerOverride = Pilot;
        Random.InitState(1357);
    }

    public static void Cleanup()
    {
        EliteSystem.Clear();
        EliteSystem.PlayerOverride = null;
        ShotSkins.ResetForTests();
        AttackArt.Clear();
        AttackHazardArt.Forget();
        AttackPools.Forget();
        AttackPreview.EndAll();
        ShotOutline.Bold = null;
        HostileShots.ResetCounters();
        buttonClicks.playerDied = false;
        moveBackGround.speed = 0f;
        EnemyBehaviours.ClearOverrides();
        EnemyThreat.Reset();
        EnemyThreat.ForceShooting = false;
    }

    // Steps the hostile world (shots, pooled hazards) `seconds` of running frames.
    public static void Advance(float seconds)
    {
        int n = Mathf.RoundToInt(seconds / Dt);
        for (int i = 0; i < n; i++) EliteSystem.Step(Dt);
    }

    // Steps frame by frame until `done` or `limit` seconds; returns the seconds stepped.
    public static float AdvanceUntil(System.Func<bool> done, float limit)
    {
        int n = Mathf.RoundToInt(limit / Dt);
        for (int i = 0; i < n; i++)
        {
            if (done()) return i * Dt;
            EliteSystem.Step(Dt);
        }
        return limit;
    }
}
