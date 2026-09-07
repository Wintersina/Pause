using UnityEngine;
using UnityEngine.UI;

// Pins a shop button beneath the ship it buys, and keeps its label current.
//
// The ships live in world space and the buttons in a screen-space canvas, which
// share no coordinate system -- the mapping depends on the actual screen size,
// so it cannot be baked into the scene. Converting every frame is cheap for
// seven buttons and survives rotation and resizes.
public class ShopButtonAligner : MonoBehaviour
{
    public int shipIndex;

    [Tooltip("The button is centred on the ship; its label is placed below it.")]
    public float dropBelowShip = 0f;

    RectTransform rect;
    RectTransform canvasRect;
    Text label;
    Transform ship;
    string lastText;

    void Awake()
    {
        rect = GetComponent<RectTransform>();
        label = GetComponentInChildren<Text>(true);
        var canvas = GetComponentInParent<Canvas>();
        if (canvas != null) canvasRect = canvas.transform as RectTransform;
    }

    void LateUpdate()
    {
        if (ship == null)
        {
            var go = SceneUtil.FindAny("ship" + shipIndex);
            if (go == null) return;
            ship = go.transform;
        }

        Follow();
        Relabel();
    }

    void Follow()
    {
        var cam = Camera.main;
        if (cam == null || canvasRect == null || rect == null) return;

        Vector3 screen = cam.WorldToScreenPoint(ship.position);

        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, screen, null, out local))
            return;

        rect.anchoredPosition = local - new Vector2(0f, dropBelowShip);
    }

    void Relabel()
    {
        if (label == null) return;

        string shipName = shopingShips.NameFor(shipIndex);
        if (string.IsNullOrEmpty(shipName)) return;

        bool owned = PlayerPrefs.GetString("boughtship" + shipIndex) == "True";
        bool active = PlayerPrefs.GetInt("spawnShip", 0) == shipIndex;

        string text = active ? shipName.ToUpperInvariant() + "  ✓"
                    : owned  ? shipName.ToUpperInvariant() + "  OWNED"
                             : shipName.ToUpperInvariant() + "  " +
                               Mathf.RoundToInt(shopingShips.CostFor(shipIndex)).ToString("N0");

        if (text == lastText) return;
        lastText = text;
        label.text = text;

        label.color = active ? new Color(0.55f, 1f, 0.62f)
                    : owned  ? new Color(0.85f, 0.9f, 1f)
                             : new Color(1f, 0.79f, 0.26f);
    }
}
