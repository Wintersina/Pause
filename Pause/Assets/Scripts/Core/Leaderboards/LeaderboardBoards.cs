using System;
using System.Collections.Generic;

// The one table of leaderboards (see docs/leaderboards.md, "Adding a board").
//
// The game refers to a board by its LOGICAL id; each store has its own id for
// it (Play Console generates one, App Store Connect takes the one you type).
// A board is Enabled on a platform when THAT platform's store id is filled in
// (Android needs only its Play Games id, iOS only its Game Center id), so a
// board that exists on one store works there while the other is still pending.
// Disabled boards are skipped everywhere: no tab, no submission, nothing queued.
public enum LeaderboardSort
{
    HigherIsBetter,   // Play Console "Larger is better" / App Store Connect "High to Low"
    LowerIsBetter,    // "Smaller is better" / "Low to High"
}

// What a finished run measured, in the units the boards store.
public struct LeaderboardRunStats
{
    public long score;         // the run score (RunScore.Total)
    public float starDust;    // star dust earned in this run
    // The loop the run reached: 1 while still on the first pass through the
    // worlds, 2 once it flew the last live world's portal, and so on (no cap).
    // RunLoop.DisplayNumber is the game's own counter, so the boundary is
    // whatever the last live world was when the loop happened.
    public int loop;
}

public sealed class LeaderboardBoard
{
    public readonly string id;
    public readonly string androidId;
    public readonly string iosId;
    public readonly string displayName;
    public readonly string description;
    public readonly LeaderboardSort sort;
    readonly Func<long, string> format;
    readonly Func<LeaderboardRunStats, long> measure;

    public LeaderboardBoard(string id, string androidId, string iosId, string displayName, string description,
                            LeaderboardSort sort, Func<long, string> format, Func<LeaderboardRunStats, long> measure)
    {
        this.id = id;
        this.androidId = androidId ?? "";
        this.iosId = iosId ?? "";
        this.displayName = displayName;
        this.description = description;
        this.sort = sort;
        this.format = format;
        this.measure = measure;
    }

    // Enabled on the platform the game is running on.
    public bool Enabled { get { return EnabledOn(AchievementIds.IsIOS); } }

    public bool EnabledOn(bool ios) { return (ios ? iosId : androidId).Length > 0; }

    // The id for the given store, or null when the board can't be used there.
    public string PlatformId(bool ios)
    {
        if (!EnabledOn(ios)) return null;
        return ios ? iosId : androidId;
    }

    public string PlatformId() { return PlatformId(AchievementIds.IsIOS); }

    public bool IsBetter(long candidate, long than)
    {
        return sort == LeaderboardSort.HigherIsBetter ? candidate > than : candidate < than;
    }

    public string Format(long value) { return format != null ? format(value) : value.ToString(); }

    public long Measure(LeaderboardRunStats run) { return measure(run); }
}

public static class LeaderboardBoards
{
    public const string TopScore = "top_score";
    public const string RunStarDust = "run_star_dust";
    public const string FurthestWorld = "furthest_world";

    // The board the game used to have for speed. Speed is capped now
    // (SpeedRamp.Cap), so there is nothing to rank: the board is gone from
    // the table, nothing is submitted to it, and a value an older build left
    // in the offline queue under this id is dropped the next time the queue
    // is sent (LeaderboardService.Flush). The store-side board has to be
    // retired in the consoles (docs/leaderboards.md).
    public const string RetiredSpeedBoard = "top_speed";

    // The table. Order is tab order: the primary board first.
    public static LeaderboardBoard[] All { get { return overrideAll ?? table; } }

    // Tests: a table of their own (null: the real one again).
    static LeaderboardBoard[] overrideAll;
    public static void OverrideForTests(LeaderboardBoard[] boards) { overrideAll = boards; }

    static readonly LeaderboardBoard[] table =
    {
        // The primary board. Android id from Play Console (Get resources).
        new LeaderboardBoard(TopScore,
            "CgkIopqxqbAPEAIQPg",   // Play Console id
            AchievementIds.IosPrefix + "top_score",
            "Top Score",
            "Best score in a single run.",
            LeaderboardSort.HigherIsBetter,
            FormatScore,
            run => run.score),

        // Live on Android; the Game Center ids are still to be created (empty = off on iOS).
        new LeaderboardBoard(RunStarDust,
            "CgkIopqxqbAPEAIQPw",   // Play Console id
            "",   // proposed: me.hapticgate.pause.run_star_dust
            "Star Dust",
            "Most star dust collected in a single run.",
            LeaderboardSort.HigherIsBetter,
            FormatHundredths,
            run => (long)Math.Round(run.starDust * 100.0)),   // stored in hundredths (2 decimals)

        new LeaderboardBoard(FurthestWorld,
            "CgkIopqxqbAPEAIQQA",   // Play Console id (lowest 1, no upper limit)
            "",   // proposed: me.hapticgate.pause.furthest_world
            "Furthest Loop",
            "Furthest loop reached in a single run.",
            LeaderboardSort.HigherIsBetter,
            FormatLoop,
            run => Math.Max(1, run.loop)),                   // 1 = first pass, no cap
    };

    public static LeaderboardBoard Get(string id)
    {
        foreach (var b in All) if (b.id == id) return b;
        return null;
    }

    public static List<LeaderboardBoard> Enabled()
    {
        var list = new List<LeaderboardBoard>();
        foreach (var b in All) if (b.Enabled) list.Add(b);
        return list;
    }

    public static string FormatScore(long v)
    {
        return v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static string FormatHundredths(long v)
    {
        return (v / 100) + "." + Math.Abs(v % 100).ToString("00");
    }

    public static string FormatLoop(long v)
    {
        return "Loop " + v;
    }
}
