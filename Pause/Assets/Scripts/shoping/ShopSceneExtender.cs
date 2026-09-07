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
        if (sr.sprite == null)
        {
            var sprite = LoadShipSprite(index);
            if (sprite != null) sr.sprite = sprite;
        }
        sr.sortingOrder = 5;

        EnsureBoost(go);
    }

    // First damage frame of the ship's sheet, matching what lifeControler loads.
    static Sprite LoadShipSprite(int index)
    {
        string name = shopingShips.NameFor(index);
        if (string.IsNullOrEmpty(name)) return null;

        var frames = Resources.LoadAll<Sprite>("prefabs/Ships/Sprites/" + name);
        return (frames != null && frames.Length > 0) ? frames[0] : null;
    }

    static void EnsureBoost(GameObject ship)
    {
        if (ship.transform.Find("Boost") != null) return;
        foreach (Transform c in ship.transform)
            if (c.name.StartsWith("Boost")) return;

        var boost = new GameObject("Boost");
        boost.transform.SetParent(ship.transform, false);
        boost.transform.localPosition = new Vector3(0f, -0.6f, 0f);
        boost.AddComponent<SpriteRenderer>().sortingOrder = 4;
        boost.SetActive(false);
    }

    static void EnsureButton(int index, GameObject canvas, GameObject template)
    {
        if (SceneUtil.FindAny("Button" + index) != null) return;
        if (canvas == null || template == null) return;

        var clone = Instantiate(template, template.transform.parent);
        clone.name = "Button" + index;

        // Continue the authored button grid: two columns, 100px rows.
        var rt = clone.GetComponent<RectTransform>();
        var src = template.GetComponent<RectTransform>();
        if (rt != null && src != null)
        {
            int col = (index - 1) % 2, row = (index - 1) / 2;
            rt.anchoredPosition = new Vector2(col == 0 ? -200f : 200f, 380f - row * 100f);
        }

        // shipselected() keys off the button's name, so the cloned handler is
        // already correct -- nothing else to wire.
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
