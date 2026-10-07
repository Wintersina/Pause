using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Runs the *Test suites in a single editor launch. Recommended command line
// (working directory Pause/ -- EnemyRosterTest & co. read Assets/... relative
// paths -- and an ABSOLUTE -projectPath):
//
//   cd Pause && Unity -batchmode -quit -projectPath "$PWD" -executeMethod AllTests.RunAll -logFile tests.log
//
//   AllTests.RunAll     every suite, every check (what verification uses)
//   AllTests.RunSuites  only the suites named by -suites A,B,C (required)
//   AllTests.RunFast    every suite (or -suites A,B) minus the checks wrapped
//                       in TestHarness.Slow(...) -- quick iteration, not a sign-off
//
// Keep graphics on (no -nographics): CodexTest, ShopTest, HomePauseTest and
// SplashLayoutTest read pixels back from Camera.Render(). Startup is the
// editor's own launch plus its asset refresh / script compile, which is only
// long after a pull or branch switch touched assets or scripts.
//
// Output: "[ALL] <suite>: PASS (1.2s)" or "FAIL (n) (1.2s)" per suite, then
//   [ALL] N suites, M failures, tests 123.4s (startup 30.1s)
//   [ALL] RESULT: PASS|FAIL failures=M failed=A,B
// A suite's failure count is the larger of what Execute() returned and the
// "[TAG] FAIL" lines it logged, so a logged failure can't be lost. The editor
// exits 1 on any failure (0 otherwise), also under -quit.
public static class AllTests
{
    // One line per suite (merge-friendly); order doesn't matter, each suite
    // cleans up after itself through TestHarness.Sandbox.
    static readonly (string name, Func<int> execute)[] Suites =
    {
        ("AccountCloudSaveTest", AccountCloudSaveTest.Execute),
        ("AccountSignInTest", AccountSignInTest.Execute),
        ("AppIconTest", AppIconTest.Execute),
        ("ArtRestyleTest", ArtRestyleTest.Execute),
        ("AchievementTiersTest", AchievementTiersTest.Execute),
        ("AsteroidBackwardsAndShopColumnsTest", AsteroidBackwardsAndShopColumnsTest.Execute),
        ("AsteroidColliderTest", AsteroidColliderTest.Execute),
        ("AtomFlightTest", AtomFlightTest.Execute),
        ("BackNavigationTest", BackNavigationTest.Execute),
        ("BossAttackTest", BossAttackTest.Execute),
        ("BossEncounterTest", BossEncounterTest.Execute),
        ("BossIntroTest", BossIntroTest.Execute),
        ("BossWarningTest", BossWarningTest.Execute),
        ("BugBatch0907Test", BugBatch0907Test.Execute),
        ("CameraFitTest", CameraFitTest.Execute),
        ("CinematicEarlyClearTest", CinematicEarlyClearTest.Execute),
        ("CloakShieldTest", CloakShieldTest.Execute),
        ("CodexTest", CodexTest.Execute),
        ("CooldownAtomTest", CooldownAtomTest.Execute),
        ("CreditsTest", CreditsTest.Execute),
        ("DeathCrashTest", DeathCrashTest.Execute),
        ("DustDischargeTest", DustDischargeTest.Execute),
        ("DeathDominoTest", DeathDominoTest.Execute),
        ("DeathPanelTest", DeathPanelTest.Execute),
        ("DeveloperModeTest", DeveloperModeTest.Execute),
        ("DifficultyRebalanceTest", DifficultyRebalanceTest.Execute),
        ("DifficultyRetuneTest", DifficultyRetuneTest.Execute),
        ("EliteTest", EliteTest.Execute),
        ("EliteEvasionTest", EliteEvasionTest.Execute),
        ("EnemyBehaviourTest", EnemyBehaviourTest.Execute),
        ("EnemyDensityTest", EnemyDensityTest.Execute),
        ("EnemyRosterTest", EnemyRosterTest.Execute),
        ("ExhaustSkinTest", ExhaustSkinTest.Execute),
        ("ExhaustStyleTest", ExhaustStyleTest.Execute),
        ("FrameRateBootstrapTest", FrameRateBootstrapTest.Execute),
        ("GreenAtomSizeTest", GreenAtomSizeTest.Execute),
        ("HeartsPlacementTest", HeartsPlacementTest.Execute),
        ("HomePauseTest", HomePauseTest.Execute),
        ("HazardSizeTest", HazardSizeTest.Execute),
        ("HostileProjectileTest", HostileProjectileTest.Execute),
        ("LaunchCountdownTest", LaunchCountdownTest.Execute),
        ("LeaderboardTest", LeaderboardTest.Execute),
        ("LoopTest", LoopTest.Execute),
        ("MissingScriptsTest", MissingScriptsTest.Execute),
        ("NextFeatures0907Test", NextFeatures0907Test.Execute),
        ("OpenPortalTest", OpenPortalTest.Execute),
        ("PauseGlowTest", PauseGlowTest.Execute),
        ("PausedLabelTest", PausedLabelTest.Execute),
        ("PickupHitchTest", PickupHitchTest.Execute),
        ("PickupRulesTest", PickupRulesTest.Execute),
        ("PostHitInvulnTest", PostHitInvulnTest.Execute),
        ("RailMineArtTest", RailMineArtTest.Execute),
        ("RailsRollTest", RailsRollTest.Execute),
        ("RailsShipSizeTest", RailsShipSizeTest.Execute),
        ("RailsVettingTest", RailsVettingTest.Execute),
        ("RamKillTest", RamKillTest.Execute),
        ("RedAtomFreeShotTest", RedAtomFreeShotTest.Execute),
        ("ResumeFxTest", ResumeFxTest.Execute),
        ("ResumeSlowMoTest", ResumeSlowMoTest.Execute),
        ("RosterCleanupTest", RosterCleanupTest.Execute),
        ("ScoringTest", ScoringTest.Execute),
        ("ScoreBonusTest", ScoreBonusTest.Execute),
        ("ShieldFitTest", ShieldFitTest.Execute),
        ("ShieldPickupSkinTest", ShieldPickupSkinTest.Execute),
        ("ShieldShockwaveTest", ShieldShockwaveTest.Execute),
        ("ShipArtTest", ShipArtTest.Execute),
        ("ScreenFitTest", ScreenFitTest.Execute),
        ("ShipHitboxTest", ShipHitboxTest.Execute),
        ("ShipReachTest", ShipReachTest.Execute),
        ("ShipAttacksTest", ShipAttacksTest.Execute),
        ("ShipDamageTest", ShipDamageTest.Execute),
        ("ShipLivesIndicatorTest", ShipLivesIndicatorTest.Execute),
        ("ShipLivesTest", ShipLivesTest.Execute),
        ("ShipNozzlesTest", ShipNozzlesTest.Execute),
        ("ShipSelectionTest", ShipSelectionTest.Execute),
        ("ShipSkinsTest", ShipSkinsTest.Execute),
        ("SkinDamageGameplayTest", SkinDamageGameplayTest.Execute),
        ("ShipWeaponUpgradesTest", ShipWeaponUpgradesTest.Execute),
        ("ShopHealthCarryoverTest", ShopHealthCarryoverTest.Execute),
        ("ShopTest", ShopTest.Execute),
        ("SpawnSpaceTest", SpawnSpaceTest.Execute),
        ("SpeedCapTest", SpeedCapTest.Execute),
        ("SpeedRampTest", SpeedRampTest.Execute),
        ("SpinWindTest", SpinWindTest.Execute),
        ("SplashLayoutTest", SplashLayoutTest.Execute),
        ("StarDustPersistenceTest", StarDustPersistenceTest.Execute),
        ("ThrusterTest", ThrusterTest.Execute),
        ("TallScreenTest", TallScreenTest.Execute),
        ("TeleportKillTest", TeleportKillTest.Execute),
        ("TitleScreenTrafficTest", TitleScreenTrafficTest.Execute),
        ("TitleScreenCombatTest", TitleScreenCombatTest.Execute),
        ("TutorialAtomFlowTest", TutorialAtomFlowTest.Execute),
        ("TutorialCurrencyTest", TutorialCurrencyTest.Execute),
        ("TutorialParityTest", TutorialParityTest.Execute),
        ("TutorialRobotTest", TutorialRobotTest.Execute),
        ("UiScaleTest", UiScaleTest.Execute),
        ("UnusedAssetGuardTest", UnusedAssetGuardTest.Execute),
        ("WeaponChargeTest", WeaponChargeTest.Execute),
        ("WorldBackdropTest", WorldBackdropTest.Execute),
        ("WorldLogicTest", WorldLogicTest.Execute),
        ("WorldPaceTest", WorldPaceTest.Execute),
    };

