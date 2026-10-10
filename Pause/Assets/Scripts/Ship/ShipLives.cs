using UnityEngine;

// How many lives (hearts) each ship flies with -- the one table, and the one
// accessor everything reads.
//
// The hull's own lives (Base) are its ROW in the dock's ship grid, counted
// top to bottom as the player sees it (the dock lays ships out cheapest
// first, three to a row: SpaceDock.BayOrder / DockLayout):
//   row 1 (Neon Comet, the free starter, + the two cheapest)   1 heart
//   row 2 .. row 5 (the dearest three: Ion, Jade, Gold)        2 .. 5
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
    public const int Fewest = 1;
    // The most hearts any hull has of its own: the bottom row of the grid.
    public static int MostBase { get { return Rows; } }
    // The most any ship can fly with: a bottom-row hull with every colour and
    // the complete set (the heart orbit is built for this many).
    public static int Most { get { return MostBase + SkinHearts.MostFromColours + SkinHearts.AllSkinsBonus; } }

    // Rows in the dock grid (the roster over the dock's column count).
    public static int Rows
    {
        get { return Mathf.CeilToInt(ShipId.Count / (float)DockLayout.MaxColumns); }
    }

    // The ship's row in the dock grid, 1 = top. 0 for an unknown ship.
    // Derived from the berth order, so it can't drift from what's on screen.
    public static int Row(int id)
    {
        return SpaceDock.RowOf(id);
    }

    // The hull's own lives by ship id, before any colour (SkinHearts): its row.
    public static int Base(int id)
    {
        int row = Row(id);
        return row > 0 ? row : Fewest;
    }

    // What ship `id` flies with right now: its hull plus what the colours
    // owned add (SkinHearts). Which skin is equipped doesn't matter.
    public static int Max(int id)
    {
        return Mathf.Clamp(Base(id) + SkinHearts.Bonus(id), 1, Most);
    }

    // The tutorial teaches on the bare hull (no colour hearts) with two extra
    // hearts orbiting it, so the alien's crash costs one and can never end the
    // lesson (Hints tops them back up). Only the tutorial scene flies this;
    // every real run uses Max.
    public const int TutorialExtraHearts = 2;
    public static int TutorialMax(int id) { return Mathf.Max(Base(id) + TutorialExtraHearts, TutorialExtraHearts + 1); }

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
    // 2 critical (one life left), 1 damaged in between (a two-heart ship goes straight intact ->
    // critical; a one-heart ship dies on its first hit).
    public static int DamageState(int hitsTaken, int max) { return ShipDamageTable.StateFor(hitsTaken, max); }

    public static int RunDamageState
    {
        get { return DamageState(collisionDetection.lifeCounter, RunMax); }
    }
}
