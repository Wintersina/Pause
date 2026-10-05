using UnityEditor.SceneManagement;
using UnityEngine;

// The 2026-10 density cut (EnemyDensity): "overall enemy density reduced by
// about 45%, especially at higher speeds".
//
// Measured with EnemyDensityProbe -- the real spawner stepped headless,
// every enemy on its real mover and brain, shots flying -- against the
// numbers the same probe gave before the cut (Before*, recorded from the
// pre-change spawner at b377b1c6). "Threats" = enemies in the view plus
// half a threat per live hostile projectile, so shooters' shots count.
//
//   - at every speed there are fewer threats than before
//   - the cut is smaller at low speed (the early game is not empty) and
//     larger at high speed
//   - over a whole stock run it averages about 45%
//   - the tunables behave: the rate scale and the threat ceiling fall with
//     speed, and the ceiling is never passed
public static class EnemyDensityTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[DENSITY] PASS  " : "[DENSITY] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    // The probe's numbers before the cut: mean enemies in the view at each
    // EnemyDensityProbe.HudPoints moment, and over a whole 120 s run.
    public static readonly float[] BeforeOnScreen = { 6.29f, 6.61f, 10.06f, 15.20f, 19.64f, 17.32f, 15.14f };
    public static readonly float[] BeforeSpawnsPerSecond = { 1.00f, 2.03f, 6.08f, 14.08f, 21.17f, 21.26f, 21.17f };
    public const float BeforeRunOnScreen = 9.99f, BeforeRunSpawnsPerSecond = 7.44f;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            Tunables();
            if (TestHarness.Slow("density table at representative speeds (headless spawner runs)"))
            {
                EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
                EnemyThreat.ForceShooting = true;
                Table();
            }
        }
        finally
        {
            EnemyThreat.ForceShooting = false;
            EnemyDensity.Disabled = false;
            SpawnSpace.ClockOverride = null;
            EnemyDensityProbe.Clear();
            moveBackGround.speed = 0f;
        }
        Debug.Log("[DENSITY] failures: " + fails);
        return fails;
    }

    static void Tunables()
    {
        Check("the spawn rate is cut least at low speed and most at high speed (x" + EnemyDensity.RateScale(5f) + " at HUD 5, x" +
              EnemyDensity.RateScale(35f) + " at 35)",
              EnemyDensity.RateScale(5f) > EnemyDensity.RateScale(20f) && EnemyDensity.RateScale(20f) > EnemyDensity.RateScale(35f) &&
              EnemyDensity.RateScale(5f) <= 1f && EnemyDensity.RateScale(5f) >= .7f && EnemyDensity.RateScale(35f) <= .6f);
        Check("past the high-speed end it holds (no further cut at the cap)",
              Mathf.Approximately(EnemyDensity.RateScale(35f), EnemyDensity.RateScale(50f)) &&
              Mathf.Approximately(EnemyDensity.RateScale(0f), EnemyDensity.RateScale(5f)));
        Check("the threat ceiling does not rise with speed", EnemyDensity.MaxThreats(35f) <= EnemyDensity.MaxThreats(5f));
        EnemyDensity.Disabled = true;
        Check("switched off, it is the old spawner (rate x1, no ceiling)", EnemyDensity.RateScale(35f) == 1f && EnemyDensity.RoomFor(35f));
        EnemyDensity.Disabled = false;
        Check("a projectile counts as a fraction of a body", EnemyThreat.ShotWeight > 0f && EnemyThreat.ShotWeight < 1f);
    }

    static void Table()
    {
        var points = EnemyDensityProbe.HudPoints;
        var cut = new float[points.Length];
        float peakThreats = 0f;
        Debug.Log("[DENSITY] HUD | level s | before: spawns/s, on screen | after: spawns/s, on screen, shots, threats | cut");
        for (int i = 0; i < points.Length; i++)
        {
            var s = EnemyDensityProbe.Pinned(points[i], EnemyDensityProbe.LevelSecondFor(points[i]), 3);
            cut[i] = 1f - s.Threats / BeforeOnScreen[i];
            peakThreats = Mathf.Max(peakThreats, s.peakOnScreen);
            Debug.Log(string.Format("[DENSITY] TABLE hud {0,2} | t {1,5:F1} | before {2,5:F2}/s {3,5:F2} | after {4,5:F2}/s {5,5:F2} + {6:F2} shots = {7,5:F2} | cut {8:P0}",
                                    points[i], EnemyDensityProbe.LevelSecondFor(points[i]), BeforeSpawnsPerSecond[i], BeforeOnScreen[i],
                                    s.spawnsPerSecond, s.onScreen, s.shots, s.Threats, cut[i]));
        }
        var run = EnemyDensityProbe.WholeRun(EnemyDensityProbe.ReferenceHudPerSecond, 46f, 3);
        float runCut = 1f - run.Threats / BeforeRunOnScreen;
        Debug.Log(string.Format("[DENSITY] TABLE whole run | before {0:F2}/s {1:F2} | after {2:F2}/s {3:F2} + {4:F2} shots = {5:F2} | cut {6:P0}",
                                BeforeRunSpawnsPerSecond, BeforeRunOnScreen, run.spawnsPerSecond, run.onScreen, run.shots, run.Threats, runCut));

        bool fewer = true;
        for (int i = 0; i < cut.Length; i++) fewer &= cut[i] > .05f;
        Check("fewer threats than before at every speed", fewer);
        Check(string.Format("low speed is cut least: HUD 5 {0:P0}, HUD 10 {1:P0} (under 45%, the early game is not empty)", cut[0], cut[1]),
              cut[0] < .45f && cut[1] < .45f);
        Check(string.Format("high speed is cut most: HUD 30 {0:P0}, 35 {1:P0}, 40 {2:P0}, 46 {3:P0} (each at least 45%)", cut[3], cut[4], cut[5], cut[6]),
              cut[3] >= .45f && cut[4] >= .45f && cut[5] >= .45f && cut[6] >= .45f);
        Check("the cut grows with speed (HUD 5 < HUD 20 < HUD 35)", cut[0] < cut[2] && cut[2] < cut[4]);
        Check(string.Format("a whole stock run averages about 45% fewer threats ({0:P0}; 38% to 55%)", runCut), runCut >= .38f && runCut <= .55f);
        Check("the early game still has company: at least 4 enemies in view on average at HUD 5 and 10",
              BeforeOnScreen[0] * (1f - cut[0]) >= 4f && BeforeOnScreen[1] * (1f - cut[1]) >= 4f);
        Check("the board never holds more bodies than the threat ceiling allows (peak " + peakThreats + ")",
              peakThreats <= EnemyDensity.MaxThreats(0f) + 2f);
    }
}