    public static void RunAll() { Run(null, fast: false); }

    public static void RunSuites()
    {
        var names = CommandLineSuites();
        if (names == null || names.Count == 0)
        {
            Debug.LogError("[ALL] RunSuites needs -suites A,B,C");
            Debug.Log("[ALL] RESULT: FAIL failures=1 failed=-suites?");
            EditorApplication.Exit(1);
            return;
        }
        Run(names, fast: false);
    }

    public static void RunFast() { Run(CommandLineSuites(), fast: true); }

    // "-suites A,B,C" (names with or without the "Test" suffix).
    static List<string> CommandLineSuites()
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-suites")
                return args[i + 1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
        return null;
    }

    // "[TAG] FAIL ..." -- the line every suite's Check() logs for a failed check.
    static readonly Regex FailLine = new Regex(@"^\[[A-Za-z0-9_\-]+\] FAIL\b");

    static int loggedFails;
    static readonly Stopwatch suiteClock = new Stopwatch();
    static double lastLogAt;
    static readonly List<(double gap, string after)> gaps = new List<(double, string)>();

    static void OnLog(string message, string stackTrace, LogType type)
    {
        if (message == null) return;
        if (FailLine.IsMatch(message)) loggedFails++;
        // The longest silences inside a suite point at its slow step.
        double now = suiteClock.Elapsed.TotalSeconds;
        double gap = now - lastLogAt;
        lastLogAt = now;
        if (gap >= .3 && !message.StartsWith("[ALL]", StringComparison.Ordinal))
            gaps.Add((gap, message.Length > 140 ? message.Substring(0, 140) : message));
    }

