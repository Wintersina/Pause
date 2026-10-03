using System.Collections.Generic;
using UnityEngine;

// Every enemy the spawner can field, per world, in one table.
//
// Each world (WorldManager.Worlds: Space, Frost, Verdant, Ember) fills the
// same gameplay roles, so enmiesOnBoard's phases and difficulty hold whatever
// planet the player is on -- only the cast changes:
//
//   Rock     the obstacle hazards (tag Astr): the spawner's small/mid/large
//            asteroid slots and its "small enemy" slot. Three or four per
//            world; the FLOATING ones are chunks of that world's ground
//            (grass, regolith, snow, crust cap) that drift upright and sway
//            instead of tumbling
//   Big      the "big enemy" slot: one slow, armoured heavy
//   Fighter  the escalating extras, tiers 1-4 by phase
//   Chaser   climbs up from below and hunts the player (ChaserEnemy)
//   Alien    the invader lines (the alien achievements key on these)
//   Mine     the rail mine clamped to the side rails
//
// Ids are stable: the codex, the explosion variants and saved discoveries key
// on them, so rename the display name freely but never the id. `key` is the
// art key (the strip Resources/Enemies/<key>.png, built by
// Art/Enemies/src~) and the spawned GameObject's name, except for the alien
// and the mine, which keep the legacy names "alien1" and "mine" that
// collisionDetection matches on.
public enum EnemyRole { Rock, Big, Fighter, Chaser, Alien, Mine }

public sealed class EnemyDef
{
    public string key;            // art key, e.g. "frost_fighter_2" (also the object name)
    public string displayName;    // "Icicle"
    public EnemyRole role;
    public int world;             // WorldManager.Worlds index
    public int tier;              // fighters 1-4; rocks 1-4 (variant); others 0
    public bool floating;         // rocks only: a floating chunk of the world's ground (sways, never tumbles)
    public string codexId;        // CodexCatalogue entry id
    public string concept;        // one-line art brief
    public string lore;           // codex text
    public string[] legacyNames;  // older prefab names that belong to this entry (codex matching)
    public TargetExplosion.Kind explosion;
    public TargetExplosion.Size explosionSize;

    public string Tag => role == EnemyRole.Rock ? "Astr" : "Enimey";

    public string ObjectName =>
        role == EnemyRole.Alien ? EnemyRoster.AlienObjectName :
        role == EnemyRole.Mine ? EnemyRoster.MineObjectName : key;

    public string StripPath => EnemyRoster.ArtFolder + "/" + key;

    // World size of one 128 u flipbook frame (the drawing sits inside it) and
    // the BoxCollider2D in world units, both per role.
    public float FrameWorldSize => EnemyRoster.FrameWorldSize(role);
    public Vector2 ColliderSize => EnemyRoster.ColliderSize(role);

    public bool IsHazard => role == EnemyRole.Rock || role == EnemyRole.Mine;
}

public static class EnemyRoster
{
    public const string ArtFolder = "Enemies";
    public const string AlienObjectName = "alien1";   // collisionDetection: alien achievement
    public const string MineObjectName = "mine";      // collisionDetection: mine explosion
    public const int FrameCount = 7;                  // 0-3 idle, 4-5 tell, 6 hit flash
    public const int TellFrame = 4, HitFrame = 6;

    public static readonly string[] WorldKeys = { "space", "frost", "verdant", "ember" };

    // ---- sizes ---------------------------------------------------------------
    // What each role measured before the redraw (world units), so every
    // replacement stays within +/-15% (EnemyRosterTest):
    //   Rock    aestroid_* prefabs: 0.576 wide, BoxCollider 0.472 square
    //   Big     the slot held those same rocks: 0.576 / 0.472
    //   Fighter kn_enemy* hulls: ~0.83 wide, collider ~0.68 x 0.61
    //   Chaser  kn_enemyRed5 + ChaserEnemy: 0.866 x 0.75, collider 0.71 x 0.615
    //   Alien   alien1: 0.64, collider 0.4 x 0.2
    //   Mine    runtime rail mine: ~0.8, collider 0.62 square
    public static Vector2 LegacyCollider(EnemyRole role)
    {
        switch (role)
        {
            case EnemyRole.Rock: return new Vector2(.472f, .472f);
            case EnemyRole.Big: return new Vector2(.472f, .472f);
            case EnemyRole.Fighter: return new Vector2(.68f, .61f);
            case EnemyRole.Chaser: return new Vector2(.71f, .615f);
            case EnemyRole.Alien: return new Vector2(.4f, .2f);
            default: return new Vector2(.621f, .621f);
        }
    }

