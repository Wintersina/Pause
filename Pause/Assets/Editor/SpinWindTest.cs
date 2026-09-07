using UnityEditor;
using UnityEngine;

// Ninja and UFO are the only forward-flight spinners, so neither may acquire
// the standard rear flame used by the rest of the roster.
public static class SpinWindTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SW] PASS  " : "[SW] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        foreach (int index in new[] { 11, 13 })
        {
            Check(shopingShips.NameFor(index) + " is marked as a wind ship", ShipExhaust.UsesWind(index));
            var ship = new GameObject("ship" + index + "(Clone)", typeof(SpriteRenderer));
            var wind = ship.AddComponent<ShipSpinWind>();
            wind.SendMessage("Start");
            Check(shopingShips.NameFor(index) + " builds a wind effect", ship.transform.Find("~SpinWind") != null);
            Check(shopingShips.NameFor(index) + " has no thruster component", ship.GetComponent<ShipThruster>() == null);
            Object.DestroyImmediate(ship);
        }
        Check("a standard ship remains nozzle-driven", !ShipExhaust.UsesWind(2));
        Debug.Log("[SW] failures: " + fails);
        EditorApplication.Exit(fails == 0 ? 0 : 1);
    }
}
