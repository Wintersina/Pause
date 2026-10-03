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
    // Not even listed (no "???" card, not in the totals) until discovered:
    // the end-of-level bosses.
    public readonly bool secret;

    readonly Func<Sprite> resolve;
    Sprite cached;

    public CodexEntry(string id, string name, CodexCategory category, Func<Sprite> sprite, string lore,
                      string[] matches = null, bool round = false, bool secret = false)
    {
        this.id = id;
        this.name = name;
        this.category = category;
        this.resolve = sprite;
        this.lore = lore;
        this.matches = matches ?? new string[0];
        this.round = round;
        this.secret = secret;
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
            // The spawner's Resources/Prefabs/Enemies families are added from
            // the live folder below (SpawnTableEntries); these two are scene-
            // wired or behaviour-defined.
            new CodexEntry("enemy_chaser", "Hunter", CodexCategory.Enemies, () => ChaserSprite(),
                "A Crimson hull that learned a nasty trick: it climbs up from below and tails you for a few " +
                "seconds before losing interest. Keep sliding sideways and it overshoots."),
            new CodexEntry("enemy_alien", "Invader", CodexCategory.Enemies, () => FirstSprite("Prefabs/alien1", "alien"),
                "Pixel-perfect pests that travel in lines of up to four, wiggling like they own the place. " +
                "There's always a gap - find it. Smash them while shielded for alien-hunter medals.",
                new[] { "alien" }),

            // ------------------------------------------------------------ Hazards
            new CodexEntry("hazard_mine", "Rail Mine", CodexCategory.Hazards, () => RailBombSprites.FrameForWorld(0, 0),
                "Bombs clamped to the side rails of every world, painted to match the local scenery. They " +
                "never leave their rail, so hug the middle lanes when one blinks into view.",
                new[] { "mine" }),
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

        // ------------------------------------------------------------- Bosses
        // Secret: hidden from the codex until the pilot first meets one at
        // the end of its world (BossEncounter discovers it by id).
        int enemyEnd = 0;
        for (int i = 0; i < list.Count; i++) if (list[i].category == CodexCategory.Enemies) enemyEnd = i + 1;
        var bosses = new List<CodexEntry>();
        foreach (var boss in BossCatalog.All)
        {
            var def = boss;
            bosses.Add(new CodexEntry(def.id, BossDisplayName(def.name), CodexCategory.Enemies,
                () => BossArt.Body(def, BossArt.Portrait), def.lore, null, false, secret: true));
        }
        list.InsertRange(enemyEnd, bosses);

        // -------------------------------------------------------------- Ships
        // Every roster ship (ShipId.All), keyed by its stable art key.
        foreach (int ship in ShipId.All)
        {
            int index = ship;
            string key = ShipId.KeyOf(index) ?? index.ToString();
            string lore;
            if (!ShipLore.TryGetValue(key, out lore))
                lore = "A hull from the space dock, ready to carry a lost pilot a little closer to home.";
            list.Add(new CodexEntry(ShipPrefix + key, ShipId.NameOf(index), CodexCategory.Ships,
                () => shopingShips.SpriteFor(index), lore));
        }

        list.InsertRange(EnemyInsertIndex(list), SpawnTableEntries());
        return list.ToArray();
    }

    // Stable art key (ShipId.KeyOf) -> lore.
    static readonly Dictionary<string, string> ShipLore = new Dictionary<string, string>
    {
        { "NeonComet", "The very ship that fell into the black hole - scorched, stubborn and still flying. Not fancy, but it got him this far." },
        { "VoltViper", "A wiry interceptor humming with stored lightning. Bought with star dust and nerves." },
        { "SolarFang", "Built around a captured sunspot, it runs hot and bites hard." },
        { "CrimsonHalo", "A heavy cruiser with a ring of red light around its nose. Slow to love, impossible to forget." },
        { "IonLancer", "Long, sleek and pointed - a flying spear for pilots who prefer straight lines." },
        { "JadePhantom", "A ghostly green hull that seems to blur at the edges, as if it's half in another place." },
        { "GoldWarden", "Gold plating, heavy armour and a very smug captain. The pride of any hangar." },
        { "Lightning", "A classic arcade fighter from an older age of space, as fast as its name." },
        { "Ligher", "A light-frame courier that slips between rocks like it's late for something." },
        { "Paranoid", "Wide wings, a hundred sensors and a pilot who trusts none of them." },
        { "Ninja", "A dark, quiet hull that strikes before anyone sees it coming." },
        { "Saboteur", "Built for mischief. Its owner insists the dents are 'battle intelligence'." },
        { "UFO", "Nobody knows who built this saucer, and it isn't telling." },
        { "Dove", "A peaceful-looking ship with a not-so-peaceful ultimate." },
        { "Turtle", "Never slow, always tough: a shell built to shrug off space itself." },
    };

    // ---------------------------------------------------------------------
    // Live spawn table: Resources/Prefabs/Enemies
    // ---------------------------------------------------------------------

    // The folder enmiesOnBoard loads its extra enemies and meteors from.
    public const string SpawnFolder = "Prefabs/Enemies";

    // Known families: how a group of spawn-table prefabs is named and told
    // about. A family is a prefab name with its trailing variant number
    // dropped ("kn_enemyBlack3" -> "knenemyblack"); several families can share
    // one entry (brown and grey meteors of a size). An entry only exists while
    // at least one of its families is actually in the folder, and a family
    // nobody wrote lore for still gets an entry of its own (see Fallback), so
    // redrawn, renamed or brand-new enemies are always in the codex.
    struct Family
    {
        public string id, name, lore;
        public CodexCategory category;
        public string[] keys;
        public Family(string id, string name, CodexCategory category, string lore, params string[] keys)
        {
            this.id = id; this.name = name; this.category = category; this.lore = lore; this.keys = keys;
        }
    }

    static readonly Family[] KnownFamilies =
    {
        new Family("enemy_black", "Shadow Wing", CodexCategory.Enemies,
            "Matte-black scouts that patrol the outer lanes. They're the first to spot a lost pilot and " +
            "the first to try ramming him. Dodge them, or plough through with a blue atom's shield.", "knenemyblack"),
        new Family("enemy_blue", "Cobalt Patrol", CodexCategory.Enemies,
            "Border guards of a sector that never asked for visitors. They fly in tidy lines and expect " +
            "you to move. Shielded, you're the one who doesn't have to.", "knenemyblue"),
        new Family("enemy_green", "Venom Raider", CodexCategory.Enemies,
            "Scrappy raiders who strip anything that drifts too close - hulls included. They show up once " +
            "the sky gets crowded, and every one you smash pays a pinch of star dust.", "knenemygreen"),
        new Family("enemy_red", "Crimson Ace", CodexCategory.Enemies,
            "The universe's top guns, saved for the deepest stretch of a flight. Fast, mean and very proud " +
            "of their paint job. Meet them shielded or not at all.", "knenemyred"),
        new Family("hazard_meteor_tiny", "Pebble Meteor", CodexCategory.Hazards,
            "Barely bigger than the ship's cup holder, yet still enough to dent a hull. They rattle through " +
            "the busier sectors.", "knmeteorbrowntiny", "knmeteorgreytiny"),
        new Family("hazard_meteor_small", "Small Meteor", CodexCategory.Hazards,
            "Fist-sized chunks of a planet that didn't make it. Easy to dodge alone, nasty in a crowd.",
            "knmeteorbrownsmall", "knmeteorgreysmall"),
        new Family("hazard_meteor_med", "Medium Meteor", CodexCategory.Hazards,
            "Tumbling boulders that spin as they fall. Their lumpy edges reach further than they look, " +
            "so give them room.", "knmeteorbrownmed", "knmeteorgreymed"),
        new Family("hazard_meteor_big", "Big Meteor", CodexCategory.Hazards,
            "Mountains with no planet to sit on. They fill a whole lane - go around, or go through with a " +
            "shield up and a grin on.", "knmeteorbrownbig", "knmeteorgreybig"),
    };

    // "kn_enemyBlack3" -> "knenemyblack"; "kn_meteorGrey_big2" -> "knmeteorgreybig".
    public static string FamilyOf(string prefabName)
    {
        string key = Codex.Normalise(prefabName);
        int end = key.Length;
        while (end > 0 && char.IsDigit(key[end - 1])) end--;
        return end > 0 ? key.Substring(0, end) : key;
    }

    static List<CodexEntry> SpawnTableEntries()
    {
        var prefabs = new List<GameObject>(Resources.LoadAll<GameObject>(SpawnFolder));
        prefabs.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        // family -> its prefabs, in name order
        var families = new Dictionary<string, List<GameObject>>();
        var order = new List<string>();
        foreach (var go in prefabs)
        {
            if (go == null) continue;
            string family = FamilyOf(go.name);
            List<GameObject> members;
            if (!families.TryGetValue(family, out members))
            {
                families[family] = members = new List<GameObject>();
                order.Add(family);
            }
            members.Add(go);
        }

        var result = new List<CodexEntry>();
        var claimed = new HashSet<string>();
        foreach (var known in KnownFamilies)
        {
            var present = new List<string>();
            foreach (string k in known.keys)
                if (families.ContainsKey(k)) { present.Add(k); claimed.Add(k); }
            if (present.Count == 0) continue;
            var art = families[present[0]][0];
            result.Add(new CodexEntry(known.id, known.name, known.category, () => SpriteOf(art), known.lore,
                                      present.ToArray()));
        }
        foreach (string family in order)
        {
            if (claimed.Contains(family)) continue;
            var art = families[family][0];
            bool meteor = family.Contains("meteor") || art.CompareTag("Astr");
            result.Add(new CodexEntry((meteor ? "hazard_" : "enemy_") + family, Fallback.NameFor(art.name),
                                      meteor ? CodexCategory.Hazards : CodexCategory.Enemies,
                                      () => SpriteOf(art), meteor ? Fallback.HazardLore : Fallback.EnemyLore,
                                      new[] { family }));
        }
        return result;
    }

    public static string FallbackName(string prefabName) { return Fallback.NameFor(prefabName); }

    // "HOARFROST LEVIATHAN" -> "Hoarfrost Leviathan"; "THE BLOOM QUEEN" -> "The Bloom Queen".
    public static string BossDisplayName(string upper)
    {
        var words = upper.ToLowerInvariant().Split(' ');
        for (int i = 0; i < words.Length; i++)
            if (words[i].Length > 0) words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
        return string.Join(" ", words);
    }

    // Entries for spawn-table families nobody has written lore for yet.
    static class Fallback
    {
        public const string EnemyLore =
            "A hostile hull the pilot hadn't logged before. It flies the same lanes you do and won't move " +
            "for you - dodge it, or smash through with a shield up.";
        public const string HazardLore =
            "More rubble from a universe that keeps falling apart. Don't let it touch the hull.";

        // "kn_enemyPurple2" -> "Enemy Purple"
        public static string NameFor(string prefabName)
        {
            string raw = prefabName;
            if (raw.StartsWith("kn_", StringComparison.Ordinal)) raw = raw.Substring(3);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < raw.Length; i++)
            {
                char ch = raw[i];
                if (char.IsDigit(ch)) continue;
                if (ch == '_' || ch == '-') { sb.Append(' '); continue; }
                if (i > 0 && char.IsUpper(ch) && sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
                sb.Append(sb.Length == 0 || sb[sb.Length - 1] == ' ' ? char.ToUpperInvariant(ch) : ch);
            }
            return sb.ToString().Trim();
        }
    }

    // Spawn-table families go right after the scene-wired enemies.
    static int EnemyInsertIndex(List<CodexEntry> list)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i].category == CodexCategory.Enemies) return i;
        return list.Count;
    }

    static Sprite SpriteOf(GameObject go)
    {
        var sr = go != null ? go.GetComponentInChildren<SpriteRenderer>(true) : null;
        return sr != null ? sr.sprite : null;
    }

    // The hunter borrows a hull from the spawn table (enmiesOnBoard.chaser);
    // prefer the one it uses today, else any hull of its family, else any.
    static Sprite ChaserSprite()
    {
        return FirstSprite(SpawnFolder + "/kn_enemyRed5", "knenemyred");
    }

    static Sprite FirstSprite(string path, string familyPrefix)
    {
        var sprite = Prefab(path);
        if (sprite != null) return sprite;
        GameObject any = null;
        foreach (var go in Resources.LoadAll<GameObject>(path.Substring(0, path.LastIndexOf('/'))))
        {
            if (go == null || SpriteOf(go) == null) continue;
            if (Codex.Normalise(go.name).StartsWith(familyPrefix, StringComparison.Ordinal)) return SpriteOf(go);
            if (any == null) any = go;
        }
        return SpriteOf(any);
    }


    public static int ShipIndex(string id)
    {
        if (id == null || !id.StartsWith(ShipPrefix, StringComparison.Ordinal)) return -1;
        string key = id.Substring(ShipPrefix.Length);
        int index = ShipId.FromKey(key);
        if (index == ShipId.None && int.TryParse(key, out index) && !ShipId.IsValid(index)) index = ShipId.None;
        return index == ShipId.None ? -1 : index;
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

    // A square from the middle of a world's tall scrolling sky tile (the far
    // layer of its animated WorldBackdrop set; see BackdropCatalog).
    static Sprite Backdrop(string folder)
    {
        return SquareOf(Resources.Load<Texture2D>(BackdropCatalog.Folder(folder) + "sky"));
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
