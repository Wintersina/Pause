using System.Collections.Generic;
using UnityEngine;

// The ONE place the run HUD's booster icons (BoosterHud) read booster state, through the boosters' read-only API
// (Boosters.IsActive / IsTimed / SecondsLeft / ChargesLeft): a change to that API is a change to this file only.
//
// What counts as "showing":
//   DUST x2       time left on the multiplier (Boosters.RemainingSeconds), in a real run (practice runs never multiply).
//   HOMING ATOMS  time left on homing (Boosters.HomingRemainingSeconds), in a real run.
//   HAZARD CLEAR  at least one charge held (Boosters.ClearCharges); not armed (dimmed) while a scripted beat holds the ship.
// Nothing shows in the tutorial, a practice run, a menu, or once the pilot is dead, and a booster the pilot switched off
// in the Shop (Boosters.IsOn) never shows.
//
// The timers are the booster clocks themselves: Boosters.Tick only runs on unfrozen real-run frames (score.payDust), so a
// frozen world, a scripted entrance or the tutorial freezes the countdown with it. The HUD never keeps its own clock.
public static class BoosterHudSource
{
    public struct Entry
    {
        public Boosters.Kind kind;
        public bool timed;          // a countdown (seconds) rather than a count
        public float secondsLeft;   // timed only
        public int count;           // counted only
        public bool armed;          // false: shown dimmed (Hazard Clear held while it cannot fire)

        // "9:59" under a timed icon, "x2" under a counted one.
        public string Label { get { return timed ? Boosters.FormatTime(secondsLeft) : "x" + count; } }
    }

    // Top to bottom: the two timed boosters, then the held charges.
    public static readonly Boosters.Kind[] Order = { Boosters.Kind.DustMultiplier, Boosters.Kind.HomingAtoms, Boosters.Kind.HazardClear };

    // The Shop's icon file for each booster (Resources/ShopIcons, ShopIconArt).
    public static string IconName(Boosters.Kind kind)
    {
        switch (kind)
        {
            case Boosters.Kind.DustMultiplier: return "booster_dust_x2";
            case Boosters.Kind.HomingAtoms: return "booster_homing_atoms";
            default: return "booster_hazard_clear";
        }
    }

    // A live real run the boosters apply to (not the tutorial, a practice run or a menu), and the pilot is alive.
    public static bool InRun
    {
        get { return score.paysRealDust && !startMenu.youAreInTutorial && !buttonClicks.playerDied && !DeathCrash.Running; }
    }

    // One booster's entry; false when it is not showing: it must be held AND switched on (Boosters.IsActive), in a live run.
    public static bool Read(Boosters.Kind kind, out Entry e)
    {
        e = new Entry { kind = kind, armed = true, timed = Boosters.IsTimed(kind) };
        if (!InRun || !Boosters.IsActive(kind)) return false;
        if (e.timed) e.secondsLeft = Boosters.SecondsLeft(kind);
        else
        {
            e.count = Boosters.ChargesLeft(kind);
            e.armed = HazardClear.BlockedBy() == HazardClear.Block.None;
        }
        return true;
    }

    // Every showing booster, in Order. Returns the count. Allocates nothing (reuse `into`).
    public static int Read(List<Entry> into)
    {
        into.Clear();
        for (int i = 0; i < Order.Length; i++)
        {
            Entry e;
            if (Read(Order[i], out e)) into.Add(e);
        }
        return into.Count;
    }
}
