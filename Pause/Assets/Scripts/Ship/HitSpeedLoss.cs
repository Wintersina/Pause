using UnityEngine;

// A heart lost slows the ship, a little less each time.
//
// The ship's forward speed ramps up over the run (SpeedRamp) and the speed
// decides how soon the next boss arrives (a world is a fixed DISTANCE:
// WorldManager.distanceLeft). A hit that costs a heart therefore also costs
// some progress toward the boss: the natural speed drops by this table's
// entry for the run's Nth hit, never below the speed the run started at, and
// then climbs back on the normal ramp.
//
//   hit of the run     1st  2nd  3rd  4th  5th and every later one
//   HUD speed lost      5    4    3    2    1
//
// (HUD units: 5 is moveBackGround.speed .05. The early ramp gains about 0.3 HUD
// per second, so the first hit is worth roughly 15 s of climbing back.)
//
// What counts as a hit: the same event as a lost heart (collisionDetection's
// unshielded branch, where ScoreMultiplier.OnHeartLost fires). Shield, Cloak,
// post-hit i-frames and a Hazard Clear save are no hit; a banked green atom
// that takes the hit still counts (the heart was spent). Every such hit counts
// toward the table, but the speed is only cut when it is the ramp's to give: a
// boss holds the speed (BossEncounter.SpeedLocked) and the next world re-enters
// at the start speed anyway, and a planetfall/portal entry waits too.
// Never in the tutorial. The blue atom's boost rides on top untouched.
public static class HitSpeedLoss
{
    // The table, in HUD speed units. The last entry holds for every later hit.
    public static readonly int[] LossHud = { 5, 4, 3, 2, 1 };

    static int hits;

    public static int Hits { get { return hits; } }

    public static void BeginRun() { hits = 0; }

    // HUD speed lost to the `hitNumber`th hit of the run (1 = the first).
    public static int LossFor(int hitNumber)
    {
        if (hitNumber < 1) return 0;
        return LossHud[Mathf.Min(hitNumber, LossHud.Length) - 1];
    }

    // The natural speed after the `hitNumber`th hit: the table's loss off
    // `natural`, never below `startSpeed` (and a speed already under it stays).
    public static float After(float natural, float startSpeed, int hitNumber)
    {
        float cut = natural - LossFor(hitNumber) / 100f;
        return Mathf.Min(natural, Mathf.Max(cut, startSpeed));
    }

    // A heart was lost (collisionDetection).
    public static void OnHit()
    {
        hits++;
        if (BossEncounter.SpeedLocked || WorldEntry.Active) return;
        SpeedRamp.SetNatural(After(SpeedRamp.Natural, WorldManager.ArrivalSpeed(RunLoop.Index), hits));
    }
}
