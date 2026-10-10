using UnityEngine;

// The four end-of-level bosses, one per world (index-aligned with
// WorldManager.Worlds), and their attack patterns.
//
// Every attack comes OUT OF THE BOSS: it names the body parts it fires from
// (BossEmitters; the muzzle pixels are measured from the art, per drawing),
// so a shot leaves the Archon's chin cannon, a laser grows out of the
// Leviathan's eyes, spores burst from the Bloom Queen's petal tips. And
// every attack has a tell drawn on that same part: the boss holds its tell
// pose (tell0/1/2, one per attack, each lighting the part that is about to
// fire) while a charge gathers at the muzzle -- and for a laser, a thin
// sight line scans the ground it is about to cover -- and only then fires.
//
// Projectiles meet the side rails in one of three ways (BossRailMode):
// ricochet off them (a limited number of times, with a spark, then they
// splash on the next one), splash against them at once, or fly on past.
// Lasers stop where they meet a rail, sparking.
//
// Patterns unlock in thirds of the fight: attacks[0] alone, then [0] and
// [1] in turn, then all three with shorter cooldowns
// (BossConfig.FinalPhaseCooldownScale; later loops: LoopRules).
public enum BossAttackKind
{
    Aimed,  // volleys at the ship from the attack's part(s)
    Fan,    // even fans out of the part(s), turning a little between volleys
    Lob,    // arcs up out of the part and rains down on chosen columns
    Beam,   // a laser that grows out of the part(s), then sweeps
    // The themed area hazards (docs/world-attacks-design.md). They have no projectiles: the attack arms pooled
    // hazards (Gameplay/Enemies/Attacks) during its tell and the boss waits for them to end (BossExecutors).
    // New kinds are APPENDED here and registered in BossExecutors; nothing else switches on them.
    Jet,    // a flame cone / water column / lance pulse out of the part(s): AttackJet
    Wave,   // a band falling down the lane with a gap: AttackWave (surf, scan line)
    Blast,  // an expanding ring with a crack from the part: AttackBlast (cold blast)
    Strike, // telegraphed lane columns at the ship's row: AttackStrike (icicle drop, eruption, thunder)
    Lash,   // a whip swept across the ship's place from a part: AttackLash (the Bloom Queen's vine lash)
    Roll,   // trunks lobbed onto marked spots that land and roll with one rail bounce: AttackLog (the Bloom Queen's trunk toss)
}

public enum BossShotStyle { Bolt, Shard }

// What a projectile does at a side rail.
public enum BossRailMode
{
    Absorb,  // splashes against the rail (a spark) and is gone
    Bounce,  // ricochets `bounces` times (a spark each), then splashes
    Pass,    // flies on past the rail and off the screen
}

// Where a laser points when its tell starts.
public enum BossBeamAim
{
    Down,    // straight down, turned aimDeg outward (away from the boss's middle)
    AtShip,  // at the ship's position when the tell starts
}

public sealed class BossAttack
{
    public string name;             // what it is, for previews and tests
    public BossAttackKind kind;
    public int tell;                // which drawn tell pose (0..2) the boss holds
    public float tellSeconds = .7f; // telegraph before the first shot / beam
    // The body parts it fires from (BossEmitterTable names) and their
    // resolved indices (filled by BossCatalog).
    public string[] emitters;
    public int[] parts;
    // On each volley the boss shows its drawn Fire pose -- whose muzzle burst
    // is drawn at this attack's part -- instead of holding its tell pose.
    public bool fireFrame;
    // Aimed / fan: one part per volley, in turn, instead of all at once.
    public bool alternate;
    // Fan: each part fans out along the line from this part through it
    // (spores flying off the petal tips, away from the bulb), bent down
    // towards the ship by radialBend (1 = straight out, 0 = straight down).
    public string radialFrom;
    public int radialPart = -1;
    public float radialBend = .6f;
    public int volleys = 1;
    public float volleyGap = .3f;
    public int count = 1;           // shots per part per volley (lob: columns hit)
    public float spreadDeg;         // aimed: spread of a volley; fan: total arc
    public float rotateDeg;         // fan: offset alternated between volleys
    public float speed = 4f;        // world units / second
    public BossShotStyle style;
    public BossRailMode rail = BossRailMode.Absorb;
    public int bounces;             // Bounce: ricochets before it splashes
    // Lob: thrown up at lobUp, falls under gravity to at most fallSpeed.
    public float lobUp = 2.2f, gravity = 6f, fallSpeed = 3.6f;
    // Beam
    public BossBeamAim aim;
    public float aimDeg;            // Down: turned this far outward
    public float sweepDeg;          // over the hold: + outward (or away from the ship's side), - inward
    public float beamWidth = .3f;   // world units
    public float hold = .7f;        // seconds a beam stays live
    public float cooldown = 1.2f;   // idle time after the attack

