using System.Collections.Generic;

// Per-platform ids for every achievement and leaderboard.
//
// The game refers to achievements by their Google Play Games id (StringHolder,
// generated from the Play Console). Game Center ids are separate strings that
// are defined in App Store Connect, so the same achievement needs a second id
// there. SocialBridge resolves the right one for the running platform.
//
// The iOS ids below are a PROPOSED scheme: none existed in the project before.
// Each one must be created in App Store Connect (Features > Game Center) with
// exactly this id, or Game Center rejects reports for it. Titles and points
// are suggestions for that setup; Game Center caps a game at 1000 points and
// these add up to 955.
public static class AchievementIds
{
    public const string IosPrefix = "me.sinaserati.Pause.";

    public struct Entry
    {
        public readonly string android;
        public readonly string ios;
        public readonly string title;
        public readonly int points;      // Game Center points; 0 for a leaderboard
        public readonly bool leaderboard;

        public Entry(string android, string iosSuffix, string title, int points, bool leaderboard = false)
        {
            this.android = android;
            ios = IosPrefix + iosSuffix;
            this.title = title;
            this.points = points;
            this.leaderboard = leaderboard;
        }
    }

    public static readonly Entry[] All =
    {
        // Leaderboard
        new Entry(StringHolder.leaderboard_highest_speed_reached, "highest_speed", "Highest Speed Reached", 0, true),

        // One-shot achievements
        new Entry(StringHolder.achievement_logged_on_successfully, "logged_on", "Logged On Successfully", 5),
        new Entry(StringHolder.achievement_tutorial_complete, "tutorial_complete", "Tutorial Complete", 10),
        new Entry(StringHolder.achievement_paused, "paused", "Paused", 5),
        new Entry(StringHolder.achievement_correct_pause, "correct_pause", "Correct Pause", 10),
        new Entry(StringHolder.achievement_buy_your_first_ship, "buy_first_ship", "Buy Your First Ship", 10),
        new Entry(StringHolder.achievement_flash, "flash", "Flash", 20),
        new Entry(StringHolder.achievement_speedster, "speedster", "Speedster", 25),
        new Entry(StringHolder.achievement_super_sonic, "super_sonic", "Super Sonic", 50),
        new Entry(StringHolder.achievement_buy_all_ships, "buy_all_ships", "Buy All Ships", 100),
        new Entry(StringHolder.achievement_you_have_unlocked_the_secret_ship, "secret_ship", "Secret Ship", 50),

        // Tiered: aliens 5/25/50/150/1000/3500
        new Entry(StringHolder.achievement_aliens, "aliens_5", "Alien Hunter I", 5),
        new Entry(StringHolder.achievement_aliens_2, "aliens_25", "Alien Hunter II", 10),
        new Entry(StringHolder.achievement_aliens_3, "aliens_50", "Alien Hunter III", 20),
        new Entry(StringHolder.achievement_aliens_4, "aliens_150", "Alien Hunter IV", 40),
        new Entry(StringHolder.achievement_aliens_5, "aliens_1000", "Alien Hunter V", 80),
        new Entry(StringHolder.achievement_aliens_6, "aliens_3500", "Alien Hunter VI", 100),

        // Tiered: asteroids 5/25/50/100/1500
        new Entry(StringHolder.achievement_destroyer, "asteroids_5", "Destroyer I", 5),
        new Entry(StringHolder.achievement_destroy_2, "asteroids_25", "Destroyer II", 10),
        new Entry(StringHolder.achievement_destroyer_3, "asteroids_50", "Destroyer III", 20),
        new Entry(StringHolder.achievement_destroyer_4, "asteroids_100", "Destroyer IV", 40),
        new Entry(StringHolder.achievement_destroyer_5, "asteroids_1500", "Destroyer V", 90),

        // Tiered: deaths 1/5/10/50/100
        new Entry(StringHolder.achievement_first_death, "deaths_1", "First Death", 5),
        new Entry(StringHolder.achievement_death_2, "deaths_5", "Death II", 10),
        new Entry(StringHolder.achievement_death_3, "deaths_10", "Death III", 15),
        new Entry(StringHolder.achievement_death_4, "deaths_50", "Death IV", 30),
        new Entry(StringHolder.achievement_death_5, "deaths_100", "Death V", 50),

        // Tiered: stars 150/1000
        new Entry(StringHolder.achievement_stars, "stars_150", "Star Collector I", 40),
        new Entry(StringHolder.achievement_stars_2, "stars_1000", "Star Collector II", 100),
    };

    static Dictionary<string, Entry> byAndroid;

    public static bool TryGet(string androidId, out Entry entry)
    {
        if (byAndroid == null)
        {
            var map = new Dictionary<string, Entry>();
            foreach (var e in All) map[e.android] = e;
            byAndroid = map;
        }
        return byAndroid.TryGetValue(androidId ?? "", out entry);
    }

    // The id to send for the given platform. Unknown ids pass through so a
    // newly generated StringHolder entry still works on Android.
    public static string Resolve(string androidId, bool ios)
    {
        if (!ios) return androidId;
        Entry entry;
        return TryGet(androidId, out entry) ? entry.ios : null;
    }

    public static bool IsIOS
    {
        get
        {
#if UNITY_IOS
            return true;
#else
            return false;
#endif
        }
    }

    public static string ForCurrentPlatform(string androidId)
    {
        return Resolve(androidId, IsIOS);
    }
}
