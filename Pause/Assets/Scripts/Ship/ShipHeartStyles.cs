using UnityEngine;

// How each ship's life hearts orbit it (ShipLivesIndicator): the one table,
// keyed by ShipId.
//
// Every ship's hearts circle the hull, slowly and continuously, on tilted
// orbits that read as 3D: a heart swinging round the back draws behind the
// hull, smaller and dimmer, and comes round the front over it. Each ship has
// its own flavour of orbit -- an atom's crossing electrons, a crown-like
// halo, a comet's train, a gyroscope's counter-rotating rings, a playful
// swarm -- and the spinners (Ninja, UFO) a near-round shield ring carried
// the way the hull turns.
public enum HeartStyle
{
    Atom,        // crossing tilted orbits, evenly fanned, all one way round
    Halo,        // one tilted ring, hearts evenly spaced round it, swaying
    Comet,       // one orbit, the hearts following each other in a train
    Gyro,        // crossing rounder orbits, neighbours going opposite ways
    Swarm,       // jittery crossing orbits that wobble, loop and turn back
    ShieldRing,  // a near-round ring turning with a spinning hull (spinners only)
}

// The numbers behind a style.
public struct OrbitStyle
{
    public float radius;        // x the hull's half-size (plus room for the heart)
    public float speed;         // radians a second round the orbit, before the warp
    public float tilt;          // degrees out of the screen plane (0 flat ring, 90 edge-on)
    public float tiltJitter;    // per-heart spread of the tilt (degrees)
    public float tiltWobble;    // the tilt nods by this much (degrees)
    public float spread;        // degrees the hearts' orbit planes fan over (0: one plane)
    public float rollJitter;    // per-heart spread of the plane's heading (degrees)
    public float precession;    // degrees a second the planes turn round the ship
    public float sway;          // degrees the planes rock to and fro
    public float gap;           // a train's spacing (radians; 0: evenly round)
    public bool alternate;      // every other heart goes the other way round
    public float flourishEvery; // seconds between a heart's flourishes (0 never)
    public float turnBackChance;// a flourish is a turn-back this often, else a loop-de-loop
}

public static class ShipHeartStyles
{
    // By ship id (0 is "none").
    static readonly HeartStyle[] table =
    {
        HeartStyle.Atom,        //  0 none
        HeartStyle.Comet,       //  1 Neon Comet: a comet, so a comet's train
        HeartStyle.Gyro,        //  2 Volt Viper: counter-rotating coils
        HeartStyle.Swarm,       //  3 Solar Fang: sparks buzzing round a small sun
        HeartStyle.Halo,        //  4 Crimson Halo: a halo
        HeartStyle.Gyro,        //  5 Ion Lancer: gyroscope rings round the lance
        HeartStyle.Swarm,       //  6 Jade Phantom: will-o'-the-wisps
        HeartStyle.Halo,        //  7 Gold Warden: five hearts as a turning crown
        HeartStyle.Atom,        //  8 Lightning: charged, electrons round it
        HeartStyle.Comet,       //  9 Ligher: embers trailing round
        HeartStyle.Gyro,        // 10 Paranoid: guards circling both ways
        HeartStyle.ShieldRing,  // 11 Ninja: spins, so a spinning ring
        HeartStyle.Swarm,       // 12 Saboteur: twitchy
        HeartStyle.ShieldRing,  // 13 UFO: spins, so a spinning ring
        HeartStyle.Comet,       // 14 Dove: a flock following its lead
        HeartStyle.Atom,        // 15 Turtle: steady electrons
    };

    public static HeartStyle For(int id)
    {
        // A spinning hull always wears the ring; only a spinner does.
        if (ShipUiSlots.Spins(id)) return HeartStyle.ShieldRing;
        var style = id >= 0 && id < table.Length ? table[id] : HeartStyle.Atom;
        return style == HeartStyle.ShieldRing ? HeartStyle.Atom : style;
    }

    public static OrbitStyle Orbit(HeartStyle style)
    {
        switch (style)
        {
            case HeartStyle.Halo:
                return new OrbitStyle
                {
                    radius = 1.05f, speed = 1.1f, tilt = 60f, tiltWobble = 8f,
                    spread = 0f, sway = 28f, flourishEvery = 9f, turnBackChance = 0f,
                };
            case HeartStyle.Comet:
                return new OrbitStyle
                {
                    radius = 1.05f, speed = 1.35f, tilt = 55f, tiltWobble = 10f,
                    spread = 0f, sway = 40f, precession = 6f, gap = .85f,
                    flourishEvery = 8f, turnBackChance = 0f,
                };
            case HeartStyle.Gyro:
                return new OrbitStyle
                {
                    radius = 1.05f, speed = 1.2f, tilt = 45f, tiltWobble = 10f,
                    spread = 180f, precession = -7f, alternate = true,
                    flourishEvery = 10f, turnBackChance = .5f,
                };
            case HeartStyle.Swarm:
                return new OrbitStyle
                {
                    radius = 1.08f, speed = 1.3f, tilt = 52f, tiltJitter = 14f, tiltWobble = 16f,
                    spread = 180f, rollJitter = 22f, precession = 11f, sway = 15f,
                    flourishEvery = 6f, turnBackChance = .45f,
                };
            case HeartStyle.ShieldRing:
                return new OrbitStyle
                {
                    radius = .82f, speed = 1.5f, tilt = 35f, tiltWobble = 8f,
                    spread = 0f, sway = 20f, precession = 0f,
                    flourishEvery = 9f, turnBackChance = 0f,
                };
            case HeartStyle.Atom:
            default:
                return new OrbitStyle
                {
                    radius = 1.05f, speed = 1.25f, tilt = 55f, tiltWobble = 9f,
                    spread = 180f, precession = 9f,
                    flourishEvery = 8f, turnBackChance = .35f,
                };
        }
    }

    // Styles whose hearts share one orbit (kept evenly apart / in a train)
    // rather than fanning over crossing planes.
    public static bool OnePlane(HeartStyle style)
    {
        var o = Orbit(style);
        return o.spread == 0f;
    }
}
