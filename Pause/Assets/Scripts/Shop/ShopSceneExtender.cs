using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Fills in the shop objects the scene does not have.
//
// shopS6 was authored for a four-ship roster, so it only contains ship1-4 and
// face/return markers for the first three. Rather than hand-authoring the rest
// into the scene, the missing pieces are built here from the same grid the
// authored ones already sit on -- so growing the roster stays a code change.
//
// Runs before shopingShips.Start() via a lower script execution order is not
// required: it runs from sceneLoaded, which fires before any Start().
public class ShopSceneExtender : MonoBehaviour
{
    // Matches the authored layout exactly:
    //   ship1 (-0.5, 3)  ship2 (0.5, 3)
    //   ship3 (-0.5, 2)  ship4 (0.5, 2)   ...
    // face markers sit one unit further out than their ship.
    // Camera is orthographic size 5, so the view is y in [-5, 5] and about
    // x in [-2.8, 2.8] on a portrait phone. Seven ships sit in two columns.
    const float ColumnX = 1.15f;
    const float FaceX = 2.30f;
    const float TopY = 3.15f;
    const float RowStep = 1.55f;

    // Authored previews are hand-scaled so every hull ends up roughly this tall
    // in world units, regardless of how large its source texture is -- Darkwing
    // is 15401px wide and sits at scale 0.03, Proteus is 400px at scale 0.60.
    // Generated ships are normalised to the same visual size instead of being
    // left at scale 1, which is why they towered over the rest.
    const float TargetHullHeight = 0.72f;

    public static Vector3 ShipSlot(int i)
    {
        int col = (i - 1) % 2, row = (i - 1) / 2;
        return new Vector3(col == 0 ? -ColumnX : ColumnX, TopY - row * RowStep, 0f);
    }

    public static Vector3 FaceSlot(int i)
    {
        int col = (i - 1) % 2;
        var p = ShipSlot(i);
        return new Vector3(col == 0 ? -FaceX : FaceX, p.y, 0f);
    }

    public static void Build()
    {
        int total = shopingShips.shipTotal;

        var canvas = SceneUtil.FindAny("Canvas");
        var templateButton = SceneUtil.FindAny("Button3");

        for (int i = 1; i < total; i++)
        {
            EnsureMarker("face" + i, FaceSlot(i));
            EnsureMarker("return" + i, ShipSlot(i));
            EnsureShip(i);
            EnsureButton(i, canvas, templateButton);
        }
    }

    // face/return are pure position markers; rotateRight only reads .position.
    static void EnsureMarker(string name, Vector3 pos)
    {
        var go = SceneUtil.FindAny(name);
        if (go == null) go = new GameObject(name);
        // Markers authored as UI RectTransforms keep their own placement.
        if (go.transform is RectTransform) return;
        go.transform.position = pos;
    }

    static void EnsureShip(int index)
    {
        var go = SceneUtil.FindAny("ship" + index);
        if (go == null)
        {
            go = new GameObject("ship" + index);
            go.AddComponent<SpriteRenderer>();
        }

        go.transform.position = ShipSlot(index);

        var sr = go.GetComponent<SpriteRenderer>();
        if (sr == null) sr = go.AddComponent<SpriteRenderer>();

        // Always use the undamaged frame -- some authored previews pointed at a
        // battle-scarred one.
        var sprite = LoadShipSprite(index);
        if (sprite != null) sr.sprite = sprite;
        sr.sortingOrder = 5;

        // Every hull is normalised now. The authored scales were hand-tuned for
        // a four-ship dock and are much too large once seven have to fit.
        NormaliseScale(go, sr);

        EnsureBoost(go, index);
    }

    static void NormaliseScale(GameObject go, SpriteRenderer sr)
    {
        if (sr.sprite == null) return;
        float h = sr.sprite.bounds.size.y;
        if (h <= 0.0001f) return;
        float k = TargetHullHeight / h;
        go.transform.localScale = new Vector3(k, k, 1f);
    }

    // First damage frame of the ship's sheet, matching what lifeControler loads.
    static Sprite LoadShipSprite(int index)
    {
        string name = shopingShips.NameFor(index);
        if (string.IsNullOrEmpty(name)) return null;

        var frames = Resources.LoadAll<Sprite>("Prefabs/Ships/Sprites/" + name);
        return (frames != null && frames.Length > 0) ? frames[0] : null;
    }

    // rotateRight looks these up by the exact name "Boost<N>", so the generated
    // ones must match or the lift-off flare silently never appears.
    static void EnsureBoost(GameObject ship, int index)
    {
        foreach (Transform c in ship.transform)
            if (c.name.StartsWith("Boost")) return;

        var boost = new GameObject("Boost" + index);
        boost.transform.SetParent(ship.transform, false);
        boost.transform.localPosition = new Vector3(0f, -0.6f, 0f);
        boost.AddComponent<SpriteRenderer>().sortingOrder = 4;
        boost.SetActive(false);
    }

    static void EnsureButton(int index, GameObject canvas, GameObject template)
    {
        var go = SceneUtil.FindAny("Button" + index);

        if (go == null)
        {
            if (canvas == null || template == null) return;
            go = Instantiate(template, template.transform.parent);
            go.name = "Button" + index;
            // shipselected() keys off the button's name, so the cloned handler
            // is already correct -- nothing else to wire.
        }

        var rt = go.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.sizeDelta = new Vector2(210f, 44f);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        // Tap targets sit over the ships, so the plate itself stays subtle.
        var img = go.GetComponent<Image>();
        if (img != null)
        {
            img.color = new Color(0.07f, 0.09f, 0.14f, 0.55f);
            img.raycastTarget = true;
        }

        EnsureLabel(go);

        // Keep each button pinned beneath its ship at runtime, where the real
        // screen size is known -- the world grid and the canvas do not share a
        // coordinate space, so this cannot be baked in.
        var aligner = go.GetComponent<ShopButtonAligner>();
        if (aligner == null) aligner = go.AddComponent<ShopButtonAligner>();
        aligner.shipIndex = index;
    }

    // Buttons cloned from the scene came without the authored "Select" child,
    // so several had no text at all and showed neither name nor price.
    static void EnsureLabel(GameObject button)
    {
        var text = button.GetComponentInChildren<Text>(true);
        if (text == null)
        {
            var labelGo = new GameObject("Label", typeof(Text));
            labelGo.transform.SetParent(button.transform, false);
            text = labelGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        }

        // Best-fit blew the labels up to fill the whole plate; a fixed size
        // keeps every button reading the same.
        text.resizeTextForBestFit = false;
        text.fontSize = 22;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
    }


    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "shopS6") return;
        ShopSceneExtender.Build();
    }
}
