using UnityEngine;

// How many lives (hearts) each ship flies with -- the one table, and the one
// accessor everything reads.
//
// The hull's own lives (Base) follow the dock price (shopingShips.Prices):
//   Neon Comet (the free starter)                  2
//   the cheap ships, under 3000 (Volt Viper..Dove)  3
//   Turtle, Ion Lancer, Jade Phantom (3000-4400)    4
//   Gold Warden (the most expensive)                5
// and the colours bought add to them (SkinHearts: +1 with the ship's first
// colour, +2 with its third, +2 more on every ship once every skin of every
// ship is owned) -- Max. The tutorial flies the bare hull (TutorialMax).
//
// A run's numbers: collisionDetection.MAXLIFE is the flown ship's Max(),
// set when the ship spawns (collisionDetection.Start); lifeCounter counts the
// hits taken since (0 = full health). Heals (the green atom, Mending) undo a
// hit, so they can never lift a ship past its own maximum.
public static class ShipLives
{
    public const int Fewest = 2;
    // The most hearts any hull has of its own (Gold Warden).
    public const int MostBase = 5;
    // The most any ship can fly with: the dearest hull with every colour and
    // the complete set (the heart orbit is built for this many).
    public static int Most { get { return MostBase + SkinHearts.MostFromColours + SkinHearts.AllSkinsBonus; } }

    // The starter's own lives.
    public const int StarterLives = 2;

    // Price bands (star dust) above the starter.
    public const float FourHeartsFrom = 3000f;

    // The hull's own lives by ship id, before any colour (SkinHearts).
    public static int Base(int id)
    {
        if (!ShipId.IsValid(id)) return 3;
        if (id == ShipId.Starter) return StarterLives;
        if (id == MostExpensive) return MostBase;
        return shopingShips.CostFor(id) >= FourHeartsFrom ? 4 : 3;
    }

    // What ship `id` flies with right now: its hull plus what the colours
    // owned add (SkinHearts). Which skin is equipped doesn't matter.
    public static int Max(int id)
    {
        return Mathf.Clamp(Base(id) + SkinHearts.Bonus(id), 1, Most);
    }

    // The tutorial teaches on the bare hull: no colour hearts.
    public static int TutorialMax(int id) { return Base(id); }

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

    // Damage state for art (ShipDamageTable.StateFor): 0 intact (full),
    // 2 critical (one life left), 1 damaged in between (a two-heart ship goes
    // straight intact -> critical).
    public static int DamageState(int hitsTaken, int max) { return ShipDamageTable.StateFor(hitsTaken, max); }

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
