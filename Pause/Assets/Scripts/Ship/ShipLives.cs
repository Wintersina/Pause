using UnityEngine;

// How many lives (hearts) each ship flies with -- the one table, and the one
// accessor everything reads.
//
// Tiers follow the dock price (shopingShips.Prices):
//   Neon Comet (the free starter)                  2, or 3 once it wears any
//                                                     colour of its own (a
//                                                     bought skin; developer
//                                                     mode owns them all)
//   the cheap ships, under 3000 (Volt Viper..Dove)  3
//   Turtle, Ion Lancer, Jade Phantom (3000-4400)    4
//   Gold Warden (the most expensive)                5
//
// A run's numbers: collisionDetection.MAXLIFE is the flown ship's Max(),
// set when the ship spawns (collisionDetection.Start); lifeCounter counts the
// hits taken since (0 = full health). Heals (the green atom, Mending) undo a
// hit, so they can never lift a ship past its own maximum.
public static class ShipLives
{
    public const int Fewest = 2;
    public const int Most = 5;

    // The starter's lives before / after it gets a new colour.
    public const int StarterLives = 2;
    public const int StarterColourBonus = 1;

    // Price bands (star dust) above the starter.
    public const float FourHeartsFrom = 3000f;

    // Lives by ship id, before the starter's colour bonus.
    public static int Base(int id)
    {
        if (!ShipId.IsValid(id)) return 3;
        if (id == ShipId.Starter) return StarterLives;
        if (id == MostExpensive) return Most;
        return shopingShips.CostFor(id) >= FourHeartsFrom ? 4 : 3;
    }

    // The starter wears any colour besides its stock one (bought, or
    // developer mode). Which skin is equipped doesn't matter.
    public static bool StarterHasColour
    {
        get
        {
            for (int skin = 1; skin < ShipSkins.CountFor(ShipId.Starter); skin++)
                if (ShipSkins.IsOwned(ShipId.Starter, skin)) return true;
            return false;
        }
    }

    // What ship `id` flies with right now.
    public static int Max(int id)
    {
        int lives = Base(id);
        if (id == ShipId.Starter && StarterHasColour) lives += StarterColourBonus;
        return Mathf.Clamp(lives, 1, Most);
    }

    // The flown ship's maximum this run (falls back to the equipped ship's
    // before collisionDetection has set it).
    public static int RunMax
    {
        get { return collisionDetection.MAXLIFE > 0 ? collisionDetection.MAXLIFE : Max(ShipId.Equipped()); }
    }

    // Hearts left this run.
    public static int Left
    {
        get { return Mathf.Clamp(RunMax - collisionDetection.lifeCounter, 0, RunMax); }
    }

    public static bool FullHealth { get { return collisionDetection.lifeCounter <= 0; } }

    // Damage state for art: 0 intact (full), 2 critical (one life left),
    // 1 damaged in between (a two-heart ship goes straight intact -> critical).
    public static int DamageState(int hitsTaken, int max)
    {
        if (hitsTaken <= 0) return 0;
        return max - hitsTaken <= 1 ? 2 : 1;
    }

    public static int RunDamageState
    {
        get { return DamageState(collisionDetection.lifeCounter, RunMax); }
    }

    // The priciest roster ship (Gold Warden).
    public static int MostExpensive
    {
        get
        {
            int best = ShipId.Starter;
            foreach (int id in ShipId.All)
                if (shopingShips.CostFor(id) > shopingShips.CostFor(best)) best = id;
            return best;
        }
    }
}
