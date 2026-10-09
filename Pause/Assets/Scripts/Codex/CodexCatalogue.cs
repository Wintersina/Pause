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

    // Subtitle line for the detail view, or null: a ship's attack + secret
    // power, a boss's title, an elite's world.
    public string Subtitle
    {
        get
        {
            if (category == CodexCategory.Ships)
            {
                int index = CodexCatalogue.ShipIndex(id);
                return index < 0 ? null : ShipLoadoutTable.Summary(index);
            }
            if (category != CodexCategory.Enemies) return null;
            var boss = BossCatalog.Find(id);
            if (boss != null) return boss.title;
            var elite = EliteCatalog.FindByCodexId(id);
            if (elite != null && elite.WorldIndex >= 0 && elite.WorldIndex < EnemyRoster.WorldKeys.Length)
                return EnemyRoster.WorldKeys[elite.WorldIndex].ToUpperInvariant() + " ELITE";
            return null;
        }
    }
}

public static class CodexCatalogue
{
    public const string ShipPrefix = "ship_";
    public const string WorldPrefix = "world_";
    public const string PortalId = "world_portal";
    public const string VioletAtomId = "atom_violet";

    // What the codex quotes for the atoms' charge cuts, from the gameplay
    // values (ShipPowerController): the red atom's own cut, the shared atom
    // cut on the blue and green atoms, the violet capacitor's.
    public static string RedAtomCut { get { return Seconds(ShipPowerController.RedAtomCutSeconds); } }
    public static string BlueAtomCut { get { return Seconds(ShipPowerController.AtomCutSeconds); } }
    public static string GreenAtomCut { get { return Seconds(ShipPowerController.AtomCutSeconds); } }
    public static string CapacitorCut { get { return Seconds(ShipPowerController.CooldownAtomCutSeconds); } }
    public static string Seconds(float s) { return s.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " s"; }

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
                "the whole universe pauses while you think, and a touch somewhere else blinks your ship there. " +
                "Pauses run out, so spend them wisely - red atoms bring more."),

            // -------------------------------------------------------------- Atoms
            // Art: the pixel pickup's own idle frame (PickupArt), the drawing
            // the game shows -- the prefabs' authored sprites are replaced by
            // PickupFlipbook in flight. The green atom keeps its original.
            new CodexEntry("atom_stardust", "Star Dust", CodexCategory.Atoms,
                () => Pickup(PickupKind.DustSmall, "prefabs/smStar_1"),
                "Glittering crumbs of collapsed stars, scattered in long trails. Scoop them up - star dust buys " +
                "new ships and colours, and every crumb shaves a moment off your weapon's charge and feeds your " +
                "secret power.",
                new[] { "smstar" }),
            new CodexEntry("atom_bigstar", "Bright Star", CodexCategory.Atoms,
                () => Pickup(PickupKind.Dust, "prefabs/LargeStar_1"),
                "A fat, bright clump of star dust worth twice a small one. It charges your weapon and your secret " +
                "power just the same, so it's worth a swerve.",
                new[] { "largestar" }),
            new CodexEntry("atom_blue", "Blue Atom", CodexCategory.Atoms,
                () => Pickup(PickupKind.Shield, "prefabs/atom3a"),
                "Pure forward momentum. Grab one and a shield snaps around your hull for about six seconds - plough " +
                "through anything while the world speeds up around you, past the speed limit if you were already " +
                "at it. It also pays two star dust and cuts " +
                BlueAtomCut + " off your weapon's charge.",
                new[] { "atom3a" }),
            new CodexEntry("atom_red", "Red Atom", CodexCategory.Atoms,
                () => Pickup(PickupKind.Pause, "prefabs/pauseAtom"),
                "Condensed wormhole energy. Each one adds two pauses to your stash, fires a free shot of your " +
                "weapon and cuts " + RedAtomCut + " off its next charge.",
                new[] { "pauseatom" }),
            new CodexEntry("atom_green", "Green Atom", CodexCategory.Atoms, () => Texture("Pickups/heal_atom_green"),
                "A rare repair kit from who-knows-where: it patches one heart of hull damage and cuts " +
                GreenAtomCut + " off your weapon's charge. It only turns up when you're already banged up, and " +
                "never more than a couple of times a world.",
                new[] { Codex.Normalise(HealAtom.ObjectName) }),
            new CodexEntry(VioletAtomId, "Violet Atom", CodexCategory.Atoms,
                () => Pickup(PickupKind.Cooldown, "prefabs/cooldownAtom"),
                "A capacitor humming with stored lightning. Grab one and it dumps up to " + CapacitorCut +
                " into your weapon's charge - often enough to fire it on the spot. Only " +
                PickupRules.CooldownAtomsPerWorld + " drift through each world, so they're worth a detour.",
                new[] { "cooldownatom" }),

            // ------------------------------------------------------------- Worlds
            new CodexEntry(PortalId, "Wormhole Portal", CodexCategory.Worlds, () => TeleportPortalSprites.FrameAt(0),
                "Fly long enough and the wormhole inside you tears a door in space. Dive through it to jump to " +
                "the next world - one step closer to home. It waits for you, but the skies get meaner every " +
                "second you keep it waiting.",
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
                () => BossArt.Body(def, BossArt.Portrait), def.lore + "\n\n" + BossAttackLore(def),
                null, false, secret: true));
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
            lore += "\n\n" + LoadoutLore(index) + "\n" + LivesLore(index) + "\n" + ColoursLore(index);
            list.Add(new CodexEntry(ShipPrefix + key, ShipId.NameOf(index), CodexCategory.Ships,
                () => ShipHullArt.StockRest(index), lore));
        }

        list.InsertRange(RosterInsertIndex(list), RosterEntries());
        return list.ToArray();
    }

    // What the ship fights with, from ShipLoadoutTable: its attack (fires when
    // the charge meter fills) and its secret power (its own meter, fills from
    // star dust and kills, fires itself at the right moment).
    // How many hearts the hull flies with (ShipLives: its row in the dock grid) and
    // what its colours add (SkinHearts).
    public static string LivesLore(int ship)
    {
        int lives = ShipLives.Base(ship);
        int most = lives + SkinHearts.MostFromColours;
        return "HULL  " + lives + " " + SkinHearts.HeartsWord(lives) + " (up to " + most + " with its colours, " +
               (most + SkinHearts.AllSkinsBonus) + " with every skin)\n" + SkinHearts.LoreLine();
    }

    // Its colours (ShipSkins), what each one bought adds to the attack
    // (ShipWeaponUpgrades) and the start speed each colour flies at
    // (ShipStartSpeed). Spec lines, no sentence stops.
    public static string ColoursLore(int ship)
    {
        int n = ShipSkins.CountFor(ship);
        var names = new List<string>();
        var speeds = new List<string>();
        for (int skin = 0; skin < n; skin++)
        {
            names.Add(ShipSkins.Get(ship, skin).DisplayName);
            speeds.Add(ShipStartSpeed.HudFor(ship, skin).ToString());
        }
        var steps = new List<string>();
        for (int level = 1; level <= ShipWeaponUpgrades.MaxLevel; level++)
            steps.Add(ShipWeaponUpgrades.Step(ship, level).label);
        return "COLOURS  " + string.Join(" / ", names) + "\n" +
               "UPGRADES  each colour bought adds " + string.Join(", ", steps) + "\n" +
               "START SPEED  " + string.Join(" / ", speeds) + " by colour";
    }

    // A boss's attacks, one spec line each: what it fires and from which
    // part of its body (BossCatalog), then how the fight escalates.
    public static string BossAttackLore(BossDef boss)
    {
        var lines = new List<string>();
        foreach (var a in boss.attacks)
            lines.Add(a.name.ToUpperInvariant() + "  " + AttackWhat(a) + " from " + PartsOf(a));
        lines.Add("PHASES  one attack, then two, then all " + boss.attacks.Length + " faster");
        lines.Add("HEARTS  " + BossConfig.Hearts + " spin round it, all gone in " + BossConfig.HitPoints + " weapon hits");
        lines.Add("ENDS  break every heart (+" + ScoreRules.BossDestroyed +
                  ") or outlast it for " + Mathf.RoundToInt(BossConfig.FightSeconds) + " s (+" + ScoreRules.BossSurvived + ")");
        return string.Join("\n", lines);
    }

    static string AttackWhat(BossAttack a)
    {
        string shots = a.style == BossShotStyle.Shard ? "shards" : "bolts";
        string what;
        switch (a.kind)
        {
            case BossAttackKind.Aimed: what = (a.count > 1 ? "aimed spreads of " : "aimed ") + shots; break;
            case BossAttackKind.Fan: what = "fans of " + shots; break;
            case BossAttackKind.Lob: what = "hail lobbed onto your lanes"; break;
            default:
                bool twin = a.emitters != null && a.emitters.Length > 1;
                what = (twin ? "twin lasers" : "a laser") +
                       (a.aim == BossBeamAim.AtShip ? " locked on you, then sweeping," : " sweeping the lanes");
                break;
        }
        if (a.kind != BossAttackKind.Beam && a.rail == BossRailMode.Bounce)
            what += a.bounces == 1 ? " that ricochet once" : " that ricochet " + a.bounces + " times";
        return what;
    }

    // Body-part words for the boss art's emitters (BossEmitterTable names,
    // the L / R / UL ... sides folded together). A switch rather than a
    // static table, so no test sandbox can snapshot and clear it.
    static string PartWordFor(string key)
    {
        switch (key)
        {
            case "Chin": return "its chin cannon";
            case "Core": return "its reactor core";
            case "Pod": return "its engine pods";
            case "Jaw": return "its jaw";
            case "Eye": return "its eyes";
            case "Crown": return "its blowhole crown";
            case "Stinger": return "its stinger";
            case "Petal": return "every petal tip";
            case "Cannon": return "its flank cannons";
            case "Furnace": return "its chest furnace";
            case "Brow": return "the gem on its brow";
            default: return null;
        }
    }

    // "PetalUL" -> "every petal tip"; null for a part with no words yet.
    public static string PartWord(string emitter)
    {
        string key = PartKey(emitter);
        return key != null ? PartWordFor(key) : null;
    }

    static string PartKey(string emitter)
    {
        if (string.IsNullOrEmpty(emitter)) return null;
        foreach (string side in new[] { "UL", "UR", "LL", "LR", "L", "R" })
            if (emitter.Length > side.Length + 2 && emitter.EndsWith(side, StringComparison.Ordinal) &&
                char.IsLower(emitter[emitter.Length - side.Length - 1]))
                return emitter.Substring(0, emitter.Length - side.Length);
        return emitter;
    }

    static string PartsOf(BossAttack a)
    {
        var words = new List<string>();
        if (a.emitters != null)
            foreach (string em in a.emitters)
            {
                string w = PartWord(em) ?? em.ToLowerInvariant();
                if (!words.Contains(w)) words.Add(w);
            }
        return words.Count > 0 ? string.Join(" and ", words) : "its body";
    }

    // An elite's spec lines: its role, hearts and what downing it pays
    // (EliteDef.Score / Dust: ScoreRules' defaults unless the def says).
    public static string EliteLore(EliteDef e)
    {
        string role = string.IsNullOrEmpty(e.role) ? "SHIP" : e.role.ToUpperInvariant();
        return e.lore + "\n\n" +
               "ELITE  " + role + " - " + e.hearts + " HEARTS" + (e.armored ? ", ARMOURED" : "") + "\n" +
               "DOWNED  +" + e.Score + " POINTS, +" + Mathf.RoundToInt(e.Dust) + " STAR DUST";
    }

    public static string LoadoutLore(int ship)
    {
        var l = ShipLoadoutTable.For(ship);
        // Spec lines, not prose: no sentence stops, so the lore's own
        // three-sentence limit still holds.
        return "ATTACK  " + l.attackName + " - " + l.attackLine.TrimEnd('.') + "\n" +
               "SECRET POWER  " + l.powerName + " - " + l.powerLine.TrimEnd('.') +
               " (fires on " + ShipLoadoutTable.TriggerLine(l) + ")";
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
        // The elite ships (EliteCatalog), after the roster's enemies.
        foreach (var elite in EliteCatalog.All)
        {
            var e = elite;
            enemies.Add(new CodexEntry(e.codexId, e.displayName, CodexCategory.Enemies,
                                       () => EliteArt.Frame(e, e.cells.Flight0), EliteLore(e), new[] { Codex.Normalise(e.key) }));
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

    // "HOARFROST LEVIATHAN" -> "Hoarfrost Leviathan"; "THE BLOOM QUEEN" -> "The Bloom Queen".
    public static string BossDisplayName(string upper)
    {
        var words = upper.ToLowerInvariant().Split(' ');
        for (int i = 0; i < words.Length; i++)
            if (words[i].Length > 0) words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
        return string.Join(" ", words);
    }

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

    // A pickup's first idle drawing (PickupArt), else its prefab's sprite.
    static Sprite Pickup(PickupKind kind, string prefabPath)
    {
        var frames = PickupArt.Frames(PickupArt.IdleName(kind), PickupArt.IdleTicks(kind).Length);
        if (frames != null && frames.Length > 0 && frames[0] != null) return frames[0];
        return Prefab(prefabPath);
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
        // Worlds with variant ground sets (Frost) keep their sky in Backdrop3/v1..vN: show variant 1.
        return SquareOf(Resources.Load<Texture2D>(BackdropCatalog.TileFolder(folder, 1) + "sky"));
    }

    // Space shows the same sky tile the game's Space world and the menu
    // backdrop draw (Codex's sky_01 of sky_01..04, SpaceSkySelection).
    public static Texture2D SpaceSkyTexture()
    {
        return Resources.Load<Texture2D>(BackdropCatalog.Folder("Space") + SpaceSkySelection.TextureFor(1));
    }

    static Sprite SpaceBackdrop()
    {
        return SquareOf(SpaceSkyTexture());
    }

    static Sprite SquareOf(Texture2D tex)
    {
        if (tex == null) return null;
        float side = Mathf.Min(tex.width, tex.height);
        var rect = new Rect((tex.width - side) * .5f, (tex.height - side) * .5f, side, side);
        return Sprite.Create(tex, rect, new Vector2(.5f, .5f), 100f);
    }
}
