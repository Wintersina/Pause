using System.Collections.Generic;

// The star dust a Codex find pays when the player collects it. ONE table, here, so the numbers are easy to
// tune and to audit. Every payout is between Min (5) and Max (25 = super rare); normal entries scale 5..15 by
// rarity / tier, and only the SuperRare ids pay Max. The Log entries pay 5 too.
//
//   kind                              pays
//   --------------------------------  -----------------------------------------------------------
//   Atoms: star dust, bright star     5
//   Atoms: blue, red                  8
//   Atoms: green, violet              10
//   Atoms: magnetic                   12
//   Hazards: rocks                    5
//   Hazards: mines                    5 + world / 2   (Space 5 ... Desert 8)
//   Enemies (roster)                  5 + world index (Space 5 ... Desert 11)
//   Elites                            12
//   Bosses                            15
//   Worlds: Deep Space                5
//   Worlds: the other worlds          10
//   Worlds: the wormhole portal       8
//   Ships                             10
//   Drift asteroid (Space rock)       5
//   Log: Pilot's Log, Wormhole Gift,
//        Last Stand (first replay kept) 5
//   Combos (atomic fusions): pairs    8
//   Combos: triples (Singularity)     15
//   Music: a world / credits theme    5
//   Music: a boss track               8
//   Backdrops (one per world variant) 5
//   SUPER RARE (25): the last world's boss, and the Paragon ship.
//
// Nothing pays by itself: a find is a pending reward and Codex.Collect* pays it (per item, per tab, COLLECT ALL).
public static class CodexPayout
{
    public const int Min = 5, Max = 25;

    public const int Atom = 5, ShieldRedAtom = 8, RepairCapacitorAtom = 10, MagnetAtom = 12;
    public const int Rock = 5, MineBase = 5, EnemyBase = 5;
    public const int Elite = 12, Boss = 15;
    public const int SpaceWorld = 5, OtherWorld = 10, Portal = 8;
    public const int Ship = 10, SuperRare = 25;
    public const int LogEntry = 5, FusionPair = 8, FusionTriple = 15, Track = 5, BossTrack = 8, Backdrop = 5;

    // Paid-list ids of the things that are not CodexEntry rows (they share Codex.PaidKey, so one guard covers all).
    public const string FusionPrefix = "fusion_";
    public static string FusionPayId(FusionDef f) { return FusionPrefix + f.key; }

    // Star dust for a non-entry find: a combo ("fusion_bloom"), a music track ("music_..."), a backdrop ("bd_...").
    // 0 for anything else (unknown / future ids, the always-open codex theme).
    public static int ForExtra(string id)
    {
        if (string.IsNullOrEmpty(id)) return 0;
        if (id.StartsWith(FusionPrefix, System.StringComparison.Ordinal))
        {
            var f = AtomFusionTable.FindKey(id.Substring(FusionPrefix.Length));
            return f == null ? 0 : f.Triple ? FusionTriple : FusionPair;
        }
        if (id.StartsWith("music_", System.StringComparison.Ordinal))
        {
            if (id == CodexMusic.ThemeId || CodexMusic.Find(id) == null) return 0;
            return id.StartsWith("music_boss_", System.StringComparison.Ordinal) ? BossTrack : Track;
        }
        if (id.StartsWith(CodexBackdrops.Prefix, System.StringComparison.Ordinal)) return CodexBackdrops.Find(id) == null ? 0 : Backdrop;
        return 0;
    }

    // The few entries that pay SuperRare. The last boss is the one of the highest world.
    public static IEnumerable<string> SuperRareIds
    {
        get
        {
            BossDef last = null;
            foreach (var b in BossCatalog.All) if (last == null || b.WorldIndex > last.WorldIndex) last = b;
            if (last != null) yield return last.id;
            yield return CodexCatalogue.ShipPrefix + "Paragon";
        }
    }

    public static bool IsSuperRare(CodexEntry entry)
    {
        if (entry == null) return false;
        foreach (string id in SuperRareIds) if (id == entry.id) return true;
        return false;
    }

    // Star dust for the entry (0 for null).
    public static int For(CodexEntry entry)
    {
        if (entry == null) return 0;
        if (entry.category == CodexCategory.Log) return LogEntry;
        if (IsSuperRare(entry)) return SuperRare;
        string id = entry.id;
        switch (entry.category)
        {
            case CodexCategory.Atoms:
                if (id == "atom_blue" || id == "atom_red") return ShieldRedAtom;
                if (id == "atom_green" || id == CodexCatalogue.VioletAtomId) return RepairCapacitorAtom;
                if (id == CodexCatalogue.MagnetAtomId) return MagnetAtom;
                return Atom;
            case CodexCategory.Worlds:
                if (id == CodexCatalogue.PortalId) return Portal;
                return id == CodexCatalogue.WorldIds[0] ? SpaceWorld : OtherWorld;
            case CodexCategory.Ships:
                return Ship;
            case CodexCategory.Hazards:
            {
                var def = EnemyRoster.FindByCodexId(id);
                return def != null && def.role == EnemyRole.Mine ? MineBase + def.world / 2 : Rock;
            }
            case CodexCategory.Enemies:
            {
                if (BossCatalog.Find(id) != null) return Boss;
                if (EliteCatalog.FindByCodexId(id) != null) return Elite;
                var def = EnemyRoster.FindByCodexId(id);
                return EnemyBase + (def != null ? def.world : 0);
            }
        }
        return Min;
    }

    public static int For(string id) { return For(Codex.Find(id)); }
}
