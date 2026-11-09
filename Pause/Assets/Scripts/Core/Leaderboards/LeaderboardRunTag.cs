using UnityEngine;

// The note kept with a score: which ship the run STARTED with, how many
// hearts it started with, how many it FINISHED with and how many pauses it had
// left, and the skin (colour) the ship wore.
// "s<ship>h<hearts>e<end>p<pauses>k<skin>", e.g. "s7h4e2p3k2" (a run that died
// ends on e0; no k part = the default skin) -- URL-safe and far under Play Games' 64-character score-tag
// limit. Everything after the h part is optional: tags an older build wrote
// ("s7h4", "s7h4e2") still read, with what they lack unknown. A later build may
// add more "<letter><digits>" fields; a reader skips the ones it does not know
// (so "s7h4e2p3q9" still reads) rather than rejecting the whole tag.
//
// Run columns, after k: "d<star dust in hundredths>w<worlds cleared>u<power-up bits>r<run score>", e.g.
// "s7h4e2p3k2d123456w12u3". Play Games keeps one sort value per board, so a row's other numbers ride in its tag.
//   - every NORMAL-board run writes d, w and r (the main leaderboard shows score, dust and worlds from one run, whichever
//     of the three boards is being read); no u.
//   - a POWER-UPS run writes d (all the dust it earned), w and u; no r (its score is the board value).
// The longest tag an in-range run can write is about 45 characters (PowerUpsLeaderboardTest pins it), under the 64 limit.
// A reader that predates a letter skips it, and a tag without the letters (older builds) reads as "not recorded".
//
//   Android  the tag is the Play Games score tag, so every player's row shows
//            their own ship and hearts.
//   iOS      Unity's Social API has no Game Center score context, so other
//            players' rows have no tag (blank); the player's own row is filled
//            from the device-local record LeaderboardService keeps next to the
//            submitted score.
//
// A tag naming a premium (cheat) ship is never written, and one found on a
// board is read as unknown, so that ship never shows up in a leaderboard row.
public static class LeaderboardRunTag
{
    public const int MaxLength = 64;
    // Most pauses a row can show (two digits); a bigger count reads as unknown.
    public const int MostPauses = 99;
    // Highest skin index a tag carries (two digits); a bigger one reads as the default skin.
    public const int MostSkin = 99;
    // Power-ups board columns. Star dust is in hundredths (9 digits = 9,999,999.99, far past any run); worlds
    // are capped at the store board's upper limit.
    public const long MostDust = 999999999L;
    public const int MostWorlds = 9999;
    // Run score in the r field (10 digits); a bigger one is clamped.
    public const long MostScore = 9999999999L;
    // The bits of the u field: which boosters the run used.
    public const int PowerDust = 1, PowerClear = 2, PowerHoming = 4, PowerAll = PowerDust | PowerClear | PowerHoming;

    // endHearts < 0 = not known: no e part. pauses < 0 = not known: no p part.
    // skin <= 0 (the default skin) writes no k part.
    // The run columns: dust >= 0 = d part, worlds >= 0 = w part, powerUps > 0 = u part (Power-Ups board only),
    // score >= 0 = r part (normal boards only).
    public static string Encode(int ship, int hearts, int endHearts = -1, int pauses = -1, int skin = 0,
                                long dustHundredths = -1, int worlds = -1, int powerUps = 0, long score = -1)
    {
        if (!ShipId.IsValid(ship) || PremiumShips.IsPremium(ship) || hearts < 1) return "";
        string tag = "s" + ship + "h" + hearts;
        if (endHearts >= 0) tag += "e" + Mathf.Min(endHearts, ShipLives.MostHearts);
        if (pauses >= 0) tag += "p" + Mathf.Min(pauses, MostPauses);
        if (skin > 0) tag += "k" + Mathf.Min(skin, MostSkin);
        if (dustHundredths >= 0) tag += "d" + System.Math.Min(dustHundredths, MostDust);
        if (worlds >= 0) tag += "w" + Mathf.Min(worlds, MostWorlds);
        if ((powerUps & PowerAll) != 0) tag += "u" + (powerUps & PowerAll);
        if (score >= 0) tag += "r" + System.Math.Min(score, MostScore);
        return tag;
    }

