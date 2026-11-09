using System.Collections.Generic;
using UnityEngine;

// Planet escape: every PORTAL DANGER level survived while the portal waits pays score (ScoreRules.DangerSurvivedPoints,
// through RunScore like any other points), and surviving level 10 in one escape unlocks "Hot Escape".
public static class EscapeWaveTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ESC] PASS  " : "[ESC] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    // Open a portal and fly through the grace: Level 0, nothing survived yet.
    static void OpenAndGrace()
    {
        PortalPressure.Reset();
        PortalPressure.Open(1);
        PortalPressure.Tick(PortalPressure.GraceSeconds);
    }

    // One whole DANGER level of flight.
    static void Level() { PortalPressure.Tick(PortalPressure.LevelSeconds); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        try
        {
            Curve();
            Awards();
            Achievement();
        }
        finally
        {
            PortalPressure.Reset();
            RunScore.EndRun(RunScore.RunId);
            Achievements.ForceReal = null;
            AchievementStores.Current = null;
            AchievementStore.ResetAll();
        }
        Debug.Log("[ESC] failures: " + fails);
        return fails;
    }

    static void Curve()
    {
        bool linear = true, rising = true;
        int prev = 0;
        for (int n = 1; n <= 15; n++)
        {
            int p = ScoreRules.DangerSurvivedPoints(n);
            linear &= p == ScoreRules.DangerSurvivedBase * n;
            rising &= p > prev;
            prev = p;
        }
        Check("the curve is DangerSurvivedBase x level for levels 1..15", linear);
        Check("the curve strictly rises", rising);
        Check("level 1 pays " + ScoreRules.DangerSurvivedPoints(1) + ", level 10 " + ScoreRules.DangerSurvivedPoints(10) + ", level 15 " + ScoreRules.DangerSurvivedPoints(15),
              ScoreRules.DangerSurvivedPoints(1) == 25 && ScoreRules.DangerSurvivedPoints(10) == 250 && ScoreRules.DangerSurvivedPoints(15) == 375);
        Check("no level, no points", ScoreRules.DangerSurvivedPoints(0) == 0 && ScoreRules.DangerSurvivedPoints(-3) == 0);
        Check("a later loop never pays less", ScoreRules.DangerSurvivedPoints(5, 1) >= ScoreRules.DangerSurvivedPoints(5, 0));
        int tenLevels = 0;
        for (int n = 1; n <= 10; n++) tenLevels += ScoreRules.DangerSurvivedPoints(n);
        Check("the first ten levels together (" + tenLevels + ") stay well under a first pass (~6,600)", tenLevels < 2000);
    }

    static void Awards()
    {
        var raised = new List<KeyValuePair<int, RunScore.Source>>();
        System.Action<int, Vector3, RunScore.Source> onScored = (p, at, s) => raised.Add(new KeyValuePair<int, RunScore.Source>(p, s));
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(true, true, false);
        RunScore.Scored += onScored;
        try
        {
            OpenAndGrace();
            Check("grace is over, no level survived, nothing earned", PortalPressure.Pressing && PortalPressure.DangersSurvived == 0 && RunScore.Total == 0);
            PortalPressure.Tick(PortalPressure.LevelSeconds * .9f);
            Check("dying (or just flying) inside a level pays nothing", RunScore.Total == 0 && raised.Count == 0);
            PortalPressure.Tick(PortalPressure.LevelSeconds * .1f + .01f);
            Check("DANGER 1 survived pays 25 as a Danger popup (" + RunScore.Total + ")",
                  PortalPressure.DangersSurvived == 1 && RunScore.Total == 25 && raised.Count == 1 && raised[0].Key == 25 && raised[0].Value == RunScore.Source.Danger);
            Check("it sits on the WORLDS row and does not count a world", RunScore.Parts.worlds == 25 && RunScore.Parts.worldCount == 0);
            PortalPressure.Tick(1f);
            PortalPressure.Tick(1f);
            Check("more frames inside the level do not pay it again", RunScore.Total == 25 && raised.Count == 1);
            Check("earnings are still closed for everything else", PortalPressure.EarningsClosed);
            Level();
            Check("DANGER 2 survived pays 50 more (75)", RunScore.Total == 75 && raised.Count == 2 && raised[1].Key == 50);
            PortalPressure.Tick(PortalPressure.LevelSeconds * 2f);
            Check("one long frame over two levels pays both, in order (75 + 75 + 100 = 250)", RunScore.Total == 250 && raised.Count == 4 && raised[2].Key == 75 && raised[3].Key == 100);

            long before = RunScore.Total;
            RunScore.EndRun(RunScore.RunId);   // the pilot died
            Level();
            Check("a dead run is paid nothing", RunScore.Total == before);

            raised.Clear();
            RunScore.BeginRun(true, true, false);
            OpenAndGrace();
            Level();
            Check("a new escape starts again from DANGER 1 (25)", RunScore.Total == 25 && raised.Count == 1 && raised[0].Key == 25);
            PortalPressure.Close(true);
            PortalPressure.Tick(100f);
            Check("a portal that was flown pays no more", RunScore.Total == 25);

            RunScore.EndRun(RunScore.RunId);
            RunScore.BeginRun(false, false, false);   // the tutorial: not scoring
            OpenAndGrace();
            Level();
            Check("a run that does not score pays nothing", RunScore.Total == 0);
        }
        finally { RunScore.Scored -= onScored; }
    }

    static void Achievement()
    {
        var unlocked = new List<string>();
        System.Action<AchievementDef> onUnlocked = d => unlocked.Add(d.id);
        AchievementStores.Current = new FakeAchievementStore { available = false };
        AchievementStore.ResetAll();
        AchievementStore.Unlocked += onUnlocked;
        try
        {
            var def = AchievementCatalog.Find("escape_danger_10");
            Check("Hot Escape is in the catalogue (Skill group, 10 points, one-shot)", def != null && def.group == AchievementGroup.Skill && def.points == 10 && !def.IsCounter);
            Check("its store id is a placeholder until it is created in Play Console", AchievementIds.IsPlaceholder(AchievementIds.AndroidId(def)));

            Achievements.ForceReal = true;
            RunScore.EndRun(RunScore.RunId);
            RunScore.BeginRun(true, true, false);
            OpenAndGrace();
            for (int i = 0; i < 9; i++) Level();
            Check("DANGER 9 survived: not yet", PortalPressure.DangersSurvived == 9 && !AchievementStore.IsUnlocked(def));
            PortalPressure.Tick(PortalPressure.LevelSeconds * .9f);
            Check("inside DANGER 10: still not", !AchievementStore.IsUnlocked(def));
            PortalPressure.Tick(PortalPressure.LevelSeconds * .1f + .01f);
            Check("DANGER 10 survived: unlocked, exactly once", AchievementStore.IsUnlocked(def) && unlocked.Count == 1 && unlocked[0] == "escape_danger_10");
            Level(); Level();
            Check("surviving more does not unlock it again", unlocked.Count == 1);

            // a second escape in the same run
            AchievementStore.ResetAll();
            unlocked.Clear();
            OpenAndGrace();
            for (int i = 0; i < 9; i++) Level();
            Check("each escape counts on its own: 9 levels in a fresh escape is not enough", !AchievementStore.IsUnlocked(def));

            // guards
            AchievementStore.ResetAll();
            Achievements.ForceReal = false;
            OpenAndGrace();
            for (int i = 0; i < 12; i++) Level();
            Check("not a real run (tutorial / developer mode): no unlock", !AchievementStore.IsUnlocked(def));
            Achievements.ForceReal = true;
            RunScore.EndRun(RunScore.RunId);
            RunScore.BeginRun(true, true, true);   // the premium ship
            OpenAndGrace();
            for (int i = 0; i < 12; i++) Level();
            Check("an unranked premium-ship run: no unlock", !AchievementStore.IsUnlocked(def));
        }
        finally { AchievementStore.Unlocked -= onUnlocked; }
    }
}
