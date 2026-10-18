using UnityEngine;

// The note kept with a score: which ship the run STARTED with, how many
// hearts it started with and how many it FINISHED with. "s<ship>h<hearts>e<end>",
// e.g. "s7h4e2" (a run that died ends on e0) -- URL-safe and far under Play
// Games' 64-character score-tag limit. The e part is optional: tags an older
// build wrote ("s7h4") still read, with the end hearts unknown.
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

    // endHearts < 0 = not known: no e part.
    public static string Encode(int ship, int hearts, int endHearts = -1)
    {
        if (!ShipId.IsValid(ship) || PremiumShips.IsPremium(ship) || hearts < 1) return "";
        string tag = "s" + ship + "h" + hearts;
        if (endHearts >= 0) tag += "e" + Mathf.Min(endHearts, ShipLives.MostHearts);
        return tag;
    }

    // True when the tag is readable; ship / hearts are 0 for what is unknown
    // or out of range (a hearts count a ship can't have, a premium ship).
    public static bool TryParse(string tag, out int ship, out int hearts)
    {
        int end;
        return TryParse(tag, out ship, out hearts, out end);
    }

    // endHearts is -1 when the tag has no (readable) end hearts.
    public static bool TryParse(string tag, out int ship, out int hearts, out int endHearts)
    {
        ship = 0;
        hearts = 0;
        endHearts = -1;
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
        long e = -1;
        if (i < tag.Length)
        {
            if (tag[i] != 'e') return false;
            i++;
            digits = 0;
            e = 0;
            while (i < tag.Length && tag[i] >= '0' && tag[i] <= '9' && digits < 4) { e = e * 10 + (tag[i] - '0'); i++; digits++; }
            if (digits == 0 || i != tag.Length) return false;
        }
        if (PremiumShips.IsPremium((int)s)) return false;   // never shown, hearts included
        if (ShipId.IsValid((int)s)) ship = (int)s;
        if (h >= 1 && h <= ShipLives.MostHearts) hearts = (int)h;
        if (e >= 0 && e <= ShipLives.MostHearts) endHearts = (int)e;
        return ship != 0 || hearts != 0;
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
