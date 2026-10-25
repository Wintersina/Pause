using UnityEngine;

// Ship UI drawn at a fixed world size (charge indicator, secret meter) must
// follow the ship's growth out of the portal arrival (PortalArrival.ShipFactor),
// not tower over the tiny hull ("items are large around the ship" on the
// first-run tutorial fly-in).
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod PortalShipFactorTest.Run
public static class PortalShipFactorTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[PSF] PASS  " : "[PSF] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        var ship = new GameObject("ship");
        ship.transform.localScale = new Vector3(.8f, .8f, 1f);
        var other = new GameObject("other");
        Check("no arrival: factor is 1", Mathf.Approximately(PortalArrival.ShipFactor(ship.transform), 1f));
        var arrival = PortalArrival.SpawnDriven(ship.transform, Color.white);
        float start = PortalArrival.ShipFactor(ship.transform);
        Check("at the start of the fly-in the factor is small (" + start.ToString("0.00") + ")", start < .3f);
        Check("another transform is unaffected", Mathf.Approximately(PortalArrival.ShipFactor(other.transform), 1f));
        for (int i = 0; i < 600 && arrival.State != PortalArrival.Stage.Done; i++) arrival.Step(1f / 60f);
        Check("the fly-in ends", arrival.State == PortalArrival.Stage.Done);
        Check("after the fly-in the factor is 1", Mathf.Approximately(PortalArrival.ShipFactor(ship.transform), 1f));
        Object.DestroyImmediate(ship);
        Object.DestroyImmediate(other);
        return fails;
    }
}
