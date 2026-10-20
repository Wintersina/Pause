using UnityEditor.SceneManagement;
using UnityEngine;

// Lock-on (docs/ship-scale-and-lockon-analysis.md, 1.3): an elite does not weld itself to the pilot. Every def's follow lag,
// aim commit and facing limit are in range; an attack that aims at the pilot winds up long enough that the aim is frozen at
// least 0.3 s before the shot; the brain's goal trails a moving pilot by about followLag; the aim is live up to the commit
// and frozen after it; and the hull never turns to face the pilot faster than faceTurnMax.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod EliteLockOnTest.Run
public static class EliteLockOnTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[LOCK] PASS  " : "[LOCK] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 30f;
    static Transform pilot;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EliteCatalog.Reload();
            Data();
            Lag();
            Commit();
            FaceLimit();
        }
        finally
        {
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
            RunScore.EndRun(RunScore.RunId);
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
        }
        Debug.Log("[LOCK] failures: " + fails);
        return fails;
    }

    static void Fresh()
    {
        EliteSystem.Clear();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RunScore.EndRun(RunScore.RunId);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = .05f;
        collisionDetection.lifeCounter = 0;
        collisionDetection.atomCheck = false;
        collisionDetection.cloakTimer = 0f;
        PlayerInvuln.Reset();
        RunScore.BeginRun(true, true);
        pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -2.5f, 0f);
        EliteSystem.PlayerOverride = pilot;
        Random.InitState(4321);
    }

    static EliteDef ByBrain(string brain)
    {
        return BaseEliteDefs.For(brain);
    }

    static EliteShip InPlay(EliteDef d, Vector2 at)
    {
        var e = EliteShip.CreateInPlay(d, at);
        e.AttackCooldown = 99f;
        return e;
    }

    // The pilot flies to x over `frames` steps (a real move: a one-frame jump of a unit or more is a teleport and loses the elite).
    static void Fly(float x, int frames)
    {
        float x0 = pilot.position.x;
        for (int i = 1; i <= frames; i++)
        {
            pilot.position = new Vector3(Mathf.Lerp(x0, x, i / (float)frames), pilot.position.y, 0f);
            EliteSystem.Step(Dt);
        }
    }

    static void Step(float s, System.Action each = null)
    {
        for (float t = 0f; t < s - 1e-4f; t += Dt) { if (each != null) each(); EliteSystem.Step(Dt); }
    }

    static void Data()
    {
        int aimed = 0;
        foreach (var d in EliteCatalog.All)
        {
            Check(d.key + ": followLag " + d.followLag.ToString("0.00") + " s in [" + EliteDef.MinFollowLag + ", " + EliteDef.MaxFollowLag + "]",
                  d.followLag >= EliteDef.MinFollowLag && d.followLag <= EliteDef.MaxFollowLag);
            Check(d.key + ": aimCommit " + d.aimCommit.ToString("0.00") + " in [0.3, 0.65], facing limit " + d.faceTurnMax + " deg/s <= 300",
                  d.aimCommit >= .3f && d.aimCommit <= .65f && d.faceTurnMax <= 300.01f && d.faceTurnMax >= 120f);
            var attack = EliteAttacks.Create(d.attack);
            if (!attack.Aimed) continue;
            aimed++;
            float tell = Mathf.Max(d.tellSeconds, EliteDef.MinAimedTell);
            float warning = tell * (1f - d.aimCommit);
            Check(d.key + " (" + d.attack + "): the aim is frozen " + warning.ToString("0.00") + " s before the shot (tell " + tell.ToString("0.00") + " s, commit " + d.aimCommit.ToString("0.00") + "), >= 0.3 s",
                  warning >= .3f - 1e-3f);
        }
        Check("the aimed attacks are covered (" + aimed + " defs)", aimed >= 14);
    }

    // The brain's goal follows a pilot who steps sideways by followLag, not at once.
    static void Lag()
    {
        Fresh();
        var d = ByBrain("gunship");
        var e = InPlay(d, new Vector2(1.7f, -2.5f));
        Step(1f);
        Vector2 settled = e.Follow;
        Check("settled: the followed point is the pilot (" + settled + ")", (settled - (Vector2)pilot.position).magnitude < .05f);
        Fly(1.2f, 6);   // 0.2 s: a 6 u/s juke
        float share = (e.Follow.x - settled.x) / Mathf.Max(.01f, e.Seen.x - settled.x);
        Check("at the end of a 0.2 s, 1.2 u juke it has followed " + (share * 100f).ToString("0") + "% of what it sees (not welded: < 75%, but moving: > 5%)", share < .75f && share > .05f);
        Check("its true perception is exact (Seen " + e.Seen.x.ToString("0.00") + ")", Mathf.Abs(e.Seen.x - 1.2f) < .01f);
        Step(d.followLag * 5f);
        Check("after 5 x followLag it has caught up (" + e.Follow.x.ToString("0.00") + ")", Mathf.Abs(e.Follow.x - 1.2f) < .05f);
    }

    // Aim: live until the commit, frozen after it.
    static void Commit()
    {
        Fresh();
        var d = ByBrain("skirmisher");   // blink_shards: re-read the pilot at fire before
        var e = InPlay(d, new Vector2(0f, 0f));
        Step(1f);
        e.ForceAttack();
        Check("it is telling", e.Telling && e.Attack.Aimed);
        float tell = e.Attack.TellSeconds;
        // a little before the commit: still live
        Step(tell * d.aimCommit * .5f);
        Fly(.6f, 3);
        Check("before the commit the aim follows the pilot (" + e.AimSeen.x.ToString("0.00") + ")", !e.AimLocked && Mathf.Abs(e.AimSeen.x - .6f) < .05f);
        // past the commit: frozen
        while (e.Telling && !e.AimLocked) EliteSystem.Step(Dt);
        Vector2 frozen = e.AimSeen;
        Check("the commit freezes the aim where the pilot was (" + frozen.x.ToString("0.00") + ", .6 within a step)", e.AimLocked && Mathf.Abs(frozen.x - .6f) < .1f);
        Fly(-2.0f, 12);
        int guard = 0;
        while ((e.Telling || e.Acting) && guard++ < 200)
        {
            EliteSystem.Step(Dt);
            if (!e.Telling && !e.Acting) break;
            if (Vector2.Distance(e.AimSeen, frozen) > 1e-4f) { Check("the aim stayed frozen through the rest of the tell and the action", false); return; }
        }
        Check("the aim stayed frozen through the rest of the tell and the action (the pilot moved 3 u)", Vector2.Distance(e.AimSeen, frozen) < 1e-4f);
        Check("it fired", e.Attack.Fired > 0 || e.MuzzleFlashesShown > 0);
    }

    // The nose never swings faster than faceTurnMax.
    static void FaceLimit()
    {
        Fresh();
        var d = ByBrain("skirmisher");
        var e = InPlay(d, new Vector2(0f, 0f));
        Step(1f);
        float last = e.Facing, worst = 0f;
        for (int i = 0; i < 90; i++)
        {
            pilot.position = new Vector3(Mathf.PingPong(i * .25f, 4.6f) - 2.3f, -2.5f + Mathf.PingPong(i * .12f, 1.5f), 0f);
            EliteSystem.Step(Dt);
            worst = Mathf.Max(worst, Mathf.Abs(Mathf.DeltaAngle(last, e.Facing)) / Dt);
            last = e.Facing;
        }
        Check("the facing turned at most " + worst.ToString("0") + " deg/s (limit " + d.faceTurnMax + ", turnRate x 1.4 = " + (d.turnRate * 1.4f).ToString("0") + ")", worst <= d.faceTurnMax + 5f);
    }
}