    static void Run(List<string> only, bool fast)
    {
        var startup = DateTime.Now - Process.GetCurrentProcess().StartTime;
        var selected = Suites.ToList();
        int failed = 0;
        var failedNames = new List<string>();
        if (only != null)
        {
            foreach (string n in only)
                if (!Suites.Any(s => Matches(s.name, n)))
                {
                    Debug.LogError("[ALL] unknown suite '" + n + "'");
                    failedNames.Add(n + "?");
                    failed++;
                }
            selected = Suites.Where(s => only.Any(n => Matches(s.name, n))).ToList();
        }

        // In batch mode every Debug.Log carries a managed stack trace, and
        // every edit-mode SendMessage("Update") logs a ShouldRunBehaviour
        // assertion with one: hundreds of thousands of traces were a large
        // share of a run and its 400 MB log. Errors and exceptions keep theirs.
        // (Re-applied per suite: the setting lives in ProjectSettings, which a
        // suite that saves assets -- AppIconTest -- reloads from disk.)
        var traces = new Dictionary<LogType, StackTraceLogType>();
        foreach (var t in QuietTypes) traces[t] = Application.GetStackTraceLogType(t);
        QuietTraces();
        TestHarness.Fast = fast;
        TestHarness.SkippedSlow = 0;
        Application.logMessageReceived += OnLog;
        Debug.Log("[ALL] " + (fast ? "RunFast" : only != null ? "RunSuites" : "RunAll") + ": " + selected.Count +
                  " suites; editor startup took " + startup.TotalSeconds.ToString("F1") + "s");

        var all = Stopwatch.StartNew();
        var times = new List<(string name, double seconds)>();
        try
        {
            if (selected.Count > 3)   // pays off once several suites build ships
            {
                var warm = Stopwatch.StartNew();
                TestHarness.BeginRun();
                Debug.Log("[ALL] shared art warmed in " + warm.Elapsed.TotalSeconds.ToString("F1") + "s");
            }
            foreach (var suite in selected)
            {
                QuietTraces();
                loggedFails = 0;
                gaps.Clear();
                lastLogAt = 0;
                suiteClock.Restart();
                int returned;
                try { returned = suite.execute(); }
                catch (Exception e)
                {
                    // A throwing suite counts as (at least) one failure; keep going.
                    Debug.LogError("[ALL] " + suite.name + " threw: " + e);
                    returned = 1;
                }
                suiteClock.Stop();
                double seconds = suiteClock.Elapsed.TotalSeconds;
                int failures = Math.Max(returned, loggedFails);
                if (returned != loggedFails)
                    Debug.LogWarning("[ALL] " + suite.name + " returned " + returned + " failures but logged " +
                                     loggedFails + " FAIL lines; counting " + failures);
                if (failures > 0) { failed += failures; failedNames.Add(suite.name); }
                times.Add((suite.name, seconds));
                Debug.Log("[ALL] " + suite.name + ": " + (failures == 0 ? "PASS" : "FAIL (" + failures + ")") +
                          " (" + seconds.ToString("F1") + "s)");
                if (seconds >= 2)
                    foreach (var g in gaps.OrderByDescending(g => g.gap).Take(3))
                        Debug.Log("[ALL]     " + g.gap.ToString("F1") + "s before: " + g.after);
            }
        }
        finally
        {
            Application.logMessageReceived -= OnLog;
            TestHarness.Fast = false;
            TestHarness.EndRun();
            foreach (var pair in traces) Application.SetStackTraceLogType(pair.Key, pair.Value);
        }

        Debug.Log("[ALL] slowest: " + string.Join(", ", times.OrderByDescending(t => t.seconds).Take(15)
                                                       .Select(t => t.name + " " + t.seconds.ToString("F1") + "s")));
        Debug.Log("[ALL] " + selected.Count + " suites, " + failed + " failures, tests " +
                  all.Elapsed.TotalSeconds.ToString("F1") + "s (startup " + startup.TotalSeconds.ToString("F1") + "s)");
        if (fast) Debug.Log("[ALL] RunFast skipped " + TestHarness.SkippedSlow + " slow checks (RunAll runs them)");
        Debug.Log("[ALL] RESULT: " + (failed == 0 ? "PASS" : "FAIL") + " failures=" + failed +
                  " failed=" + string.Join(",", failedNames));
        EditorApplication.Exit(failed == 0 ? 0 : 1);
    }

    static readonly LogType[] QuietTypes = { LogType.Log, LogType.Warning, LogType.Assert };

    static void QuietTraces()
    {
        foreach (var t in QuietTypes) Application.SetStackTraceLogType(t, StackTraceLogType.None);
    }

    static bool Matches(string suite, string name) =>
        string.Equals(suite, name, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(suite, name + "Test", StringComparison.OrdinalIgnoreCase);
}
