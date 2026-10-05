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
//   - over a whole stock run it averages about 45% (the run's reference ramp
//     now holds at the cap, HUD 35, for its last 9 s; the recorded "before"
//     run peaked at 37.8. HUD 40 and 46 in the table are limit-break speeds)
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
                EnemyDensityProbe.SetAuthoredView();   // the 10 u view the recorded baseline was measured in
                Table();
                PhoneTables();
            }
        }
        finally
        {
            EnemyThreat.ForceShooting = false;
            EnemyDensityProbe.RestoreView();
            EnemyDensity.Disabled = false;
            SpawnSpace.ClockOverride = null;
            EnemyDensityProbe.Clear();
            moveBackGround.speed = 0f;
        }
        Debug.Log("[DENSITY] failures: " + fails);
        return fails;
    }

    // The same table in the views phones really get (CameraFit: 13.2 u tall
    // at 1080x1920, 17.4 u at 1080x2520). The original spawner was never
    // measured there; every enemy then rode the board, so its bodies in view
    // scale with the view's height, and "before" is the recorded number
    // times that. Pilots do not scale (they are capped, not rained).
    static void PhoneTables()
    {
        foreach (var shape in new[] { new Vector2Int(1080, 1920), new Vector2Int(1080, 2520) })
        {
            EnemyDensityProbe.SetView(shape.x, shape.y);
            float k = EnemyDensityProbe.ViewHalfHeight / EnemyDensityProbe.AuthoredHalfHeight;
            var points = EnemyDensityProbe.HudPoints;
            string tag = shape.x + "x" + shape.y;
            bool fewer = true, shooting = true, pilots = true, bounded = true;
            float lowCut = 1f, highCut = 0f;
            for (int i = 0; i < points.Length; i++)
            {
                var s = EnemyDensityProbe.Pinned(points[i], EnemyDensityProbe.LevelSecondFor(points[i]), 3);
                float before = BeforeOnScreen[i] * k, cut = 1f - s.Threats / before;
                Debug.Log(string.Format("[DENSITY] PHONE {0} (view {1:F1} u) hud {2,2} | before (scaled) {3,5:F2} | now {4,5:F2} + {5:F2} shots = {6,5:F2} | cut {7:P0} | pilots in view {8:F2}, in view {9:F1}s | peak {10:F0} of ceiling {11:F1}",
                                        tag, 2f * EnemyDensityProbe.ViewHalfHeight, points[i], before, s.onScreen, s.shots, s.Threats, cut, s.pilots,
                                        s.inViewSeconds, s.peakOnScreen, EnemyDensity.MaxThreats(points[i])));
                fewer &= cut > .05f;
                if (points[i] <= 10) lowCut = Mathf.Min(lowCut, cut);
                if (points[i] >= 30) highCut = Mathf.Max(highCut, cut);
                if (points[i] >= 20) { shooting &= s.shots >= .25f; pilots &= s.pilots >= .8f; }
                bounded &= s.inViewSeconds >= 3f && s.inViewSeconds <= 13f;
            }
            var run = EnemyDensityProbe.WholeRun(EnemyDensityProbe.ReferenceHudPerSecond, SpeedRamp.CapHud, 3);
            float runCut = 1f - run.Threats / (BeforeRunOnScreen * k);
            Debug.Log(string.Format("[DENSITY] PHONE {0} whole run | before (scaled) {1:F2} | now {2:F2} + {3:F2} shots = {4:F2} | cut {5:P0} | pilots in view {6:F2}, in view {7:F1}s",
                                    tag, BeforeRunOnScreen * k, run.onScreen, run.shots, run.Threats, runCut, run.pilots, run.inViewSeconds));
            Check(tag + ": fewer threats than the original at every speed, cut least at low speed", fewer && lowCut < highCut);
            Check(tag + string.Format(": a whole run is about 45% below the original ({0:P0}; 35% to 58%)", runCut), runCut >= .35f && runCut <= .58f);
            Check(tag + ": from HUD 20 up pilots are on screen (0.8+) and shots are in flight (0.25+)", pilots && shooting);
            Check(tag + ": a departing pilot was in view 3 to 13 s on average, as in the authored view", bounded);
        }
        EnemyDensityProbe.SetAuthoredView();
    }

    static void Tunables()
    {
        Check("the spawn rate is cut least at low speed and most at high speed (x" + EnemyDensity.RateScale(5f) + " at HUD 5, x" +
              EnemyDensity.RateScale(35f) + " at 35)",
              EnemyDensity.RateScale(5f) > EnemyDensity.RateScale(20f) && EnemyDensity.RateScale(20f) > EnemyDensity.RateScale(35f) &&
              EnemyDensity.RateScale(5f) <= 1f + 1e-4f && EnemyDensity.RateScale(5f) >= .7f && EnemyDensity.RateScale(35f) <= .6f);
        Check("past the high-speed end it holds (no further cut at the cap)",
              Mathf.Approximately(EnemyDensity.RateScale(35f), EnemyDensity.RateScale(50f)) &&
              Mathf.Approximately(EnemyDensity.RateScale(0f), EnemyDensity.RateScale(5f)));
        Check("the threat ceiling does not rise with speed", EnemyDensity.MaxThreats(35f) <= EnemyDensity.MaxThreats(5f));
        // The speed cap: anything above HUD 35 is a limit break (the boost, at
        // most 45). It fields exactly what 35 fields.
        bool flat = true;
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
            foreach (float hud in new[] { 36f, 40f, 45f, 46f })
                flat &= Mathf.Approximately(EnemyDensity.RateScale(hud), EnemyDensity.RateScale(35f)) &&
                        Mathf.Approximately(EnemyDensity.MaxThreats(hud), EnemyDensity.MaxThreats(35f)) &&
                        Mathf.Approximately(EnemyDensity.MaxPilotLoad(hud, w), EnemyDensity.MaxPilotLoad(35f, w)) &&
                        EnemyDensity.MaxChasers(hud) == EnemyDensity.MaxChasers(35f);
        Check("a limit break (HUD 36-46) fields what the cap fields: rate, threat ceiling, pilot load, chasers", flat);
        EnemyDensity.Disabled = true;
        Check("switched off, it is the old spawner (rate x1, no ceiling)", EnemyDensity.RateScale(35f) == 1f && EnemyDensity.RoomFor(35f));
        EnemyDensity.Disabled = false;
        Check("a projectile counts as a fraction of a body", EnemyThreat.ShotWeight > 0f && EnemyThreat.ShotWeight < 1f);
    }

    static void Table()
    {
        var points = EnemyDensityProbe.HudPoints;
        var cut = new float[points.Length];
        var pilotsAt = new float[points.Length];
        var shotsAt = new float[points.Length];
        var stayAt = new float[points.Length];
        float peakThreats = 0f;
        Debug.Log("[DENSITY] HUD | level s | before: spawns/s, on screen | after: spawns/s, on screen, shots, threats | cut");
        for (int i = 0; i < points.Length; i++)
        {
            var s = EnemyDensityProbe.Pinned(points[i], EnemyDensityProbe.LevelSecondFor(points[i]), 3);
            cut[i] = 1f - s.Threats / BeforeOnScreen[i];
            peakThreats = Mathf.Max(peakThreats, s.peakOnScreen);
            Debug.Log(string.Format("[DENSITY] TABLE hud {0,2} | t {1,5:F1} | before {2,5:F2}/s {3,5:F2} | after {4,5:F2}/s {5,5:F2} + {6:F2} shots = {7,5:F2} | cut {8:P0} | pilots in view {9:F2}, left {10:F0}, engaged {11:F1}s, in view {12:F1}s",
                                    points[i], EnemyDensityProbe.LevelSecondFor(points[i]), BeforeSpawnsPerSecond[i], BeforeOnScreen[i],
                                    s.spawnsPerSecond, s.onScreen, s.shots, s.Threats, cut[i], s.pilots, s.pilotsDeparted, s.engageSeconds, s.inViewSeconds));
            pilotsAt[i] = s.pilots; shotsAt[i] = s.shots; stayAt[i] = s.inViewSeconds;
        }
        var run = EnemyDensityProbe.WholeRun(EnemyDensityProbe.ReferenceHudPerSecond, SpeedRamp.CapHud, 3);
        float runCut = 1f - run.Threats / BeforeRunOnScreen;
        Debug.Log(string.Format("[DENSITY] TABLE whole run | before {0:F2}/s {1:F2} | after {2:F2}/s {3:F2} + {4:F2} shots = {5:F2} | cut {6:P0} | pilots in view {7:F2}, left {8:F0}, engaged {9:F1}s, in view {10:F1}s",
                                BeforeRunSpawnsPerSecond, BeforeRunOnScreen, run.spawnsPerSecond, run.onScreen, run.shots, run.Threats, runCut,
                                run.pilots, run.pilotsDeparted, run.engageSeconds, run.inViewSeconds));

        bool fewer = true;
        for (int i = 0; i < cut.Length; i++) fewer &= cut[i] > .05f;
        Check("fewer threats than before at every speed", fewer);
        Check(string.Format("low speed is cut least: HUD 5 {0:P0}, HUD 10 {1:P0} (under 45%, the early game is not empty)", cut[0], cut[1]),
              cut[0] < .45f && cut[1] < .45f);
        Check(string.Format("high speed is cut most: HUD 30 {0:P0}, 35 {1:P0}, limit break 40 {2:P0}, 46 {3:P0} (each at least 45%)", cut[3], cut[4], cut[5], cut[6]),
              cut[3] >= .45f && cut[4] >= .45f && cut[5] >= .45f && cut[6] >= .45f);
        Check("the cut grows with speed (HUD 5 < HUD 20 < HUD 35)", cut[0] < cut[2] && cut[2] < cut[4]);
        Check(string.Format("a whole stock run averages about 45% fewer threats ({0:P0}; 38% to 55%)", runCut), runCut >= .38f && runCut <= .55f);
        Check("the early game still has company: at least 4 enemies in view on average at HUD 5 and 10",
              BeforeOnScreen[0] * (1f - cut[0]) >= 4f && BeforeOnScreen[1] * (1f - cut[1]) >= 4f);
        Check("the board never holds more bodies than the threat ceiling allows (peak " + peakThreats + ")",
              peakThreats <= EnemyDensity.MaxThreats(0f) + 2f);

        // pilots: they stay, they are few, and they get to shoot at any speed
        bool some = true, capped = true, shooting = true, bounded = true;
        for (int i = 0; i < points.Length; i++)
        {
            some &= pilotsAt[i] >= .3f;
            capped &= pilotsAt[i] <= EnemyDensity.MaxPilotLoad(points[i], 0) / .5f + EnemyDensity.MaxChasers(points[i]);
            if (points[i] >= 20) { shooting &= shotsAt[i] >= .25f; some &= pilotsAt[i] >= .8f; }
            bounded &= stayAt[i] >= 3f && stayAt[i] <= 13f;
        }
        Check("pilots are on screen at every speed (0.3+ on average early, 0.8+ from HUD 20) and never past their cap", some && capped);
        Check("at HUD 20 and above, 35 and the caps included, enemy shots are in flight (0.25+ on average): shooters get to shoot at speed", shooting);
        Check("a pilot that flies off has been in view 3 to 13 s on average at every speed (whole run " + run.inViewSeconds.ToString("F1") + " s)",
              bounded && run.inViewSeconds >= 3f && run.inViewSeconds <= 13f);
    }
}
