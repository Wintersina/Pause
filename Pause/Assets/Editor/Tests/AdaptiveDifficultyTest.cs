using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// Adaptive spawn density (docs/adaptive-difficulty.md).
//   1  PerformanceMeter: rises with an unbroken chain and clean time, falls on hits, slew-limited,
//      frozen during authored beats, reset on run start / revive, deterministic, world change blends to neutral
//   2  multiplier bounds, held = neutral, dust never under its floor, recovery only when struggling
//   3  spawners consult the multiplier (enemy Roll, rails excluded); elites/bosses/held ignore it
//   4  tutorial / Paragon / flag off = no effect
public static class AdaptiveDifficultyTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ADAPT] PASS  " : "[ADAPT] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        EnemyDensity.Disabled = true;
        try
        {
            MeterBehaviour();
            Bounds();
            SpawnersConsult();
            GatesOff();
        }
        finally
        {
            AdaptiveDifficulty.Reset();
            FeatureFlags.Set(FeatureFlags.AdaptiveDifficulty, true);
            LoopDifficulty.Reset();
            BossEncounter.ResetRun();
        }
        Debug.Log("[ADAPT] failures: " + fails);
        return fails;
    }

    // A well-played minute: a kill each second, chain 8, no hits.
    static PerformanceMeter Good(float seconds)
    {
        var m = new PerformanceMeter();
        for (float t = 0; t < seconds; t += 1f) { m.OnKill(); m.Tick(1f, 8, 3, 3); }
        return m;
    }

    static void MeterBehaviour()
    {
        var good = Good(60f);
        Check("unbroken chain + clean time lifts the flow (" + good.Flow.ToString("F2") + ")", good.Flow > .5f);

        var idle = new PerformanceMeter();
        for (int i = 0; i < 60; i++) idle.Tick(1f, 0, 3, 3);
        Check("flying peacefully (no kills, no hits) stays neutral", Mathf.Abs(idle.Flow) < .01f);

        var hurt = Good(60f);
        float before = hurt.Flow;
        hurt.OnHit(); hurt.OnHit();
        for (int i = 0; i < 20; i++) hurt.Tick(1f, 0, 1, 3);
        Check("hits drop the flow (" + before.ToString("F2") + " -> " + hurt.Flow.ToString("F2") + ")", hurt.Flow < before - .3f);
        for (int i = 0; i < 60; i++) hurt.Tick(1f, 0, 1, 3);
        Check("struggling (hits, last heart) goes negative", hurt.Flow < -.2f);

        // slew limit: no step larger than MaxFall / MaxRise per second
        var m = new PerformanceMeter();
        float last = 0f; bool slewOk = true;
        for (int i = 0; i < 400; i++)
        {
            if (i % 3 == 0) m.OnKill();
            if (i == 200) { m.OnHit(); m.OnHit(); m.OnHit(); }
            m.Tick(.1f, i < 200 ? 8 : 0, i < 200 ? 3 : 1, 3);
            float step = Mathf.Abs(m.Flow - last);
            if (step > PerformanceMeter.MaxFall * .1f + 1e-5f) slewOk = false;
            last = m.Flow;
        }
        Check("flow never moves faster than the slew limit", slewOk);

        // frozen: nothing changes, events ignored
        var f = Good(40f);
        float held = f.Flow, clock = f.Clock;
        f.Frozen = true;
        f.OnHit(); f.OnHit();
        for (int i = 0; i < 50; i++) f.Tick(1f, 0, 1, 3);
        Check("frozen during an authored beat: value, clock and hits stand still", f.Flow == held && f.Clock == clock);
        f.Frozen = false;
        f.Tick(1f, 8, 3, 3);
        Check("... and resumes without the frozen hits counting", f.Flow >= held - .01f);

        var r = Good(40f);
        r.OnRevive();
        Check("revive resets to neutral", r.Flow == 0f && r.Clock == 0f);
        var w = Good(60f);
        float wv = w.Flow;
        w.OnWorldChange();
        Check("a world change blends toward neutral (" + wv.ToString("F2") + " -> " + w.Flow.ToString("F2") + ")", Mathf.Approximately(w.Flow, wv * PerformanceMeter.WorldBlend));

        var a = Good(45f); var b = Good(45f);
        Check("deterministic: same events, same flow", a.Flow == b.Flow && a.Raw == b.Raw);
        Check("flow stays within [-1, 1]", Mathf.Abs(Good(600f).Flow) <= 1f);
    }

    static void Bounds()
    {
        bool enemies = true, dust = true, rec = true, held = true;
        for (float fl = -1.5f; fl <= 1.5f; fl += .01f)
        {
            float e = AdaptiveDifficulty.EnemyScaleFor(fl, false), d = AdaptiveDifficulty.DustScaleFor(fl, false), r = AdaptiveDifficulty.RecoveryScaleFor(fl, false);
            if (e < AdaptiveDifficulty.EnemyMin - 1e-5f || e > AdaptiveDifficulty.EnemyMax + 1e-5f) enemies = false;
            if (d < AdaptiveDifficulty.DustMin - 1e-5f || d > AdaptiveDifficulty.DustMax + 1e-5f) dust = false;
            if (r < 1f || r > AdaptiveDifficulty.RecoveryMax + 1e-5f || (fl >= 0f && r != 1f)) rec = false;
            if (AdaptiveDifficulty.EnemyScaleFor(fl, true) != 1f || AdaptiveDifficulty.DustScaleFor(fl, true) != 1f || AdaptiveDifficulty.RecoveryScaleFor(fl, true) != 1f) held = false;
        }
        Check("enemy multiplier stays in [" + AdaptiveDifficulty.EnemyMin + ", " + AdaptiveDifficulty.EnemyMax + "]", enemies);
        Check("dust multiplier stays in [" + AdaptiveDifficulty.DustMin + ", " + AdaptiveDifficulty.DustMax + "] (economy floor .85)", dust && AdaptiveDifficulty.DustMin >= .85f);
        Check("recovery help only when struggling, capped at " + AdaptiveDifficulty.RecoveryMax, rec);
        Check("authored beats (held) read neutral", held);
        Check("neutral flow is exactly x1", AdaptiveDifficulty.EnemyScaleFor(0f, false) == 1f && AdaptiveDifficulty.DustScaleFor(0f, false) == 1f);
        Check("farming hits never beats playing well: best ease still spawns >= x" + AdaptiveDifficulty.EnemyMin + " and pays 1 dust per kill",
              AdaptiveDifficulty.EnemyScaleFor(-1f, false) >= .75f && AdaptiveDifficulty.DustScaleFor(-1f, false) < AdaptiveDifficulty.DustScaleFor(1f, false));
    }

    static void SpawnersConsult()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        buttonClicks.playerDied = false;
        var go = new GameObject("~board");
        var board = go.AddComponent<enmiesOnBoard>();
        var roll = typeof(enmiesOnBoard).GetMethod("Roll", Inst);
        var rollWith = typeof(enmiesOnBoard).GetMethod("RollWith", Inst);
        var range = new Vector2(12f, 12f);
        AdaptiveDifficulty.Reset();
        float neutral = (float)roll.Invoke(board, new object[] { range });

        AdaptiveDifficulty.RunActive = true;
        AdaptiveDifficulty.HeldOverride = false;
        AdaptiveDifficulty.Meter.Force(1f);
        float hi = (float)roll.Invoke(board, new object[] { range });
        AdaptiveDifficulty.Meter.Force(-1f);
        float lo = (float)roll.Invoke(board, new object[] { range });
        Check("enemy Roll divides by the multiplier (" + lo.ToString("F2") + " / " + neutral.ToString("F2") + " / " + hi.ToString("F2") + ")",
              Mathf.Approximately(hi, neutral / AdaptiveDifficulty.EnemyMax) && Mathf.Approximately(lo, neutral / AdaptiveDifficulty.EnemyMin));
        float rail = (float)rollWith.Invoke(board, new object[] { range, false });
        Check("rails are not scaled", Mathf.Approximately(rail, neutral));

        AdaptiveDifficulty.HeldOverride = true;   // a boss / elite beat
        float heldRoll = (float)roll.Invoke(board, new object[] { range });
        Check("during a boss / elite beat the Roll is the plain one", Mathf.Approximately(heldRoll, neutral));
        AdaptiveDifficulty.HeldOverride = null;
        Check("EliteFight and BossEncounter feed Held", HeldReadsBeats());

        // star dust cadence consults the multiplier
        AdaptiveDifficulty.HeldOverride = false;
        AdaptiveDifficulty.Meter.Force(1f);
        Check("star dust timers run x" + AdaptiveDifficulty.DustMax + " at full flow", Mathf.Approximately(AdaptiveDifficulty.DustScale, AdaptiveDifficulty.DustMax));
        Object.DestroyImmediate(go);
        AdaptiveDifficulty.Reset();
    }

    static bool HeldReadsBeats()
    {
        AdaptiveDifficulty.HeldOverride = null;
        EliteFight.ActiveOverride = true;
        bool elite = AdaptiveDifficulty.Held;
        EliteFight.ActiveOverride = null;
        BossEncounter.ResetRun();
        bool boss = BossEncounter.Begin(0, null) && AdaptiveDifficulty.Held;
        BossEncounter.ResetRun();
        return elite && boss;
    }

    static void GatesOff()
    {
        AdaptiveDifficulty.Reset();
        AdaptiveDifficulty.BeginRun(false, false);
        AdaptiveDifficulty.HeldOverride = false;
        AdaptiveDifficulty.Meter.Force(1f);
        Check("tutorial / practice run (no real dust): off", !AdaptiveDifficulty.On && AdaptiveDifficulty.EnemyScale == 1f && AdaptiveDifficulty.DustScale == 1f);
        AdaptiveDifficulty.BeginRun(true, true);
        Check("premium Paragon ship: off", !AdaptiveDifficulty.RunActive);
        AdaptiveDifficulty.BeginRun(true, false);
        AdaptiveDifficulty.HeldOverride = false;
        AdaptiveDifficulty.Meter.Force(1f);
        Check("a normal run: on by default", FeatureFlags.DefaultOf(FeatureFlags.AdaptiveDifficulty) && AdaptiveDifficulty.On && AdaptiveDifficulty.EnemyScale > 1f);
        FeatureFlags.Set(FeatureFlags.AdaptiveDifficulty, false);
        Check("flag off: everything neutral", AdaptiveDifficulty.EnemyScale == 1f && AdaptiveDifficulty.DustScale == 1f && AdaptiveDifficulty.LaneFollow == 0f);
        FeatureFlags.Set(FeatureFlags.AdaptiveDifficulty, true);
        AdaptiveDifficulty.BeginRun(true, false);
        Check("a new run resets the flow", AdaptiveDifficulty.Meter.Flow == 0f);
    }
}
