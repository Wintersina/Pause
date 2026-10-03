using UnityEngine;

// How the ultimate's charge indicator in front of the hull animates.
// One per roster ship, each themed on that ship's weapon.
public enum ChargeStyle
{
    CometOrbit,      // a light streak races round an octagonal track
    CoilViper,       // a segmented mechanical snake lights up tail to head
    SolarFangs,      // a jagged sun core; its fangs ignite one by one
    HaloCapacitor,   // a rail capacitor ring fills segment by segment
    FocusLens,       // beams converge through a lens until it focuses
    PlasmaCore,      // a jagged energy core swells, arcs crackle
    MissileRack,     // panel-lined missiles slide out of the rack one by one
    ThunderRings,    // rings drop onto a lightning rod
    PilotFlame,      // a lighter's pilot light grows into a blowtorch
    WatchingEye,     // an eye wakes up and starts looking around
    ShurikenSpin,    // shuriken blades snap out and spin up
    GatlingHeat,     // a gatling cluster spins up through the heat colours
    DroneFormation,  // drones swoop in and lock into formation
    FeatherFan,      // wing feathers fan out around a halo
    ShellPlates,     // hex shell plates lock in around a core
}

// The homing shot each ship's ultimate releases.
public enum ShotStyle
{
    CometStreak, ViperBolt, SolarFireball, RailSlug, IonLance, PlasmaWisp,
    GoldMissile, LightningJavelin, Fireball, SeekerEye, Shuriken, HeavyShell,
    SaucerDrone, FeatherDart, ShellDisc,
}

public struct WeaponStyle
{
    public int shipId;
    // Resources/Weapons/<artKey>.png, rendered from Art/Weapons/src~/weapons.py
    public string artKey;
    public ChargeStyle charge;
    public ShotStyle shot;
    // Cel tones (match the PALETTES row in weapons.py): main is the weapon
    // body, energy the light it throws -- used for the long light trail.
    public Color main, shade, energy;
    // Indicator spin at full charge, degrees per second (0: never spins,
    // used where the art itself carries a fixed reading direction).
    public float indicatorSpin;
    // Shot behaviour: spin (0 = face the heading), sideways weave and
    // cruise speed. Flipbook timing is the shared tick table in WeaponArt.
    public float shotSpin;
    public float weaveAmplitude;
    public float weaveFrequency;
    public float speed;
    // Ready-tell tick pitch, so each weapon "counts down" in its own voice.
    public float tickPitch;
}

// One table keyed by ship id: the indicator style, shot style, colours and
// art for every ship's ultimate. Everything about how a weapon looks is
// here or in its atlas; nothing is derived from the ship index elsewhere.
//
// Lives apart from shopingShips (the roster) on purpose -- it only reads
// the roster's numbering.
public static class WeaponStyleTable
{
    static Color Hex(string hex)
    {
        Color c;
        return ColorUtility.TryParseHtmlString(hex, out c) ? c : Color.white;
    }

    static WeaponStyle Make(int id, string key, ChargeStyle charge, ShotStyle shot,
                            string main, string shade, string energy,
                            float indicatorSpin, float spin, float weaveAmp, float weaveFreq,
                            float speed, float tickPitch)
    {
        return new WeaponStyle
        {
            shipId = id, artKey = key, charge = charge, shot = shot,
            main = Hex(main), shade = Hex(shade), energy = Hex(energy),
            indicatorSpin = indicatorSpin, shotSpin = spin,
            weaveAmplitude = weaveAmp, weaveFrequency = weaveFreq, speed = speed, tickPitch = tickPitch,
        };
    }

    // Palette shorthands: docs/art-style.md section 1, as in weapons.py.
    const string Kaneda = "#D8232C", KanedaShade = "#86121F";
    const string Sodium = "#F2862B", SodiumShade = "#A9481A";
    const string Amber = "#FFB43C", AmberShade = "#A9481A";
    const string Cyan = "#6EF2EE", Teal = "#1FB5B9";
    const string Bone = "#F4EAD4", GunHi = "#5A5C78";