    public static float LegacyWidth(EnemyRole role)
    {
        switch (role)
        {
            case EnemyRole.Rock: case EnemyRole.Big: return .576f;
            case EnemyRole.Fighter: return .83f;
            case EnemyRole.Chaser: return .866f;
            case EnemyRole.Alien: return .64f;
            default: return .8f;
        }
    }

    public static Vector2 ColliderSize(EnemyRole role)
    {
        switch (role)
        {
            case EnemyRole.Rock: return new Vector2(.472f, .472f);
            case EnemyRole.Big: return new Vector2(.52f, .52f);      // +10%: it's the heavy
            case EnemyRole.Fighter: return new Vector2(.68f, .61f);
            case EnemyRole.Chaser: return new Vector2(.66f, .62f);
            case EnemyRole.Alien: return new Vector2(.4f, .2f);
            default: return new Vector2(.621f, .621f);
        }
    }

    // The drawings fill ~75-100% of their 128 u frame, so a frame is a bit
    // larger in the world than the silhouette it holds (EnemyRosterTest
    // measures the drawn silhouette against LegacyWidth).
    public static float FrameWorldSize(EnemyRole role)
    {
        switch (role)
        {
            case EnemyRole.Rock: return .66f;
            case EnemyRole.Big: return .70f;
            case EnemyRole.Fighter: return .9f;
            case EnemyRole.Chaser: return .95f;
            case EnemyRole.Alien: return .67f;
            default: return .85f;
        }
    }

    // ---- animation timing ------------------------------------------------------
    // Hold ticks at 24 fps per frame (art-style.md section 3), mirrored from
    // the IDLE_TICKS / TELL_TICKS tables in Art/Enemies/src~.
    public static int[] IdleTicks(EnemyRole role)
    {
        switch (role)
        {
            case EnemyRole.Rock: return new[] { 5, 3, 2, 3 };
            case EnemyRole.Mine: return new[] { 6, 2, 2, 4 };
            case EnemyRole.Big: return new[] { 6, 2, 3, 3 };
            case EnemyRole.Alien: return new[] { 4, 2, 3, 3 };
            default: return new[] { 6, 3, 2, 3 };   // fighter, chaser
        }
    }

    public static int[] TellTicks(EnemyRole role)
    {
        switch (role)
        {
            case EnemyRole.Big: return new[] { 3, 4 };
            case EnemyRole.Mine: return new[] { 2, 2 };
            default: return new[] { 2, 3 };
        }
    }

    public const int HitTicks = 2;

    // Floating rocks rock gently either side of upright (AsteroidSpin sway).
    public const float FloatSwayDegrees = 9f, FloatSwayPeriod = 3.2f;

    // ---- the table -------------------------------------------------------------
    static EnemyDef[] all;
    public static EnemyDef[] All { get { if (all == null) all = Build(); return all; } }

    static Dictionary<string, EnemyDef> byKey;
    public static EnemyDef Find(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (byKey == null)
        {
            byKey = new Dictionary<string, EnemyDef>();
            foreach (var d in All) byKey[d.key] = d;
        }
        EnemyDef def;
        return byKey.TryGetValue(key, out def) ? def : null;
    }

    public static EnemyDef FindByCodexId(string id)
    {
        foreach (var d in All) if (d.codexId == id) return d;
        return null;
    }

    // The world the spawner should draw from right now: the current planet,
    // or Space when no WorldManager runs (the tutorial, edit-mode tests).
    public static int CurrentWorld =>
        WorldManager.Instance != null ? WorldManager.CurrentIndex : 0;

    public static List<EnemyDef> For(int world, EnemyRole role)
    {
        world = Mathf.Clamp(world, 0, WorldKeys.Length - 1);
        var list = new List<EnemyDef>();
        foreach (var d in All) if (d.world == world && d.role == role) list.Add(d);
        return list;
    }

