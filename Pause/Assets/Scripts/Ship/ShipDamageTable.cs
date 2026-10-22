using UnityEngine;

// Where each hull's damage FX come from, per damage state.
//
// A ship's sheet has three damage rows (ShipHullArt), picked by the lives
// LEFT, whatever the ship's max lives (MaxLives(), the one accessor):
//
//   state 0  intact    every life left (no hits taken)
//   state 1  damaged   anything in between (none on a 2-life ship)
//   state 2  critical  the last life
//
// The last life is a wreck: blown-through holes with the frame showing,
// bites torn out of the edges, char round every wound, long cracks, and
// smolder emitters on each of those spots.
//
// Each state's art is designed per hull (a blown panel, a torn wingtip, a bent
// fin, scorch, a cracked canopy on the last life), and so are its emitters (the
// "emitters" of the ship's own data file, ShipArt/<Key>/<Key>_data.json): the sparks come out of
// that hull's blown panel, the smoke out of its damaged engine. A state
// shows its own emitters plus every earlier state's, so a worse state always
// has more. The first emitter a state adds is where the debris burst sprays
// from when the ship reaches it.
//
// Points are in the hull's 128 u design canvas (x right, y down, the cell is
// 2 px per u), measured from the art by the pipeline, which also asserts each
// sits on painted pixels in the rest and bank frames. Local() turns one into
// hull-local units for the pose being shown (bob, bank lean), with the same
// warp the pipeline draws the frames with.
public enum DamageEmitterKind { Sparks, Arc, Smoke, Flame, Leak, Smolder }

public struct DamageEmitter
{
    public int state;               // the damage state it starts at (1 or 2)
    public DamageEmitterKind kind;
    public float u, v;              // design canvas units, y down
    public DamageEmitter(int state, DamageEmitterKind kind, float u, float v)
    {
        this.state = state; this.kind = kind; this.u = u; this.v = v;
    }
}

public static class ShipDamageTable
{
    public const int States = ShipHullArt.States;
    public const float Canvas = 128f;
    const float PixelsPerUnit = ShipHullArt.Cell / Canvas;

    // The flown ship's max lives: the single place the damage art / FX read it.
    // (Before any ship has set it -- an editor tool, a test -- one life per
    // damage row.)
    public static int MaxLives() { return ShipLives.RunMax; }

    // The sheet row for hits taken (collisionDetection.lifeCounter) on a ship
    // with `maxLives` lives: intact at full lives, critical on the last one,
    // damaged in between.
    public const int WreckProportionalFrom = 10;

    public static int StateFor(int hitsTaken, int maxLives)
    {
        if (hitsTaken <= 0) return 0;
        // the last life is the wreck; a 10+ heart hull (Paragon) wrecks on its
        // last two, the same fifth of its health (~20%) the others show it at
        int critical = maxLives >= WreckProportionalFrom ? 2 : 1;
        return maxLives - hitsTaken <= critical ? States - 1 : 1;
    }

    // (Full health needs no maximum: the dock's ships ask every frame.)
    public static int StateFor(int hitsTaken) { return hitsTaken <= 0 ? 0 : StateFor(hitsTaken, MaxLives()); }

    // The ship's emitters, ordered by state (null when it has no data file).
    static DamageEmitter[] RowOf(int id)
    {
        var d = ShipData.For(id);
        return d != null ? d.emitterList : null;
    }

    public static bool Has(int id) { return RowOf(id) != null; }

    // Every emitter of the ship (all states).
    public static int Total(int id) { return Has(id) ? RowOf(id).Length : 0; }

    public static DamageEmitter Get(int id, int index) { return RowOf(id)[index]; }

    // How many emitters are live at `state` (they're the first Count ones).
    public static int Count(int id, int state)
    {
        if (!Has(id)) return 0;
        int n = 0;
        var row = RowOf(id);
        while (n < row.Length && row[n].state <= state) n++;
        return n;
    }

    // Where the debris burst sprays from on reaching `state`: the first
    // emitter that state adds. False for state 0 or an unknown ship.
    public static bool BurstAt(int id, int state, out DamageEmitter emitter)
    {
        emitter = default(DamageEmitter);
        if (!Has(id) || state <= 0) return false;
        var row = RowOf(id);
        for (int i = 0; i < row.Length; i++)
            if (row[i].state == state) { emitter = row[i]; return true; }
        return false;
    }

    // The idle drawings' vertical bob, u (the pipeline's IDLE table; negative is up).
    static readonly float[] idleDy = { 0f, -2f, -2f, 0f, 0f, 2f };

    // A canvas point as drawn in sheet column `column` (the bob of an idle
    // drawing, the lean of a bank pose; the pipeline's Pose + hullkit.warp).
    public static Vector2 Posed(float u, float v, int column)
    {
        float dy = column >= 0 && column < idleDy.Length ? idleDy[column] : 0f;
        float bank = column == ShipHullArt.BankLeft ? -1f : column == ShipHullArt.BankRight ? 1f : 0f;
        if (bank != 0f)
        {
            float d = u - 64f;
            bool near = (d < 0f) == (bank < 0f);
            float k = near ? .80f : .95f;
            u = 64f + d * k + 1.5f * bank;
            v += (near ? 1.2f : -.6f) * Mathf.Abs(d) / 60f;
        }
        return new Vector2(u, v + dy);
    }

    // A canvas point in hull-local units (the hull sprite's pivot is its
    // rect's centre), for the pose in `column`.
    public static Vector2 Local(int id, float u, float v, int column)
    {
        var r = ShipHullArt.RectFor(id);
        Vector2 p = Posed(u, v, column);
        float px = p.x * PixelsPerUnit - r.x;                       // from the rect's left
        float py = (ShipHullArt.Cell - p.y * PixelsPerUnit) - r.y;  // from the rect's bottom
        return new Vector2((px - r.w * .5f) / r.pixelsPerUnit, (py - r.h * .5f) / r.pixelsPerUnit);
    }

    public static Vector2 Local(int id, DamageEmitter e, int column) { return Local(id, e.u, e.v, column); }

    // The emitter's outward direction (hull-local, unit): away from the
    // hull's centre, for sparks and debris.
    public static Vector2 Outward(DamageEmitter e)
    {
        var d = new Vector2(e.u - 64f, 64f - e.v);
        return d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.up;
    }
}
