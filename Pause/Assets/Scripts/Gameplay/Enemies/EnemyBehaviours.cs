using System.Collections.Generic;
using UnityEngine;

// What every roster enemy DOES, in one table (docs/enemy-behaviours.md has
// the reasoning per enemy). A behaviour is a choice of primitives and their
// numbers; EnemyBrain runs any of them (chasers: ChaserEnemy reads theirs).
//
//   lateral   None / Drift / Glide / Sway / Orbit / Track / March
//   vertical  None / Bob / Pulse / Brake / Sink / Patrol / Creep
//   attack    None / Lunge / Shot / Ring / Cross / Lob / Laser / Blast / Strike / Jet / Wave
//             (Blast: AttackBlast's expanding ring with a crack; Strike: AttackStrike's lane columns; Jet: AttackJet's cone or column;
//              Wave: AttackWave's falling band with a gap -- themed area hazards)
//
// Everything a brain adds is an OFFSET in board space on top of the enemy's
// mover, bounded by the behaviour's envelope (bandX either side, Up above,
// Down below). SpawnSpace places enemies by that envelope, so two enemies'
// patterns can never meet.
public enum EnemyLateral { None, Drift, Glide, Sway, Orbit, Track, March }
public enum EnemyVertical { None, Bob, Pulse, Brake, Sink, Patrol, Creep }
public enum EnemyAttack { None, Lunge, Shot, Ring, Cross, Lob, Laser, Blast, Strike, Jet, Wave }
public enum ChaserStyle { Hound, Lancer, Weaver, Burner }

// PRESENCE. A Hazard (rocks, rail mines) rides the board and rushes past. A
// Pilot (fighters, heavies, chasers, aliens) is piloted or alive: it flies
// under its own power in world space -- waits above the view until its column
// is clear, enters, engages on station for a bounded window, and leaves in
// character. The scroll moves the world behind it, not the pilot.
public enum EnemyPresence { Hazard, Pilot }
public enum PilotEntry { Drop, Swoop, Descend }   // straight in / overshoot and rise / no station: marches down the screen
public enum PilotExit { Climb, Peel, Run }        // retreats up / climbs out on an arc / a telegraphed attack run out the bottom

public sealed class EnemyBehaviour
{
    public string key;
    public string signature;          // one line: what makes it this enemy

    // ---- movement ----
    public EnemyLateral lateral;
    public float bandX;               // half-width of the lateral band (u)
    public float lateralSpeed;        // Drift / Glide / Track: u/s
    public float lateralPeriod = 2f;  // Sway / Orbit: seconds a cycle; March: seconds a hop
    public EnemyVertical vertical;
    public float rise;                // Brake / Pulse / Bob / Patrol / Orbit: how far up the board (u)
    public float sink;                // Sink / Creep / Bob / Patrol / Orbit: how far down (u)
    public float verticalPeriod = 2f; // Bob / Pulse / Patrol: seconds a cycle
    public float verticalSpeed = .6f; // Sink / Creep: u/s; Brake: share of the scroll shed while braking

    // ---- rocks' tumble (AsteroidSpin) ----
    public Vector2 spin = new Vector2(15f, 60f);   // degrees/s range
    public float tilt;                // > 0: stays upright, sways this many degrees
    public float tiltPeriod = EnemyRoster.FloatSwayPeriod;

    // ---- hazard sizes (HazardSize; rocks only) ----
    // Tier centres as multiples of the roster's nominal size: small, typical,
    // large. 0 = one size (mines, pilots).
    public float sizeSmall, sizeTypical, sizeLarge;

