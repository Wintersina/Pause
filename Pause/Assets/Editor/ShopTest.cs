using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class ShopTest
{
    static int fails;

    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ST] PASS  " : "[ST] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/shopS6.unity", OpenSceneMode.Single);

        // the bug that hid every button: Find() cannot see inactive objects
        Check("GameObject.Find cannot see the inactive Canvas (the original bug)",
              GameObject.Find("Canvas") == null);
        Check("SceneUtil.FindAny does find it",
              SceneUtil.FindAny("Canvas") != null);
        Check("SceneUtil.FindAny finds PopUpCanvas too",
              SceneUtil.FindAny("PopUpCanvas") != null);

        ShopSceneExtender.Build();

        int total = shopingShips.shipTotal;
        Check("roster is 8", total == 8);

        for (int i = 1; i < total; i++)
        {
            var ship = SceneUtil.FindAny("ship" + i);
            var face = SceneUtil.FindAny("face" + i);
            var ret  = SceneUtil.FindAny("return" + i);
            var btn  = SceneUtil.FindAny("Button" + i);

            Check("ship" + i + " exists", ship != null);
            Check("face" + i + " exists", face != null);
            Check("return" + i + " exists", ret != null);
            Check("Button" + i + " exists with a Button component",
                  btn != null && btn.GetComponent<Button>() != null);

            if (ship != null)
            {
                var sr = ship.GetComponent<SpriteRenderer>();
                Check("ship" + i + " (" + shopingShips.NameFor(i) + ") has art",
                      sr != null && sr.sprite != null);
            }
        }

        // no two ships stacked on the same slot
        for (int i = 1; i < total; i++)
        for (int j = i + 1; j < total; j++)
        {
            var a = SceneUtil.FindAny("ship" + i);
            var b = SceneUtil.FindAny("ship" + j);
            if (a == null || b == null) continue;
            Check("ship" + i + " and ship" + j + " occupy different slots",
                  Vector3.Distance(a.transform.position, b.transform.position) > 0.05f);
        }

        Debug.Log("[ST] failures: " + fails);
        EditorApplication.Exit(0);
    }
}
