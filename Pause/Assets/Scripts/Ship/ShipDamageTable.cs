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
// smolder emitters on each of those spots (damage.py wreck()).
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

    static DamageEmitter E(int state, DamageEmitterKind kind, float u, float v) { return new DamageEmitter(state, kind, u, v); }
    const DamageEmitterKind Sparks = DamageEmitterKind.Sparks, Arc = DamageEmitterKind.Arc,
                            Smoke = DamageEmitterKind.Smoke, Flame = DamageEmitterKind.Flame,
                            Leak = DamageEmitterKind.Leak, Smolder = DamageEmitterKind.Smolder;

    // Per ship id, ordered by state.
    static readonly DamageEmitter[][] table =
    {
        new DamageEmitter[0],
        // BEGIN GENERATED EMITTERS
        //  1 NeonComet: 1) left wing panel blown, wiring sparks; scorched right wing; 2) right wingtip torn off, spine cracked, right pod burning, canopy cracked
        new[] { E(1, Sparks, 32.6f, 82.6f), E(1, Smoke, 52.2f, 106.4f), E(1, Smolder, 32.8f, 82.3f), E(2, Arc, 107.2f, 92.3f), E(2, Flame, 75.8f, 104.6f), E(2, Leak, 69.9f, 92.3f), E(2, Smolder, 108.4f, 92.6f), E(2, Smolder, 82.2f, 90.1f), E(2, Smolder, 55.2f, 97.6f), E(2, Smolder, 75.2f, 61.3f), E(2, Smolder, 53.2f, 61.9f) },
        //  2 VoltViper: 1) hull panel blown over the right wing root; left engine smoking; 2) left wingtip torn, right wing bent, right leg on fire, canopy cracked
        new[] { E(1, Sparks, 75.0f, 63.1f), E(1, Smoke, 53.0f, 100.9f), E(1, Smolder, 75.2f, 63.3f), E(2, Arc, 17.2f, 70.6f), E(2, Flame, 75.0f, 100.9f), E(2, Leak, 69.5f, 89.5f), E(2, Smolder, 16.5f, 70.6f), E(2, Smolder, 93.9f, 55.5f), E(2, Smolder, 69.2f, 23.1f), E(2, Smolder, 48.1f, 79.4f), E(2, Smolder, 76.5f, 85.6f) },
        //  3 SolarFang: 1) left flank panel blown, reactor cracked; nozzle smoking; 2) left fang bent, reactor housing open and arcing, right tip torn, canopy cracked
        new[] { E(1, Sparks, 46.3f, 91.5f), E(1, Smoke, 64.0f, 106.8f), E(1, Smolder, 46.8f, 91.0f), E(2, Arc, 69.9f, 76.2f), E(2, Flame, 103.3f, 108.8f), E(2, Leak, 83.6f, 102.7f), E(2, Smolder, 103.3f, 109.8f), E(2, Smolder, 70.4f, 76.2f), E(2, Smolder, 82.9f, 63.1f), E(2, Smolder, 53.3f, 46.4f), E(2, Smolder, 78.4f, 106.3f), E(2, Smolder, 24.3f, 96.8f) },
        //  4 CrimsonHalo: 1) left wing panel blown, left halo cracked; tail smoking; 2) right wingtip torn, right halo cracked and arcing, right tail fin bent, canopy cracked
        new[] { E(1, Sparks, 38.8f, 84.3f), E(1, Smoke, 61.5f, 109.4f), E(1, Smolder, 39.7f, 84.0f), E(2, Arc, 82.5f, 45.7f), E(2, Flame, 99.2f, 93.9f), E(2, Leak, 65.7f, 103.6f), E(2, Smolder, 100.9f, 93.7f), E(2, Smolder, 77.5f, 76.3f), E(2, Smolder, 51.0f, 71.9f), E(2, Smolder, 48.5f, 110.1f) },
        //  5 IonLancer: 1) left wing panel blown; left pod smoking; 2) right canard bent, right wingtip torn, right wing wiring arcing, canopy cracked
        new[] { E(1, Sparks, 36.5f, 99.0f), E(1, Smoke, 55.4f, 113.0f), E(1, Smolder, 36.5f, 99.2f), E(2, Arc, 88.1f, 93.0f), E(2, Flame, 72.6f, 113.0f), E(2, Leak, 64.0f, 99.0f), E(2, Smolder, 105.3f, 112.0f), E(2, Smolder, 88.1f, 93.0f), E(2, Smolder, 51.6f, 96.9f), E(2, Smolder, 74.7f, 72.5f), E(2, Smolder, 49.4f, 76.5f), E(2, Smolder, 27.5f, 109.8f) },
        //  6 JadePhantom: 1) left wing panel blown; spine smoking; 2) right trailing edge torn, left wingtip bent, right wing wiring arcing, canopy cracked
        new[] { E(1, Sparks, 26.4f, 65.1f), E(1, Smoke, 61.2f, 109.1f), E(1, Smolder, 26.4f, 65.7f), E(2, Arc, 106.2f, 58.5f), E(2, Flame, 91.5f, 91.5f), E(2, Leak, 65.8f, 100.3f), E(2, Smolder, 91.3f, 93.3f), E(2, Smolder, 106.2f, 58.5f), E(2, Smolder, 81.1f, 59.5f), E(2, Smolder, 64.6f, 88.6f), E(2, Smolder, 49.2f, 58.0f), E(2, Smolder, 62.3f, 105.9f) },
        //  7 GoldWarden: 1) right wing panel blown; left engine smoking; 2) left gun barrel bent, left wingtip torn, belly panel open and arcing, canopy cracked
        new[] { E(1, Sparks, 87.4f, 67.1f), E(1, Smoke, 45.5f, 104.5f), E(1, Smolder, 87.4f, 66.6f), E(2, Arc, 63.0f, 98.2f), E(2, Flame, 82.5f, 105.5f), E(2, Leak, 21.1f, 85.8f), E(2, Smolder, 21.1f, 85.3f), E(2, Smolder, 63.0f, 97.7f), E(2, Smolder, 44.8f, 78.4f), E(2, Smolder, 91.5f, 82.9f), E(2, Smolder, 65.0f, 17.5f), E(2, Smolder, 82.9f, 51.8f) },
        //  8 Lightning: 1) left pod panel blown; left pod engine smoking; 2) right wingtip torn, right pod nose bent, right pod on fire, canopy cracked
        new[] { E(1, Sparks, 43.1f, 68.1f), E(1, Smoke, 43.1f, 110.7f), E(1, Smolder, 43.1f, 68.1f), E(2, Arc, 109.5f, 94.1f), E(2, Flame, 84.9f, 110.7f), E(2, Leak, 62.1f, 92.0f), E(2, Smolder, 111.4f, 95.1f), E(2, Smolder, 62.6f, 48.4f), E(2, Smolder, 31.7f, 96.2f), E(2, Smolder, 64.3f, 24.0f), E(2, Smolder, 94.5f, 61.4f), E(2, Smolder, 34.7f, 55.9f) },
        //  9 Ligher: 1) hull panel blown, nose cap cracked; nozzle smoking; 2) right fin bent, second panel open and arcing, leaking, canopy cracked
        new[] { E(1, Sparks, 51.1f, 55.2f), E(1, Smoke, 59.7f, 111.1f), E(1, Smolder, 51.6f, 55.2f), E(2, Arc, 74.8f, 86.6f), E(2, Flame, 70.5f, 109.2f), E(2, Leak, 94.2f, 99.4f), E(2, Smolder, 74.8f, 86.6f), E(2, Smolder, 56.7f, 75.6f), E(2, Smolder, 64.9f, 104.5f), E(2, Smolder, 82.1f, 59.5f), E(2, Smolder, 75.2f, 42.7f) },
        // 10 Paranoid: 1) disc rim panel blown; left pod smoking; 2) right pod torn open, right strut bent, disc wiring arcing, canopy cracked
        new[] { E(1, Sparks, 45.7f, 39.8f), E(1, Smoke, 17.7f, 92.8f), E(1, Smolder, 45.7f, 39.8f), E(2, Arc, 90.1f, 65.2f), E(2, Flame, 110.3f, 92.8f), E(2, Leak, 64.0f, 97.4f), E(2, Smolder, 107.4f, 61.7f), E(2, Smolder, 90.1f, 65.2f), E(2, Smolder, 16.9f, 81.3f), E(2, Smolder, 114.0f, 80.2f), E(2, Smolder, 46.7f, 73.9f), E(2, Smolder, 114.2f, 32.5f) },
        // 11 Ninja: 1) top blade panel blown; hub smoking; 2) right blade tip torn, bottom blade bent, left blade wiring arcing, canopy cracked
        new[] { E(1, Sparks, 64.0f, 31.9f), E(1, Smoke, 75.9f, 75.9f), E(1, Smolder, 63.8f, 31.9f), E(2, Arc, 38.3f, 64.0f), E(2, Flame, 64.0f, 95.2f), E(2, Leak, 102.5f, 64.0f), E(2, Smolder, 103.0f, 64.0f), E(2, Smolder, 37.9f, 63.5f), E(2, Smolder, 76.7f, 82.0f), E(2, Smolder, 75.3f, 47.6f), E(2, Smolder, 55.9f, 91.2f) },
        // 12 Saboteur: 1) left horn panel blown, core cracked; tail smoking; 2) right horn tip torn and bent, cradle panel open and arcing, canopy cracked
        new[] { E(1, Sparks, 33.3f, 36.5f), E(1, Smoke, 64.0f, 107.2f), E(1, Smolder, 33.9f, 36.5f), E(2, Arc, 40.1f, 84.6f), E(2, Flame, 89.0f, 87.6f), E(2, Leak, 98.1f, 16.9f), E(2, Smolder, 99.8f, 14.9f), E(2, Smolder, 40.1f, 84.6f), E(2, Smolder, 40.9f, 60.5f), E(2, Smolder, 64.0f, 52.5f), E(2, Smolder, 92.5f, 32.5f), E(2, Smolder, 60.6f, 82.3f) },
        // 13 UFO: 1) rim panel blown; underside smoking; 2) rim torn at upper right and burning, second rim panel arcing, canopy cracked
        new[] { E(1, Sparks, 22.2f, 73.5f), E(1, Smoke, 88.7f, 94.4f), E(1, Smolder, 22.2f, 73.5f), E(2, Arc, 40.3f, 101.0f), E(2, Flame, 103.9f, 41.2f), E(2, Leak, 69.7f, 105.8f), E(2, Smolder, 102.3f, 38.9f), E(2, Smolder, 40.3f, 101.0f), E(2, Smolder, 32.1f, 54.6f), E(2, Smolder, 37.8f, 75.8f), E(2, Smolder, 47.9f, 23.6f), E(2, Smolder, 20.7f, 91.1f) },
        // 14 Dove: 1) flank panel blown; tail smoking; 2) right wing torn, left wing bent, belly panel open, canopy cracked
        new[] { E(1, Sparks, 47.1f, 59.1f), E(1, Smoke, 64.0f, 107.2f), E(1, Smolder, 47.1f, 59.1f), E(2, Arc, 97.9f, 60.1f), E(2, Flame, 72.5f, 89.5f), E(2, Leak, 54.6f, 87.6f), E(2, Smolder, 99.8f, 60.1f), E(2, Smolder, 72.5f, 88.6f), E(2, Smolder, 57.9f, 16.5f), E(2, Smolder, 82.6f, 43.5f), E(2, Smolder, 42.0f, 38.3f), E(2, Smolder, 79.3f, 19.8f) },
        // 15 Turtle: 1) shell plate blown, scute cracked; tail smoking; 2) rear scute shattered and arcing, front flipper bent, shell torn, canopy cracked
        new[] { E(1, Sparks, 45.7f, 72.6f), E(1, Smoke, 64.0f, 110.4f), E(1, Smolder, 45.7f, 72.6f), E(2, Arc, 64.0f, 88.8f), E(2, Flame, 87.4f, 78.0f), E(2, Leak, 49.7f, 103.9f), E(2, Smolder, 64.5f, 88.8f), E(2, Smolder, 88.5f, 79.1f), E(2, Smolder, 41.1f, 44.8f), E(2, Smolder, 73.0f, 20.3f), E(2, Smolder, 50.1f, 19.2f), E(2, Smolder, 55.0f, 109.6f) },
        // END GENERATED EMITTERS
    };

    // The flown ship's max lives: the single place the damage art / FX read it.
    // (Before any ship has set it -- an editor tool, a test -- one life per
    // damage row.)
    public static int MaxLives() { return ShipLives.RunMax; }

    // The sheet row for hits taken (collisionDetection.lifeCounter) on a ship
    // with `maxLives` lives: intact at full lives, critical on the last one,
    // damaged in between.
    public static int StateFor(int hitsTaken, int maxLives)
    {
        if (hitsTaken <= 0) return 0;
        return maxLives - hitsTaken <= 1 ? States - 1 : 1;
    }

    // (Full health needs no maximum: the dock's ships ask every frame.)
    public static int StateFor(int hitsTaken) { return hitsTaken <= 0 ? 0 : StateFor(hitsTaken, MaxLives()); }

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
