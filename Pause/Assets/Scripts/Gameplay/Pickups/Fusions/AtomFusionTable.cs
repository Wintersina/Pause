using UnityEngine;

// ATOMIC FUSIONS: what can fuse with what. Catch two atoms close together
// (the first one still primed on the ship: AtomFusionState) and, if the pair
// is a row here, it FUSES: a named effect (AtomFusions runs the row's
// handler), a big callout, and the fusion is recorded in the codex's COMBOS
// page (FusionDiscoveryStore). Order never matters: a recipe is an unordered
// set of atoms (Recipe). Adding a fusion later = one row here + its handler
// in AtomFusions.Apply (and its numbers in FusionTuning).
//
// The ship-flavoured "atoms" (Mending, Shell) are not pickups: they are the
// moments a ship's secret power goes off (Dove's Mending, Turtle's Hard
// Shell), primed like an atom so a pickup can fuse with them.
public enum AtomKind { Shield = 0, Heal = 1, Pause = 2, Capacitor = 3, Magnet = 4, Mending = 5, Shell = 6 }

public enum FusionId
{
    Vacuum = 0, Lifeline = 1, Bloom = 2, Nova = 3, Overclock = 4, Stasis = 5, DustStorm = 6,
    Singularity = 7, FeatherStorm = 8, ShellBurst = 9, Overheal = 10, Aftershock = 11,
}

// How long a fusion stays "on" (its chain window, and its effect's life).
public enum FusionWindow
{
    Fixed,         // FusionDef.seconds
    WhileShield,   // while the blue-atom shield is up (at most FusionTuning.ShieldBoundCap)
    WhileMagnet,   // while the magnet pulls (at most FusionTuning.MagnetBoundCap)
}

// A condition on the catch that completes the recipe (passed in as bits).
public enum FusionGate { None = 0, HealAtFullHearts = 1 }

public sealed class FusionDef
{
    public FusionId id;
    public string key;           // the save's id (FusionDiscoveryStore): never rename
    public string name;          // "BLOOM"
    public AtomKind[] atoms;     // the recipe, in display order
    public string recipe;        // "SHIELD + HEAL"
    public string hint;          // the locked codex row's clue
    public string description;   // one line: what it does
    public FusionWindow window;
    public float seconds;        // Fixed: the window; otherwise unused
    public bool external;        // decided and announced elsewhere (VACUUM: MagnetPull / MagnetCombo), only recorded here
    public FusionGate gate;
    public Color glow;

    public int Key => AtomFusionTable.Recipe(atoms);
    public bool Triple => atoms.Length == 3;
    public bool SameAtom => atoms.Length == 2 && atoms[0] == atoms[1];

    public bool Uses(AtomKind k)
    {
        for (int i = 0; i < atoms.Length; i++) if (atoms[i] == k) return true;
        return false;
    }
}

public static class AtomFusionTable
{
    public const int KindCount = 7;
    public static int Bit(AtomKind k) { return 1 << (int)k; }

    // An unordered recipe as one int: the atoms' bits plus how many there are
    // (so a same-atom pair, shield + shield, differs from the lone shield).
    public static int Recipe(AtomKind a, AtomKind b) { return Bit(a) | Bit(b) | (2 << 8); }
    public static int Recipe(AtomKind a, AtomKind b, AtomKind c) { return Bit(a) | Bit(b) | Bit(c) | (3 << 8); }
    public static int Recipe(AtomKind[] atoms)
    {
        int m = 0;
        for (int i = 0; i < atoms.Length; i++) m |= Bit(atoms[i]);
        return m | (atoms.Length << 8);
    }

    // The colour an atom is shown in (motes, codex glyphs).
    public static Color ColourOf(AtomKind k)
    {
        switch (k)
        {
            case AtomKind.Shield: return AkiraPalette.Cyan;
            case AtomKind.Heal: return PickupGlow.HealGreen;
            case AtomKind.Pause: return AkiraPalette.RedHi;
            case AtomKind.Capacitor: return AkiraPalette.VioletHi;
            case AtomKind.Magnet: return AkiraPalette.MagnetBlueHi;
            case AtomKind.Mending: return AkiraPalette.Bone;
            default: return AkiraPalette.Amber;   // Shell
        }
    }

    public static string WordOf(AtomKind k)
    {
        switch (k)
        {
            case AtomKind.Shield: return "SHIELD";
            case AtomKind.Heal: return "HEAL";
            case AtomKind.Pause: return "PAUSE";
            case AtomKind.Capacitor: return "CAPACITOR";
            case AtomKind.Magnet: return "MAGNET";
            case AtomKind.Mending: return "MENDING";
            default: return "HARD SHELL";
        }
    }

    // Instant atoms leave a mote for FusionTuning.MoteSeconds; timed ones are
    // primed while their own effect runs.
    public static bool IsTimed(AtomKind k) { return k == AtomKind.Shield || k == AtomKind.Magnet || k == AtomKind.Shell; }

    // Spawned by the atom spawners (not a ship power).
    public static bool IsPickup(AtomKind k) { return k <= AtomKind.Magnet; }

