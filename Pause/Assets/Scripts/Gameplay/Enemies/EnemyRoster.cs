using System.Collections.Generic;
using UnityEngine;

// Every enemy the spawner can field, per world, in one table.
//
// Each world (WorldManager.Worlds: Space, Frost, Verdant, Ember, Tide) fills the
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
// collisionDetection matches on. The rail mines are neon pixel art instead:
// one row each of the original four-world atlas, Tide's in a second atlas
// file of its own (RailMineArt).
public enum EnemyRole { Rock, Big, Fighter, Chaser, Alien, Mine }

public sealed class EnemyDef
{
    public string key;            // art key, e.g. "frost_fighter_2" (also the object name)
    public string displayName;    // "Icicle"
    public EnemyRole role;
    public int world;             // WorldManager.Worlds index
    public int tier;              // fighters 1-4; rocks 1-4 (variant); others 0
    public bool floating;         // rocks only: a floating chunk of the world's ground (sways, never tumbles)
    public float frameScale = 1f; // the drawing's frame size relative to its role's (an art that draws small inside its cell)
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

    // The mines all read the one neon atlas (RailMineArt), a row per world.
    public string StripPath => role == EnemyRole.Mine ? RailMineArt.AtlasPathFor(world) : EnemyRoster.ArtFolder + "/" + key;

    // World size of one 128 u flipbook frame (the drawing sits inside it) and
    // the BoxCollider2D in world units, both per role.
    public float FrameWorldSize => EnemyRoster.FrameWorldSize(role) * frameScale;
    public Vector2 ColliderSize => EnemyRoster.ColliderSize(role);

    public bool IsHazard => role == EnemyRole.Rock || role == EnemyRole.Mine;

    // What it does: its movement pattern and attack (EnemyBehaviours).
    public EnemyBehaviour Behaviour => EnemyBehaviours.For(key);
}

public static class EnemyRoster
{
    public const string ArtFolder = "Enemies";
    public const string AlienObjectName = "alien1";   // collisionDetection: alien achievement
    public const string MineObjectName = "mine";      // collisionDetection: mine explosion
    public const int FrameCount = 7;                  // 0-3 idle, 4-5 tell, 6 hit flash
    public const int TellFrame = 4, HitFrame = 6;

    public static readonly string[] WorldKeys = { "space", "frost", "verdant", "ember", "tide" };

    // ---- sizes ---------------------------------------------------------------
    // What each role measured before the redraw (world units), so every
    // replacement stays within +/-15% (EnemyRosterTest):
    //   Rock    aestroid_* prefabs: 0.576 wide, BoxCollider 0.472 square
    //   Big     the slot held those same rocks: 0.576 / 0.472
    //   Fighter kn_enemy* hulls: ~0.83 wide, collider ~0.68 x 0.61
    //   Chaser  kn_enemyRed5 + ChaserEnemy: 0.866 x 0.75, collider 0.71 x 0.615
    //   Alien   alien1: 0.64, collider 0.4 x 0.2
    //   Mine    runtime rail mine: ~0.8 cell, collider 0.62 square
    // Every role keeps to those, except the Big heavies: the user wanted them
    // to read as properly big (they were smaller than the fighters), so they
    // target BigWidth instead (TargetWidth / TargetCollider below), and the
    // mines, back on their original neon atlas: held to the drawing the
    // original showed (MineWidth), not its 0.8 u cell.
    public const float BigWidth = 1.1f;

    // The original atlas mine's drawn width: ~255 px at 180 PPU, prefab
    // scale 0.46. RailMineArt.PixelsPerUnit draws it at ~0.66 u.
    public const float MineWidth = .65f;

    // The size a role is held to (+/-15%, EnemyRosterTest): the legacy size,
    // or the heavies' and mines' own.
    public static float TargetWidth(EnemyRole role)
    {
        return role == EnemyRole.Big ? BigWidth : role == EnemyRole.Mine ? MineWidth : LegacyWidth(role);
    }

