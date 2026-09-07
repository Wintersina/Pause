using UnityEngine;

// Per-ship special abilities.
//
// Each ship has one distinct power that recharges on a cooldown. The power
// fires automatically when it comes up -- the game is one-touch, so there is no
// spare input to bind a manual trigger to.
//
// Effects deliberately reuse the systems already in the game (tags "Enimey" /
// "Astr", collisionDetection.invTimer, score.incromentPause) rather than adding
// parallel machinery.
public enum ShipPower
{
    None = 0,
    Laser,        // ship 0  Rookie   - pierces a lane straight ahead
    Missiles,     // ship 1  Proteus  - homes on the nearest few targets
    Shockwave,    // ship 2  Amadeus  - clears everything close by
    Cloak,        // ship 3  Darkwing - brief invulnerability
    Railgun,      // ship 4  M237     - clears the two neighbouring lanes
    Magnet,       // ship 5  Cygnus   - pulls pickups toward the ship
    TimeDilation, // ship 6  Vesper   - slows the world for a moment
    Overcharge,   // ship 7  XR7      - grants extra pauses
}

public static class ShipPowerTable
{
    // Index matches shopingShips ship numbering.
    static readonly ShipPower[] byShip =
    {
        ShipPower.Laser,
        ShipPower.Missiles,
        ShipPower.Shockwave,
        ShipPower.Cloak,
        ShipPower.Railgun,
        ShipPower.Magnet,
        ShipPower.TimeDilation,
        ShipPower.Overcharge,
    };

    public static ShipPower For(int shipNumber)
    {
        if (shipNumber < 0 || shipNumber >= byShip.Length) return ShipPower.Laser;
        return byShip[shipNumber];
    }

    public static string DisplayName(ShipPower p)
    {
        switch (p)
        {
            case ShipPower.Laser:        return "LANCE";
            case ShipPower.Missiles:     return "SWARM MISSILES";
            case ShipPower.Shockwave:    return "SHOCKWAVE";
            case ShipPower.Cloak:        return "PHASE CLOAK";
            case ShipPower.Railgun:      return "RAILGUN";
            case ShipPower.Magnet:       return "TRACTOR FIELD";
            case ShipPower.TimeDilation: return "TIME DILATION";
            case ShipPower.Overcharge:   return "OVERCHARGE";
            default:                     return "";
        }
    }

    public static string Description(ShipPower p)
    {
        switch (p)
        {
            case ShipPower.Laser:        return "Fires a piercing beam straight ahead.";
            case ShipPower.Missiles:     return "Launches homing missiles at nearby targets.";
            case ShipPower.Shockwave:    return "Clears everything around the ship.";
            case ShipPower.Cloak:        return "Briefly phases out of danger.";
            case ShipPower.Railgun:      return "Clears the lanes either side of you.";
            case ShipPower.Magnet:       return "Pulls nearby pickups toward you.";
            case ShipPower.TimeDilation: return "Slows the world down for a moment.";
            case ShipPower.Overcharge:   return "Restores extra pauses.";
            default:                     return "";
        }
    }
}
