using System.Collections.Generic;
using UnityEngine;

// One backdrop the codex BACKDROPS tab can list and open in the viewer: a
// world's ground / sky variant (Frost's frozen ocean, Storm's eye of the
// storm, Space's nebula skies ...).
public sealed class CodexBackdrop
{
    public readonly string id;          // "bd_frost_2"
    public readonly string world;       // WorldManager display name ("Frost")
    public readonly int worldIndex;     // index in WorldManager.Worlds
    public readonly int variant;        // 1..N (the BackdropCatalog variant set / Space sky); 0 = the world has none
    public readonly string title;       // shown once seen ("Coast and Harbour")

    public CodexBackdrop(string id, string world, int worldIndex, int variant, string title)
    {
        this.id = id; this.world = world; this.worldIndex = worldIndex; this.variant = variant; this.title = title;
    }

    // The tile layer the cards and the locked/unlocked art show: the ground, or Space's sky.
    public string ThumbResource
    {
        get
        {
            string folder = BackdropCatalog.TileFolder(world, variant);
            if (world == "Space") return BackdropCatalog.Folder("Space") + SpaceSkySelection.TextureFor(variant);
            return folder + "mid";
        }
    }
}

// The codex's backdrop list and its unlock rule.
//
// A backdrop unlocks the first time it is SHOWN IN PLAY: WorldBackdrop.Show
// builds a world's set (a run starting there, a planetfall, a portal) and the
// variant that set picked (Space: the run's sky) is marked seen. It is stored as one more
// id ("bd_frost_2") in the codex's own PrefsKey set, exactly like the MUSIC
// tab's tracks, so it persists, batches and cloud-syncs the same way. It is
// NOT a CodexEntry: backdrops never touch Total, DiscoveredCount, NEW dots or
// Completionist. The menus' backdrop is not play and unlocks nothing; the
// tutorial is practice and unlocks nothing; developer mode shows every
// backdrop without writing. A world the loop cannot reach yet (WorldManager.
// LiveWorldCount) is not listed at all (developer mode lists it).
public static class CodexBackdrops
{
    public const string Prefix = "bd_";
    public const string LockedTitle = "???";

    // Names of each world's variants, index-aligned with WorldManager.Worlds and then variant 1..4
    // (from the art: BackdropCatalog / each world's director docs).
    static readonly string[][] Titles =
    {
        new[] { "Violet Drift", "Magenta Veil", "Blue Reach", "Nebula Eye" },                       // Space (sky_01..04)
        new[] { "Frozen Ocean", "Coast and Harbour", "Tundra Industry", "Glacier Night" },          // Frost
        new[] { "Canopy Sea", "Swamp Delta", "Ruined City", "Night Forest" },                       // Verdant
        new[] { "Caldera Fields", "Forge City", "Ash Ridges", "Cracked Crust" },                    // Ember
        new[] { "Open Sea", "Rig Field", "Drowned City", "Night Currents" },                        // Tide
        new[] { "Banded Deck", "Thunderhead Field", "Eye of the Storm", "Night Side" },             // Storm
        new[] { "Day Dunes", "Canyon and Ruins", "Salt Flat", "Night Dunes" },                      // Desert
    };

    static List<CodexBackdrop> all;
    static readonly Dictionary<string, bool> installed = new Dictionary<string, bool>();

    public static List<CodexBackdrop> All
    {
        get { if (all == null) all = Build(); return all; }
    }

    public static string IdFor(string world, int variant)
    {
        return Prefix + world.ToLowerInvariant() + "_" + variant;
    }

    static List<CodexBackdrop> Build()
    {
        var list = new List<CodexBackdrop>();
        var worlds = WorldManager.Worlds;
        for (int i = 0; i < worlds.Length; i++)
        {
            string name = worlds[i].displayName;
            BackdropCatalog.Spec spec = null;
            foreach (var s in BackdropCatalog.All) if (s.world == name) spec = s;
            if (spec == null) continue;
            int n = spec.variantSets > 0 ? spec.variantSets : name == "Space" ? SpaceSkySelection.VariantCount : 0;
            if (n == 0)
            {
                list.Add(new CodexBackdrop(IdFor(name, 0), name, i, 0, name));
                continue;
            }
            for (int v = 1; v <= n; v++)
            {
                string[] titles = i < Titles.Length ? Titles[i] : null;
                string title = titles != null && v - 1 < titles.Length ? titles[v - 1] : name + " " + v;
                list.Add(new CodexBackdrop(IdFor(name, v), name, i, v, title));
            }
        }
        return list;
    }

    public static CodexBackdrop Find(string id)
    {
        foreach (var b in All) if (b.id == id) return b;
        return null;
    }

    public static CodexBackdrop Find(string world, int variant)
    {
        foreach (var b in All) if (b.world == world && b.variant == variant) return b;
        return null;
    }

    // Is the backdrop's art on disk (a variant folder dropped in later is picked up the same way the
    // game does: its sky resolves)?
    public static bool ArtInstalled(CodexBackdrop b)
    {
        bool ok;
        if (installed.TryGetValue(b.id, out ok)) return ok;
        string sky = b.world == "Space"
            ? BackdropCatalog.Folder("Space") + SpaceSkySelection.TextureFor(b.variant)
            : BackdropCatalog.TileFolder(b.world, b.variant) + "sky";
        ok = Resources.Load<Sprite>(sky) != null;
        installed[b.id] = ok;
        return ok;
    }

    // Every backdrop whose art is installed is listed (every world is live).
    public static bool IsListed(CodexBackdrop b)
    {
        if (b == null) return false;
        return ArtInstalled(b);
    }

    public static bool IsUnlocked(CodexBackdrop b)
    {
        return b != null && Codex.IsBackdropSeen(b.id);
    }

    public static string DisplayTitle(CodexBackdrop b) { return IsUnlocked(b) ? b.title : LockedTitle; }

    public static int ListedCount
    {
        get { int n = 0; foreach (var b in All) if (IsListed(b)) n++; return n; }
    }

    public static int UnlockedCount
    {
        get { int n = 0; foreach (var b in All) if (IsListed(b) && IsUnlocked(b)) n++; return n; }
    }

    // The listed AND unlocked backdrops in world order: what the viewer steps through.
    public static List<CodexBackdrop> UnlockedList()
    {
        var list = new List<CodexBackdrop>();
        foreach (var b in All) if (IsListed(b) && IsUnlocked(b)) list.Add(b);
        return list;
    }

    // ---- gameplay hook (WorldBackdrop.Show) ----

    // A world's set was just built for play with this variant (Space: its sky variant 1..4).
    // Returns true only the first time.
    public static bool SeenInPlay(string world, int variant)
    {
        var b = Find(world, variant);
        return b != null && Codex.SeeBackdrop(b.id);
    }
}
