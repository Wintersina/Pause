using UnityEngine;

// Every ship that has a ShipArt/<Key>/<Key>_rotors.json (Turtle's four helicopter rotors): the file parses,
// every hub lies inside the ship's hull rect, spins at a non-zero speed, the rotor sheet loads, and
// ShipRotors.Attach builds one blade + blur renderer per rotor on a hull.
public static class ShipRotorsTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ROT] PASS  " : "[ROT] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        int withRotors = 0;
        for (int id = 1; id < shopingShips.shipTotal; id++)
        {
            string key = ShipId.ArtKeyOf(id);
            if (key == null || Resources.Load<TextAsset>(ShipRotors.DataPath(key)) == null) continue;
            withRotors++;
            string name = key;
            var file = ShipRotors.Load(key);
            Check(name + " rotors file parses with at least one rotor", file != null);
            if (file == null) continue;

            var r = ShipHullArt.RectFor(id);
            Check(name + " has a hull rect", ShipHullArt.Has(id) && r.w > 0 && r.h > 0);
            for (int i = 0; i < file.rotors.Length; i++)
            {
                var d = file.rotors[i];
                string at = name + " rotor " + i + " (" + d.x + "," + d.y + ")";
                float bottomY = ShipRotors.CellSize - d.y;   // hull rect is bottom-up
                Check(at + " hub lies inside the hull rect",
                      d.x >= r.x && d.x <= r.x + r.w && bottomY >= r.y && bottomY <= r.y + r.h);
                Check(at + " has a non-zero speed", Mathf.Abs(d.speed_deg_per_s) > 1f);
                Check(at + " has a sensible scale", d.scale > .2f && d.scale < 4f);
                Check(at + " has a direction", d.direction == 1f || d.direction == -1f);
            }

            var art = ShipRotors.SpritesFor(key);
            Check(name + " rotor sheet loads (blades + blur sprites)", art != null && art.Length == 2 && art[0] != null && art[1] != null);

            var go = new GameObject("ship" + id);
            var hull = go.AddComponent<SpriteRenderer>();
            hull.sprite = ShipHullArt.Rest(id, 0);
            var rotors = ShipRotors.Attach(go, id, hull, false);
            Check(name + " Attach builds the rotors", rotors != null && rotors.Count == file.rotors.Length);
            if (rotors != null)
            {
                Check(name + " rotors are children of the hull", go.GetComponentsInChildren<SpriteRenderer>().Length == 1 + 2 * file.rotors.Length);
                Check(name + " Attach is idempotent", ShipRotors.Attach(go, id, hull, false) == rotors);
            }
            Object.DestroyImmediate(go);
        }
        Check("at least one ship has rotors (Turtle)", withRotors >= 1);

        var bad = ShipRotors.Parse("{ \"rotors\": [] }");
        Check("an empty rotors list is treated as none", bad == null);
        Check("a ship without a rotors file gets none", !ShipRotors.Has(ShipId.FromKey("NeonComet")));

        Debug.Log("[ROT] failures: " + fails);
        return fails;
    }
}
