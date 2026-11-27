using UnityEngine;

// THE SQUAD'S SLOW ORBIT. Every drone circles the ship on one ring, one full turn every TurnSeconds, however many there are.
//
//   * ONE shared angle (Angle) advances once a frame (Advance, called by FloatieBattery.Step); each drone adds its own PHASE, the
//     even share 2 pi rank / members of the ring. Nothing is integrated per drone, so nothing can drift apart.
//   * The ring grows a little with the squad (RadiusPerExtra per drone past three, capped) and is never smaller than what the
//     drones' size needs (chord >= ChordClearance x a drone), nor than the hull plus a drone (nothing covers the hull).
//   * A drone that joins or leaves (a recruit, a sniper knock-out: DroneSquad.KnockOut) changes the rank of the others: their phases
//     EASE to the new even spacing (EaseRate), and so does the radius. A drone seen for the first time starts on its place.
//   * The ring never crosses the hearts: its clearance is the hearts' LIVE outer radius (FloatieBattery passes HeartOrbit.OrbitRadius
//     plus a heart), so hearts sit inside the ring, drones outside, whatever the squad size or the hearts' crowding.
//   * Near a screen edge the ring is FITTED to the room (Fit): squashed to an ellipse and, when the room is narrower than the ring,
//     slid toward the open side; if even that would cross the clearance circle it becomes a smaller ring BESIDE the ship on the open
//     side. A drone is never pushed over the hull (Clamp is only a last-resort for a tiny view). Works on any aspect ratio
//     (including the 1080x2520 foldable cover).
// Units are the hull's LOCAL units (the same as the old rest offsets); the view tests convert with the hull's scale.
// No allocations: static arrays, no lists.
public static class DroneOrbit
{
    public const float TurnSeconds = 7f;          // one full turn (tunable: 6..8 s)
    public const float EaseRate = 3f;             // 1/s: how fast phases and radii settle after a join / leave
    public const float BaseRadius = 3.2f;         // x the hull's half width, for up to three drones
    public const float RadiusPerExtra = .14f;     // x half width per drone past three
    public const float MaxRadius = 4.6f;          // x half width
    public const float ChordClearance = 1.3f;     // neighbours stay at least this x a drone's size apart
    public const float HullMargin = .12f;         // local units of air between a drone and the hull's corner
    public const float ViewMargin = .05f;

    public static bool Enabled = true;            // tests / fall back to the old rest places
    public static float Angle { get; private set; }   // radians, shared by the whole squad
    public static float Cx { get; private set; }      // the ring's centre, hull-local (0 unless slid at a screen edge)
    public static float Cy { get; private set; }
    public static float Rx { get; private set; }   // current ring radii (local units)
    public static float Ry { get; private set; }

    static readonly float[] phase = new float[DroneSquad.MaxSlots];
    static readonly float[] target = new float[DroneSquad.MaxSlots];
    static readonly bool[] member = new bool[DroneSquad.MaxSlots];
    static readonly bool[] seen = new bool[DroneSquad.MaxSlots];
    static int members;
    static bool ringSeen;

    public static bool Ready => Enabled && ringSeen && (Rx > 0f || Ry > 0f);   // false until the first Advance: the old rest places are used
    public static int Members => members;
    public static bool IsMember(int slot) => slot >= 0 && slot < member.Length && member[slot];
    public static float Phase(int slot) => slot >= 0 && slot < phase.Length ? phase[slot] : 0f;
    public static float TargetPhase(int slot) => slot >= 0 && slot < target.Length ? target[slot] : 0f;

    public static void Reset()
    {
        Angle = 0f; Rx = Ry = Cx = Cy = 0f; members = 0; ringSeen = false;
        for (int i = 0; i < phase.Length; i++) { phase[i] = target[i] = 0f; member[i] = seen[i] = false; }
    }

    // ---- pure pieces (tests) ----

    // The even share of the ring for member `rank` of `count`.
    public static float PhaseFor(int rank, int count) { return count <= 0 ? 0f : Mathf.PI * 2f * rank / count; }

    // The ring radius (local units) for `count` drones around a hull of half width `halfW` and half height `halfH`, drones `droneSize` big.
    public static float RadiusFor(int count, float halfW, float halfH, float droneSize)
    {
        float r = halfW * Mathf.Min(MaxRadius, BaseRadius + RadiusPerExtra * Mathf.Max(0, count - 3));
        if (count >= 2) r = Mathf.Max(r, droneSize * ChordClearance / (2f * Mathf.Sin(Mathf.PI / count)));
        return Mathf.Max(r, HullClear(halfW, halfH, droneSize));
    }

    // Nothing inside this distance of the hull's centre: the hull's corner plus a drone's half size and a margin.
    public static float HullClear(float halfW, float halfH, float droneSize)
    {
        return Mathf.Sqrt(halfW * halfW + halfH * halfH) + droneSize * .5f + HullMargin;
    }

