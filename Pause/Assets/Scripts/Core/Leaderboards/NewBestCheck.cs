using System;
using UnityEngine;

// Decides which columns of a finished (or live) run are a NEW BEST, for the flight-complete panel and the HUD.
//
//   Sharing ON (the shared Firebase board is live): a column is a new best when the run beats the player's OWN row
//     on the shared board (score / dust / worlds, in the table the run routes to: Main, or Power-Ups for a boosted
//     run). The row is read from what this device knows locally (SharedLeaderboardService.KnownBest: last server
//     document + the queued run), so it works offline and never waits on a network read. No row yet: the first
//     run with a value is a new best.
//   Sharing OFF (or no shared backend): the original rule. Score is a new best when it passes the saved best
//     (RunScore.IsNewBest); dust / worlds have no board of their own, so they never flag.
//
// Baselines are captured when the run starts, because the finished run is queued for upload before the panel opens.
public static class NewBestCheck
{
    public struct Flags
    {
        public bool score, dust, worlds;
        public bool Any { get { return score || dust || worlds; } }
    }

    // Seams for tests.
    public static Func<bool> SharedMode = DefaultSharedMode;
    public static Func<SharedTable, SharedColumn, long> KnownBest = DefaultKnownBest;

    static readonly long[] baseline = new long[6];
    static int capturedRun = -1;

    public static bool DefaultSharedMode()
    {
        return SharedLeaderboardService.Instance.Enabled;
    }

    static long DefaultKnownBest(SharedTable t, SharedColumn c)
    {
        return SharedLeaderboardService.Instance.KnownBest(t, c);
    }

    static int Slot(SharedTable t, SharedColumn c) { return (int)t * 3 + (int)c; }

    public static long Baseline(SharedTable t, SharedColumn c)
    {
        try { return KnownBest(t, c); }
        catch (Exception) { return 0; }   // never block the results on the shared store
    }

    // Run start: remember the player's row per table and column.
    public static void Capture()
    {
        capturedRun = RunScore.RunId;
        foreach (SharedTable t in new[] { SharedTable.Main, SharedTable.Power })
            foreach (SharedColumn c in SharedDoc.Columns)
                baseline[Slot(t, c)] = Baseline(t, c);
    }

    public static Flags Evaluate(LeaderboardRunStats run)
    {
        var f = new Flags();
        if (!RunScore.Scoring || !RunScore.SavesBest || RunScore.Unranked || run.UsedPremium) return f;
        if (!SharedMode())
        {
            f.score = RunScore.IsNewBest;
            return f;
        }
        var table = run.PoweredUp ? SharedTable.Power : SharedTable.Main;
        var shared = SharedRun.From(run);
        foreach (SharedColumn c in SharedDoc.Columns)
        {
            long have = capturedRun == RunScore.RunId ? baseline[Slot(table, c)] : Baseline(table, c);
            bool better = shared.Metric(c) > 0 && shared.Metric(c) > have;
            if (c == SharedColumn.Score) f.score = better;
            else if (c == SharedColumn.Dust) f.dust = better;
            else f.worlds = better;
        }
        return f;
    }
}

// One-time wipe of the stored best scores used for new-best comparisons, so every player sees fresh NEW BEST
// notifications against the reset shared board. Versioned by a PlayerPrefs flag; touches ONLY the keys below.
public static class NewBestReset
{
    public const string FlagKey = "scores.resetV2";

    public static string[] Keys()
    {
        var keys = new System.Collections.Generic.List<string>
        {
            RunScore.BestScoreKey,
            LeaderboardService.NormalBestKey, LeaderboardService.BoostedBestKey,
            LeaderboardService.PendingKey, LeaderboardService.SubmittedKey,
            SharedLeaderboardService.PendingKey, SharedLeaderboardService.SubmittedKey,
        };
        foreach (SharedTable t in new[] { SharedTable.Main, SharedTable.Power })
            foreach (SharedColumn c in SharedDoc.Columns)
                keys.Add(SharedLeaderboardService.CachePrefix + (int)t + "_" + (int)c);
        return keys.ToArray();
    }

    // True when the wipe ran now.
    public static bool Run()
    {
        if (PlayerPrefs.GetInt(FlagKey, 0) >= 1) return false;
        foreach (string k in Keys()) PlayerPrefs.DeleteKey(k);
        PlayerPrefs.SetInt(FlagKey, 1);
        PrefsSaver.SaveNow();
        return true;
    }
}
