using System;
using System.Collections.Generic;
using UnityEngine;

// Who owns which bit of space around the player's ship.
//
// Several things hang off the hull: the ultimate's charge indicator in
// front of the nose (and its ride onto the gun muzzle when it's ready), the
// companion gun beside the hull, the exhaust plume or spin wake behind it,
// the life hearts, and more to come (the secret-power meter). Each element
// that sits near the ship registers a footprint here: a function returning
// its world-space keep-out box, the union of every pose it can take (idle,
// charging, ready, firing, hover), not just this frame's. Anything that
// needs a free spot (the hearts) asks Place() for the first clear slot in
// Order -- above, below, left, right -- that stays on screen.
//
// Registering (one line, from the element's Awake/Start/Build):
//
//   ShipUiSlots.Register(shipTransform, this, () => myWorldKeepOutBounds);
//
// The owner is a UnityEngine.Object: once it is destroyed its entry drops
// out on its own (Unregister is there for elements that hide for good).
// Footprints are cheap to compute and are re-read whenever a client
// refreshes its layout; Version changes on every (un)registration.
//
// Everything is derived from sprite bounds and the owners' own geometry,
// never per-ship tables, so a redrawn hull or a new indicator stays clear.
public static class ShipUiSlots
{
    public enum Side { Above, Below, Left, Right }

    // The order free slots are tried in.
    public static readonly Side[] Order = { Side.Above, Side.Below, Side.Left, Side.Right };

    struct Entry
    {
        public Transform ship;
        public UnityEngine.Object owner;
        public Func<Bounds> footprint;
    }

    static readonly List<Entry> entries = new List<Entry>();

    public static int Version { get; private set; }

    // Tests set this to stand in for the camera's safe area.
    public static Func<Rect> ScreenOverride;

    public static void Register(Transform ship, UnityEngine.Object owner, Func<Bounds> footprint)
    {
        if (ship == null || owner == null || footprint == null) return;
        for (int i = entries.Count - 1; i >= 0; i--)
            if (entries[i].owner == owner) entries.RemoveAt(i);
        entries.Add(new Entry { ship = ship, owner = owner, footprint = footprint });
        Version++;
    }

    public static void Unregister(UnityEngine.Object owner)
    {
        for (int i = entries.Count - 1; i >= 0; i--)
            if (entries[i].owner == owner || entries[i].owner == null) entries.RemoveAt(i);
        Version++;
    }

    public static bool IsRegistered(UnityEngine.Object owner)
    {
        foreach (var e in entries) if (e.owner == owner && owner != null) return true;
        return false;
    }