    // The phase of the fight (1..3) from which the attack is in rotation (BossCatalog.UnlockedAttacks). A table is
    // kept sorted by it, so "the unlocked attacks" is a prefix: phase 1 = 2 attacks, phase 2 = 3, phase 3 = all five.
    public int minPhase = 1;

    // The area hazard kinds (Jet / Wave / Blast / Strike; BossExecutors). `count` is a strike's lanes; `volleys` /
    // `volleyGap` is how many times the hazard comes, each one `volleyGap` after the last (all armed at the start of
    // the tell, so every outline shows from the start of it and each aims where the ship was then).
    public BlastSpec blast;
    public JetSpec jet;
    public StrikeSpec strike;
    public WaveSpec wave;
    public LashSpec lash;           // Lash: one whip per volley, from the parts in turn
    public LogSpec log;             // Roll: one trunk per volley, thrown from the parts in turn
    public float spacing = 1.9f;    // strike: lane spacing (>= AttackStrike.MinLaneSpacing); lash / roll: how far across the ship the second volley is aimed
    public float heightAbove = 2.6f; // roll: how far above the ship's row the trunk lands
    public float aimSpreadX;        // jet: each part aims this far to its own side of the ship (0: straight at it)
}

public sealed class BossDef
{
    public string id;          // codex id
    public string name;        // name card + codex
    public string title;       // name card subtitle
    public string artKey;      // Resources/Bosses/<artKey>(_shots|_card)
    public string lore;
    // Movement: a Lissajous drift around (0, BossConfig.BossY).
    public float swayX, swayY, freqX, freqY;
    // The attacks in rotation: the default table, or (BossDef.themedAttacks) the themed one -- everything that reads
    // `attacks` follows the switch. Sorted by minPhase.
    public BossAttack[] attacks
    {
        get { return themedAttacks && themed != null ? themed : baseAttacks; }
        set { baseAttacks = value; }
    }
    BossAttack[] baseAttacks;
    // Five attacks: today's three plus the world's two new ones (docs/world-attacks-design.md section 7). Null: the
    // boss has no themed table yet (it needs a primitive that is not built).
    public BossAttack[] themed;
    // Fights with `themed` instead of the default table. Default false; a world's phase turns it on.
    public bool themedAttacks;
    // The default table, whatever themedAttacks says (tests).
    public BossAttack[] DefaultAttacks => baseAttacks;
    // Colour the hit flash and ring are tinted with (never the player's red).
    public Color flash;
    // Its hearts (BossHearts), tinting the white elite heart: a colour of
    // its own world, never the player's red.
    public Color heartColor = new Color(.8f, .4f, 1f);
    // Battle damage art (BossArt): Resources/Bosses/<damageKey>_damage.png
    // and _damage_fx.png. Null: no damage art, it fights pristine.
    public string damageKey;
    // Death animation strip: Resources/Bosses/<deathKey>_death.png, 6 square
    // cells played over the body while it blows up (BossArt.HasDeathArt).
    // Null: it keeps the body atlas's own death frames.
    public string deathKey;
    // The atlas has the expanded combat rows (BossArt.HasExpandedCombat).
    public bool expandedCombat;
    // Its damage smoke's strength: BossArt.SmokeAlpha's schedule times this
    // (clamped to 1). Tune per boss, as each one's smoke art is drawn.
    public float smokeStrength = 1f;
    // BossConfig.Underside's cache (-1: not measured yet).
    [System.NonSerialized] public float underside = -1f;
}