    // Indexed by ship id. Index 0 ("non") is not a flyable hull; it borrows
    // the starter's weapon so a stray index never renders nothing.
    static readonly WeaponStyle[] table =
    {
        Make(0,  "NeonComet",   ChargeStyle.CometOrbit,     ShotStyle.CometStreak,      Kaneda, KanedaShade, Cyan,    0f, 0f,    0f,   0f,  6.2f, 1.00f),
        Make(1,  "NeonComet",   ChargeStyle.CometOrbit,     ShotStyle.CometStreak,      Kaneda, KanedaShade, Cyan,    0f, 0f,    0f,   0f,  6.2f, 1.00f),
        Make(2,  "VoltViper",   ChargeStyle.CoilViper,      ShotStyle.ViperBolt,        Sodium, SodiumShade, Teal,    0f, 0f,   .34f, 18f,  6.0f, 1.12f),
        Make(3,  "SolarFang",   ChargeStyle.SolarFangs,     ShotStyle.SolarFireball,    Amber,  AmberShade,  Sodium,  0f, 0f,    0f,   0f,  5.6f, 0.90f),
        Make(4,  "CrimsonHalo", ChargeStyle.HaloCapacitor,  ShotStyle.RailSlug,         Kaneda, KanedaShade, Amber, 0f, 0f,    0f,   0f,  7.0f, 1.05f),
        Make(5,  "IonLancer",   ChargeStyle.FocusLens,      ShotStyle.IonLance,         Kaneda, KanedaShade, Cyan,    0f, 0f,    0f,   0f,  7.4f, 1.25f),
        Make(6,  "JadePhantom", ChargeStyle.PlasmaCore,     ShotStyle.PlasmaWisp,       Sodium, SodiumShade, Teal,    0f, 0f,   .22f,  9f,  5.4f, 0.85f),
        Make(7,  "GoldWarden",  ChargeStyle.MissileRack,    ShotStyle.GoldMissile,      Amber,  AmberShade,  Kaneda,  0f, 0f,   .12f, 11f,  6.0f, 0.95f),
        Make(8,  "Lightning",   ChargeStyle.ThunderRings,   ShotStyle.LightningJavelin, Sodium, SodiumShade, Cyan,    0f, 0f,    0f,   0f,  7.2f, 1.30f),
        Make(9,  "Ligher",      ChargeStyle.PilotFlame,     ShotStyle.Fireball,         Kaneda, KanedaShade, Amber,   0f, 0f,   .16f, 13f,  5.8f, 0.80f),
        Make(10, "Paranoid",    ChargeStyle.WatchingEye,    ShotStyle.SeekerEye,        Kaneda, KanedaShade, Teal,    0f, 0f,   .30f, 12f,  5.6f, 0.75f),
        Make(11, "Ninja",       ChargeStyle.ShurikenSpin,   ShotStyle.Shuriken,         Bone, GunHi, Kaneda, 540f, -900f, 0f, 0f,  6.6f, 1.40f),
        Make(12, "Saboteur",    ChargeStyle.GatlingHeat,    ShotStyle.HeavyShell,       Kaneda, KanedaShade, Sodium,  720f, 0f,    0f,   0f,  6.4f, 0.70f),
        Make(13, "UFO",         ChargeStyle.DroneFormation, ShotStyle.SaucerDrone,      Sodium, SodiumShade, Cyan,    0f, 0f,   .26f, 7f,  5.2f, 1.18f),
        Make(14, "Dove",        ChargeStyle.FeatherFan,     ShotStyle.FeatherDart,      Bone, Amber, Sodium, 0f, 0f,   .20f, 8f,  6.0f, 1.10f),
        Make(15, "Turtle",      ChargeStyle.ShellPlates,    ShotStyle.ShellDisc,        Amber,  AmberShade,  Teal,    0f, 420f,  0f,   0f,  5.4f, 0.88f),
    };

    public static int Count => table.Length;

    public static bool Has(int shipId)
    {
        return shipId >= 0 && shipId < table.Length;
    }

    public static WeaponStyle For(int shipId)
    {
        if (!Has(shipId)) shipId = shopingShips.StarterShip;
        return table[shipId];
    }
}
