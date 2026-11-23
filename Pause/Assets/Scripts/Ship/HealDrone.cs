using System.Collections.Generic;
using UnityEngine;

// The healing mini-drone: a small green drone that slides out from under the damaged ship, hovers beside the
// broken part, sprays it with the repair retardant (the old water-spray animation, ShipDamageFx) and tucks back
// under the hull. It is CAST ONLY: spraying restores nothing by itself, the banked green atoms do (HealBank).
//
// It is not one of the gun drones (UltimateGun, the floaties): it never shoots, never blocks or moves them, and
// is not parented to the ship (so a spinner's turning hull and the hull's scale don't carry it).
//   * ShipDamageFx decides WHEN: damaged with 2+ hearts left, a spray burst about every HealGap seconds; a
//     banked atom spent on a hit calls it in at once, at any heart count; at 1 heart it stays tucked.
//   * This class is the body: Firefight orders in, the pose and nozzle out (the same contract UltimateGun
//     gives the retardant).
// Art: the ship's own companion drone drawing (UltimateGun.CompanionSprite) tinted green, with the damage-FX
// atlas's nozzle -- a placeholder until Codex draws a proper green mini-drone.
// Scaled time (Step(dt) with the world's dt): it holds still while the world is frozen. Built once per ship, no
// per-frame allocation.
[DefaultExecutionOrder(55)]   // after the hull pose (lifeControler) and the hearts (50), BEFORE ShipDamageFx (60): the spray starts at this frame's nozzle
public class HealDrone : MonoBehaviour
{
    public const string ObjectName = "~HealDrone";
    public const float BodyOfHull = .3f;        // the body's longest edge, of the hull's longest edge
    public const float NozzleSpan = .42f;       // the nozzle, of the body art's narrow edge
    public const float MaxTilt = 24f;
    const float Arrive = 3.2f, Leave = 2.2f, Chase = 9f;
    public static readonly Color Green = new Color(.6f, 1f, .66f, 1f);

    static readonly List<HealDrone> all = new List<HealDrone>();

    Transform ship;
    SpriteRenderer hull, body, nozzleRenderer;
    Transform bodyT, nozzle;
    int shipId;
    float nozzleSize;
    float deploy, clock, lean, nozzleAngle, nozzleOpen, nozzleClock;
    bool dutyWanted, dutySpraying, haveStation;
    Vector3 dutySpot, stationNow;

    public static HealDrone For(Transform ship)
    {
        for (int i = all.Count - 1; i >= 0; i--)
        {
            var d = all[i];
            if (d == null) { all.RemoveAt(i); continue; }
            if (d.ship == ship) return d;
        }
        return null;
    }

    // One per ship (the ship's collisionDetection attaches it at the start of a run).
    public static HealDrone Attach(GameObject shipGo)
    {
        var existing = For(shipGo.transform);
        if (existing != null) return existing;
        var go = new GameObject(ObjectName);
        go.transform.position = shipGo.transform.position;
        var d = go.AddComponent<HealDrone>();
        d.Init(shipGo.transform);
        return d;
    }

    void Init(Transform shipTransform)
    {
        ship = shipTransform;
        hull = ship.GetComponent<SpriteRenderer>();
        shipId = ShipId.Of(ship.gameObject, ShipId.Equipped());
        var bodyGo = new GameObject("Body", typeof(SpriteRenderer));
        bodyGo.transform.SetParent(transform, false);
        bodyT = bodyGo.transform;
        body = bodyGo.GetComponent<SpriteRenderer>();
        body.sprite = UltimateGun.CompanionSprite(shipId);
        body.color = Green;
        body.enabled = false;
        var nozzleGo = new GameObject("Nozzle", typeof(SpriteRenderer));
        nozzleGo.transform.SetParent(bodyT, false);
        nozzle = nozzleGo.transform;
        nozzleRenderer = nozzleGo.GetComponent<SpriteRenderer>();
        nozzleRenderer.sprite = ShipDamageFx.Frame(ShipDamageFx.RowNozzle, 0);
        nozzleRenderer.enabled = false;
        if (body.sprite != null)
        {
            var sb = body.sprite.bounds;
            nozzle.localPosition = new Vector3(sb.center.x, sb.center.y - sb.size.y * .08f, -.01f);
            nozzleSize = Mathf.Min(sb.size.x, sb.size.y) * NozzleSpan;
        }
        else nozzleSize = NozzleSpan;
        all.Add(this);
        Step(0f);
    }

    void OnDestroy() { all.Remove(this); }

    void LateUpdate()
    {
        if (ship == null) { Destroy(gameObject); return; }
        Step(Time.deltaTime);
    }

    // ---- orders (ShipDamageFx, every frame) ------------------------------

    // Go (or not) to the station beside the hot spot at `spotWorld`, spraying it or not.
    public void Firefight(bool onDuty, Vector3 spotWorld, bool spraying)
    {
        dutyWanted = onDuty;
        dutySpot = spotWorld;
        dutySpraying = onDuty && spraying;
    }

    // ---- what it is doing (tests) -----------------------------------------