public static class BossCatalog
{
    public const string CodexPrefix = "boss_";

    static BossDef[] all;

    public static BossDef[] All
    {
        get
        {
            if (all == null) all = Build();
            return all;
        }
    }

    public static BossDef ForWorld(int world)
    {
        var a = All;
        return a[Mathf.Clamp(world, 0, a.Length - 1)];
    }

    public static BossDef Find(string id)
    {
        foreach (var b in All) if (b.id == id) return b;
        return null;
    }

    static readonly Color Magenta = new Color(1f, .18f, .53f);
    static readonly Color Ice = new Color(.62f, .91f, .94f);
    static readonly Color BileLight = new Color(.78f, 1f, .23f);
    // Heart colours: Space violet, Frost ice, Verdant bile, Ember amber.
    static readonly Color HeartViolet = new Color(.78f, .38f, 1f);
    static readonly Color HeartAmber = new Color(1f, .74f, .16f);
    static readonly Color Mint = new Color(.45f, 1f, .82f);

    static BossDef[] Build()
    {
        var bosses = new[]
        {
            // ---------------------------------------------------------- Space
            // A carrier: a chin cannon, a reactor core behind a chest hatch,
            // two engine pods hanging off its shoulders.
            new BossDef
            {
                id = CodexPrefix + "space", name = "VOID ARCHON", title = "CAPITAL CARRIER", artKey = "Space", damageKey = "Space", expandedCombat = true,
                lore = "A capital carrier the size of a city, parked across the only lane out of deep space. " +
                       "It doesn't chase - it just fills the sky with fire and waits for you to blink. " +
                       "Hold your nerve for half a minute and even the Archon has to let you pass.",
                swayX = 1.15f, swayY = .12f, freqX = .32f, freqY = .64f, flash = Magenta, heartColor = HeartViolet,
                attacks = new[]
                {
                    // The chin cannon glows, then snaps three bolts at the ship;
                    // they splash on a rail.
                    new BossAttack { name = "chin cannon", kind = BossAttackKind.Aimed, tell = 0, tellSeconds = .7f,
                                     emitters = new[] { "Chin" }, fireFrame = true,
                                     volleys = 3, volleyGap = .32f, count = 1, speed = 4.8f, style = BossShotStyle.Bolt,
                                     rail = BossRailMode.Absorb, cooldown = 1.1f },
                    // The chest hatch opens on the core, which vents two fans
                    // of plasma shards that ricochet once off the rails.
                    new BossAttack { name = "core burst", kind = BossAttackKind.Fan, tell = 1, tellSeconds = .8f,
                                     emitters = new[] { "Core" },
                                     volleys = 2, volleyGap = .55f, count = 7, spreadDeg = 100f, rotateDeg = 7f,
                                     speed = 3.1f, style = BossShotStyle.Shard,
                                     rail = BossRailMode.Bounce, bounces = 1, cooldown = 1.3f },
                    // Both engine pods flare and burn two lasers straight down
                    // that swing outward: the middle, between them, is safe.
                    new BossAttack { name = "pod lasers", kind = BossAttackKind.Beam, tell = 2, tellSeconds = .95f,
                                     emitters = new[] { "PodL", "PodR" },
                                     aim = BossBeamAim.Down, aimDeg = 0f, sweepDeg = 18f, beamWidth = .32f, hold = .9f,
                                     cooldown = 1.3f },
                },
            },
            // ---------------------------------------------------------- Frost
            // Half whale, half fortress: an icicle jaw, two glaring eyes, a
            // blowhole crown on top.
            new BossDef
            {
                id = CodexPrefix + "frost", name = "HOARFROST LEVIATHAN", title = "CRYO FORTRESS", artKey = "Frost", damageKey = "Frost",
                lore = "Half whale, half ice fortress, it has slept under the Frost cliffs since before the first star map. " +
                       "Its glare freezes whole lanes solid, its jaw is full of icicles the size of your ship and its crown spouts hail. " +
                       "Slip between the shards - it is slow to turn and slower to forgive.",
                swayX = 1.25f, swayY = .2f, freqX = .26f, freqY = .52f, flash = Ice, heartColor = Ice,
                attacks = new[]
                {
                    // The jaw fills with cold light and sprays icicles that
                    // ricochet off the rails twice before they shatter.
                    new BossAttack { name = "icicle spray", kind = BossAttackKind.Fan, tell = 0, tellSeconds = .8f,
                                     emitters = new[] { "Jaw" }, fireFrame = true,
                                     volleys = 3, volleyGap = .5f, count = 5, spreadDeg = 70f, rotateDeg = 9f,
                                     speed = 3f, style = BossShotStyle.Bolt,
                                     rail = BossRailMode.Bounce, bounces = 2, cooldown = 1.2f },
                    // Both eyes flare and lock on where the ship is; two
                    // freezing beams cross there, then drift apart.
                    new BossAttack { name = "glare beams", kind = BossAttackKind.Beam, tell = 1, tellSeconds = .8f,
                                     emitters = new[] { "EyeL", "EyeR" },
                                     aim = BossBeamAim.AtShip, sweepDeg = 10f, beamWidth = .26f, hold = .6f,
                                     cooldown = 1.2f },
                    // The blowhole crown spouts: hail crystals arc up out of it
                    // and rain down on four of five columns.
                    new BossAttack { name = "blowhole hail", kind = BossAttackKind.Lob, tell = 2, tellSeconds = .8f,
                                     emitters = new[] { "Crown" },
                                     volleys = 2, volleyGap = .5f, count = 4, style = BossShotStyle.Shard,
                                     lobUp = 2.2f, gravity = 6f, fallSpeed = 3.6f,
                                     rail = BossRailMode.Absorb, cooldown = 1.4f },
                },
            },
            // -------------------------------------------------------- Verdant
            // A flower with teeth: a brass stinger under the seed bulb, six
            // petal tips, two acid cannons on its flanks.
            new BossDef
            {
                id = CodexPrefix + "verdant", name = "THE BLOOM QUEEN", title = "HIVE MOTHER", artKey = "Verdant",
                lore = "The jungle planet's heart is a flower with teeth, and every vine on Verdant answers to her. " +
                       "She spits thorns from her stinger, flings spores off every petal and hoses acid from the cannons on her flanks. " +
                       "The pilot swears she smiled at him, which did not help.",
                swayX = .8f, swayY = .26f, freqX = .22f, freqY = .66f, flash = BileLight, heartColor = BileLight,
                attacks = new[]
                {
                    // The stinger swells and spits four thorns at the ship;
                    // they stick in a rail.
                    new BossAttack { name = "stinger thorns", kind = BossAttackKind.Aimed, tell = 0, tellSeconds = .6f,
                                     emitters = new[] { "Stinger" }, fireFrame = true,
                                     volleys = 4, volleyGap = .24f, count = 1, speed = 4.1f, style = BossShotStyle.Bolt,
                                     rail = BossRailMode.Absorb, cooldown = 1.1f },
                    // Every petal tip sparks, then flings two spiky spores out
                    // and down, away from the bulb; they bounce once.
                    new BossAttack { name = "spore bloom", kind = BossAttackKind.Fan, tell = 1, tellSeconds = .9f,
                                     emitters = new[] { "PetalUL", "PetalUR", "PetalL", "PetalR", "PetalLL", "PetalLR" },
                                     radialFrom = "Bulb", radialBend = .6f,
                                     volleys = 1, count = 2, spreadDeg = 24f, speed = 2.5f, style = BossShotStyle.Shard,
                                     rail = BossRailMode.Bounce, bounces = 1, cooldown = 1.3f },
                    // The flank cannons glow and hose acid: two jets start
                    // aimed out at the rails and swing in to straight down --
                    // get between the cannons.
                    new BossAttack { name = "acid cannons", kind = BossAttackKind.Beam, tell = 2, tellSeconds = 1f,
                                     emitters = new[] { "CannonL", "CannonR" },
                                     aim = BossBeamAim.Down, aimDeg = 30f, sweepDeg = -30f, beamWidth = .3f, hold = .8f,
                                     cooldown = 1.4f },
                },
            },
            // ---------------------------------------------------------- Ember
            // A basalt dragon: a burning maw, a chest furnace, a gem on its
            // brow between the eyes.
            new BossDef
            {
                id = CodexPrefix + "ember", name = "CINDER DRAKE", title = "VOLCANIC WYRM", artKey = "Ember", damageKey = "Ember", deathKey = "Ember",
                lore = "A basalt dragon that swims through magma the way the pilot swims through stars. " +
                       "It guards the last gate before home, breathing fire, hurling magma from its furnace and raking the sky with the gem on its brow. " +
                       "Everything in Ember burns - make sure it isn't you.",
                swayX = 1.35f, swayY = .18f, freqX = .38f, freqY = .76f, flash = Magenta, heartColor = HeartAmber,
                attacks = new[]
                {
                    // It rears and breathes two wide fans of fireballs from
                    // its maw; each splashes off a rail once.
                    new BossAttack { name = "fire breath", kind = BossAttackKind.Fan, tell = 0, tellSeconds = .7f,
                                     emitters = new[] { "Jaw" }, fireFrame = true,
                                     volleys = 2, volleyGap = .5f, count = 9, spreadDeg = 120f, rotateDeg = 6f,
                                     speed = 3.3f, style = BossShotStyle.Bolt,
                                     rail = BossRailMode.Bounce, bounces = 1, cooldown = 1.1f },
                    // The chest furnace roars and hurls three triplets of
                    // magma at the ship; they splash on a rail.
                    new BossAttack { name = "furnace slugs", kind = BossAttackKind.Aimed, tell = 1, tellSeconds = .6f,
                                     emitters = new[] { "Furnace" },
                                     volleys = 3, volleyGap = .36f, count = 3, spreadDeg = 24f, speed = 4.9f,
                                     style = BossShotStyle.Shard, rail = BossRailMode.Absorb, cooldown = 1.1f },
                    // The brow gem locks on to the ship, then burns a laser
                    // that rakes away from that side towards the other --
                    // step out behind it.
                    new BossAttack { name = "brow laser", kind = BossAttackKind.Beam, tell = 2, tellSeconds = .8f,
                                     emitters = new[] { "Brow" },
                                     aim = BossBeamAim.AtShip, sweepDeg = 55f, beamWidth = .34f, hold = .8f,
                                     cooldown = 1.3f },
                },
            },
            // ----------------------------------------------------------- Tide
            // An iron kraken: a hydraulic beak with a pressure jet, two
            // clusters of cannon tentacles on its flanks. (Heart and flash
            // colours are the mint of its eye.)
            new BossDef
            {
                id = CodexPrefix + "tide", name = "IRON KRAKEN", title = "TYRANT OF THE DEEP", artKey = "Tide", damageKey = "Tide", deathKey = "Tide", expandedCombat = true,
                lore = "A riveted iron kraken that has ruled the Tide trenches since the first hull went down. " +
                       "Its eight pipe-and-cable arms end in cannons, its beak spits a hydraulic jet and its one mint eye never blinks. " +
                       "Everything that sinks here ends up bolted to it.",
                swayX = 1.2f, swayY = .2f, freqX = .3f, freqY = .6f, flash = Mint, heartColor = Mint,
                attacks = new[]
                {
                    // The left tentacle cluster glows and sweeps two fans of
                    // pressure bolts out of its cannons; they ricochet once.
                    new BossAttack { name = "port cluster", kind = BossAttackKind.Fan, tell = 1, tellSeconds = .8f,
                                     emitters = new[] { "LeftA", "LeftB" }, fireFrame = true, alternate = true,
                                     volleys = 3, volleyGap = .45f, count = 5, spreadDeg = 60f, rotateDeg = 8f,
                                     speed = 3f, style = BossShotStyle.Bolt,
                                     rail = BossRailMode.Bounce, bounces = 1, cooldown = 1.2f },
                    // The beak opens around a mint bubble, then a pressure jet
                    // rakes straight down from the mouth.
                    new BossAttack { name = "beak jet", kind = BossAttackKind.Beam, tell = 0, tellSeconds = .9f,
                                     emitters = new[] { "Beak" },
                                     aim = BossBeamAim.Down, aimDeg = 0f, sweepDeg = 30f, beamWidth = .34f, hold = .8f,
                                     cooldown = 1.3f },
                    // The right cluster glows and lobs depth charges that
                    // arc up and rain down on four columns.
                    new BossAttack { name = "starboard cluster", kind = BossAttackKind.Lob, tell = 2, tellSeconds = .8f,
                                     emitters = new[] { "RightA", "RightB" },
                                     volleys = 2, volleyGap = .5f, count = 4, style = BossShotStyle.Shard,
                                     lobUp = 2.2f, gravity = 6f, fallSpeed = 3.6f,
                                     rail = BossRailMode.Absorb, cooldown = 1.4f },
                },
            },
        };

        foreach (var b in bosses)
        {
            // minPhase of the default tables: attack n joins at phase n + 1 (what the thirds of the fight always did)
            for (int i = 0; i < b.DefaultAttacks.Length; i++) b.DefaultAttacks[i].minPhase = i + 1;
            b.themed = BossThemed.TableFor(b);
            // Fighting themed: Frost (icicle drop, cold blast), Ember (flame sweep, eruption columns) passed the dodge-bot budget and every
            // boss suite. Space needs Streak (rail slugs) and its arc / scan art and sounds, Tide its water art and sounds, Verdant (built, flag OFF) awaits a verified budget run of the final table (plan 8.2e).
            b.themedAttacks = b.themed != null && (b.artKey == "Frost" || b.artKey == "Ember");
            Resolve(b, b.DefaultAttacks);
            Resolve(b, b.themed);
        }
        return bosses;
    }