    public static EnemyDef One(int world, EnemyRole role)
    {
        var list = For(world, role);
        return list.Count > 0 ? list[0] : null;
    }

    public static EnemyDef Pick(int world, EnemyRole role)
    {
        var list = For(world, role);
        return list.Count > 0 ? list[Random.Range(0, list.Count)] : null;
    }

    public static EnemyDef Fighter(int world, int tier)
    {
        foreach (var d in For(world, EnemyRole.Fighter)) if (d.tier == tier) return d;
        return null;
    }

    // Marks a rock as one of the floating world rocks (rocks.py FLOATING).
    static EnemyDef Floating(EnemyDef d)
    {
        d.floating = true;
        return d;
    }

    static EnemyDef Def(string key, string name, EnemyRole role, int world, int tier, string codexId,
                        TargetExplosion.Kind explosion, string concept, string lore, params string[] legacy)
    {
        return new EnemyDef
        {
            key = key, displayName = name, role = role, world = world, tier = tier, codexId = codexId,
            explosion = explosion,
            explosionSize = role == EnemyRole.Big ? TargetExplosion.Size.Large : TargetExplosion.Size.Medium,
            concept = concept, lore = lore, legacyNames = legacy,
        };
    }

    const EnemyRole R = EnemyRole.Rock, B = EnemyRole.Big, F = EnemyRole.Fighter, C = EnemyRole.Chaser,
                    A = EnemyRole.Alien, M = EnemyRole.Mine;
    const TargetExplosion.Kind Metal = TargetExplosion.Kind.Metal, Rock = TargetExplosion.Kind.Rock,
                               Mine = TargetExplosion.Kind.Mine, Ice = TargetExplosion.Kind.Ice,
                               Spore = TargetExplosion.Kind.Spore, Magma = TargetExplosion.Kind.Magma;