    // ---- attack ----
    public EnemyAttack attack;
    public float tell = .6f;          // windup seconds (never below EnemyBrain.TellFloorSeconds)
    public float cooldown = 2.5f;     // seconds between attacks
    public float firstDelay = .15f;   // seconds on screen before the first windup
    public int maxVolleys = 1;
    public float armedChance = 1f;    // share of individuals that attack at all
    // Lunge
    public float lungeX;              // 0..1: how much of the band it may cross toward the pilot
    public float lungeDive;           // u down the board
    public float lungeSeconds = .28f;
    // Shot / Ring / Cross / Lob
    public EliteShots.Kind shotKind = EliteShots.Kind.Bolt;
    public int shotCount = 1;
    public float shotSpread;          // degrees between neighbours
    public float shotGap;             // u between parallel muzzles (Bastion's twin prongs)
    public float shotSpeed = 3f;      // u/s relative to the board
    public float shotSize = .2f;      // drawn diameter (u)
    public float aimCone;             // degrees either side of straight down it may aim; 0 = fixed
    public float ride = 1f;           // share of the board's scroll the shot keeps
    public float muzzle = .3f;        // how far below the centre a shot leaves (u)
    public float poolSeconds = 2.5f;  // Lob: how long the pool lingers
    // Blast / Strike (the themed area hazards: Attacks/AttackBlast.cs, AttackStrike.cs). The spec's `world` is filled from the
    // enemy's own world when the attack is armed; `ride` is the behaviour's `ride` for a hazard and 0 for a pilot.
    public BlastSpec blast = BlastSpec.Standard(0);
    public StrikeSpec strike = StrikeSpec.Standard(0);
    public JetSpec jet = JetSpec.Standard(0);     // Jet (Attacks/AttackJet.cs)
    public WaveSpec wave = WaveSpec.Standard(0);  // Wave (Attacks/AttackWave.cs)
    public int strikeLanes = 1;        // Strike: columns in one pattern
    public float laneSpacing = AttackStrike.MinLaneSpacing;

    // ---- presence (pilots: EnemyBrain's engagement script) ----
    public EnemyPresence presence = EnemyPresence.Hazard;
    public PilotEntry entry = PilotEntry.Drop;
    public PilotExit exit = PilotExit.Climb;
    public float stationDepth = 2f;    // station: this far below the top of the view (u)
    public float engageSeconds = 5f;   // upper bound on its stay on station
    public float entrySpeed = 3.2f;    // u/s coming in
    public float exitSpeed = 4.5f;     // u/s climbing out
    public float runSpeed = 8.5f;      // u/s on an attack run (PilotExit.Run)
    public float descendSpeed = 1f;    // PilotEntry.Descend: its own march down the screen (u/s)
    public float lingerSeconds = 5f;   // chasers: orbiting after the chase before they leave

    public bool IsPilot => presence == EnemyPresence.Pilot;

    // ---- chasers (ChaserEnemy) ----
    public ChaserStyle chaser;
    public float chaseSeconds = 3.5f, chaseSpeed = 2.4f, chaseStart = .9f, wanderSpeed = 1.1f, wanderRadius = .7f;

    public bool Shoots => attack == EnemyAttack.Shot || attack == EnemyAttack.Ring ||
                          attack == EnemyAttack.Cross || attack == EnemyAttack.Lob || attack == EnemyAttack.Laser ||
                          attack == EnemyAttack.Blast || attack == EnemyAttack.Strike || attack == EnemyAttack.Jet || attack == EnemyAttack.Wave;
    // An area hazard (AttackHazard): told for at least AttackHazard.MinTellSeconds, never a projectile.
    public bool IsAreaHazard => attack == EnemyAttack.Blast || attack == EnemyAttack.Strike || attack == EnemyAttack.Jet || attack == EnemyAttack.Wave;
    // Shots' worth of the roster budget the volley reserves while it is told (FR7: a blast is 2, a strike 1 a lane).
    // (a jet counts 1.5, rounded up; a wave 2)
    public int ThreatCount => attack == EnemyAttack.Blast || attack == EnemyAttack.Jet || attack == EnemyAttack.Wave ? 2 : (attack == EnemyAttack.Strike ? Mathf.Max(1, strikeLanes) : shotCount);
    public bool Attacks => attack != EnemyAttack.None;

    // The envelope: how far the brain's offset can ever reach.
    public float Up => rise;
    public float Down => sink + (attack == EnemyAttack.Lunge ? lungeDive : 0f);
    public bool Moves => lateral != EnemyLateral.None || vertical != EnemyVertical.None || attack == EnemyAttack.Lunge;