    static void Resolve(BossDef b, BossAttack[] table)
    {
        if (table == null) return;
        foreach (var a in table)
        {
            a.parts = BossEmitters.Resolve(b, a.emitters);
            a.radialPart = string.IsNullOrEmpty(a.radialFrom) ? -1 : BossEmitters.Part(b, a.radialFrom);
        }
    }

    // The phase of the fight (1..3) at a point of it (0..1 of its length): thirds, as BossEncounter.FightPhase.
    public static int PhaseOf(float fightProgress01) => fightProgress01 >= 2f / 3f ? 3 : fightProgress01 >= 1f / 3f ? 2 : 1;

    // How many of the boss's attacks are in rotation at a point of the fight: those whose minPhase has come (the
    // table is sorted by it, so they are the first n). The default tables are 1, 2, 3 attacks by phase; a themed
    // table is 2, 3, 5 (docs/world-attacks-design.md section 7).
    public static int UnlockedAttacks(BossDef boss, float fightProgress01)
    {
        var table = boss.attacks;
        int phase = PhaseOf(fightProgress01), n = 0;
        while (n < table.Length && table[n].minPhase <= phase) n++;
        return Mathf.Max(n, Mathf.Min(1, table.Length));
    }

    public static bool FinalPhase(float fightProgress01) => fightProgress01 >= 2f / 3f;
}