    // Every live registered footprint on `ship` (world space), except `except`'s.
    public static void Occupied(Transform ship, List<Bounds> into, UnityEngine.Object except = null)
    {
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var e = entries[i];
            if (e.owner == null || e.ship == null) { entries.RemoveAt(i); continue; }
            if (e.ship != ship || e.owner == except) continue;
            Bounds b;
            try { b = e.footprint(); }
            catch (Exception) { continue; }
            if (b.size.sqrMagnitude > 0f) into.Add(b);
        }
    }

    // ---- the ship's own body ---------------------------------------------

    // Ninja and UFO spin their whole hull, so anything parented to it orbits.
    public static bool Spins(int shipId) { return ShipExhaust.UsesSpinDrift(shipId); }

    // A ship-local box in world space. A spinning ship sweeps it round its
    // centre, so it becomes the square around the circle its far corner
    // traces.
    public static Bounds ToWorld(Transform ship, Bounds local, bool spins)
    {
        if (spins)
        {
            float r = 0f;
            Vector3 min = local.min, max = local.max;
            for (int i = 0; i < 4; i++)
            {
                var c = new Vector2((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y);
                r = Mathf.Max(r, c.magnitude);
            }
            Vector3 lossy = ship.lossyScale;
            r *= Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y));
            return new Bounds(ship.position, new Vector3(r * 2f, r * 2f, 0f));
        }
        var b = new Bounds(ship.TransformPoint(local.min), Vector3.zero);
        b.Encapsulate(ship.TransformPoint(local.max));
        b.Encapsulate(ship.TransformPoint(new Vector3(local.min.x, local.max.y, local.min.z)));
        b.Encapsulate(ship.TransformPoint(new Vector3(local.max.x, local.min.y, local.min.z)));
        return b;
    }

    // The companion gun (UltimateGun) stays upright beside a spinning hull,
    // so its hull-local box maps to the world without the hull's rotation.
    public static Bounds GunToWorld(Transform ship, int shipId, Bounds local)
    {
        if (!Spins(shipId)) return ToWorld(ship, local, false);
        Vector3 s = ship.lossyScale;
        var abs = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), 1f);
        return new Bounds(ship.position + Vector3.Scale(local.center, s), Vector3.Scale(local.size, abs));
    }

    // The hull itself: its sprite rect (every drawing of a ship shares it).
    public static Bounds HullBounds(Transform ship, int shipId)
    {
        var hull = ship.GetComponent<SpriteRenderer>();
        Bounds local = hull != null && hull.sprite != null
            ? hull.sprite.bounds
            : new Bounds(Vector3.zero, new Vector3(.5f, .6f, 0f));
        local.center = new Vector3(local.center.x, local.center.y, 0f);
        return ToWorld(ship, local, Spins(shipId));
    }

    // Behind the hull: the boost-length nozzle plumes, or a spinner's ring
    // and its world-aligned wake (ShipExhaust / ShipSpinDrift geometry).
    public static bool ExhaustBounds(Transform ship, int shipId, out Bounds world)
    {
        world = default(Bounds);
        var hull = ship.GetComponent<SpriteRenderer>();
        Sprite hullSprite = hull != null ? hull.sprite : null;
        if (hullSprite == null) return false;
        Vector3 lossy = ship.lossyScale;
        float s = Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y));
        bool any = false;

        if (ShipExhaust.UsesSpinDrift(shipId))
        {
            float extent = Mathf.Max(hullSprite.bounds.size.x, hullSprite.bounds.size.y);
            float ring = extent * ShipSpinDrift.RingSize * .5f * s;
            world = new Bounds(ship.position, new Vector3(ring * 2f, ring * 2f, 0f));
            any = true;
            var wake = ShipExhaustStyle.Frame(shipId, ExhaustLayer.WakeBoost, 0) ?? ShipExhaustStyle.Frame(shipId, ExhaustLayer.Wake, 0);
            if (wake != null && wake.bounds.size.y > 0f)
            {
                float k = extent * ShipSpinDrift.WakeLength(shipId) / wake.bounds.size.y * s;
                Vector3 at = ship.position + Vector3.down * (extent * ShipSpinDrift.WakeOffset * s);
                var w = new Bounds(at + wake.bounds.center * k, wake.bounds.size * k);
                world.Encapsulate(w);
            }
            return true;
        }

        var mounts = ShipExhaust.MountsFor(hullSprite, shipId);
        Vector3 scale = ShipExhaust.ScaleFor(hullSprite, shipId);
        var plume = ShipExhaust.Frame(shipId, true, 0) ?? ShipExhaust.SpriteFor(shipId);
        if (plume == null) return false;
        for (int i = 0; i < mounts.Length; i++)
        {
            float k = ShipExhaust.NozzleScale(shipId, i);
            var ls = new Vector3(scale.x * k, scale.y * Mathf.Sqrt(k) * ShipExhaustStyle.BoostLength, 1f);
            var local = new Bounds(mounts[i] + Vector3.Scale(plume.bounds.center, ls), Vector3.Scale(plume.bounds.size, ls));
            local.center = new Vector3(local.center.x, local.center.y, 0f);
            var w = ToWorld(ship, local, false);
            if (!any) { world = w; any = true; }
            else world.Encapsulate(w);
        }
        return any;
    }

    // The charge indicator's keep-out box: its resting pose in front of the
    // nose (ChargeIndicator.Place), everywhere it can ride to on the way onto
    // the gun's muzzle, and the extra reach of a spinning drawing.
    public static Bounds ChargeIndicatorFootprint(Transform ship, int shipId)
    {
        float w = ChargeIndicator.WorldSize;
        float half = w * .5f * (WeaponStyleTable.For(shipId).indicatorSpin != 0f ? 1.4143f : 1f);
        var hull = ship.GetComponent<SpriteRenderer>();
        float nose = .3f;
        if (hull != null && hull.sprite != null)
        {
            Bounds b = hull.sprite.bounds;
            float sy = Mathf.Abs(ship.lossyScale.y);
            nose = ShipExhaust.UsesWind(shipId)
                ? Mathf.Max(b.extents.x, b.extents.y) * Mathf.Max(Mathf.Abs(ship.lossyScale.x), sy)
                : b.max.y * sy;
        }
        Vector3 rest = ship.position + Vector3.up * (nose + w * .5f);
        var box = new Bounds(rest, new Vector3(half * 2f, half * 2f, 0f));
        var gun = ship.GetComponentInChildren<UltimateGun>(true);
        if (gun != null)
        {
            Bounds muzzle = GunToWorld(ship, shipId, gun.LocalMuzzleEnvelope());
            muzzle.center += Vector3.up * (w * .3f);
            muzzle.Expand(new Vector3(half * 2f, half * 2f, 0f));
            box.Encapsulate(muzzle);
        }
        box.center = new Vector3(box.center.x, box.center.y, ship.position.z);
        return box;
    }

    // ---- placement -------------------------------------------------------

    public struct Request
    {
        public Vector2 rowSize;     // the element laid out across (Above / Below)
        public Vector2 columnSize;  // the element stacked up (Left / Right)
        public float gap;           // clearance kept to everything
        public float nearReach;     // a slot within this of the hull beats any further one
        public float maxReach;      // furthest its near edge may sit from the hull
    }

    public struct Slot
    {
        public Side side;
        public Vector2 offset;      // centre, relative to the ship's position
        public Vector2 size;
        public bool clear;          // free of everything within reach
        public float reach;
        public Bounds At(Vector3 shipPosition)
        {
            return new Bounds(new Vector3(shipPosition.x + offset.x, shipPosition.y + offset.y, shipPosition.z),
                              new Vector3(size.x, size.y, 0f));
        }
    }

    static readonly List<Bounds> scratch = new List<Bounds>();

    // Every candidate slot for `request` around `ship`, in Order, each pushed
    // out along its side past whatever it touches (hull, exhaust, every
    // registered footprint but `except`'s) -- and marked unclear if that
    // takes it further than maxReach from the hull. Relative to the ship's
    // position, so a client can keep them while the ship moves.
    public static Slot[] Candidates(Transform ship, int shipId, Request request, UnityEngine.Object except = null,
                                    List<Bounds> extraObstacles = null)
    {
        scratch.Clear();
        Bounds hull = HullBounds(ship, shipId);
        scratch.Add(hull);
        Bounds exhaust;
        if (ExhaustBounds(ship, shipId, out exhaust)) scratch.Add(exhaust);
        Occupied(ship, scratch, except);
        if (extraObstacles != null) scratch.AddRange(extraObstacles);

        Vector3 p = ship.position;
        var slots = new Slot[Order.Length];
        for (int i = 0; i < Order.Length; i++)
        {
            Side side = Order[i];
            bool across = side == Side.Above || side == Side.Below;
            Vector2 size = across ? request.rowSize : request.columnSize;
            Vector2 dir = side == Side.Above ? Vector2.up : side == Side.Below ? Vector2.down
                        : side == Side.Left ? Vector2.left : Vector2.right;
            // Start snug against the hull, centred on it.
            Vector2 c = new Vector2(hull.center.x, hull.center.y);
            float hullEdge = Vector2.Dot(dir, c) + Mathf.Abs(Vector2.Dot(dir, (Vector2)hull.extents));
            float halfAlong = Mathf.Abs(Vector2.Dot(dir, size * .5f));
            float along = hullEdge + request.gap + halfAlong;
            c += dir * (along - Vector2.Dot(dir, c));

            bool clear = false;
            for (int pass = 0; pass < 16; pass++)
            {
                var box = new Bounds(new Vector3(c.x, c.y, p.z), new Vector3(size.x + request.gap * 2f, size.y + request.gap * 2f, 0f));
                float push = float.NegativeInfinity;
                foreach (var o in scratch)
                {
                    var flat = new Bounds(new Vector3(o.center.x, o.center.y, p.z), new Vector3(o.size.x, o.size.y, 0f));
                    if (!Overlaps(flat, box)) continue;
                    float far = Vector2.Dot(dir, (Vector2)o.center) + Mathf.Abs(Vector2.Dot(dir, (Vector2)o.extents));
                    push = Mathf.Max(push, far);
                }
                if (float.IsNegativeInfinity(push)) { clear = true; break; }
                float target = push + request.gap + halfAlong + .001f;
                float now = Vector2.Dot(dir, c);
                if (target <= now) target = now + .01f;
                c += dir * (target - now);
            }
            float reach = Vector2.Dot(dir, c) - halfAlong - hullEdge;
            slots[i] = new Slot
            {
                side = side,
                offset = c - new Vector2(p.x, p.y),
                size = size,
                reach = reach,
                clear = clear && reach <= request.maxReach + 1e-4f,
            };
        }
        return slots;
    }

    // Strict overlap on x/y (touching edges don't count).
    public static bool Overlaps(Bounds a, Bounds b)
    {
        return a.min.x < b.max.x && b.min.x < a.max.x && a.min.y < b.max.y && b.min.y < a.max.y;
    }

    public static bool Inside(Rect screen, Bounds b)
    {
        return b.min.x >= screen.xMin && b.max.x <= screen.xMax && b.min.y >= screen.yMin && b.max.y <= screen.yMax;
    }

    // The slot to use with the ship at `shipPosition`: the first clear slot
    // in Order that sits snug (within nearReach of the hull) and fits on
    // screen; failing that, the nearest clear one that fits (Order breaks
    // near-ties). `keep` (the slot already in use) only has to fit the screen
    // itself; switching to any other needs `hysteresis` to spare, so a ship
    // hovering at an edge doesn't flick its hearts back and forth. Falls back
    // to a clear slot ignoring the screen, then to the first slot.
    public static int Choose(Slot[] slots, Vector3 shipPosition, Rect screen, float nearReach,
                             int keep = -1, float hysteresis = .12f)
    {
        if (slots == null || slots.Length == 0) return -1;
        int best = -1;
        for (int i = 0; i < slots.Length; i++)
        {
            if (!slots[i].clear) continue;
            float inset = i == keep ? 0f : hysteresis;
            var r = Rect.MinMaxRect(screen.xMin + inset, screen.yMin + inset, screen.xMax - inset, screen.yMax - inset);
            if (!Inside(r, slots[i].At(shipPosition))) continue;
            if (slots[i].reach <= nearReach + 1e-4f) return i;
            if (best < 0 || slots[i].reach < slots[best].reach - .05f) best = i;
        }
        if (best >= 0) return best;
        for (int i = 0; i < slots.Length; i++)
            if (slots[i].clear && Inside(screen, slots[i].At(shipPosition))) return i;
        // Nothing clear fits: anything on screen, then the best clear one.
        for (int i = 0; i < slots.Length; i++)
            if (Inside(screen, slots[i].At(shipPosition))) return i;
        for (int i = 0; i < slots.Length; i++)
            if (slots[i].clear) return i;
        return 0;
    }

    // The camera's safe area in world units.
    public static Rect ScreenRect(Camera cam)
    {
        if (ScreenOverride != null) return ScreenOverride();
        if (cam == null) cam = Camera.main;
        if (cam == null || Screen.width <= 0 || Screen.height <= 0)
            return Rect.MinMaxRect(-1e4f, -1e4f, 1e4f, 1e4f);
        Rect sa = Screen.safeArea;
        if (sa.width <= 0f || sa.height <= 0f) sa = new Rect(0f, 0f, Screen.width, Screen.height);
        Vector3 a = cam.ViewportToWorldPoint(new Vector3(sa.xMin / Screen.width, sa.yMin / Screen.height, 0f));
        Vector3 b = cam.ViewportToWorldPoint(new Vector3(sa.xMax / Screen.width, sa.yMax / Screen.height, 0f));
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }
}