    static float Wrap(float a)
    {
        while (a > Mathf.PI) a -= Mathf.PI * 2f;
        while (a < -Mathf.PI) a += Mathf.PI * 2f;
        return a;
    }

    // Fits one axis of the ring into the room. `r`: wanted radius, `clear`: nothing of the ring may come nearer the hull than this,
    // `lo` / `hi`: room (local units) on the negative / positive side of the hull for a drone's centre. Returns the radius and the
    // centre shift along the axis. Wide room: (r, 0). Too narrow: slide toward the open side; still too near the hull: a smaller ring
    // wholly beside the clearance circle on the open side.
    public static void Fit(float r, float clear, float lo, float hi, out float radius, out float centre)
    {
        float avail = lo + hi;
        radius = r; centre = 0f;
        if (avail >= 2f * r)
        {
            centre = Mathf.Clamp(0f, r - lo, hi - r);
        }
        else
        {
            radius = Mathf.Max(avail * .5f, 0f); centre = (hi - lo) * .5f;
        }
        if (radius - Mathf.Abs(centre) >= clear - 1e-4f) return;
        // beside the ship, on the open side: the nearest point of the ring is `clear` from the hull's centre
        float dir = hi >= lo ? 1f : -1f;
        float open = Mathf.Max(lo, hi);
        radius = Mathf.Clamp((open - clear) * .5f, 0f, r);
        centre = dir * (clear + radius);
    }

    // One frame. `memberMask`: which slots are on the ring (bit i = slot i); `slots` slots exist. `heartClear`: the hearts' live outer
    // radius in local units (0: none). `ship`: the hull's world position, `scale` its world scale, `view`: the visible world rect
    // (width 0 or huge: unbounded).
    public static void Advance(float dt, int memberMask, int slots, Vector2 halfExtents, float droneSize, float heartClear, Vector2 ship, float scale, Rect view)
    {
        int n = 0;
        for (int i = 0; i < member.Length; i++)
        {
            bool m = i < slots && (memberMask & (1 << i)) != 0;
            member[i] = m;
            if (m) n++;
        }
        members = n;
        int rank = 0;
        float k = dt > 0f ? 1f - Mathf.Exp(-EaseRate * dt) : 0f;
        for (int i = 0; i < member.Length; i++)
        {
            if (!member[i]) { seen[i] = false; continue; }
            target[i] = PhaseFor(rank++, n);
            if (!seen[i]) { phase[i] = target[i]; seen[i] = true; }
            else phase[i] += Wrap(target[i] - phase[i]) * k;
        }
        if (dt > 0f) Angle = Wrap(Angle + Mathf.PI * 2f * dt / TurnSeconds);

        float clear = Mathf.Max(HullClear(halfExtents.x, halfExtents.y, droneSize), heartClear + droneSize * .5f + HullMargin);
        float want = Mathf.Max(RadiusFor(Mathf.Max(1, n), halfExtents.x, halfExtents.y, droneSize), clear);
        float wantX = want, wantY = want, wantCx = 0f, wantCy = 0f;
        if (view.width > 0f && view.width < 1000f && scale > 0f)
        {
            float pad = droneSize * .5f + ViewMargin;
            float left = (ship.x - view.xMin) / scale - pad, right = (view.xMax - ship.x) / scale - pad;
            float down = (ship.y - view.yMin) / scale - pad, up = (view.yMax - ship.y) / scale - pad;
            Fit(want, clear, left, right, out wantX, out wantCx);
            Fit(want, clear, down, up, out wantY, out wantCy);
        }
        if (!ringSeen) { Rx = wantX; Ry = wantY; Cx = wantCx; Cy = wantCy; ringSeen = true; }
        else
        {
            Rx += (wantX - Rx) * k; Ry += (wantY - Ry) * k; Cx += (wantCx - Cx) * k; Cy += (wantCy - Cy) * k;
        }
    }

    // Where member `slot` is on the ring, hull-local.
    public static Vector2 Local(int slot)
    {
        float a = Angle + Phase(slot) + Mathf.PI * .5f;   // slot 0 starts straight above... then turns
        return new Vector2(Cx + Mathf.Cos(a) * Rx, Cy + Mathf.Sin(a) * Ry);
    }

    // `p` (world) nudged to keep a body of `size` inside the view.
    public static Vector3 Clamp(Vector3 p, Vector2 size, Rect view)
    {
        if (view.width <= 0f || view.width >= 1000f) return p;
        p.x = Mathf.Clamp(p.x, view.xMin + size.x * .5f + ViewMargin, Mathf.Max(view.xMin + size.x * .5f + ViewMargin, view.xMax - size.x * .5f - ViewMargin));
        p.y = Mathf.Clamp(p.y, view.yMin + size.y * .5f + ViewMargin, Mathf.Max(view.yMin + size.y * .5f + ViewMargin, view.yMax - size.y * .5f - ViewMargin));
        return p;
    }
}
