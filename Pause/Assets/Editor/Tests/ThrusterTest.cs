using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Verifies the idle flame is attached and positioned behind each hull.
public static class ThrusterTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[TH] PASS  " : "[TH] FAIL  ") + what);
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

        // Dock ships get a flame from the extender.
        EditorSceneLoader.Open("shopS6", OpenSceneMode.Single);
        ShopSceneExtender.Build();

        for (int i = 1; i < shopingShips.shipTotal; i++)
        {
            var ship = SceneUtil.FindAny("ship" + i);
            if (ship == null) continue;

            var th = ship.GetComponent<ShipThruster>();
            if (ShipExhaust.UsesWind(i))
            {
                Check("ship" + i + " (" + shopingShips.NameFor(i) + ") has no rotating flame", th == null);
                var boostHolder = SceneUtil.FindAny("Boost" + i);
                var boostRenderers = boostHolder != null ? boostHolder.GetComponentsInChildren<SpriteRenderer>(true) : null;
                bool noVisibleFlame = boostRenderers != null;
                if (boostRenderers != null)
                    foreach (var renderer in boostRenderers) noVisibleFlame &= !renderer.enabled;
                Check("ship" + i + " keeps its boost holder visually inert", noVisibleFlame);
                continue;
            }
            Check("ship" + i + " (" + shopingShips.NameFor(i) + ") has a thruster", th != null);
            if (th == null) continue;

            th.SendMessage("Start", SendMessageOptions.DontRequireReceiver);

            var flame = ship.transform.Find("~Thruster");
            Check("ship" + i + " flame was built", flame != null);
            if (flame == null) continue;

            var sr = flame.GetComponent<SpriteRenderer>();
            Check("ship" + i + " flame has art", sr != null && sr.sprite != null);

            // must sit behind the hull, not on top of it
            Check("ship" + i + " flame is behind the hull (y=" +
                  flame.localPosition.y.ToString("F2") + ")", flame.localPosition.y < 0f);

            var hull = ship.GetComponent<SpriteRenderer>();
            if (hull != null && sr != null)
                Check("ship" + i + " flame draws under the hull",
                      sr.sortingOrder < hull.sortingOrder);

            int expectedNozzles = ShipNozzles.For(i).Length;
            var idleNozzles = ship.GetComponentsInChildren<SpriteRenderer>(true);
            int idleFlames = 0;
            foreach (var renderer in idleNozzles)
                if (renderer.transform.name.StartsWith("~Thruster")) idleFlames++;
            Check("ship" + i + " has " + expectedNozzles + " idle flame nozzle(s)",
                  idleFlames == expectedNozzles);

            var boost = SceneUtil.FindAny("Boost" + i);
            int boostNozzles = boost == null ? 0 : boost.GetComponentsInChildren<SpriteRenderer>(true).Length -
                              (boost.GetComponent<SpriteRenderer>() != null ? 1 : 0);
            Check("ship" + i + " has " + expectedNozzles + " matching boost nozzle(s)",
                  boostNozzles == expectedNozzles);

            // Every plume sits on its own painted nozzle (ShipNozzles), not
            // on a shared bottom-edge rule.
            for (int n = 0; n < expectedNozzles; n++)
            {
                var plume = ship.transform.Find(n == 0 ? "~Thruster" : "~Thruster" + n);
                Vector2 want = ShipNozzles.ToLocal(hull.sprite, ShipNozzles.For(i)[n]);
                Check("ship" + i + " plume " + n + " leaves its nozzle",
                      plume != null && Vector2.Distance((Vector2)plume.localPosition, want) < .001f);
            }
        }

        Debug.Log("[TH] failures: " + fails);
        return fails;
    }
}
