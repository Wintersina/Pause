using UnityEngine;

// Cycles the dock hull through its three small bob frames. The existing
// ShipThruster continues to animate the rear flame independently.
public class DockShipIdleAnimator : MonoBehaviour
{
    public int shipIndex;
    SpriteRenderer spriteRenderer;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    void Update()
    {
        if (spriteRenderer == null) return;
        int frame = Mathf.FloorToInt(Time.unscaledTime * 8f) % 3;
        Sprite sprite = shopingShips.IdleSpriteFor(shipIndex, 0, frame);
        if (sprite != null) spriteRenderer.sprite = sprite;
    }
}
