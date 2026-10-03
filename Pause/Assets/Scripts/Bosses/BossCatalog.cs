using UnityEngine;

// The four end-of-level bosses, one per world (index-aligned with
// WorldManager.Worlds), and their attack patterns.
//
// Every attack has a tell: the boss holds a drawn anticipation pose while a
// telegraph shows where it is going (a charging muzzle flash for shots, a
// flashing stripe for a lane), and only then fires. Patterns unlock in
// thirds of the fight: attacks[0] alone, then [0] and [1] in turn, then all
// three with shorter cooldowns (BossConfig.FinalPhaseCooldownScale).
public enum BossAttackKind
{
    Aimed,  // volleys at the ship's current position
    Fan,    // an even fan straight down, rotating a little between volleys
    Lanes,  // telegraphed columns that turn into beams
}

public enum BossShotStyle { Bolt, Shard }

public sealed class BossAttack
{
    public BossAttackKind kind;
    public int tell;                // which drawn tell pose (0..2) the boss holds
    public float tellSeconds = .7f; // telegraph before the first shot / beam
    public int volleys = 1;
    public float volleyGap = .3f;
    public int count = 1;           // shots per volley, or lanes
    public float spreadDeg;         // aimed: spread of a volley; fan: total arc
    public float rotateDeg;         // fan: offset alternated between volleys
    public float speed = 4f;        // world units / second
    public BossShotStyle style;
    public Vector2 muzzle;          // local offset from the boss centre
    // Lanes
    public bool sweep;              // every lane but one safe lane, in order
    public float stagger = .3f;     // sweep: seconds between lanes
    public float laneHold = .6f;    // seconds a beam stays live
    public float cooldown = 1.2f;   // idle time after the attack
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
    public BossAttack[] attacks;
    // Colour the hit flash and ring are tinted with (never the player's red).
    public Color flash;
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

