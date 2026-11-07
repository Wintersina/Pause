using UnityEngine;

// The HOMING ATOMS booster's flight (Boosters.Kind.HomingAtoms, docs/economy.md "Boosters"). While the booster runs
// (Boosters.HomingRunning), an atom that comes on screen stops its AtomWander drift, locks onto the ship ONCE and flies
// at it like a slow missile: a fixed speed and a limited turn rate, so it cannot turn on the spot.
//
//   CATCH    flying into the hull collects it the ordinary way (collisionDetection), so it pays, scores and
//            counts exactly as if the pilot had flown into it.
//   DODGE    a quick side-step close in leaves it unable to turn in time: once the ship is more than MissAngle
//            off its heading it has MISSED.
//   TELEPORT a pause-jump (movePlayer -> OnTeleport) breaks every lock at once: each atom missed.
//   GIVE UP  after MaxSeconds without a catch (an atom circling a ship inside its turn circle) it has missed too.
//
// A missed atom goes back to its ordinary wander from where it is (falls, wanders, leaves the bottom) and never locks
// on again. Only atoms move this way (anything moveItemEnmInStrightLine hands to AtomWander): star dust is not pulled.
// It moves only on the frames the world moves (the mover's Step), so a frozen world freezes it in place, and a magnet
// pull (MagnetPull) switches the mover off and hauls the atom itself.
public class AtomHoming
{
    // ---- tuning (world units, seconds) ----
    public const float Speed = 6f;                  // u/s: the top of the view to the hull in about two seconds
    public const float DescentShare = 1.6f;         // ... and never slower than this x the atom's own descent (fast worlds)
    public const float TurnRate = 2.2f;             // rad/s: a turn circle of ~2.7 u, wide enough to side-step
    public const float MissAngle = 100f;            // degrees: the ship this far off the heading = missed
    public const float MaxSeconds = 4f;             // a lock that has not caught by then gives up
    public const float LockBelowTop = .3f;          // locks once this far inside the top of the view
    public const float MaxSubstep = .02f;           // big frames are split, so a hitch does not skip the turn

    // Pause-jumps so far (movePlayer calls OnTeleport). A lock remembers the count it started with.
    public static int Teleports { get; private set; }
    public static void OnTeleport() { Teleports++; }

    public Vector2 heading = Vector2.down;          // unit; starts straight down, the way the atom was falling
    public float elapsed;
    public bool Missed { get; private set; }
    readonly int teleportsAtLock;

    public AtomHoming() { teleportsAtLock = Teleports; }

    // Whether an atom at `y` may lock on now (it is inside the top of the view).
    public static bool CanLock(float y, float viewTop) { return y <= viewTop - LockBelowTop; }

    public static float SpeedFor(float worldSpeed)
    {
        return Mathf.Max(Speed, AtomWander.Descent(worldSpeed) * DescentShare);
    }

    // One step toward `ship`. dt is scaled time. Sets Missed (and stops moving the atom) on a dodge, a teleport or a timeout.
    public Vector3 Advance(Vector3 p, float dt, float worldSpeed, Vector2 ship)
    {
        if (Missed || dt <= 0f) return p;
        if (Teleports != teleportsAtLock || elapsed >= MaxSeconds) { Missed = true; return p; }
        float speed = SpeedFor(worldSpeed);
        float cosMiss = Mathf.Cos(MissAngle * Mathf.Deg2Rad);
        int steps = Mathf.Max(1, Mathf.CeilToInt(dt / MaxSubstep));
        float h = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            Vector2 to = ship - (Vector2)p;
            float dist = to.magnitude;
            if (dist <= speed * h) { p.x = ship.x; p.y = ship.y; break; }   // on the hull: the collision collects it
            Vector2 want = to / dist;
            if (Vector2.Dot(heading, want) < cosMiss) { Missed = true; break; }
            heading = RotateToward(heading, want, TurnRate * h);
            p.x += heading.x * speed * h;
            p.y += heading.y * speed * h;
            p.x = Mathf.Clamp(p.x, -AtomWander.LaneHalfWidth, AtomWander.LaneHalfWidth);
        }
        elapsed += dt;
        return p;
    }

    // `from` turned toward `to` by at most `maxRadians` (both unit vectors).
    public static Vector2 RotateToward(Vector2 from, Vector2 to, float maxRadians)
    {
        float angle = Vector2.SignedAngle(from, to) * Mathf.Deg2Rad;
        float turn = Mathf.Clamp(angle, -maxRadians, maxRadians);
        float c = Mathf.Cos(turn), s = Mathf.Sin(turn);
        return new Vector2(from.x * c - from.y * s, from.x * s + from.y * c).normalized;
    }
}
