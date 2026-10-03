using UnityEngine;

// The secret power's meter: a small hex badge riding beside the hull, on the
// side away from the ultimate's gun, showing the power's icon.
//
// Distinct from the attack's ChargeIndicator in front of the nose: smaller,
// beside rather than ahead, and drawn as a badge (flat cel hex, ink outline,
// the icon lit segment by segment as the meter fills -- SecretMeter.png).
// Ready tell: the full badge swells on its held key pose and pulses on 2s
// (drawn, not tweened) while it waits for its trigger. Firing shows the
// one-tick flash drawing, then it starts again from empty.
//
// Gameplay time: holds still while the world is paused.
public class SecretMeter : MonoBehaviour
{
    public const float WorldSize = .3f;
    const float FlashSeconds = 3f / 24f;

    SecretPowerController controller;
    SpriteRenderer hull, view;
    int ship;
    float side = 1f;
    float clock, flashLeft;

    public bool ReadyShowing { get; private set; }
    public int ReadyTellCount { get; private set; }
    public int FillFrame { get; private set; }
    public Sprite CurrentSprite => view != null ? view.sprite : null;
    public Transform View => view != null ? view.transform : null;

    public static SecretMeter Attach(SecretPowerController owner)
    {
        var existing = owner.GetComponentInChildren<SecretMeter>();
        if (existing != null) { existing.Build(owner); return existing; }
        var go = new GameObject("~SecretMeter", typeof(SpriteRenderer));
        go.transform.SetParent(owner.transform, false);
        var meter = go.AddComponent<SecretMeter>();
        meter.Build(owner);
        return meter;
    }

    void Build(SecretPowerController owner)
    {
        controller = owner;
        ship = owner.Ship;
        hull = owner.GetComponent<SpriteRenderer>();
        view = GetComponent<SpriteRenderer>();
        view.sortingOrder = (hull != null ? hull.sortingOrder : 0) + 5;
        // the gun rests on the left for even ids (UltimateGun), so the badge
        // takes the other side
        side = ship % 2 == 0 ? 1f : -1f;
        view.sprite = ShipFxArt.MeterFill(ship, 0);
        Place();
    }

    public void Flash()
    {
        flashLeft = FlashSeconds;
        if (view != null) view.sprite = ShipFxArt.MeterFlash(ship);
    }

    void LateUpdate()
    {
        Step(ShipAttackRunner.ScaledDelta());
    }

    public void Step(float dt)
    {
        if (controller == null || view == null) return;
        Place();
        if (dt <= 0f) return;
        clock += dt;
        if (flashLeft > 0f)
        {
            flashLeft -= dt;
            view.sprite = ShipFxArt.MeterFlash(ship);
            ReadyShowing = false;
            return;
        }
        bool ready = controller.Ready;
        if (ready && !ReadyShowing) { ReadyTellCount++; clock = 0f; }
        ReadyShowing = ready;
        if (ready)
            view.sprite = ShipFxArt.MeterReady(ship, WeaponArt.FrameAt(ShipFxArt.MeterReadyTicks, clock, true));
        else
        {
            FillFrame = ShipFxArt.MeterFrameFor(controller.Meter01);
            view.sprite = ShipFxArt.MeterFill(ship, FillFrame);
        }
    }

    // World rect the badge occupies this frame -- for the ship's UI-slot
    // registry (hearts / charge indicator) once it lands.
    public Rect WorldBounds
    {
        get
        {
            if (view == null) return new Rect();
            Vector3 p = view.transform.position;
            return new Rect(p.x - WorldSize * .5f, p.y - WorldSize * .5f, WorldSize, WorldSize);
        }
    }

    // The one placement function: beside the hull on the side away from the
    // gun, a little below centre -- clear of the charge indicator and the
    // life hearts, which both sit above the nose. Hook a slot registry here.
    void Place()
    {
        float x = .3f, y = -.12f;
        if (hull != null && hull.sprite != null)
        {
            Bounds b = hull.sprite.bounds;
            Vector3 s = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
            x = b.extents.x * Mathf.Abs(s.x) + WorldSize * .45f;
            y = -b.extents.y * Mathf.Abs(s.y) * .35f;
        }
        var t = view.transform;
        Vector3 parent = transform.parent != null ? transform.parent.position : transform.position;
        t.position = new Vector3(parent.x + side * x, parent.y + y, parent.z - .05f);
        t.rotation = Quaternion.identity;
        Vector3 lossy = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        t.localScale = new Vector3(WorldSize / Mathf.Max(.0001f, Mathf.Abs(lossy.x)),
                                   WorldSize / Mathf.Max(.0001f, Mathf.Abs(lossy.y)), 1f);
    }
}
