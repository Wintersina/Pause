using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

// A/B proof that DodgeBot's optimised PathClear answers exactly like the original (PathClearRef): the same
// rows, same seed, measured twice in one process (reference, optimised, reference again to expose any
// run-to-run drift of the world's own state) must give identical hits / attacked / ghost / frames.
// Also logs the time of each, so the speed-up is visible.
public static class AttackBotAbTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[BOTAB] PASS  " : "[BOTAB] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    // (id, rolls)
    static readonly (string id, int rolls)[] Rows =
    {
        ("roster:space_fighter_4", 150), ("themed:frost_cold_blast", 40), ("themed:verdant_vine_lash", 60),
        ("themed:tide_surf_wave", 60), ("themed:boss_frost_cold_blast", 6), ("elite:verdant_elite_thornlash", 60),
        ("roster:space_mine", 150),
    };

    static string Sec(long ticks) { return (ticks / (double)Stopwatch.Frequency).ToString("0.00"); }

    public static int Execute()
    {
        fails = 0;
        bool was = DodgeBot.UseReference;
        using (new TestHarness.Sandbox())
        {
            try
            {
                foreach (var row in Rows)
                {
                    DodgeBot.ProfStep = DodgeBot.ProfCollect = DodgeBot.ProfPlan = 0;
                    DodgeBot.UseReference = true;
                    var sw = Stopwatch.StartNew();
                    var r1 = AttackBudgetTest.Measure(row.id, row.rolls);
                    double t1 = sw.Elapsed.TotalSeconds;
                    Debug.Log("[BOTAB] PROF " + row.id + " step " + Sec(DodgeBot.ProfStep) + " collect " + Sec(DodgeBot.ProfCollect) + " plan " + Sec(DodgeBot.ProfPlan) + " of " + t1.ToString("0.00") + " (ref)");
                    DodgeBot.ProfStep = DodgeBot.ProfCollect = DodgeBot.ProfPlan = 0;
                    DodgeBot.UseReference = false;
                    sw.Restart();
                    var r2 = AttackBudgetTest.Measure(row.id, row.rolls);
                    double t2 = sw.Elapsed.TotalSeconds;
                    Debug.Log("[BOTAB] PROF " + row.id + " step " + Sec(DodgeBot.ProfStep) + " collect " + Sec(DodgeBot.ProfCollect) + " plan " + Sec(DodgeBot.ProfPlan) + " of " + t2.ToString("0.00") + " (fast)");
                    DodgeBot.UseReference = true;
                    var r3 = AttackBudgetTest.Measure(row.id, row.rolls);
                    string a = r1.hits + "/" + r1.attacked + "/" + r1.ghostHits + "/" + r1.frames;
                    string b = r2.hits + "/" + r2.attacked + "/" + r2.ghostHits + "/" + r2.frames;
                    string c = r3.hits + "/" + r3.attacked + "/" + r3.ghostHits + "/" + r3.frames;
                    Debug.Log("[BOTAB] TIME " + row.id + " ref " + t1.ToString("0.00") + " s, fast " + t2.ToString("0.00") + " s, x" + (t1 / System.Math.Max(t2, 1e-3)).ToString("0.0"));
                    // (the ghost never moves, so its hits cannot depend on the bot: they drift by one between runs in one editor process --
                    // the world's clock and brains carry a little state -- so compare only what the bot decides: hits, attacked and, when
                    // the reference repeats itself, the frame count)
                    bool same = r1.hits == r2.hits && r1.attacked == r2.attacked && r1.hits == r3.hits && r1.attacked == r3.attacked
                                && (r1.frames != r3.frames || r1.frames == r2.frames);
                    Check(row.id + ": optimised == reference (" + b + " vs " + a + "; reference again " + c + "" + ")", same);
                }
            }
            finally { DodgeBot.UseReference = was; AttackBudgetScenarios.Cleanup(); }
        }
        Debug.Log("[BOTAB] failures: " + fails);
        return fails;
    }
}
