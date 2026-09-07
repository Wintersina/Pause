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
        // Dock ships get a flame from the extender.
        EditorSceneManager.OpenScene("Assets/Scenes/shopS6.unity", OpenSceneMode.Single);
        ShopSceneExtender.Build();

        for (int i = 1; i < shopingShips.shipTotal; i++)
        {
            var ship = SceneUtil.FindAny("ship" + i);
            if (ship == null) continue;

            var th = ship.GetComponent<ShipThruster>();
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
        }

        Debug.Log("[TH] failures: " + fails);
        EditorApplication.Exit(0);
    }
}
