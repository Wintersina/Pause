using System.Reflection;
using UnityEditor;
using UnityEngine;

// Bug: "the green atom is too large when it shows up". HealAtom.Spawn fitted
// the 1254 px art down to the red/blue atoms' 0.28 world units with a ~0.04
// localScale, but HealAtom.Update then overwrote localScale with a pulse
// around 1.0, so from the first frame on it drew ~25x too big. Its art also
// sat inside wide transparent margins, so even the fitted size was smaller
// than the red/blue atoms that fill their canvas.
//
// Spawns the green atom exactly as HealAtomSpawner does and the red/blue
// atoms exactly as spawnGoodStuff does, then compares rendered bounds at spawn
// and after the green atom's per-frame animation has run.
public static class GreenAtomSizeTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[GA] PASS  " : "[GA] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    static bool Near(float a, float b, float tol = 0.02f)
    {
        return Mathf.Abs(a - b) <= tol * Mathf.Max(Mathf.Abs(a), Mathf.Abs(b));
    }

    // Sprite bounds times lossyScale: the atom's on-screen footprint.
    static Vector2 Rendered(GameObject go)
    {
        var sr = go.GetComponent<SpriteRenderer>();
        Vector3 s = sr.sprite.bounds.size;
        Vector3 k = go.transform.lossyScale;
        return new Vector2(Mathf.Abs(s.x * k.x), Mathf.Abs(s.y * k.y));
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        var blue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/prefabs/atom3a.prefab");
        var red = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/prefabs/pauseAtom.prefab");
        Check("blue and red atom prefabs load", blue != null && red != null);
        if (blue == null || red == null) return fails;

        // spawnGoodStuff: AtomSpin.AddTo(Instantiate(Atom, pos, rot))
        var blueGo = AtomSpin.AddTo(Object.Instantiate(blue, Vector3.zero, Quaternion.identity));
        var redGo = AtomSpin.AddTo(Object.Instantiate(red, Vector3.zero, Quaternion.identity));
        // HealAtomSpawner / spawnGoodStuffTut: HealAtom.Spawn(pos)
        var green = HealAtom.Spawn(Vector3.zero);

        try
        {
            Vector2 b = Rendered(blueGo), r = Rendered(redGo), g = Rendered(green);
            Debug.Log("[GA] rendered size  blue=" + b.ToString("F4") + "  red=" + r.ToString("F4") +
                      "  green(spawn)=" + g.ToString("F4"));

            var gsr = green.GetComponent<SpriteRenderer>();
            Check("green atom uses the authored art", gsr.sprite != null && gsr.sprite.texture != null
                  && gsr.sprite.texture.width == 1254);
            Check("blue and red atoms are the same size (reference sanity)", Near(b.x, r.x) && Near(b.y, r.y));
            Check("green atom width matches the blue atom at spawn (" + g.x.ToString("F4") + " vs " + b.x.ToString("F4") + ")",
                  Near(g.x, b.x));
            Check("green atom height matches the blue atom at spawn (" + g.y.ToString("F4") + " vs " + b.y.ToString("F4") + ")",
                  Near(g.y, b.y));

            // The cropped sprite must still contain the whole visible art:
            // opaque pixels span 834 x 943 of the 1254 canvas.
            Rect rect = gsr.sprite.rect;
            Check("green sprite crop contains the visible art",
                  rect.xMin <= 210f && rect.xMax >= 1044f && rect.yMin <= 163f && rect.yMax >= 1106f);

            // Run the per-frame animation many times at different phases.
            var update = typeof(HealAtom).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            var heal = green.GetComponent<HealAtom>();
            float maxDev = 0f;
            for (int i = 0; i < 60; i++)
            {
                update.Invoke(heal, null);
                Vector2 now = Rendered(green);
                maxDev = Mathf.Max(maxDev, Mathf.Abs(now.x - b.x) / b.x, Mathf.Abs(now.y - b.y) / b.y);
            }
            Vector2 after = Rendered(green);
            Debug.Log("[GA] green(after update)=" + after.ToString("F4") + " maxDev=" + maxDev.ToString("P2"));
            Check("green atom stays the blue atom's size after its animation runs (max dev " +
                  maxDev.ToString("P2") + ")", maxDev <= 0.02f);

            // Pickup footprint: red/blue use a 0.28 box, green a circle of the
            // same diameter in world space.
            var box = blueGo.GetComponent<BoxCollider2D>();
            var circle = green.GetComponent<CircleCollider2D>();
            float boxWorld = box != null ? box.size.x * blueGo.transform.lossyScale.x : 0f;
            float circleWorld = circle != null ? circle.radius * 2f * green.transform.lossyScale.x : 0f;
            Debug.Log("[GA] collider  blue box=" + boxWorld.ToString("F4") + "  green circle diameter=" + circleWorld.ToString("F4"));
            Check("green collider diameter matches the blue atom's box", circle != null && box != null
                  && Near(circleWorld, boxWorld));
        }
        finally
        {
            Object.DestroyImmediate(blueGo);
            Object.DestroyImmediate(redGo);
            Object.DestroyImmediate(green);
        }

        return fails;
    }
}
