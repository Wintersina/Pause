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
    // How many worlds the run CLEARED in total, across loops: one per portal /
    // lift-off flown (RunScore.Parts.worldCount, the same event that pays the
    // world-cleared points). Entering a world is not clearing it.
    public int worldsCleared;
    // What the run STARTED with (captured at run start, not read at the end):
    // the ship id and its hearts. Shown beside the score on the boards.
    public int startShip;
    public int startHearts;
    // The hearts the run FINISHED with (0 = died). Sampled during the run.
    public bool hasEnd;
    public int endHearts;
    // The pauses the run FINISHED with (score.pauseCounter at its end).
    public bool hasPauses;
    public int endPauses;
    // The run flew the premium (cheat) ship at any point, or ranked nothing
    // (RunScore.Unranked). Such a run is never submitted anywhere.
    public bool premiumRun;

    public bool UsedPremium { get { return premiumRun || PremiumShips.IsPremium(startShip); } }
    public string Tag { get { return LeaderboardRunTag.Encode(startShip, startHearts, hasEnd ? endHearts : -1, hasPauses ? endPauses : -1); } }
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
    // A value below this is never offered (the store board's lower limit: the
    // worlds board starts at 1, so a run that cleared nothing sends nothing).
    public readonly long minValue;

    public LeaderboardBoard(string id, string androidId, string iosId, string displayName, string description,
                            LeaderboardSort sort, Func<long, string> format, Func<LeaderboardRunStats, long> measure,
                            long minValue = long.MinValue)
    {
        this.minValue = minValue;
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

        // Worlds cleared across loops (logical and store ids kept from the old
        // "Furthest Loop" board; only the meaning changed). The Play Console
        // draft was made with limits 1..7: its upper limit must be raised
        // (docs/leaderboards.md, "Worlds cleared"). Lowest 1: a run that
        // cleared no world is never offered.
        new LeaderboardBoard(FurthestWorld,
            "CgkIopqxqbAPEAIQQA",   // Play Console id (same id as the old Furthest Loop board)
            "",   // proposed: me.hapticgate.pause.furthest_world
            "Worlds",
            "Most worlds cleared in a single run, across loops.",
            LeaderboardSort.HigherIsBetter,
            FormatWorlds,
            run => Math.Max(0, run.worldsCleared),
            1),
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

    // Worlds in one full pass (Space, Frost, Verdant, Ember, Tide, Storm,
    // Desert). Fixed here, NOT read from WorldManager, so a stored value keeps
    // its meaning if a world is added later (LeaderboardTest pins it to the
    // live world count).
    public const int WorldsPerLoop = 7;

    // The loop a run that cleared `worlds` worlds is on: 1 for 0..7, 2 for 8..14,
    // 3 for 15..21. Clearing the 7th world still reads LOOP 1; the loop number
    // goes up with the 8th clear.
    public static int LoopOfWorlds(long worlds)
    {
        if (worlds <= WorldsPerLoop) return 1;
        return (int)((worlds - 1) / WorldsPerLoop) + 1;
    }

    // "LOOP 2 \u00B7 WORLDS 8". WORLD (singular) for exactly 1, WORLDS otherwise.
    public const string WorldsSeparator = " \u00B7 ";
    public static string FormatWorlds(long worlds)
    {
        if (worlds < 0) worlds = 0;
        return "LOOP " + LoopOfWorlds(worlds) + WorldsSeparator + (worlds == 1 ? "WORLD " : "WORLDS ") + worlds;
    }
}
