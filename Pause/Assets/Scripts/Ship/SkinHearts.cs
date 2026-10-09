using UnityEngine;

// Hull colours (ShipSkins) make the ship tougher as well as better armed:
// the one tuning block for the hearts the skins add. ShipLives.Max reads it.
//
// Per ship: the hearts follow the same tier the weapon does
// (ShipWeaponUpgrades.Level = how many of THAT ship's non-stock colours the
// player owns, whichever is equipped), so buying a colour for a ship raises
// that ship's weapon and, every other colour, its hearts:
//
//   colours owned (of 4)    0   1   2   3   4
//   extra hearts            0  +1  +1  +2  +2      (ByColoursOwned)
//
// The first colour gives a heart at once (the starter's old "3 once it wears
// a colour" rule, now every ship's), the third the second one.
//
// The complete set: once EVERY colour of EVERY ship is owned (the 60 bought
// skins -- which needs every ship bought too), every ship flies with
// AllSkinsBonus more hearts on top. The dock announces it once, on the
// purchase that completes the set (SpaceDock.BuySkin -> CodexToast).
//
//   dock row (base)   stock  1-2 colours  3-4 colours  + all skins
//   1 (3 ships)         1        2            3             5
//   2                   2        3            4             6
//   3                   3        4            5             7
//   4                   4        5            6             8
//   5                   5        6            7             9
//
// Nothing is saved for it: the bonus is worked out from the owned-skin keys
// every time, so old saves, cloud restores and restored purchases all get it
// for free. Developer mode owns every skin, so it flies the full bonus. The
// tutorial flies the bare hull (ShipLives.Base) so its lessons don't change.
//
// To change the curve: edit ByColoursOwned (one entry per colour count,
// 0..ShipSkins.PerShip-1, never decreasing) and AllSkinsBonus. ShipLives.Most
// (the heart orbit's capacity) follows on its own.
public static class SkinHearts
{
    // ---- tuning ----
    // Extra hearts by how many of the ship's non-stock colours are owned.
    public static readonly int[] ByColoursOwned = { 0, 1, 1, 2, 2 };
    // Extra hearts on every ship once every skin of every ship is owned.
    public const int AllSkinsBonus = 2;

    // The most the colours of one ship can add.
    public static int MostFromColours { get { return ByColoursOwned[ByColoursOwned.Length - 1]; } }

    // Extra hearts for owning `colours` of a ship's colours.
    public static int ForColours(int colours)
    {
        return ByColoursOwned[Mathf.Clamp(colours, 0, ByColoursOwned.Length - 1)];
    }

    // The ship's colour tier (the weapon level: non-stock colours owned).
    public static int ColoursOwned(int id) { return ShipWeaponUpgrades.Level(id); }

    public static int FromColours(int id) { return ShipId.IsValid(id) ? ForColours(ColoursOwned(id)) : 0; }

    // Every non-stock colour of every ship is owned (developer mode: yes).
    // Stops at the first one missing, so it is a handful of pref reads for
    // almost every player.
    public static bool AllSkinsOwned
    {
        get
        {
            foreach (int id in ShipId.All)
                for (int skin = 1; skin < ShipSkins.CountFor(id); skin++)
                    if (!ShipSkins.IsOwned(id, skin)) return false;
            return true;
        }
    }

    // How many skins are still to buy for the complete set.
    public static int SkinsMissing
    {
        get
        {
            int n = 0;
            foreach (int id in ShipId.All)
                for (int skin = 1; skin < ShipSkins.CountFor(id); skin++)
                    if (!ShipSkins.IsOwned(id, skin)) n++;
            return n;
        }
    }

    public static int SetBonus { get { return AllSkinsOwned ? AllSkinsBonus : 0; } }

    // Everything the skins add to ship `id` right now.
    public static int Bonus(int id) { return FromColours(id) + SetBonus; }

    // What buying skin `skin` of ship `id` would add to that ship's hearts:
    // its colour step, plus the set bonus if it is the last skin missing.
    public static int GainIfBought(int id, int skin, out bool completesSet)
    {
        completesSet = false;
        if (!ShipSkins.Has(id, skin) || skin == ShipSkins.Stock || ShipSkins.IsOwned(id, skin)) return 0;
        int n = ColoursOwned(id);
        int gain = ForColours(n + 1) - ForColours(n);
        completesSet = SkinsMissing == 1;
        if (completesSet) gain += AllSkinsBonus;
        return gain;
    }

    public static int GainIfBought(int id, int skin)
    {
        bool set;
        return GainIfBought(id, skin, out set);
    }

    // ---- text ----

    public static string HeartsWord(int n) { return n == 1 ? "HEART" : "HEARTS"; }

    // The dock's line after a purchase, with the hearts it added to that ship
    // (SkinHearts.GainIfBought): "ACQUIRED", "ACQUIRED  +1 HEART", "+2 HEARTS".
    public static string PurchaseMessage(int gain)
    {
        return gain > 0 ? "ACQUIRED  +" + gain + " " + HeartsWord(gain) : "ACQUIRED";
    }

    // The one-time announcement when the set is completed.
    public const string AllSkinsHeading = "EVERY SKIN OWNED";
    public static string AllSkinsTitle { get { return "ALL SKINS: +" + AllSkinsBonus + " HEARTS"; } }

    // The dock's hearts line for ship `id`: "HEARTS 3", "HEARTS 4: 3 +1 COLOURS",
    // "HEARTS 9: 5 +2 COLOURS +2 SET".
    public static string HeartsLine(int id)
    {
        int b = ShipLives.Base(id), c = FromColours(id), s = SetBonus;
        string line = "HEARTS " + ShipLives.Max(id);
        if (c == 0 && s == 0) return line;
        line += ": " + b;
        if (c > 0) line += " +" + c + " COLOURS";
        if (s > 0) line += " +" + s + " SET";
        return line;
    }

    // The dock's line while an unbought colour is previewed: what it adds.
    public static string BuyLine(int id, int skin)
    {
        bool set;
        int gain = GainIfBought(id, skin, out set);
        if (gain <= 0) return "";
        if (!set) return "BUY: +" + gain + " " + HeartsWord(gain);
        int colour = gain - AllSkinsBonus;
        return colour > 0 ? "BUY: +" + colour + " " + HeartsWord(colour) + " +" + AllSkinsBonus + " ALL SKINS"
                          : "BUY: +" + AllSkinsBonus + " HEARTS ALL SKINS";
    }

    // The codex's line: when the colours add their hearts.
    public static string LoreLine()
    {
        var parts = new System.Collections.Generic.List<string>();
        for (int n = 1; n < ByColoursOwned.Length; n++)
            if (ByColoursOwned[n] > ByColoursOwned[n - 1])
                parts.Add("+" + ByColoursOwned[n] + " at " + n + (n == 1 ? " colour" : " colours"));
        return "COLOUR HEARTS  " + string.Join(", ", parts) + "\n" +
               "ALL SKINS  +" + AllSkinsBonus + " hearts on every ship once every skin is owned";
    }
}