    // True when the tag is readable; ship / hearts are 0 for what is unknown
    // or out of range (a hearts count a ship can't have, a premium ship).
    public static bool TryParse(string tag, out int ship, out int hearts)
    {
        int end, pauses;
        return TryParse(tag, out ship, out hearts, out end, out pauses);
    }

    // endHearts is -1 when the tag has no (readable) end hearts.
    public static bool TryParse(string tag, out int ship, out int hearts, out int endHearts)
    {
        int pauses;
        return TryParse(tag, out ship, out hearts, out endHearts, out pauses);
    }

    // pauses is -1 when the tag has no (readable) pauses count.
    public static bool TryParse(string tag, out int ship, out int hearts, out int endHearts, out int pauses)
    {
        int skin;
        return TryParse(tag, out ship, out hearts, out endHearts, out pauses, out skin);
    }

    // skin is 0 (the default skin) when the tag has none (an older build) or one that can't be.
    public static bool TryParse(string tag, out int ship, out int hearts, out int endHearts, out int pauses, out int skin)
    {
        long dust;
        int worlds, powerUps;
        return TryParse(tag, out ship, out hearts, out endHearts, out pauses, out skin, out dust, out worlds, out powerUps);
    }

    // dust is -1 and worlds is -1 when the tag has none (every normal-board tag), powerUps is 0 then.
    public static bool TryParse(string tag, out int ship, out int hearts, out int endHearts, out int pauses, out int skin,
                                out long dust, out int worlds, out int powerUps)
    {
        long runScore;
        return TryParse(tag, out ship, out hearts, out endHearts, out pauses, out skin, out dust, out worlds, out powerUps, out runScore);
    }

    // runScore is -1 when the tag has no r field (a Power-Ups tag, or one an older build wrote).
    public static bool TryParse(string tag, out int ship, out int hearts, out int endHearts, out int pauses, out int skin,
                                out long dust, out int worlds, out int powerUps, out long runScore)
    {
        runScore = -1;
        dust = -1;
        worlds = -1;
        powerUps = 0;
        skin = 0;
        ship = 0;
        hearts = 0;
        endHearts = -1;
        pauses = -1;
        if (string.IsNullOrEmpty(tag) || tag.Length > MaxLength || tag[0] != 's') return false;
        int i = 1;
        long s = 0, h = 0;
        int digits = 0;
        while (i < tag.Length && tag[i] >= '0' && tag[i] <= '9' && digits < 4) { s = s * 10 + (tag[i] - '0'); i++; digits++; }
        if (digits == 0 || i >= tag.Length || tag[i] != 'h') return false;
        i++;
        digits = 0;
        while (i < tag.Length && tag[i] >= '0' && tag[i] <= '9' && digits < 4) { h = h * 10 + (tag[i] - '0'); i++; digits++; }
        if (digits == 0) return false;
        long e = -1, p = -1, k = -1, d = -1, w = -1, u = -1, r = -1;
        while (i < tag.Length)
        {
            char key = tag[i];
            if (key < 'a' || key > 'z') return false;
            i++;
            long n = 0;
            digits = 0;
            while (i < tag.Length && tag[i] >= '0' && tag[i] <= '9')
            {
                if (digits < 10) n = n * 10 + (tag[i] - '0');   // a longer run is absurd, not an error
                i++; digits++;
            }
            if (digits == 0) return false;   // a field without a number: malformed
            if (key == 'e' && e < 0) e = digits <= 4 ? n : 1000000;
            else if (key == 'p' && p < 0) p = digits <= 4 ? n : 1000000;
            else if (key == 'k' && k < 0) k = digits <= 4 ? n : 1000000;
            else if (key == 'd' && d < 0) d = digits <= 9 ? n : MostDust;
            else if (key == 'w' && w < 0) w = digits <= 4 ? n : 1000000;
            else if (key == 'u' && u < 0) u = digits <= 2 ? n : 0;
            else if (key == 'r' && r < 0) r = digits <= 10 ? n : MostScore;
            // any other letter is a field from a newer build: skipped
        }
        if (PremiumShips.IsPremium((int)s)) return false;   // never shown, hearts included
        if (ShipId.IsValid((int)s)) ship = (int)s;
        if (h >= 1 && h <= ShipLives.MostHearts) hearts = (int)h;
        if (e >= 0 && e <= ShipLives.MostHearts) endHearts = (int)e;
        if (p >= 0 && p <= MostPauses) pauses = (int)p;
        if (ship != 0 && k >= 1 && k <= MostSkin && ShipSkins.Has(ship, (int)k)) skin = (int)k;
        if (d >= 0 && d <= MostDust) dust = d;
        if (w >= 0 && w <= MostWorlds) worlds = (int)w;
        if (u > 0 && u <= PowerAll) powerUps = (int)u;
        if (r >= 0 && r <= MostScore) runScore = r;
        return ship != 0 || hearts != 0;
    }