    static EnemyDef[] Build()
    {
        return new[]
        {
            // ================================================================ SPACE
            // The crater rocks and rail mine stay; the Kenney fighters, the red
            // "Hunter" hull and the pixel invader are redrawn in the house style.
            // Floating: a chunk of asteroid under a cratered regolith cap.
            Floating(Def("space_rock_crater", "Beacon Rock", R, 0, 1, "hazard_rock_crater", Rock,
                "Floating asteroid chunk: pale cratered regolith cap over a dark keel, a survey beacon blinking magenta, two loose shards bobbing below.",
                "Somebody planted a survey beacon on this drifting crater and never came back for it. It still " +
                "blinks for them. Don't be the next dent.",
                "aestroidbrown1", "aestroidgay1", "aestroidgraycrooked2")),
            Def("space_rock_cluster", "Cluster Rock", R, 0, 2, "hazard_rock_cluster", Rock,
                "Three boulders welded by a glowing magenta mineral seam.",
                "Three boulders welded together by some ancient crash, tumbling as one. Wider than it looks - " +
                "give it room, or smash it shielded for asteroid medals.",
                "aestroidbrown", "aestroiddark1", "aestroidgraycrooked1"),
            Def("space_rock_dark", "Coal Rock", R, 0, 3, "hazard_rock_dark", Rock,
                "Slanted coal wedge cut in big hard planes, a buried magenta eye that blinks.",
                "Dull, dark asteroids that hide against the void until the last second. Watch for the stars " +
                "they blot out.",
                "aestroiddark", "aestroidgay3"),
            Def("space_mine", "Rail Mine", M, 0, 0, "hazard_mine", Mine,
                "The approved rail bomb: gunmetal clamp, steel hub, four lugs, cyan core that arms to a burst.",
                "Bombs clamped to the side rails of deep space. They never leave their rail, and the core " +
                "arms as you pass - hug the middle lanes.",
                "mine"),
            Def("space_big", "Bastion", B, 0, 0, "enemy_space_big", Metal,
                "Octagonal armoured pod, four bruise plates, twin cannon prongs, one big magenta reactor eye.",
                "A slow armoured pod that guards the old shipping lanes. When its plates slam open and the " +
                "eye flares, it's charging - be somewhere else."),
            Def("space_fighter_1", "Needle", F, 0, 1, "enemy_space_fighter_1", Metal,
                "Slim steel dart, single ram prong, bruise wings, magenta visor.",
                "The scout of the steel fleets: thin, fast and nosy. It was probably the first thing that saw " +
                "you fall out of that black hole."),
            Def("space_fighter_2", "Steel Claw", F, 0, 2, "enemy_space_fighter_2", Metal,
                "The style-guide claw fighter: twin prongs that pinch, then snap open.",
                "Twin prongs and a bad attitude. Watch the visor: when it dims and the claws pinch, it's " +
                "winding up to lunge."),
            Def("space_fighter_3", "Twin Claw", F, 0, 3, "enemy_space_fighter_3", Metal,
                "Claw hull with outer bruise blades and a twin visor.",
                "A claw with extra blades bolted on and two eyes to aim them. It flies wider than it looks."),
            Def("space_fighter_4", "Warden", F, 0, 4, "enemy_space_fighter_4", Metal,
                "Heavy claw: shoulder armour, a centre ram prong and a magenta power core.",
                "The steel fleets' officer class, saved for the deepest stretch of a flight. Meet it shielded " +
                "or not at all."),
            Def("space_chaser", "Steel Hound", C, 0, 0, "enemy_chaser", Metal,
                "Up-facing steel interceptor with hinged jaws and a hungry eye; lunges jaws-first.",
                "It climbs up from below and tails you, jaws snapping, until it loses interest. Keep sliding " +
                "sideways and it overshoots."),
            Def("space_alien", "Bile Mite", A, 0, 0, "enemy_alien", Metal,
                "The style-guide crowned bug: bile carapace, bruise mandibles, one slit eye.",
                "Wriggling pests that travel in lines of up to four, wiggling like they own the place. There's " +
                "always a gap - find it. Smash them while shielded for alien-hunter medals.",
                "alien"),

            // ================================================================ FROST
            Def("frost_rock_shard", "Ice Shard", R, 1, 1, "hazard_frost_rock_shard", Ice,
                "Cluster of hexagonal ice prisms around a cyan heart.",
                "Splinters of a frozen moon, sharp enough to shave paint. Their hearts still glow from " +
                "whatever froze them."),
            // Floating: frozen bedrock under a snow cap.
            Floating(Def("frost_rock_chunk", "Frozen Chunk", R, 1, 2, "hazard_frost_rock_chunk", Ice,
                "Floating block of frozen bedrock under an overhanging snow cap, a fringe of icicles underneath, a frost crystal glowing cyan in its face.",
                "A lump of frozen ground that broke off the glacier and kept going, snow cap and all. The icicles " +
                "underneath are the sharp end. Out here even the rubble dresses for the cold.")),
            Def("frost_rock_rime", "Rime Star", R, 1, 3, "hazard_frost_rock_rime", Ice,
                "Six-point ice star with a hex cyan core.",
                "A snowflake the size of a car, spinning slowly: pretty, deadly, mostly deadly."),
            Def("frost_mine", "Geode Mine", M, 1, 0, "hazard_frost_mine", Mine,
                "A tall crystal geode held off the rail by two ice hooks; arming grows crystals that shoot out in a frost star.",
                "A geode the Frost rails grew around a cold star. Get close and the crystals start to grow - " +
                "then they all come out at once."),
            Def("frost_big", "Glacier Golem", B, 1, 0, "enemy_frost_big", Ice,
                "A hunched iceberg hulk with crystal shoulders, one cyan visor and a jagged ice maw that cracks open.",
                "A walking iceberg with one cold eye. When its crystal jaw cracks open, the frost is coming " +
                "your way."),
            Def("frost_fighter_1", "Flake", F, 1, 1, "enemy_frost_fighter_1", Ice,
                "Hex drone, two swept crystal blades and an icicle lance.",
                "A tiny drone with a big icicle. It drifts like a snowflake until it decides you're the target."),
            Def("frost_fighter_2", "Icicle", F, 1, 2, "enemy_frost_fighter_2", Ice,
                "Hex drone with four crystal blades and a long lance.",
                "Four blades, one lance, zero warmth. It pinches its blades in before it strikes."),
            Def("frost_fighter_3", "Frost Kite", F, 1, 3, "enemy_frost_fighter_3", Ice,
                "Six-bladed crystal star drone.",
                "A spinning star of ice blades. The cold makes them fast; the blades make them rude."),
            Def("frost_fighter_4", "Hailstorm", F, 1, 4, "enemy_frost_fighter_4", Ice,
                "Armoured-collar drone, seven crystal blades, double lance.",
                "The drone the others form up behind. When it flares, the whole sky glitters."),
            Def("frost_chaser", "Frost Lancer", C, 1, 0, "enemy_frost_chaser", Ice,
                "A needle of ice that hunts from below; its lance shoots out of its nose and its fins fold back on the lunge.",
                "It hunts from below with an icicle for a nose. The lance shoots out when it lunges, so " +
                "don't be where it's pointing."),
            Def("frost_alien", "Cryo Jelly", A, 1, 0, "enemy_frost_alien", Ice,
                "A crystal-domed jellyfish with one magenta eye, trailing icicle tentacles that lash.",
                "Ice jellies drifting in lines, tentacles swaying. They pull them in, then lash - keep to " +
                "the gaps."),

            // ============================================================== VERDANT
            Def("verdant_rock_pod", "Thorn Pod", R, 2, 1, "hazard_verdant_rock_pod", Spore,
                "Armoured seed pod ringed with magenta thorns; its glowing seam breathes open.",
                "A seed the jungle flung into the sky. The seam glows when it's ready to burst - better it " +
                "bursts behind you."),
            // Floating: the first-pass grass-capped rock, restored and inked harder.
            Floating(Def("verdant_rock_spore", "Spore Rock", R, 2, 2, "hazard_verdant_rock_spore", Spore,
                "Chunky octagonal rock under a bright grass cap, one hard shadow plane, lime spore vents as hard hex lights that pulse and puff.",
                "Rock gone mouldy with life. Its vents puff spores every few seconds; the smell is the least " +
                "of your problems.")),
            Def("verdant_rock_knot", "Bramble Knot", R, 2, 3, "hazard_verdant_rock_knot", Spore,
                "Three thorny branches knotted round a glowing bile bud.",
                "Branches tangled into a spinning thorn ball. It catches anything - including lost pilots."),
            Floating(Def("verdant_rock_vine", "Vine Rock", R, 2, 4, "hazard_verdant_rock_vine", Spore,
                "Floating wedge of earth under an angular bush, three vines dangling with lime buds that glow and whip.",
                "A clump of jungle floor that floated off with its garden still attached. The buds on the " +
                "vines light up before they whip - and they always whip.")),
            Def("verdant_mine", "Burr Mine", M, 2, 0, "hazard_verdant_mine", Mine,
                "A thorny seed burr hanging from a coiled vine tendril; its husk splits along glowing seams to arm.",
                "A seed burr dangling from the rail vines. When its husk splits and the seams glow, it's about " +
                "to scatter thorns everywhere."),
            Def("verdant_big", "Bloom Maw", B, 2, 0, "enemy_verdant_big", Spore,
                "Five-petal carnivorous bud; the petals fold back to show a toothed glowing maw.",
                "A flower that eats ships. Shut, it's just a big bud; when the petals snap open, it's " +
                "hungry."),
            Def("verdant_fighter_1", "Gnat", F, 2, 1, "enemy_verdant_fighter_1", Spore,
                "Small bile bug, buzzing leaf wings, bile eyes.",
                "Little buzzing bugs that swarm anything new. You're very new."),
            Def("verdant_fighter_2", "Wasp", F, 2, 2, "enemy_verdant_fighter_2", Spore,
                "Striped bruise abdomen, magenta stinger, bile thorax.",
                "Striped, angry and armed at both ends. The wings blur just before it dives."),
            Def("verdant_fighter_3", "Mantis", F, 2, 3, "enemy_verdant_fighter_3", Spore,
                "Wasp body with bone-bladed scythe arms reaching down.",
                "Scythe arms folded like it's praying. It isn't."),
            Def("verdant_fighter_4", "Hornet Queen", F, 2, 4, "enemy_verdant_fighter_4", Spore,
                "Four-winged armoured queen with scythes and stinger.",
                "The swarm's queen: four wings, two scythes and one stinger, all pointed your way."),
            Def("verdant_chaser", "Dragonsting", C, 2, 0, "enemy_verdant_chaser", Spore,
                "A four-winged dragonfly with bile compound eyes, forward mandibles and a long barbed tail.",
                "It rises out of the canopy on four blurring wings and snaps at your engines. Slide sideways " +
                "and let it bite the air."),
            Def("verdant_alien", "Snap Sprout", A, 2, 0, "enemy_verdant_alien", Spore,
                "A walking flytrap on root legs; its jaws gape and snap around a bile gullet.",
                "Flytraps that learned to march in lines. Their jaws snap shut on anything slow."),

            // ================================================================ EMBER
            Def("ember_rock_magma", "Magma Rock", R, 3, 1, "hazard_ember_rock_magma", Magma,
                "Basalt rock split by sodium magma cracks that pulse.",
                "Basalt with a molten heart that never cooled. The cracks pulse like it's breathing."),
            Def("ember_rock_cinder", "Cinder Chunk", R, 3, 2, "hazard_ember_rock_cinder", Magma,
                "Blocky cinder with a molten core showing through a split.",
                "A cinder with a window into its own furnace. Don't look too long; don't fly too close."),
            Def("ember_rock_obsidian", "Obsidian Shard", R, 3, 3, "hazard_ember_rock_obsidian", Magma,
                "Tall violet obsidian blade with a sodium rim and one lava vein.",
                "Volcanic glass sharpened by the heat. It reflects the lava so well you might miss it."),
            Floating(Def("ember_rock_islet", "Lava Islet", R, 3, 4, "hazard_ember_rock_islet", Magma,
                "Floating basalt slab under a cracked black crust cap, glowing magma seams, lava dripping off its underside.",
                "A slab of the shore that drifted off before the lava finished with it. It drips as it floats - " +
                "mind the drops, mind the rock.")),
            Def("ember_mine", "Crucible Mine", M, 3, 0, "hazard_ember_mine", Mine,
                "A basalt crucible slung from the rail on chains; its magma boils over and erupts when armed.",
                "A pot of magma on a chain. It simmers as you pass and boils over if you linger - don't."),
            Def("ember_big", "Magma Skull", B, 3, 0, "enemy_ember_big", Magma,
                "A horned basalt skull whose furnace jaw glows behind a grille and drops open.",
                "A skull of cooled lava with a furnace for a jaw. The grille glows brighter right before it " +
                "drops open."),
            Def("ember_fighter_1", "Cinder", F, 3, 1, "enemy_ember_fighter_1", Metal,
                "Charred arrowhead with molten vents and a magenta visor.",
                "A scorched little dart that leaves a smell of burnt metal everywhere it goes."),
            Def("ember_fighter_2", "Scorch", F, 3, 2, "enemy_ember_fighter_2", Metal,
                "Forked char claw with four molten vents and a sodium core.",
                "Vents along the hull glow white-hot when it winds up. Hot hull, short temper."),
            Def("ember_fighter_3", "Brand", F, 3, 3, "enemy_ember_fighter_3", Metal,
                "Forked char claw with flickering flame fins.",
                "It flies with flames for fins and brands anything that doesn't move."),
            Def("ember_fighter_4", "Pyre", F, 3, 4, "enemy_ember_fighter_4", Metal,
                "Horned, flame-finned heavy with a sodium core.",
                "The fire fleet's champion: horns, flames and a core like a small sun. Shield up, grin on."),
            Def("ember_chaser", "Cinder Fang", C, 3, 0, "enemy_ember_chaser", Metal,
                "A wide salamander head whose upper and lower jaws hinge apart to show a molten throat.",
                "It climbs out of the lava glow below and gapes its molten jaws at your heels until it " +
                "overheats."),
            Def("ember_alien", "Ember Imp", A, 3, 0, "enemy_ember_alien", Magma,
                "A living flame wearing a horned basalt mask with two magenta eyes.",
                "Little flames in basalt masks, grinning in formation. They flare up when you get close."),
        };
    }
}
