using UnityEngine;

// What every ship fights with: its main attack and its secret power.
//
// The main attack fires itself when the ultimate's charge meter fills
// (ShipPowerController, the ChargeIndicator in front of the hull). Only the
// top price tier -- the three most expensive hulls in shopingShips.Prices --
// gets the cinematic auto-target-everything volley with its slow motion;
// every cheaper ship has a directional or limited weapon that plays at
// normal speed (ShipAttackRunner).
//
// The secret power has its own, slower meter (SecretPowerController, the
// SecretMeter badge beside the hull) filled by star dust and kills. When it
// is full it waits for its trigger -- the moment that power is useful -- and
// fires itself. No new controls.
//
// One row per ShipId; the tests check every ship has one, that no two ships
// share an attack + power pair, and that ScreenClear is exactly the top tier
// by price.
public enum ShipAttack
{
    CometStreak,    // two comet streaks straight ahead, each stops at the first hit
    ChainLightning, // a bolt that jumps between nearby enemies
    SkyJavelins,    // javelins strike three lanes straight down from the top
    Blowtorch,      // a short-range flame cone held in front of the ship
    SeekerEyes,     // a few eyes that home on the nearest targets
    SolarFireball,  // a slow fireball that bursts over an area
    Ricochet,       // a shuriken that bounces from enemy to enemy
    Gatling,        // a burst of rapid shells in a tight forward cone
    TractorBeam,    // a forward beam held for a moment, sweeping with the ship
    RailSlug,       // a piercing slug that hits everything in a narrow line
    FeatherFan,     // a fan of darts in a forward spread
    OrbitDisc,      // a disc that orbits the ship and smashes what it touches
    ScreenClear,    // top tier only: the cinematic homing volley at everything
}

public enum SecretPower
{
    ShieldPulse,    // pops what is about to hit you, brief invulnerability
    EmpStun,        // freezes every enemy on screen for a few seconds
    SolarNova,      // a flare that burns away everything close by
    CapacitorDump,  // the main attack recharges instantly
    IonBattery,     // +2 pauses
    PhaseCloak,     // a few seconds of real invulnerability
    GoldMagnet,     // pulls star dust in from across the screen
    Thunderclap,    // shoves nearby hazards back up the screen
    FlareDecoy,     // a flare that chasers hunt instead of you, then pops
    TimeBubble,     // the world slows down around you
    BlinkDash,      // sidesteps out of the way of a hit
    BlackHole,      // a little black hole ahead swallows what drifts in
    StarShower,     // rains a ring of star dust ahead while it's calm
    Mending,        // repairs one point of hull damage
    HardShell,      // a shell that eats the next hit
}

// When a full secret meter lets go.
public enum PowerTrigger
{
    ImminentHit,          // a hazard is about to touch the hull
    EnemiesOnScreen,      // at least `count` enemies on screen
    HazardsNearShip,      // at least `count` hazards within `radius`
    TargetsWhileCharging, // `count` targets on screen and the attack is still far off
    LowPauses,            // `count` pauses or fewer left
    PickupsOnScreen,      // at least `count` star dust pickups on screen
    EnemyClosing,         // a chaser is hunting you, or `count` enemies within `radius`
    ScreenCalm,           // `count` hazards or fewer on screen
    HullDamaged,          // the hull has taken a hit
    HazardInLane,         // something is in your lane within `radius` ahead
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
    // shots -- projectiles / javelins / darts / eyes; maxHits -- chain and
    // ricochet hops; duration -- held beams, cones and the orbit; range --
    // reach in world units; width -- lane half-width / cone half-angle /
    // orbit radius; speed -- projectile speed (world units per second).
    public int shots;
    public int maxHits;
    public float duration;
    public float range;
    public float width;
    public float speed;

    public bool IsTopTier => attack == ShipAttack.ScreenClear;
}