    static BossDef[] Build()
    {
        return new[]
        {
            // ---------------------------------------------------------- Space
            new BossDef
            {
                id = CodexPrefix + "space", name = "VOID ARCHON", title = "CAPITAL CARRIER", artKey = "Space",
                lore = "A capital carrier the size of a city, parked across the only lane out of deep space. " +
                       "It doesn't chase - it just fills the sky with fire and waits for you to blink. " +
                       "Hold your nerve for half a minute and even the Archon has to let you pass.",
                swayX = 1.15f, swayY = .12f, freqX = .32f, freqY = .64f, flash = Magenta,
                attacks = new[]
                {
                    new BossAttack { kind = BossAttackKind.Aimed, tell = 0, tellSeconds = .7f, volleys = 3, volleyGap = .32f,
                                     count = 1, speed = 4.8f, style = BossShotStyle.Bolt, muzzle = new Vector2(0f, -1.2f), cooldown = 1.1f },
                    new BossAttack { kind = BossAttackKind.Fan, tell = 1, tellSeconds = .8f, volleys = 2, volleyGap = .55f,
                                     count = 7, spreadDeg = 100f, rotateDeg = 7f, speed = 3.1f, style = BossShotStyle.Shard,
                                     muzzle = new Vector2(0f, -.27f), cooldown = 1.3f },
                    new BossAttack { kind = BossAttackKind.Lanes, tell = 2, tellSeconds = .95f, count = 2, laneHold = .7f,
                                     muzzle = new Vector2(0f, -.6f), cooldown = 1.3f },
                },
            },
            // ---------------------------------------------------------- Frost
            new BossDef
            {
                id = CodexPrefix + "frost", name = "HOARFROST LEVIATHAN", title = "CRYO FORTRESS", artKey = "Frost",
                lore = "Half whale, half ice fortress, it has slept under the Frost cliffs since before the first star map. " +
                       "Its breath freezes whole lanes solid and its jaw is full of icicles the size of your ship. " +
                       "Slip between the shards - it is slow to turn and slower to forgive.",
                swayX = 1.25f, swayY = .2f, freqX = .26f, freqY = .52f, flash = Ice,
                attacks = new[]
                {
                    new BossAttack { kind = BossAttackKind.Fan, tell = 0, tellSeconds = .8f, volleys = 3, volleyGap = .5f,
                                     count = 5, spreadDeg = 70f, rotateDeg = 9f, speed = 3f, style = BossShotStyle.Shard,
                                     muzzle = new Vector2(0f, -.85f), cooldown = 1.2f },
                    new BossAttack { kind = BossAttackKind.Aimed, tell = 1, tellSeconds = .7f, volleys = 2, volleyGap = .45f,
                                     count = 3, spreadDeg = 18f, speed = 4.3f, style = BossShotStyle.Bolt,
                                     muzzle = new Vector2(0f, -.85f), cooldown = 1.2f },
                    new BossAttack { kind = BossAttackKind.Lanes, tell = 2, tellSeconds = .8f, sweep = true, stagger = .3f,
                                     laneHold = .55f, muzzle = new Vector2(0f, -.6f), cooldown = 1.4f },
                },
            },
            // -------------------------------------------------------- Verdant
            new BossDef
            {
                id = CodexPrefix + "verdant", name = "THE BLOOM QUEEN", title = "HIVE MOTHER", artKey = "Verdant",
                lore = "The jungle planet's heart is a flower with teeth, and every vine on Verdant answers to her. " +
                       "She spits thorns, coughs clouds of acid spores and lashes whole lanes with her roots. " +
                       "The pilot swears she smiled at him, which did not help.",
                swayX = .8f, swayY = .26f, freqX = .22f, freqY = .66f, flash = BileLight,
                attacks = new[]
                {
                    new BossAttack { kind = BossAttackKind.Aimed, tell = 0, tellSeconds = .6f, volleys = 4, volleyGap = .24f,
                                     count = 1, speed = 4.1f, style = BossShotStyle.Bolt, muzzle = new Vector2(0f, -.4f), cooldown = 1.1f },
                    new BossAttack { kind = BossAttackKind.Fan, tell = 1, tellSeconds = .9f, volleys = 1,
                                     count = 11, spreadDeg = 150f, speed = 2.5f, style = BossShotStyle.Shard,
                                     muzzle = new Vector2(0f, -.5f), cooldown = 1.3f },
                    new BossAttack { kind = BossAttackKind.Lanes, tell = 2, tellSeconds = 1f, count = 3, laneHold = .8f,
                                     muzzle = new Vector2(0f, -.5f), cooldown = 1.4f },
                },
            },
            // ---------------------------------------------------------- Ember
            new BossDef
            {
                id = CodexPrefix + "ember", name = "CINDER DRAKE", title = "VOLCANIC WYRM", artKey = "Ember",
                lore = "A basalt dragon that swims through magma the way the pilot swims through stars. " +
                       "It guards the last gate before home, raking the sky with fire and burying lanes under its breath. " +
                       "Everything in Ember burns - make sure it isn't you.",
                swayX = 1.35f, swayY = .18f, freqX = .38f, freqY = .76f, flash = Magenta,
                attacks = new[]
                {
                    new BossAttack { kind = BossAttackKind.Fan, tell = 0, tellSeconds = .7f, volleys = 2, volleyGap = .5f,
                                     count = 9, spreadDeg = 120f, rotateDeg = 6f, speed = 3.3f, style = BossShotStyle.Shard,
                                     muzzle = new Vector2(0f, -1.1f), cooldown = 1.1f },
                    new BossAttack { kind = BossAttackKind.Aimed, tell = 1, tellSeconds = .6f, volleys = 3, volleyGap = .36f,
                                     count = 3, spreadDeg = 24f, speed = 4.9f, style = BossShotStyle.Bolt,
                                     muzzle = new Vector2(0f, -.95f), cooldown = 1.1f },
                    new BossAttack { kind = BossAttackKind.Lanes, tell = 2, tellSeconds = .7f, sweep = true, stagger = .25f,
                                     laneHold = .5f, muzzle = new Vector2(0f, -.7f), cooldown = 1.3f },
                },
            },
        };
    }

    // Which attacks are in rotation at a point of the fight (0..1 of the
    // fight's length): the first alone, then the first two, then all.
    public static int UnlockedAttacks(BossDef boss, float fightProgress01)
    {
        int n = boss.attacks.Length;
        if (fightProgress01 < 1f / 3f) return Mathf.Min(1, n);
        if (fightProgress01 < 2f / 3f) return Mathf.Min(2, n);
        return n;
    }

    public static bool FinalPhase(float fightProgress01) => fightProgress01 >= 2f / 3f;
}
