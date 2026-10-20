using UnityEngine;

// What every ship fights with: its main attack and its secret power.
//
// The main attack fires itself when the ultimate's charge meter fills
// (ShipPowerController, the ChargeIndicator in front of the hull). Only the
// top price tier -- the three most expensive hulls in shopingShips.Prices --
// gets a cinematic attack (ShipPowerController) and each of the three has its own verb (a sweeping
// laser, a frost nova, deployed sentries); every cheaper ship has a
// directional or limited weapon that plays at normal speed
// (ShipAttackRunner).
//
// The secret power has its own, slower meter (SecretPowerController, the
// SecretMeter badge beside the hull) filled by star dust and kills. When it
// is full it waits for its trigger -- the moment that power is useful -- and
// fires itself. No new controls.
//
// One row per ShipId; the tests check every ship has one, that no two ships
// share an attack or an attack + power pair, and that the cinematic attacks
// (ShipLoadoutTable.IsCinematic) are exactly the top tier by price.
public enum ShipAttack
{
    CometStreak,    // two comet streaks straight ahead, each stops at the first hit
    ChainLightning, // a bolt that jumps between nearby enemies
    SkyJavelins,    // three rockets fly up from the nose, each opens into a javelin that strikes down its lane
    Blowtorch,      // a short-range flame cone held in front of the ship
    ArcaneEye,      // an eye gazes up its cone and marks targets; the marks detonate 0.8 s later
    HeatWave,       // a thin wall of heat rolls up the whole screen from the nose, burning what it passes
    BoomerangStar,  // a shuriken thrown on an ellipse that hits out and back to the ship
    ProximityMines, // mines launched ahead hold their screen position and blow when something comes close
    AbductHurl,     // a beam lifts the nearest enemy, then hurls it at the densest group
    RailSlug,       // a piercing slug that hits everything in a narrow line
    GaleWings,      // a feathered gust shoves everything up and outward; collisions hurt
    OrbitDisc,      // a disc that orbits the ship and smashes what it touches
    Nuke,           // premium: a warhead climbs from the nose, blinks, detonates; a thin shockwave kills everything it reaches
    IonLaser,       // top tier (cinematic): a thin laser leaves the nose and sweeps the screen, damaging everything it crosses
    FrostNova,      // top tier (cinematic): a frost ring grows over the screen, freezing all it reaches; frozen things shatter
    WardenSentries, // top tier (cinematic): sentry pods deploy across the lower screen and shoot the nearest targets
}

public enum SecretPower
{
    ShieldPulse,    // pops what is about to hit you, brief invulnerability
    EmpStun,        // freezes every enemy on screen and cancels their shots
    SolarNova,      // a flare that burns away everything close by, then a burning ring
    CapacitorDump,  // the main attack fires at once
    IonBattery,     // +2 pauses
    PhaseCloak,     // a few seconds of ghosting: invulnerable, passes through hazards
    GoldMagnet,     // pulls star dust in from across the screen, worth double meanwhile
    Thunderclap,    // lightning strikes everything close dead
    FlareDecoy,     // a flare that chasers and shooters aim at instead of you, then pops
    TimeBubble,     // a visible dome around the hull
    BlinkDash,      // sidesteps to the safer side, at a hit or a close threat
    BlackHole,      // a little black hole ahead swallows what drifts in
    TractorPull,    // drags nearby enemies and debris in; kills drop star dust
    Mending,        // repairs one point of hull damage
    HardShell,      // a shell that eats the next hit
    AegisShield,    // premium: a timed shield, raised on its own every few seconds
}

// When a full secret meter lets go.
public enum PowerTrigger
{
    ImminentHit,          // a hazard is about to touch the hull
    EnemiesOnScreen,      // at least `count` enemies on screen
    HazardsNearShip,      // at least `count` hazards within `radius`
    TargetsOnScreen,      // at least `count` hazards on screen
    LowPauses,            // `count` pauses or fewer left
    PickupsOnScreen,      // at least `count` star dust pickups on screen
    EnemyClosing,         // a chaser is hunting you, or `count` enemies within `radius`
    ScreenCalm,           // `count` hazards or fewer on screen
    ThreatClose,          // a hit is imminent or a hazard is within `radius`
    HullDamaged,          // the hull has taken a hit
    HazardInLane,         // something is in your lane within `radius` ahead
    Always,               // as soon as the meter is full (a power whose meter is a timer)
}

