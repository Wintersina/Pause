using System;
using System.Collections.Generic;
using UnityEngine;

// Every enemy, hazard, pickup, world and ship the pilot can meet, in one
// table: id, display name, category, a sprite resolver that loads the real
// in-game art (so the codex always matches what is on screen) and a short
// bit of lore.
//
// `matches` are normalised prefab-name prefixes (see Codex.Normalise): any
// spawned object whose name starts with one of them belongs to the entry, so
// the gameplay hooks can stay one line -- Codex.Discover(hit.gameObject) --
// and still resolve "kn_enemyBlack3(Clone)" to the Shadow Wing entry.
public enum CodexCategory
{
    Log,
    Enemies,
    Hazards,
    Atoms,
    Worlds,
    Ships,
}

public sealed class CodexEntry
{
    public readonly string id;
    public readonly string name;
    public readonly CodexCategory category;
    public readonly string lore;
    public readonly string[] matches;
    // Draw the art inside a circular mask (the worlds' backdrops are
    // rectangular textures; round reads as a planet).
    public readonly bool round;

    readonly Func<Sprite> resolve;
    Sprite cached;

    public CodexEntry(string id, string name, CodexCategory category, Func<Sprite> sprite, string lore,
                      string[] matches = null, bool round = false)
    {
        this.id = id;
        this.name = name;
        this.category = category;
        this.resolve = sprite;
        this.lore = lore;
        this.matches = matches ?? new string[0];
        this.round = round;
    }

    // Resolved on first use and cached. A Sprite.Create()d sprite can be
    // destroyed under us when the editor swaps scenes; Unity's null check
    // catches that and the art is simply resolved again.
    public Sprite Sprite
    {
        get
        {
            if (cached == null && resolve != null) cached = resolve();
            return cached;
        }
    }

    // Subtitle line for the detail view (a ship's ultimate), or null.
    public string Subtitle
    {
        get
        {
            if (category != CodexCategory.Ships) return null;
            int index = CodexCatalogue.ShipIndex(id);
            if (index < 0) return null;
            return "ULTIMATE  " + ShipPowerTable.DisplayName(ShipPowerTable.For(index));
        }
    }
}

public static class CodexCatalogue
{
    public const string ShipPrefix = "ship_";
    public const string WorldPrefix = "world_";
    public const string PortalId = "world_portal";

    // World ids, index-aligned with WorldManager.Worlds.
    public static readonly string[] WorldIds = { "world_space", "world_frost", "world_verdant", "world_ember" };

    static CodexEntry[] all;

    public static CodexEntry[] All
    {
        get
        {
            if (all == null) all = Build();
            return all;
        }
    }

