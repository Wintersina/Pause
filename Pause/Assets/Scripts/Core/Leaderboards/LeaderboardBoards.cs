using System;
using System.Collections.Generic;

// The one table of leaderboards (see docs/leaderboards.md, "Adding a board").
//
// The game refers to a board by its LOGICAL id; each store has its own id for
// it (Play Console generates one, App Store Connect takes the one you type).
// A board is Enabled only when BOTH store ids are filled in. Disabled boards
// are skipped everywhere: no tab, no submission, nothing queued.
public enum LeaderboardSort
{
    HigherIsBetter,   // Play Console "Larger is better" / App Store Connect "High to Low"
    LowerIsBetter,    // "Smaller is better" / "Low to High"
}

// What a finished run measured, in the units the boards store.
public struct LeaderboardRunStats
{
    public long score;         // the run score (RunScore.Total)
    public long topSpeed;      // the speed readout: round(moveBackGround.speed * 100)
    public float starDust;     // star dust earned in this run
    public int worldIndex;     // furthest world reached (0 = Space)
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

    public bool Enabled { get { return androidId.Length > 0 && iosId.Length > 0; } }

    // The id for the given store, or null when the board can't be used there.
    public string PlatformId(bool ios)
    {
        if (!Enabled) return null;
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
    public const string TopSpeed = "top_speed";
    public const string RunStarDust = "run_star_dust";
    public const string FurthestWorld = "furthest_world";

    // Order is tab order: the primary board first.
    public static readonly LeaderboardBoard[] All =
    {
        // The primary board. Disabled until the Play Console board exists:
        // paste its generated id (Get resources) into the empty string.
        new LeaderboardBoard(TopScore,
            "",   // Play Console id
            AchievementIds.IosPrefix + "top_score",
            "Top Score",
            "Best score in a single run.",
            LeaderboardSort.HigherIsBetter,
            FormatScore,
            run => run.score),

        // Secondary: already live in both stores.
        new LeaderboardBoard(TopSpeed,
            StringHolder.leaderboard_highest_speed_reached,
            AchievementIds.IosPrefix + "highest_speed",
            "Top Speed",
            "Best speed reached in a single run.",
            LeaderboardSort.HigherIsBetter,
            v => v.ToString(),
            run => run.topSpeed),

        // Placeholders: fill in both ids once the boards exist in the consoles.
        new LeaderboardBoard(RunStarDust,
            "",   // Play Console id
            "",   // proposed: me.sinaserati.Pause.run_star_dust
            "Star Dust",
            "Most star dust collected in a single run.",
            LeaderboardSort.HigherIsBetter,
            FormatHundredths,
            run => (long)Math.Round(run.starDust * 100.0)),   // stored in hundredths (2 decimals)

        new LeaderboardBoard(FurthestWorld,
            "",   // Play Console id
            "",   // proposed: me.sinaserati.Pause.furthest_world
            "Furthest World",
            "Furthest world reached in a single run.",
            LeaderboardSort.HigherIsBetter,
            FormatWorld,
            run => run.worldIndex + 1),                      // 1 = Space
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

    public static string FormatWorld(long v)
    {
        int i = (int)v - 1;
        if (i < 0 || i >= WorldManager.Worlds.Length) return v.ToString();
        return WorldManager.Worlds[i].displayName;
    }
}
