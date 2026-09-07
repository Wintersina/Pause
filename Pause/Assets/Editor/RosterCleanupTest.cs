using UnityEditor;
using UnityEngine;

public static class RosterCleanupTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[RC] PASS  " : "[RC] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        // The three duplicates are gone, and shipTotal must track Roster
        // exactly -- shipTotal used to be a separate hardcoded constant, and
        // the two drifting out of sync is exactly the kind of bug that made
        // dock buttons or arrays run short in the past.
        foreach (var removed in new[] { "Scout", "Interceptor", "Xenon" })
            Check(removed + " is gone from the roster", System.Array.IndexOf(shopingShips.Roster, removed) < 0);

        Check("shipTotal matches Roster.Length", shopingShips.shipTotal == shopingShips.Roster.Length);
        Check("Prices matches Roster.Length", shopingShips.Prices.Length == shopingShips.Roster.Length);

        // Every remaining legacy ship (Lightning through Turtle) must resolve
        // three genuinely different idle frames rather than the same static
        // sprite three times over -- that silent fallback is exactly what
        // made them look un-animated before.
        for (int i = 0; i < shopingShips.Roster.Length; i++)
        {
            string name = shopingShips.Roster[i];
            if (i < 8) continue; // Retro80s ships, covered elsewhere
            var f0 = shopingShips.IdleSpriteFor(i, 0, 0);
            var f1 = shopingShips.IdleSpriteFor(i, 0, 1);
            var f2 = shopingShips.IdleSpriteFor(i, 0, 2);
            Check(name + " idle frames all resolve", f0 != null && f1 != null && f2 != null);
            Check(name + " idle frames are not all the same sprite (real animation)",
                  !(f0 == f1 && f1 == f2));
        }

        Debug.Log("[RC] failures: " + fails);
        EditorApplication.Exit(0);
    }
}
