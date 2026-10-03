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
// and still resolve "aestroid_brown(Clone)" to the Cluster Rock entry.
//
// Enemies and hazards come straight from EnemyRoster (every world's cast);
// spawned roster enemies carry an EnemyIdentity, which Codex.IdFor checks
// first, so per-world aliens and mines (all named "alien1" / "mine" for the
// gameplay matchers) still resolve to their own entries.
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

        list.InsertRange(RosterInsertIndex(list), RosterEntries());
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
    // Enemies and hazards: EnemyRoster, all four worlds
    // ---------------------------------------------------------------------

    // One entry per roster enemy: enemies first, then hazards (rocks, mines),
    // each in world order. Matches are the spawned object's name (its art
    // key) plus any older prefab names it replaces.
    static List<CodexEntry> RosterEntries()
    {
        var enemies = new List<CodexEntry>();
        var hazards = new List<CodexEntry>();
        foreach (var def in EnemyRoster.All)
        {
            var d = def;
            var matches = new List<string> { Codex.Normalise(d.key) };
            if (d.legacyNames != null)
                foreach (string legacy in d.legacyNames) matches.Add(Codex.Normalise(legacy));
            var entry = new CodexEntry(d.codexId, d.displayName,
                                       d.IsHazard ? CodexCategory.Hazards : CodexCategory.Enemies,
                                       () => EnemyArt.Frame(d, 0), d.lore, matches.ToArray());
            (d.IsHazard ? hazards : enemies).Add(entry);
        }
        enemies.AddRange(hazards);
        return enemies;
    }

    // "kn_enemyBlack3" -> "knenemyblack"; "kn_meteorGrey_big2" -> "knmeteorgreybig".
    public static string FamilyOf(string prefabName)
    {
        string key = Codex.Normalise(prefabName);
        int end = key.Length;
        while (end > 0 && char.IsDigit(key[end - 1])) end--;
        return end > 0 ? key.Substring(0, end) : key;
    }

    public static string FallbackName(string prefabName) { return Fallback.NameFor(prefabName); }

    // A readable name from a prefab name, for objects outside the roster.
    static class Fallback
    {
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

    // The roster goes between the Pilot's Log and the atoms.
    static int RosterInsertIndex(List<CodexEntry> list)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i].category == CodexCategory.Atoms) return i;
        return list.Count;
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
