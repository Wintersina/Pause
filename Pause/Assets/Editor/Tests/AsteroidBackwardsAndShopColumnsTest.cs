using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Covers two follow-up bugs from the previous asteroid-spin/shop-columns work:
//
//   1. moveEnimes.moveEnim() used Translate()'s default local space. That was
//      harmless before anything rotated the transform, but AsteroidSpin now
//      does, and a rotating local "down" swings the actual travel direction
//      away from straight down -- reported as asteroids "going backwards".
//   2. The shop's columns were tuned against Editor/Mac aspect ratios and
//      fell short on real (tall, narrow) Android phones. The space dock's
//      DockLayout is now checked against real phone aspects directly.
public static class AsteroidBackwardsAndShopColumnsTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[AS] PASS  " : "[AS] FAIL  ") + what);
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

        AsteroidsTravelDownRegardlessOfSpin();
        ShopRackFitsPhoneViewports();

        Debug.Log("[AS] failures: " + fails);
        return fails;
    }

    // Time.deltaTime is always exactly 0 outside Play mode (confirmed: a
    // batch -executeMethod run never ticks the player loop), so moveEnim()'s
    // actual deltaTime-scaled Translate() can't be driven through Update()
    // here -- it would move by zero regardless of which space it uses, and
    // "still didn't move" would trivially pass whether the fix is present or
    // reverted. This instead proves the mechanism the fix relies on (a
    // world-space Translate is rotation-invariant; local-space is not, which
    // is exactly the bug) against a bare transform with a fixed, non-zero
    // offset, and separately pins moveEnimes.cs's own source to actually
    // pass Space.World, so a revert to the default local space is caught.
    static void AsteroidsTravelDownRegardlessOfSpin()
    {
        var t = new GameObject("~TranslateSpaceCheck").transform;
        t.rotation = Quaternion.Euler(0, 0, 180f); // upside down, as a mid-spin asteroid would be
        t.Translate(Vector3.down * 0.5f, Space.World);
        Check("Space.World keeps \"down\" pointing down even when the object is rotated 180 -- " +
              "this is the fix; Space.Self (the old default) would instead move it up",
              t.position.y < 0f);
        Object.DestroyImmediate(t.gameObject);

        string source = System.IO.File.ReadAllText("Assets/Scripts/Gameplay/moveEnimes.cs");
        Check("moveEnimes.cs actually passes Space.World to Translate (not the local-space default)",
              System.Text.RegularExpressions.Regex.IsMatch(source, @"Translate\([^;]*Space\.World\)"));
    }

    // The dock rack must fit every phone aspect CameraFit can produce: at
    // least three columns, never wider than the screen, and on portrait
    // phones the whole roster fits without scrolling.
    static void ShopRackFitsPhoneViewports()
    {
        int count = shopingShips.shipTotal - 1;
        var screens = new[]
        {
            new Vector2Int(1080, 1920), new Vector2Int(1080, 2400), new Vector2Int(1080, 2520),
            new Vector2Int(720, 1280), new Vector2Int(1536, 2048), new Vector2Int(1100, 800),
        };
        foreach (var s in screens)
        {
            float size = CameraFit.ComputeSize(5f, 2.85f, s.x, s.y);
            float halfW = size * s.x / s.y;
            var layout = DockLayout.For(count, halfW);
            string tag = s.x + "x" + s.y;
            Check(tag + ": at least three berth columns", layout.columns >= 3);
            Check(tag + ": every ship has a berth", layout.columns * layout.rows >= count);
            Check(tag + ": the rack is no wider than the screen",
                  layout.Width * layout.scale <= halfW * 2f + .001f);
            Check(tag + ": berths are not shrunk much", layout.scale > .9f);
            Vector2 a = layout.BayCenter(0), b = layout.BayCenter(1);
            Check(tag + ": neighbouring berths do not overlap", b.x - a.x >= DockLayout.BaySize.x - .001f);
            if (s.y > s.x)
            {
                // ~1.9 world units of header + footer HUD on a portrait phone.
                Check(tag + ": the whole roster fits between header and footer",
                      layout.Height * layout.scale <= size * 2f - 1.9f);
            }
        }

        var narrow = DockLayout.For(count, 2.85f);
        float top, bottom;
        narrow.ScrollRange(1f, -1f, out top, out bottom);
        Check("a rack taller than its space scrolls (range > 0)", bottom - top > 0f);
        narrow.ScrollRange(10f, -10f, out top, out bottom);
        Check("a rack that fits is centred and does not scroll", Mathf.Approximately(top, bottom));
    }
}
