using UnityEngine;

// How high an armed, piloted hostile -- a roster pilot (EnemyBrain) or an
// elite (EliteShip) -- may hover: inside the player's reach, so a pause
// jump (TeleportFx: the ship lands anywhere in ShipReach's range and erases
// what is within BlastRadius of the landing) can land on it, and a
// shielded ship (blue atom / Cloak) can fly into it. The one place those
// heights are decided; every number is read from the live reach
// (ShipReach.Top, HullAbove) and the jump (TeleportFx.BlastRadius), never
// a constant height.
//
//   CEILING   the highest a hostile holds while it is not about to fire:
//             its collider's bottom ContactBite under the top of the
//             ship's hull with the ship at the top of its reach -- a ship
//             flown up under it touches it, and a jump landing at the top
//             of the reach is well inside BlastRadius of it.
//   STANDOFF  never closer above the ship than this: the ship's hull top
//             plus the hostile's drawn half plus StandoffGap. With the ship
//             parked high it wins over the ceiling, so nothing sits on a
//             parked hull (its body still in jump range: HullAbove +
//             StandoffGap < BlastRadius).
//   RISE      the fire rules are unchanged (EnemyBrain.MinFireAbove /
//             MinFireDistance x view, each elite brain's MinAttackAbove):
//             when the ship is so close under a hostile that it could not
//             fire from its reach hold, it climbs for its attack -- a pilot
//             to the lowest fair firing height (EnemyBrain.HoldY), an elite
//             to its brain's own attack height plus EliteRiseMargin --
//             starting RiseLead seconds before it is ready, at most
//             RiseMaxSeconds if the attack never starts, and drops back
//             into reach after it. No pilot shot starts inside the fire
//             clearance; no elite attacks below its brain's own height.
public static class HostileReach
{
    // ---- tunables ----
    // false: the old heights (pilots back up to HoldY, elites fly their brain's goal).
    public static bool Enabled = true;
    // At the ceiling a hostile's collider overlaps the hull of a ship at the top of its reach by this much (u).
    public const float ContactBite = .15f;
    // Clear air between the ship's hull top and a hostile's drawn bottom at the standoff (u).
    public const float StandoffGap = .2f;
    // A hostile starts climbing to its firing height this long before its next attack may start (s).
    public const float RiseLead = .3f;
    // A pilot climbs only when that gains it at least this much (u).
    public const float MinRise = .05f;
    // An elite climbs to its brain's attack height (MinAttackAbove over the pilot) plus this (u).
    public const float EliteRiseMargin = .35f;
    // ... climbing / dropping at this speed (u/s, x EnemyBrain.ViewScale), never slower than its own hold speed.
    public const float PilotRiseSpeed = 2.6f;
    // Risen this long without its attack starting (the shot budget, its column): it drops back (s).
    public const float RiseMaxSeconds = 2.5f;
    // A jump counts as reaching a hostile with this much of BlastRadius to spare (tests, the probe).
    public const float JumpSlack = .1f;

    // ---- pure (tests) ----

    // The highest centre a hostile whose collider reaches `colliderBelow`
    // under its centre holds, the ship's reach topping out at `shipTop`.
    public static float CeilingFor(float shipTop, float colliderBelow)
    {
        return shipTop + ShipReach.HullAbove + colliderBelow - ContactBite;
    }

    // How far above the ship's centre a hostile drawn `drawnBelow` under its
    // centre keeps at least.
    public static float StandoffFor(float drawnBelow)
    {
        return ShipReach.HullAbove + drawnBelow + StandoffGap;
    }

    // The highest it holds with the ship at `shipY`: the ceiling, or the
    // standoff above the ship where that is higher.
    public static float CapFor(float shipTop, float shipY, float colliderBelow, float drawnBelow)
    {
        return Mathf.Max(CeilingFor(shipTop, colliderBelow), shipY + StandoffFor(drawnBelow));
    }

    // Can a pause jump land on a collider whose bottom is `bottomY` (centre
    // x `x`, half-width `halfX`)? The ship lands anywhere in its range
    // [-halfWidth, halfWidth] x [.., shipTop], erasing within BlastRadius.
    public static bool JumpReaches(float x, float bottomY, float halfX, float shipTop, float halfWidth)
    {
        float dx = Mathf.Max(0f, Mathf.Abs(x) - halfX - halfWidth);
        float dy = Mathf.Max(0f, bottomY - shipTop);
        return dx * dx + dy * dy <= (TeleportFx.BlastRadius - JumpSlack) * (TeleportFx.BlastRadius - JumpSlack);
    }

    // Can the ship's hull (flown anywhere in its range) touch a collider
    // whose bottom is `bottomY`? (Sideways the hull reaches the rails' inner
    // edge; the lane is inside them.)
    public static bool RamReaches(float bottomY, float shipTop)
    {
        return bottomY < shipTop + ShipReach.HullAbove;
    }

    // ---- live ----
    public static float Ceiling(float colliderBelow) => CeilingFor(ShipReach.Top, colliderBelow);
    public static float Cap(float shipY, float colliderBelow, float drawnBelow) => CapFor(ShipReach.Top, shipY, colliderBelow, drawnBelow);
}