    // Whether the tag records the run's score (the r field): the marker of a tag with the main leaderboard's columns.
    public static bool HasRunScore(string tag)
    {
        int ship, hearts, end, pauses, skin, worlds, powerUps;
        long dust, runScore;
        return TryParse(tag, out ship, out hearts, out end, out pauses, out skin, out dust, out worlds, out powerUps, out runScore) && runScore >= 0;
    }

    // The row's short form: one booster's name, "BOTH" for two, "ALL 3" for three (fits under the score at 16 units).
    public static string PowerUpShort(int powerUps)
    {
        powerUps &= PowerAll;
        int n = (powerUps & PowerDust) != 0 ? 1 : 0;
        if ((powerUps & PowerClear) != 0) n++;
        if ((powerUps & PowerHoming) != 0) n++;
        return n == 3 ? "ALL 3" : n == 2 ? "BOTH" : PowerUpLabel(powerUps);
    }

    // The Shop/HUD icon names of the boosters a row used, in the HUD order (dust, homing, clear); empty for none.
    public static string[] PowerUpIcons(int powerUps)
    {
        powerUps &= PowerAll;
        var list = new System.Collections.Generic.List<string>(3);
        if ((powerUps & PowerDust) != 0) list.Add("booster_dust_x2");
        if ((powerUps & PowerHoming) != 0) list.Add("booster_homing_atoms");
        if ((powerUps & PowerClear) != 0) list.Add("booster_hazard_clear");
        return list.ToArray();
    }

    // "DUST x2", "CLEAR", "HOMING", or the used ones joined by " + " ("DUST x2 + CLEAR"); empty for none. The row shows the short form.
    public static string PowerUpLabel(int powerUps)
    {
        powerUps &= PowerAll;
        string label = "";
        if ((powerUps & PowerDust) != 0) label = "DUST x2";
        if ((powerUps & PowerClear) != 0) label += (label.Length > 0 ? " + " : "") + "CLEAR";
        if ((powerUps & PowerHoming) != 0) label += (label.Length > 0 ? " + " : "") + "HOMING";
        return label;
    }

    // Whether a tag names the premium ship (the queue drops such an entry).
    public static bool IsPremium(string tag)
    {
        if (string.IsNullOrEmpty(tag) || tag.Length > MaxLength || tag[0] != 's') return false;
        int i = 1;
        long s = 0;
        while (i < tag.Length && tag[i] >= '0' && tag[i] <= '9' && i < 6) { s = s * 10 + (tag[i] - '0'); i++; }
        return i > 1 && PremiumShips.IsPremium((int)s);
    }
}