public static class ShipLoadoutTable
{
    // How many of the priciest hulls get the screen-clearing volley.
    public const int TopTierCount = 3;

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
            SecretPower.ShieldPulse, "SHIELD PULSE", "Pops whatever is about to hit you.",
            PowerTrigger.ImminentHit, 1, 0f,
            shots: 2, range: 11f, width: .3f, speed: 13f),
        // 2 Volt Viper (600)
        Row(2, ShipAttack.ChainLightning, "CHAIN LIGHTNING", "A bolt that jumps between enemies.",
            SecretPower.EmpStun, "EMP", "Freezes every enemy on screen.",
            PowerTrigger.EnemiesOnScreen, 5, 0f,
            maxHits: 5, range: 4.6f, width: 2.5f),
        // 3 Solar Fang (1400)
        Row(3, ShipAttack.SolarFireball, "SOLAR FIREBALL", "A slow fireball that bursts wide.",
            SecretPower.SolarNova, "SOLAR NOVA", "Burns away everything close by.",
            PowerTrigger.HazardsNearShip, 3, 2.3f,
            range: 3.6f, width: 1.7f, speed: 4.6f),
        // 4 Crimson Halo (2200)
        Row(4, ShipAttack.RailSlug, "RAIL GUN", "Pierces everything in a straight line.",
            SecretPower.CapacitorDump, "CAPACITOR DUMP", "Recharges the rail gun instantly.",
            PowerTrigger.TargetsWhileCharging, 3, 0f,
            range: 11f, width: .42f),
        // 5 Ion Lancer (3200, top tier)
        Row(5, ShipAttack.ScreenClear, "ION BARRAGE", "Ion lances lock on to everything.",
            SecretPower.IonBattery, "ION BATTERY", "Adds two pauses when you run low.",
            PowerTrigger.LowPauses, 1, 0f),
        // 6 Jade Phantom (4400, top tier)
        Row(6, ShipAttack.ScreenClear, "WISP STORM", "Plasma wisps hunt everything on screen.",
            SecretPower.PhaseCloak, "PHASE CLOAK", "Phases out just before a hit.",
            PowerTrigger.ImminentHit, 1, 0f),
        // 7 Gold Warden (5800, top tier)
        Row(7, ShipAttack.ScreenClear, "WARDEN SALVO", "Missiles lock on to everything.",
            SecretPower.GoldMagnet, "GOLD MAGNET", "Pulls in star dust from afar.",
            PowerTrigger.PickupsOnScreen, 4, 0f),
        // 8 Lightning (800)
        Row(8, ShipAttack.SkyJavelins, "SKY JAVELINS", "Javelins strike three lanes from above.",
            SecretPower.Thunderclap, "THUNDERCLAP", "Shoves nearby hazards back.",
            PowerTrigger.HazardsNearShip, 2, 2.0f,
            shots: 3, width: .32f, range: 1.2f, speed: 26f),
        // 9 Ligher (1000)
        Row(9, ShipAttack.Blowtorch, "BLOWTORCH", "A short flame cone in front.",
            SecretPower.FlareDecoy, "FLARE DECOY", "A flare hunters chase, then it pops.",
            PowerTrigger.EnemyClosing, 2, 2.4f,
            duration: 1.4f, range: 2.7f, width: 24f),
        // 10 Paranoid (1200)
        Row(10, ShipAttack.SeekerEyes, "SEEKER EYES", "Three eyes hunt the nearest targets.",
            SecretPower.TimeBubble, "TIME BUBBLE", "Slows the world when it crowds you.",
            PowerTrigger.HazardsNearShip, 3, 2.6f,
            shots: 3, speed: 7.5f),
        // 11 Ninja (1600)
        Row(11, ShipAttack.Ricochet, "RICOCHET STAR", "A shuriken that bounces between foes.",
            SecretPower.BlinkDash, "BLINK DASH", "Sidesteps a hit at the last moment.",
            PowerTrigger.ImminentHit, 1, 0f,
            maxHits: 5, range: 3.1f, speed: 11f),
        // 12 Saboteur (1800)
        Row(12, ShipAttack.Gatling, "GATLING", "A burst of rapid shells up front.",
            SecretPower.BlackHole, "BLACK HOLE", "Swallows a crowded screen.",
            PowerTrigger.EnemiesOnScreen, 6, 0f,
            shots: 14, duration: 1.1f, width: 7f, range: 11f, speed: 16f),
        // 13 UFO (2000)
        Row(13, ShipAttack.TractorBeam, "TRACTOR BEAM", "A beam held straight ahead.",
            SecretPower.StarShower, "STAR SHOWER", "Rains star dust while it's calm.",
            PowerTrigger.ScreenCalm, 1, 0f,
            duration: 1.5f, range: 11f, width: .38f),
        // 14 Dove (2400)
        Row(14, ShipAttack.FeatherFan, "FEATHER FAN", "Five darts in a forward spread.",
            SecretPower.Mending, "MENDING", "Repairs one hull point.",
            PowerTrigger.HullDamaged, 1, 0f,
            shots: 5, width: 56f, range: 11f, speed: 12f),
        // 15 Turtle (3000)
        Row(15, ShipAttack.OrbitDisc, "SHELL DISC", "A disc orbits you, smashing hazards.",
            SecretPower.HardShell, "HARD SHELL", "A shell that eats the next hit.",
            PowerTrigger.HazardInLane, 1, 3.4f,
            duration: 5f, width: 1.05f, range: .42f, speed: 320f),
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

    // The ships that get the screen-clearing volley, by price: rank every
    // roster hull by shopingShips.Prices (ties broken by id) and take the
    // top TopTierCount. The table above must agree (tests).
    public static bool IsTopTierByPrice(int shipId)
    {
        if (!ShipId.IsValid(shipId)) return false;
        float price = shopingShips.CostFor(shipId);
        int above = 0;
        foreach (int other in ShipId.All)
        {
            if (other == shipId) continue;
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
            case PowerTrigger.TargetsWhileCharging: return "a busy screen while recharging";
            case PowerTrigger.LowPauses:            return "down to " + l.triggerCount + " pause";
            case PowerTrigger.PickupsOnScreen:      return l.triggerCount + "+ star dust in sight";
            case PowerTrigger.EnemyClosing:         return "an enemy closing in";
            case PowerTrigger.ScreenCalm:           return "a calm screen";
            case PowerTrigger.HullDamaged:          return "a damaged hull";
            case PowerTrigger.HazardInLane:         return "danger in your lane";
            default:                                return "";
        }
    }

    static ShipLoadoutTable()
    {
        byShip[0] = byShip[ShipId.Starter];
    }
}
