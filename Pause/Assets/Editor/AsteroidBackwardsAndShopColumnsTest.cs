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
//   2. DockScrollView.RefreshLayout()'s flat 540px viewport-width threshold
//      for two columns was tuned against Editor/Mac aspect ratios and fell
//      short on real (tall, narrow) Android phones, silently dropping to one
//      column exactly where two was wanted.
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
        AsteroidsTravelDownRegardlessOfSpin();
        ShopGetsTwoColumnsOnARealPhoneViewport();

        Debug.Log("[AS] failures: " + fails);
        EditorApplication.Exit(0);
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

    static void ShopGetsTwoColumnsOnARealPhoneViewport()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/shopS6.unity", OpenSceneMode.Single);

        var canvasGo = new GameObject("~DockLayoutTestCanvas", typeof(RectTransform));
        var rt = canvasGo.GetComponent<RectTransform>();

        var scrollGo = new GameObject("~ScrollRect", typeof(RectTransform), typeof(ScrollRect));
        scrollGo.transform.SetParent(canvasGo.transform, false);
        var scroll = scrollGo.GetComponent<ScrollRect>();

        var viewportGo = new GameObject("~Viewport", typeof(RectTransform));
        viewportGo.transform.SetParent(scrollGo.transform, false);
        var viewportRt = viewportGo.GetComponent<RectTransform>();
        scroll.viewport = viewportRt;

        var contentGo = new GameObject("~Content", typeof(RectTransform), typeof(GridLayoutGroup));
        contentGo.transform.SetParent(viewportGo.transform, false);
        scroll.content = contentGo.GetComponent<RectTransform>();
        var grid = contentGo.GetComponent<GridLayoutGroup>();

        var controllerGo = new GameObject("~DockScrollViewTest");
        var controller = controllerGo.AddComponent<DockScrollView>();
        controller.scroll = scroll;
        SetPrivate(controller, "content", scroll.content);
        SetPrivate(controller, "grid", grid);

        // A real, common tall Android phone (e.g. 1080x2400) works out to a
        // viewport width around here once CanvasScaler's width/height blend
        // and this dock's own chrome margins are applied -- comfortably
        // above the fixed minimum two-column width, but was below the old
        // flat 540px threshold.
        SetViewportWidth(viewportRt, 480f);
        controller.RefreshLayout();
        Check("a realistic tall-phone viewport (480px) now gets two columns (was one)",
              grid.constraintCount == 2);
        Check("cell width at that viewport doesn't overflow past the available width",
              grid.cellSize.x * 2f + 16f + 16f <= 480f + 0.5f);

        // Genuinely too narrow for even a tight two columns should still
        // fall back to one rather than forcing overflowing cells.
        SetPrivate(controller, "lastWidth", -1f);
        SetViewportWidth(viewportRt, 300f);
        controller.RefreshLayout();
        Check("a genuinely too-narrow viewport (300px) still falls back to one column",
              grid.constraintCount == 1);

        Object.DestroyImmediate(controllerGo);
        Object.DestroyImmediate(canvasGo);
    }

    static void SetViewportWidth(RectTransform rt, float width)
    {
        rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
    }

    static void SetPrivate(object target, string field, object value)
    {
        var f = target.GetType().GetField(field,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        f.SetValue(target, value);
    }
}