    // The shot's look, as the elite pool wants it (EliteShot.Launch).
    EliteDef style;
    public EliteDef ShotStyle
    {
        get
        {
            if (style != null) return style;
            var def = EnemyRoster.Find(key);
            Color c = EnemyBehaviours.ShotColor(def != null ? def.world : 0);
            // (the shooter's world rides along: ShotSkins picks the shot's drawing by it, EliteShot.Launch)
            int w = def != null ? Mathf.Clamp(def.world, 0, EnemyRoster.WorldKeys.Length - 1) : 0;
            style = new EliteDef
            {
                key = key + "_shot", shotSize = shotSize, shotBounces = 0, lobSeconds = EnemyBehaviours.LobSeconds,
                poolSeconds = poolSeconds, ShotColor = c, ShotCore = Color.Lerp(c, Color.white, .8f),
                world = EnemyRoster.WorldKeys[w], WorldIndex = w,
            };
            return style;
        }
    }

    // ---- fluent builders (the table reads as sentences) ----
    public EnemyBehaviour Drift(float band, float speed) { lateral = EnemyLateral.Drift; bandX = band; lateralSpeed = speed; return this; }
    public EnemyBehaviour Glide(float band, float speed) { lateral = EnemyLateral.Glide; bandX = band; lateralSpeed = speed; return this; }
    public EnemyBehaviour Sway(float band, float period) { lateral = EnemyLateral.Sway; bandX = band; lateralPeriod = period; return this; }
    public EnemyBehaviour Orbit(float radius, float period)
    {
        lateral = EnemyLateral.Orbit; bandX = radius; lateralPeriod = period;
        rise = Mathf.Max(rise, radius); sink = Mathf.Max(sink, radius);
        return this;
    }
    public EnemyBehaviour Track(float band, float speed) { lateral = EnemyLateral.Track; bandX = band; lateralSpeed = speed; return this; }
    public EnemyBehaviour March(float band, float hopSeconds) { lateral = EnemyLateral.March; bandX = band; lateralPeriod = hopSeconds; return this; }
    public EnemyBehaviour Band(float band) { bandX = band; return this; }
    public EnemyBehaviour Bob(float amp, float period) { vertical = EnemyVertical.Bob; rise = amp; sink = amp; verticalPeriod = period; return this; }
    public EnemyBehaviour Pulse(float amp, float period) { vertical = EnemyVertical.Pulse; rise = amp; verticalPeriod = period; return this; }
    public EnemyBehaviour Brake(float up, float shed = .6f) { vertical = EnemyVertical.Brake; rise = up; verticalSpeed = shed; return this; }
    public EnemyBehaviour Sink(float down, float speed) { vertical = EnemyVertical.Sink; sink = down; verticalSpeed = speed; return this; }
    public EnemyBehaviour Patrol(float amp, float period) { vertical = EnemyVertical.Patrol; rise = amp; sink = amp; verticalPeriod = period; return this; }
    public EnemyBehaviour Creep(float down, float speed) { vertical = EnemyVertical.Creep; sink = down; verticalSpeed = speed; return this; }
    public EnemyBehaviour Spin(float lo, float hi) { spin = new Vector2(lo, hi); return this; }
    public EnemyBehaviour Tilt(float degrees, float period) { tilt = degrees; tiltPeriod = period; return this; }
    public EnemyBehaviour Sizes(float small, float typical, float large)
    {
        sizeSmall = small; sizeTypical = typical; sizeLarge = large;
        return this;
    }