    static FusionDef Row(FusionId id, string key, string name, AtomKind[] atoms, string hint, string description,
                         FusionWindow window, float seconds, Color glow, bool external = false, FusionGate gate = FusionGate.None)
    {
        string recipe = WordOf(atoms[0]);
        for (int i = 1; i < atoms.Length; i++) recipe += " + " + WordOf(atoms[i]);
        return new FusionDef
        {
            id = id, key = key, name = name, atoms = atoms, recipe = recipe, hint = hint, description = description,
            window = window, seconds = seconds, glow = glow, external = external, gate = gate,
        };
    }

    // THE TABLE. Index = (int)FusionId.
    public static readonly FusionDef[] All =
    {
        Row(FusionId.Vacuum, "vacuum", "VACUUM", new[] { AtomKind.Magnet, AtomKind.Shield },
            "Two forces: one pulls, one protects.",
            "The magnet sucks the enemies and rocks on screen into your shield.",
            FusionWindow.Fixed, MagnetCombo.SyncSeconds, MagnetCombo.CalloutColour, external: true),
        Row(FusionId.Lifeline, "lifeline", "LIFELINE", new[] { AtomKind.Magnet, AtomKind.Heal },
            "A pull that reaches for repairs.",
            "The magnet reaches the whole screen and past its top edge.",
            FusionWindow.Fixed, FusionTuning.LifelineSeconds, Color.Lerp(PickupGlow.HealGreen, AkiraPalette.MagnetBlueHi, .5f)),
        Row(FusionId.Bloom, "bloom", "BLOOM", new[] { AtomKind.Shield, AtomKind.Heal },
            "Protection that grows back.",
            "Kills on your shield bank toward a heart: every " + FusionTuning.BloomKillsPerHeart + " restore one.",
            FusionWindow.WhileShield, 0f, PickupGlow.HealGreen),
        Row(FusionId.Nova, "nova", "NOVA", new[] { AtomKind.Shield, AtomKind.Capacitor },
            "A shield with a charge stored inside.",
            "When the shield lets go, its shockwave wipes out the enemy shots, ships, rocks and mines around you.",
            FusionWindow.WhileShield, 0f, AkiraPalette.Cyan),
        Row(FusionId.Overclock, "overclock", "OVERCLOCK", new[] { AtomKind.Pause, AtomKind.Capacitor },
            "Time and power, together.",
            "Your weapon fires at once and your pauses refill to " + FusionTuning.OverclockPausesTo + ".",
            FusionWindow.Fixed, FusionTuning.ChainWindowSeconds, AkiraPalette.Amber),
        Row(FusionId.Stasis, "stasis", "STASIS", new[] { AtomKind.Pause, AtomKind.Shield },
            "A frozen moment behind a wall.",
            "While shielded, up to " + FusionTuning.StasisFreePauses + " pauses cost nothing.",
            FusionWindow.WhileShield, 0f, AkiraPalette.RedHi),
        Row(FusionId.DustStorm, "duststorm", "DUST STORM", new[] { AtomKind.Capacitor, AtomKind.Magnet },
            "Stored lightning meets polarity.",
            "While the magnet pulls, star dust charges your powers much faster.",
            FusionWindow.WhileMagnet, 0f, AkiraPalette.VioletHi),
        Row(FusionId.Singularity, "singularity", "SINGULARITY", new[] { AtomKind.Magnet, AtomKind.Shield, AtomKind.Heal },
            "Three atoms at once. Rare.",
            "The vacuum heals: a heart back for the enemies and rocks it crushes (capped).",
            FusionWindow.Fixed, FusionTuning.SingularitySeconds, AkiraPalette.Bone),
        Row(FusionId.FeatherStorm, "featherstorm", "FEATHER STORM", new[] { AtomKind.Mending, AtomKind.Heal },
            "A dove's gift, doubled.",
            "A burst of feathers clears the shots around you and shields you for a moment.",
            FusionWindow.Fixed, FusionTuning.ChainWindowSeconds, AkiraPalette.Bone),
        Row(FusionId.ShellBurst, "shellburst", "SHELL BURST", new[] { AtomKind.Shell, AtomKind.Shield },
            "A turtle's armour under strain.",
            "The hard shell cracks into a shockwave that clears the shots around you.",
            FusionWindow.Fixed, FusionTuning.ChainWindowSeconds, AkiraPalette.Amber),
        Row(FusionId.Overheal, "overheal", "OVERHEAL", new[] { AtomKind.Heal, AtomKind.Heal },
            "More repairs than a hull can hold.",
            "A second heal at full hearts turns into star dust.",
            FusionWindow.Fixed, FusionTuning.ChainWindowSeconds, PickupGlow.HealGreen, gate: FusionGate.HealAtFullHearts),
        Row(FusionId.Aftershock, "aftershock", "AFTERSHOCK", new[] { AtomKind.Shield, AtomKind.Shield },
            "Shield upon shield.",
            "The shield's parting shockwave reaches further.",
            FusionWindow.WhileShield, 0f, AkiraPalette.Cyan),
    };

    public static int Count => All.Length;

    public static FusionDef Get(FusionId id) { return All[(int)id]; }

    // The row for a recipe (null: no fusion). No allocation.
    public static FusionDef Find(int recipe)
    {
        for (int i = 0; i < All.Length; i++) if (All[i].Key == recipe) return All[i];
        return null;
    }

    public static FusionDef FindKey(string key)
    {
        for (int i = 0; i < All.Length; i++) if (All[i].key == key) return All[i];
        return null;
    }
}