    // 0 tucked under the hull .. 1 on its station.
    public float Deploy { get { return deploy; } }
    public bool OnStation { get { return deploy >= .9f; } }
    public bool Shown { get { return body != null && body.enabled; } }
    public bool NozzleShown { get { return nozzleRenderer != null && nozzleRenderer.enabled; } }
    public SpriteRenderer BodyRenderer { get { return body; } }
    public SpriteRenderer NozzleRenderer { get { return nozzleRenderer; } }
    public Transform Ship { get { return ship; } }
    public float Lean { get { return lean; } }
    // Where the retardant leaves the nozzle (world).
    public Vector3 NozzlePosition
    {
        get { return nozzle != null ? nozzle.TransformPoint(new Vector3(0f, .16f, 0f)) : transform.position; }
    }

    // ---- the body ----------------------------------------------------------

    static float Smooth01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    public void Step(float dt)
    {
        if (ship == null || body == null) return;
        bool alive = ship.gameObject.activeInHierarchy && !buttonClicks.playerDied;
        bool want = dutyWanted && alive;
        if (dt > 0f)
        {
            clock += dt;
            deploy = Mathf.MoveTowards(deploy, want ? 1f : 0f, dt * (want ? Arrive : Leave));
            nozzleOpen = Mathf.MoveTowards(nozzleOpen, want && deploy > .5f ? 1f : 0f, dt * 5f);
            nozzleClock = dutySpraying && want && nozzleOpen >= 1f ? nozzleClock + dt : 0f;
        }
        if (deploy <= 0f) haveStation = false;

        Bounds hb = ShipUiSlots.HullBounds(ship, shipId);
        float hullLong = Mathf.Max(hb.size.x, hb.size.y);
        float sizeNow = body.sprite != null ? Mathf.Max(body.sprite.bounds.size.x, body.sprite.bounds.size.y) : 1f;
        float k = hullLong * BodyOfHull / Mathf.Max(1e-4f, sizeNow);
        bodyT.localScale = new Vector3(k, k, 1f);
        float bodyWorld = hullLong * BodyOfHull;

        float z = ship.position.z;
        Vector3 tuck = hb.center + Vector3.down * (hb.extents.y * .3f);
        tuck.z = z;
        Vector3 spot = dutySpot;
        spot.z = z;
        if (want && dt > 0f)
        {
            if (!haveStation) { stationNow = tuck; haveStation = true; }
            stationNow = Vector3.Lerp(stationNow, StationFor(hb, spot, bodyWorld), 1f - Mathf.Exp(-Chase * dt));
        }
        float e = Smooth01(deploy);
        Vector3 at = haveStation ? Vector3.Lerp(tuck, stationNow, e) : tuck;
        at += new Vector3(Mathf.Sin(clock * 3.1f) * .1f, Mathf.Sin(clock * 4.3f) * .08f, 0f) * (bodyWorld * e);
        transform.position = at;

        // lean toward the spot, the nozzle facing it
        Vector3 to = spot - at;
        float aim = (to.x * to.x + to.y * to.y) > 1e-8f ? Mathf.Atan2(-to.x, to.y) * Mathf.Rad2Deg : 0f;
        float leanWant = want && haveStation ? Mathf.Clamp(aim, -MaxTilt, MaxTilt) * e : 0f;
        if (dt > 0f) lean = Mathf.MoveTowardsAngle(lean, leanWant, dt * 260f);
        bodyT.localRotation = Quaternion.Euler(0f, 0f, lean);

        bool shown = deploy > .02f && alive;
        body.enabled = shown;
        // under the hull while it slides out, over it once it is clear
        int order = (hull != null ? hull.sortingOrder : 0) + (deploy > .55f ? 1 : -1);
        body.sortingOrder = order;
        nozzleRenderer.sortingOrder = order + 1;
        bool showNozzle = shown && nozzleOpen > 0f;
        nozzleRenderer.enabled = showNozzle;
        if (showNozzle)
        {
            if (dt > 0f)
                nozzleAngle = Mathf.MoveTowardsAngle(nozzleAngle, Mathf.Clamp(Mathf.DeltaAngle(lean, aim), -150f, 150f), dt * 540f);
            nozzle.localRotation = Quaternion.Euler(0f, 0f, nozzleAngle);
            nozzle.localScale = Vector3.one * (nozzleSize * Smooth01(nozzleOpen));
            int frame = 0;
            if (nozzleClock > 0f) frame = nozzleClock < .08f ? 1 : 2 + Mathf.FloorToInt(nozzleClock * 12f) % 2;
            nozzleRenderer.sprite = ShipDamageFx.Frame(ShipDamageFx.RowNozzle, frame);
        }
    }

    // Beside the spot, on the side of the hull it is on, just off the hull, kept on screen.
    static Vector3 StationFor(Bounds hull, Vector3 spot, float bodyWorld)
    {
        float side = spot.x >= hull.center.x ? 1f : -1f;
        float standoff = bodyWorld * .9f;
        Vector3 want = spot + new Vector3(side * standoff, -standoff * .35f, 0f);
        Rect screen = ShipUiSlots.ScreenRect(null);
        float half = bodyWorld * .5f;
        want.x = Mathf.Clamp(want.x, screen.xMin + half, Mathf.Max(screen.xMin + half, screen.xMax - half));
        want.y = Mathf.Clamp(want.y, screen.yMin + half, Mathf.Max(screen.yMin + half, screen.yMax - half));
        return want;
    }
}