public struct ShipLoadout
{
    public int shipId;
    public ShipAttack attack;
    public SecretPower power;
    public PowerTrigger trigger;
    public int triggerCount;
    public float triggerRadius;

    public string attackName, attackLine;
    public string powerName, powerLine;

    // Attack tuning. Meaning depends on the attack (see ShipAttackRunner):
    // shots -- projectiles / javelins / marks / mines / waves / stars; maxHits --
    // chain hops, bodies a hurled enemy pierces; duration -- held cones and the
    // orbit, an eye's gaze, the gap between heat waves; range -- reach in world
    // units; width -- lane half-width / cone half-angle / orbit radius / a heat
    // wave's thickness / a gust's half-angle; speed -- projectile speed (world
    // units per second), a wave's climb, a star's speed.
    public int shots;
    public int maxHits;
    public float duration;
    public float range;
    public float width;
    public float speed;

    // The top price tier flies a cinematic attack (the world is never slowed).
    public bool IsTopTier => ShipLoadoutTable.IsCinematic(attack);
}

public static class ShipLoadoutTable
{
    // How many of the priciest hulls get a cinematic attack.
    public const int TopTierCount = 3;

    // The cinematic attacks: exactly the top price tier's.
    public static bool IsCinematic(ShipAttack a)
    {
        return a == ShipAttack.IonLaser || a == ShipAttack.FrostNova || a == ShipAttack.WardenSentries;
    }

    static ShipLoadout Row(int id, ShipAttack attack, string attackName, string attackLine,
                           SecretPower power, string powerName, string powerLine,
                           PowerTrigger trigger, int count, float radius,
                           int shots = 1, int maxHits = 1, float duration = 0f,
                           float range = 0f, float width = 0f, float speed = 0f)
    {
        return new ShipLoadout
        {
            shipId = id, attack = attack, attackName = attackName, attackLine = attackLine,
            power = power, powerName = powerName, powerLine = powerLine,
            trigger = trigger, triggerCount = count, triggerRadius = radius,
            shots = shots, maxHits = maxHits, duration = duration, range = range, width = width, speed = speed,
        };
    }

