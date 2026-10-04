using UnityEngine;

// Guards against the collider regressing back to something bigger than the
// playfield itself. The playfield is roughly 5-6 world units wide (camera
// half-width floors at 2.85, see CameraFit), so any hazard collider anywhere
// near that size would let it hit the ship well before it's visually close.
//
// The rocks are every world's EnemyRoster rocks and heavy, built exactly as
// the spawner builds them (the old aestroid_* prefabs this used to load were
// deleted with their art).
public static class AsteroidColliderTest
{
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
        TestHarness.Exit(Execute());
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        int seen = 0;
        for (int world = 0; world < EnemyRoster.WorldKeys.Length; world++)
            foreach (var role in new[] { EnemyRole.Rock, EnemyRole.Big })
                foreach (var def in EnemyRoster.For(world, role))
                {
                    seen++;
                    var go = EnemyFactory.Create(def, Vector3.zero, Quaternion.identity);
                    string name = def.key;
                    var col = go != null ? go.GetComponentInChildren<BoxCollider2D>() : null;
                    Check(name + " has a BoxCollider2D", col != null);
                    if (col != null)
                    {
                        Vector3 scale = col.transform.lossyScale;
                        float worldW = col.size.x * Mathf.Abs(scale.x);
                        float worldH = col.size.y * Mathf.Abs(scale.y);

                        Check(string.Format("{0} world hitbox {1:F2}x{2:F2} fits the playfield",
                              name, worldW, worldH),
                              worldW <= MaxWorldSize && worldH <= MaxWorldSize);

                        // Also catch a collider so tiny it stops registering hits at all.
                        Check(name + " hitbox is not degenerate", worldW > 0.05f && worldH > 0.05f);
                    }
                    if (go != null) Object.DestroyImmediate(go);
                }
        Check("every world's rocks and heavy were checked (" + seen + ")", seen >= 8);

        Debug.Log("[ACT] failures: " + fails);
        return fails;
    }
}
