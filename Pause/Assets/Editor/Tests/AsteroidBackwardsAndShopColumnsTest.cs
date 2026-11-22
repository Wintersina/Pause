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
            new Vector2Int(1080, 2640), new Vector2Int(1080, 2880), new Vector2Int(968, 2376),
        };
        foreach (var s in screens)
        {
            float size = CameraFit.ComputeSize(5f, 2.85f, s.x, s.y);
            float halfW = size * s.x / s.y;
            var layout = DockLayout.For(count, halfW * 2f, size * 2f - 2.6f);
            string tag = s.x + "x" + s.y;
            Check(tag + ": every ship has a bay", layout.columns * layout.rows >= count);
            Check(tag + ": the lot is no wider than the screen", layout.Width * layout.scale <= halfW * 2f + .001f);
            Check(tag + ": bays are never stretched past " + DockLayout.MaxScale + " (" + layout.scale.ToString("F2") + ")", layout.scale <= DockLayout.MaxScale + .001f);
            Check(tag + ": vertical runways, " + layout.lanes + " (1 to " + DockLayout.MaxLanes + ")", layout.lanes >= 1 && layout.lanes <= DockLayout.MaxLanes);
            Check(tag + ": 1 runway on phones (< .57), 2 on wide tablets (>= .7)",
                  s.x / (float)s.y >= .57f ? layout.lanes == 2 || s.x / (float)s.y < .7f : layout.lanes == 1);
            Check(tag + ": no two bays overlap", NoBayOverlap(layout));
        }

        // bays are 70-75% of the mockups' size: a phone fits more across than the mockups' three
        Check("bays are drawn at 75% of the mockup size", Mathf.Approximately(DockLayout.BayScale, .75f) &&
              Mathf.Approximately(DockLayout.BayW, 96f * .75f / 64f));
        // a parked hull is never bigger than the player's ship in play (reference hull x ShipScale.Main)
        Check("the lot's scale cap keeps a parked hull at or under the in-play ship (" + DockLayout.MaxScale.ToString("F3") + ")",
              DockBay.HullSize * DockLayout.MaxScale <= shopingShips.ReferenceHullSize * ShipScale.Main + .0001f);

        ShopRackSplitsWideViews(count);

        var narrow = DockLayout.For(count, 4.6f);
        float top, bottom;
        narrow.ScrollRange(1f, -1f, out top, out bottom);
        Check("a lot taller than its space scrolls (range > 0)", bottom - top > 0f);
        narrow.ScrollRange(30f, -30f, out top, out bottom);
        Check("a lot that fits is centred and does not scroll", Mathf.Approximately(top, bottom));
    }

    public static bool NoBayOverlap(DockLayout l)
    {
        Vector2 e = l.BayExtent * .5f;
        for (int a = 0; a < l.count; a++)
            for (int b = a + 1; b < l.count; b++)
            {
                Vector2 ca = l.BayCenter(a), cb = l.BayCenter(b);
                if (Mathf.Abs(ca.x - cb.x) < 2f * e.x - .0005f && Mathf.Abs(ca.y - cb.y) < 2f * e.y - .0005f) return false;
            }
        return true;
    }

    // Near-square views (Z Flip7 cover 948x1048, a fold's inner screen): the lot and the ship card sit side by
    // side; the lot is fitted to its own pane's width (never stretched), tall phones keep the single lot.
    static void ShopRackSplitsWideViews(int count)
    {
        foreach (var s in new[] { new Vector2Int(948, 1048), new Vector2Int(1812, 2176), new Vector2Int(1100, 800) })
        {
            string tag = s.x + "x" + s.y;
            float size = CameraFit.ComputeSize(5f, 2.85f, s.x, s.y);
            float halfH = size, halfW = size * s.x / s.y;
            Check(tag + ": a wide view", DockLayout.IsWide(halfW, halfH));
            // header (dust, instruction, start-world row) and footer: ~2.6 world units of a 10-unit view
            var view = new Rect(-halfW + .05f, -halfH + 1.1f, halfW * 2f - .1f, halfH * 2f - 2.6f);
            float cardW = 4.6f;
            Rect rackPane, cardPane;
            DockLayout.Panes(view, cardW, out rackPane, out cardPane);
            Check(tag + ": the panes tile the view, the card pane on the right",
                  Mathf.Approximately(rackPane.xMax, cardPane.xMin) && Mathf.Approximately(cardPane.xMax, view.xMax) &&
                  rackPane.xMin == view.xMin && cardPane.width <= view.width * DockLayout.CardShare + .001f);
            var layout = DockLayout.For(count, rackPane.width, rackPane.height);
            Check(tag + ": every ship has a bay (" + layout.lanes + " runways, " + layout.columns + " columns)", layout.columns * layout.rows >= count);
            Check(tag + ": the lot fits its pane's width", layout.Width * layout.scale <= rackPane.width + .001f);
            Check(tag + ": no two bays overlap", NoBayOverlap(layout));
        }
        float tall = CameraFit.ComputeSize(5f, 2.85f, 1080, 2520);
        Check("a tall phone is not a wide view", !DockLayout.IsWide(tall * 1080f / 2520f, tall));
        Check("a 4:3 portrait iPad is not a wide view", !DockLayout.IsWide(1536f / 2048f * 5f, 5f));
    }
}