    // Indexed by ShipId. Index 0 (ShipId.None) borrows the starter's row so
    // a stray id still flies something.
    static readonly ShipLoadout[] byShip =
    {
        default(ShipLoadout), // 0: filled from the starter below
        // 1 Neon Comet (starter, free)
        Row(1, ShipAttack.CometStreak, "COMET STREAK", "Two comet streaks straight ahead.",
            SecretPower.ShieldPulse, "SHIELD PULSE", "Just before a hit: 1.2 s invulnerable, and a ring deletes enemy shots.",
            PowerTrigger.ImminentHit, 1, 0f,
            shots: 2, range: 11f, width: .3f, speed: 13f),
        // 2 Volt Viper (600)
        Row(2, ShipAttack.ChainLightning, "CHAIN LIGHTNING", "A bolt that jumps between enemies.",
            SecretPower.EmpStun, "EMP", "Freezes every enemy and cancels their shots, arcing through each.",
            PowerTrigger.EnemiesOnScreen, 3, 0f,
            maxHits: 5, range: 4.6f, width: 2.5f),
        // 3 Solar Fang (1400)
        Row(3, ShipAttack.HeatWave, "HEAT WAVE", "A wall of heat rolls up the screen, burning everything it passes.",
            SecretPower.SolarNova, "SOLAR NOVA", "Burns away everything close by and leaves a burning ring for 2 s.",
            PowerTrigger.HazardsNearShip, 3, 2.3f,
            duration: .4f, width: .7f, speed: 5f),
        // 4 Crimson Halo (2200)
        Row(4, ShipAttack.RailSlug, "RAIL GUN", "Pierces everything in a straight line.",
            SecretPower.CapacitorDump, "CAPACITOR DUMP", "Fires the rail gun at once.",
            PowerTrigger.TargetsOnScreen, 3, 0f,
            range: 11f, width: .42f),
        // 5 Ion Lancer (3200, top tier)
        Row(5, ShipAttack.IonLaser, "ION LASER", "A thin laser leaves the nose and sweeps the whole screen, burning everything it crosses.",
            SecretPower.IonBattery, "ION BATTERY", "Adds two pauses when you run low.",
            PowerTrigger.LowPauses, 2, 0f,
            shots: 1, duration: 1.2f, range: 16f, width: 70f),
        // 6 Jade Phantom (4400, top tier)
        Row(6, ShipAttack.FrostNova, "FROST NOVA", "A frost ring sweeps the screen. Everything it reaches freezes solid, then shatters.",
            SecretPower.PhaseCloak, "PHASE CLOAK", "Ghosts the ship for 4 s: untouchable, passes through hazards.",
            PowerTrigger.ImminentHit, 1, 0f,
            duration: 1.0f, width: 0f),
        // 7 Gold Warden (5800, top tier)
        Row(7, ShipAttack.WardenSentries, "WARDEN SENTRIES", "Three sentry pods deploy across the screen and shoot the nearest targets for 6 s.",
            SecretPower.GoldMagnet, "GOLD MAGNET", "Pulls in star dust for 5 s, worth double meanwhile.",
            PowerTrigger.PickupsOnScreen, 4, 0f,
            shots: 3, duration: 6f, range: 6f, speed: 3f),
        // 8 Lightning (800)
        Row(8, ShipAttack.SkyJavelins, "SKY JAVELINS", "Three rockets climb to the top, then javelins strike down three lanes.",
            SecretPower.Thunderclap, "THUNDERCLAP", "Lightning strikes everything within 2.6 dead, bosses take a hit.",
            PowerTrigger.HazardsNearShip, 2, 2.0f,
            shots: 3, width: .32f, range: 1.2f, speed: 26f),
        // 9 Ligher (1000)
        Row(9, ShipAttack.Blowtorch, "BLOWTORCH", "A short flame cone in front.",
            SecretPower.FlareDecoy, "FLARE DECOY", "A flare that hunters chase and shooters aim at for 4 s, then it pops.",
            PowerTrigger.EnemyClosing, 2, 2.4f,
            duration: 1.4f, range: 2.7f, width: 24f),
        // 10 Paranoid (1200)
        Row(10, ShipAttack.ArcaneEye, "ARCANE EYE", "An eye marks four targets ahead; the marks detonate.",
            SecretPower.TimeBubble, "TIME BUBBLE", "A dome shoves hazards out and blocks bullets for 2.5 s when it crowds you.",
            PowerTrigger.HazardsNearShip, 3, 2.6f,
            shots: 4, duration: .6f, range: 9f, width: 40f),
        // 11 Ninja (1600)
        Row(11, ShipAttack.BoomerangStar, "BOOMERANG STAR", "A shuriken arcs out and curves back, cutting both ways.",
            SecretPower.BlinkDash, "BLINK DASH", "Dodges to the safer side at the last moment, or when a threat gets within 2.",
            PowerTrigger.ThreatClose, 1, 2f,
            range: 6f, speed: 12f),
        // 12 Saboteur (1800)
        Row(12, ShipAttack.ProximityMines, "PROXIMITY MINES", "Four mines land ahead and blow when danger comes close.",
            SecretPower.BlackHole, "BLACK HOLE", "Swallows what drifts in once 3 enemies are on screen.",
            PowerTrigger.EnemiesOnScreen, 3, 0f,
            shots: 4),
        // 13 UFO (2000)
        Row(13, ShipAttack.AbductHurl, "ABDUCT & HURL", "Lifts the nearest enemy and hurls it into the crowd.",
            SecretPower.TractorPull, "TRACTOR PULL", "Pulls nearby enemies and debris in for 1.8 s, and kills drop star dust.",
            PowerTrigger.HazardsNearShip, 2, 3.5f,
            maxHits: 1, range: 9f, speed: 15f),
        // 14 Dove (2400)
        Row(14, ShipAttack.GaleWings, "GALE WINGS", "A gust shoves enemies up and outward; collisions hurt.",
            SecretPower.Mending, "MENDING", "Repairs one heart in a burst of feathers as soon as the hull is damaged.",
            PowerTrigger.HullDamaged, 1, 0f,
            width: 60f, range: 6f),
        // 15 Turtle (3000)
        Row(15, ShipAttack.OrbitDisc, "SHELL DISC", "A disc orbits you, smashing hazards.",
            SecretPower.HardShell, "HARD SHELL", "A shell shows on the hull for up to 8 s and shatters on the next hit.",
            PowerTrigger.HazardInLane, 1, 3.4f,
            duration: 5f, width: 1.05f, range: .42f, speed: 320f),
        // 16 Paragon (premium, real-money IAP: PremiumShips). Its signature extras (timed mini-warheads
        // and a shield, both on timers) are PremiumShips tiers; this row is its ultimate and its timed
        // shield power. range: the nuke's flight height (share of the screen); duration: its fuse.
        Row(16, ShipAttack.Nuke, "NUKE", "A warhead climbs, blinks, then detonates: a red shockwave clears the screen.",
            SecretPower.AegisShield, "AEGIS SHIELD", "Raises the shield on its own every 8 s (quicker with every skin).",
            PowerTrigger.Always, 0, 0f,
            duration: .6f, range: .55f, speed: 14f),
    };

