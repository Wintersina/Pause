using UnityEngine;

// The player's ship, announced once it is in the scene (movePlayer in gameS1,
// movePlayerInTut in the tutorial): the pieces that attach themselves to the
// ship a frame or two after the scene loads (the thruster, the power
// controller, the orbiting hearts) read Live or wait for Spawned instead of
// scanning the scene with FindFirstObjectByType every frame until it appears.
public static class ShipSpawn
{
    // The ship in the scene, null until its Start and after it is destroyed.
    public static GameObject Live { get; private set; }
    public static event System.Action<GameObject> Spawned;

    public static void Announce(GameObject ship)
    {
        if (ship == null) return;
        Live = ship;
        var handlers = Spawned;
        if (handlers != null) handlers(ship);
    }

    public static void Forget(GameObject ship)
    {
        if (Live == ship) Live = null;
    }

    // Tests.
    public static void Reset() { Live = null; Spawned = null; }
}
