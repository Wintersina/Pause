using System;

// The bounds the Firestore security rules enforce (firebase/firestore.rules), mirrored in C#. The Fake backend
// applies them so the service is tested against the same walls, and SharedLeaderboardTest checks this mirror
// and the rules against the one vector file firebase/test-vectors.json. Change both together.
public static class SharedBoardLimits
{
    public const int MaxNameLength = 24;
    public const int MinWriteIntervalSeconds = 15;
    public const int MaxListLimit = 25;
    public const long MaxScore = 9999999999L;
    public const long MaxDust = 999999999L;
    public const int MaxWorlds = 9999;
    public const int MaxShip = 40;
    public const int MaxHearts = 20;
    public const int MaxPauses = 99;
    public const int MaxSkin = 99;
    public const string Android = "android";
    public const string Ios = "ios";
    // Firebase's sign_in_provider claim for each platform's sign-in (Play Games / Game Center).
    public const string ProviderAndroid = "playgames.google.com";
    public const string ProviderIos = "gc.apple.com";

    public static string ProviderFor(string platform)
    {
        return platform == Android ? ProviderAndroid : platform == Ios ? ProviderIos : "";
    }

    // The platform must be one of the two and match how the player signed in.
    public static bool PlatformOk(string platform, string provider)
    {
        return !string.IsNullOrEmpty(platform) && platform == (provider == ProviderAndroid ? Android : provider == ProviderIos ? Ios : null);
    }

    // Boards are public: get is open, a list needs a limit of 25 or less, signed in or not.
    public static bool AllowList(int limit) { return limit >= 1 && limit <= MaxListLimit; }

    public static string Collection(SharedTable table) { return table == SharedTable.Power ? "power" : "main"; }

    public static string Field(SharedColumn column)
    {
        return column == SharedColumn.Score ? "byScore.score" : column == SharedColumn.Dust ? "byDust.dust" : "byWorlds.worlds";
    }

    static bool In(long v, long lo, long hi) { return v >= lo && v <= hi; }

    public static bool ValidRun(SharedRun r, SharedTable table)
    {
        return In(r.score, 1, MaxScore)
            && In(r.dust, 0, MaxDust)
            && In(r.worlds, 0, MaxWorlds)
            && In(r.ship, 0, MaxShip) && r.ship != PremiumShips.Paragon
            && In(r.hearts, 0, MaxHearts)
            && In(r.endHearts, -1, MaxHearts)
            && In(r.pauses, -1, MaxPauses)
            && In(r.skin, 0, MaxSkin)
            && (table == SharedTable.Power ? In(r.powerUps, 1, LeaderboardRunTag.PowerAll) : r.powerUps == 0);
    }

    // The three slots are bests over the same runs, so each slot's own metric dominates the others' value of it.
    public static bool Consistent(SharedDoc d)
    {
        return d.byScore.score >= d.byDust.score && d.byScore.score >= d.byWorlds.score
            && d.byDust.dust >= d.byScore.dust && d.byDust.dust >= d.byWorlds.dust
            && d.byWorlds.worlds >= d.byScore.worlds && d.byWorlds.worlds >= d.byDust.worlds;
    }

    // Everything but the timestamp (the server stamps it).
    public static bool ValidDoc(SharedDoc d, SharedTable table, string provider)
    {
        return d != null && d.IsFull
            && PlatformOk(d.platform, provider)
            && d.name != null && d.name.Length >= 1 && d.name.Length <= MaxNameLength
            && ValidRun(d.byScore, table) && ValidRun(d.byDust, table) && ValidRun(d.byWorlds, table)
            && Consistent(d);
    }

    // Fields only improve (equal is allowed: a tie may gain its columns).
    public static bool Improves(SharedDoc n, SharedDoc o)
    {
        return n.byScore.score >= o.byScore.score && n.byDust.dust >= o.byDust.dust && n.byWorlds.worlds >= o.byWorlds.worlds;
    }

    public static bool AllowCreate(SharedDoc d, SharedTable table, string provider) { return ValidDoc(d, table, provider); }

    public static bool AllowUpdate(SharedDoc old, SharedDoc d, SharedTable table, double secondsSinceLastWrite, string provider)
    {
        return ValidDoc(d, table, provider) && Improves(d, old) && secondsSinceLastWrite > MinWriteIntervalSeconds;
    }

    // The name shown on the row: the platform profile name, control characters dropped, at most 24 characters.
    // An empty one becomes PILOT plus the last four characters of the uid.
    public static string CleanName(string name, string uid)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char ch in name ?? "")
            if (!char.IsControl(ch)) sb.Append(ch);
        string clean = sb.ToString().Trim();
        if (clean.Length > MaxNameLength) clean = clean.Substring(0, MaxNameLength).TrimEnd();
        if (clean.Length > 0) return clean;
        string tail = string.IsNullOrEmpty(uid) ? "" : uid.Length > 4 ? uid.Substring(uid.Length - 4) : uid;
        return "PILOT" + tail;
    }
}