    public EnemyBehaviour Timing(float tellSeconds, float cooldownSeconds, int volleys, float first = .15f)
    {
        tell = tellSeconds; cooldown = cooldownSeconds; maxVolleys = volleys; firstDelay = first;
        return this;
    }
    public EnemyBehaviour Lunge(float towardPilot, float dive, float seconds = .28f)
    {
        attack = EnemyAttack.Lunge; lungeX = towardPilot; lungeDive = dive; lungeSeconds = seconds;
        return this;
    }
    public EnemyBehaviour Shot(EliteShots.Kind kind, int count, float spread, float speed, float size, float cone = 0f)
    {
        attack = EnemyAttack.Shot; shotKind = kind; shotCount = count; shotSpread = spread; shotSpeed = speed;
        shotSize = size; aimCone = cone;
        if (kind == EliteShots.Kind.Slag) ride = 0f;   // slag sinks on its own (EliteShot)
        return this;
    }
    public EnemyBehaviour Ring(EliteShots.Kind kind, int count, float speed, float size)
    {
        attack = EnemyAttack.Ring; shotKind = kind; shotCount = count; shotSpeed = speed; shotSize = size;
        return this;
    }
    public EnemyBehaviour Cross(EliteShots.Kind kind, int count, float spread, float speed, float size)
    {
        attack = EnemyAttack.Cross; shotKind = kind; shotCount = count; shotSpread = spread; shotSpeed = speed;
        shotSize = size;
        if (kind == EliteShots.Kind.Slag) ride = 0f;
        return this;
    }
    // A rail mine's laser across the lane (RailMineLaser): one beam a volley.
    public EnemyBehaviour Laser()
    {
        attack = EnemyAttack.Laser; shotCount = 1;
        return this;
    }
    // A ring of bars with a crack, from the muzzle: AttackBlast (BlastSpec.Standard(world) is the Frost cold blast).
    public EnemyBehaviour Blast(BlastSpec spec)
    {
        attack = EnemyAttack.Blast; blast = spec; shotCount = 1; ride = 1f;
        return this;
    }
    public EnemyBehaviour Blast(int bars, float reach, float speed, float gapDeg, float barHalf = .09f)
    {
        var s = BlastSpec.Standard(0);
        s.bars = bars; s.reach = reach; s.speed = speed; s.gapDeg = gapDeg; s.barHalf = barHalf;
        return Blast(s);
    }
    // Lane columns on the pilot's lane and around it: AttackStrike (StrikeSpec.Standard(world) picks the world's style).
    public EnemyBehaviour Strike(StrikeSpec spec, int lanes = 1, float spacing = AttackStrike.MinLaneSpacing)
    {
        attack = EnemyAttack.Strike; strike = spec; strikeLanes = Mathf.Max(1, lanes); laneSpacing = spacing; shotCount = 1; ride = 1f;
        return this;
    }
    // A cone or column out of the muzzle at the locked pilot point: AttackJet (JetSpec.Standard(world) picks the world's: flame, pressure jet, frost ray, lance).
    public EnemyBehaviour Jet(JetSpec spec)
    {
        attack = EnemyAttack.Jet; jet = spec; shotCount = 1; ride = 1f;
        return this;
    }
    // A band across the lane with one gap (aimed at the pilot at the tell), falling: AttackWave (WaveSpec.Standard(world): surf wave, scan line).
    public EnemyBehaviour Wave(WaveSpec spec)
    {
        attack = EnemyAttack.Wave; wave = spec; shotCount = 1; ride = 1f;
        return this;
    }
    public EnemyBehaviour Lob(float size, float pool)
    {
        attack = EnemyAttack.Lob; shotKind = EliteShots.Kind.Glob; shotCount = 1; shotSize = size; poolSeconds = pool;
        return this;
    }
    public EnemyBehaviour Pilot(PilotEntry how, float depth, float seconds, PilotExit leave)
    {
        presence = EnemyPresence.Pilot; entry = how; stationDepth = depth; engageSeconds = seconds; exit = leave;
        return this;
    }
    // A heavy: arrives and leaves slowly.
    public EnemyBehaviour Slow() { entrySpeed = 1.3f; exitSpeed = 1.8f; return this; }
    public EnemyBehaviour Descend(float speed)
    {
        presence = EnemyPresence.Pilot; entry = PilotEntry.Descend; descendSpeed = speed;
        return this;
    }
    public EnemyBehaviour Linger(float seconds) { presence = EnemyPresence.Pilot; lingerSeconds = seconds; return this; }
    public EnemyBehaviour Volleys(int n) { maxVolleys = n; return this; }
    public EnemyBehaviour Twin(float gap) { shotGap = gap; return this; }
    public EnemyBehaviour Muzzle(float below) { muzzle = below; return this; }
    public EnemyBehaviour Armed(float chance) { armedChance = chance; return this; }
    public EnemyBehaviour Chaser(ChaserStyle s, float seconds, float speed, float start, float wander, float radius)
    {
        chaser = s; chaseSeconds = seconds; chaseSpeed = speed; chaseStart = start; wanderSpeed = wander; wanderRadius = radius;
        return this;
    }
}

