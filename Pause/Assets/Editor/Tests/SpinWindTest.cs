using UnityEditor;
using UnityEngine;

// Ninja and UFO are the only forward-flight spinners, so neither may acquire
// the standard rear flame used by the rest of the roster. They get their spin
// drift (ShipSpinDrift) instead: a ring that turns with the hull and a wake.
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
        TestHarness.Exit(Execute());
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        foreach (int index in new[] { 11, 13 })
        {
            string who = shopingShips.NameFor(index);
            Check(who + " is marked as a spinner", ShipExhaust.UsesWind(index) && ShipExhaust.UsesSpinDrift(index));
            var ship = new GameObject("ship" + index + "(Clone)", typeof(SpriteRenderer));
            ship.GetComponent<SpriteRenderer>().sprite = shopingShips.SpriteFor(index, 0);
            var drift = ship.AddComponent<ShipSpinDrift>();
            drift.respondToPause = false;
            drift.SendMessage("Start");
            var root = ship.transform.Find("~SpinDrift");
            Check(who + " builds its spin drift", root != null && drift.Ring != null && drift.Wake != null);
            Check(who + " has no thruster component", ship.GetComponent<ShipThruster>() == null);
            if (root == null) { Object.DestroyImmediate(ship); continue; }

            drift.Step(1f / 24f);
            Check(who + " drift shows its own ring and wake drawings",
                  drift.Ring.enabled && drift.Wake.enabled &&
                  drift.Ring.sprite != null && drift.Ring.sprite.name.StartsWith(index + "_Ring") &&
                  drift.Wake.sprite != null && drift.Wake.sprite.name.StartsWith(index + "_Wake"));
            Check(who + " ring is the hull's child, so it turns in sync with the spin",
                  drift.Ring.transform.IsChildOf(ship.transform));
            Check(who + " drift draws behind the hull",
                  drift.Ring.sortingOrder < ship.GetComponent<SpriteRenderer>().sortingOrder &&
                  drift.Wake.sortingOrder < ship.GetComponent<SpriteRenderer>().sortingOrder);

            // Spin the hull: the ring turns with it, the wake stays below.
            ship.transform.rotation = Quaternion.Euler(0f, 0f, 70f);
            drift.Step(1f / 24f);
            Check(who + " ring turns with the hull",
                  Mathf.Abs(Mathf.DeltaAngle(drift.Ring.transform.eulerAngles.z, 70f)) < .01f);
            Check(who + " wake stays world-aligned below the ship",
                  Mathf.Abs(Mathf.DeltaAngle(drift.Wake.transform.eulerAngles.z, 0f)) < .01f &&
                  drift.Wake.transform.position.y < ship.transform.position.y);

            var holder = new GameObject("Boost" + index);
            holder.tag = "boost";
            holder.transform.SetParent(ship.transform, false);
            drift.Rebuild();
            drift.Step(1f / 24f);
            Check(who + " lit boost holder switches to the boost drift", drift.ShowingBoost &&
                  drift.Ring.sprite.name.StartsWith(index + "_RingBoost"));
            holder.SetActive(false);
            drift.Step(1f / 24f);
            Check(who + " drift drops back when the boost ends", !drift.ShowingBoost);

            drift.powered = false;
            drift.Step(1f / 24f);
            Check(who + " powered-down drift draws nothing", !drift.Ring.enabled && !drift.Wake.enabled);
            Object.DestroyImmediate(ship);
        }
        Check("a standard ship remains nozzle-driven", !ShipExhaust.UsesWind(2));
        Debug.Log("[SW] failures: " + fails);
        return fails;
    }
}
