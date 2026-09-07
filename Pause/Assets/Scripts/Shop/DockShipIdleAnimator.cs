using UnityEngine;

// Cycles the dock hull through its three small bob frames. The existing
// ShipThruster continues to animate the rear flame independently.
public class DockShipIdleAnimator : MonoBehaviour
{
    public int shipIndex;
    SpriteRenderer spriteRenderer;
    Vector3 restingScale;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        restingScale = transform.localScale;
    }

    void Update()
    {
        if (spriteRenderer == null) return;
        if (restingScale == Vector3.zero) restingScale = transform.localScale;
        bool selected = rotateRight.shipSelected == shipIndex && !rotateRight.flyOffChecker;
        int frame = Mathf.FloorToInt(Time.unscaledTime * (selected ? 14f : 8f)) % 3;
        Sprite sprite = shopingShips.IdleSpriteFor(shipIndex, 0, frame);
        if (sprite != null) spriteRenderer.sprite = sprite;

        // A selected ship visibly wakes up in its berth: quicker idle frames,
        // a small hover pulse, and a stronger persistent engine flame.
        float pulse = selected ? 1f + Mathf.Sin(Time.unscaledTime * 9f) * .055f : 1f;
        transform.localScale = restingScale * pulse;
        var thruster = GetComponent<ShipThruster>();
        if (thruster != null) thruster.idleScale = selected ? .46f : .26f;
    }
}
