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
    const float ColumnX = 0.5f;
    const float FaceX = 1.5f;
    const float TopY = 3.0f;
    const float RowStep = 1.0f;

    // Authored previews are hand-scaled so every hull ends up roughly this tall
    // in world units, regardless of how large its source texture is -- Darkwing
    // is 15401px wide and sits at scale 0.03, Proteus is 400px at scale 0.60.
    // Generated ships are normalised to the same visual size instead of being
    // left at scale 1, which is why they towered over the rest.
    const float TargetHullHeight = 1.05f;

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

        // Only fill in art we are missing, so authored previews stay untouched.
        bool weSuppliedTheArt = false;
        if (sr.sprite == null)
        {
            var sprite = LoadShipSprite(index);
            if (sprite != null) { sr.sprite = sprite; weSuppliedTheArt = true; }
        }
        sr.sortingOrder = 5;

        if (weSuppliedTheArt) NormaliseScale(go, sr);

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

    // The canvas is 800x600 anchored at its centre, so anything beyond y = +300
    // hangs off the top. The authored rows sat at 380 and 280, which put the
    // first four buttons partly or wholly out of reach on a 4:3 view -- they
    // simply could not be tapped. Every button is now placed inside the safe
    // area, and the whole grid is laid out from one formula so it stays
    // consistent as the roster grows.
    const float ButtonTopY = 250f;
    const float ButtonRowStep = 100f;
    const float ButtonColumnX = 200f;

    static Vector2 ButtonSlot(int index)
    {
        int col = (index - 1) % 2, row = (index - 1) / 2;
        return new Vector2(col == 0 ? -ButtonColumnX : ButtonColumnX,
                           ButtonTopY - row * ButtonRowStep);
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
        if (rt != null) rt.anchoredPosition = ButtonSlot(index);

        Label(go, index);
    }

    // Each button states which ship it is and what it costs. Previously they
    // were unlabelled, so the price only appeared after opening the panel.
    static void Label(GameObject button, int index)
    {
        var texts = button.GetComponentsInChildren<Text>(true);
        if (texts == null || texts.Length == 0) return;

        string shipName = shopingShips.NameFor(index);
        float cost = shopingShips.CostFor(index);
        bool owned = PlayerPrefs.GetString("boughtship" + index) == "True";

        string price = owned ? "OWNED" : Mathf.RoundToInt(cost).ToString("N0");
        texts[0].text = string.IsNullOrEmpty(shipName)
            ? price
            : shipName.ToUpperInvariant() + "   " + price;
        texts[0].resizeTextForBestFit = true;
    }
}

public static class ShopSceneBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "shopS6") return;
        ShopSceneExtender.Build();
    }
}
