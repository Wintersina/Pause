using UnityEngine;

// Ship 15 anchors the scaled dust-price table at 15,000; starters and IAP stay unchanged.
public static class ShipPriceTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SHIPPRICE] PASS  " : "[SHIPPRICE] FAIL  ") + what);
        if (!ok) fails++;
    }

    // Old prices by roster id (0..15), written out on purpose.
    static readonly float[] Old = { 0, 0, 300, 2900, 4550, 6600, 9100, 12000, 500, 2050, 2500, 3350, 3700, 4100, 4950, 6150 };
    static readonly float[] Expected = { 0, 0, 300, 7050, 11100, 16100, 22200, 29250, 1200, 5000, 6100, 8150, 9000, 10000, 12050, 15000 };

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        var p = shopingShips.Prices;
        Check("table covers the roster", p.Length == shopingShips.Roster.Length && p.Length == shopingShips.RosterTotal);
        Check("Turtle is exactly 15000", p[15] == 15000f);
        bool exact = true, mult = true, order = true;
        for (int i = 0; i < Old.Length; i++)
        {
            if (p[i] != Expected[i]) exact = false;
            if (p[i] % 50f != 0f) mult = false;
        }
        for (int a = 2; a < Old.Length; a++)
            for (int b = 2; b < Old.Length; b++)
                if (Old[a] < Old[b] && !(p[a] < p[b])) order = false;
        Check("every dust ship has its expected new price", exact);
        Check("all prices are multiples of 50", mult);
        Check("old price order kept, no ties", order);
        Check("starter and slot 0 are free", p[0] == 0f && p[shopingShips.StarterShip] == 0f);
        float min = float.MaxValue;
        for (int i = 2; i < Old.Length; i++) min = Mathf.Min(min, p[i]);
        Check("Volt Viper is the cheapest paid ship", min == p[2]);
        Check("ships 3–15 scale to the 15k anchor within 25", AllScaled(p));
        Check("Paragon has no dust price", PremiumShips.IsPremium(16) && p[16] == 0f && shopingShips.CostFor(16) == float.MaxValue);
                Debug.Log("[SHIPPRICE] failures: " + fails);
        return fails;
    }

    static bool AllScaled(float[] p)
    {
        for (int i = 2; i < Old.Length; i++)
            if (i != 2 && Mathf.Abs(p[i] - Old[i] * (15000f / 6150f)) > 25.001f) return false;
        return true;
    }
}