public static class EnemyBehaviours
{
    public const float LobSeconds = .9f;

    // Space's shots are the elites' magenta-pink: the roster's own magenta
    // (EnemyPalette.Magenta, hue 334) reads as the player's red to
    // HostileGlow.IsPlayerRed, whose band starts at 332.
    public static readonly Color SpaceShot = new Color(1f, .31f, .85f);

    // Each world's hostile shot colour. Never the player's red
    // (HostileGlow.IsPlayerRed; EnemyBehaviourTest): Ember is amber, not the
    // sodium orange, whose hue sits on the edge of the red band.
    public static Color ShotColor(int world)
    {
        switch (world)
        {
            case 1: return EnemyPalette.Cyan;
            case 2: return EnemyPalette.BileLight;
            case 3: return EnemyPalette.Amber;
            default: return SpaceShot;
        }
    }

    static Dictionary<string, EnemyBehaviour> table;

    // Tests: a behaviour served in place of the table's (a fixture for an attack no world uses yet). ClearOverrides() puts the table back.
    static Dictionary<string, EnemyBehaviour> overrides;
    public static void TestOverride(string key, EnemyBehaviour b)
    {
        if (overrides == null) overrides = new Dictionary<string, EnemyBehaviour>();
        if (b == null) overrides.Remove(key); else overrides[key] = b;
    }
    public static void ClearOverrides() { if (overrides != null) overrides.Clear(); }

    public static EnemyBehaviour For(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        EnemyBehaviour over;
        if (overrides != null && overrides.Count > 0 && overrides.TryGetValue(key, out over)) return over;
        if (table == null) table = Build();
        EnemyBehaviour b;
        return table.TryGetValue(key, out b) ? b : null;
    }

    public static EnemyBehaviour For(EnemyDef def) { return def != null ? For(def.key) : null; }

    public static int Count { get { if (table == null) table = Build(); return table.Count; } }

    static Dictionary<string, EnemyBehaviour> building;

    static EnemyBehaviour B(string key, string signature)
    {
        var b = new EnemyBehaviour { key = key, signature = signature };
        building[key] = b;
        return b;
    }

    const EliteShots.Kind Bolt = EliteShots.Kind.Bolt, Shard = EliteShots.Kind.Shard,
                          Slag = EliteShots.Kind.Slag, Shell = EliteShots.Kind.Shell;

