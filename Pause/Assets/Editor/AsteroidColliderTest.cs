using UnityEditor;
using UnityEngine;

// Guards against the collider regressing back to something bigger than the
// playfield itself. The playfield is roughly 5-6 world units wide (camera
// half-width floors at 2.85, see CameraFit), so any hazard collider anywhere
// near that size would let it hit the ship well before it's visually close.
public static class AsteroidColliderTest
{
    static readonly string[] Targets =
    {
        "aestroid_brown", "aestroid_brown_1",
        "aestroid_dark", "aestroid_dark_1",
        "aestroid_gay_1", "aestroid_gay_3",
        "aestroid_gray_crooked_1", "aestroid_gray_crooked_2",
    };

    // Generous relative to the ~0.47 the fitted colliders land on, but well
    // under the ~5-6 unit playfield width that caused the original bug.
    const float MaxWorldSize = 1.5f;

    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ACT] PASS  " : "[ACT] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        foreach (var name in Targets)
        {
            string path = "Assets/Resources/Prefabs/" + name + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Check(name + " prefab exists", prefab != null);
            if (prefab == null) continue;

            var col = prefab.GetComponentInChildren<BoxCollider2D>();
            Check(name + " has a BoxCollider2D", col != null);
            if (col == null) continue;

            float scale = prefab.transform.localScale.x;
            float worldW = col.size.x * scale;
            float worldH = col.size.y * scale;

            Check(string.Format("{0} world hitbox {1:F2}x{2:F2} fits the playfield",
                  name, worldW, worldH),
                  worldW <= MaxWorldSize && worldH <= MaxWorldSize);

            // Also catch a collider so tiny it stops registering hits at all.
            Check(name + " hitbox is not degenerate", worldW > 0.05f && worldH > 0.05f);
        }

        Debug.Log("[ACT] failures: " + fails);
        EditorApplication.Exit(0);
    }
}
