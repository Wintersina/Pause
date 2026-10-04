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

    // ---- placement (ShipUiSlots) ----------------------------------------

    const float Gap = .04f, NearReach = .2f, MaxReach = .9f;
    // The ready pose swells the drawing a little (SecretMeter.png, 1.06x).
    const float Envelope = WorldSize * 1.1f;

    ShipUiSlots.Slot[] slots;
    int slotIndex = -1, layoutVersion = -1;
    float relayoutIn;
    Vector2 offset = new Vector2(.45f, -.1f);
    bool registered;

    // Everywhere the badge can draw (every pose) with the ship where it is
    // now: what the hearts and other ship UI keep clear of.
    public Bounds Footprint
    {
        get
        {
            Vector3 p = transform.parent != null ? transform.parent.position : transform.position;
            return new Bounds(new Vector3(p.x + offset.x, p.y + offset.y, p.z), new Vector3(Envelope, Envelope, 0f));
        }
    }

    public Rect WorldBounds
    {
        get { var b = Footprint; return new Rect(b.min.x, b.min.y, b.size.x, b.size.y); }
    }

    public ShipUiSlots.Side Side => slots != null && slotIndex >= 0 ? slots[slotIndex].side
                                  : (side > 0f ? ShipUiSlots.Side.Right : ShipUiSlots.Side.Left);

    // The one placement function. A slot beside the hull, on the side away
    // from the gun first (the gun's own footprint pushes it clear anyway),
    // then the other side, then whatever ShipUiSlots.Choose finds. The
    // charge indicator, the gun and the exhaust are avoided through the
    // registry; the hearts avoid this badge's registered footprint.
    void Place()
    {
        var host = transform.parent;
        if (host == null || view == null) return;
        if (!registered)
        {
            ShipUiSlots.Register(host, this, () => Footprint, () => ShipUiSlots.DrawnBounds(View));
            registered = true;
        }
        relayoutIn -= Time.unscaledDeltaTime;
        if (slots == null || layoutVersion != ShipUiSlots.Version || relayoutIn <= 0f)
        {
            var size = new Vector2(Envelope, Envelope);
            slots = ShipUiSlots.Candidates(host, ship,
                new ShipUiSlots.Request { rowSize = size, columnSize = size, gap = Gap, nearReach = NearReach, maxReach = MaxReach },
                this);
            layoutVersion = ShipUiSlots.Version;
            relayoutIn = 1f;
        }
        Vector3 p = host.position;
        Rect screen = ShipUiSlots.ScreenRect(null);
        var first = side > 0f ? ShipUiSlots.Side.Right : ShipUiSlots.Side.Left;
        var second = side > 0f ? ShipUiSlots.Side.Left : ShipUiSlots.Side.Right;
        int pick = Usable(first, p, screen);
        if (pick < 0) pick = Usable(second, p, screen);
        if (pick < 0) pick = ShipUiSlots.Choose(slots, p, screen, NearReach, slotIndex);
        if (pick >= 0)
        {
            // a new slot: the hearts (which keep clear of this badge) re-lay out now
            if (pick != slotIndex && slotIndex >= 0) ShipUiSlots.Moved();
            slotIndex = pick;
            offset = slots[pick].offset;
            // sit a touch below the hull's centre line when beside it
            if (slots[pick].side == ShipUiSlots.Side.Left || slots[pick].side == ShipUiSlots.Side.Right)
                offset.y -= WorldSize * .25f;
        }

        var t = view.transform;
        t.position = new Vector3(p.x + offset.x, p.y + offset.y, p.z - .05f);
        t.rotation = Quaternion.identity;
        Vector3 lossy = host.lossyScale;
        t.localScale = new Vector3(WorldSize / Mathf.Max(.0001f, Mathf.Abs(lossy.x)),
                                   WorldSize / Mathf.Max(.0001f, Mathf.Abs(lossy.y)), 1f);
    }

    int Usable(ShipUiSlots.Side want, Vector3 p, Rect screen)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            var sl = slots[i];
            if (sl.side != want || !sl.clear) continue;
            var keep = i == slotIndex ? 0f : .12f;
            var r = Rect.MinMaxRect(screen.xMin + keep, screen.yMin + keep, screen.xMax - keep, screen.yMax - keep);
            if (ShipUiSlots.Inside(r, sl.At(p))) return i;
        }
        return -1;
    }

    void OnDestroy()
    {
        ShipUiSlots.Unregister(this);
    }
}