    static CodexEntry[] Build()
    {
        var list = new List<CodexEntry>
        {
            // ---------------------------------------------------------------- Log
            new CodexEntry("log_pilot", "Pilot's Log", CodexCategory.Log, () => Art("cx_blackhole"),
                "Day one of being lost: a routine hop went sideways and a black hole swallowed me whole. " +
                "It spat me out on the far side of nowhere with something new humming in my hull - the power " +
                "of a wormhole. Home is out there somewhere. I'm flying until I find it."),
            new CodexEntry("log_wormhole", "Wormhole Gift", CodexCategory.Log, () => Art("cx_wormhole"),
                "The black hole kept my map but left me its wormhole. Lift your finger and time folds shut - " +
                "the whole universe pauses while you think. Pauses run out, so spend them wisely, and when your " +
                "ultimate fires, time slows to a crawl."),

            // ------------------------------------------------------------ Enemies
            new CodexEntry("enemy_black", "Shadow Wing", CodexCategory.Enemies, () => Prefab("Prefabs/Enemies/kn_enemyBlack1"),
                "Matte-black scouts that patrol the outer lanes. They're the first to spot a lost pilot and " +
                "the first to try ramming him. Dodge them, or plough through with a blue atom's shield.",
                new[] { "knenemyblack" }),
            new CodexEntry("enemy_blue", "Cobalt Patrol", CodexCategory.Enemies, () => Prefab("Prefabs/Enemies/kn_enemyBlue1"),
                "Border guards of a sector that never asked for visitors. They fly in tidy lines and expect " +
                "you to move. Shielded, you're the one who doesn't have to.",
                new[] { "knenemyblue" }),
            new CodexEntry("enemy_green", "Venom Raider", CodexCategory.Enemies, () => Prefab("Prefabs/Enemies/kn_enemyGreen1"),
                "Scrappy raiders who strip anything that drifts too close - hulls included. They show up once " +
                "the sky gets crowded, and every one you smash pays a pinch of star dust.",
                new[] { "knenemygreen" }),
            new CodexEntry("enemy_red", "Crimson Ace", CodexCategory.Enemies, () => Prefab("Prefabs/Enemies/kn_enemyRed1"),
                "The universe's top guns, saved for the deepest stretch of a flight. Fast, mean and very proud " +
                "of their paint job. Meet them shielded or not at all.",
                new[] { "knenemyred" }),
            new CodexEntry("enemy_chaser", "Hunter", CodexCategory.Enemies, () => Prefab("Prefabs/Enemies/kn_enemyRed5"),
                "A Crimson hull that learned a nasty trick: it climbs up from below and tails you for a few " +
                "seconds before losing interest. Keep sliding sideways and it overshoots."),
            new CodexEntry("enemy_alien", "Invader", CodexCategory.Enemies, () => Prefab("Prefabs/alien1"),
                "Pixel-perfect pests that travel in lines of up to four, wiggling like they own the place. " +
                "There's always a gap - find it. Smash them while shielded for alien-hunter medals.",
                new[] { "alien" }),

            // ------------------------------------------------------------ Hazards
            new CodexEntry("hazard_mine", "Rail Mine", CodexCategory.Hazards, () => RailBombSprites.FrameForWorld(0, 0),
                "Bombs clamped to the side rails of every world, painted to match the local scenery. They " +
                "never leave their rail, so hug the middle lanes when one blinks into view.",
                new[] { "mine" }),
            new CodexEntry("hazard_meteor_tiny", "Pebble Meteor", CodexCategory.Hazards, () => Prefab("Prefabs/Enemies/kn_meteorBrown_tiny1"),
                "Barely bigger than the ship's cup holder, yet still enough to dent a hull. Brown or grey, " +
                "they rattle through the busier sectors.",
                new[] { "knmeteorbrowntiny", "knmeteorgreytiny" }),
            new CodexEntry("hazard_meteor_small", "Small Meteor", CodexCategory.Hazards, () => Prefab("Prefabs/Enemies/kn_meteorGrey_small1"),
                "Fist-sized chunks of a planet that didn't make it. Easy to dodge alone, nasty in a crowd.",
                new[] { "knmeteorbrownsmall", "knmeteorgreysmall" }),
            new CodexEntry("hazard_meteor_med", "Medium Meteor", CodexCategory.Hazards, () => Prefab("Prefabs/Enemies/kn_meteorBrown_med1"),
                "Tumbling boulders that spin as they fall. Their lumpy edges reach further than they look, " +
                "so give them room.",
                new[] { "knmeteorbrownmed", "knmeteorgreymed" }),
            new CodexEntry("hazard_meteor_big", "Big Meteor", CodexCategory.Hazards, () => Prefab("Prefabs/Enemies/kn_meteorGrey_big1"),
                "Mountains with no planet to sit on. They fill a whole lane - go around, or go through with a " +
                "shield up and a grin on.",
                new[] { "knmeteorbrownbig", "knmeteorgreybig" }),
            // The eight classic aestroid_* prefabs share just three images
            // (their names predate a re-skin), so the entries follow the art
            // the player actually sees, matched by exact prefab name.
            new CodexEntry("hazard_rock_cluster", "Cluster Rock", CodexCategory.Hazards, () => Prefab("Prefabs/aestroid_brown"),
                "Three boulders welded together by some ancient crash, tumbling as one. Wider than it looks - " +
                "give it room, or smash it shielded for asteroid medals.",
                new[] { "aestroidbrown", "aestroiddark1", "aestroidgraycrooked1" }),
            new CodexEntry("hazard_rock_dark", "Coal Rock", CodexCategory.Hazards, () => Prefab("Prefabs/aestroid_dark"),
                "Dull, dark asteroids that hide against the void until the last second. Watch for the stars " +
                "they blot out.",
                new[] { "aestroiddark", "aestroidgay3" }),
            new CodexEntry("hazard_rock_crater", "Crater Rock", CodexCategory.Hazards, () => Prefab("Prefabs/aestroid_gay_1"),
                "Pockmarked by a million tiny collisions, these rocks have survived everything space threw at " +
                "them. Don't be the next dent.",
                new[] { "aestroidbrown1", "aestroidgay1", "aestroidgraycrooked2" }),

            // -------------------------------------------------------------- Atoms
            new CodexEntry("atom_stardust", "Star Dust", CodexCategory.Atoms, () => Prefab("Prefabs/smStar_1"),
                "Glittering crumbs of collapsed stars, scattered in long trails. Scoop them up - star dust buys " +
                "new ships, and each one charges your ultimate a little faster.",
                new[] { "smstar" }),
            new CodexEntry("atom_bigstar", "Bright Star", CodexCategory.Atoms, () => Prefab("Prefabs/LargeStar_1"),
                "A fat, bright clump of star dust worth twice a small one. Worth a swerve.",
                new[] { "largestar" }),
            new CodexEntry("atom_blue", "Blue Atom", CodexCategory.Atoms, () => Prefab("Prefabs/atom3a"),
                "Pure forward momentum. Grab one and a shield snaps around your hull for a few seconds - plough " +
                "through anything while the world speeds up around you. Rare, so make it count.",
                new[] { "atom3a" }),
            new CodexEntry("atom_red", "Red Atom", CodexCategory.Atoms, () => Prefab("Prefabs/pauseAtom"),
                "Condensed wormhole energy. Each one adds a pause to your stash, so you can freeze the " +
                "universe one more time.",
                new[] { "pauseatom" }),
            new CodexEntry("atom_green", "Green Atom", CodexCategory.Atoms, () => Texture("Pickups/heal_atom_green"),
                "A rare repair kit from who-knows-where. It patches one point of hull damage, and only turns up " +
                "when you're already banged up.",
                new[] { Codex.Normalise(HealAtom.ObjectName) }),

            // ------------------------------------------------------------- Worlds
            new CodexEntry(PortalId, "Wormhole Portal", CodexCategory.Worlds, () => TeleportPortalSprites.FrameAt(0),
                "Fly long enough and the wormhole inside you tears a door in space. Dive through it to jump to " +
                "the next world - one step closer to home. Miss it and another opens a little later.",
                new[] { "~portal" }),
            new CodexEntry(WorldIds[0], "Deep Space", CodexCategory.Worlds, () => SpaceBackdrop(),
                "Where the black hole dropped you: cold, quiet and full of things that want you gone. Every " +
                "journey home starts here.", null, round: true),
            new CodexEntry(WorldIds[1], "Frost", CodexCategory.Worlds, () => Backdrop("Frost"),
                "A frozen world of ice cliffs and glittering walls. Things move faster out here, and so must you.",
                null, round: true),
            new CodexEntry(WorldIds[2], "Verdant", CodexCategory.Worlds, () => Backdrop("Verdant"),
                "A jungle planet buzzing with life - and with enemies. The pace climbs again, but the air smells " +
                "almost like home.", null, round: true),
            new CodexEntry(WorldIds[3], "Ember", CodexCategory.Worlds, () => Backdrop("Ember"),
                "Lava, ash and the fastest skies yet. If home is past this, he'll fly through fire to reach it.",
                null, round: true),
        };

        // -------------------------------------------------------------- Ships
        // Index-aligned with shopingShips.Roster (0 is the unused "non" slot).
        string[] shipLore =
        {
            null,
            "The very ship that fell into the black hole - scorched, stubborn and still flying. Not fancy, but it got him this far.",
            "A wiry interceptor humming with stored lightning. Bought with star dust and nerves.",
            "Built around a captured sunspot, it runs hot and bites hard.",
            "A heavy cruiser with a ring of red light around its nose. Slow to love, impossible to forget.",
            "Long, sleek and pointed - a flying spear for pilots who prefer straight lines.",
            "A ghostly green hull that seems to blur at the edges, as if it's half in another place.",
            "Gold plating, heavy armour and a very smug captain. The pride of any hangar.",
            "A classic arcade fighter from an older age of space, as fast as its name.",
            "A light-frame courier that slips between rocks like it's late for something.",
            "Wide wings, a hundred sensors and a pilot who trusts none of them.",
            "A dark, quiet hull that strikes before anyone sees it coming.",
            "Built for mischief. Its owner insists the dents are 'battle intelligence'.",
            "Nobody knows who built this saucer, and it isn't telling.",
            "A peaceful-looking ship with a not-so-peaceful ultimate.",
            "Never slow, always tough: a shell built to shrug off space itself.",
        };
        for (int i = 1; i < shipLore.Length && i < shopingShips.Roster.Length; i++)
        {
            int index = i;
            list.Add(new CodexEntry(ShipPrefix + index, shopingShips.Roster[index], CodexCategory.Ships,
                () => shopingShips.SpriteFor(index), shipLore[index]));
        }

        return list.ToArray();
    }

