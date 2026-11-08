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
    // The skin the start ship wore (ShipSkins.Equipped at run start; 0 = default).
    public int startSkin;
    // The hearts the run FINISHED with (0 = died). Sampled during the run.
    public bool hasEnd;
    public int endHearts;
    // The pauses the run FINISHED with (score.pauseCounter at its end).
    public bool hasPauses;
    public int endPauses;
    // The run flew the premium (cheat) ship at any point, or ranked nothing
    // (RunScore.Unranked). Such a run is never submitted anywhere.
    public bool premiumRun;
    // Boosters the run used (LeaderboardRunTag.PowerDust / PowerClear / PowerHoming bits): Dust x2 or homing atoms active during real flight at any
    // point, or a Hazard Clear charge fired. Non-zero routes the run to the Power-Ups board ONLY, and every
    // other board then gets nothing from it.
    public int powerUps;
    // All the star dust the run earned, booster doubling included (the Power-Ups row's star dust column).
    // `starDust` above is the dust the Star Dust board ranks, without what a booster added.
    public float dustEarned;

    public bool PoweredUp { get { return powerUps != 0; } }
    public bool UsedPremium { get { return premiumRun || PremiumShips.IsPremium(startShip); } }
    // Every tag carries the run's star dust (d) and worlds (w). A normal run (the normal boards) also carries its score (r),
    // so the main leaderboard can show all three numbers from one run on any of the three boards. A powered-up run
    // (the Power-Ups board only) carries the dust it earned with the booster's doubling, and its power-ups (u).
    public string Tag
    {
        get
        {
            bool powered = PoweredUp;
            return LeaderboardRunTag.Encode(startShip, startHearts, hasEnd ? endHearts : -1, hasPauses ? endPauses : -1, startSkin,
                (long)System.Math.Round(System.Math.Max(0f, powered ? dustEarned : starDust) * 100.0),
                Math.Max(0, worldsCleared),
                powered ? powerUps : 0,
                powered ? -1 : Math.Max(0L, score));
        }
    }
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
    // The Power-Ups board: takes ONLY runs that used a booster, and those runs take no other board.
    public readonly bool poweredUpOnly;

    public LeaderboardBoard(string id, string androidId, string iosId, string displayName, string description,
                            LeaderboardSort sort, Func<long, string> format, Func<LeaderboardRunStats, long> measure,
                            long minValue = long.MinValue, bool poweredUpOnly = false)
    {
        this.poweredUpOnly = poweredUpOnly;
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
    // The leaderboard "season": bumped together with NEW store board ids, in the same release (docs/leaderboards.md,
    // "Season reset"). When the stored season is behind this, the device forgets its submitted / pending scores and local
    // bests once (LeaderboardService.MigrateSeason), so nothing from the old boards is offered to the new ones.
    // Season 1 is where every existing player already is: no stored season counts as 1, so 1 changes nothing.
    public const int CurrentSeason = 1;
    public const int FirstSeason = 1;
    static int? overrideSeason;
    public static int Season { get { return overrideSeason ?? CurrentSeason; } }
    // Tests: pretend the constant has this value (null: the real one again).
    public static void OverrideSeasonForTests(int? season) { overrideSeason = season; }

    public const string TopScore = "top_score";
    // RETIRED native boards (2026-11-08). The Star Dust and Worlds boards no longer exist in `All`: nothing is submitted,
    // queued, backfilled or read back for them on Play Games or Game Center, and the old store boards are simply left
    // unused (they can't be deleted; hide them in Play Console). The two ids live on as (1) the column ids of the shared
    // Firebase board and of the panel's DUST / WORLDS headers (SharedOnlyColumns below) and (2) the names the device
    // migration drops from the saved queues (LeaderboardService.MigrateRetiredBoards).
    public const string RunStarDust = "run_star_dust";
    public const string FurthestWorld = "furthest_world";
    // Runs that used a booster (Dust x2 / Hazard Clear), ranked by run score. Its star dust, worlds and power-ups
    // columns ride in the score tag. No store ids yet: off until Play Console creates it.
    public const string BoostedRuns = "boosted_runs";

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

    // Column descriptors for the panel's DUST and WORLDS headers and for the shared board. They are NOT native boards:
    // both store ids are empty, so they are never Enabled / Usable and never in `All`. Get() still finds them.
    public static readonly LeaderboardBoard DustColumn = new LeaderboardBoard(RunStarDust, "", "", "Star Dust",
        "Most star dust collected in a single run.", LeaderboardSort.HigherIsBetter, FormatHundredths,
        run => (long)Math.Round(run.starDust * 100.0), 1);
    public static readonly LeaderboardBoard WorldsColumn = new LeaderboardBoard(FurthestWorld, "", "", "Worlds",
        "Most worlds cleared in a single run, across loops.", LeaderboardSort.HigherIsBetter, FormatWorlds,
        run => Math.Max(0, run.worldsCleared), 1);
    public static readonly LeaderboardBoard[] SharedOnlyColumns = { DustColumn, WorldsColumn };

    public static bool IsRetiredNative(string id) { return id == RunStarDust || id == FurthestWorld; }

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
            run => run.score,
            1),   // a 0-score run is never offered

        // Powered-up runs only (docs/leaderboards.md, "Power-Ups board"). Live on Android; the iOS id stays empty on purpose
        // (Game Center cannot carry the columns), so the board has no tab and nothing is submitted or queued for it on iOS.
        new LeaderboardBoard(BoostedRuns,
            "CgkIopqxqbAPEAIQQg",   // Play Console id
            "",   // iOS: Game Center cannot carry the columns (no score tag), see docs
            "Power-Ups",
            "Best score in a run that used Dust x2 or Hazard Clear.",
            LeaderboardSort.HigherIsBetter,
            FormatScore,
            run => run.score,
            1,
            true),
    };

    public static LeaderboardBoard Get(string id)
    {
        foreach (var b in All) if (b.id == id) return b;
        foreach (var b in SharedOnlyColumns) if (b.id == id) return b;
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
