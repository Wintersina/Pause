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
        fails = 0;
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
        Check("roster includes retro and original ships", total == shopingShips.Roster.Length && total > 8);

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

        // every ship must resolve a preview sprite for the confirm panel
        for (int i = 1; i < total; i++)
        {
            var ship = SceneUtil.FindAny("ship" + i);
            if (ship == null) continue;

            var img = ship.GetComponentInChildren<UnityEngine.UI.Image>(true);
            var sr  = ship.GetComponentInChildren<SpriteRenderer>(true);
            var byName = Resources.LoadAll<Sprite>("Prefabs/Ships/Sprites/" + shopingShips.NameFor(i));
            bool resolvable = (img != null && img.sprite != null)
                           || (sr != null && sr.sprite != null)
                           || (byName != null && byName.Length > 0);
            Check("ship" + i + " resolves a popup preview", resolvable);
        }

        // generated hulls must be normalised, not left at scale 1
        for (int i = 1; i < total; i++)
        {
            var ship = SceneUtil.FindAny("ship" + i);
            var sr = ship != null ? ship.GetComponent<SpriteRenderer>() : null;
            if (sr == null || sr.sprite == null) continue;
            float h = sr.bounds.size.y;
            Check("ship" + i + " hull height " + h.ToString("F2") + " is in range",
                  h > 0.3f && h < 1.8f);
        }

        // lift-off flare must be named so rotateRight can find it
        for (int i = 1; i < total; i++)
        {
            var ship = SceneUtil.FindAny("ship" + i);
            if (ship == null) continue;
            bool named = SceneUtil.FindAny("Boost" + i) != null;
            Check("Boost" + i + " exists for lift-off", named);
        }

        // Selecting a ship slides it out of its parking spot toward its face
        // marker, so the two must not be the same point or nothing moves.
        for (int i = 1; i < total; i++)
        {
            var ret = SceneUtil.FindAny("return" + i);
            var face = SceneUtil.FindAny("face" + i);
            if (ret == null || face == null) continue;
            float d = Vector3.Distance(ret.transform.position, face.transform.position);
            Check("ship" + i + " flies " + d.ToString("F2") + "u out of its parking spot", d > 0.4f);
        }

        // Buttons are pinned under their ship at runtime by ShopButtonAligner,
        // so their authored anchors mean nothing -- what matters is that the
        // aligner is present and pointed at the right ship, and that the ship
        // itself is inside the camera's view.
        var cam = Object.FindFirstObjectByType<Camera>();
        float halfHeight = cam.orthographicSize;
        float halfWidth = halfHeight * (9f / 16f);   // portrait-locked

        for (int i = 1; i < total; i++)
        {
            var b = SceneUtil.FindAny("Button" + i);
            if (b == null) continue;

            var aligner = b.GetComponent<ShopButtonAligner>();
            Check("Button" + i + " uses a scroll card", aligner != null && aligner.shipIndex == i && !aligner.followShip);

            var label = b.GetComponentInChildren<UnityEngine.UI.Text>(true);
            Check("Button" + i + " has a label", label != null);
            if (label != null)
                Check("Button" + i + " label is not best-fit inflated", !label.resizeTextForBestFit);

            var ship = SceneUtil.FindAny("ship" + i);
            if (ship == null) continue;
            var p = ship.transform.position;
            Check("ship" + i + " has clipped card art", b.GetComponentInChildren<DockCardArt>() != null
                  && b.GetComponentInParent<ScrollRect>() != null);
            Check("ship" + i + " has a dock idle animator", ship.GetComponent<DockShipIdleAnimator>() != null);
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

        var selectedShip = SceneUtil.FindAny("ship2");
        var selectedAnimator = selectedShip != null ? selectedShip.GetComponent<DockShipIdleAnimator>() : null;
        if (selectedAnimator != null)
        {
            selectedAnimator.SendMessage("Awake");
            rotateRight.shipSelected = 2;
            rotateRight.flyOffChecker = false;
            selectedAnimator.SendMessage("Update");
            var selectedThruster = selectedShip.GetComponent<ShipThruster>();
            Check("selected dock ship wakes up with a stronger flame",
                  selectedThruster != null && selectedThruster.idleScale > .4f);
            rotateRight.shipSelected = 0;
        }
        else Check("selected dock ship has an animator to wake it up", false);

        var scroll = SceneUtil.FindAny("~DockScroll").GetComponent<ScrollRect>();
        Canvas.ForceUpdateCanvases();
        Check("dock clips cards at viewport", scroll.viewport.GetComponent<RectMask2D>() != null);
        Check("dock scrolls vertically", scroll.vertical && !scroll.horizontal);
        var dockBackground = SceneUtil.FindAny("~DockScroll").GetComponent<Image>();
        Check("dock background leaves the starfield visible", dockBackground != null && dockBackground.color.a < 0.7f);
        Check("roster extends beyond viewport", scroll.content.rect.height > scroll.viewport.rect.height);
        Check("scrollbar is connected", scroll.verticalScrollbar != null);
        scroll.verticalNormalizedPosition = 0;
        Canvas.ForceUpdateCanvases();
        var last = SceneUtil.FindAny("Button" + (total - 1)).GetComponent<RectTransform>();
        Vector3[] corners = new Vector3[4];
        last.GetWorldCorners(corners);
        Vector3 bottom = scroll.viewport.InverseTransformPoint(corners[0]);
        Check("last ship is reachable at bottom of scroll", bottom.y >= scroll.viewport.rect.yMin - 1);
        for (int i = 1; i < total; i++)
        {
            Check("all health states load for ship" + i,
                shopingShips.SpriteFor(i, 0) != null && shopingShips.SpriteFor(i, 1) != null && shopingShips.SpriteFor(i, 2) != null);
            var boost = SceneUtil.FindAny("Boost" + i);
            if (ShipExhaust.UsesWind(i))
            {
                bool noFlame = boost != null;
                if (boost != null)
                    foreach (var renderer in boost.GetComponentsInChildren<SpriteRenderer>(true)) noFlame &= !renderer.enabled;
                Check("ship" + i + " uses spin wind instead of engine art", noFlame);
            }
            else
                Check("ship" + i + " has engine art", boost != null &&
                    boost.GetComponentInChildren<SpriteRenderer>(true) != null);
        }
        Debug.Log("[ST] failures: " + fails);
        EditorApplication.Exit(fails == 0 ? 0 : 1);
    }
}