    public static Vector2 TargetCollider(EnemyRole role)
    {
        return role == EnemyRole.Big ? ColliderSize(EnemyRole.Big) : LegacyCollider(role);
    }

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
            // ~78% of the 1.1 u drawing: scaled with the art, inset a little
            // further than the rocks' 82% so a graze past the plating is fair
            case EnemyRole.Big: return new Vector2(.86f, .86f);
            case EnemyRole.Fighter: return new Vector2(.68f, .61f);
            case EnemyRole.Chaser: return new Vector2(.66f, .62f);
            case EnemyRole.Alien: return new Vector2(.4f, .2f);
            default: return new Vector2(.621f, .621f);
        }
    }

    // The drawings fill ~75-100% of their 128 u frame, so a frame is a bit
    // larger in the world than the silhouette it holds (EnemyRosterTest
    // measures the drawn silhouette against TargetWidth).
    public static float FrameWorldSize(EnemyRole role)
    {
        switch (role)
        {
            case EnemyRole.Rock: return .66f;
            case EnemyRole.Big: return 1.3f;   // the drawing fills ~85%: ~1.1 u
            case EnemyRole.Fighter: return .9f;
            case EnemyRole.Chaser: return .95f;
            case EnemyRole.Alien: return .67f;
            default: return .85f;
        }
    }

    // ---- animation timing ------------------------------------------------------
    // Hold ticks at 24 fps per frame (art-style.md section 3), mirrored from
    // the IDLE_TICKS / TELL_TICKS tables in Art/Enemies/src~. The mines
    // (RailMineArt.FlipbookColumns) keep the old RailBombAnimator's 6 fps
    // snap: idle holds dormant with one 4-tick waking blink of the core, and
    // arming loops waking -> charging at 4 ticks a frame.
    public static int[] IdleTicks(EnemyRole role)
    {
        switch (role)
        {
            case EnemyRole.Rock: return new[] { 5, 3, 2, 3 };
            case EnemyRole.Mine: return new[] { 6, 4, 6, 6 };
            case EnemyRole.Big: return new[] { 6, 2, 3, 3 };
            case EnemyRole.Alien: return new[] { 4, 2, 3, 3 };
            default: return new[] { 6, 3, 2, 3 };   // fighter, chaser
        }
    }

    // Per-enemy idle holds: the Space alien hovers on a calm 3 fps loop
    // (8 ticks a drawing); everyone else uses its role's table.
    public const int SpaceAlienIdleTicks = 8;
    public static int[] IdleTicks(EnemyDef def) =>
        def != null && def.key == "space_alien"
            ? new[] { SpaceAlienIdleTicks, SpaceAlienIdleTicks, SpaceAlienIdleTicks, SpaceAlienIdleTicks }
            : IdleTicks(def.role);

    // What EnemyFlipbook actually loops (the Steel Hound loops all four idle
    // cells now, like everyone else).
    public static int[] FlipbookIdleTicks(EnemyDef def) => IdleTicks(def);

    public static int[] TellTicks(EnemyRole role)
    {
        switch (role)
        {
            case EnemyRole.Big: return new[] { 3, 4 };
            case EnemyRole.Mine: return new[] { 4, 4 };
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

    // The art draws small inside its cell: show the frame this much larger (silhouette back within the role's size).
    static EnemyDef Scaled(EnemyDef d, float scale)
    {
        d.frameScale = scale;
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
                "Neon pixel rail bomb: gunmetal clamp and sphere, four lugs, a cyan core that wakes, charges and bursts.",
                "Bombs clamped to the side rails of deep space. They never leave their rail: the core arms as " +
                "you pass, a line blinks across the lane at a slant, then a laser burns it - be off that line.",
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
                "Neon pixel ice mine: clamp and steel lugs round a blue crystal sphere, a snowflake core that bursts into a frost star.",
                "A geode the Frost rails grew around a cold star. Get close and the crystals start to grow - " +
                "then it lasers the lane, rail to rail, at a slant of its choosing."),
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
                "Neon pixel seed mine: a wooden clamp and vine-wrapped burr with magenta thorns, a lime core that bursts in leaves.",
                "A seed burr dangling from the rail vines. When its husk splits and the seams glow, it's about " +
                "to fire a laser across the lane, at any angle - it rides up the rail to keep pace with you, fires twice, then drops away."),
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
                "Neon pixel magma mine: a basalt clamp and sphere cracked with lava, an orange core that erupts in a sun burst.",
                "A pot of magma on a chain. It simmers as you pass, then a laser boils across the lane - twice, at two angles. Don't linger."),
            Def("ember_big", "Magma Skull", B, 3, 0, "enemy_ember_big", Magma,
                "A horned basalt skull whose furnace jaw glows behind a grille and drops open.",
                "A skull of cooled lava with a furnace for a jaw. The grille glows brighter right before it " +
                "drops open."),
            Def("ember_fighter_1", "Cinder", F, 3, 1, "enemy_ember_fighter_1", Magma,
                "Charred arrowhead with molten vents and a magenta visor.",
                "A scorched little dart that leaves a smell of burnt metal everywhere it goes."),
            Def("ember_fighter_2", "Scorch", F, 3, 2, "enemy_ember_fighter_2", Magma,
                "Forked char claw with four molten vents and a sodium core.",
                "Vents along the hull glow white-hot when it winds up. Hot hull, short temper."),
            Def("ember_fighter_3", "Brand", F, 3, 3, "enemy_ember_fighter_3", Magma,
                "Forked char claw with flickering flame fins.",
                "It flies with flames for fins and brands anything that doesn't move."),
            Def("ember_fighter_4", "Pyre", F, 3, 4, "enemy_ember_fighter_4", Magma,
                "Horned, flame-finned heavy with a sodium core.",
                "The fire fleet's champion: horns, flames and a core like a small sun. Shield up, grin on."),
            Def("ember_chaser", "Cinder Fang", C, 3, 0, "enemy_ember_chaser", Magma,
                "A wide salamander head whose upper and lower jaws hinge apart to show a molten throat.",
                "It climbs out of the lava glow below and gapes its molten jaws at your heels until it " +
                "overheats."),
            Def("ember_alien", "Ember Imp", A, 3, 0, "enemy_ember_alien", Magma,
                "A living flame wearing a horned basalt mask with two magenta eyes.",
                "Little flames in basalt masks, grinning in formation. They flare up when you get close."),

            // ================================================================ TIDE
            Def("tide_rock_brain", "Brain Coral", R, 4, 1, "hazard_tide_rock_brain", Ice,
                "Rounded maze-fold coral boulder pitted with barnacles, faint mint cracks.",
                "A reef that outgrew its ocean. The folds are full of barnacles and the cracks glow when " +
                "something stirs them. It tumbles slow and wide - give it the room it asks for."),
            Def("tide_rock_staghorn", "Staghorn Spire", R, 4, 2, "hazard_tide_rock_staghorn", Ice,
                "Upright branched reef blade with rusted brass tips and glowing polyps.",
                "A branch of drowned coral sharp enough to open a hull. It slices across the lane on one " +
                "slanted line, polyps lit, so you can see it coming."),
            Def("tide_rock_urchin", "Spine Urchin", R, 4, 3, "hazard_tide_rock_urchin", Ice,
                "Round shell of uneven bone-white iron spines around a mint core.",
                "An urchin with iron for spines and a lamp for a heart. It spins as it sinks and arrives " +
                "sooner than it looks."),
            Floating(Def("tide_rock_islet", "Kelp Islet", R, 4, 4, "hazard_tide_rock_islet", Ice,
                "Floating reef slab under a kelp cap, a tide pool on top, roots and drips hanging below.",
                "A slab of reef that floated off with its tide pool and its kelp still on it. It dips and " +
                "sways, dripping, wide and slow.")),
            Def("tide_mine", "Limpet Mine", M, 4, 0, "hazard_tide_mine", Mine,
                "Neon pixel sea-mine: a barnacled iron sphere with four pearl horns on a brass clamp, a mint eye that wakes, charges and bursts in shell shards.",
                "A limpet on the rail, horned and patient. When the eye flares mint a line blinks across " +
                "the lane, then a pressure jet burns it - twice, at two angles. Be off the line."),
            Def("tide_big", "Nautilus Bulwark", B, 4, 0, "enemy_tide_big", Ice,
                "A huge layered spiral shell of plates and barnacles with tentacle pipes, its shell opening on a mint-lit maw.",
                "A shell-plated hulk with a lantern in its mouth. When the shell cracks open it fans pearl " +
                "shots across the water - stand between them."),
            Def("tide_fighter_1", "Remora", F, 4, 1, "enemy_tide_fighter_1", Ice,
                "Small suckerfish drone with an oval back disc, paired fins and a mint eye.",
                "A little drone that clings to bigger things. It drifts alongside, then lets go and darts " +
                "straight at you - and never steers."),
            Def("tide_fighter_2", "Needlefish", F, 4, 2, "enemy_tide_fighter_2", Ice,
                "Narrow skiff with a needle-beak cannon, a dorsal blade and paired fins.",
                "A long beak and a short temper. It lines up above you and spits quick bolts straight down."),
            Def("tide_fighter_3", "Lantern Angler", F, 4, 3, "enemy_tide_fighter_3", Ice,
                "Armoured anglerfish gunship: hinged jaw of pearl teeth, a curved stalk carrying a bright mint lure.",
                "It hangs in the dark behind its lure. When the lamp flares it has picked you out - move " +
                "after the light comes on."),
            Def("tide_fighter_4", "Hammerhead", F, 4, 4, "enemy_tide_fighter_4", Ice,
                "Broad transverse hammer hull with twin tip cannons, plated gills and tail.",
                "The deep fleet's heavy: a hammer of plate with a cannon on each end. It parks and rings " +
                "itself in shots - the gaps open with distance."),
            Def("tide_chaser", "Wire Eel", C, 4, 0, "enemy_tide_chaser", Ice,
                "Long segmented iron-and-brass mechanical eel, a sharp downward head, a mint eye and thruster fins.",
                "It slips up out of the dark in an S, coils and whips forward again. Slide sideways and let " +
                "it overshoot."),
            // (the jelly's bell and tendrils draw 0.53 u of its cell: shown 17% larger, 0.62 u, inside the Alien role's 0.64 +/-15%)
            Scaled(Def("tide_alien", "Glow Jelly", A, 4, 0, "enemy_tide_alien", Ice,
                "Translucent mint bell in a brass collar trailing glowing tendrils; a living jelly drone.",
                "Jellies in a brass collar, drifting in lines with their lights on. They pulse as they sink - " +
                "slip past on the down-beat."), 1.17f),
        };
    }
}
