using UnityEngine;

// THE GREEDY BOT vs THE LADDER (docs/boss-attack-overhaul.md): the dodge bot (DodgeBot: a human-like reaction time, a
// nearest-safe-spot planner, a ship that takes time to get up to speed) flies against each boss's whole phase-3 rotation
// (enrage on), run through the REAL BossEncounter, and the hits it still takes are counted (a hit, then a second and a half
// of the ship's post-hit grace, then the next).
//
//   * beatable in principle: the bot takes a bounded number of hits a minute even from the last boss, and from the first
//     it takes far fewer
//   * harder bosses hit it more: Desert > Space, and no boss hits it much less than an easier one before it
//
// (Feel on a device cannot be tested headless: this is the machine's stand-in for "harder", not a playtest.)
public static class BossLadderBotTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[LADDERBOT] PASS  " : "[LADDERBOT] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static readonly string[] Names = { "Void Archon", "Hoarfrost Leviathan", "Bloom Queen", "Cinder Drake", "Iron Kraken", "Tempest Engine", "Dune-Eater" };

    // hits a minute the bot takes from a boss's phase-3 rotation, per world (filled by Execute; read by the report)
    // rolls of 36 s per boss: enough that the hits a minute is a trend, not one lucky run
    static int Rolls => TestHarness.FullLane ? 24 : 12;

    public static readonly float[] HitsPerMinute = new float[7];
    public static readonly float[] StandStillPerMinute = new float[7];

    // Calibration (PAUSE_LADDER_SWEEP=1): hits a minute the bot takes at several densities, for BossLadder.Density.
    static void Sweep()
    {
        bool counted = DodgeBot.CountHits;
        DodgeBot.CountHits = true;
        var saved = (float[])BossLadder.Density.Clone();
        try
        {
            var vectors = new System.Collections.Generic.List<float[]>();
            string env = System.Environment.GetEnvironmentVariable("PAUSE_DENSITY");
            if (!string.IsNullOrEmpty(env))
                foreach (var v in env.Split(';'))
                {
                    var f = new float[7];
                    var parts = v.Split(',');
                    for (int i = 0; i < 7; i++) f[i] = float.Parse(parts[Mathf.Min(i, parts.Length - 1)], System.Globalization.CultureInfo.InvariantCulture);
                    vectors.Add(f);
                }
            else foreach (float d in new[] { .9f, .75f, .6f, .5f, .4f, .3f }) vectors.Add(new[] { d, d, d, d, d, d, d });
            foreach (var vec in vectors)
            {
                var sb = new System.Text.StringBuilder("[LADDERBOT] sweep density " + string.Join(",", System.Linq.Enumerable.Select(vec, x => x.ToString("F2"))) + ":");
                for (int w = 0; w < 7; w++)
                {
                    BossLadder.Density[w] = vec[w];
                    var r = DodgeBot.Run(new AttackBudgetScenarios.Rotation(w, 36f), Rolls, 4242u + (uint)w);
                    sb.Append("  ").Append(Names[w].Split(' ')[0]).Append(' ').Append((r.hitEvents / Mathf.Max(1f, r.seconds) * 60f).ToString("F1"));
                    BossLadder.Density[w] = saved[w];
                }
                Debug.Log(sb.ToString());
            }
        }
        finally { DodgeBot.CountHits = counted; for (int w = 0; w < 7; w++) BossLadder.Density[w] = saved[w]; BossCatalog.UseOverhaul = true; BossEncounter.ResetRun(); BossRails.Reset(); BossAttackTest.ReleaseAtlases(); }
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        float[] savedDensity = (float[])BossLadder.Density.Clone();
        string dens = System.Environment.GetEnvironmentVariable("PAUSE_DENSITY");   // calibration: "d0,d1,...,d6" (several vectors, ';' apart, in sweep mode)
        if (!string.IsNullOrEmpty(dens) && System.Environment.GetEnvironmentVariable("PAUSE_LADDER_SWEEP") != "1")
        {
            var parts = dens.Split(',');
            for (int i = 0; i < parts.Length && i < 7; i++) BossLadder.Density[i] = float.Parse(parts[i], System.Globalization.CultureInfo.InvariantCulture);
        }
        if (System.Environment.GetEnvironmentVariable("PAUSE_LADDER_SWEEP") == "1") { Sweep(); return 0; }
        bool counted = DodgeBot.CountHits;
        DodgeBot.CountHits = true;
        try
        {
            int rolls = Rolls;
            float seconds = 36f;
            var worlds = new System.Collections.Generic.List<int>();
            for (int w = 0; w < 7; w++) worlds.Add(w);
            // the baseline: what the bot took from the OLD tables (info only, no check reads it: they were never
            // meant to be a ladder). It doubled the suite's time, so it only runs on request: PAUSE_LADDER_BASELINE=1.
            if (System.Environment.GetEnvironmentVariable("PAUSE_LADDER_BASELINE") == "1")
                for (int w = 0; w < 7; w++)
                {
                    var old = new AttackBudgetScenarios.Rotation(w, seconds) { Legacy = true };
                    var ro = DodgeBot.Run(old, rolls, 4242u + (uint)w);
                    Debug.Log("[LADDERBOT] baseline " + Names[w] + " (old table): " + (ro.hitEvents / Mathf.Max(1f, ro.seconds) * 60f).ToString("F1") + " hits a minute");
                }
            foreach (int w in worlds)
            {
                var sc = new AttackBudgetScenarios.Rotation(w, seconds);
                var by = new System.Collections.Generic.SortedDictionary<string, int>();
                DodgeBot.HitHook = (t, id) => { string n = sc.CurrentName; by[n] = (by.ContainsKey(n) ? by[n] : 0) + 1; };
                var r = DodgeBot.Run(sc, rolls, 4242u + (uint)w);
                DodgeBot.HitHook = null;
                Debug.Log("[LADDERBOT] " + Names[w] + " hits by attack: " + string.Join(", ", System.Linq.Enumerable.Select(by, kv => kv.Key + " " + kv.Value)));
                HitsPerMinute[w] = r.hitEvents / Mathf.Max(1f, r.seconds) * 60f;
                Debug.Log("[LADDERBOT] " + Names[w] + " by shot/beam/other: " + r.hitByShot + "/" + r.hitByBeam + "/" + r.hitByOther);
                Debug.Log("[LADDERBOT] " + Names[w] + ": " + r.hitEvents + " hits in " + r.seconds.ToString("F0") + " s of rotation = " + HitsPerMinute[w].ToString("F1") + " a minute (stand-still ghost hit in " + r.ghostHits + "/" + r.attacked + " rolls)");
                Check(Names[w] + ": the fight began and the bot flew it", r.attacked == rolls && r.seconds > seconds * rolls * .9f);
                Check(Names[w] + ": the bot takes " + HitsPerMinute[w].ToString("F1") + " hits a minute: beatable in principle (under 14)", HitsPerMinute[w] < 14f);
                Check(Names[w] + ": a ship that stands still is hit in every roll (the patterns are not empty)", r.ghostHits == r.attacked);
            }
            Check("the last boss hurts the greedy bot more than the first (" + HitsPerMinute[6].ToString("F1") + " vs " + HitsPerMinute[0].ToString("F1") + " hits a minute)", HitsPerMinute[6] > HitsPerMinute[0]);
            float prev = -1f;
            bool order = true;
            for (int w = 0; w < 7; w++)
            {
                if (prev >= 0f && HitsPerMinute[w] < prev - 1f) order = false;   // (twelve rolls of noise: nothing may fall clearly below an easier boss)
                prev = HitsPerMinute[w];
            }
            Check("no boss hits the bot clearly less than an easier one before it", order);
            float early = (HitsPerMinute[0] + HitsPerMinute[1] + HitsPerMinute[2]) / 3f, late = (HitsPerMinute[4] + HitsPerMinute[5] + HitsPerMinute[6]) / 3f;
            Check("the last three bosses hurt the bot clearly more than the first three (" + late.ToString("F1") + " vs " + early.ToString("F1") + " a minute)", late > 1.5f * early);
            Check("the last boss takes more of the bot's hearts than the first (" + HitsPerMinute[6].ToString("F1") + " vs " + HitsPerMinute[0].ToString("F1") + ": at least 2.5x)", HitsPerMinute[6] > 2.5f * HitsPerMinute[0]);
        }
        finally
        {
            DodgeBot.CountHits = counted;
            for (int i = 0; i < 7; i++) BossLadder.Density[i] = savedDensity[i];
            BossCatalog.UseOverhaul = true;
            BossEncounter.ResetRun();
            BossRails.Reset();
            BossAttackTest.ReleaseAtlases();
        }
        Debug.Log("[LADDERBOT] failures: " + fails);
        return fails;
    }
}