    static Dictionary<string, EnemyBehaviour> Build()
    {
        building = new Dictionary<string, EnemyBehaviour>();

        // ================================================================ SPACE
        B("space_rock_crater", "floating beacon rock: slow heave and a lazy sway, upright")
            .Sway(.3f, 5.2f).Bob(.16f, 3.6f).Tilt(EnemyRoster.FloatSwayDegrees, EnemyRoster.FloatSwayPeriod)
            .Sizes(.78f, .98f, 1.32f);
        B("space_rock_cluster", "three welded boulders: heavy tumble, slow bouncing drift")
            .Drift(.55f, .32f).Spin(10f, 28f)
            .Sizes(.8f, .92f, 1.48f);
        B("space_rock_dark", "coal wedge: quick tumble on one slanted line")
            .Glide(.9f, .55f).Spin(45f, 85f)
            .Sizes(.78f, .98f, 1.32f);
        B("space_mine", "rail mine: slides up and down its rail, two lasers across the lane at random angles, riding up beside the ship to fire them")
            .Patrol(.8f, 2.6f).Laser().Muzzle(.5f).Timing(.9f, RailMineLaser.ShotGapSeconds, RailMineLaser.ShotsPerRide, .15f);
        B("space_big", "Bastion: holds its column, twin cannon bolts straight down")
            .Brake(1.2f, .5f).Shot(Bolt, 2, 0f, 2.6f, .2f).Twin(.42f).Muzzle(.5f).Timing(.8f, 2.6f, 3, .15f)
            .Pilot(PilotEntry.Drop, 1.5f, 9f, PilotExit.Climb).Slow().Volleys(3);
        B("space_fighter_1", "Needle: fast narrow weave, then a straight dash")
            .Sway(.45f, .9f).Lunge(0f, 1.5f, .22f).Timing(.45f, 2f, 1, .15f)
            .Pilot(PilotEntry.Swoop, 2.2f, 2.5f, PilotExit.Run).Volleys(0);
        B("space_fighter_2", "Steel Claw: shadows the pilot, pinches, pounces")
            .Track(.55f, .8f).Brake(1.1f, .55f).Lunge(1f, 1.3f).Timing(.55f, 1.6f, 2, .15f)
            .Pilot(PilotEntry.Drop, 2.6f, 5f, PilotExit.Run).Volleys(1);
        B("space_fighter_3", "Twin Claw: wide slow sweep, two splayed bolts")
            .Sway(.7f, 2.6f).Shot(Bolt, 2, 26f, 2.8f, .18f).Timing(.6f, 2.2f, 2, .15f)
            .Pilot(PilotEntry.Drop, 2f, 7f, PilotExit.Peel).Volleys(3);
        B("space_fighter_4", "Warden: hovers, tracks, one heavy aimed shell")
            .Track(.5f, .5f).Brake(1.8f, .65f).Shot(Shell, 1, 0f, 3.4f, .3f, 28f).Muzzle(.4f).Timing(.9f, 2.4f, 2, .15f)
            .Pilot(PilotEntry.Drop, 1.6f, 9f, PilotExit.Climb).Volleys(4);
        B("space_chaser", "Steel Hound: steady pursuit, then orbits")
            .Chaser(ChaserStyle.Hound, 3.5f, 2.4f, .9f, 1.1f, .7f)
            .Linger(5f);
        B("space_alien", "Bile Mite: lockstep wiggle; some spit a shard")
            .Sway(.28f, .8f).Shot(Shard, 1, 0f, 2f, .16f).Armed(.4f).Timing(.55f, 3.5f, 1, .15f)
            .Descend(1.1f).Volleys(2);

        // ================================================================ FROST
        B("frost_rock_shard", "ice splinters: light, skittish bouncing drift")
            .Drift(.95f, .7f).Spin(30f, 70f)
            .Sizes(.76f, 1.02f, 1.24f);
        B("frost_rock_chunk", "frozen bedrock: heavy slow heave, no sideways")
            .Bob(.24f, 4.4f).Tilt(6f, 4.4f)   // (tilt: floating rocks only, <= 15 degrees)
            .Sizes(.8f, .96f, 1.4f);
        B("frost_rock_rime", "rime star: even snowflake spin in a slow circle")
            .Orbit(.45f, 3.4f).Spin(38f, 44f)
            .Sizes(.76f, 1f, 1.3f);
        B("frost_mine", "geode mine: creeps down its rail, two lasers across the lane at random angles, riding up beside the ship to fire them")
            .Creep(1.4f, .7f).Laser().Muzzle(.5f).Timing(.9f, RailMineLaser.ShotGapSeconds, RailMineLaser.ShotsPerRide, .15f);
        B("frost_big", "Glacier Golem: slow sway, a fan of three frost shards")
            .Sway(.3f, 4.5f).Shot(Shard, 3, 22f, 2.4f, .22f).Muzzle(.5f).Timing(.9f, 3.2f, 2, .15f)
            .Pilot(PilotEntry.Drop, 1.6f, 9f, PilotExit.Climb).Slow().Volleys(3);
        B("frost_fighter_1", "Flake: snowflake drift, then a lance dash at the pilot")
            .Sway(.6f, 3f).Lunge(.8f, 1.5f).Timing(.55f, 2f, 1, .15f)
            .Pilot(PilotEntry.Swoop, 2.4f, 3f, PilotExit.Run).Volleys(0);
        B("frost_fighter_2", "Icicle: tracks, one aimed lance bolt")
            .Track(.55f, .9f).Shot(Bolt, 1, 0f, 3.6f, .18f, 30f).Timing(.55f, 2f, 2, .15f)
            .Pilot(PilotEntry.Drop, 2.4f, 5.5f, PilotExit.Peel).Volleys(3);
        B("frost_fighter_3", "Frost Kite: quick loops, a splayed pair of shards")
            .Orbit(.5f, 1.5f).Shot(Shard, 2, 34f, 3f, .18f).Timing(.55f, 2.2f, 2, .15f)
            .Pilot(PilotEntry.Drop, 2.6f, 7f, PilotExit.Run).Volleys(3);
        B("frost_fighter_4", "Hailstorm: hovers, a wide slow hail of five")
            .Brake(1.8f, .65f).Shot(Shard, 5, 24f, 2f, .2f).Muzzle(.35f).Timing(1f, 3.4f, 2, .15f)
            .Pilot(PilotEntry.Drop, 1.5f, 9f, PilotExit.Climb).Volleys(3);
        B("frost_chaser", "Frost Lancer: stops, aims, dashes in a straight line")
            .Chaser(ChaserStyle.Lancer, 3.6f, 2.6f, .8f, 1f, .6f)
            .Linger(4f);
        B("frost_alien", "Cryo Jelly: pulses up and sinks back")
            .Sway(.18f, 2.4f).Pulse(.45f, 1.6f)
            .Descend(.9f);

        // ============================================================== VERDANT
        B("verdant_rock_pod", "thorn pod: slow roll, small slow sway")
            .Sway(.3f, 3.8f).Spin(12f, 30f)
            .Sizes(.78f, .98f, 1.32f);
        B("verdant_rock_spore", "spore rock: each puff lifts it; slow drift")
            .Drift(.4f, .22f).Pulse(.3f, 2.2f).Tilt(EnemyRoster.FloatSwayDegrees, EnemyRoster.FloatSwayPeriod)
            .Sizes(.8f, .96f, 1.4f);
        B("verdant_rock_knot", "bramble knot: fast spin, rolls across a wide band")
            .Drift(1.1f, .6f).Spin(90f, 140f)
            .Sizes(.76f, 1f, 1.3f);
        B("verdant_rock_vine", "vine rock: swings like a pendulum")
            .Sway(.55f, 2.6f).Tilt(14f, 2.6f)
            .Sizes(.78f, .98f, 1.32f);
        B("verdant_mine", "burr mine: swings on its rail, two lasers across the lane at random angles, riding up beside the ship to fire them")
            .Patrol(.7f, 1.9f).Laser().Muzzle(.5f).Timing(1f, RailMineLaser.ShotGapSeconds, RailMineLaser.ShotsPerRide, .15f);
        B("verdant_big", "Bloom Maw: lobs a resin glob onto the pilot's spot")
            .Lob(.3f, 2.5f).Muzzle(.2f).Timing(.9f, 3.5f, 2, .15f)
            .Pilot(PilotEntry.Drop, 1.4f, 9f, PilotExit.Climb).Slow().Volleys(3);
        B("verdant_fighter_1", "Gnat: fast jittery weave")
            .Sway(.5f, .55f).Bob(.14f, .7f)
            .Pilot(PilotEntry.Swoop, 2.6f, 3f, PilotExit.Run);
        B("verdant_fighter_2", "Wasp: tracks, then the deepest dive in the roster")
            .Track(.6f, 1f).Brake(1.2f, .55f).Lunge(1f, 2f, .24f).Timing(.5f, 2f, 1, .15f)
            .Pilot(PilotEntry.Drop, 2.4f, 5f, PilotExit.Run).Volleys(1);
        B("verdant_fighter_3", "Mantis: hovers still, slashes sideways across its band")
            .Band(.7f).Brake(1.6f, .65f).Lunge(1f, 0f, .2f).Timing(.6f, 1.3f, 3, .15f)
            .Pilot(PilotEntry.Drop, 3f, 6.5f, PilotExit.Peel).Volleys(3);
        B("verdant_fighter_4", "Hornet Queen: hovers, tracks, tight fans of three stingers")
            .Track(.5f, .6f).Brake(1.8f, .65f).Shot(Bolt, 3, 12f, 3.2f, .16f, 26f).Muzzle(.4f).Timing(.8f, 2.6f, 3, .15f)
            .Pilot(PilotEntry.Drop, 1.6f, 9f, PilotExit.Climb).Volleys(4);
        B("verdant_chaser", "Dragonsting: weaving pursuit, longer and slower")
            .Chaser(ChaserStyle.Weaver, 4.6f, 2f, .9f, 1.2f, .8f)
            .Linger(6f);
        B("verdant_alien", "Snap Sprout: marches sideways in step")
            .March(.5f, .55f)
            .Descend(1f);

        // ================================================================ EMBER
        B("ember_rock_magma", "magma rock: slow tumble, slow drift, breathing bob")
            .Drift(.4f, .28f).Bob(.08f, 2.4f).Spin(12f, 30f)
            .Sizes(.8f, .96f, 1.4f);
        B("ember_rock_cinder", "cinder chunk: steady spin, sinks down the board")
            .Sink(1.1f, .4f).Spin(20f, 24f)
            .Sizes(.8f, .96f, 1.4f);
        B("ember_rock_obsidian", "obsidian blade: barely turns, one fast slanted slice")
            .Glide(1.2f, .9f).Spin(5f, 9f)
            .Sizes(.76f, 1.02f, 1.24f);
        B("ember_rock_islet", "lava islet: wide slow sway and bob")
            .Sway(.7f, 4.2f).Bob(.14f, 3f).Tilt(EnemyRoster.FloatSwayDegrees, EnemyRoster.FloatSwayPeriod)
            .Sizes(.8f, .92f, 1.48f);
        B("ember_mine", "crucible mine: boils over, two lasers across the lane at random angles, riding up beside the ship to fire them")
            .Laser().Muzzle(.5f).Timing(1.1f, RailMineLaser.ShotGapSeconds, RailMineLaser.ShotsPerRide, .15f);
        B("ember_big", "Magma Skull: jaw drops, two slag blobs angled out")
            .Shot(Slag, 2, 56f, 1.5f, .32f).Muzzle(.45f).Timing(1f, 3.6f, 2, .15f)
            .Pilot(PilotEntry.Drop, 1.5f, 9f, PilotExit.Climb).Slow().Volleys(3);
        B("ember_fighter_1", "Cinder: diagonal drift, then a straight dash")
            .Drift(.5f, .7f).Lunge(0f, 1.7f, .22f).Timing(.45f, 2f, 1, .15f)
            .Pilot(PilotEntry.Swoop, 2.4f, 2.5f, PilotExit.Run).Volleys(0);
        B("ember_fighter_2", "Scorch: lines up over the pilot, quick bolts straight down")
            .Track(.6f, .9f).Shot(Bolt, 1, 0f, 3.8f, .18f).Timing(.5f, 1.4f, 3, .15f)
            .Pilot(PilotEntry.Drop, 2.2f, 5.5f, PilotExit.Peel).Volleys(4);
        B("ember_fighter_3", "Brand: fast strafing run, one aimed bolt")
            .Drift(.75f, 1.5f).Shot(Bolt, 1, 0f, 3.4f, .18f, 34f).Timing(.5f, 1.8f, 2, .15f)
            .Pilot(PilotEntry.Drop, 2.4f, 7f, PilotExit.Run).Volleys(3);
        B("ember_fighter_4", "Pyre: hovers, a full ring of eight")
            .Track(.5f, .4f).Brake(1.8f, .65f).Ring(Bolt, 8, 2.2f, .18f).Muzzle(0f).Timing(1.1f, 3.8f, 2, .15f)
            .Pilot(PilotEntry.Drop, 1.5f, 9f, PilotExit.Climb).Volleys(3);
        B("ember_chaser", "Cinder Fang: short hard chase, wide burnt-out wander")
            .Chaser(ChaserStyle.Burner, 2.5f, 3f, 1.4f, .8f, 1.1f)
            .Linger(7f);
        B("ember_alien", "Ember Imp: flickers, quick small pulses")
            .Sway(.22f, 1.1f).Pulse(.2f, .85f)
            .Descend(1.3f);

        var built = building;
        building = null;
        return built;
    }
}
