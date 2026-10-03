using UnityEngine;

// The ultimate's charge indicator, sitting just in front of the hull.
//
// Replaces the old charge bar (PowerReadyIndicator). Each ship gets its own
// animated piece of its weapon -- missiles sliding out of a rack, an eye
// waking up, shuriken blades snapping out, drones flying into formation...
// (WeaponStyleTable, art rendered by Art/Weapons/src~/weapons.py). The
// charge sequence is sixteen key drawings of one parametric picture, so the
// progress reads at a glance.
//
// Timing:
//  * builds steadily with the cooldown, on gameplay time: no progress and no
//    idle motion while the world is frozen;
//  * a pickup that shortens the cooldown makes the drawing catch up quickly
//    (a fast ease through the in-between drawings) instead of snapping;
//  * the last second is the ready tell: a held key pose and a drawn pulse on
//    2s, and three rising ticks; as the gun slides into its firing slot the
//    indicator rides onto its muzzle; the squash drawing holds as
//    anticipation just before it fires;
//  * Fire() calls Release(): stretch / flash / speed-lines drawings, after
//    which it starts again from empty.
// Squash and stretch are drawn into the frames (docs/art-style.md), never
// tweened here.
public class ChargeIndicator : MonoBehaviour
{
    public const float ReadySeconds = 1f;
    public const float AnticipationSeconds = .14f;
    public static float ReleaseSeconds => WeaponArt.Seconds(WeaponArt.ReleaseTicks) - WeaponArt.ReleaseTicks[0] * WeaponArt.Tick;
    public const float WorldSize = .42f;
    const float CatchUp = 9f;     // per second, exponential catch-up after a pickup
    const float MinRate = .02f;   // charge per second, so a tiny gap still closes
    static readonly float[] TickAt = { 1f, .66f, .33f };

    ShipPowerController controller;
    SpriteRenderer hull;
    UltimateGun gun;
    SpriteRenderer view;
    WeaponStyle style;
    int ship;

    float shown, ambient, spin, releaseT;
    bool releasing;
    int nextTick;

    public float Shown => shown;
    public bool Ready { get; private set; }
    public bool Releasing => releasing;
    public int ReleaseCount { get; private set; }
    public int ReadyTellCount { get; private set; }
    public int TicksPlayed { get; private set; }
    public int ChargeFrame { get; private set; }
    public Sprite CurrentSprite => view != null ? view.sprite : null;
    public Transform View => view != null ? view.transform : null;

    public static ChargeIndicator Attach(ShipPowerController owner)
    {
        var indicator = owner.GetComponent<ChargeIndicator>();
        if (indicator == null) indicator = owner.gameObject.AddComponent<ChargeIndicator>();
        indicator.Build(owner);
        return indicator;
    }

    // Charge fraction -> which of the sixteen drawings. Monotonic; the full
    // drawing only once the charge is in its last sixteenth.
    public static int ChargeFrameFor(float progress)
    {
        int n = WeaponArt.ChargeFrames;
        return Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(progress) * n), 0, n - 1);
    }

    void Build(ShipPowerController owner)
    {
        controller = owner;
        if (view != null) return;
        hull = GetComponent<SpriteRenderer>();
        ship = owner.ShipIndex;
        style = WeaponStyleTable.For(ship);
        var go = new GameObject("~ChargeIndicator", typeof(SpriteRenderer));
        go.transform.SetParent(transform, false);
        view = go.GetComponent<SpriteRenderer>();
        view.sortingOrder = (hull != null ? hull.sortingOrder : 0) + 4;
        view.sprite = WeaponArt.Charge(ship, 0);
        Place();
    }

    void LateUpdate()
    {
        // gameplay time: nothing moves while the world is frozen
        Step(Time.timeScale > 0f ? Time.deltaTime : 0f, Time.unscaledDeltaTime);
    }

    public void Step(float dt, float unscaledDt)
    {
        if (controller == null || view == null) return;
        if (gun == null) gun = GetComponentInChildren<UltimateGun>();
        float target = controller.Charge01;
        float left = controller.SecondsLeft;

        if (releasing)
        {
            releaseT += unscaledDt;
            if (releaseT >= ReleaseSeconds) releasing = false;
        }

        if (target < shown) shown = target; // a fresh cycle
        if (dt > 0f)
        {
            ambient += dt;
            float gap = target - shown;
            if (gap > 0f)
                shown = Mathf.Min(target, shown + gap * (1f - Mathf.Exp(-CatchUp * dt)) + MinRate * dt);
            spin += style.indicatorSpin * shown * shown * dt;
        }

        bool ready = !releasing && left <= ReadySeconds;
        if (ready && !Ready) { ReadyTellCount++; nextTick = 0; }
        Ready = ready;
        if (ready)
        {
            int crossed = -1;
            while (nextTick < TickAt.Length && left <= TickAt[nextTick]) { crossed = nextTick; nextTick++; }
            if (crossed >= 0)
            {
                TicksPlayed++;
                UltimateShotSound.Tick(ship, crossed);
            }
        }

        if (releasing)
        {
            // the release drawings after the squash (which already played
            // as the anticipation)
            int f = WeaponArt.FrameAt(WeaponArt.ReleaseTicks, releaseT + WeaponArt.ReleaseTicks[0] * WeaponArt.Tick, false);
            view.sprite = WeaponArt.Release(ship, Mathf.Clamp(f, 1, WeaponArt.ReleaseFrames - 1));
        }
        else if (ready)
        {
            view.sprite = left <= AnticipationSeconds
                ? WeaponArt.Release(ship, 0) // anticipation: the drawn squash pose
                : WeaponArt.Ready(ship, WeaponArt.FrameAt(WeaponArt.ReadyTicks, ambient, true));
        }
        else
        {
            ChargeFrame = ChargeFrameFor(shown);
            view.sprite = WeaponArt.Charge(ship, ChargeFrame);
        }
        Place();
    }

    void Place()
    {
        var t = view.transform;
        float hullTop = hull != null && hull.sprite != null
            ? hull.sprite.bounds.extents.y * Mathf.Abs(transform.lossyScale.y)
            : .3f;
        Vector3 rest = transform.position + Vector3.up * (hullTop + WorldSize * .5f);
        Vector3 pos = rest;
        if (gun != null)
            pos = Vector3.Lerp(rest, gun.MuzzlePosition + Vector3.up * WorldSize * .3f, gun.Extend01);
        pos.z = transform.position.z - .06f;
        t.position = pos;
        t.rotation = Quaternion.Euler(0f, 0f, -spin);
        Vector3 lossy = transform.lossyScale;
        t.localScale = new Vector3(WorldSize / Mathf.Max(.0001f, Mathf.Abs(lossy.x)),
                                   WorldSize / Mathf.Max(.0001f, Mathf.Abs(lossy.y)), 1f);
    }

    // The ultimate just went off.
    public void Release()
    {
        releasing = true;
        releaseT = 0f;
        ReleaseCount++;
        shown = 0f;
        Ready = false;
        nextTick = 0;
        if (view != null) view.sprite = WeaponArt.Release(ship, 1);
    }
}