    public static int ShipIndex(string id)
    {
        if (id == null || !id.StartsWith(ShipPrefix, StringComparison.Ordinal)) return -1;
        int index;
        return int.TryParse(id.Substring(ShipPrefix.Length), out index) ? index : -1;
    }

    public static int WorldIndex(string id)
    {
        return Array.IndexOf(WorldIds, id);
    }

    // ---- Sprite resolvers: the real in-game art ----

    static Sprite Prefab(string path)
    {
        var go = Resources.Load<GameObject>(path);
        var sr = go != null ? go.GetComponentInChildren<SpriteRenderer>(true) : null;
        return sr != null ? sr.sprite : null;
    }

    static Sprite Art(string name)
    {
        return Resources.Load<Sprite>("Codex/" + name);
    }

    static Sprite Texture(string path)
    {
        var tex = Resources.Load<Texture2D>(path);
        if (tex == null) return null;
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, .5f), 100f);
    }

    // A square from the middle of a world's tall scrolling backdrop.
    static Sprite Backdrop(string folder)
    {
        return SquareOf(Resources.Load<Texture2D>("Worlds/" + folder + "/backdrop"));
    }

    // Space keeps the scene's own authored backdrop (WorldTheme leaves its
    // resourceFolder empty), which isn't under Resources; CodexArtRefs holds a
    // reference to that same texture so the codex can show it.
    static Sprite SpaceBackdrop()
    {
        var refs = Resources.Load<CodexArtRefs>("Codex/CodexArtRefs");
        return SquareOf(refs != null ? refs.spaceBackdrop : null);
    }

    static Sprite SquareOf(Texture2D tex)
    {
        if (tex == null) return null;
        float side = Mathf.Min(tex.width, tex.height);
        var rect = new Rect((tex.width - side) * .5f, (tex.height - side) * .5f, side, side);
        return Sprite.Create(tex, rect, new Vector2(.5f, .5f), 100f);
    }
}
