using UnityEngine;

// Where each hull's damage FX come from, per damage state.
//
// Every ship has collisionDetection.MAXLIFE = 3 lives, and its sheet's
// damage rows (ShipHullArt) map to the hits taken:
//
//   state 0  intact    lifeCounter 0   3 lives left
//   state 1  damaged   lifeCounter 1   2 lives left
//   state 2  critical  lifeCounter 2   the last life
//
// Each state's art is designed per hull (Art/Resources/ShipArt/Hulls/src~/
// damage.py: a blown panel, a torn wingtip, a bent fin, scorch, a cracked
// canopy on the last life), and so are its emitters: the sparks come out of
// that hull's blown panel, the smoke out of its damaged engine. A state
// shows its own emitters plus every earlier state's, so a worse state always
// has more. The first emitter a state adds is where the debris burst sprays
// from when the ship reaches it.
//
// Points are in the hull's 128 u design canvas (x right, y down, the cell is
// 2 px per u), measured from the art by build.py, which also asserts each
// sits on painted pixels in the rest and bank frames. Local() turns one into
// hull-local units for the pose being shown (bob, bank lean), with the same
// warp build.py draws the frames with.
public enum DamageEmitterKind { Sparks, Arc, Smoke, Flame, Leak }

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

    static DamageEmitter E(int state, DamageEmitterKind kind, float u, float v) { return new DamageEmitter(state, kind, u, v); }
    const DamageEmitterKind Sparks = DamageEmitterKind.Sparks, Arc = DamageEmitterKind.Arc,
                            Smoke = DamageEmitterKind.Smoke, Flame = DamageEmitterKind.Flame,
                            Leak = DamageEmitterKind.Leak;

    // Per ship id, ordered by state.
    static readonly DamageEmitter[][] table =
    {
        new DamageEmitter[0],
        // BEGIN GENERATED EMITTERS
        //  1 NeonComet: 1) left wing panel blown, wiring sparks; scorched right wing; 2) right wingtip torn off, spine cracked, right pod burning, canopy cracked
        new[] { E(1, Sparks, 32.6f, 82.6f), E(1, Smoke, 52.2f, 106.4f), E(2, Arc, 107.2f, 92.3f), E(2, Flame, 75.8f, 104.6f), E(2, Leak, 69.9f, 92.3f) },
        //  2 VoltViper: 1) hull panel blown over the right wing root; left engine smoking; 2) left wingtip torn, right wing bent, right leg on fire, canopy cracked
        new[] { E(1, Sparks, 75.0f, 63.1f), E(1, Smoke, 53.0f, 100.9f), E(2, Arc, 17.2f, 70.6f), E(2, Flame, 75.0f, 100.9f), E(2, Leak, 69.5f, 89.5f) },
        //  3 SolarFang: 1) left flank panel blown, reactor cracked; nozzle smoking; 2) left fang bent, reactor housing open and arcing, right tip torn, canopy cracked
        new[] { E(1, Sparks, 46.3f, 91.5f), E(1, Smoke, 64.0f, 106.8f), E(2, Arc, 69.9f, 76.2f), E(2, Flame, 103.3f, 108.8f), E(2, Leak, 83.6f, 102.7f) },
        //  4 CrimsonHalo: 1) left wing panel blown, left halo cracked; tail smoking; 2) right wingtip torn, right halo cracked and arcing, right tail fin bent, canopy cracked
        new[] { E(1, Sparks, 38.8f, 84.3f), E(1, Smoke, 61.5f, 109.4f), E(2, Arc, 82.5f, 45.7f), E(2, Flame, 99.2f, 93.9f), E(2, Leak, 65.7f, 103.6f) },
        //  5 IonLancer: 1) left wing panel blown; left pod smoking; 2) right canard bent, right wingtip torn, right wing wiring arcing, canopy cracked
        new[] { E(1, Sparks, 36.5f, 99.0f), E(1, Smoke, 55.4f, 113.0f), E(2, Arc, 88.1f, 93.0f), E(2, Flame, 72.6f, 113.0f), E(2, Leak, 64.0f, 99.0f) },
        //  6 JadePhantom: 1) left wing panel blown; spine smoking; 2) right trailing edge torn, left wingtip bent, right wing wiring arcing, canopy cracked
        new[] { E(1, Sparks, 26.4f, 65.1f), E(1, Smoke, 61.2f, 109.1f), E(2, Arc, 106.2f, 58.5f), E(2, Flame, 91.5f, 91.5f), E(2, Leak, 65.8f, 100.3f) },
        //  7 GoldWarden: 1) right wing panel blown; left engine smoking; 2) left gun barrel bent, left wingtip torn, belly panel open and arcing, canopy cracked
        new[] { E(1, Sparks, 87.4f, 67.1f), E(1, Smoke, 45.5f, 104.5f), E(2, Arc, 63.0f, 98.2f), E(2, Flame, 82.5f, 105.5f), E(2, Leak, 21.1f, 85.8f) },
        //  8 Lightning: 1) left pod panel blown; left pod engine smoking; 2) right wingtip torn, right pod nose bent, right pod on fire, canopy cracked
        new[] { E(1, Sparks, 43.1f, 68.1f), E(1, Smoke, 43.1f, 110.7f), E(2, Arc, 109.5f, 94.1f), E(2, Flame, 84.9f, 110.7f), E(2, Leak, 62.1f, 92.0f) },
        //  9 Ligher: 1) hull panel blown, nose cap cracked; nozzle smoking; 2) right fin bent, second panel open and arcing, leaking, canopy cracked
        new[] { E(1, Sparks, 51.1f, 55.2f), E(1, Smoke, 59.7f, 111.1f), E(2, Arc, 74.8f, 86.6f), E(2, Flame, 70.5f, 109.2f), E(2, Leak, 94.2f, 99.4f) },
        // 10 Paranoid: 1) disc rim panel blown; left pod smoking; 2) right pod torn open, right strut bent, disc wiring arcing, canopy cracked
        new[] { E(1, Sparks, 45.7f, 39.8f), E(1, Smoke, 17.7f, 92.8f), E(2, Arc, 90.1f, 65.2f), E(2, Flame, 110.3f, 92.8f), E(2, Leak, 64.0f, 97.4f) },
        // 11 Ninja: 1) top blade panel blown; hub smoking; 2) right blade tip torn, bottom blade bent, left blade wiring arcing, canopy cracked
        new[] { E(1, Sparks, 64.0f, 31.9f), E(1, Smoke, 75.9f, 75.9f), E(2, Arc, 38.3f, 64.0f), E(2, Flame, 64.0f, 95.2f), E(2, Leak, 102.5f, 64.0f) },
        // 12 Saboteur: 1) left horn panel blown, core cracked; tail smoking; 2) right horn tip torn and bent, cradle panel open and arcing, canopy cracked
        new[] { E(1, Sparks, 33.3f, 36.5f), E(1, Smoke, 64.0f, 107.2f), E(2, Arc, 40.1f, 84.6f), E(2, Flame, 89.0f, 87.6f), E(2, Leak, 98.1f, 16.9f) },
        // 13 UFO: 1) rim panel blown; underside smoking; 2) rim torn at upper right and burning, second rim panel arcing, canopy cracked
        new[] { E(1, Sparks, 22.2f, 73.5f), E(1, Smoke, 88.7f, 94.4f), E(2, Arc, 40.3f, 101.0f), E(2, Flame, 103.9f, 41.2f), E(2, Leak, 69.7f, 105.8f) },
        // 14 Dove: 1) flank panel blown; tail smoking; 2) right wing torn, left wing bent, belly panel open, canopy cracked
        new[] { E(1, Sparks, 47.1f, 59.1f), E(1, Smoke, 64.0f, 107.2f), E(2, Arc, 97.9f, 60.1f), E(2, Flame, 72.5f, 89.5f), E(2, Leak, 54.6f, 87.6f) },
        // 15 Turtle: 1) shell plate blown, scute cracked; tail smoking; 2) rear scute shattered and arcing, front flipper bent, shell torn, canopy cracked
        new[] { E(1, Sparks, 45.7f, 72.6f), E(1, Smoke, 64.0f, 110.4f), E(2, Arc, 64.0f, 88.8f), E(2, Flame, 87.4f, 78.0f), E(2, Leak, 49.7f, 103.9f) },
        // END GENERATED EMITTERS
    };

    // The sheet row for hits taken (collisionDetection.lifeCounter).
    public static int StateFor(int hitsTaken) { return Mathf.Clamp(hitsTaken, 0, States - 1); }

    public static bool Has(int id) { return id > 0 && id < table.Length; }

    // Every emitter of the ship (all states).
    public static int Total(int id) { return Has(id) ? table[id].Length : 0; }

    public static DamageEmitter Get(int id, int index) { return table[id][index]; }

    // How many emitters are live at `state` (they're the first Count ones).
    public static int Count(int id, int state)
    {
        if (!Has(id)) return 0;
        int n = 0;
        var row = table[id];
        while (n < row.Length && row[n].state <= state) n++;
        return n;
    }

    // Where the debris burst sprays from on reaching `state`: the first
    // emitter that state adds. False for state 0 or an unknown ship.
    public static bool BurstAt(int id, int state, out DamageEmitter emitter)
    {
        emitter = default(DamageEmitter);
        if (!Has(id) || state <= 0) return false;
        var row = table[id];
        for (int i = 0; i < row.Length; i++)
            if (row[i].state == state) { emitter = row[i]; return true; }
        return false;
    }

    // The idle drawings' vertical bob, u (build.py IDLE; negative is up).
    static readonly float[] idleDy = { 0f, -2f, -2f, 0f, 0f, 2f };

    // A canvas point as drawn in sheet column `column` (the bob of an idle
    // drawing, the lean of a bank pose; build.py's Pose + hullkit.warp).
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
