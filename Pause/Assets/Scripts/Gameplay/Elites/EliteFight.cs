using UnityEngine;

// The elite fight's hold on the board: while an elite is in play -- from its
// lift-off through its retreat or death -- the ordinary spawners run thin, so
// the encounter reads as a duel and not a pile-up. enmiesOnBoard reads
// SpawnScale (it multiplies the speed its spawn timers run at: enemies,
// rocks, mines, chasers, rails; bosses, portals, pickups and the world
// weather do not look at it). The scale drops to ThinScale the moment a fight
// begins and climbs back to 1 over RampSeconds once it is over.
//
// Stepped by EliteSystem.Step, i.e. on running frames only, so a pause
// freezes the ramp like everything else. Tests set Override / call Step with
// their own dt.
public static class EliteFight
{
    public const float ThinScale = .35f;     // share of the normal spawn rate during a fight (tunable)
    public const float RampSeconds = 2f;     // back to normal over this long after the last elite is gone

    static float scale = 1f;
    // Tests: ignore the live elites and use this (null: look at EliteShip.Live).
    public static bool? ActiveOverride;

    public static float SpawnScale => scale;

    // An elite is past its parking: lifting off, in play, hiding, retreating.
    public static bool Active
    {
        get
        {
            if (ActiveOverride.HasValue) return ActiveOverride.Value;
            var live = EliteShip.Live;
            for (int i = 0; i < live.Count; i++)
                if (live[i] != null && live[i].State != EliteState.Parked && live[i].State != EliteState.Dead) return true;
            return false;
        }
    }

    public static void Step(float dt)
    {
        if (Active) scale = ThinScale;
        else if (scale < 1f) scale = Mathf.Min(1f, scale + (1f - ThinScale) / RampSeconds * Mathf.Max(0f, dt));
    }

    public static void Reset() { scale = 1f; ActiveOverride = null; }
}
