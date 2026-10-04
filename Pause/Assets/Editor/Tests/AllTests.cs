using System;
using UnityEditor;
using UnityEngine;

// Runs every *Test suite in a single editor launch:
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod AllTests.RunAll
//
// Logs one "[ALL] <suite>: ..." line per suite and a final
// "[ALL] N suites, M failures" line; exits 1 if anything failed. Each suite
// cleans up after itself through TestHarness.Sandbox, so order shouldn't
// matter -- add new suites to the list below.
public static class AllTests
{
    static readonly (string name, Func<int> execute)[] Suites =
    {
        ("AccountCloudSaveTest", AccountCloudSaveTest.Execute),
        ("AccountSignInTest", AccountSignInTest.Execute),
        ("ArtRestyleTest", ArtRestyleTest.Execute),
        ("AchievementTiersTest", AchievementTiersTest.Execute),
        ("AsteroidBackwardsAndShopColumnsTest", AsteroidBackwardsAndShopColumnsTest.Execute),
        ("AsteroidColliderTest", AsteroidColliderTest.Execute),
        ("BackNavigationTest", BackNavigationTest.Execute),
        ("BossEncounterTest", BossEncounterTest.Execute),
        ("BossIntroTest", BossIntroTest.Execute),
        ("BugBatch0907Test", BugBatch0907Test.Execute),
        ("CameraFitTest", CameraFitTest.Execute),
        ("CinematicEarlyClearTest", CinematicEarlyClearTest.Execute),
        ("CloakShieldTest", CloakShieldTest.Execute),
        ("CodexTest", CodexTest.Execute),
        ("CreditsTest", CreditsTest.Execute),
        ("DeathPanelTest", DeathPanelTest.Execute),
        ("DeveloperModeTest", DeveloperModeTest.Execute),
        ("DifficultyRebalanceTest", DifficultyRebalanceTest.Execute),
        ("EnemyRosterTest", EnemyRosterTest.Execute),
        ("ExhaustSkinTest", ExhaustSkinTest.Execute),
        ("ExhaustStyleTest", ExhaustStyleTest.Execute),
        ("FrameRateBootstrapTest", FrameRateBootstrapTest.Execute),
        ("GreenAtomSizeTest", GreenAtomSizeTest.Execute),
        ("HeartsPlacementTest", HeartsPlacementTest.Execute),
        ("HomePauseTest", HomePauseTest.Execute),
        ("LaunchCountdownTest", LaunchCountdownTest.Execute),
        ("LeaderboardTest", LeaderboardTest.Execute),
        ("LoopTest", LoopTest.Execute),
        ("MissingScriptsTest", MissingScriptsTest.Execute),
        ("NextFeatures0907Test", NextFeatures0907Test.Execute),
        ("PauseGlowTest", PauseGlowTest.Execute),
        ("PickupHitchTest", PickupHitchTest.Execute),
        ("RailMineArtTest", RailMineArtTest.Execute),
        ("ResumeSlowMoTest", ResumeSlowMoTest.Execute),
        ("RosterCleanupTest", RosterCleanupTest.Execute),
        ("ScoringTest", ScoringTest.Execute),
        ("ScoreBonusTest", ScoreBonusTest.Execute),
        ("ShieldFitTest", ShieldFitTest.Execute),
        ("ShipArtTest", ShipArtTest.Execute),
        ("ShipAttacksTest", ShipAttacksTest.Execute),
        ("ShipLivesIndicatorTest", ShipLivesIndicatorTest.Execute),
        ("ShipNozzlesTest", ShipNozzlesTest.Execute),
        ("ShipSelectionTest", ShipSelectionTest.Execute),
        ("ShipSkinsTest", ShipSkinsTest.Execute),
        ("ShopHealthCarryoverTest", ShopHealthCarryoverTest.Execute),
        ("ShopTest", ShopTest.Execute),
        ("SpeedRampTest", SpeedRampTest.Execute),
        ("SpinWindTest", SpinWindTest.Execute),
        ("SplashLayoutTest", SplashLayoutTest.Execute),
        ("StarDustPersistenceTest", StarDustPersistenceTest.Execute),
        ("ThrusterTest", ThrusterTest.Execute),
        ("TallScreenTest", TallScreenTest.Execute),
        ("TitleScreenTrafficTest", TitleScreenTrafficTest.Execute),
        ("TutorialAtomFlowTest", TutorialAtomFlowTest.Execute),
        ("TutorialCurrencyTest", TutorialCurrencyTest.Execute),
        ("TutorialParityTest", TutorialParityTest.Execute),
        ("TutorialRobotTest", TutorialRobotTest.Execute),
        ("UnusedAssetGuardTest", UnusedAssetGuardTest.Execute),
        ("WeaponChargeTest", WeaponChargeTest.Execute),
        ("WorldBackdropTest", WorldBackdropTest.Execute),
        ("WorldLogicTest", WorldLogicTest.Execute),
    };

    public static void RunAll()
    {
        int total = 0;
        foreach (var suite in Suites)
        {
            int failures;
            try { failures = suite.execute(); }
            catch (Exception e)
            {
                // A throwing suite counts as one failure; keep going.
                Debug.LogError("[ALL] " + suite.name + " threw: " + e);
                failures = 1;
            }
            total += failures;
            Debug.Log("[ALL] " + suite.name + ": " + (failures == 0 ? "PASS" : "FAIL (" + failures + ")"));
        }

        Debug.Log("[ALL] " + Suites.Length + " suites, " + total + " failures");
        EditorApplication.Exit(total == 0 ? 0 : 1);
    }
}
