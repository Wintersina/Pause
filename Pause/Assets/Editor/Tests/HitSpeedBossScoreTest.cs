using UnityEngine;

// A hit costs speed (HitSpeedLoss: 5, 4, 3, 2, then 1 HUD per hit, never under the run's start speed) and a destroyed boss
// pays 1000 x its number in the run (ScoreRules.BossPoints / RunScore.OnBoss), whichever world the run started in.
public static class HitSpeedBossScoreTest
{
    static int fails;
    static void Check(string what, bool ok) { Debug.Log((ok ? "[HITSPEED] PASS  " : "[HITSPEED] FAIL  ") + what); if (!ok) fails++; }
    public static void Run() { TestHarness.Exit(Execute()); }

    static int Hud() { return Mathf.RoundToInt(moveBackGround.speed * 100f); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            Table();
            Run6Hits(10, 30);
            Run6Hits(0, 30);
            Floor();
            Locked();
            BossScores();
        }
        finally
        {
            ShipStartSpeed.EquippedHudOverride = null;
            SpeedRamp.ResetBoost();
            BossEncounter.ResetRun();
            HitSpeedLoss.BeginRun();
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
        }
        Debug.Log("[HITSPEED] failures: " + fails);
        return fails;
    }

    static void Begin(int startHud, int speedHud)
    {
        BossEncounter.ResetRun();
        SpeedRamp.ResetBoost();
        ShipStartSpeed.EquippedHudOverride = () => startHud;
        moveBackGround.speed = speedHud / 100f;
        HitSpeedLoss.BeginRun();
    }

    static void Table()
    {
        Check("the table is 5, 4, 3, 2, 1", HitSpeedLoss.LossHud.Length == 5 && HitSpeedLoss.LossFor(1) == 5 && HitSpeedLoss.LossFor(2) == 4 &&
              HitSpeedLoss.LossFor(3) == 3 && HitSpeedLoss.LossFor(4) == 2 && HitSpeedLoss.LossFor(5) == 1);
        Check("every hit after the fifth costs 1; no hit costs nothing", HitSpeedLoss.LossFor(6) == 1 && HitSpeedLoss.LossFor(40) == 1 && HitSpeedLoss.LossFor(0) == 0);
        Check("After(): the loss off the speed, held at the start speed",
              Mathf.Approximately(HitSpeedLoss.After(.30f, .10f, 1), .25f) && Mathf.Approximately(HitSpeedLoss.After(.12f, .10f, 1), .10f) &&
              Mathf.Approximately(HitSpeedLoss.After(.08f, .10f, 1), .08f));
    }

    static void Run6Hits(int startHud, int speedHud)
    {
        Begin(startHud, speedHud);
        int[] expect = new int[6];
        int v = speedHud;
        for (int i = 1; i <= 6; i++) { v = Mathf.Max(startHud, v - HitSpeedLoss.LossFor(i)); expect[i - 1] = v; }
        bool ok = true;
        string log = "";
        for (int i = 0; i < 6; i++)
        {
            HitSpeedLoss.OnHit();
            ok &= Hud() == expect[i];
            log += Hud() + " ";
        }
        Check("start " + startHud + ", speed " + speedHud + ": six hits take it to " + log + "(5, 4, 3, 2, 1, 1 lost)", ok && HitSpeedLoss.Hits == 6);
        Check("the first five hits lost 15 in all", expect[4] == Mathf.Max(startHud, speedHud - 15));
    }

    static void Floor()
    {
        Begin(10, 12);
        HitSpeedLoss.OnHit();
        Check("never under the start speed (12 - 5 held at 10)", Hud() == 10);
        for (int i = 0; i < 8; i++) HitSpeedLoss.OnHit();
        Check("and it stays there", Hud() == 10);
        Begin(10, 6);
        HitSpeedLoss.OnHit();
        Check("a speed already under the start is not raised by a hit", Hud() == 6);
    }

    static void Locked()
    {
        // inside a boss fight the speed is the boss's (BossEncounter.SpeedLocked) and the next world re-enters at the start speed
        // anyway: the hit still counts toward the table, the speed is left alone
        string src = System.IO.File.ReadAllText("Assets/Scripts/Ship/HitSpeedLoss.cs");
        Check("a hit while a boss or a world entry holds the speed counts but does not cut it",
              src.Contains("hits++;") && src.Contains("if (BossEncounter.SpeedLocked || WorldEntry.Active) return;") &&
              src.IndexOf("hits++;") < src.IndexOf("if (BossEncounter.SpeedLocked"));
        Check("the hit is wired to the lost heart, once, and never in the tutorial or on the killing hit",
              System.IO.File.ReadAllText("Assets/Scripts/Ship/collisionDetection.cs").Contains("if (!TutorialCannotDie(gameObject) && lifeCounter < MAXLIFE) HitSpeedLoss.OnHit();") &&
              System.IO.File.ReadAllText("Assets/Scripts/Ship/collisionDetection.cs").Contains("HitSpeedLoss.BeginRun();"));
    }

    static void BossScores()
    {
        PlayerPrefs.SetString("HasDoneTut", "true");
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = false;
        moveBackGround.speed = 0f;
        ScoreMultiplier.Reset();
        Check("pure: 1000, 2000, 3000, 4000 by boss number, whatever the loop",
              ScoreRules.BossPoints(true, 0f, false, 0, 1) == 1000 && ScoreRules.BossPoints(true, 0f, false, 0, 2) == 2000 &&
              ScoreRules.BossPoints(true, 0f, false, 2, 3) == 3000 && ScoreRules.BossPoints(true, 0f, false, 5, 4) == 4000);
        // a run from any start world: the n-th boss defeated pays n x 1000 (the world the boss lives in does not matter)
        // (the world the run started in is not an input: the number counts bosses defeated, so a run begun in a later dock world pays the same)
        foreach (int startWorld in new[] { 0, 3 })
        {
            RunScore.EndRun(RunScore.RunId);
            RunScore.BeginRun(true, true);
            ScoreMultiplier.Reset();
            long t = RunScore.Total;
            bool ok = true;
            string log = "";
            for (int n = 1; n <= 3; n++)
            {
                t = RunScore.Total;
                RunScore.OnBoss(true, 0f, false, Vector3.zero);
                long paid = RunScore.Total - t;
                log += paid + " ";
                ok &= paid == 1000 * n;
            }
            Check("run starting in world " + (startWorld + 1) + ": bosses pay " + log, ok && RunScore.Parts.bossCount == 3 && RunScore.Parts.bosses == 6000);
        }
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(true, true);
        Check("a new run starts the ladder over: the first boss pays 1000 again",
              RunScore.OnBoss(true, 0f, false, Vector3.zero) == 1000);
        RunScore.EndRun(RunScore.RunId);
    }
}
