using System.Collections.Generic;
using UnityEngine;

// The ownership gate on the last three berths of the dock.
//
// The three dearest roster ships (price ascending, the dock's own order, with
// the premium Paragon last) cannot be bought until the pilot already owns
// enough other ships: the first two need 4 owned, the last one 5. "Owned"
// counts the free starter and every ship in the roster (Paragon too, if
// granted), read from the saved boughtship<id> keys through ShipId.IsOwned --
// the ship being bought is not counted. A ship the player already owns stays
// owned and usable whatever the count (the gate only guards buying, never
// ownership, restore or launch). No saved key or format is involved.
public static class ShipUnlocks
{
    public const int GatedCount = 3;

    // Ships owned before each gated berth opens, in dock order.
    static readonly int[] required = { 4, 4, 5 };

    static int[] gated;

    // The gated ids, in dock order (cheapest first). Worked out over the whole
    // roster so a hidden Paragon does not shift the other two.
    public static int[] GatedIds
    {
        get
        {
            if (gated == null)
            {
                var ids = new List<int>();
                for (int id = 1; id < shopingShips.RosterTotal; id++) ids.Add(id);
                ids.Sort((a, b) =>
                {
                    int byPrice = shopingShips.CostFor(a).CompareTo(shopingShips.CostFor(b));
                    return byPrice != 0 ? byPrice : a.CompareTo(b);
                });
                gated = ids.GetRange(ids.Count - GatedCount, GatedCount).ToArray();
            }
            return gated;
        }
    }

    // Ships that must be owned before `id` may be bought; 0 for an ungated ship.
    public static int RequiredOwned(int id)
    {
        var ids = GatedIds;
        for (int n = 0; n < ids.Length; n++)
            if (ids[n] == id) return required[n];
        return 0;
    }

    // The one source of truth: how many roster ships the player owns now,
    // not counting `excluding` (the ship being bought).
    public static int OwnedCount(int excluding = 0)
    {
        int count = 0;
        for (int id = 1; id < shopingShips.RosterTotal; id++)
            if (id != excluding && ShipId.IsOwned(id)) count++;
        return count;
    }

    // True while buying `id` is refused for want of ships. Owned ships are
    // never locked.
    public static bool IsLocked(int id)
    {
        int need = RequiredOwned(id);
        if (need == 0 || ShipId.IsOwned(id) || DeveloperUnlocks.Enabled) return false;
        return OwnedCount(id) < need;
    }

    // The popup / button wording while locked: "OWN 4 SHIPS".
    public static string LockedButtonText(int id)
    {
        return "OWN " + RequiredOwned(id) + " SHIPS";
    }

    // The longer line for messages: "Own 4 ships to unlock".
    public static string LockedMessage(int id)
    {
        return "OWN " + RequiredOwned(id) + " SHIPS TO UNLOCK";
    }
}