    public static int Count => byShip.Length;

    public static bool Has(int shipId) => ShipId.IsValid(shipId) && shipId < byShip.Length;

    public static ShipLoadout For(int shipId)
    {
        if (!Has(shipId)) shipId = ShipId.Starter;
        return byShip[shipId];
    }

    public static ShipAttack AttackFor(int shipId) => For(shipId).attack;
    public static SecretPower PowerFor(int shipId) => For(shipId).power;

    // The ships that get the cinematic attack, by price: rank every
    // roster hull by shopingShips.Prices (ties broken by id) and take the
    // top TopTierCount. The table above must agree (tests).
    public static bool IsTopTierByPrice(int shipId)
    {
        // premium ships are outside the dust economy and its price tiers
        if (!ShipId.IsValid(shipId) || PremiumShips.IsPremium(shipId)) return false;
        float price = shopingShips.CostFor(shipId);
        int above = 0;
        foreach (int other in ShipId.All)
        {
            if (other == shipId || PremiumShips.IsPremium(other)) continue;
            float p = shopingShips.CostFor(other);
            if (p > price || (Mathf.Approximately(p, price) && other > shipId)) above++;
        }
        return above < TopTierCount;
    }

    // One-line summaries for the codex and the dock.
    public static string Summary(int shipId)
    {
        var l = For(shipId);
        return l.attackName + "  +  " + l.powerName;
    }

    public static string TriggerLine(ShipLoadout l)
    {
        switch (l.trigger)
        {
            case PowerTrigger.ImminentHit:          return "just before a hit";
            case PowerTrigger.EnemiesOnScreen:      return l.triggerCount + "+ enemies on screen";
            case PowerTrigger.HazardsNearShip:      return l.triggerCount + "+ hazards close by";
            case PowerTrigger.TargetsOnScreen:      return l.triggerCount + "+ targets on screen";
            case PowerTrigger.LowPauses:            return "down to " + l.triggerCount + (l.triggerCount == 1 ? " pause" : " pauses");
            case PowerTrigger.PickupsOnScreen:      return l.triggerCount + "+ star dust in sight";
            case PowerTrigger.EnemyClosing:         return "an enemy closing in";
            case PowerTrigger.ScreenCalm:           return "a calm screen";
            case PowerTrigger.ThreatClose:          return "a hit or a threat within " + l.triggerRadius.ToString("0.#");
            case PowerTrigger.HullDamaged:          return "a damaged hull";
            case PowerTrigger.HazardInLane:         return "danger in your lane";
            case PowerTrigger.Always:               return "on its own, on a timer";
            default:                                return "";
        }
    }

    static ShipLoadoutTable()
    {
        byShip[0] = byShip[ShipId.Starter];
    }
}
